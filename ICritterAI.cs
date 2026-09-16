using Microsoft.Xna.Framework;

namespace StoreChase;

/// <summary>
/// Provides a snapshot of the world state so the AI can make decisions 
/// without needing direct access to the full Game object.
/// </summary>
public record struct CritterContext(
    GridPos CurrentCell,
    GridPos PlayerCell,
    GridMap Map,
    float DeltaTime
);

public interface ICritterAI
{
    /// <summary>
    /// Analyzes the context and decides the next destination.
    /// </summary>
    /// <returns>The target GridPos, or null if no movement is required.</returns>
    GridPos? DecideNextMove(CritterContext context);
}