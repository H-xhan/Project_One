using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "Game/Items/Item Database", fileName = "ItemDatabase")]
public class ItemDatabaseSO : ScriptableObject
{
    [Tooltip("프로젝트에서 사용하는 모든 아이템 SO 목록")]
    public List<ItemDataSO> items = new List<ItemDataSO>();

    private Dictionary<int, ItemDataSO> _map;

    private void OnEnable()
    {
        BuildCache();
    }

    public void BuildCache()
    {
        _map = new Dictionary<int, ItemDataSO>(items.Count);
        for (int i = 0; i < items.Count; i++)
        {
            var it = items[i];
            if (it == null) continue;

            if (_map.ContainsKey(it.itemId))
            {
                Debug.LogError($"[ItemDatabase] Duplicate itemId: {it.itemId} ({it.name})");
                continue;
            }

            _map.Add(it.itemId, it);
        }
    }

    public ItemDataSO Get(int itemId)
    {
        if (_map == null) BuildCache();
        return _map.TryGetValue(itemId, out var it) ? it : null;
    }

    public WeaponItemDataSO GetWeapon(int itemId)
    {
        return Get(itemId) as WeaponItemDataSO;
    }
}
