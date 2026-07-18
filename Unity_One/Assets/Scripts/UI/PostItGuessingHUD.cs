using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class PostItGuessingHUD : MonoBehaviour
{
    private static readonly PostItTopicId[] TopicOptions =
    {
        PostItTopicId.Animal,
        PostItTopicId.Food,
        PostItTopicId.Object,
        PostItTopicId.Emotion
    };

    [Header("Roots")]
    [SerializeField] private GameObject guessingPanel;
    [SerializeField] private GameObject guessCardRoot;
    [SerializeField] private Transform topicOptionsRoot;

    [Header("Data")]
    [SerializeField] private GameStateManager gameStateManager;
    [SerializeField] private PlayerPostItInventory targetInventory;
    [SerializeField] private PostItVisualCatalogSO visualCatalog;
    [SerializeField] private float rebindInterval = 0.25f;

    [Header("Card")]
    [SerializeField] private Image previewImage;
    [SerializeField] private TMP_Text progressText;
    [SerializeField] private TMP_Text statusText;
    [SerializeField] private TMP_Text deadlineText;
    [SerializeField] private TMP_Text emptyStateText;

    [Header("Actions")]
    [SerializeField] private Button topicButtonTemplate;
    [SerializeField] private Button submitButton;
    [SerializeField] private Color selectedTopicColor = new Color(1f, 0.84f, 0.3f, 1f);

    private readonly Button[] _topicButtons = new Button[4];
    private readonly TMP_Text[] _topicButtonLabels = new TMP_Text[4];
    private readonly UnityAction[] _topicButtonActions = new UnityAction[4];
    private readonly ColorBlock[] _topicButtonBaseColors = new ColorBlock[4];

    private PlayerPostItInventory _boundInventory;
    private PostItGuessOwnerData _currentData = PostItGuessOwnerData.Invalid;
    private PostItTopicId _selectedTopicId = PostItTopicId.None;
    private float _nextInventoryBindAttemptTime;
    private float _nextGameStateBindAttemptTime;
    private int _currentGuessIndex = -1;
    private int _currentPostItId = -1;
    private int _lastDeadlineSeconds = int.MinValue;
    private bool _isGameStateSubscribed;
    private bool _topicButtonsCreated;
    private bool _buttonListenersRegistered;
    private bool _awaitingSubmitResponse;
    private bool _currentCatalogEntryValid;

    private void Awake()
    {
        EnsureTopicButtons();
    }

    private void OnEnable()
    {
        EnsureTopicButtons();
        RegisterButtonListeners();
        TryBindGameState();
        TryBindInventory();
        ApplyCurrentGameState();
    }

    private void OnDisable()
    {
        UnregisterButtonListeners();
        UnbindInventory();
        UnbindGameState();
        ResetCurrentSelection();
    }

    private void Update()
    {
        if (_isGameStateSubscribed && gameStateManager == null)
            _isGameStateSubscribed = false;

        if (!_isGameStateSubscribed &&
            Time.unscaledTime >= _nextGameStateBindAttemptTime)
        {
            TryBindGameState();
        }

        if (_boundInventory == null &&
            Time.unscaledTime >= _nextInventoryBindAttemptTime)
        {
            TryBindInventory();
        }
    }

    private void EnsureTopicButtons()
    {
        if (_topicButtonsCreated ||
            topicButtonTemplate == null ||
            topicOptionsRoot == null)
        {
            return;
        }

        for (int index = 0; index < TopicOptions.Length; index++)
        {
            PostItTopicId topicId = TopicOptions[index];
            Button button = Instantiate(topicButtonTemplate, topicOptionsRoot, false);
            button.name = $"TopicButton_{topicId}";
            button.gameObject.SetActive(true);

            _topicButtons[index] = button;
            _topicButtonLabels[index] = button.GetComponentInChildren<TMP_Text>(true);
            _topicButtonBaseColors[index] = button.colors;

            if (_topicButtonLabels[index] != null)
                _topicButtonLabels[index].text = GetTopicLabel(topicId);

            int capturedIndex = index;
            _topicButtonActions[index] = () => SelectTopic(TopicOptions[capturedIndex]);
        }

        topicButtonTemplate.gameObject.SetActive(false);
        _topicButtonsCreated = true;
    }

    private void RegisterButtonListeners()
    {
        if (_buttonListenersRegistered)
            return;

        if (_topicButtonsCreated)
        {
            for (int index = 0; index < _topicButtons.Length; index++)
            {
                if (_topicButtons[index] != null && _topicButtonActions[index] != null)
                    _topicButtons[index].onClick.AddListener(_topicButtonActions[index]);
            }
        }

        if (submitButton != null)
            submitButton.onClick.AddListener(SubmitCurrentGuess);

        _buttonListenersRegistered = true;
    }

    private void UnregisterButtonListeners()
    {
        if (!_buttonListenersRegistered)
            return;

        for (int index = 0; index < _topicButtons.Length; index++)
        {
            if (_topicButtons[index] != null && _topicButtonActions[index] != null)
                _topicButtons[index].onClick.RemoveListener(_topicButtonActions[index]);
        }

        if (submitButton != null)
            submitButton.onClick.RemoveListener(SubmitCurrentGuess);

        _buttonListenersRegistered = false;
    }

    private void TryBindGameState()
    {
        _nextGameStateBindAttemptTime =
            Time.unscaledTime + Mathf.Max(0.05f, rebindInterval);

        if (gameStateManager == null)
            gameStateManager = FindFirstObjectByType<GameStateManager>();

        if (gameStateManager == null || _isGameStateSubscribed)
            return;

        gameStateManager.StateValue.OnValueChanged += OnGameStateChanged;
        gameStateManager.StateTimer.OnValueChanged += OnStateTimerChanged;
        _isGameStateSubscribed = true;
        ApplyCurrentGameState();
    }

    private void UnbindGameState()
    {
        if (_isGameStateSubscribed && gameStateManager != null)
        {
            gameStateManager.StateValue.OnValueChanged -= OnGameStateChanged;
            gameStateManager.StateTimer.OnValueChanged -= OnStateTimerChanged;
        }

        _isGameStateSubscribed = false;
        gameStateManager = null;
    }

    private void OnGameStateChanged(int previousStateValue, int newStateValue)
    {
        ApplyCurrentGameState();
    }

    private void OnStateTimerChanged(float previousValue, float newValue)
    {
        if (!IsGuessingState())
            return;

        RefreshDeadlineAndControls(false);
    }

    private void TryBindInventory()
    {
        _nextInventoryBindAttemptTime =
            Time.unscaledTime + Mathf.Max(0.05f, rebindInterval);

        PlayerPostItInventory inventory = ResolveTargetInventory();
        if (inventory == null)
        {
            RefreshGuessItems();
            return;
        }

        BindInventory(inventory);
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

    private void BindInventory(PlayerPostItInventory inventory)
    {
        if (_boundInventory == inventory)
        {
            RefreshGuessItems();
            return;
        }

        UnbindInventory();
        _boundInventory = inventory;
        _boundInventory.GuessItemsChanged += OnGuessItemsChanged;
        RefreshGuessItems();
    }

    private void UnbindInventory()
    {
        if (_boundInventory != null)
            _boundInventory.GuessItemsChanged -= OnGuessItemsChanged;

        _boundInventory = null;
    }

    private void OnGuessItemsChanged()
    {
        _awaitingSubmitResponse = false;
        RefreshGuessItems();
    }

    private void ApplyCurrentGameState()
    {
        bool showGuessing = IsGuessingState();
        if (guessingPanel != null && guessingPanel != gameObject &&
            guessingPanel.activeSelf != showGuessing)
        {
            guessingPanel.SetActive(showGuessing);
        }

        _lastDeadlineSeconds = int.MinValue;
        if (showGuessing)
        {
            RefreshGuessItems();
            return;
        }

        _awaitingSubmitResponse = false;
        ResetCurrentSelection();
        RefreshDeadlineText(true);
    }

    private bool IsGuessingState()
    {
        return gameStateManager != null &&
               gameStateManager.GetState() == GameStateManager.GameState.Guessing;
    }

    private void RefreshGuessItems()
    {
        int itemCount = _boundInventory != null
            ? _boundInventory.GuessItemCount
            : 0;

        if (itemCount <= 0)
        {
            ResetCurrentSelection();
            SetEmptyState(true);
            if (progressText != null)
                progressText.SetText("0 / 0");
            RefreshCurrentCardPresentation();
            return;
        }

        int currentIndex = -1;
        int firstPendingIndex = -1;
        for (int index = 0; index < itemCount; index++)
        {
            PostItGuessOwnerData candidate = _boundInventory.GuessItems[index];
            if (candidate.PostItId == _currentPostItId)
                currentIndex = index;

            if (firstPendingIndex < 0 && candidate.Status == PostItGuessStatus.Pending)
                firstPendingIndex = index;
        }

        int selectedIndex;
        if (currentIndex >= 0 &&
            _boundInventory.GuessItems[currentIndex].Status == PostItGuessStatus.Pending)
        {
            selectedIndex = currentIndex;
        }
        else if (firstPendingIndex >= 0)
        {
            selectedIndex = firstPendingIndex;
        }
        else
        {
            selectedIndex = currentIndex >= 0 ? currentIndex : itemCount - 1;
        }

        PostItGuessOwnerData selectedData = _boundInventory.GuessItems[selectedIndex];
        bool changedCard = selectedData.PostItId != _currentPostItId;
        _currentGuessIndex = selectedIndex;
        _currentPostItId = selectedData.PostItId;
        _currentData = selectedData;

        if (changedCard)
        {
            _awaitingSubmitResponse = false;
            _selectedTopicId = IsSelectableTopic(selectedData.SelectedTopicId)
                ? selectedData.SelectedTopicId
                : PostItTopicId.None;
        }
        else if (selectedData.Status != PostItGuessStatus.Pending &&
                 IsSelectableTopic(selectedData.SelectedTopicId))
        {
            _selectedTopicId = selectedData.SelectedTopicId;
        }

        SetEmptyState(false);
        if (progressText != null)
            progressText.SetText("{0} / {1}", _currentGuessIndex + 1, itemCount);

        RefreshCurrentCardPresentation();
    }

    private void ResetCurrentSelection()
    {
        _currentData = PostItGuessOwnerData.Invalid;
        _currentGuessIndex = -1;
        _currentPostItId = -1;
        _selectedTopicId = PostItTopicId.None;
        _currentCatalogEntryValid = false;
    }

    private void SetEmptyState(bool isEmpty)
    {
        if (guessCardRoot != null && guessCardRoot.activeSelf == isEmpty)
            guessCardRoot.SetActive(!isEmpty);

        if (topicOptionsRoot != null && topicOptionsRoot.gameObject.activeSelf == isEmpty)
            topicOptionsRoot.gameObject.SetActive(!isEmpty);

        if (submitButton != null && submitButton.gameObject.activeSelf == isEmpty)
            submitButton.gameObject.SetActive(!isEmpty);

        if (emptyStateText != null)
        {
            if (emptyStateText.gameObject.activeSelf != isEmpty)
                emptyStateText.gameObject.SetActive(isEmpty);

            if (isEmpty)
                emptyStateText.text = "추측할 포스트잇이 없습니다.";
        }
    }

    private void RefreshCurrentCardPresentation()
    {
        RefreshPreview();
        RefreshDeadlineText(true);
        RefreshStatusText();
        RefreshTopicButtons();
        RefreshSubmitButton();
    }

    private void RefreshPreview()
    {
        _currentCatalogEntryValid = false;
        Sprite previewSprite = null;

        if (_currentData.IsValid &&
            visualCatalog != null &&
            visualCatalog.TryGetEntryByVisualId(
                _currentData.VisualId,
                out PostItVisualCatalogSO.Entry entry) &&
            entry.Type == PostItType.Drawing &&
            entry.PreviewSprite != null)
        {
            previewSprite = entry.PreviewSprite;
            _currentCatalogEntryValid = true;
        }

        if (previewImage == null)
            return;

        previewImage.sprite = previewSprite;
        previewImage.enabled = previewSprite != null;
    }

    private void RefreshDeadlineAndControls(bool forceText)
    {
        int previousDeadlineSeconds = _lastDeadlineSeconds;
        RefreshDeadlineText(forceText);
        if (!forceText && previousDeadlineSeconds == _lastDeadlineSeconds)
            return;

        RefreshStatusText();
        RefreshTopicButtons();
        RefreshSubmitButton();
    }

    private void RefreshDeadlineText(bool force)
    {
        int remainingSeconds = 0;
        if (IsGuessingState())
        {
            remainingSeconds = Mathf.CeilToInt(
                Mathf.Max(0f, gameStateManager.StateTimer.Value));
        }

        if (!force && remainingSeconds == _lastDeadlineSeconds)
            return;

        _lastDeadlineSeconds = remainingSeconds;
        if (deadlineText == null)
            return;

        if (!IsGuessingState())
            deadlineText.text = string.Empty;
        else if (remainingSeconds <= 0)
            deadlineText.text = "시간 종료";
        else
            deadlineText.SetText("남은 시간 {0}초", remainingSeconds);
    }

    private void RefreshStatusText()
    {
        if (statusText == null)
            return;

        if (!_currentData.IsValid)
        {
            statusText.text = string.Empty;
            return;
        }

        switch (_currentData.Status)
        {
            case PostItGuessStatus.Pending:
                if (!IsSubmissionWindowOpen())
                    statusText.text = "시간 종료";
                else if (_awaitingSubmitResponse)
                    statusText.text = "제출 중...";
                else
                    statusText.text = GetSelectionStatusLabel(_selectedTopicId);
                break;
            case PostItGuessStatus.Submitted:
                statusText.text = "제출 완료";
                break;
            case PostItGuessStatus.Correct:
            case PostItGuessStatus.Incorrect:
            case PostItGuessStatus.Skipped:
                statusText.text = "추측 종료";
                break;
            default:
                statusText.text = string.Empty;
                break;
        }
    }

    private void RefreshTopicButtons()
    {
        bool canChoose = _currentData.IsValid &&
                         _currentData.Status == PostItGuessStatus.Pending &&
                         IsSubmissionWindowOpen() &&
                         !_awaitingSubmitResponse;

        for (int index = 0; index < _topicButtons.Length; index++)
        {
            Button button = _topicButtons[index];
            if (button == null)
                continue;

            PostItTopicId topicId = TopicOptions[index];
            bool topicAvailable = IsCatalogTopicAvailable(topicId);
            button.interactable = canChoose && topicAvailable;

            ColorBlock colors = _topicButtonBaseColors[index];
            if (_selectedTopicId == topicId)
            {
                colors.normalColor = selectedTopicColor;
                colors.highlightedColor = selectedTopicColor;
                colors.selectedColor = selectedTopicColor;
                colors.disabledColor = selectedTopicColor;
            }

            button.colors = colors;
        }
    }

    private void RefreshSubmitButton()
    {
        if (submitButton != null)
            submitButton.interactable = CanSubmitCurrentGuess();
    }

    private void SelectTopic(PostItTopicId topicId)
    {
        if (!_currentData.IsValid ||
            _currentData.Status != PostItGuessStatus.Pending ||
            !IsSubmissionWindowOpen() ||
            _awaitingSubmitResponse ||
            !IsCatalogTopicAvailable(topicId))
        {
            return;
        }

        _selectedTopicId = topicId;
        RefreshStatusText();
        RefreshTopicButtons();
        RefreshSubmitButton();
    }

    private void SubmitCurrentGuess()
    {
        if (!CanSubmitCurrentGuess())
            return;

        _awaitingSubmitResponse = true;
        _boundInventory.RequestSubmitPostItGuess(
            _currentData.RoundRevision,
            _currentData.GuessRevision,
            _currentData.PostItId,
            _selectedTopicId);

        RefreshStatusText();
        RefreshTopicButtons();
        RefreshSubmitButton();
    }

    private bool CanSubmitCurrentGuess()
    {
        return _boundInventory != null &&
               _boundInventory.IsSpawned &&
               _boundInventory.IsOwner &&
               _currentData.IsValid &&
               _currentData.Status == PostItGuessStatus.Pending &&
               _currentCatalogEntryValid &&
               IsSubmissionWindowOpen() &&
               !_awaitingSubmitResponse &&
               IsCatalogTopicAvailable(_selectedTopicId);
    }

    private bool IsSubmissionWindowOpen()
    {
        return IsGuessingState() && gameStateManager.StateTimer.Value > 0f;
    }

    private bool IsCatalogTopicAvailable(PostItTopicId topicId)
    {
        return IsSelectableTopic(topicId) &&
               visualCatalog != null &&
               visualCatalog.TryGetDrawingEntry(topicId, out _);
    }

    private static bool IsSelectableTopic(PostItTopicId topicId)
    {
        return PostItVisualCatalogSO.IsSupportedDrawingTopic(topicId);
    }

    private static string GetTopicLabel(PostItTopicId topicId)
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
                return "미선택";
        }
    }

    private static string GetSelectionStatusLabel(PostItTopicId topicId)
    {
        switch (topicId)
        {
            case PostItTopicId.Animal:
                return "선택: 동물";
            case PostItTopicId.Food:
                return "선택: 음식";
            case PostItTopicId.Object:
                return "선택: 사물";
            case PostItTopicId.Emotion:
                return "선택: 감정";
            default:
                return "주제를 선택하세요.";
        }
    }

    private void OnValidate()
    {
        rebindInterval = Mathf.Max(0.05f, rebindInterval);
    }
}
