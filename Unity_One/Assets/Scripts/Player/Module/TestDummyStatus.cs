using System.Collections;
using UnityEngine;
using UnityEngine.AI;
using Unity.Netcode;

public class TestDummyStatus : MonoBehaviour
{
    [Header("Ragdoll Settings")]
    [SerializeField] private Animator animator;
    [SerializeField] private Transform hipsBone;

    [Header("Physics Tuning")]
    [SerializeField] private float forceMultiplier = 8.0f;
    [SerializeField] private float bonusUpwardForce = 20.0f;

    [Header("Reward Settings")]
    [SerializeField] private GameObject coinPrefab;
    [SerializeField] private int coinCount = 3;

    private Rigidbody[] _ragdollRbs;
    private Rigidbody _mainRb;
    private NavMeshAgent _agent;
    private TestAIController _aiController;
    private CapsuleCollider _mainCollider;
    private bool _isRagdoll = false;
    private int _originalLayer;

    private void Awake()
    {
        _ragdollRbs = GetComponentsInChildren<Rigidbody>();
        _mainRb = GetComponent<Rigidbody>();
        _agent = GetComponent<NavMeshAgent>();
        _aiController = GetComponent<TestAIController>();
        _mainCollider = GetComponent<CapsuleCollider>();
        _originalLayer = gameObject.layer;

        ToggleRagdoll(false);
    }

    public void TakeHit(Vector3 hitForce)
    {
        if (_isRagdoll) return;
        StartCoroutine(RagdollRoutine(hitForce));
    }

    private IEnumerator RagdollRoutine(Vector3 hitForce)
    {
        _isRagdoll = true;
        GameObject player = GameObject.FindWithTag("Player");

        // 1. 유령 상태 및 AI 정지
        if (player != null) SetCollisionWithPlayer(player, true);
        if (_agent != null) _agent.enabled = false;
        if (_aiController != null) _aiController.enabled = false;
        if (_mainCollider != null) _mainCollider.enabled = false;

        SetLayerRecursive(gameObject, 2);

        // 2. 물리 켜기 (Kinematic 해제)
        ToggleRagdoll(true);
        if (_mainRb != null) _mainRb.isKinematic = false;

        // 한 프레임 대기하여 물리 엔진이 활성화되도록 함
        yield return new WaitForFixedUpdate();

        SpawnCoins(); // 코인 드랍

        // 3. 넉백 적용 (경고 로그 방지를 위해 속도 대입 대신 힘 추가 위주로 처리)
        if (hipsBone != null && hipsBone.TryGetComponent(out Rigidbody hipsRb))
        {
            // 물리 엔진이 완전히 켜진 상태에서만 힘을 가함
            Vector3 finalForce = (hitForce * forceMultiplier) + (Vector3.up * bonusUpwardForce);
            hipsRb.AddForce(finalForce, ForceMode.Impulse);
            hipsRb.AddTorque(Random.insideUnitSphere * 10f, ForceMode.Impulse);
        }

        // 4. 비행 및 낙하 대기 (3초)
        yield return new WaitForSeconds(3.0f);

        // 5. 지면 안착 및 복구 위치 잡기
        if (hipsBone != null)
        {
            Vector3 targetPos = hipsBone.position;
            if (NavMesh.SamplePosition(targetPos, out NavMeshHit navHit, 5.0f, NavMesh.AllAreas))
            {
                transform.position = navHit.position + Vector3.up * 0.05f;
            }
        }

        // 6. 물리 끄기 및 기상 준비
        ToggleRagdoll(false);
        if (_mainRb != null) _mainRb.isKinematic = true;

        if (animator != null) animator.SetTrigger("StandUpFront");

        // 7. 기상 완료 대기 (3초)
        yield return new WaitForSeconds(3.0f);

        // 8. 상태 복구
        SetLayerRecursive(gameObject, _originalLayer);
        if (_mainCollider != null) _mainCollider.enabled = true;

        // 9. 투명벽 방지 거리 체크
        if (player != null)
        {
            float safeDistance = 1.0f;
            while (Vector3.Distance(transform.position, player.transform.position) < safeDistance)
            {
                yield return new WaitForSeconds(0.1f);
            }
            SetCollisionWithPlayer(player, false);
        }

        // 10. AI 지능 가동
        if (_agent != null)
        {
            _agent.enabled = true;
            _agent.Warp(transform.position);
        }
        if (_aiController != null) _aiController.enabled = true;

        _isRagdoll = false;
    }

    private void SetCollisionWithPlayer(GameObject player, bool ignore)
    {
        Collider[] playerColls = player.GetComponentsInChildren<Collider>();
        Collider[] myColls = GetComponentsInChildren<Collider>();
        foreach (var pCol in playerColls)
        {
            foreach (var myCol in myColls)
                if (pCol != null && myCol != null) Physics.IgnoreCollision(pCol, myCol, ignore);
        }
    }

    private void ToggleRagdoll(bool state)
    {
        if (animator != null) animator.enabled = !state;
        foreach (var rb in _ragdollRbs)
        {
            if (rb.transform == transform) continue;
            rb.isKinematic = !state;
        }
    }

    private void SetLayerRecursive(GameObject obj, int newLayer)
    {
        obj.layer = newLayer;
        foreach (Transform child in obj.transform) SetLayerRecursive(child.gameObject, newLayer);
    }

    private void SpawnCoins()
    {
        if (coinPrefab == null) return;
        for (int i = 0; i < coinCount; i++)
        {
            GameObject coin = Instantiate(coinPrefab, transform.position + Vector3.up, Quaternion.identity);
            var networkObj = coin.GetComponent<NetworkObject>();
            if (networkObj != null) networkObj.Spawn();

            if (coin.TryGetComponent(out Rigidbody rb))
                rb.AddForce((Random.insideUnitSphere + Vector3.up) * 5f, ForceMode.Impulse);
        }
    }
}