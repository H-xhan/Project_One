using System.Collections;
using UnityEngine;
using UnityEngine.AI;

public class TestDummyStatus : MonoBehaviour
{
    [Header("Ragdoll Settings")]
    [SerializeField] private Animator animator;
    [SerializeField] private Transform hipsBone;

    private Rigidbody[] _ragdollRbs;
    private NavMeshAgent _agent;
    private TestAIController _aiController;
    private bool _isRagdoll = false;

    private void Awake()
    {
        _ragdollRbs = GetComponentsInChildren<Rigidbody>();
        _agent = GetComponent<NavMeshAgent>();
        _aiController = GetComponent<TestAIController>();

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

        // 1. [긴급] NavMeshAgent와 AI 뇌를 완전히 차단
        // 영상처럼 승천하는 이유는 에이전트가 켜진 채로 위치를 강제 고정하려 하기 때문입니다.
        if (_agent != null)
        {
            _agent.velocity = Vector3.zero;
            _agent.isStopped = true;
            _agent.enabled = false;
        }
        if (_aiController != null) _aiController.enabled = false;

        // 2. 물리 켜기
        ToggleRagdoll(true);

        // 3. 충격 가하기
        Rigidbody hipsRb = null;
        if (hipsBone != null && hipsBone.TryGetComponent(out hipsRb))
        {
            // [팁] 회전력을 살짝 주면 더 찰지게 날아갑니다.
            hipsRb.AddForce(hitForce, ForceMode.Impulse);
            hipsRb.AddTorque(Random.insideUnitSphere * 10f, ForceMode.Impulse);
        }

        // 4. 수달이가 바닥에 완전히 멈출 때까지 기다림 (안정화)
        // 무작정 3초를 기다리는 것보다 속도가 줄었을 때 일어나는 게 자연스럽습니다.
        float timer = 0f;
        while (timer < 3.0f)
        {
            timer += Time.deltaTime;
            // 엉덩이 속도가 아주 낮아지면 일찍 종료 가능
            if (timer > 0.5f && hipsRb != null && hipsRb.linearVelocity.magnitude < 0.1f) break;
            yield return null;
        }

        // 5. [중요] 기상 전 위치 보정
        // 랙돌이 날아간 곳으로 본체의 위치를 옮겨줍니다.
        if (hipsBone != null)
        {
            transform.position = new Vector3(hipsBone.position.x, transform.position.y, hipsBone.position.z);
        }

        // 6. 물리 끄고 애니메이션 모드로 복귀
        ToggleRagdoll(false);
        _isRagdoll = false;

        // 7. 기상 애니메이션 실행
        if (animator != null) animator.SetTrigger("StandUpFront");

        // 8. 애니메이션이 끝날 때까지 잠시 대기 후 AI 재개
        yield return new WaitForSeconds(1.5f);

        if (_agent != null)
        {
            _agent.enabled = true;
            _agent.Warp(transform.position); // 현재 위치로 에이전트 좌표 갱신
        }
        if (_aiController != null) _aiController.enabled = true;
    }

    private void ToggleRagdoll(bool state)
    {
        if (animator != null) animator.enabled = !state;

        foreach (var rb in _ragdollRbs)
        {
            if (rb.gameObject == gameObject) continue;
            rb.isKinematic = !state;

            // [추가] 팽이처럼 도는 버그 방지 (마찰력/공기저항 증가)
            if (state)
            {
                rb.linearDamping = 1f; // Drag
                rb.angularDamping = 5f; // Angular Drag
            }
        }

        var mainCollider = GetComponent<CapsuleCollider>();
        if (mainCollider != null) mainCollider.enabled = !state;
    }
}