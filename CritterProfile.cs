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

    /// <summary>Picks its moment: won't shoplift while you're watching, and
    /// bolts hard the moment it has something. The cheap tins, too — it knows
    /// what the alarm notices.</summary>
    Burglar,

    /// <summary>Enormous, slow, and completely unbothered by a thrown ball.
    /// It takes whatever it fancies, slowly. Tackle it or corner it.</summary>
    Moggy,

    /// <summary>The fastest thing in the store. Low patience for thieving and
    /// high panic, so it grab-and-go rather than planning a heist.</summary>
    Mouser,

    /// <summary>A store guard that hunts the player instead of the loot, and
    /// barks to alert every cat within earshot.</summary>
    GuardDog,
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

    /// <summary>
    /// How far the cat *hears* a plain footstep, in cells.
    ///
    /// Scaled by the noise's loudness, so skids and crashes carry further
    /// (see <see cref="NoiseSystem"/>). This is what gives each species a
    /// stealth profile: sneaking past a napping Moggy works, tiptoeing past
    /// a Mouser does not.
    /// </summary>
    public float HearingRadius { get; init; } = 5f;

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
    /// How much of a ball's stun this species keeps, as a multiplier.
    ///
    /// 1 = normal, 0.5 = shrugs it off twice as fast, 0 = immune: the ball
    /// bounces off its head and it just gets annoyed (see
    /// <see cref="Critter.TryStunFromBall"/>). This is the difference
    /// between " distraction tool" and "this one has to be cornered".
    /// </summary>
    public float StunDurationFactor { get; init; } = 1f;

    /// <summary>
    /// Won't take loot while the player is watching it.
    ///
    /// Makes the camera a two-way tool: pinning a Nervous cat freezes it,
    /// and pinning a Burglar stops it mid-shoplift. The AI checks this
    /// before every steal, not just the initial grab.
    /// </summary>
    public bool Sneaky { get; init; }

    /// <summary>
    /// Speed multiplier while carrying stolen goods.
    ///
    /// The getaway gear: a cat with this doesn't just keep its usual pace
    /// once it has something, it sprints for the till.
    /// </summary>
    public float CarrySpeedBoost { get; init; } = 1f;

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
    /// This species hunts the player instead of the merchandise: it never
    /// steals, never eats treats, and never befriends. Its sighting of the
    /// player propagates further than a normal cat's (see SharedAwareness).
    /// </summary>
    public bool ChasesPlayer { get; init; } = false;

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
            HearingRadius = 5f,
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
            // Young and twitchy: hears nearly as far as it sees.
            HearingRadius = 6f,
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
            // Asleep on the job. You can walk past this one at a stroll.
            HearingRadius = 2f,
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

        CritterKind.Burglar => new CritterProfile
        {
            Kind = kind,
            Name = "Burglar Cat",
            MoveSpeed = 4.4f,
            AlertRadius = 9,
            FleeRadius = 7,
            VisionRadius = 11,
            // A working predator: hears footsteps through the aisles.
            HearingRadius = 5f,
            ReplanInterval = 0.45f,
            QuirkChance = 0.35f,
            ThiefDrive = 0.85f,
            FreezesWhenWatched = false,
            IgnoresBall = false,
            // Won't shoplift in front of you: watching it actually stops it.
            Sneaky = true,
            // And once it has something it is gone.
            CarrySpeedBoost = 1.4f,
            // Takes the tins: low value, so the alarm barely notices.
            PreferredKind = LootKind.Canned,
            UnpreferredThiefFactor = 0.25f,
            Scale = 0.95f,
            Tint = new Color(168, 144, 118),
            IdlePose = CatPose.Stand,
        },

        CritterKind.Moggy => new CritterProfile
        {
            Kind = kind,
            Name = "Moggy",
            MoveSpeed = 2.0f,
            AlertRadius = 3,
            FleeRadius = 2,
            VisionRadius = 5,
            // Hard of hearing to match: it sleeps through the aisles.
            HearingRadius = 1f,
            ReplanInterval = 1.4f,
            QuirkChance = 0.05f,
            ThiefDrive = 0.6f,
            FreezesWhenWatched = false,
            // The ball bounces off. All of it: no stun, just indignation.
            IgnoresBall = true,
            StunDurationFactor = 0f,
            // Slowly develops a taste for anything.
            PreferredKind = LootKind.Boxed,
            UnpreferredThiefFactor = 0.45f,
            Scale = 1.45f,
            Tint = new Color(126, 116, 110),
            IdlePose = CatPose.Loaf,
        },

        CritterKind.Mouser => new CritterProfile
        {
            Kind = kind,
            Name = "Mouser",
            MoveSpeed = 6.0f,
            AlertRadius = 12,
            FleeRadius = 8,
            VisionRadius = 12,
            // The fastest thing in the store, and the sharpest ears.
            HearingRadius = 7f,
            ReplanInterval = 0.3f,
            QuirkChance = 0.5f,
            ThiefDrive = 0.55f,
            FreezesWhenWatched = false,
            IgnoresBall = false,
            // Grab-and-run: the light bags and not much interest in the rest.
            PreferredKind = LootKind.Bagged,
            UnpreferredThiefFactor = 0.15f,
            Scale = 0.8f,
            Tint = new Color(238, 190, 116),
            IdlePose = CatPose.Stretch,
        },

        CritterKind.GuardDog => new CritterProfile
        {
            Kind = kind,
            Name = "Guard Dog",
            MoveSpeed = 3.6f,
            AlertRadius = 14,
            FleeRadius = 0,
            VisionRadius = 13,
            HearingRadius = 8f,
            ReplanInterval = 0.35f,
            QuirkChance = 0.1f,
            ThiefDrive = 0f,
            FreezesWhenWatched = false,
            IgnoresBall = false,
            ChasesPlayer = true,
            // Not a thief: nothing it wants from the shelves.
            PreferredKind = LootKind.Canned,
            UnpreferredThiefFactor = 0f,
            Scale = 1.35f,
            Tint = new Color(138, 92, 58),
            IdlePose = CatPose.Stand,
        },

        _ => new CritterProfile
        {
            Kind = CritterKind.Nervous,
            Name = "Nervous Cat",
            MoveSpeed = 4.8f,
            AlertRadius = 7,
            FleeRadius = 6,
            VisionRadius = 12,
            // Jumpy: hears a footstep nearly as far as it can see one.
            HearingRadius = 6f,
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
        CritterKind.Burglar,
        CritterKind.Moggy,
        CritterKind.Mouser,
        CritterKind.GuardDog,
    };

    /// <summary>Returns a random species profile.</summary>
    public static CritterProfile Random(Random rng) => For(AllKinds[rng.Next(AllKinds.Count)]);
}
