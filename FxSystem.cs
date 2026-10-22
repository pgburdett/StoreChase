using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;

namespace StoreChase;

/// <summary>
/// A speck of dust, sparkle or debris flying through the air. Pure data —
/// the graphics-free half of the game's juice, so the harness can simulate a
/// round's worth of bursts and assert that they all die out instead of
/// leaking or producing NaNs that would only show up as a pink screen.
/// </summary>
public readonly record struct FxParticle(
    Vector3 Position,
    Vector3 Velocity,
    float Life,
    float MaxLife,
    Color Color)
{
    /// <summary>0 = just born, 1 = about to die. Used for fading.</summary>
    public float Age => 1f - Math.Clamp(Life / MaxLife, 0f, 1f);
}

/// <summary>
/// A line of floating text at a world position — "+1 secured", "SLIP!", the
/// alarm cost of pulling the fire alarm. Rises and fades; the HUD projects
/// it to the screen each frame.
/// </summary>
public readonly record struct FxText(
    Vector3 WorldPos,
    string Text,
    Color Color,
    float Life,
    float MaxLife)
{
    /// <summary>1 = full strength, 0 = gone.</summary>
    public float Strength => Math.Clamp(Life / MaxLife, 0f, 1f);
}

/// <summary>
/// Owns every particle and floating text the game has going at once.
///
/// <para>Two principles, learned the easy way from the ball's own particle
/// system: everything dies (every particle carries its own MaxLife and is
/// removed at zero, so a burst can never accumulate into a leak), and
/// nothing is ever drawn by this class — <see cref="Game1"/> draws
/// particles as small cubes, <see cref="Hud"/> projects texts, and this
/// file stays free of <c>GraphicsDevice</c> so all of it is testable from a
/// terminal.</para>
/// </summary>
public sealed class FxSystem
{
    private readonly List<FxParticle> _particles = new();
    private readonly List<FxText> _texts = new();

    /// <summary>Cap so a frame full of events can't run away with memory.</summary>
    private const int MaxParticles = 400;
    private const int MaxTexts = 40;

    /// <summary>All live particles, oldest first.</summary>
    public IReadOnlyList<FxParticle> Particles => _particles;

    /// <summary>All live floating texts.</summary>
    public IReadOnlyList<FxText> Texts => _texts;

    /// <summary>
    /// Throws <paramref name="count"/> specks outward from a point.
    /// Used for dust on a landing, sparkles on a secure, debris on a tackle.
    /// </summary>
    public void Burst(Vector3 position, Color color, int count, float speed, float life, Random rng)
    {
        for (int i = 0; i < count && _particles.Count < MaxParticles; i++)
        {
            // A random unit-ish direction biased upward: floor puffs rise and
            // drift, they don't sink into the ground.
            float angle = (float)(rng.NextDouble() * Math.PI * 2);
            float lift = (float)rng.NextDouble() * 0.9f + 0.25f;
            var velocity = new Vector3(MathF.Cos(angle) * speed, lift * speed, MathF.Sin(angle) * speed);

            float variance = 0.6f + (float)rng.NextDouble() * 0.8f;
            _particles.Add(new FxParticle(
                position, velocity, life * variance, life * variance, color));
        }
    }

    /// <summary>A small grey puff — footsteps, landings, skids.</summary>
    public void Dust(Vector3 position, Random rng) =>
        Burst(position, new Color(190, 186, 178), count: 6, speed: 1.4f, life: 0.5f, rng);

    /// <summary>A bright burst — securing loot, pocketing a power-up.</summary>
    public void Sparkle(Vector3 position, Color color, Random rng) =>
        Burst(position, color, count: 14, speed: 2.6f, life: 0.7f, rng);

    /// <summary>
    /// A floating line of text that rises off <paramref name="position"/>
    /// and fades. The line the player reads a beat after the event.
    /// </summary>
    public void Text(Vector3 position, string text, Color color, float life = 1.4f)
    {
        // Keep the newest ones: a text nobody can see because forty others
        // are stacked on it is worse than a dropped line.
        if (_texts.Count >= MaxTexts) _texts.RemoveAt(0);
        _texts.Add(new FxText(position, text, color, life, life));
    }

    /// <summary>Advances every particle and text; removes the dead.</summary>
    public void Update(float dt)
    {
        for (int i = _particles.Count - 1; i >= 0; i--)
        {
            var p = _particles[i];
            float life = p.Life - dt;
            if (life <= 0f)
            {
                _particles.RemoveAt(i);
                continue;
            }

            // Decelerating drift with a hint of gravity: sparks arc, dust
            // slows to a hover and vanishes. Velocity damping (not friction
            // per step) keeps the motion independent of frame rate.
            var velocity = p.Velocity * MathF.Exp(-2.5f * dt);
            velocity.Y -= 1.6f * dt;
            _particles[i] = p with
            {
                Position = p.Position + velocity * dt,
                Velocity = velocity,
                Life = life,
            };
        }

        for (int i = _texts.Count - 1; i >= 0; i--)
        {
            var t = _texts[i];
            float life = t.Life - dt;
            if (life <= 0f)
            {
                _texts.RemoveAt(i);
                continue;
            }

            // Rise and slow: the classic "+1" arc, without a second of hang
            // time at the top.
            _texts[i] = t with
            {
                WorldPos = t.WorldPos + new Vector3(0f, 1.1f * dt, 0f),
                Life = life,
            };
        }
    }

    /// <summary>Clears everything (round restart).</summary>
    public void Clear()
    {
        _particles.Clear();
        _texts.Clear();
    }
}
