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
    private bool _mouseCaptured;

    public InputDeviceKind Kind => InputDeviceKind.KeyboardMouse;

    public bool MouseLookEnabled { get; set; }
    public Point MouseLookCenter { get; set; }

    public InputState Poll(GameTime gameTime)
    {
        var kb = Keyboard.GetState();
        var ms = Mouse.GetState();

        // Grid movement: one axis at a time, first match wins (matches the
        // original held-direction behavior).
        Vector2 move = Vector2.Zero;
        if (kb.IsKeyDown(Keys.Up) || kb.IsKeyDown(Keys.W)) move.Y -= 1f;
        else if (kb.IsKeyDown(Keys.Down) || kb.IsKeyDown(Keys.S)) move.Y += 1f;
        else if (kb.IsKeyDown(Keys.Left) || kb.IsKeyDown(Keys.A)) move.X -= 1f;
        else if (kb.IsKeyDown(Keys.Right) || kb.IsKeyDown(Keys.D)) move.X += 1f;

        // I/J/K/L pan the camera; deliberately not WASD/arrows (player).
        Vector2 pan = Vector2.Zero;
        if (kb.IsKeyDown(Keys.I)) pan.Y -= 1f;
        if (kb.IsKeyDown(Keys.K)) pan.Y += 1f;
        if (kb.IsKeyDown(Keys.J)) pan.X -= 1f;
        if (kb.IsKeyDown(Keys.L)) pan.X += 1f;

        // U/O tilt between straight-down (0) and angled perspective.
        float tilt = 0f;
        if (kb.IsKeyDown(Keys.O)) tilt += 1f;
        if (kb.IsKeyDown(Keys.U)) tilt -= 1f;

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
        bool toggleNow = kb.IsKeyDown(Keys.V);
        bool toggle = toggleNow && !_previousToggle;
        _previousToggle = toggleNow;

        return new InputState
        {
            Move = move,
            CameraPan = pan,
            Tilt = tilt,
            Zoom = zoom,
            Look = look,
            ToggleCamera = toggle,
            Restart = kb.IsKeyDown(Keys.R),
            Exit = kb.IsKeyDown(Keys.Escape),
        };
    }
}