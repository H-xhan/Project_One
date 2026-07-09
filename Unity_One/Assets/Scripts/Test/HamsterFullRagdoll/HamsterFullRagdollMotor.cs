using System.Reflection;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

public class HamsterFullRagdollMotor : MonoBehaviour
{
    private const string InputRouteTargetName = "Hamster_JointFreeMotorShell_MainScenes";
    private const float InputRouteLogInterval = 0.5f;
    private const float MotorJumpRearmStableGroundTime = 0.05f;
    private const float MaxMotorJumpRearmGroundHitDistance = 0.5f;
    private const float PostJumpSprintMovementSettleTime = 0.10f;
    private const float PostJumpSprintMovementSettleMinScale = 0.35f;
    private const string CameraPivotName = "CameraPivot";
    private const string PlayerCameraName = "PlayerCamera";
    private const string VisualPreviewRootName = "VisualPreviewRoot";

    [Header("References")]
    [SerializeField] private Rigidbody hipsBody;
    [SerializeField] private Rigidbody chestBody;
    [SerializeField] private Rigidbody headBody;
    [SerializeField] private Rigidbody leftArmBody;
    [SerializeField] private Rigidbody rightArmBody;
    [SerializeField] private Transform cameraTransform;
    [SerializeField] private LayerMask groundMask = ~0;

    [Header("Movement")]
    [SerializeField] private float maxWalkSpeed = 2.2f;
    [SerializeField] private float maxSprintSpeed = 3.0f;
    [SerializeField] private float walkAcceleration = 14f;
    [SerializeField] private float sprintAcceleration = 19f;
    [SerializeField] private float chestForceMultiplier = 0.35f;
    [SerializeField] private float turnTorque = 45f;
    [SerializeField] private float turnDamping = 8f;
    [SerializeField] private float inputSmoothTime = 0.12f;
    [SerializeField] private float stopDragAssist = 4f;
    [SerializeField] private float airControlMultiplier = 0.35f;
    [SerializeField] private float controlStrength = 1.0f;

    [Header("Sprint Debug")]
    [SerializeField] private bool debugSprintLogs = false;
    [SerializeField] private float sprintLogInterval = 0.5f;

    [Header("Sprint Pivot Assist")]
    [SerializeField] private bool enableSprintPivotAssist = true;
    [SerializeField] private float sprintPivotDotThreshold = -0.25f;
    [SerializeField] private float sprintPivotAccelerationMultiplier = 1.8f;
    [SerializeField] private float sprintPivotMinPlanarSpeed = 0.5f;
    [SerializeField] private bool debugSprintPivotLogs = false;

    [Header("Jump")]
    [SerializeField] private bool enableJump = true;
    [SerializeField] private KeyCode jumpKey = KeyCode.Space;
    [SerializeField] private float jumpVelocityChange = 4.5f;
    [SerializeField] private float jumpCooldown = 0.25f;
    [SerializeField] private bool requireGroundedForJump = true;
    [SerializeField] private float jumpBufferTime = 0.12f;
    [SerializeField] private float jumpCoyoteTime = 0.08f;
    [SerializeField] private bool cancelDownwardVelocityOnJump = false;
    [SerializeField] private float maxUpwardVelocityBeforeJump = 0.5f;
    [SerializeField] private float landingLogMinVerticalSpeed = 2.0f;
    [SerializeField] private bool debugJumpLogs = false;

    [Header("Jump Visual")]
    [SerializeField] private HamsterVisualFollower visualFollower;
    [SerializeField] private bool autoFindVisualFollower = true;
    [SerializeField] private float jumpVisualIntensity = 1.0f;
    [SerializeField] private float landingVisualIntensity = 1.0f;

    [Header("Upright")]
    [SerializeField] private float uprightStrength = 75f;
    [SerializeField] private float uprightDamping = 10f;
    [SerializeField] private float chestUprightMultiplier = 0.45f;
    [SerializeField] private float groundCheckRadius = 0.15f;
    [SerializeField] private float groundCheckDistance = 0.25f;

    [Header("Pose Hold")]
    [SerializeField] private bool enableStandingPoseHold = true;
    [SerializeField] private bool captureInitialPoseOnEnable = true;
    [SerializeField] private bool yawPoseTowardsMoveDirection = true;
    [SerializeField] private float poseYawSmoothTime = 0.18f;
    [SerializeField] private float hipsPoseSpring = 180f;
    [SerializeField] private float hipsPoseDamping = 24f;
    [SerializeField] private float hipsMaxPoseTorque = 350f;
    [SerializeField] private float chestPoseSpring = 120f;
    [SerializeField] private float chestPoseDamping = 18f;
    [SerializeField] private float chestMaxPoseTorque = 260f;
    [SerializeField] private float headPoseSpring = 45f;
    [SerializeField] private float headPoseDamping = 8f;
    [SerializeField] private float headMaxPoseTorque = 80f;
    [SerializeField] private float armPoseSpring = 25f;
    [SerializeField] private float armPoseDamping = 5f;
    [SerializeField] private float armMaxPoseTorque = 45f;

    [Header("Phase 1 Stability")]
    [SerializeField] private bool disableTurnTorqueWhilePoseHold = true;
    [SerializeField] private float groundedPoseStrengthMultiplier = 1f;
    [SerializeField] private float airbornePoseStrengthMultiplier = 0.35f;

    [Header("Debug")]
    [SerializeField] private bool drawDebugGizmos = false;
    [SerializeField] private bool debugLogs = false;
    [SerializeField] private bool debugInputRouteLogs = false;

    private const float InputDeadzone = 0.01f;
    private const float MaxUprightTorque = 250f;
    private const float MaxTurnTorque = 160f;
    private const float MaxStopAssistAcceleration = 35f;

    private Vector2 _smoothedMoveInput;
    private Vector2 _smoothInputVelocity;
    private Vector3 _smoothedMoveWorldDirection;
    private bool _isGrounded;
    private bool _legacyInputUnavailable;
    private NetworkObject _ownerNetworkObject;
    private Vector2 _networkMoveInput;
    private bool _networkSprintHeld;
    private Vector3 _networkCameraPlanarForward = Vector3.forward;
    private Vector3 _networkCameraPlanarRight = Vector3.right;
    private bool _hasNetworkCameraBasis;
    private bool _hasNetworkInput;
    private string _lastInputSource = "Legacy";
    private bool _missingRequiredReferenceLogged;
    private bool _hasInitialPose;
    private Quaternion _initialMotorRotation = Quaternion.identity;
    private Vector3 _initialForwardOnPlane = Vector3.forward;
    private Quaternion _initialHipsRotation = Quaternion.identity;
    private Quaternion _initialChestRotation = Quaternion.identity;
    private Quaternion _initialHeadRotation = Quaternion.identity;
    private Quaternion _initialLeftArmRotation = Quaternion.identity;
    private Quaternion _initialRightArmRotation = Quaternion.identity;
    private float _currentPoseYaw;
    private float _poseYawVelocity;
    private float _lastPoseTargetYaw;
    private float _nextDebugLogTime;
    private Vector2 _lastRawMoveInput;
    private float _lastMaxSpeed;
    private float _lastSelectedAcceleration;
    private float _lastAppliedAcceleration;
    private float _lastAccelerationScale;
    private float _lastEffectiveControl;
    private Vector3 _lastAppliedMoveForce;
    private Vector3 _lastAppliedStopDragForce;
    private string _lastMoveForceSkipReason = "not evaluated";
    private string _lastStopDragSkipReason = "not evaluated";
    private bool _lastMoveForceApplied;
    private bool _lastStopDragApplied;
    private float _jumpCooldownTimer;
    private float _jumpBufferTimer;
    private float _lastGroundedTime = float.NegativeInfinity;
    private bool _motorJumpConsumedSinceStableGround;
    private bool _motorJumpNeedsAirborneBeforeRearm;
    private float _motorJumpStableGroundTimer;
    private float _postJumpSprintMovementSettleTimer;
    private bool _wasGroundedForJump;
    private float _lastAirborneVerticalSpeed;
    private bool _jumpConsumedThisFixedStep;
    private bool _suppressJumpUntilKeyRelease;
    private float _sprintLogTimer;
    private bool _lastSprintHeld;
    private float _lastSelectedMaxSpeed;
    private float _nextInputRouteMotorLogTime;
    private Vector3 _lastGroundProbeOrigin;
    private float _lastGroundProbeRadius;
    private float _lastGroundProbeDistance;
    private float _lastGroundColliderBottomY = float.NaN;
    private float _lastGroundRbCenterOfMassY = float.NaN;
    private bool _lastGroundHit;
    private string _lastGroundHitName = "<none>";
    private int _lastGroundHitLayer = -1;
    private float _lastGroundHitDistance = float.NaN;
    private bool _lastGroundIgnoreLayerCollision;
    private Vector3 _lastMoveVelocityBefore;
    private Vector3 _lastMoveVelocityAfter;
    private float _lastMovePlanarSpeedBefore;
    private float _lastMovePlanarSpeedAfter;
    private Vector3 _lastInputRoutePositionDelta;
    private Vector3 _previousInputRouteBodyPosition;
    private bool _hasPreviousInputRouteBodyPosition;
    private string _lastCameraBasisSource = "None";
    private Vector3 _lastCameraBasisForward;
    private Vector3 _lastCameraBasisRight;
    private Vector3 _lastPlanarBasisForward = Vector3.forward;
    private Vector3 _lastPlanarBasisRight = Vector3.right;
    private Vector3 _lastMoveWorldDirectionBefore;
    private Vector3 _lastMoveWorldDirectionAfter;
    private Vector3 _lastPlanarFacingForward = Vector3.forward;
    private Vector3 _lastDesiredFacingDirection = Vector3.forward;
    private string _lastAppliedYawSource = "Initial";
    private bool _externalControlLocked;
    private bool _externalJumpLocked;
    private float _externalMovementControlScale = 1f;
    private float _externalUprightControlScale = 1f;
    private float _externalPoseControlScale = 1f;
    private string _externalControlLockReason = "none";
    private string _externalJumpLockReason = "none";
    private string _externalMovementControlReason = "none";
    private string _externalUprightControlReason = "none";
    private string _externalPoseControlReason = "none";

