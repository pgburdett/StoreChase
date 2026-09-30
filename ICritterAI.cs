using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;

namespace StoreChase;

/// <summary>
/// Everything a critter's AI is allowed to know about the world for one
/// decision. Deliberately a value type passed by copy: the AI can't reach back
/// into the game, and the tests/tools can construct a context by hand.
/// </summary>
public record struct CritterContext(
    GridPos CurrentCell,
    GridPos PlayerCell,
    GridMap Map,
    float DeltaTime,

    /// <summary>Whether this critter currently has line of sight to the player,
    /// within its own vision radius. Gates the alert/flee tiers: a cat that
    /// hasn't seen you isn't reacting to you.</summary>
    bool CanSeePlayer,

    /// <summary>Whether the player's camera is pointed at this critter. This is
    /// what makes the first-person view a tactic — you can pin a Nervous cat in
    /// place just by looking at it.</summary>
    bool BeingWatched,

    /// <summary>Cell of a resting thrown ball, if one exists and this critter
    /// cares about it.</summary>
    GridPos? Ball,

    /// <summary>A loose item lying on the critter's own cell, i.e. one it could
    /// pick up right now by not moving.</summary>
    LootItem? LootUnderfoot,

    /// <summary>Cell of the nearest loose item, or null when the store is
    /// picked clean.</summary>
    GridPos? LootTarget,

    /// <summary>Whether this critter is currently carrying something.</summary>
    bool Carrying,

    /// <summary>How many items are off the shelves in total, so a cat can tell
    /// whether the store is worth raiding.</summary>
    int StolenCount
);

/// <summary>
/// The decision-making half of a critter. Given a <see cref="CritterContext"/>
/// (a snapshot of the world from the critter's perspective), it returns the
/// next cell to move to, whether it wants to steal what's underfoot, and what
/// pose the animation rig should target.
///
/// Implementations must be stateless with respect to the game — any memory
/// (paths, timers) lives on the AI object itself, not in the context.
/// </summary>
public interface ICritterAI
{
    /// <summary>
    /// Analyzes the context and decides the next destination.
    /// </summary>
    /// <returns>The target GridPos, or null if no movement is required.</returns>
    GridPos? DecideNextMove(CritterContext context);

    /// <summary>
    /// True when the AI wants to pick up <see cref="CritterContext.LootUnderfoot"/>
    /// on the step it just decided. Set by DecideNextMove; the game performs the
    /// transfer. Splitting it this way keeps possession rules out of the AI.
    /// </summary>
    bool WantsToSteal { get; }

    /// <summary>What the AI wants the body to be doing, for the animation rig.</summary>
    CatPose Pose { get; }
}
