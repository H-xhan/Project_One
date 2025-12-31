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
        // 1. 애니메이션 실행
        TriggerAttackAnimClientRpc(weaponData.weaponAnimID);

        // 2. 공격 범위 설정 (데이터 사용)
        float range = weaponData.weapon.hitDistance; // 사거리 (예: 2m)
        float radius = weaponData.weapon.hitRadius;  // 공격 범위 (예: 1m)

        // [핵심 변경] "발사"하지 않고, 내 앞에 "공격 지대"를 생성합니다.
        // 내 위치에서 '사거리의 절반'만큼 앞으로 간 곳을 중심으로 잡습니다.
        Vector3 attackCenter = transform.position + (transform.forward * (range * 0.5f));

        // 사거리와 공격 범위를 모두 커버하도록 넉넉하게 반지름을 잡습니다.
        float finalRadius = Mathf.Max(range * 0.5f, radius);

        // 디버그용: 몇 명이나 걸렸는지 로그 찍기
        Collider[] hits = Physics.OverlapSphere(attackCenter, finalRadius);

        Debug.Log($"[공격 판정] 위치: {attackCenter}, 크기: {finalRadius}, 감지된 수: {hits.Length}");

        foreach (Collider col in hits)
        {
            Debug.Log($"감지된 물체: {col.name} (부모: {col.transform.root.name})");
            // 1. 나 자신은 때리지 않기
            if (col.transform.root == transform.root) continue;

            // 2. [PvP] 플레이어 타격
            NetworkObject targetNetObj = col.GetComponentInParent<NetworkObject>();
            if (targetNetObj != null && targetNetObj.OwnerClientId != OwnerClientId)
            {
                var targetStatus = targetNetObj.GetComponent<PlayerStatusModule>();
                if (targetStatus != null)
                {
                    Vector3 knockbackForce = transform.forward * 10f + Vector3.up * 2f;
                    targetStatus.TakeHit(knockbackForce);
                    Debug.Log($"[PvP] {targetNetObj.name} 타격 성공!");
                }
            }

            // 3. [PvE] 봇 타격 (이제 겹쳐 있어도 무조건 맞습니다!)
            var dummyStatus = col.GetComponentInParent<TestDummyStatus>();
            if (dummyStatus != null)
            {
                Vector3 knockbackForce = transform.forward * 15f + Vector3.up * 3f;
                dummyStatus.TakeHit(knockbackForce);
                Debug.Log($"🤖 [PvE] 봇({col.name}) 타격 성공! 뻥!");
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