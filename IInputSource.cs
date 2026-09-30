using Microsoft.Xna.Framework;

namespace StoreChase;

// Which kind of device a source represents. Lets gameplay/UI know what the
// player is using right now (e.g. to show the matching control hints).
/// <summary>Which kind of device a source represents. Lets gameplay/UI know what the
/// player is using right now (e.g. to show the matching control hints).</summary>
public enum InputDeviceKind
{
    /// <summary>Keyboard and mouse.</summary>
    KeyboardMouse,

    /// <summary>Gamepad / controller.</summary>
    GamePad,
}

// A single physical device (keyboard+mouse, gamepad, ...). Each source is
// polled once per frame and reports only what it contributes; InputSystem
// picks the active source and exposes its state.
public interface IInputSource
{
    /// <summary>Which kind of device this source represents.</summary>
    InputDeviceKind Kind { get; }

    /// <summary>Polls the device and returns this frame's input state.</summary>
    InputState Poll(GameTime gameTime);
}

// Optional capability for sources that own a pointer. While enabled the
// source turns the pointer into a relative, centered look delta (so the
// cursor can't wander off-screen) instead of an absolute position. Devices
// without a pointer simply don't implement this; InputSystem forwards the
// request to any source that does.
public interface IMouseLookSource
{
    /// <summary>Whether the pointer should be captured for relative look input.</summary>
    bool MouseLookEnabled { get; set; }

    /// <summary>The screen center to re-center the pointer to while captured.</summary>
    Point MouseLookCenter { get; set; }
}