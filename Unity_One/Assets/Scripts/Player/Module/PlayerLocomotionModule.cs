using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public class PlayerLocomotionModule : NetworkBehaviour
{
    private enum SpinDashVisualRotationAxis
    {
        WorldUp,
        ParentUp,
        LocalUp,
        LocalForward,
        LocalRight
    }

    private const float MinExternalMoveSpeedMultiplier = 0.1f;
    private const float MaxExternalMoveSpeedMultiplier = 3f;

    [Header("Move Settings")]
    [Tooltip("걷기 이동 속도입니다. 값이 높을수록 기본 이동이 빨라집니다.")]
    [SerializeField] private float walkSpeed = 4f;

    [Tooltip("달리기 이동 속도입니다. 값이 높을수록 sprint 입력 시 더 빠르게 이동합니다.")]
    [SerializeField] private float sprintSpeed = 7f;

    [Tooltip("출발할 때 얼마나 빨리 최고 속도에 도달하는지")]
    [SerializeField] private float acceleration = 15f; // [수정] 기존 10 -> 15 (좀 더 빠르게 출발)

    [Tooltip("멈출 때 얼마나 빨리 정지하는지 (높을수록 칼브레이크)")]
    [SerializeField] private float deceleration = 30f; // [추가] 멈출 때는 2배 더 강력하게!

    [Header("Sprint Stamina")]
    [SerializeField, Tooltip("달리기 중 스테미너를 소비하고 부족하면 달리기를 제한할지 여부입니다.")]
    private bool useStaminaForSprint = true;

    [SerializeField, Tooltip("달리기 중 초당 소비되는 스테미너 양입니다.")]
    private float sprintStaminaCostPerSecond = 20f;

    [SerializeField, Tooltip("달리기를 시작하기 위해 필요한 최소 스테미너입니다.")]
    private float sprintMinimumStaminaToStart = 5f;

    [SerializeField, Tooltip("달리기를 계속 유지하기 위해 필요한 최소 스테미너입니다.")]
    private float sprintMinimumStaminaToContinue = 0.5f;

    [SerializeField, Tooltip("이동 입력이 있을 때만 달리기 스테미너를 소비할지 여부입니다.")]
    private bool consumeSprintStaminaOnlyWhileMoving = true;

    [SerializeField, Tooltip("스테미너 모듈을 찾지 못했을 때 기존처럼 달리기를 허용할지 여부입니다.")]
    private bool allowSprintWhenStaminaModuleMissing = true;

    [Header("Jump/Gravity")]
    [Tooltip("점프 높이입니다. 값이 높을수록 점프가 더 높아집니다.")]
    [SerializeField] private float jumpHeight = 1.5f;

    [Tooltip("중력 가속도입니다. 더 음수일수록 더 빠르게 떨어집니다.")]
    [SerializeField] private float gravity = -25f;

    [Tooltip("지면에 붙어 있도록 아래로 누르는 힘입니다. 값이 낮을수록 지면 접지가 강해집니다.")]
    [SerializeField] private float stickToGroundForce = -5f;

    [Header("Jump Stamina")]
    [SerializeField, Tooltip("점프 시 스테미너를 소비하고 부족하면 점프를 제한할지 여부입니다.")]
    private bool useStaminaForJump = true;

    [SerializeField, Tooltip("점프 1회에 소비되는 스테미너 양입니다.")]
    private float jumpStaminaCost = 12f;

    [SerializeField, Tooltip("점프를 시작하기 위해 필요한 최소 스테미너입니다.")]
    private float jumpMinimumStaminaToStart = 12f;

    [SerializeField, Tooltip("스테미너 모듈을 찾지 못했을 때 기존처럼 점프를 허용할지 여부입니다.")]
    private bool allowJumpWhenStaminaModuleMissing = true;

    [Header("Character Grab Locomotion")]
    [SerializeField, Tooltip("캐릭터 grab 상태일 때 SpinDash/Jump 제한을 적용할지 여부입니다.")]
    private bool enableCharacterGrabLocomotionRules = true;

    [SerializeField, Tooltip("다른 캐릭터를 잡고 있는 동안 점프 높이/속도에 곱할 배율입니다.")]
    private float characterGrabberJumpMultiplier = 0.4f;

    [SerializeField, Tooltip("다른 캐릭터에게 잡힌 상태에서 실제 jump를 차단할지 여부입니다.")]
    private bool blockJumpWhileGrabbedByCharacter = true;

    [Header("SpinDash")]
    [SerializeField, Tooltip("코드 기반 SpinDash 돌진 기능을 사용할지 여부입니다.")]
    private bool enableSpinDash = true;

    [SerializeField, Tooltip("SpinDash 중 코드 기반 회전/어지러움 시각 피드백을 사용할지 여부입니다.")]
    private bool enableSpinDashVisualFeedback = true;

    [SerializeField, Tooltip("SpinDash 시작 시 소모할 스태미나입니다.")]
    private float spinDashStaminaCost = 40f;

    [SerializeField, Tooltip("SpinDash 돌진이 지속되는 시간입니다.")]
    private float spinDashDuration = 0.6f;

    [SerializeField, Tooltip("SpinDash 중 전방으로 이동하는 속도입니다.")]
    private float spinDashSpeed = 9f;

    [SerializeField, Tooltip("SpinDash 재사용 대기 시간입니다.")]
    private float spinDashCooldown = 1.2f;

    [SerializeField, Tooltip("SpinDash 종료 후 적용할 짧은 조작 잠금 시간입니다.")]
    private float spinDashStunDuration = 0.7f;

    [SerializeField, Tooltip("SpinDash 중 visual child를 코드로 회전시킬지 여부입니다.")]
    private bool spinDashRotateVisual = true;

    [SerializeField, Tooltip("SpinDash 중 visual child가 회전하는 속도입니다.")]
    private float spinDashVisualRotationDegreesPerSecond = 1080f;

    [SerializeField, Tooltip("SpinDash 중 현재 들고 있는 무기 자세 오버라이드를 사용할지 여부입니다.")]
    private bool enableSpinDashHeldItemPose = true;

    [SerializeField, Tooltip("SpinDash 중 현재 들고 있는 무기에 추가로 적용할 로컬 오일러 회전 오프셋입니다.")]
    private Vector3 spinDashHeldItemEulerOffset = new Vector3(0f, 0f, 90f);

    [SerializeField, Tooltip("SpinDash visual feedback이 사용할 회전축입니다. 모델 local 축이 어긋나면 WorldUp 또는 ParentUp을 사용합니다.")]
    private SpinDashVisualRotationAxis spinDashVisualRotationAxis = SpinDashVisualRotationAxis.WorldUp;

    [SerializeField, Tooltip("SpinDash 중 회전시킬 비주얼 루트입니다. 비워두면 안전한 child를 자동 탐색하거나 회전을 생략합니다.")]
    private Transform spinDashVisualRoot;

    [SerializeField, Tooltip("SpinDash 중 함께 회전시킬 외형 파츠 목록입니다. 단일 부모가 없을 때 날개/몸통/본/평면 같은 여러 child를 지정합니다.")]
    private Transform[] spinDashVisualRoots;

    [SerializeField, Tooltip("SpinDash 종료 후 어지러움 흔들림 피드백이 지속되는 시간입니다.")]
    private float spinDashDizzyFeedbackDuration = 0.7f;

    [SerializeField, Tooltip("어지러움 피드백 중 visual child가 좌우로 흔들리는 각도입니다.")]
    private float spinDashDizzyWobbleAngle = 8f;

    [SerializeField, Tooltip("어지러움 피드백 중 visual child가 흔들리는 속도입니다.")]
    private float spinDashDizzyWobbleSpeed = 18f;

    [SerializeField, Tooltip("SpinDash 시작/종료 디버그 로그를 출력할지 여부입니다.")]
    private bool enableSpinDashDebugLogs = false;

    [Header("SpinDash VFX")]
    [SerializeField, Tooltip("SpinDash가 시작될 때 표시할 VFX 프리팹입니다. 비어 있으면 표시하지 않습니다.")]
    private GameObject spinDashStartVfxPrefab;

    [SerializeField, Tooltip("SpinDash로 대상 타격에 성공했을 때 표시할 VFX 프리팹입니다. 비어 있으면 표시하지 않습니다.")]
    private GameObject spinDashHitVfxPrefab;

    [SerializeField, Tooltip("SpinDash VFX 생성 위치의 Y 오프셋입니다.")]
    private float spinDashVfxYOffset = 0.35f;

    [SerializeField, Tooltip("SpinDash 시작 VFX를 강제로 제거할 시간입니다. 0이면 프리팹 자체 수명에 맡깁니다.")]
    private float spinDashStartVfxLifetime = 0f;

    [SerializeField, Tooltip("SpinDash 타격 VFX를 강제로 제거할 시간입니다. 0이면 프리팹 자체 수명에 맡깁니다.")]
    private float spinDashHitVfxLifetime = 0f;

    [SerializeField, Tooltip("SpinDash 시작 VFX를 플레이어에 붙여서 표시할지 여부입니다.")]
    private bool spinDashStartVfxAttachToPlayer = false;

    [Header("SpinDash Hit")]
    [SerializeField, Tooltip("SpinDash 중 다른 플레이어에게 서버 기준 타격 판정을 적용할지 여부입니다.")]
    private bool enableSpinDashHit = true;

    [SerializeField, Tooltip("SpinDash 타격 판정 반경입니다.")]
    private float spinDashHitRadius = 0.75f;

    [SerializeField, Tooltip("SpinDash 타격 판정 중심을 진행 방향 앞으로 얼마나 이동할지입니다.")]
    private float spinDashHitForwardOffset = 0.65f;

    [SerializeField, Tooltip("SpinDash 타격 판정 중심의 위쪽 보정값입니다.")]
    private float spinDashHitUpOffset = 0.5f;

    [SerializeField, Tooltip("SpinDash에 맞은 대상에게 적용할 전방 넉백 힘입니다.")]
    private float spinDashHitImpulse = 14f;

    [SerializeField, Tooltip("SpinDash에 맞은 대상에게 적용할 위쪽 넉백 힘입니다.")]
    private float spinDashHitUpImpulse = 3f;

    [SerializeField, Tooltip("SpinDash 타격 판정에 사용할 레이어입니다. 비어 있으면 Hurtbox/Player/BodyBlocker 레이어 또는 Body Blocker Mask를 사용합니다.")]
    private LayerMask spinDashHitLayerMask;

    [SerializeField, Tooltip("SpinDash 1회 동안 타격 가능한 최대 대상 수입니다.")]
    private int spinDashHitMaxTargetsPerDash = 8;

    [Header("Rotate")]
    [Tooltip("서버 회전 입력 배율입니다. 값이 높을수록 같은 입력으로 더 빠르게 회전합니다.")]
    [SerializeField] private float yawScale = 1f;

    [Tooltip("좌우 입력으로 캐릭터가 회전하는 속도입니다.")]
    [SerializeField] private float moveFacingTurnSpeed = 180f;

    [Tooltip("이 값보다 작은 전진/회전 입력은 무시합니다.")]
    [SerializeField] private float moveFacingInputDeadzone = 0.01f;

    [Tooltip("정지 상태에서만 적용할 수동 yaw 입력 배율입니다.")]
    [SerializeField] private float idleYawInputScale = 0.35f;

    [Header("Body Separation")]
    [Tooltip("플레이어 몸 분리 검사에 사용할 충돌 레이어 마스크입니다.")]
    [SerializeField] private LayerMask bodyBlockerMask;

    [Tooltip("몸 겹침을 검사할 구체 반경입니다. 값이 클수록 더 넓게 밀어냅니다.")]
    [SerializeField] private float separationProbeRadius = 0.18f;

    [Tooltip("몸 겹침 검사 중심의 높이 오프셋입니다.")]
    [SerializeField] private float separationProbeHeight = 0.18f;

    [Tooltip("몸 분리 시 추가로 확보할 여유 거리입니다.")]
    [SerializeField] private float separationPadding = 0.02f;

    [Tooltip("한 번의 분리 처리에서 이동할 수 있는 최대 거리입니다.")]
    [SerializeField] private float maxSeparationMove = 0.08f;

    private const int BodyOverlapBufferSize = 16;
    private const int SpinDashHitBufferSize = 16;

    private CharacterController _cc;
    private Vector3 _planarVelocity; // 수평 속도 (X, Z)
    private float _verticalVelocity; // 수직 속도 (Y)
    private readonly Collider[] _bodyOverlapHits = new Collider[BodyOverlapBufferSize];
    private readonly Collider[] _spinDashHitBuffer = new Collider[SpinDashHitBufferSize];
    private readonly HashSet<int> _processedOverlapRoots = new HashSet<int>(BodyOverlapBufferSize);
    private readonly HashSet<ulong> _spinDashHitClientIds = new HashSet<ulong>();
    private readonly Dictionary<string, float> _externalMoveSpeedMultipliers = new Dictionary<string, float>();
    private float _movementReferenceYaw;
    private bool _movementReferenceYawCaptured;
    private PlayerHub _playerHub;
    private PlayerStatusModule _statusModule;
    private PlayerInteractModule _interactModule;
    private bool _isSprintingWithStamina;
    private bool _isSpinDashing;
    private bool _isProcessingSpinDashProfileHit;
    private Vector3 _spinDashDirection;
    private float _spinDashEndTime;
    private float _spinDashCooldownUntil;
    private Transform _resolvedSpinDashVisualRoot;
    private bool _isSpinDashVisualFeedbackActive;
    private float _spinDashVisualFeedbackEndTime;
    private float _spinDashVisualFeedbackElapsed;
    private bool _isSpinDashDizzyFeedbackActive;
    private float _spinDashDizzyFeedbackEndTime;
    private GameObject _attachedSpinDashStartVfxInstance;
    private readonly List<Transform> _spinDashFeedbackVisualRoots = new List<Transform>();
    private readonly List<Quaternion> _spinDashFeedbackOriginalLocalRotations = new List<Quaternion>();

    public bool IsGrounded => _cc != null && _cc.isGrounded;
    public float PlanarSpeed => new Vector2(_planarVelocity.x, _planarVelocity.z).magnitude;
    public bool IsSpinDashing => _isSpinDashing || _isProcessingSpinDashProfileHit;
    public float SpinDashRemainingSeconds => _isSpinDashing ? Mathf.Max(0f, _spinDashEndTime - Time.time) : 0f;
    public float SpinDashCooldownRemainingSeconds => Mathf.Max(0f, _spinDashCooldownUntil - Time.time);

    public void SetExternalMoveSpeedMultiplier(string sourceKey, float multiplier)
    {
        if (string.IsNullOrEmpty(sourceKey))
            return;

        _externalMoveSpeedMultipliers[sourceKey] = ClampExternalMoveSpeedMultiplier(multiplier);
    }

    public void ClearExternalMoveSpeedMultiplier(string sourceKey)
    {
        if (string.IsNullOrEmpty(sourceKey))
            return;

        _externalMoveSpeedMultipliers.Remove(sourceKey);
    }

    public bool ServerTryApplyBounce(float upwardVelocity, bool overrideExistingUpwardVelocity = true)
    {
        NetworkManager networkManager = NetworkManager.Singleton;
        if (networkManager == null || !networkManager.IsServer)
            return false;

        if (!IsFinitePositive(upwardVelocity))
            return false;

        if (_cc == null)
            return false;

        if (!_cc.enabled)
            return false;

        if (!gameObject.activeInHierarchy || !_cc.gameObject.activeInHierarchy)
            return false;

        _verticalVelocity = overrideExistingUpwardVelocity
            ? upwardVelocity
            : Mathf.Max(_verticalVelocity, upwardVelocity);

        return true;
    }

    public bool ServerCanStartSpinDash()
    {
        if (!IsServerActive())
            return false;

        if (!enableSpinDash || _isSpinDashing)
            return false;

        if (Time.time < _spinDashCooldownUntil)
            return false;

        if (_cc == null || !_cc.enabled || !gameObject.activeInHierarchy || !_cc.gameObject.activeInHierarchy)
            return false;

        if (!CanSpinDashByStatusServer())
            return false;

        if (!CanSpinDashByCharacterGrabServer())
            return false;

        if (!ServerHasRequiredHeldItemForSpinDash())
            return false;

        if (!IsFinitePositive(spinDashDuration) || !IsFinitePositive(spinDashSpeed))
            return false;

        if (!TryGetSpinDashDirection(out _))
            return false;

        PlayerStaminaModule staminaModule = ResolveStaminaModule();
        float staminaCost = GetFiniteNonNegative(spinDashStaminaCost);
        if (staminaModule != null && !staminaModule.ServerCanSpendStamina(staminaCost))
            return false;

        return true;
    }

    public bool ServerTryStartSpinDash()
    {
        if (!ServerCanStartSpinDash())
            return false;

        if (!TryGetSpinDashDirection(out Vector3 direction))
            return false;

        if (!TrySpendSpinDashStaminaServer())
            return false;

        BeginSpinDashServer(direction);
        return true;
    }

    public void ServerCancelSpinDash(bool applyStun)
    {
        if (!IsServerActive())
            return;

        FinishSpinDashServer(applyStun);
    }

    private void Awake()
    {
        _cc = GetComponentInParent<CharacterController>();
    }

    private void LateUpdate()
    {
        TickSpinDashFeedbackLocal(Time.deltaTime);
    }

    private void OnDisable()
    {
        _isProcessingSpinDashProfileHit = false;
        ClearSpinDashHitState();
        StopSpinDashVisualFeedback();
    }

    private void OnDestroy()
    {
        _isProcessingSpinDashProfileHit = false;
        ClearSpinDashHitState();
        StopSpinDashFeedbackLocal();
        RestoreSpinDashFeedbackVisualLocal();
    }

    public bool TickServer(Vector2 moveInput, float yawDelta, bool jumpPressed, bool sprintHeld)
    {
        if (_cc == null) return false;
        bool didJump = false;
        float dt = Time.deltaTime;
        float forwardInput = GetForwardInput(moveInput.y);
        float turnInput = GetTurnInput(moveInput.x);
        bool spinDashActive = IsSpinDashActive();
        bool blockNormalInputMovementThisTick = false;

        if (spinDashActive)
        {
            if (!CanContinueSpinDashByStatusServer())
            {
                FinishSpinDashServer(false);
                spinDashActive = false;
                blockNormalInputMovementThisTick = true;
            }
            else if (Time.time >= _spinDashEndTime)
            {
                FinishSpinDashServer(true);
                spinDashActive = false;
                blockNormalInputMovementThisTick = true;
            }
        }

        bool canUseNormalInputMovement =
            !spinDashActive &&
            !blockNormalInputMovementThisTick &&
            CanUseNormalMovementByStatusServer();

        // 1. 회전 처리 (A/D 탱크 회전)
        if (canUseNormalInputMovement && Mathf.Abs(turnInput) > 0f)
            ApplyTurnInput(turnInput, dt);

        // 2. 점프 및 중력 처리
        bool grounded = IsGrounded;

        if (grounded)
        {
            if (_verticalVelocity > 0f)
                _verticalVelocity = 0f;

            if (_verticalVelocity <= 0f)
                _verticalVelocity = stickToGroundForce;

            if (canUseNormalInputMovement && jumpPressed && CanJumpByCharacterGrabServer() && ShouldAllowJumpWithStamina())
            {
                _verticalVelocity = GetCharacterGrabAdjustedJumpVelocity(Mathf.Sqrt(jumpHeight * -2f * gravity));
                didJump = true;
            }
        }

        _verticalVelocity += gravity * dt;

        if (spinDashActive)
        {
            TickSpinDashServer();
        }
        else
        {
            bool hasMoveInput = canUseNormalInputMovement && Mathf.Abs(forwardInput) > 0f;
            Vector3 inputDir = hasMoveInput ? GetMoveFacingDirection(moveInput) : Vector3.zero;

            // 3. 이동 속도 계산 (핵심 수정!)
            bool shouldApplySprint = ShouldApplySprint(sprintHeld && canUseNormalInputMovement, hasMoveInput, dt);
            float targetSpeed = shouldApplySprint ? sprintSpeed : walkSpeed;

            if (hasMoveInput)
                targetSpeed *= GetExternalMoveSpeedMultiplier();

            // 전진/후진 입력이 없으면 목표 속도는 0
            if (!hasMoveInput) targetSpeed = 0;

            Vector3 desiredVelocity = inputDir * targetSpeed;

            // [핵심] 입력이 있으면 '가속도', 입력이 없으면(멈출 때) '감속도' 적용
            float currentAccel = hasMoveInput ? acceleration : deceleration;

            // 부드러운 속도 변화 (Lerp)
            _planarVelocity = Vector3.Lerp(_planarVelocity, desiredVelocity, 1f - Mathf.Exp(-currentAccel * dt));

            // 속도가 아주 미세하게 남았을 때 완벽하게 0으로 만들기 (떨림 방지)
            if (targetSpeed == 0 && _planarVelocity.sqrMagnitude < 0.01f)
            {
                _planarVelocity = Vector3.zero;
            }
        }

        // 4. 최종 이동 적용
        Vector3 finalMotion = _planarVelocity;
        finalMotion.y = _verticalVelocity;

        _cc.Move(finalMotion * dt);
        if (spinDashActive)
            ServerProcessSpinDashHit();

        ResolveBodyOverlapServer();

        return didJump;
    }
    public void ResetMotionServer()
    {
        _isProcessingSpinDashProfileHit = false;
        FinishSpinDashServer(false);
        _planarVelocity = Vector3.zero;
        _verticalVelocity = 0f;
    }

    private bool IsSpinDashActive()
    {
        return _isSpinDashing;
    }

    private bool TryGetSpinDashDirection(out Vector3 direction)
    {
        direction = Vector3.zero;
        if (_cc == null)
            return false;

        direction = _cc.transform.forward;
        direction.y = 0f;

        if (direction.sqrMagnitude <= 0.0001f)
            return false;

        direction.Normalize();
        return IsFiniteVector(direction);
    }

    private void BeginSpinDashServer(Vector3 direction)
    {
        float now = Time.time;
        float duration = GetFiniteNonNegative(spinDashDuration);
        float cooldown = GetFiniteNonNegative(spinDashCooldown);

        _isSpinDashing = true;
        _spinDashDirection = direction;
        _spinDashEndTime = now + duration;
        _spinDashCooldownUntil = now + cooldown;
        _planarVelocity = _spinDashDirection * GetFiniteNonNegative(spinDashSpeed);
        _isSprintingWithStamina = false;
        ClearSpinDashHitState();

        TriggerSpinDashVisualFeedback(duration);
        LogSpinDash($"started. duration:{duration:0.###}, cooldown:{cooldown:0.###}, direction:{_spinDashDirection}");
    }

    private void TickSpinDashServer()
    {
        if (!_isSpinDashing)
            return;

        _planarVelocity = _spinDashDirection * GetFiniteNonNegative(spinDashSpeed);
    }

    private void ServerProcessSpinDashHit()
    {
        if (!IsServer || !enableSpinDashHit || !_isSpinDashing)
            return;

        int maxTargets = GetSpinDashHitMaxTargetsPerDash();
        if (maxTargets <= 0 || _spinDashHitClientIds.Count >= maxTargets)
            return;

        float radius = GetFiniteNonNegative(spinDashHitRadius);
        if (radius <= 0f)
            return;

        if (!TryGetSpinDashActorClientId(out ulong actorClientId))
            return;

        Vector3 direction = GetSpinDashHitDirection();
        Transform origin = _cc != null ? _cc.transform : transform;
        Vector3 center =
            origin.position +
            direction * GetFiniteNonNegative(spinDashHitForwardOffset) +
            Vector3.up * GetFiniteNonNegative(spinDashHitUpOffset);

        int hitCount = Physics.OverlapSphereNonAlloc(
            center,
            radius,
            _spinDashHitBuffer,
            GetSpinDashHitLayerMask(),
            QueryTriggerInteraction.Collide
        );

        if (hitCount <= 0)
            return;

        PlayerStatusModule selfStatus = ResolveStatusModule();

        for (int i = 0; i < hitCount; i++)
        {
            if (_spinDashHitClientIds.Count >= maxTargets)
                break;

            Collider hit = _spinDashHitBuffer[i];
            if (hit == null)
                continue;

            PlayerStatusModule targetStatus = ResolveSpinDashTargetStatus(hit);
            if (targetStatus == null)
                continue;

            if (!TryGetStatusOwnerClientId(targetStatus, out ulong targetClientId))
                continue;

            if (IsSelfSpinDashHitTarget(targetStatus, selfStatus, targetClientId, actorClientId))
                continue;

            if (_spinDashHitClientIds.Contains(targetClientId))
                continue;

            if (targetStatus.IsEliminated || targetStatus.IsKnocked || targetStatus.IsStandingUp)
                continue;

            _spinDashHitClientIds.Add(targetClientId);
            Vector3 impulse =
                direction * GetFiniteNonNegative(spinDashHitImpulse) +
                Vector3.up * GetFiniteNonNegative(spinDashHitUpImpulse);

            bool recordedContributor = ServerApplySpinDashHitKnockbackForProfileRouting(targetStatus, impulse, actorClientId, targetClientId);
            Vector3 hitVfxPosition = GetSpinDashHitVfxPosition(hit, center, targetStatus);
            TriggerSpinDashHitVfx(hitVfxPosition);
            LogSpinDash($"Hit target client={targetClientId} contributor={recordedContributor} impulse={impulse}");
        }
    }

    private bool ServerApplySpinDashHitKnockbackForProfileRouting(
        PlayerStatusModule targetStatus,
        Vector3 impulse,
        ulong actorClientId,
        ulong targetClientId)
    {
        bool previousRouting = _isProcessingSpinDashProfileHit;

        try
        {
            _isProcessingSpinDashProfileHit = true;
            LogSpinDash($"Profile routing enabled for hit target={targetClientId}");
            return targetStatus.ServerTryApplyCombatKnockback(impulse, actorClientId);
        }
        finally
        {
            _isProcessingSpinDashProfileHit = previousRouting;
            LogSpinDash("Profile routing restored");
        }
    }

    private void ClearSpinDashHitState()
    {
        _spinDashHitClientIds.Clear();
    }

    private Vector3 GetSpinDashHitDirection()
    {
        Vector3 direction = _spinDashDirection;
        direction.y = 0f;

        if (direction.sqrMagnitude <= 0.0001f)
        {
            Transform origin = _cc != null ? _cc.transform : transform;
            direction = origin.forward;
            direction.y = 0f;
        }

        if (direction.sqrMagnitude <= 0.0001f || !IsFiniteVector(direction))
            return Vector3.forward;

        return direction.normalized;
    }

    private int GetSpinDashHitLayerMask()
    {
        if (spinDashHitLayerMask.value != 0)
            return spinDashHitLayerMask.value;

        int playerLikeLayerMask = LayerMask.GetMask("Hurtbox", "Player", "BodyBlocker");
        if (playerLikeLayerMask != 0)
            return playerLikeLayerMask;

        if (bodyBlockerMask.value != 0)
            return bodyBlockerMask.value;

        return ~0;
    }

    private int GetSpinDashHitMaxTargetsPerDash()
    {
        return Mathf.Max(0, spinDashHitMaxTargetsPerDash);
    }

    private PlayerStatusModule ResolveSpinDashTargetStatus(Collider hit)
    {
        if (hit == null)
            return null;

        PlayerStatusModule targetStatus = hit.GetComponentInParent<PlayerStatusModule>();
        if (targetStatus != null)
            return targetStatus;

        Transform root = hit.transform.root;
        return root != null ? root.GetComponentInChildren<PlayerStatusModule>(true) : null;
    }

    private bool TryGetSpinDashActorClientId(out ulong actorClientId)
    {
        actorClientId = ulong.MaxValue;

        NetworkObject ownerObject = NetworkObject;
        if (ownerObject == null)
            ownerObject = GetComponentInParent<NetworkObject>();

        if (ownerObject == null)
            return false;

        actorClientId = ownerObject.OwnerClientId;
        return actorClientId != ulong.MaxValue;
    }

    private static bool TryGetStatusOwnerClientId(PlayerStatusModule status, out ulong clientId)
    {
        clientId = ulong.MaxValue;
        if (status == null)
            return false;

        NetworkObject ownerObject = status.NetworkObject;
        if (ownerObject == null)
            ownerObject = status.GetComponentInParent<NetworkObject>();

        if (ownerObject == null)
            return false;

        clientId = ownerObject.OwnerClientId;
        return clientId != ulong.MaxValue;
    }

    private bool IsSelfSpinDashHitTarget(PlayerStatusModule targetStatus, PlayerStatusModule selfStatus, ulong targetClientId, ulong actorClientId)
    {
        if (targetStatus == null)
            return false;

        if (selfStatus != null && targetStatus == selfStatus)
            return true;

        if (targetClientId == actorClientId)
            return true;

        Transform selfRoot = selfStatus != null ? selfStatus.transform.root : transform.root;
        Transform targetRoot = targetStatus.transform.root;
        return selfRoot != null && targetRoot != null && selfRoot == targetRoot;
    }

    private Vector3 GetSpinDashHitVfxPosition(Collider hitCollider, Vector3 hitCenter, PlayerStatusModule targetStatus)
    {
        float yOffset = GetFiniteNonNegative(spinDashVfxYOffset);

        if (hitCollider != null)
        {
            Vector3 closestPoint = hitCollider.ClosestPoint(hitCenter);
            if (IsFiniteVector(closestPoint) && (closestPoint - hitCenter).sqrMagnitude > 0.0001f)
                return closestPoint + Vector3.up * yOffset;

            Vector3 boundsCenter = hitCollider.bounds.center;
            if (IsFiniteVector(boundsCenter))
                return boundsCenter + Vector3.up * yOffset;
        }

        if (targetStatus != null)
            return targetStatus.transform.position + Vector3.up * yOffset;

        return hitCenter + Vector3.up * yOffset;
    }

    private void TriggerSpinDashHitVfx(Vector3 position)
    {
        if (spinDashHitVfxPrefab == null)
        {
            LogSpinDashVisual("Hit VFX skipped prefab null");
            return;
        }

        if (IsSpawned)
        {
            PlaySpinDashHitVfxClientRpc(position);
            LogSpinDashVisual($"Hit VFX sent position={position}");
            return;
        }

        SpawnSpinDashVfxLocal(spinDashHitVfxPrefab, position, GetSpinDashVfxRotation(), GetFiniteNonNegative(spinDashHitVfxLifetime));
    }

    private void FinishSpinDashServer(bool applyStun)
    {
        if (!_isSpinDashing)
        {
            _isProcessingSpinDashProfileHit = false;
            ClearSpinDashHitState();
            StopSpinDashVisualFeedback();
            return;
        }

        _isProcessingSpinDashProfileHit = false;
        _isSpinDashing = false;
        _spinDashDirection = Vector3.zero;
        _spinDashEndTime = 0f;
        _planarVelocity = Vector3.zero;
        _isSprintingWithStamina = false;
        ClearSpinDashHitState();

        StopSpinDashVisualFeedback();

        if (applyStun)
        {
            PlayerStatusModule statusModule = ResolveStatusModule();
            if (statusModule != null)
                statusModule.ServerApplyTemporaryControlLock(GetFiniteNonNegative(spinDashStunDuration), true, true, true);

            TriggerSpinDashDizzyFeedback(GetSpinDashDizzyFeedbackDuration());
        }

        LogSpinDash($"finished. applyStun:{applyStun}");
    }

    private void TriggerSpinDashVisualFeedback(float duration)
    {
        if (!enableSpinDashVisualFeedback && !enableSpinDashHeldItemPose && spinDashStartVfxPrefab == null)
            return;

        float validDuration = GetFiniteNonNegative(duration);
        if (validDuration <= 0f)
            return;

        if (IsSpawned)
            PlaySpinDashVisualFeedbackClientRpc(validDuration);
        else
            BeginSpinDashVisualFeedbackLocal(validDuration);
    }

    private void TriggerSpinDashDizzyFeedback(float duration)
    {
        if (!enableSpinDashVisualFeedback && !enableSpinDashHeldItemPose)
            return;

        float validDuration = GetFiniteNonNegative(duration);
        if (validDuration <= 0f)
            return;

        if (IsSpawned)
            PlaySpinDashDizzyFeedbackClientRpc(validDuration);
        else
            BeginSpinDashDizzyFeedbackLocal(validDuration);
    }

    [ClientRpc]
    private void PlaySpinDashVisualFeedbackClientRpc(float duration)
    {
        BeginSpinDashVisualFeedbackLocal(duration);
    }

    [ClientRpc]
    private void PlaySpinDashHitVfxClientRpc(Vector3 position)
    {
        SpawnSpinDashVfxLocal(spinDashHitVfxPrefab, position, GetSpinDashVfxRotation(), GetFiniteNonNegative(spinDashHitVfxLifetime));
    }

    [ClientRpc]
    private void PlaySpinDashDizzyFeedbackClientRpc(float duration)
    {
        BeginSpinDashDizzyFeedbackLocal(duration);
    }

    [ClientRpc]
    private void StopSpinDashVisualFeedbackClientRpc()
    {
        StopSpinDashFeedbackLocal();
        RestoreSpinDashFeedbackVisualLocal();
    }

    private void BeginSpinDashVisualFeedbackLocal(float duration)
    {
        bool shouldStartVisualRotation = enableSpinDashVisualFeedback && spinDashRotateVisual;
        bool shouldApplyHeldItemPose = enableSpinDashHeldItemPose;
        bool shouldSpawnStartVfx = spinDashStartVfxPrefab != null;
        if (!shouldStartVisualRotation && !shouldApplyHeldItemPose && !shouldSpawnStartVfx)
            return;

        bool hasPreparedVisualRoots = false;
        if (shouldStartVisualRotation)
        {
            if (!TryPrepareSpinDashFeedbackVisualRoot(out _))
            {
                if (!shouldApplyHeldItemPose && !shouldSpawnStartVfx)
                    return;
            }
            else
            {
                hasPreparedVisualRoots = true;
            }
        }

        _isSpinDashDizzyFeedbackActive = false;
        _isSpinDashVisualFeedbackActive = hasPreparedVisualRoots;
        _spinDashVisualFeedbackElapsed = 0f;
        _spinDashVisualFeedbackEndTime = Time.time + GetFiniteNonNegative(duration);

        if (hasPreparedVisualRoots)
        {
            RestoreSpinDashFeedbackVisualLocal();
            LogSpinDashVisual($"RotationAxis={spinDashVisualRotationAxis}");
            LogSpinDashVisual($"Start roots={GetSpinDashFeedbackRootsLabel()} duration={duration:0.###}");
        }

        SpawnSpinDashStartVfxLocal();
        ApplySpinDashHeldItemPoseLocal();
    }

    private void SpawnSpinDashStartVfxLocal()
    {
        if (spinDashStartVfxPrefab == null)
        {
            LogSpinDashVisual("Start VFX skipped prefab null");
            return;
        }

        Transform parent = spinDashStartVfxAttachToPlayer
            ? (_cc != null ? _cc.transform : transform)
            : null;

        GameObject instance = SpawnSpinDashVfxLocal(
            spinDashStartVfxPrefab,
            GetSpinDashStartVfxPosition(),
            GetSpinDashVfxRotation(),
            GetFiniteNonNegative(spinDashStartVfxLifetime),
            parent
        );

        if (spinDashStartVfxAttachToPlayer)
        {
            ClearAttachedSpinDashStartVfxLocal();
            _attachedSpinDashStartVfxInstance = instance;
        }

        LogSpinDashVisual("Start VFX spawned");
    }

    private GameObject SpawnSpinDashVfxLocal(GameObject prefab, Vector3 position, Quaternion rotation, float lifetime, Transform parent = null)
    {
        if (prefab == null)
            return null;

        GameObject instance = Instantiate(prefab, position, rotation, parent);
        if (instance != null && lifetime > 0f)
            Destroy(instance, lifetime);

        return instance;
    }

    private Vector3 GetSpinDashStartVfxPosition()
    {
        float yOffset = GetFiniteNonNegative(spinDashVfxYOffset);
        if (_cc != null)
            return _cc.transform.TransformPoint(_cc.center) + Vector3.up * yOffset;

        return transform.position + Vector3.up * yOffset;
    }

    private Quaternion GetSpinDashVfxRotation()
    {
        Vector3 direction = GetSpinDashHitDirection();
        if (direction.sqrMagnitude > 0.0001f && IsFiniteVector(direction))
            return Quaternion.LookRotation(direction.normalized, Vector3.up);

        return transform.rotation;
    }

    private void ClearAttachedSpinDashStartVfxLocal()
    {
        if (_attachedSpinDashStartVfxInstance == null)
            return;

        Destroy(_attachedSpinDashStartVfxInstance);
        _attachedSpinDashStartVfxInstance = null;
    }

    private void BeginSpinDashDizzyFeedbackLocal(float duration)
    {
        if (!enableSpinDashVisualFeedback && !enableSpinDashHeldItemPose)
            return;

        bool hasPreparedVisualRoots = false;
        if (enableSpinDashVisualFeedback)
        {
            if (!TryPrepareSpinDashFeedbackVisualRoot(out _))
            {
                if (!enableSpinDashHeldItemPose)
                    return;
            }
            else
            {
                hasPreparedVisualRoots = true;
            }
        }

        _isSpinDashVisualFeedbackActive = false;
        _isSpinDashDizzyFeedbackActive = hasPreparedVisualRoots;
        _spinDashDizzyFeedbackEndTime = Time.time + GetFiniteNonNegative(duration);
        RestoreSpinDashFeedbackVisualLocal();
        RestoreSpinDashHeldItemPoseLocal();

        if (hasPreparedVisualRoots)
            LogSpinDashVisual($"Dizzy roots={GetSpinDashFeedbackRootsLabel()} duration={duration:0.###}");
    }

    private void TickSpinDashFeedbackLocal(float deltaTime)
    {
        if (_isSpinDashVisualFeedbackActive)
            TickSpinDashVisualFeedbackLocal(deltaTime);

        if (_isSpinDashDizzyFeedbackActive)
            TickSpinDashDizzyFeedbackLocal(deltaTime);
    }

    private void TickSpinDashVisualFeedbackLocal(float deltaTime)
    {
        if (!_isSpinDashVisualFeedbackActive)
            return;

        if (!EnsureSpinDashFeedbackVisualRoot())
        {
            StopSpinDashFeedbackLocal();
            return;
        }

        if (Time.time >= _spinDashVisualFeedbackEndTime)
        {
            _isSpinDashVisualFeedbackActive = false;
            RestoreSpinDashFeedbackVisualLocal();
            return;
        }

        float rotationSpeed = GetFiniteNonNegative(spinDashVisualRotationDegreesPerSecond);
        if (rotationSpeed <= 0f)
            return;

        _spinDashVisualFeedbackElapsed += Mathf.Max(0f, deltaTime);
        float rotationAngle = _spinDashVisualFeedbackElapsed * rotationSpeed;
        ApplySpinDashFeedbackRotation(rotationAngle);
    }

    private void TickSpinDashDizzyFeedbackLocal(float deltaTime)
    {
        if (!_isSpinDashDizzyFeedbackActive)
            return;

        if (!EnsureSpinDashFeedbackVisualRoot())
        {
            StopSpinDashFeedbackLocal();
            return;
        }

        if (Time.time >= _spinDashDizzyFeedbackEndTime)
        {
            _isSpinDashDizzyFeedbackActive = false;
            RestoreSpinDashFeedbackVisualLocal();
            return;
        }

        float wobbleAngle = GetFiniteNonNegative(spinDashDizzyWobbleAngle);
        float wobbleSpeed = GetFiniteNonNegative(spinDashDizzyWobbleSpeed);
        if (wobbleAngle <= 0f || wobbleSpeed <= 0f)
            return;

        float wobble = Mathf.Sin(Time.time * wobbleSpeed) * wobbleAngle;
        ApplySpinDashDizzyWobble(wobble);
    }

    private void StopSpinDashFeedbackLocal()
    {
        _isSpinDashVisualFeedbackActive = false;
        _isSpinDashDizzyFeedbackActive = false;
        _spinDashVisualFeedbackElapsed = 0f;
        _spinDashVisualFeedbackEndTime = 0f;
        _spinDashDizzyFeedbackEndTime = 0f;
        ClearAttachedSpinDashStartVfxLocal();
        RestoreSpinDashHeldItemPoseLocal();
    }

    private void StopSpinDashVisualFeedback()
    {
        if (IsSpawned)
        {
            StopSpinDashVisualFeedbackClientRpc();
            return;
        }

        StopSpinDashFeedbackLocal();
        RestoreSpinDashFeedbackVisualLocal();
    }

    private bool TryPrepareSpinDashFeedbackVisualRoot(out Transform visualRoot)
    {
        visualRoot = null;

        if (!TryResolveSpinDashFeedbackRoots(_spinDashFeedbackVisualRoots))
        {
            LogSpinDashVisualWarning("No safe visual root found");
            return false;
        }

        visualRoot = _spinDashFeedbackVisualRoots[0];
        StoreSpinDashFeedbackOriginalRotations();
        return true;
    }

    private bool EnsureSpinDashFeedbackVisualRoot()
    {
        if (AreSpinDashFeedbackRootsReady())
            return true;

        return TryPrepareSpinDashFeedbackVisualRoot(out _);
    }

    private void RestoreSpinDashFeedbackVisualLocal()
    {
        if (_spinDashFeedbackVisualRoots.Count <= 0 || _spinDashFeedbackVisualRoots.Count != _spinDashFeedbackOriginalLocalRotations.Count)
            return;

        for (int i = 0; i < _spinDashFeedbackVisualRoots.Count; i++)
        {
            Transform root = _spinDashFeedbackVisualRoots[i];
            if (root == null)
                continue;

            root.localRotation = _spinDashFeedbackOriginalLocalRotations[i];
        }

        LogSpinDashVisual($"Restore roots={GetSpinDashFeedbackRootsLabel()}");
    }

    private bool TryResolveSpinDashFeedbackRoots(List<Transform> roots)
    {
        roots.Clear();

        if (spinDashVisualRoots != null && spinDashVisualRoots.Length > 0)
        {
            LogSpinDashVisual($"Explicit roots count={spinDashVisualRoots.Length}");

            HashSet<int> seenRootIds = new HashSet<int>();
            for (int i = 0; i < spinDashVisualRoots.Length; i++)
            {
                Transform candidate = spinDashVisualRoots[i];
                if (!TryAddSpinDashFeedbackRoot(candidate, roots, seenRootIds, true))
                    continue;
            }

            if (roots.Count > 0)
            {
                LogSpinDashVisual($"Selected roots={GetSpinDashFeedbackRootsLabel(roots)}");
                return true;
            }
        }

        HashSet<int> seenIds = new HashSet<int>();

        if (TryAddSpinDashFeedbackRoot(spinDashVisualRoot, roots, seenIds, true))
        {
            LogSpinDashVisual($"Selected roots={GetSpinDashFeedbackRootsLabel(roots)}");
            return true;
        }

        if (TryAddSpinDashFeedbackRoot(_resolvedSpinDashVisualRoot, roots, seenIds, false))
        {
            LogSpinDashVisual($"Selected roots={GetSpinDashFeedbackRootsLabel(roots)}");
            return true;
        }

        if (_cc == null)
            return false;

        Transform[] candidates = _cc.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < candidates.Length; i++)
        {
            Transform candidate = candidates[i];
            if (IsUnsafeSpinDashVisualRoot(candidate) || !IsLikelySpinDashVisualRoot(candidate))
                continue;

            if (candidate.name.IndexOf("Boing_Visual", System.StringComparison.OrdinalIgnoreCase) >= 0)
                continue;

            _resolvedSpinDashVisualRoot = candidate;
            roots.Add(candidate);
            LogSpinDashVisual($"Selected roots={GetSpinDashFeedbackRootsLabel(roots)}");
            return true;
        }

        return false;
    }

    private void ApplySpinDashHeldItemPoseLocal()
    {
        if (!enableSpinDashHeldItemPose)
            return;

        PlayerInteractModule interactModule = ResolveInteractModule();
        if (interactModule == null)
            return;

        if (interactModule.GetHeldItemVisualTransform() == null)
            return;

        interactModule.SetExternalHeldItemPoseOverride(spinDashHeldItemEulerOffset, "SpinDash");
        LogSpinDashVisual("Held item pose override set source=SpinDash");
    }

    private void RestoreSpinDashHeldItemPoseLocal()
    {
        PlayerInteractModule interactModule = ResolveInteractModule();
        if (interactModule == null)
            return;

        if (!interactModule.HasExternalHeldItemPoseOverride)
            return;

        interactModule.ClearExternalHeldItemPoseOverride("SpinDash");
        LogSpinDashVisual("Held item pose override cleared source=SpinDash");
    }

    private bool TryAddSpinDashFeedbackRoot(Transform candidate, List<Transform> roots, HashSet<int> seenRootIds, bool isExplicit)
    {
        if (candidate == null)
        {
            if (isExplicit)
                LogSpinDashVisualWarning("Rejected root=<null> reason=null");

            return false;
        }

        string rejectionReason;
        bool isUnsafe = isExplicit
            ? IsUnsafeExplicitVisualRoot(candidate, out rejectionReason)
            : IsUnsafeSpinDashVisualRoot(candidate, out rejectionReason);

        if (isUnsafe)
        {
            if (isExplicit)
                LogSpinDashVisualWarning($"Rejected root={candidate.name} reason={rejectionReason}");

            return false;
        }

        int instanceId = candidate.GetInstanceID();
        if (!seenRootIds.Add(instanceId))
            return false;

        roots.Add(candidate);
        return true;
    }

    private bool IsUnsafeExplicitVisualRoot(Transform candidate)
    {
        return IsUnsafeExplicitVisualRoot(candidate, out _);
    }

    private bool IsUnsafeExplicitVisualRoot(Transform candidate, out string rejectionReason)
    {
        if (candidate == null)
        {
            rejectionReason = "null";
            return true;
        }

        if (_cc == null)
        {
            rejectionReason = string.Empty;
            return false;
        }

        Transform ccTransform = _cc.transform;
        Transform playerRoot = transform.root;
        if (candidate == ccTransform || candidate == transform || candidate == ccTransform.root || candidate == playerRoot)
        {
            rejectionReason = "unsafe-root";
            return true;
        }

        if (candidate.GetComponent<CharacterController>() != null ||
            candidate.GetComponent<Rigidbody>() != null ||
            candidate.GetComponent<NetworkObject>() != null)
        {
            rejectionReason = "unsafe-component";
            return true;
        }

        rejectionReason = string.Empty;
        return false;
    }

    private bool IsUnsafeSpinDashVisualRoot(Transform candidate)
    {
        return IsUnsafeSpinDashVisualRoot(candidate, out _);
    }

    private bool IsUnsafeSpinDashVisualRoot(Transform candidate, out string rejectionReason)
    {
        if (candidate == null || _cc == null)
        {
            rejectionReason = candidate == null ? "null" : "missing-character-controller";
            return true;
        }

        Transform ccTransform = _cc.transform;
        Transform playerRoot = transform.root;
        if (candidate == ccTransform || candidate == transform || candidate == ccTransform.root || candidate == playerRoot)
        {
            rejectionReason = "unsafe-root";
            return true;
        }

        if (!candidate.IsChildOf(ccTransform))
        {
            rejectionReason = "not-character-controller-child";
            return true;
        }

        if (candidate.GetComponent<CharacterController>() != null ||
            candidate.GetComponent<Rigidbody>() != null ||
            candidate.GetComponent<NetworkObject>() != null)
        {
            rejectionReason = "unsafe-component";
            return true;
        }

        rejectionReason = string.Empty;
        return false;
    }

    private static bool IsLikelySpinDashVisualRoot(Transform candidate)
    {
        if (candidate == null || string.IsNullOrEmpty(candidate.name))
            return false;

        string candidateName = candidate.name;
        return candidateName.IndexOf("visual", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
               candidateName.IndexOf("body", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
               candidateName.IndexOf("model", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
               candidateName.IndexOf("mesh", System.StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private bool TrySpendSpinDashStaminaServer()
    {
        float staminaCost = GetFiniteNonNegative(spinDashStaminaCost);
        if (staminaCost <= 0f)
            return true;

        PlayerStaminaModule staminaModule = ResolveStaminaModule();
        if (staminaModule == null)
            return true;

        if (!staminaModule.ServerCanSpendStamina(staminaCost))
            return false;

        return staminaModule.ServerTrySpendStamina(staminaCost);
    }

    private float GetSpinDashDizzyFeedbackDuration()
    {
        float configuredDuration = GetFiniteNonNegative(spinDashDizzyFeedbackDuration);
        if (configuredDuration > 0f)
            return configuredDuration;

        return GetFiniteNonNegative(spinDashStunDuration);
    }

    private bool CanSpinDashByStatusServer()
    {
        PlayerStatusModule statusModule = ResolveStatusModule();
        if (statusModule == null)
            return false;

        if (statusModule.IsEliminated)
            return false;

        return statusModule.CanMove;
    }

    private bool ServerHasRequiredHeldItemForSpinDash()
    {
        PlayerInteractModule interactModule = ResolveInteractModule();
        if (interactModule == null)
        {
            LogSpinDash("Start rejected: interact module missing");
            return false;
        }

        if (!interactModule.HasHeldItem())
        {
            LogSpinDash("Start rejected: held item required");
            return false;
        }

        return true;
    }

    private bool CanSpinDashByCharacterGrabServer()
    {
        if (!TryGetCharacterGrabStateServer(out bool isGrabbing, out bool isGrabbed))
            return true;

        if (isGrabbing)
        {
            LogSpinDash("Start rejected: grabbing character");
            return false;
        }

        if (isGrabbed)
        {
            LogSpinDash("Start rejected: grabbed by character");
            return false;
        }

        return true;
    }

    private bool CanJumpByCharacterGrabServer()
    {
        if (!blockJumpWhileGrabbedByCharacter)
            return true;

        if (!IsGrabbedByCharacterServer())
            return true;

        LogLocomotion("Jump blocked: grabbed by character");
        return false;
    }

    private float GetCharacterGrabAdjustedJumpVelocity(float baseJumpVelocity)
    {
        if (!IsGrabbingCharacterServer())
            return baseJumpVelocity;

        float multiplier = Mathf.Max(0f, characterGrabberJumpMultiplier);
        LogLocomotion($"Jump scaled while grabbing multiplier={multiplier:0.###}");
        return baseJumpVelocity * multiplier;
    }

    private bool IsGrabbingCharacterServer()
    {
        return TryGetCharacterGrabStateServer(out bool isGrabbing, out _) && isGrabbing;
    }

    private bool IsGrabbedByCharacterServer()
    {
        return TryGetCharacterGrabStateServer(out _, out bool isGrabbed) && isGrabbed;
    }

    private bool TryGetCharacterGrabStateServer(out bool isGrabbing, out bool isGrabbed)
    {
        isGrabbing = false;
        isGrabbed = false;

        if (!enableCharacterGrabLocomotionRules)
            return false;

        PlayerInteractModule interactModule = ResolveInteractModule();
        if (interactModule == null)
            return false;

        isGrabbing = interactModule.IsGrabbingCharacter;
        isGrabbed = interactModule.IsGrabbedByCharacter;
        return isGrabbing || isGrabbed;
    }

    private bool CanContinueSpinDashByStatusServer()
    {
        PlayerStatusModule statusModule = ResolveStatusModule();
        if (statusModule == null)
            return false;

        if (statusModule.IsEliminated)
            return false;

        return statusModule.CanMove;
    }

    private bool CanUseNormalMovementByStatusServer()
    {
        PlayerStatusModule statusModule = ResolveStatusModule();
        return statusModule == null || statusModule.CanMove;
    }

    private PlayerStatusModule ResolveStatusModule()
    {
        if (_statusModule != null)
            return _statusModule;

        if (_playerHub == null)
            _playerHub = GetComponentInParent<PlayerHub>();

        if (_playerHub != null)
            _statusModule = _playerHub.GetComponentInChildren<PlayerStatusModule>(true);

        if (_statusModule == null)
            _statusModule = GetComponentInParent<PlayerStatusModule>();

        if (_statusModule == null)
            _statusModule = GetComponentInChildren<PlayerStatusModule>(true);

        return _statusModule;
    }

    private PlayerInteractModule ResolveInteractModule()
    {
        if (_interactModule != null)
            return _interactModule;

        if (_playerHub == null)
            _playerHub = GetComponentInParent<PlayerHub>();

        if (_playerHub != null)
            _interactModule = _playerHub.GetComponentInChildren<PlayerInteractModule>(true);

        if (_interactModule == null)
            _interactModule = GetComponentInParent<PlayerInteractModule>();

        if (_interactModule == null)
            _interactModule = GetComponentInChildren<PlayerInteractModule>(true);

        return _interactModule;
    }

    private void ResolveBodyOverlapServer()
    {
        if (_cc == null) return;

        Transform ccTransform = _cc.transform;
        Transform selfRoot = ccTransform.root;
        Vector3 selfCenter = ccTransform.position + Vector3.up * separationProbeHeight;

        int hitCount = Physics.OverlapSphereNonAlloc(
            selfCenter,
            separationProbeRadius,
            _bodyOverlapHits,
            bodyBlockerMask,
            QueryTriggerInteraction.Collide
        );

        Collider[] hits = _bodyOverlapHits;
        if (hitCount >= _bodyOverlapHits.Length)
        {
            hits = Physics.OverlapSphere(
                selfCenter,
                separationProbeRadius,
                bodyBlockerMask,
                QueryTriggerInteraction.Collide
            );
            hitCount = hits != null ? hits.Length : 0;
        }

        if (hitCount <= 0)
            return;

        Vector3 totalPush = Vector3.zero;
        int validCount = 0;
        _processedOverlapRoots.Clear();

        for (int i = 0; i < hitCount; i++)
        {
            Collider hit = hits[i];
            if (hit == null) continue;

            Transform otherRoot = hit.transform.root;
            if (otherRoot == selfRoot)
                continue;

            int rootId = otherRoot.gameObject.GetInstanceID();
            if (!_processedOverlapRoots.Add(rootId))
                continue;

            Vector3 otherCenter = hit.bounds.center;
            Vector3 delta = selfCenter - otherCenter;
            delta.y = 0f;

            float dist = delta.magnitude;

            float otherRadius = Mathf.Max(hit.bounds.extents.x, hit.bounds.extents.z);
            float targetDist = separationProbeRadius + otherRadius + separationPadding;

            if (dist < 0.0001f)
            {
                delta = -ccTransform.forward;
                dist = 0.0001f;
            }
            else
            {
                delta /= dist;
            }

            if (dist >= targetDist)
                continue;

            float pushAmount = targetDist - dist;
            totalPush += delta * pushAmount;
            validCount++;
        }

        if (validCount <= 0)
            return;

        Vector3 push = totalPush / validCount;
        push.y = 0f;
        push = Vector3.ClampMagnitude(push, maxSeparationMove);

        _cc.Move(push);
    }

    private float GetForwardInput(float forwardInput)
    {
        float deadzone = Mathf.Max(0f, moveFacingInputDeadzone);
        if (Mathf.Abs(forwardInput) <= deadzone)
            return 0f;

        return Mathf.Clamp(forwardInput, -1f, 1f);
    }

    private float GetTurnInput(float turnInput)
    {
        float deadzone = Mathf.Max(0f, moveFacingInputDeadzone);
        if (Mathf.Abs(turnInput) <= deadzone)
            return 0f;

        return Mathf.Clamp(turnInput, -1f, 1f);
    }

    private void ApplyTurnInput(float turnInput, float dt)
    {
        if (_cc == null || Mathf.Abs(turnInput) <= 0f)
            return;

        float turnStep = turnInput * Mathf.Max(0f, moveFacingTurnSpeed) * Mathf.Max(0f, yawScale) * dt;
        _cc.transform.Rotate(0f, turnStep, 0f);
    }

    private void CaptureMovementReferenceYawIfNeeded()
    {
        if (_movementReferenceYawCaptured || _cc == null)
            return;

        _movementReferenceYaw = _cc.transform.eulerAngles.y;
        _movementReferenceYawCaptured = true;
    }

    private Vector3 GetMoveFacingDirection(Vector2 moveInput)
    {
        if (_cc == null)
            return Vector3.zero;

        float forwardInput = GetForwardInput(moveInput.y);
        if (Mathf.Abs(forwardInput) <= 0f)
            return Vector3.zero;

        return _cc.transform.forward * forwardInput;
    }

    private bool ShouldApplySprint(bool sprintHeld, bool hasMoveInput, float dt)
    {
        if (!sprintHeld || !hasMoveInput)
        {
            _isSprintingWithStamina = false;
            return false;
        }

        if (!useStaminaForSprint)
        {
            _isSprintingWithStamina = true;
            return true;
        }

        PlayerStaminaModule staminaModule = ResolveStaminaModule();
        if (staminaModule == null)
        {
            _isSprintingWithStamina = allowSprintWhenStaminaModuleMissing;
            return allowSprintWhenStaminaModuleMissing;
        }

        bool wasSprinting = _isSprintingWithStamina;
        if (!CanUseSprintStamina(staminaModule, wasSprinting))
        {
            _isSprintingWithStamina = false;
            return false;
        }

        if (!ShouldConsumeSprintStamina(hasMoveInput) || TryConsumeSprintStamina(staminaModule, dt))
        {
            _isSprintingWithStamina = true;
            return true;
        }

        _isSprintingWithStamina = false;
        return false;
    }

    private PlayerStaminaModule ResolveStaminaModule()
    {
        if (_playerHub == null)
            _playerHub = GetComponentInParent<PlayerHub>();

        return _playerHub != null ? _playerHub.StaminaModule : null;
    }

    private bool CanUseSprintStamina(PlayerStaminaModule staminaModule, bool isCurrentlySprinting)
    {
        if (staminaModule == null)
            return allowSprintWhenStaminaModuleMissing;

        float requiredStamina = isCurrentlySprinting ? sprintMinimumStaminaToContinue : sprintMinimumStaminaToStart;
        return staminaModule.ServerCanSpendStamina(Mathf.Max(0f, requiredStamina));
    }

    private bool ShouldConsumeSprintStamina(bool hasMoveInput)
    {
        return hasMoveInput || !consumeSprintStaminaOnlyWhileMoving;
    }

    private bool TryConsumeSprintStamina(PlayerStaminaModule staminaModule, float dt)
    {
        if (staminaModule == null)
            return allowSprintWhenStaminaModuleMissing;

        float spendAmount = Mathf.Max(0f, sprintStaminaCostPerSecond) * Mathf.Max(0f, dt);
        if (spendAmount <= 0f)
            return true;

        return staminaModule.ServerTrySpendStamina(spendAmount);
    }

    private bool ShouldAllowJumpWithStamina()
    {
        if (!useStaminaForJump)
            return true;

        if (Mathf.Max(0f, jumpStaminaCost) <= 0f)
            return true;

        PlayerStaminaModule staminaModule = ResolveStaminaModule();
        if (staminaModule == null)
            return allowJumpWhenStaminaModuleMissing;

        if (!CanUseJumpStamina(staminaModule))
            return false;

        return TryConsumeJumpStamina(staminaModule);
    }

    private bool CanUseJumpStamina(PlayerStaminaModule staminaModule)
    {
        if (Mathf.Max(0f, jumpStaminaCost) <= 0f)
            return true;

        if (staminaModule == null)
            return allowJumpWhenStaminaModuleMissing;

        return staminaModule.ServerCanSpendStamina(Mathf.Max(0f, jumpMinimumStaminaToStart));
    }

    private bool TryConsumeJumpStamina(PlayerStaminaModule staminaModule)
    {
        float spendAmount = Mathf.Max(0f, jumpStaminaCost);
        if (spendAmount <= 0f)
            return true;

        if (staminaModule == null)
            return allowJumpWhenStaminaModuleMissing;

        return staminaModule.ServerTrySpendStamina(spendAmount);
    }

    private void RotateTowardsMoveDirection(Vector3 moveDirection, float dt)
    {
        if (_cc == null || moveDirection.sqrMagnitude <= 0.0001f)
            return;

        float currentYaw = _cc.transform.eulerAngles.y;
        float targetYaw = Quaternion.LookRotation(moveDirection.normalized, Vector3.up).eulerAngles.y;
        float nextYaw = Mathf.MoveTowardsAngle(currentYaw, targetYaw, Mathf.Max(0f, moveFacingTurnSpeed) * dt);
        _cc.transform.rotation = Quaternion.Euler(0f, nextYaw, 0f);
    }

    private float GetExternalMoveSpeedMultiplier()
    {
        if (_externalMoveSpeedMultipliers.Count <= 0)
            return 1f;

        float multiplier = 1f;
        foreach (float value in _externalMoveSpeedMultipliers.Values)
        {
            if (!IsFiniteFloat(value))
                continue;

            multiplier *= value;
            if (!IsFiniteFloat(multiplier))
                return MaxExternalMoveSpeedMultiplier;

            multiplier = ClampExternalMoveSpeedMultiplier(multiplier);
        }

        return ClampExternalMoveSpeedMultiplier(multiplier);
    }

    private static float ClampExternalMoveSpeedMultiplier(float multiplier)
    {
        if (!IsFiniteFloat(multiplier))
            return 1f;

        return Mathf.Clamp(multiplier, MinExternalMoveSpeedMultiplier, MaxExternalMoveSpeedMultiplier);
    }

    private static bool IsFiniteFloat(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }

    private static bool IsFinitePositive(float value)
    {
        return IsFiniteFloat(value) && value > 0f;
    }

    private static bool IsFiniteVector(Vector3 value)
    {
        return IsFiniteFloat(value.x) && IsFiniteFloat(value.y) && IsFiniteFloat(value.z);
    }

    private static float GetFiniteNonNegative(float value)
    {
        return IsFiniteFloat(value) ? Mathf.Max(0f, value) : 0f;
    }

    private static bool IsServerActive()
    {
        NetworkManager networkManager = NetworkManager.Singleton;
        return networkManager != null && networkManager.IsServer;
    }

    private void LogSpinDashVisual(string message)
    {
        if (!enableSpinDashDebugLogs)
            return;

        Debug.Log($"[SpinDashVisual] {message}", this);
    }

    private void LogSpinDashVisualWarning(string message)
    {
        if (!enableSpinDashDebugLogs)
            return;

        Debug.LogWarning($"[SpinDashVisual] {message}", this);
    }

    private void StoreSpinDashFeedbackOriginalRotations()
    {
        _spinDashFeedbackOriginalLocalRotations.Clear();

        for (int i = 0; i < _spinDashFeedbackVisualRoots.Count; i++)
        {
            Transform root = _spinDashFeedbackVisualRoots[i];
            _spinDashFeedbackOriginalLocalRotations.Add(root != null ? root.localRotation : Quaternion.identity);
        }
    }

    private void ApplySpinDashFeedbackRotation(float angle)
    {
        if (_spinDashFeedbackVisualRoots.Count != _spinDashFeedbackOriginalLocalRotations.Count)
            return;

        for (int i = 0; i < _spinDashFeedbackVisualRoots.Count; i++)
        {
            Transform root = _spinDashFeedbackVisualRoots[i];
            if (root == null)
                continue;

            Quaternion offset = GetSpinDashVisualRotationOffset(root, angle);
            root.localRotation = offset * _spinDashFeedbackOriginalLocalRotations[i];
        }
    }

    private void ApplySpinDashDizzyWobble(float wobbleAngle)
    {
        if (_spinDashFeedbackVisualRoots.Count != _spinDashFeedbackOriginalLocalRotations.Count)
            return;

        Quaternion offset = Quaternion.Euler(0f, 0f, wobbleAngle);
        for (int i = 0; i < _spinDashFeedbackVisualRoots.Count; i++)
        {
            Transform root = _spinDashFeedbackVisualRoots[i];
            if (root == null)
                continue;

            root.localRotation = _spinDashFeedbackOriginalLocalRotations[i] * offset;
        }
    }

    private bool AreSpinDashFeedbackRootsReady()
    {
        return _spinDashFeedbackVisualRoots.Count > 0 &&
               _spinDashFeedbackVisualRoots.Count == _spinDashFeedbackOriginalLocalRotations.Count;
    }

    private string GetSpinDashFeedbackRootsLabel()
    {
        return GetSpinDashFeedbackRootsLabel(_spinDashFeedbackVisualRoots);
    }

    private static string GetSpinDashFeedbackRootsLabel(List<Transform> roots)
    {
        if (roots == null || roots.Count <= 0)
            return "<none>";

        List<string> names = new List<string>(roots.Count);
        for (int i = 0; i < roots.Count; i++)
        {
            Transform root = roots[i];
            if (root != null)
                names.Add(root.name);
        }

        return names.Count > 0 ? string.Join(",", names) : "<none>";
    }

    private Quaternion GetSpinDashVisualRotationOffset(Transform root, float angle)
    {
        Vector3 axisInParentSpace = GetSpinDashVisualAxisInParentSpace(root);
        if (axisInParentSpace.sqrMagnitude <= 0.0001f)
            axisInParentSpace = Vector3.up;

        return Quaternion.AngleAxis(angle, axisInParentSpace.normalized);
    }

    private Vector3 GetSpinDashVisualAxisInParentSpace(Transform root)
    {
        switch (spinDashVisualRotationAxis)
        {
            case SpinDashVisualRotationAxis.ParentUp:
                return Vector3.up;

            case SpinDashVisualRotationAxis.LocalUp:
                return Vector3.up;

            case SpinDashVisualRotationAxis.LocalForward:
                return Vector3.forward;

            case SpinDashVisualRotationAxis.LocalRight:
                return Vector3.right;

            case SpinDashVisualRotationAxis.WorldUp:
            default:
                return GetWorldAxisInParentSpace(root, Vector3.up);
        }
    }

    private static Vector3 GetWorldAxisInParentSpace(Transform root, Vector3 worldAxis)
    {
        if (root == null)
            return worldAxis;

        Transform parent = root.parent;
        if (parent == null)
            return worldAxis;

        Vector3 axisInParentSpace = parent.InverseTransformDirection(worldAxis);
        if (axisInParentSpace.sqrMagnitude <= 0.0001f)
            return worldAxis;

        return axisInParentSpace.normalized;
    }

    private void LogSpinDash(string message)
    {
        if (!enableSpinDashDebugLogs)
            return;

        Debug.Log($"[SpinDash] {message}", this);
    }

    private void LogLocomotion(string message)
    {
        if (!enableSpinDashDebugLogs)
            return;

        Debug.Log($"[Locomotion] {message}", this);
    }

    private void OnValidate()
    {
        sprintStaminaCostPerSecond = Mathf.Max(0f, sprintStaminaCostPerSecond);
        sprintMinimumStaminaToStart = Mathf.Max(0f, sprintMinimumStaminaToStart);
        sprintMinimumStaminaToContinue = Mathf.Max(0f, sprintMinimumStaminaToContinue);
        jumpStaminaCost = Mathf.Max(0f, jumpStaminaCost);
        jumpMinimumStaminaToStart = Mathf.Max(0f, jumpMinimumStaminaToStart);
        characterGrabberJumpMultiplier = Mathf.Max(0f, characterGrabberJumpMultiplier);
        spinDashStaminaCost = GetFiniteNonNegative(spinDashStaminaCost);
        spinDashDuration = GetFiniteNonNegative(spinDashDuration);
        spinDashSpeed = GetFiniteNonNegative(spinDashSpeed);
        spinDashCooldown = GetFiniteNonNegative(spinDashCooldown);
        spinDashStunDuration = GetFiniteNonNegative(spinDashStunDuration);
        spinDashDizzyFeedbackDuration = GetFiniteNonNegative(spinDashDizzyFeedbackDuration);
        spinDashDizzyWobbleAngle = GetFiniteNonNegative(spinDashDizzyWobbleAngle);
        spinDashDizzyWobbleSpeed = GetFiniteNonNegative(spinDashDizzyWobbleSpeed);
        spinDashVisualRotationDegreesPerSecond = GetFiniteNonNegative(spinDashVisualRotationDegreesPerSecond);
        spinDashVfxYOffset = GetFiniteNonNegative(spinDashVfxYOffset);
        spinDashStartVfxLifetime = GetFiniteNonNegative(spinDashStartVfxLifetime);
        spinDashHitVfxLifetime = GetFiniteNonNegative(spinDashHitVfxLifetime);
        spinDashHitRadius = GetFiniteNonNegative(spinDashHitRadius);
        spinDashHitForwardOffset = GetFiniteNonNegative(spinDashHitForwardOffset);
        spinDashHitUpOffset = GetFiniteNonNegative(spinDashHitUpOffset);
        spinDashHitImpulse = GetFiniteNonNegative(spinDashHitImpulse);
        spinDashHitUpImpulse = GetFiniteNonNegative(spinDashHitUpImpulse);
        spinDashHitMaxTargetsPerDash = Mathf.Max(0, spinDashHitMaxTargetsPerDash);
    }

}
