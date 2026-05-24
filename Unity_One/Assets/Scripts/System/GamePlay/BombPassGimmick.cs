using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public class BombPassGimmick : NetworkBehaviour
{
    private const string LogPrefix = "[BombPassGimmick]";
    private const float MinBombDuration = 1f;
    private const float MinPositiveValue = 0.01f;
    private const float MinDirectionSqrMagnitude = 0.0001f;
    private const string BombMoveSpeedMultiplierKey = "BombPassGimmick";

    private enum BombPhase
    {
        Idle,
        Spawn,
        Armed,
        Passing,
        Explode,
        Cooldown
    }

    [Header("Bomb Setup")]
    [SerializeField, Tooltip("폭탄 비주얼 오브젝트입니다. 비어 있으면 비주얼 없이 로직만 동작합니다.")]
    private GameObject bombVisual;

    [SerializeField, Tooltip("폭탄을 처음 배치할 위치입니다. 비어 있으면 이 오브젝트 위치를 사용합니다.")]
    private Transform bombSpawnPoint;

    [SerializeField, Tooltip("폭탄이 보유자를 따라다닐 때 보유자 위치에 더할 오프셋입니다.")]
    private Vector3 holderFollowOffset = new Vector3(0f, 1.5f, 0f);

    [SerializeField, Tooltip("폭탄 비주얼을 보유자 월드 위치가 아니라 보유자 root 로컬 좌표 기준으로 따라가게 할지 여부입니다.")]
    private bool useHolderLocalOffset = true;

    [SerializeField, Tooltip("폭탄이 보유자 등에 붙어 보이도록 사용할 로컬 오프셋입니다.")]
    private Vector3 holderLocalFollowOffset = new Vector3(0f, 0.9f, -0.4f);

    [SerializeField, Tooltip("폭탄 비주얼에 적용할 로컬 회전 보정값입니다.")]
    private Vector3 bombLocalEulerOffset = Vector3.zero;

    [SerializeField, Tooltip("폭탄 비주얼이 보유자를 따라다닐 속도입니다.")]
    private float followSpeed = 18f;

    [SerializeField, Tooltip("폭탄 보유자에게 이동속도 증가 효과를 적용할지 여부입니다.")]
    private bool boostHolderMoveSpeed = true;

    [SerializeField, Tooltip("폭탄 보유자의 이동속도 배율입니다.")]
    private float holderMoveSpeedMultiplier = 1.08f;

    [Header("Target Selection")]
    [SerializeField, Tooltip("폭탄 시작 시 반경 탐색 대신 현재 씬의 유효한 플레이어 중 랜덤으로 한 명을 선택할지 여부입니다.")]
    private bool assignRandomPlayerOnStart = true;

    [SerializeField, Tooltip("폭탄 시작 시 가장 가까운 플레이어를 자동으로 대상으로 지정할지 여부입니다.")]
    private bool assignNearestPlayerOnStart = true;

    [SerializeField, Tooltip("자동 대상 탐색 기준 위치입니다. 비어 있으면 이 오브젝트 위치를 기준으로 합니다.")]
    private Transform targetSearchCenter;

    [SerializeField, Tooltip("폭탄 시작 대상 탐색 반경입니다.")]
    private float targetSearchRadius = 20f;

    [SerializeField, Tooltip("플레이어 탐색에 사용할 레이어 마스크입니다.")]
    private LayerMask playerMask;

    [Header("Timer")]
    [SerializeField, Tooltip("폭탄 카운트다운 전체 시간입니다.")]
    private float bombDuration = 10f;

    [SerializeField, Tooltip("마지막 경고가 시작되는 남은 시간입니다.")]
    private float finalWarningTime = 3f;

    [SerializeField, Tooltip("폭탄 전달 직후 다시 전달되기까지 필요한 최소 시간입니다.")]
    private float passCooldown = 0.75f;

    [SerializeField, Tooltip("폭발 후 Idle 상태로 돌아가기 전 대기 시간입니다.")]
    private float cooldownDuration = 1f;

    [Header("Pass Rule")]
    [SerializeField, Tooltip("폭탄 전달이 가능한 거리입니다.")]
    private float passRadius = 1.6f;

    [SerializeField, Tooltip("폭탄 전달 판정을 검사하는 간격입니다.")]
    private float passCheckInterval = 0.1f;

    [SerializeField, Tooltip("같은 두 플레이어 사이에서 즉시 왕복 전달되는 것을 막기 위한 시간입니다.")]
    private float samePairPassBlockTime = 0.6f;

    [Header("Explosion")]
    [SerializeField, Tooltip("폭탄 폭발 범위입니다.")]
    private float explosionRadius = 4f;

    [SerializeField, Tooltip("폭발 시 수평 넉백 힘입니다.")]
    private float explosionForce = 10f;

    [SerializeField, Tooltip("폭발 시 위쪽으로 띄우는 힘입니다.")]
    private float upwardForce = 2f;

    [SerializeField, Tooltip("폭탄 보유자에게 추가로 적용할 폭발 힘 배율입니다.")]
    private float holderExplosionMultiplier = 1.2f;

    [Header("Visual And Sound")]
    [SerializeField, Tooltip("폭탄 대상 지정 시 켤 오브젝트입니다.")]
    private GameObject targetMarkerVisual;

    [SerializeField, Tooltip("마지막 경고 시간에 켤 오브젝트입니다.")]
    private GameObject finalWarningVisual;

    [SerializeField, Tooltip("폭발 순간 켤 오브젝트입니다.")]
    private GameObject explosionVisual;

    [SerializeField, Tooltip("폭탄 시작 시 재생할 오디오 소스입니다.")]
    private AudioSource startAudio;

    [SerializeField, Tooltip("폭탄 전달 시 재생할 오디오 소스입니다.")]
    private AudioSource passAudio;

    [SerializeField, Tooltip("마지막 경고 시작 시 재생할 오디오 소스입니다.")]
    private AudioSource warningAudio;

    [SerializeField, Tooltip("폭발 시 재생할 오디오 소스입니다.")]
    private AudioSource explosionAudio;

    [Header("Debug")]
    [SerializeField, Tooltip("디버그 로그를 출력할지 여부입니다.")]
    private bool enableDebugLogs = false;

    [SerializeField, Tooltip("Scene View에서 폭탄 전달/폭발 범위 기즈모를 표시할지 여부입니다.")]
    private bool drawGizmos = true;

    private PlayerStatusModule _currentHolder;
    private PlayerStatusModule _previousHolder;
    private float _remainingTime;
    private float _nextPassAllowedTime;
    private float _nextPassCheckTime;
    private bool _finalWarningTriggered;
    private Coroutine _routine;
    private readonly HashSet<PlayerStatusModule> _explosionHitPlayers = new HashSet<PlayerStatusModule>();
    private readonly HashSet<PlayerStatusModule> _candidatePlayers = new HashSet<PlayerStatusModule>();
    private BombPhase _phase = BombPhase.Idle;
    private float _lastPassTime;
    private bool _loggedEmptyPlayerMaskWarning;
    private PlayerStatusModule _speedBoostedHolder;
    private PlayerLocomotionModule _speedBoostedLocomotion;
    private PlayerStatusModule _remoteVisualHolder;
    private bool _remoteVisualActive;
    private bool _remoteFinalWarningTriggered;

    private void Update()
    {
        if (_phase != BombPhase.Idle)
        {
            UpdateFollowerVisuals();
            return;
        }

        if (_remoteVisualActive)
            UpdateRemoteFollowerVisuals();
    }

    private void OnDisable()
    {
        if (!Application.isPlaying)
            return;

        StopBombPassInternal(false);
    }

    public override void OnNetworkDespawn()
    {
        SendCleanupBombPassVisual("network-despawn");
        CleanupRemoteBombPassVisual("network-despawn");
        base.OnNetworkDespawn();
    }

    public override void OnDestroy()
    {
        if (Application.isPlaying)
        {
            SendCleanupBombPassVisual("destroy");
            CleanupRemoteBombPassVisual("destroy");
            ClearHolderSpeedBoost();
        }

        base.OnDestroy();
    }

    [ContextMenu("Debug Start Bomb Pass")]
    private void DebugStartBombPass()
    {
        if (!Application.isPlaying)
        {
            LogWarning($"{LogPrefix} Debug start ignored. Application is not playing.");
            return;
        }

        if (_phase != BombPhase.Idle || _routine != null)
        {
            Log($"{LogPrefix} Start ignored. phase={_phase}");
            return;
        }

        StartBombPass();
    }

    [ContextMenu("Debug Stop Bomb Pass")]
    private void DebugStopBombPass()
    {
        if (!Application.isPlaying)
        {
            LogWarning($"{LogPrefix} Debug stop ignored. Application is not playing.");
            return;
        }

        StopBombPassInternal(true);
    }

    private void StartBombPass()
    {
        if (_phase != BombPhase.Idle || _routine != null)
        {
            Log($"{LogPrefix} Start ignored. phase={_phase}");
            return;
        }

        ClearRuntimeCaches();
        _loggedEmptyPlayerMaskWarning = false;

        PlayerStatusModule initialHolder = null;
        if (assignRandomPlayerOnStart)
        {
            initialHolder = FindRandomPlayer();
        }
        else if (assignNearestPlayerOnStart)
        {
            Log($"{LogPrefix} Using nearest target fallback.");
            initialHolder = FindNearestPlayer();
        }
        else
        {
            Log($"{LogPrefix} No start target selection enabled.");
        }

        if (!IsValidPlayerStatus(initialHolder))
        {
            Log($"{LogPrefix} No valid target.");
            ResetVisuals();
            return;
        }

        SetPhase(BombPhase.Spawn);
        ResetVisuals();
        SendCleanupBombPassVisual("start-reset");
        SetHolder(initialHolder, true);
        Log($"{LogPrefix} Target assigned: {initialHolder.name}");
        PlayAudio(startAudio);

        _remainingTime = GetBombDuration();
        _routine = StartCoroutine(RunBombPassRoutine());
    }

    private PlayerStatusModule FindRandomPlayer()
    {
#if UNITY_2022_2_OR_NEWER
        PlayerStatusModule[] statuses = FindObjectsByType<PlayerStatusModule>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
#else
        PlayerStatusModule[] statuses = FindObjectsOfType<PlayerStatusModule>();
#endif

        _candidatePlayers.Clear();

        for (int i = 0; i < statuses.Length; i++)
        {
            PlayerStatusModule status = statuses[i];
            if (!IsValidPlayerStatus(status))
                continue;

            _candidatePlayers.Add(status);
        }

        int candidateCount = _candidatePlayers.Count;
        Log($"{LogPrefix} Random target search found candidates={candidateCount}.");

        if (candidateCount <= 0)
        {
            Log($"{LogPrefix} No valid random target.");
            return null;
        }

        int selectedIndex = Random.Range(0, candidateCount);
        int currentIndex = 0;
        foreach (PlayerStatusModule candidate in _candidatePlayers)
        {
            if (currentIndex == selectedIndex)
            {
                Log($"{LogPrefix} Random target selected: {candidate.name}");
                return candidate;
            }

            currentIndex++;
        }

        Log($"{LogPrefix} No valid random target.");
        return null;
    }

    private IEnumerator RunBombPassRoutine()
    {
        SetPhase(BombPhase.Armed);
        SetPhase(BombPhase.Passing);

        while (_remainingTime > 0f)
        {
            if (_currentHolder == null)
            {
                Log($"{LogPrefix} Stopped. Current holder is missing.");
                FinishBombPass();
                yield break;
            }

            _remainingTime = Mathf.Max(0f, _remainingTime - Time.deltaTime);
            TryTriggerFinalWarning();
            if (_remainingTime <= 0f)
                break;

            TryPassBomb();
            yield return null;
        }

        SetPhase(BombPhase.Explode);
        ExplodeBomb();

        SetPhase(BombPhase.Cooldown);
        yield return WaitForSecondsSafe(GetCooldownDuration());

        FinishBombPass();
    }

    private PlayerStatusModule FindNearestPlayer()
    {
        Vector3 center = GetTargetSearchPosition();
        float radius = GetTargetSearchRadius();
        Collider[] colliders = Physics.OverlapSphere(
            center,
            radius,
            GetPlayerMask(),
            QueryTriggerInteraction.Ignore);

        _candidatePlayers.Clear();
        PlayerStatusModule nearest = null;
        float nearestSqrDistance = float.MaxValue;

        for (int i = 0; i < colliders.Length; i++)
        {
            PlayerStatusModule status = FindPlayerStatusFromCollider(colliders[i]);
            if (!IsValidPlayerStatus(status))
                continue;

            if (!_candidatePlayers.Add(status))
                continue;

            float sqrDistance = (status.transform.position - center).sqrMagnitude;
            if (sqrDistance >= nearestSqrDistance)
                continue;

            nearest = status;
            nearestSqrDistance = sqrDistance;
        }

        return nearest;
    }

    private PlayerStatusModule FindPlayerStatusFromCollider(Collider hit)
    {
        if (hit == null)
            return null;

        PlayerStatusModule parentStatus = hit.GetComponentInParent<PlayerStatusModule>();
        if (parentStatus != null)
            return parentStatus;

        PlayerStatusModule selfStatus = hit.GetComponent<PlayerStatusModule>();
        if (selfStatus != null)
            return selfStatus;

        PlayerHub hub = hit.GetComponentInParent<PlayerHub>();
        PlayerStatusModule hubStatus = hub != null ? hub.GetComponentInChildren<PlayerStatusModule>(true) : null;
        if (hubStatus != null)
            return hubStatus;

        Rigidbody attachedRigidbody = hit.attachedRigidbody;
        PlayerHub rbHub = attachedRigidbody != null ? attachedRigidbody.GetComponentInParent<PlayerHub>() : null;
        PlayerStatusModule rbHubStatus = rbHub != null ? rbHub.GetComponentInChildren<PlayerStatusModule>(true) : null;
        if (rbHubStatus != null)
            return rbHubStatus;

        Transform root = hit.transform.root;
        return root != null ? root.GetComponentInChildren<PlayerStatusModule>(true) : null;
    }

    private bool IsValidPlayerStatus(PlayerStatusModule status)
    {
        if (status == null)
            return false;

        if (!status.IsSpawned)
            return false;

        return !status.IsEliminated && !status.IsKnocked && !status.IsStandingUp;
    }

    private void SetHolder(PlayerStatusModule newHolder, bool playStartAudioForRemote = false)
    {
        if (!IsValidPlayerStatus(newHolder))
            return;

        ClearHolderSpeedBoost();

        _previousHolder = _currentHolder;
        _currentHolder = newHolder;
        _lastPassTime = Time.time;
        _nextPassAllowedTime = Time.time + GetPassCooldown();
        _nextPassCheckTime = Time.time;

        Vector3 markerPosition = GetHolderFollowPosition(newHolder);

        if (targetMarkerVisual != null)
        {
            targetMarkerVisual.transform.position = markerPosition;
            targetMarkerVisual.SetActive(true);
        }

        if (bombVisual != null)
        {
            if (TryGetBombVisualTargetPose(out Vector3 bombPosition, out Quaternion bombRotation))
            {
                if (!bombVisual.activeSelf)
                {
                    bombVisual.transform.position = bombPosition;
                    bombVisual.transform.rotation = bombRotation;
                }
            }
            else if (!bombVisual.activeSelf)
            {
                bombVisual.transform.position = markerPosition;
            }

            bombVisual.SetActive(true);
        }

        ApplyHolderSpeedBoost(_currentHolder);
        PlayAudio(passAudio);
        SendSetBombPassHolderVisual(newHolder, playStartAudioForRemote);
        Log($"{LogPrefix} Holder changed: {newHolder.name}");
    }

    private void TryPassBomb()
    {
        if (_phase != BombPhase.Passing)
            return;

        if (_currentHolder == null)
            return;

        if (Time.time < _nextPassAllowedTime || Time.time < _nextPassCheckTime)
            return;

        _nextPassCheckTime = Time.time + GetPassCheckInterval();

        Vector3 holderPosition = _currentHolder.transform.position;
        Collider[] colliders = Physics.OverlapSphere(
            holderPosition,
            GetPassRadius(),
            GetPlayerMask(),
            QueryTriggerInteraction.Ignore);

        _candidatePlayers.Clear();
        PlayerStatusModule passTarget = null;
        float nearestSqrDistance = float.MaxValue;

        for (int i = 0; i < colliders.Length; i++)
        {
            PlayerStatusModule candidate = FindPlayerStatusFromCollider(colliders[i]);
            if (candidate == null || candidate == _currentHolder)
                continue;

            if (!IsValidPlayerStatus(candidate))
                continue;

            if (!_candidatePlayers.Add(candidate))
                continue;

            if (candidate == _previousHolder && Time.time < _lastPassTime + GetSamePairPassBlockTime())
                continue;

            float sqrDistance = (candidate.transform.position - holderPosition).sqrMagnitude;
            if (sqrDistance >= nearestSqrDistance)
                continue;

            passTarget = candidate;
            nearestSqrDistance = sqrDistance;
        }

        if (passTarget == null)
            return;

        PlayerStatusModule oldHolder = _currentHolder;
        SetHolder(passTarget);
        Log($"{LogPrefix} Pass success: {oldHolder.name} -> {passTarget.name}");
    }

    private void TryTriggerFinalWarning()
    {
        if (_finalWarningTriggered)
            return;

        if (_remainingTime > GetFinalWarningTime())
            return;

        _finalWarningTriggered = true;

        if (finalWarningVisual != null)
        {
            finalWarningVisual.transform.position = GetCurrentHolderFollowPosition();
            finalWarningVisual.SetActive(true);
        }

        PlayAudio(warningAudio);
        SendPlayBombPassFinalWarningVisual();
        Log($"{LogPrefix} Final warning. remaining={_remainingTime:0.00}");
    }

    private void ExplodeBomb()
    {
        ClearHolderSpeedBoost();

        if (_currentHolder == null)
        {
            Log($"{LogPrefix} Explosion skipped. Current holder is missing.");
            return;
        }

        Vector3 explosionCenter = _currentHolder.transform.position;
        Collider[] colliders = Physics.OverlapSphere(
            explosionCenter,
            GetExplosionRadius(),
            GetPlayerMask(),
            QueryTriggerInteraction.Ignore);

        _explosionHitPlayers.Clear();

        for (int i = 0; i < colliders.Length; i++)
        {
            PlayerStatusModule status = FindPlayerStatusFromCollider(colliders[i]);
            if (!IsValidPlayerStatus(status))
                continue;

            if (!_explosionHitPlayers.Add(status))
                continue;

            Vector3 direction = GetExplosionDirection(status, explosionCenter);
            Vector3 impulse = direction * GetExplosionForce() + Vector3.up * GetUpwardForce();

            if (status == _currentHolder)
                impulse *= GetHolderExplosionMultiplier();

            status.ServerTryApplyGimmickKnockback(impulse);
            Log($"{LogPrefix} Gimmick explosion knockback requested target={status.name}, impulse={impulse}");
        }

        if (explosionVisual != null)
        {
            explosionVisual.transform.position = explosionCenter;
            if (explosionVisual.activeSelf)
                explosionVisual.SetActive(false);

            explosionVisual.SetActive(true);
        }

        PlayAudio(explosionAudio);
        SendPlayBombPassExplosionVisual(explosionCenter);

        if (bombVisual != null)
            bombVisual.SetActive(false);

        if (targetMarkerVisual != null)
            targetMarkerVisual.SetActive(false);

        if (finalWarningVisual != null)
            finalWarningVisual.SetActive(false);

        SendClearBombPassHolderVisual();
        Log($"{LogPrefix} Explosion. center={explosionCenter}, hits={_explosionHitPlayers.Count}");
    }

    private void UpdateFollowerVisuals()
    {
        if (_currentHolder == null)
        {
            if (bombVisual != null)
            {
                bombVisual.transform.position = GetBombSpawnPosition();
                bombVisual.SetActive(false);
            }

            if (targetMarkerVisual != null)
                targetMarkerVisual.SetActive(false);

            if (finalWarningVisual != null)
                finalWarningVisual.SetActive(false);

            return;
        }

        Vector3 markerPosition = GetCurrentHolderFollowPosition();
        float followT = Mathf.Clamp01(GetFollowSpeed() * Time.deltaTime);

        if (bombVisual != null)
        {
            if (TryGetBombVisualTargetPose(out Vector3 bombPosition, out Quaternion bombRotation))
            {
                bombVisual.transform.position = Vector3.Lerp(bombVisual.transform.position, bombPosition, followT);
                bombVisual.transform.rotation = Quaternion.Slerp(bombVisual.transform.rotation, bombRotation, followT);
            }

            if (!bombVisual.activeSelf && _phase != BombPhase.Explode && _phase != BombPhase.Cooldown)
                bombVisual.SetActive(true);
        }

        if (targetMarkerVisual != null)
        {
            targetMarkerVisual.transform.position = markerPosition;
            if (!targetMarkerVisual.activeSelf && _phase != BombPhase.Explode && _phase != BombPhase.Cooldown)
                targetMarkerVisual.SetActive(true);
        }

        if (_finalWarningTriggered && finalWarningVisual != null)
        {
            finalWarningVisual.transform.position = markerPosition;
            if (!finalWarningVisual.activeSelf && _phase != BombPhase.Explode && _phase != BombPhase.Cooldown)
                finalWarningVisual.SetActive(true);
        }
    }

    private Vector3 GetExplosionDirection(PlayerStatusModule status, Vector3 explosionCenter)
    {
        Vector3 direction = status.transform.position - explosionCenter;
        direction.y = 0f;

        if (direction.sqrMagnitude >= MinDirectionSqrMagnitude)
            return direction.normalized;

        if (_currentHolder != null)
        {
            direction = _currentHolder.transform.forward;
            direction.y = 0f;

            if (direction.sqrMagnitude >= MinDirectionSqrMagnitude)
                return direction.normalized;
        }

        return Vector3.forward;
    }

    private void FinishBombPass()
    {
        ClearHolderSpeedBoost();
        SetPhase(BombPhase.Idle);
        _routine = null;
        _currentHolder = null;
        _previousHolder = null;
        _remainingTime = 0f;
        _nextPassAllowedTime = 0f;
        _nextPassCheckTime = 0f;
        _lastPassTime = 0f;
        _finalWarningTriggered = false;
        ClearRuntimeCaches();
        ResetVisuals();
        SendCleanupBombPassVisual("finish");
    }

    private void StopBombPassInternal(bool logStop)
    {
        if (_routine != null)
        {
            StopCoroutine(_routine);
            _routine = null;
        }

        FinishBombPass();

        if (logStop)
            Log($"{LogPrefix} Stopped.");
    }

    private void ClearRuntimeCaches()
    {
        _candidatePlayers.Clear();
        _explosionHitPlayers.Clear();
    }

    private void ResetVisuals()
    {
        Vector3 spawnPosition = GetBombSpawnPosition();

        if (bombVisual != null)
        {
            bombVisual.transform.position = spawnPosition;
            bombVisual.SetActive(false);
        }

        if (targetMarkerVisual != null)
            targetMarkerVisual.SetActive(false);

        if (finalWarningVisual != null)
            finalWarningVisual.SetActive(false);

        if (explosionVisual != null)
            explosionVisual.SetActive(false);
    }

    private void SendSetBombPassHolderVisual(PlayerStatusModule holder, bool playStartAudioForRemote)
    {
        if (!CanSendBombPassVisualMessage())
            return;

        if (!TryGetBombPassHolderReference(holder, out NetworkObjectReference holderReference))
        {
            SendClearBombPassHolderVisual();
            Log($"{LogPrefix} Visual holder RPC skipped. reason=no-holder-reference");
            return;
        }

        SetBombPassHolderVisualClientRpc(holderReference, playStartAudioForRemote, _finalWarningTriggered);
        Log($"{LogPrefix} Visual holder RPC sent holder={holder.name}");
    }

    private void SendClearBombPassHolderVisual()
    {
        if (CanSendBombPassVisualMessage())
            ClearBombPassHolderVisualClientRpc();
    }

    private void SendPlayBombPassFinalWarningVisual()
    {
        if (CanSendBombPassVisualMessage())
            PlayBombPassFinalWarningVisualClientRpc();
    }

    private void SendPlayBombPassExplosionVisual(Vector3 explosionPosition)
    {
        if (CanSendBombPassVisualMessage())
            PlayBombPassExplosionVisualClientRpc(explosionPosition);
    }

    private void SendCleanupBombPassVisual(string reason)
    {
        if (CanSendBombPassVisualMessage())
            CleanupBombPassVisualClientRpc(reason);
    }

    private bool CanSendBombPassVisualMessage()
    {
        return IsServer && IsSpawned;
    }

    private bool ShouldSkipBombPassVisualOnThisPeer()
    {
        return IsServer;
    }

    private bool TryGetBombPassHolderReference(PlayerStatusModule holder, out NetworkObjectReference holderReference)
    {
        holderReference = default;
        if (holder == null || holder.NetworkObject == null || !holder.NetworkObject.IsSpawned)
            return false;

        holderReference = new NetworkObjectReference(holder.NetworkObject);
        return true;
    }

    [ClientRpc]
    private void SetBombPassHolderVisualClientRpc(NetworkObjectReference holderReference, bool playStartAudioForRemote, bool finalWarningActive)
    {
        if (ShouldSkipBombPassVisualOnThisPeer())
            return;

        if (!TryResolveBombPassHolder(holderReference, out PlayerStatusModule holder))
        {
            ClearRemoteBombPassHolderVisual();
            Log($"{LogPrefix} Remote holder visual resolved=False");
            return;
        }

        _remoteVisualHolder = holder;
        _remoteVisualActive = true;
        _remoteFinalWarningTriggered = finalWarningActive;
        if (!_remoteFinalWarningTriggered && finalWarningVisual != null)
            finalWarningVisual.SetActive(false);

        UpdateRemoteFollowerVisuals(true);
        PlayAudio(passAudio);

        if (playStartAudioForRemote)
            PlayAudio(startAudio);

        Log($"{LogPrefix} Remote holder visual resolved=True holder={holder.name}");
    }

    [ClientRpc]
    private void ClearBombPassHolderVisualClientRpc()
    {
        if (ShouldSkipBombPassVisualOnThisPeer())
            return;

        ClearRemoteBombPassHolderVisual();
        Log($"{LogPrefix} Remote holder visual cleared");
    }

    [ClientRpc]
    private void PlayBombPassFinalWarningVisualClientRpc()
    {
        if (ShouldSkipBombPassVisualOnThisPeer())
            return;

        _remoteFinalWarningTriggered = true;
        Vector3 warningPosition = GetRemoteHolderFollowPosition();
        if (finalWarningVisual != null)
        {
            finalWarningVisual.transform.position = warningPosition;
            finalWarningVisual.SetActive(true);
        }

        PlayAudio(warningAudio);
        Log($"{LogPrefix} Final warning visual RPC");
    }

    [ClientRpc]
    private void PlayBombPassExplosionVisualClientRpc(Vector3 explosionPosition)
    {
        if (ShouldSkipBombPassVisualOnThisPeer())
            return;

        if (explosionVisual != null)
        {
            explosionVisual.transform.position = explosionPosition;
            if (explosionVisual.activeSelf)
                explosionVisual.SetActive(false);

            explosionVisual.SetActive(true);
        }

        PlayAudio(explosionAudio);
        ClearRemoteBombPassHolderVisual();
        Log($"{LogPrefix} Explosion visual RPC pos={explosionPosition}");
    }

    [ClientRpc]
    private void CleanupBombPassVisualClientRpc(string reason)
    {
        if (ShouldSkipBombPassVisualOnThisPeer())
            return;

        CleanupRemoteBombPassVisual(reason);
    }

    private bool TryResolveBombPassHolder(NetworkObjectReference holderReference, out PlayerStatusModule holder)
    {
        holder = null;
        if (!holderReference.TryGet(out NetworkObject holderObject) || holderObject == null)
            return false;

        holder = holderObject.GetComponentInChildren<PlayerStatusModule>(true);
        if (holder != null)
            return true;

        holder = holderObject.GetComponent<PlayerStatusModule>();
        return holder != null;
    }

    private void UpdateRemoteFollowerVisuals(bool snap = false)
    {
        if (_remoteVisualHolder == null)
        {
            ClearRemoteBombPassHolderVisual();
            return;
        }

        Vector3 markerPosition = GetHolderFollowPosition(_remoteVisualHolder);
        float followT = snap ? 1f : Mathf.Clamp01(GetFollowSpeed() * Time.deltaTime);

        if (bombVisual != null)
        {
            if (TryGetBombVisualTargetPose(_remoteVisualHolder, out Vector3 bombPosition, out Quaternion bombRotation))
            {
                bombVisual.transform.position = snap
                    ? bombPosition
                    : Vector3.Lerp(bombVisual.transform.position, bombPosition, followT);
                bombVisual.transform.rotation = snap
                    ? bombRotation
                    : Quaternion.Slerp(bombVisual.transform.rotation, bombRotation, followT);
            }
            else if (snap || !bombVisual.activeSelf)
            {
                bombVisual.transform.position = markerPosition;
            }

            if (!bombVisual.activeSelf)
                bombVisual.SetActive(true);
        }

        if (targetMarkerVisual != null)
        {
            targetMarkerVisual.transform.position = markerPosition;
            if (!targetMarkerVisual.activeSelf)
                targetMarkerVisual.SetActive(true);
        }

        if (_remoteFinalWarningTriggered && finalWarningVisual != null)
        {
            finalWarningVisual.transform.position = markerPosition;
            if (!finalWarningVisual.activeSelf)
                finalWarningVisual.SetActive(true);
        }
    }

    private void ClearRemoteBombPassHolderVisual()
    {
        _remoteVisualHolder = null;
        _remoteVisualActive = false;
        _remoteFinalWarningTriggered = false;

        if (bombVisual != null)
        {
            bombVisual.transform.position = GetBombSpawnPosition();
            bombVisual.SetActive(false);
        }

        if (targetMarkerVisual != null)
            targetMarkerVisual.SetActive(false);

        if (finalWarningVisual != null)
            finalWarningVisual.SetActive(false);
    }

    private void CleanupRemoteBombPassVisual(string reason)
    {
        ClearRemoteBombPassHolderVisual();

        if (explosionVisual != null)
            explosionVisual.SetActive(false);

        Log($"{LogPrefix} Remote visual cleanup reason={reason}");
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

    private Vector3 GetBombSpawnPosition()
    {
        return bombSpawnPoint != null ? bombSpawnPoint.position : transform.position;
    }

    private Vector3 GetTargetSearchPosition()
    {
        return targetSearchCenter != null ? targetSearchCenter.position : transform.position;
    }

    private Vector3 GetCurrentHolderFollowPosition()
    {
        return _currentHolder != null ? GetHolderFollowPosition(_currentHolder) : GetBombSpawnPosition();
    }

    private Vector3 GetRemoteHolderFollowPosition()
    {
        return _remoteVisualHolder != null ? GetHolderFollowPosition(_remoteVisualHolder) : GetBombSpawnPosition();
    }

    private bool TryGetBombVisualTargetPose(out Vector3 position, out Quaternion rotation)
    {
        return TryGetBombVisualTargetPose(_currentHolder, out position, out rotation);
    }

    private bool TryGetBombVisualTargetPose(PlayerStatusModule holder, out Vector3 position, out Quaternion rotation)
    {
        position = GetBombSpawnPosition();
        rotation = bombVisual != null ? bombVisual.transform.rotation : Quaternion.identity;

        if (holder == null)
            return false;

        if (!useHolderLocalOffset)
        {
            position = GetHolderFollowPosition(holder);
            return true;
        }

        Transform root = GetHolderRootTransform(holder);
        if (root == null)
            return false;

        position = root.TransformPoint(GetHolderLocalFollowOffset());
        rotation = root.rotation * Quaternion.Euler(GetBombLocalEulerOffset());
        return true;
    }

    private Transform GetHolderRootTransform(PlayerStatusModule status)
    {
        if (status == null)
            return null;

        PlayerHub hub = status.GetComponentInParent<PlayerHub>();
        if (hub != null)
            return hub.transform;

        Transform root = status.transform.root;
        if (root != null)
            return root;

        return status.transform;
    }

    private Vector3 GetHolderFollowPosition(PlayerStatusModule holder)
    {
        return holder.transform.position + GetHolderFollowOffset();
    }

    private Vector3 GetHolderFollowOffset()
    {
        return IsFiniteVector(holderFollowOffset) ? holderFollowOffset : Vector3.zero;
    }

    private Vector3 GetHolderLocalFollowOffset()
    {
        return IsFiniteVector(holderLocalFollowOffset) ? holderLocalFollowOffset : Vector3.zero;
    }

    private Vector3 GetBombLocalEulerOffset()
    {
        return IsFiniteVector(bombLocalEulerOffset) ? bombLocalEulerOffset : Vector3.zero;
    }

    private float GetHolderMoveSpeedMultiplier()
    {
        return Mathf.Clamp(GetFiniteFloatOrDefault(holderMoveSpeedMultiplier, 1f), 0.1f, 3f);
    }

    private PlayerLocomotionModule FindLocomotionModule(PlayerStatusModule status)
    {
        if (status == null)
            return null;

        PlayerLocomotionModule parentLocomotion = status.GetComponentInParent<PlayerLocomotionModule>();
        if (parentLocomotion != null)
            return parentLocomotion;

        PlayerLocomotionModule selfLocomotion = status.GetComponent<PlayerLocomotionModule>();
        if (selfLocomotion != null)
            return selfLocomotion;

        PlayerHub hub = status.GetComponentInParent<PlayerHub>();
        PlayerLocomotionModule hubLocomotion = hub != null ? hub.GetComponentInChildren<PlayerLocomotionModule>(true) : null;
        if (hubLocomotion != null)
            return hubLocomotion;

        Transform root = status.transform.root;
        return root != null ? root.GetComponentInChildren<PlayerLocomotionModule>(true) : null;
    }

    private void ApplyHolderSpeedBoost(PlayerStatusModule holder)
    {
        if (!boostHolderMoveSpeed || holder == null)
            return;

        PlayerLocomotionModule locomotion = FindLocomotionModule(holder);
        if (locomotion == null)
        {
            LogWarning($"{LogPrefix} Holder speed boost skipped. PlayerLocomotionModule not found. holder={holder.name}");
            return;
        }

        float multiplier = GetHolderMoveSpeedMultiplier();
        locomotion.SetExternalMoveSpeedMultiplier(BombMoveSpeedMultiplierKey, multiplier);
        _speedBoostedHolder = holder;
        _speedBoostedLocomotion = locomotion;
        Log($"{LogPrefix} Applied holder speed boost to {holder.name}, multiplier={multiplier:0.###}");
    }

    private void ClearHolderSpeedBoost()
    {
        if (_speedBoostedLocomotion != null)
        {
            _speedBoostedLocomotion.ClearExternalMoveSpeedMultiplier(BombMoveSpeedMultiplierKey);
            string holderName = _speedBoostedHolder != null ? _speedBoostedHolder.name : "missing holder";
            Log($"{LogPrefix} Cleared holder speed boost from {holderName}");
        }

        _speedBoostedHolder = null;
        _speedBoostedLocomotion = null;
    }

    private float GetBombDuration()
    {
        return Mathf.Max(MinBombDuration, GetFiniteFloatOrDefault(bombDuration, MinBombDuration));
    }

    private float GetFinalWarningTime()
    {
        return Mathf.Clamp(GetFiniteFloatOrDefault(finalWarningTime, 0f), 0f, GetBombDuration());
    }

    private float GetPassCooldown()
    {
        return Mathf.Max(0f, GetFiniteFloatOrDefault(passCooldown, 0f));
    }

    private float GetCooldownDuration()
    {
        return Mathf.Max(0f, GetFiniteFloatOrDefault(cooldownDuration, 0f));
    }

    private float GetTargetSearchRadius()
    {
        return Mathf.Max(MinPositiveValue, GetFiniteFloatOrDefault(targetSearchRadius, MinPositiveValue));
    }

    private float GetPassRadius()
    {
        return Mathf.Max(MinPositiveValue, GetFiniteFloatOrDefault(passRadius, MinPositiveValue));
    }

    private float GetPassCheckInterval()
    {
        return Mathf.Max(MinPositiveValue, GetFiniteFloatOrDefault(passCheckInterval, MinPositiveValue));
    }

    private float GetSamePairPassBlockTime()
    {
        return Mathf.Max(0f, GetFiniteFloatOrDefault(samePairPassBlockTime, 0f));
    }

    private float GetExplosionRadius()
    {
        return Mathf.Max(MinPositiveValue, GetFiniteFloatOrDefault(explosionRadius, MinPositiveValue));
    }

    private float GetExplosionForce()
    {
        return Mathf.Max(0f, GetFiniteFloatOrDefault(explosionForce, 0f));
    }

    private float GetUpwardForce()
    {
        return Mathf.Max(0f, GetFiniteFloatOrDefault(upwardForce, 0f));
    }

    private float GetHolderExplosionMultiplier()
    {
        return Mathf.Max(0f, GetFiniteFloatOrDefault(holderExplosionMultiplier, 1f));
    }

    private float GetFollowSpeed()
    {
        return Mathf.Max(0f, GetFiniteFloatOrDefault(followSpeed, 0f));
    }

    private void SetPhase(BombPhase nextPhase)
    {
        if (_phase == nextPhase)
            return;

        _phase = nextPhase;
        Log($"{LogPrefix} Phase changed: {_phase}");
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

    private void OnDrawGizmosSelected()
    {
        if (!drawGizmos)
            return;

        Vector3 searchPosition = GetTargetSearchPosition();
        Vector3 holderPosition = _currentHolder != null ? _currentHolder.transform.position : transform.position;
        Vector3 spawnPosition = GetBombSpawnPosition();

        Gizmos.color = new Color(0.2f, 0.65f, 1f, 0.8f);
        Gizmos.DrawWireSphere(searchPosition, GetTargetSearchRadius());

        Gizmos.color = new Color(1f, 0.85f, 0.15f, 0.8f);
        Gizmos.DrawWireSphere(holderPosition, GetPassRadius());

        Gizmos.color = new Color(1f, 0.25f, 0.1f, 0.8f);
        Gizmos.DrawWireSphere(holderPosition, GetExplosionRadius());

        Gizmos.color = new Color(0.2f, 1f, 0.45f, 0.9f);
        Gizmos.DrawWireSphere(spawnPosition, 0.2f);
    }

    private static bool IsFiniteVector(Vector3 value)
    {
        return IsFiniteFloat(value.x) && IsFiniteFloat(value.y) && IsFiniteFloat(value.z);
    }

    private static bool IsFiniteFloat(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }

    private static float GetFiniteFloatOrDefault(float value, float fallback)
    {
        return IsFiniteFloat(value) ? value : fallback;
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
