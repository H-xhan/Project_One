using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

[RequireComponent(typeof(NetworkObject))]
public class CoinSpawnManager : NetworkBehaviour
{
    [SerializeField, Tooltip("서버에서 생성할 코인 프리팹입니다. NetworkObject와 CoinPickup이 포함되어 있어야 합니다.")]
    private CoinPickup coinPrefab;

    [SerializeField, Tooltip("코인이 생성될 위치 목록입니다. 비어 있으면 코인을 생성하지 않습니다.")]
    private Transform[] spawnPoints;

    [SerializeField, Tooltip("라운드 시작 시 서버가 처음 생성할 코인 수입니다.")]
    private int initialSpawnCount = 20;

    [SerializeField, Tooltip("맵에 동시에 존재할 수 있는 최대 코인 수입니다.")]
    private int maximumActiveCoins = 35;

    [SerializeField, Tooltip("코인이 부족할 때 서버가 새 코인을 생성하는 간격입니다.")]
    private float respawnInterval = 6f;

    [SerializeField, Tooltip("스폰 포인트 주변에 적용할 수평 랜덤 오프셋 반경입니다.")]
    private float spawnPositionRandomRadius = 0.25f;

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

        Transform spawnPoint = GetRandomSpawnPoint();
        if (spawnPoint == null) return false;

        return ServerTrySpawnCoinAt(spawnPoint);
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

        CoinPickup spawnedCoin = Instantiate(coinPrefab, position, rotation);
        NetworkObject spawnedNetworkObject = spawnedCoin.GetComponent<NetworkObject>();
        if (spawnedNetworkObject == null)
        {
            Destroy(spawnedCoin.gameObject);
            return false;
        }

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
    }
}