    private struct MoveBasis
    {
        public readonly string Source;
        public readonly Vector3 CameraForward;
        public readonly Vector3 CameraRight;
        public readonly Vector3 PlanarForward;
        public readonly Vector3 PlanarRight;

        public MoveBasis(string source, Vector3 cameraForward, Vector3 cameraRight, Vector3 planarForward, Vector3 planarRight)
        {
            Source = source;
            CameraForward = cameraForward;
            CameraRight = cameraRight;
            PlanarForward = planarForward;
            PlanarRight = planarRight;
        }
    }

    public bool IsGrounded => _isGrounded;
    public bool IsSprintHeld => _lastSprintHeld;
    public bool HasMoveInput => _smoothedMoveInput.sqrMagnitude > InputDeadzone * InputDeadzone;
    public bool IsMainScenesInputRouteTarget => IsInputRouteTarget();
    public Vector3 SmoothedMoveWorldDirection => _smoothedMoveWorldDirection;
    public Vector3 DesiredFacingDirection => _lastDesiredFacingDirection;
    public Vector3 CameraPlanarForward => _lastPlanarBasisForward;
    public Vector3 CurrentPlanarVelocity => GetPlanarVelocity(hipsBody);
    public float CurrentPlanarSpeed => CurrentPlanarVelocity.magnitude;
    public float CurrentVerticalVelocity => GetVerticalVelocity(hipsBody);
    public float LastPoseTargetYaw => _lastPoseTargetYaw;
    public float CurrentPoseYaw => _currentPoseYaw;
    public string LastCameraBasisSource => _lastCameraBasisSource;
    public string LastAppliedYawSource => _lastAppliedYawSource;
    public bool IsExternalControlLocked => _externalControlLocked;
    public bool IsExternalJumpLocked => _externalJumpLocked;
    public float ExternalMovementControlScale => _externalMovementControlScale;
    public float ExternalUprightControlScale => _externalUprightControlScale;
    public float ExternalPoseControlScale => _externalPoseControlScale;
    public string ExternalControlLockReason => _externalControlLockReason;
    public string ExternalJumpLockReason => _externalJumpLockReason;
    public string ExternalMovementControlReason => _externalMovementControlReason;
    public string ExternalUprightControlReason => _externalUprightControlReason;
    public string ExternalPoseControlReason => _externalPoseControlReason;

    public void SetControlStrength(float value)
    {
        controlStrength = Mathf.Max(0f, value);
    }

    public void SetExternalControlLock(bool locked, string reason)
    {
        _externalControlLocked = locked;
        _externalControlLockReason = string.IsNullOrEmpty(reason) ? "none" : reason;
    }

    public void SetExternalJumpLock(bool locked, string reason)
    {
        _externalJumpLocked = locked;
        _externalJumpLockReason = string.IsNullOrEmpty(reason) ? "none" : reason;
        if (locked)
            _jumpBufferTimer = 0f;
    }

    public void ClearRecoveryJumpState(string reason)
    {
        _jumpBufferTimer = 0f;
        _jumpConsumedThisFixedStep = false;
        _lastGroundedTime = _isGrounded ? Time.time : float.NegativeInfinity;
        _motorJumpConsumedSinceStableGround = false;
        _motorJumpNeedsAirborneBeforeRearm = false;
        _motorJumpStableGroundTimer = 0f;
        _postJumpSprintMovementSettleTimer = 0f;
        _wasGroundedForJump = _isGrounded;

        if (TryReadJumpHeld(out bool jumpHeld) && jumpHeld)
            _suppressJumpUntilKeyRelease = true;
        else
            _suppressJumpUntilKeyRelease = false;

        if (debugJumpLogs)
        {
            Debug.Log(
                $"[HamsterFullRagdollMotor:{gameObject.name}] Recovery jump state cleared reason={reason} suppressUntilRelease={_suppressJumpUntilKeyRelease}",
                this);
        }
    }

    public void SetExternalControlScale(float scale, string reason)
    {
        SetExternalMovementControlScale(scale, reason);
        SetExternalUprightControlScale(scale, reason);
        SetExternalPoseControlScale(scale, reason);
    }

    public void SetExternalMovementControlScale(float scale, string reason)
    {
        _externalMovementControlScale = Mathf.Clamp01(scale);
        _externalMovementControlReason = string.IsNullOrEmpty(reason) ? "none" : reason;
    }

    public void SetExternalUprightControlScale(float scale, string reason)
    {
        _externalUprightControlScale = Mathf.Clamp01(scale);
        _externalUprightControlReason = string.IsNullOrEmpty(reason) ? "none" : reason;
    }

    public void SetExternalPoseControlScale(float scale, string reason)
    {
        _externalPoseControlScale = Mathf.Clamp01(scale);
        _externalPoseControlReason = string.IsNullOrEmpty(reason) ? "none" : reason;
    }

    public void SetNetworkInput(Vector2 moveInput, bool sprintHeld, bool jumpPressed)
    {
        SetNetworkInput(moveInput, sprintHeld, jumpPressed, Vector3.zero, Vector3.zero);
    }

    public void SetNetworkInput(
        Vector2 moveInput,
        bool sprintHeld,
        bool jumpPressed,
        Vector3 cameraPlanarForward,
        Vector3 cameraPlanarRight)
    {
        _networkMoveInput = Vector2.ClampMagnitude(moveInput, 1f);
        _networkSprintHeld = sprintHeld;
        _hasNetworkCameraBasis = TryStoreNetworkCameraBasis(cameraPlanarForward, cameraPlanarRight);
        _hasNetworkInput = true;

        if (jumpPressed)
            BufferJumpPressedInput();
    }

    private void Awake()
    {
        if (!HasRequiredReferences())
        {
            LogMissingRequiredReferences();
            enabled = false;
            return;
        }

        ResolveJumpVisualFollower();
        CaptureInitialPose();
    }

    private void OnEnable()
    {
        ResolveJumpVisualFollower();

        if (captureInitialPoseOnEnable && HasRequiredReferences())
            CaptureInitialPose();

        LogMotorEnabledState();
    }

    private void Update()
    {
        CaptureJumpInput();
    }

    private void FixedUpdate()
    {
        if (!HasRequiredReferences())
            return;

        if (!_hasInitialPose)
            CaptureInitialPose();

        float fixedDeltaTime = Time.fixedDeltaTime;
        TickJumpCooldown(fixedDeltaTime);
        TickPostJumpSprintMovementSettle(fixedDeltaTime);
        CaptureInputRoutePositionDelta();

        Vector2 rawMoveInput = ReadMoveInput();
        _lastRawMoveInput = rawMoveInput;
        _smoothedMoveInput = Vector2.SmoothDamp(
            _smoothedMoveInput,
            rawMoveInput,
            ref _smoothInputVelocity,
            inputSmoothTime,
            Mathf.Infinity,
            fixedDeltaTime);

        _isGrounded = CheckGrounded();
        UpdateJumpGroundedState();
        _smoothedMoveWorldDirection = BuildMoveDirection(_smoothedMoveInput);

        bool sprintHeld = ReadSprintHeld();
        float maxSpeed = sprintHeld ? maxSprintSpeed : maxWalkSpeed;
        float acceleration = sprintHeld ? sprintAcceleration : walkAcceleration;
        float groundedMultiplier = _isGrounded ? 1f : airControlMultiplier;
        float effectiveControl = Mathf.Max(0f, controlStrength) * groundedMultiplier;
        if (_externalControlLocked)
            effectiveControl = 0f;
        else
            effectiveControl *= Mathf.Clamp01(_externalMovementControlScale);
        bool hasMoveInput = _smoothedMoveInput.sqrMagnitude > InputDeadzone * InputDeadzone;
        acceleration = ApplySprintPivotAssist(_smoothedMoveWorldDirection, sprintHeld, hasMoveInput, acceleration);
        acceleration = ApplyPostJumpSprintMovementSettle(sprintHeld, acceleration);
        _lastMaxSpeed = maxSpeed;
        _lastSelectedMaxSpeed = maxSpeed;
        _lastSelectedAcceleration = acceleration;
        _lastEffectiveControl = effectiveControl;
        _lastSprintHeld = sprintHeld;

        TryConsumeJump();
        TickJumpBuffer(fixedDeltaTime);
        ApplyMovementForce(_smoothedMoveWorldDirection, maxSpeed, acceleration, effectiveControl);
        ApplyStopDragAssist(_smoothedMoveInput, effectiveControl);
        LogSprintState(hasMoveInput);
        float uprightControlScale = _externalControlLocked ? 0f : Mathf.Clamp01(_externalUprightControlScale);
        Rigidbody uprightBody0 = null;
        Rigidbody uprightBody1 = null;
        Rigidbody uprightBody2 = null;
        Rigidbody uprightBody3 = null;
        Rigidbody uprightBody4 = null;
        ApplyUprightTorqueOnce(hipsBody, uprightStrength * uprightControlScale, uprightDamping * uprightControlScale, ref uprightBody0, ref uprightBody1, ref uprightBody2, ref uprightBody3, ref uprightBody4);
        ApplyUprightTorqueOnce(chestBody, uprightStrength * chestUprightMultiplier * uprightControlScale, uprightDamping * chestUprightMultiplier * uprightControlScale, ref uprightBody0, ref uprightBody1, ref uprightBody2, ref uprightBody3, ref uprightBody4);

        bool inputRouteTarget = IsInputRouteTarget();
        bool poseHoldActive = (enableStandingPoseHold || inputRouteTarget) && _hasInitialPose;
        if (poseHoldActive)
            ApplyStandingPoseHold(hasMoveInput, fixedDeltaTime);

        if (!poseHoldActive || !disableTurnTorqueWhilePoseHold)
            ApplyTurnTorque(_smoothedMoveWorldDirection, effectiveControl);

        LogDebugState(hasMoveInput, poseHoldActive);
        LogInputRouteMotor(hasMoveInput);
    }

