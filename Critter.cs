using System;
using Microsoft.Xna.Framework;

namespace StoreChase;

/// <summary>
/// A moving animal in the store. It owns grid movement between cells and its own
/// facing, and delegates every decision about *where* it wants to go to an
/// <see cref="ICritterAI"/>.
///
/// Gameplay sets <see cref="Carrying"/> and <see cref="Pose"/>; the AI sets
/// them through its return values (see <see cref="ICritterAI.WantsToSteal"/>).
/// This class deliberately contains no rules about stealing, alarms or winning
/// — it just moves, turns, and tells the animation rig what state it's in.
/// </summary>
public class Critter
{
    /// <summary>The critter's current grid cell.</summary>
    public GridPos Cell { get; private set; }

    /// <summary>World position (X/Z = position, Y = 0 at floor level).</summary>
    public Vector3 WorldPos { get; private set; }

    /// <summary>Facing direction in radians around Y; +Z = 0.</summary>
    public float FacingAngle { get; private set; }

    /// <summary>True while the critter is mid-step between two cells.</summary>
    public bool IsMoving => _isMoving;

    /// <summary>The profile this animal was built with: speed, senses, species.</summary>
    public CritterProfile Profile { get; }

    /// <summary>True once the player has collided with it. The game reads this
    /// to end the encounter and make it drop whatever it was carrying.</summary>
    public bool Tackled { get; private set; }

    /// <summary>Health for boss critters (1 for normal cats). A tackle
    /// decrements this; the critter is only removed at zero.</summary>
    public int Health { get; private set; } = 1;

    /// <summary>Maximum health, which the HUD draws as a boss bar.</summary>
    public int MaxHealth { get; private set; } = 1;

    /// <summary>True for boss critters that take multiple tackles.</summary>
    public bool IsBoss { get; private set; }

    /// <summary>
    /// Promotes this critter to a boss: <paramref name="health"/> tackles to
    /// retire, starting at full. Setting max and current together keeps the
    /// "hurt but alive" state from being reachable as a spawn condition.
    /// </summary>
    public void MakeBoss(int health)
    {
        IsBoss = true;
        MaxHealth = Math.Max(1, health);
        Health = MaxHealth;
    }

    /// <summary>Clears the tackled flag so a boss can be tackled again.</summary>
    public void ResetTackle() => Tackled = false;

    /// <summary>One tackle's worth of damage. True when this was the last.</summary>
    public bool TakeTackleDamage()
    {
        Health = Math.Max(0, Health - 1);
        return Health <= 0;
    }

    /// <summary>Whether this critter is currently holding stolen goods.</summary>
    public bool Carrying { get; set; }

    /// <summary>Whether the critter can currently see the player. Set by the
    /// game (which owns the map and the camera) and read by the AI.</summary>
    public bool CanSeePlayer { get; set; }

    /// <summary>Whether the player's camera is pointed at it.</summary>
    public bool BeingWatched { get; set; }

    /// <summary>
    /// True for the one frame after the critter finishes a step and lands on
    /// a new cell.
    ///
    /// The game reads this to react to *arriving* somewhere. The cell it
    /// landed on is the same cell it is standing on now, but the game has to
    /// know that the arrival just happened rather than re-acting every frame
    /// it stands there.
    /// </summary>
    public bool JustArrived { get; private set; }

    /// <summary>Seconds left of stun. While above zero the cat neither moves
    /// nor makes decisions — it's out of the action but still in the game.
    /// Set by a ball hit; the game handles the drop.</summary>
    public bool Stunned => _stunTimer > 0f;

    /// <summary>
    /// True while the cat is showing a "what was that?!" reaction — flinched
    /// by something that did not stun it (a ball bouncing off a Moggy's
    /// head, a slip). It keeps its freedom of movement; only the rig reacts.
    /// </summary>
    public bool Startled => _startleTimer > 0f;

    /// <summary>
    /// Seconds left of panic (from a spray). A panicking cat flees the player
    /// no matter what it was doing, and won't touch the merchandise.
    /// </summary>
    public float PanicSeconds => _panicTimer;

    /// <summary>
    /// The speed this cat is actually moving at this frame, in cells per
    /// second. A thief carrying goods may sprint (see
    /// <see cref="CritterProfile.CarrySpeedBoost"/>) — that is the getaway
    /// gear, and it is what makes a grab-and-run species feel different from
    /// one that ambles off with a tin.
    /// </summary>
    public float CurrentMoveSpeed =>
        Profile.MoveSpeed * (Carrying ? Profile.CarrySpeedBoost : 1f);

    /// <summary>What the AI wants the body doing; drives the animation rig.</summary>
    public CatPose Pose { get; set; } = CatPose.Stand;

    /// <summary>
    /// What this critter is doing right now, as the AI last reported it.
    ///
    /// Descriptive only — nothing in gameplay branches on it; it exists so
    /// the HUD, debug tools and the harness can answer "why is that cat
    /// standing there" without duplicating the AI's priority order. A stun
    /// overrides whatever the AI says, since a stunned cat decides nothing.
    /// </summary>
    public AIState State => Stunned ? AIState.Stunned : _ai.State;

