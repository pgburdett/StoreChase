using System;
using System.Collections.Generic;
using StoreChase;

namespace StoreChase.Harness;

/// <summary>
/// Checks on the shift-event schedule (when and which) and the alarm audit's
/// meter effect (how much).
///
/// The effects themselves — stock appearing, lights out, a rat — live in
/// Game1 and are exercised by playing; what can be checked headlessly is the
/// pure scheduler and the one arithmetic effect, and both are the parts a
/// tuning change is likely to break: a schedule that never fires, fires
/// every frame, or has quietly lost an event kind.
/// </summary>
public static class ShiftChecks
{
    /// <summary>Runs every shift-event check.</summary>
    public static void Run()
    {
        Check.Section("Shift: the event schedule");

        var rng = new Random(7);
        var shift = new ShiftEventSystem(rng);
        shift.StartRound();

        Check.That("the first event waits for its window",
            shift.TimeUntilNext >= ShiftEventSystem.FirstEventMin
            && shift.TimeUntilNext <= ShiftEventSystem.FirstEventMax,
            $"first at {shift.TimeUntilNext:F1}s");

        // Nothing fires before the window: age to just short of it.
        shift.StartRound();
        var early = shift.Update(ShiftEventSystem.FirstEventMin - 1f);
        Check.That("nothing fires before the first window", early == null);

        // Once one fires, the next is scheduled on the longer interval.
        int guard = 0;
        ShiftEventKind? first = null;
        while (first == null && guard++ < 40)
            first = shift.Update(1f);
        Check.That("an event eventually fires", first.HasValue);
        Check.That("the next event waits for the interval window",
            shift.TimeUntilNext >= ShiftEventSystem.IntervalMin
            && shift.TimeUntilNext <= ShiftEventSystem.IntervalMax,
            $"next at {shift.TimeUntilNext:F1}s");

        // Over a long shift every kind turns up — a schedule that lost a
        // kind in a refactor would still "work", just never go dark.
        var kinds = new HashSet<ShiftEventKind>();
        var marathon = new ShiftEventSystem(new Random(11));
        marathon.StartRound();
        for (int i = 0; i < 6000; i++)
        {
            var fired = marathon.Update(0.1f);
            if (fired.HasValue) kinds.Add(fired.Value);
        }
        Check.That("every event kind is reachable",
            kinds.Count == 4, $"saw {kinds.Count}/4");

        // And it keeps a steady rhythm: at 12–22s apart, 600s holds between
        // 27 and 51 events.
        int longCount = EventCount(6000);
        Check.That("the rhythm stays sane over a long shift",
            longCount >= 600 / (int)ShiftEventSystem.IntervalMax
            && longCount <= 600 / (int)ShiftEventSystem.IntervalMin + 1,
            $"{longCount} events in 600s");

        // A new round reschedules from the top.
        marathon.StartRound();
        Check.That("StartRound reschedules the first event",
            marathon.TimeUntilNext >= ShiftEventSystem.FirstEventMin
            && marathon.TimeUntilNext <= ShiftEventSystem.FirstEventMax);

        Check.Section("Shift: the manager's audit");

        var alarm = new AlarmSystem();
        alarm.Spike(0.5f);
        alarm.Drain(0.3f);
        Check.That("the audit drops the meter by its fraction",
            MathF.Abs(alarm.Level - 0.2f) < 1e-4f, $"level {alarm.Level:F2}");

        alarm.Drain(1f);
        Check.That("the audit cannot push the meter below empty",
            alarm.Level == 0f, $"level {alarm.Level:F2}");
    }

    // Events fired by 600s of 0.1s ticks from a fresh schedule.
    private static int EventCount(int ticks)
    {
        var shift = new ShiftEventSystem(new Random(11));
        shift.StartRound();
        int count = 0;
        for (int i = 0; i < ticks; i++)
            if (shift.Update(0.1f).HasValue) count++;
        return count;
    }
}
