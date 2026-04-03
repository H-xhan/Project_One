using Unity.Netcode;
using UnityEngine;

public class PlayerInteractModule : NetworkBehaviour
{
    [Header("Raycast")]
    [Tooltip("오너가 사용하는 카메라")]
    [SerializeField] private Camera ownerCamera;

    [Tooltip("픽업 레이캐스트 거리")]
    [SerializeField] private float pickupDistance = 6f;

    [Tooltip("픽업 가능한 레이어 마스크")]
    [SerializeField] private LayerMask pickupMask;

    [Header("Hand")]
    [Tooltip("오른손 소켓(WeaponPoint). 비어있으면 휴머노이드 RightHand를 자동 탐색")]
    [SerializeField] private Transform rightHandBone;

    [Tooltip("왼손 소켓. 비어있으면 휴머노이드 LeftHand를 자동 탐색")]
    [SerializeField] private Transform leftHandBone;

    [Tooltip("아이템을 손에 붙일 때 기본 로컬 위치 오프셋")]
    [SerializeField] private Vector3 defaultHeldLocalPosition;

    [Tooltip("아이템을 손에 붙일 때 기본 로컬 회전 오프셋(오일러)")]
    [SerializeField] private Vector3 defaultHeldLocalEulerAngles;

    [Tooltip("아이템을 손에 붙일 때 기본 로컬 스케일")]
    [SerializeField] private Vector3 defaultHeldLocalScale = Vector3.one;

    [Header("Held Visual")]
    [Tooltip("장착 비주얼 프리팹이 있으면 월드 네트워크 아이템 대신 로컬 비주얼을 손에 붙입니다")]
    [SerializeField] private bool preferLocalHeldVisual = true;

    [Tooltip("로컬 장착 비주얼이 없을 때만 기존 월드 아이템 손부착 방식을 사용합니다")]
    [SerializeField] private bool fallbackToWorldAttachWhenNoVisual = true;

    [Tooltip("픽업 중 캐릭터 컨트롤러 충돌을 끄고 싶으면 체크(월드 아이템 부착 fallback일 때만 적용)")]
    [SerializeField] private bool disableCharacterControllerWhilePickingUp = true;

    [Header("Pickup Timing")]
    [Tooltip("애니 이벤트가 누락되었을 때 자동으로 붙이는 대기 시간(초)")]
    [SerializeField] private float pickupPendingTime = 1.5f;

    [Header("Drop/Throw")]
    [Tooltip("드랍/던지기 전방 힘")]
    [SerializeField] private float throwForwardForce = 5f;

    [Tooltip("드랍/던지기 위쪽 힘")]
    [SerializeField] private float throwUpForce = 2f;

    [Header("Drop From Hand")]
    [Tooltip("드랍 시 손 위치에서 플레이어 전방으로 살짝 밀어 겹침을 줄이는 거리")]
    [SerializeField] private float dropHandForwardOffset = 0.15f;

    [Tooltip("드랍 시 손 위치에서 위로 살짝 올려 겹침을 줄이는 거리")]
    [SerializeField] private float dropHandUpOffset = 0.05f;

    [Header("Debug")]
    [SerializeField] private bool enableDebugLogs = false;

    private readonly NetworkVariable<NetworkObjectReference> _heldItem =
        new NetworkVariable<NetworkObjectReference>(
            default,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server
        );

    private bool _ownerMode;
    private bool _pendingAttach;
    private bool _attached;
    private float _pendingStartTime;

    private NetworkObject _heldCache;
    private ItemPickupNetwork _heldPickup;
    private WeaponItemDataSO _heldWeaponData;

    private int _cachedWeaponAnimId;
    private Vector3 _cachedLocalPos;
    private Vector3 _cachedLocalEuler;
    private Vector3 _cachedLocalScale;
    private GameObject _cachedEquippedVisualPrefab;
    private bool _hasCachedMeta;
    private bool _useLocalHeldVisual;

