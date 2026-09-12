using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;

namespace StoreChase
{
    public class Critter
    {
        public GridPos Cell { get; private set; }
        public Vector3 WorldPos { get; private set; }
        public bool Caught { get; private set; }

        // Facing direction in radians, for orienting the cat model to face
        // its direction of travel (Y-axis rotation, since it moves on the
        // XZ plane). 0 = facing +Z; adjust ModelFacingOffset in Game1 if the
        // source model's "forward" axis doesn't match that.
        public float FacingAngle { get; private set; }

        private GridPos _from;
        private GridPos _to;
        private float _t;
        private float _moveSpeed = 3.5f;

        private readonly Random _rng = new();

        // Two-tier awareness: "alert" means it's noticed you and starts
        // drifting away casually; "flee" means it commits to an actual
        // escape route. Alert kicking in earlier than flee is what makes it
        // feel like it's reacting to you approaching, not just to contact.
        private const int AlertRadius = 8;
        private const int FleeRadius = 4;

        // How often (in seconds) the critter re-plans its escape route while
        // fleeing. Re-planning too often makes it twitchy; too rarely makes
        // it commit to a bad route once the player changes direction.
        private const float ReplanInterval = 0.6f;
        private float _replanTimer = 0f;
        private List<GridPos> _currentPath = new();

        // Chance per decision to do something other than the "optimal"
        // escape move — a sidestep or a brief double-back — so the critter
        // doesn't read as a perfectly optimal, robotic pathfinder.
        private const float QuirkChance = 0.2f;
        private GridPos? _lastCell; // one-tile memory, used for the "double back" quirk

        public Critter(GridPos start, float cellSize)
        {
            Cell = start;
            _from = start;
            _to = start;
            WorldPos = new Vector3(start.X * cellSize, 0f, start.Y * cellSize);
        }

        public void Update(GameTime gameTime, GridMap map, float cellSize, GridPos playerCell)
        {
            if (Caught) return;

            if (Cell == playerCell)
            {
                Caught = true;
                return;
            }

            float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;
            _replanTimer -= dt;

            bool moving = _t < 1f;

            if (!moving)
            {
                _lastCell = Cell;
                Cell = _to;
                if (Cell == playerCell) { Caught = true; return; }

                var next = ChooseNextCell(map, playerCell);
                if (next.HasValue)
                {
                    _from = Cell;
                    _to = next.Value;
                    _t = 0f;
                    moving = true;
                }
            }

            if (moving)
            {
                _t += dt * _moveSpeed;
                if (_t > 1f) _t = 1f;

                Vector3 fromPos = new(_from.X * cellSize, 0f, _from.Y * cellSize);
                Vector3 toPos = new(_to.X * cellSize, 0f, _to.Y * cellSize);
                WorldPos = Vector3.Lerp(fromPos, toPos, _t);

                // Face the direction of travel. Only update when actually
                // moving somewhere (toPos != fromPos) so it doesn't snap to
                // some default angle when idle between moves.
                Vector3 delta = toPos - fromPos;
                if (delta.LengthSquared() > 0.0001f)
                    FacingAngle = (float)Math.Atan2(delta.X, delta.Z);

                if (_t >= 1f)
                {
                    Cell = _to;
                    if (Cell == playerCell) Caught = true;
                }
            }
        }

        private GridPos? ChooseNextCell(GridMap map, GridPos playerCell)
        {
            int dist = ManhattanDistance(Cell, playerCell);

            if (dist <= FleeRadius)
                return ChooseFleeMove(map, playerCell);

            if (dist <= AlertRadius)
                return ChooseAlertMove(map, playerCell);

            // Not aware of the player at all: same relaxed random wander as before.
            return ChooseWanderMove(map);
        }

        // Full commitment escape: plan a path to a genuinely distant, safe
        // tile using real (wall-aware) distance, and follow it — instead of
        // greedily picking whichever adjacent tile looks best one step at a
        // time, which is what let it get walked into corners before.
        private GridPos? ChooseFleeMove(GridMap map, GridPos playerCell)
        {
            bool needsNewPlan = _currentPath.Count == 0 || _replanTimer <= 0f;

            if (needsNewPlan)
            {
                _replanTimer = ReplanInterval;
                _currentPath = PlanEscapeRoute(map, playerCell);
            }

            // Occasionally ignore the plan and do something quirky instead —
            // a real animal doesn't always take the optimal escape line.
            if (_rng.NextDouble() < QuirkChance)
            {
                var quirkMove = ChooseQuirkMove(map, playerCell);
                if (quirkMove.HasValue)
                {
                    _currentPath.Clear(); // abandon the plan, re-plan next tick
                    return quirkMove;
                }
            }

            if (_currentPath.Count > 0)
            {
                var next = _currentPath[0];
                _currentPath.RemoveAt(0);
                if (map.IsWalkable(next.X, next.Y)) return next;
            }

            // Plan exhausted or invalidated — fall back to a single greedy
            // step away from the player so it's never just stuck.
            return GreedyStepAway(map, playerCell);
        }

