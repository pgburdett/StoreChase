using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace StoreChase;

// What the HUD needs to know about the world, snapshotted once per frame by
// Game1. Everything here is read-only: the HUD draws the state, it never
// writes it back. Passing a snapshot rather than the subsystems themselves is
// what keeps the HUD from reaching into the game — and what makes it possible
// to test a HUD frame without a GraphicsDevice.
public readonly record struct HudState(
    RunPhase Phase,
    float ElapsedTime,
    string EndMessage,
    int SecuredCount,
    int TotalCount,
    bool PlayerCarrying,
    int StolenCount,
    AlarmCause AlarmCause,
    AlarmStage AlarmStage,
    float AlarmLevel,
    CameraMode ViewMode,
    Matrix ViewProjection,
    IReadOnlyList<Critter> Critters,
    IReadOnlyList<LootItem> Loot,
    GridPos PlayerCell,
    StoreLayout Layout,
    float TitlePhase,

    /// <summary>Seconds since startup, monotonic. Motion on the title screen is
    /// driven by this; <see cref="TitlePhase"/> wraps and would snap back.</summary>
    float TitleTime,

    /// <summary>What's in the power-up pocket, for the "[E] use …" line.</summary>
    PowerUpKind? Pocketed,

    /// <summary>Seconds left of the active speed boost (skates), 0 when none.</summary>
    float BoostSecondsLeft,

    /// <summary>Floating texts to project over the world (the juice layer).</summary>
    IReadOnlyList<FxText> Popups,

    /// <summary>The purse: coins banked this run.</summary>
    int Coins,

    /// <summary>One-based round number ("Round 3").</summary>
    int RoundNumber,

    /// <summary>What R does on the end screen ("new store" or "retry").</summary>
    string RestartHint,

    /// <summary>The shop, in key-slot order; empty while playing.</summary>
    IReadOnlyList<ShopOption> Shop,

    /// <summary>
    /// How audible the player is right now, 0..1 (0 = silent, 1 = as loud as
    /// the store gets). Normalised by Game1 before it gets here; the HUD only
    /// draws the pip.
    /// </summary>
    float Noise,

    /// <summary>Current securing combo (1 = none). Only drawn while playing.</summary>
    int Combo);

// The screen-space overlay: the objective readout, the alarm meter, the
// control hint, the off-screen thief marker and the end-of-round banner.
//
// It owns no game state and makes no decisions. Game1 builds a HudState each
// frame (see Game1.BuildHudState) and hands it over; this class turns it into
// pixels. That boundary is the whole point of the split — the HUD is the one
// part of the frame that is pure output, so it is the part you can reason
// about without holding the whole simulation in your head.
public sealed class Hud
{
    private readonly SpriteBatch _spriteBatch;
    private readonly GraphicsDevice _graphicsDevice;
    private readonly Texture2D _uiPixel;
    private readonly MessageFeed _feed;
    private SpriteFont? _font;

    // The minimap. The store is bigger than the screen, so the player needs
    // a way to see the whole floor plan at once — otherwise the scrolling
    // camera turns a heist into an exploration. Rebuilt by SetLayout when
    // the campaign moves the game to a different store.
    private Texture2D _minimap;
    private readonly int _minimapCellPixels;

    /// <summary>Creates a new HUD with the given graphics resources.</summary>
    /// <param name="spriteBatch">The batch to draw with.</param>
    /// <param name="graphicsDevice">The device, for the viewport and the minimap.</param>
    /// <param name="uiPixel">A 1x1 white texture, for solid rectangles.</param>
    /// <param name="layout">The floor plan to draw the minimap from.</param>
    public Hud(SpriteBatch spriteBatch, GraphicsDevice graphicsDevice, Texture2D uiPixel, StoreLayout layout)
    {
        _spriteBatch = spriteBatch;
        _graphicsDevice = graphicsDevice;
        _uiPixel = uiPixel;
        _feed = new MessageFeed(EventSystem.Instance);
        (_minimap, _minimapCellPixels) = BuildMinimap(graphicsDevice, layout);
    }

    /// <summary>
    /// Rebuilds the minimap for a new floor plan. The campaign changes
    /// store between rounds; the baked texture is the floor plan, so it
    /// has to be re-baked (the old one is dropped, or it leaks one texture
    /// per store visited).
    /// </summary>
    public void SetLayout(StoreLayout layout)
    {
        _minimap.Dispose();
        (_minimap, _) = BuildMinimap(_graphicsDevice, layout);
    }

    /// <summary>
    /// Builds the minimap texture: one pixel per cell, scaled up when drawn.
    ///
    /// Baking it once means the map is not re-rendered every frame, and a
    /// one-pixel-per-cell texture scaled up with nearest-neighbour filtering
    /// is exactly the crisp little plan a minimap wants to be.
    /// </summary>
    private static (Texture2D, int) BuildMinimap(GraphicsDevice device, StoreLayout layout)
    {
        var texture = new Texture2D(device, layout.Width, layout.Height);
        texture.SetData(MinimapPixels(layout));

        // Big enough to read the aisles at a glance, small enough to sit in a
        // corner. Four pixels a cell is about the floor of what is legible.
        const int cellPixels = 4;
        return (texture, cellPixels);
    }

