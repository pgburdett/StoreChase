using System;
using System.Collections.Generic;
using System.Linq;

namespace StoreChase;

/// <summary>
/// A wall-mounted security camera and the field of them.
///
/// Pure state (no GraphicsDevice): the game reads <see cref="IsCovered"/>
/// to decide whether a carrying cat is in view, and disables a camera when
/// the player's ball lands on it. The view is a straight beam down an aisle
/// until a wall, so a camera reads as a line-of-sight cone you can dodge by
/// stepping behind a shelf.
/// </summary>
public sealed class SecurityCamera
{
    public GridPos Cell { get; }
    public int Direction { get; } // 0=+X, 1=+Y, 2=-X, 3=-Y
    public int ViewLength { get; } = 6;

    public SecurityCamera(GridPos cell, int direction) { Cell = cell; Direction = direction; }
}

public sealed class CameraField
{
    private readonly List<(SecurityCamera Cam, float DisabledUntil)> _cams = new();
    private float _time;

    public void Add(SecurityCamera cam) => _cams.Add((cam, 0f));

    public void Update(float dt) => _time += dt;

    public bool IsCovered(GridMap map, GridPos cell)
    {
        foreach (var (cam, until) in _cams)
        {
            if (_time < until) continue;
            int dx = cam.Direction == 0 ? 1 : cam.Direction == 2 ? -1 : 0;
            int dy = cam.Direction == 1 ? 1 : cam.Direction == 3 ? -1 : 0;
            int x = cam.Cell.X + dx, y = cam.Cell.Y + dy;
            for (int i = 0; i < cam.ViewLength; i++)
            {
                if (x < 0 || y < 0 || x >= map.Width || y >= map.Height) break;
                if (!map.IsWalkable(x, y)) break;
                if (x == cell.X && y == cell.Y) return true;
                x += dx; y += dy;
            }
        }
        return false;
    }

    public void DisableAt(GridPos cell, float duration)
    {
        for (int i = 0; i < _cams.Count; i++)
        {
            var (cam, until) = _cams[i];
            if (cam.Cell == cell)
                _cams[i] = (cam, _time + duration);
        }
    }

    public IReadOnlyList<SecurityCamera> Cameras => _cams.Select(c => c.Cam).ToList();
}
