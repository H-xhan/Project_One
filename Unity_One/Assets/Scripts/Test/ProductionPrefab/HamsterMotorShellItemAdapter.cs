using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

[DefaultExecutionOrder(100)]
public sealed class HamsterMotorShellItemAdapter : MonoBehaviour
{
    private const string TargetRootName = "Hamster_JointFreeMotorShell_MainScenes";
    private const string CameraRootFieldName = "cameraRoot";
    private const string PlayerCameraName = "PlayerCamera";
    private const string VisualPreviewRootName = "VisualPreviewRoot";
    private const string RuntimeRightHandSocketName = "MotorShellRightHandHeldSocket";
    private const float HeldPoseLogInterval = 0.5f;
    private const int ThrowOverlapBufferSize = 64;
    private const float MinThrowCollisionGraceDuration = 0.01f;
    private const float MinThrowReleaseStep = 0.02f;

    private static readonly string[] WeaponPointSocketNameCandidates =
    {
        "WeaponPoint_R"
    };

    private static readonly string[] RightWeaponSocketNameCandidates =
    {
        "RightWeaponSocket"
    };

    private static readonly string[] RightHandEndSocketNameCandidates =
    {
        "hand.R_end",
        "hand.R.End",
        "RightHandEnd",
        "RightHand_end",
        "mixamorig:RightHand_end",
        "Bip001 R Hand_end"
    };

    private static readonly string[] RightHandNameCandidates =
    {
        "RightHand",
        "Right Hand",
        "Hand_R",
        "R_Hand",
        "hand.R",
        "mixamorig:RightHand",
        "Bip001 R Hand",
        "RightWrist",
        "Wrist_R"
    };

    private static readonly string[] GripPointNameCandidates =
    {
        "GripPoint",
        "ItemGrip",
        "WeaponGrip",
        "RightHandGrip",
        "HoldPoint",
        "ItemHoldPoint",
        "Handle",
        "Socket",
        "AttachPoint"
    };

    [Header("References")]
    [SerializeField] private Camera ownerCamera;
    [SerializeField] private Transform heldItemSocket;
    [SerializeField] private Transform itemDropAnchor;
    [SerializeField] private Rigidbody carrierBody;
    [SerializeField] private HamsterFullRagdollMotor motorStateSource;

    [Header("Input")]
    [SerializeField] private bool enableRightMousePickup = true;
    [SerializeField] private bool enableKeyboardPickupDropKey = false;
    [SerializeField] private Key pickupDropKey = Key.E;
    [SerializeField] private Key dropKey = Key.G;
    [SerializeField] private Key throwKey = Key.Q;

    [Header("Pickup")]
    [SerializeField] private float pickupDistance = 6f;
    [SerializeField] private float pickupRayRadius = 0.2f;
    [SerializeField] private LayerMask pickupMask = 640;
    [SerializeField] private bool fallbackToDefaultRaycastLayers = true;

    [Header("Held Pose")]
    // Production Suga uses WeaponPoint_R local zero/identity; keep MainScenes offsets explicit for per-item tuning.
    [SerializeField] private bool useVisualHandSocket = true;
    [SerializeField] private bool preferLocalHeldVisual = true;
    [SerializeField] private Vector3 heldItemLocalPositionOffset;
    [SerializeField] private Vector3 heldItemLocalEulerOffset;
    [SerializeField] private Vector3 heldItemLocalScaleMultiplier = Vector3.one;
    [SerializeField] private bool forceHeldRenderersVisible = true;
    [SerializeField] private bool syncHeldPoseOnPickup = true;

    [Header("Sprint Held Pose")]
    [SerializeField] private bool enableSprintHeldPose = true;
    [SerializeField] private int sprintHeldPoseWeaponItemId = 1;
    [SerializeField] private string sprintHeldPoseWeaponNameContains = "Toy_Hammer";
    [SerializeField] private float sprintHeldPoseBlendSpeed = 12f;
    [SerializeField] private Vector3 sprintHeldLocalPositionOffset;
    [SerializeField] private Vector3 sprintHeldLocalEulerOffset = new Vector3(0f, 0f, 90f);
    [SerializeField] private Vector3 sprintHeldLocalScaleMultiplier = Vector3.one;
    [SerializeField] private float sprintHeldPoseMinPlanarSpeed = 0.5f;

    [Header("Drop/Throw")]
    [SerializeField] private float throwForwardForce = 5f;
    [SerializeField] private float throwUpForce = 2f;
    [SerializeField] private bool useCameraForwardForThrow = true;
    [SerializeField] private float throwForceMultiplier = 1.5f;
    [SerializeField] private float throwUpwardBonus = 0.4f;
    [SerializeField] private float throwTorqueMultiplier = 1.1f;

    [Header("Throw Collision Grace")]
    [SerializeField] private bool enableThrowCollisionGrace = true;
    [SerializeField] private float throwCollisionGraceDuration = 0.25f;
    [SerializeField] private float throwReleaseCarrierClearance = 0.25f;
    [SerializeField] private float throwReleaseMaxForwardCorrection = 0.75f;
    [SerializeField] private float throwInitialOverlapRadius = 0.45f;
    [SerializeField] private bool debugThrowCollisionGraceLogs = false;

    [Header("Debug")]
    [SerializeField] private bool debugLogs = false;
    [SerializeField] private bool debugHeldPoseLogs = false;

    private NetworkObject _ownerNetworkObject;
    private PlayerHub _playerHub;
    private GameStateManager _gameStateManager;
    private PlayerStatusModule _playerStatusModule;
    private bool _playerStatusResolveAttempted;
    private Transform _targetRoot;
    private HamsterMotorShellSpinDashAdapter _spinDashAdapter;
    private Transform _runtimeRightHandSocket;
    private Transform _resolvedVisualRightHand;
    private Transform _heldGripPoint;
    private WeaponItemDataSO _heldWeaponData;
    private GameObject _localHeldVisualInstance;
    private GameObject _localHeldVisualSourcePrefab;
    private ItemPickupNetwork _heldPickup;
    private NetworkObject _heldNetworkObject;
    private Vector3 _heldWorldScale = Vector3.one;
    private Vector3 _baseHeldLocalPosition;
    private Vector3 _baseHeldLocalEuler;
    private Vector3 _baseHeldLocalScale = Vector3.one;
    private Vector3 _finalHeldLocalPosition;
    private Vector3 _finalHeldLocalEuler;
    private Quaternion _finalHeldLocalRotation = Quaternion.identity;
    private Vector3 _finalHeldLocalScale = Vector3.one;
    private float _sprintHeldPoseBlend;
    private bool _lastSprintPoseActive;
    private string _lastRaycastTargetName = "<none>";
    private string _lastBlockerReason = string.Empty;
    private string _lastSocketSource = "None";
    private string _lastHandBoneName = "<none>";
    private string _lastGripPointName = "<none>";
    private string _lastWeaponDataName = "<none>";
    private bool _usingLocalHeldVisual;
    private float _lastHeldPoseLogTime = -HeldPoseLogInterval;
    private readonly Collider[] _throwOverlapHits = new Collider[ThrowOverlapBufferSize];
    private readonly List<CollisionIgnorePair> _activeThrowCollisionIgnorePairs = new List<CollisionIgnorePair>();

