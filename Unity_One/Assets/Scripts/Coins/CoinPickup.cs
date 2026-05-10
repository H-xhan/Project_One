using Unity.Netcode;
using UnityEngine;

[RequireComponent(typeof(NetworkObject))]
public class CoinPickup : NetworkBehaviour
{
    [SerializeField, Tooltip("이 코인을 획득했을 때 추가되는 코인 수입니다.")]
    private int coinValue = 1;

    [SerializeField, Tooltip("플레이어 획득 감지에 사용할 Trigger Collider입니다. 비워두면 자식에서 자동 탐색합니다.")]
    private Collider pickupCollider;

    [SerializeField, Tooltip("중복 획득을 막기 위해 획득 성공 시 Collider를 즉시 비활성화할지 여부입니다.")]
    private bool disableColliderOnPickup = true;

    private bool _isCollected;

    public int CoinValue => coinValue;
    public bool IsCollected => _isCollected;

    private void Awake()
    {
        ResolveRefs();
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        ResolveRefs();
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!IsServer) return;
        if (_isCollected) return;
        if (coinValue <= 0) return;

        PlayerHub playerHub = other.GetComponentInParent<PlayerHub>();
        if (playerHub == null) return;

        PlayerCoinWalletModule wallet = playerHub.CoinWalletModule;
        if (wallet == null) return;

        if (!wallet.ServerTryAddCoins(coinValue, out int addedAmount)) return;
        if (addedAmount <= 0) return;

        CompletePickup();
    }

    public void ServerSetCoinValue(int value)
    {
        if (!IsServer) return;

        coinValue = Mathf.Max(1, value);
    }

    private void ResolveRefs()
    {
        if (pickupCollider == null)
            pickupCollider = GetComponentInChildren<Collider>(true);
    }

    private void CompletePickup()
    {
        _isCollected = true;

        if (disableColliderOnPickup && pickupCollider != null)
            pickupCollider.enabled = false;

        if (NetworkObject != null && NetworkObject.IsSpawned)
        {
            NetworkObject.Despawn(true);
            return;
        }

        Destroy(gameObject);
    }
}
