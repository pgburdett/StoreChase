using System;

namespace StoreChase;

/// <summary>The things a shift can throw at you, mid-round.</summary>
public enum ShiftEventKind
{
    /// <summary>A delivery: fresh stock hits the shelves, pressure up.</summary>
    Delivery,

    /// <summary>The lights go: the cats can barely see for a few seconds.</summary>
    PowerCut,

    /// <summary>The manager audits the stockroom: the alarm drains a chunk.</summary>
    AlarmAudit,

    /// <summary>A rat runs the aisles: every cat chases it.</summary>
    Rat,

    /// <summary>A bottle breaks: a spill appears mid-aisle.</summary>
    BottleBreak,

    /// <summary>A customer drops their basket: free goods on the floor.</summary>
    CustomerDrop,
}

/// <summary>
/// When the next shift event lands, and which one.
///
/// Pure scheduling — the game applies the effects (they touch the items,
/// the alarm, the lights and the cats, none of which this file may know
/// about), this only answers "is one due, and which?". That split is what
/// makes the timing testable headlessly: no clock, no GraphicsDevice, no
/// game state, just a countdown and an RNG.
///
/// The rhythm is deliberately generous (first event ~10–18s in, then every
/// 12–22s): an event system that fires constantly is weather, not weather.
/// A short round may see none at all, which is also fine — the events are
/// spice for a long shift, not a promise.
/// </summary>
public sealed class ShiftEventSystem
{
    /// <summary>Earliest the first event of a round can land, in seconds.</summary>
    public const float FirstEventMin = 10f;

    /// <summary>Latest the first event of a round can land.</summary>
    public const float FirstEventMax = 18f;

    /// <summary>Earliest gap between events, in seconds.</summary>
    public const float IntervalMin = 12f;

    /// <summary>Latest gap between events.</summary>
    public const float IntervalMax = 22f;

    private readonly Random _rng;
    private float _timer = float.MaxValue;

    /// <summary>Creates a scheduler. Call <see cref="StartRound"/> before
    /// <see cref="Update"/>, or nothing is ever scheduled.</summary>
    public ShiftEventSystem(Random rng) => _rng = rng;

    /// <summary>Seconds until the next event; infinity when none is scheduled.</summary>
    public float TimeUntilNext => _timer;

    /// <summary>Schedules this round's first event and clears any old clock.</summary>
    public void StartRound() =>
        _timer = _rng.NextSingle() * (FirstEventMax - FirstEventMin) + FirstEventMin;

    /// <summary>
    /// Ages the clock. Returns the event that fired this frame, if any;
    /// firing schedules the next one on the longer interval.
    /// </summary>
    public ShiftEventKind? Update(float dt)
    {
        _timer -= dt;
        if (_timer > 0f) return null;

        var kind = (ShiftEventKind)_rng.Next(Enum.GetValues<ShiftEventKind>().Length);
        _timer = _rng.NextSingle() * (IntervalMax - IntervalMin) + IntervalMin;
        return kind;
    }
}
