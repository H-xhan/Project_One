using UnityEngine;

public sealed class HamsterAnimationEventRelay : MonoBehaviour
{
    [SerializeField] private HamsterRagdollGrabber grabber;
    [SerializeField] private HamsterMotorShellCombatAdapter combatAdapter;
    [SerializeField] private bool autoFindGrabber = true;
    [SerializeField] private bool autoFindCombatAdapter = true;
    [SerializeField] private bool debugLogs = false;
    [SerializeField] private bool ignoreFaceEvents = true;

    private bool _warnedMissingGrabber;
    private bool _warnedMissingCombatAdapter;

    private void Awake()
    {
        if (autoFindGrabber)
            ResolveGrabber();
        if (autoFindCombatAdapter)
            ResolveCombatAdapter();
    }

    private void OnEnable()
    {
        if (autoFindGrabber)
            ResolveGrabber();
        if (autoFindCombatAdapter)
            ResolveCombatAdapter();
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

    public void AnimEvent_AttackHit()
    {
        ForwardToCombatAdapter("AnimEvent_AttackHit");
    }

    public void AttackHit()
    {
        ForwardToCombatAdapter("AttackHit");
    }

    public void OnAttackHit()
    {
        ForwardToCombatAdapter("OnAttackHit");
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

    private void ForwardToCombatAdapter(string eventName)
    {
        if (combatAdapter == null && autoFindCombatAdapter)
            ResolveCombatAdapter();

        if (combatAdapter == null)
        {
            if (!_warnedMissingCombatAdapter)
            {
                Debug.LogWarning($"[HamsterAnimationEventRelay:{name}] {eventName} ignored because HamsterMotorShellCombatAdapter was not found.", this);
                _warnedMissingCombatAdapter = true;
            }

            return;
        }

        _warnedMissingCombatAdapter = false;
        if (debugLogs)
            Debug.Log($"[HamsterAnimationEventRelay:{name}] Forward {eventName} to {combatAdapter.name}.", this);

        combatAdapter.CompletePendingAttackFromAnimationEvent();
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

    private void ResolveCombatAdapter()
    {
        if (combatAdapter != null)
            return;

        combatAdapter = GetComponentInParent<HamsterMotorShellCombatAdapter>();
        if (combatAdapter != null)
            return;

        combatAdapter = GetComponentInChildren<HamsterMotorShellCombatAdapter>(true);
        if (combatAdapter != null)
            return;

        HamsterMotorShellCombatAdapter[] candidates = UnityEngine.Object.FindObjectsByType<HamsterMotorShellCombatAdapter>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);
        if (candidates == null || candidates.Length == 0)
            return;

        if (candidates.Length == 1)
        {
            combatAdapter = candidates[0];
            return;
        }

        Debug.LogWarning($"[HamsterAnimationEventRelay:{name}] Multiple HamsterMotorShellCombatAdapter instances found. Assign combatAdapter manually.", this);
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (autoFindGrabber)
            ResolveGrabber();
        if (autoFindCombatAdapter)
            ResolveCombatAdapter();
    }
#endif
}
