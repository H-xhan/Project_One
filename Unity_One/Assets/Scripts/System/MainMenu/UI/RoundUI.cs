using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class RoundUI : MonoBehaviour
{
    [SerializeField, Tooltip("ReadySystem 참조")]
    private ReadySystem readySystem;

    [SerializeField, Tooltip("GameStateManager 참조")]
    private GameStateManager gameStateManager;

    [Header("UI")]
    [SerializeField, Tooltip("레디 현황 텍스트")]
    private TMP_Text readyText;

    [SerializeField, Tooltip("레디 버튼")]
    private Button readyButton;

    [SerializeField, Tooltip("방 코드를 표시할 텍스트")]
    private TMP_Text roomCodeText;

    private void Awake()
    {
        if (readySystem == null) readySystem = FindFirstObjectByType<ReadySystem>();
        if (gameStateManager == null) gameStateManager = FindFirstObjectByType<GameStateManager>();

        if (readyButton != null)
            readyButton.onClick.AddListener(OnClickReady);
    }

    private void Update()
    {
        UpdateReadyText();
        UpdateRoomCodeText();
    }

    private void UpdateReadyText()
    {
        if (readySystem == null || readyText == null)
            return;

        int ready = readySystem.GetReadyCount();
        int total = readySystem.GetConnectedClientCount();
        readyText.text = $"{ready}/{total} Ready";
    }

    private void UpdateRoomCodeText()
    {
        if (roomCodeText == null)
            return;

        string lobbyCode = "----";

        if (LobbyManager.Instance != null)
        {
            var lobby = LobbyManager.Instance.GetHostLobby();

            if (lobby != null && !string.IsNullOrWhiteSpace(lobby.LobbyCode))
                lobbyCode = lobby.LobbyCode;
        }

        roomCodeText.text = $"ROOM CODE : {lobbyCode}";
    }

    private void OnClickReady()
    {
        if (readySystem == null)
            return;

        readySystem.ToggleLocalReady();
    }
}