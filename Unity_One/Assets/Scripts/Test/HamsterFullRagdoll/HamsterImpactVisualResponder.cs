using System;
using UnityEngine;

public sealed class HamsterImpactVisualResponder : MonoBehaviour
{
    private const string LogPrefix = "[HamsterImpactVisualResponder]";

    [Header("References")]
    [SerializeField] private Rigidbody targetBody;
    [SerializeField] private HamsterVisualFollower visualFollower;

    [Header("Filtering")]
    [SerializeField] private LayerMask ignoredLayers;
    [SerializeField] private string[] ignoredTags = { "RagdollTestGround", "Ground" };
    [SerializeField] private string[] ignoredNames = { "Plane", "Ground" };
    [SerializeField] private bool ignoreGroundLikeCollisions = true;
    [SerializeField] private bool ignoreTriggers = true;

    [Header("Thresholds")]
    [SerializeField] private float minRelativeSpeed = 1.0f;
    [SerializeField] private float mediumRelativeSpeed = 2.75f;
    [SerializeField] private float heavyRelativeSpeed = 5.0f;
    [SerializeField] private float impactCooldown = 0.18f;

    [Header("Reaction")]
    [SerializeField] private float lightIntensity = 0.35f;
    [SerializeField] private float mediumIntensity = 0.65f;
    [SerializeField] private float heavyIntensity = 1.0f;
    [SerializeField] private bool useCollisionRelativeVelocityDirection = true;

    [Header("Debug")]
    [SerializeField] private bool debugLogs = true;
    [SerializeField] private bool drawDebugGizmos = true;

    [Header("Diagnostics")]
    [SerializeField] private bool logAllCollisionEnter = false;
    [SerializeField] private bool logIgnoredCollisions = false;
    [SerializeField] private bool logBelowThresholdCollisions = false;
    [SerializeField] private bool logAcceptedImpacts = true;
    [SerializeField] private bool drawLastCollisionGizmo = true;

    private float _cooldownTimer;
    private Vector3 _lastImpactDirection;
    private float _lastImpactSpeed;
    private float _lastImpactIntensity;
    private Vector3 _lastCollisionPoint;
    private Vector3 _lastCollisionNormal;
    private string _lastCollisionOtherName;
    private string _lastIgnoredReason;

    private void Reset()
    {
        AutoBindReferences();
        AssignDefaultIgnoredLayersIfEmpty();
        ClampValues();
    }

    private void Awake()
    {
        AutoBindReferences();
    }

    private void Update()
    {
        if (_cooldownTimer > 0f)
            _cooldownTimer = Mathf.Max(0f, _cooldownTimer - Time.deltaTime);
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (collision == null)
            return;

        Collider otherCollider = collision.collider;
        Vector3 relativeVelocity = collision.relativeVelocity;
        float relativeSpeed = IsFiniteVector(relativeVelocity) ? relativeVelocity.magnitude : 0f;
        RecordLastCollision(collision, otherCollider, relativeVelocity);

        if (debugLogs && logAllCollisionEnter)
            LogCollisionEnter(otherCollider, relativeSpeed);

        if (_cooldownTimer > 0f)
        {
            LogIgnoredCollision($"cooldown remaining={_cooldownTimer:F2}");
            return;
        }

        string ignoredReason;
        if (ShouldIgnoreCollision(collision, otherCollider, out ignoredReason))
        {
            LogIgnoredCollision(ignoredReason);
            return;
        }

        if (!IsFiniteVector(relativeVelocity))
        {
            LogIgnoredCollision("non-finite relativeVelocity");
            return;
        }

        if (relativeSpeed < minRelativeSpeed)
        {
            _lastIgnoredReason = $"below minRelativeSpeed speed={relativeSpeed:F2} min={minRelativeSpeed:F2}";
            if (debugLogs && logBelowThresholdCollisions)
                Debug.Log($"{LogPrefix} ignored reason=below minRelativeSpeed speed={relativeSpeed:F2} min={minRelativeSpeed:F2}", this);

            return;
        }

        ImpactClass impactClass = ResolveImpactClass(relativeSpeed);
        float intensity = ResolveImpactIntensity(impactClass, relativeSpeed);
        Vector3 worldDirection = ResolveImpactDirection(collision, relativeVelocity);
        if (worldDirection.sqrMagnitude <= 0.0001f)
        {
            LogIgnoredCollision("empty impact direction");
            return;
        }

        if (visualFollower == null)
            AutoBindReferences();

        if (visualFollower != null)
            visualFollower.AddImpactVisualReaction(worldDirection, intensity);

        _lastImpactDirection = worldDirection.normalized;
        _lastImpactSpeed = relativeSpeed;
        _lastImpactIntensity = intensity;
        _lastIgnoredReason = string.Empty;
        _cooldownTimer = Mathf.Max(0f, impactCooldown);

        if (debugLogs && logAcceptedImpacts)
        {
            string otherName = otherCollider != null ? otherCollider.name : "null";
            Debug.Log(
                $"{LogPrefix} impact={impactClass} speed={relativeSpeed:F2} intensity={intensity:F2} other={otherName} dir={FormatVector3(_lastImpactDirection)}",
                this);
        }
    }

