using System;
using System.Linq;
using StoreChase;

namespace StoreChase.Harness;

/// <summary>
/// Checks on the adaptive soundtrack's generator (the device side stays out
/// of the harness — no audio card in a terminal).
///
/// What matters: the signal is finite and bounded (a clipped or NaN buffer
/// would be a scream, not music), the alarm stages genuinely get *louder*
/// (the whole design — the track thickens as the meter fills), a mood change
/// mid-stream doesn't corrupt anything, and the generator is deterministic so
/// two fresh synths agree byte for byte.
/// </summary>
public static class MusicChecks
{
    /// <summary>Runs every music check.</summary>
    public static void Run()
    {
        Check.Section("Music: the alarm drives the arrangement");

        const int sec = MusicSynth.SampleRate;

        // Bounded, finite, and actually producing sound.
        var calm = RenderSteady(AlarmStage.Calm, sec);
        Check.That("the calm track is bounded to ±1",
            calm.All(s => s >= -1f && s <= 1f), "out of range");
        Check.That("the calm track is finite", calm.All(float.IsFinite), "NaN/Inf");
        Check.That("the calm track is audible", Rms(calm) > 0.002f, $"rms {Rms(calm):F4}");

        var suspicious = RenderSteady(AlarmStage.Suspicious, sec);
        var critical = RenderSteady(AlarmStage.Critical, sec);

        // The entire point of the system: more alarm, more music.
        Check.That("the suspicious stage is fuller than the calm one",
            Rms(suspicious) > Rms(calm),
            $"rms {Rms(suspicious):F4} vs {Rms(calm):F4}");
        Check.That("the critical stage is louder still",
            Rms(critical) > Rms(suspicious),
            $"rms {Rms(critical):F4} vs {Rms(suspicious):F4}");

        // And it isn't just level: the critical stage has drums, so its
        // peaks (kick transients) should stand well above the calm vamp's.
        Check.That("the critical stage adds transients, not just volume",
            Peak(critical) > Peak(calm) * 1.3f,
            $"peak {Peak(critical):F3} vs {Peak(calm):F3}");

        // A mood change mid-stream (the alarm crossing a threshold while a
        // buffer is cooking) must not corrupt the output.
        var mixed = new MusicSynth();
        var scratch = new float[sec];
        mixed.Mood = AlarmStage.Calm;
        mixed.Fill(scratch, sec);
        mixed.Mood = AlarmStage.Critical;
        mixed.Fill(scratch, sec);
        mixed.Mood = AlarmStage.Calm;
        mixed.Fill(scratch, sec);
        Check.That("mood switches mid-stream stay finite and bounded",
            scratch.All(float.IsFinite) && scratch.All(s => s >= -1f && s <= 1f),
            "corrupt buffer");

        // Determinism: identical construction and inputs, identical bytes.
        // (This is why the hat noise is an LCG instead of Random.Shared.)
        var a = new MusicSynth { Mood = AlarmStage.Critical };
        var b = new MusicSynth { Mood = AlarmStage.Critical };
        var bufA = new float[22050];
        var bufB = new float[22050];
        a.Fill(bufA, bufA.Length);
        b.Fill(bufB, bufB.Length);
        Check.That("two fresh synths agree exactly",
            bufA.SequenceEqual(bufB), "outputs diverged");
    }

    // Fills a warm-up buffer (so the layer gains have eased in) and then
    // renders the buffer actually measured.
    private static float[] RenderSteady(AlarmStage mood, int samples)
    {
        var synth = new MusicSynth { Mood = mood };
        var scratch = new float[samples];
        synth.Fill(scratch, samples);   // warm-up: layer gains arrive here
        var measured = new float[samples];
        synth.Fill(measured, measured.Length);
        return measured;
    }

    private static double Rms(float[] samples)
    {
        double sum = 0;
        foreach (var s in samples) sum += (double)s * s;
        return Math.Sqrt(sum / samples.Length);
    }

    private static float Peak(float[] samples) => samples.Max(Math.Abs);
}
