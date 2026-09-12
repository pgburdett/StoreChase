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
        private VertexPositionNormalColor[] _critterVerts = null!;
        private Texture2D _uiPixel = null!;

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

            _graphics.PreferredBackBufferWidth = 1024;
            _graphics.PreferredBackBufferHeight = 768;
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
            _critterVerts = CubeMesh.Build(Color.OrangeRed);
            _floorVerts = CubeMesh.BuildFloorQuad(new Color(50, 50, 55), _map.Width * CellSize, _map.Height * CellSize);

            _uiPixel = new Texture2D(GraphicsDevice, 1, 1);
            _uiPixel.SetData(new[] { Color.White });

            // Optional: load a font for UI text (see README for setup).
            // _font = Content.Load<SpriteFont>("DefaultFont");
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

            // Player and critter: smaller cubes, positioned at their interpolated world pos.
            var playerWorld = Matrix.CreateScale(CellSize * 0.6f)
                               * Matrix.CreateTranslation(_player.WorldPos);
            DrawMesh(_playerVerts, playerWorld);

            var critterScale = _critter.Caught ? CellSize * 0.4f : CellSize * 0.5f;
            var critterWorld = Matrix.CreateScale(critterScale)
                                * Matrix.CreateTranslation(_critter.WorldPos);
            DrawMesh(_critterVerts, critterWorld);

            DrawUiOverlay();

            base.Draw(gameTime);
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
