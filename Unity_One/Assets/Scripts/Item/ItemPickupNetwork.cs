using Unity.Netcode;
using UnityEngine;

[RequireComponent(typeof(NetworkObject))]
public class ItemPickupNetwork : NetworkBehaviour
{
    [Header("Data")]
    [Tooltip("에디터 편의용. 지정하면 itemId에 자동 반영됨.")]
    [SerializeField] private ItemDataSO itemData;

    [Tooltip("네트워크로 전달/저장되는 아이템 ID")]
    [SerializeField] private int itemId = 0;

    [Tooltip("수량")]
    [SerializeField] private int amount = 1;

    public int ItemId => itemId;
    public int Amount => amount;

    private void OnValidate()
    {
        if (itemData != null)
            itemId = itemData.itemId;

        if (amount < 1) amount = 1;
    }
}
