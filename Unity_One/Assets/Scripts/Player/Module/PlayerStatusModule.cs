using Unity.Netcode;
using UnityEngine;

public class PlayerStatusModule : NetworkBehaviour, IDamageable
{
    [Header("Knockback")]
    [Tooltip("넉백/다운 시 물리를 적용할 루트 Rigidbody")]
    [SerializeField] private Rigidbody rootRigidbody;

    [Tooltip("평상시 이동을 담당하는 CharacterController")]
    [SerializeField] private CharacterController charController;

    [Tooltip("이동 모듈. 기상/다운 전환 때 잔여 속도를 초기화합니다.")]
    [SerializeField] private PlayerLocomotionModule locomotionModule;

    [Tooltip("다운 상태 유지 시간(초)")]
    [SerializeField] private float knockbackDuration = 1.2f;

    [Tooltip("이 속도 이하로 떨어져야 기상 루프를 시작합니다.")]
    [SerializeField] private float standUpVelocityThreshold = 0.15f;

    [Header("Knockback Tuning")]
    [Tooltip("질량 영향을 무시하고 바로 속도를 부여합니다. 작은 캐릭터 넉백에 더 잘 맞습니다.")]
    [SerializeField] private bool useVelocityChange = true;

    [Tooltip("수평 넉백 세기. 뒤로 미는 힘을 얼마나 줄지 조절합니다.")]
    [SerializeField] private float horizontalLaunchScale = 0.65f;

    [Tooltip("위로 띄우는 최소 속도. 이 값이 낮으면 미끄러지듯 밀립니다.")]
    [SerializeField] private float minimumUpwardLaunch = 2.8f;

    [Tooltip("기본 위쪽 힘에 추가로 더해줄 상승 보정값입니다.")]
    [SerializeField] private float upwardLaunchBonus = 1.1f;

    [Tooltip("옆으로/앞뒤로 구르도록 주는 회전 토크 세기입니다.")]
    [SerializeField] private float tumbleTorque = 10f;

    [Tooltip("약간의 랜덤 회전을 섞어 덜 기계적으로 보이게 합니다.")]
    [SerializeField] private float randomYawTorque = 2.5f;

    [Tooltip("진짜 물리로 굴릴지 여부. 꺼두면 회전은 고정된 채로 밀려납니다.")]
    [SerializeField] private bool allowKnockRotation = true;

    [Header("Stand Up")]
    [Tooltip("기상 트리거를 보낼 애니메이션 모듈")]
    [SerializeField] private PlayerAnimModule animModule;

    [Tooltip("기상 애니메이션 상태 확인에 사용할 Animator")]
    [SerializeField] private Animator rootAnimator;

    [Tooltip("다운 종료 후 Back Stand Up 애니메이션으로 기상할지")]
    [SerializeField] private bool useBackStandUp = true;

    [Tooltip("기상 애니메이션 중 CharacterController를 계속 끌지")]
    [SerializeField] private bool disableControllerDuringStandUp = true;

    [Tooltip("애니메이션 이벤트가 누락됐을 때 강제로 standing으로 복귀시키는 최대 대기 시간(초)")]
    [SerializeField] private float standUpFallbackTime = 3.6f;

    [Tooltip("Stand-up 애니메이션이 이 normalized time 이상 진행된 뒤에만 조작 잠금을 풀 수 있습니다.")]
    [SerializeField] private float standUpFinishNormalizedTime = 0.98f;

    [Tooltip("Stand-up 시작 후 최소한 이 시간 동안 이동/공격/상호작용을 막습니다.")]
    [SerializeField] private float standUpMinimumControlLockSeconds = 0.8f;

    [Tooltip("애니메이션 이벤트가 오지 않아도 stand-up을 안전하게 끝낼 fallback 시간입니다.")]
    [SerializeField] private float standUpFallbackFinishSeconds = 1.25f;

    [Tooltip("Stand-up physics recovery 이후 애니메이션/transition이 마무리될 때까지 추가로 이동/공격/상호작용을 막는 시간입니다.")]
    [SerializeField] private float standUpPostFinishControlLockSeconds = 0.45f;

    [Tooltip("Stand-up physics recovery 이후에도 Animator가 stand-up 상태/transition에서 빠질 때까지 조작을 계속 막을지 여부입니다.")]
    [SerializeField] private bool standUpWaitForAnimatorExit = true;

    [Tooltip("Animator stand-up 종료를 기다리는 최대 시간입니다. 이 시간이 지나면 안전하게 조작 잠금을 해제합니다.")]
    [SerializeField] private float standUpAnimatorExitMaxLockSeconds = 1.2f;

    [Tooltip("기상 시작 시 루트 회전을 지면 기준으로 바로 세웁니다.")]
    [SerializeField] private bool snapUprightOnStandUp = true;

    [Tooltip("Stand-up 시작 시 root pitch/roll을 즉시 세우며 생기는 순간 뒤집힘을 줄이기 위해 root snap을 조건부로 완화합니다.")]
    [SerializeField] private bool reduceStandUpRootSnap = true;

    [Tooltip("엎드림/측면처럼 불안정한 ragdoll pose에서는 stand-up 시작 시 즉시 upright snap을 하지 않고 종료 시점으로 미룹니다.")]
    [SerializeField] private bool deferUprightSnapOnUnsafeStandUpPose = true;

    [Tooltip("root up 벡터와 world up의 dot 값이 이 값보다 낮으면 stand-up 시작 pose가 불안정한 것으로 간주합니다.")]
    [SerializeField] private float unsafeStandUpPoseUpDotThreshold = 0.35f;

    [Tooltip("root forward/up 방향을 이용한 prone/side pose 감지 보조 임계값입니다.")]
    [SerializeField] private float unsafeStandUpPoseForwardDotThreshold = 0.25f;

    [Tooltip("Stand-up pose 샘플링과 root snap 결정 로그를 출력할지 여부입니다.")]
    [SerializeField] private bool standUpPoseSnapDebugLogs = false;

    [Header("Ground Snap")]
    [Tooltip("기상 완료 시 루트를 바닥에 맞춰 한 번 내려붙일지")]
    [SerializeField] private bool snapToGroundOnStandUpFinish = true;

    [Tooltip("기상 애니 재생 중에도 루트를 바닥에 계속 붙일지")]
    [SerializeField] private bool snapToGroundDuringStandUp = true;

    [Tooltip("기상 애니에서 이 normalized time 이후부터 ground snap을 시작합니다.")]
    [SerializeField] private float standUpSnapStartNormalized = 0.10f;

    [Tooltip("기상 완료 직후 CharacterController를 켠 다음 아래로 살짝 눌러 바닥에 붙입니다.")]
    [SerializeField] private float postStandUpDownNudge = 0.02f;

    [Tooltip("Stand-up 중 캐릭터 root가 바닥 아래로 파고들지 않도록 최소 지면 여유 높이를 유지합니다.")]
    [SerializeField] private bool maintainGroundClearanceDuringStandUp = true;

    [Tooltip("Stand-up 중 root가 ground 기준으로 유지해야 하는 최소 높이입니다.")]
    [SerializeField] private float standUpGroundClearance = 0.18f;

    [Tooltip("Stand-up 중 ground를 찾기 위해 아래 방향으로 검사하는 거리입니다.")]
    [SerializeField] private float standUpGroundProbeDistance = 1.5f;

    [Tooltip("한 틱에 stand-up ground clearance 보정으로 root를 위로 올릴 수 있는 최대 거리입니다.")]
    [SerializeField] private float standUpMaxGroundCorrectionPerTick = 0.08f;

    [Tooltip("한 번의 stand-up 동안 ground clearance 보정으로 root를 올릴 수 있는 최대 총 거리입니다.")]
    [SerializeField] private float standUpMaxTotalGroundLift = 0.35f;

    [Tooltip("Stand-up ground clearance 보정 로그를 출력할지 여부입니다.")]
    [SerializeField] private bool standUpGroundClearanceDebugLogs = false;

    [Tooltip("바닥 검사 시작 높이")]
    [SerializeField] private float groundProbeHeight = 1.2f;

    [Tooltip("바닥 검사 최대 거리")]
    [SerializeField] private float groundProbeDistance = 3.0f;

    [Tooltip("기상 후 바닥에 살짝 띄워둘 보정값")]
    [SerializeField] private float groundContactOffset = 0.01f;

    [Tooltip("바닥 스냅에 사용할 레이어 마스크. 비워두면 기본 Raycast 레이어를 사용합니다.")]
    [SerializeField] private LayerMask groundMask;

    [Tooltip("루트/자식 자신의 콜라이더는 바닥 판정에서 무시합니다.")]
    [SerializeField] private bool ignoreOwnCollidersOnGroundSnap = true;

    [Header("Ragdoll Root Sync")]
    [SerializeField, Tooltip("Ragdoll 복구/기상 직전에 Player root를 Ragdoll 중심 위치 근처로 1회 보정할지 여부입니다.")]
    private bool syncRootToRagdollFocusBeforeStandUp = true;

    [SerializeField, Tooltip("Ragdoll focus 위치를 root 보정에 사용할 수 있는 최대 시간입니다.")]
    private float ragdollRootSyncFocusMaxAge = 1.2f;

    [SerializeField, Tooltip("root와 Ragdoll focus 사이 거리가 이 값보다 작으면 위치 보정을 생략합니다.")]
    private float ragdollRootSyncMinDistance = 0.25f;

    [SerializeField, Tooltip("root와 Ragdoll focus 사이 거리가 이 값보다 크면 위치 보정을 생략합니다. 0 이하이면 제한하지 않습니다.")]
    private float ragdollRootSyncMaxDistance = 20f;

    [SerializeField, Tooltip("Ragdoll focus 위치에서 지면을 찾기 위해 위로 올리는 Raycast 시작 높이입니다.")]
    private float ragdollRootSyncGroundProbeHeight = 3f;

    [SerializeField, Tooltip("Ragdoll focus 위치에서 지면을 찾기 위해 아래로 쏘는 Raycast 거리입니다.")]
    private float ragdollRootSyncGroundProbeDistance = 8f;

