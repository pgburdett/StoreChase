using System;
using System.Collections.Generic;
using System.Linq;

namespace StoreChase;

public class SmartAnimalAI : ICritterAI
{
    private readonly Random _rng = new();
    private List<GridPos> _currentPath = new();
    private float _replanTimer = 0f;
    private GridPos? _lastCell;

    // Tuning parameters
    public float ReplanInterval { get; set; } = 0.6f;
    public int AlertRadius { get; set; } = 8;
    public int FleeRadius { get; set; } = 4;
    public float QuirkChance { get; set; } = 0.2f;

    public GridPos? DecideNextMove(CritterContext context)
    {
        GridPos? result;

        // If we are currently following a planned path, just take the next step
        if (_currentPath.Count > 0)
        {
            var next = _currentPath[0];
            _currentPath.RemoveAt(0);
            result = next;
        }
        else
        {
            _replanTimer -= context.DeltaTime;
            int dist = ManhattanDistance(context.CurrentCell, context.PlayerCell);

            if (dist <= FleeRadius)
                result = ExecuteFleeLogic(context);
            else if (dist <= AlertRadius)
                result = ExecuteAlertLogic(context);
            else
                result = ExecuteWanderLogic(context);
        }

        // Remember the cell the critter is about to leave, so the next
        // decision can "double back" to it when a quirk fires.
        _lastCell = context.CurrentCell;
        return result;
    }

    private GridPos? ExecuteFleeLogic(CritterContext context)
    {
        if (_replanTimer <= 0f || _currentPath.Count == 0)
        {
            _replanTimer = ReplanInterval;
            _currentPath = PlanEscapeRoute(context);
        }

        if (_rng.NextDouble() < QuirkChance)
        {
            var quirkMove = ChooseQuirkMove(context);
            if (quirkMove.HasValue)
            {
                _currentPath.Clear();
                return quirkMove;
            }
        }

        return null; 
    }

    private List<GridPos> PlanEscapeRoute(CritterContext context)
    {
        var distFromPlayer = context.Map.DistanceMapFrom(context.PlayerCell);
        var distFromSelf = context.Map.DistanceMapFrom(context.CurrentCell);

        GridPos? bestTarget = null;
        int bestScore = -1;

        for (int y = 0; y < context.Map.Height; y++)
        {
            for (int x = 0; x < context.Map.Width; x++)
            {
                if (!context.Map.IsWalkable(x, y)) continue;
                
                int reachDist = distFromSelf[x, y];
                if (reachDist == int.MaxValue || reachDist > 6) continue;

                int safetyDist = distFromPlayer[x, y];
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

    private GridPos? ExecuteAlertLogic(CritterContext context)
    {
        var options = GetShuffledDirections();
        GridPos? fallback = null;

        foreach (var d in options)
        {
            var candidate = new GridPos(context.CurrentCell.X + d.X, context.CurrentCell.Y + d.Y);
            if (!context.Map.IsWalkable(candidate.X, candidate.Y)) continue;

            fallback ??= candidate;
            if (ManhattanDistance(candidate, context.PlayerCell) >= ManhattanDistance(context.CurrentCell, context.PlayerCell))
                return candidate;
        }
        return fallback;
    }

    private GridPos? ExecuteWanderLogic(CritterContext context)
    {
        foreach (var d in GetShuffledDirections())
        {
            var candidate = new GridPos(context.CurrentCell.X + d.X, context.CurrentCell.Y + d.Y);
            if (context.Map.IsWalkable(candidate.X, candidate.Y)) return candidate;
        }
        return null;
    }

    private GridPos? ChooseQuirkMove(CritterContext context)
    {
        // Half the time make a short lateral juke perpendicular to the line
        // to the player; otherwise double back to the cell we just left.
        if (_rng.NextDouble() < 0.5f)
        {
            var sidestep = TrySidestep(context);
            if (sidestep.HasValue) return sidestep;
        }

        if (_lastCell.HasValue)
        {
            var last = _lastCell.Value;
            if (context.Map.IsWalkable(last.X, last.Y) &&
                ManhattanDistance(last, context.CurrentCell) == 1)
                return last;
        }
        return null;
    }

    // A lateral move: the direction to the player defines an axis, so the
    // perpendicular axis is a side-step. Handles both axis-aligned and
    // diagonal player bearings.
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

    private GridPos[] GetShuffledDirections()
    {
        GridPos[] options = { new(0, -1), new(0, 1), new(-1, 0), new(1, 0) };
        for (int i = 3; i > 0; i--)
        {
            int j = _rng.Next(i + 1);
            (options[i], options[j]) = (options[j], options[i]);
        }
        return options;
    }

    private static int ManhattanDistance(GridPos a, GridPos b) => Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y);
}
