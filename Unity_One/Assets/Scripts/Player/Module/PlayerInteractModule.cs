using Unity.Netcode;
using UnityEngine;

public class PlayerInteractModule : NetworkBehaviour
{
    private const float RequestedPosePositionEpsilon = 0.0005f;
    private const float RequestedPoseRotationEpsilon = 0.1f;
    private const float RequestedPoseScaleEpsilon = 0.0005f;
    private const float LocalHeldVisualSettlePositionEpsilon = 0.03f;
    private const float LocalHeldVisualSettleRotationEpsilon = 5f;
    private const string RightWeaponSocketName = "RightWeaponSocket";
    private const string ItemDropAnchorName = "ItemDropAnchor";

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
    private bool _dropInProgress;
    private float _pendingStartTime;

    private NetworkObject _heldCache;
    private ItemPickupNetwork _heldPickup;
    private ItemDataSO _heldItemData;
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
    private bool _localHeldVisualReady;
    private Transform _resolvedRightWeaponSocket;
    private Transform _resolvedDropAnchor;
    private Vector3 _lastRequestedHeldWorldPosition;
    private Quaternion _lastRequestedHeldWorldRotation = Quaternion.identity;
    private Vector3 _lastRequestedHeldWorldScale = Vector3.one;
    private bool _hasLastRequestedHeldPose;

