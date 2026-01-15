using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Networking.Transport.Relay;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
using UnityEngine;
using UnityEngine.SceneManagement;

public class RelayManager : MonoBehaviour
{
    public static RelayManager Instance { get; private set; }

    [Header("Relay")]
    [SerializeField, Tooltip("호스트가 만들 수 있는 최대 접속자 수(호스트 제외). 예: 3이면 총 4명")]
    private int defaultMaxConnections = 3;

    [Header("Scene (옵션)")]
    [SerializeField, Tooltip("호스트 시작 후 자동으로 RoomLobby 씬으로 이동할지 여부. 테스트 씬에서는 꺼두세요")]
    private bool autoLoadRoomLobbyScene = true;

    [SerializeField, Tooltip("autoLoadRoomLobbyScene이 켜져 있을 때 로드할 씬 이름")]
    private string roomLobbySceneName = "RoomLobby";

    private bool _servicesReady;

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

    private async Task EnsureServicesReady()
    {
        if (_servicesReady) return;

        await UnityServices.InitializeAsync();

        if (!AuthenticationService.Instance.IsSignedIn)
            await AuthenticationService.Instance.SignInAnonymouslyAsync();

        _servicesReady = true;
    }

    // 기존 호출 유지용
    public Task<string> CreateRelay(int maxConnections) => CreateRelayInternal(maxConnections);

    // 편의용(인스펙터 기본값)
    public Task<string> CreateRelayDefault() => CreateRelayInternal(defaultMaxConnections);

    private async Task<string> CreateRelayInternal(int maxConnections)
    {
        try
        {
            await EnsureServicesReady();

            Allocation allocation = await RelayService.Instance.CreateAllocationAsync(maxConnections);
            string joinCode = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);

            RelayServerData relayServerData = AllocationUtils.ToRelayServerData(allocation, "dtls");
            NetworkManager.Singleton.GetComponent<UnityTransport>().SetRelayServerData(relayServerData);

            NetworkManager.Singleton.StartHost();

            Debug.Log($"[Relay] Host Started. Code: {joinCode}");

            if (autoLoadRoomLobbyScene && NetworkManager.Singleton.SceneManager != null)
                NetworkManager.Singleton.SceneManager.LoadScene(roomLobbySceneName, LoadSceneMode.Single);

            return joinCode;
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[Relay] Create Failed: {e}");
            return null;
        }
    }

    // 기존 호출 유지용
    public async void JoinRelay(string joinCode)
    {
        await JoinRelayAsync(joinCode);
    }

    // UI에서 await 하려고 추가
    public async Task<bool> JoinRelayAsync(string joinCode)
    {
        try
        {
            await EnsureServicesReady();

            JoinAllocation joinAllocation = await RelayService.Instance.JoinAllocationAsync(joinCode);
            RelayServerData relayServerData = AllocationUtils.ToRelayServerData(joinAllocation, "dtls");

            NetworkManager.Singleton.GetComponent<UnityTransport>().SetRelayServerData(relayServerData);
            NetworkManager.Singleton.StartClient();

            Debug.Log($"[Relay] Client Joined. Code: {joinCode}");
            return true;
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[Relay] Join Failed: {e}");
            return false;
        }
    }
}
