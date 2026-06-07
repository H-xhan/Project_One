using TMPro;
using UnityEngine;

public class ProjectOneResultTextView : MonoBehaviour
{
    public TMP_Text titleText;
    public TMP_Text earnedCoinText;
    public TMP_Text totalCoinText;
    public TMP_Text[] playerNameTexts;
    public TMP_Text[] scoreTexts;

    public void SetResult(int earnedCoin, int totalCoin)
    {
        if (titleText != null)
        {
            titleText.text = "승리!";
        }

        if (earnedCoinText != null)
        {
            earnedCoinText.text = string.Format("+{0} 코인", Mathf.Max(0, earnedCoin));
        }

        if (totalCoinText != null)
        {
            totalCoinText.text = Mathf.Max(0, totalCoin).ToString();
        }
    }

    public void SetRanking(string[] names, int[] scores)
    {
        int nameCount = playerNameTexts != null ? playerNameTexts.Length : 0;
        int scoreCount = scoreTexts != null ? scoreTexts.Length : 0;
        int rowCount = Mathf.Max(nameCount, scoreCount);

        for (int index = 0; index < rowCount; index++)
        {
            bool hasName = names != null && index < names.Length && !string.IsNullOrWhiteSpace(names[index]);
            bool hasScore = scores != null && index < scores.Length;

            if (playerNameTexts != null && index < playerNameTexts.Length && playerNameTexts[index] != null)
            {
                playerNameTexts[index].text = hasName ? names[index] : string.Empty;
            }

            if (scoreTexts != null && index < scoreTexts.Length && scoreTexts[index] != null)
            {
                scoreTexts[index].text = hasScore ? scores[index].ToString() : string.Empty;
            }
        }
    }
}
