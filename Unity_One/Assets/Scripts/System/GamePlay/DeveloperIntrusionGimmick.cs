using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Events;

public class DeveloperIntrusionGimmick : MonoBehaviour
{
    private const string LogPrefix = "[DEV_INTRUSION]";

    public enum Phase
    {
        Idle = 0,
        Telegraph = 1,
        Response = 2,
        Scan = 3,
        Resolve = 4,
        Cooldown = 5
    }

    [Header("Timing")]
    [Tooltip("전조 단계 지속 시간(초)입니다. 플레이어에게 곧 멈춰야 함을 알리는 시간입니다.")]
    [SerializeField] private float telegraphDuration = 1.5f;

    [Tooltip("대응 단계 지속 시간(초)입니다. 플레이어가 입력을 멈출 수 있는 시간입니다.")]
    [SerializeField] private float responseDuration = 2.0f;

    [Tooltip("스캔 판정에 사용할 샘플링 시간(초)입니다. 짧은 평균 속도로 억울한 판정을 줄입니다.")]
    [SerializeField] private float scanSampleDuration = 0.35f;

    [Tooltip("실패 처리 후 정리 단계 지속 시간(초)입니다.")]
    [SerializeField] private float resolveDuration = 1.0f;

    [Header("Scan")]
    [Tooltip("스캔 실패로 볼 평균 수평 속도 임계값입니다. 값이 낮을수록 더 엄격하게 판정합니다.")]
    [SerializeField] private float scanVelocityThreshold = 0.35f;

    [Tooltip("스캔 실패로 볼 평균 각속도 임계값입니다. 값이 낮을수록 회전에 더 민감합니다.")]
    [SerializeField] private float scanAngularVelocityThreshold = 1.2f;

    [Header("Resolve")]
    [Tooltip("실패자에게 적용할 수평 넉백 힘입니다. 값이 높을수록 더 멀리 밀려납니다.")]
    [SerializeField] private float knockbackForce = 14f;

    [Tooltip("실패자에게 적용할 위쪽 넉백 힘입니다. 값이 높을수록 더 높이 뜹니다.")]
    [SerializeField] private float upwardForce = 4f;

    [Header("Presentation Hooks")]
    [Tooltip("기믹 연출 사운드를 재생할 AudioSource입니다. 비워두면 사운드 재생을 건너뜁니다.")]
    [SerializeField] private AudioSource audioSource;

    [Tooltip("전조 단계 시작 시 재생할 사운드 클립입니다.")]
    [SerializeField] private AudioClip telegraphClip;

    [Tooltip("대응 단계 시작 시 재생할 사운드 클립입니다.")]
    [SerializeField] private AudioClip responseClip;

    [Tooltip("스캔 단계 시작 시 재생할 사운드 클립입니다.")]
    [SerializeField] private AudioClip scanClip;

    [Tooltip("실패 처리 단계 시작 시 재생할 사운드 클립입니다.")]
    [SerializeField] private AudioClip resolveClip;

    [Tooltip("기믹 종료 시 재생할 사운드 클립입니다.")]
    [SerializeField] private AudioClip endClip;

    [Tooltip("전조 단계 시작 시 생성할 VFX 프리팹입니다.")]
    [SerializeField] private GameObject telegraphVfxPrefab;

    [Tooltip("대응 단계 시작 시 생성할 VFX 프리팹입니다.")]
    [SerializeField] private GameObject responseVfxPrefab;

    [Tooltip("스캔 단계 시작 시 생성할 VFX 프리팹입니다.")]
    [SerializeField] private GameObject scanVfxPrefab;

    [Tooltip("실패 처리 단계 시작 시 생성할 VFX 프리팹입니다.")]
    [SerializeField] private GameObject resolveVfxPrefab;

    [Tooltip("VFX를 생성할 위치입니다. 비워두면 이 오브젝트 위치를 사용합니다.")]
    [SerializeField] private Transform vfxSpawnPoint;

    [Tooltip("전조 단계 시작 시 호출할 추가 연출 이벤트입니다.")]
    [SerializeField] private UnityEvent onTelegraph;

    [Tooltip("대응 단계 시작 시 호출할 추가 연출 이벤트입니다.")]
    [SerializeField] private UnityEvent onResponse;

    [Tooltip("스캔 단계 시작 시 호출할 추가 연출 이벤트입니다.")]
    [SerializeField] private UnityEvent onScan;

