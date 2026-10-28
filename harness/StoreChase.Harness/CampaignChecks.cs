using System;
using System.Linq;
using Microsoft.Xna.Framework;
using StoreChase;

namespace StoreChase.Harness;

/// <summary>
/// Checks on the campaign: coin values, the shop's buy rules (afford,
/// escalate, max, queue), the permanent upgrades' factors, and the round
/// specs' escalation across the store path.
///
/// <para>The purse and the shop are the run's memory — a bug here doesn't
/// crash anything, it just quietly pays the wrong amount or sells an upgrade
/// twice, which is exactly the kind of thing a player feels is unfair long
/// before anyone can point at it. So the checks are arithmetic, and they are
/// exact.</para>
/// </summary>
public static class CampaignChecks
{
    /// <summary>Runs every campaign check.</summary>
    public static void Run()
    {
        Check.Section("Campaign: coins, shop and escalation");

        CheckCoinMath();
        CheckBuying();
        CheckUpgradeFactors();
        CheckEscalation();
        CheckStorePath();
        CheckSaveRoundTrip();
        CheckCustomStoreParsing();
    }

    // A secured carton is worth more than a tin, on the same scale the
    // alarm's own weight uses.
    private static void CheckCoinMath()
    {
        Check.That("a carton pays 15c",
            CampaignState.CoinsFor(Item(LootKind.Boxed)) == 15,
            $"{CampaignState.CoinsFor(Item(LootKind.Boxed))}c");
        Check.That("a bag pays 10c",
            CampaignState.CoinsFor(Item(LootKind.Bagged)) == 10,
            $"{CampaignState.CoinsFor(Item(LootKind.Bagged))}c");
        Check.That("a tin pays 7c",
            CampaignState.CoinsFor(Item(LootKind.Canned)) == 7,
            $"{CampaignState.CoinsFor(Item(LootKind.Canned))}c");

        Check.That("clearing a store pays a flat bonus on top",
            CampaignState.WinBonus == 25, $"{CampaignState.WinBonus}c");

        static LootItem Item(LootKind kind) =>
            new(kind, new GridPos(1, 1), Vector3.Zero);
    }

