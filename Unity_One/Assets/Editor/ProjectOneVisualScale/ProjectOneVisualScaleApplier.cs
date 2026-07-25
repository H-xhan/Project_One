using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Unity.Netcode;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

internal enum VisualScaleReportSeverity
{
    Info = 0,
    Warning = 1,
    Error = 2
}

internal sealed class VisualScaleValidationEntry
{
    internal VisualScaleValidationEntry(
        VisualScaleReportSeverity severity,
        string message)
    {
        Severity = severity;
        Message = message;
    }

    internal VisualScaleReportSeverity Severity { get; }
    internal string Message { get; }
}

internal sealed class ValidationReport
{
    private readonly List<VisualScaleValidationEntry> entries =
        new List<VisualScaleValidationEntry>();

    internal IReadOnlyList<VisualScaleValidationEntry> Entries => entries;
    internal bool HasErrors =>
        entries.Any(entry => entry.Severity == VisualScaleReportSeverity.Error);
    internal int ErrorCount =>
        entries.Count(entry =>
            entry.Severity == VisualScaleReportSeverity.Error);
    internal int WarningCount =>
        entries.Count(entry =>
            entry.Severity == VisualScaleReportSeverity.Warning);

    internal void AddInfo(string message)
    {
        entries.Add(new VisualScaleValidationEntry(
            VisualScaleReportSeverity.Info,
            message));
    }

    internal void AddWarning(string message)
    {
        entries.Add(new VisualScaleValidationEntry(
            VisualScaleReportSeverity.Warning,
            message));
    }

    internal void AddError(string message)
    {
        entries.Add(new VisualScaleValidationEntry(
            VisualScaleReportSeverity.Error,
            message));
    }
}

internal enum VisualScaleChangeStatus
{
    NoChange = 0,
    WillChange = 1,
    Clamped = 2,
    Warning = 3,
    Invalid = 4
}

internal sealed class VisualScaleChange
{
    internal VisualScaleChange(
        string category,
        string assetPath,
        string objectPath,
        string property,
        string currentValue,
        string targetValue,
        float calculatedFinalRatio,
        bool clamped,
        bool changed,
        VisualScaleChangeStatus status)
    {
        Category = category;
        AssetPath = assetPath;
        ObjectPath = objectPath;
        Property = property;
        CurrentValue = currentValue;
        TargetValue = targetValue;
        CalculatedFinalRatio = calculatedFinalRatio;
        Clamped = clamped;
        Changed = changed;
        Status = status;
    }

    internal string Category { get; }
    internal string AssetPath { get; }
    internal string ObjectPath { get; }
    internal string Property { get; }
    internal string CurrentValue { get; }
    internal string TargetValue { get; }
    internal float CalculatedFinalRatio { get; }
    internal bool Clamped { get; }
    internal bool Changed { get; }
    internal VisualScaleChangeStatus Status { get; }
}

internal sealed class ChangePlan
{
    private readonly List<VisualScaleChange> changes =
        new List<VisualScaleChange>();

    internal ChangePlan(ValidationReport validation)
    {
        Validation = validation;
    }

    internal ValidationReport Validation { get; }
    internal IReadOnlyList<VisualScaleChange> Changes => changes;
    internal bool HasChanges => changes.Any(change => change.Changed);

    internal void Add(VisualScaleChange change)
    {
        changes.Add(change);
    }
}

internal sealed class ApplyResult
{
    internal ApplyResult(
        bool succeeded,
        string message,
        string backupPath,
        ValidationReport validation,
        ChangePlan plan,
        bool rollbackSucceeded)
    {
        Succeeded = succeeded;
        Message = message;
        BackupPath = backupPath;
        Validation = validation;
        Plan = plan;
        RollbackSucceeded = rollbackSucceeded;
    }

    internal bool Succeeded { get; }
    internal string Message { get; }
    internal string BackupPath { get; }
    internal ValidationReport Validation { get; }
    internal ChangePlan Plan { get; }
    internal bool RollbackSucceeded { get; }
}

internal static class ProjectOneVisualScaleApplier
{
    internal const string ExpectedBaselineSourceHead =
        "b0fa18439c709ec7808a6b0732560356ad20308d";
    internal const string DefaultProfileAssetPath =
        "Assets/Editor/ProjectOneVisualScale/" +
        "ProjectOneVisualScaleProfile.asset";

    private const string ExpectedPlayerPrefabPath =
        "Assets/Prefab/Test/" +
        "Hamster_JointFreeMotorShell_MainScenes.prefab";
    private const string ExpectedPlayerPrefabGuid =
        "bbf5381b5913ae443ac31b9d33ae4a5d";
    private const string ExpectedCharacterPath =
        "MotorShellBody/VisualPreviewRoot/슈가";
    private const string ExpectedBodyPropertyName =
        "bodyVisualScaleMultiplier";
    private const string ExpectedWorldPropertyName =
        "worldVisualScaleMultiplier";
    private const string BaseballBatRuleId = "BaseballBat";
    private const string ExpectedBatWorldPrefabPath =
        "Assets/Prefab/Weapon/BaseballBat.prefab";
    private const string ExpectedBatWorldTransformPath = "WorldVisualScale";
    private const string ExpectedBatEquippedPrefabPath =
        "Assets/Prefab/Weapon/BaseballBat_Equip.prefab";
    private const string ExpectedBatEquippedTransformPath =
        "BaseballBatMesh";
    private const float ValueTolerance = 0.000001f;

    private static readonly Regex InstanceIdRegex = new Regex(
        "\"instanceID\"\\s*:\\s*-?\\d+",
        RegexOptions.Compiled);

    internal static ValidationReport Validate(
        ProjectOneVisualScaleProfileSO profile)
    {
        return Validate(profile, true);
    }

    internal static ChangePlan BuildPreview(
        ProjectOneVisualScaleProfileSO profile)
    {
        ValidationReport validation = Validate(profile, true);
        ChangePlan plan = new ChangePlan(validation);
        if (validation.HasErrors)
        {
            return plan;
        }

        try
        {
            AddPlayerPreview(profile, plan, false);
            AddItemPreview(profile, plan, false);
        }
        catch (Exception exception)
        {
            validation.AddError(
                "Preview failed without writing assets: " +
                exception.Message);
        }

        return plan;
    }

    internal static ChangePlan BuildRestorePreview(
        ProjectOneVisualScaleProfileSO profile)
    {
        ValidationReport validation = Validate(profile, false);
        ChangePlan plan = new ChangePlan(validation);
        if (validation.HasErrors)
        {
            return plan;
        }

        try
        {
            AddPlayerPreview(profile, plan, true);
            AddItemPreview(profile, plan, true);
        }
        catch (Exception exception)
        {
            validation.AddError(
                "Restore preview failed without writing assets: " +
                exception.Message);
        }

        return plan;
    }

    internal static ApplyResult ApplyAll(
        ProjectOneVisualScaleProfileSO profile)
    {
        return ApplyAll(profile, null);
    }

    internal static ApplyResult ApplyAll(
        ProjectOneVisualScaleProfileSO profile,
        Func<bool> additionalVerification)
    {
        return RunTransaction(
            profile,
            false,
            additionalVerification);
    }

    internal static ApplyResult RestoreBaseline(
        ProjectOneVisualScaleProfileSO profile)
    {
        return RunTransaction(profile, true, null);
    }

