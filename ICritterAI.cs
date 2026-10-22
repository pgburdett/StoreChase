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

    /// <summary>
    /// The nearest loose item, or null when the store is picked clean.
    ///
    /// This is the same item as <see cref="LootTarget"/>, but with its kind
    /// attached, so the AI can decide whether it actually wants it. A cat
    /// that only steals the boxed goods should path to a box and ignore a
    /// tin; that is not something a cell on its own can express.
    /// </summary>
    LootItem? LootTargetItem,

    /// <summary>Whether this critter is currently carrying something.</summary>
    bool Carrying,

    /// <summary>How many items are off the shelves in total, so a cat can tell
    /// whether the store is worth raiding.</summary>
    int StolenCount,

    /// <summary>
    /// How alarmed the store is.
    ///
    /// This is the round's mood. At <see cref="AlarmStage.Suspicious"/> the
    /// cats get braver — more likely to grab goods they do not particularly
    /// want. At <see cref="AlarmStage.Critical"/> they stop playing about and
    /// go for the goods whatever they feel like, which is the moment the
    /// store is most likely to be emptied out from under the player.
    /// </summary>
    AlarmStage AlarmStage,

    /// <summary>
    /// How unsure this cat is about where the player is.
    ///
    /// Zero means it knows exactly — it can see them. A larger number means
    /// a vaguer idea: it heard about the player from another cat, and the
    /// news travelled a distance and degraded on the way (see
    /// <see cref="SharedAwareness"/>).
    ///
    /// This is what makes one cat spotting the player matter to the others.
    /// </summary>
    float PlayerAwareness,

    /// <summary>
    /// The last place this cat saw the player, if it has seen them at all.
    ///
    /// A cat that has lost sight of the player but knows roughly where they
    /// were will head for that spot rather than wandering off.
    /// </summary>
    GridPos? LastSeenPlayerCell,

    /// <summary>
    /// Cell of a dropped treat, if one exists.
    ///
    /// Like the ball, but louder: several cats will break off what they are
    /// doing to converge on it. A cat in the middle of an escape still
    /// ignores it — a thief that abandons the loot because of a snack is a
    /// mechanic, not a cat.
    /// </summary>
    GridPos? Treat = null,

    /// <summary>
    /// Seconds left of panic (from a spray bottle).
    ///
    /// A panicking cat flees the player regardless of its own radii and
    /// ignores the merchandise. It is the player's way to clear a crowded
    /// aisle without hurting anyone's loot.
    /// </summary>
    float Panic = 0f,

    /// <summary>
    /// Whether this cat has been fed into friendship (two treats — see
    /// <see cref="Critter.FriendThreshold"/>).
    ///
    /// A friend is neither a thief nor prey: it never steals, and it
    /// shadows the player around the store instead.
    /// </summary>
    bool Befriended = false
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

    /// <summary>
    /// True when the AI wants the dropped treat underfoot — it is standing
    /// on it and eating is what it wants right now. Set by DecideNextMove;
    /// the game performs the feed (friendship points, consuming the treat),
    /// same split as stealing.
    /// </summary>
    bool WantsTreat { get; }

    /// <summary>What the AI wants the body to be doing, for the animation rig.</summary>
    CatPose Pose { get; }
}
