using Unity.Netcode;
using UnityEngine;

public class PlayerInventory : NetworkBehaviour
{
    [Header("Data")]
    [Tooltip("아이템 DB (모든 클라이언트에 동일하게 존재해야 함)")]
    [SerializeField] private ItemDatabaseSO itemDatabase;

    [Header("Inventory")]
    [Tooltip("인벤토리 슬롯 수")]
    [SerializeField] private int capacity = 8;

    public NetworkList<InventorySlot> Slots { get; private set; }

    private void Awake()
    {
        Slots = new NetworkList<InventorySlot>();
    }

    public override void OnNetworkSpawn()
    {
        if (IsServer)
        {
            if (Slots.Count == 0)
            {
                for (int i = 0; i < capacity; i++)
                    Slots.Add(new InventorySlot(0, 0));
            }
        }
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
    public void RequestPickupServerRpc(ulong pickupNetworkObjectId)
    {
        if (!NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(pickupNetworkObjectId, out var netObj))
            return;

        var pickup = netObj.GetComponent<ItemPickupNetwork>();
        if (pickup == null)
            return;

        int itemId = pickup.ItemId;
        int amount = pickup.Amount;

        if (itemDatabase == null || itemDatabase.Get(itemId) == null)
        {
            Debug.LogWarning($"[Inventory] Unknown itemId: {itemId}");
            return;
        }

        if (!TryAddItemServer(itemId, amount))
            return;

        netObj.Despawn(true);
        NotifyPickedUpClientRpc(itemId, amount);
    }

    private bool TryAddItemServer(int itemId, int amount)
    {
        if (!IsServer) return false;

        var data = itemDatabase.Get(itemId);
        if (data == null) return false;

        int remaining = amount;

        if (data.stackable)
        {
            for (int i = 0; i < Slots.Count; i++)
            {
                var slot = Slots[i];
                if (slot.itemId != itemId) continue;

                int canAdd = Mathf.Max(0, data.maxStack - slot.amount);
                if (canAdd <= 0) continue;

                int add = Mathf.Min(canAdd, remaining);
                slot.amount += add;
                Slots[i] = slot;

                remaining -= add;
                if (remaining <= 0) return true;
            }
        }

        for (int i = 0; i < Slots.Count; i++)
        {
            var slot = Slots[i];
            if (slot.itemId != 0) continue;

            int add = data.stackable ? Mathf.Min(data.maxStack, remaining) : 1;
            Slots[i] = new InventorySlot(itemId, add);

            remaining -= add;
            if (remaining <= 0) return true;
        }

        Debug.Log($"[Inventory] Inventory full. itemId={itemId}, remaining={remaining}");
        return false;
    }

    [ClientRpc]
    private void NotifyPickedUpClientRpc(int itemId, int amount)
    {
        if (!IsOwner) return;
        Debug.Log($"[Inventory] Picked up itemId={itemId}, amount={amount}");
    }
}