    [Tooltip("실패 처리 단계 시작 시 호출할 추가 연출 이벤트입니다.")]
    [SerializeField] private UnityEvent onResolve;

    [Tooltip("기믹 종료 시 호출할 추가 연출 이벤트입니다.")]
    [SerializeField] private UnityEvent onEnd;

    [Header("Minimal Telegraph Text")]
    [Tooltip("전조 메시지를 표시할 TMP 텍스트입니다. 비워두면 텍스트 표시를 건너뜁니다.")]
    [SerializeField] private TMP_Text telegraphText;

    [Tooltip("전조 단계에서 화면에 표시할 경고 문구입니다.")]
    [SerializeField] private string telegraphMessage = "STOP. Developer is watching.";

    [Header("Door Presentation")]
    [Tooltip("개발자 난입 문 연출에 사용할 회전 피벗입니다.")]
    [SerializeField] private Transform doorPivot;

    [Tooltip("문이 닫혀 있을 때의 로컬 오일러 각도입니다.")]
    [SerializeField] private Vector3 doorClosedEuler;

    [Tooltip("문이 열려 있을 때의 로컬 오일러 각도입니다.")]
    [SerializeField] private Vector3 doorOpenEuler;

    [Tooltip("문이 열리는 데 걸리는 시간(초)입니다.")]
    [SerializeField] private float doorOpenDuration = 0.35f;

    [Tooltip("문이 닫히는 데 걸리는 시간(초)입니다.")]
    [SerializeField] private float doorCloseDuration = 0.3f;

    [Header("Developer Actor")]
    [Tooltip("난입 연출에 사용할 개발자 actor입니다. 비워두면 actor 연출을 건너뜁니다.")]
    [SerializeField] private GameObject developerActor;

    [Tooltip("개발자 actor의 기본 등장 위치입니다. developerScanPoint가 비어 있을 때 fallback으로 사용합니다.")]
    [SerializeField] private Transform developerSpawnPoint;

    [Tooltip("개발자 actor를 문 앞에 배치할 기본 위치입니다. 현재 단순화된 연출에서는 등장 위치로 사용합니다.")]
    [SerializeField] private Transform developerScanPoint;

    [Header("Sequence Timing")]
    [Tooltip("문이 열린 뒤 개발자 actor를 표시하기 전에 잠시 기다릴 시간입니다.")]
    [SerializeField] private float waitAfterDoorOpen = 0.2f;

    [Tooltip("문이 닫힌 뒤 스캔 단계로 넘어가기 전에 잠시 기다릴 시간입니다.")]
    [SerializeField] private float waitBeforeScan = 0.3f;

    [Tooltip("실패 처리 이후 문을 다시 열기 전에 잠시 유지할 시간입니다.")]
    [SerializeField] private float waitAfterScan = 0.5f;

    [Header("Debug")]
    [Tooltip("개발자 난입 기믹의 디버그 로그를 출력할지 여부입니다.")]
    [SerializeField] private bool enableDebugLogs = false;

    private readonly List<PlayerSample> _samples = new List<PlayerSample>();
    private readonly List<ScanResult> _lastScanResults = new List<ScanResult>();
    private readonly List<HamsterRecoverySample> _hamsterRecoverySamples = new List<HamsterRecoverySample>();
    private readonly List<HamsterRecoveryScanResult> _lastHamsterRecoveryScanResults = new List<HamsterRecoveryScanResult>();
    private Coroutine _runningRoutine;
    private Coroutine _presentationSequenceRoutine;
    private Phase _phase = Phase.Idle;
    private bool _entranceSequenceRequested;
    private bool _exitSequenceRequested;

    public Phase CurrentPhase => _phase;
    public bool IsRunning => _runningRoutine != null;
    public IReadOnlyList<ScanResult> LastScanResults => _lastScanResults;

    public struct ScanResult
    {
        public PlayerStatusModule Status;
        public float AverageSpeed;
        public float AverageAngularSpeed;
        public bool HasGroundedInfo;
        public bool IsGrounded;
        public bool Failed;
        public string Reason;
    }

    private struct PlayerSample
    {
        public PlayerStatusModule status;
        public CharacterController characterController;
        public Rigidbody rigidbody;
        public Transform trackingTransform;
        public Vector3 previousPosition;
        public float speedSum;
        public float angularSpeedSum;
        public float maxFrameSpeed;
        public float maxFrameAngularSpeed;
        public int sampleCount;
        public bool hasGroundedInfo;
        public bool isGrounded;
        public string maxSpeedSource;
        public bool invalidStateDetected;
        public string invalidStateReason;
    }

