using Unity.Netcode;
using UnityEngine;

public class ItemPickupNetwork : NetworkBehaviour
{
    [Tooltip("아이템 데이터 (SO 파일 연결 필수!)")]
    [SerializeField] public ItemDataSO itemData; // [추가] 여기가 없어서 에러 났음

    [Tooltip("픽업 후 비주얼 숨김 처리")]
    [SerializeField] private bool hideOncePicked = true;

    [Tooltip("숨길 루트 오브젝트(없으면 자기 자신)")]
    [SerializeField] private GameObject visualRoot;

    // ItemId는 이제 itemData에서 가져오면 더 안전함
    public int ItemId => itemData != null ? itemData.itemId : 0;

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