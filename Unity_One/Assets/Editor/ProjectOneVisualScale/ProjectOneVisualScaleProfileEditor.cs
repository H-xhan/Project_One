using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(ProjectOneVisualScaleProfileSO))]
public sealed class ProjectOneVisualScaleProfileEditor : Editor
{
    private const string MenuPath =
        "Tools/Triad Canvas/Project One/Visual Scale Profile";

    private readonly Dictionary<int, bool> itemFoldouts =
        new Dictionary<int, bool>();

    private SerializedProperty productionPlayerPrefab;
    private SerializedProperty characterVisualTransformPath;
    private SerializedProperty presenterSerializedPropertyName;
    private SerializedProperty worldPresenterSerializedPropertyName;
    private SerializedProperty itemRules;
    private SerializedProperty characterVisualScale;
    private SerializedProperty bodyPostItRatioToCharacter;
    private SerializedProperty worldPostItRatioToCharacter;
    private SerializedProperty presetA;
    private SerializedProperty presetB;
    private SerializedProperty presetC;

    private bool showBindings = true;
    private bool showPresetDefinitions;
    private bool showBaseline;
    private ValidationReport lastValidation;
    private ChangePlan lastPlan;
    private ApplyResult lastResult;

    private void OnEnable()
    {
        productionPlayerPrefab =
            serializedObject.FindProperty("productionPlayerPrefab");
        characterVisualTransformPath =
            serializedObject.FindProperty("characterVisualTransformPath");
        presenterSerializedPropertyName =
            serializedObject.FindProperty(
                "presenterSerializedPropertyName");
        worldPresenterSerializedPropertyName =
            serializedObject.FindProperty(
                "worldPresenterSerializedPropertyName");
        itemRules = serializedObject.FindProperty("itemRules");
        characterVisualScale =
            serializedObject.FindProperty("characterVisualScale");
        bodyPostItRatioToCharacter =
            serializedObject.FindProperty(
                "bodyPostItRatioToCharacter");
        worldPostItRatioToCharacter =
            serializedObject.FindProperty(
                "worldPostItRatioToCharacter");
        presetA = serializedObject.FindProperty("presetA");
        presetB = serializedObject.FindProperty("presetB");
        presetC = serializedObject.FindProperty("presetC");
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        ProjectOneVisualScaleProfileSO profile =
            (ProjectOneVisualScaleProfileSO)target;

        DrawHeader(profile);
        EditorGUILayout.Space(4f);
        DrawPresetButtons(profile);
        EditorGUILayout.Space(4f);
        DrawMasterScale();
        EditorGUILayout.Space(4f);
        DrawPostItScale();
        EditorGUILayout.Space(4f);
        DrawItemRules();
        EditorGUILayout.Space(4f);
        DrawBindings();
        EditorGUILayout.Space(4f);
        DrawPresetDefinitions();
        EditorGUILayout.Space(4f);
        DrawBaseline(profile);

        if (serializedObject.ApplyModifiedProperties())
        {
            EditorUtility.SetDirty(profile);
        }

        EditorGUILayout.Space(8f);
        DrawOperations(profile);
        EditorGUILayout.Space(8f);
        DrawLastReport();
    }

    [MenuItem(MenuPath)]
    private static void SelectDefaultProfile()
    {
        ProjectOneVisualScaleProfileSO profile =
            AssetDatabase.LoadAssetAtPath<
                ProjectOneVisualScaleProfileSO>(
                ProjectOneVisualScaleApplier.DefaultProfileAssetPath);
        if (profile == null)
        {
            Debug.LogError(
                "Visual Scale Profile was not found at " +
                ProjectOneVisualScaleApplier.DefaultProfileAssetPath +
                ". The menu does not create assets automatically.");
            return;
        }

        Selection.activeObject = profile;
        EditorGUIUtility.PingObject(profile);
    }

    private static void DrawHeader(
        ProjectOneVisualScaleProfileSO profile)
    {
        EditorGUILayout.LabelField(
            "Project One Visual Scale Profile",
            EditorStyles.boldLabel);
        using (new EditorGUI.DisabledScope(true))
        {
            EditorGUILayout.Toggle(
                "Baseline Captured",
                profile.BaselineCaptured);
            EditorGUILayout.TextField(
                "Baseline Source HEAD",
                profile.BaselineSourceHead ?? string.Empty);
            EditorGUILayout.TextField(
                "Production Player",
                profile.ProductionPlayerPrefab != null
                    ? AssetDatabase.GetAssetPath(
                        profile.ProductionPlayerPrefab)
                    : "<unbound>");
            EditorGUILayout.IntField(
                "Item Rule Count",
                profile.ItemRules != null
                    ? profile.ItemRules.Count
                    : 0);
        }
    }

