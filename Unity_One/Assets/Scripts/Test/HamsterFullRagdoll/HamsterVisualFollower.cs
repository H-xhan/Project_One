using UnityEngine;

public sealed class HamsterVisualFollower : MonoBehaviour
{
    private const int BaseLayerIndex = 0;
    private const float ClipHeightBoostDuration = 0.16f;
    private const float ClipHeightLogInterval = 0.75f;
    private const float TargetVisualFacingLogInterval = 0.5f;

    [Header("References")]
    [SerializeField] private Rigidbody targetBody;
    [SerializeField] private Transform visualRoot;
    [SerializeField] private Animator visualAnimator;

    [Header("Follow Mode")]
    [SerializeField] private bool captureInitialLocalTransformOnEnable = true;
    [SerializeField] private bool resetVisualLocalTransformOnEnable = true;
    [SerializeField] private bool visualRootIsChildOfTarget = true;
    [SerializeField] private Vector3 visualLocalOffset = Vector3.zero;
    [SerializeField] private Vector3 visualLocalEulerOffset = Vector3.zero;

    [Header("Rotation Follow")]
    [SerializeField] private bool faceMoveDirection = true;
    [SerializeField] private bool keepLastMoveYawWhenIdle = true;
    [SerializeField] private float minSpeedToFaceMove = 0.12f;
    [SerializeField] private float yawSmoothTime = 0.22f;
    [SerializeField] private float visualYawOffsetDegrees = 0f;

    [Header("Body Lean")]
    [SerializeField] private bool enableBodyLean = true;
    [SerializeField] private float speedForMaxLean = 2.5f;
    [SerializeField] private float maxForwardLeanDegrees = 9f;
    [SerializeField] private float maxSideLeanDegrees = 14f;
    [SerializeField] private float leanSmoothTime = 0.12f;
    [SerializeField] private bool invertForwardLean = false;
    [SerializeField] private bool invertSideLean = false;

    [Header("Soft Local Lag")]
    [SerializeField] private bool enableLocalLag = true;
    [SerializeField] private float maxBackLag = 0.06f;
    [SerializeField] private float maxSideLag = 0.04f;
    [SerializeField] private float lagSmoothTime = 0.10f;

    [Header("Vertical Grounding")]
    [SerializeField] private bool enableSpeedBasedVisualHeight = false;
    [SerializeField] private float idleVisualYOffset = 0.07f;
    [SerializeField] private float movingVisualYOffset = 0.10f;
    [SerializeField] private float speedForMovingVisualYOffset = 0.6f;
    [SerializeField] private float visualHeightSmoothTime = 0.08f;

    [Header("Clip State Height Stabilization")]
    [SerializeField] private bool enableClipStateHeightOffsets = true;
    [SerializeField] private Animator stateHeightAnimator;
    [SerializeField] private bool autoUseVisualAnimatorForHeight = true;
    [SerializeField] private string idleStateName = "Idle";
    [SerializeField] private string walkStateName = "Walk";
    [SerializeField] private string runStateName = "Run";
    [SerializeField] private string jumpStateName = "JumpUp";
    [SerializeField] private float idleStateYOffset = 0.0f;
    [SerializeField] private float walkStateYOffset = -0.02f;
    [SerializeField] private float runStateYOffset = 0.0f;
    [SerializeField] private float jumpStateYOffset = 0.04f;
    [SerializeField] private float sprintPivotYOffset = 0.04f;
    [SerializeField] private float landingYOffset = 0.05f;
    [SerializeField] private float clipHeightSmoothTime = 0.06f;
    [SerializeField] private float maxClipHeightYOffset = 0.12f;
    [SerializeField] private bool debugClipHeightLogs = false;

    [Header("Clip State Height Timing")]
    [SerializeField] private bool useNextAnimatorStateForClipHeight = true;
    [SerializeField] private bool prioritizeWalkNextStateHeight = true;
    [SerializeField] private float clipHeightDownSmoothTime = 0.015f;
    [SerializeField] private float clipHeightUpSmoothTime = 0.05f;
    [SerializeField] private bool snapDownOnWalkEnter = false;
    [SerializeField] private float walkEnterSnapMaxDelta = 0.04f;

    [Header("Acceleration Wobble")]
    [SerializeField] private bool enableAccelerationWobble = false;
    [SerializeField] private float accelerationForMaxWobble = 5.0f;
    [SerializeField] private float maxAccelerationForwardWobbleDegrees = 8.0f;
    [SerializeField] private float maxAccelerationSideWobbleDegrees = 10.0f;
    [SerializeField] private float accelerationWobbleSmoothTime = 0.12f;
    [SerializeField] private float accelerationWobbleReturnSmoothTime = 0.18f;
    [SerializeField] private bool invertAccelerationForwardWobble = false;
    [SerializeField] private bool invertAccelerationSideWobble = false;

    [Header("Stop Overshoot")]
    [SerializeField] private bool enableStopOvershoot = false;
    [SerializeField] private float stopOvershootSpeedThreshold = 0.45f;
    [SerializeField] private float stopOvershootInputThreshold = 0.08f;
    [SerializeField] private float maxStopOvershootDegrees = 8.0f;
    [SerializeField] private float stopOvershootDuration = 0.18f;
    [SerializeField] private float stopOvershootCooldown = 0.12f;
    [SerializeField] private bool invertStopOvershoot = false;

    [Header("Turn Wobble")]
    [SerializeField] private bool enableTurnWobble = false;
    [SerializeField] private float turnWobbleYawDeltaForMax = 90.0f;
    [SerializeField] private float maxTurnWobbleDegrees = 10.0f;
    [SerializeField] private float turnWobbleSmoothTime = 0.12f;
    [SerializeField] private bool invertTurnWobble = false;

    [Header("Impact Visual Reaction")]
    [SerializeField] private bool enableImpactVisualReaction = true;
    [SerializeField] private float impactForMaxWobble = 5.0f;
    [SerializeField] private float maxImpactForwardWobbleDegrees = 10.0f;
    [SerializeField] private float maxImpactSideWobbleDegrees = 12.0f;
    [SerializeField] private float impactWobbleSmoothTime = 0.08f;
    [SerializeField] private float impactWobbleReturnSmoothTime = 0.20f;
    [SerializeField] private bool invertImpactForwardWobble = false;
    [SerializeField] private bool invertImpactSideWobble = false;

    [Header("Jump Visual Reaction")]
    [SerializeField] private bool enableJumpVisualReaction = true;
    [SerializeField] private float jumpStretchAmount = 0.08f;
    [SerializeField] private float jumpPitchDegrees = 6.0f;
    [SerializeField] private float jumpVisualDuration = 0.16f;
    [SerializeField] private float landingSquashAmount = 0.03f;
    [SerializeField] private float landingPitchDegrees = 1.0f;
    [SerializeField] private float landingVisualDuration = 0.18f;
    [SerializeField] private float jumpVisualReturnSmoothTime = 0.12f;
    [SerializeField] private bool invertJumpPitch = false;
    [SerializeField] private bool invertLandingPitch = false;

    [Header("Animator")]
    [SerializeField] private bool updateAnimator = true;
    [SerializeField] private bool disableAnimatorRootMotion = true;
    [SerializeField] private string speedParameter = "Speed";
    [SerializeField] private string move01Parameter = "Move01";
    [SerializeField] private float animatorDampTime = 0.10f;
    [SerializeField] private float speedForMove01 = 2.5f;

    [Header("Locomotion Animator Parameters")]
    [SerializeField] private HamsterFullRagdollMotor motorStateSource;
    [SerializeField] private bool autoFindMotorStateSource = true;
    [SerializeField] private string groundedParameter = "Grounded";
    [SerializeField] private string verticalVelocityParameter = "VerticalVelocity";
    [SerializeField] private string sprint01Parameter = "Sprint01";
    [SerializeField] private float sprint01DampTime = 0.08f;
    [SerializeField] private bool updateGroundedParameter = true;
    [SerializeField] private bool updateVerticalVelocityParameter = true;
    [SerializeField] private bool updateSprintParameter = true;

    [Header("Recovery Visual")]
    [SerializeField] private HamsterMotorShellRagdollRecoveryAdapter recoveryStateSource;
    [SerializeField] private bool autoFindRecoveryStateSource = true;
    [SerializeField] private bool followBodyRotationDuringRecovery = true;
    [SerializeField] private float recoveryVisualRotationSmoothTime = 0.08f;

    [Header("Debug")]
    [SerializeField] private bool debugLogs = false;
    [SerializeField] private bool drawGizmos = false;

