#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

public sealed class ProjectOnePaperUiWrapperWindow : EditorWindow
{
    private enum BackFillShapeMode
    {
        ExactAlpha,
        SmoothInner,
        RoundedRect
    }

    private enum UpdateSourceMode
    {
        RootImage,
        PaperOverlayImage,
        ManualSprite
    }

    private const string GeneratedFolder = "Assets/ProjectONE/UI/Generated/BackFills";
    private const string BackFillName = "BackFill";
    private const string PaperOverlayName = "PaperOverlay";
    private const string UndoName = "Wrap Project ONE Paper UI";

    [SerializeField] private Color backFillColor = new Color32(0xF8, 0xEF, 0xDE, 0xFF);
    [SerializeField] private BackFillShapeMode shapeMode = BackFillShapeMode.SmoothInner;
    [SerializeField] private float alphaThreshold = 0.05f;
    [SerializeField] private int cornerRadius = 32;
    [SerializeField] private bool softenEdge;
    [SerializeField] private int smoothPasses = 1;
    [SerializeField] private int backFillInset;
    [SerializeField] private bool fillInternalHoles = true;
    [SerializeField] private bool overwriteExistingBackFill;
    [SerializeField] private bool disableOriginalImage = true;
    [SerializeField] private bool setOverlayRaycastOffWhenNoButton = true;
    [SerializeField] private UpdateSourceMode updateSourceMode = UpdateSourceMode.ManualSprite;
    [SerializeField] private Sprite manualSourceSprite;

    [MenuItem("Project ONE/UI/Paper UI Wrapper")]
    public static void Open()
    {
        ProjectOnePaperUiWrapperWindow window = GetWindow<ProjectOnePaperUiWrapperWindow>();
        window.titleContent = new GUIContent("Paper UI Wrapper");
        window.minSize = new Vector2(420f, 390f);
        window.Show();
    }

    private void OnGUI()
    {
        EditorGUILayout.Space(8f);
        EditorGUILayout.LabelField("Project ONE Paper UI Wrapper", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "선택한 UI Image만 BackFill + PaperOverlay 구조로 변환합니다.\n" +
            "원본 Sprite PNG는 수정하지 않고, 생성된 BackFill PNG만 Generated 폴더에 저장합니다.",
            MessageType.Info);

        EditorGUILayout.Space(6f);
        backFillColor = EditorGUILayout.ColorField("BackFill Color", backFillColor);
        shapeMode = (BackFillShapeMode)EditorGUILayout.EnumPopup("Shape Mode", shapeMode);
        alphaThreshold = EditorGUILayout.Slider("Alpha Threshold", alphaThreshold, 0f, 1f);
        cornerRadius = Mathf.Max(0, EditorGUILayout.IntField("Corner Radius", cornerRadius));
        softenEdge = EditorGUILayout.Toggle("Soften Edge", softenEdge);
        smoothPasses = EditorGUILayout.IntSlider("Smooth Passes", smoothPasses, 0, 4);
        backFillInset = Mathf.Max(0, EditorGUILayout.IntField("BackFill Inset", backFillInset));
        fillInternalHoles = EditorGUILayout.Toggle("Fill Internal Holes", fillInternalHoles);
        overwriteExistingBackFill = EditorGUILayout.Toggle("Overwrite Existing BackFill", overwriteExistingBackFill);
        disableOriginalImage = EditorGUILayout.Toggle("Disable Original Image", disableOriginalImage);
        setOverlayRaycastOffWhenNoButton = EditorGUILayout.Toggle("Overlay Raycast Off When No Button", setOverlayRaycastOffWhenNoButton);

        EditorGUILayout.Space(8f);
        EditorGUILayout.LabelField("Update Existing Wrapper", EditorStyles.boldLabel);
        updateSourceMode = (UpdateSourceMode)EditorGUILayout.EnumPopup("Source Sprite Mode", updateSourceMode);
        manualSourceSprite = (Sprite)EditorGUILayout.ObjectField("Manual Source Sprite", manualSourceSprite, typeof(Sprite), false);

        EditorGUILayout.Space(10f);
        using (new EditorGUI.DisabledScope(Selection.gameObjects == null || Selection.gameObjects.Length == 0))
        {
            if (GUILayout.Button("Apply To Selected", GUILayout.Height(34f)))
                ApplyToSelected();

            if (GUILayout.Button("Update Existing Wrapper", GUILayout.Height(34f)))
                UpdateExistingWrappers();
        }

        EditorGUILayout.Space(8f);
        EditorGUILayout.LabelField("Selected Objects", Selection.gameObjects != null ? Selection.gameObjects.Length.ToString() : "0");
        EditorGUILayout.LabelField("Output Folder", GeneratedFolder);
    }

