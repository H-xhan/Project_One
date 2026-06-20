using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

public sealed class HamsterMotorShellCombatAdapter : MonoBehaviour
{
    private const string TargetRootName = "Hamster_JointFreeMotorShell_MainScenes";
    private const string PlayerCameraName = "PlayerCamera";

    [Header("References")]
    [SerializeField] private Camera ownerCamera;
    [SerializeField] private HamsterMotorShellItemAdapter itemAdapter;
    [SerializeField] private Rigidbody carrierBody;

    [Header("Attack")]
    [SerializeField] private bool enableCombatAdapter = true;
    [SerializeField] private bool enableLeftMouseAttack = true;
    [SerializeField] private float attackCooldown = 0.35f;
    [SerializeField] private float attackWindup = 0.03f;
    [SerializeField] private float attackActiveDuration = 0.12f;
    [SerializeField] private float defaultHitDistance = 1.0f;
    [SerializeField] private float defaultHitRadius = 0.35f;
    [SerializeField] private float defaultDamage = 1f;
    [SerializeField] private float defaultImpulse = 4f;
    [SerializeField] private float weaponImpulseMultiplier = 1.25f;
    [SerializeField] private float upwardImpulse = 0.5f;
    [SerializeField] private LayerMask hitMask = ~0;

    [Header("Debug")]
    [SerializeField] private bool debugCombatLogs = false;

    private readonly HashSet<int> _processedTargets = new HashSet<int>();
    private NetworkObject _ownerNetworkObject;
    private Transform _targetRoot;
    private bool _pendingAttack;
    private float _pendingAttackTime;
    private float _nextAttackTime;

    private struct AttackProfile
    {
        public float Cooldown;
        public float Distance;
        public float Radius;
        public float Damage;
        public float Impulse;
        public string WeaponName;
        public int WeaponId;
    }

    private void Awake()
    {
        CacheReferences();
    }

    private void OnEnable()
    {
        CacheReferences();
    }

    private void Update()
    {
        if (!CanReadOwnerInput())
            return;

        if (ReadAttackPressed())
            TryQueueAttack();

        if (_pendingAttack && Time.time >= _pendingAttackTime)
        {
            _pendingAttack = false;
            ExecuteAttack();
        }
    }

    private void CacheReferences()
    {
        _targetRoot = transform.root != null ? transform.root : transform;

        if (_ownerNetworkObject == null)
            _ownerNetworkObject = GetComponent<NetworkObject>();
        if (_ownerNetworkObject == null)
            _ownerNetworkObject = GetComponentInParent<NetworkObject>();

        if (itemAdapter == null)
            itemAdapter = GetComponent<HamsterMotorShellItemAdapter>();
        if (itemAdapter == null)
            itemAdapter = GetComponentInParent<HamsterMotorShellItemAdapter>();

        if (carrierBody == null)
        {
            Transform motorShellBody = FindChildRecursive(_targetRoot, "MotorShellBody");
            if (motorShellBody != null)
                carrierBody = motorShellBody.GetComponent<Rigidbody>();
        }
    }

    private bool CanReadOwnerInput()
    {
        if (!enableCombatAdapter || !IsTargetRoot())
            return false;

        NetworkManager networkManager = NetworkManager.Singleton;
        if (networkManager == null || !networkManager.IsListening)
            return false;

        return _ownerNetworkObject == null || _ownerNetworkObject.IsOwner;
    }

    private bool CanApplyAttack()
    {
        NetworkManager networkManager = NetworkManager.Singleton;
        return networkManager != null && networkManager.IsServer;
    }

    private bool ReadAttackPressed()
    {
        if (!enableLeftMouseAttack)
            return false;

        Mouse mouse = Mouse.current;
        return mouse != null && mouse.leftButton.wasPressedThisFrame;
    }

    private void TryQueueAttack()
    {
        Log("[MSCombat/Input]", "leftMouse=true");

        if (IsTextInputSelected())
        {
            Log("[MSCombat/Input]", "skip=uiSelected");
            return;
        }

        AttackProfile profile = BuildAttackProfile();
        if (Time.time < _nextAttackTime)
        {
            Log("[MSCombat/Input]", $"skip=cooldown now={Time.time:F2} next={_nextAttackTime:F2}");
            return;
        }

        _nextAttackTime = Time.time + Mathf.Max(0.01f, profile.Cooldown);
        _pendingAttack = true;
        _pendingAttackTime = Time.time + Mathf.Max(0f, attackWindup);
        Log("[MSCombat/Attack]", $"queued weapon={profile.WeaponName} id={profile.WeaponId} cooldown={profile.Cooldown:F2} active={attackActiveDuration:F2}");
    }