    private Vector3 _initialLocalPosition;
    private Quaternion _initialLocalRotation = Quaternion.identity;
    private Vector3 _initialLocalScale = Vector3.one;
    private Vector3 _positionSmoothVelocity;
    private Vector3 _lagSmoothVelocity;
    private float _yawVelocity;
    private float _currentYaw;
    private float _currentForwardLean;
    private float _currentSideLean;
    private float _forwardLeanVelocity;
    private float _sideLeanVelocity;
    private Vector3 _lastPlanarMoveDirection = Vector3.forward;
    private float _lastLogTime;
    private float _currentVisualYOffset;
    private float _visualYOffsetVelocity;
    private float _currentClipHeightYOffset;
    private float _targetClipHeightYOffset;
    private float _clipHeightYOffsetVelocity;
    private float _clipHeightPivotBoostTimer;
    private float _clipHeightLandingBoostTimer;
    private float _previousClipHeightVerticalVelocity;
    private float _nextClipHeightLogTime;
    private string _currentClipHeightStateName = "None";
    private string _clipHeightCurrentStateName = "None";
    private string _clipHeightNextStateName = "None";
    private string _clipHeightSmoothMode = "Normal";
    private bool _clipHeightGroundedInitialized;
    private bool _wasClipHeightGrounded;
    private bool _warnedClipHeightFallLand;
    private bool _clipHeightIsInTransition;
    private bool _usedNextStateForHeight;
    private bool _clipHeightCurrentOrNextIsWalk;
    private bool _wasClipHeightCurrentOrNextWalk;
    private bool _loggedClipHeightCurrentTransition;
    private Vector3 _previousPlanarVelocity;
    private Vector3 _currentPlanarAcceleration;
    private float _accelForwardWobble;
    private float _accelForwardWobbleVelocity;
    private float _accelSideWobble;
    private float _accelSideWobbleVelocity;
    private float _stopOvershoot;
    private float _stopOvershootVelocity;
    private float _stopOvershootTimer;
    private float _stopOvershootCooldownTimer;
    private float _lastPlanarSpeed;
    private float _previousYawForTurnWobble;
    private float _turnWobble;
    private float _turnWobbleVelocity;
    private float _stopOvershootDirection = 1f;
    private float _impactForwardWobble;
    private float _impactForwardWobbleVelocity;
    private float _impactSideWobble;
    private float _impactSideWobbleVelocity;
    private float _targetImpactForwardWobble;
    private float _targetImpactSideWobble;
    private Vector3 _initialVisualLocalScale = Vector3.one;
    private bool _hasInitialVisualLocalScale;
    private float _jumpStretch;
    private float _jumpStretchVelocity;
    private float _jumpPitch;
    private float _jumpPitchVelocity;
    private float _landingSquash;
    private float _landingSquashVelocity;
    private float _landingPitch;
    private float _landingPitchVelocity;
    private float _jumpVisualTimer;
    private float _landingVisualTimer;
    private float _jumpVisualIntensity;
    private float _landingVisualIntensity;

    private Vector3 _currentLagOffset;
    private Animator _cachedAnimator;
    private string _cachedSpeedParameter;
    private string _cachedMove01Parameter;
    private string _cachedGroundedParameter;
    private string _cachedVerticalVelocityParameter;
    private string _cachedSprint01Parameter;
    private int _speedParameterHash;
    private int _move01ParameterHash;
    private int _groundedParameterHash;
    private int _verticalVelocityParameterHash;
    private int _sprint01ParameterHash;
    private bool _hasSpeedParameter;
    private bool _hasMove01Parameter;
    private bool _hasGroundedParameter;
    private bool _hasVerticalVelocityParameter;
    private bool _hasSprint01Parameter;
    private bool _missingVisualRootLogged;
    private bool _missingTargetBodyLogged;
    private float _nextTargetVisualFacingLogTime;
    private string _lastVisualFacingSource = "Velocity";
    private Quaternion _initialBodyToVisualRotation = Quaternion.identity;
    private bool _hasInitialBodyToVisualRotation;

    private void Awake()
    {
        ResolveReferences();
        CacheInitialLocalTransform();
        ConfigureAnimator();
    }

    private void OnEnable()
    {
        ResolveReferences();

        if (visualRoot != null)
        {
            if (captureInitialLocalTransformOnEnable)
                CacheInitialLocalTransform();

            if (resetVisualLocalTransformOnEnable)
                ResetVisualLocalTransform();
        }

        ConfigureAnimator();
        ResetSmoothingState();
    }

    private void LateUpdate()
    {
        if (targetBody == null || visualRoot == null)
            return;

        float deltaTime = Time.deltaTime;
        Vector3 velocity = targetBody.linearVelocity;
        Vector3 planarVelocity = velocity;
        planarVelocity.y = 0f;

        float planarSpeed = planarVelocity.magnitude;
        Vector3 localVelocity = targetBody.transform.InverseTransformDirection(planarVelocity);

        if (ShouldStabilizeMainScenesAirborneVisuals())
            ResetMainScenesAirborneVisualState();

        UpdateMotionWobble(planarVelocity, planarSpeed, deltaTime);
        UpdateJumpVisualReaction(deltaTime);
        UpdateClipStateHeightOffset(planarVelocity, planarSpeed, velocity.y, deltaTime);
        UpdateVisualPosition(localVelocity, planarSpeed);
        UpdateVisualRotation(planarVelocity, planarSpeed, localVelocity);
        UpdateJumpVisualScale();
        UpdateAnimator(planarSpeed);
        LogDebugState(velocity, planarSpeed, localVelocity);
        StorePreviousMotionState(planarVelocity, planarSpeed);
    }

    private void ResolveReferences()
    {
        if (targetBody == null)
            targetBody = GetComponentInParent<Rigidbody>();

        if (targetBody == null)
            targetBody = GetComponent<Rigidbody>();

        if (targetBody == null && !_missingTargetBodyLogged)
        {
            _missingTargetBodyLogged = true;
            Debug.LogWarning("[HamsterVisualFollower] targetBody is missing. Visual follow is disabled until assigned.", this);
        }

        if (visualRoot == null && !_missingVisualRootLogged)
        {
            _missingVisualRootLogged = true;
            Debug.LogWarning("[HamsterVisualFollower] visualRoot is missing. Assign VisualPreviewRoot explicitly.", this);
        }

        if (autoFindMotorStateSource && motorStateSource == null)
        {
            motorStateSource = GetComponent<HamsterFullRagdollMotor>();
            if (motorStateSource == null)
                motorStateSource = GetComponentInParent<HamsterFullRagdollMotor>();
        }

        if (autoFindRecoveryStateSource && recoveryStateSource == null)
        {
            recoveryStateSource = GetComponent<HamsterMotorShellRagdollRecoveryAdapter>();
            if (recoveryStateSource == null)
                recoveryStateSource = GetComponentInParent<HamsterMotorShellRagdollRecoveryAdapter>();
        }
    }

    private void CacheInitialLocalTransform()
    {
        if (visualRoot == null)
            return;

        _initialLocalPosition = visualRoot.localPosition;
        _initialLocalRotation = visualRoot.localRotation;
        _initialLocalScale = visualRoot.localScale;
        _initialVisualLocalScale = visualRoot.localScale;
        _hasInitialVisualLocalScale = true;
        CacheInitialBodyToVisualRotation();
    }

    private void ResetVisualLocalTransform()
    {
        visualRoot.localPosition = _initialLocalPosition;
        visualRoot.localRotation = _initialLocalRotation;
        visualRoot.localScale = _initialLocalScale;
    }

    private void ResetSmoothingState()
    {
        _positionSmoothVelocity = Vector3.zero;
        _lagSmoothVelocity = Vector3.zero;
        _currentLagOffset = Vector3.zero;
        _yawVelocity = 0f;
        _currentYaw = 0f;
        _currentForwardLean = 0f;
        _currentSideLean = 0f;
        _forwardLeanVelocity = 0f;
        _sideLeanVelocity = 0f;
        _currentVisualYOffset = enableSpeedBasedVisualHeight ? idleVisualYOffset : visualLocalOffset.y;
        _visualYOffsetVelocity = 0f;
        _currentClipHeightYOffset = 0f;
        _targetClipHeightYOffset = 0f;
        _clipHeightYOffsetVelocity = 0f;
        _clipHeightPivotBoostTimer = 0f;
        _clipHeightLandingBoostTimer = 0f;
        _previousClipHeightVerticalVelocity = 0f;
        _nextClipHeightLogTime = 0f;
        _currentClipHeightStateName = "None";
        _clipHeightCurrentStateName = "None";
        _clipHeightNextStateName = "None";
        _clipHeightSmoothMode = "Normal";
        _clipHeightGroundedInitialized = false;
        _wasClipHeightGrounded = false;
        _warnedClipHeightFallLand = false;
        _clipHeightIsInTransition = false;
        _usedNextStateForHeight = false;
        _clipHeightCurrentOrNextIsWalk = false;
        _wasClipHeightCurrentOrNextWalk = false;
        _loggedClipHeightCurrentTransition = false;
        _previousPlanarVelocity = Vector3.zero;
        _currentPlanarAcceleration = Vector3.zero;
        _accelForwardWobble = 0f;
        _accelForwardWobbleVelocity = 0f;
        _accelSideWobble = 0f;
        _accelSideWobbleVelocity = 0f;
        _stopOvershoot = 0f;
        _stopOvershootVelocity = 0f;
        _stopOvershootTimer = 0f;
        _stopOvershootCooldownTimer = 0f;
        _lastPlanarSpeed = 0f;
        _previousYawForTurnWobble = 0f;
        _turnWobble = 0f;
        _turnWobbleVelocity = 0f;
        _stopOvershootDirection = 1f;
        _impactForwardWobble = 0f;
        _impactForwardWobbleVelocity = 0f;
        _impactSideWobble = 0f;
        _impactSideWobbleVelocity = 0f;
        _targetImpactForwardWobble = 0f;
        _targetImpactSideWobble = 0f;
        _jumpStretch = 0f;
        _jumpStretchVelocity = 0f;
        _jumpPitch = 0f;
        _jumpPitchVelocity = 0f;
        _landingSquash = 0f;
        _landingSquashVelocity = 0f;
        _landingPitch = 0f;
        _landingPitchVelocity = 0f;
        _jumpVisualTimer = 0f;
        _landingVisualTimer = 0f;
        _jumpVisualIntensity = 0f;
        _landingVisualIntensity = 0f;

        if (targetBody == null)
            return;

        _previousPlanarVelocity = targetBody.linearVelocity;
        _previousPlanarVelocity.y = 0f;
        _lastPlanarSpeed = _previousPlanarVelocity.magnitude;
        _previousClipHeightVerticalVelocity = targetBody.linearVelocity.y;
        if (motorStateSource != null)
        {
            _wasClipHeightGrounded = motorStateSource.IsGrounded;
            _clipHeightGroundedInitialized = true;
        }

        Vector3 initialDirection = Vector3.ProjectOnPlane(targetBody.transform.forward, Vector3.up);
        _lastPlanarMoveDirection = initialDirection.sqrMagnitude > 0.0001f
            ? initialDirection.normalized
            : Vector3.forward;
    }

