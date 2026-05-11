using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class CharacterSelectionView : MonoBehaviour
{
    [SerializeField, Tooltip("선택된 캐릭터 ID를 제공하는 캐릭터 선택 시스템입니다. 비워두면 씬에서 자동 탐색합니다.")]
    private CharacterSelectionSystem characterSelectionSystem;

    [SerializeField, Tooltip("캐릭터 버튼별 선택 표시 오브젝트 목록입니다. 배열 인덱스가 characterId와 일치해야 합니다.")]
    private GameObject[] selectedFrames;

    [SerializeField, Tooltip("캐릭터 선택 버튼 목록입니다. 선택 상태에 따른 버튼 시각 효과를 추가로 갱신할 때 사용합니다.")]
    private Button[] characterButtons;

    [SerializeField, Tooltip("선택된 캐릭터 이름을 표시할 텍스트입니다. 비워두면 갱신하지 않습니다.")]
    private TMP_Text selectedCharacterNameText;

    [SerializeField, Tooltip("캐릭터 ID별 표시 이름입니다. 배열 인덱스가 characterId와 일치해야 합니다.")]
    private string[] characterNames;

    [SerializeField, Tooltip("선택 표시를 다시 확인하는 갱신 간격입니다.")]
    private float refreshInterval = 0.1f;

    [SerializeField, Tooltip("선택값을 찾지 못했을 때 모든 선택 표시를 숨길지 여부입니다.")]
    private bool hideAllWhenNoSelection = true;

    [SerializeField, Tooltip("선택 표시 갱신 디버그 로그를 출력할지 여부입니다.")]
    private bool enableDebugLogs = false;

    private int _currentCharacterId = int.MinValue;
    private float _nextRefreshTime;

    private void Awake()
    {
        ResolveRefs();
        ClampSettings();
    }

    private void OnEnable()
    {
        ResolveRefs();
        ForceRefresh();
    }

    private void Update()
    {
        if (refreshInterval > 0f && Time.time < _nextRefreshTime)
            return;

        _nextRefreshTime = Time.time + Mathf.Max(0f, refreshInterval);
        ForceRefresh();
    }

    public void ForceRefresh()
    {
        ResolveRefs();

        int selectedCharacterId = GetSelectedCharacterId();
        if (selectedCharacterId == _currentCharacterId)
            return;

        _currentCharacterId = selectedCharacterId;
        RefreshSelectionView(selectedCharacterId);
    }

    public void SetCharacterSelectionSystem(CharacterSelectionSystem system)
    {
        characterSelectionSystem = system;
        _currentCharacterId = int.MinValue;
        ForceRefresh();
    }

    private void ResolveRefs()
    {
        if (characterSelectionSystem == null)
            characterSelectionSystem = FindFirstObjectByType<CharacterSelectionSystem>();
    }

    private int GetSelectedCharacterId()
    {
        if (characterSelectionSystem == null)
            return -1;

        return characterSelectionSystem.LocalSelectedCharacterId;
    }

    private void RefreshSelectionView(int selectedCharacterId)
    {
        bool hasValidSelection = IsValidFrameIndex(selectedCharacterId);
        RefreshSelectedFrames(selectedCharacterId, hasValidSelection);
        RefreshCharacterButtons();
        RefreshSelectedCharacterName(selectedCharacterId, hasValidSelection);
        LogView($"선택 표시 갱신: {selectedCharacterId}");
    }

    private void RefreshSelectedFrames(int selectedCharacterId, bool hasValidSelection)
    {
        if (selectedFrames == null || (!hasValidSelection && !hideAllWhenNoSelection))
            return;

        for (int i = 0; i < selectedFrames.Length; i++)
        {
            GameObject frame = selectedFrames[i];
            if (frame == null)
                continue;

            bool shouldShow = hasValidSelection && i == selectedCharacterId;
            if (frame.activeSelf != shouldShow)
                frame.SetActive(shouldShow);
        }
    }

    private void RefreshCharacterButtons()
    {
        if (characterButtons == null)
            return;

        for (int i = 0; i < characterButtons.Length; i++)
        {
            Button button = characterButtons[i];
            if (button != null && button.targetGraphic != null)
                button.targetGraphic.SetAllDirty();
        }
    }

    private void RefreshSelectedCharacterName(int selectedCharacterId, bool hasValidSelection)
    {
        if (selectedCharacterNameText == null)
            return;

        if (!hasValidSelection)
        {
            selectedCharacterNameText.text = string.Empty;
            return;
        }

        selectedCharacterNameText.text = GetCharacterName(selectedCharacterId);
    }

    private string GetCharacterName(int characterId)
    {
        if (characterNames != null &&
            characterId >= 0 &&
            characterId < characterNames.Length &&
            !string.IsNullOrWhiteSpace(characterNames[characterId]))
        {
            return characterNames[characterId];
        }

        return $"캐릭터 {characterId}";
    }

    private bool IsValidFrameIndex(int characterId)
    {
        return selectedFrames != null &&
               characterId >= 0 &&
               characterId < selectedFrames.Length;
    }

    private void ClampSettings()
    {
        if (refreshInterval < 0f)
            refreshInterval = 0f;
    }

    private void LogView(string message)
    {
        if (!enableDebugLogs)
            return;

        Debug.Log($"[CharacterSelectionView] {message}", this);
    }

    private void OnValidate()
    {
        ClampSettings();
    }
}
