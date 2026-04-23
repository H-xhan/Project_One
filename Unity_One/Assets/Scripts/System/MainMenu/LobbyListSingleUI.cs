using TMPro;
using Unity.Services.Lobbies.Models;
using UnityEngine;
using UnityEngine.UI;

public class LobbyListSingleUI : MonoBehaviour
{
    [Tooltip("로비 목록에서 방 이름을 표시할 텍스트입니다.")]
    [SerializeField] private TextMeshProUGUI lobbyNameText;

    [Tooltip("로비 목록에서 현재 인원과 최대 인원을 표시할 텍스트입니다.")]
    [SerializeField] private TextMeshProUGUI playersText;

    [Tooltip("해당 로비에 참가할 때 누르는 버튼입니다.")]
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