    private void UpdateVisualPosition(Vector3 localVelocity, float planarSpeed)
    {
        bool stabilizeAirborne = ShouldStabilizeMainScenesAirborneVisuals();
        Vector3 targetLagOffset = Vector3.zero;
        if (enableLocalLag && !stabilizeAirborne)
        {
            float speedScale = Mathf.Max(0.01f, speedForMaxLean);
            float backLag = -Mathf.Clamp(localVelocity.z / speedScale, -1f, 1f) * maxBackLag;
            float sideLag = -Mathf.Clamp(localVelocity.x / speedScale, -1f, 1f) * maxSideLag;
            targetLagOffset = new Vector3(sideLag, 0f, backLag);
        }
        else if (stabilizeAirborne)
        {
            _currentLagOffset = Vector3.zero;
            _lagSmoothVelocity = Vector3.zero;
            _currentVisualYOffset = visualLocalOffset.y;
            _visualYOffsetVelocity = 0f;
            _currentClipHeightYOffset = 0f;
            _targetClipHeightYOffset = 0f;
        }

        float deltaTime = Time.deltaTime;
        _currentLagOffset = Vector3.SmoothDamp(
            _currentLagOffset,
            targetLagOffset,
            ref _lagSmoothVelocity,
            lagSmoothTime,
            Mathf.Infinity,
            deltaTime);

        Vector3 targetOffset = visualLocalOffset;
        targetOffset.y = stabilizeAirborne ? visualLocalOffset.y : ResolveVisualYOffset(planarSpeed);
        if (!stabilizeAirborne)
            targetOffset.y += _currentClipHeightYOffset;
        targetOffset += _currentLagOffset;
        if (visualRootIsChildOfTarget)
        {
            Vector3 targetLocalPosition = _initialLocalPosition + targetOffset;
            visualRoot.localPosition = Vector3.SmoothDamp(
                visualRoot.localPosition,
                targetLocalPosition,
                ref _positionSmoothVelocity,
                lagSmoothTime,
                Mathf.Infinity,
                deltaTime);
            return;
        }

        Vector3 targetWorldPosition = targetBody.position + targetBody.transform.TransformVector(targetOffset);
        visualRoot.position = Vector3.SmoothDamp(
            visualRoot.position,
            targetWorldPosition,
            ref _positionSmoothVelocity,
            lagSmoothTime,
            Mathf.Infinity,
            deltaTime);
    }

    private float ResolveVisualYOffset(float planarSpeed)
    {
        if (!enableSpeedBasedVisualHeight)
            return visualLocalOffset.y;

        float move01 = Mathf.Clamp01(planarSpeed / Mathf.Max(0.01f, speedForMovingVisualYOffset));
        float targetY = Mathf.Lerp(idleVisualYOffset, movingVisualYOffset, move01);
        _currentVisualYOffset = Mathf.SmoothDamp(
            _currentVisualYOffset,
            targetY,
            ref _visualYOffsetVelocity,
            visualHeightSmoothTime,
            Mathf.Infinity,
            Time.deltaTime);

        return _currentVisualYOffset;
    }

    private void UpdateClipStateHeightOffset(Vector3 planarVelocity, float planarSpeed, float verticalVelocity, float deltaTime)
    {
        if (!enableClipStateHeightOffsets)
        {
            _currentClipHeightStateName = "Disabled";
            _clipHeightCurrentStateName = "Disabled";
            _clipHeightNextStateName = "None";
            _clipHeightIsInTransition = false;
            _usedNextStateForHeight = false;
            _clipHeightCurrentOrNextIsWalk = false;
            _loggedClipHeightCurrentTransition = false;
            _targetClipHeightYOffset = 0f;
            _clipHeightPivotBoostTimer = 0f;
            _clipHeightLandingBoostTimer = 0f;
            _currentClipHeightYOffset = SmoothClipHeightYOffset(_targetClipHeightYOffset, deltaTime);
            _wasClipHeightCurrentOrNextWalk = false;
            StoreClipHeightMotionState(verticalVelocity);
            return;
        }

        if (ShouldStabilizeMainScenesAirborneVisuals())
        {
            _currentClipHeightStateName = "MainScenesAirborne";
            _clipHeightCurrentStateName = "MainScenesAirborne";
            _clipHeightNextStateName = "None";
            _clipHeightIsInTransition = false;
            _usedNextStateForHeight = false;
            _clipHeightCurrentOrNextIsWalk = false;
            _loggedClipHeightCurrentTransition = false;
            _targetClipHeightYOffset = 0f;
            _clipHeightPivotBoostTimer = 0f;
            _clipHeightLandingBoostTimer = 0f;
            _currentClipHeightYOffset = SmoothMainScenesAirborneClipHeightYOffset(deltaTime);
            _wasClipHeightCurrentOrNextWalk = false;
            StoreClipHeightMotionState(verticalVelocity);
            return;
        }

        UpdateClipHeightBoostTriggers(planarVelocity, planarSpeed, verticalVelocity);

        float stateYOffset = ResolveClipStateYOffset(out _currentClipHeightStateName);
        float boostYOffset = ResolveClipHeightBoostYOffset();
        _targetClipHeightYOffset = ClampClipHeightOffset(stateYOffset + boostYOffset);
        _currentClipHeightYOffset = SmoothClipHeightYOffset(_targetClipHeightYOffset, deltaTime);
        if (!_clipHeightIsInTransition || !_usedNextStateForHeight)
            _loggedClipHeightCurrentTransition = false;

        LogClipHeightState(planarSpeed, verticalVelocity, stateYOffset, boostYOffset);
        _wasClipHeightCurrentOrNextWalk = _clipHeightCurrentOrNextIsWalk;
        DecayClipHeightBoostTimers(deltaTime);
        StoreClipHeightMotionState(verticalVelocity);
    }

    private float ResolveClipStateYOffset(out string stateName)
    {
        stateName = "None";
        _clipHeightCurrentStateName = "None";
        _clipHeightNextStateName = "None";
        _clipHeightIsInTransition = false;
        _usedNextStateForHeight = false;
        _clipHeightCurrentOrNextIsWalk = false;

        Animator animator = ResolveStateHeightAnimator();
        if (animator == null || !animator.isActiveAndEnabled || animator.runtimeAnimatorController == null)
            return 0f;

        AnimatorStateInfo stateInfo = animator.GetCurrentAnimatorStateInfo(BaseLayerIndex);
        bool hasCurrentOffset = TryResolveClipStateYOffset(stateInfo, true, out string currentStateName, out float currentYOffset);
        bool currentIsWalk = MatchesAnimatorState(stateInfo, walkStateName);
        _clipHeightCurrentStateName = currentStateName;
        _clipHeightCurrentOrNextIsWalk = currentIsWalk;

        if (useNextAnimatorStateForClipHeight && animator.IsInTransition(BaseLayerIndex))
        {
            _clipHeightIsInTransition = true;
            AnimatorStateInfo nextStateInfo = animator.GetNextAnimatorStateInfo(BaseLayerIndex);
            bool hasNextOffset = TryResolveClipStateYOffset(nextStateInfo, false, out string nextStateName, out float nextYOffset);
            bool nextIsWalk = MatchesAnimatorState(nextStateInfo, walkStateName);
            bool nextIsRun = MatchesAnimatorState(nextStateInfo, runStateName);
            bool nextIsJump = MatchesAnimatorState(nextStateInfo, jumpStateName);
            _clipHeightNextStateName = nextStateName;
            _clipHeightCurrentOrNextIsWalk = currentIsWalk || nextIsWalk;

            if (nextIsWalk && prioritizeWalkNextStateHeight)
            {
                _usedNextStateForHeight = true;
                stateName = walkStateName;
                return ClampClipHeightOffset(walkStateYOffset);
            }

            if (hasNextOffset && (nextIsRun || nextIsJump))
            {
                _usedNextStateForHeight = true;
                stateName = nextStateName;
                return nextYOffset;
            }
        }

        stateName = currentStateName;
        return hasCurrentOffset ? currentYOffset : 0f;
    }

