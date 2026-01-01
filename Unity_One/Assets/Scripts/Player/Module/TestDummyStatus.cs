using System.Collections;
using UnityEngine;
using UnityEngine.AI;

public class TestDummyStatus : MonoBehaviour
{
    [Header("Ragdoll Settings")]
    [SerializeField] private Animator animator;
    [SerializeField] private Transform hipsBone;

    [Header("Physics Tuning")]
    [SerializeField] private float forceMultiplier = 8.0f;  // 힘 증폭 배율
    [SerializeField] private float bonusUpwardForce = 15.0f; // 추가 공중 부양 힘

    private Rigidbody[] _ragdollRbs;
    private Rigidbody _mainRb; // [추가] 본체 리지드바디 제어용
    private NavMeshAgent _agent;
    private TestAIController _aiController;
    private CapsuleCollider _mainCollider;
    private bool _isRagdoll = false;

    private void Awake()
    {
        _ragdollRbs = GetComponentsInChildren<Rigidbody>();
        _mainRb = GetComponent<Rigidbody>(); // 본체 Rigidbody 참조
        _agent = GetComponent<NavMeshAgent>();
        _aiController = GetComponent<TestAIController>();
        _mainCollider = GetComponent<CapsuleCollider>();

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

        // 1. AI 기능 차단 (안전장치 강화)
        if (_agent != null && _agent.enabled && _agent.isOnNavMesh)
        {
            _agent.ResetPath();
            _agent.velocity = Vector3.zero;
            _agent.isStopped = true;
            _agent.enabled = false;
        }
        if (_aiController != null) _aiController.enabled = false;
        if (_mainCollider != null) _mainCollider.enabled = false;

        // 2. 물리 켜기
        if (_mainRb != null)
        {
            _mainRb.isKinematic = false;
            _mainRb.useGravity = true;
        }
        ToggleRagdoll(true);

        // 물리 엔진 동기화 대기
        yield return new WaitForFixedUpdate();

        // 3. 충격 적용 (날아가는 파워 상향)
        if (hipsBone != null && hipsBone.TryGetComponent(out Rigidbody hipsRb))
        {
            hipsRb.linearVelocity = Vector3.zero;
            if (_mainRb != null) _mainRb.linearVelocity = Vector3.zero;

            // [핵심] hitForce에 더 큰 배율을 곱하고, 위로 뜨는 힘(up)을 섞어줍니다.
            // 그래야 바닥에 긁히지 않고 포물선을 그리며 멀리 날아갑니다.
            Vector3 finalForce = (hitForce * forceMultiplier) + (Vector3.up * bonusUpwardForce);
            hipsRb.AddForce(finalForce, ForceMode.Impulse);
            if (_mainRb != null) _mainRb.AddForce(finalForce, ForceMode.Impulse);

            hipsRb.AddTorque(Random.insideUnitSphere * 30f, ForceMode.Impulse);
        }

        // 4. 날아가는 동안 대기 (3초)
        yield return new WaitForSeconds(3.0f);

        // 5. 복구 로직 (기존과 동일)
        if (hipsBone != null)
        {
            Vector3 targetPos = hipsBone.position;
            if (Physics.Raycast(targetPos + Vector3.up, Vector3.down, out RaycastHit hit, 5f))
            {
                targetPos.y = hit.point.y;
            }
            transform.position = targetPos;
        }

        ToggleRagdoll(false);
        if (_mainRb != null) _mainRb.isKinematic = true;
        _isRagdoll = false;

        if (animator != null) animator.SetTrigger("StandUpFront");

        yield return new WaitForSeconds(1.5f);

        if (_mainCollider != null) _mainCollider.enabled = true;
        if (_agent != null) { _agent.enabled = true; _agent.Warp(transform.position); }
        if (_aiController != null) _aiController.enabled = true;
    }

    private void ToggleRagdoll(bool state)
    {
        if (animator != null) animator.enabled = !state;

        foreach (var rb in _ragdollRbs)
        {
            // 본체 리지드바디는 별도로 제어하므로 제외
            if (rb.gameObject == gameObject) continue;

            rb.isKinematic = !state;

            if (state) // 날아갈 때의 공기 저항값 설정
            {
                rb.linearDamping = 0.5f;
                rb.angularDamping = 2f;
                rb.collisionDetectionMode = CollisionDetectionMode.Continuous;
            }
        }
    }
}