    private void ExecuteAttack()
    {
        if (!CanApplyAttack())
        {
            Log("[MSCombat/Attack]", "skip=serverRequired");
            return;
        }

        AttackProfile profile = BuildAttackProfile();
        if (!TryFindHit(profile, out Collider hitCollider, out Rigidbody targetBody, out Transform targetRoot))
        {
            Log("[MSCombat/Miss]", $"weapon={profile.WeaponName} id={profile.WeaponId} distance={profile.Distance:F2} radius={profile.Radius:F2}");
            return;
        }

        Vector3 impulse = BuildImpulse(profile, targetBody);
        ApplyDamage(targetRoot, profile.Damage);
        bool recoveryApplied = ApplyRecoveryImpact(hitCollider, targetRoot, targetBody, impulse);
        if (!recoveryApplied)
            ApplyImpulse(targetBody, impulse);

        Log(
            "[MSCombat/Hit]",
            $"weapon={profile.WeaponName} id={profile.WeaponId} damage={profile.Damage:F1} target={(targetRoot != null ? targetRoot.name : "<null>")} collider={(hitCollider != null ? hitCollider.name : "<null>")} impulse={FormatVector(impulse)} recoveryApplied={recoveryApplied}");
    }

    private AttackProfile BuildAttackProfile()
    {
        AttackProfile profile = new AttackProfile
        {
            Cooldown = Mathf.Max(0.01f, attackCooldown),
            Distance = Mathf.Max(0f, defaultHitDistance),
            Radius = Mathf.Max(0.01f, defaultHitRadius),
            Damage = Mathf.Max(0f, defaultDamage),
            Impulse = Mathf.Max(0f, defaultImpulse),
            WeaponName = "<none>",
            WeaponId = 0
        };

        WeaponItemDataSO weaponData = itemAdapter != null ? itemAdapter.CurrentHeldWeaponData : null;
        if (weaponData == null)
            return profile;

        profile.Cooldown = Mathf.Max(0.01f, weaponData.weapon.cooldown);
        profile.Distance = Mathf.Max(0f, weaponData.weapon.hitDistance);
        profile.Radius = Mathf.Max(0.01f, weaponData.weapon.hitRadius);
        profile.Damage = Mathf.Max(0f, weaponData.weapon.damage);
        profile.Impulse = Mathf.Max(0f, defaultImpulse * weaponImpulseMultiplier);
        profile.WeaponName = weaponData.name;
        profile.WeaponId = weaponData.itemId;
        return profile;
    }

    private bool TryFindHit(AttackProfile profile, out Collider hitCollider, out Rigidbody targetBody, out Transform targetRoot)
    {
        hitCollider = null;
        targetBody = null;
        targetRoot = null;
        _processedTargets.Clear();

        Vector3 origin = GetAttackOrigin(out Vector3 direction, out float extraDistance);
        int mask = hitMask.value != 0 ? hitMask.value : Physics.DefaultRaycastLayers;
        float castDistance = profile.Distance + extraDistance;

        RaycastHit[] hits = Physics.SphereCastAll(origin, profile.Radius, direction, castDistance, mask, QueryTriggerInteraction.Collide);
        if (TrySelectHit(hits, out hitCollider, out targetBody, out targetRoot))
            return true;

        Vector3 center = GetAttackReferencePosition() + direction * profile.Distance;
        Collider[] overlaps = Physics.OverlapSphere(center, profile.Radius, mask, QueryTriggerInteraction.Collide);
        return TrySelectHit(overlaps, out hitCollider, out targetBody, out targetRoot);
    }

    private Vector3 GetAttackOrigin(out Vector3 direction, out float extraDistance)
    {
        Camera camera = ResolveOwnerCamera();
        Transform reference = carrierBody != null ? carrierBody.transform : transform;
        if (camera != null)
        {
            direction = camera.transform.forward;
            if (direction.sqrMagnitude <= 0.0001f)
                direction = reference.forward;
            else
                direction.Normalize();

            extraDistance = Vector3.Distance(camera.transform.position, GetAttackReferencePosition()) + 0.5f;
            return camera.transform.position;
        }

        direction = reference.forward.sqrMagnitude > 0.0001f ? reference.forward.normalized : transform.forward;
        extraDistance = 0f;
        return GetAttackReferencePosition();
    }

