using Unity.Netcode;
using UnityEngine;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

public class PlayerNetworkController : NetworkBehaviour
{
    [Header("Move")]
    [SerializeField] private float walkSpeed = 4.5f;
    [SerializeField] private float sprintSpeed = 7f;

    [Header("Rotate")]
    [SerializeField] private float yawSensitivity = 180f;
    [SerializeField] private float mouseDeltaToAxis = 0.1f;

    [Header("Jump/Gravity")]
    [SerializeField] private float jumpHeight = 1.4f;
    [SerializeField] private float gravity = 25f;
    [SerializeField] private float groundedStickVelocity = -2f;
    [SerializeField] private float coyoteTime = 0.12f;
    [SerializeField] private float jumpBufferTime = 0.12f;

    [Header("Animation")]
    [SerializeField] private PlayerAnimDriver animDriver;

    [Header("Cursor")]
    [SerializeField] private bool lockCursorOnSpawn = true;

    [Header("Attack")]
    [Tooltip("공격 쿨다운(초)")]
    [SerializeField] private float attackCooldown = 0.35f;

    [Tooltip("좌클릭 공격을 Heavy로 취급(넉백/파워)할지")]
    [SerializeField] private bool primaryAttackIsHeavy = true;

    [Tooltip("타격 판정 거리(앞으로)")]
    [SerializeField] private float hitDistance = 1.0f;

    [Tooltip("타격 판정 반지름")]
    [SerializeField] private float hitRadius = 0.6f;

    [Tooltip("타격 대상 레이어 마스크(예: Player)")]
    [SerializeField] private LayerMask hitMask;

    [Tooltip("넉백 힘(가벼운 공격)")]
    [SerializeField] private float knockbackLight = 6f;

    [Tooltip("넉백 힘(강한 공격)")]
    [SerializeField] private float knockbackHeavy = 10f;

    [Tooltip("넉백 지속 시간(초)")]
    [SerializeField] private float knockbackDuration = 0.15f;

    [Header("Interact")]
    [Tooltip("우클릭 상호작용 거리")]
    [SerializeField] private float interactDistance = 2.0f;

    [Tooltip("우클릭 상호작용 레이어 마스크(아이템 레이어)")]
    [SerializeField] private LayerMask interactMask;

    private CharacterController _controller;

    private Vector2 _moveInput;
    private float _verticalVelocity;
    private bool _isSprinting;

    private float _lastGroundedTime;
    private float _lastJumpPressedTime;

    private float _nextAttackTime;

    private Vector3 _externalVelocity;
    private float _externalTimeLeft;

    private void Awake()
    {
        _controller = GetComponent<CharacterController>();
    }

    public override void OnNetworkSpawn()
    {
        if (!IsOwner) return;

        if (lockCursorOnSpawn)
            SetCursorLocked(true);
    }

    private void Update()
    {
        if (!IsOwner) return;

        HandleCursorToggle();

        _moveInput = ReadMoveInput();
        _isSprinting = ReadSprintHeld();
        ReadJumpPressed();

        ReadPrimaryAttackPressed();   // 좌클릭 = 공격
        ReadInteractPressed();        // 우클릭 = 픽업(상호작용)

        RotateByMouseX();
        ApplyGravityAndMove();
        PushAnim();
    }

    private void HandleCursorToggle()
    {
#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
        {
            bool locked = Cursor.lockState == CursorLockMode.Locked;
            SetCursorLocked(!locked);
        }
#endif
    }

    private void SetCursorLocked(bool locked)
    {
        Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !locked;
    }

    private Vector2 ReadMoveInput()
    {
#if ENABLE_INPUT_SYSTEM
        var kb = Keyboard.current;
        if (kb == null) return Vector2.zero;

        float x = 0f;
        if (kb.aKey.isPressed) x -= 1f;
        if (kb.dKey.isPressed) x += 1f;

        float y = 0f;
        if (kb.sKey.isPressed) y -= 1f;
        if (kb.wKey.isPressed) y += 1f;

        return Vector2.ClampMagnitude(new Vector2(x, y), 1f);
#else
        return Vector2.zero;
#endif
    }

    private bool ReadSprintHeld()
    {
#if ENABLE_INPUT_SYSTEM
        var kb = Keyboard.current;
        return kb != null && (kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed);
#else
        return false;
#endif
    }

    private void ReadJumpPressed()
    {
#if ENABLE_INPUT_SYSTEM
        var kb = Keyboard.current;
        if (kb != null && kb.spaceKey.wasPressedThisFrame)
            _lastJumpPressedTime = Time.time;
#endif
    }

    // 좌클릭 = 공격(Primary)
    private void ReadPrimaryAttackPressed()
    {
        if (Time.time < _nextAttackTime) return;

#if ENABLE_INPUT_SYSTEM
        var m = Mouse.current;
        bool pressed = (m != null && m.leftButton.wasPressedThisFrame);
#else
        bool pressed = false;
#endif
        if (!pressed) return;

        _nextAttackTime = Time.time + attackCooldown;

        if (animDriver != null)
            animDriver.PlayPrimaryAttack();

        AttackServerRpc(primaryAttackIsHeavy);
    }

