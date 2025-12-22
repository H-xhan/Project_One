using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;

public class PlayerHub : NetworkBehaviour
{
    [Header("Core Refs")]
    [Tooltip("이동에 사용할 CharacterController")]
    [SerializeField] private CharacterController characterController;

    [Tooltip("캐릭터 Animator")]
    [SerializeField] private Animator animator;

    [Tooltip("Animator 파라미터 동기화(NetworkAnimator)")]
    [SerializeField] private NetworkAnimator networkAnimator;

    [Header("Shared Data")]
    [Tooltip("아이템 DB(SO). 모든 클라이언트에 동일해야 함")]
    [SerializeField] private ItemDatabaseSO itemDatabase;

    [Header("Move")]
    [Tooltip("걷기 속도")]
    [SerializeField] private float walkSpeed = 4.5f;

    [Tooltip("달리기 속도")]
    [SerializeField] private float sprintSpeed = 7f;

    [Header("Rotate")]
    [Tooltip("Yaw 민감도(도/초)")]
    [SerializeField] private float yawSensitivity = 180f;

    [Tooltip("마우스 델타를 축 입력으로 바꾸는 배수")]
    [SerializeField] private float mouseDeltaToAxis = 0.1f;

    [Header("Jump/Gravity")]
    [Tooltip("점프 높이")]
    [SerializeField] private float jumpHeight = 1.4f;

    [Tooltip("중력")]
    [SerializeField] private float gravity = 25f;

    [Tooltip("지면에 붙는 Y 속도")]
    [SerializeField] private float groundedStickVelocity = -2f;

    [Tooltip("코요테 타임")]
    [SerializeField] private float coyoteTime = 0.12f;

    [Tooltip("점프 버퍼 시간")]
    [SerializeField] private float jumpBufferTime = 0.12f;

    [Header("Cursor")]
    [Tooltip("스폰 시 커서 락 여부")]
    [SerializeField] private bool lockCursorOnSpawn = true;

    [Header("Combat")]
    [Tooltip("공격 쿨다운(초) - 무기 장착 시 무기 쿨다운이 우선")]
    [SerializeField] private float fallbackAttackCooldown = 0.35f;

    [Tooltip("좌클릭을 Heavy로 취급할지(넉백/파워)")]
    [SerializeField] private bool primaryAttackIsHeavy = true;

    [Tooltip("타격 판정 거리(앞으로) - 무기 장착 시 무기 값이 우선")]
    [SerializeField] private float fallbackHitDistance = 1.0f;

    [Tooltip("타격 판정 반지름 - 무기 장착 시 무기 값이 우선")]
    [SerializeField] private float fallbackHitRadius = 0.6f;

    [Tooltip("피격 레이어 마스크")]
    [SerializeField] private LayerMask hitMask = ~0;

    [Tooltip("자기 자신 피격 무시")]
    [SerializeField] private bool ignoreSelf = true;

    [Tooltip("넉백 힘(가벼운 공격)")]
    [SerializeField] private float knockbackLight = 6f;

    [Tooltip("넉백 힘(강한 공격)")]
    [SerializeField] private float knockbackHeavy = 10f;

    [Tooltip("넉백 지속 시간(초)")]
    [SerializeField] private float knockbackDuration = 0.15f;

    [Header("Equipment")]
    [Tooltip("오른손 무기 소켓(Transform)")]
    [SerializeField] private Transform rightHandSocket;

    [Tooltip("왼손 무기 소켓(Transform)")]
    [SerializeField] private Transform leftHandSocket;

    [Header("Interact/Pickup")]
    [Tooltip("레이캐스트 시작 카메라(없으면 Camera.main 사용)")]
    [SerializeField] private Camera targetCamera;

    [Tooltip("상호작용 거리")]
    [SerializeField] private float interactDistance = 3.0f;

    [Tooltip("픽업 레이어 마스크")]
    [SerializeField] private LayerMask pickupMask;