    private static ValidationReport Validate(
        ProjectOneVisualScaleProfileSO profile,
        bool validateAuthoringValues)
    {
        ValidationReport report = new ValidationReport();
        if (profile == null)
        {
            report.AddError("Profile is null.");
            return report;
        }

        ValidateEditorState(report);
        ValidateBaseline(profile, report);
        ValidateProfileValues(profile, report, validateAuthoringValues);
        ValidatePresets(profile, report);
        ValidatePlayerBinding(profile, report);
        ValidateItemBindings(profile, report);

        if (!report.HasErrors)
        {
            report.AddInfo(
                "All V2 visual scale bindings and protected scopes are valid.");
        }

        return report;
    }

    private static void ValidateEditorState(ValidationReport report)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            report.AddError("Apply is not allowed in Play Mode.");
        }

        if (EditorApplication.isCompiling)
        {
            report.AddError("Apply is not allowed while scripts compile.");
        }

        if (EditorApplication.isUpdating)
        {
            report.AddError("Apply is not allowed while assets update.");
        }

        if (PrefabStageUtility.GetCurrentPrefabStage() != null)
        {
            report.AddError("Close the open Prefab Stage before applying.");
        }
    }

    private static void ValidateBaseline(
        ProjectOneVisualScaleProfileSO profile,
        ValidationReport report)
    {
        if (!profile.BaselineCaptured)
        {
            report.AddError("The V2 baseline has not been captured.");
        }

        if (!string.Equals(
                profile.BaselineSourceHead,
                ExpectedBaselineSourceHead,
                StringComparison.Ordinal))
        {
            report.AddError(
                "Baseline source HEAD must be " +
                ExpectedBaselineSourceHead + ".");
        }

        ValidatePositiveVector(
            profile.ReferenceCharacterVisualLocalScale,
            "referenceCharacterVisualLocalScale",
            report);
        ValidatePositive(
            profile.ReferenceBodyPostItMultiplier,
            "referenceBodyPostItMultiplier",
            report);
        ValidatePositive(
            profile.ReferenceWorldPostItMultiplier,
            "referenceWorldPostItMultiplier",
            report);
        ValidatePositiveVector(
            profile.CapturedCharacterVisualLocalScale,
            "capturedCharacterVisualLocalScale",
            report);
        ValidatePositive(
            profile.CapturedBodyPostItMultiplier,
            "capturedBodyPostItMultiplier",
            report);
        ValidatePositive(
            profile.CapturedWorldPostItMultiplier,
            "capturedWorldPostItMultiplier",
            report);
        ValidatePositive(
            profile.CapturedCharacterVisualScale,
            "capturedCharacterVisualScale",
            report);
        ValidatePositive(
            profile.CapturedBodyPostItRatioToCharacter,
            "capturedBodyPostItRatioToCharacter",
            report);
        ValidatePositive(
            profile.CapturedWorldPostItRatioToCharacter,
            "capturedWorldPostItRatioToCharacter",
            report);
    }

    private static void ValidateProfileValues(
        ProjectOneVisualScaleProfileSO profile,
        ValidationReport report,
        bool validateAuthoringValues)
    {
        if (validateAuthoringValues)
        {
            ValidatePositive(
                profile.CharacterVisualScale,
                "characterVisualScale",
                report);
            ValidatePositive(
                profile.BodyPostItRatioToCharacter,
                "bodyPostItRatioToCharacter",
                report);
            ValidatePositive(
                profile.WorldPostItRatioToCharacter,
                "worldPostItRatioToCharacter",
                report);
        }

        IReadOnlyList<ItemScaleRule> rules = profile.ItemRules;
        if (rules == null)
        {
            report.AddError("itemRules is null.");
            return;
        }

        HashSet<string> ruleIds = new HashSet<string>(
            StringComparer.Ordinal);
        bool foundBaseballBat = false;
        for (int index = 0; index < rules.Count; index++)
        {
            ItemScaleRule rule = rules[index];
            string label = "itemRules[" + index + "]";
            if (rule == null)
            {
                report.AddError(label + " is null.");
                continue;
            }

            if (string.IsNullOrWhiteSpace(rule.RuleId))
            {
                report.AddError(label + ".ruleId is blank.");
            }
            else if (!ruleIds.Add(rule.RuleId))
            {
                report.AddError(
                    "Duplicate item ruleId: " + rule.RuleId + ".");
            }

            if (string.Equals(
                    rule.RuleId,
                    BaseballBatRuleId,
                    StringComparison.Ordinal))
            {
                foundBaseballBat = true;
            }

            if (validateAuthoringValues)
            {
                ValidatePositive(
                    rule.RatioToCharacter,
                    label + ".ratioToCharacter",
                    report);
                ValidatePositive(
                    rule.FixedFinalScale,
                    label + ".fixedFinalScale",
                    report);
            }

            if (!IsFinite(rule.MaximumFinalScale) ||
                rule.MaximumFinalScale < 0f)
            {
                report.AddError(
                    label +
                    ".maximumFinalScale must be finite and non-negative.");
            }

            ValidatePositiveVector(
                rule.ReferenceWorldVisualLocalScale,
                label + ".referenceWorldVisualLocalScale",
                report);
            ValidatePositiveVector(
                rule.ReferenceEquippedVisualLocalScale,
                label + ".referenceEquippedVisualLocalScale",
                report);
            ValidatePositiveVector(
                rule.CapturedWorldVisualLocalScale,
                label + ".capturedWorldVisualLocalScale",
                report);
            ValidatePositiveVector(
                rule.CapturedEquippedVisualLocalScale,
                label + ".capturedEquippedVisualLocalScale",
                report);

            if (validateAuthoringValues && rule.Enabled)
            {
                float rawFinal;
                bool clamped;
                float itemFinal = CalculateItemFinal(
                    rule,
                    profile.CharacterVisualScale,
                    out rawFinal,
                    out clamped);
                ValidatePositive(
                    rawFinal,
                    label + ".calculatedRawFinal",
                    report);
                ValidatePositive(
                    itemFinal,
                    label + ".calculatedFinal",
                    report);
                ValidatePositive(
                    itemFinal / profile.CharacterVisualScale,
                    label + ".equippedCompensation",
                    report);
            }
        }

        if (!foundBaseballBat)
        {
            report.AddError(
                "The required BaseballBat item rule is missing.");
        }
    }

    private static void ValidatePresets(
        ProjectOneVisualScaleProfileSO profile,
        ValidationReport report)
    {
        VisualScalePreset[] presets =
        {
            profile.PresetA,
            profile.PresetB,
            profile.PresetC
        };

        HashSet<string> presetIds = new HashSet<string>(
            StringComparer.Ordinal);
        for (int index = 0; index < presets.Length; index++)
        {
            VisualScalePreset preset = presets[index];
            string label = "preset" + (char)('A' + index);
            if (preset == null)
            {
                report.AddError(label + " is null.");
                continue;
            }

            if (string.IsNullOrWhiteSpace(preset.PresetId))
            {
                report.AddError(label + ".presetId is blank.");
            }
            else if (!presetIds.Add(preset.PresetId))
            {
                report.AddError(
                    "Duplicate presetId: " + preset.PresetId + ".");
            }

            ValidatePositive(
                preset.CharacterVisualScale,
                label + ".characterVisualScale",
                report);
            ValidatePositive(
                preset.BodyPostItRatioToCharacter,
                label + ".bodyPostItRatioToCharacter",
                report);
            ValidatePositive(
                preset.WorldPostItRatioToCharacter,
                label + ".worldPostItRatioToCharacter",
                report);

            IReadOnlyList<ItemPresetValue> itemValues = preset.ItemValues;
            if (itemValues == null)
            {
                report.AddError(label + ".itemValues is null.");
                continue;
            }

            HashSet<string> itemIds = new HashSet<string>(
                StringComparer.Ordinal);
            for (int itemIndex = 0;
                itemIndex < itemValues.Count;
                itemIndex++)
            {
                ItemPresetValue value = itemValues[itemIndex];
                string itemLabel =
                    label + ".itemValues[" + itemIndex + "]";
                if (value == null)
                {
                    report.AddError(itemLabel + " is null.");
                    continue;
                }

                if (string.IsNullOrWhiteSpace(value.RuleId))
                {
                    report.AddError(itemLabel + ".ruleId is blank.");
                }
                else if (!itemIds.Add(value.RuleId))
                {
                    report.AddError(
                        label + " contains duplicate item ruleId " +
                        value.RuleId + ".");
                }

                ValidatePositive(
                    value.RatioToCharacter,
                    itemLabel + ".ratioToCharacter",
                    report);
                ValidatePositive(
                    value.FixedFinalScale,
                    itemLabel + ".fixedFinalScale",
                    report);
                if (!IsFinite(value.MaximumFinalScale) ||
                    value.MaximumFinalScale < 0f)
                {
                    report.AddError(
                        itemLabel +
                        ".maximumFinalScale must be finite and " +
                        "non-negative.");
                }
            }
        }
    }

    private static void ValidatePlayerBinding(
        ProjectOneVisualScaleProfileSO profile,
        ValidationReport report)
    {
        GameObject playerPrefab = profile.ProductionPlayerPrefab;
        if (playerPrefab == null)
        {
            report.AddError("Production PlayerPrefab is null.");
            return;
        }

        string assetPath = AssetDatabase.GetAssetPath(playerPrefab);
        if (!string.Equals(
                assetPath,
                ExpectedPlayerPrefabPath,
                StringComparison.Ordinal))
        {
            report.AddError(
                "Production PlayerPrefab must resolve to " +
                ExpectedPlayerPrefabPath + ".");
            return;
        }

        if (!string.Equals(
                AssetDatabase.AssetPathToGUID(assetPath),
                ExpectedPlayerPrefabGuid,
                StringComparison.Ordinal))
        {
            report.AddError(
                "Production PlayerPrefab GUID does not match the " +
                "NetworkManager registration.");
        }

        if (!string.Equals(
                profile.CharacterVisualTransformPath,
                ExpectedCharacterPath,
                StringComparison.Ordinal))
        {
            report.AddError(
                "Character visual path must be " +
                ExpectedCharacterPath + ".");
        }

        if (!string.Equals(
                profile.PresenterSerializedPropertyName,
                ExpectedBodyPropertyName,
                StringComparison.Ordinal) ||
            !string.Equals(
                profile.WorldPresenterSerializedPropertyName,
                ExpectedWorldPropertyName,
                StringComparison.Ordinal))
        {
            report.AddError(
                "Presenter serialized property names do not match the " +
                "approved contract.");
        }

        ValidateAssetWritable(assetPath, report);

        GameObject root = null;
        try
        {
            root = PrefabUtility.LoadPrefabContents(assetPath);
            Transform target = ResolveExactTransform(
                root.transform,
                profile.CharacterVisualTransformPath);
            if (target == null)
            {
                report.AddError(
                    "Character visual exact path was not found once: " +
                    profile.CharacterVisualTransformPath + ".");
            }
            else
            {
                ValidateVisualTarget(
                    target,
                    "Character visual target",
                    report);
                ValidatePositiveVector(
                    target.localScale,
                    "Character visual current localScale",
                    report);
            }

            PlayerPostItWorldPresenter[] presenters =
                root.GetComponentsInChildren<PlayerPostItWorldPresenter>(
                    true);
            if (presenters.Length != 1)
            {
                report.AddError(
                    "Production PlayerPrefab must contain exactly one " +
                    "PlayerPostItWorldPresenter; found " +
                    presenters.Length + ".");
            }
            else
            {
                SerializedObject presenter =
                    new SerializedObject(presenters[0]);
                ValidateFloatProperty(
                    presenter,
                    profile.PresenterSerializedPropertyName,
                    report);
                ValidateFloatProperty(
                    presenter,
                    profile.WorldPresenterSerializedPropertyName,
                    report);
            }
        }
        catch (Exception exception)
        {
            report.AddError(
                "Production PlayerPrefab validation failed: " +
                exception.Message);
        }
        finally
        {
            if (root != null)
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }
    }

    private static void ValidateItemBindings(
        ProjectOneVisualScaleProfileSO profile,
        ValidationReport report)
    {
        IReadOnlyList<ItemScaleRule> rules = profile.ItemRules;
        if (rules == null)
        {
            return;
        }

        HashSet<string> bindingKeys = new HashSet<string>(
            StringComparer.Ordinal);
        for (int index = 0; index < rules.Count; index++)
        {
            ItemScaleRule rule = rules[index];
            if (rule == null)
            {
                continue;
            }

            ValidateItemPrefabBinding(
                rule,
                rule.WorldPrefab,
                rule.WorldVisualTransformPath,
                "World",
                report,
                bindingKeys);
            ValidateItemPrefabBinding(
                rule,
                rule.EquippedPrefab,
                rule.EquippedVisualTransformPath,
                "Equipped",
                report,
                bindingKeys);

            if (string.Equals(
                    rule.RuleId,
                    BaseballBatRuleId,
                    StringComparison.Ordinal))
            {
                string worldPath =
                    AssetDatabase.GetAssetPath(rule.WorldPrefab);
                string equippedPath =
                    AssetDatabase.GetAssetPath(rule.EquippedPrefab);
                if (!string.Equals(
                        worldPath,
                        ExpectedBatWorldPrefabPath,
                        StringComparison.Ordinal) ||
                    !string.Equals(
                        rule.WorldVisualTransformPath,
                        ExpectedBatWorldTransformPath,
                        StringComparison.Ordinal) ||
                    !string.Equals(
                        equippedPath,
                        ExpectedBatEquippedPrefabPath,
                        StringComparison.Ordinal) ||
                    !string.Equals(
                        rule.EquippedVisualTransformPath,
                        ExpectedBatEquippedTransformPath,
                        StringComparison.Ordinal))
                {
                    report.AddError(
                        "BaseballBat bindings do not match the approved " +
                        "WorldVisualScale/BaseballBatMesh targets.");
                }
            }
        }
    }

    private static void ValidateItemPrefabBinding(
        ItemScaleRule rule,
        GameObject prefab,
        string transformPath,
        string bindingLabel,
        ValidationReport report,
        HashSet<string> bindingKeys)
    {
        string label = rule.RuleId + " " + bindingLabel;
        if (prefab == null)
        {
            report.AddError(label + " Prefab is null.");
            return;
        }

        string assetPath = AssetDatabase.GetAssetPath(prefab);
        if (string.IsNullOrWhiteSpace(assetPath) ||
            !assetPath.StartsWith("Assets/", StringComparison.Ordinal) ||
            !File.Exists(GetFullAssetPath(assetPath)))
        {
            report.AddError(label + " is not a valid Project asset.");
            return;
        }

        if (string.IsNullOrWhiteSpace(transformPath))
        {
            report.AddError(label + " exact Transform path is blank.");
            return;
        }

        string bindingKey = assetPath + "|" + transformPath;
        if (!bindingKeys.Add(bindingKey))
        {
            report.AddError(
                "Duplicate visual target binding: " + bindingKey + ".");
        }

        ValidateAssetWritable(assetPath, report);

        GameObject root = null;
        try
        {
            root = PrefabUtility.LoadPrefabContents(assetPath);
            Transform target = ResolveExactTransform(
                root.transform,
                transformPath);
            if (target == null)
            {
                report.AddError(
                    label + " exact Transform path was not found once: " +
                    transformPath + ".");
                return;
            }

            ValidateVisualTarget(target, label + " target", report);
            ValidatePositiveVector(
                target.localScale,
                label + " current localScale",
                report);
        }
        catch (Exception exception)
        {
            report.AddError(
                label + " binding validation failed: " +
                exception.Message);
        }
        finally
        {
            if (root != null)
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }
    }

    private static void ValidateAssetWritable(
        string assetPath,
        ValidationReport report)
    {
        string fullPath = GetFullAssetPath(assetPath);
        if (!File.Exists(fullPath))
        {
            report.AddError("Asset does not exist: " + assetPath + ".");
            return;
        }

        if ((File.GetAttributes(fullPath) & FileAttributes.ReadOnly) != 0)
        {
            report.AddError("Asset is read-only: " + assetPath + ".");
        }

        string openForEditMessage;
        if (!AssetDatabase.IsOpenForEdit(
                assetPath,
                out openForEditMessage,
                StatusQueryOptions.UseCachedIfPossible))
        {
            report.AddError(
                "Asset is not open for edit: " + assetPath + ". " +
                openForEditMessage);
        }
    }

    private static void ValidateVisualTarget(
        Transform target,
        string label,
        ValidationReport report)
    {
        if (target.GetComponentsInChildren<Renderer>(true).Length == 0)
        {
            report.AddError(label + " contains no Renderer.");
        }

        Component[] components =
            target.GetComponentsInChildren<Component>(true);
        for (int index = 0; index < components.Length; index++)
        {
            Component component = components[index];
            if (component == null)
            {
                report.AddError(label + " contains a Missing Script.");
                continue;
            }

            if (IsForbiddenVisualComponent(component))
            {
                report.AddError(
                    label + " contains protected component " +
                    component.GetType().FullName + " at " +
                    GetRelativePath(target, component.transform) + ".");
            }
        }
    }

    private static bool IsForbiddenVisualComponent(Component component)
    {
        return component is Rigidbody ||
            component is Collider ||
            component is Joint ||
            component is CharacterController ||
            component is Camera ||
            component is AudioListener ||
            component is NetworkObject ||
            component is NetworkBehaviour;
    }

    private static void ValidateFloatProperty(
        SerializedObject serializedObject,
        string propertyName,
        ValidationReport report)
    {
        SerializedProperty property =
            serializedObject.FindProperty(propertyName);
        if (property == null)
        {
            report.AddError(
                "Missing Presenter property: " + propertyName + ".");
        }
        else if (property.propertyType != SerializedPropertyType.Float)
        {
            report.AddError(
                "Presenter property is not float: " + propertyName + ".");
        }
    }

    private static void AddPlayerPreview(
        ProjectOneVisualScaleProfileSO profile,
        ChangePlan plan,
        bool restore)
    {
        string assetPath =
            AssetDatabase.GetAssetPath(profile.ProductionPlayerPrefab);
        GameObject root = null;
        try
        {
            root = PrefabUtility.LoadPrefabContents(assetPath);
            Transform character = ResolveExactTransform(
                root.transform,
                profile.CharacterVisualTransformPath);
            PlayerPostItWorldPresenter presenter =
                root.GetComponentsInChildren<PlayerPostItWorldPresenter>(
                    true)[0];
            SerializedObject presenterObject =
                new SerializedObject(presenter);
            SerializedProperty bodyProperty =
                presenterObject.FindProperty(
                    profile.PresenterSerializedPropertyName);
            SerializedProperty worldProperty =
                presenterObject.FindProperty(
                    profile.WorldPresenterSerializedPropertyName);

            float characterFinal = restore
                ? profile.CapturedCharacterVisualScale
                : profile.CharacterVisualScale;
            Vector3 characterTarget = restore
                ? profile.CapturedCharacterVisualLocalScale
                : profile.ReferenceCharacterVisualLocalScale *
                    characterFinal;
            float bodyFinal = restore
                ? profile.CapturedCharacterVisualScale *
                    profile.CapturedBodyPostItRatioToCharacter
                : profile.CharacterVisualScale *
                    profile.BodyPostItRatioToCharacter;
            float bodyTarget = restore
                ? profile.CapturedBodyPostItMultiplier
                : profile.ReferenceBodyPostItMultiplier *
                    profile.BodyPostItRatioToCharacter;
            float worldFinal = restore
                ? profile.CapturedCharacterVisualScale *
                    profile.CapturedWorldPostItRatioToCharacter
                : profile.CharacterVisualScale *
                    profile.WorldPostItRatioToCharacter;
            float worldTarget = restore
                ? profile.CapturedWorldPostItMultiplier
                : profile.ReferenceWorldPostItMultiplier * worldFinal;

            AddVectorChange(
                plan,
                "Character",
                assetPath,
                profile.CharacterVisualTransformPath,
                "m_LocalScale",
                character.localScale,
                characterTarget,
                characterFinal,
                false);
            AddFloatChange(
                plan,
                "Body Post-it",
                assetPath,
                GetRelativePath(root.transform, presenter.transform),
                profile.PresenterSerializedPropertyName,
                bodyProperty.floatValue,
                bodyTarget,
                bodyFinal,
                false);
            AddFloatChange(
                plan,
                "World Post-it",
                assetPath,
                GetRelativePath(root.transform, presenter.transform),
                profile.WorldPresenterSerializedPropertyName,
                worldProperty.floatValue,
                worldTarget,
                worldFinal,
                false);
        }
        finally
        {
            if (root != null)
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }
    }

    private static void AddItemPreview(
        ProjectOneVisualScaleProfileSO profile,
        ChangePlan plan,
        bool restore)
    {
        IReadOnlyList<ItemScaleRule> rules = profile.ItemRules;
        for (int index = 0; index < rules.Count; index++)
        {
            ItemScaleRule rule = rules[index];
            if (rule == null || !rule.Enabled)
            {
                continue;
            }

            float rawFinal;
            bool clamped;
            float itemFinal;
            if (restore)
            {
                itemFinal = CalculatePresetItemFinal(
                    profile.PresetA,
                    rule.RuleId,
                    profile.CapturedCharacterVisualScale,
                    out rawFinal,
                    out clamped);
            }
            else
            {
                itemFinal = CalculateItemFinal(
                    rule,
                    profile.CharacterVisualScale,
                    out rawFinal,
                    out clamped);
            }

            float characterFinal = restore
                ? profile.CapturedCharacterVisualScale
                : profile.CharacterVisualScale;
            Vector3 worldTarget = restore
                ? rule.CapturedWorldVisualLocalScale
                : rule.ReferenceWorldVisualLocalScale * itemFinal;
            Vector3 equippedTarget = restore
                ? rule.CapturedEquippedVisualLocalScale
                : rule.ReferenceEquippedVisualLocalScale *
                    (itemFinal / characterFinal);

            AddItemPrefabPreview(
                plan,
                rule,
                rule.WorldPrefab,
                rule.WorldVisualTransformPath,
                "Item World",
                worldTarget,
                itemFinal,
                clamped);
            AddItemPrefabPreview(
                plan,
                rule,
                rule.EquippedPrefab,
                rule.EquippedVisualTransformPath,
                "Item Equipped",
                equippedTarget,
                itemFinal,
                clamped);
        }
    }

    private static void AddItemPrefabPreview(
        ChangePlan plan,
        ItemScaleRule rule,
        GameObject prefab,
        string transformPath,
        string category,
        Vector3 targetScale,
        float finalRatio,
        bool clamped)
    {
        string assetPath = AssetDatabase.GetAssetPath(prefab);
        GameObject root = null;
        try
        {
            root = PrefabUtility.LoadPrefabContents(assetPath);
            Transform target = ResolveExactTransform(
                root.transform,
                transformPath);
            AddVectorChange(
                plan,
                category,
                assetPath,
                transformPath,
                rule.RuleId + ".m_LocalScale",
                target.localScale,
                targetScale,
                finalRatio,
                clamped);
        }
        finally
        {
            if (root != null)
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }
    }

    private static ApplyResult RunTransaction(
        ProjectOneVisualScaleProfileSO profile,
        bool restore,
        Func<bool> additionalVerification)
    {
        ChangePlan plan = restore
            ? BuildRestorePreview(profile)
            : BuildPreview(profile);
        ValidationReport validation = plan.Validation;
        if (validation.HasErrors)
        {
            return new ApplyResult(
                false,
                "Validation failed. No asset was written.",
                string.Empty,
                validation,
                plan,
                false);
        }

        List<string> targetPaths = GetTargetAssetPaths(
            profile,
            restore);
        BackupSet backup = null;
        bool assetEditing = false;
        bool rollbackSucceeded = false;

        try
        {
            Dictionary<string, string> protectedBefore =
                CaptureProtectedFingerprints(profile, targetPaths);
            backup = BackupSet.Create(
                targetPaths,
                restore ? "Restore" : "Apply");

            AssetDatabase.StartAssetEditing();
            assetEditing = true;
            ApplyPlayerValues(profile, restore);
            ApplyItemValues(profile, restore);
            AssetDatabase.StopAssetEditing();
            assetEditing = false;

            ImportTargets(targetPaths);

            ChangePlan verificationPlan = restore
                ? BuildRestorePreview(profile)
                : BuildPreview(profile);
            if (verificationPlan.Validation.HasErrors ||
                verificationPlan.HasChanges)
            {
                throw new InvalidOperationException(
                    "Exact post-apply value verification failed.");
            }

            Dictionary<string, string> protectedAfter =
                CaptureProtectedFingerprints(profile, targetPaths);
            VerifyProtectedFingerprints(
                protectedBefore,
                protectedAfter);

            if (restore)
            {
                RestoreProfileAuthoringValues(profile);
            }

            if (additionalVerification != null &&
                !additionalVerification())
            {
                throw new InvalidOperationException(
                    "Additional post-write verification gate failed.");
            }

            return new ApplyResult(
                true,
                restore
                    ? "Captured V2 baseline restored successfully."
                    : "All visual scales applied successfully.",
                backup.RootPath,
                validation,
                verificationPlan,
                false);
        }
        catch (Exception exception)
        {
            if (assetEditing)
            {
                AssetDatabase.StopAssetEditing();
                assetEditing = false;
            }

            string rollbackMessage = string.Empty;
            if (backup != null)
            {
                try
                {
                    backup.Restore();
                    ImportTargets(backup.AssetPaths);
                    backup.VerifyRestored();
                    rollbackSucceeded = true;
                }
                catch (Exception rollbackException)
                {
                    rollbackMessage =
                        " Rollback also failed: " +
                        rollbackException.Message;
                }
            }

            validation.AddError(
                "Transaction failed: " + exception.Message +
                rollbackMessage);
            return new ApplyResult(
                false,
                rollbackSucceeded
                    ? "Transaction failed and raw-byte rollback succeeded."
                    : "Transaction failed; inspect the validation report.",
                backup != null ? backup.RootPath : string.Empty,
                validation,
                plan,
                rollbackSucceeded);
        }
        finally
        {
            if (assetEditing)
            {
                AssetDatabase.StopAssetEditing();
            }
        }
    }

    private static void ApplyPlayerValues(
        ProjectOneVisualScaleProfileSO profile,
        bool restore)
    {
        string assetPath =
            AssetDatabase.GetAssetPath(profile.ProductionPlayerPrefab);
        GameObject root = null;
        try
        {
            root = PrefabUtility.LoadPrefabContents(assetPath);
            HashSet<string> allowedPaths = new HashSet<string>(
                StringComparer.Ordinal)
            {
                profile.CharacterVisualTransformPath
            };
            string before = CaptureProtectedFingerprint(
                root,
                allowedPaths,
                true,
                profile.PresenterSerializedPropertyName,
                profile.WorldPresenterSerializedPropertyName);

            Transform character = ResolveExactTransform(
                root.transform,
                profile.CharacterVisualTransformPath);
            character.localScale = restore
                ? profile.CapturedCharacterVisualLocalScale
                : profile.ReferenceCharacterVisualLocalScale *
                    profile.CharacterVisualScale;

            PlayerPostItWorldPresenter presenter =
                root.GetComponentsInChildren<PlayerPostItWorldPresenter>(
                    true)[0];
            SerializedObject presenterObject =
                new SerializedObject(presenter);
            SerializedProperty bodyProperty =
                presenterObject.FindProperty(
                    profile.PresenterSerializedPropertyName);
            SerializedProperty worldProperty =
                presenterObject.FindProperty(
                    profile.WorldPresenterSerializedPropertyName);
            bodyProperty.floatValue = restore
                ? profile.CapturedBodyPostItMultiplier
                : profile.ReferenceBodyPostItMultiplier *
                    profile.BodyPostItRatioToCharacter;
            float worldFinal =
                profile.CharacterVisualScale *
                profile.WorldPostItRatioToCharacter;
            worldProperty.floatValue = restore
                ? profile.CapturedWorldPostItMultiplier
                : profile.ReferenceWorldPostItMultiplier * worldFinal;
            presenterObject.ApplyModifiedPropertiesWithoutUndo();

            string after = CaptureProtectedFingerprint(
                root,
                allowedPaths,
                true,
                profile.PresenterSerializedPropertyName,
                profile.WorldPresenterSerializedPropertyName);
            if (!string.Equals(before, after, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Player protected component fingerprint changed " +
                    "before saving.");
            }

            if (PrefabUtility.SaveAsPrefabAsset(root, assetPath) == null)
            {
                throw new IOException(
                    "Saving Production PlayerPrefab failed.");
            }
        }
        finally
        {
            if (root != null)
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }
    }

    private static void ApplyItemValues(
        ProjectOneVisualScaleProfileSO profile,
        bool restore)
    {
        Dictionary<string, List<ScaleAssignment>> assignments =
            BuildItemAssignments(profile, restore);
        foreach (KeyValuePair<string, List<ScaleAssignment>> pair
            in assignments)
        {
            string assetPath = pair.Key;
            List<ScaleAssignment> assetAssignments = pair.Value;
            GameObject root = null;
            try
            {
                root = PrefabUtility.LoadPrefabContents(assetPath);
                HashSet<string> allowedPaths = new HashSet<string>(
                    assetAssignments.Select(item => item.TransformPath),
                    StringComparer.Ordinal);
                string before = CaptureProtectedFingerprint(
                    root,
                    allowedPaths,
                    false,
                    null,
                    null);

                for (int index = 0;
                    index < assetAssignments.Count;
                    index++)
                {
                    ScaleAssignment assignment =
                        assetAssignments[index];
                    Transform target = ResolveExactTransform(
                        root.transform,
                        assignment.TransformPath);
                    target.localScale = assignment.TargetScale;
                }

                string after = CaptureProtectedFingerprint(
                    root,
                    allowedPaths,
                    false,
                    null,
                    null);
                if (!string.Equals(before, after, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        "Protected component fingerprint changed for " +
                        assetPath + " before saving.");
                }

                if (PrefabUtility.SaveAsPrefabAsset(root, assetPath) == null)
                {
                    throw new IOException(
                        "Saving item Prefab failed: " + assetPath + ".");
                }
            }
            finally
            {
                if (root != null)
                {
                    PrefabUtility.UnloadPrefabContents(root);
                }
            }
        }
    }

    private static Dictionary<string, List<ScaleAssignment>>
        BuildItemAssignments(
            ProjectOneVisualScaleProfileSO profile,
            bool restore)
    {
        Dictionary<string, List<ScaleAssignment>> assignments =
            new Dictionary<string, List<ScaleAssignment>>(
                StringComparer.Ordinal);
        IReadOnlyList<ItemScaleRule> rules = profile.ItemRules;
        for (int index = 0; index < rules.Count; index++)
        {
            ItemScaleRule rule = rules[index];
            if (rule == null || !rule.Enabled)
            {
                continue;
            }

            float rawFinal;
            bool clamped;
            float itemFinal = CalculateItemFinal(
                rule,
                profile.CharacterVisualScale,
                out rawFinal,
                out clamped);
            Vector3 worldScale = restore
                ? rule.CapturedWorldVisualLocalScale
                : rule.ReferenceWorldVisualLocalScale * itemFinal;
            Vector3 equippedScale = restore
                ? rule.CapturedEquippedVisualLocalScale
                : rule.ReferenceEquippedVisualLocalScale *
                    (itemFinal / profile.CharacterVisualScale);

            AddScaleAssignment(
                assignments,
                AssetDatabase.GetAssetPath(rule.WorldPrefab),
                rule.WorldVisualTransformPath,
                worldScale);
            AddScaleAssignment(
                assignments,
                AssetDatabase.GetAssetPath(rule.EquippedPrefab),
                rule.EquippedVisualTransformPath,
                equippedScale);
        }

        return assignments;
    }

    private static void AddScaleAssignment(
        Dictionary<string, List<ScaleAssignment>> assignments,
        string assetPath,
        string transformPath,
        Vector3 targetScale)
    {
        List<ScaleAssignment> assetAssignments;
        if (!assignments.TryGetValue(assetPath, out assetAssignments))
        {
            assetAssignments = new List<ScaleAssignment>();
            assignments.Add(assetPath, assetAssignments);
        }

        assetAssignments.Add(new ScaleAssignment(
            transformPath,
            targetScale));
    }

    private static Dictionary<string, string>
        CaptureProtectedFingerprints(
            ProjectOneVisualScaleProfileSO profile,
            IEnumerable<string> assetPaths)
    {
        Dictionary<string, HashSet<string>> allowedByPath =
            BuildAllowedTransformPaths(profile);
        string playerPath =
            AssetDatabase.GetAssetPath(profile.ProductionPlayerPrefab);
        Dictionary<string, string> fingerprints =
            new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (string assetPath in assetPaths.Distinct(
            StringComparer.Ordinal))
        {
            if (!assetPath.EndsWith(
                    ".prefab",
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            GameObject root = null;
            try
            {
                root = PrefabUtility.LoadPrefabContents(assetPath);
                HashSet<string> allowedPaths;
                if (!allowedByPath.TryGetValue(
                        assetPath,
                        out allowedPaths))
                {
                    allowedPaths = new HashSet<string>(
                        StringComparer.Ordinal);
                }

                bool isPlayer = string.Equals(
                    assetPath,
                    playerPath,
                    StringComparison.Ordinal);
                fingerprints.Add(
                    assetPath,
                    CaptureProtectedFingerprint(
                        root,
                        allowedPaths,
                        isPlayer,
                        profile.PresenterSerializedPropertyName,
                        profile.WorldPresenterSerializedPropertyName));
            }
            finally
            {
                if (root != null)
                {
                    PrefabUtility.UnloadPrefabContents(root);
                }
            }
        }

        return fingerprints;
    }

    private static Dictionary<string, HashSet<string>>
        BuildAllowedTransformPaths(
            ProjectOneVisualScaleProfileSO profile)
    {
        Dictionary<string, HashSet<string>> result =
            new Dictionary<string, HashSet<string>>(
                StringComparer.Ordinal);
        AddAllowedPath(
            result,
            AssetDatabase.GetAssetPath(profile.ProductionPlayerPrefab),
            profile.CharacterVisualTransformPath);

        IReadOnlyList<ItemScaleRule> rules = profile.ItemRules;
        for (int index = 0; index < rules.Count; index++)
        {
            ItemScaleRule rule = rules[index];
            if (rule == null || !rule.Enabled)
            {
                continue;
            }

            AddAllowedPath(
                result,
                AssetDatabase.GetAssetPath(rule.WorldPrefab),
                rule.WorldVisualTransformPath);
            AddAllowedPath(
                result,
                AssetDatabase.GetAssetPath(rule.EquippedPrefab),
                rule.EquippedVisualTransformPath);
        }

        return result;
    }

    private static void AddAllowedPath(
        Dictionary<string, HashSet<string>> pathsByAsset,
        string assetPath,
        string transformPath)
    {
        HashSet<string> paths;
        if (!pathsByAsset.TryGetValue(assetPath, out paths))
        {
            paths = new HashSet<string>(StringComparer.Ordinal);
            pathsByAsset.Add(assetPath, paths);
        }

        paths.Add(transformPath);
    }

    private static string CaptureProtectedFingerprint(
        GameObject root,
        HashSet<string> allowedScalePaths,
        bool normalizePresenterFields,
        string bodyPropertyName,
        string worldPropertyName)
    {
        StringBuilder builder = new StringBuilder(16384);
        Transform[] transforms =
            root.GetComponentsInChildren<Transform>(true);
        for (int transformIndex = 0;
            transformIndex < transforms.Length;
            transformIndex++)
        {
            Transform transform = transforms[transformIndex];
            string path = GetRelativePath(root.transform, transform);
            builder.Append("T|").Append(path).Append('|');
            AppendVector(builder, transform.localPosition);
            AppendQuaternion(builder, transform.localRotation);
            if (allowedScalePaths.Contains(path))
            {
                builder.Append("|<allowed-scale>");
            }
            else
            {
                builder.Append('|');
                AppendVector(builder, transform.localScale);
            }

            builder.Append("|active=")
                .Append(transform.gameObject.activeSelf ? '1' : '0')
                .AppendLine();

            Component[] components =
                transform.GetComponents<Component>();
            for (int componentIndex = 0;
                componentIndex < components.Length;
                componentIndex++)
            {
                Component component = components[componentIndex];
                if (component == null)
                {
                    builder.Append("C|")
                        .Append(componentIndex)
                        .Append("|<missing>")
                        .AppendLine();
                    continue;
                }

                builder.Append("C|")
                    .Append(componentIndex)
                    .Append('|')
                    .Append(component.GetType().AssemblyQualifiedName)
                    .Append('|');
                if (component is Transform)
                {
                    builder.Append("<transform>").AppendLine();
                    continue;
                }

                string json = EditorJsonUtility.ToJson(component, false);
                json = InstanceIdRegex.Replace(
                    json,
                    "\"instanceID\":0");
                if (normalizePresenterFields &&
                    component is PlayerPostItWorldPresenter)
                {
                    json = NormalizeJsonFloatField(
                        json,
                        bodyPropertyName);
                    json = NormalizeJsonFloatField(
                        json,
                        worldPropertyName);
                }

                builder.Append(json).AppendLine();
            }
        }

        return ComputeSha256(builder.ToString());
    }

    private static string NormalizeJsonFloatField(
        string json,
        string fieldName)
    {
        if (string.IsNullOrWhiteSpace(fieldName))
        {
            return json;
        }

        Regex fieldRegex = new Regex(
            "(\\\"" + Regex.Escape(fieldName) +
            "\\\"\\s*:\\s*)[-+0-9.eE]+");
        return fieldRegex.Replace(
            json,
            match => match.Groups[1].Value + "0");
    }

    private static void VerifyProtectedFingerprints(
        IReadOnlyDictionary<string, string> before,
        IReadOnlyDictionary<string, string> after)
    {
        foreach (KeyValuePair<string, string> pair in before)
        {
            string afterValue;
            if (!after.TryGetValue(pair.Key, out afterValue) ||
                !string.Equals(
                    pair.Value,
                    afterValue,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Protected Physics/Network fingerprint changed: " +
                    pair.Key + ".");
            }
        }
    }

    private static void RestoreProfileAuthoringValues(
        ProjectOneVisualScaleProfileSO profile)
    {
        Undo.RecordObject(profile, "Restore V2 Visual Scale Profile");
        SerializedObject serializedProfile =
            new SerializedObject(profile);
        serializedProfile.FindProperty("characterVisualScale").floatValue =
            profile.CapturedCharacterVisualScale;
        serializedProfile.FindProperty(
            "bodyPostItRatioToCharacter").floatValue =
            profile.CapturedBodyPostItRatioToCharacter;
        serializedProfile.FindProperty(
            "worldPostItRatioToCharacter").floatValue =
            profile.CapturedWorldPostItRatioToCharacter;
        ApplyPresetItemsToSerializedProfile(
            serializedProfile,
            profile.PresetA);
        serializedProfile.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(profile);
        AssetDatabase.SaveAssets();
    }

    internal static void ApplyPresetItemsToSerializedProfile(
        SerializedObject serializedProfile,
        VisualScalePreset preset)
    {
        if (serializedProfile == null || preset == null)
        {
            return;
        }

        SerializedProperty rules =
            serializedProfile.FindProperty("itemRules");
        for (int itemIndex = 0;
            itemIndex < preset.ItemValues.Count;
            itemIndex++)
        {
            ItemPresetValue presetValue =
                preset.ItemValues[itemIndex];
            for (int ruleIndex = 0;
                ruleIndex < rules.arraySize;
                ruleIndex++)
            {
                SerializedProperty rule =
                    rules.GetArrayElementAtIndex(ruleIndex);
                SerializedProperty ruleId =
                    rule.FindPropertyRelative("ruleId");
                if (!string.Equals(
                        ruleId.stringValue,
                        presetValue.RuleId,
                        StringComparison.Ordinal))
                {
                    continue;
                }

                rule.FindPropertyRelative("mode").enumValueIndex =
                    (int)presetValue.Mode;
                rule.FindPropertyRelative(
                    "ratioToCharacter").floatValue =
                    presetValue.RatioToCharacter;
                rule.FindPropertyRelative(
                    "fixedFinalScale").floatValue =
                    presetValue.FixedFinalScale;
                rule.FindPropertyRelative(
                    "maximumFinalScale").floatValue =
                    presetValue.MaximumFinalScale;
                break;
            }
        }
    }

    private static List<string> GetTargetAssetPaths(
        ProjectOneVisualScaleProfileSO profile,
        bool includeProfile)
    {
        HashSet<string> paths = new HashSet<string>(
            StringComparer.Ordinal)
        {
            AssetDatabase.GetAssetPath(profile.ProductionPlayerPrefab)
        };

        IReadOnlyList<ItemScaleRule> rules = profile.ItemRules;
        for (int index = 0; index < rules.Count; index++)
        {
            ItemScaleRule rule = rules[index];
            if (rule == null || !rule.Enabled)
            {
                continue;
            }

            paths.Add(AssetDatabase.GetAssetPath(rule.WorldPrefab));
            paths.Add(AssetDatabase.GetAssetPath(rule.EquippedPrefab));
        }

        if (includeProfile)
        {
            string profilePath = AssetDatabase.GetAssetPath(profile);
            if (!string.IsNullOrWhiteSpace(profilePath))
            {
                paths.Add(profilePath);
            }
        }

        return paths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToList();
    }

    private static void ImportTargets(IEnumerable<string> assetPaths)
    {
        foreach (string path in assetPaths.Distinct(
            StringComparer.Ordinal))
        {
            AssetDatabase.ImportAsset(
                path,
                ImportAssetOptions.ForceUpdate |
                ImportAssetOptions.ForceSynchronousImport);
        }
    }

    private static Transform ResolveExactTransform(
        Transform root,
        string exactPath)
    {
        if (root == null || string.IsNullOrWhiteSpace(exactPath))
        {
            return null;
        }

        Transform found = root.Find(exactPath);
        if (found == null)
        {
            return null;
        }

        int matchCount = 0;
        Transform matched = null;
        Transform[] transforms =
            root.GetComponentsInChildren<Transform>(true);
        for (int index = 0; index < transforms.Length; index++)
        {
            if (!string.Equals(
                    GetRelativePath(root, transforms[index]),
                    exactPath,
                    StringComparison.Ordinal))
            {
                continue;
            }

            matchCount++;
            matched = transforms[index];
        }

        return matchCount == 1 && matched == found ? found : null;
    }

    private static string GetRelativePath(
        Transform root,
        Transform target)
    {
        if (root == target)
        {
            return string.Empty;
        }

        Stack<string> names = new Stack<string>();
        Transform current = target;
        while (current != null && current != root)
        {
            names.Push(current.name);
            current = current.parent;
        }

        return current == root
            ? string.Join("/", names)
            : "<outside-root>";
    }

    private static void AddVectorChange(
        ChangePlan plan,
        string category,
        string assetPath,
        string objectPath,
        string property,
        Vector3 current,
        Vector3 target,
        float finalRatio,
        bool clamped)
    {
        bool changed = !Approximately(current, target);
        VisualScaleChangeStatus status = GetChangeStatus(
            changed,
            clamped);
        plan.Add(new VisualScaleChange(
            category,
            assetPath,
            objectPath,
            property,
            FormatVector(current),
            FormatVector(target),
            finalRatio,
            clamped,
            changed,
            status));
    }

    private static void AddFloatChange(
        ChangePlan plan,
        string category,
        string assetPath,
        string objectPath,
        string property,
        float current,
        float target,
        float finalRatio,
        bool clamped)
    {
        bool changed = !Approximately(current, target);
        VisualScaleChangeStatus status = GetChangeStatus(
            changed,
            clamped);
        plan.Add(new VisualScaleChange(
            category,
            assetPath,
            objectPath,
            property,
            FormatFloat(current),
            FormatFloat(target),
            finalRatio,
            clamped,
            changed,
            status));
    }

    private static VisualScaleChangeStatus GetChangeStatus(
        bool changed,
        bool clamped)
    {
        if (clamped)
        {
            return VisualScaleChangeStatus.Clamped;
        }

        return changed
            ? VisualScaleChangeStatus.WillChange
            : VisualScaleChangeStatus.NoChange;
    }

    private static float CalculateItemFinal(
        ItemScaleRule rule,
        float characterScale,
        out float rawFinal,
        out bool clamped)
    {
        rawFinal = rule.Mode == ItemScaleMode.RelativeToCharacter
            ? characterScale * rule.RatioToCharacter
            : rule.FixedFinalScale;
        clamped =
            rule.MaximumFinalScale > 0f &&
            rawFinal > rule.MaximumFinalScale;
        return clamped ? rule.MaximumFinalScale : rawFinal;
    }

    private static float CalculatePresetItemFinal(
        VisualScalePreset preset,
        string ruleId,
        float characterScale,
        out float rawFinal,
        out bool clamped)
    {
        if (preset == null || preset.ItemValues == null)
        {
            rawFinal = characterScale;
            clamped = false;
            return rawFinal;
        }

        for (int index = 0; index < preset.ItemValues.Count; index++)
        {
            ItemPresetValue value = preset.ItemValues[index];
            if (value != null &&
                string.Equals(
                    value.RuleId,
                    ruleId,
                    StringComparison.Ordinal))
            {
                rawFinal =
                    value.Mode == ItemScaleMode.RelativeToCharacter
                    ? characterScale * value.RatioToCharacter
                    : value.FixedFinalScale;
                clamped =
                    value.MaximumFinalScale > 0f &&
                    rawFinal > value.MaximumFinalScale;
                return clamped
                    ? value.MaximumFinalScale
                    : rawFinal;
            }
        }

        rawFinal = characterScale;
        clamped = false;
        return rawFinal;
    }

    private static bool Approximately(float left, float right)
    {
        return Mathf.Abs(left - right) <= ValueTolerance;
    }

    private static bool Approximately(Vector3 left, Vector3 right)
    {
        return Approximately(left.x, right.x) &&
            Approximately(left.y, right.y) &&
            Approximately(left.z, right.z);
    }

    private static void ValidatePositive(
        float value,
        string label,
        ValidationReport report)
    {
        if (!IsFinite(value) || value <= 0f)
        {
            report.AddError(label + " must be finite and positive.");
        }
    }

    private static void ValidatePositiveVector(
        Vector3 value,
        string label,
        ValidationReport report)
    {
        if (!IsFinite(value.x) ||
            !IsFinite(value.y) ||
            !IsFinite(value.z) ||
            value.x <= 0f ||
            value.y <= 0f ||
            value.z <= 0f)
        {
            report.AddError(label + " must be finite and positive.");
        }
    }

    private static bool IsFinite(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }

    private static string FormatFloat(float value)
    {
        return value.ToString("0.########", CultureInfo.InvariantCulture);
    }

    private static string FormatVector(Vector3 value)
    {
        return "(" + FormatFloat(value.x) + ", " +
            FormatFloat(value.y) + ", " +
            FormatFloat(value.z) + ")";
    }

    private static void AppendVector(
        StringBuilder builder,
        Vector3 value)
    {
        builder.Append(FormatFloat(value.x))
            .Append(',')
            .Append(FormatFloat(value.y))
            .Append(',')
            .Append(FormatFloat(value.z));
    }

    private static void AppendQuaternion(
        StringBuilder builder,
        Quaternion value)
    {
        builder.Append(FormatFloat(value.x))
            .Append(',')
            .Append(FormatFloat(value.y))
            .Append(',')
            .Append(FormatFloat(value.z))
            .Append(',')
            .Append(FormatFloat(value.w));
    }

    private static string ComputeSha256(string value)
    {
        using (SHA256 sha = SHA256.Create())
        {
            byte[] hash = sha.ComputeHash(
                Encoding.UTF8.GetBytes(value));
            return BitConverter.ToString(hash)
                .Replace("-", string.Empty);
        }
    }

    private static string ComputeFileSha256(string path)
    {
        using (SHA256 sha = SHA256.Create())
        using (FileStream stream = File.OpenRead(path))
        {
            byte[] hash = sha.ComputeHash(stream);
            return BitConverter.ToString(hash)
                .Replace("-", string.Empty);
        }
    }

    private static string GetFullAssetPath(string assetPath)
    {
        string projectRoot = Directory.GetParent(
            Application.dataPath).FullName;
        return Path.GetFullPath(Path.Combine(projectRoot, assetPath));
    }

    private sealed class ScaleAssignment
    {
        internal ScaleAssignment(
            string transformPath,
            Vector3 targetScale)
        {
            TransformPath = transformPath;
            TargetScale = targetScale;
        }

        internal string TransformPath { get; }
        internal Vector3 TargetScale { get; }
    }

    private sealed class BackupSet
    {
        private readonly List<BackupEntry> entries;

        private BackupSet(
            string rootPath,
            List<BackupEntry> entries)
        {
            RootPath = rootPath;
            this.entries = entries;
        }

        internal string RootPath { get; }
        internal IEnumerable<string> AssetPaths =>
            entries.Select(entry => entry.AssetPath);

        internal static BackupSet Create(
            IEnumerable<string> assetPaths,
            string operation)
        {
            string projectRoot = Directory.GetParent(
                Application.dataPath).FullName;
            string rootPath = Path.Combine(
                projectRoot,
                "Library",
                "ProjectOneVisualScaleBackups",
                DateTime.Now.ToString(
                    "yyyyMMdd_HHmmss_fff",
                    CultureInfo.InvariantCulture) +
                "-" + operation);
            Directory.CreateDirectory(rootPath);

            List<BackupEntry> entries = new List<BackupEntry>();
            foreach (string assetPath in assetPaths.Distinct(
                StringComparer.Ordinal))
            {
                string sourcePath = GetFullAssetPath(assetPath);
                string backupPath = Path.Combine(
                    rootPath,
                    assetPath.Replace('/', Path.DirectorySeparatorChar));
                string backupDirectory =
                    Path.GetDirectoryName(backupPath);
                Directory.CreateDirectory(backupDirectory);
                File.Copy(sourcePath, backupPath, false);

                string metaPath = sourcePath + ".meta";
                entries.Add(new BackupEntry(
                    assetPath,
                    sourcePath,
                    backupPath,
                    ComputeFileSha256(sourcePath),
                    File.Exists(metaPath)
                        ? ComputeFileSha256(metaPath)
                        : string.Empty));
            }

            return new BackupSet(rootPath, entries);
        }

        internal void Restore()
        {
            for (int index = 0; index < entries.Count; index++)
            {
                BackupEntry entry = entries[index];
                File.Copy(
                    entry.BackupPath,
                    entry.SourcePath,
                    true);
            }
        }

        internal void VerifyRestored()
        {
            for (int index = 0; index < entries.Count; index++)
            {
                BackupEntry entry = entries[index];
                if (!string.Equals(
                        ComputeFileSha256(entry.SourcePath),
                        entry.SourceSha256,
                        StringComparison.Ordinal))
                {
                    throw new IOException(
                        "Raw rollback verification failed: " +
                        entry.AssetPath + ".");
                }

                string metaPath = entry.SourcePath + ".meta";
                string metaHash = File.Exists(metaPath)
                    ? ComputeFileSha256(metaPath)
                    : string.Empty;
                if (!string.Equals(
                        metaHash,
                        entry.MetaSha256,
                        StringComparison.Ordinal))
                {
                    throw new IOException(
                        "Meta changed during transaction: " +
                        entry.AssetPath + ".meta.");
                }
            }
        }
    }

    private sealed class BackupEntry
    {
        internal BackupEntry(
            string assetPath,
            string sourcePath,
            string backupPath,
            string sourceSha256,
            string metaSha256)
        {
            AssetPath = assetPath;
            SourcePath = sourcePath;
            BackupPath = backupPath;
            SourceSha256 = sourceSha256;
            MetaSha256 = metaSha256;
        }

        internal string AssetPath { get; }
        internal string SourcePath { get; }
        internal string BackupPath { get; }
        internal string SourceSha256 { get; }
        internal string MetaSha256 { get; }
    }
}
