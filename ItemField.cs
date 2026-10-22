using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;

namespace StoreChase;

// Owns every piece of loot in the store and all the rules about who is holding
// what. Game1 asks it what to draw and tells it about tackles and pickups; the
// critter AI asks it where the nearest loose item is.
//
// The rules it enforces, in one place so they stay consistent:
//   - a critter can pick an item up by stepping onto it (thieving)
//   - a tackled critter drops whatever it was carrying, where it stands
//   - the player picks an item up by stepping onto it, and secures it by
//     reaching the counter while holding one
//   - items dropped on a counter cell are secured automatically, so a thief
//     that reaches the counter with loot doesn't win the round
public class ItemField
{
    private readonly GridMap _map;
    private readonly float _cellSize;
    private readonly List<LootItem> _items = new();
    private readonly Random _rng;

    /// <summary>All loot items in the store.</summary>
    public IReadOnlyList<LootItem> Items => _items;

    /// <summary>Total number of items.</summary>
    public int TotalCount => _items.Count;

    /// <summary>Number of items behind the counter.</summary>
    public int SecuredCount => _items.Count(i => i.IsSecured);

    /// <summary>Number of items currently carried by critters.</summary>
    public int StolenCount => _items.Count(i => i.State == LootState.Carried);

    /// <summary>
    /// How much the alarm cares about what is currently out of place.
    ///
    /// This is the sum of the stolen items' weights, rather than a plain
    /// count. A single boxed item is worth more to the store than three tins,
    /// so the alarm should treat them differently — otherwise the player has
    /// no reason to care which goods they left on the shelves.
    /// </summary>
    public float StolenWeight => _items
        .Where(i => i.State == LootState.Carried)
        .Sum(i => i.AlarmWeight);

    /// <summary>The item the player is carrying, if any.</summary>
    public LootItem? Held { get; private set; }

    /// <summary>Raised when a critter picks something up, for audio/HUD.</summary>
    public event Action<LootItem>? ItemStolen;

    /// <summary>Raised when a critter drops something (e.g. after a tackle).</summary>
    public event Action<LootItem>? ItemDropped;

    /// <summary>Raised when an item is secured behind the counter.</summary>
    public event Action<LootItem>? ItemSecured;

    /// <summary>Creates a new ItemField with the given map, cell size, and RNG.</summary>
    public ItemField(GridMap map, float cellSize, Random rng)
    {
        _map = map;
        _cellSize = cellSize;
        _rng = rng;
    }

    /// <summary>
    /// Scatters items on cells beside the shelves, spread out and away from the
    /// player start so the first one isn't under their feet.
    /// </summary>
    public void Spawn(int count, GridPos avoidNear)
    {
        _items.Clear();
        Held = null;

        var candidates = _map.ShelfEdgeCells()
            .Where(c => !_map.IsCounter(c.X, c.Y))
            .Where(c => Math.Abs(c.X - avoidNear.X) + Math.Abs(c.Y - avoidNear.Y) > 2)
            .ToList();

        if (candidates.Count == 0) return;

        // Fisher-Yates, then take the first `count`, so the spread is random
        // across the whole store rather than clustered at one end.
        for (int i = candidates.Count - 1; i > 0; i--)
        {
            int j = _rng.Next(i + 1);
            (candidates[i], candidates[j]) = (candidates[j], candidates[i]);
        }

        foreach (var cell in candidates.Take(count))
        {
            var kind = (LootKind)_rng.Next(3);
            _items.Add(new LootItem(kind, cell, CellCenter(cell)));
        }
    }

    /// <summary>Resets all items to the Loose state (used on round restart).</summary>
    public void Reset()
    {
        foreach (var item in _items)
        {
            item.State = LootState.Loose;
            item.Carrier = null;
        }
        Held = null;
    }

    private Vector3 CellCenter(GridPos cell) => new(cell.X * _cellSize, 0f, cell.Y * _cellSize);

    /// <summary>
    /// The loose item a critter standing on <paramref name="cell"/> would take,
    /// or null if there isn't one. Separate from the act of taking it so the AI
    /// can decide whether it wants the trouble.
    /// </summary>
    public LootItem? LooseAt(GridPos cell) => _items.FirstOrDefault(i => i.IsLoose && i.Cell == cell);

    /// <summary>Nearest loose item to a cell, or null if the store is picked clean.</summary>
    public LootItem? NearestLoose(GridPos from)
        => _items.Where(i => i.IsLoose)
                 .OrderBy(i => Math.Abs(i.Cell.X - from.X) + Math.Abs(i.Cell.Y - from.Y))
                 .FirstOrDefault();

    // ---- Transitions -----------------------------------------------------

    /// <summary>Transfers an item to a critter's possession (thieving).</summary>
    public void TakeByCritter(LootItem item, Critter carrier)
    {
        item.State = LootState.Carried;
        item.Carrier = carrier;
        item.AgeSeconds = 0f;
        ItemStolen?.Invoke(item);
    }

