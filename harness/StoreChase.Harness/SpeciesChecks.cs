using System;
using System.Linq;
using Microsoft.Xna.Framework;
using StoreChase;

namespace StoreChase.Harness;

/// <summary>
/// Checks on the species roster and the per-species hooks that grew with it:
/// the ball-stun tolerance, the getaway boost, the sneaky flag, panic and
/// the treat lure.
///
/// These live outside the round simulation, so a profile edit that silently
/// neuters one of the new mechanics would otherwise only show up as a
/// slightly odd win rate.
/// </summary>
public static class SpeciesChecks
{
    /// <summary>Runs every species check.</summary>
    public static void Run()
    {
        Check.Section("Species: roster and per-species mechanics");

        var names = CritterProfile.AllKinds.Select(k => CritterProfile.For(k).Name).ToList();
        Check.That("every species has a distinct name",
            names.Distinct().Count() == names.Count,
            string.Join(", ", names));

        // The roster is what the random cast draws from, so a new species
        // that forgot to join AllKinds would never appear in the game.
        Check.That("the roster covers every defined kind",
            Enum.GetValues<CritterKind>().All(k => CritterProfile.AllKinds.Contains(k)),
            string.Join(", ", CritterProfile.AllKinds));

        // Sanity on the numbers that make a species visible at all.
        foreach (var kind in CritterProfile.AllKinds)
        {
            var p = CritterProfile.For(kind);
            Check.That($"'{p.Name}' has sane movement and size",
                p.MoveSpeed > 0f && p.Scale > 0f && p.VisionRadius > 0,
                $"speed {p.MoveSpeed}, scale {p.Scale}, vision {p.VisionRadius}");
        }

        CheckStunTolerance();
        CheckStartleKeepsMoving();
        CheckGetawayBoost();
        CheckSneaky();
        CheckPanic();
        CheckTreatLure();
    }

    // The Moggy shrugs off balls; a normal cat does not; a half-hardy test
    // profile sits out half the stun. The factor is the only thing standing
    // between "distraction tool" and "must be cornered".
    private static void CheckStunTolerance()
    {
        var map = new GridMap(StoreLayouts.Standard);
        var locus = new CritterLocus(
            Map: map,
            PlayerCell: new GridPos(16, 10),
            Ball: null,
            LootTarget: null,
            LootTargetItem: null,
            StolenCount: 0,
            LootUnderfoot: _ => null,
            TrySteal: _ => false);

        var moggy = NewCritter(CritterKind.Moggy);
        Check.That("a ball does not stun a Moggy",
            !moggy.TryStunFromBall(3f) && !moggy.Stunned && moggy.Startled,
            $"stunned {moggy.Stunned}, startled {moggy.Startled}");

        var shop = NewCritter(CritterKind.ShopCat);
        Check.That("a ball stuns a Shop Cat",
            shop.TryStunFromBall(3f) && shop.Stunned,
            $"stunned {shop.Stunned}");

        // A half-hardy species (not on the roster, but expressible with the
        // `with` syntax the record supports) sits out half the time. Measured
        // by ticking the critter: the stun ends between 1.5s and 2s, not at 3s.
        var half = CritterProfile.For(CritterKind.ShopCat) with { StunDurationFactor = 0.5f };
        var critter = new Critter(new GridPos(3, 3), 2f, new SmartAnimalAI(half, new Random(1)), half);
        critter.TryStunFromBall(3f);

        Tick(critter, locus, 1.0f);
        bool stillStunnedEarly = critter.Stunned;
        Tick(critter, locus, 1.2f);
        Check.That("a half-hardy species sits out half the stun",
            stillStunnedEarly && !critter.Stunned,
            $"stunned at 1.0s {stillStunnedEarly}, at 2.2s {critter.Stunned}");
    }

