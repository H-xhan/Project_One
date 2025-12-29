using TMPro;
using Unity.Services.Lobbies;
using Unity.Services.Lobbies.Models;
using UnityEngine;

public class RoomLobbyUI : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI lobbyCodeText;

    private void Start()
    {
        // 로비 매니저가 기억하고 있는 "현재 방" 정보를 가져옴
        Lobby currentLobby = LobbyManager.Instance.GetHostLobby();

        if (currentLobby != null)
        {
            lobbyCodeText.text = $"방 코드: {currentLobby.LobbyCode}";
        }
        else
        {
            lobbyCodeText.text = "방 정보를 불러올 수 없습니다.";
        }
    }
}