using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public class BouncePadGimmick : MonoBehaviour
{
    [SerializeField, Tooltip("플레이어를 위로 튕길 수직 속도입니다.")]
    private float bounceUpVelocity = 9f;

    [SerializeField, Tooltip("기존 상승 속도를 무시하고 바운스 속도로 덮어쓸지 여부입니다.")]
    private bool overrideExistingUpwardVelocity = true;

    [SerializeField, Tooltip("같은 플레이어가 다시 바운스되기까지의 대기 시간입니다.")]
    private float bounceCooldown = 0.75f;

    [SerializeField, Tooltip("서버에서만 바운스 처리를 수행할지 여부입니다.")]
    private bool requireServer = true;

    [SerializeField, Tooltip("다운 상태의 플레이어는 바운스시키지 않을지 여부입니다.")]
    private bool ignoreKnockedPlayers = true;

    [SerializeField, Tooltip("기상 중인 플레이어는 바운스시키지 않을지 여부입니다.")]
    private bool ignoreStandingUpPlayers = true;

    [SerializeField, Tooltip("탈락 상태의 플레이어는 바운스시키지 않을지 여부입니다.")]
    private bool ignoreEliminatedPlayers = true;

    [SerializeField, Tooltip("이동 가능한 상태의 플레이어만 바운스시킬지 여부입니다.")]
    private bool requireCanMove = true;

    [SerializeField, Tooltip("바운스 감지에 사용할 Trigger Collider입니다. 비워두면 같은 오브젝트나 자식에서 자동 탐색합니다.")]
    private Collider triggerCollider;

    [SerializeField, Tooltip("바운스 처리 디버그 로그를 출력할지 여부입니다.")]
    private bool enableDebugLogs = false;

    private readonly Dictionary<ulong, float> _nextBounceTimes = new Dictionary<ulong, float>();

    private void Awake()
    {
        if (triggerCollider == null)
            triggerCollider = GetComponent<Collider>();

        if (triggerCollider == null)
            triggerCollider = GetComponentInChildren<Collider>(true);

        if (triggerCollider != null)
            triggerCollider.isTrigger = true;
    }

    private void OnTriggerEnter(Collider other)
    {
        TryBounceFromCollider(other);
    }

    private void OnTriggerStay(Collider other)
    {
        TryBounceFromCollider(other);
    }

    private void TryBounceFromCollider(Collider other)
    {
        if (!TryResolvePlayer(other, out PlayerHub playerHub))
        {
            LogBounce("Bounce skipped. reason=PlayerHub not found.");
            return;
        }

        TryBounce(playerHub);
    }

    public void ClearCooldowns()
    {
        _nextBounceTimes.Clear();
    }

    public void ClearCooldownFor(PlayerHub playerHub)
    {
        if (playerHub == null)
            return;

        _nextBounceTimes.Remove(GetPlayerCooldownKey(playerHub));
    }

    public bool TryBounce(PlayerHub playerHub)
    {
        if (!CanBouncePlayer(playerHub))
            return false;

        PlayerLocomotionModule locomotionModule = ResolveLocomotionModule(playerHub);
        if (locomotionModule == null)
        {
            LogBounce($"Bounce skipped. reason=LocomotionModule not found. player:{playerHub.name}");
            return false;
        }

        if (!locomotionModule.ServerTryApplyBounce(bounceUpVelocity, overrideExistingUpwardVelocity))
        {
            LogBounce($"Bounce skipped. reason=ServerTryApplyBounce failed. player:{playerHub.name}");
            return false;
        }

        SetPlayerCooldown(playerHub);
        LogBounce($"Bounce applied. player:{playerHub.name}, velocity:{bounceUpVelocity:0.###}");
        return true;
    }

    private bool CanProcessOnThisInstance()
    {
        if (!requireServer)
            return true;

        NetworkManager networkManager = NetworkManager.Singleton;
        return networkManager != null && networkManager.IsServer;
    }

    private bool TryResolvePlayer(Collider other, out PlayerHub playerHub)
    {
        playerHub = null;
        if (other == null)
            return false;

        playerHub = other.GetComponentInParent<PlayerHub>();
        return playerHub != null;
    }

    private bool CanBouncePlayer(PlayerHub playerHub)
    {
        if (!CanProcessOnThisInstance())
        {
            LogBounce("Bounce skipped. reason=not server.");
            return false;
        }

        if (playerHub == null)
        {
            LogBounce("Bounce skipped. reason=PlayerHub is null.");
            return false;
        }

        PlayerStatusModule statusModule = ResolveStatusModule(playerHub);
        if (statusModule != null)
        {
            if (ignoreEliminatedPlayers && statusModule.IsEliminated)
            {
                LogBounce($"Bounce skipped. reason=status eliminated. player:{playerHub.name}");
                return false;
            }

            if (ignoreKnockedPlayers && statusModule.IsKnocked)
            {
                LogBounce($"Bounce skipped. reason=status knocked. player:{playerHub.name}");
                return false;
            }

            if (ignoreStandingUpPlayers && statusModule.IsStandingUp)
            {
                LogBounce($"Bounce skipped. reason=status standing up. player:{playerHub.name}");
                return false;
            }

            if (requireCanMove && !statusModule.CanMove)
            {
                LogBounce($"Bounce skipped. reason=status cannot move. player:{playerHub.name}");
                return false;
            }
        }

        if (IsPlayerOnCooldown(playerHub))
        {
            LogBounce($"Bounce skipped. reason=cooldown. player:{playerHub.name}");
            return false;
        }

        return true;
    }

    private ulong GetPlayerCooldownKey(PlayerHub playerHub)
    {
        if (playerHub == null)
            return 0UL;

        NetworkObject networkObject = playerHub.NetworkObject;
        if (networkObject != null && networkObject.IsSpawned)
            return networkObject.NetworkObjectId;

        return unchecked((ulong)playerHub.GetInstanceID());
    }

    private bool IsPlayerOnCooldown(PlayerHub playerHub)
    {
        ulong key = GetPlayerCooldownKey(playerHub);
        return _nextBounceTimes.TryGetValue(key, out float nextBounceTime) && Time.time < nextBounceTime;
    }

    private void SetPlayerCooldown(PlayerHub playerHub)
    {
        ulong key = GetPlayerCooldownKey(playerHub);
        _nextBounceTimes[key] = Time.time + Mathf.Max(0f, bounceCooldown);
    }

    private PlayerLocomotionModule ResolveLocomotionModule(PlayerHub playerHub)
    {
        return playerHub != null ? playerHub.GetComponentInChildren<PlayerLocomotionModule>(true) : null;
    }

    private PlayerStatusModule ResolveStatusModule(PlayerHub playerHub)
    {
        return playerHub != null ? playerHub.GetComponentInChildren<PlayerStatusModule>(true) : null;
    }

    private void LogBounce(string message)
    {
        if (!enableDebugLogs)
            return;

        Debug.Log($"[BouncePadGimmick] {message}", this);
    }

    private void OnValidate()
    {
        bounceUpVelocity = Mathf.Max(0f, bounceUpVelocity);
        bounceCooldown = Mathf.Max(0f, bounceCooldown);

        if (triggerCollider != null)
            triggerCollider.isTrigger = true;
    }
}
