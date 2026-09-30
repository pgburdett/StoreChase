using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Audio;

namespace StoreChase;

/// <summary>Identifiers for every synthesised sound cue in the game.</summary>
public enum SfxId
{
    /// <summary>A critter picks up loot.</summary>
    Steal,

    /// <summary>A critter drops loot (after a tackle).</summary>
    Drop,

    /// <summary>The player picks up loot.</summary>
    PickUp,

    /// <summary>An item is secured behind the counter.</summary>
    Secure,

    /// <summary>The player tackles a critter.</summary>
    Tackle,

    /// <summary>The ball is thrown.</summary>
    Throw,

    /// <summary>The ball bounces off a shelf.</summary>
    Bounce,

    /// <summary>A cat notices the player.</summary>
    Meow,

    /// <summary>The round is won.</summary>
    Win,

    /// <summary>The round is lost.</summary>
    Lose,

    /// <summary>The alarm meter is rising.</summary>
    AlarmBlip,

    /// <summary>A cat is nearby (proximity cue).</summary>
    Heartbeat,
}

/// <summary>
/// Every sound in the game, synthesised into PCM buffers at startup.
///
/// Why not audio files: the project deliberately has no content pipeline for
/// sound and no assets to load, so generating the waveforms keeps that
/// property. Each cue is a short envelope over one or two oscillators (or a
/// filtered noise burst for the footsteps and thuds), which is enough for
/// readable, distinct feedback without a sample library.
///
/// If there's no audio device — a headless machine, a container with no
/// sound — construction fails. That's caught once at startup and the whole
/// system turns into no-ops, so the game still runs.
/// </summary>
public static class Sfx
{
    private const int SampleRate = 44100;

    private static readonly Dictionary<SfxId, SoundEffect> _sounds = new();
    private static bool _available;
    private static bool _initialised;

    /// <summary>True if the audio system is available (i.e. an audio device was found).</summary>
    public static bool Available => _available;

    /// <summary>
    /// Synthesises all sound cues into PCM buffers at startup. Safe to fail:
    /// if there's no audio device, the whole system turns into no-ops.
    /// </summary>
    public static void Initialise()
    {
        if (_initialised) return;
        _initialised = true;

        try
        {
            _sounds[SfxId.Steal] = FromSamples(Sweep(0.10f, 900f, 420f, 0.25f, 0.9f));
            _sounds[SfxId.Drop] = FromSamples(Thud(0.12f, 160f, 0.4f));
            _sounds[SfxId.PickUp] = FromSamples(Chirp(0.09f, 620f, 980f, 0.3f));
            _sounds[SfxId.Secure] = FromSamples(Chirp(0.16f, 520f, 1180f, 0.35f));
            _sounds[SfxId.Tackle] = FromSamples(NoiseBurst(0.16f, 0.35f, 1400f, 0.5f));
            _sounds[SfxId.Throw] = FromSamples(Sweep(0.09f, 300f, 1100f, 0.3f, 1.6f));
            _sounds[SfxId.Bounce] = FromSamples(Thud(0.07f, 260f, 0.3f));
            _sounds[SfxId.Meow] = FromSamples(Meow());
            _sounds[SfxId.Win] = FromSamples(Arpeggio(new[] { 523f, 659f, 784f, 1047f }, 0.10f, 0.35f));
            _sounds[SfxId.Lose] = FromSamples(Arpeggio(new[] { 523f, 415f, 349f, 262f }, 0.13f, 0.4f));
            _sounds[SfxId.AlarmBlip] = FromSamples(Chirp(0.07f, 1400f, 1400f, 0.22f));
            _sounds[SfxId.Heartbeat] = FromSamples(Heartbeat());
            _available = true;
        }
        catch (Exception ex)
        {
            // No audio device, or the buffer was rejected. Not worth crashing
            // over — the game is perfectly playable in silence.
            Console.WriteLine($"[Sfx] disabled (no audio device): {ex.Message}");
            _available = false;
        }
    }

