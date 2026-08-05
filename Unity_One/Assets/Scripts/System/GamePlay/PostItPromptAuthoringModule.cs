using System;
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
    InsufficientDistractors = 9,
    EmptyTopic = 10,
    TopicTooLong = 11,
    InvalidTopicText = 12,
    EmptyDistractor = 13,
    DistractorTooLong = 14,
    InvalidDistractorText = 15,
    DuplicateChoice = 16
}

public sealed class PostItPromptAuthoringModule
{
    public const int RequiredDistractorCount =
        PostItPromptDatabaseSO.RequiredChoiceCount - 1;
    public const int MaxPromptTextElements = 12;
    public const int MaxPromptUtf8Bytes = 96;
    public const int MaxAnswerTextElements = MaxPromptTextElements;
    public const int MaxAnswerUtf8Bytes = MaxPromptUtf8Bytes;

    private const string CustomAuthoringId = "citizen_author";

    private static readonly UTF8Encoding StrictUtf8 =
        new UTF8Encoding(false, true);

    public bool TryCreateSelection(
        string rawPublicTopic,
        string rawAnswer,
        string rawDistractor0,
        string rawDistractor1,
        string rawDistractor2,
        Random random,
        out PostItPromptSelection selection,
        out PostItPromptAuthoringRejectionReason rejectionReason)
    {
        selection = null;
        rejectionReason = PostItPromptAuthoringRejectionReason.None;

        if (random == null)
        {
            rejectionReason =
                PostItPromptAuthoringRejectionReason.RandomSourceMissing;
            return false;
        }

        if (!TryValidateTopic(
                rawPublicTopic,
                out string publicTopic,
                out rejectionReason) ||
            !TryValidateAnswer(
                rawAnswer,
                publicTopic,
                out string answer,
                out rejectionReason))
        {
            return false;
        }

        string[] rawDistractors =
        {
            rawDistractor0,
            rawDistractor1,
            rawDistractor2
        };

        string[] choices =
            new string[PostItPromptDatabaseSO.RequiredChoiceCount];
        choices[0] = answer;
        for (int index = 0; index < RequiredDistractorCount; index++)
        {
            if (!TryValidateDistractor(
                    rawDistractors[index],
                    out string distractor,
                    out rejectionReason))
            {
                return false;
            }

            for (int choiceIndex = 0; choiceIndex <= index; choiceIndex++)
            {
                if (string.Equals(
                        choices[choiceIndex],
                        distractor,
                        StringComparison.Ordinal))
                {
                    rejectionReason =
                        PostItPromptAuthoringRejectionReason.DuplicateChoice;
                    return false;
                }
            }

            choices[index + 1] = distractor;
        }

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
            publicTopic,
            answer,
            choices,
            Array.Empty<string>(),
            correctChoiceSlot);
        return true;
    }

    public static bool TryValidateTopic(
        string rawTopic,
        out string normalizedTopic,
        out PostItPromptAuthoringRejectionReason rejectionReason)
    {
        switch (ValidatePromptText(rawTopic, out normalizedTopic))
        {
            case PromptTextValidationResult.Valid:
                rejectionReason = PostItPromptAuthoringRejectionReason.None;
                return true;
            case PromptTextValidationResult.Empty:
                rejectionReason =
                    PostItPromptAuthoringRejectionReason.EmptyTopic;
                return false;
            case PromptTextValidationResult.TooLong:
                rejectionReason =
                    PostItPromptAuthoringRejectionReason.TopicTooLong;
                return false;
            default:
                rejectionReason =
                    PostItPromptAuthoringRejectionReason.InvalidTopicText;
                return false;
        }
    }

    public static bool TryValidateAnswer(
        string rawAnswer,
        string rawPublicTopic,
        out string normalizedAnswer,
        out PostItPromptAuthoringRejectionReason rejectionReason)
    {
        normalizedAnswer = string.Empty;
        rejectionReason = PostItPromptAuthoringRejectionReason.None;

        if (!TryValidateTopic(
                rawPublicTopic,
                out string publicTopic,
                out rejectionReason))
        {
            return false;
        }

        switch (ValidatePromptText(rawAnswer, out normalizedAnswer))
        {
            case PromptTextValidationResult.Empty:
                normalizedAnswer = string.Empty;
                rejectionReason =
                    PostItPromptAuthoringRejectionReason.EmptyAnswer;
                return false;
            case PromptTextValidationResult.TooLong:
                normalizedAnswer = string.Empty;
                rejectionReason =
                    PostItPromptAuthoringRejectionReason.AnswerTooLong;
                return false;
            case PromptTextValidationResult.Invalid:
                normalizedAnswer = string.Empty;
                rejectionReason =
                    PostItPromptAuthoringRejectionReason.InvalidAnswerText;
                return false;
        }

        if (string.Equals(
                normalizedAnswer,
                publicTopic,
                StringComparison.Ordinal))
        {
            normalizedAnswer = string.Empty;
            rejectionReason =
                PostItPromptAuthoringRejectionReason.AnswerMatchesCategory;
            return false;
        }

        return true;
    }

    private static bool TryValidateDistractor(
        string rawDistractor,
        out string normalizedDistractor,
        out PostItPromptAuthoringRejectionReason rejectionReason)
    {
        switch (ValidatePromptText(rawDistractor, out normalizedDistractor))
        {
            case PromptTextValidationResult.Valid:
                rejectionReason = PostItPromptAuthoringRejectionReason.None;
                return true;
            case PromptTextValidationResult.Empty:
                rejectionReason =
                    PostItPromptAuthoringRejectionReason.EmptyDistractor;
                return false;
            case PromptTextValidationResult.TooLong:
                rejectionReason =
                    PostItPromptAuthoringRejectionReason.DistractorTooLong;
                return false;
            default:
                rejectionReason =
                    PostItPromptAuthoringRejectionReason.InvalidDistractorText;
                return false;
        }
    }

    private static PromptTextValidationResult ValidatePromptText(
        string rawValue,
        out string normalizedValue)
    {
        if (!TryNormalizeSingleLine(rawValue, out normalizedValue))
            return PromptTextValidationResult.Invalid;

        if (normalizedValue.Length == 0)
            return PromptTextValidationResult.Empty;

        try
        {
            return StringInfo.ParseCombiningCharacters(normalizedValue).Length <=
                       MaxPromptTextElements &&
                   StrictUtf8.GetByteCount(normalizedValue) <=
                       MaxPromptUtf8Bytes
                ? PromptTextValidationResult.Valid
                : PromptTextValidationResult.TooLong;
        }
        catch (ArgumentException)
        {
            normalizedValue = string.Empty;
            return PromptTextValidationResult.Invalid;
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
            if (CharUnicodeInfo.GetUnicodeCategory(formC, index) == UnicodeCategory.Format)
                return false;

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

    private enum PromptTextValidationResult : byte
    {
        Valid = 0,
        Empty = 1,
        TooLong = 2,
        Invalid = 3
    }
}
