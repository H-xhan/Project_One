using Unity.Netcode;
using UnityEngine;

public sealed class HamsterVisualClipStateDriver : MonoBehaviour
{
    private const int BaseLayerIndex = 0;
    private const float ClientDiagnosticsLargeDeltaDistance = 1f;
    private const string PlayerBuildMotionFallbackGlideReason = "TraversalGlide";
    private const string PlayerBuildMotionFallbackGlideStateName = "Glide";
    private const string PlayerBuildMotionFallbackWallReason = "TraversalWall";
    private const string PlayerBuildMotionFallbackWallClingStateName = "WallCling";
    private const string PlayerBuildMotionFallbackWallUpStateName = "WallMove_Up";
    private const string PlayerBuildMotionFallbackWallDownStateName = "WallMove_Down";
    private const string PlayerBuildMotionFallbackWallLeftStateName = "WallMove_Left";
    private const string PlayerBuildMotionFallbackWallRightStateName = "WallMove_Right";
    private const string CharacterGrabPresentationReason = "CharacterGrab";
    private const string CharacterCarryPresentationReason = "CharacterCarry";

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

    [Header("Network Animation Authority")]
    [SerializeField] private bool useServerAuthoritativeNetworkAnimator = true;

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

    [Header("Client Network Locomotion")]
    [SerializeField] private bool enableClientNetworkLocomotion = true;
    [SerializeField] private float clientNetworkSpeedSmoothTime = 0.10f;
    [SerializeField] private float clientNetworkStopSpeedThreshold = 0.06f;
    [SerializeField] private float clientNetworkTeleportDistanceThreshold = 1.0f;
    [SerializeField] private float clientNetworkAirborneVerticalSpeedThreshold = 0.5f;
    [SerializeField] private float clientNetworkMaxVisualSpeed = 8.0f;
    [SerializeField] private float clientNetworkWalkEnterSpeed = 0.18f;
    [SerializeField] private float clientNetworkIdleEnterSpeed = 0.06f;
    [SerializeField] private float clientNetworkRunEnterSpeed = 1.9f;
    [SerializeField] private float clientNetworkRunExitSpeed = 1.4f;
    [SerializeField] private float clientNetworkStateStableTime = 0.12f;
    [SerializeField] private float clientNetworkMinimumLocomotionHoldTime = 0.15f;
    [SerializeField] private float clientAnimatorResyncCooldown = 0.12f;

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

    [Header("Client Locomotion Diagnostics")]
    [SerializeField] private bool debugClientLocomotionDiagnostics = false;
    [SerializeField] private float clientLocomotionDiagnosticsInterval = 0.25f;

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
    private bool _externalSustainedStateActive;
    private int _externalSustainedStateHash;
    private string _externalSustainedStateName = string.Empty;
    private string _externalSustainedStateReason = string.Empty;
    private float _externalSustainedStateTimer;
    private float _externalSustainedCrossFadeDuration;
    private PlayerInteractModule _characterGrabPresentationSource;
    private bool _characterGrabPresentationSourceResolveAttempted;
    private PlayerInteractModule _subscribedCharacterThrowSource;
    private bool _warnedMissingAnimator;
    private bool _warnedAnimatorDisabled;
    private bool _warnedMissingController;
    private bool _warnedRootMotion;
    private NetworkObject _clientNetworkLocomotionObject;
    private HamsterMotorShellRagdollRecoveryAdapter _clientNetworkLocomotionRecoveryStateSource;
    private Transform _clientNetworkLocomotionRoot;
    private Vector3 _clientNetworkPreviousRootPosition;
    private bool _clientNetworkPositionInitialized;
    private bool _clientNetworkSampleValid;
    private bool _clientNetworkMotionActive;
    private float _clientNetworkRawPlanarSpeed;
    private float _clientNetworkSmoothedPlanarSpeed;
    private float _clientNetworkSpeedSmoothVelocity;
    private float _clientNetworkVerticalSpeed;
    private bool _clientNetworkLargeDelta;
    private bool _clientNetworkFallbackApplied;
    private string _clientNetworkFallbackBlockedReason = "tracking reset";
    private string _clientNetworkMotionSampleSourceBefore = string.Empty;
    private string _clientNetworkMotionSampleSourceAfter = string.Empty;
    private ClipState _clientNetworkRawSelectedLocomotionState = ClipState.Idle;
    private ClipState _clientNetworkStableLocomotionState = ClipState.Idle;
    private bool _clientNetworkPendingLocomotionStateActive;
    private ClipState _clientNetworkPendingLocomotionState = ClipState.Idle;
    private float _clientNetworkPendingLocomotionDuration;
    private float _clientNetworkLocomotionHoldRemaining;
    private int _clientNetworkLocomotionEvaluationFrame = -1;
    private bool _clientNetworkTransitionSuppressed;
    private string _clientNetworkTransitionSuppressionReason = "none";
    private int _clientAnimatorResyncTargetHash;
    private float _clientAnimatorResyncCooldownRemaining;
    private bool _clientNetworkActualAnimatorLocomotionResolved;
    private ClipState _clientNetworkActualAnimatorLocomotionState = ClipState.Idle;
    private AnimatorLocomotionStateSource _clientNetworkActualAnimatorStateSource;
    private bool _clientNetworkActualAnimatorMatchesTarget;
    private bool _clientNetworkDriverAnimatorMismatch;
    private bool _clientAnimatorResyncRequested;
    private bool _clientAnimatorResyncSuppressed;
    private string _clientAnimatorResyncSuppressionReason = "none";
    private NetworkObject _clientDiagnosticsNetworkObject;
    private HamsterMotorShellRagdollRecoveryAdapter _clientDiagnosticsRecoveryStateSource;
    private Transform _clientDiagnosticsRoot;
    private Transform _clientDiagnosticsVisualPreviewRoot;
    private Vector3 _clientDiagnosticsPreviousRootPosition;
    private Vector3 _clientDiagnosticsPreviousRootPositionForLog;
    private Vector3 _clientDiagnosticsCurrentRootPosition;
    private Vector3 _clientDiagnosticsWorldDelta;
    private Vector3 _clientDiagnosticsPlanarDelta;
    private bool _clientDiagnosticsRootTrackingInitialized;
    private bool _clientDiagnosticsRootInitializedThisFrame;
    private bool _clientDiagnosticsRootPositionValid;
    private bool _clientDiagnosticsDeltaTimeValid;
    private bool _clientDiagnosticsLargeDelta;
    private float _clientDiagnosticsInferredPlanarSpeed;
    private float _clientDiagnosticsDeltaTime;
    private float _clientDiagnosticsNextPeriodicLogTime;
    private float _clientDiagnosticsNextCrossFadeLogTime;
    private bool _clientDiagnosticsWasEnabled;
    private bool _clientDiagnosticsPeriodicLogThisFrame;
    private bool _clientDiagnosticsAnimatorLogThisFrame;
    private int _clientDiagnosticsUpdateFrame = -1;
    private bool _clientDiagnosticsLastDecisionInitialized;
    private ClipState _clientDiagnosticsLastDecisionState;
    private string _clientDiagnosticsLastDecisionReason = string.Empty;
    private string _clientDiagnosticsLastRequestedStateName = string.Empty;
    private int _clientDiagnosticsLastRequestedStateHash;
    private bool _clientDiagnosticsLastRestartIfCurrent;
    private string _clientDiagnosticsLastCallerReason = string.Empty;
    private int _clientDiagnosticsLastCrossFadeCalledFrame = -1;
    private int _clientDiagnosticsLastCrossFadeFingerprint;
    private AnimatorDiagnosticSnapshot _clientDiagnosticsAnimatorUpdateBefore;
    private AnimatorDiagnosticSnapshot _clientDiagnosticsAnimatorUpdateAfter;

    private enum ClipState
    {
        Idle,
        Walk,
        Run,
        JumpUp
    }

    private enum AnimatorLocomotionStateSource
    {
        None,
        Current,
        Next
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

    private struct AnimatorDiagnosticSnapshot
    {
        public bool Available;
        public int ShortNameHash;
        public int FullPathHash;
        public float NormalizedTime;
        public bool InTransition;
        public int NextShortNameHash;
        public int NextFullPathHash;
        public float NextNormalizedTime;
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
        ResetClientNetworkLocomotionState();
        ResetRuntimeState();
        ResetClientLocomotionDiagnosticsState();
        RefreshCharacterGrabPresentationSubscription();

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
        RefreshCharacterGrabPresentationSubscription();
    }

    private void Update()
    {
        float deltaTime = Time.deltaTime;
        _clientNetworkTransitionSuppressed = false;
        _clientNetworkTransitionSuppressionReason = "none";
        ResetClientAnimatorResyncFrameDiagnostics();
        _stateTimer += deltaTime;

        if (debugClientLocomotionDiagnostics)
            BeginClientLocomotionDiagnosticsUpdate(deltaTime);
        else
            _clientDiagnosticsWasEnabled = false;

        if (!enableClipStateDriver)
        {
            if (debugClientLocomotionDiagnostics)
                CompleteClientLocomotionDiagnosticsUpdate(default, false, false, "enableClipStateDriver=false");
            return;
        }

        if (autoFindReferences &&
            (visualAnimator == null ||
             motorStateSource == null ||
             targetBody == null ||
             (!_characterGrabPresentationSourceResolveAttempted &&
              _characterGrabPresentationSource == null) ||
             (autoFindGrabStateSource && grabStateSource == null)))
        {
            ResolveReferences();
        }

        RefreshCharacterGrabPresentationSubscription();

        if (!CanDriveAnimator())
        {
            if (debugClientLocomotionDiagnostics)
                CompleteClientLocomotionDiagnosticsUpdate(default, false, false, "CanDriveAnimator=false");
            return;
        }

        bool characterGrabPresentationActive =
            TickCharacterGrabPresentation();

        if (TickExternalOneShot(deltaTime))
        {
            if (debugClientLocomotionDiagnostics)
                CompleteClientLocomotionDiagnosticsUpdate(default, false, true, "external one-shot active");
            return;
        }

        if (TickExternalSustainedState(deltaTime))
        {
            if (debugClientLocomotionDiagnostics)
                CompleteClientLocomotionDiagnosticsUpdate(default, false, true, "external sustained state active");
            return;
        }

        bool canDriveUpdateLocomotionAnimator = ShouldAuthoritativelyDriveUpdateLocomotionAnimator();
        MotionSample sample = ReadMotionSample();
        bool interactionHandled =
            characterGrabPresentationActive ||
            TickInteractionDriver(sample, deltaTime);
        if (!interactionHandled && canDriveUpdateLocomotionAnimator)
            TickStateDriver(sample, deltaTime);

        TickDebugLog(sample, deltaTime);

        if (sample.HasGroundedState)
            _wasGrounded = sample.IsGrounded;

        if (debugClientLocomotionDiagnostics)
        {
            CompleteClientLocomotionDiagnosticsUpdate(
                sample,
                true,
                true,
                interactionHandled
                    ? "interaction handled"
                    : canDriveUpdateLocomotionAnimator
                        ? "state driver ticked"
                        : "network animator observer; locomotion state driver skipped");
        }
    }

    private void OnDisable()
    {
        ClearCharacterGrabPresentationState(false);
        ClearCharacterGrabPresentationSubscription();
        ResetClientNetworkLocomotionState();
    }

