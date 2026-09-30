using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;

namespace StoreChase;

/// <summary>The ball's current state in its throw lifecycle.</summary>
public enum BallState
{
    /// <summary>With the player, ready to throw.</summary>
    Held,

    /// <summary>Bouncing off shelves, checking for a cat hit or a return.</summary>
    Thrown,

    /// <summary>Pieces flying out along 8 axes from the contact point.</summary>
    Exploding,

    /// <summary>A beat of nothing, so the explosion reads.</summary>
    Paused,

    /// <summary>Pieces converging on the player, ball materializes.</summary>
    Returning,
}

public readonly record struct Particle(
    Vector3 Position,
    Vector3 Velocity,
    float Life,
    float MaxLife,
    Color Color);

// A thrown ball. It's the player's one offensive tool: it bounces off shelves
// until it returns to the player or hits a cat. A hit stuns the cat and triggers
// an explosion; after a pause the ball materializes back at the player with a
// reverse explosion.
//
// The state machine:
//   Thrown     — bouncing off walls, checking for a cat hit or a return
//   Exploding  — pieces fly out along 8 axes from the contact point
//   Paused     — a beat of nothing, so the explosion reads
//   Returning  — pieces converge on the player, ball materializes
//   Held       — back with the player, ready to throw again
/// <summary>
/// A thrown ball. It's the player's one offensive tool: it bounces off shelves
/// until it returns to the player or hits a cat. A hit stuns the cat and triggers
/// an explosion; after a pause the ball materializes back at the player with a
/// reverse explosion.
///
/// The state machine:
///   Thrown     — bouncing off walls, checking for a cat hit or a return
///   Exploding  — pieces fly out along 8 axes from the contact point
///   Paused     — a beat of nothing, so the explosion reads
///   Returning  — pieces converge on the player, ball materializes
///   Held       — back with the player, ready to throw again
/// </summary>
public class Ball
{
    // Backing fields rather than auto-properties: the simulation writes single
    // components (Position.Y = ...) every frame, and you can't assign into a
    // property's field.
    /// <summary>The ball's current world position.</summary>
    public Vector3 Position => _position;

    /// <summary>The ball's current velocity.</summary>
    public Vector3 Velocity => _velocity;

    private Vector3 _position;
    private Vector3 _velocity;

    /// <summary>The ball's current state.</summary>
    public BallState State { get; private set; } = BallState.Held;

    /// <summary>True while the ball is in play (not held).</summary>
    public bool Active => State != BallState.Held;

    /// <summary>True when the ball is back with the player and ready to throw.</summary>
    public bool Settled => State == BallState.Held;

    private float _cellSize;
    private int _floorBounces;
    private float _stateTimer;
    private float _thrownLifetime;

    private readonly List<Particle> _particles = new();

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

    // Explosion timing.
    private const float ExplodeDuration = 0.5f;
    private const float PauseDuration = 1.0f;
    private const float ReturnDuration = 0.5f;
    private const float MaxThrownLifetime = 10f;
    private const float ParticleSpeed = 6f;
    private const float ParticleLife = 0.4f;
    private const float ReturnRadius = 2.5f;
    private const int ParticlesPerDirection = 3;

    // 8 directions in the XZ plane: N, NE, E, SE, S, SW, W, NW.
    private static readonly Vector2[] Directions =
    {
        new(0, 1), new(1, 1), new(1, 0), new(1, -1),
        new(0, -1), new(-1, -1), new(-1, 0), new(-1, 1)
    };

    /// <summary>Sets the grid cell size used for collision and cell lookups.</summary>
    public void Configure(float cellSize) => _cellSize = cellSize;

    /// <summary>
    /// Throws the ball from the given origin along the given world XZ heading.
    /// A little loft is added so it clears the low shelf tops on the way out.
    /// </summary>
    public void Throw(Vector3 origin, Vector2 heading)
    {
        if (heading.LengthSquared() < 0.0001f) heading = Vector2.UnitY;

        _position = origin;
        _velocity = new Vector3(heading.X, 0f, heading.Y) * ThrowSpeed;
        // A little loft so it clears the low shelf tops on the way out.
        _velocity.Y = 3.2f;
        State = BallState.Thrown;
        _floorBounces = 0;
        _thrownLifetime = 0f;
        _particles.Clear();
    }

    /// <summary>Returns the ball to the Held state (used on round restart).</summary>
    public void Clear()
    {
        State = BallState.Held;
        _particles.Clear();
    }

