using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public static class ProjectOneUIProblemSpriteAuditor
{
    private const string MenuPath = "Project ONE/UI/Create Problem UI Audit Scene";
    private const string SpriteRoot = "Assets/ProjectONE/UI/Sprites";
    private const string SceneFolder = "Assets/ProjectONE/UI/Scenes";
    private const string ScenePath = SceneFolder + "/UI_Problem_Audit.unity";
    private const string ReportPath = "Assets/ProjectONE/UI/Sprites/_Manifest/problem_ui_audit_report.csv";

    private static readonly string[] TargetFileNames =
    {
        "004_top_button_notice.png",
        "008_mission_card_blank_stamp.png",
        "010_stamina_bar_full.png",
        "025_ready_status_panel.png",
        "042_long_blank_paper_banner_tape.png",
        "046_long_blank_pill_panel.png",
        "047_long_blank_rounded_label.png"
    };

    [MenuItem(MenuPath)]
    public static void CreateProblemAuditScene()
    {
        if (!AssetDatabase.IsValidFolder(SpriteRoot))
        {
            Debug.LogError($"Project ONE UI Problem Sprite Auditor: Sprite root folder does not exist: {SpriteRoot}");
            return;
        }

        if (SceneFileExists() &&
            !EditorUtility.DisplayDialog(
                "Overwrite Problem UI Audit Scene",
                $"{ScenePath} already exists. Overwrite the generated audit scene?",
                "Overwrite",
                "Cancel"))
        {
            Debug.Log("Project ONE UI Problem Sprite Auditor: Scene creation cancelled.");
            return;
        }

        EnsureFolder(SceneFolder);
        EnsureFolder(Path.GetDirectoryName(ReportPath));

        List<AuditSpriteInfo> auditSprites = CollectAuditSprites();
        int foundCount = auditSprites.Count(info => info.Found);
        int missingCount = auditSprites.Count - foundCount;
        if (foundCount == 0)
        {
            Debug.LogWarning("Project ONE UI Problem Sprite Auditor: No problem candidate sprites were found. Scene generation skipped.");
            WriteReport(auditSprites);
            Debug.Log($"Project ONE UI Problem Sprite Auditor Report: found count: {foundCount}, missing count: {missingCount}, generated audit item count: 0, report path: {ReportPath}");
            return;
        }

        Scene auditScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        EditorSceneManager.SetActiveScene(auditScene);

        int generatedItemCount = 0;
        try
        {
            BuildScene(auditSprites.Where(info => info.Found), out generatedItemCount);
            WriteReport(auditSprites);

            if (!EditorSceneManager.SaveScene(auditScene, ScenePath))
            {
                Debug.LogError($"Project ONE UI Problem Sprite Auditor: Failed to save scene at {ScenePath}");
                return;
            }

            AssetDatabase.Refresh();
            EditorSceneManager.SetActiveScene(auditScene);
            SceneAsset sceneAsset = AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath);
            if (sceneAsset != null)
            {
                Selection.activeObject = sceneAsset;
                EditorGUIUtility.PingObject(sceneAsset);
            }

            Debug.Log(
                "Project ONE UI Problem Sprite Auditor complete. " +
                $"found count: {foundCount}, " +
                $"missing count: {missingCount}, " +
                $"generated audit item count: {generatedItemCount}, " +
                $"report path: {ReportPath}");
        }
        catch (Exception exception)
        {
            Debug.LogError($"Project ONE UI Problem Sprite Auditor failed: {exception}");
        }
    }

    private static List<AuditSpriteInfo> CollectAuditSprites()
    {
        Dictionary<string, string> pathsByFileName = FindTargetSpritePaths();
        List<AuditSpriteInfo> results = new List<AuditSpriteInfo>();

        foreach (string fileName in TargetFileNames)
        {
            if (!pathsByFileName.TryGetValue(fileName, out string assetPath))
            {
                Debug.LogWarning($"Project ONE UI Problem Sprite Auditor: Candidate sprite not found: {fileName}");
                results.Add(AuditSpriteInfo.Missing(fileName));
                continue;
            }

            Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
            TextureImporter importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
            if (sprite == null)
            {
                Debug.LogWarning($"Project ONE UI Problem Sprite Auditor: Candidate PNG is not loadable as Sprite: {assetPath}");
                results.Add(AuditSpriteInfo.Missing(fileName, assetPath));
                continue;
            }

            results.Add(AuditSpriteInfo.FoundSprite(fileName, assetPath, sprite, texture, importer));
        }

        return results;
    }

    private static Dictionary<string, string> FindTargetSpritePaths()
    {
        HashSet<string> targetNames = new HashSet<string>(TargetFileNames, StringComparer.OrdinalIgnoreCase);
        Dictionary<string, string> pathsByFileName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        string[] guids = AssetDatabase.FindAssets("t:Texture2D", new[] { SpriteRoot });
        foreach (string guid in guids)
        {
            string assetPath = NormalizeAssetPath(AssetDatabase.GUIDToAssetPath(guid));
            string fileName = Path.GetFileName(assetPath);
            if (!assetPath.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
                !targetNames.Contains(fileName) ||
                pathsByFileName.ContainsKey(fileName))
            {
                continue;
            }

            pathsByFileName[fileName] = assetPath;
        }

        return pathsByFileName;
    }

    private static void BuildScene(IEnumerable<AuditSpriteInfo> foundSprites, out int generatedItemCount)
    {
        generatedItemCount = 0;

        GameObject root = new GameObject("UI_Problem_Audit");
        CreateCamera(root.transform);
        CreateEventSystem(root.transform);

        GameObject canvasObject = CreateCanvas(root.transform);
        RectTransform canvasTransform = canvasObject.GetComponent<RectTransform>();

        CreateBackground(canvasTransform);
        CreateHeader(canvasTransform);
        RectTransform contentTransform = CreateAuditScrollView(canvasTransform);
        CreateFooter(canvasTransform);

        foreach (AuditSpriteInfo info in foundSprites)
        {
            CreateAuditItem(contentTransform, info);
            generatedItemCount++;
        }
    }

    private static void CreateCamera(Transform parent)
    {
        GameObject cameraObject = new GameObject("Camera");
        cameraObject.transform.SetParent(parent, false);
        Camera camera = cameraObject.AddComponent<Camera>();
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = ParseColor("#1F2A44");
        camera.orthographic = true;
        camera.transform.position = new Vector3(0f, 0f, -10f);
        cameraObject.tag = "MainCamera";
    }

    private static void CreateEventSystem(Transform parent)
    {
        GameObject eventSystemObject = new GameObject("EventSystem");
        eventSystemObject.transform.SetParent(parent, false);
        eventSystemObject.AddComponent<EventSystem>();
        eventSystemObject.AddComponent<StandaloneInputModule>();
    }

    private static GameObject CreateCanvas(Transform parent)
    {
        GameObject canvasObject = new GameObject("Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasObject.transform.SetParent(parent, false);

        Canvas canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;

        CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1536f, 864f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;

        return canvasObject;
    }

    private static void CreateBackground(RectTransform parent)
    {
        GameObject backgroundObject = CreateUIObject("Background_Navy", parent);
        Image image = backgroundObject.AddComponent<Image>();
        image.color = ParseColor("#1F2A44");
        image.raycastTarget = false;

        RectTransform rectTransform = backgroundObject.GetComponent<RectTransform>();
        Stretch(rectTransform);
        rectTransform.SetAsFirstSibling();
    }

    private static void CreateHeader(RectTransform parent)
    {
        GameObject headerObject = CreateUIObject("Header_Text", parent);
        RectTransform rectTransform = headerObject.GetComponent<RectTransform>();
        rectTransform.anchorMin = new Vector2(0.5f, 1f);
        rectTransform.anchorMax = new Vector2(0.5f, 1f);
        rectTransform.pivot = new Vector2(0.5f, 1f);
        rectTransform.anchoredPosition = new Vector2(0f, -12f);
        rectTransform.sizeDelta = new Vector2(1000f, 60f);

        AddText(headerObject, "Project ONE Problem UI Audit", 34f, Color.white, TextAlignmentOptions.Center, TextAnchor.MiddleCenter);
    }

    private static RectTransform CreateAuditScrollView(RectTransform parent)
    {
        GameObject scrollViewObject = CreateUIObject("AuditScrollView", parent);
        RectTransform scrollViewTransform = scrollViewObject.GetComponent<RectTransform>();
        scrollViewTransform.anchorMin = Vector2.zero;
        scrollViewTransform.anchorMax = Vector2.one;
        scrollViewTransform.pivot = new Vector2(0.5f, 0.5f);
        scrollViewTransform.offsetMin = new Vector2(40f, 40f);
        scrollViewTransform.offsetMax = new Vector2(-40f, -80f);

        Image scrollViewImage = scrollViewObject.AddComponent<Image>();
        scrollViewImage.color = new Color(0f, 0f, 0f, 0f);
        scrollViewImage.raycastTarget = true;

        ScrollRect scrollRect = scrollViewObject.AddComponent<ScrollRect>();
        scrollRect.horizontal = false;
        scrollRect.vertical = true;
        scrollRect.movementType = ScrollRect.MovementType.Clamped;

        GameObject viewportObject = CreateUIObject("Viewport", scrollViewTransform);
        RectTransform viewportTransform = viewportObject.GetComponent<RectTransform>();
        Stretch(viewportTransform);

        Image viewportImage = viewportObject.AddComponent<Image>();
        viewportImage.color = new Color(1f, 1f, 1f, 0.03f);
        viewportImage.raycastTarget = true;

        Mask mask = viewportObject.AddComponent<Mask>();
        mask.showMaskGraphic = false;

        GameObject contentObject = CreateUIObject("Content", viewportTransform);
        RectTransform contentTransform = contentObject.GetComponent<RectTransform>();
        contentTransform.anchorMin = new Vector2(0.5f, 1f);
        contentTransform.anchorMax = new Vector2(0.5f, 1f);
        contentTransform.pivot = new Vector2(0.5f, 1f);
        contentTransform.anchoredPosition = Vector2.zero;
        contentTransform.sizeDelta = new Vector2(1440f, 0f);

        VerticalLayoutGroup layoutGroup = contentObject.AddComponent<VerticalLayoutGroup>();
        layoutGroup.spacing = 40f;
        layoutGroup.padding = new RectOffset(20, 20, 20, 20);
        layoutGroup.childAlignment = TextAnchor.UpperCenter;
        layoutGroup.childControlWidth = false;
        layoutGroup.childControlHeight = false;
        layoutGroup.childForceExpandWidth = false;
        layoutGroup.childForceExpandHeight = false;

        ContentSizeFitter contentSizeFitter = contentObject.AddComponent<ContentSizeFitter>();
        contentSizeFitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        contentSizeFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        scrollRect.viewport = viewportTransform;
        scrollRect.content = contentTransform;

        return contentTransform;
    }

    private static void CreateFooter(RectTransform parent)
    {
        GameObject footerObject = CreateUIObject("Footer_Notes_Text", parent);
        RectTransform rectTransform = footerObject.GetComponent<RectTransform>();
        rectTransform.anchorMin = new Vector2(0.5f, 0f);
        rectTransform.anchorMax = new Vector2(0.5f, 0f);
        rectTransform.pivot = new Vector2(0.5f, 0f);
        rectTransform.anchoredPosition = new Vector2(0f, 12f);
        rectTransform.sizeDelta = new Vector2(1280f, 36f);

        AddText(
            footerObject,
            "Compare baked backgrounds, noisy edges, compression artifacts, alpha quality, and Korean text readability.",
            16f,
            ParseColor("#DDE6FF"),
            TextAlignmentOptions.Center,
            TextAnchor.MiddleCenter);
    }

    private static void CreateAuditItem(RectTransform parent, AuditSpriteInfo info)
    {
        GameObject itemObject = CreateUIObject($"AuditItem_{Path.GetFileNameWithoutExtension(info.FileName)}", parent);
        RectTransform itemTransform = itemObject.GetComponent<RectTransform>();
        itemTransform.sizeDelta = new Vector2(1400f, 520f);
        itemTransform.localScale = Vector3.one;

        LayoutElement layoutElement = itemObject.AddComponent<LayoutElement>();
        layoutElement.preferredWidth = 1400f;
        layoutElement.preferredHeight = 520f;
        layoutElement.minWidth = 1400f;
        layoutElement.minHeight = 520f;

        Image background = itemObject.AddComponent<Image>();
        background.color = new Color(1f, 1f, 1f, 0.035f);
        background.raycastTarget = false;

        CreateTitleText(itemTransform, info.FileName);
        CreateComparisonRow(itemTransform, info);
        CreateTextReadabilityRow(itemTransform, info);
        CreateInfoText(itemTransform, info);
    }

    private static void CreateTitleText(RectTransform parent, string fileName)
    {
        GameObject titleObject = CreateUIObject("Title_Text", parent);
        RectTransform titleTransform = titleObject.GetComponent<RectTransform>();
        titleTransform.anchorMin = new Vector2(0f, 1f);
        titleTransform.anchorMax = new Vector2(1f, 1f);
        titleTransform.pivot = new Vector2(0f, 1f);
        titleTransform.offsetMin = new Vector2(20f, -52f);
        titleTransform.offsetMax = new Vector2(-20f, -12f);

        AddText(titleObject, fileName, 22f, Color.white, TextAlignmentOptions.Left, TextAnchor.MiddleLeft);
    }

    private static void CreateComparisonRow(RectTransform parent, AuditSpriteInfo info)
    {
        GameObject rowObject = CreateUIObject("ComparisonRow", parent);
        RectTransform rowTransform = rowObject.GetComponent<RectTransform>();
        rowTransform.anchorMin = new Vector2(0.5f, 1f);
        rowTransform.anchorMax = new Vector2(0.5f, 1f);
        rowTransform.pivot = new Vector2(0.5f, 1f);
        rowTransform.anchoredPosition = new Vector2(0f, -60f);
        rowTransform.sizeDelta = new Vector2(1320f, 190f);

        HorizontalLayoutGroup layoutGroup = rowObject.AddComponent<HorizontalLayoutGroup>();
        layoutGroup.spacing = 30f;
        layoutGroup.padding = new RectOffset(0, 0, 0, 0);
        layoutGroup.childAlignment = TextAnchor.MiddleCenter;
        layoutGroup.childControlWidth = false;
        layoutGroup.childControlHeight = false;
        layoutGroup.childForceExpandWidth = false;
        layoutGroup.childForceExpandHeight = false;

        CreatePreviewPanel(rowTransform, "NavyPreview", "Navy", ParseColor("#1F2A44"), info.Sprite);
        CreatePreviewPanel(rowTransform, "GrayPreview", "Gray", ParseColor("#808080"), info.Sprite);
        CreatePreviewPanel(rowTransform, "CreamPreview", "Cream", ParseColor("#FFF7EB"), info.Sprite);
    }

    private static void CreatePreviewPanel(RectTransform parent, string objectName, string label, Color backgroundColor, Sprite sprite)
    {
        GameObject panelObject = CreateUIObject(objectName, parent);
        RectTransform panelTransform = panelObject.GetComponent<RectTransform>();
        panelTransform.sizeDelta = new Vector2(420f, 190f);

        LayoutElement layoutElement = panelObject.AddComponent<LayoutElement>();
        layoutElement.preferredWidth = 420f;
        layoutElement.preferredHeight = 190f;

        Image backgroundImage = panelObject.AddComponent<Image>();
        backgroundImage.color = backgroundColor;
        backgroundImage.raycastTarget = false;

        GameObject spriteObject = CreateUIObject("SpriteImage", panelTransform);
        Image spriteImage = spriteObject.AddComponent<Image>();
        spriteImage.sprite = sprite;
        spriteImage.preserveAspect = true;
        spriteImage.raycastTarget = false;

        RectTransform spriteTransform = spriteObject.GetComponent<RectTransform>();
        spriteTransform.anchorMin = new Vector2(0.5f, 0.58f);
        spriteTransform.anchorMax = new Vector2(0.5f, 0.58f);
        spriteTransform.pivot = new Vector2(0.5f, 0.5f);
        spriteTransform.anchoredPosition = Vector2.zero;
        FitToMaxSize(spriteTransform, sprite, 360f, 130f);

        GameObject labelObject = CreateUIObject("BackgroundLabel_Text", panelTransform);
        RectTransform labelTransform = labelObject.GetComponent<RectTransform>();
        labelTransform.anchorMin = new Vector2(0.5f, 0f);
        labelTransform.anchorMax = new Vector2(0.5f, 0f);
        labelTransform.pivot = new Vector2(0.5f, 0f);
        labelTransform.anchoredPosition = new Vector2(0f, 8f);
        labelTransform.sizeDelta = new Vector2(180f, 26f);

        Color labelColor = backgroundColor.grayscale > 0.55f ? ParseColor("#1F2A44") : Color.white;
        AddText(labelObject, label, 16f, labelColor, TextAlignmentOptions.Center, TextAnchor.MiddleCenter);
    }

    private static void CreateTextReadabilityRow(RectTransform parent, AuditSpriteInfo info)
    {
        GameObject rowObject = CreateUIObject("TextReadabilityRow", parent);
        RectTransform rowTransform = rowObject.GetComponent<RectTransform>();
        rowTransform.anchorMin = new Vector2(0.5f, 1f);
        rowTransform.anchorMax = new Vector2(0.5f, 1f);
        rowTransform.pivot = new Vector2(0.5f, 1f);
        rowTransform.anchoredPosition = new Vector2(0f, -275f);
        rowTransform.sizeDelta = new Vector2(1320f, 150f);

        HorizontalLayoutGroup layoutGroup = rowObject.AddComponent<HorizontalLayoutGroup>();
        layoutGroup.spacing = 30f;
        layoutGroup.childAlignment = TextAnchor.MiddleCenter;
        layoutGroup.childControlWidth = false;
        layoutGroup.childControlHeight = false;
        layoutGroup.childForceExpandWidth = false;
        layoutGroup.childForceExpandHeight = false;

        CreateTextOverlayPanel(rowTransform, "SmallTextOverlay", "코인 05", 18f, info.Sprite);
        CreateTextOverlayPanel(rowTransform, "MediumTextOverlay", "남은 시간 00:54", 24f, info.Sprite);
        CreateTextOverlayPanel(rowTransform, "LargeTextOverlay", "몰래 운반", 32f, info.Sprite);
    }

    private static void CreateTextOverlayPanel(RectTransform parent, string objectName, string overlayText, float fontSize, Sprite sprite)
    {
        GameObject panelObject = CreateUIObject(objectName, parent);
        RectTransform panelTransform = panelObject.GetComponent<RectTransform>();
        panelTransform.sizeDelta = new Vector2(420f, 150f);

        LayoutElement layoutElement = panelObject.AddComponent<LayoutElement>();
        layoutElement.preferredWidth = 420f;
        layoutElement.preferredHeight = 150f;

        Image backgroundImage = panelObject.AddComponent<Image>();
        backgroundImage.sprite = sprite;
        backgroundImage.preserveAspect = true;
        backgroundImage.raycastTarget = false;

        GameObject textObject = CreateUIObject("Overlay_Text", panelTransform);
        RectTransform textTransform = textObject.GetComponent<RectTransform>();
        Stretch(textTransform);
        textTransform.offsetMin = new Vector2(16f, 12f);
        textTransform.offsetMax = new Vector2(-16f, -12f);

        AddText(textObject, overlayText, fontSize, ParseColor("#1F2A44"), TextAlignmentOptions.Center, TextAnchor.MiddleCenter);
    }

    private static void CreateInfoText(RectTransform parent, AuditSpriteInfo info)
    {
        GameObject infoObject = CreateUIObject("Info_Text", parent);
        RectTransform infoTransform = infoObject.GetComponent<RectTransform>();
        infoTransform.anchorMin = new Vector2(0f, 0f);
        infoTransform.anchorMax = new Vector2(1f, 0f);
        infoTransform.pivot = new Vector2(0f, 0f);
        infoTransform.offsetMin = new Vector2(20f, 16f);
        infoTransform.offsetMax = new Vector2(-20f, 82f);

        AddText(infoObject, BuildInfoText(info), 13f, ParseColor("#DDE6FF"), TextAlignmentOptions.Left, TextAnchor.UpperLeft);
    }

    private static string BuildInfoText(AuditSpriteInfo info)
    {
        return
            $"path: {info.AssetPath}\n" +
            $"size: {info.Width} x {info.Height} | " +
            $"textureType: {info.TextureType} | " +
            $"spriteImportMode: {info.SpriteImportMode} | " +
            $"alphaIsTransparency: {info.AlphaIsTransparency} | " +
            $"compression: {info.Compression} | " +
            $"maxTextureSize: {info.MaxTextureSize} | " +
            $"mipmapEnabled: {info.MipmapEnabled}";
    }

    private static void AddText(GameObject target, string text, float fontSize, Color color, TextAlignmentOptions tmpAlignment, TextAnchor fallbackAlignment)
    {
        if (CanUseTextMeshPro())
        {
            TextMeshProUGUI tmpText = target.AddComponent<TextMeshProUGUI>();
            tmpText.text = text;
            tmpText.fontSize = fontSize;
            tmpText.color = color;
            tmpText.alignment = tmpAlignment;
            tmpText.textWrappingMode = TextWrappingModes.Normal;
            tmpText.raycastTarget = false;
            return;
        }

        Text fallbackText = target.AddComponent<Text>();
        fallbackText.text = text;
        fallbackText.font = GetFallbackFont();
        fallbackText.fontSize = Mathf.RoundToInt(fontSize);
        fallbackText.color = color;
        fallbackText.alignment = fallbackAlignment;
        fallbackText.raycastTarget = false;
    }

    private static bool CanUseTextMeshPro()
    {
        try
        {
            return TMP_Settings.defaultFontAsset != null;
        }
        catch
        {
            return false;
        }
    }

    private static Font GetFallbackFont()
    {
        Font legacyFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (legacyFont != null)
        {
            return legacyFont;
        }

        return Resources.GetBuiltinResource<Font>("Arial.ttf");
    }

    private static void WriteReport(IReadOnlyList<AuditSpriteInfo> auditSprites)
    {
        StringBuilder builder = new StringBuilder();
        builder.AppendLine("file_name,path,found,width,height,texture_type,sprite_import_mode,alpha_is_transparency,compression,max_size,mipmap_enabled,category_hint,suggested_action");

        foreach (AuditSpriteInfo info in auditSprites)
        {
            AppendCsvLine(
                builder,
                info.FileName,
                info.AssetPath,
                info.Found.ToString(),
                info.Width.ToString(CultureInfo.InvariantCulture),
                info.Height.ToString(CultureInfo.InvariantCulture),
                info.TextureType,
                info.SpriteImportMode,
                info.AlphaIsTransparency,
                info.Compression,
                info.MaxTextureSize.ToString(CultureInfo.InvariantCulture),
                info.MipmapEnabled,
                info.CategoryHint,
                info.SuggestedAction);
        }

        string fullPath = AssetPathToFullPath(ReportPath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath));
        File.WriteAllText(fullPath, builder.ToString(), Encoding.UTF8);
        AssetDatabase.ImportAsset(ReportPath);
    }

    private static string GetSuggestedAction(string fileName)
    {
        string lowerName = fileName.ToLowerInvariant();
        if (lowerName.Contains("stamina") || lowerName.Contains("010"))
        {
            return "regenerate_recommended";
        }

        if (lowerName.Contains("mission") || lowerName.Contains("008"))
        {
            return "test_with_text_then_fix_or_regenerate";
        }

        if (lowerName.Contains("ready") || lowerName.Contains("025"))
        {
            return "fix_noise_then_retest";
        }

        if (lowerName.Contains("046") || lowerName.Contains("047"))
        {
            return "fix_noise_if_used_as_text_background";
        }

        if (lowerName.Contains("004") || lowerName.Contains("042"))
        {
            return "keep_if_visual_ok";
        }

        return "manual_review";
    }

    private static string GetCategoryHint(string assetPath)
    {
        if (string.IsNullOrEmpty(assetPath))
        {
            return string.Empty;
        }

        string relativePath = NormalizeAssetPath(assetPath).StartsWith(SpriteRoot + "/", StringComparison.OrdinalIgnoreCase)
            ? NormalizeAssetPath(assetPath).Substring(SpriteRoot.Length + 1)
            : NormalizeAssetPath(assetPath);
        int slashIndex = relativePath.IndexOf('/');
        return slashIndex > 0 ? relativePath.Substring(0, slashIndex) : string.Empty;
    }

    private static GameObject CreateUIObject(string name, Transform parent)
    {
        GameObject gameObject = new GameObject(name, typeof(RectTransform));
        gameObject.transform.SetParent(parent, false);
        return gameObject;
    }

    private static void Stretch(RectTransform rectTransform)
    {
        rectTransform.anchorMin = Vector2.zero;
        rectTransform.anchorMax = Vector2.one;
        rectTransform.pivot = new Vector2(0.5f, 0.5f);
        rectTransform.offsetMin = Vector2.zero;
        rectTransform.offsetMax = Vector2.zero;
    }

    private static void FitToMaxSize(RectTransform rectTransform, Sprite sprite, float maxWidth, float maxHeight)
    {
        float width = sprite.rect.width;
        float height = sprite.rect.height;
        if (width <= 0f || height <= 0f)
        {
            rectTransform.sizeDelta = Vector2.zero;
            return;
        }

        float scale = Mathf.Min(1f, Mathf.Min(maxWidth / width, maxHeight / height));
        rectTransform.sizeDelta = new Vector2(width * scale, height * scale);
    }

    private static bool SceneFileExists()
    {
        return AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) != null || File.Exists(AssetPathToFullPath(ScenePath));
    }

    private static void EnsureFolder(string assetFolder)
    {
        assetFolder = NormalizeAssetPath(assetFolder);
        if (AssetDatabase.IsValidFolder(assetFolder))
        {
            return;
        }

        string parent = NormalizeAssetPath(Path.GetDirectoryName(assetFolder));
        string folderName = Path.GetFileName(assetFolder);
        if (!AssetDatabase.IsValidFolder(parent))
        {
            EnsureFolder(parent);
        }

        AssetDatabase.CreateFolder(parent, folderName);
    }

    private static string NormalizeAssetPath(string path)
    {
        return string.IsNullOrWhiteSpace(path) ? string.Empty : path.Replace('\\', '/').TrimEnd('/');
    }

    private static string AssetPathToFullPath(string assetPath)
    {
        string projectRoot = Path.GetDirectoryName(Application.dataPath);
        return Path.Combine(projectRoot, NormalizeAssetPath(assetPath));
    }

    private static Color ParseColor(string htmlColor)
    {
        return ColorUtility.TryParseHtmlString(htmlColor, out Color color) ? color : Color.white;
    }

    private static void AppendCsvLine(StringBuilder builder, params string[] values)
    {
        for (int i = 0; i < values.Length; i++)
        {
            if (i > 0)
            {
                builder.Append(',');
            }

            builder.Append(EscapeCsv(values[i]));
        }

        builder.AppendLine();
    }

    private static string EscapeCsv(string value)
    {
        if (value == null)
        {
            return string.Empty;
        }

        bool mustQuote = value.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0;
        if (!mustQuote)
        {
            return value;
        }

        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }

    private sealed class AuditSpriteInfo
    {
        public readonly string FileName;
        public readonly string AssetPath;
        public readonly bool Found;
        public readonly Sprite Sprite;
        public readonly int Width;
        public readonly int Height;
        public readonly string TextureType;
        public readonly string SpriteImportMode;
        public readonly string AlphaIsTransparency;
        public readonly string Compression;
        public readonly int MaxTextureSize;
        public readonly string MipmapEnabled;
        public readonly string CategoryHint;
        public readonly string SuggestedAction;

        private AuditSpriteInfo(
            string fileName,
            string assetPath,
            bool found,
            Sprite sprite,
            int width,
            int height,
            string textureType,
            string spriteImportMode,
            string alphaIsTransparency,
            string compression,
            int maxTextureSize,
            string mipmapEnabled,
            string categoryHint,
            string suggestedAction)
        {
            FileName = fileName;
            AssetPath = assetPath;
            Found = found;
            Sprite = sprite;
            Width = width;
            Height = height;
            TextureType = textureType;
            SpriteImportMode = spriteImportMode;
            AlphaIsTransparency = alphaIsTransparency;
            Compression = compression;
            MaxTextureSize = maxTextureSize;
            MipmapEnabled = mipmapEnabled;
            CategoryHint = categoryHint;
            SuggestedAction = suggestedAction;
        }

        public static AuditSpriteInfo Missing(string fileName, string assetPath = "")
        {
            return new AuditSpriteInfo(
                fileName,
                assetPath,
                false,
                null,
                0,
                0,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                0,
                string.Empty,
                GetCategoryHint(assetPath),
                GetSuggestedAction(fileName));
        }

        public static AuditSpriteInfo FoundSprite(string fileName, string assetPath, Sprite sprite, Texture2D texture, TextureImporter importer)
        {
            return new AuditSpriteInfo(
                fileName,
                assetPath,
                true,
                sprite,
                texture != null ? texture.width : Mathf.RoundToInt(sprite.rect.width),
                texture != null ? texture.height : Mathf.RoundToInt(sprite.rect.height),
                importer != null ? importer.textureType.ToString() : string.Empty,
                importer != null ? importer.spriteImportMode.ToString() : string.Empty,
                importer != null ? importer.alphaIsTransparency.ToString() : string.Empty,
                importer != null ? importer.textureCompression.ToString() : string.Empty,
                importer != null ? importer.maxTextureSize : 0,
                importer != null ? importer.mipmapEnabled.ToString() : string.Empty,
                GetCategoryHint(assetPath),
                GetSuggestedAction(fileName));
        }
    }
}
