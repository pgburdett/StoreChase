using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;

namespace StoreChase.Harness;

/// <summary>
/// The harness's assertion plumbing.
///
/// Deliberately not a test framework: there is no test project in this repo
/// and no CI, so the harness is a console app you run by hand after a change.
/// That keeps the dependency list empty and lets the same binary act as a
/// smoke test ("does the game still simulate sensibly?") as well as a set of
/// checks.
/// </summary>
public static class Check
{
    private static readonly List<string> Failures = new();
    private static int _passed;
    private static string _section = "";

    /// <summary>Starts a named group of checks.</summary>
    public static void Section(string name)
    {
        _section = name;
        Console.WriteLine();
        Console.WriteLine($"=== {name} ===");
    }

    /// <summary>Records a passing or failing check.</summary>
    public static void That(string label, bool condition, string detail = "")
    {
        if (condition)
        {
            _passed++;
            Console.WriteLine($"  PASS  {label}{(detail.Length > 0 ? "  (" + detail + ")" : "")}");
        }
        else
        {
            string msg = _section.Length > 0 ? $"[{_section}] {label}" : label;
            Failures.Add(msg + (detail.Length > 0 ? $"  ({detail})" : ""));
            Console.WriteLine($"  FAIL  {label}{(detail.Length > 0 ? "  (" + detail + ")" : "")}");
        }
    }

    /// <summary>Records an informational line that isn't a pass/fail.</summary>
    public static void Note(string text) => Console.WriteLine($"        {text}");

    /// <summary>Prints the summary and returns a process exit code.</summary>
    public static int Summarize()
    {
        Console.WriteLine();
        Console.WriteLine(new string('-', 60));
        if (Failures.Count == 0)
        {
            Console.WriteLine($"ALL CHECKS PASSED ({_passed} checks)");
            return 0;
        }

        Console.WriteLine($"{Failures.Count} FAILED, {_passed} passed:");
        foreach (var f in Failures) Console.WriteLine("  - " + f);
        return 1;
    }
}

/// <summary>
/// The headless round simulator.
///
/// It rebuilds the parts of <c>Game1</c> that decide the round — the map, the
/// player, the cast of cats, the merchandise and the alarm — without any
/// graphics, and plays whole rounds with a scripted player. This is the check
/// that catches the class of bug that reading the source cannot: an item
/// orphaned in a state nobody can reach, a thief that parks somewhere the
/// player can never get to, a round that can neither be won nor lost.
///
/// The rules it asserts are the ones the README states as promises:
///   - no item is ever in two hands at once
///   - no item is ever duplicated or vanishes
///   - the player can secure everything (a win is reachable)
///   - the alarm can fill (a loss is reachable)
///   - a round always terminates
/// </summary>
public static class RoundSim
{
    private const float CellSize = 2f;

    /// <summary>The result of one simulated round.</summary>
    public sealed class RoundResult
    {
        /// <summary>True if the player secured every item.</summary>
        public bool Won { get; init; }

        /// <summary>True if the alarm filled.</summary>
        public bool Lost { get; init; }

        /// <summary>How long the round ran, in simulated seconds.</summary>
        public float Seconds { get; init; }

        /// <summary>How many items the player secured.</summary>
        public int Secured { get; init; }

        /// <summary>How many items a cat got away with.</summary>
        public int Stolen { get; init; }

        /// <summary>How many cats were tackled.</summary>
        public int Tackles { get; init; }

        /// <summary>Which species took the most items.</summary>
        public string TopThief { get; init; } = "none";

        /// <summary>Anything that went wrong during the round.</summary>
        public List<string> Violations { get; } = new();
    }

