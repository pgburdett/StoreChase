using System;
using System.Collections.Generic;

namespace StoreChase;

/// <summary>What the shop sells. Slot order is the key order (1..6).</summary>
public enum ShopItemId
{
    Skates,
    Spray,
    Treat,
    FastShoes,
    CalmNerves,
    LuckyCharm,
    QuietShoes,
    SteadyHands,
    SmokeBomb,
}

/// <summary>How a purchase attempt ended.</summary>
public enum BuyResult
{
    Bought,
    CannotAfford,
    Maxed,
}

/// <summary>
/// One row of the between-rounds shop, as the HUD needs it: what it is, what
/// it costs *right now* (permanents price per tier), and whether this
/// campaign can currently afford it.
/// </summary>
public readonly record struct ShopOption(
    ShopItemId Id,
    string Name,
    int Cost,
    int Tier,
    int MaxTier,
    bool Consumable,
    bool CanAfford);

/// <summary>
/// A shop item. Consumables (pocket power-ups) are repeatable and have no
/// tier; permanents are one purchase per tier, up to <see cref="MaxTier"/>.
/// </summary>
public sealed record ShopItem(
    ShopItemId Id,
    string Name,
    string Description,
    int Cost,
    bool Consumable,
    int MaxTier = 1)
{
    /// <summary>
    /// What buying the *next* one costs: a flat price for consumables, and a
    /// rising one per permanent tier (the good stuff gets dearer as you
    /// near the top, so late upgrades are a real decision).
    /// </summary>
    public int CostForTier(int currentTier) =>
        Consumable ? Cost : Cost * (currentTier + 1);
}

/// <summary>The shop's stock, in key-slot order.</summary>
public static class ShopCatalog
{
    private static readonly ShopItem[] Stock =
    {
        new ShopItem(ShopItemId.Skates, "Roller skates",
            "Pocket one skate-use for the next store",
            15, Consumable: true),
        new ShopItem(ShopItemId.Spray, "Spray bottle",
            "Pocket one spray-use: panic the cats near you",
            20, Consumable: true),
        new ShopItem(ShopItemId.Treat, "Cat treat",
            "Pocket one treat: drop it to lure the cats",
            18, Consumable: true),
        new ShopItem(ShopItemId.FastShoes, "Fast shoes",
            "+7% walk speed for the rest of the run (3 tiers)",
            25, Consumable: false, MaxTier: 3),
        new ShopItem(ShopItemId.CalmNerves, "Calm nerves",
            "The alarm drains 15% faster between scares (3 tiers)",
            30, Consumable: false, MaxTier: 3),
        new ShopItem(ShopItemId.LuckyCharm, "Lucky charm",
            "A thief's counter-dump spikes the alarm 20% less (3 tiers)",
            35, Consumable: false, MaxTier: 3),
        new ShopItem(ShopItemId.QuietShoes, "Quiet shoes",
            "Your footsteps are 25% quieter per tier (3 tiers)",
            28, Consumable: false, MaxTier: 3),
        new ShopItem(ShopItemId.SteadyHands, "Steady hands",
            "The alarm rises 10% slower while you carry goods (3 tiers)",
            32, Consumable: false, MaxTier: 3),
        new ShopItem(ShopItemId.SmokeBomb, "Smoke bomb",
            "Pocket one: panic every cat in earshot for six seconds",
            24, Consumable: true),
    };

    /// <summary>Six items: one per number key, consumables first.</summary>
    public static IReadOnlyList<ShopItem> All => Stock;

    public static ShopItem Get(ShopItemId id) => Stock[(int)id];
}

/// <summary>
/// A round of the campaign: which store, and how much work is in it.
/// Escalation lives here (see <see cref="CampaignRounds.SpecFor"/>), not in
/// <see cref="Game1"/>'s constants — those stay as the reference values the
/// round simulation is tuned against.
/// </summary>
/// <param name="Number">One-based round number, for "Round 3" on the HUD.</param>
public sealed record RoundSpec(StoreLayout Layout, int LootCount, int CritterCount, int Number);

/// <summary>The campaign's escalating floor plans and workloads.</summary>
public static class CampaignRounds
{
    // The route through the run: the two hand-made stores, then two bigger
    // generated ones you settle into — after round 4 the store stops
    // changing and only the workload grows (loot to 8, cats to 5).
    private static readonly StoreLayout[] Layouts =
    {
        StoreLayouts.Standard,
        StoreLayouts.BigBox,
        StoreLayouts.Generate("Cross-Town Market", 30, 20, seed: 20261015),
        StoreLayouts.Generate("The Warehouse", 34, 22, seed: 20261016),
    };

    /// <summary>Every loot/critter count the campaign reaches, for tests.</summary>
    public static IReadOnlyList<StoreLayout> StorePath => Layouts;

    /// <summary>
    /// The spec for a round, 0-based. Loot grows by one every two rounds
    /// (capped at 8), critters every three (capped at 5) — enough escalation
    /// to keep late rounds tight without the alarm becoming unwinnable.
    /// </summary>
    public static RoundSpec SpecFor(int roundIndex)
    {
        int i = Math.Max(0, roundIndex);
        var layout = i < Layouts.Length ? Layouts[i] : Layouts[^1];
        int loot = Math.Min(5 + i / 2, 8);
        int critters = Math.Min(3 + i / 3, 5);
        return new RoundSpec(layout, loot, critters, i + 1);
    }
}

