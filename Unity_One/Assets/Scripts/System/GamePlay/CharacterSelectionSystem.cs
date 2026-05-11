using System;
using System.Collections.Generic;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

public class CharacterSelectionSystem : NetworkBehaviour
{
    public struct CharacterSelectionEntry : INetworkSerializable, IEquatable<CharacterSelectionEntry>
    {
        public ulong ClientId;
        public int CharacterId;

        public CharacterSelectionEntry(ulong clientId, int characterId)
        {
            ClientId = clientId;
            CharacterId = characterId;
        }

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref ClientId);
            serializer.SerializeValue(ref CharacterId);
        }

        public bool Equals(CharacterSelectionEntry other)
        {
            return ClientId == other.ClientId && CharacterId == other.CharacterId;
        }

        public override bool Equals(object obj)
        {
            return obj is CharacterSelectionEntry other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return (ClientId.GetHashCode() * 397) ^ CharacterId;
            }
        }
    }

    [Header("Refs")]
    [SerializeField, Tooltip("준비 상태를 확인할 ReadySystem입니다. 비워두면 씬에서 자동 탐색합니다.")]
    private ReadySystem readySystem;

    [SerializeField, Tooltip("현재 게임 상태를 확인할 GameStateManager입니다. 비워두면 씬에서 자동 탐색합니다.")]
    private GameStateManager gameStateManager;

    [SerializeField, Tooltip("캐릭터 선택 UI가 들어있는 루트입니다. 비워두면 이 오브젝트의 자식에서 자동 탐색합니다.")]
    private Transform characterPanelRoot;

    [Header("UI")]
    [SerializeField, Tooltip("캐릭터 선택 버튼 목록입니다. 배열 순서가 캐릭터 ID로 사용됩니다.")]
    private Button[] characterButtons;

    [SerializeField, Tooltip("현재 로컬 플레이어가 선택한 캐릭터를 표시할 TMP 텍스트입니다. 비워두면 SelectedCharacterText 이름의 자식에서 자동 탐색합니다.")]
    private TMP_Text selectedCharacterText;

    [SerializeField, Tooltip("캐릭터 버튼 클릭 리스너를 배열 순서 기준으로 자동 등록할지 여부입니다. 버튼 OnClick을 직접 연결할 경우 꺼두는 것을 권장합니다.")]
    private bool registerButtonCallbacksAutomatically = false;

    [Header("Selection")]
    [SerializeField, Tooltip("선택 가능한 캐릭터 개수입니다. 캐릭터 ID는 0부터 이 값보다 작은 수까지 허용됩니다.")]
    private int availableCharacterCount = 3;

    [SerializeField, Tooltip("접속한 플레이어에게 기본으로 등록할 캐릭터 ID입니다.")]
    private int defaultCharacterId = 0;

    [SerializeField, Tooltip("접속한 플레이어에게 기본 캐릭터 선택값을 자동으로 등록할지 여부입니다.")]
    private bool assignDefaultCharacterOnConnect = true;

    [SerializeField, Tooltip("Lobby 상태에서만 캐릭터 선택을 허용할지 여부입니다.")]
    private bool requireLobbyStateForSelection = true;

    [SerializeField, Tooltip("준비 완료 후 캐릭터 선택 변경을 막을지 여부입니다.")]
    private bool blockSelectionAfterReady = true;

    [Header("Text")]
    [SerializeField, Tooltip("캐릭터를 아직 선택하지 않았을 때 표시할 문구입니다.")]
    private string noSelectionText = "캐릭터 미선택";

    [SerializeField, Tooltip("선택한 캐릭터를 표시할 형식입니다. {0}에는 캐릭터 ID가 들어갑니다.")]
    private string selectedCharacterTextFormat = "선택 캐릭터 {0}";

    [SerializeField, Tooltip("준비 완료 후 선택 변경이 막힌 상태에서 선택 문구 뒤에 붙일 문구입니다.")]
    private string readyLockedSuffix = " (준비 완료)";

    private NetworkList<CharacterSelectionEntry> selectedCharacters;
    private UnityAction[] _registeredButtonActions;
    private bool _buttonListenersRegistered;
    private bool _selectionListSubscribed;
    private bool _networkManagerCallbacksRegistered;

    public int LocalSelectedCharacterId
    {
        get
        {
            return TryGetLocalSelectedCharacter(out int characterId) ? characterId : -1;
        }
    }

    private void Awake()
    {
        selectedCharacters = new NetworkList<CharacterSelectionEntry>();
        ResolveRefs();
        ResolveUIRefs();
        RegisterButtonListeners();
    }

    private void OnEnable()
    {
        ResolveRefs();
        ResolveUIRefs();
        RegisterButtonListeners();

        if (IsSpawned)
            SubscribeSelectionList();

        RefreshUI();
    }

    private void OnDisable()
    {
        UnsubscribeSelectionList();
        UnregisterButtonListeners();
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        ResolveRefs();
        ResolveUIRefs();
        SubscribeSelectionList();

        if (IsServer)
        {
            RegisterNetworkManagerCallbacks();
            RegisterConnectedClientsServer();
        }

        RefreshUI();
    }

    public override void OnNetworkDespawn()
    {
        UnregisterNetworkManagerCallbacks();
        UnsubscribeSelectionList();

        base.OnNetworkDespawn();
    }

    private void OnDestroy()
    {
        UnregisterNetworkManagerCallbacks();
        UnsubscribeSelectionList();
        UnregisterButtonListeners();

        if (selectedCharacters != null)
        {
            selectedCharacters.Dispose();
            selectedCharacters = null;
        }
    }

    private void Update()
    {
        RefreshUI();
    }

    public void UI_SelectCharacter(int characterId)
    {
        if (!CanLocalRequestSelection(characterId))
        {
            RefreshUI();
            return;
        }

        SelectCharacterServerRpc(characterId);
    }

    public bool TryGetSelectedCharacter(ulong clientId, out int characterId)
    {
        int index = FindSelectionIndex(clientId);
        if (index < 0)
        {
            characterId = -1;
            return false;
        }

        characterId = selectedCharacters[index].CharacterId;
        return true;
    }

    public bool TryGetLocalSelectedCharacter(out int characterId)
    {
        NetworkManager networkManager = NetworkManager.Singleton;
        if (networkManager == null)
        {
            characterId = -1;
            return false;
        }

        return TryGetSelectedCharacter(networkManager.LocalClientId, out characterId);
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void SelectCharacterServerRpc(int characterId, RpcParams rpcParams = default)
    {
        if (!IsServer)
            return;

        ulong senderClientId = rpcParams.Receive.SenderClientId;
        if (!CanClientSelectCharacterServer(senderClientId, characterId))
            return;

        SetSelectedCharacterServer(senderClientId, characterId);
    }

    private void ResolveRefs()
    {
        if (readySystem == null)
            readySystem = FindFirstObjectByType<ReadySystem>();

        if (gameStateManager == null)
            gameStateManager = FindFirstObjectByType<GameStateManager>();
    }

    private void ResolveUIRefs()
    {
        Transform searchRoot = characterPanelRoot != null ? characterPanelRoot : transform;

        if (selectedCharacterText == null)
            selectedCharacterText = FindChildComponentByName<TMP_Text>(searchRoot, "SelectedCharacterText");

        if (characterButtons == null || characterButtons.Length == 0)
        {
            List<Button> foundButtons = new List<Button>();
            int buttonCount = Mathf.Max(1, availableCharacterCount);
            for (int i = 0; i < buttonCount; i++)
            {
                Button button = FindChildComponentByName<Button>(searchRoot, $"CharacterButton_{i}");
                if (button != null)
                    foundButtons.Add(button);
            }

            if (foundButtons.Count > 0)
                characterButtons = foundButtons.ToArray();
        }
    }

    private static T FindChildComponentByName<T>(Transform root, string childName) where T : Component
    {
        if (root == null)
            return null;

        T[] components = root.GetComponentsInChildren<T>(true);
        for (int i = 0; i < components.Length; i++)
        {
            T component = components[i];
            if (component != null && component.name == childName)
                return component;
        }

        return null;
    }

    private void RegisterButtonListeners()
    {
        if (_buttonListenersRegistered || !registerButtonCallbacksAutomatically)
            return;

        if (characterButtons == null || characterButtons.Length == 0)
            return;

        _registeredButtonActions = new UnityAction[characterButtons.Length];
        for (int i = 0; i < characterButtons.Length; i++)
        {
            Button button = characterButtons[i];
            if (button == null)
                continue;

            int characterId = i;
            UnityAction action = () => UI_SelectCharacter(characterId);
            button.onClick.AddListener(action);
            _registeredButtonActions[i] = action;
        }

        _buttonListenersRegistered = true;
    }

    private void UnregisterButtonListeners()
    {
        if (!_buttonListenersRegistered || _registeredButtonActions == null || characterButtons == null)
        {
            _buttonListenersRegistered = false;
            _registeredButtonActions = null;
            return;
        }

        int count = Mathf.Min(characterButtons.Length, _registeredButtonActions.Length);
        for (int i = 0; i < count; i++)
        {
            if (characterButtons[i] != null && _registeredButtonActions[i] != null)
                characterButtons[i].onClick.RemoveListener(_registeredButtonActions[i]);
        }

        _buttonListenersRegistered = false;
        _registeredButtonActions = null;
    }

    private void SubscribeSelectionList()
    {
        if (_selectionListSubscribed || selectedCharacters == null)
            return;

        selectedCharacters.OnListChanged += OnSelectedCharactersChanged;
        _selectionListSubscribed = true;
    }

    private void UnsubscribeSelectionList()
    {
        if (!_selectionListSubscribed || selectedCharacters == null)
            return;

        selectedCharacters.OnListChanged -= OnSelectedCharactersChanged;
        _selectionListSubscribed = false;
    }

    private void RegisterNetworkManagerCallbacks()
    {
        NetworkManager networkManager = NetworkManager.Singleton;
        if (_networkManagerCallbacksRegistered || networkManager == null)
            return;

        networkManager.OnClientConnectedCallback += OnClientConnectedServer;
        networkManager.OnClientDisconnectCallback += OnClientDisconnectedServer;
        _networkManagerCallbacksRegistered = true;
    }

    private void UnregisterNetworkManagerCallbacks()
    {
        NetworkManager networkManager = NetworkManager.Singleton;
        if (!_networkManagerCallbacksRegistered || networkManager == null)
        {
            _networkManagerCallbacksRegistered = false;
            return;
        }

        networkManager.OnClientConnectedCallback -= OnClientConnectedServer;
        networkManager.OnClientDisconnectCallback -= OnClientDisconnectedServer;
        _networkManagerCallbacksRegistered = false;
    }

    private void OnClientConnectedServer(ulong clientId)
    {
        if (!IsServer || !assignDefaultCharacterOnConnect)
            return;

        EnsureDefaultSelectionServer(clientId);
    }

    private void OnClientDisconnectedServer(ulong clientId)
    {
        if (!IsServer)
            return;

        RemoveSelectionServer(clientId);
    }

    private void RegisterConnectedClientsServer()
    {
        if (!assignDefaultCharacterOnConnect)
            return;

        NetworkManager networkManager = NetworkManager.Singleton;
        if (networkManager == null)
            return;

        for (int i = 0; i < networkManager.ConnectedClientsList.Count; i++)
        {
            EnsureDefaultSelectionServer(networkManager.ConnectedClientsList[i].ClientId);
        }
    }

    private void EnsureDefaultSelectionServer(ulong clientId)
    {
        if (FindSelectionIndex(clientId) >= 0)
            return;

        SetSelectedCharacterServer(clientId, GetClampedDefaultCharacterId());
    }

    private bool CanLocalRequestSelection(int characterId)
    {
        if (!IsCharacterIdValid(characterId))
            return false;

        if (!IsSpawned)
            return false;

        NetworkManager networkManager = NetworkManager.Singleton;
        if (networkManager == null || !networkManager.IsListening)
            return false;

        if (requireLobbyStateForSelection)
        {
            if (gameStateManager == null || gameStateManager.GetState() != GameStateManager.GameState.Lobby)
                return false;
        }

        if (blockSelectionAfterReady)
        {
            if (readySystem != null && readySystem.IsLocalReady())
                return false;
        }

        return true;
    }

    private bool CanClientSelectCharacterServer(ulong clientId, int characterId)
    {
        if (!IsCharacterIdValid(characterId))
            return false;

        if (requireLobbyStateForSelection)
        {
            ResolveRefs();
            if (gameStateManager == null || gameStateManager.GetState() != GameStateManager.GameState.Lobby)
                return false;
        }

        if (blockSelectionAfterReady)
        {
            ResolveRefs();
            if (readySystem != null && readySystem.IsClientReady(clientId))
                return false;
        }

        return true;
    }

    private bool IsCharacterIdValid(int characterId)
    {
        return characterId >= 0 && characterId < Mathf.Max(1, availableCharacterCount);
    }

    private int GetClampedDefaultCharacterId()
    {
        return Mathf.Clamp(defaultCharacterId, 0, Mathf.Max(1, availableCharacterCount) - 1);
    }

    private void SetSelectedCharacterServer(ulong clientId, int characterId)
    {
        int index = FindSelectionIndex(clientId);
        CharacterSelectionEntry entry = new CharacterSelectionEntry(clientId, characterId);

        if (index >= 0)
        {
            if (selectedCharacters[index].CharacterId == characterId)
                return;

            selectedCharacters[index] = entry;
            return;
        }

        selectedCharacters.Add(entry);
    }

    private void RemoveSelectionServer(ulong clientId)
    {
        for (int i = selectedCharacters.Count - 1; i >= 0; i--)
        {
            if (selectedCharacters[i].ClientId == clientId)
                selectedCharacters.RemoveAt(i);
        }
    }

    private int FindSelectionIndex(ulong clientId)
    {
        if (selectedCharacters == null)
            return -1;

        for (int i = 0; i < selectedCharacters.Count; i++)
        {
            if (selectedCharacters[i].ClientId == clientId)
                return i;
        }

        return -1;
    }

    private void OnSelectedCharactersChanged(NetworkListEvent<CharacterSelectionEntry> changeEvent)
    {
        RefreshUI();
    }

    private void RefreshUI()
    {
        RefreshSelectedCharacterText();
        RefreshButtonInteractable();
    }

    private void RefreshSelectedCharacterText()
    {
        if (selectedCharacterText == null)
            return;

        if (!TryGetLocalSelectedCharacter(out int characterId))
        {
            selectedCharacterText.text = noSelectionText;
            return;
        }

        string text = FormatSelectedCharacterText(characterId);
        if (blockSelectionAfterReady && readySystem != null && readySystem.IsLocalReady())
            text += readyLockedSuffix;

        selectedCharacterText.text = text;
    }

    private string FormatSelectedCharacterText(int characterId)
    {
        if (string.IsNullOrEmpty(selectedCharacterTextFormat))
            return characterId.ToString();

        try
        {
            return string.Format(selectedCharacterTextFormat, characterId);
        }
        catch (FormatException)
        {
            return characterId.ToString();
        }
    }

    private void RefreshButtonInteractable()
    {
        if (characterButtons == null)
            return;

        bool canSelect = CanLocalSelectNow();
        for (int i = 0; i < characterButtons.Length; i++)
        {
            if (characterButtons[i] != null)
                characterButtons[i].interactable = canSelect && IsCharacterIdValid(i);
        }
    }

    private bool CanLocalSelectNow()
    {
        if (!IsSpawned)
            return false;

        NetworkManager networkManager = NetworkManager.Singleton;
        if (networkManager == null || !networkManager.IsListening)
            return false;

        if (requireLobbyStateForSelection)
        {
            if (gameStateManager == null || gameStateManager.GetState() != GameStateManager.GameState.Lobby)
                return false;
        }

        if (blockSelectionAfterReady)
        {
            if (readySystem != null && readySystem.IsLocalReady())
                return false;
        }

        return true;
    }

    private void OnValidate()
    {
        if (availableCharacterCount < 1)
            availableCharacterCount = 1;

        defaultCharacterId = Mathf.Clamp(defaultCharacterId, 0, availableCharacterCount - 1);
    }
}
