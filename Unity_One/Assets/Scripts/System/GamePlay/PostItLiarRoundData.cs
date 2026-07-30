using System;
using Unity.Collections;
using Unity.Netcode;

[Serializable]
public enum PostItLiarPhase : byte
{
    None = 0,
    SecretReveal = 1,
    ClueWrite = 2,
    ClueLock = 3,
    BrawlCountdown = 4,
    Brawl = 5,
    LiarGuess = 6,
    LiarVote = 7,
    Reveal = 8,
    Complete = 9
}

[Serializable]
public enum PostItLiarRole : byte
{
    None = 0,
    Citizen = 1,
    Liar = 2
}

[Serializable]
public enum PostItLiarSubmitResult : byte
{
    None = 0,
    Accepted = 1,
    NotActive = 2,
    InvalidPhase = 3,
    NotParticipant = 4,
    PlayerObjectMismatch = 5,
    WrongRole = 6,
    InvalidChoice = 7,
    Empty = 8,
    TooLong = 9,
    ContainsAnswer = 10,
    ContainsForbidden = 11,
    Duplicate = 12,
    Late = 13,
    Stale = 14,
    InvalidText = 15
}

[Serializable]
public enum PostItLiarSubmissionKind : byte
{
    None = 0,
    Clue = 1,
    LiarAnswer = 2,
    CitizenVote = 3
}

[Serializable]
public struct PostItLiarPhaseState :
    INetworkSerializable,
    IEquatable<PostItLiarPhaseState>
{
    public bool IsActive;
    public int RoundRevision;
    public int PhaseRevision;
    public PostItLiarPhase Phase;
    public double DeadlineServerTime;
    public FixedString128Bytes PublicCategory;

    public PostItLiarPhaseState(
        bool isActive,
        int roundRevision,
        int phaseRevision,
        PostItLiarPhase phase,
        double deadlineServerTime,
        FixedString128Bytes publicCategory)
    {
        IsActive = isActive;
        RoundRevision = roundRevision;
        PhaseRevision = phaseRevision;
        Phase = phase;
        DeadlineServerTime = deadlineServerTime;
        PublicCategory = publicCategory;
    }

    public static PostItLiarPhaseState Inactive => new PostItLiarPhaseState(
        false,
        -1,
        -1,
        PostItLiarPhase.None,
        0d,
        default);

    public void NetworkSerialize<T>(BufferSerializer<T> serializer)
        where T : IReaderWriter
    {
        serializer.SerializeValue(ref IsActive);
        serializer.SerializeValue(ref RoundRevision);
        serializer.SerializeValue(ref PhaseRevision);
        serializer.SerializeValue(ref Phase);
        serializer.SerializeValue(ref DeadlineServerTime);
        serializer.SerializeValue(ref PublicCategory);
    }

    public bool Equals(PostItLiarPhaseState other)
    {
        return IsActive == other.IsActive &&
               RoundRevision == other.RoundRevision &&
               PhaseRevision == other.PhaseRevision &&
               Phase == other.Phase &&
               DeadlineServerTime.Equals(other.DeadlineServerTime) &&
               PublicCategory.Equals(other.PublicCategory);
    }

    public override bool Equals(object obj)
    {
        return obj is PostItLiarPhaseState other && Equals(other);
    }

    public override int GetHashCode()
    {
        unchecked
        {
            int hash = 17;
            hash = hash * 31 + IsActive.GetHashCode();
            hash = hash * 31 + RoundRevision;
            hash = hash * 31 + PhaseRevision;
            hash = hash * 31 + (int)Phase;
            hash = hash * 31 + DeadlineServerTime.GetHashCode();
            hash = hash * 31 + PublicCategory.GetHashCode();
            return hash;
        }
    }
}

[Serializable]
public struct PostItLiarRosterEntry
{
    public ulong ClientId;
    public ulong PlayerNetworkObjectId;
    public byte StableSlot;
    public bool IsConnected;

