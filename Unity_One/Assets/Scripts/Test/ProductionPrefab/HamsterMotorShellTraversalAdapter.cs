using Unity.Netcode;
using UnityEngine;
using UnityEngine.EventSystems;

public sealed class HamsterMotorShellTraversalAdapter : MonoBehaviour
{
    private const string TargetRootName = "Hamster_JointFreeMotorShell_MainScenes";
    private const string MotorShellBodyName = "MotorShellBody";
    private const string PlayerCameraName = "PlayerCamera";
    private const string TraversalReason = "TraversalGlide";
    private const string TraversalWallReason = "TraversalWall";
    private const float GameStateResolveRetryInterval = 0.5f;

    private enum TraversalState
    {
        Normal,
        WallContact,
        WallClinging,
        WallClimbing,
        WallJumping,
        Gliding,
        Cooldown
    }

    private enum WallMoveDirection
    {
        None,
        Up,
        Down,
        Left,
        Right
    }

    [Header("References")]
    [SerializeField] private HamsterFullRagdollMotor motor;
    [SerializeField] private Rigidbody bodyRigidbody;
    [SerializeField] private Transform bodyTransform;
    [SerializeField] private Camera ownerCamera;
    [SerializeField] private HamsterMotorShellRagdollRecoveryAdapter recoveryAdapter;
    [SerializeField] private HamsterMotorShellSpinDashAdapter spinDashAdapter;
    [SerializeField] private HamsterVisualClipStateDriver visualClipStateDriver;
    [SerializeField] private bool autoFindReferences = true;

    [Header("Glide")]
    [SerializeField] private bool enableGlide = true;
    [SerializeField] private KeyCode glideKey = KeyCode.Space;
    [SerializeField] private float glideMinAirTimeAfterJump = 0.2f;
    [SerializeField] private float maxGlideDuration = 1.5f;
    [SerializeField] private float glideCooldown = 0.25f;
    [SerializeField] private float maxGlideFallSpeed = 4.0f;
    [SerializeField] private float glideLiftAcceleration = 12f;
    [SerializeField] private float glideForwardAcceleration = 2.0f;
    [SerializeField] private float glideMovementControlScale = 0.55f;
    [SerializeField] private float glideUprightControlScale = 0.5f;
    [SerializeField] private bool requireFallingOrLowVerticalSpeed = true;
    [SerializeField] private float maxVerticalSpeedToStartGlide = 1.0f;
    [SerializeField] private bool endGlideOnGrounded = true;
    [SerializeField] private bool blockGlideDuringRecovery = true;
    [SerializeField] private bool blockGlideDuringLiquidSweep = true;
    [SerializeField] private bool blockGlideDuringSpinDashOrDizzy = true;

    [Header("Wall Climb")]
    [SerializeField] private bool enableWallClimb = true;
    [SerializeField] private bool allowWallClimbFromGround = true;
    [SerializeField] private bool requireWallInputToStart = true;
    [SerializeField] private KeyCode wallClimbHoldKey = KeyCode.W;
    [SerializeField] private KeyCode wallMoveDownKey = KeyCode.S;
    [SerializeField] private KeyCode wallMoveLeftKey = KeyCode.A;
    [SerializeField] private KeyCode wallMoveRightKey = KeyCode.D;
    [SerializeField] private KeyCode wallJumpKey = KeyCode.Space;
    [SerializeField] private LayerMask wallMask = ~0;
    [SerializeField] private float wallCheckDistance = 0.65f;
    [SerializeField] private float wallCheckRadius = 0.28f;
    [SerializeField] private float wallMinApproachDot = 0.35f;
    [SerializeField] private float wallMaxUpDot = 0.35f;
    [SerializeField] private float maxWallClimbDuration = 0f;
    [SerializeField] private float wallStickAcceleration = 10f;
    [SerializeField] private float wallClimbUpAcceleration = 12f;
    [SerializeField] private float wallMoveDownAcceleration = 7f;
    [SerializeField] private float wallMoveSideAcceleration = 8f;
    [SerializeField] private float wallSideMoveVerticalHoldAcceleration = 12f;
    [SerializeField] private float wallSideMoveMaxFallSpeed = 0.15f;
    [SerializeField] private float wallClingVerticalHoldAcceleration = 9.5f;
    [SerializeField] private float wallClingMaxFallSpeed = 0.35f;
    [SerializeField] private float wallSlideSlowAcceleration = 6f;
    [SerializeField] private float maxWallSlideFallSpeed = 2.5f;

    [Header("Wall Contact Stability")]
    [SerializeField] private float wallContactGraceTime = 0.22f;
    [SerializeField] private float wallReattachGraceTime = 0.6f;
    [SerializeField] private float wallReattachMaxDistance = 1.0f;
    [SerializeField] private bool useCachedWallNormalForReattach = true;
    [SerializeField] private bool keepWallTraversalDuringContactGrace = true;

    [Header("Wall Start Validation")]
    [SerializeField] private bool requireWallClimbHoldForStart = true;
    [SerializeField] private float wallStartMinApproachDot = 0.55f;
    [SerializeField] private float wallStartMaxUpDot = 0.2f;
    [SerializeField] private bool rejectLowWallStartHitsWhenGrounded = true;
    [SerializeField] private float wallStartMinHitPointYOffsetFromBody = -0.35f;

    [Header("Wall Vertical Speed Control")]
    [SerializeField] private bool useWallVerticalSpeedControl = true;
    [SerializeField] private float wallUpTargetVerticalSpeed = 2.2f;
    [SerializeField] private float wallDownTargetVerticalSpeed = -1.8f;
    [SerializeField] private float wallSideTargetVerticalSpeed = 0f;
    [SerializeField] private float wallIdleTargetVerticalSpeed = -0.05f;
    [SerializeField] private float wallVerticalSpeedControlGain = 12f;
    [SerializeField] private float wallVerticalGravityCompensation = 9.8f;
    [SerializeField] private float maxWallVerticalControlAcceleration = 35f;
    [SerializeField] private bool useRigidbodyVerticalSpeedForWallControl = true;

    [Header("Wall Side Speed Control")]
    [SerializeField] private bool useWallSideSpeedControl = true;
    [SerializeField] private float wallSideTargetSpeed = 2.2f;
    [SerializeField] private float wallSideSpeedControlGain = 14f;
    [SerializeField] private float maxWallSideControlAcceleration = 45f;
    [SerializeField] private bool useRigidbodySideSpeedForWallControl = true;

    [SerializeField] private float wallJumpUpVelocityChange = 5.5f;
    [SerializeField] private float wallJumpAwayVelocityChange = 4.5f;
    [SerializeField] private float wallJumpCooldown = 0.25f;
    [SerializeField] private float glideDelayAfterWallJump = 0.18f;

    [Header("Wall Jump Input Suppression")]
    [SerializeField] private float wallJumpSuppressNormalJumpDuration = 0.45f;
    [SerializeField] private bool holdJumpLockUntilWallJumpKeyRelease = true;
    [SerializeField] private bool clearTraversalJumpKeyOnWallJump = true;

    [SerializeField] private float wallMovementControlScale = 0.2f;
    [SerializeField] private float wallUprightControlScale = 0.4f;
    [SerializeField] private bool requireForwardInputForWallClimb = true;
    [SerializeField] private float wallMoveInputDeadzone = 0.1f;
    [SerializeField] private bool blockWallClimbDuringRecovery = true;
    [SerializeField] private bool blockWallClimbDuringLiquidSweep = true;
    [SerializeField] private bool blockWallClimbDuringSpinDashOrDizzy = true;
    [SerializeField] private bool debugWallTraversalLogs = false;

    [Header("Glide Animation")]
    [SerializeField] private bool useGlideAnimation = true;
    [SerializeField] private string glideAnimationStateName = "Glide";
    [SerializeField] private float glideAnimationCrossFade = 0.08f;

    [Header("Wall Animation")]
    [SerializeField] private bool useWallAnimation = true;
    [SerializeField] private string wallClingStateName = "WallCling";
    [SerializeField] private string wallMoveUpStateName = "WallMove_Up";
    [SerializeField] private string wallMoveDownStateName = "WallMove_Down";
    [SerializeField] private string wallMoveLeftStateName = "WallMove_Left";
    [SerializeField] private string wallMoveRightStateName = "WallMove_Right";
    [SerializeField] private float wallAnimationCrossFade = 0.08f;
    [SerializeField] private bool requireWallAnimationMotion = true;

    [Header("Debug")]
    [SerializeField] private bool debugTraversalLogs = false;