    private bool HasRequiredReferences()
    {
        return hipsBody != null && chestBody != null;
    }

    private void CaptureInitialPose()
    {
        _initialMotorRotation = transform.rotation;
        _initialForwardOnPlane = Vector3.ProjectOnPlane(
            hipsBody != null ? hipsBody.transform.forward : transform.forward,
            Vector3.up);

        if (_initialForwardOnPlane.sqrMagnitude <= 0.0001f)
            _initialForwardOnPlane = Vector3.forward;
        else
            _initialForwardOnPlane.Normalize();

        if (hipsBody != null)
            _initialHipsRotation = hipsBody.rotation;

        if (chestBody != null)
            _initialChestRotation = chestBody.rotation;

        if (headBody != null)
            _initialHeadRotation = headBody.rotation;

        if (leftArmBody != null)
            _initialLeftArmRotation = leftArmBody.rotation;

        if (rightArmBody != null)
            _initialRightArmRotation = rightArmBody.rotation;

        _currentPoseYaw = 0f;
        _poseYawVelocity = 0f;
        _lastPoseTargetYaw = 0f;
        _lastPlanarFacingForward = _initialForwardOnPlane;
        _lastDesiredFacingDirection = _initialForwardOnPlane;
        _hasInitialPose = true;
    }

    private Vector2 ReadMoveInput()
    {
        if (ShouldUseNetworkInput())
        {
            _lastInputSource = _hasNetworkInput ? "NetworkRouted" : "NetworkRoutedPending";
            return _networkMoveInput;
        }

        if (!CanReadDirectLocalInput())
        {
            _lastInputSource = "BlockedNonOwner";
            return Vector2.zero;
        }

        if (_legacyInputUnavailable)
            return Vector2.zero;

        try
        {
            float horizontal = Input.GetAxisRaw("Horizontal");
            float vertical = Input.GetAxisRaw("Vertical");
            _lastInputSource = "LegacyDirect";
            return Vector2.ClampMagnitude(new Vector2(horizontal, vertical), 1f);
        }
        catch (System.InvalidOperationException exception)
        {
            DisableLegacyInput(exception.Message);
        }
        catch (System.ArgumentException exception)
        {
            DisableLegacyInput(exception.Message);
        }

        return Vector2.zero;
    }

    private bool ReadSprintHeld()
    {
        if (ShouldUseNetworkInput())
            return _networkSprintHeld;

        if (!CanReadDirectLocalInput())
            return false;

        if (_legacyInputUnavailable)
            return false;

        try
        {
            return Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
        }
        catch (System.InvalidOperationException exception)
        {
            DisableLegacyInput(exception.Message);
        }

        return false;
    }

    private float ApplySprintPivotAssist(Vector3 moveDirection, bool sprintHeld, bool hasMoveInput, float selectedAcceleration)
    {
        if (!enableSprintPivotAssist
            || !sprintHeld
            || !hasMoveInput
            || hipsBody == null
            || selectedAcceleration <= 0f
            || moveDirection.sqrMagnitude <= 0.0001f)
        {
            return selectedAcceleration;
        }

        Vector3 planarVelocity = GetPlanarVelocity(hipsBody);
        float planarSpeed = planarVelocity.magnitude;
        if (planarSpeed < sprintPivotMinPlanarSpeed)
            return selectedAcceleration;

        float dot = Vector3.Dot(planarVelocity.normalized, moveDirection.normalized);
        if (dot > sprintPivotDotThreshold)
            return selectedAcceleration;

        float assistedAcceleration = selectedAcceleration * sprintPivotAccelerationMultiplier;
        if (debugSprintPivotLogs)
        {
            Debug.Log(
                $"[HamsterFullRagdollMotor:{gameObject.name}] sprintPivotAssist dot={dot:F2} acceleration={assistedAcceleration:F2} speed={planarSpeed:F2}",
                this);
        }

        return assistedAcceleration;
    }

    private void CaptureJumpInput()
    {
        if (ShouldUseNetworkInput() || !CanReadDirectLocalInput())
            return;

        if (!enableJump || _legacyInputUnavailable)
            return;

        if (_externalControlLocked || _externalJumpLocked)
            return;

        try
        {
            if (_suppressJumpUntilKeyRelease)
            {
                if (Input.GetKey(jumpKey))
                    return;

                _suppressJumpUntilKeyRelease = false;
            }

            if (Input.GetKeyDown(jumpKey))
                BufferJumpPressedInput();
        }
        catch (System.InvalidOperationException exception)
        {
            DisableLegacyInput(exception.Message);
        }
    }

    private bool TryReadJumpHeld(out bool jumpHeld)
    {
        jumpHeld = false;

        if (ShouldUseNetworkInput() || !CanReadDirectLocalInput())
            return false;

        if (_legacyInputUnavailable)
            return false;

        try
        {
            jumpHeld = Input.GetKey(jumpKey);
            return true;
        }
        catch (System.InvalidOperationException exception)
        {
            DisableLegacyInput(exception.Message);
            return false;
        }
    }

    private void BufferJumpPressedInput()
    {
        if (!enableJump || _externalControlLocked || _externalJumpLocked)
            return;

        if (CanBufferMotorJumpInputNow())
            _jumpBufferTimer = Mathf.Max(0f, jumpBufferTime);
        else
            _jumpBufferTimer = 0f;
    }

    private void TickJumpCooldown(float deltaTime)
    {
        _jumpConsumedThisFixedStep = false;

        if (_jumpCooldownTimer > 0f)
            _jumpCooldownTimer = Mathf.Max(0f, _jumpCooldownTimer - deltaTime);
    }

    private bool CanBufferMotorJumpInputNow()
    {
        if (_jumpCooldownTimer > 0f)
            return false;

        if (_motorJumpConsumedSinceStableGround)
            return false;

        if (!requireGroundedForJump)
            return true;

        if (_isGrounded)
            return true;

        return Time.time - _lastGroundedTime <= Mathf.Max(0f, jumpCoyoteTime);
    }

    private void TickJumpBuffer(float deltaTime)
    {
        if (_jumpBufferTimer > 0f)
            _jumpBufferTimer = Mathf.Max(0f, _jumpBufferTimer - deltaTime);
    }

    private void UpdateJumpGroundedState()
    {
        float verticalVelocity = GetVerticalVelocity(hipsBody);

        if (_isGrounded)
        {
            _lastGroundedTime = Time.time;

            if (!_wasGroundedForJump)
                HandleLanding(verticalVelocity);

            _lastAirborneVerticalSpeed = 0f;
        }
        else
        {
            _lastAirborneVerticalSpeed = Mathf.Max(_lastAirborneVerticalSpeed, Mathf.Max(0f, -verticalVelocity));
        }

        _wasGroundedForJump = _isGrounded;
        UpdateMotorJumpRearmState();
    }

    private void TryConsumeJump()
    {
        if (_jumpBufferTimer <= 0f)
            return;

        if (_externalControlLocked || _externalJumpLocked)
        {
            ClearJumpBufferAndLogSkip(_externalControlLocked
                ? $"external control locked reason={_externalControlLockReason}"
                : $"external jump locked reason={_externalJumpLockReason}");
            return;
        }

        if (!enableJump)
        {
            ClearJumpBufferAndLogSkip("disabled");
            return;
        }

        Rigidbody jumpBody = hipsBody;
        if (jumpBody == null)
        {
            ClearJumpBufferAndLogSkip("no body");
            return;
        }

        if (jumpBody.isKinematic)
        {
            ClearJumpBufferAndLogSkip("body kinematic");
            return;
        }

        if ((jumpBody.constraints & RigidbodyConstraints.FreezePositionY) != 0)
        {
            ClearJumpBufferAndLogSkip("freeze position y");
            return;
        }

        if (_jumpCooldownTimer > 0f)
        {
            ClearJumpBufferAndLogSkip($"cooldown remaining={_jumpCooldownTimer:F2}");
            return;
        }

        if (_motorJumpConsumedSinceStableGround)
        {
            ClearJumpBufferAndLogSkip("jump not rearmed");
            return;
        }

        bool canUseCoyote = Time.time - _lastGroundedTime <= Mathf.Max(0f, jumpCoyoteTime);
        if (requireGroundedForJump && !_isGrounded && !canUseCoyote)
        {
            ClearJumpBufferAndLogSkip("not grounded");
            return;
        }

        Vector3 velocityBefore = jumpBody.linearVelocity;
        if (velocityBefore.y > maxUpwardVelocityBeforeJump)
        {
            ClearJumpBufferAndLogSkip($"upward velocity too high y={velocityBefore.y:F2}");
            return;
        }

        if (cancelDownwardVelocityOnJump && velocityBefore.y < 0f)
        {
            velocityBefore.y = 0f;
            jumpBody.linearVelocity = velocityBefore;
        }

        jumpBody.AddForce(Vector3.up * jumpVelocityChange, ForceMode.VelocityChange);
        NotifyJumpVisualReaction();
        _jumpCooldownTimer = Mathf.Max(0f, jumpCooldown);
        _jumpBufferTimer = 0f;
        _motorJumpConsumedSinceStableGround = true;
        _motorJumpNeedsAirborneBeforeRearm = true;
        _motorJumpStableGroundTimer = 0f;
        _postJumpSprintMovementSettleTimer = _lastSprintHeld ? PostJumpSprintMovementSettleTime : 0f;
        _jumpConsumedThisFixedStep = true;

        if (debugJumpLogs)
        {
            Debug.Log(
                $"[HamsterFullRagdollMotor:{gameObject.name}] Jump applied grounded={_isGrounded} coyote={canUseCoyote} force={jumpVelocityChange:F2} velocityBefore={FormatVector3(velocityBefore)}",
                this);
        }
    }

