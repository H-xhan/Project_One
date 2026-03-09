using System.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

public class PlayerHub : NetworkBehaviour
{
    [Header("Refs")]
    [Tooltip("로컬 소유자만 활성화할 카메라 루트")]
    [SerializeField] private GameObject cameraRoot;

    [Tooltip("로컬 소유자만 활성화할 AudioListener")]
    [SerializeField] private AudioListener audioListener;

    [Header("Camera Settings")]
    [Tooltip("위로 올려다보는 최대 각도")]
    [SerializeField] private float topClamp = 70f;

    [Tooltip("아래로 내려다보는 최소 각도")]
    [SerializeField] private float bottomClamp = -40f;

    private float _cameraPitchVelocity;

    [Header("Attack Buffer Settings")]
    [Tooltip("Animator에서 공격 애니가 재생되는 State의 이름(Short Name). 예: Attack")]
    [SerializeField] private string attackStateName = "Attack";

    [Tooltip("공격 중 입력 버퍼 허용 여부")]
    [SerializeField] private bool allowAttackBuffer = true;

    [Tooltip("버퍼 입력 유효 시간(초). 이 시간 안에 들어온 입력만 다음 공격으로 이어짐. 0이면 무제한")]
    [SerializeField] private float attackBufferWindow = 0.35f;

    [Tooltip("공격 상태 감지 최대 대기 시간(초). 상태명이 다르거나 전이가 꼬였을 때 무한 대기 방지")]
    [SerializeField] private float attackStateTimeout = 2.0f;

    [Header("Spawn Settings")]
    [Tooltip("이 씬들에서는 초기 Owner 스폰 보정 루틴을 건너뜁니다. 인게임 씬은 InGameMatchManager가 배치를 전담하도록 비워두지 않는 것을 권장합니다.")]
    [SerializeField] private string[] skipInitialSpawnScenes = new[] { "InGame" };

    [Header("Modules (자동 연결됨)")]
    [SerializeField] private PlayerInputModule inputModule;
    [SerializeField] private PlayerLocomotionModule locomotionModule;
    [SerializeField] private PlayerAnimModule animModule;
    [SerializeField] private PlayerCombatModule combatModule;
    [SerializeField] private PlayerInteractModule interactModule;
    [SerializeField] private PlayerStatusModule statusModule;

    public bool IsCursorLocked => inputModule != null && inputModule.IsCursorLocked;

    public CharacterController CharacterController => GetComponentInChildren<CharacterController>(true);
    public Animator Animator => GetComponentInChildren<Animator>(true);
    public Camera PlayerCamera => GetComponentInChildren<Camera>(true);

    private Vector2 _moveInput;
    private float _yawDelta;
    private float _pitchDelta;
    private bool _jumpPressed;
    private bool _sprintHeld;

    private bool _attackLockedServer;
    private bool _attackBufferedServer;
    private float _attackBufferedAtServer;
    private Coroutine _attackLockRoutine;

    private void Awake()
    {
        ResolveRefs();
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        ResolveRefs();
        ApplyOwnerVisuals();

        if (!IsOwner && inputModule != null)
            inputModule.enabled = false;

        if (!IsOwner)
        {
            var cam = GetComponentInChildren<Camera>();
            if (cam != null) cam.enabled = false;

            var listener = GetComponentInChildren<AudioListener>();
            if (listener != null) listener.enabled = false;
        }

        if (!ShouldSkipInitialSpawnRoutine())
            StartCoroutine(SpawnPosRoutine());
    }

    private IEnumerator SpawnPosRoutine()
    {
        var cc = GetComponent<CharacterController>();

        if (cc != null) cc.enabled = false;
        yield return null;

        string pointName = $"SpawnPoint_{OwnerClientId}";
        GameObject spawnPoint = GameObject.Find(pointName);

        if (spawnPoint != null)
        {
            transform.position = spawnPoint.transform.position;
            transform.rotation = spawnPoint.transform.rotation;
        }
        else
        {
            float xPos = (OwnerClientId % 2 == 0) ? -2f : 2f;
            transform.position = new Vector3(xPos, 2.0f, 0f);
        }

        yield return null;
        if (cc != null) cc.enabled = true;
    }

    private bool ShouldSkipInitialSpawnRoutine()
    {
        string currentScene = SceneManager.GetActiveScene().name;
        if (skipInitialSpawnScenes == null) return false;

        for (int i = 0; i < skipInitialSpawnScenes.Length; i++)
        {
            if (!string.IsNullOrWhiteSpace(skipInitialSpawnScenes[i]) && skipInitialSpawnScenes[i] == currentScene)
                return true;
        }

        return false;
    }

    [ContextMenu("Auto Find Modules")]
    private void ResolveRefs()
    {
        if (cameraRoot == null)
        {
            var cam = GetComponentInChildren<Camera>(true);
            if (cam != null) cameraRoot = cam.gameObject;
        }

        if (audioListener == null) audioListener = GetComponentInChildren<AudioListener>(true);

        if (inputModule == null) inputModule = GetComponentInChildren<PlayerInputModule>(true);
        if (locomotionModule == null) locomotionModule = GetComponentInChildren<PlayerLocomotionModule>(true);
        if (animModule == null) animModule = GetComponentInChildren<PlayerAnimModule>(true);
        if (combatModule == null) combatModule = GetComponentInChildren<PlayerCombatModule>(true);
        if (interactModule == null) interactModule = GetComponentInChildren<PlayerInteractModule>(true);
        if (statusModule == null) statusModule = GetComponentInChildren<PlayerStatusModule>(true);
    }

