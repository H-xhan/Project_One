using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class LiquidSweepGimmick : MonoBehaviour
{
    private const string LogPrefix = "[LiquidSweepGimmick]";
    private const float MinPositiveValue = 0.01f;
    private const int MaxSweepDebugLogs = 20;
    private const int MaxResidueDebugLogs = 20;
    private const string SourceSweep = "Sweep";
    private const string SourceResidue = "Residue";

    private enum SweepPhase
    {
        Idle,
        Telegraph,
        Sweep,
        Residue,
        Cooldown
    }

    [System.Serializable]
    private struct SweepLane
    {
        [Tooltip("스윕 경로 이름입니다. 디버그와 구분용입니다.")]
        public string name;

        [Tooltip("이 스윕 경로의 시작 위치입니다. 비어 있으면 기본 Sweep Origin을 사용합니다.")]
        public Transform origin;

        [Tooltip("이 스윕 경로의 진행 방향입니다.")]
        public Vector3 direction;

        [Tooltip("이 스윕 경로의 이동 거리입니다. 0 이하이면 기본 Sweep Distance를 사용합니다.")]
        public float distance;

        [Tooltip("이 스윕 경로의 폭입니다. 0 이하이면 기본 Sweep Width를 사용합니다.")]
        public float width;

        [Tooltip("이 스윕 경로의 앞뒤 두께입니다. 0 이하이면 기본 Sweep Depth를 사용합니다.")]
        public float depth;

        [Tooltip("이 스윕 경로의 잔여 미끄럼 구역 중심입니다. 비어 있으면 스윕 경로 중앙을 사용합니다.")]
        public Transform residueCenter;

        [Tooltip("이 스윕 경로의 잔여 미끄럼 구역 크기입니다. X/Z가 0 이하이면 기본 Residue Area Size를 사용합니다.")]
        public Vector3 residueSize;

        [Tooltip("이 경로가 랜덤 선택될 가중치입니다. 0 이하이면 선택되지 않습니다.")]
        public float weight;
    }

    private struct CircularIcePatchRuntimeData
    {
        public Vector3 center;
        public Vector3 visualPosition;
        public Quaternion rotation;
        public float radius;
        public float detectionHeight;
        public GameObject visualInstance;
    }

    [Header("Sweep Setup")]
    [SerializeField, Tooltip("스윕 시작 기준 위치입니다. 비어 있으면 이 오브젝트 위치를 사용합니다.")]
    private Transform sweepOrigin;

    [SerializeField, Tooltip("스윕이 진행되는 월드 방향입니다.")]
    private Vector3 sweepDirection = Vector3.right;

    [SerializeField, Tooltip("스윕이 이동하는 거리입니다.")]
    private float sweepDistance = 12f;

    [SerializeField, Tooltip("스윕 판정 박스의 가로 폭입니다.")]
    private float sweepWidth = 4f;

    [SerializeField, Tooltip("스윕 판정 박스의 높이입니다.")]
    private float sweepHeight = 2f;

    [SerializeField, Tooltip("스윕 판정 박스의 앞뒤 두께입니다.")]
    private float sweepDepth = 1.5f;

    [SerializeField, Tooltip("스윕이 이동하는 시간입니다.")]
    private float sweepDuration = 1.2f;

    [SerializeField, Tooltip("ㄱ자 책상처럼 여러 방향에서 액체 스윕이 발생할 수 있도록 등록하는 스윕 경로 목록입니다. 비어 있으면 기본 단일 스윕 설정을 사용합니다.")]
    private SweepLane[] sweepLanes = System.Array.Empty<SweepLane>();

    [Header("Phase Time")]
    [SerializeField, Tooltip("스윕이 시작되기 전 전조 시간입니다.")]
    private float telegraphDuration = 1f;

    [SerializeField, Tooltip("스윕 종료 후 미끄럼 구역이 유지되는 시간입니다.")]
    private float residueDuration = 3f;

    [SerializeField, Tooltip("기믹 종료 후 다시 실행 가능해지기까지의 대기 시간입니다.")]
    private float cooldownDuration = 1f;

    [Header("Knockback")]
    [SerializeField, Tooltip("스윕에 맞은 플레이어에게 적용할 수평 넉백 힘입니다.")]
    private float knockbackForce = 7f;

    [SerializeField, Tooltip("스윕에 맞은 플레이어에게 적용할 위쪽 힘입니다.")]
    private float upwardForce = 1.5f;

    [SerializeField, Tooltip("플레이어 탐색에 사용할 레이어 마스크입니다.")]
    private LayerMask playerMask;

    [SerializeField, Tooltip("한 번의 스윕 중 같은 플레이어를 한 번만 넉백할지 여부입니다.")]
    private bool hitPlayerOnlyOncePerSweep = true;

    [Header("Residue Slip")]
    [SerializeField, Tooltip("스윕이 지나간 뒤 미끄럼 구역을 사용할지 여부입니다.")]
    private bool enableResidueSlip = true;

    [SerializeField, Tooltip("잔여 미끄럼 구역의 중심 위치입니다. 비어 있으면 스윕 경로 중앙을 사용합니다.")]
    private Transform residueAreaCenter;

    [SerializeField, Tooltip("잔여 미끄럼 구역의 크기입니다.")]
    private Vector3 residueAreaSize = new Vector3(10f, 2f, 4f);

    [SerializeField, Tooltip("잔여 미끄럼 구역에서 플레이어에게 반복 적용할 미끄럼 힘입니다.")]
    private float residueSlipForce = 1.5f;

    [SerializeField, Tooltip("잔여 미끄럼 힘을 적용하는 간격입니다.")]
    private float residueTickInterval = 0.2f;

    [SerializeField, Tooltip("잔여 미끄럼 힘이 적용되는 방향입니다. 비어 있으면 스윕 방향을 사용합니다.")]
    private Vector3 residueSlipDirection = Vector3.zero;

    [Header("Random Circular Ice Patch")]
    [SerializeField, Tooltip("Residue 단계에서 기존 전체 영역 대신 랜덤 원형 얼음 패치 판정을 사용할지 여부입니다.")]
    private bool useRandomCircularIcePatches = true;

    [SerializeField, Tooltip("원형 얼음 패치가 생성될 책상 위 영역들입니다. 비워두면 기존 Residue 영역 방식을 사용합니다.")]
    private BoxCollider[] circularIcePatchSpawnAreas = System.Array.Empty<BoxCollider>();

    [SerializeField, Tooltip("Residue 단계에서 생성할 원형 얼음 패치 개수입니다.")]
    private int circularIcePatchCount = 5;

    [SerializeField, Tooltip("원형 얼음 패치 반지름의 랜덤 범위입니다.")]
    private Vector2 circularIcePatchRadiusRange = new Vector2(0.45f, 0.9f);

    [SerializeField, Tooltip("스폰 영역 표면보다 얼음 패치 판정을 얼마나 위에 둘지 설정합니다.")]
    private float circularIcePatchHeightOffset = 0.05f;

    [SerializeField, Tooltip("원형 얼음 패치가 플레이어를 감지할 세로 판정 높이입니다.")]
    private float circularIcePatchDetectionHeight = 0.6f;

    [SerializeField, Tooltip("랜덤 원형 얼음 패치가 유지되는 시간입니다.")]
    private float circularIcePatchDuration = 15f;

    [SerializeField, Tooltip("원형 얼음 패치가 플레이어 미끄럼 판정을 갱신하는 간격입니다.")]
    private float circularIcePatchTickInterval = 0.15f;

    [SerializeField, Tooltip("원형 얼음 패치에 닿은 플레이어에게 적용할 미끄럼 힘입니다.")]
    private float circularIcePatchSlipForce = 1.0f;

    [SerializeField, Tooltip("원형 얼음 패치 위치를 표시할 선택적 비주얼 프리팹입니다. 비워두면 판정만 적용합니다.")]
    private GameObject circularIcePatchVisualPrefab;

    [SerializeField, Tooltip("원형 얼음 패치 비주얼을 책상 표면 기준으로 얼마나 위아래에 배치할지 설정합니다.")]
    private float circularIcePatchVisualYOffset = 0.01f;

    [SerializeField, Tooltip("같은 플레이어가 원형 얼음 패치 미끄럼 효과를 다시 받기까지의 대기 시간입니다.")]
    private float circularIcePatchSlipCooldown = 2.5f;

    [SerializeField, Tooltip("원형 얼음 패치 위치를 찾기 위해 시도할 최대 횟수입니다.")]
    private int maxCircularIcePatchSpawnAttempts = 32;

    [SerializeField, Tooltip("원형 얼음 패치끼리 너무 가까이 생기지 않도록 하는 최소 중심 거리입니다.")]
    private float minimumCircularIcePatchDistance = 1.0f;

    [SerializeField, Tooltip("원형 얼음 패치 설정이 부족할 때 기존 전체 Residue 방식을 사용할지 여부입니다.")]
    private bool fallbackToFullResidueWhenCircularPatchSetupMissing = true;

    [Header("Visual And Sound")]
    [SerializeField, Tooltip("전조 단계에서 켤 오브젝트입니다.")]
    private GameObject telegraphVisual;

    [SerializeField, Tooltip("스윕 진행 중 켤 오브젝트입니다.")]
    private GameObject sweepVisual;

    [SerializeField, Tooltip("잔여 미끄럼 구역 동안 켤 오브젝트입니다.")]
    private GameObject residueVisual;

    [SerializeField, Tooltip("스윕 중 sweepVisual을 현재 스윕 판정 박스 위치와 회전에 맞춰 이동시킬지 여부입니다.")]
    private bool alignSweepVisualToSweepBox = true;

    [SerializeField, Tooltip("스윕 비주얼을 판정 박스 크기에 맞춰 자동 스케일할지 여부입니다.")]
    private bool autoScaleSweepVisual = true;

    [SerializeField, Tooltip("스윕 비주얼의 Y 위치를 판정 박스 중심에서 추가로 보정하는 값입니다.")]
    private float sweepVisualYOffset = 0.02f;

    [SerializeField, Tooltip("스윕 비주얼 자동 스케일에 곱할 배율입니다.")]
    private Vector3 sweepVisualScaleMultiplier = new Vector3(1f, 0.02f, 1f);

    [SerializeField, Tooltip("잔여 비주얼을 잔여 미끄럼 구역 위치와 크기에 맞춰 자동 배치할지 여부입니다.")]
    private bool alignResidueVisualToResidueArea = true;

    [SerializeField, Tooltip("잔여 비주얼의 Y 위치를 잔여 구역 중심에서 추가로 보정하는 값입니다.")]
    private float residueVisualYOffset = 0.02f;

    [SerializeField, Tooltip("잔여 비주얼 자동 스케일에 곱할 배율입니다.")]
    private Vector3 residueVisualScaleMultiplier = new Vector3(1f, 0.02f, 1f);

    [SerializeField, Tooltip("전조 시작 시 재생할 오디오 소스입니다.")]
    private AudioSource telegraphAudio;

    [SerializeField, Tooltip("스윕 시작 시 재생할 오디오 소스입니다.")]
    private AudioSource sweepAudio;

    [SerializeField, Tooltip("잔여 구역 시작 시 재생할 오디오 소스입니다.")]
    private AudioSource residueAudio;

    [Header("Debug")]
    [SerializeField, Tooltip("디버그 로그를 출력할지 여부입니다.")]
    private bool enableDebugLogs = false;

    [SerializeField, Tooltip("Scene View에서 스윕/잔여 구역 기즈모를 표시할지 여부입니다.")]
    private bool drawGizmos = true;

    private readonly HashSet<PlayerStatusModule> _sweepHitPlayers = new HashSet<PlayerStatusModule>();
    private readonly HashSet<PlayerStatusModule> _sweepTickPlayers = new HashSet<PlayerStatusModule>();
    private readonly HashSet<PlayerStatusModule> _residueTickPlayers = new HashSet<PlayerStatusModule>();
    private readonly List<GameObject> _activeCircularIcePatchVisuals = new List<GameObject>();
    private readonly Dictionary<PlayerStatusModule, float> _circularIcePatchNextSlipTimes = new Dictionary<PlayerStatusModule, float>();
    private Coroutine _runningRoutine;
    private SweepPhase _phase = SweepPhase.Idle;
    private bool _loggedEmptyPlayerMaskWarning;
    private int _sweepDebugLogCount;
    private int _residueDebugLogCount;
    private bool _hasActiveLane;
    private SweepLane _activeLane;

    [ContextMenu("Debug Start Liquid Sweep")]
    private void DebugStartLiquidSweep()
    {
        if (!Application.isPlaying)
        {
            LogWarning($"{LogPrefix} Debug start ignored. Application is not playing.");
            return;
        }

        StartLiquidSweep();
    }

    private void StartLiquidSweep()
    {
        if (_phase != SweepPhase.Idle || _runningRoutine != null)
        {
            Log($"{LogPrefix} Start ignored. phase={_phase}");
            return;
        }

        _sweepHitPlayers.Clear();
        _sweepTickPlayers.Clear();
        _residueTickPlayers.Clear();
        _loggedEmptyPlayerMaskWarning = false;
        SelectActiveSweepLane();
        SetPresentationActive(false, false, false);
        _runningRoutine = StartCoroutine(RunLiquidSweepRoutine());
    }

    private IEnumerator RunLiquidSweepRoutine()
    {
        SetPhase(SweepPhase.Telegraph);
        SetPresentationActive(true, false, false);
        PlayAudio(telegraphAudio);
        yield return WaitForSecondsSafe(telegraphDuration);

        SetPhase(SweepPhase.Sweep);
        UpdateSweepVisual(0f);
        SetPresentationActive(false, true, false);
        PlayAudio(sweepAudio);
        yield return RunSweepRoutine();

        SetPhase(SweepPhase.Residue);
        bool shouldUseRandomCircularIcePatches = ShouldUseRandomCircularIcePatches();
        LogCircularIcePatchResidueEntry(shouldUseRandomCircularIcePatches);
        if (shouldUseRandomCircularIcePatches)
        {
            SetPresentationActive(false, false, false);
        }
        else
        {
            UpdateResidueVisual();
            SetPresentationActive(false, false, true);
        }

        PlayAudio(residueAudio);
        yield return RunResidueRoutine();

        SetPhase(SweepPhase.Cooldown);
        SetPresentationActive(false, false, false);
        yield return WaitForSecondsSafe(cooldownDuration);

        SetPhase(SweepPhase.Idle);
        _sweepHitPlayers.Clear();
        _sweepTickPlayers.Clear();
        _residueTickPlayers.Clear();
        ClearActiveSweepLane();
        _runningRoutine = null;
    }

    private IEnumerator RunSweepRoutine()
    {
        float duration = Mathf.Max(MinPositiveValue, sweepDuration);
        float elapsed = 0f;

        while (elapsed < duration)
        {
            float progress = Mathf.Clamp01(elapsed / duration);
            GetSweepBox(progress, out Vector3 center, out Vector3 halfExtents, out Quaternion rotation);
            UpdateSweepVisual(center, halfExtents, rotation);
            ApplySweepHit(center, rotation);

            elapsed += Time.deltaTime;
            yield return null;
        }

        GetSweepBox(1f, out Vector3 endCenter, out Vector3 endHalfExtents, out Quaternion endRotation);
        UpdateSweepVisual(endCenter, endHalfExtents, endRotation);
        ApplySweepHit(endCenter, endRotation);
    }

    private IEnumerator RunResidueRoutine()
    {
        if (ShouldUseRandomCircularIcePatches())
        {
            yield return RunRandomCircularIcePatchResidueRoutine();
            yield break;
        }

        yield return RunFullResidueRoutine();
    }

    private IEnumerator RunFullResidueRoutine()
    {
        if (!enableResidueSlip)
        {
            UpdateResidueVisual();
            SetPresentationActive(false, false, true);
            yield return WaitForSecondsSafe(residueDuration);
            yield break;
        }

        float duration = Mathf.Max(0f, residueDuration);
        float tickInterval = Mathf.Max(MinPositiveValue, residueTickInterval);
        float elapsed = 0f;
        float nextTickTime = 0f;
        GetResidueBox(out Vector3 center, out Vector3 halfExtents, out Quaternion rotation);
        UpdateResidueVisual(center, halfExtents, rotation);
        SetPresentationActive(false, false, true);

        while (elapsed < duration)
        {
            if (elapsed >= nextTickTime)
            {
                ApplyResidueSlip(center, rotation);
                nextTickTime += tickInterval;
            }

            elapsed += Time.deltaTime;
            yield return null;
        }
    }

    private IEnumerator RunRandomCircularIcePatchResidueRoutine()
    {
        ClearCircularIcePatchSlipCooldowns();

        List<CircularIcePatchRuntimeData> patches = new List<CircularIcePatchRuntimeData>(Mathf.Max(0, circularIcePatchCount));
        Log($"{LogPrefix} Circular ice patch build requested. targetCount={circularIcePatchCount}, maxAttempts={maxCircularIcePatchSpawnAttempts}, validSpawnAreas={CountValidCircularIcePatchSpawnAreas()}, visualPrefabNull={circularIcePatchVisualPrefab == null}");
        if (!TryBuildCircularIcePatches(patches))
        {
            CleanupCircularIcePatches(patches);
            Log($"{LogPrefix} Circular ice patch build produced 0 patches. fallbackToFullResidue={fallbackToFullResidueWhenCircularPatchSetupMissing}");

            if (fallbackToFullResidueWhenCircularPatchSetupMissing)
            {
                yield return RunFullResidueRoutine();
                yield break;
            }

            SetPresentationActive(false, false, false);
            yield return WaitForSecondsSafe(Mathf.Max(0f, GetFiniteFloatOrZero(circularIcePatchDuration)));
            yield break;
        }

        SetPresentationActive(false, false, false);

        float duration = Mathf.Max(0f, GetFiniteFloatOrZero(circularIcePatchDuration));
        float tickInterval = Mathf.Max(0.02f, GetFiniteFloatOrZero(circularIcePatchTickInterval));
        float elapsed = 0f;
        float nextTickTime = 0f;

        try
        {
            while (elapsed < duration)
            {
                if (elapsed >= nextTickTime)
                {
                    TickCircularIcePatches(patches);
                    nextTickTime += tickInterval;
                }

                elapsed += Time.deltaTime;
                yield return null;
            }
        }
        finally
        {
            CleanupCircularIcePatches(patches);
            ClearCircularIcePatchSlipCooldowns();
        }
    }

    private void ApplySweepHit(Vector3 center, Quaternion rotation)
    {
        _sweepTickPlayers.Clear();
        Vector3 halfExtents = GetSweepHalfExtents();
        int mask = GetPlayerMask();

        Collider[] colliders = Physics.OverlapBox(
            center,
            halfExtents,
            rotation,
            mask,
            QueryTriggerInteraction.Ignore);
        LogSourceDebug(SourceSweep, $"OverlapBox center={center}, half={halfExtents}, rotation={rotation.eulerAngles}, hits={colliders.Length}, mask={mask}");

        Vector3 direction = GetSweepDirection();
        Vector3 impulse = direction * Mathf.Max(0f, knockbackForce) + Vector3.up * Mathf.Max(0f, upwardForce);

        for (int i = 0; i < colliders.Length; i++)
        {
            PlayerStatusModule status = FindPlayerStatusFromCollider(colliders[i], SourceSweep);
            if (!IsValidTarget(status))
            {
                LogSourceDebug(SourceSweep, $"Skip invalid target. {FormatStatusState(status)}");
                continue;
            }

            if (_sweepTickPlayers.Contains(status))
            {
                LogSourceDebug(SourceSweep, $"Skip duplicate status={status.name}, reason=same tick");
                continue;
            }

            if (hitPlayerOnlyOncePerSweep && _sweepHitPlayers.Contains(status))
            {
                LogSourceDebug(SourceSweep, $"Skip duplicate status={status.name}, reason=already hit this sweep, hitPlayerOnlyOncePerSweep={hitPlayerOnlyOncePerSweep}");
                continue;
            }

            LogSourceDebug(SourceSweep, $"Applying knockback to {status.name} impulse={impulse}, hitPlayerOnlyOncePerSweep={hitPlayerOnlyOncePerSweep}, {FormatStatusState(status)}");
            status.ApplyKnockbackServer(impulse);
            LogSourceDebug(SourceSweep, $"Applied knockback to {status.name}");
            _sweepTickPlayers.Add(status);
            _sweepHitPlayers.Add(status);
            Log($"{LogPrefix} Sweep hit player: {status.name}, impulse:{impulse}");
        }
    }

    private void ApplyResidueSlip(Vector3 center, Quaternion rotation)
    {
        _residueTickPlayers.Clear();
        Vector3 halfExtents = GetResidueHalfExtents();
        int mask = GetPlayerMask();

        Collider[] colliders = Physics.OverlapBox(
            center,
            halfExtents,
            rotation,
            mask,
            QueryTriggerInteraction.Ignore);
        LogSourceDebug(SourceResidue, $"OverlapBox center={center}, half={halfExtents}, rotation={rotation.eulerAngles}, hits={colliders.Length}, mask={mask}");

        Vector3 slipDirection = GetResidueSlipDirection();
        Vector3 slipImpulse = slipDirection * Mathf.Max(0f, residueSlipForce);

        for (int i = 0; i < colliders.Length; i++)
        {
            PlayerStatusModule status = FindPlayerStatusFromCollider(colliders[i], SourceResidue);
            if (!IsValidTarget(status))
            {
                LogSourceDebug(SourceResidue, $"Skip invalid target. {FormatStatusState(status)}");
                continue;
            }

            if (_residueTickPlayers.Contains(status))
            {
                LogSourceDebug(SourceResidue, $"Skip duplicate status={status.name}, reason=same residue tick");
                continue;
            }

            LogSourceDebug(SourceResidue, $"Applying knockback to {status.name} impulse={slipImpulse}, {FormatStatusState(status)}");
            status.ApplyKnockbackServer(slipImpulse);
            LogSourceDebug(SourceResidue, $"Applied knockback to {status.name}");
            _residueTickPlayers.Add(status);
        }
    }

    private bool ShouldUseRandomCircularIcePatches()
    {
        if (!enableResidueSlip)
            return false;

        if (!useRandomCircularIcePatches)
            return false;

        if (circularIcePatchCount <= 0)
            return false;

        if (!IsFiniteFloat(circularIcePatchRadiusRange.y) || circularIcePatchRadiusRange.y <= 0f)
            return false;

        return HasValidCircularIcePatchSpawnArea();
    }

    private void LogCircularIcePatchResidueEntry(bool shouldUseRandomCircularIcePatches)
    {
        Log($"{LogPrefix} Circular ice patch residue entry. useRandomCircularIcePatches={useRandomCircularIcePatches}, enableResidueSlip={enableResidueSlip}, circularIcePatchCount={circularIcePatchCount}, validSpawnAreas={CountValidCircularIcePatchSpawnAreas()}, shouldUseRandomCircularIcePatches={shouldUseRandomCircularIcePatches}, visualPrefabNull={circularIcePatchVisualPrefab == null}");
    }

    private bool TryBuildCircularIcePatches(List<CircularIcePatchRuntimeData> patches)
    {
        if (patches == null)
            return false;

        patches.Clear();

        if (!ShouldUseRandomCircularIcePatches())
            return false;

        int targetCount = Mathf.Max(0, circularIcePatchCount);
        int attemptCount = Mathf.Max(1, maxCircularIcePatchSpawnAttempts);
        for (int i = 0; i < attemptCount && patches.Count < targetCount; i++)
        {
            if (!TryGetRandomCircularIcePatchPose(out Vector3 center, out Quaternion rotation, out float radius))
                continue;

            if (!IsFarEnoughFromCircularIcePatches(patches, center))
                continue;

            Vector3 visualPosition = GetCircularIcePatchVisualPosition(center);
            GameObject visualInstance = CreateCircularIcePatchVisual(center, visualPosition, rotation, radius);
            patches.Add(new CircularIcePatchRuntimeData
            {
                center = center,
                visualPosition = visualPosition,
                rotation = rotation,
                radius = radius,
                detectionHeight = Mathf.Max(0.05f, GetFiniteFloatOrZero(circularIcePatchDetectionHeight)),
                visualInstance = visualInstance
            });
            Log($"{LogPrefix} Circular ice patch created. index={patches.Count - 1}, center={center}, visualPosition={visualPosition}, radius={radius:0.###}, visualCreated={visualInstance != null}");
        }

        Log($"{LogPrefix} Circular ice patch build finished. created={patches.Count}, target={targetCount}, attempts={attemptCount}");
        return patches.Count > 0;
    }

    private bool TryGetRandomCircularIcePatchPose(out Vector3 center, out Quaternion rotation, out float radius)
    {
        center = Vector3.zero;
        rotation = Quaternion.identity;
        radius = 0f;

        if (!TryGetRandomCircularIcePatchSpawnArea(out BoxCollider spawnArea))
            return false;

        float minRadius = Mathf.Max(0f, GetFiniteFloatOrZero(circularIcePatchRadiusRange.x));
        float maxRadius = Mathf.Max(minRadius, GetFiniteFloatOrZero(circularIcePatchRadiusRange.y));
        if (!IsFiniteFloat(maxRadius) || maxRadius <= 0f)
            return false;

        radius = Random.Range(minRadius, maxRadius);
        if (!IsFiniteFloat(radius) || radius <= 0f)
            radius = maxRadius;

        Vector3 areaCenter = spawnArea.center;
        Vector3 areaSize = spawnArea.size;
        float halfWidth = Mathf.Abs(areaSize.x) * 0.5f;
        float halfDepth = Mathf.Abs(areaSize.z) * 0.5f;
        Vector3 localPosition = new Vector3(
            Random.Range(areaCenter.x - halfWidth, areaCenter.x + halfWidth),
            areaCenter.y + areaSize.y * 0.5f + GetFiniteFloatOrZero(circularIcePatchHeightOffset),
            Random.Range(areaCenter.z - halfDepth, areaCenter.z + halfDepth));

        center = spawnArea.transform.TransformPoint(localPosition);
        rotation = spawnArea.transform.rotation;
        return IsFiniteVector(center) && IsFiniteQuaternion(rotation);
    }

    private bool TryGetRandomCircularIcePatchSpawnArea(out BoxCollider spawnArea)
    {
        spawnArea = null;

        if (circularIcePatchSpawnAreas == null || circularIcePatchSpawnAreas.Length == 0)
            return false;

        int validCount = 0;
        for (int i = 0; i < circularIcePatchSpawnAreas.Length; i++)
        {
            if (IsValidCircularIcePatchSpawnArea(circularIcePatchSpawnAreas[i]))
                validCount++;
        }

        if (validCount <= 0)
            return false;

        int selectedIndex = Random.Range(0, validCount);
        for (int i = 0; i < circularIcePatchSpawnAreas.Length; i++)
        {
            BoxCollider candidate = circularIcePatchSpawnAreas[i];
            if (!IsValidCircularIcePatchSpawnArea(candidate))
                continue;

            if (selectedIndex == 0)
            {
                spawnArea = candidate;
                return true;
            }

            selectedIndex--;
        }

        return false;
    }

    private bool HasValidCircularIcePatchSpawnArea()
    {
        return CountValidCircularIcePatchSpawnAreas() > 0;
    }

    private int CountValidCircularIcePatchSpawnAreas()
    {
        if (circularIcePatchSpawnAreas == null || circularIcePatchSpawnAreas.Length == 0)
            return 0;

        int validCount = 0;
        for (int i = 0; i < circularIcePatchSpawnAreas.Length; i++)
        {
            if (IsValidCircularIcePatchSpawnArea(circularIcePatchSpawnAreas[i]))
                validCount++;
        }

        return validCount;
    }

    private static bool IsValidCircularIcePatchSpawnArea(BoxCollider spawnArea)
    {
        if (spawnArea == null || !spawnArea.enabled || !spawnArea.gameObject.activeInHierarchy)
            return false;

        if (!IsFiniteVector(spawnArea.center) || !IsFiniteVector(spawnArea.size))
            return false;

        return Mathf.Abs(spawnArea.size.x) > 0.0001f && Mathf.Abs(spawnArea.size.z) > 0.0001f;
    }

    private bool IsFarEnoughFromCircularIcePatches(List<CircularIcePatchRuntimeData> patches, Vector3 center)
    {
        float minimumDistance = Mathf.Max(0f, GetFiniteFloatOrZero(minimumCircularIcePatchDistance));
        if (minimumDistance <= 0f || patches == null)
            return true;

        float minimumSqrDistance = minimumDistance * minimumDistance;
        for (int i = 0; i < patches.Count; i++)
        {
            Vector3 delta = patches[i].center - center;
            delta.y = 0f;
            if (delta.sqrMagnitude < minimumSqrDistance)
                return false;
        }

        return true;
    }

    private GameObject CreateCircularIcePatchVisual(Vector3 center, Vector3 visualPosition, Quaternion rotation, float radius)
    {
        if (circularIcePatchVisualPrefab == null)
        {
            Log($"{LogPrefix} Circular ice patch visual skipped. prefabNull=True, center={center}, visualPosition={visualPosition}, radius={radius:0.###}");
            return null;
        }

        GameObject visualInstance = Instantiate(circularIcePatchVisualPrefab, visualPosition, rotation);
        ApplyCircularIcePatchVisualTransform(visualInstance, visualPosition, rotation, radius);

        _activeCircularIcePatchVisuals.Add(visualInstance);
        float diameter = Mathf.Max(0f, radius) * 2f;
        Log($"{LogPrefix} Circular ice patch visual instantiated. success={visualInstance != null}, prefabNull=False, center={center}, visualPosition={visualPosition}, patchCenterY={center.y:0.###}, circularIcePatchHeightOffset={GetFiniteFloatOrZero(circularIcePatchHeightOffset):0.###}, circularIcePatchVisualYOffset={GetFiniteFloatOrZero(circularIcePatchVisualYOffset):0.###}, finalVisualY={visualInstance.transform.position.y:0.###}, radius={radius:0.###}, diameter={diameter:0.###}");
        StartCoroutine(ReapplyCircularIcePatchVisualPositionNextFrame(visualInstance, visualPosition, rotation));
        return visualInstance;
    }

    private void ApplyCircularIcePatchVisualTransform(GameObject visualInstance, Vector3 visualPosition, Quaternion rotation, float radius)
    {
        if (visualInstance == null)
            return;

        Transform visualTransform = visualInstance.transform;
        visualTransform.SetPositionAndRotation(visualPosition, rotation);

        Vector3 scale = visualTransform.localScale;
        float diameter = Mathf.Max(0f, radius) * 2f;
        scale.x = diameter;
        scale.z = diameter;

        if (IsFiniteVector(scale))
            visualTransform.localScale = scale;

        visualTransform.SetPositionAndRotation(visualPosition, rotation);
    }

    private IEnumerator ReapplyCircularIcePatchVisualPositionNextFrame(GameObject visualInstance, Vector3 visualPosition, Quaternion rotation)
    {
        yield return null;

        if (visualInstance == null)
            yield break;

        visualInstance.transform.SetPositionAndRotation(visualPosition, rotation);
    }

    private Vector3 GetCircularIcePatchVisualPosition(Vector3 patchCenter)
    {
        Vector3 visualPosition = patchCenter;
        visualPosition.y = patchCenter.y - GetFiniteFloatOrZero(circularIcePatchHeightOffset) + GetFiniteFloatOrZero(circularIcePatchVisualYOffset);

        return IsFiniteVector(visualPosition) ? visualPosition : patchCenter;
    }

    private void TickCircularIcePatches(List<CircularIcePatchRuntimeData> patches)
    {
        _residueTickPlayers.Clear();

        if (patches == null || patches.Count == 0)
            return;

        for (int i = 0; i < patches.Count; i++)
        {
            ApplyCircularIcePatchSlip(patches[i], _residueTickPlayers);
        }
    }

    private void ApplyCircularIcePatchSlip(CircularIcePatchRuntimeData patch, HashSet<PlayerStatusModule> affectedPlayersThisTick)
    {
        if (affectedPlayersThisTick == null)
            return;

        float radius = Mathf.Max(0f, patch.radius);
        if (radius <= 0f)
            return;

        float halfHeight = Mathf.Max(0.05f, patch.detectionHeight) * 0.5f;
        Vector3 halfExtents = new Vector3(radius, halfHeight, radius);
        int mask = GetPlayerMask();
        Collider[] colliders = Physics.OverlapBox(
            patch.center,
            halfExtents,
            patch.rotation,
            mask,
            QueryTriggerInteraction.Ignore);

        Vector3 slipDirection = GetResidueSlipDirection();
        Vector3 slipImpulse = slipDirection * Mathf.Max(0f, GetFiniteFloatOrZero(circularIcePatchSlipForce));
        int appliedCount = 0;

        for (int i = 0; i < colliders.Length; i++)
        {
            Collider candidate = colliders[i];
            Vector3 testPoint = GetCircularIcePatchTestPoint(candidate, patch.center);
            float xzDistance = GetHorizontalDistance(testPoint, patch.center);
            bool isInsideCircle = IsInsideCircularIcePatch(testPoint, patch.center, radius);
            PlayerStatusModule status = FindPlayerStatusFromCollider(candidate, SourceResidue);
            if (!IsValidTarget(status))
            {
                LogCircularIcePatchCandidate(candidate, status, testPoint, xzDistance, radius, isInsideCircle, false, "invalid target");
                continue;
            }

            if (affectedPlayersThisTick.Contains(status))
            {
                LogCircularIcePatchCandidate(candidate, status, testPoint, xzDistance, radius, isInsideCircle, false, "already affected this tick");
                continue;
            }

            if (!isInsideCircle)
            {
                LogCircularIcePatchCandidate(candidate, status, testPoint, xzDistance, radius, false, false, "outside circle");
                continue;
            }

            if (IsCircularIcePatchSlipOnCooldown(status, out float cooldownRemaining))
            {
                LogCircularIcePatchCandidate(candidate, status, testPoint, xzDistance, radius, true, false, $"slip cooldown {cooldownRemaining:0.##}s");
                continue;
            }

            status.ApplyKnockbackServer(slipImpulse);
            affectedPlayersThisTick.Add(status);
            SetCircularIcePatchSlipCooldown(status);
            appliedCount++;
            LogCircularIcePatchCandidate(candidate, status, testPoint, xzDistance, radius, true, true, "applied");
        }

        Log($"{LogPrefix} Circular ice patch tick. center={patch.center}, radius={radius:0.###}, candidateCount={colliders.Length}, appliedInCircleCount={appliedCount}");
    }

    private void LogCircularIcePatchCandidate(Collider candidate, PlayerStatusModule status, Vector3 testPoint, float xzDistance, float radius, bool insideCircle, bool applied, string reason)
    {
        Log($"{LogPrefix} Circular ice patch candidate. collider={(candidate != null ? candidate.name : "null")}, status={(status != null ? status.name : "null")}, testPoint={testPoint}, xzDistance={xzDistance:0.###}, radius={radius:0.###}, inside={insideCircle}, applied={applied}, reason={reason}");
    }

    private bool IsCircularIcePatchSlipOnCooldown(PlayerStatusModule status, out float remainingTime)
    {
        remainingTime = 0f;
        if (status == null)
            return false;

        if (!_circularIcePatchNextSlipTimes.TryGetValue(status, out float nextAllowedTime))
            return false;

        float now = Time.time;
        if (now >= nextAllowedTime)
        {
            _circularIcePatchNextSlipTimes.Remove(status);
            return false;
        }

        remainingTime = nextAllowedTime - now;
        return true;
    }

    private void SetCircularIcePatchSlipCooldown(PlayerStatusModule status)
    {
        if (status == null)
            return;

        float cooldown = Mathf.Max(0f, GetFiniteFloatOrZero(circularIcePatchSlipCooldown));
        if (cooldown <= 0f)
        {
            _circularIcePatchNextSlipTimes.Remove(status);
            return;
        }

        _circularIcePatchNextSlipTimes[status] = Time.time + cooldown;
    }

    private void ClearCircularIcePatchSlipCooldowns()
    {
        _circularIcePatchNextSlipTimes.Clear();
    }

    private static Vector3 GetCircularIcePatchTestPoint(Collider candidate, Vector3 patchCenter)
    {
        if (candidate == null)
            return patchCenter;

        Vector3 closestPoint = candidate.ClosestPoint(patchCenter);
        if (IsUsableCircularIcePatchTestPoint(closestPoint, patchCenter))
            return closestPoint;

        return candidate.bounds.center;
    }

    private static bool IsUsableCircularIcePatchTestPoint(Vector3 point, Vector3 patchCenter)
    {
        if (!IsFiniteVector(point))
            return false;

        float deltaX = point.x - patchCenter.x;
        float deltaY = point.y - patchCenter.y;
        float deltaZ = point.z - patchCenter.z;
        return deltaX * deltaX + deltaY * deltaY + deltaZ * deltaZ > 0.000001f;
    }

    private static bool IsInsideCircularIcePatch(Vector3 testPoint, Vector3 patchCenter, float radius)
    {
        if (!IsFiniteVector(testPoint) || !IsFiniteVector(patchCenter) || !IsFiniteFloat(radius) || radius <= 0f)
            return false;

        float deltaX = testPoint.x - patchCenter.x;
        float deltaZ = testPoint.z - patchCenter.z;
        return deltaX * deltaX + deltaZ * deltaZ <= radius * radius;
    }

    private static float GetHorizontalDistance(Vector3 a, Vector3 b)
    {
        if (!IsFiniteVector(a) || !IsFiniteVector(b))
            return float.PositiveInfinity;

        float deltaX = a.x - b.x;
        float deltaZ = a.z - b.z;
        return Mathf.Sqrt(deltaX * deltaX + deltaZ * deltaZ);
    }

    private void CleanupCircularIcePatches(List<CircularIcePatchRuntimeData> patches)
    {
        if (patches == null)
            return;

        for (int i = 0; i < patches.Count; i++)
        {
            GameObject visualInstance = patches[i].visualInstance;
            if (visualInstance == null)
                continue;

            _activeCircularIcePatchVisuals.Remove(visualInstance);
            Destroy(visualInstance);
        }

        patches.Clear();
    }

    private void CleanupActiveCircularIcePatchVisuals()
    {
        for (int i = _activeCircularIcePatchVisuals.Count - 1; i >= 0; i--)
        {
            GameObject visualInstance = _activeCircularIcePatchVisuals[i];
            if (visualInstance != null)
                Destroy(visualInstance);
        }

        _activeCircularIcePatchVisuals.Clear();
    }

    private PlayerStatusModule FindPlayerStatusFromCollider(Collider hit, string source)
    {
        if (hit == null)
        {
            LogSourceDebug(source, "Hit collider is null. statusFound=False");
            return null;
        }

        PlayerStatusModule parentStatus = hit.GetComponentInParent<PlayerStatusModule>();
        PlayerStatusModule selfStatus = hit.GetComponent<PlayerStatusModule>();
        PlayerHub hub = hit.GetComponentInParent<PlayerHub>();
        PlayerStatusModule hubStatus = hub != null ? hub.GetComponentInChildren<PlayerStatusModule>(true) : null;
        Rigidbody attachedRigidbody = hit.attachedRigidbody;
        PlayerHub rbHub = attachedRigidbody != null ? attachedRigidbody.GetComponentInParent<PlayerHub>() : null;
        PlayerStatusModule rbHubStatus = rbHub != null ? rbHub.GetComponentInChildren<PlayerStatusModule>(true) : null;
        Transform root = hit.transform.root;
        PlayerStatusModule rootStatus = root != null ? root.GetComponentInChildren<PlayerStatusModule>(true) : null;
        PlayerStatusModule status = parentStatus != null
            ? parentStatus
            : selfStatus != null
                ? selfStatus
                : hubStatus != null
                    ? hubStatus
                    : rbHubStatus != null
                        ? rbHubStatus
                        : rootStatus;
        int layer = hit.gameObject.layer;
        string layerName = LayerMask.LayerToName(layer);
        if (string.IsNullOrEmpty(layerName))
            layerName = "Unnamed";

        LogSourceDebug(
            source,
            $"Hit collider={hit.name}, colliderType={hit.GetType().Name}, object={hit.gameObject.name}, layer={layerName}({layer}), trigger={hit.isTrigger}, rb={(attachedRigidbody != null ? attachedRigidbody.name : "null")}, parentStatusFound={parentStatus != null}, selfStatusFound={selfStatus != null}, hubStatusFound={hubStatus != null}, rbHubStatusFound={rbHubStatus != null}, rootStatusFound={rootStatus != null}, statusFound={status != null}");
        return status;
    }

    private static bool IsValidTarget(PlayerStatusModule status)
    {
        if (status == null)
            return false;

        if (!status.IsSpawned)
            return false;

        return !status.IsEliminated && !status.IsKnocked && !status.IsStandingUp;
    }

    private void SelectActiveSweepLane()
    {
        if (TryPickSweepLane(out _activeLane))
        {
            _hasActiveLane = true;
            Log($"{LogPrefix} Selected sweep lane: {GetLaneName(_activeLane)}");
            return;
        }

        ClearActiveSweepLane();
        Log($"{LogPrefix} No valid sweep lane. Using default sweep setup.");
    }

    private void ClearActiveSweepLane()
    {
        _hasActiveLane = false;
        _activeLane = default(SweepLane);
    }

    private bool TryPickSweepLane(out SweepLane lane)
    {
        lane = default(SweepLane);

        if (sweepLanes == null || sweepLanes.Length == 0)
            return false;

        float totalWeight = 0f;
        for (int i = 0; i < sweepLanes.Length; i++)
        {
            if (!IsValidSweepLane(sweepLanes[i]))
                continue;

            totalWeight += sweepLanes[i].weight;
        }

        if (!IsFiniteFloat(totalWeight) || totalWeight <= 0f)
            return false;

        float pick = Random.Range(0f, totalWeight);
        for (int i = 0; i < sweepLanes.Length; i++)
        {
            SweepLane candidate = sweepLanes[i];
            if (!IsValidSweepLane(candidate))
                continue;

            pick -= candidate.weight;
            if (pick <= 0f)
            {
                lane = candidate;
                return true;
            }
        }

        for (int i = sweepLanes.Length - 1; i >= 0; i--)
        {
            if (!IsValidSweepLane(sweepLanes[i]))
                continue;

            lane = sweepLanes[i];
            return true;
        }

        return false;
    }

    private bool IsValidSweepLane(SweepLane lane)
    {
        if (!IsFiniteFloat(lane.weight) || lane.weight <= 0f)
            return false;

        return HasUsableSweepLaneDirection(lane);
    }

    private bool HasUsableSweepLaneDirection(SweepLane lane)
    {
        return IsUsableHorizontalDirection(lane.direction) || IsUsableHorizontalDirection(sweepDirection);
    }

    private Vector3 GetSweepOriginPosition()
    {
        return _hasActiveLane ? GetSweepOriginPosition(_activeLane) : GetDefaultSweepOriginPosition();
    }

    private Vector3 GetSweepOriginPosition(SweepLane lane)
    {
        return lane.origin != null ? lane.origin.position : GetDefaultSweepOriginPosition();
    }

    private Vector3 GetDefaultSweepOriginPosition()
    {
        return sweepOrigin != null ? sweepOrigin.position : transform.position;
    }

    private Vector3 GetSweepDirection()
    {
        return _hasActiveLane ? GetSweepDirection(_activeLane) : GetDefaultSweepDirection();
    }

    private Vector3 GetSweepDirection(SweepLane lane)
    {
        if (IsUsableHorizontalDirection(lane.direction))
            return NormalizeHorizontalDirection(lane.direction);

        return GetDefaultSweepDirection();
    }

    private Vector3 GetDefaultSweepDirection()
    {
        if (IsUsableHorizontalDirection(sweepDirection))
            return NormalizeHorizontalDirection(sweepDirection);

        return Vector3.right;
    }

    private Vector3 GetResidueSlipDirection()
    {
        Vector3 direction = residueSlipDirection;
        direction.y = 0f;

        if (!IsUsableHorizontalDirection(direction))
            direction = GetSweepDirection();

        return direction.normalized;
    }

    private Vector3 GetSweepCenter(float progress)
    {
        Vector3 origin = GetSweepOriginPosition();
        Vector3 direction = GetSweepDirection();
        float distance = Mathf.Max(0f, GetSweepDistance());
        return origin + direction * (distance * Mathf.Clamp01(progress));
    }

    private Vector3 GetSweepCenter(SweepLane lane, float progress)
    {
        Vector3 origin = GetSweepOriginPosition(lane);
        Vector3 direction = GetSweepDirection(lane);
        float distance = Mathf.Max(0f, GetSweepDistance(lane));
        return origin + direction * (distance * Mathf.Clamp01(progress));
    }

    private Vector3 GetResidueCenter()
    {
        if (_hasActiveLane)
            return GetResidueCenter(_activeLane);

        if (residueAreaCenter != null)
            return residueAreaCenter.position;

        return GetSweepOriginPosition() + GetSweepDirection() * (Mathf.Max(0f, GetSweepDistance()) * 0.5f);
    }

    private Vector3 GetResidueCenter(SweepLane lane)
    {
        if (lane.residueCenter != null)
            return lane.residueCenter.position;

        if (residueAreaCenter != null)
            return residueAreaCenter.position;

        return GetSweepOriginPosition(lane) + GetSweepDirection(lane) * (Mathf.Max(0f, GetSweepDistance(lane)) * 0.5f);
    }

    private void GetSweepBox(float progress, out Vector3 center, out Vector3 halfExtents, out Quaternion rotation)
    {
        center = GetSweepCenter(progress);
        halfExtents = GetSweepHalfExtents();
        rotation = GetSweepRotation();
    }

    private void GetSweepBox(SweepLane lane, float progress, out Vector3 center, out Vector3 halfExtents, out Quaternion rotation)
    {
        center = GetSweepCenter(lane, progress);
        halfExtents = GetSweepHalfExtents(lane);
        rotation = GetSweepRotation(lane);
    }

    private void GetResidueBox(out Vector3 center, out Vector3 halfExtents, out Quaternion rotation)
    {
        center = GetResidueCenter();
        halfExtents = GetResidueHalfExtents();
        rotation = GetSweepRotation();
    }

    private void GetResidueBox(SweepLane lane, out Vector3 center, out Vector3 halfExtents, out Quaternion rotation)
    {
        center = GetResidueCenter(lane);
        halfExtents = GetResidueHalfExtents(lane);
        rotation = GetSweepRotation(lane);
    }

    private Quaternion GetSweepRotation()
    {
        Vector3 direction = GetSweepDirection();
        return Quaternion.LookRotation(direction, Vector3.up);
    }

    private Quaternion GetSweepRotation(SweepLane lane)
    {
        Vector3 direction = GetSweepDirection(lane);
        return Quaternion.LookRotation(direction, Vector3.up);
    }

    private Vector3 GetSweepHalfExtents()
    {
        return new Vector3(
            Mathf.Max(MinPositiveValue, GetSweepWidth()) * 0.5f,
            Mathf.Max(MinPositiveValue, sweepHeight) * 0.5f,
            Mathf.Max(MinPositiveValue, GetSweepDepth()) * 0.5f);
    }

    private Vector3 GetSweepHalfExtents(SweepLane lane)
    {
        return new Vector3(
            Mathf.Max(MinPositiveValue, GetSweepWidth(lane)) * 0.5f,
            Mathf.Max(MinPositiveValue, sweepHeight) * 0.5f,
            Mathf.Max(MinPositiveValue, GetSweepDepth(lane)) * 0.5f);
    }

    private float GetSweepDistance()
    {
        return _hasActiveLane ? GetSweepDistance(_activeLane) : sweepDistance;
    }

    private float GetSweepDistance(SweepLane lane)
    {
        return IsPositiveFinite(lane.distance) ? lane.distance : sweepDistance;
    }

    private float GetSweepWidth()
    {
        return _hasActiveLane ? GetSweepWidth(_activeLane) : sweepWidth;
    }

    private float GetSweepWidth(SweepLane lane)
    {
        return IsPositiveFinite(lane.width) ? lane.width : sweepWidth;
    }

    private float GetSweepDepth()
    {
        return _hasActiveLane ? GetSweepDepth(_activeLane) : sweepDepth;
    }

    private float GetSweepDepth(SweepLane lane)
    {
        return IsPositiveFinite(lane.depth) ? lane.depth : sweepDepth;
    }

    private Vector3 GetResidueHalfExtents()
    {
        Vector3 size = _hasActiveLane ? GetResidueSize(_activeLane) : residueAreaSize;
        return GetResidueHalfExtents(size);
    }

    private Vector3 GetResidueHalfExtents(SweepLane lane)
    {
        return GetResidueHalfExtents(GetResidueSize(lane));
    }

    private Vector3 GetResidueHalfExtents(Vector3 size)
    {
        return new Vector3(
            Mathf.Max(MinPositiveValue, size.x) * 0.5f,
            Mathf.Max(MinPositiveValue, size.y) * 0.5f,
            Mathf.Max(MinPositiveValue, size.z) * 0.5f);
    }

    private Vector3 GetResidueSize(SweepLane lane)
    {
        if (IsPositiveFinite(lane.residueSize.x) && IsPositiveFinite(lane.residueSize.z))
        {
            Vector3 size = lane.residueSize;
            if (!IsPositiveFinite(size.y))
                size.y = residueAreaSize.y;

            return size;
        }

        return residueAreaSize;
    }

    private int GetPlayerMask()
    {
        if (playerMask.value != 0)
            return playerMask.value;

        if (!_loggedEmptyPlayerMaskWarning)
        {
            LogWarning($"{LogPrefix} Player Mask is empty. Overlap checks will use every layer.");
            _loggedEmptyPlayerMaskWarning = true;
        }

        return ~0;
    }

    private void SetPhase(SweepPhase nextPhase)
    {
        if (nextPhase == SweepPhase.Sweep)
            _sweepDebugLogCount = 0;
        else if (nextPhase == SweepPhase.Residue)
            _residueDebugLogCount = 0;

        _phase = nextPhase;
        Log($"{LogPrefix} Phase changed: {_phase}");
    }

    private void SetPresentationActive(bool telegraphActive, bool sweepActive, bool residueActive)
    {
        if (telegraphVisual != null)
            telegraphVisual.SetActive(telegraphActive);

        if (sweepVisual != null)
            sweepVisual.SetActive(sweepActive);

        if (residueVisual != null)
            residueVisual.SetActive(residueActive);
    }

    private void UpdateSweepVisual(float progress)
    {
        GetSweepBox(progress, out Vector3 center, out Vector3 halfExtents, out Quaternion rotation);
        UpdateSweepVisual(center, halfExtents, rotation);
    }

    private void UpdateSweepVisual(Vector3 center, Vector3 halfExtents, Quaternion rotation)
    {
        if (!alignSweepVisualToSweepBox || sweepVisual == null)
            return;

        if (!IsFiniteVector(center) || !IsFiniteVector(halfExtents) || !IsFiniteQuaternion(rotation))
            return;

        Vector3 position = center + Vector3.up * GetFiniteFloatOrZero(sweepVisualYOffset);
        sweepVisual.transform.SetPositionAndRotation(position, rotation);

        if (!autoScaleSweepVisual)
            return;

        Vector3 multiplier = IsFiniteVector(sweepVisualScaleMultiplier) ? sweepVisualScaleMultiplier : Vector3.one;
        Vector3 scale = Vector3.Scale(halfExtents * 2f, multiplier);
        if (IsFiniteVector(scale))
            sweepVisual.transform.localScale = scale;
    }

    private void UpdateResidueVisual()
    {
        GetResidueBox(out Vector3 center, out Vector3 halfExtents, out Quaternion rotation);
        UpdateResidueVisual(center, halfExtents, rotation);
    }

    private void UpdateResidueVisual(Vector3 center, Vector3 halfExtents, Quaternion rotation)
    {
        if (!alignResidueVisualToResidueArea || residueVisual == null)
            return;

        if (!IsFiniteVector(center) || !IsFiniteVector(halfExtents) || !IsFiniteQuaternion(rotation))
            return;

        Vector3 position = center + Vector3.up * GetFiniteFloatOrZero(residueVisualYOffset);
        residueVisual.transform.SetPositionAndRotation(position, rotation);

        Vector3 multiplier = IsFiniteVector(residueVisualScaleMultiplier) ? residueVisualScaleMultiplier : Vector3.one;
        Vector3 scale = Vector3.Scale(halfExtents * 2f, multiplier);
        if (IsFiniteVector(scale))
            residueVisual.transform.localScale = scale;
    }

    private static void PlayAudio(AudioSource audioSource)
    {
        if (audioSource != null)
            audioSource.Play();
    }

    private static IEnumerator WaitForSecondsSafe(float duration)
    {
        if (duration <= 0f)
            yield break;

        yield return new WaitForSeconds(duration);
    }

    private void OnDisable()
    {
        if (_runningRoutine != null)
        {
            StopCoroutine(_runningRoutine);
            _runningRoutine = null;
        }

        SetPresentationActive(false, false, false);
        _sweepHitPlayers.Clear();
        _sweepTickPlayers.Clear();
        _residueTickPlayers.Clear();
        CleanupActiveCircularIcePatchVisuals();
        ClearCircularIcePatchSlipCooldowns();
        ClearActiveSweepLane();
        _phase = SweepPhase.Idle;
    }

    private void OnValidate()
    {
        circularIcePatchCount = Mathf.Max(0, circularIcePatchCount);

        float minRadius = Mathf.Max(0f, GetFiniteFloatOrZero(circularIcePatchRadiusRange.x));
        float maxRadius = Mathf.Max(0f, GetFiniteFloatOrZero(circularIcePatchRadiusRange.y));
        if (maxRadius < minRadius)
            maxRadius = minRadius;

        circularIcePatchRadiusRange = new Vector2(minRadius, maxRadius);
        if (!IsFiniteFloat(circularIcePatchHeightOffset))
            circularIcePatchHeightOffset = 0f;

        circularIcePatchDetectionHeight = Mathf.Max(0.05f, GetFiniteFloatOrZero(circularIcePatchDetectionHeight));
        circularIcePatchDuration = Mathf.Max(0f, GetFiniteFloatOrZero(circularIcePatchDuration));
        circularIcePatchTickInterval = Mathf.Max(0.02f, GetFiniteFloatOrZero(circularIcePatchTickInterval));
        circularIcePatchSlipForce = Mathf.Max(0f, GetFiniteFloatOrZero(circularIcePatchSlipForce));
        if (!IsFiniteFloat(circularIcePatchVisualYOffset))
            circularIcePatchVisualYOffset = 0f;

        circularIcePatchSlipCooldown = Mathf.Max(0f, GetFiniteFloatOrZero(circularIcePatchSlipCooldown));
        maxCircularIcePatchSpawnAttempts = Mathf.Max(1, maxCircularIcePatchSpawnAttempts);
        minimumCircularIcePatchDistance = Mathf.Max(0f, GetFiniteFloatOrZero(minimumCircularIcePatchDistance));
    }

    private void OnDrawGizmosSelected()
    {
        if (!drawGizmos)
            return;

        if (sweepLanes != null && sweepLanes.Length > 0)
        {
            bool drewLane = false;
            for (int i = 0; i < sweepLanes.Length; i++)
            {
                SweepLane lane = sweepLanes[i];
                if (!HasUsableSweepLaneDirection(lane))
                    continue;

                DrawSweepLaneGizmos(lane, i);
                drewLane = true;
            }

            if (drewLane)
                return;
        }

        DrawDefaultSweepGizmos();
    }

    private void DrawDefaultSweepGizmos()
    {
        GetSweepBox(0f, out Vector3 start, out Vector3 sweepHalfExtents, out Quaternion rotation);
        GetSweepBox(1f, out Vector3 end, out Vector3 endHalfExtents, out Quaternion endRotation);
        GetResidueBox(out Vector3 residueCenter, out Vector3 residueHalfExtents, out Quaternion residueRotation);

        DrawSweepGizmos(start, end, rotation, endRotation, sweepHalfExtents, endHalfExtents, residueCenter, residueRotation, residueHalfExtents);
    }

    private void DrawSweepLaneGizmos(SweepLane lane, int index)
    {
        GetSweepBox(lane, 0f, out Vector3 start, out Vector3 sweepHalfExtents, out Quaternion rotation);
        GetSweepBox(lane, 1f, out Vector3 end, out Vector3 endHalfExtents, out Quaternion endRotation);
        GetResidueBox(lane, out Vector3 residueCenter, out Vector3 residueHalfExtents, out Quaternion residueRotation);

        Color lineColor = index % 2 == 0 ? new Color(0.2f, 0.65f, 1f, 0.9f) : new Color(0.95f, 0.6f, 0.2f, 0.9f);
        DrawSweepGizmos(start, end, rotation, endRotation, sweepHalfExtents, endHalfExtents, residueCenter, residueRotation, residueHalfExtents, lineColor);
    }

    private static void DrawSweepGizmos(
        Vector3 start,
        Vector3 end,
        Quaternion startRotation,
        Quaternion endRotation,
        Vector3 startHalfExtents,
        Vector3 endHalfExtents,
        Vector3 residueCenter,
        Quaternion residueRotation,
        Vector3 residueHalfExtents)
    {
        DrawSweepGizmos(
            start,
            end,
            startRotation,
            endRotation,
            startHalfExtents,
            endHalfExtents,
            residueCenter,
            residueRotation,
            residueHalfExtents,
            new Color(0.2f, 0.65f, 1f, 0.9f));
    }

    private static void DrawSweepGizmos(
        Vector3 start,
        Vector3 end,
        Quaternion startRotation,
        Quaternion endRotation,
        Vector3 startHalfExtents,
        Vector3 endHalfExtents,
        Vector3 residueCenter,
        Quaternion residueRotation,
        Vector3 residueHalfExtents,
        Color lineColor)
    {
        Gizmos.color = lineColor;
        Gizmos.DrawLine(start, end);
        DrawWireBox(start, startRotation, startHalfExtents);

        Gizmos.color = new Color(0.1f, 0.9f, 1f, 0.9f);
        DrawWireBox(end, endRotation, endHalfExtents);

        Gizmos.color = new Color(0.1f, 0.8f, 0.45f, 0.9f);
        DrawWireBox(residueCenter, residueRotation, residueHalfExtents);
    }

    private static void DrawWireBox(Vector3 center, Quaternion rotation, Vector3 halfExtents)
    {
        Matrix4x4 previousMatrix = Gizmos.matrix;
        Gizmos.matrix = Matrix4x4.TRS(center, rotation, Vector3.one);
        Gizmos.DrawWireCube(Vector3.zero, halfExtents * 2f);
        Gizmos.matrix = previousMatrix;
    }

    private static bool IsFiniteVector(Vector3 value)
    {
        return IsFiniteFloat(value.x) && IsFiniteFloat(value.y) && IsFiniteFloat(value.z);
    }

    private static bool IsUsableHorizontalDirection(Vector3 direction)
    {
        direction.y = 0f;
        return IsFiniteVector(direction) && direction.sqrMagnitude >= 0.0001f;
    }

    private static Vector3 NormalizeHorizontalDirection(Vector3 direction)
    {
        direction.y = 0f;
        return direction.normalized;
    }

    private static bool IsFiniteQuaternion(Quaternion value)
    {
        return IsFiniteFloat(value.x) && IsFiniteFloat(value.y) && IsFiniteFloat(value.z) && IsFiniteFloat(value.w);
    }

    private static bool IsFiniteFloat(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }

    private static float GetFiniteFloatOrZero(float value)
    {
        return IsFiniteFloat(value) ? value : 0f;
    }

    private static bool IsPositiveFinite(float value)
    {
        return IsFiniteFloat(value) && value > 0f;
    }

    private static string GetLaneName(SweepLane lane)
    {
        return string.IsNullOrWhiteSpace(lane.name) ? "(Unnamed Lane)" : lane.name;
    }

    private void LogSourceDebug(string source, string message)
    {
        if (!enableDebugLogs)
            return;

        if (source == SourceSweep)
        {
            if (_sweepDebugLogCount >= MaxSweepDebugLogs)
                return;

            _sweepDebugLogCount++;
        }
        else if (source == SourceResidue)
        {
            if (_residueDebugLogCount >= MaxResidueDebugLogs)
                return;

            _residueDebugLogCount++;
        }

        Debug.Log($"{LogPrefix} [{source}] {message}", this);
    }

    private static string FormatStatusState(PlayerStatusModule status)
    {
        if (status == null)
            return "status=null";

        return $"status={status.name}, isSpawned={status.IsSpawned}, isEliminated={status.IsEliminated}, isKnocked={status.IsKnocked}, isStandingUp={status.IsStandingUp}";
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
