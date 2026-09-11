using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace StoreChase
{
    // Pac-Man style movement: moves one full tile at a time, smoothly
    // interpolated, and only accepts a new direction once it lands on a tile.
    public class Player
    {
        public GridPos Cell { get; private set; }
        public Vector2 PixelPos { get; private set; }

        private GridPos _from;
        private GridPos _to;
        private float _t; // 0..1 progress between _from and _to
        private const float MoveSpeed = 4.7f; // tiles per second

        private Point _heldDirection = Point.Zero;

        public Player(GridPos start, int cellSize)
        {
            Cell = start;
            _from = start;
            _to = start;
            PixelPos = new Vector2(start.X * cellSize, start.Y * cellSize);
        }

        public void Update(GameTime gameTime, GridMap map, int cellSize)
        {
            ReadInput();

            bool moving = _t < 1f;

            if (!moving)
            {
                // Landed on a tile — try to move in the held direction.
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

                Vector2 fromPx = new(_from.X * cellSize, _from.Y * cellSize);
                Vector2 toPx = new(_to.X * cellSize, _to.Y * cellSize);
                PixelPos = Vector2.Lerp(fromPx, toPx, _t);

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

        public void Draw(SpriteBatch sb, Texture2D pixel, int cellSize)
        {
            var rect = new Rectangle((int)PixelPos.X + 4, (int)PixelPos.Y + 4, cellSize - 8, cellSize - 8);
            sb.Draw(pixel, rect, Color.DodgerBlue);
        }
    }
}
