using System;
using System.Linq;
using Microsoft.Xna.Framework;
using StoreChase;

namespace StoreChase.Harness;

/// <summary>
/// Checks on the winding of the geometry.
///
/// The floor was invisible for as long as the texture work existed, and
/// nothing in the source could tell you: the vertex data was perfectly
/// reasonable and the *winding* was wrong, so the quad was back-facing and
/// got culled. A screenshot of the running game was the only thing that
/// showed it.
///
/// So this checks the winding as a property of the geometry rather than
/// trusting the eye. The rule is that a face and the camera-facing face of
/// the same cube must agree: the floor is a single quad laid flat, and the
/// cube's up face is the same orientation of surface, so they must have the
/// same screen-space winding once projected.
///
/// The stronger version of that rule needs no camera at all. For every mesh
/// drawn with BasicEffect and outward-authored normals, the game's rasterizer
/// keeps a triangle exactly when cross(v1-v0, v2-v0) · normal is *negative*
/// (the floor quad is uniform-negative and renders; a screenshot proved it).
/// That single sign is the whole visibility story: it is how the floor stayed
/// invisible for months, how the cube's ±Z faces stayed backwards after the
/// first fix (holes you could see through in tilted and first-person views),
/// and how the player ended up a hollow shell. The mesh builders now
/// self-correct at build time; these checks fail the moment a new one
/// hand-orders its corners wrongly again.
///
/// The cat is the documented exemption: it renders through the custom
/// CatNormalMap.fx with its own lighting, and its OBJ normals run the other
/// way (uniform *positive*), so only winding *uniformity* is checked there —
/// a mesh that is half-and-half can never close, whatever sign it chose.
///
/// This needs no graphics device — the winding is a property of the vertices
/// and the projection, both of which are available headlessly.
/// </summary>
public static class WindingChecks
{
    /// <summary>Runs every winding check.</summary>
    public static void Run()
    {
        Check.Section("Winding: faces point the way they claim");
        CheckTriangleInvariant();

        // A top-down camera over the store, which is the view the game opens
        // in and the one the floor has to survive.
        const float aspect = 1600f / 1200f;
        const float cellSize = 2f;

        var layout = StoreLayouts.BigBox;
        var map = new GridMap(layout);
        var cam = new Camera(aspect, map.Width * cellSize, map.Height * cellSize, cellSize);
        cam.Update(Frame(), InputState.Neutral, new Vector3(0f, 0f, 0f), 0f);

        // A unit floor quad and the cube's unit up face are the same shape
        // facing the same way, so they must project to the same winding. The
        // floor is compared at unit size rather than at store size so the two
        // are directly comparable.
        var floor = CubeMesh.BuildFloorQuad(Color.White, 1f, 1f, 1f, 1f);
        Check.That("the floor quad has two triangles", floor.Length == 6, $"{floor.Length} verts");

        // The cube's faces, in the order CubeMesh.Build emits them.
        var cube = CubeMesh.Build(Color.White, 2f, 3f);
        const int faceSize = 6;
        int upFace = 4 * faceSize;   // Back, Forward, Left, Right, Up, Down

        // The cube mesh is in local space around the origin, so it has to be
        // placed in the world before it can be projected — and placed where
        // the camera is actually looking, or the projection is edge-on and the
        // area is meaningless.
        var placement = Matrix.CreateTranslation(cam.FocusPoint);

        float floorArea = ScreenArea(cam, floor, 0, Matrix.Identity);
        float floorArea2 = ScreenArea(cam, floor, 3, Matrix.Identity);
        float upArea = ScreenArea(cam, cube, upFace, placement);
        float downArea = ScreenArea(cam, cube, 5 * faceSize, placement);

        Check.That("the floor's two triangles are wound the same way",
            Math.Sign(floorArea) == Math.Sign(floorArea2),
            $"{floorArea:F1} vs {floorArea2:F1}");

        // The regression: the floor used to be wound the opposite way to the
        // cube's up face, which is what got it culled.
        Check.That("the floor faces the same way as the cube's up face",
            Math.Sign(floorArea) == Math.Sign(upArea),
            $"floor {floorArea:F3}, cube up face {upArea:F3}");

        // Sanity: the cube's down face is the opposite way round, so a sign
        // that matched everything would mean the test was vacuous.
        Check.That("the cube's up and down faces are wound oppositely",
            Math.Sign(upArea) != Math.Sign(downArea),
            $"up {upArea:F3}, down {downArea:F3}");
    }

    private static GameTime Frame() =>
        new GameTime(TimeSpan.FromSeconds(1f / 60f), TimeSpan.FromSeconds(1f / 60f));

    /// <summary>
    /// The signed area of a triangle in screen space, for the first triangle of
    /// the given vertices starting at <paramref name="startVertex"/>.
    /// </summary>
    /// <remarks>
    /// The projection is applied by hand rather than through the camera's
    /// matrices, because the screen's Y axis points down while the world's
    /// points up: the sign of the area is what tells the two apart, so the
    /// flip has to be applied consistently.
    /// </remarks>
    private static float ScreenArea(
        Camera cam, VertexPositionNormalColorTexture[] verts, int startVertex, Matrix world)
    {
        var a = Project(cam, Vector3.Transform(verts[startVertex].Position, world));
        var b = Project(cam, Vector3.Transform(verts[startVertex + 1].Position, world));
        var c = Project(cam, Vector3.Transform(verts[startVertex + 2].Position, world));

        return (b.X - a.X) * (c.Y - a.Y) - (c.X - a.X) * (b.Y - a.Y);
    }

