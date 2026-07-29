using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

public class PlayerHub : NetworkBehaviour
{
    public enum PostItPeelHoldState
    {
        Idle,
        Tracking,
        Completed,
        Cancelled,
        Cooldown
    }

    private const string InputRouteTargetName = "Hamster_JointFreeMotorShell_MainScenes";
    private const float InputRouteLogInterval = 0.5f;
    private const float ExactPostItPeelVisualRayTolerance = 0.1f;
    private const string GameplayPhaseJumpLockReason = "GameState:GuessingResults";
    private const string PostItHeavyJumpLockReason = "PostIt:Heavy";
    private const string RuntimeMainCameraTag = "MainCamera";
    private const string SceneMainCameraName = "Main Camera";
    private const float PostItWorldRecoveryEndpointGroundMinUpDot = 0.35f;
    private const int PostItPhysicsHitBufferSize = 64;
    private const int DefaultNetworkFaceExpressionId = 0;
    private const int MaximumNetworkFaceExpressionId = 4;

    private readonly NetworkVariable<byte> _currentFaceExpressionId =
        new NetworkVariable<byte>(
            DefaultNetworkFaceExpressionId,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

    [Header("Refs")]
    [Tooltip("로컬 소유자만 활성화할 카메라 루트")]
    [SerializeField] private GameObject cameraRoot;

    [Tooltip("로컬 소유자만 활성화할 AudioListener")]
    [SerializeField] private AudioListener audioListener;

    [Header("Camera Settings")]
    [Tooltip("기본 세미 고정 쿼터뷰 피치 각도입니다. 값이 클수록 더 아래를 내려다봅니다.")]
    [SerializeField] private float defaultQuarterViewPitch = 28f;

#pragma warning disable 0414 // Preserve serialized legacy recenter settings without using auto recenter.
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
#pragma warning restore 0414

    [Tooltip("이동 중 장면 가독성을 위해 현재 카메라 로컬 위치에 추가할 프레이밍 오프셋입니다.")]
    [SerializeField] private Vector3 cameraMoveFramingOffset = new Vector3(0f, 0.2f, -0.35f);

    [Tooltip("카메라 로컬 위치가 목표 프레이밍으로 따라가는 속도입니다.")]
    [SerializeField] private float cameraPositionBlendSpeed = 6f;

    [Tooltip("카메라 위치가 목표 위치로 즉시 이동하지 않고 부드럽게 따라가도록 합니다.")]
    [SerializeField] private bool enableCameraPositionSmoothing = true;

    [Tooltip("카메라 위치 smoothing 시간입니다. 낮을수록 즉시 반응합니다.")]
    [SerializeField] private float cameraPositionSmoothTime = 0.08f;

    [Tooltip("목표 위치와 너무 멀어지면 smoothing하지 않고 즉시 스냅합니다.")]
    [SerializeField] private float cameraPositionMaxSmoothDistance = 4f;

    [Tooltip("오브젝트 가림 방지로 카메라가 플레이어 쪽으로 가까워질 때는 즉시 반영합니다.")]
    [SerializeField] private bool cameraObstructionSnapInward = true;

    [Tooltip("카메라 smoothing 디버그 로그를 출력합니다.")]
    [SerializeField] private bool cameraSmoothingDebugLogs = false;

    [Header("MotorShell Diagnostics")]
    [SerializeField] private bool debugMovementRoutingLogs = false;
    [SerializeField] private bool debugCameraLogs = false;

    [Header("Ragdoll Camera Focus")]
    [SerializeField, Tooltip("Ragdoll 활성 중 카메라가 Ragdoll 중심 위치를 따라가게 할지 여부입니다.")]
    private bool useRagdollFocusForCamera = true;

    [SerializeField, Tooltip("Ragdoll 중심 위치에 더할 카메라 focus 높이 보정값입니다.")]
    private float ragdollCameraFocusHeightOffset = 0.4f;

    [SerializeField, Tooltip("Ragdoll focus가 플레이어 루트 기준 이 높이보다 아래로 내려가지 않도록 보정합니다.")]
    private float ragdollCameraMinimumFocusHeight = 0.85f;

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
    private float _cameraRootYawOffset;
    private bool _cameraRootYawOffsetCaptured;
    private Vector3 _cameraCurrentLocalPosition;
    private bool _cameraLocalPositionInitialized;
    private Vector3 _cameraPositionSmoothVelocity;
    private bool _hasSmoothedCameraPosition;
    private bool _cameraSmoothingWasSnappingLastFrame;
    private string _lastCameraSmoothingSnapReason;
    private bool _cameraWasObstructedLastFrame;
    private bool _hasLocalCameraOverride;
    private bool _isLocalCameraOverrideMutationInProgress;
    private UnityEngine.Object _localCameraOverrideRequester;
    private Camera _localCameraOverrideCamera;
    private AudioListener _localCameraOverrideAudioListener;
    private GameObject _localCameraOverrideGameObject;
    private Camera _localCameraOverrideOriginalPlayerCamera;
    private AudioListener _localCameraOverrideOriginalPlayerAudioListener;
    private GameObject _localCameraOverrideOriginalPlayerCameraGameObject;
    private bool _localCameraOverrideOriginalPlayerCameraEnabled;
    private string _localCameraOverrideOriginalPlayerCameraTag;
    private bool _localCameraOverrideOriginalPlayerAudioListenerEnabled;
    private bool _localCameraOverrideCameraGameObjectWasActive;
    private bool _localCameraOverrideCameraEnabled;
    private string _localCameraOverrideCameraOriginalTag;
    private bool _localCameraOverrideAudioListenerEnabled;
    private Camera _localCameraHandoffPreservedCamera;
    private AudioListener _localCameraHandoffPreservedAudioListener;
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

    [Header("Basic Attack Stamina")]
    [SerializeField, Tooltip("기본 공격 시 스테미너를 소비하고 부족하면 공격을 제한할지 여부입니다.")]
    private bool useStaminaForBasicAttack = true;

    [SerializeField, Tooltip("기본 공격 1회에 소비되는 스테미너 양입니다.")]
    private float basicAttackStaminaCost = 6f;

    [SerializeField, Tooltip("기본 공격을 시작하기 위해 필요한 최소 스테미너입니다.")]
    private float basicAttackMinimumStaminaToStart = 6f;

    [SerializeField, Tooltip("스테미너 모듈을 찾지 못했을 때 기존처럼 기본 공격을 허용할지 여부입니다.")]
    private bool allowBasicAttackWhenStaminaModuleMissing = true;

    [Header("Spawn Settings")]
    [Tooltip("이 씬들에서는 초기 Owner 스폰 보정 루틴을 건너뜁니다. 인게임 씬은 InGameMatchManager가 배치를 전담하도록 비워두지 않는 것을 권장합니다.")]
    [SerializeField] private string[] skipInitialSpawnScenes = new[] { "InGame" };

    [Header("Post-it Peel")]
    [SerializeField, Min(0f)] private float postItPeelSelectionDistance = 6f;
    [SerializeField, Min(0f)] private float postItPeelServerDistance = 2f;
    [SerializeField, Min(0f)] private float postItPeelBodyRayRadius = 0.2f;
    [SerializeField, Min(0f)] private float postItPeelVisualRayTolerance = 0.3f;
    [SerializeField, Range(-1f, 1f)] private float postItPeelAimDirectionMinDot = 0.9f;
    [SerializeField, Range(-1f, 1f)] private float postItPeelMinForwardDot = 0.25f;
    [SerializeField, Min(0f)] private float postItPeelCooldown = 0.2f;
    [SerializeField, Min(0f)] private float postItPeelHoldDuration = 0.75f;

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

    [Tooltip("플레이어의 코인 보유량과 낙사 페널티 계산을 담당하는 모듈입니다. 비워두면 자식에서 자동 탐색합니다.")]
    [SerializeField] private PlayerCoinWalletModule coinWalletModule;

    [Tooltip("플레이어의 스테미너 수치와 회복 처리를 담당하는 모듈입니다. 비워두면 자식에서 자동 탐색합니다.")]
    [SerializeField] private PlayerStaminaModule staminaModule;

    [Tooltip("현재 게임 상태를 확인할 매니저입니다. 비워두면 씬에서 자동 탐색합니다.")]
    [SerializeField] private GameStateManager gameStateManager;

    private ReadySystem _readySystem;

    public bool IsCursorLocked => inputModule != null && inputModule.IsCursorLocked;

    public CharacterController CharacterController => GetComponentInChildren<CharacterController>(true);
    public Animator Animator => GetComponentInChildren<Animator>(true);
    public Camera PlayerCamera => GetComponentInChildren<Camera>(true);
    public bool IsLocalCameraOverrideActive =>
        _hasLocalCameraOverride &&
        !_isLocalCameraOverrideMutationInProgress &&
        _localCameraOverrideRequester != null &&
        _localCameraOverrideCamera != null &&
        _localCameraOverrideAudioListener != null;
    public Camera ActiveLocalCamera =>
        IsLocalCameraOverrideActive ? _localCameraOverrideCamera : PlayerCamera;
    public PlayerCoinWalletModule CoinWalletModule => coinWalletModule;
    public PlayerStaminaModule StaminaModule => staminaModule;
    public PostItPeelHoldState CurrentPostItPeelHoldState => _postItPeelHoldState;
    public bool IsPostItPeelTracking => _postItPeelHoldState == PostItPeelHoldState.Tracking;
    public float PostItPeelHoldProgress01
    {
        get
        {
            if (_postItPeelHoldState == PostItPeelHoldState.Completed)
                return 1f;

            if (_postItPeelHoldState != PostItPeelHoldState.Tracking)
                return 0f;

            float duration = Mathf.Max(0.01f, postItPeelHoldDuration);
            return Mathf.Clamp01((Time.unscaledTime - _postItPeelHoldStartedAt) / duration);
        }
    }

    private Vector2 _moveInput;
    private Vector2 _ownerPresentationMoveInput;
    private float _yawDelta;
    private float _pitchDelta;
    private bool _jumpPressed;
    private bool _sprintHeld;
    private bool _gameplayPhaseLockedServer;
    private bool _isGameplayStateChangeSubscribed;
    private bool _isAuthoritativeFaceExpressionSubscribed;
    private bool _isReplicatedFaceExpressionSubscribed;
    private float _nextInputRouteOwnerLogTime;
    private float _nextInputRouteServerLogTime;
    private float _nextMotorShellCameraLogTime;
    private HamsterFullRagdollMotor _motorShellMotor;
    private HamsterMotorShellCombatAdapter _motorShellCombatAdapter;
    private HamsterMotorShellItemAdapter _motorShellItemAdapter;
    private HamsterMotorShellSpinDashAdapter _motorShellSpinDashAdapter;
    private HamsterMotorShellRagdollRecoveryAdapter _motorShellRecoveryAdapter;
    private HamsterRagdollGrabber _ragdollGrabber;
    private HamsterRagdollGrabbable _ragdollGrabbable;
    private PlayerPostItInventory _postItInventory;
    private PlayerPostItInventory _subscribedPostItEffectInventory;
    private PostItRoundManager _postItRoundManager;
    private FaceExpressionController _faceExpressionController;
    private readonly RaycastHit[] _postItPhysicsHitBuffer =
        new RaycastHit[PostItPhysicsHitBufferSize];
    private readonly List<Collider> _postItBodyColliderBuffer = new List<Collider>(16);
    private int _postItPeelEvaluatedFrame = -1;
    private bool _postItPeelConsumedInEvaluatedFrame;
    private int _characterGrabReservedInteractFrame = -1;
    private float _nextPostItPeelServerTime;
    private PostItPeelHoldState _postItPeelHoldState;
    private ulong _postItPeelTrackedTargetNetworkObjectId = ulong.MaxValue;
    private int _postItPeelTrackedPostItId = -1;
    private float _postItPeelHoldStartedAt;
    private float _postItPeelLocalCooldownUntil;

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
        InitializeFaceNetworkState();
        CacheCameraDefaults();
        ApplyOwnerVisuals();
        ApplyDefaultCameraPitchImmediate();
        SubscribeGameplayStateChanges();
        SubscribePostItEffectChanges();

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

    public override void OnNetworkDespawn()
    {
        UnsubscribeFaceNetworkState();
        ForceEndLocalCameraOverride(false);
        ResetPostItPeelHoldState();
        UnsubscribePostItEffectChanges();
        ReleaseOwnedPostItHeavyJumpLock();
        UnsubscribeGameplayStateChanges();
        ReleaseOwnedGameplayPhaseJumpLock();
        _gameplayPhaseLockedServer = false;
        ResetCameraPositionSmoothingState();
        base.OnNetworkDespawn();
    }

    private void OnDisable()
    {
        ForceEndLocalCameraOverride(false);
        ResetPostItPeelHoldState();
        ResetCameraPositionSmoothingState();
    }

    private void OnDestroy()
    {
        UnsubscribeFaceNetworkState();
        ForceEndLocalCameraOverride(false);
        ResetPostItPeelHoldState();
        UnsubscribePostItEffectChanges();
        ReleaseOwnedPostItHeavyJumpLock();
        UnsubscribeGameplayStateChanges();
        ReleaseOwnedGameplayPhaseJumpLock();
        ResetCameraPositionSmoothingState();
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
        if (coinWalletModule == null) coinWalletModule = GetComponentInChildren<PlayerCoinWalletModule>(true);
        if (staminaModule == null) staminaModule = GetComponentInChildren<PlayerStaminaModule>(true);
        if (_motorShellMotor == null) _motorShellMotor = GetComponentInChildren<HamsterFullRagdollMotor>(true);
        if (_motorShellItemAdapter == null) _motorShellItemAdapter = GetComponentInChildren<HamsterMotorShellItemAdapter>(true);
        if (_motorShellSpinDashAdapter == null) _motorShellSpinDashAdapter = GetComponentInChildren<HamsterMotorShellSpinDashAdapter>(true);
        if (_motorShellRecoveryAdapter == null) _motorShellRecoveryAdapter = GetComponentInChildren<HamsterMotorShellRagdollRecoveryAdapter>(true);
        if (_ragdollGrabber == null) _ragdollGrabber = GetComponentInChildren<HamsterRagdollGrabber>(true);
        if (_ragdollGrabbable == null) _ragdollGrabbable = GetComponentInChildren<HamsterRagdollGrabbable>(true);
        if (_postItInventory == null) _postItInventory = GetComponentInChildren<PlayerPostItInventory>(true);
        ResolveFaceExpressionController();
        ResolveMotorShellCombatAdapter();
        if (gameStateManager == null) gameStateManager = FindFirstObjectByType<GameStateManager>();
        if (_readySystem == null) _readySystem = FindFirstObjectByType<ReadySystem>();
    }

    private HamsterMotorShellCombatAdapter ResolveMotorShellCombatAdapter()
    {
        if (_motorShellCombatAdapter == null)
            _motorShellCombatAdapter = GetComponentInChildren<HamsterMotorShellCombatAdapter>(true);

        if (_motorShellCombatAdapter == null)
            return null;

        NetworkObject hubNetworkObject = GetComponentInParent<NetworkObject>();
        NetworkObject adapterNetworkObject = _motorShellCombatAdapter.GetComponentInParent<NetworkObject>();
        if (hubNetworkObject != null && adapterNetworkObject == hubNetworkObject)
            return _motorShellCombatAdapter;

        _motorShellCombatAdapter = null;
        return null;
    }

    private FaceExpressionController ResolveFaceExpressionController()
    {
        if (_faceExpressionController != null)
            return _faceExpressionController;

        FaceExpressionController candidate =
            GetComponentInChildren<FaceExpressionController>(true);
        if (candidate == null)
            return null;

        NetworkObject hubNetworkObject = GetComponentInParent<NetworkObject>();
        NetworkObject faceNetworkObject = candidate.GetComponentInParent<NetworkObject>();
        if (hubNetworkObject != null && faceNetworkObject == hubNetworkObject)
            _faceExpressionController = candidate;

        return _faceExpressionController;
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

    public bool TryBeginLocalCameraOverride(
        UnityEngine.Object requester,
        Camera overrideCamera,
        AudioListener overrideAudioListener)
    {
        if (_isLocalCameraOverrideMutationInProgress)
            return false;

        if (_hasLocalCameraOverride)
        {
            return IsLocalCameraOverrideActive &&
                   ReferenceEquals(_localCameraOverrideRequester, requester) &&
                   ReferenceEquals(_localCameraOverrideCamera, overrideCamera) &&
                   ReferenceEquals(_localCameraOverrideAudioListener, overrideAudioListener);
        }

        if (requester == null ||
            overrideCamera == null ||
            overrideAudioListener == null ||
            overrideAudioListener.gameObject != overrideCamera.gameObject ||
            !IsOwner ||
            !IsSpawned ||
            !isActiveAndEnabled ||
            statusModule == null ||
            !statusModule.IsEliminated ||
            !TryGetGameState(out GameStateManager.GameState state) ||
            state != GameStateManager.GameState.Playing)
        {
            return false;
        }

        Camera playerCamera = PlayerCamera;
        if (playerCamera == null || playerCamera == overrideCamera)
            return false;

        GameObject overrideGameObject = overrideCamera.gameObject;
        GameObject playerCameraGameObject = playerCamera.gameObject;
        Scene overrideScene = overrideGameObject.scene;
        if (!overrideScene.IsValid() ||
            !overrideScene.isLoaded ||
            overrideGameObject.GetComponentInParent<NetworkObject>(true) != null ||
            overrideAudioListener.GetComponentInParent<NetworkObject>(true) != null)
        {
            return false;
        }

        AudioListener playerAudioListener = audioListener != null
            ? audioListener
            : playerCamera.GetComponent<AudioListener>();

        bool originalPlayerCameraEnabled;
        string originalPlayerCameraTag;
        bool originalPlayerAudioListenerEnabled;
        bool overrideCameraGameObjectWasActive;
        bool overrideCameraEnabled;
        string overrideCameraOriginalTag;
        bool overrideAudioListenerEnabled;

        try
        {
            originalPlayerCameraEnabled = playerCamera.enabled;
            originalPlayerCameraTag = playerCameraGameObject.tag;
            originalPlayerAudioListenerEnabled =
                playerAudioListener != null && playerAudioListener.enabled;
            overrideCameraGameObjectWasActive = overrideGameObject.activeSelf;
            overrideCameraEnabled = overrideCamera.enabled;
            overrideCameraOriginalTag = overrideGameObject.tag;
            overrideAudioListenerEnabled = overrideAudioListener.enabled;
        }
        catch (System.Exception)
        {
            return false;
        }

        _localCameraOverrideRequester = requester;
        _localCameraOverrideCamera = overrideCamera;
        _localCameraOverrideAudioListener = overrideAudioListener;
        _localCameraOverrideGameObject = overrideGameObject;
        _localCameraOverrideOriginalPlayerCamera = playerCamera;
        _localCameraOverrideOriginalPlayerAudioListener = playerAudioListener;
        _localCameraOverrideOriginalPlayerCameraGameObject = playerCameraGameObject;
        _localCameraOverrideOriginalPlayerCameraEnabled = originalPlayerCameraEnabled;
        _localCameraOverrideOriginalPlayerCameraTag = originalPlayerCameraTag;
        _localCameraOverrideOriginalPlayerAudioListenerEnabled = originalPlayerAudioListenerEnabled;
        _localCameraOverrideCameraGameObjectWasActive = overrideCameraGameObjectWasActive;
        _localCameraOverrideCameraEnabled = overrideCameraEnabled;
        _localCameraOverrideCameraOriginalTag = overrideCameraOriginalTag;
        _localCameraOverrideAudioListenerEnabled = overrideAudioListenerEnabled;
        _hasLocalCameraOverride = true;
        _isLocalCameraOverrideMutationInProgress = true;

        try
        {
            ApplyLocalCameraOverridePresentation();
            ClearPendingGameplayInput();
            ResetCameraPositionSmoothingState();
            _isLocalCameraOverrideMutationInProgress = false;
            if (!IsLocalCameraOverridePresentationValid())
            {
                ForceEndLocalCameraOverride(false, true);
                return false;
            }

            return true;
        }
        catch (System.Exception)
        {
            _isLocalCameraOverrideMutationInProgress = false;
            ForceEndLocalCameraOverride(false, true);
            return false;
        }
    }

    public bool TryEndLocalCameraOverride(UnityEngine.Object requester)
    {
        if (!IsLocalCameraOverrideActive ||
            requester == null ||
            !ReferenceEquals(_localCameraOverrideRequester, requester))
        {
            return false;
        }

        ForceEndLocalCameraOverride(true);
        return true;
    }

    private void ApplyOwnerVisuals()
    {
        bool active = IsOwner;
        if (!active && _hasLocalCameraOverride)
            ForceEndLocalCameraOverride(false);

        if (cameraRoot != null) cameraRoot.SetActive(active);
        if (audioListener != null)
            audioListener.enabled = active && !_hasLocalCameraOverride;
        if (interactModule != null) interactModule.SetOwnerMode(active);

        ApplyInputRouteOwnerCameraHandoff();
    }

    private bool CanMoveNow()
    {
        return !IsGameplayPhaseLocked() &&
               (statusModule == null || statusModule.CanMove);
    }

    private bool CanAttackNow()
    {
        return !IsGameplayPhaseLocked() &&
               (statusModule == null || statusModule.CanAttack);
    }

    private bool CanInteractNow()
    {
        return !IsGameplayPhaseLocked() &&
               (statusModule == null || statusModule.CanInteract);
    }
    private bool IsPlayingState()
    {
        return TryGetGameState(out GameStateManager.GameState state) &&
               state == GameStateManager.GameState.Playing;
    }

    private bool AllowOwnerLookInput()
    {
        return IsPlayingState();
    }

    private bool AllowServerLookInput()
    {
        return IsPlayingState();
    }

    private void InitializeFaceNetworkState()
    {
        if (!IsSpawned)
            return;

        FaceExpressionController faceController = ResolveFaceExpressionController();
        if (IsServer)
        {
            if (!_isAuthoritativeFaceExpressionSubscribed && faceController != null)
            {
                faceController.ExpressionChanged +=
                    HandleAuthoritativeFaceExpressionChanged;
                _isAuthoritativeFaceExpressionSubscribed = true;
            }

            int currentExpressionId = faceController != null
                ? faceController.CurrentExpressionId
                : DefaultNetworkFaceExpressionId;
            if (!IsValidNetworkFaceExpressionId(currentExpressionId))
            {
                faceController?.SetFaceIndex(DefaultNetworkFaceExpressionId);
                currentExpressionId = DefaultNetworkFaceExpressionId;
            }

            ServerSetNetworkFaceExpression(currentExpressionId);
            return;
        }

        if (!_isReplicatedFaceExpressionSubscribed)
        {
            _currentFaceExpressionId.OnValueChanged +=
                HandleReplicatedFaceExpressionChanged;
            _isReplicatedFaceExpressionSubscribed = true;
        }

        ApplyReplicatedFaceExpression(_currentFaceExpressionId.Value);
    }

    private void UnsubscribeFaceNetworkState()
    {
        if (_isAuthoritativeFaceExpressionSubscribed)
        {
            if (_faceExpressionController != null)
            {
                _faceExpressionController.ExpressionChanged -=
                    HandleAuthoritativeFaceExpressionChanged;
            }

            _isAuthoritativeFaceExpressionSubscribed = false;
        }

        if (_isReplicatedFaceExpressionSubscribed)
        {
            _currentFaceExpressionId.OnValueChanged -=
                HandleReplicatedFaceExpressionChanged;
            _isReplicatedFaceExpressionSubscribed = false;
        }
    }

    private void HandleAuthoritativeFaceExpressionChanged(int expressionId)
    {
        ServerSetNetworkFaceExpression(expressionId);
    }

    private void HandleReplicatedFaceExpressionChanged(
        byte previousExpressionId,
        byte currentExpressionId)
    {
        ApplyReplicatedFaceExpression(currentExpressionId);
    }

    private void ApplyReplicatedFaceExpression(byte expressionId)
    {
        if (!IsSpawned || IsServer)
            return;

        int safeExpressionId = IsValidNetworkFaceExpressionId(expressionId)
            ? expressionId
            : DefaultNetworkFaceExpressionId;
        ResolveFaceExpressionController()?.SetFaceIndex(safeExpressionId);
    }

    private void ServerSetNetworkFaceExpression(int expressionId)
    {
        if (!IsSpawned ||
            !IsServer ||
            !IsValidNetworkFaceExpressionId(expressionId))
        {
            return;
        }

        byte networkExpressionId = (byte)expressionId;
        if (_currentFaceExpressionId.Value != networkExpressionId)
            _currentFaceExpressionId.Value = networkExpressionId;
    }

    private void ServerResetFaceExpressionToDefault()
    {
        if (!IsSpawned || !IsServer)
            return;

        ResolveFaceExpressionController()?.SetFaceIndex(
            DefaultNetworkFaceExpressionId);
        ServerSetNetworkFaceExpression(DefaultNetworkFaceExpressionId);
    }

    private static bool IsValidNetworkFaceExpressionId(int expressionId)
    {
        return expressionId >= DefaultNetworkFaceExpressionId &&
               expressionId <= MaximumNetworkFaceExpressionId;
    }

    private bool TryGetGameState(out GameStateManager.GameState state)
    {
        if (gameStateManager == null)
            gameStateManager = FindFirstObjectByType<GameStateManager>();

        if (gameStateManager == null)
        {
            state = default;
            return false;
        }

        state = gameStateManager.GetState();
        return true;
    }

    private bool IsGameplayPhaseLocked()
    {
        return !TryGetGameState(out GameStateManager.GameState state) ||
               IsGameplayPhaseLocked(state);
    }

    private static bool IsGameplayPhaseLocked(GameStateManager.GameState state)
    {
        return state != GameStateManager.GameState.Playing;
    }

    private void SubscribeGameplayStateChanges()
    {
        if (_isGameplayStateChangeSubscribed ||
            !IsSpawned ||
            (!IsOwner && !IsServer))
        {
            return;
        }

        if (gameStateManager == null)
            gameStateManager = FindFirstObjectByType<GameStateManager>();

        if (gameStateManager == null)
            return;

        gameStateManager.StateValue.OnValueChanged += HandleGameplayStateChanged;
        _isGameplayStateChangeSubscribed = true;
        ApplyCurrentGameplayPhaseLock();
    }

    private void UnsubscribeGameplayStateChanges()
    {
        if (!_isGameplayStateChangeSubscribed)
            return;

        if (gameStateManager != null)
        {
            gameStateManager.StateValue.OnValueChanged -=
                HandleGameplayStateChanged;
        }

        _isGameplayStateChangeSubscribed = false;
    }

    private void HandleGameplayStateChanged(
        int previousStateValue,
        int newStateValue)
    {
        GameStateManager.GameState newState =
            (GameStateManager.GameState)newStateValue;
        bool wasGameplayLocked =
            IsGameplayPhaseLocked((GameStateManager.GameState)previousStateValue);
        bool gameplayLocked = IsGameplayPhaseLocked(newState);
        if (!IsServer && !wasGameplayLocked && gameplayLocked)
            ClearPendingGameplayInput();

        if (IsServer)
        {
            if (gameplayLocked)
                ServerResetFaceExpressionToDefault();

            ApplyGameplayPhaseLockServer(gameplayLocked);
        }
    }

    private void ApplyCurrentGameplayPhaseLock()
    {
        bool gameplayLocked = IsGameplayPhaseLocked();
        if (!IsServer && gameplayLocked)
            ClearPendingGameplayInput();

        if (IsServer)
        {
            if (gameplayLocked)
                ServerResetFaceExpressionToDefault();

            ApplyGameplayPhaseLockServer(gameplayLocked);
        }
    }

    private void ApplyGameplayPhaseLockServer(bool gameplayLocked)
    {
        if (!IsServer)
            return;

        if (_gameplayPhaseLockedServer == gameplayLocked)
            return;

        _gameplayPhaseLockedServer = gameplayLocked;
        if (!gameplayLocked)
        {
            ReleaseOwnedGameplayPhaseJumpLock();
            return;
        }

        ClearPendingGameplayInput();
        _attackBufferedServer = false;
        _attackBufferedAtServer = 0f;
        ApplyOwnedGameplayPhaseJumpLock();

        if (interactModule != null && interactModule.IsCharacterGrabBusy)
            interactModule.ServerReleaseCharacterGrab("GameplayPhaseLock");

        if (locomotionModule != null && locomotionModule.IsSpinDashing)
            locomotionModule.ServerCancelSpinDash(false);
    }

    private void ClearPendingGameplayInput()
    {
        NeutralizeRoutedGameplayInput();
        _postItPeelEvaluatedFrame = -1;
        _postItPeelConsumedInEvaluatedFrame = false;
        _characterGrabReservedInteractFrame = -1;
        CancelPostItPeelHold();
    }

    private void NeutralizeRoutedGameplayInput()
    {
        _moveInput = Vector2.zero;
        _ownerPresentationMoveInput = Vector2.zero;
        _yawDelta = 0f;
        _pitchDelta = 0f;
        _jumpPressed = false;
        _sprintHeld = false;
    }

    private void ApplyOwnedGameplayPhaseJumpLock()
    {
        HamsterFullRagdollMotor motorShellMotor = ResolveMotorShellMotor();
        if (motorShellMotor == null)
            return;

        if (motorShellMotor.IsExternalJumpLocked)
            return;

        motorShellMotor.SetExternalJumpLock(
            true,
            GameplayPhaseJumpLockReason);
    }

    private void ReleaseOwnedGameplayPhaseJumpLock()
    {
        HamsterFullRagdollMotor motorShellMotor = _motorShellMotor;
        if (motorShellMotor == null ||
            !motorShellMotor.IsExternalJumpLocked ||
            motorShellMotor.ExternalJumpLockReason != GameplayPhaseJumpLockReason)
        {
            return;
        }

        motorShellMotor.SetExternalJumpLock(false, "GameState:Restore");
    }

    private ReadySystem ResolveReadySystem()
    {
        if (_readySystem == null)
            _readySystem = FindFirstObjectByType<ReadySystem>();

        return _readySystem;
    }

    private void Update()
    {
        SubscribeGameplayStateChanges();
        if (IsOwner) TickOwner();
        if (IsServer) TickServer();
    }

    private void TickOwner()
    {
        if (_hasLocalCameraOverride)
        {
            ClearPendingGameplayInput();
            if (IsOwner && IsSpawned)
                SubmitInputServerRpc(Vector2.zero, 0f, false);

            ApplyInputRouteOwnerCameraHandoff();
            return;
        }

        if (inputModule == null)
        {
            CancelPostItPeelHold();
            LogInputRouteOwner(Vector2.zero, Vector2.zero, false, false, false, false, "inputModule null");
            return;
        }

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

        Vector2 rawMove = move;
        bool rawSprintHeld = sprintHeld;
        float rawYawDelta = yawDelta;
        float rawPitchDelta = pitchDelta;
        bool gameplayPhaseLocked = IsGameplayPhaseLocked();
        if (gameplayPhaseLocked)
        {
            move = Vector2.zero;
            yawDelta = 0f;
            pitchDelta = 0f;
            jumpPressed = false;
            sprintHeld = false;
            attackPressed = false;
            interactPressed = false;
            dropPressed = false;
        }

        Vector2 requestedMove = move;
        bool requestedSprintHeld = sprintHeld;
        bool postItAllowsJump = ApplyOwnerPostItInputPresentation(ref move, ref sprintHeld);

        float cameraPivotYawBefore = GetTransformYawForLog(cameraRoot != null ? cameraRoot.transform : null);
        Camera playerCameraBefore = PlayerCamera;
        float playerCameraPitchBefore = GetTransformPitchForLog(playerCameraBefore != null ? playerCameraBefore.transform : null);
        bool allowLook = AllowOwnerLookInput();

        if (!allowLook)
        {
            yawDelta = 0f;
            pitchDelta = 0f;
        }

        yawDelta = GetProcessedYawDelta(yawDelta, move, allowLook);
        ApplyInputRouteCameraYawOffset(yawDelta);

        _ownerPresentationMoveInput = move;
        _yawDelta = yawDelta;
        _pitchDelta = pitchDelta;

        ApplyInputRouteOwnerCameraHandoff();

        if (allowLook)
        {
            HandleCameraRotation(_pitchDelta);
        }
        else
        {
            HandleCameraRotation(0f);
        }

        TickPostItPeelHold();

        bool canMoveNow = CanMoveNow();
        if (!canMoveNow)
        {
            _ownerPresentationMoveInput = Vector2.zero;
            _yawDelta = 0f;
            requestedMove = Vector2.zero;
            requestedSprintHeld = false;
        }

        SubmitInputServerRpc(requestedMove, _yawDelta, requestedSprintHeld);
        string routeResult = gameplayPhaseLocked
            ? "gameplay phase locked"
            : (canMoveNow ? "submitted" : "CanMoveNow false");
        LogInputRouteOwner(rawMove, _ownerPresentationMoveInput, jumpPressed, rawSprintHeld, sprintHeld, allowLook, routeResult);
        Camera playerCameraAfter = PlayerCamera;
        LogMotorShellCameraInput(
            rawYawDelta,
            rawPitchDelta,
            allowLook,
            cameraPivotYawBefore,
            GetTransformYawForLog(cameraRoot != null ? cameraRoot.transform : null),
            playerCameraPitchBefore,
            GetTransformPitchForLog(playerCameraAfter != null ? playerCameraAfter.transform : null),
            routeResult);

        if (jumpPressed)
        {
            if (interactModule != null && interactModule.IsGrabbedByCharacter)
                interactModule.RequestCharacterGrabEscapeTap();
            else if (postItAllowsJump)
                QueueJumpServerRpc();
        }

        if (attackPressed)
        {
            if (interactModule != null && interactModule.IsGrabbingCharacter)
                interactModule.RequestThrowCarriedCharacter();
            else if (CanAttackNow())
                AttackServerRpc();
        }

        bool consumedInteractForSpinDash = false;
        if (interactPressed &&
            sprintHeld &&
            CanMoveNow() &&
            HasAvailableSpinDashRoute())
        {
            if (IsServer)
            {
                ServerTryStartSpinDashOnAvailableRoute();
            }
            else
            {
                RequestSpinDashServerRpc();
            }

            consumedInteractForSpinDash = true;
        }

        bool consumedInteractForPostItPeel =
            interactPressed &&
            !consumedInteractForSpinDash &&
            TryConsumePostItPeelInteractThisFrame();

        if (interactPressed &&
            !consumedInteractForSpinDash &&
            !consumedInteractForPostItPeel &&
            CanInteractNow() &&
            interactModule != null)
        {
            if (!interactModule.HasHeldItem() && !interactModule.IsGrabbingCharacter)
            {
                if (interactModule.TryFindPickupTarget(out NetworkObjectReference target))
                {
                    TryPickupServerRpc(target);
                }
            }
        }

        if (dropPressed)
        {
            if (interactModule != null && interactModule.IsGrabbingCharacter)
            {
                if (IsServer)
                    interactModule.ServerReleaseCharacterGrab("DropInput");
                else
                    interactModule.RequestReleaseCharacterGrab();
            }
            else
            {
                DropItemServerRpc();
            }
        }
    }

    [ServerRpc]
    private void DropItemServerRpc()
    {
        if (IsGameplayPhaseLocked())
        {
            ClearPendingGameplayInput();
            return;
        }

        if (interactModule != null && interactModule.IsGrabbingCharacter)
        {
            interactModule.ServerReleaseCharacterGrab("DropInput");
            return;
        }

        if (!CanInteractNow()) return;
        if (interactModule != null) interactModule.ServerTryDrop();
    }

    [ServerRpc]
    private void RequestSpinDashServerRpc(ServerRpcParams rpcParams = default)
    {
        if (rpcParams.Receive.SenderClientId != OwnerClientId)
            return;

        if (IsGameplayPhaseLocked())
            return;

        if (!HasHeldItemForSpinDash() ||
            !HasAvailableSpinDashRoute())
            return;

        if (!CanMoveNow())
            return;

        if (TryGetPostItHeavyMovementScale(out _))
            return;

        ServerTryStartSpinDashOnAvailableRoute();
    }

    private bool HasHeldItemForSpinDash()
    {
        if (interactModule != null && interactModule.HasHeldItem())
            return true;

        if (_motorShellItemAdapter == null)
        {
            _motorShellItemAdapter =
                GetComponentInChildren<HamsterMotorShellItemAdapter>(
                    true);
        }

        return _motorShellItemAdapter != null &&
               _motorShellItemAdapter.HasHeldItem;
    }

    private bool HasAvailableSpinDashRoute()
    {
        return locomotionModule != null ||
               ResolveMotorShellSpinDashAdapter() != null;
    }

    private bool ServerTryStartSpinDashOnAvailableRoute()
    {
        if (!IsServer)
            return false;

        if (locomotionModule != null)
            return locomotionModule.ServerTryStartSpinDash();

        HamsterMotorShellSpinDashAdapter adapter =
            ResolveMotorShellSpinDashAdapter();
        return adapter != null &&
               adapter.ServerTryStartSpinDash();
    }

    private HamsterMotorShellSpinDashAdapter
        ResolveMotorShellSpinDashAdapter()
    {
        if (_motorShellSpinDashAdapter == null)
        {
            _motorShellSpinDashAdapter =
                GetComponentInChildren<
                    HamsterMotorShellSpinDashAdapter>(true);
        }

        return _motorShellSpinDashAdapter;
    }

    public bool TryConsumePostItPeelInteractThisFrame()
    {
        if (interactModule != null && interactModule.IsGrabbingCharacter)
        {
            if (IsServer)
                interactModule.ServerReleaseCharacterGrab(
                    "InteractInput");
            else
                interactModule.RequestReleaseCharacterGrab();

            return true;
        }

        if (interactModule != null && interactModule.IsGrabbedByCharacter)
            return true;

        if (ShouldReserveSpinDashInteractThisFrame())
            return true;

        int currentFrame = Time.frameCount;
        if (_characterGrabReservedInteractFrame == currentFrame)
            return true;

        if (_postItPeelEvaluatedFrame == currentFrame)
        {
            return _postItPeelConsumedInEvaluatedFrame;
        }

        _postItPeelEvaluatedFrame = currentFrame;
        _postItPeelConsumedInEvaluatedFrame =
            TryBeginPostItPeelHold();
        if (!_postItPeelConsumedInEvaluatedFrame)
        {
            _postItPeelConsumedInEvaluatedFrame =
                TryReserveCharacterGrabInteractThisFrame();
        }

        if (!_postItPeelConsumedInEvaluatedFrame)
        {
            _postItPeelConsumedInEvaluatedFrame =
                TryRequestDroppedPostItRecovery();
        }

        return _postItPeelConsumedInEvaluatedFrame;
    }

    private bool ShouldReserveSpinDashInteractThisFrame()
    {
        if (!CanMoveNow() ||
            !HasAvailableSpinDashRoute())
        {
            return false;
        }

        Keyboard keyboard = Keyboard.current;
        return keyboard != null &&
               (keyboard.leftShiftKey.isPressed ||
                keyboard.rightShiftKey.isPressed);
    }

    private bool TryReserveCharacterGrabInteractThisFrame()
    {
        if (!CanInteractNow() ||
            interactModule == null ||
            interactModule.HasHeldItem() ||
            interactModule.IsCharacterGrabBusy ||
            !interactModule.TryFindCharacterGrabTarget(
                out PlayerStatusModule targetStatus))
        {
            return false;
        }

        _characterGrabReservedInteractFrame = Time.frameCount;
        RequestCharacterGrab(targetStatus);
        return true;
    }

    private bool TryBeginPostItPeelHold()
    {
        if (_postItPeelHoldState != PostItPeelHoldState.Idle)
            return true;

        if (inputModule == null || !inputModule.IsInteractHeld())
            return false;

        if (!TryResolvePostItPeelHoldCandidate(
                out _,
                out ulong targetNetworkObjectId,
                out int postItId,
                out _))
        {
            return false;
        }

        _postItPeelTrackedTargetNetworkObjectId = targetNetworkObjectId;
        _postItPeelTrackedPostItId = postItId;
        _postItPeelHoldStartedAt = Time.unscaledTime;
        _postItPeelHoldState = PostItPeelHoldState.Tracking;
        return true;
    }

    private void TickPostItPeelHold()
    {
        if (_postItPeelHoldState == PostItPeelHoldState.Idle)
            return;

        if (_postItPeelHoldState == PostItPeelHoldState.Completed)
        {
            _postItPeelHoldState = PostItPeelHoldState.Cooldown;
            return;
        }

        if (_postItPeelHoldState == PostItPeelHoldState.Cooldown)
        {
            if (Time.unscaledTime >= _postItPeelLocalCooldownUntil)
                ResetPostItPeelHoldState();

            return;
        }

        bool interactHeld = inputModule != null && inputModule.IsInteractHeld();
        if (_postItPeelHoldState == PostItPeelHoldState.Cancelled)
        {
            if (!interactHeld)
                ResetPostItPeelHoldState();

            return;
        }

        if (!interactHeld ||
            !TryResolvePostItPeelHoldCandidate(
                out NetworkObjectReference targetReference,
                out ulong targetNetworkObjectId,
                out int postItId,
                out Vector3 aimDirection) ||
            targetNetworkObjectId != _postItPeelTrackedTargetNetworkObjectId ||
            postItId != _postItPeelTrackedPostItId)
        {
            CancelPostItPeelHold();
            return;
        }

        float duration = Mathf.Max(0.01f, postItPeelHoldDuration);
        if (Time.unscaledTime - _postItPeelHoldStartedAt < duration)
            return;

        _postItPeelLocalCooldownUntil =
            Time.unscaledTime + Mathf.Max(0f, postItPeelCooldown);
        _postItPeelHoldState = PostItPeelHoldState.Completed;
        RequestPostItPeelServerRpc(
            targetReference,
            postItId,
            aimDirection);
    }

    private bool TryResolvePostItPeelHoldCandidate(
        out NetworkObjectReference targetReference,
        out ulong targetNetworkObjectId,
        out int postItId,
        out Vector3 aimDirection)
    {
        targetReference = default;
        targetNetworkObjectId = ulong.MaxValue;
        postItId = -1;
        aimDirection = Vector3.zero;

        if (!IsOwner ||
            !IsSpawned ||
            !IsPlayingState() ||
            !CanInteractNow() ||
            IsPostItPeelHoldLocallyBlocked())
        {
            return false;
        }

        PlayerPostItInventory requesterInventory = ResolvePostItInventory();
        if (requesterInventory == null || requesterInventory.IsFull)
            return false;

        Camera playerCamera = PlayerCamera;
        if (playerCamera == null || !playerCamera.isActiveAndEnabled)
            return false;

        Ray ray = playerCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
        if (!TryFindPostItPeelTarget(
                ray,
                out NetworkObjectReference selectedTargetReference,
                out PostItPublicVisualData selectedPostIt) ||
            !selectedTargetReference.TryGet(out NetworkObject targetNetworkObject) ||
            targetNetworkObject == null ||
            !targetNetworkObject.IsSpawned)
        {
            return false;
        }

        PlayerPostItWorldPresenter targetPresenter =
            targetNetworkObject.GetComponent<PlayerPostItWorldPresenter>();
        if (targetPresenter == null)
        {
            targetPresenter =
                targetNetworkObject.GetComponentInChildren<PlayerPostItWorldPresenter>(true);
        }

        if (targetPresenter == null ||
            !targetPresenter.TryGetVisiblePostItWorldPosition(
                selectedPostIt.PostItId,
                out Vector3 postItWorldPosition) ||
            !ValidatePostItPeelGeometry(
                NetworkObject,
                targetNetworkObject,
                postItWorldPosition,
                ray.origin))
        {
            return false;
        }

        targetReference = selectedTargetReference;
        targetNetworkObjectId = targetNetworkObject.NetworkObjectId;
        postItId = selectedPostIt.PostItId;
        aimDirection = ray.direction.normalized;
        return true;
    }

    private bool IsPostItPeelHoldLocallyBlocked()
    {
        if (IsPostItPeelInputBlocked() || IsPostItPeelUiInputBlocked())
            return true;

        if (statusModule != null &&
            (statusModule.IsKnocked || statusModule.IsStandingUp))
        {
            return true;
        }

        if (_motorShellRecoveryAdapter == null)
        {
            _motorShellRecoveryAdapter =
                GetComponentInChildren<HamsterMotorShellRagdollRecoveryAdapter>(true);
        }

        if (_motorShellRecoveryAdapter != null &&
            _motorShellRecoveryAdapter.IsKnockedOrRecovering)
        {
            return true;
        }

        SugaActiveRagdollController activeRagdollController =
            ResolveActiveRagdollController();
        if (activeRagdollController != null &&
            activeRagdollController.IsRagdollActiveForGameplay)
        {
            return true;
        }

        if (_ragdollGrabber == null)
            _ragdollGrabber = GetComponentInChildren<HamsterRagdollGrabber>(true);

        if (_ragdollGrabber != null &&
            (_ragdollGrabber.IsHolding ||
             _ragdollGrabber.HasPendingGrab ||
             _ragdollGrabber.HasPendingThrow))
        {
            return true;
        }

        if (_ragdollGrabbable == null)
            _ragdollGrabbable = GetComponentInChildren<HamsterRagdollGrabbable>(true);

        return _ragdollGrabbable != null && _ragdollGrabbable.IsHeld;
    }

    private static bool IsPostItPeelUiInputBlocked()
    {
        EventSystem eventSystem = EventSystem.current;
        return eventSystem != null &&
               (eventSystem.currentSelectedGameObject != null ||
                eventSystem.IsPointerOverGameObject());
    }

    private void CancelPostItPeelHold()
    {
        if (_postItPeelHoldState != PostItPeelHoldState.Tracking)
            return;

        _postItPeelHoldState = PostItPeelHoldState.Cancelled;
        _postItPeelTrackedTargetNetworkObjectId = ulong.MaxValue;
        _postItPeelTrackedPostItId = -1;
    }

    private void ResetPostItPeelHoldState()
    {
        _characterGrabReservedInteractFrame = -1;
        _postItPeelHoldState = PostItPeelHoldState.Idle;
        _postItPeelTrackedTargetNetworkObjectId = ulong.MaxValue;
        _postItPeelTrackedPostItId = -1;
        _postItPeelHoldStartedAt = 0f;
        _postItPeelLocalCooldownUntil = 0f;
    }

    private bool TryRequestDroppedPostItRecovery()
    {
        if (!IsOwner || !IsSpawned || !IsPlayingState() || !CanInteractNow())
            return false;

        if (IsPostItPeelInputBlocked())
            return false;

        PlayerPostItInventory requesterInventory = ResolvePostItInventory();
        PostItRoundManager roundManager = ResolvePostItRoundManager();
        if (requesterInventory == null || requesterInventory.IsFull || roundManager == null)
            return false;

        Camera playerCamera = PlayerCamera;
        if (playerCamera == null || !playerCamera.isActiveAndEnabled)
            return false;

        Ray ray = playerCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
        if (!roundManager.TryGetClosestWorldDrop(
                ray,
                Mathf.Max(0f, postItPeelSelectionDistance),
                Mathf.Max(0f, postItPeelVisualRayTolerance),
                out PostItWorldDropData selectedDrop) ||
            !IsPostItPeelWithinReach(NetworkObject, selectedDrop.Position) ||
            !HasPostItWorldRecoveryLineOfSight(
                ray.origin,
                selectedDrop.Position,
                NetworkObject,
                Mathf.Max(0f, postItPeelVisualRayTolerance)))
        {
            return false;
        }

        RequestPostItWorldRecoveryServerRpc(
            selectedDrop.PostItId,
            ray.direction.normalized);
        return true;
    }

    private bool IsPostItPeelInputBlocked()
    {
        if (interactModule != null &&
            (interactModule.HasHeldItem() ||
             interactModule.IsGrabbingCharacter ||
             interactModule.IsGrabbedByCharacter))
        {
            return true;
        }

        if (_motorShellItemAdapter == null)
            _motorShellItemAdapter = GetComponentInChildren<HamsterMotorShellItemAdapter>(true);

        if (_motorShellItemAdapter != null && _motorShellItemAdapter.HasHeldItem)
            return true;

        if (locomotionModule != null && locomotionModule.IsSpinDashing)
            return true;

        if (_motorShellSpinDashAdapter == null)
            _motorShellSpinDashAdapter = GetComponentInChildren<HamsterMotorShellSpinDashAdapter>(true);

        return _motorShellSpinDashAdapter != null && _motorShellSpinDashAdapter.IsSpinDashBusy;
    }

    private PlayerPostItInventory ResolvePostItInventory()
    {
        if (_postItInventory == null)
            _postItInventory = GetComponentInChildren<PlayerPostItInventory>(true);

        if (_postItInventory == null)
            return null;

        NetworkObject inventoryNetworkObject = _postItInventory.GetComponentInParent<NetworkObject>();
        return inventoryNetworkObject == NetworkObject ? _postItInventory : null;
    }

    private void SubscribePostItEffectChanges()
    {
        PlayerPostItInventory inventory = ResolvePostItInventory();
        if (_subscribedPostItEffectInventory == inventory)
            return;

        UnsubscribePostItEffectChanges();
        if (inventory == null)
            return;

        inventory.EffectsChanged += HandlePostItEffectsChanged;
        _subscribedPostItEffectInventory = inventory;
        HandlePostItEffectsChanged();
    }

    private void UnsubscribePostItEffectChanges()
    {
        if (_subscribedPostItEffectInventory == null)
            return;

        _subscribedPostItEffectInventory.EffectsChanged -= HandlePostItEffectsChanged;
        _subscribedPostItEffectInventory = null;
    }

    private void HandlePostItEffectsChanged()
    {
        if (!IsServer)
            return;

        bool heavyActive = TryGetPostItHeavyMovementScale(out _);
        if (heavyActive)
            _jumpPressed = false;

        ApplyOwnedPostItHeavyJumpLock(heavyActive);
    }

    private bool TryGetPostItHeavyMovementScale(out float movementScale)
    {
        movementScale = 1f;
        PlayerPostItInventory inventory = ResolvePostItInventory();
        if (inventory == null)
            return false;

        movementScale = inventory.ServerGetHeavyMovementScale();
        return movementScale < 1f;
    }

    private bool ApplyOwnerPostItInputPresentation(ref Vector2 move, ref bool sprintHeld)
    {
        if (!TryGetPostItHeavyMovementScale(out float movementScale))
            return true;

        move *= movementScale;
        sprintHeld = false;
        return false;
    }

    private void ApplyOwnedPostItHeavyJumpLock(bool heavyActive)
    {
        HamsterFullRagdollMotor motorShellMotor = ResolveMotorShellMotor();
        if (motorShellMotor == null)
            return;

        if (heavyActive)
        {
            if (!motorShellMotor.IsExternalJumpLocked ||
                motorShellMotor.ExternalJumpLockReason == PostItHeavyJumpLockReason)
            {
                motorShellMotor.SetExternalJumpLock(
                    true,
                    PostItHeavyJumpLockReason);
            }

            return;
        }

        if (!motorShellMotor.IsExternalJumpLocked ||
            motorShellMotor.ExternalJumpLockReason != PostItHeavyJumpLockReason)
        {
            return;
        }

        motorShellMotor.SetExternalJumpLock(false, "PostIt:HeavyExpired");
        if (IsGameplayPhaseLocked())
            ApplyOwnedGameplayPhaseJumpLock();
    }

    private void ReleaseOwnedPostItHeavyJumpLock()
    {
        HamsterFullRagdollMotor motorShellMotor = _motorShellMotor;
        if (motorShellMotor == null ||
            !motorShellMotor.IsExternalJumpLocked ||
            motorShellMotor.ExternalJumpLockReason != PostItHeavyJumpLockReason)
        {
            return;
        }

        motorShellMotor.SetExternalJumpLock(false, "PostIt:HeavyRelease");
    }

    private PostItRoundManager ResolvePostItRoundManager()
    {
        if (_postItRoundManager == null)
            _postItRoundManager = FindFirstObjectByType<PostItRoundManager>();

        return _postItRoundManager;
    }

    private bool TryFindPostItPeelTarget(
        Ray ray,
        out NetworkObjectReference targetReference,
        out PostItPublicVisualData selectedPostIt)
    {
        targetReference = default;
        selectedPostIt = PostItPublicVisualData.Invalid;

        float selectionDistance = Mathf.Max(0f, postItPeelSelectionDistance);
        if (selectionDistance <= 0f)
            return false;

        float rayRadius = Mathf.Max(0f, postItPeelBodyRayRadius);
        int hitCount = rayRadius > 0f
            ? Physics.SphereCastNonAlloc(
                ray,
                rayRadius,
                _postItPhysicsHitBuffer,
                selectionDistance,
                Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Collide)
            : Physics.RaycastNonAlloc(
                ray,
                _postItPhysicsHitBuffer,
                selectionDistance,
                Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Collide);
        if (hitCount >= _postItPhysicsHitBuffer.Length)
            return false;

        SortPostItPhysicsHitsByDistance(hitCount);
        NetworkObject requesterNetworkObject = NetworkObject;

        for (int i = 0; i < hitCount; i++)
        {
            Collider hitCollider = _postItPhysicsHitBuffer[i].collider;
            if (hitCollider == null)
                continue;

            NetworkObject hitNetworkObject = hitCollider.GetComponentInParent<NetworkObject>();
            if (hitNetworkObject == requesterNetworkObject)
                continue;

            PlayerHub targetHub = hitCollider.GetComponentInParent<PlayerHub>();
            if (targetHub != null && targetHub != this)
            {
                NetworkObject targetNetworkObject = targetHub.GetComponentInParent<NetworkObject>();
                if (targetNetworkObject == null ||
                    !targetNetworkObject.IsSpawned ||
                    targetNetworkObject == requesterNetworkObject)
                {
                    return false;
                }

                PlayerPostItWorldPresenter presenter =
                    targetNetworkObject.GetComponent<PlayerPostItWorldPresenter>();
                if (presenter == null)
                {
                    presenter = targetNetworkObject.GetComponentInChildren<PlayerPostItWorldPresenter>(true);
                }

                if (presenter == null ||
                    !presenter.TryGetClosestVisiblePostIt(
                        ray,
                        selectionDistance,
                        Mathf.Min(
                            Mathf.Max(
                                0f,
                                postItPeelVisualRayTolerance),
                            ExactPostItPeelVisualRayTolerance),
                        out selectedPostIt) ||
                    !presenter.TryGetVisiblePostItWorldPosition(
                        selectedPostIt.PostItId,
                        out Vector3 postItWorldPosition) ||
                    !IsPostItPeelWithinReach(requesterNetworkObject, postItWorldPosition))
                {
                    return false;
                }

                targetReference = targetNetworkObject;
                return true;
            }

            if (hitCollider.isTrigger)
                continue;

            return false;
        }

        return false;
    }

    [ServerRpc]
    private void RequestPostItPeelServerRpc(
        NetworkObjectReference targetReference,
        int postItId,
        Vector3 aimDirection,
        ServerRpcParams rpcParams = default)
    {
        if (rpcParams.Receive.SenderClientId != OwnerClientId)
            return;

        if (!IsSpawned || !IsPlayingState() || !CanInteractNow() || IsPostItPeelInputBlocked())
            return;

        float now = Time.unscaledTime;
        if (now < _nextPostItPeelServerTime)
            return;

        _nextPostItPeelServerTime = now + Mathf.Max(0f, postItPeelCooldown);

        if (!ServerTryValidatePostItPeelRequest(
                targetReference,
                postItId,
                aimDirection,
                out PlayerPostItInventory requesterInventory,
                out PlayerPostItInventory targetInventory))
        {
            return;
        }

        int guardChargesBefore = targetInventory.GuardCharges;
        if (guardChargesBefore > 0)
        {
            bool guardConsumed =
                targetInventory.ServerTryConsumeGuardAgainstPeel();
            int guardChargesAfter = targetInventory.GuardCharges;
            if (guardConsumed)
            {
                Debug.Log(
                    $"[PlayerHub] Post-it Peel blocked by Guard. requester={OwnerClientId}, " +
                    $"target={targetInventory.OwnerClientId}, postItId={postItId}",
                    targetInventory);
            }
            else if (guardChargesAfter < guardChargesBefore)
            {
                Debug.LogError(
                    $"[PlayerHub] Guard consumption was reconciled from observed state; " +
                    $"Post-it Peel transfer was blocked. requester={OwnerClientId}, " +
                    $"target={targetInventory.OwnerClientId}, postItId={postItId}",
                    targetInventory);
            }
            else
            {
                Debug.LogError(
                    $"[PlayerHub] Guard consumption failed; Post-it Peel transfer was blocked. " +
                    $"requester={OwnerClientId}, target={targetInventory.OwnerClientId}, " +
                    $"postItId={postItId}",
                    targetInventory);
            }

            return;
        }

        targetInventory.ServerTryTransferPostItTo(
            requesterInventory,
            postItId,
            out _);
    }

    private bool ServerTryValidatePostItPeelRequest(
        NetworkObjectReference targetReference,
        int postItId,
        Vector3 aimDirection,
        out PlayerPostItInventory requesterInventory,
        out PlayerPostItInventory targetInventory)
    {
        requesterInventory = null;
        targetInventory = null;

        NetworkObject requesterNetworkObject = NetworkObject;
        if (postItId < 0 ||
            requesterNetworkObject == null ||
            !requesterNetworkObject.IsSpawned ||
            !targetReference.TryGet(out NetworkObject targetNetworkObject) ||
            targetNetworkObject == null ||
            !targetNetworkObject.IsSpawned ||
            targetNetworkObject == requesterNetworkObject ||
            targetNetworkObject.OwnerClientId == OwnerClientId)
        {
            return false;
        }

        PlayerHub targetHub = targetNetworkObject.GetComponent<PlayerHub>();
        if (targetHub == null || !targetHub.IsSpawned)
            return false;

        PlayerStatusModule targetStatus = targetHub.statusModule;
        if (targetStatus == null)
            targetStatus = targetNetworkObject.GetComponentInChildren<PlayerStatusModule>(true);

        if (targetStatus != null && targetStatus.IsEliminated)
            return false;

        if (!ServerTryResolvePostItPeelAimRay(aimDirection, out Ray serverAimRay) ||
            !TryFindPostItPeelTarget(
                serverAimRay,
                out NetworkObjectReference serverTargetReference,
                out PostItPublicVisualData serverSelectedPostIt) ||
            !serverTargetReference.TryGet(out NetworkObject serverTargetNetworkObject) ||
            serverTargetNetworkObject != targetNetworkObject ||
            serverSelectedPostIt.PostItId != postItId)
        {
            return false;
        }

        requesterInventory = ResolvePostItInventory();
        targetInventory = targetNetworkObject.GetComponent<PlayerPostItInventory>();
        if (targetInventory == null)
            targetInventory = targetNetworkObject.GetComponentInChildren<PlayerPostItInventory>(true);

        if (requesterInventory == null ||
            targetInventory == null ||
            requesterInventory == targetInventory ||
            requesterInventory.GetComponentInParent<NetworkObject>() != requesterNetworkObject ||
            targetInventory.GetComponentInParent<NetworkObject>() != targetNetworkObject ||
            requesterInventory.IsFull ||
            requesterInventory.ContainsPostIt(postItId))
        {
            return false;
        }

        PostItRoundManager roundManager = ResolvePostItRoundManager();
        if (roundManager == null ||
            !roundManager.IsSpawned ||
            !roundManager.IsServer ||
            !roundManager.ServerIsCurrentPlayingParticipant(requesterInventory) ||
            !roundManager.ServerIsCurrentPlayingParticipant(targetInventory))
        {
            return false;
        }

        if (!targetInventory.TryGetPostIt(postItId, out PostItRuntimeData runtimeData) ||
            !runtimeData.IsValid ||
            runtimeData.HolderClientId != targetNetworkObject.OwnerClientId ||
            !targetInventory.TryGetPublicVisualAtSlot(
                runtimeData.SlotIndex,
                out PostItPublicVisualData publicData) ||
            publicData.PostItId != postItId)
        {
            return false;
        }

        PlayerPostItWorldPresenter targetPresenter =
            targetNetworkObject.GetComponent<PlayerPostItWorldPresenter>();
        if (targetPresenter == null)
            targetPresenter = targetNetworkObject.GetComponentInChildren<PlayerPostItWorldPresenter>(true);

        if (targetPresenter == null ||
            targetPresenter.BoundInventory != targetInventory ||
            !targetPresenter.TryGetVisiblePostItWorldPosition(postItId, out Vector3 postItWorldPosition))
        {
            return false;
        }

        return ValidatePostItPeelGeometry(
            requesterNetworkObject,
            targetNetworkObject,
            postItWorldPosition,
            serverAimRay.origin);
    }

    private bool ServerTryResolvePostItPeelAimRay(
        Vector3 requestedAimDirection,
        out Ray aimRay)
    {
        aimRay = default;

        if (!IsFiniteVector3(requestedAimDirection) ||
            requestedAimDirection.sqrMagnitude <= 0.0001f)
        {
            return false;
        }

        Vector3 normalizedAimDirection = requestedAimDirection.normalized;
        if (Mathf.Abs(normalizedAimDirection.y) > 0.98f ||
            !TryResolvePostItPeelServerForward(out Vector3 serverPlanarForward))
        {
            return false;
        }

        Vector3 requestedPlanarForward = Vector3.ProjectOnPlane(normalizedAimDirection, Vector3.up);
        if (requestedPlanarForward.sqrMagnitude <= 0.0001f)
            return false;

        float aimDirectionDot = Vector3.Dot(
            requestedPlanarForward.normalized,
            serverPlanarForward);
        if (aimDirectionDot < Mathf.Clamp(postItPeelAimDirectionMinDot, -1f, 1f))
            return false;

        Camera serverCamera = PlayerCamera;
        if (serverCamera == null || !IsFiniteVector3(serverCamera.transform.position))
            return false;

        aimRay = new Ray(serverCamera.transform.position, normalizedAimDirection);
        return true;
    }

    private bool ValidatePostItPeelGeometry(
        NetworkObject requesterNetworkObject,
        NetworkObject targetNetworkObject,
        Vector3 postItWorldPosition,
        Vector3 visibilityOrigin)
    {
        Vector3 origin = ResolvePostItPeelBodyCenter(requesterNetworkObject);
        Vector3 toPostIt = postItWorldPosition - origin;
        float maxDistance = Mathf.Max(0f, postItPeelServerDistance);
        float distanceSqr = toPostIt.sqrMagnitude;
        if (maxDistance <= 0f ||
            distanceSqr <= 0.0001f ||
            distanceSqr > maxDistance * maxDistance)
        {
            return false;
        }

        if (!TryResolvePostItPeelServerForward(out Vector3 planarForward))
            return false;

        Vector3 planarToPostIt = Vector3.ProjectOnPlane(toPostIt, Vector3.up);
        if (planarForward.sqrMagnitude > 0.0001f && planarToPostIt.sqrMagnitude > 0.0001f)
        {
            float forwardDot = Vector3.Dot(planarForward.normalized, planarToPostIt.normalized);
            if (forwardDot < Mathf.Clamp(postItPeelMinForwardDot, -1f, 1f))
                return false;
        }

        return HasPostItPeelLineOfSight(
            visibilityOrigin,
            postItWorldPosition,
            requesterNetworkObject,
            targetNetworkObject,
            Mathf.Max(
                Mathf.Max(0f, postItPeelVisualRayTolerance),
                Mathf.Max(0f, postItPeelBodyRayRadius)));
    }

    private bool IsPostItPeelWithinReach(
        NetworkObject requesterNetworkObject,
        Vector3 postItWorldPosition)
    {
        if (requesterNetworkObject == null || !IsFiniteVector3(postItWorldPosition))
            return false;

        float maxDistance = Mathf.Max(0f, postItPeelServerDistance);
        if (maxDistance <= 0f)
            return false;

        Vector3 origin = ResolvePostItPeelBodyCenter(requesterNetworkObject);
        return (postItWorldPosition - origin).sqrMagnitude <= maxDistance * maxDistance;
    }

    private bool TryResolvePostItPeelServerForward(out Vector3 planarForward)
    {
        planarForward = Vector3.zero;

        HamsterFullRagdollMotor motorShellMotor = ResolveMotorShellMotor();
        if (motorShellMotor != null && motorShellMotor.IsMainScenesInputRouteTarget)
        {
            return TryNormalizePostItPeelPlanarDirection(
                motorShellMotor.CameraPlanarForward,
                out planarForward);
        }

        Camera playerCamera = PlayerCamera;
        if (playerCamera != null &&
            TryNormalizePostItPeelPlanarDirection(playerCamera.transform.forward, out planarForward))
        {
            return true;
        }

        return TryNormalizePostItPeelPlanarDirection(transform.forward, out planarForward);
    }

    private static bool TryNormalizePostItPeelPlanarDirection(
        Vector3 direction,
        out Vector3 normalizedDirection)
    {
        normalizedDirection = Vector3.ProjectOnPlane(direction, Vector3.up);
        if (!IsFiniteVector3(normalizedDirection) || normalizedDirection.sqrMagnitude <= 0.0001f)
        {
            normalizedDirection = Vector3.zero;
            return false;
        }

        normalizedDirection.Normalize();
        return true;
    }

    private void SortPostItPhysicsHitsByDistance(int hitCount)
    {
        for (int index = 1; index < hitCount; index++)
        {
            RaycastHit current = _postItPhysicsHitBuffer[index];
            int insertionIndex = index - 1;
            while (insertionIndex >= 0 &&
                   _postItPhysicsHitBuffer[insertionIndex].distance > current.distance)
            {
                _postItPhysicsHitBuffer[insertionIndex + 1] =
                    _postItPhysicsHitBuffer[insertionIndex];
                insertionIndex--;
            }

            _postItPhysicsHitBuffer[insertionIndex + 1] = current;
        }
    }

    private Vector3 ResolvePostItPeelBodyCenter(NetworkObject playerNetworkObject)
    {
        if (playerNetworkObject == null)
            return Vector3.zero;

        Vector3 bodyCenter = playerNetworkObject.transform.position + Vector3.up * 0.4f;
        _postItBodyColliderBuffer.Clear();
        playerNetworkObject.GetComponentsInChildren<Collider>(
            true,
            _postItBodyColliderBuffer);
        for (int i = 0; i < _postItBodyColliderBuffer.Count; i++)
        {
            Collider candidate = _postItBodyColliderBuffer[i];
            if (candidate == null ||
                !candidate.enabled ||
                !candidate.gameObject.activeInHierarchy ||
                candidate.gameObject.name != "BodyHurtbox")
            {
                continue;
            }

            bodyCenter = candidate.bounds.center;
            break;
        }

        _postItBodyColliderBuffer.Clear();
        return bodyCenter;
    }

    private bool HasPostItPeelLineOfSight(
        Vector3 origin,
        Vector3 postItWorldPosition,
        NetworkObject requesterNetworkObject,
        NetworkObject targetNetworkObject,
        float targetEndpointTolerance)
    {
        Vector3 toPostIt = postItWorldPosition - origin;
        float distance = toPostIt.magnitude;
        if (distance <= 0.0001f)
            return false;

        int hitCount = Physics.RaycastNonAlloc(
            origin,
            toPostIt / distance,
            _postItPhysicsHitBuffer,
            distance,
            Physics.DefaultRaycastLayers,
            QueryTriggerInteraction.Collide);
        if (hitCount >= _postItPhysicsHitBuffer.Length)
            return false;

        SortPostItPhysicsHitsByDistance(hitCount);
        for (int i = 0; i < hitCount; i++)
        {
            RaycastHit hit = _postItPhysicsHitBuffer[i];
            Collider hitCollider = hit.collider;
            if (hitCollider == null)
                continue;

            NetworkObject hitNetworkObject = hitCollider.GetComponentInParent<NetworkObject>();
            if (hitNetworkObject == requesterNetworkObject)
                continue;

            if (hitNetworkObject == targetNetworkObject)
            {
                if (distance - hit.distance <= targetEndpointTolerance)
                    continue;

                return false;
            }

            if (hitNetworkObject != null)
                return false;

            if (hitCollider.isTrigger)
                continue;

            return false;
        }

        return true;
    }

    [ServerRpc]
    private void RequestPostItWorldRecoveryServerRpc(
        int postItId,
        Vector3 aimDirection,
        ServerRpcParams rpcParams = default)
    {
        if (rpcParams.Receive.SenderClientId != OwnerClientId)
            return;

        if (!IsSpawned || !IsPlayingState() || !CanInteractNow() || IsPostItPeelInputBlocked())
            return;

        float now = Time.unscaledTime;
        if (now < _nextPostItPeelServerTime)
            return;

        _nextPostItPeelServerTime = now + Mathf.Max(0f, postItPeelCooldown);
        if (!ServerTryValidatePostItWorldRecoveryRequest(
                postItId,
                aimDirection,
                out PlayerPostItInventory requesterInventory,
                out PostItRoundManager roundManager))
        {
            return;
        }

        roundManager.ServerTryRecoverWorldDrop(requesterInventory, postItId, out _);
    }

    private bool ServerTryValidatePostItWorldRecoveryRequest(
        int postItId,
        Vector3 aimDirection,
        out PlayerPostItInventory requesterInventory,
        out PostItRoundManager roundManager)
    {
        requesterInventory = null;
        roundManager = null;

        NetworkObject requesterNetworkObject = NetworkObject;
        if (postItId < 0 ||
            requesterNetworkObject == null ||
            !requesterNetworkObject.IsSpawned ||
            !ServerTryResolvePostItPeelAimRay(aimDirection, out Ray serverAimRay))
        {
            return false;
        }

        roundManager = ResolvePostItRoundManager();
        if (roundManager == null ||
            !roundManager.IsSpawned ||
            !roundManager.IsServer ||
            !roundManager.TryGetWorldDrop(postItId, out PostItWorldDropData worldDrop) ||
            !roundManager.TryGetClosestWorldDrop(
                serverAimRay,
                Mathf.Max(0f, postItPeelSelectionDistance),
                Mathf.Max(0f, postItPeelVisualRayTolerance),
                out PostItWorldDropData selectedDrop) ||
            selectedDrop.PostItId != postItId)
        {
            roundManager = null;
            return false;
        }

        requesterInventory = ResolvePostItInventory();
        if (requesterInventory == null ||
            !requesterInventory.IsSpawned ||
            !requesterInventory.IsServer ||
            requesterInventory.GetComponentInParent<NetworkObject>() != requesterNetworkObject ||
            !roundManager.ServerIsCurrentPlayingParticipant(requesterInventory) ||
            requesterInventory.IsFull ||
            requesterInventory.ContainsPostIt(postItId))
        {
            requesterInventory = null;
            roundManager = null;
            return false;
        }

        if (!ServerValidatePostItWorldRecoveryGeometry(
                requesterNetworkObject,
                worldDrop.Position,
                serverAimRay.origin))
        {
            requesterInventory = null;
            roundManager = null;
            return false;
        }

        return true;
    }

    private bool ServerValidatePostItWorldRecoveryGeometry(
        NetworkObject requesterNetworkObject,
        Vector3 postItWorldPosition,
        Vector3 visibilityOrigin)
    {
        if (requesterNetworkObject == null ||
            !IsFiniteVector3(postItWorldPosition) ||
            !IsFiniteVector3(visibilityOrigin))
        {
            return false;
        }

        Vector3 origin = ResolvePostItPeelBodyCenter(requesterNetworkObject);
        Vector3 toPostIt = postItWorldPosition - origin;
        float maxDistance = Mathf.Max(0f, postItPeelServerDistance);
        float distanceSqr = toPostIt.sqrMagnitude;
        if (maxDistance <= 0f ||
            distanceSqr <= 0.0001f ||
            distanceSqr > maxDistance * maxDistance)
        {
            return false;
        }

        if (!TryResolvePostItPeelServerForward(out Vector3 planarForward))
            return false;

        Vector3 planarToPostIt = Vector3.ProjectOnPlane(toPostIt, Vector3.up);
        if (planarForward.sqrMagnitude > 0.0001f && planarToPostIt.sqrMagnitude > 0.0001f)
        {
            float forwardDot = Vector3.Dot(planarForward.normalized, planarToPostIt.normalized);
            if (forwardDot < Mathf.Clamp(postItPeelMinForwardDot, -1f, 1f))
                return false;
        }

        return HasPostItWorldRecoveryLineOfSight(
            visibilityOrigin,
            postItWorldPosition,
            requesterNetworkObject,
            Mathf.Max(0f, postItPeelVisualRayTolerance));
    }

    private bool HasPostItWorldRecoveryLineOfSight(
        Vector3 origin,
        Vector3 postItWorldPosition,
        NetworkObject requesterNetworkObject,
        float targetEndpointTolerance)
    {
        Vector3 toPostIt = postItWorldPosition - origin;
        float distance = toPostIt.magnitude;
        if (distance <= 0.0001f)
            return false;

        int hitCount = Physics.RaycastNonAlloc(
            origin,
            toPostIt / distance,
            _postItPhysicsHitBuffer,
            distance,
            Physics.DefaultRaycastLayers,
            QueryTriggerInteraction.Collide);
        if (hitCount >= _postItPhysicsHitBuffer.Length)
            return false;

        SortPostItPhysicsHitsByDistance(hitCount);
        for (int i = 0; i < hitCount; i++)
        {
            RaycastHit hit = _postItPhysicsHitBuffer[i];
            Collider hitCollider = hit.collider;
            if (hitCollider == null)
                continue;

            NetworkObject hitNetworkObject = hitCollider.GetComponentInParent<NetworkObject>();
            if (hitNetworkObject == requesterNetworkObject)
                continue;

            if (hitNetworkObject != null)
                return false;

            if (hitCollider.isTrigger)
                continue;

            if (distance - hit.distance <= targetEndpointTolerance &&
                IsFiniteVector3(hit.normal) &&
                hit.normal.sqrMagnitude > 0.0001f &&
                Vector3.Dot(hit.normal.normalized, Vector3.up) >=
                    PostItWorldRecoveryEndpointGroundMinUpDot)
            {
                continue;
            }

            return false;
        }

        return true;
    }

    private void RequestCharacterGrab(PlayerStatusModule targetStatus)
    {
        if (IsGameplayPhaseLocked() ||
            interactModule == null ||
            targetStatus == null)
            return;

        if (IsServer)
        {
            interactModule.ServerTryStartCharacterGrab(targetStatus);
            return;
        }

        if (!TryCreateCharacterGrabTargetReference(targetStatus, out NetworkObjectReference targetReference))
            return;

        RequestCharacterGrabServerRpc(targetReference);
    }

    [ServerRpc]
    private void RequestCharacterGrabServerRpc(NetworkObjectReference targetReference, ServerRpcParams rpcParams = default)
    {
        if (rpcParams.Receive.SenderClientId != OwnerClientId)
            return;

        if (IsGameplayPhaseLocked())
            return;

        if (interactModule == null)
            return;

        if (!CanInteractNow())
            return;

        if (!targetReference.TryGet(out NetworkObject targetObject))
            return;

        PlayerStatusModule targetStatus = ResolveCharacterGrabTargetStatus(targetObject);
        if (targetStatus == null)
            return;

        interactModule.ServerTryStartCharacterGrab(targetStatus);
    }

    private bool TryCreateCharacterGrabTargetReference(PlayerStatusModule targetStatus, out NetworkObjectReference targetReference)
    {
        targetReference = default;

        if (targetStatus == null)
            return false;

        NetworkObject targetObject = targetStatus.GetComponentInParent<NetworkObject>();
        if (targetObject == null)
            targetObject = targetStatus.NetworkObject;

        if (targetObject == null || !targetObject.IsSpawned)
            return false;

        targetReference = targetObject;
        return true;
    }

    private PlayerStatusModule ResolveCharacterGrabTargetStatus(NetworkObject targetObject)
    {
        if (targetObject == null)
            return null;

        PlayerStatusModule targetStatus = targetObject.GetComponentInChildren<PlayerStatusModule>(true);
        if (targetStatus != null)
            return targetStatus;

        return targetObject.GetComponentInParent<PlayerStatusModule>();
    }

    private void HandleCameraRotation(float pitchDelta)
    {
        if (cameraRoot == null) return;

        float scaledPitchDelta = pitchDelta * Mathf.Max(0f, manualPitchInputScale);
        _cameraPitchVelocity -= scaledPitchDelta;

        _cameraPitchVelocity = Mathf.Clamp(_cameraPitchVelocity, bottomClamp, topClamp);
        UpdateCameraLocalPosition();
        ApplyStableCameraWorldRotation();
    }

    private void ApplyDefaultCameraPitchImmediate()
    {
        CacheCameraDefaults();
        _cameraPitchVelocity = GetClampedDefaultQuarterViewPitch();

        if (cameraRoot != null)
        {
            ApplyCameraLocalPositionImmediate();
            ApplyStableCameraWorldRotation();
        }
    }

    private float GetClampedDefaultQuarterViewPitch()
    {
        return Mathf.Clamp(defaultQuarterViewPitch, bottomClamp, topClamp);
    }

    private void CacheCameraDefaults()
    {
        if (!_defaultQuarterViewYawCaptured)
        {
            _defaultQuarterViewYaw = GetStablePlayerYaw();
            _defaultQuarterViewYawCaptured = true;
        }

        if (!_cameraRootYawOffsetCaptured && cameraRoot != null)
        {
            _cameraRootYawOffset = Mathf.DeltaAngle(GetStablePlayerYaw(), GetYawOnly(cameraRoot.transform, GetStablePlayerYaw()));
            if (!IsFiniteFloat(_cameraRootYawOffset))
                _cameraRootYawOffset = 0f;

            _cameraRootYawOffsetCaptured = true;
        }

        if (!_cameraRootBaseLocalPositionCaptured && cameraRoot != null)
        {
            _cameraRootBaseLocalPosition = GetCameraLocalPositionFromWorld(cameraRoot.transform.position);
            _cameraRootBaseLocalPositionCaptured = true;
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
        float moveMagnitude = Mathf.Clamp01(_ownerPresentationMoveInput.magnitude);
        float forwardWeight = Mathf.Clamp01(_ownerPresentationMoveInput.y);
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

        Vector3 desiredOffset = GetStableCameraLocalVectorFromWorld(desiredFocusWorld - defaultFocusWorld);

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
        focusWorld = ClampRagdollCameraFocusWorldHeight(focusWorld);
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

    private Vector3 ClampRagdollCameraFocusWorldHeight(Vector3 focusWorld)
    {
        float minimumFocusY = GetStableCameraPositionOrigin().y + GetFiniteNonNegative(ragdollCameraMinimumFocusHeight);
        if (focusWorld.y < minimumFocusY)
            focusWorld.y = minimumFocusY;

        return focusWorld;
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

        ApplyCameraLocalPosition(targetLocalPosition, obstructed, "update");
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
        SnapCameraLocalPosition(_cameraCurrentLocalPosition, "immediate", false);
        _cameraWasObstructedLastFrame = obstructed;
    }

    private void ApplyCameraLocalPosition(Vector3 targetLocalPosition, bool obstructed, string reason)
    {
        if (cameraRoot == null)
        {
            ResetCameraPositionSmoothingState();
            return;
        }

        float deltaTime = Time.deltaTime;
        if (ShouldSnapCameraLocalPosition(targetLocalPosition, obstructed, deltaTime, out string snapReason, out bool logSnap))
        {
            SnapCameraLocalPosition(targetLocalPosition, snapReason, logSnap);
            return;
        }

        float smoothTime = Mathf.Max(0.0001f, GetFiniteNonNegative(cameraPositionSmoothTime));
        float maxSpeed = Mathf.Max(0f, GetCameraPositionBlendSpeed(targetLocalPosition, obstructed));
        _cameraCurrentLocalPosition = Vector3.SmoothDamp(
            _cameraCurrentLocalPosition,
            targetLocalPosition,
            ref _cameraPositionSmoothVelocity,
            smoothTime,
            maxSpeed,
            deltaTime
        );

        if (!IsFiniteVector3(_cameraCurrentLocalPosition))
        {
            SnapCameraLocalPosition(targetLocalPosition, reason, false);
            return;
        }

        _cameraLocalPositionInitialized = true;
        _hasSmoothedCameraPosition = true;
        ClearCameraSmoothingSnapLogState();
        cameraRoot.transform.position = GetCameraWorldPositionFromLocal(_cameraCurrentLocalPosition);
    }

    private bool ShouldSnapCameraLocalPosition(Vector3 targetLocalPosition, bool obstructed, float deltaTime, out string snapReason, out bool logSnap)
    {
        snapReason = null;
        logSnap = false;

        if (!_cameraLocalPositionInitialized || !_hasSmoothedCameraPosition)
        {
            snapReason = "first-frame";
            logSnap = true;
            return true;
        }

        if (!enableCameraPositionSmoothing)
        {
            snapReason = "smoothing-disabled";
            return true;
        }

        if (!IsFiniteFloat(deltaTime) || deltaTime <= 0f)
        {
            snapReason = "invalid-delta-time";
            return true;
        }

        if (GetFiniteNonNegative(cameraPositionSmoothTime) <= 0f)
        {
            snapReason = "smooth-time-zero";
            return true;
        }

        if (cameraObstructionSnapInward && IsCameraObstructionMovingInward(targetLocalPosition, obstructed))
        {
            snapReason = "obstruction-inward";
            logSnap = true;
            return true;
        }

        float maxSmoothDistance = GetFiniteNonNegative(cameraPositionMaxSmoothDistance);
        if (Vector3.Distance(_cameraCurrentLocalPosition, targetLocalPosition) > maxSmoothDistance)
        {
            snapReason = "max-distance";
            logSnap = true;
            return true;
        }

        return false;
    }

    private bool IsCameraObstructionMovingInward(Vector3 targetLocalPosition, bool obstructed)
    {
        if (!obstructed || !_cameraLocalPositionInitialized)
            return false;

        float currentDistance = GetCameraDistanceFromObstructionOrigin(_cameraCurrentLocalPosition);
        float targetDistance = GetCameraDistanceFromObstructionOrigin(targetLocalPosition);
        if (!IsFiniteFloat(currentDistance) || !IsFiniteFloat(targetDistance))
            return false;

        return targetDistance + 0.01f < currentDistance;
    }

    private void SnapCameraLocalPosition(Vector3 targetLocalPosition, string reason, bool logSnap)
    {
        _cameraCurrentLocalPosition = targetLocalPosition;
        _cameraLocalPositionInitialized = true;
        _hasSmoothedCameraPosition = true;
        _cameraPositionSmoothVelocity = Vector3.zero;
        cameraRoot.transform.position = GetCameraWorldPositionFromLocal(_cameraCurrentLocalPosition);

        if (logSnap)
            LogCameraSmoothingSnap(reason);
        else
            ClearCameraSmoothingSnapLogState();
    }

    private void ResetCameraPositionSmoothingState()
    {
        _cameraPositionSmoothVelocity = Vector3.zero;
        _hasSmoothedCameraPosition = false;
        ClearCameraSmoothingSnapLogState();
    }

    private void LogCameraSmoothingSnap(string reason)
    {
        if (!cameraSmoothingDebugLogs)
            return;

        if (_cameraSmoothingWasSnappingLastFrame && _lastCameraSmoothingSnapReason == reason)
            return;

        Debug.Log($"[PlayerHub] Camera smoothing snap reason={reason}", this);
        _cameraSmoothingWasSnappingLastFrame = true;
        _lastCameraSmoothingSnapReason = reason;
    }

    private void ClearCameraSmoothingSnapLogState()
    {
        _cameraSmoothingWasSnappingLastFrame = false;
        _lastCameraSmoothingSnapReason = null;
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

        return ClampRagdollCameraFocusWorldHeight(_lastRagdollCameraFocusWorld);
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
        Vector3 horizontalOffset = GetStableCameraYawRotation() * new Vector3(localPosition.x, 0f, localPosition.z);
        return GetStableCameraPositionOrigin() + horizontalOffset + Vector3.up * localPosition.y;
    }

    private Vector3 GetCameraLocalPositionFromWorld(Vector3 worldPosition)
    {
        Vector3 fromOrigin = worldPosition - GetStableCameraPositionOrigin();
        Vector3 horizontalWorld = new Vector3(fromOrigin.x, 0f, fromOrigin.z);
        Vector3 horizontalLocal = Quaternion.Inverse(GetStableCameraYawRotation()) * horizontalWorld;
        return new Vector3(horizontalLocal.x, fromOrigin.y, horizontalLocal.z);
    }

    private Vector3 GetStableCameraLocalVectorFromWorld(Vector3 worldVector)
    {
        Vector3 horizontalWorld = new Vector3(worldVector.x, 0f, worldVector.z);
        Vector3 horizontalLocal = Quaternion.Inverse(GetStableCameraYawRotation()) * horizontalWorld;
        return new Vector3(horizontalLocal.x, worldVector.y, horizontalLocal.z);
    }

    private Vector3 GetStableCameraPositionOrigin()
    {
        return transform.position;
    }

    private Quaternion GetStableCameraYawRotation()
    {
        return Quaternion.Euler(0f, GetStableCameraYaw(), 0f);
    }

    private float GetStableCameraYaw()
    {
        return Mathf.Repeat(GetStablePlayerYaw() + _cameraRootYawOffset, 360f);
    }

    private float GetStablePlayerYaw()
    {
        return GetYawOnly(transform, transform.eulerAngles.y);
    }

    private static float GetYawOnly(Transform source, float fallbackYaw)
    {
        if (source == null)
            return fallbackYaw;

        Vector3 forward = Vector3.ProjectOnPlane(source.forward, Vector3.up);
        if (forward.sqrMagnitude > 0.0001f)
            return Quaternion.LookRotation(forward.normalized, Vector3.up).eulerAngles.y;

        return fallbackYaw;
    }

    private void ApplyStableCameraWorldRotation()
    {
        if (cameraRoot == null)
            return;

        float stablePitch = Mathf.Clamp(_cameraPitchVelocity, bottomClamp, topClamp);
        cameraRoot.transform.rotation = Quaternion.Euler(stablePitch, GetStableCameraYaw(), 0f);
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
        Vector3 stableRight = GetStableCameraYawRotation() * Vector3.right;
        if (stableRight.sqrMagnitude > 0.0001f)
            return stableRight.normalized;

        return Vector3.right;
    }

    private float GetProcessedYawDelta(float yawDelta, Vector2 moveInput, bool allowLook)
    {
        if (!allowLook)
            return 0f;

        float scaledYawDelta = yawDelta * Mathf.Max(0f, manualYawInputScale);
        if (Mathf.Abs(yawDelta) > Mathf.Max(0f, cameraYawInputDeadzone))
            return scaledYawDelta;

        return 0f;
    }

    private void ApplyInputRouteCameraYawOffset(float scaledYawDelta)
    {
        if (!ShouldUseInputRouteCameraYawOffset())
            return;

        if (Mathf.Abs(scaledYawDelta) <= 0.0001f)
            return;

        CacheCameraDefaults();
        _cameraRootYawOffset = Mathf.Repeat(_cameraRootYawOffset + scaledYawDelta, 360f);
        _cameraRootYawOffsetCaptured = true;
    }

    private bool ShouldUseInputRouteCameraYawOffset()
    {
        if (!IsInputRouteTarget())
            return false;

        if (locomotionModule == null)
            return true;

        CharacterController characterController = CharacterController;
        return characterController == null || !characterController.enabled;
    }

    private void ApplyInputRouteOwnerCameraHandoff()
    {
        if (_hasLocalCameraOverride)
        {
            bool restoreOwnerPresentation = IsOwner && IsSpawned && isActiveAndEnabled;
            if (!IsLocalCameraOverridePresentationValid())
            {
                ForceEndLocalCameraOverride(restoreOwnerPresentation);
                return;
            }

            bool presentationApplied = false;
            _isLocalCameraOverrideMutationInProgress = true;
            try
            {
                ApplyLocalCameraOverridePresentation();
                presentationApplied = true;
            }
            catch (System.Exception)
            {
            }

            _isLocalCameraOverrideMutationInProgress = false;
            if (!presentationApplied || !IsLocalCameraOverridePresentationValid())
                ForceEndLocalCameraOverride(restoreOwnerPresentation);

            return;
        }

        if (!IsInputRouteTarget())
            return;

        Camera playerCamera = PlayerCamera;
        if (playerCamera == null)
            return;

        if (!IsOwner)
        {
            playerCamera.enabled = false;
            AudioListener nonOwnerListener = playerCamera.GetComponent<AudioListener>();
            if (nonOwnerListener != null)
                nonOwnerListener.enabled = false;

            return;
        }

        if (cameraRoot != null && !cameraRoot.activeSelf)
            cameraRoot.SetActive(true);

        if (!playerCamera.gameObject.activeSelf)
            playerCamera.gameObject.SetActive(true);

        if (!playerCamera.enabled)
            playerCamera.enabled = true;

        EnsureMainCameraTag(playerCamera);
        EnsureOwnerAudioListener(playerCamera);
        DisableCompetingMainCameras(playerCamera);
        DisableCompetingAudioListeners(audioListener);
    }

    private bool IsLocalCameraOverridePresentationValid()
    {
        if (!IsLocalCameraOverrideActive ||
            !IsOwner ||
            !IsSpawned ||
            !isActiveAndEnabled ||
            statusModule == null ||
            !statusModule.IsEliminated ||
            !TryGetGameState(out GameStateManager.GameState state) ||
            state != GameStateManager.GameState.Playing ||
            _localCameraOverrideGameObject == null ||
            _localCameraOverrideOriginalPlayerCamera == null ||
            _localCameraOverrideOriginalPlayerCameraGameObject == null ||
            _localCameraOverrideCamera.gameObject != _localCameraOverrideGameObject ||
            _localCameraOverrideAudioListener.gameObject != _localCameraOverrideGameObject)
        {
            return false;
        }

        Camera playerCamera = PlayerCamera;
        if (playerCamera == null ||
            playerCamera != _localCameraOverrideOriginalPlayerCamera ||
            playerCamera.gameObject != _localCameraOverrideOriginalPlayerCameraGameObject ||
            playerCamera == _localCameraOverrideCamera)
        {
            return false;
        }

        Scene overrideScene = _localCameraOverrideGameObject.scene;
        return overrideScene.IsValid() &&
               overrideScene.isLoaded &&
               _localCameraOverrideGameObject.GetComponentInParent<NetworkObject>(true) == null &&
               _localCameraOverrideAudioListener.GetComponentInParent<NetworkObject>(true) == null;
    }

    private void ApplyLocalCameraOverridePresentation()
    {
        if (cameraRoot != null && !cameraRoot.activeSelf)
            cameraRoot.SetActive(true);

        if (_localCameraOverrideOriginalPlayerCamera != null)
            _localCameraOverrideOriginalPlayerCamera.enabled = false;

        if (_localCameraOverrideOriginalPlayerAudioListener != null)
            _localCameraOverrideOriginalPlayerAudioListener.enabled = false;

        if (_localCameraOverrideOriginalPlayerCameraGameObject != null)
            _localCameraOverrideOriginalPlayerCameraGameObject.tag = "Untagged";

        if (!_localCameraOverrideGameObject.activeSelf)
            _localCameraOverrideGameObject.SetActive(true);

        _localCameraOverrideCamera.enabled = true;
        _localCameraOverrideGameObject.tag = RuntimeMainCameraTag;
        _localCameraOverrideAudioListener.enabled = true;
        DisableCompetingMainCameras(_localCameraOverrideCamera);
        DisableCompetingAudioListeners(_localCameraOverrideAudioListener);
    }

    private void ForceEndLocalCameraOverride(
        bool restoreOwnerPresentation,
        bool restoreOriginalPlayerSnapshot = false)
    {
        if (!_hasLocalCameraOverride || _isLocalCameraOverrideMutationInProgress)
            return;

        Camera restoredOverrideCamera = _localCameraOverrideCamera;
        AudioListener restoredOverrideAudioListener =
            _localCameraOverrideAudioListener;
        _isLocalCameraOverrideMutationInProgress = true;
        RestoreLocalCameraOverrideSnapshot(
            restoreOwnerPresentation || restoreOriginalPlayerSnapshot);
        ClearLocalCameraOverrideState();
        ResetCameraPositionSmoothingState();

        if (restoreOwnerPresentation && IsOwner && IsSpawned && isActiveAndEnabled)
        {
            _localCameraHandoffPreservedCamera = restoredOverrideCamera;
            _localCameraHandoffPreservedAudioListener = restoredOverrideAudioListener;
            try
            {
                ApplyInputRouteOwnerCameraHandoff();
            }
            catch (System.Exception)
            {
            }
            finally
            {
                _localCameraHandoffPreservedCamera = null;
                _localCameraHandoffPreservedAudioListener = null;
            }
        }

        _isLocalCameraOverrideMutationInProgress = false;
    }

    private void RestoreLocalCameraOverrideSnapshot(
        bool restoreOriginalPlayerSnapshot)
    {
        try
        {
            if (_localCameraOverrideCamera != null)
                _localCameraOverrideCamera.enabled = _localCameraOverrideCameraEnabled;

            if (_localCameraOverrideGameObject != null)
                _localCameraOverrideGameObject.tag = _localCameraOverrideCameraOriginalTag;
        }
        catch (System.Exception)
        {
        }

        try
        {
            if (_localCameraOverrideAudioListener != null)
                _localCameraOverrideAudioListener.enabled = _localCameraOverrideAudioListenerEnabled;
        }
        catch (System.Exception)
        {
        }

        try
        {
            if (_localCameraOverrideGameObject != null)
            {
                _localCameraOverrideGameObject.SetActive(
                    _localCameraOverrideCameraGameObjectWasActive);
            }
        }
        catch (System.Exception)
        {
        }

        try
        {
            if (_localCameraOverrideOriginalPlayerCameraGameObject != null)
            {
                _localCameraOverrideOriginalPlayerCameraGameObject.tag =
                    _localCameraOverrideOriginalPlayerCameraTag;
            }

            if (_localCameraOverrideOriginalPlayerCamera != null)
            {
                _localCameraOverrideOriginalPlayerCamera.enabled =
                    restoreOriginalPlayerSnapshot &&
                    _localCameraOverrideOriginalPlayerCameraEnabled;
            }
        }
        catch (System.Exception)
        {
        }

        try
        {
            if (_localCameraOverrideOriginalPlayerAudioListener != null)
            {
                _localCameraOverrideOriginalPlayerAudioListener.enabled =
                    restoreOriginalPlayerSnapshot &&
                    _localCameraOverrideOriginalPlayerAudioListenerEnabled;
            }
        }
        catch (System.Exception)
        {
        }
    }

    private void ClearLocalCameraOverrideState()
    {
        _hasLocalCameraOverride = false;
        _localCameraOverrideRequester = null;
        _localCameraOverrideCamera = null;
        _localCameraOverrideAudioListener = null;
        _localCameraOverrideGameObject = null;
        _localCameraOverrideOriginalPlayerCamera = null;
        _localCameraOverrideOriginalPlayerAudioListener = null;
        _localCameraOverrideOriginalPlayerCameraGameObject = null;
        _localCameraOverrideOriginalPlayerCameraEnabled = false;
        _localCameraOverrideOriginalPlayerCameraTag = null;
        _localCameraOverrideOriginalPlayerAudioListenerEnabled = false;
        _localCameraOverrideCameraGameObjectWasActive = false;
        _localCameraOverrideCameraEnabled = false;
        _localCameraOverrideCameraOriginalTag = null;
        _localCameraOverrideAudioListenerEnabled = false;
        _localCameraHandoffPreservedCamera = null;
        _localCameraHandoffPreservedAudioListener = null;
    }

    private void EnsureMainCameraTag(Camera playerCamera)
    {
        if (playerCamera == null)
            return;

        if (playerCamera.CompareTag(RuntimeMainCameraTag))
            return;

        playerCamera.gameObject.tag = RuntimeMainCameraTag;
    }

    private void EnsureOwnerAudioListener(Camera playerCamera)
    {
        if (playerCamera == null)
            return;

        if (audioListener == null)
            audioListener = playerCamera.GetComponent<AudioListener>();

        if (audioListener == null)
            audioListener = playerCamera.gameObject.AddComponent<AudioListener>();

        audioListener.enabled = true;
    }

    private void DisableCompetingMainCameras(Camera ownerCamera)
    {
        if (ownerCamera == null)
            return;

        Camera[] cameras = FindObjectsByType<Camera>(FindObjectsSortMode.None);
        for (int i = 0; i < cameras.Length; i++)
        {
            Camera candidate = cameras[i];
            if (candidate == null ||
                candidate == ownerCamera ||
                candidate == _localCameraHandoffPreservedCamera ||
                !candidate.enabled)
                continue;

            bool isSceneMainCamera = candidate.name == SceneMainCameraName;
            bool hasMainCameraTag = candidate.CompareTag(RuntimeMainCameraTag);
            if (!isSceneMainCamera && !hasMainCameraTag)
                continue;

            candidate.enabled = false;
            AudioListener listener = candidate.GetComponent<AudioListener>();
            if (listener != null)
                listener.enabled = false;
        }
    }

    private void DisableCompetingAudioListeners(AudioListener ownerListener)
    {
        if (ownerListener == null)
            return;

        AudioListener[] listeners = FindObjectsByType<AudioListener>(FindObjectsSortMode.None);
        for (int i = 0; i < listeners.Length; i++)
        {
            AudioListener candidate = listeners[i];
            if (candidate == null ||
                candidate == ownerListener ||
                candidate == _localCameraHandoffPreservedAudioListener ||
                !candidate.enabled)
                continue;

            candidate.enabled = false;
        }
    }

    private void LogMotorShellCameraInput(
        float rawYawDelta,
        float rawPitchDelta,
        bool allowLook,
        float cameraPivotYawBefore,
        float cameraPivotYawAfter,
        float playerCameraPitchBefore,
        float playerCameraPitchAfter,
        string routeResult)
    {
        if (!ShouldLogMotorShellCamera())
            return;

        Camera playerCamera = PlayerCamera;
        Camera mainCamera = Camera.main;
        string baseState = $"isOwner={IsOwner} isServer={IsServer} scene={SceneManager.GetActiveScene().name} gameState={GetInputRouteGameState()}";
        string selectedUi = GetCurrentSelectedGameObjectName();
        AudioListener playerListener = playerCamera != null ? playerCamera.GetComponent<AudioListener>() : null;

        Debug.Log(
            $"[MSCamera/Input:{GetInputRouteObjectName()}] {baseState} Cursor.lockState={Cursor.lockState} Cursor.visible={Cursor.visible} EventSystem.currentSelectedGameObject={selectedUi} mouseDelta=({rawYawDelta:F2},{rawPitchDelta:F2}) allowLook={allowLook} routeResult={routeResult}",
            this);
        Debug.Log(
            $"[MSCamera/Owner:{GetInputRouteObjectName()}] {baseState} PlayerCamera.exists={playerCamera != null} PlayerCamera.enabled={(playerCamera != null && playerCamera.enabled)} PlayerCamera.tag={(playerCamera != null ? playerCamera.tag : "<null>")} AudioListener.exists={playerListener != null} AudioListener.enabled={(playerListener != null && playerListener.enabled)} cameraRoot.activeSelf={(cameraRoot != null && cameraRoot.activeSelf)}",
            this);
        Debug.Log(
            $"[MSCamera/Pivot:{GetInputRouteObjectName()}] {baseState} CameraPivot.yaw.before={FormatCameraFloat(cameraPivotYawBefore)} CameraPivot.yaw.after={FormatCameraFloat(cameraPivotYawAfter)} PlayerCamera.pitch.before={FormatCameraFloat(playerCameraPitchBefore)} PlayerCamera.pitch.after={FormatCameraFloat(playerCameraPitchAfter)} mouseDelta=({rawYawDelta:F2},{rawPitchDelta:F2})",
            this);
        Debug.Log(
            $"[MSCamera/Main:{GetInputRouteObjectName()}] {baseState} PlayerCamera.enabled={(playerCamera != null && playerCamera.enabled)} PlayerCamera.tag={(playerCamera != null ? playerCamera.tag : "<null>")} Camera.main.name={(mainCamera != null ? mainCamera.name : "<null>")} Camera.main.isPlayerCamera={mainCamera == playerCamera}",
            this);
    }

    private bool ShouldLogMotorShellCamera()
    {
        if (!debugCameraLogs)
            return false;

        if (!IsInputRouteTarget())
            return false;

        if (Time.unscaledTime < _nextMotorShellCameraLogTime)
            return false;

        _nextMotorShellCameraLogTime = Time.unscaledTime + InputRouteLogInterval;
        return true;
    }

    private static float GetTransformYawForLog(Transform target)
    {
        return target != null ? NormalizeSignedAngle(target.eulerAngles.y) : float.NaN;
    }

    private static float GetTransformPitchForLog(Transform target)
    {
        return target != null ? NormalizeSignedAngle(target.eulerAngles.x) : float.NaN;
    }

    private static float NormalizeSignedAngle(float angle)
    {
        return Mathf.DeltaAngle(0f, angle);
    }

    private static string FormatCameraFloat(float value)
    {
        return IsFiniteFloat(value) ? value.ToString("F2") : "<null>";
    }

    private static string GetCurrentSelectedGameObjectName()
    {
        GameObject selected = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
        return selected != null ? selected.name : "<none>";
    }

    private void TickServer()
    {
        bool heavyActive = TryGetPostItHeavyMovementScale(out float heavyMovementScale);
        bool gameplayPhaseLocked = IsGameplayPhaseLocked();
        ApplyGameplayPhaseLockServer(gameplayPhaseLocked);
        if (gameplayPhaseLocked)
        {
            NeutralizeRoutedGameplayInput();
            ApplyOwnedGameplayPhaseJumpLock();
        }

        ApplyOwnedPostItHeavyJumpLock(heavyActive);

        ResolveServerPostItInputRoute(
            heavyActive,
            heavyMovementScale,
            out Vector2 routedMove,
            out bool routedSprintHeld,
            out bool routedJumpPressed);

        if (TryTickMotorShellServer(routedMove, routedSprintHeld, routedJumpPressed))
            return;

        CharacterController characterController = CharacterController;
        if (characterController == null || !characterController.enabled)
        {
            LogInputRouteServer(false, 0f, characterController == null ? "CharacterController null" : "CharacterController disabled");
            return;
        }

        bool jumped = false;
        float serverYawDelta = AllowServerLookInput() ? _yawDelta : 0f;

        if (locomotionModule != null)
            jumped = locomotionModule.TickServer(routedMove, serverYawDelta, routedJumpPressed, routedSprintHeld);

        if (jumped && animModule != null) animModule.TriggerJump();

        if (animModule != null && locomotionModule != null)
            animModule.TickServer(locomotionModule);

        LogInputRouteServer(jumped, serverYawDelta, locomotionModule != null ? "locomotion tick" : "locomotionModule null");

        _jumpPressed = false;
        _yawDelta = 0f;
    }

    private void ResolveServerPostItInputRoute(
        bool heavyActive,
        float heavyMovementScale,
        out Vector2 move,
        out bool sprintHeld,
        out bool jumpPressed)
    {
        move = _moveInput;
        sprintHeld = _sprintHeld;
        jumpPressed = _jumpPressed;

        if (!heavyActive)
            return;

        move *= heavyMovementScale;
        sprintHeld = false;
        jumpPressed = false;
        _jumpPressed = false;
    }

    private bool TryTickMotorShellServer(Vector2 move, bool sprintHeld, bool jumpPressed)
    {
        HamsterFullRagdollMotor motorShellMotor = ResolveMotorShellMotor();
        if (motorShellMotor == null || !motorShellMotor.IsMainScenesInputRouteTarget)
            return false;

        float serverYawDelta = MirrorInputRouteCameraYawOnServer();
        motorShellMotor.SetNetworkInput(move, sprintHeld, jumpPressed);
        LogInputRouteServer(false, serverYawDelta, motorShellMotor.isActiveAndEnabled ? "motor shell input routed" : "motor shell disabled");

        _jumpPressed = false;
        _yawDelta = 0f;
        return true;
    }

    private float MirrorInputRouteCameraYawOnServer()
    {
        if (!IsServer ||
            !IsInputRouteTarget() ||
            IsOwner ||
            !AllowServerLookInput() ||
            Mathf.Abs(_yawDelta) <= 0.0001f)
        {
            return 0f;
        }

        ApplyInputRouteCameraYawOffset(_yawDelta);
        ApplyStableCameraWorldRotation();
        return _yawDelta;
    }

    private HamsterFullRagdollMotor ResolveMotorShellMotor()
    {
        if (_motorShellMotor != null)
            return _motorShellMotor;

        _motorShellMotor = GetComponentInChildren<HamsterFullRagdollMotor>(true);
        return _motorShellMotor;
    }

    [ServerRpc(Delivery = RpcDelivery.Unreliable)]
    private void SubmitInputServerRpc(Vector2 move, float yawDelta, bool sprintHeld)
    {
        if (IsGameplayPhaseLocked())
        {
            ClearPendingGameplayInput();
            return;
        }

        _moveInput = move;
        _yawDelta = yawDelta;
        _sprintHeld = sprintHeld;
    }

    private void LogInputRouteOwner(
        Vector2 rawMove,
        Vector2 routedMove,
        bool jumpPressed,
        bool rawSprintHeld,
        bool routedSprintHeld,
        bool allowLook,
        string routeResult)
    {
        if (!ShouldLogInputRoute(ref _nextInputRouteOwnerLogTime))
            return;

        Debug.Log(
            $"[InputRoute/Hub:{GetInputRouteObjectName()}] phase=Owner enabled={enabled} active={gameObject.activeInHierarchy} isSpawned={IsSpawned} isOwner={IsOwner} ownerClientId={OwnerClientId} networkManagerIsServer={(NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer)} scene={SceneManager.GetActiveScene().name} gameState={GetInputRouteGameState()} inputModule={FormatBehaviour(inputModule)} locomotionModule={FormatBehaviour(locomotionModule)} animModule={FormatBehaviour(animModule)} statusModule={FormatBehaviour(statusModule)} externalMotorShellFlag=<none> rawMove={FormatVector2(rawMove)} routedMove={FormatVector2(routedMove)} jumpPressed={jumpPressed} rawSprintHeld={rawSprintHeld} routedSprintHeld={routedSprintHeld} allowLook={allowLook} routeResult={routeResult}",
            this);
    }

    private void LogInputRouteServer(bool jumped, float serverYawDelta, string routeResult)
    {
        if (!ShouldLogInputRoute(ref _nextInputRouteServerLogTime))
            return;

        Debug.Log(
            $"[InputRoute/Hub:{GetInputRouteObjectName()}] phase=Server enabled={enabled} active={gameObject.activeInHierarchy} isSpawned={IsSpawned} isOwner={IsOwner} ownerClientId={OwnerClientId} networkManagerIsServer={(NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer)} scene={SceneManager.GetActiveScene().name} gameState={GetInputRouteGameState()} inputModule={FormatBehaviour(inputModule)} locomotionModule={FormatBehaviour(locomotionModule)} animModule={FormatBehaviour(animModule)} statusModule={FormatBehaviour(statusModule)} externalMotorShellFlag=<none> moveInput={FormatVector2(_moveInput)} sprintHeld={_sprintHeld} jumpQueued={_jumpPressed} jumped={jumped} serverYawDelta={serverYawDelta:F2} routeResult={routeResult}",
            this);
    }

    private bool ShouldLogInputRoute(ref float nextLogTime)
    {
        if (!debugMovementRoutingLogs)
            return false;

        if (!IsInputRouteTarget())
            return false;

        if (Time.unscaledTime < nextLogTime)
            return false;

        nextLogTime = Time.unscaledTime + InputRouteLogInterval;
        return true;
    }

    private bool IsInputRouteTarget()
    {
        Transform root = transform.root;
        return root != null && root.name.Contains(InputRouteTargetName);
    }

    private string GetInputRouteObjectName()
    {
        Transform root = transform.root;
        return root != null ? root.name : gameObject.name;
    }

    private string GetInputRouteGameState()
    {
        return TryGetGameState(out GameStateManager.GameState state) ? state.ToString() : "<missing>";
    }

    private static string FormatBehaviour(Behaviour behaviour)
    {
        return behaviour != null ? $"exists=True enabled={behaviour.enabled}" : "exists=False";
    }

    private static string FormatVector2(Vector2 value)
    {
        return $"({value.x:F2},{value.y:F2})";
    }

    [ServerRpc(Delivery = RpcDelivery.Reliable)]
    private void QueueJumpServerRpc()
    {
        if (IsGameplayPhaseLocked())
        {
            _jumpPressed = false;
            return;
        }

        if (interactModule != null && interactModule.IsGrabbedByCharacter)
        {
            interactModule.ServerRegisterCharacterGrabEscapeTap(OwnerClientId);
            return;
        }

        if (!CanMoveNow())
            return;

        if (TryGetPostItHeavyMovementScale(out _))
        {
            _jumpPressed = false;
            ApplyOwnedPostItHeavyJumpLock(true);
            return;
        }

        _jumpPressed = true;
    }

    [Rpc(
        SendTo.Server,
        InvokePermission = RpcInvokePermission.Owner)]
    private void AttackServerRpc()
    {
        if (IsGameplayPhaseLocked())
        {
            _attackBufferedServer = false;
            _attackBufferedAtServer = 0f;
            return;
        }

        if (TryConsumeCharacterThrowOrBlockAttackServer())
        {
            _attackBufferedServer = false;
            return;
        }

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
        if (IsGameplayPhaseLocked())
        {
            _attackBufferedServer = false;
            _attackBufferedAtServer = 0f;
            return;
        }

        if (TryConsumeCharacterThrowOrBlockAttackServer())
            return;

        if (!CanAttackNow())
            return;

        HamsterMotorShellCombatAdapter motorShellCombatAdapter = ResolveMotorShellCombatAdapter();
        if (motorShellCombatAdapter != null)
        {
            TryStartMotorShellAttackServer(motorShellCombatAdapter, out _);
            return;
        }

        if (!ShouldAllowBasicAttackWithStamina())
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

    private bool TryStartMotorShellAttackServer(
        HamsterMotorShellCombatAdapter adapter,
        out string failureReason)
    {
        failureReason = "none";
        if (adapter == null)
        {
            failureReason = "adapter missing";
            return false;
        }

        if (!adapter.CanQueueServerAttackFromPlayerHub(out failureReason))
            return false;

        bool shouldSpendStamina =
            useStaminaForBasicAttack &&
            Mathf.Max(0f, basicAttackStaminaCost) > 0f;
        PlayerStaminaModule targetStaminaModule =
            shouldSpendStamina
                ? ResolveBasicAttackStaminaModule()
                : null;
        if (shouldSpendStamina)
        {
            if (targetStaminaModule == null)
            {
                if (!allowBasicAttackWhenStaminaModuleMissing)
                {
                    failureReason = "stamina module missing";
                    return false;
                }
            }
            else if (!CanUseBasicAttackStamina(targetStaminaModule))
            {
                failureReason = "stamina unavailable";
                return false;
            }
        }

        float spentStaminaAmount = 0f;
        if (shouldSpendStamina &&
            targetStaminaModule != null)
        {
            if (!TryConsumeBasicAttackStamina(targetStaminaModule))
            {
                failureReason = "stamina spend rejected";
                return false;
            }

            spentStaminaAmount =
                Mathf.Max(0f, basicAttackStaminaCost);
        }

        if (!adapter.ServerTryQueueAttackFromPlayerHub(out failureReason))
        {
            float restoredStaminaAmount =
                spentStaminaAmount > 0f &&
                targetStaminaModule != null
                    ? targetStaminaModule.ServerRestoreStamina(
                        spentStaminaAmount)
                    : 0f;
            if (!Mathf.Approximately(
                    restoredStaminaAmount,
                    spentStaminaAmount))
            {
                Debug.LogError(
                    $"[MSCombat/Hub] invariant=stamina rollback incomplete spent={spentStaminaAmount:F3} restored={restoredStaminaAmount:F3} failureReason={failureReason}",
                    this);
            }

            Debug.LogWarning(
                $"[MSCombat/Hub] invariant=queue failed after successful preflight spent={spentStaminaAmount:F3} restored={restoredStaminaAmount:F3} NetworkObjectId={NetworkObjectId} OwnerClientId={OwnerClientId} IsServer={IsServer} IsOwner={IsOwner} route=HamsterAdapter failureReason={failureReason}",
                this);
            return false;
        }

        _attackLockedServer = true;
        if (_attackLockRoutine != null) StopCoroutine(_attackLockRoutine);
        _attackLockRoutine = StartCoroutine(ServerAttackLockRoutine());
        failureReason = "none";
        return true;
    }

    private bool ShouldAllowBasicAttackWithStamina()
    {
        if (!useStaminaForBasicAttack)
            return true;

        if (Mathf.Max(0f, basicAttackStaminaCost) <= 0f)
            return true;

        PlayerStaminaModule targetStaminaModule =
            ResolveBasicAttackStaminaModule();
        if (targetStaminaModule == null)
            return allowBasicAttackWhenStaminaModuleMissing;

        if (!CanUseBasicAttackStamina(targetStaminaModule))
            return false;

        return TryConsumeBasicAttackStamina(targetStaminaModule);
    }

    private PlayerStaminaModule ResolveBasicAttackStaminaModule()
    {
        PlayerStaminaModule candidate = staminaModule;
        NetworkObject playerNetworkObject = NetworkObject;
        if (candidate == null ||
            playerNetworkObject == null ||
            candidate.NetworkObject != playerNetworkObject)
        {
            return null;
        }

        return candidate;
    }

    private bool CanUseBasicAttackStamina(PlayerStaminaModule targetStaminaModule)
    {
        if (Mathf.Max(0f, basicAttackStaminaCost) <= 0f)
            return true;

        if (targetStaminaModule == null)
            return allowBasicAttackWhenStaminaModuleMissing;

        return targetStaminaModule.ServerCanSpendStamina(Mathf.Max(0f, basicAttackMinimumStaminaToStart));
    }

    private bool TryConsumeBasicAttackStamina(PlayerStaminaModule targetStaminaModule)
    {
        float spendAmount = Mathf.Max(0f, basicAttackStaminaCost);
        if (spendAmount <= 0f)
            return true;

        if (targetStaminaModule == null)
            return allowBasicAttackWhenStaminaModuleMissing;

        return targetStaminaModule.ServerTrySpendStamina(spendAmount);
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

        if (IsGameplayPhaseLocked())
        {
            _attackBufferedServer = false;
            _attackBufferedAtServer = 0f;
            return;
        }

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

    private bool TryConsumeCharacterThrowOrBlockAttackServer()
    {
        if (interactModule == null)
            return false;

        if (interactModule.CanThrowCarriedCharacter)
        {
            interactModule.ServerTryThrowCarriedCharacter("AttackInput");
            return true;
        }

        return interactModule.IsGrabbingCharacter;
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
        if (IsGameplayPhaseLocked()) return;
        if (!CanInteractNow()) return;
        if (interactModule == null) return;
        if (!interactModule.ServerTryPickup(target)) return;

        if (animModule != null) animModule.TriggerPickUp();
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        basicAttackStaminaCost = Mathf.Max(0f, basicAttackStaminaCost);
        basicAttackMinimumStaminaToStart = Mathf.Max(0f, basicAttackMinimumStaminaToStart);
        ResolveRefs();
    }
#endif
}
