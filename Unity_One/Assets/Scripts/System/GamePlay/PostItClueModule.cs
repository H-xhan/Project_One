using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Unity.Collections;

public sealed class PostItClueModule
{
    public const int MaxTextElements = 24;
    public const int MaxUtf8Bytes = 128;

    private static readonly UTF8Encoding StrictUtf8 =
        new UTF8Encoding(false, true);

    private readonly string[] _clues =
        new string[PostItLiarFixedSet.Capacity];
    private readonly bool[] _submitted =
        new bool[PostItLiarFixedSet.Capacity];

    private byte _participantCount;
    private bool _acceptingSubmissions;

    public bool BeginRound(byte participantCount)
    {
        Reset();
        if (participantCount == 0 ||
            participantCount > PostItLiarFixedSet.Capacity)
        {
            return false;
        }

        _participantCount = participantCount;
        _acceptingSubmissions = true;
        return true;
    }

    public void Reset()
    {
        Array.Clear(_clues, 0, _clues.Length);
        Array.Clear(_submitted, 0, _submitted.Length);
        _participantCount = 0;
        _acceptingSubmissions = false;
    }

    public static PostItLiarSubmitResult ValidateAndNormalize(
        string raw,
        string secretAnswer,
        IReadOnlyList<string> forbiddenStrings,
        out string normalized)
    {
        normalized = string.Empty;
        if (!TryNormalizeSingleLine(raw, out normalized))
            return PostItLiarSubmitResult.InvalidText;

        if (normalized.Length == 0)
            return PostItLiarSubmitResult.Empty;

        try
        {
            if (StringInfo.ParseCombiningCharacters(normalized).Length >
                MaxTextElements)
            {
                normalized = string.Empty;
                return PostItLiarSubmitResult.TooLong;
            }

            if (StrictUtf8.GetByteCount(normalized) > MaxUtf8Bytes)
            {
                normalized = string.Empty;
                return PostItLiarSubmitResult.TooLong;
            }
        }
        catch (ArgumentException)
        {
            normalized = string.Empty;
            return PostItLiarSubmitResult.InvalidText;
        }

        if (!TryNormalizeSearchTerm(secretAnswer, out string normalizedAnswer))
        {
            normalized = string.Empty;
            return PostItLiarSubmitResult.InvalidText;
        }

        if (ContainsTerm(normalized, normalizedAnswer))
        {
            normalized = string.Empty;
            return PostItLiarSubmitResult.ContainsAnswer;
        }

        if (forbiddenStrings != null)
        {
            for (int index = 0; index < forbiddenStrings.Count; index++)
            {
                if (!TryNormalizeSearchTerm(
                        forbiddenStrings[index],
                        out string forbidden))
                {
                    normalized = string.Empty;
                    return PostItLiarSubmitResult.InvalidText;
                }

                if (ContainsTerm(normalized, forbidden))
                {
                    normalized = string.Empty;
                    return PostItLiarSubmitResult.ContainsForbidden;
                }
            }
        }

        return PostItLiarSubmitResult.Accepted;
    }

    public PostItLiarSubmitResult TrySubmit(
        byte stableSlot,
        string raw,
        string secretAnswer,
        IReadOnlyList<string> forbiddenStrings,
        out string normalized)
    {
        normalized = string.Empty;
        if (stableSlot >= _participantCount)
            return PostItLiarSubmitResult.NotParticipant;

        if (_submitted[stableSlot])
            return PostItLiarSubmitResult.Duplicate;

        if (!_acceptingSubmissions)
            return PostItLiarSubmitResult.Late;

        PostItLiarSubmitResult validationResult = ValidateAndNormalize(
            raw,
            secretAnswer,
            forbiddenStrings,
            out normalized);
        if (validationResult != PostItLiarSubmitResult.Accepted)
            return validationResult;

        _clues[stableSlot] = normalized;
        _submitted[stableSlot] = true;
        return PostItLiarSubmitResult.Accepted;
    }

    public bool HasSubmission(byte stableSlot)
    {
        return stableSlot < _participantCount && _submitted[stableSlot];
    }

