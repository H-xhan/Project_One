using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public class PlayerPostItInventory : NetworkBehaviour
{
    [SerializeField] private int maxPostItSlots = 6;
    [SerializeField] private bool debugLogs = false;

    private readonly List<PostItRuntimeData> _postIts = new List<PostItRuntimeData>();
    private readonly List<PostItPublicVisualData> _publicVisuals = new List<PostItPublicVisualData>();
    private readonly List<PostItGuessOwnerData> _guessItems = new List<PostItGuessOwnerData>();
    private NetworkList<PostItRuntimeData> _networkPostIts;
    private NetworkList<PostItPublicVisualData> _networkPublicVisuals;
    private NetworkList<PostItGuessOwnerData> _networkGuessItems;
    private bool _hasSubscribedToNetworkPostIts;
    private bool _hasSubscribedToNetworkPublicVisuals;
    private bool _hasSubscribedToNetworkGuessItems;

    public event Action PostItsChanged;
    public event Action PublicVisualsChanged;
    public event Action GuessItemsChanged;

    private void Awake()
    {
        _networkPostIts = new NetworkList<PostItRuntimeData>(
            values: null,
            readPerm: NetworkVariableReadPermission.Owner,
            writePerm: NetworkVariableWritePermission.Server);

        _networkPublicVisuals = new NetworkList<PostItPublicVisualData>(
            values: null,
            readPerm: NetworkVariableReadPermission.Everyone,
            writePerm: NetworkVariableWritePermission.Server);

        _networkGuessItems = new NetworkList<PostItGuessOwnerData>(
            values: null,
            readPerm: NetworkVariableReadPermission.Owner,
            writePerm: NetworkVariableWritePermission.Server);
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        if (_networkPostIts == null)
        {
            LogWarning("Network post-it storage is unavailable on spawn.");
            return;
        }

        bool hasPublicNetworkStorage = _networkPublicVisuals != null;
        if (!hasPublicNetworkStorage)
        {
            LogWarning("Public visual network storage is unavailable on spawn.");
        }

        bool hasGuessNetworkStorage = _networkGuessItems != null;
        if (!hasGuessNetworkStorage)
        {
            LogWarning("Guess network storage is unavailable on spawn.");
        }

        if (IsServer && _networkPostIts.Count == 0 && _postIts.Count > 0)
        {
            PostItRuntimeData[] fallbackSnapshot = _postIts.ToArray();
            for (int i = 0; i < fallbackSnapshot.Length; i++)
            {
                PostItRuntimeData data = fallbackSnapshot[i];
                if (NetworkListContainsPostIt(data.PostItId))
                {
                    LogWarning($"Skipped duplicate post-it during spawn migration. postItId={data.PostItId}");
                    continue;
                }

                if (!AddToAuthoritativeStorage(data))
                {
                    LogWarning($"Failed to migrate post-it into network storage. postItId={data.PostItId}");
                }
            }
        }

        if (IsServer && hasPublicNetworkStorage)
        {
            ReconcileServerPublicVisualsFromPrivate();
        }

        SubscribeToNetworkPostIts();
        if (hasPublicNetworkStorage)
        {
            SubscribeToNetworkPublicVisuals();
        }

        if (hasGuessNetworkStorage)
        {
            SubscribeToNetworkGuessItems();
        }

        RebuildLocalMirrorFromNetworkList();
        if (hasPublicNetworkStorage)
        {
            RebuildLocalPublicVisualMirror();
        }
        else
        {
            _publicVisuals.Clear();
        }

        if (hasGuessNetworkStorage)
        {
            RebuildLocalGuessMirror();
        }
        else
        {
            _guessItems.Clear();
        }

        NotifyPostItsChanged();
        NotifyPublicVisualsChanged();
        NotifyGuessItemsChanged();
    }

    public override void OnNetworkDespawn()
    {
        UnsubscribeFromNetworkPostIts();
        UnsubscribeFromNetworkPublicVisuals();
        UnsubscribeFromNetworkGuessItems();
        base.OnNetworkDespawn();
    }

    public int Count => _postIts.Count;
    public int Capacity => Mathf.Max(0, maxPostItSlots);
    public bool IsFull => Count >= Capacity;
    public IReadOnlyList<PostItRuntimeData> Items => _postIts;
    public int PublicVisualCount => _publicVisuals.Count;
    public IReadOnlyList<PostItPublicVisualData> PublicVisualItems => _publicVisuals;
    public int GuessItemCount => _guessItems.Count;
    public IReadOnlyList<PostItGuessOwnerData> GuessItems => _guessItems;

    public PostItRuntimeData[] GetSnapshot()
    {
        return _postIts.ToArray();
    }

    public PostItGuessOwnerData[] GetGuessSnapshot()
    {
        return _guessItems.ToArray();
    }

    public bool TryGetGuessItem(int postItId, out PostItGuessOwnerData data)
    {
        for (int i = 0; i < _guessItems.Count; i++)
        {
            if (_guessItems[i].PostItId == postItId)
            {
                data = _guessItems[i];
                return true;
            }
        }

        data = PostItGuessOwnerData.Invalid;
        return false;
    }

    public void RequestSubmitPostItGuess(
        int roundRevision,
        int guessRevision,
        int postItId,
        PostItTopicId selectedTopicId)
    {
        if (!IsOwner || !IsSpawnedNetworkSession())
        {
            return;
        }

        RequestSubmitPostItGuessServerRpc(
            roundRevision,
            guessRevision,
            postItId,
            selectedTopicId);
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
    private void RequestSubmitPostItGuessServerRpc(
        int roundRevision,
        int guessRevision,
        int postItId,
        PostItTopicId selectedTopicId,
        RpcParams rpcParams = default)
    {
        if (!IsServer || !IsSpawnedNetworkSession())
        {
            return;
        }

        ulong senderClientId = rpcParams.Receive.SenderClientId;
        if (senderClientId != OwnerClientId)
        {
            return;
        }

        PostItRoundManager roundManager = FindFirstObjectByType<PostItRoundManager>();
        if (roundManager == null || !roundManager.IsSpawned || !roundManager.IsServer)
        {
            return;
        }

        roundManager.ServerTrySubmitGuess(
            this,
            senderClientId,
            roundRevision,
            guessRevision,
            postItId,
            selectedTopicId,
            out _);
    }

    public bool ContainsPostIt(int postItId)
    {
        for (int i = 0; i < _postIts.Count; i++)
        {
            if (_postIts[i].PostItId == postItId)
            {
                return true;
            }
        }

        return false;
    }

    public bool TryGetPostIt(int postItId, out PostItRuntimeData data)
    {
        for (int i = 0; i < _postIts.Count; i++)
        {
            if (_postIts[i].PostItId == postItId)
            {
                data = _postIts[i];
                return true;
            }
        }

        data = PostItRuntimeData.Invalid;
        return false;
    }

    public bool TryGetPostItAtSlot(int slotIndex, out PostItRuntimeData data)
    {
        for (int i = 0; i < _postIts.Count; i++)
        {
            if (_postIts[i].SlotIndex == slotIndex)
            {
                data = _postIts[i];
                return true;
            }
        }

        data = PostItRuntimeData.Invalid;
        return false;
    }

    public bool TryGetPublicVisualAtSlot(int slotIndex, out PostItPublicVisualData data)
    {
        if (slotIndex < 0)
        {
            data = PostItPublicVisualData.Invalid;
            return false;
        }

        for (int i = 0; i < _publicVisuals.Count; i++)
        {
            if (_publicVisuals[i].SlotIndex == slotIndex)
            {
                data = _publicVisuals[i];
                return true;
            }
        }

        data = PostItPublicVisualData.Invalid;
        return false;
    }

    public int FindFirstFreeSlot()
    {
        int capacity = Capacity;
        for (int slotIndex = 0; slotIndex < capacity; slotIndex++)
        {
            if (!IsSlotOccupied(slotIndex))
            {
                return slotIndex;
            }
        }

        return -1;
    }

    public void ServerClearPostIts()
    {
        if (!CanMutateServerState())
        {
            LogWarning("Blocked post-it clear on non-server instance.");
            return;
        }

        ClearAuthoritativeStorage();
    }

    public bool ServerReplaceGuessItems(IReadOnlyList<PostItGuessOwnerData> guessItems)
    {
        if (!CanMutateServerState())
        {
            LogWarning("Blocked guess item replace on non-server instance.");
            return false;
        }

        if (!ValidateGuessReplacement(guessItems))
        {
            LogWarning("Rejected invalid guess item replacement.");
            return false;
        }

        PostItGuessOwnerData[] desiredItems = new PostItGuessOwnerData[guessItems.Count];
        for (int i = 0; i < guessItems.Count; i++)
        {
            desiredItems[i] = guessItems[i];
        }

        if (IsGuessNetworkStorageActive())
        {
            if (NetworkGuessItemsMatch(desiredItems))
            {
                return true;
            }

            return ReplaceNetworkGuessItemsWithRollback(desiredItems);
        }

        if (IsSpawnedNetworkSession())
        {
            LogWarning("Cannot replace guess items because network storage is unavailable.");
            return false;
        }

        if (LocalGuessItemsMatch(desiredItems))
        {
            return true;
        }

        _guessItems.Clear();
        for (int i = 0; i < desiredItems.Length; i++)
        {
            _guessItems.Add(desiredItems[i]);
        }

        NotifyGuessItemsChanged();
        return true;
    }

    public bool ServerTryUpdateGuessItem(PostItGuessOwnerData data)
    {
        if (!CanMutateServerState())
        {
            LogWarning("Blocked guess item update on non-server instance.");
            return false;
        }

        if (!data.IsValid)
        {
            LogWarning($"Rejected invalid guess item update. postItId={data.PostItId}");
            return false;
        }

        int index = FindGuessItemIndex(data.PostItId);
        if (index < 0)
        {
            return false;
        }

        PostItGuessOwnerData current = _guessItems[index];
        if (current.RoundRevision != data.RoundRevision ||
            current.GuessRevision != data.GuessRevision ||
            current.VisualId != data.VisualId)
        {
            LogWarning($"Rejected stale guess item update. postItId={data.PostItId}");
            return false;
        }

        if (IsGuessNetworkStorageActive())
        {
            if (index >= _networkGuessItems.Count ||
                !_networkGuessItems[index].Equals(current))
            {
                LogWarning($"Rejected guess update because mirror and network storage differ. postItId={data.PostItId}");
                return false;
            }

            if (current.Equals(data))
            {
                return true;
            }

            try
            {
                _networkGuessItems[index] = data;
            }
            catch (Exception exception)
            {
                LogWarning($"Guess item update threw an exception. message={exception.Message}");
            }

            if (index < _networkGuessItems.Count && _networkGuessItems[index].Equals(data))
            {
                return true;
            }

            try
            {
                if (index < _networkGuessItems.Count)
                {
                    _networkGuessItems[index] = current;
                }
            }
            catch (Exception exception)
            {
                LogWarning($"Guess item update rollback threw an exception. message={exception.Message}");
            }

            if (index >= _networkGuessItems.Count || !_networkGuessItems[index].Equals(current))
            {
                Debug.LogError(
                    $"[{nameof(PlayerPostItInventory)}] Failed to roll back guess item update. " +
                    $"postItId={data.PostItId}",
                    this);
            }

            return false;
        }

        if (IsSpawnedNetworkSession())
        {
            LogWarning($"Cannot update guess item because network storage is unavailable. postItId={data.PostItId}");
            return false;
        }

        _guessItems[index] = data;
        NotifyGuessItemsChanged();
        return true;
    }

    public bool ServerClearGuessItems()
    {
        return ServerReplaceGuessItems(Array.Empty<PostItGuessOwnerData>());
    }

    public bool ServerTryAddPostIt(PostItRuntimeData data, out PostItRuntimeData assignedData)
    {
        assignedData = PostItRuntimeData.Invalid;

        if (!CanMutateServerState())
        {
            LogWarning("Blocked post-it add on non-server instance.");
            return false;
        }

        if (!data.IsValid || data.Type == PostItType.None)
        {
            LogWarning($"Rejected invalid post-it data. postItId={data.PostItId}, type={data.Type}");
            return false;
        }

        if (ContainsPostIt(data.PostItId))
        {
            LogWarning($"Rejected duplicate post-it id. postItId={data.PostItId}");
            return false;
        }

        if (IsFull)
        {
            LogWarning($"Rejected post-it add because inventory is full. postItId={data.PostItId}");
            return false;
        }

        int slotIndex = data.SlotIndex;
        if (slotIndex < 0 || IsSlotOccupied(slotIndex))
        {
            slotIndex = FindFirstFreeSlot();
        }

        if (slotIndex < 0)
        {
            LogWarning($"Rejected post-it add because no free slot was found. postItId={data.PostItId}");
            return false;
        }

        ulong holderClientId = data.HolderClientId;
        if (TryResolveOwnerClientId(out ulong ownerClientId))
        {
            holderClientId = ownerClientId;
        }

        assignedData = new PostItRuntimeData(
            data.PostItId,
            data.Type,
            data.TopicId,
            data.VisualId,
            data.OriginalOwnerClientId,
            holderClientId,
            slotIndex);

        if (!AddToAuthoritativeStorage(assignedData))
        {
            assignedData = PostItRuntimeData.Invalid;
            return false;
        }

        Log($"Added post-it. postItId={assignedData.PostItId}, slot={assignedData.SlotIndex}");
        return true;
    }

    public bool ServerTryRemovePostIt(int postItId, out PostItRuntimeData removedData)
    {
        removedData = PostItRuntimeData.Invalid;

        if (!CanMutateServerState())
        {
            LogWarning("Blocked post-it remove on non-server instance.");
            return false;
        }

        for (int i = 0; i < _postIts.Count; i++)
        {
            if (_postIts[i].PostItId != postItId)
            {
                continue;
            }

            PostItRuntimeData candidate = _postIts[i];
            if (!RemoveAtAuthoritativeStorage(i, candidate.PostItId))
            {
                return false;
            }

            removedData = candidate;
            Log($"Removed post-it. postItId={removedData.PostItId}");
            return true;
        }

        return false;
    }

    public bool ServerTryTransferPostItTo(
        PlayerPostItInventory targetInventory,
        int postItId,
        out PostItRuntimeData transferredData)
    {
        transferredData = PostItRuntimeData.Invalid;

        if (!CanMutateServerState())
        {
            LogWarning("Blocked post-it transfer on non-server instance.");
            return false;
        }

        if (targetInventory == null)
        {
            LogWarning($"Rejected post-it transfer because target inventory is null. postItId={postItId}");
            return false;
        }

        if (!ServerTryRemovePostIt(postItId, out PostItRuntimeData removedData))
        {
            return false;
        }

        if (targetInventory.ServerTryAddPostIt(removedData, out transferredData))
        {
            Log($"Transferred post-it. postItId={transferredData.PostItId}");
            return true;
        }

        if (!ServerTryAddPostIt(removedData, out _))
        {
            LogWarning($"Failed to roll back post-it transfer. postItId={removedData.PostItId}");
        }

        transferredData = PostItRuntimeData.Invalid;
        return false;
    }

    private bool IsNetworkStorageActive()
    {
        return _networkPostIts != null &&
               NetworkManager != null &&
               NetworkManager.IsListening &&
               IsSpawned;
    }

    private bool IsGuessNetworkStorageActive()
    {
        return _networkGuessItems != null &&
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

    private void SubscribeToNetworkPostIts()
    {
        if (_networkPostIts == null || _hasSubscribedToNetworkPostIts)
        {
            return;
        }

        _networkPostIts.OnListChanged += OnNetworkPostItsChanged;
        _hasSubscribedToNetworkPostIts = true;
    }

    private void UnsubscribeFromNetworkPostIts()
    {
        if (_networkPostIts == null || !_hasSubscribedToNetworkPostIts)
        {
            return;
        }

        _networkPostIts.OnListChanged -= OnNetworkPostItsChanged;
        _hasSubscribedToNetworkPostIts = false;
    }

    private void SubscribeToNetworkPublicVisuals()
    {
        if (_networkPublicVisuals == null || _hasSubscribedToNetworkPublicVisuals)
        {
            return;
        }

        _networkPublicVisuals.OnListChanged += OnNetworkPublicVisualsChanged;
        _hasSubscribedToNetworkPublicVisuals = true;
    }

    private void UnsubscribeFromNetworkPublicVisuals()
    {
        if (_networkPublicVisuals == null || !_hasSubscribedToNetworkPublicVisuals)
        {
            return;
        }

        _networkPublicVisuals.OnListChanged -= OnNetworkPublicVisualsChanged;
        _hasSubscribedToNetworkPublicVisuals = false;
    }

    private void SubscribeToNetworkGuessItems()
    {
        if (_networkGuessItems == null || _hasSubscribedToNetworkGuessItems)
        {
            return;
        }

        _networkGuessItems.OnListChanged += OnNetworkGuessItemsChanged;
        _hasSubscribedToNetworkGuessItems = true;
    }

    private void UnsubscribeFromNetworkGuessItems()
    {
        if (_networkGuessItems == null || !_hasSubscribedToNetworkGuessItems)
        {
            return;
        }

        _networkGuessItems.OnListChanged -= OnNetworkGuessItemsChanged;
        _hasSubscribedToNetworkGuessItems = false;
    }

    private void OnNetworkPostItsChanged(NetworkListEvent<PostItRuntimeData> changeEvent)
    {
        RebuildLocalMirrorFromNetworkList();

        if (IsServer)
        {
            ProjectNetworkPostItChangeToPublicVisuals(changeEvent);
        }

        NotifyPostItsChanged();

        if (debugLogs)
        {
            Debug.Log(
                $"[{nameof(PlayerPostItInventory)}] Network list changed. " +
                $"type={changeEvent.Type}, index={changeEvent.Index}, count={_postIts.Count}",
                this);
        }
    }

    private void OnNetworkPublicVisualsChanged(
        NetworkListEvent<PostItPublicVisualData> changeEvent)
    {
        RebuildLocalPublicVisualMirror();
        NotifyPublicVisualsChanged();

        if (debugLogs)
        {
            Debug.Log(
                $"[{nameof(PlayerPostItInventory)}] Public visual list changed. " +
                $"type={changeEvent.Type}, index={changeEvent.Index}, count={_publicVisuals.Count}",
                this);
        }
    }

    private void OnNetworkGuessItemsChanged(
        NetworkListEvent<PostItGuessOwnerData> changeEvent)
    {
        RebuildLocalGuessMirror();
        NotifyGuessItemsChanged();

        if (debugLogs)
        {
            Debug.Log(
                $"[{nameof(PlayerPostItInventory)}] Guess list changed. " +
                $"type={changeEvent.Type}, index={changeEvent.Index}, count={_guessItems.Count}",
                this);
        }
    }

    private void RebuildLocalMirrorFromNetworkList()
    {
        _postIts.Clear();

        if (_networkPostIts == null)
        {
            return;
        }

        for (int i = 0; i < _networkPostIts.Count; i++)
        {
            _postIts.Add(_networkPostIts[i]);
        }
    }

    private void RebuildLocalPublicVisualMirror()
    {
        _publicVisuals.Clear();

        if (_networkPublicVisuals == null)
        {
            return;
        }

        for (int i = 0; i < _networkPublicVisuals.Count; i++)
        {
            _publicVisuals.Add(_networkPublicVisuals[i]);
        }
    }

    private void RebuildLocalGuessMirror()
    {
        _guessItems.Clear();

        if (_networkGuessItems == null)
        {
            return;
        }

        for (int i = 0; i < _networkGuessItems.Count; i++)
        {
            _guessItems.Add(_networkGuessItems[i]);
        }
    }

    private void RebuildLocalPublicVisualsFromPrivateMirrorAndNotify()
    {
        _publicVisuals.Clear();

        for (int i = 0; i < _postIts.Count; i++)
        {
            _publicVisuals.Add(CreatePublicVisualData(_postIts[i]));
        }

        NotifyPublicVisualsChanged();
    }

    private void NotifyPostItsChanged()
    {
        PostItsChanged?.Invoke();
    }

    private void NotifyPublicVisualsChanged()
    {
        PublicVisualsChanged?.Invoke();
    }

    private void NotifyGuessItemsChanged()
    {
        GuessItemsChanged?.Invoke();
    }

    private bool ValidateGuessReplacement(IReadOnlyList<PostItGuessOwnerData> guessItems)
    {
        if (guessItems == null)
        {
            return false;
        }

        for (int i = 0; i < guessItems.Count; i++)
        {
            PostItGuessOwnerData data = guessItems[i];
            if (!data.IsValid)
            {
                return false;
            }

            if (i > 0 &&
                (guessItems[0].RoundRevision != data.RoundRevision ||
                 guessItems[0].GuessRevision != data.GuessRevision))
            {
                return false;
            }

            for (int otherIndex = i + 1; otherIndex < guessItems.Count; otherIndex++)
            {
                if (data.PostItId == guessItems[otherIndex].PostItId)
                {
                    return false;
                }
            }
        }

        return true;
    }

    private bool ReplaceNetworkGuessItemsWithRollback(
        IReadOnlyList<PostItGuessOwnerData> guessItems)
    {
        PostItGuessOwnerData[] previousItems = GetNetworkGuessSnapshot();
        if (TryWriteNetworkGuessItems(guessItems))
        {
            return true;
        }

        if (!TryWriteNetworkGuessItems(previousItems))
        {
            Debug.LogError(
                $"[{nameof(PlayerPostItInventory)}] Failed to roll back guess item replacement.",
                this);
        }

        return false;
    }

    private bool TryWriteNetworkGuessItems(IReadOnlyList<PostItGuessOwnerData> items)
    {
        try
        {
            if (_networkGuessItems.Count > 0)
            {
                _networkGuessItems.Clear();
            }

            for (int i = 0; i < items.Count; i++)
            {
                _networkGuessItems.Add(items[i]);
            }

            return NetworkGuessItemsMatch(items);
        }
        catch (Exception exception)
        {
            LogWarning($"Guess item network write threw an exception. message={exception.Message}");
            return false;
        }
    }

    private PostItGuessOwnerData[] GetNetworkGuessSnapshot()
    {
        if (_networkGuessItems == null || _networkGuessItems.Count == 0)
        {
            return Array.Empty<PostItGuessOwnerData>();
        }

        PostItGuessOwnerData[] snapshot = new PostItGuessOwnerData[_networkGuessItems.Count];
        for (int i = 0; i < _networkGuessItems.Count; i++)
        {
            snapshot[i] = _networkGuessItems[i];
        }

        return snapshot;
    }

    private bool NetworkGuessItemsMatch(IReadOnlyList<PostItGuessOwnerData> expectedItems)
    {
        if (_networkGuessItems == null || _networkGuessItems.Count != expectedItems.Count)
        {
            return false;
        }

        for (int i = 0; i < expectedItems.Count; i++)
        {
            if (!_networkGuessItems[i].Equals(expectedItems[i]))
            {
                return false;
            }
        }

        return true;
    }

    private int FindGuessItemIndex(int postItId)
    {
        int foundIndex = -1;
        for (int i = 0; i < _guessItems.Count; i++)
        {
            if (_guessItems[i].PostItId == postItId)
            {
                if (foundIndex >= 0)
                {
                    return -1;
                }

                foundIndex = i;
            }
        }

        return foundIndex;
    }

    private bool LocalGuessItemsMatch(IReadOnlyList<PostItGuessOwnerData> expectedItems)
    {
        if (_guessItems.Count != expectedItems.Count)
        {
            return false;
        }

        for (int i = 0; i < expectedItems.Count; i++)
        {
            if (!_guessItems[i].Equals(expectedItems[i]))
            {
                return false;
            }
        }

        return true;
    }

    private void ProjectNetworkPostItChangeToPublicVisuals(
        NetworkListEvent<PostItRuntimeData> changeEvent)
    {
        if (!IsServer || _networkPostIts == null || _networkPublicVisuals == null)
        {
            return;
        }

        if (TryApplyServerPublicVisualDelta(changeEvent) &&
            PublicProjectionMatchesPrivate())
        {
            return;
        }

        if (debugLogs)
        {
            Debug.LogWarning(
                $"[{nameof(PlayerPostItInventory)}] Public visual projection mismatch. " +
                $"type={changeEvent.Type}, index={changeEvent.Index}",
                this);
        }

        ReconcileServerPublicVisualsFromPrivate();
    }

    private bool TryApplyServerPublicVisualDelta(
        NetworkListEvent<PostItRuntimeData> changeEvent)
    {
        switch (changeEvent.Type)
        {
            case NetworkListEvent<PostItRuntimeData>.EventType.Add:
            {
                int index = changeEvent.Index;
                if (index < 0 ||
                    index >= _networkPostIts.Count ||
                    _networkPublicVisuals.Count + 1 != _networkPostIts.Count ||
                    index != _networkPublicVisuals.Count)
                {
                    return false;
                }

                PostItRuntimeData privateData = _networkPostIts[index];
                if (privateData.PostItId != changeEvent.Value.PostItId)
                {
                    return false;
                }

                _networkPublicVisuals.Add(CreatePublicVisualData(privateData));
                return true;
            }

            case NetworkListEvent<PostItRuntimeData>.EventType.Insert:
            {
                int index = changeEvent.Index;
                if (index < 0 ||
                    index >= _networkPostIts.Count ||
                    index > _networkPublicVisuals.Count ||
                    _networkPublicVisuals.Count + 1 != _networkPostIts.Count)
                {
                    return false;
                }

                PostItRuntimeData privateData = _networkPostIts[index];
                if (privateData.PostItId != changeEvent.Value.PostItId)
                {
                    return false;
                }

                _networkPublicVisuals.Insert(index, CreatePublicVisualData(privateData));
                return true;
            }

            case NetworkListEvent<PostItRuntimeData>.EventType.Remove:
            {
                if (_networkPublicVisuals.Count != _networkPostIts.Count + 1)
                {
                    return false;
                }

                int publicIndex = FindNetworkPublicVisualIndex(changeEvent.Value.PostItId);
                if (publicIndex < 0 ||
                    !_networkPublicVisuals[publicIndex].Equals(
                        CreatePublicVisualData(changeEvent.Value)))
                {
                    return false;
                }

                _networkPublicVisuals.RemoveAt(publicIndex);
                return true;
            }

            case NetworkListEvent<PostItRuntimeData>.EventType.RemoveAt:
            {
                int index = changeEvent.Index;
                if (index < 0 ||
                    index >= _networkPublicVisuals.Count ||
                    _networkPublicVisuals.Count != _networkPostIts.Count + 1)
                {
                    return false;
                }

                PostItPublicVisualData removedPublicData =
                    CreatePublicVisualData(changeEvent.Value);
                if (_networkPublicVisuals[index].PostItId != removedPublicData.PostItId ||
                    !_networkPublicVisuals[index].Equals(removedPublicData))
                {
                    return false;
                }

                _networkPublicVisuals.RemoveAt(index);
                return true;
            }

            case NetworkListEvent<PostItRuntimeData>.EventType.Value:
            {
                int index = changeEvent.Index;
                if (index < 0 ||
                    index >= _networkPostIts.Count ||
                    index >= _networkPublicVisuals.Count ||
                    _networkPublicVisuals.Count != _networkPostIts.Count)
                {
                    return false;
                }

                PostItRuntimeData privateData = _networkPostIts[index];
                PostItPublicVisualData previousPublicData =
                    CreatePublicVisualData(changeEvent.PreviousValue);
                if (!privateData.Equals(changeEvent.Value) ||
                    privateData.PostItId != changeEvent.PreviousValue.PostItId ||
                    _networkPublicVisuals[index].PostItId != privateData.PostItId ||
                    !_networkPublicVisuals[index].Equals(previousPublicData))
                {
                    return false;
                }

                PostItPublicVisualData projectedData = CreatePublicVisualData(privateData);
                if (!_networkPublicVisuals[index].Equals(projectedData))
                {
                    _networkPublicVisuals[index] = projectedData;
                }

                return true;
            }

            case NetworkListEvent<PostItRuntimeData>.EventType.Clear:
            {
                if (_networkPostIts.Count != 0)
                {
                    return false;
                }

                if (_networkPublicVisuals.Count > 0)
                {
                    _networkPublicVisuals.Clear();
                }

                return true;
            }

            default:
                return false;
        }
    }

    private void ReconcileServerPublicVisualsFromPrivate()
    {
        if (!IsServer ||
            !IsSpawnedNetworkSession() ||
            _networkPostIts == null ||
            _networkPublicVisuals == null ||
            PublicProjectionMatchesPrivate())
        {
            return;
        }

        if (_networkPublicVisuals.Count > 0)
        {
            _networkPublicVisuals.Clear();
        }

        for (int i = 0; i < _networkPostIts.Count; i++)
        {
            _networkPublicVisuals.Add(CreatePublicVisualData(_networkPostIts[i]));
        }

        if (!PublicProjectionMatchesPrivate())
        {
            LogWarning("Failed to reconcile public visual state from private inventory.");
        }
    }

    private bool PublicProjectionMatchesPrivate()
    {
        if (_networkPostIts == null ||
            _networkPublicVisuals == null ||
            _networkPostIts.Count != _networkPublicVisuals.Count)
        {
            return false;
        }

        for (int i = 0; i < _networkPostIts.Count; i++)
        {
            PostItPublicVisualData projectedData =
                CreatePublicVisualData(_networkPostIts[i]);
            if (!_networkPublicVisuals[i].Equals(projectedData))
            {
                return false;
            }
        }

        return true;
    }

    private int FindNetworkPublicVisualIndex(int postItId)
    {
        if (_networkPublicVisuals == null)
        {
            return -1;
        }

        for (int i = 0; i < _networkPublicVisuals.Count; i++)
        {
            if (_networkPublicVisuals[i].PostItId == postItId)
            {
                return i;
            }
        }

        return -1;
    }

    private static PostItPublicVisualData CreatePublicVisualData(PostItRuntimeData data)
    {
        return new PostItPublicVisualData(
            data.PostItId,
            data.SlotIndex,
            data.Type,
            data.VisualId,
            data.OriginalOwnerClientId == data.HolderClientId);
    }

    private bool ClearAuthoritativeStorage()
    {
        if (IsNetworkStorageActive())
        {
            if (_networkPostIts.Count == 0)
            {
                return true;
            }

            _networkPostIts.Clear();
            if (_networkPostIts.Count != 0)
            {
                LogWarning("Failed to clear network post-it storage.");
                return false;
            }

            return true;
        }

        if (IsSpawnedNetworkSession())
        {
            LogWarning("Cannot clear post-its because network storage is unavailable.");
            return false;
        }

        if (_postIts.Count == 0)
        {
            return true;
        }

        _postIts.Clear();
        NotifyPostItsChanged();
        RebuildLocalPublicVisualsFromPrivateMirrorAndNotify();
        return true;
    }

    private bool AddToAuthoritativeStorage(PostItRuntimeData data)
    {
        if (IsNetworkStorageActive())
        {
            int previousCount = _networkPostIts.Count;
            _networkPostIts.Add(data);

            if (_networkPostIts.Count != previousCount + 1 ||
                !_networkPostIts[previousCount].Equals(data))
            {
                LogWarning($"Failed to add post-it to network storage. postItId={data.PostItId}");
                return false;
            }

            return true;
        }

        if (IsSpawnedNetworkSession())
        {
            LogWarning($"Cannot add post-it because network storage is unavailable. postItId={data.PostItId}");
            return false;
        }

        _postIts.Add(data);
        NotifyPostItsChanged();
        RebuildLocalPublicVisualsFromPrivateMirrorAndNotify();
        return true;
    }

    private bool RemoveAtAuthoritativeStorage(int index, int expectedPostItId)
    {
        if (IsNetworkStorageActive())
        {
            if (index < 0 || index >= _networkPostIts.Count)
            {
                LogWarning($"Rejected network post-it remove because index is invalid. index={index}");
                return false;
            }

            if (_networkPostIts[index].PostItId != expectedPostItId)
            {
                LogWarning(
                    $"Rejected network post-it remove because mirror and network storage differ. " +
                    $"index={index}, expectedPostItId={expectedPostItId}, " +
                    $"networkPostItId={_networkPostIts[index].PostItId}");
                return false;
            }

            int previousCount = _networkPostIts.Count;
            _networkPostIts.RemoveAt(index);
            if (_networkPostIts.Count != previousCount - 1)
            {
                LogWarning($"Failed to remove post-it from network storage. postItId={expectedPostItId}");
                return false;
            }

            return true;
        }

        if (IsSpawnedNetworkSession())
        {
            LogWarning($"Cannot remove post-it because network storage is unavailable. postItId={expectedPostItId}");
            return false;
        }

        if (index < 0 || index >= _postIts.Count || _postIts[index].PostItId != expectedPostItId)
        {
            LogWarning($"Rejected local post-it remove because index is invalid. index={index}");
            return false;
        }

        _postIts.RemoveAt(index);
        NotifyPostItsChanged();
        RebuildLocalPublicVisualsFromPrivateMirrorAndNotify();
        return true;
    }

    private bool NetworkListContainsPostIt(int postItId)
    {
        if (_networkPostIts == null)
        {
            return false;
        }

        for (int i = 0; i < _networkPostIts.Count; i++)
        {
            if (_networkPostIts[i].PostItId == postItId)
            {
                return true;
            }
        }

        return false;
    }

    private bool CanMutateServerState()
    {
        if (NetworkManager != null && NetworkManager.IsListening)
        {
            return IsServer;
        }

        return true;
    }

    private bool TryResolveOwnerClientId(out ulong ownerClientId)
    {
        if (NetworkObject != null && NetworkObject.IsSpawned)
        {
            ownerClientId = NetworkObject.OwnerClientId;
            return true;
        }

        ownerClientId = ulong.MaxValue;
        return false;
    }

    private bool IsSlotOccupied(int slotIndex)
    {
        for (int i = 0; i < _postIts.Count; i++)
        {
            if (_postIts[i].SlotIndex == slotIndex)
            {
                return true;
            }
        }

        return false;
    }

    private void Log(string message)
    {
        if (debugLogs)
        {
            Debug.Log($"[{nameof(PlayerPostItInventory)}] {message}", this);
        }
    }

    private void LogWarning(string message)
    {
        if (debugLogs)
        {
            Debug.LogWarning($"[{nameof(PlayerPostItInventory)}] {message}", this);
        }
    }
}
