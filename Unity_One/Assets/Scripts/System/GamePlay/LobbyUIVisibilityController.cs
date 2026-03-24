using UnityEngine;

public class LobbyUIVisibilityController : MonoBehaviour
{
    [SerializeField, Tooltip("GameStateManager 참조. 비어 있으면 자동 탐색합니다.")]
    private GameStateManager gameStateManager;

    [SerializeField, Tooltip("숨기고 보여줄 CanvasGroup. 비어 있으면 현재 오브젝트에서 자동 탐색합니다.")]
    private CanvasGroup targetCanvasGroup;

    [SerializeField, Tooltip("Countdown 상태에서도 UI를 계속 보여줄지 여부")]
    private bool showInCountdown = true;

    private bool _lastVisible = true;

    private void Awake()
    {
        ResolveRefs();
        RefreshVisibility(force: true);
    }

    private void Update()
    {
        RefreshVisibility(force: false);
    }

    private void ResolveRefs()
    {
        if (gameStateManager == null)
            gameStateManager = FindFirstObjectByType<GameStateManager>();

        if (targetCanvasGroup == null)
            targetCanvasGroup = GetComponent<CanvasGroup>();
    }

    private void RefreshVisibility(bool force)
    {
        ResolveRefs();

        if (gameStateManager == null || targetCanvasGroup == null)
            return;

        var state = gameStateManager.GetState();

        bool shouldShow =
            state == GameStateManager.GameState.Lobby ||
            (showInCountdown && state == GameStateManager.GameState.Countdown);

        if (!force && _lastVisible == shouldShow)
            return;

        _lastVisible = shouldShow;
        ApplyVisibility(shouldShow);
    }

    private void ApplyVisibility(bool visible)
    {
        targetCanvasGroup.alpha = visible ? 1f : 0f;
        targetCanvasGroup.interactable = visible;
        targetCanvasGroup.blocksRaycasts = visible;
    }
}