    private CharacterController _cc;
    private Animator _anim;

    private GameObject _localHeldVisualInstance;
    private GameObject _localHeldVisualSourcePrefab;

    public void SetOwnerMode(bool active)
    {
        _ownerMode = active;
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        _cc = GetComponentInParent<CharacterController>();
        _anim = GetComponentInParent<Animator>();

        AutoFindRefs();
        _heldItem.OnValueChanged += OnHeldItemChanged;

        ResolveHeldCache();
        CacheHeldMeta();
        RefreshHeldPresentation();
    }

    public override void OnNetworkDespawn()
    {
        _heldItem.OnValueChanged -= OnHeldItemChanged;
        DestroyLocalHeldVisual();
        base.OnNetworkDespawn();
    }

    private void AutoFindRefs()
    {
        if (ownerCamera == null)
            ownerCamera = GetComponentInParent<Camera>();

        if (_anim == null)
            _anim = GetComponentInParent<Animator>();

        if (_anim != null && _anim.isHuman)
        {
            if (rightHandBone == null)
                rightHandBone = _anim.GetBoneTransform(HumanBodyBones.RightHand);

            if (leftHandBone == null)
                leftHandBone = _anim.GetBoneTransform(HumanBodyBones.LeftHand);
        }
    }

    private void OnHeldItemChanged(NetworkObjectReference previousValue, NetworkObjectReference newValue)
    {
        RestorePreviousWorldItemVisual(previousValue);

        _heldCache = null;
        _heldPickup = null;
        _heldWeaponData = null;

        _hasCachedMeta = false;
        _cachedWeaponAnimId = 0;
        _cachedLocalPos = defaultHeldLocalPosition;
        _cachedLocalEuler = defaultHeldLocalEulerAngles;
        _cachedLocalScale = defaultHeldLocalScale;
        _cachedEquippedVisualPrefab = null;
        _useLocalHeldVisual = false;

        _pendingAttach = false;
        _attached = false;
        _pendingStartTime = Time.time;

        DestroyLocalHeldVisual();

        ResolveHeldCache();
        CacheHeldMeta();
        RefreshHeldPresentation();
    }

    private void Update()
    {
        // 로컬 비주얼 기반 held state는 월드 아이템을 손에 붙일 필요가 없어서 서버 pending attach를 사용하지 않음
        if (IsServer && !_useLocalHeldVisual && _pendingAttach && !_attached)
        {
            if (Time.time - _pendingStartTime >= pickupPendingTime)
                ForceAttach();
        }

        // 씬 로드 타이밍 때문에 손 본/메타가 늦게 준비됐을 때 재시도
        if (_heldItem.Value.TryGet(out _) && _useLocalHeldVisual && _localHeldVisualInstance == null)
        {
            RefreshHeldPresentation();
        }
    }

    private void LateUpdate()
    {
        if (_useLocalHeldVisual)
        {
            UpdateLocalHeldVisualTransform();
            return;
        }

        if (!IsServer) return;
        if (!_attached) return;

        if (!ResolveHeldCache()) return;
        Transform handSocket = GetTargetHandSocket();
        if (handSocket == null) return;

        Vector3 localPos = _hasCachedMeta ? _cachedLocalPos : defaultHeldLocalPosition;
        Vector3 localEuler = _hasCachedMeta ? _cachedLocalEuler : defaultHeldLocalEulerAngles;

        Vector3 worldPos = handSocket.TransformPoint(localPos);
        Quaternion worldRot = handSocket.rotation * Quaternion.Euler(localEuler);

        _heldCache.transform.SetPositionAndRotation(worldPos, worldRot);
    }

    public bool HasHeldItem()
    {
        return ResolveHeldCache();
    }

