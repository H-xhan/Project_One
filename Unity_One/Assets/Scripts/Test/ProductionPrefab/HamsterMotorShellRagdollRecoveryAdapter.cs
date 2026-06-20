using Unity.Netcode;
using UnityEngine;

public sealed class HamsterMotorShellRagdollRecoveryAdapter : MonoBehaviour
{
    private const string TargetRootName = "Hamster_JointFreeMotorShell_MainScenes";
    private const float ImpactedDuration = 0.08f;
    private const float MinLogInterval = 0.05f;

    public enum RecoveryState
    {
        Normal,
        Impacted,
        KnockedDown,
        Recovering,
        LiquidSwept
    }

    [Header("References")]
    [SerializeField] private HamsterFullRagdollMotor motor;
    [SerializeField] private Rigidbody bodyRigidbody;
    [SerializeField] private Transform bodyTransform;
    [SerializeField] private HamsterVisualFollower visualFollower;
    [SerializeField] private MotorShellRootBodySync rootBodySync;
    [SerializeField] private HamsterMotorShellCombatAdapter combatAdapter;

    [Header("Recovery")]
    [SerializeField] private bool enableRecoveryAdapter = true;
    [SerializeField] private float impactImpulseMultiplier = 1.0f;
    [SerializeField] private float impactUpwardBonus = 0.5f;
    [SerializeField] private float impactTorqueMultiplier = 1.0f;
    [SerializeField] private float minKnockdownImpulse = 3.0f;
    [SerializeField] private bool useProductionLikeLaunch = true;
    [SerializeField] private bool useVelocityChangeForImpact = true;
    [SerializeField] private float productionHorizontalLaunchScale = 0.65f;
    [SerializeField] private float productionMinimumUpwardLaunch = 2.8f;
    [SerializeField] private float productionUpwardLaunchBonus = 1.1f;
    [SerializeField] private float productionTumbleTorque = 10f;
    [SerializeField] private float productionRandomYawTorque = 2.5f;
    [SerializeField] private float knockdownDuration = 1.0f;
    [SerializeField] private float recoveryDuration = 0.8f;
    [SerializeField] private float maxKnockdownDuration = 2.0f;
    [SerializeField] private bool disableMotorControlWhileKnocked = true;
    [SerializeField] private bool releaseRotationConstraintsWhileKnocked = true;
    [SerializeField] private bool restoreOriginalConstraintsOnRecover = true;
    [SerializeField] private float recoveryUprightTorque = 30f;
    [SerializeField] private float recoveryDamping = 5f;
    [SerializeField] private bool groundedRequiredToFinishRecovery = true;
    [SerializeField] private float maxRecoverPlanarSpeed = 2.0f;
    [SerializeField] private float minRecoverUpDot = 0.85f;

    [Header("Liquid Sweep")]
    [SerializeField] private float liquidSweepControlMultiplier = 0.25f;
    [SerializeField] private float liquidSweepDuration = 1.2f;
    [SerializeField] private float liquidSweepForceMultiplier = 1.0f;
    [SerializeField] private float liquidSweepInitialImpulseMultiplier = 0.35f;
    [SerializeField] private bool liquidSweepAllowJump = false;

    [Header("Debug")]
    [SerializeField] private bool debugRecoveryLogs = false;
    [SerializeField] private float debugLogInterval = 0.5f;

    private RecoveryState _state = RecoveryState.Normal;
    private RigidbodyConstraints _originalConstraints;
    private bool _hasOriginalConstraints;
    private float _stateTimer;
    private float _activeKnockdownDuration;
    private float _activeRecoveryDuration;
    private float _activeLiquidSweepDuration;
    private Vector3 _liquidSweepDirection;
    private float _liquidSweepForce;
    private string _lastSource = "none";
    private float _nextDebugLogTime;
    private bool _lastReleasedConstraints;