    [SerializeField, Tooltip("지면에 root를 맞출 때 추가로 적용할 Y 오프셋입니다.")]
    private float ragdollRootSyncGroundOffset = 0.05f;

    [Header("Hit Reaction")]
    [Tooltip("데미지를 받으면 Hit 트리거를 보낼지")]
    [SerializeField] private bool triggerHitOnDamage = false;

    [Tooltip("피격 트리거 최소 간격(초). 너무 짧으면 애니메이션이 과도하게 끊깁니다.")]
    [SerializeField] private float hitReactionCooldown = 0.12f;

    [Tooltip("다운 상태에서도 Hit 트리거를 허용할지")]
    [SerializeField] private bool triggerHitWhileKnocked = false;

    [Header("Active Ragdoll Hit Reaction")]
    [Tooltip("피격/넉백 시 Active Ragdoll 피격 반응을 호출할지 여부입니다.")]
    [SerializeField] private bool enableActiveRagdollHitReaction = true;

    [Tooltip("Active Ragdoll 피격 반응에 전달할 넉백 힘 배율입니다.")]
    [SerializeField] private float activeRagdollHitImpulseScale = 1f;

    [Header("Knockback Auto Drop")]
    [Tooltip("공격이나 맵 기믹 넉백으로 플레이어가 강제로 날아갈 때 들고 있는 아이템을 자동으로 떨어뜨릴지 여부입니다.")]
    [SerializeField] private bool dropHeldItemOnKnockback = true;

    [Tooltip("넉백 자동 드랍이 짧은 시간 안에 중복 호출되는 것을 막기 위한 쿨다운입니다.")]
    [SerializeField] private float dropHeldItemOnKnockbackCooldown = 0.35f;

    [Tooltip("넉백 자동 드랍 디버그 로그를 출력할지 여부입니다.")]
    [SerializeField] private bool enableDropHeldItemOnKnockbackDebugLogs = false;

    [Header("Elimination")]
    [Tooltip("이 높이 아래로 떨어지면 탈락 처리되는 Y값")]
    [SerializeField] private float eliminationY = -15f;

    [Tooltip("Ragdoll 활성 중에는 Ragdoll 중심 위치를 낙사 판정에 사용할지 여부입니다.")]
    [SerializeField] private bool useRagdollFocusForElimination = true;

    [Tooltip("Ragdoll 중심 위치가 낙사 판정에 사용될 때 추가로 적용할 Y 오프셋입니다.")]
    [SerializeField] private float ragdollFocusEliminationYOffset = 0f;

    [Header("Coin Fall Respawn")]
    [SerializeField, Tooltip("낙사 시 코인을 보유하고 있으면 기존 탈락 대신 코인 차감 후 리스폰할지 여부입니다.")]
    private bool useCoinRespawnOnFall = true;

    [SerializeField, Tooltip("코인 리스폰 처리 시 차감된 코인을 맵에 드랍할지 여부입니다.")]
    private bool dropCoinsOnFallRespawn = true;

    [SerializeField, Tooltip("Playing 상태에서만 코인 기반 낙사 리스폰을 허용할지 여부입니다.")]
    private bool requirePlayingStateForCoinRespawn = true;

    [SerializeField, Tooltip("매치 매니저를 찾지 못했을 때도 코인 기반 리스폰을 허용할지 여부입니다. 테스트 목적 외에는 끄는 것을 권장합니다.")]
    private bool allowRespawnWhenMatchManagerMissing = false;

    [SerializeField, Tooltip("리스폰 위치 주변에 드랍 코인을 흩뿌릴 반경입니다.")]
    private float coinDropSpawnRadius = 0.75f;

    [SerializeField, Tooltip("드랍 코인을 리스폰 위치보다 얼마나 위에 생성할지 설정합니다.")]
    private float coinDropSpawnHeightOffset = 0.25f;

    [SerializeField, Tooltip("드랍 코인을 생성할 때 실패를 대비해 시도할 최대 횟수입니다.")]
    private int maxCoinDropSpawnAttempts = 16;

    [SerializeField, Tooltip("드랍 코인을 특정 위치 주변이 아니라 CoinSpawnManager의 스폰 영역에 랜덤으로 생성할지 여부입니다.")]
    private bool preferCoinSpawnManagerRandomDrop = true;

    [SerializeField, Tooltip("코인 기반 낙사 리스폰 처리 로그를 출력할지 여부입니다.")]
    private bool enableCoinRespawnDebugLogs = false;

    [Header("Combat Fall Contribution")]
    [SerializeField, Tooltip("전투 공격으로 인한 낙사 기여 기록을 사용할지 여부입니다.")]
    private bool enableCombatFallContributionTracking = true;

    [SerializeField, Tooltip("최근 전투 기여자가 KnockOff 낙사 기여자로 인정되는 유효 시간입니다.")]
    private float recentCombatContributorValidSeconds = 4f;

    [SerializeField, Tooltip("코인을 소모하고 리스폰되는 낙사도 KnockOff 기여로 인정할지 여부입니다.")]
    private bool countCoinRespawnFallAsKnockOff = true;

    [SerializeField, Tooltip("전투 낙사 기여 기록 디버그 로그를 출력할지 여부입니다.")]
    private bool enableCombatFallContributionDebugLogs = false;

    [Header("Debug")]
    [Tooltip("디버그 로그 출력 여부입니다.")]
    [SerializeField] private bool enableDebugLogs = false;

    [Tooltip("임시 조작 잠금 상태의 시작/종료 디버그 로그를 출력할지 여부입니다.")]
    [SerializeField] private bool enableTemporaryControlLockDebugLogs = false;

    [Tooltip("Stand-up 조작 잠금/해제 디버그 로그를 출력할지 여부입니다.")]
    [SerializeField] private bool enableStandUpControlLockDebugLogs = false;

    private bool isKnocked;
    private bool isStandingUp;
    private bool isEliminated;
    private float knockTimer;
    private float nextHitReactionAt;
    private float standUpTimer;
    private float _standUpStartedAt;
    private bool _standUpFinishEventReceived;
    private bool _standUpMinimumDelayLogged;
    private float _standUpControlLockedUntil;
    private bool _waitingForStandUpAnimatorExit;
    private float _standUpAnimatorExitLockStartedAt;
    private bool _standUpAnimatorStillActiveLogged;
    private bool _deferredUprightSnapForCurrentStandUp;
    private string _lastStandUpPoseReason = string.Empty;
    private float _standUpTotalGroundLiftApplied;
    private float _nextStandUpGroundClearanceLogAt;
    private bool _hasTemporaryControlLock;
    private float _temporaryControlLockUntil;
    private bool _temporaryControlLockMove;
    private bool _temporaryControlLockAttack;
    private bool _temporaryControlLockInteract;

    private NetworkObject rootNetObj;
    private Transform rootTransform;
    private RigidbodyConstraints cachedConstraints;
    private int backStandUpStateHash;
    private PlayerHub playerHub;
    private InGameMatchManager inGameMatchManager;
    private CoinSpawnManager coinSpawnManager;
    private GameStateManager gameStateManager;
    private float nextGameStateManagerLookupAt;
    private bool hasLoggedMissingGameStateManager;
    private bool hasLoggedEliminationGateState;
    private bool hasReachedSafePlayingPosition;
    private GameStateManager.GameState lastLoggedGameState;
    private SugaActiveRagdollController _activeRagdollController;
    private bool hasLoggedMissingActiveRagdollController;
    private Vector3 _lastRagdollFocusForRootSync;
    private float _lastRagdollFocusForRootSyncTime;
    private bool _hasLastRagdollFocusForRootSync;
    private bool _didSyncRootFromRagdollForCurrentKnockback;
    private bool _hasRecentCombatFallContributor;
    private ulong _recentCombatFallContributorClientId;
    private float _recentCombatFallContributorRecordedAt;
    private bool _hasReportedFallContributionForCurrentFall;
    private RoundMissionManager _roundMissionManager;
    private float _nextDropHeldItemOnKnockbackAllowedAt;
    private PlayerInteractModule _cachedInteractModule;

    public bool IsKnocked => isKnocked;
    public bool IsStandingUp => isStandingUp;
    public bool IsEliminated => isEliminated;
    public bool IsTemporaryControlLocked => IsTemporaryControlLockActive();
    public float TemporaryControlLockRemainingSeconds
    {
        get
        {
            if (!IsTemporaryControlLockActive())
                return 0f;

            return Mathf.Max(0f, _temporaryControlLockUntil - Time.time);
        }
    }
    public bool CanMove => !isKnocked && !isStandingUp && !IsStandUpVisualControlLocked && !isEliminated && !ShouldBlockMoveByTemporaryLock();
    public bool CanAttack => !isKnocked && !isStandingUp && !IsStandUpVisualControlLocked && !isEliminated && !ShouldBlockAttackByTemporaryLock();
    public bool CanInteract => !isKnocked && !isStandingUp && !IsStandUpVisualControlLocked && !isEliminated && !ShouldBlockInteractByTemporaryLock();
    public bool HasRecentCombatFallContributor => IsServer && IsRecentCombatContributorValid();

    public bool ServerApplyTemporaryControlLock(float duration)
    {
        return ServerApplyTemporaryControlLock(duration, true, true, true);
    }

    public bool ServerApplyTemporaryControlLock(float duration, bool lockMove, bool lockAttack, bool lockInteract)
    {
        if (!IsServer)
            return false;

        if (isEliminated)
            return false;

        if (!lockMove && !lockAttack && !lockInteract)
            return false;

        float finiteDuration = GetFiniteOrZero(duration);
        if (finiteDuration <= 0f)
            return false;

        RefreshTemporaryControlLock();

        float lockUntil = Time.time + finiteDuration;
        if (!_hasTemporaryControlLock || lockUntil > _temporaryControlLockUntil)
            _temporaryControlLockUntil = lockUntil;

        _hasTemporaryControlLock = true;
        _temporaryControlLockMove |= lockMove;
        _temporaryControlLockAttack |= lockAttack;
        _temporaryControlLockInteract |= lockInteract;

        LogTemporaryControlLock($"applied. duration:{finiteDuration:0.###}, remaining:{TemporaryControlLockRemainingSeconds:0.###}, move:{_temporaryControlLockMove}, attack:{_temporaryControlLockAttack}, interact:{_temporaryControlLockInteract}");
        return true;
    }

