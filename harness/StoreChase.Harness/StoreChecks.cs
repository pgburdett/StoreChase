using System;
using Microsoft.Xna.Framework;
using StoreChase;

namespace StoreChase.Harness;

/// <summary>
/// Invariants for the store's visible surfaces: the shelf units (ShelfMesh)
/// and the procedural textures (StoreTextures).
///
/// These are the pieces the round simulation never touches, and they fail
/// quietly when wrong — a shelf a hair outside its cell reads as a wall you
/// can clip, a product built on the closed side of a gondola is invisible
/// clutter, a UV parked in the gap between the Unit atlas's two bands samples
/// a mip-blended smear instead of steel or a clean tint. None of it throws;
/// it just looks broken, which is exactly how the twisted shelf lip and the
/// invisible floor shipped.
///
/// So the assertions are on invariants rather than golden values (UV bands,
/// cell extents, "grout darker than tile"), and everything here runs without
/// a GraphicsDevice: the painters are pure functions of tile coordinates and
/// the meshes are vertex arrays.
/// </summary>
public static class StoreChecks
{
    /// <summary>Runs every store-surface check.</summary>
    public static void Run()
    {
        Check.Section("Store: shelf unit geometry");
        CheckShelfGeometry();

        Check.Section("Store: procedural surfaces");
        CheckTextures();
    }

    // The Unit atlas's two UV bands, mirrored from ShelfMesh's builders:
    // steel lives in [0.06, 0.66], tints in [0.80, 0.96], and nothing may
    // sit in the gap between them (a sample there blends the two bands once
    // mips kick in and the face renders as soup).
    private const float SteelVMin = 0.06f;
    private const float SteelVMax = 0.66f;
    private const float BandVMin = 0.80f;
    private const float BandVMax = 0.96f;

