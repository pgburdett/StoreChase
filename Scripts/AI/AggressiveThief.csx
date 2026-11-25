// Scripts/AI/AggressiveThief.csx
// An aggressive cat that prioritizes stealing and escaping.
// Drop this in Scripts/AI/ and reference it from Game1.

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using StoreChase;
using StoreChase.Scripting;

// The script must define a class implementing IScriptedAIProvider
// and return an instance at the end.

public class AggressiveThiefAI : ScriptedAIBase
{
    private List<GridPos> _escapePath = new();
    private float _replanTimer;
    private GridPos? _lastCell;
    private GridPos? _targetItemCell;
    private float _stuckTimer;

    public override void Initialize()
    {
        ScriptServices.Log($"AggressiveThiefAI initialized for {Profile.Name}");
    }

    public override void Reset()
    {
        _escapePath.Clear();
        _replanTimer = 0f;
        _lastCell = null;
        _targetItemCell = null;
        _stuckTimer = 0f;
    }

    public override GridPos? DecideNextMove(CritterContext context)
    {
        _replanTimer -= context.DeltaTime;
        _stuckTimer -= context.DeltaTime;

        int distToPlayer = ManhattanDistance(context.CurrentCell, context.PlayerCell);
        bool threatened = context.CanSeePlayer && distToPlayer <= (Profile.FleeRadius + (context.Mood == Mood.Wary ? 2 : 0));

        // PRIORITY 1: Panic (sprayed) - run away
        if (context.Panic > 0f)
        {
            _escapePath.Clear();
            return Flee(context.Map, context.CurrentCell, context.PlayerCell);
        }

        // PRIORITY 2: Carrying loot - escape to counter
        if (context.Carrying)
        {
            if (threatened)
            {
                _escapePath.Clear();
                return Flee(context.Map, context.CurrentCell, context.PlayerCell);
            }
            return HeadForCounter(context);
        }

        // PRIORITY 3: Loot underfoot - steal it if we like it
        if (context.LootUnderfoot != null && ShouldTakeLoot(context))
        {
            return null; // Stay and steal
        }

        // PRIORITY 4: Ball distraction
        if (context.Ball.HasValue && !Profile.IgnoresBall)
        {
            _escapePath.Clear();
            return PathStepToward(context.Map, context.CurrentCell, context.Ball.Value);
        }

        // PRIORITY 5: Treat
        if (context.Treat.HasValue && ManhattanDistance(context.CurrentCell, context.Treat.Value) <= SmartAnimalAI.TreatRadius)
        {
            if (context.CurrentCell == context.Treat.Value) return null; // Eat it
            _escapePath.Clear();
            return PathStepToward(context.Map, context.CurrentCell, context.Treat.Value);
        }

        // PRIORITY 6: Hunt for loot aggressively
        if (context.LootTargetItem is { } item && context.StolenCount < 5) // Higher threshold
        {
            float drive = Profile.ThiefDriveFor(item.Kind);
            if (context.AlarmStage == AlarmStage.Critical) drive = MathF.Max(drive, Profile.ThiefDrive * 0.9f);
            
            if (Rng.NextDouble() < drive * 1.5f) // More aggressive
            {
                _targetItemCell = item.Cell;
                _escapePath.Clear();
                return PathStepToward(context.Map, context.CurrentCell, item.Cell);
            }
        }

        // PRIORITY 7: Investigate player if seen recently
        if (context.LastSeenPlayerCell.HasValue && context.PlayerAwareness > 0f)
        {
            if (distToPlayer > Profile.AlertRadius)
            {
                _escapePath.Clear();
                return PathStepToward(context.Map, context.CurrentCell, context.LastSeenPlayerCell.Value);
            }
        }

        // PRIORITY 8: Flee if threatened
        if (threatened)
        {
            _stuckTimer = 0.5f;
            return Flee(context.Map, context.CurrentCell, context.PlayerCell);
        }

        // PRIORITY 9: Patrol shelf edges (where loot spawns)
        return PatrolShelfEdges(context);
    }

