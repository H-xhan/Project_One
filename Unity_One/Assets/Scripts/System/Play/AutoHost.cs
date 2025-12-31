using Unity.Netcode;
using UnityEngine;

public class AutoHost : MonoBehaviour
{
    void Start()
    {
        // 씬에 NetworkManager가 있는데, 아직 연결이 안 된 상태라면?
        if (NetworkManager.Singleton != null && !NetworkManager.Singleton.IsClient && !NetworkManager.Singleton.IsServer)
        {
            Debug.Log("🛠️ [테스트 모드] 자동으로 호스트를 시작합니다!");
            NetworkManager.Singleton.StartHost();
        }
    }
}