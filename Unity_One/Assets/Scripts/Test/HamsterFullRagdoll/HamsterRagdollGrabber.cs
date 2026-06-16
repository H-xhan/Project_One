using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public sealed class HamsterRagdollGrabber : MonoBehaviour
{
    private const int MaxGrabHits = 32;
    private static readonly Collider[] GrabHits = new Collider[MaxGrabHits];

    [Header("References")]
    [SerializeField] private Rigidbody targetBody;
    [SerializeField] private Transform aimReference;
    [SerializeField] private Transform holdReference;
    [SerializeField] private bool autoFindReferences = true;

    [Header("Test Input")]
    [SerializeField] private bool enableTestInput = true;
    [SerializeField] private KeyCode grabDropKey = KeyCode.E;
    [SerializeField] private KeyCode throwKey = KeyCode.Q;
    [SerializeField] private bool allowMouseInput = true;

    [Header("Grab")]
    [SerializeField] private float grabDistance = 1.4f;
    [SerializeField] private float grabRadius = 0.35f;
    [SerializeField] private LayerMask grabbableMask = ~0;
    [SerializeField] private Vector3 holdLocalOffset = new Vector3(0f, 0.45f, 0.75f);

    [Header("Follow")]
    [SerializeField] private float followSpring = 45f;
    [SerializeField] private float followDamping = 10f;
    [SerializeField] private float maxFollowAcceleration = 90f;

    [Header("Throw")]
    [SerializeField] private float throwForwardImpulse = 6f;
    [SerializeField] private float throwUpImpulse = 2f;
    [SerializeField] private float throwTorqueImpulse = 0.5f;

    [Header("Throw Direction")]
    [SerializeField] private bool useCharacterForwardForThrow = true;
    [SerializeField] private Transform characterForwardReference;
    [SerializeField] private bool autoUseTargetBodyAsCharacterForwardReference = true;
    [SerializeField] private bool captureThrowDirectionOnRequest = true;
    [SerializeField] private bool flattenThrowDirectionOnGroundPlane = true;
    [SerializeField] private bool fallbackToAimReferenceForThrowDirection = true;

    [Header("Throw Facing Cache")]
    [SerializeField] private bool useCachedPlanarFacingForThrow = true;
    [SerializeField] private bool preferCachedPlanarFacingOverReference = true;
    [SerializeField] private Transform facingMotionSource;
    [SerializeField] private bool autoUseTargetBodyAsFacingMotionSource = true;
    [SerializeField] private float minFacingCacheSpeed = 0.05f;
    [SerializeField] private bool debugThrowDirectionLogs = false;

    [Header("Collision")]
    [SerializeField] private float releaseCollisionRestoreDelay = 0.15f;
    [SerializeField] private bool ignoreHolderCollisionWhileHeld = true;

    [Header("Animation Event Timing")]
    [SerializeField] private bool useAnimationEventTiming = true;
    [SerializeField] private bool useGrabAnimationEvent = true;
    [SerializeField] private bool useThrowAnimationEvent = true;
    [SerializeField] private bool useFallbackTimingIfEventMissing = true;
    [SerializeField] private float grabFallbackDelay = 0.55f;
    [SerializeField] private float throwFallbackDelay = 0.65f;
    [SerializeField] private bool debugAnimationEventTimingLogs = false;

    [Header("Debug")]
    [SerializeField] private bool debugLogs = false;
    [SerializeField] private bool drawDebugGizmos = true;

    private readonly List<CollisionIgnorePair> _ignoredPairs = new List<CollisionIgnorePair>();
    private Collider[] _holderColliders;
    private HamsterRagdollGrabbable _heldTarget;
    private Coroutine _restoreCollisionRoutine;
    private bool _legacyInputUnavailable;
    private bool _hasLastHoldPoint;
    private Vector3 _lastHoldPoint;
    private Vector3 _previousHoldPoint;
    private Vector3 _holdVelocity;
    private float _lastGrabTime = float.NegativeInfinity;
    private float _lastDropTime = float.NegativeInfinity;
    private float _lastThrowTime = float.NegativeInfinity;
    private int _grabCount;
    private int _dropCount;
    private int _throwCount;
    private HamsterRagdollGrabbable _pendingGrabTarget;
    private bool _hasPendingGrab;
    private bool _hasPendingThrow;
    private Vector3 _pendingThrowForward;
    private bool _hasPendingThrowForward;
    private Vector3 _lastResolvedThrowForward = Vector3.forward;
    private Vector3 _lastPlanarFacingForward = Vector3.forward;
    private bool _hasLastPlanarFacingForward;
    private Vector3 _previousFacingSourcePosition;
    private bool _hasPreviousFacingSourcePosition;
    private float _lastGrabRequestTime = float.NegativeInfinity;
    private float _lastThrowRequestTime = float.NegativeInfinity;
    private int _grabRequestCount;
    private int _throwRequestCount;

    public bool IsHolding => _heldTarget != null;
    public HamsterRagdollGrabbable HeldTarget => _heldTarget;
    public bool HasPendingGrab => _hasPendingGrab;
    public bool HasPendingThrow => _hasPendingThrow;
    public float LastGrabTime => _lastGrabTime;
    public float LastDropTime => _lastDropTime;
    public float LastThrowTime => _lastThrowTime;
    public int GrabCount => _grabCount;
    public int DropCount => _dropCount;
    public int ThrowCount => _throwCount;
    public int GrabRequestCount => _grabRequestCount;
    public int ThrowRequestCount => _throwRequestCount;
    public float LastGrabRequestTime => _lastGrabRequestTime;
    public float LastThrowRequestTime => _lastThrowRequestTime;
    public Vector3 LastResolvedThrowForward => _lastResolvedThrowForward;
    public Vector3 CurrentHoldPoint => Application.isPlaying && _hasLastHoldPoint ? _lastHoldPoint : GetHoldPoint();

    private struct CollisionIgnorePair
    {
        public Collider holderCollider;
        public Collider targetCollider;
    }

    private void Reset()
    {
        AutoFindReferences();
        CacheHolderColliders();
        ClampValues();
    }

    private void Awake()
    {
        if (autoFindReferences)
            AutoFindReferences();

        CacheHolderColliders();
    }

    private void OnEnable()
    {
        if (autoFindReferences)
            AutoFindReferences();

        CacheHolderColliders();
        _hasLastHoldPoint = false;
    }

    private void OnDisable()
    {
        CancelPendingInteraction();
        ForceReleaseHeldTarget();
        RestoreIgnoredCollisionsNow();
    }

    private void OnDestroy()
    {
        CancelPendingInteraction();
        ForceReleaseHeldTarget();
        RestoreIgnoredCollisionsNow();
    }

    private void OnValidate()
    {
        ClampValues();
    }

    private void Update()
    {
        UpdatePlanarFacingCache();
        TickPendingInteractionFallbacks();

        if (!enableTestInput || _legacyInputUnavailable)
            return;

        if (ReadGrabDropPressed())
            HandleGrabDropInput();

        if (ReadThrowPressed())
            HandleThrowInput();
    }

    private void FixedUpdate()
    {
        if (_heldTarget == null)
            return;

        Vector3 holdPoint = GetHoldPoint();
        UpdateHoldVelocity(holdPoint, Time.fixedDeltaTime);
        _heldTarget.ApplyHoldForce(
            holdPoint,
            _holdVelocity,
            followSpring,
            followDamping,
            maxFollowAcceleration,
            Time.fixedDeltaTime);

        if (!_heldTarget.IsHeld)
        {
            Log("Held target released itself.");
            _heldTarget = null;
            ScheduleCollisionRestore();
        }
    }

    public bool TryGrabBestTarget()
    {
        if (targetBody == null && autoFindReferences)
            AutoFindReferences();

        if (_heldTarget != null)
            return false;

        HamsterRagdollGrabbable target = FindBestTarget();
        if (target == null)
        {
            Log("Grab skipped. No valid target.");
            return false;
        }

        return TryGrabTargetNow(target, "Immediate");
    }

    public void DropHeldTarget()
    {
        CancelPendingThrow("drop input");

        if (_heldTarget == null)
            return;

        HamsterRagdollGrabbable target = _heldTarget;
        _heldTarget = null;
        _lastDropTime = Time.time;
        _dropCount++;
        target.ReleaseHold(Vector3.zero, Vector3.zero);
        ScheduleCollisionRestore();
        Log($"Dropped {target.name}");
    }

    public void ThrowHeldTarget()
    {
        CancelPendingThrow("immediate throw");
        ThrowHeldTargetNow("Immediate");
    }

    public void CompletePendingGrabFromAnimationEvent()
    {
        if (!useAnimationEventTiming || !useGrabAnimationEvent)
        {
            LogTiming("Grab animation event ignored because event timing is disabled.");
            return;
        }

        CompletePendingGrab("AnimationEvent");
    }

    public void CompletePendingThrowFromAnimationEvent()
    {
        if (!useAnimationEventTiming || !useThrowAnimationEvent)
        {
            LogTiming("Throw animation event ignored because event timing is disabled.");
            return;
        }

        CompletePendingThrow("AnimationEvent");
    }

    public void CompletePendingDropFromAnimationEvent()
    {
        LogTiming("Drop animation event received. No pending drop action is used in this test shell.");
    }

    public void CancelPendingInteraction()
    {
        CancelPendingGrab("cancel pending interaction");
        CancelPendingThrow("cancel pending interaction");
    }

    private void HandleGrabDropInput()
    {
        if (_hasPendingGrab)
        {
            CancelPendingGrab("grab/drop input while grab is pending");
            return;
        }

        if (_hasPendingThrow)
        {
            LogTiming("Grab/drop input ignored while throw is pending.");
            return;
        }

        if (_heldTarget != null)
        {
            DropHeldTarget();
            return;
        }

        if (useAnimationEventTiming)
        {
            RequestPendingGrab();
            return;
        }

        TryGrabBestTarget();
    }

    private void HandleThrowInput()
    {
        if (_hasPendingGrab)
        {
            LogTiming("Throw input ignored while grab is pending.");
            return;
        }

        if (_heldTarget == null)
            return;

        if (useAnimationEventTiming)
        {
            RequestPendingThrow();
            return;
        }

        ThrowHeldTarget();
    }

    private void RequestPendingGrab()
    {
        if (targetBody == null && autoFindReferences)
            AutoFindReferences();

        if (_heldTarget != null)
            return;

        if (_hasPendingGrab)
        {
            LogTiming("Grab request ignored because a grab is already pending.");
            return;
        }

        HamsterRagdollGrabbable target = FindBestTarget();
        if (target == null)
        {
            Log("Grab request skipped. No valid target.");
            return;
        }

        _pendingGrabTarget = target;
        _hasPendingGrab = true;
        _lastGrabRequestTime = Time.time;
        _grabRequestCount++;
        LogTiming($"Pending grab requested target={target.name}");
    }

    private void RequestPendingThrow()
    {
        if (_heldTarget == null)
            return;

        if (_hasPendingThrow)
        {
            LogTiming("Throw request ignored because a throw is already pending.");
            return;
        }

        _hasPendingThrow = true;
        if (captureThrowDirectionOnRequest)
        {
            _pendingThrowForward = ResolveCharacterThrowForward("ThrowRequest");
            _hasPendingThrowForward = true;
        }
        else
        {
            ClearPendingThrowForward();
        }

        _lastThrowRequestTime = Time.time;
        _throwRequestCount++;
        string forwardLog = _hasPendingThrowForward ? FormatVector(_pendingThrowForward) : "deferred";
        LogTiming($"Pending throw requested target={_heldTarget.name} forward={forwardLog}");
    }

    private void TickPendingInteractionFallbacks()
    {
        if (!useAnimationEventTiming || !useFallbackTimingIfEventMissing)
            return;

        float now = Time.time;
        if (_hasPendingGrab && now - _lastGrabRequestTime >= grabFallbackDelay)
            CompletePendingGrab("FallbackDelay");

        if (_hasPendingThrow && now - _lastThrowRequestTime >= throwFallbackDelay)
            CompletePendingThrow("FallbackDelay");
    }

    private void CompletePendingGrab(string reason)
    {
        if (!_hasPendingGrab)
            return;

        HamsterRagdollGrabbable target = _pendingGrabTarget;
        _pendingGrabTarget = null;
        _hasPendingGrab = false;

        if (!IsValidTarget(target))
        {
            Debug.LogWarning($"[HamsterRagdollGrabber:{name}] Pending grab canceled because target is no longer valid. reason={reason}", this);
            return;
        }

        TryGrabTargetNow(target, reason);
    }

    private void CompletePendingThrow(string reason)
    {
        if (!_hasPendingThrow)
            return;

        _hasPendingThrow = false;
        if (_heldTarget == null)
        {
            ClearPendingThrowForward();
            Debug.LogWarning($"[HamsterRagdollGrabber:{name}] Pending throw canceled because there is no held target. reason={reason}", this);
            return;
        }

        ThrowHeldTargetNow(reason);
    }

    private void CancelPendingGrab(string reason)
    {
        if (!_hasPendingGrab)
            return;

        LogTiming($"Pending grab canceled. reason={reason}");
        _pendingGrabTarget = null;
        _hasPendingGrab = false;
    }

    private void CancelPendingThrow(string reason)
    {
        ClearPendingThrowForward();

        if (!_hasPendingThrow)
            return;

        LogTiming($"Pending throw canceled. reason={reason}");
        _hasPendingThrow = false;
    }

    private bool TryGrabTargetNow(HamsterRagdollGrabbable target, string reason)
    {
        if (!target.TryBeginHold(this))
            return false;

        _heldTarget = target;
        _lastGrabTime = Time.time;
        _grabCount++;
        _hasLastHoldPoint = false;
        RestoreIgnoredCollisionsNow();
        ApplyCollisionIgnore(target);
        Log($"Grabbed {target.name} reason={reason}");
        return true;
    }

    private void ThrowHeldTargetNow(string reason)
    {
        if (_heldTarget == null)
            return;

        HamsterRagdollGrabbable target = _heldTarget;
        _heldTarget = null;
        _lastThrowTime = Time.time;
        _throwCount++;

        Vector3 forward = GetThrowForwardForRelease(reason);
        Vector3 impulse = forward * Mathf.Max(0f, throwForwardImpulse) + Vector3.up * Mathf.Max(0f, throwUpImpulse);
        Vector3 torque = Vector3.Cross(Vector3.up, forward) * Mathf.Max(0f, throwTorqueImpulse);

        target.ReleaseHold(impulse, torque);
        ClearPendingThrowForward();
        ScheduleCollisionRestore();
        Log($"Threw {target.name} reason={reason} impulse={FormatVector(impulse)} torque={FormatVector(torque)}");
    }

    private void ForceReleaseHeldTarget()
    {
        CancelPendingInteraction();

        if (_heldTarget == null)
            return;

        HamsterRagdollGrabbable target = _heldTarget;
        _heldTarget = null;
        target.ForceReleaseWithoutImpulse();
        Log($"Force released {target.name}");
    }

    private HamsterRagdollGrabbable FindBestTarget()
    {
        Vector3 origin = GetGrabOrigin();
        Vector3 forward = GetPlanarAimForward();
        float searchDistance = Mathf.Max(0f, grabDistance);
        float radius = Mathf.Max(0.01f, grabRadius);
        Vector3 center = origin + forward * (searchDistance * 0.5f);
        float broadPhaseRadius = searchDistance * 0.5f + radius;

        int hitCount = Physics.OverlapSphereNonAlloc(
            center,
            broadPhaseRadius,
            GrabHits,
            grabbableMask,
            QueryTriggerInteraction.Ignore);

        HamsterRagdollGrabbable best = null;
        float bestScore = float.MaxValue;
        Vector3 searchOrigin = origin;

        for (int i = 0; i < hitCount; i++)
        {
            Collider hit = GrabHits[i];
            GrabHits[i] = null;
            if (hit == null)
                continue;

            if (ShouldIgnoreCandidateCollider(hit))
                continue;

            HamsterRagdollGrabbable candidate = hit.GetComponentInParent<HamsterRagdollGrabbable>();
            if (candidate == null)
                candidate = hit.GetComponentInChildren<HamsterRagdollGrabbable>();

            if (!IsValidTarget(candidate))
                continue;

            Rigidbody candidateBody = candidate.Body;
            Vector3 candidatePosition = candidateBody != null ? candidateBody.worldCenterOfMass : candidate.transform.position;
            Vector3 toCandidate = candidatePosition - searchOrigin;
            float forwardDistance = Vector3.Dot(toCandidate, forward);
            if (forwardDistance < -radius || forwardDistance > searchDistance + radius)
                continue;

            float lateralDistance = (toCandidate - forward * forwardDistance).magnitude;
            if (lateralDistance > radius)
                continue;

            float score = Mathf.Max(0f, forwardDistance) + lateralDistance * 0.5f;
            if (score >= bestScore)
                continue;

            bestScore = score;
            best = candidate;
        }

        return best;
    }

    private bool IsValidTarget(HamsterRagdollGrabbable candidate)
    {
        if (candidate == null || !candidate.CanBeGrabbed)
            return false;

        if (candidate.gameObject == gameObject)
            return false;

        Transform candidateTransform = candidate.transform;
        if (candidateTransform == transform || candidateTransform.IsChildOf(transform))
            return false;

        if (IsInVisualPreviewRoot(candidateTransform))
            return false;

        Rigidbody candidateBody = candidate.Body;
        if (candidateBody != null && targetBody != null && candidateBody == targetBody)
            return false;

        return true;
    }

    private bool ShouldIgnoreCandidateCollider(Collider candidate)
    {
        if (candidate == null)
            return true;

        if (IsVisualPreviewCollider(candidate))
            return true;

        if (_holderColliders == null)
            return false;

        for (int i = 0; i < _holderColliders.Length; i++)
        {
            if (_holderColliders[i] == candidate)
                return true;
        }

        return false;
    }

    private void ApplyCollisionIgnore(HamsterRagdollGrabbable target)
    {
        if (!ignoreHolderCollisionWhileHeld || target == null)
            return;

        if (_holderColliders == null || _holderColliders.Length == 0)
            CacheHolderColliders();

        Collider[] targetColliders = target.Colliders;
        if (_holderColliders == null || targetColliders == null)
            return;

        for (int holderIndex = 0; holderIndex < _holderColliders.Length; holderIndex++)
        {
            Collider holderCollider = _holderColliders[holderIndex];
            if (holderCollider == null || IsVisualPreviewCollider(holderCollider))
                continue;

            for (int targetIndex = 0; targetIndex < targetColliders.Length; targetIndex++)
            {
                Collider targetCollider = targetColliders[targetIndex];
                if (targetCollider == null || holderCollider == targetCollider)
                    continue;

                if (HasPair(holderCollider, targetCollider))
                    continue;

                Physics.IgnoreCollision(holderCollider, targetCollider, true);
                _ignoredPairs.Add(new CollisionIgnorePair
                {
                    holderCollider = holderCollider,
                    targetCollider = targetCollider
                });
            }
        }

        Log($"Collision ignore pair count={_ignoredPairs.Count}");
    }

    private void ScheduleCollisionRestore()
    {
        if (_restoreCollisionRoutine != null)
            StopCoroutine(_restoreCollisionRoutine);

        float delay = Mathf.Max(0f, releaseCollisionRestoreDelay);
        if (delay <= 0f || !isActiveAndEnabled)
        {
            RestoreIgnoredCollisionsNow();
            return;
        }

        _restoreCollisionRoutine = StartCoroutine(RestoreIgnoredCollisionsAfterDelay(delay));
    }

    private IEnumerator RestoreIgnoredCollisionsAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);
        RestoreIgnoredCollisionsNow();
    }

    private void RestoreIgnoredCollisionsNow()
    {
        if (_restoreCollisionRoutine != null)
        {
            StopCoroutine(_restoreCollisionRoutine);
            _restoreCollisionRoutine = null;
        }

        for (int i = 0; i < _ignoredPairs.Count; i++)
        {
            CollisionIgnorePair pair = _ignoredPairs[i];
            if (pair.holderCollider == null || pair.targetCollider == null)
                continue;

            Physics.IgnoreCollision(pair.holderCollider, pair.targetCollider, false);
        }

        if (_ignoredPairs.Count > 0)
            Log($"Collision restored pair count={_ignoredPairs.Count}");

        _ignoredPairs.Clear();
    }

    private bool HasPair(Collider first, Collider second)
    {
        for (int i = 0; i < _ignoredPairs.Count; i++)
        {
            CollisionIgnorePair pair = _ignoredPairs[i];
            if (pair.holderCollider == first && pair.targetCollider == second)
                return true;

            if (pair.holderCollider == second && pair.targetCollider == first)
                return true;
        }

        return false;
    }

    private void UpdateHoldVelocity(Vector3 holdPoint, float deltaTime)
    {
        if (!_hasLastHoldPoint)
        {
            _hasLastHoldPoint = true;
            _previousHoldPoint = holdPoint;
            _lastHoldPoint = holdPoint;
            _holdVelocity = Vector3.zero;
            return;
        }

        float safeDeltaTime = Mathf.Max(0.0001f, deltaTime);
        _previousHoldPoint = _lastHoldPoint;
        _lastHoldPoint = holdPoint;
        _holdVelocity = (_lastHoldPoint - _previousHoldPoint) / safeDeltaTime;
    }

    private Vector3 GetHoldPoint()
    {
        Transform reference = holdReference != null ? holdReference : transform;
        return reference.TransformPoint(holdLocalOffset);
    }

    private Vector3 GetGrabOrigin()
    {
        Transform reference = holdReference != null ? holdReference : transform;
        return reference.position;
    }

    private Vector3 GetPlanarAimForward()
    {
        Transform reference = aimReference != null ? aimReference : transform;
        Vector3 forward = reference.forward;
        forward.y = 0f;

        if (!IsFinite(forward) || forward.sqrMagnitude <= 0.0001f)
        {
            forward = transform.forward;
            forward.y = 0f;
        }

        if (!IsFinite(forward) || forward.sqrMagnitude <= 0.0001f)
            return Vector3.forward;

        return forward.normalized;
    }

    private Vector3 GetThrowForwardForRelease(string reason)
    {
        if (_hasPendingThrowForward && IsFinite(_pendingThrowForward) && _pendingThrowForward.sqrMagnitude > 0.0001f)
            return RememberResolvedThrowForward(_pendingThrowForward, "PendingThrowForward", reason);

        return ResolveCharacterThrowForward(reason);
    }

    private Vector3 ResolveCharacterThrowForward(string reason)
    {
        if (!useCharacterForwardForThrow)
            return RememberResolvedThrowForward(GetPlanarAimForward(), "PlanarAimForward", reason);

        if (preferCachedPlanarFacingOverReference && TryGetCachedPlanarFacing(out Vector3 forward))
            return RememberResolvedThrowForward(forward, "CachedPlanarFacing", reason);

        if (TryResolveThrowForward(characterForwardReference, out forward))
            return RememberResolvedThrowForward(forward, "CharacterForwardReference", reason);

        if (autoUseTargetBodyAsCharacterForwardReference && targetBody != null && TryResolveThrowForward(targetBody.transform, out forward))
            return RememberResolvedThrowForward(forward, "TargetBodyForward", reason);

        if (TryResolveThrowForward(transform, out forward))
            return RememberResolvedThrowForward(forward, "GrabberTransformForward", reason);

        if (fallbackToAimReferenceForThrowDirection && TryResolveThrowForward(aimReference, out forward))
            return RememberResolvedThrowForward(forward, "AimReferenceForward", reason);

        if (TryGetCachedPlanarFacing(out forward))
            return RememberResolvedThrowForward(forward, "CachedPlanarFacingFallback", reason);

        return RememberResolvedThrowForward(Vector3.forward, "Vector3.forward", reason);
    }

    private void UpdatePlanarFacingCache()
    {
        if (!useCachedPlanarFacingForThrow)
        {
            _hasPreviousFacingSourcePosition = false;
            return;
        }

        Transform source = ResolveFacingMotionSource();
        if (source == null)
        {
            _hasPreviousFacingSourcePosition = false;
            return;
        }

        Vector3 currentPosition = source.position;
        if (!IsFinite(currentPosition))
            return;

        if (!_hasPreviousFacingSourcePosition)
        {
            _previousFacingSourcePosition = currentPosition;
            _hasPreviousFacingSourcePosition = true;
            return;
        }

        Vector3 planarDelta = Vector3.ProjectOnPlane(currentPosition - _previousFacingSourcePosition, Vector3.up);
        _previousFacingSourcePosition = currentPosition;

        float safeDeltaTime = Mathf.Max(Time.deltaTime, 0.0001f);
        float planarSpeed = planarDelta.magnitude / safeDeltaTime;
        if (!IsFinite(planarSpeed) || planarSpeed < minFacingCacheSpeed || planarDelta.sqrMagnitude <= 0.0001f)
            return;

        _lastPlanarFacingForward = planarDelta.normalized;
        _hasLastPlanarFacingForward = true;
    }

    private Transform ResolveFacingMotionSource()
    {
        if (facingMotionSource != null)
            return facingMotionSource;

        if (autoUseTargetBodyAsFacingMotionSource && targetBody != null)
            return targetBody.transform;

        if (characterForwardReference != null)
            return characterForwardReference;

        return transform;
    }

    private bool TryGetCachedPlanarFacing(out Vector3 forward)
    {
        forward = Vector3.zero;
        if (!useCachedPlanarFacingForThrow || !_hasLastPlanarFacingForward)
            return false;

        return TryResolveThrowForward(_lastPlanarFacingForward, out forward);
    }

    private bool TryResolveThrowForward(Transform reference, out Vector3 forward)
    {
        forward = Vector3.zero;
        return reference != null && TryResolveThrowForward(reference.forward, out forward);
    }

    private bool TryResolveThrowForward(Vector3 rawForward, out Vector3 forward)
    {
        forward = flattenThrowDirectionOnGroundPlane
            ? Vector3.ProjectOnPlane(rawForward, Vector3.up)
            : rawForward;

        if (!IsFinite(forward) || forward.sqrMagnitude <= 0.0001f)
        {
            forward = Vector3.zero;
            return false;
        }

        forward.Normalize();
        return true;
    }

    private Vector3 RememberResolvedThrowForward(Vector3 forward, string source, string reason)
    {
        if (!IsFinite(forward) || forward.sqrMagnitude <= 0.0001f)
            forward = Vector3.forward;

        _lastResolvedThrowForward = forward.normalized;
        LogThrowDirection(source, reason, _lastResolvedThrowForward);
        return _lastResolvedThrowForward;
    }

    private void LogThrowDirection(string source, string reason, Vector3 forward)
    {
        if (!debugThrowDirectionLogs)
            return;

        Debug.Log(
            $"[HamsterRagdollGrabber:{name}] throwDirection source={source} reason={reason} forward={FormatVector(forward)} hasCachedFacing={_hasLastPlanarFacingForward}",
            this);
    }

    private void ClearPendingThrowForward()
    {
        _pendingThrowForward = Vector3.zero;
        _hasPendingThrowForward = false;
    }

    private bool ReadGrabDropPressed()
    {
        bool keyPressed = ReadKeyDown(grabDropKey);
        bool mousePressed = allowMouseInput && ReadMouseButtonDown(1);
        return keyPressed || mousePressed;
    }

    private bool ReadThrowPressed()
    {
        bool keyPressed = ReadKeyDown(throwKey);
        bool mousePressed = allowMouseInput && ReadMouseButtonDown(0);
        return keyPressed || mousePressed;
    }

    private bool ReadKeyDown(KeyCode key)
    {
        try
        {
            return Input.GetKeyDown(key);
        }
        catch (System.InvalidOperationException exception)
        {
            HandleLegacyInputUnavailable(exception);
            return false;
        }
    }

    private bool ReadMouseButtonDown(int button)
    {
        try
        {
            return Input.GetMouseButtonDown(button);
        }
        catch (System.InvalidOperationException exception)
        {
            HandleLegacyInputUnavailable(exception);
            return false;
        }
    }

    private void HandleLegacyInputUnavailable(System.InvalidOperationException exception)
    {
        if (_legacyInputUnavailable)
            return;

        _legacyInputUnavailable = true;
        Debug.LogWarning(
            $"[HamsterRagdollGrabber:{name}] Legacy input is unavailable. Test input disabled. {exception.Message}",
            this);
    }

    private void AutoFindReferences()
    {
        if (targetBody == null)
            targetBody = GetComponent<Rigidbody>();

        if (aimReference == null)
        {
            Camera mainCamera = Camera.main;
            aimReference = mainCamera != null ? mainCamera.transform : transform;
        }

        if (holdReference == null)
            holdReference = transform;
    }

    private void CacheHolderColliders()
    {
        Collider[] allColliders = GetComponentsInChildren<Collider>(true);
        List<Collider> filtered = new List<Collider>(allColliders.Length);
        for (int i = 0; i < allColliders.Length; i++)
        {
            Collider candidate = allColliders[i];
            if (candidate == null || IsVisualPreviewCollider(candidate))
                continue;

            filtered.Add(candidate);
        }

        _holderColliders = filtered.ToArray();
    }

    private static bool IsVisualPreviewCollider(Collider candidate)
    {
        return candidate != null && IsInVisualPreviewRoot(candidate.transform);
    }

    private static bool IsInVisualPreviewRoot(Transform candidate)
    {
        Transform current = candidate;
        while (current != null)
        {
            if (current.name.IndexOf("VisualPreviewRoot", System.StringComparison.OrdinalIgnoreCase) >= 0)
                return true;

            current = current.parent;
        }

        return false;
    }

    private void ClampValues()
    {
        grabDistance = Mathf.Max(0f, grabDistance);
        grabRadius = Mathf.Max(0.01f, grabRadius);
        followSpring = Mathf.Max(0f, followSpring);
        followDamping = Mathf.Max(0f, followDamping);
        maxFollowAcceleration = Mathf.Max(0f, maxFollowAcceleration);
        throwForwardImpulse = Mathf.Max(0f, throwForwardImpulse);
        throwUpImpulse = Mathf.Max(0f, throwUpImpulse);
        throwTorqueImpulse = Mathf.Max(0f, throwTorqueImpulse);
        releaseCollisionRestoreDelay = Mathf.Max(0f, releaseCollisionRestoreDelay);
        grabFallbackDelay = Mathf.Max(0f, grabFallbackDelay);
        throwFallbackDelay = Mathf.Max(0f, throwFallbackDelay);
        minFacingCacheSpeed = Mathf.Max(0f, minFacingCacheSpeed);
    }

    private void Log(string message)
    {
        if (!debugLogs)
            return;

        Debug.Log($"[HamsterRagdollGrabber:{name}] {message}", this);
    }

    private void LogTiming(string message)
    {
        if (!debugAnimationEventTimingLogs && !debugLogs)
            return;

        Debug.Log($"[HamsterRagdollGrabber:{name}] {message}", this);
    }

    private void OnDrawGizmosSelected()
    {
        if (!drawDebugGizmos)
            return;

        Vector3 origin = holdReference != null ? holdReference.position : transform.position;
        Vector3 forward = GetPlanarAimForward();
        Vector3 grabCenter = origin + forward * Mathf.Max(0f, grabDistance);
        Vector3 holdPoint = Application.isPlaying && _hasLastHoldPoint ? _lastHoldPoint : GetHoldPoint();

        Gizmos.color = new Color(0.2f, 0.8f, 1.0f, 0.35f);
        Gizmos.DrawWireSphere(grabCenter, Mathf.Max(0.01f, grabRadius));
        Gizmos.DrawLine(origin, grabCenter);

        Gizmos.color = new Color(1.0f, 0.65f, 0.1f, 0.9f);
        Gizmos.DrawSphere(holdPoint, 0.05f);
    }

    private static bool IsFinite(Vector3 value)
    {
        return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
    }

    private static bool IsFinite(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }

    private static string FormatVector(Vector3 value)
    {
        return $"({value.x:F2}, {value.y:F2}, {value.z:F2})";
    }
}
