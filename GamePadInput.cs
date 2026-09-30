using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

namespace StoreChase;

// Gamepad implementation of IInputSource. Uses the first connected controller
// (default Player One) and returns Neutral when nothing is connected.
//
// Mapping:
//   Left stick / D-pad   -> move (grid, dominant axis only)
//   Right stick          -> camera pan; also first-person look
//   Triggers (RT/LT)     -> camera tilt up/down
//   Shoulders (RB/LB)    -> zoom in/out (continuous)
//   Y                    -> toggle first-person / isometric
//   X                    -> throw the ball
//   Start                -> restart
//   Back                 -> exit
public class GamePadInput : IInputSource
{
    private const float StickDeadzone = 0.5f;
    private const float LookRate = 600f; // look units/sec at full stick

    private readonly PlayerIndex _playerIndex;
    private bool _previousToggle;
    private bool _previousThrow;

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

        // The right stick doubles as first-person look. Pan already has the
        // right signs (X right, Y up), and scaling by dt turns it into the
        // same per-frame unit the mouse reports. Camera reads whichever of
        // pan/look its current mode uses.
        Vector2 look = pan * dt * LookRate;

        // Y flips between isometric and first-person; rising edge only.
        bool toggleNow = gp.Buttons.Y == ButtonState.Pressed;
        bool toggle = toggleNow && !_previousToggle;
        _previousToggle = toggleNow;

        // X throws the ball; rising edge, same as the keyboard's click.
        bool throwNow = gp.Buttons.X == ButtonState.Pressed;
        bool throwPressed = throwNow && !_previousThrow;
        _previousThrow = throwNow;

        return new InputState
        {
            Move = move,
            CameraPan = pan,
            Tilt = tilt,
            // Shoulders are held: scale by dt and a notches-per-second rate so
            // it adds up to the same "wheel notches" unit the mouse reports.
            Zoom = zoom * dt * 2f,
            Look = look,
            ToggleCamera = toggle,
            Restart = gp.Buttons.Start == ButtonState.Pressed,
            Exit = gp.Buttons.Back == ButtonState.Pressed,
            Throw = throwPressed,
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