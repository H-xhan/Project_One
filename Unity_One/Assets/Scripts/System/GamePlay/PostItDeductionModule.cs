using System;
using System.Collections.Generic;
using Unity.Collections;

public sealed class PostItDeductionModule
{
    public const int LiarCorrectAnswerScore = 2;
    public const int CitizenCorrectVoteScore = 1;

    private readonly bool[] _voteSubmitted =
        new bool[PostItLiarFixedSet.Capacity];
    private readonly byte[] _voteTargets =
        new byte[PostItLiarFixedSet.Capacity];

    private byte _liarSlot = PostItLiarFixedSet.InvalidSlot;
    private int _correctChoiceSlot = -1;
    private int _choiceCount;
    private bool _active;
    private bool _acceptingSubmissions;
    private bool _liarAnswerSubmitted;
    private int _liarSelectedChoiceSlot = -1;
    private bool _finalized;
    private int _finalizedRoundRevision = -1;
    private PostItLiarRevealData _cachedResult;

    public bool HasLiarAnswerSubmission => _liarAnswerSubmitted;

    public bool BeginRound(
        byte liarSlot,
        int correctChoiceSlot,
        int choiceCount)
    {
        Reset();
        if (liarSlot >= PostItLiarFixedSet.Capacity ||
            choiceCount != PostItPromptDatabaseSO.RequiredChoiceCount ||
            correctChoiceSlot < 0 ||
            correctChoiceSlot >= choiceCount)
        {
            return false;
        }

        _liarSlot = liarSlot;
        _correctChoiceSlot = correctChoiceSlot;
        _choiceCount = choiceCount;
        _active = true;
        _acceptingSubmissions = true;
        return true;
    }

    public void Reset()
    {
        Array.Clear(_voteSubmitted, 0, _voteSubmitted.Length);
        for (int index = 0; index < _voteTargets.Length; index++)
            _voteTargets[index] = PostItLiarFixedSet.InvalidSlot;

        _liarSlot = PostItLiarFixedSet.InvalidSlot;
        _correctChoiceSlot = -1;
        _choiceCount = 0;
        _active = false;
        _acceptingSubmissions = false;
        _liarAnswerSubmitted = false;
        _liarSelectedChoiceSlot = -1;
        _finalized = false;
        _finalizedRoundRevision = -1;
        _cachedResult = default;
    }

    public PostItLiarSubmitResult SubmitLiarChoice(
        byte requesterSlot,
        int choiceSlot)
    {
        if (!_active)
            return PostItLiarSubmitResult.NotActive;

        if (requesterSlot != _liarSlot)
            return PostItLiarSubmitResult.WrongRole;

        if (_liarAnswerSubmitted)
            return PostItLiarSubmitResult.Duplicate;

        if (!_acceptingSubmissions)
            return PostItLiarSubmitResult.Late;

        if (choiceSlot < 0 || choiceSlot >= _choiceCount)
            return PostItLiarSubmitResult.InvalidChoice;

        _liarSelectedChoiceSlot = choiceSlot;
        _liarAnswerSubmitted = true;
        return PostItLiarSubmitResult.Accepted;
    }

    public PostItLiarSubmitResult SubmitCitizenVote(
        byte voterSlot,
        byte targetSlot)
    {
        if (!_active)
            return PostItLiarSubmitResult.NotActive;

        if (voterSlot >= PostItLiarFixedSet.Capacity)
            return PostItLiarSubmitResult.NotParticipant;

        if (voterSlot == _liarSlot)
            return PostItLiarSubmitResult.WrongRole;

        if (_voteSubmitted[voterSlot])
            return PostItLiarSubmitResult.Duplicate;

        if (!_acceptingSubmissions)
            return PostItLiarSubmitResult.Late;

        if (targetSlot >= PostItLiarFixedSet.Capacity ||
            targetSlot == voterSlot)
        {
            return PostItLiarSubmitResult.InvalidChoice;
        }

        _voteTargets[voterSlot] = targetSlot;
        _voteSubmitted[voterSlot] = true;
        return PostItLiarSubmitResult.Accepted;
    }

    public bool HasCitizenVote(byte stableSlot)
    {
        return stableSlot < PostItLiarFixedSet.Capacity &&
               stableSlot != _liarSlot &&
               _voteSubmitted[stableSlot];
    }

    public bool AreAllConnectedCitizensResolved(
        IReadOnlyList<PostItLiarRosterEntry> roster)
    {
        if (!_active ||
            roster == null ||
            !IsValidParticipantCount(roster.Count) ||
            _liarSlot >= roster.Count)
        {
            return false;
        }

        for (int index = 0; index < roster.Count; index++)
        {
            PostItLiarRosterEntry entry = roster[index];
            if (entry.StableSlot >= PostItLiarFixedSet.Capacity)
                return false;

            if (entry.IsConnected &&
                entry.StableSlot != _liarSlot &&
                !_voteSubmitted[entry.StableSlot])
            {
                return false;
            }
        }

        return true;
    }

