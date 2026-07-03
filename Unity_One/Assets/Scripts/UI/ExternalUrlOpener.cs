using System;
using UnityEngine;
using UnityEngine.UI;

public sealed class ExternalUrlOpener : MonoBehaviour
{
    [Header("External URL")]
    [SerializeField] private string url = "https://discord.gg/YOUR_INVITE_CODE";

    [Header("Button Binding")]
    [SerializeField] private bool autoBindButtonOnSameObject = true;

    [Header("Debug")]
    [SerializeField] private bool logOpenUrl = true;

    private Button boundButton;
    private bool addedRuntimeListener;

    private void OnEnable()
    {
        TryAutoBindButton();
    }

    private void OnDisable()
    {
        if (addedRuntimeListener && boundButton != null)
        {
            boundButton.onClick.RemoveListener(Open);
        }

        addedRuntimeListener = false;
        boundButton = null;
    }

    private void TryAutoBindButton()
    {
        if (!autoBindButtonOnSameObject)
        {
            return;
        }

        if (!TryGetComponent(out boundButton))
        {
            boundButton = null;
            Debug.LogWarning("[ExternalUrlOpener] Button component was not found on the same GameObject.", this);
            return;
        }

        if (HasPersistentOpenListener(boundButton))
        {
            if (logOpenUrl)
            {
                Debug.Log("[ExternalUrlOpener] Persistent Button listener already targets Open().", this);
            }

            return;
        }

        addedRuntimeListener = false;
        boundButton.onClick.RemoveListener(Open);
        boundButton.onClick.AddListener(Open);
        addedRuntimeListener = true;

        if (logOpenUrl)
        {
            Debug.Log("[ExternalUrlOpener] Auto-bound Button.onClick to Open().", this);
        }
    }

    private bool HasPersistentOpenListener(Button button)
    {
        int listenerCount = button.onClick.GetPersistentEventCount();
        for (int i = 0; i < listenerCount; i++)
        {
            if (button.onClick.GetPersistentTarget(i) == this &&
                button.onClick.GetPersistentMethodName(i) == nameof(Open))
            {
                return true;
            }
        }

        return false;
    }

    public void Open()
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            Debug.LogWarning("[ExternalUrlOpener] URL is empty.", this);
            return;
        }

        bool isWebUrl =
            url.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
            url.StartsWith("http://", StringComparison.OrdinalIgnoreCase);

        if (!isWebUrl)
        {
            Debug.LogWarning($"[ExternalUrlOpener] Unsupported URL format: {url}", this);
            return;
        }

        if (logOpenUrl)
        {
            Debug.Log($"[ExternalUrlOpener] Open URL: {url}", this);
        }

        Application.OpenURL(url);
    }
}