    private void HandleLanding(float currentVerticalVelocity)
    {
        float verticalSpeed = Mathf.Max(_lastAirborneVerticalSpeed, Mathf.Max(0f, -currentVerticalVelocity));
        NotifyLandingVisualReaction(verticalSpeed);

        if (debugJumpLogs && verticalSpeed >= landingLogMinVerticalSpeed)
        {
            Debug.Log(
                $"[HamsterFullRagdollMotor:{gameObject.name}] Landed verticalSpeed={verticalSpeed:F2} planarSpeed={CurrentPlanarSpeed:F2}",
                this);
        }
    }

    private void LogJumpSkip(string reason)
    {
        if (!debugJumpLogs)
            return;

        Debug.Log($"[HamsterFullRagdollMotor:{gameObject.name}] Jump skipped reason={reason}", this);
    }

    private void ClearJumpBufferAndLogSkip(string reason)
    {
        _jumpBufferTimer = 0f;
        LogJumpSkip(reason);
    }

    private void TickPostJumpSprintMovementSettle(float deltaTime)
    {
        if (_postJumpSprintMovementSettleTimer <= 0f)
            return;

        _postJumpSprintMovementSettleTimer = Mathf.Max(0f, _postJumpSprintMovementSettleTimer - deltaTime);
    }

    private float ApplyPostJumpSprintMovementSettle(bool sprintHeld, float selectedAcceleration)
    {
        if (!sprintHeld || _postJumpSprintMovementSettleTimer <= 0f || selectedAcceleration <= 0f)
            return selectedAcceleration;

        float duration = Mathf.Max(0.0001f, PostJumpSprintMovementSettleTime);
        float settle01 = 1f - Mathf.Clamp01(_postJumpSprintMovementSettleTimer / duration);
        float scale = Mathf.Lerp(PostJumpSprintMovementSettleMinScale, 1f, settle01);
        return selectedAcceleration * scale;
    }

    private void UpdateMotorJumpRearmState()
    {
        if (!_motorJumpConsumedSinceStableGround)
            return;

        bool stableGround = IsMotorJumpRearmGroundStableNow();
        if (_motorJumpNeedsAirborneBeforeRearm)
        {
            if (stableGround)
            {
                _motorJumpStableGroundTimer = 0f;
                return;
            }

            _motorJumpNeedsAirborneBeforeRearm = false;
            _motorJumpStableGroundTimer = 0f;
            return;
        }

        if (!stableGround)
        {
            _motorJumpStableGroundTimer = 0f;
            return;
        }

        _motorJumpStableGroundTimer += Time.fixedDeltaTime;
        if (_motorJumpStableGroundTimer < MotorJumpRearmStableGroundTime)
            return;

        _motorJumpConsumedSinceStableGround = false;
        _motorJumpStableGroundTimer = 0f;
    }

    private bool IsMotorJumpRearmGroundStableNow()
    {
        return _isGrounded &&
               !float.IsNaN(_lastGroundHitDistance) &&
               _lastGroundHitDistance <= MaxMotorJumpRearmGroundHitDistance;
    }

    private void NotifyJumpVisualReaction()
    {
        if (visualFollower == null && autoFindVisualFollower)
            ResolveJumpVisualFollower();

        if (visualFollower == null)
        {
            if (debugJumpLogs)
                Debug.Log($"[HamsterFullRagdollMotor:{gameObject.name}] Jump visual skipped reason=no visualFollower", this);

            return;
        }

        visualFollower.AddJumpVisualReaction(jumpVisualIntensity);
    }

    private void NotifyLandingVisualReaction(float verticalSpeed)
    {
        if (visualFollower == null && autoFindVisualFollower)
            ResolveJumpVisualFollower();

        if (visualFollower == null)
        {
            if (debugJumpLogs)
                Debug.Log($"[HamsterFullRagdollMotor:{gameObject.name}] Landing visual skipped reason=no visualFollower", this);

            return;
        }

        float intensity = Mathf.Clamp01(verticalSpeed / 5f) * landingVisualIntensity;
        visualFollower.AddLandingVisualReaction(intensity);
    }

    private void ResolveJumpVisualFollower()
    {
        if (!autoFindVisualFollower || visualFollower != null)
            return;

        visualFollower = GetComponent<HamsterVisualFollower>();
        if (visualFollower == null && debugJumpLogs)
            Debug.Log($"[HamsterFullRagdollMotor:{gameObject.name}] HamsterVisualFollower not found on MotorShellBody.", this);
    }

    private void LogSprintState(bool hasMoveInput)
    {
        if (!debugSprintLogs)
            return;

        _sprintLogTimer -= Time.fixedDeltaTime;
        if (_sprintLogTimer > 0f)
            return;

        _sprintLogTimer = Mathf.Max(0.02f, sprintLogInterval);
        Debug.Log(
            $"[HamsterFullRagdollMotor:{gameObject.name}] sprintHeld={_lastSprintHeld} selectedMaxSpeed={_lastSelectedMaxSpeed:F2} selectedAcceleration={_lastSelectedAcceleration:F2} planarSpeed={CurrentPlanarSpeed:F2} rawInput={FormatVector2(_lastRawMoveInput)} smoothedInput={FormatVector2(_smoothedMoveInput)} hasMoveInput={hasMoveInput}",
            this);
    }

    private void DisableLegacyInput(string reason)
    {
        _legacyInputUnavailable = true;
        if (IsInputRouteTarget())
        {
            Debug.LogWarning(
                $"[InputRoute/Motor:{GetInputRouteObjectName()}] legacyInputDisabled reason={reason}",
                this);
        }

        Log($"Legacy Input Manager unavailable. Movement input disabled. Reason={reason}");
    }

