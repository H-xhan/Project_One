using UnityEngine;

public sealed class HamsterVisualClipStateDriver : MonoBehaviour
{
    private const int BaseLayerIndex = 0;

    [Header("References")]
    [SerializeField] private Animator visualAnimator;
    [SerializeField] private HamsterFullRagdollMotor motorStateSource;
    [SerializeField] private Rigidbody targetBody;
    [SerializeField] private bool autoFindReferences = true;

    [Header("Interaction References")]
    [SerializeField] private HamsterRagdollGrabber grabStateSource;
    [SerializeField] private bool autoFindGrabStateSource = true;

    [Header("State Names")]
    [SerializeField] private string idleStateName = "Idle";
    [SerializeField] private string walkStateName = "Walk";
    [SerializeField] private string runStateName = "Run";
    [SerializeField] private string jumpStateName = "JumpUp";

    [Header("Interaction State Names")]
    [SerializeField] private string grabStateName = "Grab";
    [SerializeField] private string carryStateName = "Carry";
    [SerializeField] private string throwStateName = "Throw";

    [Header("Carry State Names")]
    [SerializeField] private string carryIdleStateName = "CarryIdle";
    [SerializeField] private string carryWalkStateName = "CarryWalk";
    [SerializeField] private string carryRunStateName = "CarryRun";
    [SerializeField] private string carryJumpStateName = "CarryJump";

    [Header("Mode")]
    [SerializeField] private bool enableClipStateDriver = true;
    [SerializeField] private bool driveGroundLocomotion = true;
    [SerializeField] private bool driveJump = true;
    [SerializeField] private bool disableFallAndLandForNow = true;
    [SerializeField] private bool requireAnimatorController = true;

    [Header("Interaction Driver")]
    [SerializeField] private bool driveInteractionStates = false;
    [SerializeField] private bool useCarryStateWhileHolding = false;
    [SerializeField] private bool playGrabOnGrabCount = true;
    [SerializeField] private bool playThrowOnThrowCount = true;
    [SerializeField] private float grabCrossFadeDuration = 0.05f;
    [SerializeField] private float throwCrossFadeDuration = 0.04f;
    [SerializeField] private float minGrabStateTime = 0.12f;
    [SerializeField] private float maxGrabStateTime = 0.25f;
    [SerializeField] private float minThrowStateTime = 0.12f;
    [SerializeField] private float maxThrowStateTime = 0.25f;
    [SerializeField] private bool returnToLocomotionAfterInteraction = true;
    [SerializeField] private bool debugInteractionLogs = false;

    [Header("Carry Driver")]
    [SerializeField] private bool useCarryJumpState = true;
    [SerializeField] private float carryCrossFadeDuration = 0.08f;
    [SerializeField] private bool requireCarryStateMotion = true;
    [SerializeField] private bool debugCarryLogs = false;

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
    private float _carryDebugLogTimer;
    private int _lastCarryDecisionLoggedHash;
    private string _lastCarryDecisionLoggedReason;

    private int _idleStateHash;
    private int _walkStateHash;
    private int _runStateHash;
    private int _jumpStateHash;
    private int _grabStateHash;
    private int _carryStateHash;
    private int _throwStateHash;
    private int _carryIdleStateHash;
    private int _carryWalkStateHash;
    private int _carryRunStateHash;
    private int _carryJumpStateHash;
    private bool _hasIdleState;
    private bool _hasWalkState;
    private bool _hasRunState;
    private bool _hasJumpState;
    private bool _hasGrabState;
    private bool _hasCarryState;
    private bool _hasThrowState;
    private bool _hasCarryIdleState;
    private bool _hasCarryWalkState;
    private bool _hasCarryRunState;
    private bool _hasCarryJumpState;
    private int _lastObservedGrabCount;
    private int _lastObservedThrowCount;
    private int _lastObservedGrabRequestCount;
    private int _lastObservedThrowRequestCount;
    private bool _suppressNextGrabCountInteractionTrigger;
    private bool _suppressNextThrowCountInteractionTrigger;
    private bool _interactionObservationInitialized;
    private bool _interactionStateActive;
    private int _interactionStateHash;
    private string _interactionStateName;
    private float _interactionStateTimer;
    private float _interactionStateMinTime;
    private float _interactionStateMaxTime;
    private bool _externalOneShotActive;
    private int _externalOneShotStateHash;
    private string _externalOneShotStateName = string.Empty;
    private float _externalOneShotTimer;
    private float _externalOneShotMinTime;
    private float _externalOneShotMaxTime;
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

