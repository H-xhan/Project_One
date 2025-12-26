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
    [Tooltip("아이템이 붙을 오른손 뼈 위치 (필수!)")]
    [SerializeField] private Transform rightHandBone;

    public NetworkVariable<NetworkObjectReference> CurrentHeldItem = new NetworkVariable<NetworkObjectReference>();

    private bool _ownerMode;
    private NetworkObject _spawnedItemObj;
    private WeaponItemDataSO _currentWeaponData; // 데이터 캐싱용

    private void Awake()
    {
        if (ownerCamera == null)
        {
            if (transform.parent != null) ownerCamera = transform.parent.GetComponentInChildren<Camera>(true);
            else ownerCamera = GetComponentInChildren<Camera>(true);
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

    // [매 프레임 실행] 위치 보정 (Grip Fix 적용)
    private void LateUpdate()
    {
        if (_spawnedItemObj == null || rightHandBone == null) return;

        Vector3 finalPosition = rightHandBone.position;
        Quaternion finalRotation = rightHandBone.rotation;

        if (_currentWeaponData != null)
        {
            // [수정] 오프셋 적용 로직
            finalPosition = rightHandBone.TransformPoint(_currentWeaponData.equippedLocalPosition);

            // [수정] EulerAngles(Vector3)를 Quaternion으로 변환해서 곱하기
            Quaternion localRot = Quaternion.Euler(_currentWeaponData.equippedLocalEulerAngles);
            finalRotation = rightHandBone.rotation * localRot;
        }

        _spawnedItemObj.transform.position = finalPosition;
        _spawnedItemObj.transform.rotation = finalRotation;
    }

    // [중복 삭제됨] 하나로 통합된 함수
    private void OnHeldItemChanged(NetworkObjectReference oldVal, NetworkObjectReference newVal)
    {
        if (newVal.TryGet(out NetworkObject itemNo))
        {
            _spawnedItemObj = itemNo;

            // [수정] ItemPickupNetwork에서 데이터 가져오기
            var itemPickup = itemNo.GetComponent<ItemPickupNetwork>();

            // itemData가 WeaponItemDataSO인지 확인
            if (itemPickup != null && itemPickup.itemData is WeaponItemDataSO weaponData)
            {
                _currentWeaponData = weaponData;
            }
            else
            {
                _currentWeaponData = null;
            }

            var rb = itemNo.GetComponent<Rigidbody>();
            if (rb != null) rb.isKinematic = true;

            var colliders = itemNo.GetComponentsInChildren<Collider>();
            foreach (var c in colliders) c.enabled = false;

            Debug.Log($"[Client/Server] 아이템({itemNo.name}) 장착 완료!");
        }
        else
        {
            _spawnedItemObj = null;
            _currentWeaponData = null;
        }
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
}