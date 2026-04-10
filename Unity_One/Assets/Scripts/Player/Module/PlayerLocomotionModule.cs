using System.Collections.Generic;
using UnityEngine;

public class PlayerLocomotionModule : MonoBehaviour
{
    [Header("Move Settings")]
    [SerializeField] private float walkSpeed = 4f;
    [SerializeField] private float sprintSpeed = 7f;

    [Tooltip("출발할 때 얼마나 빨리 최고 속도에 도달하는지")]
    [SerializeField] private float acceleration = 15f; // [수정] 기존 10 -> 15 (좀 더 빠르게 출발)

    [Tooltip("멈출 때 얼마나 빨리 정지하는지 (높을수록 칼브레이크)")]
    [SerializeField] private float deceleration = 30f; // [추가] 멈출 때는 2배 더 강력하게!

    [Header("Jump/Gravity")]
    [SerializeField] private float jumpHeight = 1.5f;
    [SerializeField] private float gravity = -25f;
    [SerializeField] private float stickToGroundForce = -5f;

    [Header("Rotate")]
    [SerializeField] private float yawScale = 1f;

    [Header("Body Separation")]
    [SerializeField] private LayerMask bodyBlockerMask;
    [SerializeField] private float separationProbeRadius = 0.18f;
    [SerializeField] private float separationProbeHeight = 0.18f;
    [SerializeField] private float separationPadding = 0.02f;
    [SerializeField] private float maxSeparationMove = 0.08f;

    private const int BodyOverlapBufferSize = 16;

    private CharacterController _cc;
    private Vector3 _planarVelocity; // 수평 속도 (X, Z)
    private float _verticalVelocity; // 수직 속도 (Y)
    private readonly Collider[] _bodyOverlapHits = new Collider[BodyOverlapBufferSize];
    private readonly HashSet<int> _processedOverlapRoots = new HashSet<int>(BodyOverlapBufferSize);

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

        // 1. 회전 처리 (마우스)
        if (Mathf.Abs(yawDelta) > 0.001f)
            _cc.transform.Rotate(0f, yawDelta * yawScale, 0f);

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

        // 입력이 없으면 목표 속도는 0
        if (moveInput.sqrMagnitude == 0) targetSpeed = 0;

        // 목표 방향 벡터 계산
        Vector3 inputDir = (_cc.transform.right * moveInput.x + _cc.transform.forward * moveInput.y);
        if (inputDir.sqrMagnitude > 1f) inputDir.Normalize();

        Vector3 desiredVelocity = inputDir * targetSpeed;

        // [핵심] 입력이 있으면 '가속도', 입력이 없으면(멈출 때) '감속도' 적용
        float currentAccel = (moveInput.sqrMagnitude > 0) ? acceleration : deceleration;

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

}
