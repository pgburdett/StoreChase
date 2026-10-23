using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using StoreChase;

namespace StoreChase.Harness;

/// <summary>
/// Checks on the scrolling camera.
///
/// The camera is the one part of the game that is hard to reason about by
/// reading: whether it actually follows the player, whether the viewport is
/// smaller than the store, whether the view ever leaves the floor plan. All
/// three are pure arithmetic, so they are checked as arithmetic.
///
/// It needs no GraphicsDevice: the camera has a constructor that takes an
/// aspect ratio instead, which is why that constructor exists.
/// </summary>
public static class CameraChecks
{
    /// <summary>Runs every camera check.</summary>
    public static void Run()
    {        Check.Section("Camera: scrolling and framing");

        const float cellSize = 2f;
        const float aspect = 1600f / 1200f;
        const float zoom = Camera.DefaultZoom;

        foreach (var layout in StoreLayouts.All)
        {
            var map = new GridMap(layout);
            float mapW = map.Width * cellSize;
            float mapH = map.Height * cellSize;
            var cam = new Camera(aspect, mapW, mapH, cellSize);

            // The viewport must be smaller than the store, or there is nothing
            // to scroll. (The small reference store at maximum zoom-out is
            // allowed to fit — that is the point of zooming out.)
            float viewWidth = Camera.ViewCellsWide * cellSize * zoom;
            float viewHeight = viewWidth / aspect;

            Check.That($"'{layout.Name}': the viewport is smaller than the store at zoom 1",
                viewWidth < mapW || viewHeight < mapH,
                $"view {viewWidth:F0}x{viewHeight:F0} vs store {mapW:F0}x{mapH:F0}");

            // The camera must follow the player: moving the player across the
            // store must move the view.
            var start = layout.PlayerStart;
            var far = new GridPos(map.Width - 2, map.Height - 2);

            var camA = new Camera(aspect, mapW, mapH, cellSize);
            camA.Update(Frame(), InputState.Neutral, World(start, cellSize), 0f);
            var camB = new Camera(aspect, mapW, mapH, cellSize);
            camB.Update(Frame(), InputState.Neutral, World(far, cellSize), 0f);

            Vector3 viewCenterA = ViewCenter(camA);
            Vector3 viewCenterB = ViewCenter(camB);

            Check.That($"'{layout.Name}': the view follows the player",
                Vector3.Distance(viewCenterA, viewCenterB) > 1f,
                $"view moved {Vector3.Distance(viewCenterA, viewCenterB):F1} world units");

            // The view must never leave the store. Check every cell of the
            // floor plan: whatever the player is standing on, the view has to
            // stay on the map.
            bool staysInside = true;
            float worst = 0f;
            foreach (var cell in map.WalkableCells())
            {
                var c = new Camera(aspect, mapW, mapH, cellSize);
                c.Update(Frame(), InputState.Neutral, World(cell, cellSize), 0f);
                var center = ViewCenter(c);

                // The world is X/Z with Y up, so the view's position on the
                // floor plan is (X, Z).
                float cx = center.X;
                float cz = center.Z;

                // The visible rectangle is centred on the focus point. It has
                // to sit entirely inside the store — which spans
                // [-cellSize/2, mapWorld - cellSize/2] on both axes: grid
                // cells anchor at cell * cellSize and extend half a cell past
                // it, and both the floor draw and the camera's clamp work in
                // that frame (Game1.FloorWorld, Camera's map origin).
                float halfW = viewWidth / 2f;
                float halfH = viewHeight / 2f;
                float minEdge = -cellSize * 0.5f;
                float maxW = mapW - cellSize * 0.5f;
                float maxH = mapH - cellSize * 0.5f;

                float over = Math.Max(0f, minEdge + halfW - cx)   // past the left edge
                           + Math.Max(0f, cx + halfW - maxW)      // past the right edge
                           + Math.Max(0f, minEdge + halfH - cz)   // past the near edge
                           + Math.Max(0f, cz + halfH - maxH);     // past the far edge

                if (over > 0.01f) { staysInside = false; worst = Math.Max(worst, over); }
            }
            Check.That($"'{layout.Name}': the view never leaves the store", staysInside,
                staysInside ? "" : $"up to {worst:F2} units outside");

            // A point the player is standing on must be on screen.
            var camC = new Camera(aspect, mapW, mapH, cellSize);
            camC.Update(Frame(), InputState.Neutral, World(start, cellSize), 0f);
            Check.That($"'{layout.Name}': the player is always on screen",
                camC.IsOnScreen(World(start, cellSize)));

            // A point far across the store must NOT be on screen, or there is
            // no point to the scrolling at all.
            var camD = new Camera(aspect, mapW, mapH, cellSize);
            camD.Update(Frame(), InputState.Neutral, World(start, cellSize), 0f);
            var across = new Vector3(mapW - cellSize, 0f, mapH - cellSize);
            bool acrossVisible = camD.IsOnScreen(across);
            Check.That($"'{layout.Name}': the far side of the store is off screen",
                !acrossVisible || viewWidth >= mapW,
                acrossVisible ? "the far corner is visible" : "the far corner is not visible");
        }
    }

    private static GameTime Frame() =>
        new GameTime(TimeSpan.FromSeconds(1f / 60f), TimeSpan.FromSeconds(1f / 60f));

    private static Vector3 World(GridPos p, float cellSize) =>
        new(p.X * cellSize, 0f, p.Y * cellSize);

    // The world point the camera is looking at: the centre of the view.
    private static Vector3 ViewCenter(Camera cam) => cam.FocusPoint;
}
