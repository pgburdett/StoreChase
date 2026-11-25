// Scripts/Campaigns/RogueLikeCampaign.csx
// A roguelike-style campaign with escalating difficulty, procedural layouts,
// and meaningful choices between rounds.

using System;
using System.Collections.Generic;
using System.Linq;
using StoreChase;
using StoreChase.Scripting;

public class RogueLikeCampaign : ScriptedCampaignBase
{
    private List<StoreLayout> _layoutPool;
    private int _currentDifficultyTier;

    public override void Initialize()
    {
        _layoutPool = new List<StoreLayout>
        {
            StoreLayouts.Standard,
            StoreLayouts.BigBox,
            StoreLayouts.CornerStore,
            StoreLayouts.Midtown,
        };
        _currentDifficultyTier = 0;
        ScriptServices.Log("RogueLikeCampaign initialized");
    }

    public override int TotalRounds => 20;

    public override RoundSpec SpecFor(int roundIndex)
    {
        int tier = roundIndex / 4;
        _currentDifficultyTier = tier;

        int baseLoot = 5 + roundIndex / 2;
        int baseCritters = 3 + roundIndex / 3;

        int lootCount = Math.Min(12, baseLoot + tier);
        int critterCount = Math.Min(8, baseCritters + tier);

        StoreLayout layout = _layoutPool[roundIndex % _layoutPool.Count];
        if (tier >= 2 && layout == StoreLayouts.CornerStore)
            layout = StoreLayouts.BigBox;

        bool bossRound = (roundIndex % 4 == 3);

        float alarmScale = 1f + tier * 0.15f;
        if (tier >= 1) alarmScale *= 1.1f;
        if (tier >= 2) alarmScale *= 1.15f;
        if (tier >= 3) alarmScale *= 1.25f;

        ScriptServices.Log($"Round {roundIndex + 1}: Loot={lootCount}, Critters={critterCount}, Layout={layout.Name}, Boss={bossRound}");

        return MakeSpec(lootCount, critterCount, layout, alarmScale, bossRound);
    }

    public override IReadOnlyList<ShopItem> CreateShop()
    {
        return ShopCatalog.All;
    }

    public override StoreLayout LayoutFor(int roundIndex)
    {
        return SpecFor(roundIndex).Layout;
    }

    public override bool ShouldAdvance(CampaignState state)
    {
        return state.Wins > state.Losses;
    }

    public override void OnRoundStart(CampaignState state)
    {
        var spec = SpecFor(state.RoundIndex);
        ScriptServices.Log($"=== ROUND {state.RoundIndex + 1} START ===");
        ScriptServices.Log($"Store: {spec.Layout.Name} | Loot: {spec.LootCount} | Cats: {spec.CritterCount}");

        if (spec.Number % 4 == 0)
            ScriptServices.Log("!!! BOSS ROUND !!!");
    }

    public override void OnRoundEnd(CampaignState state, bool won)
    {
        ScriptServices.Log($"=== ROUND {state.RoundIndex + 1} END: {(won ? "VICTORY" : "DEFEAT")} ===");
        ScriptServices.Log($"Coins: {state.Coins} | Wins: {state.Wins} | Losses: {state.Losses}");

        if (won)
        {
            int bonus = 10 + _currentDifficultyTier * 5;
            state.AddCoins(bonus);
            ScriptServices.Log($"Victory bonus: +{bonus} coins");
        }
        else if (_currentDifficultyTier >= 2)
        {
            state.AddCoins(5);
            ScriptServices.Log("Consolation: +5 coins");
        }
    }
}

new RogueLikeCampaign()