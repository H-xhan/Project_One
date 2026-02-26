using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

public class InGameMatchManager : NetworkBehaviour
{
    [Header("플레이어 스폰")]
    [SerializeField, Tooltip("인게임에서 스폰에 사용할 플레이어 프리팹(NetworkObject 포함). 비어있으면 NetworkManager의 PlayerPrefab을 사용합니다.")]
    private NetworkObject playerPrefab;

    [SerializeField, Tooltip("인게임 스폰포인트 태그")]
    private string gameSpawnTag = "GameSpawnPoint";

    [SerializeField, Tooltip("씬 로드 직후 스폰/텔레포트 지연(초). 네트워크/로딩이 느리면 올리세요.")]
    private float spawnAndTeleportDelay = 0.1f;

    private bool _didEnsureThisScene;

    public override void OnNetworkSpawn()
    {
        if (NetworkManager.Singleton == null) return;

        if (NetworkManager.Singleton.SceneManager != null)
            NetworkManager.Singleton.SceneManager.OnLoadEventCompleted += OnLoadEventCompleted;

        // 호스트에서 이미 인게임 씬으로 들어온 상태에서 스폰되는 케이스 안전 처리
        if (IsServer)
            TryEnsureNow();
    }

    public override void OnDestroy()
    {
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.SceneManager != null)
        {
            NetworkManager.Singleton.SceneManager.OnLoadEventCompleted -= OnLoadEventCompleted;
        }

        base.OnDestroy();
    }

    private void OnLoadEventCompleted(string sceneName, LoadSceneMode loadMode, List<ulong> clientsCompleted, List<ulong> clientsTimedOut)
    {
        if (!IsServer) return;

        // 이 오브젝트가 인게임 씬에만 있다면 사실상 항상 true지만, 안전하게 한 번 더 체크
        if (SceneManager.GetActiveScene().name != sceneName) return;

        TryEnsureNow();
    }

    private void TryEnsureNow()
    {
        if (!IsServer) return;
        if (_didEnsureThisScene) return;

        _didEnsureThisScene = true;
        StartCoroutine(ServerEnsurePlayersAndTeleportRoutine());
    }

    private IEnumerator ServerEnsurePlayersAndTeleportRoutine()
    {
        if (spawnAndTeleportDelay > 0f)
            yield return new WaitForSeconds(spawnAndTeleportDelay);

        var nm = NetworkManager.Singleton;
        if (nm == null)
        {
            Debug.LogWarning("[InGameMatchManager] NetworkManager.Singleton is null.");
            yield break;
        }

        // 스폰포인트 수집
        var spawnPoints = FindSpawnPointsByTag(gameSpawnTag);
        if (spawnPoints.Count == 0)
        {
            Debug.LogWarning($"[InGameMatchManager] SpawnPoint tag not found or empty: {gameSpawnTag}");
        }

        int index = 0;

        foreach (var clientId in nm.ConnectedClientsIds)
        {
            var client = nm.ConnectedClients[clientId];
            NetworkObject playerObj = client.PlayerObject;

            // 1) 플레이어가 없으면 스폰
            if (playerObj == null || !playerObj.IsSpawned)
            {
                var prefabToUse = ResolvePlayerPrefab(nm);
                if (prefabToUse == null)
                {
                    Debug.LogError("[InGameMatchManager] playerPrefab이 비어 있습니다. 인스펙터에 플레이어 프리팹(NetworkObject)을 넣어주세요.");
                    yield break;
                }

                var instance = Instantiate(prefabToUse.gameObject);
                playerObj = instance.GetComponent<NetworkObject>();
                playerObj.SpawnAsPlayerObject(clientId, true);
            }

            // 2) 스폰포인트 텔레포트
            if (spawnPoints.Count > 0 && playerObj != null)
            {
                var sp = spawnPoints[index % spawnPoints.Count];
                TeleportPlayerSafely(playerObj, sp.position, sp.rotation);
                index++;
            }
        }
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

    private void TeleportPlayerSafely(NetworkObject player, Vector3 pos, Quaternion rot)
    {
        var go = player.gameObject;

        // CharacterController가 있으면 순간 이동 시 충돌 문제 방지
        var cc = go.GetComponent<CharacterController>();
        if (cc != null) cc.enabled = false;

        go.transform.SetPositionAndRotation(pos, rot);

        if (cc != null) cc.enabled = true;
    }
}