        // Looks a handful of tiles ahead in every direction, scores each
        // reachable candidate by its wall-aware distance from the player,
        // and paths toward the best one. This is what lets the critter find
        // "go around this aisle to get further away" instead of just
        // "step into the nearest open square."
        private List<GridPos> PlanEscapeRoute(GridMap map, GridPos playerCell)
        {
            var distFromPlayer = map.DistanceMapFrom(playerCell);
            var distFromSelf = map.DistanceMapFrom(Cell);

            GridPos? bestTarget = null;
            int bestScore = -1;

            for (int y = 0; y < map.Height; y++)
            {
                for (int x = 0; x < map.Width; x++)
                {
                    if (!map.IsWalkable(x, y)) continue;
                    int reachDist = distFromSelf[x, y];
                    if (reachDist == int.MaxValue || reachDist == 0) continue;
                    if (reachDist > 6) continue; // don't plan absurdly long routes

                    int safetyDist = distFromPlayer[x, y];
                    if (safetyDist == int.MaxValue) continue;

                    // Prefer tiles that are far from the player but not an
                    // excessive detour to reach — otherwise it "sees" a
                    // faraway corner and beelines for it in a straight line
                    // that happens to pass near the player.
                    int score = safetyDist - reachDist / 2;
                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestTarget = new GridPos(x, y);
                    }
                }
            }

            if (!bestTarget.HasValue) return new List<GridPos>();

            return BuildPathTo(map, Cell, bestTarget.Value);
        }

        // Reconstructs a full step-by-step path using repeated single-step
        // BFS lookups (GridMap already exposes NextStepToward for this).
        private List<GridPos> BuildPathTo(GridMap map, GridPos from, GridPos to)
        {
            var path = new List<GridPos>();
            var current = from;
            int safety = 64; // guard against any pathfinding edge case looping

            while (current != to && safety-- > 0)
            {
                var next = map.NextStepToward(current, to);
                if (!next.HasValue) break;
                path.Add(next.Value);
                current = next.Value;
            }

            return path;
        }

        // Lighter-weight reaction once the player is noticed but not yet
        // close enough to trigger full flee: drift toward whichever
        // neighboring tile increases distance from the player, without
        // committing to a multi-step plan. This is what makes the critter
        // feel like it "notices" you approaching well before you're on it.
        private GridPos? ChooseAlertMove(GridMap map, GridPos playerCell)
        {
            var options = ShuffledDirections();
            GridPos? fallback = null;

            foreach (var d in options)
            {
                var candidate = new GridPos(Cell.X + d.X, Cell.Y + d.Y);
                if (!map.IsWalkable(candidate.X, candidate.Y)) continue;

                fallback ??= candidate;

                int candidateDist = ManhattanDistance(candidate, playerCell);
                int currentDist = ManhattanDistance(Cell, playerCell);
                if (candidateDist >= currentDist) return candidate;
            }

            return fallback;
        }

        private GridPos? ChooseWanderMove(GridMap map)
        {
            foreach (var d in ShuffledDirections())
            {
                var candidate = new GridPos(Cell.X + d.X, Cell.Y + d.Y);
                if (map.IsWalkable(candidate.X, candidate.Y)) return candidate;
            }
            return null;
        }

        private GridPos? GreedyStepAway(GridMap map, GridPos playerCell)
        {
            GridPos[] dirs = { new(0, -1), new(0, 1), new(-1, 0), new(1, 0) };
            GridPos? best = null;
            int bestDist = -1;

            foreach (var d in dirs)
            {
                var candidate = new GridPos(Cell.X + d.X, Cell.Y + d.Y);
                if (!map.IsWalkable(candidate.X, candidate.Y)) continue;

                int candDist = ManhattanDistance(candidate, playerCell);
                if (candDist > bestDist)
                {
                    bestDist = candDist;
                    best = candidate;
                }
            }
            return best;
        }

        // The "personality" quirks: either a sideways dodge (perpendicular
        // to the direct line away from the player) or, if there's a
        // recently-visited tile still available, doubling back through it.
        // Both are deliberately suboptimal — that's the point.
        private GridPos? ChooseQuirkMove(GridMap map, GridPos playerCell)
        {
            bool doubleBack = _lastCell.HasValue && _rng.NextDouble() < 0.4;
            if (doubleBack && map.IsWalkable(_lastCell!.Value.X, _lastCell.Value.Y))
                return _lastCell;

            // Sidestep: pick a walkable neighbor that's roughly perpendicular
            // to the straight-line direction away from the player, rather
            // than directly away from or toward them.
            int dx = Cell.X - playerCell.X;
            int dy = Cell.Y - playerCell.Y;

            GridPos[] perpendicular = Math.Abs(dx) > Math.Abs(dy)
                ? new[] { new GridPos(Cell.X, Cell.Y - 1), new GridPos(Cell.X, Cell.Y + 1) }
                : new[] { new GridPos(Cell.X - 1, Cell.Y), new GridPos(Cell.X + 1, Cell.Y) };

            var walkable = perpendicular.Where(p => map.IsWalkable(p.X, p.Y)).ToList();
            if (walkable.Count > 0) return walkable[_rng.Next(walkable.Count)];

            return null;
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

        private static int ManhattanDistance(GridPos a, GridPos b) =>
            Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y);
    }
}
