using System;
using System.Collections.Generic;
using System.Linq;

namespace StoreChase;

/// <summary>
/// Pure decision helper for <see cref="Customer"/>s. The game calls
/// <see cref="Decide"/> each frame; it only sets movement targets, never
/// mutates possession (that is the game's job, mirroring the critter split).
/// </summary>
public static class CustomerBrain
{
    public static void Decide(Customer c, GridMap map, GridPos playerCell, ItemField items,
        Random rng, IReadOnlyList<GridPos> exits)
    {
        if (c.Escaped) return;
        if (c.IsMoving) return;

        if (c.Leaving)
        {
            c.SetTarget(FarFrom(c.Cell, playerCell, map), map);
            return;
        }

        if (c.IsShoplifter && c.Carrying != null)
        {
            // Make for the nearest exit.
            if (exits.Count > 0)
                c.SetTarget(Nearest(c.Cell, exits), map);
            return;
        }

        if (c.IsShoplifter && c.Carrying == null)
        {
            var item = items.NearestLoose(c.Cell);
            // Only steal when the player isn't close enough to notice.
            if (item != null && Manhattan(c.Cell, playerCell) > 4)
            {
                c.SetTarget(item.Cell, map);
                return;
            }
        }

        // Browse: occasionally pick a new aisle to wander down.
        if (rng.NextDouble() < 0.03)
            c.SetTarget(RandomWalkable(map, rng), map);
    }

    private static GridPos RandomWalkable(GridMap map, Random rng)
    {
        var cells = map.WalkableCells().ToList();
        if (cells.Count == 0) return new GridPos(0, 0);
        return cells[rng.Next(cells.Count)];
    }

    private static GridPos Nearest(GridPos from, IReadOnlyList<GridPos> cells)
        => cells.OrderBy(c => Manhattan(from, c)).First();

    private static GridPos FarFrom(GridPos from, GridPos away, GridMap map)
    {
        GridPos best = from;
        int bestGap = -1;
        foreach (var d in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
        {
            var cand = new GridPos(from.X + d.Item1, from.Y + d.Item2);
            if (!map.IsWalkable(cand.X, cand.Y)) continue;
            int gap = Manhattan(cand, away);
            if (gap > bestGap) { bestGap = gap; best = cand; }
        }
        return best;
    }

    private static int Manhattan(GridPos a, GridPos b) =>
        Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y);
}
