using Unity.Netcode;
using UnityEngine;

public enum CoinItemDepositRecipientFallback
{
    None = 0,
    CurrentOwner = 1,
    ServerClient = 2,
    NearestPlayer = 3
}

[DisallowMultipleComponent]
[RequireComponent(typeof(Collider))]
public sealed class CoinItemDepositZone : MonoBehaviour
{
    [SerializeField, Tooltip("CoinItem이 zone에 들어온 순간 deposit을 시도합니다.")]
    private bool depositOnTriggerEnter = true;

    [SerializeField, Tooltip("CoinItem이 zone 안에 머무는 동안 deposit을 재시도합니다.")]
    private bool depositOnTriggerStay = true;

    [SerializeField, Tooltip("마지막 carrier wallet을 찾지 못했을 때 사용할 fallback입니다. 기본 None은 오지급을 피합니다.")]
    private CoinItemDepositRecipientFallback recipientFallback = CoinItemDepositRecipientFallback.None;

    [SerializeField, Tooltip("NearestPlayer fallback에서 deposit 대상 player를 찾을 최대 거리입니다.")]
    private float nearestPlayerRadius = 4f;

    [SerializeField, Tooltip("deposit zone 동작 로그를 출력합니다.")]
    private bool debugCoinItemDepositLogs = false;

    private Collider _zoneCollider;

    private void Awake()
    {
        ResolveRefs();
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!depositOnTriggerEnter)
            return;

        TryDeposit(other, "enter");
    }

    private void OnTriggerStay(Collider other)
    {
        if (!depositOnTriggerStay)
            return;

        TryDeposit(other, "stay");
    }

    public bool TryResolveRecipientWallet(
        CoinItemToken token,
        out PlayerCoinWalletModule wallet,
        out string reason)
    {
        wallet = null;
        reason = string.Empty;

        if (token == null)
        {
            reason = "token-null";
            return false;
        }

        if (token.TryGetLastCarrierClientId(out ulong carrierClientId) &&
            CoinItemToken.TryGetWalletForClient(carrierClientId, out wallet))
        {
            reason = $"last-carrier:{carrierClientId}";
            return true;
        }

        switch (recipientFallback)
        {
            case CoinItemDepositRecipientFallback.CurrentOwner:
                return TryResolveCurrentOwnerWallet(token, out wallet, out reason);
            case CoinItemDepositRecipientFallback.ServerClient:
                return TryResolveServerClientWallet(out wallet, out reason);
            case CoinItemDepositRecipientFallback.NearestPlayer:
                return TryResolveNearestPlayerWallet(token.transform.position, out wallet, out reason);
            default:
                reason = token.HasLastCarrier ? "last-carrier-wallet-not-found" : "last-carrier-missing";
                return false;
        }
    }

    private void ResolveRefs()
    {
        if (_zoneCollider == null)
            _zoneCollider = GetComponent<Collider>();
    }

    private bool TryDeposit(Collider other, string reason)
    {
        if (!IsServer())
            return false;

        CoinItemToken token = FindCoinItemToken(other);
        if (token == null)
            return false;

        bool deposited = token.ServerTryDepositFromZone(this, out int addedAmount);
        Log($"tryDeposit reason={reason} token={token.name} deposited={deposited} added={addedAmount}");
        return deposited;
    }

    private static bool IsServer()
    {
        NetworkManager networkManager = NetworkManager.Singleton;
        return networkManager != null && networkManager.IsServer;
    }

    private static CoinItemToken FindCoinItemToken(Collider other)
    {
        if (other == null)
            return null;

        CoinItemToken token = other.GetComponentInParent<CoinItemToken>();
        if (token != null)
            return token;

        Rigidbody attachedRigidbody = other.attachedRigidbody;
        if (attachedRigidbody != null)
        {
            token = attachedRigidbody.GetComponent<CoinItemToken>();
            if (token != null)
                return token;
        }

        return null;
    }

    private static bool TryResolveCurrentOwnerWallet(
        CoinItemToken token,
        out PlayerCoinWalletModule wallet,
        out string reason)
    {
        wallet = null;

        if (token == null || !token.TryGetCurrentOwnerClientId(out ulong ownerClientId))
        {
            reason = "owner-missing";
            return false;
        }

        if (!CoinItemToken.TryGetWalletForClient(ownerClientId, out wallet))
        {
            reason = $"owner-wallet-not-found:{ownerClientId}";
            return false;
        }

        reason = $"owner:{ownerClientId}";
        return true;
    }

    private static bool TryResolveServerClientWallet(out PlayerCoinWalletModule wallet, out string reason)
    {
        ulong serverClientId = NetworkManager.ServerClientId;
        if (!CoinItemToken.TryGetWalletForClient(serverClientId, out wallet))
        {
            reason = "server-client-wallet-not-found";
            return false;
        }

        reason = $"server-client:{serverClientId}";
        return true;
    }

    private bool TryResolveNearestPlayerWallet(
        Vector3 position,
        out PlayerCoinWalletModule wallet,
        out string reason)
    {
        wallet = null;
        reason = "nearest-player-not-found";

        PlayerHub[] players = FindObjectsByType<PlayerHub>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        float maxDistance = Mathf.Max(0f, nearestPlayerRadius);
        float bestDistanceSqr = maxDistance > 0f ? maxDistance * maxDistance : float.PositiveInfinity;
        PlayerCoinWalletModule bestWallet = null;

        for (int i = 0; i < players.Length; i++)
        {
            PlayerHub player = players[i];
            if (player == null)
                continue;

            PlayerCoinWalletModule candidateWallet = player.CoinWalletModule != null
                ? player.CoinWalletModule
                : player.GetComponentInChildren<PlayerCoinWalletModule>(true);

            if (candidateWallet == null)
                continue;

            float distanceSqr = (player.transform.position - position).sqrMagnitude;
            if (distanceSqr > bestDistanceSqr)
                continue;

            bestDistanceSqr = distanceSqr;
            bestWallet = candidateWallet;
        }

        if (bestWallet == null)
            return false;

        wallet = bestWallet;
        reason = $"nearest-player:{Mathf.Sqrt(bestDistanceSqr):0.###}";
        return true;
    }

    private void Log(string message)
    {
        if (!debugCoinItemDepositLogs)
            return;

        Debug.Log($"[CoinItemDepositZone] {name} {message}", this);
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        nearestPlayerRadius = Mathf.Max(0f, nearestPlayerRadius);
        ResolveRefs();
    }
#endif
}