    public RecoveryState CurrentRecoveryState => _state;
    public bool IsKnockedOrRecovering => _state == RecoveryState.Impacted || _state == RecoveryState.KnockedDown || _state == RecoveryState.Recovering;
    public bool IsLiquidSwept => _state == RecoveryState.LiquidSwept;
    public bool IsControlLockedByRecovery => IsKnockedOrRecovering || (_state == RecoveryState.LiquidSwept && !liquidSweepAllowJump);
    public bool ShouldVisualFollowBodyRotation => IsTargetRoot() && IsKnockedOrRecovering;
    public bool CanReceiveRecoveryState => enableRecoveryAdapter && IsTargetRoot();
    public Rigidbody BodyRigidbody
    {
        get
        {
            ResolveReferences();
            return bodyRigidbody;
        }
    }

    private void Awake()
    {
        ResolveReferences();
        CaptureOriginalConstraints();
    }

    private void OnEnable()
    {
        ResolveReferences();
        CaptureOriginalConstraints();
    }

    private void OnDisable()
    {
        RestoreOriginalConstraints();
        RestoreNormalMotorControl();
    }

    private void FixedUpdate()
    {
        if (!enableRecoveryAdapter || !IsTargetRoot())
            return;

        ResolveReferences();
        if (bodyRigidbody == null)
            return;

        float fixedDeltaTime = Time.fixedDeltaTime;
        switch (_state)
        {
            case RecoveryState.Impacted:
                TickImpacted(fixedDeltaTime);
                break;
            case RecoveryState.KnockedDown:
                TickKnockedDown(fixedDeltaTime);
                break;
            case RecoveryState.Recovering:
                TickRecovering(fixedDeltaTime);
                break;
            case RecoveryState.LiquidSwept:
                TickLiquidSwept(fixedDeltaTime);
                break;
        }

        LogDebugState();
    }

    public bool ApplyImpact(Vector3 impulse, Vector3 hitPoint, string source, float overrideKnockdownDuration = -1f)
    {
        return ApplyImpactInternal(impulse, hitPoint, source, overrideKnockdownDuration, false);
    }

    public bool ApplyGimmickImpact(Vector3 impulse, Vector3 hitPoint, string gimmickName)
    {
        return ApplyImpactInternal(
            impulse,
            hitPoint,
            string.IsNullOrEmpty(gimmickName) ? "Gimmick" : gimmickName,
            knockdownDuration,
            true);
    }

    public bool ApplyLiquidSweep(Vector3 flowDirection, float force, float duration, string source)
    {
        RecoveryState stateBefore = _state;
        bool result = ApplyLiquidSweepInternal(flowDirection, force, duration, source);
        LogLiquidRequest(source, flowDirection, force, duration, stateBefore, _state, result);
        return result;
    }

    private bool ApplyImpactInternal(Vector3 impulse, Vector3 hitPoint, string source, float overrideKnockdownDuration, bool forceKnockdown)
    {
        RecoveryState stateBefore = _state;
        RigidbodyConstraints originalConstraints = bodyRigidbody != null ? bodyRigidbody.constraints : RigidbodyConstraints.None;
        RigidbodyConstraints releasedConstraints = originalConstraints;
        Vector3 adjustedImpulse = Vector3.zero;
        bool shouldKnockdown = false;
        bool result = false;
        string failureReason = "none";

        if (!CanApplyStateChange())
        {
            failureReason = "state change not allowed";
            LogImpactRequest(source, impulse, adjustedImpulse, 0f, forceKnockdown, shouldKnockdown, originalConstraints, releasedConstraints, stateBefore, _state, false, failureReason);
            return false;
        }

        ResolveReferences();
        if (bodyRigidbody == null || bodyRigidbody.isKinematic)
        {
            failureReason = bodyRigidbody == null ? "body missing" : "body kinematic";
            LogImpactRequest(source, impulse, adjustedImpulse, 0f, forceKnockdown, shouldKnockdown, originalConstraints, releasedConstraints, stateBefore, _state, false, failureReason);
            return false;
        }

        originalConstraints = bodyRigidbody.constraints;

        Vector3 safeImpulse = SanitizeVector(impulse);
        if (safeImpulse.sqrMagnitude <= 0.0001f)
        {
            failureReason = "impulse too small";
            LogImpactRequest(source, impulse, adjustedImpulse, 0f, forceKnockdown, shouldKnockdown, originalConstraints, releasedConstraints, stateBefore, _state, false, failureReason);
            return false;
        }

        adjustedImpulse = BuildImpactLaunch(safeImpulse);
        float magnitude = adjustedImpulse.magnitude;
        shouldKnockdown = forceKnockdown || overrideKnockdownDuration > 0f || magnitude >= Mathf.Max(0f, minKnockdownImpulse);
        float duration = overrideKnockdownDuration > 0f ? overrideKnockdownDuration : knockdownDuration;
        if (shouldKnockdown)
        {
            BeginImpactStateBeforeImpulse(source, duration);
            releasedConstraints = bodyRigidbody.constraints;
        }
        else
        {
            bodyRigidbody.WakeUp();
        }

        ForceMode forceMode = useVelocityChangeForImpact ? ForceMode.VelocityChange : ForceMode.Impulse;
        bodyRigidbody.AddForce(adjustedImpulse, forceMode);
        ApplyImpactTorque(adjustedImpulse, hitPoint, forceMode);
        result = true;

        LogImpactRequest(source, impulse, adjustedImpulse, magnitude, forceKnockdown, shouldKnockdown, originalConstraints, releasedConstraints, stateBefore, _state, result, failureReason);
        return result;
    }

