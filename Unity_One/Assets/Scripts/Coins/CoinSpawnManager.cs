using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

[RequireComponent(typeof(NetworkObject))]
public class CoinSpawnManager : NetworkBehaviour
{
    [SerializeField, Tooltip("서버에서 생성할 코인 프리팹입니다. NetworkObject가 포함되어 있어야 합니다.")]
    private NetworkObject coinPrefab;

    [SerializeField, Tooltip("코인이 생성될 위치 목록입니다. 비어 있으면 코인을 생성하지 않습니다.")]
    private Transform[] spawnPoints;

    [SerializeField, Tooltip("코인이 랜덤 위치에 생성될 BoxCollider 기반 스폰 영역 목록입니다. 값이 있으면 Spawn Points보다 우선 사용합니다.")]
    private BoxCollider[] spawnAreas;

    [SerializeField, Tooltip("라운드 시작 시 서버가 처음 생성할 코인 수입니다.")]
    private int initialSpawnCount = 20;

    [SerializeField, Tooltip("맵에 동시에 존재할 수 있는 최대 코인 수입니다.")]
    private int maximumActiveCoins = 35;

    [SerializeField, Tooltip("코인이 부족할 때 서버가 새 코인을 생성하는 간격입니다.")]
    private float respawnInterval = 6f;

    [SerializeField, Tooltip("스폰 포인트 주변에 적용할 수평 랜덤 오프셋 반경입니다.")]
    private float spawnPositionRandomRadius = 0.25f;

    [SerializeField, Tooltip("스폰 영역이 있을 때 기존 Spawn Points보다 스폰 영역을 우선 사용할지 여부입니다.")]
    private bool preferSpawnAreas = true;

    [SerializeField, Tooltip("스폰 영역의 윗면 기준으로 코인을 얼마나 위에 생성할지 설정합니다.")]
    private float areaSpawnHeightOffset = 0.15f;

    [SerializeField, Tooltip("유효한 랜덤 스폰 위치를 찾기 위해 시도할 최대 횟수입니다.")]
    private int maxSpawnPositionAttempts = 20;

    [SerializeField, Tooltip("코인이 다른 오브젝트와 겹치는 위치에 생성되지 않도록 검사할지 여부입니다.")]
    private bool useSpawnBlockingCheck = false;

    [SerializeField, Tooltip("스폰 위치 겹침 검사에 사용할 레이어입니다.")]
    private LayerMask spawnBlockingLayers;

    [SerializeField, Tooltip("스폰 위치 겹침 검사용 구 반경입니다.")]
    private float spawnBlockingCheckRadius = 0.25f;

    [SerializeField, Tooltip("네트워크 스폰 시 서버가 초기 코인을 자동 생성할지 여부입니다.")]
    private bool spawnInitialCoinsOnNetworkSpawn = true;

    [SerializeField, Tooltip("서버가 주기적으로 필드 코인 수를 최대치에 가깝게 보충할지 여부입니다.")]
    private bool maintainMaximumCoins = true;

    [SerializeField, Tooltip("코인 생성 시 Y축 회전을 랜덤으로 적용할지 여부입니다.")]
    private bool randomizeYaw = true;

    private readonly List<NetworkObject> _spawnedCoins = new List<NetworkObject>();
    private float _nextRespawnTime;

    public int ActiveCoinCount
    {
        get
        {
            PruneSpawnedCoins();
            return _spawnedCoins.Count;
        }
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        if (!IsServer) return;

        if (spawnInitialCoinsOnNetworkSpawn)
            ServerSpawnInitialCoins();

        _nextRespawnTime = Time.time + Mathf.Max(0f, respawnInterval);
    }

    private void Update()
    {
        if (!IsServer) return;

        PruneSpawnedCoins();

        if (!maintainMaximumCoins) return;
        if (Time.time < _nextRespawnTime) return;

        _nextRespawnTime = Time.time + Mathf.Max(0f, respawnInterval);

        if (_spawnedCoins.Count < Mathf.Max(0, maximumActiveCoins))
            ServerTrySpawnCoin();
    }

