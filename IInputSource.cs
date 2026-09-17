using Microsoft.Xna.Framework;

namespace StoreChase;

// Which kind of device a source represents. Lets gameplay/UI know what the
// player is using right now (e.g. to show the matching control hints).
public enum InputDeviceKind
{
    KeyboardMouse,
    GamePad,
}

// A single physical device (keyboard+mouse, gamepad, ...). Each source is
// polled once per frame and reports only what it contributes; InputSystem
// picks the active source and exposes its state.
public interface IInputSource
{
    InputDeviceKind Kind { get; }

    InputState Poll(GameTime gameTime);
}

// Optional capability for sources that own a pointer. While enabled the
// source turns the pointer into a relative, centered look delta (so the
// cursor can't wander off-screen) instead of an absolute position. Devices
// without a pointer simply don't implement this; InputSystem forwards the
// request to any source that does.
public interface IMouseLookSource
{
    bool MouseLookEnabled { get; set; }
    Point MouseLookCenter { get; set; }
}