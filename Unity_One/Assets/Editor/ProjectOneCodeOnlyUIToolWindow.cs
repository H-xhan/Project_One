#if UNITY_EDITOR
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

public sealed class ProjectOneCodeOnlyUIToolWindow : EditorWindow
{
    private const string ToolTitle = "ONE Code-Only UI Tool";
    private const string DefaultStyleFolder = "Assets/ProjectOneUI";
    private const string DefaultStylePath = DefaultStyleFolder + "/ProjectOneCodeOnlyUIStyle.asset";

    private ProjectOneCodeOnlyUIStyle style;
    private string defaultButtonText = "Ready";
    private string defaultTitleText = "캐릭터 선택";
    private string defaultBodyText = "모든 플레이어가 Ready를 누르면 게임이 시작돼요!";
    private bool useSelectionAsParent = true;
    private bool addDecorations = true;
    private Vector2 scroll;

    [MenuItem("Window/Project One/Code Only UI Tool")]
    public static void Open()
    {
        ProjectOneCodeOnlyUIToolWindow window = GetWindow<ProjectOneCodeOnlyUIToolWindow>();
        window.titleContent = new GUIContent(ToolTitle);
        window.minSize = new Vector2(380f, 620f);
        window.Show();
    }

    private void OnEnable()
    {
        if (style == null)
            style = FindFirstStyle();
    }

    private void OnGUI()
    {
        scroll = EditorGUILayout.BeginScrollView(scroll);

        EditorGUILayout.Space(8f);
        EditorGUILayout.LabelField("Project ONE Code-Only UI Tool", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Sprite/PNG/Image 리소스 없이 C# procedural mesh로 UGUI 패널, 버튼, HUD, 아이콘을 생성합니다.\n" +
            "폰트만 선택 사항이며, 종이 카드/버튼/아이콘/테이프는 모두 코드 도형입니다.", MessageType.Info);

        DrawStyleSection();
        DrawOptionsSection();
        DrawCanvasSection();
        DrawLayoutSection();
        DrawWidgetSection();

        EditorGUILayout.EndScrollView();
    }

