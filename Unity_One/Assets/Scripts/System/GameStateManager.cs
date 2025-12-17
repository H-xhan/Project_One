using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

public class GameStateManager : NetworkBehaviour
{
    public enum GameState
    {
        Lobby = 0,
        Countdown = 1,
        Playing = 2,
        Results = 3
    }

    [Tooltip("ReadySystem 참조(없으면 자동 탐색)")]
    [SerializeField] private ReadySystem readySystem;

    [Tooltip("카운트다운 시간(초)")]
    [SerializeField] private float countdownSeconds = 3f;

    [Tooltip("플레이 시간(초)")]
    [SerializeField] private float playSeconds = 120f;

    [Tooltip("결과 시간(초)")]
    [SerializeField] private float resultsSeconds = 6f;

    [Tooltip("Results 후 Lobby로 자동 복귀")]
    [SerializeField] private bool autoReturnToLobby = true;

    [Tooltip("게임플레이 씬에서 사용할 스폰포인트 태그")]
    [SerializeField] private string spawnPointTag = "SpawnPoint";

    public NetworkVariable<int> StateValue = new NetworkVariable<int>((int)GameState.Lobby);
    public NetworkVariable<float> StateTimer = new NetworkVariable<float>(0f);

    private bool _waitingForGameScene;

    public GameState GetState()
    {
        return (GameState)StateValue.Value;
    }

    public override void OnNetworkSpawn()
    {
        if (readySystem == null)
            readySystem = FindFirstObjectByType<ReadySystem>();

        if (!IsServer) return;

        if (NetworkManager.Singleton != null && NetworkManager.Singleton.SceneManager != null)
        {
            NetworkManager.Singleton.SceneManager.OnLoadEventCompleted += OnLoadEventCompleted;
        }

        EnterLobby();
    }

    public override void OnNetworkDespawn()
    {
        if (!IsServer) return;

        if (NetworkManager.Singleton != null && NetworkManager.Singleton.SceneManager != null)
        {
            NetworkManager.Singleton.SceneManager.OnLoadEventCompleted -= OnLoadEventCompleted;
        }
    }

    private void Update()
    {
        if (!IsServer) return;

        if (_waitingForGameScene)
            return;

        var state = GetState();

        if (state == GameState.Lobby)
        {
            if (readySystem != null && readySystem.CanStartGameServer() && readySystem.AreAllReady())
            {
                readySystem.ResetAllReadyServer(); // 또는 readySystem.Server_ResetAllReady()
                EnterCountdown();
            }
            return;
        }


        float t = StateTimer.Value;
        if (t > 0f)
        {
            t -= Time.deltaTime;
            if (t < 0f) t = 0f;
            StateTimer.Value = t;
        }

        if (StateTimer.Value > 0f)
            return;

        if (state == GameState.Countdown) EnterPlaying();
        else if (state == GameState.Playing) EnterResults();
        else if (state == GameState.Results && autoReturnToLobby) EnterLobby();
    }

    private void EnterLobby()
    {
        StateValue.Value = (int)GameState.Lobby;
        StateTimer.Value = 0f;
        _waitingForGameScene = false;
    }

    private void EnterCountdown()
    {
        StateValue.Value = (int)GameState.Countdown;

        StateTimer.Value = 0f;
        _waitingForGameScene = true;
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

    private void OnLoadEventCompleted(string sceneName, LoadSceneMode mode, List<ulong> clientsCompleted, List<ulong> clientsTimedOut)
    {
        if (!IsServer) return;

        if (GetState() != GameState.Countdown)
            return;

        _waitingForGameScene = false;

        StateTimer.Value = countdownSeconds;

        TeleportAllPlayersToTaggedSpawns();
    }

    private void TeleportAllPlayersToTaggedSpawns()
    {
        var spawnObjects = GameObject.FindGameObjectsWithTag(spawnPointTag);
        if (spawnObjects == null || spawnObjects.Length == 0)
        {
            Debug.LogWarning($"[GameStateManager] No SpawnPoints found with tag '{spawnPointTag}'.");
            return;
        }

        var spawns = new List<Transform>(spawnObjects.Length);
        for (int i = 0; i < spawnObjects.Length; i++)
            spawns.Add(spawnObjects[i].transform);

        spawns.Sort((a, b) => string.Compare(a.name, b.name, System.StringComparison.Ordinal));

        int idx = 0;
        foreach (var clientId in NetworkManager.Singleton.ConnectedClientsIds)
        {
            var client = NetworkManager.Singleton.ConnectedClients[clientId];
            var playerObj = client.PlayerObject;
            if (playerObj == null) continue;

            var target = spawns[idx % spawns.Count];
            idx++;

            playerObj.transform.SetPositionAndRotation(target.position, target.rotation);
        }
    }
}
