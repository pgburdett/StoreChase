using System;
using Microsoft.Xna.Framework;

namespace StoreChase
{
    // The player moves two ways: grid-locked cell-to-cell in the isometric
    // view, or free world-space in first-person. Game1 resolves the input to a
    // direction (no device APIs here) and picks the mode.
    /// <summary>
    /// The player character. Exposes <see cref="WorldPos"/> (X/Z, Y=0) and
    /// <see cref="Cell"/> for the game's rendering and grid-based AI. Has no
    /// <c>Draw</c> method — Game1 owns all rendering.
    /// </summary>
    public class Player
    {
        /// <summary>The player's current grid cell.</summary>
        public GridPos Cell { get; private set; }

        /// <summary>World position (X/Z = position, Y = 0 at floor level).</summary>
        public Vector3 WorldPos { get; private set; }

        /// <summary>Facing direction in radians around Y; +Z = 0 (matches Critter).</summary>
        public float FacingAngle { get; private set; }

        private GridPos _from;
        private GridPos _to;
        private float _t;
        private const float MoveSpeed = 4.5f;   // grid steps per second
        private const float FreeMoveSpeed = 6f; // world units per second (first-person)
        private const float FreeRadius = 0.35f; // collision radius for free movement (world units)

        /// <summary>
        /// The player's grid speed, in cells per second.
        ///
        /// Public because the store's alarm rates are scaled by how long a
        /// round takes to clear, and that calculation needs the speed.
        /// </summary>
        public const float GridSpeed = MoveSpeed;

        private readonly float _cellSize;

        private Point _heldDirection = Point.Zero;
        private bool _wasFreeMove;

        /// <summary>True while the player is mid-step between two cells.</summary>
        public bool IsMoving => _t < 1f;

        /// <summary>Creates a player at the given start cell.</summary>
        public Player(GridPos start, float cellSize)
        {
            _cellSize = cellSize;
            Cell = start;
            _from = start;
            _to = start;
            _t = 1f; // at rest, so the first frame isn't treated as "moving"
            WorldPos = new Vector3(start.X * cellSize, 0f, start.Y * cellSize);
        }

        /// <summary>
        /// Advances the player one frame.
        /// </summary>
        /// <param name="gameTime">Current game time (for delta time).</param>
        /// <param name="map">The grid map (for walkability checks).</param>
        /// <param name="moveDir">
        /// A grid step (e.g. (0,-1)) in isometric, or a normalized world XZ
        /// heading (camera-relative, X/Z in X/Y) in first-person.
        /// </param>
        /// <param name="freeMove">Selects which interpretation of <paramref name="moveDir"/> applies.</param>
        public void Update(GameTime gameTime, GridMap map, Vector2 moveDir, bool freeMove)
        {
            if (freeMove != _wasFreeMove)
            {
                // Returning to the grid: re-anchor to the nearest cell so the
                // first grid step starts from a cell center instead of jumping.
                if (!freeMove)
                {
                    _from = Cell;
                    _to = Cell;
                    _t = 1f;
                    WorldPos = new Vector3(Cell.X * _cellSize, 0f, Cell.Y * _cellSize);
                }

                _wasFreeMove = freeMove;
            }

            if (freeMove)
                UpdateFree(gameTime, map, moveDir);
            else
                UpdateGrid(gameTime, map, moveDir);
        }

