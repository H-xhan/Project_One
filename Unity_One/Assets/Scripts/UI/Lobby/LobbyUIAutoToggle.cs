using Unity.Netcode;
using UnityEngine;

public class LobbyUIAutoToggle : MonoBehaviour
{
    [Tooltip("Host/Join/JoinCode 등 로비 UI 묶음")]
    [SerializeField] private GameObject lobbyPanel;

    [Tooltip("Ready UI 묶음")]
    [SerializeField] private GameObject readyPanel;

    [Tooltip("라운드 상태 매니저(비우면 자동 탐색)")]
    [SerializeField] private GameStateManager gameStateManager;

    [Tooltip("Ready 시스템(비우면 자동 탐색)")]
    [SerializeField] private ReadySystem readySystem;

    [Tooltip("ReadyPanel을 보여줄 최소 인원")]
    [SerializeField] private int minPlayersToShowReady = 2;

    private void Awake()
    {
        if (gameStateManager == null)
            gameStateManager = FindFirstObjectByType<GameStateManager>();

        if (readySystem == null)
            readySystem = FindFirstObjectByType<ReadySystem>();

    }

    private void Update()
    {
        var nm = NetworkManager.Singleton;

        bool connected = nm != null && nm.IsConnectedClient;

        // 연결되면 로비 UI 자동 숨김
        if (lobbyPanel != null)
            lobbyPanel.SetActive(!connected);

        if (readyPanel == null || gameStateManager == null || readySystem == null)
            return;

        if (!connected)
        {
            readyPanel.SetActive(false);
            return;
        }

        bool isLobby = gameStateManager.GetState() == GameStateManager.GameState.Lobby;
        bool enoughPlayers = readySystem.GetConnectedClientCount() >= minPlayersToShowReady;

        // Lobby 상태 + 인원 2명 이상일 때만 Ready UI 표시
        readyPanel.SetActive(isLobby && enoughPlayers);
    }
}
