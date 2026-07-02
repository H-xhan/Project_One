#if UNITY_EDITOR
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public sealed class ProjectOneUIReleaseToolWindow : EditorWindow
{
    private const string ToolTitle = "ONE UI Release Tool";
    private const string DefaultPresetFolder = "Assets/ProjectOneUI";
    private const string DefaultPresetPath = DefaultPresetFolder + "/ProjectOneUIStylePreset.asset";

    private ProjectOneUIStylePreset style;
    private string defaultButtonText = "Ready";
    private string defaultTitleText = "캐릭터 선택";
    private string defaultBodyText = "모든 플레이어가 Ready를 누르면 게임이 시작돼요!";
    private bool attachAnimations = true;
    private bool addDecorations = true;
    private bool useSelectionAsParent = true;

    private Vector2 scroll;

    [MenuItem("Window/Project One/UI Release Tool")]
    public static void Open()
    {
        ProjectOneUIReleaseToolWindow window = GetWindow<ProjectOneUIReleaseToolWindow>();
        window.titleContent = new GUIContent(ToolTitle);
        window.minSize = new Vector2(380f, 620f);
        window.Show();
    }

    private void OnEnable()
    {
        if (style == null)
            style = FindFirstStylePreset();
    }

    private void OnGUI()
    {
        scroll = EditorGUILayout.BeginScrollView(scroll);

        DrawHeader();
        DrawPresetSection();
        DrawOptionsSection();
        DrawCanvasSection();
        DrawReleaseLayoutSection();
        DrawWidgetSection();
        DrawUtilitySection();

        EditorGUILayout.EndScrollView();
    }

    private void DrawHeader()
    {
        EditorGUILayout.Space(8f);
        EditorGUILayout.LabelField("Project ONE Release UI Tool", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "현재 레퍼런스의 종이 카드, 테이프, 클립, 노란 CTA, HUD pill 스타일을 UGUI 오브젝트로 빠르게 생성하는 내부 툴입니다.\n" +
            "권장 흐름: Canvas 선택 → Layout 또는 Widget 생성 → Sprite/Font preset 연결 → Play Mode에서 애니메이션 확인.",
            MessageType.Info);
    }

    private void DrawPresetSection()
    {
        EditorGUILayout.Space(8f);
        EditorGUILayout.LabelField("1. Style Preset", EditorStyles.boldLabel);

        style = (ProjectOneUIStylePreset)EditorGUILayout.ObjectField("Style Preset", style, typeof(ProjectOneUIStylePreset), false);

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Find Preset"))
            {
                style = FindFirstStylePreset();
                if (style == null)
                    Debug.LogWarning("[ProjectOneUIReleaseTool] ProjectOneUIStylePreset asset을 찾지 못했습니다.");
            }

            if (GUILayout.Button("Create Preset"))
            {
                style = CreateStylePresetAsset();
            }
        }

        using (new EditorGUI.DisabledScope(style == null))
        {
            if (GUILayout.Button("Auto Bind Sprites From Project"))
            {
                AutoBindStylePreset(style);
            }
        }
    }

    private void DrawOptionsSection()
    {
        EditorGUILayout.Space(8f);
        EditorGUILayout.LabelField("2. Options", EditorStyles.boldLabel);

        defaultButtonText = EditorGUILayout.TextField("Button Text", defaultButtonText);
        defaultTitleText = EditorGUILayout.TextField("Title Text", defaultTitleText);
        defaultBodyText = EditorGUILayout.TextField("Body Text", defaultBodyText);
        attachAnimations = EditorGUILayout.Toggle("Attach Animations", attachAnimations);
        addDecorations = EditorGUILayout.Toggle("Add Tape / Clip", addDecorations);
        useSelectionAsParent = EditorGUILayout.Toggle("Use Selection As Parent", useSelectionAsParent);
    }

    private void DrawCanvasSection()
    {
        EditorGUILayout.Space(8f);
        EditorGUILayout.LabelField("3. Canvas", EditorStyles.boldLabel);

        if (GUILayout.Button("Create Release Canvas 1920x1080"))
            CreateReleaseCanvas();
    }

    private void DrawReleaseLayoutSection()
    {
        EditorGUILayout.Space(8f);
        EditorGUILayout.LabelField("4. Release Layouts", EditorStyles.boldLabel);

        if (GUILayout.Button("Create Main Menu Release Layout"))
            CreateMainMenuReleaseLayout();

        if (GUILayout.Button("Create Lobby Release Layout"))
            CreateLobbyReleaseLayout();

        if (GUILayout.Button("Create InGame HUD Release Layout"))
            CreateInGameHudReleaseLayout();

        if (GUILayout.Button("Create Result Release Layout"))
            CreateResultReleaseLayout();
    }

    private void DrawWidgetSection()
    {
        EditorGUILayout.Space(8f);
        EditorGUILayout.LabelField("5. Widgets", EditorStyles.boldLabel);

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Paper Button"))
                CreateWidgetPaperButton();

            if (GUILayout.Button("Ready Button"))
                CreateWidgetReadyButton();
        }

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Paper Panel"))
                CreateWidgetPaperPanel();

            if (GUILayout.Button("Mission Card"))
                CreateWidgetMissionCard();
        }

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("HUD Counter"))
                CreateWidgetHudCounter();

            if (GUILayout.Button("Timer Pill"))
                CreateWidgetTimerPill();
        }

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Stamina Gauge"))
                CreateWidgetStaminaGauge();

            if (GUILayout.Button("Character Card"))
                CreateWidgetCharacterCard();
        }
    }

    private void DrawUtilitySection()
    {
        EditorGUILayout.Space(8f);
        EditorGUILayout.LabelField("6. Utilities", EditorStyles.boldLabel);

        if (GUILayout.Button("Apply UI Image Defaults To Selection"))
            ApplyUiImageDefaultsToSelection();

        EditorGUILayout.HelpBox(
            "주의: 이 툴은 기존 MainMenu / Lobby / Network / Scene 전환 로직을 수정하지 않습니다. 생성된 UI는 배치용 뼈대이며, 실제 버튼 onClick과 데이터 바인딩은 기존 스크립트에서 연결하세요.",
            MessageType.Warning);
    }

    private void CreateWidgetPaperButton()
    {
        RectTransform parent = ResolveTargetParent();
        if (parent == null)
            return;

        RectTransform button = CreatePaperButton(parent, "ONE_Button_Paper", defaultButtonText, Vector2.zero, new Vector2(380f, 96f), style != null ? style.paperButton : null, GetColor(c => c.paperSoftColor), style != null ? style.playIcon : null, false);
        FinalizeCreatedRoot(button, "Create ONE Paper Button");
    }

    private void CreateWidgetReadyButton()
    {
        RectTransform parent = ResolveTargetParent();
        if (parent == null)
            return;

        RectTransform button = CreatePaperButton(parent, "ONE_Button_Ready", string.IsNullOrWhiteSpace(defaultButtonText) ? "Ready" : defaultButtonText, Vector2.zero, new Vector2(520f, 140f), style != null ? style.yellowButton : null, GetColor(c => c.yellowColor), style != null ? style.pawIcon : null, true);
        AddTape(button, new Vector2(120f, 34f), new Vector2(140f, 58f), -7f, style != null ? style.blueTape : null, GetColor(c => c.blueColor));
        FinalizeCreatedRoot(button, "Create ONE Ready Button");
    }

    private void CreateWidgetPaperPanel()
    {
        RectTransform parent = ResolveTargetParent();
        if (parent == null)
            return;

        RectTransform panel = CreatePaperPanel(parent, "ONE_Panel_Paper", defaultTitleText, defaultBodyText, Vector2.zero, new Vector2(640f, 420f));
        FinalizeCreatedRoot(panel, "Create ONE Paper Panel");
    }

    private void CreateWidgetMissionCard()
    {
        RectTransform parent = ResolveTargetParent();
        if (parent == null)
            return;

        RectTransform card = CreateMissionCard(parent, Vector2.zero, new Vector2(440f, 520f));
        FinalizeCreatedRoot(card, "Create ONE Mission Card");
    }

    private void CreateWidgetHudCounter()
    {
        RectTransform parent = ResolveTargetParent();
        if (parent == null)
            return;

        RectTransform counter = CreateHudCounter(parent, "ONE_HUD_CoinCounter", "코인", "5", style != null ? style.coinIcon : null, Vector2.zero, new Vector2(390f, 106f));
        FinalizeCreatedRoot(counter, "Create ONE HUD Counter");
    }

    private void CreateWidgetTimerPill()
    {
        RectTransform parent = ResolveTargetParent();
        if (parent == null)
            return;

        RectTransform timer = CreateTimerPill(parent, Vector2.zero, new Vector2(460f, 106f));
        FinalizeCreatedRoot(timer, "Create ONE Timer Pill");
    }

    private void CreateWidgetStaminaGauge()
    {
        RectTransform parent = ResolveTargetParent();
        if (parent == null)
            return;

        RectTransform gauge = CreateStaminaGauge(parent, Vector2.zero, new Vector2(650f, 132f));
        FinalizeCreatedRoot(gauge, "Create ONE Stamina Gauge");
    }

    private void CreateWidgetCharacterCard()
    {
        RectTransform parent = ResolveTargetParent();
        if (parent == null)
            return;

        RectTransform card = CreateCharacterSelectCard(parent, Vector2.zero, new Vector2(560f, 360f));
        FinalizeCreatedRoot(card, "Create ONE Character Card");
    }

    private void CreateMainMenuReleaseLayout()
    {
        RectTransform parent = ResolveTargetParent();
        if (parent == null)
            return;

        RectTransform root = CreateRoot(parent, "ONE_MainMenu_ReleaseLayout");
        Stretch(root);

        RectTransform leftCard = CreatePaperPanel(root, "Left_MenuCard", "", "", new Vector2(-650f, -10f), new Vector2(390f, 680f));
        SetBox(leftCard, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-650f, -10f), new Vector2(390f, 680f));
        AddClip(leftCard, new Vector2(-45f, 332f), 0f);
        AddTape(leftCard, new Vector2(130f, 34f), new Vector2(40f, -330f), -5f, style != null ? style.blueTape : null, GetColor(c => c.mintColor));

        CreatePaperButton(leftCard, "Button_QuickStart", "빠른 시작", new Vector2(0f, 210f), new Vector2(306f, 76f), style != null ? style.yellowButton : null, GetColor(c => c.yellowColor), style != null ? style.playIcon : null, false);
        CreatePaperButton(leftCard, "Button_CustomGame", "커스텀 게임", new Vector2(0f, 104f), new Vector2(306f, 76f), style != null ? style.paperButton : null, GetColor(c => c.paperSoftColor), style != null ? style.peopleIcon : null, false);
        CreatePaperButton(leftCard, "Button_Tutorial", "튜토리얼", new Vector2(0f, -2f), new Vector2(306f, 76f), style != null ? style.paperButton : null, GetColor(c => c.paperSoftColor), style != null ? style.pawIcon : null, false);
        CreatePaperButton(leftCard, "Button_Settings", "설정", new Vector2(0f, -108f), new Vector2(306f, 76f), style != null ? style.paperButton : null, GetColor(c => c.paperSoftColor), style != null ? style.missionIcon : null, false);
        CreatePaperButton(leftCard, "Button_Quit", "게임 종료", new Vector2(0f, -214f), new Vector2(306f, 76f), style != null ? style.paperButton : null, GetColor(c => c.paperSoftColor), style != null ? style.exitIcon : null, false);

        RectTransform logo = CreatePaperPanel(root, "Logo_ProjectOne", "PROJECT\nONE", "작은 발, 큰 모험", new Vector2(0f, 225f), new Vector2(620f, 300f));
        AddTape(logo, new Vector2(160f, 42f), new Vector2(0f, -154f), 0f, style != null ? style.blueTape : null, GetColor(c => c.blueColor));

        RectTransform profile = CreateHudCounter(root, "TopRight_Profile", "프로젝트원", "Lv.8    75%", style != null ? style.defaultCharacterPortrait : null, new Vector2(-40f, -56f), new Vector2(430f, 104f));
        SetBox(profile, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-40f, -56f), new Vector2(430f, 104f));

        RectTransform coin = CreateHudCounter(root, "TopRight_Coin", "", "125", style != null ? style.coinIcon : null, new Vector2(-40f, -184f), new Vector2(330f, 92f));
        SetBox(coin, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-40f, -184f), new Vector2(330f, 92f));

        RectTransform community = CreatePaperButton(root, "Button_Community", "커뮤니티", new Vector2(566f, -360f), new Vector2(170f, 150f), style != null ? style.paperButton : null, GetColor(c => c.paperSoftColor), style != null ? style.peopleIcon : null, false);
        SetBox(community, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-250f, 80f), new Vector2(170f, 150f));

        RectTransform notice = CreatePaperButton(root, "Button_Notice", "공지사항", new Vector2(760f, -360f), new Vector2(170f, 150f), style != null ? style.paperButton : null, GetColor(c => c.paperSoftColor), style != null ? style.missionIcon : null, false);
        SetBox(notice, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-50f, 80f), new Vector2(170f, 150f));

        FinalizeCreatedRoot(root, "Create ONE MainMenu Release Layout");
    }

    private void CreateLobbyReleaseLayout()
    {
        RectTransform parent = ResolveTargetParent();
        if (parent == null)
            return;

        RectTransform root = CreateRoot(parent, "ONE_Lobby_ReleaseLayout");
        Stretch(root);

        RectTransform roomCode = CreateHudCounter(root, "TopLeft_RoomCode", "ROOM CODE :", "RPMKJK", style != null ? style.coinIcon : null, new Vector2(40f, -42f), new Vector2(520f, 112f));
        SetBox(roomCode, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(40f, -42f), new Vector2(520f, 112f));

        RectTransform status = CreateHudCounter(root, "TopCenter_Status", "", "준비를 눌러주세요", style != null ? style.clockIcon : null, new Vector2(0f, -42f), new Vector2(520f, 112f));
        SetBox(status, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -42f), new Vector2(520f, 112f));

        RectTransform readyCount = CreateHudCounter(root, "TopRight_ReadyCount", "", "0/1 Ready", style != null ? style.peopleIcon : null, new Vector2(-40f, -42f), new Vector2(430f, 112f));
        SetBox(readyCount, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-40f, -42f), new Vector2(430f, 112f));

        RectTransform character = CreateCharacterSelectCard(root, new Vector2(0f, 20f), new Vector2(600f, 390f));
        SetBox(character, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 20f), new Vector2(600f, 390f));

        RectTransform readyButton = CreatePaperButton(root, "Button_Ready", "Ready", new Vector2(-56f, 72f), new Vector2(520f, 140f), style != null ? style.yellowButton : null, GetColor(c => c.yellowColor), style != null ? style.pawIcon : null, true);
        SetBox(readyButton, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-56f, 72f), new Vector2(520f, 140f));
        AddTape(readyButton, new Vector2(120f, 34f), new Vector2(140f, 58f), -7f, style != null ? style.blueTape : null, GetColor(c => c.blueColor));

        RectTransform help = CreateHudCounter(root, "BottomLeft_Help", "도움말", "모든 플레이어가 Ready를 누르면 게임이 시작돼요!", style != null ? style.missionIcon : null, new Vector2(40f, 44f), new Vector2(560f, 92f));
        SetBox(help, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(40f, 44f), new Vector2(560f, 92f));

        FinalizeCreatedRoot(root, "Create ONE Lobby Release Layout");
    }

    private void CreateInGameHudReleaseLayout()
    {
        RectTransform parent = ResolveTargetParent();
        if (parent == null)
            return;

        RectTransform root = CreateRoot(parent, "ONE_InGameHUD_ReleaseLayout");
        Stretch(root);

        RectTransform coin = CreateHudCounter(root, "TopLeft_CoinHUD", "코인", "5", style != null ? style.coinIcon : null, new Vector2(40f, -42f), new Vector2(400f, 112f));
        SetBox(coin, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(40f, -42f), new Vector2(400f, 112f));

        RectTransform timer = CreateTimerPill(root, new Vector2(0f, -42f), new Vector2(520f, 112f));
        SetBox(timer, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -42f), new Vector2(520f, 112f));

        RectTransform mission = CreateMissionCard(root, new Vector2(-40f, -42f), new Vector2(440f, 520f));
        SetBox(mission, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-40f, -42f), new Vector2(440f, 520f));

        RectTransform stamina = CreateStaminaGauge(root, new Vector2(0f, 48f), new Vector2(650f, 132f));
        SetBox(stamina, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 48f), new Vector2(650f, 132f));

        FinalizeCreatedRoot(root, "Create ONE InGame HUD Release Layout");
    }

    private void CreateResultReleaseLayout()
    {
        RectTransform parent = ResolveTargetParent();
        if (parent == null)
            return;

        RectTransform root = CreateRoot(parent, "ONE_Result_ReleaseLayout");
        Stretch(root);

        Image dim = root.gameObject.AddComponent<Image>();
        ApplyImage(dim, null, new Color(0f, 0f, 0f, 0.35f), true, Image.Type.Simple);

        RectTransform board = CreatePaperPanel(root, "Result_Board", "승리!", "라운드 종료", new Vector2(0f, 45f), new Vector2(980f, 660f));
        SetBox(board, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 45f), new Vector2(980f, 660f));
        AddTape(board, new Vector2(160f, 42f), new Vector2(0f, 330f), 0f, style != null ? style.blueTape : null, GetColor(c => c.blueColor));

        RectTransform portraitCard = CreatePaperPanel(board, "BestPlayer_Card", "", "최고의 플레이어", new Vector2(-285f, -70f), new Vector2(320f, 410f));
        Image portrait = AddImage(portraitCard, "CharacterPortrait", style != null ? style.defaultCharacterPortrait : null, Color.white, false, Image.Type.Simple);
        SetBox(portrait.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 45f), new Vector2(250f, 250f));

        RectTransform summary = CreatePaperPanel(board, "CoinSummary", "", "", new Vector2(90f, 80f), new Vector2(360f, 210f));
        CreateRowText(summary, "획득 코인", "+12", new Vector2(0f, 44f));
        CreateRowText(summary, "총 코인", "17", new Vector2(0f, -44f));

        RectTransform mission = CreatePaperPanel(board, "SecretMissionResult", "비밀 미션", "몰래 운반\n들키지 않고 물건을 목적지로 운반", new Vector2(90f, -155f), new Vector2(360f, 240f));
        RectTransform stamp = AddImage(mission, "SuccessStamp", style != null ? style.successStamp : null, GetColor(c => c.mintColor), false, Image.Type.Simple).rectTransform;
        SetBox(stamp, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-34f, 28f), new Vector2(104f, 104f));

        RectTransform ranking = CreatePaperPanel(board, "RankingList", "참가자 순위", "", new Vector2(335f, -70f), new Vector2(330f, 450f));
        for (int i = 0; i < 4; i++)
        {
            RectTransform row = AddImage(ranking, "RankingRow_" + (i + 1), null, i == 0 ? new Color(1f, 0.84f, 0.25f, 0.32f) : new Color(1f, 1f, 1f, 0.55f), false, Image.Type.Simple).rectTransform;
            SetBox(row, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -110f - i * 82f), new Vector2(285f, 64f));
            AddText(row, "Label", (i + 1) + "   플레이어 " + (i + 1) + "        " + (87 - i * 19), 23, FontStyles.Bold, TextAlignmentOptions.MidlineLeft, GetColor(c => c.navyColor));
        }

        RectTransform replay = CreatePaperButton(root, "Button_Replay", "다시 하기", new Vector2(-360f, -425f), new Vector2(330f, 110f), style != null ? style.paperButton : null, new Color(0.80f, 0.91f, 1f, 1f), style != null ? style.playIcon : null, false);
        RectTransform lobby = CreatePaperButton(root, "Button_Lobby", "로비로", new Vector2(0f, -425f), new Vector2(330f, 110f), style != null ? style.yellowButton : null, GetColor(c => c.yellowColor), style != null ? style.homeIcon : null, false);
        RectTransform exit = CreatePaperButton(root, "Button_Exit", "나가기", new Vector2(360f, -425f), new Vector2(330f, 110f), style != null ? style.paperButton : null, new Color(0.80f, 0.95f, 0.88f, 1f), style != null ? style.exitIcon : null, false);
        _ = replay;
        _ = lobby;
        _ = exit;

        FinalizeCreatedRoot(root, "Create ONE Result Release Layout");
    }

    private RectTransform CreatePaperPanel(RectTransform parent, string name, string title, string body, Vector2 position, Vector2 size)
    {
        RectTransform panel = CreateRoot(parent, name);
        SetBox(panel, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), position, size);

        Image bg = panel.gameObject.AddComponent<Image>();
        ApplyImage(bg, style != null ? style.paperPanelLarge : null, GetColor(c => c.paperColor), false, Image.Type.Sliced);
        AddShadow(panel.gameObject, new Vector2(0f, -6f), 0.22f);

        CanvasGroup group = panel.gameObject.AddComponent<CanvasGroup>();
        _ = group;
        AddReleaseAnimator(panel, false);

        if (addDecorations)
        {
            AddTape(panel, new Vector2(126f, 34f), new Vector2(0f, size.y * 0.5f - 10f), 0f, style != null ? style.blueTape : null, GetColor(c => c.blueColor));
            AddClip(panel, new Vector2(-size.x * 0.5f + 50f, size.y * 0.5f - 42f), -18f);
        }

        if (!string.IsNullOrEmpty(title))
        {
            TextMeshProUGUI titleText = AddText(panel, "TitleText", title, GetInt(c => c.titleSize), FontStyles.Bold, TextAlignmentOptions.Center, GetColor(c => c.navyColor));
            SetBox(titleText.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -72f), new Vector2(size.x - 90f, 120f));
        }

        if (!string.IsNullOrEmpty(body))
        {
            TextMeshProUGUI bodyText = AddText(panel, "BodyText", body, GetInt(c => c.bodySize), FontStyles.Normal, TextAlignmentOptions.Center, GetColor(c => c.mutedNavyColor));
            bodyText.textWrappingMode = TextWrappingModes.Normal;
            SetBox(bodyText.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -76f), new Vector2(size.x - 110f, size.y - 220f));
        }

        RectTransform divider = AddImage(panel, "Divider", null, GetColor(c => c.lineColor), false, Image.Type.Simple).rectTransform;
        SetBox(divider, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -150f), new Vector2(size.x - 130f, 2f));

        return panel;
    }

    private RectTransform CreatePaperButton(RectTransform parent, string name, string label, Vector2 position, Vector2 size, Sprite sprite, Color color, Sprite icon, bool addAttentionWiggle)
    {
        RectTransform button = CreateRoot(parent, name);
        SetBox(button, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), position, size);

        Image bg = button.gameObject.AddComponent<Image>();
        ApplyImage(bg, sprite, color, true, Image.Type.Sliced);
        AddShadow(button.gameObject, new Vector2(0f, -5f), 0.20f);

        Button uiButton = button.gameObject.AddComponent<Button>();
        uiButton.transition = Selectable.Transition.None;

        AddReleaseAnimator(button, true);

        RectTransform iconBox = AddImage(button, "Icon", icon, icon == null ? GetColor(c => c.blueColor) : Color.white, false, Image.Type.Simple).rectTransform;
        SetBox(iconBox, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(42f, 0f), new Vector2(size.y * 0.46f, size.y * 0.46f));

        TextMeshProUGUI text = AddText(button, "Label_TMP", label, size.y > 115f ? GetInt(c => c.bigButtonSize) : GetInt(c => c.buttonSize), FontStyles.Bold, TextAlignmentOptions.Center, GetColor(c => c.navyColor));
        SetBox(text.rectTransform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        text.rectTransform.offsetMin = new Vector2(70f, 0f);
        text.rectTransform.offsetMax = new Vector2(-38f, 0f);

        RectTransform dot = AddImage(button, "Accent_Dot", null, GetColor(c => c.blueColor), false, Image.Type.Simple).rectTransform;
        SetBox(dot, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-36f, 0f), new Vector2(16f, 16f));

        if (addAttentionWiggle)
        {
            ProjectOneUIWiggle wiggle = button.gameObject.AddComponent<ProjectOneUIWiggle>();
            _ = wiggle;
        }

        return button;
    }

    private RectTransform CreateHudCounter(RectTransform parent, string name, string label, string value, Sprite icon, Vector2 position, Vector2 size)
    {
        RectTransform root = CreateRoot(parent, name);
        SetBox(root, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), position, size);

        Image bg = root.gameObject.AddComponent<Image>();
        ApplyImage(bg, style != null ? style.paperPanelSmall : null, GetColor(c => c.paperSoftColor), false, Image.Type.Sliced);
        AddShadow(root.gameObject, new Vector2(0f, -5f), 0.18f);
        root.gameObject.AddComponent<CanvasGroup>();
        AddReleaseAnimator(root, false);

        RectTransform iconBg = AddImage(root, "Icon_Backplate", null, new Color(1f, 0.86f, 0.32f, 0.95f), false, Image.Type.Simple).rectTransform;
        SetBox(iconBg, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(58f, 0f), new Vector2(size.y * 0.62f, size.y * 0.62f));

        RectTransform iconImage = AddImage(root, "Icon", icon, icon == null ? GetColor(c => c.navyColor) : Color.white, false, Image.Type.Simple).rectTransform;
        SetBox(iconImage, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(58f, 0f), new Vector2(size.y * 0.45f, size.y * 0.45f));

        TextMeshProUGUI labelText = AddText(root, "LabelText", label, GetInt(c => c.bodySize), FontStyles.Bold, TextAlignmentOptions.MidlineLeft, GetColor(c => c.navyColor));
        SetBox(labelText.rectTransform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        labelText.rectTransform.offsetMin = new Vector2(112f, 0f);
        labelText.rectTransform.offsetMax = new Vector2(-125f, 0f);

        TextMeshProUGUI valueText = AddText(root, "ValueText", value, GetInt(c => c.subtitleSize), FontStyles.Bold, TextAlignmentOptions.MidlineRight, GetColor(c => c.navyColor));
        SetBox(valueText.rectTransform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        valueText.rectTransform.offsetMin = new Vector2(145f, 0f);
        valueText.rectTransform.offsetMax = new Vector2(-34f, 0f);

        AddVerticalDots(root, new Vector2(size.x * 0.5f - 24f, 0f));

        return root;
    }

    private RectTransform CreateTimerPill(RectTransform parent, Vector2 position, Vector2 size)
    {
        RectTransform timer = CreateHudCounter(parent, "ONE_HUD_TimerPill", "남은 시간", "00:54", style != null ? style.clockIcon : null, position, size);
        return timer;
    }

    private RectTransform CreateMissionCard(RectTransform parent, Vector2 position, Vector2 size)
    {
        RectTransform card = CreateRoot(parent, "ONE_Card_SecretMission");
        SetBox(card, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), position, size);

        Image bg = card.gameObject.AddComponent<Image>();
        ApplyImage(bg, style != null ? style.paperPanelLarge : null, new Color(1f, 0.95f, 0.72f, 1f), false, Image.Type.Sliced);
        AddShadow(card.gameObject, new Vector2(0f, -6f), 0.20f);
        card.gameObject.AddComponent<CanvasGroup>();
        AddReleaseAnimator(card, false);

        RectTransform header = AddImage(card, "Header_BlueTag", style != null ? style.bluePillPanel : null, GetColor(c => c.blueColor), false, Image.Type.Sliced).rectTransform;
        SetBox(header, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, 3f), new Vector2(size.x - 80f, 74f));
        AddText(header, "HeaderText", "비밀 미션", GetInt(c => c.subtitleSize), FontStyles.Bold, TextAlignmentOptions.Center, Color.white);

        TextMeshProUGUI title = AddText(card, "MissionTitleText", "몰래 운반", GetInt(c => c.subtitleSize) + 8, FontStyles.Bold, TextAlignmentOptions.MidlineLeft, GetColor(c => c.navyColor));
        SetBox(title.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -122f), new Vector2(0f, 64f));
        title.rectTransform.offsetMin = new Vector2(96f, -178f);
        title.rectTransform.offsetMax = new Vector2(-38f, -92f);

        RectTransform face = AddImage(card, "MissionIcon", style != null ? style.defaultCharacterPortrait : null, Color.white, false, Image.Type.Simple).rectTransform;
        SetBox(face, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(58f, -135f), new Vector2(62f, 62f));

        RectTransform divider = AddImage(card, "Divider", null, GetColor(c => c.lineColor), false, Image.Type.Simple).rectTransform;
        SetBox(divider, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -204f), new Vector2(size.x - 82f, 2f));

        TextMeshProUGUI body = AddText(card, "MissionBodyText", "종료 순간 노트북 구역 안에서\nitemId 2 아이템을\n들고 있으면 성공", GetInt(c => c.bodySize), FontStyles.Bold, TextAlignmentOptions.TopLeft, GetColor(c => c.navyColor));
        body.textWrappingMode = TextWrappingModes.Normal;
        SetBox(body.rectTransform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        body.rectTransform.offsetMin = new Vector2(56f, 112f);
        body.rectTransform.offsetMax = new Vector2(-56f, -235f);

        RectTransform reward = AddImage(card, "RewardBox", null, new Color(1f, 1f, 1f, 0.48f), false, Image.Type.Simple).rectTransform;
        SetBox(reward, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 64f), new Vector2(size.x - 90f, 72f));
        AddText(reward, "RewardText", "보상     +12 코인", GetInt(c => c.bodySize), FontStyles.Bold, TextAlignmentOptions.Center, GetColor(c => c.navyColor));

        if (addDecorations)
        {
            AddTape(card, new Vector2(96f, 34f), new Vector2(size.x * 0.5f - 65f, -size.y * 0.5f + 35f), -25f, style != null ? style.blueTape : null, GetColor(c => c.blueColor));
        }

        return card;
    }

    private RectTransform CreateStaminaGauge(RectTransform parent, Vector2 position, Vector2 size)
    {
        RectTransform root = CreateRoot(parent, "ONE_HUD_StaminaGauge");
        SetBox(root, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), position, size);

        Image bg = root.gameObject.AddComponent<Image>();
        ApplyImage(bg, style != null ? style.paperPanelSmall : null, GetColor(c => c.paperSoftColor), false, Image.Type.Sliced);
        AddShadow(root.gameObject, new Vector2(0f, -5f), 0.18f);
        root.gameObject.AddComponent<CanvasGroup>();
        AddReleaseAnimator(root, false);

        RectTransform icon = AddImage(root, "EnergyIcon", null, GetColor(c => c.mintColor), false, Image.Type.Simple).rectTransform;
        SetBox(icon, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(70f, 0f), new Vector2(86f, 86f));
        AddText(icon, "EnergyText", "⚡", 42, FontStyles.Bold, TextAlignmentOptions.Center, GetColor(c => c.navyColor));

        TextMeshProUGUI label = AddText(root, "LabelText", "스태미너   100 / 100", GetInt(c => c.bodySize), FontStyles.Bold, TextAlignmentOptions.MidlineLeft, GetColor(c => c.navyColor));
        SetBox(label.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), Vector2.zero, Vector2.zero);
        label.rectTransform.offsetMin = new Vector2(132f, -66f);
        label.rectTransform.offsetMax = new Vector2(-40f, -18f);

        RectTransform barBg = AddImage(root, "Bar_Background", null, new Color(0.70f, 0.63f, 0.52f, 0.28f), false, Image.Type.Simple).rectTransform;
        SetBox(barBg, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f), Vector2.zero, Vector2.zero);
        barBg.offsetMin = new Vector2(132f, 30f);
        barBg.offsetMax = new Vector2(-52f, 62f);

        RectTransform fill = AddImage(barBg, "Bar_Fill", null, GetColor(c => c.mintColor), false, Image.Type.Simple).rectTransform;
        fill.anchorMin = new Vector2(0f, 0f);
        fill.anchorMax = new Vector2(1f, 1f);
        fill.pivot = new Vector2(0f, 0.5f);
        fill.offsetMin = Vector2.zero;
        fill.offsetMax = Vector2.zero;

        if (addDecorations)
            AddTape(root, new Vector2(110f, 30f), new Vector2(110f, size.y * 0.5f - 8f), 0f, style != null ? style.yellowTape : null, GetColor(c => c.yellowColor));

        return root;
    }

    private RectTransform CreateCharacterSelectCard(RectTransform parent, Vector2 position, Vector2 size)
    {
        RectTransform card = CreateRoot(parent, "ONE_Card_CharacterSelect");
        SetBox(card, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), position, size);

        Image bg = card.gameObject.AddComponent<Image>();
        ApplyImage(bg, style != null ? style.paperPanelLarge : null, GetColor(c => c.paperColor), false, Image.Type.Sliced);
        AddShadow(card.gameObject, new Vector2(0f, -6f), 0.20f);
        card.gameObject.AddComponent<CanvasGroup>();
        AddReleaseAnimator(card, false);

        TextMeshProUGUI title = AddText(card, "TitleText", "🐾  캐릭터 선택  🐾", GetInt(c => c.subtitleSize) + 2, FontStyles.Bold, TextAlignmentOptions.Center, GetColor(c => c.navyColor));
        SetBox(title.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -55f), new Vector2(size.x - 80f, 64f));

        for (int i = 0; i < 3; i++)
        {
            float x = -170f + i * 170f;
            RectTransform slot = AddImage(card, "CharacterSlot_" + (i + 1), null, i == 0 ? new Color(1f, 0.82f, 0.20f, 0.80f) : new Color(1f, 1f, 1f, 0.65f), true, Image.Type.Simple).rectTransform;
            SetBox(slot, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(x, 28f), new Vector2(145f, 170f));

            Image character = AddImage(slot, "CharacterImage", style != null ? style.defaultCharacterPortrait : null, Color.white, false, Image.Type.Simple);
            SetBox(character.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 26f), new Vector2(116f, 116f));

            TextMeshProUGUI name = AddText(card, "CharacterName_" + (i + 1), i == 0 ? "기본" : (i == 1 ? "빨강" : "파랑"), GetInt(c => c.smallSize), FontStyles.Bold, TextAlignmentOptions.Center, GetColor(c => c.navyColor));
            SetBox(name.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(x, 40f), new Vector2(130f, 42f));
        }

        if (addDecorations)
            AddClip(card, new Vector2(-size.x * 0.5f + 45f, size.y * 0.5f - 36f), -18f);

        return card;
    }

    private void CreateRowText(RectTransform parent, string label, string value, Vector2 position)
    {
        RectTransform row = AddImage(parent, "Row_" + label, null, new Color(1f, 1f, 1f, 0.32f), false, Image.Type.Simple).rectTransform;
        SetBox(row, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), position, new Vector2(300f, 62f));
        TextMeshProUGUI labelText = AddText(row, "Label", label, GetInt(c => c.smallSize), FontStyles.Bold, TextAlignmentOptions.MidlineLeft, GetColor(c => c.mutedNavyColor));
        labelText.rectTransform.offsetMin = new Vector2(24f, 0f);
        labelText.rectTransform.offsetMax = new Vector2(-120f, 0f);

        TextMeshProUGUI valueText = AddText(row, "Value", value, GetInt(c => c.subtitleSize), FontStyles.Bold, TextAlignmentOptions.MidlineRight, GetColor(c => c.navyColor));
        valueText.rectTransform.offsetMin = new Vector2(120f, 0f);
        valueText.rectTransform.offsetMax = new Vector2(-24f, 0f);
    }

    private void AddReleaseAnimator(RectTransform target, bool pointerAnimation)
    {
        if (!attachAnimations || target == null)
            return;

        ProjectOneUIReleaseAnimator animator = target.gameObject.AddComponent<ProjectOneUIReleaseAnimator>();
        if (style != null)
            animator.Configure(style.hoverScale, style.pressScale, style.scaleDuration, style.introDuration);

        animator.SetPointerAnimationEnabled(pointerAnimation);
    }

    private void AddTape(RectTransform parent, Vector2 size, Vector2 position, float zRotation, Sprite sprite, Color color)
    {
        if (!addDecorations || parent == null)
            return;

        RectTransform tape = AddImage(parent, "Deco_Tape", sprite, sprite == null ? new Color(color.r, color.g, color.b, 0.85f) : Color.white, false, Image.Type.Sliced).rectTransform;
        SetBox(tape, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), position, size);
        tape.localRotation = Quaternion.Euler(0f, 0f, zRotation);
    }

    private void AddClip(RectTransform parent, Vector2 position, float zRotation)
    {
        if (!addDecorations || parent == null)
            return;

        RectTransform clip = AddImage(parent, "Deco_PaperClip", style != null ? style.paperClip : null, Color.white, false, Image.Type.Simple).rectTransform;
        SetBox(clip, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), position, new Vector2(72f, 112f));
        clip.localRotation = Quaternion.Euler(0f, 0f, zRotation);
    }

    private void AddVerticalDots(RectTransform parent, Vector2 position)
    {
        for (int i = 0; i < 3; i++)
        {
            RectTransform dot = AddImage(parent, "MoreDot_" + i, null, GetColor(c => c.blueColor), false, Image.Type.Simple).rectTransform;
            SetBox(dot, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), position + new Vector2(0f, 18f - i * 18f), new Vector2(7f, 7f));
        }
    }

    private TextMeshProUGUI AddText(RectTransform parent, string name, string text, int fontSize, FontStyles fontStyle, TextAlignmentOptions alignment, Color color)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);

        TextMeshProUGUI tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = fontSize;
        tmp.fontStyle = fontStyle;
        tmp.alignment = alignment;
        tmp.color = color;
        tmp.raycastTarget = false;
        tmp.textWrappingMode = TextWrappingModes.NoWrap;

        if (style != null && style.mainFont != null)
            tmp.font = style.mainFont;

        Stretch(tmp.rectTransform);
        return tmp;
    }

    private Image AddImage(RectTransform parent, string name, Sprite sprite, Color color, bool raycastTarget, Image.Type preferredType)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);

        Image image = go.AddComponent<Image>();
        ApplyImage(image, sprite, color, raycastTarget, preferredType);
        Stretch(image.rectTransform);
        return image;
    }

    private void ApplyImage(Image image, Sprite sprite, Color color, bool raycastTarget, Image.Type preferredType)
    {
        image.sprite = sprite;
        image.color = color;
        image.raycastTarget = raycastTarget;
        image.preserveAspect = preferredType == Image.Type.Simple;
        image.type = sprite != null && HasBorder(sprite) ? preferredType : Image.Type.Simple;
    }

    private static bool HasBorder(Sprite sprite)
    {
        if (sprite == null)
            return false;

        Vector4 border = sprite.border;
        return border.x > 0f || border.y > 0f || border.z > 0f || border.w > 0f;
    }

    private void AddShadow(GameObject target, Vector2 distance, float alpha)
    {
        if (target == null)
            return;

        Shadow shadow = target.AddComponent<Shadow>();
        Color baseShadow = style != null ? style.shadowColor : new Color(0f, 0f, 0f, 0.25f);
        shadow.effectColor = new Color(baseShadow.r, baseShadow.g, baseShadow.b, alpha);
        shadow.effectDistance = distance;
        shadow.useGraphicAlpha = true;
    }

    private RectTransform ResolveTargetParent()
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

        return CreateReleaseCanvas();
    }

    private RectTransform CreateReleaseCanvas()
    {
        GameObject canvasObject = new GameObject("Canvas_ProjectOne_ReleaseUI", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        Undo.RegisterCreatedObjectUndo(canvasObject, "Create Project One Release Canvas");

        Canvas canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.pixelPerfect = false;
        canvas.sortingOrder = 0;

        CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;
        scaler.referencePixelsPerUnit = 100f;

        EnsureEventSystem();

        Selection.activeGameObject = canvasObject;
        EditorSceneManager.MarkSceneDirty(canvasObject.scene);
        return canvasObject.GetComponent<RectTransform>();
    }

    private void EnsureEventSystem()
    {
#if UNITY_2023_1_OR_NEWER
        EventSystem existing = Object.FindFirstObjectByType<EventSystem>();
#else
        EventSystem existing = Object.FindObjectOfType<EventSystem>();
#endif
        if (existing != null)
            return;

        GameObject eventSystem = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
        Undo.RegisterCreatedObjectUndo(eventSystem, "Create EventSystem");
    }

    private RectTransform CreateRoot(RectTransform parent, string name)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        RectTransform rect = go.GetComponent<RectTransform>();
        go.transform.SetParent(parent, false);
        return rect;
    }

    private void FinalizeCreatedRoot(RectTransform root, string undoName)
    {
        if (root == null)
            return;

        Undo.RegisterCreatedObjectUndo(root.gameObject, undoName);
        Selection.activeGameObject = root.gameObject;
        EditorSceneManager.MarkSceneDirty(root.gameObject.scene);
    }

    private static void SetBox(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 anchoredPosition, Vector2 size)
    {
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.pivot = pivot;
        rect.anchoredPosition = anchoredPosition;
        rect.sizeDelta = size;
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    private Color GetColor(System.Func<ProjectOneUIStylePreset, Color> selector)
    {
        if (style == null)
            return Color.white;

        return selector(style);
    }

    private int GetInt(System.Func<ProjectOneUIStylePreset, int> selector)
    {
        if (style == null)
            return 28;

        return selector(style);
    }

    private ProjectOneUIStylePreset FindFirstStylePreset()
    {
        string[] guids = AssetDatabase.FindAssets("t:ProjectOneUIStylePreset");
        if (guids == null || guids.Length == 0)
            return null;

        string path = AssetDatabase.GUIDToAssetPath(guids[0]);
        return AssetDatabase.LoadAssetAtPath<ProjectOneUIStylePreset>(path);
    }

    private ProjectOneUIStylePreset CreateStylePresetAsset()
    {
        EnsureFolder(DefaultPresetFolder);

        ProjectOneUIStylePreset newPreset = CreateInstance<ProjectOneUIStylePreset>();
        string path = AssetDatabase.GenerateUniqueAssetPath(DefaultPresetPath);
        AssetDatabase.CreateAsset(newPreset, path);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        AutoBindStylePreset(newPreset);
        Selection.activeObject = newPreset;
        return newPreset;
    }

    private void AutoBindStylePreset(ProjectOneUIStylePreset target)
    {
        if (target == null)
            return;

        Undo.RecordObject(target, "Auto Bind Project One UI Style Preset");

        target.mainFont = FindFirstAsset<TMP_FontAsset>("ONE Mobile OTF Regular SDF", "ONE Mobile", "NotoSansKR SDF", "NotoSansKR");

        target.paperPanelLarge = FindFirstAsset<Sprite>("026_note_card_large_blank", "note_card_large_blank", "large_blank_card", "paper_panel");
        target.paperPanelSmall = FindFirstAsset<Sprite>("068_status_ready_small", "047_long_blank_rounded", "long_blank_rounded", "status_ready_small");
        target.paperButton = FindFirstAsset<Sprite>("046_long_blank_pill_panel", "long_blank_pill", "button_paper", "paper_button");
        target.yellowButton = FindFirstAsset<Sprite>("069_pinned_note_small", "yellow_button", "ready", "pinned_note");
        target.bluePillPanel = FindFirstAsset<Sprite>("blue_ribbon", "blue_tape", "pill_blue", "status_ready");
        target.noteTag = FindFirstAsset<Sprite>("098_corner", "note_tag", "corner_note");

        target.blueTape = FindFirstAsset<Sprite>("blue_tape", "tape_blue", "masking_blue");
        target.yellowTape = FindFirstAsset<Sprite>("yellow_tape", "tape_yellow", "masking_yellow");
        target.paperClip = FindFirstAsset<Sprite>("paperclip", "paper_clip", "clip");
        target.pin = FindFirstAsset<Sprite>("pin", "thumbtack", "pushpin");
        target.cornerFold = FindFirstAsset<Sprite>("corner", "fold", "page_corner");

        target.pawIcon = FindFirstAsset<Sprite>("paw", "paw_icon");
        target.coinIcon = FindFirstAsset<Sprite>("027_coin_gold_paw", "coin_gold_paw", "coin_gold", "coin");
        target.clockIcon = FindFirstAsset<Sprite>("clock", "timer", "watch");
        target.peopleIcon = FindFirstAsset<Sprite>("people", "group", "community");
        target.missionIcon = FindFirstAsset<Sprite>("mission", "clipboard", "quest");
        target.playIcon = FindFirstAsset<Sprite>("play", "triangle");
        target.homeIcon = FindFirstAsset<Sprite>("home", "lobby");
        target.exitIcon = FindFirstAsset<Sprite>("exit", "logout", "door");

        target.defaultCharacterPortrait = FindFirstAsset<Sprite>("hamster", "character", "player_portrait", "portrait");
        target.crownIcon = FindFirstAsset<Sprite>("crown", "winner");
        target.successStamp = FindFirstAsset<Sprite>("084_success_stamp_green", "success_stamp", "stamp_green", "success");

        EditorUtility.SetDirty(target);
        AssetDatabase.SaveAssets();
        Debug.Log("[ProjectOneUIReleaseTool] Style preset auto-bind 완료: " + AssetDatabase.GetAssetPath(target));
    }

    private T FindFirstAsset<T>(params string[] queries) where T : Object
    {
        string typeName = typeof(T).Name;

        foreach (string query in queries)
        {
            if (string.IsNullOrWhiteSpace(query))
                continue;

            string[] guids = AssetDatabase.FindAssets(query + " t:" + typeName);
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                T asset = AssetDatabase.LoadAssetAtPath<T>(path);
                if (asset != null)
                    return asset;
            }
        }

        return null;
    }

    private void EnsureFolder(string folderPath)
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

    private void ApplyUiImageDefaultsToSelection()
    {
        GameObject selected = Selection.activeGameObject;
        if (selected == null)
        {
            Debug.LogWarning("[ProjectOneUIReleaseTool] 선택된 GameObject가 없습니다.");
            return;
        }

        Image[] images = selected.GetComponentsInChildren<Image>(true);
        int changed = 0;

        foreach (Image image in images)
        {
            Undo.RecordObject(image, "Apply Project One UI Image Defaults");
            image.preserveAspect = image.sprite != null && image.type == Image.Type.Simple;

            Button button = image.GetComponent<Button>();
            if (button == null)
                image.raycastTarget = false;

            if (image.sprite != null && HasBorder(image.sprite))
                image.type = Image.Type.Sliced;

            EditorUtility.SetDirty(image);
            changed++;
        }

        Debug.Log("[ProjectOneUIReleaseTool] UI Image defaults 적용 완료: " + changed + "개");
    }
}
#endif
