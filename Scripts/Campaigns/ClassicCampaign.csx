// Scripts/Campaigns/ClassicCampaign.csx
// The original campaign progression, now scriptable for easy tweaking.

using System;
using System.Collections.Generic;
using System.Linq;
using StoreChase;
using StoreChase.Scripting;

public class ClassicCampaign : ScriptedCampaignBase
{
    public override void Initialize()
    {
        ScriptServices.Log("ClassicCampaign initialized (scripted version)");
    }

    public override int TotalRounds => 16; // 4 stores × 4 rounds each

    public override RoundSpec SpecFor(int roundIndex)
    {
        // Original progression: 5 loot, 3 cats -> 8 loot, 5 cats over 16 rounds
        int lootCount = Math.Min(8, 5 + roundIndex / 2);
        int critterCount = Math.Min(5, 3 + roundIndex / 3);

        // Store progression: Standard -> BigBox -> CornerStore -> Midtown -> repeat Midtown
        StoreLayout layout = roundIndex switch
        {
            < 4 => StoreLayouts.Standard,
            < 8 => StoreLayouts.BigBox,
            < 12 => StoreLayouts.CornerStore,
            _ => StoreLayouts.Midtown
        };

        // Boss every 4 rounds (round 3, 7, 11, 15)
        bool bossRound = (roundIndex % 4 == 3);

        // Alarm scale based on store size
        float alarmScale = layout.AlarmScale(lootCount, Player.GridSpeed);

        ScriptServices.Log($"Round {roundIndex + 1}: {layout.Name} | Loot: {lootCount} | Cats: {critterCount} | Boss: {bossRound}");

        return MakeSpec(lootCount, critterCount, layout, alarmScale, bossRound);
    }

    public override IReadOnlyList<ShopItem> CreateShop()
    {
        // Return the default shop catalog
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
        ScriptServices.Log($"Round {state.RoundIndex + 1}/{TotalRounds}: {spec.Layout.Name}");
    }

    public override void OnRoundEnd(CampaignState state, bool won)
    {
        if (won)
        {
            int winBonus = CampaignState.WinBonus;
            state.AddCoins(winBonus);
            ScriptServices.Log($"Round won! +{winBonus} coins. Total: {state.Coins}");
        }
        else
        {
            ScriptServices.Log($"Round lost. Coins: {state.Coins}");
        }
    }
}

new ClassicCampaign()