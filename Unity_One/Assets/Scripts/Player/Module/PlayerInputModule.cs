using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerInputModule : MonoBehaviour
{
    [Header("Move")]
    [Tooltip("걷기 입력에 적용되는 민감도")]
    [SerializeField] private float moveScale = 1f;

    [Header("Look")]
    [Tooltip("마우스 X(좌우) 감도")]
    [SerializeField] private float lookSensitivityX = 0.12f;
    [Tooltip("마우스 Y(상하) 감도")] // [추가]
    [SerializeField] private float lookSensitivityY = 0.12f;

    [Tooltip("ESC로 커서 락 토글을 허용")]
    [SerializeField] private bool allowCursorToggle = true;

    private bool _cursorLocked = true;
    public bool IsCursorLocked => _cursorLocked;

    private void Awake()
    {
        SetCursorLock(true);
    }

    public void ReadInputs(
            out Vector2 move,
            out float yawDelta,
            out float pitchDelta,
            out bool jumpPressed,
            out bool sprintHeld,
            out bool attackPressed,
            out bool interactPressed,
            out bool dropPressed) // [추가]
    {
        if (allowCursorToggle && Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
            SetCursorLock(!_cursorLocked);

        if (!_cursorLocked)
        {
            move = Vector2.zero;
            yawDelta = 0f;
            pitchDelta = 0f;
            jumpPressed = false;
            sprintHeld = false;
            attackPressed = false;
            interactPressed = false;
            dropPressed = false; // [추가]
            return;
        }

        move = ReadMove() * moveScale;
        yawDelta = ReadYawDelta();
        pitchDelta = ReadPitchDelta();

        var kb = Keyboard.current;
        jumpPressed = kb != null && kb.spaceKey.wasPressedThisFrame;
        sprintHeld = kb != null && kb.leftShiftKey.isPressed;
        dropPressed = kb != null && kb.gKey.wasPressedThisFrame; // [추가] G키로 버리기

        var ms = Mouse.current;
        attackPressed = ms != null && ms.leftButton.wasPressedThisFrame;
        interactPressed = ms != null && ms.rightButton.wasPressedThisFrame;
    }

    private Vector2 ReadMove()
    {
        float x = 0f; float y = 0f;
        var kb = Keyboard.current;
        if (kb == null) return Vector2.zero;

        if (kb.aKey.isPressed) x -= 1f;
        if (kb.dKey.isPressed) x += 1f;
        if (kb.wKey.isPressed) y += 1f;
        if (kb.sKey.isPressed) y -= 1f;

        Vector2 v = new Vector2(x, y);
        return v.sqrMagnitude > 1f ? v.normalized : v;
    }

    private float ReadYawDelta()
    {
        if (Mouse.current == null) return 0f;
        return Mouse.current.delta.x.ReadValue() * lookSensitivityX;
    }

    // [추가] Y축(상하) 입력 읽기
    private float ReadPitchDelta()
    {
        if (Mouse.current == null) return 0f;
        // 마우스 위로 올리면 -, 아래로 내리면 + (혹은 반대) 취향에 맞게 부호 변경 가능
        return Mouse.current.delta.y.ReadValue() * lookSensitivityY;
    }

    private void SetCursorLock(bool locked)
    {
        _cursorLocked = locked;
        Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !locked;
    }
}