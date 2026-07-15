using System;
using Unity.Netcode;

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
