using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;

public class InGameMatchManager : NetworkBehaviour
{
    public enum ServerGameTeleportPhase
    {
        None = 0,
        InProgress = 1,
        Completed = 2
    }

    public enum ServerGameTeleportOutcome
    {
        None = 0,
        Succeeded = 1,
        Failed = 2,
        Canceled = 3
    }

    public readonly struct ServerGameTeleportSnapshot
    {
        public int RequestVersion { get; }
        public ServerGameTeleportPhase Phase { get; }
        public ServerGameTeleportOutcome Outcome { get; }

        public ServerGameTeleportSnapshot(
            int requestVersion,
            ServerGameTeleportPhase phase,
            ServerGameTeleportOutcome outcome)
        {
            RequestVersion = requestVersion;
            Phase = phase;
            Outcome = outcome;
        }
    }

    [Header("플레이어 스폰")]
    [SerializeField, Tooltip("플레이어 프리팹(NetworkObject 포함). 현재는 직접 스폰하지 않고 NetworkManager의 PlayerObject를 사용합니다.")]
    private NetworkObject playerPrefab;

    [Header("로비 스폰 설정")]
    [SerializeField, Tooltip("로비 스폰포인트 태그")]
    private string lobbySpawnTag = "LobbySpawnPoint";

    [SerializeField, Tooltip("로비 스폰포인트 이름 접두어")]
    private string lobbySpawnNamePrefix = "LobbySpawnPoint_";

    [Header("게임 스폰 설정")]
    [SerializeField, Tooltip("게임 스폰포인트 태그")]
    private string gameSpawnTag = "GameSpawnPoint";

    [SerializeField, Tooltip("게임 스폰포인트 이름 접두어")]
    private string gameSpawnNamePrefix = "GameSpawnPoint_";

    [Header("텔레포트 설정")]
    [SerializeField, Tooltip("상태 전환 후 텔레포트 시작 전 대기 시간(초)")]
    private float teleportDelay = 0.3f;

    [SerializeField, Tooltip("PlayerObject가 준비될 때까지 기다리는 최대 시간(초)")]
    private float playerResolveTimeout = 2.0f;

    [SerializeField, Tooltip("스폰 시 위로 띄울 기본 오프셋")]
    private float baseSpawnYOffset = 0.5f;

    [SerializeField, Tooltip("late join 클라이언트 전용 텔레포트 지연(초)")]
    private float lateJoinTeleportDelay = 0.15f;

    [SerializeField, Tooltip("바닥 탐색 시작 높이")]
    private float groundProbeStartHeight = 4f;

    [SerializeField, Tooltip("바닥 탐색 거리")]
    private float groundProbeDistance = 12f;

    [SerializeField, Tooltip("텔레포트 직후 허용할 최대 수평 드리프트")]
    private float postTeleportDriftTolerance = 0.05f;

    [Header("Debug")]
    [SerializeField, Tooltip("디버그 로그 출력 여부입니다.")]
    private bool enableDebugLogs = false;

