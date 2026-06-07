using TMPro;
using UnityEngine;
using UnityEngine.UI;

[ExecuteAlways]
[DisallowMultipleComponent]
public class ProjectOneTextStyleApplier : MonoBehaviour
{
    private const string ShadowObjectSuffix = "_ProjectOneSoftShadow";

    public TMP_Text target;
    public ProjectOneTextStyleSet styleSet;
    public ProjectOneTextStyleType styleType;
    public bool useSoftShadow;
    public bool useWhiteOutline;
    public bool useNavyOutline;
    public Vector2 shadowOffset = new Vector2(2f, -2f);
    public Color shadowColor = new Color(0f, 0f, 0f, 0.25f);

    [SerializeField, HideInInspector]
    private TMP_Text shadowText;

    public void ApplyStyle()
    {
        if (target == null)
        {
            target = GetComponent<TMP_Text>();
        }

        if (target == null)
        {
            return;
        }

        StyleValues values = ResolveStyleValues();
        ApplyTextValues(values);
        RemoveLegacyShadowChild();
        ApplyGraphicEffects(values);

        if (!Application.isPlaying)
        {
            target.SetMaterialDirty();
            target.SetVerticesDirty();
            target.SetLayoutDirty();
        }
    }

    private void Reset()
    {
        target = GetComponent<TMP_Text>();
        ApplyStyle();
    }

    private void OnEnable()
    {
        ApplyStyle();
    }

    private void OnValidate()
    {
        ApplyStyle();
    }

    private void ApplyTextValues(StyleValues values)
    {
        if (styleSet != null && styleSet.defaultFont != null)
        {
            target.font = styleSet.defaultFont;
        }
        else if (target.font == null)
        {
            Debug.LogWarning("ProjectOneTextStyleApplier: No Project ONE TMP font asset is assigned. TMP default font will be used temporarily.", this);
        }

        target.fontSize = values.FontSize;
        target.color = values.Color;
        target.alignment = values.Alignment;
        target.fontStyle = values.FontStyle;
        target.fontWeight = values.FontWeight;
        target.enableWordWrapping = values.Wrap;
        target.overflowMode = values.OverflowMode;
        target.characterSpacing = values.CharacterSpacing;
        target.lineSpacing = values.LineSpacing;
        target.extraPadding = true;
        target.richText = true;
        target.raycastTarget = false;
        target.enableAutoSizing = false;
    }

    private void ApplyGraphicEffects(StyleValues values)
    {
        bool shadowEnabled = useSoftShadow || values.UseShadow;
        Color effectiveShadowColor = useSoftShadow ? shadowColor : values.ShadowColor;
        Vector2 effectiveShadowOffset = useSoftShadow ? shadowOffset : values.ShadowDistance;
        ConfigureExactEffect<Shadow>(shadowEnabled, effectiveShadowColor, effectiveShadowOffset);

        bool outlineEnabled = useWhiteOutline || useNavyOutline || values.UseOutline;
        Color outlineColor = values.OutlineColor;
        if (useWhiteOutline)
        {
            outlineColor = GetWhite();
        }

        if (useNavyOutline)
        {
            outlineColor = GetNavy();
        }

        ConfigureExactEffect<UnityEngine.UI.Outline>(outlineEnabled, outlineColor, values.OutlineDistance);

        target.raycastTarget = false;
    }

    private void ConfigureExactEffect<T>(bool enabled, Color effectColor, Vector2 effectDistance) where T : Shadow
    {
        T effect = GetExactComponent<T>();
        if (!enabled)
        {
            if (effect != null)
            {
                DestroyObject(effect);
            }

            return;
        }

        if (effect == null)
        {
            effect = gameObject.AddComponent<T>();
        }

        effect.effectColor = effectColor;
        effect.effectDistance = effectDistance;
        effect.useGraphicAlpha = true;
        effect.enabled = true;
    }

    private T GetExactComponent<T>() where T : Component
    {
        T[] components = GetComponents<T>();
        for (int index = 0; index < components.Length; index++)
        {
            T component = components[index];
            if (component != null && component.GetType() == typeof(T))
            {
                return component;
            }
        }

        return null;
    }

