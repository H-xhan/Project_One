using Unity.Netcode;
using UnityEngine;

public class ItemPickupNetwork : NetworkBehaviour
{
    [Tooltip("아이템 ID (ItemDatabaseSO 기준)")]
    [SerializeField] private int itemId = 0;

    [Tooltip("픽업 후 비주얼 숨김 처리")]
    [SerializeField] private bool hideOncePicked = true;

    [Tooltip("숨길 루트 오브젝트(없으면 자기 자신)")]
    [SerializeField] private GameObject visualRoot;

    public int ItemId => itemId;

    public void HideVisual()
    {
        if (!hideOncePicked) return;

        GameObject target = (visualRoot != null) ? visualRoot : gameObject;
        if (target != null)
        {
            target.SetActive(false);
        }
    }
}
