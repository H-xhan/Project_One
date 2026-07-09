using System;

[Serializable]
public enum PostItType
{
    Drawing = 0,
    Message = 1,
    Bonus = 2,
    Penalty = 3
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
public struct PostItRuntimeData
{
    public int PostItId;
    public PostItType Type;
    public PostItTopicId TopicId;
    public int VisualId;
    public ulong OriginalOwnerClientId;
    public ulong HolderClientId;
    public int SlotIndex;

    public bool IsValid => PostItId >= 0;

    public static PostItRuntimeData Invalid => new PostItRuntimeData(
        -1,
        PostItType.Drawing,
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
}
