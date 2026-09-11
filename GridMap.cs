using System;
using System.Collections.Generic;

namespace StoreChase
{
    // 0 = open floor, 1 = shelf/wall (blocked)
    public class GridMap
    {
        public int Width { get; }
        public int Height { get; }
        public int CellSize { get; }
        private readonly int[,] _cells;

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

        public bool IsWalkable(int x, int y)
        {
            if (x < 0 || y < 0 || x >= Width || y >= Height) return false;
            return _cells[x, y] == 0;
        }

        public int this[int x, int y] => _cells[x, y];

        // Breadth-first search: returns the next step from `start` toward `goal`.
        // Good enough for a store-sized grid — no need for A* here.
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
    }

    public readonly struct GridPos
    {
        public readonly int X, Y;
        public GridPos(int x, int y) { X = x; Y = y; }

        public static bool operator ==(GridPos a, GridPos b) => a.X == b.X && a.Y == b.Y;
        public static bool operator !=(GridPos a, GridPos b) => !(a == b);
        public override bool Equals(object? obj) => obj is GridPos p && p == this;
        public override int GetHashCode() => (X, Y).GetHashCode();
    }
}
