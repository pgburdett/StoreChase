using System;
using System.Linq;
using Microsoft.Xna.Framework;
using StoreChase;

namespace StoreChase.Harness;

/// <summary>
/// Checks on the power-up system: spawning, the one-slot pocket, the speed
/// boost's effect on actual movement, and the spray's walking-distance
/// radius.
///
/// The pocket rules are the ones worth guarding — a bug that lets the pocket
/// hold two things (or silently drops one) would be invisible in a round
/// simulation, because nothing in the sim picks power-ups up.
/// </summary>
public static class PowerUpChecks
{
    private const float CellSize = 2f;

    /// <summary>Runs every power-up check.</summary>
    public static void Run()
    {
        Check.Section("Power-ups: pocket, boosts and spray radius");

        var rng = new Random(7);
        var map = new GridMap(StoreLayouts.Standard);
        var start = StoreLayouts.Standard.PlayerStart;

        var field = new PowerUpField(CellSize);
        field.Spawn(map, start, rng);

        Check.That("a round lays out one of every power-up",
            field.Items.Count == Enum.GetValues<PowerUpKind>().Length,
            $"{field.Items.Count} items: {string.Join(", ", field.Items.Select(i => i.Kind))}");

        Check.That("no power-up lands on the counter or in a wall",
            field.Items.All(i => map.IsWalkable(i.Cell.X, i.Cell.Y) && !map.IsCounter(i.Cell.X, i.Cell.Y)),
            string.Join(", ", field.Items.Select(i => $"{i.Kind}@{i.Cell.X},{i.Cell.Y}")));

        CheckThatNoneNearStart(field, start);

        CheckPocket(field, map);
        CheckSpeedBoost(map, start);
        CheckSprayRadius(map);
    }

    // The opening moment of a round should be quiet: a power-up under the
    // player's feet would hand them a boost before they've seen the store.
    private static void CheckThatNoneNearStart(PowerUpField field, GridPos start)
    {
        bool allFar = field.Items.All(i =>
            Math.Abs(i.Cell.X - start.X) + Math.Abs(i.Cell.Y - start.Y) > 3);
        Check.That("no power-up spawns on top of the player's start", allFar,
            string.Join(", ", field.Items.Select(i => $"{i.Kind}@{i.Cell.X},{i.Cell.Y}")));
    }

    private static void CheckPocket(PowerUpField field, GridMap map)
    {
        var rng = new Random(3);
        field.Spawn(map, new GridPos(1, 1), rng);
        var inventory = new PowerUpInventory();

        Check.That("the pocket starts empty", inventory.IsEmpty, "pocketed: " + inventory.Pocketed);

        // Walking onto the first one pockets it.
        var first = field.Items[0];
        bool picked = field.TryPickUp(first.Cell, inventory);
        Check.That("walking onto a power-up pockets it",
            picked && inventory.Pocketed == first.Kind && !field.Items.Contains(first),
            $"picked {picked}, pocket {inventory.Pocketed}");

        // The slot is full: a second pickup must fail and leave the item
        // where it was, rather than swapping or dropping one.
        var second = field.Items.First();
        bool pickedWhileFull = field.TryPickUp(second.Cell, inventory);
        Check.That("a full pocket leaves the power-up on the floor",
            !pickedWhileFull && inventory.Pocketed == first.Kind && field.Items.Contains(second),
            $"picked {pickedWhileFull}, pocket {inventory.Pocketed}");

        // Using spends it, and reports what it spent.
        var used = inventory.Use();
        Check.That("using spends the pocket",
            used == first.Kind && inventory.IsEmpty,
            $"used {used}, pocket {inventory.Pocketed}");

        // And an empty pocket fires nothing (so the game stays quiet when
        // the key is pressed with nothing to use).
        Check.That("using an empty pocket is a no-op",
            inventory.Use() == null, "returned something");

        // Now the floor one goes in, and a round restart clears everything.
        Check.That("the pocket refills after use",
            field.TryPickUp(second.Cell, inventory) && inventory.Pocketed == second.Kind,
            $"pocket {inventory.Pocketed}");

        inventory.Clear();
        field.Clear();
        Check.That("clearing for a new round empties field and pocket",
            inventory.IsEmpty && field.Items.Count == 0,
            $"items {field.Items.Count}");
    }

