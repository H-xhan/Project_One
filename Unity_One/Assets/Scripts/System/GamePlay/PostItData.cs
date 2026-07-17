using System;
using Unity.Netcode;
using UnityEngine;

[Serializable]
public enum PostItType
{
    None = 0,
    Drawing = 1,
    Message = 2,
    Bonus = 3,
    Penalty = 4
}

[Serializable]
public enum PostItTopicId
{
    None = 0,
    Animal = 1,
    Food = 2,
    Object = 3,
    Emotion = 4,
    Free = 5
}

[Serializable]
public struct PostItRuntimeData :
    INetworkSerializable,
    IEquatable<PostItRuntimeData>
{
    public int PostItId;
    public PostItType Type;
    public PostItTopicId TopicId;
    public int VisualId;
    public ulong OriginalOwnerClientId;
    public ulong HolderClientId;
    public int SlotIndex;

    public bool IsValid => PostItId >= 0 && Type != PostItType.None;

    public static PostItRuntimeData Invalid => new PostItRuntimeData(
        -1,
        PostItType.None,
        PostItTopicId.None,
        -1,
        ulong.MaxValue,
        ulong.MaxValue,
        -1);

    public PostItRuntimeData(
        int postItId,
        PostItType type,
        PostItTopicId topicId,
        int visualId,
        ulong originalOwnerClientId,
        ulong holderClientId,
        int slotIndex)
    {
        PostItId = postItId;
        Type = type;
        TopicId = topicId;
        VisualId = visualId;
        OriginalOwnerClientId = originalOwnerClientId;
        HolderClientId = holderClientId;
        SlotIndex = slotIndex;
    }

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref PostItId);
        serializer.SerializeValue(ref Type);
        serializer.SerializeValue(ref TopicId);
        serializer.SerializeValue(ref VisualId);
        serializer.SerializeValue(ref OriginalOwnerClientId);
        serializer.SerializeValue(ref HolderClientId);
        serializer.SerializeValue(ref SlotIndex);
    }

    public bool Equals(PostItRuntimeData other)
    {
        return PostItId == other.PostItId &&
               Type == other.Type &&
               TopicId == other.TopicId &&
               VisualId == other.VisualId &&
               OriginalOwnerClientId == other.OriginalOwnerClientId &&
               HolderClientId == other.HolderClientId &&
               SlotIndex == other.SlotIndex;
    }

    public override bool Equals(object obj)
    {
        return obj is PostItRuntimeData other && Equals(other);
    }

    public override int GetHashCode()
    {
        unchecked
        {
            int hash = 17;
            hash = hash * 31 + PostItId;
            hash = hash * 31 + (int)Type;
            hash = hash * 31 + (int)TopicId;
            hash = hash * 31 + VisualId;
            hash = hash * 31 + OriginalOwnerClientId.GetHashCode();
            hash = hash * 31 + HolderClientId.GetHashCode();
            hash = hash * 31 + SlotIndex;
            return hash;
        }
    }
}

[Serializable]
public struct PostItPublicVisualData :
    INetworkSerializable,
    IEquatable<PostItPublicVisualData>
{
    public int PostItId;
    public int SlotIndex;
    public PostItType Type;
    public int VisualId;
    public bool IsOriginalOwnerItem;

    public bool IsValid =>
        PostItId >= 0 &&
        SlotIndex >= 0 &&
        Type != PostItType.None;

    public static PostItPublicVisualData Invalid => new PostItPublicVisualData(
        -1,
        -1,
        PostItType.None,
        0,
        false);

    public PostItPublicVisualData(
        int postItId,
        int slotIndex,
        PostItType type,
        int visualId,
        bool isOriginalOwnerItem)
    {
        PostItId = postItId;
        SlotIndex = slotIndex;
        Type = type;
        VisualId = visualId;
        IsOriginalOwnerItem = isOriginalOwnerItem;
    }

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref PostItId);
        serializer.SerializeValue(ref SlotIndex);
        serializer.SerializeValue(ref Type);
        serializer.SerializeValue(ref VisualId);
        serializer.SerializeValue(ref IsOriginalOwnerItem);
    }

    public bool Equals(PostItPublicVisualData other)
    {
        return PostItId == other.PostItId &&
               SlotIndex == other.SlotIndex &&
               Type == other.Type &&
               VisualId == other.VisualId &&
               IsOriginalOwnerItem == other.IsOriginalOwnerItem;
    }

    public override bool Equals(object obj)
    {
        return obj is PostItPublicVisualData other && Equals(other);
    }

    public override int GetHashCode()
    {
        unchecked
        {
            int hash = 17;
            hash = hash * 31 + PostItId;
            hash = hash * 31 + SlotIndex;
            hash = hash * 31 + (int)Type;
            hash = hash * 31 + VisualId;
            hash = hash * 31 + IsOriginalOwnerItem.GetHashCode();
            return hash;
        }
    }
}

[Serializable]
public struct PostItWorldDropData :
    INetworkSerializable,
    IEquatable<PostItWorldDropData>
{
    public int PostItId;
    public PostItType Type;
    public int VisualId;
    public bool IsOriginalOwnerItem;
    public Vector3 Position;
    public Quaternion Rotation;

    public bool IsValid => PostItId >= 0 && Type != PostItType.None;

    public static PostItWorldDropData Invalid => new PostItWorldDropData(
        -1,
        PostItType.None,
        0,
        false,
        Vector3.zero,
        Quaternion.identity);

    public PostItWorldDropData(
        int postItId,
        PostItType type,
        int visualId,
        bool isOriginalOwnerItem,
        Vector3 position,
        Quaternion rotation)
    {
        PostItId = postItId;
        Type = type;
        VisualId = visualId;
        IsOriginalOwnerItem = isOriginalOwnerItem;
        Position = position;
        Rotation = rotation;
    }

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref PostItId);
        serializer.SerializeValue(ref Type);
        serializer.SerializeValue(ref VisualId);
        serializer.SerializeValue(ref IsOriginalOwnerItem);
        serializer.SerializeValue(ref Position);
        serializer.SerializeValue(ref Rotation);
    }

    public bool Equals(PostItWorldDropData other)
    {
        return PostItId == other.PostItId &&
               Type == other.Type &&
               VisualId == other.VisualId &&
               IsOriginalOwnerItem == other.IsOriginalOwnerItem &&
               Position.Equals(other.Position) &&
               Rotation.Equals(other.Rotation);
    }

    public override bool Equals(object obj)
    {
        return obj is PostItWorldDropData other && Equals(other);
    }

    public override int GetHashCode()
    {
        unchecked
        {
            int hash = 17;
            hash = hash * 31 + PostItId;
            hash = hash * 31 + (int)Type;
            hash = hash * 31 + VisualId;
            hash = hash * 31 + IsOriginalOwnerItem.GetHashCode();
            hash = hash * 31 + Position.GetHashCode();
            hash = hash * 31 + Rotation.GetHashCode();
            return hash;
        }
    }
}