    private struct HamsterRecoverySample
    {
        public HamsterMotorShellRagdollRecoveryAdapter recovery;
        public Rigidbody rigidbody;
        public Transform trackingTransform;
        public Vector3 previousPosition;
        public float speedSum;
        public float angularSpeedSum;
        public float maxFrameSpeed;
        public float maxFrameAngularSpeed;
        public int sampleCount;
        public bool invalidStateDetected;
        public string invalidStateReason;
    }

    private struct HamsterRecoveryScanResult
    {
        public HamsterMotorShellRagdollRecoveryAdapter Recovery;
        public float AverageSpeed;
        public float AverageAngularSpeed;
        public bool Failed;
        public string Reason;
    }

    public Coroutine StartGimmick(IList<PlayerStatusModule> players, bool playPresentationInternally = true)
    {
        if (IsRunning)
        {
            Log($"{LogPrefix} Start ignored. Gimmick is already running.");
            return _runningRoutine;
        }

        ResetPresentationSequenceState(true);
        _runningRoutine = StartCoroutine(RunRoutine(players, playPresentationInternally));
        return _runningRoutine;
    }

    public void StopGimmick()
    {
        if (_runningRoutine != null)
        {
            StopCoroutine(_runningRoutine);
            _runningRoutine = null;
        }

        ResetPresentationSequenceState(true);
        SetPhase(Phase.Idle);
        CleanupPresentationState();
    }

    public void PlayTelegraphPresentation()
    {
        Log($"{LogPrefix} Telegraph");
        ShowTelegraphText();
        PlayOneShot(telegraphClip);
        SpawnVfx(telegraphVfxPrefab);
        onTelegraph?.Invoke();
        RequestEntranceSequence();
    }

    public void PlayResponsePresentation()
    {
        Log($"{LogPrefix} Response");
        HideTelegraphText();
        PlayOneShot(responseClip);
        SpawnVfx(responseVfxPrefab);
        onResponse?.Invoke();
    }

    public void PlayScanPresentation()
    {
        Log($"{LogPrefix} Scan");
        PlayOneShot(scanClip);
        SpawnVfx(scanVfxPrefab);
        onScan?.Invoke();
    }

    public void PlayResolvePresentation()
    {
        int failedCount = CountFailedResults();
        Log($"{LogPrefix} Resolve presentation failedCount:{failedCount}");
        PlayOneShot(resolveClip);
        SpawnVfx(resolveVfxPrefab);
        onResolve?.Invoke();
        RequestExitSequence();
    }

    public void PlayEndPresentation()
    {
        Log($"{LogPrefix} End");
        ResetPresentationSequenceState(true);
        CleanupPresentationState();
        PlayOneShot(endClip);
        onEnd?.Invoke();
    }

    private IEnumerator RunRoutine(IList<PlayerStatusModule> players, bool playPresentationInternally)
    {
        _lastScanResults.Clear();
        _lastHamsterRecoveryScanResults.Clear();

        if (playPresentationInternally)
            PrepareDeveloperActorForSequence();

        SetPhase(Phase.Telegraph);
        if (playPresentationInternally)
            PlayTelegraphPresentation();
        yield return WaitForSecondsSafe(telegraphDuration);

        SetPhase(Phase.Response);
        if (playPresentationInternally)
            PlayResponsePresentation();
        yield return WaitForSecondsSafe(responseDuration);

        if (playPresentationInternally)
            yield return WaitForSecondsSafe(waitBeforeScan);

        SetPhase(Phase.Scan);
        if (playPresentationInternally)
            PlayScanPresentation();
        yield return SamplePlayersRoutine(players);

        SetPhase(Phase.Resolve);
        if (playPresentationInternally)
            PlayResolvePresentation();
        ResolveFailures();
        yield return WaitForSecondsSafe(resolveDuration);

        if (playPresentationInternally)
            yield return WaitForSecondsSafe(waitAfterScan);

        SetPhase(Phase.Cooldown);
        if (playPresentationInternally)
            PlayEndPresentation();
        SetPhase(Phase.Idle);
        _runningRoutine = null;
    }

    private IEnumerator SamplePlayersRoutine(IList<PlayerStatusModule> players)
    {
        BuildSamples(players);

        float elapsed = 0f;
        float duration = Mathf.Max(0.01f, scanSampleDuration);

        while (elapsed < duration)
        {
            yield return null;
            float dt = Mathf.Max(Time.deltaTime, 0.0001f);
            elapsed += Time.deltaTime;
            CaptureSample(dt);
        }

        BuildResults();
    }

