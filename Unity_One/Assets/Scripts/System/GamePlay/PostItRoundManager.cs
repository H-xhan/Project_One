using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public class PostItRoundManager : NetworkBehaviour
{
    [SerializeField] private int initialDrawingPostItCountPerPlayer = 2;
    [SerializeField] private int initialEffectPostItCountPerPlayer = 1;
    [SerializeField, HideInInspector] private int initialPostItCountPerPlayer = 3;
    [SerializeField] private int firstPostItId = 0;
    [SerializeField, HideInInspector] private int defaultVisualId = 0;
    [SerializeField] private bool debugLogs = false;

    [Header("Guessing")]
    [SerializeField] private PostItVisualCatalogSO visualCatalog;
    [SerializeField] private int maxGuessablePostItsPerPlayer = 2;
    [SerializeField] private float guessingDurationSeconds = 15f;
    [SerializeField] private bool debugGuessLogs = false;

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
    private readonly Dictionary<ulong, int> _initialAssignmentRevisionByOwner =
        new Dictionary<ulong, int>();
    private int _lastInitialAssignmentRoundRevision = -1;
    private int _lastExplicitInitialAssignmentRoundRevision = -1;
    private bool _initialAssignmentInProgress;
    private readonly List<PostItGuessPlayerScoreData> _guessScores =
        new List<PostItGuessPlayerScoreData>();
    private NetworkList<PostItWorldDropData> _networkWorldDrops;
    private NetworkList<PostItGuessPlayerScoreData> _networkGuessScores;
    private bool _hasSubscribedToWorldDrops;
    private bool _hasSubscribedToGuessScores;
    private bool _isResettingWorldDrops;
    private int _worldDropMutationDepth;
    private bool _hasPendingWorldDropsChangedNotification;

    private readonly List<ServerPostItGuessEntry> _serverGuessEntries =
        new List<ServerPostItGuessEntry>();
    private readonly Dictionary<ulong, PostItGuessPlayerScoreData> _serverGuessScores =
        new Dictionary<ulong, PostItGuessPlayerScoreData>();
    private readonly Dictionary<ulong, ulong> _serverParticipantPlayerObjectIds =
        new Dictionary<ulong, ulong>();
    private int _roundRevision = -1;
    private int _guessRevision = -1;
    private double _guessDeadlineServerTime;
    private bool _guessSubmissionOpen;
    private int _finalizedGuessRevision = -1;

    public event Action WorldDropsChanged;
    public event Action GuessScoresChanged;

    public int WorldDropCount => _worldDrops.Count;
    public IReadOnlyList<PostItWorldDropData> WorldDrops => _worldDrops;
    public int GuessScoreCount => _guessScores.Count;
    public IReadOnlyList<PostItGuessPlayerScoreData> ScoreItems => _guessScores;

    private struct ServerPostItGuessEntry
    {
        public int RoundRevision;
        public int GuessRevision;
        public ulong OwnerClientId;
        public ulong PlayerNetworkObjectId;
        public int PostItId;
        public int SlotIndexAtSnapshot;
        public int VisualId;
        public PostItTopicId CorrectTopicId;
        public PostItTopicId SelectedTopicId;
        public PostItGuessStatus Status;
        public bool IsCorrect;
        public bool BonusApplied;
    }

    private sealed class PreparedOwnerGuessState
    {
        public PlayerPostItInventory Inventory;
        public PostItGuessOwnerData[] PreviousItems;
        public PostItGuessOwnerData[] DesiredItems;
    }

    private sealed class PreparedGuessParticipant
    {
        public PlayerPostItInventory Inventory;
        public ulong OwnerClientId;
        public ulong PlayerNetworkObjectId;
    }

    private sealed class PreparedInitialAssignment
    {
        public PlayerPostItInventory Inventory;
        public ulong OwnerClientId;
        public PostItRuntimeData[] PreviousItems;
        public PostItRuntimeData[] DesiredItems;
    }

    private void Awake()
    {
        _nextPostItId = firstPostItId;
        _networkWorldDrops = new NetworkList<PostItWorldDropData>(
            values: null,
            readPerm: NetworkVariableReadPermission.Everyone,
            writePerm: NetworkVariableWritePermission.Server);

        _networkGuessScores = new NetworkList<PostItGuessPlayerScoreData>(
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

        if (IsServer && _serverGuessScores.Count == 0 && _networkGuessScores.Count > 0)
        {
            _networkGuessScores.Clear();
        }

        SubscribeToWorldDrops();
        SubscribeToGuessScores();
        RebuildWorldDropMirror();
        RebuildGuessScoreMirror();
        NotifyWorldDropsChanged();
        NotifyGuessScoresChanged();
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

            if (IsServer && _networkGuessScores != null && _networkGuessScores.Count > 0)
            {
                try
                {
                    _networkGuessScores.Clear();
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception, this);
                }
            }

            UnsubscribeFromWorldDrops();
            UnsubscribeFromGuessScores();
            _worldDrops.Clear();
            _guessScores.Clear();
            _claimedWorldDropIds.Clear();
            if (IsServer)
            {
                _worldDropPayloads.Clear();
                _initialAssignmentRevisionByOwner.Clear();
                _lastInitialAssignmentRoundRevision = -1;
                _lastExplicitInitialAssignmentRoundRevision = -1;
                _initialAssignmentInProgress = false;
                ClearServerGuessSnapshotState();
            }

            NotifyWorldDropsChanged();
            NotifyGuessScoresChanged();
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
        if (IsSpawnedNetworkSession() && !IsPlayingState())
        {
            LogWarning(
                "The no-revision initial assignment API is restricted to Playing. " +
                "Use the revision overload during Countdown.");
            return false;
        }

        bool reuseExplicitRevision =
            _lastInitialAssignmentRoundRevision >= 0 &&
            _lastExplicitInitialAssignmentRoundRevision ==
            _lastInitialAssignmentRoundRevision;
        if (!reuseExplicitRevision &&
            _lastInitialAssignmentRoundRevision == int.MaxValue)
        {
            LogAuthorityError("Cannot advance the initial assignment round revision.");
            return false;
        }

        int roundRevision = reuseExplicitRevision
            ? _lastInitialAssignmentRoundRevision
            : _lastInitialAssignmentRoundRevision + 1;
        return ServerAssignInitialPostItsCore(inventories, roundRevision, false);
    }

    public bool ServerAssignInitialPostIts(
        IEnumerable<PlayerPostItInventory> inventories,
        int roundRevision)
    {
        return ServerAssignInitialPostItsCore(inventories, roundRevision, true);
    }

    private bool ServerAssignInitialPostItsCore(
        IEnumerable<PlayerPostItInventory> inventories,
        int roundRevision,
        bool explicitRevision)
    {
        if (!CanMutateServerState())
        {
            LogWarning("Blocked initial post-it assignment on non-server instance.");
            return false;
        }

        if (inventories == null || roundRevision < 0)
        {
            LogWarning("Rejected invalid initial post-it assignment request.");
            return false;
        }

        if (_initialAssignmentInProgress)
        {
            LogWarning("Rejected reentrant initial post-it assignment.");
            return false;
        }

        if (roundRevision < _lastInitialAssignmentRoundRevision)
        {
            LogWarning(
                $"Rejected stale initial post-it assignment. roundRevision={roundRevision}");
            return false;
        }

        bool isNewRoundRevision = roundRevision > _lastInitialAssignmentRoundRevision;

        if (visualCatalog == null)
        {
            LogAuthorityError("Cannot assign initial Post-its because the visual catalog is missing.");
            return false;
        }

        if (!TryBuildInitialAssignmentCatalog(
                out List<PostItVisualCatalogSO.Entry> drawingEntries,
                out List<PostItVisualCatalogSO.Entry> effectEntries,
                out string catalogError))
        {
            LogAuthorityError(
                $"Cannot assign initial Post-its because the catalog is invalid. {catalogError}");
            return false;
        }

        int drawingCount = Mathf.Max(0, initialDrawingPostItCountPerPlayer);
        int effectCount = Mathf.Max(0, initialEffectPostItCountPerPlayer);
        long desiredCountLong = (long)drawingCount + effectCount;
        if (desiredCountLong > int.MaxValue)
        {
            LogAuthorityError("Initial Post-it count exceeds the supported range.");
            return false;
        }

        int desiredCount = (int)desiredCountLong;
        if ((drawingCount > 0 && drawingEntries.Count == 0) ||
            (effectCount > 0 && effectEntries.Count == 0))
        {
            LogAuthorityError("The visual catalog cannot satisfy the initial Post-it counts.");
            return false;
        }

        List<PreparedInitialAssignment> preparedAssignments =
            new List<PreparedInitialAssignment>();
        HashSet<PlayerPostItInventory> uniqueInventories = new HashSet<PlayerPostItInventory>();
        HashSet<ulong> uniqueOwnerClientIds = new HashSet<ulong>();
        foreach (PlayerPostItInventory inventory in inventories)
        {
            if (!IsValidServerInventory(inventory))
            {
                continue;
            }

            if (!uniqueInventories.Add(inventory))
            {
                continue;
            }

            ulong ownerClientId = ResolveInventoryOwnerClientId(inventory);
            if (ownerClientId == ulong.MaxValue || !uniqueOwnerClientIds.Add(ownerClientId))
            {
                LogAuthorityError("Initial assignment contains an invalid or duplicate inventory owner.");
                return false;
            }

            if (inventory.Capacity < desiredCount)
            {
                LogAuthorityError(
                    $"Initial assignment exceeds inventory capacity. ownerClientId={ownerClientId}");
                return false;
            }

            if (_initialAssignmentRevisionByOwner.TryGetValue(
                    ownerClientId,
                    out int assignedRevision) &&
                assignedRevision == roundRevision)
            {
                continue;
            }

            preparedAssignments.Add(new PreparedInitialAssignment
            {
                Inventory = inventory,
                OwnerClientId = ownerClientId,
                PreviousItems = inventory.GetSnapshot()
            });
        }

        if (uniqueOwnerClientIds.Count == 0)
        {
            LogWarning("Rejected initial post-it assignment because no valid inventories were found.");
            return false;
        }

        if (preparedAssignments.Count == 0)
        {
            if (explicitRevision)
            {
                _lastExplicitInitialAssignmentRoundRevision = roundRevision;
            }

            return true;
        }

        preparedAssignments.Sort(CompareInitialAssignmentsByOwnerClientId);

        PlayerPostItInventory[] allSceneInventories =
            FindObjectsByType<PlayerPostItInventory>(FindObjectsSortMode.None);
        if (!ServerTryBuildRoundSnapshot(allSceneInventories, out PostItRuntimeData[] currentSnapshot))
        {
            LogAuthorityError("Cannot reserve initial Post-it IDs because current locations are invalid.");
            return false;
        }

        HashSet<int> reservedPostItIds = new HashSet<int>();
        for (int snapshotIndex = 0; snapshotIndex < currentSnapshot.Length; snapshotIndex++)
        {
            reservedPostItIds.Add(currentSnapshot[snapshotIndex].PostItId);
        }

        int nextPostItId = _nextPostItId;
        if (nextPostItId < 0)
        {
            LogAuthorityError("Cannot assign initial Post-its from a negative PostItId counter.");
            return false;
        }

        for (int assignmentIndex = 0;
             assignmentIndex < preparedAssignments.Count;
             assignmentIndex++)
        {
            PreparedInitialAssignment assignment = preparedAssignments[assignmentIndex];
            PostItRuntimeData[] desiredItems = new PostItRuntimeData[desiredCount];
            int slotIndex = 0;

            int drawingStartIndex = drawingEntries.Count > 0
                ? (int)(ComputeStableAssignmentHash(
                    roundRevision,
                    assignment.OwnerClientId,
                    0,
                    0x44524157u) % (uint)drawingEntries.Count)
                : 0;
            for (int drawingIndex = 0; drawingIndex < drawingCount; drawingIndex++)
            {
                if (!TryReserveNextPostItId(
                        reservedPostItIds,
                        ref nextPostItId,
                        out int postItId))
                {
                    LogAuthorityError("Initial PostItId space is exhausted.");
                    return false;
                }

                PostItVisualCatalogSO.Entry entry =
                    drawingEntries[(drawingStartIndex + drawingIndex) % drawingEntries.Count];
                desiredItems[slotIndex] = new PostItRuntimeData(
                    postItId,
                    entry.Type,
                    entry.TopicId,
                    entry.VisualId,
                    assignment.OwnerClientId,
                    assignment.OwnerClientId,
                    slotIndex);
                slotIndex++;
            }

            for (int effectIndex = 0; effectIndex < effectCount; effectIndex++)
            {
                if (!TryReserveNextPostItId(
                        reservedPostItIds,
                        ref nextPostItId,
                        out int postItId))
                {
                    LogAuthorityError("Initial PostItId space is exhausted.");
                    return false;
                }

                uint effectHash = ComputeStableAssignmentHash(
                    roundRevision,
                    assignment.OwnerClientId,
                    effectIndex,
                    0x45464654u);
                PostItVisualCatalogSO.Entry entry =
                    effectEntries[(int)(effectHash % (uint)effectEntries.Count)];
                desiredItems[slotIndex] = new PostItRuntimeData(
                    postItId,
                    entry.Type,
                    entry.TopicId,
                    entry.VisualId,
                    assignment.OwnerClientId,
                    assignment.OwnerClientId,
                    slotIndex);
                slotIndex++;
            }

            assignment.DesiredItems = desiredItems;
        }

        _initialAssignmentInProgress = true;
        try
        {
            int mutatedAssignmentCount = 0;
            for (int assignmentIndex = 0;
                 assignmentIndex < preparedAssignments.Count;
                 assignmentIndex++)
            {
                PreparedInitialAssignment assignment = preparedAssignments[assignmentIndex];
                assignment.Inventory.ServerClearPostIts();
                mutatedAssignmentCount = assignmentIndex + 1;
                if (!InventoryMatches(assignment.Inventory, Array.Empty<PostItRuntimeData>()) ||
                    !TryAddInitialAssignmentItems(assignment))
                {
                    RollBackInitialAssignments(preparedAssignments, mutatedAssignmentCount);
                    return false;
                }
            }

            if (isNewRoundRevision && !ServerClearWorldDrops())
            {
                RollBackInitialAssignments(preparedAssignments, mutatedAssignmentCount);
                LogAuthorityError("Initial assignment failed because world drops could not be cleared.");
                return false;
            }

            _nextPostItId = nextPostItId;
            _lastInitialAssignmentRoundRevision = roundRevision;
            for (int assignmentIndex = 0;
                 assignmentIndex < preparedAssignments.Count;
                 assignmentIndex++)
            {
                _initialAssignmentRevisionByOwner[
                    preparedAssignments[assignmentIndex].OwnerClientId] = roundRevision;
            }

            if (explicitRevision)
            {
                _lastExplicitInitialAssignmentRoundRevision = roundRevision;
            }

            Log(
                $"Assigned Initial PostIts\nRound={roundRevision}\n" +
                $"Players={preparedAssignments.Count}\n" +
                $"TotalPostIts={preparedAssignments.Count * desiredCount}");
            return true;
        }
        finally
        {
            _initialAssignmentInProgress = false;
        }
    }

    public bool ServerAssignInitialPostItsFromScene()
    {
        PlayerPostItInventory[] inventories = FindObjectsByType<PlayerPostItInventory>(FindObjectsSortMode.None);
        return ServerAssignInitialPostIts(inventories);
    }

    public bool ServerAssignInitialPostItsFromScene(int roundRevision)
    {
        PlayerPostItInventory[] inventories =
            FindObjectsByType<PlayerPostItInventory>(FindObjectsSortMode.None);
        return ServerAssignInitialPostIts(inventories, roundRevision);
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

    private bool TryPrepareServerGuessSnapshot(
        IEnumerable<PlayerPostItInventory> inventories,
        int roundRevision,
        int guessRevision,
        double absoluteDeadline,
        out int totalEligibleCount)
    {
        totalEligibleCount = 0;

        if (!CanMutateServerState())
        {
            LogAuthorityError("Blocked guess snapshot preparation on non-server instance.");
            return false;
        }

        if (inventories == null ||
            roundRevision < 0 ||
            guessRevision < 0 ||
            double.IsNaN(absoluteDeadline) ||
            double.IsInfinity(absoluteDeadline) ||
            absoluteDeadline < 0d)
        {
            LogWarning("Rejected invalid guess snapshot preparation request.");
            return false;
        }

        if (visualCatalog == null)
        {
            LogAuthorityError("Cannot prepare guess snapshot because the catalog is missing.");
            return false;
        }

        if (!visualCatalog.ValidateCatalog(out string catalogError))
        {
            LogAuthorityError($"Cannot prepare guess snapshot because the catalog is invalid. {catalogError}");
            return false;
        }

        List<PreparedGuessParticipant> orderedParticipants =
            new List<PreparedGuessParticipant>();
        HashSet<ulong> includedOwnerClientIds = new HashSet<ulong>();
        foreach (PlayerPostItInventory inventory in inventories)
        {
            if (!IsValidServerInventory(inventory))
            {
                continue;
            }

            ulong ownerClientId = ResolveInventoryOwnerClientId(inventory);
            if (ownerClientId == ulong.MaxValue)
            {
                LogAuthorityError("Cannot prepare guess snapshot for an inventory without an owner.");
                return false;
            }

            if (!includedOwnerClientIds.Add(ownerClientId))
            {
                LogAuthorityError($"Duplicate inventory owner in guess snapshot. ownerClientId={ownerClientId}");
                return false;
            }

            ulong playerNetworkObjectId = ResolveInventoryNetworkObjectId(inventory);
            if (playerNetworkObjectId == ulong.MaxValue)
            {
                LogAuthorityError(
                    $"Cannot prepare guess snapshot without a Player NetworkObject. " +
                    $"ownerClientId={ownerClientId}");
                return false;
            }

            orderedParticipants.Add(new PreparedGuessParticipant
            {
                Inventory = inventory,
                OwnerClientId = ownerClientId,
                PlayerNetworkObjectId = playerNetworkObjectId
            });
        }

        if (orderedParticipants.Count == 0)
        {
            LogWarning("Rejected guess snapshot preparation because no valid inventories were found.");
            return false;
        }

        orderedParticipants.Sort(CompareGuessParticipantsByOwnerClientId);
        List<PlayerPostItInventory> orderedInventories =
            new List<PlayerPostItInventory>(orderedParticipants.Count);
        for (int participantIndex = 0;
             participantIndex < orderedParticipants.Count;
             participantIndex++)
        {
            orderedInventories.Add(orderedParticipants[participantIndex].Inventory);
        }

        if (!ServerTryBuildRoundSnapshot(orderedInventories, out _))
        {
            LogAuthorityError("Cannot prepare guess snapshot because round Post-it uniqueness failed.");
            return false;
        }

        List<PreparedOwnerGuessState> preparedOwnerStates =
            new List<PreparedOwnerGuessState>(orderedInventories.Count);
        List<ServerPostItGuessEntry> preparedEntries =
            new List<ServerPostItGuessEntry>();
        Dictionary<ulong, PostItGuessPlayerScoreData> preparedScores =
            new Dictionary<ulong, PostItGuessPlayerScoreData>();
        Dictionary<ulong, ulong> preparedParticipantObjectIds =
            new Dictionary<ulong, ulong>();
        HashSet<int> preparedPostItIds = new HashSet<int>();

        for (int inventoryIndex = 0;
             inventoryIndex < orderedParticipants.Count;
             inventoryIndex++)
        {
            PreparedGuessParticipant participant = orderedParticipants[inventoryIndex];
            PlayerPostItInventory inventory = participant.Inventory;
            ulong ownerClientId = participant.OwnerClientId;
            ulong playerNetworkObjectId = participant.PlayerNetworkObjectId;
            PostItRuntimeData[] inventoryItems = inventory.GetSnapshot();
            List<PostItRuntimeData> eligibleItems = new List<PostItRuntimeData>();

            for (int itemIndex = 0; itemIndex < inventoryItems.Length; itemIndex++)
            {
                PostItRuntimeData item = inventoryItems[itemIndex];
                if (!TryEvaluateGuessEligibility(item, ownerClientId, out bool isEligible))
                {
                    return false;
                }

                if (!isEligible)
                {
                    continue;
                }

                if (!preparedPostItIds.Add(item.PostItId))
                {
                    LogAuthorityError($"Duplicate eligible Post-it. postItId={item.PostItId}");
                    return false;
                }

                eligibleItems.Add(item);
            }

            eligibleItems.Sort(CompareGuessEligiblePostIts);
            int eligibleCount = eligibleItems.Count;
            if (maxGuessablePostItsPerPlayer > 0)
            {
                eligibleCount = Mathf.Min(eligibleCount, maxGuessablePostItsPerPlayer);
            }

            PostItGuessOwnerData[] desiredOwnerItems =
                new PostItGuessOwnerData[eligibleCount];
            for (int eligibleIndex = 0; eligibleIndex < eligibleCount; eligibleIndex++)
            {
                PostItRuntimeData item = eligibleItems[eligibleIndex];
                preparedEntries.Add(new ServerPostItGuessEntry
                {
                    RoundRevision = roundRevision,
                    GuessRevision = guessRevision,
                    OwnerClientId = ownerClientId,
                    PlayerNetworkObjectId = playerNetworkObjectId,
                    PostItId = item.PostItId,
                    SlotIndexAtSnapshot = item.SlotIndex,
                    VisualId = item.VisualId,
                    CorrectTopicId = item.TopicId,
                    SelectedTopicId = PostItTopicId.None,
                    Status = PostItGuessStatus.Pending,
                    IsCorrect = false,
                    BonusApplied = false
                });

                desiredOwnerItems[eligibleIndex] = new PostItGuessOwnerData(
                    roundRevision,
                    guessRevision,
                    item.PostItId,
                    item.VisualId,
                    PostItTopicId.None,
                    PostItTopicId.None,
                    PostItGuessStatus.Pending);
            }

            PostItGuessPlayerScoreData frozenScore = new PostItGuessPlayerScoreData(
                roundRevision,
                guessRevision,
                ownerClientId,
                inventoryItems.Length,
                eligibleCount,
                0,
                0,
                0,
                0);
            if (!frozenScore.IsValid)
            {
                LogAuthorityError($"Prepared an invalid frozen score. ownerClientId={ownerClientId}");
                return false;
            }

            preparedScores.Add(ownerClientId, frozenScore);
            preparedParticipantObjectIds.Add(ownerClientId, playerNetworkObjectId);
            preparedOwnerStates.Add(new PreparedOwnerGuessState
            {
                Inventory = inventory,
                PreviousItems = inventory.GetGuessSnapshot(),
                DesiredItems = desiredOwnerItems
            });
            totalEligibleCount += eligibleCount;
        }

        int publishedOwnerCount = 0;
        for (int stateIndex = 0; stateIndex < preparedOwnerStates.Count; stateIndex++)
        {
            PreparedOwnerGuessState state = preparedOwnerStates[stateIndex];
            if (!state.Inventory.ServerReplaceGuessItems(state.DesiredItems))
            {
                RollBackPreparedOwnerGuessStates(preparedOwnerStates, publishedOwnerCount);
                totalEligibleCount = 0;
                return false;
            }

            publishedOwnerCount++;
        }

        if (!TryReplaceGuessScoreItems(Array.Empty<PostItGuessPlayerScoreData>()))
        {
            RollBackPreparedOwnerGuessStates(preparedOwnerStates, publishedOwnerCount);
            totalEligibleCount = 0;
            return false;
        }

        _serverGuessEntries.Clear();
        _serverGuessEntries.AddRange(preparedEntries);
        _serverGuessScores.Clear();
        foreach (KeyValuePair<ulong, PostItGuessPlayerScoreData> pair in preparedScores)
        {
            _serverGuessScores.Add(pair.Key, pair.Value);
        }

        _serverParticipantPlayerObjectIds.Clear();
        foreach (KeyValuePair<ulong, ulong> pair in preparedParticipantObjectIds)
        {
            _serverParticipantPlayerObjectIds.Add(pair.Key, pair.Value);
        }

        _roundRevision = roundRevision;
        _guessRevision = guessRevision;
        _guessDeadlineServerTime = absoluteDeadline;
        _guessSubmissionOpen = false;
        _finalizedGuessRevision = -1;

        GuessLog(
            $"Prepared frozen guess snapshot. round={roundRevision}, " +
            $"guess={guessRevision}, players={preparedOwnerStates.Count}, " +
            $"eligible={totalEligibleCount}");
        return true;
    }

    public PostItWorldDropData[] GetWorldDropSnapshot()
    {
        return _worldDrops.ToArray();
    }

    public PostItGuessPlayerScoreData[] GetGuessScoreSnapshot()
    {
        return _guessScores.ToArray();
    }

    public bool TryGetGuessScore(
        ulong ownerClientId,
        out PostItGuessPlayerScoreData scoreData)
    {
        for (int i = 0; i < _guessScores.Count; i++)
        {
            if (_guessScores[i].OwnerClientId == ownerClientId)
            {
                scoreData = _guessScores[i];
                return true;
            }
        }

        scoreData = PostItGuessPlayerScoreData.Invalid;
        return false;
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

    private bool TryReplaceGuessScoreItems(
        IReadOnlyList<PostItGuessPlayerScoreData> scoreItems)
    {
        if (!CanMutateServerState() || !ValidateGuessScoreReplacement(scoreItems))
        {
            return false;
        }

        PostItGuessPlayerScoreData[] desiredItems =
            new PostItGuessPlayerScoreData[scoreItems.Count];
        for (int i = 0; i < scoreItems.Count; i++)
        {
            desiredItems[i] = scoreItems[i];
        }

        if (IsNetworkGuessScoreStorageActive())
        {
            if (NetworkGuessScoresMatch(desiredItems))
            {
                return true;
            }

            PostItGuessPlayerScoreData[] previousItems =
                GetAuthoritativeGuessScoreSnapshot();
            if (TryWriteNetworkGuessScores(desiredItems))
            {
                return true;
            }

            if (!TryWriteNetworkGuessScores(previousItems))
            {
                Debug.LogError(
                    $"[{nameof(PostItRoundManager)}] Failed to roll back guess score publication.",
                    this);
            }

            return false;
        }

        if (IsSpawnedNetworkSession())
        {
            LogAuthorityError("Cannot publish guess scores because network storage is unavailable.");
            return false;
        }

        if (LocalGuessScoresMatch(desiredItems))
        {
            return true;
        }

        _guessScores.Clear();
        for (int i = 0; i < desiredItems.Length; i++)
        {
            _guessScores.Add(desiredItems[i]);
        }

        NotifyGuessScoresChanged();
        return true;
    }

    private bool ValidateGuessScoreReplacement(
        IReadOnlyList<PostItGuessPlayerScoreData> scoreItems)
    {
        if (scoreItems == null)
        {
            return false;
        }

        for (int i = 0; i < scoreItems.Count; i++)
        {
            PostItGuessPlayerScoreData score = scoreItems[i];
            if (!score.IsValid)
            {
                return false;
            }

            if (i > 0 &&
                (scoreItems[0].RoundRevision != score.RoundRevision ||
                 scoreItems[0].GuessRevision != score.GuessRevision))
            {
                return false;
            }

            for (int otherIndex = i + 1; otherIndex < scoreItems.Count; otherIndex++)
            {
                if (score.OwnerClientId == scoreItems[otherIndex].OwnerClientId)
                {
                    return false;
                }
            }
        }

        return true;
    }

    private bool TryWriteNetworkGuessScores(
        IReadOnlyList<PostItGuessPlayerScoreData> scoreItems)
    {
        try
        {
            if (_networkGuessScores.Count > 0)
            {
                _networkGuessScores.Clear();
            }

            for (int i = 0; i < scoreItems.Count; i++)
            {
                _networkGuessScores.Add(scoreItems[i]);
            }

            return NetworkGuessScoresMatch(scoreItems);
        }
        catch (Exception exception)
        {
            LogWarning($"Guess score network write threw an exception. message={exception.Message}");
            return false;
        }
    }

    private PostItGuessPlayerScoreData[] GetAuthoritativeGuessScoreSnapshot()
    {
        if (_networkGuessScores == null || _networkGuessScores.Count == 0)
        {
            return Array.Empty<PostItGuessPlayerScoreData>();
        }

        PostItGuessPlayerScoreData[] snapshot =
            new PostItGuessPlayerScoreData[_networkGuessScores.Count];
        for (int i = 0; i < _networkGuessScores.Count; i++)
        {
            snapshot[i] = _networkGuessScores[i];
        }

        return snapshot;
    }

    private bool NetworkGuessScoresMatch(
        IReadOnlyList<PostItGuessPlayerScoreData> expectedItems)
    {
        if (_networkGuessScores == null ||
            _networkGuessScores.Count != expectedItems.Count)
        {
            return false;
        }

        for (int i = 0; i < expectedItems.Count; i++)
        {
            if (!_networkGuessScores[i].Equals(expectedItems[i]))
            {
                return false;
            }
        }

        return true;
    }

    private bool LocalGuessScoresMatch(
        IReadOnlyList<PostItGuessPlayerScoreData> expectedItems)
    {
        if (_guessScores.Count != expectedItems.Count)
        {
            return false;
        }

        for (int i = 0; i < expectedItems.Count; i++)
        {
            if (!_guessScores[i].Equals(expectedItems[i]))
            {
                return false;
            }
        }

        return true;
    }

    private bool TryEvaluateGuessEligibility(
        PostItRuntimeData item,
        ulong inventoryOwnerClientId,
        out bool isEligible)
    {
        isEligible = false;
        if (!item.IsValid)
        {
            LogAuthorityError($"Invalid Post-it in guess snapshot. postItId={item.PostItId}");
            return false;
        }

        if (item.HolderClientId != inventoryOwnerClientId)
        {
            LogAuthorityError(
                $"Inventory holder mismatch in guess snapshot. postItId={item.PostItId}");
            return false;
        }

        if (item.Type != PostItType.Drawing ||
            item.OriginalOwnerClientId == item.HolderClientId)
        {
            return true;
        }

        if (item.SlotIndex < 0 ||
            !PostItVisualCatalogSO.IsSupportedDrawingTopic(item.TopicId))
        {
            LogAuthorityError(
                $"Invalid acquired Drawing metadata. postItId={item.PostItId}");
            return false;
        }

        if (!visualCatalog.TryGetEntryByVisualId(
                item.VisualId,
                out PostItVisualCatalogSO.Entry catalogEntry) ||
            catalogEntry.Type != PostItType.Drawing ||
            catalogEntry.TopicId != item.TopicId)
        {
            LogAuthorityError(
                $"Drawing catalog invariant failed. postItId={item.PostItId}, " +
                $"visualId={item.VisualId}");
            return false;
        }

        isEligible = true;
        return true;
    }

    private void RollBackPreparedOwnerGuessStates(
        IReadOnlyList<PreparedOwnerGuessState> preparedStates,
        int publishedOwnerCount)
    {
        for (int index = publishedOwnerCount - 1; index >= 0; index--)
        {
            PreparedOwnerGuessState state = preparedStates[index];
            if (!state.Inventory.ServerReplaceGuessItems(state.PreviousItems))
            {
                Debug.LogError(
                    $"[{nameof(PostItRoundManager)}] Failed to roll back owner guess state. " +
                    $"ownerClientId={ResolveInventoryOwnerClientId(state.Inventory)}",
                    this);
            }
        }
    }

    private void ClearServerGuessSnapshotState()
    {
        _serverGuessEntries.Clear();
        _serverGuessScores.Clear();
        _serverParticipantPlayerObjectIds.Clear();
        _roundRevision = -1;
        _guessRevision = -1;
        _guessDeadlineServerTime = 0d;
        _guessSubmissionOpen = false;
        _finalizedGuessRevision = -1;
    }

    private static int CompareGuessParticipantsByOwnerClientId(
        PreparedGuessParticipant left,
        PreparedGuessParticipant right)
    {
        return left.OwnerClientId.CompareTo(right.OwnerClientId);
    }

    private static int CompareGuessEligiblePostIts(
        PostItRuntimeData left,
        PostItRuntimeData right)
    {
        int slotComparison = left.SlotIndex.CompareTo(right.SlotIndex);
        return slotComparison != 0
            ? slotComparison
            : left.PostItId.CompareTo(right.PostItId);
    }

    private static ulong ResolveInventoryNetworkObjectId(PlayerPostItInventory inventory)
    {
        if (inventory != null &&
            inventory.NetworkObject != null &&
            inventory.NetworkObject.IsSpawned)
        {
            return inventory.NetworkObjectId;
        }

        return ulong.MaxValue;
    }

    private bool TryBuildInitialAssignmentCatalog(
        out List<PostItVisualCatalogSO.Entry> drawingEntries,
        out List<PostItVisualCatalogSO.Entry> effectEntries,
        out string validationError)
    {
        drawingEntries = new List<PostItVisualCatalogSO.Entry>();
        effectEntries = new List<PostItVisualCatalogSO.Entry>();
        validationError = null;

        if (!visualCatalog.ValidateCatalog(out validationError))
        {
            return false;
        }

        IReadOnlyList<PostItVisualCatalogSO.Entry> entries = visualCatalog.Entries;
        for (int entryIndex = 0; entryIndex < entries.Count; entryIndex++)
        {
            PostItVisualCatalogSO.Entry entry = entries[entryIndex];
            if (!entry.Enabled)
            {
                continue;
            }

            if (entry.Type == PostItType.Drawing &&
                PostItVisualCatalogSO.IsSupportedDrawingTopic(entry.TopicId))
            {
                drawingEntries.Add(entry);
                continue;
            }

            if ((entry.Type == PostItType.Bonus || entry.Type == PostItType.Penalty) &&
                entry.TopicId == PostItTopicId.None)
            {
                effectEntries.Add(entry);
            }
        }

        drawingEntries.Sort(CompareCatalogEntriesByVisualId);
        effectEntries.Sort(CompareCatalogEntriesByVisualId);
        return true;
    }

    private static int CompareCatalogEntriesByVisualId(
        PostItVisualCatalogSO.Entry left,
        PostItVisualCatalogSO.Entry right)
    {
        return left.VisualId.CompareTo(right.VisualId);
    }

    private static int CompareInitialAssignmentsByOwnerClientId(
        PreparedInitialAssignment left,
        PreparedInitialAssignment right)
    {
        return left.OwnerClientId.CompareTo(right.OwnerClientId);
    }

    private static uint ComputeStableAssignmentHash(
        int roundRevision,
        ulong ownerClientId,
        int ordinal,
        uint categorySalt)
    {
        unchecked
        {
            uint hash = 2166136261u;
            hash = (hash ^ (uint)roundRevision) * 16777619u;
            hash = (hash ^ (uint)ownerClientId) * 16777619u;
            hash = (hash ^ (uint)(ownerClientId >> 32)) * 16777619u;
            hash = (hash ^ (uint)ordinal) * 16777619u;
            hash = (hash ^ categorySalt) * 16777619u;
            return hash;
        }
    }

    private static bool TryReserveNextPostItId(
        HashSet<int> reservedPostItIds,
        ref int nextPostItId,
        out int postItId)
    {
        postItId = -1;
        while (reservedPostItIds.Contains(nextPostItId))
        {
            if (nextPostItId == int.MaxValue)
            {
                return false;
            }

            nextPostItId++;
        }

        if (nextPostItId == int.MaxValue)
        {
            return false;
        }

        postItId = nextPostItId;
        reservedPostItIds.Add(postItId);
        nextPostItId++;
        return true;
    }

    private bool TryAddInitialAssignmentItems(PreparedInitialAssignment assignment)
    {
        for (int itemIndex = 0; itemIndex < assignment.DesiredItems.Length; itemIndex++)
        {
            PostItRuntimeData desiredItem = assignment.DesiredItems[itemIndex];
            if (!assignment.Inventory.ServerTryAddPostIt(
                    desiredItem,
                    out PostItRuntimeData assignedItem) ||
                !assignedItem.Equals(desiredItem))
            {
                LogAuthorityError(
                    $"Failed to write initial Post-it. ownerClientId={assignment.OwnerClientId}, " +
                    $"postItId={desiredItem.PostItId}");
                return false;
            }
        }

        return InventoryMatches(assignment.Inventory, assignment.DesiredItems);
    }

    private void RollBackInitialAssignments(
        IReadOnlyList<PreparedInitialAssignment> assignments,
        int mutatedAssignmentCount)
    {
        bool rollbackSucceeded = true;
        for (int assignmentIndex = mutatedAssignmentCount - 1;
             assignmentIndex >= 0;
             assignmentIndex--)
        {
            PreparedInitialAssignment assignment = assignments[assignmentIndex];
            assignment.Inventory.ServerClearPostIts();
            if (!InventoryMatches(assignment.Inventory, Array.Empty<PostItRuntimeData>()))
            {
                rollbackSucceeded = false;
                continue;
            }

            for (int itemIndex = 0;
                 itemIndex < assignment.PreviousItems.Length;
                 itemIndex++)
            {
                PostItRuntimeData previousItem = assignment.PreviousItems[itemIndex];
                if (!assignment.Inventory.ServerTryAddPostIt(previousItem, out PostItRuntimeData restoredItem) ||
                    !restoredItem.Equals(previousItem))
                {
                    rollbackSucceeded = false;
                    break;
                }
            }

            if (!InventoryMatches(assignment.Inventory, assignment.PreviousItems))
            {
                rollbackSucceeded = false;
            }
        }

        if (!rollbackSucceeded)
        {
            Debug.LogError(
                $"[{nameof(PostItRoundManager)}] Failed to roll back initial Post-it assignment.",
                this);
        }
    }

    private static bool InventoryMatches(
        PlayerPostItInventory inventory,
        IReadOnlyList<PostItRuntimeData> expectedItems)
    {
        PostItRuntimeData[] currentItems = inventory.GetSnapshot();
        if (currentItems.Length != expectedItems.Count)
        {
            return false;
        }

        for (int itemIndex = 0; itemIndex < currentItems.Length; itemIndex++)
        {
            if (!currentItems[itemIndex].Equals(expectedItems[itemIndex]))
            {
                return false;
            }
        }

        return true;
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

    private bool IsNetworkGuessScoreStorageActive()
    {
        return _networkGuessScores != null &&
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

    private void SubscribeToGuessScores()
    {
        if (_hasSubscribedToGuessScores || _networkGuessScores == null)
            return;

        _networkGuessScores.OnListChanged += OnNetworkGuessScoresChanged;
        _hasSubscribedToGuessScores = true;
    }

    private void UnsubscribeFromGuessScores()
    {
        if (!_hasSubscribedToGuessScores || _networkGuessScores == null)
            return;

        _networkGuessScores.OnListChanged -= OnNetworkGuessScoresChanged;
        _hasSubscribedToGuessScores = false;
    }

    private void OnNetworkGuessScoresChanged(
        NetworkListEvent<PostItGuessPlayerScoreData> changeEvent)
    {
        RebuildGuessScoreMirror();
        NotifyGuessScoresChanged();
        GuessLog(
            $"Guess score list changed. type={changeEvent.Type}, count={_guessScores.Count}");
    }

    private void RebuildGuessScoreMirror()
    {
        _guessScores.Clear();
        if (_networkGuessScores == null)
            return;

        for (int i = 0; i < _networkGuessScores.Count; i++)
            _guessScores.Add(_networkGuessScores[i]);
    }

    private void NotifyGuessScoresChanged()
    {
        Action handlers = GuessScoresChanged;
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
        initialDrawingPostItCountPerPlayer = Mathf.Max(0, initialDrawingPostItCountPerPlayer);
        initialEffectPostItCountPerPlayer = Mathf.Max(0, initialEffectPostItCountPerPlayer);
        initialPostItCountPerPlayer = Mathf.Max(0, initialPostItCountPerPlayer);
        defaultVisualId = Mathf.Max(0, defaultVisualId);
        guessingDurationSeconds = Mathf.Max(0.1f, guessingDurationSeconds);
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

    private void GuessLog(string message)
    {
        if (debugGuessLogs)
        {
            Debug.Log($"[{nameof(PostItRoundManager)}][Guess] {message}", this);
        }
    }

    private void Log(string message)
    {
        if (debugLogs)
        {
            Debug.Log($"[{nameof(PostItRoundManager)}]\n{message}", this);
        }
    }
}
