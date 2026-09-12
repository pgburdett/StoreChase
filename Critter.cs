using System;
using Microsoft.Xna.Framework;

namespace StoreChase
{
    public class Critter
    {
        public GridPos Cell { get; private set; }
        public Vector3 WorldPos { get; private set; }
        public bool Caught { get; private set; }

        private GridPos _from;
        private GridPos _to;
        private float _t;
        private float _moveSpeed = 3.5f;

        private readonly Random _rng = new();
        private const int FleeRadius = 5;

        public Critter(GridPos start, float cellSize)
        {
            Cell = start;
            _from = start;
            _to = start;
            WorldPos = new Vector3(start.X * cellSize, 0f, start.Y * cellSize);
        }

        public void Update(GameTime gameTime, GridMap map, float cellSize, GridPos playerCell)
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

                Vector3 fromPos = new(_from.X * cellSize, 0f, _from.Y * cellSize);
                Vector3 toPos = new(_to.X * cellSize, 0f, _to.Y * cellSize);
                WorldPos = Vector3.Lerp(fromPos, toPos, _t);

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

            GridPos[] options = { new(0, -1), new(0, 1), new(-1, 0), new(1, 0) };
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
    }
}