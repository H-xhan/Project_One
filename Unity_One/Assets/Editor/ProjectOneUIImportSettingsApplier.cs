using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

public sealed class ProjectOneUIImportSettingsApplier : EditorWindow
{
    private const string DefaultSourceFolder = "Assets/ProjectONE/UI/Sprites";
    private const string MenuPath = "Project ONE/UI/Apply UI Sprite Import Settings";
    private const string ManifestFolderName = "_Manifest";
    private const string ReportFileName = "import_settings_report.csv";
    private const string NextStepText = "After import settings are applied, create a UI test scene and check transparency/background.";

    private static readonly HashSet<string> AlwaysExcludedFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "_Manifest"
    };

    private static readonly HashSet<string> UtilityFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "_preview",
        "_Duplicates"
    };

    private static readonly HashSet<string> IconLikeFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "Icons",
        "Decorations",
        "Characters"
    };

    private string sourceFolder = DefaultSourceFolder;
    private bool reimportAfterApply = true;
    private bool includeUtilityFolders;
    private Vector2 logScroll;
    private readonly List<string> logLines = new List<string>();

    [MenuItem(MenuPath)]
    public static void Open()
    {
        ProjectOneUIImportSettingsApplier window = GetWindow<ProjectOneUIImportSettingsApplier>("UI Import Settings");
        window.minSize = new Vector2(720f, 500f);
        window.Show();
    }

    private void OnGUI()
    {
        EditorGUILayout.LabelField("Project ONE UI Sprite Import Settings", EditorStyles.boldLabel);
        EditorGUILayout.Space(6f);

        DrawSourceFolderField();

        reimportAfterApply = EditorGUILayout.ToggleLeft("Reimport After Apply", reimportAfterApply);
        includeUtilityFolders = EditorGUILayout.ToggleLeft("Include Utility Folders (_preview, _Duplicates)", includeUtilityFolders);

        EditorGUILayout.Space(8f);

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Dry Run", GUILayout.Height(32f)))
            {
                RunDryRun();
            }

            if (GUILayout.Button("Apply Import Settings", GUILayout.Height(32f)))
            {
                ApplyImportSettings();
            }
        }

        EditorGUILayout.Space(8f);
        EditorGUILayout.LabelField("Log", EditorStyles.boldLabel);

        using (GUILayout.ScrollViewScope scrollView = new GUILayout.ScrollViewScope(logScroll, EditorStyles.helpBox))
        {
            logScroll = scrollView.scrollPosition;
            foreach (string line in logLines)
            {
                EditorGUILayout.LabelField(line, EditorStyles.wordWrappedLabel);
            }
        }

        EditorGUILayout.Space(6f);
        EditorGUILayout.HelpBox("Next Step: " + NextStepText, MessageType.Info);
    }

    private void DrawSourceFolderField()
    {
        DefaultAsset currentFolder = AssetDatabase.LoadAssetAtPath<DefaultAsset>(sourceFolder);
        EditorGUI.BeginChangeCheck();
        UnityEngine.Object selected = EditorGUILayout.ObjectField("Source Folder", currentFolder, typeof(DefaultAsset), false);
        if (EditorGUI.EndChangeCheck() && selected != null)
        {
            string selectedPath = AssetDatabase.GetAssetPath(selected);
            if (AssetDatabase.IsValidFolder(selectedPath))
            {
                sourceFolder = NormalizeAssetPath(selectedPath);
            }
            else
            {
                AddLog("Selected object is not a folder. Source Folder was not changed.");
            }
        }

        EditorGUI.BeginChangeCheck();
        string typedPath = EditorGUILayout.TextField("Source Path", sourceFolder);
        if (EditorGUI.EndChangeCheck())
        {
            sourceFolder = NormalizeAssetPath(typedPath);
        }
    }

    private void RunDryRun()
    {
        logLines.Clear();

        ImportSettingsPlan plan = BuildPlan();
        LogPlanPreview(plan, "Dry Run");
        LogSummary(plan, "Dry Run");
    }

    private void ApplyImportSettings()
    {
        logLines.Clear();

        if (!EditorUtility.DisplayDialog(
                "Apply UI Sprite Import Settings",
                "This will change Texture Import Settings for matching PNG assets only. It will not move, delete, rename, or modify PNG pixels.",
                "Apply",
                "Cancel"))
        {
            AddLog("Apply cancelled.");
            return;
        }

        ImportSettingsPlan plan = BuildPlan();
        if (plan.Errors.Count == 0)
        {
            ExecutePlan(plan);
        }

        WriteReport(plan);
        LogPlanPreview(plan, "Apply");
        LogSummary(plan, "Apply");
        AssetDatabase.Refresh();
    }

    private ImportSettingsPlan BuildPlan()
    {
        ImportSettingsPlan plan = new ImportSettingsPlan(NormalizeAssetPath(sourceFolder), includeUtilityFolders, reimportAfterApply);

        if (!ValidateSourceFolder(plan))
        {
            return plan;
        }

        string[] guids = AssetDatabase.FindAssets("t:Texture2D", new[] { plan.SourceFolder });
        foreach (string guid in guids)
        {
            string assetPath = NormalizeAssetPath(AssetDatabase.GUIDToAssetPath(guid));
            if (!assetPath.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (ShouldExclude(assetPath, plan.SourceFolder, includeUtilityFolders))
            {
                continue;
            }

            plan.ScannedCount++;
            PlanEntry entry = BuildEntry(assetPath, plan.SourceFolder);
            plan.Entries.Add(entry);
        }

        plan.Entries.Sort((left, right) => string.Compare(left.Path, right.Path, StringComparison.OrdinalIgnoreCase));
        return plan;
    }

    private bool ValidateSourceFolder(ImportSettingsPlan plan)
    {
        if (string.IsNullOrWhiteSpace(plan.SourceFolder) || !plan.SourceFolder.StartsWith("Assets", StringComparison.Ordinal))
        {
            plan.Errors.Add("Source Folder must be inside Assets.");
            return false;
        }

        if (!AssetDatabase.IsValidFolder(plan.SourceFolder))
        {
            plan.Errors.Add($"Source Folder does not exist: {plan.SourceFolder}");
            return false;
        }

        return true;
    }

    private PlanEntry BuildEntry(string assetPath, string sourceRoot)
    {
        TextureImporter importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
        string category = GetCategory(assetPath, sourceRoot);
        ImportSettingsSnapshot target = ImportSettingsSnapshot.CreateTarget(category);

        if (importer == null)
        {
            return PlanEntry.Skipped(assetPath, category, target, "TextureImporter not found.");
        }

        TextureImporterSettings settings = new TextureImporterSettings();
        importer.ReadTextureSettings(settings);
        ImportSettingsSnapshot current = ImportSettingsSnapshot.CreateCurrent(importer, settings);
        bool alreadyOk = current.Matches(target);

        return new PlanEntry(
            assetPath,
            category,
            current,
            target,
            alreadyOk ? "already_ok" : "would_change",
            alreadyOk ? "Already matches recommended UI sprite import settings." : BuildChangeNote(current, target));
    }

    private void ExecutePlan(ImportSettingsPlan plan)
    {
        foreach (PlanEntry entry in plan.Entries)
        {
            if (entry.Action == "already_ok" || entry.Action == "skipped")
            {
                continue;
            }

            TextureImporter importer = AssetImporter.GetAtPath(entry.Path) as TextureImporter;
            if (importer == null)
            {
                entry.Action = "error";
                entry.Note = "TextureImporter not found during Apply. Skipped.";
                Debug.LogWarning($"Project ONE UI Import Settings Applier: TextureImporter not found during Apply for {entry.Path}.");
                continue;
            }

            try
            {
                ApplyImporterSettings(importer, entry.Target);
                if (reimportAfterApply)
                {
                    importer.SaveAndReimport();
                    entry.Note = "Applied recommended settings and reimported.";
                }
                else
                {
                    AssetDatabase.ImportAsset(entry.Path, ImportAssetOptions.ForceUpdate);
                    entry.Note = "Applied recommended settings and imported with ForceUpdate.";
                }

                entry.Action = "changed";
            }
            catch (Exception exception)
            {
                entry.Action = "error";
                entry.Note = exception.Message;
                Debug.LogError($"Project ONE UI Import Settings Applier failed for {entry.Path}: {exception}");
            }
        }
    }

    private static void ApplyImporterSettings(TextureImporter importer, ImportSettingsSnapshot target)
    {
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.alphaIsTransparency = true;
        importer.filterMode = FilterMode.Bilinear;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.mipmapEnabled = false;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.maxTextureSize = target.MaxSize;
        importer.spritePixelsPerUnit = 100f;
        importer.isReadable = false;

        TextureImporterSettings settings = new TextureImporterSettings();
        importer.ReadTextureSettings(settings);
        settings.spriteMeshType = SpriteMeshType.FullRect;
        importer.SetTextureSettings(settings);
    }

    private void WriteReport(ImportSettingsPlan plan)
    {
        if (!AssetDatabase.IsValidFolder(plan.SourceFolder))
        {
            return;
        }

        string manifestFolder = NormalizeAssetPath($"{plan.SourceFolder}/{ManifestFolderName}");
        EnsureFolder(manifestFolder);

        string reportAssetPath = NormalizeAssetPath($"{manifestFolder}/{ReportFileName}");
        string reportFullPath = AssetPathToFullPath(reportAssetPath);
        Directory.CreateDirectory(Path.GetDirectoryName(reportFullPath));
        File.WriteAllText(reportFullPath, BuildReportCsv(plan), Encoding.UTF8);
        AssetDatabase.ImportAsset(reportAssetPath);
    }

    private static string BuildReportCsv(ImportSettingsPlan plan)
    {
        StringBuilder builder = new StringBuilder();
        builder.AppendLine("path,category,old_texture_type,new_texture_type,old_max_size,new_max_size,old_compression,new_compression,action,note");

        foreach (PlanEntry entry in plan.Entries)
        {
            ProjectOneUIImportSettingsCsvUtility.AppendCsvLine(
                builder,
                entry.Path,
                entry.Category,
                entry.Current.TextureType,
                entry.Target.TextureType,
                entry.Current.MaxSize.ToString(CultureInfo.InvariantCulture),
                entry.Target.MaxSize.ToString(CultureInfo.InvariantCulture),
                entry.Current.Compression,
                entry.Target.Compression,
                entry.Action,
                entry.Note);
        }

        return builder.ToString();
    }

    private void LogPlanPreview(ImportSettingsPlan plan, string label)
    {
        foreach (string error in plan.Errors)
        {
            AddLog("ERROR: " + error);
            Debug.LogWarning("Project ONE UI Import Settings Applier: " + error);
        }

        foreach (PlanEntry entry in plan.Entries)
        {
            string line =
                $"{label}: {entry.Action}: {entry.Path} | {entry.Category} | " +
                $"TextureType {entry.Current.TextureType} -> {entry.Target.TextureType}, " +
                $"MaxSize {entry.Current.MaxSize} -> {entry.Target.MaxSize}, " +
                $"Compression {entry.Current.Compression} -> {entry.Target.Compression}";

            AddLog(line);
            Debug.Log("Project ONE UI Import Settings Applier: " + line);
        }
    }

    private void LogSummary(ImportSettingsPlan plan, string label)
    {
        string summary =
            $"Project ONE UI Import Settings Applier {label} Summary | " +
            $"scanned count: {plan.ScannedCount}, " +
            $"changed count: {plan.ChangedCount}, " +
            $"already ok count: {plan.AlreadyOkCount}, " +
            $"skipped count: {plan.SkippedCount}, " +
            $"error count: {plan.ErrorCount}";

        AddLog(summary);
        Debug.Log(summary);
    }

    private void AddLog(string message)
    {
        logLines.Add(message);
        Repaint();
    }

    private static bool ShouldExclude(string assetPath, string sourceRoot, bool includeUtilityFolders)
    {
        string[] segments = GetRelativeSegments(assetPath, sourceRoot);
        for (int i = 0; i < segments.Length - 1; i++)
        {
            string segment = segments[i];
            if (AlwaysExcludedFolders.Contains(segment))
            {
                return true;
            }

            if (!includeUtilityFolders && UtilityFolders.Contains(segment))
            {
                return true;
            }
        }

        return false;
    }

    private static string GetCategory(string assetPath, string sourceRoot)
    {
        string[] segments = GetRelativeSegments(assetPath, sourceRoot);
        foreach (string segment in segments)
        {
            if (string.Equals(segment, "ColorChips", StringComparison.OrdinalIgnoreCase))
            {
                return "ColorChips";
            }
        }

        foreach (string segment in segments)
        {
            if (IconLikeFolders.Contains(segment))
            {
                return segment;
            }
        }

        return segments.Length > 1 ? segments[0] : "Root";
    }

    private static string BuildChangeNote(ImportSettingsSnapshot current, ImportSettingsSnapshot target)
    {
        List<string> changes = new List<string>();

        AddChange(changes, "Texture Type", current.TextureType, target.TextureType);
        AddChange(changes, "Sprite Mode", current.SpriteMode, target.SpriteMode);
        AddChange(changes, "Alpha Is Transparency", current.AlphaIsTransparency, target.AlphaIsTransparency);
        AddChange(changes, "Mesh Type", current.MeshType, target.MeshType);
        AddChange(changes, "Filter Mode", current.FilterMode, target.FilterMode);
        AddChange(changes, "Wrap Mode", current.WrapMode, target.WrapMode);
        AddChange(changes, "Mip Maps", current.MipmapEnabled, target.MipmapEnabled);
        AddChange(changes, "Compression", current.Compression, target.Compression);
        AddChange(changes, "Max Size", current.MaxSize.ToString(CultureInfo.InvariantCulture), target.MaxSize.ToString(CultureInfo.InvariantCulture));
        AddChange(changes, "Pixels Per Unit", current.SpritePixelsPerUnit, target.SpritePixelsPerUnit);
        AddChange(changes, "Read/Write Enabled", current.ReadWriteEnabled, target.ReadWriteEnabled);

        return changes.Count == 0 ? "No change required." : string.Join("; ", changes);
    }

    private static void AddChange(List<string> changes, string label, string oldValue, string newValue)
    {
        if (!string.Equals(oldValue, newValue, StringComparison.Ordinal))
        {
            changes.Add($"{label}: {oldValue} -> {newValue}");
        }
    }

    private static string[] GetRelativeSegments(string assetPath, string sourceRoot)
    {
        string relativePath = GetRelativeAssetPath(sourceRoot, assetPath);
        return relativePath.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
    }

    private static string GetRelativeAssetPath(string root, string assetPath)
    {
        root = NormalizeAssetPath(root).TrimEnd('/');
        assetPath = NormalizeAssetPath(assetPath);
        if (!assetPath.StartsWith(root + "/", StringComparison.OrdinalIgnoreCase))
        {
            return assetPath;
        }

        return assetPath.Substring(root.Length + 1);
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

    private sealed class ImportSettingsPlan
    {
        public readonly string SourceFolder;
        public readonly bool IncludeUtilityFolders;
        public readonly bool ReimportAfterApply;
        public readonly List<PlanEntry> Entries = new List<PlanEntry>();
        public readonly List<string> Errors = new List<string>();
        public int ScannedCount;

        public ImportSettingsPlan(string sourceFolder, bool includeUtilityFolders, bool reimportAfterApply)
        {
            SourceFolder = sourceFolder;
            IncludeUtilityFolders = includeUtilityFolders;
            ReimportAfterApply = reimportAfterApply;
        }

        public int ChangedCount => Entries.Count(entry => entry.Action == "changed" || entry.Action == "would_change");
        public int AlreadyOkCount => Entries.Count(entry => entry.Action == "already_ok");
        public int SkippedCount => Entries.Count(entry => entry.Action == "skipped");
        public int ErrorCount => Errors.Count + Entries.Count(entry => entry.Action == "error");
    }

    private sealed class PlanEntry
    {
        public readonly string Path;
        public readonly string Category;
        public readonly ImportSettingsSnapshot Current;
        public readonly ImportSettingsSnapshot Target;
        public string Action;
        public string Note;

        public PlanEntry(string path, string category, ImportSettingsSnapshot current, ImportSettingsSnapshot target, string action, string note)
        {
            Path = path;
            Category = category;
            Current = current;
            Target = target;
            Action = action;
            Note = note;
        }

        public static PlanEntry Skipped(string path, string category, ImportSettingsSnapshot target, string note)
        {
            return new PlanEntry(path, category, ImportSettingsSnapshot.Empty, target, "skipped", note);
        }
    }

    private sealed class ImportSettingsSnapshot
    {
        public static readonly ImportSettingsSnapshot Empty = new ImportSettingsSnapshot(
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            0,
            string.Empty,
            string.Empty,
            string.Empty);

        public readonly string TextureType;
        public readonly string SpriteMode;
        public readonly string AlphaIsTransparency;
        public readonly string MeshType;
        public readonly string FilterMode;
        public readonly string WrapMode;
        public readonly string MipmapEnabled;
        public readonly int MaxSize;
        public readonly string Compression;
        public readonly string SpritePixelsPerUnit;
        public readonly string ReadWriteEnabled;

        private ImportSettingsSnapshot(
            string textureType,
            string spriteMode,
            string alphaIsTransparency,
            string meshType,
            string filterMode,
            string wrapMode,
            string mipmapEnabled,
            int maxSize,
            string compression,
            string spritePixelsPerUnit,
            string readWriteEnabled)
        {
            TextureType = textureType;
            SpriteMode = spriteMode;
            AlphaIsTransparency = alphaIsTransparency;
            MeshType = meshType;
            FilterMode = filterMode;
            WrapMode = wrapMode;
            MipmapEnabled = mipmapEnabled;
            MaxSize = maxSize;
            Compression = compression;
            SpritePixelsPerUnit = spritePixelsPerUnit;
            ReadWriteEnabled = readWriteEnabled;
        }

        public static ImportSettingsSnapshot CreateCurrent(TextureImporter importer, TextureImporterSettings settings)
        {
            return new ImportSettingsSnapshot(
                importer.textureType.ToString(),
                importer.spriteImportMode.ToString(),
                importer.alphaIsTransparency.ToString(),
                settings.spriteMeshType.ToString(),
                importer.filterMode.ToString(),
                importer.wrapMode.ToString(),
                importer.mipmapEnabled.ToString(),
                importer.maxTextureSize,
                importer.textureCompression.ToString(),
                importer.spritePixelsPerUnit.ToString(CultureInfo.InvariantCulture),
                importer.isReadable.ToString());
        }

        public static ImportSettingsSnapshot CreateTarget(string category)
        {
            return new ImportSettingsSnapshot(
                TextureImporterType.Sprite.ToString(),
                SpriteImportMode.Single.ToString(),
                true.ToString(),
                SpriteMeshType.FullRect.ToString(),
                UnityEngine.FilterMode.Bilinear.ToString(),
                TextureWrapMode.Clamp.ToString(),
                false.ToString(),
                GetRecommendedMaxSize(category),
                TextureImporterCompression.Uncompressed.ToString(),
                100f.ToString(CultureInfo.InvariantCulture),
                false.ToString());
        }

        public bool Matches(ImportSettingsSnapshot target)
        {
            return string.Equals(TextureType, target.TextureType, StringComparison.Ordinal) &&
                   string.Equals(SpriteMode, target.SpriteMode, StringComparison.Ordinal) &&
                   string.Equals(AlphaIsTransparency, target.AlphaIsTransparency, StringComparison.Ordinal) &&
                   string.Equals(MeshType, target.MeshType, StringComparison.Ordinal) &&
                   string.Equals(FilterMode, target.FilterMode, StringComparison.Ordinal) &&
                   string.Equals(WrapMode, target.WrapMode, StringComparison.Ordinal) &&
                   string.Equals(MipmapEnabled, target.MipmapEnabled, StringComparison.Ordinal) &&
                   MaxSize == target.MaxSize &&
                   string.Equals(Compression, target.Compression, StringComparison.Ordinal) &&
                   string.Equals(SpritePixelsPerUnit, target.SpritePixelsPerUnit, StringComparison.Ordinal) &&
                   string.Equals(ReadWriteEnabled, target.ReadWriteEnabled, StringComparison.Ordinal);
        }

        private static int GetRecommendedMaxSize(string category)
        {
            if (string.Equals(category, "ColorChips", StringComparison.OrdinalIgnoreCase))
            {
                return 512;
            }

            if (IconLikeFolders.Contains(category))
            {
                return 2048;
            }

            return 4096;
        }
    }
}

internal static class ProjectOneUIImportSettingsCsvUtility
{
    public static void AppendCsvLine(StringBuilder builder, params string[] values)
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
}
