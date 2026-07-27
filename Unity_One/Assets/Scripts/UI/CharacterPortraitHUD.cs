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
    private Transform _boundVisualRoot;
    private Transform _boundModelRoot;
    private CharacterPortraitLiveRenderer _liveRenderer;
    private float _nextBindAttemptTime;
    private int _displayedExpressionId = -1;
    private Texture _displayedAtlas;
    private Texture _sceneFallbackTexture;
    private Rect _sceneFallbackUvRect;
    private bool _sceneFallbackCaptured;
    private bool _hasValidExpression;
    private bool _isUsingLivePortrait;
    private bool _visibilityInitialized;
    private bool _isVisible;
    private bool _loggedWaitingForLocalPlayer;

    private void Awake()
    {
        ResolvePresentationReferences();
        CaptureSceneFallback();
        ResolveLiveRenderer();
        SetVisible(false);
    }

    private void OnEnable()
    {
        ResolvePresentationReferences();
        CaptureSceneFallback();
        ResolveLiveRenderer();
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
        if (_boundPlayerObject != null &&
            (!IsBoundLocalOwnerValid() ||
             !IsBoundVisualSourceValid()))
        {
            ClearBinding();
        }

        if (_isUsingLivePortrait &&
            (_liveRenderer == null || !_liveRenderer.IsReady))
        {
            ApplyExpression(
                _boundFaceController != null
                    ? _boundFaceController.CurrentExpressionId
                    : 0);
        }

        if (Time.unscaledTime >= _nextBindAttemptTime)
        {
            if (NeedsPlayerBinding())
            {
                TryBindLocalPlayer();
            }
            else if (gameStateManager == null)
            {
                _nextBindAttemptTime =
                    Time.unscaledTime + Mathf.Max(0f, rebindInterval);
                ResolveGameStateManager();
            }
        }

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

    private void CaptureSceneFallback()
    {
        if (_sceneFallbackCaptured || expressionImage == null)
            return;

        _sceneFallbackCaptured = true;
        _sceneFallbackTexture = expressionImage.texture;
        _sceneFallbackUvRect = expressionImage.uvRect;
    }

    private void ResolveLiveRenderer()
    {
        if (_liveRenderer == null)
            _liveRenderer =
                GetComponent<CharacterPortraitLiveRenderer>();

        if (_liveRenderer == null && Application.isPlaying)
        {
            _liveRenderer =
                gameObject.AddComponent<
                    CharacterPortraitLiveRenderer>();
        }

        if (_liveRenderer != null)
            _liveRenderer.SetDebugLogging(debugLogs);
    }

    private bool NeedsPlayerBinding()
    {
        return _boundPlayerObject == null ||
               _boundFaceController == null ||
               _boundStatusModule == null ||
               _boundVisualRoot == null ||
               _boundModelRoot == null;
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

        PlayerStatusModule statusModule =
            playerObject.GetComponentInChildren<PlayerStatusModule>(true);

        if (!TryResolveSingleSugarVisual(
                playerObject,
                out FaceExpressionController faceController,
                out Transform visualRoot,
                out Transform modelRoot) ||
            !IsSamePlayerRoot(playerObject, statusModule))
        {
            if (_boundPlayerObject != null)
                ClearBinding();

            LogWaitingForLocalPlayer();
            RefreshVisibility();
            return;
        }

        Bind(
            playerObject,
            faceController,
            statusModule,
            visualRoot,
            modelRoot);
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
        PlayerStatusModule statusModule,
        Transform visualRoot,
        Transform modelRoot)
    {
        bool bindingUnchanged =
            _boundPlayerObject == playerObject &&
            _boundFaceController == faceController &&
            _boundStatusModule == statusModule &&
            _boundVisualRoot == visualRoot &&
            _boundModelRoot == modelRoot;

        if (bindingUnchanged)
        {
            _loggedWaitingForLocalPlayer = false;
            RefreshVisibility();
            return;
        }

        ClearBinding();

        _boundPlayerObject = playerObject;
        _boundFaceController = faceController;
        _boundStatusModule = statusModule;
        _boundVisualRoot = visualRoot;
        _boundModelRoot = modelRoot;
        _boundFaceController.ExpressionChanged +=
            HandleExpressionChanged;

        Log("Local owner bound.");

        _loggedWaitingForLocalPlayer = false;
        ResolveLiveRenderer();

        if (_liveRenderer != null &&
            _liveRenderer.Bind(
                _boundPlayerObject,
                _boundVisualRoot,
                _boundModelRoot,
                _boundFaceController))
        {
            ApplyLivePortrait();
        }
        else
        {
            ApplyExpression(
                _boundFaceController.CurrentExpressionId);
        }

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
        _boundVisualRoot = null;
        _boundModelRoot = null;
        _displayedExpressionId = -1;
        _displayedAtlas = null;
        _isUsingLivePortrait = false;

        if (_liveRenderer != null)
        {
            _liveRenderer.SetRenderingActive(false);
            _liveRenderer.Unbind();
        }

        ApplySceneFallback();

        SetVisible(false);
    }

    private void HandleExpressionChanged(int expressionId)
    {
        if (_isUsingLivePortrait &&
            _liveRenderer != null &&
            _liveRenderer.IsReady)
        {
            RefreshVisibility();
            return;
        }

        ApplyExpression(expressionId);
        RefreshVisibility();
    }

    private void ApplyLivePortrait()
    {
        if (_liveRenderer == null ||
            !_liveRenderer.IsReady ||
            expressionImage == null ||
            _liveRenderer.OutputTexture == null)
        {
            InvalidateExpression();
            return;
        }

        expressionImage.texture = _liveRenderer.OutputTexture;
        expressionImage.uvRect = new Rect(0f, 0f, 1f, 1f);
        _displayedExpressionId = -1;
        _displayedAtlas = null;
        _hasValidExpression = true;
        _isUsingLivePortrait = true;

        Log(
            $"Live portrait applied: " +
            $"renderers={_liveRenderer.SourceRendererCount}, " +
            $"transforms={_liveRenderer.MappedTransformCount}.");
    }

    private void ApplyExpression(int expressionId)
    {
        _isUsingLivePortrait = false;
        if (_liveRenderer != null)
            _liveRenderer.SetRenderingActive(false);

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
        _isUsingLivePortrait = false;

        if (!ApplySceneFallback())
            SetVisible(false);
    }

    private bool ApplySceneFallback()
    {
        _displayedExpressionId = -1;
        _displayedAtlas = null;
        _isUsingLivePortrait = false;
        _hasValidExpression =
            expressionImage != null &&
            _sceneFallbackCaptured &&
            _sceneFallbackTexture != null;

        if (expressionImage != null)
        {
            expressionImage.texture =
                _hasValidExpression
                    ? _sceneFallbackTexture
                    : null;
            expressionImage.uvRect =
                _hasValidExpression
                    ? _sceneFallbackUvRect
                    : new Rect(0f, 0f, 1f, 1f);
        }

        return _hasValidExpression;
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

        if (_isUsingLivePortrait && !CanRenderLivePortrait())
            return false;

        return true;
    }

    private bool CanRenderLivePortrait()
    {
        return gameStateManager != null &&
               gameStateManager.IsSpawned &&
               gameStateManager.GetState() ==
               GameStateManager.GameState.Playing &&
               _boundStatusModule != null &&
               !_boundStatusModule.IsEliminated;
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

    private bool IsBoundVisualSourceValid()
    {
        return _boundVisualRoot != null &&
               _boundModelRoot != null &&
               _boundFaceController != null &&
               _boundVisualRoot.gameObject.activeInHierarchy &&
               _boundModelRoot.gameObject.activeInHierarchy &&
               _boundFaceController.isActiveAndEnabled &&
               _boundModelRoot.parent == _boundVisualRoot &&
               _boundPlayerObject != null &&
               _boundVisualRoot.IsChildOf(
                   _boundPlayerObject.transform) &&
               _boundModelRoot.IsChildOf(
                   _boundPlayerObject.transform) &&
               _boundFaceController.transform.IsChildOf(
                   _boundPlayerObject.transform);
    }

    private static bool TryResolveSingleSugarVisual(
        NetworkObject playerObject,
        out FaceExpressionController faceController,
        out Transform visualRoot,
        out Transform modelRoot)
    {
        faceController = null;
        visualRoot = null;
        modelRoot = null;

        if (playerObject == null)
            return false;

        FaceExpressionController[] controllers =
            playerObject.GetComponentsInChildren<
                FaceExpressionController>(true);
        for (int i = 0; i < controllers.Length; i++)
        {
            FaceExpressionController candidate = controllers[i];
            if (candidate == null ||
                !candidate.isActiveAndEnabled ||
                !candidate.gameObject.activeInHierarchy ||
                !IsSamePlayerRoot(playerObject, candidate))
            {
                continue;
            }

            if (faceController != null)
                return false;

            faceController = candidate;
        }

        if (faceController == null)
            return false;

        modelRoot = faceController.transform;
        if (modelRoot.name != "슈가")
            return false;

        Transform candidateVisualRoot = modelRoot.parent;
        if (candidateVisualRoot == null ||
            candidateVisualRoot.name != "VisualPreviewRoot" ||
            candidateVisualRoot.parent == null ||
            candidateVisualRoot.parent.name != "MotorShellBody" ||
            !candidateVisualRoot.gameObject.activeInHierarchy ||
            !IsSamePlayerRoot(playerObject, candidateVisualRoot) ||
            !IsSamePlayerRoot(playerObject, modelRoot))
        {
            modelRoot = null;
            faceController = null;
            return false;
        }

        visualRoot = candidateVisualRoot;
        return true;
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
        if (_liveRenderer != null)
        {
            _liveRenderer.SetRenderingActive(
                visible &&
                _isUsingLivePortrait &&
                CanRenderLivePortrait() &&
                _liveRenderer.IsReady);
        }

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
