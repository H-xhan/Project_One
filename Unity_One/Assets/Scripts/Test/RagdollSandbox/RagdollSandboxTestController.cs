using System;
using System.Reflection;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

public class RagdollSandboxTestController : MonoBehaviour
{
    private const BindingFlags RagdollMethodFlags =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private static readonly string[] ImpulseTargetNameHints =
    {
        "BodyCore",
        "Core",
        "Body",
        "몸통"
    };

    [Header("References")]
    [SerializeField] private SugaActiveRagdollController ragdollController;
    [SerializeField] private Animator animator;
    [SerializeField] private Rigidbody rootRigidbody;
    [SerializeField] private CharacterController characterController;
    [SerializeField] private Rigidbody impulseTargetRigidbody;
    [SerializeField] private Rigidbody[] managedRigidbodies;

    [Header("Auto Setup")]
    [SerializeField] private bool autoCollectReferences = true;
    [SerializeField] private bool disableCharacterControllerOnStart = true;
    [SerializeField] private bool disableAnimatorWhenRagdollActive = false;
    [SerializeField] private bool makeRigidbodiesNonKinematicOnStart = false;

    [Header("Impulse")]
    [SerializeField] private float hitForwardImpulse = 6f;
    [SerializeField] private float hitUpImpulse = 2f;
    [SerializeField] private float throwForwardImpulse = 12f;
    [SerializeField] private float throwUpImpulse = 4f;
    [SerializeField] private float upwardImpulse = 6f;
    [SerializeField] private float torqueImpulse = 4f;

    [Header("Debug")]
    [SerializeField] private bool showOnGuiHelp = true;
    [SerializeField] private bool debugLogs = true;
    [SerializeField] private int guiFontSize = 24;
    [SerializeField] private float guiBoxWidth = 420f;
    [SerializeField] private float guiBoxHeight = 300f;
    [SerializeField] private bool allowTargetCycling = true;

    private Vector3 _initialPosition;
    private Quaternion _initialRotation = Quaternion.identity;
    private bool _hasInitialPose;
    private bool _animatorDisabledBySandbox;

    private void Reset()
    {
        CollectReferences();
        CacheInitialPose();
    }

    private void Awake()
    {
        if (autoCollectReferences)
            CollectReferences();

        CacheInitialPose();
    }

    private void Start()
    {
        if (disableCharacterControllerOnStart && characterController != null)
        {
            characterController.enabled = false;
            Log("CharacterController disabled for local ragdoll sandbox.");
        }

        if (makeRigidbodiesNonKinematicOnStart)
        {
            SetAllManagedRigidbodiesKinematic(false, "Start");
            Log("Managed rigidbodies set to non-kinematic on start.");
        }
    }

    private void Update()
    {
        if (WasPressedThisFrame(SandboxKey.Normal))
            InvokeStateMethod("SetNormal", true);

        if (WasPressedThisFrame(SandboxKey.Stunned))
            InvokeStateMethod("SetStunned", false);

        if (WasPressedThisFrame(SandboxKey.Limp))
            InvokeStateMethod("SetLimp", false);

        if (WasPressedThisFrame(SandboxKey.Hit))
            ApplyHitImpulse();

        if (WasPressedThisFrame(SandboxKey.Throw))
            ApplyThrowImpulse();

        if (WasPressedThisFrame(SandboxKey.Up))
            ApplyUpwardImpulse();

        if (WasPressedThisFrame(SandboxKey.Reset))
            ResetRagdollSandboxPose();

        if (WasPressedThisFrame(SandboxKey.Animator))
            ToggleAnimator();

        if (WasPressedThisFrame(SandboxKey.Kinematic))
            ToggleManagedRigidbodiesKinematic();

        if (WasPressedThisFrame(SandboxKey.NonKinematic))
            SetAllManagedRigidbodiesKinematic(false, "N");

        if (WasPressedThisFrame(SandboxKey.AllKinematic))
            SetAllManagedRigidbodiesKinematic(true, "M");

        if (WasPressedThisFrame(SandboxKey.PreviousTarget))
            CycleImpulseTarget(-1);

        if (WasPressedThisFrame(SandboxKey.NextTarget))
            CycleImpulseTarget(1);
    }

