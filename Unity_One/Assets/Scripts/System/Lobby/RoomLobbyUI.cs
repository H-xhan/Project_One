using TMPro;
using Unity.Netcode;
using UnityEngine;

public class RoomLobbyUI : MonoBehaviour
{
    [SerializeField, Tooltip("룸 로비의 플레이어/레디 카운트를 표시할 텍스트")]
    private TextMeshProUGUI playerCountText;

    [SerializeField, Tooltip("룸 코드를 표시할 텍스트")]
    private TextMeshProUGUI roomCodeText;

    [SerializeField, Tooltip("레디 시스템 참조(룸 로비 오브젝트의 ReadySystem)")]
    private ReadySystem readySystem;

    [SerializeField, Tooltip("룸 로비에서 커서를 항상 표시하고 잠금 해제 상태로 유지할지")]
    private bool enforceLobbyCursor = true;

    private void OnEnable()
    {
        ApplyLobbyCursor();
        UpdateRoomCodeText();
    }

    private void Update()
    {
        UpdateReadyText();
        UpdateRoomCodeText();
    }

    private void LateUpdate()
    {
        if (!enforceLobbyCursor)
            return;

        ApplyLobbyCursor();
    }

    private void UpdateReadyText()
    {
        if (playerCountText == null || readySystem == null)
            return;

        int totalPlayers = 0;

        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
            totalPlayers = NetworkManager.Singleton.ConnectedClientsIds.Count;

        int readyPlayers = readySystem.GetReadyCount();
        playerCountText.text = $"{readyPlayers}/{totalPlayers} READY";
    }

    private void UpdateRoomCodeText()
    {
        if (roomCodeText == null)
            return;

        string code = RelayManager.Instance != null
            ? RelayManager.Instance.CurrentJoinCode
            : string.Empty;

        roomCodeText.text = string.IsNullOrEmpty(code)
            ? "ROOM CODE : ----"
            : $"ROOM CODE : {code}";
    }

    private void ApplyLobbyCursor()
    {
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }
}