    private void RemoveLegacyShadowChild()
    {
        if (shadowText != null)
        {
            DestroyObject(shadowText.gameObject);
            shadowText = null;
        }

        Transform parent = target.transform.parent;
        if (parent == null)
        {
            return;
        }

        Transform legacyShadow = parent.Find(target.name + ShadowObjectSuffix);
        if (legacyShadow != null)
        {
            DestroyObject(legacyShadow.gameObject);
        }
    }

    private static void DestroyObject(Object targetObject)
    {
        if (targetObject == null)
        {
            return;
        }

        if (Application.isPlaying)
        {
            Destroy(targetObject);
        }
        else
        {
            DestroyImmediate(targetObject);
        }
    }

    private StyleValues ResolveStyleValues()
    {
        StyleValues values = new StyleValues
        {
            FontSize = 24f,
            Color = GetNavy(),
            Alignment = TextAlignmentOptions.Center,
            FontStyle = FontStyles.Bold,
            FontWeight = FontWeight.Bold,
            Wrap = false,
            OverflowMode = TextOverflowModes.Overflow,
            CharacterSpacing = 0f,
            LineSpacing = 0f,
            UseShadow = false,
            ShadowColor = new Color(0f, 0f, 0f, 0.22f),
            ShadowDistance = new Vector2(2f, -2f),
            UseOutline = false,
            OutlineColor = GetWhite(),
            OutlineDistance = new Vector2(2f, -2f)
        };

        switch (styleType)
        {
            case ProjectOneTextStyleType.LogoProject:
                values.FontSize = GetSize(styleSet != null ? styleSet.logoProjectSize : 68f, 68f);
                values.FontWeight = FontWeight.Bold;
                values.CharacterSpacing = 2f;
                break;
            case ProjectOneTextStyleType.LogoOne:
                values.FontSize = GetSize(styleSet != null ? styleSet.logoOneSize : 104f, 104f);
                values.FontWeight = FontWeight.Black;
                values.CharacterSpacing = -2f;
                values.UseShadow = true;
                values.ShadowColor = new Color(0f, 0f, 0f, 0.22f);
                values.ShadowDistance = new Vector2(3f, -3f);
                break;
            case ProjectOneTextStyleType.ResultTitle:
                values.FontSize = GetSize(styleSet != null ? styleSet.resultTitleSize : 88f, 88f);
                values.FontWeight = FontWeight.Black;
                values.CharacterSpacing = -2f;
                values.UseShadow = true;
                values.ShadowColor = new Color(0f, 0f, 0f, 0.16f);
                values.ShadowDistance = new Vector2(2f, -2f);
                break;
            case ProjectOneTextStyleType.ReadyLarge:
                values.FontSize = GetSize(styleSet != null ? styleSet.readyLargeSize : 88f, 88f);
                values.FontWeight = FontWeight.Black;
                values.CharacterSpacing = -1f;
                values.UseShadow = true;
                values.ShadowColor = new Color(0f, 0f, 0f, 0.22f);
                values.ShadowDistance = new Vector2(3f, -3f);
                values.UseOutline = true;
                values.OutlineColor = GetWhite();
                values.OutlineDistance = new Vector2(2f, -2f);
                break;
            case ProjectOneTextStyleType.ScreenTitle:
                values.FontSize = GetSize(styleSet != null ? styleSet.screenTitleSize : 48f, 48f);
                values.FontWeight = FontWeight.Bold;
                values.CharacterSpacing = -1f;
                values.Wrap = true;
                break;
            case ProjectOneTextStyleType.MenuButton:
                values.FontSize = GetSize(styleSet != null ? styleSet.menuButtonSize : 36f, 36f);
                values.FontWeight = FontWeight.Bold;
                values.CharacterSpacing = -1f;
                break;
            case ProjectOneTextStyleType.ButtonLabel:
                values.FontSize = GetSize(styleSet != null ? styleSet.buttonLabelSize : 36f, 36f);
                values.FontWeight = FontWeight.Bold;
                values.CharacterSpacing = -1f;
                break;
            case ProjectOneTextStyleType.HUDNumber:
                values.FontSize = GetSize(styleSet != null ? styleSet.hudNumberSize : 54f, 54f);
                values.FontWeight = FontWeight.Black;
                values.CharacterSpacing = -1f;
                break;
            case ProjectOneTextStyleType.HUDLabel:
                values.FontSize = GetSize(styleSet != null ? styleSet.hudLabelSize : 24f, 24f);
                values.FontWeight = FontWeight.Bold;
                break;
            case ProjectOneTextStyleType.CardTitle:
                values.FontSize = GetSize(styleSet != null ? styleSet.cardTitleSize : 38f, 38f);
                values.FontWeight = FontWeight.Bold;
                values.Alignment = TextAlignmentOptions.Left;
                break;
            case ProjectOneTextStyleType.CardBody:
                values.FontSize = GetSize(styleSet != null ? styleSet.cardBodySize : 25f, 25f);
                values.FontStyle = FontStyles.Normal;
                values.FontWeight = FontWeight.Regular;
                values.Alignment = TextAlignmentOptions.TopLeft;
                values.Wrap = true;
                values.OverflowMode = TextOverflowModes.Ellipsis;
                values.LineSpacing = 8f;
                break;
            case ProjectOneTextStyleType.SmallCaption:
                values.FontSize = GetSize(styleSet != null ? styleSet.smallCaptionSize : 19f, 19f);
                values.Color = styleSet != null ? styleSet.subGray : new Color32(0x6F, 0x74, 0x80, 0xFF);
                values.FontStyle = FontStyles.Normal;
                values.FontWeight = FontWeight.Regular;
                values.Wrap = true;
                break;
            case ProjectOneTextStyleType.WhiteTabLabel:
                values.FontSize = GetSize(styleSet != null ? styleSet.tagLabelSize : 26f, 26f);
                values.Color = GetWhite();
                values.FontWeight = FontWeight.Bold;
                values.UseShadow = true;
                values.ShadowColor = new Color(0f, 0f, 0f, 0.18f);
                values.ShadowDistance = new Vector2(1.5f, -1.5f);
                break;
            case ProjectOneTextStyleType.BlueAccentText:
                values.FontSize = 26f;
                values.Color = styleSet != null ? styleSet.accentBlue : new Color32(0x2F, 0x80, 0xED, 0xFF);
                values.FontWeight = FontWeight.Bold;
                values.Alignment = TextAlignmentOptions.Left;
                values.Wrap = true;
                break;
            case ProjectOneTextStyleType.RankingName:
                values.FontSize = GetSize(styleSet != null ? styleSet.rankingNameSize : 22f, 22f);
                values.FontWeight = FontWeight.Bold;
                values.Alignment = TextAlignmentOptions.Left;
                break;
            case ProjectOneTextStyleType.RankingScore:
                values.FontSize = GetSize(styleSet != null ? styleSet.rankingScoreSize : 28f, 28f);
                values.FontWeight = FontWeight.Black;
                break;
            case ProjectOneTextStyleType.RoomCodeLabel:
                values.FontSize = 24f;
                values.FontStyle = FontStyles.Normal;
                values.FontWeight = FontWeight.Regular;
                values.CharacterSpacing = 1f;
                break;
            case ProjectOneTextStyleType.RoomCodeValue:
                values.FontSize = 40f;
                values.FontWeight = FontWeight.Black;
                values.CharacterSpacing = 1f;
                break;
        }

        return values;
    }

    private static float GetSize(float configuredSize, float fallbackSize)
    {
        return configuredSize > 0f ? configuredSize : fallbackSize;
    }

    private Color GetNavy()
    {
        return styleSet != null ? styleSet.navy : new Color32(0x1F, 0x2A, 0x44, 0xFF);
    }

    private Color GetWhite()
    {
        return styleSet != null ? styleSet.white : Color.white;
    }

    private struct StyleValues
    {
        public float FontSize;
        public Color Color;
        public TextAlignmentOptions Alignment;
        public FontStyles FontStyle;
        public FontWeight FontWeight;
        public bool Wrap;
        public TextOverflowModes OverflowMode;
        public float CharacterSpacing;
        public float LineSpacing;
        public bool UseShadow;
        public Color ShadowColor;
        public Vector2 ShadowDistance;
        public bool UseOutline;
        public Color OutlineColor;
        public Vector2 OutlineDistance;
    }
}
