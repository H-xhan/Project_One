using System;
using System.Globalization;
using System.Text;

public enum PostItLiarAnswerValidationResult : byte
{
    Valid = 0,
    Empty = 1,
    TooLong = 2,
    InvalidText = 3
}

public static class PostItLiarAnswerMatcher
{
    public const int MaxTextElements = 12;
    public const int MaxUtf8Bytes = 96;
    public const int MinimumHangulSyllablesForTypo = 3;

    private const int HangulSyllableBase = 0xAC00;
    private const int HangulSyllableEnd = 0xD7A3;
    private const int MedialCount = 21;
    private const int FinalCount = 28;
    private const int SyllablesPerInitial = MedialCount * FinalCount;

    private static readonly UTF8Encoding StrictUtf8 =
        new UTF8Encoding(false, true);

    public static bool HasComparableContent(string value)
    {
        return TryNormalizeSingleLine(value, out string normalized) &&
               TryBuildComparisonKey(normalized, out string key) &&
               key.Length > 0;
    }

    public static bool TryEvaluate(
        string rawGuess,
        string expectedAnswer,
        out string displayGuess,
        out bool isCorrect,
        out PostItLiarAnswerValidationResult validationResult)
    {
        displayGuess = string.Empty;
        isCorrect = false;
        validationResult = PostItLiarAnswerValidationResult.InvalidText;

        if (!TryNormalizeSingleLine(rawGuess, out displayGuess))
            return false;

        if (displayGuess.Length == 0)
        {
            validationResult = PostItLiarAnswerValidationResult.Empty;
            return false;
        }

        try
        {
            if (StringInfo.ParseCombiningCharacters(displayGuess).Length >
                    MaxTextElements ||
                StrictUtf8.GetByteCount(displayGuess) > MaxUtf8Bytes)
            {
                displayGuess = string.Empty;
                validationResult = PostItLiarAnswerValidationResult.TooLong;
                return false;
            }
        }
        catch (ArgumentException)
        {
            displayGuess = string.Empty;
            return false;
        }

        if (!TryNormalizeSingleLine(
                expectedAnswer,
                out string normalizedExpected) ||
            normalizedExpected.Length == 0 ||
            !TryBuildComparisonKey(displayGuess, out string guessKey) ||
            !TryBuildComparisonKey(normalizedExpected, out string expectedKey))
        {
            displayGuess = string.Empty;
            return false;
        }

        if (guessKey.Length == 0 || expectedKey.Length == 0)
        {
            displayGuess = string.Empty;
            validationResult = PostItLiarAnswerValidationResult.Empty;
            return false;
        }

        isCorrect = string.Equals(
                        guessKey,
                        expectedKey,
                        StringComparison.OrdinalIgnoreCase) ||
                    IsLimitedHangulJamoTypo(guessKey, expectedKey);
        validationResult = PostItLiarAnswerValidationResult.Valid;
        return true;
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
        try
        {
            for (int index = 0; index < formC.Length;)
            {
                int scalarLength = GetScalarLength(formC, index);
                if (scalarLength == 0)
                    return false;

                UnicodeCategory category =
                    CharUnicodeInfo.GetUnicodeCategory(formC, index);
                if (category == UnicodeCategory.Format)
                    return false;

                if (IsWhitespaceOrControl(category))
                {
                    if (!previousWasSpace)
                    {
                        builder.Append(' ');
                        previousWasSpace = true;
                    }
                }
                else
                {
                    builder.Append(formC, index, scalarLength);
                    previousWasSpace = false;
                }

                index += scalarLength;
            }
        }
        catch (ArgumentException)
        {
            return false;
        }

        if (builder.Length > 0 && builder[builder.Length - 1] == ' ')
            builder.Length--;

        normalized = builder.ToString();
        return true;
    }

    private static bool TryBuildComparisonKey(
        string value,
        out string key)
    {
        key = string.Empty;
        StringBuilder builder = new StringBuilder(value.Length);
        try
        {
            for (int index = 0; index < value.Length;)
            {
                int scalarLength = GetScalarLength(value, index);
                if (scalarLength == 0)
                    return false;

                UnicodeCategory category =
                    CharUnicodeInfo.GetUnicodeCategory(value, index);
                if (category == UnicodeCategory.Format)
                    return false;

                if (!IsWhitespaceOrControl(category) &&
                    !IsPunctuation(category))
                {
                    builder.Append(value, index, scalarLength);
                }

                index += scalarLength;
            }
        }
        catch (ArgumentException)
        {
            return false;
        }

        key = builder.ToString();
        return true;
    }

    private static bool IsLimitedHangulJamoTypo(
        string guessKey,
        string expectedKey)
    {
        if (guessKey.Length != expectedKey.Length ||
            expectedKey.Length < MinimumHangulSyllablesForTypo)
        {
            return false;
        }

        int jamoMismatchCount = 0;
        for (int index = 0; index < expectedKey.Length; index++)
        {
            int guessSyllable = guessKey[index];
            int expectedSyllable = expectedKey[index];
            if (guessSyllable < HangulSyllableBase ||
                guessSyllable > HangulSyllableEnd ||
                expectedSyllable < HangulSyllableBase ||
                expectedSyllable > HangulSyllableEnd)
            {
                return false;
            }

            int guessOffset = guessSyllable - HangulSyllableBase;
            int expectedOffset = expectedSyllable - HangulSyllableBase;
            if (guessOffset / SyllablesPerInitial !=
                expectedOffset / SyllablesPerInitial)
            {
                jamoMismatchCount++;
            }
            if ((guessOffset % SyllablesPerInitial) / FinalCount !=
                (expectedOffset % SyllablesPerInitial) / FinalCount)
            {
                jamoMismatchCount++;
            }
            if (guessOffset % FinalCount != expectedOffset % FinalCount)
                jamoMismatchCount++;

            if (jamoMismatchCount > 1)
                return false;
        }

        return jamoMismatchCount == 1;
    }

    private static int GetScalarLength(string value, int index)
    {
        char character = value[index];
        if (char.IsHighSurrogate(character))
        {
            return index + 1 < value.Length &&
                   char.IsLowSurrogate(value[index + 1])
                ? 2
                : 0;
        }

        return char.IsLowSurrogate(character) ? 0 : 1;
    }

    private static bool IsWhitespaceOrControl(UnicodeCategory category)
    {
        return category == UnicodeCategory.Control ||
               category == UnicodeCategory.SpaceSeparator ||
               category == UnicodeCategory.LineSeparator ||
               category == UnicodeCategory.ParagraphSeparator;
    }

    private static bool IsPunctuation(UnicodeCategory category)
    {
        return category == UnicodeCategory.ConnectorPunctuation ||
               category == UnicodeCategory.DashPunctuation ||
               category == UnicodeCategory.OpenPunctuation ||
               category == UnicodeCategory.ClosePunctuation ||
               category == UnicodeCategory.InitialQuotePunctuation ||
               category == UnicodeCategory.FinalQuotePunctuation ||
               category == UnicodeCategory.OtherPunctuation;
    }
}