    /// <summary>
    /// The minimap's pixels: one per cell of the floor plan.
    ///
    /// Split out from the texture upload so it can be checked without a
    /// graphics device. The plan is a pure function of the layout, and
    /// getting it wrong would mean a minimap that lies about where the
    /// shelves are.
    /// </summary>
    /// <param name="layout">The floor plan to draw.</param>
    /// <returns>The pixels, row by row, top row first.</returns>
    public static Color[] MinimapPixels(StoreLayout layout)
    {
        var pixels = new Color[layout.Width * layout.Height];

        for (int y = 0; y < layout.Height; y++)
        {
            for (int x = 0; x < layout.Width; x++)
            {
                Color c;
                if (layout.IsCounter(x, y)) c = new Color(226, 176, 64);   // the till
                else if (StoreLayout.IsBlocked(layout.Rows[y][x])) c = new Color(52, 54, 64); // shelves
                else c = new Color(24, 26, 34);                              // floor
                pixels[y * layout.Width + x] = c;
            }
        }

        return pixels;
    }

    /// <summary>
    /// The attract-screen wordmark face. A separate, blocky font loaded from
    /// the content pipeline (Robotron.spritefont); the UI font stays DejaVu so
    /// gameplay text remains legible. Null until LoadContent, so every use has
    /// a fallback.
    /// </summary>
    public SpriteFont? TitleFont { get; set; }

    /// <summary>The UI font, arriving after construction (see <see cref="Font"/>).</summary>
    public SpriteFont? Font
    {
        get => _font;
        set => _font = value;
    }

    /// <summary>Ages the message feed. Called once per frame from Game1's update loop.</summary>
    public void Update(float dt) => _feed.Update(dt);

    /// <summary>Releases the HUD's own graphics resources.</summary>
    public void Dispose() => _minimap.Dispose();

    /// <summary>
    /// Draws the full HUD overlay: rainbow title, objective panel, alarm meter,
    /// control hint, thief marker, end banner, and the message feed.
    /// </summary>
    public void Draw(in HudState state)
    {
        // The font comes from the content pipeline and could in principle
        // fail to load; without it there's no text HUD, but the 3D scene
        // and the alarm bar still draw.
        if (_font == null)
        {
            _graphicsDevice.DepthStencilState = DepthStencilState.Default;
            return;
        }

        // The minimap is drawn in its own pass. A one-pixel-per-cell texture
        // scaled up needs nearest-neighbour filtering to stay crisp, and the
        // sampler state is applied when a batch begins, so it cannot be
        // changed partway through one.
        DrawMinimap(state);

        // SpriteBatch draws are 2D-over-3D, fine as a screen-space overlay.
        _spriteBatch.Begin();

        DrawRainbowTitle(state);
        DrawObjectivePanel(state);
        DrawAlarmMeter(state);
        DrawHoldHint(state);
        DrawThiefMarker(state);
        DrawPopups(state);
        DrawEndBanner(state);

        // The event feed: newest at the bottom, older ones scroll up and off.
        // It sits below the minimap so the two do not overlap.
        _feed.Draw(_spriteBatch, _font,
            new Vector2(_graphicsDevice.Viewport.Width - 220, 40 + state.Layout.Height * _minimapCellPixels + 20));

        _spriteBatch.End();

        // Re-enable depth state for next frame's 3D draw (SpriteBatch changes it).
        _graphicsDevice.DepthStencilState = DepthStencilState.Default;
    }