    private bool ApplyLiquidSweepInternal(Vector3 flowDirection, float force, float duration, string source)
    {
        if (!CanApplyStateChange())
            return false;

        ResolveReferences();
        if (bodyRigidbody == null || bodyRigidbody.isKinematic)
            return false;

        Vector3 planarDirection = Vector3.ProjectOnPlane(SanitizeVector(flowDirection), Vector3.up);
        if (planarDirection.sqrMagnitude <= 0.0001f)
            return false;

        _liquidSweepDirection = planarDirection.normalized;
        _liquidSweepForce = Mathf.Max(0f, force);
        _activeLiquidSweepDuration = Mathf.Max(0.01f, duration > 0f ? duration : liquidSweepDuration);
        _stateTimer = 0f;
        _lastSource = string.IsNullOrEmpty(source) ? "LiquidSweep" : source;
        SetState(RecoveryState.LiquidSwept, _lastSource);
        ApplyLiquidSweepMotorControl();
        bodyRigidbody.WakeUp();

        float initialImpulse = _liquidSweepForce * Mathf.Max(0f, liquidSweepInitialImpulseMultiplier);
        if (initialImpulse > 0f)
            bodyRigidbody.AddForce(_liquidSweepDirection * initialImpulse, ForceMode.VelocityChange);

        return true;
    }

    public static bool TryFindOnCollider(Collider hit, out HamsterMotorShellRagdollRecoveryAdapter adapter)
    {
        adapter = null;
        if (hit == null)
            return false;

        adapter = hit.GetComponentInParent<HamsterMotorShellRagdollRecoveryAdapter>();
        if (IsUsableAdapter(adapter))
            return true;

        Rigidbody attachedRigidbody = hit.attachedRigidbody;
        if (attachedRigidbody != null)
        {
            adapter = attachedRigidbody.GetComponentInParent<HamsterMotorShellRagdollRecoveryAdapter>();
            if (IsUsableAdapter(adapter))
                return true;
        }

        Transform root = hit.transform.root;
        adapter = root != null ? root.GetComponentInChildren<HamsterMotorShellRagdollRecoveryAdapter>(true) : null;
        return IsUsableAdapter(adapter);
    }

    public static bool TryFindOnTransform(Transform target, out HamsterMotorShellRagdollRecoveryAdapter adapter)
    {
        adapter = null;
        if (target == null)
            return false;

        adapter = target.GetComponentInParent<HamsterMotorShellRagdollRecoveryAdapter>();
        if (IsUsableAdapter(adapter))
            return true;

        Transform root = target.root;
        adapter = root != null ? root.GetComponentInChildren<HamsterMotorShellRagdollRecoveryAdapter>(true) : null;
        return IsUsableAdapter(adapter);
    }

    private static bool IsUsableAdapter(HamsterMotorShellRagdollRecoveryAdapter adapter)
    {
        return adapter != null && adapter.isActiveAndEnabled && adapter.enableRecoveryAdapter && adapter.IsTargetRoot();
    }

