using UnityEngine;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

public sealed class PlayerInputModule
{
    private readonly PlayerHub _hub;

    public Vector2 MoveInput { get; private set; }
    public bool IsSprinting { get; private set; }

    private bool _jumpPressed;
    private bool _attackPressed;
    private bool _interactPressed;
    private bool _escapePressed;

    public PlayerInputModule(PlayerHub hub)
    {
        _hub = hub;
    }

    public void Tick()
    {
#if ENABLE_INPUT_SYSTEM
        var kb = Keyboard.current;
        var ms = Mouse.current;

        MoveInput = ReadMove(kb);
        IsSprinting = kb != null && (kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed);

        if (kb != null && kb.spaceKey.wasPressedThisFrame) _jumpPressed = true;
        if (ms != null && ms.leftButton.wasPressedThisFrame) _attackPressed = true;
        if (ms != null && ms.rightButton.wasPressedThisFrame) _interactPressed = true;

        if (kb != null && kb.escapeKey.wasPressedThisFrame) _escapePressed = true;
#else
        MoveInput = Vector2.zero;
        IsSprinting = false;
#endif
    }

#if ENABLE_INPUT_SYSTEM
    private Vector2 ReadMove(Keyboard kb)
    {
        if (kb == null) return Vector2.zero;

        float x = 0f;
        if (kb.aKey.isPressed) x -= 1f;
        if (kb.dKey.isPressed) x += 1f;

        float y = 0f;
        if (kb.sKey.isPressed) y -= 1f;
        if (kb.wKey.isPressed) y += 1f;

        return Vector2.ClampMagnitude(new Vector2(x, y), 1f);
    }
#endif

    public bool ConsumeJumpPressed()
    {
        bool v = _jumpPressed;
        _jumpPressed = false;
        return v;
    }

    public bool ConsumePrimaryAttackPressed()
    {
        bool v = _attackPressed;
        _attackPressed = false;
        return v;
    }

    public bool ConsumeInteractPressed()
    {
        bool v = _interactPressed;
        _interactPressed = false;
        return v;
    }

    public bool ConsumeEscapePressed()
    {
        bool v = _escapePressed;
        _escapePressed = false;
        return v;
    }
}