    /// <summary>
    /// Draws the attract screen: the arcade title.
    ///
    /// Layout is proportional rather than fixed-pixel, because the window is
    /// resizable and a fixed 1200-tall layout silently overlaps itself at any
    /// other height. Everything is expressed as a fraction of the viewport and
    /// clamped, so the same code frames correctly at 800x600 and at 4K.
    ///
    /// Two rules learned the hard way and worth keeping:
    ///  - The wordmark is drawn *below* the marquee band. They used to share
    ///    the same 28 pixels and the title lost.
    ///  - Translucent shapes are drawn with premultiplied colours. The batch
    ///    blends One/InverseSourceAlpha, so a <c>new Color(r,g,b,a)</c> with a
    ///    low alpha renders at full brightness — a "glow" becomes a slab.
    /// </summary>
    public void DrawTitleScreen(in HudState state)
    {
        if (_font == null) return;
        var vp = _graphicsDevice.Viewport;
        int w = vp.Width, h = vp.Height;
        int cx = w / 2;
        float t = state.TitlePhase;
        float time = state.TitleTime;

        // One scale factor for the whole screen, so a smaller window shrinks
        // the layout instead of truncating it.
        float s = MathF.Min(w / 1600f, h / 1200f);
        s = MathHelper.Clamp(s, 0.5f, 2f);

        int barHeight = (int)(34 * s);

        _spriteBatch.Begin();

        // --- The void ----------------------------------------------------
        // Pure black, as the arcade had it. Everything else is a palette
        // colour on top; there is no "dark grey" in this palette.
        _spriteBatch.Draw(_uiPixel, new Rectangle(0, 0, w, h), ArcadePalette.Black);

        // The starfield is confined to the left and right margins, so it can
        // never draw through the wordmark, the prompt or the controls panel.
        // A field that runs under the text reads as dirt on the screen.
        DrawStarfield(state, s, barHeight, w, h);

        // --- Marquee band ------------------------------------------------
        // The ticker keeps its own strip so it can never collide with the
        // wordmark below it.
        _spriteBatch.Draw(_uiPixel, new Rectangle(0, 0, w, barHeight), ArcadePalette.Magenta);
        DrawMarquee(state, s, barHeight);

        // The layout below is a *stack*: each block is placed under the
        // measured height of the one above it, so nothing overlaps at any
        // window size. The old code used two absolute anchors (h*0.60 and
        // h*0.72) which left a dead band in the middle of a tall window.
        float y = barHeight;

        // --- The wordmark ------------------------------------------------
        // STORE CHASE 3D in the arcade face: one solid colour per letter,
        // marching through the palette left to right. It is the arcade
        // colour-cycle, not a rainbow: only palette entries are used, and
        // the whole sequence steps rather than sweeping.
        float titleScale = 2.6f * s;
        y += 44 * s;
        float titleWidth = DrawPaletteWordmark("STORE CHASE 3D", time, cx, y, titleScale);
        y += TitleFont!.LineSpacing * titleScale;

        // Underline: a single bright rule the width of the wordmark, which is
        // what stops a black letter from looking like a rendering bug.
        y += 12 * s;
        int ruleY = (int)y;
        int half = (int)(titleWidth / 2);
        _spriteBatch.Draw(_uiPixel,
            new Rectangle(cx - half, ruleY, half * 2, Math.Max(2, (int)(3 * s))),
            ArcadePalette.Yellow);
        y += Math.Max(2, 3 * s);

        // --- Subtitle -----------------------------------------------------
        const string subtitle = "A HEIST IN EVERY AISLE";
        float subScale = 1.6f * s;
        y += 26 * s;
        var subSize = _font.MeasureString(subtitle) * subScale;
        DrawScaledText(subtitle, cx - subSize.X / 2, y, subScale, ArcadePalette.Cyan);
        y += subSize.Y;

        // --- Prompt -------------------------------------------------------
        // Blinks between yellow and white. Drawn with a dark drop shadow
        // rather than a coloured glow: a glow needs real alpha, and this
        // batch has none to give.
        const string prompt = "PRESS ENTER TO START";
        float promptScale = 1.5f * s;
        var promptSize = _font.MeasureString(prompt) * promptScale;

        // --- Controls -----------------------------------------------------
        // Anchored to the bottom (above the footer), growing upward, so the
        // gap is always between the prompt and the panel rather than after
        // the panel. A panel placed at a fraction of the height floats in
        // the middle of a tall window and touches the footer in a short one.
        string[] hints =
        {
            "WASD  MOVE      MOUSE  LOOK (V FIRST-PERSON)",
            "E  USE     CLICK  THROW     R  RESTART     ESC  QUIT",
            "1-9  SHOP BETWEEN ROUNDS",
        };
        float footerScale = 1.0f * s;
        var footerSize = _font.MeasureString(FooterText) * footerScale;
        float panelBottom = h - barHeight - footerSize.Y - 34 * s;

        // The prompt is centred in the slack between the subtitle and the
        // panel, and the panel is placed *after* it so the two never collide
        // in a short window. Centring the block is what stops a tall window
        // from showing one enormous gap under the prompt.
        float stackBottom = y + promptSize.Y;      // bottom of the subtitle block
        float promptTop = stackBottom + (panelBottom - stackBottom - promptSize.Y) * 0.42f;

        float blink = (MathF.Sin(t * 5f) + 1f) * 0.5f;
        var promptColour = Color.Lerp(ArcadePalette.Yellow, ArcadePalette.White, blink);
        DrawScaledText(prompt, cx - promptSize.X / 2 + 2 * s, promptTop + 2 * s, promptScale, ArcadePalette.Black);
        DrawScaledText(prompt, cx - promptSize.X / 2, promptTop, promptScale, promptColour);

        DrawControlsPanel(hints, cx, panelBottom, s);

        // --- Footer -------------------------------------------------------
        // Matches the marquee's name for the game. The two used to disagree
        // (the marquee said "3D", the footer did not).
        DrawScaledText(FooterText, cx - footerSize.X / 2,
            h - barHeight - footerSize.Y - 14 * s, footerScale,
            ArcadePalette.Dim(ArcadePalette.White, 0.45f));

        // --- Bottom rule --------------------------------------------------
        _spriteBatch.Draw(_uiPixel,
            new Rectangle(0, h - Math.Max(2, (int)(3 * s)), w, Math.Max(2, (int)(3 * s))),
            ArcadePalette.Cyan);

        _spriteBatch.End();
        _graphicsDevice.DepthStencilState = DepthStencilState.Default;
    }

    /// <summary>The footer line. One constant so the marquee and footer cannot
    /// drift apart again — they disagreed about the "3D" once already.</summary>
    private const string FooterText = "(c) 2026 STORE CHASE 3D ARCADE";