    private static void CheckShelfGeometry()
    {
        int extentViolations = 0, degenerate = 0, normalMismatch = 0;
        int uvViolations = 0, tintedSteel = 0, signErrors = 0, productErrors = 0;
        float worstExtent = 0f, worstNormal = float.MinValue;
        int productMeshes = 0, signMeshes = 0;

        foreach (var style in AllShelfStyles())
        {
            var verts = ShelfMesh.Build(style);

            // ---- The unit must fit its cell. Game1 scales this mesh into a
            // cell box, so anything past the unit bounds pokes into the
            // aisle (clip-through) or below the floor.
            foreach (var v in verts)
            {
                float over = Math.Max(
                    Math.Max(Math.Abs(v.Position.X) - 0.5f, Math.Abs(v.Position.Z) - 0.5f),
                    Math.Max(-v.Position.Y, v.Position.Y - ShelfMesh.SignTop));
                if (over > 1e-4f)
                {
                    extentViolations++;
                    worstExtent = Math.Max(worstExtent, over);
                }
            }

            // ---- Triangle quality: no zero-area faces (a twisted quad used
            // to ship exactly this way), and each triangle's geometric
            // normal must sit exactly anti-parallel to the stored one — that
            // opposition *is* this game's front-facing convention (cross·n <
            // 0 renders; see WindingChecks), and it only holds for a planar,
            // correctly-ordered face. A twisted quad passes the winding-sign
            // rule but fails here, because its two halves face different
            // directions.
            for (int i = 0; i + 2 < verts.Length; i += 3)
            {
                var cross = Vector3.Cross(
                    verts[i + 1].Position - verts[i].Position,
                    verts[i + 2].Position - verts[i].Position);
                if (cross.LengthSquared() < 1e-12f)
                {
                    degenerate++;
                    continue;
                }

                float agree = Vector3.Dot(Vector3.Normalize(cross), verts[i].Normal);
                worstNormal = Math.Max(worstNormal, agree);
                if (agree > -0.99f)
                    normalMismatch++;
            }

            // ---- UV contract: u in [0,1], v inside exactly one of the two
            // bands, never in the dead gap between them.
            foreach (var v in verts)
            {
                bool inSteel = v.TexCoord.X >= 0f && v.TexCoord.Y >= SteelVMin - 1e-4f
                               && v.TexCoord.Y <= SteelVMax + 1e-4f;
                bool inBand = v.TexCoord.X <= 1f && v.TexCoord.Y >= BandVMin - 1e-4f
                              && v.TexCoord.Y <= BandVMax + 1e-4f;
                if (!inSteel && !inBand)
                    uvViolations++;

                // Steel carries the texture's pattern; a saturated vertex
                // colour on the steel band means a product/lip/sign face
                // drifted onto it and will render as mottled grey.
                if (inSteel)
                {
                    var c = v.Color;
                    int span = Math.Max(c.R, Math.Max(c.G, c.B)) - Math.Min(c.R, Math.Min(c.G, c.B));
                    if (span > 20)
                        tintedSteel++;
                }
            }

            // ---- The header sign is the only geometry above the uprights:
            // it exists exactly on run ends.
            float maxY = float.MinValue;
            foreach (var v in verts)
                maxY = Math.Max(maxY, v.Position.Y);
            bool hasSign = style.End != ShelfEnd.None;
            if (hasSign) signMeshes++;
            if (hasSign != (maxY > 1.0001f))
                signErrors++;

            // ---- Products (band-UV faces below the spine cap, inset from
            // the cell edge — lips sit at |z| = 0.46 exactly, the sign and
            // spine cap are at y >= 1) may only stand on a side that opens
            // onto an aisle.
            float minProductZ = float.MaxValue, maxProductZ = float.MinValue;
            bool anyProduct = false;
            foreach (var v in verts)
            {
                if (v.TexCoord.Y < BandVMin || v.Position.Y >= 1f)
                    continue;
                float az = Math.Abs(v.Position.Z);
                if (az < 0.30f || az > 0.459f)
                    continue; // spine/lips/caps, not a product.

                anyProduct = true;
                productMeshes++;
                minProductZ = Math.Min(minProductZ, v.Position.Z);
                maxProductZ = Math.Max(maxProductZ, v.Position.Z);
            }

            if (style.Open == ShelfOpen.PositiveZ && anyProduct && minProductZ < 0.29f)
                productErrors++;
            else if (style.Open == ShelfOpen.NegativeZ && anyProduct && maxProductZ > -0.29f)
                productErrors++;
            else if (style.Open == ShelfOpen.BothSides && anyProduct
                     && (minProductZ > -0.29f || maxProductZ < 0.29f))
                productErrors++;
        }

        Check.That("shelf units stay inside their cell",
            extentViolations == 0, $"{extentViolations} vertices out, worst {worstExtent:F4}");
        Check.That("shelf triangles have area (no twisted quads)",
            degenerate == 0, $"{degenerate} degenerate");
        Check.That("shelf triangle normals agree with their geometry",
            normalMismatch == 0, $"worst dot {worstNormal:F3} (want -1), {normalMismatch} bad");
        Check.That("shelf UVs land in one of the Unit atlas's two bands",
            uvViolations == 0, $"{uvViolations} vertices in the gap");
        Check.That("steel-band shelf faces stay untinted",
            tintedSteel == 0, $"{tintedSteel} vertices with a colour span > 20");
        Check.That("the header sign appears exactly on run ends",
            signErrors == 0, $"{signMeshes} signed styles, {signErrors} mismatches");
        Check.That("products stand only on aisle-facing sides",
            productErrors == 0, $"{productMeshes} product vertices, {productErrors} on closed sides");
    }

