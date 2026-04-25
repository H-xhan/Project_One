using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;

public class InGameMatchManager : NetworkBehaviour
{
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

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        if (!IsServer) return;

        if (NetworkManager.Singleton != null)
            NetworkManager.Singleton.OnClientConnectedCallback += HandleClientConnected;

        TeleportPlayersToLobbyServer();
    }

    public override void OnNetworkDespawn()
    {
        if (NetworkManager.Singleton != null)
            NetworkManager.Singleton.OnClientConnectedCallback -= HandleClientConnected;

        if (_teleportRoutine != null)
        {
            StopCoroutine(_teleportRoutine);
            _teleportRoutine = null;
        }

        StopAllSingleClientTeleportRoutines();
        _teleportVersion++;

        base.OnNetworkDespawn();
    }

    private void HandleClientConnected(ulong clientId)
    {
        if (!IsServer) return;
        if (NetworkManager.Singleton == null) return;

        // 호스트 자기 자신 초기 접속은 전체 로비 배치 루틴에서 처리
        if (clientId == NetworkManager.ServerClientId &&
            NetworkManager.Singleton.ConnectedClientsIds.Count <= 1)
            return;

        StartSingleClientTeleportRoutine(clientId);
    }

    public void TeleportPlayersToLobbyServer()
    {
        if (!IsServer) return;
        StartTeleportRoutine(lobbySpawnTag, lobbySpawnNamePrefix);
    }

    public void TeleportPlayersToGameServer()
    {
        if (!IsServer) return;
        StartTeleportRoutine(gameSpawnTag, gameSpawnNamePrefix);
    }

    private void StartTeleportRoutine(string tagName, string namePrefix)
    {
        if (!IsServer) return;

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

        bool hadCC = cc != null && cc.enabled;
        if (hadCC)
            cc.enabled = false;

        ResetMotion(locomotion, rb);

        Vector3 euler = rot.eulerAngles;
        Quaternion uprightRot = Quaternion.Euler(0f, euler.y, 0f);
        Vector3 exactSpawnPos = ResolveExactSpawnPosition(pos, cc);

        ForceSetTransform(tf, nt, exactSpawnPos, uprightRot);
        yield return null;

        // 호스트 입력/모션 잔여값이 다음 프레임에 한 번 더 들어오는 경우를 막기 위해
        // CC를 끈 상태로 한 프레임 더 대기하며 모션을 다시 0으로 정리합니다.
        ResetMotion(locomotion, rb);
        ForceSetTransform(tf, nt, exactSpawnPos, uprightRot);
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