    /// <summary>
    /// The starfield. Positions come from a hash of the index, and the whole
    /// field scrolls slowly upward; brightness twinkles on top of that.
    ///
    /// The first version hashed the index to a position and never moved it, so
    /// the "stars" were 90 frozen dots that only pulsed, and the integer
    /// divide that produced the row clumped most of them into a few columns.
    /// A starfield that does not move does not read as a starfield.
    /// </summary>
    private void DrawStarfield(in HudState state, float s, int barHeight, int w, int h)
    {
        // Motion uses the monotonic clock; the twinkle uses the wrapped phase.
        // Mixing the two up is what made the field reset every four seconds.
        float time = state.TitleTime;
        float t = state.TitlePhase;
        int size = Math.Max(1, (int)(2 * s));

        for (int i = 0; i < 110; i++)
        {
            uint n = (uint)(i * 2654435761u + 1013904223u);

            // Across the full width. An earlier pass kept the field out of the
            // centre column so it could not draw through the text, which left
            // an obviously empty stripe down the middle of the screen — worse
            // than the occasional star behind a letter.
            int x = (int)(n % (uint)w);

            // Vertical drift in *pixels per second*, from this star's own
            // start offset. The band is walked at a real speed and wrapped, so
            // a star exits the bottom and re-enters the top seamlessly.
            float speedPx = 12f + (n >> 8 & 15u) * 3.5f;   // 12..64 px/sec
            float offset = (n >> 4 & 255u) / 255f;
            int span = Math.Max(1, h - barHeight);
            float travelled = (offset * span + time * speedPx) % span;
            int y = barHeight + (int)travelled;

            float tw = 0.5f + 0.5f * MathF.Sin(t * 3f + i * 0.7f);
            // Fade near the edges of the band so stars do not pop in and out.
            float edge = MathF.Min(y - barHeight, (barHeight + span) - y) / (span * 0.25f);
            float a = MathHelper.Clamp(edge, 0f, 1f) * (0.25f + 0.75f * tw);

            // Premultiplied: the batch blends One/InverseSourceAlpha, so the
            // colour has to carry the alpha or a dim star is a bright dot.
            byte c = (byte)(255 * a);
            _spriteBatch.Draw(_uiPixel, new Rectangle(x, y, size, size),
                new Color(c, c, c, c));
        }
    }

    /// <summary>
    /// The scrolling ticker. Drawn as text on the marquee band with no
    /// background of its own, repeated enough times to cover any viewport.
    /// The repeat count is computed from the measured string rather than
    /// hard-coded at three copies, which left gaps on a wide window.
    /// </summary>
    private void DrawMarquee(in HudState state, float s, int barHeight)
    {
        const string marquee = "  *  STORE CHASE 3D  *  HEIST IN EVERY AISLE  *  INSERT COIN  *  ";
        var size = _font!.MeasureString(marquee).X;
        if (size <= 1f) return;

        int w = _graphicsDevice.Viewport.Width;
        int copies = (int)(w / size) + 2;
        // Scroll speed is in pixels per second off the monotonic clock.
        // Driving it from TitlePhase made the text lurch: that value wraps
        // four times a second at 0.25Hz, so the offset was a fast sawtooth.
        float offset = (state.TitleTime * 140f * s) % size;

        var colour = ArcadePalette.Black;
        for (int k = 0; k < copies; k++)
        {
            float x = k * size - offset;
            _spriteBatch.DrawString(_font, marquee, new Vector2(x, barHeight * 0.18f), colour);
        }
    }

