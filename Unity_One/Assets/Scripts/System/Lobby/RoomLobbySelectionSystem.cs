using System;
using Unity.Netcode;
using UnityEngine;

public class RoomLobbySelectionSystem : NetworkBehaviour
{
    [Serializable]
    public struct PlayerSelection : INetworkSerializable, IEquatable<PlayerSelection>
    {
        public ulong clientId;
        public int characterId;
        public bool hasSelected;

        public PlayerSelection(ulong clientId, int characterId, bool hasSelected)
        {
            this.clientId = clientId;
            this.characterId = characterId;
            this.hasSelected = hasSelected;
        }

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref clientId);
            serializer.SerializeValue(ref characterId);
            serializer.SerializeValue(ref hasSelected);
        }

        public bool Equals(PlayerSelection other) => clientId == other.clientId;
    }

    [Header("Refs")]
    [Tooltip("ReadySystem 참조(없으면 자동 탐색)")]
    [SerializeField] private ReadySystem readySystem;

    [Header("Rules")]
    [Tooltip("캐릭터 선택을 해야 Ready 가능")]
    [SerializeField] private bool requireCharacterSelectToReady = false;

    [Tooltip("기본 캐릭터 ID(아무것도 선택 안 했을 때 표시용)")]
    [SerializeField] private int defaultCharacterId = 0;

    [Header("Map")]
    [Tooltip("선택된 맵 ID(Host만 변경 가능)")]
    public NetworkVariable<int> SelectedMapId = new NetworkVariable<int>(0);

    private NetworkList<PlayerSelection> _selections;

    private void Awake()
    {
        _selections = new NetworkList<PlayerSelection>();
    }

    public override void OnNetworkSpawn()
    {
        if (readySystem == null)
            readySystem = FindFirstObjectByType<ReadySystem>();

        if (IsServer)
        {
            SyncAllConnectedClientsToList();

            if (NetworkManager.Singleton != null)
            {
                NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
                NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;
            }
        }
    }

    public override void OnNetworkDespawn()
    {
        if (!IsServer) return;

        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnected;
        }
    }

    private void OnClientConnected(ulong clientId)
    {
        if (!IsServer) return;
        EnsureClientEntry(clientId);
    }

    private void OnClientDisconnected(ulong clientId)
    {
        if (!IsServer) return;

        for (int i = _selections.Count - 1; i >= 0; i--)
        {
            if (_selections[i].clientId == clientId)
                _selections.RemoveAt(i);
        }
    }

    private void SyncAllConnectedClientsToList()
    {
        if (!IsServer) return;
        if (NetworkManager.Singleton == null) return;

        foreach (var id in NetworkManager.Singleton.ConnectedClientsIds)
            EnsureClientEntry(id);
    }

    private void EnsureClientEntry(ulong clientId)
    {
        for (int i = 0; i < _selections.Count; i++)
        {
            if (_selections[i].clientId == clientId)
                return;
        }

        _selections.Add(new PlayerSelection(clientId, defaultCharacterId, false));
    }

    public bool TryGetCharacterId(ulong clientId, out int characterId)
    {
        for (int i = 0; i < _selections.Count; i++)
        {
            if (_selections[i].clientId == clientId)
            {
                var s = _selections[i];
                characterId = s.hasSelected ? s.characterId : defaultCharacterId;
                return true;
            }
        }

        characterId = defaultCharacterId;
        return false;
    }

    public bool IsLocalCharacterSelected()
    {
        if (NetworkManager.Singleton == null) return false;
        ulong localId = NetworkManager.Singleton.LocalClientId;

        for (int i = 0; i < _selections.Count; i++)
        {
            if (_selections[i].clientId == localId)
                return _selections[i].hasSelected;
        }

        return false;
    }

    public void UI_SelectCharacter(int characterId)
    {
        SubmitCharacterSelectionServerRpc(characterId);
    }

    public void UI_SelectMap(int mapId)
    {
        SubmitMapSelectionServerRpc(mapId);
    }

    public void UI_ToggleReady()
    {
        if (readySystem == null)
            readySystem = FindFirstObjectByType<ReadySystem>();

        if (readySystem == null) return;

        if (requireCharacterSelectToReady && !IsLocalCharacterSelected())
        {
            Debug.LogWarning("[RoomLobbySelection] 캐릭터를 선택해야 Ready 할 수 있습니다.");
            return;
        }

        readySystem.ToggleLocalReady();
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void SubmitCharacterSelectionServerRpc(int characterId, RpcParams rpcParams = default)
    {
        if (!IsServer) return;

        ulong senderId = rpcParams.Receive.SenderClientId;
        EnsureClientEntry(senderId);

        for (int i = 0; i < _selections.Count; i++)
        {
            if (_selections[i].clientId == senderId)
            {
                _selections[i] = new PlayerSelection(senderId, characterId, true);
                break;
            }
        }
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
    private void SubmitMapSelectionServerRpc(int mapId, RpcParams rpcParams = default)
    {
        if (!IsServer) return;

        ulong senderId = rpcParams.Receive.SenderClientId;
        if (NetworkManager.Singleton == null) return;

        if (senderId != NetworkManager.Singleton.LocalClientId && !NetworkManager.Singleton.IsHost)
            return;

        if (!NetworkManager.Singleton.IsHost) return;

        SelectedMapId.Value = mapId;
    }
}
