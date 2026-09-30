using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;

namespace StoreChase;

// Owns the active input sources and produces one InputState per frame.
// Game1 updates this once at the top of Update() and passes the resulting
// state to Player/Camera, so nothing else touches the raw input APIs.
//
// Device auto-detection: the player never picks a device. The source that
// most recently produced input (a key press, stick/D-pad move, button, or
// wheel) becomes active and drives the game. The current device keeps control
// while it stays active; when it goes idle, the first other device with input
// takes over. Every source is still polled each frame so its internal delta
// state (e.g. the cumulative mouse wheel) stays current while inactive.
public class InputSystem
{
    private readonly List<IInputSource> _sources;
    private readonly List<InputState> _polled = new();
    private int _activeIndex;

    /// <summary>The current frame's input from the active device.</summary>
    public InputState State { get; private set; }

    /// <summary>The currently active input source (the one whose state is exposed).</summary>
    public IInputSource ActiveSource => _sources[_activeIndex];

    /// <summary>Which kind of device is currently active.</summary>
    public InputDeviceKind ActiveDevice => ActiveSource.Kind;

    /// <summary>Requested pointer capture for first-person look. Game1 sets these from
    /// the camera mode; Update forwards them to any source that supports it.</summary>
    public bool MouseLookEnabled { get; set; }

    /// <summary>The screen center to re-center the pointer to while captured.</summary>
    public Point MouseLookCenter { get; set; }

    /// <summary>Creates a new InputSystem with the given sources.</summary>
    /// <exception cref="ArgumentException">If no sources are provided.</exception>
    public InputSystem(params IInputSource[] sources)
    {
        if (sources.Length == 0)
            throw new ArgumentException("At least one input source is required.", nameof(sources));

        _sources = new List<IInputSource>(sources);
    }

    /// <summary>Adds an additional input source and returns this (for chaining).</summary>
    public InputSystem Add(IInputSource source)
    {
        _sources.Add(source);
        return this;
    }

    /// <summary>
    /// Polls all registered sources, forwards the mouse-look request to any
    /// source that supports it, and selects the active device.
    ///
    /// The current device keeps control while it has input; when it goes idle,
    /// the first other device with input takes over. Every source is still
    /// polled each frame so its internal delta state (e.g. the cumulative mouse
    /// wheel) stays current while inactive.
    /// </summary>
    public void Update(GameTime gameTime)
    {
        foreach (var source in _sources)
            if (source is IMouseLookSource mouse)
            {
                mouse.MouseLookEnabled = MouseLookEnabled;
                mouse.MouseLookCenter = MouseLookCenter;
            }

        _polled.Clear();
        foreach (var source in _sources)
            _polled.Add(source.Poll(gameTime));

        // Keep the current device while it has input; otherwise hand off to
        // the first other device that does.
        if (!_polled[_activeIndex].HasActivity)
        {
            for (int i = 0; i < _polled.Count; i++)
            {
                if (i != _activeIndex && _polled[i].HasActivity)
                {
                    _activeIndex = i;
                    break;
                }
            }
        }

        State = _polled[_activeIndex];
    }
}