        private void UpdateGrid(GameTime gameTime, GridMap map, Vector2 move)
        {
            // The direction being asked for right now. Releasing the keys
            // stops the player.
            //
            // This used to only update the held direction when the device
            // reported one, "so releasing mid-tile keeps the existing
            // grid-movement feel". But the held direction was then reused to
            // start the *next* tile as well, so a player who let go of the
            // keys kept walking in that direction until they ran into a
            // shelf — there was no way to stand still in the isometric view.
            // The held direction is now only used to finish the tile already
            // in progress.
            bool hasInput = move != Vector2.Zero;
            if (hasInput)
                _heldDirection = new Point((int)move.X, (int)move.Y);

            bool moving = _t < 1f;

            if (!moving)
            {
                Cell = _to;

                // Only start a new tile while the player is actually asking
                // to move. A tile already under way is always finished.
                if (hasInput)
                {
                    var candidate = new GridPos(Cell.X + _heldDirection.X, Cell.Y + _heldDirection.Y);
                    if (map.IsWalkable(candidate.X, candidate.Y))
                    {
                        _from = Cell;
                        _to = candidate;
                        _t = 0f;
                        moving = true;
                    }
                }
            }

            if (moving)
            {
                float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;
                _t += dt * MoveSpeed;
                if (_t > 1f) _t = 1f;

                Vector3 fromPos = new(_from.X * _cellSize, 0f, _from.Y * _cellSize);
                Vector3 toPos = new(_to.X * _cellSize, 0f, _to.Y * _cellSize);
                WorldPos = Vector3.Lerp(fromPos, toPos, _t);

                Vector3 delta = toPos - fromPos;
                if (delta.LengthSquared() > 0.0001f)
                    FacingAngle = (float)Math.Atan2(delta.X, delta.Z);

                if (_t >= 1f) Cell = _to;
            }
        }

        // Continuous movement along a world heading, with per-axis wall
        // collision so the player slides along shelves instead of sticking.
        private void UpdateFree(GameTime gameTime, GridMap map, Vector2 dir)
        {
            float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;
            if (dir.LengthSquared() > 1f) dir.Normalize();

            Vector2 pos = new(WorldPos.X, WorldPos.Z);
            Vector2 delta = dir * FreeMoveSpeed * dt;

            Vector2 tryX = new(pos.X + delta.X, pos.Y);
            if (CanStandAt(map, tryX)) pos = tryX;

            Vector2 tryZ = new(pos.X, pos.Y + delta.Y);
            if (CanStandAt(map, tryZ)) pos = tryZ;

            WorldPos = new Vector3(pos.X, 0f, pos.Y);

            // The critter AI and the catch check are still grid-based, so keep
            // Cell pointed at the nearest walkable tile.
            Cell = NearestWalkableCell(map, pos);

            if (dir.LengthSquared() > 0.0001f)
                FacingAngle = (float)Math.Atan2(dir.X, dir.Y);

            _heldDirection = Point.Zero;
        }

        // Treats the player as a circle of FreeRadius and rejects a position
        // whose bounding box overlaps any shelf cell. Cell centers sit at
        // multiples of cellSize, so a world coordinate maps to the nearest
        // cell by rounding.
        private bool CanStandAt(GridMap map, Vector2 pos)
        {
            int x0 = CellIndex(pos.X - FreeRadius);
            int x1 = CellIndex(pos.X + FreeRadius);
            int y0 = CellIndex(pos.Y - FreeRadius);
            int y1 = CellIndex(pos.Y + FreeRadius);

            for (int x = x0; x <= x1; x++)
                for (int y = y0; y <= y1; y++)
                    if (!map.IsWalkable(x, y)) return false;

            return true;
        }

        private int CellIndex(float world) => (int)MathF.Floor(world / _cellSize + 0.5f);

        private GridPos NearestWalkableCell(GridMap map, Vector2 pos)
        {
            int cx = Math.Clamp(CellIndex(pos.X), 0, map.Width - 1);
            int cy = Math.Clamp(CellIndex(pos.Y), 0, map.Height - 1);

            if (map.IsWalkable(cx, cy)) return new GridPos(cx, cy);

            // Rare fallback: the rounded cell is a shelf corner, so search
            // outward for the closest walkable tile.
            for (int r = 1; r <= 3; r++)
                for (int dx = -r; dx <= r; dx++)
                    for (int dy = -r; dy <= r; dy++)
                    {
                        if (Math.Abs(dx) != r && Math.Abs(dy) != r) continue;
                        int x = cx + dx, y = cy + dy;
                        if (x >= 0 && y >= 0 && x < map.Width && y < map.Height && map.IsWalkable(x, y))
                            return new GridPos(x, y);
                    }

            return new GridPos(cx, cy);
        }
    }
}
