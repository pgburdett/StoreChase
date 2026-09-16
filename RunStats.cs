using System;

namespace StoreChase;

// Gives the EventSystem a real consumer: Game1 publishes gameplay events and
// this class reacts by tracking cross-run stats. The win summary goes to the
// console, which makes the pub/sub wiring observable without needing the
// (currently unloaded) SpriteFont for on-screen text.
//
// Lives for the lifetime of the process. It isn't stored on Game1 because the
// EventSystem singleton holds a strong reference to the subscribed handlers,
// which in turn reference this instance — so it stays rooted on its own.
public class RunStats
{
    public int RunsStarted { get; private set; }
    public int Wins { get; private set; }
    public float BestWinTime { get; private set; } = float.MaxValue;
    public float LastWinTime { get; private set; }

    public RunStats(EventSystem events)
    {
        events.Subscribe<GameStartedEvent>(OnGameStarted);
        events.Subscribe<GameWonEvent>(OnGameWon);
    }

    private void OnGameStarted(GameStartedEvent e) => RunsStarted++;

    private void OnGameWon(GameWonEvent e)
    {
        Wins++;
        LastWinTime = e.ElapsedTime;
        if (e.ElapsedTime < BestWinTime) BestWinTime = e.ElapsedTime;

        Console.WriteLine(
            $"[RunStats] Caught the critter in {e.ElapsedTime:F1}s " +
            $"(win #{Wins} of run {RunsStarted}, best {BestWinTime:F1}s).");
    }
}