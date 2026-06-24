using UnityEngine;
using UnityEngine.EventSystems;

public sealed class HamsterMotorShellTraversalAdapter : MonoBehaviour
{
    private const string TargetRootName = "Hamster_JointFreeMotorShell_MainScenes";
    private const string MotorShellBodyName = "MotorShellBody";
    private const string PlayerCameraName = "PlayerCamera";
    private const string TraversalReason = "TraversalGlide";

    private enum TraversalState
    {
        Normal,
        Gliding,
        Cooldown
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

    [Header("Glide Animation")]
    [SerializeField] private bool useGlideAnimation = true;
    [SerializeField] private string glideAnimationStateName = "Glide";
    [SerializeField] private float glideAnimationCrossFade = 0.08f;

    [Header("Debug")]
    [SerializeField] private bool debugTraversalLogs = false;

    private Transform _targetRoot;
    private TraversalState _state;
    private float _lastGlideKeyDownTime = float.NegativeInfinity;
    private float _glideStartedTime = float.NegativeInfinity;
    private float _cooldownUntil;
    private bool _legacyInputUnavailable;
    private bool _appliedMovementScale;
    private bool _appliedUprightScale;
    private bool _glideAnimationActive;

    public bool IsGliding => _state == TraversalState.Gliding;
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
    }

    private void OnDestroy()
    {
        EndGlide("OnDestroy", false);
    }

    private void Update()
    {
        if (!IsTargetRoot())
            return;

        if (autoFindReferences)
            CacheReferences();

        CaptureGlideKeyDown();

        if (_state == TraversalState.Cooldown && Time.time >= _cooldownUntil)
            _state = TraversalState.Normal;

        if (_state == TraversalState.Gliding)
        {
            if (ShouldEndGlide(out string endReason))
                EndGlide(endReason, true);
            return;
        }

        if (_state == TraversalState.Normal)
        {
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
        if (!IsTargetRoot() || _state != TraversalState.Gliding)
            return;

        if (autoFindReferences)
            CacheReferences();

        if (ShouldEndGlide(out string endReason))
        {
            EndGlide(endReason, true);
            return;
        }

        ApplyGlideForces();
    }

    private void CacheReferences()
    {
        _targetRoot = transform.root != null ? transform.root : transform;

        if (motor == null && _targetRoot != null)
            motor = _targetRoot.GetComponentInChildren<HamsterFullRagdollMotor>(true);

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

    private void CaptureGlideKeyDown()
    {
        if (_legacyInputUnavailable || IsUiInputBlocked())
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

        if (_appliedMovementScale && IsTraversalReason(motor.ExternalMovementControlReason))
            motor.SetExternalMovementControlScale(1f, "TraversalGlide:Restore");

        if (_appliedUprightScale && IsTraversalReason(motor.ExternalUprightControlReason))
            motor.SetExternalUprightControlScale(1f, "TraversalGlide:Restore");

        _appliedMovementScale = false;
        _appliedUprightScale = false;
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

    private bool ReadGlideHeld()
    {
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
        return reason == TraversalReason || reason == "TraversalGlide:Restore";
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
    }
}
