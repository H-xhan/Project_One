using Unity.Netcode;
using UnityEngine;

public class LobbyUIAutoToggle : MonoBehaviour
{
    [Tooltip("Host/Join/JoinCode UI 묶음")]
    [SerializeField] private GameObject lobbyPanel;

    [Tooltip("Ready UI 묶음(HudPanel 등)")]
    [SerializeField] private GameObject readyPanel;

    [Tooltip("게임 상태 매니저(비우면 자동 탐색)")]
    [SerializeField] private GameStateManager gameStateManager;

    [Tooltip("Ready 시스템(비우면 자동 탐색)")]
    [SerializeField] private ReadySystem readySystem;

    [Tooltip("ReadyPanel을 보여줄 최소 인원")]
    [SerializeField] private int minPlayersToShowReady = 2;

    private void Awake()
    {
        if (gameStateManager == null) gameStateManager = FindFirstObjectByType<GameStateManager>();
        if (readySystem == null) readySystem = FindFirstObjectByType<ReadySystem>();
    }

    private void Update()
    {
        var nm = NetworkManager.Singleton;

        bool connected = nm != null && nm.IsConnectedClient;
        bool isHost = nm != null && nm.IsHost;

        if (!connected)
        {
            if (lobbyPanel != null) lobbyPanel.SetActive(true);
            if (readyPanel != null) readyPanel.SetActive(false);
            return;
        }

        if (readyPanel == null || gameStateManager == null || readySystem == null)
        {
            if (lobbyPanel != null) lobbyPanel.SetActive(false);
            return;
        }

        bool isLobby = gameStateManager.GetState() == GameStateManager.GameState.Lobby;
        int clientCount = readySystem.GetConnectedClientCount();
        bool enoughPlayers = clientCount >= minPlayersToShowReady;

        bool showLobby = !connected || (isHost && isLobby && !enoughPlayers);
        if (lobbyPanel != null) lobbyPanel.SetActive(showLobby);

        readyPanel.SetActive(isLobby && enoughPlayers);
    }
}
