using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Core.Environments;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
using UnityEngine;

public class RelayManager : MonoBehaviour
{
    public static RelayManager Instance { get; private set; }

    [Header("옵션")]
    [Tooltip("UGS Environment 이름 (두 PC에서 반드시 동일해야 합니다. 예: production, dev)")]
    [SerializeField] private string environmentName = "production";

    [Tooltip("Relay 연결에 DTLS(보안) 사용 여부")]
    [SerializeField] private bool useDtls = true;

    private bool _servicesInitialized;

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

    // LobbyManager(기존 코드) 호환용 오버로드
    public Task<string> CreateRelay(int maxConnections)
    {
        return CreateRelay(maxConnections, true);
    }

    // DevTestUI 등에서 사용
    public async Task<string> CreateRelay(int maxConnections, bool autoStartHost)
    {
        if (!ValidateNetworkManager()) return null;

        if (NetworkManager.Singleton.IsListening)
        {
            Debug.LogWarning("[Relay] Cannot start Host while network is already running");
            return null;
        }

        bool ok = await EnsureServicesInitialized();
        if (!ok) return null;

        try
        {
            Allocation allocation = await RelayService.Instance.CreateAllocationAsync(maxConnections);
            string joinCode = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);

            ApplyRelayToTransport(allocation);
            Debug.Log($"[Relay] Host Prepared. Code: {joinCode}");

            if (autoStartHost)
            {
                bool started = NetworkManager.Singleton.StartHost();
                if (!started)
                {
                    Debug.LogError("[Relay] StartHost failed");
                    return null;
                }

                Debug.Log($"[Relay] Host Started. Code: {joinCode}");
            }

            return joinCode;
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[Relay] CreateRelay failed: {e}");
            return null;
        }
    }

    public async Task<bool> JoinRelayAsync(string joinCode)
    {
        return await JoinRelayAsync(joinCode, true);
    }

    public async Task<bool> JoinRelayAsync(string joinCode, bool autoStartClient)
    {
        if (!ValidateNetworkManager()) return false;

        if (NetworkManager.Singleton.IsListening)
        {
            Debug.LogWarning("[Relay] Cannot start Client while network is already running");
            return false;
        }

        bool ok = await EnsureServicesInitialized();
        if (!ok) return false;

        // 입력 정규화 (공백/줄바꿈 때문에 404 나는 경우 방지)
        joinCode = (joinCode ?? "").Trim().ToUpperInvariant();
        Debug.Log($"[Relay] TryJoin code='{joinCode}' len={joinCode.Length}");

        // 보통 JoinCode는 6자리입니다. 다르면 일단 입력 문제로 처리합니다.
        if (joinCode.Length != 6)
        {
            Debug.LogWarning($"[Relay] Invalid join code format: '{joinCode}'");
            return false;
        }

        try
        {
            JoinAllocation joinAllocation = await RelayService.Instance.JoinAllocationAsync(joinCode);

            ApplyRelayToTransport(joinAllocation);
            Debug.Log("[Relay] Client Join Prepared");

            if (autoStartClient)
            {
                bool started = NetworkManager.Singleton.StartClient();
                if (!started)
                {
                    Debug.LogError("[Relay] StartClient failed");
                    return false;
                }

                Debug.Log("[Relay] Client Started");
            }

            return true;
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[Relay] JoinRelay failed: {e}");
            return false;
        }
    }

    // 기존 코드에서 async void로 호출하던 부분 호환용
    public async void JoinRelay(string joinCode)
    {
        await JoinRelayAsync(joinCode, true);
    }

    private async Task<bool> EnsureServicesInitialized()
    {
        if (_servicesInitialized) return true;

        try
        {
            if (UnityServices.State != ServicesInitializationState.Initialized)
            {
                var options = new InitializationOptions();

                if (!string.IsNullOrWhiteSpace(environmentName))
                    options.SetEnvironmentName(environmentName.Trim());

                await UnityServices.InitializeAsync(options);
            }

            if (!AuthenticationService.Instance.IsSignedIn)
                await AuthenticationService.Instance.SignInAnonymouslyAsync();

            _servicesInitialized = true;

            Debug.Log($"[UGS] Initialized. cloudProjectId={Application.cloudProjectId}, env={environmentName}");
            Debug.Log("[Relay] Services Initialized");

            return true;
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[Relay] Services Init failed: {e}");
            return false;
        }
    }

    private bool ValidateNetworkManager()
    {
        if (NetworkManager.Singleton == null)
        {
            Debug.LogError("[Relay] NetworkManager.Singleton not found in scene");
            return false;
        }

        var transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
        if (transport == null)
        {
            Debug.LogError("[Relay] UnityTransport not found on NetworkManager");
            return false;
        }

        return true;
    }

    private void ApplyRelayToTransport(Allocation allocation)
    {
        var transport = NetworkManager.Singleton.GetComponent<UnityTransport>();

        transport.SetRelayServerData(
            allocation.RelayServer.IpV4,
            (ushort)allocation.RelayServer.Port,
            allocation.AllocationIdBytes,
            allocation.Key,
            allocation.ConnectionData,
            allocation.ConnectionData,
            useDtls
        );

        Debug.Log("[Relay] Transport configured for Host");
    }

    private void ApplyRelayToTransport(JoinAllocation joinAllocation)
    {
        var transport = NetworkManager.Singleton.GetComponent<UnityTransport>();

        transport.SetRelayServerData(
            joinAllocation.RelayServer.IpV4,
            (ushort)joinAllocation.RelayServer.Port,
            joinAllocation.AllocationIdBytes,
            joinAllocation.Key,
            joinAllocation.ConnectionData,
            joinAllocation.HostConnectionData,
            useDtls
        );

        Debug.Log("[Relay] Transport configured for Client");
    }
}
