using Unity.Netcode;
using UnityEngine;

public class PlayerInteractModule : NetworkBehaviour
{
    private const float RequestedPosePositionEpsilon = 0.0005f;
    private const float RequestedPoseRotationEpsilon = 0.1f;
    private const float RequestedPoseScaleEpsilon = 0.0005f;
    private const float LocalHeldVisualSettlePositionEpsilon = 0.03f;
    private const float LocalHeldVisualSettleRotationEpsilon = 5f;
    private const float PickupExpressionHoldSeconds = 0.75f;
    private const string RightWeaponSocketName = "RightWeaponSocket";
    private const string ItemDropAnchorName = "ItemDropAnchor";
    private const string GrabberMoveMultiplierKey = "CharacterGrabber";
    private const string GrabbedMoveMultiplierKey = "CharacterGrabbed";
    private const string CharacterGrabEscapeReason = "escape";
    private const float GameplayGateResolveRetryInterval = 0.5f;

    [Header("Raycast")]
    [Tooltip("오너가 사용하는 카메라")]
    [SerializeField] private Camera ownerCamera;

    [Tooltip("픽업 레이캐스트 거리")]
    [SerializeField] private float pickupDistance = 6f;

    [Tooltip("픽업 가능한 레이어 마스크")]
    [SerializeField] private LayerMask pickupMask;

    [Tooltip("쿼터뷰 카메라에서도 작은 아이템을 덜 놓치도록 보정할 픽업 구체 반경")]
    [SerializeField] private float pickupRayRadius = 0.2f;

    [Tooltip("카메라 레이 실패 시 플레이어 쪽 보조 레이를 시작할 높이")]
    [SerializeField] private float pickupFallbackOriginHeight = 1.0f;

    [Tooltip("카메라 레이 실패 시 플레이어 쪽 보조 레이를 전방으로 미는 거리")]
    [SerializeField] private float pickupFallbackForwardOffset = 0.35f;

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

    [Header("Character Grab")]
    [Tooltip("Production MotorShell에서 legacy item route를 끄고 Character Grab/Lift/Throw만 사용합니다.")]
    [SerializeField] private bool characterGrabOnlyMode = false;

    [Tooltip("빈손 상태에서 다른 캐릭터를 잡을 수 있게 할지 여부입니다.")]
    [SerializeField] private bool enableCharacterGrab = true;

    [Tooltip("캐릭터 grab 대상 탐색 거리입니다.")]
    [SerializeField] private float characterGrabDistance = 2.0f;

    [Tooltip("캐릭터 grab 대상 탐색 반경입니다.")]
    [SerializeField] private float characterGrabRadius = 0.45f;

    [Tooltip("캐릭터 grab 대상 탐색에 사용할 레이어입니다.")]
    [SerializeField] private LayerMask characterGrabMask = ~0;

    [Tooltip("다른 캐릭터를 잡고 있는 동안 잡는 사람의 이동 속도 배율입니다.")]
    [SerializeField] private float grabberMoveSpeedMultiplier = 0.65f;

    [Tooltip("잡힌 사람의 이동 속도 배율입니다.")]
    [SerializeField] private float grabbedMoveSpeedMultiplier = 0.2f;

    [Tooltip("캐릭터를 잡은 뒤 들어 올릴 수 있게 되기까지 걸리는 시간입니다.")]
    [SerializeField] private float liftChargeDuration = 1.2f;

    [Tooltip("캐릭터를 최대 몇 초 동안 잡고 있을 수 있는지입니다.")]
    [SerializeField] private float maxCharacterGrabDuration = 5f;

    [Tooltip("잡힌 사람이 탈출하기 위해 필요한 Space 입력 횟수입니다.")]
    [SerializeField] private int escapeTapRequiredCount = 6;

    [Tooltip("탈출 입력이 너무 빠르게 중복 처리되지 않도록 하는 최소 간격입니다.")]
    [SerializeField] private float escapeTapMinInterval = 0.1f;

    [Tooltip("탈출 성공 후 다시 잡히지 않는 시간입니다.")]
    [SerializeField] private float escapeRegrabImmunitySeconds = 0.75f;

    [Tooltip("캐릭터 grab 상태 로그를 출력할지 여부입니다.")]
    [SerializeField] private bool characterGrabDebugLogs = false;

    [Header("Character Carry Follow")]
    [Tooltip("Lift ready 이후 잡힌 캐릭터를 grabber carry anchor 위치로 따라오게 할지 여부입니다.")]
    [SerializeField] private bool enableCharacterCarryFollow = true;

    [Tooltip("잡힌 캐릭터를 grabber 앞쪽으로 얼마나 떨어뜨려 들고 있을지입니다.")]
    [SerializeField] private float characterCarryForwardOffset = 0.75f;

    [Tooltip("잡힌 캐릭터를 grabber 위쪽으로 얼마나 올릴지입니다.")]
    [SerializeField] private float characterCarryUpOffset = 1.05f;

    [Tooltip("잡힌 캐릭터 carry 위치의 좌우 오프셋입니다.")]
    [SerializeField] private float characterCarryRightOffset = 0f;

    [Tooltip("잡힌 캐릭터가 carry 위치를 따라가는 보간 속도입니다. 0 이하이면 즉시 이동합니다.")]
    [SerializeField] private float characterCarryFollowLerp = 18f;

    [Tooltip("grabber와 grabbed target 사이 거리가 너무 멀어지면 release하는 거리입니다.")]
    [SerializeField] private float characterCarryMaxDistance = 4f;

    [Tooltip("carry 중 target CharacterController를 잠시 끌지 여부입니다.")]
    [SerializeField] private bool characterCarryDisableTargetController = true;

    [Tooltip("carry 중 target Rigidbody를 kinematic으로 전환할지 여부입니다.")]
    [SerializeField] private bool characterCarrySetTargetRigidbodyKinematic = true;

    [Tooltip("carry 중 target이 grabber의 forward 방향을 바라보게 할지 여부입니다.")]
    [SerializeField] private bool characterCarryFaceGrabberForward = true;

    [Tooltip("release 시 target 위치가 바닥에 너무 박히지 않도록 보정할 Y 오프셋입니다.")]
    [SerializeField] private float characterCarryGroundReleaseYOffset = 0.05f;

    [Header("Character Throw")]
    [Tooltip("들고 있는 캐릭터를 던질 수 있게 할지 여부입니다.")]
    [SerializeField] private bool enableCharacterThrow = true;

    [Tooltip("던질 때 전방으로 적용할 넉백 힘입니다.")]
    [SerializeField] private float characterThrowForwardImpulse = 14f;

    [Tooltip("던질 때 위쪽으로 적용할 넉백 힘입니다.")]
    [SerializeField] private float characterThrowUpImpulse = 3f;

    [Tooltip("던지기 직전 target을 grabber 앞쪽으로 놓을 위치 보정입니다.")]
    [SerializeField] private float characterThrowReleaseForwardOffset = 0.75f;

    [Tooltip("던지기 직전 target을 위쪽으로 살짝 띄울 위치 보정입니다.")]
    [SerializeField] private float characterThrowReleaseUpOffset = 0.35f;

    [Tooltip("던져진 직후 다시 잡히지 않는 시간입니다.")]
    [SerializeField] private float characterThrowRegrabImmunitySeconds = 0.75f;

    [Tooltip("던질 방향을 grabber forward 기준으로 사용할지 여부입니다.")]
    [SerializeField] private bool characterThrowUseCarryAnchorDirection = true;

    [Header("Debug")]
    [Tooltip("상호작용/장착 처리 디버그 로그를 출력할지 여부입니다.")]
    [SerializeField] private bool enableDebugLogs = false;

    private readonly NetworkVariable<NetworkObjectReference> _heldItem =
        new NetworkVariable<NetworkObjectReference>(
            default,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server
        );

    private bool _ownerMode;
    private GameStateManager _gameplayGateStateManager;
    private PlayerStatusModule _gameplayGateOwnerStatus;
    private NetworkObject _gameplayGateOwnerRoot;
    private PostItRoundManager _postItRoundManager;
    private PlayerPostItInventory _postItInventory;
    private HamsterMotorShellRagdollRecoveryAdapter _motorShellRecoveryAdapter;
    private HamsterMotorShellItemAdapter _motorShellItemAdapter;
    private float _nextGameplayGateStateResolveTime = float.NegativeInfinity;
    private float _nextGameplayGateStatusResolveTime = float.NegativeInfinity;
    private bool _pendingAttach;
    private bool _attached;
    private bool _dropInProgress;
    private float _pendingStartTime;
    private bool _hasExternalHeldItemPoseOverride;
    private Vector3 _externalHeldItemPoseEulerOffset;
    private string _externalHeldItemPoseSource;
    private bool _isRefreshingExternalHeldItemPose;
    private bool _isClearingHeldRuntimeCache;
    private bool _isClearingExternalHeldItemPose;

    private NetworkObject _heldCache;
    private ItemPickupNetwork _heldPickup;
    private ItemDataSO _heldItemData;
    private WeaponItemDataSO _heldWeaponData;

    private NetworkObjectReference _grabbedCharacter;
    private NetworkObjectReference _grabbedByCharacter;
    private PlayerInteractModule _grabbedCharacterInteract;
    private PlayerInteractModule _grabbedByInteract;
    private PlayerStatusModule _grabbedCharacterStatus;
    private PlayerStatusModule _grabbedByStatus;
    private float _characterGrabStartedAt;
    private float _characterLiftReadyAt;
    private bool _isCharacterLiftReady;
    private int _escapeTapCount;
    private float _lastEscapeTapAt;
    private float _regrabImmuneUntil;
    private bool _isCarryingCharacter;
    private CharacterController _carriedCharacterController;
    private Rigidbody _carriedCharacterRigidbody;
    private bool _carriedCharacterControllerWasEnabled;
    private bool _carriedCharacterRigidbodyWasKinematic;
    private bool _carriedCharacterRigidbodyUseGravity;
    private Transform _carriedCharacterRoot;