    private void TickImpacted(float fixedDeltaTime)
    {
        _stateTimer += fixedDeltaTime;
        ApplyKnockedMotorControl();
        if (_stateTimer >= ImpactedDuration)
            EnterKnockedDown();
    }

    private void TickKnockedDown(float fixedDeltaTime)
    {
        _stateTimer += fixedDeltaTime;
        ApplyKnockedMotorControl();

        if (_stateTimer >= _activeKnockdownDuration)
            EnterRecovering();
    }

    private void TickRecovering(float fixedDeltaTime)
    {
        _stateTimer += fixedDeltaTime;
        float duration = Mathf.Max(0.01f, _activeRecoveryDuration);
        float recovery01 = Mathf.Clamp01(_stateTimer / duration);

        ApplyRecoveryMotorControl(recovery01);
        ApplyRecoveryUprightTorque(recovery01);

        bool durationFinished = _stateTimer >= duration;
        bool groundedOk = !groundedRequiredToFinishRecovery || motor == null || motor.IsGrounded;
        bool speedOk = GetPlanarSpeed() <= Mathf.Max(0f, maxRecoverPlanarSpeed);
        bool uprightOk = bodyTransform == null || Vector3.Dot(bodyTransform.up, Vector3.up) >= Mathf.Clamp(minRecoverUpDot, -1f, 1f);
        bool failSafeFinished = _stateTimer >= Mathf.Max(duration, maxKnockdownDuration);
        if ((durationFinished && groundedOk && speedOk && uprightOk) || failSafeFinished)
            EnterNormal();
    }

    private void TickLiquidSwept(float fixedDeltaTime)
    {
        _stateTimer += fixedDeltaTime;
        ApplyLiquidSweepMotorControl();

        float force = _liquidSweepForce * Mathf.Max(0f, liquidSweepForceMultiplier);
        if (force > 0f && _liquidSweepDirection.sqrMagnitude > 0.0001f)
            bodyRigidbody.AddForce(_liquidSweepDirection * force, ForceMode.Acceleration);

        if (_stateTimer >= _activeLiquidSweepDuration)
            EnterNormal();
    }

    private void EnterImpacted(string source, float duration)
    {
        SetState(RecoveryState.Impacted, source);
        _stateTimer = 0f;
        _activeKnockdownDuration = Mathf.Min(
            Mathf.Max(0.01f, duration),
            Mathf.Max(0.01f, maxKnockdownDuration));
        _activeRecoveryDuration = Mathf.Max(0.01f, recoveryDuration);
        _lastSource = string.IsNullOrEmpty(source) ? "Impact" : source;
        CaptureOriginalConstraints();
        ReleaseRotationConstraints();
        ApplyKnockedMotorControl();
        bodyRigidbody?.WakeUp();
    }

    private void BeginImpactStateBeforeImpulse(string source, float duration)
    {
        SetState(RecoveryState.Impacted, source);
        _stateTimer = 0f;
        _activeKnockdownDuration = Mathf.Min(
            Mathf.Max(0.01f, duration),
            Mathf.Max(0.01f, maxKnockdownDuration));
        _activeRecoveryDuration = Mathf.Max(0.01f, recoveryDuration);
        _lastSource = string.IsNullOrEmpty(source) ? "Impact" : source;
        CaptureOriginalConstraints();
        ReleaseRotationConstraints();
        ApplyKnockedMotorControl();
        bodyRigidbody?.WakeUp();
    }

    private void EnterKnockedDown()
    {
        SetState(RecoveryState.KnockedDown, _lastSource);
        _stateTimer = 0f;
        ReleaseRotationConstraints();
        ApplyKnockedMotorControl();
        bodyRigidbody?.WakeUp();
    }

    private void EnterRecovering()
    {
        SetState(RecoveryState.Recovering, _lastSource);
        _stateTimer = 0f;
        ReleaseRotationConstraints();
        ApplyRecoveryMotorControl(0f);
        bodyRigidbody?.WakeUp();
    }

    private void EnterNormal()
    {
        SetState(RecoveryState.Normal, _lastSource);
        _stateTimer = 0f;
        _liquidSweepDirection = Vector3.zero;
        _liquidSweepForce = 0f;
        RestoreOriginalConstraints();
        RestoreNormalMotorControl();
    }