    private static void CheckTextures()
    {
        // Floor: grout on the tile edges must be darker than the tile
        // itself — otherwise the walkway grid vanishes and the floor is a
        // flat grey wash (its original sin).
        float grout = Math.Min(Lum(StoreTextures.SampleFloor(0.001f, 0.5f)),
                               Lum(StoreTextures.SampleFloor(0.5f, 0.001f)));
        float tileMean = MeanLum(SampleGrid(StoreTextures.SampleFloor));
        Check.That("floor grout is darker than the tiles",
            grout < tileMean, $"grout {grout:F0}, tile mean {tileMean:F0}");

        // Chips must wrap the tile seam: a chip straddling x=0 would tear
        // visibly on every cell boundary, so texels either side of the seam
        // have to agree (beyond per-texel speckle noise).
        float seamDelta = 0f;
        for (int i = 0; i < 8; i++)
        {
            float y = 0.125f + i * 0.11f;
            seamDelta = Math.Max(seamDelta, ChannelDelta(
                StoreTextures.SampleFloor(0.001f, y), StoreTextures.SampleFloor(0.999f, y)));
            float x = 0.125f + i * 0.11f;
            seamDelta = Math.Max(seamDelta, ChannelDelta(
                StoreTextures.SampleFloor(x, 0.001f), StoreTextures.SampleFloor(x, 0.999f)));
        }
        Check.That("floor chips wrap the tile seam",
            seamDelta <= 30f, $"worst cross-seam delta {seamDelta:F0} (speckle allows ~20)");

        // The Unit atlas's white band must be *exactly* white: products,
        // lips and signs UV into it so their vertex colour is what shows.
        // Any tint baked in here would multiply through and muddy every one.
        bool bandPure = true;
        float bandFrom = StoreTextures.WhiteBandStart;
        for (int i = 0; i < 6 && bandPure; i++)
        {
            float y = bandFrom + (1f - bandFrom) * (i + 0.5f) / 6f;
            for (int j = 0; j < 4 && bandPure; j++)
            {
                var c = StoreTextures.SampleUnit(j / 3f * 0.9f + 0.05f, y);
                bandPure = c.R == 255 && c.G == 255 && c.B == 255;
            }
        }
        Check.That("the Unit atlas's white band is pure white", bandPure);

        // ...and the steel below it must be neither white nor flat: white
        // would erase the shelf's texture, flat would read as plastic.
        bool anyWhite = false;
        float steelMin = float.MaxValue, steelMax = float.MinValue;
        for (int i = 0; i < 32; i++)
        {
            float x = (i + 0.5f) / 32f;
            var c = StoreTextures.SampleUnit(x, 0.4f);
            if (c.R == 255 && c.G == 255 && c.B == 255)
                anyWhite = true;
            steelMin = Math.Min(steelMin, Lum(c));
            steelMax = Math.Max(steelMax, Lum(c));
        }
        Check.That("the Unit atlas's steel is textured, not white",
            !anyWhite && steelMax - steelMin >= 5f,
            anyWhite ? "white texel in the steel band" : $"luminance range {steelMax - steelMin:F0}");

        // The wall's baseboard grounds the store's border ring; if it's the
        // same value as the plaster, the border reads as an unpainted slab.
        float baseboard = MeanLum(SampleGrid(StoreTextures.SampleWall, yAt: 0.05f));
        float plaster = MeanLum(SampleGrid(StoreTextures.SampleWall, yAt: 0.5f));
        Check.That("the wall's baseboard is darker than the plaster",
            baseboard < plaster, $"baseboard {baseboard:F0}, plaster {plaster:F0}");
    }

    // ---- Helpers ----------------------------------------------------------

    private static float Lum(Color c) => 0.299f * c.R + 0.587f * c.G + 0.114f * c.B;

    private static float ChannelDelta(Color a, Color b) => Math.Max(
        Math.Abs(a.R - b.R), Math.Max(Math.Abs(a.G - b.G), Math.Abs(a.B - b.B)));

    // Samples a 8x8 grid over the tile interior (clear of the grout edges),
    // either at a fixed y (wall) or across both axes (floor).
    private static Color[] SampleGrid(Func<float, float, Color> sample, float? yAt = null)
    {
        var outColors = new Color[64];
        int k = 0;
        for (int i = 0; i < 8; i++)
        {
            float p = 0.125f + i * 0.11f;
            for (int j = 0; j < 8; j++)
            {
                float q = 0.125f + j * 0.11f;
                outColors[k++] = yAt is float y ? sample(q, y) : sample(p, q);
            }
        }
        return outColors;
    }

    private static float MeanLum(Color[] samples)
    {
        float sum = 0f;
        foreach (var c in samples)
            sum += Lum(c);
        return sum / samples.Length;
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
}
