#if UNITY_EDITOR
using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

public sealed class ProjectOneReferenceUIBuilderWindow : EditorWindow
{
    private const string DefaultRecipeFolder = "Assets/ProjectONE/UI/ScriptableObjects";
    private const string DefaultRecipePath = DefaultRecipeFolder + "/ProjectOneUIRecipe.asset";
    private const string BackFillName = "BackFill";
    private const string PaperOverlayName = "PaperOverlay";
    private const string ContentRootName = "ContentRoot";
    private const float CardHorizontalPadding = 56f;
    private const float CardVerticalPadding = 44f;
    private const float TipCardHeight = 86f;

    [SerializeField] private ProjectOneUIRecipe recipe;
    [SerializeField] private string menuButtonLabel = "빠른 시작";
    [SerializeField] private string loadingTitle = "로딩 중...";
    [SerializeField] private string loadingBody = "잠시만 기다려 주세요";

    private Vector2 scroll;

    [MenuItem("Project ONE/UI/Reference UI Builder")]
    public static void Open()
    {
        ProjectOneReferenceUIBuilderWindow window = GetWindow<ProjectOneReferenceUIBuilderWindow>();
        window.titleContent = new GUIContent("Reference UI Builder");
        window.minSize = new Vector2(420f, 520f);
        window.Show();
    }

    private void OnEnable()
    {
        if (recipe == null)
            recipe = FindFirstRecipe();
    }

    private void OnGUI()
    {
        scroll = EditorGUILayout.BeginScrollView(scroll);

        EditorGUILayout.Space(8f);
        EditorGUILayout.LabelField("Project ONE Reference UI Builder", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "선택한 RectTransform 아래에만 Project One 레퍼런스 스타일 UI를 생성합니다.\n" +
            "씬 전체 자동 변경, 기존 Button.onClick 삭제, Sprite 원본 수정은 하지 않습니다.",
            MessageType.Info);

        DrawRecipeSection();
        DrawCreateSection();
        DrawApplySection();

        EditorGUILayout.Space(8f);
        EditorGUILayout.LabelField("Active Selection", Selection.activeGameObject != null ? Selection.activeGameObject.name : "None");
        EditorGUILayout.EndScrollView();
    }

