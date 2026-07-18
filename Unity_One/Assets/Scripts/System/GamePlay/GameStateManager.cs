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

        EnterLobby(true);
    }

    public override void OnNetworkDespawn()
    {
        UnsubscribeGameStateCursorEvents();
        StopPlayingCursorLockRetry("network-despawn");
        if (unlockCursorOutsidePlaying)
            SetCursorLocked(false, "network-despawn");

        base.OnNetworkDespawn();
    }

    public override void OnDestroy()
    {
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
            EnterResultsWithCurrentSurvivorEvaluationServer();
        }
        else if (state == GameState.Results && autoReturnToLobby)
        {
            EnterLobby(false);
        }
    }

    private void EnterLobby(bool isInitialSpawn)
    {
        StateValue.Value = (int)GameState.Lobby;
        ApplyCursorStateForCurrentGameState("enter-lobby");
        StateTimer.Value = 0f;
        ResetRoundResultServer();
        _postItAssignedThisRound = false;
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
        StateValue.Value = (int)GameState.Countdown;
        ApplyCursorStateForCurrentGameState("enter-countdown");
        StateTimer.Value = countdownSeconds;
        _postItAssignedThisRound = false;

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

        PostItRoundManager postItRoundManager = FindFirstObjectByType<PostItRoundManager>();
        if (postItRoundManager == null)
        {
            Log("[GameStateManager] PostItRoundManager not found. Initial post-it assignment skipped.");
            return;
        }

        if (!postItRoundManager.ServerAssignInitialPostItsFromScene())
        {
            Log("[GameStateManager] Initial post-it assignment failed.");
            return;
        }

        _postItAssignedThisRound = true;
        Log("[GameStateManager] Initial post-it assignment completed.");
    }

    private void EnterResults()
    {
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
    }

    private void TickRoundEndCheckServer()
    {
        if (!CanUseLastSurvivorWinCheck())
            return;

        float now = Time.unscaledTime;
        if (now < _nextSurvivorCheckTime)
            return;

        _nextSurvivorCheckTime = now + Mathf.Max(0f, survivorCheckInterval);

        int aliveCount = CountAliveParticipantsServer(out ulong lastAliveClientId);
        if (aliveCount == 1)
        {
            ResolveRoundWinnerServer(lastAliveClientId);
        }
        else if (aliveCount == 0)
        {
            ResolveRoundDrawServer();
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

    private void EnterResultsWithCurrentSurvivorEvaluationServer()
    {
        if (!IsServer) return;
        if (_roundResultResolved) return;

        int aliveCount = CountAliveParticipantsServer(out ulong lastAliveClientId);
        if (aliveCount == 1)
        {
            ResolveRoundWinnerServer(lastAliveClientId);
            return;
        }

        ResolveRoundDrawServer();
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
