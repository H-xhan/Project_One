using TMPro;
using UnityEngine;

public class MissionHUD : MonoBehaviour
{
    [SerializeField, Tooltip("로컬 미션 정보를 제공하는 RoundMissionManager입니다. 비워두면 씬에서 자동 탐색합니다.")]
    private RoundMissionManager roundMissionManager;

    [SerializeField, Tooltip("미션 HUD 전체 루트 오브젝트입니다. 비워두면 자기 gameObject를 사용합니다.")]
    private GameObject hudRoot;

    [SerializeField, Tooltip("미션 HUD 표시/숨김에 사용할 CanvasGroup입니다. 비워두면 hudRoot에서 자동 탐색하거나 추가합니다.")]
    private CanvasGroup canvasGroup;

    [SerializeField, Tooltip("미션 이름을 표시할 텍스트입니다.")]
    private TMP_Text missionTitleText;

    [SerializeField, Tooltip("미션 조건 설명을 표시할 텍스트입니다.")]
    private TMP_Text missionDescriptionText;

    [SerializeField, Tooltip("미션 성공 보상 코인을 표시할 텍스트입니다.")]
    private TMP_Text missionRewardText;

    [SerializeField, Tooltip("미션이 아직 없을 때 표시할 문구입니다.")]
    private string emptyMissionText = "미션 대기 중...";

    [SerializeField, Tooltip("미션 보상 문구 형식입니다. {0}에는 보상 코인이 들어갑니다.")]
    private string rewardTextFormat = "보상: +{0} 코인";

    [SerializeField, Tooltip("로컬 미션이 없을 때 HUD를 숨길지 여부입니다.")]
    private bool hideWhenNoMission = true;

    [SerializeField, Tooltip("시작 시 이미 로컬 미션이 있으면 즉시 표시할지 여부입니다.")]
    private bool showOnStartIfMissionExists = true;

    private bool _isSubscribed;

    private void Awake()
    {
        ResolveRefs();
        ResolveHudRoot();
        ResolveCanvasGroup();
    }

    private void Start()
    {
        if (showOnStartIfMissionExists)
            ForceRefresh();
    }

    private void OnEnable()
    {
        ResolveRefs();
        ResolveHudRoot();
        ResolveCanvasGroup();
        Subscribe();

        ForceRefresh();
    }

    private void OnDisable()
    {
        Unsubscribe();
    }

    private void OnDestroy()
    {
        Unsubscribe();
    }

    public void ForceRefresh()
    {
        ResolveRefs();

        if (roundMissionManager != null &&
            roundMissionManager.TryGetLocalMissionAssignment(out MissionAssignment assignment))
        {
            ApplyMission(assignment);
            return;
        }

        ApplyNoMission();
    }

    public void Show()
    {
        ApplyVisibility(true);
    }

    public void Hide()
    {
        ApplyVisibility(false);
    }

    private void ResolveRefs()
    {
        if (roundMissionManager == null)
            roundMissionManager = FindFirstObjectByType<RoundMissionManager>();
    }

    private void ResolveHudRoot()
    {
        if (hudRoot == null)
            hudRoot = gameObject;
    }

    private void ResolveCanvasGroup()
    {
        if (hudRoot == null)
            return;

        if (canvasGroup == null)
            canvasGroup = hudRoot.GetComponent<CanvasGroup>();

        if (canvasGroup == null)
            canvasGroup = hudRoot.AddComponent<CanvasGroup>();
    }

    private void Subscribe()
    {
        if (_isSubscribed || roundMissionManager == null)
            return;

        roundMissionManager.LocalMissionAssignmentChanged += HandleLocalMissionAssignmentChanged;
        _isSubscribed = true;
    }

    private void Unsubscribe()
    {
        if (!_isSubscribed)
            return;

        if (roundMissionManager != null)
            roundMissionManager.LocalMissionAssignmentChanged -= HandleLocalMissionAssignmentChanged;

        _isSubscribed = false;
    }

    private void HandleLocalMissionAssignmentChanged(MissionAssignment assignment)
    {
        ForceRefresh();
    }

    private void ApplyMission(MissionAssignment assignment)
    {
        if (missionTitleText != null)
            missionTitleText.text = string.IsNullOrWhiteSpace(assignment.displayName) ? "비밀 미션" : assignment.displayName;

        if (missionDescriptionText != null)
            missionDescriptionText.text = string.IsNullOrWhiteSpace(assignment.description) ? emptyMissionText : assignment.description;

        if (missionRewardText != null)
            missionRewardText.text = rewardTextFormat.Replace("{0}", assignment.rewardCoins.ToString());

        Show();
    }

    private void ApplyNoMission()
    {
        if (hideWhenNoMission)
        {
            Hide();
            return;
        }

        if (missionTitleText != null)
            missionTitleText.text = emptyMissionText;

        if (missionDescriptionText != null)
            missionDescriptionText.text = string.Empty;

        if (missionRewardText != null)
            missionRewardText.text = string.Empty;

        Show();
    }

    private void ApplyVisibility(bool visible)
    {
        ResolveHudRoot();
        ResolveCanvasGroup();

        if (hudRoot != null && !hudRoot.activeSelf)
            hudRoot.SetActive(true);

        if (canvasGroup != null)
        {
            canvasGroup.alpha = visible ? 1f : 0f;
            canvasGroup.interactable = visible;
            canvasGroup.blocksRaycasts = visible;
            return;
        }

        if (hudRoot != null)
            hudRoot.SetActive(visible);
    }

    private void OnValidate()
    {
        if (string.IsNullOrWhiteSpace(emptyMissionText))
            emptyMissionText = "미션 대기 중...";

        if (string.IsNullOrWhiteSpace(rewardTextFormat))
            rewardTextFormat = "보상: +{0} 코인";
    }
}