    private bool TryResolveClipStateYOffset(
        AnimatorStateInfo stateInfo,
        bool allowFallLandFallback,
        out string stateName,
        out float yOffset)
    {
        yOffset = 0f;

        if (MatchesAnimatorState(stateInfo, idleStateName))
        {
            stateName = idleStateName;
            yOffset = ClampClipHeightOffset(idleStateYOffset);
            return true;
        }

        if (MatchesAnimatorState(stateInfo, walkStateName))
        {
            stateName = walkStateName;
            yOffset = ClampClipHeightOffset(walkStateYOffset);
            return true;
        }

        if (MatchesAnimatorState(stateInfo, runStateName))
        {
            stateName = runStateName;
            yOffset = ClampClipHeightOffset(runStateYOffset);
            return true;
        }

        if (MatchesAnimatorState(stateInfo, jumpStateName))
        {
            stateName = jumpStateName;
            yOffset = ClampClipHeightOffset(jumpStateYOffset);
            return true;
        }

        if (MatchesAnimatorState(stateInfo, "Fall") || MatchesAnimatorState(stateInfo, "Land") || MatchesAnimatorState(stateInfo, "Landing"))
        {
            stateName = "Fall/Land";
            WarnClipHeightFallLandState(allowFallLandFallback);
            if (!allowFallLandFallback)
                return false;

            yOffset = ClampClipHeightOffset(jumpStateYOffset);
            return true;
        }

        stateName = $"hash:{stateInfo.shortNameHash}";
        return false;
    }

    private void WarnClipHeightFallLandState(bool usingJumpFallback)
    {
        if (!debugClipHeightLogs || _warnedClipHeightFallLand)
            return;

        string fallback = usingJumpFallback
            ? "Clip height offset is using JumpUp fallback."
            : "Keeping current clip height fallback.";
        Debug.LogWarning(
            $"[HamsterVisualFollower] Fall/Land animator state detected. {fallback} Remove Fall/Land transitions for this test step.",
            this);
        _warnedClipHeightFallLand = true;
    }

    private Animator ResolveStateHeightAnimator()
    {
        if (stateHeightAnimator == null && autoUseVisualAnimatorForHeight)
            stateHeightAnimator = visualAnimator;

        return stateHeightAnimator;
    }

    private static bool MatchesAnimatorState(AnimatorStateInfo stateInfo, string stateName)
    {
        if (string.IsNullOrEmpty(stateName))
            return false;

        return stateInfo.shortNameHash == Animator.StringToHash(stateName)
            || stateInfo.IsName(stateName)
            || stateInfo.IsName($"Base Layer.{stateName}");
    }

    private void UpdateClipHeightBoostTriggers(Vector3 planarVelocity, float planarSpeed, float verticalVelocity)
    {
        if (IsMainScenesTarget())
        {
            _clipHeightPivotBoostTimer = 0f;
            _clipHeightLandingBoostTimer = 0f;
            return;
        }

        if (_previousPlanarVelocity.sqrMagnitude > 0.0001f && planarVelocity.sqrMagnitude > 0.0001f)
        {
            float previousSpeed = _previousPlanarVelocity.magnitude;
            float directionDot = Vector3.Dot(_previousPlanarVelocity / previousSpeed, planarVelocity / Mathf.Max(0.0001f, planarSpeed));
            if (previousSpeed >= 0.5f && planarSpeed >= 0.5f && directionDot <= -0.25f)
                _clipHeightPivotBoostTimer = Mathf.Max(_clipHeightPivotBoostTimer, ClipHeightBoostDuration);
        }

        bool hasGroundedState = motorStateSource != null;
        bool isGrounded = hasGroundedState && motorStateSource.IsGrounded;
        bool landedByGroundedState = hasGroundedState
            && _clipHeightGroundedInitialized
            && !_wasClipHeightGrounded
            && isGrounded
            && _previousClipHeightVerticalVelocity < -0.5f;
        bool landedByVelocityRecovery = !hasGroundedState
            && _previousClipHeightVerticalVelocity < -0.5f
            && Mathf.Abs(verticalVelocity) <= 0.08f;

        if (landedByGroundedState || landedByVelocityRecovery)
            _clipHeightLandingBoostTimer = Mathf.Max(_clipHeightLandingBoostTimer, ClipHeightBoostDuration);
    }

    private float ResolveClipHeightBoostYOffset()
    {
        float pivot01 = ClipHeightBoostDuration > 0f
            ? Mathf.Clamp01(_clipHeightPivotBoostTimer / ClipHeightBoostDuration)
            : 0f;
        float landing01 = ClipHeightBoostDuration > 0f
            ? Mathf.Clamp01(_clipHeightLandingBoostTimer / ClipHeightBoostDuration)
            : 0f;

        return sprintPivotYOffset * pivot01 + landingYOffset * landing01;
    }

    private void DecayClipHeightBoostTimers(float deltaTime)
    {
        if (_clipHeightPivotBoostTimer > 0f)
            _clipHeightPivotBoostTimer = Mathf.Max(0f, _clipHeightPivotBoostTimer - deltaTime);

        if (_clipHeightLandingBoostTimer > 0f)
            _clipHeightLandingBoostTimer = Mathf.Max(0f, _clipHeightLandingBoostTimer - deltaTime);
    }

    private float SmoothClipHeightYOffset(float targetYOffset, float deltaTime)
    {
        float delta = targetYOffset - _currentClipHeightYOffset;
        bool isMovingDown = delta < 0f;
        bool isWalkEnter = !_wasClipHeightCurrentOrNextWalk && _clipHeightCurrentOrNextIsWalk;
        if (snapDownOnWalkEnter && isWalkEnter && isMovingDown && Mathf.Abs(delta) <= walkEnterSnapMaxDelta)
        {
            _clipHeightYOffsetVelocity = 0f;
            _clipHeightSmoothMode = "SnapWalk";
            return targetYOffset;
        }

        float smoothTime = isMovingDown ? clipHeightDownSmoothTime : clipHeightUpSmoothTime;
        _clipHeightSmoothMode = isMovingDown ? "DownFast" : "UpNormal";
        if (Mathf.Approximately(delta, 0f))
            _clipHeightSmoothMode = "Normal";

        return Mathf.SmoothDamp(
            _currentClipHeightYOffset,
            targetYOffset,
            ref _clipHeightYOffsetVelocity,
            Mathf.Max(0f, smoothTime),
            Mathf.Infinity,
            deltaTime);
    }

    private float SmoothMainScenesAirborneClipHeightYOffset(float deltaTime)
    {
        _clipHeightSmoothMode = "MainScenesAirborne";
        return Mathf.SmoothDamp(
            _currentClipHeightYOffset,
            0f,
            ref _clipHeightYOffsetVelocity,
            Mathf.Max(clipHeightSmoothTime, 0.06f),
            Mathf.Infinity,
            deltaTime);
    }

    private void StoreClipHeightMotionState(float verticalVelocity)
    {
        _previousClipHeightVerticalVelocity = verticalVelocity;

        if (motorStateSource == null)
        {
            _clipHeightGroundedInitialized = false;
            _wasClipHeightGrounded = false;
            return;
        }

        _wasClipHeightGrounded = motorStateSource.IsGrounded;
        _clipHeightGroundedInitialized = true;
    }

    private float ClampClipHeightOffset(float value)
    {
        float maxOffset = Mathf.Max(0f, maxClipHeightYOffset);
        return Mathf.Clamp(value, -maxOffset, maxOffset);
    }

    private void LogClipHeightState(float planarSpeed, float verticalVelocity, float stateYOffset, float boostYOffset)
    {
        bool forceTransitionLog = _usedNextStateForHeight && !_loggedClipHeightCurrentTransition;
        if (!debugClipHeightLogs || (!forceTransitionLog && Time.time < _nextClipHeightLogTime))
            return;

        _nextClipHeightLogTime = Time.time + ClipHeightLogInterval;
        if (_usedNextStateForHeight)
            _loggedClipHeightCurrentTransition = true;

        float baseVisualY = enableSpeedBasedVisualHeight ? _currentVisualYOffset : visualLocalOffset.y;
        float targetVisualY = baseVisualY + _targetClipHeightYOffset;
        float currentVisualY = baseVisualY + _currentClipHeightYOffset;
        float localY = visualRoot != null ? visualRoot.localPosition.y : 0f;
        Debug.Log(
            $"[HamsterVisualFollower] clipHeight current={_clipHeightCurrentStateName} next={_clipHeightNextStateName} transition={_clipHeightIsInTransition} usedNext={_usedNextStateForHeight} smooth={_clipHeightSmoothMode} state={_currentClipHeightStateName} baseY={baseVisualY:F2} stateOffset={stateYOffset:F2} boost={boostYOffset:F2} target={_targetClipHeightYOffset:F3} currentOffset={_currentClipHeightYOffset:F3} targetY={targetVisualY:F3} currentY={currentVisualY:F3} localY={localY:F3} speed={planarSpeed:F2} vertical={verticalVelocity:F2}",
            this);
    }

    public void AddJumpVisualReaction(float intensity = 1f)
    {
        if (!enableJumpVisualReaction || ShouldSuppressMainScenesJumpVisualReaction())
            return;

        float normalizedIntensity = Mathf.Clamp01(intensity);
        if (normalizedIntensity <= 0f)
            return;

        _jumpVisualTimer = Mathf.Max(_jumpVisualTimer, jumpVisualDuration);
        _jumpVisualIntensity = Mathf.Max(_jumpVisualIntensity, normalizedIntensity);
    }

    public void AddLandingVisualReaction(float intensity = 1f)
    {
        if (!enableJumpVisualReaction || ShouldSuppressMainScenesJumpVisualReaction())
            return;

        float normalizedIntensity = Mathf.Clamp01(intensity);
        if (normalizedIntensity <= 0f)
            return;

        _landingVisualTimer = Mathf.Max(_landingVisualTimer, landingVisualDuration);
        _landingVisualIntensity = Mathf.Max(_landingVisualIntensity, normalizedIntensity);
    }

