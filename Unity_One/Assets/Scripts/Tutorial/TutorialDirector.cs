using System;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

[DisallowMultipleComponent]
public sealed class TutorialDirector : MonoBehaviour
{
    public interface ITutorialHitSource
    {
        int HitCount { get; }
        event Action<int> HitAccepted;
    }

    public enum TutorialStep
    {
        BootstrapWaiting = 0,
        Move = 1,
        Jump = 2,
        Attack = 3,
        Pickup = 4,
        Drop = 5,
        Throw = 6,
        Peel = 7,
        FallRespawn = 8,
        Recovery = 9,
        PrepareGhost = 10,
        Ghost = 11,
        Complete = 12,
        Failed = 13
    }

    [Header("Production Scene References")]
    [SerializeField] private GameStateManager gameStateManager;
    [SerializeField] private ReadySystem readySystem;
    [SerializeField] private InGameMatchManager inGameMatchManager;
    [SerializeField] private PostItRoundManager postItRoundManager;
    [SerializeField] private PostItGhostSpectatorController ghostSpectatorController;

    [Header("Tutorial Scene References")]
    [SerializeField] private MonoBehaviour tutorialUiMarker;
    [SerializeField] private MonoBehaviour tutorialHitSource;
    [SerializeField] private TutorialCheckpoint moveCheckpoint;
    [SerializeField] private Collider jumpPracticeArea;
    [SerializeField] private Collider guidedPeelTarget;
    [SerializeField] private BoxCollider spectatorBounds;
    [SerializeField] private AudioListener gameplayAudioListener;

    [Header("Bounded Validation")]
    [SerializeField, Min(1f)] private float bootstrapTimeoutSeconds = 10f;
    [SerializeField, Min(1f)] private float optionalHintDelaySeconds = 8f;
    [SerializeField, Min(0.1f)] private float gameSpawnPositionTolerance = 1.5f;
    [SerializeField, Min(0f)] private float respawnSettledPlanarSpeed = 0.5f;
    [SerializeField, Min(0f)] private float respawnSettledVerticalSpeed = 0.35f;
    [SerializeField, Min(0f)] private float respawnSettledSeconds = 0.35f;

    [Header("Drop / Throw Observation")]
    [SerializeField, Min(0f)] private float dropMaximumPlanarSpeed = 2.5f;
    [SerializeField, Min(0f)] private float dropMaximumForwardDisplacement = 1.25f;
    [SerializeField, Min(0f)] private float throwMinimumPlanarSpeed = 3.5f;
    [SerializeField, Min(0f)] private float throwMinimumForwardDisplacement = 1.5f;

    [Header("Guided Peel")]
    [SerializeField, Min(0.1f)] private float guidedPeelDistance = 6f;
    [SerializeField, Min(0.1f)] private float guidedPeelHoldSeconds = 0.75f;
    [SerializeField] private LayerMask guidedPeelMask = ~0;

    public event Action<TutorialStep> StepChanged;
    public event Action<string> InstructionChanged;
    public event Action<int, int> ProgressChanged;
    public event Action<string> HintChanged;

    public TutorialStep CurrentStep { get; private set; } = TutorialStep.BootstrapWaiting;
    public bool IsRunning { get; private set; }
    public bool IsCompleted { get; private set; }
    public float StepElapsedSeconds =>
        Mathf.Max(0f, Time.unscaledTime - _stepStartedAt);
    public string CurrentInstruction { get; private set; } = string.Empty;
    public string CurrentHint { get; private set; } = string.Empty;
    public int ProgressCurrent { get; private set; }
    public int ProgressTarget { get; private set; }

    private TutorialLocalHostLauncher _launcher;
    private NetworkManager _networkManager;
    private NetworkObject _localPlayerObject;
    private PlayerHub _playerHub;
    private PlayerStatusModule _playerStatus;
    private PlayerPostItInventory _inventory;
    private HamsterFullRagdollMotor _motor;
    private HamsterMotorShellItemAdapter _itemAdapter;
    private Camera _gameplayCamera;
    private ITutorialHitSource _hitSource;
    private Camera[] _presentationCameras = Array.Empty<Camera>();
    private AudioListener[] _presentationAudioListeners = Array.Empty<AudioListener>();

    private float _bootstrapDeadline;
    private float _stepStartedAt;
    private float _nextHintAt;
    private bool _readyToggleRequested;
    private bool _hasGameSpawnPose;
    private Vector3 _gameSpawnPosition;
    private bool _stateSubscribed;
    private bool _postItSubscribed;
    private bool _hitSubscribed;
    private bool _postItStateDirty;
    private int _postItEvaluationFrame;

    private PostItRuntimeData[] _inventoryBefore = Array.Empty<PostItRuntimeData>();
    private PostItWorldDropData[] _worldDropsBefore = Array.Empty<PostItWorldDropData>();
    private PostItRuntimeData _trackedFallPostIt = PostItRuntimeData.Invalid;

    private bool _jumpSawGrounded;
    private bool _jumpSawAirborne;
    private int _attackHitCountAtStart;

    private ItemPickupNetwork _tutorialItem;
    private Rigidbody _tutorialItemBody;
    private bool _pickupSawReleased;
    private bool _releaseObserved;
    private int _releaseObservedFrame;
    private Vector3 _releaseReferencePosition;
    private Vector3 _releaseForward;
    private bool _throwWaitingForPickup;

