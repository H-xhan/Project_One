using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;

public class PlayerCombat : NetworkBehaviour
{
    [Header("Refs")]
    [Tooltip("장비 컴포넌트")]
    [SerializeField] private PlayerEquipment equipment;

    [Tooltip("아이템 DB(SO)")]
    [SerializeField] private ItemDatabaseSO itemDatabase;

    [Tooltip("공격 트리거 동기화(NetworkAnimator)")]
    [SerializeField] private NetworkAnimator netAnimator;

    [Header("Combat")]
    [Tooltip("피격 레이어 마스크")]
    [SerializeField] private LayerMask hitMask = ~0;

    [Tooltip("자기 자신 피격 무시")]
    [SerializeField] private bool ignoreSelf = true;

    private float _serverNextAttackTime;

    public void RequestAttack()
    {
        if (!IsOwner) return;
        AttackRpc();
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
    private void AttackRpc()
    {
        if (Time.time < _serverNextAttackTime)
            return;

        if (equipment == null || itemDatabase == null)
            return;

        int weaponId = equipment.GetEquippedWeaponId();
        var weaponItem = itemDatabase.GetWeapon(weaponId);
        if (weaponItem == null)
            return;

        _serverNextAttackTime = Time.time + weaponItem.weapon.cooldown;

        if (netAnimator != null)
            netAnimator.SetTrigger("AttackLight");

        Vector3 origin = transform.position + transform.forward * weaponItem.weapon.hitDistance;
        Collider[] hits = Physics.OverlapSphere(origin, weaponItem.weapon.hitRadius, hitMask, QueryTriggerInteraction.Ignore);

        for (int i = 0; i < hits.Length; i++)
        {
            var col = hits[i];
            if (col == null) continue;

            if (ignoreSelf && col.transform.root == transform.root)
                continue;

            if (col.TryGetComponent<IDamageable>(out var dmg))
                dmg.TakeDamage(weaponItem.weapon.damage);
        }
    }
}
