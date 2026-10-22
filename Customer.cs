using System;
using Microsoft.Xna.Framework;

namespace StoreChase;

/// <summary>
/// A shopper wandering the aisles. Not a cat, not the player — a body that
/// moves cell to cell and, in the shoplifter's case, can pocket something and
/// make for the exit.
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
    private bool _hasTarget;
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

    public GridPos? Target => _hasTarget ? _to : null;

    public void SetTarget(GridPos? target, GridMap map)
    {
        if (IsMoving) return;
        if (target == null) { _hasTarget = false; return; }
        if (!map.IsWalkable(target.Value.X, target.Value.Y)) return;
        if (target.Value == Cell) { _hasTarget = false; return; }
        _from = Cell;
        _to = target.Value;
        _t = 0f;
        _hasTarget = true;
        IsMoving = true;
        JustArrived = false;
    }

    public void Update(float dt, GridMap map)
    {
        JustArrived = false;
        if (!IsMoving) return;
        _t += dt * Speed;
        if (_t >= 1f)
        {
            _t = 1f;
            IsMoving = false;
            Cell = _to;
            _hasTarget = false;
            JustArrived = true;
            WorldPos = new Vector3(Cell.X * _cellSize, 0f, Cell.Y * _cellSize);
            FacingAngle = MathF.Atan2(_to.X - _from.X, _to.Y - _from.Y);
        }
        else
        {
            float x = MathHelper.Lerp(_from.X * _cellSize, _to.X * _cellSize, _t);
            float z = MathHelper.Lerp(_from.Y * _cellSize, _to.Y * _cellSize, _t);
            WorldPos = new Vector3(x, 0f, z);
        }
    }

    public void MarkEscaped() => Escaped = true;
}
