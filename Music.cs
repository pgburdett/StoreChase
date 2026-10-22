using System;
using System.Threading;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Audio;

namespace StoreChase;

/// <summary>
/// The adaptive soundtrack's signal generator: a small step-sequencer that
/// renders whatever the alarm stage currently deserves.
///
/// <para>Split from <see cref="Music"/> on purpose — the part that makes
/// *sound* needs an audio device and a background thread; the part that makes
/// *music* is a pure function of a mood and a clock, and the harness runs it
/// from a terminal to check that it stays bounded, that the critical stage is
/// actually louder than the calm one, and that two fresh synths agree.</para>
///
/// <para>The arrangement is three layers over an Am vamp, gated by the stage:
/// a bass pulse (always), a soft pad (Suspicious+), and drums plus a bright
/// arp (Critical+). Layer gains are smoothed toward their targets rather than
/// switched, so the track thickens as the alarm fills instead of cutting to a
/// new song mid-frame. Tempo belongs to the *rhythm* only (kept in beats, so
/// a tempo change continues from the same bar position); every oscillator
/// runs on its own real-time phase, so a mood change can never click a sine
/// into a new position.</para>
/// </summary>
public sealed class MusicSynth
{
    public const int SampleRate = 44100;

    // The vamp: A minor. Root and its chord, in Hz. A2 for the bass (A1
    // disappears on laptop speakers), the triad an octave up for pad and arp.
    private const float BassHz = 110f;
    private static readonly float[] ChordHz = { 220f, 261.63f, 330f };  // A3, C4, E4
    private const float ArpTopHz = 660f;                                // E5

    /// <summary>What the track is currently playing to.</summary>
    public AlarmStage Mood { get; set; } = AlarmStage.Calm;

    // Smoothed layer gains, 0..1 each. Taken once per Fill (the game thread
    // writes Mood) and eased toward the new mix over roughly half a second.
    private float _padGain;
    private float _percGain;

    // Song position in *beats* (rhythm gating only), and real time for the
    // oscillators. Kept apart so tempo changes move the drums, not the pitch.
    private double _beat;
    private double _seconds;

    // Deterministic noise for the hats (an LCG, not Random.Shared: two fresh
    // synths must produce identical bytes or the harness can't compare them).
    private uint _noise = 0x2545F491;

    /// <summary>
    /// Renders <paramref name="count"/> mono samples into
    /// <paramref name="buffer"/>, clamped to ±1.
    /// </summary>
    public void Fill(float[] buffer, int count)
    {
        // Take the mood once per buffer: it is written by the game thread.
        AlarmStage mood = Mood;
        float beatSeconds = mood switch
        {
            AlarmStage.Suspicious => 60f / 96f,
            AlarmStage.Critical => 60f / 124f,
            _ => 60f / 84f,
        };

        float padTarget = mood >= AlarmStage.Suspicious ? 1f : 0f;
        float percTarget = mood >= AlarmStage.Critical ? 1f : 0f;

        // Ease toward the new layer mix, scaled by how long this buffer
        // actually is (~0.4s to fully arrive).
        float ease = 1f - MathF.Exp(-((float)count / SampleRate) * 2.5f);
        float dt = 1f / SampleRate;

        for (int i = 0; i < count; i++)
        {
            _padGain += (padTarget - _padGain) * ease;
            _percGain += (percTarget - _percGain) * ease;

            float t = (float)_seconds;
            float phaseInBeat = (float)((_beat % 1.0 + 1.0) % 1.0);   // 0..1 through the beat
            float timeInBeat = phaseInBeat * beatSeconds;
            bool onBeat = timeInBeat < 0.16f;
            float sample = 0f;

            // Layer 1 — the bass pulse. A short gated sine on every beat:
            // the heartbeat of the whole track, and the only thing playing
            // in the calm store.
            if (onBeat)
            {
                float env = 1f - timeInBeat / 0.16f;
                sample += 0.22f * env * MathF.Sin(2f * MathF.PI * BassHz * t);
            }

            // Layer 2 — the pad. Sustained triad with a slow swell, so the
            // suspicious store has a drone under it that the calm one lacks.
            if (_padGain > 0.001f)
            {
                float swell = 0.7f + 0.3f * MathF.Sin(2f * MathF.PI * 0.4f * t);
                float pad = 0f;
                for (int c = 0; c < ChordHz.Length; c++)
                    pad += MathF.Sin(2f * MathF.PI * ChordHz[c] * t);
                sample += 0.075f * _padGain * swell * pad / ChordHz.Length;
            }

            // Layer 3 — drums and arp, critical only. Kick on the beat,
            // hat between, and a bright square on the eighths.
            if (_percGain > 0.001f)
            {
                if (onBeat)
                {
                    float kickPhase = timeInBeat / 0.16f;
                    float kickHz = 120f + (50f - 120f) * kickPhase;
                    sample += 0.5f * _percGain *
                              MathF.Sin(2f * MathF.PI * kickHz * timeInBeat) * (1f - kickPhase);
                }

                bool offBeat = phaseInBeat >= 0.5f && (phaseInBeat - 0.5f) * beatSeconds < 0.05f;
                if (offBeat)
                {
                    float t2 = (phaseInBeat - 0.5f) * beatSeconds / 0.05f;
                    sample += 0.10f * _percGain * NextNoise() * (1f - t2);
                }

                // First half of each half-beat: an eighth-note pulse.
                if (phaseInBeat % 0.5f < 0.25f)
                {
                    float hz = ((int)(_beat * 2.0) % 4) switch
                    {
                        0 => ChordHz[0],
                        1 => ChordHz[1],
                        2 => ChordHz[2],
                        _ => ArpTopHz,
                    };
                    // A square reads as "urgent" over the sine pad and is
                    // one MathF.Sign instead of another oscillator.
                    sample += 0.05f * _percGain *
                              MathF.Sign(MathF.Sin(2f * MathF.PI * hz * t));
                }
            }

            buffer[i] = Math.Clamp(sample, -1f, 1f);
            _seconds += dt;
            _beat += dt / beatSeconds;
        }
    }

