using System;
using Microsoft.Xna.Framework;
using StoreChase;

namespace StoreChase.Harness;

/// <summary>
/// Checks on the befriending mechanic: feeding a cat treats until it stops
/// being a thief and starts being a companion.
///
/// The feature has three load-bearing promises, and each is checked where it
/// lives: a friend never steals (the AI), a friend cannot be caught by
/// collision (Critter), and feeding is two treats exactly (Critter.Feed).
/// Anything softer than that — the following, the poses, the feed messages
/// — is presentation on top of these.
/// </summary>
public static class FriendChecks
{
    /// <summary>Runs every befriending check.</summary>
    public static void Run()
    {
        Check.Section("Friends: feeding cats");
        CheckFeeding();

        Check.Section("Friends: what a friend does");
        CheckFriendBehaviour();
    }

    private static void CheckFeeding()
    {
        var cat = MakeCritter(new GridPos(3, 3));

        bool first = cat.Feed();
        Check.That("one treat is not enough", !cat.Befriended && !first);

        bool second = cat.Feed();
        Check.That("two treats befriend a cat", cat.Befriended && second,
            $"threshold announced: {second}");

        bool third = cat.Feed();
        Check.That("a third treat is just a snack", cat.Befriended && !third,
            $"friendship {cat.Friendship}");
    }

    private static void CheckFriendBehaviour()
    {
        var map = new GridMap(StoreLayouts.Standard);
        var profile = CritterProfile.For(CritterKind.ShopCat);

        // A friend never steals, whatever is under its paws — even when the
        // store is about to go off (the critical override is the one thing
        // that persuades ordinary cats, and it must not persuade a friend).
        int takes = 0;
        for (int i = 0; i < 100; i++)
        {
            var ai = new SmartAnimalAI(profile, new Random(i));
            var ctx = new CritterContext(
                CurrentCell: new GridPos(3, 3), PlayerCell: new GridPos(16, 10), Map: map,
                DeltaTime: 0.1f, CanSeePlayer: false, BeingWatched: false, Ball: null,
                LootUnderfoot: new LootItem(LootKind.Boxed, new GridPos(3, 3), Vector3.Zero),
                LootTarget: null, LootTargetItem: null, Carrying: false, StolenCount: 3,
                AlarmStage: AlarmStage.Critical,
                PlayerAwareness: 0f, LastSeenPlayerCell: null,
                Befriended: true);
            ai.DecideNextMove(ctx);
            if (ai.WantsToSteal) takes++;
        }
        Check.That("a friend never steals, not even at critical alarm",
            takes == 0, $"{takes}/100 grabs");

        // A friend does not flee the player: seen at fleeradius, it stays
        // put rather than running from the one it was fed by.
        var aiSeen = new SmartAnimalAI(profile, new Random(1));
        var seen = aiSeen.DecideNextMove(Ctx(map, new GridPos(5, 5), new GridPos(5, 5 + 2),
            canSee: true, befriended: true));
        Check.That("a friend does not flee a player it can see",
            seen == null, $"stepped to {seen?.X},{seen?.Y}");

        // A friend that falls behind catches up: it walks towards the player.
        // Walking distance, not Manhattan — the first step around a shelf can
        // move away while closing the route.
        int approaches = 0;
        for (int i = 0; i < 20; i++)
        {
            var here = new GridPos(3, 3);
            var player = new GridPos(9, 3);
            var ai = new SmartAnimalAI(profile, new Random(i));
            var next = ai.DecideNextMove(Ctx(map, here, player, canSee: false, befriended: true));
            var walks = map.DistanceMapFrom(player);
            if (next.HasValue && walks[next.Value.X, next.Value.Y] < walks[here.X, here.Y])
                approaches++;
        }
        Check.That("a friend that falls behind walks to the player",
            approaches > 15, $"{approaches}/20 steps approach");

        // A friend that is walked into gives ground instead of sharing the
        // player's cell (sharing would clip two bodies together).
        var aiClose = new SmartAnimalAI(profile, new Random(1));
        var stepped = aiClose.DecideNextMove(Ctx(map, new GridPos(5, 4), new GridPos(5, 4),
            canSee: false, befriended: true));
        Check.That("a friend underfoot gives ground",
            stepped.HasValue && Dist(stepped.Value, new GridPos(5, 4)) == 1,
            $"stepped to {stepped?.X},{stepped?.Y}");

        // Eating: standing on a treat is wanting the treat (and the game
        // turns it into friendship); merely being near one is going to it.
        var aiEat = new SmartAnimalAI(profile, new Random(1));
        var eatCtx = Ctx(map, new GridPos(3, 3), new GridPos(16, 10),
            canSee: false, befriended: false, treat: new GridPos(3, 3));
        var stay = aiEat.DecideNextMove(eatCtx);
        Check.That("a cat on a treat stops to eat it",
            aiEat.WantsTreat && stay == null, $"wanted {aiEat.WantsTreat}, moved {stay != null}");

        var aiFetch = new SmartAnimalAI(profile, new Random(1));
        var fetchTreat = new GridPos(5, 3);
        var fetchCtx = Ctx(map, new GridPos(3, 3), new GridPos(16, 10),
            canSee: false, befriended: false, treat: fetchTreat);
        var goEat = aiFetch.DecideNextMove(fetchCtx);
        var treatWalks = map.DistanceMapFrom(fetchTreat);
        Check.That("a cat near a treat walks to it first",
            !aiFetch.WantsTreat && goEat.HasValue
            && treatWalks[goEat.Value.X, goEat.Value.Y] < treatWalks[3, 3],
            $"wanted {aiFetch.WantsTreat}, went {goEat?.X},{goEat?.Y}");

        // A friend is not a thief: collision must not remove it. A cat that
        // has not been befriended standing in the same cell is a tackle.
        var friend = MakeCritter(new GridPos(5, 4));
        for (int i = 0; i < 3; i++) friend.Feed();
        var friendLocus = Locus(map, friend.Cell);
        friend.Update(Frame(), friendLocus);
        Check.That("the player cannot tackle a friend", !friend.Tackled);

        var stranger = MakeCritter(new GridPos(5, 4));
        var strangerLocus = Locus(map, stranger.Cell);
        stranger.Update(Frame(), strangerLocus);
        Check.That("an unfed cat in the same cell is still a tackle", stranger.Tackled);
    }

