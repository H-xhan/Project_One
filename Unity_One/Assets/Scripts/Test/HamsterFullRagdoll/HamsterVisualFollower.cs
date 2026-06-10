using UnityEngine;

public sealed class HamsterVisualFollower : MonoBehaviour
{
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

    [Header("Animator")]
    [SerializeField] private bool updateAnimator = true;
    [SerializeField] private bool disableAnimatorRootMotion = true;
    [SerializeField] private string speedParameter = "Speed";
    [SerializeField] private string move01Parameter = "Move01";
    [SerializeField] private float animatorDampTime = 0.10f;
    [SerializeField] private float speedForMove01 = 2.5f;

    [Header("Debug")]
    [SerializeField] private bool debugLogs = false;
    [SerializeField] private bool drawGizmos = true;

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

    private Vector3 _currentLagOffset;
    private Animator _cachedAnimator;
    private string _cachedSpeedParameter;
    private string _cachedMove01Parameter;
    private int _speedParameterHash;
    private int _move01ParameterHash;
    private bool _hasSpeedParameter;
    private bool _hasMove01Parameter;
    private bool _missingVisualRootLogged;
    private bool _missingTargetBodyLogged;

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

        UpdateMotionWobble(planarVelocity, planarSpeed, deltaTime);
        UpdateVisualPosition(localVelocity, planarSpeed);
        UpdateVisualRotation(planarVelocity, planarSpeed, localVelocity);
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
    }

    private void CacheInitialLocalTransform()
    {
        if (visualRoot == null)
            return;

        _initialLocalPosition = visualRoot.localPosition;
        _initialLocalRotation = visualRoot.localRotation;
        _initialLocalScale = visualRoot.localScale;
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

        if (targetBody == null)
            return;

        _previousPlanarVelocity = targetBody.linearVelocity;
        _previousPlanarVelocity.y = 0f;
        _lastPlanarSpeed = _previousPlanarVelocity.magnitude;

        Vector3 initialDirection = Vector3.ProjectOnPlane(targetBody.transform.forward, Vector3.up);
        _lastPlanarMoveDirection = initialDirection.sqrMagnitude > 0.0001f
            ? initialDirection.normalized
            : Vector3.forward;
    }

    private void UpdateVisualPosition(Vector3 localVelocity, float planarSpeed)
    {
        Vector3 targetLagOffset = Vector3.zero;
        if (enableLocalLag)
        {
            float speedScale = Mathf.Max(0.01f, speedForMaxLean);
            float backLag = -Mathf.Clamp(localVelocity.z / speedScale, -1f, 1f) * maxBackLag;
            float sideLag = -Mathf.Clamp(localVelocity.x / speedScale, -1f, 1f) * maxSideLag;
            targetLagOffset = new Vector3(sideLag, 0f, backLag);
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
        targetOffset.y = ResolveVisualYOffset(planarSpeed);
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

    private void UpdateMotionWobble(Vector3 planarVelocity, float planarSpeed, float deltaTime)
    {
        if (deltaTime > 0.0001f)
            _currentPlanarAcceleration = (planarVelocity - _previousPlanarVelocity) / deltaTime;
        else
            _currentPlanarAcceleration = Vector3.zero;

        UpdateAccelerationWobble(deltaTime);
        UpdateStopOvershoot(planarSpeed, deltaTime);
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
        float targetYaw = 0f;
        if (faceMoveDirection)
        {
            bool hasMoveDirection = planarSpeed >= minSpeedToFaceMove && planarVelocity.sqrMagnitude > 0.0001f;
            if (hasMoveDirection)
                _lastPlanarMoveDirection = planarVelocity.normalized;

            if (hasMoveDirection || (keepLastMoveYawWhenIdle && _lastPlanarMoveDirection.sqrMagnitude > 0.0001f))
                targetYaw = CalculateTargetYaw(_lastPlanarMoveDirection);
        }

        _currentYaw = Mathf.SmoothDampAngle(
            _currentYaw,
            targetYaw,
            ref _yawVelocity,
            yawSmoothTime,
            Mathf.Infinity,
            Time.deltaTime);
        UpdateTurnWobble(Time.deltaTime);

        float targetForwardLean = 0f;
        float targetSideLean = 0f;
        if (enableBodyLean)
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
        Quaternion leanRot = Quaternion.Euler(_currentForwardLean, 0f, _currentSideLean);
        Quaternion accelerationWobbleRot = Quaternion.Euler(_accelForwardWobble, 0f, _accelSideWobble);
        Quaternion stopOvershootRot = Quaternion.Euler(_stopOvershoot, 0f, 0f);
        Quaternion turnWobbleRot = Quaternion.Euler(0f, 0f, _turnWobble);
        Quaternion offsetRot = Quaternion.Euler(visualLocalEulerOffset);

        if (visualRootIsChildOfTarget)
            visualRoot.localRotation = _initialLocalRotation * yawRot * leanRot * accelerationWobbleRot * stopOvershootRot * turnWobbleRot * offsetRot;
        else
            visualRoot.rotation = yawRot * leanRot * accelerationWobbleRot * stopOvershootRot * turnWobbleRot * offsetRot;
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
            _cachedMove01Parameter == move01Parameter)
        {
            return;
        }

        _cachedAnimator = visualAnimator;
        _cachedSpeedParameter = speedParameter;
        _cachedMove01Parameter = move01Parameter;
        _hasSpeedParameter = HasFloatParameter(visualAnimator, speedParameter, out _speedParameterHash);
        _hasMove01Parameter = HasFloatParameter(visualAnimator, move01Parameter, out _move01ParameterHash);
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

    private void LogDebugState(Vector3 velocity, float planarSpeed, Vector3 localVelocity)
    {
        if (!debugLogs || Time.time < _lastLogTime + 1f)
            return;

        _lastLogTime = Time.time;
        Debug.Log(
            $"[HamsterVisualFollower:{gameObject.name}] targetVelocity={FormatVector3(velocity)} planarSpeed={planarSpeed:F2} previousPlanarSpeed={_lastPlanarSpeed:F2} localVelocity={FormatVector3(localVelocity)} currentPlanarAcceleration={FormatVector3(_currentPlanarAcceleration)} currentYaw={_currentYaw:F1} forwardLean={_currentForwardLean:F1} sideLean={_currentSideLean:F1} enableAccelerationWobble={enableAccelerationWobble} accelForwardWobble={_accelForwardWobble:F1} accelSideWobble={_accelSideWobble:F1} stopOvershoot={_stopOvershoot:F1} turnWobble={_turnWobble:F1} enableSpeedBasedVisualHeight={enableSpeedBasedVisualHeight} idleVisualYOffset={idleVisualYOffset:F3} movingVisualYOffset={movingVisualYOffset:F3} currentVisualYOffset={_currentVisualYOffset:F3} speedForMovingVisualYOffset={speedForMovingVisualYOffset:F2} visualLocalPosition={FormatVector3(visualRoot.localPosition)} visualLocalEuler={FormatVector3(visualRoot.localRotation.eulerAngles)}",
            this);
    }

    private static string FormatVector3(Vector3 value)
    {
        return $"({value.x:F2},{value.y:F2},{value.z:F2})";
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
        animatorDampTime = Mathf.Max(0f, animatorDampTime);
        speedForMove01 = Mathf.Max(0.01f, speedForMove01);
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
