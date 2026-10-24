namespace StoreChase
{
    /// <summary>
    /// What a critter's AI has decided the body should be doing.
    ///
    /// Purely descriptive — it reports a decision for debugging, the HUD and
    /// the harness; nothing in gameplay branches on it (the AI's outputs
    /// remain the next cell, WantsToSteal, WantsTreat and Pose).
    /// </summary>
    public enum AIState
    {
        /// <summary>Standing still: taking loot underfoot, frozen while watched, or eating.</summary>
        Idle,

        /// <summary>No pressing business: walking a patrol route, following the player, or wandering.</summary>
        Patrolling,

        /// <summary>Heading for a loose item it intends to take.</summary>
        Chasing,

        /// <summary>Carrying loot and making for the counter.</summary>
        Escaping,

        /// <summary>Running from the player — panicked, or threatened while carrying.</summary>
        Fleeing,

        /// <summary>Something caught its attention: a ball, a treat, an alert, a last-seen cell.</summary>
        Investigating,

        /// <summary>Stunned by a ball hit; sitting out the stun.</summary>
        Stunned,
    }
}
