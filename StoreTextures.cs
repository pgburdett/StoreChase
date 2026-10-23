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

    // Store floor: dark wood planks. Warm brown boards with a grain streak,
    // a seam between planks and a stagger so the joins don't line up across
    // the store. One texture period per grid cell, so the plank seam lands
    // exactly on cell boundaries (the floor quad is drawn offset half a cell
    // for this; see Game1.Draw).
    //
    // This replaced a pale terrazzo slab. Under the store's ambient the old
    // floor was the brightest surface in frame and the shelves read as
    // washed-out pastel against it; dark wood lets the product colours carry
    // the eye.
    private static Color MakeFloor(float x, float y, int w, int h)
    {
        const float seamFraction = 0.035f;

        // Three planks per tile: the board index and the position along it.
        const int planks = 3;
        float py = y * planks;
        int index = (int)MathF.Floor(py);
        float within = py - index;

        // Stagger: offset each plank's butt-joint by half a plank so the
        // short ends do not form a continuous line down the store.
        float shifted = x + index * 0.5f;
        float sx = shifted - MathF.Floor(shifted);

        // Per-plank tone so no two boards are the same brown.
        uint n = Hash((uint)(index & 7), 0u, 0xB0ABu);
        float v = ((n & 0xFF) / 255f - 0.5f) * 0.12f;

        // Base wood, a mid-dark walnut. Green carries a little more than the
        // other channels so the boards stay warm rather than grey.
        Color wood = new(
            (byte)MathHelper.Clamp(96 + v * 255f, 0f, 255f),
            (byte)MathHelper.Clamp(66 + v * 220f, 0f, 255f),
            (byte)MathHelper.Clamp(42 + v * 180f, 0f, 255f));

        // Grain: fine streaks running the length of the plank, plus a broader
        // slow wobble so the boards are not uniformly striped.
        float grain = Hash01((uint)(int)(x * w), (uint)(int)(py * 40f), 0x9A17u);
        float wobble = 0.5f + 0.5f * MathF.Sin((x * 6f + index) * MathF.PI * 2f);
        wood = Color.Lerp(wood, Color.Lerp(wood, new Color(58, 38, 24), 0.55f),
            0.10f + 0.22f * grain * (0.4f + 0.6f * wobble));

        // Dark seam at each plank edge (the long edges and the staggered butts).
        float dseam = MathF.Min(WrapDistance(py), MathF.Min(WrapDistance(shifted), 0.5f));
        float edge = MathHelper.Clamp(dseam / seamFraction - 0.5f, 0f, 1f);
        wood = Color.Lerp(new Color(38, 24, 15), wood, edge * edge * (3f - 2f * edge));

        return Speckle(wood, x, y, w, h, 9091u, 0.02f);
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

        // Two scales of column hash = brushed vertical grain. Toned down from
        // near-white steel so the shelf carcass reads as a mid grey against
        // the dark floor rather than as a bright frame.
        uint col = (uint)(int)(x * w);
        float fine = Hash01(col, 3u, 0x5EE1u);
        float wide = Hash01(col / 3u, 7u, 0x5EE1u);
        Color steel = Color.Lerp(
            new Color(104, 106, 114), new Color(140, 143, 152), 0.55f * fine + 0.45f * wide);

        // A panel seam halfway through the period so uprights read as panels.
        float seam = WrapDistance(y * 2f);
        if (seam < 0.015f)
            steel = Color.Lerp(new Color(74, 76, 84), steel, seam / 0.015f);

        return Speckle(steel, x, y, w, h, 31337u, 0.03f);
    }

    // The store's border ring: a cool painted block wall with courses and a
    // darker baseboard along the bottom, so the edge of the map reads as
    // building rather than as more shelving.
    //
    // Deliberately *cool* — blue-grey with no warm cast. The old warm beige
    // plaster picked up the alarm's red ambient and rendered as a flesh tone,
    // which read as skin rather than as a wall. A cool wall stays a wall at
    // every alarm level, and it separates the room from the warm wood floor.
    private static Color MakeWall(float x, float y, int w, int h)
    {
        Color plaster = Speckle(new Color(92, 100, 116), x, y, w, h, 5555u, 0.04f);

        // Faint courses every quarter tile.
        float course = WrapDistance(y * 4f);
        if (course < 0.025f)
            plaster = Color.Lerp(new Color(74, 82, 98), plaster, course / 0.025f);

        // Baseboard: the bottom eighth, darker, with a soft top edge.
        if (y < 0.14f)
        {
            Color board = Speckle(new Color(52, 58, 72), x, y, w, h, 8080u, 0.05f);
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
        Color baseColor = new(46, 50, 62);
        Color stripe = ArcadePalette.Dim(ArcadePalette.Yellow, 0.18f);

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