    /// <summary>
    /// Tackling: whatever the critter had goes on the floor where it stood.
    /// The critter is released by Game1, not here. A drop on a counter cell is
    /// secured rather than dropped, so a thief tackled at the till still counts
    /// as caught.
    /// </summary>
    public void DropFrom(Critter carrier, GridPos where)
    {
        var carried = _items.FirstOrDefault(i => i.Carrier == carrier && i.State == LootState.Carried);
        if (carried == null) return;

        // A drop on a counter cell is secured rather than dropped, so a thief
        // tackled at the till still counts as caught.
        if (_map.IsCounter(where.X, where.Y))
        {
            Secured(carried, where);
            return;
        }

        carried.State = LootState.Loose;
        carried.Carrier = null;
        carried.Cell = where;
        carried.WorldPos = CellCenter(where);
        carried.AgeSeconds = 0f;
        ItemDropped?.Invoke(carried);
    }

    /// <summary>
    /// A thief reaching the counter with loot: the goods go down on the till
    /// and the alarm spikes.
    ///
    /// This is the thief's escape, and it is a losing one. Bolting for the
    /// counter does not win the round — the item is dropped, not secured, so
    /// the player still has to pick it up and carry it behind the counter. It
    /// buys the thief time and moves the goods somewhere inconvenient, and it
    /// costs the round a big chunk of alarm.
    ///
    /// Note the difference from <see cref="DropFrom"/>, which is the tackle
    /// case: a *tackled* thief's goods on the counter cell count as recovered,
    /// because catching the thief is the point. Here the thief got there on
    /// its own, so the goods are merely dumped.
    /// </summary>
    /// <returns>The item that was dumped, or null if the critter had nothing.</returns>
    public LootItem? AbandonAtCounter(Critter carrier, GridPos where)
    {
        var carried = _items.FirstOrDefault(i => i.Carrier == carrier && i.State == LootState.Carried);
        if (carried == null) return null;

        carried.State = LootState.Loose;
        carried.Carrier = null;
        carried.Cell = where;
        carried.WorldPos = CellCenter(where);
        carried.AgeSeconds = 0f;
        ItemDropped?.Invoke(carried);
        return carried;
    }

    /// <summary>
    /// The player walking onto an item. Returns it if something was picked up.
    /// </summary>
    public LootItem? TryPickUp(GridPos playerCell)
    {
        if (Held != null) return null;

        var item = LooseAt(playerCell);
        if (item == null) return null;

        item.State = LootState.Held;
        item.Cell = playerCell;
        item.AgeSeconds = 0f;
        Held = item;
        return item;
    }

    /// <summary>
    /// The player reaching the counter. Secures whatever they're holding, or a
    /// loose item that happens to be lying on the counter.
    /// </summary>
    public bool TrySecure(GridPos playerCell)
    {
        if (!_map.IsCounter(playerCell.X, playerCell.Y)) return false;

        if (Held != null)
        {
            var held = Held;
            Held = null;
            Secured(held, playerCell);
            return true;
        }

        var onCounter = _items.FirstOrDefault(i => i.IsLoose && _map.IsCounter(i.Cell.X, i.Cell.Y));
        if (onCounter != null) { Secured(onCounter, onCounter.Cell); return true; }

        return false;
    }

    private void Secured(LootItem item, GridPos cell)
    {
        item.State = LootState.Secured;
        item.Carrier = null;
        item.Cell = cell;
        item.AgeSeconds = 0f;

        // Only clear Held if this is the item the player was holding. A tackle
        // at the counter secures the *critter's* item, and that must not orphan
        // whatever the player is already carrying (which would otherwise stay
        // stuck in the Held state forever, with no way to put it down).
        if (Held == item) Held = null;

        ItemSecured?.Invoke(item);
    }

    // ---- Per-frame -------------------------------------------------------

    /// <summary>
    /// Keeps carried/held items attached to whoever has them, with a slight bob
    /// so the loot reads as being held rather than fused to the sprite.
    /// </summary>
    public void Update(float dt, GridPos playerCell, float carryHeight)
    {
        foreach (var item in _items)
        {
            item.AgeSeconds += dt;
            if (item.AgeSeconds < 0f) item.AgeSeconds = 0f;

            switch (item.State)
            {
                case LootState.Carried when item.Carrier != null:
                {
                    Vector3 basePos = item.Carrier.WorldPos + new Vector3(0f, carryHeight, 0f);
                    item.WorldPos = basePos + new Vector3(0f, MathF.Sin(item.AgeSeconds * 6f) * 0.06f, 0f);
                    break;
                }
                case LootState.Held:
                {
                    Vector3 basePos = CellCenter(playerCell) + new Vector3(0f, carryHeight, 0f);
                    item.WorldPos = basePos + new Vector3(0f, MathF.Sin(item.AgeSeconds * 6f) * 0.06f, 0f);
                    break;
                }
                case LootState.Secured:
                {
                    // Stacked on the counter rather than in the player's hands,
                    // and lifted onto its top surface so it isn't half-buried
                    // in the slab.
                    Vector3 c = CellCenter(item.Cell);
                    item.WorldPos = new Vector3(c.X, GridMap.CounterHeight, c.Z);
                    break;
                }
            }
        }
    }

    /// <summary>True when every item is behind the counter (the win condition).</summary>
    public bool AllSecured => _items.Count > 0 && _items.All(i => i.IsSecured);
}
