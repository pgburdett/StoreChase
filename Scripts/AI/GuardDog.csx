// Scripts/AI/GuardDog.csx
// A guard dog that chases the player instead of stealing.
// Distracted by balls, ignores treats.

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using StoreChase;
using StoreChase.Scripting;

public class GuardDogAI : ScriptedAIBase
{
    private List<GridPos> _path = new();
    private float _replanTimer;
    private float _barkTimer;

    public override void Initialize()
    {
        ScriptServices.Log($"GuardDogAI initialized for {Profile.Name}");
    }

    public override void Reset()
    {
        _path.Clear();
        _replanTimer = 0f;
        _barkTimer = 0f;
    }

    public override GridPos? DecideNextMove(CritterContext context)
    {
        _replanTimer -= context.DeltaTime;
        _barkTimer = MathF.Max(0f, _barkTimer - context.DeltaTime);

        // Guard dogs never steal, never eat treats
        // They CHASE the player

        // PRIORITY 1: Panic (sprayed) - even guard dogs flee spray
        if (context.Panic > 0f)
        {
            _path.Clear();
            return Flee(context.Map, context.CurrentCell, context.PlayerCell);
        }

        // PRIORITY 2: Ball distraction (the only thing that diverts them)
        if (context.Ball.HasValue && !Profile.IgnoresBall)
        {
            _path.Clear();
            return PathStepToward(context.Map, context.CurrentCell, context.Ball.Value);
        }

        // PRIORITY 3: Chase player
        if (_replanTimer <= 0f || _path.Count == 0)
        {
            _replanTimer = Profile.ReplanInterval * 0.5f; // Replan more often
            _path = BuildPathTo(context.Map, context.CurrentCell, context.PlayerCell);
        }

        if (_path.Count > 0)
        {
            var next = _path[0];
            _path.RemoveAt(0);
            
            // Bark occasionally when chasing
            if (_barkTimer <= 0f && Rng.NextDouble() < 0.1)
            {
                _barkTimer = 2f;
                ScriptServices.Log($"{Profile.Name} barks at player!");
                ScriptServices.Publish(new ItemStolenEvent($"BARK: {Profile.Name}")); // Reuse event for logging
            }
            
            return next;
        }

        return null;
    }

    public override bool WantsToSteal(CritterContext context) => false; // Never steals

    public override bool WantsTreat(CritterContext context) => false; // Ignores treats

    public override CatPose GetPose(CritterContext context)
    {
        if (context.Panic > 0f) return CatPose.Startle;
        return CatPose.Walk; // Always moving purposefully
    }

    public override AIState GetState(CritterContext context)
    {
        if (context.Panic > 0f) return AIState.Fleeing;
        if (context.Ball.HasValue) return AIState.Investigating;
        return AIState.Chasing; // Always chasing
    }
}

new GuardDogAI()