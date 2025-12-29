using TMPro;
using Unity.Services.Lobbies.Models;
using UnityEngine;
using UnityEngine.UI;

public class LobbyListSingleUI : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI lobbyNameText;
    [SerializeField] private TextMeshProUGUI playersText;
    [SerializeField] private Button joinButton;

    private Lobby _lobby;

    private void Awake()
    {
        joinButton.onClick.AddListener(() =>
        {
            // 이 버튼 누르면 매니저에게 "나 이 방 들어갈래!" 요청
            LobbyManager.Instance.JoinLobbyById(_lobby.Id);
        });
    }

    public void SetLobby(Lobby lobby)
    {
        _lobby = lobby;
        lobbyNameText.text = lobby.Name;
        playersText.text = $"{lobby.Players.Count}/{lobby.MaxPlayers}";
    }
}