    private GridPos? HeadForCounter(CritterContext context)
    {
        var counter = NearestCounterCell(context.CurrentCell, context.Map);
        if (!counter.HasValue) return PatrolShelfEdges(context);
        if (counter.Value == context.CurrentCell) return null;

        if (_replanTimer <= 0f || _escapePath.Count == 0)
        {
            _replanTimer = Profile.ReplanInterval;
            _escapePath = BuildPathTo(context.Map, context.CurrentCell, counter.Value);
        }

        if (_escapePath.Count > 0)
        {
            var next = _escapePath[0];
            _escapePath.RemoveAt(0);
            return next;
        }
        return null;
    }

    private GridPos? PatrolShelfEdges(CritterContext context)
    {
        // Find nearby shelf edges to patrol
        var candidates = new List<GridPos>();
        foreach (var dir in ShuffledDirections())
        {
            var candidate = new GridPos(context.CurrentCell.X + dir.X, context.CurrentCell.Y + dir.Y);
            if (!context.Map.IsWalkable(candidate.X, candidate.Y)) continue;

            // Check if this cell borders a shelf (blocked cell)
            bool bordersShelf = false;
            foreach (var d2 in new[] { new GridPos(0, -1), new GridPos(0, 1), new GridPos(-1, 0), new GridPos(1, 0) })
            {
                if (!context.Map.IsWalkable(candidate.X + d2.X, candidate.Y + d2.Y))
                {
                    bordersShelf = true;
                    break;
                }
            }
            if (bordersShelf) candidates.Add(candidate);
        }

        if (candidates.Count > 0)
            return candidates[Rng.Next(candidates.Count)];

        // Fallback: random walk
        foreach (var dir in ShuffledDirections())
        {
            var candidate = new GridPos(context.CurrentCell.X + dir.X, context.CurrentCell.Y + dir.Y);
            if (context.Map.IsWalkable(candidate.X, candidate.Y))
                return candidate;
        }
        return null;
    }

    private bool ShouldTakeLoot(CritterContext context)
    {
        if (Profile.ThiefDrive <= 0f) return false;
        if (context.LootUnderfoot is not { } underfoot) return false;
        if (context.Befriended) return false;
        if (context.Mood == Mood.Wary) return false;
        if (context.Panic > 0f) return false;
        if (Profile.Sneaky && context.BeingWatched) return false;

        float drive = Profile.ThiefDriveFor(underfoot.Kind);
        if (context.AlarmStage == AlarmStage.Critical)
            drive = MathF.Max(drive, Profile.ThiefDrive * 0.9f);
        if (drive <= 0f) return false;

        // Aggressive: less hesitation near player
        if (context.CanSeePlayer && ManhattanDistance(context.CurrentCell, context.PlayerCell) <= 2)
            return Rng.NextDouble() < 0.5f; // 50% chance even when watched

        return Rng.NextDouble() < drive;
    }

    public override bool WantsToSteal(CritterContext context)
    {
        return context.LootUnderfoot != null && ShouldTakeLoot(context);
    }

    public override bool WantsTreat(CritterContext context)
    {
        return context.Treat.HasValue && context.CurrentCell == context.Treat.Value 
            && !context.Carrying && !Profile.ChasesPlayer;
    }

    public override CatPose GetPose(CritterContext context)
    {
        int dist = ManhattanDistance(context.CurrentCell, context.PlayerCell);
        // Determine if moving by checking if there's a valid next step
        var nextStep = DecideNextMove(context);
        bool moving = nextStep.HasValue && nextStep.Value != context.CurrentCell;
        
        if (context.Panic > 0f) return CatPose.Startle;
        if (context.Carrying) return CatPose.Walk; // Running with loot
        if (context.CanSeePlayer && dist <= Profile.AlertRadius) return CatPose.Stand; // Alert
        return Profile.IdlePose;
    }

    public override AIState GetState(CritterContext context)
    {
        if (context.Panic > 0f) return AIState.Fleeing;
        if (context.Carrying)
        {
            int dist = ManhattanDistance(context.CurrentCell, context.PlayerCell);
            bool threatened = context.CanSeePlayer && dist <= (Profile.FleeRadius + (context.Mood == Mood.Wary ? 2 : 0));
            return threatened ? AIState.Fleeing : AIState.Escaping;
        }
        if (context.CanSeePlayer && ManhattanDistance(context.CurrentCell, context.PlayerCell) <= Profile.AlertRadius)
            return AIState.Investigating;
        if (context.LootTargetItem != null) return AIState.Chasing;
        return AIState.Patrolling;
    }
}

// Return instance for ScriptHost
new AggressiveThiefAI()