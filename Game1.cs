using System.IO;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

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
        private Matrix _view;
        private Matrix _projection;

        // Camera pan/zoom/tilt state. Pan is an offset (in world units)
        // applied to where the camera looks; zoom scales the orthographic
        // view size; tilt blends from straight-down orthographic (0) to an
        // angled perspective view (up to ~80 degrees).
        private Vector2 _cameraPanOffset = Vector2.Zero;
        private float _zoom = 1f;
        private const float MinZoom = 0.25f;  // most zoomed in
        private const float MaxZoom = 2.5f;   // most zoomed out
        private const float PanSpeed = 12f;   // world units per second
        private const float ZoomStep = 0.1f;  // per wheel notch
        private int _previousScrollValue = 0;

        private float _tiltDegrees = 0f;      // 0 = straight down (orthographic)
        private const float MaxTiltDegrees = 80f; // near-horizontal, but never quite level
        private const float TiltSpeed = 45f;  // degrees per second

        private float _mapWidthWorld;
        private float _mapHeightWorld;

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
            _critter = new Critter(new GridPos(_map.Width - 2, _map.Height - 2), CellSize);

            SetupCamera();

            base.Initialize();
        }

        private void SetupCamera()
        {
            _mapWidthWorld = _map.Width * CellSize;
            _mapHeightWorld = _map.Height * CellSize;

            // Center the initial pan on the map's middle.
            _cameraPanOffset = Vector2.Zero;
            _zoom = 1f;

            UpdateCameraMatrices();
        }

        // Rebuilds view/projection from current pan, zoom, and tilt. Called
        // once at startup and again whenever any camera control changes.
        private void UpdateCameraMatrices()
        {
            Vector3 center = new(_mapWidthWorld / 2f + _cameraPanOffset.X, 0f,
                                  _mapHeightWorld / 2f + _cameraPanOffset.Y);

            // Distance from center to camera scales with zoom, same idea as
            // the orthographic view size before — bigger distance/zoom = more
            // zoomed out. Used for both the orthographic and perspective paths
            // so zoom feels roughly continuous across the tilt transition.
            float baseDistance = 30f * _zoom;

            float tiltRad = MathHelper.ToRadians(_tiltDegrees);

            // At tilt=0 the camera sits directly above center (0, baseDistance, 0).
            // As tilt increases it swings back along +Z and down, pivoting
            // around the center point, while still looking at center.
            Vector3 cameraPos = center + new Vector3(
                0f,
                baseDistance * (float)System.Math.Cos(tiltRad),
                baseDistance * (float)System.Math.Sin(tiltRad));

            // The view matrix's "up" vector can't be parallel to the look
            // direction. At tilt=0 we look straight down (parallel to world
            // Up), so "up" must instead be a horizontal axis (Forward/-Z) to
            // define screen orientation. Once tilted at all, the look
            // direction has a horizontal component, so world Up works and
            // gives the expected horizon-level framing.
            Vector3 upVector = _tiltDegrees < 1f ? Vector3.Forward : Vector3.Up;
            _view = Matrix.CreateLookAt(cameraPos, center, upVector);

            float aspect = GraphicsDevice.Viewport.AspectRatio;

            if (_tiltDegrees < 1f)
            {
                // Effectively straight down: use true orthographic (no
                // perspective distortion), matching the original design.
                float baseWidth = (_mapWidthWorld + CellSize * 2) * _zoom;
                float baseHeight = (_mapHeightWorld + CellSize * 2) * _zoom;
                _projection = Matrix.CreateOrthographic(baseWidth, baseHeight, 0.1f, 200f);
            }
            else
            {
                // Tilted: switch to perspective so depth reads correctly.
                // Field of view stays fixed; zoom is expressed through camera
                // distance instead (baseDistance above), which is the usual
                // way to "zoom" a perspective camera without warping the FOV.
                _projection = Matrix.CreatePerspectiveFieldOfView(
                    MathHelper.ToRadians(50f), aspect, 0.1f, 300f);
            }
        }

        private void UpdateCameraControls(GameTime gameTime)
        {
            float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;
            var kb = Keyboard.GetState();

            // Pan speed scales with zoom so it feels consistent whether
            // zoomed in or out.
            float speed = PanSpeed * _zoom * dt;

            // I/J/K/L pan — deliberately not WASD/arrows, which drive the player.
            if (kb.IsKeyDown(Keys.I)) _cameraPanOffset.Y -= speed;
            if (kb.IsKeyDown(Keys.K)) _cameraPanOffset.Y += speed;
            if (kb.IsKeyDown(Keys.J)) _cameraPanOffset.X -= speed;
            if (kb.IsKeyDown(Keys.L)) _cameraPanOffset.X += speed;

            // Clamp panning so you can't scroll the camera arbitrarily far
            // from the map — keeps the store roughly in view.
            float panLimitX = _mapWidthWorld * 0.75f;
            float panLimitY = _mapHeightWorld * 0.75f;
            _cameraPanOffset.X = MathHelper.Clamp(_cameraPanOffset.X, -panLimitX, panLimitX);
            _cameraPanOffset.Y = MathHelper.Clamp(_cameraPanOffset.Y, -panLimitY, panLimitY);

            // Mouse wheel zoom. ScrollWheelValue is cumulative, so compare
            // against last frame's value to get the delta for this frame.
            int scrollValue = Mouse.GetState().ScrollWheelValue;
            int scrollDelta = scrollValue - _previousScrollValue;
            _previousScrollValue = scrollValue;

            if (scrollDelta != 0)
            {
                // One notch is typically 120 units; scale to a smooth zoom step.
                float zoomChange = -(scrollDelta / 120f) * ZoomStep;
                _zoom = MathHelper.Clamp(_zoom + zoomChange, MinZoom, MaxZoom);
            }

            // U/O tilt the camera between straight-down (0) and a shallow
            // angled perspective view (MaxTiltDegrees). Deliberately not
            // I/J/K/L (pan) or WASD/arrows (player movement).
            float tiltDelta = TiltSpeed * dt;
            if (kb.IsKeyDown(Keys.O)) _tiltDegrees += tiltDelta;
            if (kb.IsKeyDown(Keys.U)) _tiltDegrees -= tiltDelta;
            _tiltDegrees = MathHelper.Clamp(_tiltDegrees, 0f, MaxTiltDegrees);

            UpdateCameraMatrices();
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

            // Optional: load a font for UI text (see README for setup).
            // _font = Content.Load<SpriteFont>("DefaultFont");
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
                LightingEnabled = true,
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
            if (GamePad.GetState(PlayerIndex.One).Buttons.Back == ButtonState.Pressed ||
                Keyboard.GetState().IsKeyDown(Keys.Escape))
                Exit();

            UpdateCameraControls(gameTime);

            if (!_won)
            {
                _elapsedTime += (float)gameTime.ElapsedGameTime.TotalSeconds;
                _player.Update(gameTime, _map, CellSize);
                _critter.Update(gameTime, _map, CellSize, _player.Cell);

                if (_critter.Caught) _won = true;
            }
            else
            {
                if (Keyboard.GetState().IsKeyDown(Keys.R)) Restart();
            }

            base.Update(gameTime);
        }

        private void Restart()
        {
            _player = new Player(new GridPos(1, 1), CellSize);
            _critter = new Critter(new GridPos(_map.Width - 2, _map.Height - 2), CellSize);
            _elapsedTime = 0f;
            _won = false;
        }

        protected override void Draw(GameTime gameTime)
        {
            GraphicsDevice.Clear(new Color(20, 20, 25));
            GraphicsDevice.DepthStencilState = DepthStencilState.Default; // enable depth testing for 3D

            _effect.View = _view;
            _effect.Projection = _projection;

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
            _catEffect.View = _view;
            _catEffect.Projection = _projection;

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
                string status = _won ? $"Caught it! Time: {_elapsedTime:F1}s  (R to restart)"
                                      : $"Time: {_elapsedTime:F1}s";
                _spriteBatch.DrawString(_font, status, new Vector2(40, 10), Color.White);
            }

            _spriteBatch.End();

            // Re-enable depth state for next frame's 3D draw (SpriteBatch changes it).
            GraphicsDevice.DepthStencilState = DepthStencilState.Default;
        }
    }
}
