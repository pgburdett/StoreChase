using System;
using Microsoft.Xna.Framework;

namespace StoreChase;

/// <summary>
/// A shopper wandering the aisles. Not a cat, not the player — a body that
/// moves cell to cell and, in the shoplifter's case, can pocket something and
/// make for the exit.
///
/// Movement is a *path*, not a jump. The brain hands over a destination
/// anywhere in the store, and the body walks it one adjacent cell at a time
/// using <see cref="GridMap.NextStepToward"/>. It used to lerp straight from
/// its cell to the destination in a single step, which meant a shopper that
/// picked an aisle across the store slid through the shelves and arrived in
/// about a third of a second — it read as teleporting.
///
/// Pure state (no GraphicsDevice): the game draws it and drives its decisions
/// through <see cref="CustomerBrain"/>, which is also pure.
/// </summary>
public class Customer
{
    public GridPos Cell { get; private set; }
    public Vector3 WorldPos { get; private set; }
    public bool IsMoving { get; private set; }
    public bool JustArrived { get; private set; }
    public bool IsShoplifter { get; }
    public LootItem? Carrying { get; set; }
    public bool Escaped { get; private set; }
    public bool Leaving { get; set; }
    public float FacingAngle { get; private set; }

    private readonly float _cellSize;
    private GridPos _from, _to;
    private float _t;
    private GridPos? _destination;
    private readonly Random _rng;

    public const float Speed = 2.6f;

    public Customer(GridPos start, float cellSize, bool shoplifter, Random rng)
    {
        Cell = start;
        _cellSize = cellSize;
        IsShoplifter = shoplifter;
        _rng = rng;
        WorldPos = new Vector3(start.X * cellSize, 0f, start.Y * cellSize);
        _from = _to = start;
    }

    /// <summary>The cell this customer is walking to, or null when idle.</summary>
    /// <remarks>
    /// This is the *destination*, which may be many cells away; the body takes
    /// one step at a time towards it. Callers that want "where is it going
    /// next" want <see cref="NextCell"/>, not this.
    /// </remarks>
    public GridPos? Target => _destination;

    /// <summary>The cell the current step ends on, or null when standing still.</summary>
    public GridPos? NextCell => IsMoving ? _to : null;

    /// <summary>Sets where the customer should walk to. Ignored while a step
    /// is already in flight, so a target change never interrupts a stride.</summary>
    public void SetTarget(GridPos? target, GridMap map)
    {
        if (IsMoving) return;

        if (target == null || target.Value == Cell)
        {
            _destination = null;
            return;
        }

        if (!map.IsWalkable(target.Value.X, target.Value.Y)) return;

        _destination = target.Value;
        StartNextStep(map);
    }

    public void Update(float dt, GridMap map)
    {
        JustArrived = false;

        // How many cells' worth of walking this frame can pay for. A 60fps
        // frame is far less than one step, so the loop normally runs once and
        // starts (or continues) a single stride. It matters for a large dt —
        // a hitch or a harness call — which must still walk the path rather
        // than skip to the end of it.
        float budget = dt * Speed;

        while (budget > 0f)
        {
            if (!IsMoving && !StartNextStep(map))
                break;

            float need = 1f - _t;
            if (budget < need)
            {
                _t += budget;
                budget = 0f;
                break;
            }

            // Finish this step.
            budget -= need;
            _t = 1f;
            IsMoving = false;
            Cell = _to;
            JustArrived = true;
            FacingAngle = MathF.Atan2(_to.X - _from.X, _to.Y - _from.Y);
            WorldPos = new Vector3(Cell.X * _cellSize, 0f, Cell.Y * _cellSize);

            if (_destination == Cell)
            {
                _destination = null;
                break;
            }
            // Otherwise loop and take the next step towards the destination.
        }

        if (IsMoving)
        {
            float x = MathHelper.Lerp(_from.X * _cellSize, _to.X * _cellSize, _t);
            float z = MathHelper.Lerp(_from.Y * _cellSize, _to.Y * _cellSize, _t);
            WorldPos = new Vector3(x, 0f, z);
        }
        else
        {
            WorldPos = new Vector3(Cell.X * _cellSize, 0f, Cell.Y * _cellSize);
        }
    }

    /// <summary>Begins the next adjacent step towards the destination.
    /// Returns false when there is nowhere left to go — no destination, we are
    /// standing on it, or the map has no path (which drops the destination so
    /// the customer does not spin on the spot forever).</summary>
    private bool StartNextStep(GridMap map)
    {
        if (_destination == null) return false;

        if (Cell == _destination.Value)
        {
            _destination = null;
            return false;
        }

        var step = map.NextStepToward(Cell, _destination.Value);
        if (step == null || step.Value == Cell)
        {
            _destination = null;
            return false;
        }

        _from = Cell;
        _to = step.Value;
        _t = 0f;
        IsMoving = true;
        JustArrived = false;
        return true;
    }

    public void MarkEscaped() => Escaped = true;
}