    private void AutoBindReferences()
    {
        if (targetBody == null)
            targetBody = GetComponent<Rigidbody>();

        if (visualFollower == null)
            visualFollower = GetComponent<HamsterVisualFollower>();

        if (visualFollower == null)
            visualFollower = GetComponentInParent<HamsterVisualFollower>();

        if (visualFollower == null)
            visualFollower = GetComponentInChildren<HamsterVisualFollower>(true);
    }

    private void AssignDefaultIgnoredLayersIfEmpty()
    {
        if (ignoredLayers.value != 0)
            return;

        ignoredLayers = LayerMask.GetMask("RagdollTestGround ", "RagdollTestGround", "Ground");
    }

    private bool ShouldIgnoreCollision(Collision collision, Collider otherCollider, out string reason)
    {
        reason = string.Empty;
        if (ignoreTriggers)
        {
            if (otherCollider != null && otherCollider.isTrigger)
            {
                reason = "trigger";
                return true;
            }
        }

        if (otherCollider == null)
            return false;

        GameObject otherObject = otherCollider.gameObject;
        if (IsLayerIgnored(otherObject.layer))
        {
            reason = $"ignored layer layer={LayerMask.LayerToName(otherObject.layer)}({otherObject.layer})";
            return true;
        }

        if (!ignoreGroundLikeCollisions)
            return false;

        if (HasIgnoredTag(otherObject))
        {
            reason = $"ignored tag tag={otherObject.tag}";
            return true;
        }

        if (HasIgnoredName(otherCollider.transform))
        {
            reason = $"ignored name name={otherCollider.transform.name}";
            return true;
        }

        Transform root = otherCollider.transform.root;
        if (root != null && HasIgnoredName(root))
        {
            reason = $"ground-like collision root={root.name}";
            return true;
        }

        return false;
    }

    private bool IsLayerIgnored(int layer)
    {
        return (ignoredLayers.value & (1 << layer)) != 0;
    }

