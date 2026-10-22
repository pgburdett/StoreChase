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
    /// in proportion to how close it is to the cat that saw.
    ///
    /// The value carried is *uncertainty*, not distance: zero means the cat
    /// knows exactly where the player is, and larger numbers mean a vaguer
    /// idea. A cat that saw the player itself has no uncertainty at all; a
    /// cat two aisles away that heard about it has a rough idea and a rough
    /// idea has a wide error bar.
    /// </summary>
    /// <param name="playerCell">Where the player is.</param>
    /// <param name="map">The map, for walking distance.</param>
    public void Update(GridPos playerCell, GridMap map)
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

                // How far the news has to travel, in cells.
                float apart = ManhattanDistance(cat.Cell, who.Cell);
                if (apart > ShoutRadius) continue;

                // The further the news travels, the vaguer it gets. A cat
                // that was nearly there gets a better fix than one across
                // the store.
                float carried = distance * (0.5f + 0.5f * (apart / ShoutRadius)) * Decay;
                if (carried < uncertainty)
                {
                    uncertainty = carried;
                    lastSeen = who.LastSeenPlayerCell ?? playerCell;
                }
            }

            if (uncertainty < float.MaxValue)
            {
                cat.PlayerAwareness = uncertainty;
                if (lastSeen.HasValue) cat.LastSeenPlayerCell = lastSeen;
            }
        }
    }

    private static int ManhattanDistance(GridPos a, GridPos b) =>
        Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y);
}
