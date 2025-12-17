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

    [Tooltip("ReadySystem 참조(비우면 자동 탐색)")]
    [SerializeField] private ReadySystem readySystem;

    [Tooltip("카운트다운 시간(초)")]
    [SerializeField] private float countdownSeconds = 3f;

    [Tooltip("플레이 시간(초)")]
    [SerializeField] private float playSeconds = 120f;

    [Tooltip("결과 화면 유지 시간(초)")]
    [SerializeField] private float resultsSeconds = 6f;

    [Tooltip("결과 후 자동으로 로비로 복귀")]
    [SerializeField] private bool autoReturnToLobby = true;

    [Tooltip("라운드 시작 시 플레이어 스폰 위치(클라이언트별 순환 배치)")]
    [SerializeField] private Transform[] spawnPoints;

    public NetworkVariable<int> StateValue = new NetworkVariable<int>((int)GameState.Lobby);
    public NetworkVariable<float> StateTimer = new NetworkVariable<float>(0f);

    private GameState _cachedState;

    private void Awake()
    {
        _cachedState = GameState.Lobby;
    }

    public override void OnNetworkSpawn()
    {
        readySystem = FindFirstObjectByType<ReadySystem>();


        if (IsServer)
        {
            EnterLobby();
        }
    }

    private void Update()
    {
        if (!IsSpawned) return;

        GameState state = (GameState)StateValue.Value;

        if (state != _cachedState)
        {
            _cachedState = state;
        }

        if (!IsServer) return;

        if (state == GameState.Lobby)
        {
            return;
        }

        float t = StateTimer.Value;
        t -= Time.deltaTime;
        StateTimer.Value = Mathf.Max(0f, t);

        if (StateTimer.Value > 0f) return;

        switch (state)
        {
            case GameState.Countdown:
                EnterPlaying();
                break;
            case GameState.Playing:
                EnterResults();
                break;
            case GameState.Results:
                if (autoReturnToLobby) EnterLobby();
                break;
        }
    }

    public void TryStartCountdownFromReady()
    {
        if (!IsServer) return;

        if ((GameState)StateValue.Value != GameState.Lobby) return;
        if (readySystem == null) return;
        if (!readySystem.AreAllReady()) return;

        EnterCountdown();
    }

    private void EnterLobby()
    {
        StateValue.Value = (int)GameState.Lobby;
        StateTimer.Value = 0f;

        if (readySystem != null)
            readySystem.ClearAllReadyServer();
    }

    private void EnterCountdown()
    {
        StateValue.Value = (int)GameState.Countdown;
        StateTimer.Value = countdownSeconds;

        TeleportPlayersToSpawnsClientRpc();
    }

    private void EnterPlaying()
    {
        StateValue.Value = (int)GameState.Playing;
        StateTimer.Value = playSeconds;
    }

    private void EnterResults()
    {
        StateValue.Value = (int)GameState.Results;
        StateTimer.Value = resultsSeconds;
    }

    [ClientRpc]
    private void TeleportPlayersToSpawnsClientRpc()
    {
        if (spawnPoints == null || spawnPoints.Length == 0) return;
        if (NetworkManager == null) return;

        NetworkObject localPlayer = NetworkManager.SpawnManager.GetLocalPlayerObject();
        if (localPlayer == null) return;

        int idx = (int)(NetworkManager.LocalClientId % (ulong)spawnPoints.Length);
        Transform sp = spawnPoints[idx];
        if (sp == null) return;

        localPlayer.transform.SetPositionAndRotation(sp.position, sp.rotation);
    }

    public GameState GetState()
    {
        return (GameState)StateValue.Value;
    }
}
