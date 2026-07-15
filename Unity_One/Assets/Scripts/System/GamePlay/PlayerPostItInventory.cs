using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public class PlayerPostItInventory : NetworkBehaviour
{
    [SerializeField] private int maxPostItSlots = 6;
    [SerializeField] private bool debugLogs = false;

    private readonly List<PostItRuntimeData> _postIts = new List<PostItRuntimeData>();
    private NetworkList<PostItRuntimeData> _networkPostIts;
    private bool _hasSubscribedToNetworkPostIts;

    public event Action PostItsChanged;

    private void Awake()
    {
        _networkPostIts = new NetworkList<PostItRuntimeData>(
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

        SubscribeToNetworkPostIts();
        RebuildLocalMirrorFromNetworkList();
        NotifyPostItsChanged();
    }

    public override void OnNetworkDespawn()
    {
        UnsubscribeFromNetworkPostIts();
        base.OnNetworkDespawn();
    }

    public int Count => _postIts.Count;
    public int Capacity => Mathf.Max(0, maxPostItSlots);
    public bool IsFull => Count >= Capacity;
    public IReadOnlyList<PostItRuntimeData> Items => _postIts;

    public PostItRuntimeData[] GetSnapshot()
    {
        return _postIts.ToArray();
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

    private void OnNetworkPostItsChanged(NetworkListEvent<PostItRuntimeData> changeEvent)
    {
        RebuildLocalMirrorFromNetworkList();
        NotifyPostItsChanged();

        if (debugLogs)
        {
            Debug.Log(
                $"[{nameof(PlayerPostItInventory)}] Network list changed. " +
                $"type={changeEvent.Type}, index={changeEvent.Index}, count={_postIts.Count}",
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

    private void NotifyPostItsChanged()
    {
        PostItsChanged?.Invoke();
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
