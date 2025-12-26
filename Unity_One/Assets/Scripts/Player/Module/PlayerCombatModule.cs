using Unity.Netcode;
using UnityEngine;

public class PlayerCombatModule : NetworkBehaviour
{
    [Header("Modules")]
    [SerializeField] private PlayerInteractModule interactModule;
    [SerializeField] private PlayerAnimModule animModule;

    // 쿨타임 체크용
    private float _lastAttackTime;

    private void Awake()
    {
        if (interactModule == null) interactModule = GetComponent<PlayerInteractModule>();
        if (animModule == null) animModule = GetComponent<PlayerAnimModule>();
    }

    public void DoAttack()
    {
        // [수정 1] 무기 데이터 가져오기
        WeaponItemDataSO weaponData = GetCurrentWeaponData();

        // [핵심] 무기가 없으면 공격 아예 안 함! (맨주먹 금지)
        if (weaponData == null) return;

        // 쿨타임 체크
        if (Time.time < _lastAttackTime + weaponData.weapon.cooldown) return;

        _lastAttackTime = Time.time;
        PerformAttack(weaponData);
    }

    private void PerformAttack(WeaponItemDataSO weaponData)
    {
        // [수정 2] 애니메이션 실행할 때 '어떤 무기인지(ID)'를 같이 보냄
        TriggerAttackAnimClientRpc(weaponData.weaponAnimID);

        // 공격 판정 (이전과 동일)
        float radius = weaponData.weapon.hitRadius;
        float distance = weaponData.weapon.hitDistance;
        float damage = weaponData.weapon.damage;

        Vector3 origin = transform.position + Vector3.up * 1.0f;
        Vector3 direction = transform.forward;

        if (Physics.SphereCast(origin, radius, direction, out RaycastHit hit, distance))
        {
            NetworkObject targetNetObj = hit.collider.GetComponentInParent<NetworkObject>();
            if (targetNetObj != null && targetNetObj.OwnerClientId != OwnerClientId)
            {
                Debug.Log($"⚔️ [타격 성공] {targetNetObj.name}에게 {damage} 데미지!");
                // IDamageable 등 데미지 처리
            }
        }
    }

    // [수정 3] ID를 받아서 애니메이터에 전달하는 RPC
    [ClientRpc]
    private void TriggerAttackAnimClientRpc(int weaponID)
    {
        if (animModule != null)
        {
            // "야, 1번 무기(망치)로 때리는 시늉 해!"
            animModule.TriggerAttack(weaponID);
        }
    }

    private WeaponItemDataSO GetCurrentWeaponData()
    {
        if (interactModule == null) return null;
        if (interactModule.CurrentHeldItem.Value.TryGet(out NetworkObject heldObj))
        {
            var itemPickup = heldObj.GetComponent<ItemPickupNetwork>();
            if (itemPickup != null && itemPickup.itemData is WeaponItemDataSO weaponData)
            {
                return weaponData;
            }
        }
        return null;
    }

    // (Gizmos 코드는 그대로 두셔도 됩니다)
}