    /// <summary>Plays a sound cue. No-op if the audio system is unavailable.</summary>
    public static void Play(SfxId id, float volume = 1f, float pitch = 0f, float pan = 0f)
    {
        if (!_available) return;
        if (!_sounds.TryGetValue(id, out var sound)) return;

        try
        {
            // Small pitch jitter on cues that fire in quick succession, so
            // repeated playback doesn't sound mechanical.
            float jitter = pitch + ((id == SfxId.AlarmBlip || id == SfxId.Bounce)
                ? (float)(Random.Shared.NextDouble() * 0.12 - 0.06)
                : 0f);
            sound.Play(MathHelper.Clamp(volume, 0f, 1f), MathHelper.Clamp(jitter, -1f, 1f), pan);
        }
        catch
        {
            // A device that disappears mid-run shouldn't take the game with it.
        }
    }

    /// <summary>
    /// Plays a cue quieter the further away the source is, with stereo panning
    /// based on the source's X offset from the listener.
    /// </summary>
    public static void PlayAt(SfxId id, Vector3 listener, Vector3 source, float maxDistance, float volume = 1f)
    {
        float d = Vector3.Distance(listener, source);
        if (d > maxDistance) return;
        float falloff = 1f - (d / maxDistance);
        Play(id, volume * falloff * falloff, pan: MathHelper.Clamp((source.X - listener.X) / 12f, -1f, 1f));
    }

    /// <summary>Disposes all sound buffers and marks the system as unavailable.</summary>
    public static void Dispose()
    {
        foreach (var s in _sounds.Values) s.Dispose();
        _sounds.Clear();
        _available = false;
        _initialised = false;
    }

    // ---- Buffers ---------------------------------------------------------

    // Mono samples packed into the interleaved 16-bit stereo layout a
    // SoundEffect expects: left, right, left, right...
    private static SoundEffect FromSamples(float[] mono)
    {
        var pcm = new byte[mono.Length * 4];
        for (int i = 0; i < mono.Length; i++)
        {
            short s = (short)MathHelper.Clamp(mono[i] * short.MaxValue, short.MinValue, short.MaxValue);
            pcm[i * 4 + 0] = (byte)(s & 0xFF);
            pcm[i * 4 + 1] = (byte)((s >> 8) & 0xFF);
            pcm[i * 4 + 2] = pcm[i * 4 + 0];
            pcm[i * 4 + 3] = pcm[i * 4 + 1];
        }
        return new SoundEffect(pcm, SampleRate, AudioChannels.Stereo);
    }

    private static int Samples(float seconds) => (int)(seconds * SampleRate);

    // A quick attack and an exponential decay, so nothing clicks at the edges.
    private static float Envelope(int i, int total, float attackFraction = 0.08f)
    {
        float t = (float)i / total;
        if (t < attackFraction) return t / attackFraction;
        float decay = (t - attackFraction) / (1f - attackFraction);
        return MathF.Pow(1f - decay, 2.2f);
    }

    /// <summary>A tone gliding from one frequency to another.</summary>
    private static float[] Sweep(float seconds, float fromHz, float toHz, float gain, float curve = 1f)
    {
        int n = Samples(seconds);
        var outp = new float[n];
        double phase = 0;

        for (int i = 0; i < n; i++)
        {
            float t = (float)i / n;
            float hz = fromHz + (toHz - fromHz) * MathF.Pow(t, curve);
            phase += 2 * Math.PI * hz / SampleRate;
            outp[i] = MathF.Sin((float)phase) * Envelope(i, n) * gain;
        }
        return outp;
    }

    /// <summary>Two-tone blip, for pickups and confirmations.</summary>
    private static float[] Chirp(float seconds, float fromHz, float toHz, float gain)
        => Sweep(seconds, fromHz, toHz, gain);

    /// <summary>A short pitched thump: a sine dropping fast, for drops and bounces.</summary>
    private static float[] Thud(float seconds, float startHz, float gain)
    {
        int n = Samples(seconds);
        var outp = new float[n];
        double phase = 0;
        for (int i = 0; i < n; i++)
        {
            float t = (float)i / n;
            float hz = startHz * (1f - 0.75f * t * t);
            phase += 2 * Math.PI * hz / SampleRate;
            outp[i] = MathF.Sin((float)phase) * Envelope(i, n, 0.02f) * gain;
        }
        return outp;
    }

