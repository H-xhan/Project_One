using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(CharacterController))]
public class PlayerNetworkController : NetworkBehaviour
{
    [Header("Move")]
    [Tooltip("걷기 속도")]
    [SerializeField] private float walkSpeed = 4.5f;

    [Tooltip("달리기 속도(Shift)")]
    [SerializeField] private float sprintSpeed = 7.0f;

    [Header("Rotate")]
    [Tooltip("Yaw 회전 감도(마우스 X)")]
    [SerializeField] private float yawSensitivity = 180f;

    [Header("Jump/Gravity")]
    [Tooltip("점프 높이")]
    [SerializeField] private float jumpHeight = 1.4f;

    [Tooltip("중력(양수로 두세요)")]
    [SerializeField] private float gravity = 25f;

    [Tooltip("지상에서 수직 속도 고정값(바닥 붙이기용, 음수 권장)")]
    [SerializeField] private float groundedStickVelocity = -2f;

    [Tooltip("코요테 타임(땅 떠난 후 점프 허용 시간)")]
    [SerializeField] private float coyoteTime = 0.12f;

    [Tooltip("점프 버퍼(점프 입력 선입력 허용 시간)")]
    [SerializeField] private float jumpBufferTime = 0.12f;

    [Header("Animation")]
    [Tooltip("애니메이션 드라이버(비우면 자동 탐색)")]
    [SerializeField] private PlayerAnimDriver animDriver;

    [Header("Cursor")]
    [Tooltip("스폰 시 커서를 잠그고 숨김")]
    [SerializeField] private bool lockCursorOnSpawn = true;

    private CharacterController _controller;
    private float _verticalVelocity;

    private float _lastGroundedTime = -999f;
    private float _lastJumpPressedTime = -999f;

    private bool _cursorLocked;

    private void Awake()
    {
        _controller = GetComponent<CharacterController>();

        if (animDriver == null)
            animDriver = GetComponent<PlayerAnimDriver>();
    }

    public override void OnNetworkSpawn()
    {
        if (!IsOwner) return;

        if (lockCursorOnSpawn)
            SetCursorLocked(true);
    }

    private void OnDisable()
    {
        if (!IsOwner) return;
        SetCursorLocked(false);
    }

    private void Update()
    {
        if (!IsOwner) return;

        HandleCursorToggle();

        if (_cursorLocked)
            RotateByMouseX();

        Vector2 moveInput = ReadMoveInput();
        bool isSprinting = IsSprintHeld();

        TickGroundedTimers();
        TickJumpBuffer();

        Move(moveInput, isSprinting);
        TryConsumeJump();
        ApplyGravityAndMove(moveInput, isSprinting);
        PushAnim(moveInput, isSprinting);
    }

    private void HandleCursorToggle()
    {
        if (Keyboard.current == null) return;

        if (Keyboard.current.escapeKey.wasPressedThisFrame)
            SetCursorLocked(!_cursorLocked);
    }

    private void SetCursorLocked(bool locked)
    {
        _cursorLocked = locked;
        Cursor.visible = !locked;
        Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
    }

    private void RotateByMouseX()
    {
        if (Mouse.current == null) return;

        float mouseX = Mouse.current.delta.ReadValue().x;
        float yaw = mouseX * yawSensitivity * Time.deltaTime;
        transform.Rotate(0f, yaw, 0f);
    }

    private Vector2 ReadMoveInput()
    {
        if (Keyboard.current == null) return Vector2.zero;

        float x = 0f;
        float y = 0f;

        if (Keyboard.current.aKey.isPressed) x -= 1f;
        if (Keyboard.current.dKey.isPressed) x += 1f;
        if (Keyboard.current.sKey.isPressed) y -= 1f;
        if (Keyboard.current.wKey.isPressed) y += 1f;

        Vector2 v = new Vector2(x, y);
        return (v.sqrMagnitude > 1f) ? v.normalized : v;
    }

    private bool IsSprintHeld()
    {
        return Keyboard.current != null && Keyboard.current.leftShiftKey.isPressed;
    }

    private void TickGroundedTimers()
    {
        if (_controller.isGrounded)
            _lastGroundedTime = Time.time;
    }

    private void TickJumpBuffer()
    {
        if (Keyboard.current == null) return;

        if (Keyboard.current.spaceKey.wasPressedThisFrame)
            _lastJumpPressedTime = Time.time;
    }

    private void Move(Vector2 moveInput, bool isSprinting)
    {
        // 수평 이동은 ApplyGravityAndMove에서 같이 처리함 (함수 분리용)
    }

    private void TryConsumeJump()
    {
        bool buffered = (Time.time - _lastJumpPressedTime) <= jumpBufferTime;
        bool coyote = (Time.time - _lastGroundedTime) <= coyoteTime;

        if (!buffered || !coyote) return;

        _verticalVelocity = Mathf.Sqrt(jumpHeight * 2f * gravity);
        _lastJumpPressedTime = -999f;
        _lastGroundedTime = -999f;

        if (animDriver != null)
            animDriver.PlayJump();
    }

    private void ApplyGravityAndMove(Vector2 moveInput, bool isSprinting)
    {
        if (_controller.isGrounded && _verticalVelocity < 0f)
            _verticalVelocity = groundedStickVelocity;

        _verticalVelocity -= gravity * Time.deltaTime;

        Vector3 dir = transform.right * moveInput.x + transform.forward * moveInput.y;
        if (dir.sqrMagnitude > 1f) dir.Normalize();

        float speed = isSprinting ? sprintSpeed : walkSpeed;

        Vector3 motion = dir * speed;
        motion.y = _verticalVelocity;

        _controller.Move(motion * Time.deltaTime);
    }

    private void PushAnim(Vector2 moveInput, bool isSprinting)
    {
        if (animDriver == null) return;

        float speed = isSprinting ? sprintSpeed : walkSpeed;
        float planar = (new Vector3(moveInput.x, 0f, moveInput.y)).magnitude * speed;

        animDriver.SetMoveSpeed(planar, sprintSpeed);
    }
}