    private float _guidedPeelHeldSeconds;
    private bool _fallTransitionObserved;
    private float _respawnSettledAt = -1f;

    private bool _gameplayAudioSuppressed;
    private bool _gameplayAudioWasEnabled;
    private bool _ghostPresentationValidated;
    private float _nextGhostPresentationCheckAt;
    private Vector3 _previousGhostCameraPosition;
    private bool _ghostFreeMoveObserved;
    private bool _ghostAscendObserved;
    private bool _ghostDescendObserved;
    private bool _ghostBoostObserved;
    private bool _exitRequested;

    private void Start()
    {
        IsRunning = true;
        IsCompleted = false;
        CurrentStep = TutorialStep.BootstrapWaiting;
        _stepStartedAt = Time.unscaledTime;
        _bootstrapDeadline = Time.unscaledTime + Mathf.Max(1f, bootstrapTimeoutSeconds);
        _nextHintAt = Time.unscaledTime + Mathf.Max(1f, optionalHintDelaySeconds);

        ResolveStaticReferencesOnce();
        PublishPresentation(
            "튜토리얼 Local Host와 Player를 준비하는 중입니다.",
            0,
            1,
            string.Empty);
    }

    private void Update()
    {
        if (!IsRunning)
            return;

        if (CurrentStep == TutorialStep.BootstrapWaiting)
        {
            TickBootstrap();
            return;
        }

        if (!ValidatePlayingInvariant())
            return;

        if (Time.unscaledTime >= _nextHintAt)
        {
            CurrentHint = GetHint(CurrentStep);
            HintChanged?.Invoke(CurrentHint);
            _nextHintAt = float.PositiveInfinity;
        }

        if (_postItStateDirty && Time.frameCount >= _postItEvaluationFrame)
        {
            _postItStateDirty = false;
            EvaluatePostItStep();
        }

        switch (CurrentStep)
        {
            case TutorialStep.Jump:
                TickJump();
                break;
            case TutorialStep.Pickup:
                TickPickup();
                break;
            case TutorialStep.Drop:
                TickDrop();
                break;
            case TutorialStep.Throw:
                TickThrow();
                break;
            case TutorialStep.Peel:
                TickGuidedPeel();
                break;
            case TutorialStep.FallRespawn:
            case TutorialStep.PrepareGhost:
                TickRespawnSettlement();
                break;
            case TutorialStep.Ghost:
                TickGhost();
                break;
        }
    }

    private void LateUpdate()
    {
        if (_gameplayAudioSuppressed &&
            gameplayAudioListener != null &&
            gameplayAudioListener.enabled)
        {
            gameplayAudioListener.enabled = false;
        }
    }

    private void OnDisable()
    {
        UnsubscribeAll();

        if (_gameplayAudioSuppressed &&
            gameplayAudioListener != null &&
            (_playerStatus == null || !_playerStatus.IsEliminated))
        {
            gameplayAudioListener.enabled = _gameplayAudioWasEnabled;
        }

        _gameplayAudioSuppressed = false;
    }

    public void RequestExitTutorial()
    {
        if (_exitRequested)
            return;

        _exitRequested = true;
        UnsubscribeAll();
        IsRunning = false;
        _launcher?.RequestExitTutorial();
    }

    public void RequestSkipGuidedPeel()
    {
        if (!IsRunning || CurrentStep != TutorialStep.Peel)
            return;

        CurrentHint = "GUIDED_PEEL_DEFERRED_P1";
        HintChanged?.Invoke(CurrentHint);
        EnterStep(TutorialStep.FallRespawn);
    }

    public void RequestCompleteTutorial()
    {
        if (CurrentStep != TutorialStep.Complete)
            return;

        RequestExitTutorial();
    }

    public void NotifyMoveCheckpointEntered(
        TutorialCheckpoint checkpoint,
        NetworkObject playerObject)
    {
        if (!IsRunning ||
            CurrentStep != TutorialStep.Move ||
            checkpoint == null ||
            checkpoint != moveCheckpoint ||
            playerObject == null ||
            playerObject != _localPlayerObject)
        {
            return;
        }

        EnterStep(TutorialStep.Jump);
    }

    private void ResolveStaticReferencesOnce()
    {
        _launcher = FindFirstObjectByType<TutorialLocalHostLauncher>(FindObjectsInactive.Include);
        _hitSource = tutorialHitSource as ITutorialHitSource;
    }

    private void TickBootstrap()
    {
        if (Time.unscaledTime >= _bootstrapDeadline)
        {
            FailTutorial("BOOTSTRAP_TIMEOUT");
            return;
        }

        if (!TryCacheLocalHostDependencies())
            return;

        if (!_readyToggleRequested)
        {
            _readyToggleRequested = true;
            if (!readySystem.IsLocalReady())
                readySystem.ToggleLocalReady();
        }

        if (gameStateManager.GetState() != GameStateManager.GameState.Playing ||
            _inventory.Count != 2 ||
            _playerStatus.IsEliminated ||
            tutorialUiMarker == null ||
            !tutorialUiMarker.isActiveAndEnabled ||
            _gameplayCamera == null ||
            !_gameplayCamera.enabled)
        {
            return;
        }

        if (!_hasGameSpawnPose)
        {
            if (!inGameMatchManager.ServerTryResolveGameSpawnPose(
                    _playerHub,
                    out _gameSpawnPosition,
                    out _))
            {
                return;
            }

            _hasGameSpawnPose = true;
        }

        if (Vector3.Distance(_localPlayerObject.transform.position, _gameSpawnPosition) >
            Mathf.Max(0.1f, gameSpawnPositionTolerance))
        {
            return;
        }

        SubscribeState();
        EnterStep(TutorialStep.Move);
    }

