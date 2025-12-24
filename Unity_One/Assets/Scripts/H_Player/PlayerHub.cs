using UnityEngine;
using Unity.Netcode;
using Unity.Netcode.Components;

public class PlayerHub : NetworkBehaviour
{
    [Header("Core Refs")]
    [Tooltip("이동에 사용할 CharacterController")]
    [SerializeField] private CharacterController characterController;

    [Tooltip("캐릭터 Animator")]
    [SerializeField] private Animator animator;

    [Tooltip("Animator 파라미터 동기화(NetworkAnimator)")]
    [SerializeField] private NetworkAnimator networkAnimator;

    [Tooltip("플레이어 카메라(로컬 플레이어만 활성화)")]
    [SerializeField] private Camera playerCamera;

    [Tooltip("카메라 피벗(피치 회전용). 없으면 Player 루트 사용")]
    [SerializeField] private Transform cameraPivot;

    [Tooltip("아이템 DB(SO). 모든 클라이언트에서 동일해야 함")]
    [SerializeField] private ItemDatabaseSO itemDatabase;

    [Tooltip("오른손 소켓")]
    [SerializeField] private Transform rightHandSocket;

    [Tooltip("왼손 소켓")]
    [SerializeField] private Transform leftHandSocket;

    [Header("Move Tuning")]
    [Tooltip("걷기 속도")]
    [SerializeField] private float walkSpeed = 4.5f;

    [Tooltip("달리기 속도")]
    [SerializeField] private float sprintSpeed = 7f;

    [Tooltip("점프 높이")]
    [SerializeField] private float jumpHeight = 1.4f;

    [Tooltip("중력(양수)")]
    [SerializeField] private float gravity = 25f;

    [Header("Pickup Tuning")]
    [Tooltip("픽업 거리")]
    [SerializeField] private float pickupDistance = 2.5f;

    [Tooltip("픽업 레이어 마스크")]
    [SerializeField] private LayerMask pickupMask;

    public CharacterController CharacterController => characterController;
    public Animator Animator => animator;
    public Camera PlayerCamera => playerCamera;
    public Transform CameraPivot => cameraPivot;
    public float WalkSpeed => walkSpeed;
    public float SprintSpeed => sprintSpeed;
    public float JumpHeight => jumpHeight;
    public float Gravity => gravity;

    public PlayerAnimModule AnimModule => _anim;

    private PlayerInputModule _input;
    private PlayerLocomotionModule _locomotion;
    private PlayerInteractModule _interact;
    private PlayerAnimModule _anim;

    private ThirdPersonCameraFollow _cameraFollow;

    public bool IsCursorLocked { get; private set; } = true;

    private void Awake()
    {
        _input = new PlayerInputModule(this);
        _anim = new PlayerAnimModule(this);
        _locomotion = new PlayerLocomotionModule(this);
        _interact = new PlayerInteractModule(this);

        _cameraFollow = GetComponentInChildren<ThirdPersonCameraFollow>(true);
        ApplyCursorState(true);
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        bool isOwner = IsOwner;

        if (playerCamera != null)
        {
            playerCamera.enabled = isOwner;
            var listener = playerCamera.GetComponent<AudioListener>();
            if (listener != null) listener.enabled = isOwner;
        }

        if (_cameraFollow != null)
            _cameraFollow.enabled = isOwner;
    }

    private void Update()
    {
        if (!IsOwner) return;

        _input.Tick();

        if (_input.ConsumeCursorTogglePressed())
            ApplyCursorState(!IsCursorLocked);

        if (!IsCursorLocked)
            return;

        bool jumpPressed = _input.ConsumeJumpPressed();
        _locomotion.Tick(_input.MoveInput, _input.IsSprinting, jumpPressed);

        float planarSpeed01 = _input.MoveInput.sqrMagnitude > 0.01f ? 1f : 0f;
        _anim.SetLocomotion(planarSpeed01, _input.IsSprinting);

        if (_input.ConsumePrimaryAttackPressed())
            _anim.TriggerAttackLight();

        if (_input.ConsumeInteractPressed())
            _interact.TryPickupRaycast(playerCamera, pickupDistance, pickupMask);
    }

    private void ApplyCursorState(bool locked)
    {
        IsCursorLocked = locked;

        Cursor.visible = !locked;
        Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
    }

    public void RequestPickup(ulong pickupNetworkObjectId)
    {
        if (!IsOwner) return;
        RequestPickupServerRpc(pickupNetworkObjectId);
    }

    [Rpc(SendTo.Server)]
    private void RequestPickupServerRpc(ulong pickupNetworkObjectId)
    {
        if (NetworkManager == null) return;

        if (!NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(pickupNetworkObjectId, out NetworkObject netObj))
            return;

        ItemPickupNetwork pickup = netObj.GetComponent<ItemPickupNetwork>();
        if (pickup == null) return;

        // TODO: 인벤토리 연결 (지금은 비주얼/오브젝트 숨김만 처리)
        HidePickupClientRpc(pickupNetworkObjectId);

        if (networkAnimator != null)
            networkAnimator.SetTrigger("PickUp");
        else
            _anim.TriggerPickUp();
    }

    [Rpc(SendTo.ClientsAndHost)]
    private void HidePickupClientRpc(ulong pickupNetworkObjectId)
    {
        if (NetworkManager == null) return;

        if (!NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(pickupNetworkObjectId, out NetworkObject netObj))
            return;

        if (netObj != null && netObj.gameObject != null)
            netObj.gameObject.SetActive(false);
    }
}
