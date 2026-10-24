using Microsoft.Xna.Framework;
using System;

namespace StoreChase
{
    /// <summary>
    /// Extracted movement logic for critters to reduce coupling with GridMap.
    /// </summary>
    public class MovementSystem
    {
        private readonly GridMap _map;
        private readonly float _cellSize;
        
        public MovementSystem(GridMap map, float cellSize)
        {
            _map = map ?? throw new ArgumentNullException(nameof(map));
            _cellSize = cellSize;
        }
        
        /// <summary>Get next step toward goal using BFS.</summary>
        public GridPos? GetNextStepToward(GridPos start, GridPos goal)
        {
            return _map.NextStepToward(start, goal);
        }
        
        /// <summary>Check if a position is walkable.</summary>
        public bool IsWalkable(int x, int y) => _map.IsWalkable(x, y);
        
        /// <summary>Get distance map from origin.</summary>
        public int[,] GetDistanceMap(GridPos origin) => _map.DistanceMapFrom(origin);
        
        /// <summary>Check line of sight between two points.</summary>
        public bool HasLineOfSight(GridPos from, GridPos to) => _map.HasLineOfSight(from, to);
        
        /// <summary>Convert world position to grid position.</summary>
        public GridPos WorldToGrid(Vector2 worldPos)
        {
            int x = (int)MathF.Floor(worldPos.X / _cellSize + 0.5f);
            int y = (int)MathF.Floor(worldPos.Y / _cellSize + 0.5f);
            return new GridPos(x, y);
        }
        
        /// <summary>Convert grid position to world position.</summary>
        public Vector2 GridToWorld(GridPos gridPos)
        {
            return new Vector2(gridPos.X * _cellSize, gridPos.Y * _cellSize);
        }
    }
}
