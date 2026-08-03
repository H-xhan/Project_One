using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

public enum PostItPromptAuthoringRejectionReason : byte
{
    None = 0,
    DatabaseMissing = 1,
    RandomSourceMissing = 2,
    NoEligibleCategories = 3,
    InvalidCategory = 4,
    EmptyAnswer = 5,
    AnswerTooLong = 6,
    InvalidAnswerText = 7,
    AnswerMatchesCategory = 8,
    InsufficientDistractors = 9
}

public sealed class PostItPromptAuthoringModule
{
    public const int RequiredDistractorCount =
        PostItPromptDatabaseSO.RequiredChoiceCount - 1;
    public const int MaxAnswerTextElements = 12;
    public const int MaxAnswerUtf8Bytes = 96;

    private const string CustomAuthoringId = "citizen_author";

    private static readonly UTF8Encoding StrictUtf8 =
        new UTF8Encoding(false, true);

    private readonly List<string> _categoryOrder = new List<string>();
    private readonly Dictionary<string, HashSet<string>> _categoryPools =
        new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
    private readonly List<string> _distractorCandidates = new List<string>();

    public bool TryGetEligibleCategories(
        PostItPromptDatabaseSO database,
        List<string> destination,
        out PostItPromptAuthoringRejectionReason rejectionReason)
    {
        if (destination == null)
            throw new ArgumentNullException(nameof(destination));

        destination.Clear();
        rejectionReason = PostItPromptAuthoringRejectionReason.None;

        if (database == null)
        {
            rejectionReason =
                PostItPromptAuthoringRejectionReason.DatabaseMissing;
            return false;
        }

        BuildCategoryPools(database);
        for (int index = 0; index < _categoryOrder.Count; index++)
        {
            string category = _categoryOrder[index];
            if (_categoryPools.TryGetValue(
                    category,
                    out HashSet<string> candidates) &&
                candidates.Count >= RequiredDistractorCount)
            {
                destination.Add(category);
            }
        }

        if (destination.Count > 0)
            return true;

        rejectionReason =
            PostItPromptAuthoringRejectionReason.NoEligibleCategories;
        return false;
    }

    public bool TryCreateSelection(
        PostItPromptDatabaseSO database,
        string rawCategory,
        string rawAnswer,
        Random random,
        out PostItPromptSelection selection,
        out PostItPromptAuthoringRejectionReason rejectionReason)
    {
        selection = null;
        rejectionReason = PostItPromptAuthoringRejectionReason.None;

        if (database == null)
        {
            rejectionReason =
                PostItPromptAuthoringRejectionReason.DatabaseMissing;
            return false;
        }

        if (random == null)
        {
            rejectionReason =
                PostItPromptAuthoringRejectionReason.RandomSourceMissing;
            return false;
        }

        if (!TryNormalizeSingleLine(rawCategory, out string category) ||
            category.Length == 0)
        {
            rejectionReason =
                PostItPromptAuthoringRejectionReason.InvalidCategory;
            return false;
        }

        if (!TryValidateAnswer(
                rawAnswer,
                category,
                out string answer,
                out rejectionReason))
        {
            return false;
        }

        BuildCategoryPools(database);
        if (!_categoryPools.TryGetValue(
                category,
                out HashSet<string> categoryPool))
        {
            rejectionReason =
                PostItPromptAuthoringRejectionReason.InvalidCategory;
            return false;
        }

        _distractorCandidates.Clear();
        foreach (string candidate in categoryPool)
        {
            if (!string.Equals(candidate, answer, StringComparison.Ordinal))
                _distractorCandidates.Add(candidate);
        }

        _distractorCandidates.Sort(StringComparer.Ordinal);

        if (_distractorCandidates.Count < RequiredDistractorCount)
        {
            rejectionReason =
                PostItPromptAuthoringRejectionReason.InsufficientDistractors;
            return false;
        }

        for (int index = 0; index < RequiredDistractorCount; index++)
        {
            int swapIndex = random.Next(
                index,
                _distractorCandidates.Count);
            (_distractorCandidates[index], _distractorCandidates[swapIndex]) =
                (_distractorCandidates[swapIndex], _distractorCandidates[index]);
        }

        string[] choices =
            new string[PostItPromptDatabaseSO.RequiredChoiceCount];
        choices[0] = answer;
        for (int index = 0; index < RequiredDistractorCount; index++)
            choices[index + 1] = _distractorCandidates[index];

        for (int index = choices.Length - 1; index > 0; index--)
        {
            int swapIndex = random.Next(index + 1);
            (choices[index], choices[swapIndex]) =
                (choices[swapIndex], choices[index]);
        }

        int correctChoiceSlot = Array.IndexOf(choices, answer);
        if (correctChoiceSlot < 0)
        {
            rejectionReason =
                PostItPromptAuthoringRejectionReason.InvalidAnswerText;
            return false;
        }

        selection = new PostItPromptSelection(
            CustomAuthoringId,
            category,
            answer,
            choices,
            Array.Empty<string>(),
            correctChoiceSlot);
        return true;
    }