    /// <summary>
    /// Filtered noise, for the tackle. One-pole lowpass on white noise gives a
    /// dull scuff rather than a hiss.
    /// </summary>
    private static float[] NoiseBurst(float seconds, float gain, float lowpassHz, float decay)
    {
        int n = Samples(seconds);
        var outp = new float[n];
        float alpha = MathHelper.Clamp(lowpassHz / SampleRate, 0.01f, 1f);
        float last = 0f;

        for (int i = 0; i < n; i++)
        {
            float white = (float)(Random.Shared.NextDouble() * 2 - 1);
            last += alpha * (white - last);
            float env = MathF.Pow(1f - (float)i / n, decay);
            outp[i] = last * env * gain;
        }
        return outp;
    }

    /// <summary>
    /// A meow: a fundamental with two formants, pitch rising then falling.
    /// Crude, but the pitch contour is what makes it read as a cat.
    /// </summary>
    private static float[] Meow()
    {
        const float seconds = 0.42f;
        int n = Samples(seconds);
        var outp = new float[n];
        double phase = 0;

        for (int i = 0; i < n; i++)
        {
            float t = (float)i / n;
            // Up then down, the classic "mrow".
            float hz = 480f + 220f * MathF.Sin(t * MathF.PI);
            phase += 2 * Math.PI * hz / SampleRate;

            float sample = MathF.Sin((float)phase) * 0.6f;      // fundamental
            sample += MathF.Sin((float)phase * 2.02f) * 0.3f;  // a little grit
            sample += MathF.Sin((float)phase * 3.1f) * 0.12f;

            float env = MathF.Sin(t * MathF.PI);
            outp[i] = sample * env * 0.3f;
        }
        return outp;
    }

    /// <summary>
    /// The lub-dub, as two low thumps. The second is quieter and slightly
    /// delayed, which is what makes it read as a heart rather than a drum.
    ///
    /// This is the game's proximity cue: it plays faster and louder as a cat
    /// gets closer, so "is something right behind me" is answerable without
    /// taking your eyes off the store.
    /// </summary>
    private static float[] Heartbeat()
    {
        const float seconds = 0.52f;
        int n = Samples(seconds);
        var outp = new float[n];

        // (start offset, length, gain, start frequency)
        (float Start, float Len, float Gain, float Hz)[] beats =
        {
            (0.00f, 0.15f, 1.00f, 62f),  // lub
            (0.21f, 0.13f, 0.65f, 54f),  // dub
        };

        double phase = 0;
        foreach (var (start, len, gain, hz) in beats)
        {
            int startSample = Samples(start);
            int beatSamples = Samples(len);
            for (int i = 0; i < beatSamples && startSample + i < n; i++)
            {
                float t = (float)i / beatSamples;
                // Pitch drops as the thump decays, like a struck body.
                phase += 2 * Math.PI * (hz * (1f - 0.45f * t * t)) / SampleRate;
                float env = MathF.Pow(1f - t, 3f);
                outp[startSample + i] += MathF.Sin((float)phase) * env * gain;
            }
        }

        // Keep the sum inside [-1, 1] so the two overlapping thumps don't clip.
        for (int i = 0; i < n; i++) outp[i] = MathHelper.Clamp(outp[i] * 0.85f, -1f, 1f);
        return outp;
    }

    /// <summary>A run of notes, for win and lose stings.</summary>
    private static float[] Arpeggio(float[] notes, float noteSeconds, float gain)
    {
        int perNote = Samples(noteSeconds);
        var outp = new float[perNote * notes.Length];
        for (int k = 0; k < notes.Length; k++)
        {
            double phase = 0;
            for (int i = 0; i < perNote; i++)
            {
                phase += 2 * Math.PI * notes[k] / SampleRate;
                // Slight overlap between notes via the tail of the envelope.
                outp[k * perNote + i] = MathF.Sin((float)phase) * Envelope(i, perNote) * gain;
            }
        }
        return outp;
    }
}
