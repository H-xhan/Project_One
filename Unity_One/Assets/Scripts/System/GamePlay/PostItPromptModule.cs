using System;
using System.Collections.Generic;
using System.Text;

public sealed class PostItPromptSelection
{
    private readonly string[] _shuffledChoices;
    private readonly string[] _forbiddenStrings;

    public string AuthoringId { get; }
    public string PublicCategory { get; }
    public string SecretAnswer { get; }
    public IReadOnlyList<string> ShuffledChoices => _shuffledChoices;
    public IReadOnlyList<string> ForbiddenStrings => _forbiddenStrings;
    public int CorrectChoiceSlot { get; }
    public int ChoiceCount => _shuffledChoices.Length;

    internal PostItPromptSelection(
        string authoringId,
        string publicCategory,
        string secretAnswer,
        string[] shuffledChoices,
        string[] forbiddenStrings,
        int correctChoiceSlot)
    {
        AuthoringId = authoringId;
        PublicCategory = publicCategory;
        SecretAnswer = secretAnswer;
        _shuffledChoices = shuffledChoices;
        _forbiddenStrings = forbiddenStrings;
        CorrectChoiceSlot = correctChoiceSlot;
    }

    public string GetChoice(int index)
    {
        return index >= 0 && index < _shuffledChoices.Length
            ? _shuffledChoices[index]
            : string.Empty;
    }
}

public sealed class PostItPromptModule
{
    private readonly List<PostItPromptDatabaseSO.Entry> _candidates =
        new List<PostItPromptDatabaseSO.Entry>();

    public PostItPromptSelection CurrentSelection { get; private set; }

    public bool TrySelect(
        PostItPromptDatabaseSO database,
        Random random,
        out PostItPromptSelection selection,
        out string error)
    {
        selection = null;
        error = null;
        CurrentSelection = null;
        _candidates.Clear();

        if (database == null)
        {
            error = "Prompt Database가 없습니다.";
            return false;
        }

        if (random == null)
        {
            error = "Prompt random source가 없습니다.";
            return false;
        }

        if (!database.ValidateDatabase(out error))
            return false;

        int playableCount = database.GetPlayableEntries(_candidates);
        if (playableCount < PostItPromptDatabaseSO.RequiredPlayablePromptCount)
        {
            error =
                $"플레이 가능한 prompt가 {PostItPromptDatabaseSO.RequiredPlayablePromptCount}개 미만입니다.";
            return false;
        }

        PostItPromptDatabaseSO.Entry selectedEntry =
            _candidates[random.Next(playableCount)];
        string answer = Normalize(selectedEntry.SecretAnswer);

        int[] order = { 0, 1, 2, 3 };
        for (int index = order.Length - 1; index > 0; index--)
        {
            int swapIndex = random.Next(index + 1);
            (order[index], order[swapIndex]) =
                (order[swapIndex], order[index]);
        }

        string[] shuffledChoices =
            new string[PostItPromptDatabaseSO.RequiredChoiceCount];
        int correctChoiceSlot = -1;
        for (int slot = 0; slot < shuffledChoices.Length; slot++)
        {
            string choice = Normalize(selectedEntry.Choices[order[slot]]);
            shuffledChoices[slot] = choice;
            if (string.Equals(choice, answer, StringComparison.Ordinal))
                correctChoiceSlot = slot;
        }

        if (correctChoiceSlot < 0)
        {
            error = "선택된 prompt의 정답 choice를 찾을 수 없습니다.";
            return false;
        }

        string[] forbiddenStrings = selectedEntry.ForbiddenStrings == null
            ? Array.Empty<string>()
            : new string[selectedEntry.ForbiddenStrings.Length];
        for (int index = 0; index < forbiddenStrings.Length; index++)
            forbiddenStrings[index] = Normalize(selectedEntry.ForbiddenStrings[index]);

        selection = new PostItPromptSelection(
            Normalize(selectedEntry.AuthoringId),
            Normalize(selectedEntry.PublicCategory),
            answer,
            shuffledChoices,
            forbiddenStrings,
            correctChoiceSlot);
        CurrentSelection = selection;
        return true;
    }

    public void Reset()
    {
        CurrentSelection = null;
        _candidates.Clear();
    }

    private static string Normalize(string value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Normalize(NormalizationForm.FormC).Trim();
    }
}
