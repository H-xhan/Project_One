using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public class PostItRoundManager : NetworkBehaviour
{
    [SerializeField] private int initialPostItCountPerPlayer = 3;
    [SerializeField] private int firstPostItId = 0;
    [SerializeField] private int defaultVisualId = 0;
    [SerializeField] private bool debugLogs = false;

    private int _nextPostItId;

    private void Awake()
    {
        _nextPostItId = firstPostItId;
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

        bool allAddsSucceeded = true;
        int postItCount = Mathf.Max(0, initialPostItCountPerPlayer);
        int validInventoryCount = 0;
        int totalAssignedPostIts = 0;

        foreach (PlayerPostItInventory inventory in inventories)
        {
            if (inventory == null)
            {
                continue;
            }

            validInventoryCount++;
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

        if (validInventoryCount <= 0)
        {
            LogWarning("Rejected initial post-it assignment because no valid inventories were found.");
            return false;
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
        if (inventories == null)
        {
            return new PostItRuntimeData[0];
        }

        List<PostItRuntimeData> snapshot = new List<PostItRuntimeData>();
        foreach (PlayerPostItInventory inventory in inventories)
        {
            if (inventory == null)
            {
                continue;
            }

            snapshot.AddRange(inventory.GetSnapshot());
        }

        return snapshot.ToArray();
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

    private void LogWarning(string message)
    {
        if (debugLogs)
        {
            Debug.LogWarning($"[{nameof(PostItRoundManager)}] {message}", this);
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
