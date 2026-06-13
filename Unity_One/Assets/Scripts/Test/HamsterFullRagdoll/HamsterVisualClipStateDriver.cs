using UnityEngine;

public sealed class HamsterVisualClipStateDriver : MonoBehaviour
{
    private const int BaseLayerIndex = 0;

    [Header("References")]
    [SerializeField] private Animator visualAnimator;
    [SerializeField] private HamsterFullRagdollMotor motorStateSource;
    [SerializeField] private Rigidbody targetBody;
    [SerializeField] private bool autoFindReferences = true;

    [Header("State Names")]
    [SerializeField] private string idleStateName = "Idle";
    [SerializeField] private string walkStateName = "Walk";
    [SerializeField] private string runStateName = "Run";
    [SerializeField] private string jumpStateName = "JumpUp";

    [Header("Mode")]
    [SerializeField] private bool enableClipStateDriver = true;
    [SerializeField] private bool driveGroundLocomotion = true;
    [SerializeField] private bool driveJump = true;
    [SerializeField] private bool disableFallAndLandForNow = true;
    [SerializeField] private bool requireAnimatorController = true;

    [Header("Locomotion Thresholds")]
    [SerializeField] private float minMoveSpeedForWalk = 0.12f;
    [SerializeField] private float minSprint01ForRun = 0.5f;
    [SerializeField] private float minSpeedForRun = 1.7f;
    [SerializeField] private float locomotionCrossFadeDuration = 0.08f;

    [Header("Run Gate")]
    [SerializeField] private float minPlanarSpeedForSprintRunState = 0.75f;
    [SerializeField] private bool requireMoveInputForRun = true;
    [SerializeField] private bool preventRunAtNearZeroSpeed = true;

    [Header("Jump")]
    [SerializeField] private float minVerticalVelocityForJump = 0.15f;
    [SerializeField] private float jumpCrossFadeDuration = 0.04f;
    [SerializeField] private float minJumpStateTime = 0.12f;
    [SerializeField] private float jumpStateCooldown = 0.10f;
    [SerializeField] private bool triggerJumpOnAirborneTransition = true;
    [SerializeField] private bool triggerJumpOnPositiveVerticalVelocity = true;

    [Header("Jump Safety")]
    [SerializeField] private float maxJumpStateTime = 0.20f;
    [SerializeField] private bool returnFromJumpWhenFalling = true;
    [SerializeField] private float fallingReturnVerticalVelocity = 0.0f;
    [SerializeField] private bool returnFromJumpWhenGrounded = true;

    [Header("Landing Return")]
    [SerializeField] private float groundedReturnDelay = 0.03f;
    [SerializeField] private bool returnToLocomotionOnGrounded = true;

    [Header("Debug")]
    [SerializeField] private bool debugLogs = false;
    [SerializeField] private float debugLogInterval = 0.5f;

    private int _currentStateHash;
    private string _currentStateName;
    private float _stateTimer;
    private bool _wasGrounded;
    private float _lastJumpTriggerTime = float.NegativeInfinity;
    private float _groundedReturnTimer;
    private float _debugLogTimer;

    private int _idleStateHash;
    private int _walkStateHash;
    private int _runStateHash;
    private int _jumpStateHash;
    private bool _hasIdleState;
    private bool _hasWalkState;
    private bool _hasRunState;
    private bool _hasJumpState;
    private bool _warnedMissingAnimator;
    private bool _warnedAnimatorDisabled;
    private bool _warnedMissingController;
    private bool _warnedRootMotion;

    private enum ClipState
    {
        Idle,
        Walk,
        Run,
        JumpUp
    }

    private struct MotionSample
    {
        public bool HasGroundedState;
        public bool IsGrounded;
        public bool IsSprintHeld;
        public bool HasMoveInputState;
        public bool HasMoveInput;
        public float PlanarSpeed;
        public float VerticalVelocity;
        public string Source;
    }

    private struct PlayableState
    {
        public int Hash;
        public string Name;
        public bool IsValid;
    }

    private void Awake()
    {
        if (autoFindReferences)
            ResolveReferences();

        CacheStateAvailability();
    }