/// <summary>
/// The run: which round we're on, the purse, and everything bought so far.
///
/// <para>Coins come from securing loot (its alarm weight × 10 — a carton is
/// worth more than a tin, same scale the alarm itself uses) plus a win
/// bonus. They are kept across losses: a loss costs you the round, not the
/// run. Consumables bought here are queued and pocketed at the start of the
/// next round (one slot, so two buys = two rounds of help).</para>
///
/// <para>Entirely headless — Game1 owns the buying input and the HUD draws
/// <see cref="ShopOptions"/>, so all of this runs in the harness.</para>
/// </summary>
public sealed class CampaignState
{
    /// <summary>Coins per alarm-weight unit of secured loot.</summary>
    public const int CoinsPerWeight = 10;

    /// <summary>Bonus for clearing a store.</summary>
    public const int WinBonus = 25;

    /// <summary>0-based index of the round being played.</summary>
    public int RoundIndex { get; private set; }

    public int Coins { get; private set; }
    public int Wins { get; private set; }
    public int Losses { get; private set; }

    private readonly Dictionary<ShopItemId, int> _tiers = new();
    private readonly List<ShopItemId> _queue = new();

    /// <summary>The round this campaign is currently on.</summary>
    public RoundSpec CurrentSpec => CampaignRounds.SpecFor(RoundIndex);

    /// <summary>How many tiers of a permanent are owned (0 = not owned).</summary>
    public int Tier(ShopItemId id) => _tiers.GetValueOrDefault(id);

    /// <summary>Consumables bought and waiting to be pocketed, in buy order.</summary>
    public IReadOnlyList<ShopItemId> QueuedConsumables => _queue;

    /// <summary>Coins per unit of alarm weight: a tin 7, a bag 10, a carton 15.</summary>
    public static int CoinsFor(LootItem item) =>
        (int)MathF.Round(item.AlarmWeight * CoinsPerWeight);

    /// <summary>Adds a purse top-up (securing loot, win bonus).</summary>
    public void AddCoins(int amount)
    {
        if (amount > 0) Coins += amount;
    }

    /// <summary>
    /// Tries to buy the item. Permanents escalate in price per tier and
    /// stop at <see cref="ShopItem.MaxTier"/>; consumables queue up.
    /// </summary>
    public BuyResult TryBuy(ShopItemId id)
    {
        var item = ShopCatalog.Get(id);
        int tier = Tier(id);

        if (!item.Consumable && tier >= item.MaxTier) return BuyResult.Maxed;

        int cost = item.CostForTier(tier);
        if (Coins < cost) return BuyResult.CannotAfford;

        Coins -= cost;
        if (item.Consumable) _queue.Add(id);
        else _tiers[id] = tier + 1;

        return BuyResult.Bought;
    }

    /// <summary>
    /// Dequeues the next bought consumable, to be pocketed at round start.
    /// </summary>
    public bool TryTakeQueued(out PowerUpKind kind)
    {
        if (_queue.Count == 0)
        {
            kind = default;
            return false;
        }

        kind = _queue[0] switch
        {
            ShopItemId.Skates => PowerUpKind.Skates,
            ShopItemId.Spray => PowerUpKind.Spray,
            ShopItemId.Treat => PowerUpKind.Treat,
            ShopItemId.SmokeBomb => PowerUpKind.SmokeBomb,
            _ => PowerUpKind.Treat,
        };
        _queue.RemoveAt(0);
        return true;
    }

    /// <summary>Moves to the next store (called on a win).</summary>
    public void Advance() => RoundIndex++;

    public void RecordWin() => Wins++;
    public void RecordLoss() => Losses++;

    // ---- Permanent effects ----------------------------------------------

    /// <summary>Walk-speed multiplier from Fast shoes tiers.</summary>
    public float SpeedFactor => 1f + 0.07f * Tier(ShopItemId.FastShoes);

    /// <summary>Alarm-decay multiplier from Calm nerves tiers (faster drain).</summary>
    public float DecayFactor => MathF.Pow(1.15f, Tier(ShopItemId.CalmNerves));

    /// <summary>Counter-drop spike multiplier from Lucky charm tiers (smaller hit).</summary>
    public float SpikeFactor => MathF.Pow(0.8f, Tier(ShopItemId.LuckyCharm));

    /// <summary>Footstep-noise multiplier for the run (quiet shoes).</summary>
    public float NoiseFactor => MathF.Pow(0.75f, Tier(ShopItemId.QuietShoes));

    /// <summary>Alarm rise multiplier while the player carries goods (steady hands).</summary>
    public float PlayerAlarmFactor => MathF.Pow(0.9f, Tier(ShopItemId.SteadyHands));

    /// <summary>The whole shop as the HUD should draw it this frame.</summary>
    public IReadOnlyList<ShopOption> ShopOptions()
    {
        var options = new List<ShopOption>(ShopCatalog.All.Count);
        foreach (var item in ShopCatalog.All)
        {
            int tier = Tier(item.Id);
            int cost = item.CostForTier(tier);
            options.Add(new ShopOption(
                item.Id, item.Name, cost, tier, item.MaxTier,
                item.Consumable, CanAfford: Coins >= cost));
        }
        return options;
    }
}
