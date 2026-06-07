using UnityEngine;

namespace ProjectONE.UI
{
    /// <summary>Makes a Screen Space Overlay UI element follow a world-space Transform.</summary>
    public class ProjectOneWorldUIFollowTarget : MonoBehaviour
    {
        [SerializeField] private Transform target;
        [SerializeField] private Camera worldCamera;
        [SerializeField] private RectTransform canvasRect;
        [SerializeField] private Vector3 worldOffset = new Vector3(0.8f, 1.2f, 0f);
        [SerializeField] private Vector2 screenOffset = Vector2.zero;
        [SerializeField] private bool hideWhenBehindCamera = true;

        private RectTransform rectTransform;

        private void Awake()
        {
            rectTransform = transform as RectTransform;
            if (worldCamera == null) worldCamera = Camera.main;
            if (canvasRect == null)
            {
                Canvas canvas = GetComponentInParent<Canvas>();
                if (canvas != null) canvasRect = canvas.transform as RectTransform;
            }
        }

        private void LateUpdate()
        {
            if (target == null || worldCamera == null || canvasRect == null || rectTransform == null) return;
            Vector3 screenPoint = worldCamera.WorldToScreenPoint(target.position + worldOffset);
            if (hideWhenBehindCamera && screenPoint.z < 0f)
            {
                if (gameObject.activeSelf) gameObject.SetActive(false);
                return;
            }
            if (!gameObject.activeSelf) gameObject.SetActive(true);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screenPoint, null, out Vector2 localPoint);
            rectTransform.anchoredPosition = localPoint + screenOffset;
        }
        public void SetTarget(Transform newTarget) => target = newTarget;
    }
}
