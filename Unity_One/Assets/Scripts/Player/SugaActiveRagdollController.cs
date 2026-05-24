using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

// Prototype setup:
// 1. Attach this to a duplicate/test Suga prefab root.
// 2. Register Body/Core, Head, Left/Right Arm or Wing, Left/Right Leg Rigidbody parts.
// 3. Leave Tail/Ear out so existing Boing Kit chains keep handling them.
// 4. If there is no separate target rig yet, leave targetTransform empty; the script uses the initial joint-relative pose as a fallback.
// 5. In Play Mode, use the ContextMenu entries to SetNormal, SetStunned, SetLimp, or ApplyForwardHit.
// 6. Keep this as a manual prototype component. Do not auto-write prefab, Boing Kit, or PlayerLocomotion settings from this script.
//
// Play Mode checklist:
// - SetNormal: parts should try to hold the initial or target pose without touching Tail/Ear Boing chains.
// - SetStunned: joint torque and damping should drop so CharacterJoint-only parts become loose but still bounded.
// - SetLimp: parts should behave close to passive ragdoll; upright torque is reduced to limpUprightScale.
// - ApplyForwardHit: applies impulse to hitTargetRigidbody or Body/Core, enters Hit, then recovers to Normal.
// - Keep enablePrototypeInput off while testing with existing PlayerLocomotion/CharacterController to avoid controller conflict.
public enum RagdollImpactProfile
{
    General,
    SpinDash,
    Throw,
    Gimmick
}

public class SugaActiveRagdollController : MonoBehaviour
{
    public enum RagdollState
    {
        Normal = 0,
        Hit = 1,
        Stunned = 2,
        Limp = 3
    }

    private enum RuntimeMode
    {
        Dormant = 0,
        Active = 1
    }

    [Header("State")]
    [Tooltip("현재 Active Ragdoll 상태입니다. Normal은 자세 유지, Hit/Stunned/Limp는 점점 힘이 빠지는 상태입니다.")]
    [SerializeField] private RagdollState currentState = RagdollState.Normal;

    [Tooltip("Hit 상태가 자동으로 Normal로 돌아가기까지 걸리는 시간입니다.")]
    [SerializeField] private float hitRecoverDelay = 0.35f;

    [Tooltip("피격 후 Active Ragdoll Hit 상태를 유지하는 기본 시간입니다.")]
    [SerializeField] private float hitSoftBodyDuration = 1.2f;

    [Tooltip("피격 힘 크기에 따라 Hit 상태 유지 시간을 추가로 늘리는 배율입니다.")]
    [SerializeField] private float hitSoftBodyDurationImpulseScale = 0.04f;

    [Tooltip("피격 후 Hit 상태가 유지될 수 있는 최대 시간입니다.")]
    [SerializeField] private float maxHitSoftBodyDuration = 1.6f;

    [Tooltip("Normal 상태로 돌아온 뒤 Active Ragdoll 계산을 쉬기 전까지 기다리는 시간입니다.")]
    [SerializeField] private float normalDormantDelay = 0.25f;

    [Tooltip("상태가 Normal로 돌아올 때 관절 힘이 부드럽게 회복되는 속도입니다.")]
    [SerializeField] private float normalRecoverSpeed = 7.5f;

    [Tooltip("Hit/Stunned/Limp로 약해질 때 관절 힘이 줄어드는 속도입니다.")]
    [SerializeField] private float weakenSpeed = 12f;

    [Header("State Tuning")]
    [Tooltip("Normal 상태의 기본 관절 힘, 감쇠, Rigidbody drag 설정입니다.")]
    [SerializeField] private SugaRagdollStateTuning normalTuning = new SugaRagdollStateTuning(260f, 32f, 900f, 0.15f, 4.0f);

    [Tooltip("Hit 상태의 짧은 피격 반응용 관절 힘, 감쇠, Rigidbody drag 설정입니다.")]
    [SerializeField] private SugaRagdollStateTuning hitTuning = new SugaRagdollStateTuning(85f, 14f, 360f, 0.05f, 1.2f);

    [Tooltip("Stunned 상태의 흐물흐물한 관절 힘, 감쇠, Rigidbody drag 설정입니다.")]
    [SerializeField] private SugaRagdollStateTuning stunnedTuning = new SugaRagdollStateTuning(35f, 8f, 180f, 0.02f, 0.5f);

    [Tooltip("Limp 상태의 거의 풀 렉돌에 가까운 관절 힘, 감쇠, Rigidbody drag 설정입니다.")]
    [SerializeField] private SugaRagdollStateTuning limpTuning = new SugaRagdollStateTuning(0f, 1f, 35f, 0f, 0.05f);

    [Header("Runtime Optimization")]
    [Tooltip("Normal 상태에서 Active Ragdoll 계산을 쉬게 할지 여부입니다.")]
    [SerializeField] private bool optimizeNormalAsDormant = true;

    [Tooltip("Dormant 상태에서 관리 대상 Rigidbody를 Sleep 상태로 전환할지 여부입니다.")]
    [SerializeField] private bool sleepRigidbodiesWhenDormant = true;

    [Tooltip("Dormant 상태에서 관리 대상 Rigidbody를 Kinematic으로 전환할지 여부입니다. 기존 플레이어 이동과 충돌을 줄이기 위한 옵션입니다.")]
    [SerializeField] private bool makeRigidbodiesKinematicWhenDormant = true;

    [Tooltip("Ragdoll 상태가 활성화될 때 관리 대상 Rigidbody를 깨울지 여부입니다.")]
    [SerializeField] private bool wakeRigidbodiesWhenActive = true;

    [Header("Animator Control")]
    [Tooltip("Hit, Stunned, Limp 상태에서 Animator를 잠시 비활성화해 Ragdoll 반응이 보이게 할지 여부입니다.")]
    [SerializeField] private bool disableAnimatorWhileRagdollActive = true;

    [Tooltip("Animator를 비활성화할 때 현재 Animator 상태를 유지할지 여부입니다.")]
    [SerializeField] private bool keepAnimatorStateOnDisable = true;

    [Tooltip("Normal 상태로 복귀한 뒤 Animator를 다시 켜기 전까지 기다리는 시간입니다.")]
    [SerializeField] private float animatorRestoreDelay = 0.08f;

    [Tooltip("Normal 복귀 시 관리 중인 Rigidbody 속도를 초기화해 Ragdoll 잔류 물리 움직임을 정리할지 여부입니다.")]
    [SerializeField] private bool clearRagdollVelocityOnNormal = true;

    [Tooltip("Animator 복구 직후 Animator.Update(0)를 호출해 포즈를 즉시 갱신할지 여부입니다.")]
    [SerializeField] private bool forceAnimatorUpdateOnRestore = true;

    [Tooltip("Animator 자동 탐색을 사용할지 여부입니다.")]
    [SerializeField] private bool autoFindAnimator = true;

    [Header("Renderer Culling Guard")]
    [SerializeField, Tooltip("Ragdoll 활성 중 SkinnedMeshRenderer가 culling으로 사라지지 않도록 updateWhenOffscreen을 임시로 켤지 여부입니다.")]
    private bool keepSkinnedMeshVisibleWhileRagdollActive = true;

    [SerializeField, Tooltip("SkinnedMeshRenderer를 자동 탐색할지 여부입니다.")]
    private bool autoFindSkinnedMeshRenderers = true;

    [Header("Manual Part Setup")]
    [Tooltip("Active Ragdoll로 제어할 물리 파츠입니다. Body/Core, Head, LeftArm/Wing, RightArm/Wing, LeftLeg, RightLeg 중심으로 수동 연결합니다. Tail/Ear/Boing 본은 1차 버전에서 제외하세요.")]
    [SerializeField] private SugaRagdollPart[] parts;

    [Tooltip("시작 시 Suga 마스코트형 기본 질량/drag 값을 런타임에 적용할지 여부입니다. Prefab 에셋은 수정하지 않습니다.")]
    [SerializeField] private bool applySugaMassPresetOnAwake = false;

    [Tooltip("target Transform이 비어 있을 때 시작 시점의 물리 파츠 회전을 목표 자세로 사용할지 여부입니다.")]
    [SerializeField] private bool useInitialPoseWhenTargetMissing = true;

    [Header("Core Movement")]
    [Tooltip("Body/Core Rigidbody입니다. 비워두면 PartRole이 BodyCore인 첫 번째 파츠를 사용합니다. isKinematic이면 이동/피격 impulse 테스트가 적용되지 않습니다.")]
    [SerializeField] private Rigidbody coreRigidbody;

    [Tooltip("프로토타입용 물리 이동 입력을 사용할지 여부입니다. 기존 PlayerLocomotion/CharacterController와 충돌할 수 있으므로 기본값은 꺼둡니다.")]
    [SerializeField] private bool enablePrototypeInput = false;

    [Tooltip("WASD 입력을 기준으로 할 방향 Transform입니다. 비워두면 이 컴포넌트의 Transform을 사용합니다.")]
    [SerializeField] private Transform movementReference;

    [Tooltip("Body/Core에 적용할 수평 이동 가속도입니다.")]
    [SerializeField] private float moveForce = 55f;

    [Tooltip("프로토타입 이동의 최대 수평 속도입니다.")]
    [SerializeField] private float maxMoveSpeed = 4.5f;

    [Tooltip("이동 방향으로 몸통이 살짝 기울어지는 정도입니다.")]
    [SerializeField] private float movementLean = 0.22f;

    [Tooltip("이동 방향으로 몸통이 돌아가려는 회전 힘입니다.")]
    [SerializeField] private float facingTorque = 22f;

    [Tooltip("점프 입력 시 Body/Core에 더할 위쪽 impulse입니다. 0이면 점프를 사용하지 않습니다.")]
    [SerializeField] private float jumpImpulse = 5.5f;

    [Tooltip("점프 입력 키입니다.")]
#if ENABLE_INPUT_SYSTEM
    [SerializeField] private Key jumpKey = Key.Space;
#else
    [SerializeField] private KeyCode jumpKey = KeyCode.Space;
#endif

    [Header("Core Root Anchor")]
    [SerializeField, Tooltip("Ragdoll 활성 중 core Rigidbody가 Player root 기준 위치를 따라가도록 고정해 모델과 root 분리를 줄일지 여부입니다.")]
    private bool anchorCoreToRootWhileRagdollActive = true;

    [SerializeField, Tooltip("Ragdoll 활성 중 core Rigidbody를 Kinematic으로 전환해 Player root 기준 위치에 고정할지 여부입니다.")]
    private bool makeCoreKinematicWhileRagdollActive = true;

    [SerializeField, Tooltip("Ragdoll 활성 시작 시 core Rigidbody를 root 기준 목표 위치에 즉시 맞출지 여부입니다.")]
    private bool snapCoreToRootOnRagdollActivation = true;

    [SerializeField, Tooltip("Kinematic core anchor 적용 중 core Rigidbody를 기존 Ragdoll force/torque 적용 대상에서 제외할지 여부입니다.")]
    private bool excludeCoreFromDynamicRagdollForces = true;

    [SerializeField, Tooltip("core Rigidbody가 root 기준 목표 위치를 따라가는 속도입니다.")]
    private float coreRootAnchorFollowSpeed = 20f;

    [SerializeField, Tooltip("core Rigidbody가 root 기준 목표 회전을 따라가는 속도입니다.")]
    private float coreRootAnchorRotationSpeed = 16f;

    [SerializeField, Tooltip("core Rigidbody가 root 기준 목표 위치에서 이 거리 이상 멀어지면 강하게 목표 위치 쪽으로 보정합니다. 0 이하이면 사용하지 않습니다.")]
    private float coreRootAnchorMaxDistance = 2.5f;

    [SerializeField, Tooltip("core anchor가 한 FixedUpdate에서 보정할 수 있는 최대 이동 거리입니다. 0 이하이면 제한하지 않습니다.")]
    private float coreRootAnchorMaxCorrectionPerStep = 1.0f;

    [SerializeField, Tooltip("core root anchor 적용 시 core Rigidbody 속도를 제한하거나 정리할지 여부입니다.")]
    private bool dampCoreVelocityWhileAnchored = true;

    [SerializeField, Tooltip("core root anchor 적용 시 core Rigidbody 최대 속도입니다.")]
    private float coreRootAnchorMaxVelocity = 12f;

    [Header("Upright")]
    [Tooltip("Body/Core가 넘어지지 않고 위쪽을 향하려는 보정 토크입니다.")]
    [SerializeField] private float uprightTorque = 95f;

    [Tooltip("Body/Core upright 보정의 각속도 감쇠입니다.")]
    [SerializeField] private float uprightDamping = 18f;