    private bool ShouldUseNetworkInput()
    {
        if (!IsInputRouteTarget())
            return false;

        NetworkObject ownerNetworkObject = ResolveOwnerNetworkObject();
        NetworkManager networkManager = NetworkManager.Singleton;
        return ownerNetworkObject != null &&
               ownerNetworkObject.IsSpawned &&
               networkManager != null &&
               networkManager.IsListening;
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

    private bool TryStoreNetworkCameraBasis(Vector3 cameraPlanarForward, Vector3 cameraPlanarRight)
    {
        cameraPlanarForward = Vector3.ProjectOnPlane(cameraPlanarForward, Vector3.up);
        cameraPlanarRight = Vector3.ProjectOnPlane(cameraPlanarRight, Vector3.up);

        if (!NormalizePlanarBasis(ref cameraPlanarForward, ref cameraPlanarRight))
        {
            _networkCameraPlanarForward = Vector3.forward;
            _networkCameraPlanarRight = Vector3.right;
            return false;
        }

        _networkCameraPlanarForward = cameraPlanarForward;
        _networkCameraPlanarRight = cameraPlanarRight;
        return true;
    }

    private NetworkObject ResolveOwnerNetworkObject()
    {
        if (_ownerNetworkObject != null)
            return _ownerNetworkObject;

        _ownerNetworkObject = GetComponentInParent<NetworkObject>();
        return _ownerNetworkObject;
    }

    private Vector3 BuildMoveDirection(Vector2 moveInput)
    {
        Transform referenceTransform = cameraTransform != null ? cameraTransform : transform;
        Vector3 legacyDirection = BuildMoveDirectionFromTransform(moveInput, referenceTransform);
        _lastMoveWorldDirectionBefore = legacyDirection;

        if (!IsInputRouteTarget())
            return legacyDirection;

        if (ShouldUseNetworkInput() && _hasNetworkCameraBasis)
        {
            _lastCameraBasisSource = "NetworkOwnerCamera";
            _lastCameraBasisForward = _networkCameraPlanarForward;
            _lastCameraBasisRight = _networkCameraPlanarRight;
            _lastPlanarBasisForward = _networkCameraPlanarForward;
            _lastPlanarBasisRight = _networkCameraPlanarRight;

            Vector3 networkDirection = BuildMoveDirectionFromBasis(
                moveInput,
                _networkCameraPlanarForward,
                _networkCameraPlanarRight);
            _lastMoveWorldDirectionAfter = networkDirection;
            UpdateLastPlanarFacingForward(networkDirection);
            return networkDirection;
        }

        if (!TryResolveCameraRelativeMoveBasis(out MoveBasis moveBasis))
        {
            _lastCameraBasisSource = "None";
            _lastMoveWorldDirectionAfter = legacyDirection;
            UpdateLastPlanarFacingForward(legacyDirection);
            return legacyDirection;
        }

        _lastCameraBasisSource = moveBasis.Source;
        _lastCameraBasisForward = moveBasis.CameraForward;
        _lastCameraBasisRight = moveBasis.CameraRight;
        _lastPlanarBasisForward = moveBasis.PlanarForward;
        _lastPlanarBasisRight = moveBasis.PlanarRight;

        Vector3 direction = BuildMoveDirectionFromBasis(moveInput, moveBasis.PlanarForward, moveBasis.PlanarRight);
        _lastMoveWorldDirectionAfter = direction;
        UpdateLastPlanarFacingForward(direction);
        return direction;
    }

    private Vector3 BuildMoveDirectionFromTransform(Vector2 moveInput, Transform referenceTransform)
    {
        if (referenceTransform == null)
            return BuildMoveDirectionFromBasis(moveInput, Vector3.forward, Vector3.right);

        Vector3 forward = Vector3.ProjectOnPlane(referenceTransform.forward, Vector3.up);
        Vector3 right = Vector3.ProjectOnPlane(referenceTransform.right, Vector3.up);

        if (forward.sqrMagnitude <= 0.0001f)
            forward = Vector3.forward;
        else
            forward.Normalize();

        if (right.sqrMagnitude <= 0.0001f)
            right = Vector3.right;
        else
            right.Normalize();

        return BuildMoveDirectionFromBasis(moveInput, forward, right);
    }

    private Vector3 BuildMoveDirectionFromBasis(Vector2 moveInput, Vector3 forward, Vector3 right)
    {
        if (moveInput.sqrMagnitude <= InputDeadzone * InputDeadzone)
            return Vector3.zero;

        Vector3 direction = right * moveInput.x + forward * moveInput.y;
        if (direction.sqrMagnitude > 1f)
            direction.Normalize();

        return direction;
    }

    private bool TryResolveCameraRelativeMoveBasis(out MoveBasis moveBasis)
    {
        moveBasis = default;

        if (TryCreateMoveBasis(cameraTransform, "cameraTransform", out moveBasis))
            return true;

        Transform playerCamera = FindChildByName(PlayerCameraName);
        if (TryCreateMoveBasis(playerCamera, "PlayerCamera", out moveBasis))
            return true;

        Transform cameraPivot = FindChildByName(CameraPivotName);
        if (TryCreateMoveBasis(cameraPivot, "CameraPivot", out moveBasis))
            return true;

        Transform playerHubCameraRoot = ResolvePlayerHubCameraRoot();
        if (TryCreateMoveBasis(playerHubCameraRoot, "PlayerHub.cameraRoot", out moveBasis))
            return true;

        Camera mainCamera = Camera.main;
        if (mainCamera != null &&
            IsUnderInputRouteRoot(mainCamera.transform) &&
            TryCreateMoveBasis(mainCamera.transform, "Camera.main", out moveBasis))
        {
            return true;
        }

        Transform root = transform.root != null ? transform.root : transform;
        if (TryCreateMoveBasis(root, "RootTransform", out moveBasis))
            return true;

        moveBasis = new MoveBasis("WorldFallback", Vector3.forward, Vector3.right, Vector3.forward, Vector3.right);
        return true;
    }

    private bool TryCreateMoveBasis(Transform source, string sourceName, out MoveBasis moveBasis)
    {
        moveBasis = default;
        if (source == null)
            return false;

        Vector3 cameraForward = source.forward;
        Vector3 cameraRight = source.right;
        Vector3 planarForward = Vector3.ProjectOnPlane(cameraForward, Vector3.up);
        Vector3 planarRight = Vector3.ProjectOnPlane(cameraRight, Vector3.up);
        if (!NormalizePlanarBasis(ref planarForward, ref planarRight))
            return false;

        moveBasis = new MoveBasis(sourceName, cameraForward, cameraRight, planarForward, planarRight);
        return true;
    }

    private bool NormalizePlanarBasis(ref Vector3 forward, ref Vector3 right)
    {
        bool hasForward = IsFinite(forward) && forward.sqrMagnitude > 0.0001f;
        if (!hasForward)
            return false;

        forward.Normalize();

        bool hasRight = IsFinite(right) && right.sqrMagnitude > 0.0001f;
        if (hasRight)
            right.Normalize();
        else
            right = Vector3.Cross(Vector3.up, forward).normalized;

        return IsFinite(forward) && IsFinite(right) &&
               forward.sqrMagnitude > 0.0001f &&
               right.sqrMagnitude > 0.0001f;
    }

    private static bool TryNormalizePlanarDirection(Vector3 direction, out Vector3 planarDirection)
    {
        planarDirection = Vector3.ProjectOnPlane(direction, Vector3.up);
        if (!IsFinite(planarDirection) || planarDirection.sqrMagnitude <= 0.0001f)
        {
            planarDirection = Vector3.zero;
            return false;
        }

        planarDirection.Normalize();
        return true;
    }

    private Transform ResolvePlayerHubCameraRoot()
    {
        PlayerHub hub = GetComponentInParent<PlayerHub>();
        if (hub == null && transform.root != null)
            hub = transform.root.GetComponentInChildren<PlayerHub>(true);

        if (hub == null)
            return null;

        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        FieldInfo cameraRootField = typeof(PlayerHub).GetField("cameraRoot", flags);
        if (cameraRootField == null)
            return null;

        object rawValue = cameraRootField.GetValue(hub);
        if (rawValue is GameObject cameraRootObject)
            return cameraRootObject.transform;

        return rawValue as Transform;
    }

    private bool IsUnderInputRouteRoot(Transform source)
    {
        Transform root = transform.root != null ? transform.root : transform;
        return source != null && source.root == root;
    }

    private Transform FindChildByName(string childName)
    {
        if (string.IsNullOrEmpty(childName))
            return null;

        Transform root = transform.root != null ? transform.root : transform;
        return FindChildRecursive(root, childName);
    }

    private static Transform FindChildRecursive(Transform current, string childName)
    {
        if (current == null)
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

    private void UpdateLastPlanarFacingForward(Vector3 moveDirection)
    {
        if (moveDirection.sqrMagnitude > 0.0001f && IsFinite(moveDirection))
        {
            _lastPlanarFacingForward = moveDirection.normalized;
            return;
        }

        if (!IsFinite(_lastPlanarFacingForward) || _lastPlanarFacingForward.sqrMagnitude <= 0.0001f)
            _lastPlanarFacingForward = _lastPlanarBasisForward.sqrMagnitude > 0.0001f
                ? _lastPlanarBasisForward.normalized
                : Vector3.forward;
    }

    private void ApplyMovementForce(Vector3 moveDirection, float maxSpeed, float acceleration, float effectiveControl)
    {
        _lastAppliedMoveForce = Vector3.zero;
        _lastAppliedAcceleration = 0f;
        _lastAccelerationScale = 0f;
        _lastMoveForceApplied = false;
        _lastMoveForceSkipReason = "none";
        _lastMoveVelocityBefore = hipsBody != null ? hipsBody.linearVelocity : Vector3.zero;
        _lastMoveVelocityAfter = _lastMoveVelocityBefore;
        _lastMovePlanarSpeedBefore = hipsBody != null ? GetPlanarVelocity(hipsBody).magnitude : 0f;
        _lastMovePlanarSpeedAfter = _lastMovePlanarSpeedBefore;

        if (hipsBody == null)
        {
            _lastMoveForceSkipReason = "no hipsBody";
            return;
        }

        if (effectiveControl <= 0f)
        {
            _lastMoveForceSkipReason = "controlStrength/effectiveControl <= 0";
            return;
        }

        if (moveDirection.sqrMagnitude <= 0.0001f)
        {
            _lastMoveForceSkipReason = "moveInput too small";
            return;
        }

        Vector3 planarVelocity = GetPlanarVelocity(hipsBody);
        Vector3 desiredVelocity = moveDirection * maxSpeed;
        Vector3 velocityDelta = desiredVelocity - planarVelocity;

        float accelerationScale = 1f;
        float planarSpeed = planarVelocity.magnitude;
        if (planarSpeed > maxSpeed)
        {
            float alignment = Vector3.Dot(moveDirection, planarVelocity.normalized);
            if (alignment > 0f)
                accelerationScale = Mathf.Clamp01(1f - ((planarSpeed - maxSpeed) / Mathf.Max(0.01f, maxSpeed)));
        }

        Vector3 force = Vector3.ClampMagnitude(velocityDelta * acceleration, acceleration) * effectiveControl * accelerationScale;
        _lastAccelerationScale = accelerationScale;
        _lastAppliedAcceleration = acceleration * effectiveControl * accelerationScale;
        _lastAppliedMoveForce = force;
        if (force.sqrMagnitude <= 0.000001f)
        {
            _lastMoveForceSkipReason = "force zero";
            return;
        }

        hipsBody.AddForce(force, ForceMode.Acceleration);
        _lastMoveForceApplied = true;
        _lastMoveVelocityAfter = hipsBody.linearVelocity;
        _lastMovePlanarSpeedAfter = GetPlanarVelocity(hipsBody).magnitude;

        if (chestBody != null && chestForceMultiplier > 0f)
            chestBody.AddForce(force * chestForceMultiplier, ForceMode.Acceleration);
    }

    private void ApplyStopDragAssist(Vector2 moveInput, float effectiveControl)
    {
        _lastAppliedStopDragForce = Vector3.zero;
        _lastStopDragApplied = false;
        _lastStopDragSkipReason = "none";

        if (!_isGrounded)
        {
            _lastStopDragSkipReason = "not grounded";
            return;
        }

        if (effectiveControl <= 0f)
        {
            _lastStopDragSkipReason = "controlStrength/effectiveControl <= 0";
            return;
        }

        if (moveInput.sqrMagnitude > InputDeadzone * InputDeadzone)
        {
            _lastStopDragSkipReason = "move input active";
            return;
        }

        Vector3 planarVelocity = GetPlanarVelocity(hipsBody);
        if (planarVelocity.sqrMagnitude <= 0.0001f)
        {
            _lastStopDragSkipReason = "planar velocity too small";
            return;
        }

        Vector3 dampingForce = Vector3.ClampMagnitude(-planarVelocity * stopDragAssist, MaxStopAssistAcceleration);
        dampingForce *= effectiveControl;
        _lastAppliedStopDragForce = dampingForce;
        if (dampingForce.sqrMagnitude <= 0.000001f)
        {
            _lastStopDragSkipReason = "force zero";
            return;
        }

        hipsBody.AddForce(dampingForce, ForceMode.Acceleration);
        _lastStopDragApplied = true;

        if (chestBody != null && chestForceMultiplier > 0f)
            chestBody.AddForce(dampingForce * chestForceMultiplier, ForceMode.Acceleration);
    }

    private void ApplyUprightTorque(Rigidbody targetBody, float strength, float damping)
    {
        if (targetBody == null || strength <= 0f)
            return;

        Vector3 bodyUp = targetBody.transform.up;
        Vector3 correctionAxis = Vector3.Cross(bodyUp, Vector3.up);
        Vector3 dampingTorque = -targetBody.angularVelocity * damping;
        Vector3 torque = correctionAxis * strength + dampingTorque;
        torque = Vector3.ClampMagnitude(torque, MaxUprightTorque);
        targetBody.AddTorque(torque, ForceMode.Acceleration);
    }

    private void ApplyUprightTorqueOnce(
        Rigidbody targetBody,
        float strength,
        float damping,
        ref Rigidbody body0,
        ref Rigidbody body1,
        ref Rigidbody body2,
        ref Rigidbody body3,
        ref Rigidbody body4)
    {
        if (targetBody == null || strength <= 0f)
            return;

        if (!TryRegisterBodyForTorquePass(targetBody, ref body0, ref body1, ref body2, ref body3, ref body4))
            return;

        ApplyUprightTorque(targetBody, strength, damping);
    }

    private void ApplyTurnTorque(Vector3 moveDirection, float effectiveControl)
    {
        if (effectiveControl <= 0f || moveDirection.sqrMagnitude <= 0.0001f)
            return;

        Vector3 currentForward = Vector3.ProjectOnPlane(hipsBody.transform.forward, Vector3.up);
        if (currentForward.sqrMagnitude <= 0.0001f)
            currentForward = transform.forward;

        currentForward.Normalize();
        Vector3 targetDirection = moveDirection.normalized;
        float yawError = Vector3.SignedAngle(currentForward, targetDirection, Vector3.up);
        float yawAngularVelocity = Vector3.Dot(hipsBody.angularVelocity, Vector3.up);
        float yawTorque = yawError * Mathf.Deg2Rad * turnTorque - yawAngularVelocity * turnDamping;
        yawTorque = Mathf.Clamp(yawTorque, -MaxTurnTorque, MaxTurnTorque) * effectiveControl;

        hipsBody.AddTorque(Vector3.up * yawTorque, ForceMode.Acceleration);
    }

    private void ApplyStandingPoseHold(bool hasMoveInput, float fixedDeltaTime)
    {
        float targetYaw = 0f;
        string appliedYawSource = "InitialForward";
        Vector3 desiredFacingDirection = _initialForwardOnPlane;

        if (IsInputRouteTarget())
        {
            if (TryResolveTargetDesiredFacingDirection(out desiredFacingDirection, out appliedYawSource))
                targetYaw = Vector3.SignedAngle(_initialForwardOnPlane, desiredFacingDirection, Vector3.up);
        }
        else if (yawPoseTowardsMoveDirection &&
                 hasMoveInput &&
                 TryNormalizePlanarDirection(_smoothedMoveWorldDirection, out desiredFacingDirection))
        {
            targetYaw = Vector3.SignedAngle(_initialForwardOnPlane, desiredFacingDirection, Vector3.up);
            appliedYawSource = "MoveDirection";
        }

        _lastPoseTargetYaw = targetYaw;
        _lastDesiredFacingDirection = desiredFacingDirection;
        _lastAppliedYawSource = appliedYawSource;
        if (poseYawSmoothTime <= 0f)
            _currentPoseYaw = targetYaw;
        else
            _currentPoseYaw = Mathf.SmoothDampAngle(_currentPoseYaw, targetYaw, ref _poseYawVelocity, poseYawSmoothTime, Mathf.Infinity, fixedDeltaTime);

        Quaternion yawRotation = Quaternion.AngleAxis(_currentPoseYaw, Vector3.up);
        float strengthMultiplier = ResolvePoseStrengthMultiplier();

        Rigidbody poseBody0 = null;
        Rigidbody poseBody1 = null;
        Rigidbody poseBody2 = null;
        Rigidbody poseBody3 = null;
        Rigidbody poseBody4 = null;

        ApplyRotationSpringOnce(
            hipsBody,
            yawRotation * _initialHipsRotation,
            hipsPoseSpring,
            hipsPoseDamping,
            hipsMaxPoseTorque,
            strengthMultiplier,
            ref poseBody0,
            ref poseBody1,
            ref poseBody2,
            ref poseBody3,
            ref poseBody4);

        ApplyRotationSpringOnce(
            chestBody,
            yawRotation * _initialChestRotation,
            chestPoseSpring,
            chestPoseDamping,
            chestMaxPoseTorque,
            strengthMultiplier,
            ref poseBody0,
            ref poseBody1,
            ref poseBody2,
            ref poseBody3,
            ref poseBody4);

        ApplyRotationSpringOnce(
            headBody,
            yawRotation * _initialHeadRotation,
            headPoseSpring,
            headPoseDamping,
            headMaxPoseTorque,
            strengthMultiplier,
            ref poseBody0,
            ref poseBody1,
            ref poseBody2,
            ref poseBody3,
            ref poseBody4);

        ApplyRotationSpringOnce(
            leftArmBody,
            yawRotation * _initialLeftArmRotation,
            armPoseSpring,
            armPoseDamping,
            armMaxPoseTorque,
            strengthMultiplier,
            ref poseBody0,
            ref poseBody1,
            ref poseBody2,
            ref poseBody3,
            ref poseBody4);

        ApplyRotationSpringOnce(
            rightArmBody,
            yawRotation * _initialRightArmRotation,
            armPoseSpring,
            armPoseDamping,
            armMaxPoseTorque,
            strengthMultiplier,
            ref poseBody0,
            ref poseBody1,
            ref poseBody2,
            ref poseBody3,
            ref poseBody4);
    }

    private float ResolvePoseStrengthMultiplier()
    {
        if (IsInputRouteTarget() && !_isGrounded)
            return 0f;

        float strengthMultiplier = _isGrounded ? groundedPoseStrengthMultiplier : airbornePoseStrengthMultiplier;
        if (_externalControlLocked)
            strengthMultiplier = 0f;
        else
            strengthMultiplier *= Mathf.Clamp01(_externalPoseControlScale);

        return Mathf.Max(0f, strengthMultiplier);
    }

    private bool TryResolveTargetDesiredFacingDirection(out Vector3 desiredFacingDirection, out string appliedYawSource)
    {
        if (TryNormalizePlanarDirection(_lastPlanarBasisForward, out desiredFacingDirection) &&
            IsCameraFacingBasisSource(_lastCameraBasisSource))
        {
            appliedYawSource = "Camera:" + _lastCameraBasisSource;
            return true;
        }

        if (TryNormalizePlanarDirection(_smoothedMoveWorldDirection, out desiredFacingDirection))
        {
            appliedYawSource = "MoveDirection";
            return true;
        }

        if (TryNormalizePlanarDirection(_lastPlanarFacingForward, out desiredFacingDirection))
        {
            appliedYawSource = "LastPlanarFacingForward";
            return true;
        }

        Transform root = transform.root != null ? transform.root : transform;
        if (TryNormalizePlanarDirection(root.forward, out desiredFacingDirection))
        {
            appliedYawSource = "RootForward";
            return true;
        }

        desiredFacingDirection = _initialForwardOnPlane;
        appliedYawSource = "InitialForward";
        return TryNormalizePlanarDirection(desiredFacingDirection, out desiredFacingDirection);
    }

    private static bool IsCameraFacingBasisSource(string source)
    {
        return source == "cameraTransform" ||
               source == "PlayerCamera" ||
               source == "CameraPivot" ||
               source == "PlayerHub.cameraRoot" ||
               source == "Camera.main";
    }

    private void ApplyRotationSpring(
        Rigidbody body,
        Quaternion targetRotation,
        float spring,
        float damping,
        float maxTorque,
        float strengthMultiplier)
    {
        if (body == null || spring <= 0f || maxTorque <= 0f || strengthMultiplier <= 0f)
            return;

        Quaternion delta = targetRotation * Quaternion.Inverse(body.rotation);
        delta.ToAngleAxis(out float angle, out Vector3 axis);

        if (angle > 180f)
            angle -= 360f;

        if (!IsFinite(angle) || !IsFinite(axis) || axis.sqrMagnitude <= 0.0001f)
            return;

        Vector3 torque = axis.normalized * (angle * Mathf.Deg2Rad) * spring - body.angularVelocity * damping;
        torque = Vector3.ClampMagnitude(torque, maxTorque);
        body.AddTorque(torque * strengthMultiplier, ForceMode.Acceleration);
    }

    private void ApplyRotationSpringOnce(
        Rigidbody body,
        Quaternion targetRotation,
        float spring,
        float damping,
        float maxTorque,
        float strengthMultiplier,
        ref Rigidbody body0,
        ref Rigidbody body1,
        ref Rigidbody body2,
        ref Rigidbody body3,
        ref Rigidbody body4)
    {
        if (body == null || spring <= 0f || maxTorque <= 0f || strengthMultiplier <= 0f)
            return;

        if (!TryRegisterBodyForTorquePass(body, ref body0, ref body1, ref body2, ref body3, ref body4))
            return;

        ApplyRotationSpring(body, targetRotation, spring, damping, maxTorque, strengthMultiplier);
    }

    private static bool TryRegisterBodyForTorquePass(
        Rigidbody body,
        ref Rigidbody body0,
        ref Rigidbody body1,
        ref Rigidbody body2,
        ref Rigidbody body3,
        ref Rigidbody body4)
    {
        if (body == null)
            return false;

        if (ReferenceEquals(body, body0) ||
            ReferenceEquals(body, body1) ||
            ReferenceEquals(body, body2) ||
            ReferenceEquals(body, body3) ||
            ReferenceEquals(body, body4))
        {
            return false;
        }

        if (body0 == null)
        {
            body0 = body;
            return true;
        }

        if (body1 == null)
        {
            body1 = body;
            return true;
        }

        if (body2 == null)
        {
            body2 = body;
            return true;
        }

        if (body3 == null)
        {
            body3 = body;
            return true;
        }

        if (body4 == null)
        {
            body4 = body;
            return true;
        }

        return false;
    }

    private bool CheckGrounded()
    {
        Vector3 origin = hipsBody.worldCenterOfMass + Vector3.up * groundCheckRadius;
        float distance = Mathf.Max(0.01f, groundCheckDistance + groundCheckRadius);
        _lastGroundProbeOrigin = origin;
        _lastGroundProbeRadius = groundCheckRadius;
        _lastGroundProbeDistance = distance;
        _lastGroundRbCenterOfMassY = hipsBody.worldCenterOfMass.y;
        Collider bodyCollider = hipsBody.GetComponent<Collider>();
        _lastGroundColliderBottomY = bodyCollider != null ? bodyCollider.bounds.min.y : float.NaN;

        bool hit = Physics.SphereCast(
            origin,
            groundCheckRadius,
            Vector3.down,
            out RaycastHit hitInfo,
            distance,
            groundMask,
            QueryTriggerInteraction.Ignore);
        _lastGroundHit = hit;
        _lastGroundHitName = hit && hitInfo.collider != null ? hitInfo.collider.name : "<none>";
        _lastGroundHitLayer = hit && hitInfo.collider != null ? hitInfo.collider.gameObject.layer : -1;
        _lastGroundHitDistance = hit ? hitInfo.distance : float.NaN;
        _lastGroundIgnoreLayerCollision = hit && hitInfo.collider != null
            && Physics.GetIgnoreLayerCollision(hipsBody.gameObject.layer, hitInfo.collider.gameObject.layer);
        return hit;
    }

    private void CaptureInputRoutePositionDelta()
    {
        if (hipsBody == null)
        {
            _lastInputRoutePositionDelta = Vector3.zero;
            _hasPreviousInputRouteBodyPosition = false;
            return;
        }

        Vector3 currentPosition = hipsBody.position;
        _lastInputRoutePositionDelta = _hasPreviousInputRouteBodyPosition
            ? currentPosition - _previousInputRouteBodyPosition
            : Vector3.zero;
        _previousInputRouteBodyPosition = currentPosition;
        _hasPreviousInputRouteBodyPosition = true;
    }

    private static Vector3 GetPlanarVelocity(Rigidbody targetBody)
    {
        if (targetBody == null)
            return Vector3.zero;

        Vector3 velocity = targetBody.linearVelocity;
        velocity.y = 0f;
        return velocity;
    }

    private static float GetVerticalVelocity(Rigidbody targetBody)
    {
        return targetBody != null ? targetBody.linearVelocity.y : 0f;
    }

    private void LogDebugState(bool hasMoveInput, bool poseHoldActive)
    {
        if (!debugLogs || Time.time < _nextDebugLogTime)
            return;

        _nextDebugLogTime = Time.time + 1f;
        string hipsVelocity = hipsBody != null ? FormatVector3(hipsBody.linearVelocity) : "<null>";
        string motorPath = GetTransformPath(transform);
        string hipsPath = GetBodyPath(hipsBody);
        string hipsState = hipsBody != null
            ? $"isKinematic={hipsBody.isKinematic} useGravity={hipsBody.useGravity} detectCollisions={hipsBody.detectCollisions} constraints={hipsBody.constraints}"
            : "hipsBody=<null>";

        Debug.Log(
            $"[HamsterFullRagdollMotor:{gameObject.name}] objectPath={motorPath} hipsBody={GetBodyName(hipsBody)} hipsPath={hipsPath} controlStrength={controlStrength:F2} effectiveControl={_lastEffectiveControl:F2} rawInput={FormatVector2(_lastRawMoveInput)} smoothedInput={FormatVector2(_smoothedMoveInput)} hasMoveInput={hasMoveInput} moveWorldDirection={FormatVector3(_smoothedMoveWorldDirection)} grounded={_isGrounded} maxWalkSpeed={maxWalkSpeed:F2} maxSprintSpeed={maxSprintSpeed:F2} walkAcceleration={walkAcceleration:F2} sprintAcceleration={sprintAcceleration:F2} selectedAcceleration={_lastSelectedAcceleration:F2} appliedAcceleration={_lastAppliedAcceleration:F2} accelerationScale={_lastAccelerationScale:F2} finalForce={FormatVector3(_lastAppliedMoveForce)} moveForceApplied={_lastMoveForceApplied} moveForceSkip='{_lastMoveForceSkipReason}' stopDragAssist={stopDragAssist:F2} airControlMultiplier={airControlMultiplier:F2} stopDragForce={FormatVector3(_lastAppliedStopDragForce)} stopDragApplied={_lastStopDragApplied} stopDragSkip='{_lastStopDragSkipReason}' hipsVelocity={hipsVelocity} planarSpeed={CurrentPlanarSpeed:F2} verticalVelocity={GetVerticalVelocity(hipsBody):F2} enableJump={enableJump} jumpBuffer={_jumpBufferTimer:F2} jumpCooldown={_jumpCooldownTimer:F2} jumpConsumed={_jumpConsumedThisFixedStep} {hipsState} maxSpeed={_lastMaxSpeed:F2} poseHold={poseHoldActive} yawTarget={_lastPoseTargetYaw:F1}",
            this);
    }

    private void LogInputRouteMotor(bool hasMoveInput)
    {
        if (!ShouldLogInputRouteMotor())
            return;

        string hipsState = hipsBody != null
            ? $"rbExists=True rbIsKinematic={hipsBody.isKinematic} rbUseGravity={hipsBody.useGravity} rbSleeping={hipsBody.IsSleeping()} rbConstraints={hipsBody.constraints} rbVelocity={FormatVector3(hipsBody.linearVelocity)}"
            : "rbExists=False";
        bool canApplyMoveForce =
            hipsBody != null &&
            !hipsBody.isKinematic &&
            _lastEffectiveControl > 0f &&
            _smoothedMoveWorldDirection.sqrMagnitude > 0.0001f;

        Debug.Log(
            $"[InputRoute/Motor:{GetInputRouteObjectName()}] enabled={enabled} active={gameObject.activeInHierarchy} scene={SceneManager.GetActiveScene().name} inputSource={_lastInputSource} legacyInputUnavailable={_legacyInputUnavailable} rawInput={FormatVector2(_lastRawMoveInput)} smoothedInput={FormatVector2(_smoothedMoveInput)} hasMoveInput={hasMoveInput} moveWorldDirection={FormatVector3(_smoothedMoveWorldDirection)} grounded={_isGrounded} sprintHeld={_lastSprintHeld} canApplyMoveForce={canApplyMoveForce} controlStrength={controlStrength:F2} effectiveControl={_lastEffectiveControl:F2} planarSpeed={CurrentPlanarSpeed:F2} selectedMaxSpeed={_lastSelectedMaxSpeed:F2} addForceApplied={_lastMoveForceApplied} force={FormatVector3(_lastAppliedMoveForce)} skipReason='{_lastMoveForceSkipReason}' jumpBuffer={_jumpBufferTimer:F2} jumpConsumed={_jumpConsumedThisFixedStep} {hipsState}",
            this);
        LogInputRouteGround();
        LogInputRouteForce();
        LogMoveBasis();
        LogFacingTilt(hasMoveInput);
    }

    private void LogInputRouteGround()
    {
        Debug.Log(
            $"[InputRoute/Ground:{GetInputRouteObjectName()}] grounded={_isGrounded} probeOrigin={FormatVector3(_lastGroundProbeOrigin)} probeRadius={_lastGroundProbeRadius:F2} probeDistance={_lastGroundProbeDistance:F2} colliderBottomY={FormatFloat(_lastGroundColliderBottomY)} rbCenterOfMassY={FormatFloat(_lastGroundRbCenterOfMassY)} hit={_lastGroundHit} hitName={_lastGroundHitName} hitLayer={_lastGroundHitLayer} hitDistance={FormatFloat(_lastGroundHitDistance)} groundMask={groundMask.value} ignoreLayerCollision={_lastGroundIgnoreLayerCollision}",
            this);
    }

    private void LogInputRouteForce()
    {
        Debug.Log(
            $"[InputRoute/Force:{GetInputRouteObjectName()}] rawInput={FormatVector2(_lastRawMoveInput)} smoothedInput={FormatVector2(_smoothedMoveInput)} moveDir={FormatVector3(_smoothedMoveWorldDirection)} grounded={_isGrounded} effectiveControl={_lastEffectiveControl:F2} selectedAcceleration={_lastSelectedAcceleration:F2} force={FormatVector3(_lastAppliedMoveForce)} forceMode=Acceleration addForceApplied={_lastMoveForceApplied} rbLinearBefore={FormatVector3(_lastMoveVelocityBefore)} rbLinearAfter={FormatVector3(_lastMoveVelocityAfter)} planarSpeedBefore={_lastMovePlanarSpeedBefore:F2} planarSpeedAfter={_lastMovePlanarSpeedAfter:F2} positionDelta={FormatVector3(_lastInputRoutePositionDelta)} skipReason='{_lastMoveForceSkipReason}'",
            this);
    }

    private void LogMoveBasis()
    {
        Transform root = transform.root != null ? transform.root : transform;
        Transform cameraPivot = FindChildByName(CameraPivotName);
        Transform playerCamera = FindChildByName(PlayerCameraName);
        string objectName = GetInputRouteObjectName();
        string cameraPivotYaw = cameraPivot != null
            ? FormatFloat(Mathf.DeltaAngle(0f, cameraPivot.eulerAngles.y))
            : "<none>";
        string playerCameraRotation = playerCamera != null
            ? FormatQuaternion(playerCamera.rotation)
            : "<null>";

        Debug.Log(
            $"[MSMoveBasis:{objectName}] rawInput={FormatVector2(_lastRawMoveInput)} smoothedInput={FormatVector2(_smoothedMoveInput)} cameraBasisSource={_lastCameraBasisSource} cameraForward={FormatVector3(_lastCameraBasisForward)} cameraRight={FormatVector3(_lastCameraBasisRight)} planarForward={FormatVector3(_lastPlanarBasisForward)} planarRight={FormatVector3(_lastPlanarBasisRight)}",
            this);
        Debug.Log(
            $"[MSMoveBasis:{objectName}] moveWorldDirectionBefore={FormatVector3(_lastMoveWorldDirectionBefore)} moveWorldDirectionAfter={FormatVector3(_lastMoveWorldDirectionAfter)} lastPlanarFacingForward={FormatVector3(_lastPlanarFacingForward)} force={FormatVector3(_lastAppliedMoveForce)}",
            this);
        Debug.Log(
            $"[MSMoveBasis:{objectName}] rootRotation={FormatQuaternion(root.rotation)} rootYaw={FormatFloat(Mathf.DeltaAngle(0f, root.eulerAngles.y))} CameraPivotYaw={cameraPivotYaw} PlayerCameraWorldRotation={playerCameraRotation}",
            this);
    }

    private void LogFacingTilt(bool hasMoveInput)
    {
        Transform root = transform.root != null ? transform.root : transform;
        Transform visualRoot = FindChildByName(VisualPreviewRootName);
        string objectName = GetInputRouteObjectName();
        string hipsForward = hipsBody != null ? FormatVector3(hipsBody.transform.forward) : "<null>";
        string hipsUpDot = hipsBody != null ? FormatFloat(Vector3.Dot(hipsBody.transform.up, Vector3.up)) : "<null>";
        string chestUpDot = chestBody != null ? FormatFloat(Vector3.Dot(chestBody.transform.up, Vector3.up)) : "<null>";
        string visualForward = visualRoot != null ? FormatVector3(visualRoot.forward) : "<null>";
        string visualUpDot = visualRoot != null ? FormatFloat(Vector3.Dot(visualRoot.up, Vector3.up)) : "<null>";
        string hipsRotation = hipsBody != null ? FormatQuaternion(hipsBody.rotation) : "<null>";

        Debug.Log(
            $"[MSFacing:{objectName}] cameraBasisSource={_lastCameraBasisSource} cameraPlanarForward={FormatVector3(_lastPlanarBasisForward)} desiredFacingDirection={FormatVector3(_lastDesiredFacingDirection)} moveWorldDirection={FormatVector3(_smoothedMoveWorldDirection)} targetYaw={_lastPoseTargetYaw:F1} currentPoseYaw={_currentPoseYaw:F1} appliedYawSource={_lastAppliedYawSource} hasMoveInput={hasMoveInput} planarSpeed={CurrentPlanarSpeed:F2} grounded={_isGrounded}",
            this);
        Debug.Log(
            $"[MSTilt:{objectName}] hipsForward={hipsForward} hipsUpDot={hipsUpDot} chestUpDot={chestUpDot} visualForward={visualForward} visualUpDot={visualUpDot} rootRotation={FormatQuaternion(root.rotation)} hipsBody.rotation={hipsRotation}",
            this);
    }

    private bool ShouldLogInputRouteMotor()
    {
        if (!debugInputRouteLogs)
            return false;

        if (!IsInputRouteTarget())
            return false;

        if (Time.unscaledTime < _nextInputRouteMotorLogTime)
            return false;

        _nextInputRouteMotorLogTime = Time.unscaledTime + InputRouteLogInterval;
        return true;
    }

    private bool IsInputRouteTarget()
    {
        Transform root = transform.root;
        return root != null &&
               (root.name == InputRouteTargetName || root.name == InputRouteTargetName + "(Clone)");
    }

    private string GetInputRouteObjectName()
    {
        Transform root = transform.root;
        return root != null ? root.name : gameObject.name;
    }

    private void LogMotorEnabledState()
    {
        if (!debugLogs)
            return;

        Debug.Log(
            $"[HamsterFullRagdollMotor:{gameObject.name}] enabled objectPath={GetTransformPath(transform)} controlStrength={controlStrength:F2} maxWalkSpeed={maxWalkSpeed:F2} walkAcceleration={walkAcceleration:F2} stopDragAssist={stopDragAssist:F2} hipsBodyPath={GetBodyPath(hipsBody)} chestBodyPath={GetBodyPath(chestBody)} groundMask={groundMask.value}",
            this);
    }

    private static string FormatVector2(Vector2 value)
    {
        return $"({value.x:F2},{value.y:F2})";
    }

    private static string FormatVector3(Vector3 value)
    {
        return $"({value.x:F2},{value.y:F2},{value.z:F2})";
    }

    private static string FormatFloat(float value)
    {
        return float.IsNaN(value) ? "<none>" : value.ToString("F2");
    }

    private static string FormatQuaternion(Quaternion value)
    {
        return $"({value.x:F2},{value.y:F2},{value.z:F2},{value.w:F2})";
    }

    private static string GetBodyName(Rigidbody body)
    {
        return body != null ? body.name : "<null>";
    }

    private static string GetBodyPath(Rigidbody body)
    {
        return body != null ? GetTransformPath(body.transform) : "<null>";
    }

    private static string GetTransformPath(Transform target)
    {
        if (target == null)
            return "<null>";

        string path = target.name;
        Transform current = target.parent;
        while (current != null)
        {
            path = current.name + "/" + path;
            current = current.parent;
        }

        return path;
    }

    private static bool IsFinite(Vector3 value)
    {
        return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
    }

    private static bool IsFinite(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }

    private void LogMissingRequiredReferences()
    {
        if (_missingRequiredReferenceLogged)
            return;

        _missingRequiredReferenceLogged = true;
        Debug.LogError("[HamsterFullRagdollMotor] hipsBody and chestBody are required. Component disabled.", this);
    }

    private void Log(string message)
    {
        if (debugLogs)
            Debug.Log($"[HamsterFullRagdollMotor:{gameObject.name}] {message}", this);
    }

    private void OnValidate()
    {
        maxWalkSpeed = Mathf.Max(0f, maxWalkSpeed);
        maxSprintSpeed = Mathf.Max(0f, maxSprintSpeed);
        walkAcceleration = Mathf.Max(0f, walkAcceleration);
        sprintAcceleration = Mathf.Max(0f, sprintAcceleration);
        chestForceMultiplier = Mathf.Max(0f, chestForceMultiplier);
        turnTorque = Mathf.Max(0f, turnTorque);
        turnDamping = Mathf.Max(0f, turnDamping);
        inputSmoothTime = Mathf.Max(0f, inputSmoothTime);
        stopDragAssist = Mathf.Max(0f, stopDragAssist);
        airControlMultiplier = Mathf.Max(0f, airControlMultiplier);
        controlStrength = Mathf.Max(0f, controlStrength);
        sprintLogInterval = Mathf.Max(0.02f, sprintLogInterval);
        sprintPivotDotThreshold = Mathf.Clamp(sprintPivotDotThreshold, -1f, 1f);
        sprintPivotAccelerationMultiplier = Mathf.Max(1f, sprintPivotAccelerationMultiplier);
        sprintPivotMinPlanarSpeed = Mathf.Max(0f, sprintPivotMinPlanarSpeed);
        jumpVelocityChange = Mathf.Max(0f, jumpVelocityChange);
        jumpCooldown = Mathf.Max(0f, jumpCooldown);
        jumpBufferTime = Mathf.Max(0f, jumpBufferTime);
        jumpCoyoteTime = Mathf.Max(0f, jumpCoyoteTime);
        maxUpwardVelocityBeforeJump = Mathf.Max(0f, maxUpwardVelocityBeforeJump);
        landingLogMinVerticalSpeed = Mathf.Max(0f, landingLogMinVerticalSpeed);
        jumpVisualIntensity = Mathf.Max(0f, jumpVisualIntensity);
        landingVisualIntensity = Mathf.Max(0f, landingVisualIntensity);
        uprightStrength = Mathf.Max(0f, uprightStrength);
        uprightDamping = Mathf.Max(0f, uprightDamping);
        chestUprightMultiplier = Mathf.Max(0f, chestUprightMultiplier);
        groundCheckRadius = Mathf.Max(0.01f, groundCheckRadius);
        groundCheckDistance = Mathf.Max(0.01f, groundCheckDistance);
        poseYawSmoothTime = Mathf.Max(0f, poseYawSmoothTime);
        hipsPoseSpring = Mathf.Max(0f, hipsPoseSpring);
        hipsPoseDamping = Mathf.Max(0f, hipsPoseDamping);
        hipsMaxPoseTorque = Mathf.Max(0f, hipsMaxPoseTorque);
        chestPoseSpring = Mathf.Max(0f, chestPoseSpring);
        chestPoseDamping = Mathf.Max(0f, chestPoseDamping);
        chestMaxPoseTorque = Mathf.Max(0f, chestMaxPoseTorque);
        headPoseSpring = Mathf.Max(0f, headPoseSpring);
        headPoseDamping = Mathf.Max(0f, headPoseDamping);
        headMaxPoseTorque = Mathf.Max(0f, headMaxPoseTorque);
        armPoseSpring = Mathf.Max(0f, armPoseSpring);
        armPoseDamping = Mathf.Max(0f, armPoseDamping);
        armMaxPoseTorque = Mathf.Max(0f, armMaxPoseTorque);
        groundedPoseStrengthMultiplier = Mathf.Max(0f, groundedPoseStrengthMultiplier);
        airbornePoseStrengthMultiplier = Mathf.Max(0f, airbornePoseStrengthMultiplier);
    }

    private void OnDrawGizmosSelected()
    {
        if (!drawDebugGizmos || hipsBody == null)
            return;

        Gizmos.color = _isGrounded ? Color.green : Color.yellow;
        Vector3 origin = hipsBody.worldCenterOfMass + Vector3.up * groundCheckRadius;
        Vector3 end = origin + Vector3.down * Mathf.Max(0.01f, groundCheckDistance + groundCheckRadius);
        Gizmos.DrawWireSphere(end, groundCheckRadius);

        Gizmos.color = Color.cyan;
        Gizmos.DrawLine(hipsBody.worldCenterOfMass, hipsBody.worldCenterOfMass + _smoothedMoveWorldDirection);
    }
}
