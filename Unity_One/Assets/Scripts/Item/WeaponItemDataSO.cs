using UnityEngine;
using UnityEngine.Serialization;

public enum WeaponHand
{
    Right = 0,
    Left = 1
}

[CreateAssetMenu(menuName = "Game/Items/Weapon Item", fileName = "WeaponItemData")]
public class WeaponItemDataSO : ItemDataSO
{
    [System.Serializable]
    public class WeaponStats
    {
        [Tooltip("공격 쿨타임(초)")]
        public float cooldown = 0.6f;

        [Tooltip("피격 판정 중심 거리(전방)")]
        public float hitDistance = 1.2f;

        [Tooltip("피격 판정 반경")]
        public float hitRadius = 0.8f;

        [Tooltip("데미지")]
        public float damage = 10f;
    }

    [Header("Weapon Stats")]
    [Tooltip("공격 스탯 묶음(PlayerCombat이 weapon.*로 접근)")]
    public WeaponStats weapon = new WeaponStats();

    [Header("Equip Visual")]
    [Tooltip("손에 붙일 모델 프리팹(네트워크 오브젝트 필요 없음, 로컬 비주얼용)")]
    [FormerlySerializedAs("equippedPrefab")]
    public GameObject equippedModelPrefab;

    [Tooltip("장착 손(오른손/왼손)")]
    public WeaponHand hand = WeaponHand.Right;

    [Tooltip("장착 로컬 위치 오프셋")]
    [FormerlySerializedAs("equippedLocalPos")]
    public Vector3 equippedLocalPosition;

    [Tooltip("장착 로컬 회전(Euler)")]
    [FormerlySerializedAs("equippedLocalEuler")]
    public Vector3 equippedLocalEulerAngles;

    [Tooltip("장착 로컬 스케일")]
    [FormerlySerializedAs("equippedLocalScale")]
    public Vector3 equippedLocalScale = Vector3.one;

    protected override void OnValidate()
    {
        base.OnValidate();
        category = ItemCategory.Weapon;
        stackable = false;
        maxStack = 1;

        if (weapon.cooldown < 0.01f) weapon.cooldown = 0.01f;
        if (weapon.hitRadius < 0.01f) weapon.hitRadius = 0.01f;
        if (weapon.hitDistance < 0f) weapon.hitDistance = 0f;
        if (weapon.damage < 0f) weapon.damage = 0f;
    }
}
