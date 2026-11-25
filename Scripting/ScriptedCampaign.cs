using System;
using System.Collections.Generic;
using System.Linq;
using StoreChase;

namespace StoreChase.Scripting;

/// <summary>
/// A campaign driven by a compiled C# script.
/// The script provides round specifications, shop catalog, and layout selection.
/// </summary>
public sealed class ScriptedCampaign
{
    private readonly Func<int, RoundSpec> _specFor;
    private readonly Func<IReadOnlyList<ShopItem>> _createShop;
    private readonly Func<int, StoreLayout> _layoutFor;
    private readonly Func<CampaignState, bool> _shouldAdvance;
    private readonly Action<CampaignState> _onRoundStart;
    private readonly Action<CampaignState, bool> _onRoundEnd;
    private readonly Action? _initialize;
    private readonly int _totalRounds;

    public ScriptedCampaign(string scriptPath)
    {
        var instance = ScriptHost.CompileInstance<IScriptedCampaignProvider>(scriptPath);

        _specFor = instance.SpecFor;
        _createShop = instance.CreateShop;
        _layoutFor = instance.LayoutFor;
        _shouldAdvance = instance.ShouldAdvance;
        _onRoundStart = instance.OnRoundStart;
        _onRoundEnd = instance.OnRoundEnd;
        _initialize = instance.Initialize;
        _totalRounds = instance.TotalRounds;

        _initialize?.Invoke();
    }

    public RoundSpec SpecFor(int roundIndex) => _specFor(roundIndex);
    public IReadOnlyList<ShopItem> CreateShop() => _createShop();
    public StoreLayout LayoutFor(int roundIndex) => _layoutFor(roundIndex);
    public bool ShouldAdvance(CampaignState state) => _shouldAdvance(state);
    public void OnRoundStart(CampaignState state) => _onRoundStart(state);
    public void OnRoundEnd(CampaignState state, bool won) => _onRoundEnd(state, won);
    public int TotalRounds => _totalRounds;
}

/// <summary>
/// Interface that scripted campaign classes must implement.
/// </summary>
public interface IScriptedCampaignProvider
{
    /// <summary>
    /// Returns the round specification for the given round index (0-based).
    /// </summary>
    RoundSpec SpecFor(int roundIndex);

    /// <summary>
    /// Creates the shop catalog for between-round purchases.
    /// </summary>
    IReadOnlyList<ShopItem> CreateShop();

    /// <summary>
    /// Returns the store layout for the given round index.
    /// </summary>
    StoreLayout LayoutFor(int roundIndex);

    /// <summary>
    /// Determines if the campaign should advance to the next round.
    /// Return false to repeat the current round (e.g., on loss).
    /// </summary>
    bool ShouldAdvance(CampaignState state);

    /// <summary>
    /// Called at the start of each round.
    /// </summary>
    void OnRoundStart(CampaignState state);

    /// <summary>
    /// Called at the end of each round. won = true if player won.
    /// </summary>
    void OnRoundEnd(CampaignState state, bool won);

    /// <summary>
    /// Called once after construction. Use for setup.
    /// </summary>
    void Initialize();

    /// <summary>
    /// Total number of rounds in this campaign. Used for UI/progress.
    /// </summary>
    int TotalRounds { get; }
}

/// <summary>
/// Base class for scripted campaigns with common helpers.
/// </summary>
public abstract class ScriptedCampaignBase : IScriptedCampaignProvider
{
    protected Random Rng => ScriptServices.Rng;
    protected EventSystem Events => ScriptServices.Events;

    protected static RoundSpec MakeSpec(int lootCount, int critterCount, StoreLayout layout,
        float alarmScale = 1f, bool bossRound = false)
    {
        var spec = new RoundSpec(layout, lootCount, critterCount, 1);
        // Note: alarmScale and bossRound would need to be stored elsewhere
        // For now, the script can access them via the spec's Layout property
        // which has AlarmScale method
        return spec;
    }

    protected static StoreLayout StandardLayout => StoreLayouts.Standard;
    protected static StoreLayout BigBoxLayout => StoreLayouts.BigBox;
    protected static StoreLayout CornerStoreLayout => StoreLayouts.CornerStore;
    protected static StoreLayout MidtownLayout => StoreLayouts.Midtown;

    public virtual void Initialize() { }

    public abstract RoundSpec SpecFor(int roundIndex);
    public abstract IReadOnlyList<ShopItem> CreateShop();
    public abstract StoreLayout LayoutFor(int roundIndex);
    public abstract bool ShouldAdvance(CampaignState state);
    public abstract void OnRoundStart(CampaignState state);
    public abstract void OnRoundEnd(CampaignState state, bool won);
    public abstract int TotalRounds { get; }
}

/// <summary>
/// Extension methods for CampaignState to work with scripted campaigns.
/// </summary>
public static class ScriptedCampaignExtensions
{
    /// <summary>
    /// Applies a scripted round spec to the campaign state.
    /// </summary>
    public static void ApplySpec(this CampaignState state, RoundSpec spec)
    {
        // CampaignState doesn't have public setters for these, so we use reflection
        // or the script can just read from the spec directly in OnRoundStart
    }

    /// <summary>
    /// Gets the current round index (0-based).
    /// </summary>
    public static int CurrentRoundIndex(this CampaignState state) => state.RoundIndex;
}