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
    /// <summary>Total rounds started (including the current one).</summary>
    public int RunsStarted { get; private set; }

    /// <summary>Total rounds won.</summary>
    public int Wins { get; private set; }

    /// <summary>Total rounds lost.</summary>
    public int Losses { get; private set; }

    /// <summary>Fastest winning time, or <see cref="float.MaxValue"/> if no wins yet.</summary>
    public float BestWinTime { get; private set; } = float.MaxValue;

    /// <summary>Time of the most recent win.</summary>
    public float LastWinTime { get; private set; }

    // How many items each species managed to get away with, across all runs.
    // It's the cheapest possible read on whether a critter profile is too
    // effective: if the Shop Cat accounts for most thefts, its numbers are too
    // high, and vice versa.
    public Dictionary<string, int> TheftsBySpecies { get; } = new();

    /// <summary>Subscribes to the EventSystem and starts tracking stats.</summary>
    public RunStats(EventSystem events)
    {
        events.Subscribe<GameStartedEvent>(OnGameStarted);
        events.Subscribe<GameWonEvent>(OnGameWon);
        events.Subscribe<GameLostEvent>(OnGameLost);
        events.Subscribe<ItemStolenEvent>(OnItemStolen);
        events.Subscribe<ShiftEventFiredEvent>(OnShiftEvent);
        events.Subscribe<CatBefriendedEvent>(_ => Befriendings++);
    }

    /// <summary>How many cats have been fed into friendship this run.</summary>
    public int Befriendings { get; private set; }

    /// <summary>Shift events fired this run, by kind (for frequency tuning).</summary>
    public Dictionary<ShiftEventKind, int> ShiftEvents { get; } = new();

    private void OnShiftEvent(ShiftEventFiredEvent e)
    {
        ShiftEvents.TryGetValue(e.Kind, out int n);
        ShiftEvents[e.Kind] = n + 1;
        Console.WriteLine($"[RunStats] Shift event: {e.Kind}.");
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