    private Transform _targetRoot;
    private GameStateManager _gameStateManager;
    private float _nextGameStateResolveTime = float.NegativeInfinity;
    private bool _requireTraversalInputReleaseBeforeRearm;
    private TraversalState _state;
    private float _lastGlideKeyDownTime = float.NegativeInfinity;
    private float _lastWallJumpKeyDownTime = float.NegativeInfinity;
    private float _lastWallJumpConsumedTime = float.NegativeInfinity;
    private float _glideStartedTime = float.NegativeInfinity;
    private float _wallStartedTime = float.NegativeInfinity;
    private float _wallJumpStartedTime = float.NegativeInfinity;
    private float _wallJumpSuppressNormalJumpUntil = float.NegativeInfinity;
    private float _glideBlockedUntil = float.NegativeInfinity;
    private float _cooldownUntil;
    private bool _legacyInputUnavailable;
    private NetworkObject _ownerNetworkObject;
    private bool _appliedMovementScale;
    private bool _appliedUprightScale;
    private bool _appliedWallMovementScale;
    private bool _appliedWallUprightScale;
    private bool _appliedWallJumpLock;
    private bool _glideAnimationActive;
    private bool _wallAnimationActive;
    private string _currentWallAnimationStateName = string.Empty;
    private Vector3 _currentWallNormal;
    private Vector3 _cachedWallNormal;
    private Vector3 _cachedWallPoint;
    private Collider _cachedWallCollider;
    private float _lastWallSeenTime = float.NegativeInfinity;
    private Vector2 _lastWallMoveInput;
    private WallMoveDirection _lastWallMoveDirection;
    private readonly RaycastHit[] _wallHits = new RaycastHit[8];
    private readonly Vector3[] _wallCheckDirections = new Vector3[6];

