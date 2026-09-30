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
    public GridPos Cell { get; private set; }
    public Vector3 WorldPos { get; private set; }
    public float FacingAngle { get; private set; }
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

    /// <summary>What the AI wants the body doing; drives the animation rig.</summary>
    public CatPose Pose { get; set; } = CatPose.Stand;

    private GridPos _from;
    private GridPos _to;
    private float _t;
    private bool _isMoving;

    private readonly ICritterAI _ai;
    private readonly float _cellSize;

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
    /// Advances the critter one frame. <paramref name="locus"/> supplies the
    /// world facts the AI needs but that this class doesn't own — the player,
    /// the ball, and the loot.
    /// </summary>
    public void Update(GameTime gameTime, CritterLocus locus)
    {
        if (Tackled) return;

        float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;

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
        Carrying: Carrying,
        StolenCount: locus.StolenCount);

    /// <summary>Marks the critter as tackled. The game handles the drop.</summary>
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
    int StolenCount,
    Func<Critter, LootItem?> LootUnderfoot,
    Func<Critter, bool> TrySteal
);
