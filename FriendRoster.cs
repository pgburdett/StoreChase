using System;
using System.Collections.Generic;
using System.Linq;

namespace StoreChase;

/// <summary>
/// A persistent record of which species the player has befriended across the
/// whole run, plus a name generator for the cats. Pure state (no
/// GraphicsDevice); the game feeds it from CatBefriendedEvent and reads it
/// when spawning a round's cast.
/// </summary>
public sealed class FriendRoster
{
    private readonly HashSet<CritterKind> _friends = new();
    private readonly Random _rng;

    public FriendRoster(Random rng) => _rng = rng;

    /// <summary>Species the player has befriended at least once this run.</summary>
    public IReadOnlySet<CritterKind> Befriended => _friends;

    public bool IsFriend(CritterKind kind) => _friends.Contains(kind);

    public void Befriend(CritterKind kind) => _friends.Add(kind);

    /// <summary>Picks a name for a cat of the given species.</summary>
    public string NameFor(CritterKind kind)
    {
        var pool = Names.TryGetValue(kind, out var p) ? p : Names[CritterKind.Moggy];
        return pool[_rng.Next(pool.Length)];
    }

    private static readonly Dictionary<CritterKind, string[]> Names = new()
    {
        [CritterKind.ShopCat] = new[] { "Mochi", "Biscuit", "Pip", "Noodle" },
        [CritterKind.Kitten] = new[] { "Bean", "Pebble", "Dot", "Sprout" },
        [CritterKind.LoungeLizard] = new[] { "Duchess", "Sir Reginald", "Cleo", "Magnus" },
        [CritterKind.Burglar] = new[] { "Bandit", "Rascal", "Whiskers", "Mittens" },
        [CritterKind.Moggy] = new[] { "Gizmo", "Tiger", "Socks", "Patches" },
        [CritterKind.Mouser] = new[] { "Hunter", "Scout", "Rex", "Fang" },
        [CritterKind.Nervous] = new[] { "Pip", "Shy", "Twitch", "Boo" },
    };
}
