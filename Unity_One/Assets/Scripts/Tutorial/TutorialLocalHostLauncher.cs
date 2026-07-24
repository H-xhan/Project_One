using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnitySceneManager = UnityEngine.SceneManagement.SceneManager;

[DisallowMultipleComponent]
public sealed class TutorialLocalHostLauncher : MonoBehaviour
{
    private enum LauncherState
    {
        Idle = 0,
        StartingHost = 1,
        WaitingForPlayer = 2,
        LoadingTutorial = 3,
        RunningTutorial = 4,
        ShuttingDown = 5,
        ReturningMainMenu = 6,
        Failed = 7
    }

    [Header("Scene Flow")]
    [SerializeField] private string tutorialSceneName = "Tutorial_Desk";
    [SerializeField] private string mainMenuSceneName = "MainMenu";
    [SerializeField] private LoadingOverlayUI loadingOverlay;

    [Header("Local Host")]
    [SerializeField] private string loopbackAddress = "127.0.0.1";
    [SerializeField] private int loopbackPort = 7777;

    [Header("Bounded Waits")]
    [SerializeField] private float playerObjectResolveTimeoutSeconds = 3f;
    [SerializeField] private float tutorialSceneLoadTimeoutSeconds = 20f;
    [SerializeField] private float shutdownTimeoutSeconds = 8f;
    [SerializeField] private float cameraSuppressionMaxSeconds = 24f;

    [Header("Debug")]
    [SerializeField] private bool enableDebugLogs;

    private readonly List<Camera> _sourceSceneCameras = new List<Camera>();
    private readonly List<AudioListener> _sourceSceneAudioListeners = new List<AudioListener>();

    private LauncherState _state;
    private NetworkManager _networkManager;
    private NetworkSceneManager _networkSceneManager;
    private UnityTransport _transport;
    private Coroutine _startRoutine;
    private Coroutine _shutdownRoutine;
    private Coroutine _mainMenuValidationRoutine;

    private bool _ownsTutorialSession;
    private bool _networkCallbacksSubscribed;
    private bool _sceneLoadCallbackSubscribed;
    private bool _serverStoppedReceived;
    private bool _returnToMainMenuAfterShutdown;
    private bool _mainMenuSceneLoadSubscribed;

    private bool _sceneLoadCompleted;
    private string _sceneLoadFailure = string.Empty;
    private string _pendingFailureMessage = string.Empty;
    private bool _applicationQuitting;

    private bool _transportSnapshotCaptured;
    private UnityTransport.ConnectionAddressData _originalTransportConnectionData;

    private bool _cameraSuppressionActive;
    private float _cameraSuppressionDeadline;
    private Camera _suppressedPlayerCamera;
    private AudioListener _suppressedPlayerAudioListener;
    private bool _suppressedPlayerCameraWasEnabled;
    private bool _suppressedPlayerAudioListenerWasEnabled;

    public bool IsBusy =>
        _state == LauncherState.StartingHost ||
        _state == LauncherState.WaitingForPlayer ||
        _state == LauncherState.LoadingTutorial ||
        _state == LauncherState.ShuttingDown ||
        _state == LauncherState.ReturningMainMenu;

    public bool IsTutorialSessionActive => _ownsTutorialSession;

    public void BeginTutorial()
    {
        if (_state != LauncherState.Idle || _startRoutine != null || _shutdownRoutine != null)
        {
            LogWarning("BeginTutorial ignored because another tutorial transition is active.");
            return;
        }

        if (!TryResolveStartDependencies(out string failure))
        {
            ShowFailure(failure);
            return;
        }

        if (!CanStartIsolatedLocalHost(out failure))
        {
            ShowFailure(failure);
            return;
        }

        _startRoutine = StartCoroutine(BeginTutorialRoutine());
    }

    public void RequestExitTutorial()
    {
        if (!_ownsTutorialSession)
        {
            LogWarning("RequestExitTutorial ignored because this launcher does not own a tutorial session.");
            return;
        }

        if (_state == LauncherState.ShuttingDown || _state == LauncherState.ReturningMainMenu)
            return;

        if (_startRoutine != null)
        {
            StopCoroutine(_startRoutine);
            _startRoutine = null;
        }

        BeginOwnedSessionShutdown(true, string.Empty);
    }