    // A startled-but-not-stunned cat keeps walking: the flinch is a rig
    // reaction, not a status that freezes the body.
    private static void CheckStartleKeepsMoving()
    {
        var map = new GridMap(StoreLayouts.Standard);
        var locus = new CritterLocus(
            Map: map,
            PlayerCell: new GridPos(16, 10),
            Ball: null,
            LootTarget: null,
            LootTargetItem: null,
            StolenCount: 0,
            LootUnderfoot: _ => null,
            TrySteal: _ => false);

        var c = NewCritter(CritterKind.Moggy);
        c.Startle(1f);
        c.Update(Step, locus);
        Check.That("a startled cat still acts (no stun early-return)",
            c.Startled && !c.Stunned,
            $"startled {c.Startled}");
    }

    private static void CheckGetawayBoost()
    {
        var boosted = CritterProfile.For(CritterKind.Burglar);
        var plain = CritterProfile.For(CritterKind.ShopCat);

        var c = new Critter(new GridPos(1, 1), 2f,
            new SmartAnimalAI(boosted, new Random(1)), boosted);

        c.Carrying = true;
        Check.That("the Burglar sprints once it has the goods",
            Math.Abs(c.CurrentMoveSpeed - boosted.MoveSpeed * boosted.CarrySpeedBoost) < 0.0001f,
            $"carrying speed {c.CurrentMoveSpeed:F1} vs plain {boosted.MoveSpeed:F1}");

        c.Carrying = false;
        Check.That("the Burglar walks normally empty-handed",
            Math.Abs(c.CurrentMoveSpeed - boosted.MoveSpeed) < 0.0001f,
            $"speed {c.CurrentMoveSpeed:F1}");

        Check.That("a plain cat's speed does not change with loot",
            Math.Abs(plain.CarrySpeedBoost - 1f) < 0.0001f,
            $"boost {plain.CarrySpeedBoost:F2}");
    }

    // A Burglar will not shoplift while watched, and will when not. This is
    // checked over many trials because the grab itself is a dice roll.
    private static void CheckSneaky()
    {
        var map = new GridMap(StoreLayouts.Standard);
        var profile = CritterProfile.For(CritterKind.Burglar);
        var underfoot = new LootItem(LootKind.Canned, new GridPos(3, 4), Vector3.Zero);

        int takenWhileWatched = StealTrials(map, profile, underfoot, beingWatched: true);
        int takenUnwatched = StealTrials(map, profile, underfoot, beingWatched: false);

        Check.That("a Burglar never shoplifts while watched",
            takenWhileWatched == 0, $"{takenWhileWatched}/200 grabs while watched");
        Check.That("a Burglar shoplifts when nobody is watching",
            takenUnwatched > 100, $"{takenUnwatched}/200 grabs unwatched");

        // The other sneaky behaviour must not leak onto other species.
        var cat = CritterProfile.For(CritterKind.ShopCat);
        int catWatched = StealTrials(map, cat,
            new LootItem(LootKind.Boxed, new GridPos(3, 4), Vector3.Zero), beingWatched: true);
        Check.That("a non-sneaky cat is not shy about stealing in view",
            catWatched > 100, $"{catWatched}/200 grabs while watched");
    }

    private static int StealTrials(GridMap map, CritterProfile profile, LootItem underfoot, bool beingWatched)
    {
        int taken = 0;
        for (int i = 0; i < 200; i++)
        {
            var ai = new SmartAnimalAI(profile, new Random(i));
            var ctx = new CritterContext(
                CurrentCell: new GridPos(3, 4), PlayerCell: new GridPos(16, 10), Map: map,
                DeltaTime: 0.016f, CanSeePlayer: true, BeingWatched: beingWatched, Ball: null,
                LootUnderfoot: underfoot,
                LootTarget: null, LootTargetItem: null, Carrying: false, StolenCount: 0,
                AlarmStage: AlarmStage.Calm, PlayerAwareness: 0f, LastSeenPlayerCell: null);
            ai.DecideNextMove(ctx);
            if (ai.WantsToSteal) taken++;
        }
        return taken;
    }