    private void DrawPresetButtons(
        ProjectOneVisualScaleProfileSO profile)
    {
        EditorGUILayout.LabelField("Presets", EditorStyles.boldLabel);
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("A Current V2"))
            {
                ApplyPreset(profile, profile.PresetA);
            }

            if (GUILayout.Button("B Large"))
            {
                ApplyPreset(profile, profile.PresetB);
            }

            if (GUILayout.Button("C Extra Large"))
            {
                ApplyPreset(profile, profile.PresetC);
            }
        }

        EditorGUILayout.HelpBox(
            "Preset buttons only update this Profile asset. " +
            "Production Prefabs remain unchanged until Apply All.",
            MessageType.Info);
    }

    private void ApplyPreset(
        ProjectOneVisualScaleProfileSO profile,
        VisualScalePreset preset)
    {
        if (preset == null)
        {
            lastValidation = new ValidationReport();
            lastValidation.AddError("The selected preset is null.");
            return;
        }

        Undo.RecordObject(profile, "Apply Visual Scale Preset");
        serializedObject.Update();
        characterVisualScale.floatValue =
            preset.CharacterVisualScale;
        bodyPostItRatioToCharacter.floatValue =
            preset.BodyPostItRatioToCharacter;
        worldPostItRatioToCharacter.floatValue =
            preset.WorldPostItRatioToCharacter;
        ProjectOneVisualScaleApplier.ApplyPresetItemsToSerializedProfile(
            serializedObject,
            preset);
        serializedObject.ApplyModifiedProperties();
        EditorUtility.SetDirty(profile);
        serializedObject.Update();
    }

    private void DrawMasterScale()
    {
        EditorGUILayout.LabelField(
            "Master Visual Scale",
            EditorStyles.boldLabel);
        characterVisualScale.floatValue = EditorGUILayout.Slider(
            "Character Visual Scale",
            characterVisualScale.floatValue,
            0.75f,
            2f);
        EditorGUILayout.HelpBox(
            "Authoring changes are stored only in the Profile. " +
            "They have not been applied to Production assets.",
            MessageType.None);
    }

    private void DrawPostItScale()
    {
        EditorGUILayout.LabelField(
            "Post-it Relative Scale",
            EditorStyles.boldLabel);
        bodyPostItRatioToCharacter.floatValue =
            EditorGUILayout.FloatField(
                "Body Ratio To Character",
                bodyPostItRatioToCharacter.floatValue);
        worldPostItRatioToCharacter.floatValue =
            EditorGUILayout.FloatField(
                "World Ratio To Character",
                worldPostItRatioToCharacter.floatValue);

        float character = characterVisualScale.floatValue;
        using (new EditorGUI.DisabledScope(true))
        {
            EditorGUILayout.FloatField(
                "Body Final",
                character *
                bodyPostItRatioToCharacter.floatValue);
            EditorGUILayout.FloatField(
                "World Final",
                character *
                worldPostItRatioToCharacter.floatValue);
        }
    }

    private void DrawItemRules()
    {
        EditorGUILayout.LabelField("Item Rules", EditorStyles.boldLabel);
        if (itemRules == null)
        {
            EditorGUILayout.HelpBox(
                "itemRules property is missing.",
                MessageType.Error);
            return;
        }

        for (int index = 0; index < itemRules.arraySize; index++)
        {
            SerializedProperty rule =
                itemRules.GetArrayElementAtIndex(index);
            SerializedProperty displayName =
                rule.FindPropertyRelative("displayName");
            SerializedProperty ruleId =
                rule.FindPropertyRelative("ruleId");
            string title = string.IsNullOrWhiteSpace(
                    displayName.stringValue)
                ? ruleId.stringValue
                : displayName.stringValue;
            if (string.IsNullOrWhiteSpace(title))
            {
                title = "Item Rule " + index;
            }

            bool expanded;
            itemFoldouts.TryGetValue(index, out expanded);
            expanded = EditorGUILayout.Foldout(
                expanded,
                title,
                true);
            itemFoldouts[index] = expanded;
            if (!expanded)
            {
                continue;
            }

            using (new EditorGUI.IndentLevelScope())
            {
                DrawItemRule(rule);
            }
        }
    }

    private void DrawItemRule(SerializedProperty rule)
    {
        SerializedProperty enabled =
            rule.FindPropertyRelative("enabled");
        SerializedProperty ruleId =
            rule.FindPropertyRelative("ruleId");
        SerializedProperty displayName =
            rule.FindPropertyRelative("displayName");
        SerializedProperty worldPrefab =
            rule.FindPropertyRelative("worldPrefab");
        SerializedProperty worldPath =
            rule.FindPropertyRelative("worldVisualTransformPath");
        SerializedProperty equippedPrefab =
            rule.FindPropertyRelative("equippedPrefab");
        SerializedProperty equippedPath =
            rule.FindPropertyRelative("equippedVisualTransformPath");
        SerializedProperty mode =
            rule.FindPropertyRelative("mode");
        SerializedProperty ratio =
            rule.FindPropertyRelative("ratioToCharacter");
        SerializedProperty fixedFinal =
            rule.FindPropertyRelative("fixedFinalScale");
        SerializedProperty maximum =
            rule.FindPropertyRelative("maximumFinalScale");
        SerializedProperty referenceWorld =
            rule.FindPropertyRelative("referenceWorldVisualLocalScale");
        SerializedProperty referenceEquipped =
            rule.FindPropertyRelative(
                "referenceEquippedVisualLocalScale");

        EditorGUILayout.PropertyField(enabled, new GUIContent("Enabled"));
        EditorGUILayout.PropertyField(ruleId, new GUIContent("Rule ID"));
        EditorGUILayout.PropertyField(
            displayName,
            new GUIContent("Display Name"));
        EditorGUILayout.PropertyField(
            worldPrefab,
            new GUIContent("World Prefab"));
        EditorGUILayout.PropertyField(
            worldPath,
            new GUIContent("World Transform Path"));
        EditorGUILayout.PropertyField(
            equippedPrefab,
            new GUIContent("Equipped Prefab"));
        EditorGUILayout.PropertyField(
            equippedPath,
            new GUIContent("Equipped Transform Path"));
        EditorGUILayout.PropertyField(mode, new GUIContent("Mode"));
        EditorGUILayout.PropertyField(
            ratio,
            new GUIContent("Ratio To Character"));
        EditorGUILayout.PropertyField(
            fixedFinal,
            new GUIContent("Fixed Final Scale"));
        EditorGUILayout.PropertyField(
            maximum,
            new GUIContent("Max Final Scale"));

        float character = characterVisualScale.floatValue;
        float rawFinal =
            mode.enumValueIndex ==
            (int)ItemScaleMode.RelativeToCharacter
            ? character * ratio.floatValue
            : fixedFinal.floatValue;
        bool clamped =
            maximum.floatValue > 0f &&
            rawFinal > maximum.floatValue;
        float final = clamped ? maximum.floatValue : rawFinal;
        Vector3 worldTarget =
            referenceWorld.vector3Value * final;
        Vector3 equippedTarget =
            character > 0f
            ? referenceEquipped.vector3Value * (final / character)
            : new Vector3(float.NaN, float.NaN, float.NaN);

        using (new EditorGUI.DisabledScope(true))
        {
            EditorGUILayout.FloatField("Calculated Raw Final", rawFinal);
            EditorGUILayout.FloatField(
                clamped
                    ? "Calculated Clamped Final"
                    : "Calculated Final",
                final);
            EditorGUILayout.Vector3Field(
                "World Target Local Scale",
                worldTarget);
            EditorGUILayout.Vector3Field(
                "Equipped Target Local Scale",
                equippedTarget);
        }

        if (clamped)
        {
            EditorGUILayout.HelpBox(
                "The item final ratio is clamped by Max Final Scale.",
                MessageType.Warning);
        }

        EditorGUILayout.HelpBox(
            "Binding and path fields are advanced references. " +
            "Validate Bindings before Apply.",
            MessageType.Warning);
    }

    private void DrawBindings()
    {
        showBindings = EditorGUILayout.Foldout(
            showBindings,
            "Production Bindings",
            true);
        if (!showBindings)
        {
            return;
        }

        using (new EditorGUI.IndentLevelScope())
        {
            EditorGUILayout.PropertyField(
                productionPlayerPrefab,
                new GUIContent("Production Player Prefab"));
            EditorGUILayout.PropertyField(
                characterVisualTransformPath,
                new GUIContent("Character Transform Path"));
            EditorGUILayout.PropertyField(
                presenterSerializedPropertyName,
                new GUIContent("Body Presenter Property"));
            EditorGUILayout.PropertyField(
                worldPresenterSerializedPropertyName,
                new GUIContent("World Presenter Property"));
        }

        EditorGUILayout.HelpBox(
            "Changing a binding can redirect destructive Prefab writes. " +
            "The service fails closed unless the approved exact bindings " +
            "validate.",
            MessageType.Warning);
    }

    private void DrawPresetDefinitions()
    {
        showPresetDefinitions = EditorGUILayout.Foldout(
            showPresetDefinitions,
            "Preset Definitions",
            true);
        if (!showPresetDefinitions)
        {
            return;
        }

        using (new EditorGUI.IndentLevelScope())
        {
            EditorGUILayout.PropertyField(presetA, true);
            EditorGUILayout.PropertyField(presetB, true);
            EditorGUILayout.PropertyField(presetC, true);
        }
    }

    private void DrawBaseline(
        ProjectOneVisualScaleProfileSO profile)
    {
        showBaseline = EditorGUILayout.Foldout(
            showBaseline,
            "Captured V2 Baseline (Read-only)",
            true);
        if (!showBaseline)
        {
            return;
        }

        using (new EditorGUI.DisabledScope(true))
        using (new EditorGUI.IndentLevelScope())
        {
            EditorGUILayout.Vector3Field(
                "Character Reference",
                profile.ReferenceCharacterVisualLocalScale);
            EditorGUILayout.FloatField(
                "Body Reference",
                profile.ReferenceBodyPostItMultiplier);
            EditorGUILayout.FloatField(
                "World Reference",
                profile.ReferenceWorldPostItMultiplier);
            EditorGUILayout.Vector3Field(
                "Captured Character",
                profile.CapturedCharacterVisualLocalScale);
            EditorGUILayout.FloatField(
                "Captured Body",
                profile.CapturedBodyPostItMultiplier);
            EditorGUILayout.FloatField(
                "Captured World",
                profile.CapturedWorldPostItMultiplier);

            if (profile.ItemRules != null)
            {
                for (int index = 0;
                    index < profile.ItemRules.Count;
                    index++)
                {
                    ItemScaleRule rule = profile.ItemRules[index];
                    if (rule == null)
                    {
                        continue;
                    }

                    EditorGUILayout.LabelField(
                        rule.RuleId,
                        EditorStyles.miniBoldLabel);
                    EditorGUILayout.Vector3Field(
                        "World Reference",
                        rule.ReferenceWorldVisualLocalScale);
                    EditorGUILayout.Vector3Field(
                        "Equipped Reference",
                        rule.ReferenceEquippedVisualLocalScale);
                    EditorGUILayout.Vector3Field(
                        "Captured World",
                        rule.CapturedWorldVisualLocalScale);
                    EditorGUILayout.Vector3Field(
                        "Captured Equipped",
                        rule.CapturedEquippedVisualLocalScale);
                }
            }
        }

        EditorGUILayout.HelpBox(
            "Baseline recapture is intentionally unavailable. Restore " +
            "always returns to the captured b0fa1843 V2 values.",
            MessageType.Info);
    }

    private void DrawOperations(
        ProjectOneVisualScaleProfileSO profile)
    {
        EditorGUILayout.LabelField("Operations", EditorStyles.boldLabel);
        if (GUILayout.Button("Preview Changes"))
        {
            lastPlan =
                ProjectOneVisualScaleApplier.BuildPreview(profile);
            lastValidation = lastPlan.Validation;
            lastResult = null;
        }

        if (GUILayout.Button("Validate Bindings"))
        {
            lastValidation =
                ProjectOneVisualScaleApplier.Validate(profile);
            lastPlan = null;
            lastResult = null;
        }

        Color previousColor = GUI.backgroundColor;
        GUI.backgroundColor = new Color(1f, 0.72f, 0.25f);
        if (GUILayout.Button("Apply All Visual Scales"))
        {
            ChangePlan plan =
                ProjectOneVisualScaleApplier.BuildPreview(profile);
            lastPlan = plan;
            lastValidation = plan.Validation;
            lastResult = null;
            if (!plan.Validation.HasErrors &&
                EditorUtility.DisplayDialog(
                    "Apply All Visual Scales",
                    BuildConfirmationMessage(plan, false),
                    "Apply All",
                    "Cancel"))
            {
                lastResult =
                    ProjectOneVisualScaleApplier.ApplyAll(profile);
                lastValidation = lastResult.Validation;
                lastPlan = lastResult.Plan;
                ShowResultDialog(lastResult);
            }
        }

        GUI.backgroundColor = new Color(1f, 0.48f, 0.48f);
        if (GUILayout.Button("Restore Captured V2 Baseline"))
        {
            ChangePlan plan =
                ProjectOneVisualScaleApplier.BuildRestorePreview(profile);
            lastPlan = plan;
            lastValidation = plan.Validation;
            lastResult = null;
            if (!plan.Validation.HasErrors &&
                EditorUtility.DisplayDialog(
                    "Restore Captured V2 Baseline",
                    BuildConfirmationMessage(plan, true),
                    "Restore V2 Baseline",
                    "Cancel"))
            {
                lastResult =
                    ProjectOneVisualScaleApplier.RestoreBaseline(profile);
                lastValidation = lastResult.Validation;
                lastPlan = lastResult.Plan;
                serializedObject.Update();
                ShowResultDialog(lastResult);
            }
        }

        GUI.backgroundColor = previousColor;
    }

    private static string BuildConfirmationMessage(
        ChangePlan plan,
        bool restore)
    {
        StringBuilder builder = new StringBuilder();
        builder.AppendLine(
            restore
                ? "Restore the captured b0fa1843 V2 values?"
                : "Apply the Profile values to Production Prefabs?");
        builder.AppendLine();

        int displayed = 0;
        for (int index = 0;
            index < plan.Changes.Count;
            index++)
        {
            VisualScaleChange change = plan.Changes[index];
            if (!change.Changed)
            {
                continue;
            }

            builder.Append("- ")
                .Append(change.Category)
                .Append(": ")
                .Append(change.CurrentValue)
                .Append(" -> ")
                .Append(change.TargetValue)
                .AppendLine();
            displayed++;
            if (displayed >= 12)
            {
                builder.AppendLine("- ...");
                break;
            }
        }

        if (displayed == 0)
        {
            builder.AppendLine("- Target Prefab values: No Change");
        }

        builder.AppendLine();
        builder.AppendLine(
            "A raw-byte transaction backup is created before writing.");
        return builder.ToString();
    }

    private static void ShowResultDialog(ApplyResult result)
    {
        EditorUtility.DisplayDialog(
            result.Succeeded
                ? "Visual Scale Operation Complete"
                : "Visual Scale Operation Failed",
            result.Message +
            (string.IsNullOrWhiteSpace(result.BackupPath)
                ? string.Empty
                : "\n\nBackup:\n" + result.BackupPath),
            "OK");
    }

    private void DrawLastReport()
    {
        if (lastValidation == null &&
            lastPlan == null &&
            lastResult == null)
        {
            return;
        }

        EditorGUILayout.LabelField(
            "Last Report",
            EditorStyles.boldLabel);
        if (lastValidation != null)
        {
            for (int index = 0;
                index < lastValidation.Entries.Count;
                index++)
            {
                VisualScaleValidationEntry entry =
                    lastValidation.Entries[index];
                EditorGUILayout.HelpBox(
                    entry.Message,
                    ToMessageType(entry.Severity));
            }
        }

        if (lastPlan != null)
        {
            DrawChangeTable(lastPlan);
        }

        if (lastResult != null)
        {
            EditorGUILayout.HelpBox(
                lastResult.Message,
                lastResult.Succeeded
                    ? MessageType.Info
                    : MessageType.Error);
            if (!string.IsNullOrWhiteSpace(lastResult.BackupPath))
            {
                EditorGUILayout.SelectableLabel(
                    lastResult.BackupPath,
                    EditorStyles.textField,
                    GUILayout.Height(
                        EditorGUIUtility.singleLineHeight));
            }
        }
    }

    private static void DrawChangeTable(ChangePlan plan)
    {
        EditorGUILayout.Space(4f);
        EditorGUILayout.LabelField(
            "Current -> Target Change Table",
            EditorStyles.miniBoldLabel);
        for (int index = 0; index < plan.Changes.Count; index++)
        {
            VisualScaleChange change = plan.Changes[index];
            using (new EditorGUILayout.VerticalScope(
                EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField(
                    change.Category + " / " + change.Status,
                    EditorStyles.miniBoldLabel);
                EditorGUILayout.LabelField(
                    "Asset",
                    change.AssetPath);
                EditorGUILayout.LabelField(
                    "Object",
                    change.ObjectPath);
                EditorGUILayout.LabelField(
                    "Property",
                    change.Property);
                EditorGUILayout.LabelField(
                    "Current",
                    change.CurrentValue);
                EditorGUILayout.LabelField(
                    "Target",
                    change.TargetValue);
                EditorGUILayout.LabelField(
                    "Final Ratio",
                    change.CalculatedFinalRatio.ToString(
                        "0.########",
                        CultureInfo.InvariantCulture));
            }
        }
    }

    private static MessageType ToMessageType(
        VisualScaleReportSeverity severity)
    {
        switch (severity)
        {
            case VisualScaleReportSeverity.Error:
                return MessageType.Error;
            case VisualScaleReportSeverity.Warning:
                return MessageType.Warning;
            default:
                return MessageType.Info;
        }
    }
}
