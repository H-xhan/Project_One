using System.Collections.Generic;
using UnityEngine;

public class PlayerLocomotionModule : MonoBehaviour
{
    [Header("Move Settings")]
    [Tooltip("걷기 이동 속도입니다. 값이 높을수록 기본 이동이 빨라집니다.")]
    [SerializeField] private float walkSpeed = 4f;

    [Tooltip("달리기 이동 속도입니다. 값이 높을수록 sprint 입력 시 더 빠르게 이동합니다.")]
    [SerializeField] private float sprintSpeed = 7f;

    [Tooltip("출발할 때 얼마나 빨리 최고 속도에 도달하는지")]
    [SerializeField] private float acceleration = 15f; // [수정] 기존 10 -> 15 (좀 더 빠르게 출발)

    [Tooltip("멈출 때 얼마나 빨리 정지하는지 (높을수록 칼브레이크)")]
    [SerializeField] private float deceleration = 30f; // [추가] 멈출 때는 2배 더 강력하게!

    [Header("Jump/Gravity")]
    [Tooltip("점프 높이입니다. 값이 높을수록 점프가 더 높아집니다.")]
    [SerializeField] private float jumpHeight = 1.5f;

    [Tooltip("중력 가속도입니다. 더 음수일수록 더 빠르게 떨어집니다.")]
    [SerializeField] private float gravity = -25f;

    [Tooltip("지면에 붙어 있도록 아래로 누르는 힘입니다. 값이 낮을수록 지면 접지가 강해집니다.")]
    [SerializeField] private float stickToGroundForce = -5f;

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
    private float _movementReferenceYaw;
    private bool _movementReferenceYawCaptured;

    public bool IsGrounded => _cc != null && _cc.isGrounded;
    public float PlanarSpeed => new Vector2(_planarVelocity.x, _planarVelocity.z).magnitude;

    private void Awake()
    {
        _cc = GetComponentInParent<CharacterController>();
    }

    public bool TickServer(Vector2 moveInput, float yawDelta, bool jumpPressed, bool sprintHeld)
    {
        if (_cc == null) return false;
        bool didJump = false;
        float dt = Time.deltaTime;
        float forwardInput = GetForwardInput(moveInput.y);
        float turnInput = GetTurnInput(moveInput.x);
        Vector3 inputDir = GetMoveFacingDirection(moveInput);
        bool hasMoveInput = Mathf.Abs(forwardInput) > 0f;

        // 1. 회전 처리 (A/D 탱크 회전)
        if (Mathf.Abs(turnInput) > 0f)
            ApplyTurnInput(turnInput, dt);

        // 2. 점프 및 중력 처리
        bool grounded = IsGrounded;

        if (grounded)
        {
            if (_verticalVelocity > 0f)
                _verticalVelocity = 0f;

            if (_verticalVelocity <= 0f)
                _verticalVelocity = stickToGroundForce;

            if (jumpPressed)
            {
                _verticalVelocity = Mathf.Sqrt(jumpHeight * -2f * gravity);
                didJump = true;
            }
        }

        _verticalVelocity += gravity * dt;

        // 3. 이동 속도 계산 (핵심 수정!)
        float targetSpeed = sprintHeld ? sprintSpeed : walkSpeed;

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

        // 4. 최종 이동 적용
        Vector3 finalMotion = _planarVelocity;
        finalMotion.y = _verticalVelocity;

        _cc.Move(finalMotion * dt);
        ResolveBodyOverlapServer();

        return didJump;
    }
    public void ResetMotionServer()
    {
        _planarVelocity = Vector3.zero;
        _verticalVelocity = 0f;
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

    private void RotateTowardsMoveDirection(Vector3 moveDirection, float dt)
    {
        if (_cc == null || moveDirection.sqrMagnitude <= 0.0001f)
            return;

        float currentYaw = _cc.transform.eulerAngles.y;
        float targetYaw = Quaternion.LookRotation(moveDirection.normalized, Vector3.up).eulerAngles.y;
        float nextYaw = Mathf.MoveTowardsAngle(currentYaw, targetYaw, Mathf.Max(0f, moveFacingTurnSpeed) * dt);
        _cc.transform.rotation = Quaternion.Euler(0f, nextYaw, 0f);
    }

}
