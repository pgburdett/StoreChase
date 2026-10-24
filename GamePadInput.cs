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
//   B                    -> use the pocketed power-up
//   Start                -> start the game / restart
//   Back                 -> exit
public class GamePadInput : IInputSource
{
    private const float StickDeadzone = 0.5f;
    private const float LookRate = 600f; // look units/sec at full stick

    private readonly PlayerIndex _playerIndex;
    private readonly InputMapper _map;
    private bool _previousToggle;
    private bool _previousThrow;
    private bool _previousUse;
    private bool _previousStart;

    /// <summary>Creates a gamepad input source for the given controller slot
    /// with the default bindings.</summary>
    public GamePadInput(PlayerIndex playerIndex = PlayerIndex.One)
        : this(InputMapper.Default, playerIndex)
    {
    }

    /// <summary>Creates a gamepad input source with the given bindings.</summary>
    public GamePadInput(InputMapper map, PlayerIndex playerIndex = PlayerIndex.One)
    {
        _map = map;
        _playerIndex = playerIndex;
    }

    /// <summary>Always <see cref="InputDeviceKind.GamePad"/>.</summary>
    public InputDeviceKind Kind => InputDeviceKind.GamePad;

    /// <summary>Polls the gamepad and returns this frame's input state.
    /// Returns <see cref="InputState.Neutral"/> when no controller is connected.</summary>
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
        bool toggleNow = gp.IsButtonDown(_map.ToggleCameraPad);
        bool toggle = toggleNow && !_previousToggle;
        _previousToggle = toggleNow;

        // X throws the ball; rising edge, same as the keyboard's click.
        bool throwNow = gp.IsButtonDown(_map.ThrowPad);
        bool throwPressed = throwNow && !_previousThrow;
        _previousThrow = throwNow;

        // B uses the pocketed power-up. Rising edge like X and Y.
        bool useNow = gp.IsButtonDown(_map.UsePad);
        bool usePressed = useNow && !_previousUse;
        _previousUse = useNow;

        // Start is the pad's confirm *and* its restart: rising edge, so one
        // press is one action, and both fields are set because the two phases
        // read different ones (Title reads Start/Restart, the between-rounds
        // shop reads Restart). Without this the pad could not leave the title.
        bool startNow = gp.IsButtonDown(_map.StartPad);
        bool startPressed = startNow && !_previousStart;
        _previousStart = startNow;

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
            Start = startPressed,
            Restart = startPressed,
            Exit = gp.IsButtonDown(_map.ExitPad),
            Throw = throwPressed,
            Use = usePressed,
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