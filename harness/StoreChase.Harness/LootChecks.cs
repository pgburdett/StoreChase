using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using StoreChase;

namespace StoreChase.Harness;

/// <summary>
/// Checks on the loot kinds and the species' tastes.
///
/// The kinds used to be decorative — a colour on a box. They are now a
/// mechanic: each species wants one kind and ignores the rest, and the alarm
/// cares about how much the goods are worth. Both are easy to break by
/// editing a number and not noticing, so they are checked directly.
/// </summary>
public static class LootChecks
{
    /// <summary>Runs every loot check.</summary>
    public static void Run()
    {
        Check.Section("Loot: kinds, tastes and value");

        // Every species has a taste, and it is a real one.
        foreach (var kind in CritterProfile.AllKinds)
        {
            var profile = CritterProfile.For(kind);
            Check.That($"'{profile.Name}' has a preferred kind",
                Enum.IsDefined(profile.PreferredKind),
                $"prefers {profile.PreferredKind}");
        }

        // The drive for the wanted kind is the full drive, and for the
        // unwanted kinds it is reduced. A species with a zero factor ignores
        // them entirely.
        foreach (var kind in CritterProfile.AllKinds)
        {
            var profile = CritterProfile.For(kind);
            // A guard dog (and any other non-thief) has no merchandise drive
            // at all, so the preferred/unpreferred distinction does not apply.
            if (profile.ThiefDrive <= 0f) continue;

            float preferred = profile.ThiefDriveFor(profile.PreferredKind);
            float unwanted = profile.ThiefDriveFor(Other(profile.PreferredKind));

            Check.That($"'{profile.Name}' wants its preferred kind at full drive",
                Math.Abs(preferred - profile.ThiefDrive) < 0.0001f,
                $"{preferred:F2} vs drive {profile.ThiefDrive:F2}");
            Check.That($"'{profile.Name}' is less interested in other kinds",
                unwanted < preferred,
                $"other {unwanted:F2} vs preferred {preferred:F2}");
        }

        // The Shop Cat is the strict one: it wants the cartons and nothing
        // else. That is the species the mechanic reads best on.
        var shop = CritterProfile.For(CritterKind.ShopCat);
        Check.That("the Shop Cat ignores the goods it does not want",
            shop.ThiefDriveFor(LootKind.Canned) == 0f && shop.ThiefDriveFor(LootKind.Bagged) == 0f,
            $"cans {shop.ThiefDriveFor(LootKind.Canned):F2}, bags {shop.ThiefDriveFor(LootKind.Bagged):F2}");

        // The alarm cares about value: a carton is worth more than a tin.
        Check.That("the goods are not all worth the same to the store",
            new LootItem(LootKind.Boxed, new GridPos(1, 1), Vector3.Zero).AlarmWeight >
            new LootItem(LootKind.Canned, new GridPos(1, 1), Vector3.Zero).AlarmWeight,
            $"carton {new LootItem(LootKind.Boxed, new GridPos(1, 1), Vector3.Zero).AlarmWeight:F2} " +
            $"vs tin {new LootItem(LootKind.Canned, new GridPos(1, 1), Vector3.Zero).AlarmWeight:F2}");

        // The alarm's cause must depend on the weight, not the count, or the
        // weighting does nothing.
        var alarm = new AlarmSystem();
        alarm.Update(0.1f, stolenWeight: 0.5f, playerCarrying: false, securedAll: false);
        Check.That("a half-weight theft still raises the alarm", alarm.Cause == AlarmCause.Thief,
            $"cause {alarm.Cause}, level {alarm.Level:F3}");

        // A stolen carton should raise the meter faster than a stolen tin.
        float BoxedRise(AlarmSystem a) { a.Update(0.5f, 1.5f, false, false); return a.Level; }
        float CannedRise(AlarmSystem a) { a.Update(0.5f, 0.7f, false, false); return a.Level; }
        float boxed = BoxedRise(new AlarmSystem());
        float canned = CannedRise(new AlarmSystem());
        Check.That("a stolen carton raises the alarm faster than a stolen tin",
            boxed > canned, $"carton {boxed:F3} vs tin {canned:F3}");

        // A cat only steals what it likes: check that the AI declines a tin
        // when it wants cartons.
        CheckAIDeclinesUnwantedLoot();
    }

    private static LootKind Other(LootKind kind) => kind switch
    {
        LootKind.Canned => LootKind.Boxed,
        LootKind.Boxed => LootKind.Canned,
        _ => LootKind.Canned,
    };

    private static void CheckAIDeclinesUnwantedLoot()
    {
        var map = new GridMap(StoreLayouts.Standard);
        var profile = CritterProfile.For(CritterKind.ShopCat);

        // A tin underfoot: the Shop Cat should leave it.
        int tinTaken = 0;
        for (int i = 0; i < 200; i++)
        {
            var ai = new SmartAnimalAI(profile, new Random(i));
            var ctx = new CritterContext(
                CurrentCell: new GridPos(3, 3), PlayerCell: new GridPos(16, 10), Map: map,
                DeltaTime: 0.016f, CanSeePlayer: false, BeingWatched: false, Ball: null,
                LootUnderfoot: new LootItem(LootKind.Canned, new GridPos(3, 3), Vector3.Zero),
                LootTarget: null, LootTargetItem: null, Carrying: false, StolenCount: 0,
                AlarmStage: AlarmStage.Calm, PlayerAwareness: 0f, LastSeenPlayerCell: null);
            ai.DecideNextMove(ctx);
            if (ai.WantsToSteal) tinTaken++;
        }

        // A carton underfoot: it should take it.
        int cartonTaken = 0;
        for (int i = 0; i < 200; i++)
        {
            var ai = new SmartAnimalAI(profile, new Random(i));
            var ctx = new CritterContext(
                CurrentCell: new GridPos(3, 3), PlayerCell: new GridPos(16, 10), Map: map,
                DeltaTime: 0.016f, CanSeePlayer: false, BeingWatched: false, Ball: null,
                LootUnderfoot: new LootItem(LootKind.Boxed, new GridPos(3, 3), Vector3.Zero),
                LootTarget: null, LootTargetItem: null, Carrying: false, StolenCount: 0,
                AlarmStage: AlarmStage.Calm, PlayerAwareness: 0f, LastSeenPlayerCell: null);
            ai.DecideNextMove(ctx);
            if (ai.WantsToSteal) cartonTaken++;
        }

        Check.That("the Shop Cat leaves the goods it does not want",
            tinTaken == 0, $"took a tin {tinTaken}/200 times");
        Check.That("the Shop Cat takes the goods it does want",
            cartonTaken > 150, $"took a carton {cartonTaken}/200 times");
    }
}