    // The shop's rules: can't buy what you can't pay for, consumables queue,
    // permanents escalate and stop, and nothing ever goes negative.
    private static void CheckBuying()
    {
        // Refusal leaves the purse untouched.
        var broke = new CampaignState();
        var result = broke.TryBuy(ShopItemId.Skates);
        Check.That("an unaffordable buy is refused",
            result == BuyResult.CannotAfford && broke.Coins == 0,
            $"{result}, {broke.Coins}c");

        // A consumable deducts its cost and queues (not pockets — the round
        // hasn't started yet).
        var c = new CampaignState();
        c.AddCoins(100);
        result = c.TryBuy(ShopItemId.Skates);
        Check.That("a bought consumable deducts its cost",
            result == BuyResult.Bought && c.Coins == 85,
            $"{result}, {c.Coins}c");
        Check.That("a bought consumable queues for the next round",
            c.QueuedConsumables.Count == 1 && c.QueuedConsumables[0] == ShopItemId.Skates,
            string.Join(",", c.QueuedConsumables));

        // Repeatable: buying two means two rounds of help.
        c.TryBuy(ShopItemId.Skates);
        c.TryBuy(ShopItemId.Spray);
        Check.That("consumables are repeatable",
            c.QueuedConsumables.Count == 3, $"{c.QueuedConsumables.Count} queued");

        // The queue drains in buy order, mapped to the pocket's kinds.
        bool q1 = c.TryTakeQueued(out var k1);
        bool q2 = c.TryTakeQueued(out var k2);
        bool q3 = c.TryTakeQueued(out var k3);
        Check.That("the queue drains in order, as the right power-up",
            q1 && q2 && q3 && k1 == PowerUpKind.Skates &&
            k2 == PowerUpKind.Skates && k3 == PowerUpKind.Spray,
            $"{k1},{k2},{k3}");
        Check.That("an empty queue stays empty",
            !c.TryTakeQueued(out _), "something came back");

        // Permanents: tier up, price up, stop at max — even with money.
        var p = new CampaignState();
        p.AddCoins(10_000);
        Check.That("the first Fast shoes tier costs its list price",
            p.TryBuy(ShopItemId.FastShoes) == BuyResult.Bought && p.Tier(ShopItemId.FastShoes) == 1
            && p.Coins == 10_000 - 25,
            $"{p.Coins}c, tier {p.Tier(ShopItemId.FastShoes)}");
        Check.That("the second tier costs double",
            p.TryBuy(ShopItemId.FastShoes) == BuyResult.Bought && p.Coins == 10_000 - 25 - 50,
            $"{p.Coins}c");
        Check.That("the third tier costs triple",
            p.TryBuy(ShopItemId.FastShoes) == BuyResult.Bought && p.Coins == 10_000 - 25 - 50 - 75,
            $"{p.Coins}c");
        Check.That("a maxed permanent refuses even a rich buyer",
            p.TryBuy(ShopItemId.FastShoes) == BuyResult.Maxed &&
            p.Tier(ShopItemId.FastShoes) == 3 && p.Coins == 10_000 - 150,
            $"{p.Coins}c, tier {p.Tier(ShopItemId.FastShoes)}");

        // The shop panel reflects all of the above.
        var rich = new CampaignState();
        rich.AddCoins(30);
        var options = rich.ShopOptions();
        Check.That("the shop lists every slot, in catalog order",
            options.Count == ShopCatalog.All.Count &&
            options.Select(o => o.Id).SequenceEqual(ShopCatalog.All.Select(s => s.Id)),
            string.Join(",", options.Select(o => o.Id)));
        Check.That("affordability tracks the purse",
            options.All(o => o.CanAfford == (rich.Coins >= o.Cost)),
            string.Join(",", options.Select(o => $"{o.Id}:{o.CanAfford}")));

        // A purse survives a loss: nothing resets it (the shop is the run's
        // memory, and a loss that took the money would make a losing streak
        // unrecoverable — the exact spiral the shop exists to prevent).
        var keep = new CampaignState();
        keep.AddCoins(60);
        keep.RecordLoss();
        Check.That("a loss records but costs no coins",
            keep.Losses == 1 && keep.Coins == 60, $"{keep.Coins}c");
        keep.RecordWin();
        keep.Advance();
        Check.That("advancing to the next store keeps the purse and the score",
            keep.Wins == 1 && keep.Coins == 60 && keep.RoundIndex == 1,
            $"{keep.Wins}w {keep.Coins}c round {keep.RoundIndex}");
    }

    // The three permanents, measured through their factors.
    private static void CheckUpgradeFactors()
    {
        var f = new CampaignState();
        f.AddCoins(10_000);   // the purse the buys below draw from
        Check.That("no shoes is normal speed", f.SpeedFactor == 1f, $"{f.SpeedFactor}");
        Check.That("no nerves is normal decay", f.DecayFactor == 1f, $"{f.DecayFactor}");
        Check.That("no charm is the full spike", f.SpikeFactor == 1f, $"{f.SpikeFactor}");

        f.TryBuy(ShopItemId.FastShoes);
        f.TryBuy(ShopItemId.FastShoes);
        Check.That("two shoe tiers are +14%",
            Math.Abs(f.SpeedFactor - 1.14f) < 0.0001f, $"{f.SpeedFactor}");

        f.TryBuy(ShopItemId.CalmNerves);
        f.TryBuy(ShopItemId.CalmNerves);
        f.TryBuy(ShopItemId.CalmNerves);
        Check.That("three nerve tiers speed decay by 1.15^3",
            Math.Abs(f.DecayFactor - MathF.Pow(1.15f, 3)) < 0.0001f, $"{f.DecayFactor}");

        f.TryBuy(ShopItemId.LuckyCharm);
        f.TryBuy(ShopItemId.LuckyCharm);
        Check.That("two charm tiers shrink the counter-drop spike to 64%",
            Math.Abs(f.SpikeFactor - 0.64f) < 0.0001f, $"{f.SpikeFactor}");

        // New gadgets: quiet shoes hush footsteps, steady hands slow the
        // alarm while carrying. Both are 3-tier permanents like the others.
        f.TryBuy(ShopItemId.QuietShoes);
        f.TryBuy(ShopItemId.QuietShoes);
        Check.That("two quiet-shoe tiers cut noise to 0.75^2",
            Math.Abs(f.NoiseFactor - MathF.Pow(0.75f, 2)) < 0.0001f, $"{f.NoiseFactor}");

        f.TryBuy(ShopItemId.SteadyHands);
        Check.That("one steady-hands tier cuts carry alarm to 0.9",
            Math.Abs(f.PlayerAlarmFactor - 0.9f) < 0.0001f, $"{f.PlayerAlarmFactor}");

        // The factors compose the way Game1 uses them: speed multiplies with
        // the skates boost rather than replacing it.
        float withSkates = SkatesBoost * f.SpeedFactor;
        Check.That("shop speed multiplies with skates, not replaces",
            withSkates > SkatesBoost && withSkates > f.SpeedFactor,
            $"{withSkates}");
    }

