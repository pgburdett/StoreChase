using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;

namespace StoreChase
{
    // Which camera the game is currently driving. Isometric is the default
    // top-down/pan/tilt/zoom view; FirstPerson is a mouse-look camera seated
    // at the player's head, with movement made camera-relative.
    public enum CameraViewMode
    {
        Isometric,
        FirstPerson,
    }

    // Single source of truth for camera state and controls. Game1 used to
    // own this math inline; the values below match that validated behavior.
    public class Camera
    {
        public Matrix View { get; private set; } = Matrix.Identity;
        public Matrix Projection { get; private set; } = Matrix.Identity;

        /// <summary>View * Projection, for projecting world points to screen
        /// space (the HUD's off-screen thief marker).</summary>
        public Matrix ViewProjection => View * Projection;

        public CameraViewMode Mode { get; private set; } = CameraViewMode.Isometric;

        // Pan is an offset (in world units) applied to where the camera
        // looks; zoom scales the orthographic view size / camera distance;
        // tilt blends from straight-down orthographic (0) to an angled
        // perspective view (up to ~80 degrees).
        private Vector2 _panOffset = Vector2.Zero;
        public float Zoom { get; private set; } = 1f;
        public float TiltDegrees { get; private set; } = 0f;

        // First-person yaw/pitch (radians). Yaw uses the same convention as
        // Player.FacingAngle: 0 faces +Z, increasing turns toward +X.
        private float _fpYaw;
        private float _fpPitch;

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

        // First-person: look radians per look unit (≈mouse pixel), the eye
        // height from PersonMesh's head center, and the FOV/near plane.
        public const float LookSensitivity = 0.0025f;
        public const float MaxPitchDegrees = 85f;
        public const float FirstPersonEyeHeight = 2.2f;
        public const float FirstPersonFovDegrees = 60f;
        public const float FirstPersonNearPlane = 0.05f;

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
            //
            // A tiny +Z offset on the camera position keeps the view direction
            // from ever being exactly parallel to world Up (whence a neutral
            // "up" vector would be undefined). Previously a hard Forward/
            // world-Up swap at the 0°/1° boundary caused a one-frame roll; with
            // this nudge the same world-Up vector is valid at every angle, so
            // the orientation changes continuously across the whole tilt range.
            // The offset is a fraction of the camera distance, so it scales with
            // zoom and stays far below a pixel of screen parallax.
            const float upNudge = 0.0001f;
            Vector3 cameraPos = center + new Vector3(
                0f,
                baseDistance * (float)Math.Cos(tiltRad),
                baseDistance * (float)Math.Sin(tiltRad) + baseDistance * upNudge);

            Vector3 upVector = Vector3.Up;
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

