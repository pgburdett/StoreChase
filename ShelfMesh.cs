using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;

namespace StoreChase;

/// <summary>Which side of a shelf run the end sign faces (the run terminates there).</summary>
public enum ShelfEnd
{
    None,
    PositiveX,
    NegativeX,
}

/// <summary>Which Z sides of a shelf unit open onto an aisle and get products.</summary>
public enum ShelfOpen
{
    PositiveZ,
    NegativeZ,
    BothSides,
}

/// <summary>
/// The appearance of one shelf unit: which aisle colour it carries, whether
/// it sits on the end of a run (and so gets a raised sign), which sides face
/// an aisle, and which product arrangement it shows. Game1 derives a style
/// per blocked cell from its neighbours and row, and the mesh set is built
/// once at load.
/// </summary>
public readonly record struct ShelfStyle(int Aisle, ShelfEnd End, ShelfOpen Open, int Variant);

/// <summary>
/// Real shelf-unit geometry, replacing the plain painted cube that used to
/// stand on every blocked cell. Everything lives in unit space — X/Z in
/// [-0.5, 0.5], Y in [0, 1] (signs a little above) — so Game1 scales it to a
/// cell exactly like the old cube.
///
/// What it actually builds: a plinth, two end uprights, three shelf boards
/// with lit lips, a central spine whose top face carries the aisle's colour
/// (that coloured strip is the aisle identity you read from the default
/// top-down camera), boxes of products standing on every aisle-facing board,
/// and — on run-end cells — a raised header sign above the end.
///
/// Winding is self-correcting at build time (see the Quad helper): the rule
/// the rasterizer wants is cross(v1-v0, v2-v0) · normal &lt; 0 on every
/// triangle, and hand-ordered corners are exactly what left holes in the old
/// cube. WindingChecks enforces the rule on this mesh too.
///
/// UVs land in one of two bands of the Unit atlas: steel faces take the
/// brushed band, and anything whose vertex colour should show through pure
/// (products, lips, the coloured cap, signs) takes the white band above
/// StoreTextures.WhiteBandStart, where the texture multiplies to 1.
/// </summary>
public static class ShelfMesh
{
    /// <summary>Unit-space height of a run-end sign, for extent checks.</summary>
    public const float SignTop = 1.14f;

    /// <summary>
    /// The aisle/zone colours. Game1 indexes these by shelf-row band, so a
    /// whole gondola run reads as one supermarket zone — on the spine strip
    /// from above, and on the end signs from the cross-aisle.
    ///
    /// These are the arcade palette's hues taken down in value (see
    /// ArcadePalette.Dim), not pastel tints. The earlier set was mixed
    /// independently and drifted pale and low-saturation, so against the
    /// brighter floor the aisles read as washed-out plastic; pulling them
    /// onto the palette keeps them saturated and lets the floor stay dark.
    /// </summary>
    public static readonly Color[] AislePalette =
    {
        ArcadePalette.Dim(ArcadePalette.Red, 0.20f),      // red
        ArcadePalette.Dim(ArcadePalette.Blue, 0.10f),     // blue
        ArcadePalette.Dim(ArcadePalette.Green, 0.30f),    // green
        ArcadePalette.Dim(ArcadePalette.Yellow, 0.28f),   // yellow
        ArcadePalette.Dim(ArcadePalette.Magenta, 0.28f),  // magenta (was purple)
        ArcadePalette.Dim(ArcadePalette.Orange, 0.15f),   // orange
    };

    // Packaging colours for the products: mostly the aisle hues mixed toward
    // the shelf's steel so the goods read as goods, with a couple of neutral
    // cartons (cream, kraft) so the shelves are not a solid rainbow.
    private static readonly Color[] ProductPalette =
    {
        ArcadePalette.Dim(ArcadePalette.Red, 0.15f),
        ArcadePalette.Dim(ArcadePalette.Blue, 0.05f),
        ArcadePalette.Dim(ArcadePalette.Green, 0.25f),
        ArcadePalette.Dim(ArcadePalette.Yellow, 0.22f),
        ArcadePalette.Dim(ArcadePalette.Magenta, 0.20f),
        ArcadePalette.Dim(ArcadePalette.Orange, 0.10f),
        new(198, 178, 138),   // cream carton
        new(120, 112, 104),   // grey carton
        new(232, 226, 210),   // white carton
        ArcadePalette.Dim(ArcadePalette.Cyan, 0.30f),
    };

