using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public class DeskGimmickManager : NetworkBehaviour
{
    private const string LogPrefix = "[DESK_GIMMICK]";
    private const string SchedulerLogPrefix = "[DeskGimmickManager]";
    private const float DeveloperIntrusionAfterLiquidBlockTime = 10f;
    private const float BombPassAfterDeveloperBlockTime = 15f;

    private enum PresentationPhase
    {
        Telegraph = 0,
        Response = 1,
        Scan = 2,
        Resolve = 3,
        End = 4
    }

    private enum ManagedDeskGimmickType
    {
        KeyboardPop,
        LiquidSweep,
        BombPass,
        DeveloperIntrusion
    }

    [System.Serializable]
    private class DeskGimmickProfile
    {
        [Tooltip("기믹 구분용 이름입니다.")]
        public string name;

        [Tooltip("기믹 타입입니다.")]
        public ManagedDeskGimmickType type;

        [Tooltip("기믹을 실행할 대상 MonoBehaviour입니다.")]
        public MonoBehaviour target;

        [Tooltip("기믹 시작 시 호출할 Unity SendMessage 메서드 이름입니다. 기존 ContextMenu/Start 메서드 이름과 맞춰 설정합니다.")]
        public string startMessageName;

        [Tooltip("이 기믹이 랜덤 선택될 가중치입니다.")]
        public float weight;

        [Tooltip("라운드당 최대 발동 횟수입니다.")]
        public int maxUsesPerRound;

        [Tooltip("라운드 시작 후 이 시간이 지나야 후보가 됩니다.")]
        public float minRoundTime;

        [Tooltip("기믹이 active로 간주되는 예상 시간입니다.")]
        public float estimatedActiveDuration;

        [Tooltip("이 기믹 종료 후 추가로 둘 글로벌 쿨다운입니다.")]
        public float cooldownAfter;

        [Tooltip("이 기믹을 강기믹으로 취급할지 여부입니다. 강기믹은 다른 강기믹과 겹치지 않습니다.")]
        public bool isMajor;

        [Tooltip("이 기믹은 플레이어가 최소 2명 이상일 때만 발동됩니다.")]
        public bool requireAtLeastTwoPlayers;
    }

    private struct ActiveGimmickState
    {
        public ManagedDeskGimmickType type;
        public bool isMajor;
        public float startTime;
        public float endTime;
        public string name;
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

    [Header("Gimmick Scheduler")]
    [SerializeField, Tooltip("라운드 시작 후 기믹 발동을 막는 초기 대기 시간입니다.")]
    private float initialGimmickDelay = 12f;

    [SerializeField, Tooltip("기믹과 기믹 사이의 최소 전역 대기 시간입니다.")]
    private float globalGimmickGap = 10f;

    [SerializeField, Tooltip("기믹 자동 발동을 사용할지 여부입니다.")]
    private bool enableAutoGimmickSchedule = true;

    [SerializeField, Tooltip("기믹 자동 선택을 검사하는 간격입니다.")]
    private float schedulerTickInterval = 1f;

    [SerializeField, Tooltip("현재 4개 책상 기믹 운영 프로필입니다.")]
    private DeskGimmickProfile[] gimmickProfiles = System.Array.Empty<DeskGimmickProfile>();

    [SerializeField, Tooltip("DeveloperIntrusionGimmick이 후보가 되기 위한 최소 라운드 시간입니다.")]
    private float developerIntrusionMinRoundTime = 60f;

    [SerializeField, Tooltip("BombPassGimmick 발동 후 DeveloperIntrusionGimmick이 다시 후보가 되기까지 필요한 시간입니다.")]
    private float developerIntrusionAfterBombBlockTime = 15f;

    [SerializeField, Tooltip("LiquidSweepGimmick 발동 후 BombPassGimmick이 후보가 되기까지 필요한 시간입니다.")]
    private float bombPassAfterLiquidDelay = 4f;

    [SerializeField, Tooltip("중반 구간이 시작되는 라운드 시간입니다. 이 시간 전까지는 초반 active 제한을 사용합니다.")]
    private float midPhaseStartTime = 45f;

    [SerializeField, Tooltip("후반 구간이 시작되는 라운드 시간입니다.")]
    private float latePhaseStartTime = 150f;

    [SerializeField, Tooltip("막판 구간이 시작되는 라운드 시간입니다.")]
    private float finalPhaseStartTime = 270f;

    [SerializeField, Tooltip("초반 구간에서 동시에 active일 수 있는 기믹 수입니다.")]
    private int earlyMaxActiveGimmicks = 1;

    [SerializeField, Tooltip("중반 구간에서 동시에 active일 수 있는 기믹 수입니다.")]
    private int midMaxActiveGimmicks = 2;

    [SerializeField, Tooltip("후반 구간에서 동시에 active일 수 있는 기믹 수입니다.")]
    private int lateMaxActiveGimmicks = 2;

    [SerializeField, Tooltip("막판 구간에서 동시에 active일 수 있는 기믹 수입니다.")]
    private int finalMaxActiveGimmicks = 3;

    [SerializeField, Tooltip("동시에 active일 수 있는 Major 기믹 수입니다.")]
    private int maxMajorActiveGimmicks = 1;

    [SerializeField, Tooltip("Major 기믹끼리의 동시 발동을 강제로 막을지 여부입니다.")]
    private bool blockSimultaneousMajorGimmicks = true;

    [Header("Debug")]
    [Tooltip("책상 기믹 매니저의 디버그 로그를 출력할지 여부입니다.")]
    [SerializeField] private bool enableDebugLogs = false;

    private bool isDeveloperIntrusionRunning;
    private Coroutine _developerIntrusionWatchRoutine;
    private Coroutine _autoTriggerRoutine;
    private DeveloperIntrusionGimmick.Phase _lastObservedPhase = DeveloperIntrusionGimmick.Phase.Idle;
    private bool _sentEndPresentation;
    private GameStateManager.GameState _lastObservedGameState = GameStateManager.GameState.Lobby;
    private bool _hasObservedGameState;
    private bool _hasTriggeredThisRound;
    private bool _isGimmickRoundActive;
    private float _roundStartTime;
    private float _nextAllowedGimmickTime;
    private float _nextAllowedTriggerTime;
    private readonly List<ActiveGimmickState> _activeGimmicks = new List<ActiveGimmickState>();
    private readonly Dictionary<ManagedDeskGimmickType, int> _usesThisRound = new Dictionary<ManagedDeskGimmickType, int>();
    private float _lastKeyboardPopTime = float.NegativeInfinity;
    private float _lastLiquidSweepTime = float.NegativeInfinity;
    private float _lastBombPassTime = float.NegativeInfinity;
    private float _lastDeveloperIntrusionTime = float.NegativeInfinity;
    private Coroutine _schedulerRoutine;
    private readonly List<DeskGimmickProfile> _eligibleProfiles = new List<DeskGimmickProfile>(4);

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        ResolveRefs();
        TryInitializeGameStateObservation();
    }

    public override void OnNetworkDespawn()
    {
        CancelAutoTrigger("not spawned");
        EndGimmickRound();
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

    public void BeginGimmickRound()
    {
        if (!Application.isPlaying)
        {
            LogWarning($"{SchedulerLogPrefix} Schedule begin ignored. Application is not playing.");
            return;
        }

        if (!IsServer)
        {
            Log($"{SchedulerLogPrefix} Schedule begin skipped. reason=not server");
            return;
        }

        if (!IsSpawned)
        {
            LogWarning($"{SchedulerLogPrefix} Schedule begin skipped. reason=not spawned");
            return;
        }

        StopSchedulerRoutine();

        _isGimmickRoundActive = true;
        _roundStartTime = Time.time;
        _nextAllowedTriggerTime = Time.time + Mathf.Max(0f, initialGimmickDelay);
        _nextAllowedGimmickTime = _nextAllowedTriggerTime;
        _activeGimmicks.Clear();
        _usesThisRound.Clear();
        ResetLastGimmickTimes();

        if (enableAutoGimmickSchedule)
            _schedulerRoutine = StartCoroutine(GimmickSchedulerRoutine());

        Log($"{SchedulerLogPrefix} Schedule begin. initialDelay={Mathf.Max(0f, initialGimmickDelay):0.###} nextAllowed={_nextAllowedTriggerTime:0.###}");
    }

    public void EndGimmickRound()
    {
        StopSchedulerRoutine();

        _isGimmickRoundActive = false;
        _activeGimmicks.Clear();
        _roundStartTime = 0f;
        _nextAllowedTriggerTime = 0f;
        _nextAllowedGimmickTime = 0f;
        _usesThisRound.Clear();
        _eligibleProfiles.Clear();

        Log($"{SchedulerLogPrefix} Schedule end.");
    }

    [ContextMenu("Debug Begin Gimmick Schedule")]
    private void DebugBeginGimmickSchedule()
    {
        BeginGimmickRound();
    }

    [ContextMenu("Debug Stop Gimmick Schedule")]
    private void DebugStopGimmickSchedule()
    {
        EndGimmickRound();
    }

    [ContextMenu("Debug Try Trigger Next Gimmick")]
    private void DebugTryTriggerNextGimmick()
    {
        TryTriggerNextGimmick();
    }

    [ContextMenu("Debug Print Eligible Gimmicks")]
    private void DebugPrintEligibleGimmicks()
    {
        PruneExpiredActiveGimmicks();

        float roundTime = Mathf.Max(0f, Time.time - _roundStartTime);
        int activeCount = GetActiveGimmickCount();
        int currentMaxActive = GetMaxActiveGimmicksForRoundTime(roundTime);
        int activeMajorCount = GetActiveMajorGimmickCount();
        int currentMaxMajor = Mathf.Max(0, maxMajorActiveGimmicks);

        Log($"{SchedulerLogPrefix} Eligible check. roundTime={roundTime:0.###} now={Time.time:0.###} active={activeCount}/{currentMaxActive} major={activeMajorCount}/{currentMaxMajor} nextAllowed={_nextAllowedTriggerTime:0.###}");
        Log($"{SchedulerLogPrefix} Active gimmicks: {DescribeActiveGimmicks()}");

        if (gimmickProfiles == null || gimmickProfiles.Length == 0)
        {
            Log($"{SchedulerLogPrefix} No gimmick profiles configured.");
            return;
        }

        for (int i = 0; i < gimmickProfiles.Length; i++)
        {
            DeskGimmickProfile profile = gimmickProfiles[i];
            string reason = GetProfileSkipReason(profile, roundTime);
            bool onCooldown = profile != null && IsProfileOnCooldown(profile);
            bool blockedCombination = profile != null && IsBlockedByActiveCombination(profile);
            if (string.IsNullOrEmpty(reason))
            {
                Log($"{SchedulerLogPrefix} Eligible profile index={i} type={profile.type} weight={profile.weight:0.###} uses={GetUseCount(profile.type)}/{profile.maxUsesPerRound} cooldown={onCooldown} blockedCombination={blockedCombination}");
                continue;
            }

            string profileName = profile != null ? profile.name : "null";
            Log($"{SchedulerLogPrefix} Ineligible profile index={i} name={profileName} reason={reason} cooldown={onCooldown} blockedCombination={blockedCombination}");
        }
    }

    [ContextMenu("Debug Fill Default Gimmick Profiles")]
    private void DebugFillDefaultGimmickProfiles()
    {
        if (gimmickProfiles != null && gimmickProfiles.Length > 0)
        {
            Log($"{SchedulerLogPrefix} Default profiles not filled. Existing profiles count={gimmickProfiles.Length}");
            return;
        }

        gimmickProfiles = new[]
        {
            CreateProfile(
                "KeyboardPop",
                ManagedDeskGimmickType.KeyboardPop,
                FindFirstGimmickTarget<KeyboardPopGimmick>(),
                "DebugStartKeyboardPop",
                35f,
                3,
                12f,
                4f,
                7f,
                false,
                false),
            CreateProfile(
                "LiquidSweep",
                ManagedDeskGimmickType.LiquidSweep,
                FindFirstGimmickTarget<LiquidSweepGimmick>(),
                "DebugStartLiquidSweep",
                30f,
                2,
                20f,
                6f,
                10f,
                true,
                false),
            CreateProfile(
                "BombPass",
                ManagedDeskGimmickType.BombPass,
                FindFirstGimmickTarget<BombPassGimmick>(),
                "DebugStartBombPass",
                25f,
                1,
                45f,
                12f,
                12f,
                true,
                true),
            CreateProfile(
                "DeveloperIntrusion",
                ManagedDeskGimmickType.DeveloperIntrusion,
                this,
                "StartDeveloperIntrusionServer",
                10f,
                1,
                60f,
                12f,
                15f,
                true,
                true)
        };

        Log($"{SchedulerLogPrefix} Default profiles filled. Assign missing targets in Inspector if any target is null.");
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
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
        int hamsterRecoveryPlayerCount = CountValidHamsterRecoveryAdapters();
        if (players.Count == 0 && hamsterRecoveryPlayerCount == 0)
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

        Log($"{LogPrefix} Developer Intrusion started. players:{players.Count} hamsterRecoveryPlayers:{hamsterRecoveryPlayerCount}");
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
        EndGimmickRound();
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

        if (enableAutoGimmickSchedule)
        {
            CancelAutoTrigger(null);
            BeginGimmickRound();
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

    private IEnumerator GimmickSchedulerRoutine()
    {
        while (_isGimmickRoundActive && enableAutoGimmickSchedule)
        {
            TryTriggerNextGimmick();
            yield return new WaitForSeconds(Mathf.Max(0.1f, schedulerTickInterval));
        }

        _schedulerRoutine = null;
    }

    private bool TryTriggerNextGimmick()
    {
        if (!enableAutoGimmickSchedule)
            return false;

        if (!_isGimmickRoundActive)
            return false;

        if (!Application.isPlaying)
            return false;

        if (!IsServer || !IsSpawned)
            return false;

        float now = Time.time;
        if (now < _nextAllowedTriggerTime)
            return false;

        PruneExpiredActiveGimmicks();

        float roundTime = Mathf.Max(0f, now - _roundStartTime);
        int activeCount = GetActiveGimmickCount();
        int currentMaxActive = GetMaxActiveGimmicksForRoundTime(roundTime);
        if (activeCount >= currentMaxActive)
            return false;

        DeskGimmickProfile selected = SelectWeightedEligibleProfile(roundTime);
        if (selected == null)
            return false;

        Log($"{SchedulerLogPrefix} Selected gimmick. type={selected.type} name={selected.name} roundTime={roundTime:0.###}");
        return TriggerGimmick(selected);
    }

    private DeskGimmickProfile SelectWeightedEligibleProfile(float roundTime)
    {
        _eligibleProfiles.Clear();

        if (gimmickProfiles == null || gimmickProfiles.Length == 0)
            return null;

        float totalWeight = 0f;
        for (int i = 0; i < gimmickProfiles.Length; i++)
        {
            DeskGimmickProfile profile = gimmickProfiles[i];
            if (!IsProfileEligible(profile, roundTime))
                continue;

            float clampedWeight = Mathf.Max(0f, profile.weight);
            if (clampedWeight <= 0f)
                continue;

            _eligibleProfiles.Add(profile);
            totalWeight += clampedWeight;
        }

        if (_eligibleProfiles.Count == 0 || totalWeight <= 0f)
            return null;

        float roll = Random.Range(0f, totalWeight);
        float accumulatedWeight = 0f;

        for (int i = 0; i < _eligibleProfiles.Count; i++)
        {
            DeskGimmickProfile profile = _eligibleProfiles[i];
            accumulatedWeight += Mathf.Max(0f, profile.weight);
            if (roll <= accumulatedWeight)
                return profile;
        }

        return _eligibleProfiles[_eligibleProfiles.Count - 1];
    }

    private bool IsProfileEligible(DeskGimmickProfile profile, float roundTime)
    {
        return string.IsNullOrEmpty(GetProfileSkipReason(profile, roundTime));
    }

    private string GetProfileSkipReason(DeskGimmickProfile profile, float roundTime)
    {
        PruneExpiredActiveGimmicks();

        if (profile == null)
            return "profile is null";

        if (profile.target == null)
            return "target is null";

        if (string.IsNullOrEmpty(profile.startMessageName))
            return "startMessageName is empty";

        if (profile.weight <= 0f)
            return "weight is zero or negative";

        if (GetUseCount(profile.type) >= Mathf.Max(0, profile.maxUsesPerRound))
            return "max uses reached";

        if (roundTime < Mathf.Max(0f, initialGimmickDelay))
            return "initial delay";

        if (roundTime < Mathf.Max(0f, profile.minRoundTime))
            return "profile min round time";

        if (Time.time < _nextAllowedTriggerTime)
            return "global gap";

        int activeCount = GetActiveGimmickCount();
        int currentMaxActive = GetMaxActiveGimmicksForRoundTime(roundTime);
        if (activeCount >= currentMaxActive)
            return $"max active reached {activeCount}/{currentMaxActive}";

        if (IsGimmickTypeActive(profile.type))
            return $"same gimmick active type={profile.type}";

        if (profile.requireAtLeastTwoPlayers && CountValidPlayers() < 2)
            return "valid players below two";

        if (profile.isMajor)
        {
            int activeMajorCount = GetActiveMajorGimmickCount();
            int currentMaxMajor = Mathf.Max(0, maxMajorActiveGimmicks);
            if (blockSimultaneousMajorGimmicks && activeMajorCount > 0)
                return $"major gimmick already active {activeMajorCount}/{currentMaxMajor}";

            if (activeMajorCount >= currentMaxMajor)
                return $"max major active reached {activeMajorCount}/{currentMaxMajor}";
        }

        if (IsProfileOnCooldown(profile))
            return "profile cooldown";

        string blockedCombinationReason = GetBlockedCombinationReason(profile);
        if (!string.IsNullOrEmpty(blockedCombinationReason))
            return blockedCombinationReason;

        switch (profile.type)
        {
            case ManagedDeskGimmickType.DeveloperIntrusion:
                if (roundTime < Mathf.Max(0f, developerIntrusionMinRoundTime))
                    return "developer intrusion min round time";

                if (HasRecentGimmick(_lastBombPassTime, developerIntrusionAfterBombBlockTime))
                    return "recent bomb pass";

                if (HasRecentGimmick(_lastLiquidSweepTime, DeveloperIntrusionAfterLiquidBlockTime))
                    return "recent liquid sweep";

                break;

            case ManagedDeskGimmickType.BombPass:
                if (HasRecentGimmick(_lastDeveloperIntrusionTime, BombPassAfterDeveloperBlockTime))
                    return "recent developer intrusion";

                if (HasRecentGimmick(_lastLiquidSweepTime, bombPassAfterLiquidDelay))
                    return "recent liquid sweep";

                break;

            case ManagedDeskGimmickType.LiquidSweep:
            case ManagedDeskGimmickType.KeyboardPop:
                break;
        }

        return null;
    }

    private bool TriggerGimmick(DeskGimmickProfile profile)
    {
        if (profile == null || profile.target == null || string.IsNullOrEmpty(profile.startMessageName))
            return false;

        float roundTime = Mathf.Max(0f, Time.time - _roundStartTime);
        if (!IsProfileEligible(profile, roundTime))
            return false;

        Log($"{SchedulerLogPrefix} Trigger method. type={profile.type} target={profile.target.name} method={profile.startMessageName}");
        profile.target.SendMessage(profile.startMessageName, SendMessageOptions.DontRequireReceiver);

        float now = Time.time;
        float activeDuration = Mathf.Max(0.1f, profile.estimatedActiveDuration);
        IncrementUseCount(profile.type);
        MarkLastGimmickTime(profile.type, now);

        _activeGimmicks.Add(new ActiveGimmickState
        {
            type = profile.type,
            isMajor = profile.isMajor,
            startTime = now,
            endTime = now + activeDuration,
            name = profile.name
        });
        _nextAllowedTriggerTime = now + Mathf.Max(0.1f, globalGimmickGap);
        _nextAllowedGimmickTime = _nextAllowedTriggerTime;
        _hasTriggeredThisRound = true;

        int activeCount = GetActiveGimmickCount();
        int currentMaxActive = GetMaxActiveGimmicksForRoundTime(roundTime);
        int activeMajorCount = GetActiveMajorGimmickCount();
        int currentMaxMajor = Mathf.Max(0, maxMajorActiveGimmicks);
        Log($"{SchedulerLogPrefix} Triggered. type={profile.type} activeEnd={now + activeDuration:0.###} nextAllowed={_nextAllowedTriggerTime:0.###} active={activeCount}/{currentMaxActive} major={activeMajorCount}/{currentMaxMajor}");
        return true;
    }

    private void PruneExpiredActiveGimmicks()
    {
        float now = Time.time;
        for (int i = _activeGimmicks.Count - 1; i >= 0; i--)
        {
            ActiveGimmickState state = _activeGimmicks[i];
            if (now < state.endTime)
                continue;

            _activeGimmicks.RemoveAt(i);
            Log($"{SchedulerLogPrefix} Active gimmick ended. type={state.type} name={state.name} now={now:0.###}");
        }
    }

    private int GetMaxActiveGimmicksForRoundTime(float roundTime)
    {
        int maxActive;
        if (roundTime >= Mathf.Max(0f, finalPhaseStartTime))
            maxActive = finalMaxActiveGimmicks;
        else if (roundTime >= Mathf.Max(0f, latePhaseStartTime))
            maxActive = lateMaxActiveGimmicks;
        else if (roundTime >= Mathf.Max(0f, midPhaseStartTime))
            maxActive = midMaxActiveGimmicks;
        else
            maxActive = earlyMaxActiveGimmicks;

        return Mathf.Max(1, maxActive);
    }

    private int GetActiveGimmickCount()
    {
        PruneExpiredActiveGimmicks();
        return _activeGimmicks.Count;
    }

    private int GetActiveMajorGimmickCount()
    {
        PruneExpiredActiveGimmicks();

        int count = 0;
        for (int i = 0; i < _activeGimmicks.Count; i++)
        {
            if (_activeGimmicks[i].isMajor)
                count++;
        }

        return count;
    }

    private bool IsGimmickTypeActive(ManagedDeskGimmickType type)
    {
        PruneExpiredActiveGimmicks();

        for (int i = 0; i < _activeGimmicks.Count; i++)
        {
            if (_activeGimmicks[i].type == type)
                return true;
        }

        return false;
    }

    private bool IsProfileOnCooldown(DeskGimmickProfile profile)
    {
        if (profile == null)
            return false;

        float lastTime = GetLastGimmickTime(profile.type);
        if (lastTime <= 0f)
            return false;

        float cooldownEndTime = lastTime + Mathf.Max(0.1f, profile.estimatedActiveDuration) + Mathf.Max(0f, profile.cooldownAfter);
        return Time.time < cooldownEndTime;
    }

    private bool IsBlockedByActiveCombination(DeskGimmickProfile profile)
    {
        return !string.IsNullOrEmpty(GetBlockedCombinationReason(profile));
    }

    private string GetBlockedCombinationReason(DeskGimmickProfile profile)
    {
        if (profile == null)
            return null;

        switch (profile.type)
        {
            case ManagedDeskGimmickType.BombPass:
                if (IsGimmickTypeActive(ManagedDeskGimmickType.DeveloperIntrusion))
                    return "developer intrusion active";
                break;

            case ManagedDeskGimmickType.DeveloperIntrusion:
                if (IsGimmickTypeActive(ManagedDeskGimmickType.BombPass))
                    return "bomb pass active";

                if (IsGimmickTypeActive(ManagedDeskGimmickType.LiquidSweep))
                    return "liquid sweep active";
                break;

            case ManagedDeskGimmickType.LiquidSweep:
                if (IsGimmickTypeActive(ManagedDeskGimmickType.DeveloperIntrusion))
                    return "developer intrusion active";

                if (IsGimmickTypeActive(ManagedDeskGimmickType.BombPass))
                    return "bomb pass active";
                break;

            case ManagedDeskGimmickType.KeyboardPop:
                if (IsGimmickTypeActive(ManagedDeskGimmickType.DeveloperIntrusion))
                    return "developer intrusion active";
                break;
        }

        return null;
    }

    private string DescribeActiveGimmicks()
    {
        PruneExpiredActiveGimmicks();

        if (_activeGimmicks.Count == 0)
            return "none";

        string description = string.Empty;
        for (int i = 0; i < _activeGimmicks.Count; i++)
        {
            ActiveGimmickState state = _activeGimmicks[i];
            if (i > 0)
                description += ", ";

            description += $"{state.type}(name={state.name}, major={state.isMajor}, start={state.startTime:0.###}, end={state.endTime:0.###})";
        }

        return description;
    }

    private int CountValidPlayers()
    {
        PlayerStatusModule[] found = FindPlayerStatusModules();
        if (found == null)
            return CountValidHamsterRecoveryAdapters();

        int count = 0;
        for (int i = 0; i < found.Length; i++)
        {
            PlayerStatusModule status = found[i];
            if (status == null)
                continue;

            if (!status.IsSpawned)
                continue;

            if (status.IsEliminated)
                continue;

            count++;
        }

        return count + CountValidHamsterRecoveryAdapters();
    }

    private int CountValidHamsterRecoveryAdapters()
    {
#if UNITY_6000_0_OR_NEWER
        HamsterMotorShellRagdollRecoveryAdapter[] found = FindObjectsByType<HamsterMotorShellRagdollRecoveryAdapter>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
#else
        HamsterMotorShellRagdollRecoveryAdapter[] found = FindObjectsOfType<HamsterMotorShellRagdollRecoveryAdapter>();
#endif
        if (found == null)
            return 0;

        int count = 0;
        for (int i = 0; i < found.Length; i++)
        {
            HamsterMotorShellRagdollRecoveryAdapter recovery = found[i];
            if (recovery != null && recovery.CanReceiveRecoveryState)
                count++;
        }

        return count;
    }

    private bool HasRecentGimmick(float lastTime, float blockDuration)
    {
        if (blockDuration <= 0f)
            return false;

        return Time.time - lastTime < blockDuration;
    }

    private bool IsActiveGimmick(ManagedDeskGimmickType type)
    {
        return IsGimmickTypeActive(type);
    }

    private bool IsMajorGimmickActive()
    {
        return GetActiveMajorGimmickCount() > 0;
    }

    private float GetLastGimmickTime(ManagedDeskGimmickType type)
    {
        switch (type)
        {
            case ManagedDeskGimmickType.KeyboardPop:
                return _lastKeyboardPopTime;
            case ManagedDeskGimmickType.LiquidSweep:
                return _lastLiquidSweepTime;
            case ManagedDeskGimmickType.BombPass:
                return _lastBombPassTime;
            case ManagedDeskGimmickType.DeveloperIntrusion:
                return _lastDeveloperIntrusionTime;
        }

        return float.NegativeInfinity;
    }

    private DeskGimmickProfile FindProfile(ManagedDeskGimmickType type)
    {
        if (gimmickProfiles == null)
            return null;

        for (int i = 0; i < gimmickProfiles.Length; i++)
        {
            DeskGimmickProfile profile = gimmickProfiles[i];
            if (profile != null && profile.type == type)
                return profile;
        }

        return null;
    }

    private int GetUseCount(ManagedDeskGimmickType type)
    {
        return _usesThisRound.TryGetValue(type, out int count) ? count : 0;
    }

    private void IncrementUseCount(ManagedDeskGimmickType type)
    {
        _usesThisRound[type] = GetUseCount(type) + 1;
    }

    private void MarkLastGimmickTime(ManagedDeskGimmickType type, float time)
    {
        switch (type)
        {
            case ManagedDeskGimmickType.KeyboardPop:
                _lastKeyboardPopTime = time;
                break;
            case ManagedDeskGimmickType.LiquidSweep:
                _lastLiquidSweepTime = time;
                break;
            case ManagedDeskGimmickType.BombPass:
                _lastBombPassTime = time;
                break;
            case ManagedDeskGimmickType.DeveloperIntrusion:
                _lastDeveloperIntrusionTime = time;
                break;
        }
    }

    private void ResetLastGimmickTimes()
    {
        _lastKeyboardPopTime = float.NegativeInfinity;
        _lastLiquidSweepTime = float.NegativeInfinity;
        _lastBombPassTime = float.NegativeInfinity;
        _lastDeveloperIntrusionTime = float.NegativeInfinity;
    }

    private void StopSchedulerRoutine()
    {
        if (_schedulerRoutine == null)
            return;

        StopCoroutine(_schedulerRoutine);
        _schedulerRoutine = null;
    }

    private DeskGimmickProfile CreateProfile(
        string profileName,
        ManagedDeskGimmickType type,
        MonoBehaviour target,
        string startMessageName,
        float weight,
        int maxUsesPerRound,
        float minRoundTime,
        float estimatedActiveDuration,
        float cooldownAfter,
        bool isMajor,
        bool requireAtLeastTwoPlayers)
    {
        return new DeskGimmickProfile
        {
            name = profileName,
            type = type,
            target = target,
            startMessageName = startMessageName,
            weight = weight,
            maxUsesPerRound = maxUsesPerRound,
            minRoundTime = minRoundTime,
            estimatedActiveDuration = estimatedActiveDuration,
            cooldownAfter = cooldownAfter,
            isMajor = isMajor,
            requireAtLeastTwoPlayers = requireAtLeastTwoPlayers
        };
    }

    private T FindFirstGimmickTarget<T>() where T : MonoBehaviour
    {
#if UNITY_6000_0_OR_NEWER
        T[] found = FindObjectsByType<T>(FindObjectsInactive.Include, FindObjectsSortMode.None);
#else
        T[] found = FindObjectsOfType<T>(true);
#endif
        if (found == null || found.Length == 0)
            return null;

        return found[0];
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
