using System.IO;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace StoreChase
{
    public class Game1 : Game
    {
        private GraphicsDeviceManager _graphics;
        private SpriteBatch _spriteBatch = null!;
        private SpriteFont? _font;

        private const float CellSize = 2f; // world units per grid cell

        private GridMap _map = null!;
        private Player _player = null!;
        private Critter _critter = null!;

        private BasicEffect _effect = null!;
        private Camera _camera = null!;
        private InputSystem _input = null!;
        private RainbowTitle _title = null!;

        // Cube/floor meshes, built once and reused (just repositioned via world matrix).
        private VertexPositionNormalColor[] _wallVerts = null!;
        private VertexPositionNormalColor[] _floorVerts = null!;
        private VertexPositionNormalColor[] _playerVerts = null!;
        private Texture2D _uiPixel = null!;

        // The cat replaces the procedural critter cube. It needs its own
        // effect (textured, not vertex-colored) and a local transform to
        // normalize the raw model into the game's world scale/orientation —
        // see BuildCritterModelTransform for why each piece is there.
        private StaticMesh? _catMesh;
        private BasicEffect _catEffect = null!;
        private Matrix _critterModelTransform = Matrix.Identity;

        private bool _won = false;
        private float _elapsedTime = 0f;

        public Game1()
        {
            _graphics = new GraphicsDeviceManager(this);
            Content.RootDirectory = "Content";
            IsMouseVisible = true;
        }

        protected override void Initialize()
        {
            _map = new GridMap();

            _graphics.PreferredBackBufferWidth = 1600;
            _graphics.PreferredBackBufferHeight = 1200;
            _graphics.ApplyChanges();

            _player = new Player(new GridPos(1, 1), CellSize);
            _critter = new Critter(new GridPos(_map.Width - 2, _map.Height - 2), CellSize, new SmartAnimalAI());

            _input = new InputSystem(new KeyboardMouseInput(), new GamePadInput());

            _title = new RainbowTitle();

            SetupCamera();

            new RunStats(EventSystem.Instance);
            EventSystem.Instance.Publish(new GameStartedEvent());

            base.Initialize();
        }

        private void SetupCamera()
        {
            _camera = new Camera(GraphicsDevice, _map.Width * CellSize, _map.Height * CellSize, CellSize);
        }

        protected override void LoadContent()
        {
            _spriteBatch = new SpriteBatch(GraphicsDevice);

            _effect = new BasicEffect(GraphicsDevice)
            {
                VertexColorEnabled = true,
                LightingEnabled = true,
                
            };
            _effect.EnableDefaultLighting();
            // Default lighting comes from an angle; for a top-down view we mostly
            // care about the top faces reading brightly, which EnableDefaultLighting
            // already handles reasonably well.

            _wallVerts = CubeMesh.Build(new Color(90, 60, 40));       // shelf brown
            _playerVerts = CubeMesh.Build(Color.DodgerBlue);
            _floorVerts = CubeMesh.BuildFloorQuad(new Color(50, 50, 55), _map.Width * CellSize, _map.Height * CellSize);

            _uiPixel = new Texture2D(GraphicsDevice, 1, 1);
            _uiPixel.SetData(new[] { Color.White });

            LoadCatModel();

            // Status/timer text is rendered from this font (see DrawUiOverlay).
            _font = Content.Load<SpriteFont>("DefaultFont");
        }

        // Loads the cat OBJ/MTL/textures from Content/Models at runtime
        // (not through the MGCB content pipeline — see ObjLoader.cs for
        // why) and works out a local transform that fits it into one grid
        // cell, standing upright, facing a known direction.
        private void LoadCatModel()
        {
            string modelPath = Path.Combine(Content.RootDirectory, "Models", "Cat.obj");
            _catMesh = ObjLoader.Load(GraphicsDevice, modelPath);

            _catEffect = new BasicEffect(GraphicsDevice)
            {
                TextureEnabled = true,
                Texture = _catMesh.DiffuseTexture,
                LightingEnabled = true
                
            };
            _catEffect.EnableDefaultLighting();

            _critterModelTransform = BuildCritterModelTransform(_catMesh);
        }

        // The raw OBJ is in the exporter's native units/orientation, which
        // don't match this game's world at all (see README for the actual
        // numbers). Verified against the actual vertex data (not guessed):
        // the model's original axes are X = left/right, Y = nose(-)/tail(+),
        // Z = vertical (paws near Z=0, back/head near Z=height) — a
        // standing/crouching pose with head at the Y-minimum end. This
        // transform, applied once per frame on top of the critter's
        // position, does:
        //   1. Centers the model at its own X/Z origin (so scaling/rotating
        //      happens around its middle, not some arbitrary export origin)
        //   2. Rotates -90° around X, which maps old Z (up) to new Y (up)
        //      and old Y (nose/tail) to new Z, with the nose ending up on
        //      the +Z side — confirmed by tracing a nose vertex through the
        //      full transform, which lands at positive Z. This is what
        //      makes FacingAngle=0 in Critter.cs correctly mean "facing +Z."
        //   3. Scales it down to fit inside roughly one grid cell
        //   4. Lifts it back up so its feet sit on the floor (Y=0) instead
        //      of the model's center sitting there — also verified: a paw
        //      vertex (original Z≈0) lands exactly at Y=0 after this chain.
        private static Matrix BuildCritterModelTransform(StaticMesh mesh)
        {
            Vector3 center = mesh.Center;
            Vector3 size = mesh.Size;

            // Centering uses only X/Z here — the Y-lift after rotation
            // (below) handles vertical placement instead, since we want
            // "feet on the floor," not "center on the floor."
            var centerXZ = new Matrix(
                1, 0, 0, 0,
                0, 1, 0, 0,
                0, 0, 1, 0,
                -center.X, 0, -center.Z, 1);

            // Exporter axis convention here is Z-up with the head at the
            // Y-minimum end (confirmed by inspecting vertex distribution:
            // the Y-min region is a dense, wide cluster — the body/head —
            // while Y-max is a small sparse cluster — the tail tip). Rotate
            // -90 degrees around X so old Z (up) becomes new Y (up).
            var axisFix = Matrix.CreateRotationX(MathHelper.ToRadians(-90f));

            // After the axis fix, model height maps directly from the
            // original Z extent (confirmed: paws sit near old Z=0, back/head
            // near old Z=max), so scale using that directly rather than an
            // approximation.
            float rawHeight = size.Z;
            float targetHeight = CellSize * 0.8f; // leave a little headroom under the cell
            float scale = rawHeight > 0.0001f ? targetHeight / rawHeight : 1f;

            var scaleM = Matrix.CreateScale(scale);

            // After centering+rotating+scaling, the model's lowest point is
            // at roughly -(rawHeight/2) * scale in Y (since axisFix turned
            // the old Z-min, now centered on 0, into the new Y-min). Lift by
            // that amount so the model's feet land on Y=0.
            float liftY = (rawHeight / 2f) * scale;
            var liftM = Matrix.CreateTranslation(0f, liftY, 0f);

            return centerXZ * axisFix * scaleM * liftM;
        }

        protected override void Update(GameTime gameTime)
        {
            _input.Update(gameTime);
            var input = _input.State;

            if (input.Exit) Exit();

            _camera.Update(gameTime, input);

            if (!_won)
            {
                _elapsedTime += (float)gameTime.ElapsedGameTime.TotalSeconds;
                _player.Update(gameTime, _map, CellSize, input);
                _critter.Update(gameTime, _map, CellSize, _player.Cell);

                if (_critter.Caught)
                {
                    _won = true;
                    EventSystem.Instance.Publish(new GameWonEvent(_elapsedTime));
                }
            }
            else
            {
                if (input.Restart) Restart();
            }

            base.Update(gameTime);
        }

        private void Restart()
        {
            _player = new Player(new GridPos(1, 1), CellSize);
            _critter = new Critter(new GridPos(_map.Width - 2, _map.Height - 2), CellSize, new SmartAnimalAI());
            _elapsedTime = 0f;
            _won = false;
            EventSystem.Instance.Publish(new GameStartedEvent());
        }

        protected override void Draw(GameTime gameTime)
        {
            GraphicsDevice.Clear(new Color(20, 20, 25));
            GraphicsDevice.DepthStencilState = DepthStencilState.Default; // enable depth testing for 3D

            _effect.View = _camera.View;
            _effect.Projection = _camera.Projection;
            _effect.LightingEnabled = true;
            _effect.AmbientLightColor = new Vector3(0.5f, 0.5f, 0.5f); // Fills in shadows
            _effect.SpecularColor = new Vector3(0.2f, 0.2f, 0.2f);    // Adds a tiny bit of shine
            // Floor (single quad at origin, Y=0)
            DrawMesh(_floorVerts, Matrix.Identity);

            // Walls: one cube per shelf tile, scaled up in height a bit for visual clarity
            for (int y = 0; y < _map.Height; y++)
            {
                for (int x = 0; x < _map.Width; x++)
                {
                    if (_map[x, y] == 1)
                    {
                        var world = Matrix.CreateScale(CellSize * 0.95f, CellSize * 1.5f, CellSize * 0.95f)
                                    * Matrix.CreateTranslation(x * CellSize, 0f, y * CellSize);
                        DrawMesh(_wallVerts, world);
                    }
                }
            }

            // Player: procedural cube (kept simple since it's just "you").
            var playerWorld = Matrix.CreateScale(CellSize * 0.6f)
                               * Matrix.CreateTranslation(_player.WorldPos);
            DrawMesh(_playerVerts, playerWorld);

            DrawCritter();

            DrawUiOverlay();

            base.Draw(gameTime);
        }

        // Draws the cat model at the critter's current position, facing its
        // direction of travel. Falls back to nothing drawn (not a cube) if
        // the model failed to load, so a load failure is obvious rather than
        // silently swapped for a placeholder shape.
        private void DrawCritter()
        {
            if (_catMesh == null) return;

            // Order matters: normalize the raw model (transform), then face
            // its travel direction (rotation), then place it in the world
            // (translation) — each stage operates in the space the previous
            // one produced.
            var facing = Matrix.CreateRotationY(_critter.FacingAngle);
            var placement = Matrix.CreateTranslation(_critter.WorldPos);
            var world = _critterModelTransform * facing * placement;

            _catEffect.World = world;
            _catEffect.View = _camera.View;
            _catEffect.Projection = _camera.Projection;
            _catEffect.LightingEnabled = true;
            _catEffect.AmbientLightColor = new Vector3(0.5f, 0.5f, 0.5f); // Must match floor!
            _catEffect.SpecularColor = new Vector3(0.2f, 0.2f, 0.2f);    // Must match floor!
            foreach (var pass in _catEffect.CurrentTechnique.Passes)
            {
                pass.Apply();
                GraphicsDevice.DrawUserIndexedPrimitives(
                    PrimitiveType.TriangleList,
                    _catMesh.Vertices, 0, _catMesh.Vertices.Length,
                    _catMesh.Indices, 0, _catMesh.Indices.Length / 3);
            }
        }

        private void DrawMesh(VertexPositionNormalColor[] verts, Matrix world)
        {
            _effect.World = world;
            foreach (var pass in _effect.CurrentTechnique.Passes)
            {
                pass.Apply();
                GraphicsDevice.DrawUserPrimitives(PrimitiveType.TriangleList, verts, 0, verts.Length / 3);
            }
        }

        private void DrawUiOverlay()
        {
            // SpriteBatch draws are 2D-over-3D, fine as a screen-space overlay.
            _spriteBatch.Begin();

            // Simple status indicator without a font (see README to add real text).
            var indicatorColor = _won ? Color.LimeGreen : Color.Gray;
            _spriteBatch.Draw(_uiPixel, new Rectangle(10, 10, 20, 20), indicatorColor);

            if (_font != null)
            {
                DrawRainbowTitle(_font);

                string status = _won ? $"Caught it! Time: {_elapsedTime:F1}s  (R to restart)"
                                      : $"Time: {_elapsedTime:F1}s";
                _spriteBatch.DrawString(_font, status, new Vector2(40, 10), Color.White);
            }

            _spriteBatch.End();

            // Re-enable depth state for next frame's 3D draw (SpriteBatch changes it).
            GraphicsDevice.DepthStencilState = DepthStencilState.Default;
        }

        // "Store Chase" centered at the top of the screen. The base hue comes
        // from RainbowTitle's background thread; spreading the hue per
        // character produces the rainbow gradient. Only the read/draw happens
        // on the game thread, since the graphics device isn't thread-safe.
        private void DrawRainbowTitle(SpriteFont font)
        {
            const float hueStep = 0.07f; // rainbow spread across the glyphs
            const float y = 6f;

            float phase = _title.Phase;
            float cursorX = (GraphicsDevice.Viewport.Width - font.MeasureString(RainbowTitle.Text).X) / 2f;

            for (int i = 0; i < RainbowTitle.Text.Length; i++)
            {
                string glyph = RainbowTitle.Text[i].ToString();
                _spriteBatch.DrawString(font, glyph, new Vector2(cursorX, y),
                    RainbowTitle.FromHue(phase + i * hueStep));
                cursorX += font.MeasureString(glyph).X;
            }
        }

        protected override void UnloadContent()
        {
            _title?.Dispose();
            base.UnloadContent();
        }
    }
}
