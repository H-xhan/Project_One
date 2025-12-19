using UnityEngine;

public enum ItemCategory
{
    None = 0,
    Consumable = 1,
    Material = 2,
    Weapon = 3
}

public class ItemDataSO : ScriptableObject
{
    [Header("Identity")]
    [Tooltip("아이템 고유 ID (DB에서 유일해야 함)")]
    public int itemId;

    [Tooltip("표시 이름")]
    public string displayName;

    [Tooltip("아이콘")]
    public Sprite icon;

    [Tooltip("카테고리")]
    public ItemCategory category = ItemCategory.None;

    [Header("Stack")]
    [Tooltip("스택 가능 여부")]
    public bool stackable = true;

    [Tooltip("최대 스택 수량 (stackable일 때만 의미 있음)")]
    public int maxStack = 99;

    protected virtual void OnValidate()
    {
        if (maxStack < 1) maxStack = 1;
        if (!stackable) maxStack = 1;
    }
}
