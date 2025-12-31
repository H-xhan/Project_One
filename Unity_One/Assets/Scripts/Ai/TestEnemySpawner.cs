using UnityEngine;

public class TestEnemySpawner : MonoBehaviour
{
    [Header("소환할 적 프리팹")]
    public GameObject enemyPrefab;

    [Header("소환 위치")]
    public Transform spawnPoint; // 소환될 위치 (빈 오브젝트)

    void Start()
    {
        SpawnEnemy();
    }

    void SpawnEnemy()
    {
        if (enemyPrefab != null)
        {
            // 1. 생성 (Instantiate)
            GameObject enemy = Instantiate(enemyPrefab);

            // 2. 위치 이동 (SpawnPoint가 있으면 거기로, 없으면 내 위치로)
            if (spawnPoint != null)
            {
                // NavMeshAgent가 있으면 Warp로 이동시켜야 안전함
                UnityEngine.AI.NavMeshAgent agent = enemy.GetComponent<UnityEngine.AI.NavMeshAgent>();
                if (agent != null)
                {
                    agent.Warp(spawnPoint.position);
                }
                else
                {
                    enemy.transform.position = spawnPoint.position;
                }
            }
            else
            {
                enemy.transform.position = transform.position;
            }

            Debug.Log("👾 테스트용 적 AI 소환 완료!");
        }
    }
}