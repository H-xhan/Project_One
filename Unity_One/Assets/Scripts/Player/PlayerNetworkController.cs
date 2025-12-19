using Unity.Netcode;
using UnityEngine;

public class PlayerNetworkController : NetworkBehaviour
{
    [Header("Move")]
    [Tooltip("걷기 속도")]
    [SerializeField] private float walkSpeed = 4.5f;

    [Tooltip("달리기 속도")]
    [SerializeField] private float sprintSpeed = 7f;

    [Header("Rotate")]
    [Tooltip("마우스 Yaw 감도")]
    [SerializeField] private float yawSensitivity = 180f;

    [Header("Jump/Gravity")]
    [Tooltip("점프 높이")]
    [SerializeField] private float jumpHeight = 1.4f;

    [Tooltip("중력")]
    [SerializeField] private float gravity = 25f;

    [Tooltip("바닥에 붙는 속도(음수 추천)")]
    [SerializeField] private float groundedStickVelocity = -2f;

    [Tooltip("코요테 타임(초)")]
    [SerializeField] private float coyoteTime = 0.12f;

    [Tooltip("점프 버퍼(초)")]
    [SerializeField] private float jumpBufferTime = 0.12f;

    [Header("Animation")]
    [Tooltip("PlayerAnimDriver 참조")]
    [SerializeField] private PlayerAnimDriver animDriver;

    [Header("Cursor")]
    [Tooltip("스폰 시 커서 잠금")]
    [SerializeField] private bool lockCursorOnSpawn = true;

    [Tooltip("커서 토글 키")]
    [SerializeField] private KeyCode cursorToggleKey = KeyCode.Escape;

    [Header("Melee Hit")]
    [Tooltip("좌클릭(Sweep) 공격 키")]
    [SerializeField] private int lightAttackMouseButton = 0;

    [Tooltip("우클릭(Heavy) 공격 키")]
    [SerializeField] private int heavyAttackMouseButton = 1;

    [Tooltip("공격 쿨다운(초)")]
    [SerializeField] private float attackCooldown = 0.35f;

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

    private CharacterController _controller;

    private Vector2 _moveInput;
    private float _verticalVelocity;

    private bool _isSprinting;
    private bool _isGrounded;

    private float _lastGroundedTime;
    private float _lastJumpPressedTime;

    private float _nextAttackTime;

    private Vector3 _externalVelocity;
    private float _externalTimeLeft;

    private enum PendingAttackType { None, Light, Heavy }
    private PendingAttackType _pendingAttack = PendingAttackType.None;
    private float _pendingAttackTime;
    [SerializeField] private float pendingTimeout = 0.8f;
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
        ReadMoveInput();
        ReadSprintInput();
        ReadJumpInput();
        ReadAttackInput();

        RotateByMouseX();
        ApplyGravityAndMove();
        PushAnim();
    }

    private void HandleCursorToggle()
    {
        if (Input.GetKeyDown(cursorToggleKey))
        {
            bool locked = Cursor.lockState == CursorLockMode.Locked;
            SetCursorLocked(!locked);
        }
    }

    private void SetCursorLocked(bool locked)
    {
        Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !locked;
    }

    private void ReadMoveInput()
    {
        float x = Input.GetAxisRaw("Horizontal");
        float y = Input.GetAxisRaw("Vertical");
        _moveInput = new Vector2(x, y);
        _moveInput = Vector2.ClampMagnitude(_moveInput, 1f);
    }

    private void ReadSprintInput()
    {
        _isSprinting = Input.GetKey(KeyCode.LeftShift);
    }

    private void ReadJumpInput()
    {
        if (Input.GetKeyDown(KeyCode.Space))
            _lastJumpPressedTime = Time.time;
    }

    private void ReadAttackInput()
    {
        if (Time.time < _nextAttackTime) return;

        if (Input.GetMouseButtonDown(lightAttackMouseButton))
        {
            _nextAttackTime = Time.time + attackCooldown;

            if (animDriver != null) animDriver.PlayHitSweep();
            AttackServerRpc(false);
        }
        else if (Input.GetMouseButtonDown(heavyAttackMouseButton))
        {
            _nextAttackTime = Time.time + attackCooldown;

            if (animDriver != null) animDriver.PlayHeavyAttack();
            AttackServerRpc(true);
        }
    }

    private void RotateByMouseX()
    {
        float mouseX = Input.GetAxis("Mouse X");
        float yaw = mouseX * yawSensitivity * Time.deltaTime;
        transform.Rotate(0f, yaw, 0f);
    }

    private void ApplyGravityAndMove()
    {
        _isGrounded = _controller.isGrounded;

        if (_isGrounded)
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

        // knockback
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
        }
    }

    [ClientRpc]
    private void ApplyKnockbackClientRpc(Vector3 impulse, float duration, ClientRpcParams rpcParams = default)
    {
        if (!IsOwner) return;

        _externalVelocity = impulse;
        _externalTimeLeft = Mathf.Max(0.01f, duration);
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Vector3 origin = transform.position + transform.forward * hitDistance;
        Gizmos.DrawWireSphere(origin, hitRadius);
    }
#endif
}
