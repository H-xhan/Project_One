using TMPro;
using Unity.Services.Lobbies.Models;
using UnityEngine;

public class RoomLobbyUI : MonoBehaviour
{
    [Header("UI References")]
    [Tooltip("방 제목과 코드를 보여줄 텍스트 (TMP)")]
    [SerializeField] private TextMeshProUGUI roomInfoText;

    private void Start()
    {
        UpdateRoomInfo();
    }

    private void UpdateRoomInfo()
    {
        // 1. LobbyManager에게 "지금 내가 있는 방 정보(Lobby) 좀 줘" 라고 요청
        // (LobbyManager는 DontDestroyOnLoad라서 씬이 바뀌어도 정보가 남아있음)
        Lobby currentLobby = LobbyManager.Instance.GetHostLobby();

        if (currentLobby != null)
        {
            // 2. 방 정보가 있으면 텍스트 갱신
            // 형식 예시: 
            // 전설의 수달 파티
            // Code: ABC1234
            roomInfoText.text = $"{currentLobby.Name}\n<size=70%>(Code: {currentLobby.LobbyCode})</size>";

            Debug.Log($"[RoomLobbyUI] 방 정보 표시 성공: {currentLobby.Name} / {currentLobby.LobbyCode}");
        }
        else
        {
            // 3. 방 정보가 없을 때 (에디터에서 바로 RoomLobby 씬을 켰을 때 등)
            roomInfoText.text = "방 정보를 불러올 수 없습니다.";
            Debug.LogWarning("[RoomLobbyUI] 로비 정보를 찾을 수 없습니다. MainMenu에서 시작했나요?");
        }
    }
}