    /// <summary>Builds one shelf unit in unit space.</summary>
    public static VertexPositionNormalColorTexture[] Build(ShelfStyle style)
    {
        var b = new Builder();
        var aisle = AislePalette[((style.Aisle % AislePalette.Length) + AislePalette.Length)
                                  % AislePalette.Length];

        // Deterministic across runs: same store, same shelves, every launch.
        var rng = new Random(
            style.Aisle * 131 + (int)style.End * 17 + (int)style.Open * 7 + style.Variant);

        // Plinth: the solid base. Bottom face skipped — it lies on the floor.
        // Dark steel, so the shelf carcass sits *on* the dark floor instead of
        // glowing above it.
        Box(b, new Vector3(-0.46f, 0f, -0.46f), new Vector3(0.46f, 0.20f, 0.46f),
            new Color(126, 129, 138), band: false, Faces.All & ~Faces.NegY);

        // The two end uprights, full height.
        Box(b, new Vector3(-0.50f, 0f, -0.46f), new Vector3(-0.45f, 1f, 0.46f),
            Color.White, band: false, Faces.All & ~Faces.NegY);
        Box(b, new Vector3(0.45f, 0f, -0.46f), new Vector3(0.50f, 1f, 0.46f),
            Color.White, band: false, Faces.All & ~Faces.NegY);

        // Three shelf boards. Steel everywhere except the aisle-facing lips,
        // which take the white band so their light edge stays crisp.
        ReadOnlySpan<(float bottom, float top)> boards = stackalloc[]
        {
            (0.24f, 0.275f), (0.50f, 0.535f), (0.76f, 0.795f),
        };
        foreach (var (bottom, top) in boards)
        {
            var min = new Vector3(-0.445f, bottom, -0.46f);
            var max = new Vector3(0.445f, top, 0.46f);
            Box(b, min, max, Color.White, band: false,
                Faces.PosX | Faces.NegX | Faces.PosY | Faces.NegY);

            var lip = new Color(196, 198, 206);
            b.Quad(new Vector3(0f, 0f, -1f),
                new Vector3(min.X, min.Y, min.Z), Builder.BandUv(0f, 0f),
                new Vector3(max.X, min.Y, min.Z), Builder.BandUv(1f, 0f),
                new Vector3(max.X, max.Y, min.Z), Builder.BandUv(1f, 1f),
                new Vector3(min.X, max.Y, min.Z), Builder.BandUv(0f, 1f), lip);
            b.Quad(new Vector3(0f, 0f, 1f),
                new Vector3(min.X, min.Y, max.Z), Builder.BandUv(0f, 0f),
                new Vector3(max.X, min.Y, max.Z), Builder.BandUv(1f, 0f),
                new Vector3(max.X, max.Y, max.Z), Builder.BandUv(1f, 1f),
                new Vector3(min.X, max.Y, max.Z), Builder.BandUv(0f, 1f), lip);
        }

        // The spine: closes the sightline through the gondola so you can't
        // see straight through the gaps between products, and its top face is
        // the coloured aisle strip you read from directly overhead. Its
        // bottom rests on the plinth, so that face is skipped.
        Box(b, new Vector3(-0.44f, 0.20f, -0.07f), new Vector3(0.44f, 1f, 0.07f),
            Color.White, band: false,
            Faces.PosX | Faces.NegX | Faces.PosY | Faces.PosZ | Faces.NegZ);
        b.Quad(Vector3.Up,
            new Vector3(-0.44f, 1f, -0.07f), Builder.BandUv(0f, 0f),
            new Vector3(0.44f, 1f, -0.07f), Builder.BandUv(1f, 0f),
            new Vector3(0.44f, 1f, 0.07f), Builder.BandUv(1f, 1f),
            new Vector3(-0.44f, 1f, 0.07f), Builder.BandUv(0f, 1f), aisle);

        // Products: four boxes per board on each aisle-facing side. The
        // back and bottom faces are never seen (the back faces the spine, the
        // bottom sits on the board), so only the front, top and sides are
        // built — front and top are what the tilted and top-down cameras get.
        const float rowBottom = 0.275f;   // first board's top; the rest follow
        const float productHeight = 0.16f;
        const float productWidth = 0.185f;
        const float productGap = 0.01f;
        const int perRow = 4;
        float rowSpan = perRow * productWidth + (perRow - 1) * productGap;
        float startX = -rowSpan / 2f;

        foreach (bool positive in OpenSides(style.Open))
        {
            float zMin = positive ? 0.30f : -0.45f;
            float zMax = positive ? 0.45f : -0.30f;
            Faces front = positive ? Faces.PosZ : Faces.NegZ;

            for (int row = 0; row < 3; row++)
            {
                // Board tops: 0.275, 0.535, 0.795 (boards are 0.26 apart).
                float bottom = rowBottom + row * 0.26f;
                for (int i = 0; i < perRow; i++)
                {
                    float left = startX + i * (productWidth + productGap);
                    var color = ProductPalette[rng.Next(ProductPalette.Length)];
                    Box(b,
                        new Vector3(left, bottom, zMin),
                        new Vector3(left + productWidth, bottom + productHeight, zMax),
                        color, band: true,
                        Faces.PosX | Faces.NegX | Faces.PosY | front);
                }
            }
        }

        // The raised header sign on a run end. It sits a hair above the
        // uprights (0.001) so no two faces ever share a plane.
        if (style.End != ShelfEnd.None)
        {
            bool positive = style.End == ShelfEnd.PositiveX;
            var signMin = new Vector3(positive ? 0.15f : -0.50f, 1.001f, -0.46f);
            var signMax = new Vector3(positive ? 0.50f : -0.15f, SignTop, 0.46f);
            Box(b, signMin, signMax, aisle, band: true, Faces.All);
        }

        return b.V.ToArray();
    }

