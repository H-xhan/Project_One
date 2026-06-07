using UnityEngine;
using UnityEngine.UI;

namespace ProjectONE.UI
{
    /// <summary>
    /// Controls the curved stamina gauge by changing the height of a RectMask2D fill viewport.
    /// Hierarchy suggestion:
    /// StaminaGaugeRoot
    /// ├── BackgroundImage       // StaminaArc_background_empty_aligned.png
    /// └── FillMask              // RectMask2D, anchored to bottom
    ///     └── FillImage         // StaminaArc_fill_green_aligned.png, same size/position as background
    /// </summary>
    public class ProjectOneStaminaArcGaugeUI : MonoBehaviour
    {
        [Range(0f, 1f)] [SerializeField] private float normalizedValue = 1f;
        [SerializeField] private RectTransform fillMask;
        [SerializeField] private RectTransform fillImage;
        [SerializeField] private bool fillFromBottom = true;

        private float fullMaskHeight;
        private Vector2 originalMaskSize;
        private Vector2 originalMaskAnchoredPosition;

        private void Awake()
        {
            CacheInitialValues();
            ApplyValue();
        }

        private void OnValidate()
        {
            if (!Application.isPlaying) CacheInitialValues();
            ApplyValue();
        }

        private void CacheInitialValues()
        {
            if (fillMask == null) return;
            originalMaskSize = fillMask.sizeDelta;
            originalMaskAnchoredPosition = fillMask.anchoredPosition;
            fullMaskHeight = Mathf.Max(1f, originalMaskSize.y);
        }

        public void SetValue(float value01)
        {
            normalizedValue = Mathf.Clamp01(value01);
            ApplyValue();
        }

        private void ApplyValue()
        {
            if (fillMask == null) return;
            if (fullMaskHeight <= 0f) CacheInitialValues();

            float height = fullMaskHeight * Mathf.Clamp01(normalizedValue);
            Vector2 size = originalMaskSize;
            size.y = height;
            fillMask.sizeDelta = size;

            fillMask.pivot = new Vector2(fillMask.pivot.x, fillFromBottom ? 0f : 1f);
            fillMask.anchoredPosition = originalMaskAnchoredPosition;

            if (fillImage != null) fillImage.anchoredPosition = Vector2.zero;
        }
    }
}
