using UnityEngine;

// [삭제됨] [RequireComponent(typeof(CharacterController))]  <-- 이 줄이 범인이었습니다!
public class PlayerLocomotionModule : MonoBehaviour
{
    [Header("Move")]
    [SerializeField] private float walkSpeed = 4f;
    [SerializeField] private float sprintSpeed = 7f;
    [SerializeField] private float acceleration = 10f;

    [Header("Jump/Gravity")]
    [SerializeField] private float jumpHeight = 1.5f;
    [SerializeField] private float gravity = -25f;
    [SerializeField] private float stickToGroundForce = -5f;

    [Header("Rotate")]
    [SerializeField] private float yawScale = 1f;

    private CharacterController _cc;
    private Vector3 _planarVelocity;
    private float _verticalVelocity;

    public bool IsGrounded => _cc != null && _cc.isGrounded;
    public float PlanarSpeed => new Vector2(_planarVelocity.x, _planarVelocity.z).magnitude;

    private void Awake()
    {
        // 부모에서 찾으니까 RequireComponent 없어도 됩니다.
        _cc = GetComponentInParent<CharacterController>();
    }

    public bool TickServer(Vector2 moveInput, float yawDelta, bool jumpPressed, bool sprintHeld)
    {
        if (_cc == null) return false;
        bool didJump = false;

        float dt = Time.deltaTime;

        if (Mathf.Abs(yawDelta) > 0.001f)
            _cc.transform.Rotate(0f, yawDelta * yawScale, 0f);

        if (IsGrounded)
        {
            if (_verticalVelocity < 0f)
                _verticalVelocity = stickToGroundForce;

            if (jumpPressed)
            {
                _verticalVelocity = Mathf.Sqrt(jumpHeight * -2f * gravity);
                didJump = true;
            }
        }

        _verticalVelocity += gravity * dt;

        float targetSpeed = sprintHeld ? sprintSpeed : walkSpeed;
        if (moveInput.sqrMagnitude == 0) targetSpeed = 0;

        Vector3 desired = (_cc.transform.right * moveInput.x + _cc.transform.forward * moveInput.y);
        if (desired.sqrMagnitude > 1f) desired.Normalize();
        desired *= targetSpeed;

        _planarVelocity = Vector3.Lerp(_planarVelocity, desired, 1f - Mathf.Exp(-acceleration * dt));

        Vector3 motion = _planarVelocity;
        motion.y = _verticalVelocity;

        _cc.Move(motion * dt);

        return didJump;
    }
}