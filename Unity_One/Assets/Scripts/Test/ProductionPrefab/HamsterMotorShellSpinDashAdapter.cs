using Unity.Netcode;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

public sealed class HamsterMotorShellSpinDashAdapter : MonoBehaviour
{
    private const string TargetRootName = "Hamster_JointFreeMotorShell_MainScenes";
    private const string MotorShellBodyName = "MotorShellBody";
    private const string PlayerCameraName = "PlayerCamera";
    private const string DashReason = "SpinDash:Dashing";
    private const string DizzyReason = "SpinDash:Dizzy";
    private const string CooldownReason = "SpinDash:Cooldown";
    private const string GameplayPhaseLockReason = "GameState:NotPlaying";

    private enum SpinDashState
    {
        Idle,
        Dashing,
        Dizzy,
        Cooldown
    }

    [Header("References")]
    [SerializeField] private HamsterFullRagdollMotor motor;
    [SerializeField] private Rigidbody bodyRigidbody;
    [SerializeField] private Transform bodyTransform;
    [SerializeField] private Camera ownerCamera;
    [SerializeField] private HamsterMotorShellRagdollRecoveryAdapter recoveryAdapter;
    [SerializeField] private HamsterMotorShellItemAdapter itemAdapter;
    [SerializeField] private HamsterMotorShellCombatAdapter combatAdapter;
    [SerializeField] private HamsterVisualFollower visualFollower;
    [SerializeField] private HamsterVisualClipStateDriver visualClipStateDriver;
    [SerializeField] private HamsterMotorShellFaceExpressionAdapter faceAdapter;

    [Header("Input")]
    [SerializeField] private bool enableSpinDashAdapter = true;
    [SerializeField] private bool useProductionSprintInteractInput = true;
    [SerializeField] private bool useRightMouseInteract = true;
    [SerializeField] private bool requireSprintHeld = true;
    [SerializeField] private bool useDedicatedKeyInput = false;
    [SerializeField] private Key dedicatedSpinDashKey = Key.None;

    [Header("Conditions")]
    [SerializeField] private bool requireServerForPhysics = true;
    [SerializeField] private bool requireGrounded = true;
    [SerializeField] private bool requireHeldItem = true;
    [SerializeField] private bool blockDuringRecovery = true;
    [SerializeField] private bool ignoreWhenUiSelected = true;

    [Header("Dash")]
    [SerializeField] private float dashDuration = 1.2f;
    [SerializeField] private float cooldown = 1.2f;
    [SerializeField] private float dashVelocityChange = 5f;
    [SerializeField] private float upwardVelocityChange = 0.35f;
    [SerializeField] private float sustainAcceleration = 2.5f;
    [SerializeField] private float yawSpinVelocityChange = 7f;
    [SerializeField] private float yawSpinAcceleration = 20f;
    [SerializeField] private float postDashBlendOutDuration = 0.25f;

    [Header("Motor Control")]
    [SerializeField] private float movementControlScaleWhileDashing = 0.05f;
    [SerializeField] private float uprightControlScaleWhileDashing = 0.9f;
    [SerializeField] private float poseControlScaleWhileDashing = 0.6f;
    [SerializeField] private float movementControlScaleDuringBlendOut = 0.55f;
    [SerializeField] private bool allowJumpWhileDashing = false;

    [Header("Dizzy")]
    [SerializeField] private bool enableDizzyAfterDash = true;
    [SerializeField] private float dizzyDuration = 0.7f;
    [SerializeField] private float movementControlScaleWhileDizzy = 0f;
    [SerializeField] private float uprightControlScaleWhileDizzy = 1f;
    [SerializeField] private float poseControlScaleWhileDizzy = 1f;
    [SerializeField] private bool lockJumpWhileDizzy = true;
    [SerializeField] private bool playDizzyVisualOneShot = true;
    [SerializeField] private string dizzyStateName = "Dizzy";
    [SerializeField] private float dizzyCrossFade = 0.05f;
    [SerializeField] private float dizzyMinVisualTime = 0.2f;
    [SerializeField] private float dizzyMaxVisualTime = 0.7f;
    [SerializeField] private bool requireDizzyMotion = true;

    [Header("Direction")]
    [SerializeField] private bool preferCameraForward = true;
    [SerializeField] private bool fallbackToMotorFacing = true;

