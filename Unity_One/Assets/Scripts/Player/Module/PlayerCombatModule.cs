using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public class PlayerCombatModule : NetworkBehaviour
{
    [Header("Attack Origin")]
    [Tooltip("공격 원점/방향 기준. 비워두면 플레이어 루트를 사용합니다.")]
    [SerializeField] private Transform attackOrigin;

    [Header("Fallback Hit Settings")]
    [Tooltip("장착 무기 SO가 없을 때 사용할 기본 넉백 힘")]
    [SerializeField] private float hitForce = 15f;

    [Tooltip("장착 무기 SO가 없을 때 사용할 기본 위쪽 힘")]
    [SerializeField] private float upwardForce = 3f;

    [Tooltip("장착 무기 SO가 없을 때 사용할 기본 공격 반경")]
    [SerializeField] private float hitRadius = 1.1f;

    [Tooltip("장착 무기 SO가 없을 때 사용할 기본 공격 거리")]
    [SerializeField] private float hitDistance = 1.5f;

    [Tooltip("장착 무기 SO가 없을 때 사용할 기본 공격 쿨타임")]
    [SerializeField] private float fallbackCooldown = 0.6f;

    [Tooltip("공격 대상 레이어 마스크")]
    [SerializeField] private LayerMask targetMask;

    [Header("Hit VFX")]
    [SerializeField] private GameObject hitVfxPrefab;
    [SerializeField] private float hitVfxYOffset = 0.2f;

    [Header("Debug")]
    [Tooltip("공격 판정 디버그 로그 출력 여부")]
    [SerializeField] private bool enableDebugLogs = true;

    private PlayerInteractModule interactModule;
    private PlayerStatusModule statusModule;
    private float nextAttackTime;

    private struct AttackProfile
    {
        public float cooldown;
        public float distance;
        public float radius;
        public float hitForce;
        public float upwardForce;
    }

    private void Awake()
    {
        ResolveRefs();
    }

    [ContextMenu("Auto Find Refs")]
    private void ResolveRefs()
    {
        if (attackOrigin == null)
            attackOrigin = transform.root;

        if (interactModule == null)
            interactModule = GetComponentInParent<PlayerInteractModule>();

        if (statusModule == null)
            statusModule = GetComponentInParent<PlayerStatusModule>();
    }

    public void TryAttack()
    {
        if (!IsOwner) return;
    }

    public void DoAttackServer()
    {
        if (!IsServer) return;

        ResolveRefs();

        if (statusModule != null && !statusModule.CanAttack)
        {
            Log("[PlayerCombat] Attack blocked. CanAttack is false.");
            return;
        }

        AttackProfile profile = BuildAttackProfile();

        if (Time.time < nextAttackTime)
        {
            Log($"[PlayerCombat] Cooldown. next:{nextAttackTime:0.00}, now:{Time.time:0.00}");
            return;
        }

        nextAttackTime = Time.time + Mathf.Max(0.01f, profile.cooldown);

        Transform originTf = attackOrigin != null ? attackOrigin : transform.root;
        Vector3 center = originTf.position + originTf.forward * profile.distance;

        Log($"[PlayerCombat] Attack center:{center}, radius:{profile.radius}, distance:{profile.distance}");

        // 핵심: Trigger Hurtbox를 찾기 위해 Collide 사용
        Collider[] hits = Physics.OverlapSphere(center, profile.radius, targetMask, QueryTriggerInteraction.Collide);
        Log($"[PlayerCombat] Overlap hits: {hits.Length}");

        if (hits == null || hits.Length == 0)
            return;

        HashSet<int> processedRoots = new HashSet<int>();

        for (int i = 0; i < hits.Length; i++)
        {
            Collider hit = hits[i];
            if (hit == null) continue;

            // Hurtbox만 맞춘다는 전제
            if (!hit.isTrigger)
                continue;

            Transform targetRoot = GetHitRoot(hit);
            if (targetRoot == null)
                continue;

            if (targetRoot == originTf.root)
                continue;

            int rootId = targetRoot.gameObject.GetInstanceID();
            if (!processedRoots.Add(rootId))
                continue;

            Log($"[PlayerCombat] Hurtbox hit: {hit.name}, targetRoot: {targetRoot.name}");

            PlayerStatusModule targetStatus = targetRoot.GetComponentInChildren<PlayerStatusModule>(true);
            if (targetStatus == null)
            {
                LogWarning($"[PlayerCombat] TargetStatus not found on root: {targetRoot.name}");
                continue;
            }

            Vector3 dir = targetStatus.transform.position - originTf.position;
            dir.y = 0f;

            if (dir.sqrMagnitude < 0.0001f)
                dir = originTf.forward;
            else
                dir = dir.normalized;

            Vector3 impulse = dir * profile.hitForce + Vector3.up * profile.upwardForce;

            Log($"[PlayerCombat] ApplyKnockback -> {targetStatus.name}, impulse:{impulse}");
            targetStatus.ApplyKnockbackServer(impulse);

            Vector3 hitVfxPosition = GetHitVfxPosition(hit, originTf.position, targetRoot);
            PlayHitVfxClientRpc(hitVfxPosition);
        }
    }

    private AttackProfile BuildAttackProfile()
    {
        AttackProfile profile = new AttackProfile
        {
            cooldown = fallbackCooldown,
            distance = hitDistance,
            radius = hitRadius,
            hitForce = hitForce,
            upwardForce = upwardForce
        };

        WeaponItemDataSO weaponData = interactModule != null ? interactModule.GetHeldWeaponData() : null;
        if (weaponData != null)
        {
            profile.cooldown = Mathf.Max(0.01f, weaponData.weapon.cooldown);
            profile.distance = Mathf.Max(0f, weaponData.weapon.hitDistance);
            profile.radius = Mathf.Max(0.01f, weaponData.weapon.hitRadius);
        }

        return profile;
    }

    private void Log(string message)
    {
        if (!enableDebugLogs) return;
        Debug.Log(message);
    }

    private void LogWarning(string message)
    {
        if (!enableDebugLogs) return;
        Debug.LogWarning(message);
    }

    private Transform GetHitRoot(Collider hit)
    {
        if (hit == null)
            return null;

        if (hit.attachedRigidbody != null)
            return hit.attachedRigidbody.transform;

        return hit.transform.root;
    }

    private Vector3 GetHitVfxPosition(Collider hitCollider, Vector3 attackOrigin, Transform targetRoot)
    {
        if (hitCollider != null)
        {
            Vector3 closestPoint = hitCollider.ClosestPoint(attackOrigin);
            if (!float.IsNaN(closestPoint.x) && !float.IsNaN(closestPoint.y) && !float.IsNaN(closestPoint.z))
            {
                if ((closestPoint - attackOrigin).sqrMagnitude > 0.0001f)
                    return closestPoint;
            }

            Bounds bounds = hitCollider.bounds;
            Vector3 boundsCenter = bounds.center;
            if (!float.IsNaN(boundsCenter.x) && !float.IsNaN(boundsCenter.y) && !float.IsNaN(boundsCenter.z))
                return boundsCenter;
        }

        if (targetRoot != null)
            return targetRoot.position + Vector3.up * hitVfxYOffset;

        return attackOrigin + Vector3.up * hitVfxYOffset;
    }

    [ClientRpc]
    private void PlayHitVfxClientRpc(Vector3 position)
    {
        if (hitVfxPrefab == null)
            return;

        Instantiate(hitVfxPrefab, position, Quaternion.identity);
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        ResolveRefs();
    }
#endif
}
