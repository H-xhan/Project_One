using Unity.Netcode;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(NetworkObject))]
public sealed class CoinItemToken : NetworkBehaviour
{
    [SerializeField, Tooltip("Deposit 성공 시 지급할 코인 수입니다.")]
    private int coinValue = 1;

    [SerializeField, Tooltip("Deposit 성공 시 spawned NetworkObject를 despawn합니다. 비활성화 fallback은 항상 유지됩니다.")]
    private bool despawnWhenDeposited = true;

    [SerializeField, Tooltip("Host-only fallback 용도입니다. 기본값 false에서는 ServerClientId를 carrier로 기록하지 않습니다.")]
    private bool allowServerClientIdAsCarrier = false;

    [SerializeField, Tooltip("CoinItem carrier/deposit 경로 로그를 출력합니다.")]
    private bool debugCoinItemLogs = false;

    private NetworkObject _networkObject;
    private bool _isDeposited;
    private bool _hasObservedOwner;
    private ulong _lastObservedOwnerClientId;
    private bool _hasLastCarrier;
    private ulong _lastCarrierClientId;

    public int CoinValue => coinValue;
    public bool IsDeposited => _isDeposited;
    public bool HasLastCarrier => _hasLastCarrier;
    public ulong LastCarrierClientId => _lastCarrierClientId;
    public NetworkObject TokenNetworkObject => ResolveNetworkObject();

    private void Awake()
    {
        ResolveNetworkObject();
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        ResolveNetworkObject();

        if (IsServer)
            RefreshOwnerObservation(true);
    }

    private void Update()
    {
        if (!IsServer || _isDeposited)
            return;

        RefreshOwnerObservation(false);
    }

    public bool TryGetLastCarrierClientId(out ulong clientId)
    {
        clientId = _lastCarrierClientId;
        return _hasLastCarrier;
    }

    public bool TryGetCurrentOwnerClientId(out ulong clientId)
    {
        NetworkObject netObj = ResolveNetworkObject();
        if (netObj == null)
        {
            clientId = default;
            return false;
        }

        clientId = netObj.OwnerClientId;
        return true;
    }

    public bool ServerTryDepositFromZone(CoinItemDepositZone zone, out int addedAmount)
    {
        addedAmount = 0;

        if (!CanDepositServer(out string blockedReason))
        {
            Log($"deposit blocked reason={blockedReason}");
            return false;
        }

        if (zone == null)
        {
            Log("deposit blocked reason=zone-null");
            return false;
        }

        if (!zone.TryResolveRecipientWallet(this, out PlayerCoinWalletModule wallet, out string recipientReason))
        {
            Log($"deposit blocked reason=wallet-not-found detail={recipientReason}");
            return false;
        }

        return ServerTryDepositToWallet(wallet, out addedAmount, $"zone={zone.name}");
    }

    public bool ServerTryDepositToWallet(PlayerCoinWalletModule wallet, out int addedAmount, string reason = null)
    {
        addedAmount = 0;

        if (!CanDepositServer(out string blockedReason))
        {
            Log($"deposit blocked reason={blockedReason}");
            return false;
        }

        if (wallet == null)
        {
            Log("deposit blocked reason=wallet-null");
            return false;
        }

        if (!wallet.ServerTryAddCoins(coinValue, out addedAmount) || addedAmount <= 0)
        {
            Log($"deposit blocked reason=wallet-add-failed value={coinValue} added={addedAmount}");
            return false;
        }

        _isDeposited = true;
        Log($"deposit success reason={reason ?? "<none>"} value={coinValue} added={addedAmount}");
        CompleteDeposit();
        return true;
    }

    public static bool TryGetWalletForClient(ulong clientId, out PlayerCoinWalletModule wallet)
    {
        wallet = null;

        NetworkManager networkManager = NetworkManager.Singleton;
        if (networkManager == null ||
            networkManager.ConnectedClients == null ||
            !networkManager.ConnectedClients.TryGetValue(clientId, out NetworkClient client) ||
            client == null ||
            client.PlayerObject == null ||
            !client.PlayerObject.IsSpawned)
        {
            return false;
        }

        PlayerHub playerHub = client.PlayerObject.GetComponentInChildren<PlayerHub>(true);
        if (playerHub == null)
            return false;

        wallet = playerHub.CoinWalletModule != null
            ? playerHub.CoinWalletModule
            : playerHub.GetComponentInChildren<PlayerCoinWalletModule>(true);

        return wallet != null;
    }

    private NetworkObject ResolveNetworkObject()
    {
        if (_networkObject == null)
            _networkObject = GetComponent<NetworkObject>();

        return _networkObject;
    }

    private void RefreshOwnerObservation(bool force)
    {
        NetworkObject netObj = ResolveNetworkObject();
        if (netObj == null)
            return;

        ulong ownerClientId = netObj.OwnerClientId;
        bool firstObservation = !_hasObservedOwner;
        if (!force && !firstObservation && ownerClientId == _lastObservedOwnerClientId)
            return;

        _hasObservedOwner = true;
        _lastObservedOwnerClientId = ownerClientId;

        if (firstObservation && ownerClientId == NetworkManager.ServerClientId && !allowServerClientIdAsCarrier)
        {
            Log("owner initial server owner ignored");
            return;
        }

        if (!CanUseClientAsCarrier(ownerClientId))
        {
            Log($"owner observed but not carrier client={ownerClientId}");
            return;
        }

        _hasLastCarrier = true;
        _lastCarrierClientId = ownerClientId;
        Log($"carrier observed client={ownerClientId}");
    }

    private bool CanUseClientAsCarrier(ulong clientId)
    {
        if (clientId == NetworkManager.ServerClientId && !allowServerClientIdAsCarrier)
            return false;

        NetworkManager networkManager = NetworkManager.Singleton;
        return networkManager != null &&
            networkManager.ConnectedClients != null &&
            networkManager.ConnectedClients.ContainsKey(clientId);
    }

    private bool CanDepositServer(out string blockedReason)
    {
        blockedReason = null;

        if (!IsServer)
        {
            blockedReason = "not-server";
            return false;
        }

        if (_isDeposited)
        {
            blockedReason = "already-deposited";
            return false;
        }

        if (coinValue <= 0)
        {
            blockedReason = "coinValue<=0";
            return false;
        }

        return true;
    }

    private void CompleteDeposit()
    {
        NetworkObject netObj = ResolveNetworkObject();
        if (despawnWhenDeposited && netObj != null && netObj.IsSpawned)
        {
            netObj.Despawn(true);
            return;
        }

        gameObject.SetActive(false);
    }

    private void Log(string message)
    {
        if (!debugCoinItemLogs)
            return;

        Debug.Log($"[CoinItemToken] {name} {message}", this);
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        coinValue = Mathf.Max(1, coinValue);
        ResolveNetworkObject();
    }
#endif
}
