using Unity.Netcode;
using UnityEngine;

public class PlayerCombatModule : NetworkBehaviour
{
    [Header("Modules")]
    [SerializeField] private PlayerInteractModule interactModule;
    [SerializeField] private PlayerAnimModule animModule;

    [Header("Default Debug")]

    private float _lastAttackTime;

    private void Awake()
    {
        if (interactModule == null) interactModule = GetComponent<PlayerInteractModule>();
        if (animModule == null) animModule = GetComponent<PlayerAnimModule>();
    }

    public void DoAttack()
    {
        WeaponItemDataSO weaponData = GetCurrentWeaponData();

        // 무기가 없으면 공격 안 함
        if (weaponData == null) return;

        // 쿨타임 체크
        if (Time.time < _lastAttackTime + weaponData.weapon.cooldown) return;

        _lastAttackTime = Time.time;
        PerformAttack(weaponData);
    }

    private void PerformAttack(WeaponItemDataSO weaponData)
    {
        // 애니메이션 실행
        TriggerAttackAnimClientRpc(weaponData.weaponAnimID);

        // 공격 범위 계산
        float radius = weaponData.weapon.hitRadius;
        float distance = weaponData.weapon.hitDistance;

        Vector3 origin = transform.position + Vector3.up * 1.0f;
        Vector3 direction = transform.forward;

        // 충돌 감지 (SphereCast)
        if (Physics.SphereCast(origin, radius, direction, out RaycastHit hit, distance))
        {
            NetworkObject targetNetObj = hit.collider.GetComponentInParent<NetworkObject>();

            // 맞은 게 있고, 그게 나 자신이 아니라면
            if (targetNetObj != null && targetNetObj.OwnerClientId != OwnerClientId)
            {
                // [수정] 상대방의 PlayerStatusModule을 찾아서 밀어버리기!
                var targetStatus = targetNetObj.GetComponent<PlayerStatusModule>();

                if (targetStatus != null)
                {
                    // 때리는 힘 계산 (보는 방향으로 10만큼 + 위로 살짝)
                    Vector3 knockbackForce = transform.forward * 10f + Vector3.up * 2f;
                    targetStatus.TakeHit(knockbackForce);

                    Debug.Log($"[타격 성공] {targetNetObj.name}를 날려버렸습니다!");
                }
            }
        }
    } // <--- [중요] 아까 이 괄호가 없어서 에러가 났던 겁니다!

    [ClientRpc]
    private void TriggerAttackAnimClientRpc(int weaponID)
    {
        if (animModule != null)
        {
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
}