    public bool ServerTrySpawnCoin()
    {
        if (!IsServer) return false;

        PruneSpawnedCoins();
        if (_spawnedCoins.Count >= Mathf.Max(0, maximumActiveCoins)) return false;

        if (!TryGetRandomSpawnPose(out Vector3 position, out Quaternion rotation))
            return false;

        return ServerTrySpawnCoinAtPosition(position, rotation);
    }

    public bool ServerTrySpawnCoinAt(Transform spawnPoint)
    {
        if (!IsServer) return false;
        if (spawnPoint == null) return false;

        PruneSpawnedCoins();
        if (_spawnedCoins.Count >= Mathf.Max(0, maximumActiveCoins)) return false;

        return ServerTrySpawnCoinAtPosition(GetSpawnPosition(spawnPoint), GetSpawnRotation(spawnPoint));
    }

    public bool ServerTrySpawnCoinAtPosition(Vector3 position, Quaternion rotation)
    {
        if (!IsServer) return false;
        if (coinPrefab == null) return false;

        PruneSpawnedCoins();
        if (_spawnedCoins.Count >= Mathf.Max(0, maximumActiveCoins)) return false;

        NetworkObject spawnedNetworkObject = Instantiate(coinPrefab, position, rotation);
        spawnedNetworkObject.Spawn(true);
        _spawnedCoins.Add(spawnedNetworkObject);
        return true;
    }

    public int ServerSpawnInitialCoins()
    {
        if (!IsServer) return 0;

        PruneSpawnedCoins();

        int availableSlots = Mathf.Max(0, maximumActiveCoins) - _spawnedCoins.Count;
        int spawnCount = Mathf.Min(Mathf.Max(0, initialSpawnCount), Mathf.Max(0, availableSlots));
        int spawnedCount = 0;

        for (int i = 0; i < spawnCount; i++)
        {
            if (!ServerTrySpawnCoin())
                break;

            spawnedCount++;
        }

        return spawnedCount;
    }

    public void ServerDespawnAllCoins()
    {
        if (!IsServer) return;

        for (int i = _spawnedCoins.Count - 1; i >= 0; i--)
        {
            NetworkObject spawnedCoin = _spawnedCoins[i];
            if (spawnedCoin != null && spawnedCoin.IsSpawned)
                spawnedCoin.Despawn(true);
        }

        _spawnedCoins.Clear();
    }

    private void PruneSpawnedCoins()
    {
        for (int i = _spawnedCoins.Count - 1; i >= 0; i--)
        {
            NetworkObject spawnedCoin = _spawnedCoins[i];
            if (spawnedCoin == null || !spawnedCoin.IsSpawned)
                _spawnedCoins.RemoveAt(i);
        }
    }

    private Transform GetRandomSpawnPoint()
    {
        if (spawnPoints == null || spawnPoints.Length == 0)
            return null;

        int validCount = 0;
        for (int i = 0; i < spawnPoints.Length; i++)
        {
            if (spawnPoints[i] != null)
                validCount++;
        }

        if (validCount <= 0)
            return null;

        int selectedIndex = Random.Range(0, validCount);
        for (int i = 0; i < spawnPoints.Length; i++)
        {
            if (spawnPoints[i] == null)
                continue;

            if (selectedIndex == 0)
                return spawnPoints[i];

            selectedIndex--;
        }

        return null;
    }

    private bool HasValidSpawnAreas()
    {
        if (spawnAreas == null || spawnAreas.Length == 0)
            return false;

        for (int i = 0; i < spawnAreas.Length; i++)
        {
            if (spawnAreas[i] != null && spawnAreas[i].enabled)
                return true;
        }

        return false;
    }

