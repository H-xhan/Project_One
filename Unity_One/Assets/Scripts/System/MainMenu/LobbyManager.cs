using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Lobbies;
using Unity.Services.Lobbies.Models;
using UnityEngine;
using UnityEngine.SceneManagement;

public class LobbyManager : MonoBehaviour
{
    public static LobbyManager Instance { get; private set; }

    [Header("Scene Flow")]
    [Tooltip("Host 방 생성 완료 후 자동 이동할 씬 이름")]
    [SerializeField] private string roomLobbySceneName = "RoomLobby";

    [Tooltip("로비 생성/참가 흐름의 일반 디버그 로그 출력 여부입니다.")]
    [SerializeField] private bool enableDebugLogs = false;

    private Lobby _hostLobby;
    private float _heartbeatTimer;
    private bool _isLobbyOwner;
    private bool _servicesReady;
    private bool _isLobbyOperationInProgress;
    private string _currentLobbyOperationMessage = string.Empty;
    private string _lastLobbyOperationError = string.Empty;

    public bool IsLobbyOperationInProgress => _isLobbyOperationInProgress;
    public string CurrentLobbyOperationMessage => _currentLobbyOperationMessage;
    public string LastLobbyOperationError => _lastLobbyOperationError;

    public event Action<string> LobbyOperationStarted;
    public event Action<string> LobbyOperationSucceeded;
    public event Action<string> LobbyOperationFailed;

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

    private async void Start()
    {
        await EnsureServicesInitialized();
    }

    private async Task EnsureServicesInitialized()
    {
        if (_servicesReady)
            return;

        if (UnityServices.State != ServicesInitializationState.Initialized)
        {
#if UNITY_EDITOR
            string profileName = "Editor";
#else
            string profileName = "Build";
#endif
            InitializationOptions options = new InitializationOptions();
            options.SetProfile(profileName);

            await UnityServices.InitializeAsync(options);
            Log($"[Lobby] UGS Initialized. Profile={profileName}");
        }

        if (!AuthenticationService.Instance.IsSignedIn)
        {
            await AuthenticationService.Instance.SignInAnonymouslyAsync();
            Log($"[Lobby] Signed In. PlayerID={AuthenticationService.Instance.PlayerId}");
        }
        else
        {
            Log($"[Lobby] Already Signed In. PlayerID={AuthenticationService.Instance.PlayerId}");
        }

        _servicesReady = true;
    }

    private void Update()
    {
        HandleLobbyHeartbeat();
    }

    public async void CreateLobby(string lobbyName, int maxPlayers)
    {
        if (!CanStartLobbyOperation("[Lobby] Lobby operation already in progress. CreateLobby ignored."))
            return;

        BeginLobbyOperation("방을 생성하는 중...");

        try
        {
            await EnsureServicesInitialized();

            string joinCode = await RelayManager.Instance.CreateRelay(maxPlayers);
            if (string.IsNullOrEmpty(joinCode))
            {
                Debug.LogError("[Lobby] Relay 생성 실패");
                FailLobbyOperation("방 생성에 실패했습니다.");
                return;
            }

            CreateLobbyOptions options = new CreateLobbyOptions
            {
                IsPrivate = false,
                Player = new Player
                {
                    Data = new Dictionary<string, PlayerDataObject>
                    {
                        { "PlayerName", new PlayerDataObject(PlayerDataObject.VisibilityOptions.Member, "HostPlayer") }
                    }
                },
                Data = new Dictionary<string, DataObject>
                {
                    { "JoinCode", new DataObject(DataObject.VisibilityOptions.Member, joinCode) }
                }
            };

            Lobby lobby = await LobbyService.Instance.CreateLobbyAsync(lobbyName, maxPlayers, options);

            _hostLobby = lobby;
            _isLobbyOwner = true;

            Log($"[Lobby] 방 생성 완료! LobbyCode: {lobby.LobbyCode}, RelayJoinCode: {joinCode}");

            CompleteLobbyOperation("연결되었습니다.");
            TryLoadRoomLobbyForHost();
        }
        catch (LobbyServiceException e)
        {
            Debug.LogError("[Lobby] 방 생성 실패: " + e);
            FailLobbyOperation("방 생성에 실패했습니다.");
        }
        catch (Exception e)
        {
            Debug.LogError("[Lobby] 방 생성 실패: " + e);
            FailLobbyOperation("방 생성에 실패했습니다.");
        }
    }