    private IEnumerator BeginTutorialRoutine()
    {
        yield return null;

        _state = LauncherState.StartingHost;
        _pendingFailureMessage = string.Empty;
        ResolveLoadingOverlay();
        loadingOverlay?.Show("튜토리얼 Local Host를 시작하는 중...");

        CaptureSourceScenePresentation();
        CaptureTransportSnapshot();

        try
        {
            _transport.SetConnectionData(
                true,
                loopbackAddress,
                (ushort)loopbackPort,
                loopbackAddress);
        }
        catch (System.Exception exception)
        {
            RestoreTransportSnapshot();
            ClearSourceScenePresentationSnapshot();
            _state = LauncherState.Idle;
            _startRoutine = null;
            ShowFailure($"Local transport 설정에 실패했습니다: {exception.Message}");
            yield break;
        }

        SubscribeNetworkCallbacks();

        bool hostStarted;
        try
        {
            hostStarted = _networkManager.StartHost();
        }
        catch (System.Exception exception)
        {
            hostStarted = false;
            LogException(exception);
        }

        if (!hostStarted)
        {
            UnsubscribeNetworkCallbacks();
            RestoreTransportSnapshot();
            ClearSourceScenePresentationSnapshot();
            _state = LauncherState.Idle;
            _startRoutine = null;
            ShowFailure("Tutorial Local Host를 시작하지 못했습니다.");
            yield break;
        }

        _ownsTutorialSession = true;
        DontDestroyOnLoad(gameObject);
        BeginBoundedCameraSuppression();

        _state = LauncherState.WaitingForPlayer;
        loadingOverlay?.Show("로컬 플레이어를 준비하는 중...");

        float playerDeadline =
            Time.realtimeSinceStartup + Mathf.Max(0.1f, playerObjectResolveTimeoutSeconds);
        PlayerHub playerHub = null;

        while (Time.realtimeSinceStartup < playerDeadline)
        {
            if (!IsOwnedHostRunning())
            {
                _startRoutine = null;
                BeginOwnedSessionShutdown(true, "Local Host가 플레이어 준비 중 중단되었습니다.");
                yield break;
            }

            if (TryResolveLocalPlayerHub(out playerHub))
                break;

            yield return null;
        }

        if (playerHub == null)
        {
            _startRoutine = null;
            BeginOwnedSessionShutdown(true, "제한 시간 안에 Local PlayerObject를 찾지 못했습니다.");
            yield break;
        }

        CaptureSuppressedPlayerPresentation(playerHub);
        ApplyBoundedCameraSuppression();

        _networkSceneManager = _networkManager.SceneManager;
        if (_networkSceneManager == null)
        {
            _startRoutine = null;
            BeginOwnedSessionShutdown(true, "NGO NetworkSceneManager를 사용할 수 없습니다.");
            yield break;
        }

        SubscribeSceneLoadCallback();
        _sceneLoadCompleted = false;
        _sceneLoadFailure = string.Empty;
        _state = LauncherState.LoadingTutorial;
        loadingOverlay?.Show("튜토리얼 책상 맵을 불러오는 중...");

        SceneEventProgressStatus loadStatus;
        try
        {
            loadStatus = _networkSceneManager.LoadScene(
                tutorialSceneName,
                LoadSceneMode.Single);
        }
        catch (System.Exception exception)
        {
            LogException(exception);
            loadStatus = SceneEventProgressStatus.SceneFailedVerification;
        }

        if (loadStatus != SceneEventProgressStatus.Started)
        {
            _startRoutine = null;
            BeginOwnedSessionShutdown(
                true,
                $"Tutorial Scene load를 시작하지 못했습니다: {loadStatus}");
            yield break;
        }

        float sceneDeadline =
            Time.realtimeSinceStartup + Mathf.Max(1f, tutorialSceneLoadTimeoutSeconds);

        while (!_sceneLoadCompleted && string.IsNullOrEmpty(_sceneLoadFailure))
        {
            if (Time.realtimeSinceStartup >= sceneDeadline)
            {
                _sceneLoadFailure = "Tutorial Scene load 제한 시간을 초과했습니다.";
                break;
            }

            if (!IsOwnedHostRunning())
            {
                _sceneLoadFailure = "Tutorial Scene load 중 Local Host가 중단되었습니다.";
                break;
            }

            yield return null;
        }

        if (!string.IsNullOrEmpty(_sceneLoadFailure))
        {
            _startRoutine = null;
            BeginOwnedSessionShutdown(true, _sceneLoadFailure);
            yield break;
        }

        yield return null;

        if (UnitySceneManager.GetActiveScene().name != tutorialSceneName ||
            !TryResolveLocalPlayerHub(out playerHub) ||
            !HasRequiredTutorialSceneObjects())
        {
            _startRoutine = null;
            BeginOwnedSessionShutdown(
                true,
                "Tutorial Scene 완료 후 Local Player readiness 검증에 실패했습니다.");
            yield break;
        }

        EndBoundedCameraSuppression(true, false);
        UnsubscribeSceneLoadCallback();
        _state = LauncherState.RunningTutorial;
        _startRoutine = null;
        loadingOverlay?.Hide();
        Log("Tutorial Local Host와 Tutorial Scene 준비가 완료되었습니다.");
    }

