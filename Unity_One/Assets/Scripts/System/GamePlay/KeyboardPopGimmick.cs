using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public class KeyboardPopGimmick : MonoBehaviour
{
    private const string LogPrefix = "[KEYBOARD_POP]";

    private enum Phase
    {
        Idle = 0,
        Telegraph = 1,
        Launch = 2,
        Land = 3,
        Impact = 4,
        StayScattered = 5,
        Cooldown = 6
    }

    [Header("Keys")]
    [Tooltip("튀어나올 키 오브젝트들입니다.")]
    [SerializeField] private Transform[] keyTransforms;

    [Tooltip("이전 단순 팝 연출에서 사용하던 로컬 위치 오프셋입니다. 현재 분출형 이동에서는 사용하지 않습니다.")]
    [SerializeField] private Vector3 popLocalOffset = new Vector3(0f, 0.35f, 0f);

    [Tooltip("이전 단순 팝 연출에서 사용하던 튀어나오는 시간입니다. 현재 분출형 이동에서는 사용하지 않습니다.")]
    [SerializeField] private float popDuration = 0.12f;

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

    [Tooltip("이전 자동 복귀 연출에서 사용하던 시간입니다. 현재 StayScattered 모드에서는 자동 복귀에 사용하지 않습니다.")]
    [SerializeField] private float returnDuration = 0.35f;

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

    [Header("Hit")]
    [Tooltip("키 주변 플레이어 판정 반경입니다.")]
    [SerializeField] private float hitRadius = 1.0f;

    [Tooltip("플레이어에게 적용할 수평 넉백 힘입니다.")]
    [SerializeField] private float knockbackForce = 8.0f;

    [Tooltip("플레이어에게 적용할 위쪽 넉백 힘입니다.")]
    [SerializeField] private float upwardForce = 2.0f;

    [Tooltip("플레이어 탐색에 사용할 레이어 마스크입니다. 비워두면 모든 PlayerStatusModule을 검사합니다.")]
    [SerializeField] private LayerMask playerMask = ~0;

    [Header("Debug")]
    [Tooltip("키보드 팝 기믹의 일반 디버그 로그를 출력할지 여부입니다.")]
    [SerializeField] private bool enableDebugLogs = false;

    private Vector3[] _originalLocalPositions;
    private Quaternion[] _originalLocalRotations;
    private Vector3[] _landingWorldPositions;
    private Vector3[] _scatterDirections;
    private Vector3[] _rotationAxes;
    private float[] _arcHeights;
    private readonly HashSet<PlayerStatusModule> _hitPlayers = new HashSet<PlayerStatusModule>();
    private Coroutine _runningRoutine;
    private Phase _phase = Phase.Idle;

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
        _runningRoutine = StartCoroutine(RunRoutine());
    }

    private IEnumerator RunRoutine()
    {
        Log($"{LogPrefix} Started.");
        _hitPlayers.Clear();

        _phase = Phase.Telegraph;
        yield return PlayTelegraphRoutine();

        _phase = Phase.Launch;
        yield return LaunchKeysRoutine();
        Log($"{LogPrefix} Launched keys: count={CountValidPreparedKeys()}");

        _phase = Phase.Land;
        SetKeysToLandingTargets(rotateWhileFlying ? flyingRotationSpeed * Mathf.Max(0f, launchDuration) : 0f);

        _phase = Phase.Impact;
        Log($"{LogPrefix} Impact.");
        ApplyServerKnockback();

        _phase = Phase.StayScattered;
        yield return WaitForSecondsSafe(activeDuration);
        Log($"{LogPrefix} Stayed scattered.");

        if (!stayScatteredAfterImpact)
            RestoreOriginalTransforms();

        _phase = Phase.Cooldown;
        yield return WaitForSecondsSafe(cooldownDuration);

        _phase = Phase.Idle;
        _runningRoutine = null;
        Log($"{LogPrefix} Ended.");
    }

    private IEnumerator PlayTelegraphRoutine()
    {
        float duration = Mathf.Max(0f, telegraphDuration);
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
        float duration = Mathf.Max(0f, launchDuration);
        if (duration <= 0f)
        {
            SetKeysToLandingTargets(rotateWhileFlying ? flyingRotationSpeed * duration : 0f);
            yield break;
        }

        float elapsed = 0f;
        while (elapsed < duration)
        {
            float t = Mathf.Clamp01(elapsed / duration);
            float smoothT = Mathf.SmoothStep(0f, 1f, t);
            float arcMultiplier = Mathf.Sin(t * Mathf.PI);
            SetKeysToBurstPose(smoothT, arcMultiplier, elapsed);
            elapsed += Time.deltaTime;
            yield return null;
        }

        SetKeysToLandingTargets(rotateWhileFlying ? flyingRotationSpeed * duration : 0f);
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

        float halfX = Mathf.Abs(deskLandingAreaSize.x) * 0.5f;
        float halfZ = Mathf.Abs(deskLandingAreaSize.z) * 0.5f;

        if (halfX <= 0.0001f || halfZ <= 0.0001f)
            return startPosition;

        int attempts = Mathf.Max(1, landingPointPickAttempts);
        float jitter = Mathf.Max(0f, landingPointJitter);
        Vector3 fallbackCandidate = startPosition;
        bool hasFallbackCandidate = false;

        for (int i = 0; i < attempts; i++)
        {
            Vector3 candidate = new Vector3(
                Random.Range(deskLandingAreaCenter.x - halfX, deskLandingAreaCenter.x + halfX),
                startPosition.y,
                Random.Range(deskLandingAreaCenter.z - halfZ, deskLandingAreaCenter.z + halfZ));

            if (jitter > 0f)
            {
                candidate.x += Random.Range(-jitter, jitter);
                candidate.z += Random.Range(-jitter, jitter);
            }

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

    private bool CanUseDeskLandingArea()
    {
        if (!useDeskLandingArea || !IsFiniteVector(deskLandingAreaCenter) || !IsFiniteVector(deskLandingAreaSize))
            return false;

        return Mathf.Abs(deskLandingAreaSize.x) > 0.0001f && Mathf.Abs(deskLandingAreaSize.z) > 0.0001f;
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
        }
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
        int count = GetPreparedKeyCount();
        for (int i = 0; i < count; i++)
        {
            Transform key = GetKeyAt(i);
            if (key == null)
                continue;

            Vector3 startWorldPosition = GetOriginalWorldPosition(i, key);
            Vector3 baseWorldPosition = Vector3.LerpUnclamped(startWorldPosition, _landingWorldPositions[i], t);
            key.position = baseWorldPosition + Vector3.up * (arcMultiplier * GetArcHeight(i));

            if (rotateWhileFlying)
                key.localRotation = _originalLocalRotations[i] * Quaternion.AngleAxis(flyingRotationSpeed * elapsed, _rotationAxes[i]);
        }
    }

    private void SetKeysToLandingTargets(float rotationAngle)
    {
        int count = GetPreparedKeyCount();
        for (int i = 0; i < count; i++)
        {
            Transform key = GetKeyAt(i);
            if (key == null)
                continue;

            key.position = _landingWorldPositions[i];
            key.localRotation = rotateWhileFlying
                ? _originalLocalRotations[i] * Quaternion.AngleAxis(rotationAngle, _rotationAxes[i])
                : _originalLocalRotations[i];
        }
    }

    private void StopRunningAndRestore()
    {
        if (_runningRoutine != null)
        {
            StopCoroutine(_runningRoutine);
            _runningRoutine = null;
        }

        RestoreOriginalTransforms();
        _hitPlayers.Clear();
        _phase = Phase.Idle;
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
        if (!useDeskLandingArea || !IsFiniteVector(deskLandingAreaCenter) || !IsFiniteVector(deskLandingAreaSize))
            return;

        Vector3 gizmoSize = new Vector3(
            Mathf.Abs(deskLandingAreaSize.x),
            0.05f,
            Mathf.Abs(deskLandingAreaSize.z));

        Gizmos.color = new Color(0.15f, 0.75f, 1f, 0.8f);
        Gizmos.DrawWireCube(deskLandingAreaCenter, gizmoSize);
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
