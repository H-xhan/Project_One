using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;

[DisallowMultipleComponent]
public sealed class ProjectOneCodeOnlyAnimator : MonoBehaviour,
    IPointerEnterHandler,
    IPointerExitHandler,
    IPointerDownHandler,
    IPointerUpHandler
{
    [SerializeField] private RectTransform target;
    [SerializeField] private CanvasGroup canvasGroup;
    [SerializeField] private bool playIntroOnEnable = true;
    [SerializeField] private bool enablePointerAnimation = true;
    [SerializeField] private float introStartScale = 0.88f;
    [SerializeField] private float hoverScale = 1.045f;
    [SerializeField] private float pressScale = 0.955f;
    [SerializeField] private float scaleDuration = 0.09f;
    [SerializeField] private float introDuration = 0.18f;
    [SerializeField] private AnimationCurve ease = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    private Vector3 baseScale = Vector3.one;
    private bool hasBaseScale;
    private bool isPointerInside;
    private Coroutine scaleRoutine;
    private Coroutine introRoutine;

    private void Awake()
    {
        Cache();
        CaptureBaseScaleIfNeeded();
    }

    private void OnEnable()
    {
        Cache();
        CaptureBaseScaleIfNeeded();
        if (playIntroOnEnable)
            PlayPopIn();
    }

    private void OnDisable()
    {
        StopAllLocalRoutines();
        if (target != null && hasBaseScale)
            target.localScale = baseScale;
        if (canvasGroup != null)
            canvasGroup.alpha = 1f;
        isPointerInside = false;
    }

    public void Configure(float newHoverScale, float newPressScale, float newScaleDuration, float newIntroDuration)
    {
        hoverScale = newHoverScale;
        pressScale = newPressScale;
        scaleDuration = newScaleDuration;
        introDuration = newIntroDuration;
    }

    public void SetPointerAnimationEnabled(bool enabled)
    {
        enablePointerAnimation = enabled;
    }

    public void PlayPopIn()
    {
        if (!isActiveAndEnabled)
            return;
        Cache();
        CaptureBaseScaleIfNeeded();
        StopIntroRoutine();
        introRoutine = StartCoroutine(PopInRoutine());
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (!enablePointerAnimation)
            return;
        isPointerInside = true;
        AnimateTo(baseScale * hoverScale);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (!enablePointerAnimation)
            return;
        isPointerInside = false;
        AnimateTo(baseScale);
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        if (!enablePointerAnimation)
            return;
        AnimateTo(baseScale * pressScale);
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        if (!enablePointerAnimation)
            return;
        AnimateTo(isPointerInside ? baseScale * hoverScale : baseScale);
    }

    private void Cache()
    {
        if (target == null)
            target = transform as RectTransform;
        if (canvasGroup == null)
            TryGetComponent(out canvasGroup);
    }

    private void CaptureBaseScaleIfNeeded()
    {
        if (hasBaseScale || target == null)
            return;
        baseScale = target.localScale;
        hasBaseScale = true;
    }

    private void AnimateTo(Vector3 nextScale)
    {
        if (!isActiveAndEnabled || target == null)
            return;
        StopScaleRoutine();
        scaleRoutine = StartCoroutine(ScaleRoutine(nextScale, scaleDuration));
    }

    private IEnumerator PopInRoutine()
    {
        if (target == null)
            yield break;
        StopScaleRoutine();
        Vector3 from = baseScale * introStartScale;
        Vector3 to = baseScale;
        target.localScale = from;
        if (canvasGroup != null)
            canvasGroup.alpha = 0f;

        float elapsed = 0f;
        while (elapsed < introDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / Mathf.Max(0.0001f, introDuration));
            float eased = ease.Evaluate(t);
            target.localScale = Vector3.LerpUnclamped(from, to, eased);
            if (canvasGroup != null)
                canvasGroup.alpha = Mathf.Lerp(0f, 1f, eased);
            yield return null;
        }

        target.localScale = to;
        if (canvasGroup != null)
            canvasGroup.alpha = 1f;
        introRoutine = null;
    }

    private IEnumerator ScaleRoutine(Vector3 nextScale, float duration)
    {
        Vector3 start = target.localScale;
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / Mathf.Max(0.0001f, duration));
            target.localScale = Vector3.LerpUnclamped(start, nextScale, ease.Evaluate(t));
            yield return null;
        }
        target.localScale = nextScale;
        scaleRoutine = null;
    }

    private void StopAllLocalRoutines()
    {
        StopScaleRoutine();
        StopIntroRoutine();
    }

    private void StopScaleRoutine()
    {
        if (scaleRoutine == null)
            return;
        StopCoroutine(scaleRoutine);
        scaleRoutine = null;
    }

    private void StopIntroRoutine()
    {
        if (introRoutine == null)
            return;
        StopCoroutine(introRoutine);
        introRoutine = null;
    }
}
