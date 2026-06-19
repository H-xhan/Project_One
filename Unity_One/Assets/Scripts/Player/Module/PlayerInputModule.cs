using Unity.Netcode;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

public class PlayerInputModule : MonoBehaviour
{
    private const string InputRouteTargetName = "Hamster_JointFreeMotorShell_MainScenes";
    private const float InputRouteLogInterval = 0.5f;

    [Header("Mouse")]
    [Tooltip("마우스 X 감도")]
    [SerializeField] private float mouseSensitivityX = 2.0f;

    [Tooltip("마우스 Y 감도")]
    [SerializeField] private float mouseSensitivityY = 2.0f;

    [Tooltip("이 값보다 작은 마우스 델타는 0으로 처리")]
    [SerializeField] private float mouseDeadzone = 0.5f;

    [Tooltip("프레임당 허용할 최대 마우스 델타")]
    [SerializeField] private float maxMouseDeltaPerFrame = 80f;

    [Header("Keys")]
    [Tooltip("달리기 키")]
    [SerializeField] private Key sprintKey = Key.LeftShift;

    [Tooltip("점프 키")]
    [SerializeField] private Key jumpKey = Key.Space;

    [Tooltip("공격 키")]
    [SerializeField] private int attackMouseButton = 0;

    [Tooltip("상호작용/픽업 키")]
    [SerializeField] private int interactMouseButton = 1;

    [Tooltip("드랍 키")]
    [SerializeField] private Key dropKey = Key.G;

    [Header("Diagnostics")]
    [SerializeField] private bool debugMovementRoutingLogs = false;

    private NetworkObject _netObj;
    private float _nextInputRouteLogTime;

    public bool IsCursorLocked => Cursor.lockState == CursorLockMode.Locked;

    private void Awake()
    {
        _netObj = GetComponent<NetworkObject>();
    }

    private bool IsLocalOwner()
    {
        if (_netObj == null)
            return true;

        var nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsListening)
            return true;

