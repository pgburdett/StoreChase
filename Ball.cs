using System;
using Microsoft.Xna.Framework;

namespace StoreChase;

// A thrown ball. It's the player's one offensive tool and, more usefully, a
// distraction: a cat that would rather chase a bouncing ball than risk a tackle
// is a cat being herded, which is what makes the store feel winnable.
//
// It travels, bounces off shelves, settles, then lies there being interesting
// for a few seconds. Once the interest runs out it despawns, so throwing one
// is a timed resource rather than a permanent lure.
public class Ball
{
    // Backing fields rather than auto-properties: the simulation writes single
    // components (Position.Y = ...) every frame, and you can't assign into a
    // property's field.
    public Vector3 Position => _position;
    public Vector3 Velocity => _velocity;

    private Vector3 _position;
    private Vector3 _velocity;

    public bool Active { get; private set; }
    public bool Settled { get; private set; }

    private float _cellSize;
    private float _lifeRemaining;
    private int _floorBounces;

    public const float ThrowSpeed = 14f;
    public const float BallRadius = 0.28f;
    public const float SettledLifetime = 7f;

    // Friction and bounce. Enough to look like a thrown toy rather than a
    // laser, and to lose height fast enough that it doesn't cross the store.
    private const float Gravity = 26f;
    private const float BounceDamping = 0.45f;
    // Rolling resistance, applied only while the ball is on the floor. Low
    // enough that a throw crosses a few aisles and keeps the cats interested.
    private const float GroundFriction = 1.5f;
    private const float MinBounceHeight = 0.35f;
    private const float WallDamping = 0.6f;
    private const int MaxFloorBounces = 6;
    private const float MaxSpeed = 24f;

    public void Configure(float cellSize) => _cellSize = cellSize;

    // Thrown along a world XZ heading, from just in front of the player.
    public void Throw(Vector3 origin, Vector2 heading)
    {
        if (heading.LengthSquared() < 0.0001f) heading = Vector2.UnitY;

        _position = origin;
        _velocity = new Vector3(heading.X, 0f, heading.Y) * ThrowSpeed;
        // A little loft so it clears the low shelf tops on the way out.
        _velocity.Y = 3.2f;
        Active = true;
        Settled = false;
        _floorBounces = 0;
        _lifeRemaining = SettledLifetime;
    }

    public void Clear()
    {
        Active = false;
        Settled = false;
        _lifeRemaining = 0f;
    }

    public void Update(float dt, GridMap map)
    {
        if (!Active) return;

        if (Settled)
        {
            _lifeRemaining -= dt;
            if (_lifeRemaining <= 0f) Clear();
            return;
        }

        // A cap so a long frame (a stall, a debugger breakpoint) can't
        // integrate into a velocity that launches the ball out of the store.
        float speed = _velocity.Length();
        if (speed > MaxSpeed) _velocity *= MaxSpeed / speed;

        // Remembered so a blocked step can be undone exactly. See ResolveAxis.
        Vector3 previous = _position;

        _velocity.Y -= Gravity * dt;
        _position += _velocity * dt;

        // Floor. Only the floor settles the ball — see ResolveAxis.
        if (_position.Y <= BallRadius)
        {
            _position.Y = BallRadius;
            if (MathF.Abs(_velocity.Y) < MinBounceHeight || ++_floorBounces > MaxFloorBounces)
            {
                _velocity.Y = 0f;
                Settled = true;
                _lifeRemaining = SettledLifetime;
            }
            else
            {
                _velocity.Y = -_velocity.Y * BounceDamping;
            }
        }

        // Shelves: reflect on the axis the ball crossed, which is enough for a
        // square grid and keeps the ball in the store.
        ResolveAxis(map, 0, previous);
        ResolveAxis(map, 2, previous);

        if (!Settled)
        {
            // Rolling resistance only. Applying it in mid-air (as an earlier
            // version did) bled off the throw almost immediately: at 3.2/s the
            // speed had a 0.3s time constant, so a ball thrown across the store
            // stopped after about 4 units and never got near an aisle. A thrown
            // ball should coast.
            if (_position.Y <= BallRadius + 0.05f)
            {
                var flat = new Vector2(_velocity.X, _velocity.Z);
                flat *= MathF.Max(0f, 1f - GroundFriction * dt);
                _velocity = new Vector3(flat.X, _velocity.Y, flat.Y);
            }
        }
    }

    // Undoes a step that would have taken the ball into a shelf, and reflects
    // it instead. The undo restores the *previous* position along that axis
    // rather than backing off a fixed distance: a fixed cell-sized back-off put
    // the ball into the wall behind it (measured — a ball thrown down an aisle
    // ended up one cell outside the store).
    //
    // Cell centres are at multiples of cellSize, so a single frame's movement is
    // well under one cell and can't tunnel through a shelf.
    //
    // This deliberately does not count toward the settle limit. An earlier
    // version shared one bounce counter with the floor, so a ball that clipped a
    // couple of shelves ran out of "bounces" and stopped dead in mid-air.
    // Hitting a wall should cost speed, not end the throw; the lifetime does
    // that.
    private void ResolveAxis(GridMap map, int axis, Vector3 previous)
    {
        int cx = (int)MathF.Floor(_position.X / _cellSize + 0.5f);
        int cy = (int)MathF.Floor(_position.Z / _cellSize + 0.5f);
        if (map.IsWalkable(cx, cy)) return;

        if (axis == 0)
        {
            _position.X = previous.X;
            _velocity.X = -_velocity.X * WallDamping;
        }
        else
        {
            _position.Z = previous.Z;
            _velocity.Z = -_velocity.Z * WallDamping;
        }
    }

    // The cell the ball is in, which is what the AI paths to.
    public GridPos Cell
    {
        get
        {
            int cx = (int)MathF.Floor(_position.X / _cellSize + 0.5f);
            int cy = (int)MathF.Floor(_position.Z / _cellSize + 0.5f);
            return new GridPos(cx, cy);
        }
    }
}