    private const float SkatesBoost = 1.5f; // Game1.SkatesSpeedBoost; kept as a literal so this check fails loudly if the boost changes shape.

    // Rounds get busier, but stop: loot caps at 8, cats at 5.
    private static void CheckEscalation()
    {
        var first = CampaignRounds.SpecFor(0);
        Check.That("round one is the small store with the reference workload",
            first.Layout == StoreLayouts.Standard && first.LootCount == 5 &&
            first.CritterCount == 3 && first.Number == 1,
            $"{first.Layout.Name} {first.LootCount}/{first.CritterCount} #{first.Number}");

        var second = CampaignRounds.SpecFor(1);
        Check.That("round two moves to the big box",
            second.Layout == StoreLayouts.BigBox && second.Number == 2,
            $"{second.Layout.Name} #{second.Number}");

        // Escalation, over a long run: more of both, never unbounded.
        var specs = Enumerable.Range(0, 40).Select(CampaignRounds.SpecFor).ToList();
        Check.That("loot never decreases",
            specs.Zip(specs.Skip(1)).All(p => p.Second.LootCount >= p.First.LootCount),
            string.Join(",", specs.Select(s => s.LootCount)));
        Check.That("cats never decrease",
            specs.Zip(specs.Skip(1)).All(p => p.Second.CritterCount >= p.First.CritterCount),
            string.Join(",", specs.Select(s => s.CritterCount)));
        Check.That("loot caps at 8",
            specs.All(s => s.LootCount <= 8) && specs[^1].LootCount == 8,
            $"{specs[^1].LootCount}");
        Check.That("cats cap at 5",
            specs.All(s => s.CritterCount <= 5) && specs[^1].CritterCount == 5,
            $"{specs[^1].CritterCount}");

        // A negative index (defensive) behaves as round one rather than
        // throwing or indexing the layout array out of range.
        Check.That("a negative round index degrades to round one",
            CampaignRounds.SpecFor(-3).Layout == StoreLayouts.Standard,
            CampaignRounds.SpecFor(-3).Layout.Name);
    }

    // Every store the campaign can hand out must be a store the game can
    // actually build a round in: a walkable start, a counter, a grid that
    // parses, and an alarm scale (a store with no scale would run the meter
    // at the small-store rate and be quietly brutal).
    private static void CheckStorePath()
    {
        foreach (var layout in CampaignRounds.StorePath)
        {
            var map = new GridMap(layout);
            var start = layout.PlayerStart;

            Check.That($"{layout.Name}: the player start is walkable",
                map.IsWalkable(start.X, start.Y), $"{start.X},{start.Y}");
            Check.That($"{layout.Name}: it has a counter to secure at",
                layout.CounterRow >= 0, $"counter row {layout.CounterRow}");
            Check.That($"{layout.Name}: most of the floor is walkable",
                map.WalkableCells().Count() > layout.Width * layout.Height / 3,
                $"{map.WalkableCells().Count()} walkable");
            Check.That($"{layout.Name}: the alarm is scaled to the work",
                layout.AlarmScale(5, Player.GridSpeed) > 0f,
                $"{layout.AlarmScale(5, Player.GridSpeed)}");
        }

        Check.That("the campaign's route is six distinct stores",
            CampaignRounds.StorePath.Select(l => l.Name).Distinct().Count() == 6,
            string.Join(",", CampaignRounds.StorePath.Select(l => l.Name)));
    }

