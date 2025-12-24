using UnityEngine;

public class PlayerLocomotionModule
{
    private readonly PlayerHub _hub;

    private float _verticalVelocity;
    private bool _isGrounded;

    public bool IsGrounded => _isGrounded;

    public PlayerLocomotionModule(PlayerHub hub)
    {
        _hub = hub;
    }

    public void Tick(Vector2 moveInput, bool sprintHeld, bool jumpPressedThisFrame)
    {
        if (_hub.CharacterController == null)
            return;

        float dt = Time.deltaTime;

        UpdateGrounded();

        if (_isGrounded && _verticalVelocity < 0f)
            _verticalVelocity = -2f;

        if (jumpPressedThisFrame && _isGrounded)
        {
            float jumpSpeed = Mathf.Sqrt(2f * _hub.Gravity * _hub.JumpHeight);
            _verticalVelocity = jumpSpeed;
            _hub.AnimModule?.TriggerJump();
        }

        _verticalVelocity += -_hub.Gravity * dt;

        Vector3 moveWorld = GetMoveWorld(moveInput, sprintHeld);
        Vector3 velocity = moveWorld + Vector3.up * _verticalVelocity;

        _hub.CharacterController.Move(velocity * dt);

        if (_hub.CharacterController.velocity.y < -0.1f)
            _isGrounded = _hub.CharacterController.isGrounded;
    }

    private void UpdateGrounded()
    {
        _isGrounded = _hub.CharacterController.isGrounded;
    }

    private Vector3 GetMoveWorld(Vector2 moveInput, bool sprintHeld)
    {
        float speed = sprintHeld ? _hub.SprintSpeed : _hub.WalkSpeed;

        if (moveInput.sqrMagnitude < 0.0001f)
            return Vector3.zero;

        Transform pivot = _hub.CameraPivot != null ? _hub.CameraPivot : _hub.transform;

        Vector3 fwd = pivot.forward;
        fwd.y = 0f;
        fwd.Normalize();

        Vector3 right = pivot.right;
        right.y = 0f;
        right.Normalize();

        Vector3 dir = (right * moveInput.x + fwd * moveInput.y);
        if (dir.sqrMagnitude > 1f) dir.Normalize();

        return dir * speed;
    }
}
