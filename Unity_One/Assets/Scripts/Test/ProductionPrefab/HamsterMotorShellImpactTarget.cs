using UnityEngine;

public sealed class HamsterMotorShellImpactTarget : MonoBehaviour, IDamageable
{
    private const string TargetRootName = "Hamster_JointFreeMotorShell_MainScenes";

    [Header("References")]
    [SerializeField] private HamsterMotorShellRagdollRecoveryAdapter recoveryAdapter;
    [SerializeField] private Rigidbody bodyRigidbody;
    [SerializeField] private Transform impactCenter;

    [Header("Impact")]
    [SerializeField] private bool enableImpactTarget = true;
    [SerializeField] private float combatKnockdownDuration = 1.0f;
    [SerializeField] private float damageOnlyFallbackImpulse = 3.0f;
    [SerializeField] private float damageOnlyFallbackUpwardImpulse = 1.0f;
    [SerializeField] private float damageOnlyFallbackCooldown = 0.2f;

    [Header("Debug")]
    [SerializeField] private bool debugImpactLogs = false;

    private Transform _targetRoot;
    private float _nextDamageOnlyFallbackAllowedAt;

    public Transform TargetRoot
    {
        get
        {
            CacheReferences();
            return _targetRoot != null ? _targetRoot : transform;
        }
    }

    public Vector3 CenterPosition
    {
        get
        {
            CacheReferences();
            if (bodyRigidbody != null)
                return bodyRigidbody.worldCenterOfMass;

            return impactCenter != null ? impactCenter.position : TargetRoot.position;
        }
    }

    public Vector3 Forward
    {
        get
        {
            Transform root = TargetRoot;
            return root != null ? root.forward : transform.forward;
        }
    }

    public bool CanReceiveImpact
    {
        get
        {
            CacheReferences();
            return enableImpactTarget &&
                   isActiveAndEnabled &&
                   IsTargetRoot(TargetRoot) &&
                   recoveryAdapter != null &&
                   recoveryAdapter.CanReceiveRecoveryState;
        }
    }

    private void Awake()
    {
        CacheReferences();
    }

    private void OnEnable()
    {
        CacheReferences();
    }

    public void TakeDamage(float damage)
    {
        Log("[MSCombat/Recovery]", $"TakeDamage damage={damage:0.##} target={TargetRoot.name}");
        if (Time.time < _nextDamageOnlyFallbackAllowedAt)
            return;

        _nextDamageOnlyFallbackAllowedAt = Time.time + Mathf.Max(0f, damageOnlyFallbackCooldown);
        Vector3 direction = Vector3.ProjectOnPlane(-Forward, Vector3.up);
        if (direction.sqrMagnitude <= 0.0001f)
            direction = Vector3.forward;
        else
            direction.Normalize();

        Vector3 impulse = direction * Mathf.Max(0f, damageOnlyFallbackImpulse) +
                          Vector3.up * Mathf.Max(0f, damageOnlyFallbackUpwardImpulse);
        ApplyCombatHitLikeSugar(impulse, CenterPosition, "IDamageable");
    }

    public bool ApplyGimmickKnockbackLikeSugar(Vector3 impulse, Vector3 hitPoint, string source)
    {
        if (!CanReceiveImpact)
        {
            Log("[MSRecovery/ImpactRequest]", $"skip=not-ready source={source} target={TargetRoot.name}");
            return false;
        }

        bool result = recoveryAdapter.ApplyGimmickImpact(impulse, hitPoint, string.IsNullOrEmpty(source) ? "Gimmick" : source);
        Log("[MSRecovery/ImpactRequest]", $"target={TargetRoot.name} source={source} impulse={FormatVector(impulse)} result={result}");
        return result;
    }

