using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class RoundUI : MonoBehaviour
{
    [SerializeField, Tooltip("ReadySystem 참조")]
    private ReadySystem readySystem;

    [SerializeField, Tooltip("GameStateManager 참조")]
    private GameStateManager gameStateManager;

    [Header("UI")]
    [SerializeField, Tooltip("레디 현황 텍스트")]
    private TMP_Text readyText;

    [SerializeField, Tooltip("카운트다운 남은 시간을 숫자로 표시할 TMP 텍스트입니다. 비워두면 TimerText 이름의 자식에서 자동 탐색합니다.")]
    private TMP_Text timerText;

    [SerializeField, Tooltip("준비/카운트다운 상태 안내 문구를 표시할 TMP 텍스트입니다. 비워두면 StateText 이름의 자식에서 자동 탐색합니다.")]
    private TMP_Text stateText;

    [SerializeField, Tooltip("레디 버튼")]
    private Button readyButton;

    [SerializeField, Tooltip("방 코드를 표시할 텍스트")]
    private TMP_Text roomCodeText;

    [SerializeField, Tooltip("canonical 방 설정을 읽기 전용으로 표시할 텍스트")]
    private TMP_Text roomSettingsText;

    private bool _readyButtonListenerRegistered;

    private void Awake()
    {
        ResolveRefs();
        RegisterReadyButtonListener();
    }

    private void OnEnable()
    {
        ResolveRefs();
        Refresh();
    }

    private void OnDestroy()
    {
        if (_readyButtonListenerRegistered && readyButton != null)
            readyButton.onClick.RemoveListener(OnClickReady);
    }

    private void Update()
    {
        Refresh();
    }

    private void ResolveRefs()
    {
        if (readySystem == null)
            readySystem = FindFirstObjectByType<ReadySystem>();

        if (gameStateManager == null)
            gameStateManager = FindFirstObjectByType<GameStateManager>();

        if (readyText == null)
            readyText = FindChildTextByName("ReadyText");

        if (timerText == null)
            timerText = FindChildTextByName("TimerText");

        if (stateText == null)
            stateText = FindChildTextByName("StateText");

        if (roomCodeText == null)
            roomCodeText = FindChildTextByName("RoomCodeText");

        if (roomSettingsText == null)
            roomSettingsText = FindChildTextByName("RoomSettingsText");
    }

    private void RegisterReadyButtonListener()
    {
        if (_readyButtonListenerRegistered || readyButton == null)
            return;

        readyButton.onClick.AddListener(OnClickReady);
        _readyButtonListenerRegistered = true;
    }

    private TMP_Text FindChildTextByName(string childName)
    {
        TMP_Text[] texts = GetComponentsInChildren<TMP_Text>(true);
        for (int i = 0; i < texts.Length; i++)
        {
            TMP_Text text = texts[i];
            if (text != null && text.name == childName)
                return text;
        }

        return null;
    }

    private void Refresh()
    {
        UpdateReadyText();
        UpdateTimerText();
        UpdateStateText();
        UpdateRoomCodeText();
        UpdateRoomSettingsText();
    }

    private void UpdateReadyText()
    {
        if (readySystem == null || readyText == null)
            return;

        int ready = readySystem.GetReadyCount();
        int total = readySystem.GetConnectedClientCount();
        readyText.text = $"{ready}/{total} Ready";
    }

    private void UpdateTimerText()
    {
        if (timerText == null)
            return;

        if (gameStateManager == null)
        {
            timerText.text = "--";
            return;
        }

        GameStateManager.GameState state = gameStateManager.GetState();
        if (state != GameStateManager.GameState.Countdown)
        {
            timerText.text = state == GameStateManager.GameState.Lobby ? "--" : string.Empty;
            return;
        }

        int remainingSeconds = Mathf.CeilToInt(Mathf.Max(0f, gameStateManager.StateTimer.Value));
        timerText.text = remainingSeconds.ToString();
    }

    private void UpdateStateText()
    {
        if (stateText == null)
            return;

        if (gameStateManager == null)
        {
            stateText.text = "플레이어를 기다리는 중";
            return;
        }

        switch (gameStateManager.GetState())
        {
            case GameStateManager.GameState.Lobby:
                stateText.text = GetLobbyStateMessage();
                break;
            case GameStateManager.GameState.Countdown:
                stateText.text = "곧 시작합니다";
                break;
            case GameStateManager.GameState.Playing:
            case GameStateManager.GameState.Results:
            default:
                stateText.text = string.Empty;
                break;
        }
    }

    private string GetLobbyStateMessage()
    {
        if (readySystem == null)
            return "플레이어를 기다리는 중";

        int ready = readySystem.GetReadyCount();
        int total = readySystem.GetConnectedClientCount();

        if (total <= 0)
            return "플레이어를 기다리는 중";

        if (!readySystem.IsLocalReady())
            return "준비를 눌러주세요";

        if (ready >= total)
            return total <= 1 ? "플레이어를 기다리는 중" : "모두 준비 완료";

        return "플레이어를 기다리는 중";
    }

    private void UpdateRoomCodeText()
    {
        if (roomCodeText == null)
            return;

        string lobbyCode = "----";

        if (LobbyManager.Instance != null)
        {
            var lobby = LobbyManager.Instance.GetHostLobby();

            if (lobby != null && !string.IsNullOrWhiteSpace(lobby.LobbyCode))
                lobbyCode = lobby.LobbyCode;
        }

        roomCodeText.text = lobbyCode;
    }

    private void UpdateRoomSettingsText()
    {
        if (roomSettingsText == null)
            return;

        RoomGameplaySettingsSnapshot snapshot = LobbyManager.Instance != null
            ? LobbyManager.Instance.CanonicalRoomSettings
            : RoomGameplaySettingsValidator.CreateDefaultSnapshot();
        string promptMode = snapshot.PostItLiar.PromptSourceMode ==
                            PostItLiarPromptSourceMode.CitizenAuthor
            ? "시민 직접 출제"
            : "기본 주제";

        roomSettingsText.text = $"주제 방식: {promptMode}";
    }

    private void OnClickReady()
    {
        if (readySystem == null)
            return;

        readySystem.ToggleLocalReady();
    }
}
