using TMPro;
using UnityEngine;

public class MatchTimerHUD : MonoBehaviour
{
    [SerializeField, Tooltip("남은 플레이 시간을 표시할 TMP 텍스트입니다. 비워두면 자식에서 자동 탐색합니다.")]
    private TMP_Text timerText;

    [SerializeField, Tooltip("남은 플레이 시간을 제공하는 매치 매니저입니다. 비워두면 씬에서 자동 탐색합니다.")]
    private InGameMatchManager matchManager;

    [SerializeField, Tooltip("현재 게임 상태를 읽을 상태 매니저입니다. 비워두면 씬에서 자동 탐색합니다.")]
    private GameStateManager gameStateManager;

    [SerializeField, Tooltip("남은 시간 앞에 표시할 접두어입니다.")]
    private string timerPrefix = "남은 시간 ";

    [SerializeField, Tooltip("남은 시간 정보를 찾지 못했을 때 표시할 문구입니다.")]
    private string missingTimerText = "남은 시간 --:--";

    [SerializeField, Tooltip("Playing 상태일 때만 남은 시간을 표시할지 여부입니다.")]
    private bool showOnlyDuringPlaying = true;

    [SerializeField, Tooltip("Countdown 상태에서도 남은 시간을 표시할지 여부입니다.")]
    private bool showDuringCountdown = false;

    [SerializeField, Tooltip("시간 정보를 찾지 못했을 때 텍스트 오브젝트를 숨길지 여부입니다.")]
    private bool hideTextWhenUnavailable = false;

    [SerializeField, Tooltip("남은 시간 표시를 갱신하는 간격입니다.")]
    private float refreshInterval = 0.1f;

    private float _nextRefreshTime;

    private void Awake()
    {
        ResolveTextReference();
    }

    private void OnEnable()
    {
        RebindSources();
        ForceRefresh();
        _nextRefreshTime = Time.unscaledTime + Mathf.Max(0f, refreshInterval);
    }

    private void Update()
    {
        float interval = Mathf.Max(0f, refreshInterval);
        if (Time.unscaledTime < _nextRefreshTime)
        {
            return;
        }

        _nextRefreshTime = Time.unscaledTime + interval;
        RefreshTimer();
    }

    public void ForceRefresh()
    {
        RefreshTimer();
    }

    public void RebindSources()
    {
        ResolveTextReference();

        if (matchManager == null)
        {
            matchManager = FindFirstObjectByType<InGameMatchManager>();
        }

        if (gameStateManager == null)
        {
            gameStateManager = FindFirstObjectByType<GameStateManager>();
        }
    }

    public void SetMatchManager(InGameMatchManager manager)
    {
        matchManager = manager;
        ForceRefresh();
    }

    private void ResolveTextReference()
    {
        if (timerText == null)
        {
            timerText = GetComponentInChildren<TMP_Text>(true);
        }
    }

    private void RefreshTimer()
    {
        if (timerText == null)
        {
            return;
        }

        if (gameStateManager == null)
        {
            gameStateManager = FindFirstObjectByType<GameStateManager>();
        }

        if (!ShouldShowTimerForCurrentState())
        {
            SetTimerTextVisible(false);
            return;
        }

        if (!TryGetRemainingTime(out float remainingSeconds))
        {
            ShowMissingTimerState();
            return;
        }

        SetTimerTextVisible(true);
        timerText.text = timerPrefix + FormatTime(remainingSeconds);
    }

    private bool ShouldShowTimerForCurrentState()
    {
        if (gameStateManager == null)
        {
            return true;
        }

        GameStateManager.GameState state = gameStateManager.GetState();
        switch (state)
        {
            case GameStateManager.GameState.Playing:
                return true;
            case GameStateManager.GameState.Countdown:
                return showDuringCountdown;
            case GameStateManager.GameState.Lobby:
            case GameStateManager.GameState.Results:
                return false;
            default:
                return !showOnlyDuringPlaying;
        }
    }

    private bool TryGetRemainingTime(out float remainingSeconds)
    {
        remainingSeconds = 0f;

        if (gameStateManager == null || gameStateManager.StateTimer == null)
        {
            return false;
        }

        remainingSeconds = Mathf.Max(0f, gameStateManager.StateTimer.Value);
        return true;
    }

    private string FormatTime(float seconds)
    {
        int totalSeconds = Mathf.Max(0, Mathf.CeilToInt(seconds));
        int minutes = totalSeconds / 60;
        int remainingSeconds = totalSeconds % 60;
        return $"{minutes:00}:{remainingSeconds:00}";
    }

    private void ShowMissingTimerState()
    {
        if (timerText == null)
        {
            return;
        }

        if (hideTextWhenUnavailable)
        {
            SetTimerTextVisible(false);
            return;
        }

        SetTimerTextVisible(true);
        timerText.text = missingTimerText;
    }

    private void SetTimerTextVisible(bool visible)
    {
        if (timerText == null)
        {
            return;
        }

        if (timerText.gameObject == gameObject)
        {
            timerText.enabled = visible;
            return;
        }

        timerText.gameObject.SetActive(visible);
    }

    private void OnValidate()
    {
        refreshInterval = Mathf.Max(0f, refreshInterval);
    }
}
