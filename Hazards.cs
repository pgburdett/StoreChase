using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;

namespace StoreChase;

/// <summary>
/// The store's hazards: spills on the floor and one fire alarm on the wall.
///
/// Both are pure grid facts plus their effects — every effect here is
/// headless (critters, items and the alarm are all graphics-free), because a
/// hazard you can only test by playing the game is a hazard that will
/// eventually do something surprising in front of someone.
///
/// The two hazards answer different questions. A spill is terrain: it costs
/// the player control and costs a cat its footing, whoever walks into it.
/// The fire alarm is a deliberate, once-per-round trade: scatter every cat
/// and make them drop the goods now, in exchange for a chunk of the alarm
/// meter — you are, after all, pulling the fire alarm in a store that is
/// already watching you.
/// </summary>
public sealed class HazardField
{
    private readonly float _cellSize;
    private readonly List<GridPos> _spills = new();

    /// <summary>The cells with a spill on them.</summary>
    public IReadOnlyList<GridPos> Spills => _spills;

    /// <summary>The cell the fire alarm sits on, or null if this round has none.</summary>
    public GridPos? FireAlarmCell { get; private set; }

    /// <summary>True once the alarm has been pulled (it is a one-shot — the
    /// box reads as "used" afterwards).</summary>
    public bool FireAlarmPulled { get; private set; }

    /// <summary>Creates a hazard field; the cell size converts cells to world positions.</summary>
    public HazardField(float cellSize) => _cellSize = cellSize;

    /// <summary>
    /// Lays out the round's hazards: <paramref name="spillCount"/> spills and
    /// one fire alarm, all away from the player's start and off the counter.
    ///
    /// <paramref name="alsoAvoid"/> lets the caller keep hazard cells off the
    /// loot and power-up cells so markers never stack on top of each other.
    /// </summary>
    public void Spawn(GridMap map, GridPos avoidNear, Random rng, int spillCount = 2, IEnumerable<GridPos>? alsoAvoid = null)
    {
        _spills.Clear();
        FireAlarmCell = null;
        FireAlarmPulled = false;

        var blocked = alsoAvoid?.ToHashSet() ?? new HashSet<GridPos>();

        var floorCells = map.WalkableCells()
            .Where(c => !map.IsCounter(c.X, c.Y))
            .Where(c => Manhattan(c, avoidNear) > 3)
            .Where(c => !blocked.Contains(c))
            .ToList();
        Shuffle(floorCells, rng);

        for (int i = 0; i < spillCount && i < floorCells.Count; i++)
            _spills.Add(floorCells[i]);

        // The fire alarm is wall-mounted: a shelf-edge cell (so it sits
        // against the racking, where a real one would be) and clear of the
        // spills so the two hazards don't read as one blob.
        var wallCells = map.ShelfEdgeCells()
            .Where(c => !map.IsCounter(c.X, c.Y))
            .Where(c => Manhattan(c, avoidNear) > 4)
            .Where(c => !blocked.Contains(c) && !_spills.Contains(c))
            .ToList();
        Shuffle(wallCells, rng);
        if (wallCells.Count > 0) FireAlarmCell = wallCells[0];
    }

    /// <summary>Adds a single spill without clearing existing ones (weather).</summary>
    public void AddSpill(GridPos cell)
    {
        if (!_spills.Contains(cell)) _spills.Add(cell);
    }

    /// <summary>True if the cell has a spill on it.</summary>
    public bool IsSpill(GridPos cell) => _spills.Contains(cell);

    /// <summary>True if the cell is where the fire alarm lives.</summary>
    public bool IsFireAlarm(GridPos cell) => FireAlarmCell == cell;

    /// <summary>
    /// A cat landing on a spill loses its footing: it is stunned briefly and
    /// drops what it was carrying, exactly like a glancing ball hit (the
    /// stun honours the species' tolerance — a Moggy plows through, which
    /// reads as the tank it is).
    /// </summary>
    /// <returns>True if the cat actually slipped.</returns>
    public bool TripCritter(Critter critter, ItemField items, float slipSeconds = 1.5f)
    {
        if (!IsSpill(critter.Cell)) return false;

        bool stunned = critter.TryStunFromBall(slipSeconds); // same tolerance, different cause
        if (!stunned) return false;                          // immune: it just walks on

        if (critter.Carrying)
        {
            items.DropFrom(critter, critter.Cell);
            critter.Carrying = false;
        }
        return true;
    }

    /// <summary>
    /// The player stands on the fire alarm and presses Use.
    ///
    /// Every cat panics and startles, anything being carried hits the floor,
    /// and the alarm takes a fixed spike — one get-out-of-jail-free card per
    /// round, at a price. Returns false when the player is not on the alarm
    /// cell or it has already been pulled (so the caller can fall through to
    /// the power-up pocket on the same key).
    /// </summary>
    /// <param name="scattered">How many cats were caught in the panic.</param>
    public bool TryPullFireAlarm(GridPos playerCell, IEnumerable<Critter> critters, ItemField items, AlarmSystem alarm, float spike, out int scattered)
    {
        scattered = 0;
        if (FireAlarmPulled || !IsFireAlarm(playerCell)) return false;

        FireAlarmPulled = true;
        foreach (var critter in critters)
        {
            critter.Panic(6f);
            critter.Startle(1.2f);
            scattered++;

            if (critter.Carrying)
            {
                items.DropFrom(critter, critter.Cell);
                critter.Carrying = false;
            }
        }

        alarm.Spike(spike);
        return true;
    }

    /// <summary>Removes every hazard (round restart).</summary>
    public void Clear()
    {
        _spills.Clear();
        FireAlarmCell = null;
        FireAlarmPulled = false;
    }

    private static void Shuffle(List<GridPos> cells, Random rng)
    {
        for (int i = cells.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (cells[i], cells[j]) = (cells[j], cells[i]);
        }
    }

    private static int Manhattan(GridPos a, GridPos b) => Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y);
}
