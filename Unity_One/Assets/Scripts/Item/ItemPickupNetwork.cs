using Unity.Netcode;
using UnityEngine;

public class ItemPickupNetwork : NetworkBehaviour
{
    [Tooltip("아이템 데이터 (SO 파일 연결 필수)")]
    [SerializeField] public ItemDataSO itemData;

    [Tooltip("월드에서 보이는 모델 루트(비우면 이 오브젝트 아래의 Renderer를 모두 제어)")]
    [SerializeField] private GameObject worldVisualRoot;

    [Tooltip("잡고 있는 동안 월드 비주얼을 숨길지")]
    [SerializeField] private bool hideWorldVisualWhenHeld = true;

    private readonly NetworkVariable<bool> _worldVisualVisible =
        new NetworkVariable<bool>(
            true,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server
        );

    public int ItemId => itemData != null ? itemData.itemId : 0;

    public WeaponItemDataSO GetWeaponData()
    {
        return itemData as WeaponItemDataSO;
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        _worldVisualVisible.OnValueChanged += OnWorldVisualChanged;
        ApplyWorldVisual(_worldVisualVisible.Value);
    }

    public override void OnNetworkDespawn()
    {
        _worldVisualVisible.OnValueChanged -= OnWorldVisualChanged;
        base.OnNetworkDespawn();
    }

    private void OnWorldVisualChanged(bool previousValue, bool newValue)
    {
        ApplyWorldVisual(newValue);
    }

    public void SetWorldVisualVisibleServer(bool visible)
    {
        if (!IsServer) return;
        if (!hideWorldVisualWhenHeld) return;
        _worldVisualVisible.Value = visible;
    }

    public bool IsWorldVisualVisible()
    {
        return _worldVisualVisible.Value;
    }

    private void ApplyWorldVisual(bool visible)
    {
        GameObject root = worldVisualRoot != null ? worldVisualRoot : gameObject;

        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] == null) continue;
            renderers[i].enabled = visible;
        }
    }
}
