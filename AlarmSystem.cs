using System;
using Microsoft.Xna.Framework;

namespace StoreChase;

// The store alarm: a single 0..1 meter that fills while loot is out of place
// and drains when everything is accounted for. It is the round's clock, so it
// deliberately has no timer of its own — the tension comes from the loot
// states, not from a countdown.
//
// Two states raise it, and the distinction is the whole point of the mechanic:
//   - a critter is carrying something (it has what it came for)
//   - the player is carrying something (walking it to the counter)
// Everything else drains it, so a clean store is a quiet store.
public class AlarmSystem
{
    /// <summary>0 = calm, 1 = the round is lost.</summary>
    public float Level { get; private set; }

    public bool Tripped => Level >= 1f;

    // Rise rates, as a fraction of the meter per second, scaled by how many
    // items are out.
    //
    // Tuned from a headless simulation of full rounds (a scripted player that
    // beelines for the nearest item and ignores the cats entirely). At 0.055
    // that bot won a third of its rounds and lost almost every round in which
    // anything was stolen, because there wasn't enough time left to chase a
    // thief down and still clear the store. 0.042 gives one unattended thief
    // roughly 24 seconds — enough to corner one — while two thieves still fill
    // the meter in about 12, so stacking thefts stays a genuine crisis.
    public float StolenRate { get; set; } = 0.042f;
    public float PlayerCarryingRate { get; set; } = 0.02f;

    // Draining is faster than the player's own carry rate, so tidying up is
    // rewarded and a clean store pulls the meter back down.
    public float DecayRate { get; set; } = 0.07f;

    // A short grace period after a change in state, so walking onto a loose
    // item doesn't spike the meter on the same frame and a tackle isn't
    // punished before the drop registers.
    public float GraceSeconds { get; set; } = 0.6f;
    private float _grace;

    /// <summary>Set each frame by Game1 so the HUD can show why it's rising.</summary>
    public AlarmCause Cause { get; private set; }

    public event Action<AlarmCause>? Changed;

    public void Reset()
    {
        Level = 0f;
        _grace = 0f;
        Cause = AlarmCause.Calm;
    }

    public void Update(float dt, int stolenCount, bool playerCarrying, bool securedAll)
    {
        if (Tripped) return;

        _grace = MathF.Max(0f, _grace - dt);

        var cause = AlarmCause.Calm;
        if (stolenCount > 0) cause = AlarmCause.Thief;
        else if (playerCarrying) cause = AlarmCause.PlayerCarrying;
        // Winning stops the meter entirely rather than letting it sit full.
        else if (securedAll) cause = AlarmCause.Won;

        if (cause != Cause)
        {
            Cause = cause;
            Changed?.Invoke(cause);
        }

        if (_grace > 0f) return;

        switch (cause)
        {
            case AlarmCause.Thief:
                Level += StolenRate * stolenCount * dt;
                break;
            case AlarmCause.PlayerCarrying:
                Level += PlayerCarryingRate * dt;
                break;
            default:
                Level -= DecayRate * dt;
                break;
        }

        Level = MathHelper.Clamp(Level, 0f, 1f);
    }

    // Called when something changes hands, to hold the meter steady briefly.
    public void Ping() => _grace = GraceSeconds;
}

public enum AlarmCause
{
    Calm,
    Thief,
    PlayerCarrying,
    Won,
}
