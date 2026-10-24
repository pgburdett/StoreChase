using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using StoreChase;

namespace StoreChase.Harness;

/// <summary>
/// Checks on the input layer: InputState semantics, InputSystem's device
/// auto-detection, and the InputMapper defaults.
///
/// The sources themselves poll real devices, which a headless run cannot
/// read — so they are exercised through the seam they expose: IInputSource.
/// InputSystem is the part with behaviour worth testing (who is active, when
/// it hands off), and it only ever sees that interface.
/// </summary>
public static class InputChecks
{
    /// <summary>Runs every input check.</summary>
    public static void Run()
    {
        Check.Section("Input: state, device hand-off, mappings");

        // --- InputState ---------------------------------------------------

        Check.That("InputState.Neutral has no activity",
            !InputState.Neutral.HasActivity);

        Check.That("crouching alone does not count as device activity",
            // Crouch is a held modifier; activity drives device hand-off, and
            // holding Ctrl while using the pad would otherwise pin the keyboard
            // as the active device.
            !new InputState { Crouch = true }.HasActivity,
            "HasActivity must ignore Crouch");

        Check.That("movement counts as device activity",
            new InputState { Move = new Vector2(0f, -1f) }.HasActivity);

        Check.That("a buy press counts as device activity",
            new InputState { BuySlot = 3 }.HasActivity);

        // --- InputSystem device hand-off ----------------------------------

        // Two fake sources: whichever "reports input" should be active.
        var kb = new FakeSource(InputDeviceKind.KeyboardMouse);
        var pad = new FakeSource(InputDeviceKind.GamePad);
        var sys = new InputSystem(kb, pad);

        // No input from anyone: the default source stays active.
        sys.Update(Frame());
        Check.That("with no input anywhere the first source stays active",
            sys.ActiveDevice == InputDeviceKind.KeyboardMouse);

        // The pad moves: it takes over.
        pad.Next = new InputState { Move = new Vector2(1f, 0f) };
        sys.Update(Frame());
        Check.That("input from a second device hands control to it",
            sys.ActiveDevice == InputDeviceKind.GamePad);

        // The pad goes idle: it keeps control (the current device has
        // priority) rather than flapping back to the keyboard.
        pad.Next = InputState.Neutral;
        sys.Update(Frame());
        Check.That("an idle device keeps control until another reports input",
            sys.ActiveDevice == InputDeviceKind.GamePad);

        // The keyboard clicks: it takes over.
        kb.Next = new InputState { Throw = true };
        sys.Update(Frame());
        Check.That("the next device to report input takes over",
            sys.ActiveDevice == InputDeviceKind.KeyboardMouse);

        // Every source is polled each frame even while inactive, so internal
        // delta state (the mouse wheel) stays current — verified by counting.
        kb.Polls = 0;
        pad.Polls = 0;
        kb.Next = InputState.Neutral;
        pad.Next = InputState.Neutral;
        sys.Update(Frame());
        sys.Update(Frame());
        Check.That("every source is polled every frame",
            kb.Polls == 2 && pad.Polls == 2,
            $"kb {kb.Polls}, pad {pad.Polls}");

        Check.That("the exposed state comes from the active source",
            sys.State.Throw == false, "neutral active source exposes neutral state");

        // --- InputMapper defaults -----------------------------------------

        var map = InputMapper.Default;

        Check.That("movement keys are WASD by default",
            map.MoveUp == Keys.W && map.MoveDown == Keys.S &&
            map.MoveLeft == Keys.A && map.MoveRight == Keys.D);

        Check.That("arrow keys also steer by default",
            map.MoveUpAlt == Keys.Up && map.MoveDownAlt == Keys.Down &&
            map.MoveLeftAlt == Keys.Left && map.MoveRightAlt == Keys.Right);

        Check.That("the camera toggle is V, not a movement key",
            map.ToggleCamera == Keys.V &&
            map.ToggleCamera != map.MoveUp && map.ToggleCamera != map.MoveDown);

        Check.That("throw and use are distinct keys",
            map.Throw != map.Use && map.Throw == Keys.F && map.Use == Keys.E);

        Check.That("crouch is the left control, distinct from every other action key",
            map.Crouch == Keys.LeftControl &&
            map.Crouch != map.Use && map.Crouch != map.Throw && map.Crouch != map.Restart);

        Check.That("buy slots are D1..D9 in order",
            map.BuySlots.Length == 9 && map.BuySlots[0] == Keys.D1 && map.BuySlots[8] == Keys.D9,
            $"{map.BuySlots.Length} slots");

        Check.That("no two action keys collide",
            CountDistinct(map) == 17,
            $"{CountDistinct(map)} distinct of 17 action keys (move, pan, tilt, toggle, throw, use, crouch, start, restart, exit)");

        // Rebinding is the point of the mapper: a custom instance must not
        // disturb the default scheme.
        var rebinding = new InputMapper { Use = Keys.G };
        Check.That("a rebinding does not mutate the default scheme",
            InputMapper.Default.Use == Keys.E && rebinding.Use == Keys.G);
    }

    /// <summary>How many of the mapper's primary action keys are distinct
    /// (collisions mean one press fires two actions).</summary>
    private static int CountDistinct(InputMapper map) => new HashSet<Keys>
    {
        map.MoveUp, map.MoveDown, map.MoveLeft, map.MoveRight,
        map.PanUp, map.PanDown, map.PanLeft, map.PanRight,
        map.TiltUp, map.TiltDown,
        map.ToggleCamera, map.Throw, map.Use, map.Crouch,
        map.Start, map.Restart, map.Exit,
    }.Count;

    private static GameTime Frame() =>
        new(TimeSpan.Zero, TimeSpan.FromMilliseconds(16));

    /// <summary>A source whose output the test controls.</summary>
    private sealed class FakeSource : IInputSource
    {
        public InputDeviceKind Kind { get; }
        public InputState Next = InputState.Neutral;
        public int Polls;

        public FakeSource(InputDeviceKind kind) => Kind = kind;

        public InputState Poll(GameTime gameTime)
        {
            Polls++;
            return Next;
        }
    }
}
