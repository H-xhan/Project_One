using TMPro;
using UnityEngine;

[CreateAssetMenu(fileName = "ProjectOneCodeOnlyUIStyle", menuName = "Project One/UI/Code Only Style")]
public sealed class ProjectOneCodeOnlyUIStyle : ScriptableObject
{
    [Header("Font (optional, not an image resource)")]
    public TMP_FontAsset mainFont;

    [Header("Brand Colors")]
    public Color paperColor = new Color(1.00f, 0.94f, 0.82f, 1.00f);
    public Color paperSoftColor = new Color(1.00f, 0.97f, 0.90f, 1.00f);
    public Color navyColor = new Color(0.10f, 0.16f, 0.35f, 1.00f);
    public Color mutedNavyColor = new Color(0.32f, 0.38f, 0.55f, 1.00f);
    public Color yellowColor = new Color(1.00f, 0.78f, 0.24f, 1.00f);
    public Color blueColor = new Color(0.32f, 0.62f, 0.95f, 1.00f);
    public Color mintColor = new Color(0.43f, 0.82f, 0.72f, 1.00f);
    public Color redColor = new Color(0.95f, 0.38f, 0.34f, 1.00f);
    public Color lineColor = new Color(0.72f, 0.65f, 0.55f, 0.55f);
    public Color shadowColor = new Color(0.10f, 0.08f, 0.06f, 0.25f);

    [Header("Shape")]
    [Range(0f, 80f)] public float panelRadius = 26f;
    [Range(0f, 80f)] public float buttonRadius = 38f;
    [Range(0f, 16f)] public float borderWidth = 3f;
    [Range(0f, 16f)] public float dividerThickness = 2f;

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
