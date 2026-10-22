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

    /// <summary>What the AI wants the body doing; drives the animation rig.</summary>
    public CatPose Pose { get; set; } = CatPose.Stand;

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

    private GridPos _from;
    private GridPos _to;
    private float _t;
    private bool _isMoving;
    private float _stunTimer;

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
        // thing it does is decide to walk away.
        if (Cell == locus.PlayerCell) { Tackled = true; return; }

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
            _t += dt * Profile.MoveSpeed;
            if (_t >= 1f)
            {
                _t = 1f;
                _isMoving = false;
                Cell = _to;
                JustArrived = true;

                // Landing on the player counts as a tackle.
                if (Cell == locus.PlayerCell) Tackled = true;
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
        LastSeenPlayerCell: LastSeenPlayerCell);

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
    AlarmStage AlarmStage = AlarmStage.Calm
);
