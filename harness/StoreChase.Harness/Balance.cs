using System;
using System.Collections.Generic;
using System.Linq;
using StoreChase;

namespace StoreChase.Harness;

/// <summary>
/// Works out how long a player needs to clear a store, and how the alarm
/// rates should scale with it.
///
/// The alarm rates were tuned by hand against one small floor plan. A bigger
/// store is strictly harder for the same rates — the player has further to
/// walk between the till and the shelves, so the meter fills before the round
/// can be cleared — which is not "harder", just broken.
///
/// The fix is to scale the rates by how much work the store actually is.
/// The player's round is a sequence of trips: counter to an item, item back
/// to the counter, once per item. So the expected clear time is
///
///     2 * itemCount * meanDistance(counter, shelfEdge) / playerSpeed
///
/// and the rates scale by the ratio of that time to the reference store's.
/// A layout that is twice as far to walk gets half the rate, so the round
/// still has the same amount of *alarm budget* in it.
///
/// This is a property of the layout, not a magic number per store, so a
/// generated floor plan gets it for free.
/// </summary>
public static class Balance
{
    /// <summary>The reference store the alarm rates were tuned against.</summary>
    public const string ReferenceLayout = "Standard";

    /// <summary>The player's grid speed, in cells per second.</summary>
    /// <remarks>
    /// Mirrors <c>Player.MoveSpeed</c>. It is a private constant in the game,
    /// so the harness cannot read it; if that ever changes, this has to change
    /// with it.
    /// </remarks>
    public const float PlayerSpeedCellsPerSecond = 4.5f;

    /// <summary>
    /// The mean walking distance from the counter to a shelf-edge cell, in
    /// cells. This is the length of a typical "go and fetch one thing" trip.
    /// </summary>
    public static float MeanFetchDistance(StoreLayout layout)
    {
        var map = new GridMap(layout);
        var dist = map.DistanceMapFrom(new GridPos(layout.CounterStart, layout.CounterRow));

        var reachable = map.ShelfEdgeCells()
            .Where(c => dist[c.X, c.Y] != int.MaxValue)
            .Select(c => (float)dist[c.X, c.Y])
            .ToList();

        if (reachable.Count == 0) return 1f;
        return reachable.Average();
    }

    /// <summary>
    /// The expected number of seconds for a player to clear the store.
    /// </summary>
    public static float ExpectedClearSeconds(StoreLayout layout, int itemCount)
    {
        float distance = MeanFetchDistance(layout);
        // Each item is a round trip: counter to the item, and back again.
        float cells = 2f * itemCount * distance;
        return cells / PlayerSpeedCellsPerSecond;
    }

    /// <summary>
    /// The factor the alarm rates should be scaled by for this store.
    ///
    /// 1.0 for the reference store; smaller for a store that takes longer to
    /// clear, so the round keeps the same amount of alarm budget.
    /// </summary>
    public static float AlarmScale(StoreLayout layout, int itemCount)
    {
        float reference = ExpectedClearSeconds(StoreLayouts.All.First(l => l.Name == ReferenceLayout), itemCount);
        float thisStore = ExpectedClearSeconds(layout, itemCount);
        if (thisStore <= 0.001f) return 1f;
        return reference / thisStore;
    }
}
