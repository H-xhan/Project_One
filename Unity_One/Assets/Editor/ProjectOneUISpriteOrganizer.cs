using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEngine;

public sealed class ProjectOneUISpriteOrganizer : EditorWindow
{
    private const string DefaultSourceFolder = "Assets/ProjectONE/UI/Sprites";
    private const string MenuPath = "Project ONE/UI/Organize UI Sprites";
    private const int NearDuplicateThreshold = 8;

    private static readonly string[] CategoryOrder =
    {
        "Logo_Title",
        "Lobby_CharacterSelect",
        "Result_Ranking",
        "MainMenu",
        "HUD",
        "Buttons",
        "Characters",
        "Icons",
        "Decorations",
        "ColorChips",
        "Panels",
        "Needs_Check"
    };

    private static readonly Dictionary<string, string[]> CategoryKeywords = new Dictionary<string, string[]>
    {
        {
            "Logo_Title",
            new[]
            {
                "logo", "title", "project", "one", "subtitle", "로고", "타이틀", "제목", "프로젝트", "슬로건"
            }
        },
        {
            "Lobby_CharacterSelect",
            new[]
            {
                "lobby", "room", "roomcode", "ready", "character_select", "character-card", "character_card", "select", "help",
                "로비", "룸", "룸코드", "준비", "캐릭터", "선택", "도움말", "레디"
            }
        },
        {
            "Result_Ranking",
            new[]
            {
                "result", "victory", "win", "ranking", "rank", "medal", "score", "stamp", "success", "crown_tab", "participant",
                "결과", "승리", "우승", "랭킹", "순위", "메달", "점수", "도장", "성공", "참가자"
            }
        },
        {
            "MainMenu",
            new[]
            {
                "mainmenu", "main_menu", "quickstart", "customgame", "tutorial", "quit", "menu_panel",
                "메인메뉴", "메인", "빠른시작", "커스텀", "튜토리얼", "게임종료", "메뉴"
            }
        },
        {
            "HUD",
            new[]
            {
                "hud", "stamina", "timer", "mission", "reward", "status", "progress", "gauge", "bar", "coin_panel", "timer_panel",
                "스태미나", "타이머", "미션", "보상", "상태바", "진행바", "게이지", "코인패널", "시간패널"
            }
        },
        {
            "Buttons",
            new[]
            {
                "button", "btn", "start", "play", "retry", "restart", "exit", "home", "back", "ready_button",
                "버튼", "시작", "플레이", "다시", "재시작", "나가기", "홈", "로비로", "레디버튼"
            }
        },
        {
            "Characters",
            new[]
            {
                "hamster", "mascot", "avatar", "character", "face", "profile_hamster",
                "햄스터", "마스코트", "아바타", "캐릭터", "얼굴"
            }
        },
        {
            "Icons",
            new[]
            {
                "icon", "coin", "clock", "stopwatch", "lightning", "paw", "settings", "sound", "community", "notice", "bell", "plus",
                "home_icon", "exit_icon", "book", "gamepad", "power", "clipboard", "heart",
                "아이콘", "코인", "시계", "스톱워치", "번개", "발바닥", "설정", "사운드", "커뮤니티", "공지", "벨", "플러스",
                "홈아이콘", "나가기아이콘", "책", "게임패드", "전원", "클립보드", "하트"
            }
        },
        {
            "Decorations",
            new[]
            {
                "tape", "clip", "paperclip", "pin", "star", "ribbon", "sticker", "folded", "corner", "tag_deco", "decoration",
                "테이프", "클립", "종이클립", "핀", "별", "리본", "스티커", "접힘", "모서리", "장식"
            }
        },
        {
            "ColorChips",
            new[]
            {
                "chip", "color", "swatch", "palette", "fff7eb", "1f2a44", "ffd56a", "b8d3f6", "cdebd7",
                "색상칩", "컬러", "팔레트", "크림", "네이비", "노랑", "파랑", "민트"
            }
        },
        {
            "Panels",
            new[]
            {
                "panel", "card", "frame", "board", "label", "banner", "tag", "note", "memo", "paper", "box", "container",
                "패널", "카드", "프레임", "보드", "라벨", "배너", "태그", "노트", "메모", "종이", "박스", "컨테이너"
            }
        }
    };

    private string sourceFolder = DefaultSourceFolder;
    private bool applyRecommendedImportSettings = true;
    private bool exactDuplicateDetection = true;
    private bool nearDuplicateReport = true;
    private Vector2 logScroll;
    private readonly List<string> logLines = new List<string>();
    private OrganizationPlan lastDryRunPlan;