    // A panicking cat flees (makes a move, with the startle reaction) and
    // won't take the merchandise, whatever its profile says about stealing.
    private static void CheckPanic()
    {
        var map = new GridMap(StoreLayouts.Standard);
        var profile = CritterProfile.For(CritterKind.Mouser); // fast, high drive
        var underfoot = new LootItem(LootKind.Bagged, new GridPos(3, 4), Vector3.Zero);

        int grabbed = 0;
        int fled = 0;
        for (int i = 0; i < 100; i++)
        {
            var ai = new SmartAnimalAI(profile, new Random(i));
            var ctx = new CritterContext(
                CurrentCell: new GridPos(3, 4), PlayerCell: new GridPos(5, 4), Map: map,
                DeltaTime: 0.016f, CanSeePlayer: true, BeingWatched: false, Ball: null,
                LootUnderfoot: underfoot,
                LootTarget: null, LootTargetItem: null, Carrying: false, StolenCount: 0,
                AlarmStage: AlarmStage.Critical, PlayerAwareness: 0f, LastSeenPlayerCell: null,
                Treat: null, Panic: 2f);
            var step = ai.DecideNextMove(ctx);
            if (ai.WantsToSteal) grabbed++;
            if (step.HasValue) fled++;
        }

        Check.That("a panicking cat never grabs the merchandise",
            grabbed == 0, $"{grabbed}/100 grabs while panicked");
        Check.That("a panicking cat runs (makes a move every time)",
            fled > 90, $"{fled}/100 moves while panicked");

        // And the critter side: Panic() feeds the AI through the context.
        var c = new Critter(new GridPos(3, 4), 2f, new SmartAnimalAI(profile, new Random(2)), profile);
        c.Panic(1.5f);
        Check.That("Panic() is visible to the AI", c.PanicSeconds > 1.4f, $"{c.PanicSeconds:F2}s");
    }

    // The treat is a lure several cats converge on — but only cats close
    // enough to notice. The check drives the real Critter, so the context
    // plumbing (locus -> context) is exercised too.
    private static void CheckTreatLure()
    {
        var map = new GridMap(StoreLayouts.Standard);
        var profile = CritterProfile.For(CritterKind.Kitten);

        var near = new Critter(new GridPos(5, 5), 2f, new SmartAnimalAI(profile, new Random(1)), profile);
        var far = new Critter(new GridPos(16, 10), 2f, new SmartAnimalAI(profile, new Random(1)), profile);

        var treat = new GridPos(5, 4); // one step north of `near`, far from `far`
        bool nearSteppedToward = false;
        for (int i = 0; i < 30 && !nearSteppedToward; i++)
        {
            var locus = LocusFor(map, treat: treat);
            near.Update(new GameTime(TimeSpan.FromSeconds(0.1), TimeSpan.FromSeconds(0.1)), locus);
            nearSteppedToward = near.Cell == treat;
        }
        Check.That("a nearby cat heads for a dropped treat",
            nearSteppedToward, $"ended at {near.Cell.X},{near.Cell.Y}");

        // The far cat is beyond the treat radius, so the treat is not its
        // business: it should not walk all the way across the store for it.
        for (int i = 0; i < 30; i++)
        {
            var locus = LocusFor(map, treat: treat);
            far.Update(new GameTime(TimeSpan.FromSeconds(0.1), TimeSpan.FromSeconds(0.1)), locus);
        }
        var dist = Math.Abs(far.Cell.X - treat.X) + Math.Abs(far.Cell.Y - treat.Y);
        Check.That("a cat across the store ignores a treat",
            dist > SmartAnimalAI.TreatRadius,
            $"ended {dist} cells from the treat");
    }

    private static readonly GameTime Step = new(TimeSpan.FromSeconds(0.1), TimeSpan.FromSeconds(0.1));

    private static void Tick(Critter c, CritterLocus locus, float seconds)
    {
        int steps = (int)(seconds / 0.1f);
        for (int i = 0; i < steps; i++) c.Update(Step, locus);
    }

    private static Critter NewCritter(CritterKind kind)
    {
        var profile = CritterProfile.For(kind);
        return new Critter(new GridPos(3, 3), 2f, new SmartAnimalAI(profile, new Random(1)), profile);
    }

    private static CritterLocus LocusFor(GridMap map, GridPos? treat = null) => new(
        Map: map,
        PlayerCell: new GridPos(16, 10),
        Ball: null,
        LootTarget: null,
        LootTargetItem: null,
        StolenCount: 0,
        LootUnderfoot: _ => null,
        TrySteal: _ => false,
        Treat: treat);
}
