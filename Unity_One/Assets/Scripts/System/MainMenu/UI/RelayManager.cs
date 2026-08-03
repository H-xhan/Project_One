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

    public string CurrentJoinCode { get; private set; } = string.Empty;
    public string LastJoinFailureReason { get; private set; } = string.Empty;
    public string LastJoinFailureMessage { get; private set; } = string.Empty;
    public int LastJoinFailureStatusCode { get; private set; }
    public bool HasJoinFailure => !string.IsNullOrEmpty(LastJoinFailureReason);

    [Header("Relay 설정")]
    [SerializeField, Tooltip("Join 후 실제 연결(OnClientConnected)까지 기다리는 최대 시간(초)")]
    private float joinConnectTimeoutSec = 8f;

    [Header("Debug")]
    [SerializeField, Tooltip("디버그 로그 출력 여부입니다.")]
    private bool enableDebugLogs = false;

    private bool _servicesInitialized;
    private TaskCompletionSource<bool> _clientConnectedTcs;
    private int _sessionGeneration;

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
        Log($"[Relay] OnClientConnected: {clientId}");

        if (NetworkManager.Singleton != null && NetworkManager.Singleton.LocalClientId == clientId)
        {
            _clientConnectedTcs?.TrySetResult(true);
        }
    }

    private void OnClientDisconnected(ulong clientId)
    {
        LogWarning($"[Relay] OnClientDisconnected: {clientId}");

        if (IsWaitingForClientConnection())
            SetJoinFailure("ClientDisconnected", "호스트와의 연결이 끊어졌습니다.");

        _clientConnectedTcs?.TrySetResult(false);
    }

    private void OnTransportFailure()
    {
        Debug.LogError("[Relay] OnTransportFailure: transport failed. NetworkManager will shutdown.");
        SetCurrentJoinCode(string.Empty);

        if (IsWaitingForClientConnection())
        {
            SetJoinFailure("TransportFailure", "네트워크 전송 연결에 실패했습니다.");
            _clientConnectedTcs?.TrySetResult(false);
        }

        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
        {
            NetworkManager.Singleton.Shutdown();
        }
    }

    private void SetCurrentJoinCode(string code)
    {
        CurrentJoinCode = string.IsNullOrWhiteSpace(code)
            ? string.Empty
            : code.Trim().ToUpper();

        Log($"[Relay] CurrentJoinCode={MaskJoinCode(CurrentJoinCode)}");
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
            Log($"[Relay] Signed In. PlayerID={AuthenticationService.Instance.PlayerId}");
        }

        _servicesInitialized = true;
        HookNetcodeCallbacks();

        Log("[Relay] Services Initialized");
    }

    public async Task<string> CreateRelay(int maxConnections)
    {
        int generation = _sessionGeneration;

        try
        {
            await EnsureServicesInitialized();

            if (generation != _sessionGeneration)
                return string.Empty;

            if (!TryGetNet(out var nm, out var utp))
                return string.Empty;

            if (nm.IsListening)
            {
                LogWarning("[Relay] CreateRelay called but Network already running.");
                return string.Empty;
            }

            int mc = Mathf.Max(1, maxConnections);

            Allocation alloc = await RelayService.Instance.CreateAllocationAsync(mc);

            if (generation != _sessionGeneration)
                return string.Empty;

            string joinCode = await RelayService.Instance.GetJoinCodeAsync(alloc.AllocationId);

            if (generation != _sessionGeneration)
                return string.Empty;

            utp.SetRelayServerData(
                alloc.RelayServer.IpV4,
                (ushort)alloc.RelayServer.Port,
                alloc.AllocationIdBytes,
                alloc.Key,
                alloc.ConnectionData,
                alloc.ConnectionData,
                false
            );

            Log($"[Relay] Host Prepared. Code={MaskJoinCode(joinCode)} (UDP)");

            bool ok = nm.StartHost();
            Log($"[Relay] StartHost={ok}");

            if (!ok)
            {
                SetCurrentJoinCode(string.Empty);
                return string.Empty;
            }

            SetCurrentJoinCode(joinCode);
            return joinCode;
        }
        catch (Exception e)
        {
            if (generation != _sessionGeneration)
                return string.Empty;

            Debug.LogError($"[Relay] CreateRelay failed: {e}");
            SetCurrentJoinCode(string.Empty);
            return string.Empty;
        }
    }

    public async Task<bool> JoinRelayAsync(string joinCode)
    {
        return await JoinRelay(joinCode);
    }

    public async Task<bool> JoinRelay(string joinCode)
    {
        int generation = _sessionGeneration;
        ClearJoinFailure();

        try
        {
            await EnsureServicesInitialized();

            if (generation != _sessionGeneration)
                return false;

            if (!TryGetNet(out var nm, out var utp))
            {
                SetJoinFailure("RelayJoinFailed", "네트워크 설정을 확인할 수 없습니다.");
                return false;
            }

            if (nm.IsListening)
            {
                LogWarning("[Relay] JoinRelay called but Network already running.");
                SetJoinFailure("RelayJoinFailed", "이미 네트워크가 실행 중입니다.");
                return false;
            }

            string code = (joinCode ?? string.Empty).Trim().ToUpper();
            if (string.IsNullOrWhiteSpace(joinCode))
            {
                LogWarning("[Relay] Join code is empty.");
                SetJoinFailure("EmptyJoinCode", "참가 코드가 비어 있습니다.");
                return false;
            }

            if (code.Length < 6)
            {
                LogWarning("[Relay] Join code invalid.");
                SetJoinFailure("RelayJoinFailed", "참가 코드가 올바르지 않습니다.");
                return false;
            }

            JoinAllocation joinAlloc;
            try
            {
                joinAlloc = await RelayService.Instance.JoinAllocationAsync(code);

                if (generation != _sessionGeneration)
                    return false;
            }
            catch (RelayServiceException e)
            {
                if (generation != _sessionGeneration)
                    return false;

                int statusCode = GetRelayFailureStatusCode(e);
                if (IsRelayJoinNotFound(e))
                    SetJoinFailure("RelayJoinNotFound", "방 연결 정보가 만료되었습니다. 새 방 코드로 다시 참가해주세요.", statusCode);
                else
                    SetJoinFailure("RelayJoinFailed", "Relay 접속에 실패했습니다.", statusCode);

                Debug.LogError($"[Relay] JoinAllocation failed. reason={LastJoinFailureReason}, status={LastJoinFailureStatusCode}, exception={e}");
                SetCurrentJoinCode(string.Empty);
                return false;
            }
            catch (RequestFailedException e)
            {
                if (generation != _sessionGeneration)
                    return false;

                SetJoinFailure("RelayJoinFailed", "Relay 접속에 실패했습니다.", GetRelayFailureStatusCode(e));
                Debug.LogError($"[Relay] JoinAllocation request failed. reason={LastJoinFailureReason}, status={LastJoinFailureStatusCode}, exception={e}");
                SetCurrentJoinCode(string.Empty);
                return false;
            }

            utp.SetRelayServerData(
                joinAlloc.RelayServer.IpV4,
                (ushort)joinAlloc.RelayServer.Port,
                joinAlloc.AllocationIdBytes,
                joinAlloc.Key,
                joinAlloc.ConnectionData,
                joinAlloc.HostConnectionData,
                false
            );

            Log($"[Relay] Join Prepared. Code={MaskJoinCode(code)} (UDP)");

            _clientConnectedTcs = new TaskCompletionSource<bool>();

            bool startOk = nm.StartClient();
            Log($"[Relay] StartClient={startOk}");
            if (!startOk)
            {
                SetJoinFailure("StartClientFailed", "클라이언트 연결 시작에 실패했습니다.");
                SetCurrentJoinCode(string.Empty);
                return false;
            }

            using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(joinConnectTimeoutSec)))
            {
                cts.Token.Register(() =>
                {
                    if (IsWaitingForClientConnection())
                    {
                        SetJoinFailure("Timeout", "호스트 연결 시간이 초과되었습니다.");
                        _clientConnectedTcs.TrySetResult(false);
                    }
                });

                bool connected = await _clientConnectedTcs.Task;

                if (generation != _sessionGeneration)
                    return false;

                Log($"[Relay] Client Connected Result={connected}");

                if (connected)
                {
                    ClearJoinFailure();
                    SetCurrentJoinCode(code);
                }
                else
                {
                    if (!HasJoinFailure)
                        SetJoinFailure("ClientDisconnected", "호스트와의 연결이 끊어졌습니다.");

                    SetCurrentJoinCode(string.Empty);
                }

                return connected;
            }
        }
        catch (Exception e)
        {
            if (generation != _sessionGeneration)
                return false;

            if (!HasJoinFailure)
                SetJoinFailure("RelayJoinFailed", "Relay 접속에 실패했습니다.");

            Debug.LogError($"[Relay] JoinRelay failed: {e}");
            SetCurrentJoinCode(string.Empty);
            return false;
        }
    }

    public async Task<bool> JoinViaCode(string joinCode)
    {
        return await JoinRelay(joinCode);
    }

    public void ResetSessionState(
        bool shutdownNetwork,
        bool clearJoinFailure = true)
    {
        _sessionGeneration++;

        if (IsWaitingForClientConnection())
            _clientConnectedTcs.TrySetResult(false);

        _clientConnectedTcs = null;
        if (clearJoinFailure)
            ClearJoinFailure();
        SetCurrentJoinCode(string.Empty);

        if (shutdownNetwork &&
            NetworkManager.Singleton != null &&
            NetworkManager.Singleton.IsListening)
        {
            NetworkManager.Singleton.Shutdown();
        }
    }

    private void ClearJoinFailure()
    {
        LastJoinFailureReason = string.Empty;
        LastJoinFailureMessage = string.Empty;
        LastJoinFailureStatusCode = 0;
    }

    private void SetJoinFailure(string reason, string message, int statusCode = 0)
    {
        LastJoinFailureReason = string.IsNullOrWhiteSpace(reason)
            ? "RelayJoinFailed"
            : reason;
        LastJoinFailureMessage = string.IsNullOrWhiteSpace(message)
            ? "Relay 접속에 실패했습니다."
            : message;
        LastJoinFailureStatusCode = statusCode;

        LogWarning($"[Relay] Join failure. reason={LastJoinFailureReason}, status={LastJoinFailureStatusCode}, message={LastJoinFailureMessage}");
    }

    private bool IsWaitingForClientConnection()
    {
        return _clientConnectedTcs != null && !_clientConnectedTcs.Task.IsCompleted;
    }

    private bool IsRelayJoinNotFound(RelayServiceException exception)
    {
        if (exception == null)
            return false;

        if (exception.ErrorCode == 404)
            return true;

        if (exception.Reason == RelayExceptionReason.JoinCodeNotFound ||
            exception.Reason == RelayExceptionReason.AllocationNotFound ||
            exception.Reason == RelayExceptionReason.EntityNotFound)
            return true;

        string message = exception.Message ?? string.Empty;
        return message.IndexOf("404", StringComparison.OrdinalIgnoreCase) >= 0 ||
               message.IndexOf("not found", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private int GetRelayFailureStatusCode(RequestFailedException exception)
    {
        if (exception == null)
            return 0;

        int errorCode = exception.ErrorCode;
        if (errorCode >= 100 && errorCode <= 599)
            return errorCode;

        int mappedHttpStatus = errorCode - (int)RelayExceptionReason.Min;
        if (mappedHttpStatus >= 100 && mappedHttpStatus <= 599)
            return mappedHttpStatus;

        if (exception is RelayServiceException relayException && IsRelayJoinNotFound(relayException))
            return 404;

        return errorCode;
    }

    private string MaskJoinCode(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
            return "len=0";

        string normalized = code.Trim().ToUpper();
        int length = normalized.Length;
        if (length <= 2)
            return $"len={length}, code=**";

        int visibleCount = length <= 4 ? 1 : 2;
        string prefix = normalized.Substring(0, visibleCount);
        string suffix = normalized.Substring(length - visibleCount, visibleCount);
        return $"len={length}, code={prefix}...{suffix}";
    }

    private void Log(string message)
    {
        if (!enableDebugLogs)
            return;

        Debug.Log(message, this);
    }

    private void LogWarning(string message)
    {
        if (!enableDebugLogs)
            return;

        Debug.LogWarning(message, this);
    }
}