    private void ApplyToSelected()
    {
        GameObject[] selectedObjects = Selection.gameObjects;
        if (selectedObjects == null || selectedObjects.Length == 0)
        {
            Debug.LogWarning("[ProjectOnePaperUiWrapper] 선택된 GameObject가 없습니다.");
            return;
        }

        EnsureFolder(GeneratedFolder);

        int wrapped = 0;
        int skipped = 0;

        foreach (GameObject selected in selectedObjects)
        {
            if (TryWrap(selected))
                wrapped++;
            else
                skipped++;
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log($"[ProjectOnePaperUiWrapper] 완료: wrapped={wrapped}, skipped={skipped}");
    }

    private void UpdateExistingWrappers()
    {
        GameObject[] selectedObjects = Selection.gameObjects;
        if (selectedObjects == null || selectedObjects.Length == 0)
        {
            Debug.LogWarning("[ProjectOnePaperUiWrapper] 선택된 GameObject가 없습니다.");
            return;
        }

        EnsureFolder(GeneratedFolder);

        int updated = 0;
        int skipped = 0;

        foreach (GameObject selected in selectedObjects)
        {
            if (TryUpdateExistingWrapper(selected))
                updated++;
            else
                skipped++;
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log($"[ProjectOnePaperUiWrapper] 업데이트 완료: updated={updated}, skipped={skipped}");
    }

    private bool TryWrap(GameObject selected)
    {
        if (selected == null)
            return false;

        if (EditorUtility.IsPersistent(selected))
        {
            Debug.LogWarning($"[ProjectOnePaperUiWrapper] Scene instance가 아닌 asset은 처리하지 않습니다: {selected.name}", selected);
            return false;
        }

        RectTransform rootRect = selected.GetComponent<RectTransform>();
        Image sourceImage = selected.GetComponent<Image>();
        if (rootRect == null || sourceImage == null)
        {
            Debug.LogWarning($"[ProjectOnePaperUiWrapper] RectTransform + Image가 있는 UI 오브젝트만 처리합니다: {selected.name}", selected);
            return false;
        }

        Sprite sourceSprite = sourceImage.sprite;
        if (sourceSprite == null)
        {
            Debug.LogWarning($"[ProjectOnePaperUiWrapper] Image.sourceImage(Sprite)이 비어 있습니다: {selected.name}", selected);
            return false;
        }

        if (HasDirectChild(rootRect, BackFillName) || HasDirectChild(rootRect, PaperOverlayName))
        {
            Debug.LogWarning($"[ProjectOnePaperUiWrapper] 이미 BackFill 또는 PaperOverlay 자식이 있어 스킵합니다: {selected.name}", selected);
            return false;
        }

        Sprite backFillSprite = GetOrCreateBackFillSprite(sourceSprite);
        if (backFillSprite == null)
        {
            Debug.LogWarning($"[ProjectOnePaperUiWrapper] BackFill Sprite 생성 실패: {selected.name}", selected);
            return false;
        }

        int undoGroup = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName(UndoName);
        Undo.RegisterFullObjectHierarchyUndo(selected, UndoName);

        Button button = selected.GetComponent<Button>();
        Image backFillImage = CreateBackFill(rootRect, backFillSprite, sourceImage);
        Image overlayImage = CreatePaperOverlay(rootRect, sourceImage, button != null);

        if (disableOriginalImage)
        {
            Undo.RecordObject(sourceImage, UndoName);
            sourceImage.enabled = false;
            EditorUtility.SetDirty(sourceImage);
        }

        if (button != null)
        {
            Undo.RecordObject(button, UndoName);
            button.targetGraphic = overlayImage;
            overlayImage.raycastTarget = true;
            EditorUtility.SetDirty(button);
            EditorUtility.SetDirty(overlayImage);
        }

        backFillImage.transform.SetSiblingIndex(0);
        overlayImage.transform.SetSiblingIndex(1);

        EditorUtility.SetDirty(selected);
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(selected.scene);
        Undo.CollapseUndoOperations(undoGroup);

        Debug.Log(
            $"[ProjectOnePaperUiWrapper] Wrapped '{selected.name}' with BackFill='{backFillSprite.name}', Button={(button != null ? "yes" : "no")}",
            selected);

        return true;
    }

    private bool TryUpdateExistingWrapper(GameObject selected)
    {
        if (selected == null)
            return false;

        if (EditorUtility.IsPersistent(selected))
        {
            Debug.LogWarning($"[ProjectOnePaperUiWrapper] Scene instance가 아닌 asset은 처리하지 않습니다: {selected.name}", selected);
            return false;
        }

        Image backFillImage = GetDirectChildImage(selected.transform, BackFillName);
        Image overlayImage = GetDirectChildImage(selected.transform, PaperOverlayName);
        if (backFillImage == null || overlayImage == null)
        {
            Debug.LogWarning($"[ProjectOnePaperUiWrapper] BackFill + PaperOverlay Image 구조가 없어 업데이트를 스킵합니다: {selected.name}", selected);
            return false;
        }

        Sprite sourceSprite = GetUpdateSourceSprite(selected, overlayImage);
        if (sourceSprite == null)
        {
            Debug.LogWarning($"[ProjectOnePaperUiWrapper] 업데이트 기준 Sprite가 없습니다: {selected.name}, mode={updateSourceMode}", selected);
            return false;
        }

        Sprite backFillSprite = GetOrCreateBackFillSprite(sourceSprite);
        if (backFillSprite == null)
        {
            Debug.LogWarning($"[ProjectOnePaperUiWrapper] BackFill Sprite 생성 실패: {selected.name}", selected);
            return false;
        }

        int undoGroup = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName(UndoName);

        Undo.RecordObject(backFillImage, UndoName);
        SetImageSprite(backFillImage, backFillSprite);
        EditorUtility.SetDirty(backFillImage);

        Undo.RecordObject(overlayImage, UndoName);
        SetImageSprite(overlayImage, sourceSprite);
        EditorUtility.SetDirty(overlayImage);

        Button button = selected.GetComponent<Button>();
        if (button != null)
        {
            Undo.RecordObject(button, UndoName);
            button.targetGraphic = overlayImage;
            overlayImage.raycastTarget = true;
            EditorUtility.SetDirty(button);
            EditorUtility.SetDirty(overlayImage);
        }

        EditorUtility.SetDirty(selected);
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(selected.scene);
        Undo.CollapseUndoOperations(undoGroup);

        Debug.Log(
            $"[ProjectOnePaperUiWrapper] Updated '{selected.name}' with Source='{sourceSprite.name}', BackFill='{backFillSprite.name}', mode={updateSourceMode}",
            selected);

        return true;
    }

    private Sprite GetUpdateSourceSprite(GameObject selected, Image overlayImage)
    {
        switch (updateSourceMode)
        {
            case UpdateSourceMode.RootImage:
                return GetImageSprite(selected.GetComponent<Image>());
            case UpdateSourceMode.PaperOverlayImage:
                return GetImageSprite(overlayImage);
            case UpdateSourceMode.ManualSprite:
            default:
                return manualSourceSprite;
        }
    }

    private Image CreateBackFill(RectTransform parent, Sprite backFillSprite, Image sourceImage)
    {
        GameObject go = new GameObject(BackFillName, typeof(RectTransform), typeof(Image));
        Undo.RegisterCreatedObjectUndo(go, UndoName);
        RectTransform rect = go.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        Stretch(rect, backFillInset);

        Image image = go.GetComponent<Image>();
        image.sprite = backFillSprite;
        image.color = Color.white;
        image.raycastTarget = false;
        image.type = sourceImage.type;
        image.preserveAspect = sourceImage.preserveAspect;
        image.fillCenter = sourceImage.fillCenter;
        image.fillMethod = sourceImage.fillMethod;
        image.fillOrigin = sourceImage.fillOrigin;
        image.fillAmount = sourceImage.fillAmount;
        image.fillClockwise = sourceImage.fillClockwise;
        image.pixelsPerUnitMultiplier = sourceImage.pixelsPerUnitMultiplier;
        image.maskable = sourceImage.maskable;
        return image;
    }

    private Image CreatePaperOverlay(RectTransform parent, Image sourceImage, bool hasButton)
    {
        GameObject go = new GameObject(PaperOverlayName, typeof(RectTransform), typeof(Image));
        Undo.RegisterCreatedObjectUndo(go, UndoName);
        RectTransform rect = go.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        Stretch(rect, 0);

        Image image = go.GetComponent<Image>();
        CopyImageSettings(sourceImage, image);

        if (!hasButton && setOverlayRaycastOffWhenNoButton)
            image.raycastTarget = false;

        if (hasButton)
            image.raycastTarget = true;

        return image;
    }

    private void CopyImageSettings(Image source, Image target)
    {
        target.sprite = source.sprite;
        target.overrideSprite = source.overrideSprite;
        target.color = source.color;
        target.material = source.material;
        target.raycastTarget = source.raycastTarget;
        target.maskable = source.maskable;
        target.type = source.type;
        target.preserveAspect = source.preserveAspect;
        target.fillCenter = source.fillCenter;
        target.fillMethod = source.fillMethod;
        target.fillOrigin = source.fillOrigin;
        target.fillAmount = source.fillAmount;
        target.fillClockwise = source.fillClockwise;
        target.pixelsPerUnitMultiplier = source.pixelsPerUnitMultiplier;
        target.useSpriteMesh = source.useSpriteMesh;
    }

    private Sprite GetOrCreateBackFillSprite(Sprite sourceSprite)
    {
        string assetPath = BuildBackFillPath(sourceSprite);
        if (File.Exists(AssetPathToFullPath(assetPath)) && !overwriteExistingBackFill)
        {
            Sprite existing = AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
            if (existing != null)
            {
                Debug.Log($"[ProjectOnePaperUiWrapper] 기존 BackFill 재사용: {assetPath}");
                return existing;
            }

            Debug.LogWarning($"[ProjectOnePaperUiWrapper] 기존 파일은 있으나 Sprite로 로드할 수 없습니다. Overwrite 옵션을 켜고 다시 생성하세요: {assetPath}");
            return null;
        }

        Texture2D backFillTexture = CreateBackFillTexture(sourceSprite);
        if (backFillTexture == null)
            return null;

        byte[] pngBytes = backFillTexture.EncodeToPNG();
        DestroyImmediate(backFillTexture);

        File.WriteAllBytes(AssetPathToFullPath(assetPath), pngBytes);
        AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);
        ConfigureBackFillImporter(assetPath, sourceSprite);
        AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);

        Sprite created = AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
        if (created != null)
            Debug.Log($"[ProjectOnePaperUiWrapper] BackFill 생성: {assetPath}");

        return created;
    }

