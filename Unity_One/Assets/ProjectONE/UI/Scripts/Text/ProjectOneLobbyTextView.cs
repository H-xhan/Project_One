using TMPro;
using UnityEngine;

public class ProjectOneLobbyTextView : MonoBehaviour
{
    public TMP_Text roomCodeText;
    public TMP_Text readyCountText;
    public TMP_Text topMessageText;
    public TMP_Text characterTitleText;

    public void SetRoomCode(string roomCode)
    {
        if (roomCodeText != null)
        {
            roomCodeText.text = string.IsNullOrWhiteSpace(roomCode) ? "------" : roomCode;
        }
    }

    public void SetReadyCount(int ready, int total)
    {
        if (readyCountText != null)
        {
            readyCountText.text = string.Format("{0} / {1} Ready", Mathf.Max(0, ready), Mathf.Max(0, total));
        }
    }

    public void SetTopMessage(string message)
    {
        if (topMessageText != null)
        {
            topMessageText.text = message ?? string.Empty;
        }
    }
}