    public PostItLiarRosterEntry(
        ulong clientId,
        ulong playerNetworkObjectId,
        byte stableSlot,
        bool isConnected)
    {
        ClientId = clientId;
        PlayerNetworkObjectId = playerNetworkObjectId;
        StableSlot = stableSlot;
        IsConnected = isConnected;
    }
}

[Serializable]
public struct PostItLiarPrivateRoleData : INetworkSerializable
{
    public int RoundRevision;
    public int PhaseRevision;
    public byte StableSlot;
    public PostItLiarRole Role;
    public FixedString128Bytes SecretAnswer;

    public bool IsValid =>
        RoundRevision >= 0 &&
        PhaseRevision >= 0 &&
        StableSlot < PostItLiarFixedSet.Capacity &&
        Role != PostItLiarRole.None;

    public PostItLiarPrivateRoleData(
        int roundRevision,
        int phaseRevision,
        byte stableSlot,
        PostItLiarRole role,
        FixedString128Bytes secretAnswer)
    {
        RoundRevision = roundRevision;
        PhaseRevision = phaseRevision;
        StableSlot = stableSlot;
        Role = role;
        SecretAnswer = secretAnswer;
    }

    public void NetworkSerialize<T>(BufferSerializer<T> serializer)
        where T : IReaderWriter
    {
        serializer.SerializeValue(ref RoundRevision);
        serializer.SerializeValue(ref PhaseRevision);
        serializer.SerializeValue(ref StableSlot);
        serializer.SerializeValue(ref Role);
        serializer.SerializeValue(ref SecretAnswer);
    }
}

[Serializable]
public struct PostItLiarClueData : INetworkSerializable
{
    public byte AuthorSlot;
    public bool WasSubmitted;
    public FixedString512Bytes Clue;

    public PostItLiarClueData(
        byte authorSlot,
        bool wasSubmitted,
        FixedString512Bytes clue)
    {
        AuthorSlot = authorSlot;
        WasSubmitted = wasSubmitted;
        Clue = clue;
    }

    public void NetworkSerialize<T>(BufferSerializer<T> serializer)
        where T : IReaderWriter
    {
        serializer.SerializeValue(ref AuthorSlot);
        serializer.SerializeValue(ref WasSubmitted);
        serializer.SerializeValue(ref Clue);
    }
}

[Serializable]
public struct PostItLiarVoteData : INetworkSerializable
{
    public byte VoterSlot;
    public byte TargetSlot;
    public bool WasSubmitted;
    public bool IsCorrect;

    public PostItLiarVoteData(
        byte voterSlot,
        byte targetSlot,
        bool wasSubmitted,
        bool isCorrect)
    {
        VoterSlot = voterSlot;
        TargetSlot = targetSlot;
        WasSubmitted = wasSubmitted;
        IsCorrect = isCorrect;
    }

    public void NetworkSerialize<T>(BufferSerializer<T> serializer)
        where T : IReaderWriter
    {
        serializer.SerializeValue(ref VoterSlot);
        serializer.SerializeValue(ref TargetSlot);
        serializer.SerializeValue(ref WasSubmitted);
        serializer.SerializeValue(ref IsCorrect);
    }
}

[Serializable]
public struct PostItLiarPlayerResultData : INetworkSerializable
{
    public byte StableSlot;
    public int BattleScore;
    public int DeductionScore;
    public int FinalRoundScore;
    public bool IsConnected;

    public PostItLiarPlayerResultData(
        byte stableSlot,
        int battleScore,
        int deductionScore,
        bool isConnected)
    {
        StableSlot = stableSlot;
        BattleScore = battleScore;
        DeductionScore = deductionScore;
        FinalRoundScore = battleScore + deductionScore;
        IsConnected = isConnected;
    }

    public void NetworkSerialize<T>(BufferSerializer<T> serializer)
        where T : IReaderWriter
    {
        serializer.SerializeValue(ref StableSlot);
        serializer.SerializeValue(ref BattleScore);
        serializer.SerializeValue(ref DeductionScore);
        serializer.SerializeValue(ref FinalRoundScore);
        serializer.SerializeValue(ref IsConnected);
    }
}

