using System;
using System.Collections.Generic;

namespace StoreChase
{
    // 0 = open floor, 1 = shelf/wall (blocked)
    /// <summary>
    /// The store's grid: walkability, line-of-sight, BFS pathing, and the
    /// counter row. Pure data and logic — no graphics, no game state.
    /// </summary>
    public class GridMap
    {
        /// <summary>Map width in cells.</summary>
        public int Width { get; }

        /// <summary>Map height in cells.</summary>
        public int Height { get; }

        /// <summary>World units per cell (informational — systems use their own copy).</summary>
        public int CellSize { get; }

        private readonly int[,] _cells;

        /// <summary>Creates the store map from the hardcoded layout.</summary>
        public GridMap(int cellSize = 32)
        {
            CellSize = cellSize;

            // Simple "store" layout: outer walls + aisle shelves.
            // Feel free to redraw this — 1 = shelf, 0 = walkable aisle.
            string[] layout = new[]
            {
                "111111111111111111",
                "100000000000000001",
                "101110111011101101",
                "101110111011101101",
                "100000000000000001",
                "101110111011101101",
                "101110111011101101",
                "100000000000000001",
                "101110111011101101",
                "101110111011101101",
                "100000000000000001",
                "111111111111111111",
            };

            Height = layout.Length;
            Width = layout[0].Length;
            _cells = new int[Width, Height];

            for (int y = 0; y < Height; y++)
                for (int x = 0; x < Width; x++)
                    _cells[x, y] = layout[y][x] == '1' ? 1 : 0;
        }

        /// <summary>True if the cell is open floor (not a shelf/wall and in bounds).</summary>
        public bool IsWalkable(int x, int y)
        {
            if (x < 0 || y < 0 || x >= Width || y >= Height) return false;
            return _cells[x, y] == 0;
        }

        public int this[int x, int y] => _cells[x, y];

        // The front counter: the run of cells along the top aisle that the
        // player has to reach to secure stolen goods. It is a normal walkable
        // row in the layout, so this is data rather than geometry — the same
        // cells get the counter texture drawn on them.
        /// <summary>True if the cell is part of the front counter (the player's goal).</summary>
        public bool IsCounter(int x, int y) => y == CounterRow && x >= CounterStart && x <= CounterEnd;

        public const int CounterRow = 1;
        public const int CounterStart = 6;
        public const int CounterEnd = 11;

        // How tall the counter slab is drawn, in world units. Lives here beside
        // the counter cells themselves so Game1 (which draws it) and ItemField
        // (which stacks recovered goods on top of it) can't disagree.
        public const float CounterHeight = 0.35f;

        // Straight-line visibility between two cells, ignoring whether the
        // endpoints are walkable (the player standing in an aisle can see
        // across it). Uses a Bresenham walk, so it reports the shelves that
        // actually interrupt the sightline.
        //
        // This is what the "can the cat see me" test needs: DistanceMapFrom
        // measures walking distance, which is right for pathing but wrong for
        // sight, because a cat can see you straight down an aisle it can't
        // reach you along.
        /// <summary>
        /// Straight-line visibility between two cells, ignoring whether the
        /// endpoints are walkable. Uses a Bresenham walk, so it reports the
        /// shelves that actually interrupt a sightline.
        /// </summary>
        public bool HasLineOfSight(GridPos from, GridPos to)
        {
            if (from == to) return true;

            int x = from.X, y = from.Y;
            int dx = Math.Abs(to.X - from.X), dy = Math.Abs(to.Y - from.Y);
            int sx = to.X > from.X ? 1 : -1;
            int sy = to.Y > from.Y ? 1 : -1;
            int err = dx - dy;

            // At least one step, so the origin cell is never tested against
            // itself (a cat standing in an aisle is visible from that aisle).
            while (x != to.X || y != to.Y)
            {
                int e2 = 2 * err;
                if (e2 > -dy) { err -= dy; x += sx; }
                if (e2 < dx) { err += dx; y += sy; }

                // Reached the target: the whole path was clear.
                if (x == to.X && y == to.Y) return true;

                if (!IsWalkable(x, y)) return false;
            }

            return true;
        }

        // Every walkable cell, for spawners that need to scatter something
        // around the store without hardcoding positions.
        /// <summary>Every walkable cell, for spawners that need to scatter things.</summary>
        public IEnumerable<GridPos> WalkableCells()
        {
            for (int y = 0; y < Height; y++)
                for (int x = 0; x < Width; x++)
                    if (IsWalkable(x, y)) yield return new GridPos(x, y);
        }

