using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using StoreChase;

namespace StoreChase.Harness;

/// <summary>
/// Checks on the cats sharing what they know about the player.
///
/// Before this, every cat worked out "can I see the player?" on its own, so
/// one cat spotting you had no effect on the others. The knowledge is now
/// pooled and passed around, which is the difference between a store full of
/// cats and three unrelated animals.
///
/// It is easy to break this by accident — moving the sight check, changing
/// the radius, forgetting to reset the pool — so it is checked directly.
/// </summary>
public static class AwarenessChecks
{
    /// <summary>Runs every awareness check.</summary>
    public static void Run()
    {
        Check.Section("Awareness: cats telling each other");

        var map = new GridMap(StoreLayouts.Standard);

        // Two cats in the same aisle, one of which can see the player.
        var spotter = MakeCritter(new GridPos(5, 5), map);
        var listener = MakeCritter(new GridPos(5, 7), map);

        // The player is next to the spotter.
        var playerCell = new GridPos(5, 4);

        spotter.CanSeePlayer = map.HasLineOfSight(spotter.Cell, playerCell);
        listener.CanSeePlayer = map.HasLineOfSight(listener.Cell, playerCell);

        Check.That("the cat next to the player can see them", spotter.CanSeePlayer);
        Check.That("the cat further down the aisle has clear sight too (open aisle)",
            listener.CanSeePlayer,
            $"LOS from {listener.Cell.X},{listener.Cell.Y} to {playerCell.X},{playerCell.Y}");

        // A cat behind a shelf genuinely cannot see the player. Find one:
        // a cell with a shelf between it and the player.
        var hidden = FindBlindCell(map, playerCell);
        if (hidden.HasValue)
        {
            var blind = MakeCritter(hidden.Value, map);
            blind.CanSeePlayer = false;
            Check.That("a cat behind a shelf cannot see the player", !map.HasLineOfSight(hidden.Value, playerCell));

            var awarenessHidden = new SharedAwareness();
            awarenessHidden.Reset(new[] { spotter, blind });
            awarenessHidden.Update(playerCell, map);
            Check.That("a cat behind a shelf still hears about the player",
                blind.PlayerAwareness > 0f,
                $"awareness {blind.PlayerAwareness:F2}");
        }

        var awareness = new SharedAwareness();
        awareness.Reset(new[] { spotter, listener });
        awareness.Update(playerCell, map);

        // Awareness is a *vagueness*: zero means the cat knows exactly.
        Check.That("the cat that saw the player knows exactly where they are",
            spotter.PlayerAwareness == 0f && spotter.LastSeenPlayerCell == playerCell,
            $"awareness {spotter.PlayerAwareness:F2}");

        // The listener in this aisle can see the player too, so it knows
        // exactly as well — that is correct, not a bug.
        Check.That("a cat with its own sight of the player also knows exactly",
            listener.PlayerAwareness == 0f,
            $"awareness {listener.PlayerAwareness:F2}");

        // The interesting case is a cat that *cannot* see the player but is
        // close enough to be told.
        if (hidden.HasValue)
        {
            var blind = MakeCritter(hidden.Value, map);
            blind.CanSeePlayer = false;

            var awarenessBlind = new SharedAwareness();
            awarenessBlind.Reset(new[] { spotter, blind });
            awarenessBlind.Update(playerCell, map);

            Check.That("a blind cat that is close enough hears about the player",
                blind.PlayerAwareness > 0f,
                $"awareness {blind.PlayerAwareness:F2}");
            Check.That("the blind cat's knowledge is vaguer than the spotter's",
                blind.PlayerAwareness > spotter.PlayerAwareness,
                $"blind {blind.PlayerAwareness:F2} vs spotter {spotter.PlayerAwareness:F2}");
            Check.That("the blind cat knows roughly where the player was",
                blind.LastSeenPlayerCell.HasValue,
                $"last seen {blind.LastSeenPlayerCell?.X},{blind.LastSeenPlayerCell?.Y}");
        }

        // A cat too far away hears nothing.
        var far = MakeCritter(new GridPos(15, 10), map);
        far.CanSeePlayer = false;
        var awareness2 = new SharedAwareness();
        awareness2.Reset(new[] { spotter, far });
        awareness2.Update(playerCell, map);
        Check.That("a cat across the store hears nothing",
            far.PlayerAwareness == 0f,
            $"awareness {far.PlayerAwareness:F2}");

        // If nobody sees the player, nobody knows anything.
        var blind1 = MakeCritter(new GridPos(5, 5), map);
        var blind2 = MakeCritter(new GridPos(5, 7), map);
        blind1.CanSeePlayer = false;
        blind2.CanSeePlayer = false;
        var awareness3 = new SharedAwareness();
        awareness3.Reset(new[] { blind1, blind2 });
        awareness3.Update(playerCell, map);
        Check.That("no sighting means no knowledge",
            blind1.PlayerAwareness == 0f && blind2.PlayerAwareness == 0f);

        // The pool has to be reset each frame, or a cat that was removed
        // (tackled) would keep receiving news.
        awareness3.Reset(new[] { blind1 });
        awareness3.Update(playerCell, map);
        Check.That("a cat that is no longer in the pool stops hearing things",
            blind2.PlayerAwareness == 0f);
    }

    private static Critter MakeCritter(GridPos cell, GridMap map)
    {
        var profile = CritterProfile.For(CritterKind.ShopCat);
        var ai = new SmartAnimalAI(profile, new Random(0));
        return new Critter(cell, 2f, ai, profile);
    }

    // Finds a walkable cell that cannot see the given one, so the test has a
    // genuinely blind cat to work with rather than assuming a layout.
    private static GridPos? FindBlindCell(GridMap map, GridPos player)
    {
        foreach (var cell in map.WalkableCells())
        {
            if (cell == player) continue;
            if (!map.HasLineOfSight(cell, player)) return cell;
        }
        return null;
    }
}