    // The skates are only worth pocketing if they actually move the player.
    // Measured the honest way: how long a tile takes, with and without.
    private static void CheckSpeedBoost(GridMap map, GridPos start)
    {
        float plain = SecondsToCrossOneTile(map, start, new Vector2(1f, 0f), speedMultiplier: 1f);
        float boosted = SecondsToCrossOneTile(map, start, new Vector2(1f, 0f), speedMultiplier: Game1.SkatesSpeedBoost);

        Check.That("the skates make the player measurably faster",
            plain > 0f && boosted > 0f && boosted < plain,
            $"plain {plain:F3}s, boosted {boosted:F3}s");

        // 1.5x should be within a frame of 1/1.5th the time; the frame
        // quantisation is 1/60s, so allow one frame of slack each way.
        float expected = plain / Game1.SkatesSpeedBoost;
        Check.That("the boost is the advertised multiplier",
            Math.Abs(boosted - expected) <= (2f / 60f),
            $"boosted {boosted:F3}s vs expected {expected:F3}s");
    }

    private static float SecondsToCrossOneTile(GridMap map, GridPos start, Vector2 dir, float speedMultiplier)
    {
        var player = new Player(start, CellSize) { SpeedMultiplier = speedMultiplier };
        var target = new GridPos(start.X + (int)dir.X, start.Y + (int)dir.Y);
        var gt = new GameTime(TimeSpan.FromSeconds(1.0 / 60.0), TimeSpan.FromSeconds(1.0 / 60.0));

        for (int frame = 1; frame <= 300; frame++)
        {
            player.Update(gt, map, dir, freeMove: false);
            if (player.Cell == target) return frame / 60f;
        }
        return -1f; // never arrived
    }

    // The spray reaches through aisles, not through shelves.
    private static void CheckSprayRadius(GridMap map)
    {
        var source = new GridPos(1, 1);

        // Three cells down the left aisle: definitely in earshot.
        var near = NewCat(new GridPos(1, 4));
        // The far bottom-right corner: way outside the radius.
        var far = NewCat(new GridPos(16, 10));
        var critters = new[] { near, far };

        int caught = SprayEffect.PanicAt(map, source, Game1.SprayPanicRadius, critters, 4f);

        Check.That("the spray panics a cat three cells away",
            caught == 1 && near.PanicSeconds > 3.5f,
            $"caught {caught}, near panic {near.PanicSeconds:F1}s");
        Check.That("the spray leaves a cat across the store calm",
            far.PanicSeconds <= 0f, $"far panic {far.PanicSeconds:F1}s");

        // A panicking cat recovers on its own.
        var gt = new GameTime(TimeSpan.FromSeconds(1.0), TimeSpan.FromSeconds(1.0));
        var locus = new CritterLocus(
            Map: map,
            PlayerCell: new GridPos(16, 10),
            Ball: null,
            LootTarget: null,
            LootTargetItem: null,
            StolenCount: 0,
            LootUnderfoot: _ => null,
            TrySteal: _ => false);
        near.Update(gt, locus);
        near.Update(gt, locus);
        near.Update(gt, locus);
        near.Update(gt, locus);
        Check.That("the panic wears off after its seconds",
            near.PanicSeconds <= 0f, $"panic {near.PanicSeconds:F1}s left");
    }

    private static Critter NewCat(GridPos cell)
    {
        var profile = CritterProfile.For(CritterKind.Kitten);
        return new Critter(cell, CellSize, new SmartAnimalAI(profile, new Random(1)), profile);
    }
}
