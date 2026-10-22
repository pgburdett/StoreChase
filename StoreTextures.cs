using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace StoreChase;

// The store's surfaces, generated in code instead of loaded as image assets.
// Each one is a seamlessly tileable square, drawn from a small fixed palette
// and a seeded RNG so the store looks identical on every run.
//
// Why procedural rather than MGCB: these are repeating surface patterns, not
// art. Generating them keeps the project asset-free (like the cube geometry and
// the player) and makes the palette editable as code. The cat is the one piece
// of authored art, and it keeps its OBJ + JPEG.
//
// Every texture is written with a *complete* mip chain (see BuildMipmapped).
// MonoGame has no GenerateMipmaps on Texture2D, and a texture with only level
// 0 filled is mipmap-incomplete — a mipmapping sampler then reads black for
// every pixel, which is how the cat once vanished.
public static class StoreTextures
{
    // Tile edge length in texels. 128 is a good middle ground: enough detail
    // for steel striations and stone chips, few enough texels that the floor's
    // mip chain is cheap.
    private const int TileSize = 128;

    /// <summary>
    /// The Unit atlas's white band starts at this v: everything from here up
    /// is pure white, and faces that want an untinted vertex color (products,
    /// shelf lips, aisle signs) UV into the band so the texture multiplies to
    /// 1 and the color shows through clean. ShelfMesh mirrors this constant.
    /// </summary>
    public const float WhiteBandStart = 0.75f;

    /// <summary>A complete set of the store's procedurally generated textures.</summary>
    public sealed class Set : IDisposable
    {
        /// <summary>Floor terrazzo: pale slabs, stone chips, grout on the cell grid.</summary>
        public Texture2D Floor { get; init; } = null!;

        /// <summary>
        /// The shelf-unit atlas: brushed steel below <see cref="WhiteBandStart"/>,
        /// a pure-white tint band above it (see ShelfMesh).
        /// </summary>
        public Texture2D Unit { get; init; } = null!;

        /// <summary>Painted plaster with a baseboard, for the store's border ring.</summary>
        public Texture2D Wall { get; init; } = null!;

        /// <summary>Cardboard box face for loot.</summary>
        public Texture2D Cardboard { get; init; } = null!;

        /// <summary>Counter surface with a hazard stripe.</summary>
        public Texture2D Counter { get; init; } = null!;

        /// <summary>
        /// A flat white 1x1, for anything tinted purely by vertex color (the
        /// player, and any mesh that doesn't want a pattern).
        /// </summary>
        public Texture2D Flat { get; init; } = null!;

        /// <summary>Disposes all textures in the set.</summary>
        public void Dispose()
        {
            Floor?.Dispose();
            Unit?.Dispose();
            Wall?.Dispose();
            Cardboard?.Dispose();
            Counter?.Dispose();
            Flat?.Dispose();
        }
    }

    /// <summary>Builds the complete texture set for the store.</summary>
    public static Set Build(GraphicsDevice device)
    {
        return new Set
        {
            Floor = BuildMipmapped(device, TileSize, MakeFloor),
            Unit = BuildMipmapped(device, TileSize, MakeUnit),
            Wall = BuildMipmapped(device, TileSize, MakeWall),
            Cardboard = BuildMipmapped(device, TileSize / 2, MakeCardboard),
            Counter = BuildMipmapped(device, TileSize / 2, MakeCounter),
            Flat = BuildMipmapped(device, 4, (x, y, _, _) => Color.White),
        };
    }

    // ---- Surface painters -------------------------------------------------
    // Each takes normalized tile coordinates and returns the texel color.
    // They must be seamless: the value at x=0 has to meet x=1 cleanly, which
    // is why the patterns are built from periodic distances rather than from
    // one-sided ramps (see WrapDistance).

