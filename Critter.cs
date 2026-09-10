using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace StoreChase
{
    // The thing you're chasing. Moves tile-to-tile like the player, but
    // picks its direction based on AI state instead of input.
    public class Critter
    {
        public GridPos Cell { get; private set; }
        public Vector2 PixelPos { get; private set; }
        public bool Caught { get; private set; }

        private GridPos _from;
        private GridPos _to;
        private float _t;
        private float _moveSpeed = 3.5f; // slightly slower than player by default

        private readonly Random _rng = new();
        private float _decisionCooldown = 0f;

        // How close (in tiles) the player has to be before the critter flees
        private const int FleeRadius = 5;

        public Critter(GridPos start, int cellSize)
        {
            Cell = start;
            _from = start;
            _to = start;
            PixelPos = new Vector2(start.X * cellSize, start.Y * cellSize);
        }

        public void Update(GameTime gameTime, GridMap map, int cellSize, GridPos playerCell)
        {
            if (Caught) return;

            if (Cell == playerCell)
            {
                Caught = true;
                return;
            }

            bool moving = _t < 1f;

            if (!moving)
            {
                Cell = _to;
                if (Cell == playerCell) { Caught = true; return; }

                var next = ChooseNextCell(map, playerCell);
                if (next.HasValue)
                {
                    _from = Cell;
                    _to = next.Value;
                    _t = 0f;
                    moving = true;
                }
            }

            if (moving)
            {
                float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;
                _t += dt * _moveSpeed;
                if (_t > 1f) _t = 1f;

                Vector2 fromPx = new(_from.X * cellSize, _from.Y * cellSize);
                Vector2 toPx = new(_to.X * cellSize, _to.Y * cellSize);
                PixelPos = Vector2.Lerp(fromPx, toPx, _t);

                if (_t >= 1f)
                {
                    Cell = _to;
                    if (Cell == playerCell) Caught = true;
                }
            }
        }

        private GridPos? ChooseNextCell(GridMap map, GridPos playerCell)
        {
            int dist = ManhattanDistance(Cell, playerCell);

            if (dist <= FleeRadius)
            {
                // Flee: pick the walkable neighbor that maximizes distance from the player.
                GridPos[] dirs = { new(0, -1), new(0, 1), new(-1, 0), new(1, 0) };
                GridPos? best = null;
                int bestDist = -1;

                foreach (var d in dirs)
                {
                    var candidate = new GridPos(Cell.X + d.X, Cell.Y + d.Y);
                    if (!map.IsWalkable(candidate.X, candidate.Y)) continue;

                    int candDist = ManhattanDistance(candidate, playerCell);
                    if (candDist > bestDist)
                    {
                        bestDist = candDist;
                        best = candidate;
                    }
                }
                return best;
            }

            // Otherwise wander randomly so it's not just sitting still.
            GridPos[] options = { new(0, -1), new(0, 1), new(-1, 0), new(1, 0) };
            // Shuffle-ish: try a random order until one is walkable.
            for (int i = options.Length - 1; i > 0; i--)
            {
                int j = _rng.Next(i + 1);
                (options[i], options[j]) = (options[j], options[i]);
            }

            foreach (var d in options)
            {
                var candidate = new GridPos(Cell.X + d.X, Cell.Y + d.Y);
                if (map.IsWalkable(candidate.X, candidate.Y)) return candidate;
            }

            return null;
        }

        private static int ManhattanDistance(GridPos a, GridPos b) =>
            Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y);

        public void Draw(SpriteBatch sb, Texture2D pixel, int cellSize)
        {
            var color = Caught ? Color.Gray : Color.OrangeRed;
            var rect = new Rectangle((int)PixelPos.X + 6, (int)PixelPos.Y + 6, cellSize - 12, cellSize - 12);
            sb.Draw(pixel, rect, color);
        }
    }
}