    public void ServerClearTemporaryControlLock()
    {
        if (!IsServer)
            return;

        ClearTemporaryControlLockLocal();
    }

    private void Awake()
    {
        ResolveRefs();
        backStandUpStateHash = Animator.StringToHash("Back Stand Up");
        if (rootRigidbody != null)
            cachedConstraints = rootRigidbody.constraints;
        ApplyStandingPhysicsState();
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        ResolveRefs();
        backStandUpStateHash = Animator.StringToHash("Back Stand Up");
        if (rootRigidbody != null)
            cachedConstraints = rootRigidbody.constraints;
    }

    private void Update()
    {
        if (!IsServer) return;
        RefreshTemporaryControlLock();
        if (isEliminated) return;

        UpdateKnockState();
        UpdateStandUpState();
        UpdateStandUpAnimatorExitLock();
        CheckElimination();
    }

    private bool IsTemporaryControlLockActive()
    {
        RefreshTemporaryControlLock();
        return _hasTemporaryControlLock;
    }

    private void RefreshTemporaryControlLock()
    {
        if (!_hasTemporaryControlLock)
            return;

        if (Time.time < _temporaryControlLockUntil)
            return;

        ClearTemporaryControlLockLocal();
    }

    private void ClearTemporaryControlLockLocal()
    {
        bool hadTemporaryControlLock =
            _hasTemporaryControlLock ||
            _temporaryControlLockUntil > 0f ||
            _temporaryControlLockMove ||
            _temporaryControlLockAttack ||
            _temporaryControlLockInteract;

        _hasTemporaryControlLock = false;
        _temporaryControlLockUntil = 0f;
        _temporaryControlLockMove = false;
        _temporaryControlLockAttack = false;
        _temporaryControlLockInteract = false;

        if (hadTemporaryControlLock)
            LogTemporaryControlLock("cleared.");
    }

    private bool ShouldBlockMoveByTemporaryLock()
    {
        return IsTemporaryControlLockActive() && _temporaryControlLockMove;
    }

    private bool ShouldBlockAttackByTemporaryLock()
    {
        return IsTemporaryControlLockActive() && _temporaryControlLockAttack;
    }

    private bool ShouldBlockInteractByTemporaryLock()
    {
        return IsTemporaryControlLockActive() && _temporaryControlLockInteract;
    }

    public void ApplyKnockbackServer(Vector3 impulse)
    {
        ApplyKnockbackServerInternal(impulse, true);
    }

    public bool ServerTryApplyCombatKnockback(Vector3 impulse, ulong actorClientId)
    {
        if (!CanStartKnockbackServer())
            return false;

        bool recordedContributor = ServerRecordRecentCombatFallContributor(actorClientId);
        ApplyKnockbackServerInternal(impulse, false);
        return recordedContributor;
    }

    public bool ServerRecordRecentCombatFallContributor(ulong actorClientId)
    {
        if (!IsServer)
            return false;

        if (!enableCombatFallContributionTracking)
            return false;

        if (!TryGetOwnerClientId(out ulong targetClientId))
            return false;

        if (!IsValidCombatContributor(actorClientId, targetClientId))
            return false;

        _hasRecentCombatFallContributor = true;
        _recentCombatFallContributorClientId = actorClientId;
        _recentCombatFallContributorRecordedAt = Time.time;
        LogCombatFallContribution($"Recorded recent contributor actor:{actorClientId} target:{targetClientId}");
        return true;
    }

    public void ServerClearRecentCombatFallContributor()
    {
        if (!IsServer)
            return;

        ClearRecentCombatContributorServer();
    }

    public bool TryGetRecentCombatFallContributor(out ulong actorClientId)
    {
        actorClientId = _recentCombatFallContributorClientId;
        if (!IsServer || !IsRecentCombatContributorValid())
        {
            actorClientId = ulong.MaxValue;
            return false;
        }

        return true;
    }

    private void ApplyKnockbackServerInternal(Vector3 impulse, bool clearCombatContributor)
    {
        if (!IsServer) return;
        if (isEliminated) return;
        if (isKnocked) return;

        if (rootRigidbody == null || charController == null)
        {
            Debug.LogWarning("[PlayerStatus] Knockback skipped. Missing Rigidbody or CharacterController.");
            return;
        }

        if (clearCombatContributor)
            ClearRecentCombatContributorServer();

        if (isStandingUp)
            isStandingUp = false;

        ServerTryDropHeldItemBecauseOfKnockback("Knockback");
        BeginKnockback(impulse);
        TryApplyActiveRagdollHit(impulse);
    }

    public void ForceRecoverServer()
    {
        if (!IsServer) return;
        if (isEliminated) return;

        if (isKnocked)
        {
            BeginStandUpBack();
            return;
        }

        if (isStandingUp)
            FinishStandUpImmediate();
    }

    [ContextMenu("Auto Find Refs")]
    private void ResolveRefs()
    {
        if (rootRigidbody == null)
            rootRigidbody = GetComponentInParent<Rigidbody>();

        if (charController == null)
            charController = GetComponentInParent<CharacterController>();

        if (locomotionModule == null)
            locomotionModule = GetComponentInParent<PlayerLocomotionModule>();

        if (animModule == null)
            animModule = GetComponentInParent<PlayerAnimModule>();

        if (rootAnimator == null)
            rootAnimator = GetComponentInParent<Animator>();

        if (rootNetObj == null)
            rootNetObj = GetComponentInParent<NetworkObject>();

        if (playerHub == null)
            playerHub = GetComponentInParent<PlayerHub>();

        if (gameStateManager == null)
            gameStateManager = FindFirstObjectByType<GameStateManager>();

        ResolveRoundMissionManager();
        ResolveActiveRagdollController();
        rootTransform = rootNetObj != null ? rootNetObj.transform : transform.root;
    }

    private void ResolveRoundMissionManager()
    {
        if (_roundMissionManager == null)
            _roundMissionManager = FindFirstObjectByType<RoundMissionManager>();
    }

    private SugaActiveRagdollController ResolveActiveRagdollController()
    {
        if (_activeRagdollController != null)
            return _activeRagdollController;

        _activeRagdollController = GetComponent<SugaActiveRagdollController>();

        if (_activeRagdollController == null)
            _activeRagdollController = GetComponentInParent<SugaActiveRagdollController>();

        if (_activeRagdollController == null)
            _activeRagdollController = GetComponentInChildren<SugaActiveRagdollController>(true);

        if (_activeRagdollController != null)
            hasLoggedMissingActiveRagdollController = false;

        return _activeRagdollController;
    }

    private void TryApplyActiveRagdollHit(Vector3 impulse)
    {
        if (!enableActiveRagdollHitReaction)
            return;

        SugaActiveRagdollController controller = ResolveActiveRagdollController();
        if (controller == null)
        {
            if (!hasLoggedMissingActiveRagdollController)
            {
                Log("[PlayerStatus] Active ragdoll hit skipped. controller missing.");
                hasLoggedMissingActiveRagdollController = true;
            }

            return;
        }

        Vector3 ragdollImpulse = impulse * activeRagdollHitImpulseScale;
        controller.ApplyHit(ragdollImpulse);
        Log("[PlayerStatus] Active ragdoll hit reaction triggered.");
    }

    private bool CanStartKnockbackServer()
    {
        if (!IsServer)
            return false;

        if (isEliminated || isKnocked)
            return false;

        return rootRigidbody != null && charController != null;
    }

    private bool TryGetOwnerClientId(out ulong clientId)
    {
        clientId = ulong.MaxValue;

        if (rootNetObj == null)
            rootNetObj = GetComponentInParent<NetworkObject>();

        NetworkObject ownerObject = rootNetObj != null ? rootNetObj : NetworkObject;
        if (ownerObject == null)
            return false;

        clientId = ownerObject.OwnerClientId;
        return clientId != ulong.MaxValue;
    }

    private bool IsRecentCombatContributorValid()
    {
        if (!_hasRecentCombatFallContributor)
            return false;

        if (!enableCombatFallContributionTracking)
        {
            ClearRecentCombatContributorServer();
            return false;
        }

        float validSeconds = Mathf.Max(0f, recentCombatContributorValidSeconds);
        float elapsedSeconds = Mathf.Max(0f, Time.time - _recentCombatFallContributorRecordedAt);
        if (elapsedSeconds <= validSeconds)
            return true;

        ClearRecentCombatContributorServer();
        return false;
    }

    private void ClearRecentCombatContributorServer()
    {
        _hasRecentCombatFallContributor = false;
        _recentCombatFallContributorClientId = ulong.MaxValue;
        _recentCombatFallContributorRecordedAt = 0f;
    }

    private bool TryReportCombatFallContributionServer(string context)
    {
        if (!IsServer)
            return false;

        if (_hasReportedFallContributionForCurrentFall)
            return false;

        if (!TryGetRecentCombatFallContributor(out ulong actorClientId))
            return false;

        if (!TryGetOwnerClientId(out ulong targetClientId))
        {
            ClearRecentCombatContributorServer();
            return false;
        }

        if (!IsValidCombatContributor(actorClientId, targetClientId))
        {
            ClearRecentCombatContributorServer();
            return false;
        }

        ResolveRoundMissionManager();
        if (_roundMissionManager == null)
        {
            ClearRecentCombatContributorServer();
            return false;
        }

        _roundMissionManager.ServerRecordFallContribution(actorClientId, targetClientId);
        _hasReportedFallContributionForCurrentFall = true;
        ClearRecentCombatContributorServer();
        LogCombatFallContribution($"Reported fall contribution actor:{actorClientId} target:{targetClientId} context:{context}");
        return true;
    }

    private void ResetFallContributionReportGuard()
    {
        _hasReportedFallContributionForCurrentFall = false;
    }