    [Tooltip("Limp 상태에서도 upright 보정을 유지할 비율입니다.")]
    [SerializeField] private float limpUprightScale = 0.05f;

    [Header("Hit Test")]
    [Tooltip("ContextMenu 테스트 피격 시 사용할 기본 impulse입니다.")]
    [SerializeField] private Vector3 testForwardHitImpulse = new Vector3(0f, 2.5f, -7f);

    [Tooltip("테스트 피격 impulse를 적용할 Rigidbody입니다. 비워두면 Body/Core Rigidbody를 사용합니다. 대상이 isKinematic이면 AddForce가 적용되지 않습니다.")]
    [SerializeField] private Rigidbody hitTargetRigidbody;

    [Header("Profiled Impact")]
    [SerializeField, Tooltip("상황별 active ragdoll impact profile을 사용할지 여부입니다.")]
    private bool enableProfiledRagdollImpacts = true;

    [SerializeField, Tooltip("General hit profile의 기본 target Rigidbody 이름입니다.")]
    private string ragdollGeneralTargetName = "spine";

    [SerializeField, Tooltip("SpinDash hit profile의 기본 target Rigidbody 이름입니다.")]
    private string ragdollSpinDashTargetName = "spine";

    [SerializeField, Tooltip("Throw profile의 기본 target Rigidbody 이름입니다.")]
    private string ragdollThrowTargetName = "hips";

    [SerializeField, Tooltip("Gimmick profile의 기본 target Rigidbody 이름입니다.")]
    private string ragdollGimmickTargetName = "spine";

    [SerializeField, Tooltip("General hit profile의 전방 impulse입니다.")]
    private float generalForwardImpulse = 6f;

    [SerializeField, Tooltip("General hit profile의 상방 impulse입니다.")]
    private float generalUpImpulse = 2f;

    [SerializeField, Tooltip("General hit profile의 torque impulse입니다.")]
    private float generalTorqueImpulse = 4f;

    [SerializeField, Tooltip("SpinDash hit profile의 전방 impulse입니다.")]
    private float spinDashForwardImpulse = 8f;

    [SerializeField, Tooltip("SpinDash hit profile의 상방 impulse입니다.")]
    private float spinDashUpImpulse = 2.5f;

    [SerializeField, Tooltip("SpinDash hit profile의 torque impulse입니다.")]
    private float spinDashTorqueImpulse = 5f;

    [SerializeField, Tooltip("Throw profile의 전방 impulse입니다.")]
    private float throwForwardImpulse = 12f;

    [SerializeField, Tooltip("Throw profile의 상방 impulse입니다.")]
    private float throwUpImpulse = 4f;

    [SerializeField, Tooltip("Throw profile의 torque impulse입니다.")]
    private float throwTorqueImpulse = 4f;

    [SerializeField, Tooltip("Gimmick profile의 전방 impulse입니다.")]
    private float gimmickForwardImpulse = 6f;

    [SerializeField, Tooltip("Gimmick profile의 상방 impulse입니다.")]
    private float gimmickUpImpulse = 2f;

    [SerializeField, Tooltip("Gimmick profile의 torque impulse입니다.")]
    private float gimmickTorqueImpulse = 4f;

    [Header("Demo Impact Tuning Override")]
    [SerializeField, Tooltip("6월 시연용으로 profile별 impact 값을 기존 Inspector 값 대신 데모 튜닝값으로 적용합니다.")]
    private bool enableDemoImpactTuningOverride = true;

    [SerializeField, Tooltip("Demo General hit profile의 전방 impulse입니다.")]
    private float demoGeneralForwardImpulse = 8f;

    [SerializeField, Tooltip("Demo General hit profile의 상방 impulse입니다.")]
    private float demoGeneralUpImpulse = 2.5f;

    [SerializeField, Tooltip("Demo General hit profile의 torque impulse입니다.")]
    private float demoGeneralTorqueImpulse = 6f;

    [SerializeField, Tooltip("Demo General hit profile의 temporary unlock duration입니다.")]
    private float demoGeneralUnlockDuration = 0.35f;

    [SerializeField, Tooltip("Demo SpinDash hit profile의 전방 impulse입니다.")]
    private float demoSpinDashForwardImpulse = 14f;

    [SerializeField, Tooltip("Demo SpinDash hit profile의 상방 impulse입니다.")]
    private float demoSpinDashUpImpulse = 4f;

    [SerializeField, Tooltip("Demo SpinDash hit profile의 torque impulse입니다.")]
    private float demoSpinDashTorqueImpulse = 9f;

    [SerializeField, Tooltip("Demo SpinDash hit profile의 temporary unlock duration입니다.")]
    private float demoSpinDashUnlockDuration = 0.42f;

    [SerializeField, Tooltip("Demo Throw profile의 전방 impulse입니다.")]
    private float demoThrowForwardImpulse = 18f;

    [SerializeField, Tooltip("Demo Throw profile의 상방 impulse입니다.")]
    private float demoThrowUpImpulse = 6f;

    [SerializeField, Tooltip("Demo Throw profile의 torque impulse입니다.")]
    private float demoThrowTorqueImpulse = 8f;

    [SerializeField, Tooltip("Demo Throw profile의 temporary unlock duration입니다.")]
    private float demoThrowUnlockDuration = 0.45f;

    [SerializeField, Tooltip("Demo Gimmick profile의 전방 impulse입니다.")]
    private float demoGimmickForwardImpulse = 12f;

    [SerializeField, Tooltip("Demo Gimmick profile의 상방 impulse입니다.")]
    private float demoGimmickUpImpulse = 4f;

    [SerializeField, Tooltip("Demo Gimmick profile의 torque impulse입니다.")]
    private float demoGimmickTorqueImpulse = 8f;

    [SerializeField, Tooltip("Demo Gimmick profile의 temporary unlock duration입니다.")]
    private float demoGimmickUnlockDuration = 0.42f;

    [SerializeField, Tooltip("너무 작은 impulse라도 최소한 보이는 반응을 만들기 위한 최소값입니다.")]
    private float minimumVisibleRagdollImpulse = 2f;

    [SerializeField, Tooltip("target Rigidbody가 kinematic이면 impulse 적용을 위해 짧게 non-kinematic으로 풀지 여부입니다.")]
    private bool temporaryUnlockTargetRigidbodyForImpact = true;

    [SerializeField, Tooltip("임시 non-kinematic 상태를 유지할 시간입니다.")]
    private float impactUnlockDuration = 0.35f;

    [SerializeField, Tooltip("임시 non-kinematic 상태에서 gravity를 사용할지 여부입니다.")]
    private bool impactUseGravityWhileUnlocked = true;

    [SerializeField, Tooltip("Active ragdoll impact profile 디버그 로그를 출력할지 여부입니다.")]
    private bool activeRagdollImpactDebugLogs = false;

    [Header("Debug")]
    [Tooltip("Suga Active Ragdoll 디버그 로그를 출력할지 여부입니다.")]
    [SerializeField] private bool enableDebugLogs = false;

    private float _stateBlend = 1f;
    private float _hitRecoverAt = -1f;
    private int _hitRecoveryVersion;
    private Vector2 _moveInput;
    private bool _jumpRequested;
    private SugaRagdollStateTuning _currentTuning;
    private bool _setupWarningsLogged;
    private RuntimeMode _runtimeMode = RuntimeMode.Active;
    private float _normalDormantAt = -1f;
    private Rigidbody[] _managedRigidbodies = new Rigidbody[0];
    private bool[] _managedRigidbodyOriginalKinematic = new bool[0];
    private Animator _animator;
    private bool _originalAnimatorEnabled;
    private bool _originalKeepAnimatorStateOnDisable;
    private Coroutine _restoreAnimatorCoroutine;
    private int _animatorDisableVersion;
    private bool _animatorDisabledForRagdoll;
    private bool _hasRuntimeApplyHitRequest;
    private bool _allowAnimatorDisableForCurrentActivation;
    private SkinnedMeshRenderer[] _skinnedMeshRenderers = new SkinnedMeshRenderer[0];
    private bool[] _originalUpdateWhenOffscreenValues = new bool[0];
    private bool _skinnedMeshRenderersCached;
    private bool _skinnedMeshCullingGuardEnabled;
    private Vector3 _initialCoreLocalPosition;
    private Quaternion _initialCoreLocalRotation;
    private bool _hasInitialCoreLocalPose;
    private bool _hasOriginalCoreKinematicState;
    private bool _originalCoreIsKinematic;
    private bool _coreKinematicAnchorApplied;
    private bool _loggedMissingCoreRootAnchor;
    private readonly Dictionary<string, Rigidbody> _ragdollBodyByLowerName = new Dictionary<string, Rigidbody>();
    private readonly Dictionary<Rigidbody, bool> _originalKinematicByBody = new Dictionary<Rigidbody, bool>();
    private readonly Dictionary<Rigidbody, bool> _originalUseGravityByBody = new Dictionary<Rigidbody, bool>();
    private readonly Dictionary<Rigidbody, float> _temporaryUnlockUntilByBody = new Dictionary<Rigidbody, float>();
    private readonly List<Rigidbody> _temporaryUnlockBodies = new List<Rigidbody>();
    private Rigidbody[] _ragdollImpactBodies = new Rigidbody[0];
    private Coroutine _temporaryUnlockCoroutine;
    private bool _impactBodyCacheLogged;

    public RagdollState CurrentState => currentState;

    public bool IsRagdollActiveForGameplay
    {
        get { return ShouldUseRagdollFocusForGameplay(); }
    }

    public bool TryGetRagdollFocusPosition(out Vector3 position)
    {
        if (TryGetRigidbodyFocusPosition(coreRigidbody, out position))
            return true;

        if (TryGetRigidbodyFocusPosition(hitTargetRigidbody, out position))
            return true;

        if (TryGetBodyCorePartFocusPosition(out position))
            return true;

        if (TryGetAnyPartFocusPosition(out position))
            return true;

        position = transform.position;
        return false;
    }

    private void Awake()
    {
        ResetRuntimeApplyHitAnimatorGate(false);
        CacheAnimator();
        CacheSkinnedMeshRenderers();
        ResolveCoreRigidbody();
        CacheInitialCoreLocalPose();
        CacheParts();
        CacheManagedRigidbodies();
        CacheRagdollImpactBodies();
        ApplyInitialMassPresetIfNeeded();
        ApplyTuningImmediate(GetTargetTuning());
        LogSetupWarningsOnce();
    }

    private void OnEnable()
    {
        ResetRuntimeApplyHitAnimatorGate(false);
        EnsureAnimatorCached();
        EnsureSkinnedMeshRenderersCached();
        RestoreAnimatorValues();
        _stateBlend = GetStateStrengthScale(currentState);
        _currentTuning = GetTargetTuning();
        EnsureRagdollImpactBodyCache();
        UpdateRagdollRuntimeMode(true);
        UpdateSkinnedMeshCullingGuardForState(currentState);
        UpdateAnimatorControlForState(currentState);
    }

    private void Start()
    {
        ResetRuntimeApplyHitAnimatorGate(false);
    }

    private void OnDisable()
    {
        RestoreTemporaryUnlockedImpactBodies();
        ExitCoreKinematicAnchor(true);
        RestoreSkinnedMeshCullingGuard(true);
        RestoreAnimatorToOriginalState();
        RestoreManagedRigidbodiesToOriginalState();
        _runtimeMode = RuntimeMode.Active;
    }

    private void OnDestroy()
    {
        RestoreTemporaryUnlockedImpactBodies();
        ExitCoreKinematicAnchor(true);
        RestoreSkinnedMeshCullingGuard(true);
        RestoreAnimatorToOriginalState();
        RestoreManagedRigidbodiesToOriginalState();
    }

    private void Update()
    {
        UpdateHitAutoRecover();
        ReadPrototypeInput();
    }

    private void FixedUpdate()
    {
        UpdateRagdollRuntimeMode();
        if (!ShouldRunRagdollSimulation())
            return;

        float dt = Time.fixedDeltaTime;
        if (_coreKinematicAnchorApplied)
            ApplyCoreKinematicAnchor(dt);
        else
            ApplyCoreRootAnchor(dt);
        UpdateCurrentTuning(dt);
        ApplyPartDrives();
        ApplyCoreMovement();
        ApplyUprightTorque();
    }

    [ContextMenu("Suga Active Ragdoll/Set Normal")]
    public void SetNormal()
    {
        SetState(RagdollState.Normal);
    }

    [ContextMenu("Suga Active Ragdoll/Set Stunned")]
    public void SetStunned()
    {
        SetState(RagdollState.Stunned);
    }