    private readonly struct CollisionIgnorePair
    {
        public readonly Collider ItemCollider;
        public readonly Collider OtherCollider;

        public CollisionIgnorePair(Collider itemCollider, Collider otherCollider)
        {
            ItemCollider = itemCollider;
            OtherCollider = otherCollider;
        }
    }

    public bool HasHeldItem => _heldPickup != null;
    public string CurrentHeldItemName => _heldPickup != null ? _heldPickup.name : "<none>";
    public string LastRaycastTargetName => _lastRaycastTargetName;
    public string LastBlockerReason => _lastBlockerReason;
    public Transform HeldItemSocket => heldItemSocket;
    public Transform ResolvedHeldItemSocket => ResolveHeldSocket();
    public Transform ItemDropAnchor => itemDropAnchor;
    public ItemPickupNetwork CurrentHeldPickup => _heldPickup;
    public WeaponItemDataSO CurrentHeldWeaponData => _heldWeaponData;
    public Transform CurrentHeldSocket => ResolveHeldSocket();

    private void Awake()
    {
        CacheReferences();
    }

    private void OnEnable()
    {
        CacheReferences();
    }

    private void OnDisable()
    {
        RestoreAllActiveThrowCollisionGrace();
        DestroyLocalHeldVisual();
    }

    private void Update()
    {
        if (!CanReadOwnerInput())
            return;

        if (!IsPlayingGameplayState())
            return;

        if (IsItemInputBlockedByElimination())
            return;

        if (IsSpinDashDizzyInputBlocked())
            return;

        bool pickupDropPressed = ReadPickupDropPressed();
        bool dropPressed = ReadKeyDown(dropKey);
        bool throwPressed = ReadKeyDown(throwKey);

        if (_heldPickup != null)
        {
            if (throwPressed)
            {
                ReleaseHeldItem(true, "ThrowInput");
                return;
            }

            if (dropPressed || (enableKeyboardPickupDropKey && pickupDropPressed))
                ReleaseHeldItem(false, "DropInput");

            return;
        }

        if (pickupDropPressed)
        {
            if (_playerHub != null && _playerHub.TryConsumePostItPeelInteractThisFrame())
                return;

            TryPickupFromCamera();
        }
    }

    private void LateUpdate()
    {
        if (!IsTargetRoot())
            return;

        if (_heldPickup == null)
            return;

        ApplyHeldPose(false);
        if (forceHeldRenderersVisible && !_usingLocalHeldVisual)
            SetHeldRenderersVisible(true);
    }

    private void CacheReferences()
    {
        _targetRoot = transform.root != null ? transform.root : transform;
        _ownerNetworkObject = GetComponent<NetworkObject>();
        if (_ownerNetworkObject == null)
            _ownerNetworkObject = GetComponentInParent<NetworkObject>();

        _playerStatusModule = null;
        _playerStatusResolveAttempted = false;
        ResolvePlayerStatusModule();

        if (_playerHub == null)
            _playerHub = GetComponent<PlayerHub>();
        if (_playerHub == null)
            _playerHub = GetComponentInParent<PlayerHub>();

        if (_gameStateManager == null)
            _gameStateManager = FindFirstObjectByType<GameStateManager>();

        if (carrierBody == null)
        {
            Transform motorShellBody = FindChildRecursive(_targetRoot, "MotorShellBody");
            if (motorShellBody != null)
                carrierBody = motorShellBody.GetComponent<Rigidbody>();
        }

        if (motorStateSource == null && _targetRoot != null)
            motorStateSource = _targetRoot.GetComponentInChildren<HamsterFullRagdollMotor>(true);

        if (_spinDashAdapter == null && _targetRoot != null)
            _spinDashAdapter = _targetRoot.GetComponentInChildren<HamsterMotorShellSpinDashAdapter>(true);
    }

    private bool CanReadOwnerInput()
    {
        if (!IsTargetRoot())
            return false;

        NetworkManager networkManager = NetworkManager.Singleton;
        if (networkManager == null || !networkManager.IsListening)
        {
            SetBlocker("NetworkManager not listening");
            return false;
        }

        if (_ownerNetworkObject == null || !_ownerNetworkObject.IsSpawned)
        {
            SetInputBlockerOnce("Owner NetworkObject missing or not spawned");
            return false;
        }

        if (!_ownerNetworkObject.IsOwner)
            return false;

        return true;
    }

    private bool CanChangeItemState()
    {
        NetworkManager networkManager = NetworkManager.Singleton;
        if (networkManager == null || !networkManager.IsServer)
        {
            SetBlocker("server required");
            return false;
        }

        if (!IsPlayingGameplayState())
            return false;

        if (!IsTargetRoot() ||
            _ownerNetworkObject == null ||
            !_ownerNetworkObject.IsSpawned ||
            !_ownerNetworkObject.IsOwner)
        {
            return false;
        }

        return !IsItemInputBlockedByElimination();
    }

    private PlayerStatusModule ResolvePlayerStatusModule()
    {
        if (_playerStatusModule != null)
        {
            NetworkObject cachedOwner = _playerStatusModule.GetComponentInParent<NetworkObject>();
            if (cachedOwner == _ownerNetworkObject)
                return _playerStatusModule;

            _playerStatusModule = null;
        }
        else if (!ReferenceEquals(_playerStatusModule, null))
        {
            _playerStatusModule = null;
            _playerStatusResolveAttempted = false;
        }

        if (_playerStatusResolveAttempted)
            return null;

        _playerStatusResolveAttempted = true;
        if (_ownerNetworkObject == null)
            return null;

        PlayerStatusModule candidate = _ownerNetworkObject.GetComponent<PlayerStatusModule>();
        if (candidate == null)
            candidate = _ownerNetworkObject.GetComponentInChildren<PlayerStatusModule>(true);

        if (candidate == null ||
            candidate.GetComponentInParent<NetworkObject>() != _ownerNetworkObject)
        {
            return null;
        }

        _playerStatusModule = candidate;
        return _playerStatusModule;
    }

    private bool IsItemInputBlockedByElimination()
    {
        PlayerStatusModule status = ResolvePlayerStatusModule();
        if (status == null)
        {
            SetInputBlockerOnce("PlayerStatusModule missing or root mismatch");
            return true;
        }

        if (!status.IsEliminated)
            return false;

        SetInputBlockerOnce("Player eliminated");
        return true;
    }

    private void SetInputBlockerOnce(string reason)
    {
        if (!string.Equals(_lastBlockerReason, reason, StringComparison.Ordinal))
            SetBlocker(reason);
    }

    private bool IsPlayingGameplayState()
    {
        if (_gameStateManager == null)
            _gameStateManager = FindFirstObjectByType<GameStateManager>();

        return _gameStateManager != null &&
               _gameStateManager.GetState() == GameStateManager.GameState.Playing;
    }

    private bool IsSpinDashDizzyInputBlocked()
    {
        return _spinDashAdapter != null && _spinDashAdapter.IsDizzyActive;
    }

    private bool ReadPickupDropPressed()
    {
        bool mousePressed = false;
        Mouse mouse = Mouse.current;
        if (enableRightMousePickup && mouse != null)
            mousePressed = mouse.rightButton.wasPressedThisFrame;

        bool keyPressed = enableKeyboardPickupDropKey && ReadKeyDown(pickupDropKey);
        return mousePressed || keyPressed;
    }