    private bool IsValidCombatContributor(ulong actorClientId, ulong targetClientId)
    {
        if (actorClientId == ulong.MaxValue || targetClientId == ulong.MaxValue)
            return false;

        if (actorClientId == targetClientId)
            return false;

        NetworkManager networkManager = NetworkManager.Singleton;
        if (networkManager != null && networkManager.IsListening &&
            !networkManager.ConnectedClients.ContainsKey(actorClientId))
            return false;

        return true;
    }

    private void UpdateKnockState()
    {
        if (!isKnocked) return;
        if (isStandingUp) return;

        CacheRagdollFocusForRootSync();

        knockTimer -= Time.deltaTime;
        if (knockTimer > 0f)
            return;

        float speed = 0f;
        if (rootRigidbody != null && !rootRigidbody.isKinematic)
            speed = rootRigidbody.linearVelocity.magnitude;

        if (speed > standUpVelocityThreshold)
            return;

        BeginStandUpBack();
    }

    private void UpdateStandUpState()
    {
        if (!isStandingUp) return;

        if (_standUpFinishEventReceived)
        {
            TryFinishStandUp("AnimationEvent");
            if (!isStandingUp) return;
        }

        if (rootAnimator != null)
        {
            AnimatorStateInfo state = rootAnimator.GetCurrentAnimatorStateInfo(0);
            bool inBackStandUp = state.shortNameHash == backStandUpStateHash || state.IsName("Base Layer.Back Stand Up") || state.IsName("Back Stand Up");

            if (inBackStandUp)
            {
                if (snapToGroundDuringStandUp && state.normalizedTime >= standUpSnapStartNormalized && state.normalizedTime < 0.98f)
                    SnapRootToGround();

                if (state.normalizedTime >= Mathf.Clamp01(standUpFinishNormalizedTime))
                {
                    TryFinishStandUp("NormalizedTime");
                    return;
                }
            }
        }

        MaintainStandUpGroundClearance("UpdateStandUp");

        standUpTimer -= Time.deltaTime;
        if (standUpTimer > 0f && !HasStandUpFallbackExpired())
            return;

        TryFinishStandUp("Fallback");
    }

    private void CheckElimination()
    {
        Transform checkTf = rootTransform != null ? rootTransform : transform.root;
        if (checkTf == null) return;
        if (!IsEliminationAllowedByGameState(checkTf)) return;

        float rootY = checkTf.position.y;
        float eliminationCheckY = GetEliminationCheckY(rootY);
        if (eliminationCheckY < eliminationY)
        {
            if (eliminationCheckY < rootY)
                Log($"[PlayerStatus] Elimination triggered by ragdoll focus. rootY:{rootY:0.###}, checkY:{eliminationCheckY:0.###}, eliminationY:{eliminationY:0.###}");

            bool reportedFallContribution = false;
            if (countCoinRespawnFallAsKnockOff)
                reportedFallContribution = TryReportCombatFallContributionServer("coin-or-elimination fall");

            if (TryHandleCoinFallRespawn())
                return;

            if (!reportedFallContribution)
                TryReportCombatFallContributionServer("final elimination fall");

            HandleElimination();
        }
    }

    private bool TryHandleCoinFallRespawn()
    {
        if (!IsServer) return false;
        if (!useCoinRespawnOnFall) return false;
        if (isEliminated) return false;

        if (requirePlayingStateForCoinRespawn && !IsCoinFallRespawnAllowedByGameState())
            return false;

        PlayerHub ownerHub = ResolvePlayerHub();
        if (ownerHub == null)
        {
            LogCoinFallRespawn("Coin fall respawn skipped. PlayerHub not found.");
            return false;
        }

        PlayerCoinWalletModule wallet = ownerHub.CoinWalletModule;
        if (wallet == null)
        {
            LogCoinFallRespawn("Coin fall respawn skipped. Coin wallet not found.");
            return false;
        }

        if (wallet.CurrentCoins <= 0)
        {
            LogCoinFallRespawn("Coin fall respawn skipped. Player has no coins.");
            return false;
        }

        InGameMatchManager matchManager = ResolveInGameMatchManager();
        if (matchManager == null)
        {
            if (allowRespawnWhenMatchManagerMissing)
                LogCoinFallRespawn("Coin fall respawn skipped. Match manager missing and no safe fallback exists.");

            return false;
        }

        if (!matchManager.ServerTryResolveGameSpawnPose(ownerHub, out Vector3 respawnPosition, out Quaternion respawnRotation))
        {
            LogCoinFallRespawn("Coin fall respawn skipped. Game spawn pose could not be resolved.");
            return false;
        }

        int dropAmount = wallet.ServerPreviewFallDropAmount();
        int removedAmount = dropAmount > 0 ? wallet.ServerRemoveCoins(dropAmount) : 0;
        if (removedAmount <= 0)
            LogCoinFallRespawn("Coin fall respawn continuing with no removed coins.");

        ResetStateForCoinFallRespawn();

        if (!matchManager.ServerTryRespawnPlayerToGameSpawn(ownerHub))
        {
            if (removedAmount > 0)
            {
                wallet.ServerTryAddCoins(removedAmount, out int restoredAmount);
                LogCoinFallRespawn($"Coin fall respawn failed. Restored coins:{restoredAmount}/{removedAmount}");
            }

            return false;
        }

        if (dropCoinsOnFallRespawn && removedAmount > 0)
            SpawnFallDroppedCoins(removedAmount, respawnPosition, respawnRotation);

        LogCoinFallRespawn($"Coin fall respawn succeeded. removedCoins:{removedAmount}, remainingCoins:{wallet.CurrentCoins}");
        ClearRecentCombatContributorServer();
        ResetFallContributionReportGuard();
        return true;
    }

    private bool IsCoinFallRespawnAllowedByGameState()
    {
        if (!TryGetGameStateManager(out GameStateManager manager))
            return false;

        return manager.GetState() == GameStateManager.GameState.Playing;
    }

    private PlayerHub ResolvePlayerHub()
    {
        if (playerHub == null)
            playerHub = GetComponentInParent<PlayerHub>();

        return playerHub;
    }

    private bool TryResolveInteractModule(out PlayerInteractModule interact)
    {
        if (_cachedInteractModule != null)
        {
            interact = _cachedInteractModule;
            return true;
        }

        PlayerHub ownerHub = ResolvePlayerHub();
        if (ownerHub != null)
        {
            _cachedInteractModule = ownerHub.GetComponentInChildren<PlayerInteractModule>(true);
            if (_cachedInteractModule != null)
            {
                interact = _cachedInteractModule;
                return true;
            }
        }

        _cachedInteractModule = GetComponentInParent<PlayerInteractModule>();
        if (_cachedInteractModule == null)
            _cachedInteractModule = GetComponentInChildren<PlayerInteractModule>(true);

        interact = _cachedInteractModule;
        return interact != null;
    }

    private void ServerTryDropHeldItemBecauseOfKnockback(string reason)
    {
        if (!IsServer)
            return;

        if (!dropHeldItemOnKnockback)
            return;

        if (isEliminated)
            return;

        if (Time.time < _nextDropHeldItemOnKnockbackAllowedAt)
        {
            LogDropHeldItemOnKnockback($"Skip knockback drop: cooldown active reason={reason}");
            return;
        }

        if (!TryResolveInteractModule(out PlayerInteractModule interact) || interact == null)
        {
            LogDropHeldItemOnKnockback($"Skip knockback drop: interact module missing reason={reason}");
            return;
        }

        if (!interact.HasHeldItem())
        {
            LogDropHeldItemOnKnockback($"Skip knockback drop: no held item reason={reason}");
            return;
        }

        interact.ServerTryDrop();
        _nextDropHeldItemOnKnockbackAllowedAt = Time.time + Mathf.Max(0f, dropHeldItemOnKnockbackCooldown);
        LogDropHeldItemOnKnockback($"Drop held item on knockback reason={reason}");
    }

    private InGameMatchManager ResolveInGameMatchManager()
    {
        if (inGameMatchManager == null)
            inGameMatchManager = FindFirstObjectByType<InGameMatchManager>();

        return inGameMatchManager;
    }

    private CoinSpawnManager ResolveCoinSpawnManager()
    {
        if (coinSpawnManager == null)
            coinSpawnManager = FindFirstObjectByType<CoinSpawnManager>();

        return coinSpawnManager;
    }

    private void SpawnFallDroppedCoins(int coinCount, Vector3 respawnPosition, Quaternion respawnRotation)
    {
        CoinSpawnManager spawnManager = ResolveCoinSpawnManager();
        if (spawnManager == null)
        {
            LogCoinFallRespawn("Coin drop skipped. CoinSpawnManager not found.");
            return;
        }

        int spawnedCount = preferCoinSpawnManagerRandomDrop
            ? SpawnFallDroppedCoinsAtRandomSpawns(spawnManager, coinCount)
            : SpawnFallDroppedCoinsNearRespawn(spawnManager, coinCount, respawnPosition, respawnRotation);

        if (spawnedCount < coinCount)
            LogCoinFallRespawn($"Coin drop partially failed. spawned:{spawnedCount}/{coinCount}");
    }

    private int SpawnFallDroppedCoinsAtRandomSpawns(CoinSpawnManager spawnManager, int coinCount)
    {
        int spawnedCount = 0;
        for (int i = 0; i < coinCount; i++)
        {
            if (spawnManager.ServerTrySpawnCoin())
                spawnedCount++;
        }

        return spawnedCount;
    }

    private int SpawnFallDroppedCoinsNearRespawn(CoinSpawnManager spawnManager, int coinCount, Vector3 respawnPosition, Quaternion respawnRotation)
    {
        int spawnedCount = 0;
        int attemptsPerCoin = Mathf.Max(1, maxCoinDropSpawnAttempts);
        float radius = Mathf.Max(0f, coinDropSpawnRadius);
        float heightOffset = Mathf.Max(0f, coinDropSpawnHeightOffset);

        for (int i = 0; i < coinCount; i++)
        {
            bool spawned = false;
            for (int attempt = 0; attempt < attemptsPerCoin; attempt++)
            {
                Vector2 offset = radius > 0f ? Random.insideUnitCircle * radius : Vector2.zero;
                if (radius > 0f && offset.sqrMagnitude < 0.01f)
                    offset = Random.insideUnitCircle.normalized * radius;

                Vector3 position = respawnPosition + new Vector3(offset.x, heightOffset, offset.y);
                Quaternion rotation = Quaternion.Euler(0f, respawnRotation.eulerAngles.y + Random.Range(0f, 360f), 0f);
                if (!spawnManager.ServerTrySpawnCoinAtPosition(position, rotation))
                    continue;

                spawned = true;
                spawnedCount++;
                break;
            }

            if (!spawned)
                LogCoinFallRespawn($"Coin drop spawn failed. index:{i}");
        }

        return spawnedCount;
    }

