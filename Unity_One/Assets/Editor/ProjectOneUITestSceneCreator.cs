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

public static class ProjectOneUITestSceneCreator
{
    private const string MenuPath = "Project ONE/UI/Create UI Test Scene";
    private const string SpriteRoot = "Assets/ProjectONE/UI/Sprites";
    private const string SceneFolder = "Assets/ProjectONE/UI/Scenes";
    private const string ScenePath = SceneFolder + "/UI_Test.unity";
    private const int MaxSamplesPerCategory = 8;

    private static readonly string[] SampleFolders =
    {
        "Buttons",
        "Panels",
        "HUD",
        "MainMenu",
        "Lobby_CharacterSelect",
        "Result_Ranking",
        "Icons",
        "Decorations",
        "Characters",
        "ColorChips"
    };

    [MenuItem(MenuPath)]
    public static void CreateUITestScene()
    {
        if (!AssetDatabase.IsValidFolder(SpriteRoot))
        {
            Debug.LogError($"Project ONE UI Test Scene Creator: Sprite root folder does not exist: {SpriteRoot}");
            return;
        }

        if (SceneFileExists() &&
            !EditorUtility.DisplayDialog(
                "Overwrite UI Test Scene",
                $"{ScenePath} already exists. Overwrite the generated test scene?",
                "Overwrite",
                "Cancel"))
        {
            Debug.Log("Project ONE UI Test Scene Creator: Scene creation cancelled.");
            return;
        }

        EnsureFolder(SceneFolder);

        Scene testScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        EditorSceneManager.SetActiveScene(testScene);

        int sampleCount = 0;
        RectTransform gridTransform = null;
        try
        {
            BuildScene(out sampleCount, out gridTransform);

            if (!EditorSceneManager.SaveScene(testScene, ScenePath))
            {
                Debug.LogError($"Project ONE UI Test Scene Creator: Failed to save scene at {ScenePath}");
                return;
            }

            AssetDatabase.Refresh();
            SceneAsset sceneAsset = AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath);
            if (sceneAsset != null)
            {
                Selection.activeObject = sceneAsset;
                EditorGUIUtility.PingObject(sceneAsset);
            }

            Debug.Log($"Project ONE UI Test Scene created: {ScenePath}. Sample count: {sampleCount}. {BuildGridRectLog(gridTransform)}");
        }
        catch (Exception exception)
        {
            Debug.LogError($"Project ONE UI Test Scene Creator failed: {exception}");
        }
    }

    private static void BuildScene(out int sampleCount, out RectTransform gridTransform)
    {
        sampleCount = 0;

        CreateCamera();
        CreateEventSystem();

        GameObject canvasObject = CreateCanvas();
        RectTransform canvasTransform = canvasObject.GetComponent<RectTransform>();

        CreateDarkBackground(canvasTransform);
        CreateHeaderText(canvasTransform);
        gridTransform = CreateSampleGrid(canvasTransform);
        CreateNotesText(canvasTransform);

        foreach (SampleSprite sample in CollectSamples())
        {
            CreateSampleItem(gridTransform, sample);
            sampleCount++;
        }
    }

    private static void CreateCamera()
    {
        GameObject cameraObject = new GameObject("Camera");
        Camera camera = cameraObject.AddComponent<Camera>();
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = ParseColor("#1F2A44");
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
        scaler.referenceResolution = new Vector2(1536f, 864f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;

        return canvasObject;
    }

    private static void CreateDarkBackground(RectTransform parent)
    {
        GameObject backgroundObject = CreateUIObject("Test_DarkBackground", parent);
        Image image = backgroundObject.AddComponent<Image>();
        image.color = ParseColor("#1F2A44");
        image.raycastTarget = false;

        RectTransform rectTransform = backgroundObject.GetComponent<RectTransform>();
        Stretch(rectTransform);
        rectTransform.SetAsFirstSibling();
    }

    private static void CreateHeaderText(RectTransform parent)
    {
        GameObject textObject = CreateUIObject("Header_Text", parent);
        TextMeshProUGUI text = textObject.AddComponent<TextMeshProUGUI>();
        text.text = "Project ONE UI Sprite Test";
        text.fontSize = 36f;
        text.color = Color.white;
        text.alignment = TextAlignmentOptions.Center;
        text.raycastTarget = false;

        RectTransform rectTransform = textObject.GetComponent<RectTransform>();
        rectTransform.anchorMin = new Vector2(0.5f, 1f);
        rectTransform.anchorMax = new Vector2(0.5f, 1f);
        rectTransform.pivot = new Vector2(0.5f, 1f);
        rectTransform.anchoredPosition = new Vector2(0f, -28f);
        rectTransform.sizeDelta = new Vector2(900f, 60f);
    }

    private static RectTransform CreateSampleGrid(RectTransform parent)
    {
        GameObject gridObject = CreateUIObject("UI_Sample_Grid", parent);
        RectTransform rectTransform = gridObject.GetComponent<RectTransform>();
        rectTransform.anchorMin = new Vector2(0f, 0f);
        rectTransform.anchorMax = new Vector2(1f, 1f);
        rectTransform.pivot = new Vector2(0.5f, 0.5f);
        rectTransform.offsetMin = new Vector2(40f, 80f);
        rectTransform.offsetMax = new Vector2(-40f, -90f);
        rectTransform.localScale = Vector3.one;

        GridLayoutGroup grid = gridObject.AddComponent<GridLayoutGroup>();
        grid.cellSize = new Vector2(220f, 160f);
        grid.spacing = new Vector2(24f, 32f);
        grid.padding = new RectOffset(20, 20, 20, 20);
        grid.childAlignment = TextAnchor.UpperCenter;
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = 5;

        return rectTransform;
    }

    private static void CreateNotesText(RectTransform parent)
    {
        GameObject textObject = CreateUIObject("Notes_Text", parent);
        TextMeshProUGUI text = textObject.AddComponent<TextMeshProUGUI>();
        text.text = "Check for checkerboard baked backgrounds, white boxes, broken edges, and blurry sprites.";
        text.fontSize = 20f;
        text.color = Color.white;
        text.alignment = TextAlignmentOptions.Center;
        text.raycastTarget = false;

        RectTransform rectTransform = textObject.GetComponent<RectTransform>();
        rectTransform.anchorMin = new Vector2(0.5f, 0f);
        rectTransform.anchorMax = new Vector2(0.5f, 0f);
        rectTransform.pivot = new Vector2(0.5f, 0f);
        rectTransform.anchoredPosition = new Vector2(0f, 24f);
        rectTransform.sizeDelta = new Vector2(1200f, 48f);
    }

    private static void CreateSampleItem(RectTransform parent, SampleSprite sample)
    {
        GameObject itemObject = CreateUIObject($"SampleItem_{sample.Category}_{Path.GetFileNameWithoutExtension(sample.AssetPath)}", parent);
        itemObject.SetActive(true);
        RectTransform itemTransform = itemObject.GetComponent<RectTransform>();
        itemTransform.localScale = Vector3.one;
        itemTransform.anchoredPosition = Vector2.zero;
        itemTransform.sizeDelta = new Vector2(220f, 160f);

        GameObject imageObject = CreateUIObject("PreviewImage", itemTransform);
        Image image = imageObject.AddComponent<Image>();
        image.sprite = sample.Sprite;
        image.preserveAspect = true;
        image.raycastTarget = false;

        RectTransform imageTransform = imageObject.GetComponent<RectTransform>();
        imageTransform.anchorMin = new Vector2(0.5f, 1f);
        imageTransform.anchorMax = new Vector2(0.5f, 1f);
        imageTransform.pivot = new Vector2(0.5f, 1f);
        imageTransform.anchoredPosition = new Vector2(0f, -10f);
        imageTransform.sizeDelta = new Vector2(180f, 110f);
        imageTransform.localScale = Vector3.one;

        GameObject fileNameObject = CreateUIObject("FileNameText", itemTransform);
        TextMeshProUGUI fileNameText = fileNameObject.AddComponent<TextMeshProUGUI>();
        fileNameText.text = Path.GetFileName(sample.AssetPath);
        fileNameText.fontSize = 12f;
        fileNameText.color = Color.white;
        fileNameText.alignment = TextAlignmentOptions.Center;
        fileNameText.textWrappingMode = TextWrappingModes.Normal;
        fileNameText.raycastTarget = false;

        RectTransform textTransform = fileNameObject.GetComponent<RectTransform>();
        textTransform.anchorMin = new Vector2(0.5f, 1f);
        textTransform.anchorMax = new Vector2(0.5f, 1f);
        textTransform.pivot = new Vector2(0.5f, 1f);
        textTransform.anchoredPosition = new Vector2(0f, -122f);
        textTransform.sizeDelta = new Vector2(200f, 36f);
        textTransform.localScale = Vector3.one;
    }

    private static IEnumerable<SampleSprite> CollectSamples()
    {
        foreach (string folderName in SampleFolders)
        {
            string folderPath = $"{SpriteRoot}/{folderName}";
            if (!AssetDatabase.IsValidFolder(folderPath))
            {
                Debug.LogWarning($"Project ONE UI Test Scene Creator: Sample folder missing: {folderPath}");
                continue;
            }

            string[] guids = AssetDatabase.FindAssets("t:Texture2D", new[] { folderPath });
            foreach (string assetPath in guids
                         .Select(AssetDatabase.GUIDToAssetPath)
                         .Where(path => path.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                         .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                         .Take(MaxSamplesPerCategory))
            {
                Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
                if (sprite == null)
                {
                    Debug.LogWarning($"Project ONE UI Test Scene Creator: Sprite could not be loaded from {assetPath}");
                    continue;
                }

                yield return new SampleSprite(folderName, assetPath, sprite);
            }
        }
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

    private static string BuildGridRectLog(RectTransform gridTransform)
    {
        if (gridTransform == null)
        {
            return "UI_Sample_Grid RectTransform: null";
        }

        return
            "UI_Sample_Grid RectTransform: " +
            $"anchorMin={gridTransform.anchorMin}, " +
            $"anchorMax={gridTransform.anchorMax}, " +
            $"pivot={gridTransform.pivot}, " +
            $"offsetMin={gridTransform.offsetMin}, " +
            $"offsetMax={gridTransform.offsetMax}, " +
            $"sizeDelta={gridTransform.sizeDelta}, " +
            $"anchoredPosition={gridTransform.anchoredPosition}";
    }

    private static bool SceneFileExists()
    {
        return AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) != null || File.Exists(AssetPathToFullPath(ScenePath));
    }

    private static string AssetPathToFullPath(string assetPath)
    {
        string projectRoot = Path.GetDirectoryName(Application.dataPath);
        return Path.Combine(projectRoot, NormalizeAssetPath(assetPath));
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

    private static Color ParseColor(string htmlColor)
    {
        return ColorUtility.TryParseHtmlString(htmlColor, out Color color) ? color : Color.black;
    }

    private readonly struct SampleSprite
    {
        public readonly string Category;
        public readonly string AssetPath;
        public readonly Sprite Sprite;

        public SampleSprite(string category, string assetPath, Sprite sprite)
        {
            Category = category;
            AssetPath = assetPath;
            Sprite = sprite;
        }
    }
}
