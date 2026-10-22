using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;

namespace StoreChase;

/// <summary>
/// What power-ups exist. Each is a single-use thing the player pockets by
/// walking over it and fires with the Use key.
///
/// The three are deliberately a repel/attract/speed triangle: the spray
/// pushes every cat in earshot into a panic, the treat pulls them to where
/// you stand, and the skates are pure player speed. None of them touch the
/// loot rules — the ball remains the only thing that makes a cat drop what
/// it is carrying.
/// </summary>
public enum PowerUpKind
{
    /// <summary>Move faster for a while (roller skates under the counter).</summary>
    Skates,

    /// <summary>Hiss: every cat within earshot panics and runs from you.</summary>
    Spray,

    /// <summary>Drop a treat: nearby cats converge on it (a lure, not a stun).</summary>
    Treat,

    /// <summary>Smoke bomb: panics every cat in earshot for six seconds.</summary>
    SmokeBomb,

    /// <summary>Floor trap: stuns the first cat to step on it.</summary>
    Trap,

    /// <summary>Laser pointer: a dot cats chase for six seconds.</summary>
    Laser,
}

/// <summary>A power-up lying on the floor. Dumb data, like LootItem.</summary>
public sealed class PowerUpItem
{
    /// <summary>What it is.</summary>
    public PowerUpKind Kind { get; }

    /// <summary>The cell it sits on.</summary>
    public GridPos Cell { get; }

    /// <summary>World position (X/Z on the floor, Y = 0).</summary>
    public Vector3 WorldPos { get; }

    /// <summary>Creates a power-up of the given kind at the given cell.</summary>
    public PowerUpItem(PowerUpKind kind, GridPos cell, Vector3 worldPos)
    {
        Kind = kind;
        Cell = cell;
        WorldPos = worldPos;
    }

    /// <summary>Display colour, keyed to the kind so the HUD and the floor
    /// marker can never disagree about what a pickup is.</summary>
    public Color Tint => Kind switch
    {
        PowerUpKind.Skates => new Color(120, 190, 240),
        PowerUpKind.Spray => new Color(196, 130, 220),
        PowerUpKind.SmokeBomb => new Color(140, 140, 150),
        PowerUpKind.Trap => new Color(180, 90, 90),
        PowerUpKind.Laser => new Color(220, 80, 80),
        _ => new Color(235, 190, 110),
    };

    /// <summary>Display name, for the feed and the HUD.</summary>
    public static string Name(PowerUpKind kind) => kind switch
    {
        PowerUpKind.Skates => "roller skates",
        PowerUpKind.Spray => "spray bottle",
        PowerUpKind.SmokeBomb => "smoke bomb",
        PowerUpKind.Trap => "floor trap",
        PowerUpKind.Laser => "laser pointer",
        _ => "bag of treats",
    };
}

/// <summary>
/// The player's power-up pocket: exactly one slot, picked up by walking over
/// a <see cref="PowerUpItem"/> and spent with the Use key.
///
/// One slot is the whole design. A full inventory turns a heist into logistics
/// management, and the choice of *which* one to carry is the only decision a
/// pocket needs to be interesting.
/// </summary>
public sealed class PowerUpInventory
{
    /// <summary>What's in the pocket right now, or null when empty.</summary>
    public PowerUpKind? Pocketed { get; private set; }

    /// <summary>True when something can be picked up.</summary>
    public bool IsEmpty => Pocketed == null;

    /// <summary>Pockets <paramref name="kind"/>. Fails when already full.</summary>
    public bool TryPocket(PowerUpKind kind)
    {
        if (Pocketed != null) return false;
        Pocketed = kind;
        return true;
    }

    /// <summary>
    /// Spends the pocketed power-up and returns what it was, or null when
    /// the pocket was empty (so the caller can stay quiet about pressing
    /// a key with nothing to use).
    /// </summary>
    public PowerUpKind? Use()
    {
        var kind = Pocketed;
        Pocketed = null;
        return kind;
    }

