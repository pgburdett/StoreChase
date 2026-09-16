using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;

namespace StoreChase
{
    // Single source of truth for camera state and controls. Game1 used to
    // own this math inline; the values below match that validated behavior.
    public class Camera
    {
        public Matrix View { get; private set; } = Matrix.Identity;
        public Matrix Projection { get; private set; } = Matrix.Identity;

        // Pan is an offset (in world units) applied to where the camera
        // looks; zoom scales the orthographic view size / camera distance;
        // tilt blends from straight-down orthographic (0) to an angled
        // perspective view (up to ~80 degrees).
        private Vector2 _panOffset = Vector2.Zero;
        public float Zoom { get; private set; } = 1f;
        public float TiltDegrees { get; private set; } = 0f;

        private readonly float _mapWidthWorld;
        private readonly float _mapHeightWorld;
        private readonly float _cellSize;
        private readonly GraphicsDevice _graphicsDevice;

        // Zoom 1.0 is "standard". Bigger = zoomed out (camera pulled back),
        // smaller = zoomed in. (Inverse of a classic camera convention — kept
        // from the original implementation for continuity.)
        public const float MinZoom = 0.25f;  // most zoomed in
        public const float MaxZoom = 2.5f;   // most zoomed out
        public const float PanSpeed = 12f;   // world units per second
        public const float ZoomStep = 0.1f;  // per wheel notch
        public const float MaxTiltDegrees = 80f; // near-horizontal, but never quite level
        public const float TiltSpeed = 45f;  // degrees per second

        public Camera(GraphicsDevice graphicsDevice, float mapWidthWorld, float mapHeightWorld, float cellSize)
        {
            _graphicsDevice = graphicsDevice;
            _mapWidthWorld = mapWidthWorld;
            _mapHeightWorld = mapHeightWorld;
            _cellSize = cellSize;

            UpdateCameraMatrices();
        }

        public void Reset()
        {
            _panOffset = Vector2.Zero;
            Zoom = 1f;
            TiltDegrees = 0f;
            UpdateCameraMatrices();
        }

        // Rebuilds view/projection from current pan, zoom, and tilt.
        private void UpdateCameraMatrices()
        {
            Vector3 center = new(_mapWidthWorld / 2f + _panOffset.X, 0f,
                                  _mapHeightWorld / 2f + _panOffset.Y);

            // Distance from center to camera scales with zoom, same idea as
            // the orthographic view size before — bigger distance/zoom = more
            // zoomed out. Used for both the orthographic and perspective paths
            // so zoom feels roughly continuous across the tilt transition.
            float baseDistance = 30f * Zoom;

            float tiltRad = MathHelper.ToRadians(TiltDegrees);

            // At tilt=0 the camera sits directly above center (0, baseDistance, 0).
            // As tilt increases it swings back along +Z and down, pivoting
            // around the center point, while still looking at center.
            Vector3 cameraPos = center + new Vector3(
                0f,
                baseDistance * (float)Math.Cos(tiltRad),
                baseDistance * (float)Math.Sin(tiltRad));

            // The view matrix's "up" vector can't be parallel to the look
            // direction. At tilt=0 we look straight down (parallel to world
            // Up), so "up" must instead be a horizontal axis (Forward/-Z) to
            // define screen orientation. Once tilted at all, the look
            // direction has a horizontal component, so world Up works and
            // gives the expected horizon-level framing.
            Vector3 upVector = TiltDegrees < 1f ? Vector3.Forward : Vector3.Up;
            View = Matrix.CreateLookAt(cameraPos, center, upVector);

            float aspect = _graphicsDevice.Viewport.AspectRatio;

            if (TiltDegrees < 1f)
            {
                // Effectively straight down: use true orthographic (no
                // perspective distortion), matching the original design.
                float baseWidth = (_mapWidthWorld + _cellSize * 2) * Zoom;
                float baseHeight = (_mapHeightWorld + _cellSize * 2) * Zoom;
                Projection = Matrix.CreateOrthographic(baseWidth, baseHeight, 0.1f, 200f);
            }
            else
            {
                // Tilted: switch to perspective so depth reads correctly.
                // Field of view stays fixed; zoom is expressed through camera
                // distance instead (baseDistance above), which is the usual
                // way to "zoom" a perspective camera without warping the FOV.
                Projection = Matrix.CreatePerspectiveFieldOfView(
                    MathHelper.ToRadians(50f), aspect, 0.1f, 300f);
            }
        }

        public void Update(GameTime gameTime, in InputState input)
        {
            float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;

            // Pan speed scales with zoom so it feels consistent whether
            // zoomed in or out. Input is a -1..1 axis (I/J/K/L, or a stick).
            float speed = PanSpeed * Zoom * dt;
            _panOffset += input.CameraPan * speed;

            // Clamp panning so you can't scroll the camera arbitrarily far
            // from the map — keeps the store roughly in view.
            float panLimitX = _mapWidthWorld * 0.75f;
            float panLimitY = _mapHeightWorld * 0.75f;
            _panOffset.X = MathHelper.Clamp(_panOffset.X, -panLimitX, panLimitX);
            _panOffset.Y = MathHelper.Clamp(_panOffset.Y, -panLimitY, panLimitY);

            // Wheel/stick zoom, reported as a per-frame delta in wheel notches.
            if (input.Zoom != 0f)
            {
                float zoomChange = -input.Zoom * ZoomStep;
                Zoom = MathHelper.Clamp(Zoom + zoomChange, MinZoom, MaxZoom);
            }

            // Tilt between straight-down (0) and a shallow angled perspective
            // view; input is a -1..1 axis where +1 tilts up.
            TiltDegrees = MathHelper.Clamp(
                TiltDegrees + input.Tilt * TiltSpeed * dt, 0f, MaxTiltDegrees);

            UpdateCameraMatrices();
        }
    }
}