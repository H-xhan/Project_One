using UnityEngine;

public class PlayerCombatModule : MonoBehaviour
{
    [Header("Attack")]
    [Tooltip("공격 원점 오프셋(플레이어 기준)")]
    [SerializeField] private Vector3 originOffset = new Vector3(0f, 0.9f, 1.2f);

    [Tooltip("공격 반경")]
    [SerializeField] private float hitRadius = 0.9f;

    [Tooltip("데미지")]
    [SerializeField] private float damage = 10f;

    [Tooltip("피격 레이어 마스크")]
    [SerializeField] private LayerMask hitMask = ~0;

    public void DoAttack()
    {
        Vector3 origin = transform.TransformPoint(originOffset);

        Collider[] hits = Physics.OverlapSphere(origin, hitRadius, hitMask, QueryTriggerInteraction.Ignore);

        for (int i = 0; i < hits.Length; i++)
        {
            if (hits[i] == null) continue;

            if (hits[i].TryGetComponent<IDamageable>(out var dmg))
                dmg.TakeDamage(damage);
        }
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.DrawWireSphere(originOffset, hitRadius);
    }
#endif
}