    public bool IsGliding => _state == TraversalState.Gliding;
    public bool IsWallClinging => _state == TraversalState.WallClinging;
    public bool IsWallClimbing => _state == TraversalState.WallClimbing;
    public bool IsWallJumping => _state == TraversalState.WallJumping;
    public bool IsWallTraversing => _state == TraversalState.WallContact ||
                                     _state == TraversalState.WallClinging ||
                                     _state == TraversalState.WallClimbing ||
                                     _state == TraversalState.WallJumping;
    public Vector3 CurrentWallNormal => _currentWallNormal;
    public string CurrentTraversalStateName => _state.ToString();

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
        EndGlide("OnDisable", false);
        EndWallTraversal("OnDisable", false);
        EndWallJump("OnDisable");
    }

    private void OnDestroy()
    {
        EndGlide("OnDestroy", false);
        EndWallTraversal("OnDestroy", false);
        EndWallJump("OnDestroy");
    }

    private void Update()
    {
        if (!IsTargetRoot())
            return;

        if (autoFindReferences)
            CacheReferences();

        if (IsGameplayPhaseLocked())
        {
            if (!_requireTraversalInputReleaseBeforeRearm)
                CancelTraversalForGameplayPhase();
            return;
        }

        if (!TryRearmTraversalInput())
            return;

        CaptureGlideKeyDown();
        CaptureWallJumpKeyDown();

        if (_state == TraversalState.Cooldown && Time.time >= _cooldownUntil)
            _state = TraversalState.Normal;

        if (_state == TraversalState.Gliding)
        {
            if (ShouldEndGlide(out string endReason))
            {
                EndGlide(endReason, true);
                return;
            }

            if (CanStartWallTraversal(out RaycastHit wallHit, out string wallBlockReason))
            {
                EndGlide("wall traversal", false);
                BeginWallTraversal(wallHit);
            }
            else if (debugWallTraversalLogs && !string.IsNullOrEmpty(wallBlockReason))
            {
                WallLog($"start blocked reason={wallBlockReason}");
            }

            return;
        }

        if (IsWallClinging || IsWallClimbing || _state == TraversalState.WallContact)
        {
            if (ShouldBeginWallJump())
            {
                BeginWallJump();
                return;
            }

            if (ShouldEndWallTraversal(out string wallEndReason))
            {
                EndWallTraversal(wallEndReason, false);
                return;
            }

            UpdateWallTraversalAnimation();

            return;
        }

        if (_state == TraversalState.WallJumping)
        {
            if (ShouldEndWallJump(out string wallJumpEndReason))
                EndWallJump(wallJumpEndReason);

            return;
        }

        if (_state == TraversalState.Normal)
        {
            if (CanStartWallTraversal(out RaycastHit wallHit, out string wallBlockReason))
            {
                BeginWallTraversal(wallHit);
                return;
            }
            else if (debugWallTraversalLogs && !string.IsNullOrEmpty(wallBlockReason))
            {
                WallLog($"start blocked reason={wallBlockReason}");
            }

            if (CanStartGlide(out string startBlockReason))
            {
                BeginGlide();
            }
            else if (debugTraversalLogs && !string.IsNullOrEmpty(startBlockReason))
            {
                Log($"start blocked reason={startBlockReason}");
            }
        }
    }

    private void FixedUpdate()
    {
        if (!IsTargetRoot())
            return;

        if (autoFindReferences)
            CacheReferences();

        if (IsGameplayPhaseLocked())
        {
            if (!_requireTraversalInputReleaseBeforeRearm)
                CancelTraversalForGameplayPhase();
            return;
        }

        if (_requireTraversalInputReleaseBeforeRearm)
            return;

        if (_state == TraversalState.Gliding)
        {
            if (ShouldEndGlide(out string endReason))
            {
                EndGlide(endReason, true);
                return;
            }

            ApplyGlideForces();
            return;
        }

        if (IsWallClinging || IsWallClimbing || _state == TraversalState.WallContact)
        {
            if (ShouldEndWallTraversal(out string wallEndReason))
            {
                EndWallTraversal(wallEndReason, false);
                return;
            }

            ApplyWallTraversalForces();
        }
    }

    private void CacheReferences()
    {
        _targetRoot = transform.root != null ? transform.root : transform;

        if (motor == null && _targetRoot != null)
            motor = _targetRoot.GetComponentInChildren<HamsterFullRagdollMotor>(true);

        if (_ownerNetworkObject == null && _targetRoot != null)
            _ownerNetworkObject = _targetRoot.GetComponent<NetworkObject>();
        if (_ownerNetworkObject == null)
            _ownerNetworkObject = GetComponentInParent<NetworkObject>();

        if (bodyRigidbody == null && motor != null)
            bodyRigidbody = motor.GetComponent<Rigidbody>();

        if (bodyRigidbody == null && _targetRoot != null)
        {
            Transform body = FindChildRecursive(_targetRoot, MotorShellBodyName);
            if (body != null)
                bodyRigidbody = body.GetComponent<Rigidbody>();
        }

        if (bodyTransform == null && bodyRigidbody != null)
            bodyTransform = bodyRigidbody.transform;

        if (ownerCamera == null && _targetRoot != null)
        {
            Transform cameraTransform = FindChildRecursive(_targetRoot, PlayerCameraName);
            if (cameraTransform != null)
                ownerCamera = cameraTransform.GetComponent<Camera>();
        }

        if (recoveryAdapter == null && _targetRoot != null)
            recoveryAdapter = _targetRoot.GetComponentInChildren<HamsterMotorShellRagdollRecoveryAdapter>(true);

        if (spinDashAdapter == null && _targetRoot != null)
            spinDashAdapter = _targetRoot.GetComponentInChildren<HamsterMotorShellSpinDashAdapter>(true);

        if (visualClipStateDriver == null && _targetRoot != null)
            visualClipStateDriver = _targetRoot.GetComponentInChildren<HamsterVisualClipStateDriver>(true);
    }

    private bool TryGetGameState(out GameStateManager.GameState state)
    {
        if (_gameStateManager == null)
        {
            if (Time.unscaledTime < _nextGameStateResolveTime)
            {
                state = default;
                return false;
            }

            _nextGameStateResolveTime = Time.unscaledTime + GameStateResolveRetryInterval;
            _gameStateManager = FindFirstObjectByType<GameStateManager>();
        }

        if (_gameStateManager == null)
        {
            state = default;
            return false;
        }

        state = _gameStateManager.GetState();
        return true;
    }

    private bool IsGameplayPhaseLocked()
    {
        return !TryGetGameState(out GameStateManager.GameState state) ||
               state != GameStateManager.GameState.Playing;
    }

    private void CancelTraversalForGameplayPhase()
    {
        switch (_state)
        {
            case TraversalState.Gliding:
                EndGlide("GameplayPhase", false);
                break;
            case TraversalState.WallContact:
            case TraversalState.WallClinging:
            case TraversalState.WallClimbing:
                EndWallTraversal("GameplayPhase", false);
                break;
            case TraversalState.WallJumping:
                EndWallJump("GameplayPhase");
                break;
            case TraversalState.Cooldown:
                _state = TraversalState.Normal;
                _cooldownUntil = 0f;
                break;
        }

        if (_glideAnimationActive || _appliedMovementScale || _appliedUprightScale)
            EndGlide("GameplayPhase", false);
        if (_wallAnimationActive || _appliedWallMovementScale || _appliedWallUprightScale)
            EndWallTraversal("GameplayPhase", false);
        if (_appliedWallJumpLock)
            EndWallJump("GameplayPhase");

        _state = TraversalState.Normal;
        _cooldownUntil = 0f;
        _lastGlideKeyDownTime = float.NegativeInfinity;
        _lastWallJumpKeyDownTime = float.NegativeInfinity;
        _lastWallJumpConsumedTime = float.NegativeInfinity;
        _glideStartedTime = float.NegativeInfinity;
        _wallStartedTime = float.NegativeInfinity;
        _wallJumpStartedTime = float.NegativeInfinity;
        _wallJumpSuppressNormalJumpUntil = float.NegativeInfinity;
        _glideBlockedUntil = float.NegativeInfinity;
        _lastWallSeenTime = float.NegativeInfinity;
        ClearWallState();
        _requireTraversalInputReleaseBeforeRearm = true;
    }

    private bool TryRearmTraversalInput()
    {
        if (!_requireTraversalInputReleaseBeforeRearm)
            return true;

        if (IsTraversalStartInputHeld())
            return false;

        _requireTraversalInputReleaseBeforeRearm = false;
        return false;
    }

    private bool IsTraversalStartInputHeld()
    {
        if (_legacyInputUnavailable || !CanReadDirectLocalInput())
            return true;

        if (enableGlide && ReadGlideHeld())
            return true;

        if (!enableWallClimb)
            return _legacyInputUnavailable;

        if (IsWallJumpKeyHeld())
            return true;

        if (requireWallClimbHoldForStart && ReadWallClimbHeld())
            return true;

        if ((requireWallInputToStart || requireForwardInputForWallClimb) &&
            TryReadWallMoveInput(out _))
        {
            return true;
        }

        return _legacyInputUnavailable;
    }

    private bool CanReadDirectLocalInput()
    {
        NetworkManager networkManager = NetworkManager.Singleton;
        if (networkManager == null || !networkManager.IsListening)
            return true;

        NetworkObject ownerNetworkObject = ResolveOwnerNetworkObject();
        if (ownerNetworkObject == null || !ownerNetworkObject.IsSpawned)
            return true;

        return ownerNetworkObject.OwnerClientId == networkManager.LocalClientId;
    }

    private NetworkObject ResolveOwnerNetworkObject()
    {
        if (_ownerNetworkObject != null)
            return _ownerNetworkObject;

        if (_targetRoot == null)
            _targetRoot = transform.root != null ? transform.root : transform;

        if (_targetRoot != null)
            _ownerNetworkObject = _targetRoot.GetComponent<NetworkObject>();
        if (_ownerNetworkObject == null)
            _ownerNetworkObject = GetComponentInParent<NetworkObject>();

        return _ownerNetworkObject;
    }

    private void CaptureGlideKeyDown()
    {
        if (_legacyInputUnavailable || IsUiInputBlocked() || !CanReadDirectLocalInput())
            return;

        try
        {
            if (Input.GetKeyDown(glideKey))
                _lastGlideKeyDownTime = Time.time;
        }
        catch (System.InvalidOperationException exception)
        {
            DisableLegacyInput(exception.Message);
        }
        catch (System.ArgumentException exception)
        {
            DisableLegacyInput(exception.Message);
        }
    }

    private void CaptureWallJumpKeyDown()
    {
        if (_legacyInputUnavailable || IsUiInputBlocked() || !CanReadDirectLocalInput())
            return;

        try
        {
            if (Input.GetKeyDown(wallJumpKey))
                _lastWallJumpKeyDownTime = Time.time;
        }
        catch (System.InvalidOperationException exception)
        {
            DisableLegacyInput(exception.Message);
        }
        catch (System.ArgumentException exception)
        {
            DisableLegacyInput(exception.Message);
        }
    }

    private bool CanStartGlide(out string blockReason)
    {
        blockReason = string.Empty;

        if (!enableGlide)
            return Block("disabled", out blockReason);

        if (_legacyInputUnavailable)
            return Block("legacy input unavailable", out blockReason);

        if (motor == null)
            return Block("motor missing", out blockReason);

        if (bodyRigidbody == null || bodyRigidbody.isKinematic)
            return Block(bodyRigidbody == null ? "body missing" : "body kinematic", out blockReason);

        if (motor.IsGrounded)
            return Block("grounded", out blockReason);

        if (!ReadGlideHeld())
            return Block("key not held", out blockReason);

        if (IsUiInputBlocked())
            return Block("ui input blocked", out blockReason);

        if (Time.time - _lastGlideKeyDownTime < Mathf.Max(0f, glideMinAirTimeAfterJump))
            return Block("min air time", out blockReason);

        if (Time.time < _glideBlockedUntil)
            return Block("glide delayed", out blockReason);

        if (requireFallingOrLowVerticalSpeed && motor.CurrentVerticalVelocity > maxVerticalSpeedToStartGlide)
            return Block("vertical speed too high", out blockReason);

        if (IsGlideBlockedByExternalState(out blockReason))
            return false;

        return true;
    }

    private bool ShouldEndGlide(out string reason)
    {
        reason = "none";

        if (motor == null)
        {
            reason = "motor missing";
            return true;
        }

        if (bodyRigidbody == null || bodyRigidbody.isKinematic)
        {
            reason = bodyRigidbody == null ? "body missing" : "body kinematic";
            return true;
        }

        if (!ReadGlideHeld())
        {
            reason = "key released";
            return true;
        }

        if (endGlideOnGrounded && motor.IsGrounded)
        {
            reason = "grounded";
            return true;
        }

        if (maxGlideDuration > 0f && Time.time - _glideStartedTime >= maxGlideDuration)
        {
            reason = "max duration";
            return true;
        }

        return IsGlideBlockedByExternalState(out reason);
    }

    private bool IsGlideBlockedByExternalState(out string reason)
    {
        reason = string.Empty;

        if (blockGlideDuringRecovery &&
            recoveryAdapter != null &&
            recoveryAdapter.CurrentRecoveryState != HamsterMotorShellRagdollRecoveryAdapter.RecoveryState.Normal)
        {
            reason = $"recovery {recoveryAdapter.CurrentRecoveryState}";
            return true;
        }

        if (blockGlideDuringLiquidSweep && recoveryAdapter != null && recoveryAdapter.IsLiquidSwept)
        {
            reason = "liquid sweep";
            return true;
        }

        if (blockGlideDuringSpinDashOrDizzy && spinDashAdapter != null && spinDashAdapter.IsDizzyActive)
        {
            reason = "dizzy";
            return true;
        }

        if (blockGlideDuringRecovery &&
            (IsBlockedExternalReason(motor != null ? motor.ExternalControlLockReason : null) ||
             IsBlockedExternalReason(motor != null ? motor.ExternalJumpLockReason : null)))
        {
            reason = "external lock";
            return true;
        }

        if (blockGlideDuringSpinDashOrDizzy &&
            (IsBlockedExternalReason(motor != null ? motor.ExternalMovementControlReason : null) ||
             IsBlockedExternalReason(motor != null ? motor.ExternalUprightControlReason : null) ||
             IsBlockedExternalReason(motor != null ? motor.ExternalPoseControlReason : null)))
        {
            reason = "external scale";
            return true;
        }

        return false;
    }

    private bool CanStartWallTraversal(out RaycastHit wallHit, out string blockReason)
    {
        wallHit = default(RaycastHit);
        blockReason = string.Empty;

        if (!enableWallClimb)
            return Block("disabled", out blockReason);

        if (_legacyInputUnavailable)
            return Block("legacy input unavailable", out blockReason);

        if (motor == null)
            return Block("motor missing", out blockReason);

        if (bodyRigidbody == null || bodyRigidbody.isKinematic)
            return Block(bodyRigidbody == null ? "body missing" : "body kinematic", out blockReason);

        if (motor.IsGrounded && !allowWallClimbFromGround)
            return Block("grounded", out blockReason);

        if (IsUiInputBlocked())
            return Block("ui input blocked", out blockReason);

        if (requireWallClimbHoldForStart && !ReadWallClimbHeld())
            return Block("wall climb key missing", out blockReason);

        if ((requireWallInputToStart || requireForwardInputForWallClimb) &&
            !TryReadWallMoveInput(out Vector2 startInput))
        {
            return Block("wall input missing", out blockReason);
        }

        if (IsWallBlockedByExternalState(out blockReason))
            return false;

        if (!TryFindWallForStart(out wallHit, out blockReason))
            return false;

        return true;
    }

    private bool ShouldEndWallTraversal(out string reason)
    {
        reason = "none";

        if (motor == null)
        {
            reason = "motor missing";
            return true;
        }

        if (bodyRigidbody == null || bodyRigidbody.isKinematic)
        {
            reason = bodyRigidbody == null ? "body missing" : "body kinematic";
            return true;
        }

        if (motor.IsGrounded && !allowWallClimbFromGround)
        {
            reason = "grounded";
            return true;
        }

        if (maxWallClimbDuration > 0f && Time.time - _wallStartedTime >= maxWallClimbDuration)
        {
            reason = "max duration";
            return true;
        }

        if (IsWallBlockedByExternalState(out reason))
            return true;

        if (!TryFindWallForMaintain(out RaycastHit wallHit, out reason))
        {
            if (CanKeepWallTraversalDuringContactGrace(out string graceReason))
            {
                reason = graceReason;
                return false;
            }

            return true;
        }

        SetCurrentWall(wallHit);
        return false;
    }

    private bool IsWallBlockedByExternalState(out string reason)
    {
        reason = string.Empty;

        if (blockWallClimbDuringRecovery &&
            recoveryAdapter != null &&
            recoveryAdapter.CurrentRecoveryState != HamsterMotorShellRagdollRecoveryAdapter.RecoveryState.Normal)
        {
            reason = $"recovery {recoveryAdapter.CurrentRecoveryState}";
            return true;
        }

        if (blockWallClimbDuringLiquidSweep && recoveryAdapter != null && recoveryAdapter.IsLiquidSwept)
        {
            reason = "liquid sweep";
            return true;
        }

        if (blockWallClimbDuringSpinDashOrDizzy && spinDashAdapter != null && spinDashAdapter.IsDizzyActive)
        {
            reason = "dizzy";
            return true;
        }

        if (blockWallClimbDuringRecovery &&
            (IsBlockedExternalReason(motor != null ? motor.ExternalControlLockReason : null) ||
             IsBlockedExternalReason(motor != null ? motor.ExternalJumpLockReason : null)))
        {
            reason = "external lock";
            return true;
        }

        if (blockWallClimbDuringSpinDashOrDizzy &&
            (IsBlockedExternalReason(motor != null ? motor.ExternalMovementControlReason : null) ||
             IsBlockedExternalReason(motor != null ? motor.ExternalUprightControlReason : null) ||
             IsBlockedExternalReason(motor != null ? motor.ExternalPoseControlReason : null)))
        {
            reason = "external scale";
            return true;
        }

        return false;
    }

    private void BeginGlide()
    {
        _state = TraversalState.Gliding;
        _glideStartedTime = Time.time;

        ApplyGlideMotorControl();
        TryBeginGlideAnimation();
        Log("started");
    }

    private void EndGlide(string reason, bool enterCooldown)
    {
        if (_state != TraversalState.Gliding && !_glideAnimationActive && !_appliedMovementScale && !_appliedUprightScale)
            return;

        EndGlideAnimation();
        RestoreMotorControlIfOwned();
        _glideStartedTime = float.NegativeInfinity;

        if (enterCooldown && glideCooldown > 0f)
        {
            _state = TraversalState.Cooldown;
            _cooldownUntil = Time.time + Mathf.Max(0f, glideCooldown);
        }
        else
        {
            _state = TraversalState.Normal;
            _cooldownUntil = 0f;
        }

        Log($"ended reason={reason} cooldown={enterCooldown}");
    }

    private void ApplyGlideMotorControl()
    {
        if (motor == null)
            return;

        motor.SetExternalMovementControlScale(Mathf.Clamp01(glideMovementControlScale), TraversalReason);
        motor.SetExternalUprightControlScale(Mathf.Clamp01(glideUprightControlScale), TraversalReason);
        _appliedMovementScale = true;
        _appliedUprightScale = true;
    }

    private void RestoreMotorControlIfOwned()
    {
        if (motor == null)
        {
            _appliedMovementScale = false;
            _appliedUprightScale = false;
            return;
        }

        if (_appliedMovementScale && IsGlideTraversalReason(motor.ExternalMovementControlReason))
            motor.SetExternalMovementControlScale(1f, "TraversalGlide:Restore");

        if (_appliedUprightScale && IsGlideTraversalReason(motor.ExternalUprightControlReason))
            motor.SetExternalUprightControlScale(1f, "TraversalGlide:Restore");

        _appliedMovementScale = false;
        _appliedUprightScale = false;
    }

    private void BeginWallTraversal(RaycastHit wallHit)
    {
        if (_state == TraversalState.Gliding)
            EndGlide("wall traversal", false);

        SetCurrentWall(wallHit);
        _state = wallClimbUpAcceleration > 0f ? TraversalState.WallClimbing : TraversalState.WallClinging;
        _wallStartedTime = Time.time;
        ApplyWallMotorControl();
        UpdateWallTraversalAnimation();
        WallLog($"started normal={FormatVector3(_currentWallNormal)}");
    }

    private void EndWallTraversal(string reason, bool enterCooldown)
    {
        if (!IsWallClinging &&
            !IsWallClimbing &&
            _state != TraversalState.WallContact &&
            !_wallAnimationActive &&
            !_appliedWallMovementScale &&
            !_appliedWallUprightScale &&
            !_appliedWallJumpLock)
        {
            return;
        }

        EndWallAnimation();
        RestoreWallMotorControlIfOwned(true);
        ClearWallState();

        if (enterCooldown && wallJumpCooldown > 0f)
        {
            _state = TraversalState.Cooldown;
            _cooldownUntil = Time.time + Mathf.Max(0f, wallJumpCooldown);
        }
        else
        {
            _state = TraversalState.Normal;
        }

        WallLog($"ended reason={reason} cooldown={enterCooldown}");
    }

    private bool ShouldBeginWallJump()
    {
        if (_legacyInputUnavailable || bodyRigidbody == null || bodyRigidbody.isKinematic)
            return false;

        return _lastWallJumpKeyDownTime > _wallStartedTime &&
               _lastWallJumpKeyDownTime > _lastWallJumpConsumedTime;
    }

    private void BeginWallJump()
    {
        Vector3 wallNormal = TryNormalizePlanar(_currentWallNormal, out Vector3 planarNormal)
            ? planarNormal
            : _currentWallNormal;

        if (!IsFiniteVector(wallNormal) || wallNormal.sqrMagnitude <= 0.0001f)
            wallNormal = bodyTransform != null ? -bodyTransform.forward : Vector3.back;

        wallNormal = wallNormal.normalized;
        _lastWallJumpConsumedTime = _lastWallJumpKeyDownTime;
        if (clearTraversalJumpKeyOnWallJump)
            _lastWallJumpKeyDownTime = float.NegativeInfinity;

        _wallJumpStartedTime = Time.time;
        _wallJumpSuppressNormalJumpUntil = Time.time + Mathf.Max(0f, wallJumpSuppressNormalJumpDuration);
        _glideBlockedUntil = Time.time + Mathf.Max(0f, glideDelayAfterWallJump);

        EndWallAnimation();
        RestoreWallMotorControlIfOwned(false);
        ApplyWallJumpLock();
        ClearWallState();
        _state = TraversalState.WallJumping;

        Vector3 wallJumpForce =
            Vector3.up * Mathf.Max(0f, wallJumpUpVelocityChange) +
            wallNormal * Mathf.Max(0f, wallJumpAwayVelocityChange);

        if (bodyRigidbody != null && !bodyRigidbody.isKinematic && IsFiniteVector(wallJumpForce))
            bodyRigidbody.AddForce(wallJumpForce, ForceMode.VelocityChange);

        WallLog($"wall jump normal={FormatVector3(wallNormal)} force={FormatVector3(wallJumpForce)}");
    }

    private void EndWallJump(string reason)
    {
        if (_state != TraversalState.WallJumping && !_appliedWallJumpLock)
            return;

        RestoreWallMotorControlIfOwned(true);
        _wallJumpStartedTime = float.NegativeInfinity;
        _wallJumpSuppressNormalJumpUntil = float.NegativeInfinity;
        _state = TraversalState.Normal;
        WallLog($"wall jump ended reason={reason}");
    }

    private bool ShouldEndWallJump(out string reason)
    {
        reason = string.Empty;

        if (_state != TraversalState.WallJumping)
            return false;

        if (Time.time - _wallJumpStartedTime < Mathf.Max(0f, wallJumpCooldown))
            return false;

        if (Time.time >= _wallJumpSuppressNormalJumpUntil)
        {
            reason = "normal jump suppress elapsed";
            return true;
        }

        if (holdJumpLockUntilWallJumpKeyRelease && !IsWallJumpKeyHeld())
        {
            reason = "wall jump key released";
            return true;
        }

        return false;
    }

    private void ApplyWallMotorControl()
    {
        if (motor == null)
            return;

        motor.SetExternalMovementControlScale(Mathf.Clamp01(wallMovementControlScale), TraversalWallReason);
        motor.SetExternalUprightControlScale(Mathf.Clamp01(wallUprightControlScale), TraversalWallReason);
        ApplyWallJumpLock();
        _appliedWallMovementScale = true;
        _appliedWallUprightScale = true;
    }

    private void ApplyWallJumpLock()
    {
        if (motor == null)
            return;

        motor.SetExternalJumpLock(true, TraversalWallReason);
        _appliedWallJumpLock = true;
    }

    private void RestoreWallMotorControlIfOwned(bool restoreJumpLock)
    {
        if (motor == null)
        {
            _appliedWallMovementScale = false;
            _appliedWallUprightScale = false;
            if (restoreJumpLock)
                _appliedWallJumpLock = false;
            return;
        }

        if (_appliedWallMovementScale && IsWallTraversalReason(motor.ExternalMovementControlReason))
            motor.SetExternalMovementControlScale(1f, "TraversalWall:Restore");

        if (_appliedWallUprightScale && IsWallTraversalReason(motor.ExternalUprightControlReason))
            motor.SetExternalUprightControlScale(1f, "TraversalWall:Restore");

        if (restoreJumpLock && _appliedWallJumpLock && IsWallTraversalReason(motor.ExternalJumpLockReason))
            motor.SetExternalJumpLock(false, "TraversalWall:Restore");

        _appliedWallMovementScale = false;
        _appliedWallUprightScale = false;
        if (restoreJumpLock)
            _appliedWallJumpLock = false;
    }

    private void ApplyGlideForces()
    {
        if (bodyRigidbody == null || bodyRigidbody.isKinematic)
            return;

        float verticalVelocity = motor != null ? motor.CurrentVerticalVelocity : bodyRigidbody.linearVelocity.y;
        if (verticalVelocity < -Mathf.Max(0.01f, maxGlideFallSpeed))
            bodyRigidbody.AddForce(Vector3.up * Mathf.Max(0f, glideLiftAcceleration), ForceMode.Acceleration);

        if (glideForwardAcceleration <= 0f)
            return;

        if (TryGetGlideForward(out Vector3 forward))
            bodyRigidbody.AddForce(forward * Mathf.Max(0f, glideForwardAcceleration), ForceMode.Acceleration);
    }

    private void ApplyWallTraversalForces()
    {
        if (bodyRigidbody == null || bodyRigidbody.isKinematic || !TryNormalizePlanar(_currentWallNormal, out Vector3 wallNormal))
            return;

        bool hasWallMoveInput = TryReadWallMoveInput(out Vector2 wallMoveInput);
        _lastWallMoveInput = wallMoveInput;
        _lastWallMoveDirection = SelectWallMoveDirection(wallMoveInput);
        _state = hasWallMoveInput ? TraversalState.WallClimbing : TraversalState.WallClinging;

        Vector3 intoWall = -wallNormal;
        if (wallStickAcceleration > 0f)
            bodyRigidbody.AddForce(intoWall * Mathf.Max(0f, wallStickAcceleration), ForceMode.Acceleration);

        if (hasWallMoveInput && TryGetWallRight(wallNormal, out Vector3 wallRight))
        {
            Vector3 moveForce = Vector3.zero;
            if (wallMoveInput.x != 0f && !useWallSideSpeedControl)
                moveForce += wallRight * (wallMoveInput.x * Mathf.Max(0f, wallMoveSideAcceleration));

            if (wallMoveInput.x != 0f && useWallSideSpeedControl)
                ApplyWallSideSpeedControl(wallMoveInput.x, wallRight);

            if (!useWallVerticalSpeedControl)
            {
                if (wallMoveInput.y > 0f)
                    moveForce += Vector3.up * (wallMoveInput.y * Mathf.Max(0f, wallClimbUpAcceleration));
                else if (wallMoveInput.y < 0f)
                    moveForce += Vector3.down * (-wallMoveInput.y * Mathf.Max(0f, wallMoveDownAcceleration));
            }

            if (IsFiniteVector(moveForce) && moveForce.sqrMagnitude > 0.0001f)
                bodyRigidbody.AddForce(moveForce, ForceMode.Acceleration);
        }

        float verticalVelocity = GetWallControlVerticalSpeed();
        if (useWallVerticalSpeedControl)
            ApplyWallVerticalSpeedControl(wallMoveInput, verticalVelocity);
        else
            ApplyWallVerticalHold(wallMoveInput, verticalVelocity);

        if (verticalVelocity < -Mathf.Max(0.01f, maxWallSlideFallSpeed) && wallSlideSlowAcceleration > 0f)
            bodyRigidbody.AddForce(Vector3.up * Mathf.Max(0f, wallSlideSlowAcceleration), ForceMode.Acceleration);
    }

    private float GetWallControlVerticalSpeed()
    {
        if (useRigidbodyVerticalSpeedForWallControl && bodyRigidbody != null)
            return bodyRigidbody.linearVelocity.y;

        return motor != null ? motor.CurrentVerticalVelocity : (bodyRigidbody != null ? bodyRigidbody.linearVelocity.y : 0f);
    }

    private void ApplyWallVerticalSpeedControl(Vector2 wallMoveInput, float verticalVelocity)
    {
        float targetVerticalSpeed = SelectWallTargetVerticalSpeed(wallMoveInput);
        float speedError = targetVerticalSpeed - verticalVelocity;
        float acceleration = speedError * Mathf.Max(0f, wallVerticalSpeedControlGain) + wallVerticalGravityCompensation;
        acceleration = Mathf.Clamp(
            acceleration,
            -Mathf.Max(0f, maxWallVerticalControlAcceleration),
            Mathf.Max(0f, maxWallVerticalControlAcceleration));

        if (Mathf.Abs(acceleration) > 0.001f)
            bodyRigidbody.AddForce(Vector3.up * acceleration, ForceMode.Acceleration);
    }

    private float SelectWallTargetVerticalSpeed(Vector2 wallMoveInput)
    {
        if (wallMoveInput.y > wallMoveInputDeadzone)
            return wallUpTargetVerticalSpeed;

        if (wallMoveInput.y < -wallMoveInputDeadzone)
            return wallDownTargetVerticalSpeed;

        if (Mathf.Abs(wallMoveInput.x) > wallMoveInputDeadzone)
            return wallSideTargetVerticalSpeed;

        return wallIdleTargetVerticalSpeed;
    }

    private void ApplyWallSideSpeedControl(float sideInput, Vector3 wallRight)
    {
        float targetSideSpeed = Mathf.Clamp(sideInput, -1f, 1f) * Mathf.Max(0f, wallSideTargetSpeed);
        float currentSideSpeed = GetWallControlSideSpeed(wallRight);
        float speedError = targetSideSpeed - currentSideSpeed;
        float acceleration = speedError * Mathf.Max(0f, wallSideSpeedControlGain);
        acceleration = Mathf.Clamp(
            acceleration,
            -Mathf.Max(0f, maxWallSideControlAcceleration),
            Mathf.Max(0f, maxWallSideControlAcceleration));

        if (Mathf.Abs(acceleration) > 0.001f)
            bodyRigidbody.AddForce(wallRight * acceleration, ForceMode.Acceleration);
    }

    private float GetWallControlSideSpeed(Vector3 wallRight)
    {
        if (bodyRigidbody != null && useRigidbodySideSpeedForWallControl)
            return Vector3.Dot(bodyRigidbody.linearVelocity, wallRight);

        if (motor != null)
            return Vector3.Dot(motor.CurrentPlanarVelocity, wallRight);

        return bodyRigidbody != null ? Vector3.Dot(bodyRigidbody.linearVelocity, wallRight) : 0f;
    }

    private void ApplyWallVerticalHold(Vector2 wallMoveInput, float verticalVelocity)
    {
        if (wallMoveInput.y < -wallMoveInputDeadzone)
            return;

        bool horizontalOnly = Mathf.Abs(wallMoveInput.x) > wallMoveInputDeadzone &&
                              Mathf.Abs(wallMoveInput.y) <= wallMoveInputDeadzone;
        bool noMoveInput = wallMoveInput.sqrMagnitude <= wallMoveInputDeadzone * wallMoveInputDeadzone;

        if (horizontalOnly)
        {
            ApplyWallFallLimit(verticalVelocity, wallSideMoveMaxFallSpeed, wallSideMoveVerticalHoldAcceleration);
            return;
        }

        if (noMoveInput)
            ApplyWallFallLimit(verticalVelocity, wallClingMaxFallSpeed, wallClingVerticalHoldAcceleration);
    }

    private void ApplyWallFallLimit(float verticalVelocity, float maxFallSpeed, float holdAcceleration)
    {
        if (holdAcceleration <= 0f)
            return;

        if (verticalVelocity < -Mathf.Max(0.01f, maxFallSpeed))
            bodyRigidbody.AddForce(Vector3.up * Mathf.Max(0f, holdAcceleration), ForceMode.Acceleration);
    }

    private void TryBeginGlideAnimation()
    {
        if (!useGlideAnimation || visualClipStateDriver == null || string.IsNullOrWhiteSpace(glideAnimationStateName))
            return;

        if (visualClipStateDriver.TryBeginExternalSustainedState(
                TraversalReason,
                glideAnimationStateName,
                glideAnimationCrossFade,
                true,
                out string failureReason))
        {
            _glideAnimationActive = true;
            return;
        }

        Log($"animation skipped reason={failureReason}");
    }

    private void EndGlideAnimation()
    {
        if (!_glideAnimationActive || visualClipStateDriver == null)
        {
            _glideAnimationActive = false;
            return;
        }

        visualClipStateDriver.EndExternalSustainedState(TraversalReason);
        _glideAnimationActive = false;
    }

    private void UpdateWallTraversalAnimation()
    {
        if (!useWallAnimation || visualClipStateDriver == null)
            return;

        TryReadWallMoveInput(out Vector2 wallMoveInput);
        _lastWallMoveInput = wallMoveInput;
        _lastWallMoveDirection = SelectWallMoveDirection(wallMoveInput);
        if (_state == TraversalState.WallContact || IsWallClinging || IsWallClimbing)
            _state = _lastWallMoveDirection == WallMoveDirection.None
                ? TraversalState.WallClinging
                : TraversalState.WallClimbing;

        string stateName = GetWallAnimationStateName(_lastWallMoveDirection);
        if (string.IsNullOrWhiteSpace(stateName))
        {
            EndWallAnimation();
            return;
        }

        if (_wallAnimationActive &&
            _currentWallAnimationStateName == stateName &&
            visualClipStateDriver.IsExternalSustainedStateActive(TraversalWallReason))
        {
            return;
        }

        if (visualClipStateDriver.TryBeginExternalSustainedState(
                TraversalWallReason,
                stateName,
                wallAnimationCrossFade,
                requireWallAnimationMotion,
                out string failureReason))
        {
            _wallAnimationActive = true;
            _currentWallAnimationStateName = stateName;
            return;
        }

        WallLog($"animation skipped state={stateName} reason={failureReason}");
    }

    private string GetWallAnimationStateName(WallMoveDirection direction)
    {
        switch (direction)
        {
            case WallMoveDirection.Up:
                return wallMoveUpStateName;
            case WallMoveDirection.Down:
                return wallMoveDownStateName;
            case WallMoveDirection.Left:
                return wallMoveLeftStateName;
            case WallMoveDirection.Right:
                return wallMoveRightStateName;
            default:
                return wallClingStateName;
        }
    }

    private void EndWallAnimation()
    {
        if (!_wallAnimationActive || visualClipStateDriver == null)
        {
            _wallAnimationActive = false;
            _currentWallAnimationStateName = string.Empty;
            return;
        }

        visualClipStateDriver.EndExternalSustainedState(TraversalWallReason);
        _wallAnimationActive = false;
        _currentWallAnimationStateName = string.Empty;
    }

    private bool ReadGlideHeld()
    {
        if (!CanReadDirectLocalInput())
            return false;

        if (_legacyInputUnavailable)
            return false;

        try
        {
            return Input.GetKey(glideKey);
        }
        catch (System.InvalidOperationException exception)
        {
            DisableLegacyInput(exception.Message);
        }
        catch (System.ArgumentException exception)
        {
            DisableLegacyInput(exception.Message);
        }

        return false;
    }

    private bool ReadWallClimbHeld()
    {
        if (!CanReadDirectLocalInput())
            return false;

        if (_legacyInputUnavailable)
            return false;

        try
        {
            return Input.GetKey(wallClimbHoldKey);
        }
        catch (System.InvalidOperationException exception)
        {
            DisableLegacyInput(exception.Message);
        }
        catch (System.ArgumentException exception)
        {
            DisableLegacyInput(exception.Message);
        }

        return false;
    }

    private bool TryReadWallMoveInput(out Vector2 input)
    {
        input = Vector2.zero;

        if (!CanReadDirectLocalInput())
            return false;

        if (_legacyInputUnavailable)
            return false;

        try
        {
            if (Input.GetKey(wallMoveLeftKey))
                input.x -= 1f;

            if (Input.GetKey(wallMoveRightKey))
                input.x += 1f;

            if (Input.GetKey(wallClimbHoldKey))
                input.y += 1f;

            if (Input.GetKey(wallMoveDownKey))
                input.y -= 1f;
        }
        catch (System.InvalidOperationException exception)
        {
            DisableLegacyInput(exception.Message);
            input = Vector2.zero;
            return false;
        }
        catch (System.ArgumentException exception)
        {
            DisableLegacyInput(exception.Message);
            input = Vector2.zero;
            return false;
        }

        if (input.sqrMagnitude <= wallMoveInputDeadzone * wallMoveInputDeadzone)
        {
            input = Vector2.zero;
            return false;
        }

        input = Vector2.ClampMagnitude(input, 1f);
        return true;
    }

    private bool TryGetGlideForward(out Vector3 forward)
    {
        if (motor != null && TryNormalizePlanar(motor.SmoothedMoveWorldDirection, out forward))
            return true;

        if (ownerCamera != null && TryNormalizePlanar(ownerCamera.transform.forward, out forward))
            return true;

        if (bodyTransform != null && TryNormalizePlanar(bodyTransform.forward, out forward))
            return true;

        forward = Vector3.zero;
        return false;
    }

    private bool TryFindWallForStart(out RaycastHit wallHit, out string failureReason)
    {
        wallHit = default;
        failureReason = string.Empty;

        int directionCount = BuildWallStartCheckDirections(_wallCheckDirections);
        if (directionCount <= 0)
            return Block("wall start direction missing", out failureReason);

        string lastFailureReason = string.Empty;
        for (int i = 0; i < directionCount; i++)
        {
            if (TryFindWallInDirection(_wallCheckDirections[i], true, out wallHit, out lastFailureReason))
                return true;
        }

        return Block(string.IsNullOrEmpty(lastFailureReason) ? "wall start missing" : lastFailureReason, out failureReason);
    }

    private bool TryFindWallForMaintain(out RaycastHit wallHit, out string failureReason)
    {
        wallHit = default;
        failureReason = string.Empty;

        int directionCount = BuildWallCheckDirections(_wallCheckDirections);
        if (directionCount <= 0)
            return Block("wall direction missing", out failureReason);

        string lastFailureReason = string.Empty;
        for (int i = 0; i < directionCount; i++)
        {
            if (TryFindWallInDirection(_wallCheckDirections[i], false, out wallHit, out lastFailureReason))
                return true;
        }

        return Block(string.IsNullOrEmpty(lastFailureReason) ? "wall missing" : lastFailureReason, out failureReason);
    }

    private bool TryFindWallInDirection(Vector3 checkDirection, bool forStart, out RaycastHit wallHit, out string failureReason)
    {
        wallHit = default;
        failureReason = string.Empty;

        if (!TryNormalizePlanar(checkDirection, out checkDirection))
            return Block("wall direction missing", out failureReason);

        Vector3 origin = GetWallCheckOrigin();

        int hitCount = Physics.SphereCastNonAlloc(
            origin,
            Mathf.Max(0.01f, wallCheckRadius),
            checkDirection,
            _wallHits,
            Mathf.Max(0.01f, wallCheckDistance),
            wallMask,
            QueryTriggerInteraction.Ignore);

        if (hitCount <= 0)
            return Block("wall missing", out failureReason);

        float bestDistance = float.PositiveInfinity;
        bool rejectedSelf = false;
        bool rejectedOtherPlayer = false;
        bool rejectedSlope = false;
        bool rejectedApproach = false;
        bool rejectedLowStartHit = false;
        float maxUpDot = Mathf.Clamp01(forStart ? wallStartMaxUpDot : wallMaxUpDot);
        float minApproachDot = Mathf.Clamp(forStart ? wallStartMinApproachDot : wallMinApproachDot, -1f, 1f);

        for (int i = 0; i < hitCount; i++)
        {
            RaycastHit hit = _wallHits[i];
            if (hit.collider == null)
                continue;

            if (IsOwnCollider(hit.collider))
            {
                rejectedSelf = true;
                continue;
            }

            if (IsOtherPlayerCollider(hit.collider))
            {
                rejectedOtherPlayer = true;
                continue;
            }

            if (!IsFiniteVector(hit.normal) || hit.normal.sqrMagnitude <= 0.0001f)
                continue;

            Vector3 hitNormal = hit.normal.normalized;
            float upDot = Mathf.Abs(Vector3.Dot(hitNormal, Vector3.up));
            if (upDot > maxUpDot)
            {
                rejectedSlope = true;
                continue;
            }

            float approachDot = Vector3.Dot(checkDirection, -hitNormal);
            if (approachDot < minApproachDot)
            {
                rejectedApproach = true;
                continue;
            }

            if (forStart && !IsWallStartHitHeightAllowed(hit))
            {
                rejectedLowStartHit = true;
                continue;
            }

            if (hit.distance < bestDistance)
            {
                bestDistance = hit.distance;
                wallHit = hit;
            }
        }

        if (wallHit.collider != null)
            return true;

        if (rejectedSelf)
            return Block("own collider", out failureReason);

        if (rejectedOtherPlayer)
            return Block("other player collider", out failureReason);

        if (rejectedSlope)
            return Block("wall slope rejected", out failureReason);

        if (rejectedApproach)
            return Block("approach dot rejected", out failureReason);

        if (rejectedLowStartHit)
            return Block("wall start low hit rejected", out failureReason);

        return Block("wall rejected", out failureReason);
    }

    private int BuildWallStartCheckDirections(Vector3[] directions)
    {
        if (directions == null || directions.Length == 0)
            return 0;

        int count = 0;

        if (motor != null && TryNormalizePlanar(motor.DesiredFacingDirection, out Vector3 desiredFacingDirection))
            AddUniqueWallCheckDirection(directions, ref count, desiredFacingDirection);

        if (bodyTransform != null && TryNormalizePlanar(bodyTransform.forward, out Vector3 bodyForward))
            AddUniqueWallCheckDirection(directions, ref count, bodyForward);

        if (ownerCamera != null && TryNormalizePlanar(ownerCamera.transform.forward, out Vector3 cameraForward))
            AddUniqueWallCheckDirection(directions, ref count, cameraForward);

        if (motor != null && TryNormalizePlanar(motor.SmoothedMoveWorldDirection, out Vector3 moveDirection))
            AddUniqueWallCheckDirection(directions, ref count, moveDirection);

        return count;
    }

    private int BuildWallCheckDirections(Vector3[] directions)
    {
        if (directions == null || directions.Length == 0)
            return 0;

        int count = 0;

        if (TryNormalizePlanar(-_currentWallNormal, out Vector3 currentWallDirection))
            AddUniqueWallCheckDirection(directions, ref count, currentWallDirection);

        if (useCachedWallNormalForReattach &&
            HasUsableWallCache(Mathf.Max(0f, wallReattachGraceTime), Mathf.Max(0f, wallReattachMaxDistance)) &&
            TryNormalizePlanar(-_cachedWallNormal, out Vector3 cachedWallDirection))
        {
            AddUniqueWallCheckDirection(directions, ref count, cachedWallDirection);
        }

        if (motor != null && TryNormalizePlanar(motor.DesiredFacingDirection, out Vector3 desiredFacingDirection))
            AddUniqueWallCheckDirection(directions, ref count, desiredFacingDirection);

        if (bodyTransform != null && TryNormalizePlanar(bodyTransform.forward, out Vector3 bodyForward))
            AddUniqueWallCheckDirection(directions, ref count, bodyForward);

        if (ownerCamera != null && TryNormalizePlanar(ownerCamera.transform.forward, out Vector3 cameraForward))
            AddUniqueWallCheckDirection(directions, ref count, cameraForward);

        if (motor != null && TryNormalizePlanar(motor.SmoothedMoveWorldDirection, out Vector3 moveDirection))
            AddUniqueWallCheckDirection(directions, ref count, moveDirection);

        return count;
    }

    private bool IsWallStartHitHeightAllowed(RaycastHit hit)
    {
        if (!rejectLowWallStartHitsWhenGrounded || motor == null || !motor.IsGrounded)
            return true;

        if (!IsFiniteVector(hit.point))
            return false;

        float yOffsetFromBody = hit.point.y - GetWallCheckOrigin().y;
        return yOffsetFromBody >= wallStartMinHitPointYOffsetFromBody;
    }

    private static void AddUniqueWallCheckDirection(Vector3[] directions, ref int count, Vector3 direction)
    {
        if (directions == null || count >= directions.Length)
            return;

        if (!TryNormalizePlanar(direction, out direction))
            return;

        for (int i = 0; i < count; i++)
        {
            if (Vector3.Dot(directions[i], direction) > 0.98f)
                return;
        }

        directions[count] = direction;
        count++;
    }

    private bool TryGetWallCheckDirection(out Vector3 direction)
    {
        if (TryNormalizePlanar(-_currentWallNormal, out direction))
            return true;

        if (useCachedWallNormalForReattach &&
            HasUsableWallCache(Mathf.Max(0f, wallReattachGraceTime), Mathf.Max(0f, wallReattachMaxDistance)) &&
            TryNormalizePlanar(-_cachedWallNormal, out direction))
        {
            return true;
        }

        if (motor != null && TryNormalizePlanar(motor.DesiredFacingDirection, out direction))
            return true;

        if (bodyTransform != null && TryNormalizePlanar(bodyTransform.forward, out direction))
            return true;

        if (ownerCamera != null && TryNormalizePlanar(ownerCamera.transform.forward, out direction))
            return true;

        if (motor != null && TryNormalizePlanar(motor.SmoothedMoveWorldDirection, out direction))
            return true;

        direction = Vector3.zero;
        return false;
    }

    private bool IsWallJumpKeyHeld()
    {
        if (!CanReadDirectLocalInput())
            return false;

        if (_legacyInputUnavailable)
            return false;

        try
        {
            return Input.GetKey(wallJumpKey);
        }
        catch (System.InvalidOperationException exception)
        {
            DisableLegacyInput(exception.Message);
            return false;
        }
        catch (System.ArgumentException exception)
        {
            DisableLegacyInput(exception.Message);
            return false;
        }
    }

    private Vector3 GetWallCheckOrigin()
    {
        if (bodyRigidbody != null)
            return bodyRigidbody.worldCenterOfMass;

        if (bodyTransform != null)
            return bodyTransform.position;

        return transform.position;
    }

    private static bool TryGetWallRight(Vector3 wallNormal, out Vector3 wallRight)
    {
        wallRight = Vector3.Cross(wallNormal, Vector3.up);
        if (!IsFiniteVector(wallRight) || wallRight.sqrMagnitude <= 0.0001f)
        {
            wallRight = Vector3.zero;
            return false;
        }

        wallRight.Normalize();
        return true;
    }

    private WallMoveDirection SelectWallMoveDirection(Vector2 input)
    {
        if (input.sqrMagnitude <= wallMoveInputDeadzone * wallMoveInputDeadzone)
            return WallMoveDirection.None;

        if (Mathf.Abs(input.y) >= Mathf.Abs(input.x))
            return input.y >= 0f ? WallMoveDirection.Up : WallMoveDirection.Down;

        return input.x >= 0f ? WallMoveDirection.Right : WallMoveDirection.Left;
    }

    private void SetCurrentWall(RaycastHit wallHit)
    {
        _currentWallNormal = IsFiniteVector(wallHit.normal) && wallHit.normal.sqrMagnitude > 0.0001f
            ? wallHit.normal.normalized
            : Vector3.zero;

        CacheWallHit(wallHit);
    }

    private void CacheWallHit(RaycastHit wallHit)
    {
        if (wallHit.collider == null)
            return;

        if (IsFiniteVector(wallHit.normal) && wallHit.normal.sqrMagnitude > 0.0001f)
            _cachedWallNormal = wallHit.normal.normalized;

        _cachedWallPoint = IsFiniteVector(wallHit.point) ? wallHit.point : GetWallCheckOrigin();
        _cachedWallCollider = wallHit.collider;
        _lastWallSeenTime = Time.time;
    }

    private bool CanKeepWallTraversalDuringContactGrace(out string reason)
    {
        reason = string.Empty;

        if (!keepWallTraversalDuringContactGrace)
            return false;

        if (!HasUsableWallCache(Mathf.Max(0f, wallContactGraceTime), 0f))
            return false;

        if (!TryNormalizePlanar(_currentWallNormal, out Vector3 currentWallPlanarNormal) &&
            TryNormalizePlanar(_cachedWallNormal, out Vector3 cachedNormal))
        {
            _currentWallNormal = cachedNormal;
        }

        reason = $"wall contact grace {Time.time - _lastWallSeenTime:F2}s";
        return true;
    }

    private bool HasUsableWallCache(float maxAge, float maxDistance)
    {
        if (_cachedWallCollider == null)
            return false;

        if (!IsFiniteVector(_cachedWallNormal) || _cachedWallNormal.sqrMagnitude <= 0.0001f)
            return false;

        if (maxAge >= 0f && Time.time - _lastWallSeenTime > maxAge)
            return false;

        if (maxDistance > 0f && IsFiniteVector(_cachedWallPoint))
        {
            Vector3 offset = GetWallCheckOrigin() - _cachedWallPoint;
            if (!IsFiniteVector(offset) || offset.sqrMagnitude > maxDistance * maxDistance)
                return false;
        }

        return true;
    }

    private void ClearWallState()
    {
        _currentWallNormal = Vector3.zero;
        _lastWallMoveInput = Vector2.zero;
        _lastWallMoveDirection = WallMoveDirection.None;
        _wallStartedTime = float.NegativeInfinity;
    }

    private bool IsOwnCollider(Collider wallCollider)
    {
        if (wallCollider == null || _targetRoot == null)
            return false;

        Transform hitTransform = wallCollider.transform;
        return hitTransform == _targetRoot || hitTransform.IsChildOf(_targetRoot);
    }

    private bool IsOtherPlayerCollider(Collider candidate)
    {
        if (candidate == null || IsOwnCollider(candidate))
            return false;

        NetworkObject candidateNetworkObject = candidate.GetComponentInParent<NetworkObject>();
        if (candidateNetworkObject == null || candidateNetworkObject == ResolveOwnerNetworkObject())
            return false;

        bool isPlayer = candidateNetworkObject.GetComponentInChildren<PlayerHub>(true) != null ||
                        candidateNetworkObject.GetComponentInChildren<PlayerStatusModule>(true) != null;
        if (!isPlayer)
            return false;

        if (debugWallTraversalLogs)
        {
            string networkObjectId = candidateNetworkObject.IsSpawned
                ? candidateNetworkObject.NetworkObjectId.ToString()
                : "unspawned";
            WallLog(
                $"wall candidate rejected reason=other player collider={candidate.name} layer={candidate.gameObject.layer} root={candidateNetworkObject.name} NetworkObjectId={networkObjectId}");
        }

        return true;
    }

    private bool IsTargetRoot()
    {
        Transform root = _targetRoot != null ? _targetRoot : (transform.root != null ? transform.root : transform);
        return root != null &&
               (root.name == TargetRootName || root.name.StartsWith(TargetRootName + "(", System.StringComparison.Ordinal));
    }

    private static bool Block(string reason, out string blockReason)
    {
        blockReason = reason;
        return false;
    }

    private static bool IsBlockedExternalReason(string reason)
    {
        if (string.IsNullOrEmpty(reason) || IsTraversalReason(reason))
            return false;

        return reason.StartsWith("RagdollRecovery:", System.StringComparison.Ordinal) ||
               reason.StartsWith("SpinDash:", System.StringComparison.Ordinal);
    }

    private static bool IsTraversalReason(string reason)
    {
        return IsGlideTraversalReason(reason) || IsWallTraversalReason(reason);
    }

    private static bool IsGlideTraversalReason(string reason)
    {
        return reason == TraversalReason || reason == "TraversalGlide:Restore";
    }

    private static bool IsWallTraversalReason(string reason)
    {
        return reason == TraversalWallReason || reason == "TraversalWall:Restore";
    }

    private static bool TryNormalizePlanar(Vector3 value, out Vector3 normalized)
    {
        value = Vector3.ProjectOnPlane(value, Vector3.up);
        if (!IsFiniteVector(value) || value.sqrMagnitude <= 0.0001f)
        {
            normalized = Vector3.zero;
            return false;
        }

        normalized = value.normalized;
        return true;
    }

    private static bool IsUiInputBlocked()
    {
        EventSystem eventSystem = EventSystem.current;
        if (eventSystem == null)
            return false;

        return eventSystem.currentSelectedGameObject != null || eventSystem.IsPointerOverGameObject();
    }

    private void DisableLegacyInput(string message)
    {
        _legacyInputUnavailable = true;
        Log($"legacy input unavailable reason={message}");
    }

    private static Transform FindChildRecursive(Transform root, string childName)
    {
        if (root == null || string.IsNullOrEmpty(childName))
            return null;

        if (root.name == childName)
            return root;

        for (int i = 0; i < root.childCount; i++)
        {
            Transform found = FindChildRecursive(root.GetChild(i), childName);
            if (found != null)
                return found;
        }

        return null;
    }

    private static bool IsFiniteVector(Vector3 value)
    {
        return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
    }

    private static bool IsFinite(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }

    private void Log(string message)
    {
        if (!debugTraversalLogs)
            return;

        Debug.Log($"[MSTraversal:{name}] state={_state} {message}", this);
    }

    private void WallLog(string message)
    {
        if (!debugWallTraversalLogs)
            return;

        Debug.Log($"[MSTraversalWall:{name}] state={_state} {message}", this);
    }

    private static string FormatVector3(Vector3 value)
    {
        return $"({value.x:F2},{value.y:F2},{value.z:F2})";
    }

    private void OnValidate()
    {
        glideMinAirTimeAfterJump = Mathf.Max(0f, glideMinAirTimeAfterJump);
        maxGlideDuration = Mathf.Max(0f, maxGlideDuration);
        glideCooldown = Mathf.Max(0f, glideCooldown);
        maxGlideFallSpeed = Mathf.Max(0.01f, maxGlideFallSpeed);
        glideLiftAcceleration = Mathf.Max(0f, glideLiftAcceleration);
        glideForwardAcceleration = Mathf.Max(0f, glideForwardAcceleration);
        glideMovementControlScale = Mathf.Clamp01(glideMovementControlScale);
        glideUprightControlScale = Mathf.Clamp01(glideUprightControlScale);
        maxVerticalSpeedToStartGlide = Mathf.Max(0f, maxVerticalSpeedToStartGlide);
        glideAnimationCrossFade = Mathf.Max(0f, glideAnimationCrossFade);
        wallCheckDistance = Mathf.Max(0.01f, wallCheckDistance);
        wallCheckRadius = Mathf.Max(0.01f, wallCheckRadius);
        wallMinApproachDot = Mathf.Clamp(wallMinApproachDot, -1f, 1f);
        wallMaxUpDot = Mathf.Clamp01(wallMaxUpDot);
        maxWallClimbDuration = Mathf.Max(0f, maxWallClimbDuration);
        wallStickAcceleration = Mathf.Max(0f, wallStickAcceleration);
        wallClimbUpAcceleration = Mathf.Max(0f, wallClimbUpAcceleration);
        wallMoveDownAcceleration = Mathf.Max(0f, wallMoveDownAcceleration);
        wallMoveSideAcceleration = Mathf.Max(0f, wallMoveSideAcceleration);
        wallSideMoveVerticalHoldAcceleration = Mathf.Max(0f, wallSideMoveVerticalHoldAcceleration);
        wallSideMoveMaxFallSpeed = Mathf.Max(0f, wallSideMoveMaxFallSpeed);
        wallClingVerticalHoldAcceleration = Mathf.Max(0f, wallClingVerticalHoldAcceleration);
        wallClingMaxFallSpeed = Mathf.Max(0f, wallClingMaxFallSpeed);
        wallSlideSlowAcceleration = Mathf.Max(0f, wallSlideSlowAcceleration);
        maxWallSlideFallSpeed = Mathf.Max(0.01f, maxWallSlideFallSpeed);
        wallContactGraceTime = Mathf.Max(0f, wallContactGraceTime);
        wallReattachGraceTime = Mathf.Max(0f, wallReattachGraceTime);
        wallReattachMaxDistance = Mathf.Max(0f, wallReattachMaxDistance);
        wallStartMinApproachDot = Mathf.Clamp(wallStartMinApproachDot, -1f, 1f);
        wallStartMaxUpDot = Mathf.Clamp01(wallStartMaxUpDot);
        maxWallVerticalControlAcceleration = Mathf.Max(0f, maxWallVerticalControlAcceleration);
        wallVerticalSpeedControlGain = Mathf.Max(0f, wallVerticalSpeedControlGain);
        wallVerticalGravityCompensation = Mathf.Max(0f, wallVerticalGravityCompensation);
        wallUpTargetVerticalSpeed = Mathf.Max(0f, wallUpTargetVerticalSpeed);
        wallDownTargetVerticalSpeed = Mathf.Min(0f, wallDownTargetVerticalSpeed);
        wallSideTargetVerticalSpeed = Mathf.Max(0f, wallSideTargetVerticalSpeed);
        wallIdleTargetVerticalSpeed = Mathf.Min(0f, wallIdleTargetVerticalSpeed);
        wallSideTargetSpeed = Mathf.Max(0f, wallSideTargetSpeed);
        wallSideSpeedControlGain = Mathf.Max(0f, wallSideSpeedControlGain);
        maxWallSideControlAcceleration = Mathf.Max(0f, maxWallSideControlAcceleration);
        wallJumpUpVelocityChange = Mathf.Max(0f, wallJumpUpVelocityChange);
        wallJumpAwayVelocityChange = Mathf.Max(0f, wallJumpAwayVelocityChange);
        wallJumpCooldown = Mathf.Max(0f, wallJumpCooldown);
        wallJumpSuppressNormalJumpDuration = Mathf.Max(0f, wallJumpSuppressNormalJumpDuration);
        glideDelayAfterWallJump = Mathf.Max(0f, glideDelayAfterWallJump);
        wallMovementControlScale = Mathf.Clamp01(wallMovementControlScale);
        wallUprightControlScale = Mathf.Clamp01(wallUprightControlScale);
        wallMoveInputDeadzone = Mathf.Max(0f, wallMoveInputDeadzone);
        wallAnimationCrossFade = Mathf.Max(0f, wallAnimationCrossFade);
    }
}
