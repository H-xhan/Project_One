using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerNetworkController : NetworkBehaviour
{
    [Tooltip("이동 속도")]
    [SerializeField] private float moveSpeed = 4.5f;

    [Tooltip("회전 감도(마우스 X)")]
    [SerializeField] private float yawSensitivity = 5f;

    [Tooltip("점프 높이(미터)")]
    [SerializeField] private float jumpHeight = 1.4f;

    [Tooltip("중력(양수)")]
    [SerializeField] private float gravity = 25f;

    [Tooltip("지면에 붙어있게 만드는 하강 보정값")]
    [SerializeField] private float groundedStickVelocity = -2f;

    private CharacterController _controller;
    private float _verticalVelocity;
    private bool _missingControllerLogged;

    private void Awake()
    {
        _controller = GetComponent<CharacterController>();
    }

    public override void OnNetworkSpawn()
    {
        Debug.Log($"[Player] IsOwner={IsOwner} OwnerClientId={OwnerClientId} LocalClientId={NetworkManager.Singleton.LocalClientId}");

        if (IsOwner)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
    }

    private void Update()
    {
        if (!IsOwner) return;

        if (_controller == null)
        {
            if (!_missingControllerLogged)
            {
                _missingControllerLogged = true;
                Debug.LogError("[Player] CharacterController is missing on the same GameObject.");
            }
            return;
        }

        RotateByMouse();

        Vector2 move = ReadMoveInput();
        Move(move);
    }

    private void RotateByMouse()
    {
        if (Mouse.current == null) return;

        float mouseX = Mouse.current.delta.ReadValue().x;
        float yaw = mouseX * yawSensitivity * Time.deltaTime;
        transform.Rotate(0f, yaw, 0f, Space.World);
    }

    private Vector2 ReadMoveInput()
    {
        Vector2 move = Vector2.zero;

        if (Keyboard.current == null) return move;

        if (Keyboard.current.wKey.isPressed) move.y += 1f;
        if (Keyboard.current.sKey.isPressed) move.y -= 1f;
        if (Keyboard.current.dKey.isPressed) move.x += 1f;
        if (Keyboard.current.aKey.isPressed) move.x -= 1f;

        return move;
    }

    private void Move(Vector2 move)
    {
        bool grounded = _controller.isGrounded;

        if (grounded && _verticalVelocity < 0f)
            _verticalVelocity = groundedStickVelocity;

        if (Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame && grounded)
            _verticalVelocity = Mathf.Sqrt(2f * gravity * jumpHeight);

        _verticalVelocity -= gravity * Time.deltaTime;

        Vector3 dir = (transform.right * move.x + transform.forward * move.y);
        if (dir.sqrMagnitude > 1f) dir.Normalize();

        Vector3 motion = dir * moveSpeed;
        motion.y = _verticalVelocity;

        _controller.Move(motion * Time.deltaTime);
    }
}