    private void OnDestroy()
    {
        ClearCharacterGrabPresentationSubscription();
    }

    private void LateUpdate()
    {
        UpdateClientNetworkLocomotionSample(Time.deltaTime);

        if (!debugClientLocomotionDiagnostics ||
            !_clientDiagnosticsAnimatorLogThisFrame ||
            _clientDiagnosticsUpdateFrame != Time.frameCount)
        {
            return;
        }

        AnimatorDiagnosticSnapshot lateSnapshot = CaptureAnimatorDiagnosticSnapshot();
        bool updateMatchedRequest = AnimatorSnapshotMatchesHash(
            _clientDiagnosticsAnimatorUpdateAfter,
            _clientDiagnosticsLastRequestedStateHash);
        bool lateMatchedRequest = AnimatorSnapshotMatchesHash(
            lateSnapshot,
            _clientDiagnosticsLastRequestedStateHash);
        bool stateChangedAfterUpdate = !AnimatorSnapshotsHaveSameState(
            _clientDiagnosticsAnimatorUpdateAfter,
            lateSnapshot);
        bool suspectedOverwrite = _clientDiagnosticsLastRequestedStateHash != 0 &&
                                  updateMatchedRequest &&
                                  !lateMatchedRequest;

        Debug.Log(
            $"[HamsterVisualDiag/AnimatorLate] role={ResolveClientDiagnosticsRole()} frame={Time.frameCount} " +
            $"late={FormatAnimatorSnapshot(lateSnapshot)} updateAfter={FormatAnimatorSnapshot(_clientDiagnosticsAnimatorUpdateAfter)} " +
            $"driverState={FormatState(_currentStateName, _currentStateHash)} " +
            $"lastRequested={FormatState(_clientDiagnosticsLastRequestedStateName, _clientDiagnosticsLastRequestedStateHash)} " +
            $"lastCrossFadeFrame={_clientDiagnosticsLastCrossFadeCalledFrame} updateMatchedRequest={updateMatchedRequest} " +
            $"lateMatchedRequest={lateMatchedRequest} stateChangedAfterUpdate={stateChangedAfterUpdate} " +
            $"suspectedOverwrite={suspectedOverwrite}",
            this);
    }

    private void OnValidate()
    {
        minMoveSpeedForWalk = Mathf.Max(0f, minMoveSpeedForWalk);
        minSprint01ForRun = Mathf.Clamp01(minSprint01ForRun);
        minSpeedForRun = Mathf.Max(0f, minSpeedForRun);
        locomotionCrossFadeDuration = Mathf.Max(0f, locomotionCrossFadeDuration);
        minPlanarSpeedForSprintRunState = Mathf.Max(0f, minPlanarSpeedForSprintRunState);
        clientNetworkSpeedSmoothTime = Mathf.Max(0f, clientNetworkSpeedSmoothTime);
        clientNetworkStopSpeedThreshold = Mathf.Max(0f, clientNetworkStopSpeedThreshold);
        clientNetworkTeleportDistanceThreshold = Mathf.Max(0f, clientNetworkTeleportDistanceThreshold);
        clientNetworkAirborneVerticalSpeedThreshold = Mathf.Max(0f, clientNetworkAirborneVerticalSpeedThreshold);
        clientNetworkMaxVisualSpeed = Mathf.Max(0f, clientNetworkMaxVisualSpeed);
        clientNetworkWalkEnterSpeed = Mathf.Max(0f, clientNetworkWalkEnterSpeed);
        clientNetworkIdleEnterSpeed = Mathf.Max(0f, clientNetworkIdleEnterSpeed);
        clientNetworkRunEnterSpeed = Mathf.Max(0f, clientNetworkRunEnterSpeed);
        clientNetworkRunExitSpeed = Mathf.Max(0f, clientNetworkRunExitSpeed);
        clientNetworkStateStableTime = Mathf.Max(0f, clientNetworkStateStableTime);
        clientNetworkMinimumLocomotionHoldTime = Mathf.Max(0f, clientNetworkMinimumLocomotionHoldTime);
        clientAnimatorResyncCooldown = Mathf.Max(0f, clientAnimatorResyncCooldown);
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
        clientLocomotionDiagnosticsInterval = Mathf.Max(0.01f, clientLocomotionDiagnosticsInterval);
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

        if (_characterGrabPresentationSource == null)
            ResolveCharacterGrabPresentationSource();
    }

    private void ResolveGrabStateSource()
    {
        if (grabStateSource != null)
            return;

        grabStateSource = GetComponent<HamsterRagdollGrabber>();

        if (grabStateSource == null)
            grabStateSource = GetComponentInParent<HamsterRagdollGrabber>();
    }

    private void ResolveCharacterGrabPresentationSource()
    {
        if (_characterGrabPresentationSourceResolveAttempted)
            return;

        _characterGrabPresentationSourceResolveAttempted = true;
        NetworkObject owner = GetComponentInParent<NetworkObject>();
        if (owner == null)
            return;

        PlayerInteractModule[] candidates =
            owner.GetComponentsInChildren<PlayerInteractModule>(true);
        for (int i = 0; i < candidates.Length; i++)
        {
            PlayerInteractModule candidate = candidates[i];
            if (candidate == null ||
                candidate.GetComponentInParent<NetworkObject>() != owner)
            {
                continue;
            }

            _characterGrabPresentationSource = candidate;
            return;
        }
    }

    private void RefreshCharacterGrabPresentationSubscription()
    {
        if (_subscribedCharacterThrowSource == _characterGrabPresentationSource)
            return;

        ClearCharacterGrabPresentationSubscription();
        _subscribedCharacterThrowSource = _characterGrabPresentationSource;
        if (_subscribedCharacterThrowSource != null)
        {
            _subscribedCharacterThrowSource.CharacterThrowCommitted +=
                HandleCharacterThrowCommitted;
        }
    }

    private void ClearCharacterGrabPresentationSubscription()
    {
        if (_subscribedCharacterThrowSource != null)
        {
            _subscribedCharacterThrowSource.CharacterThrowCommitted -=
                HandleCharacterThrowCommitted;
        }

        _subscribedCharacterThrowSource = null;
    }

    private bool TickCharacterGrabPresentation()
    {
        if (_characterGrabPresentationSource == null)
        {
            if (ShouldAuthoritativelyDriveUpdateLocomotionAnimator())
                ClearCharacterGrabPresentationState(true);

            return false;
        }

        PlayerInteractModule.CharacterGrabPresentationPhase phase =
            _characterGrabPresentationSource
                .CurrentCharacterGrabPresentationPhase;
        bool isGrabberPresentationActive =
            phase == PlayerInteractModule.CharacterGrabPresentationPhase
                .Charging ||
            phase == PlayerInteractModule.CharacterGrabPresentationPhase
                .LiftReady ||
            phase == PlayerInteractModule.CharacterGrabPresentationPhase
                .Carrying;

        if (!ShouldAuthoritativelyDriveUpdateLocomotionAnimator())
            return isGrabberPresentationActive;

        if (!isGrabberPresentationActive)
        {
            ClearCharacterGrabPresentationState(true);
            return false;
        }

        if (phase !=
            PlayerInteractModule.CharacterGrabPresentationPhase.Carrying)
        {
            TryBeginCharacterGrabPresentationState(
                CharacterGrabPresentationReason,
                grabStateName,
                grabCrossFadeDuration,
                true,
                out _);
            return true;
        }

        MotionSample sample = ReadMotionSample();
        if (!TrySelectCarryState(
                sample,
                out PlayableState carryState,
                out _))
        {
            return true;
        }

        TryBeginCharacterGrabPresentationState(
            CharacterCarryPresentationReason,
            carryState.Name,
            carryCrossFadeDuration,
            requireCarryStateMotion,
            out _);
        return true;
    }

    private bool TryBeginCharacterGrabPresentationState(
        string reason,
        string stateName,
        float crossFadeDuration,
        bool requireStateMotion,
        out string failureReason)
    {
        if (_externalSustainedStateActive &&
            _externalSustainedStateReason != reason &&
            IsCharacterGrabPresentationReason(
                _externalSustainedStateReason))
        {
            ClearExternalSustainedState();
        }

        bool started = TryBeginExternalSustainedState(
            reason,
            stateName,
            crossFadeDuration,
            requireStateMotion,
            out failureReason);
        if (!started && ShouldLogInteraction())
        {
            Debug.Log(
                $"[HamsterVisualClipStateDriver] Character grab presentation skipped state={stateName} reason={reason} failure={failureReason}",
                this);
        }

        return started;
    }

    private void ClearCharacterGrabPresentationState(
        bool returnToLocomotion)
    {
        if (!_externalSustainedStateActive ||
            !IsCharacterGrabPresentationReason(
                _externalSustainedStateReason))
        {
            return;
        }

        string reason = _externalSustainedStateReason;
        if (returnToLocomotion)
            EndExternalSustainedState(reason);
        else
            ClearExternalSustainedState();
    }

    private static bool IsCharacterGrabPresentationReason(
        string reason)
    {
        return reason == CharacterGrabPresentationReason ||
               reason == CharacterCarryPresentationReason;
    }

    private void HandleCharacterThrowCommitted()
    {
        if (!ShouldAuthoritativelyDriveUpdateLocomotionAnimator())
            return;

        ClearCharacterGrabPresentationState(false);
        if (TryPlayOneShotState(
                throwStateName,
                throwCrossFadeDuration,
                minThrowStateTime,
                maxThrowStateTime,
                true,
                out _,
                out string failureReason))
        {
            return;
        }

        if (ShouldLogInteraction())
        {
            Debug.Log(
                $"[HamsterVisualClipStateDriver] Character throw presentation skipped failure={failureReason}",
                this);
        }
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
        if (!CanPlayOneShotState(stateName, requireStateMotion, out stateHash, out failureReason))
            return false;

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
        ResetClientAnimatorResyncState();
        ClearInteractionState();
        return true;
    }

    public bool CanPlayOneShotState(
        string stateName,
        bool requireStateMotion,
        out int stateHash,
        out string failureReason)
    {
        return CanPlayExternalState(
            stateName,
            requireStateMotion,
            true,
            "one-shot",
            out stateHash,
            out failureReason);
    }

