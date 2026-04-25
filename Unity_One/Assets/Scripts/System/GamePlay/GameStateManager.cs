using Unity.Netcode;
using UnityEngine;

public class GameStateManager : NetworkBehaviour
{
    public enum GameState
    {
        Lobby = 0,
        Countdown = 1,
        Playing = 2,
        Results = 3
    }

    [Header("Refs")]
    [SerializeField, Tooltip("ReadySystem 참조(없으면 자동 탐색)")]
    private ReadySystem readySystem;

    [SerializeField, Tooltip("로비/게임 존 텔레포트를 담당하는 매니저(없으면 자동 탐색)")]
    private InGameMatchManager inGameMatchManager;

    [Header("시간 설정")]
    [SerializeField, Tooltip("카운트다운 시간(초)")]
    private float countdownSeconds = 3f;

    [SerializeField, Tooltip("플레이 시간(초)")]
    private float playSeconds = 120f;

    [SerializeField, Tooltip("결과 시간(초)")]
    private float resultsSeconds = 6f;

    [Header("동작 옵션")]
    [SerializeField, Tooltip("Results 후 Lobby로 자동 복귀")]
    private bool autoReturnToLobby = true;

    [SerializeField, Tooltip("Lobby 진입 시 플레이어를 로비 존으로 다시 보낼지 여부")]
    private bool teleportPlayersOnEnterLobby = true;

    [SerializeField, Tooltip("Playing 진입 시 플레이어를 게임 존으로 보낼지 여부")]
    private bool teleportPlayersOnEnterPlaying = true;

    [Header("Debug")]
    [SerializeField, Tooltip("디버그 로그 출력 여부입니다.")]
    private bool enableDebugLogs = false;

    [Tooltip("현재 게임 상태를 네트워크로 동기화하는 값입니다.")]
    public NetworkVariable<int> StateValue = new NetworkVariable<int>((int)GameState.Lobby);

    [Tooltip("현재 게임 상태의 남은 시간을 네트워크로 동기화하는 값입니다.")]
    public NetworkVariable<float> StateTimer = new NetworkVariable<float>(0f);

    public GameState GetState()
    {
        return (GameState)StateValue.Value;
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        ResolveRefs();

        if (!IsServer) return;

        EnterLobby(true);
    }

    private void ResolveRefs()
    {
        if (readySystem == null)
            readySystem = FindFirstObjectByType<ReadySystem>();

        if (inGameMatchManager == null)
            inGameMatchManager = FindFirstObjectByType<InGameMatchManager>();
    }

    private void Update()
    {
        if (!IsServer) return;

        ResolveRefs();

        var state = GetState();

        if (state == GameState.Lobby)
        {
            if (readySystem != null && readySystem.CanStartGameServer() && readySystem.AreAllReady())
            {
                readySystem.ResetAllReadyServer();
                EnterCountdown();
            }

            return;
        }

        float timer = StateTimer.Value;
        if (timer > 0f)
        {
            timer -= Time.deltaTime;
            if (timer < 0f) timer = 0f;
            StateTimer.Value = timer;
        }

        if (StateTimer.Value > 0f)
            return;

        if (state == GameState.Countdown)
        {
            EnterPlaying();
        }
        else if (state == GameState.Playing)
        {
            EnterResults();
        }
        else if (state == GameState.Results && autoReturnToLobby)
        {
            EnterLobby(false);
        }
    }

    private void EnterLobby(bool isInitialSpawn)
    {
        StateValue.Value = (int)GameState.Lobby;
        StateTimer.Value = 0f;

        if (readySystem != null)
            readySystem.ResetAllReadyServer();

        if (!isInitialSpawn && teleportPlayersOnEnterLobby && inGameMatchManager != null && inGameMatchManager.IsSpawned)
        {
            inGameMatchManager.TeleportPlayersToLobbyServer();
        }

        Log("[GameStateManager] EnterLobby");
    }

    private void EnterCountdown()
    {
        StateValue.Value = (int)GameState.Countdown;
        StateTimer.Value = countdownSeconds;

        Log("[GameStateManager] EnterCountdown");
    }

    private void EnterPlaying()
    {
        StateValue.Value = (int)GameState.Playing;
        StateTimer.Value = playSeconds;

        if (teleportPlayersOnEnterPlaying && inGameMatchManager != null && inGameMatchManager.IsSpawned)
        {
            inGameMatchManager.TeleportPlayersToGameServer();
        }

        Log("[GameStateManager] EnterPlaying");
    }

    private void EnterResults()
    {
        StateValue.Value = (int)GameState.Results;
        StateTimer.Value = resultsSeconds;

        Log("[GameStateManager] EnterResults");
    }

    private void Log(string message)
    {
        if (!enableDebugLogs)
            return;

        Debug.Log(message, this);
    }
}