    private void UpdateJumpVisualReaction(float deltaTime)
    {
        if (!enableJumpVisualReaction || ShouldSuppressMainScenesJumpVisualReaction())
        {
            ResetJumpVisualReactionState();
            return;
        }

        if (_jumpVisualTimer > 0f)
            _jumpVisualTimer = Mathf.Max(0f, _jumpVisualTimer - deltaTime);

        if (_landingVisualTimer > 0f)
            _landingVisualTimer = Mathf.Max(0f, _landingVisualTimer - deltaTime);

        if (_jumpVisualTimer <= 0f)
            _jumpVisualIntensity = 0f;

        if (_landingVisualTimer <= 0f)
            _landingVisualIntensity = 0f;

        float jumpTargetStretch = _jumpVisualTimer > 0f ? jumpStretchAmount * _jumpVisualIntensity : 0f;
        float jumpTargetPitch = _jumpVisualTimer > 0f ? jumpPitchDegrees * _jumpVisualIntensity : 0f;
        float landingTargetSquash = _landingVisualTimer > 0f ? landingSquashAmount * _landingVisualIntensity : 0f;
        float landingTargetPitch = _landingVisualTimer > 0f ? landingPitchDegrees * _landingVisualIntensity : 0f;

        if (invertJumpPitch)
            jumpTargetPitch = -jumpTargetPitch;

        if (invertLandingPitch)
            landingTargetPitch = -landingTargetPitch;

        float smoothTime = Mathf.Max(0f, jumpVisualReturnSmoothTime);
        _jumpStretch = Mathf.SmoothDamp(_jumpStretch, jumpTargetStretch, ref _jumpStretchVelocity, smoothTime, Mathf.Infinity, deltaTime);
        _jumpPitch = Mathf.SmoothDamp(_jumpPitch, jumpTargetPitch, ref _jumpPitchVelocity, smoothTime, Mathf.Infinity, deltaTime);
        _landingSquash = Mathf.SmoothDamp(_landingSquash, landingTargetSquash, ref _landingSquashVelocity, smoothTime, Mathf.Infinity, deltaTime);
        _landingPitch = Mathf.SmoothDamp(_landingPitch, landingTargetPitch, ref _landingPitchVelocity, smoothTime, Mathf.Infinity, deltaTime);
    }

    private void UpdateJumpVisualScale()
    {
        if (visualRoot == null)
            return;

        if (!_hasInitialVisualLocalScale)
        {
            _initialVisualLocalScale = visualRoot.localScale;
            _hasInitialVisualLocalScale = true;
        }

        if (ShouldSuppressMainScenesJumpVisualReaction())
        {
            visualRoot.localScale = _initialVisualLocalScale;
            return;
        }

        float xzMultiplier = Mathf.Clamp(1f - _jumpStretch * 0.5f + _landingSquash * 0.5f, 0.85f, 1.15f);
        float yMultiplier = Mathf.Clamp(1f + _jumpStretch - _landingSquash, 0.85f, 1.18f);
        visualRoot.localScale = new Vector3(
            _initialVisualLocalScale.x * xzMultiplier,
            _initialVisualLocalScale.y * yMultiplier,
            _initialVisualLocalScale.z * xzMultiplier);
    }

    private void UpdateMotionWobble(Vector3 planarVelocity, float planarSpeed, float deltaTime)
    {
        if (ShouldStabilizeMainScenesAirborneVisuals())
        {
            _currentPlanarAcceleration = Vector3.zero;
            ResetMainScenesAirborneWobbleState();
            return;
        }

        if (deltaTime > 0.0001f)
            _currentPlanarAcceleration = (planarVelocity - _previousPlanarVelocity) / deltaTime;
        else
            _currentPlanarAcceleration = Vector3.zero;

        UpdateAccelerationWobble(deltaTime);
        UpdateStopOvershoot(planarSpeed, deltaTime);
        UpdateImpactVisualReaction(deltaTime);
    }

    public void AddImpactVisualReaction(Vector3 worldDirection, float intensity)
    {
        if (!enableImpactVisualReaction || ShouldStabilizeMainScenesAirborneVisuals() || !IsFiniteVector(worldDirection))
            return;

        if (worldDirection.sqrMagnitude <= 0.0001f)
            return;

        float normalizedIntensity = intensity > 1f
            ? Mathf.Clamp01(intensity / Mathf.Max(0.01f, impactForMaxWobble))
            : Mathf.Clamp01(intensity);
        if (normalizedIntensity <= 0f)
            return;

        Vector3 localDirection = ResolveLocalImpactDirection(worldDirection.normalized);
        if (localDirection.sqrMagnitude <= 0.0001f)
            return;

        float targetForwardWobble = -localDirection.z * normalizedIntensity * maxImpactForwardWobbleDegrees;
        float targetSideWobble = -localDirection.x * normalizedIntensity * maxImpactSideWobbleDegrees;

        if (invertImpactForwardWobble)
            targetForwardWobble = -targetForwardWobble;

        if (invertImpactSideWobble)
            targetSideWobble = -targetSideWobble;

        _targetImpactForwardWobble = Mathf.Clamp(
            _targetImpactForwardWobble + targetForwardWobble,
            -maxImpactForwardWobbleDegrees,
            maxImpactForwardWobbleDegrees);
        _targetImpactSideWobble = Mathf.Clamp(
            _targetImpactSideWobble + targetSideWobble,
            -maxImpactSideWobbleDegrees,
            maxImpactSideWobbleDegrees);
    }

    private Vector3 ResolveLocalImpactDirection(Vector3 worldDirection)
    {
        Vector3 localDirection;
        if (targetBody != null)
            localDirection = targetBody.transform.InverseTransformDirection(worldDirection);
        else if (visualRoot != null)
            localDirection = visualRoot.InverseTransformDirection(worldDirection);
        else
            localDirection = worldDirection;

        localDirection.y = 0f;
        return localDirection.sqrMagnitude > 0.0001f
            ? localDirection.normalized
            : Vector3.zero;
    }

    private void UpdateImpactVisualReaction(float deltaTime)
    {
        if (!enableImpactVisualReaction)
        {
            _targetImpactForwardWobble = 0f;
            _targetImpactSideWobble = 0f;
        }

        _impactForwardWobble = Mathf.SmoothDamp(
            _impactForwardWobble,
            _targetImpactForwardWobble,
            ref _impactForwardWobbleVelocity,
            Mathf.Abs(_targetImpactForwardWobble) > 0.001f ? impactWobbleSmoothTime : impactWobbleReturnSmoothTime,
            Mathf.Infinity,
            deltaTime);
        _impactSideWobble = Mathf.SmoothDamp(
            _impactSideWobble,
            _targetImpactSideWobble,
            ref _impactSideWobbleVelocity,
            Mathf.Abs(_targetImpactSideWobble) > 0.001f ? impactWobbleSmoothTime : impactWobbleReturnSmoothTime,
            Mathf.Infinity,
            deltaTime);

        if (impactWobbleReturnSmoothTime <= 0f)
        {
            _targetImpactForwardWobble = 0f;
            _targetImpactSideWobble = 0f;
            return;
        }

        float forwardReturnSpeed = maxImpactForwardWobbleDegrees / impactWobbleReturnSmoothTime;
        float sideReturnSpeed = maxImpactSideWobbleDegrees / impactWobbleReturnSmoothTime;
        _targetImpactForwardWobble = Mathf.MoveTowards(_targetImpactForwardWobble, 0f, forwardReturnSpeed * deltaTime);
        _targetImpactSideWobble = Mathf.MoveTowards(_targetImpactSideWobble, 0f, sideReturnSpeed * deltaTime);
    }

    private void UpdateAccelerationWobble(float deltaTime)
    {
        Vector3 localAcceleration = targetBody.transform.InverseTransformDirection(_currentPlanarAcceleration);

        float targetForwardWobble = 0f;
        float targetSideWobble = 0f;
        if (enableAccelerationWobble)
        {
            float accelerationScale = Mathf.Max(0.01f, accelerationForMaxWobble);
            float forwardAmount = Mathf.Clamp(localAcceleration.z / accelerationScale, -1f, 1f);
            float sideAmount = Mathf.Clamp(localAcceleration.x / accelerationScale, -1f, 1f);

            targetForwardWobble = forwardAmount * maxAccelerationForwardWobbleDegrees;
            targetSideWobble = -sideAmount * maxAccelerationSideWobbleDegrees;

            if (invertAccelerationForwardWobble)
                targetForwardWobble = -targetForwardWobble;

            if (invertAccelerationSideWobble)
                targetSideWobble = -targetSideWobble;
        }

        float forwardSmoothTime = Mathf.Abs(targetForwardWobble) > 0.001f
            ? accelerationWobbleSmoothTime
            : accelerationWobbleReturnSmoothTime;
        float sideSmoothTime = Mathf.Abs(targetSideWobble) > 0.001f
            ? accelerationWobbleSmoothTime
            : accelerationWobbleReturnSmoothTime;

        _accelForwardWobble = Mathf.SmoothDamp(
            _accelForwardWobble,
            targetForwardWobble,
            ref _accelForwardWobbleVelocity,
            forwardSmoothTime,
            Mathf.Infinity,
            deltaTime);
        _accelSideWobble = Mathf.SmoothDamp(
            _accelSideWobble,
            targetSideWobble,
            ref _accelSideWobbleVelocity,
            sideSmoothTime,
            Mathf.Infinity,
            deltaTime);
    }

