using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.EventSystems;

public class GameStateManager : NetworkBehaviour
{
    private const float CountdownPostItAssignmentRetryInterval = 0.1f;

    public enum GameState
    {
        Lobby = 0,
        Countdown = 1,
        Playing = 2,
        Results = 3,
        Guessing = 4
    }

    private enum PlayingEndReason
    {
        Timer = 0,
        LastSurvivor = 1,
        NoSurvivors = 2
    }

    private enum GameSpawnWarmupState
    {
        None = 0,
        WaitingForTeleport = 1,
        Settling = 2,
        Ready = 3,
        WaitingForCancellation = 4
    }

    [Header("Refs")]
    [SerializeField, Tooltip("ReadySystem 참조(없으면 자동 탐색)")]
    private ReadySystem readySystem;

    [SerializeField, Tooltip("로비/게임 존 텔레포트를 담당하는 매니저(없으면 자동 탐색)")]
    private InGameMatchManager inGameMatchManager;

    [Header("시간 설정")]
    [SerializeField, Tooltip("카운트다운 시간(초)")]
    private float countdownSeconds = 3f;

    [SerializeField, Tooltip("플레이 시간(초)")]
    private float playSeconds = 120f;

    [SerializeField, Tooltip("결과 시간(초)")]
    private float resultsSeconds = 6f;

    [Header("Game Spawn Warmup")]
    [SerializeField, Min(0f)]
    private float minimumPostTeleportSettleSeconds = 1.25f;

    [SerializeField, Min(0.1f)]
    private float gameSpawnTransitionTimeoutSeconds = 5f;

    [Header("동작 옵션")]
    [SerializeField, Tooltip("Results 후 Lobby로 자동 복귀")]
    private bool autoReturnToLobby = true;

    [SerializeField, Tooltip("Lobby 진입 시 플레이어를 로비 존으로 다시 보낼지 여부")]
    private bool teleportPlayersOnEnterLobby = true;

    [SerializeField, Tooltip("Playing 진입 시 플레이어를 게임 존으로 보낼지 여부")]
    private bool teleportPlayersOnEnterPlaying = true;

    [Header("Cursor")]
    [SerializeField, Tooltip("Playing 상태 진입 시 마우스 커서를 숨기고 lock할지 여부입니다.")]
    private bool autoLockCursorOnPlaying = true;

    [SerializeField, Tooltip("Playing 외 상태에서 UI 조작을 위해 마우스 커서를 보이게 할지 여부입니다.")]
    private bool unlockCursorOutsidePlaying = true;

    [SerializeField, Tooltip("빌드에서 포커스를 되찾았을 때 Playing 상태라면 커서 lock을 다시 적용할지 여부입니다.")]
    private bool reapplyCursorLockOnFocus = true;

    [SerializeField, Tooltip("Playing 중 커서 lock이 풀렸을 때 재적용하는 최소 간격입니다.")]
    private float cursorLockRefreshInterval = 0.25f;

    [SerializeField, Tooltip("Playing 진입 직후 커서 lock/hide를 짧은 시간 동안 반복 적용할지 여부입니다.")]
    private bool enablePlayingCursorLockRetry = true;

    [SerializeField, Tooltip("Playing 커서 lock 재시도를 유지할 최대 시간(초)입니다.")]
    private float playingCursorLockRetryDuration = 0.75f;

    [SerializeField, Tooltip("Playing 커서 lock 재시도 사이의 간격(초)입니다.")]
    private float playingCursorLockRetryInterval = 0.1f;

    [SerializeField, Tooltip("Playing 진입 시 남아 있는 UI 선택 상태를 해제할지 여부입니다.")]
    private bool clearSelectedUiOnPlaying = true;

    [SerializeField, Tooltip("커서 lock/hide 보강 로그를 출력할지 여부입니다.")]
    private bool cursorDebugLogs = false;

    [Header("라운드 결과")]
    [SerializeField, Tooltip("Playing 상태에서 마지막 생존자 승리 판정을 사용할지 여부입니다.")]
    private bool enableLastSurvivorWinCheck = true;

    [SerializeField, Tooltip("생존자 수를 다시 확인하는 간격입니다.")]
    private float survivorCheckInterval = 0.25f;

    [SerializeField, Tooltip("마지막 생존자 승리 판정을 적용하기 위한 최소 라운드 참가자 수입니다.")]
    private int minimumParticipantsForLastSurvivorWin = 2;

    [SerializeField, Tooltip("승자가 없을 때 사용할 winner client id 값입니다.")]
    private ulong invalidWinnerClientId = ulong.MaxValue;

    [Header("Debug")]
    [SerializeField, Tooltip("디버그 로그 출력 여부입니다.")]
    private bool enableDebugLogs = false;

    [Tooltip("현재 게임 상태를 네트워크로 동기화하는 값입니다.")]
    public NetworkVariable<int> StateValue = new NetworkVariable<int>((int)GameState.Lobby);

    [Tooltip("현재 게임 상태의 남은 시간을 네트워크로 동기화하는 값입니다.")]
    public NetworkVariable<float> StateTimer = new NetworkVariable<float>(0f);

    [Tooltip("라운드 승자 client id를 네트워크로 동기화하는 값입니다.")]
    public NetworkVariable<ulong> WinnerClientIdValue =
        new NetworkVariable<ulong>(
            ulong.MaxValue,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server
        );

    [Tooltip("현재 라운드에 승자가 있는지 네트워크로 동기화하는 값입니다.")]
    public NetworkVariable<bool> RoundHasWinnerValue =
        new NetworkVariable<bool>(
            false,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server
        );

    [Tooltip("현재 라운드가 무승부인지 네트워크로 동기화하는 값입니다.")]
    public NetworkVariable<bool> RoundIsDrawValue =
        new NetworkVariable<bool>(
            false,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server
        );

    private readonly List<ulong> _roundParticipantClientIds = new List<ulong>();
    private readonly HashSet<ulong> _frozenZeroScoreParticipantClientIds =
        new HashSet<ulong>();
    private float _nextSurvivorCheckTime;
    private float _nextCursorLockRefreshAt;
    private Coroutine _playingCursorLockRetryRoutine;
    private int _lastPlayingCursorLockRetryStartFrame = -1;
    private bool _roundResultResolved;
    private bool _isStateValueChangeSubscribed;
    private bool _roundEndTransitionInProgress;
    private float _nextCountdownPostItAssignmentRetryTime;
    private int _roundRevision = -1;
    private int _guessRevision = -1;
    private PlayingEndReason _activePlayingEndReason = PlayingEndReason.Timer;
    private ulong _pendingSurvivorWinnerClientId = ulong.MaxValue;
    private PostItRoundManager _postItRoundManager;
    private NetworkManager _subscribedNetworkManager;
    private GameSpawnWarmupState _gameSpawnWarmupState;
    private int _gameSpawnTeleportRequestVersion = -1;
    private bool _gameSpawnTeleportRequestAccepted;
    private float _gameSpawnTeleportRequestStartedAt = -1f;
    private float _gameSpawnTeleportSucceededAt = -1f;
    private bool _gameSpawnTeleportSuccessObserved;
    private bool _gameSpawnWarmupBypass;
    private bool _gameSpawnWarmupAbortPending;
    private bool _gameSpawnTeleportCancellationRequested;
    private bool _gameSpawnWarmupLobbyReturnRequested;
    private bool _gameSpawnWarmupAuthorityErrorLogged;
    private string _gameSpawnWarmupAbortReason;
    private ulong[] _gameSpawnExpectedClientIds;
    private ulong[] _gameSpawnExpectedPlayerNetworkObjectIds;

    public bool HasRoundWinner => RoundHasWinnerValue.Value;
    public bool IsRoundDraw => RoundIsDrawValue.Value;
    public ulong WinnerClientId => WinnerClientIdValue.Value;

    public GameState GetState()
    {
        return (GameState)StateValue.Value;
    }

    public bool TryGetWinnerClientId(out ulong winnerClientId)
    {
        winnerClientId = WinnerClientIdValue.Value;
        return RoundHasWinnerValue.Value && winnerClientId != invalidWinnerClientId;
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        ResolveRefs();

        SubscribeGameStateCursorEvents();
        ApplyCursorStateForCurrentGameState("network-spawn");

        if (!IsServer) return;

        SubscribeNetworkCallbacksServer();
        EnterLobby(true);
    }

    public override void OnNetworkDespawn()
    {
        UnsubscribeNetworkCallbacksServer();
        UnsubscribeGameStateCursorEvents();
        StopPlayingCursorLockRetry("network-despawn");
        if (unlockCursorOutsidePlaying)
            SetCursorLocked(false, "network-despawn");

        _roundParticipantClientIds.Clear();
        _frozenZeroScoreParticipantClientIds.Clear();
        ResetGameSpawnWarmupTracking();

        base.OnNetworkDespawn();
    }

    public override void OnDestroy()
    {
        UnsubscribeNetworkCallbacksServer();
        UnsubscribeGameStateCursorEvents();
        StopPlayingCursorLockRetry("destroy");
        ResetGameSpawnWarmupTracking();
        base.OnDestroy();
    }

    private void ResolveRefs()
    {
        if (readySystem == null)
            readySystem = FindFirstObjectByType<ReadySystem>();

        if (inGameMatchManager == null)
            inGameMatchManager = FindFirstObjectByType<InGameMatchManager>();

        if (_postItRoundManager == null)
            _postItRoundManager = FindFirstObjectByType<PostItRoundManager>();

    }