    private void TryLoadRoomLobbyForHost()
    {
        var nm = NetworkManager.Singleton;
        if (nm == null)
        {
            LogWarning("[Lobby] NetworkManager가 없어 일반 SceneManager로 로드합니다.");
            SceneManager.LoadScene(roomLobbySceneName);
            return;
        }

        if (!nm.IsHost)
        {
            LogWarning("[Lobby] Host가 아니라서 RoomLobby 로드를 건너뜁니다.");
            return;
        }

        if (SceneManager.GetActiveScene().name == roomLobbySceneName)
            return;

        if (nm.SceneManager != null)
        {
            nm.SceneManager.LoadScene(roomLobbySceneName, LoadSceneMode.Single);
        }
        else
        {
            LogWarning("[Lobby] Netcode SceneManager가 없어 일반 SceneManager로 로드합니다.");
            SceneManager.LoadScene(roomLobbySceneName);
        }
    }

    public async Task<List<Lobby>> GetLobbies()
    {
        try
        {
            QueryLobbiesOptions options = new QueryLobbiesOptions
            {
                Count = 25,
                Filters = new List<QueryFilter>
                {
                    new QueryFilter(QueryFilter.FieldOptions.AvailableSlots, "0", QueryFilter.OpOptions.GT)
                },
                Order = new List<QueryOrder>
                {
                    new QueryOrder(true, QueryOrder.FieldOptions.Created)
                }
            };

            QueryResponse response = await LobbyService.Instance.QueryLobbiesAsync(options);
            return response.Results;
        }
        catch (LobbyServiceException e)
        {
            Debug.LogError("[Lobby] 방 목록 로드 실패: " + e);
            return null;
        }
    }

    public async void JoinLobbyById(string lobbyId)
    {
        if (!CanStartLobbyOperation("[Lobby] Lobby operation already in progress. JoinLobbyById ignored."))
            return;

        BeginLobbyOperation("방에 참가하는 중...");

        try
        {
            await EnsureServicesInitialized();

            if (string.IsNullOrWhiteSpace(lobbyId))
            {
                FailLobbyOperation("방 참가에 실패했습니다.");
                return;
            }

            JoinLobbyByIdOptions options = new JoinLobbyByIdOptions
            {
                Player = new Player
                {
                    Data = new Dictionary<string, PlayerDataObject>
                    {
                        { "PlayerName", new PlayerDataObject(PlayerDataObject.VisibilityOptions.Member, "GuestUser") }
                    }
                }
            };

            Lobby lobby = await LobbyService.Instance.JoinLobbyByIdAsync(lobbyId, options);

            _hostLobby = lobby;
            _isLobbyOwner = false;

            Log($"[Lobby] JoinLobbyById 성공. LobbyCode={lobby.LobbyCode}");

            await JoinViaLobbyData(lobby);
        }
        catch (LobbyServiceException e)
        {
            Debug.LogError("[Lobby] 방 입장 실패: " + e);
            FailLobbyOperation("방 참가에 실패했습니다.");
        }
        catch (Exception e)
        {
            Debug.LogError("[Lobby] 방 입장 실패: " + e);
            FailLobbyOperation("방 참가에 실패했습니다.");
        }
    }

    public async void JoinLobbyByCode(string lobbyCode)
    {
        if (!CanStartLobbyOperation("[Lobby] Lobby operation already in progress. JoinLobbyByCode ignored."))
            return;

        BeginLobbyOperation("방에 참가하는 중...");

        try
        {
            await EnsureServicesInitialized();

            string normalizedCode = NormalizeLobbyCode(lobbyCode);
            Log($"[Lobby] JoinLobbyByCode 시도. Raw={lobbyCode}, Normalized={normalizedCode}");

            if (string.IsNullOrEmpty(normalizedCode))
            {
                LogWarning("[Lobby] LobbyCode 입력값이 비어 있습니다.");
                FailLobbyOperation("참가 코드를 입력해주세요.");
                return;
            }

            if (normalizedCode.Length < 6)
            {
                LogWarning("[Lobby] LobbyCode 입력값이 너무 짧습니다.");
                FailLobbyOperation("참가 코드가 올바르지 않습니다.");
                return;
            }

            JoinLobbyByCodeOptions options = new JoinLobbyByCodeOptions
            {
                Player = new Player
                {
                    Data = new Dictionary<string, PlayerDataObject>
                    {
                        { "PlayerName", new PlayerDataObject(PlayerDataObject.VisibilityOptions.Member, "GuestUser") }
                    }
                }
            };

            Lobby lobby = await LobbyService.Instance.JoinLobbyByCodeAsync(normalizedCode, options);

            _hostLobby = lobby;
            _isLobbyOwner = false;

            Log($"[Lobby] JoinLobbyByCode 성공. LobbyCode={lobby.LobbyCode}");

            await JoinViaLobbyData(lobby);
        }
        catch (LobbyServiceException e)
        {
            Debug.LogError("[Lobby] 코드 입장 실패: " + e);
            FailLobbyOperation("방 참가에 실패했습니다.");
        }
        catch (Exception e)
        {
            Debug.LogError("[Lobby] 코드 입장 실패: " + e);
            FailLobbyOperation("방 참가에 실패했습니다.");
        }
    }

