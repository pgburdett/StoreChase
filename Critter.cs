using System;
using Microsoft.Xna.Framework;

namespace StoreChase;

public class Critter
{
    public GridPos Cell { get; private set; }
    public Vector3 WorldPos { get; private set; }
    public bool Caught { get; private set; }
    public float FacingAngle { get; private set; }
    public Quaternion Rotation { get; private set; } = Quaternion.Identity;

    private GridPos _from;
    private GridPos _to;
    private float _t;
    private bool _isMoving;
    private float _moveSpeed = 4.5f;

    private readonly ICritterAI _ai;

    public Critter(GridPos start, float cellSize, ICritterAI ai)
    {
        Cell = start;
        _from = start;
        _to = start;
        _ai = ai;
        WorldPos = new Vector3(start.X * cellSize, 0f, start.Y * cellSize);
    }

    public void Update(GameTime gameTime, GridMap map, float cellSize, GridPos playerCell)
    {
        if (Caught) return;

        float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;

        // 1. Decision Phase
        if (!_isMoving)
        {
            var context = new CritterContext(Cell, playerCell, map, dt);
            GridPos? nextMove = _ai.DecideNextMove(context);

            if (nextMove.HasValue)
            {
                _from = Cell;
                _to = nextMove.Value;
                _t = 0f;
                _isMoving = true;
            }
        }

        // 2. Movement Phase
        if (_isMoving)
        {
            _t += dt * _moveSpeed;
            if (_t >= 1f)
            {
                _t = 1f;
                _isMoving = false;
                Cell = _to;
                if (Cell == playerCell) Caught = true;
            }

            Vector3 fromPos = new(_from.X * cellSize, 0f, _from.Y * cellSize);
            Vector3 toPos = new(_to.X * cellSize, 0f, _to.Y * cellSize);
            WorldPos = Vector3.Lerp(fromPos, toPos, _t);

            Vector3 delta = toPos - fromPos;
            if (delta.LengthSquared() > 0.0001f)
                FacingAngle = (float)Math.Atan2(delta.X, delta.Z);
        }

        // 3. Visual Phase
        UpdateRotation(dt);
    }

    private void UpdateRotation(float dt)
    {
        Quaternion targetRotation = Quaternion.CreateFromYawPitchRoll(FacingAngle, 0f, 0f);
        float turnT = 1f - MathF.Exp(-10f * dt);
        Rotation = Quaternion.Slerp(Rotation, targetRotation, turnT);
    }
}