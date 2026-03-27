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

    private Coroutine _autoRefreshRoutine;

    private void Awake()
    {
        if (createLobbyButton != null)
        {
            createLobbyButton.onClick.AddListener(() =>
            {
                if (!IsServicesReady())
                {
                    Debug.LogWarning("[LobbyUI] 서비스 초기화 중입니다. 잠시 후 다시 시도해주세요.");
                    return;
                }

                string lobbyName = createInput != null ? createInput.text : string.Empty;
                if (string.IsNullOrEmpty(lobbyName)) lobbyName = "New Room";

                if (LobbyManager.Instance != null)
                    LobbyManager.Instance.CreateLobby(lobbyName, 4);
            });
        }

        // CodeJoinButton은 Inspector On Click()으로만 연결해서 사용

        if (refreshButton != null)
        {
            refreshButton.onClick.AddListener(() =>
            {
                if (!IsServicesReady())
                {
                    Debug.LogWarning("[LobbyUI] 서비스 초기화 중입니다. 잠시 후 다시 시도해주세요.");
                    return;
                }

                RefreshLobbyList();
            });
        }
    }

    public void OnClickJoinByCode()
    {
        if (!IsServicesReady())
        {
            Debug.LogWarning("[LobbyUI] 서비스 초기화 중입니다. 잠시 후 다시 시도해주세요.");
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

    private IEnumerator WaitForServicesThenEnableUI()
    {
        while (LobbyManager.Instance == null)
            yield return null;

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
}