    [ContextMenu("Suga Active Ragdoll/Set Limp")]
    public void SetLimp()
    {
        SetState(RagdollState.Limp);
    }

    [ContextMenu("Suga Active Ragdoll/Apply Forward Hit")]
    public void ApplyForwardHit()
    {
        Vector3 impulse = transform.TransformDirection(testForwardHitImpulse);
        ApplyHit(impulse);
    }

    public void ApplyHit(Vector3 impulse)
    {
        ApplyGeneralImpact(impulse);
    }

    public bool ApplyProfiledImpact(RagdollImpactProfile profile, Vector3 sourceImpulse)
    {
        if (!IsFinite(sourceImpulse))
        {
            ImpactLog($"Impact skipped reason=invalid-impulse profile={profile} impulse={sourceImpulse}");
            return false;
        }

        RegisterRuntimeApplyHitAnimatorRequest();

        if (currentState == RagdollState.Stunned || currentState == RagdollState.Limp)
        {
            UpdateAnimatorControlForState(currentState);
            UpdateRagdollRuntimeMode();
            EnterCoreKinematicAnchor();
            bool applied = ApplyImpactImpulse(profile, sourceImpulse);
            Log($"[SUGA_ACTIVE_RAGDOLL] ApplyHit kept {currentState} state.");
            return applied;
        }

        bool restartingHitRecovery = currentState == RagdollState.Hit && _hitRecoverAt > 0f;
        SetState(RagdollState.Hit);
        ScheduleHitRecovery(sourceImpulse, restartingHitRecovery);
        bool impactApplied = ApplyImpactImpulse(profile, sourceImpulse);
        Log($"[SUGA_ACTIVE_RAGDOLL] Hit impulse:{sourceImpulse}");
        return impactApplied;
    }

    public bool ApplyGeneralImpact(Vector3 sourceImpulse)
    {
        return ApplyProfiledImpact(RagdollImpactProfile.General, sourceImpulse);
    }

    public bool ApplySpinDashImpact(Vector3 sourceImpulse)
    {
        return ApplyProfiledImpact(RagdollImpactProfile.SpinDash, sourceImpulse);
    }

    public bool ApplyThrowImpact(Vector3 sourceImpulse)
    {
        return ApplyProfiledImpact(RagdollImpactProfile.Throw, sourceImpulse);
    }

    public bool ApplyGimmickImpact(Vector3 sourceImpulse)
    {
        return ApplyProfiledImpact(RagdollImpactProfile.Gimmick, sourceImpulse);
    }

    public void SetState(RagdollState nextState)
    {
        RagdollState previousState = currentState;
        bool enteringNormalFromActiveRagdoll = nextState == RagdollState.Normal && IsActiveRagdollState(previousState);
        bool enteringActiveRagdoll = !IsActiveRagdollState(previousState) && IsActiveRagdollState(nextState);
        bool wasRuntimeActive = _runtimeMode == RuntimeMode.Active;

        currentState = nextState;

        if (nextState == RagdollState.Normal)
            ExitCoreKinematicAnchor(true);

        if (enteringActiveRagdoll && wasRuntimeActive)
        {
            PrepareCoreRootAnchorForRagdollActivation();
            EnterCoreKinematicAnchor();
        }

        UpdateSkinnedMeshCullingGuardForState(nextState);

        if (enteringNormalFromActiveRagdoll)
            PrepareNormalRecoveryBeforeAnimatorRestore();

        UpdateAnimatorControlForState(nextState);

        if (nextState == RagdollState.Normal)
            ResetRuntimeApplyHitAnimatorGate();

        if (nextState != RagdollState.Hit)
        {
            _hitRecoverAt = -1f;
            _hitRecoveryVersion++;
        }

        if (nextState == RagdollState.Normal && ShouldDormantInNormal())
            _normalDormantAt = Time.time + GetNormalDormantDelay();
        else
            _normalDormantAt = -1f;

        if (enteringNormalFromActiveRagdoll)
            Log("[SUGA_ACTIVE_RAGDOLL] Normal recovery prepared for dormant.");

        UpdateRagdollRuntimeMode();
        if (IsActiveRagdollState(nextState))
            EnterCoreKinematicAnchor();

        Log($"[SUGA_ACTIVE_RAGDOLL] State:{currentState}");
    }

    private void UpdateRagdollRuntimeMode(bool force = false)
    {
        RuntimeMode nextMode = ShouldRunRagdollSimulation() ? RuntimeMode.Active : RuntimeMode.Dormant;
        if (!force && _runtimeMode == nextMode)
            return;

        _runtimeMode = nextMode;
        if (_runtimeMode == RuntimeMode.Active)
            ActivateRagdollRuntime();
        else
            EnterRagdollDormant();
    }

    private bool ShouldRunRagdollSimulation()
    {
        if (enablePrototypeInput)
            return true;

        if (IsActiveRagdollState(currentState))
            return true;

        if (currentState != RagdollState.Normal)
            return false;

        if (!optimizeNormalAsDormant)
            return true;

        return _normalDormantAt > 0f && Time.time < _normalDormantAt;
    }

    private bool ShouldUseRagdollFocusForGameplay()
    {
        return IsActiveRagdollState(currentState) || _runtimeMode == RuntimeMode.Active;
    }

    private bool ShouldDormantInNormal()
    {
        return optimizeNormalAsDormant && !enablePrototypeInput;
    }

    private float GetNormalDormantDelay()
    {
        float delay = Mathf.Max(0f, normalDormantDelay);
        if (disableAnimatorWhileRagdollActive)
            delay = Mathf.Max(delay, Mathf.Max(0f, animatorRestoreDelay));

        return delay;
    }

    private static bool IsActiveRagdollState(RagdollState state)
    {
        return state == RagdollState.Hit || state == RagdollState.Stunned || state == RagdollState.Limp;
    }

    private void UpdateAnimatorControlForState(RagdollState state)
    {
        if (IsActiveRagdollState(state))
        {
            DisableAnimatorForRagdoll();
            return;
        }

        if (state == RagdollState.Normal)
            ScheduleAnimatorRestore();
    }

    private void CacheAnimator()
    {
        if (!autoFindAnimator)
            return;

        Animator animator = GetComponent<Animator>();
        if (animator == null)
            animator = GetComponentInChildren<Animator>();

        if (animator == null || _animator == animator)
            return;

        _animator = animator;
        _originalAnimatorEnabled = _animator.enabled;
        _originalKeepAnimatorStateOnDisable = _animator.keepAnimatorStateOnDisable;
    }

    private void EnsureAnimatorCached()
    {
        if (_animator == null)
            CacheAnimator();
    }

    private void CacheSkinnedMeshRenderers()
    {
        if (_skinnedMeshRenderersCached)
            return;

        _skinnedMeshRenderersCached = true;

        if (!autoFindSkinnedMeshRenderers)
        {
            _skinnedMeshRenderers = new SkinnedMeshRenderer[0];
            _originalUpdateWhenOffscreenValues = new bool[0];
            return;
        }

        _skinnedMeshRenderers = GetComponentsInChildren<SkinnedMeshRenderer>(true);
        if (_skinnedMeshRenderers == null)
            _skinnedMeshRenderers = new SkinnedMeshRenderer[0];

        _originalUpdateWhenOffscreenValues = new bool[_skinnedMeshRenderers.Length];
        for (int i = 0; i < _skinnedMeshRenderers.Length; i++)
        {
            SkinnedMeshRenderer renderer = _skinnedMeshRenderers[i];
            _originalUpdateWhenOffscreenValues[i] = renderer != null && renderer.updateWhenOffscreen;
        }
    }

    private void EnsureSkinnedMeshRenderersCached()
    {
        if (!_skinnedMeshRenderersCached)
            CacheSkinnedMeshRenderers();
    }

    private void UpdateSkinnedMeshCullingGuardForState(RagdollState state)
    {
        if (IsActiveRagdollState(state))
        {
            EnableSkinnedMeshCullingGuard();
            return;
        }

        if (state == RagdollState.Normal)
            RestoreSkinnedMeshCullingGuard(false);
    }

    private void EnableSkinnedMeshCullingGuard()
    {
        if (!keepSkinnedMeshVisibleWhileRagdollActive)
            return;

        EnsureSkinnedMeshRenderersCached();
        if (_skinnedMeshRenderers.Length == 0)
            return;

        int count = Math.Min(_skinnedMeshRenderers.Length, _originalUpdateWhenOffscreenValues.Length);
        bool hasValidRenderer = false;
        for (int i = 0; i < count; i++)
        {
            SkinnedMeshRenderer renderer = _skinnedMeshRenderers[i];
            if (renderer == null)
                continue;

            hasValidRenderer = true;
            renderer.updateWhenOffscreen = true;
        }

        if (!hasValidRenderer || _skinnedMeshCullingGuardEnabled)
            return;

        _skinnedMeshCullingGuardEnabled = true;
        Log("[SUGA_ACTIVE_RAGDOLL] Skinned mesh culling guard enabled.");
    }

    private void RestoreSkinnedMeshCullingGuard(bool force)
    {
        if (!_skinnedMeshCullingGuardEnabled)
            return;

        if (!force && IsActiveRagdollState(currentState))
            return;

        int count = Math.Min(_skinnedMeshRenderers.Length, _originalUpdateWhenOffscreenValues.Length);
        for (int i = 0; i < count; i++)
        {
            SkinnedMeshRenderer renderer = _skinnedMeshRenderers[i];
            if (renderer != null)
                renderer.updateWhenOffscreen = _originalUpdateWhenOffscreenValues[i];
        }

        _skinnedMeshCullingGuardEnabled = false;
        Log("[SUGA_ACTIVE_RAGDOLL] Skinned mesh culling guard restored.");
    }

    private void DisableAnimatorForRagdoll()
    {
        if (!disableAnimatorWhileRagdollActive)
            return;

        if (!CanDisableAnimatorForRagdoll())
        {
            Log("[SUGA_ACTIVE_RAGDOLL] Animator disable skipped. No runtime ApplyHit request.");
            return;
        }

        _animatorDisableVersion++;
        CancelAnimatorRestore();

        EnsureAnimatorCached();
        if (_animator == null)
            return;

        if (keepAnimatorStateOnDisable)
            _animator.keepAnimatorStateOnDisable = true;

        _animatorDisabledForRagdoll = true;

        if (!_animator.enabled)
            return;

        _animator.enabled = false;
        Log("[SUGA_ACTIVE_RAGDOLL] Animator disabled for ragdoll.");
    }

    private void ScheduleAnimatorRestore()
    {
        _animatorDisableVersion++;
        int restoreVersion = _animatorDisableVersion;
        CancelAnimatorRestore();

        EnsureAnimatorCached();
        if (_animator == null)
            return;

        if (!_animatorDisabledForRagdoll)
            return;

        if (!disableAnimatorWhileRagdollActive)
        {
            RestoreAnimatorIfSafe(restoreVersion);
            return;
        }

        float delay = Mathf.Max(0f, animatorRestoreDelay);
        if (!isActiveAndEnabled || delay <= 0f)
        {
            RestoreAnimatorIfSafe(restoreVersion);
            return;
        }

        _restoreAnimatorCoroutine = StartCoroutine(RestoreAnimatorAfterDelay(restoreVersion, delay));
        Log("[SUGA_ACTIVE_RAGDOLL] Animator restore scheduled.");
    }

    private IEnumerator RestoreAnimatorAfterDelay(int restoreVersion, float delay)
    {
        yield return new WaitForSeconds(delay);
        _restoreAnimatorCoroutine = null;
        RestoreAnimatorIfSafe(restoreVersion);
    }

    private void RestoreAnimatorIfSafe(int restoreVersion)
    {
        if (restoreVersion != _animatorDisableVersion)
            return;

        if (currentState != RagdollState.Normal)
        {
            Log("[SUGA_ACTIVE_RAGDOLL] Animator restore skipped because ragdoll is active.");
            return;
        }

        RestoreAnimatorValues();
        if (ForceAnimatorPoseUpdateOnRestore())
            Log("[SUGA_ACTIVE_RAGDOLL] Animator restored and pose updated.");
        else
            Log("[SUGA_ACTIVE_RAGDOLL] Animator restored.");
    }

    private void RestoreAnimatorToOriginalState()
    {
        ResetRuntimeApplyHitAnimatorGate();
        _animatorDisableVersion++;
        CancelAnimatorRestore();
        RestoreAnimatorValues();
    }

    private void RestoreAnimatorValues()
    {
        if (_animator == null)
        {
            _animatorDisabledForRagdoll = false;
            return;
        }

        _animator.enabled = _originalAnimatorEnabled;
        _animator.keepAnimatorStateOnDisable = _originalKeepAnimatorStateOnDisable;
        _animatorDisabledForRagdoll = false;
    }