    private Texture2D CreateBackFillTexture(Sprite sourceSprite)
    {
        Color32[] sourcePixels = ReadSpritePixels(sourceSprite, out int width, out int height);
        if (sourcePixels == null || width <= 0 || height <= 0)
            return null;

        bool[] solidMask = BuildSolidMask(sourcePixels, alphaThreshold);
        bool[] fillMask = BuildFillMask(solidMask, width, height);

        Color32[] resultPixels = new Color32[sourcePixels.Length];
        Color32 fill = backFillColor;
        fill.a = 255;

        for (int i = 0; i < resultPixels.Length; i++)
        {
            if (!fillMask[i])
            {
                resultPixels[i] = new Color32(0, 0, 0, 0);
                continue;
            }

            Color32 pixel = fill;
            if (softenEdge && IsEdgePixel(fillMask, i % width, i / width, width, height))
                pixel.a = 210;

            resultPixels[i] = pixel;
        }

        Texture2D texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
        texture.name = sourceSprite.name + "_backfill";
        texture.SetPixels32(resultPixels);
        texture.Apply(false, false);
        return texture;
    }

    private bool[] BuildFillMask(bool[] solidMask, int width, int height)
    {
        switch (shapeMode)
        {
            case BackFillShapeMode.RoundedRect:
                return BuildRoundedRectMask(width, height, cornerRadius);
            case BackFillShapeMode.SmoothInner:
                return BuildSmoothInnerMask(solidMask, width, height);
            case BackFillShapeMode.ExactAlpha:
            default:
                return BuildExactAlphaMask(solidMask, width, height);
        }
    }

