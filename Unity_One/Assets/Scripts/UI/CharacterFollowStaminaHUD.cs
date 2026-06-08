using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

public class CharacterFollowStaminaHUD : MonoBehaviour
{
    public enum PresentationMode
    {
        ScreenSpace,
        WorldSpaceAnchor,
        StabilizedWorldAnchor,
        ScreenLockedWorldAnchor,
        CharacterScreenAnchor,
        PrefabAttachedLocalOwner
    }

    public enum FollowOffsetMode
    {
        ScreenOffset,
        CameraRelativeWorldOffset
    }

    public enum VisualLockMode
    {
        None,
        LockedInitialWorldRotation,
        ScreenFacingUpright,
        CameraFacingFixedScale,
        TargetYawLocalRotation
    }

    [Header("UI")]
    [SerializeField, Tooltip("스태미나 UI 전체 루트 RectTransform입니다. 비워두면 자기 RectTransform을 사용합니다.")]
    private RectTransform root;

    [SerializeField, Tooltip("캐릭터를 따라 움직일 RectTransform입니다. 비워두면 root를 사용합니다.")]
    private RectTransform followRect;

    [SerializeField, Tooltip("좌표 변환에 사용할 Canvas입니다. 비워두면 부모에서 자동 탐색합니다.")]
    private Canvas targetCanvas;

    [SerializeField, Tooltip("스태미나 게이지의 채워지는 영역 Image입니다. Image Type은 Filled를 권장합니다.")]
    private Image staminaFillImage;

    [SerializeField, Tooltip("스태미나 비율을 표시할 Slider입니다. 비워두면 사용하지 않습니다.")]
    private Slider staminaSlider;

    [SerializeField, Tooltip("표시/숨김에 사용할 CanvasGroup입니다. 비워두면 오브젝트를 끄지 않고 가능한 UI 요소만 숨깁니다.")]
    private CanvasGroup canvasGroup;

    [SerializeField, Tooltip("스태미나 수치를 표시할 TMP 텍스트입니다. 선택 사항입니다.")]
    private TMP_Text staminaText;

    [Header("Follow")]
    [SerializeField, Tooltip("스태미나 UI를 표시하는 방식입니다. ScreenSpace는 기존 화면 좌표 추적, WorldSpaceAnchor는 캐릭터 옆 월드 위치에 직접 배치합니다.")]
    private PresentationMode presentationMode = PresentationMode.WorldSpaceAnchor;

    [SerializeField, Tooltip("ScreenSpace 모드에서 캐릭터 추적 위치를 계산하는 방식입니다. ScreenOffset은 기존 방식, CameraRelativeWorldOffset은 카메라 기준 캐릭터 옆 월드 위치를 사용합니다.")]
    private FollowOffsetMode followOffsetMode = FollowOffsetMode.CameraRelativeWorldOffset;

    [SerializeField, Tooltip("ScreenSpace의 ScreenOffset 모드 또는 WorldSpaceAnchor의 target local offset 미사용 시 캐릭터 월드 위치에 더할 오프셋입니다.")]
    private Vector3 worldOffset = new Vector3(0.75f, 1.0f, 0f);

    [Header("World Space Anchor")]
    [SerializeField, Tooltip("WorldSpaceAnchor 모드에서 targetLocalOffset을 캐릭터 local 좌표 기준으로 사용할지 여부입니다.")]
    private bool useTargetLocalOffset = true;

    [SerializeField, Tooltip("WorldSpaceAnchor 모드에서 캐릭터 local 기준 몸 옆 UI 위치입니다.")]
    private Vector3 targetLocalOffset = new Vector3(0.35f, 0.65f, 0f);

    [SerializeField, Tooltip("WorldSpaceAnchor 모드에서 UI가 카메라를 향하도록 회전할지 여부입니다.")]
    private bool billboardToCamera = true;

    [SerializeField, Tooltip("WorldSpaceAnchor 모드에서 초기 월드 스케일을 유지할지 여부입니다.")]
    private bool keepWorldScale = true;

    [SerializeField, Tooltip("WorldSpaceAnchor 모드에서 월드 위치를 따라가는 SmoothDamp 시간입니다. 0이면 즉시 이동합니다.")]
    private float worldFollowSmoothTime = 0f;

    [SerializeField, Tooltip("WorldSpaceAnchor 모드에서 이 거리보다 멀어지면 보간하지 않고 즉시 붙입니다.")]
    private float maxWorldSnapDistance = 2f;

    [SerializeField, Tooltip("WorldSpaceAnchor 모드의 SmoothDamp에 unscaled delta time을 사용할지 여부입니다.")]
    private bool useUnscaledTime = false;

    [Header("Visual Lock")]
    [SerializeField, Tooltip("월드 공간 계열 UI의 시각 회전 고정 방식입니다. None은 기존 회전 동작을 유지합니다.")]
    private VisualLockMode visualLockMode = VisualLockMode.TargetYawLocalRotation;

    [SerializeField, Tooltip("켜면 WorldSpaceAnchor, StabilizedWorldAnchor, World Space Canvas의 ScreenLockedWorldAnchor에서만 Visual Lock을 적용합니다.")]
    private bool applyVisualLockInWorldModesOnly = true;

    [SerializeField, Tooltip("켜면 최초 루트 local scale을 저장하고 계속 유지해 캐릭터 이동이나 카메라 회전으로 UI 크기가 바뀌지 않게 합니다.")]
    private bool keepVisualScale = true;

    [SerializeField, Tooltip("켜면 OnEnable 또는 첫 바인딩 시점의 루트 local scale을 Visual Lock 기준 scale로 저장합니다.")]
    private bool captureInitialVisualScaleOnEnable = true;

    [SerializeField, Tooltip("켜면 OnEnable 또는 첫 바인딩 시점의 루트 world rotation을 LockedInitialWorldRotation 기준 회전으로 저장합니다.")]
    private bool captureInitialWorldRotationOnEnable = true;

    [SerializeField, Tooltip("captureInitialWorldRotationOnEnable이 꺼져 있을 때 사용할 고정 world rotation Euler 값입니다.")]
    private Vector3 lockedWorldRotationEuler = Vector3.zero;

    [SerializeField, Tooltip("켜면 월드 공간 Visual Lock 적용 중 followRect의 local rotation을 identity로 유지해 내부 fill UI가 따로 기울지 않게 합니다.")]
    private bool resetFollowRectLocalRotation = true;

    [SerializeField, Tooltip("켜면 Visual Lock이 활성화된 모드에서 기존 billboard 회전 로직을 건너뛰어 이중 회전을 막습니다.")]
    private bool disableLegacyBillboardWhenVisualLocked = true;

    [SerializeField, Tooltip("켜면 ScreenFacingUpright에서 카메라 회전을 기준으로 UI를 카메라 평면과 평행하게 맞춥니다.")]
    private bool screenFacingUseCameraRotation = true;

    [SerializeField, Tooltip("켜면 ScreenFacingUpright 결과에 Y축 180도 회전을 더해 앞뒤 뒤집힘을 보정합니다.")]
    private bool screenFacingFlipForward = false;

    [SerializeField, Tooltip("켜면 ScreenFacingUpright에서 roll 기울기를 제거하고 월드 up 기준으로 똑바로 세웁니다.")]
    private bool screenFacingUprightOnly = true;

    [SerializeField, Tooltip("Visual Lock 회전을 부드럽게 따라가는 시간입니다. 0이면 즉시 적용합니다.")]
    private float visualRotationSmoothTime = 0f;

    [SerializeField, Tooltip("Visual Lock 관련 상태 변경 로그를 출력할지 여부입니다. 매 프레임 로그는 출력하지 않습니다.")]
    private bool visualLockDebugLogs = false;

    [Header("Target Visual Rotation")]
    [SerializeField, Tooltip("캐릭터의 yaw 회전만 따라가고 pitch/roll은 무시합니다.")]
    private bool targetVisualYawOnly = true;

    [SerializeField, Tooltip("캐릭터 기준으로 스태미나 UI가 바라볼 로컬 회전 보정값입니다.")]
    private Vector3 targetVisualLocalEuler = Vector3.zero;

    [SerializeField, Tooltip("UI 앞뒤가 반대로 보이면 180도 보정을 적용합니다.")]
    private bool targetVisualFlipForward = false;

    [SerializeField, Tooltip("켜면 바인딩 시점의 UI world rotation을 캐릭터 local rotation으로 변환해 저장합니다.")]
    private bool captureTargetVisualLocalRotationOnBind = false;

    [SerializeField, Tooltip("Follow Rect의 local rotation을 identity로 유지합니다.")]
    private bool targetVisualResetFollowRectLocalRotation = true;

    [SerializeField, Tooltip("UI local scale을 초기값으로 유지합니다.")]
    private bool targetVisualKeepScale = true;

    [SerializeField, Tooltip("캐릭터 회전을 따라갈 때 rotation smoothing 시간입니다. 0이면 즉시 적용합니다.")]
    private float targetVisualRotationSmoothTime = 0f;

    [Header("World Anchor Stabilization")]
    [SerializeField, Tooltip("StabilizedWorldAnchor 모드에서 작은 흔들림을 안정화할지 여부입니다. 꺼두면 기존 WorldSpaceAnchor 방식으로 동작합니다.")]
    private bool enableWorldAnchorStabilization = true;

    [SerializeField, Tooltip("StabilizedWorldAnchor 모드에서 수평 위치를 따라가는 SmoothDamp 시간입니다. 0이면 즉시 이동합니다.")]
    private float stabilizedWorldFollowSmoothTime = 0.08f;

    [SerializeField, Tooltip("StabilizedWorldAnchor 모드에서 Y축을 따라가는 SmoothDamp 시간입니다.")]
    private float stabilizedVerticalSmoothTime = 0.1f;

    [SerializeField, Tooltip("StabilizedWorldAnchor 모드에서 이 거리보다 멀어지면 보간하지 않고 즉시 붙입니다.")]
    private float worldAnchorSnapDistance = 1.5f;