    private void Update()
    {
        RefreshCursorLockIfNeeded();

        if (!IsServer) return;

        ResolveRefs();

        var state = GetState();

        if (state == GameState.Lobby)
        {
            if (_gameSpawnWarmupAbortPending ||
                _gameSpawnWarmupLobbyReturnRequested)
            {
                return;
            }

            if (readySystem != null && readySystem.CanStartGameServer() && readySystem.AreAllReady())
            {
                if (EnterCountdown())
                    readySystem.ResetAllReadyServer();
            }

            return;
        }

        bool countdownAssignmentReady = true;
        bool postItLiarCountdownActive = false;
        bool postItLiarCanEnterPlaying = true;
        if (state == GameState.Countdown &&
            Time.unscaledTime >= _nextCountdownPostItAssignmentRetryTime)
        {
            _nextCountdownPostItAssignmentRetryTime =
                Time.unscaledTime + CountdownPostItAssignmentRetryInterval;
            countdownAssignmentReady =
                AssignInitialPostItsForRoundServer(_roundRevision);
        }

        if (state == GameState.Countdown)
        {
            PostItRoundManager postItRoundManager =
                ResolvePostItRoundManager();
            postItLiarCountdownActive =
                IsValidServerPostItRoundManager(postItRoundManager) &&
                postItRoundManager.ServerIsPostItLiarRoundActive(
                    _roundRevision);
            if (postItLiarCountdownActive)
            {
                if (!postItRoundManager.ServerTryGetPostItLiarPlayingGate(
                        _roundRevision,
                        out postItLiarCanEnterPlaying,
                        out bool shouldAbortToLobby))
                {
                    AbortPostItLiarCountdownServer(
                        "Post-it liar playing gate validation failed.");
                    return;
                }

                StateTimer.Value = GetGuessDeadlineRemainingSeconds(
                    postItRoundManager.LiarPublicState
                        .DeadlineServerTime);
                if (shouldAbortToLobby)
                {
                    AbortPostItLiarCountdownServer(
                        "Post-it liar frozen roster changed before Playing.");
                    return;
                }
            }

            TickGameSpawnWarmupServer();
            if (GetState() != GameState.Countdown)
                return;

            if (!countdownAssignmentReady)
                return;

            if (postItLiarCountdownActive &&
                !postItLiarCanEnterPlaying)
            {
                return;
            }
        }

        if (state == GameState.Playing)
        {
            TickRoundEndCheckServer();
            if (GetState() != GameState.Playing)
                return;
        }

        if (state == GameState.Guessing)
        {
            TickGuessingServer();
            return;
        }

        float timer = StateTimer.Value;
        if (!postItLiarCountdownActive && timer > 0f)
        {
            timer -= Time.deltaTime;
            if (timer < 0f) timer = 0f;
            StateTimer.Value = timer;
        }

        if (StateTimer.Value > 0f)
            return;

        if (state == GameState.Countdown)
        {
            if (!_gameSpawnWarmupAbortPending &&
                !_gameSpawnTeleportCancellationRequested &&
                !_gameSpawnWarmupLobbyReturnRequested &&
                (_gameSpawnWarmupBypass ||
                 _gameSpawnWarmupState == GameSpawnWarmupState.Ready))
            {
                EnterPlaying();
            }
        }
        else if (state == GameState.Playing)
        {
            RequestPlayingEndAfterTimerServer();
        }
        else if (state == GameState.Results && autoReturnToLobby)
        {
            EnterLobby(false);
        }
    }

    private void EnterLobby(bool isInitialSpawn)
    {
        if (!isInitialSpawn && !TryResetPostItEliminationsBeforeLobbyServer())
        {
            Log("[GameStateManager] Post-it elimination reset failed. Lobby transition deferred.");
            return;
        }

        if (!isInitialSpawn && !TryClearGuessStateBeforeLobbyServer())
        {
            Log("[GameStateManager] Guess state clear failed. Lobby transition deferred.");
            return;
        }

        if (!isInitialSpawn && !TryClearPostItLiarStateBeforeLobbyServer())
        {
            Log("[GameStateManager] Post-it liar state clear failed. Lobby transition deferred.");
            return;
        }

        ResetGameSpawnWarmupTracking();

        StateValue.Value = (int)GameState.Lobby;
        ApplyCursorStateForCurrentGameState("enter-lobby");
        StateTimer.Value = 0f;
        ResetRoundResultServer();
        _guessRevision = -1;
        _activePlayingEndReason = PlayingEndReason.Timer;
        _pendingSurvivorWinnerClientId = invalidWinnerClientId;
        _roundEndTransitionInProgress = false;
        _roundParticipantClientIds.Clear();

        if (readySystem != null)
            readySystem.ResetAllReadyServer();

        if (!isInitialSpawn && teleportPlayersOnEnterLobby && inGameMatchManager != null && inGameMatchManager.IsSpawned)
        {
            inGameMatchManager.TeleportPlayersToLobbyServer();
        }

        Log("[GameStateManager] EnterLobby");
    }

    private bool EnterCountdown()
    {
        ResetGameSpawnWarmupTracking();

        if (!TryGetNextRoundRevisionServer(out int nextRoundRevision))
        {
            Log("[GameStateManager] Round revision could not advance.");
            return false;
        }

        if (!AssignInitialPostItsForRoundServer(nextRoundRevision))
        {
            Log("[GameStateManager] Initial post-it assignment failed before Countdown.");
            return false;
        }

        _roundRevision = nextRoundRevision;
        _guessRevision = -1;

        ResolveRefs();
        if (!teleportPlayersOnEnterPlaying)
        {
            _gameSpawnWarmupBypass = true;
        }
        else
        {
            if (inGameMatchManager == null ||
                !inGameMatchManager.IsSpawned ||
                NetworkManager == null ||
                !NetworkManager.IsServer ||
                !NetworkManager.IsListening ||
                !TryCaptureGameSpawnWarmupCohortServer(out ulong[] expectedClientIds))
            {
                FailGameSpawnWarmupStartServer(
                    "Game spawn warmup preflight or cohort capture failed.");
                return false;
            }

            float requestStartedAt = Time.unscaledTime;
            if (!IsFiniteGameSpawnWarmupTime(requestStartedAt) ||
                !inGameMatchManager.ServerTryStartGameSpawnTeleport(
                    out int requestVersion) ||
                requestVersion <= 0)
            {
                FailGameSpawnWarmupStartServer(
                    "Strict game spawn teleport request was not accepted.");
                return false;
            }

            _gameSpawnExpectedClientIds = expectedClientIds;
            _gameSpawnTeleportRequestVersion = requestVersion;
            _gameSpawnTeleportRequestAccepted = true;
            _gameSpawnTeleportRequestStartedAt = requestStartedAt;
            _gameSpawnWarmupState = GameSpawnWarmupState.WaitingForTeleport;
        }

        StateValue.Value = (int)GameState.Countdown;
        ApplyCursorStateForCurrentGameState("enter-countdown");
        PostItRoundManager postItRoundManager =
            ResolvePostItRoundManager();
        if (!IsValidServerPostItRoundManager(postItRoundManager) ||
            !postItRoundManager.ServerStartPreparedPostItLiarRound(
                _roundRevision))
        {
            AbortPostItLiarCountdownServer(
                "Post-it liar round start failed.");
            return false;
        }

        StateTimer.Value =
            postItRoundManager.ServerIsPostItLiarRoundActive(
                _roundRevision)
                ? GetGuessDeadlineRemainingSeconds(
                    postItRoundManager.LiarPublicState
                        .DeadlineServerTime)
                : countdownSeconds;
        _nextCountdownPostItAssignmentRetryTime =
            Time.unscaledTime + CountdownPostItAssignmentRetryInterval;

        Log("[GameStateManager] EnterCountdown");
        return true;
    }

    private void EnterPlaying()
    {
        if (!TryValidateGameSpawnWarmupReadyForPlayingServer(
                out string warmupFailureReason,
                out bool canReturnToLobby))
        {
            if (canReturnToLobby)
                AbortGameSpawnWarmupToLobbyServer(warmupFailureReason);
            else
                MarkGameSpawnWarmupAuthorityError(warmupFailureReason);
            return;
        }

        if (!AssignInitialPostItsForRoundServer(_roundRevision))
        {
            Log("[GameStateManager] Playing transition deferred until initial post-it assignment completes.");
            return;
        }

        PostItRoundManager postItRoundManager =
            ResolvePostItRoundManager();
        if (!IsValidServerPostItRoundManager(postItRoundManager) ||
            !postItRoundManager.ServerTryCommitPostItLiarBrawlStart(
                _roundRevision))
        {
            AbortPostItLiarCountdownServer(
                "Post-it liar Brawl commit failed.");
            return;
        }

        ResetRoundResultServer();
        CaptureRoundParticipantsServer();

        StateValue.Value = (int)GameState.Playing;
        HandleEnteredPlayingForCursor("enter-playing");
        StateTimer.Value = playSeconds;
        _nextSurvivorCheckTime = Time.unscaledTime + Mathf.Max(0f, survivorCheckInterval);

        ResetGameSpawnWarmupTracking();

        Log("[GameStateManager] EnterPlaying");
    }

    private bool TryCaptureGameSpawnWarmupCohortServer(
        out ulong[] expectedClientIds)
    {
        expectedClientIds = null;

        NetworkManager networkManager = NetworkManager;
        if (!IsServer ||
            networkManager == null ||
            !networkManager.IsServer ||
            !networkManager.IsListening)
        {
            return false;
        }

        int clientCount = networkManager.ConnectedClientsIds.Count;
        if (clientCount <= 0 || networkManager.ConnectedClients.Count != clientCount)
            return false;

        ulong[] candidateClientIds = new ulong[clientCount];
        for (int i = 0; i < clientCount; i++)
            candidateClientIds[i] = networkManager.ConnectedClientsIds[i];

        System.Array.Sort(candidateClientIds);
        for (int i = 0; i < candidateClientIds.Length; i++)
        {
            if (i > 0 && candidateClientIds[i - 1] == candidateClientIds[i])
                return false;

            if (!networkManager.ConnectedClients.ContainsKey(candidateClientIds[i]))
                return false;
        }

        expectedClientIds = candidateClientIds;
        return true;
    }

