using System.Collections;
using Unity.Netcode;
using UnityEngine;

public class PlayerCombatModule : NetworkBehaviour
{
    [Header("Modules")]
    [SerializeField] private PlayerInteractModule interactModule;
    [SerializeField] private PlayerAnimModule animModule;

    [Header("Combat Settings (Inspector 조절 가능)")]
    [Tooltip("AI를 밀어내는 수평 힘")]
    [SerializeField] private float pveKnockbackForce = 45f;

    [Tooltip("AI를 띄워올리는 수직 힘")]
    [SerializeField] private float pveUpwardForce = 10f;

    [Tooltip("공격 판정 중심점이 내 몸에서 얼마나 앞에 생길지 (투명벽 방지용)")]
    [SerializeField] private float attackForwardOffset = 1.5f;

    [Header("Timing Settings")]
    [Tooltip("클릭 후 실제 타격 판정이 일어날 때까지의 지연 시간 (초)")]
    [SerializeField] private float hitDelay = 0.25f; // 보통 0.2~0.3초 사이가 적당합니다.

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

        // 1. [수정] 애니메이션을 먼저 즉시 실행합니다.
        TriggerAttackAnimClientRpc(weaponData.weaponAnimID);

        // 2. [수정] 휘두르는 동작에 맞춰 시간차를 두고 판정을 실행합니다.
        StartCoroutine(AttackRoutine(weaponData));
    }

    private IEnumerator AttackRoutine(WeaponItemDataSO weaponData)
    {
        // 인스펙터에서 설정한 hitDelay만큼 기다립니다.
        yield return new WaitForSeconds(hitDelay);

        // 실제 때리는 로직 실행
        PerformAttack(weaponData);
    }

    private void PerformAttack(WeaponItemDataSO weaponData)
    {
        // 공격 범위 설정
        Vector3 attackCenter = transform.position + (transform.forward * attackForwardOffset);
        float finalRadius = 1.2f;

        // 공격 판정 실행
        Collider[] hits = Physics.OverlapSphere(attackCenter, finalRadius);

        foreach (Collider col in hits)
        {
            if (col.transform.root == transform.root) continue;

            // [PvP] 플레이어 타격
            NetworkObject targetNetObj = col.GetComponentInParent<NetworkObject>();
            if (targetNetObj != null && targetNetObj.OwnerClientId != OwnerClientId)
            {
                var targetStatus = targetNetObj.GetComponent<PlayerStatusModule>();
                if (targetStatus != null)
                {
                    Vector3 knockbackForce = transform.forward * 10f + Vector3.up * 2f;
                    targetStatus.TakeHit(knockbackForce);
                }
            }

            // [PvE] 봇 타격 (수달이 홈런!)
            var dummyStatus = col.GetComponentInParent<TestDummyStatus>();
            if (dummyStatus != null)
            {
                Vector3 knockbackForce = transform.forward * pveKnockbackForce + Vector3.up * pveUpwardForce;
                dummyStatus.TakeHit(knockbackForce);
                Debug.Log($"[PvE] 봇({col.name}) 타격 성공! 파워 {pveKnockbackForce} 적용!");
            }
        }
    }

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