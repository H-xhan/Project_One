using Unity.Netcode;
using UnityEngine;

public class PlayerHub : NetworkBehaviour
{
    [Header("Refs")]
    [Tooltip("로컬 소유자만 활성화할 카메라 루트")]
    [SerializeField] private GameObject cameraRoot;

    [Tooltip("로컬 소유자만 활성화할 AudioListener")]
    [SerializeField] private AudioListener audioListener;

    [Header("Camera Settings")] // [추가] 카메라 각도 제한 설정
    [Tooltip("위로 올려다보는 최대 각도")]
    [SerializeField] private float topClamp = 70f;
    [Tooltip("아래로 내려다보는 최소 각도")]
    [SerializeField] private float bottomClamp = -40f;

    // 현재 카메라의 상하 각도를 저장할 변수
    private float _cameraPitchVelocity;

    [Header("Modules (자동 연결됨)")]
    [SerializeField] private PlayerInputModule inputModule;
    [SerializeField] private PlayerLocomotionModule locomotionModule;
    [SerializeField] private PlayerAnimModule animModule;
    [SerializeField] private PlayerCombatModule combatModule;
    [SerializeField] private PlayerInteractModule interactModule;

    public bool IsCursorLocked => inputModule != null && inputModule.IsCursorLocked;

    public CharacterController CharacterController => GetComponentInChildren<CharacterController>(true);
    public Animator Animator => GetComponentInChildren<Animator>(true);
    public Camera PlayerCamera => GetComponentInChildren<Camera>(true);

    private Vector2 _moveInput;
    private float _yawDelta;
    private float _pitchDelta; // [추가]
    private bool _jumpPressed;
    private bool _sprintHeld;

    private void Awake() { ResolveRefs(); }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        ResolveRefs();
        ApplyOwnerVisuals();
        if (!IsOwner && inputModule != null) inputModule.enabled = false;
    }

    [ContextMenu("Auto Find Modules")]
    private void ResolveRefs()
    {
        if (cameraRoot == null) { var cam = GetComponentInChildren<Camera>(true); if (cam != null) cameraRoot = cam.gameObject; }
        if (audioListener == null) audioListener = GetComponentInChildren<AudioListener>(true);

        if (inputModule == null) inputModule = GetComponentInChildren<PlayerInputModule>(true);
        if (locomotionModule == null) locomotionModule = GetComponentInChildren<PlayerLocomotionModule>(true);
        if (animModule == null) animModule = GetComponentInChildren<PlayerAnimModule>(true);
        if (combatModule == null) combatModule = GetComponentInChildren<PlayerCombatModule>(true);
        if (interactModule == null) interactModule = GetComponentInChildren<PlayerInteractModule>(true);
    }

    private void ApplyOwnerVisuals()
    {
        bool active = IsOwner;
        if (cameraRoot != null) cameraRoot.SetActive(active);
        if (audioListener != null) audioListener.enabled = active;
        if (interactModule != null) interactModule.SetOwnerMode(active);
    }

    private void Update()
    {
        if (IsOwner) TickOwner();
        if (IsServer) TickServer();
    }

    private void TickOwner()
    {
        if (inputModule == null) return;

        // [수정] 입력 받을 때 pitchDelta(상하)도 같이 받음
        inputModule.ReadInputs(
            out Vector2 move,
            out float yawDelta,
            out float pitchDelta, // [추가]
            out bool jumpPressed,
            out bool sprintHeld,
            out bool attackPressed,
            out bool interactPressed,
            out bool dropPressed
        );

        _moveInput = move;
        _yawDelta = yawDelta;
        _pitchDelta = pitchDelta; // [추가]
        if (jumpPressed) _jumpPressed = true;
        _sprintHeld = sprintHeld;

        // [핵심] 카메라 상하 회전 처리 (클라이언트 시각 효과이므로 즉시 적용)
        HandleCameraRotation(_pitchDelta);

        SubmitInputServerRpc(_moveInput, _yawDelta, _jumpPressed, _sprintHeld);

        if (attackPressed) AttackServerRpc();

        if (interactPressed && interactModule != null)
        {
            if (interactModule.TryFindPickupTarget(out NetworkObjectReference target))
                TryPickupServerRpc(target);
        }

        // 디버깅용 틱 (레이저 등)
        if (interactModule != null) interactModule.Tick(interactPressed);

        if (dropPressed) DropItemServerRpc();

        if (attackPressed) AttackServerRpc();

        if (interactPressed && interactModule != null)
        {
            if (interactModule.TryFindPickupTarget(out NetworkObjectReference target))
                TryPickupServerRpc(target);
        }

        if (interactModule != null) interactModule.Tick(interactPressed);
    }

    [ServerRpc]
    private void DropItemServerRpc()
    {
        if (interactModule != null) interactModule.ServerTryDrop();
    }


    // [추가] 카메라 상하 회전 함수
    private void HandleCameraRotation(float pitchDelta)
    {
        if (cameraRoot == null) return;

        // 마우스 Y값 누적 (일반적으로 위로 올리면 -각도가 되어야 고개가 들림)
        _cameraPitchVelocity -= pitchDelta;

        // 각도 제한 (너무 꺾이지 않게)
        _cameraPitchVelocity = Mathf.Clamp(_cameraPitchVelocity, bottomClamp, topClamp);

        // CameraRoot의 로컬 회전만 변경 (몸통은 안 돌고 목만 끄덕거림)
        cameraRoot.transform.localRotation = Quaternion.Euler(_cameraPitchVelocity, 0f, 0f);
    }

    private void TickServer()
    {
        // [주의] 점프 여부를 받아옴
        bool jumped = false;
        if (locomotionModule != null)
            jumped = locomotionModule.TickServer(_moveInput, _yawDelta, _jumpPressed, _sprintHeld);

        if (jumped && animModule != null) animModule.TriggerJump();

        if (animModule != null && locomotionModule != null)
            animModule.TickServer(locomotionModule);

        _jumpPressed = false;
        _yawDelta = 0f;
    }

    [ServerRpc(Delivery = RpcDelivery.Unreliable)]
    private void SubmitInputServerRpc(Vector2 move, float yawDelta, bool jumpPressed, bool sprintHeld)
    {
        _moveInput = move;
        _yawDelta = yawDelta; // 좌우 회전은 서버가 처리 (캐릭터 몸통)
        if (jumpPressed) _jumpPressed = true;
        _sprintHeld = sprintHeld;
    }
    [ServerRpc]
    private void AttackServerRpc()
    {
        // 1. 전투 모듈에게 "공격 시도해!"라고 명령
        // (이제 전투 모듈이 쿨타임 체크하고 -> 무기 ID 확인하고 -> 애니메이션까지 틀어줍니다)
        if (combatModule != null) combatModule.DoAttack();
    }

    [ServerRpc]
    private void TryPickupServerRpc(NetworkObjectReference target)
    {
        if (interactModule == null) return;
        if (!interactModule.ServerTryPickup(target)) return;
        if (animModule != null) animModule.TriggerPickUp();
    }

#if UNITY_EDITOR
    private void OnValidate() { ResolveRefs(); }
#endif
}