    private void TickGameSpawnWarmupServer()
    {
        if (!IsServer || GetState() != GameState.Countdown || _gameSpawnWarmupBypass)
            return;

        if (!_gameSpawnTeleportRequestAccepted ||
            _gameSpawnTeleportRequestVersion <= 0)
        {
            MarkGameSpawnWarmupAuthorityError(
                "Countdown has no accepted strict game spawn teleport request.");
            return;
        }

        if (!TryGetGameSpawnWarmupSnapshotServer(
                out InGameMatchManager.ServerGameTeleportSnapshot snapshot) ||
            snapshot.RequestVersion != _gameSpawnTeleportRequestVersion)
        {
            MarkGameSpawnWarmupAuthorityError(
                "Strict game spawn teleport status query lost the exact request version.");
            return;
        }

        if (_gameSpawnWarmupAbortPending)
        {
            if (snapshot.Phase == InGameMatchManager.ServerGameTeleportPhase.InProgress)
            {
                RequestGameSpawnWarmupCancellationServer(
                    _gameSpawnWarmupAbortReason ??
                    "Game spawn warmup abort is pending.");
                return;
            }

            if (snapshot.Phase == InGameMatchManager.ServerGameTeleportPhase.Completed)
            {
                AbortGameSpawnWarmupToLobbyServer(
                    _gameSpawnWarmupAbortReason ??
                    "Game spawn warmup reached terminal after abort.");
                return;
            }

            MarkGameSpawnWarmupAuthorityError(
                "Strict game spawn teleport returned an invalid phase while aborting.");
            return;
        }

        if (snapshot.Phase == InGameMatchManager.ServerGameTeleportPhase.InProgress)
        {
            TickInProgressGameSpawnWarmupServer(snapshot);
            return;
        }

        if (snapshot.Phase != InGameMatchManager.ServerGameTeleportPhase.Completed)
        {
            MarkGameSpawnWarmupAuthorityError(
                "Strict game spawn teleport returned an invalid phase.");
            return;
        }

        if (snapshot.Outcome == InGameMatchManager.ServerGameTeleportOutcome.Failed ||
            snapshot.Outcome == InGameMatchManager.ServerGameTeleportOutcome.Canceled)
        {
            AbortGameSpawnWarmupToLobbyServer(
                $"Strict game spawn teleport ended with {snapshot.Outcome}.");
            return;
        }

        if (snapshot.Outcome != InGameMatchManager.ServerGameTeleportOutcome.Succeeded)
        {
            AbortGameSpawnWarmupToLobbyServer(
                "Strict game spawn teleport completed without a valid terminal outcome.");
            return;
        }

        TickSucceededGameSpawnWarmupServer();
    }

    private void TickInProgressGameSpawnWarmupServer(
        InGameMatchManager.ServerGameTeleportSnapshot snapshot)
    {
        if (snapshot.Outcome != InGameMatchManager.ServerGameTeleportOutcome.None)
        {
            MarkGameSpawnWarmupAuthorityError(
                "In-progress strict game spawn teleport has a terminal outcome.");
            return;
        }

        if (_gameSpawnWarmupState == GameSpawnWarmupState.WaitingForCancellation)
            return;

        if (_gameSpawnWarmupState != GameSpawnWarmupState.WaitingForTeleport)
        {
            MarkGameSpawnWarmupAuthorityError(
                "In-progress strict game spawn teleport has an invalid local state.");
            return;
        }

        NetworkManager networkManager = NetworkManager;
        if (!DoesGameSpawnWarmupCohortMatchServer(networkManager))
        {
            RequestGameSpawnWarmupCancellationServer(
                "Connected client cohort changed before teleport completion.");
            return;
        }

        if (!IsFiniteGameSpawnWarmupTime(_gameSpawnTeleportRequestStartedAt))
        {
            RequestGameSpawnWarmupCancellationServer(
                "Strict game spawn teleport start time is invalid.");
            return;
        }

        float elapsed = Time.unscaledTime - _gameSpawnTeleportRequestStartedAt;
        if (!IsFiniteGameSpawnWarmupTime(elapsed))
        {
            RequestGameSpawnWarmupCancellationServer(
                "Strict game spawn teleport elapsed time is invalid.");
            return;
        }

        if (elapsed >= Mathf.Max(0.1f, gameSpawnTransitionTimeoutSeconds))
        {
            RequestGameSpawnWarmupCancellationServer(
                "Strict game spawn teleport timed out.");
        }
    }

    private void TickSucceededGameSpawnWarmupServer()
    {
        if (!_gameSpawnTeleportSuccessObserved)
        {
            if (!TryObserveGameSpawnWarmupSuccessServer(out string failureReason))
            {
                AbortGameSpawnWarmupToLobbyServer(failureReason);
                return;
            }

            return;
        }

        if (_gameSpawnWarmupState != GameSpawnWarmupState.Settling &&
            _gameSpawnWarmupState != GameSpawnWarmupState.Ready)
        {
            AbortGameSpawnWarmupToLobbyServer(
                "Succeeded game spawn warmup has an invalid local state.");
            return;
        }

        if (!TryValidateGameSpawnWarmupPlayerIdentityServer(
                out string identityFailureReason))
        {
            AbortGameSpawnWarmupToLobbyServer(identityFailureReason);
            return;
        }

        if (_gameSpawnWarmupState == GameSpawnWarmupState.Ready)
            return;

        if (!IsFiniteGameSpawnWarmupTime(_gameSpawnTeleportSucceededAt))
        {
            AbortGameSpawnWarmupToLobbyServer(
                "Game spawn warmup success time is invalid.");
            return;
        }

        float settleElapsed = Time.unscaledTime - _gameSpawnTeleportSucceededAt;
        if (!IsFiniteGameSpawnWarmupTime(settleElapsed))
        {
            AbortGameSpawnWarmupToLobbyServer(
                "Game spawn warmup settle elapsed time is invalid.");
            return;
        }

        if (settleElapsed >= Mathf.Max(0f, minimumPostTeleportSettleSeconds) &&
            StateTimer.Value <= 0f)
        {
            _gameSpawnWarmupState = GameSpawnWarmupState.Ready;
        }
    }

    private bool TryObserveGameSpawnWarmupSuccessServer(out string failureReason)
    {
        failureReason = null;
        NetworkManager networkManager = NetworkManager;
        if (!DoesGameSpawnWarmupCohortMatchServer(networkManager) ||
            _gameSpawnExpectedClientIds == null ||
            _gameSpawnExpectedClientIds.Length == 0)
        {
            failureReason =
                "Connected client cohort changed before success observation.";
            return false;
        }

        for (int i = 0; i < _gameSpawnExpectedClientIds.Length; i++)
        {
            if (!TryResolveGameSpawnWarmupPlayerObjectServer(
                    networkManager,
                    _gameSpawnExpectedClientIds[i],
                    out NetworkObject playerObject) ||
                playerObject.NetworkObjectId == ulong.MaxValue)
            {
                failureReason =
                    "A success cohort PlayerObject is missing or invalid.";
                return false;
            }
        }

        ulong[] playerNetworkObjectIds =
            new ulong[_gameSpawnExpectedClientIds.Length];
        for (int i = 0; i < _gameSpawnExpectedClientIds.Length; i++)
        {
            if (!TryResolveGameSpawnWarmupPlayerObjectServer(
                    networkManager,
                    _gameSpawnExpectedClientIds[i],
                    out NetworkObject playerObject))
            {
                failureReason =
                    "A success cohort PlayerObject changed during snapshot capture.";
                return false;
            }

            playerNetworkObjectIds[i] = playerObject.NetworkObjectId;
        }

        float successObservedAt = Time.unscaledTime;
        if (!IsFiniteGameSpawnWarmupTime(successObservedAt))
        {
            failureReason = "Game spawn warmup success time is invalid.";
            return false;
        }

        _gameSpawnExpectedPlayerNetworkObjectIds = playerNetworkObjectIds;
        _gameSpawnTeleportSucceededAt = successObservedAt;
        _gameSpawnTeleportSuccessObserved = true;
        _gameSpawnWarmupState = GameSpawnWarmupState.Settling;
        return true;
    }

    private bool TryValidateGameSpawnWarmupReadyForPlayingServer(
        out string failureReason,
        out bool canReturnToLobby)
    {
        failureReason = null;
        canReturnToLobby = false;

        if (!IsServer || GetState() != GameState.Countdown)
        {
            failureReason =
                "EnterPlaying requires the authoritative Countdown state.";
            return false;
        }

        if (_gameSpawnWarmupAbortPending ||
            _gameSpawnTeleportCancellationRequested ||
            _gameSpawnWarmupLobbyReturnRequested)
        {
            failureReason =
                "EnterPlaying is blocked by a pending game spawn warmup abort.";
            return false;
        }

        if (!teleportPlayersOnEnterPlaying)
        {
            if (_gameSpawnWarmupBypass && !_gameSpawnTeleportRequestAccepted)
                return true;

            failureReason = "Game spawn warmup bypass state is invalid.";
            return false;
        }

        if (_gameSpawnWarmupBypass ||
            !_gameSpawnTeleportRequestAccepted ||
            _gameSpawnTeleportRequestVersion <= 0 ||
            !_gameSpawnTeleportSuccessObserved ||
            _gameSpawnWarmupState != GameSpawnWarmupState.Ready)
        {
            failureReason = "Game spawn warmup is not ready for Playing.";
            canReturnToLobby = !_gameSpawnTeleportRequestAccepted;
            return false;
        }

        if (!TryGetGameSpawnWarmupSnapshotServer(
                out InGameMatchManager.ServerGameTeleportSnapshot snapshot) ||
            snapshot.RequestVersion != _gameSpawnTeleportRequestVersion)
        {
            failureReason =
                "EnterPlaying lost the exact strict game spawn request status.";
            return false;
        }

        if (snapshot.Phase != InGameMatchManager.ServerGameTeleportPhase.Completed ||
            snapshot.Outcome != InGameMatchManager.ServerGameTeleportOutcome.Succeeded)
        {
            failureReason =
                "EnterPlaying requires Completed/Succeeded game spawn status.";
            canReturnToLobby =
                snapshot.Phase == InGameMatchManager.ServerGameTeleportPhase.Completed;
            return false;
        }

        if (!TryValidateGameSpawnWarmupPlayerIdentityServer(out failureReason))
        {
            canReturnToLobby = true;
            return false;
        }

        if (!IsFiniteGameSpawnWarmupTime(_gameSpawnTeleportSucceededAt))
        {
            failureReason = "EnterPlaying has an invalid settle start time.";
            canReturnToLobby = true;
            return false;
        }

        float settleElapsed = Time.unscaledTime - _gameSpawnTeleportSucceededAt;
        if (!IsFiniteGameSpawnWarmupTime(settleElapsed) ||
            settleElapsed < Mathf.Max(0f, minimumPostTeleportSettleSeconds))
        {
            failureReason = "EnterPlaying minimum settle time is incomplete.";
            canReturnToLobby = true;
            return false;
        }

        return true;
    }

