using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerInputModule
{
    private readonly PlayerHub _hub;

    private Vector2 _move;
    private Vector2 _mouseDelta;
    private bool _jumpPressed;
    private bool _primaryAttackPressed;
    private bool _interactPressed;
    private bool _cursorTogglePressed;
    private bool _sprintHeld;

    public Vector2 MoveInput => _move;
    public Vector2 MouseDelta => _mouseDelta;
    public bool IsSprinting => _sprintHeld;

    public PlayerInputModule(PlayerHub hub)
    {
        _hub = hub;
    }

    public void Tick()
    {
        _move = Vector2.zero;
        _mouseDelta = Vector2.zero;

        if (Keyboard.current != null)
        {
            float x = 0f;
            float y = 0f;

            if (Keyboard.current.aKey.isPressed) x -= 1f;
            if (Keyboard.current.dKey.isPressed) x += 1f;
            if (Keyboard.current.sKey.isPressed) y -= 1f;
            if (Keyboard.current.wKey.isPressed) y += 1f;

            _move = new Vector2(x, y);
            if (_move.sqrMagnitude > 1f) _move.Normalize();

            _sprintHeld = Keyboard.current.leftShiftKey.isPressed || Keyboard.current.rightShiftKey.isPressed;

            if (Keyboard.current.spaceKey.wasPressedThisFrame)
                _jumpPressed = true;

            if (Keyboard.current.escapeKey.wasPressedThisFrame)
                _cursorTogglePressed = true;
        }

        if (Mouse.current != null)
        {
            _mouseDelta = Mouse.current.delta.ReadValue();

            if (Mouse.current.leftButton.wasPressedThisFrame)
                _primaryAttackPressed = true;

            if (Mouse.current.rightButton.wasPressedThisFrame)
                _interactPressed = true;
        }
    }

    public bool ConsumeJumpPressed()
    {
        bool v = _jumpPressed;
        _jumpPressed = false;
        return v;
    }

    public bool ConsumePrimaryAttackPressed()
    {
        bool v = _primaryAttackPressed;
        _primaryAttackPressed = false;
        return v;
    }

    public bool ConsumeInteractPressed()
    {
        bool v = _interactPressed;
        _interactPressed = false;
        return v;
    }

    public bool ConsumeCursorTogglePressed()
    {
        bool v = _cursorTogglePressed;
        _cursorTogglePressed = false;
        return v;
    }
}