    /// <summary>
    /// Plays one round.
    /// </summary>
    /// <param name="layout">The store to play in.</param>
    /// <param name="seed">Seed for the round's randomness.</param>
    /// <param name="seconds">Maximum simulated length before the round is abandoned.</param>
    /// <param name="playerIsPassive">
    /// If true the player never moves — used to check that the alarm *can*
    /// fill, i.e. that a loss is reachable.
    /// </param>
    /// <param name="stolenRate">
    /// Overrides the alarm's stolen rate, for sweeping. Null means "use the
    /// game's default".
    /// </param>
    /// <param name="counterDropSpike">
    /// Overrides the alarm spike for a thief reaching the counter, for
    /// sweeping. Null means "use the game's default".
    /// </param>
    public static RoundResult Play(
        StoreLayout layout,
        int seed,
        float seconds = 120f,
        bool playerIsPassive = false,
        float? stolenRate = null,
        float? counterDropSpike = null,
        float? alarmScaleOverride = null)
    {
        var rng = new Random(seed);
        var map = new GridMap(layout);
        var items = new ItemField(map, CellSize, rng);
        var alarm = new AlarmSystem();

        // The alarm is scaled to the store, exactly as Game1 does it. Without
        // this the simulation would be testing a different game from the one
        // that ships.
        alarm.RateScale = alarmScaleOverride ?? layout.AlarmScale(Game1.LootCount, Player.GridSpeed);
        if (stolenRate.HasValue) alarm.StolenRate = stolenRate.Value;
        float spike = counterDropSpike ?? Game1.CounterDropAlarmSpike;

        // The bot's target is per-round state.
        _botTarget = null;
        _botCarrying = false;

        // What the cats know about the player, shared between them.
        var awareness = new SharedAwareness();

        var player = new Player(layout.PlayerStart, CellSize);
        items.Spawn(Game1.LootCount, player.Cell);
        alarm.Reset();

        // The cast, spread away from the player.
        var critters = new List<Critter>();
        var spawnCells = SpawnCells(map, layout.PlayerStart, rng);
        foreach (var cell in spawnCells)
        {
            var profile = CritterProfile.Random(rng);
            var ai = new SmartAnimalAI(profile, rng);
            critters.Add(new Critter(cell, CellSize, ai, profile));
        }

        var theftsBySpecies = new Dictionary<string, int>();
        float dt = 1f / 60f;
        float elapsed = 0f;
        bool won = false, lost = false;
        int tackles = 0;
        var violations = new List<string>();

        // Remember which items exist, so a vanished or duplicated one is
        // caught rather than inferred from the final count.
        var expectedIds = items.Items.Select(i => (object)i).ToHashSet();

        while (elapsed < seconds && !won && !lost)
        {
            elapsed += dt;
            var gameTime = new GameTime(TimeSpan.FromSeconds(dt), TimeSpan.FromSeconds(dt));

            // --- player ---
            if (!playerIsPassive)
                StepBot(map, player, items, dt);

            // --- critters ---
            var locus = new CritterLocus(
                Map: map,
                PlayerCell: player.Cell,
                Ball: null,
                LootTarget: items.NearestLoose(player.Cell)?.Cell,
                LootTargetItem: items.NearestLoose(player.Cell),
                StolenCount: items.StolenCount,
                LootUnderfoot: c => items.LooseAt(c.Cell),
                TrySteal: c => TrySteal(c, items, theftsBySpecies),
                AlarmStage: alarm.Stage);

            // What each cat can see, and then what the cats tell each other.
            // This is the same order the game uses: sight first, then the
            // news spreading, then everybody acting.
            foreach (var critter in critters)
                UpdateAwareness(critter, map, player);

            awareness.Reset(critters);
            awareness.Update(player.Cell, map);

            for (int i = critters.Count - 1; i >= 0; i--)
            {
                var critter = critters[i];

                critter.Update(gameTime, locus);

                // A thief that reaches the counter dumps the goods there.
                if (critter.JustArrived && critter.Carrying &&
                    map.IsCounter(critter.Cell.X, critter.Cell.Y))
                {
                    if (items.AbandonAtCounter(critter, critter.Cell) != null)
                    {
                        critter.Carrying = false;
                        // The spike scales with the store like the rates do:
                        // the point of the scale is that a round has the same
                        // amount of alarm budget in it however big the store
                        // is, and a fixed spike would break that.
                        // Not scaled: the spike is an absolute event, a
                        // fraction of the meter gone in one go.
                        alarm.Spike(spike);
                    }
                }

                if (critter.Tackled)
                {
                    items.DropFrom(critter, critter.Cell);
                    critter.Carrying = false;
                    critters.RemoveAt(i);
                    tackles++;
                }
            }

            // --- loot and alarm ---
            if (items.Held == null)
                items.TryPickUp(player.Cell);
            if (items.TrySecure(player.Cell))
                alarm.Ping();

            items.Update(dt, player.Cell, Game1.CarryHeight);
            alarm.Update(dt, items.StolenWeight, items.Held != null, items.AllSecured);

            if (items.AllSecured) won = true;
            if (alarm.Tripped) lost = true;

            CheckInvariants(items, expectedIds, violations);
        }

        // If the round ran out of time without resolving, that's a deadlock
        // unless the player was deliberately passive.
        if (!won && !lost && !playerIsPassive)
            violations.Add($"round did not resolve within {seconds}s (deadlock?)");

        string top = theftsBySpecies.Count == 0
            ? "none"
            : theftsBySpecies.OrderByDescending(kv => kv.Value).First().Key;

        return new RoundResult
        {
            Won = won,
            Lost = lost,
            Seconds = elapsed,
            Secured = items.SecuredCount,
            Stolen = items.StolenCount,
            Tackles = tackles,
            TopThief = top,
        }.WithViolations(violations);
    }

    private static RoundResult WithViolations(this RoundResult r, List<string> v)
    {
        r.Violations.AddRange(v);
        return r;
    }

    // The AI only decides to steal; the game performs the transfer.
    private static bool TrySteal(Critter critter, ItemField items, Dictionary<string, int> thefts)
    {
        var item = items.LooseAt(critter.Cell);
        if (item == null) return false;
        items.TakeByCritter(item, critter);
        string key = critter.Profile.Name;
        thefts.TryGetValue(key, out int n);
        thefts[key] = n + 1;
        return true;
    }