        return _netObj.IsOwner;
    }

    public void ReadInputs(
        out Vector2 move,
        out float yawDelta,
        out float pitchDelta,
        out bool jumpPressed,
        out bool sprintHeld,
        out bool attackPressed,
        out bool interactPressed,
        out bool dropPressed
    )
    {
        if (!IsLocalOwner())
        {
            move = Vector2.zero;
            yawDelta = 0f;
            pitchDelta = 0f;
            jumpPressed = false;
            sprintHeld = false;
            attackPressed = false;
            interactPressed = false;
            dropPressed = false;
            LogInputRoute(move, yawDelta, pitchDelta, jumpPressed, sprintHeld, attackPressed, interactPressed, dropPressed, null, "not-local-owner");
            return;
        }

        var kb = Keyboard.current;
        var mouse = Mouse.current;

        float x = 0f;
        float y = 0f;

        if (kb != null)
        {
            if (kb.aKey.isPressed) x -= 1f;
            if (kb.dKey.isPressed) x += 1f;
            if (kb.sKey.isPressed) y -= 1f;
            if (kb.wKey.isPressed) y += 1f;
        }

        move = new Vector2(x, y);
        if (move.sqrMagnitude > 1f) move.Normalize();

        yawDelta = 0f;
        pitchDelta = 0f;

        bool canReadMouseLook =
            mouse != null;

        if (canReadMouseLook)
        {
            Vector2 delta = mouse.delta.ReadValue();

            // 작은 흔들림 제거
            if (Mathf.Abs(delta.x) < mouseDeadzone) delta.x = 0f;
            if (Mathf.Abs(delta.y) < mouseDeadzone) delta.y = 0f;

            // 비정상적으로 큰 튐 방지
            delta.x = Mathf.Clamp(delta.x, -maxMouseDeltaPerFrame, maxMouseDeltaPerFrame);
            delta.y = Mathf.Clamp(delta.y, -maxMouseDeltaPerFrame, maxMouseDeltaPerFrame);

            yawDelta = delta.x * mouseSensitivityX;
            pitchDelta = delta.y * mouseSensitivityY;
        }

        jumpPressed = (kb != null) && kb[jumpKey].wasPressedThisFrame;
        sprintHeld = (kb != null) && kb[sprintKey].isPressed;

        attackPressed = (mouse != null) && ReadMouseButton(mouse, attackMouseButton);
        interactPressed = (mouse != null) && ReadMouseButton(mouse, interactMouseButton);

        dropPressed = (kb != null) && kb[dropKey].wasPressedThisFrame;
        LogInputRoute(move, yawDelta, pitchDelta, jumpPressed, sprintHeld, attackPressed, interactPressed, dropPressed, kb, "read");
    }

    private bool ReadMouseButton(Mouse mouse, int button)
    {
        switch (button)
        {
            case 0: return mouse.leftButton.wasPressedThisFrame;
            case 1: return mouse.rightButton.wasPressedThisFrame;
            case 2: return mouse.middleButton.wasPressedThisFrame;
            default: return false;
        }
    }

    private void LogInputRoute(
        Vector2 move,
        float yawDelta,
        float pitchDelta,
        bool jumpPressed,
        bool sprintHeld,
        bool attackPressed,
        bool interactPressed,
        bool dropPressed,
        Keyboard keyboard,
        string phase)
    {
        if (!ShouldLogInputRoute())
            return;

        bool wPressed = keyboard != null && keyboard.wKey.isPressed;
        bool aPressed = keyboard != null && keyboard.aKey.isPressed;
        bool sPressed = keyboard != null && keyboard.sKey.isPressed;
        bool dPressed = keyboard != null && keyboard.dKey.isPressed;
        string actionMap = GetCurrentActionMapName();
        string selectedUi = GetSelectedUiName();
        var networkManager = NetworkManager.Singleton;

        Debug.Log(
            $"[InputRoute/Input:{GetInputRouteObjectName()}] phase={phase} enabled={enabled} active={gameObject.activeInHierarchy} scene={SceneManager.GetActiveScene().name} networkObjectExists={_netObj != null} isOwner={(_netObj != null ? _netObj.IsOwner.ToString() : "<none>")} networkListening={(networkManager != null && networkManager.IsListening)} move={FormatVector2(move)} wasd=W:{wPressed} A:{aPressed} S:{sPressed} D:{dPressed} sprintHeld={sprintHeld} jumpPressed={jumpPressed} attackPressed={attackPressed} interactPressed={interactPressed} dropPressed={dropPressed} yawDelta={yawDelta:F2} pitchDelta={pitchDelta:F2} actionMap={actionMap} selectedUI={selectedUi} cursorLock={Cursor.lockState}",
            this);
    }

    private bool ShouldLogInputRoute()
    {
        if (!debugMovementRoutingLogs)
            return false;

        if (!IsInputRouteTarget())
            return false;

        if (Time.unscaledTime < _nextInputRouteLogTime)
            return false;

        _nextInputRouteLogTime = Time.unscaledTime + InputRouteLogInterval;
        return true;
    }

    private bool IsInputRouteTarget()
    {
        Transform root = transform.root;
        return root != null && root.name.Contains(InputRouteTargetName);
    }

    private string GetInputRouteObjectName()
    {
        Transform root = transform.root;
        return root != null ? root.name : gameObject.name;
    }

    private string GetCurrentActionMapName()
    {
        PlayerInput playerInput = GetComponentInParent<PlayerInput>();
        return playerInput != null && playerInput.currentActionMap != null
            ? playerInput.currentActionMap.name
            : "<none>";
    }

    private static string GetSelectedUiName()
    {
        GameObject selected = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
        return selected != null ? selected.name : "<none>";
    }

    private static string FormatVector2(Vector2 value)
    {
        return $"({value.x:F2},{value.y:F2})";
    }
}
