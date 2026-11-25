// Scripts/AI/NervousWatcher.csx
// A nervous cat that freezes when the player looks at it (first-person mechanic).
// Steals only when player isn't watching.

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using StoreChase;
using StoreChase.Scripting;

public class NervousWatcherAI : ScriptedAIBase
{
    private List<GridPos> _path = new();
    private float _replanTimer;
    private float _freezeTimer;
    private GridPos? _lastSeenPlayer;

    public override void Initialize()
    {
        ScriptServices.Log($"NervousWatcherAI initialized for {Profile.Name}");
    }

    public override void Reset()
    {
        _path.Clear();
        _replanTimer = 0f;
        _freezeTimer = 0f;
        _lastSeenPlayer = null;
    }

    public override GridPos? DecideNextMove(CritterContext context)
    {
        _replanTimer -= context.DeltaTime;
        _freezeTimer = MathF.Max(0f, _freezeTimer - context.DeltaTime);

        int dist = ManhattanDistance(context.CurrentCell, context.PlayerCell);
        bool canSee = context.CanSeePlayer;
        bool watched = context.BeingWatched;

        // Update last seen
        if (canSee) _lastSeenPlayer = context.PlayerCell;

        // PRIORITY 1: Panic
        if (context.Panic > 0f)
        {
            _path.Clear();
            _freezeTimer = 0f;
            return Flee(context.Map, context.CurrentCell, context.PlayerCell);
        }

        // PRIORITY 2: Frozen by being watched (the signature behavior)
        if (watched && canSee && dist <= Profile.AlertRadius && Profile.FreezesWhenWatched)
        {
            _freezeTimer = 0.8f;
            _path.Clear();
            return null; // Frozen in place
        }

        // PRIORITY 3: Carrying - escape
        if (context.Carrying)
        {
            bool threatened = canSee && dist <= (Profile.FleeRadius + (context.Mood == Mood.Wary ? 2 : 0));
            if (threatened)
            {
                _path.Clear();
                return Flee(context.Map, context.CurrentCell, context.PlayerCell);
            }
            return HeadForCounter(context);
        }

        // PRIORITY 4: Loot underfoot - but ONLY if not watched
        if (context.LootUnderfoot != null && !watched && ShouldTakeLoot(context, watched))
        {
            return null;
        }

        // PRIORITY 5: Ball
        if (context.Ball.HasValue && !Profile.IgnoresBall && !watched)
        {
            _path.Clear();
            return PathStepToward(context.Map, context.CurrentCell, context.Ball.Value);
        }

        // PRIORITY 6: Treat
        if (context.Treat.HasValue && !context.Carrying && !Profile.ChasesPlayer &&
            ManhattanDistance(context.CurrentCell, context.Treat.Value) <= SmartAnimalAI.TreatRadius)
        {
            if (context.CurrentCell == context.Treat.Value) return null;
            _path.Clear();
            return PathStepToward(context.Map, context.CurrentCell, context.Treat.Value);
        }

        // PRIORITY 7: Investigate last known player position
        if (_lastSeenPlayer.HasValue && context.PlayerAwareness > 0f && dist > Profile.AlertRadius)
        {
            if (_replanTimer <= 0f || _path.Count == 0)
            {
                _replanTimer = Profile.ReplanInterval;
                _path = BuildPathTo(context.Map, context.CurrentCell, _lastSeenPlayer.Value);
            }
            if (_path.Count > 0)
            {
                var next = _path[0];
                _path.RemoveAt(0);
                return next;
            }
        }

        // PRIORITY 8: Hunt for preferred loot when not watched
        if (context.LootTargetItem is { } item && context.StolenCount < 3 && !watched)
        {
            float drive = Profile.ThiefDriveFor(item.Kind);
            if (context.AlarmStage == AlarmStage.Critical)
                drive = MathF.Max(drive, Profile.ThiefDrive * 0.75f);

            if (Rng.NextDouble() < drive)
            {
                _path.Clear();
                return PathStepToward(context.Map, context.CurrentCell, item.Cell);
            }
        }

        // PRIORITY 9: Nervous patrol - stay away from player
        return NervousPatrol(context);
    }

