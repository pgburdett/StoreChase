using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

namespace StoreChase;

// Keyboard + mouse implementation of IInputSource. This is the only place the
// game reads the keyboard/mouse directly, and it holds the mouse-wheel
// previous-value state needed to turn the cumulative scroll value into a
// per-frame delta.
public class KeyboardMouseInput : IInputSource
{
    private int _previousScrollValue;

    public InputDeviceKind Kind => InputDeviceKind.KeyboardMouse;

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

        return new InputState
        {
            Move = move,
            CameraPan = pan,
            Tilt = tilt,
            Zoom = zoom,
            Restart = kb.IsKeyDown(Keys.R),
            Exit = kb.IsKeyDown(Keys.Escape),
        };
    }
}