    // Store floor: terrazzo. A pale stone slab with colored chips scattered
    // through it and a grout ring around each tile edge — one tile per
    // texture period, and the floor is drawn one period per grid cell, so the
    // grout lands exactly on cell boundaries (the floor quad is drawn offset
    // half a cell for this; see Game1.Draw).
    private static Color MakeFloor(float x, float y, int w, int h)
    {
        const float groutFraction = 0.05f;
        float dEdge = MathF.Min(WrapDistance(x), WrapDistance(y));

        // Slab + fine noise.
        Color tile = Speckle(new Color(224, 220, 210), x, y, w, h, 9091u, 0.04f);

        // Chips: a hashed dot per cell of a 24x24 lattice. The neighbourhood
        // scan and the wrapped lattice indices are what let a dot straddle
        // the tile seam without the pattern tearing (StoreChecks compares
        // texels either side of x=0 against each other).
        const int grid = 24;
        float cx = x * grid, cy = y * grid;
        int ix = (int)MathF.Floor(cx), iy = (int)MathF.Floor(cy);

        Color[] palette =
        {
            new(72, 72, 78),      // charcoal
            new(172, 84, 70),     // brick red
            new(226, 208, 168),   // cream
            new(98, 118, 146),    // slate blue
            new(130, 150, 116),   // sage
        };

        for (int dy = -1; dy <= 1; dy++)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                int hx = ix + dx, hy = iy + dy;
                int wx = ((hx % grid) + grid) % grid;
                int wy = ((hy % grid) + grid) % grid;
                uint n = Hash((uint)wx, (uint)wy, 4711u);
                if (n % 10u < 3u) continue; // some cells stay empty

                // Dot centre inside its lattice cell, and its radius.
                float px = hx + 0.3f + ((n >> 8) & 0xFF) / 255f * 0.4f;
                float py = hy + 0.3f + ((n >> 16) & 0xFF) / 255f * 0.4f;
                float r = 0.10f + ((n >> 24) & 0x3F) / 63f * 0.14f;

                float ddx = cx - px, ddy = cy - py;
                if (ddx * ddx + ddy * ddy <= r * r)
                    tile = palette[(n >> 4) % (uint)palette.Length];
            }
        }

        // Grout ring around every tile edge, softened so mips don't shimmer.
        float edge = MathHelper.Clamp(dEdge / groutFraction - 0.5f, 0f, 1f);
        return Color.Lerp(new Color(108, 104, 96), tile, edge * edge * (3f - 2f * edge));
    }

    // The shelf unit's atlas. Brushed steel (vertical striations, a panel
    // seam) for everything structural, and a pure-white band above
    // WhiteBandStart for the faces whose vertex color must show through
    // untinted. Deliberately NOT a picture of a shelf anymore: the shelf is
    // real geometry now (ShelfMesh), and this only has to dress it.
    private static Color MakeUnit(float x, float y, int w, int h)
    {
        if (y >= WhiteBandStart)
            return Color.White; // exact: tint faces sample here.

        // Two scales of column hash = brushed vertical grain.
        uint col = (uint)(int)(x * w);
        float fine = Hash01(col, 3u, 0x5EE1u);
        float wide = Hash01(col / 3u, 7u, 0x5EE1u);
        Color steel = Color.Lerp(
            new Color(150, 153, 161), new Color(194, 197, 205), 0.55f * fine + 0.45f * wide);

        // A panel seam halfway through the period so uprights read as panels.
        float seam = WrapDistance(y * 2f);
        if (seam < 0.015f)
            steel = Color.Lerp(new Color(114, 117, 124), steel, seam / 0.015f);

        return Speckle(steel, x, y, w, h, 31337u, 0.03f);
    }

    // The store's border ring: painted plaster with block courses and a
    // darker baseboard along the bottom, so the edge of the map reads as
    // building rather than as more shelving.
    private static Color MakeWall(float x, float y, int w, int h)
    {
        Color plaster = Speckle(new Color(198, 190, 177), x, y, w, h, 5555u, 0.04f);

        // Faint courses every quarter tile.
        float course = WrapDistance(y * 4f);
        if (course < 0.025f)
            plaster = Color.Lerp(new Color(178, 171, 159), plaster, course / 0.025f);

        // Baseboard: the bottom eighth, darker, with a soft top edge.
        if (y < 0.14f)
        {
            Color board = Speckle(new Color(120, 112, 100), x, y, w, h, 8080u, 0.05f);
            float t = MathHelper.Clamp((y - 0.11f) / 0.03f, 0f, 1f);
            return Color.Lerp(board, plaster, t * t * (3f - 2f * t));
        }

        return plaster;
    }

    // Loot: a cardboard box face, with a taped seam across the middle and a
    // faint printed label block.
    private static Color MakeCardboard(float x, float y, int w, int h)
    {
        Color kraft = new(176, 138, 88);
        Color shade = new(150, 116, 72);

        float band = MathF.Abs(y - 0.5f);
        Color c = band < 0.09f ? new Color(198, 186, 164) : kraft;   // packing tape

        // Printed label, lower right.
        bool label = x > 0.58f && x < 0.88f && y > 0.60f && y < 0.80f;
        if (label) c = new Color(238, 236, 228);

        // A soft darkening toward the bottom edge grounds the box.
        c = Color.Lerp(shade, c, 0.35f + 0.65f * y);

        return Speckle(c, x, y, w, h, 7777u, 0.07f);
    }

    // The front counter / exit tiles: a darker, matte surface with a warning
    // stripe, so the goal reads as a distinct part of the room.
    private static Color MakeCounter(float x, float y, int w, int h)
    {
        Color baseColor = new(64, 68, 82);
        Color stripe = new(206, 176, 64);

        // Diagonal hazard stripes, periodic so the tile still wraps.
        float diag = WrapDistance((x + y) - 0.5f);
        bool caution = diag < 0.18f;

        Color c = caution ? stripe : baseColor;
        return Speckle(c, x, y, w, h, 3131u, 0.04f);
    }

    // ---- Test hooks -------------------------------------------------------
    // The painters are pure functions of tile coordinates, so the harness can
    // check grout placement, chip wrapping and the white-band contract with
    // no GraphicsDevice anywhere (StoreChecks).

    /// <summary>One floor texel at normalized tile coords (headless).</summary>
    public static Color SampleFloor(float x, float y) => MakeFloor(x, y, TileSize, TileSize);

    /// <summary>One unit-atlas texel at normalized tile coords (headless).</summary>
    public static Color SampleUnit(float x, float y) => MakeUnit(x, y, TileSize, TileSize);

    /// <summary>One wall texel at normalized tile coords (headless).</summary>
    public static Color SampleWall(float x, float y) => MakeWall(x, y, TileSize, TileSize);

    // ---- Helpers ----------------------------------------------------------

    // Shortest distance from t to the tile edge, in tile units. Using the
    // wrapped distance is what makes border features (grout, posts) continue
    // across the seam instead of stopping at it.
    private static float WrapDistance(float t)
    {
        t -= MathF.Floor(t);
        return MathF.Min(t, 1f - t);
    }

    // Deterministic per-texel noise, seeded so the store is byte-identical
    // between runs (a fresh Random per texel would be far too slow, and an
    // unseeded one would make the floor shimmer differently each launch).
    private static Color Speckle(Color c, float x, float y, int w, int h, uint seed, float amount)
    {
        uint n = Hash((uint)(x * w), (uint)(y * h), seed);
        float noise = ((n & 0xFFFF) / 65535f - 0.5f) * 2f * amount;

        return new Color(
            (byte)MathHelper.Clamp(c.R + noise * 255f, 0f, 255f),
            (byte)MathHelper.Clamp(c.G + noise * 255f, 0f, 255f),
            (byte)MathHelper.Clamp(c.B + noise * 255f, 0f, 255f),
            c.A);
    }

    private static uint Hash(uint x, uint y, uint seed)
    {
        uint h = seed;
        h ^= x * 0x9E3779B1u;
        h = (h << 13) | (h >> 19);
        h ^= y * 0x85EBCA77u;
        h = (h << 7) | (h >> 25);
        h ^= h >> 15;
        return h * 0xC2B2AE3Du;
    }

    // Hash folded to 0..1, for chip placement and grain.
    private static float Hash01(uint x, uint y, uint seed) =>
        (Hash(x, y, seed) & 0xFFFF) / 65535f;

    // Box-filters every mip level down to 1x1, filling the whole chain. See
    // the class comment for why an incomplete chain is not an option.
    private static Texture2D BuildMipmapped(
        GraphicsDevice device, int size, Func<float, float, int, int, Color> paint)
    {
        var level0 = new Color[size * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
                level0[y * size + x] = paint((x + 0.5f) / size, (y + 0.5f) / size, size, size);
        }

        var texture = new Texture2D(device, size, size, true, SurfaceFormat.Color);
        texture.SetData(0, null, level0, 0, level0.Length);

        var src = level0;
        int sw = size, sh = size;
        for (int level = 1; sw > 1 || sh > 1; level++)
        {
            int dw = Math.Max(1, sw / 2);
            int dh = Math.Max(1, sh / 2);
            var dst = new Color[dw * dh];
            for (int y = 0; y < dh; y++)
            {
                int sy0 = Math.Min(sh - 1, y * 2);
                int sy1 = Math.Min(sh - 1, y * 2 + 1);
                for (int x = 0; x < dw; x++)
                {
                    int sx0 = Math.Min(sw - 1, x * 2);
                    int sx1 = Math.Min(sw - 1, x * 2 + 1);
                    dst[y * dw + x] = Average(
                        src[sy0 * sw + sx0], src[sy0 * sw + sx1],
                        src[sy1 * sw + sx0], src[sy1 * sw + sx1]);
                }
            }
            texture.SetData(level, null, dst, 0, dst.Length);
            src = dst; sw = dw; sh = dh;
        }

        return texture;
    }

    private static Color Average(Color a, Color b, Color c, Color d) => new(
        (a.R + b.R + c.R + d.R) / 4,
        (a.G + b.G + c.G + d.G) / 4,
        (a.B + b.B + c.B + d.B) / 4,
        (a.A + b.A + c.A + d.A) / 4);
}
