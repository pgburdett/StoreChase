using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace StoreChase
{
    /// <summary>
    /// The main game class. Owns the world (map, player, critters, loot, alarm),
    /// the camera, the input system, and all rendering.
    ///
    /// The frame is split into <see cref="Update"/> (simulation) and
    /// <see cref="Draw"/> (presentation). Update reads one <see cref="InputState"/>
    /// from <see cref="InputSystem"/>, advances the player, ball, critters, loot
    /// and alarm, then checks for win/lose. Draw rebuilds the HUD snapshot and
    /// renders the 3D scene (floor, shelves, counter, player, loot, ball, cats)
    /// followed by the 2D overlay.
    ///
    /// A round is started by <see cref="StartRound"/>, which is called once from
    /// <see cref="Initialize"/> and again whenever the player presses R.
    /// </summary>
    public class Game1 : Game
    {
        private GraphicsDeviceManager _graphics;

        private const float CellSize = 2f; // world units per grid cell
        private const float WalkCycleSpeed = 12f; // radians/sec of the player's limb swing

        private GridMap _map = null!;
        private Player _player = null!;

        // A whole cast of them, not one: the store is a heist, and which cats
        // turn up changes the round. Tackled critters are retired (Tackle sets
        // the flag) and removed from the list.
        private readonly List<Critter> _critters = new();
        private readonly Random _rng = new();

        // The objective: loot, the alarm that fills while it's missing, and the
        // ball you can throw to move a cat off whatever it's doing.
        private ItemField _items = null!;
        private AlarmSystem _alarm = null!;
        private Ball _ball = null!;

        private BasicEffect _effect = null!;
        private Camera _camera = null!;
        private InputSystem _input = null!;
        private RainbowTitle _title = null!;

        // The screen-space overlay. It owns no game state: Game1 snapshots
        // what it needs each frame (see BuildHudState) and hands it over.
        // Assigned in LoadContent, once its graphics dependencies exist.
        private Hud _hud = null!;

        // Cube/floor meshes, built once and reused (just repositioned via world matrix).
        private VertexPositionNormalColorTexture[] _wallVerts = null!;
        private VertexPositionNormalColorTexture[] _floorVerts = null!;
        private PersonPart[] _playerParts = null!;
        private float _walkPhase; // drives the player's limb swing

        // Procedurally generated store surfaces (floor tiles, shelf boards,
        // cardboard). One BasicEffect draws them all — the texture bound per
        // batch is what distinguishes them, and the meshes carry the UVs.
        private StoreTextures.Set _textures = null!;

        // The cat replaces the procedural critter cube. It needs its own
        // effect (normal-mapped and textured, not vertex-colored) and a local
        // transform to normalize the raw model into the game's world
        // scale/orientation — see BuildCritterModelTransform for why each
        // piece is there.
        private StaticMesh? _catSourceMesh;
        private Effect _catEffect = null!;
        private Texture2D? _flatNormalMap;
        private Matrix _critterModelTransform = Matrix.Identity;

        // One rig per critter, all sharing the same mesh: the vertices are
        // rewritten in place each frame, so a shared mesh would make every cat
        // adopt the last one drawn. Each critter therefore gets its own parsed
        // copy of the geometry (see LoadCritterModels).
        private readonly Dictionary<Critter, CatAnimator> _catAnims = new();
        private readonly Dictionary<Critter, StaticMesh> _catMeshes = new();

        // A small cube, reused for the loot boxes and the ball.
        private VertexPositionNormalColorTexture[] _propVerts = null!;

        private RunPhase _phase = RunPhase.Playing;
        private float _elapsedTime = 0f;

        // Cached per-frame values shared by the critters and the HUD.
        private GridPos? _ballCell;
        private GridPos? _lootTarget;

        public const int LootCount = 5;
        public const int CritterCount = 3;
        public const float CarryHeight = 1.5f;

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

            _input = new InputSystem(new KeyboardMouseInput(), new GamePadInput());

            _title = new RainbowTitle();

            SetupCamera();

            new RunStats(EventSystem.Instance);

            // Synthesised up front so the first cue doesn't stutter. Safe to
            // fail: Sfx turns itself off when there's no audio device.
            Sfx.Initialise();

            StartRound();

            base.Initialize();
        }

        /// <summary>
        /// Creates the camera with the map's world-space dimensions.
        ///
        /// The camera is constructed once here and driven every frame by
        /// <see cref="Camera.Update"/>. Isometric is the default view;
        /// first-person is toggled with V / gamepad Y.
        /// </summary>
        private void SetupCamera()
        {
            _camera = new Camera(GraphicsDevice, _map.Width * CellSize, _map.Height * CellSize, CellSize);
        }

        // Spawns the player, a cast of cats, the merchandise and the alarm.
        // Called on startup and on every restart, so a round is always
        // reproducible from this one method.
        /// <summary>
        /// Starts a fresh round: spawns the player, a cast of cats, the
        /// merchandise and the alarm, then loads the critter models.
        ///
        /// Called on startup and on every restart, so a round is always
        /// reproducible from this one method. The cat meshes are keyed on the
        /// critter objects, so the old cast's meshes must be dropped with the
        /// cast or every restart leaks one model per cat.
        /// </summary>
        private void StartRound()
        {
            _player = new Player(new GridPos(1, 1), CellSize);

            _critters.Clear();

            // The meshes are keyed on the critter objects, and each is a full
            // copy of the cat's ~36k vertices, so dropping the cast has to drop
            // them too or every restart leaks one model per cat.
            _catAnims.Clear();
            _catMeshes.Clear();
            foreach (var cell in SpawnPoints())
            {
                var profile = CritterProfile.Random(_rng);
                var ai = new SmartAnimalAI(profile, _rng);
                _critters.Add(new Critter(cell, CellSize, ai, profile));
            }

            _items ??= new ItemField(_map, CellSize, _rng);
            _items.Spawn(LootCount, _player.Cell);

            _alarm ??= new AlarmSystem();
            _alarm.Reset();

            _ball ??= new Ball();
            _ball.Configure(CellSize);
            _ball.Clear();

            _elapsedTime = 0f;
            _phase = RunPhase.Playing;
            _endMessage = "";
            // A fresh cast needs a fresh set of animated meshes. Do this after
            // the critters list is rebuilt, since the meshes are keyed on the
            // critter objects. LoadCritterModels is a no-op for critters that
            // already have one.
            LoadCritterModels();

            EventSystem.Instance.Publish(new GameStartedEvent());
        }

        // Somewhere apart from the player's start (1,1) and spread out, so the
        // cats don't all pile into one aisle. The far end of the store is the
        // obvious "they're over there" choice.
        /// <summary>
        /// Picks spawn cells for the critter cast, spread out and away from the
        /// player's start so the round opens with a moment of quiet.
        ///
        /// Candidates are walkable, not on the counter, and more than 5 cells
        /// (Manhattan) from the player start. The far half of the store is
        /// preferred so the cats don't all pile into one aisle.
        /// </summary>
        private IEnumerable<GridPos> SpawnPoints()
        {
            var start = new GridPos(1, 1);
            var candidates = _map.WalkableCells()
                .Where(c => !_map.IsCounter(c.X, c.Y))
                .Where(c => Math.Abs(c.X - start.X) + Math.Abs(c.Y - start.Y) > 5)
                .ToList();

            if (candidates.Count == 0) yield return start;
            for (int i = 0; i < CritterCount && candidates.Count > 0; i++)
            {
                // Bias toward the far half of the store so the round opens with
                // a moment of quiet before anything happens.
                int idx = _rng.Next(candidates.Count / 2, candidates.Count);
                yield return candidates[idx];
                candidates.RemoveAt(idx);
            }
        }

        protected override void LoadContent()
        {
            _effect = new BasicEffect(GraphicsDevice)
            {
                VertexColorEnabled = true,
                LightingEnabled = true,
                TextureEnabled = true,
            };
            _effect.EnableDefaultLighting();
            // Default lighting comes from an angle; for a top-down view we mostly
            // care about the top faces reading brightly, which EnableDefaultLighting
            // already handles reasonably well.

            _textures = StoreTextures.Build(GraphicsDevice);

            // Shelf cubes are all the same size, so one mesh serves the whole
            // store. The repeats are in mesh space and get stretched by the
            // world scale in Draw(), so they describe texel density for this
            // exact shelf size: ~2 tiles across a 2-unit face, 3 up a 3-unit face.
            _wallVerts = CubeMesh.Build(Color.White, 2f, 3f);
            _playerParts = PersonMesh.Build();

            // One floor tile every 2 world units (= CellSize), which lines the
            // grout up with the shelf grid.
            float floorWidth = _map.Width * CellSize;
            float floorDepth = _map.Height * CellSize;
            _floorVerts = CubeMesh.BuildFloorQuad(
                Color.White, floorWidth, floorDepth, floorWidth / CellSize, floorDepth / CellSize);

            // The HUD takes the sprite batch, the 1x1 white texture and the
            // font, and draws the overlay from a per-frame snapshot of game
            // state (see HudState). Both graphics objects belong to the HUD,
            // so they're created here and handed over rather than kept as
            // fields; the font arrives just below.
            var spriteBatch = new SpriteBatch(GraphicsDevice);
            var uiPixel = new Texture2D(GraphicsDevice, 1, 1);
            uiPixel.SetData(new[] { Color.White });
            _hud = new Hud(spriteBatch, GraphicsDevice, uiPixel);

            LoadCatModel();
            _propVerts = CubeMesh.Build(Color.White, 1f, 1f);

            // Status/timer text is rendered from this font (see Hud). The font
            // belongs to the HUD, so it's loaded here and handed over.
            _hud.Font = Content.Load<SpriteFont>("DefaultFont");

            // Initialize runs before LoadContent, so the first round's cast was
            // created before the cat geometry existed. Give them their meshes
            // now; StartRound does the same for every later round.
            LoadCritterModels();
        }

        // Loads the cat OBJ/MTL/textures from Content/Models at runtime
        // (not through the MGCB content pipeline — see ObjLoader.cs for
        // why) and works out a local transform that fits it into one grid
        // cell, standing upright, facing a known direction.
        private void LoadCatModel()
        {
            _catSourceMesh = ObjLoader.Load(GraphicsDevice, CatModelPath());

            // Custom normal-mapped shader (see Content/Effects/CatNormalMap.fx).
            // Its lighting is deliberately the same key+fill+back setup as
            // BasicEffect's EnableDefaultLighting, so the cat reads as part of
            // the same scene as the floor/walls.
            //
            // Every uniform is pushed from here: the effect framework does not
            // reliably apply HLSL uniform initializers, and if the light colors
            // come up zero the pixel shader returns black (an "invisible" cat).
            _catEffect = Content.Load<Effect>("Effects/CatNormalMap");
            _catEffect.Parameters["DiffuseTexture"].SetValue(_catSourceMesh.DiffuseTexture);
            _catEffect.Parameters["NormalTexture"].SetValue(_catSourceMesh.NormalMapTexture ?? FlatNormalMap());
            _catEffect.Parameters["AmbientColor"].SetValue(new Vector3(0.5f, 0.5f, 0.5f));
            _catEffect.Parameters["KeyLightDirection"].SetValue(new Vector3(0.5265408f, 0.5735765f, 0.6275069f));
            _catEffect.Parameters["KeyLightColor"].SetValue(new Vector3(1.0f, 0.9607844f, 0.8078432f));
            _catEffect.Parameters["FillLightDirection"].SetValue(new Vector3(-0.7198464f, -0.3420201f, -0.4545195f));
            _catEffect.Parameters["FillLightColor"].SetValue(new Vector3(0.9647059f, 0.9607844f, 0.945098f));
            _catEffect.Parameters["BackLightDirection"].SetValue(new Vector3(-0.4545195f, 0.3420201f, -0.7198464f));
            _catEffect.Parameters["BackLightColor"].SetValue(new Vector3(0.8078432f, 0.8039216f, 0.7647059f));
            _catEffect.Parameters["SpecularStrength"].SetValue(0.2f);
            _catEffect.Parameters["SpecularShininess"].SetValue(16f);
            _catEffect.Parameters["Tint"].SetValue(Vector3.One);  // push every uniform from C#

            _critterModelTransform = BuildCritterModelTransform(_catSourceMesh);
        }

        private string CatModelPath() => Path.Combine(Content.RootDirectory, "Models", "Cat.obj");

        // Each critter animates its own copy of the geometry, because
        // CatAnimator rewrites mesh vertices in place every frame. Sharing one
        // mesh would mean every cat in frame shows whichever one was drawn last.
        // The geometry is parsed per cat; the textures and the effect are shared.
        private void LoadCritterModels()
        {
            foreach (var critter in _critters)
            {
                if (_catMeshes.ContainsKey(critter)) continue;
                var mesh = ObjLoader.ParseGeometry(CatModelPath());
                _catMeshes[critter] = mesh;
                _catAnims[critter] = new CatAnimator(mesh);
            }
        }

        // A 1x1 blue (0,0,1) normal map — "no bump" — used when the model has
        // no map_Bump so the shader always has something valid to sample.
        private Texture2D FlatNormalMap()
        {
            if (_flatNormalMap == null)
            {
                _flatNormalMap = new Texture2D(GraphicsDevice, 1, 1, false, SurfaceFormat.Color);
                _flatNormalMap.SetData(new[] { new Color(128, 128, 255) });
            }
            return _flatNormalMap;
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
        //      the +Z side. This is what makes FacingAngle=0 in Critter.cs
        //      correctly mean "facing +Z."
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
            // Pointer capture follows the camera mode. Set it before polling so
            // KeyboardMouseInput knows whether to report a relative look delta.
            // (One frame of lag after a toggle is harmless.)
            bool firstPerson = _camera.Mode == CameraViewMode.FirstPerson;
            _input.MouseLookEnabled = firstPerson;
            _input.MouseLookCenter = new Point(
                GraphicsDevice.Viewport.Width / 2, GraphicsDevice.Viewport.Height / 2);
            IsMouseVisible = !firstPerson;

            _input.Update(gameTime);
            var input = _input.State;

            if (input.Exit) Exit();

            _camera.Update(gameTime, input, _player.WorldPos, _player.FacingAngle);

            if (_phase == RunPhase.Playing)
            {
                _elapsedTime += (float)gameTime.ElapsedGameTime.TotalSeconds;
                UpdatePlay(gameTime, input);
            }
            else if (input.Restart)
            {
                StartRound();
            }

            base.Update(gameTime);
        }

        /// <summary>
        /// Advances the simulation one frame while a round is in progress.
        ///
        /// Order matters: the player moves first (so the throw origin and the
        /// critter AI see the new position), then the ball is thrown/updated,
        /// then critters act on the post-move world, then loot and the alarm.
        /// Win/lose is checked last so a same-frame secure-and-trip resolves as
        /// a loss (the alarm is the harder constraint).
        /// </summary>
        private void UpdatePlay(GameTime gameTime, in InputState input)
        {
            float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;

            // Isometric walks the grid cell to cell; first-person moves the
            // player freely (camera-relative) with wall sliding.
            bool freeMove = _camera.Mode == CameraViewMode.FirstPerson;
            Vector2 moveDir = freeMove ? _camera.MapMoveToWorld(input) : input.Move;
            _player.Update(gameTime, _map, moveDir, freeMove);

            // Throwing. Uses the camera's facing direction as the aim, which
            // works in both first-person and isometric modes.
            if (input.Throw && _ball.State == BallState.Held)
            {
                Vector3 eye = _player.WorldPos + new Vector3(0f, 1.2f, 0f);
                _ball.Throw(eye, _camera.FacingOnPlane);
                Sfx.Play(SfxId.Throw, volume: 0.4f);
            }

            _ball.Update(dt, _map, _player.Cell);
            _ballCell = _ball.Active ? _ball.Cell : null;

            // A bounce is the ball announcing itself, which is the whole point
            // of throwing it: the cats hear it.
            if (_ball.Active && _ball.State == BallState.Thrown)
                Sfx.PlayAt(SfxId.Bounce, _player.WorldPos, _ball.Position, 18f, 0.5f);

            // A ball that reaches a cat stuns it: the cat drops whatever it was
            // carrying and sits out the stun, but stays in the game. The ball
            // explodes on impact, then returns to the player.
            if (_ball.State == BallState.Thrown)
            {
                foreach (var critter in _critters)
                {
                    if (critter.Stunned || critter.Cell != _ball.Cell) continue;

                    critter.Stun(3f);
                    _ball.Explode();
                    Sfx.Play(SfxId.Tackle, volume: 0.5f);

                    if (critter.Carrying)
                    {
                        _items.DropFrom(critter, critter.Cell);
                        critter.Carrying = false;
                        Sfx.Play(SfxId.Drop, volume: 0.5f);
                        _alarm.Ping();
                    }
                    break;
                }
            }

            UpdateCritters(gameTime);
            UpdateLoot(dt);
            UpdateHeartbeat(dt);
            _hud.Update(dt);

            _alarm.Update(dt, _items.StolenCount, _items.Held != null, _items.AllSecured);
            UpdateAlarmAudio(dt);

            if (_alarm.Tripped)
            {
                EndRound(RunPhase.Lost, "The alarm went off.");
            }
            else if (_items.AllSecured)
            {
                EndRound(RunPhase.Won, "Everything's back on the shelf.");
            }
        }

        // The alarm meter is the round's clock, so it gets an audible clock too:
        // a blip whose rate climbs as it fills. Starting only once the meter is
        // past a third keeps the early, quiet part of a round silent.
        /// <summary>
        /// Drives the alarm blip sound, whose rate climbs as the meter fills.
        ///
        /// Starting only once the meter is past a third keeps the early, quiet
        /// part of a round silent. The interval lerps from 0.9s (at one third
        /// full) down to about 0.25s (at full).
        /// </summary>
        private void UpdateAlarmAudio(float dt)
        {
            if (_alarm.Cause == AlarmCause.Calm || _alarm.Level < 0.33f)
            {
                _blipTimer = 0f;
                return;
            }

            // 0.9s between blips at a third full, down to about 0.25s at the top.
            float urgency = MathHelper.Clamp((_alarm.Level - 0.33f) / 0.67f, 0f, 1f);
            _blipTimer -= dt;
            if (_blipTimer <= 0f)
            {
                _blipTimer = MathHelper.Lerp(0.9f, 0.25f, urgency);
                Sfx.Play(SfxId.AlarmBlip, volume: 0.25f + 0.3f * urgency);
            }
        }

        private float _blipTimer;

        /// <summary>
        /// The proximity cue: a heartbeat that quickens and rises as the nearest
        /// cat closes in, and stops the moment it's out of range.
        ///
        /// This is the one piece of information the isometric camera can't give
        /// you cheaply — a cat two aisles away is off the edge of a zoomed-in
        /// view, and a shelf maze has a lot of blind corners. It costs one
        /// timer, and it means "something is right behind me" is a thing you
        /// can hear rather than something you have to notice.
        /// </summary>
        /// <summary>
        /// The proximity cue: a heartbeat that quickens and rises as the
        /// nearest cat closes in, and stops the moment it's out of range.
        ///
        /// This is the one piece of information the isometric camera can't give
        /// you cheaply — a cat two aisles away is off the edge of a zoomed-in
        /// view, and a shelf maze has a lot of blind corners. It costs one
        /// timer, and it means "something is right behind me" is a thing you
        /// can hear rather than something you have to notice.
        /// </summary>
        private void UpdateHeartbeat(float dt)
        {
            float nearest = float.MaxValue;
            foreach (var critter in _critters)
            {
                float d = Vector3.Distance(_player.WorldPos, critter.WorldPos);
                if (d < nearest) nearest = d;
            }

            if (nearest > HeartbeatRange)
            {
                _heartTimer = 0f;
                return;
            }

            // 0 = at the edge of range, 1 = right on top of you.
            float closeness = MathHelper.Clamp(1f - nearest / HeartbeatRange, 0f, 1f);

            // ~1.15s between beats when distant, ~0.42s when it's on you. The
            // curve is eased so the change is gradual at first: the alarm should
            // creep up, not flip from calm to panic.
            float eased = closeness * closeness;
            _heartTimer -= dt;
            if (_heartTimer <= 0f)
            {
                _heartTimer = MathHelper.Lerp(1.15f, 0.42f, eased);
                // Louder *and* higher pitched as it closes, so it reads as
                // panic rather than as a volume change.
                Sfx.Play(SfxId.Heartbeat,
                    volume: 0.10f + 0.34f * eased,
                    pitch: -0.25f + 0.45f * eased);
            }
        }

        private const float HeartbeatRange = 9f; // world units
        private float _heartTimer;

        /// <summary>
        /// Advances every critter one frame: updates its awareness (can it see
        /// the player, is it being watched), asks the AI for a move, drives the
        /// animation rig, and removes it from the game if it was tackled.
        ///
        /// The <see cref="CritterLocus"/> is rebuilt each frame because it
        /// captures the current ball cell, loot target and stolen count — all of
        /// which can change at any moment.
        /// </summary>
        private void UpdateCritters(GameTime gameTime)
        {
            float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;

            // Rebuilt each frame: it's a struct of two delegates, and it keeps
            // the per-critter data (sight, being watched) out of the shared
            // context.
            var locus = new CritterLocus(
                Map: _map,
                PlayerCell: _player.Cell,
                Ball: _ballCell,
                LootTarget: _lootTarget,
                StolenCount: _items.StolenCount,
                LootUnderfoot: c => _items.LooseAt(c.Cell),
                TrySteal: c => TrySteal(c));

            for (int i = _critters.Count - 1; i >= 0; i--)
            {
                var critter = _critters[i];
                UpdateAwareness(critter);
                critter.Update(gameTime, locus);

                // Drive the rig here, in the update loop, not in Draw: it
                // rewrites the mesh's vertices in place (see CatAnimator), so
                // it is a state change and belongs with the other updates.
                // The head tracks the player once they've been noticed, which
                // is what gives the cat a sense of seeing you.
                if (_catAnims.TryGetValue(critter, out var anim))
                    anim.Update(BuildCatAnimInput(critter, dt));

                if (critter.Tackled)
                {
                    // A tackle makes it drop whatever it had, right here.
                    bool hadLoot = critter.Carrying;
                    _items.DropFrom(critter, critter.Cell);
                    critter.Carrying = false;
                    _critters.RemoveAt(i);
                    _catAnims.Remove(critter);
                    _catMeshes.Remove(critter);
                    Sfx.Play(SfxId.Tackle, volume: 0.7f);
                    if (hadLoot)
                    {
                        Sfx.Play(SfxId.Drop, volume: 0.6f);
                        // Recovering the goods is a small win; let the meter off
                        // a little so a clean tackle feels like progress.
                        _alarm.Ping();
                    }
                }
            }
        }

        // Works out, per critter, whether it can see the player and whether the
        // player is looking at it. Both feed the AI, and the first-person camera
        // being a way to pin a Nervous cat depends on the second.
        /// <summary>
        /// Updates what a critter knows about the player this frame.
        ///
        /// <see cref="Critter.CanSeePlayer"/> is line-of-sight plus the critter's
        /// vision radius — it gates the AI's alert/flee behaviour.
        /// <see cref="Critter.BeingWatched"/> is whether the camera's view cone
        /// contains the critter — it's what makes a Nervous cat freeze.
        ///
        /// The two are deliberately separate: the first is what the AI reacts to,
        /// the second is what pins a Nervous cat in place.
        /// </summary>
        private void UpdateAwareness(Critter critter)
        {
            int dist = Math.Abs(critter.Cell.X - _player.Cell.X) + Math.Abs(critter.Cell.Y - _player.Cell.Y);
            bool couldSee = dist <= critter.Profile.VisionRadius &&
                            _map.HasLineOfSight(critter.Cell, _player.Cell);

            // The first frame it notices you, it says so. Gated per critter so
            // it doesn't meow every frame while standing in your eyeline.
            if (couldSee && !critter.CanSeePlayer) Sfx.Play(SfxId.Meow, volume: 0.35f);

            critter.CanSeePlayer = couldSee;

            // Judged from the player's eye: at head height in first person, and
            // from above in the isometric view (where the 90-degree cone means
            // anything in the store counts as visible, which is exactly the
            // asymmetry that makes first-person worth switching to).
            float eyeHeight = _camera.Mode == CameraViewMode.FirstPerson
                ? Camera.FirstPersonEyeHeight
                : 1f;
            critter.BeingWatched =
                _camera.IsInViewCone(_player.WorldPos + new Vector3(0f, eyeHeight, 0f), critter.WorldPos);
        }

        /// <summary>
        /// Performs the actual loot transfer when a critter decides to steal.
        ///
        /// The AI only *decides* to steal (see <see cref="ICritterAI.WantsToSteal"/>);
        /// this method performs the transfer so possession rules stay out of
        /// the AI. Returns true if something was taken.
        /// </summary>
        private bool TrySteal(Critter critter)
        {
            var item = _items.LooseAt(critter.Cell);
            if (item == null) return false;

            _items.TakeByCritter(item, critter);
            _alarm.Ping();
            Sfx.Play(SfxId.Steal, volume: 0.5f);
            EventSystem.Instance.Publish(new ItemStolenEvent(critter.Profile.Name));
            return true;
        }

        // Player-side loot: pick up what's underfoot, secure it at the counter.
        /// <summary>
        /// Handles player-side loot interactions and per-frame item positions.
        ///
        /// Pickup is a rising action (step onto a loose item while hands are
        /// free). Secure is a state (stand on the counter while holding, or
        /// drop a loose item onto the counter). Item positions are then updated
        /// so carried/held items follow their carrier and secured items stack on
        /// the counter.
        /// </summary>
        private void UpdateLoot(float dt)
        {
            if (_items.Held == null)
            {
                var picked = _items.TryPickUp(_player.Cell);
                if (picked != null)
                {
                    _alarm.Ping();
                    Sfx.Play(SfxId.PickUp, volume: 0.6f);
                }
            }

            if (_items.TrySecure(_player.Cell))
            {
                _alarm.Ping();
                Sfx.Play(SfxId.Secure, volume: 0.7f);
                EventSystem.Instance.Publish(new ItemSecuredEvent());
            }

            _items.Update(dt, _player.Cell, CarryHeight);
            _lootTarget = _items.NearestLoose(_player.Cell)?.Cell;
        }

        /// <summary>
        /// Ends the current round with a win or loss, plays the appropriate
        /// sting, publishes the corresponding event, and records the final
        /// time / secured count for the end banner.
        /// </summary>
        private void EndRound(RunPhase phase, string message)
        {
            _phase = phase;
            _endMessage = message;
            Sfx.Play(phase == RunPhase.Won ? SfxId.Win : SfxId.Lose, volume: 0.8f);
            if (phase == RunPhase.Won)
                EventSystem.Instance.Publish(new GameWonEvent(_elapsedTime));
            else
                EventSystem.Instance.Publish(new GameLostEvent(_elapsedTime, _items.SecuredCount));
        }

        private string _endMessage = "";

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
            DrawMesh(_floorVerts, Matrix.Identity, _textures.Floor);

            // Shelves: one cube per blocked tile. The counter row is drawn in a
            // different material so the goal is visible from across the store.
            for (int y = 0; y < _map.Height; y++)
            {
                for (int x = 0; x < _map.Width; x++)
                {
                    if (_map[x, y] == 1)
                    {
                        var world = Matrix.CreateScale(CellSize * 0.95f, CellSize * 1.5f, CellSize * 0.95f)
                                    * Matrix.CreateTranslation(x * CellSize, 0f, y * CellSize);
                        DrawMesh(_wallVerts, world, _textures.Shelf);
                    }
                }
            }

            DrawCounter();
            DrawPlayer(gameTime);
            DrawLoot();
            DrawBall();
            DrawCritters(gameTime);
            _hud.Draw(BuildHudState());

            base.Draw(gameTime);
        }

        // The till: a low slab along the counter cells. Drawn as a flat
        // decal-ish box rather than a shelf so it doesn't block sightlines.
        /// <summary>
        /// Draws the front counter as a low slab along the counter cells.
        ///
        /// Drawn as a flat decal-ish box rather than a shelf so it doesn't
        /// block sightlines. The counter is the player's goal — items secured
        /// here count toward winning.
        /// </summary>
        private void DrawCounter()
        {
            for (int x = GridMap.CounterStart; x <= GridMap.CounterEnd; x++)
            {
                if (!_map.IsCounter(x, GridMap.CounterRow)) continue;
                var world = Matrix.CreateScale(CellSize * 0.9f, GridMap.CounterHeight, CellSize * 0.5f)
                            * Matrix.CreateTranslation(x * CellSize, 0f, GridMap.CounterRow * CellSize);
                DrawMesh(_propVerts, world, _textures.Counter);
            }
        }

        // Loot as a small cardboard box per item, scaled up a little when
        // secured so the recovered pile reads at a glance. Anything a critter
        // is carrying is drawn with that critter instead (see DrawCritters) —
        // what a cat is holding is the single most important thing on screen.
        /// <summary>
        /// Draws every loot item that isn't being carried by a critter.
        ///
        /// Items a critter is carrying are drawn with that critter instead (see
        /// <see cref="DrawCritters"/>) — what a cat is holding is the single
        /// most important thing on screen. Secured items are scaled up slightly
        /// so the recovered pile reads at a glance.
        /// </summary>
        private void DrawLoot()
        {
            foreach (var item in _items.Items)
            {
                if (item.State == LootState.Carried) continue;

                DrawLootBox(item.WorldPos, item.Tint, CellSize * (item.IsSecured ? 0.34f : 0.28f));
            }
        }

        private void DrawLootBox(Vector3 position, Color tint, float size)
        {
            var world = Matrix.CreateScale(size, size, size) * Matrix.CreateTranslation(position);
            DrawMesh(_propVerts, world, _textures.Cardboard, tint);
        }

        /// <summary>
        /// Draws the ball and its particles.
        ///
        /// The ball itself is only visible while <see cref="BallState.Thrown"/>
        /// — during Exploding/Paused/Returning the particles are the show.
        /// The ball spins about the axis perpendicular to its travel so it
        /// reads as rolling rather than sliding.
        /// </summary>
        private void DrawBall()
        {
            if (_ball.State == BallState.Held) return;

            // Explosion / return particles: small cubes flying out or converging.
            foreach (var p in _ball.Particles)
            {
                var particleWorld = Matrix.CreateScale(0.12f, 0.12f, 0.12f)
                            * Matrix.CreateTranslation(p.Position);
                DrawMesh(_propVerts, particleWorld, _textures.Flat, p.Color);
            }

            // The ball itself is hidden while exploding / paused / returning —
            // the particles are the show during those states.
            if (_ball.State != BallState.Thrown) return;

            // A rolling ball: spin it about the axis perpendicular to its travel
            // so it reads as rolling rather than sliding.
            Vector3 travel = _ball.Velocity;
            travel.Y = 0f;
            float speed = travel.Length();
            var spin = speed > 0.01f
                ? Matrix.CreateFromAxisAngle(Vector3.Normalize(Vector3.Cross(Vector3.Up, travel)), speed * 0.12f)
                : Matrix.Identity;

            float size = Ball.BallRadius * 2f;
            var world = spin
                        * Matrix.CreateScale(size, size, size)
                        * Matrix.CreateTranslation(_ball.Position);

            DrawMesh(_propVerts, world, _textures.Flat, new Color(226, 92, 78));
        }

        /// <summary>
        /// Draws the procedural humanoid player.
        ///
        /// The walk cycle is advanced while moving and eased back to neutral
        /// when idle, so limbs don't freeze mid-stride. Each limb swings around
        /// its joint pivot (shoulder/hip) — see <see cref="PersonMesh"/> for
        /// the part layout. The player is skipped entirely in first-person mode
        /// (the camera sits inside the head).
        /// </summary>
        private void DrawPlayer(GameTime gameTime)
        {
            // The first-person camera sits inside the player's head, so skip
            // the player's own body (it would only clip through the view).
            if (_camera.Mode == CameraViewMode.FirstPerson) return;

            float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;

            // Advance the walk cycle while moving; ease back to a neutral pose
            // (swing = sin(0) = 0) when idle so limbs don't stick mid-stride.
            if (_player.IsMoving)
                _walkPhase += dt * WalkCycleSpeed;
            else
                _walkPhase = MathHelper.Lerp(_walkPhase, 0f, 1f - MathF.Exp(-8f * dt));

            float swing = MathF.Sin(_walkPhase) * 0.4f;

            var facing = Matrix.CreateRotationY(_player.FacingAngle);
            var placement = Matrix.CreateTranslation(_player.WorldPos);

            foreach (var part in _playerParts)
            {
                Matrix world;
                if (part.SwingSign != 0f)
                {
                    // Swing the limb around its joint (row-vector convention:
                    // compose left-to-right, so the swing is applied in the
                    // limb's part space first, then the joint is moved into
                    // the body, then the body is faced and placed).
                    world = Matrix.CreateRotationX(swing * part.SwingSign)
                          * Matrix.CreateTranslation(part.Pivot)
                          * facing * placement;
                }
                else
                {
                    world = facing * placement;
                }
                DrawMesh(part.Vertices, world, _textures.Flat);
            }
        }

        // Draws every critter. Each has its own mesh and rig (see
        // LoadCritterModels) but they share the effect, its textures and its
        // parameters, which are re-pushed per cat.
        /// <summary>
        /// Draws every critter with its own mesh, rig and shader parameters.
        ///
        /// Each critter has its own parsed copy of the cat geometry (~36k
        /// vertices) because the animation rig rewrites vertices in place each
        /// frame — sharing one mesh would make every cat adopt the last one
        /// drawn. The world matrix order is: normalize the raw model, scale to
        /// the species' size, face the travel direction, then place in the
        /// world.
        /// </summary>
        private void DrawCritters(GameTime gameTime)
        {
            Vector3 cameraPos = Matrix.Invert(_camera.View).Translation;

            foreach (var critter in _critters)
            {
                if (!_catMeshes.TryGetValue(critter, out var mesh)) continue;

                // Order matters: normalize the raw model (transform), scale to
                // this species' size, face its travel direction (rotation),
                // then place it in the world (translation) — each stage operates
                // in the space the previous one produced.
                float scale = critter.Profile.Scale;
                var world = _critterModelTransform
                            * Matrix.CreateScale(scale)
                            * Matrix.CreateRotationY(critter.FacingAngle)
                            * Matrix.CreateTranslation(critter.WorldPos);

                _catEffect.Parameters["World"].SetValue(world);
                _catEffect.Parameters["Tint"].SetValue(critter.Profile.TintVector);
                _catEffect.Parameters["View"].SetValue(_camera.View);
                _catEffect.Parameters["Projection"].SetValue(_camera.Projection);
                _catEffect.Parameters["ViewDirection"].SetValue(cameraPos - world.Translation);

                // The mip chains are built by ObjLoader; anisotropic filtering
                // is applied here (after pass.Apply, which binds the effect's
                // default sampler state) so the diffuse and normal maps stop
                // aliasing when the cat is small on screen. Slots match the
                // shader's s0/s1.
                foreach (var pass in _catEffect.CurrentTechnique.Passes)
                {
                    pass.Apply();
                    GraphicsDevice.SamplerStates[0] = SamplerState.AnisotropicClamp;
                    GraphicsDevice.SamplerStates[1] = SamplerState.AnisotropicClamp;
                    GraphicsDevice.DrawUserIndexedPrimitives(
                        PrimitiveType.TriangleList,
                        mesh.Vertices, 0, mesh.Vertices.Length,
                        mesh.Indices, 0, mesh.Indices.Length / 3);
                }

                // Whatever it's holding, drawn after the body so it sits in
                // front of the cat rather than inside it. ItemField keeps the
                // item's WorldPos on the carrier's shoulder, so this just
                // follows the critter.
                if (critter.Carrying)
                {
                    var carried = _items.Items.FirstOrDefault(i => i.Carrier == critter);
                    if (carried != null) DrawLootBox(carried.WorldPos, carried.Tint, CellSize * 0.26f);
                }
            }
        }

        // Assembles the per-frame cat animation request. The head-look yaw is
        // expressed in the cat's own frame (0 = its facing, + = to its right)
        // because the rig works in model space, and the model's +X axis ends up
        // as the cat's left after BuildCritterModelTransform.
        /// <summary>
        /// Assembles the per-frame animation request for a critter.
        ///
        /// The head-look yaw is expressed in the cat's own frame (0 = its
        /// facing, + = to its right) because the rig works in model space, and
        /// the model's +X axis ends up as the cat's left after
        /// <see cref="BuildCritterModelTransform"/>. A stunned cat is forced
        /// into the Startle pose regardless of what the AI last decided.
        /// </summary>
        private CatAnimInput BuildCatAnimInput(Critter critter, float dt)
        {
            Vector3 toPlayer = _player.WorldPos - critter.WorldPos;
            toPlayer.Y = 0f;

            // Only track the player once the cat has actually noticed them.
            bool hasTarget = toPlayer.LengthSquared() > 0.01f && critter.CanSeePlayer;
            float yaw = 0f, pitch = 0f;
            if (hasTarget)
            {
                float bearing = MathF.Atan2(toPlayer.X, toPlayer.Z);
                // Normalize the difference into -pi..pi so the head never spins
                // the long way round.
                yaw = -MathF.Atan2(MathF.Sin(bearing - critter.FacingAngle),
                                   MathF.Cos(bearing - critter.FacingAngle));
                // The player's head is above the cat's, so looking at them is a
                // slight upward tilt.
                pitch = MathHelper.Clamp(Camera.FirstPersonEyeHeight * 0.5f / MathF.Max(1f, toPlayer.Length()),
                                         -0.4f, 0.4f);
            }

            // A stunned cat stands still and looks startled, whatever the AI last
            // wanted it to do.
            var pose = critter.Stunned ? CatPose.Startle : critter.Pose;
            bool moving = critter.IsMoving && !critter.Stunned;
            return new CatAnimInput(pose, moving, hasTarget, yaw, pitch, dt);
        }

        /// <summary>
        /// Draws a mesh with the shared <see cref="BasicEffect"/>.
        ///
        /// <see cref="BasicEffect.DiffuseColor"/> is sticky state that
        /// multiplies the texture, so it is reset to white (or the supplied
        /// tint) on every draw — a tinted prop would otherwise leave the
        /// shelves behind it in the same colour. The sampler is set to
        /// anisotropic wrap because every generated texture has a complete mip
        /// chain and the tilted perspective view looks along the floor at a
        /// grazing angle.
        /// </summary>
        private void DrawMesh(
            VertexPositionNormalColorTexture[] verts, Matrix world, Texture2D texture, Color? tint = null)
        {
            _effect.World = world;
            _effect.Texture = texture;

            // BasicEffect's DiffuseColor is sticky state that multiplies the
            // texture, so it has to be reset every draw or a tinted prop would
            // leave the shelves behind it in the same colour.
            _effect.DiffuseColor = tint.HasValue
                ? new Vector3(tint.Value.R / 255f, tint.Value.G / 255f, tint.Value.B / 255f)
                : Vector3.One;
            // Tiling surfaces (floor grout, shelf boards) need a wrapping
            // sampler, and anisotropic filtering since the tilted perspective
            // view looks along the floor at a grazing angle. Both are safe
            // because every generated texture has a complete mip chain.
            GraphicsDevice.SamplerStates[0] = SamplerState.AnisotropicWrap;

            foreach (var pass in _effect.CurrentTechnique.Passes)
            {
                pass.Apply();
                GraphicsDevice.DrawUserPrimitives(PrimitiveType.TriangleList, verts, 0, verts.Length / 3);
            }
        }

        // Snapshots everything the HUD needs for one frame. The HUD is
        // read-only: it draws the state, it never writes it back. Passing a
        // snapshot rather than the subsystems themselves is what keeps the
        // HUD from reaching into the game.
        private HudState BuildHudState() => new(
            Phase: _phase,
            ElapsedTime: _elapsedTime,
            EndMessage: _endMessage,
            SecuredCount: _items.SecuredCount,
            TotalCount: _items.TotalCount,
            PlayerCarrying: _items.Held != null,
            StolenCount: _items.StolenCount,
            AlarmCause: _alarm.Cause,
            AlarmLevel: _alarm.Level,
            ViewMode: _camera.Mode,
            ViewProjection: _camera.ViewProjection,
            Critters: _critters,
            TitlePhase: _title.Phase);

        protected override void UnloadContent()
        {
            _title?.Dispose();
            _textures?.Dispose();
            base.UnloadContent();
        }
    }
}