    public static bool TryValidateAnswer(
        string rawAnswer,
        string rawCategory,
        out string normalizedAnswer,
        out PostItPromptAuthoringRejectionReason rejectionReason)
    {
        normalizedAnswer = string.Empty;
        rejectionReason = PostItPromptAuthoringRejectionReason.None;

        if (!TryNormalizeSingleLine(rawCategory, out string category) ||
            category.Length == 0)
        {
            rejectionReason =
                PostItPromptAuthoringRejectionReason.InvalidCategory;
            return false;
        }

        if (!TryNormalizeSingleLine(rawAnswer, out normalizedAnswer))
        {
            rejectionReason =
                PostItPromptAuthoringRejectionReason.InvalidAnswerText;
            return false;
        }

        if (normalizedAnswer.Length == 0)
        {
            rejectionReason =
                PostItPromptAuthoringRejectionReason.EmptyAnswer;
            return false;
        }

        try
        {
            if (StringInfo.ParseCombiningCharacters(normalizedAnswer).Length >
                MaxAnswerTextElements ||
                StrictUtf8.GetByteCount(normalizedAnswer) >
                MaxAnswerUtf8Bytes)
            {
                normalizedAnswer = string.Empty;
                rejectionReason =
                    PostItPromptAuthoringRejectionReason.AnswerTooLong;
                return false;
            }
        }
        catch (ArgumentException)
        {
            normalizedAnswer = string.Empty;
            rejectionReason =
                PostItPromptAuthoringRejectionReason.InvalidAnswerText;
            return false;
        }

        if (string.Equals(
                normalizedAnswer,
                category,
                StringComparison.Ordinal))
        {
            normalizedAnswer = string.Empty;
            rejectionReason =
                PostItPromptAuthoringRejectionReason.AnswerMatchesCategory;
            return false;
        }

        return true;
    }

    private void BuildCategoryPools(PostItPromptDatabaseSO database)
    {
        _categoryOrder.Clear();
        _categoryPools.Clear();

        IReadOnlyList<PostItPromptDatabaseSO.Entry> entries = database.Entries;
        for (int index = 0; index < entries.Count; index++)
        {
            PostItPromptDatabaseSO.Entry entry = entries[index];
            if (!PostItPromptDatabaseSO.ValidateEntry(entry, index, out _))
                continue;

            if (!TryNormalizeSingleLine(
                    entry.PublicCategory,
                    out string category) ||
                category.Length == 0)
            {
                continue;
            }

            if (!_categoryPools.TryGetValue(
                    category,
                    out HashSet<string> candidates))
            {
                candidates = new HashSet<string>(StringComparer.Ordinal);
                _categoryPools.Add(category, candidates);
                _categoryOrder.Add(category);
            }

            TryAddCandidate(candidates, entry.SecretAnswer);
            for (int choiceIndex = 0;
                 choiceIndex < entry.Choices.Length;
                 choiceIndex++)
            {
                TryAddCandidate(candidates, entry.Choices[choiceIndex]);
            }
        }
    }

    private static void TryAddCandidate(
        HashSet<string> destination,
        string rawCandidate)
    {
        if (TryNormalizeSingleLine(rawCandidate, out string candidate) &&
            candidate.Length > 0)
        {
            destination.Add(candidate);
        }
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
}