    [SerializeField, Tooltip("StabilizedWorldAnchor 모드에서 이 거리 이하의 미세 위치 변화는 무시합니다.")]
    private float worldAnchorDeadzone = 0.02f;

    [SerializeField, Tooltip("StabilizedWorldAnchor 모드에서 Y축 흔들림을 별도 smooth time으로 안정화할지 여부입니다.")]
    private bool stabilizeVerticalMotion = true;

    [SerializeField, Tooltip("StabilizedWorldAnchor 모드에서 캐릭터 회전 대신 카메라 기준 옆 위치를 사용할지 여부입니다.")]
    private bool useCameraRelativeStableSide = true;

    [SerializeField, Tooltip("StabilizedWorldAnchor 모드에서 카메라 기준 오른쪽 방향으로 캐릭터 옆에 띄울 거리입니다.")]
    private float stableSideWorldOffset = 0.25f;

    [SerializeField, Tooltip("StabilizedWorldAnchor 모드에서 캐릭터 기준 위쪽으로 띄울 높이입니다.")]
    private float stableHeightWorldOffset = 0.55f;

    [SerializeField, Tooltip("StabilizedWorldAnchor 모드에서 카메라 기준 앞/뒤 방향 보정값입니다.")]
    private float stableDepthWorldOffset = 0f;

    [SerializeField, Tooltip("StabilizedWorldAnchor 모드에서 billboard 회전을 Yaw만 적용해 위아래 기울어짐을 막을지 여부입니다.")]
    private bool billboardYawOnly = true;

    [SerializeField, Tooltip("StabilizedWorldAnchor 모드에서 billboard 회전을 부드럽게 보정하는 시간입니다. 0이면 즉시 회전합니다.")]
    private float billboardRotationSmoothTime = 0.04f;

    [Header("Screen Locked World Anchor")]
    [SerializeField, Tooltip("캐릭터 화면 위치 기준으로 UI를 얼마나 옆/위에 둘지 픽셀 단위로 조정합니다.")]
    private Vector2 screenLockedOffset = new Vector2(42f, 24f);

    [SerializeField, Tooltip("캐릭터 월드 위치에서 화면 좌표로 변환하기 전 기준점을 살짝 올리는 월드 오프셋입니다.")]
    private Vector3 screenLockedWorldBaseOffset = new Vector3(0f, 0.55f, 0f);

    [SerializeField, Tooltip("ScreenLockedWorldAnchor 위치 smoothing 시간입니다.")]
    private float screenLockedSmoothTime = 0.04f;

    [SerializeField, Tooltip("UI가 목표 위치와 너무 멀면 즉시 스냅합니다.")]
    private float screenLockedSnapDistance = 1.5f;

    [SerializeField, Tooltip("UI가 화면에서 고정된 방향으로 보이도록 카메라 회전을 사용합니다.")]
    private bool screenLockedUseCameraRotation = true;

    [SerializeField, Tooltip("UI가 화면에서 뒤집히거나 기울어져 보이지 않게 유지합니다.")]
    private bool screenLockedKeepUpright = true;

    [SerializeField, Tooltip("UI rotation smoothing 시간입니다. 0이면 즉시 적용합니다.")]
    private float screenLockedRotationSmoothTime = 0f;

    [Header("Character Screen Anchor")]
    [SerializeField, Tooltip("캐릭터 월드 위치를 화면 좌표로 변환하기 전 기준점을 살짝 올리는 오프셋입니다.")]
    private Vector3 characterScreenWorldBaseOffset = new Vector3(0f, 0.55f, 0f);

    [SerializeField, Tooltip("캐릭터 화면 위치 기준 UI를 옆/위로 얼마나 띄울지 픽셀 단위로 조정합니다.")]
    private Vector2 characterScreenOffset = new Vector2(42f, 24f);

    [SerializeField, Tooltip("캐릭터 화면 위치를 따라가는 smoothing 시간입니다. 0이면 즉시 따라갑니다.")]
    private float characterScreenSmoothTime = 0.03f;

    [SerializeField, Tooltip("화면 위치 차이가 너무 크면 즉시 스냅합니다.")]
    private float characterScreenSnapDistance = 300f;

    [SerializeField, Tooltip("작은 화면 좌표 흔들림은 무시합니다.")]
    private float characterScreenDeadzone = 1.5f;

    [SerializeField, Tooltip("부모 Canvas 영역 밖으로 나가지 않게 제한합니다.")]
    private bool characterScreenClampToParent = true;

    [SerializeField, Tooltip("화면 가장자리 최소 여백입니다.")]
    private Vector2 characterScreenPadding = new Vector2(24f, 24f);

    [SerializeField, Tooltip("UI 회전을 항상 0으로 유지해 카메라 회전 중에도 UI가 돌지 않게 합니다.")]
    private bool characterScreenResetRotation = true;

    [SerializeField, Tooltip("CharacterScreenAnchor 위치 적용 로그를 출력합니다.")]
    private bool characterScreenDebugLogs = false;

    [SerializeField, Tooltip("화면 좌표에서 추가로 더할 UI 오프셋입니다.")]
    private Vector2 screenOffset = Vector2.zero;

    [SerializeField, Tooltip("캐릭터를 따라가는 SmoothDamp 시간입니다. 0이면 즉시 이동합니다.")]
    private float followSmoothTime = 0.02f;

    [SerializeField, Tooltip("카메라 기준 오른쪽 방향으로 캐릭터 옆에 띄울 거리입니다.")]
    private float sideWorldOffset = 0.45f;

    [SerializeField, Tooltip("캐릭터 기준 위쪽으로 띄울 높이입니다.")]
    private float heightWorldOffset = 0.75f;

    [SerializeField, Tooltip("카메라 기준 앞/뒤 방향 보정값입니다. 보통 0으로 둡니다.")]
    private float depthWorldOffset = 0f;

    [SerializeField, Tooltip("화면 밖으로 나가지 않도록 위치를 제한할지 여부입니다.")]
    private bool clampToScreen = true;

    [SerializeField, Tooltip("화면 가장자리에서 유지할 여백입니다.")]
    private Vector2 screenPadding = new Vector2(32f, 32f);

    [SerializeField, Tooltip("로컬 플레이어 또는 스태미나 모듈을 찾지 못했을 때 다시 탐색하는 간격입니다.")]
    private float rebindInterval = 0.25f;

    [Header("Binding")]
    [SerializeField, Tooltip("활성화 시 로컬 플레이어를 자동으로 찾아 바인딩할지 여부입니다.")]
    private bool autoBindLocalPlayer = true;

    [Header("Prefab Attached Local Owner")]
    [SerializeField, Tooltip("프리팹 하위에 붙은 경우 부모 PlayerHub를 기준으로 바인딩합니다.")]
    private bool attachedUseParentPlayerHub = true;

    [SerializeField, Tooltip("local owner 플레이어일 때만 UI를 표시합니다.")]
    private bool attachedOwnerOnly = true;

    [SerializeField, Tooltip("프리팹 부착 모드에서는 위치 추적을 하지 않습니다.")]
    private bool attachedDisablePositionFollow = true;

    [SerializeField, Tooltip("프리팹 부착 모드에서는 회전/billboard 보정을 하지 않습니다.")]
    private bool attachedDisableRotationFollow = true;

    [SerializeField, Tooltip("프리팹에 저장된 localPosition/localRotation/localScale을 유지합니다.")]
    private bool attachedKeepLocalTransform = true;

    [SerializeField, Tooltip("non-owner 인스턴스에서는 UI를 숨깁니다.")]
    private bool attachedHideOnNonOwner = true;

    [SerializeField, Tooltip("프리팹 부착 모드 디버그 로그를 출력합니다.")]
    private bool attachedDebugLogs = false;

    [Header("Visibility")]
    [SerializeField, Tooltip("로컬 플레이어를 찾지 못했을 때 UI를 숨길지 여부입니다.")]
    private bool hideWhenNoLocalPlayer = true;

    [SerializeField, Tooltip("스태미나가 가득 찼을 때 UI를 숨길지 여부입니다.")]
    private bool hideWhenFull = false;

    [SerializeField, Tooltip("스태미나 변화 후 가득 찬 상태에서도 UI를 유지할 시간입니다.")]
    private float visibleAfterChangeSeconds = 1.0f;

    [SerializeField, Tooltip("이 값 이하의 스태미나 비율 또는 수치에서 낮은 스태미나 색상을 사용합니다. 1 이하이면 정규화 비율, 1보다 크면 실제 스태미나 수치 기준입니다.")]
    private float lowStaminaThreshold = 25f;

    [SerializeField, Tooltip("일반 상태에서 사용할 스태미나 fill 색상입니다.")]
    private Color normalFillColor = Color.white;

    [SerializeField, Tooltip("낮은 스태미나 상태에서 사용할 fill 색상입니다.")]
    private Color lowStaminaFillColor = Color.yellow;

    [Header("Debug")]
    [SerializeField, Tooltip("캐릭터 추적 스태미나 HUD 디버그 로그를 출력할지 여부입니다.")]
    private bool followStaminaDebugLogs = false;

    private PlayerHub _boundPlayerHub;
    private PlayerStaminaModule _boundStaminaModule;
    private Transform _targetTransform;
    private Camera _localPlayerCamera;
    private Vector2 _followVelocity;
    private Vector3 _worldFollowVelocity;
    private float _worldVerticalVelocity;
    private Vector3 _initialWorldAnchorLossyScale;
    private Vector3 _initialVisualLocalScale;
    private Quaternion _initialVisualWorldRotation;
    private Quaternion _capturedTargetVisualLocalRotation;
    private Vector3 _attachedRootLocalPosition;
    private Quaternion _attachedRootLocalRotation;
    private Vector3 _attachedRootLocalScale;
    private Vector3 _attachedFollowLocalPosition;
    private Quaternion _attachedFollowLocalRotation;
    private Vector3 _attachedFollowLocalScale;
    private float _nextBindAttemptTime;
    private float _visibleUntil;
    private float _lastRatio = -1f;
    private bool _hasSnappedToTarget;
    private bool _hasInitialWorldAnchorScale;
    private bool _hasInitialVisualScale;
    private bool _hasInitialVisualWorldRotation;
    private bool _hasCapturedTargetVisualLocalRotation;
    private bool _hasLoggedVisualLockApplied;
    private bool _hasLoggedCameraFacingFlipForward;
    private bool _hasLoggedTargetVisualFlipForward;
    private bool _hasAttachedRootLocalTransform;
    private bool _hasAttachedFollowLocalTransform;
    private bool _prefabAttachedHiddenForNonOwner;
    private bool _hasLoggedPrefabAttachedParentFound;
    private bool _hasLoggedPrefabAttachedHiddenNonOwner;
    private bool _hasLoggedPrefabAttachedOwnerBound;
    private bool _loggedWaitingForLocalPlayer;
    private bool _characterScreenTargetInView = true;
    private PresentationMode _lastPresentationMode;
    private VisualLockMode _lastLoggedVisualLockMode = VisualLockMode.None;