    private bool TryCacheLocalHostDependencies()
    {
        if (_networkManager == null)
            _networkManager = NetworkManager.Singleton;

        if (_networkManager == null ||
            !_networkManager.IsListening ||
            !_networkManager.IsHost ||
            !_networkManager.IsServer ||
            !_networkManager.IsClient ||
            _launcher == null ||
            !_launcher.IsTutorialSessionActive ||
            gameStateManager == null ||
            !gameStateManager.IsSpawned ||
            readySystem == null ||
            !readySystem.IsSpawned ||
            inGameMatchManager == null ||
            !inGameMatchManager.IsSpawned ||
            postItRoundManager == null ||
            !postItRoundManager.IsSpawned ||
            ghostSpectatorController == null ||
            tutorialUiMarker == null ||
            tutorialHitSource == null ||
            _hitSource == null ||
            moveCheckpoint == null ||
            jumpPracticeArea == null ||
            guidedPeelTarget == null ||
            spectatorBounds == null ||
            gameplayAudioListener == null)
        {
            return false;
        }

        NetworkClient localClient = _networkManager.LocalClient;
        NetworkObject playerObject = localClient != null ? localClient.PlayerObject : null;
        if (playerObject == null ||
            !playerObject.IsSpawned ||
            !playerObject.IsPlayerObject ||
            !playerObject.IsOwner ||
            playerObject.OwnerClientId != _networkManager.LocalClientId)
        {
            return false;
        }

        if (_localPlayerObject == null)
        {
            _localPlayerObject = playerObject;
            _playerHub = playerObject.GetComponent<PlayerHub>();
            _playerStatus = playerObject.GetComponentInChildren<PlayerStatusModule>(true);
            _inventory = playerObject.GetComponent<PlayerPostItInventory>();
            _motor = playerObject.GetComponentInChildren<HamsterFullRagdollMotor>(true);
            _itemAdapter = playerObject.GetComponentInChildren<HamsterMotorShellItemAdapter>(true);

            if (_playerHub == null ||
                _playerStatus == null ||
                _inventory == null ||
                _motor == null ||
                _itemAdapter == null ||
                !_playerHub.IsSpawned ||
                !_playerStatus.IsSpawned ||
                !_inventory.IsSpawned ||
                _playerHub.NetworkObject != playerObject ||
                _playerStatus.NetworkObject != playerObject ||
                _inventory.NetworkObject != playerObject)
            {
                _localPlayerObject = null;
                return false;
            }

            _gameplayCamera = _playerHub.ActiveLocalCamera;
            if (_gameplayCamera == null)
                _gameplayCamera = _playerHub.PlayerCamera;

            _presentationCameras = FindObjectsByType<Camera>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);
            _presentationAudioListeners = FindObjectsByType<AudioListener>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);
        }

        return _localPlayerObject == playerObject &&
               _gameplayCamera != null;
    }

    private bool ValidatePlayingInvariant()
    {
        if (_networkManager == null ||
            !_networkManager.IsListening ||
            !_networkManager.IsHost ||
            _localPlayerObject == null ||
            !_localPlayerObject.IsSpawned ||
            gameStateManager == null ||
            !gameStateManager.IsSpawned)
        {
            FailTutorial("LOCAL_HOST_INVARIANT_LOST");
            return false;
        }

        if (gameStateManager.GetState() != GameStateManager.GameState.Playing)
        {
            FailTutorial("GAME_STATE_LEFT_PLAYING");
            return false;
        }

        return true;
    }

    private void EnterStep(TutorialStep nextStep)
    {
        UnsubscribeStepEvents();

        CurrentStep = nextStep;
        _stepStartedAt = Time.unscaledTime;
        _nextHintAt = Time.unscaledTime + Mathf.Max(1f, optionalHintDelaySeconds);
        CurrentHint = string.Empty;
        _postItStateDirty = false;
        _respawnSettledAt = -1f;
        ResetStageState();

        switch (nextStep)
        {
            case TutorialStep.Move:
                moveCheckpoint.ResetCheckpoint();
                PublishPresentation("WASD로 이동해 체크포인트 안으로 들어가세요.", 0, 1, string.Empty);
                break;
            case TutorialStep.Jump:
                CapturePostItSnapshot();
                _jumpSawGrounded = _motor.IsGrounded;
                PublishPresentation("연습 구역 안에서 Space로 점프하고 착지하세요.", 0, 3, string.Empty);
                break;
            case TutorialStep.Attack:
                _attackHitCountAtStart = _hitSource.HitCount;
                SubscribeHit();
                PublishPresentation("공격 Target을 실제 공격으로 2회 맞히세요.", 0, 2, string.Empty);
                break;
            case TutorialStep.Pickup:
                if (!TryResolveUniqueTutorialItem())
                {
                    FailTutorial("TUTORIAL_ITEM_NOT_UNIQUE");
                    return;
                }

                _pickupSawReleased = !_itemAdapter.HasHeldItem;
                PublishPresentation("바닥의 Item을 우클릭으로 집으세요.", 0, 1, string.Empty);
                break;
            case TutorialStep.Drop:
                if (!_itemAdapter.HasHeldItem ||
                    _itemAdapter.CurrentHeldPickup != _tutorialItem)
                {
                    FailTutorial("DROP_REQUIRES_HELD_TUTORIAL_ITEM");
                    return;
                }

                CaptureReleaseReference();
                PublishPresentation("G 키로 Item을 조용히 내려놓으세요.", 0, 1, string.Empty);
                break;
            case TutorialStep.Throw:
                _throwWaitingForPickup = true;
                PublishPresentation("같은 Item을 다시 집은 뒤 Q 키로 던지세요.", 0, 2, string.Empty);
                break;
            case TutorialStep.Peel:
                CapturePostItSnapshot();
                PublishPresentation(
                    "Target을 화면 중앙에 두고 우클릭을 0.75초 유지하세요.",
                    0,
                    100,
                    "실제 게임에서는 다른 플레이어의 포스트잇을 바라보고 우클릭을 유지해 뜯을 수 있습니다.");
                break;
            case TutorialStep.FallRespawn:
                if (_inventory.Count != 2 || _playerStatus.IsEliminated)
                {
                    FailTutorial("FALL_RESPAWN_INVALID_START");
                    return;
                }

                CapturePostItSnapshot();
                SubscribePostItState();
                PublishPresentation("FallPracticeEdge로 이동해 한 번 낙사하세요.", 0, 1, string.Empty);
                break;
            case TutorialStep.Recovery:
                if (_inventory.Count != 1 || !_trackedFallPostIt.IsValid)
                {
                    FailTutorial("RECOVERY_INVALID_START");
                    return;
                }

                CapturePostItSnapshot();
                SubscribePostItState();
                PublishPresentation("떨어진 내 포스트잇을 우클릭으로 회수하세요.", 0, 1, string.Empty);
                break;
            case TutorialStep.PrepareGhost:
                if (_inventory.Count != 2 || _playerStatus.IsEliminated)
                {
                    FailTutorial("PREPARE_GHOST_INVALID_START");
                    return;
                }

                CapturePostItSnapshot();
                SubscribePostItState();
                PublishPresentation("유령 관전을 준비하려면 다시 한 번 낙사하세요.", 0, 1, string.Empty);
                break;
            case TutorialStep.Ghost:
                if (_inventory.Count != 1 || _playerStatus.IsEliminated)
                {
                    FailTutorial("GHOST_INVALID_START");
                    return;
                }

                SuppressGameplayAudio();
                CapturePostItSnapshot();
                SubscribePostItState();
                PublishPresentation("한 번 더 낙사해 탈락 상태를 체험하세요.", 0, 4, string.Empty);
                break;
            case TutorialStep.Complete:
                UnsubscribeAll();
                IsRunning = false;
                IsCompleted = true;
                PublishPresentation("튜토리얼을 완료했습니다.", 1, 1, string.Empty);
                break;
        }

        StepChanged?.Invoke(CurrentStep);
    }

    private void ResetStageState()
    {
        _releaseObserved = false;
        _releaseObservedFrame = -1;
        _guidedPeelHeldSeconds = 0f;
        _fallTransitionObserved = false;
        _ghostPresentationValidated = false;
        _nextGhostPresentationCheckAt = 0f;
    }

    private void TickJump()
    {
        if (!IsInsideCollider(jumpPracticeArea, _localPlayerObject.transform.position) ||
            _playerStatus.IsEliminated ||
            !PostItSnapshotStillMatches())
        {
            return;
        }

        if (!_jumpSawGrounded)
            _jumpSawGrounded = _motor.IsGrounded;
        else if (!_jumpSawAirborne &&
                 !_motor.IsGrounded &&
                 _motor.CurrentVerticalVelocity > 0f)
            _jumpSawAirborne = true;

        int progress = _jumpSawGrounded ? 1 : 0;
        if (_jumpSawAirborne)
            progress = 2;

        PublishProgress(progress, 3);

        if (_jumpSawGrounded && _jumpSawAirborne && _motor.IsGrounded)
        {
            PublishProgress(3, 3);
            EnterStep(TutorialStep.Attack);
        }
    }

    private void HandleHitAccepted(int acceptedHitCount)
    {
        if (!IsRunning || CurrentStep != TutorialStep.Attack)
            return;

        int acceptedDuringStep = Mathf.Max(0, acceptedHitCount - _attackHitCountAtStart);
        PublishProgress(Mathf.Min(acceptedDuringStep, 2), 2);
        if (acceptedDuringStep >= 2)
            EnterStep(TutorialStep.Pickup);
    }

    private void TickPickup()
    {
        if (_tutorialItem == null || !_tutorialItem.IsSpawned)
        {
            FailTutorial("TUTORIAL_ITEM_LOST");
            return;
        }

        if (!_itemAdapter.HasHeldItem)
        {
            _pickupSawReleased = true;
            return;
        }

        if (_pickupSawReleased && _itemAdapter.CurrentHeldPickup == _tutorialItem)
        {
            PublishProgress(1, 1);
            EnterStep(TutorialStep.Drop);
        }
    }

    private void TickDrop()
    {
        if (!_releaseObserved)
        {
            if (_itemAdapter.HasHeldItem)
            {
                if (_itemAdapter.CurrentHeldPickup != _tutorialItem)
                {
                    FailTutorial("DROP_ITEM_IDENTITY_CHANGED");
                    return;
                }

                CaptureReleaseReference();
                return;
            }

            _releaseObserved = true;
            _releaseObservedFrame = Time.frameCount;
            return;
        }

        if (Time.frameCount <= _releaseObservedFrame)
            return;

        if (!ValidateReleasedItem(out float planarSpeed, out float forwardDisplacement))
            return;

        if (planarSpeed > Mathf.Max(0f, dropMaximumPlanarSpeed) ||
            forwardDisplacement > Mathf.Max(0f, dropMaximumForwardDisplacement))
        {
            CurrentHint = "Throw로 관찰되어 Drop 완료를 보류했습니다. Item을 다시 집으려면 튜토리얼을 재시작하세요.";
            HintChanged?.Invoke(CurrentHint);
            return;
        }

        PublishProgress(1, 1);
        EnterStep(TutorialStep.Throw);
    }

    private void TickThrow()
    {
        if (_throwWaitingForPickup)
        {
            if (!_itemAdapter.HasHeldItem)
                return;

            if (_itemAdapter.CurrentHeldPickup != _tutorialItem)
            {
                FailTutorial("THROW_ITEM_IDENTITY_CHANGED");
                return;
            }

            _throwWaitingForPickup = false;
            CaptureReleaseReference();
            PublishProgress(1, 2);
            return;
        }

        if (!_releaseObserved)
        {
            if (_itemAdapter.HasHeldItem)
            {
                if (_itemAdapter.CurrentHeldPickup != _tutorialItem)
                {
                    FailTutorial("THROW_ITEM_IDENTITY_CHANGED");
                    return;
                }

                CaptureReleaseReference();
                return;
            }

            _releaseObserved = true;
            _releaseObservedFrame = Time.frameCount;
            return;
        }

        if (Time.frameCount <= _releaseObservedFrame)
            return;

        if (!ValidateReleasedItem(out float planarSpeed, out float forwardDisplacement))
            return;

        if (planarSpeed < Mathf.Max(0f, throwMinimumPlanarSpeed) &&
            forwardDisplacement < Mathf.Max(0f, throwMinimumForwardDisplacement))
        {
            CurrentHint = "Throw 기준을 확인하지 못했습니다. Item을 다시 집어 던지세요.";
            HintChanged?.Invoke(CurrentHint);
            _throwWaitingForPickup = true;
            _releaseObserved = false;
            return;
        }

        PublishProgress(2, 2);
        EnterStep(TutorialStep.Peel);
    }

    private void TickGuidedPeel()
    {
        Camera cameraToUse = _playerHub.ActiveLocalCamera;
        Mouse mouse = Mouse.current;
        bool validAim = cameraToUse != null &&
                        mouse != null &&
                        mouse.rightButton.isPressed &&
                        Physics.Raycast(
                            cameraToUse.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f)),
                            out RaycastHit hit,
                            Mathf.Max(0.1f, guidedPeelDistance),
                            guidedPeelMask,
                            QueryTriggerInteraction.Collide) &&
                        hit.collider == guidedPeelTarget;

        if (!validAim || !PostItSnapshotStillMatches())
        {
            _guidedPeelHeldSeconds = 0f;
            PublishProgress(0, 100);
            return;
        }

        _guidedPeelHeldSeconds += Time.unscaledDeltaTime;
        float duration = Mathf.Max(0.1f, guidedPeelHoldSeconds);
        int progress = Mathf.Clamp(Mathf.RoundToInt(_guidedPeelHeldSeconds / duration * 100f), 0, 100);
        PublishProgress(progress, 100);
        if (_guidedPeelHeldSeconds >= duration && PostItSnapshotStillMatches())
            EnterStep(TutorialStep.FallRespawn);
    }

    private void EvaluatePostItStep()
    {
        switch (CurrentStep)
        {
            case TutorialStep.FallRespawn:
                if (!_fallTransitionObserved &&
                    TryValidateSingleFallTransition(
                        2,
                        out PostItRuntimeData removed,
                        out _))
                {
                    _trackedFallPostIt = removed;
                    _fallTransitionObserved = true;
                }
                break;
            case TutorialStep.Recovery:
                if (TryValidateRecoveryTransition())
                {
                    PublishProgress(1, 1);
                    EnterStep(TutorialStep.PrepareGhost);
                }
                break;
            case TutorialStep.PrepareGhost:
                if (!_fallTransitionObserved &&
                    TryValidateSingleFallTransition(
                        2,
                        out _,
                        out _))
                {
                    _fallTransitionObserved = true;
                }
                break;
            case TutorialStep.Ghost:
                if (!_fallTransitionObserved &&
                    TryValidateSingleFallTransition(
                        1,
                        out _,
                        out _))
                {
                    _fallTransitionObserved = true;
                }
                break;
        }
    }

    private void TickRespawnSettlement()
    {
        if (!_fallTransitionObserved ||
            _playerStatus.IsEliminated ||
            !_hasGameSpawnPose ||
            Vector3.Distance(_localPlayerObject.transform.position, _gameSpawnPosition) >
            Mathf.Max(0.1f, gameSpawnPositionTolerance) ||
            !_motor.IsGrounded ||
            _motor.CurrentPlanarSpeed > Mathf.Max(0f, respawnSettledPlanarSpeed) ||
            Mathf.Abs(_motor.CurrentVerticalVelocity) >
            Mathf.Max(0f, respawnSettledVerticalSpeed))
        {
            _respawnSettledAt = -1f;
            return;
        }

        if (_respawnSettledAt < 0f)
        {
            _respawnSettledAt = Time.unscaledTime;
            return;
        }

        if (Time.unscaledTime - _respawnSettledAt <
            Mathf.Max(0f, respawnSettledSeconds))
        {
            return;
        }

        PublishProgress(1, 1);
        EnterStep(
            CurrentStep == TutorialStep.FallRespawn
                ? TutorialStep.Recovery
                : TutorialStep.Ghost);
    }

    private void TickGhost()
    {
        if (!_fallTransitionObserved)
            return;

        Camera ghostCamera = ghostSpectatorController.ActiveSpectatorCamera;
        AudioListener ghostListener = ghostCamera != null
            ? ghostCamera.GetComponent<AudioListener>()
            : null;

        if (_localPlayerObject == null ||
            !_localPlayerObject.IsSpawned ||
            !_playerStatus.IsEliminated ||
            !ghostSpectatorController.IsSpectating ||
            ghostCamera == null ||
            !ghostCamera.enabled ||
            ghostListener == null ||
            !ghostListener.enabled ||
            gameplayAudioListener == null ||
            gameplayAudioListener.enabled)
        {
            return;
        }

        if (!_ghostPresentationValidated)
        {
            if (Time.unscaledTime < _nextGhostPresentationCheckAt)
                return;

            _nextGhostPresentationCheckAt = Time.unscaledTime + 0.25f;
            if (CountEnabledCameras() != 1 || CountEnabledAudioListeners() != 1)
                return;

            _ghostPresentationValidated = true;
            _previousGhostCameraPosition = ghostCamera.transform.position;
            CurrentInstruction = "WASD 이동, 상승, 하강, Shift boost를 차례로 확인하세요.";
            InstructionChanged?.Invoke(CurrentInstruction);
        }

        Vector3 position = ghostCamera.transform.position;
        Vector3 delta = position - _previousGhostCameraPosition;
        _previousGhostCameraPosition = position;

        Keyboard keyboard = Keyboard.current;
        bool moving = delta.sqrMagnitude > 0.0001f;
        _ghostFreeMoveObserved |= moving;
        _ghostAscendObserved |= keyboard != null &&
                                (keyboard.spaceKey.isPressed || keyboard.eKey.isPressed) &&
                                delta.y > 0.001f;
        _ghostDescendObserved |= keyboard != null &&
                                 (keyboard.leftCtrlKey.isPressed || keyboard.qKey.isPressed) &&
                                 delta.y < -0.001f;
        _ghostBoostObserved |= keyboard != null &&
                               (keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed) &&
                               moving;

        if (!IsInsideCollider(spectatorBounds, position))
        {
            FailTutorial("GHOST_LEFT_SPECTATOR_BOUNDS");
            return;
        }

        int progress = (_ghostFreeMoveObserved ? 1 : 0) +
                       (_ghostAscendObserved ? 1 : 0) +
                       (_ghostDescendObserved ? 1 : 0) +
                       (_ghostBoostObserved ? 1 : 0);
        PublishProgress(progress, 4);
        if (progress == 4)
            EnterStep(TutorialStep.Complete);
    }

    private void CaptureReleaseReference()
    {
        if (_tutorialItem == null)
            return;

        _releaseReferencePosition = _tutorialItem.transform.position;
        Vector3 forward = _gameplayCamera != null
            ? _gameplayCamera.transform.forward
            : _localPlayerObject.transform.forward;
        forward.y = 0f;
        _releaseForward = forward.sqrMagnitude > 0.0001f
            ? forward.normalized
            : Vector3.forward;
    }

    private bool ValidateReleasedItem(
        out float planarSpeed,
        out float forwardDisplacement)
    {
        planarSpeed = 0f;
        forwardDisplacement = 0f;
        if (_tutorialItem == null ||
            !_tutorialItem.IsSpawned ||
            !_tutorialItem.IsWorldVisualVisible() ||
            _tutorialItemBody == null)
        {
            FailTutorial("TUTORIAL_ITEM_LOST_AFTER_RELEASE");
            return false;
        }

        Vector3 velocity = _tutorialItemBody.linearVelocity;
        Vector3 position = _tutorialItem.transform.position;
        if (!IsFinite(velocity) || !IsFinite(position))
        {
            FailTutorial("TUTORIAL_ITEM_NON_FINITE_RELEASE");
            return false;
        }

        Vector3 planarVelocity = Vector3.ProjectOnPlane(velocity, Vector3.up);
        Vector3 displacement = Vector3.ProjectOnPlane(
            position - _releaseReferencePosition,
            Vector3.up);
        planarSpeed = planarVelocity.magnitude;
        forwardDisplacement = Mathf.Max(0f, Vector3.Dot(displacement, _releaseForward));
        return true;
    }

    private bool TryResolveUniqueTutorialItem()
    {
        ItemPickupNetwork[] candidates = FindObjectsByType<ItemPickupNetwork>(
            FindObjectsInactive.Exclude,
            FindObjectsSortMode.None);
        ItemPickupNetwork resolved = null;
        Scene activeScene = gameObject.scene;

        for (int i = 0; i < candidates.Length; i++)
        {
            ItemPickupNetwork candidate = candidates[i];
            if (candidate == null ||
                candidate.gameObject.scene != activeScene ||
                !candidate.IsSpawned ||
                !candidate.IsWorldVisualVisible())
            {
                continue;
            }

            if (resolved != null)
                return false;

            resolved = candidate;
        }

        if (resolved == null)
            return false;

        _tutorialItem = resolved;
        _tutorialItemBody = resolved.GetComponent<Rigidbody>();
        return _tutorialItemBody != null;
    }

    private void CapturePostItSnapshot()
    {
        _inventoryBefore = _inventory.GetSnapshot();
        _worldDropsBefore = postItRoundManager.GetWorldDropSnapshot();
    }

    private bool PostItSnapshotStillMatches()
    {
        PostItRuntimeData[] inventoryNow = _inventory.GetSnapshot();
        PostItWorldDropData[] worldNow = postItRoundManager.GetWorldDropSnapshot();
        return RuntimeArraysEqual(_inventoryBefore, inventoryNow) &&
               WorldArraysEqual(_worldDropsBefore, worldNow);
    }

    private bool TryValidateSingleFallTransition(
        int expectedBeforeCount,
        out PostItRuntimeData removed,
        out PostItWorldDropData added)
    {
        removed = PostItRuntimeData.Invalid;
        added = PostItWorldDropData.Invalid;
        PostItRuntimeData[] inventoryNow = _inventory.GetSnapshot();
        PostItWorldDropData[] worldNow = postItRoundManager.GetWorldDropSnapshot();

        if (_inventoryBefore.Length != expectedBeforeCount ||
            inventoryNow.Length != expectedBeforeCount - 1 ||
            worldNow.Length != _worldDropsBefore.Length + 1)
        {
            return false;
        }

        int removedCount = 0;
        for (int i = 0; i < _inventoryBefore.Length; i++)
        {
            if (!ContainsPostIt(inventoryNow, _inventoryBefore[i].PostItId))
            {
                removed = _inventoryBefore[i];
                removedCount++;
            }
        }

        int addedCount = 0;
        for (int i = 0; i < worldNow.Length; i++)
        {
            if (!ContainsWorldDrop(_worldDropsBefore, worldNow[i].PostItId))
            {
                added = worldNow[i];
                addedCount++;
            }
        }

        return removedCount == 1 &&
               addedCount == 1 &&
               removed.PostItId == added.PostItId &&
               removed.Type == added.Type &&
               removed.VisualId == added.VisualId &&
               added.IsValid &&
               IsFinite(added.Position);
    }

    private bool TryValidateRecoveryTransition()
    {
        PostItRuntimeData[] inventoryNow = _inventory.GetSnapshot();
        PostItWorldDropData[] worldNow = postItRoundManager.GetWorldDropSnapshot();
        if (inventoryNow.Length != _inventoryBefore.Length + 1 ||
            worldNow.Length != _worldDropsBefore.Length - 1 ||
            ContainsWorldDrop(worldNow, _trackedFallPostIt.PostItId) ||
            !TryFindPostIt(inventoryNow, _trackedFallPostIt.PostItId, out PostItRuntimeData recovered))
        {
            return false;
        }

        return recovered.PostItId == _trackedFallPostIt.PostItId &&
               recovered.Type == _trackedFallPostIt.Type &&
               recovered.TopicId == _trackedFallPostIt.TopicId &&
               recovered.VisualId == _trackedFallPostIt.VisualId &&
               recovered.OriginalOwnerClientId == _trackedFallPostIt.OriginalOwnerClientId &&
               recovered.HolderClientId == _networkManager.LocalClientId &&
               recovered.SlotIndex >= 0 &&
               !_playerStatus.IsEliminated;
    }

    private void SuppressGameplayAudio()
    {
        if (gameplayAudioListener == null || _gameplayAudioSuppressed)
            return;

        _gameplayAudioWasEnabled = gameplayAudioListener.enabled;
        gameplayAudioListener.enabled = false;
        _gameplayAudioSuppressed = true;
    }

    private void SubscribeState()
    {
        if (_stateSubscribed)
            return;

        gameStateManager.StateValue.OnValueChanged += HandleStateChanged;
        _stateSubscribed = true;
    }

    private void SubscribePostItState()
    {
        if (_postItSubscribed)
            return;

        _inventory.PostItsChanged += HandlePostItStateChanged;
        postItRoundManager.WorldDropsChanged += HandlePostItStateChanged;
        _postItSubscribed = true;
    }

    private void SubscribeHit()
    {
        if (_hitSubscribed)
            return;

        _hitSource.HitAccepted += HandleHitAccepted;
        _hitSubscribed = true;
    }

    private void UnsubscribeStepEvents()
    {
        if (_postItSubscribed)
        {
            _inventory.PostItsChanged -= HandlePostItStateChanged;
            postItRoundManager.WorldDropsChanged -= HandlePostItStateChanged;
            _postItSubscribed = false;
        }

        if (_hitSubscribed)
        {
            _hitSource.HitAccepted -= HandleHitAccepted;
            _hitSubscribed = false;
        }
    }

    private void UnsubscribeAll()
    {
        UnsubscribeStepEvents();
        if (_stateSubscribed && gameStateManager != null)
        {
            gameStateManager.StateValue.OnValueChanged -= HandleStateChanged;
            _stateSubscribed = false;
        }
    }

    private void HandleStateChanged(int previousValue, int currentValue)
    {
        if (!IsRunning || CurrentStep == TutorialStep.BootstrapWaiting)
            return;

        if ((GameStateManager.GameState)currentValue != GameStateManager.GameState.Playing)
            FailTutorial("GAME_STATE_LEFT_PLAYING");
    }

    private void HandlePostItStateChanged()
    {
        if (!IsRunning)
            return;

        _postItStateDirty = true;
        _postItEvaluationFrame = Time.frameCount + 1;
    }

    private void PublishPresentation(
        string instruction,
        int current,
        int target,
        string hint)
    {
        CurrentInstruction = instruction ?? string.Empty;
        CurrentHint = hint ?? string.Empty;
        ProgressCurrent = Mathf.Max(0, current);
        ProgressTarget = Mathf.Max(0, target);
        InstructionChanged?.Invoke(CurrentInstruction);
        ProgressChanged?.Invoke(ProgressCurrent, ProgressTarget);
        HintChanged?.Invoke(CurrentHint);
    }

    private void PublishProgress(int current, int target)
    {
        current = Mathf.Max(0, current);
        target = Mathf.Max(0, target);
        if (ProgressCurrent == current && ProgressTarget == target)
            return;

        ProgressCurrent = current;
        ProgressTarget = target;
        ProgressChanged?.Invoke(current, target);
    }

    private void FailTutorial(string reason)
    {
        if (CurrentStep == TutorialStep.Failed)
            return;

        UnsubscribeAll();
        IsRunning = false;
        IsCompleted = false;
        CurrentStep = TutorialStep.Failed;
        _stepStartedAt = Time.unscaledTime;
        PublishPresentation(
            "튜토리얼을 계속할 수 없습니다.",
            0,
            1,
            reason ?? "UNKNOWN_FAILURE");
        StepChanged?.Invoke(CurrentStep);
    }

    private static string GetHint(TutorialStep step)
    {
        switch (step)
        {
            case TutorialStep.Move:
                return "WASD 입력 후 바닥의 체크포인트까지 이동하세요.";
            case TutorialStep.Jump:
                return "연습 구역 안에서 완전히 착지한 뒤 Space를 눌러보세요.";
            case TutorialStep.Attack:
                return "Target을 바라보고 좌클릭 공격을 2회 적중시키세요.";
            case TutorialStep.Pickup:
                return "Item 가까이에서 우클릭하세요.";
            case TutorialStep.Drop:
                return "Item을 든 상태에서 G 키를 눌러 내려놓으세요.";
            case TutorialStep.Throw:
                return "같은 Item을 다시 든 뒤 Q 키를 눌러 던지세요.";
            case TutorialStep.Peel:
                return "Target을 화면 중앙에 유지한 채 우클릭을 놓지 마세요.";
            case TutorialStep.FallRespawn:
            case TutorialStep.PrepareGhost:
            case TutorialStep.Ghost:
                return "표시된 FallPracticeEdge 밖으로 직접 이동하세요.";
            case TutorialStep.Recovery:
                return "방금 떨어진 포스트잇 가까이에서 우클릭하세요.";
            default:
                return string.Empty;
        }
    }

    private static bool IsInsideCollider(Collider target, Vector3 point)
    {
        if (target == null)
            return false;

        Vector3 closest = target.ClosestPoint(point);
        return (closest - point).sqrMagnitude <= 0.0001f;
    }

    private static bool ContainsPostIt(PostItRuntimeData[] items, int postItId)
    {
        return TryFindPostIt(items, postItId, out _);
    }

    private static bool TryFindPostIt(
        PostItRuntimeData[] items,
        int postItId,
        out PostItRuntimeData data)
    {
        for (int i = 0; i < items.Length; i++)
        {
            if (items[i].PostItId == postItId)
            {
                data = items[i];
                return true;
            }
        }

        data = PostItRuntimeData.Invalid;
        return false;
    }

    private static bool ContainsWorldDrop(
        PostItWorldDropData[] items,
        int postItId)
    {
        for (int i = 0; i < items.Length; i++)
        {
            if (items[i].PostItId == postItId)
                return true;
        }

        return false;
    }

    private static bool RuntimeArraysEqual(
        PostItRuntimeData[] first,
        PostItRuntimeData[] second)
    {
        if (first.Length != second.Length)
            return false;

        for (int i = 0; i < first.Length; i++)
        {
            if (!first[i].Equals(second[i]))
                return false;
        }

        return true;
    }

    private static bool WorldArraysEqual(
        PostItWorldDropData[] first,
        PostItWorldDropData[] second)
    {
        if (first.Length != second.Length)
            return false;

        for (int i = 0; i < first.Length; i++)
        {
            if (!first[i].Equals(second[i]))
                return false;
        }

        return true;
    }

    private static bool IsFinite(Vector3 value)
    {
        return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
    }

    private static bool IsFinite(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }

    private int CountEnabledCameras()
    {
        int count = 0;
        for (int i = 0; i < _presentationCameras.Length; i++)
        {
            Camera camera = _presentationCameras[i];
            if (camera != null &&
                camera.enabled &&
                camera.gameObject.activeInHierarchy)
            {
                count++;
            }
        }

        return count;
    }

    private int CountEnabledAudioListeners()
    {
        int count = 0;
        for (int i = 0; i < _presentationAudioListeners.Length; i++)
        {
            AudioListener listener = _presentationAudioListeners[i];
            if (listener != null &&
                listener.enabled &&
                listener.gameObject.activeInHierarchy)
            {
                count++;
            }
        }

        return count;
    }
}
