using System.Collections;
using System.Collections.Generic;
using TMPro;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Lobbies.Models;
using UnityEngine;
using UnityEngine.UI;

public class LobbyUI : MonoBehaviour
{
    [Header("Main Menu")]
    [Tooltip("방 만들기 버튼")]
    [SerializeField] private Button createLobbyButton;

    [Tooltip("코드로 참가 버튼")]
    [SerializeField] private Button joinCodeButton;

    [Tooltip("방 이름 입력")]
    [SerializeField] private TMP_InputField createInput;

    [Tooltip("참가 코드 입력")]
    [SerializeField] private TMP_InputField codeInput;

    [Tooltip("방 생성 전 mutable settings Draft를 편집하는 Controller")]
    [SerializeField] private RoomSettingsPanelController roomSettingsPanelController;

    [Tooltip("설정 Modal이 열렸을 때 기존 MainMenu 입력을 차단할 CanvasGroup")]
    [SerializeField] private CanvasGroup mainMenuInteractionCanvasGroup;

    [Header("Lobby List")]
    [Tooltip("목록 아이템이 생성될 부모(Content)")]
    [SerializeField] private Transform container;

    [Tooltip("목록 아이템 템플릿(복사 원본)")]
    [SerializeField] private Transform lobbySingleTemplate;

    [Tooltip("목록 새로고침 버튼")]
    [SerializeField] private Button refreshButton;

    [Header("Options")]
    [Tooltip("플레이 시작 시 자동으로 목록을 한 번 불러옵니다(서비스 준비 후 실행)")]
    [SerializeField] private bool autoRefreshOnStart = true;

    [Tooltip("템플릿을 헤더처럼 보여줄지 여부(Join 버튼은 비활성화됨)")]
    [SerializeField] private bool showTemplateAsHeader = true;

    [Header("Debug")]
    [Tooltip("디버그 로그 출력 여부입니다.")]
    [SerializeField] private bool enableDebugLogs = false;

    private Coroutine _autoRefreshRoutine;
    private bool _isCreateRequestInFlight;
    private bool _uiListenersRegistered;
    private bool _lobbyEventsSubscribed;

    private void Awake()
    {
        RegisterUiListeners();

        // CodeJoinButton은 Inspector On Click()으로만 연결해서 사용
    }

    public void OnClickJoinByCode()
    {
        if (!IsServicesReady())
        {
            LogWarning("[LobbyUI] 서비스 초기화 중입니다. 잠시 후 다시 시도해주세요.");
            return;
        }

        string code = codeInput != null ? codeInput.text : string.Empty;
        code = string.IsNullOrWhiteSpace(code) ? string.Empty : code.Trim().ToUpper();
        if (string.IsNullOrEmpty(code))
            return;

        if (LobbyManager.Instance != null)
            LobbyManager.Instance.JoinLobbyByCode(code);
    }

    private void Start()
    {
        SetupTemplateHeader();

        SetInteractable(false);

        if (_autoRefreshRoutine != null)
            StopCoroutine(_autoRefreshRoutine);

        _autoRefreshRoutine = StartCoroutine(WaitForServicesThenEnableUI());
    }

    private void OnDestroy()
    {
        UnregisterUiListeners();
        UnsubscribeLobbyEvents();
    }

    private IEnumerator WaitForServicesThenEnableUI()
    {
        while (LobbyManager.Instance == null)
            yield return null;

        SubscribeLobbyEvents();

        while (UnityServices.State != ServicesInitializationState.Initialized)
            yield return null;

        while (!AuthenticationService.Instance.IsSignedIn)
            yield return null;

        SetInteractable(true);

        if (autoRefreshOnStart)
            RefreshLobbyList();
    }

    private bool IsServicesReady()
    {
        if (UnityServices.State != ServicesInitializationState.Initialized) return false;
        if (!AuthenticationService.Instance.IsSignedIn) return false;
        return true;
    }