    private void Awake()
    {
        ResolveRefs();
        CaptureAttachedLocalTransforms();
        ApplyMissingLocalPlayerState();
    }

    private void OnEnable()
    {
        ResolveRefs();
        _nextBindAttemptTime = 0f;
        _hasSnappedToTarget = false;
        _worldFollowVelocity = Vector3.zero;
        _worldVerticalVelocity = 0f;
        _hasInitialWorldAnchorScale = false;
        _hasInitialVisualScale = false;
        _hasInitialVisualWorldRotation = false;
        _hasCapturedTargetVisualLocalRotation = false;
        _hasLoggedVisualLockApplied = false;
        _hasLoggedCameraFacingFlipForward = false;
        _hasLoggedTargetVisualFlipForward = false;
        _prefabAttachedHiddenForNonOwner = false;
        _hasLoggedPrefabAttachedParentFound = false;
        _hasLoggedPrefabAttachedHiddenNonOwner = false;
        _hasLoggedPrefabAttachedOwnerBound = false;
        _lastLoggedVisualLockMode = VisualLockMode.None;
        _characterScreenTargetInView = true;
        _lastPresentationMode = presentationMode;
        CaptureAttachedLocalTransforms();
        RestoreAttachedLocalTransforms();
        TryCaptureInitialVisualState(ResolveWorldAnchorTransform(), true);

        if (autoBindLocalPlayer)
            TryBindLocalPlayer();
        else
            RefreshStaminaUI(false);
    }

    private void OnDisable()
    {
        ClearBinding();
    }

    private void OnDestroy()
    {
        ClearBinding();
    }

    private void Update()
    {
        if (_boundStaminaModule != null && !IsBoundPlayerValid())
        {
            ClearBinding();
            ApplyMissingLocalPlayerState();
        }

        if (autoBindLocalPlayer && _boundStaminaModule == null && Time.unscaledTime >= _nextBindAttemptTime)
            TryBindLocalPlayer();

        RefreshVisibility();
    }

    private void LateUpdate()
    {
        if (IsPrefabAttachedMode())
        {
            if (_lastPresentationMode != presentationMode)
                ResetPresentationModeState();

            RestoreAttachedLocalTransforms();
            return;
        }

        UpdateFollowPosition();
    }

    public void ForceRebind()
    {
        ClearBinding();
        _nextBindAttemptTime = 0f;
        _loggedWaitingForLocalPlayer = false;
        TryBindLocalPlayer();
    }

    private void ResolveRefs()
    {
        if (root == null)
            root = GetComponent<RectTransform>();

        if (followRect == null)
            followRect = root;

        if (targetCanvas == null)
            targetCanvas = GetComponentInParent<Canvas>();

        if (canvasGroup == null && root != null)
            canvasGroup = root.GetComponent<CanvasGroup>();

        if (staminaFillImage == null)
            staminaFillImage = GetComponentInChildren<Image>(true);

        if (staminaSlider == null)
            staminaSlider = GetComponentInChildren<Slider>(true);

        if (staminaText == null)
            staminaText = GetComponentInChildren<TMP_Text>(true);
    }

    private bool IsPrefabAttachedMode()
    {
        return presentationMode == PresentationMode.PrefabAttachedLocalOwner;
    }

    private void CaptureAttachedLocalTransforms()
    {
        if (!attachedKeepLocalTransform)
            return;

        if (!_hasAttachedRootLocalTransform && root != null)
        {
            _attachedRootLocalPosition = root.localPosition;
            _attachedRootLocalRotation = root.localRotation;
            _attachedRootLocalScale = root.localScale;
            _hasAttachedRootLocalTransform = true;
        }

        if (!_hasAttachedFollowLocalTransform && followRect != null)
        {
            _attachedFollowLocalPosition = followRect.localPosition;
            _attachedFollowLocalRotation = followRect.localRotation;
            _attachedFollowLocalScale = followRect.localScale;
            _hasAttachedFollowLocalTransform = true;
        }
    }

    private void RestoreAttachedLocalTransforms()
    {
        if (!IsPrefabAttachedMode() || !attachedKeepLocalTransform)
            return;

        if (_hasAttachedRootLocalTransform && root != null)
        {
            root.localPosition = _attachedRootLocalPosition;
            root.localRotation = _attachedRootLocalRotation;
            root.localScale = _attachedRootLocalScale;
        }

        if (_hasAttachedFollowLocalTransform && followRect != null)
        {
            followRect.localPosition = _attachedFollowLocalPosition;
            followRect.localRotation = _attachedFollowLocalRotation;
            followRect.localScale = _attachedFollowLocalScale;
        }
    }

    private void TryBindLocalPlayer()
    {
        _nextBindAttemptTime = Time.unscaledTime + Mathf.Max(0f, rebindInterval);

        if (IsPrefabAttachedMode() && TryBindPrefabAttachedParentPlayer())
            return;

        _prefabAttachedHiddenForNonOwner = false;

        NetworkObject playerObject;
        PlayerHub playerHub = ResolveLocalPlayerHub(out playerObject);
        if (playerHub == null)
        {
            ApplyMissingLocalPlayerState();
            LogWaitingForLocalPlayer();
            return;
        }

        PlayerStaminaModule staminaModule = playerHub.GetComponentInChildren<PlayerStaminaModule>(true);
        if (staminaModule == null)
        {
            _boundPlayerHub = playerHub;
            _targetTransform = playerHub.transform != null ? playerHub.transform : playerObject.transform;
            _localPlayerCamera = ResolveLocalPlayerCamera(playerHub);
            TryCaptureInitialVisualState(ResolveWorldAnchorTransform(), true);
            TryCaptureTargetVisualLocalRotation(ResolveWorldAnchorTransform());
            ApplyMissingLocalPlayerState();
            Log("Waiting for local player stamina.");
            return;
        }

        _boundPlayerHub = playerHub;
        _targetTransform = playerHub.transform != null ? playerHub.transform : playerObject.transform;
        _localPlayerCamera = ResolveLocalPlayerCamera(playerHub);
        _loggedWaitingForLocalPlayer = false;
        _hasSnappedToTarget = false;
        TryCaptureInitialVisualState(ResolveWorldAnchorTransform(), true);
        TryCaptureTargetVisualLocalRotation(ResolveWorldAnchorTransform());

        BindStaminaModule(staminaModule);
        Log("Local player bound.");
        LogFollowMode();
    }

    private bool TryBindPrefabAttachedParentPlayer()
    {
        if (!attachedUseParentPlayerHub)
            return false;

        PlayerHub parentHub = GetComponentInParent<PlayerHub>(true);
        if (parentHub == null)
            return false;

        LogPrefabAttachedParentFound();

        if (ShouldRequirePrefabAttachedLocalOwner() && !IsPrefabAttachedLocalOwner(parentHub))
        {
            ApplyPrefabAttachedNonOwnerState();
            LogPrefabAttachedHiddenNonOwner();
            return true;
        }

        _prefabAttachedHiddenForNonOwner = false;

        PlayerStaminaModule staminaModule = parentHub.GetComponentInChildren<PlayerStaminaModule>(true);
        if (staminaModule == null)
        {
            _boundPlayerHub = parentHub;
            _targetTransform = parentHub.transform;
            _localPlayerCamera = ResolveLocalPlayerCamera(parentHub);
            ApplyMissingLocalPlayerState();
            LogPrefabAttached("Prefab attached waiting for owner stamina.");
            return true;
        }

        _boundPlayerHub = parentHub;
        _targetTransform = parentHub.transform;
        _localPlayerCamera = ResolveLocalPlayerCamera(parentHub);
        _loggedWaitingForLocalPlayer = false;
        _hasSnappedToTarget = false;

        BindStaminaModule(staminaModule);
        LogPrefabAttachedOwnerBound();
        LogFollowMode();
        return true;
    }

    private void ApplyPrefabAttachedNonOwnerState()
    {
        UnbindStaminaModule();
        _boundPlayerHub = null;
        _targetTransform = null;
        _localPlayerCamera = null;
        _hasSnappedToTarget = false;
        _followVelocity = Vector2.zero;
        _worldFollowVelocity = Vector3.zero;
        _worldVerticalVelocity = 0f;
        _prefabAttachedHiddenForNonOwner = true;
        _lastRatio = -1f;

        RefreshStaminaUI(false);
        SetVisible(false);
    }

    private bool ShouldRequirePrefabAttachedLocalOwner()
    {
        return attachedOwnerOnly || attachedHideOnNonOwner;
    }

    private bool IsPrefabAttachedLocalOwner(PlayerHub playerHub)
    {
        if (playerHub == null)
            return false;

        if (playerHub.IsOwner)
            return true;

        if (!playerHub.IsSpawned)
            return false;

        NetworkManager networkManager = NetworkManager.Singleton;
        return networkManager != null &&
               networkManager.IsListening &&
               playerHub.OwnerClientId == networkManager.LocalClientId;
    }

