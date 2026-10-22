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

    /// <summary>True once the meter has reached 1.0 — the round is lost.</summary>
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

    /// <summary>
    /// A multiplier applied to every rate, used to scale the alarm to the
    /// size of the store.
    ///
    /// The rates above were tuned against one small floor plan. A bigger
    /// store is not "harder" at the same rates — it is unwinnable, because the
    /// player has further to walk between the till and the shelves and the
    /// meter fills before the round can be cleared. Scaling the rates by how
    /// much work the store is keeps the same amount of *alarm budget* in a
    /// round regardless of its size.
    ///
    /// 1.0 means "as tuned".
    /// </summary>
    public float RateScale { get; set; } = 1f;

    /// <summary>Set each frame by Game1 so the HUD can show why it's rising.</summary>
    public AlarmCause Cause { get; private set; }

    /// <summary>
    /// Which band the meter is in.
    ///
    /// This is what the cats read: below <see cref="SuspicionThreshold"/>
    /// they go about their business, above <see cref="CriticalThreshold"/>
    /// they stop caring what they want and go for the goods.
    /// </summary>
    public AlarmStage Stage { get; private set; } = AlarmStage.Calm;

    /// <summary>The meter level at which the store becomes suspicious.</summary>
    public const float SuspicionThreshold = 0.4f;

    /// <summary>The meter level at which the store is about to go off.</summary>
    public const float CriticalThreshold = 0.75f;

    /// <summary>Raised when the cause changes (e.g. from Calm to Thief).</summary>
    public event Action<AlarmCause>? Changed;

    /// <summary>Resets the meter to zero and clears the cause.</summary>
    public void Reset()
    {
        Level = 0f;
        _grace = 0f;
        Cause = AlarmCause.Calm;
    }

    /// <summary>
    /// Advances the alarm meter one frame.
    ///
    /// The meter rises while a critter is carrying something (rate scales with
    /// how much is in flight, weighted by what the goods are worth) or while
    /// the player is carrying something (much lower rate). It drains when
    /// nothing is out. A short grace period after a change in state prevents
    /// the meter from spiking on the same frame.
    /// </summary>
    /// <param name="dt">Seconds since the last frame.</param>
    /// <param name="stolenWeight">
    /// How much is out of place, weighted by value (see
    /// <see cref="ItemField.StolenWeight"/>).
    /// </param>
    /// <param name="playerCarrying">Whether the player is carrying something.</param>
    /// <param name="securedAll">Whether every item is behind the counter.</param>
    public void Update(float dt, float stolenWeight, bool playerCarrying, bool securedAll)
    {
        if (Tripped) return;

        _grace = MathF.Max(0f, _grace - dt);

        var cause = AlarmCause.Calm;
        if (stolenWeight > 0f) cause = AlarmCause.Thief;
        else if (playerCarrying) cause = AlarmCause.PlayerCarrying;
        // Winning stops the meter entirely rather than letting it sit full.
        else if (securedAll) cause = AlarmCause.Won;

        if (cause != Cause)
        {
            Cause = cause;
            Changed?.Invoke(cause);
        }

        // The band the meter is in. Recomputed every frame rather than only
        // when the cause changes, because the meter can cross a threshold
        // while the cause stays the same.
        Stage = Level >= CriticalThreshold ? AlarmStage.Critical
               : Level >= SuspicionThreshold ? AlarmStage.Suspicious
               : AlarmStage.Calm;

        if (_grace > 0f) return;

        switch (cause)
        {
            case AlarmCause.Thief:
                Level += StolenRate * RateScale * stolenWeight * dt;
                break;
            case AlarmCause.PlayerCarrying:
                Level += PlayerCarryingRate * RateScale * dt;
                break;
            default:
                Level -= DecayRate * RateScale * dt;
                break;
        }

        Level = MathHelper.Clamp(Level, 0f, 1f);
    }

    /// <summary>
    /// Called when something changes hands, to hold the meter steady briefly.
    /// This prevents the meter from spiking on the same frame as a pickup or
    /// tackle, and gives the player a moment to react before the alarm starts
    /// climbing.
    /// </summary>
    public void Ping() => _grace = GraceSeconds;

    /// <summary>
    /// Jumps the meter by a fixed amount, immediately.
    ///
    /// Used for discrete events that are worse than a rate: a thief reaching
    /// the counter and dumping the goods there, for instance. That is not
    /// "the alarm is rising a bit faster", it is the alarm going off, and the
    /// difference has to be immediate and obvious.
    /// </summary>
    /// <param name="amount">How much to add. Usually a fraction of the meter.</param>
    public void Spike(float amount)
    {
        Level = MathHelper.Clamp(Level + amount, 0f, 1f);
    }
}

/// <summary>Why the alarm is currently rising (or calm).</summary>
public enum AlarmCause
{
    /// <summary>Nothing is out of place.</summary>
    Calm,

    /// <summary>A critter is carrying something.</summary>
    Thief,

    /// <summary>The player is carrying something.</summary>
    PlayerCarrying,

    /// <summary>Everything is secured — the round is won.</summary>
    Won,
}

/// <summary>
/// How alarmed the store is.
///
/// The meter itself is a 0..1 float, but the cats and the HUD care about
/// bands rather than the exact value: "things are getting interesting" and
/// "the store is about to go off" are different situations that want
/// different behaviour.
/// </summary>
public enum AlarmStage
{
    /// <summary>Quiet. The meter is below <see cref="AlarmSystem.SuspicionThreshold"/>.</summary>
    Calm,

    /// <summary>
    /// Something is wrong. The cats get braver and the store's lights start
    /// to change.
    /// </summary>
    Suspicious,

    /// <summary>
    /// The alarm is about to go off. The cats stop playing about and go for
    /// the goods regardless of what they wanted.
    /// </summary>
    Critical,
}