    /// <summary>Rewinds to a fresh, silent start — test and restart hook.</summary>
    public void Reset()
    {
        _beat = 0;
        _seconds = 0;
        _padGain = 0f;
        _percGain = 0f;
        Mood = AlarmStage.Calm;
        _noise = 0x2545F491;
    }

    private float NextNoise()
    {
        _noise = _noise * 1664525u + 1013904223u;
        return (_noise >> 8) / (float)0x800000 - 1f;  // -1..1
    }
}

/// <summary>
/// Owns the audio-device side of the soundtrack: a
/// <see cref="DynamicSoundEffectInstance"/> kept fed from a background thread
/// that renders <see cref="MusicSynth"/>.
///
/// <para>The pattern mirrors <see cref="RainbowTitle"/>'s phase thread: all
/// the *music* happens off the game thread, and everything touching the audio
/// device is guarded, because a machine with no audio device must still run
/// the game (Sfx already turns itself off in that case — the music follows
/// suit through <see cref="Sfx.Available"/>).</para>
/// </summary>
public static class Music
{
    private const int ChunkSamples = 4410; // 0.1s per submitted buffer
    private const int KeepBuffers = 5;     // fill ahead this many before waiting

    private static readonly MusicSynth Synth = new();
    private static DynamicSoundEffectInstance? _instance;
    private static Thread? _thread;
    private static volatile bool _running;

    /// <summary>True when the device side came up (false = silent, by design).</summary>
    public static bool Available => _instance != null;

    /// <summary>
    /// Starts the soundtrack. Safe to call on a machine with no audio: it
    /// silently stays unavailable, exactly like Sfx.
    /// </summary>
    public static void Start()
    {
        if (_instance != null || !Sfx.Available) return;

        try
        {
            _instance = new DynamicSoundEffectInstance(MusicSynth.SampleRate, AudioChannels.Mono);

            // Prime a few buffers before Play so the first second isn't a
            // silence-then-start pop.
            for (int i = 0; i < KeepBuffers; i++)
                _instance.SubmitBuffer(RenderChunk());

            _instance.Play();
        }
        catch
        {
            // No device, or a device that refuses: the game plays in silence
            // rather than not at all.
            try { _instance?.Dispose(); } catch { /* nothing to salvage */ }
            _instance = null;
            return;
        }

        _running = true;
        _thread = new Thread(FillLoop) { IsBackground = true, Name = "storechase-music" };
        _thread.Start();
    }

    /// <summary>Tells the track which stage of alarm to play to.</summary>
    public static void SetMood(AlarmStage stage) => Synth.Mood = stage;

    /// <summary>Stops the track and releases the device (UnloadContent).</summary>
    public static void Stop()
    {
        _running = false;
        if (_thread != null)
        {
            _thread.Join(250);
            _thread = null;
        }

        try { _instance?.Stop(); _instance?.Dispose(); } catch { /* device already gone */ }
        _instance = null;
    }

    private static void FillLoop()
    {
        while (_running)
        {
            var instance = _instance;
            if (instance == null) break;

            try
            {
                if (instance.PendingBufferCount < KeepBuffers)
                {
                    instance.SubmitBuffer(RenderChunk());
                }
                else
                {
                    Thread.Sleep(10);
                }
            }
            catch
            {
                // Device died mid-run: stop feeding, leave the game running.
                break;
            }
        }
    }

    private static byte[] RenderChunk()
    {
        var floats = new float[ChunkSamples];
        Synth.Fill(floats, ChunkSamples);

        var bytes = new byte[ChunkSamples * 2];
        for (int i = 0; i < ChunkSamples; i++)
        {
            short pcm = (short)Math.Clamp(MathF.Round(floats[i] * short.MaxValue), short.MinValue, short.MaxValue);
            bytes[i * 2] = (byte)(pcm & 0xFF);
            bytes[i * 2 + 1] = (byte)((pcm >> 8) & 0xFF);
        }
        return bytes;
    }
}