    private void UpdateStopOvershoot(float planarSpeed, float deltaTime)
    {
        if (_stopOvershootCooldownTimer > 0f)
            _stopOvershootCooldownTimer = Mathf.Max(0f, _stopOvershootCooldownTimer - deltaTime);

        if (enableStopOvershoot && _stopOvershootTimer <= 0f && _stopOvershootCooldownTimer <= 0f)
        {
            float speedDrop = _lastPlanarSpeed - planarSpeed;
            bool hadMoveSpeed = _lastPlanarSpeed >= stopOvershootSpeedThreshold;
            bool nearStopSpeed = planarSpeed <= stopOvershootSpeedThreshold;
            bool decelerating = speedDrop > 0.001f;
            bool inferredInputReleased = planarSpeed <= stopOvershootInputThreshold || speedDrop >= stopOvershootInputThreshold;

            if (hadMoveSpeed && nearStopSpeed && decelerating && inferredInputReleased && stopOvershootDuration > 0f)
            {
                Vector3 localPreviousVelocity = targetBody.transform.InverseTransformDirection(_previousPlanarVelocity);
                _stopOvershootDirection = Mathf.Abs(localPreviousVelocity.z) > 0.01f
                    ? Mathf.Sign(localPreviousVelocity.z)
                    : 1f;
                _stopOvershootTimer = stopOvershootDuration;
                _stopOvershootCooldownTimer = stopOvershootDuration + stopOvershootCooldown;
            }
        }

        float targetStopOvershoot = 0f;
        if (enableStopOvershoot && _stopOvershootTimer > 0f)
        {
            float normalizedTime = stopOvershootDuration > 0f
                ? Mathf.Clamp01(_stopOvershootTimer / stopOvershootDuration)
                : 0f;
            targetStopOvershoot = _stopOvershootDirection * maxStopOvershootDegrees * normalizedTime;

            if (invertStopOvershoot)
                targetStopOvershoot = -targetStopOvershoot;

            _stopOvershootTimer = Mathf.Max(0f, _stopOvershootTimer - deltaTime);
        }

        float smoothTime = stopOvershootDuration > 0f ? stopOvershootDuration * 0.25f : 0f;
        _stopOvershoot = Mathf.SmoothDamp(
            _stopOvershoot,
            targetStopOvershoot,
            ref _stopOvershootVelocity,
            smoothTime,
            Mathf.Infinity,
            deltaTime);
    }

    private void UpdateTurnWobble(float deltaTime)
    {
        float targetTurnWobble = 0f;
        if (enableTurnWobble)
        {
            float yawDelta = Mathf.DeltaAngle(_previousYawForTurnWobble, _currentYaw);
            float turnAmount = Mathf.Clamp(yawDelta / Mathf.Max(1f, turnWobbleYawDeltaForMax), -1f, 1f);
            targetTurnWobble = -turnAmount * maxTurnWobbleDegrees;

            if (invertTurnWobble)
                targetTurnWobble = -targetTurnWobble;
        }

        _turnWobble = Mathf.SmoothDamp(
            _turnWobble,
            targetTurnWobble,
            ref _turnWobbleVelocity,
            turnWobbleSmoothTime,
            Mathf.Infinity,
            deltaTime);
    }

    private void StorePreviousMotionState(Vector3 planarVelocity, float planarSpeed)
    {
        _previousPlanarVelocity = planarVelocity;
        _lastPlanarSpeed = planarSpeed;
        _previousYawForTurnWobble = _currentYaw;
    }

    private void UpdateVisualRotation(Vector3 planarVelocity, float planarSpeed, Vector3 localVelocity)
    {
        if (TryApplyRecoveryVisualRotation())
            return;

        bool stabilizeAirborne = ShouldStabilizeMainScenesAirborneVisuals();
        float targetYaw = 0f;
        bool usedMotorDesiredFacing = TryResolveTargetMotorDesiredFacing(out Vector3 motorDesiredFacing, out string visualFacingSource);
        if (usedMotorDesiredFacing)
        {
            _lastPlanarMoveDirection = motorDesiredFacing;
            targetYaw = CalculateTargetYaw(motorDesiredFacing);
            _lastVisualFacingSource = visualFacingSource;
        }
        else if (faceMoveDirection)
        {
            bool hasMoveDirection = planarSpeed >= minSpeedToFaceMove && planarVelocity.sqrMagnitude > 0.0001f;
            if (hasMoveDirection)
                _lastPlanarMoveDirection = planarVelocity.normalized;

            if (hasMoveDirection || (keepLastMoveYawWhenIdle && _lastPlanarMoveDirection.sqrMagnitude > 0.0001f))
                targetYaw = CalculateTargetYaw(_lastPlanarMoveDirection);

            _lastVisualFacingSource = hasMoveDirection ? "Velocity" : "LastMoveYaw";
        }
        else
        {
            _lastVisualFacingSource = "Disabled";
        }

        _currentYaw = Mathf.SmoothDampAngle(
            _currentYaw,
            targetYaw,
            ref _yawVelocity,
            yawSmoothTime,
            Mathf.Infinity,
            Time.deltaTime);
        if (stabilizeAirborne)
        {
            _turnWobble = 0f;
            _turnWobbleVelocity = 0f;
        }
        else
        {
            UpdateTurnWobble(Time.deltaTime);
        }

        float targetForwardLean = 0f;
        float targetSideLean = 0f;
        if (enableBodyLean && !stabilizeAirborne)
        {
            float speedScale = Mathf.Max(0.01f, speedForMaxLean);
            targetForwardLean = Mathf.Clamp(localVelocity.z / speedScale, -1f, 1f) * maxForwardLeanDegrees;
            targetSideLean = Mathf.Clamp(localVelocity.x / speedScale, -1f, 1f) * maxSideLeanDegrees;

            if (invertForwardLean)
                targetForwardLean = -targetForwardLean;

            if (invertSideLean)
                targetSideLean = -targetSideLean;
        }

        _currentForwardLean = Mathf.SmoothDamp(
            _currentForwardLean,
            targetForwardLean,
            ref _forwardLeanVelocity,
            leanSmoothTime,
            Mathf.Infinity,
            Time.deltaTime);
        _currentSideLean = Mathf.SmoothDamp(
            _currentSideLean,
            targetSideLean,
            ref _sideLeanVelocity,
            leanSmoothTime,
            Mathf.Infinity,
            Time.deltaTime);

        Quaternion yawRot = Quaternion.Euler(0f, _currentYaw + visualYawOffsetDegrees, 0f);
        Quaternion leanRot = stabilizeAirborne ? Quaternion.identity : Quaternion.Euler(_currentForwardLean, 0f, _currentSideLean);
        Quaternion accelerationWobbleRot = stabilizeAirborne ? Quaternion.identity : Quaternion.Euler(_accelForwardWobble, 0f, _accelSideWobble);
        Quaternion stopOvershootRot = stabilizeAirborne ? Quaternion.identity : Quaternion.Euler(_stopOvershoot, 0f, 0f);
        Quaternion turnWobbleRot = stabilizeAirborne ? Quaternion.identity : Quaternion.Euler(0f, 0f, _turnWobble);
        Quaternion impactWobbleRot = stabilizeAirborne ? Quaternion.identity : Quaternion.Euler(_impactForwardWobble, 0f, _impactSideWobble);
        Quaternion jumpVisualRot = stabilizeAirborne || ShouldSuppressMainScenesJumpVisualReaction()
            ? Quaternion.identity
            : Quaternion.Euler(_jumpPitch + _landingPitch, 0f, 0f);
        Quaternion offsetRot = stabilizeAirborne
            ? Quaternion.Euler(0f, visualLocalEulerOffset.y, 0f)
            : Quaternion.Euler(visualLocalEulerOffset);

        if (visualRootIsChildOfTarget)
            visualRoot.localRotation = _initialLocalRotation * yawRot * leanRot * accelerationWobbleRot * stopOvershootRot * turnWobbleRot * impactWobbleRot * jumpVisualRot * offsetRot;
        else
            visualRoot.rotation = yawRot * leanRot * accelerationWobbleRot * stopOvershootRot * turnWobbleRot * impactWobbleRot * jumpVisualRot * offsetRot;

        LogTargetVisualFacing(usedMotorDesiredFacing, motorDesiredFacing, targetYaw, planarSpeed);
    }

    private bool TryResolveTargetMotorDesiredFacing(out Vector3 desiredFacing, out string visualFacingSource)
    {
        desiredFacing = Vector3.zero;
        visualFacingSource = "None";

        if (motorStateSource == null ||
            !motorStateSource.IsMainScenesInputRouteTarget ||
            !IsSafeVisualFacingRoot())
        {
            return false;
        }

        if (TryNormalizePlanarDirection(motorStateSource.DesiredFacingDirection, out desiredFacing))
        {
            visualFacingSource = "Motor:" + motorStateSource.LastAppliedYawSource;
            return true;
        }

        if (TryNormalizePlanarDirection(motorStateSource.CameraPlanarForward, out desiredFacing))
        {
            visualFacingSource = "Motor:CameraPlanarForward";
            return true;
        }

        if (TryNormalizePlanarDirection(motorStateSource.SmoothedMoveWorldDirection, out desiredFacing))
        {
            visualFacingSource = "Motor:MoveWorldDirection";
            return true;
        }

        return false;
    }