[Serializable]
public struct PostItLiarClueSet : INetworkSerializable
{
    public byte Count;
    public PostItLiarClueData Item0;
    public PostItLiarClueData Item1;
    public PostItLiarClueData Item2;
    public PostItLiarClueData Item3;

    public PostItLiarClueData Get(int index)
    {
        switch (index)
        {
            case 0: return Item0;
            case 1: return Item1;
            case 2: return Item2;
            case 3: return Item3;
            default: return default;
        }
    }

    public bool TrySet(int index, PostItLiarClueData value)
    {
        switch (index)
        {
            case 0:
                Item0 = value;
                break;
            case 1:
                Item1 = value;
                break;
            case 2:
                Item2 = value;
                break;
            case 3:
                Item3 = value;
                break;
            default:
                return false;
        }

        if (Count <= index)
            Count = (byte)(index + 1);

        return true;
    }

    public void NetworkSerialize<T>(BufferSerializer<T> serializer)
        where T : IReaderWriter
    {
        serializer.SerializeValue(ref Count);
        serializer.SerializeValue(ref Item0);
        serializer.SerializeValue(ref Item1);
        serializer.SerializeValue(ref Item2);
        serializer.SerializeValue(ref Item3);
    }
}

[Serializable]
public struct PostItLiarChoiceSet : INetworkSerializable
{
    public byte Count;
    public FixedString128Bytes Choice0;
    public FixedString128Bytes Choice1;
    public FixedString128Bytes Choice2;
    public FixedString128Bytes Choice3;

    public FixedString128Bytes Get(int index)
    {
        switch (index)
        {
            case 0: return Choice0;
            case 1: return Choice1;
            case 2: return Choice2;
            case 3: return Choice3;
            default: return default;
        }
    }

    public bool TrySet(int index, FixedString128Bytes value)
    {
        switch (index)
        {
            case 0:
                Choice0 = value;
                break;
            case 1:
                Choice1 = value;
                break;
            case 2:
                Choice2 = value;
                break;
            case 3:
                Choice3 = value;
                break;
            default:
                return false;
        }

        if (Count <= index)
            Count = (byte)(index + 1);

        return true;
    }

    public void NetworkSerialize<T>(BufferSerializer<T> serializer)
        where T : IReaderWriter
    {
        serializer.SerializeValue(ref Count);
        serializer.SerializeValue(ref Choice0);
        serializer.SerializeValue(ref Choice1);
        serializer.SerializeValue(ref Choice2);
        serializer.SerializeValue(ref Choice3);
    }
}

[Serializable]
public struct PostItLiarVoteSet : INetworkSerializable
{
    public byte Count;
    public PostItLiarVoteData Item0;
    public PostItLiarVoteData Item1;
    public PostItLiarVoteData Item2;
    public PostItLiarVoteData Item3;

    public PostItLiarVoteData Get(int index)
    {
        switch (index)
        {
            case 0: return Item0;
            case 1: return Item1;
            case 2: return Item2;
            case 3: return Item3;
            default: return default;
        }
    }

    public bool TrySet(int index, PostItLiarVoteData value)
    {
        switch (index)
        {
            case 0:
                Item0 = value;
                break;
            case 1:
                Item1 = value;
                break;
            case 2:
                Item2 = value;
                break;
            case 3:
                Item3 = value;
                break;
            default:
                return false;
        }

        if (Count <= index)
            Count = (byte)(index + 1);

        return true;
    }

    public void NetworkSerialize<T>(BufferSerializer<T> serializer)
        where T : IReaderWriter
    {
        serializer.SerializeValue(ref Count);
        serializer.SerializeValue(ref Item0);
        serializer.SerializeValue(ref Item1);
        serializer.SerializeValue(ref Item2);
        serializer.SerializeValue(ref Item3);
    }
}

