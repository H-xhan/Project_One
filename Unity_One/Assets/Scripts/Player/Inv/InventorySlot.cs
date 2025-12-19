using System;
using Unity.Netcode;

[Serializable]
public struct InventorySlot : INetworkSerializable, IEquatable<InventorySlot>
{
    public int itemId;
    public int amount;

    public InventorySlot(int itemId, int amount)
    {
        this.itemId = itemId;
        this.amount = amount;
    }

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref itemId);
        serializer.SerializeValue(ref amount);
    }

    public bool Equals(InventorySlot other)
    {
        return itemId == other.itemId && amount == other.amount;
    }
}