    // 우클릭 = 픽업(상호작용) + PickUp 애니 재생
    private void ReadInteractPressed()
    {
#if ENABLE_INPUT_SYSTEM
        var m = Mouse.current;
        if (m == null) return;

        if (!m.rightButton.wasPressedThisFrame) return;

        if (animDriver != null)
            animDriver.PlayPickUp();

        TryInteractServerRpc();
#endif
    }

    private void RotateByMouseX()
    {
        float mouseX = 0f;

#if ENABLE_INPUT_SYSTEM
        var m = Mouse.current;
        if (m != null)
            mouseX = m.delta.ReadValue().x * mouseDeltaToAxis;
#endif

        float yaw = mouseX * yawSensitivity * Time.deltaTime;
        transform.Rotate(0f, yaw, 0f);
    }

    private void ApplyGravityAndMove()
    {
        bool grounded = _controller.isGrounded;

        if (grounded)
        {
            _lastGroundedTime = Time.time;
            if (_verticalVelocity < 0f)
                _verticalVelocity = groundedStickVelocity;
        }
        else
        {
            _verticalVelocity -= gravity * Time.deltaTime;
        }

        bool canCoyote = (Time.time - _lastGroundedTime) <= coyoteTime;
        bool hasJumpBuffered = (Time.time - _lastJumpPressedTime) <= jumpBufferTime;

        if (hasJumpBuffered && canCoyote)
        {
            _lastJumpPressedTime = -999f;
            _verticalVelocity = Mathf.Sqrt(jumpHeight * 2f * gravity);

            if (animDriver != null) animDriver.PlayJump();
        }

        Vector3 dir = transform.right * _moveInput.x + transform.forward * _moveInput.y;
        if (dir.sqrMagnitude > 1f) dir.Normalize();

        float speed = _isSprinting ? sprintSpeed : walkSpeed;
        Vector3 motion = dir * speed;

        if (_externalTimeLeft > 0f)
        {
            _externalTimeLeft -= Time.deltaTime;
            motion += _externalVelocity;

            float t = (_externalTimeLeft <= 0f) ? 0f : (_externalTimeLeft / knockbackDuration);
            _externalVelocity *= t;
        }

        motion.y = _verticalVelocity;
        _controller.Move(motion * Time.deltaTime);
    }

    private void PushAnim()
    {
        if (animDriver == null) return;

        float speed = _isSprinting ? sprintSpeed : walkSpeed;
        float planar = new Vector3(_moveInput.x, 0f, _moveInput.y).magnitude * speed;

        animDriver.SetMoveSpeed(planar);
    }

    [ServerRpc]
    private void AttackServerRpc(bool isHeavy, ServerRpcParams rpcParams = default)
    {
        Vector3 origin = transform.position + transform.forward * hitDistance;

        Collider[] hits = Physics.OverlapSphere(origin, hitRadius, hitMask, QueryTriggerInteraction.Ignore);
        if (hits == null || hits.Length == 0) return;

        float force = isHeavy ? knockbackHeavy : knockbackLight;

        for (int i = 0; i < hits.Length; i++)
        {
            NetworkObject no = hits[i].GetComponentInParent<NetworkObject>();
            if (no == null) continue;
            if (no == NetworkObject) continue;

            PlayerNetworkController target = no.GetComponent<PlayerNetworkController>();
            if (target == null) continue;

            ulong targetClientId = no.OwnerClientId;

            Vector3 dir = (no.transform.position - transform.position);
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.0001f) dir = transform.forward;
            dir.Normalize();

            ClientRpcParams onlyTargetOwner = new ClientRpcParams
            {
                Send = new ClientRpcSendParams { TargetClientIds = new[] { targetClientId } }
            };

            target.ApplyKnockbackClientRpc(dir * force, knockbackDuration, onlyTargetOwner);

            // “맞은 사람” 피격 애니는 서버에서 트리거 -> NetworkAnimator가 모두에게 동기화
            if (target.animDriver != null)
                target.animDriver.ServerPlayHitReact();
        }
    }

    [ClientRpc]
    private void ApplyKnockbackClientRpc(Vector3 impulse, float duration, ClientRpcParams rpcParams = default)
    {
        if (!IsOwner) return;

        _externalVelocity = impulse;
        _externalTimeLeft = Mathf.Max(0.01f, duration);
    }

    // 우클릭 상호작용 (아이템 잡기 로직은 여기 확장)
    [ServerRpc]
    private void TryInteractServerRpc(ServerRpcParams rpcParams = default)
    {
        Vector3 origin = transform.position + transform.forward * interactDistance;

        Collider[] hits = Physics.OverlapSphere(origin, 0.35f, interactMask, QueryTriggerInteraction.Collide);
        if (hits == null || hits.Length == 0) return;

        // TODO: Item/Grabbable 찾아서 서버 권한으로 잡기(소유권/부모 세팅 등) 구현
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Vector3 origin = transform.position + transform.forward * hitDistance;
        Gizmos.DrawWireSphere(origin, hitRadius);

        Gizmos.color = Color.cyan;
        Vector3 iOrigin = transform.position + transform.forward * interactDistance;
        Gizmos.DrawWireSphere(iOrigin, 0.35f);
    }
#endif
}