    private void BuildSamples(IList<PlayerStatusModule> players)
    {
        _samples.Clear();
        _hamsterRecoverySamples.Clear();

        if (players == null)
        {
            BuildHamsterRecoverySamples();
            return;
        }

        for (int i = 0; i < players.Count; i++)
        {
            PlayerStatusModule status = players[i];
            if (status == null)
                continue;

            if (status.IsEliminated)
            {
                Log($"{LogPrefix} Skip eliminated player:{status.name}");
                continue;
            }

            CharacterController cc = status.GetComponentInParent<CharacterController>();
            Rigidbody rb = status.GetComponentInParent<Rigidbody>();
            Transform trackingTransform = ResolveTrackingTransform(status, cc, rb);
            bool hasGroundedInfo = cc != null && cc.enabled;
            bool isGrounded = !hasGroundedInfo || cc.isGrounded;
            bool invalidState = IsInvalidState(status, cc, out string invalidStateReason);

            _samples.Add(new PlayerSample
            {
                status = status,
                characterController = cc,
                rigidbody = rb,
                trackingTransform = trackingTransform,
                previousPosition = trackingTransform != null ? trackingTransform.position : status.transform.position,
                speedSum = 0f,
                angularSpeedSum = 0f,
                maxFrameSpeed = 0f,
                maxFrameAngularSpeed = 0f,
                sampleCount = 0,
                hasGroundedInfo = hasGroundedInfo,
                isGrounded = isGrounded,
                maxSpeedSource = "None",
                invalidStateDetected = invalidState,
                invalidStateReason = invalidStateReason
            });
        }

        BuildHamsterRecoverySamples();
    }

    private void BuildHamsterRecoverySamples()
    {
#if UNITY_6000_0_OR_NEWER
        HamsterMotorShellRagdollRecoveryAdapter[] recoveries = FindObjectsByType<HamsterMotorShellRagdollRecoveryAdapter>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
#else
        HamsterMotorShellRagdollRecoveryAdapter[] recoveries = FindObjectsOfType<HamsterMotorShellRagdollRecoveryAdapter>();
#endif
        for (int i = 0; i < recoveries.Length; i++)
        {
            HamsterMotorShellRagdollRecoveryAdapter recovery = recoveries[i];
            if (recovery == null || !recovery.CanReceiveRecoveryState)
                continue;

            Rigidbody rb = recovery.BodyRigidbody;
            Transform trackingTransform = rb != null ? rb.transform : recovery.transform;
            bool invalidState = recovery.IsKnockedOrRecovering || recovery.IsLiquidSwept;
            string invalidStateReason = invalidState ? recovery.CurrentRecoveryState.ToString() : string.Empty;
            _hamsterRecoverySamples.Add(new HamsterRecoverySample
            {
                recovery = recovery,
                rigidbody = rb,
                trackingTransform = trackingTransform,
                previousPosition = trackingTransform != null ? trackingTransform.position : recovery.transform.position,
                speedSum = 0f,
                angularSpeedSum = 0f,
                maxFrameSpeed = 0f,
                maxFrameAngularSpeed = 0f,
                sampleCount = 0,
                invalidStateDetected = invalidState,
                invalidStateReason = invalidStateReason
            });
        }

        Log($"[MSDeveloper/RecoverySample] count={_hamsterRecoverySamples.Count}");
    }

