using Unity.Netcode;
using Unity.Netcode.Components;
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

    private NetworkTransform _networkTransform;
    private Vector3 _defaultLocalScale = Vector3.one;
    private bool _hasDefaultLocalScale;

    public int ItemId => itemData != null ? itemData.itemId : 0;

    private void Awake()
    {
        CacheDefaultScale();
        _networkTransform = GetComponent<NetworkTransform>();
    }

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

    public Vector3 GetDefaultLocalScale()
    {
        CacheDefaultScale();
        return _defaultLocalScale;
    }

    public void ApplyPose(Vector3 worldPosition, Quaternion worldRotation, Vector3 worldScale, bool syncNetworkTransform)
    {
        transform.SetPositionAndRotation(worldPosition, worldRotation);
        transform.localScale = worldScale;

        if (syncNetworkTransform && IsServer && _networkTransform != null)
            _networkTransform.Teleport(worldPosition, worldRotation, worldScale);
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

    private void CacheDefaultScale()
    {
        if (_hasDefaultLocalScale)
            return;

        _defaultLocalScale = transform.localScale;
        if (_defaultLocalScale == Vector3.zero)
            _defaultLocalScale = Vector3.one;

        _hasDefaultLocalScale = true;
    }
}
