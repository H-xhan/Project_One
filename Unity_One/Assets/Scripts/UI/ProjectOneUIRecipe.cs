using TMPro;
using UnityEngine;

[CreateAssetMenu(fileName = "ProjectOneUIRecipe", menuName = "Project ONE/UI/Reference UI Recipe")]
public sealed class ProjectOneUIRecipe : ScriptableObject
{
    [Header("Colors")]
    public Color paperCream = new Color32(0xF8, 0xEF, 0xDE, 0xFF);
    public Color paperWarm = new Color32(0xF3, 0xE3, 0xC8, 0xFF);
    public Color mainNavy = new Color32(0x1B, 0x2F, 0x67, 0xFF);
    public Color buttonYellow = new Color32(0xF6, 0xD6, 0x52, 0xFF);
    public Color infoSky = new Color32(0x78, 0xC7, 0xF2, 0xFF);
    public Color mint = new Color32(0x7D, 0xDF, 0xC9, 0xFF);
    public Color coralRed = new Color32(0xF2, 0x6D, 0x6D, 0xFF);

    [Header("Fonts")]
    public TMP_FontAsset defaultFont;
    public TMP_FontAsset fallbackFont;

    [Header("Sprites")]
    public Sprite paperPanelSprite;
    public Sprite yellowButtonSprite;
    public Sprite paperButtonSprite;

    [Header("Sizes")]
    public Vector2 menuButtonSize = new Vector2(285f, 64f);
    public Vector2 largeButtonSize = new Vector2(310f, 74f);
    public Vector2 loadingCardSize = new Vector2(760f, 360f);
    public Vector2 resultCardSize = new Vector2(950f, 520f);

    [Header("Layout")]
    public float menuPanelRotation = -5f;
    public int menuTextSize = 32;
    public int titleTextSize = 56;
    public int bodyTextSize = 24;
    public float buttonSpacing = 26f;
    public Vector2 cardShadowOffset = new Vector2(6f, -6f);
    public float cardShadowAlpha = 0.16f;
}