    private void CaptureSample(float dt)
    {
        for (int i = 0; i < _samples.Count; i++)
        {
            PlayerSample sample = _samples[i];
            if (sample.status == null)
                continue;

            if (sample.status.IsEliminated)
                continue;

            if (IsInvalidState(sample.status, sample.characterController, out string stateReason))
            {
                sample.invalidStateDetected = true;
                sample.invalidStateReason = stateReason;
            }

            Transform trackingTransform = sample.trackingTransform != null
                ? sample.trackingTransform
                : ResolveTrackingTransform(sample.status, sample.characterController, sample.rigidbody);

            Vector3 currentPosition = trackingTransform != null ? trackingTransform.position : sample.status.transform.position;
            Vector3 positionDelta = currentPosition - sample.previousPosition;
            positionDelta.y = 0f;

            float transformDeltaSpeed = positionDelta.magnitude / dt;
            float speed = transformDeltaSpeed;
            float angularSpeed = 0f;
            string speedSource = "TransformDelta";

            if (sample.rigidbody != null && !sample.rigidbody.isKinematic)
            {
                float rigidbodySpeed = HorizontalMagnitude(GetRigidbodyVelocity(sample.rigidbody));
                if (rigidbodySpeed > speed)
                {
                    speed = rigidbodySpeed;
                    speedSource = "Rigidbody";
                }

                angularSpeed = sample.rigidbody.angularVelocity.magnitude;
            }

            if (sample.characterController != null && sample.characterController.enabled)
            {
                Vector3 ccVelocity = sample.characterController.velocity;
                float characterControllerSpeed = HorizontalMagnitude(ccVelocity);
                if (characterControllerSpeed > speed)
                {
                    speed = characterControllerSpeed;
                    speedSource = "CharacterController";
                }

                sample.hasGroundedInfo = true;
                sample.isGrounded = sample.characterController.isGrounded;
            }

            sample.speedSum += speed;
            sample.angularSpeedSum += angularSpeed;
            if (speed > sample.maxFrameSpeed)
            {
                sample.maxFrameSpeed = speed;
                sample.maxSpeedSource = speedSource;
            }

            if (angularSpeed > sample.maxFrameAngularSpeed)
                sample.maxFrameAngularSpeed = angularSpeed;

            sample.sampleCount++;
            sample.trackingTransform = trackingTransform;
            sample.previousPosition = currentPosition;
            _samples[i] = sample;
        }

        CaptureHamsterRecoverySample(dt);
    }

    private void CaptureHamsterRecoverySample(float dt)
    {
        for (int i = 0; i < _hamsterRecoverySamples.Count; i++)
        {
            HamsterRecoverySample sample = _hamsterRecoverySamples[i];
            if (sample.recovery == null)
                continue;

            if (sample.recovery.IsKnockedOrRecovering || sample.recovery.IsLiquidSwept)
            {
                sample.invalidStateDetected = true;
                sample.invalidStateReason = sample.recovery.CurrentRecoveryState.ToString();
            }

            Transform trackingTransform = sample.trackingTransform != null
                ? sample.trackingTransform
                : (sample.rigidbody != null ? sample.rigidbody.transform : sample.recovery.transform);
            Vector3 currentPosition = trackingTransform != null ? trackingTransform.position : sample.recovery.transform.position;
            Vector3 velocity = (currentPosition - sample.previousPosition) / Mathf.Max(dt, 0.0001f);
            float horizontalSpeed = HorizontalMagnitude(velocity);
            float angularSpeed = sample.rigidbody != null ? sample.rigidbody.angularVelocity.magnitude : 0f;

            sample.speedSum += horizontalSpeed;
            sample.angularSpeedSum += angularSpeed;
            sample.maxFrameSpeed = Mathf.Max(sample.maxFrameSpeed, horizontalSpeed);
            sample.maxFrameAngularSpeed = Mathf.Max(sample.maxFrameAngularSpeed, angularSpeed);
            sample.sampleCount++;
            sample.trackingTransform = trackingTransform;
            sample.previousPosition = currentPosition;
            _hamsterRecoverySamples[i] = sample;
        }
    }

    private void BuildResults()
    {
        _lastScanResults.Clear();
        _lastHamsterRecoveryScanResults.Clear();

        for (int i = 0; i < _samples.Count; i++)
        {
            PlayerSample sample = _samples[i];
            if (sample.status == null)
                continue;

            if (sample.status.IsEliminated)
                continue;

            int count = Mathf.Max(1, sample.sampleCount);
            float averageSpeed = sample.speedSum / count;
            float averageAngularSpeed = sample.angularSpeedSum / count;

            bool failed = false;
            string reason = "Success";

            if (sample.invalidStateDetected)
            {
                failed = true;
                reason = sample.invalidStateReason;
            }
            else if (averageSpeed > scanVelocityThreshold)
            {
                failed = true;
                reason = $"Average speed {averageSpeed:0.00} > threshold {scanVelocityThreshold:0.00}";
            }
            else if (averageAngularSpeed > scanAngularVelocityThreshold)
            {
                failed = true;
                reason = $"Average angular speed {averageAngularSpeed:0.00} > threshold {scanAngularVelocityThreshold:0.00}";
            }

            ScanResult result = new ScanResult
            {
                Status = sample.status,
                AverageSpeed = averageSpeed,
                AverageAngularSpeed = averageAngularSpeed,
                HasGroundedInfo = sample.hasGroundedInfo,
                IsGrounded = sample.isGrounded,
                Failed = failed,
                Reason = reason
            };

            _lastScanResults.Add(result);
            Log($"{LogPrefix} Scan result player:{sample.status.name} failed:{failed} avgSpeed:{averageSpeed:0.00}/{scanVelocityThreshold:0.00} avgAngular:{averageAngularSpeed:0.00}/{scanAngularVelocityThreshold:0.00} grounded:{FormatGrounded(sample.hasGroundedInfo, sample.isGrounded)} maxFrameSpeed:{sample.maxFrameSpeed:0.00} maxAngular:{sample.maxFrameAngularSpeed:0.00} maxSpeedSource:{sample.maxSpeedSource} reason:{reason}");
        }

        BuildHamsterRecoveryResults();
    }

