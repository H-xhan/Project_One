using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

[DefaultExecutionOrder(-100)]
public class PostItRoundManager : NetworkBehaviour
{
    private const int InitialMapDrawingPostItCount = 2;
    private const int InitialMapBonusPostItCount = 1;
    private const int InitialMapPenaltyPostItCount = 1;
    private const int InitialMapPostItCount =
        InitialMapDrawingPostItCount +
        InitialMapBonusPostItCount +
        InitialMapPenaltyPostItCount;
    private const float MinimumMapSpawnSeparation = 0.5f;
    private const float FallMapOffsetMinimumRadius = 0.75f;
    private const float FallMapOffsetMaximumRadius = 1.25f;
    private const int FallMapOffsetAttemptsPerMarker = 4;
    private const float FallWorldDropMinimumSeparation = 0.75f;
    private const uint FallMapDropSeedSalt = 0x46414C4Cu;
    private const uint FallMapDropAngleSalt = 0x414E474Cu;
    private const uint FallMapDropRadiusSalt = 0x52414449u;
    private const float ZeroPostItPollIntervalSeconds = 0.25f;

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
    private readonly Dictionary<ulong, ulong> _initialAssignmentPlayerObjectIdByOwner =
        new Dictionary<ulong, ulong>();
    private readonly Dictionary<ulong, int> _zeroPostItEliminationRevisionByOwner =
        new Dictionary<ulong, int>();
    private readonly List<ulong> _zeroPostItPollClientIds = new List<ulong>();
    private float _nextZeroPostItPollTime;
    private GameStateManager _gameStateManager;
    private int _lastInitialAssignmentRoundRevision = -1;
    private int _lastExplicitInitialAssignmentRoundRevision = -1;
    private bool _initialAssignmentInProgress;
    private int _zeroPostItEliminationArmedRoundRevision = -1;
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
    private readonly HashSet<ulong> _serverDisconnectedGuessOwners =
        new HashSet<ulong>();
    private readonly HashSet<ulong> _serverZeroScoreGuessOwners =
        new HashSet<ulong>();
    private int _roundRevision = -1;
    private int _guessRevision = -1;
    private double _guessDeadlineServerTime;
    private bool _guessSubmissionOpen;
    private int _finalizedGuessRevision = -1;
    private bool _guessMutationInProgress;

    public event Action WorldDropsChanged;
    public event Action GuessScoresChanged;