    private int _cachedWeaponAnimId;
    private Vector3 _cachedLocalPos;
    private Vector3 _cachedLocalEuler;
    private Vector3 _cachedLocalScale;
    private GameObject _cachedEquippedVisualPrefab;
    private bool _hasCachedMeta;
    private bool _useLocalHeldVisual;

    private CharacterController _cc;
    private Animator _anim;
    private FaceExpressionController _faceExpressionController;

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

        ResetGameplayGateCache();
        _cc = GetComponentInParent<CharacterController>();
        _anim = GetComponentInParent<Animator>();

        AutoFindRefs();
        _heldItem.OnValueChanged += OnHeldItemChanged;

        if (!characterGrabOnlyMode)
        {
            ResolveHeldCache();
            CacheHeldMeta();
            RefreshHeldPresentation();
        }
    }

    public override void OnNetworkDespawn()
    {
        _heldItem.OnValueChanged -= OnHeldItemChanged;
        ClearExternalHeldItemPoseOverrideInternal("NetworkDespawn", false);
        DestroyLocalHeldVisual();
        CleanupCharacterGrabOnLifecycle("network-despawn");
        ResetGameplayGateCache();
        base.OnNetworkDespawn();
    }

    private void OnDisable()
    {
        ClearExternalHeldItemPoseOverrideInternal("OnDisable", false);
        CleanupCharacterGrabOnLifecycle("disable");
    }

    public override void OnDestroy()
    {
        ClearExternalHeldItemPoseOverrideInternal("OnDestroy", false);
        CleanupCharacterGrabOnLifecycle("destroy");
        base.OnDestroy();
    }

    private void AutoFindRefs()
    {
        if (ownerCamera == null)
        {
            PlayerHub hub = GetComponentInParent<PlayerHub>();
            if (hub != null)
                ownerCamera = hub.PlayerCamera;

            if (ownerCamera == null)
                ownerCamera = GetComponentInChildren<Camera>(true);
        }

        if (_anim == null)
            _anim = GetComponentInParent<Animator>();

        if (_faceExpressionController == null)
        {
            PlayerHub hub = GetComponentInParent<PlayerHub>();
            if (hub != null)
                _faceExpressionController = hub.GetComponentInChildren<FaceExpressionController>(true);

            if (_faceExpressionController == null)
                _faceExpressionController = transform.root != null
                    ? transform.root.GetComponentInChildren<FaceExpressionController>(true)
                    : GetComponentInChildren<FaceExpressionController>(true);
        }
    }

    private void OnHeldItemChanged(NetworkObjectReference previousValue, NetworkObjectReference newValue)
    {
        if (characterGrabOnlyMode)
        {
            ClearHeldRuntimeCache();
            ResetHeldMetaCache();
            DestroyLocalHeldVisual();
            return;
        }

        bool hadHeldItem = TryGetNetworkObjectSafe(previousValue, out NetworkObject previousItem) && previousItem != null;

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

        if (!hadHeldItem && ResolveHeldCache())
            TryTriggerPickupExpression();
    }

    private void Update()
    {
        if (IsServer)
            ServerTickCharacterGrab();

        if (characterGrabOnlyMode)
            return;

        if (_pendingAttach && !_attached && ShouldMaintainWorldAttachFallback())
        {
            if (Time.time - _pendingStartTime >= pickupPendingTime || TryApplyHeldWorldPose(false))
                ForceAttach();
        }
    }

    private void LateUpdate()
    {
        if (characterGrabOnlyMode)
            return;

        if (_hasExternalHeldItemPoseOverride)
            ForceRefreshHeldItemPoseForExternalOverride();

        if (!_attached || !ShouldMaintainWorldAttachFallback()) return;
        TryApplyHeldWorldPose(false);
    }

    public bool HasHeldItem()
    {
        if (characterGrabOnlyMode)
            return false;

        return ResolveHeldCache();
    }

    public bool TryGetHeldItemId(out int itemId)
    {
        itemId = 0;

        if (characterGrabOnlyMode)
            return false;

        if (!ResolveHeldCache())
            return false;

        if (_heldPickup != null)
        {
            int pickupItemId = _heldPickup.ItemId;
            if (pickupItemId > 0)
            {
                itemId = pickupItemId;
                return true;
            }
        }

        if (_heldItemData != null && _heldItemData.itemId > 0)
        {
            itemId = _heldItemData.itemId;
            return true;
        }

        if (_heldWeaponData != null && _heldWeaponData.itemId > 0)
        {
            itemId = _heldWeaponData.itemId;
            return true;
        }

        return false;
    }

    public WeaponItemDataSO GetHeldWeaponData()
    {
        if (characterGrabOnlyMode)
            return null;

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

    public void SetExternalHeldItemPoseOverride(Vector3 eulerOffset, string source = null)
    {
        if (characterGrabOnlyMode)
            return;

        _hasExternalHeldItemPoseOverride = true;
        _externalHeldItemPoseEulerOffset = eulerOffset;
        _externalHeldItemPoseSource = source;
        if (HasActiveHeldItemTargetForExternalPose())
            ForceRefreshHeldItemPoseForExternalOverride();

        Log($"[PlayerInteract] External held item pose override set source={source ?? "<null>"} offset={eulerOffset}");
    }

    public void ClearExternalHeldItemPoseOverride(string source = null)
    {
        if (characterGrabOnlyMode)
            return;

        ClearExternalHeldItemPoseOverrideInternal(source, true);
    }

    public bool HasExternalHeldItemPoseOverride => _hasExternalHeldItemPoseOverride;

    public Transform GetHeldItemVisualTransform()
    {
        if (characterGrabOnlyMode)
            return null;

        if (_localHeldVisualInstance != null)
            return _localHeldVisualInstance.transform;

        return _heldCache != null && _heldCache.IsSpawned ? _heldCache.transform : null;
    }

    public bool IsGrabbingCharacter => HasCharacterGrabReference(_grabbedCharacter, _grabbedCharacterInteract);
    public bool IsGrabbedByCharacter => HasCharacterGrabReference(_grabbedByCharacter, _grabbedByInteract);
    public bool IsCharacterLiftReady => IsGrabbingCharacter && _isCharacterLiftReady;
    public bool IsCharacterGrabBusy => IsGrabbingCharacter || IsGrabbedByCharacter;
    public bool CanThrowCarriedCharacter => CanThrowCarriedCharacterInternal();

    public bool CanPickupItemBecauseOfCharacterGrab()
    {
        return !IsGrabbingCharacter;
    }

    public bool TryFindPickupTarget(out NetworkObjectReference target)
    {
        target = default;

        if (characterGrabOnlyMode)
            return false;

        if (!CanProcessLocalOwnerGameplayRequest()) return false;
        if (!_ownerMode) return false;
        if (ownerCamera == null) return false;
        if (HasHeldItemBlockingCharacterGrab()) return false;
        if (!CanPickupItemBecauseOfCharacterGrab()) return false;

        Ray cameraRay = ownerCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
        if (TryFindPickupTargetFromRay(cameraRay, out target))
            return true;

        Vector3 fallbackOrigin = transform.position
            + Vector3.up * Mathf.Max(0f, pickupFallbackOriginHeight)
            + transform.forward * Mathf.Max(0f, pickupFallbackForwardOffset);
        Vector3 fallbackDirection = cameraRay.origin + cameraRay.direction * pickupDistance - fallbackOrigin;
        if (fallbackDirection.sqrMagnitude < 0.0001f)
            fallbackDirection = transform.forward;

        return TryFindPickupTargetFromRay(new Ray(fallbackOrigin, fallbackDirection.normalized), out target);
    }

    private bool TryFindPickupTargetFromRay(Ray ray, out NetworkObjectReference target)
    {
        target = default;

        if (!TryPickupRaycast(ray, out RaycastHit hit))
            return false;

        NetworkObject netObj = hit.collider.GetComponentInParent<NetworkObject>();
        if (netObj == null || !netObj.IsSpawned) return false;

        target = netObj;
        return true;
    }

    private bool TryPickupRaycast(Ray ray, out RaycastHit hit)
    {
        float radius = Mathf.Max(0f, pickupRayRadius);
        if (radius > 0f &&
            Physics.SphereCast(ray, radius, out hit, pickupDistance, pickupMask, QueryTriggerInteraction.Ignore))
        {
            return true;
        }

        return Physics.Raycast(ray, out hit, pickupDistance, pickupMask, QueryTriggerInteraction.Ignore);
    }

    public bool ServerTryPickup(NetworkObjectReference target)
    {
        if (characterGrabOnlyMode)
            return false;

        if (!CanProcessServerGameplayMutation()) return false;
        if (!CanPickupItemBecauseOfCharacterGrab()) return false;
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
        if (characterGrabOnlyMode)
            return;

        if (!CanProcessServerGameplayMutation()) return;
        if (!ResolveHeldCache()) return;

        NetworkObject netObj = _heldCache;
        bool usedLocalVisual = _useLocalHeldVisual;
        _dropInProgress = true;
        ClearExternalHeldItemPoseOverrideInternal("ServerTryDrop", true);

        _attached = false;
        _pendingAttach = false;
        _hasLastRequestedHeldPose = false;

        if (!_hasCachedMeta)
            CacheHeldMeta();

        RefreshDropAnchorPoseImmediately();
        Log($"[PlayerInteract][DropDebug] refreshedDropPoseBeforeSample=true localVisualReady={_localHeldVisualReady}");
        TryGetBestDropWorldPose(out Vector3 dropPos, out Quaternion dropRot, out string dropPoseSource);
        Log($"[PlayerInteract] Drop pose source={dropPoseSource} pos={FormatVector(dropPos)} rot={FormatVector(dropRot.eulerAngles)}");
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

    public bool TryFindCharacterGrabTarget(out PlayerStatusModule targetStatus)
    {
        targetStatus = null;

        if (!CanProcessLocalOwnerGameplayRequest()) return false;
        if (!_ownerMode) return false;
        if (!enableCharacterGrab) return false;
        if (ownerCamera == null) return false;
        if (HasHeldItemBlockingCharacterGrab()) return false;
        if (IsCharacterGrabBusy) return false;

        Ray cameraRay = ownerCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
        if (TryFindCharacterGrabTargetFromRay(cameraRay, out targetStatus))
            return true;

        Vector3 fallbackOrigin = GetCharacterGrabFallbackOrigin();
        Vector3 fallbackDirection = cameraRay.origin + cameraRay.direction * Mathf.Max(0f, characterGrabDistance) - fallbackOrigin;
        if (fallbackDirection.sqrMagnitude < 0.0001f)
            fallbackDirection = transform.forward;

        return TryFindCharacterGrabTargetFromRay(new Ray(fallbackOrigin, fallbackDirection.normalized), out targetStatus);
    }

    public void ServerTryStartCharacterGrab(PlayerStatusModule targetStatus)
    {
        if (!CanProcessServerGameplayMutation()) return;

        if (!CanStartCharacterGrab(targetStatus, out PlayerInteractModule targetInteract, out NetworkObject selfNetObj, out NetworkObject targetNetObj))
            return;

        float now = Time.time;
        float liftDuration = Mathf.Max(0f, liftChargeDuration);
        bool liftReadyImmediately = liftDuration <= 0f;

        _grabbedCharacter = targetNetObj;
        _grabbedCharacterInteract = targetInteract;
        _grabbedCharacterStatus = targetStatus;
        _grabbedByCharacter = default;
        _grabbedByInteract = null;
        _grabbedByStatus = null;
        _characterGrabStartedAt = now;
        _characterLiftReadyAt = now + liftDuration;
        _isCharacterLiftReady = liftReadyImmediately;
        _escapeTapCount = 0;
        _lastEscapeTapAt = 0f;

        targetInteract._grabbedCharacter = default;
        targetInteract._grabbedCharacterInteract = null;
        targetInteract._grabbedCharacterStatus = null;
        targetInteract._grabbedByCharacter = selfNetObj;
        targetInteract._grabbedByInteract = this;
        targetInteract._grabbedByStatus = ResolveOwnStatusModule();
        targetInteract._characterGrabStartedAt = now;
        targetInteract._characterLiftReadyAt = _characterLiftReadyAt;
        targetInteract._isCharacterLiftReady = liftReadyImmediately;
        targetInteract._escapeTapCount = 0;
        targetInteract._lastEscapeTapAt = 0f;

        ApplyCharacterGrabMoveSpeedMultiplier(this, GrabberMoveMultiplierKey, grabberMoveSpeedMultiplier);
        ApplyCharacterGrabMoveSpeedMultiplier(targetInteract, GrabbedMoveMultiplierKey, grabbedMoveSpeedMultiplier);

        CharacterGrabLog($"[PlayerInteract] Character grab started target={GetCharacterGrabDebugName(targetStatus, targetInteract)}");

        if (liftReadyImmediately)
        {
            CharacterGrabLog($"[PlayerInteract] Character grab lift ready target={GetCharacterGrabDebugName(targetStatus, targetInteract)}");
            ServerBeginCharacterCarryFollow("LiftReady");
        }
    }

    public void ServerReleaseCharacterGrab(string reason)
    {
        if (!IsServer) return;

        string releaseReason = string.IsNullOrWhiteSpace(reason) ? "release" : reason;
        if (IsInputDrivenCharacterGrabRelease(releaseReason) &&
            !CanProcessServerGameplayMutation())
        {
            return;
        }

        if (IsGrabbingCharacter)
        {
            PlayerInteractModule grabbedInteract = ResolveGrabbedCharacterInteract();
            string targetName = GetCharacterGrabDebugName(_grabbedCharacterStatus, grabbedInteract);
            bool escaped = IsEscapeReleaseReason(releaseReason);

            SafeClearCharacterGrabState(releaseReason, false);

            if (grabbedInteract != null && grabbedInteract != this)
                grabbedInteract.SafeClearCharacterGrabState(releaseReason, escaped);

            CharacterGrabLog($"[PlayerInteract] Character grab released reason={releaseReason} target={targetName}");
            return;
        }

        if (IsGrabbedByCharacter)
        {
            PlayerInteractModule grabberInteract = ResolveGrabbedByInteract();
            if (grabberInteract != null && grabberInteract != this)
            {
                grabberInteract.ServerReleaseCharacterGrab(releaseReason);
                return;
            }

            SafeClearCharacterGrabState(releaseReason, IsEscapeReleaseReason(releaseReason));
            CharacterGrabLog($"[PlayerInteract] Character grab released reason={releaseReason} target=<self>");
        }
    }

    public void ServerRegisterCharacterGrabEscapeTap(ulong senderClientId)
    {
        if (!CanProcessServerGameplayMutation()) return;

        PlayerInteractModule grabbedInteract = ResolveEscapeTargetInteract();
        if (grabbedInteract == null)
            return;

        if (!grabbedInteract.TryGetOwnerClientIdForCharacterGrab(out ulong grabbedOwnerClientId))
            return;

        if (grabbedOwnerClientId != senderClientId)
            return;

        float now = Time.time;
        float minInterval = Mathf.Max(0f, grabbedInteract.escapeTapMinInterval);
        if (grabbedInteract._lastEscapeTapAt > 0f && now - grabbedInteract._lastEscapeTapAt < minInterval)
            return;

        grabbedInteract._lastEscapeTapAt = now;
        grabbedInteract._escapeTapCount = Mathf.Max(0, grabbedInteract._escapeTapCount) + 1;

        int requiredCount = Mathf.Max(1, grabbedInteract.escapeTapRequiredCount);
        grabbedInteract.CharacterGrabLog($"[PlayerInteract] Character grab escape tap count={grabbedInteract._escapeTapCount}/{requiredCount}");

        if (grabbedInteract._escapeTapCount < requiredCount)
            return;

        PlayerInteractModule grabberInteract = grabbedInteract.ResolveGrabbedByInteract();
        if (grabberInteract != null && grabberInteract != grabbedInteract)
            grabberInteract.ServerReleaseCharacterGrab(CharacterGrabEscapeReason);
        else
            grabbedInteract.ServerReleaseCharacterGrab(CharacterGrabEscapeReason);
    }

    public void RequestCharacterGrabEscapeTap()
    {
        if (!CanProcessLocalOwnerGameplayRequest())
            return;

        if (IsServer)
        {
            ServerRegisterCharacterGrabEscapeTap(OwnerClientId);
            return;
        }

        RequestCharacterGrabEscapeTapServerRpc();
    }

    public void RequestReleaseCharacterGrab()
    {
        if (!CanProcessLocalOwnerGameplayRequest())
            return;

        if (IsServer)
        {
            if (IsGrabbingCharacter)
                ServerReleaseCharacterGrab("request-release");
            return;
        }

        RequestReleaseCharacterGrabServerRpc();
    }

    [ServerRpc]
    private void RequestCharacterGrabEscapeTapServerRpc(ServerRpcParams rpcParams = default)
    {
        if (rpcParams.Receive.SenderClientId != OwnerClientId)
            return;

        if (!CanProcessServerGameplayMutation())
            return;

        ServerRegisterCharacterGrabEscapeTap(OwnerClientId);
    }

    [ServerRpc]
    private void RequestReleaseCharacterGrabServerRpc(ServerRpcParams rpcParams = default)
    {
        if (rpcParams.Receive.SenderClientId != OwnerClientId)
            return;

        if (!CanProcessServerGameplayMutation())
            return;

        if (!IsGrabbingCharacter)
            return;

        ServerReleaseCharacterGrab("request-release");
    }

    public void RequestThrowCarriedCharacter()
    {
        if (!CanProcessLocalOwnerGameplayRequest())
            return;

        if (IsServer)
        {
            ServerTryThrowCarriedCharacter("Local");
            return;
        }

        RequestThrowCarriedCharacterServerRpc();
    }

    [ServerRpc]
    private void RequestThrowCarriedCharacterServerRpc(ServerRpcParams rpcParams = default)
    {
        if (rpcParams.Receive.SenderClientId != OwnerClientId)
            return;

        if (!CanProcessServerGameplayMutation())
            return;

        ServerTryThrowCarriedCharacter("Request");
    }

    public bool ServerTryThrowCarriedCharacter(string reason = "Throw")
    {
        if (!CanProcessServerGameplayMutation())
            return false;

        string throwReason = string.IsNullOrWhiteSpace(reason) ? "Throw" : reason;
        if (!CanThrowCarriedCharacterInternal())
        {
            CharacterGrabLog($"[PlayerInteract] Character throw failed reason={throwReason}");
            if (IsGrabbingCharacter && (!ResolveGrabbedCharacterRefs() || _grabbedCharacterStatus == null))
                ServerReleaseCharacterGrab("ThrowInvalidTarget");

            return false;
        }

        PlayerInteractModule targetInteract = ResolveGrabbedCharacterInteract();
        PlayerStatusModule targetStatus = _grabbedCharacterStatus;
        Transform targetRoot = _carriedCharacterRoot;
        if (targetRoot == null || targetStatus == null || targetInteract == null)
        {
            CharacterGrabLog($"[PlayerInteract] Character throw failed reason=ThrowInvalidTarget");
            ServerReleaseCharacterGrab("ThrowInvalidTarget");
            return false;
        }

        if (targetStatus.IsEliminated)
        {
            CharacterGrabLog($"[PlayerInteract] Character throw failed reason=TargetEliminated");
            ServerReleaseCharacterGrab("target-eliminated");
            return false;
        }

        if (characterGrabOnlyMode &&
            (!ServerIsCurrentPlayingParticipant() ||
             !targetInteract.ServerIsCurrentPlayingParticipant() ||
             IsMotorShellRecoveryBlockingCharacterGrab() ||
             targetInteract.IsMotorShellRecoveryBlockingCharacterGrab() ||
             HasHeldItemBlockingCharacterGrab() ||
             targetInteract.HasHeldItemBlockingCharacterGrab()))
        {
            CharacterGrabLog(
                $"[PlayerInteract] Character throw failed reason=ProductionStateInvalid");
            ServerReleaseCharacterGrab("ThrowProductionStateInvalid");
            return false;
        }

        Vector3 throwDirection = GetCharacterThrowDirection(targetRoot);
        Vector3 releasePosition = GetCharacterThrowReleasePosition(throwDirection);
        Quaternion releaseRotation = Quaternion.LookRotation(throwDirection, Vector3.up);
        Vector3 impulse = throwDirection * Mathf.Max(0f, characterThrowForwardImpulse) + Vector3.up * Mathf.Max(0f, characterThrowUpImpulse);
        string targetName = GetCharacterGrabDebugName(targetStatus, targetInteract);

        CharacterGrabLog($"[PlayerInteract] Character throw started target={targetName} reason={throwReason}");

        targetRoot.SetPositionAndRotation(releasePosition, releaseRotation);
        SafeClearCharacterGrabState("Throw", false);
        targetInteract.SafeClearCharacterGrabState("Throw", false);
        targetInteract._regrabImmuneUntil = Time.time + Mathf.Max(0f, characterThrowRegrabImmunitySeconds);

        CharacterGrabLog($"[PlayerInteract] Character throw knockback profile=Throw target={targetName}");
        bool appliedKnockback = targetStatus.ServerTryApplyThrowCombatKnockback(impulse, OwnerClientId);
        if (!appliedKnockback)
            CharacterGrabLog($"[PlayerInteract] Character throw knockback failed target={targetName}");

        CharacterGrabLog($"[PlayerInteract] Character throw impulse={impulse} applied={appliedKnockback}");
        CharacterGrabLog($"[PlayerInteract] Character throw completed target={targetName}");
        return appliedKnockback;
    }

    private void ServerTickCharacterGrab()
    {
        if (!IsCharacterGrabBusy)
            return;

        PlayerStatusModule ownStatus = ResolveOwnStatusModule();
        if (ownStatus != null && ownStatus.IsEliminated)
        {
            ServerReleaseCharacterGrab("eliminated");
            return;
        }

        if (characterGrabOnlyMode &&
            (!ServerIsCurrentPlayingParticipant() ||
             IsMotorShellRecoveryBlockingCharacterGrab() ||
             HasHeldItemBlockingCharacterGrab()))
        {
            ServerReleaseCharacterGrab("production-state-invalid");
            return;
        }

        if (IsGrabbingCharacter)
        {
            if (!ResolveGrabbedCharacterRefs() || !IsCharacterGrabLinkValidAsGrabber())
            {
                ServerReleaseCharacterGrab("invalid-target");
                return;
            }

            if (characterGrabOnlyMode &&
                (!_grabbedCharacterInteract
                    .ServerIsCurrentPlayingParticipant() ||
                  _grabbedCharacterInteract
                    .IsMotorShellRecoveryBlockingCharacterGrab() ||
                 _grabbedCharacterInteract
                    .HasHeldItemBlockingCharacterGrab()))
            {
                ServerReleaseCharacterGrab("target-production-state-invalid");
                return;
            }

            if (_grabbedCharacterStatus != null && _grabbedCharacterStatus.IsEliminated)
            {
                ServerReleaseCharacterGrab("target-eliminated");
                return;
            }

            float maxDuration = Mathf.Max(0f, maxCharacterGrabDuration);
            if (maxDuration > 0f && Time.time - _characterGrabStartedAt >= maxDuration)
            {
                ServerReleaseCharacterGrab("max-duration");
                return;
            }

            if (!_isCharacterLiftReady && Time.time >= _characterLiftReadyAt)
            {
                SetCharacterLiftReadyForPair(true);
                CharacterGrabLog($"[PlayerInteract] Character grab lift ready target={GetCharacterGrabDebugName(_grabbedCharacterStatus, _grabbedCharacterInteract)}");
                ServerBeginCharacterCarryFollow("LiftReady");
            }

            if (_isCharacterLiftReady)
                ServerUpdateCharacterCarryFollow();
        }

        if (IsGrabbedByCharacter)
        {
            if (!ResolveGrabbedByRefs() || !IsCharacterGrabLinkValidAsGrabbed())
            {
                SafeClearCharacterGrabState("invalid-grabber", false);
                CharacterGrabLog("[PlayerInteract] Character grab released reason=invalid-grabber target=<self>");
            }
        }
    }

    private bool TryFindCharacterGrabTargetFromRay(Ray ray, out PlayerStatusModule targetStatus)
    {
        targetStatus = null;

        float distance = Mathf.Max(0f, characterGrabDistance);
        if (distance <= 0f)
            return false;

        float radius = Mathf.Max(0f, characterGrabRadius);
        int layerMask = GetCharacterGrabLayerMask();
        RaycastHit[] hits = radius > 0f
            ? Physics.SphereCastAll(ray, radius, distance, layerMask, QueryTriggerInteraction.Collide)
            : Physics.RaycastAll(ray, distance, layerMask, QueryTriggerInteraction.Collide);

        float bestDistance = float.MaxValue;
        for (int i = 0; i < hits.Length; i++)
        {
            Collider hitCollider = hits[i].collider;
            if (hitCollider == null)
                continue;

            if (!TryResolvePlayerStatusFromCollider(hitCollider, out PlayerStatusModule candidateStatus))
                continue;

            if (!IsValidCharacterGrabCandidate(candidateStatus))
                continue;

            float hitDistance = Mathf.Max(0f, hits[i].distance);
            if (hitDistance >= bestDistance)
                continue;

            bestDistance = hitDistance;
            targetStatus = candidateStatus;
        }

        return targetStatus != null;
    }

    private bool CanStartCharacterGrab(
        PlayerStatusModule targetStatus,
        out PlayerInteractModule targetInteract,
        out NetworkObject selfNetObj,
        out NetworkObject targetNetObj)
    {
        targetInteract = null;
        selfNetObj = null;
        targetNetObj = null;

        if (!enableCharacterGrab)
            return false;

        if (HasHeldItemBlockingCharacterGrab())
            return false;

        if (IsCharacterGrabBusy)
            return false;

        if (!IsValidCharacterGrabCandidate(targetStatus))
            return false;

        targetInteract = ResolveInteractModule(targetStatus);
        if (targetInteract == null || targetInteract == this)
            return false;

        selfNetObj = ResolveRootNetworkObject(this);
        targetNetObj = ResolveRootNetworkObject(targetStatus);
        if (selfNetObj == null || targetNetObj == null)
            return false;

        if (!selfNetObj.IsSpawned || !targetNetObj.IsSpawned)
            return false;

        if (selfNetObj == targetNetObj)
            return false;

        if (characterGrabOnlyMode &&
            (!ServerIsCurrentPlayingParticipant() ||
             !targetInteract.ServerIsCurrentPlayingParticipant() ||
             IsMotorShellRecoveryBlockingCharacterGrab() ||
             targetInteract.IsMotorShellRecoveryBlockingCharacterGrab() ||
             targetInteract.HasHeldItemBlockingCharacterGrab()))
        {
            return false;
        }

        return IsCharacterGrabTargetInServerRange(targetStatus);
    }

    private bool ServerIsCurrentPlayingParticipant()
    {
        if (!characterGrabOnlyMode)
            return true;

        if (!IsServer || !IsSpawned)
            return false;

        if (_postItRoundManager == null)
            _postItRoundManager = FindFirstObjectByType<PostItRoundManager>();

        if (_postItRoundManager == null)
            return false;

        NetworkObject ownerRoot = ResolveRootNetworkObject(this);
        if (ownerRoot == null || !ownerRoot.IsSpawned)
            return false;

        if (_postItInventory == null ||
            _postItInventory.NetworkObject != ownerRoot)
        {
            _postItInventory =
                ownerRoot.GetComponentInChildren<PlayerPostItInventory>(true);
        }

        return _postItInventory != null &&
               _postItInventory.NetworkObject == ownerRoot &&
               _postItRoundManager.ServerIsCurrentPlayingParticipant(
                   _postItInventory);
    }

    private bool IsMotorShellRecoveryBlockingCharacterGrab()
    {
        if (!characterGrabOnlyMode)
            return false;

        NetworkObject ownerRoot = ResolveRootNetworkObject(this);
        if (ownerRoot == null)
            return true;

        if (_motorShellRecoveryAdapter == null ||
            _motorShellRecoveryAdapter.transform.root != ownerRoot.transform)
        {
            _motorShellRecoveryAdapter =
                ownerRoot.GetComponentInChildren<
                    HamsterMotorShellRagdollRecoveryAdapter>(true);
        }

        return _motorShellRecoveryAdapter == null ||
               _motorShellRecoveryAdapter.IsKnockedOrRecovering;
    }

    private bool HasHeldItemBlockingCharacterGrab()
    {
        if (!characterGrabOnlyMode)
            return HasHeldItem();

        NetworkObject ownerRoot = ResolveRootNetworkObject(this);
        if (ownerRoot == null)
            return true;

        if (_motorShellItemAdapter == null ||
            _motorShellItemAdapter.transform.root != ownerRoot.transform)
        {
            _motorShellItemAdapter =
                ownerRoot.GetComponentInChildren<
                    HamsterMotorShellItemAdapter>(true);
        }

        return _motorShellItemAdapter != null &&
               _motorShellItemAdapter.HasHeldItem;
    }

    private bool IsValidCharacterGrabCandidate(PlayerStatusModule candidateStatus)
    {
        if (candidateStatus == null)
            return false;

        if (candidateStatus.IsEliminated)
            return false;

        if (IsSelfCharacterStatus(candidateStatus))
            return false;

        PlayerInteractModule candidateInteract = ResolveInteractModule(candidateStatus);
        if (candidateInteract == null || candidateInteract == this)
            return false;

        if (candidateInteract.IsCharacterGrabBusy)
            return false;

        if (candidateInteract.IsCharacterRegrabImmune())
            return false;

        return true;
    }

    private bool IsCharacterGrabTargetInServerRange(PlayerStatusModule targetStatus)
    {
        float distance = Mathf.Max(0f, characterGrabDistance);
        if (distance <= 0f)
            return false;

        float radius = Mathf.Max(0.01f, characterGrabRadius);
        Vector3 origin = GetCharacterGrabServerOrigin();
        Vector3 direction = GetCharacterGrabServerDirection();

        Vector3 targetPoint = GetCharacterGrabTargetPoint(targetStatus);
        Vector3 toTarget = targetPoint - origin;
        float along = Vector3.Dot(toTarget, direction);
        if (along < -radius || along > distance + radius)
            return false;

        Vector3 closestPoint = origin + direction * Mathf.Clamp(along, 0f, distance);
        return (targetPoint - closestPoint).sqrMagnitude <= radius * radius;
    }

    private Vector3 GetCharacterGrabServerDirection()
    {
        if (ownerCamera != null &&
            TryNormalizeCharacterGrabPlanarDirection(
                ownerCamera.transform.forward,
                out Vector3 direction))
        {
            return direction;
        }

        HamsterFullRagdollMotor motor =
            GetComponentInChildren<HamsterFullRagdollMotor>(true);
        if (motor != null)
        {
            if (TryNormalizeCharacterGrabPlanarDirection(
                    motor.DesiredFacingDirection,
                    out direction))
            {
                return direction;
            }

            if (TryNormalizeCharacterGrabPlanarDirection(
                    motor.SmoothedMoveWorldDirection,
                    out direction))
            {
                return direction;
            }
        }

        if (TryNormalizeCharacterGrabPlanarDirection(
                transform.forward,
                out direction))
        {
            return direction;
        }

        return Vector3.forward;
    }

    private bool TryNormalizeCharacterGrabPlanarDirection(
        Vector3 source,
        out Vector3 direction)
    {
        direction = Vector3.ProjectOnPlane(source, Vector3.up);
        if (!IsFinite(direction.x) ||
            !IsFinite(direction.y) ||
            !IsFinite(direction.z) ||
            direction.sqrMagnitude < 0.0001f)
        {
            direction = Vector3.zero;
            return false;
        }

        direction.Normalize();
        return true;
    }

    private void CleanupCharacterGrabOnLifecycle(string reason)
    {
        if (IsServer)
            ServerReleaseCharacterGrab(reason);
        else
            SafeClearCharacterGrabState(reason, false);
    }

    private void SafeClearCharacterGrabState(string reason, bool applyEscapeRegrabImmunity)
    {
        ServerEndCharacterCarryFollow(reason);
        ClearCharacterGrabMoveSpeedMultipliers(this);

        _grabbedCharacter = default;
        _grabbedByCharacter = default;
        _grabbedCharacterInteract = null;
        _grabbedByInteract = null;
        _grabbedCharacterStatus = null;
        _grabbedByStatus = null;
        _characterGrabStartedAt = 0f;
        _characterLiftReadyAt = 0f;
        _isCharacterLiftReady = false;
        _escapeTapCount = 0;
        _lastEscapeTapAt = 0f;

        if (applyEscapeRegrabImmunity)
            _regrabImmuneUntil = Time.time + Mathf.Max(0f, escapeRegrabImmunitySeconds);
    }

    private void SetCharacterLiftReadyForPair(bool isReady)
    {
        _isCharacterLiftReady = isReady;

        PlayerInteractModule grabbedInteract = ResolveGrabbedCharacterInteract();
        if (grabbedInteract != null && grabbedInteract != this)
            grabbedInteract._isCharacterLiftReady = isReady;
    }

    private bool CanThrowCarriedCharacterInternal()
    {
        if (!enableCharacterThrow)
            return false;

        if (!IsGrabbingCharacter || !IsCharacterLiftReady || !_isCarryingCharacter)
            return false;

        if (!ResolveGrabbedCharacterRefs() || _grabbedCharacterStatus == null || _grabbedCharacterStatus.IsEliminated)
            return false;

        PlayerInteractModule grabbedInteract = ResolveGrabbedCharacterInteract();
        if (grabbedInteract == null)
            return false;

        return _carriedCharacterRoot != null;
    }

    private void ServerBeginCharacterCarryFollow(string reason)
    {
        if (!IsServer)
            return;

        if (_isCarryingCharacter || !enableCharacterCarryFollow)
            return;

        if (!IsGrabbingCharacter)
            return;

        if (!ResolveGrabbedCharacterRefs() || _grabbedCharacterStatus == null)
        {
            ServerReleaseCharacterGrab("invalid-target");
            return;
        }

        if (_grabbedCharacterStatus.IsEliminated)
        {
            ServerReleaseCharacterGrab("target-eliminated");
            return;
        }

        if (!TryResolveCharacterCarryTarget(out Transform targetRoot, out CharacterController targetController, out Rigidbody targetRigidbody))
        {
            ServerReleaseCharacterGrab("invalid-target");
            return;
        }

        _carriedCharacterRoot = targetRoot;
        _carriedCharacterController = targetController;
        _carriedCharacterRigidbody = targetRigidbody;
        _carriedCharacterControllerWasEnabled = targetController != null && targetController.enabled;
        _carriedCharacterRigidbodyWasKinematic = targetRigidbody != null && targetRigidbody.isKinematic;
        _carriedCharacterRigidbodyUseGravity = targetRigidbody != null && targetRigidbody.useGravity;
        _isCarryingCharacter = true;

        if (characterCarryDisableTargetController && targetController != null && targetController.enabled)
            targetController.enabled = false;

        if (characterCarrySetTargetRigidbodyKinematic && targetRigidbody != null)
        {
            targetRigidbody.isKinematic = true;
            targetRigidbody.useGravity = false;
        }

        ServerApplyCharacterCarryPose(true);
        CharacterGrabLog($"[PlayerInteract] Character carry started target={GetCharacterGrabDebugName(_grabbedCharacterStatus, _grabbedCharacterInteract)} reason={reason ?? "<null>"}");
    }

    private void ServerUpdateCharacterCarryFollow()
    {
        if (!IsServer || !enableCharacterCarryFollow)
            return;

        if (!_isCarryingCharacter)
        {
            ServerBeginCharacterCarryFollow("LiftReady");
            return;
        }

        if (!ResolveGrabbedCharacterRefs() || _grabbedCharacterStatus == null || _grabbedCharacterStatus.IsEliminated)
        {
            ServerReleaseCharacterGrab(_grabbedCharacterStatus != null && _grabbedCharacterStatus.IsEliminated ? "target-eliminated" : "invalid-target");
            return;
        }

        if (_carriedCharacterRoot == null)
        {
            ServerReleaseCharacterGrab("invalid-target");
            return;
        }

        float maxDistance = Mathf.Max(0f, characterCarryMaxDistance);
        if (maxDistance > 0f)
        {
            float distanceSqr = (_carriedCharacterRoot.position - transform.position).sqrMagnitude;
            if (distanceSqr > maxDistance * maxDistance)
            {
                ServerReleaseCharacterGrab("carry-distance");
                return;
            }
        }

        ServerApplyCharacterCarryPose(false);
    }

    private void ServerEndCharacterCarryFollow(string reason)
    {
        if (!_isCarryingCharacter)
        {
            ClearCharacterCarryRuntimeCache();
            return;
        }

        Transform targetRoot = _carriedCharacterRoot;
        CharacterController targetController = _carriedCharacterController;
        Rigidbody targetRigidbody = _carriedCharacterRigidbody;

        if (targetRoot != null)
        {
            float releaseYOffset = Mathf.Max(0f, characterCarryGroundReleaseYOffset);
            if (releaseYOffset > 0f)
                targetRoot.position += Vector3.up * releaseYOffset;
        }

        if (targetRigidbody != null && characterCarrySetTargetRigidbodyKinematic)
        {
            targetRigidbody.useGravity = _carriedCharacterRigidbodyUseGravity;
            targetRigidbody.isKinematic = _carriedCharacterRigidbodyWasKinematic;
        }

        if (targetController != null && characterCarryDisableTargetController)
            targetController.enabled = _carriedCharacterControllerWasEnabled;

        CharacterGrabLog($"[PlayerInteract] Character carry released reason={reason ?? "<null>"}");
        CharacterGrabLog("[PlayerInteract] Character carry restored controller/rigidbody");
        ClearCharacterCarryRuntimeCache();
    }

    private bool TryResolveCharacterCarryTarget(out Transform targetRoot, out CharacterController targetController, out Rigidbody targetRigidbody)
    {
        targetRoot = null;
        targetController = null;
        targetRigidbody = null;

        if (!TryGetNetworkObjectSafe(_grabbedCharacter, out NetworkObject targetNetObj) || targetNetObj == null || !targetNetObj.IsSpawned)
            return false;

        targetRoot = targetNetObj.transform;

        if (_grabbedCharacterStatus != null)
        {
            targetController = _grabbedCharacterStatus.GetComponentInParent<CharacterController>();
            targetRigidbody = _grabbedCharacterStatus.GetComponentInParent<Rigidbody>();
        }

        if (targetController == null)
            targetController = targetNetObj.GetComponentInChildren<CharacterController>(true);

        if (targetRigidbody == null)
            targetRigidbody = targetNetObj.GetComponentInChildren<Rigidbody>(true);

        return targetRoot != null;
    }

    private void ServerApplyCharacterCarryPose(bool immediate)
    {
        if (_carriedCharacterRoot == null)
            return;

        Vector3 targetPosition = GetCharacterCarryAnchorPosition();
        Quaternion targetRotation = GetCharacterCarryAnchorRotation();
        float followLerp = Mathf.Max(0f, characterCarryFollowLerp);

        if (!immediate && followLerp > 0f)
        {
            float t = Mathf.Clamp01(followLerp * Time.deltaTime);
            targetPosition = Vector3.Lerp(_carriedCharacterRoot.position, targetPosition, t);
            targetRotation = Quaternion.Slerp(_carriedCharacterRoot.rotation, targetRotation, t);
        }

        _carriedCharacterRoot.SetPositionAndRotation(targetPosition, targetRotation);
    }

    private Vector3 GetCharacterCarryAnchorPosition()
    {
        Vector3 forward = GetSafeCharacterCarryForward();
        Vector3 right = Vector3.Cross(Vector3.up, forward);
        if (right.sqrMagnitude < 0.0001f)
            right = transform.right;

        return transform.position
            + forward.normalized * characterCarryForwardOffset
            + right.normalized * characterCarryRightOffset
            + Vector3.up * characterCarryUpOffset;
    }

    private Quaternion GetCharacterCarryAnchorRotation()
    {
        if (!characterCarryFaceGrabberForward && _carriedCharacterRoot != null)
            return _carriedCharacterRoot.rotation;

        Vector3 forward = GetSafeCharacterCarryForward();
        return Quaternion.LookRotation(forward, Vector3.up);
    }

    private Vector3 GetCharacterThrowDirection(Transform targetRoot)
    {
        Vector3 direction = characterThrowUseCarryAnchorDirection
            ? GetSafeCharacterCarryForward()
            : targetRoot != null ? targetRoot.forward : Vector3.zero;

        if (direction.sqrMagnitude >= 0.0001f)
            return direction.normalized;

        direction = transform.forward;
        if (direction.sqrMagnitude >= 0.0001f)
            return direction.normalized;

        direction = transform.rotation * Vector3.forward;
        return direction.sqrMagnitude >= 0.0001f ? direction.normalized : Vector3.forward;
    }

    private Vector3 GetCharacterThrowReleasePosition(Vector3 throwDirection)
    {
        Vector3 direction = throwDirection.sqrMagnitude >= 0.0001f ? throwDirection.normalized : GetSafeCharacterCarryForward();
        return transform.position
            + direction * Mathf.Max(0f, characterThrowReleaseForwardOffset)
            + Vector3.up * Mathf.Max(0f, characterThrowReleaseUpOffset);
    }

    private Vector3 GetSafeCharacterCarryForward()
    {
        Vector3 forward = transform.forward;
        if (forward.sqrMagnitude >= 0.0001f)
            return forward.normalized;

        forward = transform.rotation * Vector3.forward;
        if (forward.sqrMagnitude >= 0.0001f)
            return forward.normalized;

        return Vector3.forward;
    }

    private void ClearCharacterCarryRuntimeCache()
    {
        _isCarryingCharacter = false;
        _carriedCharacterController = null;
        _carriedCharacterRigidbody = null;
        _carriedCharacterControllerWasEnabled = false;
        _carriedCharacterRigidbodyWasKinematic = false;
        _carriedCharacterRigidbodyUseGravity = false;
        _carriedCharacterRoot = null;
    }

    private PlayerInteractModule ResolveEscapeTargetInteract()
    {
        if (IsGrabbedByCharacter)
            return this;

        if (IsGrabbingCharacter)
            return ResolveGrabbedCharacterInteract();

        return null;
    }

    private PlayerInteractModule ResolveGrabbedCharacterInteract()
    {
        if (_grabbedCharacterInteract != null)
            return _grabbedCharacterInteract;

        if (!TryGetNetworkObjectSafe(_grabbedCharacter, out NetworkObject netObj))
            return null;

        _grabbedCharacterInteract = ResolveInteractModule(netObj);
        _grabbedCharacterStatus = ResolveStatusModule(netObj);
        return _grabbedCharacterInteract;
    }

    private PlayerInteractModule ResolveGrabbedByInteract()
    {
        if (_grabbedByInteract != null)
            return _grabbedByInteract;

        if (!TryGetNetworkObjectSafe(_grabbedByCharacter, out NetworkObject netObj))
            return null;

        _grabbedByInteract = ResolveInteractModule(netObj);
        _grabbedByStatus = ResolveStatusModule(netObj);
        return _grabbedByInteract;
    }

    private bool ResolveGrabbedCharacterRefs()
    {
        PlayerInteractModule interact = ResolveGrabbedCharacterInteract();
        if (interact == null)
            return false;

        if (_grabbedCharacterStatus == null)
            _grabbedCharacterStatus = ResolveStatusModule(interact);

        return _grabbedCharacterStatus != null;
    }

    private bool ResolveGrabbedByRefs()
    {
        PlayerInteractModule interact = ResolveGrabbedByInteract();
        if (interact == null)
            return false;

        if (_grabbedByStatus == null)
            _grabbedByStatus = ResolveStatusModule(interact);

        return _grabbedByStatus != null;
    }

    private bool IsCharacterGrabLinkValidAsGrabber()
    {
        PlayerInteractModule grabbedInteract = ResolveGrabbedCharacterInteract();
        return grabbedInteract != null && grabbedInteract._grabbedByInteract == this;
    }

    private bool IsCharacterGrabLinkValidAsGrabbed()
    {
        PlayerInteractModule grabberInteract = ResolveGrabbedByInteract();
        return grabberInteract != null && grabberInteract._grabbedCharacterInteract == this;
    }

    private bool IsCharacterRegrabImmune()
    {
        return Time.time < _regrabImmuneUntil;
    }

    private bool TryGetOwnerClientIdForCharacterGrab(out ulong clientId)
    {
        clientId = ulong.MaxValue;

        NetworkObject ownerObject = ResolveRootNetworkObject(this);
        if (ownerObject == null)
            ownerObject = NetworkObject;

        if (ownerObject == null)
            return false;

        clientId = ownerObject.OwnerClientId;
        return clientId != ulong.MaxValue;
    }

    private static void ApplyCharacterGrabMoveSpeedMultiplier(PlayerInteractModule interact, string sourceKey, float multiplier)
    {
        PlayerLocomotionModule locomotion = ResolveLocomotionModule(interact);
        if (locomotion == null)
            return;

        locomotion.SetExternalMoveSpeedMultiplier(sourceKey, Mathf.Max(0f, multiplier));
    }

    private static void ClearCharacterGrabMoveSpeedMultipliers(PlayerInteractModule interact)
    {
        PlayerLocomotionModule locomotion = ResolveLocomotionModule(interact);
        if (locomotion == null)
            return;

        locomotion.ClearExternalMoveSpeedMultiplier(GrabberMoveMultiplierKey);
        locomotion.ClearExternalMoveSpeedMultiplier(GrabbedMoveMultiplierKey);
    }

    private Vector3 GetCharacterGrabFallbackOrigin()
    {
        return transform.position
            + Vector3.up * Mathf.Max(0f, pickupFallbackOriginHeight)
            + transform.forward * Mathf.Max(0f, pickupFallbackForwardOffset);
    }

    private Vector3 GetCharacterGrabServerOrigin()
    {
        if (_cc != null)
            return _cc.transform.TransformPoint(_cc.center);

        return transform.position + Vector3.up * Mathf.Max(0f, pickupFallbackOriginHeight);
    }

    private Vector3 GetCharacterGrabTargetPoint(PlayerStatusModule targetStatus)
    {
        if (targetStatus == null)
            return Vector3.zero;

        CharacterController targetController = targetStatus.GetComponentInParent<CharacterController>();
        if (targetController != null)
            return targetController.transform.TransformPoint(targetController.center);

        NetworkObject targetNetObj = ResolveRootNetworkObject(targetStatus);
        if (targetNetObj != null)
            return targetNetObj.transform.position + Vector3.up * Mathf.Max(0f, pickupFallbackOriginHeight);

        return targetStatus.transform.position;
    }

    private bool IsSelfCharacterStatus(PlayerStatusModule candidateStatus)
    {
        if (candidateStatus == null)
            return false;

        PlayerStatusModule ownStatus = ResolveOwnStatusModule();
        if (ownStatus != null && candidateStatus == ownStatus)
            return true;

        NetworkObject selfNetObj = ResolveRootNetworkObject(this);
        NetworkObject candidateNetObj = ResolveRootNetworkObject(candidateStatus);
        return selfNetObj != null && candidateNetObj != null && selfNetObj == candidateNetObj;
    }

    private static bool TryResolvePlayerStatusFromCollider(Collider source, out PlayerStatusModule status)
    {
        status = null;

        if (source == null)
            return false;

        status = source.GetComponentInParent<PlayerStatusModule>();
        if (status != null)
            return true;

        if (source.attachedRigidbody != null)
        {
            status = source.attachedRigidbody.GetComponentInParent<PlayerStatusModule>();
            if (status != null)
                return true;
        }

        Transform root = source.transform.root;
        if (root != null)
            status = root.GetComponentInChildren<PlayerStatusModule>(true);

        return status != null;
    }

    private bool CanProcessLocalOwnerGameplayRequest()
    {
        if (!IsSpawned || !IsOwner)
            return false;

        NetworkManager networkManager = NetworkManager;
        if (networkManager == null ||
            !networkManager.IsListening ||
            networkManager.LocalClientId != OwnerClientId)
        {
            return false;
        }

        return CanProcessGameplayMutation();
    }

    private bool CanProcessServerGameplayMutation()
    {
        return IsServer && IsSpawned && CanProcessGameplayMutation();
    }

    private bool CanProcessGameplayMutation()
    {
        if (!TryGetGameplayGateState(out GameStateManager.GameState state) ||
            state != GameStateManager.GameState.Playing)
        {
            return false;
        }

        return TryResolveGameplayGateOwnerStatus(out PlayerStatusModule status) &&
               !status.IsEliminated;
    }

    private bool TryGetGameplayGateState(out GameStateManager.GameState state)
    {
        if (_gameplayGateStateManager == null ||
            !_gameplayGateStateManager.IsSpawned)
        {
            _gameplayGateStateManager = null;
            if (Time.unscaledTime < _nextGameplayGateStateResolveTime)
            {
                state = default;
                return false;
            }

            _nextGameplayGateStateResolveTime =
                Time.unscaledTime + GameplayGateResolveRetryInterval;
            _gameplayGateStateManager = FindFirstObjectByType<GameStateManager>();
        }

        if (_gameplayGateStateManager == null ||
            !_gameplayGateStateManager.IsSpawned)
        {
            state = default;
            return false;
        }

        state = _gameplayGateStateManager.GetState();
        return true;
    }

    private bool TryResolveGameplayGateOwnerStatus(out PlayerStatusModule status)
    {
        status = null;
        NetworkObject ownerRoot = NetworkObject;
        if (ownerRoot == null || !ownerRoot.IsSpawned)
            return false;

        if (_gameplayGateOwnerRoot != ownerRoot)
        {
            _gameplayGateOwnerRoot = ownerRoot;
            _gameplayGateOwnerStatus = null;
            _nextGameplayGateStatusResolveTime = float.NegativeInfinity;
        }

        if (_gameplayGateOwnerStatus != null &&
            _gameplayGateOwnerStatus.IsSpawned &&
            _gameplayGateOwnerStatus.GetComponentInParent<NetworkObject>() ==
            ownerRoot)
        {
            status = _gameplayGateOwnerStatus;
            return true;
        }

        _gameplayGateOwnerStatus = null;
        if (Time.unscaledTime < _nextGameplayGateStatusResolveTime)
            return false;

        _nextGameplayGateStatusResolveTime =
            Time.unscaledTime + GameplayGateResolveRetryInterval;
        PlayerStatusModule candidate =
            ownerRoot.GetComponent<PlayerStatusModule>();
        if (candidate == null)
            candidate = ownerRoot.GetComponentInChildren<PlayerStatusModule>(true);

        if (candidate == null ||
            !candidate.IsSpawned ||
            candidate.GetComponentInParent<NetworkObject>() != ownerRoot)
        {
            return false;
        }

        _gameplayGateOwnerStatus = candidate;
        status = candidate;
        return true;
    }

    private static bool IsInputDrivenCharacterGrabRelease(string reason)
    {
        return reason == "DropInput" || reason == "request-release";
    }

    private void ResetGameplayGateCache()
    {
        _gameplayGateStateManager = null;
        _gameplayGateOwnerStatus = null;
        _gameplayGateOwnerRoot = null;
        _nextGameplayGateStateResolveTime = float.NegativeInfinity;
        _nextGameplayGateStatusResolveTime = float.NegativeInfinity;
    }

    private PlayerStatusModule ResolveOwnStatusModule()
    {
        return ResolveStatusModule(this);
    }

    private static PlayerStatusModule ResolveStatusModule(PlayerInteractModule interact)
    {
        if (interact == null)
            return null;

        PlayerHub hub = interact.GetComponentInParent<PlayerHub>();
        if (hub != null)
        {
            PlayerStatusModule status = hub.GetComponentInChildren<PlayerStatusModule>(true);
            if (status != null)
                return status;
        }

        PlayerStatusModule parentStatus = interact.GetComponentInParent<PlayerStatusModule>();
        if (parentStatus != null)
            return parentStatus;

        return interact.GetComponentInChildren<PlayerStatusModule>(true);
    }

    private static PlayerStatusModule ResolveStatusModule(NetworkObject netObj)
    {
        if (netObj == null)
            return null;

        PlayerStatusModule status = netObj.GetComponentInChildren<PlayerStatusModule>(true);
        if (status != null)
            return status;

        return netObj.GetComponentInParent<PlayerStatusModule>();
    }

    private static PlayerInteractModule ResolveInteractModule(PlayerStatusModule status)
    {
        if (status == null)
            return null;

        PlayerHub hub = status.GetComponentInParent<PlayerHub>();
        if (hub != null)
        {
            PlayerInteractModule interact = hub.GetComponentInChildren<PlayerInteractModule>(true);
            if (interact != null)
                return interact;
        }

        PlayerInteractModule parentInteract = status.GetComponentInParent<PlayerInteractModule>();
        if (parentInteract != null)
            return parentInteract;

        return status.GetComponentInChildren<PlayerInteractModule>(true);
    }

    private static PlayerInteractModule ResolveInteractModule(NetworkObject netObj)
    {
        if (netObj == null)
            return null;

        PlayerInteractModule interact = netObj.GetComponentInChildren<PlayerInteractModule>(true);
        if (interact != null)
            return interact;

        return netObj.GetComponentInParent<PlayerInteractModule>();
    }

    private static PlayerLocomotionModule ResolveLocomotionModule(PlayerInteractModule interact)
    {
        if (interact == null)
            return null;

        PlayerHub hub = interact.GetComponentInParent<PlayerHub>();
        if (hub != null)
        {
            PlayerLocomotionModule locomotion = hub.GetComponentInChildren<PlayerLocomotionModule>(true);
            if (locomotion != null)
                return locomotion;
        }

        PlayerLocomotionModule parentLocomotion = interact.GetComponentInParent<PlayerLocomotionModule>();
        if (parentLocomotion != null)
            return parentLocomotion;

        return interact.GetComponentInChildren<PlayerLocomotionModule>(true);
    }

    private static NetworkObject ResolveRootNetworkObject(Component component)
    {
        if (component == null)
            return null;

        NetworkObject rootNetObj = component.GetComponentInParent<NetworkObject>();
        if (rootNetObj != null)
            return rootNetObj;

        NetworkBehaviour behaviour = component as NetworkBehaviour;
        return behaviour != null ? behaviour.NetworkObject : null;
    }

    private int GetCharacterGrabLayerMask()
    {
        return characterGrabMask.value != 0 ? characterGrabMask.value : Physics.DefaultRaycastLayers;
    }

    private bool HasCharacterGrabReference(NetworkObjectReference reference, PlayerInteractModule cachedInteract)
    {
        if (cachedInteract != null)
            return true;

        return TryGetNetworkObjectSafe(reference, out NetworkObject netObj) && netObj != null && netObj.IsSpawned;
    }

    private static bool IsEscapeReleaseReason(string reason)
    {
        return string.Equals(reason, CharacterGrabEscapeReason, System.StringComparison.OrdinalIgnoreCase);
    }

    private string GetCharacterGrabDebugName(PlayerStatusModule status, PlayerInteractModule interact)
    {
        NetworkObject netObj = status != null ? ResolveRootNetworkObject(status) : ResolveRootNetworkObject(interact);
        if (netObj != null)
            return netObj.name;

        if (status != null)
            return status.name;

        if (interact != null)
            return interact.name;

        return "<null>";
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

        if (_isClearingHeldRuntimeCache || _isClearingExternalHeldItemPose)
            return false;

        NetworkManager networkManager = NetworkManager.Singleton;
        if (networkManager == null || networkManager.SpawnManager == null)
        {
            ClearHeldRuntimeCache();
            _hasLastRequestedHeldPose = false;
            return false;
        }

        if (TryGetNetworkObjectSafe(_heldItem.Value, out NetworkObject netObj))
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
        Quaternion localRotation = Quaternion.Euler(localEuler);
        if (_hasExternalHeldItemPoseOverride)
            localRotation *= Quaternion.Euler(_externalHeldItemPoseEulerOffset);

        pos = handSocket.TransformPoint(localPos);
        rot = handSocket.rotation * localRotation;
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
        Quaternion expectedWorldRot = handSocket.rotation * GetHeldItemLocalRotation();

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

        Transform handSocket = GetRightWeaponSocket();
        if (handSocket == null || !visualRoot.IsChildOf(handSocket))
            return false;

        if (!IsLocalHeldVisualSettledAgainstSocket(visualRoot))
            return false;

        pos = visualRoot.position;
        rot = visualRoot.rotation;
        _lastDropAnchorSourceDetail = "(Root)";
        return true;
    }

    private bool TryGetBestDropWorldPose(out Vector3 dropPos, out Quaternion dropRot, out string source)
    {
        _lastDropAnchorSourceDetail = string.Empty;

        if (TryGetLocalHeldVisualWorldPose(out dropPos, out dropRot))
        {
            _lastDropAnchorSource = "LocalHeldVisual";
            _lastAppliedDropForwardOffset = 0f;
            _lastAppliedDropUpOffset = 0f;
            source = _lastDropAnchorSource + _lastDropAnchorSourceDetail;
            return true;
        }

        if (TryGetHandWorldPose(out dropPos, out dropRot))
        {
            _lastDropAnchorSource = "HandWorldPose";
            _lastDropAnchorSourceDetail = string.Empty;
            _lastAppliedDropForwardOffset = 0f;
            _lastAppliedDropUpOffset = 0f;
            source = _lastDropAnchorSource;
            return true;
        }

        GetDropAnchorWorldPose(out dropPos, out dropRot);
        source = _lastDropAnchorSource == "RootFallback"
            ? _lastDropAnchorSource
            : "DropAnchorFallback";

        if (!string.IsNullOrEmpty(_lastDropAnchorSourceDetail))
            source += _lastDropAnchorSourceDetail;

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
        if (!TryGetNetworkObjectSafe(previousValue, out NetworkObject prevNetObj))
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
        _isClearingHeldRuntimeCache = true;
        try
        {
            ClearExternalHeldItemPoseOverrideInternal("ClearHeldRuntimeCache", false);
            _heldCache = null;
            _heldPickup = null;
            _heldItemData = null;
            _heldWeaponData = null;
            _hasLastRequestedHeldPose = false;
        }
        finally
        {
            _isClearingHeldRuntimeCache = false;
        }
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
        _localHeldVisualInstance.transform.localRotation = GetHeldItemLocalRotation();
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

    private Quaternion GetHeldItemLocalRotation()
    {
        Quaternion baseRotation = Quaternion.Euler(_cachedLocalEuler);
        if (!_hasExternalHeldItemPoseOverride)
            return baseRotation;

        return baseRotation * Quaternion.Euler(_externalHeldItemPoseEulerOffset);
    }

    private void ForceRefreshHeldItemPoseForExternalOverride()
    {
        if (_isRefreshingExternalHeldItemPose || _isClearingHeldRuntimeCache || _isClearingExternalHeldItemPose)
            return;

        _isRefreshingExternalHeldItemPose = true;
        try
        {
            if (TryForceApplyExternalPoseToLocalHeldVisual(true))
                return;

            if (TryForceApplyExternalPoseToWorldHeldItem(true))
                return;

            if ((_hasExternalHeldItemPoseOverride || _localHeldVisualInstance != null || (_heldCache != null && _heldCache.IsSpawned)) && enableDebugLogs)
                Debug.LogWarning("[PlayerInteract] External held item pose target is null.", this);
        }
        finally
        {
            _isRefreshingExternalHeldItemPose = false;
        }
    }

    private bool TryForceApplyExternalPoseToLocalHeldVisual(bool logOnce)
    {
        if (_localHeldVisualInstance == null)
            return false;

        Transform visualTransform = _localHeldVisualInstance.transform;
        if (visualTransform == null)
            return false;

        Vector3 beforeEuler = visualTransform.localEulerAngles;
        visualTransform.localPosition = _cachedLocalPos;
        visualTransform.localRotation = GetHeldItemLocalRotation();
        visualTransform.localScale = SanitizeVisualScale(_cachedLocalScale);
        _localHeldVisualReady = true;

        if (logOnce && enableDebugLogs)
        {
            Vector3 afterEuler = visualTransform.localEulerAngles;
            if (_hasExternalHeldItemPoseOverride)
            {
                Debug.Log($"[PlayerInteract] External held item pose apply target={visualTransform.name} before={FormatEuler(beforeEuler)} after={FormatEuler(afterEuler)} offset={FormatEuler(_externalHeldItemPoseEulerOffset)} source={_externalHeldItemPoseSource ?? "<null>"}", this);
            }
            else
            {
                Debug.Log($"[PlayerInteract] External held item pose restore target={visualTransform.name} before={FormatEuler(beforeEuler)} after={FormatEuler(afterEuler)} source={_externalHeldItemPoseSource ?? "<null>"}", this);
            }
        }

        return true;
    }

    private bool TryForceApplyExternalPoseToWorldHeldItem(bool logOnce)
    {
        if (_heldCache == null || !_heldCache.IsSpawned)
            return false;

        if (!ShouldMaintainWorldAttachFallback())
            return false;

        Transform worldTransform = _heldCache != null ? _heldCache.transform : null;
        if (worldTransform == null)
            return false;

        Vector3 beforeEuler = worldTransform.eulerAngles;
        if (!TryApplyHeldWorldPose(true))
        {
            if (!TryGetHandWorldPose(out Vector3 handPos, out Quaternion handRot))
                return false;

            Vector3 heldWorldScale = GetHeldWorldScale();
            ApplyWorldItemPose(_heldCache, handPos, handRot, heldWorldScale, true);
            _lastRequestedHeldWorldPosition = handPos;
            _lastRequestedHeldWorldRotation = handRot;
            _lastRequestedHeldWorldScale = heldWorldScale;
            _hasLastRequestedHeldPose = true;
        }

        if (logOnce && enableDebugLogs)
        {
            Vector3 afterEuler = worldTransform.eulerAngles;
            if (_hasExternalHeldItemPoseOverride)
            {
                Debug.Log($"[PlayerInteract] External held item pose apply target={worldTransform.name} before={FormatEuler(beforeEuler)} after={FormatEuler(afterEuler)} offset={FormatEuler(_externalHeldItemPoseEulerOffset)} source={_externalHeldItemPoseSource ?? "<null>"}", this);
            }
            else
            {
                Debug.Log($"[PlayerInteract] External held item pose restore target={worldTransform.name} before={FormatEuler(beforeEuler)} after={FormatEuler(afterEuler)} source={_externalHeldItemPoseSource ?? "<null>"}", this);
            }
        }

        return true;
    }

    private static string FormatEuler(Vector3 euler)
    {
        return $"({euler.x:0.00}, {euler.y:0.00}, {euler.z:0.00})";
    }

    private static string FormatVector(Vector3 value)
    {
        return $"({value.x:0.00}, {value.y:0.00}, {value.z:0.00})";
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
        ClearExternalHeldItemPoseOverrideInternal("DestroyLocalHeldVisual", false);
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

    private void ClearExternalHeldItemPoseOverrideInternal(string source, bool refreshExistingTarget)
    {
        if (_isClearingExternalHeldItemPose)
            return;

        bool hadOverride = _hasExternalHeldItemPoseOverride;
        bool shouldRefreshExistingTarget = hadOverride && refreshExistingTarget && HasActiveHeldItemTargetForExternalPose();
        string sourceForLog = source ?? _externalHeldItemPoseSource;

        _isClearingExternalHeldItemPose = true;
        try
        {
            _externalHeldItemPoseSource = sourceForLog;
            _hasExternalHeldItemPoseOverride = false;
            _externalHeldItemPoseEulerOffset = Vector3.zero;
        }
        finally
        {
            _isClearingExternalHeldItemPose = false;
        }

        if (shouldRefreshExistingTarget)
            ForceRefreshHeldItemPoseForExternalOverride();

        _externalHeldItemPoseSource = null;

        if (hadOverride)
            Log($"[PlayerInteract] External held item pose override cleared source={sourceForLog ?? "<null>"}");
    }

    private bool HasActiveHeldItemTargetForExternalPose()
    {
        if (_localHeldVisualInstance != null)
            return true;

        return _heldCache != null && _heldCache.IsSpawned && ShouldMaintainWorldAttachFallback();
    }

    private static bool TryGetNetworkObjectSafe(NetworkObjectReference reference, out NetworkObject networkObject)
    {
        networkObject = null;

        NetworkManager networkManager = NetworkManager.Singleton;
        if (networkManager == null || networkManager.SpawnManager == null)
            return false;

        return reference.TryGet(out networkObject) && networkObject != null;
    }

    private void Log(string message)
    {
        if (!enableDebugLogs) return;
        Debug.Log(message);
    }

    private void CharacterGrabLog(string message)
    {
        if (!enableDebugLogs && !characterGrabDebugLogs) return;
        Debug.Log(message, this);
    }

    private void TryTriggerPickupExpression()
    {
        AutoFindRefs();
        if (_faceExpressionController == null)
            return;

        _faceExpressionController.Face_4_HoldSeconds(PickupExpressionHoldSeconds);
        Log("[PlayerInteract] Pickup expression triggered.");
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        AutoFindRefs();
    }
#endif
}