    /// <summary>This cat's current mood.</summary>
    public Mood Mood { get; set; } = Mood.Calm;

    /// <summary>Seconds left on the current mood before it fades to calm.</summary>
    public float MoodTimer { get; set; }

    /// <summary>This cat's name (for the HUD roster and the feed).</summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// How unsure this critter is about where the player is.
    ///
    /// Zero means it knows exactly — it can see them. A larger number means
    /// a vaguer idea: it heard about the player from another cat, and the
    /// news travelled a distance and degraded on the way.
    ///
    /// It is a *vagueness*, not a distance, so that "seeing the player" and
    /// "hearing about the player" can be compared directly.
    /// </summary>
    public float PlayerAwareness { get; set; }

    /// <summary>
    /// The last place this critter saw the player.
    ///
    /// Kept so a cat that has lost sight of the player can head for where
    /// they were, rather than wandering off.
    /// </summary>
    public GridPos? LastSeenPlayerCell { get; set; }

    /// <summary>
    /// How many treats this cat has been fed.
    ///
    /// Two make a friend (see <see cref="FriendThreshold"/>): a fed cat
    /// stops stealing for the round and shadows the player instead. The
    /// counter keeps climbing past the threshold — extra snacks are just
    /// extra snacks.
    /// </summary>
    public int Friendship { get; private set; }

    /// <summary>Treats needed to befriend a cat.</summary>
    public const int FriendThreshold = 2;

    /// <summary>True once the cat has been fed into friendship.</summary>
    public bool Befriended => Friendship >= FriendThreshold;

    /// <summary>
    /// Feeds the cat one treat. Returns true when this treat is the one
    /// that crossed it into friendship (the moment worth announcing).
    /// </summary>
    public bool Feed()
    {
        Friendship++;
        Mood = Mood.Calm;
        MoodTimer = 0f;
        return Friendship == FriendThreshold;
    }

    private GridPos _from;
    private GridPos _to;
    private float _t;
    private bool _isMoving;
    private float _stunTimer;
    private float _startleTimer;
    private float _panicTimer;

    private readonly ICritterAI _ai;
    private readonly float _cellSize;

    /// <summary>Creates a critter at the given start cell with the given AI and profile.</summary>
    public Critter(GridPos start, float cellSize, ICritterAI ai, CritterProfile profile)
    {
        Cell = start;
        _from = start;
        _to = start;
        _cellSize = cellSize;
        _ai = ai;
        Profile = profile;
        WorldPos = new Vector3(start.X * cellSize, 0f, start.Y * cellSize);
    }

    /// <summary>
    /// Stuns the cat for <paramref name="seconds"/>. It drops whatever it was
    /// carrying (the game handles that) and sits out the stun, then resumes
    /// as normal. A stun never removes the cat from the game.
    /// </summary>
    public void Stun(float seconds) => _stunTimer = MathF.Max(_stunTimer, seconds);

    /// <summary>
    /// Shows a brief flinch reaction for <paramref name="seconds"/> without
    /// taking away the cat's freedom of movement. The rig plays it as a
    /// Startle; the AI is not consulted about it.
    /// </summary>
    public void Startle(float seconds) => _startleTimer = MathF.Max(_startleTimer, seconds);

    /// <summary>
    /// Panics the cat for <paramref name="seconds"/>: while it lasts the AI
    /// flees the player whatever it was doing, and won't touch the
    /// merchandise. This is the spray bottle's effect — crowd control that
    /// does not cost the cat its loot.
    /// </summary>
    public void Panic(float seconds) => _panicTimer = MathF.Max(_panicTimer, seconds);

    /// <summary>
    /// Applies a ball hit, honouring this species' tolerance
    /// (<see cref="CritterProfile.StunDurationFactor"/>).
    ///
    /// A normal cat is stunned for the full <paramref name="seconds"/>. An
    /// immune one (the Moggy) just flinches: the ball still explodes, the
    /// cat still notices, but it walks on. Returning whether the cat was
    /// actually stunned keeps the "did it drop its loot?" decision in one
    /// place — the game, not the critter.
    /// </summary>
    /// <returns>True if the cat was stunned (and so should drop its loot).</returns>
    public bool TryStunFromBall(float seconds)
    {
        float factor = Profile.StunDurationFactor;
        if (factor <= 0f)
        {
            Startle(0.8f);
            return false;
        }

        Stun(seconds * factor);
        return true;
    }

