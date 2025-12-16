using System;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Networking.Transport.Relay;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
using UnityEngine;
using TMPro;

public class RelayUIController : MonoBehaviour
{
    [Header("UI")]
    [Tooltip("현재 생성된 Join Code를 보여줄 텍스트")]
    [SerializeField] private TMP_Text joinCodeText;

    [Tooltip("Join Code 입력 필드")]
    [SerializeField] private TMP_InputField joinCodeInput;

    [Header("Relay")]
    [Tooltip("최대 접속 인원(호스트 포함)")]
    [SerializeField] private int maxConnections = 4;

    [Tooltip("연결 상태/에러 로그를 콘솔에 출력할지")]
    [SerializeField] private bool verboseLog = true;

    private bool _servicesReady;

    private async void Awake()
    {
        await EnsureServicesReady();
    }

    public async void Host()
    {
        await EnsureServicesReady();

        try
        {
            Allocation allocation = await RelayService.Instance.CreateAllocationAsync(maxConnections - 1);
            string joinCode = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);

            var transport = GetTransport();
            RelayServerData relayServerData = AllocationUtils.ToRelayServerData(allocation, "dtls");
            transport.SetRelayServerData(relayServerData);

            NetworkManager.Singleton.StartHost();

            if (joinCodeText != null) joinCodeText.text = joinCode;
            if (verboseLog) Debug.Log($"[Relay] Host started. JoinCode={joinCode}");
        }
        catch (Exception e)
        {
            Debug.LogError($"[Relay] Host failed: {e}");
        }
    }

    public async void Join()
    {
        Debug.Log("[Relay] Join button clicked");

        await EnsureServicesReady();

        string code = joinCodeInput != null ? joinCodeInput.text.Trim() : string.Empty;
        if (string.IsNullOrEmpty(code))
        {
            Debug.LogWarning("[Relay] JoinCode is empty.");
            return;
        }

        try
        {
            JoinAllocation joinAllocation = await RelayService.Instance.JoinAllocationAsync(code);

            var transport = GetTransport();
            RelayServerData relayServerData = AllocationUtils.ToRelayServerData(joinAllocation, "dtls");
            transport.SetRelayServerData(relayServerData);

            NetworkManager.Singleton.StartClient();

            if (verboseLog) Debug.Log($"[Relay] Client started. JoinCode={code}");
        }
        catch (Exception e)
        {
            Debug.LogError($"[Relay] Join failed: {e}");
        }
    }

    private UnityTransport GetTransport()
    {
        var transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
        if (transport == null)
        {
            throw new InvalidOperationException("UnityTransport component is missing on NetworkManager.");
        }
        return transport;
    }

    private async Task EnsureServicesReady()
    {
        if (_servicesReady) return;

        await UnityServices.InitializeAsync();

        if (!AuthenticationService.Instance.IsSignedIn)
        {
            await AuthenticationService.Instance.SignInAnonymouslyAsync();
        }

        _servicesReady = true;
        if (verboseLog) Debug.Log("[Relay] UGS initialized & signed in.");
    }
}
