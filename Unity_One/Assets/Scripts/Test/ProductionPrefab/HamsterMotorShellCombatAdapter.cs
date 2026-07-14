using System;
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
    [SerializeField] private HamsterVisualClipStateDriver visualClipStateDriver;
    [SerializeField] private HamsterAnimationEventRelay animationEventRelay;

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

    [Header("Attack Animation")]
    [SerializeField] private string attackStateName = "Attack";
    [SerializeField] private float attackCrossFade = 0.05f;
    [SerializeField] private float attackMinVisualTime = 0.15f;
    [SerializeField] private float attackMaxVisualTime = 0.6f;
    [SerializeField] private bool requireAttackMotion = true;
    [SerializeField] private bool useAttackAnimationEventTiming = true;
    [SerializeField] private bool useHeldItemAttackVisual = true;
    [SerializeField] private string heldItemAttackStateName = "Attack_HeldItem";
    [SerializeField] private float heldItemAttackCrossFade = 0.05f;
    [SerializeField] private float heldItemAttackMinVisualTime = 0.2f;
    [SerializeField] private float heldItemAttackMaxVisualTime = 0.7f;
    [SerializeField] private bool fallbackToUnarmedAttackVisualIfHeldMotionMissing = true;

    [Header("Debug")]
    [SerializeField] private bool debugCombatLogs = false;

    public event Action AttackStarted;

    private readonly HashSet<int> _processedTargets = new HashSet<int>();
    private NetworkObject _ownerNetworkObject;
    private Transform _targetRoot;
    private HamsterFullRagdollMotor _motorStateSource;
    private HamsterMotorShellRagdollRecoveryAdapter _recoveryAdapter;
    private HamsterMotorShellSpinDashAdapter _spinDashAdapter;
    private bool _pendingAttack;
    private float _pendingAttackTime;
    private float _nextAttackTime;
    private bool _waitingForAttackAnimationEvent;
    private int _attackSequence;
    private PendingAttackSnapshot _pendingAttackSnapshot;

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

    private struct AttackVisualSelection
    {
        public string StateName;
        public int StateHash;
        public float CrossFade;
        public float MinTime;
        public float MaxTime;
        public bool HasHeldItem;
        public bool UsedFallbackState;
    }

    private struct PendingAttackSnapshot
    {
        public AttackProfile Profile;
        public AttackVisualSelection Visual;
        public int Sequence;
        public bool RequiresSpawnedServer;
    }

    private enum AttackHitTiming
    {
        AnimationEvent,
        Fallback
    }

    private void Awake()
    {
        CacheReferences();
    }

    private void OnEnable()
    {
        CacheReferences();
    }

    private void OnDisable()
    {
        ClearPendingAttack();
    }

    private void Update()
    {
        if (ShouldReadDirectAttackInput() && ReadAttackPressed())
            TryQueueDirectAttack();

        if (_pendingAttack &&
            _pendingAttackSnapshot.RequiresSpawnedServer &&
            !CanApplyAttack(true))
        {
            ClearPendingAttack();
            return;
        }

        if (_pendingAttack && Time.time >= _pendingAttackTime)
        {
            if (TryConsumePendingAttack(AttackHitTiming.Fallback, out PendingAttackSnapshot snapshot, out string failureReason))
            {
                LogRoute("HitFallback", true, "none", snapshot.Sequence, AttackHitTiming.Fallback);
                ExecuteAttack(snapshot);
            }
            else
            {
                LogRoute("HitSkipped", false, failureReason, 0, AttackHitTiming.Fallback);
            }
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

        if (visualClipStateDriver == null)
            visualClipStateDriver = GetComponentInChildren<HamsterVisualClipStateDriver>(true);
        if (visualClipStateDriver == null)
            visualClipStateDriver = GetComponentInParent<HamsterVisualClipStateDriver>();

        if (animationEventRelay == null)
            animationEventRelay = GetComponentInChildren<HamsterAnimationEventRelay>(true);
        if (animationEventRelay == null)
            animationEventRelay = GetComponentInParent<HamsterAnimationEventRelay>();

        if (_motorStateSource == null && _targetRoot != null)
            _motorStateSource = _targetRoot.GetComponentInChildren<HamsterFullRagdollMotor>(true);

        if (_recoveryAdapter == null && _targetRoot != null)
            _recoveryAdapter = _targetRoot.GetComponentInChildren<HamsterMotorShellRagdollRecoveryAdapter>(true);

        if (_spinDashAdapter == null && _targetRoot != null)
            _spinDashAdapter = _targetRoot.GetComponentInChildren<HamsterMotorShellSpinDashAdapter>(true);
    }

    public bool CanQueueServerAttackFromPlayerHub(out string failureReason)
    {
        return TryBuildAttackSnapshot(true, out _, out failureReason);
    }

    public bool ServerTryQueueAttackFromPlayerHub(out string failureReason)
    {
        if (!TryBuildAttackSnapshot(true, out PendingAttackSnapshot snapshot, out failureReason))
        {
            LogRoute("ServerQueue", false, failureReason, 0, null);
            return false;
        }

        bool queued = TryCommitAttack(snapshot, out failureReason);
        LogRoute("ServerQueue", queued, failureReason, queued ? _attackSequence : 0, null);
        return queued;
    }

    private bool ShouldReadDirectAttackInput()
    {
        if (!enableCombatAdapter || !IsTargetRoot())
            return false;

        if (_targetRoot == null || _ownerNetworkObject == null)
            CacheReferences();

        NetworkManager networkManager = NetworkManager.Singleton;
        if (networkManager == null || !networkManager.IsListening)
            return true;

        if (_ownerNetworkObject == null || !_ownerNetworkObject.IsSpawned)
            return true;

        return false;
    }

    private bool CanApplyAttack(bool requireSpawnedServer)
    {
        NetworkManager networkManager = NetworkManager.Singleton;
        if (networkManager == null || !networkManager.IsListening)
            return !requireSpawnedServer;

        if (!networkManager.IsServer)
            return false;

        return !requireSpawnedServer ||
               (_ownerNetworkObject != null && _ownerNetworkObject.IsSpawned);
    }

    private bool ReadAttackPressed()
    {
        if (!enableLeftMouseAttack)
            return false;

        Mouse mouse = Mouse.current;
        return mouse != null && mouse.leftButton.wasPressedThisFrame;
    }

    private void TryQueueDirectAttack()
    {
        Log("[MSCombat/Input]", "leftMouse=true");

        if (IsTextInputSelected())
        {
            Log("[MSCombat/Input]", "skip=uiSelected");
            return;
        }

        if (!TryBuildAttackSnapshot(false, out PendingAttackSnapshot snapshot, out string failureReason))
        {
            Log("[MSCombat/Input]", $"skip={failureReason}");
            return;
        }

        if (!TryCommitAttack(snapshot, out failureReason))
            Log("[MSCombat/Input]", $"skip={failureReason}");
    }

    private bool TryBuildAttackSnapshot(
        bool requireSpawnedServer,
        out PendingAttackSnapshot snapshot,
        out string failureReason)
    {
        snapshot = default;
        failureReason = "none";

        if (!isActiveAndEnabled || !enableCombatAdapter)
        {
            failureReason = "adapter disabled";
            return false;
        }

        if (!IsTargetRoot())
        {
            failureReason = "target root invalid";
            return false;
        }

        if (requireSpawnedServer && !CanQueueOnSpawnedServer(out failureReason))
            return false;

        if (_pendingAttack)
        {
            failureReason = "attack pending";
            return false;
        }

        if (Time.time < _nextAttackTime)
        {
            failureReason = "cooldown active";
            return false;
        }

        if (_recoveryAdapter != null && _recoveryAdapter.IsControlLockedByRecovery)
        {
            failureReason = "recovering";
            return false;
        }

        if (_motorStateSource != null && _motorStateSource.IsExternalControlLocked)
        {
            failureReason = "external control locked";
            return false;
        }

        if (IsSpinDashDizzyInputBlocked())
        {
            failureReason = "spindash dizzy";
            return false;
        }

        AttackProfile profile = BuildAttackProfile();
        if (!TrySelectAttackVisual(out AttackVisualSelection visual, out failureReason))
            return false;

        snapshot = new PendingAttackSnapshot
        {
            Profile = profile,
            Visual = visual,
            Sequence = 0,
            RequiresSpawnedServer = requireSpawnedServer
        };
        return true;
    }

    private bool CanQueueOnSpawnedServer(out string failureReason)
    {
        failureReason = "none";
        NetworkManager networkManager = NetworkManager.Singleton;
        if (networkManager == null || !networkManager.IsListening)
        {
            failureReason = "network session not listening";
            return false;
        }

        if (_ownerNetworkObject == null)
        {
            failureReason = "network object missing";
            return false;
        }

        if (!_ownerNetworkObject.IsSpawned)
        {
            failureReason = "network object not spawned";
            return false;
        }

        if (!networkManager.IsServer)
        {
            failureReason = "not server";
            return false;
        }

        if (_motorStateSource == null)
        {
            failureReason = "motor state source missing";
            return false;
        }

        if (_recoveryAdapter == null)
        {
            failureReason = "recovery adapter missing";
            return false;
        }

        if (_spinDashAdapter == null)
        {
            failureReason = "spin dash adapter missing";
            return false;
        }

        return true;
    }

    private bool TryCommitAttack(PendingAttackSnapshot snapshot, out string failureReason)
    {
        if (!TryStartAttackVisual(ref snapshot.Visual, out failureReason))
        {
            LogRoute("AnimationRejected", false, failureReason, 0, null);
            return false;
        }

        snapshot.Sequence = ++_attackSequence;
        _pendingAttackSnapshot = snapshot;
        _pendingAttack = true;
        _waitingForAttackAnimationEvent = useAttackAnimationEventTiming;
        float fallbackDelay = _waitingForAttackAnimationEvent
            ? Mathf.Max(Mathf.Max(0f, attackWindup), Mathf.Max(0f, snapshot.Visual.MinTime))
            : Mathf.Max(0f, attackWindup);
        _pendingAttackTime = Time.time + fallbackDelay;
        _nextAttackTime = Time.time + Mathf.Max(0.01f, snapshot.Profile.Cooldown);
        AttackStarted?.Invoke();

        LogRoute("AnimationAccepted", true, "none", snapshot.Sequence, null);
        Log(
            "[MSCombat/Attack]",
            $"queued sequence={snapshot.Sequence} weapon={snapshot.Profile.WeaponName} id={snapshot.Profile.WeaponId} heldItem={snapshot.Visual.HasHeldItem} selectedState={snapshot.Visual.StateName} visual=true visualFallback={snapshot.Visual.UsedFallbackState} cooldown={snapshot.Profile.Cooldown:F2} active={attackActiveDuration:F2} eventTiming={_waitingForAttackAnimationEvent} fallbackDelay={fallbackDelay:F2}");
        failureReason = "none";
        return true;
    }

    public void CompletePendingAttackFromAnimationEvent()
    {
        if (TryConsumePendingAttack(AttackHitTiming.AnimationEvent, out PendingAttackSnapshot snapshot, out string failureReason))
        {
            LogRoute("HitEvent", true, "none", snapshot.Sequence, AttackHitTiming.AnimationEvent);
            ExecuteAttack(snapshot);
            return;
        }

        LogRoute("HitSkipped", false, failureReason, 0, AttackHitTiming.AnimationEvent);
    }

    private bool TryConsumePendingAttack(
        AttackHitTiming timing,
        out PendingAttackSnapshot snapshot,
        out string failureReason)
    {
        snapshot = default;
        failureReason = "none";
        if (!_pendingAttack)
        {
            failureReason = "no pending attack";
            return false;
        }

        if (!CanApplyAttack(_pendingAttackSnapshot.RequiresSpawnedServer))
        {
            ClearPendingAttack();
            failureReason = "server required";
            return false;
        }

        if (timing == AttackHitTiming.AnimationEvent)
        {
            if (!_waitingForAttackAnimationEvent)
            {
                failureReason = "animation event not expected";
                return false;
            }

            AttackVisualSelection visual = _pendingAttackSnapshot.Visual;
            bool matchingExternalState = visualClipStateDriver != null &&
                                         visualClipStateDriver.IsPlayingExternalOneShot(visual.StateName) &&
                                         visualClipStateDriver.TryGetAnimatorStateNormalizedTime(visual.StateHash, out _);
            if (!matchingExternalState)
            {
                failureReason = "animation event state mismatch";
                return false;
            }
        }
        else if (Time.time < _pendingAttackTime)
        {
            failureReason = "fallback not due";
            return false;
        }

        snapshot = _pendingAttackSnapshot;
        ClearPendingAttack();
        return true;
    }

    private void ClearPendingAttack()
    {
        _pendingAttack = false;
        _pendingAttackTime = 0f;
        _waitingForAttackAnimationEvent = false;
        _pendingAttackSnapshot = default;
    }

    private void ExecuteAttack(PendingAttackSnapshot snapshot)
    {
        if (!CanApplyAttack(snapshot.RequiresSpawnedServer))
        {
            Log("[MSCombat/Attack]", $"skip=serverRequired sequence={snapshot.Sequence}");
            return;
        }

        AttackProfile profile = snapshot.Profile;
        if (!TryFindHit(profile, out Collider hitCollider, out Rigidbody targetBody, out Transform targetRoot))
        {
            Log("[MSCombat/Miss]", $"sequence={snapshot.Sequence} weapon={profile.WeaponName} id={profile.WeaponId} distance={profile.Distance:F2} radius={profile.Radius:F2}");
            return;
        }

        Vector3 impulse = BuildImpulse(profile, targetBody);
        ApplyDamage(targetRoot, profile.Damage);
        bool recoveryApplied = ApplyRecoveryImpact(hitCollider, targetRoot, targetBody, impulse);
        if (!recoveryApplied)
            ApplyImpulse(targetBody, impulse);

        Log(
            "[MSCombat/Hit]",
            $"sequence={snapshot.Sequence} weapon={profile.WeaponName} id={profile.WeaponId} damage={profile.Damage:F1} target={(targetRoot != null ? targetRoot.name : "<null>")} collider={(hitCollider != null ? hitCollider.name : "<null>")} impulse={FormatVector(impulse)} recoveryApplied={recoveryApplied}");
    }

    private bool TrySelectAttackVisual(out AttackVisualSelection selection, out string failureReason)
    {
        selection = default;
        failureReason = "none";
        bool hasHeldItem = HasHeldItemForAttack();

        if (visualClipStateDriver == null)
        {
            failureReason = "clip state driver missing";
            return false;
        }

        if (visualClipStateDriver.IsExternalOneShotActive)
        {
            failureReason = "external one-shot active";
            return false;
        }

        if (useHeldItemAttackVisual && hasHeldItem && !string.IsNullOrWhiteSpace(heldItemAttackStateName))
        {
            if (CanPlayAttackVisualState(heldItemAttackStateName, out int heldStateHash, out string heldFailureReason))
            {
                selection = new AttackVisualSelection
                {
                    StateName = heldItemAttackStateName,
                    StateHash = heldStateHash,
                    CrossFade = heldItemAttackCrossFade,
                    MinTime = Mathf.Max(0f, heldItemAttackMinVisualTime),
                    MaxTime = Mathf.Max(0f, heldItemAttackMaxVisualTime),
                    HasHeldItem = true,
                    UsedFallbackState = false
                };
                return true;
            }

            if (!fallbackToUnarmedAttackVisualIfHeldMotionMissing)
            {
                failureReason = $"held attack state unavailable: {heldFailureReason}";
                return false;
            }
        }

        if (!CanPlayAttackVisualState(attackStateName, out int stateHash, out failureReason))
            return false;

        selection = new AttackVisualSelection
        {
            StateName = attackStateName,
            StateHash = stateHash,
            CrossFade = attackCrossFade,
            MinTime = Mathf.Max(0f, attackMinVisualTime),
            MaxTime = Mathf.Max(0f, attackMaxVisualTime),
            HasHeldItem = hasHeldItem,
            UsedFallbackState = useHeldItemAttackVisual && hasHeldItem && !string.IsNullOrWhiteSpace(heldItemAttackStateName)
        };
        failureReason = "none";
        return true;
    }

    private bool CanPlayAttackVisualState(string stateName, out int stateHash, out string failureReason)
    {
        stateHash = 0;
        if (string.IsNullOrWhiteSpace(stateName))
        {
            failureReason = "state name empty";
            return false;
        }

        return visualClipStateDriver.CanPlayOneShotState(
            stateName,
            requireAttackMotion,
            out stateHash,
            out failureReason);
    }

    private bool TryStartAttackVisual(ref AttackVisualSelection selection, out string failureReason)
    {
        bool played = visualClipStateDriver.TryPlayOneShotState(
            selection.StateName,
            selection.CrossFade,
            selection.MinTime,
            selection.MaxTime,
            requireAttackMotion,
            out int stateHash,
            out failureReason);
        if (played)
            selection.StateHash = stateHash;

        Log(
            "[MSCombat/Visual]",
            played
                ? $"play state={selection.StateName} hash={selection.StateHash} heldItem={selection.HasHeldItem} fallback={selection.UsedFallbackState}"
                : $"skip={failureReason} state={selection.StateName} requireMotion={requireAttackMotion} heldItem={selection.HasHeldItem} fallback={selection.UsedFallbackState}");
        return played;
    }

    private bool HasHeldItemForAttack()
    {
        return itemAdapter != null && itemAdapter.HasHeldItem;
    }

    private bool IsSpinDashDizzyInputBlocked()
    {
        return _spinDashAdapter != null && _spinDashAdapter.IsDizzyActive;
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

    private void LogRoute(
        string phase,
        bool accepted,
        string failureReason,
        int sequence,
        AttackHitTiming? hitTiming)
    {
        if (!debugCombatLogs)
            return;

        NetworkManager networkManager = NetworkManager.Singleton;
        bool isServer = networkManager != null && networkManager.IsServer;
        bool isOwner = _ownerNetworkObject != null && _ownerNetworkObject.IsOwner;
        string networkObjectId = _ownerNetworkObject != null && _ownerNetworkObject.IsSpawned
            ? _ownerNetworkObject.NetworkObjectId.ToString()
            : "unspawned";
        string ownerClientId = _ownerNetworkObject != null
            ? _ownerNetworkObject.OwnerClientId.ToString()
            : "none";
        string timing = hitTiming.HasValue ? hitTiming.Value.ToString() : "none";

        Debug.Log(
            $"[MSCombat/Route] phase={phase} objectName={name} NetworkObjectId={networkObjectId} IsServer={isServer} IsOwner={isOwner} OwnerClientId={ownerClientId} accepted={accepted} failureReason={failureReason} pending={_pendingAttack} attackSequence={sequence} hitTiming={timing}",
            this);
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