    public bool TryFindPickupTarget(out NetworkObjectReference target)
    {
        target = default;

        if (!_ownerMode) return false;
        if (ownerCamera == null) return false;
        if (HasHeldItem()) return false;

        Ray ray = new Ray(ownerCamera.transform.position, ownerCamera.transform.forward);
        if (!Physics.Raycast(ray, out RaycastHit hit, pickupDistance, pickupMask, QueryTriggerInteraction.Ignore))
            return false;

        NetworkObject netObj = hit.collider.GetComponentInParent<NetworkObject>();
        if (netObj == null || !netObj.IsSpawned) return false;

        target = netObj;
        return true;
    }

    public bool ServerTryPickup(NetworkObjectReference target)
    {
        if (!IsServer) return false;
        if (ResolveHeldCache()) return false;

        if (!target.TryGet(out NetworkObject netObj)) return false;
        if (netObj == null || !netObj.IsSpawned) return false;

        netObj.ChangeOwnership(OwnerClientId);

        // 먼저 캐시를 임시 계산해서 local visual 가능 여부를 판정
        ItemPickupNetwork pickup = netObj.GetComponent<ItemPickupNetwork>();
        WeaponItemDataSO weaponData = pickup != null ? pickup.GetWeaponData() : null;
        bool useLocalVisual = ShouldUseLocalHeldVisual(weaponData);

        SetHeldPhysics(netObj, true);

        // local visual을 쓸 때는 월드 아이템을 로컬마다 숨긴다. 드랍 시 다시 켠다.
        if (useLocalVisual)
            SetWorldItemPresentationState(netObj, false);

        _heldItem.Value = target;

        if (!useLocalVisual && disableCharacterControllerWhilePickingUp && _cc != null && _cc.enabled)
            _cc.enabled = false;

        if (useLocalVisual)
        {
            _attached = true;
            _pendingAttach = false;
            if (_cc != null && !_cc.enabled)
                _cc.enabled = true;
        }

        Log($"[PlayerInteract] ServerTryPickup -> {netObj.name}, localVisual:{useLocalVisual}");
        return true;
    }

    public void ServerTryDrop()
    {
        if (!IsServer) return;
        if (!ResolveHeldCache()) return;

        NetworkObject netObj = _heldCache;
        bool usedLocalVisual = _useLocalHeldVisual;

        _attached = false;
        _pendingAttach = false;

        if (!_hasCachedMeta)
            CacheHeldMeta();

        if (TryGetHandWorldPose(out Vector3 handPos, out Quaternion handRot))
        {
            Vector3 dropPos = handPos + transform.forward * dropHandForwardOffset + Vector3.up * dropHandUpOffset;
            netObj.transform.SetPositionAndRotation(dropPos, handRot);
        }
        else
        {
            Vector3 dropPos = transform.position + transform.forward * 1.2f + Vector3.up * 1.0f;
            netObj.transform.SetPositionAndRotation(dropPos, Quaternion.identity);
        }

        SetWorldItemPresentationState(netObj, true);
        SetHeldPhysics(netObj, false);

        Rigidbody rb = netObj.GetComponent<Rigidbody>();
        if (rb != null)
        {
            Vector3 force = transform.forward * throwForwardForce + Vector3.up * throwUpForce;
            rb.AddForce(force, ForceMode.Impulse);
        }

        if (NetworkManager != null)
            netObj.ChangeOwnership(NetworkManager.ServerClientId);

        _heldItem.Value = default;

        if (_cc != null && !_cc.enabled)
            _cc.enabled = true;

        Log($"[PlayerInteract] ServerTryDrop -> {netObj.name}, localVisual:{usedLocalVisual}");
    }

    public int GetCurrentWeaponAnimID()
    {
        if (!ResolveHeldCache()) return 0;
        if (!_hasCachedMeta) CacheHeldMeta();
        return _hasCachedMeta ? _cachedWeaponAnimId : 0;
    }

    public void AnimEvent_AttachHeldItem()
    {
        if (_useLocalHeldVisual)
        {
            RefreshHeldPresentation();
            return;
        }

        if (!IsServer) return;
        ForceAttach();
    }

