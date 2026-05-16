using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class SplashSequenceController : MonoBehaviour
{
    [SerializeField, Tooltip("처음 보여줄 Project One 대표 이미지입니다.")]
    private Graphic projectOneImage;

    [SerializeField, Tooltip("두 번째로 보여줄 회사 로고 이미지입니다.")]
    private Graphic companyLogoImage;

    [SerializeField, Tooltip("화면 페이드 인/아웃에 사용할 검정 패널입니다.")]
    private Graphic fadePanel;

    [SerializeField, Tooltip("MainMenu 로드 중 표시할 텍스트입니다. 비워두면 사용하지 않습니다.")]
    private TMP_Text loadingText;

    [SerializeField, Tooltip("스플래시 이후 로드할 MainMenu 씬 이름입니다.")]
    private string mainMenuSceneName = "MainMenu";

    [SerializeField, Tooltip("스플래시 시작 전 대기 시간입니다.")]
    private float initialDelay = 0.3f;

    [SerializeField, Tooltip("Project One 대표 이미지를 유지할 시간입니다.")]
    private float projectOneHoldSeconds = 2f;

    [SerializeField, Tooltip("회사 로고를 유지할 시간입니다.")]
    private float companyLogoHoldSeconds = 1.5f;

    [SerializeField, Tooltip("이미지 전환 페이드 시간입니다.")]
    private float fadeDuration = 0.5f;

    [SerializeField, Tooltip("시퀀스 종료 후 MainMenu 씬을 로드할지 여부입니다.")]
    private bool loadSceneAtEnd = true;

    private Coroutine sequenceRoutine;
    private bool isLoadingMainMenu;

    private void Start()
    {
        InitializeVisuals();
        sequenceRoutine = StartCoroutine(RunSplashSequence());
    }

    public void SkipToMainMenu()
    {
        if (sequenceRoutine != null)
        {
            StopCoroutine(sequenceRoutine);
            sequenceRoutine = null;
        }

        StartCoroutine(LoadMainMenuRoutine());
    }

    private IEnumerator RunSplashSequence()
    {
        yield return WaitUnscaled(initialDelay);

        yield return ShowSplashGraphic(projectOneImage);
        yield return WaitUnscaled(projectOneHoldSeconds);
        yield return HideSplashGraphic(projectOneImage);

        yield return ShowSplashGraphic(companyLogoImage);
        yield return WaitUnscaled(companyLogoHoldSeconds);
        yield return HideSplashGraphic(companyLogoImage);

        sequenceRoutine = null;

        if (loadSceneAtEnd)
        {
            yield return LoadMainMenuRoutine();
        }
    }

    private IEnumerator ShowSplashGraphic(Graphic graphic)
    {
        if (fadePanel != null)
        {
            SetGraphicAlpha(graphic, 1f);
            yield return FadePanelAlpha(0f);
            yield break;
        }

        yield return FadeGraphicAlpha(graphic, 1f);
    }

    private IEnumerator HideSplashGraphic(Graphic graphic)
    {
        if (fadePanel != null)
        {
            yield return FadePanelAlpha(1f);
            SetGraphicAlpha(graphic, 0f);
            yield break;
        }

        yield return FadeGraphicAlpha(graphic, 0f);
    }

    private IEnumerator LoadMainMenuRoutine()
    {
        if (isLoadingMainMenu || string.IsNullOrWhiteSpace(mainMenuSceneName))
        {
            yield break;
        }

        isLoadingMainMenu = true;
        SetLoadingTextVisible(true);

        AsyncOperation loadOperation = SceneManager.LoadSceneAsync(mainMenuSceneName);
        if (loadOperation == null)
        {
            isLoadingMainMenu = false;
            yield break;
        }

        while (!loadOperation.isDone)
        {
            yield return null;
        }
    }

    private IEnumerator FadePanelAlpha(float targetAlpha)
    {
        yield return FadeGraphicAlpha(fadePanel, targetAlpha, true);
    }

    private IEnumerator FadeGraphicAlpha(Graphic graphic, float targetAlpha, bool forceBlack = false)
    {
        if (graphic == null)
        {
            yield break;
        }

        float duration = Mathf.Max(0f, fadeDuration);
        float startAlpha = graphic.color.a;
        targetAlpha = Mathf.Clamp01(targetAlpha);

        if (duration <= 0f)
        {
            SetGraphicAlpha(graphic, targetAlpha, forceBlack);
            yield break;
        }

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            SetGraphicAlpha(graphic, Mathf.Lerp(startAlpha, targetAlpha, t), forceBlack);
            yield return null;
        }

        SetGraphicAlpha(graphic, targetAlpha, forceBlack);
    }

    private IEnumerator WaitUnscaled(float seconds)
    {
        float duration = Mathf.Max(0f, seconds);
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            yield return null;
        }
    }

    private void InitializeVisuals()
    {
        SetGraphicAlpha(projectOneImage, 0f);
        SetGraphicAlpha(companyLogoImage, 0f);
        SetGraphicAlpha(fadePanel, 1f, true);
        SetLoadingTextVisible(false);
    }

    private void SetGraphicAlpha(Graphic graphic, float alpha, bool forceBlack = false)
    {
        if (graphic == null)
        {
            return;
        }

        Color color = graphic.color;
        if (forceBlack)
        {
            color.r = 0f;
            color.g = 0f;
            color.b = 0f;
        }

        color.a = Mathf.Clamp01(alpha);
        graphic.color = color;
    }

    private void SetLoadingTextVisible(bool visible)
    {
        if (loadingText == null)
        {
            return;
        }

        loadingText.text = visible ? "Loading..." : string.Empty;
        SetGraphicAlpha(loadingText, visible ? 1f : 0f);
    }

    private void OnValidate()
    {
        initialDelay = Mathf.Max(0f, initialDelay);
        projectOneHoldSeconds = Mathf.Max(0f, projectOneHoldSeconds);
        companyLogoHoldSeconds = Mathf.Max(0f, companyLogoHoldSeconds);
        fadeDuration = Mathf.Max(0f, fadeDuration);
    }
}