    private bool ForceAnimatorPoseUpdateOnRestore()
    {
        if (!forceAnimatorUpdateOnRestore || _animator == null || !_animator.isActiveAndEnabled)
            return false;

        _animator.Update(0f);
        return true;
    }

    private void CancelAnimatorRestore()
    {
        if (_restoreAnimatorCoroutine == null)
            return;

        StopCoroutine(_restoreAnimatorCoroutine);
        _restoreAnimatorCoroutine = null;
    }

    private void RegisterRuntimeApplyHitAnimatorRequest()
    {
        _hasRuntimeApplyHitRequest = true;
        _allowAnimatorDisableForCurrentActivation = true;
        Log("[SUGA_ACTIVE_RAGDOLL] Animator disable allowed by ApplyHit.");
    }

    private void ResetRuntimeApplyHitAnimatorGate(bool logReset = true)
    {
        bool hadRuntimeGate = _hasRuntimeApplyHitRequest || _allowAnimatorDisableForCurrentActivation;
        _hasRuntimeApplyHitRequest = false;
        _allowAnimatorDisableForCurrentActivation = false;

        if (logReset && hadRuntimeGate)
            Log("[SUGA_ACTIVE_RAGDOLL] Runtime ApplyHit animator gate reset.");
    }

    private bool CanDisableAnimatorForRagdoll()
    {
        return _hasRuntimeApplyHitRequest && _allowAnimatorDisableForCurrentActivation;
    }

    private void PrepareNormalRecoveryBeforeAnimatorRestore()
    {
        if (!clearRagdollVelocityOnNormal)
            return;

        ClearManagedRigidbodyVelocities();
        Log("[SUGA_ACTIVE_RAGDOLL] Clearing ragdoll velocities before animator restore.");
    }

    private void ClearManagedRigidbodyVelocities()
    {
        for (int i = 0; i < _managedRigidbodies.Length; i++)
            TryClearRigidbodyVelocity(_managedRigidbodies[i]);
    }

    private void ActivateRagdollRuntime()
    {
        RestoreManagedRigidbodyKinematicStates();

        if (IsActiveRagdollState(currentState))
        {
            PrepareCoreRootAnchorForRagdollActivation();
            EnterCoreKinematicAnchor();
        }

        SanitizeManagedRigidbodyVelocities();

        if (wakeRigidbodiesWhenActive)
        {
            for (int i = 0; i < _managedRigidbodies.Length; i++)
            {
                Rigidbody rb = _managedRigidbodies[i];
                if (rb != null && !rb.isKinematic)
                    rb.WakeUp();
            }

            Log("[SUGA_ACTIVE_RAGDOLL] Rigidbody wake.");
        }

        Log("[SUGA_ACTIVE_RAGDOLL] Runtime active.");
    }

    private void EnterRagdollDormant()
    {
        ExitCoreKinematicAnchor(true);

        if (currentState == RagdollState.Normal)
            ApplyTuningImmediate(normalTuning);

        SanitizeManagedRigidbodyVelocities();

        if (sleepRigidbodiesWhenDormant)
        {
            for (int i = 0; i < _managedRigidbodies.Length; i++)
            {
                Rigidbody rb = _managedRigidbodies[i];
                if (rb != null && !rb.isKinematic)
                    rb.Sleep();
            }

            Log("[SUGA_ACTIVE_RAGDOLL] Rigidbody sleep.");
        }

        if (makeRigidbodiesKinematicWhenDormant)
        {
            for (int i = 0; i < _managedRigidbodies.Length; i++)
            {
                Rigidbody rb = _managedRigidbodies[i];
                if (rb != null && !rb.isKinematic)
                    rb.isKinematic = true;
            }
        }

        Log("[SUGA_ACTIVE_RAGDOLL] Runtime dormant.");
    }

    private void RestoreManagedRigidbodyKinematicStates()
    {
        int count = Math.Min(_managedRigidbodies.Length, _managedRigidbodyOriginalKinematic.Length);
        for (int i = 0; i < count; i++)
        {
            Rigidbody rb = _managedRigidbodies[i];
            if (rb != null)
                rb.isKinematic = _managedRigidbodyOriginalKinematic[i];
        }
    }

    private void RestoreManagedRigidbodiesToOriginalState()
    {
        int count = Math.Min(_managedRigidbodies.Length, _managedRigidbodyOriginalKinematic.Length);
        for (int i = 0; i < count; i++)
        {
            Rigidbody rb = _managedRigidbodies[i];
            if (rb == null)
                continue;

            rb.isKinematic = _managedRigidbodyOriginalKinematic[i];
            SanitizeRigidbodyVelocity(rb);
        }
    }

    private void SanitizeManagedRigidbodyVelocities()
    {
        for (int i = 0; i < _managedRigidbodies.Length; i++)
            SanitizeRigidbodyVelocity(_managedRigidbodies[i]);
    }

    private void UpdateHitAutoRecover()
    {
        if (currentState != RagdollState.Hit || _hitRecoverAt <= 0f)
            return;

        if (Time.time < _hitRecoverAt)
            return;

        RecoverHitIfCurrent(_hitRecoveryVersion);
    }

    private void ScheduleHitRecovery(Vector3 impulse, bool restartingHitRecovery)
    {
        float duration = CalculateHitSoftBodyDuration(impulse);
        _hitRecoveryVersion++;
        _hitRecoverAt = Time.time + duration;

        Log($"[SUGA_ACTIVE_RAGDOLL] Hit soft body duration:{duration:0.###}");
        if (restartingHitRecovery)
            Log("[SUGA_ACTIVE_RAGDOLL] Hit recovery restarted.");
    }

    private void RecoverHitIfCurrent(int recoveryVersion)
    {
        if (recoveryVersion != _hitRecoveryVersion || currentState != RagdollState.Hit)
        {
            Log("[SUGA_ACTIVE_RAGDOLL] Hit recovery ignored because newer hit exists.");
            return;
        }

        SetNormal();
        Log("[SUGA_ACTIVE_RAGDOLL] Hit recovered to Normal.");
    }

    private float CalculateHitSoftBodyDuration(Vector3 impulse)
    {
        float baseDuration = Mathf.Max(0.01f, Mathf.Max(hitRecoverDelay, hitSoftBodyDuration));
        float maxDuration = Mathf.Max(baseDuration, maxHitSoftBodyDuration);
        float scaledDuration = baseDuration + impulse.magnitude * Mathf.Max(0f, hitSoftBodyDurationImpulseScale);
        return Mathf.Clamp(scaledDuration, baseDuration, maxDuration);
    }

    private void ApplyHitImpulse(Vector3 impulse)
    {
        ApplyImpactImpulse(RagdollImpactProfile.General, impulse);
    }

    private bool ApplyImpactImpulse(RagdollImpactProfile profile, Vector3 sourceImpulse)
    {
        if (!enableProfiledRagdollImpacts)
            return ApplyLegacyHitImpulse(sourceImpulse);

        RagdollImpactSettings settings = GetImpactSettings(profile);
        Rigidbody target = ResolveImpactTarget(settings.targetName);
        if (target == null)
        {
            ImpactLog($"Impact skipped reason=no-target profile={profile}");
            return false;
        }

        Vector3 direction = ResolveImpactDirection(sourceImpulse);
        Vector3 impulse = BuildProfiledImpulse(settings, direction, sourceImpulse);
        Vector3 torque = transform.right * Mathf.Max(0f, settings.torqueImpulse);
        if (!IsFinite(impulse) || !IsFinite(torque))
        {
            ImpactLog($"Impact skipped reason=invalid-result profile={profile} target={target.name}");
            return false;
        }

        float unlockDuration = ResolveImpactUnlockDuration(settings.unlockDuration);
        if (!EnsureRigidbodyCanReceiveImpact(target, unlockDuration))
        {
            ImpactLog($"Impact skipped reason=kinematic target={target.name} profile={profile}");
            return false;
        }

        target.WakeUp();
        target.AddForce(impulse, ForceMode.Impulse);

        if (torque.sqrMagnitude > 0.0001f)
            target.AddTorque(torque, ForceMode.Impulse);

        ImpactLog($"Impact profile={profile} target={target.name} impulse={impulse} torque={torque} demoOverride={settings.demoOverride} unlock={unlockDuration:0.###}");
        return true;
    }

    private bool ApplyLegacyHitImpulse(Vector3 impulse)
    {
        Rigidbody target = hitTargetRigidbody != null ? hitTargetRigidbody : coreRigidbody;
        if (target == null)
        {
            Warn("ApplyHit 대상 Rigidbody가 없습니다. hitTargetRigidbody 또는 Body/Core Rigidbody를 연결하세요.");
            return false;
        }
        else if (ShouldSkipDynamicRigidbody(target))
        {
            Log("[SUGA_ACTIVE_RAGDOLL] ApplyHit impulse skipped for kinematic core anchor.");
            return false;
        }
        else if (target.isKinematic)
        {
            Warn($"ApplyHit 대상 Rigidbody '{target.name}'가 isKinematic=true라 impulse가 적용되지 않습니다.");
            return false;
        }
        else
        {
            target.WakeUp();
            target.AddForce(impulse, ForceMode.Impulse);
            return true;
        }
    }

    private RagdollImpactSettings GetImpactSettings(RagdollImpactProfile profile)
    {
        if (enableDemoImpactTuningOverride)
            return GetDemoImpactSettings(profile);

        switch (profile)
        {
            case RagdollImpactProfile.SpinDash:
                return new RagdollImpactSettings(
                    ragdollSpinDashTargetName,
                    spinDashForwardImpulse,
                    spinDashUpImpulse,
                    spinDashTorqueImpulse,
                    impactUnlockDuration,
                    false);
            case RagdollImpactProfile.Throw:
                return new RagdollImpactSettings(
                    ragdollThrowTargetName,
                    throwForwardImpulse,
                    throwUpImpulse,
                    throwTorqueImpulse,
                    impactUnlockDuration,
                    false);
            case RagdollImpactProfile.Gimmick:
                return new RagdollImpactSettings(
                    ragdollGimmickTargetName,
                    gimmickForwardImpulse,
                    gimmickUpImpulse,
                    gimmickTorqueImpulse,
                    impactUnlockDuration,
                    false);
            default:
                return new RagdollImpactSettings(
                    ragdollGeneralTargetName,
                    generalForwardImpulse,
                    generalUpImpulse,
                    generalTorqueImpulse,
                    impactUnlockDuration,
                    false);
        }
    }

    private RagdollImpactSettings GetDemoImpactSettings(RagdollImpactProfile profile)
    {
        switch (profile)
        {
            case RagdollImpactProfile.SpinDash:
                return new RagdollImpactSettings(
                    ragdollSpinDashTargetName,
                    demoSpinDashForwardImpulse,
                    demoSpinDashUpImpulse,
                    demoSpinDashTorqueImpulse,
                    demoSpinDashUnlockDuration,
                    true);
            case RagdollImpactProfile.Throw:
                return new RagdollImpactSettings(
                    ragdollThrowTargetName,
                    demoThrowForwardImpulse,
                    demoThrowUpImpulse,
                    demoThrowTorqueImpulse,
                    demoThrowUnlockDuration,
                    true);
            case RagdollImpactProfile.Gimmick:
                return new RagdollImpactSettings(
                    ragdollGimmickTargetName,
                    demoGimmickForwardImpulse,
                    demoGimmickUpImpulse,
                    demoGimmickTorqueImpulse,
                    demoGimmickUnlockDuration,
                    true);
            default:
                return new RagdollImpactSettings(
                    ragdollGeneralTargetName,
                    demoGeneralForwardImpulse,
                    demoGeneralUpImpulse,
                    demoGeneralTorqueImpulse,
                    demoGeneralUnlockDuration,
                    true);
        }
    }

    private Vector3 ResolveImpactDirection(Vector3 sourceImpulse)
    {
        Vector3 direction = sourceImpulse;
        direction.y = 0f;

        if (!IsFinite(direction) || direction.sqrMagnitude <= 0.0001f)
        {
            direction = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
            if (!IsFinite(direction) || direction.sqrMagnitude <= 0.0001f)
                direction = Vector3.forward;
        }

        return direction.normalized;
    }

    private Vector3 BuildProfiledImpulse(RagdollImpactSettings settings, Vector3 direction, Vector3 sourceImpulse)
    {
        float forward = Mathf.Max(0f, settings.forwardImpulse);
        float up = Mathf.Max(0f, settings.upImpulse);
        float minimum = Mathf.Max(0f, minimumVisibleRagdollImpulse);

        Vector3 impulse = direction * forward + Vector3.up * up;
        if (minimum > 0f && sourceImpulse.magnitude < minimum && impulse.magnitude < minimum)
            impulse = direction * minimum;

        return impulse;
    }