    private void ResetStateForCoinFallRespawn()
    {
        isKnocked = false;
        isStandingUp = false;
        knockTimer = 0f;
        standUpTimer = 0f;
        _standUpStartedAt = 0f;
        _standUpFinishEventReceived = false;
        _standUpMinimumDelayLogged = false;
        ClearStandUpControlLock("CoinFallRespawn");
        ClearStandUpAnimatorExitLock("CoinFallRespawn");
        ClearDeferredStandUpUprightSnap("CoinFallRespawn");
        ResetStandUpGroundClearanceState();
        ClearTemporaryControlLockLocal();
        _didSyncRootFromRagdollForCurrentKnockback = false;
        _hasLastRagdollFocusForRootSync = false;
        hasReachedSafePlayingPosition = false;
        hasLoggedEliminationGateState = false;
        ResetFallContributionReportGuard();

        if (locomotionModule != null)
            locomotionModule.ResetMotionServer();

        if (rootRigidbody != null)
        {
            if (!rootRigidbody.isKinematic)
            {
                rootRigidbody.linearVelocity = Vector3.zero;
                rootRigidbody.angularVelocity = Vector3.zero;
            }

            rootRigidbody.constraints = cachedConstraints;
            rootRigidbody.isKinematic = true;
            rootRigidbody.Sleep();
        }

        if (charController != null && !charController.enabled)
            charController.enabled = true;
    }

    private float GetEliminationCheckY(float rootY)
    {
        if (!useRagdollFocusForElimination)
            return rootY;

        SugaActiveRagdollController controller = ResolveActiveRagdollController();
        if (controller == null)
            return rootY;

        if (!controller.IsRagdollActiveForGameplay)
            return rootY;

        if (!controller.TryGetRagdollFocusPosition(out Vector3 focusPosition))
            return rootY;

        if (!IsFiniteVector(focusPosition))
            return rootY;

        StoreRagdollFocusForRootSync(focusPosition);

        float focusY = focusPosition.y + ragdollFocusEliminationYOffset;
        if (float.IsNaN(focusY) || float.IsInfinity(focusY))
            return rootY;

        return Mathf.Min(rootY, focusY);
    }

    private bool IsEliminationAllowedByGameState(Transform checkTf)
    {
        if (!TryGetGameStateManager(out GameStateManager manager))
        {
            if (!hasLoggedMissingGameStateManager)
            {
                Debug.LogWarning("[PlayerStatus] Elimination ignored. GameStateManager not found.");
                hasLoggedMissingGameStateManager = true;
            }

            hasReachedSafePlayingPosition = false;
            return false;
        }

        hasLoggedMissingGameStateManager = false;

        GameStateManager.GameState currentState = manager.GetState();
        if (currentState != GameStateManager.GameState.Playing)
        {
            hasReachedSafePlayingPosition = false;
            ClearRecentCombatContributorServer();
            ResetFallContributionReportGuard();

            if (!hasLoggedEliminationGateState || lastLoggedGameState != currentState)
            {
                Log($"[PlayerStatus] Elimination ignored. GameState is not Playing. CurrentState:{currentState}");
                lastLoggedGameState = currentState;
                hasLoggedEliminationGateState = true;
            }

            return false;
        }

        // Playing enters before the async teleport routine finishes, so arm pit elimination
        // only after the player is first observed above the elimination line in Playing.
        if (!hasReachedSafePlayingPosition && checkTf.position.y >= eliminationY)
        {
            hasReachedSafePlayingPosition = true;
            ResetFallContributionReportGuard();
            lastLoggedGameState = currentState;
            hasLoggedEliminationGateState = true;
            Log("[PlayerStatus] Elimination allowed. GameState is Playing.");
        }

        if (!hasReachedSafePlayingPosition)
        {
            if (!hasLoggedEliminationGateState || lastLoggedGameState != currentState)
            {
                Log("[PlayerStatus] Elimination ignored. GameState is Playing but spawn placement is not ready.");
                lastLoggedGameState = currentState;
                hasLoggedEliminationGateState = true;
            }

            return false;
        }

        return true;
    }

    private bool TryGetGameStateManager(out GameStateManager manager)
    {
        if (gameStateManager != null)
        {
            manager = gameStateManager;
            return true;
        }

        if (Time.unscaledTime < nextGameStateManagerLookupAt)
        {
            manager = null;
            return false;
        }

        nextGameStateManagerLookupAt = Time.unscaledTime + 0.5f;
        gameStateManager = FindFirstObjectByType<GameStateManager>();
        manager = gameStateManager;
        return manager != null;
    }

    private static bool IsFiniteVector(Vector3 value)
    {
        return !float.IsNaN(value.x) && !float.IsInfinity(value.x)
            && !float.IsNaN(value.y) && !float.IsInfinity(value.y)
            && !float.IsNaN(value.z) && !float.IsInfinity(value.z);
    }

    private void BeginKnockback(Vector3 impulse)
    {
        isKnocked = true;
        isStandingUp = false;
        knockTimer = Mathf.Max(0.01f, knockbackDuration);
        standUpTimer = 0f;
        _standUpStartedAt = 0f;
        _standUpFinishEventReceived = false;
        _standUpMinimumDelayLogged = false;
        ClearStandUpControlLock("BeginKnockback");
        ClearStandUpAnimatorExitLock("BeginKnockback");
        ClearDeferredStandUpUprightSnap("BeginKnockback");
        ResetStandUpGroundClearanceState();
        _didSyncRootFromRagdollForCurrentKnockback = false;

        if (locomotionModule != null)
            locomotionModule.ResetMotionServer();

        if (charController != null && charController.enabled)
            charController.enabled = false;

        if (rootRigidbody == null)
            return;

        rootRigidbody.isKinematic = false;
        rootRigidbody.useGravity = true;
        rootRigidbody.interpolation = RigidbodyInterpolation.Interpolate;
        rootRigidbody.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        rootRigidbody.WakeUp();
        rootRigidbody.linearVelocity = Vector3.zero;
        rootRigidbody.angularVelocity = Vector3.zero;

        rootRigidbody.constraints = allowKnockRotation
            ? RigidbodyConstraints.None
            : cachedConstraints;

        Vector3 flat = new Vector3(impulse.x, 0f, impulse.z);
        Vector3 launch = flat * horizontalLaunchScale;
        launch.y = Mathf.Max(minimumUpwardLaunch, impulse.y + upwardLaunchBonus);

        if (useVelocityChange)
            rootRigidbody.AddForce(launch, ForceMode.VelocityChange);
        else
            rootRigidbody.AddForce(launch, ForceMode.Impulse);

        if (allowKnockRotation)
        {
            Vector3 axis = flat.sqrMagnitude > 0.0001f
                ? Vector3.Cross(flat.normalized, Vector3.up)
                : rootTransform != null ? rootTransform.right : Vector3.right;

            Vector3 torque = axis * tumbleTorque + Vector3.up * Random.Range(-randomYawTorque, randomYawTorque);

            if (useVelocityChange)
                rootRigidbody.AddTorque(torque, ForceMode.VelocityChange);
            else
                rootRigidbody.AddTorque(torque, ForceMode.Impulse);

            Log($"[PlayerStatus] Knockback launch:{launch}, torque:{torque}, mode:{(useVelocityChange ? "VelocityChange" : "Impulse")}, mass:{rootRigidbody.mass}");
            return;
        }

        Log($"[PlayerStatus] Knockback launch:{launch}, mode:{(useVelocityChange ? "VelocityChange" : "Impulse")}, mass:{rootRigidbody.mass}");
    }

    private void BeginStandUpBack()
    {
        if (!IsServer) return;
        if (isStandingUp) return;
        if (isEliminated) return;

        TrySyncRootToRagdollFocusBeforeStandUp("BeginStandUpBack");

        isKnocked = false;
        isStandingUp = true;
        standUpTimer = Mathf.Max(0.2f, Mathf.Max(0f, standUpFallbackFinishSeconds));
        _standUpStartedAt = Time.time;
        _standUpFinishEventReceived = false;
        _standUpMinimumDelayLogged = false;
        ClearDeferredStandUpUprightSnap("BeginStandUpBack");
        ResetStandUpGroundClearanceState();
        ExtendStandUpControlLock(standUpMinimumControlLockSeconds, "Minimum");
        LogStandUpControlLock("StandUp started");

        if (rootRigidbody != null)
        {
            if (!rootRigidbody.isKinematic)
            {
                rootRigidbody.linearVelocity = Vector3.zero;
                rootRigidbody.angularVelocity = Vector3.zero;
            }

            rootRigidbody.constraints = cachedConstraints;
            rootRigidbody.isKinematic = true;
            rootRigidbody.Sleep();
        }

        ApplyStandUpStartRootPreparation();

        if (snapToGroundDuringStandUp)
            SnapRootToGround();

        MaintainStandUpGroundClearance("BeginStandUp");

        if (disableControllerDuringStandUp && charController != null && charController.enabled)
            charController.enabled = false;

        if (locomotionModule != null)
            locomotionModule.ResetMotionServer();

        if (animModule != null && useBackStandUp)
        {
            animModule.TriggerStandUpBack();
            return;
        }

        TryFinishStandUp("NoStandUpAnimation");
    }

    private void SnapRootUpright()
    {
        if (rootTransform == null)
            rootTransform = rootNetObj != null ? rootNetObj.transform : transform.root;
        if (rootTransform == null)
            return;

        Vector3 forward = Vector3.ProjectOnPlane(rootTransform.forward, Vector3.up);
        if (forward.sqrMagnitude < 0.0001f)
            forward = Vector3.ProjectOnPlane(rootTransform.up, Vector3.up);
        if (forward.sqrMagnitude < 0.0001f)
            forward = Vector3.forward;

        Quaternion upright = Quaternion.LookRotation(forward.normalized, Vector3.up);

        if (rootRigidbody != null)
            rootRigidbody.MoveRotation(upright);

        rootTransform.rotation = upright;
    }

