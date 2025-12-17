using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

public class NetworkSceneFlow : NetworkBehaviour
{
    [SerializeField] private GameStateManager gameStateManager;
    [SerializeField] private string lobbySceneName = "Lobby";
    [SerializeField] private string gameSceneName = "GamePlay";

    private GameStateManager.GameState _prev;

    private void Awake()
    {
        if (gameStateManager == null)
            gameStateManager = FindFirstObjectByType<GameStateManager>();
    }

    public override void OnNetworkSpawn()
    {
        if (gameStateManager != null)
            _prev = gameStateManager.GetState();
    }

    private void Update()
    {
        if (!IsServer) return;
        if (NetworkManager == null || NetworkManager.SceneManager == null) return;
        if (gameStateManager == null) return;

        var cur = gameStateManager.GetState();
        if (cur == _prev) return;

        _prev = cur;

        // Lobby면 Lobby, 그 외(Countdown/Playing/Results)는 GamePlay로 보냄
        string target = (cur == GameStateManager.GameState.Lobby) ? lobbySceneName : gameSceneName;

        if (SceneManager.GetActiveScene().name == target) return;

        NetworkManager.SceneManager.LoadScene(target, LoadSceneMode.Single);
    }
}
