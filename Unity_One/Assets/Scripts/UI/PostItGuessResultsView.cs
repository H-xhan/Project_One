using System.Collections.Generic;
using System.Text;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class PostItGuessResultsView : MonoBehaviour
{
    [Header("Roots")]
    [SerializeField] private GameObject resultsRoot;
    [SerializeField] private Transform localCardResultsRoot;
    [SerializeField] private GameObject resultCardTemplate;

    [Header("Data")]
    [SerializeField] private GameStateManager gameStateManager;
    [SerializeField] private PostItRoundManager postItRoundManager;
    [SerializeField] private PlayerPostItInventory targetInventory;
    [SerializeField] private PostItVisualCatalogSO visualCatalog;
    [SerializeField] private float rebindInterval = 0.25f;

    [Header("Text")]
    [SerializeField] private TMP_Text playerScoresText;
    [SerializeField] private string scoresPendingText = "점수 집계 중...";

    private readonly StringBuilder _scoreBuilder = new StringBuilder(512);
    private readonly StringBuilder _cardBuilder = new StringBuilder(128);
    private readonly List<PostItGuessPlayerScoreData> _orderedScores =
        new List<PostItGuessPlayerScoreData>();
    private readonly List<GameObject> _resultCards = new List<GameObject>();
    private readonly List<Image> _resultCardPreviews = new List<Image>();
    private readonly List<TMP_Text> _resultCardTexts = new List<TMP_Text>();

    private PostItRoundManager _boundRoundManager;
    private PlayerPostItInventory _boundInventory;
    private float _nextBindAttemptTime;
    private bool _isGameStateSubscribed;
    private bool _isScoreSubscribed;
    private bool _refreshPending;

    private void Awake()
    {
        if (resultCardTemplate != null)
            resultCardTemplate.SetActive(false);
    }

    private void OnEnable()
    {
        if (resultCardTemplate != null)
            resultCardTemplate.SetActive(false);

        TryBindDependencies();
        ApplyCurrentGameState();
    }

    private void OnDisable()
    {
        UnbindInventory();
        UnbindRoundManager();
        UnbindGameState();
    }

    private void Update()
    {
        if (_isGameStateSubscribed && gameStateManager == null)
            _isGameStateSubscribed = false;

        if (_isScoreSubscribed && _boundRoundManager == null)
            _isScoreSubscribed = false;

        if (_isGameStateSubscribed &&
            _isScoreSubscribed &&
            _boundInventory != null)
        {
            return;
        }

        if (Time.unscaledTime >= _nextBindAttemptTime)
            TryBindDependencies();
    }

    private void LateUpdate()
    {
        if (!_refreshPending)
            return;

        _refreshPending = false;
        if (IsResultsState())
            RefreshResults();
    }

    private void TryBindDependencies()
    {
        _nextBindAttemptTime =
            Time.unscaledTime + Mathf.Max(0.05f, rebindInterval);

        TryBindGameState();
        TryBindRoundManager();
        TryBindInventory();
    }

    private void TryBindGameState()
    {
        if (gameStateManager == null)
            gameStateManager = FindFirstObjectByType<GameStateManager>();

        if (gameStateManager == null || _isGameStateSubscribed)
            return;

        gameStateManager.StateValue.OnValueChanged += OnGameStateChanged;
        _isGameStateSubscribed = true;
    }

    private void UnbindGameState()
    {
        if (_isGameStateSubscribed && gameStateManager != null)
            gameStateManager.StateValue.OnValueChanged -= OnGameStateChanged;

        _isGameStateSubscribed = false;
        gameStateManager = null;
    }

    private void OnGameStateChanged(int previousStateValue, int newStateValue)
    {
        ApplyCurrentGameState();
    }

    private void TryBindRoundManager()
    {
        PostItRoundManager candidate = postItRoundManager;
        if (candidate == null)
            candidate = FindFirstObjectByType<PostItRoundManager>();

        if (candidate == null)
            return;

        if (_boundRoundManager == candidate && _isScoreSubscribed)
            return;

        UnbindRoundManager();
        postItRoundManager = candidate;
        _boundRoundManager = candidate;
        _boundRoundManager.GuessScoresChanged += OnGuessScoresChanged;
        _boundRoundManager.PostItLiarRevealChanged +=
            OnPostItLiarRevealChanged;
        _isScoreSubscribed = true;
        RefreshPlayerScores();
    }

    private void UnbindRoundManager()
    {
        if (_isScoreSubscribed && _boundRoundManager != null)
        {
            _boundRoundManager.GuessScoresChanged -= OnGuessScoresChanged;
            _boundRoundManager.PostItLiarRevealChanged -=
                OnPostItLiarRevealChanged;
        }

        _isScoreSubscribed = false;
        _boundRoundManager = null;
    }

    private void OnGuessScoresChanged()
    {
        if (IsResultsState())
            _refreshPending = true;
    }

    private void OnPostItLiarRevealChanged()
    {
        if (IsResultsState())
            _refreshPending = true;
    }

    private void TryBindInventory()
    {
        PlayerPostItInventory inventory = ResolveTargetInventory();
        if (inventory == null)
            return;

        if (_boundInventory == inventory)
            return;

        UnbindInventory();
        _boundInventory = inventory;
        _boundInventory.GuessItemsChanged += OnGuessItemsChanged;
        RefreshLocalCardResults();
    }

    private PlayerPostItInventory ResolveTargetInventory()
    {
        if (targetInventory != null)
            return CanBindInventory(targetInventory) ? targetInventory : null;

        NetworkManager networkManager = NetworkManager.Singleton;
        if (networkManager == null || !networkManager.IsListening)
            return null;

        NetworkClient localClient = networkManager.LocalClient;
        if (localClient == null || localClient.PlayerObject == null)
            return null;

        PlayerPostItInventory inventory =
            localClient.PlayerObject.GetComponentInChildren<PlayerPostItInventory>(true);
        return CanBindInventory(inventory) ? inventory : null;
    }

    private static bool CanBindInventory(PlayerPostItInventory inventory)
    {
        if (inventory == null)
            return false;

        return !inventory.IsSpawned || inventory.IsOwner;
    }

    private void UnbindInventory()
    {
        if (_boundInventory != null)
            _boundInventory.GuessItemsChanged -= OnGuessItemsChanged;

        _boundInventory = null;
    }

    private void OnGuessItemsChanged()
    {
        if (IsResultsState())
            _refreshPending = true;
    }

    private void ApplyCurrentGameState()
    {
        bool showResults = IsResultsState();
        if (resultsRoot != null &&
            resultsRoot != gameObject &&
            resultsRoot.activeSelf != showResults)
        {
            resultsRoot.SetActive(showResults);
        }

        if (showResults)
        {
            RefreshResults();
            return;
        }

        HideUnusedResultCards(0);
    }

    private bool IsResultsState()
    {
        return gameStateManager != null &&
               gameStateManager.GetState() == GameStateManager.GameState.Results;
    }

    private void RefreshResults()
    {
        RefreshPlayerScores();
        RefreshLocalCardResults();
    }

    private void RefreshPlayerScores()
    {
        if (playerScoresText == null)
            return;

        if (TryRefreshPostItLiarScores())
            return;

        _orderedScores.Clear();
        if (_boundRoundManager != null)
        {
            for (int index = 0; index < _boundRoundManager.GuessScoreCount; index++)
            {
                PostItGuessPlayerScoreData score =
                    _boundRoundManager.ScoreItems[index];
                if (score.IsValid)
                    _orderedScores.Add(score);
            }
        }

        _orderedScores.Sort(CompareScoresByOwnerClientId);
        if (_orderedScores.Count == 0)
        {
            playerScoresText.text = scoresPendingText ?? string.Empty;
            return;
        }

        int roundRevision = _orderedScores[0].RoundRevision;
        int guessRevision = _orderedScores[0].GuessRevision;
        for (int index = 1; index < _orderedScores.Count; index++)
        {
            if (_orderedScores[index].RoundRevision != roundRevision ||
                _orderedScores[index].GuessRevision != guessRevision)
            {
                playerScoresText.text = scoresPendingText ?? string.Empty;
                return;
            }
        }

        NetworkManager networkManager = NetworkManager.Singleton;
        bool hasLocalClientId = networkManager != null && networkManager.IsListening;
        ulong localClientId = hasLocalClientId
            ? networkManager.LocalClientId
            : ulong.MaxValue;

        _scoreBuilder.Clear();
        for (int index = 0; index < _orderedScores.Count; index++)
        {
            PostItGuessPlayerScoreData score = _orderedScores[index];
            if (index > 0)
                _scoreBuilder.Append('\n');

            _scoreBuilder
                .Append("Player ")
                .Append(score.OwnerClientId);

            if (hasLocalClientId && score.OwnerClientId == localClientId)
                _scoreBuilder.Append(" (나)");

            _scoreBuilder
                .Append(" · 보유 ")
                .Append(score.HeldPostItCount)
                .Append(" · 정답 ")
                .Append(score.CorrectCount)
                .Append(" · 보너스 +")
                .Append(score.GuessBonusScore)
                .Append(" · 최종 ")
                .Append(score.FinalRoundScore);
        }

        playerScoresText.text = _scoreBuilder.ToString();
    }

    private bool TryRefreshPostItLiarScores()
    {
        if (_boundRoundManager == null ||
            !_boundRoundManager.LiarPublicState.IsActive)
        {
            return false;
        }

        if (!_boundRoundManager.HasLocalLiarReveal)
        {
            playerScoresText.text = scoresPendingText ?? string.Empty;
            return true;
        }

        PostItLiarRevealData reveal =
            _boundRoundManager.LocalLiarReveal;
        if (!reveal.IsValid ||
            reveal.RoundRevision !=
            _boundRoundManager.LiarPublicState.RoundRevision ||
            (reveal.PlayerResults.Count != 2 &&
             reveal.PlayerResults.Count != PostItLiarFixedSet.Capacity))
        {
            playerScoresText.text = scoresPendingText ?? string.Empty;
            return true;
        }

        byte localStableSlot = PostItLiarFixedSet.InvalidSlot;
        if (_boundRoundManager.HasLocalLiarPrivateRole)
        {
            localStableSlot =
                _boundRoundManager.LocalLiarPrivateRole.StableSlot;
        }

        _scoreBuilder.Clear();
        for (int index = 0;
             index < reveal.PlayerResults.Count;
             index++)
        {
            PostItLiarPlayerResultData result =
                reveal.PlayerResults.Get(index);
            if (result.StableSlot != index)
            {
                playerScoresText.text =
                    scoresPendingText ?? string.Empty;
                return true;
            }

            if (index > 0)
                _scoreBuilder.Append('\n');

            _scoreBuilder
                .Append('P')
                .Append(index + 1);

            if (result.StableSlot == localStableSlot)
                _scoreBuilder.Append(" (나)");

            _scoreBuilder
                .Append(" · 난투 ")
                .Append(result.BattleScore)
                .Append(" · 추리 +")
                .Append(result.DeductionScore)
                .Append(" · 최종 ")
                .Append(result.FinalRoundScore);

            if (!result.IsConnected)
                _scoreBuilder.Append(" · 연결 종료");
        }

        playerScoresText.text = _scoreBuilder.ToString();
        return true;
    }

    private static int CompareScoresByOwnerClientId(
        PostItGuessPlayerScoreData left,
        PostItGuessPlayerScoreData right)
    {
        return left.OwnerClientId.CompareTo(right.OwnerClientId);
    }

    private void RefreshLocalCardResults()
    {
        if (_boundRoundManager != null &&
            _boundRoundManager.LiarPublicState.IsActive)
        {
            HideLocalCardResults();
            return;
        }

        if (!TryGetLocalPublishedScore(out PostItGuessPlayerScoreData localScore) ||
            !AreLocalResultsReady(localScore))
        {
            HideLocalCardResults();
            return;
        }

        int itemCount = _boundInventory != null
            ? _boundInventory.GuessItemCount
            : 0;

        EnsureResultCardCount(itemCount);
        int renderCount = Mathf.Min(itemCount, _resultCards.Count);
        for (int index = 0; index < renderCount; index++)
        {
            GameObject card = _resultCards[index];
            if (card != null && !card.activeSelf)
                card.SetActive(true);

            RefreshResultCard(
                index,
                _boundInventory.GuessItems[index]);
        }

        HideUnusedResultCards(renderCount);
        if (localCardResultsRoot != null)
        {
            bool shouldShow = renderCount > 0;
            if (localCardResultsRoot.gameObject.activeSelf != shouldShow)
                localCardResultsRoot.gameObject.SetActive(shouldShow);
        }
    }

    private bool TryGetLocalPublishedScore(
        out PostItGuessPlayerScoreData localScore)
    {
        localScore = PostItGuessPlayerScoreData.Invalid;
        NetworkManager networkManager = NetworkManager.Singleton;
        if (_boundInventory == null ||
            !_boundInventory.IsSpawned ||
            !_boundInventory.IsOwner ||
            _boundRoundManager == null ||
            networkManager == null ||
            !networkManager.IsListening ||
            _boundInventory.OwnerClientId != networkManager.LocalClientId)
        {
            return false;
        }

        bool found = false;
        for (int index = 0; index < _boundRoundManager.GuessScoreCount; index++)
        {
            PostItGuessPlayerScoreData candidate =
                _boundRoundManager.ScoreItems[index];
            if (candidate.OwnerClientId != networkManager.LocalClientId)
                continue;

            if (found || !candidate.IsValid)
            {
                localScore = PostItGuessPlayerScoreData.Invalid;
                return false;
            }

            localScore = candidate;
            found = true;
        }

        return found;
    }

    private bool AreLocalResultsReady(PostItGuessPlayerScoreData localScore)
    {
        if (_boundInventory == null ||
            _boundInventory.GuessItemCount != localScore.EligibleCount)
        {
            return false;
        }

        int submittedCount = 0;
        int correctCount = 0;
        for (int index = 0; index < _boundInventory.GuessItemCount; index++)
        {
            PostItGuessOwnerData data = _boundInventory.GuessItems[index];
            if (!data.IsValid ||
                data.RoundRevision != localScore.RoundRevision ||
                data.GuessRevision != localScore.GuessRevision ||
                !IsFinalResultDataConsistent(data))
            {
                return false;
            }

            for (int otherIndex = index + 1;
                 otherIndex < _boundInventory.GuessItemCount;
                 otherIndex++)
            {
                if (_boundInventory.GuessItems[otherIndex].PostItId == data.PostItId)
                    return false;
            }

            if (data.Status == PostItGuessStatus.Correct)
            {
                submittedCount++;
                correctCount++;
            }
            else if (data.Status == PostItGuessStatus.Incorrect)
            {
                submittedCount++;
            }
        }

        return submittedCount == localScore.SubmittedCount &&
               correctCount == localScore.CorrectCount;
    }

    private static bool IsFinalResultDataConsistent(PostItGuessOwnerData data)
    {
        bool selectedTopicValid =
            PostItVisualCatalogSO.IsSupportedDrawingTopic(data.SelectedTopicId);
        bool revealedTopicValid =
            PostItVisualCatalogSO.IsSupportedDrawingTopic(data.RevealedTopicId);

        switch (data.Status)
        {
            case PostItGuessStatus.Correct:
                return selectedTopicValid &&
                       revealedTopicValid &&
                       data.SelectedTopicId == data.RevealedTopicId;
            case PostItGuessStatus.Incorrect:
                return selectedTopicValid &&
                       revealedTopicValid &&
                       data.SelectedTopicId != data.RevealedTopicId;
            case PostItGuessStatus.Skipped:
                return data.SelectedTopicId == PostItTopicId.None &&
                       revealedTopicValid;
            default:
                return false;
        }
    }

    private void HideLocalCardResults()
    {
        HideUnusedResultCards(0);
        if (localCardResultsRoot != null &&
            localCardResultsRoot.gameObject.activeSelf)
        {
            localCardResultsRoot.gameObject.SetActive(false);
        }
    }

    private void EnsureResultCardCount(int requiredCount)
    {
        if (requiredCount <= _resultCards.Count ||
            localCardResultsRoot == null ||
            resultCardTemplate == null)
        {
            return;
        }

        resultCardTemplate.SetActive(false);
        while (_resultCards.Count < requiredCount)
        {
            int cardIndex = _resultCards.Count;
            GameObject card = Instantiate(
                resultCardTemplate,
                localCardResultsRoot,
                false);
            card.name = $"ResultCard_{cardIndex + 1}";

            Image preview = FindChildComponentByName<Image>(
                card.transform,
                "PreviewImage");
            TMP_Text resultText = FindChildComponentByName<TMP_Text>(
                card.transform,
                "ResultText");
            if (resultText == null)
                resultText = card.GetComponentInChildren<TMP_Text>(true);

            _resultCards.Add(card);
            _resultCardPreviews.Add(preview);
            _resultCardTexts.Add(resultText);
        }
    }

    private void RefreshResultCard(
        int cardIndex,
        PostItGuessOwnerData data)
    {
        Image previewImage = _resultCardPreviews[cardIndex];
        Sprite previewSprite = null;
        if (data.IsValid &&
            visualCatalog != null &&
            visualCatalog.TryGetEntryByVisualId(
                data.VisualId,
                out PostItVisualCatalogSO.Entry entry) &&
            entry.Type == PostItType.Drawing)
        {
            previewSprite = entry.PreviewSprite;
        }

        if (previewImage != null)
        {
            previewImage.sprite = previewSprite;
            previewImage.enabled = previewSprite != null;
        }

        TMP_Text resultText = _resultCardTexts[cardIndex];
        if (resultText == null)
            return;

        if (!data.IsValid || !IsFinalResultStatus(data.Status))
        {
            resultText.text = "결과 동기화 중...";
            return;
        }

        _cardBuilder.Clear();
        _cardBuilder
            .Append("선택: ")
            .Append(GetSelectedTopicLabel(data.SelectedTopicId))
            .Append('\n')
            .Append("정답: ")
            .Append(GetRevealedTopicLabel(data.RevealedTopicId))
            .Append('\n')
            .Append("결과: ")
            .Append(GetResultStatusLabel(data.Status));
        resultText.text = _cardBuilder.ToString();
    }

    private static bool IsFinalResultStatus(PostItGuessStatus status)
    {
        return status == PostItGuessStatus.Correct ||
               status == PostItGuessStatus.Incorrect ||
               status == PostItGuessStatus.Skipped;
    }

    private static string GetSelectedTopicLabel(PostItTopicId topicId)
    {
        if (topicId == PostItTopicId.None)
            return "미선택";

        return GetSupportedTopicLabel(topicId);
    }

    private static string GetRevealedTopicLabel(PostItTopicId topicId)
    {
        return GetSupportedTopicLabel(topicId);
    }

    private static string GetSupportedTopicLabel(PostItTopicId topicId)
    {
        switch (topicId)
        {
            case PostItTopicId.Animal:
                return "동물";
            case PostItTopicId.Food:
                return "음식";
            case PostItTopicId.Object:
                return "사물";
            case PostItTopicId.Emotion:
                return "감정";
            default:
                return "미확인";
        }
    }

    private static string GetResultStatusLabel(PostItGuessStatus status)
    {
        switch (status)
        {
            case PostItGuessStatus.Correct:
                return "정답";
            case PostItGuessStatus.Incorrect:
                return "오답";
            case PostItGuessStatus.Skipped:
                return "미제출";
            default:
                return "동기화 중";
        }
    }

    private void HideUnusedResultCards(int activeCount)
    {
        for (int index = activeCount; index < _resultCards.Count; index++)
        {
            if (_resultCards[index] != null && _resultCards[index].activeSelf)
                _resultCards[index].SetActive(false);
        }
    }

    private static T FindChildComponentByName<T>(
        Transform root,
        string childName) where T : Component
    {
        if (root == null)
            return null;

        if (root.name == childName && root.TryGetComponent(out T component))
            return component;

        for (int index = 0; index < root.childCount; index++)
        {
            T match = FindChildComponentByName<T>(
                root.GetChild(index),
                childName);
            if (match != null)
                return match;
        }

        return null;
    }

    private void OnValidate()
    {
        rebindInterval = Mathf.Max(0.05f, rebindInterval);
    }
}
