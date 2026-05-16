using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class LoadingOverlayUI : MonoBehaviour
{
    [SerializeField, Tooltip("방 생성/참가 작업 상태 이벤트를 제공하는 LobbyManager입니다. 비워두면 씬에서 자동 탐색합니다.")]
    private LobbyManager lobbyManager;

    [SerializeField, Tooltip("로딩 오버레이 전체 루트 오브젝트입니다.")]
    private GameObject overlayRoot;

    [SerializeField, Tooltip("로딩 오버레이 표시/숨김에 사용할 CanvasGroup입니다. 비워두면 overlayRoot에서 자동 탐색하거나 추가합니다.")]
    private CanvasGroup overlayCanvasGroup;

    [SerializeField, Tooltip("현재 로딩 상태 메시지를 표시할 텍스트입니다.")]
    private TMP_Text loadingMessageText;

    [SerializeField, Tooltip("작업 실패 메시지를 표시할 텍스트입니다.")]
    private TMP_Text errorMessageText;

    [SerializeField, Tooltip("로딩 제목을 표시할 텍스트입니다.")]
    private TMP_Text loadingTitleText;

    [SerializeField, Tooltip("로딩 진행바 Fill 이미지입니다. 실제 진행률이 없으면 반복 애니메이션으로 사용합니다.")]
    private Image progressFillImage;

    [SerializeField, Tooltip("시작 시 LobbyManager가 이미 작업 중이면 오버레이를 표시할지 여부입니다.")]
    private bool showOnStartIfOperationInProgress = true;

    [SerializeField, Tooltip("성공 메시지를 보여준 뒤 오버레이를 숨기기까지의 대기 시간입니다.")]
    private float hideDelayAfterSuccess = 0.35f;

    [SerializeField, Tooltip("실패 메시지를 보여준 뒤 오버레이를 숨기기까지의 대기 시간입니다.")]
    private float hideDelayAfterFailure = 2.0f;

    [SerializeField, Tooltip("실제 진행률 대신 진행바 반복 애니메이션을 사용할지 여부입니다.")]
    private bool animateProgress = true;

    [SerializeField, Tooltip("진행바 반복 애니메이션 속도입니다.")]
    private float progressAnimationSpeed = 0.75f;

    [SerializeField, Tooltip("로딩 중 표시할 기본 제목입니다.")]
    private string defaultLoadingTitle = "LOADING";

    [SerializeField, Tooltip("성공 시 표시할 기본 메시지입니다.")]
    private string successMessage = "연결되었습니다.";

    private LobbyManager subscribedLobbyManager;
    private Coroutine hideCoroutine;
    private Coroutine progressCoroutine;
    private bool isSubscribedToLobbyManager;
    private bool isVisible;

    private void Awake()
    {
        ResolveOverlayReferences();
        ResolveLobbyManager();
        ApplyVisibility(false);
    }

    private void OnEnable()
    {
        ResolveOverlayReferences();
        ResolveLobbyManager();
        SubscribeToLobbyManager();
        ForceRefreshFromLobbyManager();
    }

    private void OnDisable()
    {
        UnsubscribeFromLobbyManager();
        StopHideCoroutine();
        StopProgressAnimation();
    }

    private void OnDestroy()
    {
        UnsubscribeFromLobbyManager();
        StopHideCoroutine();
        StopProgressAnimation();
    }

    public void Show(string message)
    {
        StopHideCoroutine();
        ResolveOverlayReferences();

        SetLoadingTitle(defaultLoadingTitle);
        SetLoadingMessage(message);
        SetErrorMessage(string.Empty);
        ApplyVisibility(true);
        StartProgressAnimationIfNeeded();
    }

    public void Hide()
    {
        StopHideCoroutine();
        ApplyVisibility(false);
        StopProgressAnimation();
    }

    public void ShowError(string message)
    {
        StopHideCoroutine();
        ResolveOverlayReferences();

        SetLoadingTitle(defaultLoadingTitle);
        SetLoadingMessage(errorMessageText == null ? message : string.Empty);
        SetErrorMessage(message);
        ApplyVisibility(true);
        StartProgressAnimationIfNeeded();
    }

    public void ForceRefreshFromLobbyManager()
    {
        if (lobbyManager == null)
        {
            ResolveLobbyManager();
            SubscribeToLobbyManager();
        }

        if (lobbyManager != null &&
            showOnStartIfOperationInProgress &&
            lobbyManager.IsLobbyOperationInProgress)
        {
            Show(lobbyManager.CurrentLobbyOperationMessage);
            return;
        }

        Hide();
    }

    private void HandleLobbyOperationStarted(string message)
    {
        Show(message);
    }

    private void HandleLobbyOperationSucceeded(string message)
    {
        Show(string.IsNullOrEmpty(message) ? successMessage : message);
        ScheduleHide(hideDelayAfterSuccess);
    }

    private void HandleLobbyOperationFailed(string message)
    {
        ShowError(message);
        ScheduleHide(hideDelayAfterFailure);
    }

    private void ResolveLobbyManager()
    {
        if (lobbyManager == null)
        {
            lobbyManager = FindFirstObjectByType<LobbyManager>();
        }
    }

    private void ResolveOverlayReferences()
    {
        GameObject targetObject = GetOverlayRoot();
        if (targetObject == null)
        {
            return;
        }

        if (overlayCanvasGroup == null)
        {
            overlayCanvasGroup = targetObject.GetComponent<CanvasGroup>();
        }

        if (overlayCanvasGroup == null)
        {
            overlayCanvasGroup = targetObject.AddComponent<CanvasGroup>();
        }
    }

    private void SubscribeToLobbyManager()
    {
        if (isSubscribedToLobbyManager || lobbyManager == null)
        {
            return;
        }

        lobbyManager.LobbyOperationStarted += HandleLobbyOperationStarted;
        lobbyManager.LobbyOperationSucceeded += HandleLobbyOperationSucceeded;
        lobbyManager.LobbyOperationFailed += HandleLobbyOperationFailed;

        subscribedLobbyManager = lobbyManager;
        isSubscribedToLobbyManager = true;
    }

    private void UnsubscribeFromLobbyManager()
    {
        if (!isSubscribedToLobbyManager)
        {
            return;
        }

        if (subscribedLobbyManager != null)
        {
            subscribedLobbyManager.LobbyOperationStarted -= HandleLobbyOperationStarted;
            subscribedLobbyManager.LobbyOperationSucceeded -= HandleLobbyOperationSucceeded;
            subscribedLobbyManager.LobbyOperationFailed -= HandleLobbyOperationFailed;
        }

        subscribedLobbyManager = null;
        isSubscribedToLobbyManager = false;
    }

    private void ScheduleHide(float delay)
    {
        StopHideCoroutine();
        hideCoroutine = StartCoroutine(HideAfterDelay(delay));
    }

    private IEnumerator HideAfterDelay(float delay)
    {
        if (delay > 0f)
        {
            yield return new WaitForSecondsRealtime(delay);
        }

        hideCoroutine = null;
        Hide();
    }

    private void StopHideCoroutine()
    {
        if (hideCoroutine == null)
        {
            return;
        }

        StopCoroutine(hideCoroutine);
        hideCoroutine = null;
    }

    private void StartProgressAnimationIfNeeded()
    {
        if (!animateProgress || progressFillImage == null || progressCoroutine != null)
        {
            return;
        }

        progressCoroutine = StartCoroutine(AnimateProgressFill());
    }

    private IEnumerator AnimateProgressFill()
    {
        while (isVisible && animateProgress && progressFillImage != null)
        {
            progressFillImage.fillAmount = Mathf.Repeat(Time.unscaledTime * Mathf.Max(0f, progressAnimationSpeed), 1f);
            yield return null;
        }

        progressCoroutine = null;
    }

    private void StopProgressAnimation()
    {
        if (progressCoroutine != null)
        {
            StopCoroutine(progressCoroutine);
            progressCoroutine = null;
        }

        if (progressFillImage != null)
        {
            progressFillImage.fillAmount = 0f;
        }
    }

    private void ApplyVisibility(bool visible)
    {
        isVisible = visible;

        GameObject targetObject = GetOverlayRoot();
        if (targetObject != null && (targetObject != gameObject || visible))
        {
            targetObject.SetActive(visible);
        }

        if (overlayCanvasGroup != null)
        {
            overlayCanvasGroup.alpha = visible ? 1f : 0f;
            overlayCanvasGroup.interactable = visible;
            overlayCanvasGroup.blocksRaycasts = visible;
        }
    }

    private void SetLoadingTitle(string message)
    {
        if (loadingTitleText != null)
        {
            loadingTitleText.text = message ?? string.Empty;
        }
    }

    private void SetLoadingMessage(string message)
    {
        if (loadingMessageText != null)
        {
            loadingMessageText.text = message ?? string.Empty;
        }
    }

    private void SetErrorMessage(string message)
    {
        if (errorMessageText != null)
        {
            errorMessageText.text = message ?? string.Empty;
        }
    }

    private GameObject GetOverlayRoot()
    {
        return overlayRoot != null ? overlayRoot : gameObject;
    }

    private void OnValidate()
    {
        hideDelayAfterSuccess = Mathf.Max(0f, hideDelayAfterSuccess);
        hideDelayAfterFailure = Mathf.Max(0f, hideDelayAfterFailure);
        progressAnimationSpeed = Mathf.Max(0f, progressAnimationSpeed);
    }
}
