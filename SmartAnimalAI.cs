using System;
using System.Collections.Generic;
using System.Linq;

namespace StoreChase;

/// <summary>
/// The default critter brain: a distance-tiered animal that can also steal.
/// All of its personality comes from a <see cref="CritterProfile"/>, so the
/// species differ in numbers and a few flags rather than in separate classes.
///
/// Priority order for each decision:
///   1. standing on loot and inclined to take it  -> stand still and take it
///   2. being watched, if this profile freezes     -> do nothing at all
///   3. a ball is bouncing nearby                 -> go and look at the ball
///   4. carrying something and the player is close -> run, ideally for the till
///   5. carrying something and the player is far   -> make for the counter
///   6. the player is close                       -> flee
///   7. the player is nearby                      -> drift away
///   8. otherwise                                 -> wander, biased toward shelves
///
/// The loot rules are the interesting part: a thief that reaches the counter
/// drops what it stole there and the alarm spikes, so bolting for the till is
/// a real (if losing) option rather than an exploit.
/// </summary>
public class SmartAnimalAI : ICritterAI
{
    private readonly CritterProfile _profile;
    private readonly Random _rng;

    private List<GridPos> _currentPath = new();
    private float _replanTimer;
    private GridPos? _lastCell;

    // Seconds left of "I have just noticed you" panic, which is what gates the
    // Startle pose.
    private float _startleTimer;
    private float _freezeTimer;

    // Scratch buffers for the escape planner. Reused because this runs per
    // critter per decision and a fresh int[,] every time is pure garbage.
    private int[,]? _distFromPlayer;
    private int[,]? _distFromSelf;

    /// <summary>True when the AI wants to steal what's underfoot (set by <see cref="DecideNextMove"/>).</summary>
    public bool WantsToSteal { get; private set; }

    /// <summary>The current pose target for the animation rig.</summary>
    public CatPose Pose { get; private set; }

    /// <summary>Creates a new SmartAnimalAI with the given profile and RNG.</summary>
    public SmartAnimalAI(CritterProfile profile, Random rng)
    {
        _profile = profile;
        _rng = rng;
        Pose = profile.IdlePose;
    }

    /// <summary>The profile this AI uses for all its decisions.</summary>
    public CritterProfile Profile => _profile;

    /// <summary>
    /// Analyzes the context and decides the next destination. See the class
    /// comment for the priority order.
    /// </summary>
    public GridPos? DecideNextMove(CritterContext context)
    {
        WantsToSteal = false;
        _replanTimer -= context.DeltaTime;
        _startleTimer = MathF.Max(0f, _startleTimer - context.DeltaTime);
        _freezeTimer = MathF.Max(0f, _freezeTimer - context.DeltaTime);

        int dist = ManhattanDistance(context.CurrentCell, context.PlayerCell);
        bool threatened = context.CanSeePlayer &&
                          dist <= _profile.FleeRadius;

        GridPos? result = Decide(context, dist, threatened);

        _lastCell = context.CurrentCell;
        UpdatePose(context, dist, threatened, result.HasValue);
        return result;
    }

    private GridPos? Decide(CritterContext context, int dist, bool threatened)
    {
        // 1. Loot underfoot. Standing still is the move: the game picks it up.
        if (context.LootUnderfoot != null && !context.Carrying)
        {
            if (ShouldTakeLoot(context, out bool paused))
            {
                WantsToSteal = true;
                if (paused) return null;
                // Otherwise fall through and keep moving; it grabbed it on the way.
            }
        }

        // 2. A Nervous cat that can see you and is being looked at roots itself
        // to the spot. This is the one behaviour that rewards the first-person
        // camera, since the isometric view has everything in frame.
        if (_profile.FreezesWhenWatched && context.BeingWatched && context.CanSeePlayer
            && dist <= _profile.AlertRadius)
        {
            _freezeTimer = 0.5f;
            _currentPath.Clear();
            return null;
        }

        // 3. The ball wins over caution, but not over a thief in the middle of
        // an escape. The planned path is dropped: it was computed for a
        // different goal from a different cell, and resuming it later would
        // send the cat off stale steps.
        if (context.Ball is { } ball && !_profile.IgnoresBall && !context.Carrying)
        {
            _currentPath.Clear();
            return PathStepToward(context.Map, context.CurrentCell, ball);
        }

        // 4/5. Carrying loot. Close and looking -> get away. Far enough -> take
        // the long run at the counter.
        if (context.Carrying)
        {
            return threatened
                ? Flee(context)
                : HeadForCounter(context);
        }

        // 6. Too close. Run.
        if (threatened)
        {
            _startleTimer = 0.8f;
            return Flee(context);
        }

        // 7. Noticed you, but no rush.
        if (context.CanSeePlayer && dist <= _profile.AlertRadius)
        {
            return Alert(context);
        }

        // 8. Nothing happening. Go and find something to steal, or wander.
        if (context.LootTarget is { } target && context.StolenCount < 3)
        {
            if (_rng.NextDouble() < _profile.ThiefDrive)
                return PathStepToward(context.Map, context.CurrentCell, target);
        }

        return Wander(context);
    }