    // Screen-space position, with Y increasing downwards the way a framebuffer
    // does.
    private static Vector2 Project(Camera cam, Vector3 world)
    {
        Vector4 clip = Vector4.Transform(world, cam.ViewProjection);
        float ndcX = clip.X / clip.W;
        float ndcY = clip.Y / clip.W;
        return new Vector2((ndcX + 1f) * 0.5f, (1f - ndcY) * 0.5f);
    }

    /// <summary>
    /// The camera-free rule: every triangle of every BasicEffect mesh must
    /// satisfy cross(v1-v0, v2-v0) · normal &lt; 0, the sign the rasterizer
    /// keeps (established by the floor quad, which is uniform-negative and
    /// renders). The mesh builders self-correct at build time, so a violation
    /// here means either a new builder got the rule backwards or the sign
    /// itself changed — both worth failing loudly.
    /// </summary>
    private static void CheckTriangleInvariant()
    {
        Check.That("the cube is front-wound on every face",
            FrontWound(CubeMesh.Build(Color.White, 2f, 3f), out int cubeNeg, out int cubePos),
            $"{cubeNeg} negative, {cubePos} positive");

        Check.That("the floor quad is front-wound",
            FrontWound(CubeMesh.BuildFloorQuad(Color.White, 10f, 8f, 5f, 4f), out int fNeg, out int fPos),
            $"{fNeg} negative, {fPos} positive");

        string[] names = { "torso", "head", "left arm", "right arm", "left leg", "right leg" };
        var parts = PersonMesh.Build();
        for (int i = 0; i < parts.Length; i++)
            Check.That($"the person's {(i < names.Length ? names[i] : $"part {i}")} is front-wound",
                FrontWound(parts[i].Vertices, out int pNeg, out int pPos),
                $"{pNeg} negative, {pPos} positive");

        // Every shelf style, aggregated: 6 aisles x 3 ends x 3 opens x 3
        // variants share one line because they share one builder (a failure
        // here is a Builder/Box bug, not a per-style one). Aggregating also
        // keeps the check count from ballooning past what a human reads.
        int shelfBad = 0, shelfTris = 0;
        foreach (var style in AllShelfStyles())
        {
            FrontWound(ShelfMesh.Build(style), out int sNeg, out int sPos);
            shelfBad += sPos;
            shelfTris += sNeg + sPos;
        }
        Check.That("every shelf-unit style is front-wound",
            shelfBad == 0, $"{shelfTris} triangles, {shelfBad} positive");

        // The cat is exempt from the sign rule (custom shader, inward normals)
        // but never from uniformity: a half-and-half mesh cannot close.
        var cat = ObjLoader.ParseGeometry(GeometryChecks.CatObjPath());
        bool catUniform = CatUniform(cat.Vertices, cat.Indices, out bool catNeg);
        Check.That("the cat's winding is uniform (sign exempt, see class doc)",
            catUniform, catNeg ? "uniform negative" : "mixed or uniform positive");
    }

    // Every style combination BuildShelfMeshes will ever ask for (Game1's
    // mesh table covers exactly this cross-product).
    private static System.Collections.Generic.IEnumerable<ShelfStyle> AllShelfStyles()
    {
        for (int aisle = 0; aisle < ShelfMesh.AislePalette.Length; aisle++)
            for (int e = 0; e < 3; e++)
                for (int o = 0; o < 3; o++)
                    for (int v = 0; v < 3; v++)
                        yield return new ShelfStyle(aisle, (ShelfEnd)e, (ShelfOpen)o, v);
    }

    private static bool FrontWound(
        VertexPositionNormalColorTexture[] verts, out int negative, out int positive)
    {
        negative = 0;
        positive = 0;
        for (int i = 0; i + 2 < verts.Length; i += 3)
        {
            var cross = Vector3.Cross(
                verts[i + 1].Position - verts[i].Position,
                verts[i + 2].Position - verts[i].Position);
            if (cross.LengthSquared() < 1e-12f)
                continue; // Degenerate (zero-area) triangles cull silently either way.
            var n = verts[i].Normal;
            if (Vector3.Dot(cross, n) < 0f) negative++;
            else positive++;
        }
        return positive == 0;
    }

    private static bool CatUniform(
        VertexPositionNormalTextureTangent[] verts, int[] indices, out bool negative)
    {
        negative = false;
        bool sawNeg = false, sawPos = false;
        for (int i = 0; i + 2 < indices.Length; i += 3)
        {
            var a = verts[indices[i]];
            var b = verts[indices[i + 1]];
            var c = verts[indices[i + 2]];
            var cross = Vector3.Cross(b.Position - a.Position, c.Position - a.Position);
            if (cross.LengthSquared() < 1e-12f)
                continue;
            var n = (a.Normal + b.Normal + c.Normal) / 3f;
            if (Vector3.Dot(cross, n) < 0f) sawNeg = true;
            else sawPos = true;
        }
        negative = sawNeg && !sawPos;
        return sawNeg != sawPos; // Uniform iff exactly one sign appeared.
    }
}
