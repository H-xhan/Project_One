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
    [Tooltip("기본 세미 고정 쿼터뷰 피치 각도입니다. 값이 클수록 더 아래를 내려다봅니다.")]
    [SerializeField] private float defaultQuarterViewPitch = 28f;

    [Tooltip("입력이 없을 때 기본 쿼터뷰 구도로 복귀하는 속도입니다.")]
    [SerializeField] private float cameraPitchReturnSpeed = 4f;

    [Tooltip("수동 피치 입력에 곱할 배율입니다. 값이 낮을수록 카메라 조작 피로도가 줄어듭니다.")]
    [SerializeField] private float manualPitchInputScale = 0.35f;

    [Tooltip("이 값보다 작은 피치 입력은 무입력으로 간주하고 기본 구도로 복귀합니다.")]
    [SerializeField] private float cameraPitchInputDeadzone = 0.01f;

    [Tooltip("장면 중심 프레이밍을 위해 현재 카메라 로컬 위치에 더할 오프셋입니다.")]
    [SerializeField] private Vector3 cameraLocalPositionOffset = new Vector3(0f, 0.8f, -0.9f);

    [Tooltip("수동 yaw 입력에 곱할 배율입니다. 값이 낮을수록 자유 회전 의존이 줄어듭니다.")]
    [SerializeField] private float manualYawInputScale = 0.45f;

    [Tooltip("이 값보다 작은 yaw 입력은 무입력으로 간주합니다.")]
    [SerializeField] private float cameraYawInputDeadzone = 0.01f;

    [Tooltip("정지 중 yaw가 기본 방향으로 복귀하는 속도입니다.")]
    [SerializeField] private float cameraYawReturnSpeed = 45f;

    [Tooltip("이동 중 장면 가독성을 위해 현재 카메라 로컬 위치에 추가할 프레이밍 오프셋입니다.")]
    [SerializeField] private Vector3 cameraMoveFramingOffset = new Vector3(0f, 0.2f, -0.35f);

    [Tooltip("카메라 로컬 위치가 목표 프레이밍으로 따라가는 속도입니다.")]
    [SerializeField] private float cameraPositionBlendSpeed = 6f;

    [Header("Ragdoll Camera Focus")]
    [SerializeField, Tooltip("Ragdoll 활성 중 카메라가 Ragdoll 중심 위치를 따라가게 할지 여부입니다.")]
    private bool useRagdollFocusForCamera = true;

    [SerializeField, Tooltip("Ragdoll 중심 위치에 더할 카메라 focus 높이 보정값입니다.")]
    private float ragdollCameraFocusHeightOffset = 0.4f;

    [SerializeField, Tooltip("Ragdoll focus를 따라갈 때의 보간 속도입니다.")]
    private float ragdollCameraBlendSpeed = 8f;

    [SerializeField, Tooltip("Ragdoll이 끝난 뒤 기존 카메라 기준으로 돌아가는 속도입니다.")]
    private float ragdollCameraReturnSpeed = 5f;

    [SerializeField, Tooltip("Ragdoll 비활성 후 마지막 Ragdoll focus를 유지하는 시간입니다.")]
    private float ragdollCameraHoldAfterInactive = 0.45f;

    [SerializeField, Tooltip("Ragdoll focus 보정 거리를 제한합니다. 0 이하이면 제한하지 않습니다.")]
    private float ragdollCameraMaxFocusDistance = 8f;

    [SerializeField, Tooltip("Ragdoll focus가 기존 카메라 기준에서 이 거리 이상 멀어지면 빠른 추적 속도를 사용합니다.")]
    private float ragdollCameraFastCatchupDistance = 3.0f;

    [SerializeField, Tooltip("Ragdoll focus가 빠르게 멀어질 때 사용하는 빠른 카메라 추적 속도입니다.")]
    private float ragdollCameraFastBlendSpeed = 22f;

    [SerializeField, Tooltip("Ragdoll 활성 또는 유지 시간 중 카메라 가림/충돌 검사 기준점을 Ragdoll focus로 사용할지 여부입니다.")]
    private bool useRagdollFocusForCameraObstruction = true;

    [SerializeField, Tooltip("Ragdoll 종료 후 기상/복귀 시점에서 마지막 Ragdoll focus를 추가로 유지하는 시간입니다.")]
    private float ragdollCameraStandUpHoldExtra = 0.25f;

    [Header("Camera Collision")]
    [Tooltip("카메라 가림 검사에 사용할 충돌 레이어 마스크입니다.")]
    [SerializeField] private LayerMask cameraCollisionMask = ~0;

    [Tooltip("카메라 가림 검사에 사용할 SphereCast 반경입니다.")]
    [SerializeField] private float cameraCollisionRadius = 0.2f;

    [Tooltip("충돌 지점 앞에 카메라를 얼마나 여유 두고 둘지 정합니다.")]
    [SerializeField] private float cameraCollisionPadding = 0.1f;

    [Tooltip("가림 시 카메라가 플레이어에 지나치게 붙지 않도록 유지할 최소 거리입니다.")]
    [SerializeField] private float minimumCameraDistance = 0.75f;

    [Tooltip("가림이 사라졌을 때 원래 쿼터뷰 위치로 복귀하는 속도입니다.")]
    [SerializeField] private float cameraCollisionReturnSpeed = 4f;

    [Tooltip("카메라 가림 검사를 수행할 캐릭터 상체 기준 높이입니다.")]
    [SerializeField] private float cameraCollisionFocusHeight = 0.95f;

    [Tooltip("얇은 오브젝트 대응을 위해 충돌 지점보다 추가로 플레이어 쪽으로 당기는 거리입니다.")]
    [SerializeField] private float cameraCollisionExtraPullForward = 0.25f;

    [Tooltip("상체 좌우 가시성 보장을 위해 추가 검사할 샘플의 좌우 벌림 거리입니다.")]
    [SerializeField] private float cameraCollisionSampleSideOffset = 0.25f;

    [Tooltip("위로 올려다보는 최대 각도")]
    [SerializeField] private float topClamp = 70f;

    [Tooltip("아래로 내려다보는 최소 각도")]
    [SerializeField] private float bottomClamp = -40f;

    private float _cameraPitchVelocity;
    private Vector3 _cameraRootBaseLocalPosition;
    private bool _cameraRootBaseLocalPositionCaptured;
    private float _defaultQuarterViewYaw;
    private bool _defaultQuarterViewYawCaptured;
    private Vector3 _cameraCurrentLocalPosition;
    private bool _cameraLocalPositionInitialized;
    private bool _cameraWasObstructedLastFrame;
    private SugaActiveRagdollController _activeRagdollController;
    private bool _activeRagdollControllerResolveAttempted;
    private Vector3 _ragdollCameraFocusLocalOffset;
    private Vector3 _lastRagdollCameraFocusWorld;
    private float _lastRagdollCameraFocusTime;
    private bool _hasLastRagdollCameraFocus;

    private const int CameraObstructionHitBufferSize = 8;
    private readonly RaycastHit[] _cameraObstructionHits = new RaycastHit[CameraObstructionHitBufferSize];

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
    [Tooltip("플레이어 입력을 읽는 모듈입니다. 비워두면 자식에서 자동 탐색합니다.")]
    [SerializeField] private PlayerInputModule inputModule;

    [Tooltip("서버 이동과 점프를 처리하는 모듈입니다. 비워두면 자식에서 자동 탐색합니다.")]
    [SerializeField] private PlayerLocomotionModule locomotionModule;

    [Tooltip("애니메이션 파라미터와 트리거를 처리하는 모듈입니다. 비워두면 자식에서 자동 탐색합니다.")]
    [SerializeField] private PlayerAnimModule animModule;

    [Tooltip("공격 판정과 타격 처리를 담당하는 모듈입니다. 비워두면 자식에서 자동 탐색합니다.")]
    [SerializeField] private PlayerCombatModule combatModule;

    [Tooltip("아이템 상호작용과 장착 표현을 담당하는 모듈입니다. 비워두면 자식에서 자동 탐색합니다.")]
    [SerializeField] private PlayerInteractModule interactModule;

    [Tooltip("넉백, 기상, 탈락 등 플레이어 상태를 담당하는 모듈입니다. 비워두면 자식에서 자동 탐색합니다.")]
    [SerializeField] private PlayerStatusModule statusModule;

    [Tooltip("현재 게임 상태를 확인할 매니저입니다. 비워두면 씬에서 자동 탐색합니다.")]
    [SerializeField] private GameStateManager gameStateManager;

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
        ApplyDefaultCameraPitchImmediate();
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        ResolveRefs();
        CacheCameraDefaults();
        ApplyOwnerVisuals();
        ApplyDefaultCameraPitchImmediate();

        if (!IsOwner && inputModule != null)
            inputModule.enabled = false;

        if (!IsOwner)
        {
            var cam = GetComponentInChildren<Camera>();
            if (cam != null) cam.enabled = false;

            var listener = GetComponentInChildren<AudioListener>();
            if (listener != null) listener.enabled = false;
        }

        //if (!ShouldSkipInitialSpawnRoutine())
        //StartCoroutine(SpawnPosRoutine());
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

        CacheCameraDefaults();

        if (audioListener == null) audioListener = GetComponentInChildren<AudioListener>(true);

        if (inputModule == null) inputModule = GetComponentInChildren<PlayerInputModule>(true);
        if (locomotionModule == null) locomotionModule = GetComponentInChildren<PlayerLocomotionModule>(true);
        if (animModule == null) animModule = GetComponentInChildren<PlayerAnimModule>(true);
        if (combatModule == null) combatModule = GetComponentInChildren<PlayerCombatModule>(true);
        if (interactModule == null) interactModule = GetComponentInChildren<PlayerInteractModule>(true);
        if (statusModule == null) statusModule = GetComponentInChildren<PlayerStatusModule>(true);
        if (gameStateManager == null) gameStateManager = FindFirstObjectByType<GameStateManager>();
    }

    private SugaActiveRagdollController ResolveActiveRagdollController()
    {
        if (_activeRagdollController != null)
            return _activeRagdollController;

        if (_activeRagdollControllerResolveAttempted)
            return null;

        _activeRagdollControllerResolveAttempted = true;

        _activeRagdollController = GetComponent<SugaActiveRagdollController>();
        if (_activeRagdollController != null)
            return _activeRagdollController;

        _activeRagdollController = GetComponentInParent<SugaActiveRagdollController>();
        if (_activeRagdollController != null)
            return _activeRagdollController;

        _activeRagdollController = GetComponentInChildren<SugaActiveRagdollController>(true);
        return _activeRagdollController;
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
    private bool IsPlayingState()
    {
        if (gameStateManager == null)
            gameStateManager = FindFirstObjectByType<GameStateManager>();

        if (gameStateManager == null)
            return false;

        return gameStateManager.GetState() == GameStateManager.GameState.Playing;
    }

    private bool AllowLookInput()
    {
        return IsPlayingState();
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

        bool allowLook = AllowLookInput();

        if (!allowLook)
        {
            yawDelta = 0f;
            pitchDelta = 0f;
        }

        yawDelta = GetProcessedYawDelta(yawDelta, move, allowLook);

        _moveInput = move;
        _yawDelta = yawDelta;
        _pitchDelta = pitchDelta;
        _sprintHeld = sprintHeld;

        if (allowLook)
        {
            HandleCameraRotation(_pitchDelta);
        }
        else
        {
            HandleCameraRotation(0f);
        }

        if (!CanMoveNow())
        {
            _moveInput = Vector2.zero;
            _yawDelta = 0f;
            _sprintHeld = false;
        }

        SubmitInputServerRpc(_moveInput, _yawDelta, _sprintHeld);

        if (jumpPressed && CanMoveNow())
            QueueJumpServerRpc();

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

        float scaledPitchDelta = pitchDelta * Mathf.Max(0f, manualPitchInputScale);
        _cameraPitchVelocity -= scaledPitchDelta;

        if (Mathf.Abs(pitchDelta) <= Mathf.Max(0f, cameraPitchInputDeadzone))
        {
            float targetPitch = GetClampedDefaultQuarterViewPitch();
            float recenterStep = Mathf.Max(0f, cameraPitchReturnSpeed) * Time.deltaTime;
            _cameraPitchVelocity = Mathf.MoveTowards(_cameraPitchVelocity, targetPitch, recenterStep);
        }

        _cameraPitchVelocity = Mathf.Clamp(_cameraPitchVelocity, bottomClamp, topClamp);
        UpdateCameraLocalPosition();
        cameraRoot.transform.localRotation = Quaternion.Euler(_cameraPitchVelocity, 0f, 0f);
    }

    private void ApplyDefaultCameraPitchImmediate()
    {
        CacheCameraDefaults();
        _cameraPitchVelocity = GetClampedDefaultQuarterViewPitch();

        if (cameraRoot != null)
        {
            ApplyCameraLocalPositionImmediate();
            cameraRoot.transform.localRotation = Quaternion.Euler(_cameraPitchVelocity, 0f, 0f);
        }
    }

    private float GetClampedDefaultQuarterViewPitch()
    {
        return Mathf.Clamp(defaultQuarterViewPitch, bottomClamp, topClamp);
    }

    private void CacheCameraDefaults()
    {
        if (!_cameraRootBaseLocalPositionCaptured && cameraRoot != null)
        {
            _cameraRootBaseLocalPosition = cameraRoot.transform.localPosition;
            _cameraRootBaseLocalPositionCaptured = true;
        }

        if (!_defaultQuarterViewYawCaptured)
        {
            _defaultQuarterViewYaw = transform.eulerAngles.y;
            _defaultQuarterViewYawCaptured = true;
        }
    }

    private Vector3 GetTargetCameraLocalPosition()
    {
        if (!_cameraRootBaseLocalPositionCaptured)
            return cameraLocalPositionOffset;

        return _cameraRootBaseLocalPosition + cameraLocalPositionOffset + cameraMoveFramingOffset * GetCameraMoveFramingWeight();
    }

    private float GetCameraMoveFramingWeight()
    {
        float moveMagnitude = Mathf.Clamp01(_moveInput.magnitude);
        float forwardWeight = Mathf.Clamp01(_moveInput.y);
        return Mathf.Clamp01(moveMagnitude * 0.5f + forwardWeight * 0.5f);
    }

    private Vector3 UpdateRagdollCameraFocusLocalOffset()
    {
        Vector3 desiredOffset = GetDesiredRagdollCameraFocusLocalOffset(out bool usingRagdollOrHold);
        float speed = GetRagdollCameraFocusBlendSpeed(usingRagdollOrHold, desiredOffset);
        float t = 1f - Mathf.Exp(-GetFiniteNonNegative(speed) * Time.deltaTime);

        _ragdollCameraFocusLocalOffset = Vector3.Lerp(_ragdollCameraFocusLocalOffset, desiredOffset, t);
        if (!IsFiniteVector3(_ragdollCameraFocusLocalOffset))
            _ragdollCameraFocusLocalOffset = Vector3.zero;

        if (!usingRagdollOrHold && _ragdollCameraFocusLocalOffset.sqrMagnitude <= 0.000001f)
            _ragdollCameraFocusLocalOffset = Vector3.zero;

        return _ragdollCameraFocusLocalOffset;
    }

    private float GetRagdollCameraFocusBlendSpeed(bool usingRagdollOrHold, Vector3 desiredOffset)
    {
        if (!usingRagdollOrHold)
            return ragdollCameraReturnSpeed;

        float fastCatchupDistance = GetFiniteNonNegative(ragdollCameraFastCatchupDistance);
        if (desiredOffset.magnitude >= fastCatchupDistance)
            return ragdollCameraFastBlendSpeed;

        return ragdollCameraBlendSpeed;
    }

    private Vector3 GetDesiredRagdollCameraFocusLocalOffset(out bool usingRagdollOrHold)
    {
        usingRagdollOrHold = false;

        Vector3 defaultFocusWorld = transform.position;
        Vector3 desiredFocusWorld = defaultFocusWorld;

        if (TryGetActiveRagdollCameraFocus(out Vector3 ragdollFocusWorld))
        {
            desiredFocusWorld = ragdollFocusWorld;
            usingRagdollOrHold = true;
        }
        else if (ShouldHoldLastRagdollCameraFocus())
        {
            desiredFocusWorld = _lastRagdollCameraFocusWorld;
            usingRagdollOrHold = true;
        }

        Transform reference = cameraRoot != null && cameraRoot.transform.parent != null ? cameraRoot.transform.parent : transform;
        Vector3 defaultLocal = reference.InverseTransformPoint(defaultFocusWorld);
        Vector3 targetLocal = reference.InverseTransformPoint(desiredFocusWorld);
        Vector3 desiredOffset = targetLocal - defaultLocal;

        if (!IsFiniteVector3(desiredOffset))
            return Vector3.zero;

        float maxFocusDistance = GetFiniteNonNegative(ragdollCameraMaxFocusDistance);
        if (maxFocusDistance > 0f)
            desiredOffset = Vector3.ClampMagnitude(desiredOffset, maxFocusDistance);

        return desiredOffset;
    }

    private bool TryGetActiveRagdollCameraFocus(out Vector3 focusWorld)
    {
        focusWorld = default;

        if (!ShouldUseRagdollCameraFocusForThisPlayer())
            return false;

        SugaActiveRagdollController controller = ResolveActiveRagdollController();
        if (controller == null)
            return false;

        if (!controller.IsRagdollActiveForGameplay)
            return false;

        if (!controller.TryGetRagdollFocusPosition(out focusWorld))
            return false;

        focusWorld += Vector3.up * GetFiniteOrZero(ragdollCameraFocusHeightOffset);
        if (!IsFiniteVector3(focusWorld))
            return false;

        _lastRagdollCameraFocusWorld = focusWorld;
        _lastRagdollCameraFocusTime = Time.time;
        _hasLastRagdollCameraFocus = true;
        return true;
    }

    private bool ShouldHoldLastRagdollCameraFocus()
    {
        if (!ShouldUseRagdollCameraFocusForThisPlayer() || !_hasLastRagdollCameraFocus)
            return false;

        float holdDuration = GetFiniteNonNegative(ragdollCameraHoldAfterInactive);
        if (IsStandingUpForRagdollCameraHold())
            holdDuration += GetFiniteNonNegative(ragdollCameraStandUpHoldExtra);

        return holdDuration > 0f && Time.time - _lastRagdollCameraFocusTime < holdDuration;
    }

    private bool IsStandingUpForRagdollCameraHold()
    {
        return statusModule != null && statusModule.IsStandingUp;
    }

    private bool ShouldUseRagdollCameraFocusForThisPlayer()
    {
        return useRagdollFocusForCamera && IsOwner;
    }

    private static bool IsFiniteVector3(Vector3 value)
    {
        return IsFiniteFloat(value.x) && IsFiniteFloat(value.y) && IsFiniteFloat(value.z);
    }

    private static bool IsFiniteFloat(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }

    private static float GetFiniteOrZero(float value)
    {
        return IsFiniteFloat(value) ? value : 0f;
    }

    private static float GetFiniteNonNegative(float value)
    {
        return Mathf.Max(0f, GetFiniteOrZero(value));
    }

    private void UpdateCameraLocalPosition()
    {
        if (cameraRoot == null)
            return;

        Vector3 targetLocalPosition = GetTargetCameraLocalPosition();
        Vector3 ragdollFocusOffset = UpdateRagdollCameraFocusLocalOffset();
        targetLocalPosition += ragdollFocusOffset;
        bool obstructed;
        targetLocalPosition = GetObstructionAdjustedCameraLocalPosition(targetLocalPosition, out obstructed);

        if (!_cameraLocalPositionInitialized)
        {
            _cameraCurrentLocalPosition = targetLocalPosition;
            _cameraLocalPositionInitialized = true;
        }
        else
        {
            float positionStep = GetCameraPositionBlendSpeed(targetLocalPosition, obstructed) * Time.deltaTime;
            _cameraCurrentLocalPosition = Vector3.MoveTowards(_cameraCurrentLocalPosition, targetLocalPosition, positionStep);
        }

        cameraRoot.transform.localPosition = _cameraCurrentLocalPosition;
        _cameraWasObstructedLastFrame = obstructed;
    }

    private void ApplyCameraLocalPositionImmediate()
    {
        if (cameraRoot == null)
            return;

        bool obstructed;
        Vector3 targetLocalPosition = GetTargetCameraLocalPosition();
        targetLocalPosition += UpdateRagdollCameraFocusLocalOffset();
        _cameraCurrentLocalPosition = GetObstructionAdjustedCameraLocalPosition(targetLocalPosition, out obstructed);
        _cameraLocalPositionInitialized = true;
        cameraRoot.transform.localPosition = _cameraCurrentLocalPosition;
        _cameraWasObstructedLastFrame = obstructed;
    }

    private Vector3 GetObstructionAdjustedCameraLocalPosition(Vector3 targetLocalPosition, out bool obstructed)
    {
        obstructed = false;

        if (cameraRoot == null)
            return targetLocalPosition;

        if (IsSpawned && !IsOwner)
            return targetLocalPosition;

        Vector3 obstructionOrigin = GetCameraObstructionOrigin();
        Vector3 targetWorldPosition = GetCameraWorldPositionFromLocal(targetLocalPosition);
        Vector3 toCamera = targetWorldPosition - obstructionOrigin;
        float targetDistance = toCamera.magnitude;
        if (targetDistance <= 0.0001f)
            return targetLocalPosition;

        Vector3 direction = toCamera / targetDistance;
        if (!TryGetNearestCameraObstructionDistance(targetWorldPosition, out float nearestValidDistance))
            return targetLocalPosition;

        obstructed = true;

        float padding = Mathf.Max(0f, cameraCollisionPadding);
        float extraPullForward = Mathf.Max(0f, cameraCollisionExtraPullForward);
        float unclampedDistance = nearestValidDistance - padding - extraPullForward;
        float adjustedDistance = Mathf.Clamp(unclampedDistance, 0.05f, targetDistance);
        float safeMinimumDistance = Mathf.Min(Mathf.Max(0.05f, minimumCameraDistance), targetDistance);
        if (unclampedDistance >= safeMinimumDistance)
            adjustedDistance = Mathf.Max(adjustedDistance, safeMinimumDistance);

        Vector3 adjustedWorldPosition = obstructionOrigin + direction * adjustedDistance;
        return GetCameraLocalPositionFromWorld(adjustedWorldPosition);
    }

    private Vector3 GetCameraObstructionOrigin()
    {
        return GetCameraObstructionFocusWorldPosition();
    }

    private Vector3 GetCameraObstructionFocusWorldPosition()
    {
        Vector3 defaultOrigin = GetDefaultCameraObstructionOrigin();
        if (!ShouldUseLastRagdollCameraFocusForObstruction())
            return defaultOrigin;

        if (!IsFiniteVector3(_lastRagdollCameraFocusWorld))
            return defaultOrigin;

        return _lastRagdollCameraFocusWorld;
    }

    private Vector3 GetDefaultCameraObstructionOrigin()
    {
        return transform.position + Vector3.up * Mathf.Max(0f, cameraCollisionFocusHeight);
    }

    private bool ShouldUseLastRagdollCameraFocusForObstruction()
    {
        if (!useRagdollFocusForCameraObstruction || !ShouldUseRagdollCameraFocusForThisPlayer() || !_hasLastRagdollCameraFocus)
            return false;

        SugaActiveRagdollController controller = ResolveActiveRagdollController();
        if (controller != null && controller.IsRagdollActiveForGameplay)
            return true;

        return ShouldHoldLastRagdollCameraFocus();
    }

    private bool ShouldIgnoreCameraObstructionCollider(Collider hitCollider)
    {
        if (hitCollider == null)
            return true;

        Transform hitTransform = hitCollider.transform;
        if (hitTransform.root == transform.root)
            return true;

        if (cameraRoot == null)
            return false;

        Transform cameraTransform = cameraRoot.transform;
        return hitTransform == cameraTransform ||
               hitTransform.IsChildOf(cameraTransform) ||
               cameraTransform.IsChildOf(hitTransform);
    }

    private Vector3 GetCameraWorldPositionFromLocal(Vector3 localPosition)
    {
        if (cameraRoot == null || cameraRoot.transform.parent == null)
            return localPosition;

        return cameraRoot.transform.parent.TransformPoint(localPosition);
    }

    private Vector3 GetCameraLocalPositionFromWorld(Vector3 worldPosition)
    {
        if (cameraRoot == null || cameraRoot.transform.parent == null)
            return worldPosition;

        return cameraRoot.transform.parent.InverseTransformPoint(worldPosition);
    }

    private float GetCameraPositionBlendSpeed(Vector3 targetLocalPosition, bool obstructed)
    {
        float defaultBlendSpeed = Mathf.Max(0f, cameraPositionBlendSpeed);
        float collisionReturnBlendSpeed = Mathf.Max(0f, cameraCollisionReturnSpeed);

        if (obstructed)
            return Mathf.Max(defaultBlendSpeed, collisionReturnBlendSpeed);

        if (_cameraWasObstructedLastFrame && GetCameraDistanceFromObstructionOrigin(_cameraCurrentLocalPosition) < GetCameraDistanceFromObstructionOrigin(targetLocalPosition))
            return collisionReturnBlendSpeed;

        return defaultBlendSpeed;
    }

    private float GetCameraDistanceFromObstructionOrigin(Vector3 localPosition)
    {
        return Vector3.Distance(GetCameraObstructionOrigin(), GetCameraWorldPositionFromLocal(localPosition));
    }

    private bool TryGetNearestCameraObstructionDistance(Vector3 targetWorldPosition, out float nearestValidDistance)
    {
        nearestValidDistance = float.MaxValue;

        Vector3 focusOrigin = GetCameraObstructionOrigin();
        float centerDistance = Vector3.Distance(focusOrigin, targetWorldPosition);
        if (centerDistance <= 0.0001f)
            return false;

        float nearestObstructionRatio = float.MaxValue;
        EvaluateCameraObstructionSample(focusOrigin, targetWorldPosition, ref nearestObstructionRatio);

        float sampleSideOffset = Mathf.Max(0f, cameraCollisionSampleSideOffset);
        if (sampleSideOffset > 0.0001f)
        {
            Vector3 sampleRight = GetCameraCollisionSampleRight();
            EvaluateCameraObstructionSample(focusOrigin + sampleRight * sampleSideOffset, targetWorldPosition, ref nearestObstructionRatio);
            EvaluateCameraObstructionSample(focusOrigin - sampleRight * sampleSideOffset, targetWorldPosition, ref nearestObstructionRatio);
        }

        if (nearestObstructionRatio == float.MaxValue)
            return false;

        nearestValidDistance = centerDistance * nearestObstructionRatio;
        return true;
    }

    private void EvaluateCameraObstructionSample(Vector3 sampleOrigin, Vector3 targetWorldPosition, ref float nearestObstructionRatio)
    {
        Vector3 toCamera = targetWorldPosition - sampleOrigin;
        float sampleDistance = toCamera.magnitude;
        if (sampleDistance <= 0.0001f)
            return;

        Vector3 direction = toCamera / sampleDistance;
        int hitCount = Physics.SphereCastNonAlloc(
            sampleOrigin,
            Mathf.Max(0.01f, cameraCollisionRadius),
            direction,
            _cameraObstructionHits,
            sampleDistance,
            cameraCollisionMask,
            QueryTriggerInteraction.Ignore
        );

        for (int i = 0; i < hitCount; i++)
        {
            Collider hitCollider = _cameraObstructionHits[i].collider;
            if (hitCollider == null || ShouldIgnoreCameraObstructionCollider(hitCollider))
                continue;

            float obstructionRatio = _cameraObstructionHits[i].distance / sampleDistance;
            if (obstructionRatio < nearestObstructionRatio)
                nearestObstructionRatio = obstructionRatio;
        }
    }

    private Vector3 GetCameraCollisionSampleRight()
    {
        Camera playerCamera = PlayerCamera;
        if (playerCamera != null && playerCamera.transform.right.sqrMagnitude > 0.0001f)
            return playerCamera.transform.right.normalized;

        if (cameraRoot != null && cameraRoot.transform.right.sqrMagnitude > 0.0001f)
            return cameraRoot.transform.right.normalized;

        return transform.right;
    }

    private float GetProcessedYawDelta(float yawDelta, Vector2 moveInput, bool allowLook)
    {
        if (!allowLook)
            return 0f;

        float scaledYawDelta = yawDelta * Mathf.Max(0f, manualYawInputScale);
        if (Mathf.Abs(yawDelta) > Mathf.Max(0f, cameraYawInputDeadzone))
            return scaledYawDelta;

        if (moveInput.sqrMagnitude > 0.001f)
            return 0f;

        float yawError = Mathf.DeltaAngle(transform.eulerAngles.y, _defaultQuarterViewYaw);
        float yawReturnStep = Mathf.Max(0f, cameraYawReturnSpeed) * Time.deltaTime;
        return Mathf.Clamp(yawError, -yawReturnStep, yawReturnStep);
    }

    private void TickServer()
    {
        if (CharacterController == null || !CharacterController.enabled) return;

        bool jumped = false;
        float serverYawDelta = AllowLookInput() ? _yawDelta : 0f;

        if (locomotionModule != null)
            jumped = locomotionModule.TickServer(_moveInput, serverYawDelta, _jumpPressed, _sprintHeld);

        if (jumped && animModule != null) animModule.TriggerJump();

        if (animModule != null && locomotionModule != null)
            animModule.TickServer(locomotionModule);

        _jumpPressed = false;
        _yawDelta = 0f;
    }

    [ServerRpc(Delivery = RpcDelivery.Unreliable)]
    private void SubmitInputServerRpc(Vector2 move, float yawDelta, bool sprintHeld)
    {
        _moveInput = move;
        _yawDelta = yawDelta;
        _sprintHeld = sprintHeld;
    }

    [ServerRpc(Delivery = RpcDelivery.Reliable)]
    private void QueueJumpServerRpc()
    {
        _jumpPressed = true;
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

        // 실제 타격 판정은 공격 애니메이션 이벤트에서 처리합니다.
        // (PickupAnimEventRelay -> AnimEvent_AttackHit -> PlayerCombatModule.DoAttackServer)

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