    private PlayerHub ResolveLocalPlayerHub(out NetworkObject playerObject)
    {
        playerObject = null;

        NetworkManager networkManager = NetworkManager.Singleton;
        if (networkManager == null || !networkManager.IsListening)
            return null;

        NetworkClient localClient = networkManager.LocalClient;
        if (localClient != null && localClient.PlayerObject != null)
        {
            playerObject = localClient.PlayerObject;
        }
        else if (networkManager.IsServer &&
                 networkManager.ConnectedClients != null &&
                 networkManager.ConnectedClients.TryGetValue(networkManager.LocalClientId, out NetworkClient connectedClient) &&
                 connectedClient != null)
        {
            playerObject = connectedClient.PlayerObject;
        }

        if (playerObject == null || !playerObject.IsSpawned)
            return null;

        if (playerObject.OwnerClientId != networkManager.LocalClientId)
            return null;

        PlayerHub playerHub = playerObject.GetComponentInChildren<PlayerHub>(true);
        return CanBindPlayerHub(playerHub) ? playerHub : null;
    }

    private void BindStaminaModule(PlayerStaminaModule staminaModule)
    {
        if (_boundStaminaModule == staminaModule)
        {
            RefreshStaminaUI(false);
            RefreshVisibility();
            return;
        }

        UnbindStaminaModule();

        _boundStaminaModule = staminaModule;
        _boundStaminaModule.StaminaChanged += OnStaminaChanged;
        _boundStaminaModule.MaxStaminaChanged += OnMaxStaminaChanged;

        RefreshStaminaUI(false);
        RefreshVisibility();
    }

    private void UnbindStaminaModule()
    {
        if (_boundStaminaModule != null)
        {
            _boundStaminaModule.StaminaChanged -= OnStaminaChanged;
            _boundStaminaModule.MaxStaminaChanged -= OnMaxStaminaChanged;
        }

        _boundStaminaModule = null;
    }

    private void ClearBinding()
    {
        UnbindStaminaModule();
        _boundPlayerHub = null;
        _targetTransform = null;
        _localPlayerCamera = null;
        _hasSnappedToTarget = false;
        _followVelocity = Vector2.zero;
        _worldFollowVelocity = Vector3.zero;
        _worldVerticalVelocity = 0f;
        _hasInitialWorldAnchorScale = false;
        _hasCapturedTargetVisualLocalRotation = false;
        _characterScreenTargetInView = true;
        _lastRatio = -1f;
    }

    private void OnStaminaChanged(float previousStamina, float currentStamina)
    {
        RefreshStaminaUI(true);
    }

    private void OnMaxStaminaChanged(float previousMaxStamina, float currentMaxStamina)
    {
        RefreshStaminaUI(true);
    }

    private void RefreshStaminaUI(bool markChanged)
    {
        float currentStamina = _boundStaminaModule != null ? _boundStaminaModule.CurrentStamina : 0f;
        float maxStamina = _boundStaminaModule != null ? _boundStaminaModule.MaxStamina : 0f;
        float ratio = CalculateStaminaRatio(currentStamina, maxStamina);

        if (markChanged)
            _visibleUntil = Time.unscaledTime + Mathf.Max(0f, visibleAfterChangeSeconds);

        if (staminaFillImage != null)
        {
            staminaFillImage.fillAmount = ratio;
            staminaFillImage.color = IsLowStamina(currentStamina, ratio) ? lowStaminaFillColor : normalFillColor;
        }

        if (staminaSlider != null)
            staminaSlider.value = ratio;

        if (staminaText != null)
            staminaText.text = $"{Mathf.RoundToInt(currentStamina)} / {Mathf.RoundToInt(maxStamina)}";

        _lastRatio = ratio;
    }

    private float CalculateStaminaRatio(float currentStamina, float maxStamina)
    {
        if (maxStamina <= 0f)
            return 0f;

        return Mathf.Clamp01(currentStamina / maxStamina);
    }

    private bool IsLowStamina(float currentStamina, float ratio)
    {
        float threshold = Mathf.Max(0f, lowStaminaThreshold);
        if (threshold <= 1f)
            return ratio <= threshold;

        return currentStamina <= threshold;
    }

    private void UpdateFollowPosition()
    {
        if (_lastPresentationMode != presentationMode)
            ResetPresentationModeState();

        if (presentationMode == PresentationMode.WorldSpaceAnchor)
        {
            UpdateWorldSpaceAnchorPosition();
            return;
        }

        if (presentationMode == PresentationMode.StabilizedWorldAnchor)
        {
            if (enableWorldAnchorStabilization)
                UpdateStabilizedWorldAnchorPosition();
            else
                UpdateWorldSpaceAnchorPosition();

            return;
        }

        if (presentationMode == PresentationMode.ScreenLockedWorldAnchor)
        {
            UpdateScreenLockedWorldAnchorPosition();
            return;
        }

        if (presentationMode == PresentationMode.CharacterScreenAnchor)
        {
            UpdateCharacterScreenAnchorPosition();
            return;
        }

        UpdateScreenSpaceFollowPosition();
    }

    private void ResetPresentationModeState()
    {
        _followVelocity = Vector2.zero;
        _worldFollowVelocity = Vector3.zero;
        _worldVerticalVelocity = 0f;
        _hasSnappedToTarget = false;
        _hasInitialWorldAnchorScale = false;
        _hasLoggedVisualLockApplied = false;
        _hasLoggedCameraFacingFlipForward = false;
        _hasLoggedTargetVisualFlipForward = false;
        _lastLoggedVisualLockMode = VisualLockMode.None;
        SetCharacterScreenTargetInView(true);
        _lastPresentationMode = presentationMode;
    }

    private void UpdateWorldSpaceAnchorPosition()
    {
        Transform anchorTransform = ResolveWorldAnchorTransform();
        if (_targetTransform == null || anchorTransform == null)
            return;

        CacheWorldAnchorScale(anchorTransform);

        Vector3 targetPosition = GetWorldAnchorTargetPosition();
        float snapDistance = Mathf.Max(0f, maxWorldSnapDistance);
        float currentDistance = Vector3.Distance(anchorTransform.position, targetPosition);

        string snapReason = null;
        if (!_hasSnappedToTarget)
            snapReason = "first-bind";
        else if (currentDistance > snapDistance)
            snapReason = "max-distance";

        if (snapReason != null || worldFollowSmoothTime <= 0f)
        {
            anchorTransform.position = targetPosition;
            _worldFollowVelocity = Vector3.zero;
            _hasSnappedToTarget = true;

            if (snapReason != null)
                LogWorldPositionSnapped(snapReason);
        }
        else
        {
            float smoothTime = Mathf.Max(0.001f, worldFollowSmoothTime);
            float deltaTime = GetWorldAnchorDeltaTime();
            anchorTransform.position = Vector3.SmoothDamp(
                anchorTransform.position,
                targetPosition,
                ref _worldFollowVelocity,
                smoothTime,
                Mathf.Infinity,
                deltaTime);

            _hasSnappedToTarget = true;
        }

        Camera camera = ResolveWorldCamera();
        ApplyVisualLock(anchorTransform, camera);

        if (!ShouldSuppressLegacyBillboard())
            ApplyBillboard(anchorTransform, false, false);

        ApplyWorldAnchorScale(anchorTransform);
        ApplyVisualScale(anchorTransform);
    }

    private void UpdateStabilizedWorldAnchorPosition()
    {
        Transform anchorTransform = ResolveWorldAnchorTransform();
        if (_targetTransform == null || anchorTransform == null)
            return;

        CacheWorldAnchorScale(anchorTransform);

        Camera camera = ResolveWorldCamera();
        Vector3 targetPosition = GetStabilizedWorldAnchorTargetPosition(camera);
        Vector3 currentPosition = anchorTransform.position;
        float currentDistance = Vector3.Distance(currentPosition, targetPosition);
        float snapDistance = Mathf.Max(0f, worldAnchorSnapDistance);
        float deadzone = Mathf.Max(0f, worldAnchorDeadzone);

        string snapReason = null;
        if (!_hasSnappedToTarget)
            snapReason = "first-bind";
        else if (currentDistance > snapDistance)
            snapReason = "distance";

        bool snappedPosition = false;
        if (snapReason != null)
        {
            anchorTransform.position = targetPosition;
            ResetWorldAnchorVelocity();
            _hasSnappedToTarget = true;
            snappedPosition = true;
            LogStabilizedAnchorSnapped(snapReason);
        }
        else if (currentDistance > deadzone)
        {
            if (stabilizedWorldFollowSmoothTime <= 0f)
            {
                anchorTransform.position = targetPosition;
                ResetWorldAnchorVelocity();
            }
            else
            {
                anchorTransform.position = SmoothStabilizedWorldPosition(currentPosition, targetPosition);
            }

            _hasSnappedToTarget = true;
        }

        ApplyVisualLock(anchorTransform, camera);

        if (!ShouldSuppressLegacyBillboard())
            ApplyBillboard(anchorTransform, true, !snappedPosition);

        ApplyWorldAnchorScale(anchorTransform);
        ApplyVisualScale(anchorTransform);
    }

    private void UpdateScreenLockedWorldAnchorPosition()
    {
        Transform anchorTransform = ResolveWorldAnchorTransform();
        if (_targetTransform == null || anchorTransform == null)
            return;

        Camera camera = ResolveWorldCamera();
        if (camera == null)
        {
            UpdateWorldSpaceAnchorPosition();
            return;
        }

        CacheWorldAnchorScale(anchorTransform);

        Vector3 targetPosition = GetScreenLockedWorldAnchorTargetPosition(camera);
        float currentDistance = Vector3.Distance(anchorTransform.position, targetPosition);
        float snapDistance = Mathf.Max(0f, screenLockedSnapDistance);

        string snapReason = null;
        if (!_hasSnappedToTarget)
            snapReason = "first-bind";
        else if (currentDistance > snapDistance)
            snapReason = "distance";

        bool snappedPosition = false;
        if (snapReason != null || screenLockedSmoothTime <= 0f)
        {
            anchorTransform.position = targetPosition;
            ResetWorldAnchorVelocity();
            _hasSnappedToTarget = true;
            snappedPosition = snapReason != null;

            if (snapReason != null)
                LogScreenLockedAnchorSnapped(snapReason);
        }
        else
        {
            float smoothTime = Mathf.Max(0.001f, screenLockedSmoothTime);
            anchorTransform.position = Vector3.SmoothDamp(
                anchorTransform.position,
                targetPosition,
                ref _worldFollowVelocity,
                smoothTime,
                Mathf.Infinity,
                GetWorldAnchorDeltaTime());

            _hasSnappedToTarget = true;
        }

        ApplyVisualLock(anchorTransform, camera);

        if (!ShouldSuppressLegacyBillboard())
        {
            if (screenLockedUseCameraRotation)
                ApplyScreenLockedRotation(anchorTransform, camera, !snappedPosition);
            else
                ApplyBillboard(anchorTransform, true, !snappedPosition);
        }

        ApplyWorldAnchorScale(anchorTransform);
        ApplyVisualScale(anchorTransform);
    }

