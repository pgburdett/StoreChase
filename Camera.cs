using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using System;

namespace StoreChase
{
    public class Camera
    {
        public Matrix View { get; private set; } = Matrix.Identity;
        public Matrix Projection { get; private set; } = Matrix.Identity;

        private Vector2 _panOffset = Vector2.Zero;
        public Vector2 PanOffset => _panOffset;

// Zoom 1.0 is "standard". Higher = closer (zoomed in), Lower = further (zoomed out).
        public float Zoom { get; private set; } = 1f;
        public float TiltDegrees { get; private set; } = 0f;

        private readonly int _mapWidth;
        private readonly int _mapHeight;
        private readonly float _cellSize;
        private readonly float _mapWidthWorld;
        private readonly float _mapHeightWorld;
        private readonly GraphicsDevice _graphicsDevice;

        private int _previousScrollValue = 0;

// Control constants
        public const float MinZoom = 0.5f; // Maximum zoom out
        public const float MaxZoom = 3.0f; // Maximum zoom in
        public const float PanSpeed = 15f;
        public const float ZoomStep = 0.15f;
        public const float MaxTiltDegrees = 80f;
        public const float TiltSpeed = 45f;

// Internal math constants to bridge Ortho/Perspective
        private const float DefaultFOV = MathHelper.PiOver4; // 45 degrees
        private const float BaseDistance = 40f;

        public Camera(GraphicsDevice graphicsDevice, int mapWidth, int mapHeight, float cellSize)
        {
            _graphicsDevice = graphicsDevice;
            _mapWidth = mapWidth;
            _mapHeight = mapHeight;
            _cellSize = cellSize;
            _mapWidthWorld = mapWidth * cellSize;
            _mapHeightWorld = mapHeight * cellSize;

            UpdateCameraMatrices();
        }

        public void Reset()
        {
            _panOffset = Vector2.Zero;
            Zoom = 1f;
            TiltDegrees = 0f;
            _previousScrollValue = 0;
            UpdateCameraMatrices();
        }

        /// <summary>
        /// Calculates matrices using a unified zoom/tilt approach to prevent visual jumps.
        /// </summary>
        public void UpdateCameraMatrices()
        {
// 1. Calculate Target Center (The point the camera looks at)
            Vector3 center = new Vector3(
                (_mapWidthWorld / 2f) + _panOffset.X,
                0f,
                (_mapHeightWorld / 2f) + _panOffset.Y);

// 2. Calculate Camera Position based on Tilt and Zoom
// We use trigonometry to place the camera on an arc.
            float tiltRad = MathHelper.ToRadians(TiltDegrees);

// The "Distance" here is how far from the center the camera sits.
// We scale this distance by Zoom so that zooming feels consistent in both modes.
            float distance = BaseDistance / Zoom;

            Vector3 cameraPos = new Vector3(
                center.X,
                distance * (float)Math.Cos(tiltRad), // Y is height
                center.Z - (distance * (float)Math.Sin(tiltRad)) // Z is depth
            );

// 3. Set View Matrix
// Use Up = Vector3.Up unless looking straight down, then use Forward to prevent gimbal lock/flip
            Vector3 up = TiltDegrees < 0.1f ? Vector3.Forward : Vector3.Up;
            View = Matrix.CreateLookAt(cameraPos, center, up);

// 4. Set Projection Matrix (The "Bridge" Logic)
            float aspect = _graphicsDevice.Viewport.AspectRatio;

            if (TiltDegrees < 0.5f) // Threshold for Orthographic
            {
// In Ortho mode, Zoom controls the size of the view frustum.
// To make it match Perspective, we calculate the width based on FOV and distance.
                float orthoWidth = 2f * (float)Math.Tan(DefaultFOV / 2f) * distance * aspect;
                float orthoHeight = 2f * (float)Math.Tan(DefaultFOV / 2f) * distance;

// We scale the dimensions by Zoom to actually zoom in/out
                Projection = Matrix.CreateOrthographic(orthoWidth / Zoom, orthoHeight / Zoom, 0.1f, 500f);
            }
            else
            {
// In Perspective mode, we use a standard FOV.
//                Zoom is handled by the camera distance in the View matrix calculation above.
                Projection = Matrix.CreatePerspectiveFieldOfView(DefaultFOV, aspect, 0.1f, 500f);
            }
        }

        public void Update(GameTime gameTime)
        {
            float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;
            var kb = Keyboard.GetState();
            var ms = Mouse.GetState();

// --- Panning ---
// We scale pan speed by Zoom so that when you are zoomed in,
// movement feels "snappier" and matches the visual scale.
            float currentPanSpeed = PanSpeed * (1f / Zoom);
            float moveStep = currentPanSpeed * dt;

            if (kb.IsKeyDown(Keys.I)) _panOffset.Y -= moveStep;
            if (kb.IsKeyDown(Keys.K)) _panOffset.Y += moveStep;
            if (kb.IsKeyDown(Keys.J)) _panOffset.X -= moveStep;
            if (kb.IsKeyDown(Keys.L)) _panOffset.X += moveStep;

// Clamp panning to map bounds
            float panLimitX = (_mapWidthWorld / 2f) * 0.8f;
            float panLimitY = (_mapHeightWorld / 2f) * 0.8f;
            _panOffset.X = MathHelper.Clamp(_panOffset.X, -panLimitX, panLimitX);
            _panOffset.Y = MathHelper.Clamp(_panOffset.Y, -panLimitY, panLimitY);

// --- Zooming ---
            int scrollValue = ms.ScrollWheelValue;
            int scrollDelta = scrollValue - _previousScrollValue;
            _previousScrollValue = scrollValue;

            if (scrollDelta != 0)
            {
// Use a logarithmic-style zoom for smoother feeling
                float zoomAdjustment = (scrollDelta > 0) ? ZoomStep : -ZoomStep;
                Zoom = MathHelper.Clamp(Zoom + (zoomAdjustment / Zoom), MinZoom, MaxZoom);
            }

// --- Tilting ---
            float tiltStep = TiltSpeed * dt;
            if (kb.IsKeyDown(Keys.O)) TiltDegrees += tiltStep; // Tilt up/away
            if (kb.IsKeyDown(Keys.U)) TiltDegrees -= tiltStep; // Tilt down/toward floor
            TiltDegrees = MathHelper.Clamp(TiltDegrees, 0f, MaxTiltDegrees);

            UpdateCameraMatrices();
        }
    }
}