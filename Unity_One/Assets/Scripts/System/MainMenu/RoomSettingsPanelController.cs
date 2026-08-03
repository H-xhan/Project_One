using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class RoomSettingsPanelController : MonoBehaviour
{
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
    private bool _isCreateLocked;

    public PostItLiarPromptSourceMode SelectedPromptSourceMode =>
        _draft.PostItLiar.PromptSourceMode;

    private void Awake()
    {
        ResolveReferences();
        RegisterListeners();
        ResetDraft();
    }

    private void OnDestroy()
    {
        UnregisterListeners();
    }

    public RoomGameplaySettingsSnapshot FreezeForCreate()
    {
        _isCreateLocked = true;
        RefreshVisuals();
        return _draft.Freeze();
    }

    public void SetCreateLocked(bool isLocked)
    {
        _isCreateLocked = isLocked;
        RefreshVisuals();
    }

    public void ResetDraft()
    {
        _draft.ResetToDefaults();
        _isCreateLocked = false;
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
    }

    private void UnregisterListeners()
    {
        if (presetDatabaseButton != null)
            presetDatabaseButton.onClick.RemoveListener(SelectPresetDatabase);

        if (citizenAuthorButton != null)
            citizenAuthorButton.onClick.RemoveListener(SelectCitizenAuthor);
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
