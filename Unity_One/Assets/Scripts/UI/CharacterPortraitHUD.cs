using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

public class CharacterPortraitHUD : MonoBehaviour
{
    [Header("Presentation")]
    [SerializeField] private CanvasGroup canvasGroup;
    [SerializeField] private RawImage expressionImage;
    [SerializeField] private float rebindInterval = 0.25f;

    [Header("State")]
    [SerializeField] private GameStateManager gameStateManager;

    [Header("Visibility")]
    [SerializeField] private bool showOnlyDuringPlaying = true;
    [SerializeField] private bool hideWhenEliminated = true;

    [Header("Debug")]
    [SerializeField] private bool debugLogs = false;

    private NetworkObject _boundPlayerObject;
    private FaceExpressionController _boundFaceController;
    private PlayerStatusModule _boundStatusModule;
    private float _nextBindAttemptTime;
    private int _displayedExpressionId = -1;
    private Texture _displayedAtlas;
    private bool _hasValidExpression;
    private bool _visibilityInitialized;
    private bool _isVisible;
    private bool _loggedWaitingForLocalPlayer;

    private void Awake()
    {
        ResolvePresentationReferences();
        SetVisible(false);
    }

    private void OnEnable()
    {
        ResolvePresentationReferences();
        _nextBindAttemptTime = 0f;
        TryBindLocalPlayer();
        RefreshVisibility();
    }

    private void OnDisable()
    {
        ClearBinding();
    }

    private void OnDestroy()
    {
        ClearBinding();
    }

    private void Update()
    {
        if (_boundPlayerObject != null && !IsBoundLocalOwnerValid())
            ClearBinding();

        if (NeedsBinding() && Time.unscaledTime >= _nextBindAttemptTime)
            TryBindLocalPlayer();

        RefreshVisibility();
    }

    public void ForceRebind()
    {
        ClearBinding();
        gameStateManager = null;
        _nextBindAttemptTime = 0f;
        _loggedWaitingForLocalPlayer = false;
        TryBindLocalPlayer();
        RefreshVisibility();
    }

    private void ResolvePresentationReferences()
    {
        if (canvasGroup == null)
            canvasGroup = GetComponent<CanvasGroup>();

        if (expressionImage == null)
            expressionImage = GetComponentInChildren<RawImage>(true);
    }

    private bool NeedsBinding()
    {
        return _boundPlayerObject == null ||
               _boundFaceController == null ||
               _boundStatusModule == null ||
               gameStateManager == null;
    }

    private void TryBindLocalPlayer()
    {
        _nextBindAttemptTime =
            Time.unscaledTime + Mathf.Max(0f, rebindInterval);

        ResolveGameStateManager();

        NetworkObject playerObject = ResolveLocalPlayerObject();
        if (playerObject == null)
        {
            if (_boundPlayerObject != null)
                ClearBinding();

            LogWaitingForLocalPlayer();
            RefreshVisibility();
            return;
        }

        FaceExpressionController faceController =
            playerObject.GetComponentInChildren<FaceExpressionController>(true);
        PlayerStatusModule statusModule =
            playerObject.GetComponentInChildren<PlayerStatusModule>(true);

        if (!IsSamePlayerRoot(playerObject, faceController) ||
            !IsSamePlayerRoot(playerObject, statusModule))
        {
            if (_boundPlayerObject != null)
                ClearBinding();

            LogWaitingForLocalPlayer();
            RefreshVisibility();
            return;
        }

        Bind(playerObject, faceController, statusModule);
    }

    private void ResolveGameStateManager()
    {
        if (gameStateManager == null)
            gameStateManager = FindFirstObjectByType<GameStateManager>();
    }

    private NetworkObject ResolveLocalPlayerObject()
    {
        NetworkManager networkManager = NetworkManager.Singleton;
        if (networkManager == null || !networkManager.IsListening)
            return null;

        NetworkClient localClient = networkManager.LocalClient;
        NetworkObject playerObject =
            localClient != null ? localClient.PlayerObject : null;

        if (playerObject == null ||
            !playerObject.IsSpawned ||
            playerObject.OwnerClientId != networkManager.LocalClientId)
        {
            return null;
        }

        return playerObject;
    }

    private void Bind(
        NetworkObject playerObject,
        FaceExpressionController faceController,
        PlayerStatusModule statusModule)
    {
        bool bindingUnchanged =
            _boundPlayerObject == playerObject &&
            _boundFaceController == faceController &&
            _boundStatusModule == statusModule;

        if (!bindingUnchanged)
        {
            ClearBinding();

            _boundPlayerObject = playerObject;
            _boundFaceController = faceController;
            _boundStatusModule = statusModule;
            _boundFaceController.ExpressionChanged +=
                HandleExpressionChanged;

            Log("Local owner bound.");
        }

        _loggedWaitingForLocalPlayer = false;
        ApplyExpression(_boundFaceController.CurrentExpressionId);
        RefreshVisibility();
    }

