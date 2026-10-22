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
    CameraViewMode ViewMode,
    Matrix ViewProjection,
    IReadOnlyList<Critter> Critters,
    IReadOnlyList<LootItem> Loot,
    GridPos PlayerCell,
    StoreLayout Layout,
    float TitlePhase);

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
    // camera turns a heist into an exploration.
    private readonly Texture2D _minimap;
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
    /// The font is loaded from the content pipeline after the HUD is
    /// constructed (LoadContent runs after Initialize), so it arrives
    /// separately rather than through the constructor.
    /// </summary>
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
        DrawEndBanner(state);

        // The event feed: newest at the bottom, older ones scroll up and off.
        // It sits below the minimap so the two do not overlap.
        _feed.Draw(_spriteBatch, _font,
            new Vector2(_graphicsDevice.Viewport.Width - 220, 40 + state.Layout.Height * _minimapCellPixels + 20));

        _spriteBatch.End();

        // Re-enable depth state for next frame's 3D draw (SpriteBatch changes it).
        _graphicsDevice.DepthStencilState = DepthStencilState.Default;
    }

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
            string flag = critter.Carrying ? "  [!] carrying" : "";
            _spriteBatch.DrawString(font, $"  {critter.Profile.Name}{flag}",
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

        string view = state.ViewMode == CameraViewMode.FirstPerson ? "first-person" : "isometric";
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
            AlarmCause.Thief => new Color(226, 84, 66),
            AlarmCause.PlayerCarrying => new Color(230, 176, 64),
            _ => new Color(96, 176, 112),
        };

        _spriteBatch.Draw(_uiPixel, new Rectangle(x, y, width, height), new Color(30, 30, 38));
        _spriteBatch.Draw(_uiPixel,
            new Rectangle(x, y, (int)(width * state.AlarmLevel), height), fill);
        _spriteBatch.Draw(_uiPixel, new Rectangle(x, y, width, 2), new Color(90, 90, 100));

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

        string hint = state.ViewMode == CameraViewMode.FirstPerson
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
                    new Color(226, 84, 66));
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
                new Color(226, 84, 66));
        }

        // The species name, so the player learns which cat is the trouble.
        _spriteBatch.DrawString(_font!, thief.Profile.Name,
            new Vector2(projected.X - 40, projected.Y + 24), new Color(226, 84, 66));
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
    }

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