    private void SetInteractable(bool enabled)
    {
        if (createLobbyButton != null) createLobbyButton.interactable = enabled;
        if (joinCodeButton != null) joinCodeButton.interactable = enabled;
        if (refreshButton != null) refreshButton.interactable = enabled;
        if (roomSettingsPanelController != null && roomSettingsPanelController.IsModalOpen)
            roomSettingsPanelController.SetCreateLocked(!enabled);

        bool modalOpen = roomSettingsPanelController != null &&
                         roomSettingsPanelController.IsModalOpen;
        SetMainMenuInteractionEnabled(enabled && !modalOpen);
    }

    private void SubscribeLobbyEvents()
    {
        if (_lobbyEventsSubscribed || LobbyManager.Instance == null)
            return;

        LobbyManager.Instance.LobbyOperationStarted += HandleLobbyOperationStarted;
        LobbyManager.Instance.LobbyOperationSucceeded += HandleLobbyOperationSucceeded;
        LobbyManager.Instance.LobbyOperationFailed += HandleLobbyOperationFailed;
        _lobbyEventsSubscribed = true;
    }

    private void UnsubscribeLobbyEvents()
    {
        if (!_lobbyEventsSubscribed || LobbyManager.Instance == null)
            return;

        LobbyManager.Instance.LobbyOperationStarted -= HandleLobbyOperationStarted;
        LobbyManager.Instance.LobbyOperationSucceeded -= HandleLobbyOperationSucceeded;
        LobbyManager.Instance.LobbyOperationFailed -= HandleLobbyOperationFailed;
        _lobbyEventsSubscribed = false;
    }

    private void HandleLobbyOperationStarted(string message)
    {
        _ = message;
        SetInteractable(false);
    }

    private void HandleLobbyOperationSucceeded(string message)
    {
        _ = message;
        if (_isCreateRequestInFlight && roomSettingsPanelController != null)
            roomSettingsPanelController.HandleCreateSucceeded();
        _isCreateRequestInFlight = false;
        SetInteractable(false);
    }

    private void HandleLobbyOperationFailed(string message)
    {
        if (_isCreateRequestInFlight && roomSettingsPanelController != null)
            roomSettingsPanelController.HandleCreateFailed(message);
        _isCreateRequestInFlight = false;
        SetInteractable(IsServicesReady());
    }

    private void RegisterUiListeners()
    {
        if (_uiListenersRegistered)
            return;

        if (createLobbyButton != null)
            createLobbyButton.onClick.AddListener(HandleCreateButtonClicked);

        if (refreshButton != null)
            refreshButton.onClick.AddListener(HandleRefreshButtonClicked);

        if (roomSettingsPanelController != null)
        {
            roomSettingsPanelController.CreateRequested += HandleCreateRequested;
            roomSettingsPanelController.ModalCancelled += HandleSettingsModalCancelled;
        }

        _uiListenersRegistered = true;
    }

    private void UnregisterUiListeners()
    {
        if (!_uiListenersRegistered)
            return;

        if (createLobbyButton != null)
            createLobbyButton.onClick.RemoveListener(HandleCreateButtonClicked);

        if (refreshButton != null)
            refreshButton.onClick.RemoveListener(HandleRefreshButtonClicked);

        if (roomSettingsPanelController != null)
        {
            roomSettingsPanelController.CreateRequested -= HandleCreateRequested;
            roomSettingsPanelController.ModalCancelled -= HandleSettingsModalCancelled;
        }

        _uiListenersRegistered = false;
    }

    private void HandleCreateButtonClicked()
    {
        if (!IsServicesReady())
        {
            LogWarning("[LobbyUI] 서비스 초기화 중입니다. 잠시 후 다시 시도해주세요.");
            return;
        }

        if (LobbyManager.Instance == null || LobbyManager.Instance.IsLobbyOperationInProgress)
            return;

        if (roomSettingsPanelController == null)
        {
            LogWarning("[LobbyUI] RoomSettingsPanelController가 연결되지 않았습니다.");
            return;
        }

        if (createInput != null)
            createInput.DeactivateInputField();

        if (codeInput != null)
            codeInput.DeactivateInputField();

        if (roomSettingsPanelController.OpenForCreate())
            SetMainMenuInteractionEnabled(false);
    }

