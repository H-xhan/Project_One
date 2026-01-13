using Unity.Netcode;
using UnityEngine;

public class PlayerCombatModule : NetworkBehaviour
{
    [Header("Attack Origin")]
    [Tooltip("공격 원점/방향 기준. Module(자식)이 아니라 PlayerRoot(부모)를 넣는 걸 추천")]
    [SerializeField] private Transform attackOrigin;

    [Header("Hit Settings")]
    [SerializeField] private float hitForce = 15f;
    [SerializeField] private float upwardForce = 3f;
    [SerializeField] private float hitRadius = 1.2f;
    [SerializeField] private float hitDistance = 1.5f;
    [SerializeField] private LayerMask targetMask;

    private void Awake()
    {
        if (attackOrigin == null)
            attackOrigin = transform.root; // Module이 자식이어도 Root로 고정
    }

    // 로컬 입력에서 호출(= Owner만)
    // 실제 판정은 PlayerHub의 AttackServerRpc -> DoAttackServer()로 서버에서 처리됨
    public void TryAttack()
    {
        if (!IsOwner) return;
        // 여기서는 아무 RPC도 안 보냄 (Hub가 이미 ServerRpc를 보내고 있으니까)
    }

    // 서버에서만 호출되어야 함 (PlayerHub.AttackServerRpc에서 호출)
    public void DoAttackServer()
    {
        if (!IsServer) return;

        Vector3 origin = attackOrigin.position + attackOrigin.forward * hitDistance;
        Collider[] hits = Physics.OverlapSphere(origin, hitRadius, targetMask, QueryTriggerInteraction.Ignore);

        for (int i = 0; i < hits.Length; i++)
        {
            var hit = hits[i];

            // 자기 자신 제외 (Root 기준)
            if (hit.transform.root == attackOrigin.root)
                continue;

            // 자식 콜라이더 대비해서 상위에서 Status 찾기
            var status = hit.GetComponentInParent<PlayerStatusModule>();
            if (status == null)
                continue;

            Vector3 dir = hit.transform.position - attackOrigin.position;
            dir.y = 0f;

            if (dir.sqrMagnitude < 0.0001f)
                dir = attackOrigin.forward;
            else
                dir = dir.normalized;

            Vector3 impulse = dir * hitForce + Vector3.up * upwardForce;

            // 서버에서만 물리 넉백 적용
            status.ApplyKnockbackServer(impulse);
        }
    }
}