    private Coroutine _teleportRoutine;
    private readonly Dictionary<ulong, Coroutine> _singleTeleportRoutines = new Dictionary<ulong, Coroutine>();
    private readonly Dictionary<ulong, int> _singleTeleportTokens = new Dictionary<ulong, int>();
    private int _teleportVersion;
    private int _serverGameTeleportRequestVersion = -1;
    private ServerGameTeleportPhase _serverGameTeleportPhase;
    private ServerGameTeleportOutcome _serverGameTeleportOutcome;
    private ulong[] _serverGameTeleportCohort;
    private bool _serverGameTeleportCohortChanged;
    private bool _serverGameTeleportCancellationRequested;
    private int _serverGameTeleportExpectedCount;
    private int _serverGameTeleportCompletedCount;

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        if (!IsServer) return;

        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientConnectedCallback += HandleClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback += HandleClientDisconnected;
        }

        TeleportPlayersToLobbyServer();
    }

    public override void OnNetworkDespawn()
    {
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientConnectedCallback -= HandleClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback -= HandleClientDisconnected;
        }

        if (_serverGameTeleportPhase == ServerGameTeleportPhase.InProgress)
        {
            _serverGameTeleportCancellationRequested = true;
            CompleteServerGameTeleportRequest(
                _serverGameTeleportRequestVersion,
                ServerGameTeleportOutcome.Canceled,
                "Network despawn.");
        }

        if (_teleportRoutine != null)
        {
            StopCoroutine(_teleportRoutine);
            _teleportRoutine = null;
        }

        StopAllSingleClientTeleportRoutines();
        _teleportVersion++;
        ClearServerGameTeleportTracking();

        base.OnNetworkDespawn();
    }

    private void HandleClientConnected(ulong clientId)
    {
        if (!IsServer) return;
        if (NetworkManager.Singleton == null) return;

        if (_serverGameTeleportPhase == ServerGameTeleportPhase.InProgress)
        {
            MarkServerGameTeleportCohortChanged($"Client connected during strict game teleport. client:{clientId}");
            return;
        }

        // 호스트 자기 자신 초기 접속은 전체 로비 배치 루틴에서 처리
        if (clientId == NetworkManager.ServerClientId &&
            NetworkManager.Singleton.ConnectedClientsIds.Count <= 1)
            return;

        StartSingleClientTeleportRoutine(clientId);
    }

    private void HandleClientDisconnected(ulong clientId)
    {
        if (!IsServer) return;
        if (_serverGameTeleportPhase != ServerGameTeleportPhase.InProgress) return;

        MarkServerGameTeleportCohortChanged($"Client disconnected during strict game teleport. client:{clientId}");
    }

    public void TeleportPlayersToLobbyServer()
    {
        if (!IsServer) return;

        if (_serverGameTeleportPhase == ServerGameTeleportPhase.InProgress)
        {
            RequestServerGameTeleportCancellation("Legacy lobby teleport requested.");
            return;
        }

        ClearServerGameTeleportTracking();
        StartTeleportRoutine(lobbySpawnTag, lobbySpawnNamePrefix);
    }

    public void TeleportPlayersToGameServer()
    {
        if (!IsServer) return;
        StartTeleportRoutine(gameSpawnTag, gameSpawnNamePrefix);
    }

    public bool ServerTryStartGameSpawnTeleport(out int requestVersion)
    {
        requestVersion = -1;

        if (!IsServer || !IsSpawned || !isActiveAndEnabled)
            return false;

        NetworkManager nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsListening)
            return false;

        if (_serverGameTeleportPhase == ServerGameTeleportPhase.InProgress)
            return false;

        if (_teleportRoutine != null || _singleTeleportRoutines.Count != 0)
            return false;

        if (_teleportVersion < 0 || _teleportVersion == int.MaxValue)
            return false;

        int connectedClientCount = nm.ConnectedClientsIds.Count;
        if (connectedClientCount <= 0)
            return false;

        ulong[] cohort = new ulong[connectedClientCount];
        for (int i = 0; i < connectedClientCount; i++)
            cohort[i] = nm.ConnectedClientsIds[i];

        System.Array.Sort(cohort);
        for (int i = 1; i < cohort.Length; i++)
        {
            if (cohort[i - 1] == cohort[i])
                return false;
        }

        int previousRequestVersion = _serverGameTeleportRequestVersion;
        ServerGameTeleportPhase previousPhase = _serverGameTeleportPhase;
        ServerGameTeleportOutcome previousOutcome = _serverGameTeleportOutcome;
        ulong[] previousCohort = _serverGameTeleportCohort;
        bool previousCohortChanged = _serverGameTeleportCohortChanged;
        bool previousCancellationRequested = _serverGameTeleportCancellationRequested;
        int previousExpectedCount = _serverGameTeleportExpectedCount;
        int previousCompletedCount = _serverGameTeleportCompletedCount;

        int nextVersion = _teleportVersion + 1;
        _teleportVersion = nextVersion;
        _serverGameTeleportRequestVersion = nextVersion;
        _serverGameTeleportPhase = ServerGameTeleportPhase.InProgress;
        _serverGameTeleportOutcome = ServerGameTeleportOutcome.None;
        _serverGameTeleportCohort = cohort;
        _serverGameTeleportCohortChanged = false;
        _serverGameTeleportCancellationRequested = false;
        _serverGameTeleportExpectedCount = cohort.Length;
        _serverGameTeleportCompletedCount = 0;

        Coroutine routine;
        try
        {
            routine = StartCoroutine(TeleportGameSpawnRequestRoutine(nextVersion, cohort));
        }
        catch (System.Exception exception)
        {
            RestoreServerGameTeleportTracking(
                previousRequestVersion,
                previousPhase,
                previousOutcome,
                previousCohort,
                previousCohortChanged,
                previousCancellationRequested,
                previousExpectedCount,
                previousCompletedCount);
            Debug.LogError($"[InGameMatchManager] Failed to start strict game teleport. {exception}", this);
            return false;
        }

        if (routine == null)
        {
            RestoreServerGameTeleportTracking(
                previousRequestVersion,
                previousPhase,
                previousOutcome,
                previousCohort,
                previousCohortChanged,
                previousCancellationRequested,
                previousExpectedCount,
                previousCompletedCount);
            Debug.LogError("[InGameMatchManager] Failed to start strict game teleport. Coroutine reference is null.", this);
            return false;
        }

        _teleportRoutine = routine;
        requestVersion = nextVersion;
        return true;
    }

    public bool ServerTryGetGameSpawnTeleportStatus(
        int requestVersion,
        out ServerGameTeleportSnapshot snapshot)
    {
        snapshot = default;

        if (!IsServer || !IsSpawned)
            return false;

        NetworkManager nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsListening)
            return false;

        if (requestVersion <= 0 || requestVersion != _serverGameTeleportRequestVersion)
            return false;

        if (_serverGameTeleportPhase == ServerGameTeleportPhase.None)
            return false;

        snapshot = new ServerGameTeleportSnapshot(
            _serverGameTeleportRequestVersion,
            _serverGameTeleportPhase,
            _serverGameTeleportOutcome);
        return true;
    }

    public bool ServerTryRespawnPlayerToGameSpawn(PlayerHub playerHub)
    {
        if (!IsServer) return false;
        if (playerHub == null) return false;

        if (!ServerTryResolveGameSpawnPose(playerHub, out Vector3 position, out Quaternion rotation))
            return false;

        NetworkObject playerObject = ResolvePlayerNetworkObject(playerHub);
        if (playerObject == null || !playerObject.IsSpawned)
            return false;

        StartSinglePlayerTeleportRoutine(playerObject, position, rotation);
        return true;
    }

    public bool ServerTryResolveGameSpawnPose(PlayerHub playerHub, out Vector3 position, out Quaternion rotation)
    {
        position = Vector3.zero;
        rotation = Quaternion.identity;

        if (!IsServer) return false;
        if (playerHub == null) return false;
        if (!IsGameSpawnRespawnAllowedByState()) return false;

        NetworkObject playerObject = ResolvePlayerNetworkObject(playerHub);
        if (playerObject == null || !playerObject.IsSpawned)
            return false;

        List<Transform> spawnPoints = FindSpawnPointsByTag(gameSpawnTag);
        spawnPoints.Sort((a, b) => string.Compare(a.name, b.name, System.StringComparison.Ordinal));

        if (spawnPoints.Count == 0)
            return false;

        ulong clientId = playerObject.OwnerClientId;
        int fallbackIndex = ResolveFallbackSpawnIndexForClient(clientId);
        Transform targetSpawn = ResolveSpawnPointForClient(spawnPoints, clientId, fallbackIndex, gameSpawnNamePrefix);
        if (targetSpawn == null)
            return false;

        CharacterController cc = playerObject.GetComponent<CharacterController>();
        position = ResolveExactSpawnPosition(targetSpawn.position, cc);

        Vector3 euler = targetSpawn.rotation.eulerAngles;
        rotation = Quaternion.Euler(0f, euler.y, 0f);
        return true;
    }

    private void StartTeleportRoutine(string tagName, string namePrefix)
    {
        if (!IsServer) return;

        if (_serverGameTeleportPhase == ServerGameTeleportPhase.InProgress)
        {
            RequestServerGameTeleportCancellation($"Legacy batch teleport requested. tag:{tagName}");
            return;
        }

        if (_teleportRoutine != null)
        {
            StopCoroutine(_teleportRoutine);
            _teleportRoutine = null;
        }

        StopAllSingleClientTeleportRoutines();

        _teleportVersion++;
        int requestVersion = _teleportVersion;
        _teleportRoutine = StartCoroutine(TeleportPlayersRoutine(tagName, namePrefix, requestVersion));
    }

    private void StartSingleClientTeleportRoutine(ulong clientId)
    {
        StopSingleClientTeleportRoutine(clientId);

        int nextToken = 1;
        if (_singleTeleportTokens.TryGetValue(clientId, out int prevToken))
            nextToken = prevToken + 1;

        _singleTeleportTokens[clientId] = nextToken;

        int requestVersion = _teleportVersion;
        Coroutine routine = StartCoroutine(TeleportSingleClientRoutine(clientId, requestVersion, nextToken));
        _singleTeleportRoutines[clientId] = routine;
    }

    private void StartSinglePlayerTeleportRoutine(NetworkObject playerObject, Vector3 position, Quaternion rotation)
    {
        if (playerObject == null)
            return;

        ulong clientId = playerObject.OwnerClientId;
        StopSingleClientTeleportRoutine(clientId);

        int nextToken = 1;
        if (_singleTeleportTokens.TryGetValue(clientId, out int prevToken))
            nextToken = prevToken + 1;

        _singleTeleportTokens[clientId] = nextToken;

        int requestVersion = _teleportVersion;
        Coroutine routine = StartCoroutine(TeleportSinglePlayerToPoseRoutine(playerObject, position, rotation, requestVersion, nextToken));
        _singleTeleportRoutines[clientId] = routine;
    }

    private void StopSingleClientTeleportRoutine(ulong clientId)
    {
        if (_singleTeleportRoutines.TryGetValue(clientId, out Coroutine runningRoutine) && runningRoutine != null)
            StopCoroutine(runningRoutine);

        _singleTeleportRoutines.Remove(clientId);
        _singleTeleportTokens.Remove(clientId);
    }

    private void StopAllSingleClientTeleportRoutines()
    {
        foreach (var pair in _singleTeleportRoutines)
        {
            if (pair.Value != null)
                StopCoroutine(pair.Value);
        }

        _singleTeleportRoutines.Clear();
        _singleTeleportTokens.Clear();
    }

    private void CompleteSingleClientTeleportRoutine(ulong clientId, int requestToken)
    {
        if (_singleTeleportTokens.TryGetValue(clientId, out int currentToken) && currentToken == requestToken)
        {
            _singleTeleportTokens.Remove(clientId);
            _singleTeleportRoutines.Remove(clientId);
        }
    }

    private bool IsSingleTeleportRequestValid(ulong clientId, int requestVersion, int requestToken)
    {
        if (requestVersion != _teleportVersion)
            return false;

        if (!_singleTeleportTokens.TryGetValue(clientId, out int currentToken))
            return false;

        return currentToken == requestToken;
    }

    private IEnumerator TeleportPlayersRoutine(string tagName, string namePrefix, int requestVersion)
    {
        if (teleportDelay > 0f)
            yield return new WaitForSeconds(teleportDelay);

        if (requestVersion != _teleportVersion)
            yield break;

        var nm = NetworkManager.Singleton;
        if (nm == null)
        {
            Debug.LogWarning("[InGameMatchManager] NetworkManager.Singleton is null.");
            if (requestVersion == _teleportVersion)
                _teleportRoutine = null;
            yield break;
        }

        float wait = 0f;
        while (wait < playerResolveTimeout && !AreAllPlayerObjectsReady(nm))
        {
            if (requestVersion != _teleportVersion)
                yield break;

            wait += Time.deltaTime;
            yield return null;
        }

        if (requestVersion != _teleportVersion)
            yield break;

        var spawnPoints = FindSpawnPointsByTag(tagName);
        spawnPoints.Sort((a, b) => string.Compare(a.name, b.name, System.StringComparison.Ordinal));

        if (spawnPoints.Count == 0)
        {
            Debug.LogWarning($"[InGameMatchManager] SpawnPoint tag not found or empty: {tagName}");
            if (requestVersion == _teleportVersion)
                _teleportRoutine = null;
            yield break;
        }

        var clientIds = new List<ulong>(nm.ConnectedClientsIds);
        clientIds.Sort();

        for (int i = 0; i < clientIds.Count; i++)
        {
            if (requestVersion != _teleportVersion)
                yield break;

            ulong clientId = clientIds[i];

            if (!nm.ConnectedClients.TryGetValue(clientId, out var client) || client == null)
                continue;

            NetworkObject playerObj = client.PlayerObject;
            if (playerObj == null || !playerObj.IsSpawned)
            {
                LogWarning($"[InGameMatchManager] Skip teleport. PlayerObject not ready. client:{clientId}");
                continue;
            }

            Transform targetSpawn = ResolveSpawnPointForClient(spawnPoints, clientId, i, namePrefix);
            if (targetSpawn == null)
                continue;

            yield return TeleportPlayerSafely(playerObj, targetSpawn.position, targetSpawn.rotation);

            if (requestVersion != _teleportVersion)
                yield break;

            Log($"[InGameMatchManager] Teleport client:{clientId} -> {targetSpawn.name} pos:{targetSpawn.position} actual:{playerObj.transform.position}");
        }

        if (requestVersion == _teleportVersion)
            _teleportRoutine = null;
    }

    private IEnumerator TeleportGameSpawnRequestRoutine(int requestVersion, ulong[] cohort)
    {
        try
        {
            if (teleportDelay > 0f)
                yield return new WaitForSeconds(teleportDelay);
            else
                yield return null;

            NetworkManager nm = NetworkManager.Singleton;
            if (!TryContinueServerGameTeleportRequest(requestVersion, nm))
                yield break;

            if (cohort == null ||
                !ReferenceEquals(cohort, _serverGameTeleportCohort) ||
                cohort.Length != _serverGameTeleportExpectedCount)
            {
                CompleteServerGameTeleportRequest(
                    requestVersion,
                    ServerGameTeleportOutcome.Failed,
                    "Request cohort tracking mismatch.");
                yield break;
            }

            float wait = 0f;
            float readinessTimeout = Mathf.Max(0f, playerResolveTimeout);
            while (true)
            {
                nm = NetworkManager.Singleton;
                if (!TryContinueServerGameTeleportRequest(requestVersion, nm))
                    yield break;

                if (AreServerGameTeleportCohortPlayersReady(nm, cohort))
                    break;

                if (wait >= readinessTimeout)
                {
                    CompleteServerGameTeleportRequest(
                        requestVersion,
                        ServerGameTeleportOutcome.Failed,
                        "PlayerObject readiness timeout.");
                    yield break;
                }

                wait += Time.deltaTime;
                yield return null;
            }

            List<Transform> spawnPoints = FindSpawnPointsByTag(gameSpawnTag);
            spawnPoints.Sort((a, b) => string.Compare(a.name, b.name, System.StringComparison.Ordinal));

            if (spawnPoints.Count == 0)
            {
                CompleteServerGameTeleportRequest(
                    requestVersion,
                    ServerGameTeleportOutcome.Failed,
                    $"SpawnPoint tag not found or empty: {gameSpawnTag}");
                yield break;
            }

            for (int i = 0; i < cohort.Length; i++)
            {
                nm = NetworkManager.Singleton;
                if (!TryContinueServerGameTeleportRequest(requestVersion, nm))
                    yield break;

                ulong clientId = cohort[i];
                if (!nm.ConnectedClients.TryGetValue(clientId, out var client) || client == null)
                {
                    CompleteServerGameTeleportRequest(
                        requestVersion,
                        ServerGameTeleportOutcome.Failed,
                        $"Connected client missing. client:{clientId}");
                    yield break;
                }

                NetworkObject playerObject = client.PlayerObject;
                if (playerObject == null || !playerObject.IsSpawned)
                {
                    CompleteServerGameTeleportRequest(
                        requestVersion,
                        ServerGameTeleportOutcome.Failed,
                        $"PlayerObject missing or unspawned. client:{clientId}");
                    yield break;
                }

                Transform targetSpawn = ResolveSpawnPointForClient(
                    spawnPoints,
                    clientId,
                    i,
                    gameSpawnNamePrefix);
                if (targetSpawn == null)
                {
                    CompleteServerGameTeleportRequest(
                        requestVersion,
                        ServerGameTeleportOutcome.Failed,
                        $"Game spawn could not be resolved. client:{clientId}");
                    yield break;
                }

                yield return TeleportPlayerSafely(playerObject, targetSpawn.position, targetSpawn.rotation);

                nm = NetworkManager.Singleton;
                if (!TryContinueServerGameTeleportRequest(requestVersion, nm))
                    yield break;

                if (!nm.ConnectedClients.TryGetValue(clientId, out var currentClient) ||
                    currentClient == null ||
                    currentClient.PlayerObject != playerObject ||
                    !playerObject.IsSpawned)
                {
                    CompleteServerGameTeleportRequest(
                        requestVersion,
                        ServerGameTeleportOutcome.Failed,
                        $"Player identity changed during safe teleport. client:{clientId}");
                    yield break;
                }

                if (_serverGameTeleportCompletedCount >= _serverGameTeleportExpectedCount)
                {
                    CompleteServerGameTeleportRequest(
                        requestVersion,
                        ServerGameTeleportOutcome.Failed,
                        "Completed player count exceeded the expected cohort count.");
                    yield break;
                }

                _serverGameTeleportCompletedCount++;
                Log($"[InGameMatchManager] Strict game teleport client:{clientId} -> {targetSpawn.name} completed:{_serverGameTeleportCompletedCount}/{_serverGameTeleportExpectedCount}");
            }

            nm = NetworkManager.Singleton;
            if (!TryContinueServerGameTeleportRequest(requestVersion, nm))
                yield break;

            if (_serverGameTeleportCompletedCount != _serverGameTeleportExpectedCount)
            {
                CompleteServerGameTeleportRequest(
                    requestVersion,
                    ServerGameTeleportOutcome.Failed,
                    "Final completed player count mismatch.");
                yield break;
            }

            CompleteServerGameTeleportRequest(
                requestVersion,
                ServerGameTeleportOutcome.Succeeded,
                "All request cohort players completed safe teleport.");
        }
        finally
        {
            if (_serverGameTeleportRequestVersion == requestVersion)
            {
                if (_serverGameTeleportPhase == ServerGameTeleportPhase.InProgress)
                {
                    CompleteServerGameTeleportRequest(
                        requestVersion,
                        ServerGameTeleportOutcome.Failed,
                        "Strict game teleport ended without a terminal outcome.");
                }

                _teleportRoutine = null;
            }
        }
    }

    private bool TryContinueServerGameTeleportRequest(int requestVersion, NetworkManager nm)
    {
        if (requestVersion != _serverGameTeleportRequestVersion ||
            _serverGameTeleportPhase != ServerGameTeleportPhase.InProgress)
            return false;

        if (requestVersion != _teleportVersion || !IsServer || !IsSpawned || !isActiveAndEnabled)
        {
            _serverGameTeleportCancellationRequested = true;
            CompleteServerGameTeleportRequest(
                requestVersion,
                ServerGameTeleportOutcome.Canceled,
                "Request version or lifecycle was invalidated.");
            return false;
        }

        if (_serverGameTeleportCancellationRequested)
        {
            CompleteServerGameTeleportRequest(
                requestVersion,
                ServerGameTeleportOutcome.Canceled,
                "Cancellation was requested.");
            return false;
        }

        if (_serverGameTeleportCohortChanged)
        {
            CompleteServerGameTeleportRequest(
                requestVersion,
                ServerGameTeleportOutcome.Failed,
                "Connected client cohort changed.");
            return false;
        }

        if (nm == null || !nm.IsListening)
        {
            CompleteServerGameTeleportRequest(
                requestVersion,
                ServerGameTeleportOutcome.Failed,
                "NetworkManager is unavailable or not listening.");
            return false;
        }

        if (!DoesServerGameTeleportCohortMatch(nm, _serverGameTeleportCohort))
        {
            _serverGameTeleportCohortChanged = true;
            CompleteServerGameTeleportRequest(
                requestVersion,
                ServerGameTeleportOutcome.Failed,
                "Connected client cohort no longer matches the request snapshot.");
            return false;
        }

        return true;
    }

    private bool DoesServerGameTeleportCohortMatch(NetworkManager nm, ulong[] cohort)
    {
        if (nm == null || cohort == null || nm.ConnectedClientsIds.Count != cohort.Length)
            return false;

        bool hasPreviousId = false;
        ulong previousId = 0;

        for (int expectedIndex = 0; expectedIndex < cohort.Length; expectedIndex++)
        {
            bool foundNextId = false;
            ulong nextId = 0;

            for (int liveIndex = 0; liveIndex < nm.ConnectedClientsIds.Count; liveIndex++)
            {
                ulong liveId = nm.ConnectedClientsIds[liveIndex];
                if (hasPreviousId && liveId <= previousId)
                    continue;

                if (!foundNextId || liveId < nextId)
                {
                    nextId = liveId;
                    foundNextId = true;
                }
            }

            if (!foundNextId || nextId != cohort[expectedIndex])
                return false;

            previousId = nextId;
            hasPreviousId = true;
        }

        return true;
    }

    private bool AreServerGameTeleportCohortPlayersReady(NetworkManager nm, ulong[] cohort)
    {
        if (nm == null || cohort == null)
            return false;

        for (int i = 0; i < cohort.Length; i++)
        {
            if (!nm.ConnectedClients.TryGetValue(cohort[i], out var client) || client == null)
                return false;

            if (client.PlayerObject == null || !client.PlayerObject.IsSpawned)
                return false;
        }

        return true;
    }

    private void CompleteServerGameTeleportRequest(
        int requestVersion,
        ServerGameTeleportOutcome outcome,
        string reason)
    {
        if (requestVersion != _serverGameTeleportRequestVersion ||
            _serverGameTeleportPhase != ServerGameTeleportPhase.InProgress ||
            outcome == ServerGameTeleportOutcome.None)
            return;

        if (outcome == ServerGameTeleportOutcome.Succeeded &&
            (_serverGameTeleportExpectedCount <= 0 ||
             _serverGameTeleportCompletedCount != _serverGameTeleportExpectedCount))
        {
            outcome = ServerGameTeleportOutcome.Failed;
            reason = "Succeeded postcondition rejected because completed count did not match the expected cohort.";
        }

        _serverGameTeleportPhase = ServerGameTeleportPhase.Completed;
        _serverGameTeleportOutcome = outcome;

        string message = $"[InGameMatchManager] Strict game teleport terminal. version:{requestVersion} outcome:{outcome} completed:{_serverGameTeleportCompletedCount}/{_serverGameTeleportExpectedCount} reason:{reason}";
        if (outcome == ServerGameTeleportOutcome.Succeeded)
            Log(message);
        else
            LogWarning(message);
    }

    private void RequestServerGameTeleportCancellation(string reason)
    {
        if (_serverGameTeleportPhase != ServerGameTeleportPhase.InProgress)
            return;

        if (_serverGameTeleportCancellationRequested)
            return;

        _serverGameTeleportCancellationRequested = true;
        LogWarning($"[InGameMatchManager] Strict game teleport cancellation requested. version:{_serverGameTeleportRequestVersion} reason:{reason}");
    }

    private void MarkServerGameTeleportCohortChanged(string reason)
    {
        if (_serverGameTeleportPhase != ServerGameTeleportPhase.InProgress)
            return;

        if (_serverGameTeleportCohortChanged)
            return;

        _serverGameTeleportCohortChanged = true;
        LogWarning($"[InGameMatchManager] Strict game teleport cohort changed. version:{_serverGameTeleportRequestVersion} reason:{reason}");
    }

    private void ClearServerGameTeleportTracking()
    {
        _serverGameTeleportRequestVersion = -1;
        _serverGameTeleportPhase = ServerGameTeleportPhase.None;
        _serverGameTeleportOutcome = ServerGameTeleportOutcome.None;
        _serverGameTeleportCohort = null;
        _serverGameTeleportCohortChanged = false;
        _serverGameTeleportCancellationRequested = false;
        _serverGameTeleportExpectedCount = 0;
        _serverGameTeleportCompletedCount = 0;
    }

    private void RestoreServerGameTeleportTracking(
        int requestVersion,
        ServerGameTeleportPhase phase,
        ServerGameTeleportOutcome outcome,
        ulong[] cohort,
        bool cohortChanged,
        bool cancellationRequested,
        int expectedCount,
        int completedCount)
    {
        _serverGameTeleportRequestVersion = requestVersion;
        _serverGameTeleportPhase = phase;
        _serverGameTeleportOutcome = outcome;
        _serverGameTeleportCohort = cohort;
        _serverGameTeleportCohortChanged = cohortChanged;
        _serverGameTeleportCancellationRequested = cancellationRequested;
        _serverGameTeleportExpectedCount = expectedCount;
        _serverGameTeleportCompletedCount = completedCount;
    }

    private IEnumerator TeleportSingleClientRoutine(ulong clientId, int requestVersion, int requestToken)
    {
        var nm = NetworkManager.Singleton;
        if (nm == null)
        {
            CompleteSingleClientTeleportRoutine(clientId, requestToken);
            yield break;
        }

        float wait = 0f;
        while (wait < playerResolveTimeout)
        {
            if (!IsSingleTeleportRequestValid(clientId, requestVersion, requestToken))
            {
                CompleteSingleClientTeleportRoutine(clientId, requestToken);
                yield break;
            }

            if (nm.ConnectedClients.TryGetValue(clientId, out var client) &&
                client != null &&
                client.PlayerObject != null &&
                client.PlayerObject.IsSpawned)
            {
                break;
            }

            wait += Time.deltaTime;
            yield return null;
        }

        if (!IsSingleTeleportRequestValid(clientId, requestVersion, requestToken))
        {
            CompleteSingleClientTeleportRoutine(clientId, requestToken);
            yield break;
        }

        if (!nm.ConnectedClients.TryGetValue(clientId, out var targetClient) ||
            targetClient == null ||
            targetClient.PlayerObject == null ||
            !targetClient.PlayerObject.IsSpawned)
        {
            LogWarning($"[InGameMatchManager] Late-join teleport skipped. PlayerObject not ready. client:{clientId}");
            CompleteSingleClientTeleportRoutine(clientId, requestToken);
            yield break;
        }

        if (lateJoinTeleportDelay > 0f)
            yield return new WaitForSeconds(lateJoinTeleportDelay);

        if (!IsSingleTeleportRequestValid(clientId, requestVersion, requestToken))
        {
            CompleteSingleClientTeleportRoutine(clientId, requestToken);
            yield break;
        }

        ResolveCurrentSpawnSettings(out string tagName, out string namePrefix);

        var spawnPoints = FindSpawnPointsByTag(tagName);
        spawnPoints.Sort((a, b) => string.Compare(a.name, b.name, System.StringComparison.Ordinal));

        if (spawnPoints.Count == 0)
        {
            Debug.LogWarning($"[InGameMatchManager] SpawnPoint tag not found or empty: {tagName}");
            CompleteSingleClientTeleportRoutine(clientId, requestToken);
            yield break;
        }

        var ids = new List<ulong>(nm.ConnectedClientsIds);
        ids.Sort();
        int fallbackIndex = Mathf.Max(0, ids.IndexOf(clientId));

        Transform targetSpawn = ResolveSpawnPointForClient(spawnPoints, clientId, fallbackIndex, namePrefix);
        if (targetSpawn == null)
        {
            CompleteSingleClientTeleportRoutine(clientId, requestToken);
            yield break;
        }

        yield return TeleportPlayerSafely(targetClient.PlayerObject, targetSpawn.position, targetSpawn.rotation);

        if (!IsSingleTeleportRequestValid(clientId, requestVersion, requestToken))
        {
            CompleteSingleClientTeleportRoutine(clientId, requestToken);
            yield break;
        }

        Log($"[InGameMatchManager] LateJoin Teleport client:{clientId} -> {targetSpawn.name} pos:{targetSpawn.position} actual:{targetClient.PlayerObject.transform.position}");
        CompleteSingleClientTeleportRoutine(clientId, requestToken);
    }

    private IEnumerator TeleportSinglePlayerToPoseRoutine(NetworkObject playerObject, Vector3 position, Quaternion rotation, int requestVersion, int requestToken)
    {
        if (playerObject == null)
            yield break;

        ulong clientId = playerObject.OwnerClientId;
        if (!IsSingleTeleportRequestValid(clientId, requestVersion, requestToken))
        {
            CompleteSingleClientTeleportRoutine(clientId, requestToken);
            yield break;
        }

        if (!playerObject.IsSpawned)
        {
            CompleteSingleClientTeleportRoutine(clientId, requestToken);
            yield break;
        }

        yield return TeleportPlayerSafely(playerObject, position, rotation);

        if (IsSingleTeleportRequestValid(clientId, requestVersion, requestToken))
            CompleteSingleClientTeleportRoutine(clientId, requestToken);
    }

    private void ResolveCurrentSpawnSettings(out string tagName, out string namePrefix)
    {
        var gsm = FindFirstObjectByType<GameStateManager>();
        bool goToGame = gsm != null && gsm.GetState() == GameStateManager.GameState.Playing;

        tagName = goToGame ? gameSpawnTag : lobbySpawnTag;
        namePrefix = goToGame ? gameSpawnNamePrefix : lobbySpawnNamePrefix;
    }

    private bool AreAllPlayerObjectsReady(NetworkManager nm)
    {
        if (nm == null) return false;

        foreach (ulong clientId in nm.ConnectedClientsIds)
        {
            if (!nm.ConnectedClients.TryGetValue(clientId, out var client) || client == null)
                return false;

            if (client.PlayerObject == null || !client.PlayerObject.IsSpawned)
                return false;
        }

        return true;
    }

    private bool IsGameSpawnRespawnAllowedByState()
    {
        GameStateManager gameStateManager = FindFirstObjectByType<GameStateManager>();
        return gameStateManager != null && gameStateManager.GetState() == GameStateManager.GameState.Playing;
    }

    private NetworkObject ResolvePlayerNetworkObject(PlayerHub playerHub)
    {
        if (playerHub == null)
            return null;

        NetworkObject playerObject = playerHub.NetworkObject;
        if (playerObject != null)
            return playerObject;

        return playerHub.GetComponentInParent<NetworkObject>();
    }

    private int ResolveFallbackSpawnIndexForClient(ulong clientId)
    {
        NetworkManager nm = NetworkManager.Singleton;
        if (nm == null)
            return 0;

        List<ulong> clientIds = new List<ulong>(nm.ConnectedClientsIds);
        clientIds.Sort();

        int index = clientIds.IndexOf(clientId);
        return Mathf.Max(0, index);
    }

    private Transform ResolveSpawnPointForClient(List<Transform> spawnPoints, ulong clientId, int fallbackIndex, string namePrefix)
    {
        string exactName = $"{namePrefix}{clientId}";

        for (int i = 0; i < spawnPoints.Count; i++)
        {
            if (spawnPoints[i] != null && spawnPoints[i].name == exactName)
                return spawnPoints[i];
        }

        if (spawnPoints.Count == 0)
            return null;

        return spawnPoints[fallbackIndex % spawnPoints.Count];
    }

    private List<Transform> FindSpawnPointsByTag(string tagName)
    {
        var list = new List<Transform>();

        if (string.IsNullOrWhiteSpace(tagName))
            return list;

        try
        {
            var found = GameObject.FindGameObjectsWithTag(tagName);
            for (int i = 0; i < found.Length; i++)
            {
                if (found[i] != null && found[i].activeInHierarchy)
                    list.Add(found[i].transform);
            }
        }
        catch (UnityException)
        {
            Debug.LogWarning($"[InGameMatchManager] Tag not defined: {tagName}");
        }

        return list;
    }

    private IEnumerator TeleportPlayerSafely(NetworkObject player, Vector3 pos, Quaternion rot)
    {
        if (player == null) yield break;

        GameObject go = player.gameObject;
        Transform tf = go.transform;
        CharacterController cc = go.GetComponent<CharacterController>();
        NetworkTransform nt = go.GetComponent<NetworkTransform>();
        Rigidbody rb = go.GetComponent<Rigidbody>();
        PlayerLocomotionModule locomotion = go.GetComponentInChildren<PlayerLocomotionModule>(true);
        MotorShellRootBodySync motorShellRootBodySync = go.GetComponentInChildren<MotorShellRootBodySync>(true);

        bool hadCC = cc != null && cc.enabled;
        if (hadCC)
            cc.enabled = false;

        ResetMotion(locomotion, rb);

        Vector3 euler = rot.eulerAngles;
        Quaternion uprightRot = Quaternion.Euler(0f, euler.y, 0f);
        Vector3 exactSpawnPos = ResolveExactSpawnPosition(pos, cc);

        ForceSetTransform(tf, nt, exactSpawnPos, uprightRot);
        AlignMotorShellBodyToRoot(motorShellRootBodySync);
        yield return null;

        // 호스트 입력/모션 잔여값이 다음 프레임에 한 번 더 들어오는 경우를 막기 위해
        // CC를 끈 상태로 한 프레임 더 대기하며 모션을 다시 0으로 정리합니다.
        ResetMotion(locomotion, rb);
        ForceSetTransform(tf, nt, exactSpawnPos, uprightRot);
        AlignMotorShellBodyToRoot(motorShellRootBodySync);
        yield return null;

        if (hadCC)
            cc.enabled = true;

        yield return null;

        ResetMotion(locomotion, rb);

        Vector3 horizontalNow = new Vector3(tf.position.x, 0f, tf.position.z);
        Vector3 horizontalTarget = new Vector3(exactSpawnPos.x, 0f, exactSpawnPos.z);
        float horizontalDrift = Vector3.Distance(horizontalNow, horizontalTarget);

        if (horizontalDrift > postTeleportDriftTolerance)
        {
            bool ccWasEnabled = cc != null && cc.enabled;
            if (ccWasEnabled)
                cc.enabled = false;

            ResetMotion(locomotion, rb);
            ForceSetTransform(tf, nt, exactSpawnPos, uprightRot);
            AlignMotorShellBodyToRoot(motorShellRootBodySync);
            Physics.SyncTransforms();
            yield return null;

            if (ccWasEnabled)
                cc.enabled = true;

            LogWarning($"[InGameMatchManager] Post-teleport drift corrected. owner:{player.OwnerClientId} drift:{horizontalDrift:F3} target:{exactSpawnPos} actual:{tf.position}");
        }
    }

    private void ResetMotion(PlayerLocomotionModule locomotion, Rigidbody rb)
    {
        if (locomotion != null)
            locomotion.ResetMotionServer();

        if (rb != null && !rb.isKinematic)
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }
    }

    private void ForceSetTransform(Transform tf, NetworkTransform nt, Vector3 pos, Quaternion rot)
    {
        if (tf == null) return;

        tf.SetPositionAndRotation(pos, rot);
        Physics.SyncTransforms();

        if (nt != null)
            nt.Teleport(pos, rot, tf.localScale);
    }

    private void AlignMotorShellBodyToRoot(MotorShellRootBodySync motorShellRootBodySync)
    {
        if (motorShellRootBodySync == null)
            return;

        motorShellRootBodySync.AlignBodyToRootForTeleport(clearVelocity: true);
        Physics.SyncTransforms();
    }

    private Vector3 ResolveExactSpawnPosition(Vector3 requestedPos, CharacterController cc)
    {
        Vector3 result = requestedPos;

        float bottomOffset = 0f;
        float extraLift = Mathf.Max(0.02f, baseSpawnYOffset * 0.1f);

        if (cc != null)
        {
            bottomOffset = cc.center.y - (cc.height * 0.5f);
            extraLift = Mathf.Max(0.02f, cc.skinWidth + 0.02f);
        }

        Vector3 rayStart = requestedPos + Vector3.up * Mathf.Max(0.5f, groundProbeStartHeight);
        float rayDistance = Mathf.Max(1f, groundProbeStartHeight + groundProbeDistance);

        if (Physics.Raycast(rayStart, Vector3.down, out RaycastHit hit, rayDistance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
        {
            result.y = hit.point.y - bottomOffset + extraLift;
        }
        else
        {
            result.y = requestedPos.y + Mathf.Max(baseSpawnYOffset, extraLift - bottomOffset);
        }

        return result;
    }

    private void Log(string message)
    {
        if (!enableDebugLogs)
            return;

        Debug.Log(message, this);
    }

    private void LogWarning(string message)
    {
        if (!enableDebugLogs)
            return;

        Debug.LogWarning(message, this);
    }
}
