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

    [Tooltip("장착 무기 SO가 없을 때 사용할 기본 데미지")]
    [SerializeField] private float fallbackDamage = 10f;

    [Tooltip("장착 무기 SO가 없을 때 사용할 기본 공격 쿨타임")]
    [SerializeField] private float fallbackCooldown = 0.6f;

    [Tooltip("공격 대상 레이어 마스크")]
    [SerializeField] private LayerMask targetMask;

    private PlayerInteractModule interactModule;
    private PlayerStatusModule statusModule;
    private float nextAttackTime;

    private struct AttackProfile
    {
        public float cooldown;
        public float distance;
        public float radius;
        public float damage;
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
            return;

        AttackProfile profile = BuildAttackProfile();

        if (Time.time < nextAttackTime)
            return;

        nextAttackTime = Time.time + Mathf.Max(0.01f, profile.cooldown);

        Transform originTf = attackOrigin != null ? attackOrigin : transform.root;
        Vector3 center = originTf.position + originTf.forward * profile.distance;

        Collider[] hits = Physics.OverlapSphere(center, profile.radius, targetMask, QueryTriggerInteraction.Ignore);
        if (hits == null || hits.Length == 0)
            return;

        HashSet<int> processedRoots = new HashSet<int>();

        for (int i = 0; i < hits.Length; i++)
        {
            Collider hit = hits[i];
            if (hit == null) continue;

            Transform root = hit.transform.root;
            if (root == originTf.root)
                continue;

            int rootId = root.gameObject.GetInstanceID();
            if (!processedRoots.Add(rootId))
                continue;

            IDamageable damageable = hit.GetComponentInParent<IDamageable>();
            if (damageable != null)
                damageable.TakeDamage(profile.damage);

            PlayerStatusModule targetStatus = hit.GetComponentInParent<PlayerStatusModule>();
            if (targetStatus == null)
                continue;

            Vector3 dir = targetStatus.transform.position - originTf.position;
            dir.y = 0f;

            if (dir.sqrMagnitude < 0.0001f)
                dir = originTf.forward;
            else
                dir = dir.normalized;

            Vector3 impulse = dir * profile.hitForce + Vector3.up * profile.upwardForce;
            targetStatus.ApplyKnockbackServer(impulse);
        }
    }

    private AttackProfile BuildAttackProfile()
    {
        AttackProfile profile = new AttackProfile
        {
            cooldown = fallbackCooldown,
            distance = hitDistance,
            radius = hitRadius,
            damage = fallbackDamage,
            hitForce = hitForce,
            upwardForce = upwardForce
        };

        if (TryGetHeldWeaponData(out WeaponItemDataSO weaponData) && weaponData != null)
        {
            profile.cooldown = Mathf.Max(0.01f, weaponData.weapon.cooldown);
            profile.distance = Mathf.Max(0f, weaponData.weapon.hitDistance);
            profile.radius = Mathf.Max(0.01f, weaponData.weapon.hitRadius);
            profile.damage = Mathf.Max(0f, weaponData.weapon.damage);
        }

        return profile;
    }

    private bool TryGetHeldWeaponData(out WeaponItemDataSO weaponData)
    {
        weaponData = null;

        if (interactModule == null)
            return false;

        if (!interactModule.HasHeldItem())
            return false;

        ItemPickupNetwork[] pickups = FindObjectsByType<ItemPickupNetwork>(FindObjectsSortMode.None);
        for (int i = 0; i < pickups.Length; i++)
        {
            ItemPickupNetwork pickup = pickups[i];
            if (pickup == null || !pickup.IsSpawned)
                continue;

            NetworkObject netObj = pickup.NetworkObject;
            if (netObj == null || !netObj.IsSpawned)
                continue;

            if (netObj.OwnerClientId != OwnerClientId)
                continue;

            Rigidbody rb = pickup.GetComponent<Rigidbody>();
            if (rb != null && !rb.isKinematic)
                continue;

            if (!AreAllCollidersDisabled(pickup.gameObject))
                continue;

            weaponData = pickup.GetWeaponData();
            return weaponData != null;
        }

        return false;
    }

    private bool AreAllCollidersDisabled(GameObject target)
    {
        Collider[] cols = target.GetComponentsInChildren<Collider>(true);
        if (cols == null || cols.Length == 0)
            return false;

        for (int i = 0; i < cols.Length; i++)
        {
            if (cols[i] != null && cols[i].enabled)
                return false;
        }

        return true;
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        ResolveRefs();
    }
#endif
}