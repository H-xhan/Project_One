using Unity.Netcode;
using UnityEngine;

public sealed class HamsterMotorShellRagdollRecoveryAdapter : MonoBehaviour
{
    private const string TargetRootName = "Hamster_JointFreeMotorShell_MainScenes";
    private const string RecoveryControlReasonPrefix = "RagdollRecovery:";
    private const float MinLogInterval = 0.05f;
    private const float MinStateDuration = 0.01f;

    public enum RecoveryState
    {
        Normal,
        Impacted,
        KnockedDown,
        PrepareGetUp,
        GettingUp,
        RecoveryBlendOut,
        Recovering,
        LiquidSwept
    }

    private enum DirectionalGetUpMode
    {
        AnchorOnly,
        Front,
        Back
    }

    [Header("References")]
    [SerializeField] private HamsterFullRagdollMotor motor;
    [SerializeField] private Rigidbody bodyRigidbody;
    [SerializeField] private Transform bodyTransform;
    [SerializeField] private HamsterVisualFollower visualFollower;
    [SerializeField] private MotorShellRootBodySync rootBodySync;
    [SerializeField] private HamsterMotorShellCombatAdapter combatAdapter;
    [SerializeField] private HamsterVisualClipStateDriver visualClipStateDriver;

    [Header("Recovery")]
    [SerializeField] private bool enableRecoveryAdapter = true;
    [SerializeField] private float impactImpulseMultiplier = 1.0f;
    [SerializeField] private float impactUpwardBonus = 0.5f;
    [SerializeField] private float impactTorqueMultiplier = 1.2f;
    [SerializeField] private float minKnockdownImpulse = 3.0f;
    [SerializeField] private float impactedDuration = 0.22f;
    [SerializeField] private float minimumKnockdownDuration = 0.9f;
    [SerializeField] private bool useProductionLikeLaunch = true;
    [SerializeField] private bool useVelocityChangeForImpact = true;
    [SerializeField] private float productionHorizontalLaunchScale = 0.65f;
    [SerializeField] private float productionMinimumUpwardLaunch = 2.8f;
    [SerializeField] private float productionUpwardLaunchBonus = 1.1f;
    [SerializeField] private float productionTumbleTorque = 14f;
    [SerializeField] private float productionRandomYawTorque = 4f;
    [SerializeField] private float knockdownDuration = 1.2f;
    [SerializeField] private float recoveryDuration = 1.2f;
    [SerializeField] private float maxKnockdownDuration = 2.4f;
    [SerializeField] private bool disableMotorControlWhileKnocked = true;
    [SerializeField] private bool releaseRotationConstraintsWhileKnocked = true;
    [SerializeField] private bool restoreOriginalConstraintsOnRecover = true;
    [SerializeField] private float recoveryUprightTorque = 30f;
    [SerializeField] private float recoveryDamping = 5f;
    [SerializeField] private float recoveryMovementStartScale = 0.04f;
    [SerializeField] private float recoveryUprightStartScale = 0.08f;
    [SerializeField] private float recoveryPoseStartScale = 0.03f;
    [SerializeField] private float recoveryUprightTorqueStartScale = 0.1f;
    [SerializeField] private bool groundedRequiredToFinishRecovery = true;
    [SerializeField] private float maxRecoverPlanarSpeed = 2.0f;
    [SerializeField] private float minRecoverUpDot = 0.85f;
    [SerializeField] private bool dampAngularVelocityDuringRecovery = true;
    [SerializeField] private float recoveryAngularDamping = 14f;
    [SerializeField] private float recoveryYawAngularDamping = 20f;
    [SerializeField] private float maxRecoverAngularSpeed = 1.0f;
    [SerializeField] private float maxRecoverYawAngularSpeed = 0.35f;
    [SerializeField] private float maxExitYawAngularVelocity = 0.05f;
    [SerializeField] private bool zeroAngularVelocityOnRecoveryFinish = true;

    [Header("Liquid Sweep")]
    [SerializeField] private float liquidSweepControlMultiplier = 0.15f;
    [SerializeField] private float liquidSweepDuration = 1.2f;
    [SerializeField] private float liquidSweepForceMultiplier = 1.55f;
    [SerializeField] private float liquidSweepInitialImpulseMultiplier = 0.55f;
    [SerializeField] private bool liquidSweepAllowJump = false;

    [Header("Get Up Animation")]
    [SerializeField] private bool useGetUpAnimation = true;
    [SerializeField] private bool useGetUpRecoveryPhase = true;
    [SerializeField] private bool useHardGetUpPhysicsIsolation = true;
    [SerializeField] private bool getUpKinematicBody = true;
    [SerializeField] private bool getUpDisableGravity = true;
    [SerializeField] private bool getUpDisableBodyCollisions = false;
    [SerializeField] private bool getUpFreezeAllRotation = true;
    [SerializeField] private bool alignBodyUprightOnGetUpStart = true;
    [SerializeField] private bool alignBodyUprightOnGetUpFinish = true;
    [SerializeField] private bool resetBodyLocalPositionOnGetUp = true;
    [SerializeField] private string getUpStateName = "Legacy_GetUp_DISABLED";
    [SerializeField] private bool useImpactDirectionForGetUp = true;
    [SerializeField] private string getUpFrontStateName = "GetUp_Front";
    [SerializeField] private string getUpBackStateName = "GetUp_Back";
    [SerializeField] private float impactDirectionDotThreshold = 0.3f;
    [SerializeField] private bool invertImpactFrontBack = false;
    [SerializeField] private bool fallbackToAnchorOnlyWhenAmbiguous = true;
    [SerializeField] private bool fallbackToAnchorOnlyWhenNoDirection = true;
    [SerializeField] private bool driveGetUpStateOnlyWhenMotionAssigned = true;
    [SerializeField] private float getUpCrossFadeDuration = 0.08f;
    [SerializeField] private float minGetUpStateTime = 0.4f;
    [SerializeField] private float maxGetUpStateTime = 1.2f;
    [SerializeField] private float prepareGetUpDuration = 0.10f;
    [SerializeField] private float getUpBlendOutDuration = 0.25f;
    [SerializeField] private bool suppressVisualDuringGetUp = true;
    [SerializeField] private bool freezeRotationDuringGetUp = true;
    [SerializeField] private bool zeroAngularVelocityOnGetUpStart = true;
    [SerializeField] private bool zeroAngularVelocityOnGetUpFinish = true;
    [SerializeField] private bool disableUprightTorqueDuringGetUp = true;
    [SerializeField] private float getUpMovementScale = 0f;
    [SerializeField] private float getUpUprightScale = 0f;
    [SerializeField] private float getUpPoseScale = 0f;
    [SerializeField] private bool blendOutJumpUnlockAtEnd = true;
    [SerializeField] private bool returnToLocomotionAfterGetUp = true;
    [SerializeField] private bool debugImpactGetUpLogs = false;
    [SerializeField] private bool debugGetUpPlayLogs = false;
    [SerializeField] private bool debugGetUpLogs = false;

