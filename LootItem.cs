using System;
using Microsoft.Xna.Framework;

namespace StoreChase;

// Which shelf the loot came from. Not purely cosmetic any more: each species
// has a taste (see <see cref="CritterProfile.PreferredKind"/>), and the
// value decides how much the alarm minds it being gone.
public enum LootKind
{
    /// <summary>A canned good — red box.</summary>
    Canned,

    /// <summary>A boxed good — brown box.</summary>
    Boxed,

    /// <summary>A bagged good — green box.</summary>
    Bagged,
}

public enum LootState
{
    /// <summary>On the floor/shelf, waiting to be taken.</summary>
    Loose,

    /// <summary>In a critter's jaws, being carried off.</summary>
    Carried,

    /// <summary>The player is holding it and heading for the counter.</summary>
    Held,

    /// <summary>Behind the counter. Safe; counts toward winning.</summary>
    Secured,
}

// One stealable item. It is deliberately a dumb data holder: who may take it,
// who dropped it and what that does to the alarm all live in ItemField and
// AlarmSystem, so this stays a thing that can be rendered and tracked without
// knowing any rules.
public sealed class LootItem
{
    /// <summary>Which shelf the loot came from (cosmetic — picks the box colour).</summary>
    public LootKind Kind { get; }

    /// <summary>Current possession state.</summary>
    public LootState State { get; set; } = LootState.Loose;

    /// <summary>Where it sits when loose, and where it was dropped.</summary>
    public GridPos Cell { get; set; }

    /// <summary>Rendered position; follows the carrier when Carried/Held.</summary>
    public Vector3 WorldPos { get; set; }

    /// <summary>The critter currently carrying it, if any.</summary>
    public Critter? Carrier { get; set; }

    /// <summary>Seconds since the player last picked it up, used for the
    /// "dropped just now" hint.</summary>
    public float AgeSeconds { get; set; }

    /// <summary>Creates a new loot item of the given kind at the given cell.</summary>
    public LootItem(LootKind kind, GridPos cell, Vector3 worldPos)
    {
        Kind = kind;
        Cell = cell;
        WorldPos = worldPos;
    }

    /// <summary>
    /// How much this item is worth, as a multiplier on the alarm's rise.
    ///
    /// The cheap stuff barely matters if it goes missing; the expensive
    /// stuff is what the store actually cares about. This is what makes the
    /// three kinds worth distinguishing beyond their colour.
    /// </summary>
    public float AlarmWeight => Kind switch
    {
        LootKind.Canned => 0.7f,
        LootKind.Bagged => 1.0f,
        LootKind.Boxed => 1.5f,
        _ => 1.0f,
    };

    /// <summary>True when the item is on the floor/shelf, waiting to be taken.</summary>
    public bool IsLoose => State == LootState.Loose;

    /// <summary>True when the item is behind the counter (safe).</summary>
    public bool IsSecured => State == LootState.Secured;

    // Per-kind palette. These read against the store's grey floor and brown
    // shelves without needing textures.
    /// <summary>The item's display colour, based on its kind.</summary>
    public Color Tint => Kind switch
    {
        LootKind.Canned => new Color(196, 78, 72),
        LootKind.Boxed => new Color(196, 154, 88),
        _ => new Color(120, 168, 104),
    };
}