    private void OnEnable()
    {
        if (autoFindReferences)
            ResolveReferences();

        CacheStateAvailability();
        ResetRuntimeState();

        if (requireAnimatorController && visualAnimator != null && visualAnimator.runtimeAnimatorController == null)
        {
            WarnMissingController();
            enabled = false;
        }
    }

    private void Start()
    {
        if (autoFindReferences)
            ResolveReferences();

        CacheStateAvailability();
    }

    private void Update()
    {
        float deltaTime = Time.deltaTime;
        _stateTimer += deltaTime;

        if (!enableClipStateDriver)
            return;

        if (autoFindReferences && (visualAnimator == null || motorStateSource == null || targetBody == null))
            ResolveReferences();

        if (!CanDriveAnimator())
            return;

        MotionSample sample = ReadMotionSample();
        TickStateDriver(sample, deltaTime);
        TickDebugLog(sample, deltaTime);

        if (sample.HasGroundedState)
            _wasGrounded = sample.IsGrounded;
    }

    private void OnValidate()
    {
        minMoveSpeedForWalk = Mathf.Max(0f, minMoveSpeedForWalk);
        minSprint01ForRun = Mathf.Clamp01(minSprint01ForRun);
        minSpeedForRun = Mathf.Max(0f, minSpeedForRun);
        locomotionCrossFadeDuration = Mathf.Max(0f, locomotionCrossFadeDuration);
        minPlanarSpeedForSprintRunState = Mathf.Max(0f, minPlanarSpeedForSprintRunState);
        minVerticalVelocityForJump = Mathf.Max(0f, minVerticalVelocityForJump);
        jumpCrossFadeDuration = Mathf.Max(0f, jumpCrossFadeDuration);
        minJumpStateTime = Mathf.Max(0f, minJumpStateTime);
        jumpStateCooldown = Mathf.Max(0f, jumpStateCooldown);
        maxJumpStateTime = Mathf.Max(0f, maxJumpStateTime);
        groundedReturnDelay = Mathf.Max(0f, groundedReturnDelay);
        debugLogInterval = Mathf.Max(0.01f, debugLogInterval);
    }

    [ContextMenu("Find Clip State Driver References")]
    private void ResolveReferences()
    {
        if (motorStateSource == null)
            motorStateSource = GetComponent<HamsterFullRagdollMotor>();

        if (motorStateSource == null)
            motorStateSource = GetComponentInParent<HamsterFullRagdollMotor>();

        if (targetBody == null)
            targetBody = GetComponent<Rigidbody>();

        if (targetBody == null && motorStateSource != null)
            targetBody = motorStateSource.GetComponent<Rigidbody>();

        if (visualAnimator == null)
            visualAnimator = FindVisualPreviewAnimator();
    }

    private Animator FindVisualPreviewAnimator()
    {
        Transform visualPreviewRoot = FindChildRecursive(transform, "VisualPreviewRoot");
        if (visualPreviewRoot != null)
        {
            Animator animatorInPreviewRoot = visualPreviewRoot.GetComponentInChildren<Animator>(true);
            if (animatorInPreviewRoot != null)
                return animatorInPreviewRoot;
        }

        return GetComponentInChildren<Animator>(true);
    }

    private Transform FindChildRecursive(Transform root, string childName)
    {
        if (root == null)
            return null;

        for (int i = 0; i < root.childCount; i++)
        {
            Transform child = root.GetChild(i);
            if (child.name == childName)
                return child;

            Transform result = FindChildRecursive(child, childName);
            if (result != null)
                return result;
        }

        return null;
    }

    private void CacheStateAvailability()
    {
        _hasIdleState = TryCacheState(idleStateName, out _idleStateHash);
        _hasWalkState = TryCacheState(walkStateName, out _walkStateHash);
        _hasRunState = TryCacheState(runStateName, out _runStateHash);
        _hasJumpState = TryCacheState(jumpStateName, out _jumpStateHash);

        if (visualAnimator == null || visualAnimator.runtimeAnimatorController == null)
            return;

        WarnIfMissingState(idleStateName, _hasIdleState);
        WarnIfMissingState(walkStateName, _hasWalkState);
        WarnIfMissingState(runStateName, _hasRunState);
        WarnIfMissingState(jumpStateName, _hasJumpState);
    }

