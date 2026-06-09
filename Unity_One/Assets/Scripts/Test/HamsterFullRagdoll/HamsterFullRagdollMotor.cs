using UnityEngine;

public class HamsterFullRagdollMotor : MonoBehaviour
{
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
    [SerializeField] private bool drawDebugGizmos = true;
    [SerializeField] private bool debugLogs = false;

    private const float InputDeadzone = 0.01f;
    private const float MaxUprightTorque = 250f;
    private const float MaxTurnTorque = 160f;
    private const float MaxStopAssistAcceleration = 35f;

    private Vector2 _smoothedMoveInput;
    private Vector2 _smoothInputVelocity;
    private Vector3 _smoothedMoveWorldDirection;
    private bool _isGrounded;
    private bool _legacyInputUnavailable;
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

    public bool IsGrounded => _isGrounded;
    public Vector3 SmoothedMoveWorldDirection => _smoothedMoveWorldDirection;
    public float CurrentPlanarSpeed => GetPlanarVelocity(hipsBody).magnitude;

    public void SetControlStrength(float value)
    {
        controlStrength = Mathf.Max(0f, value);
    }

    private void Awake()
    {
        if (!HasRequiredReferences())
        {
            LogMissingRequiredReferences();
            enabled = false;
            return;
        }

        CaptureInitialPose();
    }

    private void OnEnable()
    {
        if (captureInitialPoseOnEnable && HasRequiredReferences())
            CaptureInitialPose();

        LogMotorEnabledState();
    }

    private void FixedUpdate()
    {
        if (!HasRequiredReferences())
            return;

        if (!_hasInitialPose)
            CaptureInitialPose();

        float fixedDeltaTime = Time.fixedDeltaTime;
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
        _smoothedMoveWorldDirection = BuildMoveDirection(_smoothedMoveInput);

        bool sprintHeld = IsSprintHeld();
        float maxSpeed = sprintHeld ? maxSprintSpeed : maxWalkSpeed;
        float acceleration = sprintHeld ? sprintAcceleration : walkAcceleration;
        float groundedMultiplier = _isGrounded ? 1f : airControlMultiplier;
        float effectiveControl = Mathf.Max(0f, controlStrength) * groundedMultiplier;
        _lastMaxSpeed = maxSpeed;
        _lastSelectedAcceleration = acceleration;
        _lastEffectiveControl = effectiveControl;

        ApplyMovementForce(_smoothedMoveWorldDirection, maxSpeed, acceleration, effectiveControl);
        ApplyStopDragAssist(_smoothedMoveInput, effectiveControl);
        ApplyUprightTorque(hipsBody, uprightStrength, uprightDamping);
        ApplyUprightTorque(chestBody, uprightStrength * chestUprightMultiplier, uprightDamping * chestUprightMultiplier);

        bool hasMoveInput = _smoothedMoveInput.sqrMagnitude > InputDeadzone * InputDeadzone;
        bool poseHoldActive = enableStandingPoseHold && _hasInitialPose;
        if (poseHoldActive)
            ApplyStandingPoseHold(hasMoveInput, fixedDeltaTime);

        if (!poseHoldActive || !disableTurnTorqueWhilePoseHold)
            ApplyTurnTorque(_smoothedMoveWorldDirection, effectiveControl);

        LogDebugState(hasMoveInput, poseHoldActive);
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
        _hasInitialPose = true;
    }

