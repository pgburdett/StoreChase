using System;
using System.Collections.Generic;
using System.Linq;
using StoreChase;

namespace StoreChase.Harness;

/// <summary>
/// The harness entry point.
///
/// Run with:  dotnet run --project harness/StoreChase.Harness
///
/// Everything here runs without a display. The game's gameplay systems are
/// deliberately free of GraphicsDevice dependencies, and the parts that need
/// geometry (the OBJ parser, the rig) have device-free paths, so the whole
/// thing can be simulated from a terminal.
/// </summary>
public static class Program
{
    public static int Main(string[] args)
    {
        Console.WriteLine("StoreChase headless harness");
        Console.WriteLine("============================");

        bool onlyRounds = args.Contains("--rounds");
        bool onlyRig = args.Contains("--rig");
        bool onlyGeom = args.Contains("--geometry");
        bool anyFlag = onlyRounds || onlyRig || onlyGeom;

        // A trace of a single round, for working out a balance number.
        if (args.Contains("--trace"))
        {
            int seed = 1;
            var layoutName = "Standard";
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == "--seed" && int.TryParse(args[i + 1], out int s)) seed = s;
                if (args[i] == "--layout" && i + 1 < args.Length) layoutName = args[i + 1];
            }
            Trace.Run(layoutName, seed);
            return 0;
        }

        // A balance sweep: many rounds at a range of alarm settings.
        if (args.Contains("--sweep"))
        {
            if (args.Contains("--spike")) Sweep.RunSpikeSweep();
            else if (args.Contains("--scale")) Sweep.RunScaleSweep();
            else Sweep.RunSweep();
            return 0;
        }

        if (!anyFlag || onlyGeom) { RunGeometry(); CameraChecks.Run(); GridChecks.Run(); InputChecks.Run(); LootChecks.Run(); SpeciesChecks.Run(); PowerUpChecks.Run(); HazardChecks.Run(); FxChecks.Run(); MusicChecks.Run(); CampaignChecks.Run(); AwarenessChecks.Run(); MinimapChecks.Run(); WindingChecks.Run(); StoreChecks.Run(); NoiseChecks.Run(); FriendChecks.Run(); ShiftChecks.Run(); CustomerChecks.Run(); }
        if (!anyFlag || onlyRig) RunRigs();
        if (!anyFlag || onlyRounds) RunRounds();

        return Check.Summarize();
    }

    private static void RunGeometry()
    {
        GeometryChecks.Run();
    }

    private static void RunRigs()
    {
        Check.Section("Rig: edge stretch per pose");
        string objPath = GeometryChecks.CatObjPath();
        if (!System.IO.File.Exists(objPath))
        {
            Check.Note($"cat OBJ not found at '{objPath}'; skipping rig checks");
            return;
        }

        var reports = RigCheck.MeasureAllPoses(objPath);
        foreach (var r in reports)
        {
            string regions = string.Join(", ",
                r.WorstByRegion.OrderBy(kv => kv.Key)
                    .Select(kv => $"{kv.Key} {kv.Value:F2}x ({r.EdgeCount[kv.Key]} edges)"));
            Check.Note($"{r.Pose,-9} worst {r.WorstOverall:F2}x in {r.WorstRegion,-7} | {regions}");
        }

        // No pose may tear a visible edge. This is the threshold that caught
        // the first pass of the pose table, which read fine in source and was
        // a shapeless blob on screen.
        foreach (var r in reports)
        {
            Check.That($"pose '{r.Pose}' does not tear visible edges",
                r.WorstOverall <= 2.5f,
                $"worst {r.WorstOverall:F2}x in {r.WorstRegion}, {r.TearingCount} torn edges");
        }

        // Every region should be represented, or the measurement is silently
        // only looking at part of the model.
        var allRegions = reports.SelectMany(r => r.EdgeCount.Keys).Distinct().ToList();
        Check.That("the measurement covers head, tail, legs and torso",
            new[] { "head", "tail", "legs", "torso" }.All(r => allRegions.Contains(r)),
            string.Join(", ", allRegions));
    }

    private static void RunRounds()
    {
        Check.Section("Rounds: item invariants and reachability");

        // How much work is each store? This drives the alarm scaling.
        foreach (var layout in StoreLayouts.All)
        {
            float mean = layout.MeanFetchDistance();
            float clear = layout.EstimatedClearSeconds(StoreChase.Game1.LootCount, StoreChase.Player.GridSpeed);
            float scale = layout.AlarmScale(StoreChase.Game1.LootCount, StoreChase.Player.GridSpeed);
            Check.Note($"{layout.Name}: mean fetch {mean:F1} cells, expected clear {clear:F1}s, alarm scale {scale:F3}");
        }


        foreach (var layout in StoreLayouts.All)
        {
            const int rounds = 12;

            int wins = 0, losses = 0, unresolved = 0;
            var violations = new List<string>();
            float totalTime = 0f;

            for (int seed = 1; seed <= rounds; seed++)
            {
                var result = RoundSim.Play(layout, seed);
                violations.AddRange(result.Violations.Select(v => $"{layout.Name} seed {seed}: {v}"));
                if (result.Won) wins++;
                if (result.Lost) losses++;
                if (!result.Won && !result.Lost) unresolved++;
                totalTime += result.Seconds;
            }

            Check.That($"'{layout.Name}': no item invariant was ever violated",
                violations.Count == 0,
                violations.Count == 0 ? $"{rounds} rounds" : string.Join("; ", violations.Take(3)));

            Check.That($"'{layout.Name}': every round resolves (no deadlock)",
                unresolved == 0, $"{unresolved}/{rounds} unresolved");

            Check.That($"'{layout.Name}': a win is reachable",
                wins > 0, $"{wins}/{rounds} won by the scripted bot");

            // A loss must be reachable too, otherwise the alarm is not a
            // threat. This is checked with a passive player who never
            // retrieves anything.
            int passiveLosses = 0;
            int passiveWins = 0;
            var passiveTimes = new List<float>();
            for (int seed = 1; seed <= 8; seed++)
            {
                var result = RoundSim.Play(layout, seed, seconds: 180f, playerIsPassive: true);
                if (result.Lost) passiveLosses++;
                if (result.Won) passiveWins++;
                passiveTimes.Add(result.Seconds);
            }
            Check.That($"'{layout.Name}': a loss is reachable (passive player)",
                passiveLosses > 0, $"{passiveLosses}/8 passive rounds lost, avg {passiveTimes.Average():F1}s");

            // A player who never moves must never win. If they do, something
            // is securing goods without the player doing anything, which is
            // the kind of bug that makes a round unwinnable in the other
            // direction too.
            Check.That($"'{layout.Name}': a passive player never wins",
                passiveWins == 0, $"{passiveWins} passive wins");

            Check.Note($"{layout.Name}: {wins} wins / {losses} losses / {unresolved} unresolved, " +
                       $"avg {totalTime / rounds:F1}s per round");

            // Per-round detail, so a balance problem can be pinned on a seed.
            for (int seed = 1; seed <= rounds; seed++)
            {
                var r = RoundSim.Play(layout, seed);
                Check.Note($"  seed {seed,2}: {(r.Won ? "WON " : "LOST")} in {r.Seconds,5:F1}s  " +
                           $"secured {r.Secured}/{StoreChase.Game1.LootCount}  stolen {r.Stolen}  " +
                           $"tackles {r.Tackles}  top thief {r.TopThief}");
            }
        }
    }
}