    // ---- Helpers ----------------------------------------------------------

    private static CritterContext Ctx(GridMap map, GridPos here, GridPos player,
        bool canSee, bool befriended, GridPos? treat = null) => new(
        CurrentCell: here, PlayerCell: player, Map: map,
        DeltaTime: 0.1f, CanSeePlayer: canSee, BeingWatched: false, Ball: null,
        LootUnderfoot: null, LootTarget: null, LootTargetItem: null,
        Carrying: false, StolenCount: 0, AlarmStage: AlarmStage.Calm,
        PlayerAwareness: 0f, LastSeenPlayerCell: null,
        Treat: treat, Panic: 0f, Befriended: befriended);

    private static Critter MakeCritter(GridPos cell)
    {
        var profile = CritterProfile.For(CritterKind.ShopCat);
        return new Critter(cell, 2f, new SmartAnimalAI(profile, new Random(0)), profile);
    }

    private static CritterLocus Locus(GridMap map, GridPos playerCell) => new(
        Map: map,
        PlayerCell: playerCell,
        Ball: null,
        LootTarget: null,
        LootTargetItem: null,
        StolenCount: 0,
        LootUnderfoot: _ => null,
        TrySteal: _ => false);

    private static GameTime Frame() =>
        new(TimeSpan.Zero, TimeSpan.FromSeconds(0.1));

    private static int Dist(GridPos a, GridPos b) =>
        Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y);
}
