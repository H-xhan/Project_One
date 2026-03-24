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

        [Tooltip("이 아이템이 생성될 전용 스폰 포인트")]
        public Transform spawnPoint;

        [Tooltip("스폰 포인트 기준 로컬 위치 오프셋")]
        public Vector3 localPositionOffset = Vector3.zero;

        [Tooltip("스폰 포인트 기준 로컬 회전 오프셋")]
        public Vector3 localRotationOffset = Vector3.zero;

        [Tooltip("스폰 포인트가 비어 있을 때만 사용할 예비 위치")]
        public Vector3 fallbackPosition = Vector3.zero;

        [Tooltip("스폰 포인트가 비어 있을 때만 사용할 예비 회전값")]
        public Vector3 fallbackRotation = Vector3.zero;
    }

    [Header("Spawn Settings")]
    [Tooltip("비워두면 어떤 씬에서도 스폰. 특정 씬에서만 스폰하려면 씬 이름 입력 (예: InGame)")]
    [SerializeField] private string spawnOnlyInSceneName = "";

    [Tooltip("같은 씬 로드 사이클에서 중복 스폰을 막음")]
    [SerializeField] private bool preventDuplicateSpawnPerScene = true;

    [Header("Spawn List")]
    [Tooltip("각 아이템마다 전용 스폰 포인트를 연결하세요")]
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
        base.OnNetworkSpawn();

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
            if (entry == null || entry.prefab == null)
                continue;

            SpawnItem(entry);
        }

        Debug.Log($"[ItemSpawner] SpawnAll 완료 (Scene: {sceneName}) Count={itemsToSpawn.Count}");
    }

    private void SpawnItem(SpawnEntry entry)
    {
        Vector3 spawnPos = ResolveSpawnPosition(entry);
        Quaternion spawnRot = ResolveSpawnRotation(entry);

        GameObject newItem = Instantiate(entry.prefab, spawnPos, spawnRot);
        SceneManager.MoveGameObjectToScene(newItem, SceneManager.GetActiveScene());

        NetworkObject netObj = newItem.GetComponent<NetworkObject>();
        if (netObj != null)
        {
            netObj.Spawn();
            Debug.Log($"[ItemSpawner] {entry.name} 소환 완료! 위치: {spawnPos}");
        }
        else
        {
            Debug.LogError($"[ItemSpawner] {entry.name} 프리팹에 NetworkObject 컴포넌트가 없습니다!");
        }
    }

    private Vector3 ResolveSpawnPosition(SpawnEntry entry)
    {
        if (entry.spawnPoint != null)
            return entry.spawnPoint.TransformPoint(entry.localPositionOffset);

        return entry.fallbackPosition;
    }

    private Quaternion ResolveSpawnRotation(SpawnEntry entry)
    {
        if (entry.spawnPoint != null)
            return entry.spawnPoint.rotation * Quaternion.Euler(entry.localRotationOffset);

        return Quaternion.Euler(entry.fallbackRotation);
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.cyan;

        if (itemsToSpawn == null) return;

        foreach (var entry in itemsToSpawn)
        {
            if (entry == null) continue;

            Vector3 gizmoPos = entry.spawnPoint != null
                ? entry.spawnPoint.TransformPoint(entry.localPositionOffset)
                : entry.fallbackPosition;

            Gizmos.DrawWireSphere(gizmoPos, 0.15f);
            Gizmos.DrawRay(gizmoPos, Vector3.up * 0.5f);
        }
    }
}