    private void ApplyStandUpStartRootPreparation()
    {
        _deferredUprightSnapForCurrentStandUp = false;
        _lastStandUpPoseReason = string.Empty;

        if (!snapUprightOnStandUp)
            return;

        if (ShouldApplyImmediateUprightSnapForStandUp(out string reason))
        {
            _lastStandUpPoseReason = reason;
            SnapRootUpright();
            LogStandUpPoseSnap($"StandUp pose safe: immediate upright snap reason={reason}");
            return;
        }

        _deferredUprightSnapForCurrentStandUp = true;
        _lastStandUpPoseReason = reason;
        LogStandUpPoseSnap($"StandUp pose unsafe: defer upright snap reason={reason}");
    }

    private bool ShouldApplyImmediateUprightSnapForStandUp(out string reason)
    {
        if (!reduceStandUpRootSnap)
        {
            reason = "RootSnapReductionDisabled";
            return true;
        }

        if (!deferUprightSnapOnUnsafeStandUpPose)
        {
            reason = "UnsafePoseDeferralDisabled";
            return true;
        }

        return !IsUnsafeStandUpStartPose(out reason);
    }

    private bool IsUnsafeStandUpStartPose(out string reason)
    {
        reason = "PoseSampleUnavailable";
        if (!TryGetStandUpStartPoseSample(out Vector3 up, out Vector3 forward))
            return false;

        up.Normalize();
        forward.Normalize();

        float upDot = Vector3.Dot(up, Vector3.up);
        float forwardUpDot = Mathf.Abs(Vector3.Dot(forward, Vector3.up));
        float upThreshold = Mathf.Clamp(GetFiniteOrZero(unsafeStandUpPoseUpDotThreshold), -1f, 1f);
        float forwardThreshold = Mathf.Clamp01(GetFiniteOrZero(unsafeStandUpPoseForwardDotThreshold));

        if (upDot < upThreshold)
        {
            reason = $"RootUpDot={upDot:0.###}<threshold={upThreshold:0.###}, RootForwardUpDot={forwardUpDot:0.###}";
            return true;
        }

        if (forwardThreshold > 0f && forwardUpDot > forwardThreshold)
        {
            reason = $"RootForwardUpDot={forwardUpDot:0.###}>threshold={forwardThreshold:0.###}, RootUpDot={upDot:0.###}";
            return true;
        }

        reason = $"RootUpDot={upDot:0.###}, RootForwardUpDot={forwardUpDot:0.###}";
        return false;
    }

    private bool TryGetStandUpStartPoseSample(out Vector3 up, out Vector3 forward)
    {
        Quaternion rotation;
        if (rootRigidbody != null)
        {
            rotation = rootRigidbody.rotation;
        }
        else
        {
            if (rootTransform == null)
                rootTransform = rootNetObj != null ? rootNetObj.transform : transform.root;
            if (rootTransform == null)
            {
                up = Vector3.up;
                forward = Vector3.forward;
                return false;
            }

            rotation = rootTransform.rotation;
        }

        up = rotation * Vector3.up;
        forward = rotation * Vector3.forward;
        return IsFiniteVector(up) && up.sqrMagnitude > 0.0001f
            && IsFiniteVector(forward) && forward.sqrMagnitude > 0.0001f;
    }

    private bool ApplyDeferredStandUpUprightSnapIfNeeded()
    {
        if (!_deferredUprightSnapForCurrentStandUp)
            return false;

        _deferredUprightSnapForCurrentStandUp = false;
        string reason = _lastStandUpPoseReason;
        _lastStandUpPoseReason = string.Empty;

        if (!snapUprightOnStandUp)
            return false;

        SnapRootUpright();
        LogStandUpPoseSnap($"StandUp deferred upright snap applied reason={reason}");
        return true;
    }

    private void ClearDeferredStandUpUprightSnap(string reason)
    {
        bool hadDeferredSnapState = _deferredUprightSnapForCurrentStandUp || !string.IsNullOrEmpty(_lastStandUpPoseReason);
        _deferredUprightSnapForCurrentStandUp = false;
        _lastStandUpPoseReason = string.Empty;

        if (hadDeferredSnapState)
            LogStandUpPoseSnap($"StandUp pose snap state cleared reason={reason}");
    }

    private void SnapRootToGround()
    {
        if (!snapToGroundOnStandUpFinish && !snapToGroundDuringStandUp)
            return;

        if (rootTransform == null)
            return;

        Vector3 origin = rootTransform.position + Vector3.up * Mathf.Max(0.1f, groundProbeHeight);
        float distance = Mathf.Max(0.2f, groundProbeDistance);
        int mask = groundMask.value == 0 ? Physics.DefaultRaycastLayers : groundMask.value;

        RaycastHit[] hits = Physics.RaycastAll(origin, Vector3.down, distance, mask, QueryTriggerInteraction.Ignore);
        if (hits == null || hits.Length == 0)
            return;

        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

        for (int i = 0; i < hits.Length; i++)
        {
            RaycastHit hit = hits[i];
            if (hit.collider == null)
                continue;

            if (ignoreOwnCollidersOnGroundSnap && hit.collider.transform.root == rootTransform)
                continue;

            float bottomOffset = 0f;
            if (charController != null)
            {
                float halfHeight = Mathf.Max(charController.radius, charController.height * 0.5f);
                bottomOffset = charController.center.y - halfHeight;
            }

            float snappedY = hit.point.y - bottomOffset + groundContactOffset;
            Vector3 snappedPos = new Vector3(rootTransform.position.x, snappedY, rootTransform.position.z);

            if (rootRigidbody != null)
                rootRigidbody.position = snappedPos;

            rootTransform.position = snappedPos;
            Physics.SyncTransforms();
            return;
        }
    }

    private void ResetStandUpGroundClearanceState()
    {
        _standUpTotalGroundLiftApplied = 0f;
        _nextStandUpGroundClearanceLogAt = 0f;
    }

    private void MaintainStandUpGroundClearance(string reason)
    {
        if (!maintainGroundClearanceDuringStandUp)
            return;

        if (!IsServer || !isStandingUp)
            return;

        if (rootTransform == null)
            rootTransform = rootRigidbody != null ? rootRigidbody.transform : rootNetObj != null ? rootNetObj.transform : transform.root;
        if (rootTransform == null)
            return;

        if (!TryGetStandUpGroundPoint(out Vector3 groundPoint, out _))
        {
            LogStandUpGroundClearance("StandUp ground clearance skipped: no ground", true);
            return;
        }

        float clearance = Mathf.Max(0f, GetFiniteOrZero(standUpGroundClearance));
        float targetY = groundPoint.y + clearance;
        float currentY = rootTransform.position.y;
        float neededLift = targetY - currentY;
        if (neededLift <= 0f)
            return;

        float maxTotalLift = Mathf.Max(0f, GetFiniteOrZero(standUpMaxTotalGroundLift));
        float remainingTotalLift = maxTotalLift - _standUpTotalGroundLiftApplied;
        if (remainingTotalLift <= 0f)
        {
            LogStandUpGroundClearance("StandUp ground clearance max lift reached", true);
            return;
        }

        float maxPerTick = Mathf.Max(0f, GetFiniteOrZero(standUpMaxGroundCorrectionPerTick));
        if (maxPerTick <= 0f)
            return;

        float deltaY = Mathf.Min(neededLift, maxPerTick, remainingTotalLift);
        if (deltaY <= 0f)
            return;

        ApplyRootVerticalOffsetForStandUp(deltaY, reason);
        _standUpTotalGroundLiftApplied += deltaY;
        LogStandUpGroundClearance($"StandUp ground clearance lift delta={deltaY:0.###} total={_standUpTotalGroundLiftApplied:0.###} reason={reason}", reason == "UpdateStandUp");
    }

    private bool TryGetStandUpGroundPoint(out Vector3 point, out Vector3 normal)
    {
        point = Vector3.zero;
        normal = Vector3.up;

        if (rootTransform == null)
            rootTransform = rootRigidbody != null ? rootRigidbody.transform : rootNetObj != null ? rootNetObj.transform : transform.root;
        if (rootTransform == null)
            return false;

        float clearance = Mathf.Max(0f, GetFiniteOrZero(standUpGroundClearance));
        float probeDistance = Mathf.Max(0.1f, GetFiniteOrZero(standUpGroundProbeDistance));
        float originLift = Mathf.Max(0.2f, clearance + 0.5f);
        Vector3 origin = rootTransform.position + Vector3.up * originLift;
        float distance = originLift + probeDistance;
        int mask = groundMask.value == 0 ? Physics.DefaultRaycastLayers : groundMask.value;

        RaycastHit[] hits = Physics.RaycastAll(origin, Vector3.down, distance, mask, QueryTriggerInteraction.Ignore);
        if (hits == null || hits.Length == 0)
            return false;

        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        for (int i = 0; i < hits.Length; i++)
        {
            RaycastHit hit = hits[i];
            if (hit.collider == null)
                continue;

            if (ignoreOwnCollidersOnGroundSnap && hit.collider.transform.root == rootTransform)
                continue;

            point = hit.point;
            normal = hit.normal.sqrMagnitude > 0.0001f ? hit.normal.normalized : Vector3.up;
            return IsFiniteVector(point) && IsFiniteVector(normal);
        }

        return false;
    }

    private void ApplyRootVerticalOffsetForStandUp(float deltaY, string reason)
    {
        if (deltaY <= 0f)
            return;

        if (rootTransform == null)
            rootTransform = rootRigidbody != null ? rootRigidbody.transform : rootNetObj != null ? rootNetObj.transform : transform.root;
        if (rootTransform == null)
            return;

        bool wasControllerEnabled = charController != null && charController.enabled;
        if (wasControllerEnabled)
            charController.enabled = false;

        Vector3 targetPosition = rootTransform.position + Vector3.up * deltaY;
        if (!IsFiniteVector(targetPosition))
        {
            if (wasControllerEnabled)
                charController.enabled = true;
            return;
        }

        if (rootRigidbody != null)
            rootRigidbody.position = targetPosition;

        rootTransform.position = targetPosition;
        Physics.SyncTransforms();

        if (wasControllerEnabled)
            charController.enabled = true;
    }

