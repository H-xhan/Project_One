using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public class PlayerLocomotionModule : NetworkBehaviour
{
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

    [SerializeField, Tooltip("SpinDash 중 회전시킬 비주얼 루트입니다. 비워두면 안전한 child를 자동 탐색하거나 회전을 생략합니다.")]
    private Transform spinDashVisualRoot;

    [SerializeField, Tooltip("SpinDash 종료 후 어지러움 흔들림 피드백이 지속되는 시간입니다.")]
    private float spinDashDizzyFeedbackDuration = 0.7f;

    [SerializeField, Tooltip("어지러움 피드백 중 visual child가 좌우로 흔들리는 각도입니다.")]
    private float spinDashDizzyWobbleAngle = 8f;

    [SerializeField, Tooltip("어지러움 피드백 중 visual child가 흔들리는 속도입니다.")]
    private float spinDashDizzyWobbleSpeed = 18f;

    [SerializeField, Tooltip("SpinDash 시작/종료 디버그 로그를 출력할지 여부입니다.")]
    private bool enableSpinDashDebugLogs = false;

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

    private CharacterController _cc;
    private Vector3 _planarVelocity; // 수평 속도 (X, Z)
    private float _verticalVelocity; // 수직 속도 (Y)
    private readonly Collider[] _bodyOverlapHits = new Collider[BodyOverlapBufferSize];
    private readonly HashSet<int> _processedOverlapRoots = new HashSet<int>(BodyOverlapBufferSize);
    private readonly Dictionary<string, float> _externalMoveSpeedMultipliers = new Dictionary<string, float>();
    private float _movementReferenceYaw;
    private bool _movementReferenceYawCaptured;
    private PlayerHub _playerHub;
    private PlayerStatusModule _statusModule;
    private bool _isSprintingWithStamina;
    private bool _isSpinDashing;
    private Vector3 _spinDashDirection;
    private float _spinDashEndTime;
    private float _spinDashCooldownUntil;
    private Transform _resolvedSpinDashVisualRoot;
    private bool _isSpinDashVisualFeedbackActive;
    private float _spinDashVisualFeedbackEndTime;
    private float _spinDashVisualFeedbackElapsed;
    private bool _isSpinDashDizzyFeedbackActive;
    private float _spinDashDizzyFeedbackEndTime;
    private Transform _spinDashFeedbackVisualRoot;
    private Quaternion _spinDashFeedbackOriginalLocalRotation;
    private bool _hasSpinDashFeedbackOriginalLocalRotation;

    public bool IsGrounded => _cc != null && _cc.isGrounded;
    public float PlanarSpeed => new Vector2(_planarVelocity.x, _planarVelocity.z).magnitude;
    public bool IsSpinDashing => _isSpinDashing;
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

    private void Update()
    {
        TickSpinDashFeedbackLocal(Time.deltaTime);
    }

    private void OnDisable()
    {
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

            if (canUseNormalInputMovement && jumpPressed && ShouldAllowJumpWithStamina())
            {
                _verticalVelocity = Mathf.Sqrt(jumpHeight * -2f * gravity);
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
        ResolveBodyOverlapServer();

        return didJump;
    }
    public void ResetMotionServer()
    {
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

        TriggerSpinDashVisualFeedback(duration);
        LogSpinDash($"started. duration:{duration:0.###}, cooldown:{cooldown:0.###}, direction:{_spinDashDirection}");
    }

    private void TickSpinDashServer()
    {
        if (!_isSpinDashing)
            return;

        _planarVelocity = _spinDashDirection * GetFiniteNonNegative(spinDashSpeed);
    }

    private void FinishSpinDashServer(bool applyStun)
    {
        if (!_isSpinDashing)
        {
            StopSpinDashVisualFeedback();
            return;
        }

        _isSpinDashing = false;
        _spinDashDirection = Vector3.zero;
        _spinDashEndTime = 0f;
        _planarVelocity = Vector3.zero;
        _isSprintingWithStamina = false;

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
        if (!enableSpinDashVisualFeedback)
            return;

        float validDuration = GetFiniteNonNegative(duration);
        if (validDuration <= 0f)
            return;

        if (IsSpawned)
        {
            PlaySpinDashVisualFeedbackClientRpc(validDuration);
            return;
        }

        BeginSpinDashVisualFeedbackLocal(validDuration);
    }

    private void TriggerSpinDashDizzyFeedback(float duration)
    {
        if (!enableSpinDashVisualFeedback)
            return;

        float validDuration = GetFiniteNonNegative(duration);
        if (validDuration <= 0f)
            return;

        if (IsSpawned)
        {
            PlaySpinDashDizzyFeedbackClientRpc(validDuration);
            return;
        }

        BeginSpinDashDizzyFeedbackLocal(validDuration);
    }

    [ClientRpc]
    private void PlaySpinDashVisualFeedbackClientRpc(float duration)
    {
        BeginSpinDashVisualFeedbackLocal(duration);
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
        if (!enableSpinDashVisualFeedback || !spinDashRotateVisual)
            return;

        if (!TryPrepareSpinDashFeedbackVisualRoot(out Transform visualRoot))
            return;

        _spinDashFeedbackVisualRoot = visualRoot;
        _isSpinDashDizzyFeedbackActive = false;
        _isSpinDashVisualFeedbackActive = true;
        _spinDashVisualFeedbackElapsed = 0f;
        _spinDashVisualFeedbackEndTime = Time.time + GetFiniteNonNegative(duration);
        _spinDashFeedbackVisualRoot.localRotation = _spinDashFeedbackOriginalLocalRotation;
    }

    private void BeginSpinDashDizzyFeedbackLocal(float duration)
    {
        if (!enableSpinDashVisualFeedback)
            return;

        if (!TryPrepareSpinDashFeedbackVisualRoot(out Transform visualRoot))
            return;

        _spinDashFeedbackVisualRoot = visualRoot;
        _isSpinDashVisualFeedbackActive = false;
        _isSpinDashDizzyFeedbackActive = true;
        _spinDashDizzyFeedbackEndTime = Time.time + GetFiniteNonNegative(duration);
        _spinDashFeedbackVisualRoot.localRotation = _spinDashFeedbackOriginalLocalRotation;
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
        _spinDashFeedbackVisualRoot.localRotation =
            _spinDashFeedbackOriginalLocalRotation * Quaternion.Euler(0f, rotationAngle, 0f);
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
        _spinDashFeedbackVisualRoot.localRotation =
            _spinDashFeedbackOriginalLocalRotation * Quaternion.Euler(0f, 0f, wobble);
    }

    private void StopSpinDashFeedbackLocal()
    {
        _isSpinDashVisualFeedbackActive = false;
        _isSpinDashDizzyFeedbackActive = false;
        _spinDashVisualFeedbackElapsed = 0f;
        _spinDashVisualFeedbackEndTime = 0f;
        _spinDashDizzyFeedbackEndTime = 0f;
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

        if (!TryResolveSpinDashFeedbackVisualRoot(out visualRoot))
            return false;

        bool visualRootChanged = _spinDashFeedbackVisualRoot != visualRoot;
        _spinDashFeedbackVisualRoot = visualRoot;
        if (visualRootChanged || !_hasSpinDashFeedbackOriginalLocalRotation)
        {
            _spinDashFeedbackOriginalLocalRotation = visualRoot.localRotation;
            _hasSpinDashFeedbackOriginalLocalRotation = true;
        }

        return true;
    }

    private bool EnsureSpinDashFeedbackVisualRoot()
    {
        if (_spinDashFeedbackVisualRoot != null)
            return true;

        return TryPrepareSpinDashFeedbackVisualRoot(out _spinDashFeedbackVisualRoot);
    }

    private void RestoreSpinDashFeedbackVisualLocal()
    {
        if (_hasSpinDashFeedbackOriginalLocalRotation && _spinDashFeedbackVisualRoot != null)
            _spinDashFeedbackVisualRoot.localRotation = _spinDashFeedbackOriginalLocalRotation;
    }

    private bool TryResolveSpinDashFeedbackVisualRoot(out Transform visualRoot)
    {
        visualRoot = null;

        if (!IsUnsafeSpinDashVisualRoot(spinDashVisualRoot))
        {
            visualRoot = spinDashVisualRoot;
            return true;
        }

        if (!IsUnsafeSpinDashVisualRoot(_resolvedSpinDashVisualRoot))
        {
            visualRoot = _resolvedSpinDashVisualRoot;
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

            _resolvedSpinDashVisualRoot = candidate;
            visualRoot = candidate;
            return true;
        }

        return false;
    }

    private bool IsUnsafeSpinDashVisualRoot(Transform candidate)
    {
        if (candidate == null || _cc == null)
            return true;

        Transform ccTransform = _cc.transform;
        Transform playerRoot = transform.root;
        if (candidate == ccTransform || candidate == transform || candidate == ccTransform.root || candidate == playerRoot)
            return true;

        if (!candidate.IsChildOf(ccTransform))
            return true;

        if (candidate.GetComponent<CharacterController>() != null ||
            candidate.GetComponent<Rigidbody>() != null ||
            candidate.GetComponent<NetworkObject>() != null)
            return true;

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

    private void LogSpinDash(string message)
    {
        if (!enableSpinDashDebugLogs)
            return;

        Debug.Log($"[SpinDash] {message}", this);
    }

    private void OnValidate()
    {
        sprintStaminaCostPerSecond = Mathf.Max(0f, sprintStaminaCostPerSecond);
        sprintMinimumStaminaToStart = Mathf.Max(0f, sprintMinimumStaminaToStart);
        sprintMinimumStaminaToContinue = Mathf.Max(0f, sprintMinimumStaminaToContinue);
        jumpStaminaCost = Mathf.Max(0f, jumpStaminaCost);
        jumpMinimumStaminaToStart = Mathf.Max(0f, jumpMinimumStaminaToStart);
        spinDashStaminaCost = GetFiniteNonNegative(spinDashStaminaCost);
        spinDashDuration = GetFiniteNonNegative(spinDashDuration);
        spinDashSpeed = GetFiniteNonNegative(spinDashSpeed);
        spinDashCooldown = GetFiniteNonNegative(spinDashCooldown);
        spinDashStunDuration = GetFiniteNonNegative(spinDashStunDuration);
        spinDashDizzyFeedbackDuration = GetFiniteNonNegative(spinDashDizzyFeedbackDuration);
        spinDashDizzyWobbleAngle = GetFiniteNonNegative(spinDashDizzyWobbleAngle);
        spinDashDizzyWobbleSpeed = GetFiniteNonNegative(spinDashDizzyWobbleSpeed);
        spinDashVisualRotationDegreesPerSecond = GetFiniteNonNegative(spinDashVisualRotationDegreesPerSecond);
    }

}