    private void UpdateScreenSpaceFollowPosition()
    {
        RectTransform targetRect = followRect != null ? followRect : root;
        if (_targetTransform == null || targetRect == null)
            return;

        RectTransform parentRect = targetRect.parent as RectTransform;
        if (parentRect == null)
            return;

        Camera worldCamera = ResolveWorldCamera();
        Vector3 worldPosition = GetFollowWorldPosition(worldCamera);
        Vector2 screenPosition = RectTransformUtility.WorldToScreenPoint(worldCamera, worldPosition) + screenOffset;

        if (clampToScreen)
            screenPosition = ClampScreenPosition(screenPosition);

        Camera uiCamera = ResolveUiCamera();
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(parentRect, screenPosition, uiCamera, out Vector2 localPosition))
            return;

        if (!_hasSnappedToTarget || followSmoothTime <= 0f)
        {
            targetRect.anchoredPosition = localPosition;
            _followVelocity = Vector2.zero;
            _hasSnappedToTarget = true;
            return;
        }

        float smoothTime = Mathf.Max(0.001f, followSmoothTime);
        targetRect.anchoredPosition = Vector2.SmoothDamp(
            targetRect.anchoredPosition,
            localPosition,
            ref _followVelocity,
            smoothTime,
            Mathf.Infinity,
            Time.unscaledDeltaTime);
    }

    private void UpdateCharacterScreenAnchorPosition()
    {
        RectTransform targetRect = followRect != null ? followRect : root;
        if (_targetTransform == null || targetRect == null)
            return;

        ResetCharacterScreenRotation(targetRect);

        RectTransform parentRect = ResolveCharacterScreenParentRect(targetRect);
        if (parentRect == null)
            return;

        Camera camera = ResolveWorldCamera();
        if (camera == null)
            return;

        Vector3 baseWorldPosition = _targetTransform.position + characterScreenWorldBaseOffset;
        Vector3 screenPoint = camera.WorldToScreenPoint(baseWorldPosition);
        if (screenPoint.z <= 0.001f)
        {
            _hasSnappedToTarget = false;
            _followVelocity = Vector2.zero;
            SetCharacterScreenTargetInView(false);
            return;
        }

        SetCharacterScreenTargetInView(true);

        Vector2 screenPosition = new Vector2(
            screenPoint.x + characterScreenOffset.x,
            screenPoint.y + characterScreenOffset.y);

        Camera uiCamera = ResolveUiCamera();
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(parentRect, screenPosition, uiCamera, out Vector2 localPosition))
            return;

        if (characterScreenClampToParent)
            localPosition = ClampLocalPositionToParent(parentRect, localPosition, characterScreenPadding);

        Vector3 targetWorldPosition = parentRect.TransformPoint(new Vector3(localPosition.x, localPosition.y, 0f));
        Vector2 targetScreenPosition = RectTransformUtility.WorldToScreenPoint(uiCamera, targetWorldPosition);
        Vector2 currentScreenPosition = RectTransformUtility.WorldToScreenPoint(uiCamera, targetRect.position);
        float screenDistance = Vector2.Distance(currentScreenPosition, targetScreenPosition);
        float snapDistance = Mathf.Max(0f, characterScreenSnapDistance);
        float deadzone = Mathf.Max(0f, characterScreenDeadzone);

        string snapReason = null;
        if (!_hasSnappedToTarget)
            snapReason = "first-bind";
        else if (screenDistance > snapDistance)
            snapReason = "distance";

        if (snapReason != null)
        {
            targetRect.anchoredPosition = localPosition;
            _followVelocity = Vector2.zero;
            _hasSnappedToTarget = true;
            LogCharacterScreenAnchorSnapped(snapReason);
            return;
        }

        if (screenDistance <= deadzone)
            return;

        if (characterScreenSmoothTime <= 0f)
        {
            targetRect.anchoredPosition = localPosition;
            _followVelocity = Vector2.zero;
            _hasSnappedToTarget = true;
            return;
        }

        float smoothTime = Mathf.Max(0.001f, characterScreenSmoothTime);
        targetRect.anchoredPosition = Vector2.SmoothDamp(
            targetRect.anchoredPosition,
            localPosition,
            ref _followVelocity,
            smoothTime,
            Mathf.Infinity,
            Time.unscaledDeltaTime);

        _hasSnappedToTarget = true;
    }

    private RectTransform ResolveCharacterScreenParentRect(RectTransform targetRect)
    {
        if (targetRect != null && targetRect.parent is RectTransform targetParentRect)
            return targetParentRect;

        if (targetCanvas != null && targetCanvas.transform is RectTransform canvasRect)
            return canvasRect;

        if (root != null && root.parent is RectTransform rootParentRect)
            return rootParentRect;

        return null;
    }

    private Vector2 ClampLocalPositionToParent(RectTransform parentRect, Vector2 localPosition, Vector2 padding)
    {
        if (parentRect == null)
            return localPosition;

        Rect rect = parentRect.rect;
        float paddingX = Mathf.Max(0f, padding.x);
        float paddingY = Mathf.Max(0f, padding.y);

        float minX = rect.xMin + paddingX;
        float maxX = rect.xMax - paddingX;
        float minY = rect.yMin + paddingY;
        float maxY = rect.yMax - paddingY;

        if (maxX > minX)
            localPosition.x = Mathf.Clamp(localPosition.x, minX, maxX);
        else
            localPosition.x = rect.center.x;

        if (maxY > minY)
            localPosition.y = Mathf.Clamp(localPosition.y, minY, maxY);
        else
            localPosition.y = rect.center.y;

        return localPosition;
    }

    private void ResetCharacterScreenRotation(RectTransform targetRect)
    {
        if (!characterScreenResetRotation)
            return;

        if (root != null)
            root.localRotation = Quaternion.identity;

        if (targetRect != null)
            targetRect.localRotation = Quaternion.identity;
    }

    private Transform ResolveWorldAnchorTransform()
    {
        if (root != null)
            return root.transform;

        if (followRect != null)
            return followRect.transform;

        return transform;
    }

    private Vector3 GetWorldAnchorTargetPosition()
    {
        if (_targetTransform == null)
            return Vector3.zero;

        if (useTargetLocalOffset)
            return _targetTransform.TransformPoint(targetLocalOffset);

        return _targetTransform.position + worldOffset;
    }

    private Vector3 GetStabilizedWorldAnchorTargetPosition(Camera camera)
    {
        if (_targetTransform == null)
            return Vector3.zero;

        if (useCameraRelativeStableSide && camera != null)
        {
            Transform cameraTransform = camera.transform;
            return _targetTransform.position
                + cameraTransform.right * stableSideWorldOffset
                + Vector3.up * stableHeightWorldOffset
                + cameraTransform.forward * stableDepthWorldOffset;
        }

        return _targetTransform.TransformPoint(targetLocalOffset);
    }

    private Vector3 GetScreenLockedWorldAnchorTargetPosition(Camera camera)
    {
        if (_targetTransform == null)
            return Vector3.zero;

        if (camera == null)
            return GetWorldAnchorTargetPosition();

        Vector3 baseWorldPosition = _targetTransform.position + screenLockedWorldBaseOffset;
        Vector3 screenPosition = camera.WorldToScreenPoint(baseWorldPosition);
        if (screenPosition.z <= 0.001f)
            return GetWorldAnchorTargetPosition();

        screenPosition.x += screenLockedOffset.x;
        screenPosition.y += screenLockedOffset.y;
        return camera.ScreenToWorldPoint(screenPosition);
    }

    private Vector3 SmoothStabilizedWorldPosition(Vector3 currentPosition, Vector3 targetPosition)
    {
        float deltaTime = GetWorldAnchorDeltaTime();
        float horizontalSmoothTime = Mathf.Max(0.001f, stabilizedWorldFollowSmoothTime);
        Vector3 nextPosition = Vector3.SmoothDamp(
            currentPosition,
            targetPosition,
            ref _worldFollowVelocity,
            horizontalSmoothTime,
            Mathf.Infinity,
            deltaTime);

        if (stabilizeVerticalMotion)
        {
            float verticalSmoothTime = Mathf.Max(0.001f, stabilizedVerticalSmoothTime);
            nextPosition.y = Mathf.SmoothDamp(
                currentPosition.y,
                targetPosition.y,
                ref _worldVerticalVelocity,
                verticalSmoothTime,
                Mathf.Infinity,
                deltaTime);
        }

        return nextPosition;
    }

    private void ResetWorldAnchorVelocity()
    {
        _worldFollowVelocity = Vector3.zero;
        _worldVerticalVelocity = 0f;
    }

    private float GetWorldAnchorDeltaTime()
    {
        return useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
    }

    private void ApplyBillboard(Transform anchorTransform, bool stabilized, bool allowRotationSmoothing)
    {
        if (!billboardToCamera || anchorTransform == null)
            return;

        Camera camera = ResolveWorldCamera();
        if (camera == null)
            return;

        if (!TryGetBillboardRotation(anchorTransform, camera, stabilized && billboardYawOnly, out Quaternion targetRotation))
            return;

        if (!stabilized || !allowRotationSmoothing || billboardRotationSmoothTime <= 0f)
        {
            anchorTransform.rotation = targetRotation;
            return;
        }

        float deltaTime = GetWorldAnchorDeltaTime();
        float rotationT = GetSmoothingFactor(deltaTime, billboardRotationSmoothTime);
        anchorTransform.rotation = Quaternion.Slerp(anchorTransform.rotation, targetRotation, rotationT);
    }

    private bool TryGetBillboardRotation(Transform anchorTransform, Camera camera, bool yawOnly, out Quaternion rotation)
    {
        rotation = Quaternion.identity;

        if (anchorTransform == null || camera == null)
            return false;

        Vector3 cameraToAnchor = anchorTransform.position - camera.transform.position;
        if (yawOnly)
            cameraToAnchor.y = 0f;

        if (cameraToAnchor.sqrMagnitude <= 0.000001f)
        {
            cameraToAnchor = camera.transform.forward;
            if (yawOnly)
                cameraToAnchor.y = 0f;
        }

        if (cameraToAnchor.sqrMagnitude <= 0.000001f)
            return false;

        rotation = Quaternion.LookRotation(cameraToAnchor.normalized, Vector3.up);
        return true;
    }

    private float GetSmoothingFactor(float deltaTime, float smoothTime)
    {
        if (smoothTime <= 0f)
            return 1f;

        return 1f - Mathf.Exp(-Mathf.Max(0f, deltaTime) / smoothTime);
    }

    private void ApplyScreenLockedRotation(Transform anchorTransform, Camera camera, bool allowRotationSmoothing)
    {
        if (!billboardToCamera || !screenLockedUseCameraRotation || anchorTransform == null || camera == null)
            return;

        Quaternion targetRotation = GetScreenLockedRotation(anchorTransform, camera);
        if (!allowRotationSmoothing || screenLockedRotationSmoothTime <= 0f)
        {
            anchorTransform.rotation = targetRotation;
            return;
        }

        float rotationT = GetSmoothingFactor(GetWorldAnchorDeltaTime(), screenLockedRotationSmoothTime);
        anchorTransform.rotation = Quaternion.Slerp(anchorTransform.rotation, targetRotation, rotationT);
    }

    private Quaternion GetScreenLockedRotation(Transform anchorTransform, Camera camera)
    {
        if (camera == null)
            return Quaternion.identity;

        if (!screenLockedKeepUpright)
            return camera.transform.rotation;

        Vector3 forward = camera.transform.forward;
        if (forward.sqrMagnitude <= 0.000001f && anchorTransform != null)
            forward = anchorTransform.position - camera.transform.position;

        if (forward.sqrMagnitude <= 0.000001f)
            forward = Vector3.forward;

        Vector3 normalizedForward = forward.normalized;
        Vector3 up = Vector3.up;
        if (Mathf.Abs(Vector3.Dot(normalizedForward, up)) > 0.98f)
            up = camera.transform.up.sqrMagnitude > 0.000001f ? camera.transform.up : Vector3.up;

        return Quaternion.LookRotation(normalizedForward, up);
    }

    private void TryCaptureInitialVisualState(Transform anchorTransform, bool respectOnEnableFlags)
    {
        if (anchorTransform == null)
            return;

        if (!respectOnEnableFlags || captureInitialVisualScaleOnEnable)
            TryCaptureInitialVisualScale(anchorTransform);

        if (!respectOnEnableFlags || captureInitialWorldRotationOnEnable)
            TryCaptureInitialVisualWorldRotation(anchorTransform);
    }

    private void TryCaptureInitialVisualScale(Transform anchorTransform)
    {
        if (!ShouldKeepVisualScale() || _hasInitialVisualScale || anchorTransform == null)
            return;

        _initialVisualLocalScale = anchorTransform.localScale;
        _hasInitialVisualScale = true;
        LogVisualLock("Captured visual scale.");
    }

    private void TryCaptureInitialVisualWorldRotation(Transform anchorTransform)
    {
        if (_hasInitialVisualWorldRotation || anchorTransform == null)
            return;

        _initialVisualWorldRotation = anchorTransform.rotation;
        _hasInitialVisualWorldRotation = true;
        LogVisualLock("Captured world rotation.");
    }

    private bool ApplyVisualLock(Transform anchorTransform, Camera camera)
    {
        if (anchorTransform == null || !ShouldApplyVisualLock())
            return false;

        if (visualLockMode == VisualLockMode.LockedInitialWorldRotation)
        {
            Quaternion targetRotation = GetLockedInitialWorldRotation(anchorTransform);
            ApplyVisualRotation(anchorTransform, targetRotation);
            ResetFollowRectVisualLocalRotation(anchorTransform);
            LogVisualLockApplied();
            return true;
        }

        if (visualLockMode == VisualLockMode.ScreenFacingUpright)
        {
            if (!TryGetScreenFacingVisualRotation(anchorTransform, camera, out Quaternion targetRotation))
                return false;

            ApplyVisualRotation(anchorTransform, targetRotation);
            ResetFollowRectVisualLocalRotation(anchorTransform);
            LogVisualLockApplied();
            return true;
        }

        if (visualLockMode == VisualLockMode.CameraFacingFixedScale)
        {
            if (!TryGetCameraFacingFixedScaleRotation(anchorTransform, camera, out Quaternion targetRotation))
                return false;

            ApplyVisualRotation(anchorTransform, targetRotation);
            ResetFollowRectVisualLocalRotation(anchorTransform);
            LogVisualLockApplied();
            LogCameraFacingFlipForward();
            return true;
        }

        if (visualLockMode == VisualLockMode.TargetYawLocalRotation)
        {
            if (!TryGetTargetYawLocalRotation(anchorTransform, out Quaternion targetRotation))
                return false;

            ApplyTargetVisualRotation(anchorTransform, targetRotation);
            ResetTargetVisualFollowRectLocalRotation(anchorTransform);
            LogVisualLockApplied();
            LogTargetVisualFlipForward();
            return true;
        }

        return false;
    }

    private Quaternion GetLockedInitialWorldRotation(Transform anchorTransform)
    {
        if (captureInitialWorldRotationOnEnable)
        {
            TryCaptureInitialVisualWorldRotation(anchorTransform);
            return _hasInitialVisualWorldRotation ? _initialVisualWorldRotation : anchorTransform.rotation;
        }

        return Quaternion.Euler(lockedWorldRotationEuler);
    }

    private bool TryGetScreenFacingVisualRotation(Transform anchorTransform, Camera camera, out Quaternion rotation)
    {
        rotation = Quaternion.identity;
        if (camera == null)
            return false;

        if (screenFacingUseCameraRotation && !screenFacingUprightOnly)
        {
            rotation = camera.transform.rotation;
        }
        else
        {
            Vector3 forward = screenFacingUseCameraRotation
                ? camera.transform.forward
                : anchorTransform.position - camera.transform.position;

            if (forward.sqrMagnitude <= 0.000001f)
                forward = camera.transform.forward;

            if (forward.sqrMagnitude <= 0.000001f)
                return false;

            Vector3 normalizedForward = forward.normalized;
            Vector3 up = screenFacingUprightOnly ? Vector3.up : camera.transform.up;
            if (up.sqrMagnitude <= 0.000001f)
                up = Vector3.up;

            if (screenFacingUprightOnly && Mathf.Abs(Vector3.Dot(normalizedForward, up.normalized)) > 0.98f)
                up = camera.transform.up.sqrMagnitude > 0.000001f ? camera.transform.up : Vector3.up;

            rotation = Quaternion.LookRotation(normalizedForward, up.normalized);
        }

        if (screenFacingFlipForward)
            rotation *= Quaternion.Euler(0f, 180f, 0f);

        return true;
    }

    private bool TryGetCameraFacingFixedScaleRotation(Transform anchorTransform, Camera camera, out Quaternion rotation)
    {
        rotation = Quaternion.identity;
        if (anchorTransform == null || camera == null)
            return false;

        Vector3 toCamera = camera.transform.position - anchorTransform.position;
        if (toCamera.sqrMagnitude <= 0.000001f)
            return false;

        Vector3 forward = -toCamera.normalized;
        Vector3 up = Vector3.up;
        if (Mathf.Abs(Vector3.Dot(forward, up)) > 0.98f)
            up = camera.transform.up.sqrMagnitude > 0.000001f ? camera.transform.up : Vector3.up;

        rotation = Quaternion.LookRotation(forward, up.normalized);

        if (screenFacingFlipForward)
            rotation *= Quaternion.Euler(0f, 180f, 0f);

        return true;
    }

    private bool TryGetTargetYawLocalRotation(Transform anchorTransform, out Quaternion rotation)
    {
        rotation = Quaternion.identity;
        if (anchorTransform == null || _targetTransform == null)
            return false;

        Quaternion targetBaseRotation = GetTargetVisualBaseRotation();
        Quaternion localCorrection = GetTargetVisualLocalCorrection(anchorTransform, targetBaseRotation);
        rotation = targetBaseRotation * localCorrection;
        return true;
    }

    private Quaternion GetTargetVisualBaseRotation()
    {
        if (_targetTransform == null)
            return Quaternion.identity;

        if (targetVisualYawOnly)
            return Quaternion.Euler(0f, _targetTransform.eulerAngles.y, 0f);

        return _targetTransform.rotation;
    }

    private Quaternion GetTargetVisualLocalCorrection(Transform anchorTransform, Quaternion targetBaseRotation)
    {
        Quaternion localCorrection = Quaternion.Euler(targetVisualLocalEuler);
        if (captureTargetVisualLocalRotationOnBind)
        {
            TryCaptureTargetVisualLocalRotation(anchorTransform, targetBaseRotation);
            if (_hasCapturedTargetVisualLocalRotation)
                localCorrection = _capturedTargetVisualLocalRotation;
        }

        if (targetVisualFlipForward)
            localCorrection *= Quaternion.Euler(0f, 180f, 0f);

        return localCorrection;
    }

    private void TryCaptureTargetVisualLocalRotation(Transform anchorTransform)
    {
        if (anchorTransform == null || _targetTransform == null)
            return;

        TryCaptureTargetVisualLocalRotation(anchorTransform, GetTargetVisualBaseRotation());
    }

    private void TryCaptureTargetVisualLocalRotation(Transform anchorTransform, Quaternion targetBaseRotation)
    {
        if (!captureTargetVisualLocalRotationOnBind ||
            _hasCapturedTargetVisualLocalRotation ||
            anchorTransform == null ||
            _targetTransform == null)
        {
            return;
        }

        _capturedTargetVisualLocalRotation = Quaternion.Inverse(targetBaseRotation) * anchorTransform.rotation;
        _hasCapturedTargetVisualLocalRotation = true;
        LogVisualLock("Captured target local visual rotation.");
    }

    private void ApplyTargetVisualRotation(Transform anchorTransform, Quaternion targetRotation)
    {
        if (targetVisualRotationSmoothTime <= 0f)
        {
            anchorTransform.rotation = targetRotation;
            return;
        }

        float rotationT = GetSmoothingFactor(GetWorldAnchorDeltaTime(), targetVisualRotationSmoothTime);
        anchorTransform.rotation = Quaternion.Slerp(anchorTransform.rotation, targetRotation, rotationT);
    }

    private void ResetTargetVisualFollowRectLocalRotation(Transform anchorTransform)
    {
        if (!targetVisualResetFollowRectLocalRotation || !IsTargetVisualWorldMode())
            return;

        if (followRect == null || followRect.transform == anchorTransform)
            return;

        followRect.localRotation = Quaternion.identity;
    }

    private void ApplyVisualRotation(Transform anchorTransform, Quaternion targetRotation)
    {
        if (visualRotationSmoothTime <= 0f)
        {
            anchorTransform.rotation = targetRotation;
            return;
        }

        float rotationT = GetSmoothingFactor(GetWorldAnchorDeltaTime(), visualRotationSmoothTime);
        anchorTransform.rotation = Quaternion.Slerp(anchorTransform.rotation, targetRotation, rotationT);
    }

    private void ResetFollowRectVisualLocalRotation(Transform anchorTransform)
    {
        if (!resetFollowRectLocalRotation || !IsWorldSpaceVisualMode())
            return;

        if (followRect == null || followRect.transform == anchorTransform)
            return;

        followRect.localRotation = Quaternion.identity;
    }

    private bool ShouldApplyVisualLock()
    {
        if (visualLockMode == VisualLockMode.None)
            return false;

        if (visualLockMode == VisualLockMode.TargetYawLocalRotation)
            return IsTargetVisualWorldMode();

        if (visualLockMode == VisualLockMode.CameraFacingFixedScale)
            return IsWorldSpaceVisualMode();

        if (!applyVisualLockInWorldModesOnly)
            return true;

        return IsWorldSpaceVisualMode();
    }

    private bool ShouldSuppressLegacyBillboard()
    {
        if (visualLockMode == VisualLockMode.TargetYawLocalRotation)
            return IsTargetVisualWorldMode();

        return disableLegacyBillboardWhenVisualLocked && ShouldApplyVisualLock();
    }

    private bool IsWorldSpaceVisualMode()
    {
        if (presentationMode == PresentationMode.WorldSpaceAnchor ||
            presentationMode == PresentationMode.StabilizedWorldAnchor)
        {
            return true;
        }

        if (presentationMode == PresentationMode.ScreenLockedWorldAnchor)
            return targetCanvas != null && targetCanvas.renderMode == RenderMode.WorldSpace;

        return false;
    }

    private bool IsTargetVisualWorldMode()
    {
        return presentationMode == PresentationMode.WorldSpaceAnchor ||
               presentationMode == PresentationMode.StabilizedWorldAnchor;
    }

    private void ApplyVisualScale(Transform anchorTransform)
    {
        if (!ShouldKeepVisualScale() || anchorTransform == null || !ShouldApplyVisualLock())
            return;

        if (!_hasInitialVisualScale)
            TryCaptureInitialVisualScale(anchorTransform);

        if (!_hasInitialVisualScale)
            return;

        anchorTransform.localScale = _initialVisualLocalScale;
    }

    private bool ShouldKeepVisualScale()
    {
        return keepVisualScale ||
               (visualLockMode == VisualLockMode.TargetYawLocalRotation && targetVisualKeepScale);
    }

    private void LogVisualLockApplied()
    {
        if (_hasLoggedVisualLockApplied && _lastLoggedVisualLockMode == visualLockMode)
            return;

        _hasLoggedVisualLockApplied = true;
        _lastLoggedVisualLockMode = visualLockMode;
        if (visualLockMode == VisualLockMode.TargetYawLocalRotation)
            LogVisualLock("TargetYawLocalRotation applied.");
        else if (visualLockMode == VisualLockMode.CameraFacingFixedScale)
            LogVisualLock("CameraFacingFixedScale applied.");
        else
            LogVisualLock($"Visual lock mode={visualLockMode} applied.");
    }

    private void LogCameraFacingFlipForward()
    {
        if (!screenFacingFlipForward || _hasLoggedCameraFacingFlipForward)
            return;

        _hasLoggedCameraFacingFlipForward = true;
        LogVisualLock("Camera facing flip forward enabled.");
    }

    private void LogTargetVisualFlipForward()
    {
        if (!targetVisualFlipForward || _hasLoggedTargetVisualFlipForward)
            return;

        _hasLoggedTargetVisualFlipForward = true;
        LogVisualLock("Target visual flip forward enabled.");
    }

    private void CacheWorldAnchorScale(Transform anchorTransform)
    {
        if (!keepWorldScale || _hasInitialWorldAnchorScale || anchorTransform == null)
            return;

        _initialWorldAnchorLossyScale = anchorTransform.lossyScale;
        _hasInitialWorldAnchorScale = true;
    }

    private void ApplyWorldAnchorScale(Transform anchorTransform)
    {
        if (!keepWorldScale || !_hasInitialWorldAnchorScale || anchorTransform == null)
            return;

        Transform parent = anchorTransform.parent;
        if (parent == null)
        {
            anchorTransform.localScale = _initialWorldAnchorLossyScale;
            return;
        }

        Vector3 parentScale = parent.lossyScale;
        anchorTransform.localScale = new Vector3(
            SafeDivide(_initialWorldAnchorLossyScale.x, parentScale.x),
            SafeDivide(_initialWorldAnchorLossyScale.y, parentScale.y),
            SafeDivide(_initialWorldAnchorLossyScale.z, parentScale.z));
    }

    private Vector3 GetFollowWorldPosition(Camera camera)
    {
        if (_targetTransform == null)
            return Vector3.zero;

        if (followOffsetMode == FollowOffsetMode.ScreenOffset)
            return _targetTransform.position + worldOffset;

        Vector3 right = camera != null ? camera.transform.right : Vector3.right;
        Vector3 up = Vector3.up;
        Vector3 forward = camera != null ? camera.transform.forward : Vector3.forward;

        return _targetTransform.position
            + right * sideWorldOffset
            + up * heightWorldOffset
            + forward * depthWorldOffset;
    }

    private Vector2 ClampScreenPosition(Vector2 screenPosition)
    {
        float paddingX = Mathf.Max(0f, screenPadding.x);
        float paddingY = Mathf.Max(0f, screenPadding.y);

        float minX = paddingX;
        float maxX = Screen.width - paddingX;
        float minY = paddingY;
        float maxY = Screen.height - paddingY;

        if (maxX > minX)
            screenPosition.x = Mathf.Clamp(screenPosition.x, minX, maxX);
        else
            screenPosition.x = Screen.width * 0.5f;

        if (maxY > minY)
            screenPosition.y = Mathf.Clamp(screenPosition.y, minY, maxY);
        else
            screenPosition.y = Screen.height * 0.5f;

        return screenPosition;
    }

    private Camera ResolveWorldCamera()
    {
        if (targetCanvas != null && targetCanvas.worldCamera != null)
            return targetCanvas.worldCamera;

        Camera mainCamera = Camera.main;
        if (mainCamera != null)
            return mainCamera;

        if (_localPlayerCamera != null)
            return _localPlayerCamera;

        if (_boundPlayerHub != null)
            _localPlayerCamera = ResolveLocalPlayerCamera(_boundPlayerHub);

        return _localPlayerCamera;
    }

    private Camera ResolveUiCamera()
    {
        if (targetCanvas == null || targetCanvas.renderMode == RenderMode.ScreenSpaceOverlay)
            return null;

        if (targetCanvas.worldCamera != null)
            return targetCanvas.worldCamera;

        return ResolveWorldCamera();
    }

    private Camera ResolveLocalPlayerCamera(PlayerHub playerHub)
    {
        if (playerHub == null)
            return null;

        Camera playerCamera = playerHub.GetComponentInChildren<Camera>(true);
        return playerCamera != null ? playerCamera : null;
    }

    private void SetCharacterScreenTargetInView(bool inView)
    {
        if (_characterScreenTargetInView == inView)
            return;

        _characterScreenTargetInView = inView;
        RefreshVisibility();
    }

    private void RefreshVisibility()
    {
        if (IsPrefabAttachedMode() && _prefabAttachedHiddenForNonOwner)
        {
            SetVisible(false);
            return;
        }

        if (_boundStaminaModule == null)
        {
            SetVisible(!hideWhenNoLocalPlayer);
            return;
        }

        if (presentationMode == PresentationMode.CharacterScreenAnchor && !_characterScreenTargetInView)
        {
            SetVisible(false);
            return;
        }

        SetVisible(ShouldShowBoundStamina());
    }

    private bool ShouldShowBoundStamina()
    {
        if (!hideWhenFull)
            return true;

        float currentStamina = _boundStaminaModule.CurrentStamina;
        float maxStamina = _boundStaminaModule.MaxStamina;
        float ratio = CalculateStaminaRatio(currentStamina, maxStamina);

        if (IsLowStamina(currentStamina, ratio))
            return true;

        if (ratio < 0.999f)
            return true;

        return Time.unscaledTime < _visibleUntil;
    }

    private void ApplyMissingLocalPlayerState()
    {
        RefreshStaminaUI(false);

        if (IsPrefabAttachedMode() && _prefabAttachedHiddenForNonOwner)
        {
            SetVisible(false);
            return;
        }

        SetVisible(!hideWhenNoLocalPlayer);
    }

    private void SetVisible(bool visible)
    {
        if (canvasGroup != null)
        {
            canvasGroup.alpha = visible ? 1f : 0f;
            canvasGroup.interactable = false;
            canvasGroup.blocksRaycasts = false;
            return;
        }

        if (TrySetObjectActive(root != null ? root.gameObject : null, visible))
            return;

        if (TrySetObjectActive(followRect != null ? followRect.gameObject : null, visible))
            return;

        if (staminaFillImage != null)
            staminaFillImage.enabled = visible;

        if (staminaSlider != null)
            staminaSlider.enabled = visible;

        if (staminaText != null)
            staminaText.enabled = visible;
    }

    private bool TrySetObjectActive(GameObject target, bool active)
    {
        if (target == null || target == gameObject)
            return false;

        if (transform.IsChildOf(target.transform))
            return false;

        target.SetActive(active);
        return true;
    }

    private bool CanBindPlayerHub(PlayerHub playerHub)
    {
        if (playerHub == null)
            return false;

        if (playerHub.IsSpawned && !playerHub.IsOwner)
            return false;

        return true;
    }

    private bool IsBoundPlayerValid()
    {
        if (_boundPlayerHub == null || _targetTransform == null)
            return false;

        if (IsPrefabAttachedMode() && ShouldRequirePrefabAttachedLocalOwner())
        {
            bool isLocalOwner = IsPrefabAttachedLocalOwner(_boundPlayerHub);
            _prefabAttachedHiddenForNonOwner = !isLocalOwner;
            return isLocalOwner;
        }

        if (!_boundPlayerHub.IsSpawned)
            return false;

        return _boundPlayerHub.IsOwner;
    }

    private void LogWaitingForLocalPlayer()
    {
        if (_loggedWaitingForLocalPlayer)
            return;

        _loggedWaitingForLocalPlayer = true;
        Log("Waiting for local player.");
    }

    private void LogFollowMode()
    {
        if (_targetTransform == null)
            return;

        if (presentationMode == PresentationMode.PrefabAttachedLocalOwner)
        {
            LogPrefabAttached($"Prefab attached mode active. positionFollowDisabled={attachedDisablePositionFollow} rotationFollowDisabled={attachedDisableRotationFollow}");
            return;
        }

        if (presentationMode == PresentationMode.CharacterScreenAnchor)
        {
            LogCharacterScreen("CharacterScreenAnchor active");
            return;
        }

        if (!followStaminaDebugLogs)
            return;

        if (presentationMode == PresentationMode.WorldSpaceAnchor)
        {
            Vector3 worldAnchorPosition = GetWorldAnchorTargetPosition();
            Log($"WorldSpaceAnchor bound. useTargetLocalOffset={useTargetLocalOffset} anchor={worldAnchorPosition}");
            return;
        }

        if (presentationMode == PresentationMode.StabilizedWorldAnchor)
        {
            Camera camera = ResolveWorldCamera();
            Vector3 stabilizedAnchorPosition = enableWorldAnchorStabilization
                ? GetStabilizedWorldAnchorTargetPosition(camera)
                : GetWorldAnchorTargetPosition();
            Log($"Stabilized anchor mode active. stabilization={enableWorldAnchorStabilization} anchor={stabilizedAnchorPosition}");
            return;
        }

        if (presentationMode == PresentationMode.ScreenLockedWorldAnchor)
        {
            Camera camera = ResolveWorldCamera();
            Vector3 screenLockedAnchorPosition = GetScreenLockedWorldAnchorTargetPosition(camera);
            Log($"ScreenLockedWorldAnchor mode active. screenOffset={screenLockedOffset} anchor={screenLockedAnchorPosition}");
            return;
        }

        Vector3 screenAnchorPosition = GetFollowWorldPosition(ResolveWorldCamera());
        Log($"ScreenSpace follow mode {followOffsetMode} side={sideWorldOffset:0.###} height={heightWorldOffset:0.###} depth={depthWorldOffset:0.###} anchor={screenAnchorPosition}");
    }

    private void LogWorldPositionSnapped(string reason)
    {
        Log($"World position snapped reason={reason}");
    }

    private void LogStabilizedAnchorSnapped(string reason)
    {
        Log($"Stabilized anchor snap reason={reason}");
    }

    private void LogScreenLockedAnchorSnapped(string reason)
    {
        Log($"Screen locked snap reason={reason}");
    }

    private void LogCharacterScreenAnchorSnapped(string reason)
    {
        LogCharacterScreen($"Character screen snap reason={reason}");
    }

    private void LogCharacterScreen(string message)
    {
        if (!characterScreenDebugLogs && !followStaminaDebugLogs)
            return;

        Debug.Log($"[CharacterFollowStaminaHUD] {message}", this);
    }

    private void LogPrefabAttachedParentFound()
    {
        if (_hasLoggedPrefabAttachedParentFound)
            return;

        _hasLoggedPrefabAttachedParentFound = true;
        LogPrefabAttached("Prefab attached parent player found.");
    }

    private void LogPrefabAttachedHiddenNonOwner()
    {
        if (_hasLoggedPrefabAttachedHiddenNonOwner)
            return;

        _hasLoggedPrefabAttachedHiddenNonOwner = true;
        LogPrefabAttached("Prefab attached hidden: non-owner.");
    }

    private void LogPrefabAttachedOwnerBound()
    {
        if (_hasLoggedPrefabAttachedOwnerBound)
            return;

        _hasLoggedPrefabAttachedOwnerBound = true;
        LogPrefabAttached("Prefab attached owner bound.");
    }

    private void LogPrefabAttached(string message)
    {
        if (!attachedDebugLogs && !followStaminaDebugLogs)
            return;

        Debug.Log($"[CharacterFollowStaminaHUD] {message}", this);
    }

    private void Log(string message)
    {
        if (!followStaminaDebugLogs)
            return;

        Debug.Log($"[CharacterFollowStaminaHUD] {message}", this);
    }

    private void LogVisualLock(string message)
    {
        if (!visualLockDebugLogs && !followStaminaDebugLogs)
            return;

        Debug.Log($"[CharacterFollowStaminaHUD] {message}", this);
    }

    private void OnValidate()
    {
        followSmoothTime = Mathf.Max(0f, followSmoothTime);
        worldFollowSmoothTime = Mathf.Max(0f, worldFollowSmoothTime);
        maxWorldSnapDistance = Mathf.Max(0f, maxWorldSnapDistance);
        visualRotationSmoothTime = Mathf.Max(0f, visualRotationSmoothTime);
        targetVisualRotationSmoothTime = Mathf.Max(0f, targetVisualRotationSmoothTime);
        stabilizedWorldFollowSmoothTime = Mathf.Max(0f, stabilizedWorldFollowSmoothTime);
        stabilizedVerticalSmoothTime = Mathf.Max(0f, stabilizedVerticalSmoothTime);
        worldAnchorSnapDistance = Mathf.Max(0f, worldAnchorSnapDistance);
        worldAnchorDeadzone = Mathf.Max(0f, worldAnchorDeadzone);
        billboardRotationSmoothTime = Mathf.Max(0f, billboardRotationSmoothTime);
        screenLockedSmoothTime = Mathf.Max(0f, screenLockedSmoothTime);
        screenLockedSnapDistance = Mathf.Max(0f, screenLockedSnapDistance);
        screenLockedRotationSmoothTime = Mathf.Max(0f, screenLockedRotationSmoothTime);
        characterScreenSmoothTime = Mathf.Max(0f, characterScreenSmoothTime);
        characterScreenSnapDistance = Mathf.Max(0f, characterScreenSnapDistance);
        characterScreenDeadzone = Mathf.Max(0f, characterScreenDeadzone);
        worldOffset = GetFiniteVector3OrZero(worldOffset);
        targetLocalOffset = GetFiniteVector3OrZero(targetLocalOffset);
        lockedWorldRotationEuler = GetFiniteVector3OrZero(lockedWorldRotationEuler);
        targetVisualLocalEuler = GetFiniteVector3OrZero(targetVisualLocalEuler);
        screenLockedOffset = GetFiniteVector2OrZero(screenLockedOffset);
        screenLockedWorldBaseOffset = GetFiniteVector3OrZero(screenLockedWorldBaseOffset);
        characterScreenWorldBaseOffset = GetFiniteVector3OrZero(characterScreenWorldBaseOffset);
        characterScreenOffset = GetFiniteVector2OrZero(characterScreenOffset);
        stableSideWorldOffset = GetFiniteOrZero(stableSideWorldOffset);
        stableHeightWorldOffset = GetFiniteOrZero(stableHeightWorldOffset);
        stableDepthWorldOffset = GetFiniteOrZero(stableDepthWorldOffset);
        sideWorldOffset = GetFiniteOrZero(sideWorldOffset);
        heightWorldOffset = GetFiniteOrZero(heightWorldOffset);
        depthWorldOffset = GetFiniteOrZero(depthWorldOffset);
        screenPadding = new Vector2(Mathf.Max(0f, screenPadding.x), Mathf.Max(0f, screenPadding.y));
        characterScreenPadding = new Vector2(Mathf.Max(0f, characterScreenPadding.x), Mathf.Max(0f, characterScreenPadding.y));
        rebindInterval = Mathf.Max(0f, rebindInterval);
        visibleAfterChangeSeconds = Mathf.Max(0f, visibleAfterChangeSeconds);
        lowStaminaThreshold = Mathf.Max(0f, lowStaminaThreshold);
    }

    private static float GetFiniteOrZero(float value)
    {
        return float.IsNaN(value) || float.IsInfinity(value) ? 0f : value;
    }

    private static Vector3 GetFiniteVector3OrZero(Vector3 value)
    {
        return new Vector3(
            GetFiniteOrZero(value.x),
            GetFiniteOrZero(value.y),
            GetFiniteOrZero(value.z));
    }

    private static Vector2 GetFiniteVector2OrZero(Vector2 value)
    {
        return new Vector2(
            GetFiniteOrZero(value.x),
            GetFiniteOrZero(value.y));
    }

    private static float SafeDivide(float value, float divisor)
    {
        return Mathf.Abs(divisor) <= 0.0001f ? value : value / divisor;
    }
}
