using System;
using System.Linq;
using Microsoft.Xna.Framework;
using StoreChase;

namespace StoreChase.Harness;

/// <summary>
/// Checks on the minimap.
///
/// The store is bigger than the screen, so the minimap is how the player
/// knows where they are in the whole floor plan. If it lies — a shelf drawn
/// as open floor, the till drawn in the wrong place — it is worse than no
/// minimap at all.
///
/// The pixel data is a pure function of the layout, so it can be checked
/// without a graphics device.
/// </summary>
public static class MinimapChecks
{
    /// <summary>Runs every minimap check.</summary>
    public static void Run()
    {
        Check.Section("Minimap: the plan on the screen matches the floor");

        foreach (var layout in StoreLayouts.All)
        {
            var pixels = Hud.MinimapPixels(layout);

            Check.That($"'{layout.Name}': the minimap has one pixel per cell",
                pixels.Length == layout.Width * layout.Height,
                $"{pixels.Length} pixels for {layout.Width}x{layout.Height}");

            // Every cell must be distinguishable from the floor only if it is
            // actually a shelf or the till. Walk the plan and compare.
            int shelfMismatches = 0;
            int counterMismatches = 0;
            var counterColour = new Color(226, 176, 64);
            var shelfColour = new Color(52, 54, 64);
            var floorColour = new Color(24, 26, 34);

            for (int y = 0; y < layout.Height; y++)
            {
                for (int x = 0; x < layout.Width; x++)
                {
                    var p = pixels[y * layout.Width + x];
                    bool isCounter = layout.IsCounter(x, y);
                    bool isShelf = !isCounter && StoreLayout.IsBlocked(layout.Rows[y][x]);

                    if (isCounter && p != counterColour) counterMismatches++;
                    else if (isShelf && p != shelfColour) shelfMismatches++;
                    else if (!isCounter && !isShelf && p != floorColour) shelfMismatches++;
                }
            }

            Check.That($"'{layout.Name}': every shelf and the till are drawn correctly",
                shelfMismatches == 0 && counterMismatches == 0,
                $"{shelfMismatches} wrong cells, {counterMismatches} wrong counter cells");

            // The till has to be visible on the plan, or the player has no
            // idea where to take the goods.
            int counterCells = 0;
            for (int x = layout.CounterStart; x <= layout.CounterEnd; x++)
                if (layout.IsCounter(x, layout.CounterRow)) counterCells++;

            Check.That($"'{layout.Name}': the till is on the plan",
                counterCells == layout.CounterEnd - layout.CounterStart + 1,
                $"{counterCells} counter cells");

            // The player must be on a walkable cell, or their dot would be
            // drawn inside a shelf.
            Check.That($"'{layout.Name}': the player starts on open floor",
                !StoreLayout.IsBlocked(layout.Rows[layout.PlayerStart.Y][layout.PlayerStart.X]));
        }
    }
}