    [Header("Inventory")]
    [Tooltip("인벤토리 슬롯 수")]
    [SerializeField] private int capacity = 8;

    [Header("Animation Params")]
    [Tooltip("최대 이동 속도(정규화 기준). Speed = currentSpeed / maxMoveSpeed")]
    [SerializeField] private float maxMoveSpeed = 7f;

    [Tooltip("이동 속도 Float 파라미터 이름")]
    [SerializeField] private string speedParam = "Speed";

    [Tooltip("점프 Trigger 파라미터 이름")]
    [SerializeField] private string jumpTrigger = "Jump";

    [Tooltip("약공격 Trigger 파라미터 이름")]
    [SerializeField] private string lightAttackTrigger = "AttackLight";

    [Tooltip("강공격 Trigger 파라미터 이름")]
    [SerializeField] private string heavyAttackTrigger = "AttackHeavy";

    [Tooltip("피격 Trigger 파라미터 이름")]
    [SerializeField] private string hitReactTrigger = "HitReact";

    [Tooltip("픽업 Trigger 파라미터 이름")]
    [SerializeField] private string pickUpTrigger = "PickUp";

    // Net State
    private readonly NetworkVariable<int> _equippedWeaponId = new NetworkVariable<int>(0);
    public NetworkList<InventorySlot> Slots { get; private set; }

    // Modules
    private PlayerInputModule _input;
    private PlayerLocomotionModule _locomotion;
    private PlayerAnimModule _anim;
    private PlayerInteractModule _interact;

    // Server state
    private float _serverNextAttackTime;

    // Equipment visual (client)
    private GameObject _equippedVisual;

    private void Awake()
    {
        if (characterController == null) characterController = GetComponent<CharacterController>();

        Slots = new NetworkList<InventorySlot>();

        _input = new PlayerInputModule(this);
        _locomotion = new PlayerLocomotionModule(this);
        _anim = new PlayerAnimModule(this);
        _interact = new PlayerInteractModule(this);
    }

    public override void OnNetworkSpawn()
    {
        if (IsClient)
        {
            _equippedWeaponId.OnValueChanged += OnEquippedChanged;
            ApplyEquippedVisual(_equippedWeaponId.Value);
        }

        if (IsServer)
        {
            if (Slots.Count == 0)
            {
                for (int i = 0; i < capacity; i++)
                    Slots.Add(new InventorySlot(0, 0));
            }
        }

        if (IsOwner)
        {
            if (lockCursorOnSpawn)
                SetCursorLocked(true);

            if (targetCamera == null)
                targetCamera = Camera.main;
        }
    }

    public override void OnNetworkDespawn()
    {
        if (IsClient)
            _equippedWeaponId.OnValueChanged -= OnEquippedChanged;
    }

    private void Update()
    {
        if (!IsOwner) return;

        _input.Tick();

        if (_input.ConsumeEscapePressed())
        {
            bool locked = Cursor.lockState == CursorLockMode.Locked;
            SetCursorLocked(!locked);
        }

        _locomotion.Tick(_input.MoveInput, _input.IsSprinting, _input.ConsumeJumpPressed(), out float planarSpeed, out bool jumpedThisFrame);
        _anim.SetMoveSpeed(planarSpeed);

        if (jumpedThisFrame)
            _anim.PlayJump();

        if (_input.ConsumePrimaryAttackPressed())
        {
            _anim.PlayPrimaryAttack(primaryAttackIsHeavy);
            RequestAttack(primaryAttackIsHeavy);
        }

        if (_input.ConsumeInteractPressed())
        {
            _anim.PlayPickUp();
            _interact.TryPickupRaycast(targetCamera, interactDistance, pickupMask);
        }
    }

    private void SetCursorLocked(bool locked)
    {
        Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !locked;
    }

    // Public API (기존 컴포넌트 기능 대체)
    public int GetEquippedWeaponId() => _equippedWeaponId.Value;