    private void SetState(RecoveryState nextState, string source)
    {
        if (_state == nextState)
            return;

        RecoveryState previousState = _state;
        _state = nextState;
        LogStateChange(previousState, nextState, source);
    }

    private Vector3 BuildImpactLaunch(Vector3 safeImpulse)
    {
        float multiplier = Mathf.Max(0f, impactImpulseMultiplier);
        if (!useProductionLikeLaunch)
            return safeImpulse * multiplier + Vector3.up * Mathf.Max(0f, impactUpwardBonus);

        Vector3 flat = Vector3.ProjectOnPlane(safeImpulse, Vector3.up);
        Vector3 launch = flat * Mathf.Max(0f, productionHorizontalLaunchScale);
        float upward = Mathf.Max(
            Mathf.Max(0f, productionMinimumUpwardLaunch),
            safeImpulse.y + Mathf.Max(0f, productionUpwardLaunchBonus));
        launch.y = upward + Mathf.Max(0f, impactUpwardBonus);
        return launch * multiplier;
    }

    private void ApplyImpactTorque(Vector3 impulse, Vector3 hitPoint, ForceMode forceMode)
    {
        float torqueScale = Mathf.Max(0f, impactTorqueMultiplier);
        if (torqueScale <= 0f)
            return;

        Vector3 torque;
        if (useProductionLikeLaunch)
        {
            Vector3 planarImpulse = Vector3.ProjectOnPlane(impulse, Vector3.up);
            Vector3 axis = planarImpulse.sqrMagnitude > 0.0001f
                ? Vector3.Cross(planarImpulse.normalized, Vector3.up)
                : bodyRigidbody.transform.right;
            torque = axis * Mathf.Max(0f, productionTumbleTorque) * torqueScale;
            float yawTorque = Mathf.Max(0f, productionRandomYawTorque);
            if (yawTorque > 0f)
                torque += Vector3.up * Random.Range(-yawTorque, yawTorque) * torqueScale;
        }
        else
        {
            Vector3 lever = hitPoint - bodyRigidbody.worldCenterOfMass;
            if (!IsFiniteVector(lever) || lever.sqrMagnitude <= 0.0001f)
                lever = Vector3.Cross(Vector3.up, Vector3.ProjectOnPlane(impulse, Vector3.up));

            torque = Vector3.Cross(lever.normalized, impulse.normalized) * impulse.magnitude * torqueScale;
            if (!IsFiniteVector(torque) || torque.sqrMagnitude <= 0.0001f)
            {
                Vector3 planarImpulse = Vector3.ProjectOnPlane(impulse, Vector3.up);
                Vector3 axis = planarImpulse.sqrMagnitude > 0.0001f
                    ? Vector3.Cross(Vector3.up, planarImpulse.normalized)
                    : bodyRigidbody.transform.right;
                torque = axis * impulse.magnitude * torqueScale;
            }
        }

        bodyRigidbody.AddTorque(torque, forceMode);
    }

    private void ApplyRecoveryUprightTorque(float recovery01)
    {
        if (bodyTransform == null || bodyRigidbody == null)
            return;

        float strength = Mathf.Max(0f, recoveryUprightTorque) * Mathf.Lerp(0.35f, 1f, recovery01);
        if (strength <= 0f)
            return;

        Vector3 correctionAxis = Vector3.Cross(bodyTransform.up, Vector3.up);
        Vector3 dampingTorque = -bodyRigidbody.angularVelocity * Mathf.Max(0f, recoveryDamping);
        Vector3 torque = correctionAxis * strength + dampingTorque;
        if (IsFiniteVector(torque) && torque.sqrMagnitude > 0.0001f)
            bodyRigidbody.AddTorque(torque, ForceMode.Acceleration);
    }

    private void ApplyKnockedMotorControl()
    {
        if (motor == null)
            return;

        if (disableMotorControlWhileKnocked)
            motor.SetExternalControlLock(true, "RagdollRecovery:" + _state);
        else
            motor.SetExternalControlScale(0f, "RagdollRecovery:" + _state);

        motor.SetExternalJumpLock(true, "RagdollRecovery:" + _state);
    }