    private bool[] BuildExactAlphaMask(bool[] solidMask, int width, int height)
    {
        if (!fillInternalHoles)
            return CopyMask(solidMask);

        bool[] exteriorTransparent = BuildExteriorTransparentMask(solidMask, width, height);
        bool[] result = new bool[solidMask.Length];
        for (int i = 0; i < result.Length; i++)
            result[i] = solidMask[i] || !exteriorTransparent[i];

        return result;
    }

    private bool[] BuildSmoothInnerMask(bool[] solidMask, int width, int height)
    {
        bool[] result = BuildExactAlphaMask(solidMask, width, height);
        int passes = Mathf.Max(0, smoothPasses);

        for (int i = 0; i < passes; i++)
            result = ApplyNeighborCleanup(result, width, height);

        if (fillInternalHoles)
            result = FillInternalHoles(result, width, height);

        return result;
    }

    private static bool[] FillInternalHoles(bool[] solidMask, int width, int height)
    {
        bool[] exteriorTransparent = BuildExteriorTransparentMask(solidMask, width, height);
        bool[] result = new bool[solidMask.Length];
        for (int i = 0; i < result.Length; i++)
            result[i] = solidMask[i] || !exteriorTransparent[i];

        return result;
    }

    private static bool[] ApplyNeighborCleanup(bool[] source, int width, int height)
    {
        bool[] result = new bool[source.Length];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int index = y * width + x;
                int neighbors = CountSolidNeighborhood(source, x, y, width, height);

                if (source[index])
                    result[index] = neighbors >= 4;
                else
                    result[index] = neighbors >= 7;
            }
        }

        return result;
    }

    private static int CountSolidNeighborhood(bool[] mask, int x, int y, int width, int height)
    {
        int count = 0;
        for (int oy = -1; oy <= 1; oy++)
        {
            for (int ox = -1; ox <= 1; ox++)
            {
                int nx = x + ox;
                int ny = y + oy;
                if (nx < 0 || ny < 0 || nx >= width || ny >= height)
                    continue;

                if (mask[ny * width + nx])
                    count++;
            }
        }

        return count;
    }

    private static bool[] BuildRoundedRectMask(int width, int height, int radius)
    {
        bool[] mask = new bool[width * height];
        float safeRadius = Mathf.Clamp(radius, 0f, Mathf.Min(width, height) * 0.5f);

        if (safeRadius <= 0.01f)
        {
            for (int i = 0; i < mask.Length; i++)
                mask[i] = true;
            return mask;
        }

        float leftCenter = safeRadius;
        float rightCenter = width - safeRadius;
        float bottomCenter = safeRadius;
        float topCenter = height - safeRadius;
        float radiusSqr = safeRadius * safeRadius;

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                float px = x + 0.5f;
                float py = y + 0.5f;

                bool insideBody = px >= leftCenter && px <= rightCenter;
                bool insideBand = py >= bottomCenter && py <= topCenter;
                if (insideBody || insideBand)
                {
                    mask[y * width + x] = true;
                    continue;
                }

                float cx = px < leftCenter ? leftCenter : rightCenter;
                float cy = py < bottomCenter ? bottomCenter : topCenter;
                float dx = px - cx;
                float dy = py - cy;
                mask[y * width + x] = dx * dx + dy * dy <= radiusSqr;
            }
        }

        return mask;
    }

    private static bool[] CopyMask(bool[] source)
    {
        bool[] copy = new bool[source.Length];
        System.Array.Copy(source, copy, source.Length);
        return copy;
    }

    private static bool IsEdgePixel(bool[] mask, int x, int y, int width, int height)
    {
        for (int oy = -1; oy <= 1; oy++)
        {
            for (int ox = -1; ox <= 1; ox++)
            {
                if (ox == 0 && oy == 0)
                    continue;

                int nx = x + ox;
                int ny = y + oy;
                if (nx < 0 || ny < 0 || nx >= width || ny >= height)
                    return true;

                if (!mask[ny * width + nx])
                    return true;
            }
        }

        return false;
    }

    private static Color32[] ReadSpritePixels(Sprite sourceSprite, out int width, out int height)
    {
        Rect textureRect = sourceSprite.textureRect;
        int x = Mathf.RoundToInt(textureRect.x);
        int y = Mathf.RoundToInt(textureRect.y);
        width = Mathf.RoundToInt(textureRect.width);
        height = Mathf.RoundToInt(textureRect.height);

        if (sourceSprite.texture == null || width <= 0 || height <= 0)
            return null;

        RenderTexture previous = RenderTexture.active;
        RenderTexture renderTexture = null;
        Texture2D readable = null;

        try
        {
            Texture2D sourceTexture = sourceSprite.texture;
            renderTexture = RenderTexture.GetTemporary(sourceTexture.width, sourceTexture.height, 0, RenderTextureFormat.ARGB32);
            Graphics.Blit(sourceTexture, renderTexture);
            RenderTexture.active = renderTexture;

            readable = new Texture2D(width, height, TextureFormat.RGBA32, false);
            readable.ReadPixels(new Rect(x, y, width, height), 0, 0, false);
            readable.Apply(false, false);
            return readable.GetPixels32();
        }
        finally
        {
            RenderTexture.active = previous;
            if (renderTexture != null)
                RenderTexture.ReleaseTemporary(renderTexture);
            if (readable != null)
                DestroyImmediate(readable);
        }
    }

    private static bool[] BuildSolidMask(Color32[] pixels, float threshold)
    {
        bool[] mask = new bool[pixels.Length];
        byte alphaByte = (byte)Mathf.Clamp(Mathf.RoundToInt(threshold * 255f), 0, 255);
        for (int i = 0; i < pixels.Length; i++)
            mask[i] = pixels[i].a > alphaByte;
        return mask;
    }

    private static bool[] BuildExteriorTransparentMask(bool[] solidMask, int width, int height)
    {
        bool[] exterior = new bool[solidMask.Length];
        Queue<int> queue = new Queue<int>();

        for (int x = 0; x < width; x++)
        {
            EnqueueTransparent(x, 0, width, solidMask, exterior, queue);
            EnqueueTransparent(x, height - 1, width, solidMask, exterior, queue);
        }

        for (int y = 0; y < height; y++)
        {
            EnqueueTransparent(0, y, width, solidMask, exterior, queue);
            EnqueueTransparent(width - 1, y, width, solidMask, exterior, queue);
        }

        while (queue.Count > 0)
        {
            int index = queue.Dequeue();
            int x = index % width;
            int y = index / width;

            EnqueueTransparent(x - 1, y, width, solidMask, exterior, queue);
            EnqueueTransparent(x + 1, y, width, solidMask, exterior, queue);
            EnqueueTransparent(x, y - 1, width, solidMask, exterior, queue);
            EnqueueTransparent(x, y + 1, width, solidMask, exterior, queue);
        }

        return exterior;
    }

    private static void EnqueueTransparent(int x, int y, int width, bool[] solidMask, bool[] exterior, Queue<int> queue)
    {
        int height = solidMask.Length / width;
        if (x < 0 || y < 0 || x >= width || y >= height)
            return;

        int index = y * width + x;
        if (solidMask[index] || exterior[index])
            return;

        exterior[index] = true;
        queue.Enqueue(index);
    }

    private static bool HasDirectChild(RectTransform parent, string childName)
    {
        for (int i = 0; i < parent.childCount; i++)
        {
            Transform child = parent.GetChild(i);
            if (child != null && child.name == childName)
                return true;
        }

        return false;
    }

    private static Image GetDirectChildImage(Transform parent, string childName)
    {
        Transform child = FindDirectChild(parent, childName);
        return child != null ? child.GetComponent<Image>() : null;
    }

    private static Transform FindDirectChild(Transform parent, string childName)
    {
        if (parent == null)
            return null;

        for (int i = 0; i < parent.childCount; i++)
        {
            Transform child = parent.GetChild(i);
            if (child != null && child.name == childName)
                return child;
        }

        return null;
    }

    private static Sprite GetImageSprite(Image image)
    {
        if (image == null)
            return null;

        return image.overrideSprite != null ? image.overrideSprite : image.sprite;
    }

    private static void SetImageSprite(Image image, Sprite sprite)
    {
        image.sprite = sprite;
        image.overrideSprite = null;
    }

    private static void Stretch(RectTransform rect, int inset)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.offsetMin = new Vector2(inset, inset);
        rect.offsetMax = new Vector2(-inset, -inset);
        rect.localScale = Vector3.one;
        rect.localRotation = Quaternion.identity;
    }

    private string BuildBackFillPath(Sprite sourceSprite)
    {
        string sourcePath = AssetDatabase.GetAssetPath(sourceSprite);
        string textureName = string.IsNullOrEmpty(sourcePath)
            ? sourceSprite.name
            : Path.GetFileNameWithoutExtension(sourcePath);

        string baseName = textureName;
        if (!string.IsNullOrEmpty(sourceSprite.name) && sourceSprite.name != textureName)
            baseName += "_" + sourceSprite.name;

        string suffix = shapeMode == BackFillShapeMode.ExactAlpha
            ? "_backfill.png"
            : "_" + shapeMode.ToString().ToLowerInvariant() + "_backfill.png";

        return GeneratedFolder + "/" + SanitizeFileName(baseName) + suffix;
    }

    private static string SanitizeFileName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "paper_ui";

        char[] invalid = Path.GetInvalidFileNameChars();
        char[] chars = value.ToCharArray();
        for (int i = 0; i < chars.Length; i++)
        {
            if (System.Array.IndexOf(invalid, chars[i]) >= 0 || char.IsWhiteSpace(chars[i]))
                chars[i] = '_';
        }

        return new string(chars);
    }

    private static string AssetPathToFullPath(string assetPath)
    {
        string projectRoot = Directory.GetCurrentDirectory().Replace("\\", "/");
        return Path.Combine(projectRoot, assetPath).Replace("\\", "/");
    }

    private static void ConfigureBackFillImporter(string assetPath, Sprite sourceSprite)
    {
        TextureImporter importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
        if (importer == null)
            return;

        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.alphaIsTransparency = true;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.mipmapEnabled = false;
        importer.maxTextureSize = 4096;
        importer.spriteBorder = sourceSprite.border;
        importer.spritePixelsPerUnit = sourceSprite.pixelsPerUnit;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.SaveAndReimport();
    }

    private static void EnsureFolder(string folderPath)
    {
        if (AssetDatabase.IsValidFolder(folderPath))
            return;

        string[] parts = folderPath.Split('/');
        string current = parts[0];
        for (int i = 1; i < parts.Length; i++)
        {
            string next = current + "/" + parts[i];
            if (!AssetDatabase.IsValidFolder(next))
                AssetDatabase.CreateFolder(current, parts[i]);
            current = next;
        }
    }
}
#endif