    private void CacheRagdollFocusForRootSync()
    {
        TryCacheRagdollFocusForRootSync(true);
    }

    private bool TryCacheRagdollFocusForRootSync(bool requireActive)
    {
        SugaActiveRagdollController controller = ResolveActiveRagdollController();
        if (controller == null)
            return false;

        if (requireActive && !controller.IsRagdollActiveForGameplay)
            return false;

        if (!controller.TryGetRagdollFocusPosition(out Vector3 focusPosition))
            return false;

        if (!IsFiniteVector(focusPosition))
            return false;

        StoreRagdollFocusForRootSync(focusPosition);
        return true;
    }

    private void StoreRagdollFocusForRootSync(Vector3 focusPosition)
    {
        _lastRagdollFocusForRootSync = focusPosition;
        _lastRagdollFocusForRootSyncTime = Time.time;
        _hasLastRagdollFocusForRootSync = true;
    }

    private bool TryGetRecentRagdollFocusForRootSync(out Vector3 focusPosition)
    {
        if (!_hasLastRagdollFocusForRootSync || IsRagdollRootSyncFocusExpired())
            TryCacheRagdollFocusForRootSync(false);

        focusPosition = _lastRagdollFocusForRootSync;
        if (!_hasLastRagdollFocusForRootSync)
            return false;

        if (IsRagdollRootSyncFocusExpired())
            return false;

        return IsFiniteVector(focusPosition);
    }

    private bool IsRagdollRootSyncFocusExpired()
    {
        float maxAge = Mathf.Max(0f, GetFiniteOrZero(ragdollRootSyncFocusMaxAge));
        return Time.time - _lastRagdollFocusForRootSyncTime > maxAge;
    }

    private bool TrySyncRootToRagdollFocusBeforeStandUp(string reason)
    {
        if (!syncRootToRagdollFocusBeforeStandUp)
            return false;

        if (_didSyncRootFromRagdollForCurrentKnockback)
            return false;

        if (isEliminated || (!isKnocked && !isStandingUp))
            return false;

        if (!IsServer || !IsSpawned)
            return false;

        if (!IsRootSyncAllowedByGameState())
            return false;

        if (rootTransform == null)
            rootTransform = rootNetObj != null ? rootNetObj.transform : transform.root;
        if (rootTransform == null)
            return false;

        if (!TryGetRecentRagdollFocusForRootSync(out Vector3 focusPosition))
        {
            Log("[PlayerStatus] Ragdoll root sync skipped. no recent focus.");
            return false;
        }

        float focusEliminationY = focusPosition.y + ragdollFocusEliminationYOffset;
        if (focusEliminationY < eliminationY)
        {
            Log("[PlayerStatus] Ragdoll root sync skipped. focus below elimination.");
            return false;
        }

        Vector3 rootPosition = rootTransform.position;
        Vector2 rootFlat = new Vector2(rootPosition.x, rootPosition.z);
        Vector2 focusFlat = new Vector2(focusPosition.x, focusPosition.z);
        float horizontalDistance = Vector2.Distance(rootFlat, focusFlat);

        if (horizontalDistance < Mathf.Max(0f, GetFiniteOrZero(ragdollRootSyncMinDistance)))
            return false;

        float maxDistance = Mathf.Max(0f, GetFiniteOrZero(ragdollRootSyncMaxDistance));
        if (maxDistance > 0f && horizontalDistance > maxDistance)
        {
            Log($"[PlayerStatus] Ragdoll root sync skipped. distance too large. distance={horizontalDistance:0.###}, max={maxDistance:0.###}");
            return false;
        }

        bool usedGroundFallback;
        float groundY = GetRagdollRootSyncGroundY(focusPosition, out usedGroundFallback);
        if (usedGroundFallback)
            Log("[PlayerStatus] Ragdoll root sync ground fallback used.");

        Vector3 targetPosition = new Vector3(focusPosition.x, GetRootYForGroundedPosition(groundY), focusPosition.z);
        if (!IsFiniteVector(targetPosition))
            return false;

        MoveRootToPositionForRagdollSync(targetPosition);
        _didSyncRootFromRagdollForCurrentKnockback = true;

        Log($"[PlayerStatus] Ragdoll root sync applied. reason={reason}, from={rootPosition}, to={targetPosition}");
        return true;
    }

    private bool IsRootSyncAllowedByGameState()
    {
        if (!TryGetGameStateManager(out GameStateManager manager))
            return false;

        if (manager.GetState() != GameStateManager.GameState.Playing)
            return false;

        return hasReachedSafePlayingPosition;
    }

    private float GetRagdollRootSyncGroundY(Vector3 focusPosition, out bool usedFallback)
    {
        usedFallback = false;

        float probeHeight = Mathf.Max(0.1f, GetFiniteOrZero(ragdollRootSyncGroundProbeHeight));
        float probeDistance = Mathf.Max(0.2f, GetFiniteOrZero(ragdollRootSyncGroundProbeDistance));
        Vector3 origin = focusPosition + Vector3.up * probeHeight;
        float distance = probeHeight + probeDistance;
        int mask = groundMask.value == 0 ? Physics.DefaultRaycastLayers : groundMask.value;

        RaycastHit[] hits = Physics.RaycastAll(origin, Vector3.down, distance, mask, QueryTriggerInteraction.Ignore);
        if (hits != null && hits.Length > 0)
        {
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            for (int i = 0; i < hits.Length; i++)
            {
                RaycastHit hit = hits[i];
                if (hit.collider == null)
                    continue;

                if (ignoreOwnCollidersOnGroundSnap && rootTransform != null && hit.collider.transform.root == rootTransform)
                    continue;

                return hit.point.y;
            }
        }

        usedFallback = true;
        return focusPosition.y;
    }

    private float GetRootYForGroundedPosition(float groundY)
    {
        float safeGroundY = GetFiniteOrZero(groundY);
        float offset = GetFiniteOrZero(ragdollRootSyncGroundOffset);
        if (charController != null)
        {
            float halfHeight = Mathf.Max(charController.radius, charController.height * 0.5f);
            return safeGroundY - charController.center.y + halfHeight + offset;
        }

        return safeGroundY + offset;
    }

    private void MoveRootToPositionForRagdollSync(Vector3 targetPosition)
    {
        bool wasControllerEnabled = charController != null && charController.enabled;
        if (wasControllerEnabled)
            charController.enabled = false;

        if (rootRigidbody != null)
        {
            if (!rootRigidbody.isKinematic)
            {
                rootRigidbody.linearVelocity = Vector3.zero;
                rootRigidbody.angularVelocity = Vector3.zero;
            }

            rootRigidbody.position = targetPosition;
        }

        rootTransform.position = targetPosition;
        Physics.SyncTransforms();

        if (wasControllerEnabled)
            charController.enabled = true;
    }

    private static float GetFiniteOrZero(float value)
    {
        return float.IsNaN(value) || float.IsInfinity(value) ? 0f : value;
    }

    private bool IsStandUpControlLocked => Time.time < _standUpControlLockedUntil;

    private bool IsStandUpVisualControlLocked => IsStandUpControlLocked || (_waitingForStandUpAnimatorExit && !HasStandUpAnimatorExitLockExpired());

    private void ExtendStandUpControlLock(float seconds, string reason)
    {
        float finiteSeconds = Mathf.Max(0f, GetFiniteOrZero(seconds));
        if (finiteSeconds <= 0f)
            return;

        _standUpControlLockedUntil = Mathf.Max(_standUpControlLockedUntil, Time.time + finiteSeconds);
        LogStandUpControlLock($"StandUp post control lock extended seconds={finiteSeconds:0.###} reason={reason}");
        LogStandUpControlLock("StandUp control lock active");
    }

    private void ClearStandUpControlLock(string reason)
    {
        bool hadStandUpControlLock = _standUpControlLockedUntil > 0f;
        _standUpControlLockedUntil = 0f;

        if (hadStandUpControlLock)
            LogStandUpControlLock($"StandUp control lock cleared reason={reason}");
    }

    private bool IsStandUpAnimatorStillActive()
    {
        if (rootAnimator == null)
            return false;

        AnimatorStateInfo currentState = rootAnimator.GetCurrentAnimatorStateInfo(0);
        if (IsStandUpAnimatorState(currentState))
            return true;

        if (!rootAnimator.IsInTransition(0))
            return false;

        AnimatorStateInfo nextState = rootAnimator.GetNextAnimatorStateInfo(0);
        return IsStandUpAnimatorState(nextState);
    }

    private bool IsStandUpAnimatorState(AnimatorStateInfo state)
    {
        return state.shortNameHash == backStandUpStateHash || state.IsName("Base Layer.Back Stand Up") || state.IsName("Back Stand Up");
    }

    private void BeginStandUpAnimatorExitLock(string reason)
    {
        if (!standUpWaitForAnimatorExit)
            return;

        if (rootAnimator == null)
            return;

        if (!IsStandUpAnimatorStillActive())
            return;

        _waitingForStandUpAnimatorExit = true;
        _standUpAnimatorExitLockStartedAt = Time.time;
        _standUpAnimatorStillActiveLogged = false;
        LogStandUpControlLock($"StandUp animator exit lock started reason={reason}");
    }

    private void UpdateStandUpAnimatorExitLock()
    {
        if (!_waitingForStandUpAnimatorExit)
            return;

        if (HasStandUpAnimatorExitLockExpired())
        {
            LogStandUpControlLock("StandUp animator exit lock timeout");
            ClearStandUpAnimatorExitLock("Timeout");
            return;
        }

        if (IsStandUpAnimatorStillActive())
        {
            if (!_standUpAnimatorStillActiveLogged)
            {
                LogStandUpControlLock("StandUp animator still active");
                _standUpAnimatorStillActiveLogged = true;
            }

            return;
        }

        ClearStandUpAnimatorExitLock("AnimatorExit");
    }