    private float ResolveImpactUnlockDuration(float profileUnlockDuration)
    {
        float fallback = IsFinite(impactUnlockDuration) && impactUnlockDuration > 0f
            ? Mathf.Max(0.02f, impactUnlockDuration)
            : 0.02f;
        if (!IsFinite(profileUnlockDuration) || profileUnlockDuration <= 0f)
            return fallback;

        return Mathf.Max(0.02f, profileUnlockDuration);
    }

    private bool EnsureRigidbodyCanReceiveImpact(Rigidbody target, float unlockDuration)
    {
        if (target == null)
            return false;

        float duration = ResolveImpactUnlockDuration(unlockDuration);
        if (_temporaryUnlockUntilByBody.ContainsKey(target))
        {
            _temporaryUnlockUntilByBody[target] = Time.time + duration;
            if (target.isKinematic)
                target.isKinematic = false;
            target.useGravity = impactUseGravityWhileUnlocked;
            EnsureTemporaryUnlockCoroutine();
            return true;
        }

        if (!target.isKinematic)
            return true;

        if (!temporaryUnlockTargetRigidbodyForImpact)
            return false;

        _originalKinematicByBody[target] = target.isKinematic;
        _originalUseGravityByBody[target] = target.useGravity;
        if (!_temporaryUnlockBodies.Contains(target))
            _temporaryUnlockBodies.Add(target);

        target.isKinematic = false;
        target.useGravity = impactUseGravityWhileUnlocked;
        _temporaryUnlockUntilByBody[target] = Time.time + duration;
        EnsureTemporaryUnlockCoroutine();

        ImpactLog($"Temporarily unlocked target={target.name} duration={duration:0.###}");
        return true;
    }

    private void EnsureTemporaryUnlockCoroutine()
    {
        if (_temporaryUnlockCoroutine == null && isActiveAndEnabled)
            _temporaryUnlockCoroutine = StartCoroutine(RestoreTemporaryUnlocksWhenExpired());
    }

    private IEnumerator RestoreTemporaryUnlocksWhenExpired()
    {
        while (_temporaryUnlockUntilByBody.Count > 0)
        {
            float now = Time.time;
            for (int i = _temporaryUnlockBodies.Count - 1; i >= 0; i--)
            {
                Rigidbody rb = _temporaryUnlockBodies[i];
                if (rb == null)
                {
                    RemoveTemporaryUnlockBodyAt(i);
                    continue;
                }

                if (!_temporaryUnlockUntilByBody.TryGetValue(rb, out float restoreAt))
                {
                    RemoveTemporaryUnlockBodyAt(i);
                    continue;
                }

                if (now >= restoreAt)
                    RestoreTemporaryUnlockedBody(rb);
            }

            yield return null;
        }

        _temporaryUnlockCoroutine = null;
    }

    private void RestoreTemporaryUnlockedImpactBodies()
    {
        if (_temporaryUnlockCoroutine != null)
        {
            StopCoroutine(_temporaryUnlockCoroutine);
            _temporaryUnlockCoroutine = null;
        }

        for (int i = _temporaryUnlockBodies.Count - 1; i >= 0; i--)
        {
            Rigidbody rb = _temporaryUnlockBodies[i];
            if (rb != null)
                RestoreTemporaryUnlockedBody(rb);
            else
                RemoveTemporaryUnlockBodyAt(i);
        }
    }

    private void RestoreTemporaryUnlockedBody(Rigidbody rb)
    {
        if (rb == null)
            return;

        TryClearRigidbodyVelocity(rb);

        bool originalKinematic;
        if (_originalKinematicByBody.TryGetValue(rb, out originalKinematic))
            rb.isKinematic = originalKinematic;

        bool originalUseGravity;
        if (_originalUseGravityByBody.TryGetValue(rb, out originalUseGravity))
            rb.useGravity = originalUseGravity;

        int index = _temporaryUnlockBodies.IndexOf(rb);
        if (index >= 0)
            _temporaryUnlockBodies.RemoveAt(index);

        _temporaryUnlockUntilByBody.Remove(rb);
        _originalKinematicByBody.Remove(rb);
        _originalUseGravityByBody.Remove(rb);

        ImpactLog($"Restored target={rb.name} kinematic={rb.isKinematic} useGravity={rb.useGravity}");
    }

    private void RemoveTemporaryUnlockBodyAt(int index)
    {
        if (index < 0 || index >= _temporaryUnlockBodies.Count)
            return;

        Rigidbody rb = _temporaryUnlockBodies[index];
        _temporaryUnlockBodies.RemoveAt(index);
        if (rb == null)
            return;

        _temporaryUnlockUntilByBody.Remove(rb);
        _originalKinematicByBody.Remove(rb);
        _originalUseGravityByBody.Remove(rb);
    }

    private void ReadPrototypeInput()
    {
        _moveInput = Vector2.zero;
        _jumpRequested = false;

        if (!enablePrototypeInput)
            return;

#if ENABLE_INPUT_SYSTEM
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null)
            return;

        if (keyboard.aKey.isPressed) _moveInput.x -= 1f;
        if (keyboard.dKey.isPressed) _moveInput.x += 1f;
        if (keyboard.sKey.isPressed) _moveInput.y -= 1f;
        if (keyboard.wKey.isPressed) _moveInput.y += 1f;

        if (_moveInput.sqrMagnitude > 1f)
            _moveInput.Normalize();

        _jumpRequested = jumpImpulse > 0f && WasJumpPressedThisFrame(keyboard);
#else
        if (Input.GetKey(KeyCode.A)) _moveInput.x -= 1f;
        if (Input.GetKey(KeyCode.D)) _moveInput.x += 1f;
        if (Input.GetKey(KeyCode.S)) _moveInput.y -= 1f;
        if (Input.GetKey(KeyCode.W)) _moveInput.y += 1f;

        if (_moveInput.sqrMagnitude > 1f)
            _moveInput.Normalize();

