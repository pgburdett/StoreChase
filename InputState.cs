using Microsoft.Xna.Framework;

namespace StoreChase;

// One frame's worth of device-agnostic input. Gameplay code (Player, Camera,
// Game1) reads this instead of Keyboard/GamePad/Mouse, so devices can be
// added or swapped without touching gameplay.
//
// Axis conventions mirror the grid: Move.X = +1 right, Move.Y = +1 down
// (screen/grid space), so "up" is -1 on Y. Tilt is +1 = tilt up. Zoom is a
// per-frame delta in "wheel notches", additive across sources. Look is the
// per-frame first-person look delta (mouse pixels, or a stick rate scaled to
// the same unit), right/up positive.
public readonly struct InputState
{
    /// <summary>Grid movement: X = +1 right, Y = +1 down (screen/grid space).</summary>
    public Vector2 Move { get; init; }

    /// <summary>Camera panning offset, in screen space.</summary>
    public Vector2 CameraPan { get; init; }

    /// <summary>Camera tilt input: +1 = tilt up, -1 = tilt down.</summary>
    public float Tilt { get; init; }

    /// <summary>Mouse wheel notches this frame (or gamepad equivalent).</summary>
    public float Zoom { get; init; }

    /// <summary>First-person look delta (mouse pixels or gamepad stick scaled to the same unit), right/up positive.</summary>
    public Vector2 Look { get; init; }

    /// <summary>Toggle between isometric and first-person camera. Rising edge.</summary>
    public bool ToggleCamera { get; init; }

    /// <summary>Restart the round. Held (not edge-triggered).</summary>
    public bool Restart { get; init; }

    /// <summary>Exit the game. Held.</summary>
    public bool Exit { get; init; }

    /// <summary>Throw the ball. A rising edge, so holding the button doesn't
    /// fire one every frame.</summary>
    public bool Throw { get; init; }

    /// <summary>Use the pocketed power-up (or pull a fire alarm — whatever the
    /// world offers on the Use key first). Rising edge.</summary>
    public bool Use { get; init; }

    /// <summary>Shop slot the player just pressed this frame, 1-based
    /// (0 = none). Only meaningful between rounds, when the shop is open.
    /// Rising edge, so one press buys one item.</summary>
    public int BuySlot { get; init; }

    /// <summary>A neutral (all-zero) input state, used when no device is connected.</summary>
    public static InputState Neutral => default;

    // True when this device did anything this frame. Used by InputSystem to
    // auto-detect which device the player is currently using.
    public bool HasActivity =>
        Move != Vector2.Zero ||
        CameraPan != Vector2.Zero ||
        Tilt != 0f ||
        Zoom != 0f ||
        Look != Vector2.Zero ||
        ToggleCamera ||
        Restart ||
        Exit ||
        Throw ||
        Use ||
        BuySlot != 0;
}