    public void FinalizeMissing()
    {
        _acceptingSubmissions = false;
    }

    public bool TryFinalize(
        int roundRevision,
        IReadOnlyList<PostItLiarRosterEntry> roster,
        IReadOnlyDictionary<ulong, int> battleScores,
        PostItLiarClueSet authoredClues,
        FixedString128Bytes secretAnswer,
        PostItLiarChoiceSet shuffledChoices,
        bool deductionCancelled,
        out PostItLiarRevealData result,
        out string error)
    {
        result = default;
        error = null;

        int participantCount = roster != null ? roster.Count : 0;

        if (_finalized)
        {
            if (roundRevision != _finalizedRoundRevision)
            {
                error = "이미 다른 revision의 deduction 결과가 확정되었습니다.";
                return false;
            }

            result = _cachedResult;
            return true;
        }

        if (!_active ||
            roundRevision < 0 ||
            !IsValidParticipantCount(participantCount) ||
            _liarSlot >= participantCount ||
            battleScores == null ||
            battleScores.Count != participantCount ||
            authoredClues.Count != participantCount ||
            shuffledChoices.Count != _choiceCount ||
            secretAnswer.IsEmpty)
        {
            error = "Deduction finalize 입력이 유효하지 않습니다.";
            return false;
        }

        HashSet<ulong> clientIds = new HashSet<ulong>();
        HashSet<byte> stableSlots = new HashSet<byte>();
        PostItLiarPlayerResultSet playerResults = default;
        for (int index = 0; index < roster.Count; index++)
        {
            PostItLiarRosterEntry entry = roster[index];
            if (entry.StableSlot != index ||
                !clientIds.Add(entry.ClientId) ||
                !stableSlots.Add(entry.StableSlot) ||
                !battleScores.TryGetValue(
                    entry.ClientId,
                    out int battleScore) ||
                battleScore < 0)
            {
                error = "Deduction roster 또는 BattleScore가 유효하지 않습니다.";
                return false;
            }

            if (_voteSubmitted[entry.StableSlot] &&
                (_voteTargets[entry.StableSlot] >= participantCount ||
                 _voteTargets[entry.StableSlot] == entry.StableSlot))
            {
                error = "Deduction vote target이 frozen roster에 없습니다.";
                return false;
            }

            int deductionScore = 0;
            if (!deductionCancelled)
            {
                if (entry.StableSlot == _liarSlot)
                {
                    if (_liarAnswerSubmitted &&
                        _liarSelectedChoiceSlot == _correctChoiceSlot)
                    {
                        deductionScore = LiarCorrectAnswerScore;
                    }
                }
                else if (_voteSubmitted[entry.StableSlot] &&
                         _voteTargets[entry.StableSlot] == _liarSlot)
                {
                    deductionScore = CitizenCorrectVoteScore;
                }
            }

            if (!playerResults.TrySet(
                    entry.StableSlot,
                    new PostItLiarPlayerResultData(
                        entry.StableSlot,
                        battleScore,
                        deductionScore,
                        entry.IsConnected)))
            {
                error = "Deduction score slot을 기록할 수 없습니다.";
                return false;
            }
        }

        PostItLiarVoteSet votes = default;
        for (byte slot = 0; slot < participantCount; slot++)
        {
            bool isCitizen = slot != _liarSlot;
            bool wasSubmitted = isCitizen && _voteSubmitted[slot];
            byte targetSlot = wasSubmitted
                ? _voteTargets[slot]
                : PostItLiarFixedSet.InvalidSlot;
            votes.TrySet(
                slot,
                new PostItLiarVoteData(
                    slot,
                    targetSlot,
                    wasSubmitted,
                    wasSubmitted && targetSlot == _liarSlot));
        }

        bool liarAnswerCorrect =
            !deductionCancelled &&
            _liarAnswerSubmitted &&
            _liarSelectedChoiceSlot == _correctChoiceSlot;
        FixedString128Bytes liarSelectedAnswer =
            _liarAnswerSubmitted &&
            _liarSelectedChoiceSlot >= 0 &&
            _liarSelectedChoiceSlot < shuffledChoices.Count
                ? shuffledChoices.Get(_liarSelectedChoiceSlot)
                : default;

        result = new PostItLiarRevealData
        {
            IsValid = true,
            DeductionCancelled = deductionCancelled,
            RoundRevision = roundRevision,
            LiarSlot = _liarSlot,
            LiarAnswerSubmitted = _liarAnswerSubmitted,
            LiarAnswerCorrect = liarAnswerCorrect,
            SecretAnswer = secretAnswer,
            LiarSelectedAnswer = liarSelectedAnswer,
            AuthoredClues = authoredClues,
            Votes = votes,
            PlayerResults = playerResults
        };

        _acceptingSubmissions = false;
        _finalized = true;
        _finalizedRoundRevision = roundRevision;
        _cachedResult = result;
        return true;
    }

    private static bool IsValidParticipantCount(int participantCount)
    {
        return participantCount == 2 ||
               participantCount == PostItLiarFixedSet.Capacity;
    }
}