        public void Update(GameTime gameTime, in InputState input, Vector3 playerWorldPos, float playerFacing)
        {
            if (input.ToggleCamera)
            {
                Mode = Mode == CameraViewMode.Isometric
                    ? CameraViewMode.FirstPerson
                    : CameraViewMode.Isometric;

                // Entering FPS: start level, looking the way the player faces.
                // Iso pan/zoom/tilt are left untouched so toggling back restores
                // the exact previous view.
                if (Mode == CameraViewMode.FirstPerson)
                {
                    _fpYaw = playerFacing;
                    _fpPitch = 0f;
                }
            }

            if (Mode == CameraViewMode.FirstPerson)
            {
                UpdateFirstPerson(input, playerWorldPos);
                return;
            }

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

        // Mouse-look from the player's head. The view is rebuilt from scratch
        // each frame (no interpolated state), so it stays glued to the player
        // as they move.
        private void UpdateFirstPerson(in InputState input, Vector3 playerWorldPos)
        {
            // Input.Look is right/up positive. Mouse-right must turn the view
            // right, which is a *decreasing* yaw: forward = (sin yaw, cos yaw)
            // and the camera's right (cross(forward, up)) is -X at yaw 0.
            _fpYaw -= input.Look.X * LookSensitivity;
            _fpPitch = MathHelper.Clamp(
                _fpPitch - input.Look.Y * LookSensitivity,
                -MathHelper.ToRadians(MaxPitchDegrees),
                MathHelper.ToRadians(MaxPitchDegrees));

            float cosPitch = (float)Math.Cos(_fpPitch);
            Vector3 forward = new(
                (float)Math.Sin(_fpYaw) * cosPitch,
                (float)Math.Sin(_fpPitch),
                (float)Math.Cos(_fpYaw) * cosPitch);

            Vector3 eye = playerWorldPos + new Vector3(0f, FirstPersonEyeHeight, 0f);
            View = Matrix.CreateLookAt(eye, eye + forward, Vector3.Up);
            Projection = Matrix.CreatePerspectiveFieldOfView(
                MathHelper.ToRadians(FirstPersonFovDegrees),
                _graphicsDevice.Viewport.AspectRatio,
                FirstPersonNearPlane, 300f);
        }

        // Is a world point inside what the camera is currently pointed at?
        //
        // This is the difference between the two camera modes as far as the
        // game is concerned. Overhead, the whole store is in frame and the
        // answer is effectively "yes" for anything in the room, so a cat being
        // watched never notices. In first person it's a real cone, which is what
        // lets a Nervous cat be pinned just by looking at it.
        //
        // The cone is a generous half-angle rather than the exact frustum: the
        // gameplay question is "am I pointed roughly at that cat", not "is it
        // inside a pixel-perfect frustum", and a narrow cone would make the
        // interaction fiddly.
        public bool IsInViewCone(Vector3 eye, Vector3 target)
        {
            Vector3 to = target - eye;

            // Anything right on top of the camera counts as watched.
            float sqrLen = to.LengthSquared();
            if (sqrLen < 0.0001f) return true;
            to /= MathF.Sqrt(sqrLen);

            float halfAngle = Mode == CameraViewMode.FirstPerson
                ? MathHelper.ToRadians(FirstPersonFovDegrees * 0.6f)
                : MathHelper.ToRadians(90f);

            Vector3 forward = FacingDirection;
            return Vector3.Dot(forward, to) >= MathF.Cos(halfAngle);
        }

        /// <summary>
        /// The direction the camera is looking, flattened onto the XZ plane and
        /// normalized. This is the aim direction for anything thrown by hand:
        /// pitching up or down shouldn't change where a ball lands, only where
        /// you were looking a moment ago.
        /// </summary>
        public Vector2 FacingOnPlane
        {
            get
            {
                Vector3 f = FacingDirection;
                var flat = new Vector2(f.X, f.Z);
                // Looking straight up or down leaves nothing to throw along;
                // fall back to the last good heading rather than throwing at a
                // zero vector.
                return flat.LengthSquared() > 0.0001f ? Vector2.Normalize(flat) : new Vector2(0f, 1f);
            }
        }

        // The direction the camera is looking, in world space. In first person
        // that's the yaw/pitch heading; overhead it's straight down, nudged off
        // vertical the same way the view matrix is.
        public Vector3 FacingDirection
        {
            get
            {
                if (Mode == CameraViewMode.FirstPerson)
                {
                    float cosPitch = (float)Math.Cos(_fpPitch);
                    return new Vector3(
                        (float)Math.Sin(_fpYaw) * cosPitch,
                        (float)Math.Sin(_fpPitch),
                        (float)Math.Cos(_fpYaw) * cosPitch);
                }

                // Straight down, nudged so it's never exactly parallel to Up
                // (mirrors the upNudge in UpdateCameraMatrices).
                return Vector3.Normalize(new Vector3(0f, -1f, 0.0001f));
            }
        }

        // Turns the movement axes into a world-space XZ heading for free
        // (first-person) movement: W heads where you look, S backs up, A/D
        // strafe. The result is normalized; isometric doesn't use it (the
        // player moves grid-cell to cell off the raw axes).
        public Vector2 MapMoveToWorld(in InputState input)
        {
            float forwardAmount = -input.Move.Y; // W = +1
            float strafeAmount = input.Move.X;   // D = +1

            float sin = (float)Math.Sin(_fpYaw);
            float cos = (float)Math.Cos(_fpYaw);

            // Camera forward and right on the XZ plane (right = cross(forward, up)).
            Vector2 dir = new(
                sin * forwardAmount - cos * strafeAmount,
                cos * forwardAmount + sin * strafeAmount);

            if (dir.LengthSquared() > 1f) dir.Normalize();
            return dir;
        }
    }
}