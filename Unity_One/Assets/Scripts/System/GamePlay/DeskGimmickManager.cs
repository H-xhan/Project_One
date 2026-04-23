using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public class DeskGimmickManager : NetworkBehaviour
{
    private const string LogPrefix = "[DESK_GIMMICK]";

    private enum PresentationPhase
    {
        Telegraph = 0,
        Response = 1,
        Scan = 2,
        Resolve = 3,
        End = 4
    }

    [Header("Modules")]
    [Tooltip("책상 맵의 개발자 난입 기믹 모듈 참조입니다.")]
    [SerializeField] private DeveloperIntrusionGimmick developerIntrusion;

    [Tooltip("현재 라운드 상태를 확인할 GameStateManager 참조입니다. 비워두면 자동 탐색합니다.")]
    [SerializeField] private GameStateManager gameStateManager;

    [Header("Auto Trigger")]
    [Tooltip("Playing 진입 시 개발자 난입을 자동으로 1회 예약할지 여부입니다.")]
    [SerializeField] private bool autoTriggerOnPlaying = true;

    [Tooltip("Playing 진입 후 자동 발동을 시도하기 전 대기 시간(초)입니다.")]
    [SerializeField] private float autoTriggerDelay = 5.0f;

    [Tooltip("자동 발동을 시도하기 위한 최소 활성 플레이어 수입니다.")]
    [SerializeField] private int minAutoTriggerPlayers = 2;

    [Header("Debug")]
    [Tooltip("책상 기믹 매니저의 디버그 로그를 출력할지 여부입니다.")]
    [SerializeField] private bool enableDebugLogs = true;

    private bool isDeveloperIntrusionRunning;
    private Coroutine _developerIntrusionWatchRoutine;
    private Coroutine _autoTriggerRoutine;
    private DeveloperIntrusionGimmick.Phase _lastObservedPhase = DeveloperIntrusionGimmick.Phase.Idle;
    private bool _sentEndPresentation;
    private GameStateManager.GameState _lastObservedGameState = GameStateManager.GameState.Lobby;
    private bool _hasObservedGameState;
    private bool _hasTriggeredThisRound;

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        ResolveRefs();
        TryInitializeGameStateObservation();
    }

    public override void OnNetworkDespawn()
    {
        CancelAutoTrigger("not spawned");
        ResetRoundTriggerState();

        if (_developerIntrusionWatchRoutine != null)
        {
            StopCoroutine(_developerIntrusionWatchRoutine);
            _developerIntrusionWatchRoutine = null;
        }

        isDeveloperIntrusionRunning = false;
        _lastObservedPhase = DeveloperIntrusionGimmick.Phase.Idle;
        _sentEndPresentation = false;
        _hasObservedGameState = false;

        base.OnNetworkDespawn();
    }

    private void Update()
    {
        if (!IsServer || !IsSpawned)
            return;

        ResolveRefs();
        ObserveGameState();
    }

    [ContextMenu("Debug Start Developer Intrusion")]
    public void DebugStartDeveloperIntrusion()
    {
        if (!Application.isPlaying)
        {
            LogWarning($"{LogPrefix} Debug start ignored. Application is not playing.");
            return;
        }

        if (!IsSpawned)
        {
            LogWarning($"{LogPrefix} Debug start ignored. NetworkObject is not spawned.");
            return;
        }

        if (IsServer)
        {
            StartDeveloperIntrusionServer();
            return;
        }

        DebugStartDeveloperIntrusionServerRpc();
    }

    public void StartDeveloperIntrusionServer()
    {
        TryStartDeveloperIntrusionServer();
    }

    [ServerRpc(RequireOwnership = false)]
    private void DebugStartDeveloperIntrusionServerRpc()
    {
        StartDeveloperIntrusionServer();
    }

    private bool TryStartDeveloperIntrusionServer()
    {
        if (!IsServer)
            return false;

        if (!IsSpawned)
        {
            LogWarning($"{LogPrefix} Developer Intrusion start skipped. NetworkObject is not spawned.");
            return false;
        }

        ResolveRefs();

        if (developerIntrusion == null)
        {
            LogWarning($"{LogPrefix} DeveloperIntrusionGimmick reference is missing.");
            return false;
        }

        if (isDeveloperIntrusionRunning || developerIntrusion.IsRunning)
        {
            Log($"{LogPrefix} Developer Intrusion start ignored. Already running.");
            return false;
        }

        List<PlayerStatusModule> players = CollectActivePlayers();
        if (players.Count == 0)
        {
            LogWarning($"{LogPrefix} Developer Intrusion start skipped. No valid players found.");
            return false;
        }

        isDeveloperIntrusionRunning = true;
        _lastObservedPhase = developerIntrusion.CurrentPhase;
        _sentEndPresentation = false;

        developerIntrusion.StartGimmick(players, false);

        if (_developerIntrusionWatchRoutine != null)
            StopCoroutine(_developerIntrusionWatchRoutine);

        _developerIntrusionWatchRoutine = StartCoroutine(WatchDeveloperIntrusionRoutine());

        if (IsCurrentlyPlaying())
        {
            _hasTriggeredThisRound = true;
            CancelAutoTrigger("already triggered this round");
        }

        Log($"{LogPrefix} Developer Intrusion started. players:{players.Count}");
        return true;
    }

    private IEnumerator WatchDeveloperIntrusionRoutine()
    {
        while (developerIntrusion != null && developerIntrusion.IsRunning)
        {
            DeveloperIntrusionGimmick.Phase currentPhase = developerIntrusion.CurrentPhase;
            if (currentPhase != _lastObservedPhase)
            {
                _lastObservedPhase = currentPhase;
                SendPresentationForPhase(currentPhase);
            }

            yield return null;
        }

        if (!_sentEndPresentation)
        {
            PlayDeveloperIntrusionPresentationClientRpc((int)PresentationPhase.End);
            _sentEndPresentation = true;
        }

        isDeveloperIntrusionRunning = false;
        _developerIntrusionWatchRoutine = null;
        _lastObservedPhase = DeveloperIntrusionGimmick.Phase.Idle;
        Log($"{LogPrefix} Developer Intrusion ended.");
    }

    private void SendPresentationForPhase(DeveloperIntrusionGimmick.Phase phase)
    {
        switch (phase)
        {
            case DeveloperIntrusionGimmick.Phase.Telegraph:
                PlayDeveloperIntrusionPresentationClientRpc((int)PresentationPhase.Telegraph);
                break;
            case DeveloperIntrusionGimmick.Phase.Response:
                PlayDeveloperIntrusionPresentationClientRpc((int)PresentationPhase.Response);
                break;
            case DeveloperIntrusionGimmick.Phase.Scan:
                PlayDeveloperIntrusionPresentationClientRpc((int)PresentationPhase.Scan);
                break;
            case DeveloperIntrusionGimmick.Phase.Resolve:
                PlayDeveloperIntrusionPresentationClientRpc((int)PresentationPhase.Resolve);
                break;
        }
    }

    [ClientRpc]
    private void PlayDeveloperIntrusionPresentationClientRpc(int phaseValue)
    {
        ResolveRefs();

        if (developerIntrusion == null)
        {
            LogWarning($"{LogPrefix} Cannot play presentation. DeveloperIntrusionGimmick reference is missing.");
            return;
        }

        PresentationPhase phase = (PresentationPhase)phaseValue;
        switch (phase)
        {
            case PresentationPhase.Telegraph:
                developerIntrusion.PlayTelegraphPresentation();
                break;
            case PresentationPhase.Response:
                developerIntrusion.PlayResponsePresentation();
                break;
            case PresentationPhase.Scan:
                developerIntrusion.PlayScanPresentation();
                break;
            case PresentationPhase.Resolve:
                developerIntrusion.PlayResolvePresentation();
                break;
            case PresentationPhase.End:
                developerIntrusion.PlayEndPresentation();
                break;
        }
    }

    private List<PlayerStatusModule> CollectActivePlayers()
    {
        List<PlayerStatusModule> players = new List<PlayerStatusModule>();
        PlayerStatusModule[] found = FindPlayerStatusModules();

        if (found == null)
        {
            LogWarning($"{LogPrefix} CollectActivePlayers found null result.");
            return players;
        }

        int nullCount = 0;
        int eliminatedCount = 0;

        for (int i = 0; i < found.Length; i++)
        {
            PlayerStatusModule status = found[i];
            if (status == null)
            {
                nullCount++;
                continue;
            }

            if (status.IsEliminated)
            {
                eliminatedCount++;
                Log($"{LogPrefix} Collect skip eliminated player:{DescribePlayer(status)}");
                continue;
            }

            players.Add(status);
            Log($"{LogPrefix} Collect valid player index:{players.Count - 1} {DescribePlayer(status)}");
        }

        Log($"{LogPrefix} CollectActivePlayers summary found:{found.Length} valid:{players.Count} null:{nullCount} eliminated:{eliminatedCount}");

        if (found.Length >= 2 && players.Count < 2)
            LogWarning($"{LogPrefix} Expected 2+ valid players but collected {players.Count}. Check PlayerStatusModule spawn/elimination state.");

        return players;
    }

    private PlayerStatusModule[] FindPlayerStatusModules()
    {
#if UNITY_6000_0_OR_NEWER
        return FindObjectsByType<PlayerStatusModule>(FindObjectsSortMode.None);
#else
        return FindObjectsOfType<PlayerStatusModule>();
#endif
    }

    private void ResolveRefs()
    {
        if (developerIntrusion == null)
            developerIntrusion = GetComponentInChildren<DeveloperIntrusionGimmick>(true);

        if (gameStateManager == null)
            gameStateManager = FindFirstObjectByType<GameStateManager>();

        if (developerIntrusion == null)
        {
            developerIntrusion = FindFirstDeveloperIntrusion();
            if (developerIntrusion != null)
                LogWarning($"{LogPrefix} DeveloperIntrusionGimmick was found by scene fallback. Inspector reference is recommended.");
        }
    }

    private void ObserveGameState()
    {
        if (!TryInitializeGameStateObservation())
            return;

        GameStateManager.GameState currentState = gameStateManager.GetState();
        if (currentState == _lastObservedGameState)
            return;

        HandleGameStateExited(_lastObservedGameState, currentState);
        HandleGameStateEntered(currentState);
        _lastObservedGameState = currentState;
    }

    private bool TryInitializeGameStateObservation()
    {
        if (_hasObservedGameState)
            return true;

        if (gameStateManager == null)
            return false;

        _lastObservedGameState = gameStateManager.GetState();
        _hasObservedGameState = true;

        if (_lastObservedGameState == GameStateManager.GameState.Playing)
        {
            ResetRoundTriggerState();
            ScheduleAutoTrigger();
        }

        return true;
    }

    private void HandleGameStateEntered(GameStateManager.GameState state)
    {
        if (state != GameStateManager.GameState.Playing)
            return;

        ResetRoundTriggerState();
        ScheduleAutoTrigger();
    }

    private void HandleGameStateExited(GameStateManager.GameState previousState, GameStateManager.GameState currentState)
    {
        if (previousState != GameStateManager.GameState.Playing)
            return;

        CancelAutoTrigger("left playing before delay elapsed");
        ResetRoundTriggerState();
    }

    private void ScheduleAutoTrigger()
    {
        if (!autoTriggerOnPlaying)
        {
            Log($"{LogPrefix} Auto trigger skipped. reason=disabled");
            return;
        }

        if (!IsServer)
        {
            Log($"{LogPrefix} Auto trigger skipped. reason=not server");
            return;
        }

        if (!IsSpawned)
        {
            Log($"{LogPrefix} Auto trigger skipped. reason=not spawned");
            return;
        }

        if (!IsCurrentlyPlaying())
        {
            Log($"{LogPrefix} Auto trigger skipped. reason=not playing");
            return;
        }

        if (_hasTriggeredThisRound)
        {
            Log($"{LogPrefix} Auto trigger skipped. reason=already triggered this round");
            return;
        }

        CancelAutoTrigger(null);
        _autoTriggerRoutine = StartCoroutine(AutoTriggerAfterDelayRoutine());
        Log($"{LogPrefix} Auto trigger scheduled. delay={Mathf.Max(0f, autoTriggerDelay):0.###}");
    }

    private IEnumerator AutoTriggerAfterDelayRoutine()
    {
        float delay = Mathf.Max(0f, autoTriggerDelay);
        if (delay > 0f)
            yield return new WaitForSeconds(delay);

        _autoTriggerRoutine = null;

        if (!IsServer)
        {
            Log($"{LogPrefix} Auto trigger skipped. reason=not server");
            yield break;
        }

        if (!IsSpawned)
        {
            Log($"{LogPrefix} Auto trigger skipped. reason=not spawned");
            yield break;
        }

        ResolveRefs();

        if (!IsCurrentlyPlaying())
        {
            Log($"{LogPrefix} Auto trigger skipped. reason=not playing");
            yield break;
        }

        if (developerIntrusion == null)
        {
            Log($"{LogPrefix} Auto trigger skipped. reason=developerIntrusion missing");
            yield break;
        }

        if (isDeveloperIntrusionRunning || developerIntrusion.IsRunning)
        {
            Log($"{LogPrefix} Auto trigger skipped. reason=already running");
            yield break;
        }

        if (_hasTriggeredThisRound)
        {
            Log($"{LogPrefix} Auto trigger skipped. reason=already triggered this round");
            yield break;
        }

        List<PlayerStatusModule> players = CollectActivePlayers();
        if (players.Count < Mathf.Max(1, minAutoTriggerPlayers))
        {
            Log($"{LogPrefix} Auto trigger skipped. reason=active players below min");
            yield break;
        }

        Log($"{LogPrefix} Auto trigger accepted. playerCount={players.Count}");
        TryStartDeveloperIntrusionServer();
    }

    private void CancelAutoTrigger(string reason)
    {
        if (_autoTriggerRoutine == null)
            return;

        StopCoroutine(_autoTriggerRoutine);
        _autoTriggerRoutine = null;

        if (!string.IsNullOrEmpty(reason))
            Log($"{LogPrefix} Auto trigger canceled. reason={reason}");
    }

    private void ResetRoundTriggerState()
    {
        _hasTriggeredThisRound = false;
        Log($"{LogPrefix} Round trigger state reset.");
    }

    private bool IsCurrentlyPlaying()
    {
        return gameStateManager != null && gameStateManager.GetState() == GameStateManager.GameState.Playing;
    }

    private DeveloperIntrusionGimmick FindFirstDeveloperIntrusion()
    {
#if UNITY_6000_0_OR_NEWER
        DeveloperIntrusionGimmick[] found = FindObjectsByType<DeveloperIntrusionGimmick>(FindObjectsInactive.Include, FindObjectsSortMode.None);
#else
        DeveloperIntrusionGimmick[] found = FindObjectsOfType<DeveloperIntrusionGimmick>(true);
#endif
        if (found == null || found.Length == 0)
            return null;

        return found[0];
    }

    private string DescribePlayer(PlayerStatusModule status)
    {
        if (status == null)
            return "null";

        NetworkObject netObj = status.NetworkObject;
        if (netObj != null)
            return $"name:{status.name} owner:{netObj.OwnerClientId} netId:{netObj.NetworkObjectId} spawned:{netObj.IsSpawned} eliminated:{status.IsEliminated}";

        return $"name:{status.name} owner:n/a netId:n/a spawned:false eliminated:{status.IsEliminated}";
    }

    private void Log(string message)
    {
        if (!enableDebugLogs)
            return;

        Debug.Log(message, this);
    }

    private void LogWarning(string message)
    {
        if (!enableDebugLogs)
            return;

        Debug.LogWarning(message, this);
    }
}