    private void OnGUI()
    {
        if (!showOnGuiHelp)
            return;

        Rigidbody target = impulseTargetRigidbody;
        string targetName = target != null ? target.name : "None";
        string animatorState = animator != null ? animator.enabled.ToString() : "None";
        string targetKinematic = target != null ? target.isKinematic.ToString() : "None";
        int managedCount = CountManagedRigidbodies();

        GUIStyle boxStyle = new GUIStyle(GUI.skin.box)
        {
            alignment = TextAnchor.UpperLeft,
            fontSize = Mathf.Max(10, guiFontSize),
            padding = new RectOffset(16, 16, 14, 14)
        };

        GUIStyle labelStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = Mathf.Max(10, guiFontSize),
            wordWrap = false
        };
        labelStyle.normal.textColor = Color.white;

        GUILayout.BeginArea(new Rect(10f, 10f, Mathf.Max(260f, guiBoxWidth), Mathf.Max(180f, guiBoxHeight)), boxStyle);
        GUILayout.Label("Ragdoll Sandbox Test", labelStyle);
        GUILayout.Space(4f);
        GUILayout.Label("1 Normal | 2 Stunned | 3 Limp", labelStyle);
        GUILayout.Label("H Hit | T Throw | Space Up | R Reset", labelStyle);
        GUILayout.Label("A Animator | K Toggle Kinematic", labelStyle);
        GUILayout.Label("N All Dynamic | M All Kinematic", labelStyle);
        GUILayout.Label("[ or , Prev Target | ] or . Next Target", labelStyle);
        GUILayout.Space(10f);
        GUILayout.Label($"Target Rigidbody: {targetName}", labelStyle);
        GUILayout.Label($"Target isKinematic: {targetKinematic}", labelStyle);
        GUILayout.Label($"Animator Enabled: {animatorState}", labelStyle);
        GUILayout.Label($"Managed Rigidbody Count: {managedCount}", labelStyle);
        GUILayout.EndArea();
    }

    private void CollectReferences()
    {
        if (ragdollController == null)
        {
            ragdollController = GetComponent<SugaActiveRagdollController>();
            if (ragdollController == null)
                ragdollController = GetComponentInChildren<SugaActiveRagdollController>(true);
        }

        if (animator == null)
            animator = GetComponentInChildren<Animator>(true);

        if (rootRigidbody == null)
            rootRigidbody = GetComponent<Rigidbody>();

        if (characterController == null)
            characterController = GetComponent<CharacterController>();

        if (managedRigidbodies == null || managedRigidbodies.Length == 0)
            managedRigidbodies = GetComponentsInChildren<Rigidbody>(true);

        if (impulseTargetRigidbody == null)
            impulseTargetRigidbody = FindPreferredImpulseTarget(managedRigidbodies);
    }

    private Rigidbody FindPreferredImpulseTarget(Rigidbody[] rigidbodies)
    {
        if (rigidbodies == null || rigidbodies.Length == 0)
            return rootRigidbody;

        for (int i = 0; i < rigidbodies.Length; i++)
        {
            Rigidbody rb = rigidbodies[i];
            if (rb == null || rb == rootRigidbody)
                continue;

            if (NameContainsImpulseHint(rb.transform))
                return rb;
        }

        for (int i = 0; i < rigidbodies.Length; i++)
        {
            Rigidbody rb = rigidbodies[i];
            if (rb != null && rb != rootRigidbody)
                return rb;
        }

        return rootRigidbody;
    }

    private static bool NameContainsImpulseHint(Transform target)
    {
        Transform current = target;
        while (current != null)
        {
            for (int i = 0; i < ImpulseTargetNameHints.Length; i++)
            {
                if (current.name.IndexOf(ImpulseTargetNameHints[i], StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }

            current = current.parent;
        }

        return false;
    }

    private void InvokeStateMethod(string methodName, bool shouldRestoreAnimator)
    {
        bool invoked = TryInvokeRagdollMethod(methodName);
        if (!invoked)
        {
            Log($"Ragdoll method '{methodName}' was not found or could not be invoked.");
            return;
        }

        if (shouldRestoreAnimator)
            RestoreAnimatorIfDisabledBySandbox();
        else if (disableAnimatorWhenRagdollActive)
            SetAnimatorEnabled(false, methodName);

        Log($"Ragdoll method '{methodName}' invoked.");
    }

    private bool TryInvokeRagdollMethod(string methodName)
    {
        if (ragdollController == null)
        {
            if (autoCollectReferences)
                CollectReferences();

            if (ragdollController == null)
                return false;
        }

        MethodInfo method = ragdollController.GetType().GetMethod(methodName, RagdollMethodFlags);
        if (method == null)
            return false;

        if (method.GetParameters().Length != 0)
            return false;

        try
        {
            method.Invoke(ragdollController, null);
            return true;
        }
        catch (Exception exception)
        {
            LogWarning($"Ragdoll method '{methodName}' invoke failed: {exception.Message}");
            return false;
        }
    }

    private void ApplyHitImpulse()
    {
        Vector3 impulse = GetSafeForward() * hitForwardImpulse + Vector3.up * hitUpImpulse;
        ApplyImpulse("Hit", impulse, transform.right * torqueImpulse);
    }

    private void ApplyThrowImpulse()
    {
        Vector3 impulse = GetSafeForward() * throwForwardImpulse + Vector3.up * throwUpImpulse;
        ApplyImpulse("Throw", impulse, transform.right * torqueImpulse);
    }

    private void ApplyUpwardImpulse()
    {
        ApplyImpulse("Up", Vector3.up * upwardImpulse, Vector3.zero);
    }

    private void ApplyImpulse(string label, Vector3 impulse, Vector3 torque)
    {
        Rigidbody target = GetImpulseTarget();
        if (target == null)
        {
            LogWarning($"{label} impulse skipped because no target Rigidbody is assigned.");
            return;
        }

        if (target.isKinematic)
        {
            LogWarning($"{label} impulse skipped because target Rigidbody '{target.name}' is kinematic. Press N or K first.");
            return;
        }

        if (!IsFinite(impulse) || !IsFinite(torque))
        {
            LogWarning($"{label} impulse skipped because impulse or torque is invalid.");
            return;
        }

        target.WakeUp();
        target.AddForce(impulse, ForceMode.Impulse);

        if (torque.sqrMagnitude > 0f)
            target.AddTorque(torque, ForceMode.Impulse);

        if (disableAnimatorWhenRagdollActive)
            SetAnimatorEnabled(false, label);

        Log($"{label} impulse applied target={target.name} impulse={impulse} torque={torque}");
    }

    private Rigidbody GetImpulseTarget()
    {
        if (impulseTargetRigidbody == null && autoCollectReferences)
            CollectReferences();

        return impulseTargetRigidbody;
    }

    private Vector3 GetSafeForward()
    {
        Vector3 forward = transform.forward;
        if (!IsFinite(forward) || forward.sqrMagnitude <= 0.0001f)
            return Vector3.forward;

        return forward.normalized;
    }

    private void ResetRagdollSandboxPose()
    {
        ClearManagedRigidbodyVelocities();

        if (_hasInitialPose)
        {
            transform.SetPositionAndRotation(_initialPosition, _initialRotation);

            if (rootRigidbody != null)
            {
                rootRigidbody.position = _initialPosition;
                rootRigidbody.rotation = _initialRotation;
            }

            Physics.SyncTransforms();
        }

        RestoreAnimatorIfDisabledBySandbox();
        bool invoked = TryInvokeRagdollMethod("SetNormal");
        Log($"Sandbox reset complete. SetNormalInvoked={invoked}");
    }

    private void ToggleAnimator()
    {
        if (animator == null)
        {
            LogWarning("Animator toggle skipped because no Animator is assigned.");
            return;
        }

        animator.enabled = !animator.enabled;
        _animatorDisabledBySandbox = false;
        Log($"Animator enabled={animator.enabled}");
    }

    private void ToggleManagedRigidbodiesKinematic()
    {
        if (managedRigidbodies == null || managedRigidbodies.Length == 0)
        {
            LogWarning("Kinematic toggle skipped because no managed rigidbodies are assigned.");
            return;
        }

        bool hasNonKinematic = false;
        for (int i = 0; i < managedRigidbodies.Length; i++)
        {
            Rigidbody rb = managedRigidbodies[i];
            if (rb != null && !rb.isKinematic)
            {
                hasNonKinematic = true;
                break;
            }
        }

        SetAllManagedRigidbodiesKinematic(hasNonKinematic, "K");
    }

    private void SetAllManagedRigidbodiesKinematic(bool isKinematic, string source)
    {
        if (managedRigidbodies == null)
        {
            LogWarning($"Set kinematic skipped from {source} because no managed rigidbodies are assigned.");
            return;
        }

        int changedCount = 0;
        for (int i = 0; i < managedRigidbodies.Length; i++)
        {
            Rigidbody rb = managedRigidbodies[i];
            if (rb == null)
                continue;

            if (isKinematic)
                ClearRigidbodyVelocity(rb);

            rb.isKinematic = isKinematic;
            changedCount++;
        }

        Rigidbody target = impulseTargetRigidbody;
        string targetState = target != null ? target.isKinematic.ToString() : "None";
        Log($"Managed rigidbodies set isKinematic={isKinematic} source={source} count={changedCount} targetKinematic={targetState}");
    }

    private void CycleImpulseTarget(int direction)
    {
        if (!allowTargetCycling)
            return;

        if (managedRigidbodies == null || managedRigidbodies.Length == 0)
        {
            if (autoCollectReferences)
                CollectReferences();

            if (managedRigidbodies == null || managedRigidbodies.Length == 0)
            {
                LogWarning("Target cycling skipped because no managed rigidbodies are assigned.");
                return;
            }
        }

        int count = managedRigidbodies.Length;
        int currentIndex = Array.IndexOf(managedRigidbodies, impulseTargetRigidbody);
        int startIndex = currentIndex >= 0 ? currentIndex : 0;
        int step = direction >= 0 ? 1 : -1;

        for (int offset = 1; offset <= count; offset++)
        {
            int index = (startIndex + step * offset) % count;
            if (index < 0)
                index += count;

            Rigidbody candidate = managedRigidbodies[index];
            if (candidate == null)
                continue;

            impulseTargetRigidbody = candidate;
            Log($"Impulse target changed to index={index} name={candidate.name} isKinematic={candidate.isKinematic}");
            return;
        }

        LogWarning("Target cycling skipped because all managed rigidbody entries are null.");
    }

    private int CountManagedRigidbodies()
    {
        if (managedRigidbodies == null)
            return 0;

        int count = 0;
        for (int i = 0; i < managedRigidbodies.Length; i++)
        {
            if (managedRigidbodies[i] != null)
                count++;
        }

        return count;
    }

    private void ClearManagedRigidbodyVelocities()
    {
        if (managedRigidbodies == null)
            return;

        for (int i = 0; i < managedRigidbodies.Length; i++)
            ClearRigidbodyVelocity(managedRigidbodies[i]);
    }

    private static void ClearRigidbodyVelocity(Rigidbody rb)
    {
        if (rb == null || rb.isKinematic)
            return;

        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
    }

    private void CacheInitialPose()
    {
        _initialPosition = transform.position;
        _initialRotation = transform.rotation;
        _hasInitialPose = true;
    }

    private void SetAnimatorEnabled(bool enabled, string reason)
    {
        if (animator == null)
            return;

        animator.enabled = enabled;
        _animatorDisabledBySandbox = !enabled;
        Log($"Animator enabled={enabled} reason={reason}");
    }

    private void RestoreAnimatorIfDisabledBySandbox()
    {
        if (animator == null || !_animatorDisabledBySandbox)
            return;

        animator.enabled = true;
        _animatorDisabledBySandbox = false;
        Log("Animator restored by sandbox reset/state restore.");
    }

    private static bool IsFinite(Vector3 value)
    {
        return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
    }

    private static bool IsFinite(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }

    private void Log(string message)
    {
        if (debugLogs)
            Debug.Log($"[RagdollSandbox] {message}", this);
    }

    private void LogWarning(string message)
    {
        if (debugLogs)
            Debug.LogWarning($"[RagdollSandbox] {message}", this);
    }

    private enum SandboxKey
    {
        Normal,
        Stunned,
        Limp,
        Hit,
        Throw,
        Up,
        Reset,
        Animator,
        Kinematic,
        NonKinematic,
        AllKinematic,
        PreviousTarget,
        NextTarget
    }

    private static bool WasPressedThisFrame(SandboxKey key)
    {
#if ENABLE_INPUT_SYSTEM
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null)
            return false;

        switch (key)
        {
            case SandboxKey.Normal:
                return keyboard.digit1Key.wasPressedThisFrame;
            case SandboxKey.Stunned:
                return keyboard.digit2Key.wasPressedThisFrame;
            case SandboxKey.Limp:
                return keyboard.digit3Key.wasPressedThisFrame;
            case SandboxKey.Hit:
                return keyboard.hKey.wasPressedThisFrame;
            case SandboxKey.Throw:
                return keyboard.tKey.wasPressedThisFrame;
            case SandboxKey.Up:
                return keyboard.spaceKey.wasPressedThisFrame;
            case SandboxKey.Reset:
                return keyboard.rKey.wasPressedThisFrame;
            case SandboxKey.Animator:
                return keyboard.aKey.wasPressedThisFrame;
            case SandboxKey.Kinematic:
                return keyboard.kKey.wasPressedThisFrame;
            case SandboxKey.NonKinematic:
                return keyboard.nKey.wasPressedThisFrame;
            case SandboxKey.AllKinematic:
                return keyboard.mKey.wasPressedThisFrame;
            case SandboxKey.PreviousTarget:
                return keyboard.leftBracketKey.wasPressedThisFrame || keyboard.commaKey.wasPressedThisFrame;
            case SandboxKey.NextTarget:
                return keyboard.rightBracketKey.wasPressedThisFrame || keyboard.periodKey.wasPressedThisFrame;
            default:
                return false;
        }
#else
        switch (key)
        {
            case SandboxKey.Normal:
                return Input.GetKeyDown(KeyCode.Alpha1);
            case SandboxKey.Stunned:
                return Input.GetKeyDown(KeyCode.Alpha2);
            case SandboxKey.Limp:
                return Input.GetKeyDown(KeyCode.Alpha3);
            case SandboxKey.Hit:
                return Input.GetKeyDown(KeyCode.H);
            case SandboxKey.Throw:
                return Input.GetKeyDown(KeyCode.T);
            case SandboxKey.Up:
                return Input.GetKeyDown(KeyCode.Space);
            case SandboxKey.Reset:
                return Input.GetKeyDown(KeyCode.R);
            case SandboxKey.Animator:
                return Input.GetKeyDown(KeyCode.A);
            case SandboxKey.Kinematic:
                return Input.GetKeyDown(KeyCode.K);
            case SandboxKey.NonKinematic:
                return Input.GetKeyDown(KeyCode.N);
            case SandboxKey.AllKinematic:
                return Input.GetKeyDown(KeyCode.M);
            case SandboxKey.PreviousTarget:
                return Input.GetKeyDown(KeyCode.LeftBracket) || Input.GetKeyDown(KeyCode.Comma);
            case SandboxKey.NextTarget:
                return Input.GetKeyDown(KeyCode.RightBracket) || Input.GetKeyDown(KeyCode.Period);
            default:
                return false;
        }
#endif
    }
}