    private bool TryResolveStartDependencies(out string failure)
    {
        failure = string.Empty;
        ResolveLoadingOverlay();

        if (string.IsNullOrWhiteSpace(tutorialSceneName) ||
            string.IsNullOrWhiteSpace(mainMenuSceneName))
        {
            failure = "Tutorial/MainMenu Scene 이름이 설정되지 않았습니다.";
            return false;
        }

        if (!Application.CanStreamedLevelBeLoaded(tutorialSceneName))
        {
            failure = $"Build Settings에서 Tutorial Scene을 찾지 못했습니다: {tutorialSceneName}";
            return false;
        }

        if (string.IsNullOrWhiteSpace(loopbackAddress) ||
            loopbackPort < 1 ||
            loopbackPort > ushort.MaxValue)
        {
            failure = "Local loopback address 또는 port 설정이 올바르지 않습니다.";
            return false;
        }

        if (UnitySceneManager.GetActiveScene().name != mainMenuSceneName)
        {
            failure = "Tutorial Local Host는 MainMenu에서만 시작할 수 있습니다.";
            return false;
        }

        if (transform.parent != null)
        {
            failure = "Tutorial Local Host launcher는 독립된 root GameObject여야 합니다.";
            return false;
        }

        if (GetComponent<NetworkManager>() != null ||
            GetComponent<UnityTransport>() != null ||
            GetComponent<LobbyManager>() != null ||
            GetComponent<RelayManager>() != null ||
            GetComponent<Camera>() != null ||
            GetComponent<AudioListener>() != null ||
            GetComponent<EventSystem>() != null)
        {
            failure = "Tutorial Local Host launcher 전용 root에 다른 persistent component가 있습니다.";
            return false;
        }

        _networkManager = NetworkManager.Singleton;
        if (_networkManager == null || _networkManager.NetworkConfig == null)
        {
            failure = "NetworkManager를 찾지 못했습니다.";
            return false;
        }

        NetworkManager[] networkManagers = FindObjectsByType<NetworkManager>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);
        if (networkManagers.Length != 1 ||
            _networkManager.transform.parent != null ||
            _networkManager.gameObject.scene != UnitySceneManager.GetActiveScene() ||
            _networkManager.gameObject == gameObject)
        {
            failure = "MainMenu NetworkManager root가 정확히 하나가 아닙니다.";
            return false;
        }

        _transport = _networkManager.NetworkConfig.NetworkTransport as UnityTransport;
        if (_transport == null)
        {
            failure = "NetworkManager transport가 UnityTransport가 아닙니다.";
            return false;
        }

