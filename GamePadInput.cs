using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

namespace StoreChase;

// Gamepad implementation of IInputSource. Uses the first connected controller
// (default Player One) and returns Neutral when nothing is connected.
//
// Mapping:
//   Left stick / D-pad   -> move (grid, dominant axis only)
//   Right stick          -> camera pan
//   Triggers (RT/LT)     -> camera tilt up/down
//   Shoulders (RB/LB)    -> zoom in/out (continuous)
//   Start                -> restart
//   Back                 -> exit
public class GamePadInput : IInputSource
{
    private const float StickDeadzone = 0.5f;

    private readonly PlayerIndex _playerIndex;

    public GamePadInput(PlayerIndex playerIndex = PlayerIndex.One)
    {
        _playerIndex = playerIndex;
    }

    public InputDeviceKind Kind => InputDeviceKind.GamePad;

    public InputState Poll(GameTime gameTime)
    {
        var gp = GamePad.GetState(_playerIndex);
        if (!gp.IsConnected) return InputState.Neutral;

        float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;

        Vector2 move = ReadGridStep(gp);

        // Right stick Y is up; camera pan Y is +down, so negate.
        Vector2 pan = new(gp.ThumbSticks.Right.X, -gp.ThumbSticks.Right.Y);
        if (pan.LengthSquared() < StickDeadzone * StickDeadzone) pan = Vector2.Zero;

        float tilt = (gp.Triggers.Right > 0.5f ? 1f : 0f) - (gp.Triggers.Left > 0.5f ? 1f : 0f);

        float zoom = (gp.Buttons.RightShoulder == ButtonState.Pressed ? 1f : 0f)
                   - (gp.Buttons.LeftShoulder == ButtonState.Pressed ? 1f : 0f);

        return new InputState
        {
            Move = move,
            CameraPan = pan,
            Tilt = tilt,
            // Shoulders are held: scale by dt and a notches-per-second rate so
            // it adds up to the same "wheel notches" unit the mouse reports.
            Zoom = zoom * dt * 2f,
            Restart = gp.Buttons.Start == ButtonState.Pressed,
            Exit = gp.Buttons.Back == ButtonState.Pressed,
        };
    }

    // Collapses stick/D-pad input to a single grid axis (movement is grid-locked).
    private static Vector2 ReadGridStep(GamePadState gp)
    {
        if (gp.DPad.Up == ButtonState.Pressed) return new Vector2(0f, -1f);
        if (gp.DPad.Down == ButtonState.Pressed) return new Vector2(0f, 1f);
        if (gp.DPad.Left == ButtonState.Pressed) return new Vector2(-1f, 0f);
        if (gp.DPad.Right == ButtonState.Pressed) return new Vector2(1f, 0f);

        Vector2 stick = gp.ThumbSticks.Left; // X right, Y up
        if (stick.LengthSquared() < StickDeadzone * StickDeadzone) return Vector2.Zero;

        if (MathF.Abs(stick.X) >= MathF.Abs(stick.Y))
            return new Vector2(stick.X > 0f ? 1f : -1f, 0f);

        return new Vector2(0f, stick.Y > 0f ? -1f : 1f); // stick up = grid -Y
    }
}