    private void DrawStyleSection()
    {
        EditorGUILayout.Space(8f);
        EditorGUILayout.LabelField("1. Code-Only Style", EditorStyles.boldLabel);
        style = (ProjectOneCodeOnlyUIStyle)EditorGUILayout.ObjectField("Style", style, typeof(ProjectOneCodeOnlyUIStyle), false);

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Find Style"))
                style = FindFirstStyle();

            if (GUILayout.Button("Create Style"))
                style = CreateStyleAsset();
        }
    }

    private void DrawOptionsSection()
    {
        EditorGUILayout.Space(8f);
        EditorGUILayout.LabelField("2. Options", EditorStyles.boldLabel);
        defaultButtonText = EditorGUILayout.TextField("Button Text", defaultButtonText);
        defaultTitleText = EditorGUILayout.TextField("Title Text", defaultTitleText);
        defaultBodyText = EditorGUILayout.TextField("Body Text", defaultBodyText);
        useSelectionAsParent = EditorGUILayout.Toggle("Use Selection As Parent", useSelectionAsParent);
        addDecorations = EditorGUILayout.Toggle("Add Code Decorations", addDecorations);
    }

    private void DrawCanvasSection()
    {
        EditorGUILayout.Space(8f);
        EditorGUILayout.LabelField("3. Canvas", EditorStyles.boldLabel);
        if (GUILayout.Button("Create Code-Only Canvas 1920x1080"))
            FinalizeCreatedRoot(ProjectOneCodeOnlyUIFactory.CreateCanvas().GetComponent<RectTransform>(), "Create ONE Code-Only Canvas");
    }

    private void DrawLayoutSection()
    {
        EditorGUILayout.Space(8f);
        EditorGUILayout.LabelField("4. Code-Only Layouts", EditorStyles.boldLabel);
        if (GUILayout.Button("Create Main Menu Code-Only Layout"))
            CreateMainMenuLayout();
        if (GUILayout.Button("Create Lobby Code-Only Layout"))
            CreateLobbyLayout();
        if (GUILayout.Button("Create InGame HUD Code-Only Layout"))
            CreateInGameHudLayout();
        if (GUILayout.Button("Create Result Code-Only Layout"))
            CreateResultLayout();
    }

    private void DrawWidgetSection()
    {
        EditorGUILayout.Space(8f);
        EditorGUILayout.LabelField("5. Code-Only Widgets", EditorStyles.boldLabel);
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Code Button"))
                CreateWidgetButton(false);
            if (GUILayout.Button("Code Ready Button"))
                CreateWidgetButton(true);
        }
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Code Paper Panel"))
                CreateWidgetPanel();
            if (GUILayout.Button("Code Mission Card"))
                CreateWidgetMission();
        }
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Code HUD Counter"))
                CreateWidgetHud();
            if (GUILayout.Button("Code Stamina Gauge"))
                CreateWidgetStamina();
        }
    }

    private void CreateWidgetButton(bool ready)
    {
        RectTransform parent = ResolveParent();
        if (parent == null)
            return;
        RectTransform button = ProjectOneCodeOnlyUIFactory.CreateButton(
            parent,
            ready ? "ONE_CodeOnly_Button_Ready" : "ONE_CodeOnly_Button",
            ready ? (string.IsNullOrWhiteSpace(defaultButtonText) ? "Ready" : defaultButtonText) : defaultButtonText,
            Vector2.zero,
            ready ? new Vector2(520f, 140f) : new Vector2(380f, 96f),
            ready ? GetYellow() : GetPaperSoft(),
            ready ? ProjectOneProceduralIconGraphic.IconType.Paw : ProjectOneProceduralIconGraphic.IconType.Play,
            style,
            ready);

        if (ready && addDecorations)
            ProjectOneCodeOnlyUIFactory.CreateTape(button, "Code_Tape", new Vector2(140f, 58f), new Vector2(120f, 34f), -7f, GetBlue());

        FinalizeCreatedRoot(button, ready ? "Create ONE Code-Only Ready Button" : "Create ONE Code-Only Button");
    }

    private void CreateWidgetPanel()
    {
        RectTransform parent = ResolveParent();
        if (parent == null)
            return;
        RectTransform panel = ProjectOneCodeOnlyUIFactory.CreatePanel(parent, "ONE_CodeOnly_PaperPanel", defaultTitleText, defaultBodyText, Vector2.zero, new Vector2(640f, 420f), style, true);
        AddPanelDecorations(panel, new Vector2(640f, 420f));
        FinalizeCreatedRoot(panel, "Create ONE Code-Only Paper Panel");
    }

    private void CreateWidgetMission()
    {
        RectTransform parent = ResolveParent();
        if (parent == null)
            return;
        RectTransform card = CreateMissionCard(parent, Vector2.zero, new Vector2(440f, 520f));
        FinalizeCreatedRoot(card, "Create ONE Code-Only Mission Card");
    }

    private void CreateWidgetHud()
    {
        RectTransform parent = ResolveParent();
        if (parent == null)
            return;
        RectTransform hud = ProjectOneCodeOnlyUIFactory.CreateHudCounter(parent, "ONE_CodeOnly_HUD_Coin", "코인", "5", ProjectOneProceduralIconGraphic.IconType.Coin, Vector2.zero, new Vector2(400f, 112f), style);
        FinalizeCreatedRoot(hud, "Create ONE Code-Only HUD Counter");
    }

    private void CreateWidgetStamina()
    {
        RectTransform parent = ResolveParent();
        if (parent == null)
            return;
        RectTransform gauge = ProjectOneCodeOnlyUIFactory.CreateStaminaGauge(parent, Vector2.zero, new Vector2(650f, 132f), style);
        FinalizeCreatedRoot(gauge, "Create ONE Code-Only Stamina Gauge");
    }

    private void CreateMainMenuLayout()
    {
        RectTransform parent = ResolveParent();
        if (parent == null)
            return;

        RectTransform root = ProjectOneCodeOnlyUIFactory.CreateRoot(parent, "ONE_CodeOnly_MainMenuLayout");
        ProjectOneCodeOnlyUIFactory.Stretch(root);

        RectTransform left = ProjectOneCodeOnlyUIFactory.CreatePanel(root, "Left_MenuCard", string.Empty, string.Empty, new Vector2(-650f, -10f), new Vector2(390f, 680f), style, true);
        ProjectOneCodeOnlyUIFactory.SetBox(left, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-650f, -10f), new Vector2(390f, 680f));
        AddPanelDecorations(left, new Vector2(390f, 680f));

        CreateMenuButton(left, "Button_QuickStart", "빠른 시작", 210f, GetYellow(), ProjectOneProceduralIconGraphic.IconType.Play);
        CreateMenuButton(left, "Button_CustomGame", "커스텀 게임", 104f, GetPaperSoft(), ProjectOneProceduralIconGraphic.IconType.People);
        CreateMenuButton(left, "Button_Tutorial", "튜토리얼", -2f, GetPaperSoft(), ProjectOneProceduralIconGraphic.IconType.Paw);
        CreateMenuButton(left, "Button_Settings", "설정", -108f, GetPaperSoft(), ProjectOneProceduralIconGraphic.IconType.Dots);
        CreateMenuButton(left, "Button_Quit", "게임 종료", -214f, GetPaperSoft(), ProjectOneProceduralIconGraphic.IconType.Exit);

        RectTransform logo = ProjectOneCodeOnlyUIFactory.CreatePanel(root, "Logo_ProjectOne", "PROJECT\nONE", "작은 발, 큰 모험", new Vector2(0f, 225f), new Vector2(620f, 300f), style, false);
        if (addDecorations)
            ProjectOneCodeOnlyUIFactory.CreateTape(logo, "Code_BlueTape", new Vector2(0f, -154f), new Vector2(160f, 42f), 0f, GetBlue());

        RectTransform profile = ProjectOneCodeOnlyUIFactory.CreateHudCounter(root, "TopRight_Profile", "프로젝트원", "Lv.8    75%", ProjectOneProceduralIconGraphic.IconType.Paw, new Vector2(-40f, -56f), new Vector2(430f, 104f), style);
        ProjectOneCodeOnlyUIFactory.SetBox(profile, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-40f, -56f), new Vector2(430f, 104f));

        RectTransform coin = ProjectOneCodeOnlyUIFactory.CreateHudCounter(root, "TopRight_Coin", string.Empty, "125", ProjectOneProceduralIconGraphic.IconType.Coin, new Vector2(-40f, -184f), new Vector2(330f, 92f), style);
        ProjectOneCodeOnlyUIFactory.SetBox(coin, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-40f, -184f), new Vector2(330f, 92f));

        FinalizeCreatedRoot(root, "Create ONE Code-Only MainMenu Layout");
    }

    private void CreateLobbyLayout()
    {
        RectTransform parent = ResolveParent();
        if (parent == null)
            return;

        RectTransform root = ProjectOneCodeOnlyUIFactory.CreateRoot(parent, "ONE_CodeOnly_LobbyLayout");
        ProjectOneCodeOnlyUIFactory.Stretch(root);

        RectTransform room = ProjectOneCodeOnlyUIFactory.CreateHudCounter(root, "TopLeft_RoomCode", "ROOM CODE :", "RPMKJK", ProjectOneProceduralIconGraphic.IconType.Coin, new Vector2(40f, -42f), new Vector2(520f, 112f), style);
        ProjectOneCodeOnlyUIFactory.SetBox(room, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(40f, -42f), new Vector2(520f, 112f));

        RectTransform status = ProjectOneCodeOnlyUIFactory.CreateHudCounter(root, "TopCenter_Status", string.Empty, "준비를 눌러주세요", ProjectOneProceduralIconGraphic.IconType.Clock, new Vector2(0f, -42f), new Vector2(520f, 112f), style);
        ProjectOneCodeOnlyUIFactory.SetBox(status, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -42f), new Vector2(520f, 112f));

        RectTransform count = ProjectOneCodeOnlyUIFactory.CreateHudCounter(root, "TopRight_ReadyCount", string.Empty, "0/1 Ready", ProjectOneProceduralIconGraphic.IconType.People, new Vector2(-40f, -42f), new Vector2(430f, 112f), style);
        ProjectOneCodeOnlyUIFactory.SetBox(count, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-40f, -42f), new Vector2(430f, 112f));

        RectTransform character = CreateCharacterCard(root, new Vector2(0f, 20f), new Vector2(600f, 390f));
        ProjectOneCodeOnlyUIFactory.SetBox(character, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 20f), new Vector2(600f, 390f));

        RectTransform ready = ProjectOneCodeOnlyUIFactory.CreateButton(root, "Button_Ready", "Ready", new Vector2(-56f, 72f), new Vector2(520f, 140f), GetYellow(), ProjectOneProceduralIconGraphic.IconType.Paw, style, true);
        ProjectOneCodeOnlyUIFactory.SetBox(ready, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-56f, 72f), new Vector2(520f, 140f));
        if (addDecorations)
            ProjectOneCodeOnlyUIFactory.CreateTape(ready, "Code_BlueTape", new Vector2(140f, 58f), new Vector2(120f, 34f), -7f, GetBlue());

        RectTransform help = ProjectOneCodeOnlyUIFactory.CreateHudCounter(root, "BottomLeft_Help", "도움말", "모든 플레이어가 Ready를 누르면 게임이 시작돼요!", ProjectOneProceduralIconGraphic.IconType.Check, new Vector2(40f, 44f), new Vector2(560f, 92f), style);
        ProjectOneCodeOnlyUIFactory.SetBox(help, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(40f, 44f), new Vector2(560f, 92f));

        FinalizeCreatedRoot(root, "Create ONE Code-Only Lobby Layout");
    }

    private void CreateInGameHudLayout()
    {
        RectTransform parent = ResolveParent();
        if (parent == null)
            return;

        RectTransform root = ProjectOneCodeOnlyUIFactory.CreateRoot(parent, "ONE_CodeOnly_InGameHUDLayout");
        ProjectOneCodeOnlyUIFactory.Stretch(root);

        RectTransform coin = ProjectOneCodeOnlyUIFactory.CreateHudCounter(root, "TopLeft_CoinHUD", "코인", "5", ProjectOneProceduralIconGraphic.IconType.Coin, new Vector2(40f, -42f), new Vector2(400f, 112f), style);
        ProjectOneCodeOnlyUIFactory.SetBox(coin, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(40f, -42f), new Vector2(400f, 112f));

        RectTransform timer = ProjectOneCodeOnlyUIFactory.CreateHudCounter(root, "TopCenter_Timer", "남은 시간", "00:54", ProjectOneProceduralIconGraphic.IconType.Clock, new Vector2(0f, -42f), new Vector2(520f, 112f), style);
        ProjectOneCodeOnlyUIFactory.SetBox(timer, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -42f), new Vector2(520f, 112f));

        RectTransform mission = CreateMissionCard(root, new Vector2(-40f, -42f), new Vector2(440f, 520f));
        ProjectOneCodeOnlyUIFactory.SetBox(mission, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-40f, -42f), new Vector2(440f, 520f));

        RectTransform stamina = ProjectOneCodeOnlyUIFactory.CreateStaminaGauge(root, new Vector2(0f, 48f), new Vector2(650f, 132f), style);
        ProjectOneCodeOnlyUIFactory.SetBox(stamina, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 48f), new Vector2(650f, 132f));

        FinalizeCreatedRoot(root, "Create ONE Code-Only InGame HUD Layout");
    }

    private void CreateResultLayout()
    {
        RectTransform parent = ResolveParent();
        if (parent == null)
            return;

        RectTransform root = ProjectOneCodeOnlyUIFactory.CreateRoot(parent, "ONE_CodeOnly_ResultLayout");
        ProjectOneCodeOnlyUIFactory.Stretch(root);

        Image dim = root.gameObject.AddComponent<Image>();
        dim.color = new Color(0f, 0f, 0f, 0.35f);
        dim.raycastTarget = true;

        RectTransform board = ProjectOneCodeOnlyUIFactory.CreatePanel(root, "Result_Board", "승리!", "라운드 종료", new Vector2(0f, 45f), new Vector2(980f, 660f), style, true);
        ProjectOneCodeOnlyUIFactory.SetBox(board, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 45f), new Vector2(980f, 660f));

        RectTransform portrait = ProjectOneCodeOnlyUIFactory.CreatePanel(board, "BestPlayer_Card", string.Empty, "최고의 플레이어", new Vector2(-285f, -70f), new Vector2(320f, 410f), style, false);
        ProjectOneCodeOnlyUIFactory.CreateIcon(portrait, "Code_CharacterFace", ProjectOneProceduralIconGraphic.IconType.Paw, GetBlue(), GetNavy(), new Vector2(0f, 45f), new Vector2(210f, 210f));

        RectTransform summary = ProjectOneCodeOnlyUIFactory.CreatePanel(board, "CoinSummary", string.Empty, string.Empty, new Vector2(90f, 80f), new Vector2(360f, 210f), style, false);
        CreateRow(summary, "획득 코인", "+12", new Vector2(0f, 44f));
        CreateRow(summary, "총 코인", "17", new Vector2(0f, -44f));

        RectTransform ranking = ProjectOneCodeOnlyUIFactory.CreatePanel(board, "RankingList", "참가자 순위", string.Empty, new Vector2(335f, -70f), new Vector2(330f, 450f), style, false);
        for (int i = 0; i < 4; i++)
            CreateRankingRow(ranking, i);

        ProjectOneCodeOnlyUIFactory.CreateButton(root, "Button_Replay", "다시 하기", new Vector2(-360f, -425f), new Vector2(330f, 110f), new Color(0.80f, 0.91f, 1f, 1f), ProjectOneProceduralIconGraphic.IconType.Play, style, false);
        ProjectOneCodeOnlyUIFactory.CreateButton(root, "Button_Lobby", "로비로", new Vector2(0f, -425f), new Vector2(330f, 110f), GetYellow(), ProjectOneProceduralIconGraphic.IconType.Home, style, false);
        ProjectOneCodeOnlyUIFactory.CreateButton(root, "Button_Exit", "나가기", new Vector2(360f, -425f), new Vector2(330f, 110f), new Color(0.80f, 0.95f, 0.88f, 1f), ProjectOneProceduralIconGraphic.IconType.Exit, style, false);

        FinalizeCreatedRoot(root, "Create ONE Code-Only Result Layout");
    }

    private RectTransform CreateMissionCard(RectTransform parent, Vector2 position, Vector2 size)
    {
        RectTransform card = ProjectOneCodeOnlyUIFactory.CreatePanel(parent, "ONE_CodeOnly_SecretMission", string.Empty, string.Empty, position, size, style, true);
        RectTransform header = ProjectOneCodeOnlyUIFactory.CreatePanelShape(card, "Header_BlueTag", GetBlue(), Color.clear, 22f, 0f);
        ProjectOneCodeOnlyUIFactory.SetBox(header, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, 3f), new Vector2(size.x - 80f, 74f));
        ProjectOneCodeOnlyUIFactory.AddText(header, "HeaderText", "비밀 미션", GetSubtitleSize(), FontStyles.Bold, TextAlignmentOptions.Center, Color.white, style);

        ProjectOneCodeOnlyUIFactory.CreateIcon(card, "MissionIcon", ProjectOneProceduralIconGraphic.IconType.Paw, GetBlue(), GetNavy(), new Vector2(58f, -135f), new Vector2(62f, 62f), new Vector2(0f, 1f));
        TextMeshProUGUI title = ProjectOneCodeOnlyUIFactory.AddText(card, "MissionTitleText", "몰래 운반", GetSubtitleSize() + 8, FontStyles.Bold, TextAlignmentOptions.MidlineLeft, GetNavy(), style);
        title.rectTransform.anchorMin = new Vector2(0f, 1f);
        title.rectTransform.anchorMax = new Vector2(1f, 1f);
        title.rectTransform.offsetMin = new Vector2(96f, -178f);
        title.rectTransform.offsetMax = new Vector2(-38f, -92f);

        TextMeshProUGUI body = ProjectOneCodeOnlyUIFactory.AddText(card, "MissionBodyText", "종료 순간 노트북 구역 안에서\nitemId 2 아이템을\n들고 있으면 성공", GetBodySize(), FontStyles.Bold, TextAlignmentOptions.TopLeft, GetNavy(), style);
        body.textWrappingMode = TextWrappingModes.Normal;
        body.rectTransform.offsetMin = new Vector2(56f, 112f);
        body.rectTransform.offsetMax = new Vector2(-56f, -235f);

        RectTransform reward = ProjectOneCodeOnlyUIFactory.CreatePanelShape(card, "RewardBox", new Color(1f, 1f, 1f, 0.48f), Color.clear, 18f, 0f);
        ProjectOneCodeOnlyUIFactory.SetBox(reward, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 64f), new Vector2(size.x - 90f, 72f));
        ProjectOneCodeOnlyUIFactory.AddText(reward, "RewardText", "보상     +12 코인", GetBodySize(), FontStyles.Bold, TextAlignmentOptions.Center, GetNavy(), style);

        if (addDecorations)
            ProjectOneCodeOnlyUIFactory.CreateTape(card, "Code_BlueTape", new Vector2(size.x * 0.5f - 65f, -size.y * 0.5f + 35f), new Vector2(96f, 34f), -25f, GetBlue());

        return card;
    }

    private RectTransform CreateCharacterCard(RectTransform parent, Vector2 position, Vector2 size)
    {
        RectTransform card = ProjectOneCodeOnlyUIFactory.CreatePanel(parent, "ONE_CodeOnly_CharacterSelect", "캐릭터 선택", string.Empty, position, size, style, true);
        for (int i = 0; i < 3; i++)
        {
            float x = -170f + i * 170f;
            RectTransform slot = ProjectOneCodeOnlyUIFactory.CreatePanelShape(card, "CharacterSlot_" + (i + 1), i == 0 ? new Color(1f, 0.82f, 0.20f, 0.80f) : new Color(1f, 1f, 1f, 0.65f), GetLine(), 22f, 2f);
            ProjectOneCodeOnlyUIFactory.SetBox(slot, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(x, 28f), new Vector2(145f, 170f));
            slot.gameObject.AddComponent<Button>().transition = Selectable.Transition.None;
            ProjectOneCodeOnlyUIFactory.CreateIcon(slot, "CharacterFace", ProjectOneProceduralIconGraphic.IconType.Paw, GetBlue(), GetNavy(), new Vector2(0f, 26f), new Vector2(116f, 116f));
            TextMeshProUGUI name = ProjectOneCodeOnlyUIFactory.AddText(card, "CharacterName_" + (i + 1), i == 0 ? "기본" : (i == 1 ? "빨강" : "파랑"), GetSmallSize(), FontStyles.Bold, TextAlignmentOptions.Center, GetNavy(), style);
            ProjectOneCodeOnlyUIFactory.SetBox(name.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(x, 40f), new Vector2(130f, 42f));
        }
        return card;
    }

    private void CreateMenuButton(RectTransform parent, string name, string label, float y, Color fill, ProjectOneProceduralIconGraphic.IconType icon)
    {
        ProjectOneCodeOnlyUIFactory.CreateButton(parent, name, label, new Vector2(0f, y), new Vector2(306f, 76f), fill, icon, style, false);
    }

    private void CreateRow(RectTransform parent, string label, string value, Vector2 position)
    {
        RectTransform row = ProjectOneCodeOnlyUIFactory.CreatePanelShape(parent, "Row_" + label, new Color(1f, 1f, 1f, 0.32f), Color.clear, 16f, 0f);
        ProjectOneCodeOnlyUIFactory.SetBox(row, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), position, new Vector2(300f, 62f));
        TextMeshProUGUI l = ProjectOneCodeOnlyUIFactory.AddText(row, "Label", label, GetSmallSize(), FontStyles.Bold, TextAlignmentOptions.MidlineLeft, GetMutedNavy(), style);
        l.rectTransform.offsetMin = new Vector2(24f, 0f);
        l.rectTransform.offsetMax = new Vector2(-120f, 0f);
        TextMeshProUGUI v = ProjectOneCodeOnlyUIFactory.AddText(row, "Value", value, GetSubtitleSize(), FontStyles.Bold, TextAlignmentOptions.MidlineRight, GetNavy(), style);
        v.rectTransform.offsetMin = new Vector2(120f, 0f);
        v.rectTransform.offsetMax = new Vector2(-24f, 0f);
    }

    private void CreateRankingRow(RectTransform parent, int index)
    {
        RectTransform row = ProjectOneCodeOnlyUIFactory.CreatePanelShape(parent, "RankingRow_" + (index + 1), index == 0 ? new Color(1f, 0.84f, 0.25f, 0.32f) : new Color(1f, 1f, 1f, 0.55f), Color.clear, 14f, 0f);
        ProjectOneCodeOnlyUIFactory.SetBox(row, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -110f - index * 82f), new Vector2(285f, 64f));
        ProjectOneCodeOnlyUIFactory.AddText(row, "Label", (index + 1) + "   플레이어 " + (index + 1) + "        " + (87 - index * 19), 23, FontStyles.Bold, TextAlignmentOptions.MidlineLeft, GetNavy(), style);
    }

    private void AddPanelDecorations(RectTransform panel, Vector2 size)
    {
        if (!addDecorations)
            return;
        ProjectOneCodeOnlyUIFactory.CreateTape(panel, "Code_Tape", new Vector2(0f, size.y * 0.5f - 10f), new Vector2(126f, 34f), 0f, GetBlue());
        ProjectOneCodeOnlyUIFactory.CreatePaperClip(panel, "Code_PaperClip", new Vector2(-size.x * 0.5f + 50f, size.y * 0.5f - 42f), -18f, GetBlue());
    }

    private RectTransform ResolveParent()
    {
        if (useSelectionAsParent)
        {
            GameObject selected = Selection.activeGameObject;
            if (selected != null && !EditorUtility.IsPersistent(selected))
            {
                RectTransform rect = selected.GetComponent<RectTransform>();
                Canvas canvas = selected.GetComponentInParent<Canvas>();
                if (rect != null && canvas != null)
                    return rect;
            }
        }

        Canvas canvasObject = ProjectOneCodeOnlyUIFactory.CreateCanvas();
        Undo.RegisterCreatedObjectUndo(canvasObject.gameObject, "Create ONE Code-Only Canvas");
        Selection.activeGameObject = canvasObject.gameObject;
        EditorSceneManager.MarkSceneDirty(canvasObject.gameObject.scene);
        return canvasObject.GetComponent<RectTransform>();
    }

    private void FinalizeCreatedRoot(RectTransform root, string undoName)
    {
        if (root == null)
            return;
        Undo.RegisterCreatedObjectUndo(root.gameObject, undoName);
        Selection.activeGameObject = root.gameObject;
        EditorSceneManager.MarkSceneDirty(root.gameObject.scene);
    }

    private ProjectOneCodeOnlyUIStyle FindFirstStyle()
    {
        string[] guids = AssetDatabase.FindAssets("t:ProjectOneCodeOnlyUIStyle");
        if (guids == null || guids.Length == 0)
            return null;
        string path = AssetDatabase.GUIDToAssetPath(guids[0]);
        return AssetDatabase.LoadAssetAtPath<ProjectOneCodeOnlyUIStyle>(path);
    }

    private ProjectOneCodeOnlyUIStyle CreateStyleAsset()
    {
        EnsureFolder(DefaultStyleFolder);
        ProjectOneCodeOnlyUIStyle newStyle = CreateInstance<ProjectOneCodeOnlyUIStyle>();
        string path = AssetDatabase.GenerateUniqueAssetPath(DefaultStylePath);
        AssetDatabase.CreateAsset(newStyle, path);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Selection.activeObject = newStyle;
        return newStyle;
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

    private Color GetPaperSoft() => style != null ? style.paperSoftColor : new Color(1.00f, 0.97f, 0.90f, 1.00f);
    private Color GetYellow() => style != null ? style.yellowColor : new Color(1.00f, 0.78f, 0.24f, 1.00f);
    private Color GetBlue() => style != null ? style.blueColor : new Color(0.32f, 0.62f, 0.95f, 1.00f);
    private Color GetNavy() => style != null ? style.navyColor : new Color(0.10f, 0.16f, 0.35f, 1.00f);
    private Color GetMutedNavy() => style != null ? style.mutedNavyColor : new Color(0.32f, 0.38f, 0.55f);
    private Color GetLine() => style != null ? style.lineColor : new Color(0.72f, 0.65f, 0.55f, 0.55f);
    private int GetSubtitleSize() => style != null ? style.subtitleSize : 30;
    private int GetBodySize() => style != null ? style.bodySize : 28;
    private int GetSmallSize() => style != null ? style.smallSize : 22;
}
#endif