    private void ForceAttach()
    {
        if (!ResolveHeldCache()) return;

        if (_useLocalHeldVisual)
        {
            _pendingAttach = false;
            _attached = true;
            RefreshHeldPresentation();
            return;
        }

        _pendingAttach = false;
        _attached = true;

        if (_cc != null && !_cc.enabled)
            _cc.enabled = true;

        if (TryGetHandWorldPose(out Vector3 handPos, out Quaternion handRot))
            _heldCache.transform.SetPositionAndRotation(handPos, handRot);
    }

    private bool ResolveHeldCache()
    {
        if (_heldCache != null && _heldCache.IsSpawned)
            return true;

        if (_heldItem.Value.TryGet(out NetworkObject netObj))
        {
            _heldCache = netObj;
            _heldPickup = _heldCache != null ? _heldCache.GetComponent<ItemPickupNetwork>() : null;
            return _heldCache != null && _heldCache.IsSpawned;
        }

        _heldCache = null;
        _heldPickup = null;
        return false;
    }

    private bool TryGetHandWorldPose(out Vector3 pos, out Quaternion rot)
    {
        pos = Vector3.zero;
        rot = Quaternion.identity;

        Transform handSocket = GetTargetHandSocket();
        if (handSocket == null) return false;

        Vector3 localPos = _hasCachedMeta ? _cachedLocalPos : defaultHeldLocalPosition;
        Vector3 localEuler = _hasCachedMeta ? _cachedLocalEuler : defaultHeldLocalEulerAngles;

        pos = handSocket.TransformPoint(localPos);
        rot = handSocket.rotation * Quaternion.Euler(localEuler);
        return true;
    }

    private Transform GetTargetHandSocket()
    {
        AutoFindRefs();

        if (_heldWeaponData != null && _heldWeaponData.hand == WeaponHand.Left && leftHandBone != null)
            return leftHandBone;

        if (rightHandBone != null)
            return rightHandBone;

        if (leftHandBone != null)
            return leftHandBone;

        return null;
    }

