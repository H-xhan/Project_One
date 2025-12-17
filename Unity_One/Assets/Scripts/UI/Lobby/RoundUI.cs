using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class RoundUI : MonoBehaviour
{
    [SerializeField] private ReadySystem readySystem;
    [SerializeField] private GameStateManager gameStateManager;

    [Header("UI")]
    [SerializeField] private TMP_Text readyText;
    [SerializeField] private Button readyButton;

    private void Awake()
    {
        if (readySystem == null) readySystem = FindFirstObjectByType<ReadySystem>();
        if (gameStateManager == null) gameStateManager = FindFirstObjectByType<GameStateManager>();

        if (readyButton != null)
            readyButton.onClick.AddListener(OnClickReady);
    }

    private void Update()
    {
        if (readySystem == null || readyText == null) return;

        int ready = readySystem.GetReadyCount();
        int total = readySystem.GetConnectedClientCount();
        readyText.text = $"{ready}/{total} Ready";
    }

    private void OnClickReady()
    {
        if (readySystem == null) return;
        readySystem.ToggleLocalReady();
    }
}
