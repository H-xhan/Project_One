using System.Reflection;
using UnityEngine;

public sealed class MotorShellRootBodySync : MonoBehaviour
{
    [SerializeField] private Rigidbody bodyRigidbody;
    [SerializeField] private Transform bodyTransform;
    [SerializeField] private bool alignBodyToRootOnEnable = true;
    [SerializeField] private bool followBodyAfterInitialAlign = true;
    [SerializeField] private bool preserveRootRotation = true;
    [SerializeField] private bool resetBodyLocalPositionAfterRootFollow = true;
    [SerializeField] private float initialAlignDistanceThreshold = 0.5f;
    [SerializeField] private int initialAlignFrameCount = 5;
    [SerializeField] private bool clearVelocityOnInitialAlign = true;
    [SerializeField] private bool debugLogs = false;
    [SerializeField] private float debugLogInterval = 0.5f;

    private const string BodyName = "MotorShellBody";
    private const string InputRouteTargetName = "Hamster_JointFreeMotorShell_MainScenes";
    private const float MinDebugLogInterval = 0.05f;

    private int _initialAlignRemaining;
    private float _nextDebugLogTime;
    private bool _missingReferenceWarningLogged;

    private void OnEnable()
    {
        ResolveReferences();
        _initialAlignRemaining = alignBodyToRootOnEnable ? Mathf.Max(1, initialAlignFrameCount) : 0;
        _nextDebugLogTime = 0f;
        _missingReferenceWarningLogged = false;
    }

    private void Start()
    {
        ResolveReferences();
    }

    private void LateUpdate()
    {
        if (!HasRequiredReferences())
        {
            LogMissingReferencesOnce();
            return;
        }

        bool didInitialAlignThisFrame = TickInitialAlign();

        if (_initialAlignRemaining <= 0 && followBodyAfterInitialAlign)
            FollowBodyPosition();

        LogDebugState(didInitialAlignThisFrame);
    }

    private void ResolveReferences()
    {
        if (bodyRigidbody == null)
        {
            Transform motorShellBody = FindChildByName(BodyName);
            if (motorShellBody != null)
                bodyRigidbody = motorShellBody.GetComponent<Rigidbody>();
        }

        if (bodyTransform == null && bodyRigidbody != null)
            bodyTransform = bodyRigidbody.transform;
    }

    private bool HasRequiredReferences()
    {
        return bodyRigidbody != null && bodyTransform != null;
    }

    private bool TickInitialAlign()
    {
        if (_initialAlignRemaining <= 0)
            return false;

        _initialAlignRemaining--;

        float distance = Vector3.Distance(transform.position, bodyRigidbody.position);
        if (distance <= Mathf.Max(0f, initialAlignDistanceThreshold))
            return false;

        AlignBodyToRoot();
        return true;
    }

    private void AlignBodyToRoot()
    {
        Vector3 rootPosition = transform.position;
        Quaternion bodyRotation = bodyRigidbody.rotation;

        bodyRigidbody.position = rootPosition;
        bodyTransform.position = rootPosition;
        bodyRigidbody.rotation = bodyRotation;
        bodyTransform.localPosition = Vector3.zero;

        if (clearVelocityOnInitialAlign)
            ClearBodyVelocity();
    }

    private void FollowBodyPosition()
    {
        Vector3 bodyPosition = bodyRigidbody.position;
        Quaternion rootRotation = transform.rotation;

        transform.position = bodyPosition;
        if (preserveRootRotation)
            transform.rotation = rootRotation;

        bodyRigidbody.position = bodyPosition;
        bodyTransform.position = bodyPosition;

        if (resetBodyLocalPositionAfterRootFollow)
            bodyTransform.localPosition = Vector3.zero;
    }

    private void ClearBodyVelocity()
    {
        TrySetRigidbodyVector3Property(bodyRigidbody, "linearVelocity", Vector3.zero);
        TrySetRigidbodyVector3Property(bodyRigidbody, "velocity", Vector3.zero);
        TrySetRigidbodyVector3Property(bodyRigidbody, "angularVelocity", Vector3.zero);
    }

    private void LogDebugState(bool didInitialAlignThisFrame)
    {
        if (!debugLogs || !IsInputRouteTarget() || Time.unscaledTime < _nextDebugLogTime)
            return;

        _nextDebugLogTime = Time.unscaledTime + Mathf.Max(MinDebugLogInterval, debugLogInterval);

        float rootBodyDistance = HasRequiredReferences()
            ? Vector3.Distance(transform.position, bodyRigidbody.position)
            : 0f;
        string bodyPosition = bodyRigidbody != null ? FormatVector3(bodyRigidbody.position) : "<null>";
        string bodyLocalPosition = bodyTransform != null ? FormatVector3(bodyTransform.localPosition) : "<null>";
        string rigidbodyState = bodyRigidbody != null
            ? $"isKinematic={bodyRigidbody.isKinematic} useGravity={bodyRigidbody.useGravity} constraints={bodyRigidbody.constraints} sleeping={bodyRigidbody.IsSleeping()} detectCollisions={bodyRigidbody.detectCollisions}"
            : "bodyRigidbody=<null>";
        BoxCollider bodyCollider = bodyRigidbody != null ? bodyRigidbody.GetComponent<BoxCollider>() : null;
        string colliderState = bodyCollider != null
            ? $"colliderExists=True colliderEnabled={bodyCollider.enabled} colliderCenter={FormatVector3(bodyCollider.center)} colliderSize={FormatVector3(bodyCollider.size)}"
            : "colliderExists=False";

        Debug.Log(
            $"[InputRoute/Body:{GetInputRouteObjectName()}] rootPosition={FormatVector3(transform.position)} bodyPosition={bodyPosition} bodyLocalPosition={bodyLocalPosition} bodyDistance={rootBodyDistance:F3} {rigidbodyState} {colliderState} initialAlignRemaining={_initialAlignRemaining} didInitialAlignThisFrame={didInitialAlignThisFrame} followBodyAfterInitialAlign={followBodyAfterInitialAlign} clearVelocityOnInitialAlign={clearVelocityOnInitialAlign}",
            this);
    }

    private void LogMissingReferencesOnce()
    {
        if (_missingReferenceWarningLogged)
            return;

        _missingReferenceWarningLogged = true;
        Debug.LogWarning(
            $"[MotorShellRootBodySync:{name}] Missing body references. bodyRigidbody={(bodyRigidbody != null ? bodyRigidbody.name : "<null>")} bodyTransform={(bodyTransform != null ? bodyTransform.name : "<null>")}",
            this);
    }

    private Transform FindChildByName(string childName)
    {
        Transform[] children = GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < children.Length; i++)
        {
            if (children[i].name == childName)
                return children[i];
        }

        return null;
    }

    private bool IsInputRouteTarget()
    {
        Transform root = transform.root;
        return root != null && root.name.Contains(InputRouteTargetName);
    }

    private string GetInputRouteObjectName()
    {
        Transform root = transform.root;
        return root != null ? root.name : gameObject.name;
    }

    private static bool TrySetRigidbodyVector3Property(Rigidbody rigidbody, string propertyName, Vector3 value)
    {
        if (rigidbody == null)
            return false;

        PropertyInfo property = typeof(Rigidbody).GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public);
        if (property == null || property.GetSetMethod() == null)
            return false;

        property.SetValue(rigidbody, value, null);
        return true;
    }

    private static string FormatVector3(Vector3 value)
    {
        return $"({value.x:F3},{value.y:F3},{value.z:F3})";
    }
}
