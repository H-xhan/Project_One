using UnityEngine;

[DisallowMultipleComponent]
public sealed class ProjectOneCodeOnlyWiggle : MonoBehaviour
{
    [SerializeField] private RectTransform target;
    [SerializeField] private bool playOnEnable = true;
    [SerializeField] private float rotationAmplitude = 1.4f;
    [SerializeField] private float period = 2.4f;

    private Quaternion baseRotation;
    private float seed;
    private bool hasBaseRotation;

    private void Awake()
    {
        Cache();
        CaptureBaseIfNeeded();
        seed = Random.Range(0f, 10f);
    }

    private void OnEnable()
    {
        Cache();
        CaptureBaseIfNeeded();
    }

    private void OnDisable()
    {
        if (target != null && hasBaseRotation)
            target.localRotation = baseRotation;
    }

    private void Update()
    {
        if (!playOnEnable || target == null)
            return;
        float t = ((Time.unscaledTime + seed) / Mathf.Max(0.1f, period)) * Mathf.PI * 2f;
        target.localRotation = baseRotation * Quaternion.Euler(0f, 0f, Mathf.Sin(t) * rotationAmplitude);
    }

    private void Cache()
    {
        if (target == null)
            target = transform as RectTransform;
    }

    private void CaptureBaseIfNeeded()
    {
        if (hasBaseRotation || target == null)
            return;
        baseRotation = target.localRotation;
        hasBaseRotation = true;
    }
}
