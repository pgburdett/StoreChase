using System;
using System.Collections.Generic;

namespace StoreChase;

// A store's floor plan: the grid of cells plus where the counter runs.
//
// The grid used to live inside GridMap as a hardcoded string array, which made
// "one store" the only possible store. Splitting the *data* out from the
// *logic* means a layout can be authored, generated or loaded, and GridMap
// stays a pure function of whatever layout it is handed.
//
// Row/col characters follow the original convention:
//   '1' = shelf or wall (blocked)
//   '0' = open floor (walkable)
public sealed class StoreLayout
{
    /// <summary>The layout's name, shown in logs and the HUD.</summary>
    public string Name { get; }

    /// <summary>The rows of the floor plan, top row first. '1' is blocked.</summary>
    public IReadOnlyList<string> Rows { get; }

    /// <summary>The row the counter runs along.</summary>
    public int CounterRow { get; }

    /// <summary>The first column of the counter.</summary>
    public int CounterStart { get; }

    /// <summary>The last column of the counter (inclusive).</summary>
    public int CounterEnd { get; }

    /// <summary>Where the player starts the round.</summary>
    public GridPos PlayerStart { get; }

    /// <summary>Width in cells.</summary>
    public int Width => Rows.Count > 0 ? Rows[0].Length : 0;

    /// <summary>Height in cells.</summary>
    public int Height => Rows.Count;

    /// <summary>Creates a layout from rows, a counter span and a player start.</summary>
    public StoreLayout(
        string name,
        IReadOnlyList<string> rows,
        int counterRow,
        int counterStart,
        int counterEnd,
        GridPos playerStart)
    {
        if (rows == null) throw new ArgumentNullException(nameof(rows));
        if (rows.Count == 0) throw new ArgumentException("A layout needs at least one row.", nameof(rows));

        int width = rows[0].Length;
        for (int i = 0; i < rows.Count; i++)
        {
            if (rows[i].Length != width)
                throw new ArgumentException(
                    $"Layout '{name}' row {i} is {rows[i].Length} wide, expected {width}.", nameof(rows));
        }

        if (counterRow < 0 || counterRow >= rows.Count)
            throw new ArgumentException($"Counter row {counterRow} is outside the layout.", nameof(counterRow));
        if (counterStart < 0 || counterEnd >= width || counterStart > counterEnd)
            throw new ArgumentException(
                $"Counter span {counterStart}..{counterEnd} is outside the layout's width {width}.", nameof(counterStart));
        if (playerStart.X < 0 || playerStart.Y < 0 || playerStart.X >= width || playerStart.Y >= rows.Count)
            throw new ArgumentException(
                $"Player start {playerStart} is outside the layout.", nameof(playerStart));

        Name = name;
        Rows = rows;
        CounterRow = counterRow;
        CounterStart = counterStart;
        CounterEnd = counterEnd;
        PlayerStart = playerStart;
    }

    /// <summary>True if the given cell is part of the counter.</summary>
    public bool IsCounter(int x, int y) =>
        y == CounterRow && x >= CounterStart && x <= CounterEnd;

    /// <summary>True if the given character means "blocked".</summary>
    public static bool IsBlocked(char c) => c != '0';

    /// <summary>
    /// How much alarm budget this store needs, relative to the reference one.
    ///
    /// The alarm rates are per-second, and a round's difficulty comes from
    /// how long the player has to clear the store. A bigger store is further
    /// to walk, so at the same rates the meter fills before the round can
    /// finish — the round becomes unwinnable rather than harder.
    ///
    /// A player's round is a sequence of trips: from the counter to an item
    /// and back again, once per item. So the expected clear time is
    ///
    ///     2 * itemCount * meanDistance(counter, shelfEdge) / playerSpeed
    ///
    /// and the rates scale by the ratio of that time to the reference store's.
    /// A store that is twice as far to walk gets half the rate, so the round
    /// keeps the same amount of alarm budget in it.
    ///
    /// This is a property of the geometry, so a generated floor plan gets it
    /// for free rather than needing its own hand-tuned constant.
    /// </summary>
    /// <param name="itemCount">How many items a round has.</param>
    /// <param name="playerSpeedCellsPerSecond">
    /// The player's grid speed. Mirrors <c>Player.MoveSpeed</c>.
    /// </param>
    public float AlarmScale(int itemCount, float playerSpeedCellsPerSecond)
    {
        float reference = StoreLayouts.ReferenceClearSeconds(itemCount, playerSpeedCellsPerSecond);
        float thisStore = EstimatedClearSeconds(itemCount, playerSpeedCellsPerSecond);
        if (thisStore <= 0.001f) return 1f;
        return reference / thisStore;
    }

