using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

public class RoundUI : MonoBehaviour
{
    [Tooltip("ReadySystem 참조(비우면 자동 탐색)")]
    [SerializeField] private ReadySystem readySystem;

    [Tooltip("GameStateManager 참조(비우면 자동 탐색)")]
    [SerializeField] private GameStateManager gameStateManager;

    [Tooltip("상태 텍스트")]
    [SerializeField] private TMP_Text stateText;

    [Tooltip("타이머 텍스트")]
    [SerializeField] private TMP_Text timerText;

    [Tooltip("레디 카운트 텍스트")]
    [SerializeField] private TMP_Text readyText;

    [Tooltip("Ready 버튼")]
    [SerializeField] private Button readyButton;

    private void Awake()
    {
        if (readySystem == null) readySystem = FindFirstObjectByType<ReadySystem>();
        if (gameStateManager == null) gameStateManager = FindFirstObjectByType<GameStateManager>();


        if (readyButton != null)
        {
            readyButton.onClick.AddListener(OnClickReady);
        }
    }

    private void Update()
    {
        if (gameStateManager != null && stateText != null)
        {
            var state = gameStateManager.GetState();
            stateText.text = $"State: {state}";
        }

        if (gameStateManager != null && timerText != null)
        {
            float t = gameStateManager.StateTimer.Value;
            timerText.text = $"Time: {Mathf.CeilToInt(t)}";
        }

        if (readySystem != null && readyText != null)
        {
            int r = readySystem.GetReadyCount();
            int c = readySystem.GetConnectedClientCount();
            readyText.text = $"Ready: {r}/{c}";
        }

        if (readySystem != null && readyButton != null && NetworkManager.Singleton != null)
        {
            bool isReady = readySystem.IsClientReady(NetworkManager.Singleton.LocalClientId);
            readyButton.GetComponentInChildren<TMP_Text>().text = isReady ? "Unready" : "Ready";
        }
    }

    private void OnClickReady()
    {
        if (readySystem == null) return;
        readySystem.ToggleLocalReady();
    }
}
