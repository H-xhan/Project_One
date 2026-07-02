using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public static class ProjectOneCodeOnlyUIFactory
{
    public static Canvas CreateCanvas(string name = "Canvas_ProjectOne_CodeOnlyUI")
    {
        GameObject canvasObject = new GameObject(name, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        Canvas canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.pixelPerfect = false;
        canvas.sortingOrder = 0;

        CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;
        scaler.referencePixelsPerUnit = 100f;

        EnsureEventSystem();
        return canvas;
    }

    public static RectTransform CreateRoot(RectTransform parent, string name)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        RectTransform rect = go.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        return rect;
    }

    public static RectTransform CreatePanel(RectTransform parent, string name, string title, string body, Vector2 position, Vector2 size, ProjectOneCodeOnlyUIStyle style, bool notebookLines = false)
    {
        RectTransform panel = CreateRoot(parent, name);
        SetBox(panel, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), position, size);

        ProjectOneProceduralGraphic graphic = panel.gameObject.AddComponent<ProjectOneProceduralGraphic>();
        graphic.raycastTarget = false;
        graphic.Configure(GetPaper(style), GetLine(style), GetPanelRadius(style), GetBorderWidth(style), notebookLines);
        AddShadow(panel.gameObject, new Vector2(0f, -6f), 0.22f, style);
        panel.gameObject.AddComponent<CanvasGroup>();
        AddAnimator(panel, style, false);

        if (!string.IsNullOrWhiteSpace(title))
        {
            TextMeshProUGUI titleText = AddText(panel, "TitleText", title, GetTitleSize(style), FontStyles.Bold, TextAlignmentOptions.Center, GetNavy(style), style);
            SetBox(titleText.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -72f), new Vector2(size.x - 90f, 120f));
        }

        if (!string.IsNullOrWhiteSpace(body))
        {
            TextMeshProUGUI bodyText = AddText(panel, "BodyText", body, GetBodySize(style), FontStyles.Normal, TextAlignmentOptions.Center, GetMutedNavy(style), style);
            bodyText.textWrappingMode = TextWrappingModes.Normal;
            SetBox(bodyText.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -76f), new Vector2(size.x - 110f, size.y - 220f));
        }

        if (!string.IsNullOrWhiteSpace(title) || !string.IsNullOrWhiteSpace(body))
        {
            RectTransform divider = CreateSolidRect(panel, "Divider", GetLine(style), new Vector2(0f, -150f), new Vector2(size.x - 130f, GetDividerThickness(style)));
            SetBox(divider, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -150f), new Vector2(size.x - 130f, GetDividerThickness(style)));
        }

        return panel;
    }

    public static RectTransform CreateButton(RectTransform parent, string name, string label, Vector2 position, Vector2 size, Color fill, ProjectOneProceduralIconGraphic.IconType iconType, ProjectOneCodeOnlyUIStyle style, bool attentionWiggle = false)
    {
        RectTransform button = CreateRoot(parent, name);
        SetBox(button, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), position, size);

        ProjectOneProceduralGraphic graphic = button.gameObject.AddComponent<ProjectOneProceduralGraphic>();
        graphic.raycastTarget = true;
        graphic.Configure(fill, GetLine(style), GetButtonRadius(style), GetBorderWidth(style), false);
        AddShadow(button.gameObject, new Vector2(0f, -5f), 0.20f, style);

        Button uiButton = button.gameObject.AddComponent<Button>();
        uiButton.transition = Selectable.Transition.None;

        AddAnimator(button, style, true);

        if (iconType != ProjectOneProceduralIconGraphic.IconType.None)
        {
            RectTransform icon = CreateIcon(button, "Icon", iconType, GetBlue(style), GetNavy(style), new Vector2(42f, 0f), new Vector2(size.y * 0.48f, size.y * 0.48f));
            SetBox(icon, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(42f, 0f), new Vector2(size.y * 0.48f, size.y * 0.48f));
        }

        TextMeshProUGUI text = AddText(button, "Label_TMP", label, size.y > 115f ? GetBigButtonSize(style) : GetButtonSize(style), FontStyles.Bold, TextAlignmentOptions.Center, GetNavy(style), style);
        Stretch(text.rectTransform);
        text.rectTransform.offsetMin = new Vector2(70f, 0f);
        text.rectTransform.offsetMax = new Vector2(-38f, 0f);

        CreateIcon(button, "Accent_Dots", ProjectOneProceduralIconGraphic.IconType.Dots, GetBlue(style), GetNavy(style), new Vector2(-34f, 0f), new Vector2(26f, 58f), new Vector2(1f, 0.5f));

        if (attentionWiggle)
            button.gameObject.AddComponent<ProjectOneCodeOnlyWiggle>();

        return button;
    }

    public static RectTransform CreateHudCounter(RectTransform parent, string name, string label, string value, ProjectOneProceduralIconGraphic.IconType iconType, Vector2 position, Vector2 size, ProjectOneCodeOnlyUIStyle style)
    {
        RectTransform root = CreateRoot(parent, name);
        SetBox(root, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), position, size);

        ProjectOneProceduralGraphic bg = root.gameObject.AddComponent<ProjectOneProceduralGraphic>();
        bg.raycastTarget = false;
        bg.Configure(GetPaperSoft(style), GetLine(style), GetPanelRadius(style), GetBorderWidth(style), false);
        AddShadow(root.gameObject, new Vector2(0f, -5f), 0.18f, style);
        root.gameObject.AddComponent<CanvasGroup>();
        AddAnimator(root, style, false);

        RectTransform iconBackplate = CreateCircle(root, "Icon_Backplate", GetYellow(style), new Vector2(58f, 0f), new Vector2(size.y * 0.64f, size.y * 0.64f));
        SetBox(iconBackplate, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(58f, 0f), new Vector2(size.y * 0.64f, size.y * 0.64f));

        RectTransform icon = CreateIcon(root, "Icon", iconType, iconType == ProjectOneProceduralIconGraphic.IconType.Coin ? GetYellow(style) : GetBlue(style), GetNavy(style), new Vector2(58f, 0f), new Vector2(size.y * 0.48f, size.y * 0.48f));
        SetBox(icon, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(58f, 0f), new Vector2(size.y * 0.48f, size.y * 0.48f));

        TextMeshProUGUI labelText = AddText(root, "LabelText", label, GetBodySize(style), FontStyles.Bold, TextAlignmentOptions.MidlineLeft, GetNavy(style), style);
        Stretch(labelText.rectTransform);
        labelText.rectTransform.offsetMin = new Vector2(112f, 0f);
        labelText.rectTransform.offsetMax = new Vector2(-125f, 0f);

        TextMeshProUGUI valueText = AddText(root, "ValueText", value, GetSubtitleSize(style), FontStyles.Bold, TextAlignmentOptions.MidlineRight, GetNavy(style), style);
        Stretch(valueText.rectTransform);
        valueText.rectTransform.offsetMin = new Vector2(145f, 0f);
        valueText.rectTransform.offsetMax = new Vector2(-34f, 0f);

        CreateIcon(root, "MoreDots", ProjectOneProceduralIconGraphic.IconType.Dots, GetBlue(style), GetNavy(style), new Vector2(-22f, 0f), new Vector2(22f, 60f), new Vector2(1f, 0.5f));
        return root;
    }

    public static RectTransform CreateStaminaGauge(RectTransform parent, Vector2 position, Vector2 size, ProjectOneCodeOnlyUIStyle style)
    {
        RectTransform root = CreateHudCounter(parent, "ONE_CodeOnly_StaminaGauge", "스태미너   100 / 100", string.Empty, ProjectOneProceduralIconGraphic.IconType.Lightning, position, size, style);

        RectTransform barBg = CreatePanelShape(root, "Bar_Background", new Color(0.70f, 0.63f, 0.52f, 0.28f), GetLine(style), 18f, 0f);
        SetBox(barBg, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f), Vector2.zero, Vector2.zero);
        barBg.offsetMin = new Vector2(132f, 28f);
        barBg.offsetMax = new Vector2(-52f, 58f);

        RectTransform fill = CreatePanelShape(barBg, "Bar_Fill", GetMint(style), Color.clear, 18f, 0f);
        fill.anchorMin = Vector2.zero;
        fill.anchorMax = Vector2.one;
        fill.pivot = new Vector2(0f, 0.5f);
        fill.offsetMin = Vector2.zero;
        fill.offsetMax = Vector2.zero;

        return root;
    }

    public static RectTransform CreateIcon(RectTransform parent, string name, ProjectOneProceduralIconGraphic.IconType type, Color main, Color secondary, Vector2 position, Vector2 size)
    {
        return CreateIcon(parent, name, type, main, secondary, position, size, new Vector2(0.5f, 0.5f));
    }

    public static RectTransform CreateIcon(RectTransform parent, string name, ProjectOneProceduralIconGraphic.IconType type, Color main, Color secondary, Vector2 position, Vector2 size, Vector2 anchor)
    {
        RectTransform icon = CreateRoot(parent, name);
        SetBox(icon, anchor, anchor, anchor, position, size);
        ProjectOneProceduralIconGraphic graphic = icon.gameObject.AddComponent<ProjectOneProceduralIconGraphic>();
        graphic.raycastTarget = false;
        graphic.Configure(type, main, secondary, Mathf.Max(4f, Mathf.Min(size.x, size.y) * 0.09f));
        return icon;
    }

    public static TextMeshProUGUI AddText(RectTransform parent, string name, string text, int fontSize, FontStyles fontStyle, TextAlignmentOptions alignment, Color color, ProjectOneCodeOnlyUIStyle style)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        TextMeshProUGUI tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = fontSize;
        tmp.fontStyle = fontStyle;
        tmp.alignment = alignment;
        tmp.color = color;
        tmp.raycastTarget = false;
        tmp.textWrappingMode = TextWrappingModes.NoWrap;
        if (style != null && style.mainFont != null)
            tmp.font = style.mainFont;
        Stretch(tmp.rectTransform);
        return tmp;
    }

    public static RectTransform CreateTape(RectTransform parent, string name, Vector2 position, Vector2 size, float rotation, Color color)
    {
        RectTransform tape = CreatePanelShape(parent, name, new Color(color.r, color.g, color.b, 0.82f), Color.clear, 6f, 0f);
        SetBox(tape, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), position, size);
        tape.localRotation = Quaternion.Euler(0f, 0f, rotation);
        return tape;
    }

    public static RectTransform CreatePaperClip(RectTransform parent, string name, Vector2 position, float rotation, Color color)
    {
        RectTransform root = CreateRoot(parent, name);
        SetBox(root, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), position, new Vector2(62f, 104f));
        root.localRotation = Quaternion.Euler(0f, 0f, rotation);
        ProjectOneProceduralIconGraphic icon = root.gameObject.AddComponent<ProjectOneProceduralIconGraphic>();
        icon.raycastTarget = false;
        icon.Configure(ProjectOneProceduralIconGraphic.IconType.Check, color, color, 10f);
        return root;
    }

    public static RectTransform CreatePanelShape(RectTransform parent, string name, Color fill, Color border, float radius, float borderWidth)
    {
        RectTransform rect = CreateRoot(parent, name);
        ProjectOneProceduralGraphic graphic = rect.gameObject.AddComponent<ProjectOneProceduralGraphic>();
        graphic.raycastTarget = false;
        graphic.Configure(fill, border, radius, borderWidth, false);
        return rect;
    }

    private static RectTransform CreateCircle(RectTransform parent, string name, Color fill, Vector2 position, Vector2 size)
    {
        RectTransform rect = CreateRoot(parent, name);
        SetBox(rect, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), position, size);
        ProjectOneProceduralGraphic graphic = rect.gameObject.AddComponent<ProjectOneProceduralGraphic>();
        graphic.raycastTarget = false;
        graphic.Configure(fill, Color.clear, Mathf.Min(size.x, size.y) * 0.5f, 0f, false);
        return rect;
    }

    public static void SetBox(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 anchoredPosition, Vector2 size)
    {
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.pivot = pivot;
        rect.anchoredPosition = anchoredPosition;
        rect.sizeDelta = size;
    }

    public static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    private static RectTransform CreateSolidRect(RectTransform parent, string name, Color fill, Vector2 position, Vector2 size)
    {
        RectTransform rect = CreatePanelShape(parent, name, fill, Color.clear, 0f, 0f);
        SetBox(rect, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), position, size);
        return rect;
    }

    private static void AddAnimator(RectTransform rect, ProjectOneCodeOnlyUIStyle style, bool pointerAnimation)
    {
        ProjectOneCodeOnlyAnimator animator = rect.gameObject.AddComponent<ProjectOneCodeOnlyAnimator>();
        if (style != null)
            animator.Configure(style.hoverScale, style.pressScale, style.scaleDuration, style.introDuration);
        animator.SetPointerAnimationEnabled(pointerAnimation);
    }

    private static void AddShadow(GameObject target, Vector2 distance, float alpha, ProjectOneCodeOnlyUIStyle style)
    {
        Shadow shadow = target.AddComponent<Shadow>();
        Color baseColor = style != null ? style.shadowColor : new Color(0f, 0f, 0f, 0.25f);
        shadow.effectColor = new Color(baseColor.r, baseColor.g, baseColor.b, alpha);
        shadow.effectDistance = distance;
        shadow.useGraphicAlpha = true;
    }

    private static void EnsureEventSystem()
    {
#if UNITY_2023_1_OR_NEWER
        EventSystem existing = Object.FindFirstObjectByType<EventSystem>();
#else
        EventSystem existing = Object.FindObjectOfType<EventSystem>();
#endif
        if (existing != null)
            return;
        new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
    }

    private static Color GetPaper(ProjectOneCodeOnlyUIStyle s) => s != null ? s.paperColor : new Color(1.00f, 0.94f, 0.82f, 1.00f);
    private static Color GetPaperSoft(ProjectOneCodeOnlyUIStyle s) => s != null ? s.paperSoftColor : new Color(1.00f, 0.97f, 0.90f, 1.00f);
    private static Color GetNavy(ProjectOneCodeOnlyUIStyle s) => s != null ? s.navyColor : new Color(0.10f, 0.16f, 0.35f, 1.00f);
    private static Color GetMutedNavy(ProjectOneCodeOnlyUIStyle s) => s != null ? s.mutedNavyColor : new Color(0.32f, 0.38f, 0.55f, 1.00f);
    private static Color GetYellow(ProjectOneCodeOnlyUIStyle s) => s != null ? s.yellowColor : new Color(1.00f, 0.78f, 0.24f, 1.00f);
    private static Color GetBlue(ProjectOneCodeOnlyUIStyle s) => s != null ? s.blueColor : new Color(0.32f, 0.62f, 0.95f, 1.00f);
    private static Color GetMint(ProjectOneCodeOnlyUIStyle s) => s != null ? s.mintColor : new Color(0.43f, 0.82f, 0.72f, 1.00f);
    private static Color GetLine(ProjectOneCodeOnlyUIStyle s) => s != null ? s.lineColor : new Color(0.72f, 0.65f, 0.55f, 0.55f);
    private static float GetPanelRadius(ProjectOneCodeOnlyUIStyle s) => s != null ? s.panelRadius : 26f;
    private static float GetButtonRadius(ProjectOneCodeOnlyUIStyle s) => s != null ? s.buttonRadius : 38f;
    private static float GetBorderWidth(ProjectOneCodeOnlyUIStyle s) => s != null ? s.borderWidth : 3f;
    private static float GetDividerThickness(ProjectOneCodeOnlyUIStyle s) => s != null ? s.dividerThickness : 2f;
    private static int GetTitleSize(ProjectOneCodeOnlyUIStyle s) => s != null ? s.titleSize : 58;
    private static int GetSubtitleSize(ProjectOneCodeOnlyUIStyle s) => s != null ? s.subtitleSize : 30;
    private static int GetBodySize(ProjectOneCodeOnlyUIStyle s) => s != null ? s.bodySize : 28;
    private static int GetButtonSize(ProjectOneCodeOnlyUIStyle s) => s != null ? s.buttonSize : 34;
    private static int GetBigButtonSize(ProjectOneCodeOnlyUIStyle s) => s != null ? s.bigButtonSize : 66;
}