    /// <summary>
    /// The expected number of seconds for a player to clear this store.
    /// </summary>
    public float EstimatedClearSeconds(int itemCount, float playerSpeedCellsPerSecond)
    {
        float distance = MeanFetchDistance();
        float cells = 2f * itemCount * distance;
        return cells / playerSpeedCellsPerSecond;
    }

    /// <summary>
    /// The mean walking distance from the counter to a shelf-edge cell, in
    /// cells. This is the length of a typical "go and fetch one thing" trip.
    /// </summary>
    public float MeanFetchDistance()
    {
        var map = new GridMap(this);
        var dist = map.DistanceMapFrom(new GridPos(CounterStart, CounterRow));

        var reachable = new List<float>();
        foreach (var c in map.ShelfEdgeCells())
            if (dist[c.X, c.Y] != int.MaxValue)
                reachable.Add(dist[c.X, c.Y]);

        if (reachable.Count == 0) return 1f;

        float total = 0f;
        foreach (float d in reachable) total += d;
        return total / reachable.Count;
    }

    public override string ToString() => $"{Name} ({Width}x{Height})";
}

// The library of floor plans.
//
// The authored layouts are hand-written and carry the store's character. The
// generated one exists because a store bigger than the screen is the whole
// point of the scrolling camera: hand-authoring a 30x20 floor plan as ASCII
// art is miserable to edit and impossible to vary per run.
//
// The generator carves a grid of shelf blocks out of open floor. It only ever
// *removes* obstacles, never adds them, so the aisle lattice stays connected
// by construction — a maze whose connectivity has to be checked is a maze that
// will eventually generate an unreachable shelf.
public static class StoreLayouts
{
    /// <summary>
    /// The original store: 18x12, four shelf rows, counter across the top.
    /// Small enough to see in one screen at a wide zoom.
    /// </summary>
    public static StoreLayout Standard { get; } = new(
        "Standard",
        new[]
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
        },
        counterRow: 1,
        counterStart: 6,
        counterEnd: 11,
        playerStart: new GridPos(4, 1));

    /// <summary>
    /// A big store that does not fit on the screen: 26x18, with open plazas
    /// and a couple of alcoves so it doesn't read as a repeating pattern.
    /// </summary>
    /// <remarks>
    /// The size is a balance. The viewport is 16 cells across, so anything
    /// wider than that needs the camera to scroll, but a store much bigger
    /// than that puts the cats so far away that they are never visible and
    /// the round turns into a walk. At 26x18 the store is about one and a
    /// half screens each way, which is enough for the aisles to feel like a
    /// maze and still enough of the shop floor to be on screen at once.
    /// </remarks>
    public static StoreLayout BigBox { get; } = Generate("Big Box", 26, 18, seed: 20260930);

    /// <summary>Every authored/generated layout, for a level picker.</summary>
    public static IReadOnlyList<StoreLayout> All { get; } = new[] { Standard, BigBox };

    /// <summary>The layout the alarm rates were tuned against.</summary>
    public const string ReferenceLayoutName = "Standard";

    /// <summary>
    /// The expected clear time of the reference store, for scaling a layout's
    /// alarm rates against it.
    /// </summary>
    public static float ReferenceClearSeconds(int itemCount, float playerSpeedCellsPerSecond)
    {
        foreach (var layout in All)
            if (layout.Name == ReferenceLayoutName)
                return layout.EstimatedClearSeconds(itemCount, playerSpeedCellsPerSecond);

        return 1f;
    }

    /// <summary>
    /// Builds a store by carving shelf blocks out of open floor.
    ///
    /// The shape is a lattice: vertical aisles every <see cref="aislePitchX"/>
    /// columns and horizontal aisles every <see cref="aislePitchY"/> rows, so
    /// the shelf blocks that remain are always reachable. On top of that, a
    /// seeded pass knocks holes in the blocks (plazas) and pushes some blocks
    /// along an aisle (alcoves) so the plan doesn't read as wallpaper.
    /// </summary>
    /// <param name="name">Name for the layout.</param>
    /// <param name="width">Width in cells. Must be odd-ish for tidy blocks.</param>
    /// <param name="height">Height in cells.</param>
    /// <param name="seed">Seed for the plaza/alcove pass.</param>
    public static StoreLayout Generate(string name, int width, int height, int seed)
    {
        const int aislePitchX = 4;
        const int aislePitchY = 3;

        // Counter along row 1, centred, with a walkable cell to its left so
        // the player has a starting position that isn't on the till.
        int counterStart = 4 + (width - 8) / 2;
        int counterEnd = counterStart + 5;
        if (counterEnd > width - 3) { counterStart = width - 9; counterEnd = counterStart + 5; }

        var rng = new Random(seed);
        var grid = new bool[height, width]; // true = blocked
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
                grid[y, x] = false;

        // Outer walls.
        for (int x = 0; x < width; x++) { grid[0, x] = true; grid[height - 1, x] = true; }
        for (int y = 0; y < height; y++) { grid[y, 0] = true; grid[y, width - 1] = true; }

        // Shelf blocks: everything that is not on an aisle lattice line.
        for (int y = 2; y < height - 1; y++)
            for (int x = 1; x < width - 1; x++)
            {
                bool onVerticalAisle = (x % aislePitchX) == 1;
                bool onHorizontalAisle = ((y - 1) % aislePitchY) == 0;
                if (!onVerticalAisle && !onHorizontalAisle) grid[y, x] = true;
            }

        // Plazas: knock out a rectangular patch of shelves to make an open
        // area. Only ever removes blocked cells, so connectivity is preserved.
        int plazas = Math.Max(2, (width * height) / 180);
        for (int i = 0; i < plazas; i++)
        {
            int pw = rng.Next(3, 6);
            int ph = rng.Next(2, 4);
            int px = rng.Next(2, Math.Max(3, width - pw - 2));
            int py = rng.Next(2, Math.Max(3, height - ph - 2));
            for (int y = py; y < Math.Min(height - 1, py + ph); y++)
                for (int x = px; x < Math.Min(width - 1, px + pw); x++)
                    grid[y, x] = false;
        }

        // Alcoves: extend a shelf block sideways into an aisle, but never
        // through an aisle. Cutting an aisle in half would still leave a
        // one-cell gap to walk around, so this stays safe.
        int alcoves = Math.Max(2, (width * height) / 150);
        for (int i = 0; i < alcoves; i++)
        {
            int ax = rng.Next(2, width - 3);
            int ay = rng.Next(2, height - 2);
            bool horizontal = rng.Next(2) == 0;
            for (int k = 1; k <= rng.Next(1, 2); k++)
            {
                int x = horizontal ? ax + k : ax;
                int y = horizontal ? ay : ay + k;
                if (x <= 0 || y <= 0 || x >= width - 1 || y >= height - 1) break;
                grid[y, x] = true;
            }
        }

        // The counter row is always open floor, and so is the row above it.
        for (int x = 1; x < width - 1; x++) grid[1, x] = false;

        var rows = new string[height];
        for (int y = 0; y < height; y++)
        {
            var chars = new char[width];
            for (int x = 0; x < width; x++) chars[x] = grid[y, x] ? '1' : '0';
            rows[y] = new string(chars);
        }

        // The player starts at the left end of the counter row, next to the
        // till but not on it — the "entrance" side of the shop floor.
        //
        // Next to the counter rather than in a corner matters now the store is
        // bigger than the screen: the camera clamps its view to the store's
        // edges, so a player who spawns in a corner is pinned to the edge of
        // the screen (and under the HUD) instead of being centred on it.
        var playerStart = new GridPos(Math.Max(1, counterStart - 2), 1);

        return new StoreLayout(name, rows, counterRow: 1, counterStart, counterEnd, playerStart);
    }
}
