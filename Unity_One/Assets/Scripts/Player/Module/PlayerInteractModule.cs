using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;

public class PlayerInteractModule : NetworkBehaviour
{
    [Header("Raycast")]
    [Tooltip("플레이어 카메라 (자동 탐색)")]
    [SerializeField] private Camera ownerCamera;

    [Tooltip("줍기 사거리")]
    [SerializeField] private float pickupDistance = 20f;

    [Tooltip("아이템 레이어 마스크")]
    [SerializeField] private LayerMask pickupMask = ~0;

    [Header("Hand")]
    [Tooltip("아이템이 붙을 오른손 뼈 위치 (비워도 자동 탐색 시도)")]
    [SerializeField] private Transform rightHandBone;

    public NetworkVariable<NetworkObjectReference> CurrentHeldItem = new NetworkVariable<NetworkObjectReference>();

    private bool _ownerMode;
    private NetworkObject _spawnedItemObj;
    private WeaponItemDataSO _currentWeaponData;

    private Animator _animator;
    private bool _warnedNoWeaponData;

    private void Awake()
    {
        if (ownerCamera == null)
        {
            if (transform.parent != null) ownerCamera = transform.parent.GetComponentInChildren<Camera>(true);
            else ownerCamera = GetComponentInChildren<Camera>(true);
        }

        if (_animator == null)
        {
            if (transform.parent != null) _animator = transform.parent.GetComponentInChildren<Animator>(true);
            else _animator = GetComponentInChildren<Animator>(true);
        }

        if (rightHandBone == null && _animator != null && _animator.isHuman)
        {
            rightHandBone = _animator.GetBoneTransform(HumanBodyBones.RightHand);
        }
    }

    public override void OnNetworkSpawn()
    {
        CurrentHeldItem.OnValueChanged += OnHeldItemChanged;
    }

    public override void OnNetworkDespawn()
    {
        CurrentHeldItem.OnValueChanged -= OnHeldItemChanged;
    }

    public void SetOwnerMode(bool ownerMode)
    {
        _ownerMode = ownerMode;
        if (!_ownerMode) ownerCamera = null;
    }

    public void Tick(bool interactPressed) { }

    private void LateUpdate()
    {
        if (_spawnedItemObj == null || rightHandBone == null) return;

        Vector3 finalPosition = rightHandBone.position;
        Quaternion finalRotation = rightHandBone.rotation;

        if (_currentWeaponData != null)
        {
            finalPosition = rightHandBone.TransformPoint(_currentWeaponData.equippedLocalPosition);

            Quaternion localRot = Quaternion.Euler(_currentWeaponData.equippedLocalEulerAngles);
            finalRotation = rightHandBone.rotation * localRot;

            _warnedNoWeaponData = false;
        }
        else
        {
            if (!_warnedNoWeaponData)
            {
                _warnedNoWeaponData = true;
                Debug.LogWarning("[PlayerInteract] WeaponItemDataSO를 못 잡아서 오프셋 적용이 스킵됩니다. ItemPickupNetwork.itemData가 WeaponItemDataSO인지 확인하세요.");
            }
        }

        _spawnedItemObj.transform.position = finalPosition;
        _spawnedItemObj.transform.rotation = finalRotation;
    }