    private Vector3 GetAttackReferencePosition()
    {
        if (carrierBody != null)
            return carrierBody.worldCenterOfMass;

        return transform.position + Vector3.up * 0.8f;
    }

    private bool TrySelectHit(RaycastHit[] hits, out Collider hitCollider, out Rigidbody targetBody, out Transform targetRoot)
    {
        hitCollider = null;
        targetBody = null;
        targetRoot = null;

        if (hits == null || hits.Length == 0)
            return false;

        System.Array.Sort(hits, (left, right) => left.distance.CompareTo(right.distance));
        for (int i = 0; i < hits.Length; i++)
        {
            if (TryBuildTarget(hits[i].collider, out hitCollider, out targetBody, out targetRoot))
                return true;
        }

        return false;
    }

    private bool TrySelectHit(Collider[] hits, out Collider hitCollider, out Rigidbody targetBody, out Transform targetRoot)
    {
        hitCollider = null;
        targetBody = null;
        targetRoot = null;

        if (hits == null || hits.Length == 0)
            return false;

        for (int i = 0; i < hits.Length; i++)
        {
            if (TryBuildTarget(hits[i], out hitCollider, out targetBody, out targetRoot))
                return true;
        }

        return false;
    }

    private bool TryBuildTarget(Collider candidate, out Collider hitCollider, out Rigidbody targetBody, out Transform targetRoot)
    {
        hitCollider = null;
        targetBody = null;
        targetRoot = null;

        if (candidate == null || IsSelfCollider(candidate))
            return false;

        Rigidbody body = candidate.attachedRigidbody != null ? candidate.attachedRigidbody : candidate.GetComponentInParent<Rigidbody>();
        Transform root = body != null ? body.transform.root : candidate.transform.root;
        if (root == null || IsSelfRoot(root))
            return false;

        bool hasDamageable = FindDamageable(root) != null;
        if (body == null && !hasDamageable)
            return false;

        int rootId = root.gameObject.GetInstanceID();
        if (!_processedTargets.Add(rootId))
            return false;

        hitCollider = candidate;
        targetBody = body;
        targetRoot = root;
        return true;
    }

    private Vector3 BuildImpulse(AttackProfile profile, Rigidbody targetBody)
    {
        Vector3 direction = Vector3.zero;
        Vector3 origin = GetAttackReferencePosition();
        if (targetBody != null)
            direction = targetBody.worldCenterOfMass - origin;

        direction = Vector3.ProjectOnPlane(direction, Vector3.up);
        if (direction.sqrMagnitude <= 0.0001f)
        {
            Camera camera = ResolveOwnerCamera();
            direction = camera != null ? Vector3.ProjectOnPlane(camera.transform.forward, Vector3.up) : transform.forward;
        }

        if (direction.sqrMagnitude <= 0.0001f)
            direction = Vector3.forward;
        else
            direction.Normalize();

        return direction * profile.Impulse + Vector3.up * Mathf.Max(0f, upwardImpulse);
    }

    private void ApplyDamage(Transform targetRoot, float damage)
    {
        if (HamsterMotorShellImpactTarget.TryFindOnTransform(targetRoot, out _))
            return;

        IDamageable damageable = FindDamageable(targetRoot);
        if (damageable == null)
            return;

        damageable.TakeDamage(damage);
    }

    private void ApplyImpulse(Rigidbody targetBody, Vector3 impulse)
    {
        if (targetBody == null || targetBody.isKinematic || IsSelfRoot(targetBody.transform.root))
            return;

        targetBody.AddForce(impulse, ForceMode.Impulse);
    }

