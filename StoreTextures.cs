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
    // for shelf slats, few enough texels that the floor's mip chain is cheap.
    private const int TileSize = 128;

    public sealed class Set : IDisposable
    {
        public Texture2D Floor { get; init; } = null!;
        public Texture2D Shelf { get; init; } = null!;
        public Texture2D Cardboard { get; init; } = null!;
        public Texture2D Counter { get; init; } = null!;
        // A flat white 1x1, for anything tinted purely by vertex color (the
        // player, and any mesh that doesn't want a pattern).
        public Texture2D Flat { get; init; } = null!;

        public void Dispose()
        {
            Floor?.Dispose();
            Shelf?.Dispose();
            Cardboard?.Dispose();
            Counter?.Dispose();
            Flat?.Dispose();
        }
    }

    public static Set Build(GraphicsDevice device)
    {
        return new Set
        {
            Floor = BuildMipmapped(device, TileSize, MakeFloor),
            Shelf = BuildMipmapped(device, TileSize, MakeShelf),
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

    // Store floor: large pale tiles with a darker grout line between them, plus
    // a fine speckle so the flat expanse doesn't look like plastic under the
    // ceiling lights.
    private static Color MakeFloor(float x, float y, int w, int h)
    {
        const float groutFraction = 0.06f;
        float dEdge = MathF.Min(WrapDistance(x), WrapDistance(y));

        Color tile = new(148, 148, 154);
        Color grout = new(96, 96, 104);

        // Smooth the grout edge a little so mip levels don't shimmer.
        float edge = MathHelper.Clamp(dEdge / groutFraction - 0.5f, 0f, 1f);
        Color c = Color.Lerp(grout, tile, edge * edge * (3f - 2f * edge));

        return Speckle(c, x, y, w, h, 9091u, 0.05f);
    }

    // A shelf unit: stacked boards with dark shadow gaps between them, a
    // highlight along the front lip of each board, and a vertical divider.
    private static Color MakeShelf(float x, float y, int w, int h)
    {
        const float boards = 3f;
        // Position within the current board, 0..1, and the board index.
        float scaled = y * boards;
        float within = scaled - MathF.Floor(scaled);

        Color back = new(58, 56, 62);          // shadowed interior
        Color board = new(122, 96, 72);        // the board face
        Color lip = new(158, 128, 96);         // front edge catching the light

        Color c;
        if (within < 0.12f) c = back;          // gap above each board
        else if (within < 0.88f) c = board;
        else c = lip;                          // thin lit lip at the bottom

        // A vertical support post roughly every half tile.
        float post = WrapDistance(x - 0.5f);
        if (post < 0.035f) c = Color.Lerp(new Color(78, 78, 86), c, within < 0.12f ? 0.2f : 0.75f);

        return Speckle(c, x, y, w, h, 4242u, 0.06f);
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
