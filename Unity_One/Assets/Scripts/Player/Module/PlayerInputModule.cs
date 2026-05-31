using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerInputModule : MonoBehaviour
{
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

    private NetworkObject _netObj;

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
}
