using UnityEngine;

public class PlayerInteractModule
{
    private readonly PlayerHub _hub;

    public PlayerInteractModule(PlayerHub hub)
    {
        _hub = hub;
    }

    public void TryPickupRaycast(Camera cam, float maxDistance, LayerMask mask)
    {
        if (cam == null) return;

        Ray ray = cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
        if (!Physics.Raycast(ray, out RaycastHit hit, maxDistance, mask, QueryTriggerInteraction.Ignore))
            return;

        ItemPickupNetwork pickup = hit.collider.GetComponentInParent<ItemPickupNetwork>();
        if (pickup == null) return;

        _hub.RequestPickup(pickup.NetworkObjectId);
    }
}
