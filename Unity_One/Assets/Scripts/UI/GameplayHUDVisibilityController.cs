using UnityEngine;
using UnityEngine.UI;

public class GameplayHUDVisibilityController : MonoBehaviour
{
    [SerializeField, Tooltip("표시를 제어할 플레이 전용 HUD Canvas입니다. 비워두면 같은 오브젝트에서 자동 탐색합니다.")]
    private Canvas targetCanvas;

    [SerializeField, Tooltip("HUD 표시/숨김을 부드럽고 안전하게 제어할 CanvasGroup입니다. 비워두면 같은 오브젝트에서 자동 탐색하거나 추가합니다.")]
    private CanvasGroup targetCanvasGroup;

    [SerializeField, Tooltip("HUD가 숨겨졌을 때 UI 입력 차단을 막기 위한 GraphicRaycaster입니다. 비워두면 같은 오브젝트에서 자동 탐색합니다.")]
    private GraphicRaycaster targetGraphicRaycaster;

    [SerializeField, Tooltip("게임 상태를 읽을 상태 매니저입니다. 비워두면 씬에서 자동 탐색합니다.")]
    private GameStateManager gameStateManager;

    [SerializeField, Tooltip("게임 상태를 확인하기 전까지 플레이 HUD를 기본 숨김 상태로 둘지 여부입니다.")]
    private bool hideOnAwakeUntilGameplayState = true;

    [SerializeField, Tooltip("Countdown 상태에서 플레이 HUD를 표시할지 여부입니다.")]
    private bool visibleDuringCountdown = false;

    [SerializeField, Tooltip("Playing 상태에서 플레이 HUD를 표시할지 여부입니다.")]
    private bool visibleDuringPlaying = true;

    [SerializeField, Tooltip("상태 매니저를 찾지 못했을 때 HUD를 숨길지 여부입니다.")]
    private bool hideWhenStateUnavailable = true;

    [SerializeField, Tooltip("상태 변경 이벤트를 사용할 수 없을 때 상태를 다시 확인하는 간격입니다.")]
    private float pollingInterval = 0.1f;

    private bool isVisible;
    private bool isSubscribedToStateChanges;
    private float nextPollTime;

    public bool IsVisible => isVisible;

    private void Awake()
    {
        ResolveHudReferences();
        if (hideOnAwakeUntilGameplayState)
        {
            ApplyVisibility(false);
        }
        else
        {
            isVisible = ReadCurrentVisibility();
        }

        ResolveGameStateManager();
    }

    private void OnEnable()
    {
        ResolveHudReferences();
        if (hideOnAwakeUntilGameplayState)
        {
            ApplyVisibility(false);
        }

        ResolveGameStateManager();
        SubscribeToStateChanges();
        ForceRefresh();
    }

    private void OnDisable()
    {
        UnsubscribeFromStateChanges();
    }

    private void Update()
    {
        if (isSubscribedToStateChanges)
        {
            return;
        }

        float interval = Mathf.Max(0f, pollingInterval);
        if (Time.unscaledTime < nextPollTime)
        {
            return;
        }

        nextPollTime = Time.unscaledTime + interval;
        ResolveGameStateManager();
        SubscribeToStateChanges();
        ForceRefresh();
    }

    public void ForceRefresh()
    {
        if (gameStateManager == null)
        {
            ResolveGameStateManager();
        }

        if (gameStateManager == null)
        {
            if (hideWhenStateUnavailable)
            {
                ApplyVisibility(false);
            }
            else
            {
                isVisible = ReadCurrentVisibility();
            }

            return;
        }

        ApplyVisibility(ShouldShowForState(gameStateManager.GetState()));
    }

    public void SetVisibleForTest(bool visible)
    {
        ApplyVisibility(visible);
    }

    public void RebindGameStateManager()
    {
        UnsubscribeFromStateChanges();
        gameStateManager = null;
        ResolveGameStateManager();
        SubscribeToStateChanges();
        ForceRefresh();
    }

    private void ResolveHudReferences()
    {
        if (targetCanvas == null)
        {
            targetCanvas = GetComponent<Canvas>();
        }

        if (targetCanvasGroup == null)
        {
            targetCanvasGroup = GetComponent<CanvasGroup>();
        }

        if (targetCanvasGroup == null)
        {
            targetCanvasGroup = gameObject.AddComponent<CanvasGroup>();
        }

        if (targetGraphicRaycaster == null)
        {
            targetGraphicRaycaster = GetComponent<GraphicRaycaster>();
        }
    }

    private void ResolveGameStateManager()
    {
        if (gameStateManager == null)
        {
            gameStateManager = FindFirstObjectByType<GameStateManager>();
        }
    }

    private void SubscribeToStateChanges()
    {
        if (isSubscribedToStateChanges || gameStateManager == null || gameStateManager.StateValue == null)
        {
            return;
        }

        gameStateManager.StateValue.OnValueChanged += HandleStateValueChanged;
        isSubscribedToStateChanges = true;
    }

    private void UnsubscribeFromStateChanges()
    {
        if (!isSubscribedToStateChanges)
        {
            return;
        }

        if (gameStateManager != null && gameStateManager.StateValue != null)
        {
            gameStateManager.StateValue.OnValueChanged -= HandleStateValueChanged;
        }

        isSubscribedToStateChanges = false;
    }

    private void HandleStateValueChanged(int previousValue, int currentValue)
    {
        ApplyVisibility(ShouldShowForState((GameStateManager.GameState)currentValue));
    }

    private bool ShouldShowForState(GameStateManager.GameState state)
    {
        switch (state)
        {
            case GameStateManager.GameState.Countdown:
                return visibleDuringCountdown;
            case GameStateManager.GameState.Playing:
                return visibleDuringPlaying;
            case GameStateManager.GameState.Lobby:
            case GameStateManager.GameState.Results:
            default:
                return false;
        }
    }

    private void ApplyVisibility(bool visible)
    {
        isVisible = visible;

        if (targetCanvas != null)
        {
            targetCanvas.enabled = visible;
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

    private bool ReadCurrentVisibility()
    {
        if (targetCanvas != null)
        {
            return targetCanvas.enabled;
        }

        if (targetCanvasGroup != null)
        {
            return targetCanvasGroup.alpha > 0f &&
                targetCanvasGroup.interactable &&
                targetCanvasGroup.blocksRaycasts;
        }

        if (targetGraphicRaycaster != null)
        {
            return targetGraphicRaycaster.enabled;
        }

        return isActiveAndEnabled;
    }

    private void OnValidate()
    {
        pollingInterval = Mathf.Max(0f, pollingInterval);
    }
}
