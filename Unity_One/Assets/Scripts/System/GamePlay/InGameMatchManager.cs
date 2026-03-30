using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;

public class InGameMatchManager : NetworkBehaviour
{
    [Header("플레이어 스폰")]
    [SerializeField, Tooltip("플레이어 프리팹(NetworkObject 포함). 비어 있으면 NetworkManager의 PlayerPrefab을 사용합니다.")]
    private NetworkObject playerPrefab;

    [Header("로비 스폰 설정")]
    [SerializeField, Tooltip("로비 스폰포인트 태그")]
    private string lobbySpawnTag = "LobbySpawnPoint";

    [SerializeField, Tooltip("로비 스폰포인트 이름 접두어")]
    private string lobbySpawnNamePrefix = "LobbySpawnPoint_";

    [SerializeField, Tooltip("로비 배치 시 추가할 Y 회전 오프셋")]
    private float lobbyYawOffset = 0f;

    [Header("게임 스폰 설정")]
    [SerializeField, Tooltip("게임 스폰포인트 태그")]
    private string gameSpawnTag = "GameSpawnPoint";

    [SerializeField, Tooltip("게임 스폰포인트 이름 접두어")]
    private string gameSpawnNamePrefix = "GameSpawnPoint_";

    [SerializeField, Tooltip("게임 배치 시 추가할 Y 회전 오프셋")]
    private float gameYawOffset = 0f;

    [SerializeField, Tooltip("텔레포트 전 대기 시간(초)")]
    private float teleportDelay = 0.1f;

    private Coroutine _teleportRoutine;

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        if (!IsServer) return;

        // InGame 씬에 들어오면 우선 로비 존으로 배치
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
        StartTeleportRoutine(lobbySpawnTag, lobbySpawnNamePrefix, true, lobbyYawOffset);
    }

    public void TeleportPlayersToGameServer()
    {
        if (!IsServer) return;
        StartTeleportRoutine(gameSpawnTag, gameSpawnNamePrefix, true, gameYawOffset);
    }

    private void StartTeleportRoutine(string tagName, string namePrefix, bool spawnIfMissing, float yawOffset)
    {
        if (!IsServer) return;

        if (_teleportRoutine != null)
            StopCoroutine(_teleportRoutine);

        _teleportRoutine = StartCoroutine(TeleportPlayersRoutine(tagName, namePrefix, spawnIfMissing, yawOffset));
    }

    private IEnumerator TeleportPlayersRoutine(string tagName, string namePrefix, bool spawnIfMissing, float yawOffset)
    {
        yield return null;
        yield return null;

        if (teleportDelay > 0f)
            yield return new WaitForSeconds(teleportDelay);

        var nm = NetworkManager.Singleton;
        if (nm == null)
        {
            Debug.LogWarning("[InGameMatchManager] NetworkManager.Singleton is null.");
            _teleportRoutine = null;
            yield break;
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
            var client = nm.ConnectedClients[clientId];
            NetworkObject playerObj = client.PlayerObject;

            if ((playerObj == null || !playerObj.IsSpawned) && spawnIfMissing)
            {
                var prefabToUse = ResolvePlayerPrefab(nm);
                if (prefabToUse == null)
                {
                    Debug.LogError("[InGameMatchManager] playerPrefab이 비어 있습니다. 인스펙터에 플레이어 프리팹(NetworkObject)을 넣어주세요.");
                    _teleportRoutine = null;
                    yield break;
                }

                var instance = Instantiate(prefabToUse.gameObject);
                playerObj = instance.GetComponent<NetworkObject>();
                playerObj.SpawnAsPlayerObject(clientId, true);

                yield return null;
            }

            if (playerObj == null || !playerObj.IsSpawned)
                continue;

            var targetSpawn = ResolveSpawnPointForClient(spawnPoints, clientId, i, namePrefix);
            if (targetSpawn == null)
                continue;

            TeleportPlayerSafely(playerObj, targetSpawn.position, targetSpawn.rotation, yawOffset);
            Debug.Log($"[InGameMatchManager] Teleport client:{clientId} -> {targetSpawn.name} pos:{targetSpawn.position}");
        }

        _teleportRoutine = null;
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

    private NetworkObject ResolvePlayerPrefab(NetworkManager nm)
    {
        if (playerPrefab != null) return playerPrefab;

        if (nm != null && nm.NetworkConfig != null && nm.NetworkConfig.PlayerPrefab != null)
            return nm.NetworkConfig.PlayerPrefab.GetComponent<NetworkObject>();

        return null;
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

    private void TeleportPlayerSafely(NetworkObject player, Vector3 pos, Quaternion rot, float yawOffset)
    {
        if (player == null) return;

        var go = player.gameObject;
        var tf = go.transform;
        var cc = go.GetComponent<CharacterController>();
        var nt = go.GetComponent<NetworkTransform>();
        var rb = go.GetComponent<Rigidbody>();

        // 캐릭터는 항상 수직 상태를 유지하고, 스폰포인트 Yaw + 오프셋만 사용
        Vector3 euler = rot.eulerAngles;
        float finalYaw = Mathf.Repeat(euler.y + yawOffset, 360f);
        Quaternion uprightRot = Quaternion.Euler(0f, finalYaw, 0f);

        // 바닥 겹침 방지용 소폭 오프셋
        Vector3 spawnPos = pos + Vector3.up * 0.05f;

        if (cc != null && cc.enabled)
            cc.enabled = false;

        if (rb != null)
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }

        if (nt != null)
            nt.Teleport(spawnPos, uprightRot, tf.localScale);
        else
            tf.SetPositionAndRotation(spawnPos, uprightRot);

        if (rb != null)
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }

        if (cc != null)
            cc.enabled = true;
    }
}