    private static bool ReadKeyDown(Key key)
    {
        Keyboard keyboard = Keyboard.current;
        return keyboard != null && keyboard[key].wasPressedThisFrame;
    }

    private void TryPickupFromCamera()
    {
        if (!CanChangeItemState())
            return;

        Transform socket = ResolveHeldSocket();
        if (socket == null)
        {
            SetBlocker("HeldItemSocket missing");
            return;
        }

        if (!TryFindPickupTarget(out ItemPickupNetwork pickup))
            return;

        NetworkObject itemNetworkObject = pickup.GetComponent<NetworkObject>();
        if (itemNetworkObject == null)
            itemNetworkObject = pickup.GetComponentInParent<NetworkObject>();

        if (itemNetworkObject == null || !itemNetworkObject.IsSpawned)
        {
            SetBlocker("target NetworkObject missing or not spawned");
            return;
        }

        if (!CanChangeItemState())
            return;

        _heldPickup = pickup;
        _heldNetworkObject = itemNetworkObject;
        CacheHeldPoseData(pickup);
        _heldGripPoint = ResolveGripPoint(pickup.transform);
        _heldWorldScale = ResolveHeldWorldScale();

        pickup.SetHeldStateServer(true);
        _usingLocalHeldVisual = TryEnsureLocalHeldVisual(socket);
        if (_usingLocalHeldVisual)
            SetHeldRenderersVisible(false);
        else if (forceHeldRenderersVisible)
            SetHeldRenderersVisible(true);

        ApplyHeldPose(syncHeldPoseOnPickup);
        Log("[MSItem/Pickup]", $"item={pickup.name} networkObject={itemNetworkObject.name}");
    }

    private bool TryFindPickupTarget(out ItemPickupNetwork pickup)
    {
        pickup = null;
        _lastRaycastTargetName = "<none>";

        Camera camera = ResolveOwnerCamera();
        if (camera == null)
        {
            SetBlocker("owner camera missing");
            return false;
        }

        Ray ray = new Ray(camera.transform.position, camera.transform.forward);
        int mask = pickupMask.value != 0 ? pickupMask.value : Physics.DefaultRaycastLayers;
        if (TryFindPickupTarget(ray, mask, out pickup))
            return true;

        if (fallbackToDefaultRaycastLayers && mask != Physics.DefaultRaycastLayers)
            return TryFindPickupTarget(ray, Physics.DefaultRaycastLayers, out pickup);

        SetBlocker("no pickup target");
        return false;
    }

    private bool TryFindPickupTarget(Ray ray, int mask, out ItemPickupNetwork pickup)
    {
        pickup = null;
        float distance = Mathf.Max(0f, pickupDistance);
        if (distance <= 0f)
            return false;

        RaycastHit[] hits = pickupRayRadius > 0f
            ? Physics.SphereCastAll(ray, pickupRayRadius, distance, mask, QueryTriggerInteraction.Ignore)
            : Physics.RaycastAll(ray, distance, mask, QueryTriggerInteraction.Ignore);

        if (hits == null || hits.Length == 0)
            return false;

        Array.Sort(hits, (left, right) => left.distance.CompareTo(right.distance));
        for (int i = 0; i < hits.Length; i++)
        {
            Collider hitCollider = hits[i].collider;
            if (hitCollider == null || IsSelfCollider(hitCollider))
                continue;

            ItemPickupNetwork candidate = hitCollider.GetComponentInParent<ItemPickupNetwork>();
            if (candidate == null || candidate == _heldPickup)
                continue;

            _lastRaycastTargetName = candidate.name;
            pickup = candidate;
            return true;
        }

        return false;
    }

    private void ApplyHeldPose(bool syncPoseNetworkState)
    {
        if (_heldPickup == null)
            return;

        Transform socket = ResolveHeldSocket();
        if (socket == null)
            return;

        UpdateSprintHeldPoseBlend();
        UpdateFinalHeldPose();
        _usingLocalHeldVisual = TryEnsureLocalHeldVisual(socket);
        if (_usingLocalHeldVisual)
            SetHeldRenderersVisible(false);

        Quaternion socketRotation = socket.rotation * _finalHeldLocalRotation;
        Vector3 socketPosition = socket.TransformPoint(_finalHeldLocalPosition);
        Vector3 worldPosition = socketPosition;
        Quaternion worldRotation = socketRotation;

        if (!_usingLocalHeldVisual && _heldGripPoint != null && TryGetGripAdjustedRootPose(socketPosition, socketRotation, out Vector3 gripRootPosition, out Quaternion gripRootRotation))
        {
            worldPosition = gripRootPosition;
            worldRotation = gripRootRotation;
        }

        _heldPickup.ApplyPose(worldPosition, worldRotation, _heldWorldScale, syncPoseNetworkState);
        LogHeldPose(socket, socketPosition, socketRotation, worldPosition, worldRotation);
        Log("[MSItem/Held]", $"item={_heldPickup.name} sync={syncPoseNetworkState}");
    }

    private void ReleaseHeldItem(bool throwItem, string reason)
    {
        if (!CanChangeItemState())
            return;

        if (_heldPickup == null)
            return;

        ItemPickupNetwork pickup = _heldPickup;
        Vector3 releasePosition = ResolveReleasePosition();
        Quaternion releaseRotation = ResolveReleaseRotation();
        Vector3 carrierVelocity = GetCarrierVelocity();
        Vector3 impulse = throwItem ? ResolveThrowImpulse() : Vector3.zero;
        Collider[] itemColliders = GetItemCollisionGraceColliders(pickup);
        Collider[] holderColliders = GetHolderCollisionGraceColliders();
        releasePosition = ResolveCollisionSafeReleasePosition(releasePosition, holderColliders, out bool releaseAdjusted);
        List<Collider> initialOverlapPlayerColliders = CollectInitialOverlapPlayerColliders(releasePosition, holderColliders);

        if (!CanChangeItemState())
            return;

        bool restored = pickup.TryRestoreDroppedStateServer(
            releasePosition,
            releaseRotation,
            _heldWorldScale,
            carrierVelocity,
            impulse);

        if (!restored)
        {
            pickup.SetHeldStateServer(false);
            pickup.ApplyPose(releasePosition, releaseRotation, _heldWorldScale, true);
            Rigidbody itemBody = pickup.GetComponent<Rigidbody>();
            if (throwItem && itemBody != null && !itemBody.isKinematic)
                itemBody.AddForce(impulse, ForceMode.Impulse);
        }

        if (throwItem)
            ApplyThrowTorque(pickup, impulse);

        BeginThrowCollisionGrace(itemColliders, holderColliders, initialOverlapPlayerColliders, throwItem, releaseAdjusted);

        Log(throwItem ? "[MSItem/Throw]" : "[MSItem/Drop]", $"item={pickup.name} restored={restored} reason={reason}");
        pickup.SetWorldVisualVisibleServer(true);
        DestroyLocalHeldVisual();
        _heldPickup = null;
        _heldNetworkObject = null;
        _heldGripPoint = null;
        ResetHeldPoseData();
    }

    private Vector3 ResolveReleasePosition()
    {
        if (itemDropAnchor != null)
            return itemDropAnchor.position;

        Transform reference = ResolveHeldSocket();
        if (reference == null)
            reference = transform;

        return reference.position;
    }

