using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;

public class InGameMatchManager : NetworkBehaviour
{
    [Header("플레이어 스폰")]
    [SerializeField, Tooltip("플레이어 프리팹(NetworkObject 포함). 현재는 직접 스폰하지 않고, NetworkManager의 PlayerObject를 사용합니다.")]
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

    [SerializeField, Tooltip("모든 PlayerObject가 준비될 때까지 기다리는 최대 시간(초)")]
    private float playerResolveTimeout = 2.0f;

    private Coroutine _teleportRoutine;

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        if (!IsServer) return;

        // InGame 씬 진입 직후 로비 존 배치
        TeleportPlayersToLobbyServer();
    }

    public override void OnNetworkDespawn()
    {
        if (_teleportRoutine != null)
        {
            StopCoroutine(_teleportRoutine);
            _teleportRoutine = null;
        }

        base.OnNetworkDespawn();
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
            StopCoroutine(_teleportRoutine);

        _teleportRoutine = StartCoroutine(TeleportPlayersRoutine(tagName, namePrefix));
    }

    private IEnumerator TeleportPlayersRoutine(string tagName, string namePrefix)
    {
        if (teleportDelay > 0f)
            yield return new WaitForSeconds(teleportDelay);

        var nm = NetworkManager.Singleton;
        if (nm == null)
        {
            Debug.LogWarning("[InGameMatchManager] NetworkManager.Singleton is null.");
            _teleportRoutine = null;
            yield break;
        }

        // 모든 플레이어 오브젝트가 준비될 때까지 잠깐 대기
        float wait = 0f;
        while (wait < playerResolveTimeout && !AreAllPlayerObjectsReady(nm))
        {
            wait += Time.deltaTime;
            yield return null;
        }

        if (!AreAllPlayerObjectsReady(nm))
        {
            Debug.LogWarning("[InGameMatchManager] Some PlayerObjects are still not ready. Teleport will continue with available players only.");
        }

        var spawnPoints = FindSpawnPointsByTag(tagName);
        spawnPoints.Sort((a, b) => string.Compare(a.name, b.name, System.StringComparison.Ordinal));

        if (spawnPoints.Count == 0)
        {
            Debug.LogWarning($"[InGameMatchManager] SpawnPoint tag not found or empty: {tagName}");
            _teleportRoutine = null;
            yield break;
        }

        var clientIds = new List<ulong>(nm.ConnectedClientsIds);
        clientIds.Sort();

        for (int i = 0; i < clientIds.Count; i++)
        {
            ulong clientId = clientIds[i];

            if (!nm.ConnectedClients.TryGetValue(clientId, out var client) || client == null)
                continue;

            NetworkObject playerObj = client.PlayerObject;
            if (playerObj == null || !playerObj.IsSpawned)
            {
                Debug.LogWarning($"[InGameMatchManager] Skip teleport. PlayerObject not ready. client:{clientId}");
                continue;
            }

            var targetSpawn = ResolveSpawnPointForClient(spawnPoints, clientId, i, namePrefix);
            if (targetSpawn == null)
                continue;

            yield return TeleportPlayerSafely(playerObj, targetSpawn.position, targetSpawn.rotation);
            Debug.Log($"[InGameMatchManager] Teleport client:{clientId} -> {targetSpawn.name} pos:{targetSpawn.position}");
        }

        _teleportRoutine = null;
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
                if (found[i] != null)
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

        var go = player.gameObject;
        var tf = go.transform;
        var cc = go.GetComponent<CharacterController>();
        var nt = go.GetComponent<NetworkTransform>();
        var rb = go.GetComponent<Rigidbody>();

        Vector3 euler = rot.eulerAngles;
        Quaternion uprightRot = Quaternion.Euler(0f, euler.y, 0f);
        Vector3 spawnPos = pos + Vector3.up * 0.05f;

        bool hadCC = cc != null && cc.enabled;
        if (hadCC)
            cc.enabled = false;

        if (rb != null && !rb.isKinematic)
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }

        if (nt != null)
            nt.Teleport(spawnPos, uprightRot, tf.localScale);
        else
            tf.SetPositionAndRotation(spawnPos, uprightRot);

        Physics.SyncTransforms();
        yield return null;

        if (rb != null && !rb.isKinematic)
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }

        if (hadCC)
            cc.enabled = true;
    }
}