    /// <summary>Empties the pocket (round restart).</summary>
    public void Clear() => Pocketed = null;
}

/// <summary>
/// The spray's area effect, split out of Game1 so the "who hears the hiss"
/// rule can be checked without a GraphicsDevice.
///
/// The radius is measured in <em>walking</em> distance rather than straight
/// lines: a shelf between you and a cat genuinely stops it hearing the
/// spray, which is what makes spraying down an aisle a different (and
/// sometimes useless) decision from spraying across an open plaza.
/// </summary>
public static class SprayEffect
{
    /// <summary>
    /// Panics every critter within <paramref name="radius"/> walking cells of
    /// <paramref name="source"/>. Returns how many were caught.
    /// </summary>
    public static int PanicAt(GridMap map, GridPos source, int radius, IEnumerable<Critter> critters, float seconds)
    {
        var dist = map.DistanceMapFrom(source);
        int caught = 0;

        foreach (var critter in critters)
        {
            int d = dist[critter.Cell.X, critter.Cell.Y];
            if (d == int.MaxValue || d > radius) continue;
            critter.Panic(seconds);
            caught++;
        }

        return caught;
    }
}

/// <summary>
/// Where the power-ups are. One of each kind per round, scattered on
/// walkable floor away from the counter and the player's start — the same
/// "spread out so the round opens quiet" logic as the loot, for the same
/// reason.
///
/// Headless by design: spawning, picking up and the pocket rules are pure
/// grid logic, and the harness tests them without a GraphicsDevice.
/// </summary>
public sealed class PowerUpField
{
    private readonly float _cellSize;
    private readonly List<PowerUpItem> _items = new();

    /// <summary>All power-ups still lying on the floor.</summary>
    public IReadOnlyList<PowerUpItem> Items => _items;

    /// <summary>Creates a field; the cell size converts cells to world positions.</summary>
    public PowerUpField(float cellSize) => _cellSize = cellSize;

    /// <summary>
    /// Lays out one of every kind, scattered. Returns nothing: the field's
    /// items are the state, and a round always gets the same three kinds —
    /// variety comes from where they land and which you choose to carry.
    /// </summary>
    public void Spawn(GridMap map, GridPos avoidNear, Random rng)
    {
        _items.Clear();

        var candidates = map.WalkableCells()
            .Where(c => !map.IsCounter(c.X, c.Y))
            .Where(c => Manhattan(c, avoidNear) > 3)
            .ToList();

        // Fisher-Yates so each kind lands somewhere unpredictable but the
        // whole set is spread across the candidate pool.
        for (int i = candidates.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (candidates[i], candidates[j]) = (candidates[j], candidates[i]);
        }

        int taken = 0;
        foreach (var kind in Enum.GetValues<PowerUpKind>())
        {
            if (taken >= candidates.Count) break;
            var cell = candidates[taken++];
            _items.Add(new PowerUpItem(kind, cell, CellCenter(cell)));
        }
    }

    /// <summary>The power-up on a cell, if any.</summary>
    public PowerUpItem? At(GridPos cell) => _items.FirstOrDefault(i => i.Cell == cell);

    /// <summary>
    /// Walked onto a power-up: pocket it if the pocket is empty.
    ///
    /// A full pocket leaves it on the floor — no auto-swap. Deciding what to
    /// carry is the only decision the pocket has, and taking that away by
    /// silently replacing the sprays you were saving would be the wrong kind
    /// of convenience.
    /// </summary>
    /// <returns>True if it was picked up.</returns>
    public bool TryPickUp(GridPos cell, PowerUpInventory inventory)
    {
        var item = At(cell);
        if (item == null || !inventory.TryPocket(item.Kind)) return false;

        _items.Remove(item);
        return true;
    }

    /// <summary>Removes every power-up (round restart).</summary>
    public void Clear() => _items.Clear();

    private Vector3 CellCenter(GridPos cell) => new(cell.X * _cellSize, 0f, cell.Y * _cellSize);

    private static int Manhattan(GridPos a, GridPos b) => Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y);
}