    private void ClearBinding()
    {
        if (_boundFaceController != null)
        {
            _boundFaceController.ExpressionChanged -=
                HandleExpressionChanged;
        }

        _boundPlayerObject = null;
        _boundFaceController = null;
        _boundStatusModule = null;
        _displayedExpressionId = -1;
        _displayedAtlas = null;
        _hasValidExpression = false;

        if (expressionImage != null)
            expressionImage.texture = null;

        SetVisible(false);
    }

    private void HandleExpressionChanged(int expressionId)
    {
        ApplyExpression(expressionId);
        RefreshVisibility();
    }

    private void ApplyExpression(int expressionId)
    {
        if (_boundFaceController == null ||
            expressionImage == null ||
            expressionId < 0)
        {
            InvalidateExpression();
            return;
        }

        Texture atlas = _boundFaceController.ExpressionAtlasTexture;
        if (atlas == null)
        {
            InvalidateExpression();
            return;
        }

        int resolvedExpressionId = expressionId;
        if (!_boundFaceController.TryGetExpressionUvRect(
                resolvedExpressionId,
                out Rect uvRect))
        {
            resolvedExpressionId = 0;
            if (!_boundFaceController.TryGetExpressionUvRect(
                    resolvedExpressionId,
                    out uvRect))
            {
                InvalidateExpression();
                return;
            }
        }

        if (_hasValidExpression &&
            _displayedExpressionId == resolvedExpressionId &&
            _displayedAtlas == atlas)
        {
            return;
        }

        expressionImage.texture = atlas;
        expressionImage.uvRect = uvRect;
        _displayedExpressionId = resolvedExpressionId;
        _displayedAtlas = atlas;
        _hasValidExpression = true;

        Log($"Expression {resolvedExpressionId} applied.");
    }

    private void InvalidateExpression()
    {
        _displayedExpressionId = -1;
        _displayedAtlas = null;
        _hasValidExpression = false;

        if (expressionImage != null)
            expressionImage.texture = null;

        SetVisible(false);
    }

    private void RefreshVisibility()
    {
        SetVisible(ShouldBeVisible());
    }

    private bool ShouldBeVisible()
    {
        if (!isActiveAndEnabled ||
            !_hasValidExpression ||
            expressionImage == null ||
            !IsBoundLocalOwnerValid() ||
            _boundStatusModule == null ||
            gameStateManager == null ||
            !gameStateManager.IsSpawned)
        {
            return false;
        }

        if (showOnlyDuringPlaying &&
            gameStateManager.GetState() !=
            GameStateManager.GameState.Playing)
        {
            return false;
        }

        if (hideWhenEliminated && _boundStatusModule.IsEliminated)
            return false;

        return true;
    }

    private bool IsBoundLocalOwnerValid()
    {
        if (_boundPlayerObject == null || !_boundPlayerObject.IsSpawned)
            return false;

        NetworkManager networkManager = NetworkManager.Singleton;
        if (networkManager == null ||
            !networkManager.IsListening ||
            _boundPlayerObject.OwnerClientId !=
            networkManager.LocalClientId)
        {
            return false;
        }

        NetworkClient localClient = networkManager.LocalClient;
        return localClient != null &&
               localClient.PlayerObject == _boundPlayerObject;
    }

    private static bool IsSamePlayerRoot(
        NetworkObject playerObject,
        Component component)
    {
        if (playerObject == null || component == null)
            return false;

        return component.GetComponentInParent<NetworkObject>() ==
               playerObject;
    }

    private void SetVisible(bool visible)
    {
        if (_visibilityInitialized && _isVisible == visible)
            return;

        _visibilityInitialized = true;
        _isVisible = visible;

        if (canvasGroup != null)
        {
            canvasGroup.alpha = visible ? 1f : 0f;
            canvasGroup.interactable = false;
            canvasGroup.blocksRaycasts = false;
        }

        if (expressionImage != null)
        {
            expressionImage.enabled = visible;
            expressionImage.raycastTarget = false;
        }
    }

    private void LogWaitingForLocalPlayer()
    {
        if (_loggedWaitingForLocalPlayer)
            return;

        _loggedWaitingForLocalPlayer = true;
        Log("Waiting for LocalClient.PlayerObject.");
    }

    private void Log(string message)
    {
        if (!debugLogs)
            return;

        Debug.Log($"[CharacterPortraitHUD] {message}", this);
    }

    private void OnValidate()
    {
        rebindInterval = Mathf.Max(0f, rebindInterval);
    }
}
