using UnityEngine;
using UnityEngine.UI;

public class CharacterSelectionConfirmUI : MonoBehaviour
{
    [SerializeField, Tooltip("확인 버튼을 눌렀을 때 숨길 캐릭터 선택 패널입니다.")]
    private GameObject characterSelectPanel;

    [SerializeField, Tooltip("캐릭터 선택창을 닫는 확인 버튼입니다. 비워두면 같은 오브젝트나 자식에서 자동 탐색합니다.")]
    private Button confirmButton;

    [SerializeField, Tooltip("캐릭터가 선택되어 있어야만 선택창을 닫을지 여부입니다.")]
    private bool requireSelectionBeforeClose = true;

    [SerializeField, Tooltip("선택 여부를 확인할 캐릭터 선택 시스템입니다. 비워두면 씬에서 자동 탐색합니다.")]
    private CharacterSelectionSystem characterSelectionSystem;

    [SerializeField, Tooltip("패널을 비활성화하지 않고 CanvasGroup으로 숨길지 여부입니다.")]
    private bool hideInsteadOfDisable = false;

    [SerializeField, Tooltip("hideInsteadOfDisable이 켜졌을 때 사용할 CanvasGroup입니다. 비워두면 패널에서 자동 탐색하거나 추가합니다.")]
    private CanvasGroup targetCanvasGroup;

    private bool _listenerRegistered;

    private void Awake()
    {
        ResolveRefs();
        ResolveCanvasGroupIfNeeded();
    }

    private void OnEnable()
    {
        ResolveRefs();
        ResolveCanvasGroupIfNeeded();
        RegisterButtonListener();
    }

    private void OnDisable()
    {
        UnregisterButtonListener();
    }

    public void ShowPanel()
    {
        ResolvePanelIfNeeded();
        ResolveCanvasGroupIfNeeded();

        if (characterSelectPanel == null)
            return;

        if (!characterSelectPanel.activeSelf)
            characterSelectPanel.SetActive(true);

        if (hideInsteadOfDisable && targetCanvasGroup != null)
        {
            targetCanvasGroup.alpha = 1f;
            targetCanvasGroup.interactable = true;
            targetCanvasGroup.blocksRaycasts = true;
        }
    }

    public void HidePanel()
    {
        ResolvePanelIfNeeded();
        ResolveCanvasGroupIfNeeded();

        if (characterSelectPanel == null)
            return;

        if (hideInsteadOfDisable)
        {
            if (targetCanvasGroup == null)
                return;

            targetCanvasGroup.alpha = 0f;
            targetCanvasGroup.interactable = false;
            targetCanvasGroup.blocksRaycasts = false;
            return;
        }

        characterSelectPanel.SetActive(false);
    }

    public void OnConfirmClicked()
    {
        ResolveRefs();

        if (requireSelectionBeforeClose && !HasSelection())
            return;

        HidePanel();
    }

    private void ResolveRefs()
    {
        ResolvePanelIfNeeded();

        if (confirmButton == null)
            confirmButton = GetComponent<Button>();

        if (confirmButton == null)
            confirmButton = GetComponentInChildren<Button>(true);

        if (characterSelectionSystem == null)
            characterSelectionSystem = FindFirstObjectByType<CharacterSelectionSystem>();
    }

    private void ResolvePanelIfNeeded()
    {
        if (characterSelectPanel != null)
            return;

        Transform current = transform;
        while (current != null)
        {
            if (current.name == "CharacterSelectPanel")
            {
                characterSelectPanel = current.gameObject;
                return;
            }

            current = current.parent;
        }

        characterSelectPanel = gameObject;
    }

    private void ResolveCanvasGroupIfNeeded()
    {
        if (!hideInsteadOfDisable)
            return;

        ResolvePanelIfNeeded();

        if (characterSelectPanel == null)
            return;

        if (targetCanvasGroup == null)
            targetCanvasGroup = characterSelectPanel.GetComponent<CanvasGroup>();

        if (targetCanvasGroup == null)
            targetCanvasGroup = characterSelectPanel.AddComponent<CanvasGroup>();
    }

    private void RegisterButtonListener()
    {
        if (_listenerRegistered || confirmButton == null)
            return;

        confirmButton.onClick.AddListener(OnConfirmClicked);
        _listenerRegistered = true;
    }

    private void UnregisterButtonListener()
    {
        if (!_listenerRegistered || confirmButton == null)
        {
            _listenerRegistered = false;
            return;
        }

        confirmButton.onClick.RemoveListener(OnConfirmClicked);
        _listenerRegistered = false;
    }

    private bool HasSelection()
    {
        return characterSelectionSystem != null &&
               characterSelectionSystem.LocalSelectedCharacterId >= 0;
    }
}