    private bool HasIgnoredTag(GameObject otherObject)
    {
        if (otherObject == null || ignoredTags == null)
            return false;

        string otherTag = otherObject.tag;
        for (int i = 0; i < ignoredTags.Length; i++)
        {
            string ignoredTag = ignoredTags[i];
            if (!string.IsNullOrWhiteSpace(ignoredTag) &&
                string.Equals(otherTag, ignoredTag, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private bool HasIgnoredName(Transform otherTransform)
    {
        if (otherTransform == null || ignoredNames == null)
            return false;

        string otherName = otherTransform.name;
        for (int i = 0; i < ignoredNames.Length; i++)
        {
            string ignoredName = ignoredNames[i];
            if (!string.IsNullOrWhiteSpace(ignoredName) &&
                otherName.IndexOf(ignoredName, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }
        }

        return false;
    }

    private ImpactClass ResolveImpactClass(float relativeSpeed)
    {
        if (relativeSpeed >= heavyRelativeSpeed)
            return ImpactClass.Heavy;

        if (relativeSpeed >= mediumRelativeSpeed)
            return ImpactClass.Medium;

        return ImpactClass.Light;
    }

    private float ResolveImpactIntensity(ImpactClass impactClass, float relativeSpeed)
    {
        float classIntensity;
        switch (impactClass)
        {
            case ImpactClass.Heavy:
                classIntensity = heavyIntensity;
                break;
            case ImpactClass.Medium:
                classIntensity = mediumIntensity;
                break;
            default:
                classIntensity = lightIntensity;
                break;
        }

        float speedIntensity = heavyRelativeSpeed > 0f
            ? Mathf.Clamp01(relativeSpeed / heavyRelativeSpeed)
            : classIntensity;
        return Mathf.Clamp01(Mathf.Max(classIntensity, speedIntensity));
    }

    private Vector3 ResolveImpactDirection(Collision collision, Vector3 relativeVelocity)
    {
        if (useCollisionRelativeVelocityDirection && relativeVelocity.sqrMagnitude > 0.0001f)
            return relativeVelocity.normalized;

        if (collision.contactCount > 0)
            return collision.GetContact(0).normal;

        Transform referenceTransform = targetBody != null ? targetBody.transform : transform;
        return -referenceTransform.forward;
    }

    private void RecordLastCollision(Collision collision, Collider otherCollider, Vector3 relativeVelocity)
    {
        _lastCollisionOtherName = otherCollider != null ? otherCollider.name : "null";
        _lastCollisionPoint = targetBody != null ? targetBody.position : transform.position;
        _lastCollisionNormal = relativeVelocity.sqrMagnitude > 0.0001f
            ? -relativeVelocity.normalized
            : Vector3.up;

        if (collision.contactCount <= 0)
            return;

        ContactPoint contact = collision.GetContact(0);
        _lastCollisionPoint = contact.point;
        _lastCollisionNormal = contact.normal.sqrMagnitude > 0.0001f
            ? contact.normal.normalized
            : _lastCollisionNormal;
    }

    private void LogCollisionEnter(Collider otherCollider, float relativeSpeed)
    {
        string otherName = otherCollider != null ? otherCollider.name : "null";
        string layerName = otherCollider != null ? LayerMask.LayerToName(otherCollider.gameObject.layer) : "null";
        int layer = otherCollider != null ? otherCollider.gameObject.layer : -1;
        string tag = otherCollider != null ? otherCollider.gameObject.tag : "null";
        Debug.Log($"{LogPrefix} collision enter other={otherName} layer={layerName}({layer}) tag={tag} relativeSpeed={relativeSpeed:F2}", this);
    }

    private void LogIgnoredCollision(string reason)
    {
        _lastIgnoredReason = reason;
        if (!debugLogs || !logIgnoredCollisions)
            return;

        Debug.Log($"{LogPrefix} ignored reason={reason} other={_lastCollisionOtherName}", this);
    }

    private void OnValidate()
    {
        AutoBindReferences();
        AssignDefaultIgnoredLayersIfEmpty();
        ClampValues();
    }

    private void ClampValues()
    {
        minRelativeSpeed = Mathf.Max(0f, minRelativeSpeed);
        mediumRelativeSpeed = Mathf.Max(minRelativeSpeed, mediumRelativeSpeed);
        heavyRelativeSpeed = Mathf.Max(mediumRelativeSpeed, heavyRelativeSpeed);
        impactCooldown = Mathf.Max(0f, impactCooldown);
        lightIntensity = Mathf.Clamp01(lightIntensity);
        mediumIntensity = Mathf.Clamp01(mediumIntensity);
        heavyIntensity = Mathf.Clamp01(heavyIntensity);
    }

    private void OnDrawGizmosSelected()
    {
        if (!drawDebugGizmos)
            return;

        if (drawLastCollisionGizmo && !string.IsNullOrEmpty(_lastCollisionOtherName))
        {
            Gizmos.color = string.IsNullOrEmpty(_lastIgnoredReason) ? Color.yellow : Color.gray;
            Gizmos.DrawWireSphere(_lastCollisionPoint, 0.08f);
            if (_lastCollisionNormal.sqrMagnitude > 0.0001f)
                Gizmos.DrawLine(_lastCollisionPoint, _lastCollisionPoint + _lastCollisionNormal.normalized * 0.35f);
        }

        if (_lastImpactDirection.sqrMagnitude <= 0.0001f)
            return;

        Vector3 origin = targetBody != null ? targetBody.position : transform.position;
        Gizmos.color = Color.red;
        Gizmos.DrawLine(origin, origin + _lastImpactDirection.normalized * Mathf.Clamp(_lastImpactSpeed, 0.25f, 2f));
        Gizmos.DrawWireSphere(origin + _lastImpactDirection.normalized * 0.2f, 0.04f + 0.08f * _lastImpactIntensity);
    }

    private static bool IsFiniteVector(Vector3 value)
    {
        return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
    }

    private static bool IsFinite(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }

    private static string FormatVector3(Vector3 value)
    {
        return $"({value.x:F2},{value.y:F2},{value.z:F2})";
    }

    private enum ImpactClass
    {
        Light,
        Medium,
        Heavy
    }
}
