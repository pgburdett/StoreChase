using System;
using System.Collections.Generic;

namespace StoreChase;

/// <summary>
/// The player's footsteps and clatters, as the store hears them.
///
/// The model is deliberately simple and physical: moving makes *pops* — one
/// footstep per cell entered, plus a big one when you go down on a spill —
/// and each pop reaches a cat if the cat is within (its hearing radius x the
/// pop's loudness) cells, fading out over <see cref="PopLife"/> seconds.
///
/// What the cat gets back is not knowledge, it's a fix: the cell the sound
/// came from, and a vagueness (at least 1 — a footstep is never a bearing).
/// The game folds that into <see cref="Critter.PlayerAwareness"/>, the same
/// vagueness a shouted warning carries, so a cat that hears you behaves
/// exactly like one that was told about you: it heads for the rough area and
/// gives up when nothing new arrives. Standing still is silence.
///
/// This is a pure state container: no GraphicsDevice, no game objects, no
/// randomness. The loudness values are set by the game (skates, spills and
/// a future quiet-shoes upgrade all multiply through there), and the
/// per-species ear size is the profile's HearingRadius.
/// </summary>
public sealed class NoiseSystem
{
    /// <summary>How long a footstep stays audible, in seconds.</summary>
    public const float PopLife = 1.3f;

    /// <summary>How long a clatter (a spill crash) stays audible.</summary>
    public const float ClatterLife = 2.2f;

    /// <summary>Loudness of one plain footstep. The unit everything scales from.</summary>
    public const float StepLoudness = 1f;

    /// <summary>Loudness multiplier while skidding across a spill. Loud because
    /// out of control: crashing into a shelf end is not a footstep.</summary>
    public const float SlipLoudness = 1.8f;

    private struct Pop
    {
        public readonly GridPos Cell;
        public readonly float Loudness;
        public readonly float Life;
        public float Age;

        public Pop(GridPos cell, float loudness, float life)
        {
            Cell = cell;
            Loudness = loudness;
            Life = life;
            Age = 0f;
        }

        public void AgeBy(float dt) => Age += dt;
        public float Fade => MathF.Max(0f, 1f - Age / Life);
    }

    private readonly List<Pop> _pops = new();

    /// <summary>How many pops are still audible. For the HUD's noise readout
    /// and the tests.</summary>
    public int ActivePops => _pops.Count;

    /// <summary>Loudness of the loudest audible pop, or 0 in silence. What the
    /// HUD's noise pip reads.</summary>
    public float CurrentLoudness
    {
        get
        {
            float best = 0f;
            foreach (var p in _pops)
                best = MathF.Max(best, p.Loudness * p.Fade);
            return best;
        }
    }

    /// <summary>One footstep at a cell. Loudness 0 is silence and stores nothing.</summary>
    public void Footstep(GridPos cell, float loudness)
    {
        if (loudness <= 0f) return;
        _pops.Add(new Pop(cell, loudness, PopLife));
    }

    /// <summary>A big noise (a spill crash). Louder and longer-lived than a step.</summary>
    public void Clatter(GridPos cell, float loudness) =>
        _pops.Add(new Pop(cell, loudness, ClatterLife));

    /// <summary>Ages every pop and forgets the ones that have gone quiet.</summary>
    public void Update(float dt)
    {
        for (int i = _pops.Count - 1; i >= 0; i--)
        {
            var p = _pops[i];
            p.AgeBy(dt);
            if (p.Age >= p.Life) _pops.RemoveAt(i);
            else _pops[i] = p;
        }
    }

    /// <summary>Drops every pop (a new round starts in silence).</summary>
    public void Clear() => _pops.Clear();

    /// <summary>
    /// What a cat at <paramref name="cat"/> hears right now.
    ///
    /// Returns the cell the loudest audible sound came from and a vagueness
    /// (>= 1; a footstep is a fix, not a bearing), or null when the cat hears
    /// nothing. <paramref name="hearRadius"/> is the species' hearing radius
    /// in cells at unit loudness — a Mouser hears a plain footstep further
    /// than a sleeping Moggy does.
    /// </summary>
    public (GridPos Cell, float Vagueness)? LoudestHeardBy(GridPos cat, float hearRadius)
    {
        (GridPos, float)? best = null;
        float bestStrength = float.MinValue;

        foreach (var p in _pops)
        {
            float range = hearRadius * p.Loudness * p.Fade;
            if (range <= 0f) continue;

            float dist = Manhattan(cat, p.Cell);
            if (dist > range) continue;

            // Closer and fresher is a better fix; ties go to the louder pop.
            float strength = p.Loudness * p.Fade - dist;
            if (strength > bestStrength)
            {
                bestStrength = strength;
                best = (p.Cell, MathF.Max(1f, dist));
            }
        }

        return best;
    }

    private static float Manhattan(GridPos a, GridPos b) =>
        MathF.Abs(a.X - b.X) + MathF.Abs(a.Y - b.Y);
}