    private void ApplyRecoveryMotorControl(float recovery01)
    {
        if (motor == null)
            return;

        motor.SetExternalControlLock(false, "RagdollRecovery:Recovering");
        motor.SetExternalJumpLock(true, "RagdollRecovery:Recovering");
        motor.SetExternalMovementControlScale(Mathf.Lerp(0.15f, 1f, recovery01), "RagdollRecovery:Recovering");
        motor.SetExternalUprightControlScale(Mathf.Lerp(0.35f, 1f, recovery01), "RagdollRecovery:Recovering");
        motor.SetExternalPoseControlScale(Mathf.Lerp(0.15f, 1f, recovery01), "RagdollRecovery:Recovering");
    }

    private void ApplyLiquidSweepMotorControl()
    {
        if (motor == null)
            return;

        motor.SetExternalControlLock(false, "RagdollRecovery:LiquidSwept");
        motor.SetExternalJumpLock(!liquidSweepAllowJump, "RagdollRecovery:LiquidSwept");
        motor.SetExternalMovementControlScale(Mathf.Clamp01(liquidSweepControlMultiplier), "RagdollRecovery:LiquidSwept");
        motor.SetExternalUprightControlScale(1f, "RagdollRecovery:LiquidSwept");
        motor.SetExternalPoseControlScale(0.7f, "RagdollRecovery:LiquidSwept");
    }

    private void RestoreNormalMotorControl()
    {
        if (motor == null)
            return;

        motor.SetExternalControlLock(false, "RagdollRecovery:Normal");
        motor.SetExternalJumpLock(false, "RagdollRecovery:Normal");
        motor.SetExternalMovementControlScale(1f, "RagdollRecovery:Normal");
        motor.SetExternalUprightControlScale(1f, "RagdollRecovery:Normal");
        motor.SetExternalPoseControlScale(1f, "RagdollRecovery:Normal");
    }

    private bool CanApplyStateChange()
    {
        if (!enableRecoveryAdapter || !IsTargetRoot())
            return false;

        NetworkManager networkManager = NetworkManager.Singleton;
        return networkManager == null || !networkManager.IsListening || networkManager.IsServer;
    }

    private void ResolveReferences()
    {
        Transform root = transform.root != null ? transform.root : transform;

        if (motor == null)
            motor = root.GetComponentInChildren<HamsterFullRagdollMotor>(true);
        if (bodyRigidbody == null)
        {
            Transform motorShellBody = FindChildRecursive(root, "MotorShellBody");
            if (motorShellBody != null)
                bodyRigidbody = motorShellBody.GetComponent<Rigidbody>();
        }

        if (bodyTransform == null && bodyRigidbody != null)
            bodyTransform = bodyRigidbody.transform;
        if (visualFollower == null)
            visualFollower = root.GetComponentInChildren<HamsterVisualFollower>(true);
        if (rootBodySync == null)
            rootBodySync = root.GetComponentInChildren<MotorShellRootBodySync>(true);
        if (combatAdapter == null)
            combatAdapter = root.GetComponentInChildren<HamsterMotorShellCombatAdapter>(true);
    }

    private void CaptureOriginalConstraints()
    {
        if (_hasOriginalConstraints || bodyRigidbody == null)
            return;

        _originalConstraints = bodyRigidbody.constraints;
        _hasOriginalConstraints = true;
    }

    private void ReleaseRotationConstraints()
    {
        _lastReleasedConstraints = false;
        if (!releaseRotationConstraintsWhileKnocked || bodyRigidbody == null)
            return;

        CaptureOriginalConstraints();
        RigidbodyConstraints before = bodyRigidbody.constraints;
        bodyRigidbody.constraints &= ~(RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationY | RigidbodyConstraints.FreezeRotationZ);
        _lastReleasedConstraints = before != bodyRigidbody.constraints;
    }

    private void RestoreOriginalConstraints()
    {
        if (!restoreOriginalConstraintsOnRecover || !_hasOriginalConstraints || bodyRigidbody == null)
            return;

        bodyRigidbody.constraints = _originalConstraints;
    }

