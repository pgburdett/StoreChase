using System;
using System.Diagnostics;
using System.Threading;
using Microsoft.Xna.Framework;

namespace StoreChase;

// Drives the animated rainbow for the title. The hue rotation runs on its own
// background thread (as requested); the game thread only *reads* the current
// phase and does the drawing, since the GraphicsDevice/SpriteBatch are not
// thread-safe. Dispose() stops the worker.
//
// It exposes two different things, and they are not interchangeable:
//   Phase — a wrapped 0..1 colour position. It is a sawtooth: it returns to
//           zero once a cycle. Fine for "which colour" or "where in the blink",
//           utterly wrong as a clock, because anything driven by it snaps back
//           when it wraps.
//   Time  — seconds since start, monotonically increasing. This is what any
//           *motion* should be driven by (the starfield, drift, scrolling).
// Using Phase for motion is the bug that made the stars crawl a pixel and then
// jump: at 0.25 cycles/sec the position reset every four seconds.
public sealed class RainbowTitle : IDisposable
{
    /// <summary>The title text.</summary>
    public const string Text = "Store Chase";

    private const float SpeedHz = 0.25f; // full rainbow cycles per second
    private const int FrameMs = 16;

    private readonly Thread _worker;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private volatile bool _running = true;
    private float _phase;   // 0..1; written by the worker, read by the game thread
    private float _time;    // seconds since start; monotonic, never wraps
    private double _lastSeconds;

    /// <summary>Starts the background thread that advances the hue phase.</summary>
    public RainbowTitle()
    {
        _worker = new Thread(Run)
        {
            IsBackground = true,
            Name = "RainbowTitle",
        };
        _worker.Start();
    }

    /// <summary>Current hue offset (0..1). A wrapped sawtooth — see the class note.</summary>
    public float Phase => Volatile.Read(ref _phase);

    /// <summary>Seconds since the title started, monotonically increasing. Drive
    /// motion from this, never from <see cref="Phase"/>.</summary>
    public float Time => Volatile.Read(ref _time);

    private void Run()
    {
        while (_running)
        {
            double now = _clock.Elapsed.TotalSeconds;
            float dt = (float)(now - _lastSeconds);
            _lastSeconds = now;

            float phase = Volatile.Read(ref _phase) + dt * SpeedHz;
            phase -= MathF.Floor(phase);
            Volatile.Write(ref _phase, phase);
            Volatile.Write(ref _time, Volatile.Read(ref _time) + dt);

            Thread.Sleep(FrameMs);
        }
    }

    /// <summary>Maps a hue (0..1) to a fully saturated, fully bright color (vivid rainbow).</summary>
    public static Color FromHue(float hue)
    {
        hue -= MathF.Floor(hue);
        float h = hue * 6f;
        float x = 1f - MathF.Abs(h % 2f - 1f);

        float r, g, b;
        switch ((int)h)
        {
            case 0: r = 1f; g = x; b = 0f; break;
            case 1: r = x; g = 1f; b = 0f; break;
            case 2: r = 0f; g = 1f; b = x; break;
            case 3: r = 0f; g = x; b = 1f; break;
            case 4: r = x; g = 0f; b = 1f; break;
            default: r = 1f; g = 0f; b = x; break;
        }

        return new Color(r, g, b);
    }

    /// <summary>Stops the background thread and waits for it to finish.</summary>
    public void Dispose()
    {
        _running = false;
        if (_worker.IsAlive) _worker.Join(250);
    }
}