    private Quaternion ResolveReleaseRotation()
    {
        if (itemDropAnchor != null)
            return itemDropAnchor.rotation;

        Transform reference = ResolveHeldSocket();
        return reference != null ? reference.rotation : transform.rotation;
    }

    private Vector3 ResolveThrowImpulse()
    {
        Vector3 forward = Vector3.zero;
        if (useCameraForwardForThrow)
        {
            Camera camera = ResolveOwnerCamera();
            if (camera != null)
                forward = Vector3.ProjectOnPlane(camera.transform.forward, Vector3.up);
        }

        if (forward.sqrMagnitude <= 0.0001f)
            forward = Vector3.ProjectOnPlane(transform.forward, Vector3.up);

        if (forward.sqrMagnitude <= 0.0001f)
            forward = Vector3.forward;
        else
            forward.Normalize();

        float forceMultiplier = Mathf.Max(0f, throwForceMultiplier);
        float forwardForce = Mathf.Max(0f, throwForwardForce) * forceMultiplier;
        float upForce = Mathf.Max(0f, throwUpForce) * forceMultiplier + Mathf.Max(0f, throwUpwardBonus);
        return forward * forwardForce + Vector3.up * upForce;
    }

    private void ApplyThrowTorque(ItemPickupNetwork pickup, Vector3 impulse)
    {
        if (pickup == null || throwTorqueMultiplier <= 0f)
            return;

        Rigidbody itemBody = pickup.GetComponent<Rigidbody>();
        if (itemBody == null || itemBody.isKinematic)
            return;

        Vector3 planarImpulse = Vector3.ProjectOnPlane(impulse, Vector3.up);
        Vector3 spinAxis = planarImpulse.sqrMagnitude > 0.0001f
            ? Vector3.Cross(Vector3.up, planarImpulse.normalized)
            : transform.right;

        if (spinAxis.sqrMagnitude <= 0.0001f)
            return;

        itemBody.AddTorque(spinAxis.normalized * throwTorqueMultiplier, ForceMode.Impulse);
    }

    private Vector3 GetCarrierVelocity()
    {
        return carrierBody != null ? carrierBody.linearVelocity : Vector3.zero;
    }

    private Collider[] GetItemCollisionGraceColliders(ItemPickupNetwork pickup)
    {
        return pickup != null ? pickup.GetComponentsInChildren<Collider>(true) : Array.Empty<Collider>();
    }

    private Collider[] GetHolderCollisionGraceColliders()
    {
        Transform holderRoot = _targetRoot != null ? _targetRoot : transform.root;
        return holderRoot != null ? holderRoot.GetComponentsInChildren<Collider>(true) : Array.Empty<Collider>();
    }

    private Vector3 ResolveCollisionSafeReleasePosition(Vector3 releasePosition, Collider[] holderColliders, out bool adjusted)
    {
        adjusted = false;
        if (!IsThrowCollisionGraceEnabled() || holderColliders == null || holderColliders.Length == 0)
            return releasePosition;

        float clearance = Mathf.Max(0f, throwReleaseCarrierClearance);
        float maxCorrection = Mathf.Max(0f, throwReleaseMaxForwardCorrection);
        if (clearance <= 0f || maxCorrection <= 0f)
            return releasePosition;

        Vector3 forward = ResolveCarrierPlanarForward();
        if (forward.sqrMagnitude <= 0.0001f)
            return releasePosition;

        Vector3 candidate = releasePosition;
        float moved = 0f;
        float step = Mathf.Max(MinThrowReleaseStep, clearance * 0.5f);
        const int maxIterations = 24;

        for (int i = 0; i < maxIterations && moved < maxCorrection; i++)
        {
            if (!IsPointTooCloseToSolidHolderCollider(candidate, holderColliders, clearance))
                break;

            float delta = Mathf.Min(step, maxCorrection - moved);
            candidate += forward * delta;
            moved += delta;
        }

        adjusted = (candidate - releasePosition).sqrMagnitude > 0.000001f;
        if (adjusted)
            LogThrowCollisionGrace($"releaseAdjusted from={FormatVector(releasePosition)} to={FormatVector(candidate)} moved={moved:F3}");

        return candidate;
    }

    private bool IsPointTooCloseToSolidHolderCollider(Vector3 point, Collider[] holderColliders, float clearance)
    {
        float clearanceSqr = clearance * clearance;
        for (int i = 0; i < holderColliders.Length; i++)
        {
            Collider holderCollider = holderColliders[i];
            if (!IsUsableCollisionGraceCollider(holderCollider) || holderCollider.isTrigger)
                continue;

            Vector3 closestPoint = holderCollider.ClosestPoint(point);
            if (!IsFiniteVector(closestPoint))
                continue;

            if ((closestPoint - point).sqrMagnitude <= clearanceSqr)
                return true;
        }

        return false;
    }

    private Vector3 ResolveCarrierPlanarForward()
    {
        Vector3 forward = Vector3.zero;
        if (carrierBody != null)
            forward = carrierBody.transform.forward;
        else if (_targetRoot != null)
            forward = _targetRoot.forward;
        else
            forward = transform.forward;

        forward = Vector3.ProjectOnPlane(forward, Vector3.up);
        return forward.sqrMagnitude > 0.0001f ? forward.normalized : Vector3.zero;
    }

    private List<Collider> CollectInitialOverlapPlayerColliders(Vector3 releasePosition, Collider[] holderColliders)
    {
        List<Collider> result = new List<Collider>();
        if (!IsThrowCollisionGraceEnabled())
            return result;

        float radius = Mathf.Max(0f, throwInitialOverlapRadius);
        if (radius <= 0f)
            return result;

        int hitCount = Physics.OverlapSphereNonAlloc(
            releasePosition,
            radius,
            _throwOverlapHits,
            ~0,
            QueryTriggerInteraction.Ignore);

        for (int i = 0; i < hitCount; i++)
        {
            Collider candidate = _throwOverlapHits[i];
            _throwOverlapHits[i] = null;

            if (!IsUsableCollisionGraceCollider(candidate))
                continue;

            if (ContainsCollider(holderColliders, candidate))
                continue;

            if (!IsOtherPlayerCollider(candidate))
                continue;

            if (ContainsCollider(result, candidate))
                continue;

            result.Add(candidate);
        }

        if (result.Count > 0)
            LogThrowCollisionGrace($"nearbyInitialOverlap count={result.Count} radius={radius:F3} pos={FormatVector(releasePosition)}");

        return result;
    }

    private void BeginThrowCollisionGrace(
        Collider[] itemColliders,
        Collider[] holderColliders,
        List<Collider> initialOverlapPlayerColliders,
        bool throwItem,
        bool releaseAdjusted)
    {
        if (!IsThrowCollisionGraceEnabled() || itemColliders == null || itemColliders.Length == 0)
            return;

        List<CollisionIgnorePair> pairs = new List<CollisionIgnorePair>();
        AddCollisionIgnorePairs(itemColliders, holderColliders, pairs);
        AddCollisionIgnorePairs(itemColliders, initialOverlapPlayerColliders, pairs);

        if (pairs.Count == 0)
            return;

        for (int i = 0; i < pairs.Count; i++)
        {
            CollisionIgnorePair pair = pairs[i];
            if (!IsValidCollisionIgnorePair(pair))
                continue;

            Physics.IgnoreCollision(pair.ItemCollider, pair.OtherCollider, true);
            _activeThrowCollisionIgnorePairs.Add(pair);
        }

        float duration = Mathf.Max(MinThrowCollisionGraceDuration, throwCollisionGraceDuration);
        StartCoroutine(RestoreThrowCollisionGraceAfterDelay(pairs, duration));
        LogThrowCollisionGrace($"begin mode={(throwItem ? "Throw" : "Drop")} pairs={pairs.Count} duration={duration:F3} releaseAdjusted={releaseAdjusted}");
    }