        return true;
    }

    private bool CanStartIsolatedLocalHost(out string failure)
    {
        failure = string.Empty;

        if (_networkManager.ShutdownInProgress ||
            _networkManager.IsListening ||
            _networkManager.IsServer ||
            _networkManager.IsClient ||
            _networkManager.IsConnectedClient)
        {
            failure = "이미 실행 중이거나 종료 중인 Network session이 있습니다.";
            return false;
        }

        LobbyManager lobbyManager = LobbyManager.Instance;
        RelayManager relayManager = RelayManager.Instance;
        if (lobbyManager == null || relayManager == null)
        {
            failure = "MainMenu Lobby/Relay 상태를 안전하게 확인할 수 없습니다.";
            return false;
        }

        if (lobbyManager.IsLobbyOperationInProgress)
        {
            failure = "Lobby 생성 또는 참가 작업이 진행 중입니다.";
            return false;
        }

        if (lobbyManager.GetHostLobby() != null)
        {
            failure = "활성 Lobby membership이 남아 있습니다.";
            return false;
        }

        if (!string.IsNullOrWhiteSpace(relayManager.CurrentJoinCode))
        {
            failure = "활성 Relay join state가 남아 있습니다.";
            return false;
        }

        return true;
    }

    private bool IsOwnedHostRunning()
    {
        return _ownsTutorialSession &&
               _networkManager != null &&
               !_networkManager.ShutdownInProgress &&
               _networkManager.IsListening &&
               _networkManager.IsHost &&
               _networkManager.IsServer &&
               _networkManager.IsClient;
    }

    private bool TryResolveLocalPlayerHub(out PlayerHub playerHub)
    {
        playerHub = null;
        if (_networkManager == null)
            return false;

        NetworkClient localClient = _networkManager.LocalClient;
        if (localClient == null ||
            localClient.PlayerObject == null ||
            !localClient.PlayerObject.IsSpawned ||
            !localClient.PlayerObject.IsPlayerObject ||
            localClient.PlayerObject.OwnerClientId != _networkManager.LocalClientId)
        {
            return false;
        }

        playerHub = localClient.PlayerObject.GetComponentInChildren<PlayerHub>(true);
        return playerHub != null &&
               playerHub.IsOwner &&
               playerHub.IsSpawned &&
               playerHub.NetworkObject == localClient.PlayerObject &&
               playerHub.GetComponentInParent<NetworkObject>() == localClient.PlayerObject &&
               playerHub.PlayerCamera != null;
    }

    private void HandleLoadEventCompleted(
        string sceneName,
        LoadSceneMode loadSceneMode,
        List<ulong> clientsCompleted,
        List<ulong> clientsTimedOut)
    {
        if (_state != LauncherState.LoadingTutorial ||
            sceneName != tutorialSceneName ||
            loadSceneMode != LoadSceneMode.Single)
        {
            return;
        }

        if (clientsTimedOut != null && clientsTimedOut.Count > 0)
        {
            _sceneLoadFailure =
                $"Tutorial Scene load 중 {clientsTimedOut.Count} client가 timeout되었습니다.";
            return;
        }

        if (_networkManager == null ||
            clientsCompleted == null ||
            !clientsCompleted.Contains(_networkManager.LocalClientId))
        {
            _sceneLoadFailure =
                "Tutorial Scene load 완료 목록에 Local Host가 없습니다.";
            return;
        }

        _sceneLoadCompleted = true;
    }

    private void BeginOwnedSessionShutdown(
        bool returnToMainMenu,
        string failureMessage)
    {
        if (!_ownsTutorialSession)
        {
            ShowFailure(string.IsNullOrEmpty(failureMessage)
                ? "소유한 Tutorial session이 없습니다."
                : failureMessage);
            return;
        }

        _returnToMainMenuAfterShutdown = returnToMainMenu;
        _pendingFailureMessage = failureMessage ?? string.Empty;
        _state = LauncherState.ShuttingDown;
        _serverStoppedReceived =
            _serverStoppedReceived ||
            _networkManager == null ||
            (!_networkManager.IsListening &&
             !_networkManager.IsServer &&
             !_networkManager.IsClient);

        UnsubscribeSceneLoadCallback();
        EndBoundedCameraSuppression(true, true);

        if (string.IsNullOrEmpty(_pendingFailureMessage))
            loadingOverlay?.Show("Tutorial Network session을 종료하는 중...");
        else
            loadingOverlay?.Show("Tutorial 오류를 정리하고 MainMenu로 돌아가는 중...");

        if (_networkManager != null &&
            !_networkManager.ShutdownInProgress &&
            (_networkManager.IsServer ||
             _networkManager.IsClient ||
             _networkManager.IsListening))
        {
            _networkManager.Shutdown(false);
        }

        if (_shutdownRoutine == null)
            _shutdownRoutine = StartCoroutine(CompleteShutdownRoutine());
    }

    private IEnumerator CompleteShutdownRoutine()
    {
        GameObject networkRoot = _networkManager != null
            ? _networkManager.transform.root.gameObject
            : null;
        float deadline =
            Time.realtimeSinceStartup + Mathf.Max(1f, shutdownTimeoutSeconds);

        while (Time.realtimeSinceStartup < deadline)
        {
            bool networkStopped =
                _networkManager == null ||
                (!_networkManager.ShutdownInProgress &&
                 !_networkManager.IsListening &&
                 !_networkManager.IsServer &&
                 !_networkManager.IsClient);

            if (networkStopped && (_serverStoppedReceived || _networkManager == null))
                break;

            yield return null;
        }

        bool shutdownComplete =
            _networkManager == null ||
            (!_networkManager.ShutdownInProgress &&
             !_networkManager.IsListening &&
             !_networkManager.IsServer &&
             !_networkManager.IsClient);

        if (!shutdownComplete)
        {
            _shutdownRoutine = null;
            _state = LauncherState.Failed;
            ShowFailure("Tutorial Network shutdown 제한 시간을 초과했습니다.");
            yield break;
        }

        yield return null;

        UnsubscribeNetworkCallbacks();
        RestoreTransportSnapshot();
        _networkSceneManager = null;
        _ownsTutorialSession = false;

        if (!_returnToMainMenuAfterShutdown)
        {
            _shutdownRoutine = null;
            _state = LauncherState.Idle;
            ApplyPendingFailureOrHide();
            yield break;
        }

        if (networkRoot == null ||
            networkRoot.GetComponentInChildren<NetworkManager>(true) != _networkManager)
        {
            _shutdownRoutine = null;
            _state = LauncherState.Failed;
            ShowFailure("종료할 persistent NetworkManager root를 정확히 확인하지 못했습니다.");
            yield break;
        }

        Destroy(networkRoot);

        float destroyDeadline =
            Time.realtimeSinceStartup + Mathf.Max(1f, shutdownTimeoutSeconds);
        while (Time.realtimeSinceStartup < destroyDeadline &&
               (networkRoot != null || NetworkManager.Singleton != null))
        {
            yield return null;
        }

        if (networkRoot != null || NetworkManager.Singleton != null)
        {
            _shutdownRoutine = null;
            _state = LauncherState.Failed;
            ShowFailure("기존 persistent NetworkManager root 제거 제한 시간을 초과했습니다.");
            yield break;
        }

        _networkManager = null;
        _transport = null;
        yield return null;

        _state = LauncherState.ReturningMainMenu;
        SubscribeMainMenuSceneLoad();

        try
        {
            UnitySceneManager.LoadScene(mainMenuSceneName, LoadSceneMode.Single);
        }
        catch (System.Exception exception)
        {
            UnsubscribeMainMenuSceneLoad();
            _state = LauncherState.Failed;
            ShowFailure($"MainMenu 복귀에 실패했습니다: {exception.Message}");
        }

        _shutdownRoutine = null;
    }

    private void HandleServerStopped(bool wasHost)
    {
        if (!_ownsTutorialSession)
            return;

        _serverStoppedReceived = true;
        Log($"Tutorial Network server stopped. wasHost={wasHost}");

        if (_state == LauncherState.ShuttingDown ||
            _state == LauncherState.ReturningMainMenu)
        {
            return;
        }

        if (_startRoutine != null)
        {
            StopCoroutine(_startRoutine);
            _startRoutine = null;
        }

        BeginOwnedSessionShutdown(
            true,
            "Tutorial Local Host가 외부에서 종료되었습니다.");
    }

    private void HandleTransportFailure()
    {
        if (!_ownsTutorialSession ||
            _state == LauncherState.ShuttingDown ||
            _state == LauncherState.ReturningMainMenu)
        {
            return;
        }

        if (_startRoutine != null)
        {
            StopCoroutine(_startRoutine);
            _startRoutine = null;
        }

        BeginOwnedSessionShutdown(
            true,
            "Tutorial Local Host transport가 중단되었습니다.");
    }

    private void HandleMainMenuLoaded(Scene scene, LoadSceneMode loadSceneMode)
    {
        if (scene.name != mainMenuSceneName ||
            loadSceneMode != LoadSceneMode.Single)
        {
            return;
        }

        UnsubscribeMainMenuSceneLoad();
        if (_mainMenuValidationRoutine == null)
        {
            _mainMenuValidationRoutine =
                StartCoroutine(ValidateMainMenuReturnRoutine(scene));
        }
    }

    private IEnumerator ValidateMainMenuReturnRoutine(Scene scene)
    {
        float deadline =
            Time.realtimeSinceStartup + Mathf.Max(1f, tutorialSceneLoadTimeoutSeconds);
        string failure = "MainMenu 복귀 후 필수 object 검증에 실패했습니다.";

        yield return null;

        while (Time.realtimeSinceStartup < deadline)
        {
            if (TryValidateFreshMainMenu(scene, out failure))
            {
                _mainMenuValidationRoutine = null;
                ApplyPendingFailureOrHide();
                Destroy(gameObject);
                yield break;
            }

            yield return null;
        }

        _mainMenuValidationRoutine = null;
        _state = LauncherState.Failed;
        ShowFailure(failure);
    }

    private bool TryValidateFreshMainMenu(Scene scene, out string failure)
    {
        failure = string.Empty;
        if (!scene.IsValid() ||
            !scene.isLoaded ||
            UnitySceneManager.GetActiveScene() != scene)
        {
            failure = "MainMenu Scene이 active/loaded 상태가 아닙니다.";
            return false;
        }

        NetworkManager[] networkManagers = FindObjectsByType<NetworkManager>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);
        if (networkManagers.Length != 1)
        {
            failure = $"MainMenu NetworkManager 수가 1이 아닙니다: {networkManagers.Length}";
            return false;
        }

        NetworkManager freshNetworkManager = networkManagers[0];
        if (freshNetworkManager == null ||
            freshNetworkManager.gameObject.scene != scene ||
            freshNetworkManager.transform.parent != null ||
            freshNetworkManager.IsListening ||
            freshNetworkManager.IsServer ||
            freshNetworkManager.IsClient ||
            freshNetworkManager.ShutdownInProgress)
        {
            failure = "새 MainMenu NetworkManager가 idle root 계약을 충족하지 않습니다.";
            return false;
        }

        if (CountEnabledSceneComponents<Camera>(scene) != 1 ||
            CountEnabledSceneComponents<AudioListener>(scene) != 1 ||
            CountEnabledSceneComponents<EventSystem>(scene) != 1)
        {
            failure = "새 MainMenu Camera/AudioListener/EventSystem 수가 각각 1이 아닙니다.";
            return false;
        }

        TutorialLocalHostLauncher[] launchers =
            FindObjectsByType<TutorialLocalHostLauncher>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);
        int freshLauncherCount = 0;
        for (int i = 0; i < launchers.Length; i++)
        {
            TutorialLocalHostLauncher launcher = launchers[i];
            if (launcher != null &&
                launcher != this &&
                launcher.gameObject.scene == scene)
            {
                freshLauncherCount++;
            }
        }

        if (freshLauncherCount != 1)
        {
            failure = $"새 MainMenu Tutorial launcher 수가 1이 아닙니다: {freshLauncherCount}";
            return false;
        }

        if (LobbyManager.Instance == null ||
            RelayManager.Instance == null ||
            LobbyManager.Instance.GetHostLobby() != null ||
            !string.IsNullOrWhiteSpace(RelayManager.Instance.CurrentJoinCode))
        {
            failure = "새 MainMenu Lobby/Relay manager가 idle 계약을 충족하지 않습니다.";
            return false;
        }

        return true;
    }

    private static int CountEnabledSceneComponents<T>(Scene scene)
        where T : Behaviour
    {
        T[] components = FindObjectsByType<T>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);
        int count = 0;
        for (int i = 0; i < components.Length; i++)
        {
            T component = components[i];
            if (component != null &&
                component.enabled &&
                component.gameObject.activeInHierarchy &&
                component.gameObject.scene == scene)
            {
                count++;
            }
        }

        return count;
    }

    private static bool HasRequiredTutorialSceneObjects()
    {
        GameObject tutorialFlow = GameObject.Find("TutorialFlow");
        return tutorialFlow != null &&
               tutorialFlow.GetComponent("TutorialDirector") != null &&
               FindFirstObjectByType<GameStateManager>() != null &&
               FindFirstObjectByType<ReadySystem>() != null &&
               FindFirstObjectByType<InGameMatchManager>() != null &&
               FindFirstObjectByType<PostItRoundManager>() != null;
    }

    private void SubscribeNetworkCallbacks()
    {
        if (_networkCallbacksSubscribed || _networkManager == null)
            return;

        _networkManager.OnServerStopped += HandleServerStopped;
        _networkManager.OnTransportFailure += HandleTransportFailure;
        _networkCallbacksSubscribed = true;
    }

    private void UnsubscribeNetworkCallbacks()
    {
        if (!_networkCallbacksSubscribed)
            return;

        if (_networkManager != null)
        {
            _networkManager.OnServerStopped -= HandleServerStopped;
            _networkManager.OnTransportFailure -= HandleTransportFailure;
        }

        _networkCallbacksSubscribed = false;
    }

    private void SubscribeSceneLoadCallback()
    {
        if (_sceneLoadCallbackSubscribed || _networkSceneManager == null)
            return;

        _networkSceneManager.OnLoadEventCompleted += HandleLoadEventCompleted;
        _sceneLoadCallbackSubscribed = true;
    }

    private void UnsubscribeSceneLoadCallback()
    {
        if (!_sceneLoadCallbackSubscribed)
            return;

        if (_networkSceneManager != null)
        {
            _networkSceneManager.OnLoadEventCompleted -= HandleLoadEventCompleted;
        }

        _sceneLoadCallbackSubscribed = false;
        _networkSceneManager = null;
    }

    private void SubscribeMainMenuSceneLoad()
    {
        if (_mainMenuSceneLoadSubscribed)
            return;

        UnitySceneManager.sceneLoaded += HandleMainMenuLoaded;
        _mainMenuSceneLoadSubscribed = true;
    }

    private void UnsubscribeMainMenuSceneLoad()
    {
        if (!_mainMenuSceneLoadSubscribed)
            return;

        UnitySceneManager.sceneLoaded -= HandleMainMenuLoaded;
        _mainMenuSceneLoadSubscribed = false;
    }

    private void CaptureTransportSnapshot()
    {
        if (_transport == null)
            return;

        UnityTransport.ConnectionAddressData connectionData =
            _transport.ConnectionData;
        _originalTransportConnectionData = connectionData;
        _transportSnapshotCaptured = true;
    }

    private void RestoreTransportSnapshot()
    {
        if (!_transportSnapshotCaptured || _transport == null)
            return;

        try
        {
            _transport.SetConnectionData(
                false,
                _originalTransportConnectionData.Address,
                _originalTransportConnectionData.Port,
                _originalTransportConnectionData.ServerListenAddress);
            _transport.ConnectionData = _originalTransportConnectionData;
        }
        catch (System.Exception exception)
        {
            LogException(exception);
        }

        _transportSnapshotCaptured = false;
        _originalTransportConnectionData = default;
    }

    private void CaptureSourceScenePresentation()
    {
        ClearSourceScenePresentationSnapshot();
        Scene sourceScene = UnitySceneManager.GetActiveScene();

        Camera[] cameras = FindObjectsByType<Camera>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);
        for (int i = 0; i < cameras.Length; i++)
        {
            Camera camera = cameras[i];
            if (camera != null &&
                camera.enabled &&
                camera.gameObject.scene == sourceScene)
            {
                _sourceSceneCameras.Add(camera);
            }
        }

        AudioListener[] listeners = FindObjectsByType<AudioListener>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);
        for (int i = 0; i < listeners.Length; i++)
        {
            AudioListener listener = listeners[i];
            if (listener != null &&
                listener.enabled &&
                listener.gameObject.scene == sourceScene)
            {
                _sourceSceneAudioListeners.Add(listener);
            }
        }
    }

    private void BeginBoundedCameraSuppression()
    {
        float requiredSuppressionSeconds =
            Mathf.Max(0.1f, playerObjectResolveTimeoutSeconds) +
            Mathf.Max(1f, tutorialSceneLoadTimeoutSeconds) +
            1f;
        _cameraSuppressionDeadline =
            Time.realtimeSinceStartup +
            Mathf.Max(cameraSuppressionMaxSeconds, requiredSuppressionSeconds);
        _cameraSuppressionActive = true;
    }

    private void CaptureSuppressedPlayerPresentation(PlayerHub playerHub)
    {
        if (playerHub == null)
            return;

        _suppressedPlayerCamera = playerHub.PlayerCamera;
        if (_suppressedPlayerCamera != null)
        {
            _suppressedPlayerCameraWasEnabled = _suppressedPlayerCamera.enabled;
            _suppressedPlayerAudioListener =
                _suppressedPlayerCamera.GetComponent<AudioListener>();
        }

        if (_suppressedPlayerAudioListener != null)
        {
            _suppressedPlayerAudioListenerWasEnabled =
                _suppressedPlayerAudioListener.enabled;
        }
    }

    private void LateUpdate()
    {
        if (!_cameraSuppressionActive)
            return;

        if (Time.realtimeSinceStartup >= _cameraSuppressionDeadline)
        {
            EndBoundedCameraSuppression(true, true);
            return;
        }

        ApplyBoundedCameraSuppression();
    }

    private void ApplyBoundedCameraSuppression()
    {
        if (!_cameraSuppressionActive)
            return;

        if (_suppressedPlayerCamera != null)
            _suppressedPlayerCamera.enabled = false;

        if (_suppressedPlayerAudioListener != null)
            _suppressedPlayerAudioListener.enabled = false;

        for (int i = 0; i < _sourceSceneCameras.Count; i++)
        {
            Camera camera = _sourceSceneCameras[i];
            if (camera != null)
                camera.enabled = true;
        }

        for (int i = 0; i < _sourceSceneAudioListeners.Count; i++)
        {
            AudioListener listener = _sourceSceneAudioListeners[i];
            if (listener != null)
                listener.enabled = true;
        }
    }

    private void EndBoundedCameraSuppression(
        bool restorePlayerPresentation,
        bool restoreSourcePresentation)
    {
        if (restorePlayerPresentation)
        {
            if (_suppressedPlayerCamera != null)
                _suppressedPlayerCamera.enabled = _suppressedPlayerCameraWasEnabled;

            if (_suppressedPlayerAudioListener != null)
            {
                _suppressedPlayerAudioListener.enabled =
                    _suppressedPlayerAudioListenerWasEnabled;
            }
        }

        if (restoreSourcePresentation)
        {
            for (int i = 0; i < _sourceSceneCameras.Count; i++)
            {
                Camera camera = _sourceSceneCameras[i];
                if (camera != null)
                    camera.enabled = true;
            }

            for (int i = 0; i < _sourceSceneAudioListeners.Count; i++)
            {
                AudioListener listener = _sourceSceneAudioListeners[i];
                if (listener != null)
                    listener.enabled = true;
            }
        }

        _cameraSuppressionActive = false;
        _suppressedPlayerCamera = null;
        _suppressedPlayerAudioListener = null;
        _suppressedPlayerCameraWasEnabled = false;
        _suppressedPlayerAudioListenerWasEnabled = false;
        ClearSourceScenePresentationSnapshot();
    }

    private void ClearSourceScenePresentationSnapshot()
    {
        _sourceSceneCameras.Clear();
        _sourceSceneAudioListeners.Clear();
    }

    private void ApplyPendingFailureOrHide()
    {
        ResolveLoadingOverlay();
        if (string.IsNullOrEmpty(_pendingFailureMessage))
            loadingOverlay?.Hide();
        else
            loadingOverlay?.ShowError(_pendingFailureMessage);

        _pendingFailureMessage = string.Empty;
    }

    private void ResolveLoadingOverlay()
    {
        if (loadingOverlay == null)
            loadingOverlay = FindFirstObjectByType<LoadingOverlayUI>();
    }

    private void ShowFailure(string message)
    {
        ResolveLoadingOverlay();
        if (loadingOverlay != null)
            loadingOverlay.ShowError(message);
        else
            Debug.LogError($"[TutorialLocalHost] {message}", this);
    }

    private void OnDestroy()
    {
        if (_startRoutine != null)
            StopCoroutine(_startRoutine);

        if (_shutdownRoutine != null)
            StopCoroutine(_shutdownRoutine);

        if (_mainMenuValidationRoutine != null)
            StopCoroutine(_mainMenuValidationRoutine);

        EndBoundedCameraSuppression(true, true);
        UnsubscribeSceneLoadCallback();
        UnsubscribeNetworkCallbacks();
        UnsubscribeMainMenuSceneLoad();

        if (_ownsTutorialSession && !_applicationQuitting)
        {
            Debug.LogError(
                "[TutorialLocalHost] Active Tutorial session owner가 예상 밖으로 제거되었습니다.",
                this);
            if (_networkManager != null &&
                !_networkManager.ShutdownInProgress &&
                (_networkManager.IsListening ||
                 _networkManager.IsServer ||
                 _networkManager.IsClient))
            {
                _networkManager.Shutdown(false);
            }

            RestoreTransportSnapshot();
            _ownsTutorialSession = false;
        }
    }

    private void OnApplicationQuit()
    {
        _applicationQuitting = true;
    }

    private void OnValidate()
    {
        loopbackPort = Mathf.Clamp(loopbackPort, 1, ushort.MaxValue);
        playerObjectResolveTimeoutSeconds =
            Mathf.Max(0.1f, playerObjectResolveTimeoutSeconds);
        tutorialSceneLoadTimeoutSeconds =
            Mathf.Max(1f, tutorialSceneLoadTimeoutSeconds);
        shutdownTimeoutSeconds = Mathf.Max(1f, shutdownTimeoutSeconds);
        cameraSuppressionMaxSeconds =
            Mathf.Max(0.1f, cameraSuppressionMaxSeconds);
    }

    private void Log(string message)
    {
        if (enableDebugLogs)
            Debug.Log($"[TutorialLocalHost] {message}", this);
    }

    private void LogWarning(string message)
    {
        if (enableDebugLogs)
            Debug.LogWarning($"[TutorialLocalHost] {message}", this);
    }

    private void LogException(System.Exception exception)
    {
        if (enableDebugLogs)
            Debug.LogException(exception, this);
    }
}
