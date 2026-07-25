using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class TutorialUI : MonoBehaviour
{
    [Header("Director")]
    [SerializeField] private TutorialDirector director;

    [Header("Step Presentation")]
    [SerializeField] private GameObject stepPanel;
    [SerializeField] private TMP_Text stepTitleText;
    [SerializeField] private TMP_Text instructionText;
    [SerializeField] private TMP_Text progressText;
    [SerializeField] private TMP_Text optionalHintText;

    [Header("Panels")]
    [SerializeField] private GameObject exitConfirmPanel;
    [SerializeField] private GameObject completePanel;
    [SerializeField] private GameObject controlsPanel;

    [Header("Buttons")]
    [SerializeField] private Button exitButton;
    [SerializeField] private Button exitConfirmButton;
    [SerializeField] private Button exitCancelButton;
    [SerializeField] private Button completeMainMenuButton;
    [SerializeField] private Button skipPeelButton;
    [SerializeField] private Button controlsToggleButton;

    private bool _directorSubscribed;
    private bool _cursorStateCaptured;
    private CursorLockMode _capturedCursorLockMode;
    private bool _capturedCursorVisible;

    private void Awake()
    {
        SetActive(exitConfirmPanel, false);
        SetActive(completePanel, false);
        SetActive(controlsPanel, false);
    }

    private void OnEnable()
    {
        if (!HasRequiredReferences())
        {
            Debug.LogError("[TutorialUI] Required Scene references are incomplete.", this);
            enabled = false;
            return;
        }

        SubscribeDirector();
        RenderCurrentState();
    }

    private void OnDisable()
    {
        UnsubscribeDirector();
        RestoreCapturedCursorState();
    }

    private void Update()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null)
            return;

        if (keyboard.escapeKey.wasPressedThisFrame)
        {
            if (exitConfirmPanel != null && exitConfirmPanel.activeSelf)
                OnExitCanceled();
            else
                OnExitButtonPressed();

            return;
        }

        if (keyboard.f1Key.wasPressedThisFrame &&
            (exitConfirmPanel == null || !exitConfirmPanel.activeSelf))
        {
            OnControlsTogglePressed();
        }
    }

    private void LateUpdate()
    {
        if (RequiresReleasedCursor())
            ApplyReleasedCursorState();
    }

    public void OnExitButtonPressed()
    {
        if (director == null)
            return;

        SetActive(controlsPanel, false);
        CaptureCursorState();
        SetActive(exitConfirmPanel, true);
        ApplyReleasedCursorState();
    }

    public void OnExitConfirmed()
    {
        SetActive(exitConfirmPanel, false);
        ForgetCapturedCursorState();
        ApplyReleasedCursorState();
        director?.RequestExitTutorial();
    }

    public void OnExitCanceled()
    {
        SetActive(exitConfirmPanel, false);
        RestoreCursorStateIfNoOverlay();
    }

    public void OnSkipPeelPressed()
    {
        if (director == null ||
            director.CurrentStep != TutorialDirector.TutorialStep.Peel)
        {
            return;
        }

        director.RequestSkipGuidedPeel();
    }

    public void OnCompleteMainMenuPressed()
    {
        if (director == null ||
            director.CurrentStep != TutorialDirector.TutorialStep.Complete)
        {
            return;
        }

        ForgetCapturedCursorState();
        ApplyReleasedCursorState();
        director.RequestCompleteTutorial();
    }

    public void OnControlsTogglePressed()
    {
        if (controlsPanel == null)
            return;

        bool showControls = !controlsPanel.activeSelf;
        SetActive(exitConfirmPanel, false);
        controlsPanel.SetActive(showControls);

        if (showControls)
        {
            CaptureCursorState();
            ApplyReleasedCursorState();
        }
        else
        {
            RestoreCursorStateIfNoOverlay();
        }
    }

    public void OnControlsClosePressed()
    {
        SetActive(controlsPanel, false);
        RestoreCursorStateIfNoOverlay();
    }

    private void SubscribeDirector()
    {
        if (_directorSubscribed || director == null)
            return;

        director.StepChanged += HandleStepChanged;
        director.InstructionChanged += HandleInstructionChanged;
        director.ProgressChanged += HandleProgressChanged;
        director.HintChanged += HandleHintChanged;
        _directorSubscribed = true;
    }

    private void UnsubscribeDirector()
    {
        if (!_directorSubscribed)
            return;

        if (director != null)
        {
            director.StepChanged -= HandleStepChanged;
            director.InstructionChanged -= HandleInstructionChanged;
            director.ProgressChanged -= HandleProgressChanged;
            director.HintChanged -= HandleHintChanged;
        }

        _directorSubscribed = false;
    }

    private void HandleStepChanged(TutorialDirector.TutorialStep step)
    {
        RenderCurrentState();
    }

    private bool HasRequiredReferences()
    {
        return director != null &&
               stepPanel != null &&
               stepTitleText != null &&
               instructionText != null &&
               progressText != null &&
               optionalHintText != null &&
               exitConfirmPanel != null &&
               completePanel != null &&
               controlsPanel != null &&
               exitButton != null &&
               exitConfirmButton != null &&
               exitCancelButton != null &&
               completeMainMenuButton != null &&
               skipPeelButton != null &&
               controlsToggleButton != null;
    }

    private void HandleInstructionChanged(string instruction)
    {
        SetText(instructionText, instruction);
    }

    private void HandleProgressChanged(int current, int target)
    {
        RenderProgress(current, target);
    }

    private void HandleHintChanged(string hint)
    {
        RenderHint(hint);
    }

    private void RenderCurrentState()
    {
        if (director == null)
        {
            SetActive(stepPanel, false);
            SetActive(completePanel, false);
            SetActive(skipPeelButton, false);
            return;
        }

        TutorialDirector.TutorialStep step = director.CurrentStep;
        bool isComplete = step == TutorialDirector.TutorialStep.Complete;

        SetActive(stepPanel, !isComplete);
        SetActive(completePanel, isComplete);
        SetActive(skipPeelButton, step == TutorialDirector.TutorialStep.Peel);
        SetText(stepTitleText, GetStepTitle(step));
        SetText(instructionText, director.CurrentInstruction);
        RenderProgress(director.ProgressCurrent, director.ProgressTarget);
        RenderHint(director.CurrentHint);

        if (isComplete)
        {
            SetActive(exitConfirmPanel, false);
            SetActive(controlsPanel, false);
            CaptureCursorState();
            ApplyReleasedCursorState();
        }
        else
        {
            RestoreCursorStateIfNoOverlay();
        }
    }

    private void CaptureCursorState()
    {
        if (_cursorStateCaptured)
            return;

        _capturedCursorLockMode = Cursor.lockState;
        _capturedCursorVisible = Cursor.visible;
        _cursorStateCaptured = true;
    }

    private static void ApplyReleasedCursorState()
    {
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    private bool RequiresReleasedCursor()
    {
        return (exitConfirmPanel != null && exitConfirmPanel.activeSelf) ||
               (completePanel != null && completePanel.activeSelf) ||
               (controlsPanel != null && controlsPanel.activeSelf);
    }

    private void RestoreCursorStateIfNoOverlay()
    {
        if (!RequiresReleasedCursor())
            RestoreCapturedCursorState();
    }

    private void RestoreCapturedCursorState()
    {
        if (!_cursorStateCaptured)
            return;

        Cursor.lockState = _capturedCursorLockMode;
        Cursor.visible = _capturedCursorVisible;
        _cursorStateCaptured = false;
    }

    private void ForgetCapturedCursorState()
    {
        _cursorStateCaptured = false;
    }

    private void RenderProgress(int current, int target)
    {
        if (progressText == null)
            return;

        current = Mathf.Max(0, current);
        target = Mathf.Max(0, target);
        if (target <= 0)
        {
            progressText.text = string.Empty;
            return;
        }

        progressText.text =
            director != null &&
            director.CurrentStep == TutorialDirector.TutorialStep.Peel &&
            target == 100
                ? $"{Mathf.Clamp(current, 0, target)}%"
                : $"{Mathf.Clamp(current, 0, target)} / {target}";
    }

    private void RenderHint(string hint)
    {
        if (optionalHintText == null)
            return;

        bool hasHint = !string.IsNullOrWhiteSpace(hint);
        optionalHintText.text = hasHint ? hint : string.Empty;
        optionalHintText.gameObject.SetActive(hasHint);
    }

    private static string GetStepTitle(TutorialDirector.TutorialStep step)
    {
        switch (step)
        {
            case TutorialDirector.TutorialStep.BootstrapWaiting:
                return "튜토리얼 준비";
            case TutorialDirector.TutorialStep.Move:
                return "이동";
            case TutorialDirector.TutorialStep.Jump:
                return "점프";
            case TutorialDirector.TutorialStep.Attack:
                return "공격";
            case TutorialDirector.TutorialStep.Pickup:
                return "Item 줍기";
            case TutorialDirector.TutorialStep.Drop:
                return "Item 내려놓기";
            case TutorialDirector.TutorialStep.Throw:
                return "Item 던지기";
            case TutorialDirector.TutorialStep.Peel:
                return "포스트잇 뜯기";
            case TutorialDirector.TutorialStep.FallRespawn:
                return "낙사와 리스폰";
            case TutorialDirector.TutorialStep.Recovery:
                return "포스트잇 회수";
            case TutorialDirector.TutorialStep.PrepareGhost:
                return "유령 관전 준비";
            case TutorialDirector.TutorialStep.Ghost:
                return "유령 관전";
            case TutorialDirector.TutorialStep.Complete:
                return "완료";
            case TutorialDirector.TutorialStep.Failed:
                return "튜토리얼 오류";
            default:
                return string.Empty;
        }
    }

    private static void SetText(TMP_Text target, string value)
    {
        if (target != null)
            target.text = value ?? string.Empty;
    }

    private static void SetActive(GameObject target, bool active)
    {
        if (target != null && target.activeSelf != active)
            target.SetActive(active);
    }

    private static void SetActive(Selectable target, bool active)
    {
        if (target != null)
            SetActive(target.gameObject, active);
    }
}
