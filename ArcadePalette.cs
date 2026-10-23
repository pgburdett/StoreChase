using Microsoft.Xna.Framework;

namespace StoreChase;

// The game's colour set, taken from Robotron: 2084's attract screen (the
// Williams arcade board drew everything from a small fixed palette, and the
// title screen used exactly these entries).
//
// One rule for the whole project: every colour a surface, sprite, effect or
// piece of UI draws comes from here. The arcade look is not "dark theme" —
// it is a *restricted* palette on black. A new colour mixed on the spot is
// the thing that makes a game look like a modern game wearing a retro hat.
//
// Naming is by role rather than by hue (Neon.Primary rather than Neon.Magenta)
// so a surface picks the colour that means what it is doing: loot "value",
// alarm "critical", player "goal". Where two roles need the same colour that
// is deliberate — the palette is only nine entries deep.
public static class ArcadePalette
{
    // --- The nine entries, exactly as they appear in the source material -----

    /// <summary>Black. The void behind everything; the arcade screen's border.</summary>
    public static readonly Color Black = new(0, 0, 0);

    /// <summary>Pure white. Text, highlights, the player's own body.</summary>
    public static readonly Color White = new(255, 255, 255);

    /// <summary>Yellow. Coins, score, "attention" — the brightest non-white.</summary>
    public static readonly Color Yellow = new(255, 255, 0);

    /// <summary>Red. Danger: the alarm at critical, hostile cats, security cameras.</summary>
    public static readonly Color Red = new(255, 0, 0);

    /// <summary>Green. Safety: the alarm at rest, calm, purchasable, the counter.</summary>
    public static readonly Color Green = new(0, 217, 0);

    /// <summary>Blue. Information: panels, walls, the store's structure.</summary>
    public static readonly Color Blue = new(0, 0, 255);

    /// <summary>Magenta. Oddities and pickups: power-ups, the shop's rare stock.</summary>
    public static readonly Color Magenta = new(255, 0, 255);

    /// <summary>Orange. Warm accents: shelves' merchandise, hazards, copper.</summary>
    public static readonly Color Orange = new(255, 118, 0);

    /// <summary>Deep orange. A second warm shade so shelves do not read as one flat colour.</summary>
    public static readonly Color OrangeDeep = new(255, 81, 0);

    /// <summary>
    /// Cyan. The source machine's tenth entry, used sparingly for "structural"
    /// accents — the marquee rule, panel headings — where blue is too dark to
    /// read against black.
    /// </summary>
    public static readonly Color Cyan = new(104, 255, 222);

    // --- Aliases, so surfaces name the role and not the hue ------------------
    //
    // These are the same Color values; the names exist so a reader of a draw
    // call can tell *why* a colour was chosen. Value equal, meaning different.

    /// <summary>Neutral UI text that is not asking for attention.</summary>
    public static readonly Color Text = White;

    /// <summary>Secondary UI text: hints, labels, the controls line.</summary>
    public static readonly Color TextDim = Blue;

    /// <summary>Panel backgrounds. Not black, so panels separate from the void.</summary>
    public static readonly Color Panel = Black;

    /// <summary>Panel and box outlines.</summary>
    public static readonly Color Border = Blue;

    /// <summary>The player. White so they are the brightest thing on the floor.</summary>
    public static readonly Color Player = White;

    /// <summary>Loose loot on the floor.</summary>
    public static readonly Color Loot = Yellow;

    /// <summary>A cat that has stolen something and is running for the door.</summary>
    public static readonly Color Thief = Red;

    /// <summary>A harmless shopper.</summary>
    public static readonly Color Customer = OrangeDeep;

    /// <summary>The counter — where loot is banked. Green reads as "safe".</summary>
    public static readonly Color Counter = Green;

    /// <summary>Store walls and the shelf carcass.</summary>
    public static readonly Color Structure = Blue;

    /// <summary>Shelf merchandise, and the same colour used for hazards.</summary>
    public static readonly Color Goods = Orange;

    // --- Shades ---------------------------------------------------------------
    //
    // The arcade did not have alpha, so "dark" was a different palette entry.
    // These are the same hues taken toward black, which is how the original
    // screen produced its dim greens and reds. Use them instead of drawing a
    // bright colour at low opacity: a translucent neon over black reads as
    // washed-out grey, while a *dark* neon over black stays saturated.

    /// <summary>Take a palette colour toward black by <paramref name="amount"/> (0..1).</summary>
    public static Color Dim(Color c, float amount)
    {
        float k = 1f - MathHelper.Clamp(amount, 0f, 1f);
        return new Color((byte)(c.R * k), (byte)(c.G * k), (byte)(c.B * k));
    }

    /// <summary>The palette entry nearest to an arbitrary colour. Used by the
    /// content generators so a hand-mixed surface colour gets snapped back
    /// into the palette instead of quietly becoming an eighteenth colour.</summary>
    public static Color Nearest(Color c)
    {
        Color best = Entries[0];
        int bestDistance = int.MaxValue;
        foreach (var entry in Entries)
        {
            int dr = entry.R - c.R;
            int dg = entry.G - c.G;
            int db = entry.B - c.B;
            // Weight green a little higher: it dominates perceived brightness,
            // so an unweighted match snaps mid-greys to the wrong entry.
            int distance = (dr * dr * 2) + (dg * dg * 4) + (db * db * 3);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = entry;
            }
        }
        return best;
    }

    /// <summary>Every palette entry, in the order the attract screen introduced them.</summary>
    public static readonly Color[] Entries =
    {
        Black, White, Yellow, Red, Green, Blue, Magenta, Orange, OrangeDeep, Cyan,
    };

    /// <summary>
    /// The attract-screen letter colours, left to right. The arcade wordmark
    /// ran white/blue/yellow/orange/magenta/green and dropped one letter to
    /// black so the eye completes the shape — the black letter is the trick
    /// that makes the title look like a light show rather than a paint chart.
    /// </summary>
    public static readonly Color[] TitleLetters =
    {
        White, Blue, Yellow, Orange, Magenta, Green,
    };
}
