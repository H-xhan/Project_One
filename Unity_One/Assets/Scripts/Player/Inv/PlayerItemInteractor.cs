using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerItemInteractor : NetworkBehaviour
{
    [Header("Ray")]
    [Tooltip("레이캐스트 시작 카메라(없으면 Camera.main 사용)")]
    [SerializeField] private Camera targetCamera;

    [Tooltip("상호작용 거리")]
    [SerializeField] private float interactDistance = 3.0f;

    [Tooltip("픽업 레이어 마스크(설정 추천)")]
    [SerializeField] private LayerMask pickupMask;

    [Header("Refs")]
    [Tooltip("플레이어 인벤토리")]
    [SerializeField] private PlayerInventory inventory;

    private void Start()
    {
        if (!IsOwner) return;
        if (targetCamera == null) targetCamera = Camera.main;
    }

    private void Update()
    {
        if (!IsOwner) return;

        if (Mouse.current == null) return;
        if (!Mouse.current.rightButton.wasPressedThisFrame) return;

        TryPickup();
    }

    private void TryPickup()
    {
        if (targetCamera == null || inventory == null) return;

        Ray ray = new Ray(targetCamera.transform.position, targetCamera.transform.forward);
        if (!Physics.Raycast(ray, out var hit, interactDistance, pickupMask, QueryTriggerInteraction.Collide))
            return;

        var pickup = hit.collider.GetComponentInParent<ItemPickupNetwork>();
        if (pickup == null) return;

        var netObj = pickup.GetComponent<NetworkObject>();
        if (netObj == null) return;

        inventory.RequestPickupServerRpc(netObj.NetworkObjectId);
    }
}
