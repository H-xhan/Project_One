using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public static class ProjectOneUITextSetupCreator
{
    private const string MenuPath = "Project ONE/UI/Create Text Style Setup";
    private const string UiRoot = "Assets/ProjectONE/UI";
    private const string FontsFolder = UiRoot + "/Fonts";
    private const string PrefabFolder = UiRoot + "/Prefabs/Text";
    private const string ScriptsFolder = UiRoot + "/Scripts/Text";
    private const string ScriptableObjectFolder = UiRoot + "/ScriptableObjects";
    private const string ScenesFolder = UiRoot + "/Scenes";
    private const string DocsFolder = UiRoot + "/Docs";
    private const string StyleSetPath = ScriptableObjectFolder + "/ProjectOneTextStyleSet.asset";
    private const string ScenePath = ScenesFolder + "/UI_TextStyle_Test.unity";
    private const string ReadmePath = DocsFolder + "/README_TEXT_SETUP_KR.md";
    private const string FontCandidatesPath = DocsFolder + "/text_font_candidates.csv";
    private const float ReferenceWidth = 1536f;
    private const float ReferenceHeight = 864f;

    private static readonly TextPrefabConfig[] TextPrefabs =
    {
        new TextPrefabConfig(ProjectOneTextStyleType.LogoProject, "PF_Text_LogoProject.prefab", new Vector2(300f, 80f)),
        new TextPrefabConfig(ProjectOneTextStyleType.LogoOne, "PF_Text_LogoOne.prefab", new Vector2(300f, 80f)),
        new TextPrefabConfig(ProjectOneTextStyleType.ResultTitle, "PF_Text_ResultTitle.prefab", new Vector2(420f, 120f)),
        new TextPrefabConfig(ProjectOneTextStyleType.ReadyLarge, "PF_Text_ReadyLarge.prefab", new Vector2(420f, 120f)),
        new TextPrefabConfig(ProjectOneTextStyleType.ScreenTitle, "PF_Text_ScreenTitle.prefab", new Vector2(300f, 80f)),
        new TextPrefabConfig(ProjectOneTextStyleType.MenuButton, "PF_Text_MenuButton.prefab", new Vector2(300f, 80f)),
        new TextPrefabConfig(ProjectOneTextStyleType.ButtonLabel, "PF_Text_ButtonLabel.prefab", new Vector2(300f, 80f)),
        new TextPrefabConfig(ProjectOneTextStyleType.HUDNumber, "PF_Text_HUDNumber.prefab", new Vector2(220f, 80f)),
        new TextPrefabConfig(ProjectOneTextStyleType.HUDLabel, "PF_Text_HUDLabel.prefab", new Vector2(300f, 80f)),
        new TextPrefabConfig(ProjectOneTextStyleType.CardTitle, "PF_Text_CardTitle.prefab", new Vector2(300f, 80f)),
        new TextPrefabConfig(ProjectOneTextStyleType.CardBody, "PF_Text_CardBody.prefab", new Vector2(500f, 160f)),
        new TextPrefabConfig(ProjectOneTextStyleType.SmallCaption, "PF_Text_SmallCaption.prefab", new Vector2(300f, 80f)),
        new TextPrefabConfig(ProjectOneTextStyleType.WhiteTabLabel, "PF_Text_WhiteTabLabel.prefab", new Vector2(300f, 80f)),
        new TextPrefabConfig(ProjectOneTextStyleType.BlueAccentText, "PF_Text_BlueAccentText.prefab", new Vector2(300f, 80f)),
        new TextPrefabConfig(ProjectOneTextStyleType.RankingName, "PF_Text_RankingName.prefab", new Vector2(300f, 80f)),
        new TextPrefabConfig(ProjectOneTextStyleType.RankingScore, "PF_Text_RankingScore.prefab", new Vector2(300f, 80f)),
        new TextPrefabConfig(ProjectOneTextStyleType.RoomCodeLabel, "PF_Text_RoomCodeLabel.prefab", new Vector2(300f, 80f)),
        new TextPrefabConfig(ProjectOneTextStyleType.RoomCodeValue, "PF_Text_RoomCodeValue.prefab", new Vector2(300f, 80f))
    };

    private static readonly SampleTextConfig[] SampleTexts =
    {
        new SampleTextConfig("Project ONE", ProjectOneTextStyleType.ScreenTitle, "ScreenTitle"),
        new SampleTextConfig("PROJECT", ProjectOneTextStyleType.LogoProject, "LogoProject"),
        new SampleTextConfig("ONE", ProjectOneTextStyleType.LogoOne, "LogoOne", true),
        new SampleTextConfig("승리!", ProjectOneTextStyleType.ResultTitle, "ResultTitle", true),
        new SampleTextConfig("Ready", ProjectOneTextStyleType.ReadyLarge, "ReadyLarge", true),
        new SampleTextConfig("캐릭터 선택", ProjectOneTextStyleType.ScreenTitle, "ScreenTitle"),
        new SampleTextConfig("빠른 시작", ProjectOneTextStyleType.MenuButton, "MenuButton"),
        new SampleTextConfig("커스텀 게임", ProjectOneTextStyleType.MenuButton, "MenuButton"),
        new SampleTextConfig("튜토리얼", ProjectOneTextStyleType.MenuButton, "MenuButton"),
        new SampleTextConfig("설정", ProjectOneTextStyleType.ButtonLabel, "ButtonLabel"),
        new SampleTextConfig("게임 종료", ProjectOneTextStyleType.ButtonLabel, "ButtonLabel"),
        new SampleTextConfig("코인", ProjectOneTextStyleType.HUDLabel, "HUDLabel"),
        new SampleTextConfig("05", ProjectOneTextStyleType.HUDNumber, "HUDNumber"),
        new SampleTextConfig("남은 시간", ProjectOneTextStyleType.HUDLabel, "HUDLabel"),
        new SampleTextConfig("00:54", ProjectOneTextStyleType.HUDNumber, "HUDNumber"),
        new SampleTextConfig("0 / 1 Ready", ProjectOneTextStyleType.WhiteTabLabel, "WhiteTabLabel", true),
        new SampleTextConfig("몰래 운반", ProjectOneTextStyleType.CardTitle, "CardTitle"),
        new SampleTextConfig("종료 순간 노트북 구역 안에서 itemId 2 아이템을 들고 있으면 성공", ProjectOneTextStyleType.CardBody, "CardBody"),
        new SampleTextConfig("+12 코인", ProjectOneTextStyleType.BlueAccentText, "BlueAccentText"),
        new SampleTextConfig("ROOM CODE :", ProjectOneTextStyleType.RoomCodeLabel, "RoomCodeLabel"),
        new SampleTextConfig("RPMKJK", ProjectOneTextStyleType.RoomCodeValue, "RoomCodeValue"),
        new SampleTextConfig("플레이어 1", ProjectOneTextStyleType.RankingName, "RankingName"),
        new SampleTextConfig("87", ProjectOneTextStyleType.RankingScore, "RankingScore")
    };

    private static readonly OverlaySampleConfig[] OverlaySamples =
    {
        new OverlaySampleConfig("MainMenu_YellowButton", "Assets/ProjectONE/UI/Sprites/Buttons/043_menu_button_yellow_blank.png", "빠른 시작", ProjectOneTextStyleType.MenuButton, new Vector2(270f, 104f)),
        new OverlaySampleConfig("Lobby_ReadyButton", "Assets/ProjectONE/UI/Sprites/Buttons/104_ready_button_large_yellow_blank.png", "Ready", ProjectOneTextStyleType.ReadyLarge, new Vector2(270f, 104f), true),
        new OverlaySampleConfig("HUD_MissionCard", "Assets/ProjectONE/UI/Sprites/HUD/008_mission_card_blank_stamp.png", "몰래 운반", ProjectOneTextStyleType.CardTitle, new Vector2(270f, 150f)),
        new OverlaySampleConfig("Result_Card", "Assets/ProjectONE/UI/Sprites/Result_Ranking/081_victory_card_blank.png", "승리!", ProjectOneTextStyleType.ResultTitle, new Vector2(270f, 150f), true),
        new OverlaySampleConfig("RoomCode_Panel", "Assets/ProjectONE/UI/Sprites/Lobby_CharacterSelect/106_room_code_top_panel.png", "RPMKJK", ProjectOneTextStyleType.RoomCodeValue, new Vector2(270f, 104f))
    };

    [MenuItem(MenuPath)]
    public static void CreateTextStyleSetup()
    {
        CreateTextStyleSetupInternal(false);
    }

    public static void CreateTextStyleSetupBatch()
    {
        CreateTextStyleSetupInternal(true);
    }

    private static void CreateTextStyleSetupInternal(bool forceOverwrite)
    {
        List<string> existingAssets = CollectExistingGeneratedAssets();
        if (!forceOverwrite && existingAssets.Count > 0)
        {
            string message = "The following generated assets already exist:\n\n" +
                string.Join("\n", existingAssets.Take(10).ToArray()) +
                (existingAssets.Count > 10 ? "\n..." : string.Empty) +
                "\n\nOverwrite generated text style setup assets?";

            if (!EditorUtility.DisplayDialog("Overwrite Project ONE Text Style Setup", message, "Overwrite", "Cancel"))
            {
                Debug.Log("Project ONE Text Style Setup creation cancelled.");
                return;
            }
        }

        EnsureFolders();
        CheckTmpEssentialResources();

        ProjectOneTextStyleSet existingStyleSet = AssetDatabase.LoadAssetAtPath<ProjectOneTextStyleSet>(StyleSetPath);
        TMP_FontAsset defaultFont = ResolveDefaultFont(existingStyleSet);
        ProjectOneTextStyleSet styleSet = CreateOrUpdateStyleSet(defaultFont);

        List<string> created = new List<string>();
        created.Add(StyleSetPath);
        created.AddRange(CreateTextPrefabs(styleSet));
        CreateTestScene(styleSet);
        created.Add(ScenePath);
        WriteFontCandidateReport();
        created.Add(FontCandidatesPath);
        WriteReadme();
        created.Add(ReadmePath);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log("Project ONE Text Style Setup created:\n" + string.Join("\n", created.ToArray()));
    }

    private static List<string> CollectExistingGeneratedAssets()
    {
        List<string> paths = new List<string>();

        if (AssetDatabase.LoadAssetAtPath<ProjectOneTextStyleSet>(StyleSetPath) != null)
        {
            paths.Add(StyleSetPath);
        }

        foreach (TextPrefabConfig config in TextPrefabs)
        {
            string prefabPath = PrefabFolder + "/" + config.FileName;
            if (AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath) != null)
            {
                paths.Add(prefabPath);
            }
        }

        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) != null)
        {
            paths.Add(ScenePath);
        }

        if (File.Exists(ReadmePath))
        {
            paths.Add(ReadmePath);
        }

        return paths;
    }

    private static void EnsureFolders()
    {
        EnsureFolder(UiRoot);
        EnsureFolder(FontsFolder);
        EnsureFolder("Assets/ProjectONE/UI/Prefabs");
        EnsureFolder(PrefabFolder);
        EnsureFolder("Assets/ProjectONE/UI/Scripts");
        EnsureFolder(ScriptsFolder);
        EnsureFolder(ScriptableObjectFolder);
        EnsureFolder(ScenesFolder);
        EnsureFolder(DocsFolder);
    }

    private static void EnsureFolder(string assetFolderPath)
    {
        if (AssetDatabase.IsValidFolder(assetFolderPath))
        {
            return;
        }

        string parent = Path.GetDirectoryName(assetFolderPath).Replace("\\", "/");
        string folderName = Path.GetFileName(assetFolderPath);
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, folderName);
    }

    private static void CheckTmpEssentialResources()
    {
        bool hasTmpSettings = AssetDatabase.LoadAssetAtPath<TMP_Settings>("Assets/TextMesh Pro/Resources/TMP Settings.asset") != null;
        bool hasTmpFolder = AssetDatabase.IsValidFolder("Assets/TextMesh Pro");

        if (!hasTmpSettings || !hasTmpFolder)
        {
            Debug.LogWarning("Project ONE Text Style Setup: TMP Essential Resources were not found under Assets/TextMesh Pro. Import TMP Essentials before final UI work.");
        }
    }

    private static TMP_FontAsset ResolveDefaultFont(ProjectOneTextStyleSet existingStyleSet)
    {
        if (existingStyleSet != null && existingStyleSet.defaultFont != null && !IsBasicTmpFont(existingStyleSet.defaultFont))
        {
            Debug.Log("Project ONE Text Style Setup: Preserving existing TMP Font Asset: " + AssetDatabase.GetAssetPath(existingStyleSet.defaultFont));
            return existingStyleSet.defaultFont;
        }

        return FindProjectTmpFontAsset();
    }

    private static TMP_FontAsset FindProjectTmpFontAsset()
    {
        string[] guids = AssetDatabase.FindAssets("t:TMP_FontAsset");
        List<string> paths = guids.Select(AssetDatabase.GUIDToAssetPath)
            .Where(path => path.StartsWith("Assets/", StringComparison.Ordinal))
            .OrderBy(GetFontAssetPriority)
            .ThenBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (string path in paths)
        {
            TMP_FontAsset font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
            if (font != null)
            {
                Debug.Log("Project ONE Text Style Setup: Using TMP Font Asset: " + path);
                return font;
            }
        }

        Debug.LogWarning("Project ONE Text Style Setup: No TMP_FontAsset was found in the project. TMP default font will be used temporarily.");
        return TMP_Settings.defaultFontAsset;
    }

    private static int GetFontAssetPriority(string path)
    {
        if (path.IndexOf("NotoSansKR", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return 0;
        }

        if (path.IndexOf("Assets/TextMesh Pro", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return 20;
        }

        return 10;
    }

    private static bool IsBasicTmpFont(TMP_FontAsset font)
    {
        if (font == null)
        {
            return true;
        }

        string assetPath = AssetDatabase.GetAssetPath(font);
        string searchableName = (assetPath + " " + font.name).ToLowerInvariant();
        return font == TMP_Settings.defaultFontAsset ||
            searchableName.Contains("liberationsans") ||
            searchableName.Contains("textmesh pro/resources/fonts");
    }

    private static ProjectOneTextStyleSet CreateOrUpdateStyleSet(TMP_FontAsset defaultFont)
    {
        ProjectOneTextStyleSet styleSet = AssetDatabase.LoadAssetAtPath<ProjectOneTextStyleSet>(StyleSetPath);
        if (styleSet == null)
        {
            styleSet = ScriptableObject.CreateInstance<ProjectOneTextStyleSet>();
            AssetDatabase.CreateAsset(styleSet, StyleSetPath);
        }

        styleSet.defaultFont = defaultFont;
        styleSet.navy = ParseColor("#1F2A44");
        styleSet.cream = ParseColor("#FFF7EB");
        styleSet.yellow = ParseColor("#FFD56A");
        styleSet.blue = ParseColor("#B8D3F6");
        styleSet.mint = ParseColor("#CDEBD7");
        styleSet.white = ParseColor("#FFFFFF");
        styleSet.subGray = ParseColor("#6F7480");
        styleSet.accentBlue = ParseColor("#2F80ED");
        styleSet.softDark = ParseColor("#3D4658");

        styleSet.logoProjectSize = 68f;
        styleSet.logoOneSize = 104f;
        styleSet.resultTitleSize = 88f;
        styleSet.readyLargeSize = 88f;
        styleSet.screenTitleSize = 48f;
        styleSet.menuButtonSize = 36f;
        styleSet.buttonLabelSize = 36f;
        styleSet.hudNumberSize = 54f;
        styleSet.hudLabelSize = 24f;
        styleSet.cardTitleSize = 38f;
        styleSet.cardBodySize = 25f;
        styleSet.smallCaptionSize = 19f;
        styleSet.tagLabelSize = 26f;
        styleSet.rankingNameSize = 22f;
        styleSet.rankingScoreSize = 28f;

        EditorUtility.SetDirty(styleSet);
        return styleSet;
    }

    private static List<string> CreateTextPrefabs(ProjectOneTextStyleSet styleSet)
    {
        List<string> created = new List<string>();

        foreach (TextPrefabConfig config in TextPrefabs)
        {
            string prefabPath = PrefabFolder + "/" + config.FileName;
            GameObject root = new GameObject("PrefabRoot", typeof(RectTransform));
            RectTransform rootTransform = root.GetComponent<RectTransform>();
            rootTransform.sizeDelta = config.Size;

            GameObject textObject = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI), typeof(ProjectOneTextStyleApplier));
            textObject.transform.SetParent(root.transform, false);

            RectTransform textTransform = textObject.GetComponent<RectTransform>();
            Stretch(textTransform);
            textTransform.sizeDelta = config.Size;

            TextMeshProUGUI text = textObject.GetComponent<TextMeshProUGUI>();
            text.text = config.StyleType.ToString();
            text.richText = true;
            text.raycastTarget = false;
            text.extraPadding = true;

            ProjectOneTextStyleApplier applier = textObject.GetComponent<ProjectOneTextStyleApplier>();
            applier.target = text;
            applier.styleSet = styleSet;
            applier.styleType = config.StyleType;
            ApplyRecommendedEffectOptions(applier, config.StyleType);
            applier.ApplyStyle();

            PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            UnityEngine.Object.DestroyImmediate(root);
            created.Add(prefabPath);
        }

        return created;
    }

    private static void ApplyRecommendedEffectOptions(ProjectOneTextStyleApplier applier, ProjectOneTextStyleType styleType)
    {
        applier.useSoftShadow = false;
        applier.useWhiteOutline = false;
        applier.useNavyOutline = false;
        applier.shadowOffset = new Vector2(2f, -2f);
        applier.shadowColor = new Color(0f, 0f, 0f, 0.22f);

        switch (styleType)
        {
            case ProjectOneTextStyleType.LogoOne:
                applier.useSoftShadow = true;
                applier.shadowOffset = new Vector2(3f, -3f);
                applier.shadowColor = new Color(0f, 0f, 0f, 0.22f);
                break;
            case ProjectOneTextStyleType.ResultTitle:
                applier.useSoftShadow = true;
                applier.shadowOffset = new Vector2(2f, -2f);
                applier.shadowColor = new Color(0f, 0f, 0f, 0.16f);
                break;
            case ProjectOneTextStyleType.ReadyLarge:
                applier.useSoftShadow = true;
                applier.useWhiteOutline = true;
                applier.shadowOffset = new Vector2(3f, -3f);
                applier.shadowColor = new Color(0f, 0f, 0f, 0.22f);
                break;
            case ProjectOneTextStyleType.WhiteTabLabel:
                applier.useSoftShadow = true;
                applier.shadowOffset = new Vector2(1.5f, -1.5f);
                applier.shadowColor = new Color(0f, 0f, 0f, 0.18f);
                break;
        }
    }

    private static void CreateTestScene(ProjectOneTextStyleSet styleSet)
    {
        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        scene.name = "UI_TextStyle_Test";

        CreateCamera();
        CreateEventSystem();

        GameObject canvasObject = CreateCanvas();
        RectTransform canvasTransform = canvasObject.GetComponent<RectTransform>();

        CreateBackground(canvasTransform, styleSet);
        CreateHeader(canvasTransform, styleSet);
        RectTransform gridRoot = CreateTextStyleGrid(canvasTransform);
        CreateStyleSamples(gridRoot, styleSet);
        CreateOverlaySamples(gridRoot, styleSet);
        CreateNotes(canvasTransform, styleSet);

        if (!EditorSceneManager.SaveScene(scene, ScenePath))
        {
            Debug.LogError("Project ONE Text Style Setup: Failed to save test scene at " + ScenePath);
        }
    }

    private static void CreateCamera()
    {
        GameObject cameraObject = new GameObject("Camera");
        Camera camera = cameraObject.AddComponent<Camera>();
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = ParseColor("#FFF7EB");
        camera.orthographic = true;
        camera.transform.position = new Vector3(0f, 0f, -10f);
        cameraObject.tag = "MainCamera";
    }

    private static void CreateEventSystem()
    {
        GameObject eventSystemObject = new GameObject("EventSystem");
        eventSystemObject.AddComponent<EventSystem>();
        eventSystemObject.AddComponent<StandaloneInputModule>();
    }

    private static GameObject CreateCanvas()
    {
        GameObject canvasObject = new GameObject("Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        Canvas canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;

        CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(ReferenceWidth, ReferenceHeight);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;

        return canvasObject;
    }

    private static void CreateBackground(RectTransform parent, ProjectOneTextStyleSet styleSet)
    {
        GameObject backgroundObject = CreateUIObject("Background_Cream", parent);
        Image image = backgroundObject.AddComponent<Image>();
        image.color = styleSet != null ? styleSet.cream : ParseColor("#FFF7EB");
        image.raycastTarget = false;

        RectTransform rectTransform = backgroundObject.GetComponent<RectTransform>();
        Stretch(rectTransform);
        rectTransform.SetAsFirstSibling();
    }

    private static void CreateHeader(RectTransform parent, ProjectOneTextStyleSet styleSet)
    {
        TextMeshProUGUI header = CreateStyledText(parent, "Header_Text", "Project ONE Text Style Test", ProjectOneTextStyleType.ScreenTitle, styleSet, new Vector2(900f, 72f), false);
        RectTransform rectTransform = header.rectTransform;
        rectTransform.anchorMin = new Vector2(0.5f, 1f);
        rectTransform.anchorMax = new Vector2(0.5f, 1f);
        rectTransform.pivot = new Vector2(0.5f, 1f);
        rectTransform.anchoredPosition = new Vector2(0f, -24f);
    }

    private static RectTransform CreateTextStyleGrid(RectTransform parent)
    {
        GameObject gridObject = CreateUIObject("TextStyleGrid", parent);
        RectTransform rectTransform = gridObject.GetComponent<RectTransform>();
        rectTransform.anchorMin = new Vector2(0f, 0f);
        rectTransform.anchorMax = new Vector2(1f, 1f);
        rectTransform.pivot = new Vector2(0.5f, 0.5f);
        rectTransform.offsetMin = new Vector2(42f, 72f);
        rectTransform.offsetMax = new Vector2(-42f, -96f);
        return rectTransform;
    }

    private static void CreateStyleSamples(RectTransform parent, ProjectOneTextStyleSet styleSet)
    {
        GameObject gridObject = CreateUIObject("StyleSamples", parent);
        RectTransform gridTransform = gridObject.GetComponent<RectTransform>();
        gridTransform.anchorMin = new Vector2(0f, 0.31f);
        gridTransform.anchorMax = new Vector2(1f, 1f);
        gridTransform.offsetMin = Vector2.zero;
        gridTransform.offsetMax = Vector2.zero;

        GridLayoutGroup grid = gridObject.AddComponent<GridLayoutGroup>();
        grid.cellSize = new Vector2(286f, 92f);
        grid.spacing = new Vector2(8f, 6f);
        grid.padding = new RectOffset(6, 6, 6, 6);
        grid.childAlignment = TextAnchor.UpperCenter;
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = 5;

        foreach (SampleTextConfig sample in SampleTexts)
        {
            CreateSampleCell(gridTransform, sample, styleSet);
        }
    }

    private static void CreateSampleCell(RectTransform parent, SampleTextConfig sample, ProjectOneTextStyleSet styleSet)
    {
        GameObject cellObject = CreateUIObject("Sample_" + sample.Caption, parent);
        RectTransform cellTransform = cellObject.GetComponent<RectTransform>();
        cellTransform.sizeDelta = new Vector2(286f, 92f);

        Image backing = cellObject.AddComponent<Image>();
        backing.color = new Color(1f, 1f, 1f, 0.32f);
        backing.raycastTarget = false;

        TextMeshProUGUI text = CreateStyledText(cellTransform, "Text", sample.Text, sample.StyleType, styleSet, new Vector2(272f, 62f), sample.UseShadow);
        RectTransform textTransform = text.rectTransform;
        textTransform.anchorMin = new Vector2(0.5f, 1f);
        textTransform.anchorMax = new Vector2(0.5f, 1f);
        textTransform.pivot = new Vector2(0.5f, 1f);
        textTransform.anchoredPosition = new Vector2(0f, -4f);

        TextMeshProUGUI caption = CreateStyledText(cellTransform, "Caption", sample.Caption, ProjectOneTextStyleType.SmallCaption, styleSet, new Vector2(272f, 24f), false);
        RectTransform captionTransform = caption.rectTransform;
        captionTransform.anchorMin = new Vector2(0.5f, 0f);
        captionTransform.anchorMax = new Vector2(0.5f, 0f);
        captionTransform.pivot = new Vector2(0.5f, 0f);
        captionTransform.anchoredPosition = new Vector2(0f, 4f);
    }

    private static void CreateOverlaySamples(RectTransform parent, ProjectOneTextStyleSet styleSet)
    {
        GameObject sectionObject = CreateUIObject("ImageOverlaySamples", parent);
        RectTransform sectionTransform = sectionObject.GetComponent<RectTransform>();
        sectionTransform.anchorMin = new Vector2(0f, 0f);
        sectionTransform.anchorMax = new Vector2(1f, 0.28f);
        sectionTransform.offsetMin = Vector2.zero;
        sectionTransform.offsetMax = Vector2.zero;

        TextMeshProUGUI sectionTitle = CreateStyledText(sectionTransform, "SectionTitle", "PNG UI 위 TMP 텍스트 비교", ProjectOneTextStyleType.HUDLabel, styleSet, new Vector2(500f, 28f), false);
        RectTransform titleTransform = sectionTitle.rectTransform;
        titleTransform.anchorMin = new Vector2(0.5f, 1f);
        titleTransform.anchorMax = new Vector2(0.5f, 1f);
        titleTransform.pivot = new Vector2(0.5f, 1f);
        titleTransform.anchoredPosition = new Vector2(0f, -4f);

        GameObject rowObject = CreateUIObject("OverlayRow", sectionTransform);
        RectTransform rowTransform = rowObject.GetComponent<RectTransform>();
        rowTransform.anchorMin = new Vector2(0f, 0f);
        rowTransform.anchorMax = new Vector2(1f, 1f);
        rowTransform.offsetMin = new Vector2(0f, 0f);
        rowTransform.offsetMax = new Vector2(0f, -36f);

        HorizontalLayoutGroup layout = rowObject.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = 14f;
        layout.padding = new RectOffset(6, 6, 8, 0);
        layout.childAlignment = TextAnchor.MiddleCenter;
        layout.childControlWidth = false;
        layout.childControlHeight = false;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;

        foreach (OverlaySampleConfig sample in OverlaySamples)
        {
            CreateOverlayCell(rowTransform, sample, styleSet);
        }
    }

    private static void CreateOverlayCell(RectTransform parent, OverlaySampleConfig sample, ProjectOneTextStyleSet styleSet)
    {
        GameObject cellObject = CreateUIObject(sample.Name, parent);
        RectTransform cellTransform = cellObject.GetComponent<RectTransform>();
        cellTransform.sizeDelta = sample.Size;

        LayoutElement layoutElement = cellObject.AddComponent<LayoutElement>();
        layoutElement.preferredWidth = sample.Size.x;
        layoutElement.preferredHeight = sample.Size.y;

        Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(sample.SpritePath);
        if (sprite != null)
        {
            GameObject imageObject = CreateUIObject("Image", cellTransform);
            Image image = imageObject.AddComponent<Image>();
            image.sprite = sprite;
            image.preserveAspect = true;
            image.raycastTarget = false;
            Stretch(imageObject.GetComponent<RectTransform>());
        }
        else
        {
            Debug.LogWarning("Project ONE Text Style Setup: Overlay sample sprite not found or not imported as Sprite: " + sample.SpritePath);
        }

        if (sample.Name == "HUD_MissionCard")
        {
            CreateMissionOverlayText(cellTransform, styleSet);
            return;
        }

        if (sample.Name == "RoomCode_Panel")
        {
            CreateRoomCodeOverlayText(cellTransform, styleSet);
            return;
        }

        TextMeshProUGUI text = CreateStyledText(cellTransform, "OverlayText", sample.Text, sample.StyleType, styleSet, sample.Size * 0.82f, sample.UseShadow);
        text.alignment = sample.StyleType == ProjectOneTextStyleType.CardBody ? TextAlignmentOptions.Center : text.alignment;
        RectTransform textTransform = text.rectTransform;
        textTransform.anchorMin = new Vector2(0.5f, 0.5f);
        textTransform.anchorMax = new Vector2(0.5f, 0.5f);
        textTransform.pivot = new Vector2(0.5f, 0.5f);
        textTransform.anchoredPosition = Vector2.zero;
    }

    private static void CreateMissionOverlayText(RectTransform parent, ProjectOneTextStyleSet styleSet)
    {
        TextMeshProUGUI title = CreateStyledText(parent, "MissionTitleText", "몰래 운반", ProjectOneTextStyleType.CardTitle, styleSet, new Vector2(220f, 40f), false);
        RectTransform titleTransform = title.rectTransform;
        titleTransform.anchorMin = new Vector2(0.5f, 0.5f);
        titleTransform.anchorMax = new Vector2(0.5f, 0.5f);
        titleTransform.pivot = new Vector2(0.5f, 0.5f);
        titleTransform.anchoredPosition = new Vector2(0f, 20f);
        title.alignment = TextAlignmentOptions.Center;

        TextMeshProUGUI body = CreateStyledText(
            parent,
            "MissionBodyText",
            "종료 순간 노트북 구역 안에서 itemId 2 아이템을 들고 있으면 성공",
            ProjectOneTextStyleType.CardBody,
            styleSet,
            new Vector2(220f, 72f),
            false);
        RectTransform bodyTransform = body.rectTransform;
        bodyTransform.anchorMin = new Vector2(0.5f, 0.5f);
        bodyTransform.anchorMax = new Vector2(0.5f, 0.5f);
        bodyTransform.pivot = new Vector2(0.5f, 0.5f);
        bodyTransform.anchoredPosition = new Vector2(0f, -34f);
        body.alignment = TextAlignmentOptions.TopLeft;
    }

    private static void CreateRoomCodeOverlayText(RectTransform parent, ProjectOneTextStyleSet styleSet)
    {
        TextMeshProUGUI label = CreateStyledText(parent, "RoomCodeLabelText", "ROOM CODE :", ProjectOneTextStyleType.RoomCodeLabel, styleSet, new Vector2(230f, 28f), false);
        RectTransform labelTransform = label.rectTransform;
        labelTransform.anchorMin = new Vector2(0.5f, 0.5f);
        labelTransform.anchorMax = new Vector2(0.5f, 0.5f);
        labelTransform.pivot = new Vector2(0.5f, 0.5f);
        labelTransform.anchoredPosition = new Vector2(0f, 18f);

        TextMeshProUGUI value = CreateStyledText(parent, "RoomCodeValueText", "RPMKJK", ProjectOneTextStyleType.RoomCodeValue, styleSet, new Vector2(230f, 46f), false);
        RectTransform valueTransform = value.rectTransform;
        valueTransform.anchorMin = new Vector2(0.5f, 0.5f);
        valueTransform.anchorMax = new Vector2(0.5f, 0.5f);
        valueTransform.pivot = new Vector2(0.5f, 0.5f);
        valueTransform.anchoredPosition = new Vector2(0f, -18f);
    }

    private static void CreateNotes(RectTransform parent, ProjectOneTextStyleSet styleSet)
    {
        TextMeshProUGUI notes = CreateStyledText(
            parent,
            "Notes_Text",
            "Reference Resolution: 1536 x 864 / All TMP Raycast Target = false / PNG files are referenced only, not modified.",
            ProjectOneTextStyleType.SmallCaption,
            styleSet,
            new Vector2(1200f, 42f),
            false);

        RectTransform rectTransform = notes.rectTransform;
        rectTransform.anchorMin = new Vector2(0.5f, 0f);
        rectTransform.anchorMax = new Vector2(0.5f, 0f);
        rectTransform.pivot = new Vector2(0.5f, 0f);
        rectTransform.anchoredPosition = new Vector2(0f, 22f);
    }

    private static TextMeshProUGUI CreateStyledText(RectTransform parent, string name, string value, ProjectOneTextStyleType styleType, ProjectOneTextStyleSet styleSet, Vector2 size, bool useShadow)
    {
        GameObject textObject = CreateUIObject(name, parent);
        TextMeshProUGUI text = textObject.AddComponent<TextMeshProUGUI>();
        text.text = value;
        text.richText = true;
        text.raycastTarget = false;

        RectTransform rectTransform = text.rectTransform;
        rectTransform.sizeDelta = size;

        ProjectOneTextStyleApplier applier = textObject.AddComponent<ProjectOneTextStyleApplier>();
        applier.target = text;
        applier.styleSet = styleSet;
        applier.styleType = styleType;
        ApplyRecommendedEffectOptions(applier, styleType);
        if (useShadow)
        {
            applier.useSoftShadow = true;
        }

        applier.ApplyStyle();

        return text;
    }

    private static GameObject CreateUIObject(string name, RectTransform parent)
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

    private static Color ParseColor(string hex)
    {
        ColorUtility.TryParseHtmlString(hex, out Color color);
        return color;
    }

    private static void WriteFontCandidateReport()
    {
        string[] guids = AssetDatabase.FindAssets("t:TMP_FontAsset");
        List<string> lines = new List<string>
        {
            "asset_path,asset_name,looks_like_korean_font,looks_like_bold_candidate,note"
        };

        foreach (string path in guids.Select(AssetDatabase.GUIDToAssetPath).OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            TMP_FontAsset fontAsset = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
            if (fontAsset == null)
            {
                continue;
            }

            string searchable = (path + " " + fontAsset.name).ToLowerInvariant();
            bool looksLikeKoreanFont =
                searchable.Contains("notosanskr") ||
                searchable.Contains("korean") ||
                searchable.Contains("hangul") ||
                searchable.Contains("kr");
            bool looksLikeBoldCandidate = LooksLikeBoldCandidate(path, fontAsset.name);
            string note = IsBasicTmpFont(fontAsset)
                ? "TMP default/basic fallback; not recommended for final Korean UI"
                : looksLikeKoreanFont
                    ? "Project-local Korean-capable TMP font candidate"
                    : "Check glyph coverage before using for Korean UI";

            lines.Add(string.Join(",", new[]
            {
                Csv(path),
                Csv(fontAsset.name),
                looksLikeKoreanFont ? "true" : "false",
                looksLikeBoldCandidate ? "true" : "false",
                Csv(note)
            }));
        }

        File.WriteAllLines(FontCandidatesPath, lines);
    }

    private static bool LooksLikeBoldCandidate(string path, string assetName)
    {
        string searchable = path + " " + assetName;
        string[] keywords =
        {
            "Bold",
            "Black",
            "ExtraBold",
            "Heavy",
            "SemiBold",
            "Medium",
            "Pretendard",
            "SUIT",
            "Gmarket",
            "NotoSansKR"
        };

        return keywords.Any(keyword => searchable.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0);
    }

    private static string Csv(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }

    private static void WriteReadme()
    {
        const string readme =
@"# Project ONE TMP 텍스트 스타일 시스템

## 1. PNG에 글자를 박지 않고 TMP로 올리는 이유
Project ONE UI 이미지는 배경, 버튼, 카드 같은 시각 프레임 역할만 맡고, 실제 문구는 `TextMeshProUGUI`로 얹습니다. 이렇게 해야 해상도 변경, 언어 변경, 방 코드/점수/코인 같은 동적 값 변경, 접근성 조정이 가능하며 같은 PNG를 여러 문구에 재사용할 수 있습니다.

## 2. ProjectOneTextStyleSet 사용법
`Assets/ProjectONE/UI/ScriptableObjects/ProjectOneTextStyleSet.asset`은 Project ONE UI Kit의 기본 색상과 글자 크기를 담는 ScriptableObject입니다. `defaultFont`에는 프로젝트 안에 있는 한국어 지원 TMP Font Asset을 연결합니다. 현재 프로젝트에서는 `Assets/NotoSansKR/NotoSansKR-VariableFont_wght SDF.asset` 같은 기존 TMP Font Asset을 우선 사용할 수 있습니다.

## 3. Text Prefab 사용법
`Assets/ProjectONE/UI/Prefabs/Text/` 아래의 `PF_Text_*.prefab`을 Canvas 하위에 배치하고 `Text` 오브젝트의 내용을 원하는 문구로 바꾸면 됩니다. 프리팹 구조는 기본적으로 `PrefabRoot/Text`만 사용하며, 모든 `TextMeshProUGUI`의 `Raycast Target`은 `false`입니다.

## 4. ProjectOneTextStyleApplier 사용법
`ProjectOneTextStyleApplier`는 `TMP_Text target`, `ProjectOneTextStyleSet styleSet`, `ProjectOneTextStyleType styleType`을 기준으로 크기, 색, 정렬, 굵기, fontWeight, character spacing, line spacing, 줄바꿈, overflow를 적용합니다. `useSoftShadow`를 켜면 `UnityEngine.UI.Shadow` 컴포넌트를 사용하고, `useWhiteOutline` 또는 `useNavyOutline`을 켜면 `UnityEngine.UI.Outline` 컴포넌트를 사용합니다. shared material은 직접 수정하지 않습니다.

## 5. 한국어 TMP Font Asset 연결 방법
외부 폰트 다운로드 없이 프로젝트에 이미 있는 TMP Font Asset을 사용합니다. 새 폰트가 필요한 경우에도 이 시스템은 폰트를 만들거나 다운로드하지 않습니다. `ProjectOneTextStyleSet.asset`의 `defaultFont`에 한국어 글리프가 들어 있는 TMP Font Asset을 직접 연결하면 모든 프리팹과 적용 컴포넌트가 같은 폰트를 씁니다. 프로젝트 안에서 TMP Font Asset을 찾지 못하면 TMP 기본 폰트를 임시로 사용하고 Console Warning을 남깁니다.

## 6. 버튼 텍스트 Raycast Target false 이유
버튼의 실제 클릭 판정은 Button 또는 Image가 가져야 합니다. 텍스트가 Raycast Target을 켜면 클릭 이벤트가 텍스트에서 가로막혀 버튼 입력이 불안정해질 수 있으므로, 이 시스템의 모든 TMP 텍스트는 `Raycast Target = false`를 기본으로 둡니다.

## 7. 동적 텍스트 변경 방법
HUD 값은 `ProjectOneHUDTextView`에서 `SetCoin`, `SetTime`, `SetStamina`, `SetMission`으로 갱신합니다. 로비 값은 `ProjectOneLobbyTextView`에서 `SetRoomCode`, `SetReadyCount`, `SetTopMessage`로 갱신합니다. 결과 화면 값은 `ProjectOneResultTextView`에서 `SetResult`, `SetRanking`으로 갱신합니다.

## 8. 대표 텍스트 스타일 추천
`Ready`는 `ReadyLarge`에 `useSoftShadow`를 켜고 필요하면 `useWhiteOutline`을 켜는 구성이 어울립니다. `승리!`는 `ResultTitle`에 약한 shadow를 권장합니다. `ROOM CODE :`는 `RoomCodeLabel`, 실제 코드 값인 `RPMKJK`는 `RoomCodeValue`를 사용합니다. 메뉴 버튼 문구는 `MenuButton`, 카드 제목은 `CardTitle`, 카드 본문은 `CardBody`, 랭킹 이름은 `RankingName`, 점수는 `RankingScore`를 권장합니다.

## 9. Localization String Table 확장
현재 뷰 스크립트는 문자열을 직접 받아 TMP에 반영하는 구조입니다. 나중에 Unity Localization을 붙일 때는 `SetMission`, `SetTopMessage`, `SetRanking` 등에 전달하는 문자열을 String Table에서 가져오도록 바꾸면 PNG나 프리팹 구조를 바꾸지 않고 다국어로 확장할 수 있습니다.

## 10. 기본 폰트처럼 얇고 밋밋하게 보일 때
`ProjectOneTextStyleSet.asset`의 `defaultFont`가 `LiberationSans SDF` 또는 TMP 기본 폰트이면 한글 UI가 얇고 기본 폰트처럼 보일 수 있습니다. `Assets/ProjectONE/UI/Docs/text_font_candidates.csv`를 열어 `looks_like_korean_font`와 `looks_like_bold_candidate`가 `true`인 TMP Font Asset을 확인한 뒤 `defaultFont`에 연결합니다.

## 11. Bold/ExtraBold 계열 TMP Font Asset 연결
프로젝트 안에 `Bold`, `Black`, `ExtraBold`, `Heavy`, `SemiBold`, `Medium`, `Pretendard`, `SUIT`, `Gmarket`, `NotoSansKR` 같은 이름의 TMP Font Asset이 있으면 우선 후보로 검토합니다. 외부 폰트를 다운로드하지 말고, 이미 프로젝트 안에 있는 TMP Font Asset만 연결합니다.

## 12. Ready / 승리! 스타일 확인
`ReadyLarge`는 큰 네이비 글자에 흰색 Outline과 약한 Shadow가 있어야 Project ONE 버튼 위에서 잘 보입니다. `ResultTitle`의 `승리!`는 Black weight와 약한 Shadow로 결과 카드 위 타이틀처럼 보이도록 설정되어 있습니다.

## 13. 한글이 네모로 보일 때
TMP Font Asset에 한글 glyph가 없으면 `Ready`, `ROOM CODE` 같은 영문은 보여도 한글이 네모로 표시될 수 있습니다. 이 경우 `ProjectOneTextStyleSet.asset`의 `defaultFont`에 한국어 glyph가 포함된 TMP Font Asset을 연결해야 합니다.

## 14. StyleSet 수정 후 다시 확인
`ProjectOneTextStyleSet.asset`에서 폰트나 크기를 바꾼 뒤 `Assets/ProjectONE/UI/Scenes/UI_TextStyle_Test.unity`를 열어 `1536 x 864` 기준으로 확인합니다. 필요한 경우 Unity 메뉴 `Project ONE > UI > Create Text Style Setup`을 실행해 테스트용 프리팹과 테스트 씬을 다시 생성합니다.

## 생성 도구
Unity 메뉴 `Project ONE > UI > Create Text Style Setup`을 실행하면 TMP Essential Resources 확인, TMP Font Asset 검색, 기본 스타일셋 생성, 텍스트 프리팹 생성, `UI_TextStyle_Test.unity` 생성, 폰트 후보 CSV 생성, README 생성을 한 번에 수행합니다.
";

        File.WriteAllText(ReadmePath, readme);
    }

    private struct TextPrefabConfig
    {
        public readonly ProjectOneTextStyleType StyleType;
        public readonly string FileName;
        public readonly Vector2 Size;

        public TextPrefabConfig(ProjectOneTextStyleType styleType, string fileName, Vector2 size)
        {
            StyleType = styleType;
            FileName = fileName;
            Size = size;
        }
    }

    private struct SampleTextConfig
    {
        public readonly string Text;
        public readonly ProjectOneTextStyleType StyleType;
        public readonly string Caption;
        public readonly bool UseShadow;

        public SampleTextConfig(string text, ProjectOneTextStyleType styleType, string caption, bool useShadow = false)
        {
            Text = text;
            StyleType = styleType;
            Caption = caption;
            UseShadow = useShadow;
        }
    }

    private struct OverlaySampleConfig
    {
        public readonly string Name;
        public readonly string SpritePath;
        public readonly string Text;
        public readonly ProjectOneTextStyleType StyleType;
        public readonly Vector2 Size;
        public readonly bool UseShadow;

        public OverlaySampleConfig(string name, string spritePath, string text, ProjectOneTextStyleType styleType, Vector2 size, bool useShadow = false)
        {
            Name = name;
            SpritePath = spritePath;
            Text = text;
            StyleType = styleType;
            Size = size;
            UseShadow = useShadow;
        }
    }
}