    private void BuildHamsterRecoveryResults()
    {
        for (int i = 0; i < _hamsterRecoverySamples.Count; i++)
        {
            HamsterRecoverySample sample = _hamsterRecoverySamples[i];
            if (sample.recovery == null)
                continue;

            int count = Mathf.Max(1, sample.sampleCount);
            float averageSpeed = sample.speedSum / count;
            float averageAngularSpeed = sample.angularSpeedSum / count;
            bool failed = false;
            string reason = "Success";

            if (sample.invalidStateDetected)
            {
                failed = true;
                reason = sample.invalidStateReason;
            }
            else if (averageSpeed > scanVelocityThreshold)
            {
                failed = true;
                reason = $"Average speed {averageSpeed:0.00} > threshold {scanVelocityThreshold:0.00}";
            }
            else if (averageAngularSpeed > scanAngularVelocityThreshold)
            {
                failed = true;
                reason = $"Average angular speed {averageAngularSpeed:0.00} > threshold {scanAngularVelocityThreshold:0.00}";
            }

            _lastHamsterRecoveryScanResults.Add(new HamsterRecoveryScanResult
            {
                Recovery = sample.recovery,
                AverageSpeed = averageSpeed,
                AverageAngularSpeed = averageAngularSpeed,
                Failed = failed,
                Reason = reason
            });

            Log($"{LogPrefix} Scan result hamster:{sample.recovery.name} failed:{failed} avgSpeed:{averageSpeed:0.00}/{scanVelocityThreshold:0.00} avgAngular:{averageAngularSpeed:0.00}/{scanAngularVelocityThreshold:0.00} maxFrameSpeed:{sample.maxFrameSpeed:0.00} maxAngular:{sample.maxFrameAngularSpeed:0.00} reason:{reason}");
        }
    }

    private void ResolveFailures()
    {
        int failedCount = CountFailedResults();
        if (failedCount > 0)
            LogWarning($"{LogPrefix} Resolve failure feedback failedCount:{failedCount}. Applying knockback to failed players.");
        else
            Log($"{LogPrefix} Resolve failure feedback failedCount:0. No knockback targets.");

        for (int i = 0; i < _lastScanResults.Count; i++)
        {
            ScanResult result = _lastScanResults[i];
            if (!result.Failed || result.Status == null)
                continue;

            if (result.Status.IsEliminated)
            {
                Log($"{LogPrefix} Skip knockback. Player already eliminated:{result.Status.name}");
                continue;
            }

            Vector3 direction = result.Status.transform.position - transform.position;
            direction.y = 0f;

            if (direction.sqrMagnitude < 0.0001f)
                direction = result.Status.transform.forward;
            else
                direction.Normalize();

            Vector3 impulse = direction * knockbackForce + Vector3.up * upwardForce;
            result.Status.ApplyKnockbackServer(impulse);
            LogWarning($"{LogPrefix} Failure resolved player:{result.Status.name} impulse:{impulse} avgSpeed:{result.AverageSpeed:0.00}/{scanVelocityThreshold:0.00} avgAngular:{result.AverageAngularSpeed:0.00}/{scanAngularVelocityThreshold:0.00} grounded:{FormatGrounded(result.HasGroundedInfo, result.IsGrounded)} reason:{result.Reason}");
        }

        ResolveHamsterRecoveryFailures();
    }