    private const float MinSocketLossyScale = 0.0001f;

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
    }

    private void OnHeldItemChanged(NetworkObjectReference previousValue, NetworkObjectReference newValue)
    {
        RestorePreviousWorldItemVisual(previousValue);

        ClearHeldRuntimeCache();
        ResetHeldMetaCache();

        _pendingAttach = false;
        _attached = false;
        _dropInProgress = false;
        _pendingStartTime = Time.time;
        _hasLastRequestedHeldPose = false;

        DestroyLocalHeldVisual();

        ResolveHeldCache();
        CacheHeldMeta();
        RefreshHeldPresentation();
    }

    private void Update()
    {
        if (_pendingAttach && !_attached && ShouldMaintainWorldAttachFallback())
        {
            if (Time.time - _pendingStartTime >= pickupPendingTime || TryApplyHeldWorldPose(false))
                ForceAttach();
        }
    }

    private void LateUpdate()
    {
        if (!_attached || !ShouldMaintainWorldAttachFallback()) return;
        TryApplyHeldWorldPose(false);
    }

    public bool HasHeldItem()
    {
        return ResolveHeldCache();
    }

    public WeaponItemDataSO GetHeldWeaponData()
    {
        if (!ResolveHeldCache())
        {
            ClearHeldRuntimeCache();
            ResetHeldMetaCache();
            return null;
        }

        if (_heldPickup == null)
        {
            ResetHeldMetaCache();
            return null;
        }

        if (_heldItemData == null && _heldWeaponData == null)
            CacheHeldMeta();

        return _heldWeaponData;
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

        if (pickup != null)
            pickup.SetHeldStateServer(true);
        else
        {
            SetHeldPhysics(netObj, true);
            SetWorldItemPresentationState(netObj, false);
        }

        _heldItem.Value = target;

        if (!useLocalVisual && disableCharacterControllerWhilePickingUp && _cc != null && _cc.enabled)
            _cc.enabled = false;

        ResolveHeldCache();
        CacheHeldMeta();
        RefreshHeldPresentation();

        Log($"[PlayerInteract] ServerTryPickup -> {netObj.name}, localVisual:{useLocalVisual}");
        return true;
    }

    public void ServerTryDrop()
    {
        if (!IsServer) return;
        if (!ResolveHeldCache()) return;

        NetworkObject netObj = _heldCache;
        bool usedLocalVisual = _useLocalHeldVisual;
        _dropInProgress = true;

        _attached = false;
        _pendingAttach = false;
        _hasLastRequestedHeldPose = false;

        if (!_hasCachedMeta)
            CacheHeldMeta();

        RefreshDropAnchorPoseImmediately();
        Log($"[PlayerInteract][DropDebug] refreshedDropPoseBeforeSample=true localVisualReady={_localHeldVisualReady}");
        GetDropAnchorWorldPose(out Vector3 dropPos, out Quaternion dropRot);
        DestroyLocalHeldVisual();
        Vector3 heldCurrentPos = netObj.transform.position;
        Quaternion heldCurrentRot = netObj.transform.rotation;

        if (_heldPickup != null)
        {
            Vector3 carrierVelocity = GetCarrierVelocity();
            Vector3 dropImpulse = transform.forward * throwForwardForce + Vector3.up * throwUpForce;
            bool rbKinematicBefore = false;
            Rigidbody heldRb = netObj.GetComponent<Rigidbody>();
            if (heldRb != null)
                rbKinematicBefore = heldRb.isKinematic;

            string localVisualInfo = string.Empty;
            if (_lastDropAnchorSource == "LocalHeldVisual" && _localHeldVisualInstance != null)
                localVisualInfo = $" localVisualPos={_localHeldVisualInstance.transform.position}";

            Log($"[PlayerInteract][DropDebug] source={_lastDropAnchorSource}{_lastDropAnchorSourceDetail} dropPos={dropPos} dropRot={dropRot.eulerAngles} heldCurrentPos={heldCurrentPos} heldCurrentRot={heldCurrentRot.eulerAngles} appliedForwardOffset={_lastAppliedDropForwardOffset} appliedUpOffset={_lastAppliedDropUpOffset}{localVisualInfo}");
            bool restored = _heldPickup.TryRestoreDroppedStateServer(dropPos, dropRot, GetDefaultWorldItemScale(), carrierVelocity, dropImpulse);
            if (restored)
            {
                bool rbKinematicAfter = heldRb != null && heldRb.isKinematic;
                Log($"[PlayerInteract][DropDebug] restoreTarget={dropPos} postRestorePos={netObj.transform.position} rbKinematicBefore={rbKinematicBefore} rbKinematicAfter={rbKinematicAfter}");
            }
            if (!restored)
            {
                ApplyWorldItemPose(netObj, dropPos, dropRot, GetDefaultWorldItemScale());
                _heldPickup.SetHeldStateServer(false);

                Rigidbody rb = netObj.GetComponent<Rigidbody>();
                if (rb != null)
                {
                    rb.linearVelocity = carrierVelocity;
                    rb.angularVelocity = Vector3.zero;
                    rb.AddForce(dropImpulse, ForceMode.Impulse);
                }
            }
        }
        else
        {
            string localVisualInfo = string.Empty;
            if (_lastDropAnchorSource == "LocalHeldVisual" && _localHeldVisualInstance != null)
                localVisualInfo = $" localVisualPos={_localHeldVisualInstance.transform.position}";

            Log($"[PlayerInteract][DropDebug] source={_lastDropAnchorSource}{_lastDropAnchorSourceDetail} dropPos={dropPos} dropRot={dropRot.eulerAngles} heldCurrentPos={heldCurrentPos} heldCurrentRot={heldCurrentRot.eulerAngles} appliedForwardOffset={_lastAppliedDropForwardOffset} appliedUpOffset={_lastAppliedDropUpOffset}{localVisualInfo}");
            ApplyWorldItemPose(netObj, dropPos, dropRot, GetDefaultWorldItemScale());
            SetWorldItemPresentationState(netObj, true);
            SetHeldPhysics(netObj, false);

            Rigidbody rb = netObj.GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.linearVelocity = GetCarrierVelocity();
                rb.angularVelocity = Vector3.zero;
                rb.AddForce(transform.forward * throwForwardForce + Vector3.up * throwUpForce, ForceMode.Impulse);
            }
        }

        if (NetworkManager != null)
            netObj.ChangeOwnership(NetworkManager.ServerClientId);

        _heldItem.Value = default;
        _dropInProgress = false;

        if (_cc != null && !_cc.enabled)
            _cc.enabled = true;

        Log($"[PlayerInteract] ServerTryDrop -> {netObj.name}, localVisual:{usedLocalVisual}");
    }

    private void RefreshDropAnchorPoseImmediately()
    {
        if (_useLocalHeldVisual && _localHeldVisualInstance != null)
        {
            GetRightWeaponSocket();
            UpdateLocalHeldVisualTransform();
        }

        GetDropAnchorTransform();
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
        if (!ShouldMaintainWorldAttachFallback()) return;

        _pendingAttach = false;

        if (_cc != null && !_cc.enabled)
            _cc.enabled = true;

        _attached = TryApplyHeldWorldPose(true);

        if (!_attached)
        {
            _pendingAttach = true;
            _pendingStartTime = Time.time;
        }
    }

    private bool ResolveHeldCache()
    {
        if (_heldCache != null && _heldCache.IsSpawned)
            return true;

        if (_heldItem.Value.TryGet(out NetworkObject netObj))
        {
            _heldCache = netObj;
            _heldPickup = _heldCache != null ? _heldCache.GetComponent<ItemPickupNetwork>() : null;
            _heldItemData = _heldPickup != null ? _heldPickup.itemData : null;
            _heldWeaponData = _heldItemData as WeaponItemDataSO;
            return _heldCache != null && _heldCache.IsSpawned;
        }

        ClearHeldRuntimeCache();
        _hasLastRequestedHeldPose = false;
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

    private string _lastDropAnchorSource = "Unknown";
    private string _lastDropAnchorSourceDetail = string.Empty;
    private float _lastAppliedDropForwardOffset;
    private float _lastAppliedDropUpOffset;

    private Vector3 ApplyDropOffsetBySource(Vector3 basePosition, bool suppressHandOffset)
    {
        _lastAppliedDropForwardOffset = suppressHandOffset ? 0f : dropHandForwardOffset;
        _lastAppliedDropUpOffset = suppressHandOffset ? 0f : dropHandUpOffset;
        return basePosition + transform.forward * _lastAppliedDropForwardOffset + Vector3.up * _lastAppliedDropUpOffset;
    }

    private Transform TryGetLocalHeldVisualAnchor(Transform visualRoot)
    {
        if (visualRoot == null)
            return null;

        string[] anchorNames =
        {
            "DropAnchor",
            "VisualDropAnchor",
            "HeldVisualAnchor"
        };

        for (int i = 0; i < anchorNames.Length; i++)
        {
            Transform anchor = visualRoot.Find(anchorNames[i]);
            if (anchor != null)
                return anchor;
        }

        for (int i = 0; i < visualRoot.childCount; i++)
        {
            Transform child = visualRoot.GetChild(i);
            if (child == null)
                continue;

            for (int j = 0; j < anchorNames.Length; j++)
            {
                if (child.name == anchorNames[j])
                    return child;
            }
        }

        return null;
    }

    private bool IsLocalHeldVisualSettledAgainstSocket(Transform visualRoot)
    {
        if (visualRoot == null)
            return false;

        Transform handSocket = GetRightWeaponSocket();
        if (handSocket == null)
            return false;

        Vector3 expectedWorldPos = handSocket.TransformPoint(_cachedLocalPos);
        Quaternion expectedWorldRot = handSocket.rotation * Quaternion.Euler(_cachedLocalEuler);

        float positionDelta = Vector3.Distance(visualRoot.position, expectedWorldPos);
        float rotationDelta = Quaternion.Angle(visualRoot.rotation, expectedWorldRot);

        return positionDelta <= LocalHeldVisualSettlePositionEpsilon &&
               rotationDelta <= LocalHeldVisualSettleRotationEpsilon;
    }

    private bool TryGetLocalHeldVisualWorldPose(out Vector3 pos, out Quaternion rot)
    {
        pos = Vector3.zero;
        rot = Quaternion.identity;

        if (_localHeldVisualInstance == null)
            return false;

        Transform visualRoot = _localHeldVisualInstance.transform;
        if (!_localHeldVisualReady || visualRoot == null || !visualRoot.gameObject.activeInHierarchy)
            return false;

        if (!IsLocalHeldVisualSettledAgainstSocket(visualRoot))
            return false;

        Transform anchor = TryGetLocalHeldVisualAnchor(visualRoot);
        if (anchor != null)
        {
            pos = anchor.position;
            rot = anchor.rotation;
            _lastDropAnchorSourceDetail = "(Anchor)";
            return true;
        }

        Renderer renderer = _localHeldVisualInstance.GetComponentInChildren<Renderer>(true);
        if (renderer != null)
        {
            pos = renderer.bounds.center;
            rot = visualRoot.rotation;
            _lastDropAnchorSourceDetail = "(RendererBounds)";
            return true;
        }

        pos = visualRoot.position;
        rot = visualRoot.rotation;
        _lastDropAnchorSourceDetail = "(RootFallback)";
        return true;
    }

    private void GetDropAnchorWorldPose(out Vector3 pos, out Quaternion rot)
    {
        _lastDropAnchorSourceDetail = string.Empty;
        if (TryGetLocalHeldVisualWorldPose(out pos, out rot))
        {
            _lastDropAnchorSource = "LocalHeldVisual";
            pos = ApplyDropOffsetBySource(pos, _lastDropAnchorSourceDetail == "(Anchor)");
            return;
        }

        if (_localHeldVisualInstance != null && _localHeldVisualReady)
            _lastDropAnchorSourceDetail = "(localVisualUnsettled)";

        Transform dropAnchor = GetDropAnchorTransform();
        if (dropAnchor != null)
        {
            _lastDropAnchorSource = "ItemDropAnchor";
            pos = ApplyDropOffsetBySource(dropAnchor.position, false);
            rot = dropAnchor.rotation;
            return;
        }

        Transform rightWeaponSocket = GetRightWeaponSocket();
        if (rightWeaponSocket != null)
        {
            _lastDropAnchorSource = "RightWeaponSocket";
            pos = ApplyDropOffsetBySource(rightWeaponSocket.position, false);
            rot = rightWeaponSocket.rotation;
            return;
        }

        _lastDropAnchorSource = "RootFallback";
        _lastAppliedDropForwardOffset = 0f;
        _lastAppliedDropUpOffset = 0f;
        pos = transform.position + transform.forward * 1.2f + Vector3.up * 1.0f;
        rot = Quaternion.LookRotation(transform.forward, Vector3.up);
    }

    private Transform GetTargetHandSocket()
    {
        AutoFindRefs();

        Transform rightWeaponSocket = GetRightWeaponSocket();
        if (IsValidHandSocket(rightWeaponSocket))
            return rightWeaponSocket;

        Transform humanoidRightHand = GetHumanoidHandBone(HumanBodyBones.RightHand);
        Transform humanoidLeftHand = GetHumanoidHandBone(HumanBodyBones.LeftHand);

        if (_heldWeaponData != null && _heldWeaponData.hand == WeaponHand.Left)
        {
            if (IsValidHandSocket(leftHandBone))
                return leftHandBone;

            if (IsValidHandSocket(humanoidLeftHand))
                return humanoidLeftHand;
        }

        if (IsValidHandSocket(rightHandBone))
            return rightHandBone;

        if (IsValidHandSocket(humanoidRightHand))
            return humanoidRightHand;

        if (IsValidHandSocket(leftHandBone))
            return leftHandBone;

        if (IsValidHandSocket(humanoidLeftHand))
            return humanoidLeftHand;

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
                rb.Sleep();
            }
            else
            {
                rb.isKinematic = false;
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
                rb.WakeUp();
            }
        }
    }

    private void SetWorldItemPresentationState(NetworkObject netObj, bool visible)
    {
        if (netObj == null) return;

        ItemPickupNetwork pickup = netObj.GetComponent<ItemPickupNetwork>();
        if (pickup != null)
            pickup.SetWorldVisualVisibleServer(visible);
        else
            SetRendererEnabled(netObj, visible);

        Canvas[] canvases = netObj.GetComponentsInChildren<Canvas>(true);
        for (int i = 0; i < canvases.Length; i++)
        {
            if (canvases[i] != null)
                canvases[i].enabled = visible;
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
        ResetHeldMetaCache();

        if (!ResolveHeldCache()) return;
        if (_heldPickup == null) return;

        _heldItemData = _heldPickup.itemData;
        _heldWeaponData = _heldItemData as WeaponItemDataSO;
        if (_heldWeaponData == null) return;

        _cachedWeaponAnimId = _heldWeaponData.weaponAnimID;
        _cachedLocalPos = _heldWeaponData.equippedLocalPosition;
        _cachedLocalEuler = _heldWeaponData.equippedLocalEulerAngles;
        _cachedLocalScale = _heldWeaponData.equippedLocalScale == Vector3.zero ? Vector3.one : _heldWeaponData.equippedLocalScale;
        _cachedEquippedVisualPrefab = _heldWeaponData.equippedModelPrefab;
        _hasCachedMeta = true;
        _useLocalHeldVisual = ShouldUseLocalHeldVisual(_heldWeaponData);
    }

    private void ClearHeldRuntimeCache()
    {
        _heldCache = null;
        _heldPickup = null;
        _heldItemData = null;
        _heldWeaponData = null;
        _hasLastRequestedHeldPose = false;
    }

    private void ResetHeldMetaCache()
    {
        _hasCachedMeta = false;
        _heldItemData = null;
        _heldWeaponData = null;
        _cachedWeaponAnimId = 0;
        _cachedLocalPos = defaultHeldLocalPosition;
        _cachedLocalEuler = defaultHeldLocalEulerAngles;
        _cachedLocalScale = defaultHeldLocalScale;
        _cachedEquippedVisualPrefab = null;
        _useLocalHeldVisual = false;
    }

    private bool ShouldUseLocalHeldVisual(WeaponItemDataSO weaponData)
    {
        if (!preferLocalHeldVisual)
            return false;

        if (weaponData == null || weaponData.equippedModelPrefab == null)
            return false;

        if (weaponData.equippedModelPrefab.GetComponentInChildren<NetworkObject>(true) != null)
        {
            Log($"[PlayerInteract] Equipped visual prefab '{weaponData.equippedModelPrefab.name}' contains NetworkObject. Local visual skipped.");
            return false;
        }

        if (weaponData.equippedModelPrefab.GetComponentInChildren<NetworkBehaviour>(true) != null)
        {
            Log($"[PlayerInteract] Equipped visual prefab '{weaponData.equippedModelPrefab.name}' contains NetworkBehaviour. Local visual skipped.");
            return false;
        }

        return true;
    }

    private bool ShouldMaintainWorldAttachFallback()
    {
        return !_useLocalHeldVisual;
    }

    private bool IsStableHeldState()
    {
        if (_dropInProgress)
            return false;

        if (!ResolveHeldCache())
            return false;

        bool heldStateActive = _attached || _useLocalHeldVisual;
        return heldStateActive && !_pendingAttach;
    }

    private void RefreshHeldPresentation()
    {
        if (!ResolveHeldCache())
        {
            DestroyLocalHeldVisual();
            ApplyHeldPresentationTransition(false, false, false);
            return;
        }

        if (EnsureLocalHeldVisual())
        {
            ApplyHeldPresentationTransition(true, false, false);
            return;
        }

        DestroyLocalHeldVisual();
        ApplyHeldPresentationTransition(true, true, fallbackToWorldAttachWhenNoVisual);
    }

    private bool EnsureLocalHeldVisual()
    {
        if (!_useLocalHeldVisual)
            return false;

        if (_cachedEquippedVisualPrefab == null)
        {
            Log("[PlayerInteract] No equippedModelPrefab assigned.");
            return false;
        }

        Transform handSocket = GetRightWeaponSocket();
        if (handSocket == null)
        {
            Log("[PlayerInteract] RightWeaponSocket not found. Local held visual skipped.");
            return false;
        }

        if (_localHeldVisualInstance == null || _localHeldVisualSourcePrefab != _cachedEquippedVisualPrefab)
        {
            DestroyLocalHeldVisual();
            _localHeldVisualInstance = Instantiate(_cachedEquippedVisualPrefab);
            _localHeldVisualInstance.transform.SetParent(handSocket, false);
            _localHeldVisualSourcePrefab = _cachedEquippedVisualPrefab;
            _localHeldVisualReady = false;
            DisableLocalHeldVisualPhysics(_localHeldVisualInstance);
            Log($"[PlayerInteract] Spawn local held visual: {_cachedEquippedVisualPrefab.name}");
        }

        UpdateLocalHeldVisualTransform();
        return _localHeldVisualInstance != null;
    }

    private void UpdateLocalHeldVisualTransform()
    {
        if (_localHeldVisualInstance == null)
            return;

        Transform handSocket = GetRightWeaponSocket();
        if (handSocket == null)
            return;

        if (_localHeldVisualInstance.transform.parent != handSocket)
            _localHeldVisualInstance.transform.SetParent(handSocket, false);

        _localHeldVisualInstance.transform.localPosition = _cachedLocalPos;
        _localHeldVisualInstance.transform.localRotation = Quaternion.Euler(_cachedLocalEuler);
        _localHeldVisualInstance.transform.localScale = SanitizeVisualScale(_cachedLocalScale);
        _localHeldVisualReady = true;
    }

    private void DisableLocalHeldVisualPhysics(GameObject visualRoot)
    {
        if (visualRoot == null)
            return;

        Collider[] colliders = visualRoot.GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < colliders.Length; i++)
        {
            if (colliders[i] != null)
                colliders[i].enabled = false;
        }

        Rigidbody[] rigidbodies = visualRoot.GetComponentsInChildren<Rigidbody>(true);
        for (int i = 0; i < rigidbodies.Length; i++)
        {
            if (rigidbodies[i] == null)
                continue;

            rigidbodies[i].isKinematic = true;
            rigidbodies[i].useGravity = false;
            rigidbodies[i].linearVelocity = Vector3.zero;
            rigidbodies[i].angularVelocity = Vector3.zero;
        }
    }

    private bool TryAttachWorldHeldItemImmediate()
    {
        if (!ShouldMaintainWorldAttachFallback())
            return false;

        return TryApplyHeldWorldPose(true);
    }

    private void ApplyHeldPresentationTransition(bool hasHeldItem, bool worldVisible, bool allowWorldAttachFallback)
    {
        if (!hasHeldItem || _heldCache == null)
        {
            _pendingAttach = false;
            _attached = false;
            return;
        }

        SetWorldItemPresentationState(_heldCache, worldVisible);
        _attached = allowWorldAttachFallback && TryAttachWorldHeldItemImmediate();
        _pendingAttach = allowWorldAttachFallback && !_attached;

        if (_pendingAttach)
            _pendingStartTime = Time.time;

        if (_cc != null && !_cc.enabled)
            _cc.enabled = true;
    }

    private void SetRendererEnabled(NetworkObject netObj, bool visible)
    {
        Renderer[] renderers = netObj.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] != null)
                renderers[i].enabled = visible;
        }
    }

    private Transform GetHumanoidHandBone(HumanBodyBones handBone)
    {
        if (_anim == null || !_anim.isHuman)
            return null;

        return _anim.GetBoneTransform(handBone);
    }

    private bool IsValidHandSocket(Transform handSocket)
    {
        if (handSocket == null)
            return false;

        Vector3 lossyScale = handSocket.lossyScale;
        if (!IsFinite(lossyScale.x) || !IsFinite(lossyScale.y) || !IsFinite(lossyScale.z))
            return false;

        return Mathf.Abs(lossyScale.x) > MinSocketLossyScale
            && Mathf.Abs(lossyScale.y) > MinSocketLossyScale
            && Mathf.Abs(lossyScale.z) > MinSocketLossyScale;
    }

    private Transform GetRightWeaponSocket()
    {
        AutoFindRefs();

        if (IsConfiguredRightWeaponSocket(rightHandBone))
        {
            _resolvedRightWeaponSocket = rightHandBone;
            return _resolvedRightWeaponSocket;
        }

        if (IsValidHandSocket(_resolvedRightWeaponSocket))
            return _resolvedRightWeaponSocket;

        Transform[] childTransforms = GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < childTransforms.Length; i++)
        {
            Transform candidate = childTransforms[i];
            if (candidate == null || candidate.name != RightWeaponSocketName)
                continue;

            if (!IsValidHandSocket(candidate))
                continue;

            _resolvedRightWeaponSocket = candidate;
            return _resolvedRightWeaponSocket;
        }

        return null;
    }

    private Transform GetDropAnchorTransform()
    {
        if (IsValidHandSocket(_resolvedDropAnchor))
            return _resolvedDropAnchor;

        Transform[] childTransforms = GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < childTransforms.Length; i++)
        {
            Transform candidate = childTransforms[i];
            if (candidate == null || candidate.name != ItemDropAnchorName)
                continue;

            if (!IsValidHandSocket(candidate))
                continue;

            _resolvedDropAnchor = candidate;
            return _resolvedDropAnchor;
        }

        return null;
    }

    private bool IsConfiguredRightWeaponSocket(Transform candidate)
    {
        if (!IsValidHandSocket(candidate))
            return false;

        Transform humanoidRightHand = GetHumanoidHandBone(HumanBodyBones.RightHand);
        if (candidate == humanoidRightHand)
            return false;

        return true;
    }

    private Vector3 SanitizeVisualScale(Vector3 scale)
    {
        if (scale == Vector3.zero)
            return Vector3.one;

        if (!IsFinite(scale.x) || Mathf.Approximately(scale.x, 0f))
            scale.x = 1f;

        if (!IsFinite(scale.y) || Mathf.Approximately(scale.y, 0f))
            scale.y = 1f;

        if (!IsFinite(scale.z) || Mathf.Approximately(scale.z, 0f))
            scale.z = 1f;

        return scale;
    }

    private Vector3 GetCarrierVelocity()
    {
        if (_cc != null)
            return _cc.velocity;

        return Vector3.zero;
    }

    private bool TryApplyHeldWorldPose(bool syncNetworkTransform)
    {
        if (!ResolveHeldCache())
            return false;

        if (!ShouldMaintainWorldAttachFallback() && IsStableHeldState())
            return false;

        if (!TryGetHandWorldPose(out Vector3 handPos, out Quaternion handRot))
            return false;

        Vector3 heldWorldScale = GetHeldWorldScale();
        bool isStableHeld = _attached && !_pendingAttach;

        if (!syncNetworkTransform && isStableHeld && _hasLastRequestedHeldPose)
        {
            float positionDeltaSqr = (_lastRequestedHeldWorldPosition - handPos).sqrMagnitude;
            float rotationDelta = Quaternion.Angle(_lastRequestedHeldWorldRotation, handRot);
            float scaleDeltaSqr = (_lastRequestedHeldWorldScale - heldWorldScale).sqrMagnitude;

            if (positionDeltaSqr <= RequestedPosePositionEpsilon * RequestedPosePositionEpsilon &&
                rotationDelta <= RequestedPoseRotationEpsilon &&
                scaleDeltaSqr <= RequestedPoseScaleEpsilon * RequestedPoseScaleEpsilon)
            {
                return true;
            }
        }

        ApplyWorldItemPose(_heldCache, handPos, handRot, heldWorldScale, syncNetworkTransform);
        _lastRequestedHeldWorldPosition = handPos;
        _lastRequestedHeldWorldRotation = handRot;
        _lastRequestedHeldWorldScale = heldWorldScale;
        _hasLastRequestedHeldPose = true;
        return true;
    }

    private void ApplyWorldItemPose(NetworkObject netObj, Vector3 worldPos, Quaternion worldRot, Vector3 worldScale, bool syncNetworkTransform = true)
    {
        if (netObj == null)
            return;

        ItemPickupNetwork pickup = netObj.GetComponent<ItemPickupNetwork>();
        if (pickup != null)
        {
            pickup.ApplyPose(worldPos, worldRot, worldScale, syncNetworkTransform);
            return;
        }

        netObj.transform.SetPositionAndRotation(worldPos, worldRot);
        netObj.transform.localScale = worldScale;
    }

    private Vector3 GetHeldWorldScale()
    {
        return SanitizeVisualScale(_hasCachedMeta ? _cachedLocalScale : defaultHeldLocalScale);
    }

    private Vector3 GetDefaultWorldItemScale()
    {
        if (_heldPickup != null)
            return SanitizeVisualScale(_heldPickup.GetDefaultLocalScale());

        if (_heldCache != null)
            return SanitizeVisualScale(_heldCache.transform.localScale);

        return Vector3.one;
    }

    private bool IsFinite(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }

    private void DestroyLocalHeldVisual()
    {
        _localHeldVisualReady = false;

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