    /// <summary>
    /// Draws the wordmark one glyph at a time, each letter taking the next
    /// palette colour and every letter sitting on the same baseline.
    ///
    /// The colours *march*: every <see cref="TitleStepSeconds"/> the whole
    /// sequence shifts one place, so a letter that was white becomes blue and
    /// the wordmark reads as a running light. The first version advanced the
    /// index by <c>(int)phase</c> where phase is 0..1, which is zero almost
    /// always — the colours looked frozen.
    /// </summary>
    /// <returns>The width of the drawn wordmark, for the rule underneath it.</returns>
    private float DrawPaletteWordmark(string text, float time, float centerX, float top, float scale)
    {
        var font = TitleFont ?? _font!;
        int step = (int)(time / TitleStepSeconds);
        int count = ArcadePalette.TitleLetters.Length;

        float advance = 0f;
        for (int i = 0; i < text.Length; i++)
            advance += font.MeasureString(text[i].ToString()).X * scale;

        float x = centerX - advance / 2f;
        for (int i = 0; i < text.Length; i++)
        {
            string glyph = text[i].ToString();
            var colour = ArcadePalette.TitleLetters[(i + step) % count];

            // A dark copy under each glyph. On black it is invisible, but it
            // separates two adjacent letters of the same colour.
            _spriteBatch.DrawString(font, glyph, new Vector2(x + 2f, top + 2f), ArcadePalette.Black,
                0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
            _spriteBatch.DrawString(font, glyph, new Vector2(x, top), colour,
                0f, Vector2.Zero, scale, SpriteEffects.None, 0f);

            x += font.MeasureString(glyph).X * scale;
        }

        return advance;
    }

    /// <summary>Seconds between colour steps on the wordmark. Slow enough to
    /// read as a deliberate march, fast enough to notice.</summary>
    private const float TitleStepSeconds = 0.35f;

    /// <summary>
    /// The controls panel: a measured, centred box with a palette outline.
    /// Sized from the longest line rather than a fixed 840 pixels, which ran
    /// the text past its own border on a smaller window.
    /// </summary>
    /// <param name="bottom">The y the panel's lower edge sits on; it grows upward.</param>
    /// <returns>The y of the panel's top edge, so the caller can space around it.</returns>
    private float DrawControlsPanel(string[] lines, int cx, float bottom, float s)
    {
        var font = _font!;
        float widest = 0f;
        foreach (var line in lines) widest = MathF.Max(widest, font.MeasureString(line).X * s);

        float pad = 18 * s;
        float lineHeight = font.LineSpacing * s + 6 * s;
        float panelW = widest + pad * 2;
        float panelH = lines.Length * lineHeight + pad * 2;
        var panel = new Rectangle(
            (int)(cx - panelW / 2), (int)(bottom - panelH), (int)panelW, (int)panelH);

        _spriteBatch.Draw(_uiPixel, panel, ArcadePalette.Black);
        DrawPanelBorder(panel, ArcadePalette.Blue, Math.Max(1, (int)(2 * s)));

        float y = panel.Y + pad;
        foreach (var line in lines)
        {
            float lineW = font.MeasureString(line).X * s;
            _spriteBatch.DrawString(font, line, new Vector2(panel.X + (panel.Width - lineW) / 2f, y),
                ArcadePalette.White, 0f, Vector2.Zero, s, SpriteEffects.None, 0f);
            y += lineHeight;
        }

        return panel.Y;
    }

    /// <summary>Outline for a panel, drawn as four rules so it stays crisp at any scale.</summary>
    private void DrawPanelBorder(Rectangle r, Color colour, int thickness)
    {
        _spriteBatch.Draw(_uiPixel, new Rectangle(r.X, r.Y, r.Width, thickness), colour);
        _spriteBatch.Draw(_uiPixel, new Rectangle(r.X, r.Bottom - thickness, r.Width, thickness), colour);
        _spriteBatch.Draw(_uiPixel, new Rectangle(r.X, r.Y, thickness, r.Height), colour);
        _spriteBatch.Draw(_uiPixel, new Rectangle(r.Right - thickness, r.Y, thickness, r.Height), colour);
    }

    /// <summary>Draws text at a scale, which DrawString's overloads do not do conveniently.</summary>
    private void DrawScaledText(string text, float x, float y, float scale, Color colour) =>
        _spriteBatch.DrawString(_font!, text, new Vector2(x, y), colour,
            0f, Vector2.Zero, scale, SpriteEffects.None, 0f);

    // The objective readout: how much is back, how much is still out, and
    // the timer. This is the whole scoreboard, so it stays in one place.
    private void DrawObjectivePanel(in HudState state)
    {
        var font = _font!;
        int secured = state.SecuredCount;
        int total = state.TotalCount;
        int carried = state.PlayerCarrying ? 1 : 0;
        int loose = Math.Max(0, total - secured - carried - state.StolenCount);

        var line = new Vector2(16, 40);
        const int step = 22;

        _spriteBatch.DrawString(font, $"Round {state.RoundNumber}   {state.Coins}c", line,
            new Color(245, 205, 110));
        line.Y += step;
        _spriteBatch.DrawString(font, $"Time  {state.ElapsedTime:F1}s", line, Color.White);
        line.Y += step;
        _spriteBatch.DrawString(font, $"Behind the counter  {secured}/{total}",
            line, secured == total ? Color.LimeGreen : Color.White);
        line.Y += step;

        if (state.PlayerCarrying)
        {
            _spriteBatch.DrawString(font, "You're carrying something - get to the counter!",
                line, new Color(240, 200, 90));
        }
        else if (state.StolenCount > 0)
        {
            _spriteBatch.DrawString(font, $"On the move: {state.StolenCount}", line, new Color(240, 140, 120));
        }
        else if (loose > 0)
        {
            _spriteBatch.DrawString(font, $"{loose} still on the shelves", line, Color.LightGray);
        }

        // Species list, so the player can tell who's who.
        line.Y += step * 2;
        _spriteBatch.DrawString(font, "In the store:", line, Color.DimGray);
        line.Y += step;
        foreach (var critter in state.Critters)
        {
            string displayName = string.IsNullOrEmpty(critter.Name) ? critter.Profile.Name : critter.Name;
            string flag = critter.Befriended ? "  [friend]"
                : critter.Carrying ? "  [!] carrying" : "";
            _spriteBatch.DrawString(font, $"  {displayName}{flag}",
                line, critter.Profile.Tint);
            line.Y += step;
        }

        // Each species' taste, so the player knows which goods to watch.
        line.Y += step;
        _spriteBatch.DrawString(font, "After:", line, Color.DimGray);
        line.Y += step;
        foreach (var critter in state.Critters)
        {
            _spriteBatch.DrawString(font,
                $"  {critter.Profile.Name} wants {KindName(critter.Profile.PreferredKind)}",
                line, Color.DimGray);
            line.Y += step;
        }

        // The goods still on the shelves, with what each is worth. This is
        // the read that makes the loot's kinds matter: the player can see
        // which items are worth a panic and which are not.
        line.Y += step;
        _spriteBatch.DrawString(font, "On the shelves:", line, Color.DimGray);
        line.Y += step;
        foreach (var item in state.Loot)
        {
            string where = item.IsSecured ? "counter"
                : item.State == LootState.Carried ? "STOLEN"
                : item.State == LootState.Held ? "in hand"
                : "shelf";
            _spriteBatch.DrawString(font, $"  {KindName(item.Kind)} ({where})",
                line, item.Tint);
            line.Y += step;
        }

        string view = state.ViewMode == CameraMode.FirstPerson ? "first-person" : "isometric";
        _spriteBatch.DrawString(font, $"[V] view: {view}", new Vector2(16, _graphicsDevice.Viewport.Height - 60), Color.LightGray);
    }

    /// <summary>The display name of a kind of goods.</summary>
    private static string KindName(LootKind kind) => kind switch
    {
        LootKind.Canned => "cans",
        LootKind.Boxed => "cartons",
        _ => "bags",
    };

    /// <summary>
    /// The minimap: the whole floor plan, with the player and the cats on it.
    ///
    /// The store is bigger than the screen, so the camera can only ever show
    /// a slice of it. The minimap is how the player knows where they are in
    /// the whole plan, which is the difference between a heist and a
    /// treasure hunt.
    ///
    /// It sits in the top-right corner, under the message feed.
    /// </summary>
    private void DrawMinimap(in HudState state)
    {
        var layout = state.Layout;
        int cell = _minimapCellPixels;
        int width = layout.Width * cell;
        int height = layout.Height * cell;

        int x = _graphicsDevice.Viewport.Width - width - 16;
        int y = 40;

        // Its own batch, so the nearest-neighbour sampling applies to the
        // scaled-up plan. The sampler state is bound when a batch begins.
        var previous = _graphicsDevice.SamplerStates[0];
        _spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend,
            SamplerState.PointClamp, null, null, null, null);

        // The plan itself.
        _spriteBatch.Draw(_minimap, new Rectangle(x, y, width, height), Color.White);

        // The player: a bright dot, so it is never lost against the shelves.
        var player = new Rectangle(
            x + state.PlayerCell.X * cell + cell / 4,
            y + state.PlayerCell.Y * cell + cell / 4,
            Math.Max(2, cell / 2), Math.Max(2, cell / 2));
        _spriteBatch.Draw(_uiPixel, player, new Color(120, 220, 255));

        // The cats, in their own colour. A cat carrying something is drawn
        // brighter, because that is the one the player cares about.
        foreach (var critter in state.Critters)
        {
            var c = critter.Profile.Tint;
            if (critter.Carrying) c = Color.Lerp(c, Color.White, 0.55f);

            _spriteBatch.Draw(_uiPixel, new Rectangle(
                x + critter.Cell.X * cell,
                y + critter.Cell.Y * cell,
                cell, cell), c);
        }

        // A frame, so the map reads as a panel rather than floating.
        _spriteBatch.Draw(_uiPixel, new Rectangle(x - 2, y - 2, width + 4, 2), new Color(70, 72, 82));
        _spriteBatch.Draw(_uiPixel, new Rectangle(x - 2, y + height, width + 4, 2), new Color(70, 72, 82));
        _spriteBatch.Draw(_uiPixel, new Rectangle(x - 2, y - 2, 2, height + 4), new Color(70, 72, 82));
        _spriteBatch.Draw(_uiPixel, new Rectangle(x + width, y - 2, 2, height + 4), new Color(70, 72, 82));

        _spriteBatch.End();
        _graphicsDevice.SamplerStates[0] = previous;
    }

