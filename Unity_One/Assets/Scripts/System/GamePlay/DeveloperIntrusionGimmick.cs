using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

public class DeveloperIntrusionGimmick : MonoBehaviour
{
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
    [SerializeField] private float telegraphDuration = 1.5f;
    [SerializeField] private float responseDuration = 2.0f;
    [SerializeField] private float scanSampleDuration = 0.35f;
    [SerializeField] private float resolveDuration = 1.0f;

    [Header("Scan")]
    [SerializeField] private float scanVelocityThreshold = 0.35f;
    [SerializeField] private float scanAngularVelocityThreshold = 1.2f;

    [Header("Resolve")]
    [SerializeField] private float knockbackForce = 14f;
    [SerializeField] private float upwardForce = 4f;

    [Header("Presentation Hooks")]
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private AudioClip telegraphClip;
    [SerializeField] private AudioClip responseClip;
    [SerializeField] private AudioClip scanClip;
    [SerializeField] private AudioClip resolveClip;
    [SerializeField] private AudioClip endClip;
    [SerializeField] private GameObject telegraphVfxPrefab;
    [SerializeField] private GameObject responseVfxPrefab;
    [SerializeField] private GameObject scanVfxPrefab;
    [SerializeField] private GameObject resolveVfxPrefab;
    [SerializeField] private Transform vfxSpawnPoint;
    [SerializeField] private UnityEvent onTelegraph;
    [SerializeField] private UnityEvent onResponse;
    [SerializeField] private UnityEvent onScan;
    [SerializeField] private UnityEvent onResolve;
    [SerializeField] private UnityEvent onEnd;

    [Header("Debug")]
    [SerializeField] private bool enableDebugLogs = true;

    private readonly List<PlayerSample> _samples = new List<PlayerSample>();
    private readonly List<ScanResult> _lastScanResults = new List<ScanResult>();
    private Coroutine _runningRoutine;
    private Phase _phase = Phase.Idle;

    public Phase CurrentPhase => _phase;
    public bool IsRunning => _runningRoutine != null;
    public IReadOnlyList<ScanResult> LastScanResults => _lastScanResults;

    public struct ScanResult
    {
        public PlayerStatusModule Status;
        public float AverageSpeed;
        public float AverageAngularSpeed;
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
        public int sampleCount;
        public bool invalidStateDetected;
        public string invalidStateReason;
    }

    public Coroutine StartGimmick(IList<PlayerStatusModule> players, bool playPresentationInternally = true)
    {
        if (IsRunning)
        {
            Log("[DeveloperIntrusion] Start ignored. Gimmick is already running.");
            return _runningRoutine;
        }

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

        SetPhase(Phase.Idle);
    }

    public void PlayTelegraphPresentation()
    {
        Log("[DeveloperIntrusion] Telegraph");
        PlayOneShot(telegraphClip);
        SpawnVfx(telegraphVfxPrefab);
        onTelegraph?.Invoke();
    }

    public void PlayResponsePresentation()
    {
        Log("[DeveloperIntrusion] Response");
        PlayOneShot(responseClip);
        SpawnVfx(responseVfxPrefab);
        onResponse?.Invoke();
    }

    public void PlayScanPresentation()
    {
        Log("[DeveloperIntrusion] Scan");
        PlayOneShot(scanClip);
        SpawnVfx(scanVfxPrefab);
        onScan?.Invoke();
    }

    public void PlayResolvePresentation()
    {
        Log("[DeveloperIntrusion] Resolve");
        PlayOneShot(resolveClip);
        SpawnVfx(resolveVfxPrefab);
        onResolve?.Invoke();
    }

    public void PlayEndPresentation()
    {
        Log("[DeveloperIntrusion] End");
        PlayOneShot(endClip);
        onEnd?.Invoke();
    }

    private IEnumerator RunRoutine(IList<PlayerStatusModule> players, bool playPresentationInternally)
    {
        _lastScanResults.Clear();

        SetPhase(Phase.Telegraph);
        if (playPresentationInternally)
            PlayTelegraphPresentation();
        yield return WaitForSecondsSafe(telegraphDuration);

        SetPhase(Phase.Response);
        if (playPresentationInternally)
            PlayResponsePresentation();
        yield return WaitForSecondsSafe(responseDuration);

        SetPhase(Phase.Scan);
        if (playPresentationInternally)
            PlayScanPresentation();
        yield return SamplePlayersRoutine(players);

        SetPhase(Phase.Resolve);
        if (playPresentationInternally)
            PlayResolvePresentation();
        ResolveFailures();
        yield return WaitForSecondsSafe(resolveDuration);

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

        if (players == null)
            return;

        for (int i = 0; i < players.Count; i++)
        {
            PlayerStatusModule status = players[i];
            if (status == null)
                continue;

            if (status.IsEliminated)
            {
                Log($"[DeveloperIntrusion] Skip eliminated player:{status.name}");
                continue;
            }

            CharacterController cc = status.GetComponentInParent<CharacterController>();
            Rigidbody rb = status.GetComponentInParent<Rigidbody>();
            Transform trackingTransform = ResolveTrackingTransform(status, cc, rb);
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
                sampleCount = 0,
                invalidStateDetected = invalidState,
                invalidStateReason = invalidStateReason
            });
        }
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

            float speed = positionDelta.magnitude / dt;
            float angularSpeed = 0f;

            if (sample.rigidbody != null && !sample.rigidbody.isKinematic)
            {
                speed = Mathf.Max(speed, HorizontalMagnitude(GetRigidbodyVelocity(sample.rigidbody)));
                angularSpeed = sample.rigidbody.angularVelocity.magnitude;
            }

            if (sample.characterController != null && sample.characterController.enabled)
            {
                Vector3 ccVelocity = sample.characterController.velocity;
                speed = Mathf.Max(speed, HorizontalMagnitude(ccVelocity));
            }

            sample.speedSum += speed;
            sample.angularSpeedSum += angularSpeed;
            sample.sampleCount++;
            sample.trackingTransform = trackingTransform;
            sample.previousPosition = currentPosition;
            _samples[i] = sample;
        }
    }

    private void BuildResults()
    {
        _lastScanResults.Clear();

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
                Failed = failed,
                Reason = reason
            };

            _lastScanResults.Add(result);
            Log($"[DeveloperIntrusion] Scan result player:{sample.status.name} failed:{failed} speed:{averageSpeed:0.00} angular:{averageAngularSpeed:0.00} reason:{reason}");
        }
    }

    private void ResolveFailures()
    {
        for (int i = 0; i < _lastScanResults.Count; i++)
        {
            ScanResult result = _lastScanResults[i];
            if (!result.Failed || result.Status == null)
                continue;

            if (result.Status.IsEliminated)
            {
                Log($"[DeveloperIntrusion] Skip knockback. Player already eliminated:{result.Status.name}");
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
            Log($"[DeveloperIntrusion] Apply failure knockback player:{result.Status.name} impulse:{impulse} reason:{result.Reason}");
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

    private void Log(string message)
    {
        if (!enableDebugLogs)
            return;

        Debug.Log(message, this);
    }
}