    private void OnHeldItemChanged(NetworkObjectReference oldVal, NetworkObjectReference newVal)
    {
        if (oldVal.TryGet(out NetworkObject oldItem))
        {
            var rb = oldItem.GetComponent<Rigidbody>();
            if (rb != null) rb.isKinematic = false;

            var colliders = oldItem.GetComponentsInChildren<Collider>();
            foreach (var c in colliders) c.enabled = true;

            var netTransform = oldItem.GetComponent<NetworkTransform>();
            if (netTransform != null) netTransform.enabled = true;

            _currentWeaponData = null;
            _spawnedItemObj = null;
        }

        if (newVal.TryGet(out NetworkObject itemNo))
        {
            _spawnedItemObj = itemNo;
            _currentWeaponData = null;

            var itemPickup = itemNo.GetComponent<ItemPickupNetwork>();
            if (itemPickup == null)
            {
                Debug.LogWarning($"[PlayerInteract] {itemNo.name}에 ItemPickupNetwork가 없습니다. 무기 오프셋 적용 불가.");
            }
            else
            {
                if (itemPickup.itemData is WeaponItemDataSO weaponData)
                {
                    _currentWeaponData = weaponData;
                    Debug.Log($"[PlayerInteract] WeaponData OK: {weaponData.name} pos={weaponData.equippedLocalPosition} rot={weaponData.equippedLocalEulerAngles}");
                }
                else
                {
                    Debug.LogWarning($"[PlayerInteract] itemData가 WeaponItemDataSO가 아닙니다: {(itemPickup.itemData != null ? itemPickup.itemData.GetType().Name : "null")}");
                }
            }

            var rb = itemNo.GetComponent<Rigidbody>();
            if (rb != null) rb.isKinematic = true;

            var colliders = itemNo.GetComponentsInChildren<Collider>();
            foreach (var c in colliders) c.enabled = false;

            var netTransform = itemNo.GetComponent<NetworkTransform>();
            if (netTransform != null) netTransform.enabled = false;

            Debug.Log($"[Client/Server] 아이템({itemNo.name}) 장착 완료!");
        }
        else
        {
            _spawnedItemObj = null;
            _currentWeaponData = null;
        }
    }

    public void ServerTryDrop()
    {
        if (!CurrentHeldItem.Value.TryGet(out NetworkObject itemNo)) return;

        Debug.Log($"서버: 아이템({itemNo.name}) 버리기 시도...");

        itemNo.TryRemoveParent();
        itemNo.RemoveOwnership();

        var rb = itemNo.GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.isKinematic = false;
            Vector3 throwForce = ownerCamera.transform.forward * 5f + Vector3.up * 2f;
            rb.AddForce(throwForce, ForceMode.Impulse);
        }

        var colliders = itemNo.GetComponentsInChildren<Collider>();
        foreach (var c in colliders) c.enabled = true;

        var netTransform = itemNo.GetComponent<NetworkTransform>();
        if (netTransform != null) netTransform.enabled = true;

        CurrentHeldItem.Value = default;
    }

    public bool TryFindPickupTarget(out NetworkObjectReference target)
    {
        target = default;
        if (!_ownerMode || ownerCamera == null) return false;

        Ray ray = ownerCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
        if (Physics.Raycast(ray, out RaycastHit hit, pickupDistance, pickupMask, QueryTriggerInteraction.Collide))
        {
            NetworkObject no = hit.collider.GetComponentInParent<NetworkObject>();
            if (no != null)
            {
                target = new NetworkObjectReference(no);
                return true;
            }
        }
        return false;
    }

    public bool ServerTryPickup(NetworkObjectReference target)
    {
        if (!target.TryGet(out NetworkObject no) || no == null) return false;

        if (CurrentHeldItem.Value.TryGet(out NetworkObject current) && current != null)
            return false;

        Debug.Log($"서버: 아이템({no.name}) 줍기 시도...");

        DontDestroyOnLoad(no.gameObject);

        var netTransform = no.GetComponent<NetworkTransform>();
        if (netTransform != null) netTransform.enabled = false;

        var playerNo = GetComponentInParent<NetworkObject>();
        if (playerNo != null) no.ChangeOwnership(playerNo.OwnerClientId);

        if (no.TrySetParent(playerNo.transform, false))
        {
            CurrentHeldItem.Value = target;
            Debug.Log("장착 성공!");
            return true;
        }

        return false;
    }

    public int GetCurrentWeaponAnimID()
    {
        if (_currentWeaponData == null) return 0;
        return _currentWeaponData.weaponAnimID;
    }

}