    // The alarm: a bar that fills as the meter rises, tinted by cause so
    // you can tell at a glance whether the cat or you are the problem.
    private void DrawAlarmMeter(in HudState state)
    {
        const int width = 260, height = 16;
        int x = (_graphicsDevice.Viewport.Width - width) / 2;
        int y = _graphicsDevice.Viewport.Height - 44;

        var fill = state.AlarmCause switch
        {
            AlarmCause.Thief => ArcadePalette.Thief,
            AlarmCause.PlayerCarrying => ArcadePalette.Yellow,
            _ => ArcadePalette.Green,
        };

        _spriteBatch.Draw(_uiPixel, new Rectangle(x, y, width, height), ArcadePalette.Dim(ArcadePalette.White, 0.88f));
        _spriteBatch.Draw(_uiPixel,
            new Rectangle(x, y, (int)(width * state.AlarmLevel), height), fill);
        _spriteBatch.Draw(_uiPixel, new Rectangle(x, y, width, 2), ArcadePalette.Dim(ArcadePalette.White, 0.62f));

        string label = state.AlarmCause switch
        {
            AlarmCause.Thief => "ALARM - something's being carried off!",
            AlarmCause.PlayerCarrying => "ALARM - you're carrying it",
            _ => "Store alarm",
        };

        // The stage is the escalating part: past the critical threshold the
        // store is about to go off, and that has to be unmissable.
        if (state.AlarmStage == AlarmStage.Critical)
            label = "ALARM - THE STORE IS ABOUT TO GO OFF";

        _spriteBatch.DrawString(_font!, label, new Vector2(x, y - 22), fill);

        // A second line naming the band, so the escalation is legible even
        // when the meter is not filling.
        string stage = state.AlarmStage switch
        {
            AlarmStage.Critical => "critical",
            AlarmStage.Suspicious => "suspicious",
            _ => "calm",
        };
        _spriteBatch.DrawString(_font!, stage, new Vector2(x + width, y - 22), fill);

        // The noise pip: how audible the player is right now. Silent is the
        // stealth state, so it only appears once there is something to hear —
        // a walking player gets a warm bar, a skidding one burns red.
        if (state.Noise > 0.01f)
        {
            const int pipWidth = 60, pipHeight = 6;
            int pipY = y + height + 5;
            var pipFill = Color.Lerp(new Color(210, 176, 96), new Color(230, 90, 60),
                Math.Clamp(state.Noise, 0f, 1f));
            _spriteBatch.Draw(_uiPixel, new Rectangle(x, pipY, pipWidth, pipHeight),
                new Color(30, 30, 38));
            _spriteBatch.Draw(_uiPixel,
                new Rectangle(x, pipY, (int)(pipWidth * state.Noise), pipHeight), pipFill);
            _spriteBatch.DrawString(_font!, "noise",
                new Vector2(x + pipWidth + 8, pipY - 5), pipFill * 0.9f);
        }
    }