    public bool IsExternalOneShotActive => _externalOneShotActive;

    private struct MotionSample
    {
        public bool HasGroundedState;
        public bool IsGrounded;
        public bool IsSprintHeld;
        public bool HasMoveInputState;
        public bool HasMoveInput;
        public bool HasHoldingState;
        public bool IsHolding;
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

        if (autoFindReferences && (visualAnimator == null || motorStateSource == null || targetBody == null || (autoFindGrabStateSource && grabStateSource == null)))
            ResolveReferences();

        if (!CanDriveAnimator())
            return;

        if (TickExternalOneShot(deltaTime))
            return;

        MotionSample sample = ReadMotionSample();
        bool interactionHandled = TickInteractionDriver(sample, deltaTime);
        if (!interactionHandled)
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
        grabCrossFadeDuration = Mathf.Max(0f, grabCrossFadeDuration);
        throwCrossFadeDuration = Mathf.Max(0f, throwCrossFadeDuration);
        carryCrossFadeDuration = Mathf.Max(0f, carryCrossFadeDuration);
        minGrabStateTime = Mathf.Max(0f, minGrabStateTime);
        maxGrabStateTime = Mathf.Max(minGrabStateTime, maxGrabStateTime);
        minThrowStateTime = Mathf.Max(0f, minThrowStateTime);
        maxThrowStateTime = Mathf.Max(minThrowStateTime, maxThrowStateTime);
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

        if (autoFindGrabStateSource)
            ResolveGrabStateSource();
    }

