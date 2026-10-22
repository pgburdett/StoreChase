using System;
using System.Collections.Generic;
using System.Linq;
using StoreChase;

namespace StoreChase.Harness;

/// <summary>
/// A balance sweep: plays many rounds at a range of alarm settings and
/// reports the win rate for each.
///
/// This is how the alarm numbers get chosen. The old process was to play a
/// handful of rounds by hand and eyeball the result; this plays a few hundred
/// across every layout, so a change to a rate can be seen to move the win
/// rate in the intended direction rather than one round happening to go
/// differently.
///
/// The scripted bot is deliberately mediocre — it beelines for the nearest
/// item and ignores the cats — so a healthy win rate for it means a human who
/// actually chases thieves has margin.
/// </summary>
public static class Sweep
{
    /// <summary>Sweeps the stolen rate and reports the win rate for each.</summary>
    public static void RunSweep(int rounds = 40)
    {
        Console.WriteLine("Sweeping the stolen rate (the alarm's main dial).");
        Console.WriteLine();
        Console.WriteLine($"{"rate":>8}  " + string.Join("  ", StoreLayouts.All.Select(l => $"{l.Name,-12}")));

        float[] rates = { 0.020f, 0.026f, 0.032f, 0.042f, 0.050f, 0.060f, 0.075f };

        foreach (float rate in rates)
        {
            var cells = new List<string>();
            foreach (var layout in StoreLayouts.All)
            {
                int wins = 0;
                float total = 0f;
                for (int seed = 1; seed <= rounds; seed++)
                {
                    var r = RoundSim.Play(layout, seed, seconds: 180f, stolenRate: rate);
                    if (r.Won) wins++;
                    total += r.Seconds;
                }
                cells.Add($"{wins * 100 / rounds,3}% in {total / rounds,5:F1}s");
            }
            Console.WriteLine($"{rate,8:F3}  " + string.Join("  ", cells));
        }
    }

    /// <summary>Sweeps the counter-drop spike and reports the win rate for each.</summary>
    public static void RunSpikeSweep(int rounds = 40)
    {
        Console.WriteLine("Sweeping the counter-drop alarm spike.");
        Console.WriteLine();
        Console.WriteLine($"{"spike":>8}  " + string.Join("  ", StoreLayouts.All.Select(l => $"{l.Name,-12}")));

        float[] spikes = { 0f, 0.05f, 0.10f, 0.15f, 0.20f, 0.25f, 0.35f };

        foreach (float spike in spikes)
        {
            var cells = new List<string>();
            foreach (var layout in StoreLayouts.All)
            {
                int wins = 0;
                float total = 0f;
                for (int seed = 1; seed <= rounds; seed++)
                {
                    var r = RoundSim.Play(layout, seed, seconds: 180f, counterDropSpike: spike);
                    if (r.Won) wins++;
                    total += r.Seconds;
                }
                cells.Add($"{wins * 100 / rounds,3}% in {total / rounds,5:F1}s");
            }
            Console.WriteLine($"{spike,8:F2}  " + string.Join("  ", cells));
        }
    }

    /// <summary>Sweeps the store's alarm scale and reports the win rate for each.</summary>
    public static void RunScaleSweep(int rounds = 40)
    {
        Console.WriteLine("Sweeping the store's alarm scale (how forgiving a big store is).");
        Console.WriteLine();
        Console.WriteLine($"{"scale":>8}  " + string.Join("  ", StoreLayouts.All.Select(l => $"{l.Name,-12}")));

        float[] scales = { 0.25f, 0.35f, 0.45f, 0.55f, 0.65f, 0.80f, 1.0f };

        foreach (float scale in scales)
        {
            var cells = new List<string>();
            foreach (var layout in StoreLayouts.All)
            {
                int wins = 0;
                float total = 0f;
                for (int seed = 1; seed <= rounds; seed++)
                {
                    var r = RoundSim.Play(layout, seed, seconds: 180f, alarmScaleOverride: scale);
                    if (r.Won) wins++;
                    total += r.Seconds;
                }
                cells.Add($"{wins * 100 / rounds,3}% in {total / rounds,5:F1}s");
            }
            Console.WriteLine($"{scale,8:F2}  " + string.Join("  ", cells));
        }
    }
}