    [Header("Visual Spin")]
    [SerializeField] private bool enableVisualSpin = true;
    [SerializeField] private float visualSpinDegreesPerSecond = 1080f;
    [SerializeField] private float visualSpinBlendOutDuration = 0.18f;
    [SerializeField] private Vector3 visualSpinLocalAxis = Vector3.right;
    [SerializeField] private bool invertVisualSpin = false;

    [Header("Debug")]
    [SerializeField] private bool debugSpinDashLogs = false;

    private NetworkObject _ownerNetworkObject;
    private GameStateManager _gameStateManager;
    private Transform _targetRoot;
    private SpinDashState _state;
    private Vector3 _dashDirection = Vector3.forward;
    private float _dashEndTime;
    private float _dizzyEndTime;
    private float _cooldownUntil;
    private float _blendOutEndTime;

    public bool IsDizzyActive => _state == SpinDashState.Dizzy;
    public bool IsSpinDashBusy => _state != SpinDashState.Idle;

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
        CancelSpinDash("OnDisable");
        ClearVisualSpin("OnDisable");
        RestoreMotorControlIfOwned();
    }

    private void Update()
    {
        if (!IsTargetRoot())
            return;

        CacheReferences();

        if (IsGameplayPhaseLocked())
        {
            if (_state != SpinDashState.Idle)
                CancelSpinDashForGameplayPhase();
            return;
        }

        if (blockDuringRecovery && IsRecoveryActive())
        {
            CancelSpinDash("RecoveryActive");
            return;
        }

        if (_state == SpinDashState.Dizzy && Time.time >= _dizzyEndTime)
            FinishDizzy("DurationFinished");

        if (_state == SpinDashState.Cooldown && Time.time >= _blendOutEndTime)
        {
            RestoreMotorControlIfOwned();
            _state = SpinDashState.Idle;
        }

        if (!CanReadOwnerInput())
            return;

        if (ReadSpinDashPressed())
            TryStartSpinDash();
    }

    private void FixedUpdate()
    {
        if (!IsTargetRoot())
            return;

        if (IsGameplayPhaseLocked())
        {
            if (_state != SpinDashState.Idle)
                CancelSpinDashForGameplayPhase();
            return;
        }

        if (_state != SpinDashState.Dashing)
            return;

        if (blockDuringRecovery && IsRecoveryActive())
        {
            CancelSpinDash("RecoveryActiveFixed");
            return;
        }

        if (Time.time >= _dashEndTime)
        {
            StopSpinDash(true, "DurationFinished");
            return;
        }

        ApplySustainForces();
    }

    private void CacheReferences()
    {
        _targetRoot = transform.root != null ? transform.root : transform;

        if (_ownerNetworkObject == null)
            _ownerNetworkObject = GetComponent<NetworkObject>();
        if (_ownerNetworkObject == null)
            _ownerNetworkObject = GetComponentInParent<NetworkObject>();

        if (_gameStateManager == null)
            _gameStateManager = FindFirstObjectByType<GameStateManager>();

        if (motor == null && _targetRoot != null)
            motor = _targetRoot.GetComponentInChildren<HamsterFullRagdollMotor>(true);

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
        if (itemAdapter == null && _targetRoot != null)
            itemAdapter = _targetRoot.GetComponentInChildren<HamsterMotorShellItemAdapter>(true);
        if (combatAdapter == null && _targetRoot != null)
            combatAdapter = _targetRoot.GetComponentInChildren<HamsterMotorShellCombatAdapter>(true);
        if (visualFollower == null && _targetRoot != null)
            visualFollower = _targetRoot.GetComponentInChildren<HamsterVisualFollower>(true);
        if (visualClipStateDriver == null && _targetRoot != null)
            visualClipStateDriver = _targetRoot.GetComponentInChildren<HamsterVisualClipStateDriver>(true);
        if (faceAdapter == null && _targetRoot != null)
            faceAdapter = _targetRoot.GetComponentInChildren<HamsterMotorShellFaceExpressionAdapter>(true);
    }

    private bool TryGetGameState(out GameStateManager.GameState state)
    {
        if (_gameStateManager == null)
            _gameStateManager = FindFirstObjectByType<GameStateManager>();

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

    private bool CanReadOwnerInput()
    {
        if (!enableSpinDashAdapter || !IsTargetRoot())
            return false;

        NetworkManager networkManager = NetworkManager.Singleton;
        if (networkManager == null || !networkManager.IsListening)
            return false;

        return _ownerNetworkObject == null || _ownerNetworkObject.IsOwner;
    }

    private bool CanApplyServerPhysics()
    {
        if (!requireServerForPhysics)
            return true;

        NetworkManager networkManager = NetworkManager.Singleton;
        return networkManager != null && networkManager.IsServer;
    }

    private bool ReadSpinDashPressed()
    {
        if (ignoreWhenUiSelected && IsUiInputBlocked())
            return false;

        bool productionInput = false;
        if (useProductionSprintInteractInput)
        {
            bool sprintOk = !requireSprintHeld || IsSprintHeld();
            bool interactPressed = useRightMouseInteract && Mouse.current != null && Mouse.current.rightButton.wasPressedThisFrame;
            productionInput = sprintOk && interactPressed;
        }

        bool dedicatedInput = false;
        if (useDedicatedKeyInput && dedicatedSpinDashKey != Key.None)
        {
            Keyboard keyboard = Keyboard.current;
            dedicatedInput = keyboard != null && keyboard[dedicatedSpinDashKey].wasPressedThisFrame;
        }

        return productionInput || dedicatedInput;
    }

    private bool TryStartSpinDash()
    {
        if (_state != SpinDashState.Idle)
            return false;

        if (Time.time < _cooldownUntil)
            return false;

        if (!CanApplyServerPhysics())
        {
            Log("start rejected: server physics required");
            return false;
        }

        if (blockDuringRecovery && IsRecoveryActive())
            return false;

        if (requireGrounded && motor != null && !motor.IsGrounded)
            return false;

        if (requireHeldItem && (itemAdapter == null || !itemAdapter.HasHeldItem))
            return false;

        if (bodyRigidbody == null || bodyRigidbody.isKinematic)
            return false;

        if (!TryGetDashDirection(out Vector3 direction))
            return false;

        _state = SpinDashState.Dashing;
        _dashDirection = direction;
        _dashEndTime = Time.time + Mathf.Max(0.01f, dashDuration);
        _cooldownUntil = Time.time + Mathf.Max(0.01f, cooldown);
        _blendOutEndTime = 0f;

        ApplyDashingMotorControl();

        bodyRigidbody.WakeUp();
        bodyRigidbody.AddForce(
            _dashDirection * Mathf.Max(0f, dashVelocityChange) + Vector3.up * Mathf.Max(0f, upwardVelocityChange),
            ForceMode.VelocityChange);
        bodyRigidbody.AddTorque(Vector3.up * Mathf.Max(0f, yawSpinVelocityChange), ForceMode.VelocityChange);
        StartVisualSpin();

        Log($"started direction={_dashDirection} duration={dashDuration:F2} cooldown={cooldown:F2}");
        return true;
    }

    private void ApplySustainForces()
    {
        if (bodyRigidbody == null || bodyRigidbody.isKinematic)
            return;

        float sustain = Mathf.Max(0f, sustainAcceleration);
        if (sustain > 0f)
            bodyRigidbody.AddForce(_dashDirection * sustain, ForceMode.Acceleration);

        float yawAcceleration = Mathf.Max(0f, yawSpinAcceleration);
        if (yawAcceleration > 0f)
            bodyRigidbody.AddTorque(Vector3.up * yawAcceleration, ForceMode.Acceleration);
    }

    private void StopSpinDash(bool enterBlendOut, string reason)
    {
        if (_state != SpinDashState.Dashing)
            return;

        _dashEndTime = 0f;

        if (enterBlendOut && enableDizzyAfterDash && dizzyDuration > 0f)
        {
            StartDizzy(reason);
        }
        else if (enterBlendOut)
        {
            EnterCooldownBlendOut(reason);
            StopVisualSpinWithBlendOut();
        }
        else
        {
            _state = SpinDashState.Idle;
            _dizzyEndTime = 0f;
            _blendOutEndTime = 0f;
            ClearVisualSpin(reason);
            RestoreMotorControlIfOwned();
        }

        Log($"stopped reason={reason} blendOut={enterBlendOut}");
    }

    private void CancelSpinDashForGameplayPhase()
    {
        CancelSpinDash(GameplayPhaseLockReason);
        _dashDirection = Vector3.forward;
        _cooldownUntil = 0f;
    }

    private void CancelSpinDash(string reason)
    {
        if (_state == SpinDashState.Idle)
            return;

        if (_state == SpinDashState.Dashing)
        {
            StopSpinDash(false, reason);
            return;
        }

        _state = SpinDashState.Idle;
        _dashEndTime = 0f;
        _dizzyEndTime = 0f;
        _blendOutEndTime = 0f;
        ClearVisualSpin(reason);
        CancelDizzyVisualOneShot(reason);
        RestoreMotorControlIfOwned();
        Log($"cancelled reason={reason}");
    }

    private void StartDizzy(string reason)
    {
        _state = SpinDashState.Dizzy;
        _dizzyEndTime = Time.time + Mathf.Max(0.01f, dizzyDuration);
        _blendOutEndTime = 0f;

        StopVisualSpinWithBlendOut();
        ApplyDizzyMotorControl();
        TryPlayDizzyVisualOneShot();
        Log($"dizzy started reason={reason} duration={dizzyDuration:F2}");
    }

    private void FinishDizzy(string reason)
    {
        if (_state != SpinDashState.Dizzy)
            return;

        _dizzyEndTime = 0f;
        CancelDizzyVisualOneShot(reason);
        EnterCooldownBlendOut(reason);
        Log($"dizzy finished reason={reason}");
    }

    private void EnterCooldownBlendOut(string reason)
    {
        if (postDashBlendOutDuration > 0f)
        {
            _state = SpinDashState.Cooldown;
            _blendOutEndTime = Time.time + Mathf.Max(0f, postDashBlendOutDuration);
            ApplyBlendOutMotorControl();
            Log($"cooldown blendout started reason={reason} duration={postDashBlendOutDuration:F2}");
            return;
        }

        _state = SpinDashState.Idle;
        _blendOutEndTime = 0f;
        RestoreMotorControlIfOwned();
    }

    private void StartVisualSpin()
    {
        if (!enableVisualSpin || visualFollower == null)
            return;

        visualFollower.SetSpinDashVisualSpin(
            true,
            _dashDirection,
            visualSpinDegreesPerSecond,
            visualSpinBlendOutDuration,
            visualSpinLocalAxis,
            invertVisualSpin,
            "SpinDashStart");
    }

    private void StopVisualSpinWithBlendOut()
    {
        if (!enableVisualSpin || visualFollower == null)
            return;

        visualFollower.SetSpinDashVisualSpin(
            false,
            _dashDirection,
            visualSpinDegreesPerSecond,
            visualSpinBlendOutDuration,
            visualSpinLocalAxis,
            invertVisualSpin,
            "SpinDashEnd");
    }

    private void ClearVisualSpin(string reason)
    {
        if (visualFollower == null)
            return;

        visualFollower.ClearSpinDashVisualSpin(reason);
    }

    private void ApplyDashingMotorControl()
    {
        if (motor == null)
            return;

        motor.SetExternalControlLock(false, DashReason);
        motor.SetExternalJumpLock(!allowJumpWhileDashing, DashReason);
        motor.SetExternalMovementControlScale(Mathf.Clamp01(movementControlScaleWhileDashing), DashReason);
        motor.SetExternalUprightControlScale(Mathf.Clamp01(uprightControlScaleWhileDashing), DashReason);
        motor.SetExternalPoseControlScale(Mathf.Clamp01(poseControlScaleWhileDashing), DashReason);
    }

    private void ApplyDizzyMotorControl()
    {
        if (motor == null)
            return;

        motor.SetExternalControlLock(false, DizzyReason);
        motor.SetExternalJumpLock(lockJumpWhileDizzy, DizzyReason);
        motor.SetExternalMovementControlScale(Mathf.Clamp01(movementControlScaleWhileDizzy), DizzyReason);
        motor.SetExternalUprightControlScale(Mathf.Clamp01(uprightControlScaleWhileDizzy), DizzyReason);
        motor.SetExternalPoseControlScale(Mathf.Clamp01(poseControlScaleWhileDizzy), DizzyReason);
    }

    private void ApplyBlendOutMotorControl()
    {
        if (motor == null)
            return;

        motor.SetExternalControlLock(false, CooldownReason);
        motor.SetExternalJumpLock(false, CooldownReason);
        motor.SetExternalMovementControlScale(Mathf.Clamp01(movementControlScaleDuringBlendOut), CooldownReason);
        motor.SetExternalUprightControlScale(1f, CooldownReason);
        motor.SetExternalPoseControlScale(1f, CooldownReason);
    }

    private void RestoreMotorControlIfOwned()
    {
        if (motor == null)
            return;

        if (IsSpinDashReason(motor.ExternalControlLockReason))
            motor.SetExternalControlLock(false, "SpinDash:Restore");
        if (IsSpinDashReason(motor.ExternalJumpLockReason))
            motor.SetExternalJumpLock(false, "SpinDash:Restore");
        if (IsSpinDashReason(motor.ExternalMovementControlReason))
            motor.SetExternalMovementControlScale(1f, "SpinDash:Restore");
        if (IsSpinDashReason(motor.ExternalUprightControlReason))
            motor.SetExternalUprightControlScale(1f, "SpinDash:Restore");
        if (IsSpinDashReason(motor.ExternalPoseControlReason))
            motor.SetExternalPoseControlScale(1f, "SpinDash:Restore");
    }

    private void TryPlayDizzyVisualOneShot()
    {
        if (!playDizzyVisualOneShot || visualClipStateDriver == null || string.IsNullOrWhiteSpace(dizzyStateName))
            return;

        float duration = Mathf.Max(0.01f, dizzyDuration);
        float minTime = Mathf.Min(Mathf.Max(0f, dizzyMinVisualTime), duration);
        float maxTime = Mathf.Max(minTime, dizzyMaxVisualTime > 0f ? dizzyMaxVisualTime : duration);
        if (!visualClipStateDriver.TryPlayOneShotState(
            dizzyStateName,
            Mathf.Max(0f, dizzyCrossFade),
            minTime,
            maxTime,
            requireDizzyMotion,
            out _,
            out string failureReason))
        {
            Log($"dizzy visual skipped reason={failureReason}");
        }
    }

    private void CancelDizzyVisualOneShot(string reason)
    {
        if (visualClipStateDriver == null || string.IsNullOrWhiteSpace(dizzyStateName))
            return;

        if (visualClipStateDriver.IsPlayingExternalOneShot(dizzyStateName))
            visualClipStateDriver.CancelExternalOneShot(reason);
    }

    private bool TryGetDashDirection(out Vector3 direction)
    {
        direction = Vector3.zero;

        if (preferCameraForward && ownerCamera != null)
            direction = ownerCamera.transform.forward;

        direction.y = 0f;
        if (direction.sqrMagnitude > 0.0001f)
        {
            direction.Normalize();
            return true;
        }

        if (fallbackToMotorFacing && motor != null)
        {
            direction = motor.DesiredFacingDirection;
            direction.y = 0f;
            if (direction.sqrMagnitude <= 0.0001f)
            {
                direction = motor.SmoothedMoveWorldDirection;
                direction.y = 0f;
            }
        }

        if (direction.sqrMagnitude <= 0.0001f && bodyTransform != null)
        {
            direction = bodyTransform.forward;
            direction.y = 0f;
        }

        if (direction.sqrMagnitude <= 0.0001f)
            return false;

        direction.Normalize();
        return IsFiniteVector(direction);
    }

    private bool IsRecoveryActive()
    {
        return recoveryAdapter != null &&
               recoveryAdapter.CurrentRecoveryState != HamsterMotorShellRagdollRecoveryAdapter.RecoveryState.Normal;
    }

    private bool IsSprintHeld()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null)
            return motor != null && motor.IsSprintHeld;

        return keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed;
    }

    private static bool IsUiInputBlocked()
    {
        EventSystem eventSystem = EventSystem.current;
        if (eventSystem == null)
            return false;

        return eventSystem.currentSelectedGameObject != null || eventSystem.IsPointerOverGameObject();
    }

    private bool IsTargetRoot()
    {
        Transform root = _targetRoot != null ? _targetRoot : (transform.root != null ? transform.root : transform);
        return root.name == TargetRootName || root.name.StartsWith(TargetRootName + "(", System.StringComparison.Ordinal);
    }

    private static bool IsSpinDashReason(string reason)
    {
        return reason == DashReason || reason == DizzyReason || reason == CooldownReason;
    }

    private static bool IsFiniteVector(Vector3 value)
    {
        return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
    }

    private static bool IsFinite(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value);
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

    private void Log(string message)
    {
        if (!debugSpinDashLogs)
            return;

        Debug.Log($"[MSSpinDash:{name}] {message}", this);
    }
}
