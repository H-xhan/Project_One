using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.EventSystems;

public class GameStateManager : NetworkBehaviour
{
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
    private float _nextSurvivorCheckTime;
    private float _nextCursorLockRefreshAt;
    private Coroutine _playingCursorLockRetryRoutine;
    private int _lastPlayingCursorLockRetryStartFrame = -1;
    private bool _roundResultResolved;
    private bool _isStateValueChangeSubscribed;
    private bool _postItAssignedThisRound;
    private bool _roundEndTransitionInProgress;
    private int _roundRevision = -1;
    private int _guessRevision = -1;
    private PlayingEndReason _activePlayingEndReason = PlayingEndReason.Timer;
    private ulong _pendingSurvivorWinnerClientId = ulong.MaxValue;
    private PostItRoundManager _postItRoundManager;
    private NetworkManager _subscribedNetworkManager;

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

        base.OnNetworkDespawn();
    }

    public override void OnDestroy()
    {
        UnsubscribeNetworkCallbacksServer();
        UnsubscribeGameStateCursorEvents();
        StopPlayingCursorLockRetry("destroy");
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
            if (readySystem != null && readySystem.CanStartGameServer() && readySystem.AreAllReady())
            {
                readySystem.ResetAllReadyServer();
                EnterCountdown();
            }

            return;
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
        if (timer > 0f)
        {
            timer -= Time.deltaTime;
            if (timer < 0f) timer = 0f;
            StateTimer.Value = timer;
        }

        if (StateTimer.Value > 0f)
            return;

        if (state == GameState.Countdown)
        {
            EnterPlaying();
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
        if (!isInitialSpawn && !TryClearGuessStateBeforeLobbyServer())
        {
            Log("[GameStateManager] Guess state clear failed. Lobby transition deferred.");
            return;
        }

        StateValue.Value = (int)GameState.Lobby;
        ApplyCursorStateForCurrentGameState("enter-lobby");
        StateTimer.Value = 0f;
        ResetRoundResultServer();
        _postItAssignedThisRound = false;
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

    private void EnterCountdown()
    {
        if (!TryAdvanceRoundRevisionServer())
        {
            Log("[GameStateManager] Round revision could not advance.");
            return;
        }

        StateValue.Value = (int)GameState.Countdown;
        ApplyCursorStateForCurrentGameState("enter-countdown");
        StateTimer.Value = countdownSeconds;
        _postItAssignedThisRound = false;
        _guessRevision = -1;

        Log("[GameStateManager] EnterCountdown");
    }

    private void EnterPlaying()
    {
        ResetRoundResultServer();
        CaptureRoundParticipantsServer();

        StateValue.Value = (int)GameState.Playing;
        HandleEnteredPlayingForCursor("enter-playing");
        StateTimer.Value = playSeconds;
        _nextSurvivorCheckTime = Time.unscaledTime + Mathf.Max(0f, survivorCheckInterval);

        if (teleportPlayersOnEnterPlaying && inGameMatchManager != null && inGameMatchManager.IsSpawned)
        {
            inGameMatchManager.TeleportPlayersToGameServer();
        }

        AssignInitialPostItsForCurrentRoundServer();

        Log("[GameStateManager] EnterPlaying");
    }

    private void AssignInitialPostItsForCurrentRoundServer()
    {
        if (!IsServer) return;
        if (_postItAssignedThisRound) return;

        PostItRoundManager postItRoundManager = ResolvePostItRoundManager();
        if (postItRoundManager == null)
        {
            Log("[GameStateManager] PostItRoundManager not found. Initial post-it assignment skipped.");
            return;
        }

        if (!postItRoundManager.ServerAssignInitialPostItsFromScene(_roundRevision))
        {
            Log("[GameStateManager] Initial post-it assignment failed.");
            return;
        }

        _postItAssignedThisRound = true;
        Log("[GameStateManager] Initial post-it assignment completed.");
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

        _nextSurvivorCheckTime = now + Mathf.Max(0f, survivorCheckInterval);

        int aliveCount = CountAliveParticipantsServer(out ulong lastAliveClientId);
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

    private bool TryGetPlayerStatusForClient(ulong clientId, out PlayerStatusModule status)
    {
        status = null;

        NetworkManager networkManager = NetworkManager.Singleton;
        if (networkManager == null)
            return false;

        if (!networkManager.ConnectedClients.TryGetValue(clientId, out var client) || client == null)
            return false;

        NetworkObject playerObject = client.PlayerObject;
        if (playerObject == null || !playerObject.IsSpawned)
            return false;

        status = playerObject.GetComponentInChildren<PlayerStatusModule>(true);
        return status != null && status.IsSpawned;
    }

    private int CountAliveParticipantsServer(out ulong lastAliveClientId)
    {
        lastAliveClientId = invalidWinnerClientId;
        int aliveCount = 0;

        for (int i = 0; i < _roundParticipantClientIds.Count; i++)
        {
            ulong clientId = _roundParticipantClientIds[i];
            if (!TryGetPlayerStatusForClient(clientId, out PlayerStatusModule status))
                continue;

            if (status.IsEliminated)
                continue;

            aliveCount++;
            lastAliveClientId = clientId;
        }

        return aliveCount;
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

    private bool TryAdvanceRoundRevisionServer()
    {
        if (!IsServer || _roundRevision == int.MaxValue)
            return false;

        _roundRevision++;
        return true;
    }

    private void RequestPlayingEndAfterTimerServer()
    {
        if (!TryFlushZeroPostItEliminationsServer())
            return;

        int aliveCount = CountAliveParticipantsServer(out ulong lastAliveClientId);
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
            if (reason == PlayingEndReason.NoSurvivors)
            {
                ResolveRoundDrawServer();
                return;
            }

            PostItRoundManager postItRoundManager = ResolvePostItRoundManager();
            if (!IsValidServerPostItRoundManager(postItRoundManager) ||
                !TryBuildRoundParticipantInventoriesServer(
                    out List<PlayerPostItInventory> inventories))
            {
                Log("[GameStateManager] Playing end snapshot preparation failed.");
                return;
            }

            int guessRevision = _roundRevision;
            if (!postItRoundManager.ServerBeginGuessing(
                    inventories,
                    _roundRevision,
                    guessRevision,
                    out int totalEligibleCount,
                    out double absoluteDeadlineServerTime))
            {
                Log("[GameStateManager] Guessing begin failed.");
                return;
            }

            _guessRevision = guessRevision;
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
        if (!IsValidServerPostItRoundManager(postItRoundManager) ||
            postItRoundManager.ActiveGuessRoundRevision != _roundRevision ||
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

        if (_activePlayingEndReason == PlayingEndReason.LastSurvivor)
        {
            if (!ContainsFinalScoreForOwner(
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
            ResolveRoundDrawServer();
            return _roundResultResolved;
        }

        return TryResolveRoundResultFromScoresServer(scores);
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

    private bool TryBuildRoundParticipantInventoriesServer(
        out List<PlayerPostItInventory> inventories)
    {
        inventories = new List<PlayerPostItInventory>();
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

            if (!TryResolveConnectedPlayerInventory(
                    NetworkManager,
                    clientId,
                    out PlayerPostItInventory inventory))
            {
                return false;
            }

            inventories.Add(inventory);
        }

        return inventories.Count > 0;
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

    private bool TryFlushZeroPostItEliminationsServer()
    {
        PostItRoundManager postItRoundManager = ResolvePostItRoundManager();
        return IsValidServerPostItRoundManager(postItRoundManager) &&
               postItRoundManager.ServerFlushZeroPostItEliminations();
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
        if (!IsServer || _roundRevision < 0 || _guessRevision < 0)
            return;

        PostItRoundManager postItRoundManager = ResolvePostItRoundManager();
        if (!IsValidServerPostItRoundManager(postItRoundManager) ||
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