    private void ResolveHamsterRecoveryFailures()
    {
        for (int i = 0; i < _lastHamsterRecoveryScanResults.Count; i++)
        {
            HamsterRecoveryScanResult result = _lastHamsterRecoveryScanResults[i];
            if (!result.Failed || result.Recovery == null)
                continue;

            Rigidbody body = result.Recovery.BodyRigidbody;
            Vector3 targetPosition = body != null ? body.worldCenterOfMass : result.Recovery.transform.position;
            Vector3 direction = targetPosition - transform.position;
            direction.y = 0f;

            if (direction.sqrMagnitude < 0.0001f)
                direction = result.Recovery.transform.forward;
            else
                direction.Normalize();

            Vector3 impulse = direction * knockbackForce + Vector3.up * upwardForce;
            bool applied = result.Recovery.ApplyGimmickImpact(impulse, targetPosition, "DeveloperIntrusion");
            LogWarning($"[MSDeveloper/Recovery] target={result.Recovery.name} impulse={impulse} result={applied} avgSpeed={result.AverageSpeed:0.00}/{scanVelocityThreshold:0.00} avgAngular={result.AverageAngularSpeed:0.00}/{scanAngularVelocityThreshold:0.00} reason={result.Reason}");
        }
    }

    private bool IsInvalidState(PlayerStatusModule status, CharacterController cc, out string reason)
    {
        reason = string.Empty;

        if (status == null)
        {
            reason = "Missing status";
            return true;
        }

        if (status.IsKnocked)
        {
            reason = "Knocked";
            return true;
        }

        if (status.IsStandingUp)
        {
            reason = "Standing up";
            return true;
        }

        if (cc != null && cc.enabled && !cc.isGrounded)
        {
            reason = "Airborne";
            return true;
        }

        // TODO: Hook precise attack/jump/dragged state when PlayerHub/PlayerCombat/PlayerStatus exposes read-only state APIs.
        return false;
    }

    private Transform ResolveTrackingTransform(PlayerStatusModule status, CharacterController cc, Rigidbody rb)
    {
        if (cc != null)
            return cc.transform;

        if (rb != null)
            return rb.transform;

        return status != null ? status.transform : null;
    }

    private float HorizontalMagnitude(Vector3 velocity)
    {
        velocity.y = 0f;
        return velocity.magnitude;
    }

    private Vector3 GetRigidbodyVelocity(Rigidbody rb)
    {
        if (rb == null)
            return Vector3.zero;

#if UNITY_6000_0_OR_NEWER
        return rb.linearVelocity;
#else
        return rb.velocity;
#endif
    }

    private IEnumerator WaitForSecondsSafe(float seconds)
    {
        float remaining = Mathf.Max(0f, seconds);
        while (remaining > 0f)
        {
            remaining -= Time.deltaTime;
            yield return null;
        }
    }

    private void SetPhase(Phase phase)
    {
        _phase = phase;
    }

    private void PlayOneShot(AudioClip clip)
    {
        if (audioSource == null || clip == null)
            return;

        audioSource.PlayOneShot(clip);
    }

    private void SpawnVfx(GameObject prefab)
    {
        if (prefab == null)
            return;

        Transform spawn = vfxSpawnPoint != null ? vfxSpawnPoint : transform;
        Instantiate(prefab, spawn.position, spawn.rotation);
    }

    private void ShowTelegraphText()
    {
        if (telegraphText == null)
            return;

        telegraphText.text = telegraphMessage;
        telegraphText.gameObject.SetActive(true);
    }

    private void HideTelegraphText()
    {
        if (telegraphText == null)
            return;

        telegraphText.gameObject.SetActive(false);
    }

    private void CleanupPresentationState()
    {
        HideTelegraphText();
        DeactivateDeveloperActor();
        ApplyDoorImmediate(false);
    }

    private IEnumerator PlayDeveloperEntranceSequence()
    {
        PrepareDeveloperActorForSequence();
        yield return RotateDoorRoutine(true);
        yield return WaitForSecondsSafe(waitAfterDoorOpen);
        ShowDeveloperActorAtAppearPoint();
        yield return RotateDoorRoutine(false);
    }

    private IEnumerator PlayDeveloperExitSequence()
    {
        yield return RotateDoorRoutine(true);
        HideDeveloperActor();
        yield return RotateDoorRoutine(false);
    }

    private IEnumerator RotateDoorRoutine(bool open)
    {
        if (doorPivot == null)
        {
            Log($"{LogPrefix} Door {(open ? "open" : "close")} skipped. doorPivot is null.");
            yield break;
        }

        Vector3 targetEuler = open ? doorOpenEuler : doorClosedEuler;
        float duration = Mathf.Max(0.01f, open ? doorOpenDuration : doorCloseDuration);
        Quaternion startRotation = doorPivot.localRotation;
        Quaternion targetRotation = Quaternion.Euler(targetEuler);
        float elapsed = 0f;

        Log($"{LogPrefix} Door {(open ? "opening" : "closing")}");

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            doorPivot.localRotation = Quaternion.Slerp(startRotation, targetRotation, t);
            yield return null;
        }