    private bool TryApplyRecoveryVisualRotation()
    {
        if (!followBodyRotationDuringRecovery ||
            recoveryStateSource == null ||
            !recoveryStateSource.ShouldVisualFollowBodyRotation ||
            targetBody == null ||
            visualRoot == null ||
            !IsSafeVisualFacingRoot())
        {
            return false;
        }

        if (!_hasInitialBodyToVisualRotation)
            CacheInitialBodyToVisualRotation();

        ResetJumpVisualReactionState();
        ResetMainScenesAirborneWobbleState();
        _lastVisualFacingSource = "RecoveryBodyRotation";

        Quaternion targetWorldRotation = targetBody.rotation * _initialBodyToVisualRotation * Quaternion.Euler(visualLocalEulerOffset);
        float smoothTime = Mathf.Max(0f, recoveryVisualRotationSmoothTime);
        float rotationT = smoothTime <= 0f
            ? 1f
            : 1f - Mathf.Exp(-Time.deltaTime / smoothTime);

        if (visualRootIsChildOfTarget)
        {
            Transform parent = visualRoot.parent;
            Quaternion targetLocalRotation = parent != null
                ? Quaternion.Inverse(parent.rotation) * targetWorldRotation
                : targetWorldRotation;
            visualRoot.localRotation = Quaternion.Slerp(visualRoot.localRotation, targetLocalRotation, rotationT);
        }
        else
        {
            visualRoot.rotation = Quaternion.Slerp(visualRoot.rotation, targetWorldRotation, rotationT);
        }

        return true;
    }

    private void CacheInitialBodyToVisualRotation()
    {
        if (targetBody == null || visualRoot == null)
            return;

        _initialBodyToVisualRotation = Quaternion.Inverse(targetBody.rotation) * visualRoot.rotation;
        _hasInitialBodyToVisualRotation = true;
    }

    private bool IsMainScenesTarget()
    {
        return motorStateSource != null && motorStateSource.IsMainScenesInputRouteTarget;
    }

    private bool ShouldSuppressMainScenesJumpVisualReaction()
    {
        return IsMainScenesTarget();
    }

    private bool ShouldStabilizeMainScenesAirborneVisuals()
    {
        return IsMainScenesTarget() && !motorStateSource.IsGrounded;
    }

    private void ResetJumpVisualReactionState()
    {
        _jumpVisualTimer = 0f;
        _landingVisualTimer = 0f;
        _jumpVisualIntensity = 0f;
        _landingVisualIntensity = 0f;
        _jumpStretch = 0f;
        _jumpStretchVelocity = 0f;
        _jumpPitch = 0f;
        _jumpPitchVelocity = 0f;
        _landingSquash = 0f;
        _landingSquashVelocity = 0f;
        _landingPitch = 0f;
        _landingPitchVelocity = 0f;
    }

    private void ResetMainScenesAirborneVisualState()
    {
        ResetJumpVisualReactionState();
        ResetMainScenesAirborneWobbleState();
    }

    private void ResetMainScenesAirborneWobbleState()
    {
        _currentForwardLean = 0f;
        _currentSideLean = 0f;
        _forwardLeanVelocity = 0f;
        _sideLeanVelocity = 0f;
        _accelForwardWobble = 0f;
        _accelForwardWobbleVelocity = 0f;
        _accelSideWobble = 0f;
        _accelSideWobbleVelocity = 0f;
        _stopOvershoot = 0f;
        _stopOvershootVelocity = 0f;
        _stopOvershootTimer = 0f;
        _stopOvershootCooldownTimer = 0f;
        _turnWobble = 0f;
        _turnWobbleVelocity = 0f;
        _targetImpactForwardWobble = 0f;
        _targetImpactSideWobble = 0f;
        _impactForwardWobble = 0f;
        _impactForwardWobbleVelocity = 0f;
        _impactSideWobble = 0f;
        _impactSideWobbleVelocity = 0f;
    }

    private bool IsSafeVisualFacingRoot()
    {
        if (visualRoot == null)
            return false;

        if (targetBody != null && visualRoot == targetBody.transform)
            return false;

        return visualRoot.GetComponent<Rigidbody>() == null &&
               visualRoot.GetComponent("NetworkObject") == null &&
               visualRoot.GetComponent("PlayerHub") == null;
    }

    private void LogTargetVisualFacing(bool usedMotorDesiredFacing, Vector3 desiredFacing, float targetYaw, float planarSpeed)
    {
        if (!debugLogs ||
            motorStateSource == null ||
            !motorStateSource.IsMainScenesInputRouteTarget ||
            Time.unscaledTime < _nextTargetVisualFacingLogTime)
        {
            return;
        }

        _nextTargetVisualFacingLogTime = Time.unscaledTime + TargetVisualFacingLogInterval;
        string visualForward = visualRoot != null ? FormatVector3(visualRoot.forward) : "<null>";
        string visualUpDot = visualRoot != null ? FormatFloat(Vector3.Dot(visualRoot.up, Vector3.up)) : "<null>";
        Debug.Log(
            $"[MSVisualFacing:{GetInputRouteObjectName()}] cameraBasisSource={motorStateSource.LastCameraBasisSource} cameraPlanarForward={FormatVector3(motorStateSource.CameraPlanarForward)} desiredFacingDirection={FormatVector3(desiredFacing)} moveWorldDirection={FormatVector3(motorStateSource.SmoothedMoveWorldDirection)} targetYaw={targetYaw:F1} currentVisualYaw={_currentYaw:F1} visualForward={visualForward} visualUpDot={visualUpDot} appliedYawSource={_lastVisualFacingSource} hasMoveInput={motorStateSource.HasMoveInput} planarSpeed={planarSpeed:F2} grounded={motorStateSource.IsGrounded} usedMotorDesiredFacing={usedMotorDesiredFacing} safeVisualRoot={IsSafeVisualFacingRoot()}",
            this);
    }

    private float CalculateTargetYaw(Vector3 moveDirection)
    {
        if (moveDirection.sqrMagnitude <= 0.0001f)
            return 0f;

        if (visualRootIsChildOfTarget && targetBody != null)
        {
            Vector3 localDirection = targetBody.transform.InverseTransformDirection(moveDirection.normalized);
            localDirection.y = 0f;
            if (localDirection.sqrMagnitude <= 0.0001f)
                return 0f;

            return Mathf.Atan2(localDirection.x, localDirection.z) * Mathf.Rad2Deg;
        }

        Vector3 flatDirection = moveDirection;
        flatDirection.y = 0f;
        return flatDirection.sqrMagnitude > 0.0001f
            ? Quaternion.LookRotation(flatDirection.normalized, Vector3.up).eulerAngles.y
            : 0f;
    }

    private static bool TryNormalizePlanarDirection(Vector3 direction, out Vector3 planarDirection)
    {
        planarDirection = Vector3.ProjectOnPlane(direction, Vector3.up);
        if (!IsFiniteVector(planarDirection) || planarDirection.sqrMagnitude <= 0.0001f)
        {
            planarDirection = Vector3.zero;
            return false;
        }

        planarDirection.Normalize();
        return true;
    }

    private string GetInputRouteObjectName()
    {
        Transform root = transform.root;
        return root != null ? root.name : gameObject.name;
    }

    private void ConfigureAnimator()
    {
        if (visualAnimator != null && disableAnimatorRootMotion)
            visualAnimator.applyRootMotion = false;

        CacheAnimatorParameters();
    }

    private void CacheAnimatorParameters()
    {
        if (_cachedAnimator == visualAnimator &&
            _cachedSpeedParameter == speedParameter &&
            _cachedMove01Parameter == move01Parameter &&
            _cachedGroundedParameter == groundedParameter &&
            _cachedVerticalVelocityParameter == verticalVelocityParameter &&
            _cachedSprint01Parameter == sprint01Parameter)
        {
            return;
        }

        _cachedAnimator = visualAnimator;
        _cachedSpeedParameter = speedParameter;
        _cachedMove01Parameter = move01Parameter;
        _cachedGroundedParameter = groundedParameter;
        _cachedVerticalVelocityParameter = verticalVelocityParameter;
        _cachedSprint01Parameter = sprint01Parameter;
        _hasSpeedParameter = HasFloatParameter(visualAnimator, speedParameter, out _speedParameterHash);
        _hasMove01Parameter = HasFloatParameter(visualAnimator, move01Parameter, out _move01ParameterHash);
        _hasGroundedParameter = HasBoolParameter(visualAnimator, groundedParameter, out _groundedParameterHash);
        _hasVerticalVelocityParameter = HasFloatParameter(visualAnimator, verticalVelocityParameter, out _verticalVelocityParameterHash);
        _hasSprint01Parameter = HasFloatParameter(visualAnimator, sprint01Parameter, out _sprint01ParameterHash);
    }