    public bool TryGetClue(byte stableSlot, out string clue)
    {
        clue = string.Empty;
        if (!HasSubmission(stableSlot))
            return false;

        clue = _clues[stableSlot];
        return true;
    }

    public bool AreAllConnectedSubmitted(
        IReadOnlyList<PostItLiarRosterEntry> roster)
    {
        if (roster == null || roster.Count != _participantCount)
            return false;

        for (int index = 0; index < roster.Count; index++)
        {
            PostItLiarRosterEntry entry = roster[index];
            if (entry.StableSlot >= _participantCount)
                return false;

            if (entry.IsConnected && !_submitted[entry.StableSlot])
                return false;
        }

        return true;
    }

    public void FinalizeMissing()
    {
        _acceptingSubmissions = false;
    }

    public PostItLiarClueSet BuildAuthoredSet()
    {
        PostItLiarClueSet result = default;
        for (byte slot = 0; slot < _participantCount; slot++)
        {
            result.TrySet(
                slot,
                new PostItLiarClueData(
                    slot,
                    _submitted[slot],
                    _submitted[slot]
                        ? new FixedString512Bytes(_clues[slot])
                        : default));
        }

        return result;
    }

    public PostItLiarClueSet BuildAnonymousSet(Random random)
    {
        if (random == null)
            throw new ArgumentNullException(nameof(random));

        PostItLiarClueData[] shuffled =
            new PostItLiarClueData[_participantCount];
        for (byte slot = 0; slot < _participantCount; slot++)
        {
            shuffled[slot] = new PostItLiarClueData(
                PostItLiarFixedSet.InvalidSlot,
                _submitted[slot],
                _submitted[slot]
                    ? new FixedString512Bytes(_clues[slot])
                    : default);
        }

        for (int index = shuffled.Length - 1; index > 0; index--)
        {
            int swapIndex = random.Next(index + 1);
            (shuffled[index], shuffled[swapIndex]) =
                (shuffled[swapIndex], shuffled[index]);
        }

        PostItLiarClueSet result = default;
        for (int index = 0; index < shuffled.Length; index++)
            result.TrySet(index, shuffled[index]);

        return result;
    }

    private static bool TryNormalizeSingleLine(
        string value,
        out string normalized)
    {
        normalized = string.Empty;
        if (value == null)
            return true;

        string formC;
        try
        {
            formC = value.Normalize(NormalizationForm.FormC);
        }
        catch (ArgumentException)
        {
            return false;
        }

        StringBuilder builder = new StringBuilder(formC.Length);
        bool previousWasSpace = true;
        for (int index = 0; index < formC.Length; index++)
        {
            char character = formC[index];
            bool isSpace =
                char.IsWhiteSpace(character) ||
                char.IsControl(character);
            if (isSpace)
            {
                if (!previousWasSpace)
                {
                    builder.Append(' ');
                    previousWasSpace = true;
                }

                continue;
            }

            builder.Append(character);
            previousWasSpace = false;
        }

        if (builder.Length > 0 && builder[builder.Length - 1] == ' ')
            builder.Length--;

        normalized = builder.ToString();
        return true;
    }

    private static bool TryNormalizeSearchTerm(
        string value,
        out string normalized)
    {
        if (!TryNormalizeSingleLine(value, out normalized))
            return false;

        return normalized.Length > 0;
    }

    private static bool ContainsTerm(string source, string term)
    {
        if (term.Length == 0)
            return false;

        if (source.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0)
            return true;

        string compactSource = Compact(source);
        string compactTerm = Compact(term);
        return compactTerm.Length > 0 &&
               compactSource.IndexOf(
                   compactTerm,
                   StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static string Compact(string value)
    {
        StringBuilder builder = new StringBuilder(value.Length);
        for (int index = 0; index < value.Length; index++)
        {
            char character = value[index];
            if (char.IsWhiteSpace(character) ||
                char.IsPunctuation(character))
            {
                continue;
            }

            builder.Append(character);
        }

        return builder.ToString();
    }
}