    private bool CanPlayExternalState(
        string stateName,
        bool requireStateMotion,
        bool allowPlayerBuildStateExistenceFallback,
        string validationContext,
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

        if (!HasStateMotion(
                stateName,
                requireStateMotion,
                false,
                allowPlayerBuildStateExistenceFallback,
                out bool acceptedPlayerBuildStateExistence))
        {
            failureReason = "state motion missing";
            return false;
        }

        if (acceptedPlayerBuildStateExistence && debugLogs)
        {
            Debug.Log(
                $"[HamsterVisualClipStateDriver:{name}] runtime motion verification unavailable; state existence accepted state={stateName} hash={stateHash} context={validationContext}",
                this);
        }

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

    public bool TryBeginExternalSustainedState(
        string reason,
        string stateName,
        float crossFadeDuration,
        bool requireStateMotion,
        out string failureReason)
    {
        failureReason = "none";

        if (string.IsNullOrWhiteSpace(reason))
        {
            failureReason = "reason empty";
            return false;
        }

        if (string.IsNullOrWhiteSpace(stateName))
        {
            failureReason = "state name empty";
            return false;
        }

        bool allowPlayerBuildStateExistenceFallback =
            IsPlayerBuildMotionStateExistenceFallbackAllowed(
                reason,
                stateName);

        if (_externalOneShotActive)
        {
            failureReason = "one-shot active";
            return false;
        }

        if (_externalSustainedStateActive)
        {
            if (_externalSustainedStateReason == reason && _externalSustainedStateName == stateName)
                return true;

            if (_externalSustainedStateReason == reason)
            {
                if (!CanPlayExternalState(
                        stateName,
                        requireStateMotion,
                        allowPlayerBuildStateExistenceFallback,
                        reason,
                        out int replacementStateHash,
                        out failureReason))
                    return false;

                visualAnimator.CrossFade(replacementStateHash, Mathf.Max(0f, crossFadeDuration), BaseLayerIndex, 0f);
                _currentStateHash = replacementStateHash;
                _currentStateName = stateName;
                _stateTimer = 0f;
                _externalSustainedStateHash = replacementStateHash;
                _externalSustainedStateName = stateName;
                _externalSustainedStateTimer = 0f;
                _externalSustainedCrossFadeDuration = Mathf.Max(0f, crossFadeDuration);
                ResetClientAnimatorResyncState();
                ClearInteractionState();
                return true;
            }

            failureReason = $"sustained state active reason={_externalSustainedStateReason} state={_externalSustainedStateName}";
            return false;
        }

        if (!CanPlayExternalState(
                stateName,
                requireStateMotion,
                allowPlayerBuildStateExistenceFallback,
                reason,
                out int stateHash,
                out failureReason))
            return false;

        visualAnimator.CrossFade(stateHash, Mathf.Max(0f, crossFadeDuration), BaseLayerIndex, 0f);
        _currentStateHash = stateHash;
        _currentStateName = stateName;
        _stateTimer = 0f;
        _externalSustainedStateActive = true;
        _externalSustainedStateHash = stateHash;
        _externalSustainedStateName = stateName;
        _externalSustainedStateReason = reason;
        _externalSustainedStateTimer = 0f;
        _externalSustainedCrossFadeDuration = Mathf.Max(0f, crossFadeDuration);
        ResetClientAnimatorResyncState();
        ClearInteractionState();
        return true;
    }

    private bool IsPlayerBuildMotionStateExistenceFallbackAllowed(
        string reason,
        string stateName)
    {
        if (reason == CharacterGrabPresentationReason)
            return stateName == grabStateName;

        if (reason == CharacterCarryPresentationReason)
        {
            return stateName == carryIdleStateName ||
                   stateName == carryWalkStateName ||
                   stateName == carryRunStateName ||
                   stateName == carryJumpStateName;
        }

        if (reason == PlayerBuildMotionFallbackGlideReason)
            return stateName == PlayerBuildMotionFallbackGlideStateName;

        if (reason != PlayerBuildMotionFallbackWallReason)
            return false;

        return stateName == PlayerBuildMotionFallbackWallClingStateName ||
               stateName == PlayerBuildMotionFallbackWallUpStateName ||
               stateName == PlayerBuildMotionFallbackWallDownStateName ||
               stateName == PlayerBuildMotionFallbackWallLeftStateName ||
               stateName == PlayerBuildMotionFallbackWallRightStateName;
    }

    public void EndExternalSustainedState(string reason)
    {
        if (!_externalSustainedStateActive)
            return;

        if (_externalSustainedStateReason != reason)
            return;

        ClearExternalSustainedState();
        ReturnFromExternalSustainedState(reason);
    }

    public bool IsExternalSustainedStateActive(string reason)
    {
        return _externalSustainedStateActive && _externalSustainedStateReason == reason;
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

    private bool TickExternalSustainedState(float deltaTime)
    {
        if (!_externalSustainedStateActive)
            return false;

        _externalSustainedStateTimer += Mathf.Max(0f, deltaTime);
        if (!IsAnimatorInState(_externalSustainedStateHash))
        {
            visualAnimator.CrossFade(
                _externalSustainedStateHash,
                _externalSustainedCrossFadeDuration,
                BaseLayerIndex,
                0f);
            _currentStateHash = _externalSustainedStateHash;
            _currentStateName = _externalSustainedStateName;
            _stateTimer = 0f;
        }

        return true;
    }

    private void ClearExternalSustainedState()
    {
        _externalSustainedStateActive = false;
        _externalSustainedStateHash = 0;
        _externalSustainedStateName = string.Empty;
        _externalSustainedStateReason = string.Empty;
        _externalSustainedStateTimer = 0f;
        _externalSustainedCrossFadeDuration = 0f;
    }

    private void ReturnFromExternalSustainedState(string reason)
    {
        if (_externalOneShotActive || !enableClipStateDriver || !CanDriveAnimator())
            return;

        MotionSample sample = ReadMotionSample();
        bool grounded = sample.HasGroundedState && sample.IsGrounded;
        if (TryDriveCarryState(sample, sample.HasGroundedState && !grounded))
            return;

        if (sample.HasGroundedState && !grounded)
        {
            TryCrossFade(ClipState.JumpUp, jumpCrossFadeDuration, sample, reason);
            return;
        }

        if (!driveGroundLocomotion)
            return;

        TryCrossFade(SelectLocomotionState(sample, out string locomotionReason), locomotionCrossFadeDuration, sample, locomotionReason);
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
        ClearExternalSustainedState();
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

    private bool ShouldAuthoritativelyDriveUpdateLocomotionAnimator()
    {
        if (!useServerAuthoritativeNetworkAnimator)
            return true;

        NetworkManager networkManager = NetworkManager.Singleton;
        if (networkManager == null || !networkManager.IsListening)
            return true;

        NetworkObject networkObject = GetComponentInParent<NetworkObject>();
        if (networkObject == null || !networkObject.IsSpawned)
            return true;

        return networkManager.IsServer;
    }

    private MotionSample ReadMotionSample()
    {
        bool hasHoldingState = grabStateSource != null;
        bool isHolding = hasHoldingState && grabStateSource.IsHolding;

        if (motorStateSource != null)
        {
            MotionSample sample = new MotionSample
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

            return ApplyClientNetworkLocomotionSample(sample);
        }

        if (targetBody != null)
        {
            Vector3 velocity = targetBody.linearVelocity;
            Vector3 planarVelocity = velocity;
            planarVelocity.y = 0f;

            MotionSample sample = new MotionSample
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

            return ApplyClientNetworkLocomotionSample(sample);
        }

        MotionSample fallbackSample = new MotionSample
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

        return ApplyClientNetworkLocomotionSample(fallbackSample);
    }

    private void UpdateClientNetworkLocomotionSample(float deltaTime)
    {
        NetworkManager networkManager = NetworkManager.Singleton;
        if (!enableClientNetworkLocomotion ||
            networkManager == null ||
            !networkManager.IsListening ||
            !networkManager.IsClient ||
            networkManager.IsServer)
        {
            ResetClientNetworkLocomotionState();
            return;
        }

        NetworkObject resolvedNetworkObject = GetComponentInParent<NetworkObject>();
        Transform resolvedRoot = resolvedNetworkObject != null
            ? resolvedNetworkObject.transform
            : null;
        if (resolvedNetworkObject == null || !resolvedNetworkObject.IsSpawned || resolvedRoot == null)
        {
            ResetClientNetworkLocomotionState();
            return;
        }

        bool referenceChanged = _clientNetworkLocomotionObject != resolvedNetworkObject ||
                                _clientNetworkLocomotionRoot != resolvedRoot;
        if (referenceChanged)
        {
            ResetClientNetworkLocomotionState();
            _clientNetworkLocomotionObject = resolvedNetworkObject;
            _clientNetworkLocomotionRoot = resolvedRoot;
        }

        Vector3 currentPosition = resolvedRoot.position;
        if (!IsFinite(currentPosition))
        {
            ResetClientNetworkLocomotionSampleState();
            _clientNetworkPreviousRootPosition = Vector3.zero;
            _clientNetworkPositionInitialized = false;
            return;
        }

        if (referenceChanged || !_clientNetworkPositionInitialized)
        {
            _clientNetworkPreviousRootPosition = currentPosition;
            _clientNetworkPositionInitialized = true;
            ResetClientNetworkLocomotionSampleState();
            return;
        }

        Vector3 worldDelta = currentPosition - _clientNetworkPreviousRootPosition;
        _clientNetworkPreviousRootPosition = currentPosition;
        if (!IsFinite(deltaTime) || deltaTime <= 0f || !IsFinite(worldDelta))
        {
            ResetClientNetworkLocomotionSampleState();
            return;
        }

        float worldDistance = worldDelta.magnitude;
        if (!IsFinite(worldDistance) || !IsFinite(clientNetworkTeleportDistanceThreshold))
        {
            ResetClientNetworkLocomotionSampleState();
            return;
        }

        if (worldDistance > clientNetworkTeleportDistanceThreshold)
        {
            ResetClientNetworkLocomotionSampleState();
            _clientNetworkLargeDelta = true;
            return;
        }

        Vector3 planarDelta = Vector3.ProjectOnPlane(worldDelta, Vector3.up);
        float planarSpeed = planarDelta.magnitude / deltaTime;
        float verticalSpeed = worldDelta.y / deltaTime;
        if (!IsFinite(planarDelta) || !IsFinite(planarSpeed) || !IsFinite(verticalSpeed) ||
            !IsFinite(clientNetworkMaxVisualSpeed))
        {
            ResetClientNetworkLocomotionSampleState();
            return;
        }

        float rawPlanarSpeed = Mathf.Clamp(planarSpeed, 0f, clientNetworkMaxVisualSpeed);
        float smoothedPlanarSpeed;
        if (clientNetworkSpeedSmoothTime > 0f)
        {
            smoothedPlanarSpeed = Mathf.SmoothDamp(
                _clientNetworkSmoothedPlanarSpeed,
                rawPlanarSpeed,
                ref _clientNetworkSpeedSmoothVelocity,
                clientNetworkSpeedSmoothTime,
                float.PositiveInfinity,
                deltaTime);
        }
        else
        {
            smoothedPlanarSpeed = rawPlanarSpeed;
            _clientNetworkSpeedSmoothVelocity = 0f;
        }

        if (!IsFinite(smoothedPlanarSpeed) || !IsFinite(_clientNetworkSpeedSmoothVelocity))
        {
            ResetClientNetworkLocomotionSampleState();
            return;
        }

        _clientNetworkRawPlanarSpeed = rawPlanarSpeed;
        _clientNetworkSmoothedPlanarSpeed = Mathf.Clamp(
            smoothedPlanarSpeed,
            0f,
            clientNetworkMaxVisualSpeed);
        _clientNetworkVerticalSpeed = verticalSpeed;
        _clientNetworkSampleValid = true;
        _clientNetworkLargeDelta = false;

        if (!_clientNetworkMotionActive)
        {
            if (_clientNetworkSmoothedPlanarSpeed >= minMoveSpeedForWalk)
                _clientNetworkMotionActive = true;
        }
        else if (_clientNetworkSmoothedPlanarSpeed < clientNetworkStopSpeedThreshold)
        {
            _clientNetworkMotionActive = false;
        }
    }

    private MotionSample ApplyClientNetworkLocomotionSample(MotionSample sample)
    {
        _clientNetworkFallbackApplied = false;
        _clientNetworkMotionSampleSourceBefore = sample.Source ?? string.Empty;
        _clientNetworkMotionSampleSourceAfter = _clientNetworkMotionSampleSourceBefore;

        if (!enableClientNetworkLocomotion)
            return KeepOriginalClientNetworkMotionSample(sample, "fallback disabled");

        NetworkManager networkManager = NetworkManager.Singleton;
        if (networkManager == null ||
            !networkManager.IsListening ||
            !networkManager.IsClient ||
            networkManager.IsServer)
        {
            return KeepOriginalClientNetworkMotionSample(sample, "not non-server client");
        }

        NetworkObject currentNetworkObject = GetComponentInParent<NetworkObject>();
        if (_clientNetworkLocomotionObject == null ||
            currentNetworkObject != _clientNetworkLocomotionObject ||
            !_clientNetworkLocomotionObject.IsSpawned ||
            _clientNetworkLocomotionRoot == null ||
            _clientNetworkLocomotionObject.transform != _clientNetworkLocomotionRoot)
        {
            return KeepOriginalClientNetworkMotionSample(sample, "network root unavailable");
        }

        if (!_clientNetworkSampleValid)
            return KeepOriginalClientNetworkMotionSample(sample, "network sample invalid");

        if (_externalOneShotActive)
            return KeepOriginalClientNetworkMotionSample(sample, "external one-shot active");

        if (_externalSustainedStateActive)
            return KeepOriginalClientNetworkMotionSample(sample, "external sustained state active");

        if (_interactionStateActive)
            return KeepOriginalClientNetworkMotionSample(sample, "interaction state active");

        if (_clientNetworkLocomotionRecoveryStateSource == null)
        {
            _clientNetworkLocomotionRecoveryStateSource =
                GetComponentInParent<HamsterMotorShellRagdollRecoveryAdapter>();
        }

        if (_clientNetworkLocomotionRecoveryStateSource != null &&
            _clientNetworkLocomotionRecoveryStateSource.IsKnockedOrRecovering)
        {
            return KeepOriginalClientNetworkMotionSample(sample, "knocked or recovering");
        }

        if (_clientNetworkLocomotionRecoveryStateSource != null &&
            _clientNetworkLocomotionRecoveryStateSource.IsLiquidSwept)
        {
            return KeepOriginalClientNetworkMotionSample(sample, "liquid sweep active");
        }

        if (motorStateSource != null && motorStateSource.IsExternalControlLocked)
            return KeepOriginalClientNetworkMotionSample(sample, "external control locked");

        if (motorStateSource != null && motorStateSource.ExternalMovementControlScale < 0.999f)
            return KeepOriginalClientNetworkMotionSample(sample, "external movement limited");

        if (sample.HasGroundedState && !sample.IsGrounded)
            return KeepOriginalClientNetworkMotionSample(sample, "motor sample airborne");

        if (Mathf.Abs(_clientNetworkVerticalSpeed) >= clientNetworkAirborneVerticalSpeedThreshold)
            return KeepOriginalClientNetworkMotionSample(sample, "network root vertical motion");

        sample.HasMoveInputState = true;
        sample.HasMoveInput = _clientNetworkMotionActive;
        sample.PlanarSpeed = _clientNetworkMotionActive
            ? _clientNetworkSmoothedPlanarSpeed
            : 0f;
        sample.IsSprintHeld = _clientNetworkMotionActive &&
                              _clientNetworkSmoothedPlanarSpeed >= minSpeedForRun;
        sample.Source = "Motor+ClientNetworkRoot";

        _clientNetworkFallbackApplied = true;
        _clientNetworkFallbackBlockedReason = "none";
        _clientNetworkMotionSampleSourceAfter = sample.Source;
        return sample;
    }

    private MotionSample KeepOriginalClientNetworkMotionSample(MotionSample sample, string blockedReason)
    {
        _clientNetworkFallbackApplied = false;
        _clientNetworkFallbackBlockedReason = blockedReason;
        _clientNetworkMotionSampleSourceAfter = sample.Source ?? string.Empty;
        return sample;
    }

    private void ResetClientNetworkLocomotionState()
    {
        _clientNetworkLocomotionObject = null;
        _clientNetworkLocomotionRecoveryStateSource = null;
        _clientNetworkLocomotionRoot = null;
        _clientNetworkPreviousRootPosition = Vector3.zero;
        _clientNetworkPositionInitialized = false;
        ResetClientNetworkLocomotionSampleState();
        _clientNetworkFallbackApplied = false;
        _clientNetworkFallbackBlockedReason = "tracking reset";
        _clientNetworkMotionSampleSourceBefore = string.Empty;
        _clientNetworkMotionSampleSourceAfter = string.Empty;
    }

    private void ResetClientNetworkLocomotionSampleState()
    {
        _clientNetworkSampleValid = false;
        _clientNetworkMotionActive = false;
        _clientNetworkRawPlanarSpeed = 0f;
        _clientNetworkSmoothedPlanarSpeed = 0f;
        _clientNetworkSpeedSmoothVelocity = 0f;
        _clientNetworkVerticalSpeed = 0f;
        _clientNetworkLargeDelta = false;
        ResetClientNetworkLocomotionStabilityState();
    }

    private void ResetClientNetworkLocomotionStabilityState()
    {
        _clientNetworkRawSelectedLocomotionState = ClipState.Idle;
        _clientNetworkStableLocomotionState = ClipState.Idle;
        ClearClientNetworkPendingLocomotionState();
        _clientNetworkLocomotionHoldRemaining = 0f;
        _clientNetworkLocomotionEvaluationFrame = -1;
        _clientNetworkTransitionSuppressed = false;
        _clientNetworkTransitionSuppressionReason = "none";
        ResetClientAnimatorResyncState();
    }

    private void ResetClientAnimatorResyncState()
    {
        _clientAnimatorResyncTargetHash = 0;
        _clientAnimatorResyncCooldownRemaining = 0f;
        ResetClientAnimatorResyncFrameDiagnostics();
    }

    private void ResetClientAnimatorResyncFrameDiagnostics()
    {
        _clientNetworkActualAnimatorLocomotionResolved = false;
        _clientNetworkActualAnimatorLocomotionState = ClipState.Idle;
        _clientNetworkActualAnimatorStateSource = AnimatorLocomotionStateSource.None;
        _clientNetworkActualAnimatorMatchesTarget = false;
        _clientNetworkDriverAnimatorMismatch = false;
        _clientAnimatorResyncRequested = false;
        _clientAnimatorResyncSuppressed = false;
        _clientAnimatorResyncSuppressionReason = "none";
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

        ClipState rawSelectedState = SelectLocomotionState(sample, out string rawSelectionReason);
        if (TryResolveClientNetworkStableLocomotionState(
                sample,
                rawSelectedState,
                deltaTime,
                out ClipState stableSelectedState,
                out string stableSelectionReason))
        {
            TryCrossFadeClientNetworkLocomotion(
                stableSelectedState,
                locomotionCrossFadeDuration,
                sample,
                stableSelectionReason,
                deltaTime);
            return;
        }

        TryCrossFade(rawSelectedState, locomotionCrossFadeDuration, sample, rawSelectionReason);
    }

    private bool TryResolveClientNetworkStableLocomotionState(
        MotionSample sample,
        ClipState rawSelectedState,
        float deltaTime,
        out ClipState stableSelectedState,
        out string reason)
    {
        stableSelectedState = rawSelectedState;
        reason = null;
        if (sample.Source != "Motor+ClientNetworkRoot")
            return false;

        if (_clientNetworkLocomotionEvaluationFrame >= 0 &&
            _clientNetworkLocomotionEvaluationFrame != Time.frameCount - 1)
        {
            RebaseClientNetworkStableLocomotionStateFromDriver();
        }

        _clientNetworkLocomotionEvaluationFrame = Time.frameCount;
        _clientNetworkRawSelectedLocomotionState = rawSelectedState;

        float safeDeltaTime = IsFinite(deltaTime) ? Mathf.Max(0f, deltaTime) : 0f;
        _clientNetworkLocomotionHoldRemaining = Mathf.Max(
            0f,
            _clientNetworkLocomotionHoldRemaining - safeDeltaTime);

        ClipState candidateState = SelectClientNetworkLocomotionCandidate(
            _clientNetworkStableLocomotionState,
            sample.PlanarSpeed);
        if (candidateState == _clientNetworkStableLocomotionState)
        {
            ClearClientNetworkPendingLocomotionState();
            stableSelectedState = _clientNetworkStableLocomotionState;
            reason = "client network hysteresis stable";
            return true;
        }

        if (!_clientNetworkPendingLocomotionStateActive ||
            _clientNetworkPendingLocomotionState != candidateState)
        {
            _clientNetworkPendingLocomotionStateActive = true;
            _clientNetworkPendingLocomotionState = candidateState;
            _clientNetworkPendingLocomotionDuration = 0f;
        }
        else
        {
            _clientNetworkPendingLocomotionDuration += safeDeltaTime;
        }

        stableSelectedState = _clientNetworkStableLocomotionState;
        if (_clientNetworkPendingLocomotionDuration < clientNetworkStateStableTime)
        {
            reason = "client network pending stable time";
            return true;
        }

        if (_clientNetworkLocomotionHoldRemaining > 0f)
        {
            reason = "client network minimum hold";
            return true;
        }

        if (TryGetClientNetworkLocomotionTransition(out _))
        {
            reason = "client network transition settling";
            return true;
        }

        _clientNetworkStableLocomotionState = candidateState;
        _clientNetworkLocomotionHoldRemaining = candidateState == ClipState.Idle
            ? 0f
            : clientNetworkMinimumLocomotionHoldTime;
        ClearClientNetworkPendingLocomotionState();
        stableSelectedState = _clientNetworkStableLocomotionState;
        reason = "client network stable state committed";
        return true;
    }

    private ClipState SelectClientNetworkLocomotionCandidate(
        ClipState stableState,
        float planarSpeed)
    {
        float speed = Mathf.Max(0f, planarSpeed);
        switch (stableState)
        {
            case ClipState.Idle:
                return speed >= clientNetworkWalkEnterSpeed
                    ? ClipState.Walk
                    : ClipState.Idle;

            case ClipState.Walk:
                if (speed < clientNetworkIdleEnterSpeed)
                    return ClipState.Idle;

                return speed >= clientNetworkRunEnterSpeed
                    ? ClipState.Run
                    : ClipState.Walk;

            case ClipState.Run:
                return speed < clientNetworkRunExitSpeed
                    ? ClipState.Walk
                    : ClipState.Run;

            default:
                return ClipState.Idle;
        }
    }

    private void RebaseClientNetworkStableLocomotionStateFromDriver()
    {
        bool hadStableEvaluation = _clientNetworkLocomotionEvaluationFrame >= 0;
        ResetClientAnimatorResyncState();

        if (TryResolveActualAnimatorLocomotionState(
                out ClipState actualState,
                out int actualStateHash,
                out _))
        {
            _clientNetworkStableLocomotionState = actualState;
            SynchronizeDriverStateWithActualAnimator(actualState, actualStateHash);
        }
        else if (!hadStableEvaluation)
        {
            _clientNetworkStableLocomotionState = ClipState.Idle;
        }

        ClearClientNetworkPendingLocomotionState();
        _clientNetworkLocomotionHoldRemaining = _clientNetworkStableLocomotionState == ClipState.Idle
            ? 0f
            : clientNetworkMinimumLocomotionHoldTime;
    }

    private void ClearClientNetworkPendingLocomotionState()
    {
        _clientNetworkPendingLocomotionStateActive = false;
        _clientNetworkPendingLocomotionState = ClipState.Idle;
        _clientNetworkPendingLocomotionDuration = 0f;
    }

    private bool TryCrossFadeClientNetworkLocomotion(
        ClipState desiredState,
        float crossFadeDuration,
        MotionSample sample,
        string reason,
        float deltaTime)
    {
        PlayableState playableState = ResolvePlayableState(desiredState);
        if (!playableState.IsValid)
            return TryCrossFade(playableState, crossFadeDuration, sample, reason);

        if (!CanUseClientAnimatorResync(sample))
        {
            ResetClientAnimatorResyncState();
            if (TryGetClientNetworkLocomotionTransition(out AnimatorStateInfo nextState))
            {
                bool nextMatchesTarget = AnimatorStateMatchesHash(nextState, playableState.Hash);
                _clientNetworkTransitionSuppressed = true;
                _clientNetworkTransitionSuppressionReason = nextMatchesTarget
                    ? "transition already targets stable locomotion"
                    : "locomotion transition in progress";
                LogSuppressedClientNetworkLocomotionCrossFade(
                    playableState,
                    crossFadeDuration,
                    sample,
                    reason,
                    _clientNetworkTransitionSuppressionReason);
                return false;
            }

            return TryCrossFade(playableState, crossFadeDuration, sample, reason);
        }

        float safeDeltaTime = IsFinite(deltaTime) ? Mathf.Max(0f, deltaTime) : 0f;
        _clientAnimatorResyncCooldownRemaining = Mathf.Max(
            0f,
            _clientAnimatorResyncCooldownRemaining - safeDeltaTime);
        if (_clientAnimatorResyncTargetHash != playableState.Hash)
        {
            _clientAnimatorResyncTargetHash = playableState.Hash;
            _clientAnimatorResyncCooldownRemaining = 0f;
        }

        _clientNetworkActualAnimatorLocomotionResolved = TryResolveActualAnimatorLocomotionState(
            out _clientNetworkActualAnimatorLocomotionState,
            out int actualAnimatorStateHash,
            out _clientNetworkActualAnimatorStateSource);
        _clientNetworkActualAnimatorMatchesTarget = AnimatorHasOrTargetsState(playableState.Hash);
        _clientNetworkDriverAnimatorMismatch = _clientNetworkActualAnimatorLocomotionResolved
            ? _currentStateHash != actualAnimatorStateHash
            : _currentStateHash == playableState.Hash && !_clientNetworkActualAnimatorMatchesTarget;

        bool animatorInTransition = visualAnimator.IsInTransition(BaseLayerIndex);
        if (_clientNetworkActualAnimatorMatchesTarget)
        {
            SynchronizeDriverStateWithActualAnimator(desiredState, playableState.Hash);

            _clientNetworkDriverAnimatorMismatch = _currentStateHash != playableState.Hash;
            _clientAnimatorResyncTargetHash = 0;
            _clientAnimatorResyncCooldownRemaining = 0f;
            _clientAnimatorResyncSuppressed = true;
            _clientAnimatorResyncSuppressionReason = "animator already has target locomotion";
            if (animatorInTransition)
            {
                _clientNetworkTransitionSuppressed = true;
                _clientNetworkTransitionSuppressionReason = "animator already has target locomotion";
            }

            LogSuppressedClientNetworkLocomotionCrossFade(
                playableState,
                crossFadeDuration,
                sample,
                reason,
                "animator already has target locomotion");
            return false;
        }

        if (animatorInTransition)
        {
            _clientNetworkTransitionSuppressed = true;
            _clientNetworkTransitionSuppressionReason = "different animator transition in progress";
            _clientAnimatorResyncSuppressed = true;
            _clientAnimatorResyncSuppressionReason = "different animator transition in progress";
            LogSuppressedClientNetworkLocomotionCrossFade(
                playableState,
                crossFadeDuration,
                sample,
                reason,
                _clientAnimatorResyncSuppressionReason);
            return false;
        }

        bool restartForAnimatorMismatch = _currentStateHash == playableState.Hash;
        if (restartForAnimatorMismatch && _clientAnimatorResyncCooldownRemaining > 0f)
        {
            _clientAnimatorResyncSuppressed = true;
            _clientAnimatorResyncSuppressionReason = "animator resync cooldown";
            LogSuppressedClientNetworkLocomotionCrossFade(
                playableState,
                crossFadeDuration,
                sample,
                reason,
                _clientAnimatorResyncSuppressionReason);
            return false;
        }

        string crossFadeReason = restartForAnimatorMismatch
            ? "client animator locomotion resync"
            : reason;
        _clientAnimatorResyncRequested = restartForAnimatorMismatch;
        bool crossFadeCalled = TryCrossFade(
            playableState,
            crossFadeDuration,
            sample,
            crossFadeReason,
            restartForAnimatorMismatch);
        if (crossFadeCalled)
        {
            _clientAnimatorResyncTargetHash = playableState.Hash;
            _clientAnimatorResyncCooldownRemaining = clientAnimatorResyncCooldown;
        }
        else
            _clientAnimatorResyncRequested = false;

        return crossFadeCalled;
    }

    private bool CanUseClientAnimatorResync(MotionSample sample)
    {
        if (sample.Source != "Motor+ClientNetworkRoot" ||
            !sample.HasGroundedState ||
            !sample.IsGrounded ||
            !_clientNetworkSampleValid ||
            _clientNetworkLocomotionObject == null ||
            !_clientNetworkLocomotionObject.IsSpawned ||
            _clientNetworkLocomotionRoot == null)
        {
            return false;
        }

        NetworkManager networkManager = NetworkManager.Singleton;
        if (networkManager == null ||
            !networkManager.IsListening ||
            !networkManager.IsClient ||
            networkManager.IsServer)
        {
            return false;
        }

        if (visualAnimator == null ||
            !visualAnimator.enabled ||
            visualAnimator.runtimeAnimatorController == null ||
            visualAnimator.layerCount <= BaseLayerIndex ||
            _externalOneShotActive ||
            _externalSustainedStateActive ||
            _interactionStateActive)
        {
            return false;
        }

        if (_clientNetworkLocomotionRecoveryStateSource != null &&
            (_clientNetworkLocomotionRecoveryStateSource.IsKnockedOrRecovering ||
             _clientNetworkLocomotionRecoveryStateSource.IsLiquidSwept))
        {
            return false;
        }

        if (motorStateSource != null &&
            (motorStateSource.IsExternalControlLocked ||
             motorStateSource.ExternalMovementControlScale < 0.999f))
        {
            return false;
        }

        return Mathf.Abs(_clientNetworkVerticalSpeed) < clientNetworkAirborneVerticalSpeedThreshold;
    }

    private bool TryResolveActualAnimatorLocomotionState(
        out ClipState actualState,
        out int actualStateHash,
        out AnimatorLocomotionStateSource resolvedSource)
    {
        actualState = ClipState.Idle;
        actualStateHash = 0;
        resolvedSource = AnimatorLocomotionStateSource.None;
        if (visualAnimator == null ||
            !visualAnimator.enabled ||
            visualAnimator.runtimeAnimatorController == null ||
            visualAnimator.layerCount <= BaseLayerIndex)
        {
            return false;
        }

        if (visualAnimator.IsInTransition(BaseLayerIndex))
        {
            AnimatorStateInfo nextState = visualAnimator.GetNextAnimatorStateInfo(BaseLayerIndex);
            if (TryResolveAnimatorLocomotionState(nextState, out actualState, out actualStateHash))
            {
                resolvedSource = AnimatorLocomotionStateSource.Next;
                return true;
            }
        }

        AnimatorStateInfo currentState = visualAnimator.GetCurrentAnimatorStateInfo(BaseLayerIndex);
        if (!TryResolveAnimatorLocomotionState(currentState, out actualState, out actualStateHash))
            return false;

        resolvedSource = AnimatorLocomotionStateSource.Current;
        return true;
    }

    private bool TryResolveAnimatorLocomotionState(
        AnimatorStateInfo animatorState,
        out ClipState locomotionState,
        out int stateHash)
    {
        if (_hasIdleState && AnimatorStateMatchesHash(animatorState, _idleStateHash))
        {
            locomotionState = ClipState.Idle;
            stateHash = _idleStateHash;
            return true;
        }

        if (_hasWalkState && AnimatorStateMatchesHash(animatorState, _walkStateHash))
        {
            locomotionState = ClipState.Walk;
            stateHash = _walkStateHash;
            return true;
        }

        if (_hasRunState && AnimatorStateMatchesHash(animatorState, _runStateHash))
        {
            locomotionState = ClipState.Run;
            stateHash = _runStateHash;
            return true;
        }

        locomotionState = ClipState.Idle;
        stateHash = 0;
        return false;
    }

    private bool AnimatorHasOrTargetsState(int targetHash)
    {
        if (visualAnimator == null ||
            !visualAnimator.enabled ||
            visualAnimator.runtimeAnimatorController == null ||
            visualAnimator.layerCount <= BaseLayerIndex ||
            targetHash == 0)
        {
            return false;
        }

        AnimatorStateInfo currentState = visualAnimator.GetCurrentAnimatorStateInfo(BaseLayerIndex);
        if (AnimatorStateMatchesHash(currentState, targetHash))
            return true;

        if (!visualAnimator.IsInTransition(BaseLayerIndex))
            return false;

        AnimatorStateInfo nextState = visualAnimator.GetNextAnimatorStateInfo(BaseLayerIndex);
        return AnimatorStateMatchesHash(nextState, targetHash);
    }

    private void SynchronizeDriverStateWithActualAnimator(ClipState actualState, int actualStateHash)
    {
        if (_externalOneShotActive || _externalSustainedStateActive || _interactionStateActive)
            return;

        PlayableState actualPlayableState = ResolvePlayableState(actualState);
        if (!actualPlayableState.IsValid ||
            actualPlayableState.Hash != actualStateHash ||
            _currentStateHash == actualStateHash)
        {
            return;
        }

        _currentStateHash = actualStateHash;
        _currentStateName = actualPlayableState.Name;
        _stateTimer = 0f;
    }

    private void LogSuppressedClientNetworkLocomotionCrossFade(
        PlayableState playableState,
        float crossFadeDuration,
        MotionSample sample,
        string callerReason,
        string suppressionReason)
    {
        if (!debugClientLocomotionDiagnostics)
            return;

        _clientDiagnosticsLastRequestedStateName = playableState.Name ?? string.Empty;
        _clientDiagnosticsLastRequestedStateHash = playableState.Hash;
        _clientDiagnosticsLastRestartIfCurrent = false;
        _clientDiagnosticsLastCallerReason = callerReason ?? string.Empty;
        LogClientLocomotionCrossFade(
            playableState,
            crossFadeDuration,
            sample,
            callerReason,
            false,
            false,
            suppressionReason,
            _currentStateName,
            _currentStateHash);
    }

    private bool TryGetClientNetworkLocomotionTransition(out AnimatorStateInfo nextState)
    {
        nextState = default;
        if (visualAnimator == null ||
            visualAnimator.layerCount <= BaseLayerIndex ||
            !visualAnimator.IsInTransition(BaseLayerIndex))
        {
            return false;
        }

        nextState = visualAnimator.GetNextAnimatorStateInfo(BaseLayerIndex);
        return (_hasIdleState && AnimatorStateMatchesHash(nextState, _idleStateHash)) ||
               (_hasWalkState && AnimatorStateMatchesHash(nextState, _walkStateHash)) ||
               (_hasRunState && AnimatorStateMatchesHash(nextState, _runStateHash));
    }

    private static bool AnimatorStateMatchesHash(AnimatorStateInfo stateInfo, int stateHash)
    {
        return stateHash != 0 &&
               (stateInfo.shortNameHash == stateHash || stateInfo.fullPathHash == stateHash);
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

        return TrySelectCarryState(
            sample,
            out state,
            out reason,
            preferJumpState);
    }

    private bool TrySelectCarryState(
        MotionSample sample,
        out PlayableState state,
        out string reason,
        bool preferJumpState = false)
    {
        state = default;
        reason = string.Empty;

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
        return HasStateMotion(
            stateName,
            requireMotion,
            allowWhenEditorStateUnavailable,
            false,
            out _);
    }

    private bool HasStateMotion(
        string stateName,
        bool requireMotion,
        bool allowWhenEditorStateUnavailable,
        bool allowPlayerBuildStateExistenceFallback,
        out bool acceptedPlayerBuildStateExistence)
    {
        acceptedPlayerBuildStateExistence = false;

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
        if (allowPlayerBuildStateExistenceFallback && TryCacheState(stateName, out _))
        {
            acceptedPlayerBuildStateExistence = true;
            return true;
        }

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
        ResetClientAnimatorResyncState();

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
        string driverStateNameBefore = _currentStateName;
        int driverStateHashBefore = _currentStateHash;

        if (debugClientLocomotionDiagnostics)
        {
            _clientDiagnosticsLastRequestedStateName = playableState.Name ?? string.Empty;
            _clientDiagnosticsLastRequestedStateHash = playableState.Hash;
            _clientDiagnosticsLastRestartIfCurrent = restartIfCurrent;
            _clientDiagnosticsLastCallerReason = reason ?? string.Empty;
        }

        if (!playableState.IsValid)
        {
            if (debugLogs)
                Debug.Log($"[HamsterVisualClipStateDriver] Skip state because it is missing. speed={sample.PlanarSpeed:F2} sprint={sample.IsSprintHeld} grounded={FormatGrounded(sample)} vertical={sample.VerticalVelocity:F2} reason={FormatReason(reason)}", this);

            if (debugClientLocomotionDiagnostics)
            {
                LogClientLocomotionCrossFade(
                    playableState,
                    crossFadeDuration,
                    sample,
                    reason,
                    restartIfCurrent,
                    false,
                    "state missing",
                    driverStateNameBefore,
                    driverStateHashBefore);
            }

            return false;
        }

        if (_currentStateHash == playableState.Hash && !restartIfCurrent)
        {
            if (debugClientLocomotionDiagnostics)
            {
                LogClientLocomotionCrossFade(
                    playableState,
                    crossFadeDuration,
                    sample,
                    reason,
                    restartIfCurrent,
                    false,
                    "already current",
                    driverStateNameBefore,
                    driverStateHashBefore);
            }
            return false;
        }

        visualAnimator.CrossFade(playableState.Hash, crossFadeDuration, BaseLayerIndex);
        _currentStateHash = playableState.Hash;
        _currentStateName = playableState.Name;
        _stateTimer = 0f;

        if (debugClientLocomotionDiagnostics)
            _clientDiagnosticsLastCrossFadeCalledFrame = Time.frameCount;

        if (debugClientLocomotionDiagnostics)
        {
            LogClientLocomotionCrossFade(
                playableState,
                crossFadeDuration,
                sample,
                reason,
                restartIfCurrent,
                true,
                "none",
                driverStateNameBefore,
                driverStateHashBefore);
        }

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

    private void BeginClientLocomotionDiagnosticsUpdate(float deltaTime)
    {
        if (!_clientDiagnosticsWasEnabled)
        {
            ResetClientLocomotionDiagnosticsState();
            _clientDiagnosticsWasEnabled = true;
        }

        ResolveClientLocomotionDiagnosticsReferences();
        _clientDiagnosticsUpdateFrame = Time.frameCount;
        _clientDiagnosticsAnimatorLogThisFrame = false;
        _clientDiagnosticsAnimatorUpdateBefore = CaptureAnimatorDiagnosticSnapshot();
        SampleClientDiagnosticsRootMotion(deltaTime);

        float interval = Mathf.Max(0.01f, clientLocomotionDiagnosticsInterval);
        _clientDiagnosticsPeriodicLogThisFrame = Time.unscaledTime >= _clientDiagnosticsNextPeriodicLogTime;
        if (!_clientDiagnosticsPeriodicLogThisFrame)
            return;

        _clientDiagnosticsNextPeriodicLogTime = Time.unscaledTime + interval;
        LogClientLocomotionDiagnosticsContext();
        LogClientLocomotionDiagnosticsRootMotion();
    }

    private void CompleteClientLocomotionDiagnosticsUpdate(
        MotionSample sample,
        bool hasSample,
        bool canDriveAnimator,
        string updateResult)
    {
        if (!debugClientLocomotionDiagnostics)
            return;

        MotionSample diagnosticSample = hasSample ? sample : ReadMotionSample();
        ClipState selectedState = SelectLocomotionState(diagnosticSample, out string decisionReason);
        bool decisionChanged = !_clientDiagnosticsLastDecisionInitialized ||
                               _clientDiagnosticsLastDecisionState != selectedState ||
                               _clientDiagnosticsLastDecisionReason != decisionReason;

        if (decisionChanged)
        {
            _clientDiagnosticsLastDecisionInitialized = true;
            _clientDiagnosticsLastDecisionState = selectedState;
            _clientDiagnosticsLastDecisionReason = decisionReason;
        }

        _clientDiagnosticsAnimatorUpdateAfter = CaptureAnimatorDiagnosticSnapshot();
        bool crossFadeCalledThisFrame = _clientDiagnosticsLastCrossFadeCalledFrame == Time.frameCount;
        bool shouldLogAnimator = _clientDiagnosticsPeriodicLogThisFrame || decisionChanged || crossFadeCalledThisFrame;
        _clientDiagnosticsAnimatorLogThisFrame = shouldLogAnimator;
        if (!shouldLogAnimator)
            return;

        LogClientLocomotionDiagnosticsMotorSample(diagnosticSample);
        LogClientLocomotionDiagnosticsDecision(
            diagnosticSample,
            selectedState,
            decisionReason,
            canDriveAnimator,
            updateResult);
        LogClientLocomotionDiagnosticsAnimatorUpdate(updateResult, crossFadeCalledThisFrame);

        if (!canDriveAnimator)
        {
            Debug.Log(
                $"[HamsterVisualDiag/CrossFade] role={ResolveClientDiagnosticsRole()} frame={Time.frameCount} " +
                "requested=<none> requestedHash=0 restartIfCurrent=False playableStateValid=False " +
                "animatorAvailable=False earlyReturn=animator unavailable crossFadeCalled=False " +
                $"sampleSource={FormatReason(diagnosticSample.Source)} speed={diagnosticSample.PlanarSpeed:F3} " +
                $"move={FormatMoveInput(diagnosticSample)} grounded={FormatGrounded(diagnosticSample)} " +
                $"sprint={diagnosticSample.IsSprintHeld} callerReason={FormatReason(updateResult)}",
                this);
        }
    }

    private void ResolveClientLocomotionDiagnosticsReferences()
    {
        if (_clientDiagnosticsNetworkObject == null)
            _clientDiagnosticsNetworkObject = GetComponentInParent<NetworkObject>();

        Transform resolvedRoot = _clientDiagnosticsNetworkObject != null
            ? _clientDiagnosticsNetworkObject.transform
            : null;
        if (_clientDiagnosticsRoot != resolvedRoot)
        {
            _clientDiagnosticsRoot = resolvedRoot;
            ResetClientDiagnosticsRootTracking();
        }

        if (_clientDiagnosticsVisualPreviewRoot == null)
        {
            Transform searchRoot = _clientDiagnosticsRoot != null ? _clientDiagnosticsRoot : transform;
            _clientDiagnosticsVisualPreviewRoot = FindChildRecursive(searchRoot, "VisualPreviewRoot");
        }

        if (_clientDiagnosticsRecoveryStateSource == null)
            _clientDiagnosticsRecoveryStateSource = GetComponentInParent<HamsterMotorShellRagdollRecoveryAdapter>();
    }

    private void SampleClientDiagnosticsRootMotion(float deltaTime)
    {
        _clientDiagnosticsDeltaTime = deltaTime;
        _clientDiagnosticsDeltaTimeValid = IsFinite(deltaTime) && deltaTime > 0f;
        _clientDiagnosticsRootInitializedThisFrame = false;
        _clientDiagnosticsRootPositionValid = false;
        _clientDiagnosticsLargeDelta = false;
        _clientDiagnosticsWorldDelta = Vector3.zero;
        _clientDiagnosticsPlanarDelta = Vector3.zero;
        _clientDiagnosticsInferredPlanarSpeed = 0f;

        if (_clientDiagnosticsRoot == null)
        {
            ResetClientDiagnosticsRootTracking();
            return;
        }

        Vector3 currentPosition = _clientDiagnosticsRoot.position;
        _clientDiagnosticsCurrentRootPosition = currentPosition;
        _clientDiagnosticsRootPositionValid = IsFinite(currentPosition);
        if (!_clientDiagnosticsRootPositionValid)
            return;

        if (!_clientDiagnosticsRootTrackingInitialized)
        {
            _clientDiagnosticsPreviousRootPosition = currentPosition;
            _clientDiagnosticsPreviousRootPositionForLog = currentPosition;
            _clientDiagnosticsRootTrackingInitialized = true;
            _clientDiagnosticsRootInitializedThisFrame = true;
            return;
        }

        _clientDiagnosticsPreviousRootPositionForLog = _clientDiagnosticsPreviousRootPosition;
        _clientDiagnosticsWorldDelta = currentPosition - _clientDiagnosticsPreviousRootPosition;
        _clientDiagnosticsPreviousRootPosition = currentPosition;
        _clientDiagnosticsPlanarDelta = Vector3.ProjectOnPlane(
            _clientDiagnosticsWorldDelta,
            Vector3.up);
        _clientDiagnosticsLargeDelta = _clientDiagnosticsWorldDelta.sqrMagnitude >
                                       ClientDiagnosticsLargeDeltaDistance * ClientDiagnosticsLargeDeltaDistance;

        if (_clientDiagnosticsDeltaTimeValid)
        {
            float inferredSpeed = _clientDiagnosticsPlanarDelta.magnitude / deltaTime;
            if (IsFinite(inferredSpeed))
                _clientDiagnosticsInferredPlanarSpeed = Mathf.Max(0f, inferredSpeed);
            else
                _clientDiagnosticsDeltaTimeValid = false;
        }
    }

    private void LogClientLocomotionDiagnosticsContext()
    {
        NetworkManager networkManager = NetworkManager.Singleton;
        NetworkObject networkObject = _clientDiagnosticsNetworkObject;
        RuntimeAnimatorController controller = visualAnimator != null
            ? visualAnimator.runtimeAnimatorController
            : null;
        Component networkTransform = _clientDiagnosticsRoot != null
            ? _clientDiagnosticsRoot.GetComponent("NetworkTransform")
            : null;
        string localClientId = networkManager != null
            ? networkManager.LocalClientId.ToString()
            : "<none>";
        string ownerClientId = networkObject != null
            ? networkObject.OwnerClientId.ToString()
            : "<none>";

        Debug.Log(
            $"[HamsterVisualDiag/Context] role={ResolveClientDiagnosticsRole()} objectName={name} " +
            $"objectPath={GetTransformPath(transform)} listening={(networkManager != null && networkManager.IsListening)} " +
            $"isServer={(networkManager != null && networkManager.IsServer)} isClient={(networkManager != null && networkManager.IsClient)} " +
            $"networkObject={(networkObject != null ? networkObject.name : "<null>")} " +
            $"networkObjectPath={GetTransformPath(networkObject != null ? networkObject.transform : null)} " +
            $"isSpawned={(networkObject != null && networkObject.IsSpawned)} isOwner={(networkObject != null && networkObject.IsOwner)} " +
            $"ownerClientId={ownerClientId} localClientId={localClientId} driverEnabled={enabled} " +
            $"visualAnimatorPath={GetTransformPath(visualAnimator != null ? visualAnimator.transform : null)} " +
            $"visualAnimatorEnabled={(visualAnimator != null && visualAnimator.enabled)} " +
            $"runtimeAnimatorController={(controller != null ? controller.name : "<null>")} " +
            $"motorStateSourcePath={GetTransformPath(motorStateSource != null ? motorStateSource.transform : null)} " +
            $"targetBodyPath={GetTransformPath(targetBody != null ? targetBody.transform : null)} " +
            $"trackedRootPath={GetTransformPath(_clientDiagnosticsRoot)} " +
            $"networkTransformPath={GetTransformPath(networkTransform != null ? networkTransform.transform : null)} " +
            $"networkTransformMatchesRoot={(networkTransform != null && networkTransform.transform == _clientDiagnosticsRoot)}",
            this);
    }

    private void LogClientLocomotionDiagnosticsRootMotion()
    {
        Transform root = _clientDiagnosticsRoot;
        Transform visualPreviewRoot = _clientDiagnosticsVisualPreviewRoot;

        Debug.Log(
            $"[HamsterVisualDiag/RootMotion] role={ResolveClientDiagnosticsRole()} trackedRoot={GetTransformPath(root)} " +
            $"initialized={_clientDiagnosticsRootTrackingInitialized} initializedThisFrame={_clientDiagnosticsRootInitializedThisFrame} " +
            $"rootPositionValid={_clientDiagnosticsRootPositionValid} currentPosition={FormatVector3(_clientDiagnosticsCurrentRootPosition)} " +
            $"previousPosition={FormatVector3(_clientDiagnosticsPreviousRootPositionForLog)} " +
            $"worldDelta={FormatVector3(_clientDiagnosticsWorldDelta)} planarDelta={FormatVector3(_clientDiagnosticsPlanarDelta)} " +
            $"inferredPlanarSpeed={_clientDiagnosticsInferredPlanarSpeed:F3} deltaTime={_clientDiagnosticsDeltaTime:F6} " +
            $"deltaTimeValid={_clientDiagnosticsDeltaTimeValid} largeDelta={_clientDiagnosticsLargeDelta} " +
            $"largeDeltaThreshold={ClientDiagnosticsLargeDeltaDistance:F3} rootRotation={FormatRotation(root)} " +
            $"rootYaw={FormatYaw(root)} targetBodyPosition={FormatPosition(targetBody != null ? targetBody.transform : null)} " +
            $"targetBodyRotation={FormatRotation(targetBody != null ? targetBody.transform : null)} " +
            $"visualPreviewRootPath={GetTransformPath(visualPreviewRoot)} " +
            $"visualPreviewRootPosition={FormatPosition(visualPreviewRoot)} " +
            $"visualPreviewRootRotation={FormatRotation(visualPreviewRoot)}",
            this);
    }

    private void LogClientLocomotionDiagnosticsMotorSample(MotionSample sample)
    {
        string motorDetails = motorStateSource != null
            ? $"motorIsGrounded={motorStateSource.IsGrounded} motorHasMoveInput={motorStateSource.HasMoveInput} " +
              $"motorPlanarSpeed={motorStateSource.CurrentPlanarSpeed:F3} motorVerticalVelocity={motorStateSource.CurrentVerticalVelocity:F3} " +
              $"motorSprintHeld={motorStateSource.IsSprintHeld} motorExternalControlLocked={motorStateSource.IsExternalControlLocked} " +
              $"motorExternalMovementControlScale={motorStateSource.ExternalMovementControlScale:F3}"
            : "motorStateSource=<null>";

        Debug.Log(
            $"[HamsterVisualDiag/MotorSample] role={ResolveClientDiagnosticsRole()} source={FormatReason(sample.Source)} " +
            $"hasGroundedState={sample.HasGroundedState} isGrounded={sample.IsGrounded} " +
            $"isSprintHeld={sample.IsSprintHeld} hasMoveInputState={sample.HasMoveInputState} " +
            $"hasMoveInput={sample.HasMoveInput} planarSpeed={sample.PlanarSpeed:F3} " +
            $"verticalVelocity={sample.VerticalVelocity:F3} hasHoldingState={sample.HasHoldingState} " +
            $"isHolding={sample.IsHolding} {motorDetails} " +
            $"clientNetworkSampleValid={_clientNetworkSampleValid} " +
            $"clientNetworkMotionActive={_clientNetworkMotionActive} " +
            $"clientNetworkRawPlanarSpeed={_clientNetworkRawPlanarSpeed:F3} " +
            $"clientNetworkSmoothedPlanarSpeed={_clientNetworkSmoothedPlanarSpeed:F3} " +
            $"clientNetworkVerticalSpeed={_clientNetworkVerticalSpeed:F3} " +
            $"clientNetworkLargeDelta={_clientNetworkLargeDelta} " +
            $"fallbackApplied={_clientNetworkFallbackApplied} " +
            $"fallbackBlockedReason={FormatReason(_clientNetworkFallbackBlockedReason)} " +
            $"sampleSourceBefore={FormatReason(_clientNetworkMotionSampleSourceBefore)} " +
            $"sampleSourceAfter={FormatReason(_clientNetworkMotionSampleSourceAfter)}",
            this);
    }

    private void LogClientLocomotionDiagnosticsDecision(
        MotionSample sample,
        ClipState selectedState,
        string decisionReason,
        bool canDriveAnimator,
        string updateResult)
    {
        PlayableState selectedPlayableState = ResolvePlayableState(selectedState);
        string recoveryState = _clientDiagnosticsRecoveryStateSource != null
            ? _clientDiagnosticsRecoveryStateSource.CurrentRecoveryState.ToString()
            : "<none>";
        string pendingState = _clientNetworkPendingLocomotionStateActive
            ? _clientNetworkPendingLocomotionState.ToString()
            : "<none>";
        string actualAnimatorLocomotionState = _clientNetworkActualAnimatorLocomotionResolved
            ? _clientNetworkActualAnimatorLocomotionState.ToString()
            : "<none>";

        Debug.Log(
            $"[HamsterVisualDiag/Decision] role={ResolveClientDiagnosticsRole()} selectedState={selectedState} " +
            $"selectedPlayableState={FormatState(selectedPlayableState.Name, selectedPlayableState.Hash)} " +
            $"selectedPlayableValid={selectedPlayableState.IsValid} reason={FormatReason(decisionReason)} " +
            $"driverState={FormatState(_currentStateName, _currentStateHash)} stateTimer={_stateTimer:F3} " +
            $"externalOneShotActive={_externalOneShotActive} externalSustainedStateActive={_externalSustainedStateActive} " +
            $"interactionStateActive={_interactionStateActive} recoveryStateExists={_clientDiagnosticsRecoveryStateSource != null} " +
            $"recoveryState={recoveryState} canDriveAnimator={canDriveAnimator} updateResult={FormatReason(updateResult)} " +
            $"sampleSource={FormatReason(sample.Source)} speed={sample.PlanarSpeed:F3} move={FormatMoveInput(sample)} " +
            $"grounded={FormatGrounded(sample)} sprint={sample.IsSprintHeld} " +
            $"rawSelectedState={selectedState} stableSelectedState={_clientNetworkStableLocomotionState} " +
            $"pendingState={pendingState} pendingDuration={_clientNetworkPendingLocomotionDuration:F3} " +
            $"locomotionHoldRemaining={_clientNetworkLocomotionHoldRemaining:F3} " +
            $"transitionSuppressed={_clientNetworkTransitionSuppressed} " +
            $"suppressionReason={FormatReason(_clientNetworkTransitionSuppressionReason)} " +
            $"actualAnimatorLocomotionResolved={_clientNetworkActualAnimatorLocomotionResolved} " +
            $"actualAnimatorLocomotionState={actualAnimatorLocomotionState} " +
            $"actualAnimatorStateSource={_clientNetworkActualAnimatorStateSource} " +
            $"actualAnimatorMatchesTarget={_clientNetworkActualAnimatorMatchesTarget} " +
            $"driverAnimatorMismatch={_clientNetworkDriverAnimatorMismatch} " +
            $"animatorResyncRequested={_clientAnimatorResyncRequested} " +
            $"animatorResyncSuppressed={_clientAnimatorResyncSuppressed} " +
            $"animatorResyncSuppressionReason={FormatReason(_clientAnimatorResyncSuppressionReason)} " +
            $"animatorResyncCooldownRemaining={_clientAnimatorResyncCooldownRemaining:F3} " +
            $"animatorResyncTargetHash={_clientAnimatorResyncTargetHash}",
            this);
    }

    private void LogClientLocomotionDiagnosticsAnimatorUpdate(
        string updateResult,
        bool crossFadeCalledThisFrame)
    {
        bool requestedMatchesBefore = AnimatorSnapshotMatchesHash(
            _clientDiagnosticsAnimatorUpdateBefore,
            _clientDiagnosticsLastRequestedStateHash);
        bool requestedMatchesAfter = AnimatorSnapshotMatchesHash(
            _clientDiagnosticsAnimatorUpdateAfter,
            _clientDiagnosticsLastRequestedStateHash);

        Debug.Log(
            $"[HamsterVisualDiag/AnimatorUpdate] role={ResolveClientDiagnosticsRole()} frame={Time.frameCount} " +
            $"before={FormatAnimatorSnapshot(_clientDiagnosticsAnimatorUpdateBefore)} " +
            $"after={FormatAnimatorSnapshot(_clientDiagnosticsAnimatorUpdateAfter)} " +
            $"driverState={FormatState(_currentStateName, _currentStateHash)} " +
            $"lastRequested={FormatState(_clientDiagnosticsLastRequestedStateName, _clientDiagnosticsLastRequestedStateHash)} " +
            $"restartIfCurrent={_clientDiagnosticsLastRestartIfCurrent} callerReason={FormatReason(_clientDiagnosticsLastCallerReason)} " +
            $"requestedMatchesBefore={requestedMatchesBefore} requestedMatchesAfter={requestedMatchesAfter} " +
            $"crossFadeCalledThisFrame={crossFadeCalledThisFrame} updateResult={FormatReason(updateResult)}",
            this);
    }

    private void LogClientLocomotionCrossFade(
        PlayableState playableState,
        float crossFadeDuration,
        MotionSample sample,
        string callerReason,
        bool restartIfCurrent,
        bool crossFadeCalled,
        string earlyReturnReason,
        string driverStateNameBefore,
        int driverStateHashBefore)
    {
        if (!debugClientLocomotionDiagnostics)
            return;

        int fingerprint = ComputeClientDiagnosticsCrossFadeFingerprint(
            playableState,
            restartIfCurrent,
            crossFadeCalled,
            earlyReturnReason,
            callerReason);
        bool fingerprintChanged = fingerprint != _clientDiagnosticsLastCrossFadeFingerprint;
        bool intervalElapsed = Time.unscaledTime >= _clientDiagnosticsNextCrossFadeLogTime;
        if (!crossFadeCalled && !fingerprintChanged && !intervalElapsed)
            return;

        _clientDiagnosticsLastCrossFadeFingerprint = fingerprint;
        _clientDiagnosticsNextCrossFadeLogTime = Time.unscaledTime +
                                                 Mathf.Max(0.01f, clientLocomotionDiagnosticsInterval);
        AnimatorDiagnosticSnapshot animatorSnapshot = CaptureAnimatorDiagnosticSnapshot();

        Debug.Log(
            $"[HamsterVisualDiag/CrossFade] role={ResolveClientDiagnosticsRole()} frame={Time.frameCount} " +
            $"requestedState={FormatReason(playableState.Name)} requestedHash={playableState.Hash} " +
            $"driverBefore={FormatState(driverStateNameBefore, driverStateHashBefore)} " +
            $"driverAfter={FormatState(_currentStateName, _currentStateHash)} restartIfCurrent={restartIfCurrent} " +
            $"playableStateValid={playableState.IsValid} animatorAvailable={animatorSnapshot.Available} " +
            $"earlyReturn={FormatReason(earlyReturnReason)} crossFadeCalled={crossFadeCalled} " +
            $"crossFadeDuration={crossFadeDuration:F3} sampleSource={FormatReason(sample.Source)} " +
            $"speed={sample.PlanarSpeed:F3} move={FormatMoveInput(sample)} grounded={FormatGrounded(sample)} " +
            $"sprint={sample.IsSprintHeld} callerReason={FormatReason(callerReason)} " +
            $"rawSelectedState={_clientNetworkRawSelectedLocomotionState} " +
            $"stableSelectedState={_clientNetworkStableLocomotionState} " +
            $"transitionSuppressed={_clientNetworkTransitionSuppressed} " +
            $"suppressionReason={FormatReason(_clientNetworkTransitionSuppressionReason)} " +
            $"actualAnimatorLocomotionResolved={_clientNetworkActualAnimatorLocomotionResolved} " +
            $"actualAnimatorLocomotionState={(_clientNetworkActualAnimatorLocomotionResolved ? _clientNetworkActualAnimatorLocomotionState.ToString() : "<none>")} " +
            $"actualAnimatorStateSource={_clientNetworkActualAnimatorStateSource} " +
            $"actualAnimatorMatchesTarget={_clientNetworkActualAnimatorMatchesTarget} " +
            $"driverAnimatorMismatch={_clientNetworkDriverAnimatorMismatch} " +
            $"animatorResyncRequested={_clientAnimatorResyncRequested} " +
            $"animatorResyncSuppressed={_clientAnimatorResyncSuppressed} " +
            $"animatorResyncSuppressionReason={FormatReason(_clientAnimatorResyncSuppressionReason)} " +
            $"animatorResyncCooldownRemaining={_clientAnimatorResyncCooldownRemaining:F3} " +
            $"animatorResyncTargetHash={_clientAnimatorResyncTargetHash} " +
            $"animator={FormatAnimatorSnapshot(animatorSnapshot)}",
            this);
    }

    private int ComputeClientDiagnosticsCrossFadeFingerprint(
        PlayableState playableState,
        bool restartIfCurrent,
        bool crossFadeCalled,
        string earlyReturnReason,
        string callerReason)
    {
        unchecked
        {
            int hash = playableState.Hash;
            hash = (hash * 397) ^ (playableState.IsValid ? 1 : 0);
            hash = (hash * 397) ^ (restartIfCurrent ? 1 : 0);
            hash = (hash * 397) ^ (crossFadeCalled ? 1 : 0);
            hash = (hash * 397) ^ (earlyReturnReason != null ? earlyReturnReason.GetHashCode() : 0);
            hash = (hash * 397) ^ (callerReason != null ? callerReason.GetHashCode() : 0);
            return hash;
        }
    }

    private AnimatorDiagnosticSnapshot CaptureAnimatorDiagnosticSnapshot()
    {
        AnimatorDiagnosticSnapshot snapshot = default;
        if (visualAnimator == null ||
            !visualAnimator.enabled ||
            visualAnimator.runtimeAnimatorController == null ||
            visualAnimator.layerCount <= BaseLayerIndex)
        {
            return snapshot;
        }

        snapshot.Available = true;
        AnimatorStateInfo currentState = visualAnimator.GetCurrentAnimatorStateInfo(BaseLayerIndex);
        snapshot.ShortNameHash = currentState.shortNameHash;
        snapshot.FullPathHash = currentState.fullPathHash;
        snapshot.NormalizedTime = currentState.normalizedTime;
        snapshot.InTransition = visualAnimator.IsInTransition(BaseLayerIndex);
        if (snapshot.InTransition)
        {
            AnimatorStateInfo nextState = visualAnimator.GetNextAnimatorStateInfo(BaseLayerIndex);
            snapshot.NextShortNameHash = nextState.shortNameHash;
            snapshot.NextFullPathHash = nextState.fullPathHash;
            snapshot.NextNormalizedTime = nextState.normalizedTime;
        }

        return snapshot;
    }

    private static bool AnimatorSnapshotMatchesHash(AnimatorDiagnosticSnapshot snapshot, int stateHash)
    {
        if (!snapshot.Available || stateHash == 0)
            return false;

        if (snapshot.ShortNameHash == stateHash || snapshot.FullPathHash == stateHash)
            return true;

        return snapshot.InTransition &&
               (snapshot.NextShortNameHash == stateHash || snapshot.NextFullPathHash == stateHash);
    }

    private static bool AnimatorSnapshotsHaveSameState(
        AnimatorDiagnosticSnapshot first,
        AnimatorDiagnosticSnapshot second)
    {
        return first.Available == second.Available &&
               first.ShortNameHash == second.ShortNameHash &&
               first.FullPathHash == second.FullPathHash &&
               first.InTransition == second.InTransition &&
               first.NextShortNameHash == second.NextShortNameHash &&
               first.NextFullPathHash == second.NextFullPathHash;
    }

    private string ResolveClientDiagnosticsRole()
    {
        NetworkManager networkManager = NetworkManager.Singleton;
        bool isServer = networkManager != null && networkManager.IsServer;
        bool isClient = networkManager != null && networkManager.IsClient;
        bool isOwner = _clientDiagnosticsNetworkObject != null && _clientDiagnosticsNetworkObject.IsOwner;

        if (isServer && isClient)
            return isOwner ? "HostOwner" : "HostRemoteClient";

        if (!isServer && isClient)
            return isOwner ? "ClientOwner" : "ClientRemote";

        if (isServer)
            return isOwner ? "ServerOwner" : "ServerRemote";

        return "Offline";
    }

    private void ResetClientLocomotionDiagnosticsState()
    {
        _clientDiagnosticsNetworkObject = null;
        _clientDiagnosticsRecoveryStateSource = null;
        _clientDiagnosticsRoot = null;
        _clientDiagnosticsVisualPreviewRoot = null;
        ResetClientDiagnosticsRootTracking();
        _clientDiagnosticsNextPeriodicLogTime = 0f;
        _clientDiagnosticsNextCrossFadeLogTime = 0f;
        _clientDiagnosticsPeriodicLogThisFrame = false;
        _clientDiagnosticsAnimatorLogThisFrame = false;
        _clientDiagnosticsUpdateFrame = -1;
        _clientDiagnosticsLastDecisionInitialized = false;
        _clientDiagnosticsLastDecisionState = default;
        _clientDiagnosticsLastDecisionReason = string.Empty;
        _clientDiagnosticsLastRequestedStateName = string.Empty;
        _clientDiagnosticsLastRequestedStateHash = 0;
        _clientDiagnosticsLastRestartIfCurrent = false;
        _clientDiagnosticsLastCallerReason = string.Empty;
        _clientDiagnosticsLastCrossFadeCalledFrame = -1;
        _clientDiagnosticsLastCrossFadeFingerprint = int.MinValue;
        _clientDiagnosticsAnimatorUpdateBefore = default;
        _clientDiagnosticsAnimatorUpdateAfter = default;
    }

    private void ResetClientDiagnosticsRootTracking()
    {
        _clientDiagnosticsPreviousRootPosition = Vector3.zero;
        _clientDiagnosticsPreviousRootPositionForLog = Vector3.zero;
        _clientDiagnosticsCurrentRootPosition = Vector3.zero;
        _clientDiagnosticsWorldDelta = Vector3.zero;
        _clientDiagnosticsPlanarDelta = Vector3.zero;
        _clientDiagnosticsRootTrackingInitialized = false;
        _clientDiagnosticsRootInitializedThisFrame = false;
        _clientDiagnosticsRootPositionValid = false;
        _clientDiagnosticsDeltaTimeValid = false;
        _clientDiagnosticsLargeDelta = false;
        _clientDiagnosticsInferredPlanarSpeed = 0f;
        _clientDiagnosticsDeltaTime = 0f;
    }

    private static bool IsFinite(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }

    private static bool IsFinite(Vector3 value)
    {
        return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
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

    private static string FormatAnimatorSnapshot(AnimatorDiagnosticSnapshot snapshot)
    {
        if (!snapshot.Available)
            return "available=False";

        return $"available=True shortHash={snapshot.ShortNameHash} fullPathHash={snapshot.FullPathHash} " +
               $"normalizedTime={snapshot.NormalizedTime:F3} inTransition={snapshot.InTransition} " +
               $"nextShortHash={snapshot.NextShortNameHash} nextFullPathHash={snapshot.NextFullPathHash} " +
               $"nextNormalizedTime={snapshot.NextNormalizedTime:F3}";
    }

    private static string FormatState(string stateName, int stateHash)
    {
        return $"{(string.IsNullOrEmpty(stateName) ? "<none>" : stateName)}({stateHash})";
    }

    private static string FormatVector3(Vector3 value)
    {
        return $"({value.x:F3},{value.y:F3},{value.z:F3})";
    }

    private static string FormatPosition(Transform target)
    {
        return target != null ? FormatVector3(target.position) : "<null>";
    }

    private static string FormatRotation(Transform target)
    {
        return target != null ? FormatVector3(target.rotation.eulerAngles) : "<null>";
    }

    private static string FormatYaw(Transform target)
    {
        return target != null ? target.rotation.eulerAngles.y.ToString("F3") : "<null>";
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
