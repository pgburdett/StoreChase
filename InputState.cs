using Microsoft.Xna.Framework;

namespace StoreChase;

// One frame's worth of device-agnostic input. Gameplay code (Player, Camera,
// Game1) reads this instead of Keyboard/GamePad/Mouse, so devices can be
// added or swapped without touching gameplay.
//
// Axis conventions mirror the grid: Move.X = +1 right, Move.Y = +1 down
// (screen/grid space), so "up" is -1 on Y. Tilt is +1 = tilt up. Zoom is a
// per-frame delta in "wheel notches", additive across sources.
public readonly struct InputState
{
    public Vector2 Move { get; init; }
    public Vector2 CameraPan { get; init; }
    public float Tilt { get; init; }
    public float Zoom { get; init; }
    public bool Restart { get; init; }
    public bool Exit { get; init; }

    public static InputState Neutral => default;

    // True when this device did anything this frame. Used by InputSystem to
    // auto-detect which device the player is currently using.
    public bool HasActivity =>
        Move != Vector2.Zero ||
        CameraPan != Vector2.Zero ||
        Tilt != 0f ||
        Zoom != 0f ||
        Restart ||
        Exit;
}