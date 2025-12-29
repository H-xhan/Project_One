using System.Collections.Generic;
using TMPro;
using Unity.Services.Lobbies.Models;
using UnityEngine;
using UnityEngine.UI;

public class LobbyUI : MonoBehaviour
{
    [Header("Main Menu")]
    [SerializeField] private Button createLobbyButton;
    [SerializeField] private Button joinCodeButton;
    [SerializeField] private TMP_InputField createInput; // 방 이름 입력
    [SerializeField] private TMP_InputField codeInput;   // 코드 입력

    [Header("Lobby List")]
    [SerializeField] private Transform container; // 목록이 생길 부모 위치 (Content)
    [SerializeField] private Transform lobbySingleTemplate; // 복사해서 쓸 프리팹
    [SerializeField] private Button refreshButton; // 목록 새로고침

    private void Awake()
    {
        // 방 만들기 버튼
        createLobbyButton.onClick.AddListener(() =>
        {
            string lobbyName = createInput.text;
            if (string.IsNullOrEmpty(lobbyName)) lobbyName = "New Room";
            LobbyManager.Instance.CreateLobby(lobbyName, 4);
        });

        // 코드 입장 버튼
        joinCodeButton.onClick.AddListener(() =>
        {
            string code = codeInput.text;
            if (!string.IsNullOrEmpty(code))
                LobbyManager.Instance.JoinLobbyByCode(code);
        });

        // 새로고침 버튼
        refreshButton.onClick.AddListener(RefreshLobbyList);

        lobbySingleTemplate.gameObject.SetActive(false); // 템플릿은 숨겨두기
    }

    private async void RefreshLobbyList()
    {
        // 1. 매니저에게 목록 가져오라고 시킴
        List<Lobby> lobbies = await LobbyManager.Instance.GetLobbies();

        // 2. 기존 목록 싹 지우기
        foreach (Transform child in container)
        {
            if (child == lobbySingleTemplate) continue;
            Destroy(child.gameObject);
        }

        // 3. 목록 새로 만들기
        if (lobbies == null) return;
        foreach (Lobby lobby in lobbies)
        {
            Transform lobbyTransform = Instantiate(lobbySingleTemplate, container);
            lobbyTransform.gameObject.SetActive(true);
            lobbyTransform.GetComponent<LobbyListSingleUI>().SetLobby(lobby);
        }
    }
}