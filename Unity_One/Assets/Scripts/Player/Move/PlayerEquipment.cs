using Unity.Netcode;
using UnityEngine;

public class PlayerEquipment : NetworkBehaviour
{
    [Header("Data")]
    [Tooltip("아이템 DB(SO). 모든 클라이언트에 동일해야 함")]
    [SerializeField] private ItemDatabaseSO itemDatabase;

    [Header("Sockets")]
    [Tooltip("오른손 무기 소켓(Transform)")]
    [SerializeField] private Transform rightHandSocket;

    [Tooltip("왼손 무기 소켓(Transform)")]
    [SerializeField] private Transform leftHandSocket;

    private NetworkVariable<int> _equippedWeaponId = new NetworkVariable<int>(0);

    private GameObject _equippedVisual;

    public int GetEquippedWeaponId()
    {
        return _equippedWeaponId.Value;
    }

    public override void OnNetworkSpawn()
    {
        if (IsClient)
            _equippedWeaponId.OnValueChanged += OnEquippedChanged;

        if (IsClient)
            ApplyVisual(_equippedWeaponId.Value);
    }

    public override void OnNetworkDespawn()
    {
        if (IsClient)
            _equippedWeaponId.OnValueChanged -= OnEquippedChanged;
    }

    private void OnEquippedChanged(int prev, int next)
    {
        ApplyVisual(next);
    }

    public void RequestEquip(int weaponItemId)
    {
        if (!IsOwner) return;
        RequestEquipRpc(weaponItemId);
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
    private void RequestEquipRpc(int weaponItemId)
    {
        _equippedWeaponId.Value = weaponItemId;
    }

    private void ApplyVisual(int weaponItemId)
    {
        if (_equippedVisual != null)
        {
            Destroy(_equippedVisual);
            _equippedVisual = null;
        }

        if (itemDatabase == null) return;

        var weapon = itemDatabase.GetWeapon(weaponItemId);
        if (weapon == null) return;
        if (weapon.equippedModelPrefab == null) return;

        Transform socket = weapon.hand == WeaponHand.Left ? leftHandSocket : rightHandSocket;
        if (socket == null) return;

        _equippedVisual = Instantiate(weapon.equippedModelPrefab, socket);
        _equippedVisual.transform.localPosition = weapon.equippedLocalPosition;
        _equippedVisual.transform.localRotation = Quaternion.Euler(weapon.equippedLocalEulerAngles);
        _equippedVisual.transform.localScale = weapon.equippedLocalScale;
    }
}
