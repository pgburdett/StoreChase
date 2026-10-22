// Mood is already defined in Critter.cs? Adding here as an enum in StoreChase namespace.
namespace StoreChase;

/// <summary>A cat's current mood, which tints its behaviour.</summary>
public enum Mood
{
    Calm,
    Wary,   // caught/stunned: flees sooner, won't steal
    Bold,   // successful theft: steals more readily
}