    private bool TryCacheState(string stateName, out int stateHash)
    {
        stateHash = Animator.StringToHash(stateName);

        if (visualAnimator == null || visualAnimator.runtimeAnimatorController == null || string.IsNullOrWhiteSpace(stateName))
            return false;

        if (visualAnimator.HasState(BaseLayerIndex, stateHash))
            return true;

        int baseLayerStateHash = Animator.StringToHash($"Base Layer.{stateName}");
        if (visualAnimator.HasState(BaseLayerIndex, baseLayerStateHash))
        {
            stateHash = baseLayerStateHash;
            return true;
        }

        return false;
    }

    private void ResetRuntimeState()
    {
        _currentStateHash = 0;
        _currentStateName = string.Empty;
        _stateTimer = 0f;
        _groundedReturnTimer = 0f;
        _debugLogTimer = 0f;

        MotionSample sample = ReadMotionSample();
        _wasGrounded = sample.HasGroundedState && sample.IsGrounded;
    }

    private bool CanDriveAnimator()
    {
        if (visualAnimator == null)
        {
            if (!_warnedMissingAnimator)
            {
                Debug.LogWarning($"[HamsterVisualClipStateDriver:{name}] visualAnimator is missing.", this);
                _warnedMissingAnimator = true;
            }

            return false;
        }

        if (!visualAnimator.enabled)
        {
            if (debugLogs && !_warnedAnimatorDisabled)
            {
                Debug.LogWarning($"[HamsterVisualClipStateDriver:{name}] visualAnimator is disabled.", this);
                _warnedAnimatorDisabled = true;
            }

            return false;
        }

        if (visualAnimator.runtimeAnimatorController == null)
        {
            if (requireAnimatorController)
            {
                WarnMissingController();
                enabled = false;
            }

            return false;
        }

        if (debugLogs && visualAnimator.applyRootMotion && !_warnedRootMotion)
        {
            Debug.LogWarning($"[HamsterVisualClipStateDriver:{name}] visualAnimator.applyRootMotion is true. Set it false in the Animator Inspector for this visual-only driver.", this);
            _warnedRootMotion = true;
        }

        return true;
    }

    private MotionSample ReadMotionSample()
    {
        if (motorStateSource != null)
        {
            return new MotionSample
            {
                HasGroundedState = true,
                IsGrounded = motorStateSource.IsGrounded,
                IsSprintHeld = motorStateSource.IsSprintHeld,
                HasMoveInputState = true,
                HasMoveInput = motorStateSource.SmoothedMoveWorldDirection.sqrMagnitude > 0.0001f,
                PlanarSpeed = Mathf.Max(0f, motorStateSource.CurrentPlanarSpeed),
                VerticalVelocity = motorStateSource.CurrentVerticalVelocity,
                Source = "Motor"
            };
        }

        if (targetBody != null)
        {
            Vector3 velocity = targetBody.linearVelocity;
            Vector3 planarVelocity = velocity;
            planarVelocity.y = 0f;

            return new MotionSample
            {
                HasGroundedState = false,
                IsGrounded = false,
                IsSprintHeld = false,
                HasMoveInputState = false,
                HasMoveInput = planarVelocity.sqrMagnitude > 0.0001f,
                PlanarSpeed = planarVelocity.magnitude,
                VerticalVelocity = velocity.y,
                Source = "Rigidbody"
            };
        }

        return new MotionSample
        {
            HasGroundedState = false,
            IsGrounded = false,
            IsSprintHeld = false,
            HasMoveInputState = false,
            HasMoveInput = false,
            PlanarSpeed = 0f,
            VerticalVelocity = 0f,
            Source = "None"
        };
    }

    private void TickStateDriver(MotionSample sample, float deltaTime)
    {
        bool grounded = sample.HasGroundedState && sample.IsGrounded;

        if (driveJump && ShouldTriggerJump(sample, grounded))
        {
            _lastJumpTriggerTime = Time.time;

            if (TryCrossFade(ClipState.JumpUp, jumpCrossFadeDuration, sample))
            {
                _groundedReturnTimer = 0f;
                return;
            }
        }

        if (IsCurrentState(ClipState.JumpUp))
        {
            TickJumpState(sample, grounded, deltaTime);
            return;
        }

        if (!driveGroundLocomotion)
            return;

        if (sample.HasGroundedState && !grounded)
            return;

        TryCrossFade(SelectLocomotionState(sample, out string reason), locomotionCrossFadeDuration, sample, reason);
    }