    private void UpdateAnimator(float planarSpeed)
    {
        if (!updateAnimator || visualAnimator == null)
            return;

        if (disableAnimatorRootMotion)
            visualAnimator.applyRootMotion = false;

        CacheAnimatorParameters();

        if (_hasSpeedParameter)
            visualAnimator.SetFloat(_speedParameterHash, planarSpeed, animatorDampTime, Time.deltaTime);

        if (_hasMove01Parameter)
        {
            float move01 = Mathf.Clamp01(planarSpeed / Mathf.Max(0.01f, speedForMove01));
            visualAnimator.SetFloat(_move01ParameterHash, move01, animatorDampTime, Time.deltaTime);
        }

        if (updateGroundedParameter && _hasGroundedParameter && motorStateSource != null)
            visualAnimator.SetBool(_groundedParameterHash, motorStateSource.IsGrounded);

        if (updateVerticalVelocityParameter && _hasVerticalVelocityParameter)
            visualAnimator.SetFloat(_verticalVelocityParameterHash, targetBody != null ? targetBody.linearVelocity.y : 0f, animatorDampTime, Time.deltaTime);

        if (updateSprintParameter && _hasSprint01Parameter)
        {
            float sprint01 = motorStateSource != null
                ? (motorStateSource.IsSprintHeld ? 1f : 0f)
                : Mathf.Clamp01(planarSpeed / Mathf.Max(0.01f, speedForMove01));
            visualAnimator.SetFloat(_sprint01ParameterHash, sprint01, sprint01DampTime, Time.deltaTime);
        }
    }

    private static bool HasFloatParameter(Animator animator, string parameterName, out int parameterHash)
    {
        parameterHash = 0;
        if (animator == null || string.IsNullOrEmpty(parameterName))
            return false;

        parameterHash = Animator.StringToHash(parameterName);
        AnimatorControllerParameter[] parameters = animator.parameters;
        for (int i = 0; i < parameters.Length; i++)
        {
            if (parameters[i].nameHash == parameterHash && parameters[i].type == AnimatorControllerParameterType.Float)
                return true;
        }

        return false;
    }

    private static bool HasBoolParameter(Animator animator, string parameterName, out int parameterHash)
    {
        parameterHash = 0;
        if (animator == null || string.IsNullOrEmpty(parameterName))
            return false;

        parameterHash = Animator.StringToHash(parameterName);
        AnimatorControllerParameter[] parameters = animator.parameters;
        for (int i = 0; i < parameters.Length; i++)
        {
            if (parameters[i].nameHash == parameterHash && parameters[i].type == AnimatorControllerParameterType.Bool)
                return true;
        }

        return false;
    }

    private void LogDebugState(Vector3 velocity, float planarSpeed, Vector3 localVelocity)
    {
        if (!debugLogs || Time.time < _lastLogTime + 1f)
            return;

        _lastLogTime = Time.time;
        Debug.Log(
            $"[HamsterVisualFollower:{gameObject.name}] targetVelocity={FormatVector3(velocity)} planarSpeed={planarSpeed:F2} previousPlanarSpeed={_lastPlanarSpeed:F2} localVelocity={FormatVector3(localVelocity)} currentPlanarAcceleration={FormatVector3(_currentPlanarAcceleration)} currentYaw={_currentYaw:F1} forwardLean={_currentForwardLean:F1} sideLean={_currentSideLean:F1} enableAccelerationWobble={enableAccelerationWobble} accelForwardWobble={_accelForwardWobble:F1} accelSideWobble={_accelSideWobble:F1} stopOvershoot={_stopOvershoot:F1} turnWobble={_turnWobble:F1} impactForwardWobble={_impactForwardWobble:F1} impactSideWobble={_impactSideWobble:F1} targetImpactForwardWobble={_targetImpactForwardWobble:F1} targetImpactSideWobble={_targetImpactSideWobble:F1} jumpStretch={_jumpStretch:F2} landingSquash={_landingSquash:F2} enableSpeedBasedVisualHeight={enableSpeedBasedVisualHeight} idleVisualYOffset={idleVisualYOffset:F3} movingVisualYOffset={movingVisualYOffset:F3} currentVisualYOffset={_currentVisualYOffset:F3} speedForMovingVisualYOffset={speedForMovingVisualYOffset:F2} visualLocalPosition={FormatVector3(visualRoot.localPosition)} visualLocalEuler={FormatVector3(visualRoot.localRotation.eulerAngles)}",
            this);
    }

    private static string FormatVector3(Vector3 value)
    {
        return $"({value.x:F2},{value.y:F2},{value.z:F2})";
    }

    private static string FormatFloat(float value)
    {
        return float.IsNaN(value) ? "<none>" : value.ToString("F2");
    }

    private static bool IsFiniteVector(Vector3 value)
    {
        return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
    }

    private static bool IsFinite(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }

    private void OnValidate()
    {
        minSpeedToFaceMove = Mathf.Max(0f, minSpeedToFaceMove);
        yawSmoothTime = Mathf.Max(0f, yawSmoothTime);
        speedForMaxLean = Mathf.Max(0.01f, speedForMaxLean);
        maxForwardLeanDegrees = Mathf.Max(0f, maxForwardLeanDegrees);
        maxSideLeanDegrees = Mathf.Max(0f, maxSideLeanDegrees);
        leanSmoothTime = Mathf.Max(0f, leanSmoothTime);
        maxBackLag = Mathf.Max(0f, maxBackLag);
        maxSideLag = Mathf.Max(0f, maxSideLag);
        lagSmoothTime = Mathf.Max(0f, lagSmoothTime);
        speedForMovingVisualYOffset = Mathf.Max(0.01f, speedForMovingVisualYOffset);
        visualHeightSmoothTime = Mathf.Max(0f, visualHeightSmoothTime);
        clipHeightSmoothTime = Mathf.Max(0f, clipHeightSmoothTime);
        clipHeightDownSmoothTime = Mathf.Max(0f, clipHeightDownSmoothTime);
        clipHeightUpSmoothTime = Mathf.Max(0f, clipHeightUpSmoothTime);
        walkEnterSnapMaxDelta = Mathf.Max(0f, walkEnterSnapMaxDelta);
        maxClipHeightYOffset = Mathf.Max(0f, maxClipHeightYOffset);
        idleStateYOffset = ClampClipHeightOffset(idleStateYOffset);
        walkStateYOffset = ClampClipHeightOffset(walkStateYOffset);
        runStateYOffset = ClampClipHeightOffset(runStateYOffset);
        jumpStateYOffset = ClampClipHeightOffset(jumpStateYOffset);
        sprintPivotYOffset = ClampClipHeightOffset(sprintPivotYOffset);
        landingYOffset = ClampClipHeightOffset(landingYOffset);
        accelerationForMaxWobble = Mathf.Max(0.01f, accelerationForMaxWobble);
        maxAccelerationForwardWobbleDegrees = Mathf.Max(0f, maxAccelerationForwardWobbleDegrees);
        maxAccelerationSideWobbleDegrees = Mathf.Max(0f, maxAccelerationSideWobbleDegrees);
        accelerationWobbleSmoothTime = Mathf.Max(0f, accelerationWobbleSmoothTime);
        accelerationWobbleReturnSmoothTime = Mathf.Max(0f, accelerationWobbleReturnSmoothTime);
        stopOvershootSpeedThreshold = Mathf.Max(0f, stopOvershootSpeedThreshold);
        stopOvershootInputThreshold = Mathf.Max(0f, stopOvershootInputThreshold);
        maxStopOvershootDegrees = Mathf.Max(0f, maxStopOvershootDegrees);
        stopOvershootDuration = Mathf.Max(0f, stopOvershootDuration);
        stopOvershootCooldown = Mathf.Max(0f, stopOvershootCooldown);
        turnWobbleYawDeltaForMax = Mathf.Max(1f, turnWobbleYawDeltaForMax);
        maxTurnWobbleDegrees = Mathf.Max(0f, maxTurnWobbleDegrees);
        turnWobbleSmoothTime = Mathf.Max(0f, turnWobbleSmoothTime);
        impactForMaxWobble = Mathf.Max(0.01f, impactForMaxWobble);
        maxImpactForwardWobbleDegrees = Mathf.Max(0f, maxImpactForwardWobbleDegrees);
        maxImpactSideWobbleDegrees = Mathf.Max(0f, maxImpactSideWobbleDegrees);
        impactWobbleSmoothTime = Mathf.Max(0f, impactWobbleSmoothTime);
        impactWobbleReturnSmoothTime = Mathf.Max(0f, impactWobbleReturnSmoothTime);
        jumpStretchAmount = Mathf.Max(0f, jumpStretchAmount);
        jumpPitchDegrees = Mathf.Max(0f, jumpPitchDegrees);
        jumpVisualDuration = Mathf.Max(0f, jumpVisualDuration);
        landingSquashAmount = Mathf.Max(0f, landingSquashAmount);
        landingPitchDegrees = Mathf.Max(0f, landingPitchDegrees);
        landingVisualDuration = Mathf.Max(0f, landingVisualDuration);
        jumpVisualReturnSmoothTime = Mathf.Max(0f, jumpVisualReturnSmoothTime);
        animatorDampTime = Mathf.Max(0f, animatorDampTime);
        speedForMove01 = Mathf.Max(0.01f, speedForMove01);
        sprint01DampTime = Mathf.Max(0f, sprint01DampTime);
    }

    private void OnDrawGizmosSelected()
    {
        if (!drawGizmos)
            return;

        if (targetBody != null)
        {
            Vector3 velocity = targetBody.linearVelocity;
            velocity.y = 0f;

            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(targetBody.position, 0.08f);
            if (velocity.sqrMagnitude > 0.0001f)
                Gizmos.DrawLine(targetBody.position, targetBody.position + velocity.normalized * 0.6f);
        }

        if (visualRoot != null)
        {
            Gizmos.color = Color.magenta;
            Gizmos.DrawWireSphere(visualRoot.position, 0.06f);
        }
    }
}