    // Decides whether to take loot underfoot, and whether doing so means
    // standing still for a moment (a cat that spots a can is briefly busy).
    private bool ShouldTakeLoot(CritterContext context, out bool paused)
    {
        paused = false;
        if (_profile.ThiefDrive <= 0f) return false;

        bool inclined = _rng.NextDouble() < _profile.ThiefDrive;
        if (!inclined) return false;

        // A cat that can see the player standing right there is less likely to
        // stop and start loading up, and more likely to skip it entirely.
        if (context.CanSeePlayer && ManhattanDistance(context.CurrentCell, context.PlayerCell) <= 2)
        {
            paused = _rng.NextDouble() < 0.4f;
            return _rng.NextDouble() < 0.35f;
        }

        // A cat that's fleeing grabs whatever's underfoot without ceremony.
        paused = _rng.NextDouble() < 0.5f;
        return true;
    }

    // Full escape plan: two BFS distance maps, then walk to whichever walkable
    // tile maximises (distance from the player) minus half the detour to get
    // there. Penalising the detour is what stops it from sprinting one step
    // sideways every time.
    private GridPos? Flee(CritterContext context)
    {
        if (_replanTimer <= 0f || _currentPath.Count == 0)
        {
            _replanTimer = _profile.ReplanInterval;
            _currentPath = PlanEscapeRoute(context);
        }

        if (_currentPath.Count > 0)
        {
            if (_rng.NextDouble() < _profile.QuirkChance)
            {
                var quirk = ChooseQuirkMove(context);
                if (quirk.HasValue)
                {
                    _currentPath.Clear();
                    return quirk;
                }
            }

            var next = _currentPath[0];
            _currentPath.RemoveAt(0);
            return next;
        }

        return null;
    }

    private List<GridPos> PlanEscapeRoute(CritterContext context)
    {
        _distFromPlayer = context.Map.DistanceMapFrom(context.PlayerCell);
        _distFromSelf = context.Map.DistanceMapFrom(context.CurrentCell);

        GridPos? bestTarget = null;
        int bestScore = int.MinValue;

        for (int y = 0; y < context.Map.Height; y++)
        {
            for (int x = 0; x < context.Map.Width; x++)
            {
                if (!context.Map.IsWalkable(x, y)) continue;

                int reachDist = _distFromSelf[x, y];
                if (reachDist == int.MaxValue || reachDist > 6) continue;

                int safetyDist = _distFromPlayer[x, y];
                if (safetyDist == int.MaxValue) continue;

                int score = safetyDist - (reachDist / 2);
                if (score > bestScore)
                {
                    bestScore = score;
                    bestTarget = new GridPos(x, y);
                }
            }
        }

        if (!bestTarget.HasValue) return new List<GridPos>();
        return BuildPathTo(context.Map, context.CurrentCell, bestTarget.Value);
    }

    // Walking to the counter is the thief's "escape". It drops the goods on
    // arrival, which spikes the alarm, so this is a delaying tactic: it buys
    // time and moves the loot somewhere inconvenient, not a way out.
    private GridPos? HeadForCounter(CritterContext context)
    {
        var counter = NearestCounterCell(context.CurrentCell, context.Map);
        if (!counter.HasValue) return Wander(context);

        if (counter.Value == context.CurrentCell) return null;

        if (_replanTimer <= 0f || _currentPath.Count == 0)
        {
            _replanTimer = _profile.ReplanInterval;
            _currentPath = BuildPathTo(context.Map, context.CurrentCell, counter.Value);
        }

        if (_currentPath.Count > 0)
        {
            var next = _currentPath[0];
            _currentPath.RemoveAt(0);
            return next;
        }

        return null;
    }

    /// <summary>Returns the nearest counter cell to the given position, or null if none is walkable.</summary>
    public static GridPos? NearestCounterCell(GridPos from, GridMap map)
    {
        GridPos? best = null;
        int bestDist = int.MaxValue;
        for (int x = GridMap.CounterStart; x <= GridMap.CounterEnd; x++)
        {
            var cell = new GridPos(x, GridMap.CounterRow);
            if (!map.IsWalkable(cell.X, cell.Y)) continue;
            int d = Math.Abs(cell.X - from.X) + Math.Abs(cell.Y - from.Y);
            if (d < bestDist) { bestDist = d; best = cell; }
        }
        return best;
    }