        _jumpRequested = jumpImpulse > 0f && Input.GetKeyDown(jumpKey);
#endif
    }

    private void UpdateCurrentTuning(float dt)
    {
        SugaRagdollStateTuning target = GetTargetTuning();
        float targetBlend = GetStateStrengthScale(currentState);
        float speed = targetBlend >= _stateBlend ? normalRecoverSpeed : weakenSpeed;
        float lerp = 1f - Mathf.Exp(-Mathf.Max(0.01f, speed) * dt);

        _stateBlend = Mathf.Lerp(_stateBlend, targetBlend, lerp);
        _currentTuning = SugaRagdollStateTuning.Lerp(_currentTuning, target, lerp);
    }

    private void ApplyTuningImmediate(SugaRagdollStateTuning tuning)
    {
        _currentTuning = tuning;
        _stateBlend = GetStateStrengthScale(currentState);
        ApplyRigidbodyDamping(tuning);
        ApplyConfigurableJointDrives(tuning);
    }

    private void ApplyPartDrives()
    {
        if (parts == null)
            return;

        ApplyRigidbodyDamping(_currentTuning);
        ApplyConfigurableJointDrives(_currentTuning);

        for (int i = 0; i < parts.Length; i++)
        {
            SugaRagdollPart part = parts[i];
            if (part == null || !part.IsValid)
                continue;

            if (ShouldSkipDynamicRagdollPart(part))
                continue;

            if (!part.followTargetRotation)
                continue;

            Quaternion targetRotation = ResolveTargetRotation(part);
            ApplyRotationTorque(part, targetRotation, _currentTuning);
            ApplyConfigurableJointTarget(part, targetRotation);
        }
    }

    private void ApplyRotationTorque(SugaRagdollPart part, Quaternion targetRotation, SugaRagdollStateTuning tuning)
    {
        Rigidbody rb = part.rigidbody;
        if (rb == null || rb.isKinematic)
            return;

        if (ShouldSkipDynamicRigidbody(rb))
            return;

        Quaternion delta = targetRotation * Quaternion.Inverse(rb.rotation);
        delta.ToAngleAxis(out float angleDegrees, out Vector3 axis);

        if (!IsFinite(axis) || float.IsNaN(angleDegrees) || float.IsInfinity(angleDegrees) || axis.sqrMagnitude < 0.0001f)
            return;

        if (angleDegrees > 180f)
            angleDegrees -= 360f;

        float strength = tuning.jointStrength * Mathf.Max(0f, part.strengthMultiplier);
        float damping = tuning.jointDamping * Mathf.Max(0f, part.dampingMultiplier);
        Vector3 correctiveTorque = axis.normalized * (angleDegrees * Mathf.Deg2Rad * strength);
        Vector3 dampingTorque = -rb.angularVelocity * damping;
        Vector3 torque = Vector3.ClampMagnitude(correctiveTorque + dampingTorque, Mathf.Max(0f, tuning.maxTorque));

        if (IsFinite(torque))
            rb.AddTorque(torque, ForceMode.Acceleration);
    }

    private void ApplyCoreMovement()
    {
        if (!enablePrototypeInput || coreRigidbody == null || coreRigidbody.isKinematic || ShouldSkipDynamicRigidbody(coreRigidbody))
            return;

        if (currentState == RagdollState.Limp)
            return;

        Vector3 moveDirection = GetMoveDirection();
        Vector3 planarVelocity = GetLinearVelocity(coreRigidbody);
        planarVelocity.y = 0f;

        if (moveDirection.sqrMagnitude > 0.0001f && planarVelocity.magnitude < Mathf.Max(0f, maxMoveSpeed))
            coreRigidbody.AddForce(moveDirection * moveForce, ForceMode.Acceleration);

        if (_jumpRequested)
        {
            coreRigidbody.AddForce(Vector3.up * jumpImpulse, ForceMode.Impulse);
            _jumpRequested = false;
        }

        if (moveDirection.sqrMagnitude > 0.0001f)
        {
            Vector3 currentForward = Vector3.ProjectOnPlane(coreRigidbody.transform.forward, Vector3.up).normalized;
            if (currentForward.sqrMagnitude > 0.0001f)
            {
                float signedAngle = Vector3.SignedAngle(currentForward, moveDirection, Vector3.up);
                Vector3 yawTorque = Vector3.up * (signedAngle * Mathf.Deg2Rad * facingTorque);
                if (IsFinite(yawTorque))
                    coreRigidbody.AddTorque(yawTorque, ForceMode.Acceleration);
            }
        }
    }

    private void ApplyUprightTorque()
    {
        if (coreRigidbody == null || coreRigidbody.isKinematic || ShouldSkipDynamicRigidbody(coreRigidbody))
            return;

        float stateScale = currentState == RagdollState.Limp ? Mathf.Clamp01(limpUprightScale) : Mathf.Max(0f, _stateBlend);
        if (stateScale <= 0f)
            return;

        Vector3 desiredUp = Vector3.up;
        Vector3 moveDirection = GetMoveDirection();
        if (moveDirection.sqrMagnitude > 0.0001f)
            desiredUp = (Vector3.up - moveDirection * Mathf.Max(0f, movementLean)).normalized;

        Quaternion delta = Quaternion.FromToRotation(coreRigidbody.transform.up, desiredUp);
        delta.ToAngleAxis(out float angleDegrees, out Vector3 axis);

        if (!IsFinite(axis) || float.IsNaN(angleDegrees) || float.IsInfinity(angleDegrees) || axis.sqrMagnitude < 0.0001f)
            return;

        if (angleDegrees > 180f)
            angleDegrees -= 360f;

        Vector3 torque = axis.normalized * (angleDegrees * Mathf.Deg2Rad * uprightTorque * stateScale);
        torque += -coreRigidbody.angularVelocity * uprightDamping * stateScale;
        if (IsFinite(torque))
            coreRigidbody.AddTorque(torque, ForceMode.Acceleration);
    }

    private void CacheInitialCoreLocalPose()
    {
        _hasInitialCoreLocalPose = false;

        if (coreRigidbody == null)
        {
            Log("[SUGA_ACTIVE_RAGDOLL] Core root anchor skipped. missing core.");
            return;
        }

        _initialCoreLocalPosition = transform.InverseTransformPoint(coreRigidbody.position);
        _initialCoreLocalRotation = Quaternion.Inverse(transform.rotation) * coreRigidbody.rotation;
        if (!IsFinite(_initialCoreLocalPosition) || !IsFinite(_initialCoreLocalRotation))
            return;

        _hasInitialCoreLocalPose = true;
        _loggedMissingCoreRootAnchor = false;
        Log("[SUGA_ACTIVE_RAGDOLL] Core root anchor initialized.");
    }

    private bool IsCoreRigidbodySameAsRootRigidbody()
    {
        Rigidbody rootRigidbody = GetComponent<Rigidbody>();
        return rootRigidbody != null && coreRigidbody == rootRigidbody;
    }

    private void EnterCoreKinematicAnchor()
    {
        if (!makeCoreKinematicWhileRagdollActive || !anchorCoreToRootWhileRagdollActive || coreRigidbody == null)
            return;

        if (IsCoreRigidbodySameAsRootRigidbody())
        {
            Log("[SUGA_ACTIVE_RAGDOLL] Kinematic core anchor skipped because core is root Rigidbody.");
            return;
        }

        if (!_hasInitialCoreLocalPose)
            CacheInitialCoreLocalPose();

        if (!_hasInitialCoreLocalPose)
            return;

        if (!_hasOriginalCoreKinematicState)
        {
            _originalCoreIsKinematic = coreRigidbody.isKinematic;
            _hasOriginalCoreKinematicState = true;
        }

        TryClearRigidbodyVelocity(coreRigidbody);
        coreRigidbody.isKinematic = true;

        if (snapCoreToRootOnRagdollActivation)
        {
            Vector3 targetPosition = transform.TransformPoint(_initialCoreLocalPosition);
            Quaternion targetRotation = transform.rotation * _initialCoreLocalRotation;
            if (IsFinite(targetPosition) && IsFinite(targetRotation))
            {
                coreRigidbody.position = targetPosition;
                coreRigidbody.rotation = targetRotation;
            }
        }

        _coreKinematicAnchorApplied = true;
        Log("[SUGA_ACTIVE_RAGDOLL] Kinematic core anchor entered.");
    }

    private void ApplyCoreKinematicAnchor(float deltaTime)
    {
        if (!_coreKinematicAnchorApplied)
            return;

        if (coreRigidbody == null)
            return;

        if (!_hasInitialCoreLocalPose)
            CacheInitialCoreLocalPose();

        if (!_hasInitialCoreLocalPose)
            return;

        if (IsCoreRigidbodySameAsRootRigidbody())
            return;

        if (!coreRigidbody.isKinematic)
            coreRigidbody.isKinematic = true;

        Vector3 targetPosition = transform.TransformPoint(_initialCoreLocalPosition);
        Quaternion targetRotation = transform.rotation * _initialCoreLocalRotation;
        if (!IsFinite(targetPosition) || !IsFinite(targetRotation))
            return;

        Vector3 currentPosition = coreRigidbody.position;
        Quaternion currentRotation = coreRigidbody.rotation;
        if (!IsFinite(currentPosition) || !IsFinite(currentRotation) || deltaTime <= 0f)
        {
            coreRigidbody.MovePosition(targetPosition);
            coreRigidbody.MoveRotation(targetRotation);
            return;
        }

        float safeDeltaTime = Mathf.Max(0f, deltaTime);
        float positionT = 1f - Mathf.Exp(-Mathf.Max(0f, coreRootAnchorFollowSpeed) * safeDeltaTime);
        float rotationT = 1f - Mathf.Exp(-Mathf.Max(0f, coreRootAnchorRotationSpeed) * safeDeltaTime);
        Vector3 nextPosition = Vector3.Lerp(currentPosition, targetPosition, positionT);
        Quaternion nextRotation = Quaternion.Slerp(currentRotation, targetRotation, rotationT);

        float distance = Vector3.Distance(currentPosition, targetPosition);
        float maxDistance = Mathf.Max(0f, coreRootAnchorMaxDistance);
        if (maxDistance > 0f && distance >= maxDistance)
            nextPosition = targetPosition;

        if (!IsFinite(nextPosition) || !IsFinite(nextRotation))
            return;

        coreRigidbody.MovePosition(nextPosition);
        coreRigidbody.MoveRotation(nextRotation);
    }

    private void ExitCoreKinematicAnchor(bool restoreOriginalState)
    {
        if (!_coreKinematicAnchorApplied)
            return;

        if (coreRigidbody != null)
        {
            if (restoreOriginalState && _hasOriginalCoreKinematicState)
                coreRigidbody.isKinematic = _originalCoreIsKinematic;

            TryClearRigidbodyVelocity(coreRigidbody);
        }

        _coreKinematicAnchorApplied = false;
        _hasOriginalCoreKinematicState = false;
        Log("[SUGA_ACTIVE_RAGDOLL] Kinematic core anchor exited.");
    }

    private void ApplyCoreRootAnchor(float deltaTime)
    {
        if (!ShouldApplyCoreRootAnchor())
            return;

        Vector3 targetPosition = transform.TransformPoint(_initialCoreLocalPosition);
        Quaternion targetRotation = transform.rotation * _initialCoreLocalRotation;
        if (!IsFinite(targetPosition) || !IsFinite(targetRotation))
            return;

        float safeDeltaTime = Mathf.Max(0f, deltaTime);
        float positionT = 1f - Mathf.Exp(-Mathf.Max(0f, coreRootAnchorFollowSpeed) * safeDeltaTime);
        float rotationT = 1f - Mathf.Exp(-Mathf.Max(0f, coreRootAnchorRotationSpeed) * safeDeltaTime);
        float distance = Vector3.Distance(coreRigidbody.position, targetPosition);

        Vector3 nextPosition = Vector3.Lerp(coreRigidbody.position, targetPosition, positionT);
        Quaternion nextRotation = Quaternion.Slerp(coreRigidbody.rotation, targetRotation, rotationT);

        float maxDistance = Mathf.Max(0f, coreRootAnchorMaxDistance);
        if (maxDistance > 0f && distance >= maxDistance)
        {
            nextPosition = GetCoreRootAnchorCorrectionPosition(coreRigidbody.position, targetPosition, distance);
            TryClearRigidbodyVelocity(coreRigidbody);
            Log("[SUGA_ACTIVE_RAGDOLL] Core root anchor max-distance correction applied.");
        }
        else if (dampCoreVelocityWhileAnchored)
        {
            ClampCoreRootAnchorVelocity();
        }

        coreRigidbody.MovePosition(nextPosition);
        coreRigidbody.MoveRotation(nextRotation);
    }

    private void PrepareCoreRootAnchorForRagdollActivation()
    {
        if (!anchorCoreToRootWhileRagdollActive || coreRigidbody == null)
            return;

        if (_coreKinematicAnchorApplied || IsCoreRigidbodySameAsRootRigidbody())
            return;

        if (!_hasInitialCoreLocalPose)
            CacheInitialCoreLocalPose();

        if (!_hasInitialCoreLocalPose)
            return;

        Vector3 targetPosition = transform.TransformPoint(_initialCoreLocalPosition);
        Quaternion targetRotation = transform.rotation * _initialCoreLocalRotation;
        if (!IsFinite(targetPosition) || !IsFinite(targetRotation))
            return;

        float distance = Vector3.Distance(coreRigidbody.position, targetPosition);
        float maxDistance = Mathf.Max(0f, coreRootAnchorMaxDistance);
        bool correctedPosition = false;
        if (maxDistance > 0f && distance >= maxDistance)
        {
            coreRigidbody.position = targetPosition;
            coreRigidbody.rotation = targetRotation;
            correctedPosition = true;
            Log("[SUGA_ACTIVE_RAGDOLL] Core root anchor prepared for ragdoll activation.");
        }

        if (correctedPosition || dampCoreVelocityWhileAnchored)
            TryClearRigidbodyVelocity(coreRigidbody);
    }

    private Vector3 GetCoreRootAnchorCorrectionPosition(Vector3 currentPosition, Vector3 targetPosition, float distance)
    {
        float maxCorrection = Mathf.Max(0f, coreRootAnchorMaxCorrectionPerStep);
        if (maxCorrection <= 0f || distance <= maxCorrection)
            return targetPosition;

        return Vector3.MoveTowards(currentPosition, targetPosition, maxCorrection);
    }

    private bool ShouldApplyCoreRootAnchor()
    {
        if (!anchorCoreToRootWhileRagdollActive || !IsActiveRagdollState(currentState))
            return false;

        if (_coreKinematicAnchorApplied)
            return false;

        if (coreRigidbody == null)
        {
            if (!_loggedMissingCoreRootAnchor)
            {
                Log("[SUGA_ACTIVE_RAGDOLL] Core root anchor skipped. missing core.");
                _loggedMissingCoreRootAnchor = true;
            }

            return false;
        }

        if (IsCoreRigidbodySameAsRootRigidbody())
            return false;

        if (!_hasInitialCoreLocalPose)
            CacheInitialCoreLocalPose();

        return _hasInitialCoreLocalPose;
    }

    private bool ShouldExcludeCoreFromDynamicRagdollForces()
    {
        return excludeCoreFromDynamicRagdollForces && _coreKinematicAnchorApplied;
    }

    private bool ShouldSkipDynamicRagdollPart(SugaRagdollPart part)
    {
        if (!ShouldExcludeCoreFromDynamicRagdollForces() || part == null)
            return false;

        if (part.role == SugaRagdollPartRole.BodyCore)
            return true;

        return part.rigidbody != null && part.rigidbody == coreRigidbody;
    }

    private bool ShouldSkipDynamicRigidbody(Rigidbody rb)
    {
        return ShouldExcludeCoreFromDynamicRagdollForces() && rb != null && rb == coreRigidbody;
    }

    private void ClampCoreRootAnchorVelocity()
    {
        if (coreRigidbody == null || coreRigidbody.isKinematic)
            return;

        float maxVelocity = Mathf.Max(0f, coreRootAnchorMaxVelocity);
        if (maxVelocity <= 0f)
        {
            TryClearRigidbodyVelocity(coreRigidbody);
            return;
        }

        Vector3 velocity = GetLinearVelocity(coreRigidbody);
        if (!IsFinite(velocity))
        {
            TrySetLinearVelocity(coreRigidbody, Vector3.zero);
        }
        else if (velocity.magnitude > maxVelocity)
        {
            TrySetLinearVelocity(coreRigidbody, Vector3.ClampMagnitude(velocity, maxVelocity));
        }

        if (!IsFinite(coreRigidbody.angularVelocity))
            TrySetAngularVelocity(coreRigidbody, Vector3.zero);
    }

    private Vector3 GetMoveDirection()
    {
        if (_moveInput.sqrMagnitude <= 0.0001f)
            return Vector3.zero;

        Transform reference = movementReference != null ? movementReference : transform;
        Vector3 forward = Vector3.ProjectOnPlane(reference.forward, Vector3.up).normalized;
        Vector3 right = Vector3.ProjectOnPlane(reference.right, Vector3.up).normalized;

        if (forward.sqrMagnitude < 0.0001f)
            forward = Vector3.forward;
        if (right.sqrMagnitude < 0.0001f)
            right = Vector3.right;

        Vector3 direction = forward * _moveInput.y + right * _moveInput.x;
        return direction.sqrMagnitude > 1f ? direction.normalized : direction;
    }

    private void ApplyRigidbodyDamping(SugaRagdollStateTuning tuning)
    {
        if (parts == null)
            return;

        for (int i = 0; i < parts.Length; i++)
        {
            SugaRagdollPart part = parts[i];
            if (part == null || part.rigidbody == null)
                continue;

            if (ShouldSkipDynamicRagdollPart(part))
                continue;

            SetRigidbodyDamping(
                part.rigidbody,
                Mathf.Max(0f, tuning.rigidbodyDrag * Mathf.Max(0f, part.dragMultiplier)),
                Mathf.Max(0f, tuning.rigidbodyAngularDrag * Mathf.Max(0f, part.angularDragMultiplier))
            );
        }
    }

    private void ApplyConfigurableJointDrives(SugaRagdollStateTuning tuning)
    {
        if (parts == null)
            return;

        for (int i = 0; i < parts.Length; i++)
        {
            SugaRagdollPart part = parts[i];
            if (part == null || part.configurableJoint == null)
                continue;

            if (ShouldSkipDynamicRagdollPart(part))
                continue;

            JointDrive drive = new JointDrive
            {
                positionSpring = Mathf.Max(0f, tuning.jointStrength * part.strengthMultiplier),
                positionDamper = Mathf.Max(0f, tuning.jointDamping * part.dampingMultiplier),
                maximumForce = Mathf.Max(0f, tuning.maxTorque)
            };

            part.configurableJoint.rotationDriveMode = RotationDriveMode.Slerp;
            part.configurableJoint.slerpDrive = drive;
        }
    }

    private void ApplyConfigurableJointTarget(SugaRagdollPart part, Quaternion targetWorldRotation)
    {
        if (part.configurableJoint == null)
            return;

        Quaternion connectedRotation = part.configurableJoint.connectedBody != null
            ? part.configurableJoint.connectedBody.rotation
            : Quaternion.identity;
        Quaternion targetLocalRotation = Quaternion.Inverse(connectedRotation) * targetWorldRotation;

        // ConfigurableJoint targetRotation은 joint axis/secondaryAxis 설정에 영향을 받습니다.
        // Suga 1차 프로토타입에서는 Rigidbody torque가 주 drive이고, 이 값은 ConfigurableJoint를 수동 구성했을 때 보조 drive로 사용합니다.
        part.configurableJoint.targetRotation = Quaternion.Inverse(targetLocalRotation) * part.initialTargetLocalRotation;
    }

    private Quaternion ResolveTargetRotation(SugaRagdollPart part)
    {
        if (part.targetTransform != null)
            return part.targetTransform.rotation;

        if (!useInitialPoseWhenTargetMissing)
            return part.rigidbody.rotation;

        if (part.role == SugaRagdollPartRole.BodyCore)
            return GetCoreUprightTargetRotation(part.rigidbody);

        if (part.connectedRigidbody != null)
            return part.connectedRigidbody.rotation * part.initialConnectedLocalRotation;

        return part.initialWorldRotation;
    }

    private Quaternion GetCoreUprightTargetRotation(Rigidbody rb)
    {
        if (rb == null)
            return Quaternion.identity;

        Vector3 forward = Vector3.ProjectOnPlane(rb.transform.forward, Vector3.up);
        if (forward.sqrMagnitude < 0.0001f)
            forward = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
        if (forward.sqrMagnitude < 0.0001f)
            forward = Vector3.forward;

        return Quaternion.LookRotation(forward.normalized, Vector3.up);
    }

    private void CacheParts()
    {
        if (parts == null)
            return;

        for (int i = 0; i < parts.Length; i++)
        {
            SugaRagdollPart part = parts[i];
            if (part == null)
                continue;

            part.CacheInitialState();
        }
    }

    private void CacheManagedRigidbodies()
    {
        List<Rigidbody> managedRigidbodies = new List<Rigidbody>();
        AddManagedRigidbody(managedRigidbodies, coreRigidbody);
        AddManagedRigidbody(managedRigidbodies, hitTargetRigidbody);

        if (parts != null)
        {
            for (int i = 0; i < parts.Length; i++)
            {
                SugaRagdollPart part = parts[i];
                if (part != null)
                    AddManagedRigidbody(managedRigidbodies, part.rigidbody);
            }
        }

        _managedRigidbodies = managedRigidbodies.ToArray();
        _managedRigidbodyOriginalKinematic = new bool[_managedRigidbodies.Length];
        for (int i = 0; i < _managedRigidbodies.Length; i++)
            _managedRigidbodyOriginalKinematic[i] = _managedRigidbodies[i] != null && _managedRigidbodies[i].isKinematic;
    }

    private void EnsureRagdollImpactBodyCache()
    {
        if (_ragdollImpactBodies == null || _ragdollImpactBodies.Length == 0)
            CacheRagdollImpactBodies();
        else
            LogRagdollImpactBodyCacheOnce();
    }

    private void CacheRagdollImpactBodies()
    {
        Rigidbody[] bodies = GetComponentsInChildren<Rigidbody>(true);
        _ragdollImpactBodies = bodies ?? new Rigidbody[0];
        _ragdollBodyByLowerName.Clear();

        Rigidbody rootRigidbody = GetComponent<Rigidbody>();
        for (int i = 0; i < _ragdollImpactBodies.Length; i++)
        {
            Rigidbody rb = _ragdollImpactBodies[i];
            if (rb == null)
                continue;

            string key = NormalizeRigidbodyName(rb.name);
            if (string.IsNullOrEmpty(key))
                continue;

            Rigidbody existing;
            if (!_ragdollBodyByLowerName.TryGetValue(key, out existing) || existing == rootRigidbody && rb != rootRigidbody)
                _ragdollBodyByLowerName[key] = rb;
        }

        LogRagdollImpactBodyCacheOnce();
    }

    private void LogRagdollImpactBodyCacheOnce()
    {
        if (_impactBodyCacheLogged || !activeRagdollImpactDebugLogs)
            return;

        _impactBodyCacheLogged = true;
        Rigidbody general = ResolveImpactTarget(ragdollGeneralTargetName);
        Rigidbody spinDash = ResolveImpactTarget(ragdollSpinDashTargetName);
        Rigidbody throwTarget = ResolveImpactTarget(ragdollThrowTargetName);
        ImpactLog(
            $"Impact body cache count={CountImpactBodies()} " +
            $"general={GetRigidbodyDebugName(general)} spinDash={GetRigidbodyDebugName(spinDash)} throw={GetRigidbodyDebugName(throwTarget)}");
    }

    private Rigidbody ResolveImpactTarget(string targetName)
    {
        EnsureRagdollImpactBodyCacheWithoutLogging();

        Rigidbody target;
        if (TryResolveNamedImpactTarget(targetName, out target))
            return target;

        if (!IsTargetName(targetName, "spine") && TryResolveNamedImpactTarget("spine", out target))
            return target;

        if (!IsTargetName(targetName, "hips") && TryResolveNamedImpactTarget("hips", out target))
            return target;

        Rigidbody child = GetFirstChildImpactRigidbody();
        if (child != null)
            return child;

        return GetComponent<Rigidbody>();
    }

    private void EnsureRagdollImpactBodyCacheWithoutLogging()
    {
        if (_ragdollImpactBodies != null && _ragdollImpactBodies.Length > 0)
            return;

        Rigidbody[] bodies = GetComponentsInChildren<Rigidbody>(true);
        _ragdollImpactBodies = bodies ?? new Rigidbody[0];
        _ragdollBodyByLowerName.Clear();

        Rigidbody rootRigidbody = GetComponent<Rigidbody>();
        for (int i = 0; i < _ragdollImpactBodies.Length; i++)
        {
            Rigidbody rb = _ragdollImpactBodies[i];
            if (rb == null)
                continue;

            string key = NormalizeRigidbodyName(rb.name);
            if (string.IsNullOrEmpty(key))
                continue;

            Rigidbody existing;
            if (!_ragdollBodyByLowerName.TryGetValue(key, out existing) || existing == rootRigidbody && rb != rootRigidbody)
                _ragdollBodyByLowerName[key] = rb;
        }
    }

    private bool TryResolveNamedImpactTarget(string targetName, out Rigidbody target)
    {
        target = null;
        string normalized = NormalizeRigidbodyName(targetName);
        if (string.IsNullOrEmpty(normalized))
            return false;

        if (_ragdollBodyByLowerName.TryGetValue(normalized, out target) && target != null)
            return true;

        Rigidbody rootRigidbody = GetComponent<Rigidbody>();
        for (int i = 0; i < _ragdollImpactBodies.Length; i++)
        {
            Rigidbody rb = _ragdollImpactBodies[i];
            if (rb == null || rb == rootRigidbody)
                continue;

            string bodyName = NormalizeRigidbodyName(rb.name);
            if (bodyName.Contains(normalized))
            {
                target = rb;
                return true;
            }
        }

        for (int i = 0; i < _ragdollImpactBodies.Length; i++)
        {
            Rigidbody rb = _ragdollImpactBodies[i];
            if (rb == null)
                continue;

            string bodyName = NormalizeRigidbodyName(rb.name);
            if (bodyName.Contains(normalized))
            {
                target = rb;
                return true;
            }
        }

        return false;
    }

    private Rigidbody GetFirstChildImpactRigidbody()
    {
        for (int i = 0; i < _ragdollImpactBodies.Length; i++)
        {
            Rigidbody rb = _ragdollImpactBodies[i];
            if (rb != null && rb.transform != transform)
                return rb;
        }

        return null;
    }

    private int CountImpactBodies()
    {
        if (_ragdollImpactBodies == null)
            return 0;

        int count = 0;
        for (int i = 0; i < _ragdollImpactBodies.Length; i++)
        {
            if (_ragdollImpactBodies[i] != null)
                count++;
        }

        return count;
    }

    private static bool IsTargetName(string value, string expected)
    {
        return NormalizeRigidbodyName(value) == NormalizeRigidbodyName(expected);
    }

    private static string NormalizeRigidbodyName(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().ToLowerInvariant();
    }

    private static string GetRigidbodyDebugName(Rigidbody rb)
    {
        return rb != null ? rb.name : "None";
    }

    private static void AddManagedRigidbody(List<Rigidbody> managedRigidbodies, Rigidbody rb)
    {
        if (rb == null || managedRigidbodies.Contains(rb))
            return;

        managedRigidbodies.Add(rb);
    }

    private bool TryGetBodyCorePartFocusPosition(out Vector3 position)
    {
        if (parts == null)
        {
            position = default;
            return false;
        }

        for (int i = 0; i < parts.Length; i++)
        {
            SugaRagdollPart part = parts[i];
            if (part == null || part.role != SugaRagdollPartRole.BodyCore)
                continue;

            if (TryGetRigidbodyFocusPosition(part.rigidbody, out position))
                return true;
        }

        position = default;
        return false;
    }

    private bool TryGetAnyPartFocusPosition(out Vector3 position)
    {
        if (parts == null)
        {
            position = default;
            return false;
        }

        for (int i = 0; i < parts.Length; i++)
        {
            SugaRagdollPart part = parts[i];
            if (part == null)
                continue;

            if (TryGetRigidbodyFocusPosition(part.rigidbody, out position))
                return true;
        }

        position = default;
        return false;
    }

    private static bool TryGetRigidbodyFocusPosition(Rigidbody body, out Vector3 position)
    {
        if (body == null)
        {
            position = default;
            return false;
        }

        position = body.worldCenterOfMass;
        if (!IsFinite(position))
            position = body.position;

        return IsFinite(position);
    }

    private void LogSetupWarningsOnce()
    {
        if (_setupWarningsLogged || !Application.isPlaying)
            return;

        _setupWarningsLogged = true;

        bool hasValidPart = false;
        if (parts == null || parts.Length == 0)
        {
            Warn("parts 배열이 비어 있습니다. ContextMenu 상태 전환은 가능하지만 Active Ragdoll 파츠 제어는 동작하지 않습니다.");
        }
        else
        {
            for (int i = 0; i < parts.Length; i++)
            {
                SugaRagdollPart part = parts[i];
                if (part == null)
                {
                    Warn($"parts[{i}]가 비어 있습니다. 해당 슬롯은 건너뜁니다.");
                    continue;
                }

                if (part.rigidbody == null)
                {
                    Warn($"parts[{i}] '{part.label}'에 Rigidbody가 없습니다. 해당 파츠는 건너뜁니다.");
                    continue;
                }

                hasValidPart = true;

                if (part.rigidbody.isKinematic && part.followTargetRotation)
                    Warn($"parts[{i}] '{part.label}' Rigidbody가 isKinematic=true라 torque 자세 보정이 적용되지 않습니다.");

                if (part.targetTransform == null && !useInitialPoseWhenTargetMissing)
                    Warn($"parts[{i}] '{part.label}' targetTransform이 없고 initial pose fallback도 꺼져 있어 현재 회전을 목표로 유지합니다.");

                if (part.configurableJoint == null && part.characterJoint == null && part.role != SugaRagdollPartRole.BodyCore)
                    Warn($"parts[{i}] '{part.label}'에 Joint가 없습니다. torque 보정은 가능하지만 관절 제한은 Rigidbody/Collider 설정에 의존합니다.");
            }
        }

        if (!hasValidPart)
            Warn("유효한 Rigidbody 파츠가 없습니다. Body/Core, Head, Left/Right Arm 또는 Wing, Left/Right Leg를 수동 연결하세요.");

        if (coreRigidbody == null)
            Warn("Body/Core Rigidbody를 찾지 못했습니다. Core Movement, Upright, ApplyForwardHit fallback이 제한됩니다.");
        else if (coreRigidbody.isKinematic)
            Warn($"Body/Core Rigidbody '{coreRigidbody.name}'가 isKinematic=true라 이동, upright torque, hit impulse가 적용되지 않습니다.");

        if (hitTargetRigidbody != null && hitTargetRigidbody.isKinematic)
            Warn($"hitTargetRigidbody '{hitTargetRigidbody.name}'가 isKinematic=true라 ApplyForwardHit impulse가 적용되지 않습니다.");

        if (enablePrototypeInput)
            Warn("enablePrototypeInput이 켜져 있습니다. 기존 PlayerLocomotion/CharacterController와 동시에 테스트하면 입력/이동이 충돌할 수 있습니다.");
    }

    private void ResolveCoreRigidbody()
    {
        if (coreRigidbody != null)
            return;

        if (parts == null)
            return;

        for (int i = 0; i < parts.Length; i++)
        {
            SugaRagdollPart part = parts[i];
            if (part != null && part.role == SugaRagdollPartRole.BodyCore && part.rigidbody != null)
            {
                coreRigidbody = part.rigidbody;
                return;
            }
        }
    }

    private void ApplyInitialMassPresetIfNeeded()
    {
        if (!applySugaMassPresetOnAwake || parts == null)
            return;

        for (int i = 0; i < parts.Length; i++)
        {
            SugaRagdollPart part = parts[i];
            if (part == null || part.rigidbody == null)
                continue;

            part.rigidbody.mass = Mathf.Max(0.01f, part.GetSuggestedMass());
            SetRigidbodyDamping(
                part.rigidbody,
                Mathf.Max(0f, normalTuning.rigidbodyDrag * Mathf.Max(0f, part.dragMultiplier)),
                Mathf.Max(0f, normalTuning.rigidbodyAngularDrag * Mathf.Max(0f, part.angularDragMultiplier))
            );
        }
    }