[Serializable]
public struct PostItLiarPlayerResultSet : INetworkSerializable
{
    public byte Count;
    public PostItLiarPlayerResultData Item0;
    public PostItLiarPlayerResultData Item1;
    public PostItLiarPlayerResultData Item2;
    public PostItLiarPlayerResultData Item3;

    public PostItLiarPlayerResultData Get(int index)
    {
        switch (index)
        {
            case 0: return Item0;
            case 1: return Item1;
            case 2: return Item2;
            case 3: return Item3;
            default: return default;
        }
    }

    public bool TrySet(int index, PostItLiarPlayerResultData value)
    {
        switch (index)
        {
            case 0:
                Item0 = value;
                break;
            case 1:
                Item1 = value;
                break;
            case 2:
                Item2 = value;
                break;
            case 3:
                Item3 = value;
                break;
            default:
                return false;
        }

        if (Count <= index)
            Count = (byte)(index + 1);

        return true;
    }

    public void NetworkSerialize<T>(BufferSerializer<T> serializer)
        where T : IReaderWriter
    {
        serializer.SerializeValue(ref Count);
        serializer.SerializeValue(ref Item0);
        serializer.SerializeValue(ref Item1);
        serializer.SerializeValue(ref Item2);
        serializer.SerializeValue(ref Item3);
    }
}

[Serializable]
public struct PostItLiarGuessViewData : INetworkSerializable
{
    public int RoundRevision;
    public int PhaseRevision;
    public PostItLiarClueSet AnonymousClues;
    public PostItLiarChoiceSet Choices;
    public PostItLiarPlayerResultSet BattleScores;

    public void NetworkSerialize<T>(BufferSerializer<T> serializer)
        where T : IReaderWriter
    {
        serializer.SerializeValue(ref RoundRevision);
        serializer.SerializeValue(ref PhaseRevision);
        serializer.SerializeValue(ref AnonymousClues);
        serializer.SerializeValue(ref Choices);
        serializer.SerializeValue(ref BattleScores);
    }
}

[Serializable]
public struct PostItLiarVoteViewData : INetworkSerializable
{
    public int RoundRevision;
    public int PhaseRevision;
    public PostItLiarClueSet AuthoredClues;
    public PostItLiarPlayerResultSet BattleScores;

    public void NetworkSerialize<T>(BufferSerializer<T> serializer)
        where T : IReaderWriter
    {
        serializer.SerializeValue(ref RoundRevision);
        serializer.SerializeValue(ref PhaseRevision);
        serializer.SerializeValue(ref AuthoredClues);
        serializer.SerializeValue(ref BattleScores);
    }
}

[Serializable]
public struct PostItLiarRevealData : INetworkSerializable
{
    public bool IsValid;
    public bool DeductionCancelled;
    public int RoundRevision;
    public byte LiarSlot;
    public bool LiarAnswerSubmitted;
    public bool LiarAnswerCorrect;
    public FixedString128Bytes SecretAnswer;
    public FixedString128Bytes LiarSelectedAnswer;
    public PostItLiarClueSet AuthoredClues;
    public PostItLiarVoteSet Votes;
    public PostItLiarPlayerResultSet PlayerResults;

    public void NetworkSerialize<T>(BufferSerializer<T> serializer)
        where T : IReaderWriter
    {
        serializer.SerializeValue(ref IsValid);
        serializer.SerializeValue(ref DeductionCancelled);
        serializer.SerializeValue(ref RoundRevision);
        serializer.SerializeValue(ref LiarSlot);
        serializer.SerializeValue(ref LiarAnswerSubmitted);
        serializer.SerializeValue(ref LiarAnswerCorrect);
        serializer.SerializeValue(ref SecretAnswer);
        serializer.SerializeValue(ref LiarSelectedAnswer);
        serializer.SerializeValue(ref AuthoredClues);
        serializer.SerializeValue(ref Votes);
        serializer.SerializeValue(ref PlayerResults);
    }
}

public static class PostItLiarFixedSet
{
    public const byte Capacity = 4;
    public const byte InvalidSlot = byte.MaxValue;
}
