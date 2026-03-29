using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public class ReadySystem : NetworkBehaviour
{
    [Tooltip("게임 시작을 허용하는 최소 인원 (테스트는 1로)")]
    [SerializeField] private int minPlayersToStart = 2;

    private NetworkList<ulong> readyClients;

    private void Awake()
    {
        readyClients = new NetworkList<ulong>();
    }

    public int GetConnectedClientCount()
    {
        var nm = NetworkManager.Singleton;
        if (nm == null) return 0;
        return nm.ConnectedClientsList.Count;
    }

    public int GetReadyCount()
    {
        return readyClients.Count;
    }

    public bool IsClientReady(ulong clientId)
    {
        return readyClients.Contains(clientId);
    }

    public bool IsLocalReady()
    {
        var nm = NetworkManager.Singleton;
        if (nm == null) return false;
        return IsClientReady(nm.LocalClientId);
    }

    public bool CanStartGameServer()
    {
        if (!IsServer) return false;

        int connected = GetConnectedClientCount();
        int minPlayers = Mathf.Max(1, minPlayersToStart);
        return connected >= minPlayers;
    }

    public bool AreAllReady()
    {
        if (!IsServer) return false;

        int connected = GetConnectedClientCount();
        if (connected <= 0) return false;

        return readyClients.Count >= connected;
    }

    public void ToggleLocalReady()
    {
        var nm = NetworkManager.Singleton;
        if (nm == null) return;

        ToggleReadyServerRpc(nm.LocalClientId);
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void ToggleReadyServerRpc(ulong clientId)
    {
        if (!IsServer) return;

        if (readyClients.Contains(clientId))
        {
            for (int i = readyClients.Count - 1; i >= 0; i--)
            {
                if (readyClients[i] == clientId)
                    readyClients.RemoveAt(i);
            }
        }
        else
        {
            readyClients.Add(clientId);
        }
    }

    public void ResetAllReadyServer()
    {
        if (!IsServer) return;
        readyClients.Clear();
    }

    // 구버전/다른 스크립트 호환용 별칭
    public void ClearAllReadyServer() => ResetAllReadyServer();
    public void Server_ResetAllReady() => ResetAllReadyServer();
    public bool Server_CanStartGame() => CanStartGameServer();
}