    private async Task JoinViaLobbyData(Lobby lobby)
    {
        BeginLobbyOperation("호스트에 연결하는 중...");

        if (lobby == null)
        {
            Debug.LogError("[Lobby] 로비 정보가 없습니다.");
            FailLobbyOperation("방 참가에 실패했습니다.");
            return;
        }

        if (!lobby.Data.TryGetValue("JoinCode", out DataObject joinCodeData))
        {
            Debug.LogError("[Lobby] 이 방에는 Relay JoinCode가 없습니다.");
            FailLobbyOperation("방 정보가 올바르지 않습니다.");
            return;
        }

        string joinCode = NormalizeRelayCode(joinCodeData.Value);
        if (string.IsNullOrEmpty(joinCode))
        {
            Debug.LogError("[Lobby] Relay JoinCode 값이 비어 있습니다.");
            FailLobbyOperation("방 정보가 올바르지 않습니다.");
            return;
        }

        Log($"[Lobby] Relay JoinCode={joinCode}");

        bool relayJoined = await RelayManager.Instance.JoinViaCode(joinCode);
        Log($"[Lobby] Relay Join Result={relayJoined}");

        if (!relayJoined)
        {
            Debug.LogError("[Lobby] Lobby 참가에는 성공했지만 Relay 접속에 실패했습니다.");
            FailLobbyOperation("호스트 연결에 실패했습니다.");
            return;
        }

        Log($"[Lobby] 최종 참가 성공. CurrentScene={SceneManager.GetActiveScene().name}");
        CompleteLobbyOperation("연결되었습니다.");
    }

    private void BeginLobbyOperation(string message)
    {
        _isLobbyOperationInProgress = true;
        _currentLobbyOperationMessage = message;
        _lastLobbyOperationError = string.Empty;

        LobbyOperationStarted?.Invoke(message);
    }

    private void CompleteLobbyOperation(string message)
    {
        _isLobbyOperationInProgress = false;
        _currentLobbyOperationMessage = string.Empty;
        _lastLobbyOperationError = string.Empty;

        LobbyOperationSucceeded?.Invoke(message);
    }

    private void FailLobbyOperation(string message)
    {
        _isLobbyOperationInProgress = false;
        _currentLobbyOperationMessage = string.Empty;
        _lastLobbyOperationError = message;

        LobbyOperationFailed?.Invoke(message);
    }

    private bool CanStartLobbyOperation(string failureMessage)
    {
        if (!_isLobbyOperationInProgress)
            return true;

        LogWarning(failureMessage);
        return false;
    }

    private string NormalizeLobbyCode(string code)
    {
        return string.IsNullOrWhiteSpace(code)
            ? string.Empty
            : code.Trim().ToUpper();
    }

    private string NormalizeRelayCode(string code)
    {
        return string.IsNullOrWhiteSpace(code)
            ? string.Empty
            : code.Trim().ToUpper();
    }

    private async void HandleLobbyHeartbeat()
    {
        if (!_isLobbyOwner || _hostLobby == null)
            return;

        _heartbeatTimer -= Time.deltaTime;
        if (_heartbeatTimer < 0f)
        {
            _heartbeatTimer = 15f;
            await LobbyService.Instance.SendHeartbeatPingAsync(_hostLobby.Id);
        }
    }

    public Lobby GetHostLobby()
    {
        return _hostLobby;
    }

    private void Log(string message)
    {
        if (!enableDebugLogs)
            return;

        Debug.Log(message);
    }

    private void LogWarning(string message)
    {
        if (!enableDebugLogs)
            return;

        Debug.LogWarning(message);
    }
}
