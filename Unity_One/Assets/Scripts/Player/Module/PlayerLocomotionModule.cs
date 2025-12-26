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

    private CharacterController _cc;
    private Vector3 _planarVelocity; // 수평 속도 (X, Z)
    private float _verticalVelocity; // 수직 속도 (Y)

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
        if (IsGrounded)
        {
            if (_verticalVelocity < 0f) _verticalVelocity = stickToGroundForce; // 바닥 밀착

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

        return didJump;
    }
}