#if ENABLE_INPUT_SYSTEM
    private bool WasJumpPressedThisFrame(Keyboard keyboard)
    {
        if (keyboard == null)
            return false;

        switch (jumpKey)
        {
            case Key.Space:
                return keyboard.spaceKey.wasPressedThisFrame;
            case Key.Enter:
                return keyboard.enterKey.wasPressedThisFrame;
            case Key.NumpadEnter:
                return keyboard.numpadEnterKey.wasPressedThisFrame;
            case Key.LeftShift:
                return keyboard.leftShiftKey.wasPressedThisFrame;
            case Key.RightShift:
                return keyboard.rightShiftKey.wasPressedThisFrame;
            case Key.LeftCtrl:
                return keyboard.leftCtrlKey.wasPressedThisFrame;
            case Key.RightCtrl:
                return keyboard.rightCtrlKey.wasPressedThisFrame;
            default:
                // Keep this prototype compile-safe across Input System versions by avoiding generic keyboard indexer assumptions.
                return keyboard.spaceKey.wasPressedThisFrame;
        }
    }
#endif

    private static Vector3 GetLinearVelocity(Rigidbody rb)
    {
        if (rb == null)
            return Vector3.zero;

#if UNITY_6000_0_OR_NEWER
        return rb.linearVelocity;
#else
        return rb.velocity;
#endif
    }

    private static bool TrySetLinearVelocity(Rigidbody rb, Vector3 velocity)
    {
        if (rb == null || rb.isKinematic)
            return false;

#if UNITY_6000_0_OR_NEWER
        rb.linearVelocity = velocity;
#else
        rb.velocity = velocity;
#endif
        return true;
    }

    private static bool TrySetAngularVelocity(Rigidbody rb, Vector3 angularVelocity)
    {
        if (rb == null || rb.isKinematic)
            return false;

        rb.angularVelocity = angularVelocity;
        return true;
    }

    private static void SanitizeRigidbodyVelocity(Rigidbody rb)
    {
        if (rb == null)
            return;

        if (rb.isKinematic)
            return;

        if (!IsFinite(GetLinearVelocity(rb)))
            TrySetLinearVelocity(rb, Vector3.zero);

        if (!IsFinite(rb.angularVelocity))
            TrySetAngularVelocity(rb, Vector3.zero);
    }

    private static bool TryClearRigidbodyVelocity(Rigidbody rb)
    {
        if (rb == null || rb.isKinematic)
            return false;

        TrySetLinearVelocity(rb, Vector3.zero);
        TrySetAngularVelocity(rb, Vector3.zero);
        return true;
    }

    private static void SetRigidbodyDamping(Rigidbody rb, float linearDamping, float angularDamping)
    {
        if (rb == null)
            return;

#if UNITY_6000_0_OR_NEWER
        rb.linearDamping = linearDamping;
        rb.angularDamping = angularDamping;
#else
        rb.drag = linearDamping;
        rb.angularDrag = angularDamping;
#endif
    }

    private static bool IsFinite(Vector3 value)
    {
        return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
    }

    private static bool IsFinite(Quaternion value)
    {
        return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z) && IsFinite(value.w);
    }

    private static bool IsFinite(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }

    private SugaRagdollStateTuning GetTargetTuning()
    {
        switch (currentState)
        {
            case RagdollState.Hit:
                return hitTuning;
            case RagdollState.Stunned:
                return stunnedTuning;
            case RagdollState.Limp:
                return limpTuning;
            default:
                return normalTuning;
        }
    }

    private float GetStateStrengthScale(RagdollState state)
    {
        switch (state)
        {
            case RagdollState.Hit:
                return 0.35f;
            case RagdollState.Stunned:
                return 0.15f;
            case RagdollState.Limp:
                return 0.0f;
            default:
                return 1f;
        }
    }

    private void ImpactLog(string message)
    {
        if (!activeRagdollImpactDebugLogs)
            return;

        Debug.Log($"[SugaRagdoll] {message}", this);
    }

    private void Log(string message)
    {
        if (!enableDebugLogs)
            return;

        Debug.Log(message, this);
    }

    private void Warn(string message)
    {
        Debug.LogWarning($"[SUGA_ACTIVE_RAGDOLL] {message}", this);
    }

    private readonly struct RagdollImpactSettings
    {
        public readonly string targetName;
        public readonly float forwardImpulse;
        public readonly float upImpulse;
        public readonly float torqueImpulse;
        public readonly float unlockDuration;
        public readonly bool demoOverride;

        public RagdollImpactSettings(
            string targetName,
            float forwardImpulse,
            float upImpulse,
            float torqueImpulse,
            float unlockDuration,
            bool demoOverride)
        {
            this.targetName = targetName;
            this.forwardImpulse = forwardImpulse;
            this.upImpulse = upImpulse;
            this.torqueImpulse = torqueImpulse;
            this.unlockDuration = unlockDuration;
            this.demoOverride = demoOverride;
        }
    }
}

