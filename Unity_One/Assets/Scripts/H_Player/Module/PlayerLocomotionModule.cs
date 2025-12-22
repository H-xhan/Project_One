using UnityEngine;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

public sealed class PlayerLocomotionModule
{
    private readonly PlayerHub _hub;

    private float _verticalVelocity;
    private float _lastGroundedTime;
    private float _lastJumpPressedTime = -999f;

    private Vector3 _externalVelocity;
    private float _externalTimeLeft;
    private float _externalBaseDuration;

    public PlayerLocomotionModule(PlayerHub hub)
    {
        _hub = hub;
    }

    public void Tick(Vector2 moveInput, bool sprint, bool jumpPressedThisFrame, out float planarSpeed, out bool jumpedThisFrame)
    {
        jumpedThisFrame = false;

        if (jumpPressedThisFrame)
            _lastJumpPressedTime = Time.time;

        RotateByMouseX();

        bool grounded = _hub.CC != null && _hub.CC.isGrounded;

        if (grounded)
        {
            _lastGroundedTime = Time.time;
            if (_verticalVelocity < 0f)
                _verticalVelocity = _hub.GroundedStickVelocity;
        }
        else
        {
            _verticalVelocity -= _hub.Gravity * Time.deltaTime;
        }

        bool canCoyote = (Time.time - _lastGroundedTime) <= _hub.CoyoteTime;
        bool hasJumpBuffered = (Time.time - _lastJumpPressedTime) <= _hub.JumpBufferTime;

        if (hasJumpBuffered && canCoyote)
        {
            _lastJumpPressedTime = -999f;
            _verticalVelocity = Mathf.Sqrt(_hub.JumpHeight * 2f * _hub.Gravity);
            jumpedThisFrame = true;
        }

        Vector3 dir = _hub.Self.right * moveInput.x + _hub.Self.forward * moveInput.y;
        if (dir.sqrMagnitude > 1f) dir.Normalize();

        float speed = sprint ? _hub.SprintSpeed : _hub.WalkSpeed;
        Vector3 motion = dir * speed;

        if (_externalTimeLeft > 0f)
        {
            _externalTimeLeft -= Time.deltaTime;
            motion += _externalVelocity;

            float t = (_externalTimeLeft <= 0f) ? 0f : (_externalTimeLeft / Mathf.Max(0.001f, _externalBaseDuration));
            _externalVelocity *= t;
        }

        motion.y = _verticalVelocity;

        if (_hub.CC != null)
            _hub.CC.Move(motion * Time.deltaTime);

        planarSpeed = new Vector3(moveInput.x, 0f, moveInput.y).magnitude * speed;
    }

    private void RotateByMouseX()
    {
#if ENABLE_INPUT_SYSTEM
        var m = Mouse.current;
        if (m == null) return;

        float mouseX = m.delta.ReadValue().x * _hub.MouseDeltaToAxis;
        float yaw = mouseX * _hub.YawSensitivity * Time.deltaTime;
        _hub.Self.Rotate(0f, yaw, 0f);
#endif
    }

    public void ApplyExternalImpulse(Vector3 impulse, float duration, float baseDuration)
    {
        _externalVelocity = impulse;
        _externalTimeLeft = Mathf.Max(0.01f, duration);
        _externalBaseDuration = Mathf.Max(0.01f, baseDuration);
    }
}