    private bool TryValidateGameSpawnWarmupPlayerIdentityServer(
        out string failureReason)
    {
        failureReason = null;
        NetworkManager networkManager = NetworkManager;
        if (!DoesGameSpawnWarmupCohortMatchServer(networkManager) ||
            _gameSpawnExpectedClientIds == null ||
            _gameSpawnExpectedPlayerNetworkObjectIds == null ||
            _gameSpawnExpectedClientIds.Length == 0 ||
            _gameSpawnExpectedClientIds.Length !=
                _gameSpawnExpectedPlayerNetworkObjectIds.Length)
        {
            failureReason = "Game spawn warmup cohort identity is invalid.";
            return false;
        }

        for (int i = 0; i < _gameSpawnExpectedClientIds.Length; i++)
        {
            if (!TryResolveGameSpawnWarmupPlayerObjectServer(
                    networkManager,
                    _gameSpawnExpectedClientIds[i],
                    out NetworkObject playerObject) ||
                playerObject.NetworkObjectId == ulong.MaxValue ||
                playerObject.NetworkObjectId !=
                    _gameSpawnExpectedPlayerNetworkObjectIds[i])
            {
                failureReason =
                    "Game spawn warmup PlayerObject identity changed during settle.";
                return false;
            }
        }

        return true;
    }

    private bool TryGetGameSpawnWarmupSnapshotServer(
        out InGameMatchManager.ServerGameTeleportSnapshot snapshot)
    {
        snapshot = default;
        return IsServer &&
               inGameMatchManager != null &&
               inGameMatchManager.IsServer &&
               inGameMatchManager.IsSpawned &&
               NetworkManager != null &&
               NetworkManager.IsServer &&
               NetworkManager.IsListening &&
               inGameMatchManager.ServerTryGetGameSpawnTeleportStatus(
                   _gameSpawnTeleportRequestVersion,
                   out snapshot);
    }

