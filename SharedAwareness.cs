using System;
using System.Collections.Generic;
using System.Linq;

namespace StoreChase;

/// <summary>
/// What the cats know about the player, shared between them.
///
/// Every critter used to work out "can I see the player?" on its own, which
/// made each cat an island: one cat spotting you had no effect on the others
/// two aisles away. That is not how a store full of cats works — one of them
/// seeing you is enough for the others to get twitchy.
///
/// So the knowledge is pooled here. A critter that sees the player raises the
/// alarm, and the alarm reaches the other cats, attenuated by distance. The
/// value carried is a *distance*, not a truth: a cat that has been told
/// about the player knows roughly where they are, and how roughly depends on
/// how far away the sighting was.
///
/// This is deliberately a plain class rather than part of the critter or the
/// AI: it is a fact about the world that the game owns and hands to the
/// critters, in the same way the loot and the ball are facts about the world.
/// </summary>
public sealed class SharedAwareness
{
    /// <summary>
    /// How far the news of a sighting travels, in cells.
    ///
    /// A cat two aisles away can hear you; one across the store cannot.
    /// </summary>
    public const float ShoutRadius = 8f;

    /// <summary>
    /// How far a guard dog's bark carries. Louder than a normal cat's
    /// shout, so a dog that spots you alerts the whole aisle block.
    /// </summary>
    public const float BarkRadius = 12f;

    /// <summary>
    /// How much of the news survives the distance.
    ///
    /// Awareness is a distance in cells, so a distant sighting is a vaguer
    /// fact than a close one. A factor of one means "as far away as the
    /// sighting was"; less than one means the news degrades.
    /// </summary>
    private const float Decay = 0.75f;

    private readonly List<Critter> _critters = new();

    /// <summary>
    /// The cats this pool covers. Set by the game each frame.
    /// </summary>
    public IReadOnlyList<Critter> Critters => _critters;

    /// <summary>
    /// Rebuilds the pool for a frame.
    ///
    /// The list is rebuilt rather than mutated because cats are added and
    /// removed as they are tackled.
    /// </summary>
    public void Reset(IReadOnlyList<Critter> critters)
    {
        _critters.Clear();
        _critters.AddRange(critters);
    }

    /// <summary>
    /// Updates what every cat knows.
    ///
    /// Each cat that can see the player raises the alarm; every cat hears it
    /// in proportion to how close it is to the cat that saw. A cat that is
    /// told nothing by the others may still hear the player directly: the
    /// noise the player makes is a fix all of its own, filtered through this
    /// cat's own ears (<see cref="CritterProfile.HearingRadius"/>).
    ///
    /// The value carried is *uncertainty*, not distance: zero means the cat
    /// knows exactly where the player is, and larger numbers mean a vaguer
    /// idea. A cat that saw the player itself has no uncertainty at all; a
    /// cat two aisles away that heard about it — or heard the player
    /// clattering around — has a rough idea and a rough idea has a wide
    /// error bar.
    /// </summary>
    /// <param name="playerCell">Where the player is.</param>
    /// <param name="map">The map, for walking distance.</param>
    /// <param name="noise">The store's current noises, if any. Null means
    /// hearing is off for the frame (the headless round simulation passes
    /// null and plays a completely silent player).</param>
    public void Update(GridPos playerCell, GridMap map, NoiseSystem? noise = null)
    {
        if (_critters.Count == 0) return;

        // Who can see the player, and how far away they are.
        var sightings = new List<(Critter who, float distance)>();
        foreach (var c in _critters)
        {
            if (!c.CanSeePlayer) continue;
            sightings.Add((c, ManhattanDistance(c.Cell, playerCell)));
        }

        foreach (var cat in _critters)
        {
            // The best guess this cat has, and how unsure it is about it.
            float uncertainty = float.MaxValue;
            GridPos? lastSeen = cat.LastSeenPlayerCell;

            // Seeing the player yourself is exact: no uncertainty at all.
            foreach (var (who, distance) in sightings)
            {
                if (who != cat) continue;
                uncertainty = 0f;
                lastSeen = playerCell;
            }

            // Hearing about it is a rough fix: the further the news travelled
            // and the further away the sighting was, the rougher it is.
            foreach (var (who, distance) in sightings)
            {
                if (who == cat) continue;

                // A guard dog's bark carries further than a normal cat's
                // shout, so its sighting reaches cats further down the aisles.
                float radius = who.Profile.ChasesPlayer ? BarkRadius : ShoutRadius;

                // How far the news has to travel, in cells.
                float apart = ManhattanDistance(cat.Cell, who.Cell);
                if (apart > radius) continue;

                // The further the news travels, the vaguer it gets. A cat
                // that was nearly there gets a better fix than one across
                // the store.
                float carried = distance * (0.5f + 0.5f * (apart / radius)) * Decay;
                if (carried < uncertainty)
                {
                    uncertainty = carried;
                    lastSeen = who.LastSeenPlayerCell ?? playerCell;
                }
            }

            // Hearing the player move is the weakest source of news — no
            // cat relayed it, it is just a sound — so it only counts when
            // this frame brought nothing better, and only when it beats the
            // fix the cat already holds.
            float heardVagueness = float.MaxValue;
            GridPos? heardCell = null;
            if (noise != null && cat.Profile.HearingRadius > 0f)
            {
                var sensation = noise.LoudestHeardBy(cat.Cell, cat.Profile.HearingRadius);
                if (sensation.HasValue)
                {
                    heardCell = sensation.Value.Cell;
                    heardVagueness = sensation.Value.Vagueness;
                }
            }

            if (uncertainty < float.MaxValue)
            {
                cat.PlayerAwareness = uncertainty;
                if (lastSeen.HasValue) cat.LastSeenPlayerCell = lastSeen;
            }
            // A cat that knows nothing (no fix yet — PlayerAwareness 0 with
            // no LastSeen is the spawn state, not an exact fix) simply takes
            // the hearing; one that already holds a fix only takes it if it
            // is better. Sight beats both: it writes in the branch above and
            // a vagueness is never below zero.
            else if (heardCell.HasValue
                     && (cat.LastSeenPlayerCell == null || heardVagueness < cat.PlayerAwareness))
            {
                cat.PlayerAwareness = heardVagueness;
                cat.LastSeenPlayerCell = heardCell;
            }
        }
    }

    private static int ManhattanDistance(GridPos a, GridPos b) =>
        Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y);
}