    public void RequestEquip(int weaponItemId)
    {
        if (!IsOwner) return;
        RequestEquipRpc(weaponItemId);
    }

    public void RequestAttack(bool isHeavy)
    {
        if (!IsOwner) return;
        AttackRpc(isHeavy);
    }

    public void RequestPickup(ulong pickupNetworkObjectId)
    {
        if (!IsOwner) return;
        RequestPickupRpc(pickupNetworkObjectId);
    }

    // -------- Equipment (Server) --------
    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
    private void RequestEquipRpc(int weaponItemId)
    {
        _equippedWeaponId.Value = weaponItemId;
    }

    private void OnEquippedChanged(int prev, int next)
    {
        ApplyEquippedVisual(next);
    }

    private void ApplyEquippedVisual(int weaponItemId)
    {
        if (_equippedVisual != null)
        {
            Destroy(_equippedVisual);
            _equippedVisual = null;
        }

        if (itemDatabase == null) return;

        var weapon = itemDatabase.GetWeapon(weaponItemId);
        if (weapon == null) return;
        if (weapon.equippedModelPrefab == null) return;

        Transform socket = weapon.hand == WeaponHand.Left ? leftHandSocket : rightHandSocket;
        if (socket == null) return;

        _equippedVisual = Instantiate(weapon.equippedModelPrefab, socket);
        _equippedVisual.transform.localPosition = weapon.equippedLocalPosition;
        _equippedVisual.transform.localRotation = Quaternion.Euler(weapon.equippedLocalEulerAngles);
        _equippedVisual.transform.localScale = weapon.equippedLocalScale;
    }

    // -------- Combat (Server) --------
    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
    private void AttackRpc(bool isHeavy)
    {
        float cooldown = fallbackAttackCooldown;
        float hitDistance = fallbackHitDistance;
        float hitRadius = fallbackHitRadius;
        float damage = 0f;
        bool hasDamage = false;

        if (itemDatabase != null)
        {
            int weaponId = _equippedWeaponId.Value;
            var weaponItem = itemDatabase.GetWeapon(weaponId);
            if (weaponItem != null)
            {
                cooldown = weaponItem.weapon.cooldown;
                hitDistance = weaponItem.weapon.hitDistance;
                hitRadius = weaponItem.weapon.hitRadius;
                damage = weaponItem.weapon.damage;
                hasDamage = true;

                if (networkAnimator != null)
                    networkAnimator.SetTrigger("AttackLight");
            }
        }

        if (Time.time < _serverNextAttackTime)
            return;

        _serverNextAttackTime = Time.time + cooldown;

        Vector3 origin = transform.position + transform.forward * hitDistance;
        Collider[] hits = Physics.OverlapSphere(origin, hitRadius, hitMask, QueryTriggerInteraction.Ignore);

        if (hits == null || hits.Length == 0)
            return;

        float force = isHeavy ? knockbackHeavy : knockbackLight;

        for (int i = 0; i < hits.Length; i++)
        {
            var col = hits[i];
            if (col == null) continue;

            if (ignoreSelf && col.transform.root == transform.root)
                continue;

            if (hasDamage && col.TryGetComponent<IDamageable>(out var dmg))
                dmg.TakeDamage(damage);

            var targetHub = col.GetComponentInParent<PlayerHub>();
            if (targetHub != null && targetHub.NetworkObject != null && targetHub.NetworkObject != NetworkObject)
            {
                Vector3 dir = (targetHub.transform.position - transform.position);
                dir.y = 0f;
                if (dir.sqrMagnitude < 0.0001f) dir = transform.forward;
                dir.Normalize();

                ClientRpcParams onlyTargetOwner = new ClientRpcParams
                {
                    Send = new ClientRpcSendParams { TargetClientIds = new[] { targetHub.NetworkObject.OwnerClientId } }
                };

                targetHub.ApplyKnockbackClientRpc(dir * force, knockbackDuration, onlyTargetOwner);

                targetHub._anim.ServerPlayHitReact();
            }
        }
    }

