using System;
using System.Collections.Generic;

namespace StoreChase;

// Gives the EventSystem a real consumer: Game1 publishes gameplay events and
// this class reacts by tracking cross-run stats. Summaries go to the console,
// which makes the pub/sub wiring observable without needing the SpriteFont.
//
// Lives for the lifetime of the process. It isn't stored on Game1 because the
// EventSystem singleton holds a strong reference to the subscribed handlers,
// which in turn reference this instance — so it stays rooted on its own.
public class RunStats
{
    public int RunsStarted { get; private set; }
    public int Wins { get; private set; }
    public int Losses { get; private set; }
    public float BestWinTime { get; private set; } = float.MaxValue;
    public float LastWinTime { get; private set; }

    // How many items each species managed to get away with, across all runs.
    // It's the cheapest possible read on whether a critter profile is too
    // effective: if the Shop Cat accounts for most thefts, its numbers are too
    // high, and vice versa.
    public Dictionary<string, int> TheftsBySpecies { get; } = new();

    public RunStats(EventSystem events)
    {
        events.Subscribe<GameStartedEvent>(OnGameStarted);
        events.Subscribe<GameWonEvent>(OnGameWon);
        events.Subscribe<GameLostEvent>(OnGameLost);
        events.Subscribe<ItemStolenEvent>(OnItemStolen);
    }

    private void OnGameStarted(GameStartedEvent e) => RunsStarted++;

    private void OnGameWon(GameWonEvent e)
    {
        Wins++;
        LastWinTime = e.ElapsedTime;
        if (e.ElapsedTime < BestWinTime) BestWinTime = e.ElapsedTime;

        Console.WriteLine(
            $"[RunStats] Store secured in {e.ElapsedTime:F1}s " +
            $"(win #{Wins} of run {RunsStarted}, best {BestWinTime:F1}s).");
    }

    private void OnGameLost(GameLostEvent e)
    {
        Losses++;
        Console.WriteLine(
            $"[RunStats] Alarm went off after {e.ElapsedTime:F1}s " +
            $"with {e.SecuredCount} item(s) recovered " +
            $"(loss #{Losses} of run {RunsStarted}).");
    }

    private void OnItemStolen(ItemStolenEvent e)
    {
        var key = e.Species ?? "unknown";
        TheftsBySpecies.TryGetValue(key, out int n);
        TheftsBySpecies[key] = n + 1;
    }
}
