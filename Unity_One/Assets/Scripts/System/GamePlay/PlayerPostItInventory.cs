using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public class PlayerPostItInventory : NetworkBehaviour
{
    [SerializeField] private int maxPostItSlots = 6;
    [SerializeField] private bool debugLogs = false;

    private readonly List<PostItRuntimeData> _postIts = new List<PostItRuntimeData>();

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

        _postIts.Clear();
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

        _postIts.Add(assignedData);
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

            removedData = _postIts[i];
            _postIts.RemoveAt(i);
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
