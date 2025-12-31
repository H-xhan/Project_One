using UnityEngine;
using UnityEngine.AI;

public class TestAIController : MonoBehaviour
{
    [Header("AI 설정")]
    public float wanderRadius = 10f;
    public float detectRange = 5f;
    public float moveSpeed = 5f;

    private Transform _targetPlayer;
    private NavMeshAgent _agent;
    private Animator _animator;
    private float _timer;

    void Start()
    {
        _agent = GetComponent<NavMeshAgent>();
        _animator = GetComponent<Animator>();
        _agent.speed = moveSpeed;

        FindTarget();
    }

    void Update()
    {
        if (_targetPlayer == null)
        {
            FindTarget();
            return;
        }

        float distance = Vector3.Distance(transform.position, _targetPlayer.position);

        // 1. 거리 체크 및 이동
        if (distance <= detectRange)
        {
            _agent.SetDestination(_targetPlayer.position);
        }
        else
        {
            Wander();
        }

        // 2. [핵심 수정] 애니메이션 동기화
        // 플레이어처럼 'Speed' 값도 같이 줘야 블렌드 트리가 작동합니다!
        if (_animator != null)
        {
            // 현재 AI의 실제 이동 속도를 가져옴
            float currentSpeed = _agent.velocity.magnitude;

            // (1) Speed 파라미터 (걷기/달리기 모션 섞어주는 핵심 값)
            _animator.SetFloat("Speed", currentSpeed);

            // (2) IsSprinting 파라미터 (보조 스위치)
            _animator.SetBool("IsSprinting", currentSpeed > 0.1f);
        }
    }

    void Wander()
    {
        _timer += Time.deltaTime;
        if (_timer >= 3f)
        {
            Vector3 newPos = RandomNavSphere(transform.position, wanderRadius, -1);
            _agent.SetDestination(newPos);
            _timer = 0;
        }
    }

    public static Vector3 RandomNavSphere(Vector3 origin, float dist, int layermask)
    {
        Vector3 randDirection = Random.insideUnitSphere * dist;
        randDirection += origin;
        NavMeshHit navHit;
        NavMesh.SamplePosition(randDirection, out navHit, dist, layermask);
        return navHit.position;
    }

    void FindTarget()
    {
        GameObject player = GameObject.FindGameObjectWithTag("Player");
        if (player != null)
        {
            _targetPlayer = player.transform;
        }
    }
}