using System;
using System.Linq;
using Microsoft.Xna.Framework;
using StoreChase;

namespace StoreChase.Harness;

/// <summary>
/// A diagnostic trace of one round: what the scripted bot does, frame by
/// frame, and where the round actually goes.
///
/// Used to work out why a balance number looks wrong. Not part of the normal
/// harness run.
/// </summary>
public static class Trace
{
    /// <summary>Traces a single round to the console.</summary>
    public static void Run(string layoutName = "Standard", int seed = 1)
    {
        var layout = StoreLayouts.All.First(l => l.Name == layoutName);
        var map = new GridMap(layout);
        var rng = new Random(seed);
        var items = new ItemField(map, 2f, rng);
        var alarm = new AlarmSystem();
        var player = new Player(layout.PlayerStart, 2f);
        items.Spawn(5, player.Cell);
        alarm.Reset();

        Console.WriteLine($"Layout {layout.Name} ({map.Width}x{map.Height}), seed {seed}");
        Console.WriteLine($"Player starts at {layout.PlayerStart.X},{layout.PlayerStart.Y}");
        Console.WriteLine($"Counter: row {map.CounterRow}, x {map.CounterStart}..{map.CounterEnd}");
        Console.WriteLine($"Loot spawned at: {string.Join(", ", items.Items.Select(i => $"({i.Cell.X},{i.Cell.Y})"))}");
        Console.WriteLine($"Player start is counter? {map.IsCounter(layout.PlayerStart.X, layout.PlayerStart.Y)}");
        Console.WriteLine();

        var dist = map.DistanceMapFrom(layout.PlayerStart);
        foreach (var i in items.Items)
            Console.WriteLine($"  item ({i.Cell.X},{i.Cell.Y}) walking distance from start = {(dist[i.Cell.X, i.Cell.Y] == int.MaxValue ? "UNREACHABLE" : dist[i.Cell.X, i.Cell.Y].ToString())}");

        Console.WriteLine();
        float dt = 1f / 60f;
        int frame = 0;
        string lastState = "";
        float thiefTime = 0f, carryTime = 0f, calmTime = 0f;

        while (frame < 60 * 60)
        {
            frame++;
            var gt = new GameTime(TimeSpan.FromSeconds(dt), TimeSpan.FromSeconds(dt));

            // Bot: the same logic the round simulator uses, so the trace and
            // the simulation can't drift apart.
            RoundSim.StepBot(map, player, items, dt);

            // Loot.
            if (items.Held == null) items.TryPickUp(player.Cell);
            if (items.TrySecure(player.Cell)) { alarm.Ping(); Console.WriteLine($"  [{frame}] SECURED at counter, now {items.SecuredCount}/{items.TotalCount}"); }
            items.Update(dt, player.Cell, 1.5f);
            float before = alarm.Level;
            alarm.Update(dt, items.StolenWeight, items.Held != null, items.AllSecured);
            float delta = alarm.Level - before;
            if (MathF.Abs(delta) > 0.0001f)
            {
                var cause = alarm.Cause;
                if (cause == AlarmCause.Thief) thiefTime += dt;
                else if (cause == AlarmCause.PlayerCarrying) carryTime += dt;
                else calmTime += dt;
            }

            var t = RoundSim.BotTarget;
            string state = $"player=({player.Cell.X},{player.Cell.Y}) held={(items.Held != null)} secured={items.SecuredCount} stolen={items.StolenCount} alarm={alarm.Level:F2} target={(t.HasValue ? $"{t.Value.X},{t.Value.Y}" : "none")}";
            if (state != lastState)
            {
                Console.WriteLine($"  [{frame,4}] {state}");
                lastState = state;
            }

            if (items.AllSecured) { Console.WriteLine($"  WON at {frame / 60f:F1}s  (thief {thiefTime:F1}s, carrying {carryTime:F1}s, calm {calmTime:F1}s)"); return; }
            if (alarm.Tripped) { Console.WriteLine($"  LOST at {frame / 60f:F1}s (secured {items.SecuredCount}/{items.TotalCount})  (thief {thiefTime:F1}s, carrying {carryTime:F1}s, calm {calmTime:F1}s)"); return; }
        }

        Console.WriteLine($"  TIMEOUT at {frame / 60f:F1}s (secured {items.SecuredCount}/{items.TotalCount}, stolen {items.StolenCount})");
    }

    private static GridPos NearestCounter(GridMap map, GridPos from)
    {
        GridPos best = new(map.CounterStart, map.CounterRow);
        int bestDist = int.MaxValue;
        for (int x = map.CounterStart; x <= map.CounterEnd; x++)
        {
            var cell = new GridPos(x, map.CounterRow);
            if (!map.IsWalkable(cell.X, cell.Y)) continue;
            int d = Math.Abs(cell.X - from.X) + Math.Abs(cell.Y - from.Y);
            if (d < bestDist) { bestDist = d; best = cell; }
        }
        return best;
    }
}