    [MenuItem(MenuPath)]
    public static void Open()
    {
        ProjectOneUISpriteOrganizer window = GetWindow<ProjectOneUISpriteOrganizer>("UI Sprite Organizer");
        window.minSize = new Vector2(720f, 520f);
        window.Show();
    }

    private void OnGUI()
    {
        EditorGUILayout.LabelField("Project ONE UI Sprite Organizer", EditorStyles.boldLabel);
        EditorGUILayout.Space(6f);

        DrawSourceFolderField();

        applyRecommendedImportSettings = EditorGUILayout.ToggleLeft("Apply Recommended Import Settings", applyRecommendedImportSettings);
        exactDuplicateDetection = EditorGUILayout.ToggleLeft("Exact Duplicate Detection", exactDuplicateDetection);
        nearDuplicateReport = EditorGUILayout.ToggleLeft("Near Duplicate Report", nearDuplicateReport);

        EditorGUILayout.Space(8f);

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Dry Run", GUILayout.Height(32f)))
            {
                RunDryRun();
            }

            if (GUILayout.Button("Apply Organization", GUILayout.Height(32f)))
            {
                ApplyOrganization();
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
        OrganizationPlan plan = BuildPlan(false);
        lastDryRunPlan = plan;

        AddPlanPreview(plan);
        LogSummary(plan, "Dry Run");
    }

    private void ApplyOrganization()
    {
        logLines.Clear();

        if (!EditorUtility.DisplayDialog(
                "Apply UI Sprite Organization",
                "This will move PNG assets with AssetDatabase.MoveAsset, optionally update import settings, and write CSV reports. PNG pixels will not be modified.",
                "Apply",
                "Cancel"))
        {
            AddLog("Apply cancelled.");
            return;
        }

        OrganizationPlan plan = BuildPlan(false);
        ExecutePlan(plan);
        lastDryRunPlan = plan;

        AddPlanPreview(plan);
        LogSummary(plan, "Apply");
        AssetDatabase.Refresh();
    }

    private OrganizationPlan BuildPlan(bool includeImportTargetsOnly)
    {
        OrganizationPlan plan = new OrganizationPlan(sourceFolder, applyRecommendedImportSettings, exactDuplicateDetection, nearDuplicateReport);

        if (!ValidateSourceFolder(plan))
        {
            return plan;
        }

        foreach (string folder in CategoryOrder)
        {
            plan.RequiredFolders.Add(GetCategoryFolder(plan.SourceFolder, folder));
        }

        plan.RequiredFolders.Add(GetExactDuplicateFolder(plan.SourceFolder));
        plan.RequiredFolders.Add(GetNearDuplicateFolder(plan.SourceFolder));
        plan.RequiredFolders.Add(GetManifestFolder(plan.SourceFolder));

        List<SpriteAssetInfo> sprites = ScanSprites(plan);
        if (includeImportTargetsOnly)
        {
            return plan;
        }

        Dictionary<string, SpriteAssetInfo> duplicateMap = SelectExactDuplicates(plan, sprites);
        HashSet<string> plannedTargets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (SpriteAssetInfo sprite in sprites)
        {
            if (duplicateMap.ContainsKey(sprite.AssetPath))
            {
                continue;
            }

            string targetFolder = GetCategoryFolder(plan.SourceFolder, sprite.Category);
            string targetPath = GetSafeTargetAssetPath(sprite.AssetPath, targetFolder, plannedTargets);
            bool alreadyOk = IsSameAssetPath(sprite.AssetPath, targetPath);
            string action = alreadyOk ? "already_ok" : "move";
            string reason = sprite.Category == "Needs_Check" ? "No category keyword matched." : sprite.CategoryReason;

            plan.Entries.Add(new PlanEntry(sprite, targetPath, sprite.Category, action, reason));
        }

        Dictionary<string, string> finalPathByOriginalPath = plan.Entries.ToDictionary(
            entry => entry.OriginalPath,
            entry => entry.NewPath,
            StringComparer.OrdinalIgnoreCase);

        foreach (SpriteAssetInfo duplicate in duplicateMap.Values.OrderBy(sprite => sprite.AssetPath, StringComparer.OrdinalIgnoreCase))
        {
            string targetFolder = GetExactDuplicateFolder(plan.SourceFolder);
            string targetPath = GetSafeTargetAssetPath(duplicate.AssetPath, targetFolder, plannedTargets);
            SpriteAssetInfo kept = plan.DuplicateKeepers[duplicate.AssetPath];
            string keptFinalPath = finalPathByOriginalPath.TryGetValue(kept.AssetPath, out string finalPath)
                ? finalPath
                : kept.AssetPath;

            plan.Entries.Add(new PlanEntry(
                duplicate,
                targetPath,
                "_Duplicates/Exact",
                "duplicate_exact",
                "Exact SHA256 duplicate. Moved to _Duplicates/Exact without deletion."));

            plan.DuplicateRows.Add(new DuplicateRow(
                keptFinalPath,
                targetPath,
                "Same SHA256. Keeper selected by resolution, file size, then shorter cleaned file name.",
                duplicate.Sha256));
        }

        if (nearDuplicateReport)
        {
            BuildNearDuplicateRows(plan, sprites);
        }

        return plan;
    }

    private bool ValidateSourceFolder(OrganizationPlan plan)
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

    private List<SpriteAssetInfo> ScanSprites(OrganizationPlan plan)
    {
        List<SpriteAssetInfo> sprites = new List<SpriteAssetInfo>();
        string[] guids = AssetDatabase.FindAssets("t:Texture2D", new[] { plan.SourceFolder });

        foreach (string guid in guids)
        {
            string assetPath = NormalizeAssetPath(AssetDatabase.GUIDToAssetPath(guid));
            if (!assetPath.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (IsInSpecialOrganizerFolder(plan.SourceFolder, assetPath))
            {
                continue;
            }

            SpriteAssetInfo info = ReadSpriteInfo(assetPath);
            sprites.Add(info);
        }

        plan.ScannedCount = sprites.Count;
        return sprites.OrderBy(sprite => sprite.AssetPath, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private SpriteAssetInfo ReadSpriteInfo(string assetPath)
    {
        string fullPath = AssetPathToFullPath(assetPath);
        byte[] bytes = File.Exists(fullPath) ? File.ReadAllBytes(fullPath) : Array.Empty<byte>();
        GetPngDimensions(bytes, out int width, out int height);
        string sha256 = bytes.Length > 0 ? ComputeSha256(bytes) : string.Empty;
        long fileSize = bytes.LongLength;
        string category = Classify(assetPath, out string categoryReason);

        return new SpriteAssetInfo(assetPath, width, height, fileSize, sha256, category, categoryReason, bytes);
    }

    private Dictionary<string, SpriteAssetInfo> SelectExactDuplicates(OrganizationPlan plan, List<SpriteAssetInfo> sprites)
    {
        Dictionary<string, SpriteAssetInfo> duplicateMap = new Dictionary<string, SpriteAssetInfo>(StringComparer.OrdinalIgnoreCase);

        if (!exactDuplicateDetection)
        {
            return duplicateMap;
        }

        foreach (IGrouping<string, SpriteAssetInfo> group in sprites
                     .Where(sprite => !string.IsNullOrEmpty(sprite.Sha256))
                     .GroupBy(sprite => sprite.Sha256, StringComparer.OrdinalIgnoreCase)
                     .Where(group => group.Count() > 1))
        {
            SpriteAssetInfo keeper = group.OrderByDescending(sprite => sprite.PixelArea)
                .ThenByDescending(sprite => sprite.FileSize)
                .ThenBy(sprite => CleanFileNameScore(sprite.AssetPath))
                .ThenBy(sprite => sprite.AssetPath, StringComparer.OrdinalIgnoreCase)
                .First();

            foreach (SpriteAssetInfo duplicate in group)
            {
                if (duplicate == keeper)
                {
                    continue;
                }

                duplicateMap[duplicate.AssetPath] = duplicate;
                plan.DuplicateKeepers[duplicate.AssetPath] = keeper;
            }
        }

        return duplicateMap;
    }

    private void BuildNearDuplicateRows(OrganizationPlan plan, List<SpriteAssetInfo> sprites)
    {
        List<SpriteAssetInfo> hashableSprites = new List<SpriteAssetInfo>();
        foreach (SpriteAssetInfo sprite in sprites)
        {
            if (TryComputeAverageHash(sprite, out ulong averageHash))
            {
                sprite.AverageHash = averageHash;
                hashableSprites.Add(sprite);
            }
            else
            {
                plan.NearDuplicateRows.Add(new NearDuplicateRow(sprite.AssetPath, string.Empty, string.Empty, "Average hash could not be computed."));
            }
        }

        for (int i = 0; i < hashableSprites.Count; i++)
        {
            for (int j = i + 1; j < hashableSprites.Count; j++)
            {
                int distance = HammingDistance(hashableSprites[i].AverageHash, hashableSprites[j].AverageHash);
                if (distance <= NearDuplicateThreshold)
                {
                    plan.NearDuplicateRows.Add(new NearDuplicateRow(
                        hashableSprites[i].AssetPath,
                        hashableSprites[j].AssetPath,
                        distance.ToString(CultureInfo.InvariantCulture),
                        $"Average hash distance <= {NearDuplicateThreshold}. Candidate only; not moved."));
                }
            }
        }
    }

    private void ExecutePlan(OrganizationPlan plan)
    {
        if (plan.Errors.Count > 0)
        {
            foreach (string error in plan.Errors)
            {
                AddLog($"ERROR: {error}");
            }

            return;
        }

        EnsureFolders(plan.RequiredFolders);

        foreach (PlanEntry entry in plan.Entries)
        {
            if (entry.Action == "already_ok")
            {
                if (applyRecommendedImportSettings)
                {
                    ApplyImportSettings(entry.NewPath, entry.Category);
                }

                continue;
            }

            string currentPath = entry.OriginalPath;
            if (!AssetDatabase.LoadMainAssetAtPath(currentPath))
            {
                entry.Action = "error";
                entry.Reason = "Asset missing before move. Skipped.";
                plan.Errors.Add($"{currentPath}: asset missing before move.");
                continue;
            }

            string targetPath = EnsureUniqueTargetAtApplyTime(currentPath, entry.NewPath);
            string moveError = AssetDatabase.MoveAsset(currentPath, targetPath);
            if (!string.IsNullOrEmpty(moveError))
            {
                entry.Action = "error";
                entry.Reason = $"MoveAsset failed. {moveError}";
                plan.Errors.Add($"{currentPath}: {moveError}");
                continue;
            }

            entry.NewPath = targetPath;

            if (applyRecommendedImportSettings && entry.Action != "duplicate_exact")
            {
                ApplyImportSettings(targetPath, entry.Category);
            }
        }

        WriteReports(plan);
    }

    private void EnsureFolders(IEnumerable<string> folders)
    {
        foreach (string folder in folders)
        {
            EnsureFolder(folder);
        }
    }

    private void EnsureFolder(string assetFolder)
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

    private void ApplyImportSettings(string assetPath, string category)
    {
        TextureImporter importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
        if (importer == null)
        {
            AddLog($"Import settings skipped; not a TextureImporter: {assetPath}");
            return;
        }

        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.alphaIsTransparency = true;
        importer.filterMode = FilterMode.Bilinear;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.mipmapEnabled = false;
        importer.maxTextureSize = category == "ColorChips" ? 512 : 4096;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.spritePixelsPerUnit = 100f;

        TextureImporterSettings settings = new TextureImporterSettings();
        importer.ReadTextureSettings(settings);
        settings.spriteMeshType = SpriteMeshType.FullRect;
        importer.SetTextureSettings(settings);

        importer.SaveAndReimport();
    }

    private void WriteReports(OrganizationPlan plan)
    {
        string manifestFolder = GetManifestFolder(plan.SourceFolder);
        EnsureFolder(manifestFolder);

        WriteTextAsset(
            $"{manifestFolder}/organize_report.csv",
            BuildOrganizeReport(plan));

        WriteTextAsset(
            $"{manifestFolder}/duplicate_report.csv",
            BuildDuplicateReport(plan));

        WriteTextAsset(
            $"{manifestFolder}/near_duplicate_candidates.csv",
            BuildNearDuplicateReport(plan));

        WriteTextAsset(
            $"{manifestFolder}/needs_check_report.csv",
            BuildNeedsCheckReport(plan));

        WriteTextAsset(
            $"{manifestFolder}/README_UI_ORGANIZED.md",
            BuildReadme());
    }

    private static string BuildOrganizeReport(OrganizationPlan plan)
    {
        StringBuilder builder = new StringBuilder();
        builder.AppendLine("original_path,new_path,category,action,reason,width,height,file_size,sha256");

        foreach (PlanEntry entry in plan.Entries.OrderBy(entry => entry.OriginalPath, StringComparer.OrdinalIgnoreCase))
        {
            builder.AppendCsvLine(
                entry.OriginalPath,
                entry.NewPath,
                entry.Category,
                entry.Action,
                entry.Reason,
                entry.Width.ToString(CultureInfo.InvariantCulture),
                entry.Height.ToString(CultureInfo.InvariantCulture),
                entry.FileSize.ToString(CultureInfo.InvariantCulture),
                entry.Sha256);
        }

        return builder.ToString();
    }

    private static string BuildDuplicateReport(OrganizationPlan plan)
    {
        StringBuilder builder = new StringBuilder();
        builder.AppendLine("kept_path,duplicate_path,reason,sha256");

        foreach (DuplicateRow row in plan.DuplicateRows.OrderBy(row => row.DuplicatePath, StringComparer.OrdinalIgnoreCase))
        {
            builder.AppendCsvLine(row.KeptPath, row.DuplicatePath, row.Reason, row.Sha256);
        }

        return builder.ToString();
    }

    private static string BuildNearDuplicateReport(OrganizationPlan plan)
    {
        StringBuilder builder = new StringBuilder();
        builder.AppendLine("file_a,file_b,distance_or_similarity,note");

        foreach (NearDuplicateRow row in plan.NearDuplicateRows.OrderBy(row => row.FileA, StringComparer.OrdinalIgnoreCase))
        {
            builder.AppendCsvLine(row.FileA, row.FileB, row.DistanceOrSimilarity, row.Note);
        }

        return builder.ToString();
    }

    private static string BuildNeedsCheckReport(OrganizationPlan plan)
    {
        StringBuilder builder = new StringBuilder();
        builder.AppendLine("path,reason,width,height,file_size");

        foreach (PlanEntry entry in plan.Entries
                     .Where(entry => entry.Category == "Needs_Check")
                     .OrderBy(entry => entry.NewPath, StringComparer.OrdinalIgnoreCase))
        {
            builder.AppendCsvLine(
                entry.NewPath,
                entry.Reason,
                entry.Width.ToString(CultureInfo.InvariantCulture),
                entry.Height.ToString(CultureInfo.InvariantCulture),
                entry.FileSize.ToString(CultureInfo.InvariantCulture));
        }

        return builder.ToString();
    }

    private static string BuildReadme()
    {
        return
            "# Project ONE UI Sprites Organized\n\n" +
            "## Folder Structure\n" +
            "- `Buttons`: Button sprites such as start, retry, home, back, and ready buttons.\n" +
            "- `Panels`: Panels, cards, frames, labels, banners, notes, boxes, and containers.\n" +
            "- `HUD`: In-game HUD sprites such as stamina, timer, mission, reward, status, progress, gauges, and bars.\n" +
            "- `MainMenu`: Main menu, quick start, custom game, tutorial, quit, and menu panel sprites.\n" +
            "- `Logo_Title`: Logo, title, project, subtitle, and slogan sprites.\n" +
            "- `Lobby_CharacterSelect`: Lobby, room code, ready, character select, select, and help sprites.\n" +
            "- `Result_Ranking`: Result, victory, ranking, medal, score, stamp, success, crown tab, and participant sprites.\n" +
            "- `Icons`: Icon sprites such as coin, clock, settings, sound, notice, plus, book, gamepad, power, and heart.\n" +
            "- `Decorations`: Tape, clip, pin, star, ribbon, sticker, folded corner, tag decoration, and decorative sprites.\n" +
            "- `Characters`: Hamster, mascot, avatar, character, face, and profile sprites.\n" +
            "- `ColorChips`: Color chip, swatch, palette, and named color reference sprites.\n" +
            "- `Needs_Check`: Sprites that did not clearly match a category keyword and require manual review.\n" +
            "- `_Duplicates/Exact`: Exact byte-for-byte SHA256 duplicates. These files are kept, not deleted.\n" +
            "- `_Duplicates/Near_Candidates`: Reserved folder for manually reviewing near duplicate candidates. Near duplicates are reported only and are not moved automatically.\n" +
            "- `_Manifest`: CSV reports and this README.\n\n" +
            "## Unity Import Settings\n" +
            "When `Apply Recommended Import Settings` is enabled, organized PNGs are imported as Sprite (2D and UI), Sprite Mode Single, Alpha Is Transparency enabled, Full Rect mesh, Bilinear filter, Clamp wrap, no mip maps, uncompressed texture compression, Sprite Pixels Per Unit 100, and Max Size 4096. `ColorChips` may use Max Size 512.\n\n" +
            "## Duplicates\n" +
            "Exact duplicates are detected by comparing PNG file bytes with SHA256. The selected keeper remains in its recommended category, and the other identical files are moved to `_Duplicates/Exact` using `AssetDatabase.MoveAsset` so Unity `.meta` GUID references stay intact.\n\n" +
            "## Needs Check\n" +
            "`Needs_Check` is intentionally conservative. Files in this folder were not strongly classified by the configured filename keywords and should be reviewed by a person before being used or renamed.\n\n" +
            "## 9-Slice Reminder\n" +
            "Buttons and panels may still need manual Sprite Editor setup. If a sprite should scale as UI chrome, configure its 9-slice Border in Unity's Sprite Editor after organization.\n";
    }

    private static void WriteTextAsset(string assetPath, string content)
    {
        string fullPath = AssetPathToFullPath(assetPath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath));
        File.WriteAllText(fullPath, content, Encoding.UTF8);
        AssetDatabase.ImportAsset(assetPath);
    }

    private void AddPlanPreview(OrganizationPlan plan)
    {
        if (plan.Errors.Count > 0)
        {
            foreach (string error in plan.Errors)
            {
                AddLog($"ERROR: {error}");
            }

            return;
        }

        foreach (PlanEntry entry in plan.Entries)
        {
            string previewLine = $"{entry.Action}: {entry.OriginalPath} -> {entry.NewPath} ({entry.Category})";
            AddLog(previewLine);
            Debug.Log($"Project ONE UI Sprite Organizer: {previewLine}");
        }
    }

    private void LogSummary(OrganizationPlan plan, string label)
    {
        string summary =
            $"Project ONE UI Sprite Organizer {label} Summary | " +
            $"scanned count: {plan.ScannedCount}, " +
            $"moved count: {plan.MovedCount}, " +
            $"already ok count: {plan.AlreadyOkCount}, " +
            $"exact duplicate count: {plan.ExactDuplicateCount}, " +
            $"needs check count: {plan.NeedsCheckCount}, " +
            $"error count: {plan.Errors.Count}";

        AddLog(summary);
        Debug.Log(summary);

        foreach (string error in plan.Errors)
        {
            Debug.LogWarning($"Project ONE UI Sprite Organizer: {error}");
        }
    }

    private void AddLog(string message)
    {
        logLines.Add(message);
        Repaint();
    }

    private static string Classify(string assetPath, out string reason)
    {
        string fileName = Path.GetFileNameWithoutExtension(assetPath);
        string searchable = fileName.ToLowerInvariant();

        foreach (string category in CategoryOrder)
        {
            if (category == "Needs_Check")
            {
                continue;
            }

            foreach (string keyword in CategoryKeywords[category])
            {
                string normalizedKeyword = keyword.ToLowerInvariant();
                if (searchable.Contains(normalizedKeyword))
                {
                    reason = $"Matched keyword '{keyword}'.";
                    return category;
                }
            }
        }

        reason = "No category keyword matched.";
        return "Needs_Check";
    }

    private static bool IsInSpecialOrganizerFolder(string sourceRoot, string assetPath)
    {
        string relative = GetRelativeAssetPath(sourceRoot, assetPath);
        return relative.StartsWith("_Manifest/", StringComparison.OrdinalIgnoreCase) ||
               relative.StartsWith("_Duplicates/", StringComparison.OrdinalIgnoreCase);
    }

    private static string GetSafeTargetAssetPath(string sourceAssetPath, string targetFolder, HashSet<string> plannedTargets)
    {
        targetFolder = NormalizeAssetPath(targetFolder);
        string fileName = Path.GetFileName(sourceAssetPath);
        string candidate = NormalizeAssetPath($"{targetFolder}/{fileName}");

        if (IsSameAssetPath(sourceAssetPath, candidate))
        {
            plannedTargets.Add(candidate);
            return candidate;
        }

        return MakeUniqueAssetPath(candidate, plannedTargets, sourceAssetPath);
    }

    private static string EnsureUniqueTargetAtApplyTime(string sourceAssetPath, string plannedTarget)
    {
        if (IsSameAssetPath(sourceAssetPath, plannedTarget))
        {
            return plannedTarget;
        }

        HashSet<string> empty = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return MakeUniqueAssetPath(plannedTarget, empty, sourceAssetPath);
    }

    private static string MakeUniqueAssetPath(string desiredPath, HashSet<string> plannedTargets, string sourceAssetPath)
    {
        string normalizedDesiredPath = NormalizeAssetPath(desiredPath);
        string directory = NormalizeAssetPath(Path.GetDirectoryName(normalizedDesiredPath));
        string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(normalizedDesiredPath);
        string extension = Path.GetExtension(normalizedDesiredPath);
        string candidate = normalizedDesiredPath;
        int suffix = 1;

        while (plannedTargets.Contains(candidate) ||
               (!IsSameAssetPath(sourceAssetPath, candidate) && AssetDatabase.LoadMainAssetAtPath(candidate) != null))
        {
            candidate = NormalizeAssetPath($"{directory}/{fileNameWithoutExtension}_{suffix:000}{extension}");
            suffix++;
        }

        plannedTargets.Add(candidate);
        return candidate;
    }

    private static string GetCategoryFolder(string sourceRoot, string category)
    {
        return NormalizeAssetPath($"{sourceRoot}/{category}");
    }

    private static string GetExactDuplicateFolder(string sourceRoot)
    {
        return NormalizeAssetPath($"{sourceRoot}/_Duplicates/Exact");
    }

    private static string GetNearDuplicateFolder(string sourceRoot)
    {
        return NormalizeAssetPath($"{sourceRoot}/_Duplicates/Near_Candidates");
    }

    private static string GetManifestFolder(string sourceRoot)
    {
        return NormalizeAssetPath($"{sourceRoot}/_Manifest");
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

    private static bool IsSameAssetPath(string left, string right)
    {
        return string.Equals(NormalizeAssetPath(left), NormalizeAssetPath(right), StringComparison.OrdinalIgnoreCase);
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

    private static string ComputeSha256(byte[] bytes)
    {
        using (SHA256 sha256 = SHA256.Create())
        {
            byte[] hash = sha256.ComputeHash(bytes);
            StringBuilder builder = new StringBuilder(hash.Length * 2);
            foreach (byte value in hash)
            {
                builder.Append(value.ToString("x2", CultureInfo.InvariantCulture));
            }

            return builder.ToString();
        }
    }

    private static void GetPngDimensions(byte[] bytes, out int width, out int height)
    {
        width = 0;
        height = 0;

        if (bytes.Length < 24 ||
            bytes[0] != 0x89 ||
            bytes[1] != 0x50 ||
            bytes[2] != 0x4E ||
            bytes[3] != 0x47)
        {
            return;
        }

        width = ReadBigEndianInt32(bytes, 16);
        height = ReadBigEndianInt32(bytes, 20);
    }

    private static int ReadBigEndianInt32(byte[] bytes, int offset)
    {
        return (bytes[offset] << 24) |
               (bytes[offset + 1] << 16) |
               (bytes[offset + 2] << 8) |
               bytes[offset + 3];
    }

    private static bool TryComputeAverageHash(SpriteAssetInfo sprite, out ulong hash)
    {
        hash = 0UL;

        if (sprite.Bytes == null || sprite.Bytes.Length == 0)
        {
            return false;
        }

        Texture2D texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        try
        {
            if (!ImageConversion.LoadImage(texture, sprite.Bytes, false))
            {
                return false;
            }

            Color32[] pixels = texture.GetPixels32();
            if (pixels.Length == 0 || texture.width <= 0 || texture.height <= 0)
            {
                return false;
            }

            double[] buckets = new double[64];
            int[] counts = new int[64];

            for (int y = 0; y < texture.height; y++)
            {
                int bucketY = Mathf.Min(7, y * 8 / texture.height);
                for (int x = 0; x < texture.width; x++)
                {
                    int bucketX = Mathf.Min(7, x * 8 / texture.width);
                    int bucketIndex = bucketY * 8 + bucketX;
                    Color32 pixel = pixels[y * texture.width + x];
                    buckets[bucketIndex] += (0.299 * pixel.r) + (0.587 * pixel.g) + (0.114 * pixel.b);
                    counts[bucketIndex]++;
                }
            }

            double average = 0.0;
            for (int i = 0; i < buckets.Length; i++)
            {
                if (counts[i] > 0)
                {
                    buckets[i] /= counts[i];
                }

                average += buckets[i];
            }

            average /= buckets.Length;

            for (int i = 0; i < buckets.Length; i++)
            {
                if (buckets[i] >= average)
                {
                    hash |= 1UL << i;
                }
            }

            return true;
        }
        finally
        {
            DestroyImmediate(texture);
        }
    }

    private static int HammingDistance(ulong left, ulong right)
    {
        ulong value = left ^ right;
        int count = 0;
        while (value != 0)
        {
            count++;
            value &= value - 1;
        }

        return count;
    }

    private static int CleanFileNameScore(string assetPath)
    {
        string name = Path.GetFileNameWithoutExtension(assetPath);
        int score = name.Length;
        foreach (char character in name)
        {
            if (character == ' ' || character == '(' || character == ')' || character == '[' || character == ']')
            {
                score += 2;
            }
        }

        return score;
    }

    private sealed class OrganizationPlan
    {
        public readonly string SourceFolder;
        public readonly bool ApplyImportSettings;
        public readonly bool ExactDuplicateDetection;
        public readonly bool NearDuplicateReport;
        public readonly List<string> RequiredFolders = new List<string>();
        public readonly List<PlanEntry> Entries = new List<PlanEntry>();
        public readonly List<DuplicateRow> DuplicateRows = new List<DuplicateRow>();
        public readonly List<NearDuplicateRow> NearDuplicateRows = new List<NearDuplicateRow>();
        public readonly List<string> Errors = new List<string>();
        public readonly Dictionary<string, SpriteAssetInfo> DuplicateKeepers = new Dictionary<string, SpriteAssetInfo>(StringComparer.OrdinalIgnoreCase);
        public int ScannedCount;

        public OrganizationPlan(string sourceFolder, bool applyImportSettings, bool exactDuplicateDetection, bool nearDuplicateReport)
        {
            SourceFolder = NormalizeAssetPath(sourceFolder);
            ApplyImportSettings = applyImportSettings;
            ExactDuplicateDetection = exactDuplicateDetection;
            NearDuplicateReport = nearDuplicateReport;
        }

        public int MovedCount => Entries.Count(entry => entry.Action == "move" || entry.Action == "duplicate_exact");
        public int AlreadyOkCount => Entries.Count(entry => entry.Action == "already_ok");
        public int ExactDuplicateCount => Entries.Count(entry => entry.Action == "duplicate_exact");
        public int NeedsCheckCount => Entries.Count(entry => entry.Category == "Needs_Check");
    }

    private sealed class SpriteAssetInfo
    {
        public readonly string AssetPath;
        public readonly int Width;
        public readonly int Height;
        public readonly long FileSize;
        public readonly string Sha256;
        public readonly string Category;
        public readonly string CategoryReason;
        public readonly byte[] Bytes;
        public ulong AverageHash;

        public SpriteAssetInfo(string assetPath, int width, int height, long fileSize, string sha256, string category, string categoryReason, byte[] bytes)
        {
            AssetPath = assetPath;
            Width = width;
            Height = height;
            FileSize = fileSize;
            Sha256 = sha256;
            Category = category;
            CategoryReason = categoryReason;
            Bytes = bytes;
        }

        public long PixelArea => (long)Width * Height;
    }

    private sealed class PlanEntry
    {
        public readonly string OriginalPath;
        public string NewPath;
        public string Category;
        public string Action;
        public string Reason;
        public readonly int Width;
        public readonly int Height;
        public readonly long FileSize;
        public readonly string Sha256;

        public PlanEntry(SpriteAssetInfo sprite, string newPath, string category, string action, string reason)
        {
            OriginalPath = sprite.AssetPath;
            NewPath = newPath;
            Category = category;
            Action = action;
            Reason = reason;
            Width = sprite.Width;
            Height = sprite.Height;
            FileSize = sprite.FileSize;
            Sha256 = sprite.Sha256;
        }
    }

    private sealed class DuplicateRow
    {
        public readonly string KeptPath;
        public readonly string DuplicatePath;
        public readonly string Reason;
        public readonly string Sha256;

        public DuplicateRow(string keptPath, string duplicatePath, string reason, string sha256)
        {
            KeptPath = keptPath;
            DuplicatePath = duplicatePath;
            Reason = reason;
            Sha256 = sha256;
        }
    }

    private sealed class NearDuplicateRow
    {
        public readonly string FileA;
        public readonly string FileB;
        public readonly string DistanceOrSimilarity;
        public readonly string Note;

        public NearDuplicateRow(string fileA, string fileB, string distanceOrSimilarity, string note)
        {
            FileA = fileA;
            FileB = fileB;
            DistanceOrSimilarity = distanceOrSimilarity;
            Note = note;
        }
    }
}

internal static class ProjectOneCsvExtensions
{
    public static void AppendCsvLine(this StringBuilder builder, params string[] values)
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