    /// <summary>
    /// Advances the critter one frame. <paramref name="locus"/> supplies the
    /// world facts the AI needs but that this class doesn't own — the player,
    /// the ball, and the loot.
    /// </summary>
    public void Update(GameTime gameTime, CritterLocus locus)
    {
        if (Tackled) return;

        // The arrival flag is per-frame, so it has to be cleared before
        // anything else can read it.
        JustArrived = false;

        float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;

        // Reactions age whether or not the cat is otherwise busy.
        if (_startleTimer > 0f) _startleTimer -= dt;
        if (_panicTimer > 0f) _panicTimer -= dt;
        if (MoodTimer > 0f) { MoodTimer -= dt; if (MoodTimer <= 0f) Mood = Mood.Calm; }

        // Stunned: tick down and sit out the frame. No movement, no decisions,
        // no stealing — but the cat is still in the game and still drawn.
        if (_stunTimer > 0f)
        {
            _stunTimer -= dt;
            return;
        }

        // A collision counts whether the player moved onto the critter or the
        // critter moved onto the player. Checking only on arrival (below) means
        // a critter that's standing still can never be caught, because the next
        // thing it does is decide to walk away. A friend is not a thief:
        // befriend one and you can stand on it all day.
        if (Cell == locus.PlayerCell && !Befriended) { Tackled = true; return; }

        // 1. Decision. The AI is only consulted when standing still, so it
        // plans at cell granularity exactly like before.
        if (!_isMoving)
        {
            Cell = _to;
            var context = BuildContext(locus, dt);
            GridPos? next = _ai.DecideNextMove(context);

            // The AI decided it wanted the item underfoot; the game owns the
            // transfer, since possession rules aren't the AI's business.
            if (_ai.WantsToSteal && !Carrying && locus.TrySteal(this))
            {
                Carrying = true;
            }

            // Same split for treats: the AI says "eating now", the game
            // turns the snack into friendship and consumes the treat.
            if (_ai.WantsTreat)
                locus.TryEatTreat?.Invoke(this);

            Pose = _ai.Pose;

            if (next.HasValue && next.Value != Cell)
            {
                // The AI's choice is trusted but not obeyed blindly: a planned
                // path can go stale (a shelf-adjacent plan built before the
                // ball diverted it, say) and walking into a shelf would look
                // like the cat is clipping through the world. Rejecting an
                // illegal step just means the cat holds its ground this turn.
                if (locus.Map.IsWalkable(next.Value.X, next.Value.Y))
                {
                    _from = Cell;
                    _to = next.Value;
                    _t = 0f;
                    _isMoving = true;
                }
            }
        }

        // 2. Movement. Interpolate cell to cell; the visual position is a lerp
        // between the two cell centres so the critter glides rather than
        // snapping.
        if (_isMoving)
        {
            _t += dt * CurrentMoveSpeed;
            if (_t >= 1f)
            {
                _t = 1f;
                _isMoving = false;
                Cell = _to;
                JustArrived = true;

                // Landing on the player counts as a tackle — except for a
                // friend, who is walking with you, not being caught.
                if (Cell == locus.PlayerCell && !Befriended) Tackled = true;
            }

            Vector3 fromPos = new(_from.X * _cellSize, 0f, _from.Y * _cellSize);
            Vector3 toPos = new(_to.X * _cellSize, 0f, _to.Y * _cellSize);
            WorldPos = Vector3.Lerp(fromPos, toPos, _t);

            Vector3 delta = toPos - fromPos;
            if (delta.LengthSquared() > 0.0001f)
                FacingAngle = MathF.Atan2(delta.X, delta.Z);
        }
    }

    private CritterContext BuildContext(CritterLocus locus, float dt) => new(
        CurrentCell: Cell,
        PlayerCell: locus.PlayerCell,
        Map: locus.Map,
        DeltaTime: dt,
        CanSeePlayer: CanSeePlayer,
        BeingWatched: BeingWatched,
        Ball: locus.Ball,
        LootUnderfoot: locus.LootUnderfoot(this),
        LootTarget: locus.LootTarget,
        LootTargetItem: locus.LootTargetItem,
        Carrying: Carrying,
        StolenCount: locus.StolenCount,
        AlarmStage: locus.AlarmStage,
        PlayerAwareness: PlayerAwareness,
        LastSeenPlayerCell: LastSeenPlayerCell,
        Treat: locus.Treat,
        Panic: _panicTimer,
        Befriended: Befriended,
        Mood: Mood);

    /// <summary>Marks the critter as tackled. The game handles the drop and removal.</summary>
    public void Tackle() => Tackled = true;
}

/// <summary>
/// The world facts a critter needs from the game each frame. Passed as one
/// object so <c>Critter.Update</c> keeps a stable signature as the AI grows
/// more inputs, and so the AI never reaches into Game1 directly.
/// </summary>
public readonly record struct CritterLocus(
    GridMap Map,
    GridPos PlayerCell,
    GridPos? Ball,
    GridPos? LootTarget,
    LootItem? LootTargetItem,
    int StolenCount,
    Func<Critter, LootItem?> LootUnderfoot,
    Func<Critter, bool> TrySteal,
    AlarmStage AlarmStage = AlarmStage.Calm,

    /// <summary>Cell of a dropped treat, if one exists. Cats path to it like
    /// a ball, but several at once — the lure is the point.</summary>
    GridPos? Treat = null,

    /// <summary>A cat standing on the treat wants to eat it; the game turns
    /// the snack into friendship and consumes the treat. Null when the world
    /// has no treat rules (the headless round simulation).</summary>
    Func<Critter, bool>? TryEatTreat = null
);
