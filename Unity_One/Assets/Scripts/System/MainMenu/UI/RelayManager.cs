using System;
using System.Threading;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
using UnityEngine;

public class RelayManager : MonoBehaviour
{
    public static RelayManager Instance { get; private set; }

    [Header("Relay 설정")]
    [SerializeField, Tooltip("Join 후 실제 연결(OnClientConnected)까지 기다리는 최대 시간(초)")]
    private float joinConnectTimeoutSec = 8f;

    private bool _servicesInitialized;
    private TaskCompletionSource<bool> _clientConnectedTcs;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        HookNetcodeCallbacks();
    }

    private void HookNetcodeCallbacks()
    {
        if (NetworkManager.Singleton == null) return;

        NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
        NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;

        NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnected;
        NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;

        NetworkManager.Singleton.OnTransportFailure -= OnTransportFailure;
        NetworkManager.Singleton.OnTransportFailure += OnTransportFailure;
    }

    private void OnClientConnected(ulong clientId)
    {
        Debug.Log($"[Relay] OnClientConnected: {clientId}");

        if (NetworkManager.Singleton != null && NetworkManager.Singleton.LocalClientId == clientId)
        {
            _clientConnectedTcs?.TrySetResult(true);
        }
    }

    private void OnClientDisconnected(ulong clientId)
    {
        Debug.LogWarning($"[Relay] OnClientDisconnected: {clientId}");
        _clientConnectedTcs?.TrySetResult(false);
    }

    private void OnTransportFailure()
    {
        Debug.LogError("[Relay] OnTransportFailure: transport failed. NetworkManager will shutdown.");
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
        {
            NetworkManager.Singleton.Shutdown();
        }
    }

    private bool TryGetNet(out NetworkManager nm, out UnityTransport utp)
    {
        nm = NetworkManager.Singleton;
        if (nm == null)
        {
            Debug.LogError("[Relay] NetworkManager.Singleton is null");
            utp = null;
            return false;
        }

        utp = nm.NetworkConfig.NetworkTransport as UnityTransport;
        if (utp == null)
        {
            Debug.LogError("[Relay] NetworkTransport is not UnityTransport");
            return false;
        }

        return true;
    }

    public async Task EnsureServicesInitialized()
    {
        if (_servicesInitialized) return;

        await UnityServices.InitializeAsync();

        if (!AuthenticationService.Instance.IsSignedIn)
        {
            await AuthenticationService.Instance.SignInAnonymouslyAsync();
            Debug.Log($"[Relay] Signed In. PlayerID={AuthenticationService.Instance.PlayerId}");
        }

        _servicesInitialized = true;
        HookNetcodeCallbacks();

        Debug.Log("[Relay] Services Initialized");
    }

    public async Task<string> CreateRelay(int maxConnections)
    {
        try
        {
            await EnsureServicesInitialized();

            if (!TryGetNet(out var nm, out var utp))
                return string.Empty;

            if (nm.IsListening)
            {
                Debug.LogWarning("[Relay] CreateRelay called but Network already running.");
                return string.Empty;
            }

            int mc = Mathf.Max(1, maxConnections);

            Allocation alloc = await RelayService.Instance.CreateAllocationAsync(mc);
            string joinCode = await RelayService.Instance.GetJoinCodeAsync(alloc.AllocationId);

            // UDP 고정 (DTLS 사용 안 함)
            utp.SetRelayServerData(
                alloc.RelayServer.IpV4,
                (ushort)alloc.RelayServer.Port,
                alloc.AllocationIdBytes,
                alloc.Key,
                alloc.ConnectionData,
                alloc.ConnectionData,
                false
            );

            Debug.Log($"[Relay] Host Prepared. Code={joinCode} (UDP)");

            bool ok = nm.StartHost();
            Debug.Log($"[Relay] StartHost={ok}");

            return ok ? joinCode : string.Empty;
        }
        catch (Exception e)
        {
            Debug.LogError($"[Relay] CreateRelay failed: {e}");
            return string.Empty;
        }
    }

    // 기존 코드 호환용(유지)
    public async Task<bool> JoinRelayAsync(string joinCode)
    {
        return await JoinRelay(joinCode);
    }

    public async Task<bool> JoinRelay(string joinCode)
    {
        try
        {
            await EnsureServicesInitialized();

            if (!TryGetNet(out var nm, out var utp))
                return false;

            if (nm.IsListening)
            {
                Debug.LogWarning("[Relay] JoinRelay called but Network already running.");
                return false;
            }

            string code = (joinCode ?? string.Empty).Trim().ToUpper();
            if (code.Length < 6)
            {
                Debug.LogWarning("[Relay] Join code invalid.");
                return false;
            }

            JoinAllocation joinAlloc = await RelayService.Instance.JoinAllocationAsync(code);

            // UDP 고정 (DTLS 사용 안 함)
            utp.SetRelayServerData(
                joinAlloc.RelayServer.IpV4,
                (ushort)joinAlloc.RelayServer.Port,
                joinAlloc.AllocationIdBytes,
                joinAlloc.Key,
                joinAlloc.ConnectionData,
                joinAlloc.HostConnectionData,
                false
            );

            Debug.Log($"[Relay] Join Prepared. Code={code} (UDP)");

            _clientConnectedTcs = new TaskCompletionSource<bool>();

            bool startOk = nm.StartClient();
            Debug.Log($"[Relay] StartClient={startOk}");
            if (!startOk) return false;

            using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(joinConnectTimeoutSec)))
            {
                cts.Token.Register(() => _clientConnectedTcs.TrySetResult(false));
                bool connected = await _clientConnectedTcs.Task;
                Debug.Log($"[Relay] Client Connected Result={connected}");
                return connected;
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"[Relay] JoinRelay failed: {e}");
            return false;
        }
    }

    // 공통 루트: 코드로 참가(목록 참가도 결국 여기로 들어오게 통일)
    public async Task<bool> JoinViaCode(string joinCode)
    {
        return await JoinRelay(joinCode);
    }
}