    // One contextual line of controls, so the keybinds that only matter at
    // the moment they're useful aren't buried in the README.
    private void DrawHoldHint(in HudState state)
    {
        if (state.Phase != RunPhase.Playing)
        {
            _spriteBatch.DrawString(_font!, "[R] new round", new Vector2(16, _graphicsDevice.Viewport.Height - 34), Color.White);
            return;
        }

        // The power-up line sits above the view hint: what you're carrying,
        // and how long the skates have left if they're on your feet.
        string pocket = state.Pocketed is { } kind ? $"[E] use {PowerUpItem.Name(kind)}" : "";
        if (state.BoostSecondsLeft > 0f)
            pocket += (pocket.Length > 0 ? "    " : "") + $"skates {state.BoostSecondsLeft:F1}s";
        if (pocket.Length > 0)
            _spriteBatch.DrawString(_font!, pocket,
                new Vector2(16, _graphicsDevice.Viewport.Height - 84), new Color(140, 200, 240));

        string hint = state.ViewMode == CameraMode.FirstPerson
            ? "[click] throw a ball    [V] overhead"
            : "[V] first-person (to aim throws & pin nervous cats)";
        _spriteBatch.DrawString(_font!, hint, new Vector2(16, _graphicsDevice.Viewport.Height - 34), Color.Gray);
    }

    /// <summary>
    /// Marks whichever critter is currently carrying loot, in screen space.
    ///
    /// The alarm is a ~24 second fuse and the store is a shelf maze, so
    /// without a marker the last thirty seconds of a lost round is spent
    /// hunting for a cat you can't see. When the thief is on screen this is
    /// a ring around it; when it isn't, it's an arrow pinned to the edge of
    /// the viewport pointing the way to turn.
    /// </summary>
    private void DrawThiefMarker(in HudState state)
    {
        var thief = state.Critters.FirstOrDefault(c => c.Carrying);
        if (thief == null) return;

        var viewport = _graphicsDevice.Viewport;
        Vector3 target = thief.WorldPos + new Vector3(0f, 1.4f, 0f);

        // Project with the same row-vector convention the shader uses:
        // mul(pos, View) then mul(..., Projection). The homogeneous divide
        // by w is left to the caller.
        Vector4 clip = Vector4.Transform(new Vector4(target, 1f), state.ViewProjection);

        // Behind the camera, or past the far plane: w <= 0 and the point is
        // on the wrong side of the eye, so there's no direction to point.
        if (clip.W <= 0.0001f) return;

        var ndc = new Vector2(clip.X / clip.W, clip.Y / clip.W);
        var projected = new Vector2(
            (ndc.X * 0.5f + 0.5f) * viewport.Width,
            (0.5f - ndc.Y * 0.5f) * viewport.Height);
        bool onScreen = projected.X >= 0 && projected.Y >= 0 &&
                        projected.X < viewport.Width && projected.Y < viewport.Height;

        if (onScreen)
        {
            // A ring of pixels around the cat, so it reads over any floor.
            int cx = (int)projected.X, cy = (int)projected.Y;
            for (int a = 0; a < 360; a += 6)
            {
                float r = MathHelper.ToRadians(a);
                _spriteBatch.Draw(_uiPixel,
                    new Rectangle(cx + (int)(MathF.Cos(r) * 18) - 1, cy + (int)(MathF.Sin(r) * 18) - 1, 3, 3),
                    ArcadePalette.Thief);
            }
        }
        else
        {
            // Clamp the direction to the viewport border. The sign of the
            // off-screen components is what points the right way.
            var from = new Vector2(viewport.Width / 2f, viewport.Height / 2f);
            Vector2 dir = projected - from;
            if (dir.LengthSquared() < 0.0001f) return;
            dir = Vector2.Normalize(dir);

            // Walk the ray until it leaves the box, so the marker sits on
            // the edge however the aspect compares.
            float tx = dir.X != 0f ? (viewport.Width / 2f) / MathF.Abs(dir.X) : float.MaxValue;
            float ty = dir.Y != 0f ? (viewport.Height / 2f) / MathF.Abs(dir.Y) : float.MaxValue;
            float t = MathF.Min(tx, ty) * 0.88f;
            var edge = from + dir * t;

            _spriteBatch.Draw(_uiPixel,
                new Rectangle((int)edge.X - 5, (int)edge.Y - 5, 10, 10),
                ArcadePalette.Thief);
        }

            // The species name, so the player learns which cat is the trouble.
        _spriteBatch.DrawString(_font!, thief.Profile.Name,
            new Vector2(projected.X - 40, projected.Y + 24), ArcadePalette.Thief);
    }

    // Floating texts: "+1 secured", "SLIP!", the cost of the fire alarm.
    // Projected with the same row-vector convention as the thief marker;
    // FxSystem has already risen and faded them, so all that's left here is
    // where and how strongly to draw them.
    private void DrawPopups(in HudState state)
    {
        if (state.Popups.Count == 0) return;

        var viewport = _graphicsDevice.Viewport;

        foreach (var popup in state.Popups)
        {
            Vector4 clip = Vector4.Transform(new Vector4(popup.WorldPos, 1f), state.ViewProjection);
            if (clip.W <= 0.0001f) continue;

            var ndc = new Vector2(clip.X / clip.W, clip.Y / clip.W);
            var screen = new Vector2(
                (ndc.X * 0.5f + 0.5f) * viewport.Width,
                (0.5f - ndc.Y * 0.5f) * viewport.Height);

            if (screen.X < -60 || screen.Y < -60 ||
                screen.X > viewport.Width + 60 || screen.Y > viewport.Height + 60) continue;

            var size = _font!.MeasureString(popup.Text);
            // Lifted and lightened as it fades: the tail of a "+1" arc.
            _spriteBatch.DrawString(_font, popup.Text,
                screen - size / 2f, popup.Color * popup.Strength);
        }
    }