    private void HandleCreateRequested()
    {
        if (roomSettingsPanelController == null ||
            !roomSettingsPanelController.IsModalOpen ||
            roomSettingsPanelController.IsCreateLocked)
        {
            return;
        }

        if (!IsServicesReady())
        {
            roomSettingsPanelController.ShowCreateError(
                "서비스 초기화 중입니다. 잠시 후 다시 시도해주세요.");
            return;
        }

        LobbyManager lobbyManager = LobbyManager.Instance;
        if (lobbyManager == null)
        {
            roomSettingsPanelController.ShowCreateError(
                "방 생성 서비스를 찾을 수 없습니다.");
            return;
        }

        if (lobbyManager.IsLobbyOperationInProgress)
        {
            roomSettingsPanelController.ShowCreateError(
                "다른 연결 작업이 진행 중입니다.");
            return;
        }

        if (!roomSettingsPanelController.TryFreezeSnapshot(
                out RoomGameplaySettingsSnapshot snapshot))
        {
            return;
        }

        string lobbyName = createInput != null ? createInput.text : string.Empty;
        if (string.IsNullOrEmpty(lobbyName))
            lobbyName = "New Room";

        _isCreateRequestInFlight = true;
        lobbyManager.CreateLobby(lobbyName, snapshot);
    }

    private void HandleSettingsModalCancelled()
    {
        bool canInteract = IsServicesReady() &&
                           (LobbyManager.Instance == null ||
                            !LobbyManager.Instance.IsLobbyOperationInProgress);
        SetInteractable(canInteract);
    }

    private void HandleRefreshButtonClicked()
    {
        if (!IsServicesReady())
        {
            LogWarning("[LobbyUI] 서비스 초기화 중입니다. 잠시 후 다시 시도해주세요.");
            return;
        }

        RefreshLobbyList();
    }

    private void SetMainMenuInteractionEnabled(bool enabled)
    {
        if (mainMenuInteractionCanvasGroup == null)
            return;

        mainMenuInteractionCanvasGroup.interactable = enabled;
        mainMenuInteractionCanvasGroup.blocksRaycasts = enabled;
    }

    private void SetupTemplateHeader()
    {
        if (lobbySingleTemplate == null) return;

        if (showTemplateAsHeader)
        {
            lobbySingleTemplate.gameObject.SetActive(true);

            var buttons = lobbySingleTemplate.GetComponentsInChildren<Button>(true);
            for (int i = 0; i < buttons.Length; i++)
            {
                buttons[i].onClick.RemoveAllListeners();
                buttons[i].interactable = false;
            }
        }
        else
        {
            lobbySingleTemplate.gameObject.SetActive(false);
        }
    }

    private async void RefreshLobbyList()
    {
        if (LobbyManager.Instance == null || container == null || lobbySingleTemplate == null)
            return;

        if (!IsServicesReady())
            return;

        List<Lobby> lobbies = await LobbyManager.Instance.GetLobbies();

        foreach (Transform child in container)
        {
            if (child == lobbySingleTemplate) continue;
            Destroy(child.gameObject);
        }

        if (lobbies == null) return;

        foreach (Lobby lobby in lobbies)
        {
            Transform lobbyTransform = Instantiate(lobbySingleTemplate, container);
            lobbyTransform.gameObject.SetActive(true);

            var buttons = lobbyTransform.GetComponentsInChildren<Button>(true);
            for (int i = 0; i < buttons.Length; i++)
                buttons[i].interactable = true;

            var singleUI = lobbyTransform.GetComponent<LobbyListSingleUI>();
            if (singleUI != null)
                singleUI.SetLobby(lobby);
        }
    }

    private void LogWarning(string message)
    {
        if (!enableDebugLogs)
            return;

        Debug.LogWarning(message, this);
    }
}