    private void SetHeldPhysics(NetworkObject netObj, bool held)
    {
        Rigidbody rb = netObj.GetComponent<Rigidbody>();
        if (rb != null)
        {
            if (held)
            {
                if (!rb.isKinematic)
                {
                    rb.linearVelocity = Vector3.zero;
                    rb.angularVelocity = Vector3.zero;
                }

                rb.isKinematic = true;
                rb.detectCollisions = false;
                rb.Sleep();
            }
            else
            {
                rb.isKinematic = false;
                rb.detectCollisions = true;
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
                rb.WakeUp();
            }
        }

        Collider[] cols = netObj.GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < cols.Length; i++)
            cols[i].enabled = !held;
    }

    private void SetWorldItemPresentationState(NetworkObject netObj, bool visible)
    {
        if (netObj == null) return;

        Renderer[] renderers = netObj.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] != null)
                renderers[i].enabled = visible;
        }

        Canvas[] canvases = netObj.GetComponentsInChildren<Canvas>(true);
        for (int i = 0; i < canvases.Length; i++)
        {
            if (canvases[i] != null)
                canvases[i].enabled = visible;
        }

        Collider[] cols = netObj.GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < cols.Length; i++)
        {
            if (cols[i] != null)
                cols[i].enabled = visible;
        }
    }

    private void RestorePreviousWorldItemVisual(NetworkObjectReference previousValue)
    {
        if (!previousValue.TryGet(out NetworkObject prevNetObj))
            return;

        if (prevNetObj == null || !prevNetObj.IsSpawned)
            return;

        SetWorldItemPresentationState(prevNetObj, true);
    }

    private void CacheHeldMeta()
    {
        _hasCachedMeta = false;
        _heldWeaponData = null;
        _cachedWeaponAnimId = 0;
        _cachedLocalPos = defaultHeldLocalPosition;
        _cachedLocalEuler = defaultHeldLocalEulerAngles;
        _cachedLocalScale = defaultHeldLocalScale;
        _cachedEquippedVisualPrefab = null;
        _useLocalHeldVisual = false;

        if (!ResolveHeldCache()) return;
        if (_heldPickup == null) return;

        _heldWeaponData = _heldPickup.GetWeaponData();
        if (_heldWeaponData == null) return;

        _cachedWeaponAnimId = _heldWeaponData.weaponAnimID;
        _cachedLocalPos = _heldWeaponData.equippedLocalPosition;
        _cachedLocalEuler = _heldWeaponData.equippedLocalEulerAngles;
        _cachedLocalScale = _heldWeaponData.equippedLocalScale == Vector3.zero ? Vector3.one : _heldWeaponData.equippedLocalScale;
        _cachedEquippedVisualPrefab = _heldWeaponData.equippedModelPrefab;
        _hasCachedMeta = true;
        _useLocalHeldVisual = ShouldUseLocalHeldVisual(_heldWeaponData);
    }

    private bool ShouldUseLocalHeldVisual(WeaponItemDataSO weaponData)
    {
        if (!preferLocalHeldVisual)
            return false;

        if (weaponData == null)
            return false;

        return weaponData.equippedModelPrefab != null;
    }

    private void RefreshHeldPresentation()
    {
        if (!ResolveHeldCache())
        {
            DestroyLocalHeldVisual();
            return;
        }

        if (_useLocalHeldVisual)
        {
            SetWorldItemPresentationState(_heldCache, false);
            EnsureLocalHeldVisual();
            _attached = true;
            _pendingAttach = false;
            if (_cc != null && !_cc.enabled)
                _cc.enabled = true;
        }
        else
        {
            DestroyLocalHeldVisual();

            if (fallbackToWorldAttachWhenNoVisual)
            {
                _pendingAttach = true;
                _attached = false;
                _pendingStartTime = Time.time;
            }
            else
            {
                _pendingAttach = false;
                _attached = true;
            }
        }
    }

    private void EnsureLocalHeldVisual()
    {
        if (!_useLocalHeldVisual)
            return;

        if (_cachedEquippedVisualPrefab == null)
            return;

        Transform handSocket = GetTargetHandSocket();
        if (handSocket == null)
            return;

        if (_localHeldVisualInstance == null || _localHeldVisualSourcePrefab != _cachedEquippedVisualPrefab)
        {
            DestroyLocalHeldVisual();
            _localHeldVisualInstance = Instantiate(_cachedEquippedVisualPrefab, handSocket);
            _localHeldVisualSourcePrefab = _cachedEquippedVisualPrefab;
        }

        UpdateLocalHeldVisualTransform();
    }

    private void UpdateLocalHeldVisualTransform()
    {
        if (_localHeldVisualInstance == null)
            return;

        Transform handSocket = GetTargetHandSocket();
        if (handSocket == null)
            return;

        if (_localHeldVisualInstance.transform.parent != handSocket)
            _localHeldVisualInstance.transform.SetParent(handSocket, false);

        _localHeldVisualInstance.transform.localPosition = _cachedLocalPos;
        _localHeldVisualInstance.transform.localRotation = Quaternion.Euler(_cachedLocalEuler);
        _localHeldVisualInstance.transform.localScale = _cachedLocalScale == Vector3.zero ? Vector3.one : _cachedLocalScale;
    }

    private void DestroyLocalHeldVisual()
    {
        if (_localHeldVisualInstance == null)
            return;

        if (Application.isPlaying)
            Destroy(_localHeldVisualInstance);
        else
            DestroyImmediate(_localHeldVisualInstance);

        _localHeldVisualInstance = null;
        _localHeldVisualSourcePrefab = null;
    }

    private void Log(string message)
    {
        if (!enableDebugLogs) return;
        Debug.Log(message);
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        AutoFindRefs();
    }
#endif
}