    private bool DoesGameSpawnWarmupCohortMatchServer(
        NetworkManager networkManager)
    {
        if (!IsServer ||
            networkManager == null ||
            !networkManager.IsServer ||
            !networkManager.IsListening ||
            _gameSpawnExpectedClientIds == null ||
            _gameSpawnExpectedClientIds.Length == 0 ||
            networkManager.ConnectedClients.Count !=
                _gameSpawnExpectedClientIds.Length ||
            networkManager.ConnectedClientsIds.Count !=
                _gameSpawnExpectedClientIds.Length)
        {
            return false;
        }

        for (int i = 0; i < _gameSpawnExpectedClientIds.Length; i++)
        {
            if (!networkManager.ConnectedClients.ContainsKey(
                    _gameSpawnExpectedClientIds[i]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryResolveGameSpawnWarmupPlayerObjectServer(
        NetworkManager networkManager,
        ulong clientId,
        out NetworkObject playerObject)
    {
        playerObject = null;
        if (networkManager == null ||
            !networkManager.ConnectedClients.TryGetValue(clientId, out var client) ||
            client == null)
        {
            return false;
        }

        playerObject = client.PlayerObject;
        return playerObject != null &&
               playerObject.IsSpawned &&
               playerObject.OwnerClientId == clientId;
    }

    private void RequestGameSpawnWarmupCancellationServer(string reason)
    {
        _gameSpawnWarmupAbortPending = true;
        if (string.IsNullOrEmpty(_gameSpawnWarmupAbortReason))
            _gameSpawnWarmupAbortReason = reason;

        if (_gameSpawnTeleportCancellationRequested)
            return;

        if (inGameMatchManager == null ||
            !inGameMatchManager.IsServer ||
            !inGameMatchManager.IsSpawned)
        {
            MarkGameSpawnWarmupAuthorityError(
                "Cannot request strict game spawn teleport cancellation.");
            return;
        }

        _gameSpawnTeleportCancellationRequested = true;
        _gameSpawnWarmupState = GameSpawnWarmupState.WaitingForCancellation;
        Debug.LogWarning(
            $"[GameStateManager] Game spawn warmup cancellation requested. reason:{_gameSpawnWarmupAbortReason}",
            this);
        inGameMatchManager.TeleportPlayersToLobbyServer();
    }

    private void AbortGameSpawnWarmupToLobbyServer(string reason)
    {
        _gameSpawnWarmupAbortPending = true;
        if (string.IsNullOrEmpty(_gameSpawnWarmupAbortReason))
            _gameSpawnWarmupAbortReason = reason;

        if (_gameSpawnWarmupLobbyReturnRequested)
            return;

        _gameSpawnWarmupLobbyReturnRequested = true;
        Debug.LogWarning(
            $"[GameStateManager] Game spawn warmup aborted to Lobby. reason:{_gameSpawnWarmupAbortReason}",
            this);
        EnterLobby(false);
    }

    private void AbortPostItLiarCountdownServer(string reason)
    {
        if (_gameSpawnWarmupBypass ||
            _gameSpawnWarmupState == GameSpawnWarmupState.Settling ||
            _gameSpawnWarmupState == GameSpawnWarmupState.Ready)
        {
            AbortGameSpawnWarmupToLobbyServer(reason);
            return;
        }

        RequestGameSpawnWarmupCancellationServer(reason);
    }

    private void FailGameSpawnWarmupStartServer(string reason)
    {
        _gameSpawnWarmupAbortPending = true;
        _gameSpawnWarmupAbortReason = reason;
        _gameSpawnWarmupLobbyReturnRequested = true;
        Debug.LogError(
            $"[GameStateManager] Game spawn warmup could not start. reason:{reason}",
            this);
        EnterLobby(false);
    }

    private void MarkGameSpawnWarmupAuthorityError(string reason)
    {
        _gameSpawnWarmupAbortPending = true;
        if (string.IsNullOrEmpty(_gameSpawnWarmupAbortReason))
            _gameSpawnWarmupAbortReason = reason;

        if (_gameSpawnWarmupAuthorityErrorLogged)
            return;

        _gameSpawnWarmupAuthorityErrorLogged = true;
        Debug.LogError(
            $"[GameStateManager] Game spawn warmup authority error. reason:{_gameSpawnWarmupAbortReason}",
            this);
    }

    private void ResetGameSpawnWarmupTracking()
    {
        _gameSpawnWarmupState = GameSpawnWarmupState.None;
        _gameSpawnTeleportRequestVersion = -1;
        _gameSpawnTeleportRequestAccepted = false;
        _gameSpawnTeleportRequestStartedAt = -1f;
        _gameSpawnTeleportSucceededAt = -1f;
        _gameSpawnTeleportSuccessObserved = false;
        _gameSpawnWarmupBypass = false;
        _gameSpawnWarmupAbortPending = false;
        _gameSpawnTeleportCancellationRequested = false;
        _gameSpawnWarmupLobbyReturnRequested = false;
        _gameSpawnWarmupAuthorityErrorLogged = false;
        _gameSpawnWarmupAbortReason = null;
        _gameSpawnExpectedClientIds = null;
        _gameSpawnExpectedPlayerNetworkObjectIds = null;
    }

    private static bool IsFiniteGameSpawnWarmupTime(float value)
    {
        return !float.IsNaN(value) &&
               !float.IsInfinity(value) &&
               value >= 0f;
    }

    private bool AssignInitialPostItsForRoundServer(int roundRevision)
    {
        if (!IsServer || roundRevision < 0)
            return false;

        PostItRoundManager postItRoundManager = ResolvePostItRoundManager();
        if (!IsValidServerPostItRoundManager(postItRoundManager))
        {
            Log("[GameStateManager] Server PostItRoundManager not ready for initial assignment.");
            return false;
        }

        if (!TryBuildCountdownAssignmentInventoriesServer(
                out List<PlayerPostItInventory> inventories))
        {
            return false;
        }

        return postItRoundManager.ServerAssignInitialPostIts(
                   inventories,
                   roundRevision) &&
               postItRoundManager.ServerEnsurePostItLiarRoundPrepared(
                   inventories,
                   roundRevision);
    }

    private void EnterResults()
    {
        if (GetState() == GameState.Results)
            return;

        StateValue.Value = (int)GameState.Results;
        ApplyCursorStateForCurrentGameState("enter-results");
        StateTimer.Value = resultsSeconds;

        Log("[GameStateManager] EnterResults");
    }

    private void CaptureRoundParticipantsServer()
    {
        _roundParticipantClientIds.Clear();

        if (!IsServer) return;

        NetworkManager networkManager = NetworkManager.Singleton;
        if (networkManager == null) return;

        List<ulong> clientIds = new List<ulong>(networkManager.ConnectedClientsIds);
        clientIds.Sort();

        for (int i = 0; i < clientIds.Count; i++)
        {
            ulong clientId = clientIds[i];
            if (!networkManager.ConnectedClients.TryGetValue(clientId, out var client) || client == null)
                continue;

            NetworkObject playerObject = client.PlayerObject;
            if (playerObject == null || !playerObject.IsSpawned)
                continue;

            _roundParticipantClientIds.Add(clientId);
        }
    }

    private void ResetRoundResultServer()
    {
        if (!IsServer) return;

        WinnerClientIdValue.Value = invalidWinnerClientId;
        RoundHasWinnerValue.Value = false;
        RoundIsDrawValue.Value = false;
        _roundResultResolved = false;
        _nextSurvivorCheckTime = 0f;
        _roundEndTransitionInProgress = false;
        _activePlayingEndReason = PlayingEndReason.Timer;
        _pendingSurvivorWinnerClientId = invalidWinnerClientId;
        _frozenZeroScoreParticipantClientIds.Clear();
    }

    private void TickRoundEndCheckServer()
    {
        if (!CanUseLastSurvivorWinCheck())
            return;

        float now = Time.unscaledTime;
        if (now < _nextSurvivorCheckTime)
            return;

        if (!TryFlushZeroPostItEliminationsServer())
            return;

        if (!TryCountAliveParticipantsServer(
                out int aliveCount,
                out ulong lastAliveClientId))
        {
            return;
        }

        _nextSurvivorCheckTime = now + Mathf.Max(0f, survivorCheckInterval);
        if (aliveCount == 1)
        {
            RequestPlayingEndServer(
                PlayingEndReason.LastSurvivor,
                lastAliveClientId);
        }
        else if (aliveCount == 0)
        {
            RequestPlayingEndServer(
                PlayingEndReason.NoSurvivors,
                invalidWinnerClientId);
        }
    }

    private bool TryCountAliveParticipantsServer(
        out int aliveCount,
        out ulong lastAliveClientId)
    {
        aliveCount = 0;
        lastAliveClientId = invalidWinnerClientId;
        if (!IsServer || NetworkManager == null || !NetworkManager.IsListening)
            return false;

        for (int i = 0; i < _roundParticipantClientIds.Count; i++)
        {
            ulong clientId = _roundParticipantClientIds[i];
            if (!NetworkManager.ConnectedClients.TryGetValue(clientId, out var client))
                continue;

            if (client == null)
                return false;

            NetworkObject playerObject = client.PlayerObject;
            if (playerObject == null || !playerObject.IsSpawned)
                continue;

            if (!TryResolveSpawnedPlayerStatus(
                    playerObject,
                    clientId,
                    out PlayerStatusModule status))
            {
                return false;
            }

            if (status.IsEliminated)
                continue;

            aliveCount++;
            lastAliveClientId = clientId;
        }

        return true;
    }

    private void ResolveRoundWinnerServer(ulong winnerClientId)
    {
        if (!IsServer) return;
        if (_roundResultResolved) return;

        WinnerClientIdValue.Value = winnerClientId;
        RoundHasWinnerValue.Value = true;
        RoundIsDrawValue.Value = false;
        _roundResultResolved = true;

        EnterResults();
    }

    private void ResolveRoundDrawServer()
    {
        if (!IsServer) return;
        if (_roundResultResolved) return;

        WinnerClientIdValue.Value = invalidWinnerClientId;
        RoundHasWinnerValue.Value = false;
        RoundIsDrawValue.Value = true;
        _roundResultResolved = true;

        EnterResults();
    }

    private bool TryGetNextRoundRevisionServer(out int nextRoundRevision)
    {
        nextRoundRevision = -1;
        if (!IsServer || _roundRevision == int.MaxValue)
            return false;

        nextRoundRevision = _roundRevision + 1;
        return true;
    }

    private void RequestPlayingEndAfterTimerServer()
    {
        if (!TryFlushZeroPostItEliminationsServer())
            return;

        if (!TryCountAliveParticipantsServer(
                out int aliveCount,
                out ulong lastAliveClientId))
        {
            return;
        }
        if (aliveCount == 1)
        {
            RequestPlayingEndServer(
                PlayingEndReason.LastSurvivor,
                lastAliveClientId);
        }
        else if (aliveCount == 0)
        {
            RequestPlayingEndServer(
                PlayingEndReason.NoSurvivors,
                invalidWinnerClientId);
        }
        else
        {
            RequestPlayingEndServer(
                PlayingEndReason.Timer,
                invalidWinnerClientId);
        }
    }

    private void RequestPlayingEndServer(
        PlayingEndReason reason,
        ulong survivorWinnerClientId)
    {
        if (!IsServer ||
            _roundEndTransitionInProgress ||
            _roundResultResolved ||
            GetState() != GameState.Playing ||
            _roundRevision < 0 ||
            (reason == PlayingEndReason.LastSurvivor &&
             survivorWinnerClientId == invalidWinnerClientId))
        {
            return;
        }

        _roundEndTransitionInProgress = true;
        try
        {
            PostItRoundManager postItRoundManager = ResolvePostItRoundManager();
            if (!IsValidServerPostItRoundManager(postItRoundManager) ||
                !TryBuildRoundParticipantScoreInputsServer(
                    out List<PlayerPostItInventory> inventories,
                    out List<ulong> zeroScoreOwnerClientIds) ||
                (reason == PlayingEndReason.LastSurvivor &&
                 zeroScoreOwnerClientIds.Contains(survivorWinnerClientId)))
            {
                Log("[GameStateManager] Playing end snapshot preparation failed.");
                return;
            }

            if (postItRoundManager.ServerIsPostItLiarRoundActive(
                    _roundRevision))
            {
                if (!postItRoundManager.ServerBeginPostItLiarDeduction(
                        inventories,
                        zeroScoreOwnerClientIds,
                        _roundRevision,
                        out double liarDeadlineServerTime))
                {
                    Log("[GameStateManager] Post-it liar deduction begin failed.");
                    return;
                }

                _guessRevision = -1;
                _frozenZeroScoreParticipantClientIds.Clear();
                for (int ownerIndex = 0;
                     ownerIndex < zeroScoreOwnerClientIds.Count;
                     ownerIndex++)
                {
                    _frozenZeroScoreParticipantClientIds.Add(
                        zeroScoreOwnerClientIds[ownerIndex]);
                }

                _activePlayingEndReason = reason;
                _pendingSurvivorWinnerClientId =
                    reason == PlayingEndReason.LastSurvivor
                        ? survivorWinnerClientId
                        : invalidWinnerClientId;
                EnterGuessingServer(liarDeadlineServerTime);
                return;
            }

            int guessRevision = _roundRevision;
            if (!postItRoundManager.ServerBeginGuessing(
                    inventories,
                    zeroScoreOwnerClientIds,
                    _roundRevision,
                    guessRevision,
                    out int totalEligibleCount,
                    out double absoluteDeadlineServerTime))
            {
                Log("[GameStateManager] Guessing begin failed.");
                return;
            }

            _guessRevision = guessRevision;
            _frozenZeroScoreParticipantClientIds.Clear();
            for (int ownerIndex = 0;
                 ownerIndex < zeroScoreOwnerClientIds.Count;
                 ownerIndex++)
            {
                _frozenZeroScoreParticipantClientIds.Add(
                    zeroScoreOwnerClientIds[ownerIndex]);
            }
            _activePlayingEndReason = reason;
            _pendingSurvivorWinnerClientId = reason == PlayingEndReason.LastSurvivor
                ? survivorWinnerClientId
                : invalidWinnerClientId;

            if (totalEligibleCount == 0)
            {
                if (!TryPublishFinalGuessResultServer(postItRoundManager))
                {
                    EnterGuessingServer(absoluteDeadlineServerTime);
                    Log("[GameStateManager] Zero-candidate score publication deferred.");
                }

                return;
            }

            EnterGuessingServer(absoluteDeadlineServerTime);
            if (reason != PlayingEndReason.Timer &&
                !TryFinalizeGuessFlowCore(postItRoundManager, true))
            {
                Log("[GameStateManager] Immediate terminal Guess finalize deferred.");
            }
        }
        finally
        {
            _roundEndTransitionInProgress = false;
        }
    }

    private void EnterGuessingServer(double absoluteDeadlineServerTime)
    {
        StateValue.Value = (int)GameState.Guessing;
        ApplyCursorStateForCurrentGameState("enter-guessing");
        StateTimer.Value = GetGuessDeadlineRemainingSeconds(
            absoluteDeadlineServerTime);

        Log("[GameStateManager] EnterGuessing");
    }

    private void TickGuessingServer()
    {
        if (!IsServer || GetState() != GameState.Guessing)
            return;

        PostItRoundManager postItRoundManager = ResolvePostItRoundManager();
        if (!IsValidServerPostItRoundManager(postItRoundManager))
            return;

        if (postItRoundManager.ServerIsPostItLiarRoundActive(
                _roundRevision))
        {
            PostItLiarPhaseState liarState =
                postItRoundManager.LiarPublicState;
            StateTimer.Value = GetGuessDeadlineRemainingSeconds(
                liarState.DeadlineServerTime);
            if (liarState.Phase == PostItLiarPhase.Complete)
            {
                TryPublishFinalPostItLiarResultServer(
                    postItRoundManager);
            }

            return;
        }

        if (postItRoundManager.ActiveGuessRoundRevision != _roundRevision ||
            postItRoundManager.ActiveGuessRevision != _guessRevision)
        {
            Log("[GameStateManager] Active Guess state does not match GameState revisions.");
            return;
        }

        float remainingSeconds = GetGuessDeadlineRemainingSeconds(
            postItRoundManager.GuessDeadlineServerTime);
        StateTimer.Value = remainingSeconds;

        bool finalizeImmediately =
            _activePlayingEndReason != PlayingEndReason.Timer;
        if (!finalizeImmediately &&
            !postItRoundManager.AreAllGuessEntriesResolved &&
            remainingSeconds > 0f)
        {
            return;
        }

        TryFinalizeGuessFlowServer(finalizeImmediately);
    }

    private bool TryFinalizeGuessFlowServer(bool finalizeImmediately)
    {
        if (!IsServer ||
            _roundEndTransitionInProgress ||
            _roundResultResolved ||
            GetState() != GameState.Guessing)
        {
            return false;
        }

        _roundEndTransitionInProgress = true;
        try
        {
            PostItRoundManager postItRoundManager = ResolvePostItRoundManager();
            return IsValidServerPostItRoundManager(postItRoundManager) &&
                   TryFinalizeGuessFlowCore(
                       postItRoundManager,
                       finalizeImmediately);
        }
        finally
        {
            _roundEndTransitionInProgress = false;
        }
    }

    private bool TryFinalizeGuessFlowCore(
        PostItRoundManager postItRoundManager,
        bool finalizeImmediately)
    {
        bool finalized = finalizeImmediately
            ? postItRoundManager.ServerFinalizeGuessingImmediately(
                _roundRevision,
                _guessRevision)
            : postItRoundManager.ServerFinalizeGuessing(
                _roundRevision,
                _guessRevision);
        return finalized &&
               TryPublishFinalGuessResultServer(postItRoundManager);
    }

    private bool TryPublishFinalGuessResultServer(
        PostItRoundManager postItRoundManager)
    {
        if (!postItRoundManager.ServerTryBuildFinalRoundScores(
                _roundRevision,
                _guessRevision,
                out PostItGuessPlayerScoreData[] scores))
        {
            return false;
        }

        if (!ValidateFinalScoreSetForRound(scores))
            return false;

        if (_activePlayingEndReason == PlayingEndReason.LastSurvivor)
        {
            if (_frozenZeroScoreParticipantClientIds.Contains(
                    _pendingSurvivorWinnerClientId) ||
                !ContainsFinalScoreForOwner(
                    scores,
                    _pendingSurvivorWinnerClientId))
            {
                return false;
            }

            ResolveRoundWinnerServer(_pendingSurvivorWinnerClientId);
            return _roundResultResolved;
        }

        if (_activePlayingEndReason == PlayingEndReason.NoSurvivors)
        {
            if (_frozenZeroScoreParticipantClientIds.Count !=
                _roundParticipantClientIds.Count)
            {
                return false;
            }

            ResolveRoundDrawServer();
            return _roundResultResolved;
        }

        return TryResolveRoundResultFromScoresServer(scores);
    }

    private bool TryPublishFinalPostItLiarResultServer(
        PostItRoundManager postItRoundManager)
    {
        if (_roundResultResolved)
            return true;

        if (!postItRoundManager.ServerTryBuildFinalPostItLiarResults(
                _roundRevision,
                out PostItLiarPlayerResultData[] results) ||
            !ValidateFinalPostItLiarResultSetForRound(
                postItRoundManager,
                results,
                out ulong[] ownerClientIds))
        {
            return false;
        }

        int highestScore = int.MinValue;
        ulong highestScoreOwnerClientId = invalidWinnerClientId;
        bool highestScoreIsTied = false;
        for (int index = 0; index < results.Length; index++)
        {
            int finalScore = results[index].FinalRoundScore;
            if (finalScore > highestScore)
            {
                highestScore = finalScore;
                highestScoreOwnerClientId = ownerClientIds[index];
                highestScoreIsTied = false;
            }
            else if (finalScore == highestScore)
            {
                highestScoreIsTied = true;
            }
        }

        if (highestScoreIsTied)
            ResolveRoundDrawServer();
        else
            ResolveRoundWinnerServer(highestScoreOwnerClientId);

        return _roundResultResolved;
    }

    private bool ValidateFinalPostItLiarResultSetForRound(
        PostItRoundManager postItRoundManager,
        PostItLiarPlayerResultData[] results,
        out ulong[] ownerClientIds)
    {
        ownerClientIds = null;
        if (postItRoundManager == null ||
            results == null ||
            results.Length != PostItLiarFixedSet.Capacity ||
            _roundParticipantClientIds.Count !=
            PostItLiarFixedSet.Capacity)
        {
            return false;
        }

        HashSet<ulong> expectedOwners =
            new HashSet<ulong>(_roundParticipantClientIds);
        if (expectedOwners.Count != PostItLiarFixedSet.Capacity)
            return false;

        ownerClientIds = new ulong[PostItLiarFixedSet.Capacity];
        int twoPointDeductionCount = 0;
        for (byte slot = 0;
             slot < PostItLiarFixedSet.Capacity;
             slot++)
        {
            PostItLiarPlayerResultData result = results[slot];
            long expectedFinalScore =
                (long)result.BattleScore + result.DeductionScore;
            if (result.StableSlot != slot ||
                result.BattleScore < 0 ||
                result.DeductionScore < 0 ||
                result.DeductionScore >
                PostItDeductionModule.LiarCorrectAnswerScore ||
                expectedFinalScore > int.MaxValue ||
                result.FinalRoundScore != expectedFinalScore ||
                !postItRoundManager.ServerTryGetPostItLiarClientId(
                    _roundRevision,
                    slot,
                    out ulong ownerClientId) ||
                !expectedOwners.Remove(ownerClientId) ||
                (_frozenZeroScoreParticipantClientIds.Contains(
                     ownerClientId) &&
                 result.BattleScore != 0))
            {
                ownerClientIds = null;
                return false;
            }

            if (result.DeductionScore ==
                PostItDeductionModule.LiarCorrectAnswerScore)
            {
                twoPointDeductionCount++;
                if (twoPointDeductionCount > 1)
                {
                    ownerClientIds = null;
                    return false;
                }
            }

            ownerClientIds[slot] = ownerClientId;
        }

        return expectedOwners.Count == 0;
    }

    private bool TryResolveRoundResultFromScoresServer(
        PostItGuessPlayerScoreData[] scores)
    {
        if (scores == null || scores.Length == 0)
            return false;

        HashSet<ulong> includedOwners = new HashSet<ulong>();
        int highestScore = int.MinValue;
        ulong highestScoreOwnerClientId = invalidWinnerClientId;
        bool hasHighestScore = false;
        bool highestScoreIsTied = false;

        for (int scoreIndex = 0; scoreIndex < scores.Length; scoreIndex++)
        {
            PostItGuessPlayerScoreData score = scores[scoreIndex];
            if (!score.IsValid ||
                score.RoundRevision != _roundRevision ||
                score.GuessRevision != _guessRevision ||
                !includedOwners.Add(score.OwnerClientId))
            {
                return false;
            }

            if (!hasHighestScore || score.FinalRoundScore > highestScore)
            {
                highestScore = score.FinalRoundScore;
                highestScoreOwnerClientId = score.OwnerClientId;
                hasHighestScore = true;
                highestScoreIsTied = false;
            }
            else if (score.FinalRoundScore == highestScore)
            {
                highestScoreIsTied = true;
            }
        }

        if (highestScoreIsTied)
        {
            ResolveRoundDrawServer();
        }
        else
        {
            ResolveRoundWinnerServer(highestScoreOwnerClientId);
        }

        return _roundResultResolved;
    }

    private bool ContainsFinalScoreForOwner(
        PostItGuessPlayerScoreData[] scores,
        ulong ownerClientId)
    {
        if (scores == null || ownerClientId == invalidWinnerClientId)
            return false;

        for (int scoreIndex = 0; scoreIndex < scores.Length; scoreIndex++)
        {
            PostItGuessPlayerScoreData score = scores[scoreIndex];
            if (score.IsValid &&
                score.RoundRevision == _roundRevision &&
                score.GuessRevision == _guessRevision &&
                score.OwnerClientId == ownerClientId)
            {
                return true;
            }
        }

        return false;
    }

    private bool ValidateFinalScoreSetForRound(
        PostItGuessPlayerScoreData[] scores)
    {
        if (scores == null ||
            _roundParticipantClientIds.Count == 0 ||
            scores.Length != _roundParticipantClientIds.Count)
        {
            return false;
        }

        HashSet<ulong> expectedOwners =
            new HashSet<ulong>(_roundParticipantClientIds);
        if (expectedOwners.Count != _roundParticipantClientIds.Count)
            return false;

        foreach (ulong ownerClientId in _frozenZeroScoreParticipantClientIds)
        {
            if (!expectedOwners.Contains(ownerClientId))
                return false;
        }

        for (int scoreIndex = 0; scoreIndex < scores.Length; scoreIndex++)
        {
            PostItGuessPlayerScoreData score = scores[scoreIndex];
            long expectedFinalRoundScore =
                (long)score.HeldPostItCount + score.CorrectCount;
            if (!score.IsValid ||
                score.RoundRevision != _roundRevision ||
                score.GuessRevision != _guessRevision ||
                !expectedOwners.Remove(score.OwnerClientId) ||
                score.GuessBonusScore != score.CorrectCount ||
                score.FinalRoundScore != expectedFinalRoundScore ||
                (_frozenZeroScoreParticipantClientIds.Contains(
                     score.OwnerClientId) &&
                 !HasZeroScoreValues(score)))
            {
                return false;
            }
        }

        return expectedOwners.Count == 0;
    }

    private static bool HasZeroScoreValues(
        PostItGuessPlayerScoreData score)
    {
        return score.HeldPostItCount == 0 &&
               score.EligibleCount == 0 &&
               score.SubmittedCount == 0 &&
               score.CorrectCount == 0 &&
               score.GuessBonusScore == 0 &&
               score.FinalRoundScore == 0;
    }

    private bool TryBuildRoundParticipantScoreInputsServer(
        out List<PlayerPostItInventory> inventories,
        out List<ulong> zeroScoreOwnerClientIds)
    {
        inventories = new List<PlayerPostItInventory>();
        zeroScoreOwnerClientIds = new List<ulong>();
        if (!IsServer || NetworkManager == null || !NetworkManager.IsListening)
            return false;

        for (int participantIndex = 0;
             participantIndex < _roundParticipantClientIds.Count;
             participantIndex++)
        {
            ulong clientId = _roundParticipantClientIds[participantIndex];
            if (!NetworkManager.ConnectedClients.TryGetValue(clientId, out var client))
            {
                zeroScoreOwnerClientIds.Add(clientId);
                continue;
            }

            if (client == null)
                return false;

            NetworkObject playerObject = client.PlayerObject;
            if (playerObject == null || !playerObject.IsSpawned)
            {
                zeroScoreOwnerClientIds.Add(clientId);
                continue;
            }

            if (!TryResolveSpawnedPlayerStatus(
                    playerObject,
                    clientId,
                    out PlayerStatusModule status) ||
                !TryResolveConnectedPlayerInventory(
                    NetworkManager,
                    clientId,
                    out PlayerPostItInventory inventory))
            {
                return false;
            }

            if (status.IsEliminated)
            {
                if (inventory.Count != 0)
                    return false;

                zeroScoreOwnerClientIds.Add(clientId);
                continue;
            }

            inventories.Add(inventory);
        }

        return _roundParticipantClientIds.Count > 0 &&
               inventories.Count + zeroScoreOwnerClientIds.Count ==
               _roundParticipantClientIds.Count;
    }

    private bool TryBuildConnectedInventoriesServer(
        out List<PlayerPostItInventory> inventories)
    {
        inventories = new List<PlayerPostItInventory>();
        if (!IsServer || NetworkManager == null || !NetworkManager.IsListening)
            return false;

        List<ulong> clientIds = new List<ulong>(NetworkManager.ConnectedClientsIds);
        clientIds.Sort();
        for (int clientIndex = 0; clientIndex < clientIds.Count; clientIndex++)
        {
            ulong clientId = clientIds[clientIndex];
            if (!NetworkManager.ConnectedClients.TryGetValue(clientId, out var client) ||
                client == null)
            {
                return false;
            }

            NetworkObject playerObject = client.PlayerObject;
            if (playerObject == null || !playerObject.IsSpawned)
                continue;

            if (!TryResolveConnectedPlayerInventory(
                    NetworkManager,
                    clientId,
                    out PlayerPostItInventory inventory))
            {
                return false;
            }

            inventories.Add(inventory);
        }

        return true;
    }

    private bool TryBuildCountdownAssignmentInventoriesServer(
        out List<PlayerPostItInventory> inventories)
    {
        inventories = new List<PlayerPostItInventory>();
        if (!IsServer || NetworkManager == null || !NetworkManager.IsListening)
            return false;

        List<ulong> clientIds = new List<ulong>(NetworkManager.ConnectedClientsIds);
        clientIds.Sort();
        if (clientIds.Count == 0)
            return false;

        for (int clientIndex = 0; clientIndex < clientIds.Count; clientIndex++)
        {
            if (!TryResolveConnectedPlayerInventory(
                    NetworkManager,
                    clientIds[clientIndex],
                    out PlayerPostItInventory inventory))
            {
                return false;
            }

            inventories.Add(inventory);
        }

        return inventories.Count == clientIds.Count;
    }

    private bool TryFlushZeroPostItEliminationsServer()
    {
        PostItRoundManager postItRoundManager = ResolvePostItRoundManager();
        return IsValidServerPostItRoundManager(postItRoundManager) &&
               postItRoundManager.ServerFlushZeroPostItEliminations();
    }

    private static bool TryResolveSpawnedPlayerStatus(
        NetworkObject playerObject,
        ulong clientId,
        out PlayerStatusModule status)
    {
        status = null;
        if (playerObject == null ||
            !playerObject.IsSpawned ||
            playerObject.OwnerClientId != clientId)
        {
            return false;
        }

        status = playerObject.GetComponentInChildren<PlayerStatusModule>(true);
        return status != null &&
               status.IsSpawned &&
               status.IsServer &&
               status.GetComponentInParent<NetworkObject>() == playerObject;
    }

    private static bool TryResolveConnectedPlayerInventory(
        NetworkManager networkManager,
        ulong clientId,
        out PlayerPostItInventory inventory)
    {
        inventory = null;
        if (networkManager == null ||
            !networkManager.ConnectedClients.TryGetValue(clientId, out var client) ||
            client == null)
        {
            return false;
        }

        NetworkObject playerObject = client.PlayerObject;
        if (playerObject == null ||
            !playerObject.IsSpawned ||
            playerObject.OwnerClientId != clientId)
            return false;

        inventory = playerObject.GetComponentInChildren<PlayerPostItInventory>(true);
        if (inventory == null || !inventory.IsSpawned || !inventory.IsServer)
            return false;

        NetworkObject inventoryNetworkObject =
            inventory.GetComponentInParent<NetworkObject>();
        return inventoryNetworkObject == playerObject &&
               inventory.OwnerClientId == clientId;
    }

    private bool TryClearGuessStateBeforeLobbyServer()
    {
        PostItRoundManager postItRoundManager = ResolvePostItRoundManager();
        return IsValidServerPostItRoundManager(postItRoundManager) &&
               TryBuildConnectedInventoriesServer(
                   out List<PlayerPostItInventory> inventories) &&
               postItRoundManager.ServerClearGuessState(inventories);
    }

    private bool TryClearPostItLiarStateBeforeLobbyServer()
    {
        PostItRoundManager postItRoundManager = ResolvePostItRoundManager();
        return IsValidServerPostItRoundManager(postItRoundManager) &&
               postItRoundManager.ServerClearPostItLiarRoundState();
    }

    private bool TryResetPostItEliminationsBeforeLobbyServer()
    {
        if (!IsServer || NetworkManager == null || !NetworkManager.IsListening)
            return false;

        for (int participantIndex = 0;
             participantIndex < _roundParticipantClientIds.Count;
             participantIndex++)
        {
            ulong clientId = _roundParticipantClientIds[participantIndex];
            if (!NetworkManager.ConnectedClients.TryGetValue(clientId, out var client))
                continue;

            if (client == null)
                return false;

            NetworkObject playerObject = client.PlayerObject;
            if (playerObject == null || !playerObject.IsSpawned)
                continue;

            if (!TryResolveSpawnedPlayerStatus(
                    playerObject,
                    clientId,
                    out PlayerStatusModule status))
            {
                return false;
            }

            try
            {
                if (!status.ServerResetPostItRoundElimination() ||
                    status.IsEliminated)
                {
                    return false;
                }
            }
            catch (System.Exception exception)
            {
                Debug.LogException(exception, status);
                return false;
            }
        }

        return true;
    }

    private PostItRoundManager ResolvePostItRoundManager()
    {
        if (_postItRoundManager == null)
            _postItRoundManager = FindFirstObjectByType<PostItRoundManager>();

        return _postItRoundManager;
    }

    private static bool IsValidServerPostItRoundManager(
        PostItRoundManager postItRoundManager)
    {
        return postItRoundManager != null &&
               postItRoundManager.IsSpawned &&
               postItRoundManager.IsServer;
    }

    private float GetGuessDeadlineRemainingSeconds(
        double absoluteDeadlineServerTime)
    {
        if (!TryGetAuthoritativeServerTime(out double serverTime) ||
            double.IsNaN(absoluteDeadlineServerTime) ||
            double.IsInfinity(absoluteDeadlineServerTime))
        {
            return 0f;
        }

        double remainingSeconds = absoluteDeadlineServerTime - serverTime;
        return remainingSeconds > 0d ? (float)remainingSeconds : 0f;
    }

    private bool TryGetAuthoritativeServerTime(out double serverTime)
    {
        serverTime = 0d;
        if (!IsServer || NetworkManager == null || !NetworkManager.IsListening)
            return false;

        serverTime = NetworkManager.ServerTime.Time;
        return !double.IsNaN(serverTime) &&
               !double.IsInfinity(serverTime) &&
               serverTime >= 0d;
    }

    private void SubscribeNetworkCallbacksServer()
    {
        if (!IsServer ||
            _subscribedNetworkManager != null ||
            NetworkManager == null)
        {
            return;
        }

        _subscribedNetworkManager = NetworkManager;
        _subscribedNetworkManager.OnClientDisconnectCallback +=
            HandleClientDisconnectedServer;
    }

    private void UnsubscribeNetworkCallbacksServer()
    {
        if (_subscribedNetworkManager == null)
            return;

        _subscribedNetworkManager.OnClientDisconnectCallback -=
            HandleClientDisconnectedServer;
        _subscribedNetworkManager = null;
    }

    private void HandleClientDisconnectedServer(ulong clientId)
    {
        if (!IsServer || _roundRevision < 0)
            return;

        PostItRoundManager postItRoundManager = ResolvePostItRoundManager();
        if (!IsValidServerPostItRoundManager(postItRoundManager))
            return;

        if (postItRoundManager.ServerIsPostItLiarRoundActive(
                _roundRevision))
        {
            if (!postItRoundManager.ServerHandlePostItLiarDisconnect(
                    clientId,
                    _roundRevision,
                    out bool shouldAbortToLobby,
                    out bool deductionStateChanged))
            {
                return;
            }

            if (shouldAbortToLobby &&
                GetState() == GameState.Countdown)
            {
                AbortPostItLiarCountdownServer(
                    $"Post-it liar participant {clientId} disconnected before Playing.");
            }
            else if (deductionStateChanged &&
                     GetState() == GameState.Guessing &&
                     postItRoundManager.LiarPublicState.Phase ==
                     PostItLiarPhase.Complete)
            {
                TryPublishFinalPostItLiarResultServer(
                    postItRoundManager);
            }

            return;
        }

        if (_guessRevision < 0 ||
            postItRoundManager.ActiveGuessRoundRevision != _roundRevision ||
            postItRoundManager.ActiveGuessRevision != _guessRevision ||
            !postItRoundManager.ServerHandleGuessDisconnect(
                clientId,
                _roundRevision,
                _guessRevision,
                out bool allSubmissionsResolved))
        {
            return;
        }

        if (GetState() == GameState.Guessing && allSubmissionsResolved)
        {
            TryFinalizeGuessFlowServer(
                _activePlayingEndReason != PlayingEndReason.Timer);
        }
    }

    private bool CanUseLastSurvivorWinCheck()
    {
        if (!IsServer) return false;
        if (!enableLastSurvivorWinCheck) return false;
        if (_roundResultResolved) return false;
        if (GetState() != GameState.Playing) return false;

        return _roundParticipantClientIds.Count >= Mathf.Max(1, minimumParticipantsForLastSurvivorWin);
    }

    private void SubscribeGameStateCursorEvents()
    {
        if (_isStateValueChangeSubscribed)
            return;

        StateValue.OnValueChanged += HandleStateValueChangedForCursor;
        _isStateValueChangeSubscribed = true;
    }

    private void UnsubscribeGameStateCursorEvents()
    {
        if (!_isStateValueChangeSubscribed)
            return;

        StateValue.OnValueChanged -= HandleStateValueChangedForCursor;
        _isStateValueChangeSubscribed = false;
    }

    private void HandleStateValueChangedForCursor(int previousStateValue, int newStateValue)
    {
        GameState newState = (GameState)newStateValue;
        if (newState == GameState.Playing)
        {
            HandleEnteredPlayingForCursor("state-changed");
            return;
        }

        ApplyCursorStateForGameState(newState, "state-changed");
    }

    private void OnApplicationFocus(bool hasFocus)
    {
        if (!hasFocus)
        {
            StopPlayingCursorLockRetry("focus-lost");
            return;
        }

        if (!reapplyCursorLockOnFocus)
            return;

        if (GetState() == GameState.Playing)
            ReapplyPlayingCursorAfterFocusReturn("focus");
    }

    private void OnApplicationPause(bool pauseStatus)
    {
        if (pauseStatus)
        {
            StopPlayingCursorLockRetry("pause");
            return;
        }

        if (!reapplyCursorLockOnFocus)
            return;

        if (GetState() == GameState.Playing)
            ReapplyPlayingCursorAfterFocusReturn("pause-resume");
    }

    private void RefreshCursorLockIfNeeded()
    {
        if (!autoLockCursorOnPlaying)
            return;
        if (GetState() != GameState.Playing)
            return;
        if (ShouldSkipCursorApi())
            return;
        if (!Application.isFocused)
            return;
        if (Time.unscaledTime < _nextCursorLockRefreshAt)
            return;

        _nextCursorLockRefreshAt = Time.unscaledTime + GetCursorLockRefreshInterval();

        if (Cursor.lockState != CursorLockMode.Locked || Cursor.visible)
            ApplyCursorStateForGameState(GameState.Playing, "refresh");
    }

    private void ApplyCursorStateForCurrentGameState(string reason)
    {
        ApplyCursorStateForGameState(GetState(), reason);
    }

    private void ApplyCursorStateForGameState(GameState state, string reason)
    {
        if (ShouldSkipCursorApi())
            return;

        if (ShouldLockCursorForState(state))
        {
            if (!Application.isFocused)
                return;

            SetCursorLocked(true, reason);
        }
        else if (ShouldUnlockCursorForState(state))
        {
            StopPlayingCursorLockRetry("state-changed");
            SetCursorLocked(false, reason);
        }
        else if (state != GameState.Playing)
        {
            StopPlayingCursorLockRetry("state-changed");
        }
    }

    private void HandleEnteredPlayingForCursor(string reason)
    {
        ClearSelectedUiOnPlayingIfNeeded();
        ApplyCursorStateForGameState(GameState.Playing, reason);
        StartPlayingCursorLockRetry(reason);
    }

    private void ReapplyPlayingCursorAfterFocusReturn(string reason)
    {
        ApplyCursorStateForGameState(GameState.Playing, reason);
        StartPlayingCursorLockRetry(reason);
    }

    private void ClearSelectedUiOnPlayingIfNeeded()
    {
        if (!clearSelectedUiOnPlaying)
            return;

        EventSystem eventSystem = EventSystem.current;
        if (eventSystem == null || eventSystem.currentSelectedGameObject == null)
            return;

        eventSystem.SetSelectedGameObject(null);
        CursorLog("[GameState] Cleared selected UI on Playing");
    }

    private void StartPlayingCursorLockRetry(string reason)
    {
        if (!enablePlayingCursorLockRetry)
            return;
        if (!autoLockCursorOnPlaying)
            return;
        if (GetState() != GameState.Playing)
            return;
        if (ShouldSkipCursorApi())
            return;
        if (!Application.isFocused)
            return;

        if (_playingCursorLockRetryRoutine != null &&
            _lastPlayingCursorLockRetryStartFrame == Time.frameCount)
        {
            return;
        }

        StopPlayingCursorLockRetry("restart");
        _lastPlayingCursorLockRetryStartFrame = Time.frameCount;
        _playingCursorLockRetryRoutine = StartCoroutine(PlayingCursorLockRetryRoutine());
        CursorLog($"[GameState] Cursor lock retry start source={reason}");
    }

    private void StopPlayingCursorLockRetry(string reason)
    {
        if (_playingCursorLockRetryRoutine == null)
            return;

        StopCoroutine(_playingCursorLockRetryRoutine);
        _playingCursorLockRetryRoutine = null;
        CursorLog($"[GameState] Cursor lock retry stop reason={reason}");
    }

    private IEnumerator PlayingCursorLockRetryRoutine()
    {
        yield return new WaitForEndOfFrame();
        if (!CanContinuePlayingCursorLockRetry(out string stopReason))
        {
            CompletePlayingCursorLockRetry(stopReason);
            yield break;
        }

        ApplyCursorStateForGameState(GameState.Playing, "retry-end-frame");

        float retryEndAt = Time.unscaledTime + GetPlayingCursorLockRetryDuration();
        float retryInterval = GetPlayingCursorLockRetryInterval();
        while (Time.unscaledTime < retryEndAt)
        {
            float remaining = retryEndAt - Time.unscaledTime;
            yield return new WaitForSecondsRealtime(Mathf.Min(retryInterval, remaining));

            if (!CanContinuePlayingCursorLockRetry(out stopReason))
            {
                CompletePlayingCursorLockRetry(stopReason);
                yield break;
            }

            ApplyCursorStateForGameState(GameState.Playing, "retry");
        }

        CompletePlayingCursorLockRetry("completed");
    }

    private bool CanContinuePlayingCursorLockRetry(out string stopReason)
    {
        if (!enablePlayingCursorLockRetry)
        {
            stopReason = "disabled";
            return false;
        }

        if (GetState() != GameState.Playing)
        {
            stopReason = "state-changed";
            return false;
        }

        if (ShouldSkipCursorApi())
        {
            stopReason = "cursor-api-skipped";
            return false;
        }

        if (!Application.isFocused)
        {
            stopReason = "focus-lost";
            return false;
        }

        stopReason = null;
        return true;
    }

    private void CompletePlayingCursorLockRetry(string reason)
    {
        _playingCursorLockRetryRoutine = null;
        CursorLog($"[GameState] Cursor lock retry stop reason={reason}");
    }

    private bool ShouldLockCursorForState(GameState state)
    {
        return autoLockCursorOnPlaying && state == GameState.Playing;
    }

    private bool ShouldUnlockCursorForState(GameState state)
    {
        return unlockCursorOutsidePlaying && state != GameState.Playing;
    }

    private void SetCursorLocked(bool locked, string reason)
    {
        if (ShouldSkipCursorApi())
            return;

        CursorLockMode targetLockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
        bool alreadyApplied = Cursor.lockState == targetLockState && Cursor.visible == !locked;

        Cursor.lockState = targetLockState;
        Cursor.visible = !locked;

        if (locked)
            _nextCursorLockRefreshAt = Time.unscaledTime + GetCursorLockRefreshInterval();

        if (!alreadyApplied)
            CursorLog($"[GameState] Cursor lock apply source={reason} lock={targetLockState} visible={Cursor.visible} focused={Application.isFocused}");
    }

    private float GetCursorLockRefreshInterval()
    {
        return Mathf.Max(0.01f, cursorLockRefreshInterval);
    }

    private float GetPlayingCursorLockRetryDuration()
    {
        return Mathf.Max(0f, playingCursorLockRetryDuration);
    }

    private float GetPlayingCursorLockRetryInterval()
    {
        return Mathf.Max(0.01f, playingCursorLockRetryInterval);
    }

    private bool ShouldSkipCursorApi()
    {
        if (Application.isBatchMode)
            return true;

        NetworkManager networkManager = NetworkManager.Singleton;
        return networkManager != null &&
            networkManager.IsListening &&
            networkManager.IsServer &&
            !networkManager.IsClient;
    }

    private void Log(string message)
    {
        if (!enableDebugLogs)
            return;

        Debug.Log(message, this);
    }

    private void CursorLog(string message)
    {
        if (!cursorDebugLogs)
            return;

        Debug.Log(message, this);
    }
}
