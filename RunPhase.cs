namespace StoreChase;

// Whether the round is still going, and if not, how it ended. Kept separate
// from the win/lose booleans the game used to carry so the HUD can pick the
// right message without a pile of flags.
public enum RunPhase
{
    /// <summary>Title screen — waiting for the player to start.</summary>
    Title,

    /// <summary>The round is still in progress.</summary>
    Playing,

    /// <summary>The player won — every item secured before the alarm filled.</summary>
    Won,

    /// <summary>The alarm filled — the round is lost.</summary>
    Lost,
}
