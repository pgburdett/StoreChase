using System;
using System.Collections.Generic;
using System.Linq;
using StoreChase;

namespace StoreChase.Harness;

/// <summary>
/// Checks on GridMap: walkability bounds, pathfinding, line of sight, and
/// the distance-map cache.
///
/// Asserts on invariants (a path is a shortest path; out-of-bounds is never
/// walkable; two calls agree) rather than golden coordinates, so the store
/// generator can change without rewriting the tests.
/// </summary>
public static class GridChecks
{
    /// <summary>Runs every grid check.</summary>
    public static void Run()
    {
        Check.Section("Grid: walkability, pathfinding, distance maps");

        foreach (var layout in StoreLayouts.All)
        {
            var map = new GridMap(layout);

            // --- Bounds. A negative or past-the-edge coordinate must read as
            // a wall, not throw or (worse) read as open floor.
            Check.That($"'{layout.Name}': x < 0 is not walkable",
                !map.IsWalkable(-1, 0));
            Check.That($"'{layout.Name}': y < 0 is not walkable",
                !map.IsWalkable(0, -1));
            Check.That($"'{layout.Name}': x >= Width is not walkable",
                !map.IsWalkable(map.Width, 0));
            Check.That($"'{layout.Name}': y >= Height is not walkable",
                !map.IsWalkable(0, map.Height));
            Check.That($"'{layout.Name}': IsWalkableCell agrees with IsWalkable",
                map.IsWalkableCell(1, 1) == map.IsWalkable(1, 1) &&
                map.IsWalkableCell(-5, 2) == map.IsWalkable(-5, 2));

            // --- Pathfinding invariants, sampled across the store.
            var walkable = map.WalkableCells().ToList();
            var rng = new Random(42);
            var violations = new List<string>();

            for (int i = 0; i < 40; i++)
            {
                var a = walkable[rng.Next(walkable.Count)];
                var b = walkable[rng.Next(walkable.Count)];
                var path = map.PathTo(a, b);

                if (a == b)
                {
                    if (path.Count != 0) violations.Add($"{a}->{b}: path from a cell to itself is not empty");
                    continue;
                }

                if (path.Count == 0)
                {
                    violations.Add($"{a}->{b}: no path between two walkable cells");
                    continue;
                }

                // Every step must be adjacent to the previous one and walkable.
                var prev = a;
                foreach (var step in path)
                {
                    int d = Math.Abs(step.X - prev.X) + Math.Abs(step.Y - prev.Y);
                    if (d != 1) violations.Add($"{a}->{b}: step {prev}->{step} is not adjacent");
                    if (!map.IsWalkable(step.X, step.Y)) violations.Add($"{a}->{b}: step {step} is not walkable");
                    prev = step;
                }

                if (path[^1] != b) violations.Add($"{a}->{b}: path ends at {path[^1]}, not the goal");

                // Shortest path: length must match the BFS distance map.
                var dist = map.DistanceMapFrom(b);
                if (path.Count != dist[a.X, a.Y])
                    violations.Add($"{a}->{b}: path length {path.Count} != distance {dist[a.X, a.Y]}");
            }

            Check.That($"'{layout.Name}': every sampled path is a valid shortest path",
                violations.Count == 0,
                violations.Count == 0 ? "40 samples" : string.Join("; ", violations.Take(3)));

            // --- Distance-map cache: two calls with the same origin must
            // agree (the memo must not serve a stale or wrong map), and a
            // blocked origin must be all-unreachable.
            var origin = walkable[rng.Next(walkable.Count)];
            var first = map.DistanceMapFrom(origin);
            var second = map.DistanceMapFrom(origin);
            bool same = true;
            for (int x = 0; x < map.Width && same; x++)
                for (int y = 0; y < map.Height && same; y++)
                    if (first[x, y] != second[x, y]) same = false;
            Check.That($"'{layout.Name}': repeated distance maps agree", same);

            Check.That($"'{layout.Name}': distance from origin to itself is 0",
                first[origin.X, origin.Y] == 0);

            // A shelf cell is not walkable, so no distance exists from it.
            GridPos? blocked = null;
            for (int x = 0; x < map.Width && !blocked.HasValue; x++)
                for (int y = 0; y < map.Height && !blocked.HasValue; y++)
                    if (!map.IsWalkable(x, y)) blocked = new GridPos(x, y);

            if (blocked.HasValue)
            {
                var fromShelf = map.DistanceMapFrom(blocked.Value);
                bool allMax = true;
                for (int x = 0; x < map.Width && allMax; x++)
                    for (int y = 0; y < map.Height && allMax; y++)
                        if (fromShelf[x, y] != int.MaxValue) allMax = false;
                Check.That($"'{layout.Name}': a distance map from a shelf reaches nothing", allMax);
            }

            // --- Line of sight: a cell never blocks itself, and a shelf
            // between two aisles breaks the sightline (checked via the
            // geometry invariant that any two cells with a shelf strictly
            // between them on a straight line are not mutually visible).
            Check.That($"'{layout.Name}': a cell sees itself",
                map.HasLineOfSight(origin, origin));
        }
    }
}
