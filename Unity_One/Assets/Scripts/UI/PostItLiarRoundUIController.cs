using System;
using System.Globalization;
using System.Text;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class PostItLiarRoundUIController : MonoBehaviour
{
    private static readonly UTF8Encoding StrictUtf8 =
        new UTF8Encoding(false, true);

    [Header("Binding")]
    [SerializeField] private PostItRoundManager postItRoundManager;
    [SerializeField] private float rebindInterval = 0.25f;

    [Header("Roots")]
    [SerializeField] private GameObject publicTopicHud;
    [SerializeField] private TMP_Text publicTopicText;
    [SerializeField] private GameObject liarRoundRoot;
    [SerializeField] private CanvasGroup liarRoundCanvasGroup;
    [SerializeField] private TMP_Text phaseDeadlineText;

    [Header("Phase Panels")]
    [SerializeField] private GameObject secretRolePanel;
    [SerializeField] private GameObject clueInputPanel;
    [SerializeField] private GameObject liarGuessPanel;
    [SerializeField] private GameObject citizenSettlementWaitingPanel;
    [SerializeField] private GameObject liarVotePanel;
    [SerializeField] private GameObject liarVoteWaitingPanel;
    [SerializeField] private GameObject postItRevealPanel;

    [Header("Secret Role")]
    [SerializeField] private TMP_Text secretCategoryText;
    [SerializeField] private TMP_Text secretRoleText;
    [SerializeField] private TMP_Text secretAnswerText;

    [Header("Clue")]
    [SerializeField] private TMP_Text clueCategoryText;
    [SerializeField] private TMP_Text clueAnswerText;
    [SerializeField] private TMP_InputField clueInputField;
    [SerializeField] private TMP_Text clueCharacterCountText;
    [SerializeField] private TMP_Text clueValidationText;
    [SerializeField] private Button clueSubmitButton;

    [Header("Liar Guess")]
    [SerializeField] private TMP_Text liarBattleScoreText;
    [SerializeField] private TMP_Text[] anonymousClueTexts =
        new TMP_Text[PostItLiarFixedSet.Capacity];
    [SerializeField] private Button[] liarChoiceButtons =
        new Button[PostItLiarFixedSet.Capacity];
    [SerializeField] private TMP_Text[] liarChoiceLabels =
        new TMP_Text[PostItLiarFixedSet.Capacity];
    [SerializeField] private TMP_Text liarGuessValidationText;
    [SerializeField] private Button liarGuessConfirmButton;

    [Header("Citizen Waiting")]
    [SerializeField] private TMP_Text citizenBattleScoreText;
    [SerializeField] private TMP_Text citizenWaitingText;

    [Header("Liar Vote")]
    [SerializeField] private Button[] voteCandidateButtons =
        new Button[PostItLiarFixedSet.Capacity];
    [SerializeField] private TMP_Text[] voteCandidateSlotTexts =
        new TMP_Text[PostItLiarFixedSet.Capacity];
    [SerializeField] private TMP_Text[] voteCandidateClueTexts =
        new TMP_Text[PostItLiarFixedSet.Capacity];
    [SerializeField] private TMP_Text voteValidationText;
    [SerializeField] private Button voteConfirmButton;
    [SerializeField] private TMP_Text voteWaitingText;

    [Header("Reveal")]
    [SerializeField] private TMP_Text revealLiarText;
    [SerializeField] private TMP_Text revealSecretAnswerText;
    [SerializeField] private TMP_Text revealLiarSelectedAnswerText;
    [SerializeField] private TMP_Text revealAuthoredCluesText;
    [SerializeField] private TMP_Text revealVotesText;
    [SerializeField] private TMP_Text revealPlayerScoresText;

    [Header("Selection")]
    [SerializeField] private Color selectedChoiceColor =
        new Color(1f, 0.84f, 0.3f, 1f);

    private readonly UnityAction[] _liarChoiceActions =
        new UnityAction[PostItLiarFixedSet.Capacity];
    private readonly UnityAction[] _voteCandidateActions =
        new UnityAction[PostItLiarFixedSet.Capacity];
    private readonly ColorBlock[] _liarChoiceBaseColors =
        new ColorBlock[PostItLiarFixedSet.Capacity];
    private readonly ColorBlock[] _voteCandidateBaseColors =
        new ColorBlock[PostItLiarFixedSet.Capacity];
    private readonly StringBuilder _builder = new StringBuilder(512);

    private PostItRoundManager _boundRoundManager;
    private float _nextBindAttemptTime;
    private int _displayedRoundRevision = -1;
    private int _selectedLiarChoice = -1;
    private int _selectedVoteSlot = -1;
    private int _lastDeadlineSeconds = int.MinValue;
    private ulong _observedLocalPlayerObjectId = ulong.MaxValue;
    private bool _managerEventsSubscribed;
    private bool _buttonListenersRegistered;
    private bool _requiredArraysValid;

    private void Awake()
    {
        _requiredArraysValid = HasRequiredArraySizes();
        ConfigureInputField();
        ConfigureDynamicText();
        CaptureButtonBaseColors();
        ClearRoundPresentation();
        SetAllPresentationHidden();
    }

    private void OnEnable()
    {
        RegisterButtonListeners();
        TryBindRoundManager();
        ApplyCurrentState();
    }

    private void OnDisable()
    {
        UnbindRoundManager();
        UnregisterButtonListeners();
        ClearRoundPresentation();
        SetAllPresentationHidden();
    }

    private void Update()
    {
        if (_managerEventsSubscribed && _boundRoundManager == null)
            _managerEventsSubscribed = false;

        if (!_managerEventsSubscribed &&
            Time.unscaledTime >= _nextBindAttemptTime)
        {
            TryBindRoundManager();
            ApplyCurrentState();
        }

        ulong localPlayerObjectId = ResolveLocalPlayerObjectId();
        if (_observedLocalPlayerObjectId != localPlayerObjectId)
        {
            _observedLocalPlayerObjectId = localPlayerObjectId;
            ApplyCurrentState();
        }

        RefreshDeadline(false);
    }

    private void TryBindRoundManager()
    {
        _nextBindAttemptTime =
            Time.unscaledTime + Mathf.Max(0.05f, rebindInterval);

        PostItRoundManager candidate = postItRoundManager;
        if (candidate == null)
            candidate = FindFirstObjectByType<PostItRoundManager>();

        if (_boundRoundManager == candidate && _managerEventsSubscribed)
            return;

        UnbindRoundManager();
        _boundRoundManager = candidate;
        if (_boundRoundManager == null)
            return;

        postItRoundManager = candidate;
        _boundRoundManager.PostItLiarPublicStateChanged +=
            OnPostItLiarStateChanged;
        _boundRoundManager.PostItLiarLocalStateChanged +=
            OnPostItLiarStateChanged;
        _boundRoundManager.PostItLiarRevealChanged +=
            OnPostItLiarStateChanged;
        _boundRoundManager.PostItLiarSubmissionResultReceived +=
            OnPostItLiarSubmissionResultReceived;
        _managerEventsSubscribed = true;
    }

    private void UnbindRoundManager()
    {
        if (_managerEventsSubscribed && _boundRoundManager != null)
        {
            _boundRoundManager.PostItLiarPublicStateChanged -=
                OnPostItLiarStateChanged;
            _boundRoundManager.PostItLiarLocalStateChanged -=
                OnPostItLiarStateChanged;
            _boundRoundManager.PostItLiarRevealChanged -=
                OnPostItLiarStateChanged;
            _boundRoundManager.PostItLiarSubmissionResultReceived -=
                OnPostItLiarSubmissionResultReceived;
        }

        _managerEventsSubscribed = false;
        _boundRoundManager = null;
    }

    private void OnPostItLiarStateChanged()
    {
        ApplyCurrentState();
    }

    private void OnPostItLiarSubmissionResultReceived(
        PostItLiarSubmissionKind kind,
        PostItLiarSubmitResult result)
    {
        TMP_Text target = null;
        switch (kind)
        {
            case PostItLiarSubmissionKind.Clue:
                target = clueValidationText;
                break;
            case PostItLiarSubmissionKind.LiarAnswer:
                target = liarGuessValidationText;
                break;
            case PostItLiarSubmissionKind.CitizenVote:
                target = voteValidationText;
                break;
        }

        SetText(target, GetSubmitResultLabel(result));
        ApplyCurrentState();
    }

    private void ApplyCurrentState()
    {
        if (_boundRoundManager == null ||
            !_boundRoundManager.IsSpawned)
        {
            if (_displayedRoundRevision >= 0)
                ClearRoundPresentation();

            SetAllPresentationHidden();
            return;
        }

        PostItLiarPhaseState state =
            _boundRoundManager.LiarPublicState;
        if (!state.IsActive)
        {
            if (_displayedRoundRevision >= 0)
                ClearRoundPresentation();

            SetAllPresentationHidden();
            return;
        }

        if (_displayedRoundRevision != state.RoundRevision)
        {
            ClearRoundPresentation();
            _displayedRoundRevision = state.RoundRevision;
        }

        SetText(
            publicTopicText,
            state.PublicCategory.IsEmpty
                ? string.Empty
                : $"주제: {state.PublicCategory}");
        SetActive(
            publicTopicHud,
            state.Phase == PostItLiarPhase.Brawl);

        bool showRoot =
            state.Phase == PostItLiarPhase.SecretReveal ||
            state.Phase == PostItLiarPhase.ClueWrite ||
            state.Phase == PostItLiarPhase.ClueLock ||
            state.Phase == PostItLiarPhase.LiarGuess ||
            state.Phase == PostItLiarPhase.LiarVote ||
            state.Phase == PostItLiarPhase.Reveal;
        SetLiarRootVisible(showRoot);

        SetActive(
            secretRolePanel,
            state.Phase == PostItLiarPhase.SecretReveal);
        SetActive(
            clueInputPanel,
            state.Phase == PostItLiarPhase.ClueWrite ||
            state.Phase == PostItLiarPhase.ClueLock);
        SetActive(liarGuessPanel, false);
        SetActive(citizenSettlementWaitingPanel, false);
        SetActive(liarVotePanel, false);
        SetActive(liarVoteWaitingPanel, false);
        SetActive(
            postItRevealPanel,
            state.Phase == PostItLiarPhase.Reveal);

        switch (state.Phase)
        {
            case PostItLiarPhase.SecretReveal:
                RenderSecretRole(state);
                break;
            case PostItLiarPhase.ClueWrite:
            case PostItLiarPhase.ClueLock:
                RenderClueInput(state);
                break;
            case PostItLiarPhase.LiarGuess:
                RenderLiarGuessPhase();
                break;
            case PostItLiarPhase.LiarVote:
                RenderLiarVotePhase();
                break;
            case PostItLiarPhase.Reveal:
                RenderReveal();
                break;
        }

        _lastDeadlineSeconds = int.MinValue;
        RefreshDeadline(true);
    }

    private void RenderSecretRole(PostItLiarPhaseState state)
    {
        SetText(secretCategoryText, $"카테고리 · {state.PublicCategory}");
        if (!TryGetLocalPrivateRole(
                out PostItLiarPrivateRoleData privateRole))
        {
            SetText(secretRoleText, "역할 동기화 중...");
            SetText(secretAnswerText, string.Empty);
            return;
        }

        if (privateRole.Role == PostItLiarRole.Liar)
        {
            SetText(secretRoleText, "당신은 라이어입니다");
            SetText(
                secretAnswerText,
                "비밀 정답 없이 카테고리만 보고 단서를 작성하세요");
        }
        else
        {
            SetText(secretRoleText, $"당신은 시민 P{privateRole.StableSlot + 1}");
            SetText(secretAnswerText, $"비밀 정답 · {privateRole.SecretAnswer}");
        }
    }

    private void RenderClueInput(PostItLiarPhaseState state)
    {
        SetText(clueCategoryText, $"카테고리 · {state.PublicCategory}");
        if (TryGetLocalPrivateRole(
                out PostItLiarPrivateRoleData privateRole) &&
            privateRole.Role == PostItLiarRole.Citizen)
        {
            SetText(clueAnswerText, $"비밀 정답 · {privateRole.SecretAnswer}");
        }
        else
        {
            SetText(clueAnswerText, "라이어에게는 정답이 공개되지 않습니다");
        }

        RefreshClueInputState(state.Phase);
    }

    private void RenderLiarGuessPhase()
    {
        bool isLiar =
            TryGetLocalPrivateRole(
                out PostItLiarPrivateRoleData privateRole) &&
            privateRole.Role == PostItLiarRole.Liar;
        if (!isLiar)
        {
            SetActive(citizenSettlementWaitingPanel, true);
            RenderCitizenSettlement();
            return;
        }

        SetActive(liarGuessPanel, true);
        if (!_boundRoundManager.HasLocalLiarGuessView)
        {
            SetText(liarBattleScoreText, "난투 점수 동기화 중...");
            ClearTextArray(anonymousClueTexts);
            ClearTextArray(liarChoiceLabels);
            SetLiarChoiceInteractable(false);
            if (liarGuessConfirmButton != null)
                liarGuessConfirmButton.interactable = false;
            return;
        }

        PostItLiarGuessViewData view =
            _boundRoundManager.LocalLiarGuessView;
        SetText(
            liarBattleScoreText,
            BuildBattleScoreText(view.BattleScores));
        for (int index = 0;
             index < PostItLiarFixedSet.Capacity;
             index++)
        {
            PostItLiarClueData clue = view.AnonymousClues.Get(index);
            SetText(
                GetArrayItem(anonymousClueTexts, index),
                clue.WasSubmitted
                    ? clue.Clue.ToString()
                    : "미작성");
            SetText(
                GetArrayItem(liarChoiceLabels, index),
                view.Choices.Get(index).ToString());
        }

        bool canChoose =
            !_boundRoundManager.HasSubmittedLiarAnswer &&
            view.Choices.Count == PostItLiarFixedSet.Capacity;
        SetLiarChoiceInteractable(canChoose);
        if (liarGuessConfirmButton != null)
        {
            liarGuessConfirmButton.interactable =
                canChoose &&
                _selectedLiarChoice >= 0 &&
                _selectedLiarChoice < PostItLiarFixedSet.Capacity;
        }

        RefreshLiarChoiceSelection();
    }

    private void RenderCitizenSettlement()
    {
        PostItLiarPlayerResultSet scores =
            _boundRoundManager.HasLocalLiarBattleScores
                ? _boundRoundManager.LocalLiarBattleScores
                : default;
        SetText(
            citizenBattleScoreText,
            scores.Count == PostItLiarFixedSet.Capacity
                ? BuildBattleScoreText(scores)
                : "난투 점수 동기화 중...");
        SetText(
            citizenWaitingText,
            "라이어가 정답을 추리 중입니다");
    }

    private void RenderLiarVotePhase()
    {
        bool isCitizen =
            TryGetLocalPrivateRole(
                out PostItLiarPrivateRoleData privateRole) &&
            privateRole.Role == PostItLiarRole.Citizen;
        bool showVote =
            isCitizen &&
            !_boundRoundManager.HasSubmittedLiarVote &&
            _boundRoundManager.HasLocalLiarVoteView;
        if (!showVote)
        {
            SetActive(liarVoteWaitingPanel, true);
            SetText(
                voteWaitingText,
                isCitizen
                    ? "다른 플레이어의 투표를 기다리는 중입니다"
                    : "시민들이 라이어를 찾는 중입니다");
            return;
        }

        SetActive(liarVotePanel, true);
        PostItLiarVoteViewData view =
            _boundRoundManager.LocalLiarVoteView;
        byte localSlot = privateRole.StableSlot;
        for (int index = 0;
             index < PostItLiarFixedSet.Capacity;
             index++)
        {
            PostItLiarClueData clue = view.AuthoredClues.Get(index);
            PostItLiarPlayerResultData score =
                view.BattleScores.Get(index);
            bool connected =
                score.StableSlot == index && score.IsConnected;
            SetText(
                GetArrayItem(voteCandidateSlotTexts, index),
                connected
                    ? $"P{index + 1}"
                    : $"P{index + 1} · 연결 종료");
            SetText(
                GetArrayItem(voteCandidateClueTexts, index),
                clue.WasSubmitted
                    ? clue.Clue.ToString()
                    : "미작성");

            Button button = GetArrayItem(voteCandidateButtons, index);
            if (button != null)
            {
                button.interactable =
                    connected &&
                    index != localSlot;
            }
        }

        if (_selectedVoteSlot == localSlot ||
            _selectedVoteSlot < 0 ||
            _selectedVoteSlot >= PostItLiarFixedSet.Capacity ||
            !IsVoteCandidateConnected(view, _selectedVoteSlot))
        {
            _selectedVoteSlot = -1;
        }

        if (voteConfirmButton != null)
            voteConfirmButton.interactable = _selectedVoteSlot >= 0;

        RefreshVoteSelection();
    }

    private void RenderReveal()
    {
        if (!_boundRoundManager.HasLocalLiarReveal)
        {
            SetText(revealLiarText, "결과 동기화 중...");
            SetText(revealSecretAnswerText, string.Empty);
            SetText(revealLiarSelectedAnswerText, string.Empty);
            SetText(revealAuthoredCluesText, string.Empty);
            SetText(revealVotesText, string.Empty);
            SetText(revealPlayerScoresText, string.Empty);
            return;
        }

        PostItLiarRevealData reveal =
            _boundRoundManager.LocalLiarReveal;
        SetText(revealLiarText, $"실제 라이어 · P{reveal.LiarSlot + 1}");
        SetText(revealSecretAnswerText, $"실제 정답 · {reveal.SecretAnswer}");

        if (reveal.DeductionCancelled)
        {
            SetText(
                revealLiarSelectedAnswerText,
                "연결 종료로 추리 점수가 취소되었습니다");
        }
        else if (!reveal.LiarAnswerSubmitted)
        {
            SetText(revealLiarSelectedAnswerText, "라이어 선택 · 미제출");
        }
        else
        {
            SetText(
                revealLiarSelectedAnswerText,
                $"라이어 선택 · {reveal.LiarSelectedAnswer} · " +
                (reveal.LiarAnswerCorrect ? "정답" : "오답"));
        }

        _builder.Clear();
        for (int index = 0;
             index < reveal.AuthoredClues.Count;
             index++)
        {
            if (index > 0)
                _builder.Append('\n');

            PostItLiarClueData clue =
                reveal.AuthoredClues.Get(index);
            _builder
                .Append('P')
                .Append(index + 1)
                .Append(" · ")
                .Append(clue.WasSubmitted
                    ? clue.Clue.ToString()
                    : "미작성");
        }
        SetText(revealAuthoredCluesText, _builder.ToString());

        _builder.Clear();
        for (int index = 0; index < reveal.Votes.Count; index++)
        {
            if (index > 0)
                _builder.Append('\n');

            PostItLiarVoteData vote = reveal.Votes.Get(index);
            _builder.Append('P').Append(index + 1).Append(" · ");
            if (index == reveal.LiarSlot)
            {
                _builder.Append("라이어");
            }
            else if (!vote.WasSubmitted)
            {
                _builder.Append("미제출");
            }
            else
            {
                _builder
                    .Append('P')
                    .Append(vote.TargetSlot + 1)
                    .Append(vote.IsCorrect ? " 선택 · 정답" : " 선택 · 오답");
            }
        }
        SetText(revealVotesText, _builder.ToString());

        _builder.Clear();
        for (int index = 0;
             index < reveal.PlayerResults.Count;
             index++)
        {
            if (index > 0)
                _builder.Append('\n');

            PostItLiarPlayerResultData result =
                reveal.PlayerResults.Get(index);
            _builder
                .Append('P')
                .Append(index + 1)
                .Append(" · 난투 ")
                .Append(result.BattleScore)
                .Append(" · 추리 +")
                .Append(result.DeductionScore)
                .Append(" · 최종 ")
                .Append(result.FinalRoundScore);
            if (!result.IsConnected)
                _builder.Append(" · 연결 종료");
        }
        SetText(revealPlayerScoresText, _builder.ToString());
    }

    private void RefreshDeadline(bool force)
    {
        if (phaseDeadlineText == null ||
            _boundRoundManager == null ||
            !_boundRoundManager.IsSpawned)
        {
            return;
        }

        PostItLiarPhaseState state =
            _boundRoundManager.LiarPublicState;
        bool deadlineVisible =
            state.IsActive &&
            state.DeadlineServerTime > 0d &&
            state.Phase != PostItLiarPhase.Brawl &&
            state.Phase != PostItLiarPhase.Complete;
        if (!deadlineVisible)
        {
            if (force || _lastDeadlineSeconds != int.MinValue)
                SetText(phaseDeadlineText, string.Empty);
            _lastDeadlineSeconds = int.MinValue;
            return;
        }

        NetworkManager networkManager = NetworkManager.Singleton;
        double serverTime =
            networkManager != null && networkManager.IsListening
                ? networkManager.ServerTime.Time
                : 0d;
        int remainingSeconds = Mathf.CeilToInt(
            Mathf.Max(
                0f,
                (float)(state.DeadlineServerTime - serverTime)));
        if (!force && remainingSeconds == _lastDeadlineSeconds)
            return;

        _lastDeadlineSeconds = remainingSeconds;
        SetText(phaseDeadlineText, $"남은 시간 · {remainingSeconds}초");
    }

    private void RefreshClueInputState(PostItLiarPhase phase)
    {
        string clue = clueInputField != null
            ? clueInputField.text ?? string.Empty
            : string.Empty;
        bool textValid = TryGetTextMetrics(
            clue,
            out int textElementCount,
            out int utf8ByteCount);
        SetText(
            clueCharacterCountText,
            $"{textElementCount}/{PostItClueModule.MaxTextElements} · " +
            $"{utf8ByteCount}/{PostItClueModule.MaxUtf8Bytes}B");

        bool alreadySubmitted =
            _boundRoundManager != null &&
            _boundRoundManager.HasSubmittedLiarClue;
        bool canEdit =
            phase == PostItLiarPhase.ClueWrite &&
            !alreadySubmitted;
        if (clueInputField != null)
            clueInputField.interactable = canEdit;

        if (clueSubmitButton != null)
        {
            clueSubmitButton.interactable =
                canEdit &&
                textValid &&
                !string.IsNullOrWhiteSpace(clue);
        }

        if (alreadySubmitted)
            SetText(clueValidationText, "제출 완료 · 수정할 수 없습니다");
        else if (phase == PostItLiarPhase.ClueLock)
            SetText(clueValidationText, "제출이 마감되었습니다");
        else if (!textValid)
            SetText(clueValidationText, "24자 또는 UTF-8 128B 제한을 확인하세요");
    }

    private void HandleClueTextChanged(string value)
    {
        SetText(clueValidationText, string.Empty);
        PostItLiarPhase phase =
            _boundRoundManager != null
                ? _boundRoundManager.LiarPublicState.Phase
                : PostItLiarPhase.None;
        RefreshClueInputState(phase);
    }

    private void SubmitClue()
    {
        if (_boundRoundManager == null ||
            clueInputField == null ||
            !clueSubmitButton.interactable)
        {
            return;
        }

        _boundRoundManager.RequestSubmitLiarClue(
            clueInputField.text ?? string.Empty);
    }

    private void SelectLiarChoice(int choiceIndex)
    {
        if (_boundRoundManager == null ||
            _boundRoundManager.HasSubmittedLiarAnswer ||
            choiceIndex < 0 ||
            choiceIndex >= PostItLiarFixedSet.Capacity)
        {
            return;
        }

        _selectedLiarChoice = choiceIndex;
        SetText(liarGuessValidationText, string.Empty);
        RefreshLiarChoiceSelection();
        if (liarGuessConfirmButton != null)
            liarGuessConfirmButton.interactable = true;
    }

    private void SubmitLiarChoice()
    {
        if (_boundRoundManager == null ||
            liarGuessConfirmButton == null ||
            !liarGuessConfirmButton.interactable ||
            _selectedLiarChoice < 0 ||
            _selectedLiarChoice >= PostItLiarFixedSet.Capacity)
        {
            return;
        }

        _boundRoundManager.RequestSubmitLiarAnswerChoice(
            (byte)_selectedLiarChoice);
    }

    private void SelectVoteCandidate(int stableSlot)
    {
        if (_boundRoundManager == null ||
            _boundRoundManager.HasSubmittedLiarVote ||
            stableSlot < 0 ||
            stableSlot >= PostItLiarFixedSet.Capacity)
        {
            return;
        }

        Button button = GetArrayItem(voteCandidateButtons, stableSlot);
        if (button == null || !button.interactable)
            return;

        _selectedVoteSlot = stableSlot;
        SetText(voteValidationText, string.Empty);
        RefreshVoteSelection();
        if (voteConfirmButton != null)
            voteConfirmButton.interactable = true;
    }

    private void SubmitVote()
    {
        if (_boundRoundManager == null ||
            voteConfirmButton == null ||
            !voteConfirmButton.interactable ||
            _selectedVoteSlot < 0 ||
            _selectedVoteSlot >= PostItLiarFixedSet.Capacity)
        {
            return;
        }

        _boundRoundManager.RequestSubmitLiarVote(
            (byte)_selectedVoteSlot);
    }

    private void RegisterButtonListeners()
    {
        if (_buttonListenersRegistered)
            return;

        if (clueInputField != null)
            clueInputField.onValueChanged.AddListener(HandleClueTextChanged);
        if (clueSubmitButton != null)
            clueSubmitButton.onClick.AddListener(SubmitClue);
        if (liarGuessConfirmButton != null)
            liarGuessConfirmButton.onClick.AddListener(SubmitLiarChoice);
        if (voteConfirmButton != null)
            voteConfirmButton.onClick.AddListener(SubmitVote);

        if (_requiredArraysValid)
        {
            for (int index = 0;
                 index < PostItLiarFixedSet.Capacity;
                 index++)
            {
                int capturedIndex = index;
                _liarChoiceActions[index] =
                    () => SelectLiarChoice(capturedIndex);
                _voteCandidateActions[index] =
                    () => SelectVoteCandidate(capturedIndex);
                if (liarChoiceButtons[index] != null)
                {
                    liarChoiceButtons[index].onClick.AddListener(
                        _liarChoiceActions[index]);
                }
                if (voteCandidateButtons[index] != null)
                {
                    voteCandidateButtons[index].onClick.AddListener(
                        _voteCandidateActions[index]);
                }
            }
        }

        _buttonListenersRegistered = true;
    }

    private void UnregisterButtonListeners()
    {
        if (!_buttonListenersRegistered)
            return;

        if (clueInputField != null)
            clueInputField.onValueChanged.RemoveListener(HandleClueTextChanged);
        if (clueSubmitButton != null)
            clueSubmitButton.onClick.RemoveListener(SubmitClue);
        if (liarGuessConfirmButton != null)
            liarGuessConfirmButton.onClick.RemoveListener(SubmitLiarChoice);
        if (voteConfirmButton != null)
            voteConfirmButton.onClick.RemoveListener(SubmitVote);

        if (_requiredArraysValid)
        {
            for (int index = 0;
                 index < PostItLiarFixedSet.Capacity;
                 index++)
            {
                if (liarChoiceButtons[index] != null &&
                    _liarChoiceActions[index] != null)
                {
                    liarChoiceButtons[index].onClick.RemoveListener(
                        _liarChoiceActions[index]);
                }
                if (voteCandidateButtons[index] != null &&
                    _voteCandidateActions[index] != null)
                {
                    voteCandidateButtons[index].onClick.RemoveListener(
                        _voteCandidateActions[index]);
                }
            }
        }

        _buttonListenersRegistered = false;
    }

    private void ConfigureInputField()
    {
        if (clueInputField == null)
            return;

        clueInputField.lineType = TMP_InputField.LineType.SingleLine;
        clueInputField.contentType = TMP_InputField.ContentType.Standard;
        clueInputField.characterLimit = 0;
        clueInputField.richText = false;
        clueInputField.isRichTextEditingAllowed = false;
    }

    private void ConfigureDynamicText()
    {
        SetRichTextDisabled(publicTopicText);
        SetRichTextDisabled(phaseDeadlineText);
        SetRichTextDisabled(secretCategoryText);
        SetRichTextDisabled(secretRoleText);
        SetRichTextDisabled(secretAnswerText);
        SetRichTextDisabled(clueCategoryText);
        SetRichTextDisabled(clueAnswerText);
        SetRichTextDisabled(clueCharacterCountText);
        SetRichTextDisabled(clueValidationText);
        SetRichTextDisabled(liarBattleScoreText);
        SetRichTextDisabled(liarGuessValidationText);
        SetRichTextDisabled(citizenBattleScoreText);
        SetRichTextDisabled(citizenWaitingText);
        SetRichTextDisabled(voteValidationText);
        SetRichTextDisabled(voteWaitingText);
        SetRichTextDisabled(revealLiarText);
        SetRichTextDisabled(revealSecretAnswerText);
        SetRichTextDisabled(revealLiarSelectedAnswerText);
        SetRichTextDisabled(revealAuthoredCluesText);
        SetRichTextDisabled(revealVotesText);
        SetRichTextDisabled(revealPlayerScoresText);
        SetRichTextDisabled(anonymousClueTexts);
        SetRichTextDisabled(liarChoiceLabels);
        SetRichTextDisabled(voteCandidateSlotTexts);
        SetRichTextDisabled(voteCandidateClueTexts);
    }

    private void CaptureButtonBaseColors()
    {
        if (!_requiredArraysValid)
            return;

        for (int index = 0;
             index < PostItLiarFixedSet.Capacity;
             index++)
        {
            if (liarChoiceButtons[index] != null)
            {
                _liarChoiceBaseColors[index] =
                    liarChoiceButtons[index].colors;
            }
            if (voteCandidateButtons[index] != null)
            {
                _voteCandidateBaseColors[index] =
                    voteCandidateButtons[index].colors;
            }
        }
    }

    private void RefreshLiarChoiceSelection()
    {
        if (!_requiredArraysValid)
            return;

        for (int index = 0;
             index < PostItLiarFixedSet.Capacity;
             index++)
        {
            ApplySelectionColor(
                liarChoiceButtons[index],
                _liarChoiceBaseColors[index],
                index == _selectedLiarChoice);
        }
    }

    private void RefreshVoteSelection()
    {
        if (!_requiredArraysValid)
            return;

        for (int index = 0;
             index < PostItLiarFixedSet.Capacity;
             index++)
        {
            ApplySelectionColor(
                voteCandidateButtons[index],
                _voteCandidateBaseColors[index],
                index == _selectedVoteSlot);
        }
    }

    private void ApplySelectionColor(
        Button button,
        ColorBlock baseColors,
        bool selected)
    {
        if (button == null)
            return;

        ColorBlock colors = baseColors;
        if (selected)
            colors.normalColor = selectedChoiceColor;
        button.colors = colors;
    }

    private void SetLiarChoiceInteractable(bool interactable)
    {
        if (!_requiredArraysValid)
            return;

        for (int index = 0;
             index < PostItLiarFixedSet.Capacity;
             index++)
        {
            if (liarChoiceButtons[index] != null)
                liarChoiceButtons[index].interactable = interactable;
        }
    }

    private void ClearRoundPresentation()
    {
        _displayedRoundRevision = -1;
        _selectedLiarChoice = -1;
        _selectedVoteSlot = -1;
        _lastDeadlineSeconds = int.MinValue;

        if (clueInputField != null)
            clueInputField.SetTextWithoutNotify(string.Empty);

        SetText(publicTopicText, string.Empty);
        SetText(phaseDeadlineText, string.Empty);
        SetText(secretCategoryText, string.Empty);
        SetText(secretRoleText, string.Empty);
        SetText(secretAnswerText, string.Empty);
        SetText(clueCategoryText, string.Empty);
        SetText(clueAnswerText, string.Empty);
        SetText(clueCharacterCountText, string.Empty);
        SetText(clueValidationText, string.Empty);
        SetText(liarBattleScoreText, string.Empty);
        SetText(liarGuessValidationText, string.Empty);
        SetText(citizenBattleScoreText, string.Empty);
        SetText(citizenWaitingText, string.Empty);
        SetText(voteValidationText, string.Empty);
        SetText(voteWaitingText, string.Empty);
        SetText(revealLiarText, string.Empty);
        SetText(revealSecretAnswerText, string.Empty);
        SetText(revealLiarSelectedAnswerText, string.Empty);
        SetText(revealAuthoredCluesText, string.Empty);
        SetText(revealVotesText, string.Empty);
        SetText(revealPlayerScoresText, string.Empty);
        ClearTextArray(anonymousClueTexts);
        ClearTextArray(liarChoiceLabels);
        ClearTextArray(voteCandidateSlotTexts);
        ClearTextArray(voteCandidateClueTexts);

        RefreshLiarChoiceSelection();
        RefreshVoteSelection();
    }

    private void SetAllPresentationHidden()
    {
        SetActive(publicTopicHud, false);
        SetLiarRootVisible(false);
        SetActive(secretRolePanel, false);
        SetActive(clueInputPanel, false);
        SetActive(liarGuessPanel, false);
        SetActive(citizenSettlementWaitingPanel, false);
        SetActive(liarVotePanel, false);
        SetActive(liarVoteWaitingPanel, false);
        SetActive(postItRevealPanel, false);
    }

    private void SetLiarRootVisible(bool visible)
    {
        if (liarRoundRoot != null && !liarRoundRoot.activeSelf)
            liarRoundRoot.SetActive(true);

        if (liarRoundCanvasGroup == null)
            return;

        liarRoundCanvasGroup.alpha = visible ? 1f : 0f;
        liarRoundCanvasGroup.interactable = visible;
        liarRoundCanvasGroup.blocksRaycasts = visible;
    }

    private bool TryGetLocalPrivateRole(
        out PostItLiarPrivateRoleData privateRole)
    {
        privateRole = default;
        if (_boundRoundManager == null ||
            !_boundRoundManager.HasLocalLiarPrivateRole)
        {
            return false;
        }

        privateRole = _boundRoundManager.LocalLiarPrivateRole;
        return privateRole.IsValid &&
               privateRole.RoundRevision == _displayedRoundRevision;
    }

    private string BuildBattleScoreText(
        PostItLiarPlayerResultSet scores)
    {
        if (scores.Count != PostItLiarFixedSet.Capacity)
            return "난투 점수 동기화 중...";

        _builder.Clear();
        _builder.Append("난투 점수");
        for (int index = 0;
             index < PostItLiarFixedSet.Capacity;
             index++)
        {
            PostItLiarPlayerResultData score = scores.Get(index);
            _builder
                .Append(index == 0 ? " · " : " / ")
                .Append('P')
                .Append(index + 1)
                .Append(' ')
                .Append(score.BattleScore);
        }
        return _builder.ToString();
    }

    private static bool IsVoteCandidateConnected(
        PostItLiarVoteViewData view,
        int stableSlot)
    {
        if (stableSlot < 0 ||
            stableSlot >= PostItLiarFixedSet.Capacity)
        {
            return false;
        }

        PostItLiarPlayerResultData score =
            view.BattleScores.Get(stableSlot);
        return score.StableSlot == stableSlot && score.IsConnected;
    }

    private static bool TryGetTextMetrics(
        string value,
        out int textElementCount,
        out int utf8ByteCount)
    {
        textElementCount = 0;
        utf8ByteCount = 0;
        try
        {
            string text = value ?? string.Empty;
            textElementCount =
                StringInfo.ParseCombiningCharacters(text).Length;
            utf8ByteCount = StrictUtf8.GetByteCount(text);
            return textElementCount <= PostItClueModule.MaxTextElements &&
                   utf8ByteCount <= PostItClueModule.MaxUtf8Bytes;
        }
        catch (EncoderFallbackException)
        {
            return false;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static ulong ResolveLocalPlayerObjectId()
    {
        NetworkManager networkManager = NetworkManager.Singleton;
        if (networkManager == null ||
            !networkManager.IsListening ||
            networkManager.LocalClient == null ||
            networkManager.LocalClient.PlayerObject == null ||
            !networkManager.LocalClient.PlayerObject.IsSpawned)
        {
            return ulong.MaxValue;
        }

        return networkManager.LocalClient.PlayerObject.NetworkObjectId;
    }

    private static string GetSubmitResultLabel(
        PostItLiarSubmitResult result)
    {
        switch (result)
        {
            case PostItLiarSubmitResult.Accepted:
                return "제출 완료";
            case PostItLiarSubmitResult.Empty:
                return "내용을 입력하세요";
            case PostItLiarSubmitResult.TooLong:
                return "24자 또는 UTF-8 128B 제한을 초과했습니다";
            case PostItLiarSubmitResult.ContainsAnswer:
                return "정답을 직접 포함할 수 없습니다";
            case PostItLiarSubmitResult.ContainsForbidden:
                return "사용할 수 없는 표현이 포함되어 있습니다";
            case PostItLiarSubmitResult.Duplicate:
                return "이미 제출했습니다";
            case PostItLiarSubmitResult.Late:
                return "제출 시간이 끝났습니다";
            case PostItLiarSubmitResult.WrongRole:
                return "현재 역할로는 제출할 수 없습니다";
            case PostItLiarSubmitResult.InvalidChoice:
                return "선택할 수 없는 항목입니다";
            case PostItLiarSubmitResult.Stale:
                return "이전 단계의 요청은 반영되지 않습니다";
            case PostItLiarSubmitResult.InvalidText:
                return "입력 문자열을 확인하세요";
            case PostItLiarSubmitResult.NotParticipant:
            case PostItLiarSubmitResult.PlayerObjectMismatch:
                return "현재 참가자 정보를 확인할 수 없습니다";
            case PostItLiarSubmitResult.InvalidPhase:
            case PostItLiarSubmitResult.NotActive:
                return "현재 단계에서는 제출할 수 없습니다";
            default:
                return string.Empty;
        }
    }

    private bool HasRequiredArraySizes()
    {
        bool valid =
            HasCapacity(anonymousClueTexts) &&
            HasCapacity(liarChoiceButtons) &&
            HasCapacity(liarChoiceLabels) &&
            HasCapacity(voteCandidateButtons) &&
            HasCapacity(voteCandidateSlotTexts) &&
            HasCapacity(voteCandidateClueTexts);
        if (!valid)
        {
            Debug.LogError(
                "[PostItLiarRoundUIController] UI 배열은 모두 길이 4여야 합니다.",
                this);
        }

        return valid;
    }

    private static bool HasCapacity<T>(T[] values)
    {
        return values != null &&
               values.Length == PostItLiarFixedSet.Capacity;
    }

    private static T GetArrayItem<T>(T[] values, int index)
        where T : class
    {
        return values != null &&
               index >= 0 &&
               index < values.Length
            ? values[index]
            : null;
    }

    private static void ClearTextArray(TMP_Text[] values)
    {
        if (values == null)
            return;

        for (int index = 0; index < values.Length; index++)
            SetText(values[index], string.Empty);
    }

    private static void SetRichTextDisabled(TMP_Text text)
    {
        if (text != null)
            text.richText = false;
    }

    private static void SetRichTextDisabled(TMP_Text[] values)
    {
        if (values == null)
            return;

        for (int index = 0; index < values.Length; index++)
            SetRichTextDisabled(values[index]);
    }

    private static void SetText(TMP_Text text, string value)
    {
        if (text != null)
            text.text = value ?? string.Empty;
    }

    private static void SetActive(GameObject target, bool active)
    {
        if (target != null && target.activeSelf != active)
            target.SetActive(active);
    }

    private void OnValidate()
    {
        rebindInterval = Mathf.Max(0.05f, rebindInterval);
    }
}
