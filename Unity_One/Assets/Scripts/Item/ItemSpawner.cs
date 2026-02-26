using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

public class ItemSpawner : NetworkBehaviour
{
    [System.Serializable]
    public class SpawnEntry
    {
        [Tooltip("알아보기 쉬운 이름 (예: 시작 지점 망치)")]
        public string name = "Item";

        [Tooltip("소환할 아이템 프리팹 (반드시 NetworkObject가 있어야 함)")]
        public GameObject prefab;

        [Tooltip("소환될 위치")]
        public Vector3 position;

        [Tooltip("소환될 회전값")]
        public Vector3 rotation;
    }

    [Header("Spawn Settings")]
    [Tooltip("비워두면 어떤 씬에서도 스폰. 특정 씬에서만 스폰하려면 씬 이름 입력 (예: InGame)")]
    [SerializeField] private string spawnOnlyInSceneName = "";

    [Tooltip("같은 씬 로드 사이클에서 중복 스폰을 막음")]
    [SerializeField] private bool preventDuplicateSpawnPerScene = true;

    [Header("Spawn List")]
    [Tooltip("여기에서 + 버튼을 눌러 아이템을 추가하세요")]
    [SerializeField] private List<SpawnEntry> itemsToSpawn = new List<SpawnEntry>();

    private int _lastSpawnedSceneHandle = int.MinValue;
    private bool _subscribed;
    private Coroutine _initRoutine;

    private void Start()
    {
        if (_initRoutine == null)
            _initRoutine = StartCoroutine(ServerInitRoutine());
    }

    public override void OnNetworkSpawn()
    {
        if (!IsServerNow()) return;

        HookSceneEventsOnce();
        TrySpawnForActiveSceneServer();
    }

    private IEnumerator ServerInitRoutine()
    {
        while (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening)
            yield return null;

        if (!IsServerNow()) yield break;

        HookSceneEventsOnce();
        TrySpawnForActiveSceneServer();
    }

    private bool IsServerNow()
    {
        return NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer;
    }

    private void HookSceneEventsOnce()
    {
        if (_subscribed) return;
        if (NetworkManager.Singleton == null) return;
        if (NetworkManager.Singleton.SceneManager == null) return;

        NetworkManager.Singleton.SceneManager.OnLoadEventCompleted += OnLoadEventCompletedServer;
        _subscribed = true;
    }

    public override void OnDestroy()
    {
        if (_subscribed)
        {
            if (NetworkManager.Singleton != null && NetworkManager.Singleton.SceneManager != null)
            {
                NetworkManager.Singleton.SceneManager.OnLoadEventCompleted -= OnLoadEventCompletedServer;
            }
            _subscribed = false;
        }

        base.OnDestroy();
    }

    private void OnLoadEventCompletedServer(string sceneName, LoadSceneMode mode, List<ulong> clientsCompleted, List<ulong> clientsTimedOut)
    {
        if (!IsServerNow()) return;
        TrySpawnForActiveSceneServer();
    }

    private void TrySpawnForActiveSceneServer()
    {
        if (!IsServerNow()) return;

        string activeSceneName = SceneManager.GetActiveScene().name;

        if (!string.IsNullOrEmpty(spawnOnlyInSceneName) && activeSceneName != spawnOnlyInSceneName)
            return;

        int handle = SceneManager.GetActiveScene().handle;

        if (preventDuplicateSpawnPerScene && handle == _lastSpawnedSceneHandle)
            return;

        _lastSpawnedSceneHandle = handle;

        SpawnAllServer(activeSceneName);
    }

    private void SpawnAllServer(string sceneName)
    {
        for (int i = 0; i < itemsToSpawn.Count; i++)
        {
            var entry = itemsToSpawn[i];
            if (entry == null || entry.prefab == null) continue;

            SpawnItem(entry);
        }

        Debug.Log($"[ItemSpawner] SpawnAll 완료 (Scene: {sceneName}) Count={itemsToSpawn.Count}");
    }

    private void SpawnItem(SpawnEntry entry)
    {
        Quaternion rot = Quaternion.Euler(entry.rotation);
        GameObject newItem = Instantiate(entry.prefab, entry.position, rot);

        SceneManager.MoveGameObjectToScene(newItem, SceneManager.GetActiveScene());

        NetworkObject netObj = newItem.GetComponent<NetworkObject>();
        if (netObj != null)
        {
            netObj.Spawn();
            Debug.Log($"[ItemSpawner] {entry.name} 소환 완료! 위치: {entry.position}");
        }
        else
        {
            Debug.LogError($"[ItemSpawner] {entry.name} 프리팹에 NetworkObject 컴포넌트가 없습니다!");
        }
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.cyan;
        foreach (var entry in itemsToSpawn)
        {
            Gizmos.DrawWireSphere(entry.position, 0.5f);
            Gizmos.DrawRay(entry.position, Vector3.up * 1f);
        }
    }
}