    [Header("Debug")]
    [SerializeField] private bool debugRecoveryLogs = false;
    [SerializeField] private float debugLogInterval = 0.5f;

    private RecoveryState _state = RecoveryState.Normal;
    private RigidbodyConstraints _originalConstraints;
    private bool _hasOriginalConstraints;
    private bool _hasOriginalBodyPhysicsState;
    private bool _originalBodyIsKinematic;
    private bool _originalBodyUseGravity;
    private bool _originalBodyDetectCollisions;
    private RigidbodyConstraints _originalBodyConstraints;
    private RigidbodyInterpolation _originalBodyInterpolation;
    private CollisionDetectionMode _originalBodyCollisionDetectionMode;
    private bool _hardGetUpPhysicsIsolationActive;
    private float _stateTimer;
    private float _activeKnockdownDuration;
    private float _activeRecoveryDuration;
    private float _activeLiquidSweepDuration;
    private Vector3 _liquidSweepDirection;
    private float _liquidSweepForce;
    private string _lastSource = "none";
    private float _nextDebugLogTime;
    private bool _lastReleasedConstraints;
    private bool _getUpAnimationActive;
    private int _getUpStateHash;
    private float _getUpStateTimer;
    private Vector3 _lastImpactPlanarDirection;
    private Vector3 _preImpactFacingForward;
    private bool _hasLastImpactPlanarDirection;
    private bool _hasPreImpactFacingForward;
    private string _activeGetUpStateName;