    private Vector2 ReadMoveInput()
    {
        if (_legacyInputUnavailable)
            return Vector2.zero;

        try
        {
            float horizontal = Input.GetAxisRaw("Horizontal");
            float vertical = Input.GetAxisRaw("Vertical");
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

    private bool IsSprintHeld()
    {
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

    private void DisableLegacyInput(string reason)
    {
        _legacyInputUnavailable = true;
        Log($"Legacy Input Manager unavailable. Movement input disabled. Reason={reason}");
    }

    private Vector3 BuildMoveDirection(Vector2 moveInput)
    {
        if (moveInput.sqrMagnitude <= InputDeadzone * InputDeadzone)
            return Vector3.zero;

        Transform referenceTransform = cameraTransform != null ? cameraTransform : transform;
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

        Vector3 direction = right * moveInput.x + forward * moveInput.y;
        if (direction.sqrMagnitude > 1f)
            direction.Normalize();

        return direction;
    }

    private void ApplyMovementForce(Vector3 moveDirection, float maxSpeed, float acceleration, float effectiveControl)
    {
        _lastAppliedMoveForce = Vector3.zero;
        _lastAppliedAcceleration = 0f;
        _lastAccelerationScale = 0f;
        _lastMoveForceApplied = false;
        _lastMoveForceSkipReason = "none";

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
        if (yawPoseTowardsMoveDirection && hasMoveInput && _smoothedMoveWorldDirection.sqrMagnitude > 0.0001f)
            targetYaw = Vector3.SignedAngle(_initialForwardOnPlane, _smoothedMoveWorldDirection.normalized, Vector3.up);

        _lastPoseTargetYaw = targetYaw;
        if (poseYawSmoothTime <= 0f)
            _currentPoseYaw = targetYaw;
        else
            _currentPoseYaw = Mathf.SmoothDampAngle(_currentPoseYaw, targetYaw, ref _poseYawVelocity, poseYawSmoothTime, Mathf.Infinity, fixedDeltaTime);

        Quaternion yawRotation = Quaternion.AngleAxis(_currentPoseYaw, Vector3.up);
        float strengthMultiplier = _isGrounded ? groundedPoseStrengthMultiplier : airbornePoseStrengthMultiplier;
        strengthMultiplier = Mathf.Max(0f, strengthMultiplier);

        ApplyRotationSpring(
            hipsBody,
            yawRotation * _initialHipsRotation,
            hipsPoseSpring,
            hipsPoseDamping,
            hipsMaxPoseTorque,
            strengthMultiplier);

        ApplyRotationSpring(
            chestBody,
            yawRotation * _initialChestRotation,
            chestPoseSpring,
            chestPoseDamping,
            chestMaxPoseTorque,
            strengthMultiplier);

        ApplyRotationSpring(
            headBody,
            yawRotation * _initialHeadRotation,
            headPoseSpring,
            headPoseDamping,
            headMaxPoseTorque,
            strengthMultiplier);

        ApplyRotationSpring(
            leftArmBody,
            yawRotation * _initialLeftArmRotation,
            armPoseSpring,
            armPoseDamping,
            armMaxPoseTorque,
            strengthMultiplier);

        ApplyRotationSpring(
            rightArmBody,
            yawRotation * _initialRightArmRotation,
            armPoseSpring,
            armPoseDamping,
            armMaxPoseTorque,
            strengthMultiplier);
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

    private bool CheckGrounded()
    {
        Vector3 origin = hipsBody.worldCenterOfMass + Vector3.up * groundCheckRadius;
        float distance = Mathf.Max(0.01f, groundCheckDistance + groundCheckRadius);
        return Physics.SphereCast(
            origin,
            groundCheckRadius,
            Vector3.down,
            out _,
            distance,
            groundMask,
            QueryTriggerInteraction.Ignore);
    }

    private static Vector3 GetPlanarVelocity(Rigidbody targetBody)
    {
        if (targetBody == null)
            return Vector3.zero;

        Vector3 velocity = targetBody.linearVelocity;
        velocity.y = 0f;
        return velocity;
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
            $"[HamsterFullRagdollMotor:{gameObject.name}] objectPath={motorPath} hipsBody={GetBodyName(hipsBody)} hipsPath={hipsPath} controlStrength={controlStrength:F2} effectiveControl={_lastEffectiveControl:F2} rawInput={FormatVector2(_lastRawMoveInput)} smoothedInput={FormatVector2(_smoothedMoveInput)} hasMoveInput={hasMoveInput} moveWorldDirection={FormatVector3(_smoothedMoveWorldDirection)} grounded={_isGrounded} maxWalkSpeed={maxWalkSpeed:F2} maxSprintSpeed={maxSprintSpeed:F2} walkAcceleration={walkAcceleration:F2} sprintAcceleration={sprintAcceleration:F2} selectedAcceleration={_lastSelectedAcceleration:F2} appliedAcceleration={_lastAppliedAcceleration:F2} accelerationScale={_lastAccelerationScale:F2} finalForce={FormatVector3(_lastAppliedMoveForce)} moveForceApplied={_lastMoveForceApplied} moveForceSkip='{_lastMoveForceSkipReason}' stopDragAssist={stopDragAssist:F2} airControlMultiplier={airControlMultiplier:F2} stopDragForce={FormatVector3(_lastAppliedStopDragForce)} stopDragApplied={_lastStopDragApplied} stopDragSkip='{_lastStopDragSkipReason}' hipsVelocity={hipsVelocity} planarSpeed={CurrentPlanarSpeed:F2} {hipsState} maxSpeed={_lastMaxSpeed:F2} poseHold={poseHoldActive} yawTarget={_lastPoseTargetYaw:F1}",
            this);
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
