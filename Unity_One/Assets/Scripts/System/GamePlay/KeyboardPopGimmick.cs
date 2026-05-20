using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public class KeyboardPopGimmick : NetworkBehaviour
{
    private const string LogPrefix = "[KEYBOARD_POP]";

    [System.Serializable]
    private struct DeskLandingArea
    {
        [Tooltip("착지 영역의 이름입니다. 디버그와 구분용입니다.")]
        public string name;

        [Tooltip("착지 영역의 중심 월드 좌표입니다.")]
        public Vector3 center;

        [Tooltip("착지 영역의 크기입니다. X/Z는 가로/세로 범위, Y는 사용하지 않습니다.")]
        public Vector3 size;

        [Tooltip("이 영역이 랜덤 선택될 가중치입니다. 0 이하이면 선택되지 않습니다.")]
        public float weight;
    }

    [Header("Keys")]
    [Tooltip("튀어나올 키 오브젝트들입니다.")]
    [SerializeField] private Transform[] keyTransforms;

    [Tooltip("이전 단순 팝 연출에서 사용하던 로컬 위치 오프셋입니다. 현재 분출형 이동에서는 사용하지 않습니다.")]
    [SerializeField] private Vector3 popLocalOffset = new Vector3(0f, 0.35f, 0f);

    [Header("Timing")]
    [Tooltip("키가 튀어나오기 전 예고 연출 시간(초)입니다.")]
    [SerializeField] private float telegraphDuration = 0.5f;

    [Tooltip("키가 위로 튀어 오르는 높이입니다.")]
    [SerializeField] private float launchHeight = 1.5f;

    [Tooltip("키가 사방으로 흩어지는 수평 거리입니다.")]
    [SerializeField] private float scatterRadius = 2.0f;

    [Tooltip("키가 분출 위치까지 날아가는 시간입니다.")]
    [SerializeField] private float launchDuration = 0.35f;

    [Tooltip("분출 중 포물선처럼 추가로 올라가는 높이입니다.")]
    [SerializeField] private float scatterArcHeight = 0.8f;

    [Tooltip("착지점까지 거리가 멀수록 포물선 높이를 추가로 높이는 배율입니다.")]
    [SerializeField] private float arcHeightByDistance = 0.25f;

    [Tooltip("키캡 포물선 높이의 최대값입니다.")]
    [SerializeField] private float maxArcHeight = 7f;

    [Tooltip("착지 후 쿨다운으로 넘어가기 전까지 유지하는 시간입니다. 키는 이후에도 흩어진 위치에 남습니다.")]
    [SerializeField] private float activeDuration = 0.35f;

    [Tooltip("기믹 재발동을 막는 쿨다운 시간(초)입니다.")]
    [SerializeField] private float cooldownDuration = 1.0f;

    [Header("Scatter")]
    [Tooltip("각 키의 분출 방향을 무작위로 정할지 여부입니다.")]
    [SerializeField] private bool randomizeScatterDirection = true;

    [Tooltip("분출 방향의 무작위성을 조절합니다.")]
    [SerializeField] private float scatterDirectionJitter = 1.0f;

    [Tooltip("키가 날아가는 동안 회전할지 여부입니다.")]
    [SerializeField] private bool rotateWhileFlying = true;

    [Tooltip("키가 날아가는 동안 회전하는 속도입니다.")]
    [SerializeField] private float flyingRotationSpeed = 360f;

    [Header("Landing")]
    [Tooltip("키가 착지할 바닥/책상 레이어입니다. 비워두면 모든 레이어를 대상으로 검사합니다.")]
    [SerializeField] private LayerMask landingMask = ~0;

    [Tooltip("착지 위치를 찾기 위해 위에서 아래로 검사할 시작 높이입니다.")]
    [SerializeField] private float landingRaycastHeight = 4f;

    [Tooltip("착지 위치를 찾기 위한 아래 방향 검사 거리입니다.")]
    [SerializeField] private float landingRaycastDistance = 10f;

    [Tooltip("착지 표면에서 키를 살짝 띄우는 높이입니다.")]
    [SerializeField] private float landingSurfaceOffset = 0.05f;

    [Tooltip("키캡 착지 위치를 키보드 주변 반경이 아니라 책상 전체 랜덤 영역에서 고를지 여부입니다.")]
    [SerializeField] private bool useDeskLandingArea = true;

    [Tooltip("책상 랜덤 착지 영역의 중심 월드 좌표입니다.")]
    [SerializeField] private Vector3 deskLandingAreaCenter = Vector3.zero;

    [Tooltip("책상 랜덤 착지 영역의 크기입니다. X/Z는 가로/세로 범위, Y는 사용하지 않습니다.")]
    [SerializeField] private Vector3 deskLandingAreaSize = new Vector3(16f, 0f, 10f);

    [Tooltip("ㄱ자 책상처럼 여러 직사각형 착지 영역을 사용할 때 설정합니다. 비어 있으면 단일 Desk Landing Area 설정을 사용합니다.")]
    [SerializeField] private DeskLandingArea[] deskLandingAreas = System.Array.Empty<DeskLandingArea>();

    [Tooltip("키보드 시작 위치에서 이 거리보다 가까운 착지점은 다시 뽑습니다.")]
    [SerializeField] private float minLandingDistanceFromKeyboard = 2f;

    [Tooltip("착지점이 너무 격자처럼 보이지 않도록 추가하는 작은 랜덤 흔들림 범위입니다.")]
    [SerializeField] private float landingPointJitter = 0.4f;

    [Tooltip("착지점 랜덤 선택을 다시 시도하는 최대 횟수입니다.")]
    [SerializeField] private int landingPointPickAttempts = 12;

    [Tooltip("충돌 후 키가 원위치로 돌아가지 않고 흩어진 위치에 남을지 여부입니다.")]
    [SerializeField] private bool stayScatteredAfterImpact = true;

    [Tooltip("다음 발동 시작 전에 키를 원래 위치와 회전으로 되돌릴지 여부입니다.")]
    [SerializeField] private bool resetKeysBeforeStart = true;

    [Tooltip("착지 후 키캡을 일정 시간 뒤 숨길지 여부입니다.")]
    [SerializeField] private bool hideKeysAfterLanding = true;

    [Tooltip("키캡이 착지한 뒤 사라지기까지의 최소 시간입니다.")]
    [SerializeField] private float hideAfterLandingMinDelay = 1f;

    [Tooltip("키캡이 착지한 뒤 사라지기까지의 최대 시간입니다.")]
    [SerializeField] private float hideAfterLandingMaxDelay = 2f;

    [Header("Hit")]
    [Tooltip("키 주변 플레이어 판정 반경입니다.")]
    [SerializeField] private float hitRadius = 1.0f;

    [Tooltip("플레이어에게 적용할 수평 넉백 힘입니다.")]
    [SerializeField] private float knockbackForce = 8.0f;

    [Tooltip("플레이어에게 적용할 위쪽 넉백 힘입니다.")]
    [SerializeField] private float upwardForce = 2.0f;

    [Tooltip("플레이어 탐색에 사용할 레이어 마스크입니다. 비워두면 모든 PlayerStatusModule을 검사합니다.")]
    [SerializeField] private LayerMask playerMask = ~0;

    [Header("Cast VFX")]
    [Tooltip("KeyboardPop 기믹이 시작될 때 표시할 시전 VFX 프리팹입니다. 비어 있으면 표시하지 않습니다.")]
    [SerializeField] private GameObject keyboardPopCastVfxPrefab;

    [Tooltip("KeyboardPop 시전 VFX 생성 위치의 Y 오프셋입니다.")]
    [SerializeField] private float keyboardPopCastVfxYOffset = 0.35f;

    [Tooltip("KeyboardPop 시전 VFX를 강제로 제거할 시간입니다. 0이면 프리팹 자체 수명에 맡깁니다.")]
    [SerializeField] private float keyboardPopCastVfxLifetime = 1.5f;

    [Tooltip("선택된 burst key들의 평균 위치를 기준으로 시전 VFX를 표시할지 여부입니다. false이면 전체 keyTransforms 중심을 사용합니다.")]
    [SerializeField] private bool keyboardPopCastVfxUseSelectedKeysCenter = true;

    [Tooltip("시전 VFX를 KeyboardPopGimmick 오브젝트에 붙여서 표시할지 여부입니다.")]
    [SerializeField] private bool keyboardPopCastVfxAttachToRoot = false;

    [Header("Debug")]
    [Tooltip("키보드 팝 기믹의 일반 디버그 로그를 출력할지 여부입니다.")]
    [SerializeField] private bool enableDebugLogs = false;

    private Vector3[] _originalLocalPositions;
    private Quaternion[] _originalLocalRotations;
    private Vector3[] _landingWorldPositions;
    private Vector3[] _scatterDirections;
    private Vector3[] _rotationAxes;
    private float[] _arcHeights;
    private float[] _hideAtTimes;
    private float[] _hideAfterLandingDelays;
    private readonly HashSet<PlayerStatusModule> _hitPlayers = new HashSet<PlayerStatusModule>();
    private Coroutine _runningRoutine;
    private Coroutine _clientVisualRoutine;
    private GameObject _activeKeyboardPopCastVfx;

    private void Awake()
    {
        CaptureOriginalTransforms();
    }

    private void OnEnable()
    {
        EnsureOriginalTransformsCaptured();
    }

    private void OnDisable()
    {
        StopRunningAndRestore();
    }

    public override void OnDestroy()
    {
        StopRunningAndRestore();
        base.OnDestroy();
    }

    public override void OnNetworkDespawn()
    {
        StopRunningAndRestore();
        base.OnNetworkDespawn();
    }

    private void Update()
    {
        ProcessHideTimers();
    }

    [ContextMenu("Debug Start Keyboard Pop")]
    private void DebugStartKeyboardPop()
    {
        if (!Application.isPlaying)
        {
            LogWarning($"{LogPrefix} Debug start ignored. Application is not playing.");
            return;
        }

        if (_runningRoutine != null)
        {
            Log($"{LogPrefix} Start ignored. Already running.");
            return;
        }

        EnsureOriginalTransformsCaptured();

        if (!HasValidKey())
        {
            LogWarning($"{LogPrefix} Start ignored. No valid key transforms.");
            return;
        }

        if (resetKeysBeforeStart)
        {
            RestoreOriginalTransforms();
            Log($"{LogPrefix} Reset before start.");
        }

        PrepareBurstData();
        Vector3 castVfxPosition = GetKeyboardPopCastVfxPosition();
        SpawnKeyboardPopCastVfxLocal(castVfxPosition);
        SendPlayKeyboardPopVisual(castVfxPosition);
        _runningRoutine = StartCoroutine(RunRoutine());
    }

    private IEnumerator RunRoutine()
    {
        Log($"{LogPrefix} Started.");
        _hitPlayers.Clear();

        yield return PlayTelegraphRoutine();

        yield return LaunchKeysRoutine();
        Log($"{LogPrefix} Launched keys: count={CountValidPreparedKeys()}");

        SetKeysToLandingTargets(
            rotateWhileFlying ? flyingRotationSpeed * Mathf.Max(0f, launchDuration) : 0f,
            rotateWhileFlying);
        ScheduleHideAfterLanding();

        Log($"{LogPrefix} Impact.");
        ApplyServerKnockback();

        yield return WaitForSecondsSafe(activeDuration);
        Log($"{LogPrefix} Stayed scattered.");

        if (!stayScatteredAfterImpact)
            RestoreOriginalTransforms();

        yield return WaitForSecondsSafe(cooldownDuration);

        _runningRoutine = null;
        SendStopKeyboardPopVisual(!stayScatteredAfterImpact);
        ClearKeyboardPopCastVfxLocal();
        Log($"{LogPrefix} Ended.");
    }

    private IEnumerator PlayTelegraphRoutine()
    {
        return PlayTelegraphVisualRoutine(telegraphDuration);
    }

    private IEnumerator PlayTelegraphVisualRoutine(float visualTelegraphDuration)
    {
        float duration = Mathf.Max(0f, visualTelegraphDuration);
        if (duration <= 0f)
        {
            RestoreOriginalTransforms();
            yield break;
        }

        float elapsed = 0f;

        while (elapsed < duration)
        {
            SetKeysToTelegraphOffset(elapsed);
            elapsed += Time.deltaTime;
            yield return null;
        }

        RestoreOriginalTransforms();
    }

    private IEnumerator LaunchKeysRoutine()
    {
        return LaunchKeysVisualRoutine(launchDuration, rotateWhileFlying, flyingRotationSpeed);
    }

    private IEnumerator LaunchKeysVisualRoutine(
        float visualLaunchDuration,
        bool visualRotateWhileFlying,
        float visualFlyingRotationSpeed)
    {
        float duration = Mathf.Max(0f, visualLaunchDuration);
        if (duration <= 0f)
        {
            SetKeysToLandingTargets(
                visualRotateWhileFlying ? visualFlyingRotationSpeed * duration : 0f,
                visualRotateWhileFlying);
            yield break;
        }

        float elapsed = 0f;
        while (elapsed < duration)
        {
            float t = Mathf.Clamp01(elapsed / duration);
            float smoothT = Mathf.SmoothStep(0f, 1f, t);
            float arcMultiplier = Mathf.Sin(t * Mathf.PI);
            SetKeysToBurstPose(smoothT, arcMultiplier, elapsed, visualRotateWhileFlying, visualFlyingRotationSpeed);
            elapsed += Time.deltaTime;
            yield return null;
        }

        SetKeysToLandingTargets(
            visualRotateWhileFlying ? visualFlyingRotationSpeed * duration : 0f,
            visualRotateWhileFlying);
    }

    private void ApplyServerKnockback()
    {
        if (!CanApplyServerKnockback())
        {
            Log($"{LogPrefix} Knockback skipped. Server authority is not available.");
            return;
        }

        PlayerStatusModule[] players = FindPlayerStatusModules();
        if (players == null || players.Length == 0)
            return;

        float radius = Mathf.Max(0f, hitRadius);
        float radiusSqr = radius * radius;
        int keyCount = GetPreparedKeyCount();

        for (int keyIndex = 0; keyIndex < keyCount; keyIndex++)
        {
            Transform key = GetKeyAt(keyIndex);
            if (key == null)
                continue;

            Vector3 keyPosition = key.position;
            Vector3 fallbackDirection = GetWorldScatterDirection(keyIndex, key);

            for (int playerIndex = 0; playerIndex < players.Length; playerIndex++)
            {
                PlayerStatusModule status = players[playerIndex];
                if (_hitPlayers.Contains(status) || !IsValidTarget(status))
                    continue;

                Vector3 playerPosition = status.transform.position;
                Vector3 toPlayer = playerPosition - keyPosition;
                toPlayer.y = 0f;

                if (toPlayer.sqrMagnitude > radiusSqr)
                    continue;

                Vector3 horizontalDirection = toPlayer;
                if (horizontalDirection.sqrMagnitude < 0.0001f)
                    horizontalDirection = fallbackDirection;

                horizontalDirection.y = 0f;
                if (horizontalDirection.sqrMagnitude < 0.0001f)
                    horizontalDirection = Vector3.forward;
                else
                    horizontalDirection.Normalize();

                Vector3 impulse = horizontalDirection * knockbackForce + Vector3.up * upwardForce;
                status.ApplyKnockbackServer(impulse);
                _hitPlayers.Add(status);
                Log($"{LogPrefix} Hit player: {status.name}, key:{key.name}, impulse:{impulse}");
            }
        }
    }

    private bool IsValidTarget(PlayerStatusModule status)
    {
        if (status == null)
            return false;

        if (!status.IsSpawned)
            return false;

        if (status.IsEliminated || status.IsKnocked || status.IsStandingUp)
            return false;

        return IsInPlayerMask(status);
    }

    private void PrepareBurstData()
    {
        int count = GetStoredKeyCount();
        int validKeyCount = CountValidStoredKeys();
        int validIndex = 0;

        _landingWorldPositions = new Vector3[count];
        _scatterDirections = new Vector3[count];
        _rotationAxes = new Vector3[count];
        _arcHeights = new float[count];
        _hideAtTimes = new float[count];
        _hideAfterLandingDelays = BuildHideAfterLandingDelays(count);
        ClearHideTimers();

        for (int i = 0; i < count; i++)
        {
            Transform key = GetKeyAt(i);
            if (key == null)
                continue;

            Vector3 scatterDirection = BuildScatterDirection(validIndex, validKeyCount);
            _scatterDirections[i] = scatterDirection;
            _rotationAxes[i] = BuildRotationAxis(scatterDirection);
            _landingWorldPositions[i] = CalculateLandingWorldPosition(i, scatterDirection);
            _arcHeights[i] = CalculateArcHeight(GetOriginalWorldPosition(i, key), _landingWorldPositions[i]);
            Log($"{LogPrefix} Landing position calculated. key:{key.name}, position:{_landingWorldPositions[i]}");

            validIndex++;
        }
    }

    private Vector3 BuildScatterDirection(int validIndex, int validKeyCount)
    {
        int count = Mathf.Max(1, validKeyCount);
        float sectorAngle = 360f / count;
        float baseAngle = count == 1 && randomizeScatterDirection
            ? Random.Range(0f, 360f)
            : sectorAngle * validIndex;
        float jitter = 0f;

        if (randomizeScatterDirection)
            jitter = Random.Range(-sectorAngle * 0.5f, sectorAngle * 0.5f) * Mathf.Max(0f, scatterDirectionJitter);

        float radians = (baseAngle + jitter) * Mathf.Deg2Rad;
        Vector3 direction = new Vector3(Mathf.Sin(radians), 0f, Mathf.Cos(radians));

        if (direction.sqrMagnitude < 0.0001f)
            return Vector3.forward;

        return direction.normalized;
    }

    private Vector3 BuildRotationAxis(Vector3 scatterDirection)
    {
        if (!rotateWhileFlying)
            return Vector3.up;

        Vector3 axis = Vector3.Cross(scatterDirection, Vector3.up);
        if (axis.sqrMagnitude < 0.0001f)
            axis = Random.onUnitSphere;

        if (axis.sqrMagnitude < 0.0001f)
            axis = Vector3.up;

        return axis.normalized;
    }

    private Vector3 CalculateLandingWorldPosition(int index, Vector3 scatterDirection)
    {
        Transform key = GetKeyAt(index);
        if (key == null)
            return Vector3.zero;

        Vector3 startWorldPosition = GetOriginalWorldPosition(index, key);
        bool canUseDeskLandingArea = CanUseDeskLandingArea();
        Vector3 landingTarget = canUseDeskLandingArea
            ? PickLandingPosition(startWorldPosition)
            : CalculateScatterWorldTarget(index, scatterDirection);

        if (!IsFiniteVector(landingTarget) ||
            (canUseDeskLandingArea && IsTooCloseToLandingStart(startWorldPosition, landingTarget)))
        {
            landingTarget = CalculateScatterWorldTarget(index, scatterDirection);
        }

        return ProjectLandingToSurface(landingTarget);
    }

    private Vector3 PickLandingPosition(Vector3 startPosition)
    {
        if (!CanUseDeskLandingArea() || !IsFiniteVector(startPosition))
            return startPosition;

        int attempts = Mathf.Max(1, landingPointPickAttempts);
        Vector3 fallbackCandidate = startPosition;
        bool hasFallbackCandidate = false;

        for (int i = 0; i < attempts; i++)
        {
            DeskLandingArea area = PickDeskLandingAreaOrSingle();
            Vector3 candidate = PickPositionInDeskLandingArea(area, startPosition.y);
            if (!IsFiniteVector(candidate))
                continue;

            if (!hasFallbackCandidate)
            {
                fallbackCandidate = candidate;
                hasFallbackCandidate = true;
            }

            if (!IsTooCloseToLandingStart(startPosition, candidate))
                return candidate;
        }

        return hasFallbackCandidate ? fallbackCandidate : startPosition;
    }

    private bool TryPickDeskLandingArea(out DeskLandingArea area)
    {
        area = default(DeskLandingArea);
        if (deskLandingAreas == null || deskLandingAreas.Length == 0)
            return false;

        float totalWeight = 0f;
        for (int i = 0; i < deskLandingAreas.Length; i++)
        {
            DeskLandingArea candidate = deskLandingAreas[i];
            if (!IsValidDeskLandingArea(candidate) || !IsFiniteFloat(candidate.weight))
                continue;

            totalWeight += candidate.weight;
        }

        if (totalWeight <= 0f || !IsFiniteFloat(totalWeight))
            return false;

        float randomWeight = Random.Range(0f, totalWeight);
        float accumulatedWeight = 0f;

        for (int i = 0; i < deskLandingAreas.Length; i++)
        {
            DeskLandingArea candidate = deskLandingAreas[i];
            if (!IsValidDeskLandingArea(candidate) || !IsFiniteFloat(candidate.weight))
                continue;

            accumulatedWeight += candidate.weight;
            if (randomWeight <= accumulatedWeight)
            {
                area = candidate;
                return true;
            }
        }

        return false;
    }

    private DeskLandingArea PickDeskLandingAreaOrSingle()
    {
        if (TryPickDeskLandingArea(out DeskLandingArea area))
            return area;

        return BuildSingleDeskLandingArea();
    }

    private DeskLandingArea BuildSingleDeskLandingArea()
    {
        return new DeskLandingArea
        {
            name = "Single",
            center = deskLandingAreaCenter,
            size = deskLandingAreaSize,
            weight = 1f
        };
    }

    private Vector3 PickPositionInDeskLandingArea(DeskLandingArea area, float y)
    {
        if (!IsValidDeskLandingArea(area))
            return Vector3.zero;

        float halfX = Mathf.Abs(area.size.x) * 0.5f;
        float halfZ = Mathf.Abs(area.size.z) * 0.5f;
        float jitter = Mathf.Max(0f, landingPointJitter);

        Vector3 candidate = new Vector3(
            Random.Range(area.center.x - halfX, area.center.x + halfX),
            y,
            Random.Range(area.center.z - halfZ, area.center.z + halfZ));

        if (jitter > 0f)
        {
            candidate.x += Random.Range(-jitter, jitter);
            candidate.z += Random.Range(-jitter, jitter);
        }

        return candidate;
    }

    private bool CanUseDeskLandingArea()
    {
        if (!useDeskLandingArea)
            return false;

        if (HasAnyValidDeskLandingArea())
            return true;

        return IsValidDeskLandingArea(BuildSingleDeskLandingArea());
    }

    private bool HasAnyValidDeskLandingArea()
    {
        if (deskLandingAreas == null)
            return false;

        for (int i = 0; i < deskLandingAreas.Length; i++)
        {
            DeskLandingArea area = deskLandingAreas[i];
            if (IsValidDeskLandingArea(area) && IsFiniteFloat(area.weight) && area.weight > 0f)
                return true;
        }

        return false;
    }

    private static bool IsValidDeskLandingArea(DeskLandingArea area)
    {
        return IsFiniteVector(area.center) &&
            IsFiniteVector(area.size) &&
            IsFiniteFloat(area.weight) &&
            area.size.x > 0.0001f &&
            area.size.z > 0.0001f &&
            area.weight > 0f;
    }

    private Vector3 CalculateScatterWorldTarget(int index, Vector3 scatterDirection)
    {
        Transform key = GetKeyAt(index);
        if (key == null || _originalLocalPositions == null || index < 0 || index >= _originalLocalPositions.Length)
            return Vector3.zero;

        Vector3 scatterLocalTarget = _originalLocalPositions[index] + scatterDirection * Mathf.Max(0f, scatterRadius);
        return TransformLocalPoint(key, scatterLocalTarget);
    }

    private Vector3 ProjectLandingToSurface(Vector3 landingTarget)
    {
        if (!IsFiniteVector(landingTarget))
            return Vector3.zero;

        Vector3 rayStart = landingTarget + Vector3.up * Mathf.Max(0f, landingRaycastHeight);
        float rayDistance = Mathf.Max(0f, landingRaycastDistance);
        int mask = landingMask.value == 0 ? ~0 : landingMask.value;

        if (rayDistance > 0f &&
            Physics.Raycast(rayStart, Vector3.down, out RaycastHit hit, rayDistance, mask, QueryTriggerInteraction.Ignore))
        {
            return hit.point + Vector3.up * Mathf.Max(0f, landingSurfaceOffset);
        }

        return landingTarget;
    }

    private float CalculateArcHeight(Vector3 startWorldPosition, Vector3 landingWorldPosition)
    {
        float baseArcHeight = Mathf.Max(0f, launchHeight) + Mathf.Max(0f, scatterArcHeight);
        float distance = IsFiniteVector(startWorldPosition) && IsFiniteVector(landingWorldPosition)
            ? HorizontalDistance(startWorldPosition, landingWorldPosition)
            : 0f;
        float targetArcHeight = baseArcHeight + distance * Mathf.Max(0f, arcHeightByDistance);

        return Mathf.Min(Mathf.Max(0f, maxArcHeight), targetArcHeight);
    }

    private Vector3 GetOriginalWorldPosition(int index, Transform key)
    {
        if (key == null || index < 0 || index >= GetStoredKeyCount())
            return Vector3.zero;

        return TransformLocalPoint(key, _originalLocalPositions[index]);
    }

    private Vector3 TransformLocalPoint(Transform key, Vector3 localPoint)
    {
        Transform parent = key != null ? key.parent : null;
        return parent != null ? parent.TransformPoint(localPoint) : localPoint;
    }

    private void EnsureOriginalTransformsCaptured()
    {
        if (_originalLocalPositions == null ||
            _originalLocalRotations == null ||
            _originalLocalPositions.Length != GetKeyCount() ||
            _originalLocalRotations.Length != GetKeyCount())
        {
            CaptureOriginalTransforms();
        }
    }

    private void CaptureOriginalTransforms()
    {
        int count = GetKeyCount();
        _originalLocalPositions = new Vector3[count];
        _originalLocalRotations = new Quaternion[count];

        for (int i = 0; i < count; i++)
        {
            Transform key = GetKeyAt(i);
            if (key == null)
            {
                _originalLocalRotations[i] = Quaternion.identity;
                continue;
            }

            _originalLocalPositions[i] = key.localPosition;
            _originalLocalRotations[i] = key.localRotation;
        }
    }

    private void RestoreOriginalTransforms()
    {
        int count = GetStoredKeyCount();
        for (int i = 0; i < count; i++)
        {
            Transform key = GetKeyAt(i);
            if (key == null)
                continue;

            key.localPosition = _originalLocalPositions[i];
            key.localRotation = _originalLocalRotations[i];

            if (!key.gameObject.activeSelf)
                key.gameObject.SetActive(true);
        }

        ClearHideTimers();
    }

    private void SetKeysToTelegraphOffset(float elapsed)
    {
        int count = GetStoredKeyCount();
        for (int i = 0; i < count; i++)
        {
            Transform key = GetKeyAt(i);
            if (key == null)
                continue;

            Vector3 scatterDirection = GetLocalScatterDirection(i);
            float verticalWave = Mathf.Sin((elapsed * Mathf.PI * 12f) + i * 0.73f) * 0.025f;
            float lateralWave = Mathf.Sin((elapsed * Mathf.PI * 9f) + i * 1.19f) * 0.015f;
            key.localPosition = _originalLocalPositions[i] + Vector3.up * verticalWave + scatterDirection * lateralWave;
            key.localRotation = _originalLocalRotations[i];
        }
    }

    private void SetKeysToBurstPose(float t, float arcMultiplier, float elapsed)
    {
        SetKeysToBurstPose(t, arcMultiplier, elapsed, rotateWhileFlying, flyingRotationSpeed);
    }

    private void SetKeysToBurstPose(
        float t,
        float arcMultiplier,
        float elapsed,
        bool visualRotateWhileFlying,
        float visualFlyingRotationSpeed)
    {
        int count = GetPreparedKeyCount();
        for (int i = 0; i < count; i++)
        {
            Transform key = GetKeyAt(i);
            if (key == null)
                continue;

            Vector3 startWorldPosition = GetOriginalWorldPosition(i, key);
            Vector3 baseWorldPosition = Vector3.LerpUnclamped(startWorldPosition, _landingWorldPositions[i], t);
            key.position = baseWorldPosition + Vector3.up * (arcMultiplier * GetArcHeight(i));

            if (visualRotateWhileFlying)
                key.localRotation = _originalLocalRotations[i] * Quaternion.AngleAxis(visualFlyingRotationSpeed * elapsed, _rotationAxes[i]);
        }
    }

    private void SetKeysToLandingTargets(float rotationAngle)
    {
        SetKeysToLandingTargets(rotationAngle, rotateWhileFlying);
    }

    private void SetKeysToLandingTargets(float rotationAngle, bool visualRotateWhileFlying)
    {
        int count = GetPreparedKeyCount();
        for (int i = 0; i < count; i++)
        {
            Transform key = GetKeyAt(i);
            if (key == null)
                continue;

            key.position = _landingWorldPositions[i];
            key.localRotation = visualRotateWhileFlying
                ? _originalLocalRotations[i] * Quaternion.AngleAxis(rotationAngle, _rotationAxes[i])
                : _originalLocalRotations[i];
        }
    }

    private void ScheduleHideAfterLanding()
    {
        ScheduleHideAfterLanding(_hideAfterLandingDelays, hideKeysAfterLanding);
    }

    private void ScheduleHideAfterLanding(float[] hideAfterLandingDelays, bool shouldHideKeysAfterLanding)
    {
        if (_hideAtTimes == null || _hideAtTimes.Length != GetStoredKeyCount())
            _hideAtTimes = new float[GetStoredKeyCount()];

        ClearHideTimers();
        if (!shouldHideKeysAfterLanding)
            return;

        int count = Mathf.Min(GetPreparedKeyCount(), _hideAtTimes.Length);

        for (int i = 0; i < count; i++)
        {
            Transform key = GetKeyAt(i);
            if (key == null)
                continue;

            float hideDelay = hideAfterLandingDelays != null && i < hideAfterLandingDelays.Length
                ? hideAfterLandingDelays[i]
                : -1f;
            if (!IsFiniteFloat(hideDelay) || hideDelay < 0f)
                continue;

            _hideAtTimes[i] = Time.time + hideDelay;
        }
    }

    private void ProcessHideTimers()
    {
        if (_hideAtTimes == null)
            return;

        float now = Time.time;
        int count = Mathf.Min(GetKeyCount(), _hideAtTimes.Length);
        for (int i = 0; i < count; i++)
        {
            float hideAtTime = _hideAtTimes[i];
            if (hideAtTime <= 0f || now < hideAtTime)
                continue;

            Transform key = GetKeyAt(i);
            _hideAtTimes[i] = -1f;

            if (key == null || !key.gameObject.activeSelf)
                continue;

            key.gameObject.SetActive(false);
        }
    }

    private void ClearHideTimers()
    {
        if (_hideAtTimes == null)
            return;

        for (int i = 0; i < _hideAtTimes.Length; i++)
            _hideAtTimes[i] = -1f;
    }

    private void StopRunningAndRestore()
    {
        if (_runningRoutine != null)
        {
            StopCoroutine(_runningRoutine);
            _runningRoutine = null;
        }

        SendCleanupKeyboardPopVisual();
        StopClientKeyboardPopVisualRoutine();
        ClearKeyboardPopCastVfxLocal();
        RestoreOriginalTransforms();
        _hitPlayers.Clear();
    }

    private float[] BuildHideAfterLandingDelays(int count)
    {
        int safeCount = Mathf.Max(0, count);
        float[] hideDelays = new float[safeCount];
        for (int i = 0; i < safeCount; i++)
            hideDelays[i] = -1f;

        if (!hideKeysAfterLanding)
            return hideDelays;

        float minDelay = Mathf.Max(0f, hideAfterLandingMinDelay);
        float maxDelay = Mathf.Max(minDelay, hideAfterLandingMaxDelay);
        for (int i = 0; i < safeCount; i++)
        {
            if (GetKeyAt(i) == null)
                continue;

            hideDelays[i] = Random.Range(minDelay, maxDelay);
        }

        return hideDelays;
    }

    private void SendPlayKeyboardPopVisual(Vector3 castVfxPosition)
    {
        if (!CanSendKeyboardPopVisualMessage())
            return;

        PlayKeyboardPopVisualClientRpc(
            _landingWorldPositions ?? System.Array.Empty<Vector3>(),
            _scatterDirections ?? System.Array.Empty<Vector3>(),
            _rotationAxes ?? System.Array.Empty<Vector3>(),
            _arcHeights ?? System.Array.Empty<float>(),
            _hideAfterLandingDelays ?? System.Array.Empty<float>(),
            telegraphDuration,
            launchDuration,
            activeDuration,
            cooldownDuration,
            rotateWhileFlying,
            flyingRotationSpeed,
            stayScatteredAfterImpact,
            hideKeysAfterLanding,
            resetKeysBeforeStart,
            castVfxPosition);
        Log($"{LogPrefix} Visual RPC sent keys={CountValidPreparedKeys()}");
    }

    private void SendStopKeyboardPopVisual(bool restoreOriginal)
    {
        if (CanSendKeyboardPopVisualMessage())
            StopKeyboardPopVisualClientRpc(restoreOriginal);
    }

    private void SendCleanupKeyboardPopVisual()
    {
        if (CanSendKeyboardPopVisualMessage())
            CleanupKeyboardPopVisualClientRpc();
    }

    private bool CanSendKeyboardPopVisualMessage()
    {
        return IsServer && IsSpawned;
    }

    private bool ShouldSkipKeyboardPopVisualOnThisPeer()
    {
        return IsServer;
    }

    [ClientRpc]
    private void PlayKeyboardPopVisualClientRpc(
        Vector3[] landingWorldPositions,
        Vector3[] scatterDirections,
        Vector3[] rotationAxes,
        float[] arcHeights,
        float[] hideAfterLandingDelays,
        float visualTelegraphDuration,
        float visualLaunchDuration,
        float visualActiveDuration,
        float visualCooldownDuration,
        bool visualRotateWhileFlying,
        float visualFlyingRotationSpeed,
        bool visualStayScatteredAfterImpact,
        bool visualHideKeysAfterLanding,
        bool visualResetKeysBeforeStart,
        Vector3 castVfxPosition)
    {
        if (ShouldSkipKeyboardPopVisualOnThisPeer())
            return;

        EnsureOriginalTransformsCaptured();
        if (!TryApplyKeyboardPopVisualPayload(
            landingWorldPositions,
            scatterDirections,
            rotationAxes,
            arcHeights,
            hideAfterLandingDelays))
        {
            Log($"{LogPrefix} Visual RPC ignored. reason=invalid-payload");
            return;
        }

        if (_clientVisualRoutine != null)
            StopCoroutine(_clientVisualRoutine);

        if (visualResetKeysBeforeStart)
            RestoreOriginalTransforms();

        SpawnKeyboardPopCastVfxLocal(castVfxPosition);

        _clientVisualRoutine = StartCoroutine(RunKeyboardPopVisualOnlyRoutine(
            visualTelegraphDuration,
            visualLaunchDuration,
            visualActiveDuration,
            visualCooldownDuration,
            visualRotateWhileFlying,
            visualFlyingRotationSpeed,
            visualStayScatteredAfterImpact,
            visualHideKeysAfterLanding));
        Log($"{LogPrefix} Visual RPC received keys={CountValidPreparedKeys()}");
    }

    [ClientRpc]
    private void StopKeyboardPopVisualClientRpc(bool restoreOriginal)
    {
        if (ShouldSkipKeyboardPopVisualOnThisPeer())
            return;

        StopClientKeyboardPopVisualRoutine();
        ClearKeyboardPopCastVfxLocal();
        if (restoreOriginal)
            RestoreOriginalTransforms();

        Log($"{LogPrefix} Visual RPC stopped restore={restoreOriginal}");
    }

    [ClientRpc]
    private void CleanupKeyboardPopVisualClientRpc()
    {
        if (ShouldSkipKeyboardPopVisualOnThisPeer())
            return;

        StopClientKeyboardPopVisualRoutine();
        ClearKeyboardPopCastVfxLocal();
        RestoreOriginalTransforms();
        Log($"{LogPrefix} Visual cleanup client");
    }

    private bool TryApplyKeyboardPopVisualPayload(
        Vector3[] landingWorldPositions,
        Vector3[] scatterDirections,
        Vector3[] rotationAxes,
        float[] arcHeights,
        float[] hideAfterLandingDelays)
    {
        if (landingWorldPositions == null ||
            scatterDirections == null ||
            rotationAxes == null ||
            arcHeights == null)
        {
            return false;
        }

        _landingWorldPositions = landingWorldPositions;
        _scatterDirections = scatterDirections;
        _rotationAxes = rotationAxes;
        _arcHeights = arcHeights;
        _hideAfterLandingDelays = hideAfterLandingDelays ?? System.Array.Empty<float>();
        ClearHideTimers();
        return GetPreparedKeyCount() > 0;
    }

    private IEnumerator RunKeyboardPopVisualOnlyRoutine(
        float visualTelegraphDuration,
        float visualLaunchDuration,
        float visualActiveDuration,
        float visualCooldownDuration,
        bool visualRotateWhileFlying,
        float visualFlyingRotationSpeed,
        bool visualStayScatteredAfterImpact,
        bool visualHideKeysAfterLanding)
    {
        yield return PlayTelegraphVisualRoutine(visualTelegraphDuration);
        yield return LaunchKeysVisualRoutine(visualLaunchDuration, visualRotateWhileFlying, visualFlyingRotationSpeed);

        float landingRotationAngle = visualRotateWhileFlying
            ? visualFlyingRotationSpeed * Mathf.Max(0f, visualLaunchDuration)
            : 0f;
        SetKeysToLandingTargets(landingRotationAngle, visualRotateWhileFlying);
        ScheduleHideAfterLanding(_hideAfterLandingDelays, visualHideKeysAfterLanding);

        yield return WaitForSecondsSafe(visualActiveDuration);

        if (!visualStayScatteredAfterImpact)
            RestoreOriginalTransforms();

        yield return WaitForSecondsSafe(visualCooldownDuration);
        _clientVisualRoutine = null;
    }

    private void StopClientKeyboardPopVisualRoutine()
    {
        if (_clientVisualRoutine == null)
            return;

        StopCoroutine(_clientVisualRoutine);
        _clientVisualRoutine = null;
    }

    private Vector3 GetKeyboardPopCastVfxPosition()
    {
        Vector3 center;
        if (keyboardPopCastVfxUseSelectedKeysCenter && TryGetPreparedKeyCenter(out center))
            return ApplyKeyboardPopCastVfxYOffset(center);

        if (TryGetAllKeyCenter(out center))
            return ApplyKeyboardPopCastVfxYOffset(center);

        return ApplyKeyboardPopCastVfxYOffset(transform.position);
    }

    private bool TryGetPreparedKeyCenter(out Vector3 center)
    {
        center = Vector3.zero;
        int count = GetPreparedKeyCount();
        int validCount = 0;

        for (int i = 0; i < count; i++)
        {
            Transform key = GetKeyAt(i);
            if (key == null)
                continue;

            Vector3 position = GetOriginalWorldPosition(i, key);
            if (!IsFiniteVector(position))
                position = key.position;
            if (!IsFiniteVector(position))
                continue;

            center += position;
            validCount++;
        }

        if (validCount <= 0)
            return false;

        center /= validCount;
        return true;
    }

    private bool TryGetAllKeyCenter(out Vector3 center)
    {
        center = Vector3.zero;
        int count = GetKeyCount();
        int validCount = 0;

        for (int i = 0; i < count; i++)
        {
            Transform key = GetKeyAt(i);
            if (key == null || !IsFiniteVector(key.position))
                continue;

            center += key.position;
            validCount++;
        }

        if (validCount <= 0)
            return false;

        center /= validCount;
        return true;
    }

    private Vector3 ApplyKeyboardPopCastVfxYOffset(Vector3 position)
    {
        float yOffset = IsFiniteFloat(keyboardPopCastVfxYOffset) ? keyboardPopCastVfxYOffset : 0f;
        return position + Vector3.up * yOffset;
    }

    private void SpawnKeyboardPopCastVfxLocal(Vector3 position)
    {
        if (keyboardPopCastVfxPrefab == null)
        {
            Log($"{LogPrefix} Cast VFX skipped prefab null");
            return;
        }

        if (!IsFiniteVector(position))
            position = ApplyKeyboardPopCastVfxYOffset(transform.position);

        ClearKeyboardPopCastVfxLocal();

        Transform parent = keyboardPopCastVfxAttachToRoot ? transform : null;
        GameObject instance = Instantiate(keyboardPopCastVfxPrefab, position, transform.rotation, parent);
        if (instance == null)
            return;

        _activeKeyboardPopCastVfx = instance;

        float lifetime = IsFiniteFloat(keyboardPopCastVfxLifetime)
            ? Mathf.Max(0f, keyboardPopCastVfxLifetime)
            : 0f;
        if (lifetime > 0f)
            Destroy(instance, lifetime);

        Log($"{LogPrefix} Cast VFX spawned position={position}");
    }

    private void ClearKeyboardPopCastVfxLocal()
    {
        if (_activeKeyboardPopCastVfx == null)
            return;

        Destroy(_activeKeyboardPopCastVfx);
        _activeKeyboardPopCastVfx = null;
        Log($"{LogPrefix} Cast VFX cleanup");
    }

    private bool HasValidKey()
    {
        int count = GetKeyCount();
        for (int i = 0; i < count; i++)
        {
            if (GetKeyAt(i) != null)
                return true;
        }

        return false;
    }

    private int GetKeyCount()
    {
        return keyTransforms != null ? keyTransforms.Length : 0;
    }

    private int GetStoredKeyCount()
    {
        if (_originalLocalPositions == null || _originalLocalRotations == null)
            return 0;

        return Mathf.Min(GetKeyCount(), Mathf.Min(_originalLocalPositions.Length, _originalLocalRotations.Length));
    }

    private int GetPreparedKeyCount()
    {
        if (_landingWorldPositions == null || _scatterDirections == null || _rotationAxes == null || _arcHeights == null)
            return 0;

        int preparedCount = Mathf.Min(
            Mathf.Min(_landingWorldPositions.Length, _scatterDirections.Length),
            Mathf.Min(_rotationAxes.Length, _arcHeights.Length));
        return Mathf.Min(GetStoredKeyCount(), preparedCount);
    }

    private Transform GetKeyAt(int index)
    {
        if (keyTransforms == null || index < 0 || index >= keyTransforms.Length)
            return null;

        return keyTransforms[index];
    }

    private int CountValidStoredKeys()
    {
        int count = GetStoredKeyCount();
        int validCount = 0;

        for (int i = 0; i < count; i++)
        {
            if (GetKeyAt(i) != null)
                validCount++;
        }

        return validCount;
    }

    private int CountValidPreparedKeys()
    {
        int count = GetPreparedKeyCount();
        int validCount = 0;

        for (int i = 0; i < count; i++)
        {
            if (GetKeyAt(i) != null)
                validCount++;
        }

        return validCount;
    }

    private Vector3 GetLocalScatterDirection(int index)
    {
        if (_scatterDirections == null || index < 0 || index >= _scatterDirections.Length)
            return Vector3.forward;

        Vector3 direction = _scatterDirections[index];
        if (direction.sqrMagnitude < 0.0001f)
            return Vector3.forward;

        return direction.normalized;
    }

    private Vector3 GetWorldScatterDirection(int index, Transform key)
    {
        Vector3 localDirection = GetLocalScatterDirection(index);
        Transform parent = key != null ? key.parent : null;
        Vector3 worldDirection = parent != null ? parent.TransformDirection(localDirection) : localDirection;
        worldDirection.y = 0f;

        if (worldDirection.sqrMagnitude < 0.0001f && key != null)
            worldDirection = key.forward;

        worldDirection.y = 0f;
        if (worldDirection.sqrMagnitude < 0.0001f)
            worldDirection = transform.forward;

        worldDirection.y = 0f;
        if (worldDirection.sqrMagnitude < 0.0001f)
            return Vector3.forward;

        return worldDirection.normalized;
    }

    private float GetArcHeight(int index)
    {
        if (_arcHeights == null || index < 0 || index >= _arcHeights.Length)
            return CalculateBaseArcHeight();

        float arcHeight = _arcHeights[index];
        return IsFiniteFloat(arcHeight) ? Mathf.Max(0f, arcHeight) : CalculateBaseArcHeight();
    }

    private float CalculateBaseArcHeight()
    {
        return Mathf.Max(0f, launchHeight) + Mathf.Max(0f, scatterArcHeight);
    }

    private bool IsTooCloseToLandingStart(Vector3 startPosition, Vector3 candidate)
    {
        float minDistance = Mathf.Max(0f, minLandingDistanceFromKeyboard);
        if (minDistance <= 0f)
            return false;

        return HorizontalSqrDistance(startPosition, candidate) < minDistance * minDistance;
    }

    private static float HorizontalDistance(Vector3 a, Vector3 b)
    {
        return Mathf.Sqrt(HorizontalSqrDistance(a, b));
    }

    private static float HorizontalSqrDistance(Vector3 a, Vector3 b)
    {
        float x = a.x - b.x;
        float z = a.z - b.z;
        return x * x + z * z;
    }

    private static bool IsFiniteVector(Vector3 value)
    {
        return IsFiniteFloat(value.x) && IsFiniteFloat(value.y) && IsFiniteFloat(value.z);
    }

    private static bool IsFiniteFloat(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }

    private bool CanApplyServerKnockback()
    {
        NetworkManager networkManager = NetworkManager.Singleton;
        return networkManager != null && networkManager.IsServer;
    }

    private PlayerStatusModule[] FindPlayerStatusModules()
    {
#if UNITY_6000_0_OR_NEWER
        return FindObjectsByType<PlayerStatusModule>(FindObjectsSortMode.None);
#else
        return FindObjectsOfType<PlayerStatusModule>();
#endif
    }

    private bool IsInPlayerMask(PlayerStatusModule status)
    {
        if (playerMask.value == 0)
            return true;

        if (IsLayerInPlayerMask(status.gameObject.layer))
            return true;

        Transform root = status.transform.root;
        return root != null && IsLayerInPlayerMask(root.gameObject.layer);
    }

    private bool IsLayerInPlayerMask(int layer)
    {
        return (playerMask.value & (1 << layer)) != 0;
    }

    private static IEnumerator WaitForSecondsSafe(float duration)
    {
        if (duration <= 0f)
            yield break;

        yield return new WaitForSeconds(duration);
    }

    private void OnDrawGizmosSelected()
    {
        if (!useDeskLandingArea)
            return;

        bool drewAnyArea = false;
        if (deskLandingAreas != null && deskLandingAreas.Length > 0)
        {
            for (int i = 0; i < deskLandingAreas.Length; i++)
            {
                DeskLandingArea area = deskLandingAreas[i];
                if (!IsValidDeskLandingArea(area))
                    continue;

                float hue = Mathf.Repeat(i * 0.17f, 1f);
                Gizmos.color = Color.HSVToRGB(hue, 0.65f, 1f);
                DrawDeskLandingAreaGizmo(area);
                drewAnyArea = true;
            }
        }

        if (!drewAnyArea)
        {
            DeskLandingArea singleArea = BuildSingleDeskLandingArea();
            if (!IsValidDeskLandingArea(singleArea))
                return;

            Gizmos.color = new Color(0.15f, 0.75f, 1f, 0.8f);
            DrawDeskLandingAreaGizmo(singleArea);
        }
    }

    private static void DrawDeskLandingAreaGizmo(DeskLandingArea area)
    {
        Vector3 gizmoSize = new Vector3(
            Mathf.Abs(area.size.x),
            0.05f,
            Mathf.Abs(area.size.z));

        Gizmos.DrawWireCube(area.center, gizmoSize);
    }

    private void Log(string message)
    {
        if (!enableDebugLogs)
            return;

        Debug.Log(message, this);
    }

    private void LogWarning(string message)
    {
        if (!enableDebugLogs)
            return;

        Debug.LogWarning(message, this);
    }
}
