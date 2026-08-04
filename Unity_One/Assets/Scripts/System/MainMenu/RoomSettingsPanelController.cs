using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public sealed class RoomSettingsPanelController : MonoBehaviour
{
    [Header("Create Modal")]
    [SerializeField] private GameObject modalRoot;
    [SerializeField] private CanvasGroup modalCanvasGroup;
    [SerializeField] private Button cancelButton;
    [SerializeField] private Button createConfirmButton;
    [SerializeField] private TMP_Text createStatusText;
    [SerializeField] private Selectable firstSelectable;
    [SerializeField] private Selectable returnFocusSelectable;

    [Header("Post-it Liar")]
    [SerializeField] private Button presetDatabaseButton;
    [SerializeField] private Button citizenAuthorButton;
    [SerializeField] private Image presetDatabaseButtonImage;
    [SerializeField] private Image citizenAuthorButtonImage;
    [SerializeField] private TMP_Text selectedModeText;
    [SerializeField] private TMP_Text modeDescriptionText;

    [Header("Selection Style")]
    [SerializeField] private Color selectedButtonColor = Color.white;
    [SerializeField] private Color unselectedButtonColor =
        new Color(0.78f, 0.78f, 0.78f, 1f);

    private readonly RoomGameplaySettingsDraft _draft =
        new RoomGameplaySettingsDraft();
    private bool _isInitialized;
    private bool _isModalOpen;
    private bool _isCreateLocked;

    public bool IsModalOpen => _isModalOpen;
    public bool IsCreateLocked => _isCreateLocked;
    public PostItLiarPromptSourceMode SelectedPromptSourceMode =>
        _draft.PostItLiar.PromptSourceMode;

    public event Action CreateRequested;
    public event Action ModalCancelled;

    private void Awake()
    {
        EnsureInitialized();
    }

    private void OnDestroy()
    {
        if (_isInitialized)
            UnregisterListeners();
    }

    public bool OpenForCreate()
    {
        EnsureInitialized();

        if (_isModalOpen || _isCreateLocked)
            return false;

        _draft.ResetToDefaults();
        _isCreateLocked = false;
        _isModalOpen = true;
        SetStatus(string.Empty);
        ApplyModalVisibility(true);
        RefreshVisuals();
        FocusSelectable(firstSelectable != null ? firstSelectable : presetDatabaseButton);
        return true;
    }

    public bool TryFreezeSnapshot(out RoomGameplaySettingsSnapshot snapshot)
    {
        EnsureInitialized();
        snapshot = null;

        if (!_isModalOpen || _isCreateLocked)
            return false;

        snapshot = FreezeForCreate();
        return true;
    }

    public RoomGameplaySettingsSnapshot FreezeForCreate()
    {
        EnsureInitialized();
        _isCreateLocked = true;

        if (_isModalOpen)
            SetStatus("방 생성 중...");

        RefreshVisuals();
        return _draft.Freeze();
    }

    public void HandleCreateSucceeded()
    {
        if (!_isModalOpen || !_isCreateLocked)
            return;

        _isModalOpen = false;
        ApplyModalVisibility(false);
    }

    public void HandleCreateFailed(string message)
    {
        if (!_isModalOpen || !_isCreateLocked)
            return;

        _isCreateLocked = false;
        SetStatus(string.IsNullOrWhiteSpace(message)
            ? "방 생성에 실패했습니다."
            : message);
        RefreshVisuals();
        FocusSelectable(createConfirmButton);
    }

    public void ShowCreateError(string message)
    {
        if (!_isModalOpen || _isCreateLocked)
            return;

        SetStatus(message);
        RefreshVisuals();
    }

    public void SetCreateLocked(bool isLocked)
    {
        _isCreateLocked = isLocked;
        RefreshVisuals();
    }

    public void ResetDraft()
    {
        EnsureInitialized();
        _draft.ResetToDefaults();
        _isCreateLocked = false;
        SetStatus(string.Empty);
        RefreshVisuals();
    }

    public void SelectPresetDatabase()
    {
        SetPromptSourceMode(PostItLiarPromptSourceMode.PresetDatabase);
    }

    public void SelectCitizenAuthor()
    {
        SetPromptSourceMode(PostItLiarPromptSourceMode.CitizenAuthor);
    }

    public static string GetDisplayName(PostItLiarPromptSourceMode mode)
    {
        return mode == PostItLiarPromptSourceMode.CitizenAuthor
            ? "시민 직접 출제"
            : "기본 주제";
    }

    private void SetPromptSourceMode(PostItLiarPromptSourceMode mode)
    {
        if (_isCreateLocked)
            return;

        _draft.PostItLiar.PromptSourceMode =
            RoomGameplaySettingsValidator.NormalizePromptSourceMode(mode);
        RefreshVisuals();
    }

    private void ResolveReferences()
    {
        if (presetDatabaseButtonImage == null && presetDatabaseButton != null)
            presetDatabaseButtonImage = presetDatabaseButton.targetGraphic as Image;

        if (citizenAuthorButtonImage == null && citizenAuthorButton != null)
            citizenAuthorButtonImage = citizenAuthorButton.targetGraphic as Image;
    }

    private void RegisterListeners()
    {
        if (presetDatabaseButton != null)
            presetDatabaseButton.onClick.AddListener(SelectPresetDatabase);

        if (citizenAuthorButton != null)
            citizenAuthorButton.onClick.AddListener(SelectCitizenAuthor);

        if (cancelButton != null)
            cancelButton.onClick.AddListener(CancelCreate);

        if (createConfirmButton != null)
            createConfirmButton.onClick.AddListener(RequestCreate);
    }

    private void UnregisterListeners()
    {
        if (presetDatabaseButton != null)
            presetDatabaseButton.onClick.RemoveListener(SelectPresetDatabase);

        if (citizenAuthorButton != null)
            citizenAuthorButton.onClick.RemoveListener(SelectCitizenAuthor);

        if (cancelButton != null)
            cancelButton.onClick.RemoveListener(CancelCreate);

        if (createConfirmButton != null)
            createConfirmButton.onClick.RemoveListener(RequestCreate);
    }

    private void EnsureInitialized()
    {
        if (_isInitialized)
            return;

        _isInitialized = true;
        ResolveReferences();
        RegisterListeners();
        _draft.ResetToDefaults();
        _isCreateLocked = false;
        SetStatus(string.Empty);
        RefreshVisuals();
    }

    private void RequestCreate()
    {
        if (!_isModalOpen || _isCreateLocked)
            return;

        CreateRequested?.Invoke();
    }

    private void CancelCreate()
    {
        if (!_isModalOpen || _isCreateLocked)
            return;

        _draft.ResetToDefaults();
        _isModalOpen = false;
        SetStatus(string.Empty);
        RefreshVisuals();
        ApplyModalVisibility(false);
        ModalCancelled?.Invoke();
        FocusSelectable(returnFocusSelectable);
    }

    private void ApplyModalVisibility(bool isVisible)
    {
        if (modalCanvasGroup != null)
        {
            modalCanvasGroup.alpha = isVisible ? 1f : 0f;
            modalCanvasGroup.interactable = isVisible;
            modalCanvasGroup.blocksRaycasts = isVisible;
        }

        if (modalRoot != null && modalRoot.activeSelf != isVisible)
            modalRoot.SetActive(isVisible);
    }

    private void SetStatus(string message)
    {
        if (createStatusText != null)
            createStatusText.text = message ?? string.Empty;
    }

    private static void FocusSelectable(Selectable selectable)
    {
        if (selectable == null || !selectable.IsActive() || !selectable.IsInteractable())
            return;

        if (EventSystem.current != null)
            EventSystem.current.SetSelectedGameObject(selectable.gameObject);
        else
            selectable.Select();
    }

    private void RefreshVisuals()
    {
        bool isCitizenAuthor =
            _draft.PostItLiar.PromptSourceMode ==
            PostItLiarPromptSourceMode.CitizenAuthor;

        if (presetDatabaseButton != null)
            presetDatabaseButton.interactable = !_isCreateLocked;

        if (citizenAuthorButton != null)
            citizenAuthorButton.interactable = !_isCreateLocked;

        if (cancelButton != null)
            cancelButton.interactable = _isModalOpen && !_isCreateLocked;

        if (createConfirmButton != null)
            createConfirmButton.interactable = _isModalOpen && !_isCreateLocked;

        if (presetDatabaseButtonImage != null)
        {
            presetDatabaseButtonImage.color = isCitizenAuthor
                ? unselectedButtonColor
                : selectedButtonColor;
        }

        if (citizenAuthorButtonImage != null)
        {
            citizenAuthorButtonImage.color = isCitizenAuthor
                ? selectedButtonColor
                : unselectedButtonColor;
        }

        if (selectedModeText != null)
        {
            selectedModeText.text =
                $"현재 선택: {GetDisplayName(_draft.PostItLiar.PromptSourceMode)}";
        }

        if (modeDescriptionText != null)
        {
            modeDescriptionText.text = isCitizenAuthor
                ? "라이어 선정 후 시민 출제자 한 명이\n주제와 정답을 직접 정합니다."
                : "게임이 준비한 주제와 정답을 사용합니다.";
        }
    }
}