    private void ApplyOwnerVisuals()
    {
        bool active = IsOwner;
        if (cameraRoot != null) cameraRoot.SetActive(active);
        if (audioListener != null) audioListener.enabled = active;
        if (interactModule != null) interactModule.SetOwnerMode(active);
    }

    private bool CanMoveNow()
    {
        return statusModule == null || statusModule.CanMove;
    }

    private bool CanAttackNow()
    {
        return statusModule == null || statusModule.CanAttack;
    }

    private bool CanInteractNow()
    {
        return statusModule == null || statusModule.CanInteract;
    }

    private void Update()
    {
        if (IsOwner) TickOwner();
        if (IsServer) TickServer();
    }

    private void TickOwner()
    {
        if (inputModule == null) return;

        inputModule.ReadInputs(
            out Vector2 move,
            out float yawDelta,
            out float pitchDelta,
            out bool jumpPressed,
            out bool sprintHeld,
            out bool attackPressed,
            out bool interactPressed,
            out bool dropPressed
        );

        _moveInput = move;
        _yawDelta = yawDelta;
        _pitchDelta = pitchDelta;

        if (jumpPressed) _jumpPressed = true;
        _sprintHeld = sprintHeld;

        HandleCameraRotation(_pitchDelta);

        if (!CanMoveNow())
        {
            _moveInput = Vector2.zero;
            _yawDelta = 0f;
            _jumpPressed = false;
            _sprintHeld = false;
        }

        SubmitInputServerRpc(_moveInput, _yawDelta, _jumpPressed, _sprintHeld);

        if (attackPressed && CanAttackNow())
            AttackServerRpc();

        if (interactPressed && CanInteractNow() && interactModule != null)
        {
            if (interactModule.HasHeldItem())
            {
                DropItemServerRpc();
            }
            else
            {
                if (interactModule.TryFindPickupTarget(out NetworkObjectReference target))
                    TryPickupServerRpc(target);
            }
        }

        if (dropPressed && CanInteractNow())
            DropItemServerRpc();
    }

    [ServerRpc]
    private void DropItemServerRpc()
    {
        if (!CanInteractNow()) return;
        if (interactModule != null) interactModule.ServerTryDrop();
    }

    private void HandleCameraRotation(float pitchDelta)
    {
        if (cameraRoot == null) return;

        _cameraPitchVelocity -= pitchDelta;
        _cameraPitchVelocity = Mathf.Clamp(_cameraPitchVelocity, bottomClamp, topClamp);
        cameraRoot.transform.localRotation = Quaternion.Euler(_cameraPitchVelocity, 0f, 0f);
    }

    private void TickServer()
    {
        if (CharacterController == null || !CharacterController.enabled) return;

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
        _yawDelta = yawDelta;
        if (jumpPressed) _jumpPressed = true;
        _sprintHeld = sprintHeld;
    }

    [Rpc(SendTo.Server)]
    private void AttackServerRpc()
    {
        if (!CanAttackNow())
        {
            _attackBufferedServer = false;
            return;
        }

        if (_attackLockedServer)
        {
            if (allowAttackBuffer)
            {
                _attackBufferedServer = true;
                _attackBufferedAtServer = Time.time;
            }
            return;
        }

        StartAttackServerInternal();
    }

    private void StartAttackServerInternal()
    {
        if (!CanAttackNow())
            return;

        _attackLockedServer = true;

        int weaponAnimId = 0;
        if (interactModule != null)
            weaponAnimId = interactModule.GetCurrentWeaponAnimID();

        if (animModule != null)
            animModule.TriggerAttack(weaponAnimId);

        if (combatModule != null)
            combatModule.DoAttackServer();

        if (_attackLockRoutine != null) StopCoroutine(_attackLockRoutine);
        _attackLockRoutine = StartCoroutine(ServerAttackLockRoutine());
    }

    private IEnumerator ServerAttackLockRoutine()
    {
        Animator anim = Animator;
        if (anim == null)
        {
            ReleaseAttackLockAndConsumeBuffer();
            yield break;
        }

        int attackHash = Animator.StringToHash(attackStateName);

        float startTime = Time.time;
        bool enteredAttack = false;

        while (Time.time - startTime < attackStateTimeout)
        {
            var info = anim.GetCurrentAnimatorStateInfo(0);
            if (info.shortNameHash == attackHash)
            {
                enteredAttack = true;
                break;
            }
            yield return null;
        }

        if (enteredAttack)
        {
            while (Time.time - startTime < attackStateTimeout)
            {
                var info = anim.GetCurrentAnimatorStateInfo(0);
                if (info.shortNameHash != attackHash)
                    break;

                yield return null;
            }
        }

        ReleaseAttackLockAndConsumeBuffer();
    }

    private void ReleaseAttackLockAndConsumeBuffer()
    {
        _attackLockedServer = false;

        if (!_attackBufferedServer)
            return;

        if (attackBufferWindow > 0f)
        {
            if (Time.time - _attackBufferedAtServer > attackBufferWindow)
            {
                _attackBufferedServer = false;
                return;
            }
        }

        _attackBufferedServer = false;
        StartAttackServerInternal();
    }

    [ClientRpc]
    private void AttackClientRpc(int weaponID)
    {
        if (animModule != null)
            animModule.TriggerAttack(weaponID);
    }

    [ServerRpc]
    private void TryPickupServerRpc(NetworkObjectReference target)
    {
        if (!CanInteractNow()) return;
        if (interactModule == null) return;
        if (!interactModule.ServerTryPickup(target)) return;

        if (animModule != null) animModule.TriggerPickUp();
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        ResolveRefs();
    }
#endif
}