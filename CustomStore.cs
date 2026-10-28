using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace StoreChase;

/// <summary>
/// A player-authored floor plan, read from <c>custom_store.txt</c> in the
/// working directory.
///
/// <para>The format is a small header, a <c>---</c> separator, then the grid
/// exactly as <see cref="StoreLayout"/> takes it ('1' blocked, '0' floor):</para>
///
/// <code>
/// Name: Corner Shop
/// counterRow: 1
/// counterStart: 6
/// counterEnd: 9
/// playerStart: 4,1
/// ---
/// 1111111111111111
/// 1000000000000001
/// </code>
///
/// <para>Generated layouts are connected by construction (see
/// <see cref="StoreLayouts"/>), which an authored file is not, so this
/// validates what the generator guarantees: a walkable player start, a counter
/// the player can actually reach, and no pocket of floor cut off from the
/// start — a shelf the player cannot walk to is an item nobody can secure.</para>
///
/// <para>Failure is reported, never swallowed: a rejected file logs its reason
/// rather than silently disabling the feature, because "why doesn't the HUD
/// offer my store?" is unanswerable from the outside.</para>
/// </summary>
public static class CustomStore
{
    /// <summary>Where the game looks for a custom store, relative to the cwd.</summary>
    public const string FileName = "custom_store.txt";

    /// <summary>Reads and parses <paramref name="path"/>, or explains why not.</summary>
    public static StoreLayout? Load(string path, out string? error)
    {
        error = null;
        try
        {
            if (!File.Exists(path)) { error = "not found"; return null; }
            return Parse(File.ReadAllText(path), out error);
        }
        catch (Exception e)
        {
            error = $"could not read '{path}': {e.Message}";
            return null;
        }
    }

    /// <summary>Parses the file's text. Returns null with a reason on failure.</summary>
    public static StoreLayout? Parse(string text, out string? error)
    {
        error = null;

        string name = "Custom Store";
        int counterRow = 1, counterStart = 0, counterEnd = 0;
        int startX = 1, startY = 1;
        var grid = new List<string>();
        bool inGrid = false;
        bool sawSeparator = false;

        foreach (var raw in text.Split('\n'))
        {
            string line = raw.TrimEnd().TrimStart('\r');
            if (line.Trim() == "---")
            {
                inGrid = true;
                sawSeparator = true;
                continue;
            }
            if (inGrid)
            {
                if (line.Length > 0) grid.Add(line);
                continue;
            }

            string trimmed = line.Trim();
            if (trimmed.Length == 0) continue;
            if (trimmed.StartsWith("Name:", StringComparison.OrdinalIgnoreCase))
                name = trimmed.Substring(5).Trim();
            else if (trimmed.StartsWith("counterRow:", StringComparison.OrdinalIgnoreCase)
                     && int.TryParse(trimmed.Substring(11).Trim(), out int row)) counterRow = row;
            else if (trimmed.StartsWith("counterStart:", StringComparison.OrdinalIgnoreCase)
                     && int.TryParse(trimmed.Substring(13).Trim(), out int cs)) counterStart = cs;
            else if (trimmed.StartsWith("counterEnd:", StringComparison.OrdinalIgnoreCase)
                     && int.TryParse(trimmed.Substring(11).Trim(), out int ce)) counterEnd = ce;
            else if (trimmed.StartsWith("playerStart:", StringComparison.OrdinalIgnoreCase))
            {
                var parts = trimmed.Substring(12).Trim().Split(',');
                if (parts.Length != 2 || !int.TryParse(parts[0].Trim(), out startX) || !int.TryParse(parts[1].Trim(), out startY))
                {
                    error = $"playerStart '{trimmed}' is not 'playerStart: x,y'";
                    return null;
                }
            }
            else if (trimmed.All(c => c == '0' || c == '1'))
            {
                // A grid row above the separator is almost always a missing
                // '---', and that reads very differently from a typo'd header.
                error = $"grid row '{trimmed}' is above the '---' line (the grid comes after it)";
                return null;
            }
            else
            {
                // Anything unrecognised above the separator is a typo, not a
                // grid row. Guessing is what let a stray "Width: 10" become a
                // ragged row and take the whole store down silently.
                error = $"unrecognised header line '{trimmed}' (headers come above the '---' line)";
                return null;
            }
        }

        if (!sawSeparator)
        {
            error = "missing the '---' line between the header and the grid";
            return null;
        }
        if (grid.Count == 0) { error = "no grid rows after '---'"; return null; }
        if (counterEnd == 0) counterEnd = grid[0].Length - 2;

        // Row characters are validated before the layout is built: StoreLayout
        // treats anything that is not '0' as a wall, so a '.' or a stray space
        // would silently carve a shelf where the author drew floor.
        foreach (var row in grid)
        {
            foreach (char c in row)
            {
                if (c != '0' && c != '1')
                {
                    error = $"grid row '{row}' has a '{c}' in it; only '0' (floor) and '1' (shelf)";
                    return null;
                }
            }
        }

        StoreLayout layout;
        try
        {
            layout = new StoreLayout(name, grid, counterRow: counterRow,
                counterStart: counterStart, counterEnd: counterEnd,
                playerStart: new GridPos(startX, startY));
        }
        catch (ArgumentException e)
        {
            error = e.Message;
            return null;
        }

        return Validate(layout, out error) ? layout : null;
    }

    /// <summary>
    /// Checks the guarantees the generator makes by construction: the player
    /// starts on the floor, the counter is reachable from there, and no
    /// walkable cell is cut off — loot spawns on shelf edges, and a shelf in a
    /// sealed pocket is an item nobody can ever secure.
    /// </summary>
    public static bool Validate(StoreLayout layout, out string? error)
    {
        error = null;
        var start = layout.PlayerStart;

        // Read the row directly: StoreLayout.Rows is the raw grid, and a
        // start cell on a shelf is a player who cannot move at all.
        if (StoreLayout.IsBlocked(layout.Rows[start.Y][start.X]))
        {
            error = $"player start {start.X},{start.Y} is inside a shelf";
            return false;
        }

        var map = new GridMap(layout);

        int reachable = map.DistanceMapFrom(start)
            .Cast<int>()
            .Count(d => d != int.MaxValue && d >= 0);

        if (reachable < map.WalkableCells().Count())
        {
            int stranded = map.WalkableCells().Count() - reachable;
            error = $"{stranded} floor cell(s) are cut off from the player start";
            return false;
        }

        if (map.PathTo(start, CounterCell(layout)).Count == 0
            && start != CounterCell(layout))
        {
            error = "the counter cannot be reached from the player start";
            return false;
        }

        return true;
    }

    private static GridPos CounterCell(StoreLayout layout) =>
        new(Math.Clamp((layout.CounterStart + layout.CounterEnd) / 2, 0, layout.Width - 1),
            layout.CounterRow);
}
