using System;
using System.Collections.Generic;
using System.Text;
using Unity.Collections;
using UnityEngine;

[CreateAssetMenu(
    fileName = "PostItPromptDatabase",
    menuName = "Project ONE/Post-it Liar/Prompt Database")]
public sealed class PostItPromptDatabaseSO : ScriptableObject
{
    public const int RequiredPlayablePromptCount = 24;
    public const int RequiredChoiceCount = 4;

    private static readonly UTF8Encoding StrictUtf8 =
        new UTF8Encoding(false, true);

    [Serializable]
    public sealed class Entry
    {
        public string AuthoringId;
        public bool Enabled = true;
        public string PublicCategory;
        public string SecretAnswer;
        public string[] Choices = Array.Empty<string>();
        public string[] ForbiddenStrings = Array.Empty<string>();

        [TextArea]
        public string Notes;
    }

    [SerializeField]
    private Entry[] entries = Array.Empty<Entry>();

    public IReadOnlyList<Entry> Entries => entries ?? Array.Empty<Entry>();

    public int GetPlayableEntries(List<Entry> destination)
    {
        if (destination == null)
            throw new ArgumentNullException(nameof(destination));

        destination.Clear();
        if (entries == null || entries.Length == 0)
            return 0;

        Dictionary<string, int> authoringIdCounts =
            new Dictionary<string, int>(StringComparer.Ordinal);
        for (int index = 0; index < entries.Length; index++)
        {
            Entry entry = entries[index];
            if (entry == null || !entry.Enabled)
                continue;

            if (!TryNormalizeComparable(
                    entry.AuthoringId,
                    out string authoringId))
            {
                continue;
            }

            authoringIdCounts.TryGetValue(authoringId, out int count);
            authoringIdCounts[authoringId] = count + 1;
        }

        for (int index = 0; index < entries.Length; index++)
        {
            Entry entry = entries[index];
            if (!ValidateEntry(entry, index, out _))
                continue;

            if (!TryNormalizeComparable(
                    entry.AuthoringId,
                    out string authoringId) ||
                !authoringIdCounts.TryGetValue(authoringId, out int count) ||
                count != 1)
            {
                continue;
            }

            destination.Add(entry);
        }

        return destination.Count;
    }

    public bool ValidateDatabase(out string validationError)
    {
        validationError = null;
        if (entries == null || entries.Length == 0)
        {
            validationError = "Prompt entry가 없습니다.";
            return false;
        }

        HashSet<string> authoringIds =
            new HashSet<string>(StringComparer.Ordinal);
        int playableCount = 0;

        for (int index = 0; index < entries.Length; index++)
        {
            Entry entry = entries[index];
            if (entry == null || !entry.Enabled)
                continue;

            if (!ValidateEntry(entry, index, out validationError))
                return false;

            if (!TryNormalizeComparable(
                    entry.AuthoringId,
                    out string authoringId) ||
                !authoringIds.Add(authoringId))
            {
                validationError =
                    $"Prompt entry {index}의 AuthoringId가 중복되었습니다.";
                return false;
            }

            playableCount++;
        }

        if (playableCount < RequiredPlayablePromptCount)
        {
            validationError =
                $"플레이 가능한 prompt가 {RequiredPlayablePromptCount}개 미만입니다.";
            return false;
        }

        return true;
    }

    public static bool ValidateEntry(
        Entry entry,
        int index,
        out string validationError)
    {
        validationError = null;
        if (entry == null)
        {
            validationError = $"Prompt entry {index}가 null입니다.";
            return false;
        }

        if (!entry.Enabled)
        {
            validationError = $"Prompt entry {index}가 비활성화되었습니다.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(entry.AuthoringId))
        {
            validationError = $"Prompt entry {index}의 AuthoringId가 비어 있습니다.";
            return false;
        }

        if (!ValidateNetworkText(entry.PublicCategory))
        {
            validationError =
                $"Prompt entry {index}의 PublicCategory가 비어 있거나 너무 깁니다.";
            return false;
        }

        if (!ValidateNetworkText(entry.SecretAnswer))
        {
            validationError =
                $"Prompt entry {index}의 SecretAnswer가 비어 있거나 너무 깁니다.";
            return false;
        }

        if (entry.Choices == null ||
            entry.Choices.Length != RequiredChoiceCount)
        {
            validationError =
                $"Prompt entry {index}의 Choices는 정확히 {RequiredChoiceCount}개여야 합니다.";
            return false;
        }

        HashSet<string> choices =
            new HashSet<string>(StringComparer.Ordinal);
        if (!TryNormalizeComparable(
                entry.SecretAnswer,
                out string normalizedAnswer))
        {
            validationError =
                $"Prompt entry {index}의 SecretAnswer Unicode가 유효하지 않습니다.";
            return false;
        }

        int answerCount = 0;

        for (int choiceIndex = 0;
             choiceIndex < entry.Choices.Length;
             choiceIndex++)
        {
            string choice = entry.Choices[choiceIndex];
            if (!ValidateNetworkText(choice))
            {
                validationError =
                    $"Prompt entry {index}의 choice {choiceIndex}가 비어 있거나 너무 깁니다.";
                return false;
            }

            if (!TryNormalizeComparable(choice, out string normalizedChoice))
            {
                validationError =
                    $"Prompt entry {index}의 choice {choiceIndex} Unicode가 유효하지 않습니다.";
                return false;
            }

            if (!choices.Add(normalizedChoice))
            {
                validationError =
                    $"Prompt entry {index}에 중복 choice가 있습니다.";
                return false;
            }

            if (string.Equals(
                    normalizedChoice,
                    normalizedAnswer,
                    StringComparison.Ordinal))
            {
                answerCount++;
            }
        }

        if (answerCount != 1)
        {
            validationError =
                $"Prompt entry {index}의 answer는 Choices에 정확히 한 번 있어야 합니다.";
            return false;
        }

        if (entry.ForbiddenStrings == null)
            return true;

        HashSet<string> forbiddenStrings =
            new HashSet<string>(StringComparer.Ordinal);
        for (int forbiddenIndex = 0;
             forbiddenIndex < entry.ForbiddenStrings.Length;
             forbiddenIndex++)
        {
            string forbidden = entry.ForbiddenStrings[forbiddenIndex];
            if (!ValidateNetworkText(forbidden))
            {
                validationError =
                    $"Prompt entry {index}의 forbidden string {forbiddenIndex}가 비어 있거나 너무 깁니다.";
                return false;
            }

            if (!TryNormalizeComparable(
                    forbidden,
                    out string normalizedForbidden) ||
                !forbiddenStrings.Add(normalizedForbidden))
            {
                validationError =
                    $"Prompt entry {index}에 중복 forbidden string이 있습니다.";
                return false;
            }
        }

        return true;
    }

    private static bool ValidateNetworkText(string value)
    {
        if (!TryNormalizeComparable(value, out string normalized))
            return false;

        try
        {
            return StrictUtf8.GetByteCount(normalized) <=
                   FixedString128Bytes.UTF8MaxLengthInBytes;
        }
        catch (EncoderFallbackException)
        {
            return false;
        }
    }

    private static bool TryNormalizeComparable(
        string value,
        out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        try
        {
            normalized = value.Normalize(NormalizationForm.FormC).Trim();
            return normalized.Length > 0;
        }
        catch (ArgumentException)
        {
            normalized = string.Empty;
            return false;
        }
    }
}
