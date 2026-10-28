using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

namespace StoreChase;

// Keyboard + mouse implementation of IInputSource. This is the only place the
// game reads the keyboard/mouse directly, and it holds the mouse-wheel
// previous-value state needed to turn the cumulative scroll value into a
// per-frame delta.
public class KeyboardMouseInput : IInputSource, IMouseLookSource
{
    private int _previousScrollValue;
    private bool _previousToggle;
    private bool _previousThrow;
    private bool _previousUse;
    private readonly bool[] _previousBuy;
    private bool _previousStart;
    private bool _previousPause;
    private KeyboardState _previousKb;
    private bool _mouseCaptured;

    /// <summary>Where the keys come from. Defaults to the shipping scheme
    /// (see <see cref="InputMapper.Default"/>); a rebind screen or a test
    /// can supply its own.</summary>
    private readonly InputMapper _map;

    /// <summary>How many shop slots the mapper exposes (slots 1..N).</summary>
    private int ShopSlotCount => _map.BuySlots.Length;

    /// <summary>Creates a keyboard+mouse source with the default bindings.</summary>
    public KeyboardMouseInput() : this(InputMapper.Default)
    {
    }

    /// <summary>Creates a keyboard+mouse source with the given bindings.</summary>
    public KeyboardMouseInput(InputMapper map)
    {
        _map = map;
        _previousBuy = new bool[map.BuySlots.Length];
    }

    /// <summary>Always <see cref="InputDeviceKind.KeyboardMouse"/>.</summary>
    public InputDeviceKind Kind => InputDeviceKind.KeyboardMouse;

    /// <summary>Whether the pointer should be captured for relative look input.
    /// Set by Game1 based on the camera mode.</summary>
    public bool MouseLookEnabled { get; set; }

    /// <summary>The screen center to re-center the pointer to while captured.</summary>
    public Point MouseLookCenter { get; set; }

    /// <summary>Polls the keyboard and mouse and returns this frame's input state.</summary>
    public InputState Poll(GameTime gameTime)
    {
        var kb = Keyboard.GetState();
        var ms = Mouse.GetState();

        // Grid movement: one axis at a time, first match wins (matches the
        // original held-direction behavior).
        Vector2 move = Vector2.Zero;
        if (kb.IsKeyDown(_map.MoveUp) || kb.IsKeyDown(_map.MoveUpAlt)) move.Y -= 1f;
        else if (kb.IsKeyDown(_map.MoveDown) || kb.IsKeyDown(_map.MoveDownAlt)) move.Y += 1f;
        else if (kb.IsKeyDown(_map.MoveLeft) || kb.IsKeyDown(_map.MoveLeftAlt)) move.X -= 1f;
        else if (kb.IsKeyDown(_map.MoveRight) || kb.IsKeyDown(_map.MoveRightAlt)) move.X += 1f;

        // Pan the camera; deliberately not WASD/arrows (player).
        Vector2 pan = Vector2.Zero;
        if (kb.IsKeyDown(_map.PanUp)) pan.Y -= 1f;
        if (kb.IsKeyDown(_map.PanDown)) pan.Y += 1f;
        if (kb.IsKeyDown(_map.PanLeft)) pan.X -= 1f;
        if (kb.IsKeyDown(_map.PanRight)) pan.X += 1f;

        // Tilt between straight-down (0) and angled perspective.
        float tilt = 0f;
        if (kb.IsKeyDown(_map.TiltUp)) tilt += 1f;
        if (kb.IsKeyDown(_map.TiltDown)) tilt -= 1f;

        // Mouse wheel is cumulative; diff against last frame for the delta.
        // One notch is typically 120 units.
        int scrollValue = ms.ScrollWheelValue;
        float zoom = (scrollValue - _previousScrollValue) / 120f;
        _previousScrollValue = scrollValue;

        // First-person look: while captured, report how far the pointer moved
        // from the screen center since last frame, then re-center it so the
        // delta stays relative and the cursor never reaches an edge. The
        // capture-start frame only re-centers (no delta), so entering FPS
        // doesn't snap the view.
        Vector2 look = Vector2.Zero;
        if (MouseLookEnabled)
        {
            if (_mouseCaptured)
                look = new Vector2(ms.X - MouseLookCenter.X, ms.Y - MouseLookCenter.Y);

            if (ms.X != MouseLookCenter.X || ms.Y != MouseLookCenter.Y)
                Mouse.SetPosition(MouseLookCenter.X, MouseLookCenter.Y);

            _mouseCaptured = true;
        }
        else
        {
            _mouseCaptured = false;
        }

        // V flips between isometric and first-person; report the rising edge
        // only, so holding the key doesn't toggle every frame.
        bool toggleNow = kb.IsKeyDown(_map.ToggleCamera);
        bool toggle = toggleNow && !_previousToggle;
        _previousToggle = toggleNow;

        // Throw: left mouse button, or the throw key for keyboard-only
        // players. Rising edge, like the camera toggle, so holding it
        // doesn't spam balls. (In first person the pointer is captured,
        // which doesn't affect the button state — only the position — so
        // the click still registers.)
        bool throwNow = ms.LeftButton == ButtonState.Pressed || kb.IsKeyDown(_map.Throw);
        bool throwPressed = throwNow && !_previousThrow;
        _previousThrow = throwNow;

        // Use: sits under the left hand next to WASD. Rising edge, same
        // reason as the others.
        bool useNow = kb.IsKeyDown(_map.Use);
        bool usePressed = useNow && !_previousUse;
        _previousUse = useNow;

        // Shop slots: rising edge per key. Only the shop reads these
        // (between rounds), so a held digit buys once.
        int buySlot = 0;
        for (int i = 0; i < ShopSlotCount; i++)
        {
            bool down = kb.IsKeyDown(_map.BuySlots[i]);
            if (down && !_previousBuy[i]) buySlot = i + 1;
            _previousBuy[i] = down;
        }

        bool startDown = kb.IsKeyDown(_map.Start) || kb.IsKeyDown(_map.StartAlt);
        bool startPressed = startDown && !_previousStart;
        _previousStart = startDown;

        bool pauseNow = kb.IsKeyDown(_map.Pause);
        bool pausePressed = pauseNow && !_previousPause;
        _previousPause = pauseNow;

        // Raw key capture for UIs (rebinding). Only keys that just went
        // down this frame count, so a held key isn't reported every frame.
        // Ordered: GetPressedKeys() does not promise a stable order, and a
        // frame with two new keys must report the same one every time or the
        // rebind screen binds whatever the runtime happened to hand back.
        Keys lastKey = Keys.None;
        foreach (var k in kb.GetPressedKeys().OrderBy(k => (int)k))
        {
            if (!_previousKb.IsKeyDown(k)) { lastKey = k; break; }
        }
        _previousKb = kb;

        return new InputState
        {
            Move = move,
            CameraPan = pan,
            Tilt = tilt,
            Zoom = zoom,
            Look = look,
            ToggleCamera = toggle,
            Restart = kb.IsKeyDown(_map.Restart),
            Exit = kb.IsKeyDown(_map.Exit),
            Throw = throwPressed,
            Use = usePressed,
            Crouch = kb.IsKeyDown(_map.Crouch),
            BuySlot = buySlot,
            Start = startPressed,
            Pause = pausePressed,
            LastKeyPressed = lastKey,
        };
    }
}