    public RecoveryState CurrentRecoveryState => _state;
    public bool IsKnockedOrRecovering => _state == RecoveryState.Impacted ||
                                          _state == RecoveryState.KnockedDown ||
                                          _state == RecoveryState.PrepareGetUp ||
                                          _state == RecoveryState.GettingUp ||
                                          _state == RecoveryState.RecoveryBlendOut ||
                                          _state == RecoveryState.Recovering;
    public bool IsLiquidSwept => _state == RecoveryState.LiquidSwept;
    public bool IsControlLockedByRecovery => IsKnockedOrRecovering || (_state == RecoveryState.LiquidSwept && !liquidSweepAllowJump);
    public bool ShouldVisualFollowBodyRotation => IsTargetRoot() && (_state == RecoveryState.Impacted || _state == RecoveryState.KnockedDown);
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
        CaptureOriginalBodyPhysicsState();
    }

    private void OnEnable()
    {
        ResolveReferences();
        CaptureOriginalConstraints();
        CaptureOriginalBodyPhysicsState();
    }

    private void OnDisable()
    {
        ResetGetUpAnimationState("Disable");
        SetGetUpVisualLock(false, "Disable");
        SetRecoveryVisualSuppression(false, "Disable");
        RestoreOriginalBodyPhysicsState();
        RestoreOriginalConstraints();
        CompleteRecoveryStateCleanup("Disable");
    }

    private void OnDestroy()
    {
        ResetGetUpAnimationState("Destroy");
        SetGetUpVisualLock(false, "Destroy");
        SetRecoveryVisualSuppression(false, "Destroy");
        RestoreOriginalBodyPhysicsState();
        RestoreOriginalConstraints();
        CompleteRecoveryStateCleanup("Destroy");
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
            case RecoveryState.PrepareGetUp:
                TickPrepareGetUp(fixedDeltaTime);
                break;
            case RecoveryState.GettingUp:
                TickGettingUp(fixedDeltaTime);
                break;
            case RecoveryState.RecoveryBlendOut:
                TickRecoveryBlendOut(fixedDeltaTime);
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
        RestoreOriginalBodyPhysicsState();
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
            CaptureImpactGetUpContext(safeImpulse);
            BeginImpactStateBeforeImpulse(source, duration);
            releasedConstraints = bodyRigidbody.constraints;
        }
        else
        {
            ClearImpactGetUpContext("ImpactNoKnockdown");
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
        RestoreOriginalBodyPhysicsState();
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
        ClearImpactGetUpContext("LiquidSweep");
        ResetGetUpAnimationState("LiquidSweep");
        SetGetUpVisualLock(false, "LiquidSweep");
        SetRecoveryVisualSuppression(false, "LiquidSweep");
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
        if (_stateTimer >= Mathf.Max(MinStateDuration, impactedDuration))
            EnterKnockedDown();
    }

    private void TickKnockedDown(float fixedDeltaTime)
    {
        _stateTimer += fixedDeltaTime;
        ApplyKnockedMotorControl();

        if (_stateTimer < _activeKnockdownDuration)
            return;

        bool groundedOk = !groundedRequiredToFinishRecovery || motor == null || motor.IsGrounded;
        bool speedOk = GetPlanarSpeed() <= Mathf.Max(0f, maxRecoverPlanarSpeed);
        bool failSafe = _stateTimer >= Mathf.Max(_activeKnockdownDuration, Mathf.Max(MinStateDuration, maxKnockdownDuration));
        if (groundedOk || speedOk || failSafe)
            EnterPostKnockdownRecovery();
    }

    private void TickPrepareGetUp(float fixedDeltaTime)
    {
        _stateTimer += fixedDeltaTime;
        ApplyGetUpMotorControl("PrepareGetUp");
        if (_hardGetUpPhysicsIsolationActive)
            MaintainHardGetUpPhysicsIsolation("PrepareGetUp");
        else
            DampenAngularVelocityDuringRecovery(0.65f);

        float prepareDuration = Mathf.Max(MinStateDuration, prepareGetUpDuration);
        if (_stateTimer < prepareDuration)
            return;

        if (!IsReadyToAttemptDirectionalGetUpAnimation())
        {
            EnterRecovering();
            return;
        }

        if (TryStartGetUpAnimation())
            EnterGettingUp();
        else
            EnterRecovering();
    }

    private void TickGettingUp(float fixedDeltaTime)
    {
        _stateTimer += fixedDeltaTime;
        ApplyGetUpMotorControl("GettingUp");
        if (_hardGetUpPhysicsIsolationActive)
            MaintainHardGetUpPhysicsIsolation("GettingUp");
        else
            DampenAngularVelocityDuringRecovery(1f);
        if (!disableUprightTorqueDuringGetUp)
            ApplyRecoveryUprightTorque(0f);

        bool getUpBlocking = IsGetUpAnimationBlockingRecovery(fixedDeltaTime);
        bool minTimeFinished = _stateTimer >= Mathf.Max(0f, minGetUpStateTime);
        if (!getUpBlocking && minTimeFinished)
            EnterRecoveryBlendOut();
    }

    private void TickRecoveryBlendOut(float fixedDeltaTime)
    {
        _stateTimer += fixedDeltaTime;
        float duration = Mathf.Max(MinStateDuration, getUpBlendOutDuration);
        float blend01 = EaseRecovery01(Mathf.Clamp01(_stateTimer / duration));
        ApplyRecoveryBlendOutMotorControl(blend01);
        if (_hardGetUpPhysicsIsolationActive)
            MaintainHardGetUpPhysicsIsolation("RecoveryBlendOut");
        else
            DampenAngularVelocityDuringRecovery(1f);

        if (_stateTimer >= duration)
            EnterNormal();
    }

    private void TickRecovering(float fixedDeltaTime)
    {
        _stateTimer += fixedDeltaTime;
        float duration = Mathf.Max(0.01f, _activeRecoveryDuration);
        float recovery01 = Mathf.Clamp01(_stateTimer / duration);

        float easedRecovery01 = EaseRecovery01(recovery01);
        ApplyRecoveryMotorControl(easedRecovery01);
        ApplyRecoveryUprightTorque(easedRecovery01);
        DampenAngularVelocityDuringRecovery(easedRecovery01);

        bool durationFinished = _stateTimer >= duration;
        bool groundedOk = !groundedRequiredToFinishRecovery || motor == null || motor.IsGrounded;
        bool speedOk = GetPlanarSpeed() <= Mathf.Max(0f, maxRecoverPlanarSpeed);
        bool uprightOk = bodyTransform == null || Vector3.Dot(bodyTransform.up, Vector3.up) >= Mathf.Clamp(minRecoverUpDot, -1f, 1f);
        bool angularOk = IsRecoveryAngularVelocityOk();
        bool getUpOk = !IsGetUpAnimationBlockingRecovery(fixedDeltaTime);
        bool failSafeFinished = _stateTimer >= Mathf.Max(duration, maxKnockdownDuration);
        if ((durationFinished && groundedOk && speedOk && uprightOk && angularOk && getUpOk) || failSafeFinished)
            EnterRecoveryBlendOut();
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
        _activeKnockdownDuration = ResolveKnockdownDuration(duration);
        _activeRecoveryDuration = Mathf.Max(MinStateDuration, recoveryDuration);
        _lastSource = string.IsNullOrEmpty(source) ? "Impact" : source;
        ResetGetUpAnimationState("Impact");
        SetGetUpVisualLock(false, "Impact");
        SetRecoveryVisualSuppression(false, "Impact");
        RestoreOriginalBodyPhysicsState();
        CaptureOriginalConstraints();
        ReleaseRotationConstraints();
        ApplyKnockedMotorControl();
        bodyRigidbody?.WakeUp();
    }

    private void BeginImpactStateBeforeImpulse(string source, float duration)
    {
        SetState(RecoveryState.Impacted, source);
        _stateTimer = 0f;
        _activeKnockdownDuration = ResolveKnockdownDuration(duration);
        _activeRecoveryDuration = Mathf.Max(MinStateDuration, recoveryDuration);
        _lastSource = string.IsNullOrEmpty(source) ? "Impact" : source;
        ResetGetUpAnimationState("Impact");
        SetGetUpVisualLock(false, "Impact");
        SetRecoveryVisualSuppression(false, "Impact");
        RestoreOriginalBodyPhysicsState();
        CaptureOriginalConstraints();
        ReleaseRotationConstraints();
        ApplyKnockedMotorControl();
        bodyRigidbody?.WakeUp();
    }

    private void EnterKnockedDown()
    {
        SetState(RecoveryState.KnockedDown, _lastSource);
        _stateTimer = 0f;
        SetGetUpVisualLock(false, "KnockedDown");
        SetRecoveryVisualSuppression(false, "KnockedDown");
        ReleaseRotationConstraints();
        ApplyKnockedMotorControl();
        bodyRigidbody?.WakeUp();
    }

    private void EnterPostKnockdownRecovery()
    {
        if (useGetUpRecoveryPhase)
            EnterPrepareGetUp();
        else
            EnterRecovering();
    }

    private void EnterPrepareGetUp()
    {
        SetState(RecoveryState.PrepareGetUp, _lastSource);
        _stateTimer = 0f;
        SetRecoveryVisualSuppression(true, "PrepareGetUp");
        SetGetUpVisualLock(useHardGetUpPhysicsIsolation, "PrepareGetUp");
        if (useHardGetUpPhysicsIsolation)
            ApplyHardGetUpPhysicsIsolation("PrepareGetUp", alignBodyUprightOnGetUpStart, zeroAngularVelocityOnGetUpStart);
        else
        {
            ApplyGetUpRotationConstraints();
            if (zeroAngularVelocityOnGetUpStart)
                StabilizeAngularVelocityForRecoveryBoundary(true);
        }
        ApplyGetUpMotorControl("PrepareGetUp");
        if (!_hardGetUpPhysicsIsolationActive)
            bodyRigidbody?.WakeUp();
    }

    private void EnterGettingUp()
    {
        SetState(RecoveryState.GettingUp, _lastSource);
        _stateTimer = 0f;
        SetRecoveryVisualSuppression(true, "GettingUp");
        SetGetUpVisualLock(useHardGetUpPhysicsIsolation, "GettingUp");
        if (useHardGetUpPhysicsIsolation)
            ApplyHardGetUpPhysicsIsolation("GettingUp", alignBodyUprightOnGetUpStart, zeroAngularVelocityOnGetUpStart);
        else
        {
            ApplyGetUpRotationConstraints();
            if (zeroAngularVelocityOnGetUpStart)
                StabilizeAngularVelocityForRecoveryBoundary(true);
        }
        ApplyGetUpMotorControl("GettingUp");
        if (!_hardGetUpPhysicsIsolationActive)
            bodyRigidbody?.WakeUp();
    }

    private void EnterRecovering()
    {
        SetState(RecoveryState.Recovering, _lastSource);
        _stateTimer = 0f;
        SetGetUpVisualLock(false, "FallbackRecovering");
        RestoreOriginalBodyPhysicsState();
        SetRecoveryVisualSuppression(true, "FallbackRecovering");
        ReleaseRotationConstraints();
        ApplyRecoveryMotorControl(0f);
        bodyRigidbody?.WakeUp();
    }

    private void EnterRecoveryBlendOut()
    {
        bool hardGetUpBlendOut = _hardGetUpPhysicsIsolationActive || _getUpAnimationActive;
        SetState(RecoveryState.RecoveryBlendOut, _lastSource);
        _stateTimer = 0f;
        ResetGetUpAnimationState("BlendOut");
        SetRecoveryVisualSuppression(true, "RecoveryBlendOut");
        SetGetUpVisualLock(hardGetUpBlendOut && useHardGetUpPhysicsIsolation, "RecoveryBlendOut");
        if (hardGetUpBlendOut && useHardGetUpPhysicsIsolation)
            ApplyHardGetUpPhysicsIsolation("RecoveryBlendOut", alignBodyUprightOnGetUpFinish, zeroAngularVelocityOnGetUpFinish);
        else
        {
            if (zeroAngularVelocityOnGetUpFinish)
                StabilizeAngularVelocityForRecoveryBoundary(true);
            ApplyGetUpRotationConstraints();
        }
        ApplyRecoveryBlendOutMotorControl(0f);
        if (!_hardGetUpPhysicsIsolationActive)
            bodyRigidbody?.WakeUp();
    }

    private void EnterNormal()
    {
        SetState(RecoveryState.Normal, _lastSource);
        _stateTimer = 0f;
        ResetGetUpAnimationState("EnterNormal");
        SetGetUpVisualLock(false, "Normal");
        SetRecoveryVisualSuppression(false, "Normal");
        _liquidSweepDirection = Vector3.zero;
        _liquidSweepForce = 0f;
        ClearImpactGetUpContext("Normal");
        if (_hardGetUpPhysicsIsolationActive && alignBodyUprightOnGetUpFinish)
            StabilizeBodyPoseForGetUpBoundary("EnterNormal", true, zeroAngularVelocityOnGetUpFinish);
        RestoreOriginalBodyPhysicsState();
        StabilizeAngularVelocityForNormal();
        RestoreOriginalConstraints();
        CompleteRecoveryStateCleanup("EnterNormal");
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
        if (bodyRigidbody.isKinematic)
            return;

        float startScale = Mathf.Clamp01(recoveryUprightTorqueStartScale);
        float strength = Mathf.Max(0f, recoveryUprightTorque) * Mathf.Lerp(startScale, 1f, recovery01);
        if (strength <= 0f)
            return;

        Vector3 correctionAxis = Vector3.Cross(bodyTransform.up, Vector3.up);
        Vector3 dampingTorque = -bodyRigidbody.angularVelocity * Mathf.Max(0f, recoveryDamping);
        Vector3 torque = correctionAxis * strength + dampingTorque;
        if (IsFiniteVector(torque) && torque.sqrMagnitude > 0.0001f)
            bodyRigidbody.AddTorque(torque, ForceMode.Acceleration);
    }

    private void DampenAngularVelocityDuringRecovery(float recovery01)
    {
        if (!dampAngularVelocityDuringRecovery || bodyRigidbody == null)
            return;
        if (bodyRigidbody.isKinematic)
            return;

        Vector3 currentAngular = bodyRigidbody.angularVelocity;
        if (!IsFiniteVector(currentAngular))
            return;

        float recoveryScale = Mathf.Lerp(0.35f, 1f, Mathf.Clamp01(recovery01));
        float angularDamping = Mathf.Max(0f, recoveryAngularDamping);
        float yawDamping = Mathf.Max(angularDamping, recoveryYawAngularDamping);
        Vector3 dampingTorque = new Vector3(
            -currentAngular.x * angularDamping,
            -currentAngular.y * yawDamping,
            -currentAngular.z * angularDamping) * recoveryScale;

        if (IsFiniteVector(dampingTorque) && dampingTorque.sqrMagnitude > 0.0001f)
            bodyRigidbody.AddTorque(dampingTorque, ForceMode.Acceleration);
    }

    private bool IsRecoveryAngularVelocityOk()
    {
        if (bodyRigidbody == null)
            return true;

        Vector3 currentAngular = bodyRigidbody.angularVelocity;
        if (!IsFiniteVector(currentAngular))
            return false;

        bool angularOk = currentAngular.magnitude <= Mathf.Max(0f, maxRecoverAngularSpeed);
        bool yawAngularOk = Mathf.Abs(currentAngular.y) <= Mathf.Max(0f, maxRecoverYawAngularSpeed);
        return angularOk && yawAngularOk;
    }

    private void StabilizeAngularVelocityForNormal()
    {
        StabilizeAngularVelocityForRecoveryBoundary(zeroAngularVelocityOnRecoveryFinish);
    }

    private void StabilizeAngularVelocityForRecoveryBoundary(bool zeroYaw)
    {
        if (bodyRigidbody == null)
            return;
        if (bodyRigidbody.isKinematic)
            return;

        Vector3 currentAngular = bodyRigidbody.angularVelocity;
        bool currentAngularIsFinite = IsFiniteVector(currentAngular);
        if (!currentAngularIsFinite)
            currentAngular = Vector3.zero;

        float exitYawLimit = Mathf.Max(0f, maxExitYawAngularVelocity);
        float yawVelocity = zeroYaw
            ? 0f
            : Mathf.Clamp(currentAngular.y, -exitYawLimit, exitYawLimit);
        Vector3 stabilizedAngularVelocity = new Vector3(0f, yawVelocity, 0f);
        if (currentAngularIsFinite && (currentAngular - stabilizedAngularVelocity).sqrMagnitude <= 0.000001f)
            return;

        // get-up pose stabilization only, not movement control.
        bodyRigidbody.angularVelocity = stabilizedAngularVelocity;
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
        motor.SetExternalMovementControlScale(Mathf.Lerp(Mathf.Clamp01(recoveryMovementStartScale), 1f, recovery01), "RagdollRecovery:Recovering");
        motor.SetExternalUprightControlScale(Mathf.Lerp(Mathf.Clamp01(recoveryUprightStartScale), 1f, recovery01), "RagdollRecovery:Recovering");
        motor.SetExternalPoseControlScale(Mathf.Lerp(Mathf.Clamp01(recoveryPoseStartScale), 1f, recovery01), "RagdollRecovery:Recovering");
    }

    private void ApplyGetUpMotorControl(string phase)
    {
        if (motor == null)
            return;

        string reason = "RagdollRecovery:" + phase;
        motor.SetExternalControlLock(false, reason);
        motor.SetExternalJumpLock(true, reason);
        motor.SetExternalMovementControlScale(Mathf.Clamp01(getUpMovementScale), reason);
        motor.SetExternalUprightControlScale(Mathf.Clamp01(getUpUprightScale), reason);
        motor.SetExternalPoseControlScale(Mathf.Clamp01(getUpPoseScale), reason);
    }

    private void ApplyRecoveryBlendOutMotorControl(float blend01)
    {
        if (motor == null)
            return;

        float t = Mathf.Clamp01(blend01);
        string reason = "RagdollRecovery:RecoveryBlendOut";
        motor.SetExternalControlLock(false, reason);
        motor.SetExternalJumpLock(blendOutJumpUnlockAtEnd && t < 1f, reason);
        motor.SetExternalMovementControlScale(Mathf.Lerp(Mathf.Clamp01(getUpMovementScale), 1f, t), reason);
        motor.SetExternalUprightControlScale(Mathf.Lerp(Mathf.Clamp01(getUpUprightScale), 1f, t), reason);
        motor.SetExternalPoseControlScale(Mathf.Lerp(Mathf.Clamp01(getUpPoseScale), 1f, t), reason);
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

        if (IsRecoveryControlReason(motor.ExternalControlLockReason))
            motor.SetExternalControlLock(false, null);
        if (IsRecoveryControlReason(motor.ExternalJumpLockReason))
            motor.SetExternalJumpLock(false, null);
        if (IsRecoveryControlReason(motor.ExternalMovementControlReason))
            motor.SetExternalMovementControlScale(1f, null);
        if (IsRecoveryControlReason(motor.ExternalUprightControlReason))
            motor.SetExternalUprightControlScale(1f, null);
        if (IsRecoveryControlReason(motor.ExternalPoseControlReason))
            motor.SetExternalPoseControlScale(1f, null);
    }

    private void CompleteRecoveryStateCleanup(string reason)
    {
        RestoreNormalMotorControl();
        motor?.ClearRecoveryJumpState(reason);
    }

    private static bool IsRecoveryControlReason(string reason)
    {
        return !string.IsNullOrEmpty(reason) &&
               reason.StartsWith(RecoveryControlReasonPrefix, System.StringComparison.Ordinal);
    }

    private bool CanApplyStateChange()
    {
        if (!enableRecoveryAdapter || !IsTargetRoot())
            return false;

        NetworkManager networkManager = NetworkManager.Singleton;
        return networkManager == null || !networkManager.IsListening || networkManager.IsServer;
    }

    private float ResolveKnockdownDuration(float requestedDuration)
    {
        float requested = Mathf.Max(MinStateDuration, requestedDuration);
        float minimum = Mathf.Max(MinStateDuration, minimumKnockdownDuration);
        float maximum = Mathf.Max(minimum, maxKnockdownDuration);
        return Mathf.Min(Mathf.Max(requested, minimum), maximum);
    }

    private static float EaseRecovery01(float value)
    {
        value = Mathf.Clamp01(value);
        return value * value * (3f - 2f * value);
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
        if (visualClipStateDriver == null)
            visualClipStateDriver = root.GetComponentInChildren<HamsterVisualClipStateDriver>(true);
    }

    private void CaptureOriginalBodyPhysicsState()
    {
        if (_hasOriginalBodyPhysicsState || bodyRigidbody == null)
            return;

        _originalBodyIsKinematic = bodyRigidbody.isKinematic;
        _originalBodyUseGravity = bodyRigidbody.useGravity;
        _originalBodyDetectCollisions = bodyRigidbody.detectCollisions;
        _originalBodyConstraints = _hasOriginalConstraints ? _originalConstraints : bodyRigidbody.constraints;
        _originalBodyInterpolation = bodyRigidbody.interpolation;
        _originalBodyCollisionDetectionMode = bodyRigidbody.collisionDetectionMode;
        _hasOriginalBodyPhysicsState = true;
    }

    private void ApplyHardGetUpPhysicsIsolation(string phase, bool alignUpright, bool zeroAngular)
    {
        if (!useHardGetUpPhysicsIsolation || bodyRigidbody == null)
            return;

        CaptureOriginalConstraints();
        CaptureOriginalBodyPhysicsState();
        StabilizeBodyPoseForGetUpBoundary(phase, alignUpright, zeroAngular);

        _hardGetUpPhysicsIsolationActive = true;

        // get-up pose stabilization only, not movement control.
        if (getUpKinematicBody)
            bodyRigidbody.isKinematic = true;
        if (getUpDisableGravity)
            bodyRigidbody.useGravity = false;
        if (getUpDisableBodyCollisions)
            bodyRigidbody.detectCollisions = false;
        if (getUpFreezeAllRotation || freezeRotationDuringGetUp)
        {
            bodyRigidbody.constraints |= RigidbodyConstraints.FreezeRotationX |
                                         RigidbodyConstraints.FreezeRotationY |
                                         RigidbodyConstraints.FreezeRotationZ;
        }

        bodyRigidbody.Sleep();
        LogGetUp($"physicsIsolation phase={phase} kinematic={bodyRigidbody.isKinematic} gravity={bodyRigidbody.useGravity} collisions={bodyRigidbody.detectCollisions} constraints={bodyRigidbody.constraints}");
    }

    private void MaintainHardGetUpPhysicsIsolation(string phase)
    {
        if (!_hardGetUpPhysicsIsolationActive || bodyRigidbody == null)
            return;

        // get-up pose stabilization only, not movement control.
        if (getUpKinematicBody && !bodyRigidbody.isKinematic)
            bodyRigidbody.isKinematic = true;
        if (getUpDisableGravity && bodyRigidbody.useGravity)
            bodyRigidbody.useGravity = false;
        if (getUpDisableBodyCollisions && bodyRigidbody.detectCollisions)
            bodyRigidbody.detectCollisions = false;
        if (getUpFreezeAllRotation || freezeRotationDuringGetUp)
        {
            bodyRigidbody.constraints |= RigidbodyConstraints.FreezeRotationX |
                                         RigidbodyConstraints.FreezeRotationY |
                                         RigidbodyConstraints.FreezeRotationZ;
        }

        bodyRigidbody.Sleep();
        LogGetUp($"physicsIsolationMaintain phase={phase} kinematic={bodyRigidbody.isKinematic} constraints={bodyRigidbody.constraints}");
    }

    private void RestoreOriginalBodyPhysicsState()
    {
        if (!_hardGetUpPhysicsIsolationActive || !_hasOriginalBodyPhysicsState || bodyRigidbody == null)
            return;

        // get-up pose stabilization only, not movement control.
        bodyRigidbody.isKinematic = _originalBodyIsKinematic;
        bodyRigidbody.useGravity = _originalBodyUseGravity;
        bodyRigidbody.detectCollisions = _originalBodyDetectCollisions;
        bodyRigidbody.interpolation = _originalBodyInterpolation;
        bodyRigidbody.collisionDetectionMode = _originalBodyCollisionDetectionMode;
        bodyRigidbody.constraints = _originalBodyConstraints;
        _hardGetUpPhysicsIsolationActive = false;

        if (!bodyRigidbody.isKinematic)
            bodyRigidbody.WakeUp();

        LogGetUp($"physicsIsolationRestore kinematic={bodyRigidbody.isKinematic} gravity={bodyRigidbody.useGravity} collisions={bodyRigidbody.detectCollisions} constraints={bodyRigidbody.constraints}");
    }

    private void StabilizeBodyPoseForGetUpBoundary(string phase, bool alignUpright, bool zeroAngular)
    {
        if (bodyRigidbody == null)
            return;

        if (bodyTransform == null)
            bodyTransform = bodyRigidbody.transform;

        // get-up pose stabilization only, not movement control.
        if (zeroAngular && !bodyRigidbody.isKinematic)
            bodyRigidbody.angularVelocity = Vector3.zero;
        if (resetBodyLocalPositionOnGetUp && bodyTransform != null)
            bodyTransform.localPosition = Vector3.zero;
        if (alignUpright && TryResolveStableGetUpRotation(out Quaternion stableRotation))
        {
            bodyRigidbody.rotation = stableRotation;
            if (bodyTransform != null)
                bodyTransform.rotation = stableRotation;
        }

        LogGetUp($"poseStabilize phase={phase} align={alignUpright} resetLocal={resetBodyLocalPositionOnGetUp} zeroAngular={zeroAngular}");
    }

    private bool TryResolveStableGetUpRotation(out Quaternion stableRotation)
    {
        Transform root = transform.root != null ? transform.root : transform;
        Vector3 forward = root != null ? Vector3.ProjectOnPlane(root.forward, Vector3.up) : Vector3.zero;
        if (forward.sqrMagnitude <= 0.0001f && bodyTransform != null)
            forward = Vector3.ProjectOnPlane(bodyTransform.forward, Vector3.up);
        if (forward.sqrMagnitude <= 0.0001f)
            forward = Vector3.forward;

        stableRotation = Quaternion.LookRotation(forward.normalized, Vector3.up);
        return IsFiniteQuaternion(stableRotation);
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

    private void ApplyGetUpRotationConstraints()
    {
        if ((!freezeRotationDuringGetUp && !getUpFreezeAllRotation) || bodyRigidbody == null)
            return;

        CaptureOriginalConstraints();
        bodyRigidbody.constraints |= RigidbodyConstraints.FreezeRotationX |
                                     RigidbodyConstraints.FreezeRotationY |
                                     RigidbodyConstraints.FreezeRotationZ;
    }

    private bool IsReadyToStartGetUp()
    {
        bool groundedOk = !groundedRequiredToFinishRecovery || motor == null || motor.IsGrounded;
        bool speedOk = GetPlanarSpeed() <= Mathf.Max(0f, maxRecoverPlanarSpeed);
        bool uprightOk = bodyTransform == null || Vector3.Dot(bodyTransform.up, Vector3.up) >= Mathf.Clamp(minRecoverUpDot, -1f, 1f);
        return groundedOk && speedOk && uprightOk && IsRecoveryAngularVelocityOk();
    }

    private bool IsReadyToAttemptDirectionalGetUpAnimation()
    {
        bool groundedOk = !groundedRequiredToFinishRecovery || motor == null || motor.IsGrounded;
        bool speedOk = GetPlanarSpeed() <= Mathf.Max(0f, maxRecoverPlanarSpeed);
        return groundedOk && speedOk && IsRecoveryAngularVelocityOk();
    }

    private void SetRecoveryVisualSuppression(bool suppress, string reason)
    {
        if (!suppressVisualDuringGetUp && suppress)
            return;

        ResolveReferences();
        if (visualFollower == null)
            return;

        visualFollower.SetRecoveryVisualSuppression(suppress, reason);
    }

    private void SetGetUpVisualLock(bool locked, string reason)
    {
        ResolveReferences();
        if (visualFollower == null)
            return;

        visualFollower.SetGetUpVisualLock(locked, reason);
    }

    private bool TryStartGetUpAnimation()
    {
        ResetGetUpAnimationState("TryStart");
        if (!useGetUpAnimation)
        {
            LogGetUp($"anchorOnly reason=disabled legacyState={getUpStateName}");
            return false;
        }

        ResolveReferences();
        if (visualClipStateDriver == null)
        {
            LogGetUp("anchorOnly reason=driver missing");
            return false;
        }

        DirectionalGetUpMode mode = SelectDirectionalGetUpMode(out string selectedStateName, out string selectionFailureReason);
        if (mode == DirectionalGetUpMode.AnchorOnly)
        {
            LogGetUp($"anchorOnly reason={selectionFailureReason}");
            return false;
        }

        if (!visualClipStateDriver.CanPlayOneShotState(
                selectedStateName,
                driveGetUpStateOnlyWhenMotionAssigned,
                out int selectedStateHash,
                out string failureReason))
        {
            LogGetUp($"anchorOnly reason={failureReason} state={selectedStateName}");
            return false;
        }

        if (!visualClipStateDriver.TryPlayOneShotState(
                selectedStateName,
                getUpCrossFadeDuration,
                minGetUpStateTime,
                maxGetUpStateTime,
                driveGetUpStateOnlyWhenMotionAssigned,
                out _getUpStateHash,
                out failureReason))
        {
            LogGetUp($"anchorOnly reason={failureReason} state={selectedStateName}");
            return false;
        }

        _getUpAnimationActive = true;
        _getUpStateTimer = 0f;
        _activeGetUpStateName = selectedStateName;
        LogGetUp($"play mode={mode} state={selectedStateName} hash={_getUpStateHash} precheckHash={selectedStateHash} requireMotion={driveGetUpStateOnlyWhenMotionAssigned}");
        return true;
    }

    private DirectionalGetUpMode SelectDirectionalGetUpMode(out string stateName, out string failureReason)
    {
        stateName = null;
        failureReason = "none";

        if (!useImpactDirectionForGetUp)
        {
            failureReason = "impact direction disabled";
            return DirectionalGetUpMode.AnchorOnly;
        }

        if (!_hasLastImpactPlanarDirection)
        {
            failureReason = fallbackToAnchorOnlyWhenNoDirection ? "impact direction missing" : "impact direction missing fallback disabled";
            LogImpactGetUp($"select anchorOnly reason={failureReason}");
            return DirectionalGetUpMode.AnchorOnly;
        }

        if (!_hasPreImpactFacingForward)
        {
            failureReason = fallbackToAnchorOnlyWhenNoDirection ? "pre-impact facing missing" : "pre-impact facing missing fallback disabled";
            LogImpactGetUp($"select anchorOnly reason={failureReason}");
            return DirectionalGetUpMode.AnchorOnly;
        }

        float frontBackThreshold = Mathf.Clamp01(impactDirectionDotThreshold);
        float dot = Vector3.Dot(_lastImpactPlanarDirection, _preImpactFacingForward);
        if (Mathf.Abs(dot) <= frontBackThreshold)
        {
            failureReason = fallbackToAnchorOnlyWhenAmbiguous ? "impact direction ambiguous" : "impact direction ambiguous fallback disabled";
            LogImpactGetUp($"select anchorOnly reason={failureReason} dot={dot:F2} threshold={frontBackThreshold:F2}");
            return DirectionalGetUpMode.AnchorOnly;
        }

        bool useBack = dot > frontBackThreshold;
        if (invertImpactFrontBack)
            useBack = !useBack;

        stateName = useBack ? getUpBackStateName : getUpFrontStateName;
        if (string.IsNullOrWhiteSpace(stateName))
        {
            failureReason = useBack ? "back state empty" : "front state empty";
            LogImpactGetUp($"select anchorOnly reason={failureReason} dot={dot:F2} inverted={invertImpactFrontBack}");
            return DirectionalGetUpMode.AnchorOnly;
        }

        if (IsLegacyGetUpStateName(stateName))
        {
            failureReason = $"legacy get-up state disabled: {stateName}";
            LogImpactGetUp($"select anchorOnly reason={failureReason} dot={dot:F2} inverted={invertImpactFrontBack}");
            return DirectionalGetUpMode.AnchorOnly;
        }

        DirectionalGetUpMode mode = useBack ? DirectionalGetUpMode.Back : DirectionalGetUpMode.Front;
        LogImpactGetUp($"select mode={mode} state={stateName} dot={dot:F2} threshold={frontBackThreshold:F2} inverted={invertImpactFrontBack}");
        return mode;
    }

    private bool IsGetUpAnimationBlockingRecovery(float deltaTime)
    {
        if (!_getUpAnimationActive)
            return false;

        _getUpStateTimer += Mathf.Max(0f, deltaTime);
        if (_getUpStateTimer < Mathf.Max(0f, minGetUpStateTime))
            return true;

        float maxTime = Mathf.Max(Mathf.Max(0f, minGetUpStateTime), maxGetUpStateTime);
        if (maxTime > 0f && _getUpStateTimer >= maxTime)
        {
            LogGetUp($"finish reason=maxTime timer={_getUpStateTimer:F2}");
            ResetGetUpAnimationState("MaxTime");
            return false;
        }

        if (visualClipStateDriver != null &&
            visualClipStateDriver.TryGetAnimatorStateNormalizedTime(_getUpStateHash, out float normalizedTime))
        {
            if (normalizedTime < 0.98f)
                return true;

            LogGetUp($"finish reason=normalizedTime value={normalizedTime:F2}");
            ResetGetUpAnimationState("NormalizedTime");
            return false;
        }

        LogGetUp("finish reason=stateLeft");
        ResetGetUpAnimationState("StateLeft");
        return false;
    }

    private void ResetGetUpAnimationState(string reason)
    {
        if (_getUpAnimationActive)
            LogGetUp($"clear reason={reason} state={_activeGetUpStateName} returnToLocomotion={returnToLocomotionAfterGetUp}");

        if (visualClipStateDriver != null && visualClipStateDriver.IsExternalOneShotActive)
            visualClipStateDriver.CancelExternalOneShot(reason);

        _getUpAnimationActive = false;
        _getUpStateHash = 0;
        _getUpStateTimer = 0f;
        _activeGetUpStateName = null;
    }

    private void CaptureImpactGetUpContext(Vector3 impulse)
    {
        _hasLastImpactPlanarDirection = TryNormalizePlanarDirection(impulse, out _lastImpactPlanarDirection);
        _hasPreImpactFacingForward = TryResolvePreImpactFacingForward(out _preImpactFacingForward);
        LogImpactGetUp($"capture hasDirection={_hasLastImpactPlanarDirection} direction={FormatVector(_lastImpactPlanarDirection)} hasFacing={_hasPreImpactFacingForward} facing={FormatVector(_preImpactFacingForward)}");
    }

    private void ClearImpactGetUpContext(string reason)
    {
        if (!_hasLastImpactPlanarDirection && !_hasPreImpactFacingForward)
            return;

        _lastImpactPlanarDirection = Vector3.zero;
        _preImpactFacingForward = Vector3.zero;
        _hasLastImpactPlanarDirection = false;
        _hasPreImpactFacingForward = false;
        LogImpactGetUp($"clear reason={reason}");
    }

    private bool TryResolvePreImpactFacingForward(out Vector3 forward)
    {
        Transform root = transform.root != null ? transform.root : transform;
        if (root != null && TryNormalizePlanarDirection(root.forward, out forward))
            return true;

        if (bodyTransform != null && TryNormalizePlanarDirection(bodyTransform.forward, out forward))
            return true;

        forward = Vector3.zero;
        return false;
    }

    private static bool TryNormalizePlanarDirection(Vector3 value, out Vector3 planarDirection)
    {
        Vector3 planar = Vector3.ProjectOnPlane(SanitizeVector(value), Vector3.up);
        if (planar.sqrMagnitude <= 0.0001f)
        {
            planarDirection = Vector3.zero;
            return false;
        }

        planarDirection = planar.normalized;
        return IsFiniteVector(planarDirection);
    }

    private static bool IsLegacyGetUpStateName(string stateName)
    {
        return string.Equals(stateName, "GetUp", System.StringComparison.Ordinal) ||
               string.Equals(stateName, "Legacy_GetUp_DISABLED", System.StringComparison.Ordinal);
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

    private static bool IsFiniteQuaternion(Quaternion value)
    {
        return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z) && IsFinite(value.w);
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

    private void LogGetUp(string message)
    {
        if (!debugGetUpLogs && !debugGetUpPlayLogs)
            return;

        Debug.Log($"[MSGetUp] {message}", this);
    }

    private void LogImpactGetUp(string message)
    {
        if (!debugImpactGetUpLogs)
            return;

        Debug.Log($"[MSGetUp/Impact] {message}", this);
    }

    private static string FormatVector(Vector3 value)
    {
        return $"({value.x:F2},{value.y:F2},{value.z:F2})";
    }
}