    /// <summary>
    /// Called by the game when the ball reaches a cat. Spawns the explosion
    /// and moves to the Exploding state.
    /// </summary>
    public void Explode()
    {
        if (State != BallState.Thrown) return;
        State = BallState.Exploding;
        _stateTimer = 0f;
        SpawnExplosionParticles(_position);
    }

    /// <summary>
    /// Called by the game when the ball returns to the player (or its lifetime
    /// runs out). Spawns the reverse explosion at the player's position and
    /// moves to the Returning state.
    /// </summary>
    public void ReturnToPlayer(Vector3 playerPos)
    {
        if (State != BallState.Thrown) return;
        State = BallState.Returning;
        _stateTimer = 0f;
        _position = playerPos;
        SpawnReturnParticles(playerPos);
    }

    /// <summary>
    /// Advances the ball one frame. The behaviour depends on the current state:
    /// Thrown integrates physics and checks for a cat hit or return; the other
    /// states just run their timers.
    /// </summary>
    public void Update(float dt, GridMap map, GridPos playerCell)
    {
        switch (State)
        {
            case BallState.Thrown:
                UpdateThrown(dt, map, playerCell);
                break;
            case BallState.Exploding:
                UpdateExploding(dt);
                break;
            case BallState.Paused:
                UpdatePaused(dt);
                break;
            case BallState.Returning:
                UpdateReturning(dt);
                break;
        }
        UpdateParticles(dt);
    }

    private void UpdateThrown(float dt, GridMap map, GridPos playerCell)
    {
        _thrownLifetime += dt;
        if (_thrownLifetime > MaxThrownLifetime)
        {
            ReturnToPlayer(_position);
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

        // Back at the player's cell: the ball returns. But not on the first
        // frame — the ball starts in the player's cell, so this needs a grace
        // period to let it leave.
        if (_thrownLifetime > 0.5f && Cell == playerCell)
            ReturnToPlayer(_position);
    }

    private void UpdateExploding(float dt)
    {
        _stateTimer += dt;
        if (_stateTimer >= ExplodeDuration)
        {
            State = BallState.Paused;
            _stateTimer = 0f;
        }
    }

    private void UpdatePaused(float dt)
    {
        _stateTimer += dt;
        if (_stateTimer >= PauseDuration)
        {
            State = BallState.Returning;
            _stateTimer = 0f;
        }
    }

    private void UpdateReturning(float dt)
    {
        _stateTimer += dt;
        if (_stateTimer >= ReturnDuration)
        {
            State = BallState.Held;
            _particles.Clear();
        }
    }

    private void UpdateParticles(float dt)
    {
        for (int i = _particles.Count - 1; i >= 0; i--)
        {
            var p = _particles[i];
            p = p with { Life = p.Life - dt };
            if (p.Life <= 0f)
            {
                _particles.RemoveAt(i);
                continue;
            }
            p = p with { Life = p.Life - dt, Position = p.Position + p.Velocity * dt };
            _particles[i] = p;
        }
    }

    private void SpawnExplosionParticles(Vector3 origin)
    {
        var color = new Color(255, 180, 60);
        foreach (var dir in Directions)
        {
            for (int i = 0; i < ParticlesPerDirection; i++)
            {
                float speed = ParticleSpeed * (0.8f + 0.4f * (i / (float)ParticlesPerDirection));
                _particles.Add(new Particle(
                    origin,
                    new Vector3(dir.X * speed, 2f, dir.Y * speed),
                    ParticleLife,
                    ParticleLife,
                    color));
            }
        }
    }

    private void SpawnReturnParticles(Vector3 playerPos)
    {
        var color = new Color(120, 200, 255);
        foreach (var dir in Directions)
        {
            for (int i = 0; i < ParticlesPerDirection; i++)
            {
                float offset = ReturnRadius * (0.8f + 0.4f * (i / (float)ParticlesPerDirection));
                var start = playerPos + new Vector3(dir.X * offset, 1f, dir.Y * offset);
                var velocity = new Vector3(-dir.X * ParticleSpeed, -2f, -dir.Y * ParticleSpeed);
                _particles.Add(new Particle(
                    start,
                    velocity,
                    ReturnDuration,
                    ReturnDuration,
                    color));
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

    /// <summary>The cell the ball is in, which is what the AI paths to.</summary>
    public GridPos Cell
    {
        get
        {
            int cx = (int)MathF.Floor(_position.X / _cellSize + 0.5f);
            int cy = (int)MathF.Floor(_position.Z / _cellSize + 0.5f);
            return new GridPos(cx, cy);
        }
    }

    /// <summary>The current explosion/return particles.</summary>
    public IReadOnlyList<Particle> Particles => _particles;
}
