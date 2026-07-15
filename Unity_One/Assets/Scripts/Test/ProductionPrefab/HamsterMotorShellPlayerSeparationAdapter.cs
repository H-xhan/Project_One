using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class HamsterMotorShellPlayerSeparationAdapter : MonoBehaviour
{
    private const string BodyBlockerLayerName = "BodyBlocker";
    private const string LogPrefix = "[MSPlayerSeparation]";
    private const float MinimumDirectionDistance = 0.0001f;
    private const float NormalMovementControlThreshold = 0.999f;

    [Header("References")]
    [SerializeField] private BoxCollider bodyBlockerCollider;
    [SerializeField] private Rigidbody bodyRigidbody;
    [SerializeField] private NetworkObject ownerNetworkObject;
    [SerializeField] private PlayerHub playerHub;
    [SerializeField] private PlayerStatusModule statusModule;
    [SerializeField] private HamsterFullRagdollMotor motor;
    [SerializeField] private HamsterMotorShellRagdollRecoveryAdapter recoveryAdapter;
    [SerializeField] private HamsterMotorShellTraversalAdapter traversalAdapter;

    [Header("Separation")]
    [Tooltip("Extra planar spacing added to the two BodyBlocker support radii.")]
    [SerializeField] private float separationPadding = 0.02f;
    [Tooltip("Maximum total XZ correction applied to one player pair per FixedUpdate.")]
    [SerializeField] private float maxPairCorrectionPerFixedStep = 0.08f;
    [Tooltip("Minimum shared Y span required before planar separation is allowed.")]
    [SerializeField] private float minimumVerticalOverlap = 0.05f;

    [Header("Debug")]
    [SerializeField] private bool debugSeparationLogs = false;

    private readonly Dictionary<ulong, HamsterMotorShellPlayerSeparationAdapter> _pendingPairs =
        new Dictionary<ulong, HamsterMotorShellPlayerSeparationAdapter>(4);
    private readonly List<HamsterMotorShellPlayerSeparationAdapter> _processingPairs =
        new List<HamsterMotorShellPlayerSeparationAdapter>(4);

    private int _bodyBlockerLayer = -1;
    private bool _configurationValid;
    private bool _configurationWarningLogged;
    private bool _referenceRetryAvailable;

    private void Awake()
    {
        _referenceRetryAvailable = true;
        ResolveReferences();
        RefreshConfiguration();
    }

    private void OnEnable()
    {
        ClearPairCollections();
        _referenceRetryAvailable = true;
        ResolveReferences();
        RefreshConfiguration();
    }

    private void OnDisable()
    {
        ClearPairCollections();
        _configurationValid = false;
        _referenceRetryAvailable = false;
    }

    private void OnTriggerEnter(Collider other)
    {
        CollectPairCandidate(other);
    }

    private void OnTriggerStay(Collider other)
    {
        CollectPairCandidate(other);
    }

    private void FixedUpdate()
    {
        if (!CanRunServerSeparation())
        {
            ClearPairCollections();
            return;
        }

        _processingPairs.Clear();
        foreach (KeyValuePair<ulong, HamsterMotorShellPlayerSeparationAdapter> pair in _pendingPairs)
            _processingPairs.Add(pair.Value);

        _pendingPairs.Clear();

        for (int index = 0; index < _processingPairs.Count; index++)
            TrySeparatePair(_processingPairs[index]);

        _processingPairs.Clear();
    }

    private void ResolveReferences()
    {
        if (bodyBlockerCollider == null)
            bodyBlockerCollider = GetComponent<BoxCollider>();

        if (bodyRigidbody == null && bodyBlockerCollider != null)
            bodyRigidbody = bodyBlockerCollider.attachedRigidbody;
        if (bodyRigidbody == null)
            bodyRigidbody = GetComponentInParent<Rigidbody>();

        if (ownerNetworkObject == null)
            ownerNetworkObject = GetComponentInParent<NetworkObject>();

        if (playerHub == null && ownerNetworkObject != null)
        {
            playerHub = ownerNetworkObject.GetComponent<PlayerHub>();
            if (playerHub == null)
                playerHub = ownerNetworkObject.GetComponentInChildren<PlayerHub>(true);
        }

        if (statusModule == null && ownerNetworkObject != null)
            statusModule = ownerNetworkObject.GetComponentInChildren<PlayerStatusModule>(true);

        if (motor == null && bodyRigidbody != null)
            motor = bodyRigidbody.GetComponent<HamsterFullRagdollMotor>();
        if (motor == null && ownerNetworkObject != null)
            motor = ownerNetworkObject.GetComponentInChildren<HamsterFullRagdollMotor>(true);

        if (recoveryAdapter == null && ownerNetworkObject != null)
            recoveryAdapter = ownerNetworkObject.GetComponentInChildren<HamsterMotorShellRagdollRecoveryAdapter>(true);

        if (traversalAdapter == null && ownerNetworkObject != null)
            traversalAdapter = ownerNetworkObject.GetComponentInChildren<HamsterMotorShellTraversalAdapter>(true);
    }

    private void RefreshConfiguration()
    {
        string failureReason;
        _configurationValid = ValidateConfiguration(out failureReason);
        if (!_configurationValid)
            LogInvalidConfigurationOnce(failureReason);
    }

    private bool EnsureConfiguration()
    {
        string failureReason;
        if (_configurationValid && ValidateConfiguration(out failureReason))
            return true;

        _configurationValid = false;
        if (_referenceRetryAvailable && HasMissingReference())
        {
            _referenceRetryAvailable = false;
            ResolveReferences();
        }

        _configurationValid = ValidateConfiguration(out failureReason);

        if (!_configurationValid)
            LogInvalidConfigurationOnce(failureReason);

        return _configurationValid;
    }

    private bool HasMissingReference()
    {
        return bodyBlockerCollider == null ||
               bodyRigidbody == null ||
               ownerNetworkObject == null ||
               playerHub == null ||
               statusModule == null ||
               motor == null ||
               recoveryAdapter == null ||
               traversalAdapter == null;
    }

    private bool ValidateConfiguration(out string failureReason)
    {
        _bodyBlockerLayer = LayerMask.NameToLayer(BodyBlockerLayerName);
        if (_bodyBlockerLayer < 0)
        {
            failureReason = "BodyBlockerLayerMissing";
            return false;
        }

        if (bodyBlockerCollider == null)
        {
            failureReason = "BodyBlockerColliderMissing";
            return false;
        }

        if (!bodyBlockerCollider.enabled)
        {
            failureReason = "BodyBlockerColliderDisabled";
            return false;
        }

        if (!bodyBlockerCollider.isTrigger)
        {
            failureReason = "BodyBlockerColliderNotTrigger";
            return false;
        }

        if (bodyBlockerCollider.gameObject.layer != _bodyBlockerLayer)
        {
            failureReason = "BodyBlockerLayerMismatch";
            return false;
        }

        if (bodyRigidbody == null)
        {
            failureReason = "BodyRigidbodyMissing";
            return false;
        }

        if (bodyBlockerCollider.attachedRigidbody != bodyRigidbody)
        {
            failureReason = "AttachedRigidbodyMismatch";
            return false;
        }

        if (ownerNetworkObject == null)
        {
            failureReason = "OwnerNetworkObjectMissing";
            return false;
        }

        if (playerHub == null)
        {
            failureReason = "PlayerHubMissing";
            return false;
        }

        if (statusModule == null)
        {
            failureReason = "PlayerStatusModuleMissing";
            return false;
        }

        if (motor == null)
        {
            failureReason = "MotorMissing";
            return false;
        }

        if (recoveryAdapter == null)
        {
            failureReason = "RecoveryAdapterMissing";
            return false;
        }

        if (traversalAdapter == null)
        {
            failureReason = "TraversalAdapterMissing";
            return false;
        }

        if (!IsFinite(separationPadding) || separationPadding < 0f)
        {
            failureReason = "InvalidSeparationPadding";
            return false;
        }

        if (!IsFinite(maxPairCorrectionPerFixedStep) || maxPairCorrectionPerFixedStep <= 0f)
        {
            failureReason = "InvalidMaxPairCorrection";
            return false;
        }

        if (!IsFinite(minimumVerticalOverlap) || minimumVerticalOverlap < 0f)
        {
            failureReason = "InvalidMinimumVerticalOverlap";
            return false;
        }

        failureReason = string.Empty;
        return true;
    }

    private bool CanRunServerSeparation()
    {
        if (!isActiveAndEnabled)
            return false;

        if (!EnsureConfiguration())
            return false;

        NetworkManager networkManager = NetworkManager.Singleton;
        if (networkManager == null || !networkManager.IsListening || !networkManager.IsServer)
            return false;

        return ownerNetworkObject.IsSpawned &&
               ownerNetworkObject.IsPlayerObject &&
               !bodyRigidbody.isKinematic &&
               bodyRigidbody.detectCollisions;
    }

    private void CollectPairCandidate(Collider other)
    {
        if (!CanRunServerSeparation())
            return;

        if (other == null)
        {
            LogPairPhase("RejectedNonPlayer", null, "ColliderMissing");
            return;
        }

        if (!other.enabled || !other.isTrigger || other.gameObject.layer != _bodyBlockerLayer)
        {
            LogPairPhase("RejectedNonPlayer", null, "ColliderContractMismatch");
            return;
        }

        HamsterMotorShellPlayerSeparationAdapter peer;
        if (!other.TryGetComponent(out peer) || peer == null || peer == this)
        {
            LogPairPhase("RejectedNonPlayer", peer, "AdapterMissingOrSelf");
            return;
        }

        if (!peer.isActiveAndEnabled)
        {
            LogPairPhase("RejectedNonPlayer", peer, "PeerAdapterDisabled");
            return;
        }

        if (!peer.EnsureConfiguration())
        {
            LogPairPhase("RejectedNonPlayer", peer, "PeerInvalidConfiguration");
            return;
        }

        if (other != peer.bodyBlockerCollider ||
            peer.bodyBlockerCollider.attachedRigidbody != peer.bodyRigidbody)
        {
            LogPairPhase("RejectedNonPlayer", peer, "PeerColliderContractMismatch");
            return;
        }

        if (peer.ownerNetworkObject == null ||
            peer.ownerNetworkObject == ownerNetworkObject ||
            !ownerNetworkObject.IsSpawned ||
            !peer.ownerNetworkObject.IsSpawned ||
            !ownerNetworkObject.IsPlayerObject ||
            !peer.ownerNetworkObject.IsPlayerObject ||
            playerHub == null ||
            peer.playerHub == null)
        {
            LogPairPhase("RejectedNonPlayer", peer, "NetworkPlayerContractMismatch");
            return;
        }

        ulong selfId = ownerNetworkObject.NetworkObjectId;
        ulong otherId = peer.ownerNetworkObject.NetworkObjectId;

        if (selfId >= otherId)
        {
            LogPairPhase(
                "RejectedPairOwner",
                peer,
                selfId == otherId ? "EqualNetworkObjectId" : "HigherNetworkObjectId");
            return;
        }

        bool isNewCandidate = !_pendingPairs.ContainsKey(otherId);
        _pendingPairs[otherId] = peer;

        if (isNewCandidate)
            LogPairPhase("TriggerCandidate", peer, "None");
    }

    private void TrySeparatePair(HamsterMotorShellPlayerSeparationAdapter peer)
    {
        if (peer == null || !CanRunServerSeparation() || !peer.CanRunServerSeparation())
        {
            LogPairPhase("RejectedState", peer, "PairUnavailableOrNotServerRunnable");
            return;
        }

        ulong selfId = ownerNetworkObject.NetworkObjectId;
        ulong otherId = peer.ownerNetworkObject.NetworkObjectId;
        if (selfId >= otherId)
        {
            LogPairPhase(
                "RejectedPairOwner",
                peer,
                selfId == otherId ? "EqualNetworkObjectId" : "HigherNetworkObjectId");
            return;
        }

        string stateFailureReason;
        if (!IsNormalSeparationState(this, out stateFailureReason) ||
            !IsNormalSeparationState(peer, out stateFailureReason))
        {
            LogPairPhase("RejectedState", peer, stateFailureReason);
            return;
        }

        Bounds boundsA = bodyBlockerCollider.bounds;
        Bounds boundsB = peer.bodyBlockerCollider.bounds;
        if (!boundsA.Intersects(boundsB))
        {
            LogPairPhase("RejectedVerticalOverlap", peer, "BoundsNoLongerIntersect");
            return;
        }

        float verticalOverlap = Mathf.Min(boundsA.max.y, boundsB.max.y) -
                                Mathf.Max(boundsA.min.y, boundsB.min.y);
        if (!IsFinite(verticalOverlap) || verticalOverlap < minimumVerticalOverlap)
        {
            LogPairPhase(
                "RejectedVerticalOverlap",
                peer,
                IsFinite(verticalOverlap) ? "BelowMinimumVerticalOverlap" : "NonFiniteVerticalOverlap",
                verticalOverlap: verticalOverlap);
            return;
        }

        Vector3 centerA = boundsA.center;
        Vector3 centerB = boundsB.center;
        Vector3 planarDelta = Vector3.ProjectOnPlane(centerB - centerA, Vector3.up);
        if (!IsFinite(centerA) || !IsFinite(centerB) || !IsFinite(planarDelta))
        {
            LogPairPhase(
                "RejectedState",
                peer,
                "NonFiniteCenterOrPlanarDelta",
                verticalOverlap: verticalOverlap);
            return;
        }

        float planarDistance = planarDelta.magnitude;
        if (!IsFinite(planarDistance))
        {
            LogPairPhase(
                "RejectedState",
                peer,
                "NonFinitePlanarDistance",
                planarDistance: planarDistance,
                verticalOverlap: verticalOverlap);
            return;
        }

        Vector3 direction = planarDistance > MinimumDirectionDistance
            ? planarDelta / planarDistance
            : ResolveDeterministicPlanarDirection(selfId, otherId);

        if (!IsFinite(direction))
        {
            LogPairPhase(
                "RejectedState",
                peer,
                "NonFiniteDirection",
                planarDistance: planarDistance,
                verticalOverlap: verticalOverlap);
            return;
        }

        float supportA;
        float supportB;
        if (!TryCalculatePlanarBoxSupport(bodyBlockerCollider, direction, out supportA) ||
            !TryCalculatePlanarBoxSupport(peer.bodyBlockerCollider, direction, out supportB))
        {
            LogPairPhase(
                "RejectedState",
                peer,
                "InvalidPlanarSupport",
                planarDistance: planarDistance,
                verticalOverlap: verticalOverlap);
            return;
        }

        float requiredDistance = supportA + supportB + separationPadding;
        float penetration = requiredDistance - planarDistance;
        if (!IsFinite(requiredDistance) || !IsFinite(penetration))
        {
            LogPairPhase(
                "RejectedState",
                peer,
                "NonFiniteSeparationDistance",
                planarDistance,
                requiredDistance,
                penetration,
                verticalOverlap: verticalOverlap);
            return;
        }

        if (penetration <= 0f)
            return;

        float correction = Mathf.Min(penetration, maxPairCorrectionPerFixedStep);
        Vector3 halfCorrection = direction * (correction * 0.5f);
        if (!IsFinite(correction) || correction <= 0f || !IsFinite(halfCorrection))
        {
            LogPairPhase(
                "RejectedState",
                peer,
                "InvalidCorrection",
                planarDistance,
                requiredDistance,
                penetration,
                correction,
                verticalOverlap);
            return;
        }

        Vector3 oldA = bodyRigidbody.position;
        Vector3 oldB = peer.bodyRigidbody.position;
        Vector3 newA = new Vector3(oldA.x - halfCorrection.x, oldA.y, oldA.z - halfCorrection.z);
        Vector3 newB = new Vector3(oldB.x + halfCorrection.x, oldB.y, oldB.z + halfCorrection.z);

        if (!IsFinite(oldA) || !IsFinite(oldB) || !IsFinite(newA) || !IsFinite(newB))
        {
            LogPairPhase(
                "RejectedState",
                peer,
                "NonFinitePosition",
                planarDistance,
                requiredDistance,
                penetration,
                correction,
                verticalOverlap);
            return;
        }

        bodyRigidbody.position = newA;
        peer.bodyRigidbody.position = newB;

        if (correction < penetration)
        {
            LogPairPhase(
                "CorrectionClamped",
                peer,
                "MaxPairCorrectionPerFixedStep",
                planarDistance,
                requiredDistance,
                penetration,
                correction,
                verticalOverlap);
        }

        LogPairPhase(
            "SeparationApplied",
            peer,
            "None",
            planarDistance,
            requiredDistance,
            penetration,
            correction,
            verticalOverlap);
    }

    private static bool IsNormalSeparationState(
        HamsterMotorShellPlayerSeparationAdapter adapter,
        out string failureReason)
    {
        if (adapter == null || adapter.statusModule == null || !adapter.statusModule.CanMove)
        {
            failureReason = "MovementUnavailable";
            return false;
        }

        if (adapter.recoveryAdapter == null ||
            adapter.recoveryAdapter.CurrentRecoveryState !=
            HamsterMotorShellRagdollRecoveryAdapter.RecoveryState.Normal)
        {
            failureReason = "RecoveryNotNormal";
            return false;
        }

        if (adapter.motor == null ||
            adapter.motor.IsExternalControlLocked ||
            adapter.motor.IsExternalJumpLocked ||
            !IsFinite(adapter.motor.ExternalMovementControlScale) ||
            adapter.motor.ExternalMovementControlScale < NormalMovementControlThreshold)
        {
            failureReason = "MotorControlRestricted";
            return false;
        }

        if (adapter.traversalAdapter == null ||
            adapter.traversalAdapter.IsGliding ||
            adapter.traversalAdapter.IsWallTraversing)
        {
            failureReason = "TraversalActive";
            return false;
        }

        if (adapter.bodyRigidbody == null ||
            adapter.bodyRigidbody.isKinematic ||
            !adapter.bodyRigidbody.detectCollisions)
        {
            failureReason = "BodyPhysicsUnavailable";
            return false;
        }

        failureReason = string.Empty;
        return true;
    }

    private static bool TryCalculatePlanarBoxSupport(
        BoxCollider boxCollider,
        Vector3 direction,
        out float support)
    {
        support = 0f;
        if (boxCollider == null || !IsFinite(direction))
            return false;

        Vector3 size = boxCollider.size;
        Vector3 lossyScale = boxCollider.transform.lossyScale;
        Vector3 planarRight = Vector3.ProjectOnPlane(boxCollider.transform.right, Vector3.up);
        Vector3 planarUp = Vector3.ProjectOnPlane(boxCollider.transform.up, Vector3.up);
        Vector3 planarForward = Vector3.ProjectOnPlane(boxCollider.transform.forward, Vector3.up);

        if (!IsFinite(size) ||
            !IsFinite(lossyScale) ||
            !IsFinite(planarRight) ||
            !IsFinite(planarUp) ||
            !IsFinite(planarForward))
        {
            return false;
        }

        float halfSizeX = Mathf.Abs(size.x * lossyScale.x) * 0.5f;
        float halfSizeY = Mathf.Abs(size.y * lossyScale.y) * 0.5f;
        float halfSizeZ = Mathf.Abs(size.z * lossyScale.z) * 0.5f;
        support = Mathf.Abs(Vector3.Dot(direction, planarRight)) * halfSizeX +
                  Mathf.Abs(Vector3.Dot(direction, planarUp)) * halfSizeY +
                  Mathf.Abs(Vector3.Dot(direction, planarForward)) * halfSizeZ;

        return IsFinite(halfSizeX) &&
               IsFinite(halfSizeY) &&
               IsFinite(halfSizeZ) &&
               IsFinite(support) &&
               support >= 0f;
    }

    private static Vector3 ResolveDeterministicPlanarDirection(ulong lowerId, ulong higherId)
    {
        ulong hash;
        unchecked
        {
            hash = lowerId * 11400714819323198485UL;
            hash ^= higherId + 0x9E3779B97F4A7C15UL + (hash << 6) + (hash >> 2);
        }

        switch (hash & 3UL)
        {
            case 0UL:
                return Vector3.right;
            case 1UL:
                return Vector3.forward;
            case 2UL:
                return Vector3.left;
            default:
                return Vector3.back;
        }
    }

    private void ClearPairCollections()
    {
        _pendingPairs.Clear();
        _processingPairs.Clear();
    }

    private void LogInvalidConfigurationOnce(string failureReason)
    {
        if (_configurationWarningLogged)
            return;

        _configurationWarningLogged = true;
        string selfNetworkObjectId = ownerNetworkObject != null
            ? ownerNetworkObject.NetworkObjectId.ToString()
            : "<null>";
        string selfOwnerClientId = ownerNetworkObject != null
            ? ownerNetworkObject.OwnerClientId.ToString()
            : "<null>";

        Debug.LogWarning(
            $"{LogPrefix} phase=InvalidConfiguration selfNetworkObjectId={selfNetworkObjectId} selfOwnerClientId={selfOwnerClientId} failureReason={failureReason}",
            this);
    }

    private void LogPairPhase(
        string phase,
        HamsterMotorShellPlayerSeparationAdapter peer,
        string failureReason,
        float planarDistance = 0f,
        float requiredDistance = 0f,
        float penetration = 0f,
        float correction = 0f,
        float verticalOverlap = 0f)
    {
        if (!debugSeparationLogs)
            return;

        string selfNetworkObjectId = ownerNetworkObject != null
            ? ownerNetworkObject.NetworkObjectId.ToString()
            : "<null>";
        string selfOwnerClientId = ownerNetworkObject != null
            ? ownerNetworkObject.OwnerClientId.ToString()
            : "<null>";
        string otherNetworkObjectId = peer != null && peer.ownerNetworkObject != null
            ? peer.ownerNetworkObject.NetworkObjectId.ToString()
            : "<null>";
        string otherOwnerClientId = peer != null && peer.ownerNetworkObject != null
            ? peer.ownerNetworkObject.OwnerClientId.ToString()
            : "<null>";

        Debug.Log(
            $"{LogPrefix} phase={phase} selfNetworkObjectId={selfNetworkObjectId} otherNetworkObjectId={otherNetworkObjectId} selfOwnerClientId={selfOwnerClientId} otherOwnerClientId={otherOwnerClientId} planarDistance={planarDistance:F4} requiredDistance={requiredDistance:F4} penetration={penetration:F4} correction={correction:F4} verticalOverlap={verticalOverlap:F4} failureReason={failureReason}",
            this);
    }

    private static bool IsFinite(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }

    private static bool IsFinite(Vector3 value)
    {
        return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
    }
}