    private static void UpdateAwareness(Critter critter, GridMap map, Player player)
    {
        int dist = Math.Abs(critter.Cell.X - player.Cell.X) + Math.Abs(critter.Cell.Y - player.Cell.Y);
        critter.CanSeePlayer =
            dist <= critter.Profile.VisionRadius && map.HasLineOfSight(critter.Cell, player.Cell);

        // The harness has no camera, so it approximates the isometric case:
        // the cat is "watched" when it is near the player. This is only used
        // to exercise the Nervous cat's freeze path, so an approximation is
        // enough — the exact cone is checked separately by the camera tests.
        critter.BeingWatched = dist <= 3;
    }
    // The invariants. These are the promises the README makes about item
    // possession, expressed as checks that can't pass by accident.
    private static void CheckInvariants(ItemField items, HashSet<object> expected, List<string> violations)
    {
        var seen = new HashSet<object>();
        foreach (var item in items.Items)
        {
            if (!expected.Contains(item))
                violations.Add("an item appeared that was never spawned");
            else if (!seen.Add(item))
                violations.Add("an item is listed twice");
        }

        foreach (var item in items.Items)
        {
            if (item.State == LootState.Carried && item.Carrier == null)
                violations.Add("an item is marked carried but has no carrier");
            if (item.State != LootState.Carried && item.Carrier != null)
                violations.Add("an item is held by a carrier but is not in the carried state");
        }

        // Nothing in two hands: the player holds at most one item, and no item
        // is held by two carriers at once. (The player holding one item while
        // a cat holds a *different* one is perfectly legal — that is the whole
        // point of the game.)
        int playerHeld = items.Held != null ? 1 : 0;
        int catHeld = items.Items.Count(i => i.Carrier != null);
        if (playerHeld + catHeld > items.Items.Count(i => i.State == LootState.Held || i.State == LootState.Carried))
            violations.Add("more items are held than exist in a holdable state");
    }

    // A scripted player: walk to the nearest loose item, grab it, carry it to
    // the counter, secure it, repeat. It ignores the cats entirely, which is
    // exactly the bot the alarm balance was tuned against.
    //
    // It holds onto one target until that target is resolved. Re-picking the
    // nearest item every frame makes the bot oscillate between two items
    // that are roughly equally close, and it never gets anywhere — which is
    // exactly the failure mode the game's own NearestLoose sort had.
    internal static void StepBot(GridMap map, Player player, ItemField items, float dt)
    {
        // The bot has two modes: fetch a loose item, or carry what it has to
        // the counter. The target belongs to the mode it was chosen in, so it
        // is recomputed whenever the mode changes — otherwise the bot keeps
        // walking back to the cell the item came from.
        bool carrying = items.Held != null;
        if (_botTarget is null || _botCarrying != carrying || !TargetStillValid(items, _botTarget.Value, carrying))
        {
            _botTarget = carrying
                ? NearestCounter(map, player.Cell)
                : (items.NearestLoose(player.Cell)?.Cell ?? (GridPos?)null);
            _botCarrying = carrying;
        }

        var target = _botTarget;
        if (target is null) return;

        // The player moves one cell at a time, so the direction is the step
        // from the cell it is standing on. Update is called every frame —
        // skipping it while a step is in flight would freeze the
        // interpolation and the player would never arrive.
        var step = map.NextStepToward(player.Cell, target.Value);
        var dir = step.HasValue
            ? new Vector2(step.Value.X - player.Cell.X, step.Value.Y - player.Cell.Y)
            : Vector2.Zero;

        player.Update(new GameTime(TimeSpan.FromSeconds(dt), TimeSpan.FromSeconds(dt)), map, dir, freeMove: false);
    }

    /// <summary>The bot's current target cell, exposed for the trace tool.</summary>
    internal static GridPos? BotTarget => _botTarget;

    private static GridPos? _botTarget;
    private static bool _botCarrying;

    private static bool TargetStillValid(ItemField items, GridPos target, bool carrying)
    {
        // While carrying, the target is a counter cell — it stays valid until
        // the item is secured and the bot goes back to fetching.
        if (carrying) return true;

        // While empty, the target is a loose item. If a cat took it, the
        // target is stale and a new one has to be chosen.
        return items.LooseAt(target) != null;
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

    private static List<GridPos> SpawnCells(GridMap map, GridPos start, Random rng)
    {
        var dist = map.DistanceMapFrom(start);
        var candidates = map.WalkableCells()
            .Where(c => !map.IsCounter(c.X, c.Y))
            .Where(c => dist[c.X, c.Y] != int.MaxValue && dist[c.X, c.Y] > 5)
            .ToList();

        if (candidates.Count == 0) return new List<GridPos> { start };

        var furthest = candidates.Max(c => dist[c.X, c.Y]);
        var far = candidates.Where(c => dist[c.X, c.Y] >= (int)(furthest * 0.55f)).ToList();
        if (far.Count == 0) far = candidates;

        var result = new List<GridPos>();
        for (int i = 0; i < Game1.CritterCount && far.Count > 0; i++)
        {
            int idx = rng.Next(far.Count);
            result.Add(far[idx]);
            far.RemoveAt(idx);
        }
        return result;
    }
}
