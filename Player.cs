using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

namespace StoreChase
{
    // Same grid-locked movement as the 2D version. Only the coordinate
    // mapping changes: X/Y grid -> X/Z world (Y is "up" in 3D).
    public class Player
    {
        public GridPos Cell { get; private set; }
        public Vector3 WorldPos { get; private set; } // X/Z = position, Y = 0 (floor level)

        private GridPos _from;
        private GridPos _to;
        private float _t;
        private const float MoveSpeed = 4.5f;

        private Point _heldDirection = Point.Zero;

        public Player(GridPos start, float cellSize)
        {
            Cell = start;
            _from = start;
            _to = start;
            WorldPos = new Vector3(start.X * cellSize, 0f, start.Y * cellSize);
        }

        public void Update(GameTime gameTime, GridMap map, float cellSize)
        {
            ReadInput();

            bool moving = _t < 1f;

            if (!moving)
            {
                Cell = _to;
                var candidate = new GridPos(Cell.X + _heldDirection.X, Cell.Y + _heldDirection.Y);
                if ((_heldDirection.X != 0 || _heldDirection.Y != 0) && map.IsWalkable(candidate.X, candidate.Y))
                {
                    _from = Cell;
                    _to = candidate;
                    _t = 0f;
                    moving = true;
                }
            }

            if (moving)
            {
                float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;
                _t += dt * MoveSpeed;
                if (_t > 1f) _t = 1f;

                Vector3 fromPos = new(_from.X * cellSize, 0f, _from.Y * cellSize);
                Vector3 toPos = new(_to.X * cellSize, 0f, _to.Y * cellSize);
                WorldPos = Vector3.Lerp(fromPos, toPos, _t);

                if (_t >= 1f) Cell = _to;
            }
        }

        private void ReadInput()
        {
            var kb = Keyboard.GetState();
            if (kb.IsKeyDown(Keys.Up) || kb.IsKeyDown(Keys.W)) _heldDirection = new Point(0, -1);
            else if (kb.IsKeyDown(Keys.Down) || kb.IsKeyDown(Keys.S)) _heldDirection = new Point(0, 1);
            else if (kb.IsKeyDown(Keys.Left) || kb.IsKeyDown(Keys.A)) _heldDirection = new Point(-1, 0);
            else if (kb.IsKeyDown(Keys.Right) || kb.IsKeyDown(Keys.D)) _heldDirection = new Point(1, 0);
        }
    }
}