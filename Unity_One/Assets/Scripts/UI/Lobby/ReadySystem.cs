using Unity.Netcode;
using UnityEngine;

public class ReadySystem : NetworkBehaviour
{
    [Tooltip("라운드 매니저 참조(비우면 자동 탐색)")]
    [SerializeField] private GameStateManager gameStateManager;

    public NetworkList<ulong> ReadyClients { get; private set; }

    private void Awake()
    {
        ReadyClients = new NetworkList<ulong>();
    }

    public override void OnNetworkSpawn()
    {
        if (gameStateManager == null) gameStateManager = FindFirstObjectByType<GameStateManager>();

        if (IsServer)
        {
            NetworkManager.OnClientDisconnectCallback += OnClientDisconnected;
        }
    }

    public override void OnNetworkDespawn()
    {
        if (IsServer && NetworkManager != null)
        {
            NetworkManager.OnClientDisconnectCallback -= OnClientDisconnected;
        }
    }

    private void OnClientDisconnected(ulong clientId)
    {
        RemoveReady(clientId);
    }

    public bool IsClientReady(ulong clientId)
    {
        for (int i = 0; i < ReadyClients.Count; i++)
        {
            if (ReadyClients[i] == clientId) return true;
        }
        return false;
    }

    public int GetConnectedClientCount()
    {
        if (NetworkManager == null) return 0;
        return NetworkManager.ConnectedClientsList.Count;
    }

    public int GetReadyCount()
    {
        return ReadyClients.Count;
    }

    public bool AreAllReady()
    {
        int connected = GetConnectedClientCount();
        return connected > 0 && ReadyClients.Count == connected;
    }

    public void ToggleLocalReady()
    {
        if (NetworkManager == null) return;
        ToggleReadyServerRpc(NetworkManager.LocalClientId);
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void ToggleReadyServerRpc(ulong clientId)
    {
        if (IsClientReady(clientId)) RemoveReady(clientId);
        else AddReady(clientId);

        if (gameStateManager != null && AreAllReady())
        {
            gameStateManager.TryStartCountdownFromReady();
        }
    }

    public void ClearAllReadyServer()
    {
        if (!IsServer) return;
        ReadyClients.Clear();
    }

    private void AddReady(ulong clientId)
    {
        if (IsClientReady(clientId)) return;
        ReadyClients.Add(clientId);
    }

    private void RemoveReady(ulong clientId)
    {
        for (int i = 0; i < ReadyClients.Count; i++)
        {
            if (ReadyClients[i] == clientId)
            {
                ReadyClients.RemoveAt(i);
                return;
            }
        }
    }
}