    private void ClearStandUpAnimatorExitLock(string reason)
    {
        bool hadAnimatorExitLock =
            _waitingForStandUpAnimatorExit ||
            _standUpAnimatorExitLockStartedAt > 0f ||
            _standUpAnimatorStillActiveLogged;

        _waitingForStandUpAnimatorExit = false;
        _standUpAnimatorExitLockStartedAt = 0f;
        _standUpAnimatorStillActiveLogged = false;

        if (hadAnimatorExitLock)
            LogStandUpControlLock($"StandUp animator exit lock cleared reason={reason}");
    }

    private bool HasStandUpAnimatorExitLockExpired()
    {
        if (!_waitingForStandUpAnimatorExit)
            return false;

        return Time.time - _standUpAnimatorExitLockStartedAt >= Mathf.Max(0f, standUpAnimatorExitMaxLockSeconds);
    }

    private bool CanFinishStandUpNow(string reason)
    {
        if (!isStandingUp)
            return false;

        if (!HasStandUpMinimumControlLockElapsed())
        {
            if (!_standUpMinimumDelayLogged)
            {
                LogStandUpControlLock($"StandUp finish delayed: minimum lock reason={reason}, elapsed:{GetStandUpElapsedSeconds():0.###}");
                _standUpMinimumDelayLogged = true;
            }

            return false;
        }

        if (HasStandUpFallbackExpired())
            return true;

        return _standUpFinishEventReceived || reason == "NormalizedTime";
    }

    private void TryFinishStandUp(string reason)
    {
        if (!CanFinishStandUpNow(reason))
            return;

        LogStandUpControlLock($"StandUp finished by {reason}");
        FinishStandUpImmediate();
    }

    private float GetStandUpElapsedSeconds()
    {
        return isStandingUp ? Mathf.Max(0f, Time.time - _standUpStartedAt) : 0f;
    }

    private bool HasStandUpMinimumControlLockElapsed()
    {
        return GetStandUpElapsedSeconds() >= Mathf.Max(0f, standUpMinimumControlLockSeconds);
    }

    private bool HasStandUpFallbackExpired()
    {
        return GetStandUpElapsedSeconds() >= Mathf.Max(0f, standUpFallbackFinishSeconds);
    }

    public void AnimEvent_StandUpFinished()
    {
        if (!IsServer) return;
        if (!isStandingUp) return;

        _standUpFinishEventReceived = true;
        LogStandUpControlLock("StandUp finish event received");
        TryFinishStandUp("AnimationEvent");
    }

    public void AnimEvent_StandUpBackFinished()
    {
        AnimEvent_StandUpFinished();
    }

    private void FinishStandUpImmediate()
    {
        TrySyncRootToRagdollFocusBeforeStandUp("FinishStandUpImmediate");

        bool appliedDeferredUprightSnap = ApplyDeferredStandUpUprightSnapIfNeeded();
        if (snapUprightOnStandUp && !appliedDeferredUprightSnap)
            SnapRootUpright();
        ClearDeferredStandUpUprightSnap("FinishStandUpImmediate");

        if (snapToGroundOnStandUpFinish)
            SnapRootToGround();

        isKnocked = false;
        ExtendStandUpControlLock(standUpPostFinishControlLockSeconds, "PostFinish");
        BeginStandUpAnimatorExitLock("PostFinish");
        isStandingUp = false;
        standUpTimer = 0f;
        _standUpStartedAt = 0f;
        _standUpFinishEventReceived = false;
        _standUpMinimumDelayLogged = false;
        ResetStandUpGroundClearanceState();
        ApplyStandingPhysicsState();
        ClearRecentCombatContributorServer();
        ResetFallContributionReportGuard();
    }

    private void ApplyStandingPhysicsState()
    {
        if (locomotionModule != null)
            locomotionModule.ResetMotionServer();

        if (rootRigidbody != null)
        {
            if (!rootRigidbody.isKinematic)
            {
                rootRigidbody.linearVelocity = Vector3.zero;
                rootRigidbody.angularVelocity = Vector3.zero;
            }

            rootRigidbody.constraints = cachedConstraints;
            rootRigidbody.isKinematic = true;
            rootRigidbody.Sleep();
        }

        if (charController != null && !charController.enabled)
        {
            charController.enabled = true;
            if (postStandUpDownNudge > 0f)
                charController.Move(Vector3.down * postStandUpDownNudge);
        }
    }

    private void HandleElimination()
    {
        if (isEliminated) return;
        isEliminated = true;
        isKnocked = false;
        isStandingUp = false;
        standUpTimer = 0f;
        _standUpStartedAt = 0f;
        _standUpFinishEventReceived = false;
        _standUpMinimumDelayLogged = false;
        ClearStandUpControlLock("Elimination");
        ClearStandUpAnimatorExitLock("Elimination");
        ClearDeferredStandUpUprightSnap("Elimination");
        ResetStandUpGroundClearanceState();
        ClearTemporaryControlLockLocal();
        ClearRecentCombatContributorServer();

        Log($"[PlayerStatus] {name} eliminated.");

        if (rootRigidbody != null)
        {
            if (!rootRigidbody.isKinematic)
            {
                rootRigidbody.linearVelocity = Vector3.zero;
                rootRigidbody.angularVelocity = Vector3.zero;
            }

            rootRigidbody.constraints = cachedConstraints;
            rootRigidbody.isKinematic = true;
            rootRigidbody.Sleep();
        }

        if (charController != null && charController.enabled)
            charController.enabled = false;

        if (rootNetObj != null && rootNetObj.IsSpawned)
        {
            rootNetObj.Despawn();
            return;
        }

        if (NetworkObject != null && NetworkObject.IsSpawned)
            NetworkObject.Despawn();
    }

    public void TakeDamage(float damage)
    {
        if (!IsServer) return;
        if (isEliminated) return;

        Log($"[PlayerStatus] TakeDamage -> {name}, damage:{damage}");
        TryTriggerHitReaction();
    }

    private void Log(string message)
    {
        if (!enableDebugLogs)
            return;

        Debug.Log(message, this);
    }

    private void LogCoinFallRespawn(string message)
    {
        if (!enableCoinRespawnDebugLogs)
            return;

        Debug.Log($"[CoinFallRespawn] {message}", this);
    }

    private void LogCombatFallContribution(string message)
    {
        if (!enableCombatFallContributionDebugLogs)
            return;

        Debug.Log($"[CombatFallContribution] {message}", this);
    }

    private void LogTemporaryControlLock(string message)
    {
        if (!enableTemporaryControlLockDebugLogs)
            return;

        Debug.Log($"[TemporaryControlLock] {message}", this);
    }

    private void LogStandUpControlLock(string message)
    {
        if (!enableStandUpControlLockDebugLogs)
            return;

        Debug.Log($"[PlayerStatus] {message}", this);
    }

    private void LogStandUpPoseSnap(string message)
    {
        if (!standUpPoseSnapDebugLogs && !enableStandUpControlLockDebugLogs)
            return;

        Debug.Log($"[PlayerStatus] {message}", this);
    }

    private void LogStandUpGroundClearance(string message, bool throttle = false)
    {
        if (!standUpGroundClearanceDebugLogs && !enableDebugLogs)
            return;

        if (throttle)
        {
            if (Time.time < _nextStandUpGroundClearanceLogAt)
                return;

            _nextStandUpGroundClearanceLogAt = Time.time + 0.25f;
        }

        Debug.Log($"[PlayerStatus] {message}", this);
    }

    private void LogDropHeldItemOnKnockback(string message)
    {
        if (!enableDropHeldItemOnKnockbackDebugLogs)
            return;

        Debug.Log($"[PlayerStatus] {message}", this);
    }

    private void TryTriggerHitReaction()
    {
        if (!triggerHitOnDamage) return;
        if (animModule == null) return;
        if (isStandingUp) return;
        if (isKnocked && !triggerHitWhileKnocked) return;
        if (Time.time < nextHitReactionAt) return;

        nextHitReactionAt = Time.time + Mathf.Max(0f, hitReactionCooldown);
        animModule.TriggerHit();
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        coinDropSpawnRadius = Mathf.Max(0f, coinDropSpawnRadius);
        coinDropSpawnHeightOffset = Mathf.Max(0f, coinDropSpawnHeightOffset);
        maxCoinDropSpawnAttempts = Mathf.Max(1, maxCoinDropSpawnAttempts);
        recentCombatContributorValidSeconds = Mathf.Max(0f, recentCombatContributorValidSeconds);
        dropHeldItemOnKnockbackCooldown = Mathf.Max(0f, dropHeldItemOnKnockbackCooldown);
        standUpFinishNormalizedTime = Mathf.Clamp01(standUpFinishNormalizedTime);
        standUpMinimumControlLockSeconds = Mathf.Max(0f, standUpMinimumControlLockSeconds);
        standUpFallbackFinishSeconds = Mathf.Max(0f, standUpFallbackFinishSeconds);
        standUpPostFinishControlLockSeconds = Mathf.Max(0f, standUpPostFinishControlLockSeconds);
        standUpAnimatorExitMaxLockSeconds = Mathf.Max(0f, standUpAnimatorExitMaxLockSeconds);
        unsafeStandUpPoseUpDotThreshold = Mathf.Clamp(unsafeStandUpPoseUpDotThreshold, -1f, 1f);
        unsafeStandUpPoseForwardDotThreshold = Mathf.Clamp01(unsafeStandUpPoseForwardDotThreshold);
        standUpGroundClearance = Mathf.Max(0f, standUpGroundClearance);
        standUpGroundProbeDistance = Mathf.Max(0.1f, standUpGroundProbeDistance);
        standUpMaxGroundCorrectionPerTick = Mathf.Max(0f, standUpMaxGroundCorrectionPerTick);
        standUpMaxTotalGroundLift = Mathf.Max(0f, standUpMaxTotalGroundLift);

        ResolveRefs();
        backStandUpStateHash = Animator.StringToHash("Back Stand Up");
    }
#endif
}
