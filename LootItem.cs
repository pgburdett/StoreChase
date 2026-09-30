using System;
using Microsoft.Xna.Framework;

namespace StoreChase;

// Which shelf the loot came from. Purely cosmetic — it picks the box colour and
// a little bob offset so a heap of items is readable at a glance.
public enum LootKind
{
    Canned,
    Boxed,
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
    public LootKind Kind { get; }
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

    public LootItem(LootKind kind, GridPos cell, Vector3 worldPos)
    {
        Kind = kind;
        Cell = cell;
        WorldPos = worldPos;
    }

    public bool IsLoose => State == LootState.Loose;
    public bool IsSecured => State == LootState.Secured;

    // Per-kind palette. These read against the store's grey floor and brown
    // shelves without needing textures.
    public Color Tint => Kind switch
    {
        LootKind.Canned => new Color(196, 78, 72),
        LootKind.Boxed => new Color(196, 154, 88),
        _ => new Color(120, 168, 104),
    };
}
