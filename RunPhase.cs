namespace StoreChase;

// Whether the round is still going, and if not, how it ended. Kept separate
// from the win/lose booleans the game used to carry so the HUD can pick the
// right message without a pile of flags.
public enum RunPhase
{
    Playing,
    Won,
    Lost,
}