    private bool ShouldTriggerJump(MotionSample sample, bool grounded)
    {
        if (Time.time - _lastJumpTriggerTime < jumpStateCooldown)
            return false;

        if (IsCurrentState(ClipState.JumpUp))
            return false;

        bool becameAirborne = sample.HasGroundedState && _wasGrounded && !grounded;
        bool hasPositiveVerticalVelocity = sample.VerticalVelocity > minVerticalVelocityForJump;

        return (triggerJumpOnAirborneTransition && becameAirborne)
            || (triggerJumpOnPositiveVerticalVelocity && hasPositiveVerticalVelocity);
    }

    private void TickJumpState(MotionSample sample, bool grounded, float deltaTime)
    {
        if (_stateTimer < minJumpStateTime)
            return;

        bool canReturnFromFalling = returnFromJumpWhenFalling
            && (!sample.HasGroundedState || !grounded)
            && sample.VerticalVelocity <= fallingReturnVerticalVelocity;
        if (canReturnFromFalling)
        {
            ReturnFromJumpToLocomotion(sample, "falling return");
            return;
        }

        if (maxJumpStateTime > 0f && _stateTimer >= maxJumpStateTime)
        {
            ReturnFromJumpToLocomotion(sample, "max state time");
            return;
        }

        if (sample.HasGroundedState)
        {
            if (!grounded)
            {
                _groundedReturnTimer = 0f;
                return;
            }

            _groundedReturnTimer += deltaTime;
            if (!returnFromJumpWhenGrounded || !returnToLocomotionOnGrounded || _groundedReturnTimer < groundedReturnDelay)
                return;
        }

        ReturnFromJumpToLocomotion(sample, "grounded return delay");
    }

    private void ReturnFromJumpToLocomotion(MotionSample sample, string reason)
    {
        if (!driveGroundLocomotion)
            return;

        ClipState locomotionState = SelectLocomotionState(sample, out string locomotionReason);
        if (debugLogs)
        {
            Debug.Log(
                $"[HamsterVisualClipStateDriver] jumpReturn reason={reason} state={locomotionState} speed={sample.PlanarSpeed:F2} sprint={sample.IsSprintHeld} grounded={FormatGrounded(sample)} vertical={sample.VerticalVelocity:F2} locomotionReason={locomotionReason}",
                this);
        }

        TryCrossFade(locomotionState, locomotionCrossFadeDuration, sample, reason);
    }

    private ClipState SelectLocomotionState(MotionSample sample, out string reason)
    {
        bool moving = IsMoving(sample);
        if (!moving)
        {
            reason = sample.HasMoveInputState ? "no move input" : "speed below Walk";
            return ClipState.Idle;
        }

        if (preventRunAtNearZeroSpeed && sample.PlanarSpeed < minMoveSpeedForWalk)
        {
            reason = "near zero speed";
            return ClipState.Idle;
        }

        bool groundedForRun = sample.HasGroundedState && sample.IsGrounded;
        if (groundedForRun && sample.PlanarSpeed >= minSpeedForRun)
        {
            reason = "speed >= minSpeedForRun";
            return ClipState.Run;
        }

        if (sample.IsSprintHeld)
        {
            if (groundedForRun && sample.PlanarSpeed >= minPlanarSpeedForSprintRunState)
            {
                reason = "sprint speed gate met";
                return ClipState.Run;
            }

            reason = sample.PlanarSpeed < minPlanarSpeedForSprintRunState
                ? "sprint too slow for Run"
                : "Run blocked while airborne";
            return ClipState.Walk;
        }

        reason = sample.PlanarSpeed >= minSpeedForRun && !groundedForRun
            ? "Run blocked while airborne"
            : "walking";
        return ClipState.Walk;
    }

    private bool IsMoving(MotionSample sample)
    {
        if (requireMoveInputForRun && sample.HasMoveInputState)
            return sample.HasMoveInput;

        return sample.PlanarSpeed >= minMoveSpeedForWalk;
    }

