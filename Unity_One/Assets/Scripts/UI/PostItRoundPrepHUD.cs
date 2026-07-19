using System.Collections.Generic;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class PostItRoundPrepHUD : MonoBehaviour
{
    private const int PreviewCardCount = 3;

    [Header("Roots")]
    [SerializeField] private GameObject prepPanel;
    [SerializeField] private GameObject cardTemplate;

    [Header("Data")]
    [SerializeField] private GameStateManager gameStateManager;
    [SerializeField] private PlayerPostItInventory targetInventory;
    [SerializeField] private PostItVisualCatalogSO visualCatalog;
    [SerializeField] private float rebindInterval = 0.25f;

    [Header("Text")]
    [SerializeField] private TMP_Text goalText;
    [SerializeField] private TMP_Text countdownText;
    [SerializeField] private string goalMessage =
        "상대에게서 획득한 그림을 라운드 종료 후 맞히세요";
    [SerializeField] private string syncingMessage = "초기 카드 동기화 중...";

    private readonly List<GameObject> _previewCards = new List<GameObject>();
    private readonly List<Image> _previewImages = new List<Image>();
    private readonly List<TMP_Text> _previewTexts = new List<TMP_Text>();
    private readonly HashSet<int> _snapshotPostItIds = new HashSet<int>();
    private readonly PostItRuntimeData[] _snapshot =
        new PostItRuntimeData[PreviewCardCount];
    private readonly PostItVisualCatalogSO.Entry[] _snapshotEntries =
        new PostItVisualCatalogSO.Entry[PreviewCardCount];

    private PlayerPostItInventory _boundInventory;
    private float _nextBindAttemptTime;
    private bool _isGameStateSubscribed;

    private void Awake()
    {
        if (cardTemplate != null)
            cardTemplate.SetActive(false);

        SetPrepVisible(false);
    }

    private void OnEnable()
    {
        if (cardTemplate != null)
            cardTemplate.SetActive(false);

        TryBindDependencies();
        ApplyCurrentGameState();
    }

    private void OnDisable()
    {
        UnbindInventory();
        UnbindGameState();
        SetPrepVisible(false);
    }

    private void Update()
    {
        if (_isGameStateSubscribed && gameStateManager == null)
            _isGameStateSubscribed = false;

        if ((!_isGameStateSubscribed || _boundInventory == null) &&
            Time.unscaledTime >= _nextBindAttemptTime)
        {
            TryBindDependencies();
            ApplyCurrentGameState();
        }
    }

    private void TryBindDependencies()
    {
        _nextBindAttemptTime =
            Time.unscaledTime + Mathf.Max(0.05f, rebindInterval);

        TryBindGameState();
        TryBindInventory();
    }

    private void TryBindGameState()
    {
        if (gameStateManager == null)
            gameStateManager = FindFirstObjectByType<GameStateManager>();

        if (gameStateManager == null || _isGameStateSubscribed)
            return;

        gameStateManager.StateValue.OnValueChanged += OnGameStateChanged;
        gameStateManager.StateTimer.OnValueChanged += OnStateTimerChanged;
        _isGameStateSubscribed = true;
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
        if (IsCountdownState())
            RefreshCountdown();
    }

    private void TryBindInventory()
    {
        PlayerPostItInventory inventory = ResolveTargetInventory();
        if (inventory == null)
        {
            if (IsCountdownState())
                RefreshPrep();

            return;
        }

        if (_boundInventory == inventory)
            return;

        UnbindInventory();
        _boundInventory = inventory;
        _boundInventory.PostItsChanged += OnPostItsChanged;

        if (IsCountdownState())
            RefreshPrep();
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
        return inventory != null && (!inventory.IsSpawned || inventory.IsOwner);
    }

    private void UnbindInventory()
    {
        if (_boundInventory != null)
            _boundInventory.PostItsChanged -= OnPostItsChanged;

        _boundInventory = null;
    }

    private void OnPostItsChanged()
    {
        if (IsCountdownState())
            RefreshPrep();
    }

    private void ApplyCurrentGameState()
    {
        bool showPrep = IsCountdownState();
        SetPrepVisible(showPrep);
        if (showPrep)
            RefreshPrep();
    }

    private bool IsCountdownState()
    {
        return gameStateManager != null &&
               gameStateManager.GetState() == GameStateManager.GameState.Countdown;
    }

    private void SetPrepVisible(bool visible)
    {
        if (prepPanel != null &&
            prepPanel != gameObject &&
            prepPanel.activeSelf != visible)
        {
            prepPanel.SetActive(visible);
        }
    }

    private void RefreshPrep()
    {
        EnsurePreviewCards();

        if (goalText != null)
            goalText.text = goalMessage ?? string.Empty;

        RefreshCountdown();

        if (!TryBuildInitialOwnerSnapshot())
        {
            ShowSyncingCards();
            return;
        }

        for (int cardIndex = 0; cardIndex < PreviewCardCount; cardIndex++)
            RefreshCard(cardIndex, _snapshot[cardIndex], _snapshotEntries[cardIndex]);
    }

    private void RefreshCountdown()
    {
        if (countdownText == null)
            return;

        int remainingSeconds = gameStateManager != null
            ? Mathf.CeilToInt(Mathf.Max(0f, gameStateManager.StateTimer.Value))
            : 0;
        countdownText.text = $"ROUND PREP · {remainingSeconds}";
    }

    private bool TryBuildInitialOwnerSnapshot()
    {
        NetworkManager networkManager = NetworkManager.Singleton;
        if (_boundInventory == null ||
            !_boundInventory.IsSpawned ||
            !_boundInventory.IsOwner ||
            networkManager == null ||
            !networkManager.IsListening ||
            _boundInventory.OwnerClientId != networkManager.LocalClientId ||
            visualCatalog == null)
        {
            return false;
        }

        _snapshotPostItIds.Clear();
        for (int slotIndex = 0; slotIndex < PreviewCardCount; slotIndex++)
        {
            if (!_boundInventory.TryGetPostItAtSlot(
                    slotIndex,
                    out PostItRuntimeData data) ||
                !data.IsValid ||
                data.SlotIndex != slotIndex ||
                data.OriginalOwnerClientId != networkManager.LocalClientId ||
                data.HolderClientId != networkManager.LocalClientId ||
                !IsSupportedPrepType(data.Type) ||
                !_snapshotPostItIds.Add(data.PostItId) ||
                !visualCatalog.TryGetEntryByVisualId(
                    data.VisualId,
                    out PostItVisualCatalogSO.Entry entry) ||
                entry.Type != data.Type ||
                entry.TopicId != data.TopicId)
            {
                return false;
            }

            _snapshot[slotIndex] = data;
            _snapshotEntries[slotIndex] = entry;
        }

        return true;
    }

    private static bool IsSupportedPrepType(PostItType type)
    {
        return type == PostItType.Drawing ||
               type == PostItType.Bonus ||
               type == PostItType.Penalty;
    }

    private void EnsurePreviewCards()
    {
        if (_previewCards.Count >= PreviewCardCount ||
            prepPanel == null ||
            cardTemplate == null)
        {
            return;
        }

        cardTemplate.SetActive(false);
        while (_previewCards.Count < PreviewCardCount)
        {
            int cardIndex = _previewCards.Count;
            GameObject card = Instantiate(
                cardTemplate,
                prepPanel.transform,
                false);
            card.name = $"PrepCard_{cardIndex + 1}";

            if (card.transform is RectTransform cardRect)
            {
                cardRect.anchorMin = new Vector2(0.5f, 0.5f);
                cardRect.anchorMax = new Vector2(0.5f, 0.5f);
                cardRect.pivot = new Vector2(0.5f, 0.5f);
                cardRect.sizeDelta = new Vector2(820f, 120f);
                cardRect.anchoredPosition =
                    new Vector2(0f, 132f - (132f * cardIndex));
            }

            Image preview = FindChildComponentByName<Image>(
                card.transform,
                "PreviewImage");
            TMP_Text cardText = FindChildComponentByName<TMP_Text>(
                card.transform,
                "ResultText");
            if (cardText == null)
                cardText = card.GetComponentInChildren<TMP_Text>(true);

            _previewCards.Add(card);
            _previewImages.Add(preview);
            _previewTexts.Add(cardText);
            card.SetActive(true);
        }
    }

    private void ShowSyncingCards()
    {
        int renderCount = Mathf.Min(PreviewCardCount, _previewCards.Count);
        for (int cardIndex = 0; cardIndex < renderCount; cardIndex++)
        {
            Image preview = _previewImages[cardIndex];
            if (preview != null)
            {
                preview.sprite = null;
                preview.enabled = false;
            }

            TMP_Text cardText = _previewTexts[cardIndex];
            if (cardText != null)
                cardText.text = $"카드 {cardIndex + 1} · {syncingMessage}";
        }
    }

    private void RefreshCard(
        int cardIndex,
        PostItRuntimeData data,
        PostItVisualCatalogSO.Entry entry)
    {
        if (cardIndex < 0 || cardIndex >= _previewCards.Count)
            return;

        Image preview = _previewImages[cardIndex];
        if (preview != null)
        {
            preview.sprite = entry.PreviewSprite;
            preview.enabled = entry.PreviewSprite != null;
        }

        TMP_Text cardText = _previewTexts[cardIndex];
        if (cardText == null)
            return;

        switch (data.Type)
        {
            case PostItType.Drawing:
                cardText.text =
                    $"카드 {cardIndex + 1} · 그림 주제: {GetTopicLabel(data.TopicId)}\n" +
                    "내 몸에 붙은 원본 그림 카드";
                break;
            case PostItType.Bonus:
                cardText.text =
                    $"카드 {cardIndex + 1} · Guard · E로 사용\n" +
                    "다음 유효 Post-it 뜯기 1회 방어";
                break;
            case PostItType.Penalty:
                cardText.text =
                    $"카드 {cardIndex + 1} · Heavy · Q로 사용\n" +
                    "조준한 상대의 이동/Sprint/Jump 일시 제한";
                break;
            default:
                cardText.text = $"카드 {cardIndex + 1} · 동기화 중...";
                break;
        }
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
                return "미확인";
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

        for (int childIndex = 0; childIndex < root.childCount; childIndex++)
        {
            T match = FindChildComponentByName<T>(
                root.GetChild(childIndex),
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