[Serializable]
public sealed class SugaRagdollPart
{
    [Tooltip("Inspector에서 구분하기 위한 파츠 이름입니다.")]
    public string label;

    [Tooltip("Suga 마스코트 기준 파츠 역할입니다. Tail/Ear는 1차 Active Ragdoll 대상에서 제외하세요.")]
    public SugaRagdollPartRole role = SugaRagdollPartRole.Other;

    [Tooltip("이 파츠의 물리 Rigidbody입니다. 비워두면 해당 파츠는 안전하게 건너뜁니다.")]
    public Rigidbody rigidbody;

    [Tooltip("선택 사항입니다. ConfigurableJoint가 있으면 joint drive/targetRotation도 함께 조절합니다. 현재 Suga CharacterJoint prefab에서는 비워둬도 됩니다.")]
    public ConfigurableJoint configurableJoint;

    [Tooltip("현재 Suga prefab처럼 CharacterJoint만 있는 경우 참고용으로 연결합니다. CharacterJoint를 직접 drive하지 않고 Rigidbody torque fallback이 목표 자세 보정을 담당합니다.")]
    public CharacterJoint characterJoint;

    [Tooltip("따라갈 목표 자세 Transform입니다. 별도 TargetRig가 없으면 비워두고 시작 pose fallback을 사용합니다.")]
    public Transform targetTransform;

    [Tooltip("이 파츠가 목표 회전을 따라가도록 torque를 적용할지 여부입니다.")]
    public bool followTargetRotation = true;

    [Tooltip("상태별 기본 관절 힘에 곱할 파츠별 배율입니다. Head/Neck은 높게, Arm/Wing/Leg는 낮게 시작하세요.")]
    public float strengthMultiplier = 1f;

    [Tooltip("상태별 기본 감쇠에 곱할 파츠별 배율입니다.")]
    public float dampingMultiplier = 1f;

    [Tooltip("상태별 Rigidbody drag에 곱할 파츠별 배율입니다.")]
    public float dragMultiplier = 1f;

    [Tooltip("상태별 Rigidbody angular drag에 곱할 파츠별 배율입니다. 둥근 몸통은 높게 잡으면 공처럼 구르는 현상이 줄어듭니다.")]
    public float angularDragMultiplier = 1f;

    [Tooltip("Suga 마스코트형 기본 질량 프리셋을 적용할 때 사용할 직접 지정 질량입니다. 0 이하이면 파츠 역할 기본값을 사용합니다.")]
    public float suggestedMassOverride = -1f;

    [NonSerialized] public Quaternion initialWorldRotation = Quaternion.identity;
    [NonSerialized] public Quaternion initialTargetLocalRotation = Quaternion.identity;
    [NonSerialized] public Quaternion initialConnectedLocalRotation = Quaternion.identity;
    [NonSerialized] public Rigidbody connectedRigidbody;

    public bool IsValid => rigidbody != null;

    public void CacheInitialState()
    {
        if (configurableJoint == null && rigidbody != null)
            configurableJoint = rigidbody.GetComponent<ConfigurableJoint>();

        if (characterJoint == null && rigidbody != null)
            characterJoint = rigidbody.GetComponent<CharacterJoint>();

        connectedRigidbody = null;
        if (configurableJoint != null && configurableJoint.connectedBody != null)
            connectedRigidbody = configurableJoint.connectedBody;
        else if (characterJoint != null && characterJoint.connectedBody != null)
            connectedRigidbody = characterJoint.connectedBody;

        if (rigidbody != null)
            initialWorldRotation = rigidbody.rotation;

        if (rigidbody != null && connectedRigidbody != null)
            initialConnectedLocalRotation = Quaternion.Inverse(connectedRigidbody.rotation) * rigidbody.rotation;
        else
            initialConnectedLocalRotation = Quaternion.identity;

        if (targetTransform != null)
            initialTargetLocalRotation = targetTransform.localRotation;
        else if (rigidbody != null)
            initialTargetLocalRotation = rigidbody.transform.localRotation;
        else
            initialTargetLocalRotation = Quaternion.identity;
    }

    public float GetSuggestedMass()
    {
        if (suggestedMassOverride > 0f)
            return suggestedMassOverride;

        switch (role)
        {
            case SugaRagdollPartRole.BodyCore:
                return 8.0f;
            case SugaRagdollPartRole.Head:
                return 1.6f;
            case SugaRagdollPartRole.LeftArmOrWing:
            case SugaRagdollPartRole.RightArmOrWing:
                return 0.7f;
            case SugaRagdollPartRole.LeftLeg:
            case SugaRagdollPartRole.RightLeg:
                return 0.9f;
            default:
                return 1.0f;
        }
    }
}

public enum SugaRagdollPartRole
{
    BodyCore = 0,
    Head = 1,
    LeftArmOrWing = 2,
    RightArmOrWing = 3,
    LeftLeg = 4,
    RightLeg = 5,
    Other = 6
}

[Serializable]
public struct SugaRagdollStateTuning
{
    [Tooltip("목표 자세를 따라가려는 관절 힘입니다.")]
    public float jointStrength;

    [Tooltip("관절 회전의 감쇠입니다.")]
    public float jointDamping;

    [Tooltip("각 파츠에 적용할 최대 보정 토크입니다.")]
    public float maxTorque;

    [Tooltip("Rigidbody linear damping 값입니다.")]
    public float rigidbodyDrag;

    [Tooltip("Rigidbody angular damping 값입니다.")]
    public float rigidbodyAngularDrag;

    public SugaRagdollStateTuning(float jointStrength, float jointDamping, float maxTorque, float rigidbodyDrag, float rigidbodyAngularDrag)
    {
        this.jointStrength = jointStrength;
        this.jointDamping = jointDamping;
        this.maxTorque = maxTorque;
        this.rigidbodyDrag = rigidbodyDrag;
        this.rigidbodyAngularDrag = rigidbodyAngularDrag;
    }

    public static SugaRagdollStateTuning Lerp(SugaRagdollStateTuning from, SugaRagdollStateTuning to, float t)
    {
        t = Mathf.Clamp01(t);
        return new SugaRagdollStateTuning(
            Mathf.Lerp(from.jointStrength, to.jointStrength, t),
            Mathf.Lerp(from.jointDamping, to.jointDamping, t),
            Mathf.Lerp(from.maxTorque, to.maxTorque, t),
            Mathf.Lerp(from.rigidbodyDrag, to.rigidbodyDrag, t),
            Mathf.Lerp(from.rigidbodyAngularDrag, to.rigidbodyAngularDrag, t)
        );
    }
}
