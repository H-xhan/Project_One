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
    EmptyTopic = 10,
    TopicTooLong = 11,
    InvalidTopicText = 12
}

public sealed class PostItPromptAuthoringModule
{
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
        out PostItPromptSelection selection,
        out PostItPromptAuthoringRejectionReason rejectionReason)
    {
        selection = null;
        rejectionReason = PostItPromptAuthoringRejectionReason.None;

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

        selection = new PostItPromptSelection(
            CustomAuthoringId,
            publicTopic,
            answer,
            Array.Empty<string>(),
            Array.Empty<string>(),
            -1);
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
                if (!PostItLiarAnswerMatcher.HasComparableContent(
                        normalizedTopic))
                {
                    normalizedTopic = string.Empty;
                    rejectionReason =
                        PostItPromptAuthoringRejectionReason.InvalidTopicText;
                    return false;
                }

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

        if (!PostItLiarAnswerMatcher.HasComparableContent(normalizedAnswer))
        {
            normalizedAnswer = string.Empty;
            rejectionReason =
                PostItPromptAuthoringRejectionReason.InvalidAnswerText;
            return false;
        }

        if (PostItLiarAnswerMatcher.TryEvaluate(
                publicTopic,
                normalizedAnswer,
                out _,
                out bool topicWouldMatchAnswer,
                out _) &&
            topicWouldMatchAnswer)
        {
            normalizedAnswer = string.Empty;
            rejectionReason =
                PostItPromptAuthoringRejectionReason.AnswerMatchesCategory;
            return false;
        }

        return true;
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
