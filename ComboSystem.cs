using System;

namespace StoreChase;

/// <summary>
/// Tracks the player's securing combo: consecutive items secured within a
/// short window multiply the coin payout. Pure state (no GraphicsDevice), so
/// the timing can be checked headlessly.
///
/// The combo exists to reward the skilled play pattern — sweeping a cluster
/// of loose goods in one pass — without changing the win condition. A missed
/// window just resets to 1×; it never punishes.
/// </summary>
public sealed class ComboSystem
{
    public const float DefaultWindow = 4f;
    public const int MaxCombo = 5;

    private readonly float _window;
    private float _lastSecureTime = float.NegativeInfinity;
    private int _combo = 1;

    public ComboSystem(float window = DefaultWindow) => _window = window;

    /// <summary>Current combo multiplier (1..MaxCombo).</summary>
    public int Combo => _combo;

    /// <summary>
    /// Registers a secure at <paramref name="now"/> (seconds of round time).
    /// Returns the multiplier that applies to this secure's payout.
    /// </summary>
    public int RegisterSecure(float now)
    {
        if (now - _lastSecureTime <= _window && _combo < MaxCombo)
            _combo++;
        else if (now - _lastSecureTime > _window)
            _combo = 1;

        _lastSecureTime = now;
        return _combo;
    }

    /// <summary>Resets the combo (new round).</summary>
    public void Reset()
    {
        _combo = 1;
        _lastSecureTime = float.NegativeInfinity;
    }

    /// <summary>Seconds remaining on the current combo window (0 when expired).</summary>
    public float WindowRemaining(float now) =>
        MathF.Max(0f, _window - (now - _lastSecureTime));
}