    private void DrawRecipeSection()
    {
        EditorGUILayout.Space(8f);
        EditorGUILayout.LabelField("1. Recipe", EditorStyles.boldLabel);

        recipe = (ProjectOneUIRecipe)EditorGUILayout.ObjectField("Recipe", recipe, typeof(ProjectOneUIRecipe), false);

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Find Recipe"))
                recipe = FindFirstRecipe();

            if (GUILayout.Button("Create Recipe Asset"))
                recipe = CreateRecipeAsset();
        }
    }

    private void DrawCreateSection()
    {
        EditorGUILayout.Space(8f);
        EditorGUILayout.LabelField("2. Create Under Selected RectTransform", EditorStyles.boldLabel);

        menuButtonLabel = EditorGUILayout.TextField("Menu Button Label", menuButtonLabel);
        loadingTitle = EditorGUILayout.TextField("Loading Title", loadingTitle);
        loadingBody = EditorGUILayout.TextField("Loading Body", loadingBody);

        using (new EditorGUI.DisabledScope(recipe == null))
        {
            if (GUILayout.Button("Create Menu Button", GUILayout.Height(32f)))
                CreateMenuButton();

            if (GUILayout.Button("Create Paper Card", GUILayout.Height(32f)))
                CreatePaperCard();

            if (GUILayout.Button("Create Loading Card", GUILayout.Height(32f)))
                CreateLoadingCard();
        }
    }

    private void DrawApplySection()
    {
        EditorGUILayout.Space(8f);
        EditorGUILayout.LabelField("3. Apply To Selected", EditorStyles.boldLabel);

        using (new EditorGUI.DisabledScope(recipe == null))
        {
            if (GUILayout.Button("Apply Text Style To Selected TMP", GUILayout.Height(30f)))
                ApplyTextStyleToSelected();

            if (GUILayout.Button("Apply Button Style To Selected", GUILayout.Height(30f)))
                ApplyButtonStyleToSelected();

            if (GUILayout.Button("Apply Card Style To Selected", GUILayout.Height(30f)))
                ApplyCardStyleToSelected();
        }
    }

    private void CreateMenuButton()
    {
        RectTransform parent = ResolveSelectedParent();
        if (parent == null || !ValidateRecipe())
            return;

        const string undoName = "Create Project ONE Menu Button";
        int undoGroup = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName(undoName);

        RectTransform root = CreateRect(parent, "Button_ProjectOne_Menu", undoName);
        SetCenteredBox(root, recipe.menuButtonSize);
        root.localRotation = Quaternion.Euler(0f, 0f, recipe.menuPanelRotation);

        Image image = Undo.AddComponent<Image>(root.gameObject);
        ApplyImageStyle(image, recipe.yellowButtonSprite, recipe.buttonYellow, true);

        Button button = Undo.AddComponent<Button>(root.gameObject);
        button.targetGraphic = image;
        ApplyButtonColorBlock(button);

        RectTransform iconSlot = CreateRect(root, "IconSlot", undoName);
        SetAnchoredBox(
            iconSlot,
            new Vector2(0f, 0.5f),
            new Vector2(0f, 0.5f),
            new Vector2(0f, 0.5f),
            new Vector2(24f, 0f),
            new Vector2(34f, 34f));

        TextMeshProUGUI label = CreateText(root, "Label", string.IsNullOrWhiteSpace(menuButtonLabel) ? "Button" : menuButtonLabel, recipe.menuTextSize, TextAlignmentOptions.Center, undoName);
        RectTransform labelRect = label.rectTransform;
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.pivot = new Vector2(0.5f, 0.5f);
        labelRect.offsetMin = new Vector2(58f, 0f);
        labelRect.offsetMax = new Vector2(-18f, 0f);

        SelectAndMark(root, undoGroup);
    }

    private void CreatePaperCard()
    {
        RectTransform parent = ResolveSelectedParent();
        if (parent == null || !ValidateRecipe())
            return;

        const string undoName = "Create Project ONE Paper Card";
        int undoGroup = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName(undoName);

        RectTransform root = CreatePaperCardRoot(parent, "PaperCard_ProjectOne", recipe.resultCardSize, undoName);
        SelectAndMark(root, undoGroup);
    }

    private void CreateLoadingCard()
    {
        RectTransform parent = ResolveSelectedParent();
        if (parent == null || !ValidateRecipe())
            return;

        const string undoName = "Create Project ONE Loading Card";
        int undoGroup = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName(undoName);

        RectTransform root = CreatePaperCardRoot(parent, "LoadingCard_ProjectOne", recipe.loadingCardSize, undoName);
        RectTransform contentRoot = FindDirectChild(root, ContentRootName) as RectTransform;
        if (contentRoot != null)
        {
            TextMeshProUGUI title = CreateText(contentRoot, "Title", loadingTitle, recipe.titleTextSize, TextAlignmentOptions.Center, undoName);
            SetAnchoredBox(
                title.rectTransform,
                new Vector2(0f, 1f),
                new Vector2(1f, 1f),
                new Vector2(0.5f, 1f),
                new Vector2(0f, -8f),
                new Vector2(0f, 86f));

            TextMeshProUGUI body = CreateText(contentRoot, "Body", loadingBody, recipe.bodyTextSize, TextAlignmentOptions.Center, undoName);
            body.textWrappingMode = TextWrappingModes.Normal;
            SetAnchoredBox(
                body.rectTransform,
                new Vector2(0f, 0.5f),
                new Vector2(1f, 0.5f),
                new Vector2(0.5f, 0.5f),
                new Vector2(0f, 18f),
                new Vector2(0f, 74f));

            RectTransform tipCard = CreateRect(contentRoot, "TipCard", undoName);
            SetAnchoredBox(
                tipCard,
                new Vector2(0f, 0f),
                new Vector2(1f, 0f),
                new Vector2(0.5f, 0f),
                Vector2.zero,
                new Vector2(0f, TipCardHeight));

            Image tipImage = Undo.AddComponent<Image>(tipCard.gameObject);
            ApplyImageStyle(tipImage, recipe.paperButtonSprite, recipe.mint, false);
        }

        SelectAndMark(root, undoGroup);
    }

    private void ApplyTextStyleToSelected()
    {
        if (!ValidateRecipe())
            return;

        TMP_Text[] targets = GetSelectedComponents<TMP_Text>();
        if (targets.Length == 0)
        {
            Debug.LogWarning("[ProjectOneReferenceUIBuilder] 선택된 TMP_Text가 없습니다.");
            return;
        }

        TryAddFallbackFont();

        const string undoName = "Apply Project ONE Text Style";
        int undoGroup = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName(undoName);

        foreach (TMP_Text target in targets)
            ApplyTextStyle(target, ResolveTextSize(target), undoName);

        Undo.CollapseUndoOperations(undoGroup);
        MarkSelectedScenesDirty();
        Debug.Log($"[ProjectOneReferenceUIBuilder] Text style 적용 완료: {targets.Length}개");
    }

    private void ApplyButtonStyleToSelected()
    {
        if (!ValidateRecipe())
            return;

        GameObject[] selectedObjects = Selection.gameObjects;
        if (selectedObjects == null || selectedObjects.Length == 0)
        {
            Debug.LogWarning("[ProjectOneReferenceUIBuilder] 선택된 GameObject가 없습니다.");
            return;
        }

        const string undoName = "Apply Project ONE Button Style";
        int undoGroup = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName(undoName);

        int changed = 0;
        foreach (GameObject selected in selectedObjects)
        {
            if (!CanEditSceneObject(selected))
                continue;

            RectTransform rect = selected.GetComponent<RectTransform>();
            if (rect != null)
            {
                Undo.RecordObject(rect, undoName);
                rect.sizeDelta = recipe.menuButtonSize;
                EditorUtility.SetDirty(rect);
            }

            Image image = selected.GetComponent<Image>();
            Button button = selected.GetComponent<Button>();
            if (image == null && button != null)
                image = Undo.AddComponent<Image>(selected);

            if (image != null)
            {
                Undo.RecordObject(image, undoName);
                ApplyImageStyle(image, recipe.yellowButtonSprite, recipe.buttonYellow, true);
                EditorUtility.SetDirty(image);
            }

            if (button != null && image != null)
            {
                Undo.RecordObject(button, undoName);
                button.targetGraphic = image;
                ApplyButtonColorBlock(button);
                EditorUtility.SetDirty(button);
            }

            TMP_Text[] labels = selected.GetComponentsInChildren<TMP_Text>(true);
            for (int i = 0; i < labels.Length; i++)
                ApplyTextStyle(labels[i], recipe.menuTextSize, undoName);

            changed++;
        }

        Undo.CollapseUndoOperations(undoGroup);
        MarkSelectedScenesDirty();
        Debug.Log($"[ProjectOneReferenceUIBuilder] Button style 적용 완료: {changed}개. 기존 Button.onClick은 변경하지 않았습니다.");
    }

    private void ApplyCardStyleToSelected()
    {
        if (!ValidateRecipe())
            return;

        GameObject[] selectedObjects = Selection.gameObjects;
        if (selectedObjects == null || selectedObjects.Length == 0)
        {
            Debug.LogWarning("[ProjectOneReferenceUIBuilder] 선택된 GameObject가 없습니다.");
            return;
        }

        const string undoName = "Apply Project ONE Card Style";
        int undoGroup = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName(undoName);

        int changed = 0;
        foreach (GameObject selected in selectedObjects)
        {
            if (!CanEditSceneObject(selected))
                continue;

            RectTransform rect = selected.GetComponent<RectTransform>();
            if (rect == null)
            {
                Debug.LogWarning($"[ProjectOneReferenceUIBuilder] RectTransform이 없어 Card style을 스킵합니다: {selected.name}", selected);
                continue;
            }

            Undo.RecordObject(rect, undoName);
            rect.sizeDelta = recipe.resultCardSize;
            EditorUtility.SetDirty(rect);

            Image backFill = GetDirectChildImage(selected.transform, BackFillName);
            Image paperOverlay = GetDirectChildImage(selected.transform, PaperOverlayName);
            if (backFill != null || paperOverlay != null)
            {
                if (backFill != null)
                {
                    Undo.RecordObject(backFill, undoName);
                    ApplyImageStyle(backFill, null, recipe.paperCream, false);
                    ApplyShadow(backFill.gameObject, undoName);
                    EditorUtility.SetDirty(backFill);
                }

                if (paperOverlay != null)
                {
                    Undo.RecordObject(paperOverlay, undoName);
                    ApplyImageStyle(paperOverlay, recipe.paperPanelSprite, recipe.paperWarm, false);
                    EditorUtility.SetDirty(paperOverlay);
                }
            }
            else
            {
                Image rootImage = selected.GetComponent<Image>();
                if (rootImage == null)
                    rootImage = Undo.AddComponent<Image>(selected);

                Undo.RecordObject(rootImage, undoName);
                ApplyImageStyle(rootImage, recipe.paperPanelSprite, recipe.paperWarm, rootImage.raycastTarget);
                ApplyShadow(selected, undoName);
                EditorUtility.SetDirty(rootImage);
            }

            changed++;
        }

        Undo.CollapseUndoOperations(undoGroup);
        MarkSelectedScenesDirty();
        Debug.Log($"[ProjectOneReferenceUIBuilder] Card style 적용 완료: {changed}개. 기존 자식 Text/Icon은 유지했습니다.");
    }

    private RectTransform CreatePaperCardRoot(RectTransform parent, string rootName, Vector2 size, string undoName)
    {
        RectTransform root = CreateRect(parent, rootName, undoName);
        SetCenteredBox(root, size);

        RectTransform backFill = CreateRect(root, BackFillName, undoName);
        Stretch(backFill, 0f);
        Image backFillImage = Undo.AddComponent<Image>(backFill.gameObject);
        ApplyImageStyle(backFillImage, null, recipe.paperCream, false);
        ApplyShadow(backFill.gameObject, undoName);

        RectTransform paperOverlay = CreateRect(root, PaperOverlayName, undoName);
        Stretch(paperOverlay, 0f);
        Image paperOverlayImage = Undo.AddComponent<Image>(paperOverlay.gameObject);
        ApplyImageStyle(paperOverlayImage, recipe.paperPanelSprite, recipe.paperWarm, false);

        RectTransform contentRoot = CreateRect(root, ContentRootName, undoName);
        Stretch(contentRoot, CardHorizontalPadding, CardVerticalPadding);

        backFill.SetSiblingIndex(0);
        paperOverlay.SetSiblingIndex(1);
        contentRoot.SetSiblingIndex(2);

        return root;
    }

    private TextMeshProUGUI CreateText(RectTransform parent, string name, string value, int fontSize, TextAlignmentOptions alignment, string undoName)
    {
        RectTransform rect = CreateRect(parent, name, undoName);
        TextMeshProUGUI text = Undo.AddComponent<TextMeshProUGUI>(rect.gameObject);
        text.text = value;
        text.alignment = alignment;
        text.fontStyle = FontStyles.Bold;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.overflowMode = TextOverflowModes.Ellipsis;
        text.raycastTarget = false;
        ApplyTextStyle(text, fontSize, undoName);
        return text;
    }

    private RectTransform CreateRect(RectTransform parent, string name, string undoName)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        RectTransform rect = go.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.localScale = Vector3.one;
        rect.localRotation = Quaternion.identity;
        rect.anchoredPosition = Vector2.zero;
        Undo.RegisterCreatedObjectUndo(go, undoName);
        return rect;
    }

    private void ApplyTextStyle(TMP_Text target, int fontSize, string undoName)
    {
        if (target == null)
            return;

        Undo.RecordObject(target, undoName);

        if (recipe.defaultFont != null)
            target.font = recipe.defaultFont;

        target.color = recipe.mainNavy;
        target.fontSize = fontSize;
        target.enableAutoSizing = false;
        target.extraPadding = true;
        target.richText = true;
        target.raycastTarget = false;

        EditorUtility.SetDirty(target);
    }

    private void ApplyImageStyle(Image image, Sprite sprite, Color color, bool raycastTarget)
    {
        if (image == null)
            return;

        image.sprite = sprite;
        image.overrideSprite = null;
        image.color = color;
        image.raycastTarget = raycastTarget;
        image.type = sprite != null && HasBorder(sprite) ? Image.Type.Sliced : Image.Type.Simple;
        image.preserveAspect = false;
    }

    private void ApplyButtonColorBlock(Button button)
    {
        if (button == null)
            return;

        Color normal = recipe != null ? recipe.buttonYellow : Color.white;
        Color highlighted = Color.Lerp(normal, Color.white, 0.18f);
        Color pressed = Color.Lerp(normal, new Color(0.74f, 0.55f, 0.08f, normal.a), 0.35f);

        ColorBlock colors = button.colors;
        colors.normalColor = normal;
        colors.highlightedColor = highlighted;
        colors.pressedColor = pressed;
        colors.selectedColor = normal;
        colors.disabledColor = new Color(normal.r, normal.g, normal.b, 0.55f);
        colors.colorMultiplier = 1f;
        colors.fadeDuration = 0.08f;
        button.colors = colors;
    }

    private void ApplyShadow(GameObject target, string undoName)
    {
        if (target == null)
            return;

        Shadow shadow = target.GetComponent<Shadow>();
        if (shadow == null)
            shadow = Undo.AddComponent<Shadow>(target);
        else
            Undo.RecordObject(shadow, undoName);

        shadow.effectColor = new Color(0f, 0f, 0f, Mathf.Clamp01(recipe.cardShadowAlpha));
        shadow.effectDistance = recipe.cardShadowOffset;
        shadow.useGraphicAlpha = true;
        EditorUtility.SetDirty(shadow);
    }

    private RectTransform ResolveSelectedParent()
    {
        GameObject selected = Selection.activeGameObject;
        if (!CanEditSceneObject(selected))
            return null;

        RectTransform rect = selected.GetComponent<RectTransform>();
        if (rect == null)
        {
            Debug.LogWarning("[ProjectOneReferenceUIBuilder] 선택한 GameObject에 RectTransform이 없습니다.", selected);
            return null;
        }

        return rect;
    }

    private bool ValidateRecipe()
    {
        if (recipe != null)
            return true;

        Debug.LogWarning("[ProjectOneReferenceUIBuilder] ProjectOneUIRecipe를 먼저 할당하세요.");
        return false;
    }

    private static bool CanEditSceneObject(GameObject selected)
    {
        if (selected == null)
        {
            Debug.LogWarning("[ProjectOneReferenceUIBuilder] 선택된 GameObject가 없습니다.");
            return false;
        }

        if (EditorUtility.IsPersistent(selected))
        {
            Debug.LogWarning($"[ProjectOneReferenceUIBuilder] Project asset은 직접 수정하지 않습니다: {selected.name}", selected);
            return false;
        }

        return true;
    }

    private void TryAddFallbackFont()
    {
        if (recipe.defaultFont == null || recipe.fallbackFont == null || recipe.defaultFont == recipe.fallbackFont)
            return;

        List<TMP_FontAsset> fallbackTable = recipe.defaultFont.fallbackFontAssetTable;
        if (fallbackTable == null)
            return;

        if (fallbackTable.Contains(recipe.fallbackFont))
            return;

        Undo.RecordObject(recipe.defaultFont, "Add Project ONE TMP Fallback Font");
        fallbackTable.Add(recipe.fallbackFont);
        EditorUtility.SetDirty(recipe.defaultFont);
        Debug.Log("[ProjectOneReferenceUIBuilder] fallbackFont를 defaultFont fallback table에 추가했습니다.", recipe.defaultFont);
    }

    private int ResolveTextSize(TMP_Text target)
    {
        string objectName = target != null ? target.name.ToLowerInvariant() : string.Empty;
        if (objectName.Contains("title"))
            return recipe.titleTextSize;

        if (objectName.Contains("body") || objectName.Contains("tip") || objectName.Contains("desc") || objectName.Contains("message"))
            return recipe.bodyTextSize;

        return recipe.menuTextSize;
    }

    private T[] GetSelectedComponents<T>() where T : Component
    {
        GameObject[] selectedObjects = Selection.gameObjects;
        if (selectedObjects == null || selectedObjects.Length == 0)
            return new T[0];

        List<T> components = new List<T>();
        for (int i = 0; i < selectedObjects.Length; i++)
        {
            GameObject selected = selectedObjects[i];
            if (!CanEditSceneObject(selected))
                continue;

            T component = selected.GetComponent<T>();
            if (component != null)
                components.Add(component);
        }

        return components.ToArray();
    }

    private void SelectAndMark(RectTransform root, int undoGroup)
    {
        Selection.activeGameObject = root.gameObject;
        EditorSceneManager.MarkSceneDirty(root.gameObject.scene);
        Undo.CollapseUndoOperations(undoGroup);
    }

    private static void MarkSelectedScenesDirty()
    {
        GameObject[] selectedObjects = Selection.gameObjects;
        if (selectedObjects == null)
            return;

        for (int i = 0; i < selectedObjects.Length; i++)
        {
            GameObject selected = selectedObjects[i];
            if (selected != null && !EditorUtility.IsPersistent(selected))
                EditorSceneManager.MarkSceneDirty(selected.scene);
        }
    }

    private ProjectOneUIRecipe FindFirstRecipe()
    {
        string[] guids = AssetDatabase.FindAssets("t:ProjectOneUIRecipe");
        if (guids == null || guids.Length == 0)
            return null;

        string path = AssetDatabase.GUIDToAssetPath(guids[0]);
        return AssetDatabase.LoadAssetAtPath<ProjectOneUIRecipe>(path);
    }

    private ProjectOneUIRecipe CreateRecipeAsset()
    {
        EnsureFolder(DefaultRecipeFolder);

        ProjectOneUIRecipe newRecipe = CreateInstance<ProjectOneUIRecipe>();
        string path = AssetDatabase.GenerateUniqueAssetPath(DefaultRecipePath);
        AssetDatabase.CreateAsset(newRecipe, path);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Selection.activeObject = newRecipe;
        Debug.Log("[ProjectOneReferenceUIBuilder] ProjectOneUIRecipe asset 생성: " + path, newRecipe);
        return newRecipe;
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

    private static bool HasBorder(Sprite sprite)
    {
        if (sprite == null)
            return false;

        Vector4 border = sprite.border;
        return border.x > 0f || border.y > 0f || border.z > 0f || border.w > 0f;
    }

    private static void SetCenteredBox(RectTransform rect, Vector2 size)
    {
        SetAnchoredBox(
            rect,
            new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f),
            Vector2.zero,
            size);
    }

    private static void SetAnchoredBox(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 anchoredPosition, Vector2 size)
    {
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.pivot = pivot;
        rect.anchoredPosition = anchoredPosition;
        rect.sizeDelta = size;
        rect.localScale = Vector3.one;
    }

    private static void Stretch(RectTransform rect, float inset)
    {
        Stretch(rect, inset, inset);
    }

    private static void Stretch(RectTransform rect, float horizontalInset, float verticalInset)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.offsetMin = new Vector2(horizontalInset, verticalInset);
        rect.offsetMax = new Vector2(-horizontalInset, -verticalInset);
        rect.localScale = Vector3.one;
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
