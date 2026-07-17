using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public class PostItRoundManager : NetworkBehaviour
{
    [SerializeField] private int initialPostItCountPerPlayer = 3;
    [SerializeField] private int firstPostItId = 0;
    [SerializeField] private int defaultVisualId = 0;
    [SerializeField] private bool debugLogs = false;

    [Header("World Drop")]
    [SerializeField] private LayerMask worldDropGroundMask;
    [SerializeField] private float worldDropGroundProbeHeight = 20f;
    [SerializeField] private float worldDropGroundProbeDistance = 80f;
    [SerializeField] private float worldDropMaxGroundBelowFallback = 6f;
    [SerializeField] private float worldDropMaxGroundAboveFallback = 1.5f;
    [SerializeField] private float worldDropGroundOffset = 0.025f;
    [SerializeField] private float worldDropMarkerOffsetRadius = 0.35f;
    [SerializeField, Range(-1f, 1f)] private float worldDropMinGroundNormalDot = 0.35f;

    private int _nextPostItId;
    private readonly List<PostItWorldDropData> _worldDrops = new List<PostItWorldDropData>();
    private readonly Dictionary<int, PostItRuntimeData> _worldDropPayloads =
        new Dictionary<int, PostItRuntimeData>();
    private readonly HashSet<int> _claimedWorldDropIds = new HashSet<int>();
    private NetworkList<PostItWorldDropData> _networkWorldDrops;
    private bool _hasSubscribedToWorldDrops;
    private bool _isResettingWorldDrops;
    private int _worldDropMutationDepth;
    private bool _hasPendingWorldDropsChangedNotification;

    public event Action WorldDropsChanged;

    public int WorldDropCount => _worldDrops.Count;
    public IReadOnlyList<PostItWorldDropData> WorldDrops => _worldDrops;

    private void Awake()
    {
        _nextPostItId = firstPostItId;
        _networkWorldDrops = new NetworkList<PostItWorldDropData>(
            values: null,
            readPerm: NetworkVariableReadPermission.Everyone,
            writePerm: NetworkVariableWritePermission.Server);
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        if (IsServer && _worldDropPayloads.Count == 0 && _networkWorldDrops.Count > 0)
        {
            _networkWorldDrops.Clear();
        }

        SubscribeToWorldDrops();
        RebuildWorldDropMirror();
        NotifyWorldDropsChanged();
    }

    public override void OnNetworkDespawn()
    {
        BeginWorldDropMutation();
        try
        {
            if (IsServer && _networkWorldDrops != null && _networkWorldDrops.Count > 0)
            {
                try
                {
                    _networkWorldDrops.Clear();
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception, this);
                }
            }

            UnsubscribeFromWorldDrops();
            _worldDrops.Clear();
            _claimedWorldDropIds.Clear();
            if (IsServer)
            {
                _worldDropPayloads.Clear();
            }

            NotifyWorldDropsChanged();
        }
        finally
        {
            EndWorldDropMutation();
        }

        base.OnNetworkDespawn();
    }

    public void ResetPostItIdCounter()
    {
        _nextPostItId = firstPostItId;
    }

    public bool ServerAssignInitialPostIts(IEnumerable<PlayerPostItInventory> inventories)
    {
        if (!CanMutateServerState())
        {
            LogWarning("Blocked initial post-it assignment on non-server instance.");
            return false;
        }

        if (inventories == null)
        {
            LogWarning("Rejected initial post-it assignment because inventories is null.");
            return false;
        }

        List<PlayerPostItInventory> validInventories = new List<PlayerPostItInventory>();
        HashSet<PlayerPostItInventory> uniqueInventories = new HashSet<PlayerPostItInventory>();
        foreach (PlayerPostItInventory inventory in inventories)
        {
            if (inventory != null &&
                IsValidServerInventory(inventory) &&
                uniqueInventories.Add(inventory))
            {
                validInventories.Add(inventory);
            }
        }

        if (validInventories.Count == 0)
        {
            LogWarning("Rejected initial post-it assignment because no valid inventories were found.");
            return false;
        }

        if (!ServerClearWorldDrops())
        {
            LogWarning("Rejected initial post-it assignment because world drops could not be cleared.");
            return false;
        }

        bool allAddsSucceeded = true;
        int postItCount = Mathf.Max(0, initialPostItCountPerPlayer);
        int validInventoryCount = validInventories.Count;
        int totalAssignedPostIts = 0;

        for (int inventoryIndex = 0; inventoryIndex < validInventories.Count; inventoryIndex++)
        {
            PlayerPostItInventory inventory = validInventories[inventoryIndex];
            inventory.ServerClearPostIts();

            ulong ownerClientId = ResolveInventoryOwnerClientId(inventory);
            for (int slotIndex = 0; slotIndex < postItCount; slotIndex++)
            {
                PostItRuntimeData data = new PostItRuntimeData(
                    _nextPostItId++,
                    PostItType.Drawing,
                    ResolveTopicByIndex(slotIndex),
                    defaultVisualId,
                    ownerClientId,
                    ownerClientId,
                    slotIndex);

                if (!inventory.ServerTryAddPostIt(data, out _))
                {
                    allAddsSucceeded = false;
                    LogWarning($"Failed to assign initial post-it. postItId={data.PostItId}, slot={slotIndex}");
                    continue;
                }

                totalAssignedPostIts++;
            }
        }

        if (allAddsSucceeded)
        {
            Log(
                $"Assigned Initial PostIts\nPlayers={validInventoryCount}\nTotalPostIts={totalAssignedPostIts}");
        }

        return allAddsSucceeded;
    }

    public bool ServerAssignInitialPostItsFromScene()
    {
        PlayerPostItInventory[] inventories = FindObjectsByType<PlayerPostItInventory>(FindObjectsSortMode.None);
        return ServerAssignInitialPostIts(inventories);
    }

    public PostItRuntimeData[] BuildRoundSnapshot(IEnumerable<PlayerPostItInventory> inventories)
    {
        return ServerTryBuildRoundSnapshot(inventories, out PostItRuntimeData[] snapshot)
            ? snapshot
            : Array.Empty<PostItRuntimeData>();
    }

    public bool ServerTryBuildRoundSnapshot(
        IEnumerable<PlayerPostItInventory> inventories,
        out PostItRuntimeData[] snapshotResult)
    {
        snapshotResult = Array.Empty<PostItRuntimeData>();
        if (_worldDropMutationDepth > 0 || _isResettingWorldDrops)
        {
            LogWarning("Blocked round snapshot build while world post-it state is mutating.");
            return false;
        }

        if (NetworkManager != null && NetworkManager.IsListening && !IsServer)
        {
            LogAuthorityError("Blocked round snapshot build on non-server instance.");
            return false;
        }

        List<PostItRuntimeData> snapshot = new List<PostItRuntimeData>();
        HashSet<int> includedPostItIds = new HashSet<int>();
        if (inventories != null)
        {
            foreach (PlayerPostItInventory inventory in inventories)
            {
                if (inventory == null)
                    continue;

                PostItRuntimeData[] inventorySnapshot = inventory.GetSnapshot();
                for (int i = 0; i < inventorySnapshot.Length; i++)
                {
                    PostItRuntimeData data = inventorySnapshot[i];
                    if (includedPostItIds.Add(data.PostItId))
                    {
                        snapshot.Add(data);
                    }
                    else
                    {
                        LogAuthorityError($"Duplicate post-it in round snapshot. postItId={data.PostItId}");
                        return false;
                    }
                }
            }
        }

        List<int> worldPostItIds = new List<int>(_worldDropPayloads.Keys);
        worldPostItIds.Sort();
        for (int i = 0; i < worldPostItIds.Count; i++)
        {
            PostItRuntimeData data = _worldDropPayloads[worldPostItIds[i]];
            if (includedPostItIds.Add(data.PostItId))
            {
                snapshot.Add(data);
            }
            else
            {
                LogAuthorityError($"Duplicate world post-it in round snapshot. postItId={data.PostItId}");
                return false;
            }
        }

        snapshot.Sort((left, right) => left.PostItId.CompareTo(right.PostItId));
        snapshotResult = snapshot.ToArray();
        return true;
    }

    public PostItWorldDropData[] GetWorldDropSnapshot()
    {
        return _worldDrops.ToArray();
    }

    public bool TryGetWorldDrop(int postItId, out PostItWorldDropData data)
    {
        for (int i = 0; i < _worldDrops.Count; i++)
        {
            if (_worldDrops[i].PostItId == postItId)
            {
                data = _worldDrops[i];
                return true;
            }
        }

        data = PostItWorldDropData.Invalid;
        return false;
    }

    public bool TryGetClosestWorldDrop(
        Ray ray,
        float maxRayDistance,
        float maxDistanceFromRay,
        out PostItWorldDropData data)
    {
        data = PostItWorldDropData.Invalid;

        float directionSqrMagnitude = ray.direction.sqrMagnitude;
        if (directionSqrMagnitude <= Mathf.Epsilon ||
            maxRayDistance < 0f ||
            maxDistanceFromRay < 0f)
        {
            return false;
        }

        Vector3 direction = ray.direction / Mathf.Sqrt(directionSqrMagnitude);
        float maxDistanceFromRaySqr = maxDistanceFromRay * maxDistanceFromRay;
        float bestDistanceFromRaySqr = float.PositiveInfinity;
        bool found = false;

        for (int i = 0; i < _worldDrops.Count; i++)
        {
            PostItWorldDropData candidate = _worldDrops[i];
            if (!candidate.IsValid || !IsFiniteVector(candidate.Position))
                continue;

            Vector3 toMarker = candidate.Position - ray.origin;
            float distanceAlongRay = Vector3.Dot(toMarker, direction);
            if (distanceAlongRay < 0f || distanceAlongRay > maxRayDistance)
                continue;

            Vector3 closestPoint = ray.origin + direction * distanceAlongRay;
            float distanceFromRaySqr = (candidate.Position - closestPoint).sqrMagnitude;
            if (distanceFromRaySqr > maxDistanceFromRaySqr ||
                distanceFromRaySqr >= bestDistanceFromRaySqr)
            {
                continue;
            }

            bestDistanceFromRaySqr = distanceFromRaySqr;
            data = candidate;
            found = true;
        }

        return found;
    }

    public bool ServerTryDropHighestAcquiredPostIt(
        PlayerPostItInventory sourceInventory,
        Vector3 authoritativePosition,
        bool hasFallbackPosition,
        Vector3 fallbackPosition,
        out PostItWorldDropData droppedData)
    {
        droppedData = PostItWorldDropData.Invalid;

        if (!CanMutateServerState() ||
            !IsNetworkWorldStorageActive() ||
            !IsPlayingState() ||
            _isResettingWorldDrops ||
            sourceInventory == null ||
            !IsValidServerInventory(sourceInventory) ||
            !hasFallbackPosition ||
            !IsFiniteVector(authoritativePosition) ||
            (hasFallbackPosition && !IsFiniteVector(fallbackPosition)))
        {
            return false;
        }

        if (!TrySelectHighestAcquiredPostIt(sourceInventory, out PostItRuntimeData selectedData))
            return false;

        if (CountAuthoritativePostItLocations(selectedData.PostItId) != 1)
        {
            LogWarning($"Rejected world drop because post-it location is not unique. postItId={selectedData.PostItId}");
            return false;
        }

        if (!TryResolveWorldDropPose(
                selectedData.PostItId,
                authoritativePosition,
                hasFallbackPosition,
                fallbackPosition,
                out Vector3 dropPosition,
                out Quaternion dropRotation))
        {
            LogWarning($"Rejected world drop because no recoverable pose was found. postItId={selectedData.PostItId}");
            return false;
        }

        BeginWorldDropMutation();
        try
        {
            if (!TryRemoveInventoryPostItForDrop(sourceInventory, selectedData, out PostItRuntimeData removedData))
                return false;

            if (!removedData.Equals(selectedData))
            {
                if (!RollBackInventoryAdd(sourceInventory, removedData, "drop selection mismatch"))
                {
                    PreserveRemovedPostItAsWorldDrop(removedData, dropPosition, dropRotation);
                }
                return false;
            }

            PostItRuntimeData worldPayload = new PostItRuntimeData(
                removedData.PostItId,
                removedData.Type,
                removedData.TopicId,
                removedData.VisualId,
                removedData.OriginalOwnerClientId,
                ulong.MaxValue,
                -1);
            PostItWorldDropData publicData = new PostItWorldDropData(
                worldPayload.PostItId,
                worldPayload.Type,
                worldPayload.VisualId,
                false,
                dropPosition,
                dropRotation);

            if (!TryAddWorldDrop(worldPayload, publicData))
            {
                if (!RollBackInventoryAdd(sourceInventory, removedData, "world drop publish failure") &&
                    PreserveWorldDropPayload(worldPayload, publicData))
                {
                    droppedData = publicData;
                    return true;
                }

                return false;
            }

            droppedData = publicData;
            Log($"Dropped acquired post-it. postItId={publicData.PostItId}, slot={removedData.SlotIndex}");
            return true;
        }
        finally
        {
            EndWorldDropMutation();
        }
    }

    public bool ServerTryRecoverWorldDrop(
        PlayerPostItInventory requesterInventory,
        int postItId,
        out PostItRuntimeData recoveredData)
    {
        recoveredData = PostItRuntimeData.Invalid;

        if (!CanMutateServerState() ||
            !IsNetworkWorldStorageActive() ||
            !IsPlayingState() ||
            _isResettingWorldDrops ||
            requesterInventory == null ||
            !IsValidServerInventory(requesterInventory) ||
            requesterInventory.IsFull ||
            postItId < 0 ||
            CountAuthoritativePostItLocations(postItId) != 1 ||
            !_worldDropPayloads.ContainsKey(postItId) ||
            !_claimedWorldDropIds.Add(postItId))
        {
            return false;
        }

        BeginWorldDropMutation();
        try
        {
            if (!_worldDropPayloads.TryGetValue(postItId, out PostItRuntimeData worldPayload) ||
                !TryGetWorldDrop(postItId, out _))
            {
                return false;
            }

            if (TryAddInventoryPostItForRecovery(requesterInventory, worldPayload, out recoveredData))
            {
                if (TryRemoveWorldDrop(postItId, out _, out _, out _))
                {
                    Log($"Recovered world post-it. postItId={recoveredData.PostItId}, slot={recoveredData.SlotIndex}");
                    return true;
                }

                _worldDropPayloads.Remove(postItId);
                bool removedPublicMarker = TryRemoveNetworkWorldDropPublic(postItId);
                if (!removedPublicMarker)
                {
                    LogAuthorityError(
                        $"Recovered post-it but could not remove its public marker. " +
                        $"The marker is inactive server-side and will clear with the round. postItId={postItId}");
                }

                Log($"Recovered world post-it after authority cleanup fallback. postItId={recoveredData.PostItId}");
                return true;
            }

            recoveredData = PostItRuntimeData.Invalid;
            return false;
        }
        finally
        {
            _claimedWorldDropIds.Remove(postItId);
            EndWorldDropMutation();
        }
    }

    public bool ServerClearWorldDrops()
    {
        if (!CanMutateServerState() || _isResettingWorldDrops)
            return false;

        _isResettingWorldDrops = true;
        BeginWorldDropMutation();
        try
        {
            if (IsNetworkWorldStorageActive())
            {
                if (_networkWorldDrops.Count > 0)
                {
                    PostItWorldDropData[] publicSnapshot = new PostItWorldDropData[_networkWorldDrops.Count];
                    for (int i = 0; i < _networkWorldDrops.Count; i++)
                        publicSnapshot[i] = _networkWorldDrops[i];

                    try
                    {
                        _networkWorldDrops.Clear();
                    }
                    catch (Exception exception)
                    {
                        Debug.LogException(exception, this);
                    }

                    if (_networkWorldDrops.Count != 0)
                    {
                        for (int i = 0; i < publicSnapshot.Length; i++)
                            TryInsertNetworkWorldDropPublic(i, publicSnapshot[i]);

                        LogAuthorityError("Failed to clear network world post-it storage.");
                        return false;
                    }
                }
            }
            else if (IsSpawnedNetworkSession())
            {
                LogAuthorityError("Cannot clear world post-its because network storage is unavailable.");
                return false;
            }
            else if (_worldDrops.Count > 0)
            {
                _worldDrops.Clear();
                NotifyWorldDropsChanged();
            }

            _worldDropPayloads.Clear();
            _claimedWorldDropIds.Clear();
            return true;
        }
        finally
        {
            _isResettingWorldDrops = false;
            EndWorldDropMutation();
        }
    }

    private bool TrySelectHighestAcquiredPostIt(
        PlayerPostItInventory sourceInventory,
        out PostItRuntimeData selectedData)
    {
        selectedData = PostItRuntimeData.Invalid;
        if (sourceInventory == null)
            return false;

        ulong sourceOwnerClientId = ResolveInventoryOwnerClientId(sourceInventory);
        IReadOnlyList<PostItRuntimeData> items = sourceInventory.Items;
        bool found = false;

        for (int i = 0; i < items.Count; i++)
        {
            PostItRuntimeData candidate = items[i];
            if (!candidate.IsValid ||
                candidate.OriginalOwnerClientId == candidate.HolderClientId ||
                candidate.HolderClientId != sourceOwnerClientId)
            {
                continue;
            }

            if (!found ||
                candidate.SlotIndex > selectedData.SlotIndex ||
                (candidate.SlotIndex == selectedData.SlotIndex &&
                 candidate.PostItId < selectedData.PostItId))
            {
                selectedData = candidate;
                found = true;
            }
        }

        return found;
    }

    private bool TryResolveWorldDropPose(
        int postItId,
        Vector3 authoritativePosition,
        bool hasFallbackPosition,
        Vector3 fallbackPosition,
        out Vector3 position,
        out Quaternion rotation)
    {
        position = Vector3.zero;
        rotation = Quaternion.identity;

        Vector3 markerOffset = GetDeterministicMarkerOffset(postItId);
        float referenceY = hasFallbackPosition
            ? Mathf.Max(authoritativePosition.y, fallbackPosition.y)
            : authoritativePosition.y;
        if (TryProjectWorldDropToGround(
                postItId,
                authoritativePosition + markerOffset,
                referenceY,
                hasFallbackPosition,
                fallbackPosition,
                false,
                out position,
                out rotation))
        {
            return true;
        }

        if (!hasFallbackPosition)
            return false;

        if (TryProjectWorldDropToGround(
                postItId,
                fallbackPosition + markerOffset,
                fallbackPosition.y,
                true,
                fallbackPosition,
                true,
                out position,
                out rotation))
        {
            return true;
        }

        return TryProjectWorldDropToGround(
            postItId,
            fallbackPosition,
            fallbackPosition.y,
            true,
            fallbackPosition,
            true,
            out position,
            out rotation);
    }

    private bool TryProjectWorldDropToGround(
        int postItId,
        Vector3 probeCenter,
        float referenceY,
        bool hasFallbackPosition,
        Vector3 fallbackPosition,
        bool constrainToFallbackHeight,
        out Vector3 position,
        out Quaternion rotation)
    {
        position = Vector3.zero;
        rotation = Quaternion.identity;

        float probeHeight = Mathf.Max(0.1f, worldDropGroundProbeHeight);
        float probeDistance = Mathf.Max(0.2f, worldDropGroundProbeDistance);
        Vector3 rayOrigin = new Vector3(probeCenter.x, referenceY + probeHeight, probeCenter.z);
        int mask = worldDropGroundMask.value == 0
            ? Physics.DefaultRaycastLayers
            : worldDropGroundMask.value;
        RaycastHit[] hits = Physics.RaycastAll(
            rayOrigin,
            Vector3.down,
            probeDistance,
            mask,
            QueryTriggerInteraction.Ignore);
        if (hits == null || hits.Length == 0)
            return false;

        Array.Sort(hits, (left, right) => left.distance.CompareTo(right.distance));
        for (int i = 0; i < hits.Length; i++)
        {
            RaycastHit hit = hits[i];
            if (!IsValidWorldDropGroundHit(
                    hit,
                    hasFallbackPosition,
                    fallbackPosition,
                    constrainToFallbackHeight))
                continue;

            Vector3 normal = hit.normal.sqrMagnitude > 0.0001f
                ? hit.normal.normalized
                : Vector3.up;
            position = hit.point + normal * Mathf.Max(0f, worldDropGroundOffset);
            rotation = BuildMarkerRotation(postItId, normal);
            return IsFiniteVector(position) && IsFiniteQuaternion(rotation);
        }

        return false;
    }

    private bool IsValidWorldDropGroundHit(
        RaycastHit hit,
        bool hasFallbackPosition,
        Vector3 fallbackPosition,
        bool constrainToFallbackHeight)
    {
        if (hit.collider == null ||
            !IsFiniteVector(hit.point) ||
            !IsFiniteVector(hit.normal) ||
            Vector3.Dot(hit.normal.normalized, Vector3.up) <
                Mathf.Clamp(worldDropMinGroundNormalDot, -1f, 1f))
        {
            return false;
        }

        if (hasFallbackPosition &&
            hit.point.y < fallbackPosition.y - Mathf.Max(0f, worldDropMaxGroundBelowFallback))
        {
            return false;
        }

        if (constrainToFallbackHeight &&
            hit.point.y > fallbackPosition.y + Mathf.Max(0f, worldDropMaxGroundAboveFallback))
        {
            return false;
        }

        if (hit.collider.attachedRigidbody != null ||
            hit.collider.GetComponentInParent<NetworkObject>() != null ||
            hit.collider.GetComponentInParent<PlayerPostItInventory>() != null ||
            hit.collider.GetComponentInParent<ItemPickupNetwork>() != null)
        {
            return false;
        }

        return true;
    }

    private Vector3 GetDeterministicMarkerOffset(int postItId)
    {
        uint hash = unchecked((uint)postItId * 2654435761u + 2246822519u);
        float normalizedAngle = (hash & 0xffffu) / 65535f;
        float angle = normalizedAngle * Mathf.PI * 2f;
        float radius = Mathf.Max(0f, worldDropMarkerOffsetRadius);
        return new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
    }

    private static Quaternion BuildMarkerRotation(int postItId, Vector3 groundNormal)
    {
        Vector3 normal = groundNormal.sqrMagnitude > 0.0001f
            ? groundNormal.normalized
            : Vector3.up;
        uint hash = unchecked((uint)postItId * 3266489917u + 668265263u);
        float angle = ((hash >> 8) & 0xffffu) / 65535f * Mathf.PI * 2f;
        Vector3 heading = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
        heading = Vector3.ProjectOnPlane(heading, normal);
        if (heading.sqrMagnitude <= 0.0001f)
            heading = Vector3.ProjectOnPlane(Vector3.forward, normal);
        if (heading.sqrMagnitude <= 0.0001f)
            heading = Vector3.right;

        return Quaternion.LookRotation(-normal, heading.normalized);
    }

    private bool TryAddWorldDrop(
        PostItRuntimeData worldPayload,
        PostItWorldDropData publicData)
    {
        if (!worldPayload.IsValid ||
            !publicData.IsValid ||
            worldPayload.PostItId != publicData.PostItId ||
            _worldDropPayloads.ContainsKey(worldPayload.PostItId) ||
            FindWorldDropIndex(worldPayload.PostItId) >= 0)
        {
            return false;
        }

        _worldDropPayloads.Add(worldPayload.PostItId, worldPayload);
        if (IsNetworkWorldStorageActive())
        {
            int previousCount = _networkWorldDrops.Count;
            try
            {
                _networkWorldDrops.Add(publicData);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, this);
            }

            int publicIndex = FindNetworkWorldDropIndex(worldPayload.PostItId);
            if (_networkWorldDrops.Count == previousCount + 1 &&
                publicIndex >= 0 &&
                _networkWorldDrops[publicIndex].Equals(publicData))
            {
                return true;
            }

            if (publicIndex >= 0 && _networkWorldDrops[publicIndex].Equals(publicData))
                return true;

            if (publicIndex >= 0)
            {
                try
                {
                    _networkWorldDrops.RemoveAt(publicIndex);
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception, this);
                }
            }

            if (FindNetworkWorldDropIndex(worldPayload.PostItId) >= 0)
            {
                LogAuthorityError($"Retained world payload because partial public publish could not be removed. postItId={worldPayload.PostItId}");
                return true;
            }

            _worldDropPayloads.Remove(worldPayload.PostItId);
            return false;
        }

        if (IsSpawnedNetworkSession())
        {
            _worldDropPayloads.Remove(worldPayload.PostItId);
            return false;
        }

        _worldDrops.Add(publicData);
        NotifyWorldDropsChanged();
        return true;
    }

    private bool TryRemoveWorldDrop(
        int postItId,
        out int removedIndex,
        out PostItRuntimeData worldPayload,
        out PostItWorldDropData publicData)
    {
        removedIndex = -1;
        worldPayload = PostItRuntimeData.Invalid;
        publicData = PostItWorldDropData.Invalid;

        if (!_worldDropPayloads.TryGetValue(postItId, out worldPayload))
            return false;

        if (IsNetworkWorldStorageActive())
        {
            removedIndex = FindNetworkWorldDropIndex(postItId);
            if (removedIndex < 0)
                return false;

            publicData = _networkWorldDrops[removedIndex];
            int previousCount = _networkWorldDrops.Count;
            try
            {
                _networkWorldDrops.RemoveAt(removedIndex);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, this);
            }

            if (_networkWorldDrops.Count != previousCount - 1 ||
                FindNetworkWorldDropIndex(postItId) >= 0)
            {
                return false;
            }
        }
        else if (IsSpawnedNetworkSession())
        {
            return false;
        }
        else
        {
            removedIndex = FindWorldDropIndex(postItId);
            if (removedIndex < 0)
                return false;

            publicData = _worldDrops[removedIndex];
            _worldDrops.RemoveAt(removedIndex);
            NotifyWorldDropsChanged();
        }

        if (_worldDropPayloads.Remove(postItId))
            return true;

        if (IsNetworkWorldStorageActive())
            TryInsertNetworkWorldDropPublic(removedIndex, publicData);
        else
        {
            int localIndex = Mathf.Clamp(removedIndex, 0, _worldDrops.Count);
            _worldDrops.Insert(localIndex, publicData);
            NotifyWorldDropsChanged();
        }

        return false;
    }

    private bool RollBackInventoryAdd(
        PlayerPostItInventory inventory,
        PostItRuntimeData data,
        string reason)
    {
        if (inventory == null || !data.IsValid)
            return false;

        try
        {
            if (inventory.ServerTryAddPostIt(data, out _) || inventory.ContainsPostIt(data.PostItId))
                return true;
        }
        catch (Exception exception)
        {
            Debug.LogException(exception, this);
            if (inventory.ContainsPostIt(data.PostItId))
                return true;
        }

        if (inventory.ContainsPostIt(data.PostItId))
            return true;

        LogAuthorityError($"Failed to roll back post-it inventory after {reason}. postItId={data.PostItId}");
        return false;
    }

    private bool TryRemoveInventoryPostItForDrop(
        PlayerPostItInventory inventory,
        PostItRuntimeData selectedData,
        out PostItRuntimeData removedData)
    {
        removedData = PostItRuntimeData.Invalid;
        bool reportedSuccess = false;
        try
        {
            reportedSuccess = inventory.ServerTryRemovePostIt(selectedData.PostItId, out removedData);
        }
        catch (Exception exception)
        {
            Debug.LogException(exception, this);
        }

        if (reportedSuccess && removedData.IsValid)
            return true;

        if (inventory.ContainsPostIt(selectedData.PostItId))
            return false;

        removedData = selectedData;
        LogAuthorityError(
            $"Inventory remove completed without a successful return. " +
            $"Preserving transaction from observed state. postItId={selectedData.PostItId}");
        return true;
    }

    private bool TryAddInventoryPostItForRecovery(
        PlayerPostItInventory inventory,
        PostItRuntimeData worldPayload,
        out PostItRuntimeData recoveredData)
    {
        recoveredData = PostItRuntimeData.Invalid;
        bool reportedSuccess = false;
        try
        {
            reportedSuccess = inventory.ServerTryAddPostIt(worldPayload, out recoveredData);
        }
        catch (Exception exception)
        {
            Debug.LogException(exception, this);
        }

        if (reportedSuccess && recoveredData.IsValid)
            return true;

        if (!inventory.TryGetPostIt(worldPayload.PostItId, out recoveredData))
        {
            recoveredData = PostItRuntimeData.Invalid;
            return false;
        }

        LogAuthorityError(
            $"Inventory add completed without a successful return. " +
            $"Completing recovery from observed state. postItId={worldPayload.PostItId}");
        return true;
    }

    private void PreserveRemovedPostItAsWorldDrop(
        PostItRuntimeData removedData,
        Vector3 position,
        Quaternion rotation)
    {
        if (!removedData.IsValid)
            return;

        PostItRuntimeData worldPayload = new PostItRuntimeData(
            removedData.PostItId,
            removedData.Type,
            removedData.TopicId,
            removedData.VisualId,
            removedData.OriginalOwnerClientId,
            ulong.MaxValue,
            -1);
        PostItWorldDropData publicData = new PostItWorldDropData(
            worldPayload.PostItId,
            worldPayload.Type,
            worldPayload.VisualId,
            false,
            position,
            rotation);
        PreserveWorldDropPayload(worldPayload, publicData);
    }

    private bool PreserveWorldDropPayload(
        PostItRuntimeData worldPayload,
        PostItWorldDropData publicData)
    {
        if (!worldPayload.IsValid ||
            !publicData.IsValid ||
            worldPayload.PostItId != publicData.PostItId)
        {
            return false;
        }

        _worldDropPayloads[worldPayload.PostItId] = worldPayload;
        int existingIndex = FindNetworkWorldDropIndex(worldPayload.PostItId);
        if (existingIndex >= 0)
        {
            bool matches = _networkWorldDrops[existingIndex].Equals(publicData);
            if (!matches)
            {
                LogAuthorityError($"Preserved world payload has mismatched public marker. postItId={worldPayload.PostItId}");
            }

            return matches;
        }

        bool restored = TryInsertNetworkWorldDropPublic(_networkWorldDrops.Count, publicData);
        if (!restored)
        {
            LogAuthorityError(
                $"Preserved server payload without a public marker after rollback failure. " +
                $"postItId={worldPayload.PostItId}");
        }

        return restored;
    }

    private bool TryInsertNetworkWorldDropPublic(int index, PostItWorldDropData publicData)
    {
        if (!IsNetworkWorldStorageActive() || !publicData.IsValid)
            return false;

        int existingIndex = FindNetworkWorldDropIndex(publicData.PostItId);
        if (existingIndex >= 0)
            return _networkWorldDrops[existingIndex].Equals(publicData);

        int safeIndex = Mathf.Clamp(index, 0, _networkWorldDrops.Count);
        int previousCount = _networkWorldDrops.Count;
        try
        {
            _networkWorldDrops.Insert(safeIndex, publicData);
        }
        catch (Exception exception)
        {
            Debug.LogException(exception, this);
        }

        int restoredIndex = FindNetworkWorldDropIndex(publicData.PostItId);
        return _networkWorldDrops.Count == previousCount + 1 &&
               restoredIndex >= 0 &&
               _networkWorldDrops[restoredIndex].Equals(publicData);
    }

    private bool TryRemoveNetworkWorldDropPublic(int postItId)
    {
        if (!IsNetworkWorldStorageActive())
            return false;

        int index = FindNetworkWorldDropIndex(postItId);
        if (index < 0)
            return true;

        try
        {
            _networkWorldDrops.RemoveAt(index);
        }
        catch (Exception exception)
        {
            Debug.LogException(exception, this);
        }

        return FindNetworkWorldDropIndex(postItId) < 0;
    }

    private int CountAuthoritativePostItLocations(int postItId)
    {
        int count = _worldDropPayloads.ContainsKey(postItId) ? 1 : 0;
        PlayerPostItInventory[] inventories =
            FindObjectsByType<PlayerPostItInventory>(FindObjectsSortMode.None);
        for (int i = 0; i < inventories.Length; i++)
        {
            if (inventories[i] != null && inventories[i].ContainsPostIt(postItId))
                count++;
        }

        return count;
    }

    private bool IsNetworkWorldStorageActive()
    {
        return _networkWorldDrops != null &&
               NetworkManager != null &&
               NetworkManager.IsListening &&
               IsSpawned;
    }

    private bool IsSpawnedNetworkSession()
    {
        return NetworkManager != null &&
               NetworkManager.IsListening &&
               IsSpawned;
    }

    private bool IsValidServerInventory(PlayerPostItInventory inventory)
    {
        if (inventory == null)
            return false;

        if (!IsSpawnedNetworkSession())
            return true;

        return inventory.IsSpawned &&
               inventory.IsServer &&
               inventory.NetworkObject != null &&
               inventory.NetworkObject.IsSpawned;
    }

    private static bool IsPlayingState()
    {
        GameStateManager manager = FindFirstObjectByType<GameStateManager>();
        return manager != null && manager.GetState() == GameStateManager.GameState.Playing;
    }

    private int FindWorldDropIndex(int postItId)
    {
        for (int i = 0; i < _worldDrops.Count; i++)
        {
            if (_worldDrops[i].PostItId == postItId)
                return i;
        }

        return -1;
    }

    private int FindNetworkWorldDropIndex(int postItId)
    {
        if (_networkWorldDrops == null)
            return -1;

        for (int i = 0; i < _networkWorldDrops.Count; i++)
        {
            if (_networkWorldDrops[i].PostItId == postItId)
                return i;
        }

        return -1;
    }

    private void SubscribeToWorldDrops()
    {
        if (_hasSubscribedToWorldDrops || _networkWorldDrops == null)
            return;

        _networkWorldDrops.OnListChanged += OnNetworkWorldDropsChanged;
        _hasSubscribedToWorldDrops = true;
    }

    private void UnsubscribeFromWorldDrops()
    {
        if (!_hasSubscribedToWorldDrops || _networkWorldDrops == null)
            return;

        _networkWorldDrops.OnListChanged -= OnNetworkWorldDropsChanged;
        _hasSubscribedToWorldDrops = false;
    }

    private void OnNetworkWorldDropsChanged(NetworkListEvent<PostItWorldDropData> changeEvent)
    {
        RebuildWorldDropMirror();
        NotifyWorldDropsChanged();
        Log($"World drop list changed. type={changeEvent.Type}, count={_worldDrops.Count}");
    }

    private void RebuildWorldDropMirror()
    {
        _worldDrops.Clear();
        if (_networkWorldDrops == null)
            return;

        for (int i = 0; i < _networkWorldDrops.Count; i++)
            _worldDrops.Add(_networkWorldDrops[i]);
    }

    private void NotifyWorldDropsChanged()
    {
        if (_worldDropMutationDepth > 0)
        {
            _hasPendingWorldDropsChangedNotification = true;
            return;
        }

        Action handlers = WorldDropsChanged;
        if (handlers == null)
            return;

        Delegate[] invocationList = handlers.GetInvocationList();
        for (int i = 0; i < invocationList.Length; i++)
        {
            try
            {
                ((Action)invocationList[i]).Invoke();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, this);
            }
        }
    }

    private void BeginWorldDropMutation()
    {
        _worldDropMutationDepth++;
    }

    private void EndWorldDropMutation()
    {
        _worldDropMutationDepth = Mathf.Max(0, _worldDropMutationDepth - 1);
        if (_worldDropMutationDepth != 0 || !_hasPendingWorldDropsChangedNotification)
            return;

        _hasPendingWorldDropsChangedNotification = false;
        NotifyWorldDropsChanged();
    }

    private static bool IsFiniteVector(Vector3 value)
    {
        return !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
               !float.IsNaN(value.y) && !float.IsInfinity(value.y) &&
               !float.IsNaN(value.z) && !float.IsInfinity(value.z);
    }

    private static bool IsFiniteQuaternion(Quaternion value)
    {
        return !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
               !float.IsNaN(value.y) && !float.IsInfinity(value.y) &&
               !float.IsNaN(value.z) && !float.IsInfinity(value.z) &&
               !float.IsNaN(value.w) && !float.IsInfinity(value.w) &&
               Quaternion.Dot(value, value) > 0.0001f;
    }

    private bool CanMutateServerState()
    {
        if (NetworkManager != null && NetworkManager.IsListening)
        {
            return IsServer;
        }

        return true;
    }

    private PostItTopicId ResolveTopicByIndex(int index)
    {
        switch (index % 5)
        {
            case 0:
                return PostItTopicId.Animal;
            case 1:
                return PostItTopicId.Food;
            case 2:
                return PostItTopicId.Object;
            case 3:
                return PostItTopicId.Emotion;
            default:
                return PostItTopicId.Free;
        }
    }

    private ulong ResolveInventoryOwnerClientId(PlayerPostItInventory inventory)
    {
        if (inventory != null && inventory.NetworkObject != null && inventory.NetworkObject.IsSpawned)
        {
            return inventory.NetworkObject.OwnerClientId;
        }

        return ulong.MaxValue;
    }

    private void OnValidate()
    {
        initialPostItCountPerPlayer = Mathf.Max(0, initialPostItCountPerPlayer);
        worldDropGroundProbeHeight = Mathf.Max(0.1f, worldDropGroundProbeHeight);
        worldDropGroundProbeDistance = Mathf.Max(0.2f, worldDropGroundProbeDistance);
        worldDropMaxGroundBelowFallback = Mathf.Max(0f, worldDropMaxGroundBelowFallback);
        worldDropMaxGroundAboveFallback = Mathf.Max(0f, worldDropMaxGroundAboveFallback);
        worldDropGroundOffset = Mathf.Max(0f, worldDropGroundOffset);
        worldDropMarkerOffsetRadius = Mathf.Max(0f, worldDropMarkerOffsetRadius);
        worldDropMinGroundNormalDot = Mathf.Clamp(worldDropMinGroundNormalDot, -1f, 1f);
    }

    private void LogWarning(string message)
    {
        if (debugLogs)
        {
            Debug.LogWarning($"[{nameof(PostItRoundManager)}] {message}", this);
        }
    }

    private void LogAuthorityError(string message)
    {
        Debug.LogError($"[{nameof(PostItRoundManager)}] {message}", this);
    }

    private void Log(string message)
    {
        if (debugLogs)
        {
            Debug.Log($"[{nameof(PostItRoundManager)}]\n{message}", this);
        }
    }
}
