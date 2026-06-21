using UnityEngine;

public sealed class HamsterMotorShellFaceExpressionAdapter : MonoBehaviour
{
    private const string TargetRootName = "Hamster_JointFreeMotorShell_MainScenes";

    [Header("References")]
    [SerializeField] private FaceExpressionController faceController;
    [SerializeField] private HamsterMotorShellCombatAdapter combatAdapter;
    [SerializeField] private HamsterMotorShellItemAdapter itemAdapter;
    [SerializeField] private HamsterMotorShellRagdollRecoveryAdapter recoveryAdapter;
    [SerializeField] private HamsterFullRagdollMotor motor;

    [Header("Expression Indices")]
    [Tooltip("0-5 face atlas index. Verify the actual expression in Play Mode.")]
    [SerializeField] private int normalExpressionIndex = 0;
    [Tooltip("Legacy fallback only. New attack logic uses unarmed/held-item fields below.")]
    [SerializeField] private int attackExpressionIndex = 1;
    [Tooltip("0-5 face atlas index. Used when impact or sweep starts.")]
    [SerializeField] private int hitExpressionIndex = 4;
    [Tooltip("0-5 face atlas index. Used while knocked down.")]
    [SerializeField] private int knockedDownExpressionIndex = 4;
    [Tooltip("0-5 face atlas index. Used during get-up/recovery.")]
    [SerializeField] private int recoveringExpressionIndex = 4;

    [Header("Unarmed Attack Expression")]
    [Tooltip("0-5 face atlas index for attacks with no held item. Verify in Play Mode.")]
    [SerializeField] private int unarmedAttackExpressionIndex = 1;
    [SerializeField] private bool overrideUnarmedAttackExpression = true;
    [SerializeField] private bool reapplyUnarmedAttackFaceWhileHeld = true;
    [SerializeField] private float unarmedAttackExpressionHoldTime = 0.45f;
    [SerializeField] private float unarmedAttackFaceReapplyInterval = 0.06f;

    [Header("Held Item Attack Expression")]
    [Tooltip("0-5 face atlas index for held-item attacks when override is enabled.")]
    [SerializeField] private int heldItemAttackExpressionIndex = 1;
    [SerializeField] private bool overrideHeldItemAttackExpression = false;
    [SerializeField] private bool reapplyHeldItemAttackFaceWhileHeld = false;
    [SerializeField] private float heldItemAttackExpressionHoldTime = 0.45f;
    [SerializeField] private float heldItemAttackFaceReapplyInterval = 0.06f;

    [Header("Timing")]
    [SerializeField] private float hitExpressionHoldTime = 0.5f;
    [SerializeField] private bool resetToNormalAfterHold = true;

    [Header("Debug")]
    [SerializeField] private bool debugFaceLogs = false;

    private HamsterMotorShellCombatAdapter _subscribedCombatAdapter;
    private HamsterMotorShellRagdollRecoveryAdapter.RecoveryState _lastRecoveryState;
    private bool _hasRecoveryState;
    private bool _attackExpressionOverrideActive;
    private int _activeAttackExpressionIndex;
    private float _activeAttackFaceHoldTimer;
    private bool _activeAttackShouldReapply;
    private float _activeAttackReapplyInterval;
    private float _nextAttackFaceReapplyTime;
    private float _normalReturnTime = -1f;
    private int _currentExpressionIndex = int.MinValue;

    private void OnEnable()
    {
        if (!IsTargetRoot())
            return;

        CacheReferences();
        SubscribeCombatAdapter();
        _hasRecoveryState = false;
        ApplyNormal("OnEnable");
    }

    private void Start()
    {
        if (!IsTargetRoot())
            return;

        CacheReferences();
        SubscribeCombatAdapter();
        TickRecoveryState(true);
    }

    private void OnDisable()
    {
        UnsubscribeCombatAdapter();
    }

    private void Update()
    {
        if (!IsTargetRoot())
            return;

        CacheReferences();
        SubscribeCombatAdapter();
        TickRecoveryState(false);
    }

    private void LateUpdate()
    {
        if (!IsTargetRoot())
            return;

        TickAttackFaceHold(Time.deltaTime);
        TickTimedNormalReturn();
    }

