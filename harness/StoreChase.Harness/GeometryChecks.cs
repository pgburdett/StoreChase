using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Xna.Framework;
using StoreChase;

namespace StoreChase.Harness;

/// <summary>
/// Smoke tests for the pure geometry builders and the graphics-free
/// subsystems.
///
/// These are the pieces the round simulation never touches, and they changed
/// vertex type during the texture work — so a bad UV or a bad normal would
/// be fatal at startup and invisible to every other check.
///
/// The assertions are on *invariants*, not on golden values: UVs must reach
/// the requested repeat counts, normals must be unit length, the cube must
/// sit on the floor, a shelf must block line of sight. Golden values would
/// break every time the art changed; invariants break only when something is
/// actually wrong.
/// </summary>
public static class GeometryChecks
{
    /// <summary>Runs every geometry check.</summary>
    public static void Run()
    {
        Check.Section("Geometry: cube mesh");
        CheckCube();

        Check.Section("Geometry: person mesh");
        CheckPerson();

        Check.Section("Geometry: cat mesh");
        CheckCat();

        Check.Section("Geometry: grid map");
        CheckGridMap();

        Check.Section("Geometry: ball");
        CheckBall();
    }

    private static void CheckCube()
    {
        var verts = CubeMesh.Build(Color.White, 2f, 3f);
        Check.That("cube has vertices", verts.Length > 0, $"{verts.Length} verts");

        foreach (var v in verts)
        {
            if (Math.Abs(v.Normal.Length() - 1f) > 0.001f)
            {
                Check.That("cube normals are unit length", false, $"normal length {v.Normal.Length():F4}");
                break;
            }
        }
        Check.That("cube normals are unit length", verts.All(v => Math.Abs(v.Normal.Length() - 1f) <= 0.001f));

        float minY = verts.Min(v => v.Position.Y);
        Check.That("cube sits on the floor (min Y is 0)", Math.Abs(minY) < 0.001f, $"minY {minY:F4}");

        // The cube is a unit cube in local space (X/Z span -0.5..0.5, Y spans
        // 0..1); the world scale is applied by the draw call. The two
        // arguments to Build are UV *repeat* counts, not dimensions.
        float maxY = verts.Max(v => v.Position.Y);
        Check.That("cube is a unit cube in local space", Math.Abs(maxY - 1f) < 0.001f, $"maxY {maxY:F4}");

        float maxAbsX = verts.Max(v => Math.Abs(v.Position.X));
        float maxAbsZ = verts.Max(v => Math.Abs(v.Position.Z));
        Check.That("cube is a unit cube in X/Z too",
            Math.Abs(maxAbsX - 0.5f) < 0.001f && Math.Abs(maxAbsZ - 0.5f) < 0.001f,
            $"maxX {maxAbsX:F4} maxZ {maxAbsZ:F4}");

        // The UVs must reach the requested repeat counts, or the texture tiles
        // once and stretches across the whole surface.
        float maxU = verts.Max(v => v.TexCoord.X);
        float maxV = verts.Max(v => v.TexCoord.Y);
        Check.That("cube UVs reach the repeat counts",
            Math.Abs(maxU - 2f) < 0.001f && Math.Abs(maxV - 3f) < 0.001f,
            $"maxU {maxU:F3} (want 2), maxV {maxV:F3} (want 3)");

        // The floor quad is a special case: it must be flat and face up.
        var floor = CubeMesh.BuildFloorQuad(Color.White, 10f, 8f, 5f, 4f);
        Check.That("floor quad has vertices", floor.Length > 0, $"{floor.Length} verts");
        Check.That("floor quad is flat", floor.All(v => Math.Abs(v.Position.Y) < 0.001f));
        Check.That("floor quad faces up", floor.All(v => v.Normal.Y > 0.99f));

        // UVs must actually reach the repeat counts, or the texture tiles
        // once and stretches across the whole surface.
        float floorMaxU = floor.Max(v => v.TexCoord.X);
        float floorMaxV = floor.Max(v => v.TexCoord.Y);
        Check.That("floor UVs reach the repeat counts",
            Math.Abs(floorMaxU - 5f) < 0.001f && Math.Abs(floorMaxV - 4f) < 0.001f,
            $"maxU {floorMaxU:F3} (want 5), maxV {floorMaxV:F3} (want 4)");
    }

