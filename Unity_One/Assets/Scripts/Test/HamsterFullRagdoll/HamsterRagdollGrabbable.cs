using UnityEngine;

public sealed class HamsterRagdollGrabbable : MonoBehaviour
{
    private const float MinFiniteDeltaTime = 0.0001f;

    [Header("References")]
    [SerializeField] private Rigidbody body;
    [SerializeField] private Collider[] colliders;
    [SerializeField] private bool autoFindReferences = true;

    [Header("Safety")]
    [SerializeField] private float maxSafeDistanceFromHoldPoint = 3.0f;
    [SerializeField] private bool autoReleaseWhenTooFar = true;

    [Header("Debug")]
    [SerializeField] private bool debugLogs = false;

    private HamsterRagdollGrabber _currentGrabber;
    private bool _isHeld;
    private bool _missingBodyLogged;

    public Rigidbody Body => body;
    public Collider[] Colliders => colliders;
    public bool IsHeld => _isHeld;
    public bool CanBeGrabbed => isActiveAndEnabled && !_isHeld && EnsureBody();

    private void Reset()
    {
        AutoFindReferences();
        ClampValues();
    }

    private void Awake()
    {
        if (autoFindReferences)
            AutoFindReferences();
    }

    private void OnValidate()
    {
        ClampValues();
    }

    public bool TryBeginHold(HamsterRagdollGrabber grabber)
    {
        if (grabber == null)
            return false;

        if (_isHeld)
            return false;

        if (!EnsureBody())
            return false;

        _currentGrabber = grabber;
        _isHeld = true;
        Log($"Hold began. grabber={grabber.name}");
        return true;
    }

    public void ApplyHoldForce(
        Vector3 holdPoint,
        Vector3 holdVelocity,
        float followSpring,
        float followDamping,
        float maxFollowAcceleration,
        float deltaTime)
    {
        if (!_isHeld || !EnsureBody())
            return;

        if (!IsFinite(holdPoint) || !IsFinite(holdVelocity))
        {
            Log("Hold force skipped because hold point or velocity is invalid.");
            return;
        }

        float safeDeltaTime = Mathf.Max(MinFiniteDeltaTime, deltaTime);
        _ = safeDeltaTime;

        Vector3 displacement = holdPoint - body.worldCenterOfMass;
        float distance = displacement.magnitude;
        if (autoReleaseWhenTooFar && maxSafeDistanceFromHoldPoint > 0f && distance > maxSafeDistanceFromHoldPoint)
        {
            Log($"Auto release. distance={distance:F2} max={maxSafeDistanceFromHoldPoint:F2}");
            ForceReleaseWithoutImpulse();
            return;
        }

        float spring = Mathf.Max(0f, followSpring);
        float damping = Mathf.Max(0f, followDamping);
        Vector3 targetVelocity = holdVelocity + displacement * spring;
        Vector3 velocityError = targetVelocity - body.linearVelocity;
        Vector3 acceleration = velocityError * damping;

        float accelerationLimit = Mathf.Max(0f, maxFollowAcceleration);
        if (accelerationLimit > 0f)
            acceleration = Vector3.ClampMagnitude(acceleration, accelerationLimit);

        if (!IsFinite(acceleration) || acceleration.sqrMagnitude <= 0.000001f)
            return;

        body.AddForce(acceleration, ForceMode.Acceleration);
    }

    public void ReleaseHold(Vector3 throwImpulse, Vector3 torqueImpulse)
    {
        if (!_isHeld)
            return;

        _isHeld = false;
        _currentGrabber = null;

        if (!EnsureBody())
            return;

        if (IsFinite(throwImpulse) && throwImpulse.sqrMagnitude > 0.000001f)
            body.AddForce(throwImpulse, ForceMode.Impulse);

        if (IsFinite(torqueImpulse) && torqueImpulse.sqrMagnitude > 0.000001f)
            body.AddTorque(torqueImpulse, ForceMode.Impulse);

        Log($"Released. throwImpulse={FormatVector(throwImpulse)} torqueImpulse={FormatVector(torqueImpulse)}");
    }

    public void ForceReleaseWithoutImpulse()
    {
        if (!_isHeld)
            return;

        _isHeld = false;
        _currentGrabber = null;
        Log("Released without impulse.");
    }

    private bool EnsureBody()
    {
        if (body != null)
            return true;

        if (autoFindReferences)
            AutoFindReferences();

        if (body != null)
        {
            _missingBodyLogged = false;
            return true;
        }

        if (!_missingBodyLogged)
        {
            _missingBodyLogged = true;
            Debug.LogWarning($"[HamsterRagdollGrabbable:{name}] Rigidbody was not found. Grab is disabled.", this);
        }

        return false;
    }

    private void AutoFindReferences()
    {
        if (body == null)
        {
            body = GetComponent<Rigidbody>();
            if (body == null)
                body = GetComponentInChildren<Rigidbody>(true);
            if (body == null)
                body = GetComponentInParent<Rigidbody>();
        }

        if (colliders == null || colliders.Length == 0)
            colliders = GetComponentsInChildren<Collider>(true);
    }

    private void ClampValues()
    {
        maxSafeDistanceFromHoldPoint = Mathf.Max(0f, maxSafeDistanceFromHoldPoint);
    }

    private void Log(string message)
    {
        if (!debugLogs)
            return;

        Debug.Log($"[HamsterRagdollGrabbable:{name}] {message}", this);
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