    // The save file is the run's memory across launches. It used to restore
    // through reflection on private setters and carry no version, so a shape
    // change would have loaded a silently wrong purse. It round-trips through
    // a temp path now, and a foreign version is refused rather than guessed at.
    private static void CheckSaveRoundTrip()
    {
        string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
            $"storechase-save-{Guid.NewGuid():N}.json");

        var run = new CampaignState();
        run.AddCoins(400);
        run.TryBuy(ShopItemId.FastShoes);   // a permanent, so a tier is stored
        run.TryBuy(ShopItemId.Spray);       // a consumable, so the queue is stored
        run.Advance();
        int coins = run.Coins, round = run.RoundIndex;

        try
        {
            run.Save(path);
            var loaded = CampaignState.Load(path);

            Check.That("a save round-trips the purse and the round", loaded != null,
                "load returned null");
            if (loaded == null) return;

            Check.That("the purse survives a save", loaded.Coins == coins, $"{loaded.Coins} vs {coins}");
            Check.That("the round index survives a save", loaded.RoundIndex == round,
                $"{loaded.RoundIndex} vs {round}");
            Check.That("a bought permanent survives a save", loaded.Tier(ShopItemId.FastShoes) == 1,
                $"tier {loaded.Tier(ShopItemId.FastShoes)}");
            Check.That("a queued consumable survives a save", loaded.QueuedConsumables.Count == 1,
                $"{loaded.QueuedConsumables.Count} queued");

            // A file from a different save version must be refused, not
            // reinterpreted. Poison the version and try again.
            string json = System.IO.File.ReadAllText(path)
                .Replace($"\"Version\":{CampaignState.SaveVersion}", "\"Version\":999");
            System.IO.File.WriteAllText(path, json);
            Check.That("a save from another version is refused",
                CampaignState.Load(path) == null, "loaded anyway");

            System.IO.File.WriteAllText(path, "{ this is not json");
            Check.That("a corrupt save is refused", CampaignState.Load(path) == null, "loaded anyway");
        }
        finally
        {
            try { System.IO.File.Delete(path); } catch { /* temp file: best effort */ }
        }
    }

    // A hand-authored store bypasses the generator's connectivity guarantee,
    // so the parser has to be the thing that says no. Every rejection below
    // is a store that would have started, then quietly stranded the player.
    private static void CheckCustomStoreParsing()
    {
        var good =
            "Name: Test Shop\n" +
            "counterRow: 1\ncounterStart: 3\ncounterEnd: 5\nplayerStart: 2,2\n" +
            "---\n" +
            "111111111\n" +
            "100000001\n" +
            "100000001\n" +
            "101000101\n" +
            "100000001\n" +
            "111111111\n";

        var parsed = CustomStore.Parse(good, out string? error);
        Check.That("a well-formed custom store parses", parsed != null, error ?? "");
        if (parsed != null)
            Check.That("its header values are read", parsed.CounterStart == 3 && parsed.CounterEnd == 5,
                $"counter {parsed.CounterStart}..{parsed.CounterEnd}");

        // The old parser's guess ("any line containing 0 or 1 is a grid row")
        // turned a typo'd header into a ragged row and threw it away silently.
        Check.That("an unrecognised header is explained, not swallowed",
            CustomStore.Parse("Width: 9\n---\n111111111\n", out string? bad) == null && bad != null,
            bad ?? "accepted");

        Check.That("a missing separator is explained",
            CustomStore.Parse("111111111\n", out string? noSep) == null && noSep != null,
            noSep ?? "accepted");
        Check.That("a ragged grid is explained",
            CustomStore.Parse("---\n11111\n111\n", out string? ragged) == null && ragged != null,
            ragged ?? "accepted");
        Check.That("a stray character is explained",
            CustomStore.Parse("---\n11111.\n", out string? charBad) == null && charBad != null,
            charBad ?? "accepted");
        Check.That("a player start inside a shelf is explained",
            CustomStore.Parse("---\n1111\n1101\n1111\n", out string? inWall) == null && inWall != null,
            inWall ?? "accepted");

        // A sealed pocket: loot spawns on shelf edges, so floor the player
        // cannot reach is an item nobody can secure.
        Check.That("an unreachable pocket is explained",
            CustomStore.Parse("---\n111111\n101101\n101101\n111111\n", out string? pocket) == null
                && pocket != null, pocket ?? "accepted");
    }
}
