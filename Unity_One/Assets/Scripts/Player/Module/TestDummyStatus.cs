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
    [SerializeField] private float bonusUpwardForce = 20.0f; // [상향] 공중 부양 힘

    private Rigidbody[] _ragdollRbs;
    private Rigidbody _mainRb;
    private NavMeshAgent _agent;
    private TestAIController _aiController;
    private CapsuleCollider _mainCollider;
    private bool _isRagdoll = false;

    private void Awake()
    {
        _ragdollRbs = GetComponentsInChildren<Rigidbody>();
        _mainRb = GetComponent<Rigidbody>();
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

        // 1. AI 기능 차단
        if (_agent != null && _agent.enabled && _agent.isOnNavMesh)
        {
            _agent.ResetPath();
            _agent.velocity = Vector3.zero;
            _agent.isStopped = true;
            _agent.enabled = false;
        }
        if (_aiController != null) _aiController.enabled = false;

        // 2. [투명벽 해결 핵심] 본체 콜라이더를 끄고 레이어를 충돌 무시용(2번)으로 바꿈
        if (_mainCollider != null) _mainCollider.enabled = false;
        int originalLayer = gameObject.layer;
        SetLayerRecursive(gameObject, 2); // 2번은 보통 Ignore Raycast 레이어

        // 3. 물리 켜기
        if (_mainRb != null)
        {
            _mainRb.isKinematic = false;
            _mainRb.useGravity = true;
        }
        ToggleRagdoll(true);

        // 물리 엔진 동기화 대기
        yield return new WaitForFixedUpdate();

        // 4. 충격 적용
        if (hipsBone != null && hipsBone.TryGetComponent(out Rigidbody hipsRb))
        {
            hipsRb.linearVelocity = Vector3.zero;
            if (_mainRb != null) _mainRb.linearVelocity = Vector3.zero;

            // 위로 더 확실히 띄워서 플레이어 머리 위로 보냅니다.
            Vector3 finalForce = (hitForce * forceMultiplier) + (Vector3.up * bonusUpwardForce);
            hipsRb.AddForce(finalForce, ForceMode.Impulse);
            if (_mainRb != null) _mainRb.AddForce(finalForce, ForceMode.Impulse);

            hipsRb.AddTorque(Random.insideUnitSphere * 30f, ForceMode.Impulse);
        }

        // 5. 날아가는 동안 대기 (3초)
        yield return new WaitForSeconds(3.0f);

        // 6. 복구 로직
        if (hipsBone != null)
        {
            Vector3 targetPos = hipsBone.position;
            if (Physics.Raycast(targetPos + Vector3.up, Vector3.down, out RaycastHit hit, 5f))
            {
                targetPos.y = hit.point.y;
            }
            transform.position = targetPos;
        }

        // 레이어 원복 및 물리 끄기
        SetLayerRecursive(gameObject, originalLayer);
        ToggleRagdoll(false);
        if (_mainRb != null) _mainRb.isKinematic = true;
        _isRagdoll = false;

        if (animator != null) animator.SetTrigger("StandUpFront");

        yield return new WaitForSeconds(1.5f);

        // 7. [복구] 다시 본체 콜라이더를 켜서 서로 부딪히게 만듦
        if (_mainCollider != null) _mainCollider.enabled = true;

        if (_agent != null) { _agent.enabled = true; _agent.Warp(transform.position); }
        if (_aiController != null) _aiController.enabled = true;
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

    // 레이어를 자식까지 한꺼번에 바꾸는 헬퍼 함수
    private void SetLayerRecursive(GameObject obj, int newLayer)
    {
        obj.layer = newLayer;
        foreach (Transform child in obj.transform)
        {
            SetLayerRecursive(child.gameObject, newLayer);
        }
    }
}