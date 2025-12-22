using Unity.Netcode;
using UnityEngine;

public sealed class PlayerInteractModule
{
    private readonly PlayerHub _hub;

    public PlayerInteractModule(PlayerHub hub)
    {
        _hub = hub;
    }

    public void TryPickupRaycast(Camera cam, float distance, LayerMask mask)
    {
        if (cam == null) return;

        Ray ray = new Ray(cam.transform.position, cam.transform.forward);
        if (!Physics.Raycast(ray, out var hit, distance, mask, QueryTriggerInteraction.Collide))
            return;

        var pickup = hit.collider.GetComponentInParent<ItemPickupNetwork>();
        if (pickup == null) return;

        var netObj = pickup.GetComponent<NetworkObject>();
        if (netObj == null) return;

        _hub.RequestPickup(netObj.NetworkObjectId);
    }
}