    public bool ApplyCombatHitLikeSugar(Vector3 impulse, Vector3 hitPoint, string source)
    {
        if (!CanReceiveImpact)
        {
            Log("[MSCombat/Recovery]", $"skip=not-ready source={source} target={TargetRoot.name}");
            return false;
        }

        bool result = recoveryAdapter.ApplyImpact(
            impulse,
            hitPoint,
            string.IsNullOrEmpty(source) ? "Combat" : source,
            Mathf.Max(0.01f, combatKnockdownDuration));
        Log("[MSCombat/Recovery]", $"target={TargetRoot.name} source={source} impulse={FormatVector(impulse)} result={result}");
        return result;
    }

    public bool ApplyLiquidSweepLikeSugar(Vector3 flowDirection, float force, float duration, string source)
    {
        if (!CanReceiveImpact)
        {
            Log("[MSLiquid/Recovery]", $"skip=not-ready source={source} target={TargetRoot.name}");
            return false;
        }

        bool result = recoveryAdapter.ApplyLiquidSweep(flowDirection, force, duration, string.IsNullOrEmpty(source) ? "LiquidSweep" : source);
        Log("[MSLiquid/Recovery]", $"target={TargetRoot.name} source={source} direction={FormatVector(flowDirection)} force={force:0.##} duration={duration:0.##} result={result}");
        return result;
    }

    public static bool TryFindOnCollider(Collider hit, out HamsterMotorShellImpactTarget target)
    {
        target = null;
        if (hit == null)
            return false;

        target = hit.GetComponentInParent<HamsterMotorShellImpactTarget>();
        if (IsUsableTarget(target))
            return true;

        Rigidbody attachedRigidbody = hit.attachedRigidbody;
        if (attachedRigidbody != null)
        {
            target = attachedRigidbody.GetComponentInParent<HamsterMotorShellImpactTarget>();
            if (IsUsableTarget(target))
                return true;
        }

        Transform root = hit.transform.root;
        target = root != null ? root.GetComponentInChildren<HamsterMotorShellImpactTarget>(true) : null;
        return IsUsableTarget(target);
    }

    public static bool TryFindOnTransform(Transform transformSource, out HamsterMotorShellImpactTarget target)
    {
        target = null;
        if (transformSource == null)
            return false;

        target = transformSource.GetComponentInParent<HamsterMotorShellImpactTarget>();
        if (IsUsableTarget(target))
            return true;

        Transform root = transformSource.root;
        target = root != null ? root.GetComponentInChildren<HamsterMotorShellImpactTarget>(true) : null;
        return IsUsableTarget(target);
    }

    public static bool IsTargetRoot(Transform root)
    {
        return root != null && (root.name == TargetRootName || root.name == TargetRootName + "(Clone)");
    }

    private static bool IsUsableTarget(HamsterMotorShellImpactTarget target)
    {
        return target != null && target.CanReceiveImpact;
    }

    private void CacheReferences()
    {
        _targetRoot = transform.root != null ? transform.root : transform;

        if (recoveryAdapter == null)
            recoveryAdapter = _targetRoot.GetComponentInChildren<HamsterMotorShellRagdollRecoveryAdapter>(true);

        if (bodyRigidbody == null && recoveryAdapter != null)
            bodyRigidbody = recoveryAdapter.BodyRigidbody;

        if (bodyRigidbody == null)
        {
            Transform motorShellBody = FindChildRecursive(_targetRoot, "MotorShellBody");
            if (motorShellBody != null)
                bodyRigidbody = motorShellBody.GetComponent<Rigidbody>();
        }

        if (impactCenter == null && bodyRigidbody != null)
            impactCenter = bodyRigidbody.transform;
    }

    private static Transform FindChildRecursive(Transform current, string childName)
    {
        if (current == null || string.IsNullOrEmpty(childName))
            return null;

        if (current.name == childName)
            return current;

        for (int i = 0; i < current.childCount; i++)
        {
            Transform match = FindChildRecursive(current.GetChild(i), childName);
            if (match != null)
                return match;
        }

        return null;
    }

    private void Log(string prefix, string message)
    {
        if (!debugImpactLogs)
            return;

        Debug.Log($"{prefix} {message}", this);
    }

    private static string FormatVector(Vector3 value)
    {
        return $"({value.x:F2},{value.y:F2},{value.z:F2})";
    }
}
