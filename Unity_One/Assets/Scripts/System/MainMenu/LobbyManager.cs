using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Lobbies;
using Unity.Services.Lobbies.Models;
using UnityEngine;

public class LobbyManager : MonoBehaviour
{
    public static LobbyManager Instance { get; private set; }

    private Lobby _hostLobby; // [중요] 이 변수는 여기에 있어야 합니다!
    private float _heartbeatTimer;
    private float _lobbyUpdateTimer;

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

    // [기능 1] 방 만들기
    public async void CreateLobby(string lobbyName, int maxPlayers)
    {
        try
        {
            // Relay 코드 먼저 만들기
            string joinCode = await RelayManager.Instance.CreateRelay(maxPlayers);
            if (joinCode == null) return;

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
                // Relay 코드를 로비 데이터에 숨김
                Data = new Dictionary<string, DataObject>
                {
                    { "JoinCode", new DataObject(DataObject.VisibilityOptions.Member, joinCode) }
                }
            };

            Lobby lobby = await LobbyService.Instance.CreateLobbyAsync(lobbyName, maxPlayers, options);
            _hostLobby = lobby;

            Debug.Log($"방 생성 완료! 코드: {lobby.LobbyCode}");
        }
        catch (LobbyServiceException e)
        {
            Debug.LogError("방 생성 실패: " + e);
        }
    }

    // [기능 2] 방 목록 가져오기
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

    // [기능 3] 방 ID로 입장 (여기에 코드가 있어야 _hostLobby를 찾을 수 있습니다!)
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

            if (lobby.Data.TryGetValue("JoinCode", out DataObject joinCodeData))
            {
                string joinCode = joinCodeData.Value;
                Debug.Log($"접속 코드 발견: {joinCode}");
                RelayManager.Instance.JoinRelay(joinCode);
            }
            else
            {
                Debug.LogError("이 방에는 조인 코드가 없습니다!");
            }
        }
        catch (LobbyServiceException e)
        {
            Debug.LogError("방 입장 실패: " + e);
        }
    }

    // [기능 4] 코드로 입장 (추가 기능)
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

            if (lobby.Data.TryGetValue("JoinCode", out DataObject joinCodeData))
            {
                string joinCode = joinCodeData.Value;
                RelayManager.Instance.JoinRelay(joinCode);
            }
        }
        catch (LobbyServiceException e)
        {
            Debug.LogError("코드 입장 실패: " + e);
        }
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