    private void CacheReferences()
    {
        Transform root = transform.root != null ? transform.root : transform;

        if (faceController == null)
            faceController = root.GetComponentInChildren<FaceExpressionController>(true);

        if (combatAdapter == null)
            combatAdapter = root.GetComponentInChildren<HamsterMotorShellCombatAdapter>(true);

        if (itemAdapter == null)
            itemAdapter = root.GetComponentInChildren<HamsterMotorShellItemAdapter>(true);

        if (recoveryAdapter == null)
            recoveryAdapter = root.GetComponentInChildren<HamsterMotorShellRagdollRecoveryAdapter>(true);

        if (motor == null)
            motor = root.GetComponentInChildren<HamsterFullRagdollMotor>(true);
    }

    private void SubscribeCombatAdapter()
    {
        if (_subscribedCombatAdapter == combatAdapter)
            return;

        UnsubscribeCombatAdapter();

        if (combatAdapter == null)
            return;

        combatAdapter.AttackStarted += HandleAttackStarted;
        _subscribedCombatAdapter = combatAdapter;
    }

    private void UnsubscribeCombatAdapter()
    {
        if (_subscribedCombatAdapter == null)
            return;

        _subscribedCombatAdapter.AttackStarted -= HandleAttackStarted;
        _subscribedCombatAdapter = null;
    }

    private void HandleAttackStarted()
    {
        if (!IsTargetRoot() || IsRecoveryExpressionActive())
            return;

        bool hasHeldItem = HasHeldItemForAttack();
        bool shouldOverride = hasHeldItem ? overrideHeldItemAttackExpression : overrideUnarmedAttackExpression;
        if (!shouldOverride)
        {
            ClearAttackFaceHold();
            return;
        }

        int selectedIndex = hasHeldItem ? heldItemAttackExpressionIndex : unarmedAttackExpressionIndex;
        float selectedHoldTime = hasHeldItem ? heldItemAttackExpressionHoldTime : unarmedAttackExpressionHoldTime;
        bool selectedReapply = hasHeldItem ? reapplyHeldItemAttackFaceWhileHeld : reapplyUnarmedAttackFaceWhileHeld;
        float selectedReapplyInterval = hasHeldItem ? heldItemAttackFaceReapplyInterval : unarmedAttackFaceReapplyInterval;

        _normalReturnTime = -1f;
        _attackExpressionOverrideActive = true;
        _activeAttackExpressionIndex = Mathf.Max(0, selectedIndex);
        _activeAttackFaceHoldTimer = Mathf.Max(0f, selectedHoldTime);
        _activeAttackShouldReapply = selectedReapply;
        _activeAttackReapplyInterval = Mathf.Max(0.05f, selectedReapplyInterval);
        _nextAttackFaceReapplyTime = Time.time + _activeAttackReapplyInterval;

        ApplyExpression(_activeAttackExpressionIndex, hasHeldItem ? "HeldAttackStart" : "UnarmedAttackStart", true);
    }

    private void TickRecoveryState(bool force)
    {
        if (recoveryAdapter == null)
            return;

        HamsterMotorShellRagdollRecoveryAdapter.RecoveryState state = recoveryAdapter.CurrentRecoveryState;
        if (!force && _hasRecoveryState && state == _lastRecoveryState)
            return;

        _hasRecoveryState = true;
        _lastRecoveryState = state;
        if (state != HamsterMotorShellRagdollRecoveryAdapter.RecoveryState.Normal)
            ClearAttackFaceHold();

        switch (state)
        {
            case HamsterMotorShellRagdollRecoveryAdapter.RecoveryState.Normal:
                _normalReturnTime = -1f;
                ApplyNormal("RecoveryNormal");
                break;
            case HamsterMotorShellRagdollRecoveryAdapter.RecoveryState.Impacted:
            case HamsterMotorShellRagdollRecoveryAdapter.RecoveryState.LiquidSwept:
                ApplyExpression(hitExpressionIndex, state.ToString(), false);
                ScheduleNormalReturn(hitExpressionHoldTime);
                break;
            case HamsterMotorShellRagdollRecoveryAdapter.RecoveryState.KnockedDown:
                _normalReturnTime = -1f;
                ApplyExpression(knockedDownExpressionIndex, state.ToString(), false);
                break;
            case HamsterMotorShellRagdollRecoveryAdapter.RecoveryState.PrepareGetUp:
            case HamsterMotorShellRagdollRecoveryAdapter.RecoveryState.GettingUp:
            case HamsterMotorShellRagdollRecoveryAdapter.RecoveryState.RecoveryBlendOut:
            case HamsterMotorShellRagdollRecoveryAdapter.RecoveryState.Recovering:
                _normalReturnTime = -1f;
                ApplyExpression(recoveringExpressionIndex, state.ToString(), false);
                break;
        }
    }