    [ClientRpc]
    private void ApplyKnockbackClientRpc(Vector3 impulse, float duration, ClientRpcParams rpcParams = default)
    {
        if (!IsOwner) return;
        _locomotion.ApplyExternalImpulse(impulse, duration, knockbackDuration);
    }

    // -------- Inventory (Server) --------
    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
    private void RequestPickupRpc(ulong pickupNetworkObjectId)
    {
        if (!NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(pickupNetworkObjectId, out var netObj))
            return;

        var pickup = netObj.GetComponent<ItemPickupNetwork>();
        if (pickup == null)
            return;

        int itemId = pickup.ItemId;
        int amount = pickup.Amount;

        if (itemDatabase == null || itemDatabase.Get(itemId) == null)
        {
            Debug.LogWarning($"[Inventory] Unknown itemId: {itemId}");
            return;
        }

        if (!TryAddItemServer(itemId, amount))
            return;

        netObj.Despawn(true);
        NotifyPickedUpClientRpc(itemId, amount);
    }

    private bool TryAddItemServer(int itemId, int amount)
    {
        if (!IsServer) return false;

        var data = itemDatabase != null ? itemDatabase.Get(itemId) : null;
        if (data == null) return false;

        int remaining = amount;

        if (data.stackable)
        {
            for (int i = 0; i < Slots.Count; i++)
            {
                var slot = Slots[i];
                if (slot.itemId != itemId) continue;

                int canAdd = Mathf.Max(0, data.maxStack - slot.amount);
                if (canAdd <= 0) continue;

                int add = Mathf.Min(canAdd, remaining);
                slot.amount += add;
                Slots[i] = slot;

                remaining -= add;
                if (remaining <= 0) return true;
            }
        }

        for (int i = 0; i < Slots.Count; i++)
        {
            var slot = Slots[i];
            if (slot.itemId != 0) continue;

            int add = data.stackable ? Mathf.Min(data.maxStack, remaining) : 1;
            Slots[i] = new InventorySlot(itemId, add);

            remaining -= add;
            if (remaining <= 0) return true;
        }

        Debug.Log($"[Inventory] Inventory full. itemId={itemId}, remaining={remaining}");
        return false;
    }

    [ClientRpc]
    private void NotifyPickedUpClientRpc(int itemId, int amount)
    {
        if (!IsOwner) return;
        Debug.Log($"[Inventory] Picked up itemId={itemId}, amount={amount}");
    }

    // 내부 접근(모듈용)
    internal CharacterController CC => characterController;
    internal Transform Self => transform;
    internal float WalkSpeed => walkSpeed;
    internal float SprintSpeed => sprintSpeed;
    internal float JumpHeight => jumpHeight;
    internal float Gravity => gravity;
    internal float GroundedStickVelocity => groundedStickVelocity;
    internal float CoyoteTime => coyoteTime;
    internal float JumpBufferTime => jumpBufferTime;
    internal float YawSensitivity => yawSensitivity;
    internal float MouseDeltaToAxis => mouseDeltaToAxis;

    internal Animator Anim => animator;
    internal float MaxMoveSpeed => maxMoveSpeed;

    internal int SpeedHash => Animator.StringToHash(speedParam);
    internal int JumpHash => Animator.StringToHash(jumpTrigger);
    internal int LightHash => Animator.StringToHash(lightAttackTrigger);
    internal int HeavyHash => Animator.StringToHash(heavyAttackTrigger);
    internal int HitHash => Animator.StringToHash(hitReactTrigger);
    internal int PickUpHash => Animator.StringToHash(pickUpTrigger);

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Vector3 origin = transform.position + transform.forward * fallbackHitDistance;
        Gizmos.DrawWireSphere(origin, fallbackHitRadius);
    }
#endif
}
