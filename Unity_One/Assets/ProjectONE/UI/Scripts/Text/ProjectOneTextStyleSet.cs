using TMPro;
using UnityEngine;

[CreateAssetMenu(fileName = "ProjectOneTextStyleSet", menuName = "Project ONE/UI/Text Style Set")]
public class ProjectOneTextStyleSet : ScriptableObject
{
    [Header("Font")]
    public TMP_FontAsset defaultFont;

    [Header("Project ONE UI Kit Colors")]
    public Color navy = new Color32(0x1F, 0x2A, 0x44, 0xFF);
    public Color cream = new Color32(0xFF, 0xF7, 0xEB, 0xFF);
    public Color yellow = new Color32(0xFF, 0xD5, 0x6A, 0xFF);
    public Color blue = new Color32(0xB8, 0xD3, 0xF6, 0xFF);
    public Color mint = new Color32(0xCD, 0xEB, 0xD7, 0xFF);
    public Color white = new Color32(0xFF, 0xFF, 0xFF, 0xFF);
    public Color subGray = new Color32(0x6F, 0x74, 0x80, 0xFF);
    public Color accentBlue = new Color32(0x2F, 0x80, 0xED, 0xFF);
    public Color softDark = new Color32(0x3D, 0x46, 0x58, 0xFF);

    [Header("Font Sizes")]
    public float logoProjectSize = 68f;
    public float logoOneSize = 104f;
    public float resultTitleSize = 88f;
    public float readyLargeSize = 88f;
    public float screenTitleSize = 48f;
    public float menuButtonSize = 36f;
    public float buttonLabelSize = 36f;
    public float hudNumberSize = 54f;
    public float hudLabelSize = 24f;
    public float cardTitleSize = 38f;
    public float cardBodySize = 25f;
    public float smallCaptionSize = 19f;
    public float tagLabelSize = 26f;
    public float rankingNameSize = 22f;
    public float rankingScoreSize = 28f;
}

public enum ProjectOneTextStyleType
{
    LogoProject,
    LogoOne,
    ResultTitle,
    ReadyLarge,
    ScreenTitle,
    MenuButton,
    ButtonLabel,
    HUDNumber,
    HUDLabel,
    CardTitle,
    CardBody,
    SmallCaption,
    WhiteTabLabel,
    BlueAccentText,
    RankingName,
    RankingScore,
    RoomCodeLabel,
    RoomCodeValue
}