    private static void CheckPerson()
    {
        var parts = PersonMesh.Build();
        Check.That("person has parts", parts.Length > 0, $"{parts.Length} parts");

        bool allUnit = parts.SelectMany(p => p.Vertices)
            .All(v => Math.Abs(v.Normal.Length() - 1f) <= 0.001f);
        Check.That("person normals are unit length", allUnit);

        // The parts are authored in part-local space around their pivot (the
        // legs hang from the hips, so their local Y is negative). The world
        // position is local + pivot, so that is what has to sit on the floor.
        float minWorldY = float.MaxValue;
        foreach (var p in parts)
            foreach (var v in p.Vertices)
                minWorldY = MathF.Min(minWorldY, v.Position.Y + p.Pivot.Y);

        Check.That("person's feet are on the floor", Math.Abs(minWorldY) < 0.02f, $"min world Y {minWorldY:F4}");

        float maxWorldY = float.MinValue;
        foreach (var p in parts)
            foreach (var v in p.Vertices)
                maxWorldY = MathF.Max(maxWorldY, v.Position.Y + p.Pivot.Y);
        Check.That("person is roughly human height", maxWorldY > 1.5f && maxWorldY < 3.5f, $"max world Y {maxWorldY:F2}");

        // Every part needs a pivot, or the limb can't swing.
        Check.That("every person part has a pivot", parts.All(p => p.Pivot != Vector3.Zero || p.SwingSign == 0f));
    }

    private static void CheckCat()
    {
        string path = CatObjPath();
        if (!System.IO.File.Exists(path))
        {
            Check.Note("cat OBJ not found; skipping");
            return;
        }

        var mesh = ObjLoader.ParseGeometry(path);
        Check.That("cat has vertices", mesh.Vertices.Length > 0, $"{mesh.Vertices.Length}");
        Check.That("cat has indices", mesh.Indices.Length > 0, $"{mesh.Indices.Length}");
        Check.That("cat indices are a whole number of triangles",
            mesh.Indices.Length % 3 == 0, $"{mesh.Indices.Length / 3} triangles");

        bool inRange = mesh.Indices.All(i => i >= 0 && i < mesh.Vertices.Length);
        Check.That("cat indices are in range", inRange);

        bool unitNormals = mesh.Vertices.All(v => Math.Abs(v.Normal.Length() - 1f) <= 0.01f);
        Check.That("cat normals are unit length", unitNormals);

        // The rig needs a finite, non-degenerate bounding box.
        var size = mesh.Size;
        Check.That("cat has a real bounding box",
            size.X > 0.01f && size.Y > 0.01f && size.Z > 0.01f,
            $"size {size.X:F2} x {size.Y:F2} x {size.Z:F2}");

        // The rig derives pivots from the geometry, so the animator must
        // build without throwing and must produce a finite mesh.
        var animator = new CatAnimator(mesh);
        animator.Update(new CatAnimInput(CatPose.Stand, false, false, 0f, 0f, 1f / 60f));
        Check.That("animator produces finite vertices",
            mesh.Vertices.All(v => float.IsFinite(v.Position.X) && float.IsFinite(v.Position.Y) && float.IsFinite(v.Position.Z)));
    }