    public int WorldDropCount => _worldDrops.Count;
    public IReadOnlyList<PostItWorldDropData> WorldDrops => _worldDrops;
    public int GuessScoreCount => _guessScores.Count;
    public IReadOnlyList<PostItGuessPlayerScoreData> ScoreItems => _guessScores;
    public int ActiveGuessRoundRevision => _roundRevision;
    public int ActiveGuessRevision => _guessRevision;
    public double GuessDeadlineServerTime => _guessDeadlineServerTime;
    public bool IsGuessSubmissionOpen => _guessSubmissionOpen;
    public bool AreAllGuessEntriesResolved =>
        !_guessMutationInProgress &&
        _roundRevision >= 0 &&
        _guessRevision >= 0 &&
        !HasPendingGuessEntries();

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
        public ulong PlayerNetworkObjectId;
        public PostItRuntimeData[] PreviousItems;
        public PostItRuntimeData[] DesiredItems;
    }

    private struct PreparedWorldDrop
    {
        public PostItRuntimeData Payload;
        public PostItWorldDropData PublicData;
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

    private void Update()
    {
        if (!IsServer || !IsSpawned)
            return;

        float now = Time.unscaledTime;
        if (now < _nextZeroPostItPollTime)
            return;

        _nextZeroPostItPollTime = now + ZeroPostItPollIntervalSeconds;
        ServerFlushZeroPostItEliminations();
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
                _initialAssignmentPlayerObjectIdByOwner.Clear();
                _zeroPostItEliminationRevisionByOwner.Clear();
                _zeroPostItPollClientIds.Clear();
                _nextZeroPostItPollTime = 0f;
                _gameStateManager = null;
                _lastInitialAssignmentRoundRevision = -1;
                _lastExplicitInitialAssignmentRoundRevision = -1;
                _initialAssignmentInProgress = false;
                _zeroPostItEliminationArmedRoundRevision = -1;
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
        if (HasListeningNetworkSession())
        {
            LogWarning(
                "The no-revision initial assignment API is unavailable in a network session. " +
                "Use the explicit revision overload before Playing.");
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
        if (HasListeningNetworkSession() && !IsInitialAssignmentState())
        {
            LogWarning("Blocked initial assignment outside Lobby or Countdown.");
            return false;
        }

        return ServerAssignInitialPostItsCore(inventories, roundRevision, true);
    }

    public bool ServerIsCurrentPlayingParticipant(
        PlayerPostItInventory inventory)
    {
        if (!CanMutateServerState() ||
            !IsPlayingState() ||
            inventory == null ||
            !IsValidServerInventory(inventory))
        {
            return false;
        }

        if (!HasListeningNetworkSession())
            return true;

        if (!IsSpawnedNetworkSession())
            return false;

        NetworkObject playerObject = inventory.NetworkObject;
        if (NetworkManager == null ||
            playerObject == null ||
            !playerObject.IsSpawned ||
            inventory.NetworkManager != NetworkManager ||
            inventory.GetComponentInParent<NetworkObject>() != playerObject)
        {
            return false;
        }

        ulong ownerClientId = playerObject.OwnerClientId;
        return ownerClientId != ulong.MaxValue &&
               inventory.OwnerClientId == ownerClientId &&
               NetworkManager.ConnectedClients.TryGetValue(
                   ownerClientId,
                   out NetworkClient client) &&
               client != null &&
               client.PlayerObject == playerObject &&
               _lastInitialAssignmentRoundRevision >= 0 &&
               _zeroPostItEliminationArmedRoundRevision ==
               _lastInitialAssignmentRoundRevision &&
               _initialAssignmentRevisionByOwner.TryGetValue(
                   ownerClientId,
                   out int assignedRoundRevision) &&
               assignedRoundRevision == _lastInitialAssignmentRoundRevision &&
               _initialAssignmentPlayerObjectIdByOwner.TryGetValue(
                   ownerClientId,
                   out ulong assignedPlayerNetworkObjectId) &&
               assignedPlayerNetworkObjectId == playerObject.NetworkObjectId;
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
        if (isNewRoundRevision)
        {
            _zeroPostItEliminationArmedRoundRevision = -1;
        }

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

            ulong playerNetworkObjectId = ResolveInventoryNetworkObjectId(inventory);
            if (playerNetworkObjectId == ulong.MaxValue)
            {
                LogAuthorityError(
                    $"Cannot assign initial Post-its without a spawned Player NetworkObject. " +
                    $"ownerClientId={ownerClientId}");
                return false;
            }

            if (_initialAssignmentRevisionByOwner.TryGetValue(
                    ownerClientId,
                    out int assignedRevision) &&
                assignedRevision == roundRevision &&
                _initialAssignmentPlayerObjectIdByOwner.TryGetValue(
                    ownerClientId,
                    out ulong assignedPlayerNetworkObjectId) &&
                assignedPlayerNetworkObjectId == playerNetworkObjectId)
            {
                continue;
            }

            preparedAssignments.Add(new PreparedInitialAssignment
            {
                Inventory = inventory,
                OwnerClientId = ownerClientId,
                PlayerNetworkObjectId = playerNetworkObjectId,
                PreviousItems = inventory.GetSnapshot()
            });
        }

        if (uniqueOwnerClientIds.Count == 0)
        {
            LogWarning("Rejected initial post-it assignment because no valid inventories were found.");
            return false;
        }

        if (preparedAssignments.Count == 0 && !isNewRoundRevision)
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

        PreparedWorldDrop[] previousWorldDrops = Array.Empty<PreparedWorldDrop>();
        PreparedWorldDrop[] desiredMapWorldDrops = Array.Empty<PreparedWorldDrop>();
        if (isNewRoundRevision)
        {
            if (!TryCaptureWorldDropState(out previousWorldDrops))
            {
                LogAuthorityError(
                    "Cannot replace map Post-its because the current world state is invalid.");
                return false;
            }

            if (!TryPrepareInitialMapWorldDrops(
                    roundRevision,
                    drawingEntries,
                    effectEntries,
                    reservedPostItIds,
                    ref nextPostItId,
                    out desiredMapWorldDrops,
                    out string mapSpawnError))
            {
                LogAuthorityError(
                    $"Cannot prepare initial map Post-its. {mapSpawnError}");
                return false;
            }
        }

        _initialAssignmentInProgress = true;
        bool worldMutationStarted = false;
        int mutatedAssignmentCount = 0;
        try
        {
            if (isNewRoundRevision)
            {
                BeginWorldDropMutation();
                worldMutationStarted = true;
            }

            try
            {
                for (int assignmentIndex = 0;
                     assignmentIndex < preparedAssignments.Count;
                     assignmentIndex++)
                {
                    PreparedInitialAssignment assignment = preparedAssignments[assignmentIndex];
                    mutatedAssignmentCount = assignmentIndex + 1;
                    assignment.Inventory.ServerClearPostIts();
                    if (!InventoryMatches(assignment.Inventory, Array.Empty<PostItRuntimeData>()) ||
                        !TryAddInitialAssignmentItems(assignment))
                    {
                        RollBackInitialAssignments(preparedAssignments, mutatedAssignmentCount);
                        return false;
                    }
                }

                if (isNewRoundRevision &&
                    !TryWriteWorldDropState(desiredMapWorldDrops))
                {
                    bool worldRollbackSucceeded =
                        TryRollBackInitialWorldDropState(previousWorldDrops);
                    bool inventoryRollbackSucceeded =
                        RollBackInitialAssignments(preparedAssignments, mutatedAssignmentCount);
                    if (!worldRollbackSucceeded || !inventoryRollbackSucceeded)
                    {
                        LogAuthorityError(
                            "Initial assignment failed and its compound rollback was incomplete.");
                    }

                    LogAuthorityError(
                        "Initial assignment failed because map Post-its could not be published.");
                    return false;
                }
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, this);
                bool worldRollbackSucceeded =
                    !isNewRoundRevision ||
                    TryRollBackInitialWorldDropState(previousWorldDrops);
                bool inventoryRollbackSucceeded =
                    RollBackInitialAssignments(preparedAssignments, mutatedAssignmentCount);
                if (!worldRollbackSucceeded || !inventoryRollbackSucceeded)
                {
                    LogAuthorityError(
                        "Initial assignment threw and its compound rollback was incomplete.");
                }

                return false;
            }

            _nextPostItId = nextPostItId;
            _lastInitialAssignmentRoundRevision = roundRevision;
            if (isNewRoundRevision)
            {
                _initialAssignmentRevisionByOwner.Clear();
                _initialAssignmentPlayerObjectIdByOwner.Clear();
            }

            for (int assignmentIndex = 0;
                 assignmentIndex < preparedAssignments.Count;
                 assignmentIndex++)
            {
                _initialAssignmentRevisionByOwner[
                    preparedAssignments[assignmentIndex].OwnerClientId] = roundRevision;
                _initialAssignmentPlayerObjectIdByOwner[
                    preparedAssignments[assignmentIndex].OwnerClientId] =
                    preparedAssignments[assignmentIndex].PlayerNetworkObjectId;
            }

            if (isNewRoundRevision)
                _zeroPostItEliminationRevisionByOwner.Clear();

            _zeroPostItEliminationArmedRoundRevision = roundRevision;

            if (explicitRevision)
            {
                _lastExplicitInitialAssignmentRoundRevision = roundRevision;
            }

            Log(
                $"Assigned Initial PostIts\nRound={roundRevision}\n" +
                $"Players={preparedAssignments.Count}\n" +
                $"PlayerPostIts={preparedAssignments.Count * desiredCount}\n" +
                $"MapPostIts={(isNewRoundRevision ? desiredMapWorldDrops.Length : 0)}");
            return true;
        }
        finally
        {
            if (worldMutationStarted)
            {
                EndWorldDropMutation();
            }

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

    public bool ServerFlushZeroPostItEliminations()
    {
        if (!CanEvaluateZeroPostItEliminations() || NetworkManager == null)
            return false;

        return ServerPollZeroPostItEliminations();
    }

    private bool ServerPollZeroPostItEliminations()
    {
        if (!CanEvaluateZeroPostItEliminations() || NetworkManager == null)
            return false;

        bool allZeroInventoriesResolved = true;
        _zeroPostItPollClientIds.Clear();
        try
        {
            foreach (KeyValuePair<ulong, int> assignment in
                     _initialAssignmentRevisionByOwner)
            {
                if (assignment.Value == _zeroPostItEliminationArmedRoundRevision)
                {
                    _zeroPostItPollClientIds.Add(assignment.Key);
                }
            }

            _zeroPostItPollClientIds.Sort();
            for (int clientIndex = 0;
                 clientIndex < _zeroPostItPollClientIds.Count;
                 clientIndex++)
            {
                ulong clientId = _zeroPostItPollClientIds[clientIndex];
                if (!NetworkManager.ConnectedClients.TryGetValue(clientId, out var client))
                {
                    continue;
                }

                if (client == null)
                {
                    allZeroInventoriesResolved = false;
                    continue;
                }

                NetworkObject playerObject = client.PlayerObject;
                if (playerObject == null || !playerObject.IsSpawned)
                    continue;

                if (playerObject.OwnerClientId != clientId ||
                    !_initialAssignmentPlayerObjectIdByOwner.TryGetValue(
                        clientId,
                        out ulong assignedPlayerNetworkObjectId) ||
                    assignedPlayerNetworkObjectId != playerObject.NetworkObjectId)
                {
                    allZeroInventoriesResolved = false;
                    continue;
                }

                PlayerPostItInventory inventory =
                    playerObject.GetComponentInChildren<PlayerPostItInventory>(true);
                if (!IsValidServerInventory(inventory) ||
                    inventory.NetworkObject != playerObject ||
                    inventory.OwnerClientId != clientId)
                {
                    allZeroInventoriesResolved = false;
                    continue;
                }

                bool zeroInventoryResolved =
                    ServerTryHandleZeroPostItEliminationSafely(inventory);
                if (inventory.Count == 0 && !zeroInventoryResolved)
                {
                    allZeroInventoriesResolved = false;
                }
            }
        }
        catch (Exception exception)
        {
            Debug.LogException(exception, this);
            allZeroInventoriesResolved = false;
        }
        finally
        {
            _zeroPostItPollClientIds.Clear();
        }

        return allZeroInventoriesResolved;
    }

    private bool CanEvaluateZeroPostItEliminations()
    {
        return CanMutateServerState() &&
               NetworkManager != null &&
               NetworkManager.IsListening &&
               IsPlayingState() &&
               !_initialAssignmentInProgress &&
               !_isResettingWorldDrops &&
               _worldDropMutationDepth == 0 &&
               !_guessMutationInProgress &&
               _zeroPostItEliminationArmedRoundRevision >= 0 &&
               _zeroPostItEliminationArmedRoundRevision ==
               _lastInitialAssignmentRoundRevision;
    }

    private bool ServerTryHandleZeroPostItElimination(
        PlayerPostItInventory inventory)
    {
        if (!CanEvaluateZeroPostItEliminations() ||
            inventory == null ||
            !IsValidServerInventory(inventory) ||
            inventory.Count != 0)
        {
            return false;
        }

        NetworkObject playerObject = inventory.GetComponentInParent<NetworkObject>();
        if (playerObject == null ||
            !playerObject.IsSpawned ||
            inventory.NetworkObject != playerObject)
        {
            return false;
        }

        ulong ownerClientId = playerObject.OwnerClientId;
        if (ownerClientId == ulong.MaxValue ||
            inventory.OwnerClientId != ownerClientId ||
            NetworkManager == null ||
            !NetworkManager.ConnectedClients.TryGetValue(ownerClientId, out var client) ||
            client == null ||
            client.PlayerObject != playerObject ||
            !_initialAssignmentRevisionByOwner.TryGetValue(
                ownerClientId,
                out int assignedRoundRevision) ||
            assignedRoundRevision != _zeroPostItEliminationArmedRoundRevision ||
            !_initialAssignmentPlayerObjectIdByOwner.TryGetValue(
                ownerClientId,
                out ulong assignedPlayerNetworkObjectId) ||
            assignedPlayerNetworkObjectId != playerObject.NetworkObjectId)
        {
            return false;
        }

        PlayerStatusModule status =
            playerObject.GetComponentInChildren<PlayerStatusModule>(true);
        if (status == null ||
            !status.IsServer ||
            !status.IsSpawned ||
            status.GetComponentInParent<NetworkObject>() != playerObject)
        {
            return false;
        }

        int roundRevision = _zeroPostItEliminationArmedRoundRevision;
        if (_zeroPostItEliminationRevisionByOwner.TryGetValue(
                ownerClientId,
                out int handledRoundRevision) &&
            handledRoundRevision == roundRevision)
        {
            return status.IsEliminated;
        }

        if (status.IsEliminated)
            return false;

        _zeroPostItEliminationRevisionByOwner[ownerClientId] = roundRevision;

        bool eliminated = false;
        try
        {
            eliminated = status.ServerEliminateForPostItDepletion();
        }
        catch (Exception exception)
        {
            Debug.LogException(exception, this);
            eliminated = status != null && status.IsEliminated;
        }

        if (eliminated)
        {
            Log(
                $"Eliminated player for zero Post-its. " +
                $"round={roundRevision}, ownerClientId={ownerClientId}");
            return true;
        }

        if (_zeroPostItEliminationRevisionByOwner.TryGetValue(
                ownerClientId,
                out int reservedRoundRevision) &&
            reservedRoundRevision == roundRevision)
        {
            _zeroPostItEliminationRevisionByOwner.Remove(ownerClientId);
        }

        return false;
    }

    private bool ServerTryHandleZeroPostItEliminationSafely(
        PlayerPostItInventory inventory)
    {
        try
        {
            return ServerTryHandleZeroPostItElimination(inventory);
        }
        catch (Exception exception)
        {
            Debug.LogException(exception, this);
            return false;
        }
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
        IEnumerable<ulong> zeroScoreOwnerClientIds,
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
            zeroScoreOwnerClientIds == null ||
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

            if (_lastInitialAssignmentRoundRevision >= 0 &&
                (!_initialAssignmentRevisionByOwner.TryGetValue(
                     ownerClientId,
                     out int assignedRoundRevision) ||
                 assignedRoundRevision != roundRevision ||
                 !_initialAssignmentPlayerObjectIdByOwner.TryGetValue(
                     ownerClientId,
                     out ulong assignedPlayerNetworkObjectId) ||
                 assignedPlayerNetworkObjectId != playerNetworkObjectId))
            {
                LogAuthorityError(
                    $"Cannot prepare guess snapshot for a non-current Player assignment. " +
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

        List<ulong> orderedZeroScoreOwnerClientIds = new List<ulong>();
        foreach (ulong ownerClientId in zeroScoreOwnerClientIds)
        {
            if (ownerClientId == ulong.MaxValue ||
                !includedOwnerClientIds.Add(ownerClientId) ||
                !_initialAssignmentRevisionByOwner.TryGetValue(
                    ownerClientId,
                    out int assignedRoundRevision) ||
                assignedRoundRevision != roundRevision ||
                !_initialAssignmentPlayerObjectIdByOwner.ContainsKey(ownerClientId))
            {
                LogAuthorityError(
                    $"Rejected invalid or duplicate zero-score owner. " +
                    $"ownerClientId={ownerClientId}");
                return false;
            }

            if (NetworkManager != null &&
                NetworkManager.IsListening &&
                NetworkManager.ConnectedClients.TryGetValue(
                    ownerClientId,
                    out NetworkClient client))
            {
                if (client == null)
                {
                    return false;
                }

                NetworkObject playerObject = client.PlayerObject;
                if (playerObject != null &&
                    playerObject.IsSpawned &&
                    !IsValidRetainedZeroScoreParticipant(
                        ownerClientId,
                        playerObject,
                        roundRevision))
                {
                    LogAuthorityError(
                        $"Cannot freeze an invalid retained Player as zero-score. " +
                        $"ownerClientId={ownerClientId}");
                    return false;
                }
            }

            orderedZeroScoreOwnerClientIds.Add(ownerClientId);
        }

        if (orderedParticipants.Count == 0 &&
            orderedZeroScoreOwnerClientIds.Count == 0)
        {
            LogWarning("Rejected guess snapshot preparation because no round participants were found.");
            return false;
        }

        orderedParticipants.Sort(CompareGuessParticipantsByOwnerClientId);
        orderedZeroScoreOwnerClientIds.Sort();
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

        for (int ownerIndex = 0;
             ownerIndex < orderedZeroScoreOwnerClientIds.Count;
             ownerIndex++)
        {
            ulong ownerClientId = orderedZeroScoreOwnerClientIds[ownerIndex];
            PostItGuessPlayerScoreData zeroScore = new PostItGuessPlayerScoreData(
                roundRevision,
                guessRevision,
                ownerClientId,
                0,
                0,
                0,
                0,
                0,
                0);
            if (!zeroScore.IsValid)
            {
                return false;
            }

            preparedScores.Add(ownerClientId, zeroScore);
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
        _serverDisconnectedGuessOwners.Clear();
        _serverZeroScoreGuessOwners.Clear();
        for (int ownerIndex = 0;
             ownerIndex < orderedZeroScoreOwnerClientIds.Count;
             ownerIndex++)
        {
            _serverZeroScoreGuessOwners.Add(
                orderedZeroScoreOwnerClientIds[ownerIndex]);
        }

        _roundRevision = roundRevision;
        _guessRevision = guessRevision;
        _guessDeadlineServerTime = absoluteDeadline;
        _guessSubmissionOpen = false;
        _finalizedGuessRevision = -1;

        GuessLog(
            $"Prepared frozen guess snapshot. round={roundRevision}, " +
            $"guess={guessRevision}, players={preparedScores.Count}, " +
            $"eligible={totalEligibleCount}");
        return true;
    }

    private bool IsValidRetainedZeroScoreParticipant(
        ulong ownerClientId,
        NetworkObject playerObject,
        int roundRevision)
    {
        if (ownerClientId == ulong.MaxValue ||
            playerObject == null ||
            !playerObject.IsSpawned ||
            playerObject.OwnerClientId != ownerClientId ||
            !_initialAssignmentRevisionByOwner.TryGetValue(
                ownerClientId,
                out int assignedRoundRevision) ||
            assignedRoundRevision != roundRevision ||
            !_initialAssignmentPlayerObjectIdByOwner.TryGetValue(
                ownerClientId,
                out ulong assignedPlayerNetworkObjectId) ||
            assignedPlayerNetworkObjectId != playerObject.NetworkObjectId ||
            !_zeroPostItEliminationRevisionByOwner.TryGetValue(
                ownerClientId,
                out int eliminatedRoundRevision) ||
            eliminatedRoundRevision != roundRevision)
        {
            return false;
        }

        PlayerPostItInventory inventory =
            playerObject.GetComponentInChildren<PlayerPostItInventory>(true);
        PlayerStatusModule status =
            playerObject.GetComponentInChildren<PlayerStatusModule>(true);
        return IsValidServerInventory(inventory) &&
               inventory.NetworkObject == playerObject &&
               inventory.OwnerClientId == ownerClientId &&
               inventory.Count == 0 &&
               status != null &&
               status.IsServer &&
               status.IsSpawned &&
               status.IsEliminated &&
               status.GetComponentInParent<NetworkObject>() == playerObject;
    }

    public bool ServerBeginGuessing(
        IEnumerable<PlayerPostItInventory> inventories,
        int roundRevision,
        int guessRevision,
        out int totalEligibleCount,
        out double absoluteDeadlineServerTime)
    {
        return ServerBeginGuessing(
            inventories,
            Array.Empty<ulong>(),
            roundRevision,
            guessRevision,
            out totalEligibleCount,
            out absoluteDeadlineServerTime);
    }

    public bool ServerBeginGuessing(
        IEnumerable<PlayerPostItInventory> inventories,
        IEnumerable<ulong> zeroScoreOwnerClientIds,
        int roundRevision,
        int guessRevision,
        out int totalEligibleCount,
        out double absoluteDeadlineServerTime)
    {
        totalEligibleCount = 0;
        absoluteDeadlineServerTime = 0d;
        if (_guessMutationInProgress)
        {
            return false;
        }

        _guessMutationInProgress = true;
        try
        {
            return ServerBeginGuessingCore(
                inventories,
                zeroScoreOwnerClientIds,
                roundRevision,
                guessRevision,
                out totalEligibleCount,
                out absoluteDeadlineServerTime);
        }
        finally
        {
            _guessMutationInProgress = false;
        }
    }

    private bool ServerBeginGuessingCore(
        IEnumerable<PlayerPostItInventory> inventories,
        IEnumerable<ulong> zeroScoreOwnerClientIds,
        int roundRevision,
        int guessRevision,
        out int totalEligibleCount,
        out double absoluteDeadlineServerTime)
    {
        totalEligibleCount = 0;
        absoluteDeadlineServerTime = 0d;
        if (!CanMutateServerState())
        {
            LogAuthorityError("Blocked Guessing begin on non-server instance.");
            return false;
        }

        if (inventories == null ||
            zeroScoreOwnerClientIds == null ||
            roundRevision < 0 ||
            guessRevision < 0)
        {
            LogWarning("Rejected invalid Guessing begin request.");
            return false;
        }

        if (!IsPlayingState())
        {
            LogWarning("Rejected Guessing begin outside Playing.");
            return false;
        }

        if (_roundRevision >= 0 ||
            _guessRevision >= 0 ||
            _serverGuessScores.Count > 0 ||
            _serverGuessEntries.Count > 0)
        {
            LogWarning("Rejected Guessing begin because previous Guess state was not cleared.");
            return false;
        }

        if (_lastInitialAssignmentRoundRevision >= 0 &&
            roundRevision != _lastInitialAssignmentRoundRevision)
        {
            LogWarning(
                $"Rejected Guessing begin for a non-current round. roundRevision={roundRevision}");
            return false;
        }

        List<PlayerPostItInventory> inventoryList =
            new List<PlayerPostItInventory>();
        foreach (PlayerPostItInventory inventory in inventories)
        {
            inventoryList.Add(inventory);
            if (IsValidServerInventory(inventory) && inventory.GuessItemCount > 0)
            {
                LogAuthorityError(
                    "Cannot begin Guessing because an Owner list was not cleared.");
                return false;
            }
        }

        double serverTime = GetAuthoritativeServerTime();
        if (double.IsNaN(serverTime) || double.IsInfinity(serverTime) || serverTime < 0d)
        {
            LogAuthorityError("Cannot begin Guessing because ServerTime is invalid.");
            return false;
        }

        absoluteDeadlineServerTime =
            serverTime + Math.Max(0.1d, guessingDurationSeconds);
        if (!TryPrepareServerGuessSnapshot(
                inventoryList,
                zeroScoreOwnerClientIds,
                roundRevision,
                guessRevision,
                absoluteDeadlineServerTime,
                out totalEligibleCount))
        {
            absoluteDeadlineServerTime = 0d;
            return false;
        }

        _guessSubmissionOpen = totalEligibleCount > 0;
        if (totalEligibleCount == 0 &&
            !TryFinalizeGuessingCore(roundRevision, guessRevision, true, false))
        {
            LogAuthorityError("Failed to finalize a zero-candidate Guessing snapshot.");
            if (!ServerClearGuessStateCore(inventoryList))
            {
                LogAuthorityError(
                    "Failed to clear a rejected zero-candidate Guessing snapshot.");
            }

            totalEligibleCount = 0;
            absoluteDeadlineServerTime = 0d;
            return false;
        }

        GuessLog(
            $"Began Guessing. round={roundRevision}, guess={guessRevision}, " +
            $"eligible={totalEligibleCount}, deadline={absoluteDeadlineServerTime:F3}");
        return true;
    }

    public bool ServerTrySubmitGuess(
        PlayerPostItInventory requesterInventory,
        ulong senderClientId,
        int roundRevision,
        int guessRevision,
        int postItId,
        PostItTopicId selectedTopicId,
        out bool allSubmissionsResolved)
    {
        allSubmissionsResolved = false;
        if (_guessMutationInProgress)
        {
            return false;
        }

        _guessMutationInProgress = true;
        try
        {
            return ServerTrySubmitGuessCore(
                requesterInventory,
                senderClientId,
                roundRevision,
                guessRevision,
                postItId,
                selectedTopicId,
                out allSubmissionsResolved);
        }
        finally
        {
            _guessMutationInProgress = false;
        }
    }

    private bool ServerTrySubmitGuessCore(
        PlayerPostItInventory requesterInventory,
        ulong senderClientId,
        int roundRevision,
        int guessRevision,
        int postItId,
        PostItTopicId selectedTopicId,
        out bool allSubmissionsResolved)
    {
        allSubmissionsResolved = false;
        if (!CanMutateServerState() || !IsGuessingState())
        {
            return false;
        }

        if (roundRevision != _roundRevision ||
            guessRevision != _guessRevision ||
            _finalizedGuessRevision == guessRevision ||
            postItId < 0 ||
            !PostItVisualCatalogSO.IsSupportedDrawingTopic(selectedTopicId) ||
            visualCatalog == null ||
            !visualCatalog.TryGetDrawingEntry(selectedTopicId, out _))
        {
            return false;
        }

        if (!TryResolveConnectedGuessParticipant(
                senderClientId,
                requesterInventory,
                out PlayerPostItInventory resolvedInventory))
        {
            return false;
        }

        int entryIndex = FindUniqueServerGuessEntryIndex(senderClientId, postItId);
        if (entryIndex < 0)
        {
            return false;
        }

        ServerPostItGuessEntry entry = _serverGuessEntries[entryIndex];
        if (entry.RoundRevision != roundRevision ||
            entry.GuessRevision != guessRevision ||
            entry.PlayerNetworkObjectId != resolvedInventory.NetworkObjectId)
        {
            return false;
        }

        if (!resolvedInventory.TryGetGuessItem(
                postItId,
                out PostItGuessOwnerData ownerData) ||
            ownerData.RoundRevision != roundRevision ||
            ownerData.GuessRevision != guessRevision ||
            ownerData.VisualId != entry.VisualId)
        {
            return false;
        }

        if (entry.Status != PostItGuessStatus.Pending ||
            entry.SelectedTopicId != PostItTopicId.None ||
            entry.IsCorrect ||
            entry.BonusApplied ||
            ownerData.Status != PostItGuessStatus.Pending ||
            ownerData.SelectedTopicId != PostItTopicId.None ||
            ownerData.RevealedTopicId != PostItTopicId.None ||
            !_guessSubmissionOpen)
        {
            return false;
        }

        double serverTime = GetAuthoritativeServerTime();
        if (double.IsNaN(serverTime) ||
            double.IsInfinity(serverTime) ||
            serverTime < 0d ||
            serverTime >= _guessDeadlineServerTime)
        {
            _guessSubmissionOpen = false;
            return false;
        }

        if (!_serverGuessScores.TryGetValue(
                senderClientId,
                out PostItGuessPlayerScoreData currentScore) ||
            currentScore.RoundRevision != roundRevision ||
            currentScore.GuessRevision != guessRevision ||
            currentScore.SubmittedCount >= currentScore.EligibleCount)
        {
            LogAuthorityError(
                $"Guess score invariant failed before submit. ownerClientId={senderClientId}");
            return false;
        }

        PostItGuessOwnerData submittedOwnerData = new PostItGuessOwnerData(
            roundRevision,
            guessRevision,
            entry.PostItId,
            entry.VisualId,
            selectedTopicId,
            PostItTopicId.None,
            PostItGuessStatus.Submitted);
        PostItGuessPlayerScoreData submittedScore = new PostItGuessPlayerScoreData(
            currentScore.RoundRevision,
            currentScore.GuessRevision,
            currentScore.OwnerClientId,
            currentScore.HeldPostItCount,
            currentScore.EligibleCount,
            currentScore.SubmittedCount + 1,
            currentScore.CorrectCount,
            currentScore.GuessBonusScore,
            currentScore.FinalRoundScore);
        if (!submittedOwnerData.IsValid ||
            !submittedScore.IsValid ||
            !resolvedInventory.ServerTryUpdateGuessItem(submittedOwnerData))
        {
            return false;
        }

        entry.SelectedTopicId = selectedTopicId;
        entry.Status = PostItGuessStatus.Submitted;
        _serverGuessEntries[entryIndex] = entry;
        _serverGuessScores[senderClientId] = submittedScore;

        allSubmissionsResolved = !HasPendingGuessEntries();
        if (allSubmissionsResolved)
        {
            _guessSubmissionOpen = false;
        }

        GuessLog(
            $"Accepted Guess submission. ownerClientId={senderClientId}, " +
            $"postItId={postItId}, allResolved={allSubmissionsResolved}");
        return true;
    }

    public bool ServerFinalizeGuessing(int roundRevision, int guessRevision)
    {
        if (_guessMutationInProgress)
        {
            return false;
        }

        _guessMutationInProgress = true;
        try
        {
            return TryFinalizeGuessingCore(roundRevision, guessRevision, false, false);
        }
        finally
        {
            _guessMutationInProgress = false;
        }
    }

    public bool ServerFinalizeGuessingImmediately(
        int roundRevision,
        int guessRevision)
    {
        if (_guessMutationInProgress)
        {
            return false;
        }

        _guessMutationInProgress = true;
        try
        {
            if (!IsGuessingState())
            {
                return false;
            }

            return TryFinalizeGuessingCore(roundRevision, guessRevision, false, true);
        }
        finally
        {
            _guessMutationInProgress = false;
        }
    }

    public bool ServerHandleGuessDisconnect(
        ulong ownerClientId,
        int roundRevision,
        int guessRevision,
        out bool allSubmissionsResolved)
    {
        allSubmissionsResolved = false;
        if (_guessMutationInProgress)
        {
            return false;
        }

        _guessMutationInProgress = true;
        try
        {
            return ServerHandleGuessDisconnectCore(
                ownerClientId,
                roundRevision,
                guessRevision,
                out allSubmissionsResolved);
        }
        finally
        {
            _guessMutationInProgress = false;
        }
    }

    private bool ServerHandleGuessDisconnectCore(
        ulong ownerClientId,
        int roundRevision,
        int guessRevision,
        out bool allSubmissionsResolved)
    {
        allSubmissionsResolved = false;
        if (!CanMutateServerState() ||
            ownerClientId == ulong.MaxValue ||
            roundRevision != _roundRevision ||
            guessRevision != _guessRevision ||
            (!_serverParticipantPlayerObjectIds.ContainsKey(ownerClientId) &&
             !_serverDisconnectedGuessOwners.Contains(ownerClientId) &&
             !_serverZeroScoreGuessOwners.Contains(ownerClientId)))
        {
            return false;
        }

        if (_serverZeroScoreGuessOwners.Contains(ownerClientId))
        {
            allSubmissionsResolved = !HasPendingGuessEntries();
            if (allSubmissionsResolved)
            {
                _guessSubmissionOpen = false;
            }

            return true;
        }

        _serverParticipantPlayerObjectIds.Remove(ownerClientId);
        _serverDisconnectedGuessOwners.Add(ownerClientId);

        if (_finalizedGuessRevision == guessRevision)
        {
            allSubmissionsResolved = true;
            return true;
        }

        for (int entryIndex = 0; entryIndex < _serverGuessEntries.Count; entryIndex++)
        {
            ServerPostItGuessEntry entry = _serverGuessEntries[entryIndex];
            if (entry.OwnerClientId != ownerClientId ||
                entry.Status != PostItGuessStatus.Pending)
            {
                continue;
            }

            entry.Status = PostItGuessStatus.Skipped;
            entry.SelectedTopicId = PostItTopicId.None;
            entry.IsCorrect = false;
            entry.BonusApplied = false;
            _serverGuessEntries[entryIndex] = entry;
        }

        allSubmissionsResolved = !HasPendingGuessEntries();
        if (allSubmissionsResolved)
        {
            _guessSubmissionOpen = false;
        }

        GuessLog(
            $"Handled Guess disconnect. ownerClientId={ownerClientId}, " +
            $"allResolved={allSubmissionsResolved}");
        return true;
    }

    public bool ServerClearGuessState(IEnumerable<PlayerPostItInventory> inventories)
    {
        if (_guessMutationInProgress)
        {
            return false;
        }

        _guessMutationInProgress = true;
        try
        {
            return ServerClearGuessStateCore(inventories);
        }
        finally
        {
            _guessMutationInProgress = false;
        }
    }

    private bool ServerClearGuessStateCore(
        IEnumerable<PlayerPostItInventory> inventories)
    {
        if (!CanMutateServerState() || inventories == null)
        {
            return false;
        }

        List<PreparedOwnerGuessState> preparedStates =
            new List<PreparedOwnerGuessState>();
        HashSet<ulong> includedOwners = new HashSet<ulong>();
        Dictionary<ulong, ulong> includedPlayerObjectIds =
            new Dictionary<ulong, ulong>();
        foreach (PlayerPostItInventory inventory in inventories)
        {
            if (!IsValidServerInventory(inventory))
            {
                continue;
            }

            ulong ownerClientId = ResolveInventoryOwnerClientId(inventory);
            if (ownerClientId == ulong.MaxValue || !includedOwners.Add(ownerClientId))
            {
                return false;
            }

            preparedStates.Add(new PreparedOwnerGuessState
            {
                Inventory = inventory,
                PreviousItems = inventory.GetGuessSnapshot(),
                DesiredItems = Array.Empty<PostItGuessOwnerData>()
            });
            includedPlayerObjectIds.Add(
                ownerClientId,
                ResolveInventoryNetworkObjectId(inventory));
        }

        foreach (KeyValuePair<ulong, ulong> participant in
                 _serverParticipantPlayerObjectIds)
        {
            if (!TryGetGuessClientPlayerObjectState(
                    participant.Key,
                    out bool hasSpawnedPlayerObject))
            {
                return false;
            }

            if (!hasSpawnedPlayerObject)
            {
                continue;
            }

            if (!includedPlayerObjectIds.TryGetValue(
                    participant.Key,
                    out ulong includedPlayerObjectId) ||
                includedPlayerObjectId != participant.Value)
            {
                LogAuthorityError(
                    $"Cannot clear Guess state without a connected participant. " +
                    $"ownerClientId={participant.Key}");
                return false;
            }
        }

        preparedStates.Sort(ComparePreparedOwnerGuessStatesByOwnerClientId);
        _guessSubmissionOpen = false;
        int publishedOwnerCount = 0;
        for (int stateIndex = 0; stateIndex < preparedStates.Count; stateIndex++)
        {
            if (!preparedStates[stateIndex].Inventory.ServerClearGuessItems())
            {
                RollBackPreparedOwnerGuessStates(
                    preparedStates,
                    publishedOwnerCount);
                return false;
            }

            publishedOwnerCount++;
        }

        if (!TryReplaceGuessScoreItems(Array.Empty<PostItGuessPlayerScoreData>()))
        {
            RollBackPreparedOwnerGuessStates(
                preparedStates,
                publishedOwnerCount);
            return false;
        }

        ClearServerGuessSnapshotState();
        GuessLog("Cleared Guess state.");
        return true;
    }

    public bool ServerTryBuildFinalRoundScores(
        int roundRevision,
        int guessRevision,
        out PostItGuessPlayerScoreData[] scores)
    {
        scores = Array.Empty<PostItGuessPlayerScoreData>();
        if (_guessMutationInProgress ||
            !CanMutateServerState() ||
            roundRevision != _roundRevision ||
            guessRevision != _guessRevision ||
            _finalizedGuessRevision != guessRevision ||
            _guessSubmissionOpen ||
            !TryGetCommittedFinalScores(out PostItGuessPlayerScoreData[] committedScores))
        {
            return false;
        }

        if (IsNetworkGuessScoreStorageActive() &&
            !NetworkGuessScoresMatch(committedScores))
        {
            LogAuthorityError("Final Guess score publication does not match private state.");
            return false;
        }

        scores = committedScores;
        return true;
    }

    private bool TryFinalizeGuessingCore(
        int roundRevision,
        int guessRevision,
        bool allowOutsideGuessingForZeroCandidates,
        bool allowBeforeDeadline)
    {
        if (!CanMutateServerState() ||
            roundRevision != _roundRevision ||
            guessRevision != _guessRevision)
        {
            return false;
        }

        if (_finalizedGuessRevision == guessRevision)
        {
            return true;
        }

        bool hasCandidates = _serverGuessEntries.Count > 0;
        if (!IsGuessingState() &&
            !(allowOutsideGuessingForZeroCandidates && !hasCandidates))
        {
            return false;
        }

        bool hasPendingEntries = HasPendingGuessEntries();
        double serverTime = GetAuthoritativeServerTime();
        if (double.IsNaN(serverTime) ||
            double.IsInfinity(serverTime) ||
            serverTime < 0d)
        {
            return false;
        }

        if (hasPendingEntries &&
            !allowBeforeDeadline &&
            serverTime < _guessDeadlineServerTime)
        {
            return false;
        }

        _guessSubmissionOpen = false;
        if (!TryBuildFinalizedGuessState(
                out ServerPostItGuessEntry[] finalizedEntries,
                out PostItGuessPlayerScoreData[] finalizedScores) ||
            !TryPrepareFinalOwnerPublications(
                finalizedEntries,
                finalizedScores,
                out List<PreparedOwnerGuessState> preparedOwnerStates))
        {
            return false;
        }

        int publishedOwnerCount = 0;
        for (int stateIndex = 0;
             stateIndex < preparedOwnerStates.Count;
             stateIndex++)
        {
            PreparedOwnerGuessState state = preparedOwnerStates[stateIndex];
            if (!state.Inventory.ServerReplaceGuessItems(state.DesiredItems))
            {
                RollBackPreparedOwnerGuessStates(preparedOwnerStates, publishedOwnerCount);
                return false;
            }

            publishedOwnerCount++;
        }

        if (!TryReplaceGuessScoreItems(finalizedScores))
        {
            RollBackPreparedOwnerGuessStates(preparedOwnerStates, publishedOwnerCount);
            return false;
        }

        _serverGuessEntries.Clear();
        _serverGuessEntries.AddRange(finalizedEntries);
        _serverGuessScores.Clear();
        for (int scoreIndex = 0; scoreIndex < finalizedScores.Length; scoreIndex++)
        {
            PostItGuessPlayerScoreData score = finalizedScores[scoreIndex];
            _serverGuessScores.Add(score.OwnerClientId, score);
        }

        _finalizedGuessRevision = guessRevision;
        GuessLog(
            $"Finalized Guessing. round={roundRevision}, guess={guessRevision}, " +
            $"players={finalizedScores.Length}");
        return true;
    }

    private bool TryBuildFinalizedGuessState(
        out ServerPostItGuessEntry[] finalizedEntries,
        out PostItGuessPlayerScoreData[] finalizedScores)
    {
        finalizedEntries = Array.Empty<ServerPostItGuessEntry>();
        finalizedScores = Array.Empty<PostItGuessPlayerScoreData>();
        if (_roundRevision < 0 ||
            _guessRevision < 0 ||
            _serverGuessScores.Count == 0 ||
            !ValidateFrozenGuessParticipantSets())
        {
            return false;
        }

        Dictionary<ulong, int> entryCounts = new Dictionary<ulong, int>();
        Dictionary<ulong, int> submittedCounts = new Dictionary<ulong, int>();
        Dictionary<ulong, int> correctCounts = new Dictionary<ulong, int>();
        foreach (ulong ownerClientId in _serverGuessScores.Keys)
        {
            entryCounts.Add(ownerClientId, 0);
            submittedCounts.Add(ownerClientId, 0);
            correctCounts.Add(ownerClientId, 0);
        }

        ServerPostItGuessEntry[] preparedEntries =
            new ServerPostItGuessEntry[_serverGuessEntries.Count];
        HashSet<int> includedPostItIds = new HashSet<int>();
        for (int entryIndex = 0; entryIndex < _serverGuessEntries.Count; entryIndex++)
        {
            ServerPostItGuessEntry entry = _serverGuessEntries[entryIndex];
            if (entry.RoundRevision != _roundRevision ||
                entry.GuessRevision != _guessRevision ||
                entry.PostItId < 0 ||
                entry.SlotIndexAtSnapshot < 0 ||
                entry.VisualId <= 0 ||
                !entryCounts.ContainsKey(entry.OwnerClientId) ||
                !includedPostItIds.Add(entry.PostItId) ||
                !IsFrozenGuessCatalogEntryValid(entry) ||
                !IsFrozenGuessParticipantIdentityValid(entry))
            {
                LogAuthorityError("Frozen Guess entry invariant failed during finalize.");
                return false;
            }

            entryCounts[entry.OwnerClientId]++;
            switch (entry.Status)
            {
                case PostItGuessStatus.Pending:
                    if (entry.SelectedTopicId != PostItTopicId.None ||
                        entry.IsCorrect ||
                        entry.BonusApplied)
                    {
                        return false;
                    }

                    entry.Status = PostItGuessStatus.Skipped;
                    break;

                case PostItGuessStatus.Submitted:
                    if (!IsGuessTopicOptionValid(entry.SelectedTopicId) ||
                        entry.IsCorrect ||
                        entry.BonusApplied)
                    {
                        return false;
                    }

                    submittedCounts[entry.OwnerClientId]++;
                    entry.IsCorrect = entry.SelectedTopicId == entry.CorrectTopicId;
                    entry.Status = entry.IsCorrect
                        ? PostItGuessStatus.Correct
                        : PostItGuessStatus.Incorrect;
                    entry.BonusApplied = entry.IsCorrect;
                    if (entry.IsCorrect)
                    {
                        correctCounts[entry.OwnerClientId]++;
                    }

                    break;

                case PostItGuessStatus.Skipped:
                    if (entry.SelectedTopicId != PostItTopicId.None ||
                        entry.IsCorrect ||
                        entry.BonusApplied)
                    {
                        return false;
                    }

                    break;

                default:
                    return false;
            }

            preparedEntries[entryIndex] = entry;
        }

        List<ulong> orderedOwnerClientIds =
            new List<ulong>(_serverGuessScores.Keys);
        orderedOwnerClientIds.Sort();
        PostItGuessPlayerScoreData[] preparedScores =
            new PostItGuessPlayerScoreData[orderedOwnerClientIds.Count];
        for (int ownerIndex = 0;
             ownerIndex < orderedOwnerClientIds.Count;
             ownerIndex++)
        {
            ulong ownerClientId = orderedOwnerClientIds[ownerIndex];
            PostItGuessPlayerScoreData frozenScore = _serverGuessScores[ownerClientId];
            int submittedCount = submittedCounts[ownerClientId];
            int correctCount = correctCounts[ownerClientId];
            bool isZeroScoreOwner =
                _serverZeroScoreGuessOwners.Contains(ownerClientId);
            if (!frozenScore.IsValid ||
                frozenScore.RoundRevision != _roundRevision ||
                frozenScore.GuessRevision != _guessRevision ||
                frozenScore.EligibleCount != entryCounts[ownerClientId] ||
                frozenScore.SubmittedCount != submittedCount ||
                frozenScore.CorrectCount != 0 ||
                frozenScore.GuessBonusScore != 0 ||
                frozenScore.FinalRoundScore != 0 ||
                (isZeroScoreOwner &&
                 (!HasZeroRoundScoreValues(frozenScore) ||
                  submittedCount != 0 ||
                  correctCount != 0)))
            {
                LogAuthorityError(
                    $"Frozen Guess score invariant failed. ownerClientId={ownerClientId}");
                return false;
            }

            long finalRoundScoreLong =
                (long)frozenScore.HeldPostItCount + correctCount;
            if (finalRoundScoreLong > int.MaxValue)
            {
                LogAuthorityError("Final round score exceeds the supported range.");
                return false;
            }

            PostItGuessPlayerScoreData finalizedScore =
                new PostItGuessPlayerScoreData(
                    _roundRevision,
                    _guessRevision,
                    ownerClientId,
                    frozenScore.HeldPostItCount,
                    frozenScore.EligibleCount,
                    submittedCount,
                    correctCount,
                    correctCount,
                    (int)finalRoundScoreLong);
            if (!finalizedScore.IsValid)
            {
                return false;
            }

            preparedScores[ownerIndex] = finalizedScore;
        }

        finalizedEntries = preparedEntries;
        finalizedScores = preparedScores;
        return true;
    }

    private bool TryPrepareFinalOwnerPublications(
        IReadOnlyList<ServerPostItGuessEntry> finalizedEntries,
        IReadOnlyList<PostItGuessPlayerScoreData> finalizedScores,
        out List<PreparedOwnerGuessState> preparedOwnerStates)
    {
        preparedOwnerStates = new List<PreparedOwnerGuessState>();
        Dictionary<ulong, List<PostItGuessOwnerData>> ownerItems =
            new Dictionary<ulong, List<PostItGuessOwnerData>>();
        for (int scoreIndex = 0; scoreIndex < finalizedScores.Count; scoreIndex++)
        {
            ownerItems.Add(
                finalizedScores[scoreIndex].OwnerClientId,
                new List<PostItGuessOwnerData>());
        }

        for (int entryIndex = 0; entryIndex < finalizedEntries.Count; entryIndex++)
        {
            ServerPostItGuessEntry entry = finalizedEntries[entryIndex];
            if (!ownerItems.TryGetValue(
                    entry.OwnerClientId,
                    out List<PostItGuessOwnerData> items) ||
                (entry.Status != PostItGuessStatus.Correct &&
                 entry.Status != PostItGuessStatus.Incorrect &&
                 entry.Status != PostItGuessStatus.Skipped))
            {
                return false;
            }

            PostItGuessOwnerData ownerData = new PostItGuessOwnerData(
                entry.RoundRevision,
                entry.GuessRevision,
                entry.PostItId,
                entry.VisualId,
                entry.SelectedTopicId,
                entry.CorrectTopicId,
                entry.Status);
            if (!ownerData.IsValid)
            {
                return false;
            }

            items.Add(ownerData);
        }

        for (int scoreIndex = 0; scoreIndex < finalizedScores.Count; scoreIndex++)
        {
            PostItGuessPlayerScoreData score = finalizedScores[scoreIndex];
            if (ownerItems[score.OwnerClientId].Count != score.EligibleCount)
            {
                return false;
            }

            if (_serverDisconnectedGuessOwners.Contains(score.OwnerClientId) ||
                _serverZeroScoreGuessOwners.Contains(score.OwnerClientId))
            {
                continue;
            }

            if (!TryGetGuessClientPlayerObjectState(
                    score.OwnerClientId,
                    out bool hasSpawnedPlayerObject))
            {
                return false;
            }

            if (!hasSpawnedPlayerObject)
                continue;

            if (!TryResolveConnectedGuessParticipant(
                    score.OwnerClientId,
                    null,
                    out PlayerPostItInventory inventory))
            {
                LogAuthorityError(
                    $"Connected Guess participant identity drifted. " +
                    $"ownerClientId={score.OwnerClientId}");
                return false;
            }

            preparedOwnerStates.Add(new PreparedOwnerGuessState
            {
                Inventory = inventory,
                PreviousItems = inventory.GetGuessSnapshot(),
                DesiredItems = ownerItems[score.OwnerClientId].ToArray()
            });
        }

        return true;
    }

    private bool TryGetCommittedFinalScores(
        out PostItGuessPlayerScoreData[] committedScores)
    {
        committedScores = Array.Empty<PostItGuessPlayerScoreData>();
        if (!ValidateFrozenGuessParticipantSets())
        {
            return false;
        }

        Dictionary<ulong, int> entryCounts = new Dictionary<ulong, int>();
        Dictionary<ulong, int> submittedCounts = new Dictionary<ulong, int>();
        Dictionary<ulong, int> correctCounts = new Dictionary<ulong, int>();
        foreach (ulong ownerClientId in _serverGuessScores.Keys)
        {
            entryCounts.Add(ownerClientId, 0);
            submittedCounts.Add(ownerClientId, 0);
            correctCounts.Add(ownerClientId, 0);
        }

        HashSet<int> includedPostItIds = new HashSet<int>();
        for (int entryIndex = 0; entryIndex < _serverGuessEntries.Count; entryIndex++)
        {
            ServerPostItGuessEntry entry = _serverGuessEntries[entryIndex];
            if (!entryCounts.ContainsKey(entry.OwnerClientId) ||
                !includedPostItIds.Add(entry.PostItId) ||
                entry.PostItId < 0 ||
                entry.SlotIndexAtSnapshot < 0 ||
                entry.VisualId <= 0 ||
                entry.RoundRevision != _roundRevision ||
                entry.GuessRevision != _guessRevision ||
                !IsFrozenGuessCatalogEntryValid(entry) ||
                !IsFrozenGuessParticipantIdentityValid(entry))
            {
                return false;
            }

            entryCounts[entry.OwnerClientId]++;
            if (entry.Status == PostItGuessStatus.Correct)
            {
                if (!IsGuessTopicOptionValid(entry.SelectedTopicId) ||
                    entry.SelectedTopicId != entry.CorrectTopicId ||
                    !entry.IsCorrect ||
                    !entry.BonusApplied)
                {
                    return false;
                }

                submittedCounts[entry.OwnerClientId]++;
                correctCounts[entry.OwnerClientId]++;
            }
            else if (entry.Status == PostItGuessStatus.Incorrect)
            {
                if (!IsGuessTopicOptionValid(entry.SelectedTopicId) ||
                    entry.SelectedTopicId == entry.CorrectTopicId ||
                    entry.IsCorrect ||
                    entry.BonusApplied)
                {
                    return false;
                }

                submittedCounts[entry.OwnerClientId]++;
            }
            else if (entry.Status != PostItGuessStatus.Skipped ||
                     entry.SelectedTopicId != PostItTopicId.None ||
                     entry.IsCorrect ||
                     entry.BonusApplied)
            {
                return false;
            }
        }

        List<ulong> ownerClientIds = new List<ulong>(_serverGuessScores.Keys);
        ownerClientIds.Sort();
        PostItGuessPlayerScoreData[] scores =
            new PostItGuessPlayerScoreData[ownerClientIds.Count];
        for (int ownerIndex = 0; ownerIndex < ownerClientIds.Count; ownerIndex++)
        {
            ulong ownerClientId = ownerClientIds[ownerIndex];
            PostItGuessPlayerScoreData score = _serverGuessScores[ownerClientId];
            if (!score.IsValid ||
                score.RoundRevision != _roundRevision ||
                score.GuessRevision != _guessRevision ||
                score.EligibleCount != entryCounts[ownerClientId] ||
                score.SubmittedCount != submittedCounts[ownerClientId] ||
                score.CorrectCount != correctCounts[ownerClientId] ||
                score.GuessBonusScore != correctCounts[ownerClientId] ||
                score.FinalRoundScore !=
                    (long)score.HeldPostItCount + score.CorrectCount ||
                (_serverZeroScoreGuessOwners.Contains(ownerClientId) &&
                 !HasZeroRoundScoreValues(score)))
            {
                return false;
            }

            scores[ownerIndex] = score;
        }

        committedScores = scores;
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
            !IsPlayingState() ||
            sourceInventory == null ||
            !IsValidServerInventory(sourceInventory) ||
            !ServerIsCurrentPlayingParticipant(sourceInventory))
        {
            return false;
        }

        if (sourceInventory.Count == 0)
        {
            ServerTryHandleZeroPostItEliminationSafely(sourceInventory);
            return false;
        }

        if (!IsNetworkWorldStorageActive() ||
            _isResettingWorldDrops ||
            !IsFiniteVector(authoritativePosition) ||
            (hasFallbackPosition && !IsFiniteVector(fallbackPosition)))
        {
            return false;
        }

        if (!TrySelectHighestAcquiredPostIt(sourceInventory, out PostItRuntimeData selectedData))
        {
            ServerTryHandleZeroPostItEliminationSafely(sourceInventory);
            return false;
        }

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
            ServerTryHandleZeroPostItEliminationSafely(sourceInventory);
        }
    }

    public bool ServerTryDropOnePostItForFall(
        PlayerPostItInventory sourceInventory,
        Vector3 authoritativePosition,
        bool hasFallbackPosition,
        Vector3 fallbackPosition,
        out PostItWorldDropData droppedData,
        out int remainingPostItCount)
    {
        droppedData = PostItWorldDropData.Invalid;
        remainingPostItCount = -1;

        if (!CanMutateServerState() ||
            !IsNetworkWorldStorageActive() ||
            !IsPlayingState() ||
            _isResettingWorldDrops ||
            _worldDropMutationDepth != 0 ||
            _claimedWorldDropIds.Count != 0 ||
            sourceInventory == null ||
            !IsValidServerInventory(sourceInventory) ||
            !ServerIsCurrentPlayingParticipant(sourceInventory) ||
            !IsFiniteVector(authoritativePosition) ||
            (hasFallbackPosition && !IsFiniteVector(fallbackPosition)))
        {
            return false;
        }

        int beforeCount = sourceInventory.Count;
        if (beforeCount <= 0 ||
            !TrySelectPostItForFall(sourceInventory, out PostItRuntimeData selectedData))
        {
            return false;
        }

        ulong sourceOwnerClientId = ResolveInventoryOwnerClientId(sourceInventory);
        if (!selectedData.IsValid ||
            selectedData.SlotIndex < 0 ||
            sourceOwnerClientId == ulong.MaxValue ||
            selectedData.HolderClientId != sourceOwnerClientId ||
            !sourceInventory.TryGetPostIt(
                selectedData.PostItId,
                out PostItRuntimeData currentData) ||
            !currentData.Equals(selectedData))
        {
            return false;
        }

        if (CountAuthoritativePostItLocations(selectedData.PostItId) != 1 ||
            HasFallDropWorldState(selectedData.PostItId))
        {
            LogWarning(
                $"Rejected fall drop because the selected post-it state is not unique. " +
                $"postItId={selectedData.PostItId}");
            return false;
        }

        if (!TryResolveFallWorldDropPose(
                selectedData.PostItId,
                sourceOwnerClientId,
                authoritativePosition,
                _networkWorldDrops.Count,
                out Vector3 dropPosition,
                out Quaternion dropRotation))
        {
            LogWarning(
                $"Rejected fall drop because no recoverable pose was found. " +
                $"postItId={selectedData.PostItId}");
            return false;
        }

        PostItRuntimeData expectedWorldPayload = PostItRuntimeData.Invalid;
        PostItWorldDropData expectedPublicData = PostItWorldDropData.Invalid;
        bool commitCandidate = false;

        BeginWorldDropMutation();
        try
        {
            if (!TryRemoveInventoryPostItForDrop(
                    sourceInventory,
                    selectedData,
                    out PostItRuntimeData removedData))
            {
                return false;
            }

            if (!removedData.Equals(selectedData))
            {
                if (!RollBackInventoryAdd(
                        sourceInventory,
                        removedData,
                        "fall drop selection mismatch"))
                {
                    PreserveRemovedPostItAsWorldDrop(
                        removedData,
                        dropPosition,
                        dropRotation);
                }

                LogAuthorityError(
                    $"Rejected fall drop because removed data did not match selection. " +
                    $"postItId={selectedData.PostItId}");
                return false;
            }

            expectedWorldPayload = new PostItRuntimeData(
                removedData.PostItId,
                removedData.Type,
                removedData.TopicId,
                removedData.VisualId,
                removedData.OriginalOwnerClientId,
                ulong.MaxValue,
                -1);
            expectedPublicData = new PostItWorldDropData(
                expectedWorldPayload.PostItId,
                expectedWorldPayload.Type,
                expectedWorldPayload.VisualId,
                false,
                dropPosition,
                dropRotation);

            bool publishReportedSuccess =
                TryAddWorldDrop(expectedWorldPayload, expectedPublicData);
            if (TryValidateCommittedFallDrop(
                    sourceInventory,
                    beforeCount,
                    selectedData,
                    expectedWorldPayload,
                    expectedPublicData,
                    out _))
            {
                if (!publishReportedSuccess)
                {
                    LogAuthorityError(
                        $"World drop publish reported failure after an exact fall commit. " +
                        $"Reconciling from authoritative state. postItId={selectedData.PostItId}");
                }

                commitCandidate = true;
            }
            else if (publishReportedSuccess)
            {
                LogAuthorityError(
                    $"Rejected fall drop after an incomplete reported commit. " +
                    $"postItId={selectedData.PostItId}");
                return false;
            }
            else if (HasFallDropWorldState(selectedData.PostItId))
            {
                LogAuthorityError(
                    $"Rejected fall drop after a partial world state mutation. " +
                    $"postItId={selectedData.PostItId}");
                return false;
            }
            else
            {
                bool rollbackReportedSuccess = RollBackInventoryAdd(
                    sourceInventory,
                    removedData,
                    "fall world drop publish failure");
                if (rollbackReportedSuccess)
                {
                    if (!IsFallDropRollbackStateRestored(
                            sourceInventory,
                            beforeCount,
                            selectedData))
                    {
                        LogAuthorityError(
                            $"Fall drop rollback did not restore the exact prior state. " +
                            $"postItId={selectedData.PostItId}");
                    }

                    return false;
                }

                if (sourceInventory.ContainsPostIt(selectedData.PostItId) ||
                    sourceInventory.Count != beforeCount - 1)
                {
                    LogAuthorityError(
                        $"Rejected fall drop because inventory state is uncertain after rollback failure. " +
                        $"postItId={selectedData.PostItId}");
                    return false;
                }

                bool preserveReportedSuccess =
                    PreserveWorldDropPayload(expectedWorldPayload, expectedPublicData);
                if (!TryValidateCommittedFallDrop(
                        sourceInventory,
                        beforeCount,
                        selectedData,
                        expectedWorldPayload,
                        expectedPublicData,
                        out _))
                {
                    LogAuthorityError(
                        $"Rejected fall drop because preservation did not produce an exact commit. " +
                        $"postItId={selectedData.PostItId}");
                    return false;
                }

                if (!preserveReportedSuccess)
                {
                    LogAuthorityError(
                        $"World drop preservation reported failure after an exact fall commit. " +
                        $"Reconciling from authoritative state. postItId={selectedData.PostItId}");
                }

                commitCandidate = true;
            }
        }
        finally
        {
            EndWorldDropMutation();
        }

        if (!commitCandidate ||
            !TryValidateCommittedFallDrop(
                sourceInventory,
                beforeCount,
                selectedData,
                expectedWorldPayload,
                expectedPublicData,
                out PostItWorldDropData actualPublicData))
        {
            if (commitCandidate)
            {
                LogAuthorityError(
                    $"Rejected fall drop because committed state changed during notification. " +
                    $"postItId={selectedData.PostItId}");
            }

            return false;
        }

        droppedData = actualPublicData;
        remainingPostItCount = sourceInventory.Count;
        Log(
            $"Dropped one post-it for fall. postItId={actualPublicData.PostItId}, " +
            $"slot={selectedData.SlotIndex}, remaining={remainingPostItCount}");
        return true;
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
            !ServerIsCurrentPlayingParticipant(requesterInventory) ||
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

    private bool TryCaptureWorldDropState(out PreparedWorldDrop[] capturedState)
    {
        capturedState = Array.Empty<PreparedWorldDrop>();
        if (_worldDropMutationDepth > 0 ||
            _isResettingWorldDrops ||
            _claimedWorldDropIds.Count > 0)
        {
            return false;
        }

        int publicCount;
        if (IsNetworkWorldStorageActive())
        {
            publicCount = _networkWorldDrops.Count;
        }
        else if (IsSpawnedNetworkSession())
        {
            return false;
        }
        else
        {
            publicCount = _worldDrops.Count;
        }

        if (publicCount != _worldDropPayloads.Count)
        {
            return false;
        }

        PreparedWorldDrop[] state = new PreparedWorldDrop[publicCount];
        HashSet<int> includedPostItIds = new HashSet<int>();
        for (int i = 0; i < publicCount; i++)
        {
            PostItWorldDropData publicData = IsNetworkWorldStorageActive()
                ? _networkWorldDrops[i]
                : _worldDrops[i];
            if (!includedPostItIds.Add(publicData.PostItId) ||
                !_worldDropPayloads.TryGetValue(
                    publicData.PostItId,
                    out PostItRuntimeData payload))
            {
                return false;
            }

            PreparedWorldDrop entry = new PreparedWorldDrop
            {
                Payload = payload,
                PublicData = publicData
            };
            if (!IsValidWorldDropPair(entry))
            {
                return false;
            }

            state[i] = entry;
        }

        capturedState = state;
        return true;
    }

    private bool TryWriteWorldDropState(IReadOnlyList<PreparedWorldDrop> desiredState)
    {
        if (!ValidatePreparedWorldDropState(desiredState))
        {
            return false;
        }

        if (WorldDropStateMatches(desiredState))
        {
            return true;
        }

        if (!ServerClearWorldDrops() ||
            !WorldDropStateMatches(Array.Empty<PreparedWorldDrop>()))
        {
            return false;
        }

        for (int i = 0; i < desiredState.Count; i++)
        {
            PreparedWorldDrop entry = desiredState[i];
            if (!TryAddWorldDrop(entry.Payload, entry.PublicData))
            {
                return false;
            }
        }

        return WorldDropStateMatches(desiredState);
    }

    private bool TryRollBackInitialWorldDropState(
        IReadOnlyList<PreparedWorldDrop> previousState)
    {
        try
        {
            return TryWriteWorldDropState(previousState);
        }
        catch (Exception exception)
        {
            Debug.LogException(exception, this);
            LogAuthorityError("Failed to roll back initial world Post-it state.");
            return false;
        }
    }

    private bool ValidatePreparedWorldDropState(
        IReadOnlyList<PreparedWorldDrop> state)
    {
        if (state == null)
        {
            return false;
        }

        HashSet<int> includedPostItIds = new HashSet<int>();
        for (int i = 0; i < state.Count; i++)
        {
            PreparedWorldDrop entry = state[i];
            if (!IsValidWorldDropPair(entry) ||
                !includedPostItIds.Add(entry.Payload.PostItId))
            {
                return false;
            }
        }

        return true;
    }

    private bool WorldDropStateMatches(IReadOnlyList<PreparedWorldDrop> expectedState)
    {
        if (expectedState == null ||
            _claimedWorldDropIds.Count != 0 ||
            _worldDropPayloads.Count != expectedState.Count)
        {
            return false;
        }

        bool useNetworkStorage = IsNetworkWorldStorageActive();
        if (!useNetworkStorage && IsSpawnedNetworkSession())
        {
            return false;
        }

        int publicCount = useNetworkStorage
            ? _networkWorldDrops.Count
            : _worldDrops.Count;
        if (publicCount != expectedState.Count ||
            (useNetworkStorage && _worldDrops.Count != expectedState.Count))
        {
            return false;
        }

        for (int i = 0; i < expectedState.Count; i++)
        {
            PreparedWorldDrop expected = expectedState[i];
            PostItWorldDropData publicData = useNetworkStorage
                ? _networkWorldDrops[i]
                : _worldDrops[i];
            if (!publicData.Equals(expected.PublicData) ||
                (useNetworkStorage && !_worldDrops[i].Equals(publicData)) ||
                !_worldDropPayloads.TryGetValue(
                    expected.Payload.PostItId,
                    out PostItRuntimeData payload) ||
                !payload.Equals(expected.Payload) ||
                CountAuthoritativePostItLocations(expected.Payload.PostItId) != 1)
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsValidWorldDropPair(PreparedWorldDrop entry)
    {
        return entry.Payload.IsValid &&
               entry.PublicData.IsValid &&
               entry.Payload.PostItId == entry.PublicData.PostItId &&
               entry.Payload.Type == entry.PublicData.Type &&
               entry.Payload.VisualId == entry.PublicData.VisualId &&
               entry.Payload.HolderClientId == ulong.MaxValue &&
               entry.Payload.SlotIndex == -1 &&
               !entry.PublicData.IsOriginalOwnerItem &&
               IsFiniteVector(entry.PublicData.Position) &&
               IsFiniteQuaternion(entry.PublicData.Rotation);
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

    private bool TrySelectPostItForFall(
        PlayerPostItInventory sourceInventory,
        out PostItRuntimeData selectedData)
    {
        selectedData = PostItRuntimeData.Invalid;
        if (sourceInventory == null)
            return false;

        ulong sourceOwnerClientId = ResolveInventoryOwnerClientId(sourceInventory);
        if (sourceOwnerClientId == ulong.MaxValue)
            return false;

        IReadOnlyList<PostItRuntimeData> items = sourceInventory.Items;
        bool found = false;
        bool foundAcquired = false;

        for (int i = 0; i < items.Count; i++)
        {
            PostItRuntimeData candidate = items[i];
            if (!candidate.IsValid ||
                candidate.SlotIndex < 0 ||
                candidate.HolderClientId != sourceOwnerClientId)
            {
                continue;
            }

            bool candidateIsAcquired =
                candidate.OriginalOwnerClientId != candidate.HolderClientId;
            if (foundAcquired && !candidateIsAcquired)
                continue;

            if (candidateIsAcquired && !foundAcquired)
            {
                selectedData = candidate;
                found = true;
                foundAcquired = true;
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

    private bool TryValidateCommittedFallDrop(
        PlayerPostItInventory sourceInventory,
        int beforeCount,
        PostItRuntimeData selectedData,
        PostItRuntimeData expectedWorldPayload,
        PostItWorldDropData expectedPublicData,
        out PostItWorldDropData actualPublicData)
    {
        actualPublicData = PostItWorldDropData.Invalid;
        if (sourceInventory == null ||
            beforeCount <= 0 ||
            !selectedData.IsValid ||
            !expectedWorldPayload.IsValid ||
            !expectedPublicData.IsValid ||
            selectedData.PostItId != expectedWorldPayload.PostItId ||
            expectedWorldPayload.PostItId != expectedPublicData.PostItId ||
            sourceInventory.Count != beforeCount - 1 ||
            sourceInventory.Count < 0 ||
            sourceInventory.ContainsPostIt(selectedData.PostItId) ||
            _claimedWorldDropIds.Contains(selectedData.PostItId) ||
            !_worldDropPayloads.TryGetValue(
                selectedData.PostItId,
                out PostItRuntimeData actualWorldPayload) ||
            !actualWorldPayload.Equals(expectedWorldPayload) ||
            !TryGetWorldDrop(
                selectedData.PostItId,
                out PostItWorldDropData mirroredPublicData))
        {
            return false;
        }

        int networkMarkerCount = 0;
        PostItWorldDropData networkPublicData = PostItWorldDropData.Invalid;
        for (int i = 0; i < _networkWorldDrops.Count; i++)
        {
            PostItWorldDropData candidate = _networkWorldDrops[i];
            if (candidate.PostItId != selectedData.PostItId)
                continue;

            networkMarkerCount++;
            networkPublicData = candidate;
        }

        int mirrorMarkerCount = 0;
        for (int i = 0; i < _worldDrops.Count; i++)
        {
            if (_worldDrops[i].PostItId == selectedData.PostItId)
                mirrorMarkerCount++;
        }

        if (networkMarkerCount != 1 ||
            mirrorMarkerCount != 1 ||
            !networkPublicData.Equals(expectedPublicData) ||
            !mirroredPublicData.Equals(expectedPublicData) ||
            !networkPublicData.Equals(mirroredPublicData) ||
            CountAuthoritativePostItLocations(selectedData.PostItId) != 1)
        {
            return false;
        }

        actualPublicData = mirroredPublicData;
        return true;
    }

    private bool IsFallDropRollbackStateRestored(
        PlayerPostItInventory sourceInventory,
        int beforeCount,
        PostItRuntimeData selectedData)
    {
        return sourceInventory != null &&
               beforeCount > 0 &&
               sourceInventory.Count == beforeCount &&
               sourceInventory.TryGetPostIt(
                   selectedData.PostItId,
                   out PostItRuntimeData restoredData) &&
               restoredData.Equals(selectedData) &&
               !_claimedWorldDropIds.Contains(selectedData.PostItId) &&
               !HasFallDropWorldState(selectedData.PostItId) &&
               CountAuthoritativePostItLocations(selectedData.PostItId) == 1;
    }

    private bool HasFallDropWorldState(int postItId)
    {
        return _worldDropPayloads.ContainsKey(postItId) ||
               FindNetworkWorldDropIndex(postItId) >= 0 ||
               FindWorldDropIndex(postItId) >= 0;
    }

    private bool TryResolveMapSpawnPose(
        int postItId,
        Vector3 markerPosition,
        out Vector3 position,
        out Quaternion rotation)
    {
        position = Vector3.zero;
        rotation = Quaternion.identity;
        if (!IsFiniteVector(markerPosition))
        {
            return false;
        }

        return TryProjectWorldDropToGround(
            postItId,
            markerPosition,
            markerPosition.y,
            true,
            markerPosition,
            true,
            out position,
            out rotation);
    }

    private bool TryResolveFallWorldDropPose(
        int postItId,
        ulong sourceOwnerClientId,
        Vector3 authoritativePosition,
        int authoritativeWorldDropCount,
        out Vector3 position,
        out Quaternion rotation)
    {
        position = Vector3.zero;
        rotation = Quaternion.identity;
        if (postItId < 0 ||
            sourceOwnerClientId == ulong.MaxValue ||
            !IsFiniteVector(authoritativePosition) ||
            _lastInitialAssignmentRoundRevision < 0 ||
            authoritativeWorldDropCount < 0)
        {
            return false;
        }

        if (!TryCollectCanonicalMapSpawnPoints(
                false,
                out List<PostItMapSpawnPoint> spawnPoints,
                out _))
        {
            return false;
        }

        if (spawnPoints.Count > 0)
        {
            if (!TryBuildFallMapDistributionSnapshot(
                    spawnPoints,
                    authoritativeWorldDropCount,
                    out Vector3[] worldDropPositions,
                    out int[] markerOccupancies))
            {
                return false;
            }

            uint seed = ComputeStableFallMapDropSeed(
                _lastInitialAssignmentRoundRevision,
                postItId,
                sourceOwnerClientId,
                authoritativeWorldDropCount);
            int firstMarkerIndex = (int)(seed % (uint)spawnPoints.Count);
            if (TryResolveFallMapPosePass(
                    postItId,
                    spawnPoints,
                    firstMarkerIndex,
                    seed,
                    worldDropPositions,
                    markerOccupancies,
                    true,
                    out bool foundGroundValidCandidate,
                    out position,
                    out rotation))
            {
                return true;
            }

            if (foundGroundValidCandidate)
            {
                return TryResolveFallMapPosePass(
                    postItId,
                    spawnPoints,
                    firstMarkerIndex,
                    seed,
                    worldDropPositions,
                    markerOccupancies,
                    false,
                    out _,
                    out position,
                    out rotation);
            }
        }

        return TryProjectWorldDropToGround(
            postItId,
            authoritativePosition,
            authoritativePosition.y,
            false,
            Vector3.zero,
            false,
            out position,
            out rotation);
    }

    private bool TryResolveFallMapPosePass(
        int postItId,
        IReadOnlyList<PostItMapSpawnPoint> spawnPoints,
        int firstMarkerIndex,
        uint seed,
        Vector3[] worldDropPositions,
        int[] markerOccupancies,
        bool requireWorldDropSeparation,
        out bool foundGroundValidCandidate,
        out Vector3 position,
        out Quaternion rotation)
    {
        foundGroundValidCandidate = false;
        position = Vector3.zero;
        rotation = Quaternion.identity;
        if (spawnPoints == null ||
            spawnPoints.Count == 0 ||
            firstMarkerIndex < 0 ||
            firstMarkerIndex >= spawnPoints.Count ||
            worldDropPositions == null ||
            markerOccupancies == null ||
            markerOccupancies.Length != spawnPoints.Count)
        {
            return false;
        }

        bool hasBestCandidate = false;
        int bestMarkerOccupancy = int.MaxValue;
        float bestMinimumWorldDropSqrDistance = float.NegativeInfinity;

        for (int markerOffset = 0;
             markerOffset < spawnPoints.Count;
             markerOffset++)
        {
            int markerIndex =
                (firstMarkerIndex + markerOffset) % spawnPoints.Count;
            PostItMapSpawnPoint spawnPoint = spawnPoints[markerIndex];
            if (spawnPoint == null)
                continue;

            for (int attempt = 0;
                 attempt < FallMapOffsetAttemptsPerMarker;
                 attempt++)
            {
                Vector3 markerOffsetPosition =
                    GetDeterministicFallMapMarkerOffset(
                        seed,
                        spawnPoint.SpawnOrder,
                        markerIndex,
                        attempt);
                Vector3 probePosition =
                    spawnPoint.transform.position + markerOffsetPosition;
                if (!TryResolveMapSpawnPose(
                        postItId,
                        probePosition,
                        out Vector3 candidatePosition,
                        out Quaternion candidateRotation))
                {
                    continue;
                }

                foundGroundValidCandidate = true;
                float minimumWorldDropSqrDistance =
                    GetMinimumWorldDropHorizontalSqrDistance(
                        candidatePosition,
                        worldDropPositions);
                float minimumRequiredSqrDistance =
                    FallWorldDropMinimumSeparation *
                    FallWorldDropMinimumSeparation;
                if (requireWorldDropSeparation &&
                    minimumWorldDropSqrDistance < minimumRequiredSqrDistance)
                {
                    continue;
                }

                int markerOccupancy = markerOccupancies[markerIndex];
                if (hasBestCandidate &&
                    (markerOccupancy > bestMarkerOccupancy ||
                     (markerOccupancy == bestMarkerOccupancy &&
                      minimumWorldDropSqrDistance <=
                      bestMinimumWorldDropSqrDistance)))
                {
                    continue;
                }

                hasBestCandidate = true;
                bestMarkerOccupancy = markerOccupancy;
                bestMinimumWorldDropSqrDistance =
                    minimumWorldDropSqrDistance;
                position = candidatePosition;
                rotation = candidateRotation;
            }
        }

        return hasBestCandidate;
    }

    private bool TryBuildFallMapDistributionSnapshot(
        IReadOnlyList<PostItMapSpawnPoint> spawnPoints,
        int authoritativeWorldDropCount,
        out Vector3[] worldDropPositions,
        out int[] markerOccupancies)
    {
        worldDropPositions = Array.Empty<Vector3>();
        markerOccupancies = Array.Empty<int>();
        if (spawnPoints == null ||
            spawnPoints.Count == 0 ||
            _networkWorldDrops == null ||
            authoritativeWorldDropCount < 0 ||
            _networkWorldDrops.Count != authoritativeWorldDropCount)
        {
            return false;
        }

        worldDropPositions = new Vector3[authoritativeWorldDropCount];
        markerOccupancies = new int[spawnPoints.Count];
        for (int worldDropIndex = 0;
             worldDropIndex < authoritativeWorldDropCount;
             worldDropIndex++)
        {
            PostItWorldDropData worldDrop = _networkWorldDrops[worldDropIndex];
            if (!worldDrop.IsValid ||
                !IsFiniteVector(worldDrop.Position))
            {
                return false;
            }

            worldDropPositions[worldDropIndex] = worldDrop.Position;
            int nearestMarkerIndex = -1;
            float nearestMarkerSqrDistance = float.PositiveInfinity;
            for (int markerIndex = 0;
                 markerIndex < spawnPoints.Count;
                 markerIndex++)
            {
                PostItMapSpawnPoint spawnPoint = spawnPoints[markerIndex];
                if (spawnPoint == null ||
                    !IsFiniteVector(spawnPoint.transform.position))
                {
                    return false;
                }

                float markerSqrDistance = HorizontalSqrDistance(
                    worldDrop.Position,
                    spawnPoint.transform.position);
                if (nearestMarkerIndex < 0 ||
                    markerSqrDistance < nearestMarkerSqrDistance)
                {
                    nearestMarkerIndex = markerIndex;
                    nearestMarkerSqrDistance = markerSqrDistance;
                }
            }

            if (nearestMarkerIndex < 0)
                return false;

            markerOccupancies[nearestMarkerIndex]++;
        }

        return true;
    }

    private static float GetMinimumWorldDropHorizontalSqrDistance(
        Vector3 position,
        IReadOnlyList<Vector3> worldDropPositions)
    {
        float minimumSqrDistance = float.PositiveInfinity;
        for (int worldDropIndex = 0;
             worldDropIndex < worldDropPositions.Count;
             worldDropIndex++)
        {
            minimumSqrDistance = Mathf.Min(
                minimumSqrDistance,
                HorizontalSqrDistance(
                    position,
                    worldDropPositions[worldDropIndex]));
        }

        return minimumSqrDistance;
    }

    private static Vector3 GetDeterministicFallMapMarkerOffset(
        uint seed,
        int spawnOrder,
        int markerIndex,
        int attempt)
    {
        uint angleHash = MixStableHash(seed, (uint)spawnOrder);
        angleHash = MixStableHash(angleHash, (uint)markerIndex);
        angleHash = MixStableHash(angleHash, FallMapDropAngleSalt);
        float baseAngle = HashToUnitFloat(angleHash) * Mathf.PI * 2f;
        float angle = baseAngle +
            attempt * (Mathf.PI * 2f / FallMapOffsetAttemptsPerMarker);

        uint radiusHash = MixStableHash(seed, (uint)spawnOrder);
        radiusHash = MixStableHash(radiusHash, (uint)markerIndex);
        radiusHash = MixStableHash(radiusHash, (uint)attempt);
        radiusHash = MixStableHash(radiusHash, FallMapDropRadiusSalt);
        float radius = Mathf.Lerp(
            FallMapOffsetMinimumRadius,
            FallMapOffsetMaximumRadius,
            HashToUnitFloat(radiusHash));
        return new Vector3(
            Mathf.Cos(angle) * radius,
            0f,
            Mathf.Sin(angle) * radius);
    }

    private static uint ComputeStableFallMapDropSeed(
        int roundRevision,
        int postItId,
        ulong sourceOwnerClientId,
        int authoritativeWorldDropCount)
    {
        uint hash = 2166136261u;
        hash = MixStableHash(hash, (uint)roundRevision);
        hash = MixStableHash(hash, (uint)postItId);
        hash = MixStableHash(hash, (uint)sourceOwnerClientId);
        hash = MixStableHash(hash, (uint)(sourceOwnerClientId >> 32));
        hash = MixStableHash(hash, (uint)authoritativeWorldDropCount);
        return MixStableHash(hash, FallMapDropSeedSalt);
    }

    private static uint MixStableHash(uint hash, uint value)
    {
        return unchecked((hash ^ value) * 16777619u);
    }

    private static float HashToUnitFloat(uint hash)
    {
        return (hash & 0x00ffffffu) / 16777215f;
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
            hit.collider.isTrigger ||
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

    private bool RollBackPreparedOwnerGuessStates(
        IReadOnlyList<PreparedOwnerGuessState> preparedStates,
        int publishedOwnerCount)
    {
        bool rollbackSucceeded = true;
        for (int index = publishedOwnerCount - 1; index >= 0; index--)
        {
            PreparedOwnerGuessState state = preparedStates[index];
            if (!state.Inventory.ServerReplaceGuessItems(state.PreviousItems))
            {
                rollbackSucceeded = false;
                Debug.LogError(
                    $"[{nameof(PostItRoundManager)}] Failed to roll back owner guess state. " +
                    $"ownerClientId={ResolveInventoryOwnerClientId(state.Inventory)}",
                    this);
            }
        }

        return rollbackSucceeded;
    }

    private void ClearServerGuessSnapshotState()
    {
        _serverGuessEntries.Clear();
        _serverGuessScores.Clear();
        _serverParticipantPlayerObjectIds.Clear();
        _serverDisconnectedGuessOwners.Clear();
        _serverZeroScoreGuessOwners.Clear();
        _roundRevision = -1;
        _guessRevision = -1;
        _guessDeadlineServerTime = 0d;
        _guessSubmissionOpen = false;
        _finalizedGuessRevision = -1;
    }

    private bool HasPendingGuessEntries()
    {
        for (int entryIndex = 0; entryIndex < _serverGuessEntries.Count; entryIndex++)
        {
            if (_serverGuessEntries[entryIndex].Status == PostItGuessStatus.Pending)
            {
                return true;
            }
        }

        return false;
    }

    private bool IsFrozenGuessParticipantIdentityValid(
        ServerPostItGuessEntry entry)
    {
        if (_serverParticipantPlayerObjectIds.TryGetValue(
                entry.OwnerClientId,
                out ulong participantPlayerObjectId))
        {
            return !_serverDisconnectedGuessOwners.Contains(entry.OwnerClientId) &&
                   participantPlayerObjectId == entry.PlayerNetworkObjectId;
        }

        return _serverDisconnectedGuessOwners.Contains(entry.OwnerClientId);
    }

    private bool IsFrozenGuessCatalogEntryValid(ServerPostItGuessEntry entry)
    {
        return visualCatalog != null &&
               visualCatalog.TryGetEntryByVisualId(
                   entry.VisualId,
                   out PostItVisualCatalogSO.Entry catalogEntry) &&
               catalogEntry.Type == PostItType.Drawing &&
               catalogEntry.TopicId == entry.CorrectTopicId;
    }

    private bool IsGuessTopicOptionValid(PostItTopicId topicId)
    {
        return PostItVisualCatalogSO.IsSupportedDrawingTopic(topicId) &&
               visualCatalog != null &&
               visualCatalog.TryGetDrawingEntry(topicId, out _);
    }

    private bool ValidateFrozenGuessParticipantSets()
    {
        if (_serverParticipantPlayerObjectIds.Count +
                _serverDisconnectedGuessOwners.Count +
                _serverZeroScoreGuessOwners.Count !=
            _serverGuessScores.Count)
        {
            return false;
        }

        foreach (ulong ownerClientId in _serverParticipantPlayerObjectIds.Keys)
        {
            if (_serverDisconnectedGuessOwners.Contains(ownerClientId) ||
                _serverZeroScoreGuessOwners.Contains(ownerClientId) ||
                !_serverGuessScores.ContainsKey(ownerClientId))
            {
                return false;
            }
        }

        foreach (ulong ownerClientId in _serverDisconnectedGuessOwners)
        {
            if (_serverZeroScoreGuessOwners.Contains(ownerClientId) ||
                !_serverGuessScores.ContainsKey(ownerClientId))
            {
                return false;
            }
        }

        foreach (ulong ownerClientId in _serverZeroScoreGuessOwners)
        {
            if (_serverParticipantPlayerObjectIds.ContainsKey(ownerClientId) ||
                _serverDisconnectedGuessOwners.Contains(ownerClientId) ||
                !_serverGuessScores.TryGetValue(
                    ownerClientId,
                    out PostItGuessPlayerScoreData zeroScore) ||
                !HasZeroRoundScoreValues(zeroScore))
            {
                return false;
            }
        }

        return true;
    }

    private static bool HasZeroRoundScoreValues(
        PostItGuessPlayerScoreData score)
    {
        return score.HeldPostItCount == 0 &&
               score.EligibleCount == 0 &&
               score.SubmittedCount == 0 &&
               score.CorrectCount == 0 &&
               score.GuessBonusScore == 0 &&
               score.FinalRoundScore == 0;
    }

    private int FindUniqueServerGuessEntryIndex(ulong ownerClientId, int postItId)
    {
        int foundIndex = -1;
        for (int entryIndex = 0; entryIndex < _serverGuessEntries.Count; entryIndex++)
        {
            ServerPostItGuessEntry entry = _serverGuessEntries[entryIndex];
            if (entry.OwnerClientId != ownerClientId || entry.PostItId != postItId)
            {
                continue;
            }

            if (foundIndex >= 0)
            {
                LogAuthorityError(
                    $"Duplicate frozen Guess entry. ownerClientId={ownerClientId}, " +
                    $"postItId={postItId}");
                return -1;
            }

            foundIndex = entryIndex;
        }

        return foundIndex;
    }

    private bool TryResolveConnectedGuessParticipant(
        ulong ownerClientId,
        PlayerPostItInventory requesterInventory,
        out PlayerPostItInventory resolvedInventory)
    {
        resolvedInventory = null;
        if (NetworkManager == null ||
            !NetworkManager.IsListening ||
            !NetworkManager.ConnectedClients.TryGetValue(
                ownerClientId,
                out NetworkClient client) ||
            client == null ||
            client.PlayerObject == null ||
            !client.PlayerObject.IsSpawned ||
            client.PlayerObject.OwnerClientId != ownerClientId ||
            !_serverParticipantPlayerObjectIds.TryGetValue(
                ownerClientId,
                out ulong expectedPlayerObjectId) ||
            client.PlayerObject.NetworkObjectId != expectedPlayerObjectId)
        {
            return false;
        }

        PlayerPostItInventory inventory =
            client.PlayerObject.GetComponent<PlayerPostItInventory>();
        if (!IsValidServerInventory(inventory) ||
            ResolveInventoryOwnerClientId(inventory) != ownerClientId ||
            ResolveInventoryNetworkObjectId(inventory) != expectedPlayerObjectId ||
            (requesterInventory != null && requesterInventory != inventory))
        {
            return false;
        }

        resolvedInventory = inventory;
        return true;
    }

    private bool TryGetGuessClientPlayerObjectState(
        ulong ownerClientId,
        out bool hasSpawnedPlayerObject)
    {
        hasSpawnedPlayerObject = false;
        if (NetworkManager == null || !NetworkManager.IsListening)
            return false;

        if (!NetworkManager.ConnectedClients.TryGetValue(
                ownerClientId,
                out NetworkClient client))
        {
            return true;
        }

        if (client == null)
            return false;

        NetworkObject playerObject = client.PlayerObject;
        if (playerObject == null || !playerObject.IsSpawned)
            return false;

        hasSpawnedPlayerObject = true;
        return true;
    }

    private double GetAuthoritativeServerTime()
    {
        if (NetworkManager != null && NetworkManager.IsListening)
        {
            return NetworkManager.ServerTime.Time;
        }

        return Time.unscaledTimeAsDouble;
    }

    private int ComparePreparedOwnerGuessStatesByOwnerClientId(
        PreparedOwnerGuessState left,
        PreparedOwnerGuessState right)
    {
        return ResolveInventoryOwnerClientId(left.Inventory).CompareTo(
            ResolveInventoryOwnerClientId(right.Inventory));
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

    private bool TryCollectCanonicalMapSpawnPoints(
        bool requireUniqueSpawnOrders,
        out List<PostItMapSpawnPoint> spawnPoints,
        out string validationError)
    {
        spawnPoints = new List<PostItMapSpawnPoint>();
        validationError = null;
        PostItMapSpawnPoint[] sceneSpawnPoints =
            FindObjectsByType<PostItMapSpawnPoint>(FindObjectsSortMode.None);
        HashSet<int> spawnOrders = requireUniqueSpawnOrders
            ? new HashSet<int>()
            : null;
        for (int spawnPointIndex = 0;
             spawnPointIndex < sceneSpawnPoints.Length;
             spawnPointIndex++)
        {
            PostItMapSpawnPoint spawnPoint = sceneSpawnPoints[spawnPointIndex];
            if (spawnPoint == null ||
                !spawnPoint.isActiveAndEnabled ||
                !spawnPoint.gameObject.scene.IsValid() ||
                !spawnPoint.gameObject.scene.isLoaded ||
                spawnPoint.gameObject.scene != gameObject.scene)
            {
                continue;
            }

            if (spawnPoint.SpawnOrder < 0 ||
                !IsFiniteVector(spawnPoint.transform.position))
            {
                if (requireUniqueSpawnOrders)
                {
                    validationError =
                        "Map spawn points require finite positions and unique non-negative SpawnOrder values.";
                    return false;
                }

                continue;
            }

            if (requireUniqueSpawnOrders &&
                !spawnOrders.Add(spawnPoint.SpawnOrder))
            {
                validationError =
                    "Map spawn points require finite positions and unique non-negative SpawnOrder values.";
                return false;
            }

            spawnPoints.Add(spawnPoint);
        }

        spawnPoints.Sort(CompareMapSpawnPointsByOrder);
        return true;
    }

    private bool TryPrepareInitialMapWorldDrops(
        int roundRevision,
        IReadOnlyList<PostItVisualCatalogSO.Entry> drawingEntries,
        IReadOnlyList<PostItVisualCatalogSO.Entry> effectEntries,
        HashSet<int> reservedPostItIds,
        ref int nextPostItId,
        out PreparedWorldDrop[] desiredWorldDrops,
        out string validationError)
    {
        desiredWorldDrops = Array.Empty<PreparedWorldDrop>();
        validationError = null;
        if (drawingEntries == null ||
            effectEntries == null ||
            reservedPostItIds == null ||
            roundRevision < 0)
        {
            validationError = "Map spawn preparation received invalid input.";
            return false;
        }

        List<PostItVisualCatalogSO.Entry> bonusEntries =
            new List<PostItVisualCatalogSO.Entry>();
        List<PostItVisualCatalogSO.Entry> penaltyEntries =
            new List<PostItVisualCatalogSO.Entry>();
        for (int entryIndex = 0; entryIndex < effectEntries.Count; entryIndex++)
        {
            PostItVisualCatalogSO.Entry entry = effectEntries[entryIndex];
            if (entry.Type == PostItType.Bonus)
            {
                bonusEntries.Add(entry);
            }
            else if (entry.Type == PostItType.Penalty)
            {
                penaltyEntries.Add(entry);
            }
        }

        if (drawingEntries.Count == 0 ||
            bonusEntries.Count == 0 ||
            penaltyEntries.Count == 0)
        {
            validationError =
                "The visual catalog requires Drawing, Bonus (Guard), and Penalty (Heavy) entries.";
            return false;
        }

        if (!TryCollectCanonicalMapSpawnPoints(
                true,
                out List<PostItMapSpawnPoint> spawnPoints,
                out validationError))
        {
            return false;
        }

        if (spawnPoints.Count < InitialMapPostItCount)
        {
            validationError =
                $"At least {InitialMapPostItCount} active map spawn points are required.";
            return false;
        }

        PostItMapSpawnPoint[] orderedSpawnPoints =
            BuildDeterministicMapSpawnPointOrder(spawnPoints, roundRevision);

        PostItVisualCatalogSO.Entry[] selectedEntries =
            new PostItVisualCatalogSO.Entry[InitialMapPostItCount];
        int drawingStartIndex = (int)(ComputeStableAssignmentHash(
            roundRevision,
            ulong.MaxValue,
            0,
            0x4D415044u) % (uint)drawingEntries.Count);
        for (int drawingIndex = 0;
             drawingIndex < InitialMapDrawingPostItCount;
             drawingIndex++)
        {
            selectedEntries[drawingIndex] =
                drawingEntries[(drawingStartIndex + drawingIndex) % drawingEntries.Count];
        }

        int bonusEntryIndex = (int)(ComputeStableAssignmentHash(
            roundRevision,
            ulong.MaxValue,
            0,
            0x4D415047u) % (uint)bonusEntries.Count);
        selectedEntries[InitialMapDrawingPostItCount] = bonusEntries[bonusEntryIndex];

        int penaltyEntryIndex = (int)(ComputeStableAssignmentHash(
            roundRevision,
            ulong.MaxValue,
            0,
            0x4D415048u) % (uint)penaltyEntries.Count);
        selectedEntries[InitialMapDrawingPostItCount + InitialMapBonusPostItCount] =
            penaltyEntries[penaltyEntryIndex];

        int[] postItIds = new int[InitialMapPostItCount];
        for (int itemIndex = 0; itemIndex < postItIds.Length; itemIndex++)
        {
            if (!TryReserveNextPostItId(
                    reservedPostItIds,
                    ref nextPostItId,
                    out postItIds[itemIndex]))
            {
                validationError = "Map PostItId space is exhausted.";
                return false;
            }
        }

        PreparedWorldDrop[] preparedWorldDrops =
            new PreparedWorldDrop[InitialMapPostItCount];
        int candidateIndex = 0;
        for (int itemIndex = 0; itemIndex < preparedWorldDrops.Length; itemIndex++)
        {
            bool prepared = false;
            while (candidateIndex < orderedSpawnPoints.Length)
            {
                PostItMapSpawnPoint spawnPoint = orderedSpawnPoints[candidateIndex++];
                if (!TryResolveMapSpawnPose(
                        postItIds[itemIndex],
                        spawnPoint.transform.position,
                        out Vector3 position,
                        out Quaternion rotation) ||
                    !IsSeparatedFromPreparedMapSpawns(
                        position,
                        preparedWorldDrops,
                        itemIndex))
                {
                    continue;
                }

                PostItVisualCatalogSO.Entry entry = selectedEntries[itemIndex];
                PreparedWorldDrop worldDrop = new PreparedWorldDrop
                {
                    Payload = new PostItRuntimeData(
                        postItIds[itemIndex],
                        entry.Type,
                        entry.TopicId,
                        entry.VisualId,
                        ulong.MaxValue,
                        ulong.MaxValue,
                        -1),
                    PublicData = new PostItWorldDropData(
                        postItIds[itemIndex],
                        entry.Type,
                        entry.VisualId,
                        false,
                        position,
                        rotation)
                };
                if (!IsValidWorldDropPair(worldDrop))
                {
                    validationError = "A prepared map Post-it did not satisfy the world data contract.";
                    return false;
                }

                preparedWorldDrops[itemIndex] = worldDrop;
                prepared = true;
                break;
            }

            if (!prepared)
            {
                validationError =
                    $"Could not project {InitialMapPostItCount} separated map Post-its onto valid ground.";
                return false;
            }
        }

        desiredWorldDrops = preparedWorldDrops;
        return true;
    }

    private static PostItMapSpawnPoint[] BuildDeterministicMapSpawnPointOrder(
        IReadOnlyList<PostItMapSpawnPoint> spawnPoints,
        int roundRevision)
    {
        PostItMapSpawnPoint[] ordered = new PostItMapSpawnPoint[spawnPoints.Count];
        bool[] selected = new bool[spawnPoints.Count];
        int firstIndex = (int)(ComputeStableAssignmentHash(
            roundRevision,
            ulong.MaxValue,
            0,
            0x4D415050u) % (uint)spawnPoints.Count);
        ordered[0] = spawnPoints[firstIndex];
        selected[firstIndex] = true;

        for (int orderedIndex = 1;
             orderedIndex < ordered.Length;
             orderedIndex++)
        {
            int bestCandidateIndex = -1;
            float bestMinimumDistance = -1f;
            for (int candidateIndex = 0;
                 candidateIndex < spawnPoints.Count;
                 candidateIndex++)
            {
                if (selected[candidateIndex])
                {
                    continue;
                }

                float minimumDistance = float.PositiveInfinity;
                for (int selectedIndex = 0;
                     selectedIndex < orderedIndex;
                     selectedIndex++)
                {
                    minimumDistance = Mathf.Min(
                        minimumDistance,
                        HorizontalSqrDistance(
                            spawnPoints[candidateIndex].transform.position,
                            ordered[selectedIndex].transform.position));
                }

                if (bestCandidateIndex < 0 ||
                    minimumDistance > bestMinimumDistance)
                {
                    bestCandidateIndex = candidateIndex;
                    bestMinimumDistance = minimumDistance;
                }
            }

            ordered[orderedIndex] = spawnPoints[bestCandidateIndex];
            selected[bestCandidateIndex] = true;
        }

        return ordered;
    }

    private static bool IsSeparatedFromPreparedMapSpawns(
        Vector3 position,
        IReadOnlyList<PreparedWorldDrop> preparedWorldDrops,
        int preparedCount)
    {
        float minimumSqrDistance =
            MinimumMapSpawnSeparation * MinimumMapSpawnSeparation;
        for (int i = 0; i < preparedCount; i++)
        {
            if (HorizontalSqrDistance(
                    position,
                    preparedWorldDrops[i].PublicData.Position) < minimumSqrDistance)
            {
                return false;
            }
        }

        return true;
    }

    private static float HorizontalSqrDistance(Vector3 left, Vector3 right)
    {
        float deltaX = left.x - right.x;
        float deltaZ = left.z - right.z;
        return deltaX * deltaX + deltaZ * deltaZ;
    }

    private static int CompareMapSpawnPointsByOrder(
        PostItMapSpawnPoint left,
        PostItMapSpawnPoint right)
    {
        int orderComparison = left.SpawnOrder.CompareTo(right.SpawnOrder);
        return orderComparison != 0
            ? orderComparison
            : CompareTransformHierarchy(
                left != null ? left.transform : null,
                right != null ? right.transform : null);
    }

    private static int CompareTransformHierarchy(
        Transform left,
        Transform right)
    {
        if (ReferenceEquals(left, right))
            return 0;
        if (left == null)
            return -1;
        if (right == null)
            return 1;

        int leftDepth = GetTransformHierarchyDepth(left);
        int rightDepth = GetTransformHierarchyDepth(right);
        int leftOriginalDepth = leftDepth;
        int rightOriginalDepth = rightDepth;
        Transform leftCursor = left;
        Transform rightCursor = right;
        while (leftDepth > rightDepth)
        {
            leftCursor = leftCursor.parent;
            leftDepth--;
        }

        while (rightDepth > leftDepth)
        {
            rightCursor = rightCursor.parent;
            rightDepth--;
        }

        if (leftCursor == rightCursor)
            return leftOriginalDepth.CompareTo(rightOriginalDepth);

        while (leftCursor.parent != rightCursor.parent)
        {
            leftCursor = leftCursor.parent;
            rightCursor = rightCursor.parent;
        }

        int siblingComparison =
            leftCursor.GetSiblingIndex().CompareTo(rightCursor.GetSiblingIndex());
        return siblingComparison != 0
            ? siblingComparison
            : string.CompareOrdinal(leftCursor.name, rightCursor.name);
    }

    private static int GetTransformHierarchyDepth(Transform transform)
    {
        int depth = 0;
        for (Transform current = transform;
             current != null;
             current = current.parent)
        {
            depth++;
        }

        return depth;
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

    private bool RollBackInitialAssignments(
        IReadOnlyList<PreparedInitialAssignment> assignments,
        int mutatedAssignmentCount)
    {
        bool rollbackSucceeded = true;
        for (int assignmentIndex = mutatedAssignmentCount - 1;
             assignmentIndex >= 0;
             assignmentIndex--)
        {
            PreparedInitialAssignment assignment = assignments[assignmentIndex];
            try
            {
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
                    if (!assignment.Inventory.ServerTryAddPostIt(
                            previousItem,
                            out PostItRuntimeData restoredItem) ||
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
            catch (Exception exception)
            {
                Debug.LogException(exception, this);
                rollbackSucceeded = false;
            }
        }

        if (!rollbackSucceeded)
        {
            Debug.LogError(
                $"[{nameof(PostItRoundManager)}] Failed to roll back initial Post-it assignment.",
                this);
        }

        return rollbackSucceeded;
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

    private bool HasListeningNetworkSession()
    {
        return NetworkManager != null && NetworkManager.IsListening;
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

    private GameStateManager ResolveGameStateManager()
    {
        if (_gameStateManager == null || !_gameStateManager.isActiveAndEnabled)
            _gameStateManager = FindFirstObjectByType<GameStateManager>();

        return _gameStateManager;
    }

    private bool IsPlayingState()
    {
        GameStateManager manager = ResolveGameStateManager();
        return manager != null && manager.GetState() == GameStateManager.GameState.Playing;
    }

    private bool IsInitialAssignmentState()
    {
        GameStateManager manager = ResolveGameStateManager();
        if (manager == null)
            return false;

        GameStateManager.GameState state = manager.GetState();
        return state == GameStateManager.GameState.Lobby ||
               state == GameStateManager.GameState.Countdown;
    }

    private bool IsGuessingState()
    {
        GameStateManager manager = ResolveGameStateManager();
        return manager != null && manager.GetState() == GameStateManager.GameState.Guessing;
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