    private void DrawEndBanner(in HudState state)
    {
        if (state.Phase == RunPhase.Playing) return;

        int w = _graphicsDevice.Viewport.Width, h = _graphicsDevice.Viewport.Height;
        _spriteBatch.Draw(_uiPixel, new Rectangle(0, h / 2 - 70, w, 140), new Color(12, 12, 18));

        var colour = state.Phase == RunPhase.Won ? Color.LimeGreen : Color.OrangeRed;
        string title = state.Phase == RunPhase.Won ? "STORE SECURED" : "SHOPLIFTING SPOTTED";
        _spriteBatch.DrawString(_font!, title,
            new Vector2((w - _font!.MeasureString(title).X) / 2f, h / 2f - 40), colour);
        _spriteBatch.DrawString(_font!, state.EndMessage,
            new Vector2((w - _font.MeasureString(state.EndMessage).X) / 2f, h / 2f - 8), Color.White);
        _spriteBatch.DrawString(_font!, $"Recovered {state.SecuredCount}/{state.TotalCount} in {state.ElapsedTime:F1}s",
            new Vector2((w - _font.MeasureString("Recovered").X) / 2f, h / 2f + 16), Color.LightGray);
        _spriteBatch.DrawString(_font!, state.RestartHint,
            new Vector2((w - _font.MeasureString(state.RestartHint).X) / 2f, h / 2f + 44),
            new Color(255, 220, 130));

        DrawShopPanel(state, h);
    }

    // The between-rounds shop. Six slots, drawn in the same order the number
    // keys buy in — that correspondence is the whole interface, so the
    // catalog order is the display order with nothing re-sorted. Affordable
    // rows glow gold; short rows grey out (you can still buy nothing); a
    // maxed permanent says so rather than showing a price it won't take.
    private void DrawShopPanel(in HudState state, int viewportHeight)
    {
        if (state.Shop.Count == 0) return;

        var font = _font!;
        int y = viewportHeight / 2 + 96;

        string header = $"SHOP   {state.Coins}c";
        _spriteBatch.DrawString(font, header,
            new Vector2((_graphicsDevice.Viewport.Width - font.MeasureString(header).X) / 2f, y),
            new Color(245, 205, 110));
        y += 26;

        // Two columns of three: the consumables left, the permanents right.
        // Six single-column rows would run off the bottom of a 1200px window
        // once the banner and hint are accounted for.
        const int colWidth = 340;
        int leftX = (_graphicsDevice.Viewport.Width - colWidth * 3) / 2;

        for (int i = 0; i < state.Shop.Count; i++)
        {
            var option = state.Shop[i];
            int col = i / 3;
            int row = i % 3;
            var at = new Vector2(leftX + col * colWidth, y + row * 22);

            // Tier display: permanents show "II/III" once owned, consumables
            // show how many are queued in the pocket queue.
            string tier = option.Consumable
                ? ""
                : option.Tier > 0
                    ? $" {Roman(option.Tier)}/{Roman(option.MaxTier)}"
                    : "";

            string cost = option.Tier >= option.MaxTier && !option.Consumable
                ? "MAXED"
                : $"{option.Cost}c";

            string label = $"[{i + 1}] {option.Name}{tier}   {cost}";
            var colour = option.Tier >= option.MaxTier && !option.Consumable
                ? new Color(140, 220, 140)                       // maxed: done, not dimmed
                : option.CanAfford
                    ? new Color(255, 235, 160)                   // buyable: gold
                    : new Color(120, 120, 130);                  // short: dimmed

            _spriteBatch.DrawString(font, label, at, colour);
        }
    }

    // I/II/III — enough for three tiers, which is all the shop sells. A
    // number would do, but "Fast shoes II/III" is a shop, and shops have
    // Roman numerals.
    private static string Roman(int n) => n switch
    {
        1 => "I",
        2 => "II",
        3 => "III",
        _ => n.ToString(),
    };

    // "Store Chase" centered at the top of the screen. The base hue comes
    // from RainbowTitle's background thread; spreading the hue per
    // character produces the rainbow gradient. Only the read/draw happens
    // on the game thread, since the graphics device isn't thread-safe.
    private void DrawRainbowTitle(in HudState state)
    {
        const float hueStep = 0.07f; // rainbow spread across the glyphs
        const float y = 6f;

        float phase = state.TitlePhase;
        float cursorX = (_graphicsDevice.Viewport.Width - _font!.MeasureString(RainbowTitle.Text).X) / 2f;

        for (int i = 0; i < RainbowTitle.Text.Length; i++)
        {
            string glyph = RainbowTitle.Text[i].ToString();
            _spriteBatch.DrawString(_font, glyph, new Vector2(cursorX, y),
                RainbowTitle.FromHue(phase + i * hueStep));
            cursorX += _font.MeasureString(glyph).X;
        }
    }
}
