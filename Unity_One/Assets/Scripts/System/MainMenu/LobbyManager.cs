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
    private readonly RoomGameplaySettingsContext _roomSettingsContext =
        new RoomGameplaySettingsContext();
    private bool _networkCallbacksHooked;
    private int _operationGeneration;
    private int _activeOperationGeneration;
    private int _controlledNetworkShutdownDepth;

    public bool IsLobbyOperationInProgress => _isLobbyOperationInProgress;
    public string CurrentLobbyOperationMessage => _currentLobbyOperationMessage;
    public string LastLobbyOperationError => _lastLobbyOperationError;
    public bool HasCanonicalRoomSettings => TryGetCanonicalRoomSettings(out _);
    public RoomGameplaySettingsSnapshot CanonicalRoomSettings =>
        TryGetCanonicalRoomSettings(out RoomGameplaySettingsSnapshot snapshot)
            ? snapshot
            : RoomGameplaySettingsValidator.CreateDefaultSnapshot();
    public RoomGameplaySettingsSource CurrentRoomSettingsSource =>
        _roomSettingsContext.Source;

    public event Action<string> LobbyOperationStarted;
    public event Action<string> LobbyOperationSucceeded;
    public event Action<string> LobbyOperationFailed;
    public event Action<RoomGameplaySettingsSnapshot> CanonicalRoomSettingsChanged;

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
            SceneManager.sceneLoaded += HandleSceneLoaded;
            HookNetworkCallbacks();
        }
        else
        {
            Destroy(gameObject);
        }
    }

    private async void Start()
    {
        HookNetworkCallbacks();
        await EnsureServicesInitialized();
    }

    private void OnDestroy()
    {
        if (Instance != this)
            return;

        SceneManager.sceneLoaded -= HandleSceneLoaded;
        UnhookNetworkCallbacks();
        Instance = null;
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

    public void CreateLobby(string lobbyName, int maxPlayers)
    {
        RoomGameplaySettingsDraft draft = new RoomGameplaySettingsDraft();
        draft.Core.MaxPlayers = maxPlayers;
        CreateLobby(lobbyName, draft.Freeze());
    }

    public async void CreateLobby(
        string lobbyName,
        RoomGameplaySettingsSnapshot requestedSettings)
    {
        if (!CanStartLobbyOperation("[Lobby] Lobby operation already in progress. CreateLobby ignored."))
            return;

        int operationGeneration = ++_operationGeneration;
        BeginLobbyOperation("방을 생성하는 중...");

        RoomGameplaySettingsSnapshot frozenSettings =
            RoomGameplaySettingsValidator.CreateSnapshot(
                requestedSettings?.Core.GameModeId,
                requestedSettings?.Core.MapId,
                requestedSettings?.Core.MaxPlayers ?? RoomGameplaySettingsValidator.CurrentMaxPlayers,
                requestedSettings?.PostItLiar.PromptSourceMode ??
                    PostItLiarPromptSourceMode.PresetDatabase);

        try
        {
            await EnsureServicesInitialized();
            if (!IsOperationCurrent(operationGeneration))
            {
                EndCancelledLobbyOperation(operationGeneration);
                return;
            }

            await ResetCurrentSessionAsync("create_start", true, true, false);
            if (!IsOperationCurrent(operationGeneration))
            {
                EndCancelledLobbyOperation(operationGeneration);
                return;
            }

            string joinCode = await RelayManager.Instance.CreateRelay(frozenSettings.Core.MaxPlayers);
            if (!IsOperationCurrent(operationGeneration))
            {
                RelayManager.Instance.ResetSessionState(true);
                EndCancelledLobbyOperation(operationGeneration);
                return;
            }

            if (string.IsNullOrEmpty(joinCode))
            {
                Debug.LogError("[Lobby] Relay 생성 실패");
                await ResetCurrentSessionAsync("create_relay_failed", true, true, false);
                FailLobbyOperation(operationGeneration, "방 생성에 실패했습니다.");
                return;
            }

            Dictionary<string, DataObject> lobbyData =
                new Dictionary<string, DataObject>
                {
                    { "JoinCode", new DataObject(DataObject.VisibilityOptions.Member, joinCode) }
                };

            foreach (KeyValuePair<string, string> entry in
                     RoomGameplaySettingsCodec.Serialize(frozenSettings))
            {
                lobbyData[entry.Key] =
                    new DataObject(DataObject.VisibilityOptions.Public, entry.Value);
            }

            CreateLobbyOptions options = new CreateLobbyOptions
            {
                IsPrivate = frozenSettings.PostItLiar.PromptSourceMode ==
                            PostItLiarPromptSourceMode.CitizenAuthor,
                Player = new Player
                {
                    Data = new Dictionary<string, PlayerDataObject>
                    {
                        { "PlayerName", new PlayerDataObject(PlayerDataObject.VisibilityOptions.Member, "HostPlayer") }
                    }
                },
                Data = lobbyData
            };

            Lobby lobby = await LobbyService.Instance.CreateLobbyAsync(
                lobbyName,
                frozenSettings.Core.MaxPlayers,
                options);

            if (!IsOperationCurrent(operationGeneration))
            {
                await ReleaseLobbyMembershipAsync(lobby, true, "stale_create");
                RelayManager.Instance.ResetSessionState(true);
                EndCancelledLobbyOperation(operationGeneration);
                return;
            }

            _hostLobby = lobby;
            _isLobbyOwner = true;
            BindCanonicalRoomSettings(
                lobby.Id,
                frozenSettings,
                RoomGameplaySettingsSource.HostCreated);

            if (!IsOperationCurrent(operationGeneration))
            {
                EndCancelledLobbyOperation(operationGeneration);
                return;
            }

            Log($"[Lobby] 방 생성 완료! LobbyCode: {lobby.LobbyCode}, RelayJoinCode: {joinCode}");

            CompleteLobbyOperation(operationGeneration, "연결되었습니다.");
            TryLoadRoomLobbyForHost();
        }
        catch (LobbyServiceException e)
        {
            if (!IsOperationCurrent(operationGeneration))
            {
                EndCancelledLobbyOperation(operationGeneration);
                return;
            }

            Debug.LogError("[Lobby] 방 생성 실패: " + e);
            await ResetCurrentSessionAsync("create_lobby_failed", true, true, false);
            FailLobbyOperation(operationGeneration, "방 생성에 실패했습니다.");
        }
        catch (Exception e)
        {
            if (!IsOperationCurrent(operationGeneration))
            {
                EndCancelledLobbyOperation(operationGeneration);
                return;
            }

            Debug.LogError("[Lobby] 방 생성 실패: " + e);
            await ResetCurrentSessionAsync("create_failed", true, true, false);
            FailLobbyOperation(operationGeneration, "방 생성에 실패했습니다.");
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

        int operationGeneration = ++_operationGeneration;
        BeginLobbyOperation("방에 참가하는 중...");

        try
        {
            await EnsureServicesInitialized();
            if (!IsOperationCurrent(operationGeneration))
            {
                EndCancelledLobbyOperation(operationGeneration);
                return;
            }

            await ResetCurrentSessionAsync("join_by_id_start", true, true, false);
            if (!IsOperationCurrent(operationGeneration))
            {
                EndCancelledLobbyOperation(operationGeneration);
                return;
            }

            if (string.IsNullOrWhiteSpace(lobbyId))
            {
                FailLobbyOperation(operationGeneration, "방 참가에 실패했습니다.");
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

            if (!IsOperationCurrent(operationGeneration))
            {
                await ReleaseLobbyMembershipAsync(lobby, false, "stale_join_by_id");
                EndCancelledLobbyOperation(operationGeneration);
                return;
            }

            _hostLobby = lobby;
            _isLobbyOwner = false;

            Log($"[Lobby] JoinLobbyById 성공. LobbyCode={lobby.LobbyCode}");

            await JoinViaLobbyData(lobby, operationGeneration);
        }
        catch (LobbyServiceException e)
        {
            if (!IsOperationCurrent(operationGeneration))
            {
                EndCancelledLobbyOperation(operationGeneration);
                return;
            }

            Debug.LogError("[Lobby] 방 입장 실패: " + e);
            await ResetCurrentSessionAsync("join_by_id_failed", true, true, false);
            FailLobbyOperation(operationGeneration, "방 참가에 실패했습니다.");
        }
        catch (Exception e)
        {
            if (!IsOperationCurrent(operationGeneration))
            {
                EndCancelledLobbyOperation(operationGeneration);
                return;
            }

            Debug.LogError("[Lobby] 방 입장 실패: " + e);
            await ResetCurrentSessionAsync("join_by_id_failed", true, true, false);
            FailLobbyOperation(operationGeneration, "방 참가에 실패했습니다.");
        }
    }

    public async void JoinLobbyByCode(string lobbyCode)
    {
        if (!CanStartLobbyOperation("[Lobby] Lobby operation already in progress. JoinLobbyByCode ignored."))
            return;

        int operationGeneration = ++_operationGeneration;
        BeginLobbyOperation("방에 참가하는 중...");

        try
        {
            await EnsureServicesInitialized();
            if (!IsOperationCurrent(operationGeneration))
            {
                EndCancelledLobbyOperation(operationGeneration);
                return;
            }

            await ResetCurrentSessionAsync("join_by_code_start", true, true, false);
            if (!IsOperationCurrent(operationGeneration))
            {
                EndCancelledLobbyOperation(operationGeneration);
                return;
            }

            string normalizedCode = NormalizeLobbyCode(lobbyCode);
            Log($"[Lobby] JoinLobbyByCode 시도. Raw={lobbyCode}, Normalized={normalizedCode}");

            if (string.IsNullOrEmpty(normalizedCode))
            {
                LogWarning("[Lobby] LobbyCode 입력값이 비어 있습니다.");
                FailLobbyOperation(operationGeneration, "참가 코드를 입력해주세요.");
                return;
            }

            if (normalizedCode.Length < 6)
            {
                LogWarning("[Lobby] LobbyCode 입력값이 너무 짧습니다.");
                FailLobbyOperation(operationGeneration, "참가 코드가 올바르지 않습니다.");
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

            if (!IsOperationCurrent(operationGeneration))
            {
                await ReleaseLobbyMembershipAsync(lobby, false, "stale_join_by_code");
                EndCancelledLobbyOperation(operationGeneration);
                return;
            }

            _hostLobby = lobby;
            _isLobbyOwner = false;

            Log($"[Lobby] JoinLobbyByCode 성공. LobbyCode={lobby.LobbyCode}");

            await JoinViaLobbyData(lobby, operationGeneration);
        }
        catch (LobbyServiceException e)
        {
            if (!IsOperationCurrent(operationGeneration))
            {
                EndCancelledLobbyOperation(operationGeneration);
                return;
            }

            Debug.LogError("[Lobby] 코드 입장 실패: " + e);
            await ResetCurrentSessionAsync("join_by_code_failed", true, true, false);
            FailLobbyOperation(operationGeneration, "방 참가에 실패했습니다.");
        }
        catch (Exception e)
        {
            if (!IsOperationCurrent(operationGeneration))
            {
                EndCancelledLobbyOperation(operationGeneration);
                return;
            }

            Debug.LogError("[Lobby] 코드 입장 실패: " + e);
            await ResetCurrentSessionAsync("join_by_code_failed", true, true, false);
            FailLobbyOperation(operationGeneration, "방 참가에 실패했습니다.");
        }
    }

    private async Task JoinViaLobbyData(Lobby lobby, int operationGeneration)
    {
        BeginLobbyOperation("호스트에 연결하는 중...");

        if (!IsOperationCurrent(operationGeneration))
        {
            EndCancelledLobbyOperation(operationGeneration);
            return;
        }

        if (lobby == null)
        {
            Debug.LogError("[Lobby] 로비 정보가 없습니다.");
            await ResetCurrentSessionAsync("join_missing_lobby", true, true, false);
            FailLobbyOperation(operationGeneration, "방 참가에 실패했습니다.");
            return;
        }

        if (lobby.Data == null ||
            !lobby.Data.TryGetValue("JoinCode", out DataObject joinCodeData))
        {
            Debug.LogError("[Lobby] 이 방에는 Relay JoinCode가 없습니다.");
            await ResetCurrentSessionAsync("join_missing_relay_code", true, true, false);
            FailLobbyOperation(operationGeneration, "방 정보가 올바르지 않습니다.");
            return;
        }

        string joinCode = NormalizeRelayCode(joinCodeData.Value);
        if (string.IsNullOrEmpty(joinCode))
        {
            Debug.LogError("[Lobby] Relay JoinCode 값이 비어 있습니다.");
            await ResetCurrentSessionAsync("join_empty_relay_code", true, true, false);
            FailLobbyOperation(operationGeneration, "방 정보가 올바르지 않습니다.");
            return;
        }

        RoomGameplaySettingsSnapshot joinedSettings =
            RoomGameplaySettingsCodec.Deserialize(ReadPublicRoomSettingsMetadata(lobby));
        BindCanonicalRoomSettings(
            lobby.Id,
            joinedSettings,
            RoomGameplaySettingsSource.Joined);

        if (!IsOperationCurrent(operationGeneration))
        {
            EndCancelledLobbyOperation(operationGeneration);
            return;
        }

        Log($"[Lobby] Relay JoinCode={joinCode}");

        bool relayJoined = await RelayManager.Instance.JoinViaCode(joinCode);
        if (!IsOperationCurrent(operationGeneration))
        {
            EndCancelledLobbyOperation(operationGeneration);
            return;
        }

        Log($"[Lobby] Relay Join Result={relayJoined}");

        if (!relayJoined)
        {
            Debug.LogError("[Lobby] Lobby 참가에는 성공했지만 Relay 접속에 실패했습니다.");
            await ResetCurrentSessionAsync(
                "join_relay_failed",
                true,
                true,
                false,
                true);
            FailLobbyOperation(operationGeneration, "호스트 연결에 실패했습니다.");
            return;
        }

        Log($"[Lobby] 최종 참가 성공. CurrentScene={SceneManager.GetActiveScene().name}");
        CompleteLobbyOperation(operationGeneration, "연결되었습니다.");
    }

    private void BeginLobbyOperation(string message)
    {
        _activeOperationGeneration = _operationGeneration;
        _isLobbyOperationInProgress = true;
        _currentLobbyOperationMessage = message;
        _lastLobbyOperationError = string.Empty;

        NotifyLobbyOperation(LobbyOperationStarted, message);
    }

    private void CompleteLobbyOperation(int operationGeneration, string message)
    {
        if (!_isLobbyOperationInProgress ||
            _activeOperationGeneration != operationGeneration)
        {
            return;
        }

        ClearLobbyOperationState();

        NotifyLobbyOperation(LobbyOperationSucceeded, message);
    }

    private void FailLobbyOperation(int operationGeneration, string message)
    {
        if (!_isLobbyOperationInProgress ||
            _activeOperationGeneration != operationGeneration)
        {
            return;
        }

        ClearLobbyOperationState();
        _lastLobbyOperationError = message;

        NotifyLobbyOperation(LobbyOperationFailed, message);
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

    public bool TryGetCanonicalRoomSettings(
        out RoomGameplaySettingsSnapshot snapshot)
    {
        if (_hostLobby != null &&
            _roomSettingsContext.TryGetSnapshotForLobby(_hostLobby.Id, out snapshot))
        {
            return true;
        }

        snapshot = null;
        return false;
    }

    public Task LeaveCurrentLobbyAsync()
    {
        return ResetCurrentSessionAsync("leave", true, true);
    }

    private void BindCanonicalRoomSettings(
        string lobbyId,
        RoomGameplaySettingsSnapshot snapshot,
        RoomGameplaySettingsSource source)
    {
        _roomSettingsContext.Bind(lobbyId, snapshot, source);
        NotifyCanonicalRoomSettingsChanged(_roomSettingsContext.CanonicalSnapshot);
    }

    private IReadOnlyDictionary<string, string> ReadPublicRoomSettingsMetadata(
        Lobby lobby)
    {
        Dictionary<string, string> metadata =
            new Dictionary<string, string>(StringComparer.Ordinal);

        if (lobby?.Data == null)
            return metadata;

        foreach (KeyValuePair<string, DataObject> entry in lobby.Data)
        {
            if (entry.Value == null)
                continue;

            metadata[entry.Key] = entry.Value.Value;
        }

        return metadata;
    }

    private async Task ResetCurrentSessionAsync(
        string reason,
        bool releaseLobbyMembership,
        bool shutdownNetwork,
        bool invalidateOperation = true,
        bool preserveRelayFailure = false)
    {
        if (invalidateOperation)
        {
            _operationGeneration++;

            if (_isLobbyOperationInProgress)
                ClearLobbyOperationState();
        }

        Lobby previousLobby = _hostLobby;
        bool previousOwner = _isLobbyOwner;

        _hostLobby = null;
        _isLobbyOwner = false;
        _heartbeatTimer = 0f;
        _roomSettingsContext.Reset(reason);

        if (shutdownNetwork)
            _controlledNetworkShutdownDepth++;

        try
        {
            if (RelayManager.Instance != null)
            {
                RelayManager.Instance.ResetSessionState(
                    shutdownNetwork,
                    !preserveRelayFailure);
            }

            if (shutdownNetwork)
                await WaitForNetworkShutdownAsync();
        }
        finally
        {
            if (shutdownNetwork)
                _controlledNetworkShutdownDepth--;
        }

        if (releaseLobbyMembership && previousLobby != null)
        {
            await ReleaseLobbyMembershipAsync(previousLobby, previousOwner, reason);
        }

        NotifyCanonicalRoomSettingsChanged(
            RoomGameplaySettingsValidator.CreateDefaultSnapshot());
    }

    private async Task ReleaseLobbyMembershipAsync(
        Lobby lobby,
        bool isOwner,
        string reason)
    {
        if (lobby == null ||
            UnityServices.State != ServicesInitializationState.Initialized ||
            !AuthenticationService.Instance.IsSignedIn)
        {
            return;
        }

        try
        {
            if (isOwner)
            {
                await LobbyService.Instance.DeleteLobbyAsync(lobby.Id);
            }
            else
            {
                await LobbyService.Instance.RemovePlayerAsync(
                    lobby.Id,
                    AuthenticationService.Instance.PlayerId);
            }
        }
        catch (LobbyServiceException exception)
        {
            LogWarning($"[Lobby] Session cleanup failed. reason={reason}, exception={exception}");
        }
    }

    private bool IsOperationCurrent(int generation)
    {
        return generation == _operationGeneration;
    }

    private void EndCancelledLobbyOperation(int operationGeneration)
    {
        if (!_isLobbyOperationInProgress ||
            _activeOperationGeneration != operationGeneration)
        {
            return;
        }

        ClearLobbyOperationState();
    }

    private void ClearLobbyOperationState()
    {
        _isLobbyOperationInProgress = false;
        _activeOperationGeneration = 0;
        _currentLobbyOperationMessage = string.Empty;
        _lastLobbyOperationError = string.Empty;
    }

    private void NotifyCanonicalRoomSettingsChanged(
        RoomGameplaySettingsSnapshot snapshot)
    {
        Action<RoomGameplaySettingsSnapshot> handlers =
            CanonicalRoomSettingsChanged;
        if (handlers == null)
            return;

        foreach (Delegate callback in handlers.GetInvocationList())
        {
            try
            {
                ((Action<RoomGameplaySettingsSnapshot>)callback).Invoke(snapshot);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, this);
            }
        }
    }

    private void NotifyLobbyOperation(Action<string> handlers, string message)
    {
        if (handlers == null)
            return;

        foreach (Delegate callback in handlers.GetInvocationList())
        {
            try
            {
                ((Action<string>)callback).Invoke(message);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, this);
            }
        }
    }

    private async Task WaitForNetworkShutdownAsync()
    {
        NetworkManager networkManager = NetworkManager.Singleton;
        if (networkManager == null || !networkManager.IsListening)
            return;

        float deadline = Time.realtimeSinceStartup + 2f;
        while (networkManager.IsListening && Time.realtimeSinceStartup < deadline)
            await Task.Yield();

        if (networkManager.IsListening)
            LogWarning("[Lobby] Network shutdown did not finish before the retry timeout.");
    }

    private void HookNetworkCallbacks()
    {
        if (_networkCallbacksHooked || NetworkManager.Singleton == null)
            return;

        NetworkManager.Singleton.OnClientStopped += HandleNetworkStopped;
        NetworkManager.Singleton.OnServerStopped += HandleNetworkStopped;
        _networkCallbacksHooked = true;
    }

    private void UnhookNetworkCallbacks()
    {
        if (!_networkCallbacksHooked || NetworkManager.Singleton == null)
            return;

        NetworkManager.Singleton.OnClientStopped -= HandleNetworkStopped;
        NetworkManager.Singleton.OnServerStopped -= HandleNetworkStopped;
        _networkCallbacksHooked = false;
    }

    private void HandleNetworkStopped(bool wasHost)
    {
        _ = wasHost;

        if (_controlledNetworkShutdownDepth > 0)
            return;

        if (_hostLobby == null &&
            !_roomSettingsContext.HasSnapshot &&
            !_isLobbyOperationInProgress)
            return;

        _ = ResetCurrentSessionAsync("network_shutdown", true, false);
    }

    private void HandleSceneLoaded(Scene scene, LoadSceneMode loadSceneMode)
    {
        _ = loadSceneMode;

        if (!string.Equals(scene.name, "MainMenu", StringComparison.Ordinal) ||
            (_hostLobby == null &&
             !_roomSettingsContext.HasSnapshot &&
             !_isLobbyOperationInProgress))
        {
            return;
        }

        _ = ResetCurrentSessionAsync("main_menu_return", true, true);
    }

    private async void HandleLobbyHeartbeat()
    {
        if (!_isLobbyOwner || _hostLobby == null)
            return;

        _heartbeatTimer -= Time.deltaTime;
        if (_heartbeatTimer < 0f)
        {
            _heartbeatTimer = 15f;
            Lobby heartbeatLobby = _hostLobby;
            try
            {
                await LobbyService.Instance.SendHeartbeatPingAsync(heartbeatLobby.Id);
            }
            catch (LobbyServiceException exception)
            {
                if (_hostLobby != heartbeatLobby)
                    return;

                LogWarning($"[Lobby] Heartbeat failed: {exception}");
            }
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
