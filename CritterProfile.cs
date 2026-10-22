using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;

namespace StoreChase;

public enum CritterKind
{
    /// <summary>The one that actually plans a heist.</summary>
    ShopCat,

    /// <summary>Fast, panicky, no escape plan. Easy to corner.</summary>
    Kitten,

    /// <summary>Slow, unbothered, sleeps through the alarm unless it's the
    /// one holding something.</summary>
    LoungeLizard,

    /// <summary>Freezes when you look straight at it, bolts the moment you
    /// look away. Turn on the first-person camera to corner one.</summary>
    Nervous,
}

// Everything that makes one cat behave differently from another, in one
// immutable record. SmartAnimalAI reads these instead of hardcoding numbers, so
// adding a species is a new entry here rather than a new branch in the AI.
public sealed record CritterProfile
{
    /// <summary>The species this profile describes.</summary>
    public required CritterKind Kind { get; init; }

    /// <summary>Display name (used in the HUD and event feed).</summary>
    public required string Name { get; init; }

    // Movement and senses.
    /// <summary>Grid cells per second.</summary>
    public required float MoveSpeed { get; init; }

    /// <summary>Distance at which the cat notices the player.</summary>
    public required int AlertRadius { get; init; }

    /// <summary>Distance at which the cat flees from the player.</summary>
    public required int FleeRadius { get; init; }

    /// <summary>How far the cat can see (for line-of-sight checks).</summary>
    public required int VisionRadius { get; init; }

    // Planning.
    /// <summary>Seconds between escape-route replans.</summary>
    public required float ReplanInterval { get; init; }

    /// <summary>Chance per step of a random juke/doubling-back.</summary>
    public required float QuirkChance { get; init; }

    /// <summary>0 = ignores the merchandise, 1 = makes a beeline for it.</summary>
    public required float ThiefDrive { get; init; }

    /// <summary>Freezes while the player is looking at it.</summary>
    public required bool FreezesWhenWatched { get; init; }

    /// <summary>Ignores a thrown ball.</summary>
    public required bool IgnoresBall { get; init; }

    /// <summary>
    /// What this species is after.
    ///
    /// A cat only bothers with the goods it likes: the drive to steal a
    /// preferred kind is <see cref="ThiefDrive"/>, and anything else is
    /// stolen with <see cref="ThiefDrive"/> times
    /// <see cref="UnpreferredThiefFactor"/>, which is usually "not at all".
    ///
    /// This is what turns the loot's three kinds from a colour into a
    /// decision for the player: knowing the cats' tastes tells you which
    /// items to watch, and knowing the shop's tastes tells you which to
    /// leave out.
    /// </summary>
    public required LootKind PreferredKind { get; init; }

    /// <summary>
    /// How much of its usual drive a cat keeps for goods it does not want.
    ///
    /// Zero means "leaves it alone", which is the usual case.
    /// </summary>
    public required float UnpreferredThiefFactor { get; init; }

    /// <summary>
    /// How likely this species is to grab a loose item it happens to be
    /// standing on, given that it does not particularly want it.
    /// </summary>
    public float ThiefDriveFor(LootKind kind) =>
        kind == PreferredKind
            ? ThiefDrive
            : ThiefDrive * UnpreferredThiefFactor;

    // Presentation. The rig has one model, so the cats are told apart by size,
    // a tint and what they do when idle.
    /// <summary>Scale factor applied to the shared cat model.</summary>
    public required float Scale { get; init; }

    /// <summary>Species colour. Also drives the shader tint (as a normalized
    /// Vector3) and the HUD swatch, so the three never disagree.</summary>
    public required Color Tint { get; init; }

    /// <summary>What the cat does when idle (standing, loafing, etc.).</summary>
    public required CatPose IdlePose { get; init; }

    /// <summary>Tint as the CatNormalMap shader wants it (0..1, three channels).</summary>
    public Vector3 TintVector => new(Tint.R / 255f, Tint.G / 255f, Tint.B / 255f);

    /// <summary>Returns the profile for a given species.</summary>
    public static CritterProfile For(CritterKind kind) => kind switch
    {
        CritterKind.ShopCat => new CritterProfile
        {
            Kind = kind,
            Name = "Shop Cat",
            MoveSpeed = 4.2f,
            AlertRadius = 8,
            FleeRadius = 5,
            VisionRadius = 9,
            ReplanInterval = 0.6f,
            QuirkChance = 0.15f,
            ThiefDrive = 0.95f,
            FreezesWhenWatched = false,
            IgnoresBall = false,
            // The big-box criminal: whatever is in the cartons.
            PreferredKind = LootKind.Boxed,
            UnpreferredThiefFactor = 0f,
            Scale = 1f,
            Tint = new Color(255, 255, 255), // the diffuse texture unmodified
            IdlePose = CatPose.Stand,
        },

        CritterKind.Kitten => new CritterProfile
        {
            Kind = kind,
            Name = "Kitten",
            MoveSpeed = 5.6f,
            AlertRadius = 10,
            FleeRadius = 7,
            VisionRadius = 11,
            // No patience: it re-decides constantly and jukes far more often.
            ReplanInterval = 0.25f,
            QuirkChance = 0.45f,
            ThiefDrive = 0.7f,
            FreezesWhenWatched = false,
            IgnoresBall = false,
            // A kitten will take anything, but it goes for the easy stuff.
            PreferredKind = LootKind.Bagged,
            UnpreferredThiefFactor = 0.35f,
            Scale = 0.7f,
            Tint = new Color(255, 236, 214),
            IdlePose = CatPose.Stand,
        },

        CritterKind.LoungeLizard => new CritterProfile
        {
            Kind = kind,
            Name = "Lounge Lizard",
            MoveSpeed = 2.4f,
            AlertRadius = 4,
            FleeRadius = 2,
            VisionRadius = 5,
            ReplanInterval = 1.2f,
            QuirkChance = 0.05f,
            ThiefDrive = 0.25f,
            FreezesWhenWatched = false,
            IgnoresBall = true,
            // It only nibbles at the tins, and only if it has to.
            PreferredKind = LootKind.Canned,
            UnpreferredThiefFactor = 0f,
            Scale = 1.25f,
            Tint = new Color(214, 200, 186),
            IdlePose = CatPose.Loaf,
        },

        _ => new CritterProfile
        {
            Kind = CritterKind.Nervous,
            Name = "Nervous Cat",
            MoveSpeed = 4.8f,
            AlertRadius = 7,
            FleeRadius = 6,
            VisionRadius = 12,
            ReplanInterval = 0.4f,
            QuirkChance = 0.3f,
            ThiefDrive = 0.8f,
            FreezesWhenWatched = true,
            IgnoresBall = false,
            // Nervous about the tins being heavy, so it takes the light bags.
            PreferredKind = LootKind.Bagged,
            UnpreferredThiefFactor = 0.2f,
            Scale = 0.9f,
            Tint = new Color(226, 228, 236),
            IdlePose = CatPose.Stand,
        },
    };

    /// <summary>All available species kinds.</summary>
    public static IReadOnlyList<CritterKind> AllKinds { get; } = new[]
    {
        CritterKind.ShopCat,
        CritterKind.Kitten,
        CritterKind.LoungeLizard,
        CritterKind.Nervous,
    };

    /// <summary>Returns a random species profile.</summary>
    public static CritterProfile Random(Random rng) => For(AllKinds[rng.Next(AllKinds.Count)]);
}
