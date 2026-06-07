using TMPro;
using UnityEngine;

public class ProjectOneHUDTextView : MonoBehaviour
{
    public TMP_Text coinText;
    public TMP_Text timerText;
    public TMP_Text staminaText;
    public TMP_Text missionTitleText;
    public TMP_Text missionDescriptionText;
    public TMP_Text rewardText;

    public void SetCoin(int coin)
    {
        if (coinText != null)
        {
            coinText.text = coin.ToString();
        }
    }

    public void SetTime(float seconds)
    {
        if (timerText == null)
        {
            return;
        }

        int totalSeconds = Mathf.Max(0, Mathf.CeilToInt(seconds));
        int minutes = totalSeconds / 60;
        int remainingSeconds = totalSeconds % 60;
        timerText.text = string.Format("{0:00}:{1:00}", minutes, remainingSeconds);
    }

    public void SetStamina(int current, int max)
    {
        if (staminaText != null)
        {
            staminaText.text = string.Format("{0} / {1}", Mathf.Max(0, current), Mathf.Max(0, max));
        }
    }

    public void SetMission(string title, string description, int rewardCoin)
    {
        if (missionTitleText != null)
        {
            missionTitleText.text = title ?? string.Empty;
        }

        if (missionDescriptionText != null)
        {
            missionDescriptionText.text = description ?? string.Empty;
        }

        if (rewardText != null)
        {
            rewardText.text = string.Format("+{0} 코인", Mathf.Max(0, rewardCoin));
        }
    }
}