    // Keeping a little distance without a real plan: take any step that doesn't
    // close the gap. Cheap, and reads as "the cat is keeping its distance".
    private GridPos? Alert(CritterContext context)
    {
        GridPos? fallback = null;
        foreach (var d in ShuffledDirections())
        {
            var candidate = new GridPos(context.CurrentCell.X + d.X, context.CurrentCell.Y + d.Y);
            if (!context.Map.IsWalkable(candidate.X, candidate.Y)) continue;

            fallback ??= candidate;
            if (ManhattanDistance(candidate, context.PlayerCell) >= ManhattanDistance(context.CurrentCell, context.PlayerCell))
                return candidate;
        }
        return fallback;
    }

    // Aimless ambling, with a mild bias toward shelf edges. That's where the
    // merchandise is, so a wandering thief tends to find some — which keeps the
    // heist going without every cat pathing to the loot from across the store.
    private GridPos? Wander(CritterContext context)
    {
        foreach (var d in ShuffledDirections())
        {
            var candidate = new GridPos(context.CurrentCell.X + d.X, context.CurrentCell.Y + d.Y);
            if (!context.Map.IsWalkable(candidate.X, candidate.Y)) continue;

            bool onShelfEdge = !context.Map.IsWalkable(candidate.X + d.X, candidate.Y + d.Y);
            if (onShelfEdge && _rng.NextDouble() < 0.5f) return candidate;
        }

        foreach (var d in ShuffledDirections())
        {
            var candidate = new GridPos(context.CurrentCell.X + d.X, context.CurrentCell.Y + d.Y);
            if (context.Map.IsWalkable(candidate.X, candidate.Y)) return candidate;
        }
        return null;
    }

    // A quirk overrides a plan for one step. Half the time it's a lateral
    // juke off the line to the player; otherwise it's doubling back to the cell
    // it just left, which is what makes a fleeing cat look like it's thinking.
    private GridPos? ChooseQuirkMove(CritterContext context)
    {
        if (_rng.NextDouble() < 0.5f)
        {
            var sidestep = TrySidestep(context);
            if (sidestep.HasValue) return sidestep;
        }

        if (_lastCell is { } last &&
            context.Map.IsWalkable(last.X, last.Y) &&
            ManhattanDistance(last, context.CurrentCell) == 1)
        {
            return last;
        }
        return null;
    }

    // The direction to the player defines an axis, so the perpendicular axis is
    // a side-step. Handles axis-aligned and diagonal bearings.
    private GridPos? TrySidestep(CritterContext context)
    {
        int dx = Math.Sign(context.PlayerCell.X - context.CurrentCell.X);
        int dy = Math.Sign(context.PlayerCell.Y - context.CurrentCell.Y);

        GridPos[] candidates =
        {
            new(context.CurrentCell.X + dy, context.CurrentCell.Y - dx),
            new(context.CurrentCell.X - dy, context.CurrentCell.Y + dx),
        };
        return FirstWalkable(context, candidates);
    }

    private GridPos? FirstWalkable(CritterContext context, GridPos[] candidates)
    {
        foreach (var c in candidates)
            if (context.Map.IsWalkable(c.X, c.Y)) return c;
        return null;
    }

    // One BFS step toward a goal, without holding a whole path. Used for the
    // short-range goals (the ball, the counter, a nearby item) where holding a
    // path would mean re-planning it on every step anyway.
    private static GridPos? PathStepToward(GridMap map, GridPos from, GridPos to)
        => from == to ? null : map.NextStepToward(from, to);

    private List<GridPos> BuildPathTo(GridMap map, GridPos from, GridPos to)
    {
        var path = new List<GridPos>();
        var current = from;
        int safety = 64;

        while (current != to && safety-- > 0)
        {
            var next = map.NextStepToward(current, to);
            if (!next.HasValue) break;
            path.Add(next.Value);
            current = next.Value;
        }
        return path;
    }

    private GridPos[] ShuffledDirections()
    {
        GridPos[] options = { new(0, -1), new(0, 1), new(-1, 0), new(1, 0) };
        for (int i = options.Length - 1; i > 0; i--)
        {
            int j = _rng.Next(i + 1);
            (options[i], options[j]) = (options[j], options[i]);
        }
        return options;
    }

    // The rig only gets told a pose; how urgent it looks comes from the profile
    // and whether the cat is currently panicking.
    private void UpdatePose(CritterContext context, int dist, bool threatened, bool moving)
    {
        if (_startleTimer > 0f)
        {
            Pose = CatPose.Startle;
            return;
        }

        if (moving)
        {
            Pose = CatPose.Walk;
            return;
        }

        // Standing still. What a stationary cat does is personality: the fat one
        // sleeps, the nervous one is rooted and staring, everyone else idles.
        Pose = _freezeTimer > 0f
            ? CatPose.Stand
            : _profile.IdlePose;

        if (_profile.IdlePose == CatPose.Loaf && _rng.NextDouble() < 0.02f)
        {
            Pose = CatPose.Groom; // occasional grooming beat
        }
    }

    private static int ManhattanDistance(GridPos a, GridPos b) => Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y);
}