    private void AddCollisionIgnorePairs(Collider[] itemColliders, Collider[] otherColliders, List<CollisionIgnorePair> pairs)
    {
        if (otherColliders == null)
            return;

        for (int i = 0; i < otherColliders.Length; i++)
            AddCollisionIgnorePairs(itemColliders, otherColliders[i], pairs);
    }

    private void AddCollisionIgnorePairs(Collider[] itemColliders, List<Collider> otherColliders, List<CollisionIgnorePair> pairs)
    {
        if (otherColliders == null)
            return;

        for (int i = 0; i < otherColliders.Count; i++)
            AddCollisionIgnorePairs(itemColliders, otherColliders[i], pairs);
    }

    private void AddCollisionIgnorePairs(Collider[] itemColliders, Collider otherCollider, List<CollisionIgnorePair> pairs)
    {
        if (!IsUsableCollisionGraceCollider(otherCollider))
            return;

        for (int i = 0; i < itemColliders.Length; i++)
        {
            Collider itemCollider = itemColliders[i];
            if (!IsUsableCollisionGraceCollider(itemCollider) || itemCollider == otherCollider)
                continue;

            CollisionIgnorePair pair = new CollisionIgnorePair(itemCollider, otherCollider);
            if (!ContainsPair(pairs, pair))
                pairs.Add(pair);
        }
    }

    private IEnumerator RestoreThrowCollisionGraceAfterDelay(List<CollisionIgnorePair> pairs, float delay)
    {
        yield return new WaitForSeconds(delay);
        RestoreThrowCollisionGracePairs(pairs);
    }

    private void RestoreThrowCollisionGracePairs(List<CollisionIgnorePair> pairs)
    {
        if (pairs == null)
            return;

        for (int i = 0; i < pairs.Count; i++)
        {
            CollisionIgnorePair pair = pairs[i];
            if (IsValidCollisionIgnorePair(pair))
                Physics.IgnoreCollision(pair.ItemCollider, pair.OtherCollider, false);

            _activeThrowCollisionIgnorePairs.Remove(pair);
        }

        LogThrowCollisionGrace($"restore pairs={pairs.Count}");
    }

    private void RestoreAllActiveThrowCollisionGrace()
    {
        if (_activeThrowCollisionIgnorePairs.Count == 0)
            return;

        for (int i = _activeThrowCollisionIgnorePairs.Count - 1; i >= 0; i--)
        {
            CollisionIgnorePair pair = _activeThrowCollisionIgnorePairs[i];
            if (IsValidCollisionIgnorePair(pair))
                Physics.IgnoreCollision(pair.ItemCollider, pair.OtherCollider, false);
        }

        LogThrowCollisionGrace($"restoreAll pairs={_activeThrowCollisionIgnorePairs.Count}");
        _activeThrowCollisionIgnorePairs.Clear();
    }

    private bool IsThrowCollisionGraceEnabled()
    {
        return enableThrowCollisionGrace && throwCollisionGraceDuration > 0f;
    }

    private bool IsOtherPlayerCollider(Collider candidate)
    {
        if (candidate == null)
            return false;

        Transform holderRoot = _targetRoot != null ? _targetRoot : transform.root;
        Transform candidateRoot = candidate.transform.root;
        if (holderRoot != null && candidateRoot == holderRoot)
            return false;

        return candidate.GetComponentInParent<PlayerHub>() != null ||
               candidate.GetComponentInParent<HamsterFullRagdollMotor>() != null ||
               candidate.GetComponentInParent<HamsterMotorShellImpactTarget>() != null ||
               candidate.GetComponentInParent<PlayerStatusModule>() != null;
    }

    private static bool IsUsableCollisionGraceCollider(Collider collider)
    {
        return collider != null && collider.gameObject != null;
    }

    private static bool IsValidCollisionIgnorePair(CollisionIgnorePair pair)
    {
        return IsUsableCollisionGraceCollider(pair.ItemCollider) &&
               IsUsableCollisionGraceCollider(pair.OtherCollider) &&
               pair.ItemCollider != pair.OtherCollider;
    }

    private static bool ContainsCollider(Collider[] colliders, Collider candidate)
    {
        if (colliders == null || candidate == null)
            return false;

        for (int i = 0; i < colliders.Length; i++)
        {
            if (colliders[i] == candidate)
                return true;
        }

        return false;
    }

    private static bool ContainsCollider(List<Collider> colliders, Collider candidate)
    {
        if (colliders == null || candidate == null)
            return false;

        for (int i = 0; i < colliders.Count; i++)
        {
            if (colliders[i] == candidate)
                return true;
        }

        return false;
    }

    private static bool ContainsPair(List<CollisionIgnorePair> pairs, CollisionIgnorePair candidate)
    {
        if (pairs == null)
            return false;

        for (int i = 0; i < pairs.Count; i++)
        {
            CollisionIgnorePair existing = pairs[i];
            if (existing.ItemCollider == candidate.ItemCollider &&
                existing.OtherCollider == candidate.OtherCollider)
            {
                return true;
            }
        }

        return false;
    }

