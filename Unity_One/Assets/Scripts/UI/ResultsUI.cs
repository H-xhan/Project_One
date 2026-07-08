using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class ResultsUI : MonoBehaviour
{
    private const string DefaultMissionResultLineFormat = "플레이어 {0} · {1} · {2}\n보상 +{3}코인 / 최종 코인 {4}\n{5}";
    private const string LegacyMissionResultLineFormat = "Player {0} | {1} | {2} | 보상 +{3} | 최종 코인 {4}\n{5}";

    [SerializeField, Tooltip("Results 상태에서 표시할 결과 패널입니다.")]
    private GameObject resultsPanel;

    [SerializeField, Tooltip("승자 또는 무승부 결과를 표시할 텍스트입니다.")]
    private TMP_Text resultText;

    [SerializeField, Tooltip("로비 복귀까지 남은 시간을 표시할 텍스트입니다. 비워두면 표시하지 않습니다.")]
    private TMP_Text countdownText;

    [SerializeField, Tooltip("결과 상태와 승자 정보를 제공하는 GameStateManager입니다. 비워두면 씬에서 자동 탐색합니다.")]
    private GameStateManager gameStateManager;

    [SerializeField, Tooltip("플레이어별 미션 결과를 표시할 TMP 텍스트입니다.")]
    private TMP_Text missionResultsText;

    [SerializeField, Tooltip("승자가 있을 때 표시할 문구입니다. {0}에는 winner client id가 들어갑니다.")]
    private string winnerTextFormat = "Player {0} 승리!";

    [SerializeField, Tooltip("무승부일 때 표시할 문구입니다.")]
    private string drawText = "무승부!";

    [SerializeField, Tooltip("결과 정보를 아직 읽지 못했을 때 표시할 문구입니다.")]
    private string waitingText = "결과 확인 중...";

    [SerializeField, Tooltip("Results 남은 시간을 표시할 문구입니다. {0}에는 초 단위 시간이 들어갑니다.")]
    private string returnCountdownFormat = "로비 복귀까지 {0}초";

    [SerializeField, Tooltip("Results UI를 갱신하는 간격입니다.")]
    private float refreshInterval = 0.1f;

#pragma warning disable 0414 // Legacy serialized Mission fields are intentionally retained but hidden at runtime.
    [SerializeField, Tooltip("Results 상태에서 미션 결과 상세를 표시할지 여부입니다.")]
    private bool showMissionResults = true;

    [SerializeField, Tooltip("미션 결과 목록 위에 표시할 제목입니다.")]
    private string missionResultsHeaderText = "미션 결과";

    [SerializeField, Tooltip("미션 결과 한 줄 표시 형식입니다. {0}=clientId, {1}=미션명, {2}=성공/실패, {3}=보상, {4}=최종 코인, {5}=사유입니다.")]
    private string missionResultLineFormat = DefaultMissionResultLineFormat;

    [SerializeField, Tooltip("미션 성공 시 표시할 문구입니다.")]
    private string missionSuccessText = "성공";

    [SerializeField, Tooltip("미션 실패 시 표시할 문구입니다.")]
    private string missionFailedText = "실패";

    [SerializeField, Tooltip("Results 상태지만 아직 미션 결과를 읽지 못했을 때 표시할 문구입니다.")]
    private string noMissionResultsText = string.Empty;

    [SerializeField, Tooltip("미션 이름을 알 수 없을 때 표시할 문구입니다.")]
    private string unknownMissionNameText = "알 수 없는 미션";

    [SerializeField, Tooltip("미션 결과 사유가 비어 있을 때 표시할 문구입니다.")]
    private string emptyReasonText = "결과 사유 없음";

    [SerializeField, Tooltip("미션 결과가 없을 때 missionResultsText를 숨길지 여부입니다.")]
    private bool hideMissionResultsWhenEmpty = false;
#pragma warning restore 0414

    private CanvasGroup targetCanvasGroup;
    private GraphicRaycaster targetGraphicRaycaster;
    private bool isSubscribedToStateChanges;
    private float nextRefreshTime;

    private void Awake()
    {
        ResolvePanelReferences();
        ResolveGameStateManager();
    }

    private void OnEnable()
    {
        ResolvePanelReferences();
        ResolveGameStateManager();
        SubscribeToStateChanges();
        ForceRefresh();
        nextRefreshTime = Time.unscaledTime + Mathf.Max(0f, refreshInterval);
    }

    private void OnDisable()
    {
        UnsubscribeFromStateChanges();
    }

    private void OnDestroy()
    {
        UnsubscribeFromStateChanges();
    }

    private void Update()
    {
        float interval = Mathf.Max(0f, refreshInterval);
        if (Time.unscaledTime < nextRefreshTime)
            return;

        nextRefreshTime = Time.unscaledTime + interval;

        if (gameStateManager == null)
        {
            ResolveGameStateManager();
            SubscribeToStateChanges();
        }

        ForceRefresh();
    }

    public void ForceRefresh()
    {
        if (gameStateManager == null)
            ResolveGameStateManager();

        bool shouldShow = gameStateManager != null &&
            gameStateManager.GetState() == GameStateManager.GameState.Results;

        ApplyVisibility(shouldShow);

        if (!shouldShow)
        {
            ClearMissionResults();
            return;
        }

        RefreshResultText();
        RefreshCountdownText();
        RefreshMissionResults();
    }

    public void RebindGameStateManager(GameStateManager manager)
    {
        UnsubscribeFromStateChanges();
        gameStateManager = manager;

        if (gameStateManager == null)
            ResolveGameStateManager();

        SubscribeToStateChanges();
        ForceRefresh();
    }

    private void ResolvePanelReferences()
    {
        GameObject targetObject = GetTargetPanelObject();
        if (targetObject == null)
            return;

        if (targetCanvasGroup == null)
        {
            targetCanvasGroup = targetObject.GetComponent<CanvasGroup>();
        }

        if (targetCanvasGroup == null && targetObject == gameObject)
        {
            targetCanvasGroup = targetObject.AddComponent<CanvasGroup>();
        }

        if (targetGraphicRaycaster == null)
        {
            targetGraphicRaycaster = targetObject.GetComponent<GraphicRaycaster>();
        }
    }

    private void ResolveGameStateManager()
    {
        if (gameStateManager == null)
            gameStateManager = FindFirstObjectByType<GameStateManager>();
    }

    private void SubscribeToStateChanges()
    {
        if (isSubscribedToStateChanges || gameStateManager == null || gameStateManager.StateValue == null)
            return;

        gameStateManager.StateValue.OnValueChanged += HandleStateValueChanged;
        isSubscribedToStateChanges = true;
    }

    private void UnsubscribeFromStateChanges()
    {
        if (!isSubscribedToStateChanges)
            return;

        if (gameStateManager != null && gameStateManager.StateValue != null)
        {
            gameStateManager.StateValue.OnValueChanged -= HandleStateValueChanged;
        }

        isSubscribedToStateChanges = false;
    }

    private void HandleStateValueChanged(int previousValue, int currentValue)
    {
        ForceRefresh();
    }

    private void RefreshResultText()
    {
        if (resultText == null || gameStateManager == null)
            return;

        if (gameStateManager.HasRoundWinner &&
            gameStateManager.TryGetWinnerClientId(out ulong winnerClientId))
        {
            resultText.text = string.Format(winnerTextFormat, winnerClientId);
            return;
        }

        if (gameStateManager.IsRoundDraw)
        {
            resultText.text = drawText;
            return;
        }

        resultText.text = waitingText;
    }

    private void RefreshCountdownText()
    {
        if (countdownText == null || gameStateManager == null)
            return;

        int remainingSeconds = Mathf.CeilToInt(Mathf.Max(0f, gameStateManager.StateTimer.Value));
        countdownText.text = string.Format(returnCountdownFormat, remainingSeconds);
    }

    private void RefreshMissionResults()
    {
        ClearMissionResults();
    }

    private void ClearMissionResults()
    {
        if (missionResultsText != null)
            missionResultsText.text = string.Empty;

        SetMissionResultsVisible(false);
    }

    private void SetMissionResultsVisible(bool visible)
    {
        if (missionResultsText == null)
            return;

        missionResultsText.enabled = visible;

        GameObject textObject = missionResultsText.gameObject;
        GameObject targetPanelObject = GetTargetPanelObject();
        if (textObject == gameObject || textObject == targetPanelObject)
            return;

        textObject.SetActive(visible);
    }

    private void ApplyVisibility(bool visible)
    {
        GameObject targetObject = GetTargetPanelObject();
        if (targetObject == null)
            return;

        if (targetObject != gameObject || visible)
        {
            targetObject.SetActive(visible);
        }

        if (targetCanvasGroup != null)
        {
            targetCanvasGroup.alpha = visible ? 1f : 0f;
            targetCanvasGroup.interactable = visible;
            targetCanvasGroup.blocksRaycasts = visible;
        }

        if (targetGraphicRaycaster != null)
        {
            targetGraphicRaycaster.enabled = visible;
        }
    }

    private GameObject GetTargetPanelObject()
    {
        return resultsPanel != null ? resultsPanel : gameObject;
    }

    private void OnValidate()
    {
        refreshInterval = Mathf.Max(0f, refreshInterval);

        if (string.IsNullOrWhiteSpace(missionResultsHeaderText))
            missionResultsHeaderText = "미션 결과";

        if (string.IsNullOrWhiteSpace(missionResultLineFormat))
            missionResultLineFormat = DefaultMissionResultLineFormat;

        if (string.IsNullOrWhiteSpace(missionSuccessText))
            missionSuccessText = "성공";

        if (string.IsNullOrWhiteSpace(missionFailedText))
            missionFailedText = "실패";

        if (string.IsNullOrWhiteSpace(unknownMissionNameText))
            unknownMissionNameText = "알 수 없는 미션";

        if (string.IsNullOrWhiteSpace(emptyReasonText))
            emptyReasonText = "결과 사유 없음";
    }
}