    private void TickAttackFaceHold(float deltaTime)
    {
        if (!_attackExpressionOverrideActive)
            return;

        if (IsRecoveryExpressionActive())
        {
            ClearAttackFaceHold();
            return;
        }

        _activeAttackFaceHoldTimer = Mathf.Max(0f, _activeAttackFaceHoldTimer - Mathf.Max(0f, deltaTime));

        if (_activeAttackShouldReapply && Time.time >= _nextAttackFaceReapplyTime)
        {
            ApplyExpression(_activeAttackExpressionIndex, "AttackHold", true);
            _nextAttackFaceReapplyTime = Time.time + _activeAttackReapplyInterval;
        }

        if (_activeAttackFaceHoldTimer > 0f || !resetToNormalAfterHold || IsRecoveryExpressionActive())
            return;

        ClearAttackFaceHold();
        ApplyNormal("AttackHoldEnd");
    }

    private void ClearAttackFaceHold()
    {
        _attackExpressionOverrideActive = false;
        _activeAttackExpressionIndex = 0;
        _activeAttackFaceHoldTimer = 0f;
        _activeAttackShouldReapply = false;
        _activeAttackReapplyInterval = 0f;
        _nextAttackFaceReapplyTime = 0f;
    }

    private bool HasHeldItemForAttack()
    {
        return itemAdapter != null && itemAdapter.HasHeldItem;
    }

    private void TickTimedNormalReturn()
    {
        if (!resetToNormalAfterHold || _normalReturnTime < 0f)
            return;

        if (Time.time < _normalReturnTime || IsRecoveryExpressionActive())
            return;

        _normalReturnTime = -1f;
        ApplyNormal("TimedReturn");
    }

    private void ScheduleNormalReturn(float holdTime)
    {
        if (!resetToNormalAfterHold)
            return;

        _normalReturnTime = Time.time + Mathf.Max(0f, holdTime);
    }

    private bool IsRecoveryExpressionActive()
    {
        return recoveryAdapter != null &&
               recoveryAdapter.CurrentRecoveryState != HamsterMotorShellRagdollRecoveryAdapter.RecoveryState.Normal;
    }

    private void ApplyNormal(string reason)
    {
        ApplyExpression(normalExpressionIndex, reason, false);
    }

    private void ApplyExpression(int expressionIndex, string reason, bool force)
    {
        if (faceController == null)
            return;

        int safeIndex = Mathf.Max(0, expressionIndex);
        if (!force && _currentExpressionIndex == safeIndex)
            return;

        _currentExpressionIndex = safeIndex;
        faceController.SetFaceIndex(safeIndex);
        Log($"face={safeIndex} reason={reason}");
    }

    private bool IsTargetRoot()
    {
        Transform root = transform.root != null ? transform.root : transform;
        return root.name == TargetRootName || root.name.StartsWith(TargetRootName + "(", System.StringComparison.Ordinal);
    }

    private void Log(string message)
    {
        if (!debugFaceLogs)
            return;

        Debug.Log($"[MSFace:{name}] {message}", this);
    }

    [ContextMenu("Face Test/Set Face 0")]
    private void ContextSetFace0() => ApplyContextFace(0);

    [ContextMenu("Face Test/Set Face 1")]
    private void ContextSetFace1() => ApplyContextFace(1);

    [ContextMenu("Face Test/Set Face 2")]
    private void ContextSetFace2() => ApplyContextFace(2);

    [ContextMenu("Face Test/Set Face 3")]
    private void ContextSetFace3() => ApplyContextFace(3);

    [ContextMenu("Face Test/Set Face 4")]
    private void ContextSetFace4() => ApplyContextFace(4);

    [ContextMenu("Face Test/Set Face 5")]
    private void ContextSetFace5() => ApplyContextFace(5);

    [ContextMenu("Face Test/Reset Normal")]
    private void ContextResetNormal()
    {
        if (!IsTargetRoot())
            return;

        CacheReferences();
        ClearAttackFaceHold();
        ApplyNormal("ContextResetNormal");
    }

    private void ApplyContextFace(int index)
    {
        if (!IsTargetRoot())
            return;

        CacheReferences();
        ApplyExpression(index, "ContextMenu", true);
    }
}