    private static void CheckGridMap()
    {
        foreach (var layout in StoreLayouts.All)
        {
            var map = new GridMap(layout);
            Check.That($"'{layout.Name}' has the expected size",
                map.Width == layout.Width && map.Height == layout.Height,
                $"{map.Width}x{map.Height}");

            // The outer wall must be closed, or the player can walk off the map.
            bool closed = true;
            for (int x = 0; x < map.Width; x++)
            {
                if (map.IsWalkable(x, 0) || map.IsWalkable(x, map.Height - 1)) closed = false;
            }
            for (int y = 0; y < map.Height; y++)
            {
                if (map.IsWalkable(0, y) || map.IsWalkable(map.Width - 1, y)) closed = false;
            }
            Check.That($"'{layout.Name}' has a closed outer wall", closed);

            // The player start must be walkable and not on the counter.
            Check.That($"'{layout.Name}' player start is walkable",
                map.IsWalkable(layout.PlayerStart.X, layout.PlayerStart.Y));
            Check.That($"'{layout.Name}' player start is not on the counter",
                !map.IsCounter(layout.PlayerStart.X, layout.PlayerStart.Y));

            // The counter must be reachable from the start, and every cell on
            // it must be walkable.
            var dist = map.DistanceMapFrom(layout.PlayerStart);
            bool counterReachable = true;
            for (int x = map.CounterStart; x <= map.CounterEnd; x++)
            {
                if (!map.IsWalkable(x, map.CounterRow)) { counterReachable = false; break; }
                if (dist[x, map.CounterRow] == int.MaxValue) { counterReachable = false; break; }
            }
            Check.That($"'{layout.Name}' counter is walkable and reachable", counterReachable);

            // Every walkable cell must be reachable from the start. A store
            // with an unreachable pocket would strand loot there.
            int unreachable = 0;
            foreach (var c in map.WalkableCells())
                if (dist[c.X, c.Y] == int.MaxValue) unreachable++;
            Check.That($"'{layout.Name}' has no unreachable floor", unreachable == 0, $"{unreachable} stranded cells");

            // Line of sight must actually be blocked by shelves.
            bool foundBlockedPair = false;
            for (int x = 0; x < map.Width && !foundBlockedPair; x++)
                for (int y = 0; y < map.Height && !foundBlockedPair; y++)
                {
                    if (!map.IsWalkable(x, y)) continue;
                    // Look along the row for a shelf between two open cells.
                    for (int k = 1; k < map.Width - x; k++)
                    {
                        if (!map.IsWalkable(x + k, y)) continue;
                        if (!map.HasLineOfSight(new GridPos(x, y), new GridPos(x + k, y)))
                        { foundBlockedPair = true; break; }
                    }
                }
            Check.That($"'{layout.Name}' shelves block line of sight", foundBlockedPair);
        }
    }

    private static void CheckBall()
    {
        var map = new GridMap(StoreLayouts.Standard);
        var ball = new Ball();
        ball.Configure(2f);

        var start = new Vector3(2f, 1.2f, 2f);
        ball.Throw(start, new Vector2(0f, 1f));

        float travelled = 0f;
        var last = ball.Position;
        float dt = 1f / 60f;
        for (int i = 0; i < 600 && ball.State == BallState.Thrown; i++)
        {
            ball.Update(dt, map, new GridPos(1, 1));
            travelled += Vector3.Distance(last, ball.Position);
            last = ball.Position;
        }

        Check.That("a thrown ball travels a useful distance",
            travelled > 8f, $"{travelled:F1} world units");

        // It must never end up inside a shelf.
        bool insideGeometry = false;
        for (int i = 0; i < 600; i++)
        {
            ball.Update(dt, map, new GridPos(1, 1));
            var c = ball.Cell;
            if (map.IsWalkable(c.X, c.Y)) continue;
            // Being inside a blocked cell is only a problem if it is
            // meaningfully inside, not merely clipped by the radius.
            insideGeometry = true;
            break;
        }
        Check.That("the ball never comes to rest inside a shelf", !insideGeometry);
    }

    /// <summary>Finds the cat OBJ relative to the harness binary.</summary>
    internal static string CatObjPath()
    {
        string[] candidates =
        {
            Path.Combine("Content", "Models", "Cat.obj"),
            Path.Combine("..", "..", "Content", "Models", "Cat.obj"),
            Path.Combine("..", "..", "..", "Content", "Models", "Cat.obj"),
        };
        foreach (var c in candidates)
            if (System.IO.File.Exists(c)) return System.IO.Path.GetFullPath(c);

        // Walk up looking for the repo root.
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir != null)
        {
            string p = Path.Combine(dir.FullName, "Content", "Models", "Cat.obj");
            if (System.IO.File.Exists(p)) return p;
            dir = dir.Parent;
        }
        return "Content/Models/Cat.obj";
    }
}
