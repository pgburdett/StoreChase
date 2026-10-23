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
    /// <summary>
    /// Single source of truth for camera state and controls. Game1 used to
    /// own this math inline; the values below match that validated behavior.
    ///
    /// Two modes: Isometric (default) is an orthographic top-down view that
    /// can pan, zoom and tilt into a perspective view. FirstPerson is a 60°
    /// perspective camera at the player's head with mouse-look yaw/pitch.
    /// </summary>
    public class Camera
    {
        /// <summary>The current view matrix.</summary>
        public Matrix View { get; private set; } = Matrix.Identity;

        /// <summary>The current projection matrix.</summary>
        public Matrix Projection { get; private set; } = Matrix.Identity;

        /// <summary>View * Projection, for projecting world points to screen
        /// space (the HUD's off-screen thief marker).</summary>
        public Matrix ViewProjection => View * Projection;

        /// <summary>The current camera mode.</summary>
        public CameraViewMode Mode { get; private set; } = CameraViewMode.Isometric;

        /// <summary>
        /// The world point the camera is centred on, in the isometric view.
        ///
        /// This is the player, plus any manual panning, clamped so the view
        /// stays over the floor plan. It is the point the camera looks at, so
        /// it is also the centre of the visible area.
        /// </summary>
        public Vector3 FocusPoint { get; private set; }

        // Pan is an offset (in world units) applied to where the camera
        // looks; zoom scales the orthographic view size / camera distance;
        // tilt blends from straight-down orthographic (0) to an angled
        // perspective view (up to ~80 degrees).
        private Vector2 _panOffset = Vector2.Zero;
        public float Zoom { get; private set; } = DefaultZoom;
        public float TiltDegrees { get; private set; } = 0f;

        // First-person yaw/pitch (radians). Yaw uses the same convention as
        // Player.FacingAngle: 0 faces +Z, increasing turns toward +X.
        private float _fpYaw;
        private float _fpPitch;

        private readonly float _mapWidthWorld;
        private readonly float _mapHeightWorld;
        // The floor plan's corner in world space: cells are anchored at
        // cell * cellSize (each spans ±cellSize/2 around its anchor), so the
        // store occupies [-cellSize/2, mapWorld - cellSize/2] — the same
        // half-cell frame the floor draw applies (Game1.FloorWorld). The
        // clamp works in that frame or the view shaves a wall off one edge
        // and shows a void strip beyond the floor on the other.
        private readonly float _mapOriginX;
        private readonly float _mapOriginY;
        private readonly float _cellSize;
        private readonly GraphicsDevice? _graphicsDevice;

        // The viewport's aspect ratio. Cached rather than read from the
        // device every frame, and settable without one, so the camera's
        // framing math can be tested headlessly.
        private float _aspectRatio;

        // Where the camera is looking, in world X/Z. In the isometric view
        // this is the player, offset by any manual panning; in first person
        // it's the player's head.
        private Vector2 _followTarget;

        // Zoom 1.0 is "standard". Bigger = zoomed out (camera pulled back),
        // smaller = zoomed in. (Inverse of a classic camera convention — kept
        // from the original implementation for continuity.)
        public const float MinZoom = 0.25f;  // most zoomed in
        public const float MaxZoom = 2.5f;   // most zoomed out
        public const float PanSpeed = 12f;   // world units per second
        public const float ZoomStep = 0.1f;  // per wheel notch
        public const float MaxTiltDegrees = 80f; // near-horizontal, but never quite level
        public const float TiltSpeed = 45f;  // degrees per second

        public const float DefaultTiltDegrees = 45f;
        public const float DefaultZoom = 0.6f;

        // How many cells of the store are visible across the screen at zoom 1.
        //
        // This is what makes the store scroll: the viewport is a fixed window
        // onto the floor plan rather than a fit-to-everything view, so a store
        // wider than this needs the camera to follow the player. It used to be
        // "the whole map, plus padding", which meant the view size grew with
        // the store and there was nothing to scroll.
        public const float ViewCellsWide = 16f;

        // First-person: look radians per look unit (≈mouse pixel), the eye
        // height from PersonMesh's head center, and the FOV/near plane.
        public const float LookSensitivity = 0.0025f;
        public const float MaxPitchDegrees = 85f;
        public const float FirstPersonEyeHeight = 2.2f;
        public const float FirstPersonFovDegrees = 60f;
        public const float FirstPersonNearPlane = 0.05f;

        // Screen shake. Gameplay calls AddShake (tackles, alarm spikes, the
        // fire alarm); Update decays it, and the matrix builders apply the
        // jitter as a positional offset to eye *and* look target — a pure
        // frustum translation, which reads as a shake in the orthographic and
        // perspective paths alike and leaves FocusPoint (what the framing
        // tests measure) untouched.
        public const float MaxShake = 1f;
        public const float ShakeDecay = 2.4f;       // strength units per second
        public const float ShakeWorldScale = 0.3f;  // world units of jitter at full strength

        private float _shake;
        private readonly Random _shakeRng = new();

        /// <summary>The shake strength still to burn off (0 = steady).</summary>
        public float Shake => _shake;

        /// <summary>Adds shake, accumulating up to <see cref="MaxShake"/>.</summary>
        public void AddShake(float amplitude) =>
            _shake = Math.Min(MaxShake, _shake + Math.Max(0f, amplitude));

        // A fresh random offset for this frame's matrices. Zero when steady,
        // so a camera that was never shaken is deterministic (which is what
        // lets the framing checks compare matrices at all).
        private Vector3 ShakeOffset()
        {
            if (_shake <= 0.0001f) return Vector3.Zero;
            float s = _shake * ShakeWorldScale;
            return new Vector3(
                (float)(_shakeRng.NextDouble() * 2 - 1),
                (float)(_shakeRng.NextDouble() * 2 - 1),
                (float)(_shakeRng.NextDouble() * 2 - 1)) * s;
        }

        public Camera(GraphicsDevice graphicsDevice, float mapWidthWorld, float mapHeightWorld, float cellSize)
        {
            _graphicsDevice = graphicsDevice;
            _mapWidthWorld = mapWidthWorld;
            _mapHeightWorld = mapHeightWorld;
            _cellSize = cellSize;
            _mapOriginX = _mapOriginY = -cellSize * 0.5f;
            _aspectRatio = graphicsDevice.Viewport.AspectRatio;

            // Open in the angled 3/4 view rather than straight down. From
            // here U/O tilt, and the view reads as a room instead of a floor
            // plan.
            TiltDegrees = DefaultTiltDegrees;

            // Dev-only starting view, for screenshotting the game on a
            // machine where synthetic key events never reach the window
            // (KWin's input-injection prompt blocks xdotool, so U/O/V can't
            // be driven remotely): SC_TILT=45 overrides the opening tilt,
            // SC_FP=1 starts in first person. Unset — the normal case — this
            // is two environment lookups and changes nothing.
            if (float.TryParse(Environment.GetEnvironmentVariable("SC_TILT"), out float forcedTilt))
                TiltDegrees = MathHelper.Clamp(forcedTilt, 0f, MaxTiltDegrees);
            if (Environment.GetEnvironmentVariable("SC_FP") == "1")
                Mode = CameraViewMode.FirstPerson;

            UpdateCameraMatrices();
        }

        /// <summary>
        /// Creates a camera with no graphics device.
        ///
        /// The framing math — the viewport size, the follow target, the clamp
        /// to the store's edges — needs nothing but the map's dimensions and
        /// an aspect ratio. That makes it testable without a display, which
        /// is the only way to check that the scrolling camera actually
        /// scrolls rather than reading the code and hoping.
        /// </summary>
        /// <param name="aspectRatio">The viewport's width divided by its height.</param>
        /// <param name="mapWidthWorld">The store's width in world units.</param>
        /// <param name="mapHeightWorld">The store's height in world units.</param>
        /// <param name="cellSize">World units per grid cell.</param>
        public Camera(float aspectRatio, float mapWidthWorld, float mapHeightWorld, float cellSize)
        {
            _graphicsDevice = null;
            _mapWidthWorld = mapWidthWorld;
            _mapHeightWorld = mapHeightWorld;
            _cellSize = cellSize;
            _mapOriginX = _mapOriginY = -cellSize * 0.5f;
            _aspectRatio = aspectRatio;

            // Left flat on purpose: the framing checks are defined against the
            // orthographic reference view, where the visible width is exactly
            // ViewCellsWide cells. The angled default is set by the
            // GraphicsDevice constructor, which is what the game uses.
            UpdateCameraMatrices();
        }

        /// <summary>Resets pan, zoom and tilt to their defaults.</summary>
        public void Reset()
        {
            _panOffset = Vector2.Zero;
            Zoom = DefaultZoom;
            TiltDegrees = DefaultTiltDegrees;
            UpdateCameraMatrices();
        }

        // Rebuilds view/projection from current pan, zoom, and tilt.
        private void UpdateCameraMatrices()
        {
            float aspect = _aspectRatio;

            // The viewport size in world units. It is a fixed window onto the
            // store: ViewCellsWide cells across at zoom 1, scaled by zoom from
            // there. The old version derived the size from the map's own
            // dimensions, which meant a bigger store zoomed itself out to fit
            // and there was never anything to scroll.
            float viewWidth = ViewCellsWide * _cellSize * Zoom;
            float viewHeight = viewWidth / aspect;

            // Where the camera looks: the follow target (the player), plus any
            // manual panning. Clamped so the view never leaves the store —
            // otherwise the camera would show the void beyond the walls.
            Vector2 center = ClampToMap(_followTarget + _panOffset, viewWidth, viewHeight);
            Vector3 lookAt = new(center.X, 0f, center.Y);
            FocusPoint = lookAt;

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

            // Distance back from the focus point. Derived from the viewport
            // height so that tilting keeps roughly the same amount of store
            // in frame — a fixed distance would make the view shrink and grow
            // as the player zoomed.
            float baseDistance = viewHeight * 1.6f + 8f;

            Vector3 cameraPos = lookAt + new Vector3(
                0f,
                baseDistance * (float)Math.Cos(tiltRad),
                baseDistance * (float)Math.Sin(tiltRad) + baseDistance * upNudge);

            Vector3 upVector = Vector3.Up;

            // Shake: translate the whole frustum for this frame. FocusPoint
            // above stays the unshaken centre, so the follow/clamp maths and
            // everything that measures framing see a steady camera.
            Vector3 jitter = ShakeOffset();
            View = Matrix.CreateLookAt(cameraPos + jitter, lookAt + jitter, upVector);

            if (TiltDegrees < 1f)
            {
                // Effectively straight down: use true orthographic (no
                // perspective distortion), matching the original design.
                Projection = Matrix.CreateOrthographic(viewWidth, viewHeight, 0.1f, 200f);
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

        // Keeps the centre of the view inside the store.
        //
        // The view follows the player, but the player can walk right up to a
        // wall, and a view centred there would show half the screen full of
        // nothing outside the store. So the centre is pushed back in until the
        // whole viewport sits on the floor plan. When the store is smaller
        // than the viewport (a small map at maximum zoom-out) the centre is
        // pinned to the middle of the store instead, so it stays centred.
        private Vector2 ClampToMap(Vector2 center, float viewWidth, float viewHeight)
        {
            float halfW = viewWidth / 2f;
            float halfH = viewHeight / 2f;

            float x = viewWidth >= _mapWidthWorld
                ? _mapOriginX + _mapWidthWorld / 2f
                : Math.Clamp(center.X, _mapOriginX + halfW, _mapOriginX + _mapWidthWorld - halfW);

            float y = viewHeight >= _mapHeightWorld
                ? _mapOriginY + _mapHeightWorld / 2f
                : Math.Clamp(center.Y, _mapOriginY + halfH, _mapOriginY + _mapHeightWorld - halfH);

            return new Vector2(x, y);
        }

        /// <summary>
        /// Advances the camera one frame. Handles the camera-mode toggle,
        /// then delegates to either the first-person or isometric update path.
        /// </summary>
        public void Update(GameTime gameTime, in InputState input, Vector3 playerWorldPos, float playerFacing)
        {
            // Shake burns off in real time, before this frame's matrices are
            // rebuilt below (both modes rebuild them on this path).
            if (_shake > 0f)
                _shake = MathF.Max(0f, _shake - ShakeDecay * (float)gameTime.ElapsedGameTime.TotalSeconds);

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

            // The isometric view follows the player. This is what makes a store
            // bigger than the screen work: the camera tracks where the player
            // is and the viewport is a fixed window onto the floor plan.
            _followTarget = new Vector2(playerWorldPos.X, playerWorldPos.Z);

            float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;

            // Pan speed scales with zoom so it feels consistent whether
            // zoomed in or out. Input is a -1..1 axis (I/J/K/L, or a stick).
            float speed = PanSpeed * Zoom * dt;
            _panOffset += input.CameraPan * speed;

            // Pan is an offset from the player, not an absolute camera
            // position, so it can't drift the view away from the player
            // entirely. The clamp is generous enough to let the player look
            // ahead of themselves down a long aisle.
            float panLimitX = _mapWidthWorld * 0.5f;
            float panLimitY = _mapHeightWorld * 0.5f;
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
            Vector3 jitter = ShakeOffset();
            View = Matrix.CreateLookAt(eye + jitter, eye + jitter + forward, Vector3.Up);
            Projection = Matrix.CreatePerspectiveFieldOfView(
                MathHelper.ToRadians(FirstPersonFovDegrees),
                _aspectRatio,
                FirstPersonNearPlane, 300f);
        }

        /// <summary>
        /// Is a world point inside what the camera is currently pointing at?
        ///
        /// This is the difference between the two camera modes as far as the
        /// game is concerned.
        ///
        /// In first person it's a real cone: roughly what the player is
        /// looking at, which is what lets a Nervous cat be pinned just by
        /// looking at it. The cone is a generous half-angle rather than the
        /// exact frustum: the gameplay question is "am I pointed roughly at
        /// that cat", not "is it inside a pixel-perfect frustum", and a narrow
        /// cone would make the interaction fiddly.
        ///
        /// In the isometric view the camera looks straight down, so a cone
        /// would cover the entire store and mean nothing. Instead the test is
        /// "is it on screen" — the viewport is a window onto a floor plan that
        /// can be bigger than the screen, so a cat across the store is
        /// genuinely out of sight and genuinely not pinned. Which is the point
        /// of the two modes being different: the overhead view is a wide but
        /// bounded awareness, first person is a narrow, deliberate one.
        /// </summary>
        public bool IsInViewCone(Vector3 eye, Vector3 target)
        {
            if (Mode == CameraViewMode.FirstPerson)
            {
                Vector3 to = target - eye;

                // Anything right on top of the camera counts as watched.
                float sqrLen = to.LengthSquared();
                if (sqrLen < 0.0001f) return true;
                to /= MathF.Sqrt(sqrLen);

                float halfAngle = MathHelper.ToRadians(FirstPersonFovDegrees * 0.6f);

                Vector3 forward = FacingDirection;
                return Vector3.Dot(forward, to) >= MathF.Cos(halfAngle);
            }

            return IsOnScreen(target);
        }

        /// <summary>
        /// Is a world point inside the current viewport?
        ///
        /// Projects the point through the view-projection matrix and checks it
        /// against the normalised device coordinate range. This works for both
        /// the orthographic overhead view and the tilted perspective one,
        /// because it asks the same question the screen does: is this point
        /// drawn on the glass.
        /// </summary>
        public bool IsOnScreen(Vector3 worldPoint)
        {
            Vector4 clip = Vector4.Transform(worldPoint, ViewProjection);

            // Behind the camera: w goes negative and the perspective divide
            // flips the sign, so a naive bounds check would call it visible.
            if (clip.W <= 0.0001f) return false;

            float ndcX = clip.X / clip.W;
            float ndcY = clip.Y / clip.W;
            float ndcZ = clip.Z / clip.W;

            // NDC x/y are -1..1 on screen; z is 0..1 between near and far.
            // A margin keeps a cat right at the edge of the frame from
            // flickering between watched and not as the camera scrolls.
            const float margin = 0.02f;
            return ndcX >= -1f - margin && ndcX <= 1f + margin
                && ndcY >= -1f - margin && ndcY <= 1f + margin
                && ndcZ >= 0f && ndcZ <= 1f;
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