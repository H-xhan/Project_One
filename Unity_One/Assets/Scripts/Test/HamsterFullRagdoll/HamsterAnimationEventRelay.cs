using UnityEngine;

public sealed class HamsterAnimationEventRelay : MonoBehaviour
{
    [SerializeField] private HamsterRagdollGrabber grabber;
    [SerializeField] private bool autoFindGrabber = true;
    [SerializeField] private bool debugLogs = false;
    [SerializeField] private bool ignoreFaceEvents = true;

    private bool _warnedMissingGrabber;

    private void Awake()
    {
        if (autoFindGrabber)
            ResolveGrabber();
    }

    private void OnEnable()
    {
        if (autoFindGrabber)
            ResolveGrabber();
    }

    public void AnimEvent_AttachHeldItem()
    {
        ForwardToGrabber("AnimEvent_AttachHeldItem", target => target.CompletePendingGrabFromAnimationEvent());
    }

    public void AnimEvent_ThrowHeldItem()
    {
        ForwardToGrabber("AnimEvent_ThrowHeldItem", target => target.CompletePendingThrowFromAnimationEvent());
    }

    public void AnimEvent_ReleaseHeldItem()
    {
        ForwardToGrabber("AnimEvent_ReleaseHeldItem", target => target.CompletePendingThrowFromAnimationEvent());
    }

    public void AnimEvent_DetachHeldItem()
    {
        ForwardToGrabber("AnimEvent_DetachHeldItem", target => target.CompletePendingThrowFromAnimationEvent());
    }

    public void AnimEvent_DropHeldItem()
    {
        ForwardToGrabber("AnimEvent_DropHeldItem", target => target.CompletePendingDropFromAnimationEvent());
    }

    public void Face_HoldIndex()
    {
        if (debugLogs && ignoreFaceEvents)
            Debug.Log($"[HamsterAnimationEventRelay:{name}] Face_HoldIndex ignored.", this);
    }

    private void ForwardToGrabber(string eventName, System.Action<HamsterRagdollGrabber> action)
    {
        if (grabber == null && autoFindGrabber)
            ResolveGrabber();

        if (grabber == null)
        {
            if (!_warnedMissingGrabber)
            {
                Debug.LogWarning($"[HamsterAnimationEventRelay:{name}] {eventName} ignored because HamsterRagdollGrabber was not found.", this);
                _warnedMissingGrabber = true;
            }

            return;
        }

        _warnedMissingGrabber = false;
        if (debugLogs)
            Debug.Log($"[HamsterAnimationEventRelay:{name}] Forward {eventName} to {grabber.name}.", this);

        action?.Invoke(grabber);
    }

    private void ResolveGrabber()
    {
        if (grabber != null)
            return;

        grabber = GetComponentInParent<HamsterRagdollGrabber>();
        if (grabber != null)
            return;

        GameObject motorShellBody = GameObject.Find("MotorShellBody");
        if (motorShellBody != null)
        {
            grabber = motorShellBody.GetComponent<HamsterRagdollGrabber>();
            if (grabber != null)
                return;
        }

        HamsterRagdollGrabber[] candidates = UnityEngine.Object.FindObjectsByType<HamsterRagdollGrabber>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);
        if (candidates == null || candidates.Length == 0)
            return;

        if (candidates.Length == 1)
        {
            grabber = candidates[0];
            return;
        }

        Debug.LogWarning($"[HamsterAnimationEventRelay:{name}] Multiple HamsterRagdollGrabber instances found. Assign grabber manually.", this);
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (autoFindGrabber)
            ResolveGrabber();
    }
#endif
}
