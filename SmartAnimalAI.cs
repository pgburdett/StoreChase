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
///   1. panicking (sprayed)                       -> run, ignore everything
///   2. standing on loot and inclined to take it  -> stand still and take it
///   3. being watched, if this profile freezes     -> do nothing at all
///   4. a ball is bouncing nearby                 -> go and look at the ball
///   5. a treat has landed nearby                 -> go and eat it (friends!)
///   6. befriended                                -> shadow the player
///   7. carrying something and the player is close -> run, ideally for the till
///   8. carrying something and the player is far   -> make for the counter
///   9. the player is close                       -> flee
///   10. the player is nearby                      -> drift away
///   10b. heard about the player, can't see them  -> head for where they were
///   11. otherwise                                 -> wander, biased toward shelves
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
    private GridPos? _patrolTarget;

    // Seconds left of "I have just noticed you" panic, which is what gates the
    // Startle pose.
    private float _startleTimer;
    private float _freezeTimer;

    // Scratch buffers for the escape planner. Reused because this runs per
    // critter per decision and a fresh int[,] every time is pure garbage.
    private int[,]? _distFromPlayer;
    private int[,]? _distFromSelf;

    /// <summary>How far away a cat notices a dropped treat, in cells. The
    /// ball has no such limit (one cat, one ball); the treat pulls a crowd,
    /// so it needs a range or it would empty the store from anywhere.</summary>
    public const int TreatRadius = 10;

    /// <summary>
    /// How close a befriended cat walks to the player, in cells. Below this
    /// it stands; it never shares the player's cell (walking into a friend
    /// gives ground instead — see <see cref="FollowPlayer"/>).
    /// </summary>
    public const int FriendFollowDistance = 3;

    /// <summary>True when the AI wants to steal what's underfoot (set by <see cref="DecideNextMove"/>).</summary>
    public bool WantsToSteal { get; private set; }

    /// <summary>True when the AI wants the treat underfoot (set by <see cref="DecideNextMove"/>).</summary>
    public bool WantsTreat { get; private set; }

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
        WantsTreat = false;
        _replanTimer -= context.DeltaTime;
        _startleTimer = MathF.Max(0f, _startleTimer - context.DeltaTime);
        _freezeTimer = MathF.Max(0f, _freezeTimer - context.DeltaTime);

        int dist = ManhattanDistance(context.CurrentCell, context.PlayerCell);

        // A friend is never a threatened animal...
        bool threatened = context.CanSeePlayer && !context.Befriended &&
                          dist <= (_profile.FleeRadius + (context.Mood == Mood.Wary ? 2 : 0));

        GridPos? result = Decide(context, dist, threatened);

        _lastCell = context.CurrentCell;
        UpdatePose(context, dist, threatened, result.HasValue);
        return result;
    }

    private GridPos? Decide(CritterContext context, int dist, bool threatened)
    {
        // 0. Panic (sprayed). Whatever it was doing, it is now running from
        // the player. It also stops stealing while panicking — see
        // ShouldTakeLoot — so this is pure crowd control.
        if (context.Panic > 0f)
        {
            _currentPath.Clear();
            _startleTimer = MathF.Max(_startleTimer, 0.4f);
            return Flee(context);
        }

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

        // 4. A treat works like the ball but pulls every cat in earshot, so
        // it is gated by distance: a snack across the store is not news.
        // Standing on it means eating it — the game turns the snack into
        // friendship and consumes the treat (WantsTreat).
        if (context.Treat is { } treat && !context.Carrying &&
            ManhattanDistance(context.CurrentCell, treat) <= TreatRadius)
        {
            if (context.CurrentCell == treat)
            {
                WantsTreat = true;
                return null; // busy eating
            }

            _currentPath.Clear();
            return PathStepToward(context.Map, context.CurrentCell, treat);
        }

        // 5b. Calm patrol: a cat with no idea where the player is walks a lazy
        // route between shelf blocks instead of standing still or wandering at
        // random, so the early game reads as a store with regulars in it.
        if (!context.Carrying && !context.Befriended && context.Ball == null
            && context.Treat == null && context.PlayerAwareness <= 0f
            && !context.CanSeePlayer && context.LastSeenPlayerCell == null)
        {
            if (!_patrolTarget.HasValue || _patrolTarget.Value == context.CurrentCell)
                _patrolTarget = PickPatrolTarget(context.Map, context.CurrentCell, _rng);
            _currentPath.Clear();
            return PathStepToward(context.Map, context.CurrentCell, _patrolTarget.Value);
        }

        // 5. Befriended. A fed cat is neither thief nor prey: it shadows the
        // player like a shop cat at heel. Feeding already stopped it
        // stealing for good (see ShouldTakeLoot), so there is nothing else
        // in the world it wants.
        if (context.Befriended)
            return FollowPlayer(context, dist);

        // 6/7. Carrying loot. Close and looking -> get away. Far enough -> take
        // the long run at the counter.
        if (context.Carrying)
        {
            return threatened
                ? Flee(context)
                : HeadForCounter(context);
        }

        // 7. Too close. Run.
        if (threatened)
        {
            _startleTimer = 0.8f;
            return Flee(context);
        }

        // 8. Noticed you, but no rush.
        if (context.CanSeePlayer && dist <= _profile.AlertRadius)
        {
            return Alert(context);
        }

        // 8b. Hasn't seen you, but has heard about you. Head for where you
        // were, or at least look that way. This is what makes one cat
        // spotting the player matter to the cats two aisles away.
        //
        // Awareness is a vagueness: zero would mean it knows exactly, but
        // that case is handled by CanSeePlayer above. Anything else means a
        // rough fix worth heading towards.
        if (context.LastSeenPlayerCell is { } lastSeen && context.PlayerAwareness > 0f)
        {
            if (dist > _profile.AlertRadius)
                return PathStepToward(context.Map, context.CurrentCell, lastSeen);
        }

        // 9. Nothing happening. Go and find something to steal, or wander.
        if (context.LootTargetItem is { } item && context.StolenCount < 3)
        {
            // A cat only bothers with what it likes — unless the store is
            // about to go off, in which case it takes what it can get.
            float drive = _profile.ThiefDriveFor(item.Kind);
            if (context.AlarmStage == AlarmStage.Critical)
                drive = MathF.Max(drive, _profile.ThiefDrive * 0.75f);

            if (_rng.NextDouble() < drive)
                return PathStepToward(context.Map, context.CurrentCell, item.Cell);
        }

        return Wander(context);
    }

    // Decides whether to take loot underfoot, and whether doing so means
    // standing still for a moment (a cat that spots a can is briefly busy).
    private bool ShouldTakeLoot(CritterContext context, out bool paused)
    {
        paused = false;
        if (_profile.ThiefDrive <= 0f) return false;
        if (context.LootUnderfoot is not { } underfoot) return false;

        // A friend never shoplifts — not even from under its own paws. This
        // is the whole point of feeding it: one fewer thief in the store,
        // for the price of two treats.
        if (context.Befriended) return false;

        // A wary cat (just caught) won't shoplift — it's spooked.
        if (context.Mood == Mood.Wary) return false;

        // A panicking cat (sprayed) is running, not shopping.
        if (context.Panic > 0f) return false;

        // A Burglar won't shoplift in front of you. This is what makes the
        // camera a *two-way* tool: staring down a Nervous cat pins it, and
        // staring down a Burglar stops it mid-grab.
        if (_profile.Sneaky && context.BeingWatched) return false;

        // Only what it likes. A cat that does not want tins walks over them.
        // Unless the store is about to go off, in which case it takes what it
        // can get.
        float drive = _profile.ThiefDriveFor(underfoot.Kind);
        if (context.AlarmStage == AlarmStage.Critical)
            drive = MathF.Max(drive, _profile.ThiefDrive * 0.75f);
        if (drive <= 0f) return false;

        bool inclined = _rng.NextDouble() < drive;
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
        for (int x = map.CounterStart; x <= map.CounterEnd; x++)
        {
            var cell = new GridPos(x, map.CounterRow);
            if (!map.IsWalkable(cell.X, cell.Y)) continue;
            int d = Math.Abs(cell.X - from.X) + Math.Abs(cell.Y - from.Y);
            if (d < bestDist) { bestDist = d; best = cell; }
        }
        return best;
    }

    // A friend keeps pace with the player: close enough to be company, far
    // enough not to clip through them. No quirks, no escaping, no errands —
    // the whole behaviour is "you, but a few paces behind".
    private GridPos? FollowPlayer(CritterContext context, int dist)
    {
        // Standing on (or beside) the player: give ground rather than share
        // the cell. A friend that overlaps the player reads as a bug, not
        // affection.
        if (dist <= 1)
            return StepAside(context, dist);

        // Keeping up: walk towards them.
        if (dist >= FriendFollowDistance)
            return PathStepToward(context.Map, context.CurrentCell, context.PlayerCell);

        // Companionable distance reached: amble along with them.
        return null;
    }

    // One step to a walkable neighbour strictly further from the player than
    // the cat already is (from the player's own cell, any neighbour will do).
    private GridPos? StepAside(CritterContext context, int dist)
    {
        foreach (var d in ShuffledDirections())
        {
            var candidate = new GridPos(context.CurrentCell.X + d.X, context.CurrentCell.Y + d.Y);
            if (!context.Map.IsWalkable(candidate.X, candidate.Y)) continue;
            if (ManhattanDistance(candidate, context.PlayerCell) > dist) return candidate;
        }
        return null;
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
    /// <summary>Picks a walkable cell a few aisles away to amble toward.</summary>
    private static GridPos PickPatrolTarget(GridMap map, GridPos from, Random rng)
    {
        var cells = map.WalkableCells().ToList();
        if (cells.Count == 0) return from;
        // Prefer cells at least 3 away so the cat actually moves.
        for (int tries = 0; tries < 8; tries++)
        {
            var c = cells[rng.Next(cells.Count)];
            if (Math.Abs(c.X - from.X) + Math.Abs(c.Y - from.Y) >= 3)
                return c;
        }
        return cells[rng.Next(cells.Count)];
    }

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