    // Which Z sides get products.
    private static IEnumerable<bool> OpenSides(ShelfOpen open) => open switch
    {
        ShelfOpen.BothSides => new[] { true, false },
        ShelfOpen.PositiveZ => new[] { true },
        _ => new[] { false },
    };

    // ---- Geometry helpers -------------------------------------------------

    [Flags]
    private enum Faces
    {
        None = 0,
        PosX = 1,
        NegX = 2,
        PosY = 4,
        NegY = 8,
        PosZ = 16,
        NegZ = 32,
        All = PosX | NegX | PosY | NegY | PosZ | NegZ,
    }

    private sealed class Builder
    {
        public readonly List<VertexPositionNormalColorTexture> V = new();

        /// <summary>UVs into the Unit atlas's brushed-steel band.</summary>
        public static Vector2 SteelUv(float s, float t) => new(s, 0.06f + t * 0.60f);

        /// <summary>UVs into the pure-white band; the vertex colour shows through.</summary>
        public static Vector2 BandUv(float s, float t) => new(s, 0.80f + t * 0.16f);

        /// <summary>
        /// One quad, wound at build time so the triangle front agrees with the
        /// normal (cross · n &lt; 0 — see CubeMesh.Build's Face for the full
        /// story). Corners may arrive in any cyclic order.
        /// </summary>
        public void Quad(Vector3 n,
            Vector3 a, Vector2 ua, Vector3 b, Vector2 ub,
            Vector3 c, Vector2 uc, Vector3 d, Vector2 ud, Color color)
        {
            if (Vector3.Dot(Vector3.Cross(b - a, c - a), n) < 0f)
            {
                Add(a, n, ua, color); Add(b, n, ub, color); Add(c, n, uc, color);
                Add(a, n, ua, color); Add(c, n, uc, color); Add(d, n, ud, color);
            }
            else
            {
                Add(a, n, ua, color); Add(c, n, uc, color); Add(b, n, ub, color);
                Add(a, n, ua, color); Add(d, n, ud, color); Add(c, n, uc, color);
            }
        }

        private void Add(Vector3 p, Vector3 n, Vector2 uv, Color color) =>
            V.Add(new VertexPositionNormalColorTexture(p, n, color, uv));
    }

    // A box: emits whichever faces were asked for, each with its own face
    // normal and a UV rect mapped over the face (steel band or white band).
    private static void Box(
        Builder b, Vector3 min, Vector3 max, Color color, bool band, Faces faces)
    {
        Vector2 U(float s, float t) => band ? Builder.BandUv(s, t) : Builder.SteelUv(s, t);

        if ((faces & Faces.NegX) != 0)
            b.Quad(new Vector3(-1f, 0f, 0f),
                new Vector3(min.X, min.Y, min.Z), U(0f, 0f),
                new Vector3(min.X, min.Y, max.Z), U(1f, 0f),
                new Vector3(min.X, max.Y, max.Z), U(1f, 1f),
                new Vector3(min.X, max.Y, min.Z), U(0f, 1f), color);

        if ((faces & Faces.PosX) != 0)
            b.Quad(new Vector3(1f, 0f, 0f),
                new Vector3(max.X, min.Y, max.Z), U(0f, 0f),
                new Vector3(max.X, min.Y, min.Z), U(1f, 0f),
                new Vector3(max.X, max.Y, min.Z), U(1f, 1f),
                new Vector3(max.X, max.Y, max.Z), U(0f, 1f), color);

        if ((faces & Faces.NegY) != 0)
            b.Quad(new Vector3(0f, -1f, 0f),
                new Vector3(min.X, min.Y, max.Z), U(0f, 0f),
                new Vector3(max.X, min.Y, max.Z), U(1f, 0f),
                new Vector3(max.X, min.Y, min.Z), U(1f, 1f),
                new Vector3(min.X, min.Y, min.Z), U(0f, 1f), color);

        if ((faces & Faces.PosY) != 0)
            b.Quad(new Vector3(0f, 1f, 0f),
                new Vector3(min.X, max.Y, min.Z), U(0f, 0f),
                new Vector3(max.X, max.Y, min.Z), U(1f, 0f),
                new Vector3(max.X, max.Y, max.Z), U(1f, 1f),
                new Vector3(min.X, max.Y, max.Z), U(0f, 1f), color);

        if ((faces & Faces.NegZ) != 0)
            b.Quad(new Vector3(0f, 0f, -1f),
                new Vector3(max.X, min.Y, min.Z), U(0f, 0f),
                new Vector3(min.X, min.Y, min.Z), U(1f, 0f),
                new Vector3(min.X, max.Y, min.Z), U(1f, 1f),
                new Vector3(max.X, max.Y, min.Z), U(0f, 1f), color);

        if ((faces & Faces.PosZ) != 0)
            b.Quad(new Vector3(0f, 0f, 1f),
                new Vector3(min.X, min.Y, max.Z), U(0f, 0f),
                new Vector3(max.X, min.Y, max.Z), U(1f, 0f),
                new Vector3(max.X, max.Y, max.Z), U(1f, 1f),
                new Vector3(min.X, max.Y, max.Z), U(0f, 1f), color);
    }
}
