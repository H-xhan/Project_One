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
    private Coroutine _runningRoutine;
    private SweepPhase _phase = SweepPhase.Idle;
    private bool _loggedEmptyPlayerMaskWarning;
    private int _sweepDebugLogCount;
    private int _residueDebugLogCount;

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
        UpdateResidueVisual();
        SetPresentationActive(false, false, true);
        PlayAudio(residueAudio);
        yield return RunResidueRoutine();

        SetPhase(SweepPhase.Cooldown);
        SetPresentationActive(false, false, false);
        yield return WaitForSecondsSafe(cooldownDuration);

        SetPhase(SweepPhase.Idle);
        _sweepHitPlayers.Clear();
        _sweepTickPlayers.Clear();
        _residueTickPlayers.Clear();
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
        if (!enableResidueSlip)
        {
            yield return WaitForSecondsSafe(residueDuration);
            yield break;
        }

        float duration = Mathf.Max(0f, residueDuration);
        float tickInterval = Mathf.Max(MinPositiveValue, residueTickInterval);
        float elapsed = 0f;
        float nextTickTime = 0f;
        GetResidueBox(out Vector3 center, out Vector3 halfExtents, out Quaternion rotation);
        UpdateResidueVisual(center, halfExtents, rotation);

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

    private Vector3 GetSweepOriginPosition()
    {
        return sweepOrigin != null ? sweepOrigin.position : transform.position;
    }

    private Vector3 GetSweepDirection()
    {
        Vector3 direction = sweepDirection;
        direction.y = 0f;

        if (!IsFiniteVector(direction) || direction.sqrMagnitude < 0.0001f)
            direction = Vector3.right;

        return direction.normalized;
    }

    private Vector3 GetResidueSlipDirection()
    {
        Vector3 direction = residueSlipDirection;
        direction.y = 0f;

        if (!IsFiniteVector(direction) || direction.sqrMagnitude < 0.0001f)
            direction = GetSweepDirection();

        return direction.normalized;
    }

    private Vector3 GetSweepCenter(float progress)
    {
        Vector3 origin = GetSweepOriginPosition();
        Vector3 direction = GetSweepDirection();
        float distance = Mathf.Max(0f, sweepDistance);
        return origin + direction * (distance * Mathf.Clamp01(progress));
    }

    private Vector3 GetResidueCenter()
    {
        if (residueAreaCenter != null)
            return residueAreaCenter.position;

        return GetSweepOriginPosition() + GetSweepDirection() * (Mathf.Max(0f, sweepDistance) * 0.5f);
    }

    private void GetSweepBox(float progress, out Vector3 center, out Vector3 halfExtents, out Quaternion rotation)
    {
        center = GetSweepCenter(progress);
        halfExtents = GetSweepHalfExtents();
        rotation = GetSweepRotation();
    }

    private void GetResidueBox(out Vector3 center, out Vector3 halfExtents, out Quaternion rotation)
    {
        center = GetResidueCenter();
        halfExtents = GetResidueHalfExtents();
        rotation = GetSweepRotation();
    }

    private Quaternion GetSweepRotation()
    {
        Vector3 direction = GetSweepDirection();
        return Quaternion.LookRotation(direction, Vector3.up);
    }

    private Vector3 GetSweepHalfExtents()
    {
        return new Vector3(
            Mathf.Max(MinPositiveValue, sweepWidth) * 0.5f,
            Mathf.Max(MinPositiveValue, sweepHeight) * 0.5f,
            Mathf.Max(MinPositiveValue, sweepDepth) * 0.5f);
    }

    private Vector3 GetResidueHalfExtents()
    {
        return new Vector3(
            Mathf.Max(MinPositiveValue, residueAreaSize.x) * 0.5f,
            Mathf.Max(MinPositiveValue, residueAreaSize.y) * 0.5f,
            Mathf.Max(MinPositiveValue, residueAreaSize.z) * 0.5f);
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
        _phase = SweepPhase.Idle;
    }

    private void OnDrawGizmosSelected()
    {
        if (!drawGizmos)
            return;

        GetSweepBox(0f, out Vector3 start, out Vector3 sweepHalfExtents, out Quaternion rotation);
        GetSweepBox(1f, out Vector3 end, out Vector3 endHalfExtents, out Quaternion endRotation);
        GetResidueBox(out Vector3 residueCenter, out Vector3 residueHalfExtents, out Quaternion residueRotation);

        Gizmos.color = new Color(0.2f, 0.65f, 1f, 0.9f);
        Gizmos.DrawLine(start, end);
        DrawWireBox(start, rotation, sweepHalfExtents);

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