        // Walkable cells that touch a shelf, i.e. somewhere a shopper would
        // actually leave something. Used to spawn loot so it reads as "on the
        // shelves" rather than "in the middle of the floor".
        /// <summary>
        /// Walkable cells that touch a shelf — somewhere a shopper would
        /// actually leave something. Used to spawn loot.
        /// </summary>
        public IEnumerable<GridPos> ShelfEdgeCells()
        {
            GridPos[] offsets = { new(0, -1), new(0, 1), new(-1, 0), new(1, 0) };
            foreach (var cell in WalkableCells())
            {
                foreach (var d in offsets)
                {
                    if (!IsWalkable(cell.X + d.X, cell.Y + d.Y)) { yield return cell; break; }
                }
            }
        }

        // Breadth-first search: returns the next step from `start` toward `goal`.
        // Good enough for a store-sized grid — no need for A* here.
        /// <summary>
        /// Breadth-first search: returns the next step from <paramref name="start"/>
        /// toward <paramref name="goal"/>. Good enough for a store-sized grid.
        /// </summary>
        public GridPos? NextStepToward(GridPos start, GridPos goal)
        {
            if (start == goal) return null;

            var visited = new bool[Width, Height];
            var cameFrom = new Dictionary<GridPos, GridPos>();
            var queue = new Queue<GridPos>();

            queue.Enqueue(start);
            visited[start.X, start.Y] = true;

            GridPos[] dirs = { new(0, -1), new(0, 1), new(-1, 0), new(1, 0) };

            bool found = false;
            while (queue.Count > 0 && !found)
            {
                var current = queue.Dequeue();
                foreach (var d in dirs)
                {
                    var next = new GridPos(current.X + d.X, current.Y + d.Y);
                    if (!IsWalkable(next.X, next.Y) || visited[next.X, next.Y]) continue;

                    visited[next.X, next.Y] = true;
                    cameFrom[next] = current;

                    if (next == goal) { found = true; break; }
                    queue.Enqueue(next);
                }
            }

            if (!found) return null;

            // Walk backward from goal to start to find the first step taken.
            var step = goal;
            while (cameFrom.TryGetValue(step, out var prev) && prev != start)
                step = prev;

            return step;
        }

        // Full BFS distance map from `origin`, following walls (unlike
        // Manhattan distance, this won't think a tile is "close" if it's
        // actually on the other side of a shelf). Unreachable tiles get
        // int.MaxValue. Used by the critter AI to judge how good an escape
        // tile actually is once shelves are in the way.
        /// <summary>
        /// Full BFS distance map from <paramref name="origin"/>, following walls.
        /// Unreachable tiles get <see cref="int.MaxValue"/>. Used by the critter
        /// AI to judge how good an escape tile actually is.
        /// </summary>
        public int[,] DistanceMapFrom(GridPos origin)
        {
            var dist = new int[Width, Height];
            for (int x = 0; x < Width; x++)
                for (int y = 0; y < Height; y++)
                    dist[x, y] = int.MaxValue;

            if (!IsWalkable(origin.X, origin.Y)) return dist;

            var queue = new Queue<GridPos>();
            dist[origin.X, origin.Y] = 0;
            queue.Enqueue(origin);

            GridPos[] dirs = { new(0, -1), new(0, 1), new(-1, 0), new(1, 0) };

            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                int currentDist = dist[current.X, current.Y];

                foreach (var d in dirs)
                {
                    var next = new GridPos(current.X + d.X, current.Y + d.Y);
                    if (!IsWalkable(next.X, next.Y)) continue;
                    if (dist[next.X, next.Y] != int.MaxValue) continue;

                    dist[next.X, next.Y] = currentDist + 1;
                    queue.Enqueue(next);
                }
            }

            return dist;
        }
    }

    /// <summary>A grid cell coordinate. Value type, so comparisons are by value.</summary>
    public readonly struct GridPos
    {
        /// <summary>Column (0 = left edge of the map).</summary>
        public readonly int X, Y;

        /// <summary>Creates a grid position.</summary>
        public GridPos(int x, int y) { X = x; Y = y; }

        public static bool operator ==(GridPos a, GridPos b) => a.X == b.X && a.Y == b.Y;
        public static bool operator !=(GridPos a, GridPos b) => !(a == b);
        public override bool Equals(object? obj) => obj is GridPos p && p == this;
        public override int GetHashCode() => (X, Y).GetHashCode();
    }
}