    private BoxCollider GetRandomSpawnArea()
    {
        if (!HasValidSpawnAreas())
            return null;

        int validCount = 0;
        for (int i = 0; i < spawnAreas.Length; i++)
        {
            if (spawnAreas[i] != null && spawnAreas[i].enabled)
                validCount++;
        }

        int selectedIndex = Random.Range(0, validCount);
        for (int i = 0; i < spawnAreas.Length; i++)
        {
            BoxCollider spawnArea = spawnAreas[i];
            if (spawnArea == null || !spawnArea.enabled)
                continue;

            if (selectedIndex == 0)
                return spawnArea;

            selectedIndex--;
        }

        return null;
    }

    private bool TryGetRandomAreaSpawnPose(out Vector3 position, out Quaternion rotation)
    {
        position = Vector3.zero;
        rotation = Quaternion.identity;

        if (!HasValidSpawnAreas())
            return false;

        int attemptCount = Mathf.Max(0, maxSpawnPositionAttempts);
        if (attemptCount <= 0)
            return false;

        for (int i = 0; i < attemptCount; i++)
        {
            BoxCollider spawnArea = GetRandomSpawnArea();
            if (spawnArea == null)
                return false;

            Vector3 center = spawnArea.center;
            Vector3 size = spawnArea.size;
            Vector3 localPosition = new Vector3(
                Random.Range(center.x - size.x * 0.5f, center.x + size.x * 0.5f),
                center.y + size.y * 0.5f + areaSpawnHeightOffset,
                Random.Range(center.z - size.z * 0.5f, center.z + size.z * 0.5f)
            );

            Vector3 candidatePosition = spawnArea.transform.TransformPoint(localPosition);
            if (IsSpawnPositionBlocked(candidatePosition))
                continue;

            position = candidatePosition;
            rotation = GetSpawnRotation(spawnArea.transform);
            return true;
        }

        return false;
    }

    private bool IsSpawnPositionBlocked(Vector3 position)
    {
        if (!useSpawnBlockingCheck)
            return false;

        float radius = Mathf.Max(0f, spawnBlockingCheckRadius);
        if (radius <= 0f)
            return false;

        return Physics.CheckSphere(position, radius, spawnBlockingLayers, QueryTriggerInteraction.Ignore);
    }

    private bool TryGetRandomSpawnPose(out Vector3 position, out Quaternion rotation)
    {
        position = Vector3.zero;
        rotation = Quaternion.identity;

        if (preferSpawnAreas && TryGetRandomAreaSpawnPose(out position, out rotation))
            return true;

        Transform spawnPoint = GetRandomSpawnPoint();
        if (spawnPoint != null)
        {
            position = GetSpawnPosition(spawnPoint);
            rotation = GetSpawnRotation(spawnPoint);
            return true;
        }

        if (!preferSpawnAreas && TryGetRandomAreaSpawnPose(out position, out rotation))
            return true;

        return false;
    }

    private Vector3 GetSpawnPosition(Transform spawnPoint)
    {
        if (spawnPoint == null)
            return Vector3.zero;

        Vector3 position = spawnPoint.position;
        Vector2 randomOffset = Random.insideUnitCircle * Mathf.Max(0f, spawnPositionRandomRadius);
        position.x += randomOffset.x;
        position.z += randomOffset.y;
        return position;
    }

    private Quaternion GetSpawnRotation(Transform spawnPoint)
    {
        if (spawnPoint == null)
            return Quaternion.identity;

        if (randomizeYaw)
            return Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);

        return spawnPoint.rotation;
    }

    private void OnValidate()
    {
        initialSpawnCount = Mathf.Max(0, initialSpawnCount);
        maximumActiveCoins = Mathf.Max(0, maximumActiveCoins);
        respawnInterval = Mathf.Max(0f, respawnInterval);
        spawnPositionRandomRadius = Mathf.Max(0f, spawnPositionRandomRadius);
        areaSpawnHeightOffset = Mathf.Max(0f, areaSpawnHeightOffset);
        maxSpawnPositionAttempts = Mathf.Max(0, maxSpawnPositionAttempts);
        spawnBlockingCheckRadius = Mathf.Max(0f, spawnBlockingCheckRadius);
    }
}
