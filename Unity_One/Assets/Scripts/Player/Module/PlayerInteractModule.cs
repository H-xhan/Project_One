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
        // 1. [버리기 처리] 이전에 들고 있던 아이템(oldVal)이 있다면 -> 다시 물리/충돌 켜기
        if (oldVal.TryGet(out NetworkObject oldItem))
        {
            // 물리 켜기
            var rb = oldItem.GetComponent<Rigidbody>();
            if (rb != null) rb.isKinematic = false;

            // 충돌 켜기
            var colliders = oldItem.GetComponentsInChildren<Collider>();
            foreach (var c in colliders) c.enabled = true;

            // 위치 동기화 재개
            var netTransform = oldItem.GetComponent<NetworkTransform>();
            if (netTransform != null) netTransform.enabled = true;

            // 데이터 초기화
            _currentWeaponData = null;
        }

        // 2. [줍기 처리] 새로 든 아이템(newVal)이 있다면 -> 설정 적용
        if (newVal.TryGet(out NetworkObject itemNo))
        {
            _spawnedItemObj = itemNo;

            var itemPickup = itemNo.GetComponent<ItemPickupNetwork>();
            if (itemPickup != null && itemPickup.itemData is WeaponItemDataSO weaponData)
            {
                _currentWeaponData = weaponData;
            }

            // 물리/충돌 끄기 (장착 중 방해 금지)
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
        // 1. 현재 들고 있는 게 없으면 패스
        if (!CurrentHeldItem.Value.TryGet(out NetworkObject itemNo)) return;

        Debug.Log($"서버: 아이템({itemNo.name}) 버리기 시도...");

        // 2. 부모 해제 (내 손을 떠나라)
        itemNo.TryRemoveParent();

        // 3. 소유권 반납 (서버가 물리 연산을 주도하도록)
        itemNo.RemoveOwnership();

        // 4. 물리 켜고 던지기!
        var rb = itemNo.GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.isKinematic = false;
            // 보는 방향으로 살짝 위로 던짐
            Vector3 throwForce = ownerCamera.transform.forward * 5f + Vector3.up * 2f;
            rb.AddForce(throwForce, ForceMode.Impulse);
        }

        // 충돌체 켜기
        var colliders = itemNo.GetComponentsInChildren<Collider>();
        foreach (var c in colliders) c.enabled = true;

        // 위치 동기화 켜기
        var netTransform = itemNo.GetComponent<NetworkTransform>();
        if (netTransform != null) netTransform.enabled = true;

        // 5. 변수 비우기 (-> OnHeldItemChanged 호출됨)
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
}