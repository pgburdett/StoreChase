using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using StoreChase;

namespace StoreChase.Scripting;

/// <summary>
/// A critter AI driven by a compiled C# script.
/// The script provides delegates for each ICritterAI member.
/// </summary>
public sealed class ScriptedAI : ICritterAI
{
    private readonly CritterProfile _profile;
    private readonly Func<CritterContext, GridPos?> _decideNextMove;
    private readonly Func<CritterContext, bool> _wantsToSteal;
    private readonly Func<CritterContext, bool> _wantsTreat;
    private readonly Func<CritterContext, CatPose> _getPose;
    private readonly Func<CritterContext, AIState> _getState;
    private readonly Action? _initialize;
    private readonly Action? _reset;

    private CritterContext _lastContext;

    public ScriptedAI(CritterProfile profile, Random rng, string scriptPath)
    {
        _profile = profile;

        // Compile the script - it must define a class with specific methods
        var instance = ScriptHost.CompileInstance<IScriptedAIProvider>(scriptPath);

        // Set Profile on the base class if it inherits from ScriptedAIBase
        if (instance is ScriptedAIBase baseInstance)
        {
            baseInstance.Profile = profile;
        }

        _decideNextMove = instance.DecideNextMove;
        _wantsToSteal = instance.WantsToSteal;
        _wantsTreat = instance.WantsTreat;
        _getPose = instance.GetPose;
        _getState = instance.GetState;
        _initialize = instance.Initialize;
        _reset = instance.Reset;

        // Initialize with profile and RNG
        _initialize?.Invoke();
    }

    public GridPos? DecideNextMove(CritterContext context)
    {
        _lastContext = context;
        return _decideNextMove(context);
    }

    public bool WantsToSteal => _wantsToSteal(_lastContext);

    public bool WantsTreat => _wantsTreat(_lastContext);

    public CatPose Pose => _getPose(_lastContext);

    public AIState State => _getState(_lastContext);

    public CritterProfile Profile => _profile;

    /// <summary>
    /// Call when the critter is tackled/stunned to reset AI state.
    /// </summary>
    public void Reset()
    {
        _reset?.Invoke();
    }
}

/// <summary>
/// Interface that scripted AI classes must implement.
/// This is the contract between the host and the script.
/// </summary>
public interface IScriptedAIProvider
{
    /// <summary>
    /// Main decision function. Return the next cell to move to, or null to stay.
    /// </summary>
    GridPos? DecideNextMove(CritterContext context);

    /// <summary>
    /// Return true to pick up loot underfoot.
    /// </summary>
    bool WantsToSteal(CritterContext context);

    /// <summary>
    /// Return true to eat a treat underfoot.
    /// </summary>
    bool WantsTreat(CritterContext context);

    /// <summary>
    /// Return the pose for the animation rig.
    /// </summary>
    CatPose GetPose(CritterContext context);

    /// <summary>
    /// Return the AI state for HUD/debug.
    /// </summary>
    AIState GetState(CritterContext context);

    /// <summary>
    /// Called once after construction. Use for setup.
    /// </summary>
    void Initialize();

    /// <summary>
    /// Called when the critter is tackled or stunned.
    /// </summary>
    void Reset();
}

/// <summary>
/// Base class for scripted AIs to inherit from (optional but convenient).
/// Provides common helpers and access to ScriptServices.
/// </summary>
public abstract class ScriptedAIBase : IScriptedAIProvider
{
    public CritterProfile Profile { get; set; } = null!;
    protected Random Rng => ScriptServices.Rng;
    protected EventSystem Events => ScriptServices.Events;

    protected int ManhattanDistance(GridPos a, GridPos b)
        => Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y);

    protected GridPos? PathStepToward(GridMap map, GridPos from, GridPos to)
        => from == to ? null : map.NextStepToward(from, to);

    protected List<GridPos> BuildPathTo(GridMap map, GridPos from, GridPos to)
        => map.PathTo(from, to);

    protected GridPos? NearestCounterCell(GridPos from, GridMap map)
    {
        GridPos? best = null;
        int bestDist = int.MaxValue;
        for (int x = map.CounterStart; x <= map.CounterEnd; x++)
        {
            var cell = new GridPos(x, map.CounterRow);
            if (!map.IsWalkable(cell.X, cell.Y)) continue;
            int d = Math.Abs(cell.X - from.X) + Math.Abs(cell.Y - from.Y);
            if (d < bestDist) { bestDist = d; best = cell; }
        }
        return best;
    }

    protected GridPos? Flee(GridMap map, GridPos from, GridPos playerCell, int maxDist = 6)
    {
        var distFromPlayer = map.DistanceMapFrom(playerCell);
        var distFromSelf = map.DistanceMapFrom(from);

        GridPos? bestTarget = null;
        int bestScore = int.MinValue;

        for (int y = 0; y < map.Height; y++)
        {
            for (int x = 0; x < map.Width; x++)
            {
                if (!map.IsWalkable(x, y)) continue;

                int reachDist = distFromSelf[x, y];
                if (reachDist == int.MaxValue || reachDist > maxDist) continue;

                int safetyDist = distFromPlayer[x, y];
                if (safetyDist == int.MaxValue) continue;

                int score = safetyDist - (reachDist / 2);
                if (score > bestScore)
                {
                    bestScore = score;
                    bestTarget = new GridPos(x, y);
                }
            }
        }

        return bestTarget.HasValue ? BuildPathTo(map, from, bestTarget.Value).FirstOrDefault() : null;
    }

    protected GridPos[] ShuffledDirections()
    {
        var options = new[] { new GridPos(0, -1), new GridPos(0, 1), new GridPos(-1, 0), new GridPos(1, 0) };
        for (int i = options.Length - 1; i > 0; i--)
        {
            int j = Rng.Next(i + 1);
            (options[i], options[j]) = (options[j], options[i]);
        }
        return options;
    }

    protected GridPos? FirstWalkable(GridMap map, GridPos[] candidates)
    {
        foreach (var c in candidates)
            if (map.IsWalkable(c.X, c.Y)) return c;
        return null;
    }

    public virtual void Initialize() { }

    public virtual void Reset() { }

    public abstract GridPos? DecideNextMove(CritterContext context);
    public abstract bool WantsToSteal(CritterContext context);
    public abstract bool WantsTreat(CritterContext context);
    public abstract CatPose GetPose(CritterContext context);
    public abstract AIState GetState(CritterContext context);
}