    private void ResolveGrabStateSource()
    {
        if (grabStateSource != null)
            return;

        grabStateSource = GetComponent<HamsterRagdollGrabber>();

        if (grabStateSource == null)
            grabStateSource = GetComponentInParent<HamsterRagdollGrabber>();
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
        _hasGrabState = TryCacheState(grabStateName, out _grabStateHash);
        _hasCarryState = TryCacheState(carryStateName, out _carryStateHash);
        _hasThrowState = TryCacheState(throwStateName, out _throwStateHash);
        _hasCarryIdleState = TryCacheState(carryIdleStateName, out _carryIdleStateHash);
        _hasCarryWalkState = TryCacheState(carryWalkStateName, out _carryWalkStateHash);
        _hasCarryRunState = TryCacheState(carryRunStateName, out _carryRunStateHash);
        _hasCarryJumpState = TryCacheState(carryJumpStateName, out _carryJumpStateHash);

        if (visualAnimator == null || visualAnimator.runtimeAnimatorController == null)
            return;

        WarnIfMissingState(idleStateName, _hasIdleState);
        WarnIfMissingState(walkStateName, _hasWalkState);
        WarnIfMissingState(runStateName, _hasRunState);
        WarnIfMissingState(jumpStateName, _hasJumpState);

        if (!driveInteractionStates)
            return;

        WarnIfMissingState(grabStateName, _hasGrabState);
        if (useCarryStateWhileHolding)
            WarnIfMissingCarryStates();

        WarnIfMissingState(throwStateName, _hasThrowState);
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

    public bool TryPlayOneShotState(
        string stateName,
        float crossFadeDuration,
        bool requireStateMotion,
        out int stateHash,
        out string failureReason)
    {
        return TryPlayOneShotState(
            stateName,
            crossFadeDuration,
            0f,
            0f,
            requireStateMotion,
            out stateHash,
            out failureReason);
    }

    public bool TryPlayOneShotState(
        string stateName,
        float crossFadeDuration,
        float minTime,
        float maxTime,
        bool requireStateMotion,
        out int stateHash,
        out string failureReason)
    {
        stateHash = 0;
        failureReason = "none";

        if (string.IsNullOrWhiteSpace(stateName))
        {
            failureReason = "state name empty";
            return false;
        }

        if (!isActiveAndEnabled || !enableClipStateDriver)
        {
            failureReason = "driver disabled";
            return false;
        }

        if (!CanDriveAnimator())
        {
            failureReason = "animator unavailable";
            return false;
        }

        if (!TryCacheState(stateName, out stateHash))
        {
            failureReason = "state missing";
            return false;
        }

        if (!HasStateMotion(stateName, requireStateMotion, false))
        {
            failureReason = "state motion missing";
            return false;
        }

        visualAnimator.CrossFade(stateHash, Mathf.Max(0f, crossFadeDuration), BaseLayerIndex, 0f);
        _currentStateHash = stateHash;
        _currentStateName = stateName;
        _stateTimer = 0f;
        _externalOneShotActive = true;
        _externalOneShotStateHash = stateHash;
        _externalOneShotStateName = stateName;
        _externalOneShotTimer = 0f;
        _externalOneShotMinTime = Mathf.Max(0f, minTime);
        _externalOneShotMaxTime = Mathf.Max(_externalOneShotMinTime, maxTime);
        ClearInteractionState();
        return true;
    }

    public bool IsPlayingExternalOneShot(string stateName)
    {
        if (!_externalOneShotActive || string.IsNullOrWhiteSpace(stateName))
            return false;

        int hash = Animator.StringToHash(stateName);
        int baseLayerHash = Animator.StringToHash($"Base Layer.{stateName}");
        return _externalOneShotStateHash == hash ||
               _externalOneShotStateHash == baseLayerHash ||
               _externalOneShotStateName == stateName;
    }

    public void CancelExternalOneShot(string reason)
    {
        ClearExternalOneShot();
    }

    public bool TryGetAnimatorStateNormalizedTime(int stateHash, out float normalizedTime)
    {
        normalizedTime = 0f;
        if (visualAnimator == null || stateHash == 0)
            return false;

        AnimatorStateInfo currentState = visualAnimator.GetCurrentAnimatorStateInfo(BaseLayerIndex);
        if (currentState.shortNameHash == stateHash || currentState.fullPathHash == stateHash)
        {
            normalizedTime = currentState.normalizedTime;
            return true;
        }

        if (!visualAnimator.IsInTransition(BaseLayerIndex))
            return false;

        AnimatorStateInfo nextState = visualAnimator.GetNextAnimatorStateInfo(BaseLayerIndex);
        if (nextState.shortNameHash == stateHash || nextState.fullPathHash == stateHash)
        {
            normalizedTime = nextState.normalizedTime;
            return true;
        }

        return false;
    }

    private bool TickExternalOneShot(float deltaTime)
    {
        if (!_externalOneShotActive)
            return false;

        _externalOneShotTimer += Mathf.Max(0f, deltaTime);
        if (_externalOneShotTimer < _externalOneShotMinTime)
            return true;

        bool maxTimeReached = _externalOneShotMaxTime > 0f && _externalOneShotTimer >= _externalOneShotMaxTime;
        bool animatorStillInState = IsAnimatorInState(_externalOneShotStateHash);
        if (!maxTimeReached && animatorStillInState)
            return true;

        ClearExternalOneShot();
        return false;
    }

    private void ClearExternalOneShot()
    {
        _externalOneShotActive = false;
        _externalOneShotStateHash = 0;
        _externalOneShotStateName = string.Empty;
        _externalOneShotTimer = 0f;
        _externalOneShotMinTime = 0f;
        _externalOneShotMaxTime = 0f;
    }

    private void ResetRuntimeState()
    {
        _currentStateHash = 0;
        _currentStateName = string.Empty;
        _stateTimer = 0f;
        _groundedReturnTimer = 0f;
        _debugLogTimer = 0f;
        _carryDebugLogTimer = 0f;
        _lastCarryDecisionLoggedHash = 0;
        _lastCarryDecisionLoggedReason = null;
        _interactionObservationInitialized = false;
        _suppressNextGrabCountInteractionTrigger = false;
        _suppressNextThrowCountInteractionTrigger = false;
        ClearExternalOneShot();
        ClearInteractionState();

        MotionSample sample = ReadMotionSample();
        _wasGrounded = sample.HasGroundedState && sample.IsGrounded;
        InitializeInteractionObservation();
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
        bool hasHoldingState = grabStateSource != null;
        bool isHolding = hasHoldingState && grabStateSource.IsHolding;

        if (motorStateSource != null)
        {
            return new MotionSample
            {
                HasGroundedState = true,
                IsGrounded = motorStateSource.IsGrounded,
                IsSprintHeld = motorStateSource.IsSprintHeld,
                HasMoveInputState = true,
                HasMoveInput = motorStateSource.SmoothedMoveWorldDirection.sqrMagnitude > 0.0001f,
                HasHoldingState = hasHoldingState,
                IsHolding = isHolding,
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
                HasHoldingState = hasHoldingState,
                IsHolding = isHolding,
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
            HasHoldingState = hasHoldingState,
            IsHolding = isHolding,
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

            if (TryDriveCarryState(sample, true))
            {
                _groundedReturnTimer = 0f;
                return;
            }

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

        if (TryDriveCarryState(sample))
            return;

        if (sample.HasGroundedState && !grounded)
            return;

        TryCrossFade(SelectLocomotionState(sample, out string reason), locomotionCrossFadeDuration, sample, reason);
    }

    private bool TickInteractionDriver(MotionSample sample, float deltaTime)
    {
        if (!driveInteractionStates)
        {
            ClearInteractionState();
            return false;
        }

        if (autoFindGrabStateSource && grabStateSource == null)
            ResolveGrabStateSource();

        if (grabStateSource == null)
            return false;

        InitializeInteractionObservation();

        if (playThrowOnThrowCount && HasCountIncreased(grabStateSource.ThrowRequestCount, ref _lastObservedThrowRequestCount))
        {
            _suppressNextThrowCountInteractionTrigger = true;
            if (TryBeginInteractionState(
                ResolveInteractionPlayableState(throwStateName, _throwStateHash, _hasThrowState),
                throwCrossFadeDuration,
                minThrowStateTime,
                maxThrowStateTime,
                sample,
                "ThrowRequestCount"))
            {
                return true;
            }
        }

        if (playGrabOnGrabCount && HasCountIncreased(grabStateSource.GrabRequestCount, ref _lastObservedGrabRequestCount))
        {
            _suppressNextGrabCountInteractionTrigger = true;
            if (TryBeginInteractionState(
                ResolveInteractionPlayableState(grabStateName, _grabStateHash, _hasGrabState),
                grabCrossFadeDuration,
                minGrabStateTime,
                maxGrabStateTime,
                sample,
                "GrabRequestCount"))
            {
                return true;
            }
        }

        if (playThrowOnThrowCount && HasCountIncreased(grabStateSource.ThrowCount, ref _lastObservedThrowCount))
        {
            if (_suppressNextThrowCountInteractionTrigger)
            {
                _suppressNextThrowCountInteractionTrigger = false;
            }
            else if (TryBeginInteractionState(
                         ResolveInteractionPlayableState(throwStateName, _throwStateHash, _hasThrowState),
                         throwCrossFadeDuration,
                         minThrowStateTime,
                         maxThrowStateTime,
                         sample,
                         "ThrowCount"))
            {
                return true;
            }
        }

        if (playGrabOnGrabCount && HasCountIncreased(grabStateSource.GrabCount, ref _lastObservedGrabCount))
        {
            if (_suppressNextGrabCountInteractionTrigger)
            {
                _suppressNextGrabCountInteractionTrigger = false;
            }
            else if (TryBeginInteractionState(
                         ResolveInteractionPlayableState(grabStateName, _grabStateHash, _hasGrabState),
                         grabCrossFadeDuration,
                         minGrabStateTime,
                         maxGrabStateTime,
                         sample,
                         "GrabCount"))
            {
                return true;
            }
        }

        if (_interactionStateActive && TickActiveInteractionState(deltaTime))
            return true;

        return false;
    }

    private bool TryDriveCarryState(MotionSample sample, bool preferJumpState = false)
    {
        if (!TryGetCarryState(sample, out PlayableState carryState, out string reason, preferJumpState))
            return false;

        bool changedState = TryCrossFade(carryState, carryCrossFadeDuration, sample, reason);
        if (changedState && carryState.Hash == _carryJumpStateHash)
            _lastJumpTriggerTime = Time.time;

        if (debugCarryLogs && ShouldLogCarryImmediateDecision(carryState, reason, changedState))
        {
            bool alreadyCurrent = !changedState && _currentStateHash == carryState.Hash;
            LogCarryDecision(
                sample,
                carryState.Name,
                reason,
                $"changedState={changedState} alreadyCurrent={alreadyCurrent}",
                "crossFade");
        }

        return true;
    }

    private bool TryGetCarryState(MotionSample sample, out PlayableState state, out string reason, bool preferJumpState = false)
    {
        state = default;
        reason = string.Empty;

        if (!driveInteractionStates)
        {
            reason = "skip driveInteractionStates=false";
            return false;
        }

        if (!useCarryStateWhileHolding)
        {
            reason = "skip useCarryStateWhileHolding=false";
            return false;
        }

        if (!sample.HasHoldingState)
        {
            reason = "skip grabStateSource=null";
            return false;
        }

        if (!sample.IsHolding)
        {
            reason = "skip IsHolding=false";
            return false;
        }

        ClipState fallbackState = SelectLocomotionState(sample, out string fallbackReason);
        bool airborne = sample.HasGroundedState && !sample.IsGrounded;
        bool grounded = sample.HasGroundedState && sample.IsGrounded;

        if (useCarryJumpState && (preferJumpState || airborne || (!grounded && IsCurrentState(ClipState.JumpUp)) || ShouldTriggerJump(sample, grounded)))
        {
            state = CreateCarryPlayableStateIfReady(_hasCarryJumpState, _carryJumpStateHash, carryJumpStateName);
            if (!state.IsValid)
            {
                reason = $"skip CarryJump unavailable: {DescribeCarryStateUnavailable("CarryJump", _hasCarryJumpState, carryJumpStateName)} fallbackState=JumpUp";
                return false;
            }

            reason = $"selected CarryJump preferJump={preferJumpState} airborne={airborne} fallbackState={fallbackState} fallbackReason={fallbackReason}";
            return state.IsValid;
        }

        switch (fallbackState)
        {
            case ClipState.Run:
                state = CreateCarryPlayableStateIfReady(_hasCarryRunState, _carryRunStateHash, carryRunStateName);
                if (!state.IsValid)
                {
                    reason = $"skip CarryRun unavailable: {DescribeCarryStateUnavailable("CarryRun", _hasCarryRunState, carryRunStateName)} fallbackState=Run fallbackReason={fallbackReason}";
                    return false;
                }

                reason = $"selected CarryRun fallbackReason={fallbackReason}";
                return state.IsValid;

            case ClipState.Walk:
                state = CreateCarryPlayableStateIfReady(_hasCarryWalkState, _carryWalkStateHash, carryWalkStateName);
                if (!state.IsValid)
                {
                    reason = $"skip CarryWalk unavailable: {DescribeCarryStateUnavailable("CarryWalk", _hasCarryWalkState, carryWalkStateName)} fallbackState=Walk fallbackReason={fallbackReason}";
                    return false;
                }

                reason = $"selected CarryWalk fallbackReason={fallbackReason}";
                return state.IsValid;

            default:
                PlayableState carryIdle = CreateCarryPlayableStateIfReady(_hasCarryIdleState, _carryIdleStateHash, carryIdleStateName);
                if (carryIdle.IsValid)
                {
                    state = carryIdle;
                    reason = $"selected CarryIdle fallbackReason={fallbackReason}";
                    return true;
                }

                PlayableState legacyCarry = CreateCarryPlayableStateIfReady(_hasCarryState, _carryStateHash, carryStateName);
                if (legacyCarry.IsValid)
                {
                    state = legacyCarry;
                    reason = $"selected legacy Carry fallbackReason={fallbackReason}";
                    return true;
                }

                reason = $"skip CarryIdle unavailable: {DescribeCarryStateUnavailable("CarryIdle", _hasCarryIdleState, carryIdleStateName)} legacyCarry={DescribeCarryStateUnavailable("Carry", _hasCarryState, carryStateName)} fallbackState=Idle fallbackReason={fallbackReason}";
                return state.IsValid;
        }
    }

    private PlayableState CreateCarryPlayableStateIfReady(bool hasState, int stateHash, string stateName)
    {
        if (!hasState)
            return default;

        if (requireCarryStateMotion && !HasStateMotion(stateName))
            return default;

        return CreatePlayableState(stateHash, stateName);
    }

    private bool HasStateMotion(string stateName)
    {
        return HasStateMotion(stateName, requireCarryStateMotion, true);
    }

    private bool HasStateMotion(string stateName, bool requireMotion, bool allowWhenEditorStateUnavailable)
    {
        if (!requireMotion)
            return true;

#if UNITY_EDITOR
        UnityEditor.Animations.AnimatorController controller = visualAnimator != null
            ? visualAnimator.runtimeAnimatorController as UnityEditor.Animations.AnimatorController
            : null;
        if (controller == null || controller.layers == null || controller.layers.Length <= BaseLayerIndex)
            return allowWhenEditorStateUnavailable;

        UnityEditor.Animations.AnimatorStateMachine stateMachine = controller.layers[BaseLayerIndex].stateMachine;
        UnityEditor.Animations.AnimatorState state = FindEditorStateByName(stateMachine, stateName);
        return state == null ? allowWhenEditorStateUnavailable : state.motion != null;
#else
        return allowWhenEditorStateUnavailable;
#endif
    }

#if UNITY_EDITOR
    private UnityEditor.Animations.AnimatorState FindEditorStateByName(
        UnityEditor.Animations.AnimatorStateMachine stateMachine,
        string stateName)
    {
        if (stateMachine == null || string.IsNullOrWhiteSpace(stateName))
            return null;

        UnityEditor.Animations.ChildAnimatorState[] states = stateMachine.states;
        for (int i = 0; i < states.Length; i++)
        {
            UnityEditor.Animations.AnimatorState state = states[i].state;
            if (state != null && state.name == stateName)
                return state;
        }

        UnityEditor.Animations.ChildAnimatorStateMachine[] childStateMachines = stateMachine.stateMachines;
        for (int i = 0; i < childStateMachines.Length; i++)
        {
            UnityEditor.Animations.AnimatorState state = FindEditorStateByName(childStateMachines[i].stateMachine, stateName);
            if (state != null)
                return state;
        }

        return null;
    }
#endif

    private void InitializeInteractionObservation()
    {
        if (_interactionObservationInitialized || grabStateSource == null)
            return;

        _lastObservedGrabCount = grabStateSource.GrabCount;
        _lastObservedThrowCount = grabStateSource.ThrowCount;
        _lastObservedGrabRequestCount = grabStateSource.GrabRequestCount;
        _lastObservedThrowRequestCount = grabStateSource.ThrowRequestCount;
        _interactionObservationInitialized = true;
    }

    private bool HasCountIncreased(int currentCount, ref int lastObservedCount)
    {
        bool increased = currentCount > lastObservedCount;
        lastObservedCount = currentCount;
        return increased;
    }

    private bool TryBeginInteractionState(
        PlayableState playableState,
        float crossFadeDuration,
        float minStateTime,
        float maxStateTime,
        MotionSample sample,
        string reason)
    {
        if (!playableState.IsValid)
        {
            if (ShouldLogInteraction())
                Debug.Log($"[HamsterVisualClipStateDriver] Skip interaction state because it is missing. reason={reason}", this);

            return false;
        }

        TryCrossFade(playableState, crossFadeDuration, sample, reason, true);
        _interactionStateActive = true;
        _interactionStateHash = playableState.Hash;
        _interactionStateName = playableState.Name;
        _interactionStateTimer = 0f;
        _interactionStateMinTime = Mathf.Max(0f, minStateTime);
        _interactionStateMaxTime = Mathf.Max(_interactionStateMinTime, maxStateTime);

        if (debugInteractionLogs && !debugLogs)
            Debug.Log($"[HamsterVisualClipStateDriver] Interaction CrossFade {_interactionStateName} reason={reason}", this);

        return true;
    }

    private bool TickActiveInteractionState(float deltaTime)
    {
        _interactionStateTimer += deltaTime;

        if (_interactionStateTimer < _interactionStateMinTime)
            return true;

        bool maxTimeReached = _interactionStateMaxTime <= 0f || _interactionStateTimer >= _interactionStateMaxTime;
        bool animatorLeftInteractionState = !IsAnimatorInState(_interactionStateHash);
        if (!maxTimeReached && !animatorLeftInteractionState)
            return true;

        if (ShouldLogInteraction())
            Debug.Log($"[HamsterVisualClipStateDriver] interactionReturn state={_interactionStateName} timer={_interactionStateTimer:F2} maxReached={maxTimeReached} animatorLeft={animatorLeftInteractionState}", this);

        ClearInteractionState();
        return !returnToLocomotionAfterInteraction;
    }

    private bool IsAnimatorInState(int stateHash)
    {
        if (visualAnimator == null || stateHash == 0)
            return false;

        AnimatorStateInfo currentState = visualAnimator.GetCurrentAnimatorStateInfo(BaseLayerIndex);
        if (currentState.shortNameHash == stateHash || currentState.fullPathHash == stateHash)
            return true;

        if (!visualAnimator.IsInTransition(BaseLayerIndex))
            return false;

        AnimatorStateInfo nextState = visualAnimator.GetNextAnimatorStateInfo(BaseLayerIndex);
        return nextState.shortNameHash == stateHash || nextState.fullPathHash == stateHash;
    }

    private void ClearInteractionState()
    {
        _interactionStateActive = false;
        _interactionStateHash = 0;
        _interactionStateName = string.Empty;
        _interactionStateTimer = 0f;
        _interactionStateMinTime = 0f;
        _interactionStateMaxTime = 0f;
    }

    private bool ShouldLogInteraction()
    {
        return debugLogs || debugInteractionLogs;
    }

    private void WarnIfMissingCarryStates()
    {
        WarnIfMissingState(carryIdleStateName, _hasCarryIdleState);
        WarnIfMissingState(carryWalkStateName, _hasCarryWalkState);
        WarnIfMissingState(carryRunStateName, _hasCarryRunState);
        if (useCarryJumpState)
            WarnIfMissingState(carryJumpStateName, _hasCarryJumpState);

        if (requireCarryStateMotion && debugCarryLogs)
        {
            Debug.Log(
                $"[HamsterVisualClipStateDriver:{name}] requireCarryStateMotion is enabled, but runtime motion-null checks are not available here. Keep useCarryStateWhileHolding off until Carry state Motion slots are assigned.",
                this);
        }
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

        if (TryDriveCarryState(sample))
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
        return TryCrossFade(playableState, crossFadeDuration, sample, reason);
    }

    private bool TryCrossFade(
        PlayableState playableState,
        float crossFadeDuration,
        MotionSample sample,
        string reason = null,
        bool restartIfCurrent = false)
    {
        if (!playableState.IsValid)
        {
            if (debugLogs)
                Debug.Log($"[HamsterVisualClipStateDriver] Skip state because it is missing. speed={sample.PlanarSpeed:F2} sprint={sample.IsSprintHeld} grounded={FormatGrounded(sample)} vertical={sample.VerticalVelocity:F2} reason={FormatReason(reason)}", this);

            return false;
        }

        if (_currentStateHash == playableState.Hash && !restartIfCurrent)
            return false;

        visualAnimator.CrossFade(playableState.Hash, crossFadeDuration, BaseLayerIndex);
        _currentStateHash = playableState.Hash;
        _currentStateName = playableState.Name;
        _stateTimer = 0f;

        if (debugLogs)
            Debug.Log($"[HamsterVisualClipStateDriver] CrossFade {_currentStateName} speed={sample.PlanarSpeed:F2} sprint={sample.IsSprintHeld} grounded={FormatGrounded(sample)} vertical={sample.VerticalVelocity:F2} reason={FormatReason(reason)}", this);

        return true;
    }

    private PlayableState ResolveInteractionPlayableState(string stateName, int stateHash, bool hasState)
    {
        return hasState
            ? CreatePlayableState(stateHash, stateName)
            : default;
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
        if (debugLogs)
        {
            _debugLogTimer += deltaTime;
            if (_debugLogTimer >= debugLogInterval)
            {
                _debugLogTimer = 0f;
                ClipState locomotionState = SelectLocomotionState(sample, out string reason);
                Debug.Log($"[HamsterVisualClipStateDriver] locomotionDecision state={locomotionState} speed={sample.PlanarSpeed:F2} sprint={sample.IsSprintHeld} grounded={FormatGrounded(sample)} vertical={sample.VerticalVelocity:F2} moveInput={FormatMoveInput(sample)} holding={FormatHolding(sample)} reason={reason} current={_currentStateName} source={sample.Source} fallLandDisabled={disableFallAndLandForNow}", this);
            }
        }

        if (!debugCarryLogs)
            return;

        _carryDebugLogTimer += deltaTime;
        if (_carryDebugLogTimer < debugLogInterval)
            return;

        _carryDebugLogTimer = 0f;
        bool hasCarry = TryGetCarryState(sample, out PlayableState carryState, out string carryReason);
        bool alreadyCurrent = hasCarry && _currentStateHash == carryState.Hash;
        LogCarryDecision(
            sample,
            hasCarry ? carryState.Name : "None",
            carryReason,
            $"alreadyCurrent={alreadyCurrent}",
            "periodic");
    }

    private void LogCarryDecision(
        MotionSample sample,
        string selectedState,
        string reason,
        string crossFadeResult,
        string source)
    {
        if (!debugCarryLogs)
            return;

        Debug.Log(
            $"[HamsterVisualClipStateDriver] carryDecision source={source} selectedState={FormatReason(selectedState)} reason={FormatReason(reason)} crossFade={FormatReason(crossFadeResult)} holding={FormatHolding(sample)} hasHoldingState={sample.HasHoldingState} grabberHolding={FormatGrabberHolding()} driveInteractionStates={driveInteractionStates} useCarryStateWhileHolding={useCarryStateWhileHolding} useCarryJumpState={useCarryJumpState} requireCarryStateMotion={requireCarryStateMotion} availability={FormatCarryAvailability()} currentState={FormatReason(_currentStateName)} speed={sample.PlanarSpeed:F2} sprint={sample.IsSprintHeld} grounded={FormatGrounded(sample)} vertical={sample.VerticalVelocity:F2}",
            this);
    }

    private bool ShouldLogCarryImmediateDecision(PlayableState carryState, string reason, bool changedState)
    {
        if (changedState)
        {
            RememberCarryDecision(carryState, reason);
            return true;
        }

        if (_lastCarryDecisionLoggedHash == carryState.Hash && _lastCarryDecisionLoggedReason == reason)
            return false;

        RememberCarryDecision(carryState, reason);
        return true;
    }

    private void RememberCarryDecision(PlayableState carryState, string reason)
    {
        _lastCarryDecisionLoggedHash = carryState.Hash;
        _lastCarryDecisionLoggedReason = reason;
    }

    private string FormatCarryAvailability()
    {
        return string.Join(
            " ",
            FormatCarryStateAvailability("CarryIdle", _hasCarryIdleState, carryIdleStateName),
            FormatCarryStateAvailability("CarryWalk", _hasCarryWalkState, carryWalkStateName),
            FormatCarryStateAvailability("CarryRun", _hasCarryRunState, carryRunStateName),
            FormatCarryStateAvailability("CarryJump", _hasCarryJumpState, carryJumpStateName));
    }

    private string FormatCarryStateAvailability(string label, bool hasState, string stateName)
    {
        if (!hasState)
            return $"{label}(state=False,motion=Unavailable)";

        if (!requireCarryStateMotion)
            return $"{label}(state=True,motion=NotRequired)";

        return $"{label}(state=True,motion={HasStateMotion(stateName)})";
    }

    private string DescribeCarryStateUnavailable(string label, bool hasState, string stateName)
    {
        if (!hasState)
            return $"{label} state missing";

        if (requireCarryStateMotion && !HasStateMotion(stateName))
            return $"{label} motion missing";

        return $"{label} invalid";
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

    private string FormatHolding(MotionSample sample)
    {
        return sample.HasHoldingState ? sample.IsHolding.ToString() : "Unknown";
    }

    private string FormatGrabberHolding()
    {
        return grabStateSource != null ? grabStateSource.IsHolding.ToString() : "Unknown";
    }

    private string FormatReason(string reason)
    {
        return string.IsNullOrEmpty(reason) ? "none" : reason;
    }
}
