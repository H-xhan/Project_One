using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class ResultsUI : MonoBehaviour
{
    [SerializeField, Tooltip("Results 상태에서 표시할 결과 패널입니다.")]
    private GameObject resultsPanel;

    [SerializeField, Tooltip("승자 또는 무승부 결과를 표시할 텍스트입니다.")]
    private TMP_Text resultText;

    [SerializeField, Tooltip("로비 복귀까지 남은 시간을 표시할 텍스트입니다. 비워두면 표시하지 않습니다.")]
    private TMP_Text countdownText;

    [SerializeField, Tooltip("결과 상태와 승자 정보를 제공하는 GameStateManager입니다. 비워두면 씬에서 자동 탐색합니다.")]
    private GameStateManager gameStateManager;

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
            return;

        RefreshResultText();
        RefreshCountdownText();
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
    }
}
