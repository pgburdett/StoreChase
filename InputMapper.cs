using Microsoft.Xna.Framework.Input;

namespace StoreChase;

/// <summary>
/// Central mapping of keys and gamepad buttons to the actions they trigger.
///
/// One place to read (or rebind) the whole control scheme, instead of
/// hunting through <see cref="KeyboardMouseInput"/> and
/// <see cref="GamePadInput"/> for where a key is tested. Both sources take a
/// mapper in their constructor and fall back to <see cref="Default"/>, so
/// rebinding is a matter of handing them a different instance — and a test
/// can hand them one too.
///
/// Mouse-only input (wheel zoom, pointer look, left-click throw) has no
/// mapping here: it is not a rebindable binding but the pointer itself, and
/// it stays in the source that owns the device.
///
/// Fields are mutable on purpose: a rebind screen writes them.
/// </summary>
public sealed class InputMapper
{
    /// <summary>The shipping control scheme.</summary>
    public static InputMapper Default { get; } = new();

    // --- Keyboard ---------------------------------------------------------

    /// <summary>Move up (grid -Y). Checked before <see cref="MoveDown"/>.</summary>
    public Keys MoveUp = Keys.W;
    public Keys MoveDown = Keys.S;
    public Keys MoveLeft = Keys.A;
    public Keys MoveRight = Keys.D;

    /// <summary>Arrow keys also steer, for keyboard-only players.</summary>
    public Keys MoveUpAlt = Keys.Up;
    public Keys MoveDownAlt = Keys.Down;
    public Keys MoveLeftAlt = Keys.Left;
    public Keys MoveRightAlt = Keys.Right;

    /// <summary>Pan the isometric camera (deliberately not WASD — the player has those).</summary>
    public Keys PanUp = Keys.I;
    public Keys PanDown = Keys.K;
    public Keys PanLeft = Keys.J;
    public Keys PanRight = Keys.L;

    /// <summary>Tilt the isometric camera toward the angled view.</summary>
    public Keys TiltUp = Keys.O;
    public Keys TiltDown = Keys.U;

    /// <summary>Toggle isometric / first-person.</summary>
    public Keys ToggleCamera = Keys.V;

    /// <summary>Throw the ball (keyboard half; the mouse's left button is the other).</summary>
    public Keys Throw = Keys.F;

    /// <summary>Use the pocketed power-up / pull a fire alarm.</summary>
    public Keys Use = Keys.E;

    /// <summary>Crouch: half speed, harder to spot.</summary>
    public Keys Crouch = Keys.LeftControl;

    /// <summary>Start the game / confirm. Space doubles as start on the title.</summary>
    public Keys Start = Keys.Enter;
    public Keys StartAlt = Keys.Space;

    /// <summary>Restart the round (held, not an edge — matches InputState.Restart).</summary>
    public Keys Restart = Keys.R;

    /// <summary>Exit the game (held).</summary>
    public Keys Exit = Keys.Escape;

    /// <summary>Shop buy slots, 1-based index order (D1..D9 → slots 1..9).</summary>
    public Keys[] BuySlots =
    {
        Keys.D1, Keys.D2, Keys.D3, Keys.D4, Keys.D5,
        Keys.D6, Keys.D7, Keys.D8, Keys.D9,
    };

    // --- Gamepad ----------------------------------------------------------

    /// <summary>Face-button actions. Y = camera toggle, X = throw, B = use;
    /// Start doubles as start and restart; Back exits.</summary>
    public Buttons ToggleCameraPad = Buttons.Y;
    public Buttons ThrowPad = Buttons.X;
    public Buttons UsePad = Buttons.B;
    public Buttons StartPad = Buttons.Start;
    public Buttons ExitPad = Buttons.Back;
}