    private float GetPlanarSpeed()
    {
        if (bodyRigidbody == null)
            return 0f;

        Vector3 velocity = bodyRigidbody.linearVelocity;
        velocity.y = 0f;
        return velocity.magnitude;
    }

    private bool IsTargetRoot()
    {
        Transform root = transform.root != null ? transform.root : transform;
        return root != null && (root.name == TargetRootName || root.name == TargetRootName + "(Clone)");
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

    private static Vector3 SanitizeVector(Vector3 value)
    {
        return IsFiniteVector(value) ? value : Vector3.zero;
    }

    private static bool IsFiniteVector(Vector3 value)
    {
        return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
    }

    private static bool IsFinite(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }

    private void LogDebugState()
    {
        if (!debugRecoveryLogs || Time.unscaledTime < _nextDebugLogTime)
            return;

        _nextDebugLogTime = Time.unscaledTime + Mathf.Max(MinLogInterval, debugLogInterval);
        string bodyName = bodyRigidbody != null ? bodyRigidbody.name : "<null>";
        string constraints = bodyRigidbody != null ? bodyRigidbody.constraints.ToString() : "<null>";
        string velocity = bodyRigidbody != null ? FormatVector(bodyRigidbody.linearVelocity) : "<null>";
        Debug.Log(
            $"[MSRecovery:{name}] state={_state} source={_lastSource} timer={_stateTimer:F2} body={bodyName} constraints={constraints} velocity={velocity} motorLocked={(motor != null && motor.IsExternalControlLocked)} moveScale={(motor != null ? motor.ExternalMovementControlScale : 0f):F2} uprightScale={(motor != null ? motor.ExternalUprightControlScale : 0f):F2} poseScale={(motor != null ? motor.ExternalPoseControlScale : 0f):F2}",
            this);
    }

    private void LogImpactRequest(
        string source,
        Vector3 impulse,
        Vector3 adjustedImpulse,
        float magnitude,
        bool forceKnockdown,
        bool shouldKnockdown,
        RigidbodyConstraints originalConstraints,
        RigidbodyConstraints releasedConstraints,
        RecoveryState stateBefore,
        RecoveryState stateAfter,
        bool result,
        string failureReason)
    {
        if (!debugRecoveryLogs)
            return;

        string bodyState = bodyRigidbody != null
            ? $"bodyExists=True isKinematic={bodyRigidbody.isKinematic}"
            : "bodyExists=False isKinematic=<null>";
        Debug.Log(
            $"[MSRecovery/ImpactRequest] source={source} impulse={FormatVector(impulse)} adjustedImpulse={FormatVector(adjustedImpulse)} magnitude={magnitude:F2} forceKnockdown={forceKnockdown} shouldKnockdown={shouldKnockdown} {bodyState} originalConstraints={originalConstraints} releasedConstraints={releasedConstraints} releasedThisCall={_lastReleasedConstraints} stateBefore={stateBefore} stateAfter={stateAfter} result={result} reason={failureReason}",
            this);
    }

    private void LogLiquidRequest(
        string source,
        Vector3 direction,
        float force,
        float duration,
        RecoveryState stateBefore,
        RecoveryState stateAfter,
        bool result)
    {
        if (!debugRecoveryLogs)
            return;

        string bodyState = bodyRigidbody != null
            ? $"bodyExists=True isKinematic={bodyRigidbody.isKinematic}"
            : "bodyExists=False isKinematic=<null>";
        Debug.Log(
            $"[MSRecovery/LiquidRequest] source={source} direction={FormatVector(direction)} force={force:F2} duration={duration:F2} {bodyState} stateBefore={stateBefore} stateAfter={stateAfter} result={result}",
            this);
    }

    private void LogStateChange(RecoveryState previousState, RecoveryState nextState, string source)
    {
        if (!debugRecoveryLogs)
            return;

        Debug.Log(
            $"[MSRecovery/State] source={source} from={previousState} to={nextState} body={(bodyRigidbody != null ? bodyRigidbody.name : "<null>")} constraints={(bodyRigidbody != null ? bodyRigidbody.constraints.ToString() : "<null>")}",
            this);
    }

    private static string FormatVector(Vector3 value)
    {
        return $"({value.x:F2},{value.y:F2},{value.z:F2})";
    }
}