    private void SetHeldRenderersVisible(bool visible)
    {
        if (_heldNetworkObject == null)
            return;

        Renderer[] renderers = _heldNetworkObject.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] != null)
                renderers[i].enabled = visible;
        }
    }

    private Camera ResolveOwnerCamera()
    {
        if (ownerCamera != null)
            return ownerCamera;

        Camera camera = ResolvePlayerHubCameraRootCamera();
        if (camera != null)
        {
            ownerCamera = camera;
            return ownerCamera;
        }

        Transform playerCameraTransform = FindChildRecursive(_targetRoot, PlayerCameraName);
        if (playerCameraTransform != null && playerCameraTransform.TryGetComponent(out camera))
        {
            ownerCamera = camera;
            return ownerCamera;
        }

        Camera mainCamera = Camera.main;
        if (mainCamera != null)
            ownerCamera = mainCamera;

        return ownerCamera;
    }

    private Camera ResolvePlayerHubCameraRootCamera()
    {
        if (_playerHub == null)
            return null;

        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        FieldInfo field = typeof(PlayerHub).GetField(CameraRootFieldName, flags);
        if (field == null)
            return _playerHub.PlayerCamera;

        object rawValue = field.GetValue(_playerHub);
        Transform cameraRoot = null;
        if (rawValue is GameObject cameraRootObject)
            cameraRoot = cameraRootObject.transform;
        else if (rawValue is Transform cameraRootTransform)
            cameraRoot = cameraRootTransform;

        Camera camera = cameraRoot != null ? cameraRoot.GetComponentInChildren<Camera>(true) : null;
        return camera != null ? camera : _playerHub.PlayerCamera;
    }

    private Transform ResolveHeldSocket()
    {
        if (!IsTargetRoot())
            return heldItemSocket;

        if (!useVisualHandSocket)
        {
            _lastSocketSource = heldItemSocket != null ? "FallbackHeldItemSocket" : "None";
            return heldItemSocket;
        }

        Transform productionSocket = ResolveProductionSocket();
        if (productionSocket != null)
            return productionSocket;

        if (IsConfiguredVisualHandSocket(heldItemSocket))
        {
            _lastSocketSource = "SerializedVisualSocket";
            _lastHandBoneName = heldItemSocket.name;
            return heldItemSocket;
        }

        Transform rightHand = ResolveVisualRightHand();
        if (rightHand != null)
        {
            Transform socket = GetOrCreateRuntimeRightHandSocket(rightHand);
            if (socket != null)
                return socket;
        }

        _lastSocketSource = heldItemSocket != null ? "FallbackHeldItemSocket" : "None";
        return heldItemSocket;
    }

    private Transform ResolveProductionSocket()
    {
        Transform weaponPoint = FindSocketCandidate(WeaponPointSocketNameCandidates);
        if (weaponPoint != null)
        {
            _lastSocketSource = "WeaponPoint_R";
            _lastHandBoneName = weaponPoint.name;
            return weaponPoint;
        }

        Transform rightWeaponSocket = FindSocketCandidate(RightWeaponSocketNameCandidates);
        if (rightWeaponSocket != null)
        {
            _lastSocketSource = "RightWeaponSocket";
            _lastHandBoneName = rightWeaponSocket.name;
            return rightWeaponSocket;
        }

        Transform handEnd = FindSocketCandidate(RightHandEndSocketNameCandidates);
        if (handEnd != null)
        {
            _lastSocketSource = "RightHandEnd";
            _lastHandBoneName = handEnd.name;
            return handEnd;
        }

        return null;
    }

    private Transform FindSocketCandidate(string[] names)
    {
        Transform visualRoot = GetVisualSearchRoot();
        Transform candidate = FindChildByNameCandidatesRecursive(visualRoot, names);
        if (candidate != null)
            return candidate;

        Transform root = _targetRoot != null ? _targetRoot : transform.root;
        if (root == null || root == visualRoot)
            return null;

        return FindChildByNameCandidatesRecursive(root, names);
    }

    private bool IsConfiguredVisualHandSocket(Transform candidate)
    {
        if (candidate == null)
            return false;

        if (candidate.name == "HeldItemSocket")
            return false;

        if (NameMatchesAny(candidate.name, RightHandNameCandidates))
            return true;

        string normalizedName = NormalizeName(candidate.name);
        if (normalizedName == "rightweaponsocket" || normalizedName == "weaponpointr")
            return true;

        Transform visualRoot = GetVisualSearchRoot();
        return visualRoot != null && candidate.IsChildOf(visualRoot);
    }

    private Transform ResolveVisualRightHand()
    {
        if (_resolvedVisualRightHand != null)
            return _resolvedVisualRightHand;

        Animator visualAnimator = ResolveVisualAnimator();
        if (visualAnimator != null && visualAnimator.isHuman)
        {
            Transform rightHand = visualAnimator.GetBoneTransform(HumanBodyBones.RightHand);
            if (rightHand != null)
            {
                _resolvedVisualRightHand = rightHand;
                _lastSocketSource = "Animator.RightHand";
                _lastHandBoneName = rightHand.name;
                return _resolvedVisualRightHand;
            }
        }

        Transform searchRoot = GetVisualSearchRoot();
        Transform namedHand = FindChildByNameCandidatesRecursive(searchRoot, RightHandNameCandidates);
        if (namedHand == null)
            namedHand = FindChildByNameCandidatesRecursive(_targetRoot, RightHandNameCandidates);

        if (namedHand != null)
        {
            _resolvedVisualRightHand = namedHand;
            _lastSocketSource = "NamedRightHand";
            _lastHandBoneName = namedHand.name;
        }

        return _resolvedVisualRightHand;
    }

    private Animator ResolveVisualAnimator()
    {
        Transform visualRoot = GetVisualSearchRoot();
        Animator visualAnimator = visualRoot != null ? visualRoot.GetComponentInChildren<Animator>(true) : null;
        if (visualAnimator != null)
            return visualAnimator;

        return _targetRoot != null ? _targetRoot.GetComponentInChildren<Animator>(true) : null;
    }

    private Transform GetVisualSearchRoot()
    {
        Transform root = _targetRoot != null ? _targetRoot : transform.root;
        Transform visualRoot = FindChildRecursive(root, VisualPreviewRootName);
        return visualRoot != null ? visualRoot : root;
    }

    private Transform GetOrCreateRuntimeRightHandSocket(Transform rightHand)
    {
        if (rightHand == null)
            return null;

        if (_runtimeRightHandSocket != null && _runtimeRightHandSocket.parent == rightHand)
            return _runtimeRightHandSocket;

        Transform existing = FindDirectChildByName(rightHand, RuntimeRightHandSocketName);
        if (existing != null)
        {
            _runtimeRightHandSocket = existing;
            _lastSocketSource = "RuntimeRightHandSocket";
            return _runtimeRightHandSocket;
        }

        GameObject socketObject = new GameObject(RuntimeRightHandSocketName);
        Transform socketTransform = socketObject.transform;
        socketTransform.SetParent(rightHand, false);
        socketTransform.localPosition = Vector3.zero;
        socketTransform.localRotation = Quaternion.identity;
        socketTransform.localScale = Vector3.one;
        _runtimeRightHandSocket = socketTransform;
        _lastSocketSource = "RuntimeRightHandSocket";
        return _runtimeRightHandSocket;
    }

    private Transform ResolveGripPoint(Transform itemRoot)
    {
        Transform gripPoint = FindChildByNameCandidatesRecursive(itemRoot, GripPointNameCandidates, true);
        _lastGripPointName = gripPoint != null ? gripPoint.name : "<none>";
        return gripPoint;
    }

    private void CacheHeldPoseData(ItemPickupNetwork pickup)
    {
        ResetHeldPoseData();

        _heldWeaponData = pickup != null ? pickup.GetWeaponData() : null;
        _lastWeaponDataName = _heldWeaponData != null ? _heldWeaponData.name : "<none>";

        if (_heldWeaponData != null)
        {
            _baseHeldLocalPosition = _heldWeaponData.equippedLocalPosition;
            _baseHeldLocalEuler = _heldWeaponData.equippedLocalEulerAngles;
            _baseHeldLocalScale = _heldWeaponData.equippedLocalScale == Vector3.zero ? Vector3.one : _heldWeaponData.equippedLocalScale;
        }
        else
        {
            _baseHeldLocalPosition = Vector3.zero;
            _baseHeldLocalEuler = Vector3.zero;
            _baseHeldLocalScale = pickup != null ? pickup.GetDefaultLocalScale() : Vector3.one;
        }

        Vector3 scaleMultiplier = SanitizeScale(heldItemLocalScaleMultiplier);
        _finalHeldLocalPosition = _baseHeldLocalPosition + heldItemLocalPositionOffset;
        _finalHeldLocalRotation = Quaternion.Euler(_baseHeldLocalEuler) * Quaternion.Euler(heldItemLocalEulerOffset);
        _finalHeldLocalEuler = _finalHeldLocalRotation.eulerAngles;
        _finalHeldLocalScale = SanitizeScale(Vector3.Scale(_baseHeldLocalScale, scaleMultiplier));
        _sprintHeldPoseBlend = 0f;
        _lastSprintPoseActive = false;
    }

    private void ResetHeldPoseData()
    {
        _heldWeaponData = null;
        _heldWorldScale = Vector3.one;
        _baseHeldLocalPosition = Vector3.zero;
        _baseHeldLocalEuler = Vector3.zero;
        _baseHeldLocalScale = Vector3.one;
        _finalHeldLocalPosition = heldItemLocalPositionOffset;
        _finalHeldLocalRotation = Quaternion.Euler(heldItemLocalEulerOffset);
        _finalHeldLocalEuler = _finalHeldLocalRotation.eulerAngles;
        _finalHeldLocalScale = SanitizeScale(heldItemLocalScaleMultiplier);
        _sprintHeldPoseBlend = 0f;
        _lastSprintPoseActive = false;
        _lastGripPointName = "<none>";
        _lastWeaponDataName = "<none>";
        _usingLocalHeldVisual = false;
    }

    private void UpdateSprintHeldPoseBlend()
    {
        bool targetActive = ShouldUseSprintHeldPose();
        float targetBlend = targetActive ? 1f : 0f;
        float maxDelta = Mathf.Max(0.01f, sprintHeldPoseBlendSpeed) * Time.deltaTime;
        _sprintHeldPoseBlend = Mathf.MoveTowards(_sprintHeldPoseBlend, targetBlend, maxDelta);
        _lastSprintPoseActive = targetActive;
    }

    private bool ShouldUseSprintHeldPose()
    {
        if (!enableSprintHeldPose || _heldWeaponData == null)
            return false;

        bool itemMatches = false;
        if (sprintHeldPoseWeaponItemId > 0 && _heldWeaponData.itemId == sprintHeldPoseWeaponItemId)
            itemMatches = true;

        if (!itemMatches && !string.IsNullOrEmpty(sprintHeldPoseWeaponNameContains))
            itemMatches = _heldWeaponData.name.IndexOf(sprintHeldPoseWeaponNameContains, StringComparison.OrdinalIgnoreCase) >= 0;

        if (!itemMatches)
            return false;

        if (motorStateSource == null && _targetRoot != null)
            motorStateSource = _targetRoot.GetComponentInChildren<HamsterFullRagdollMotor>(true);

        if (motorStateSource == null)
            return false;

        return motorStateSource.IsSprintHeld
            && motorStateSource.HasMoveInput
            && motorStateSource.CurrentPlanarSpeed >= Mathf.Max(0f, sprintHeldPoseMinPlanarSpeed);
    }

    private void UpdateFinalHeldPose()
    {
        Vector3 baseLocalPosition = _baseHeldLocalPosition + heldItemLocalPositionOffset;
        Quaternion baseLocalRotation = Quaternion.Euler(_baseHeldLocalEuler) * Quaternion.Euler(heldItemLocalEulerOffset);
        Vector3 baseLocalScale = SanitizeScale(Vector3.Scale(_baseHeldLocalScale, SanitizeScale(heldItemLocalScaleMultiplier)));

        Quaternion sprintRotation = baseLocalRotation * Quaternion.Euler(sprintHeldLocalEulerOffset);
        Vector3 sprintPosition = baseLocalPosition + sprintHeldLocalPositionOffset;
        Vector3 sprintScale = SanitizeScale(Vector3.Scale(baseLocalScale, SanitizeScale(sprintHeldLocalScaleMultiplier)));

        _finalHeldLocalPosition = Vector3.Lerp(baseLocalPosition, sprintPosition, _sprintHeldPoseBlend);
        _finalHeldLocalRotation = Quaternion.Slerp(baseLocalRotation, sprintRotation, _sprintHeldPoseBlend);
        _finalHeldLocalEuler = _finalHeldLocalRotation.eulerAngles;
        _finalHeldLocalScale = SanitizeScale(Vector3.Lerp(baseLocalScale, sprintScale, _sprintHeldPoseBlend));
        _heldWorldScale = ResolveHeldWorldScale();
    }

    private Vector3 ResolveHeldWorldScale()
    {
        return SanitizeScale(_finalHeldLocalScale);
    }

    private bool TryEnsureLocalHeldVisual(Transform handSocket)
    {
        if (!ShouldUseLocalHeldVisual(_heldWeaponData) || handSocket == null)
        {
            DestroyLocalHeldVisual();
            return false;
        }

        GameObject visualPrefab = _heldWeaponData.equippedModelPrefab;
        if (_localHeldVisualInstance == null || _localHeldVisualSourcePrefab != visualPrefab)
        {
            DestroyLocalHeldVisual();
            _localHeldVisualInstance = Instantiate(visualPrefab);
            _localHeldVisualSourcePrefab = visualPrefab;
            DisableLocalHeldVisualPhysics(_localHeldVisualInstance);
            Log("[MSItemVisual]", $"spawn={visualPrefab.name}");
        }

        Transform visualTransform = _localHeldVisualInstance.transform;
        if (visualTransform.parent != handSocket)
            visualTransform.SetParent(handSocket, false);

        visualTransform.localPosition = _finalHeldLocalPosition;
        visualTransform.localRotation = _finalHeldLocalRotation;
        visualTransform.localScale = _finalHeldLocalScale;
        return true;
    }

    private bool ShouldUseLocalHeldVisual(WeaponItemDataSO weaponData)
    {
        if (!preferLocalHeldVisual)
            return false;

        if (weaponData == null || weaponData.equippedModelPrefab == null)
            return false;

        if (weaponData.equippedModelPrefab.GetComponentInChildren<NetworkObject>(true) != null)
            return false;

        if (weaponData.equippedModelPrefab.GetComponentInChildren<NetworkBehaviour>(true) != null)
            return false;

        return true;
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
            rigidbodies[i].detectCollisions = false;
        }
    }

    private void DestroyLocalHeldVisual()
    {
        _usingLocalHeldVisual = false;

        if (_localHeldVisualInstance == null)
        {
            _localHeldVisualSourcePrefab = null;
            return;
        }

        if (Application.isPlaying)
            Destroy(_localHeldVisualInstance);
        else
            DestroyImmediate(_localHeldVisualInstance);

        _localHeldVisualInstance = null;
        _localHeldVisualSourcePrefab = null;
    }

    private bool TryGetGripAdjustedRootPose(Vector3 socketPosition, Quaternion socketRotation, out Vector3 rootPosition, out Quaternion rootRotation)
    {
        rootPosition = socketPosition;
        rootRotation = socketRotation;

        if (_heldPickup == null || _heldGripPoint == null)
            return false;

        Transform itemRoot = _heldPickup.transform;
        if (itemRoot == null || !_heldGripPoint.IsChildOf(itemRoot))
            return false;

        Vector3 gripLocalPosition = itemRoot.InverseTransformPoint(_heldGripPoint.position);
        Quaternion gripLocalRotation = Quaternion.Inverse(itemRoot.rotation) * _heldGripPoint.rotation;
        Vector3 scaledGripLocalPosition = Vector3.Scale(gripLocalPosition, _heldWorldScale);

        rootRotation = socketRotation * Quaternion.Inverse(gripLocalRotation);
        rootPosition = socketPosition - rootRotation * scaledGripLocalPosition;
        return true;
    }

    private bool IsSelfCollider(Collider candidate)
    {
        if (candidate == null)
            return true;

        Transform root = _targetRoot != null ? _targetRoot : transform.root;
        return root != null && candidate.transform.root == root;
    }

    private bool IsTargetRoot()
    {
        Transform root = _targetRoot != null ? _targetRoot : transform.root;
        if (root == null)
            return false;

        return root.name == TargetRootName || root.name == TargetRootName + "(Clone)";
    }

    private static Transform FindChildRecursive(Transform current, string childName)
    {
        if (current == null || string.IsNullOrEmpty(childName))
            return null;

        if (current.name == childName)
            return current;

        for (int i = 0; i < current.childCount; i++)
        {
            Transform match = FindChildRecursive(current.GetChild(i), childName);
            if (match != null)
                return match;
        }

        return null;
    }

    private static Transform FindChildByNameCandidatesRecursive(Transform current, string[] names, bool skipRoot = false)
    {
        if (current == null || names == null)
            return null;

        if (!skipRoot && NameMatchesAny(current.name, names))
            return current;

        for (int i = 0; i < current.childCount; i++)
        {
            Transform child = current.GetChild(i);
            if (child == null)
                continue;

            if (NameMatchesAny(child.name, names))
                return child;

            Transform match = FindChildByNameCandidatesRecursive(child, names);
            if (match != null)
                return match;
        }

        return null;
    }

    private static Transform FindDirectChildByName(Transform parent, string childName)
    {
        if (parent == null || string.IsNullOrEmpty(childName))
            return null;

        for (int i = 0; i < parent.childCount; i++)
        {
            Transform child = parent.GetChild(i);
            if (child != null && child.name == childName)
                return child;
        }

        return null;
    }

    private static Vector3 SanitizeScale(Vector3 scale)
    {
        if (!IsFinite(scale.x) || Mathf.Approximately(scale.x, 0f))
            scale.x = 1f;
        if (!IsFinite(scale.y) || Mathf.Approximately(scale.y, 0f))
            scale.y = 1f;
        if (!IsFinite(scale.z) || Mathf.Approximately(scale.z, 0f))
            scale.z = 1f;

        return scale;
    }

    private static bool IsFinite(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }

    private static bool IsFiniteVector(Vector3 value)
    {
        return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
    }

    private static bool NameMatchesAny(string name, string[] candidates)
    {
        if (string.IsNullOrEmpty(name) || candidates == null)
            return false;

        string normalizedName = NormalizeName(name);
        for (int i = 0; i < candidates.Length; i++)
        {
            string candidate = candidates[i];
            if (string.IsNullOrEmpty(candidate))
                continue;

            if (string.Equals(name, candidate, StringComparison.OrdinalIgnoreCase))
                return true;

            if (normalizedName == NormalizeName(candidate))
                return true;
        }

        return false;
    }

    private static string NormalizeName(string value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        return value
            .Replace(" ", string.Empty)
            .Replace("_", string.Empty)
            .Replace(":", string.Empty)
            .Replace(".", string.Empty)
            .Replace("-", string.Empty)
            .ToLowerInvariant();
    }

    private void SetBlocker(string reason)
    {
        _lastBlockerReason = reason;
        Log("[MSItem/Blocker]", reason);
    }

    private void Log(string prefix, string message)
    {
        if (!debugLogs)
            return;

        Debug.Log($"{prefix} {name} {message}", this);
    }

    private void LogThrowCollisionGrace(string message)
    {
        if (!debugThrowCollisionGraceLogs)
            return;

        Debug.Log($"[MSItem/ThrowCollisionGrace] {name} {message}", this);
    }

    private void LogHeldPose(Transform socket, Vector3 socketPosition, Quaternion socketRotation, Vector3 itemPosition, Quaternion itemRotation)
    {
        if (!debugHeldPoseLogs || _heldPickup == null)
            return;

        if (Time.unscaledTime - _lastHeldPoseLogTime < HeldPoseLogInterval)
            return;

        _lastHeldPoseLogTime = Time.unscaledTime;
        float distance = Vector3.Distance(socketPosition, _heldGripPoint != null ? _heldGripPoint.position : _heldPickup.transform.position);
        bool sprintHeld = motorStateSource != null && motorStateSource.IsSprintHeld;
        bool hasMoveInput = motorStateSource != null && motorStateSource.HasMoveInput;
        float planarSpeed = motorStateSource != null ? motorStateSource.CurrentPlanarSpeed : 0f;
        int heldWeaponId = _heldWeaponData != null ? _heldWeaponData.itemId : 0;
        string heldItemName = _heldPickup != null ? _heldPickup.name : "<null>";

        Debug.Log(
            $"[MSItemSocket] {name} source={_lastSocketSource} socket={(socket != null ? socket.name : "<null>")} hand={_lastHandBoneName} pos={FormatVector(socketPosition)} rot={FormatEuler(socketRotation)}",
            this);
        Debug.Log(
            $"[MSItemDataPose] {name} weapon={_lastWeaponDataName} basePos={FormatVector(_baseHeldLocalPosition)} baseEuler={FormatVector(_baseHeldLocalEuler)} baseScale={FormatVector(_baseHeldLocalScale)} finalPos={FormatVector(_finalHeldLocalPosition)} finalEuler={FormatVector(_finalHeldLocalEuler)} finalScale={FormatVector(_finalHeldLocalScale)}",
            this);
        Debug.Log(
            $"[MSItemSprintPose] {name} item={heldItemName} id={heldWeaponId} sprintHeld={sprintHeld} hasMoveInput={hasMoveInput} planarSpeed={planarSpeed:F2} sprintActive={_lastSprintPoseActive} sprintBlend={_sprintHeldPoseBlend:F2} baseEuler={FormatVector(_baseHeldLocalEuler)} sprintEulerOffset={FormatVector(sprintHeldLocalEulerOffset)} finalEuler={FormatVector(_finalHeldLocalEuler)} usingLocalHeldVisual={_usingLocalHeldVisual}",
            this);
        Debug.Log(
            $"[MSItemGrip] {name} item={_heldPickup.name} grip={_lastGripPointName} distance={distance:F3} offsetPos={FormatVector(heldItemLocalPositionOffset)} offsetEuler={FormatVector(heldItemLocalEulerOffset)} scaleMul={FormatVector(heldItemLocalScaleMultiplier)}",
            this);
        Debug.Log(
            $"[MSItemPose] {name} itemRootPos={FormatVector(itemPosition)} itemRootRot={FormatEuler(itemRotation)}",
            this);
        Debug.Log(
            $"[MSItemVisual] {name} usingLocalHeldVisual={_usingLocalHeldVisual} visual={(_localHeldVisualInstance != null ? _localHeldVisualInstance.name : "<none>")}",
            this);
    }

    private static string FormatVector(Vector3 value)
    {
        return $"({value.x:F3},{value.y:F3},{value.z:F3})";
    }

    private static string FormatEuler(Quaternion value)
    {
        return FormatVector(value.eulerAngles);
    }
}
