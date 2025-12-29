using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Networking.Transport.Relay;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
using UnityEngine;

public class RelayManager : MonoBehaviour
{
    public static RelayManager Instance { get; private set; }

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
        }
    }

    // [기능 1] 호스트 시작
    public async Task<string> CreateRelay(int maxConnections)
    {
        try
        {
            // 1. Relay 할당
            Allocation allocation = await RelayService.Instance.CreateAllocationAsync(maxConnections);

            // 2. 조인 코드 발급
            string joinCode = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);

            // 3. [수정됨] 찬 님 버전에서 작동하는 방식 (AllocationUtils 사용)
            RelayServerData relayServerData = AllocationUtils.ToRelayServerData(allocation, "dtls");

            NetworkManager.Singleton.GetComponent<UnityTransport>().SetRelayServerData(relayServerData);

            // 4. 호스트 시작
            NetworkManager.Singleton.StartHost();

            Debug.Log($"[Relay] 호스트 시작 완료! 코드: {joinCode}");

            NetworkManager.Singleton.SceneManager.LoadScene("RoomLobby", UnityEngine.SceneManagement.LoadSceneMode.Single);

            return joinCode;
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[Relay] 호스트 생성 실패: {e}");
            return null;
        }
    }

    // [기능 2] 게스트 입장
    public async void JoinRelay(string joinCode)
    {
        try
        {
            // 1. 코드로 Relay 데이터 찾기
            JoinAllocation joinAllocation = await RelayService.Instance.JoinAllocationAsync(joinCode);

            // 2. [수정됨] 찬 님 버전에서 작동하는 방식 (AllocationUtils 사용)
            RelayServerData relayServerData = AllocationUtils.ToRelayServerData(joinAllocation, "dtls");

            NetworkManager.Singleton.GetComponent<UnityTransport>().SetRelayServerData(relayServerData);

            // 3. 클라이언트 시작
            NetworkManager.Singleton.StartClient();

            Debug.Log($"[Relay] 방 입장 완료! 코드: {joinCode}");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[Relay] 입장 실패: {e}");
        }
    }
}