    private GridPos? HeadForCounter(CritterContext context)
    {
        var counter = NearestCounterCell(context.CurrentCell, context.Map);
        if (!counter.HasValue) return NervousPatrol(context);
        if (counter.Value == context.CurrentCell) return null;

        if (_replanTimer <= 0f || _path.Count == 0)
        {
            _replanTimer = Profile.ReplanInterval;
            _path = BuildPathTo(context.Map, context.CurrentCell, counter.Value);
        }

        if (_path.Count > 0)
        {
            var next = _path[0];
            _path.RemoveAt(0);
            return next;
        }
        return null;
    }

    private GridPos? NervousPatrol(CritterContext context)
    {
        // Patrol but maintain distance from player
        var candidates = new List<GridPos>();
        foreach (var dir in ShuffledDirections())
        {
            var candidate = new GridPos(context.CurrentCell.X + dir.X, context.CurrentCell.Y + dir.Y);
            if (!context.Map.IsWalkable(candidate.X, candidate.Y)) continue;

            int newDist = ManhattanDistance(candidate, context.PlayerCell);
            if (newDist >= Profile.AlertRadius) // Prefer staying at alert radius or beyond
                candidates.Add(candidate);
        }

        if (candidates.Count > 0)
            return candidates[Rng.Next(candidates.Count)];

        // Fallback: any walkable that increases distance
        foreach (var dir in ShuffledDirections())
        {
            var candidate = new GridPos(context.CurrentCell.X + dir.X, context.CurrentCell.Y + dir.Y);
            if (context.Map.IsWalkable(candidate.X, candidate.Y))
            {
                int newDist = ManhattanDistance(candidate, context.PlayerCell);
                if (newDist >= ManhattanDistance(context.CurrentCell, context.PlayerCell))
                    return candidate;
            }
        }
        return null;
    }

    private bool ShouldTakeLoot(CritterContext context, bool watched)
    {
        if (Profile.ThiefDrive <= 0f) return false;
        if (context.LootUnderfoot is not { } underfoot) return false;
        if (context.Befriended) return false;
        if (context.Mood == Mood.Wary) return false;
        if (context.Panic > 0f) return false;
        if (watched && Profile.Sneaky) return false; // Won't steal if watched

        float drive = Profile.ThiefDriveFor(underfoot.Kind);
        if (context.AlarmStage == AlarmStage.Critical)
            drive = MathF.Max(drive, Profile.ThiefDrive * 0.75f);
        if (drive <= 0f) return false;

        // Extra hesitation near player
        if (context.CanSeePlayer && ManhattanDistance(context.CurrentCell, context.PlayerCell) <= 2)
            return Rng.NextDouble() < 0.2f;

        return Rng.NextDouble() < drive;
    }

    public override bool WantsToSteal(CritterContext context)
    {
        bool watched = context.BeingWatched;
        return context.LootUnderfoot != null && ShouldTakeLoot(context, watched);
    }

    public override bool WantsTreat(CritterContext context)
    {
        return context.Treat.HasValue && context.CurrentCell == context.Treat.Value
            && !context.Carrying && !Profile.ChasesPlayer;
    }

    public override CatPose GetPose(CritterContext context)
    {
        if (context.Panic > 0f) return CatPose.Startle;
        if (_freezeTimer > 0f || (context.BeingWatched && context.CanSeePlayer && Profile.FreezesWhenWatched))
            return CatPose.Stand; // Frozen, staring
        if (context.Carrying) return CatPose.Walk;
        if (context.CanSeePlayer && ManhattanDistance(context.CurrentCell, context.PlayerCell) <= Profile.AlertRadius)
            return CatPose.Stand; // Alert, watching
        return Profile.IdlePose;
    }

    public override AIState GetState(CritterContext context)
    {
        if (context.Panic > 0f) return AIState.Fleeing;
        if (context.BeingWatched && context.CanSeePlayer && Profile.FreezesWhenWatched)
            return AIState.Idle; // Frozen
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

new NervousWatcherAI()