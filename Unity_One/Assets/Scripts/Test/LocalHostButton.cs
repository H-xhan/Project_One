using Unity.Netcode;
using UnityEngine;

public class LocalHostButton : MonoBehaviour
{
    public void StartLocalHost()
    {
        if (NetworkManager.Singleton == null) return;

        if (NetworkManager.Singleton.IsListening)
        {
            Debug.Log("[LocalHost] Already running.");
            return;
        }

        // Relay 안 씀: 그냥 로컬 호스트 시작
        NetworkManager.Singleton.StartHost();
        Debug.Log("[LocalHost] StartHost (No Relay).");
    }

    public void StopNet()
    {
        if (NetworkManager.Singleton == null) return;
        if (!NetworkManager.Singleton.IsListening) return;

        NetworkManager.Singleton.Shutdown();
        Debug.Log("[LocalHost] Shutdown.");
    }
}
