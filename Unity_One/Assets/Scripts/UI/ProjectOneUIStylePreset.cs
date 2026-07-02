using TMPro;
using UnityEngine;

[CreateAssetMenu(fileName = "ProjectOneUIStylePreset", menuName = "Project One/UI/Style Preset")]
public sealed class ProjectOneUIStylePreset : ScriptableObject
{
    [Header("Font")]
    public TMP_FontAsset mainFont;

    [Header("Paper / Panel Sprites")]
    public Sprite paperPanelLarge;
    public Sprite paperPanelSmall;
    public Sprite paperButton;
    public Sprite yellowButton;
    public Sprite bluePillPanel;
    public Sprite noteTag;

    [Header("Decoration Sprites")]
    public Sprite blueTape;
    public Sprite yellowTape;
    public Sprite paperClip;
    public Sprite pin;
    public Sprite cornerFold;

    [Header("Icon Sprites")]
    public Sprite pawIcon;
    public Sprite coinIcon;
    public Sprite clockIcon;
    public Sprite peopleIcon;
    public Sprite missionIcon;
    public Sprite playIcon;
    public Sprite homeIcon;
    public Sprite exitIcon;

    [Header("Character / Result Sprites")]
    public Sprite defaultCharacterPortrait;
    public Sprite crownIcon;
    public Sprite successStamp;

    [Header("Brand Colors")]
    public Color paperColor = new Color(1.0f, 0.94f, 0.82f, 1.0f);
    public Color paperSoftColor = new Color(1.0f, 0.97f, 0.90f, 1.0f);
    public Color navyColor = new Color(0.10f, 0.16f, 0.35f, 1.0f);
    public Color mutedNavyColor = new Color(0.32f, 0.38f, 0.55f, 1.0f);
    public Color yellowColor = new Color(1.0f, 0.78f, 0.24f, 1.0f);
    public Color blueColor = new Color(0.32f, 0.62f, 0.95f, 1.0f);
    public Color mintColor = new Color(0.43f, 0.82f, 0.72f, 1.0f);
    public Color redColor = new Color(0.95f, 0.38f, 0.34f, 1.0f);
    public Color lineColor = new Color(0.72f, 0.65f, 0.55f, 0.55f);
    public Color shadowColor = new Color(0.10f, 0.08f, 0.06f, 0.25f);

    [Header("Typography")]
    public int titleSize = 58;
    public int subtitleSize = 30;
    public int bodySize = 28;
    public int smallSize = 22;
    public int buttonSize = 34;
    public int bigButtonSize = 66;

    [Header("Animation Defaults")]
    [Range(1.0f, 1.12f)] public float hoverScale = 1.045f;
    [Range(0.85f, 1.0f)] public float pressScale = 0.955f;
    [Range(0.04f, 0.25f)] public float scaleDuration = 0.09f;
    [Range(0.04f, 0.4f)] public float introDuration = 0.18f;
}