    private bool ApplyRecoveryImpact(Collider hitCollider, Transform targetRoot, Rigidbody targetBody, Vector3 impulse)
    {
        HamsterMotorShellImpactTarget impactTarget = null;
        if (hitCollider != null)
            HamsterMotorShellImpactTarget.TryFindOnCollider(hitCollider, out impactTarget);

        if (impactTarget == null && targetRoot != null)
            HamsterMotorShellImpactTarget.TryFindOnTransform(targetRoot, out impactTarget);

        if (impactTarget != null && !IsSelfRoot(impactTarget.TargetRoot))
        {
            Vector3 helperHitPoint = ResolveHitPoint(hitCollider, targetBody);
            bool helperApplied = impactTarget.ApplyCombatHitLikeSugar(impulse, helperHitPoint, "HamsterCombat");
            Log("[MSCombat/Recovery]", $"target={impactTarget.TargetRoot.name} impulse={FormatVector(impulse)} result={helperApplied}");
            return helperApplied;
        }

        HamsterMotorShellRagdollRecoveryAdapter recovery = null;
        if (hitCollider != null)
            HamsterMotorShellRagdollRecoveryAdapter.TryFindOnCollider(hitCollider, out recovery);

        if (recovery == null && targetRoot != null)
            HamsterMotorShellRagdollRecoveryAdapter.TryFindOnTransform(targetRoot, out recovery);

        if (recovery == null || IsSelfRoot(recovery.transform.root))
            return false;

        Vector3 hitPoint = ResolveHitPoint(hitCollider, targetBody);
        bool applied = recovery.ApplyImpact(impulse, hitPoint, "Combat", 1f);
        Log("[MSCombat/Recovery]", $"target={recovery.transform.root.name} impulse={FormatVector(impulse)} result={applied}");
        return applied;
    }

    private Vector3 ResolveHitPoint(Collider hitCollider, Rigidbody targetBody)
    {
        Vector3 reference = GetAttackReferencePosition();
        if (hitCollider != null)
            return hitCollider.ClosestPoint(reference);

        return targetBody != null ? targetBody.worldCenterOfMass : reference;
    }

    private IDamageable FindDamageable(Transform root)
    {
        if (root == null)
            return null;

        MonoBehaviour[] behaviours = root.GetComponentsInChildren<MonoBehaviour>(true);
        for (int i = 0; i < behaviours.Length; i++)
        {
            if (behaviours[i] is IDamageable damageable)
                return damageable;
        }

        return null;
    }

    private Camera ResolveOwnerCamera()
    {
        if (ownerCamera != null)
            return ownerCamera;

        if (itemAdapter != null)
        {
            Transform socket = itemAdapter.CurrentHeldSocket;
            if (socket != null)
            {
                Camera socketCamera = socket.GetComponentInParent<Camera>();
                if (socketCamera != null)
                {
                    ownerCamera = socketCamera;
                    return ownerCamera;
                }
            }
        }

        Transform playerCameraTransform = FindChildRecursive(_targetRoot, PlayerCameraName);
        if (playerCameraTransform != null && playerCameraTransform.TryGetComponent(out Camera childCamera))
        {
            ownerCamera = childCamera;
            return ownerCamera;
        }

        ownerCamera = Camera.main;
        return ownerCamera;
    }

    private bool IsTextInputSelected()
    {
        EventSystem eventSystem = EventSystem.current;
        GameObject selected = eventSystem != null ? eventSystem.currentSelectedGameObject : null;
        if (selected == null)
            return false;

        Component[] components = selected.GetComponents<Component>();
        for (int i = 0; i < components.Length; i++)
        {
            Component component = components[i];
            if (component == null)
                continue;

            string typeName = component.GetType().Name;
            if (typeName.Contains("InputField") || typeName.Contains("TMP_InputField"))
                return true;
        }

        return false;
    }

    private bool IsSelfCollider(Collider candidate)
    {
        return candidate != null && IsSelfRoot(candidate.transform.root);
    }

    private bool IsSelfRoot(Transform root)
    {
        Transform selfRoot = _targetRoot != null ? _targetRoot : transform.root;
        return root != null && selfRoot != null && root == selfRoot;
    }

    private bool IsTargetRoot()
    {
        Transform root = _targetRoot != null ? _targetRoot : transform.root;
        if (root == null)
            return false;

        return root.name == TargetRootName || root.name == TargetRootName + "(Clone)";
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
        if (!debugCombatLogs)
            return;

        Debug.Log($"{prefix} {name} {message}", this);
    }

    private static string FormatVector(Vector3 value)
    {
        return $"({value.x:F2},{value.y:F2},{value.z:F2})";
    }
}
