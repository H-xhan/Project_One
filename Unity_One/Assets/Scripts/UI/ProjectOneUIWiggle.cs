using UnityEngine;

[DisallowMultipleComponent]
public sealed class ProjectOneUIWiggle : MonoBehaviour
{
    [SerializeField] private RectTransform target;
    [SerializeField] private bool playOnEnable = true;
    [SerializeField] private float rotationAmplitude = 1.6f;
    [SerializeField] private float scaleAmplitude = 0f;
    [SerializeField] private float period = 2.2f;

    private Quaternion baseRotation;
    private Vector3 baseScale;
    private float seed;
    private bool hasBaseTransform;

    private void Awake()
    {
        Cache();
        CaptureBaseTransformIfNeeded();
        seed = Random.Range(0f, 10f);
    }

    private void OnEnable()
    {
        Cache();
        CaptureBaseTransformIfNeeded();
    }

    private void OnDisable()
    {
        if (target == null || !hasBaseTransform)
            return;

        target.localRotation = baseRotation;
        target.localScale = baseScale;
    }

    private void Update()
    {
        if (!playOnEnable || target == null)
            return;

        float safePeriod = Mathf.Max(0.1f, period);
        float t = ((Time.unscaledTime + seed) / safePeriod) * Mathf.PI * 2f;
        float wave = Mathf.Sin(t);
        float softWave = Mathf.Sin(t * 0.7f);

        target.localRotation = baseRotation * Quaternion.Euler(0f, 0f, wave * rotationAmplitude);

        if (scaleAmplitude > 0f)
            target.localScale = baseScale * (1f + softWave * scaleAmplitude);
    }

    public void SetPlaying(bool isPlaying)
    {
        playOnEnable = isPlaying;

        if (!isPlaying && target != null && hasBaseTransform)
        {
            target.localRotation = baseRotation;
            target.localScale = baseScale;
        }
    }

    private void Cache()
    {
        if (target == null)
            target = transform as RectTransform;
    }

    private void CaptureBaseTransformIfNeeded()
    {
        if (hasBaseTransform || target == null)
            return;

        baseRotation = target.localRotation;
        baseScale = target.localScale;
        hasBaseTransform = true;
    }
}