    private bool TryCrossFade(ClipState desiredState, float crossFadeDuration, MotionSample sample, string reason = null)
    {
        PlayableState playableState = ResolvePlayableState(desiredState);
        if (!playableState.IsValid)
        {
            if (debugLogs)
                Debug.Log($"[HamsterVisualClipStateDriver] Skip {desiredState} because the state is missing. speed={sample.PlanarSpeed:F2} sprint={sample.IsSprintHeld} grounded={FormatGrounded(sample)} vertical={sample.VerticalVelocity:F2} reason={FormatReason(reason)}", this);

            return false;
        }

        if (_currentStateHash == playableState.Hash)
            return false;

        visualAnimator.CrossFade(playableState.Hash, crossFadeDuration, BaseLayerIndex);
        _currentStateHash = playableState.Hash;
        _currentStateName = playableState.Name;
        _stateTimer = 0f;

        if (debugLogs)
            Debug.Log($"[HamsterVisualClipStateDriver] CrossFade {_currentStateName} speed={sample.PlanarSpeed:F2} sprint={sample.IsSprintHeld} grounded={FormatGrounded(sample)} vertical={sample.VerticalVelocity:F2} reason={FormatReason(reason)}", this);

        return true;
    }

    private PlayableState ResolvePlayableState(ClipState desiredState)
    {
        switch (desiredState)
        {
            case ClipState.JumpUp:
                return _hasJumpState
                    ? CreatePlayableState(_jumpStateHash, jumpStateName)
                    : default;

            case ClipState.Run:
                if (_hasRunState)
                    return CreatePlayableState(_runStateHash, runStateName);

                if (_hasWalkState)
                    return CreatePlayableState(_walkStateHash, walkStateName);

                return _hasIdleState
                    ? CreatePlayableState(_idleStateHash, idleStateName)
                    : default;

            case ClipState.Walk:
                if (_hasWalkState)
                    return CreatePlayableState(_walkStateHash, walkStateName);

                return _hasIdleState
                    ? CreatePlayableState(_idleStateHash, idleStateName)
                    : default;

            default:
                return _hasIdleState
                    ? CreatePlayableState(_idleStateHash, idleStateName)
                    : default;
        }
    }

    private PlayableState CreatePlayableState(int hash, string stateName)
    {
        return new PlayableState
        {
            Hash = hash,
            Name = stateName,
            IsValid = true
        };
    }

    private bool IsCurrentState(ClipState state)
    {
        PlayableState playableState = ResolvePlayableState(state);
        return playableState.IsValid && _currentStateHash == playableState.Hash;
    }

    private void TickDebugLog(MotionSample sample, float deltaTime)
    {
        if (!debugLogs)
            return;

        _debugLogTimer += deltaTime;
        if (_debugLogTimer < debugLogInterval)
            return;

        _debugLogTimer = 0f;
        ClipState locomotionState = SelectLocomotionState(sample, out string reason);
        Debug.Log($"[HamsterVisualClipStateDriver] locomotionDecision state={locomotionState} speed={sample.PlanarSpeed:F2} sprint={sample.IsSprintHeld} grounded={FormatGrounded(sample)} vertical={sample.VerticalVelocity:F2} moveInput={FormatMoveInput(sample)} reason={reason} current={_currentStateName} source={sample.Source} fallLandDisabled={disableFallAndLandForNow}", this);
    }

    private void WarnIfMissingState(string stateName, bool hasState)
    {
        if (hasState || string.IsNullOrWhiteSpace(stateName))
            return;

        Debug.LogWarning($"[HamsterVisualClipStateDriver:{name}] Animator Base Layer state '{stateName}' is missing. This driver will not CrossFade to that state.", this);
    }

    private void WarnMissingController()
    {
        if (_warnedMissingController)
            return;

        Debug.LogWarning($"[HamsterVisualClipStateDriver:{name}] visualAnimator.runtimeAnimatorController is missing. Driver disabled because requireAnimatorController is true.", this);
        _warnedMissingController = true;
    }

    private string FormatGrounded(MotionSample sample)
    {
        return sample.HasGroundedState ? sample.IsGrounded.ToString() : "Unknown";
    }

    private string FormatMoveInput(MotionSample sample)
    {
        return sample.HasMoveInputState ? sample.HasMoveInput.ToString() : "Unknown";
    }

    private string FormatReason(string reason)
    {
        return string.IsNullOrEmpty(reason) ? "none" : reason;
    }
}