        doorPivot.localRotation = targetRotation;
        Log($"{LogPrefix} Door {(open ? "opened" : "closed")}");
    }

    private void ApplyDoorImmediate(bool open)
    {
        if (doorPivot == null)
            return;

        doorPivot.localRotation = Quaternion.Euler(open ? doorOpenEuler : doorClosedEuler);
    }

    private void PrepareDeveloperActorForSequence()
    {
        DeactivateDeveloperActor();
        ApplyDoorImmediate(false);
    }

    private void ShowDeveloperActorAtAppearPoint()
    {
        if (developerActor == null)
        {
            Log($"{LogPrefix} Actor show skipped. developerActor is null.");
            return;
        }

        Transform appearPoint = ResolveDeveloperAppearPoint();
        if (appearPoint != null)
        {
            SetDeveloperActorPose(appearPoint);
        }
        else
        {
            Log($"{LogPrefix} Actor appear point missing. Keeping current actor position.");
        }

        developerActor.SetActive(true);
        Log($"{LogPrefix} Developer actor shown");
    }

    private void HideDeveloperActor()
    {
        if (developerActor == null)
            return;

        developerActor.SetActive(false);
        Log($"{LogPrefix} Developer actor hidden");
    }

    private void SetDeveloperActorPose(Transform targetPoint)
    {
        if (developerActor == null || targetPoint == null)
            return;

        developerActor.transform.SetPositionAndRotation(targetPoint.position, targetPoint.rotation);
    }

    private void DeactivateDeveloperActor()
    {
        if (developerActor == null)
            return;

        developerActor.SetActive(false);
    }

    private void RequestEntranceSequence()
    {
        Log($"{LogPrefix} Entrance sequence requested.");

        if (_entranceSequenceRequested)
        {
            Log($"{LogPrefix} Entrance sequence skipped. Already requested.");
            return;
        }

        _entranceSequenceRequested = true;
        _exitSequenceRequested = false;
        StartPresentationSequence(PlayDeveloperEntranceSequence());
    }

    private void RequestExitSequence()
    {
        Log($"{LogPrefix} Exit sequence requested.");

        if (_exitSequenceRequested)
        {
            Log($"{LogPrefix} Exit sequence skipped. Already requested.");
            return;
        }

        _exitSequenceRequested = true;
        StartPresentationSequence(PlayDeveloperExitSequence());
    }

    private void StartPresentationSequence(IEnumerator sequence)
    {
        if (sequence == null)
            return;

        if (_presentationSequenceRoutine != null)
        {
            StopCoroutine(_presentationSequenceRoutine);
            _presentationSequenceRoutine = null;
        }

        _presentationSequenceRoutine = StartCoroutine(RunPresentationSequence(sequence));
    }

    private IEnumerator RunPresentationSequence(IEnumerator sequence)
    {
        yield return sequence;
        _presentationSequenceRoutine = null;
    }

    private void ResetPresentationSequenceState(bool stopActiveSequence)
    {
        if (stopActiveSequence && _presentationSequenceRoutine != null)
        {
            StopCoroutine(_presentationSequenceRoutine);
            _presentationSequenceRoutine = null;
        }

        _entranceSequenceRequested = false;
        _exitSequenceRequested = false;
    }

    private Transform ResolveDeveloperAppearPoint()
    {
        if (developerScanPoint != null)
            return developerScanPoint;

        if (developerSpawnPoint != null)
            return developerSpawnPoint;

        return null;
    }

    private int CountFailedResults()
    {
        int failedCount = 0;
        for (int i = 0; i < _lastScanResults.Count; i++)
        {
            ScanResult result = _lastScanResults[i];
            if (!result.Failed || result.Status == null)
                continue;

            if (result.Status.IsEliminated)
                continue;

            failedCount++;
        }

        for (int i = 0; i < _lastHamsterRecoveryScanResults.Count; i++)
        {
            HamsterRecoveryScanResult result = _lastHamsterRecoveryScanResults[i];
            if (result.Failed && result.Recovery != null)
                failedCount++;
        }

        return failedCount;
    }

    private string FormatGrounded(bool hasGroundedInfo, bool isGrounded)
    {
        if (!hasGroundedInfo)
            return "n/a";

        return isGrounded ? "true" : "false";
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
