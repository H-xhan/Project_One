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

    private Lobby _hostLobby;
    private float _heartbeatTimer;

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
        await UnityServices.InitializeAsync();

        AuthenticationService.Instance.SignedIn += () =>
        {
            Debug.Log("로그인 성공! ID: " + AuthenticationService.Instance.PlayerId);
        };

        if (!AuthenticationService.Instance.IsSignedIn)
        {
            await AuthenticationService.Instance.SignInAnonymouslyAsync();
        }
    }

    private void Update()
    {
        HandleLobbyHeartbeat();
    }

    // 방 만들기 (Host)
    public async void CreateLobby(string lobbyName, int maxPlayers)
    {
        try
        {
            // Relay 코드 먼저 만들기 (StartHost까지 여기서 처리됨)
            string joinCode = await RelayManager.Instance.CreateRelay(maxPlayers);
            if (string.IsNullOrEmpty(joinCode)) return;

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

            Debug.Log($"방 생성 완료! LobbyCode: {lobby.LobbyCode}, RelayJoinCode: {joinCode}");

            // Host 생성 성공 -> RoomLobby로 즉시 이동
            TryLoadRoomLobbyForHost();
        }
        catch (LobbyServiceException e)
        {
            Debug.LogError("방 생성 실패: " + e);
        }
    }

    private void TryLoadRoomLobbyForHost()
    {
        var nm = NetworkManager.Singleton;
        if (nm == null)
        {
            Debug.LogWarning("[Lobby] NetworkManager가 없어 일반 SceneManager로 로드합니다.");
            SceneManager.LoadScene(roomLobbySceneName);
            return;
        }

        if (!nm.IsHost)
        {
            Debug.LogWarning("[Lobby] Host가 아니라서 RoomLobby 로드를 건너뜁니다.");
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
            Debug.LogWarning("[Lobby] Netcode SceneManager가 없어 일반 SceneManager로 로드합니다. (씬 동기화 안 될 수 있음)");
            SceneManager.LoadScene(roomLobbySceneName);
        }
    }

    // 방 목록 가져오기
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
            Debug.LogError("방 목록 로드 실패: " + e);
            return null;
        }
    }

    // 목록에서 선택한 LobbyId로 입장
    public async void JoinLobbyById(string lobbyId)
    {
        try
        {
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

            await JoinViaLobbyData(lobby);
        }
        catch (LobbyServiceException e)
        {
            Debug.LogError("방 입장 실패: " + e);
        }
    }

    // 코드(LobbyCode)로 로비 입장
    public async void JoinLobbyByCode(string lobbyCode)
    {
        try
        {
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

            Lobby lobby = await LobbyService.Instance.JoinLobbyByCodeAsync(lobbyCode, options);
            _hostLobby = lobby;

            await JoinViaLobbyData(lobby);
        }
        catch (LobbyServiceException e)
        {
            Debug.LogError("코드 입장 실패: " + e);
        }
    }

    private async Task JoinViaLobbyData(Lobby lobby)
    {
        if (lobby == null)
        {
            Debug.LogError("로비 정보가 없습니다!");
            return;
        }

        if (!lobby.Data.TryGetValue("JoinCode", out DataObject joinCodeData))
        {
            Debug.LogError("이 방에는 JoinCode가 없습니다!");
            return;
        }

        string joinCode = joinCodeData.Value;
        if (string.IsNullOrEmpty(joinCode))
        {
            Debug.LogError("JoinCode 값이 비어있습니다!");
            return;
        }

        Debug.Log($"[Lobby] Relay JoinCode={joinCode}");
        await RelayManager.Instance.JoinViaCode(joinCode);
    }

    private async void HandleLobbyHeartbeat()
    {
        if (_hostLobby != null)
        {
            _heartbeatTimer -= Time.deltaTime;
            if (_heartbeatTimer < 0f)
            {
                _heartbeatTimer = 15f;
                await LobbyService.Instance.SendHeartbeatPingAsync(_hostLobby.Id);
            }
        }
    }

    public Lobby GetHostLobby()
    {
        return _hostLobby;
    }
}
