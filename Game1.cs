using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

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

        private const float CellSize = GameConstants.CellSize; // world units per grid cell
        private const float WalkCycleSpeed = GameConstants.WalkCycleSpeed; // radians/sec of the player's limb swing

        private GridMap _map = null!;
        private Player _player = null!;

        // A whole cast of them, not one: the store is a heist, and which cats
        // turn up changes the round. Tackled critters are retired (Tackle sets
        // the flag) and removed from the list.
        private readonly List<Critter> _critters = new();
        private Random _rng = new();

        // What the cats know about the player, shared between them. One cat
        // spotting the player has to matter to the others, or each cat is an
        // island and the store feels dead.
        private readonly SharedAwareness _awareness = new();

        // What the store hears of the player: one pop per cell crossed, plus
        // the clatter when a spill takes you down. Pure state (see
        // NoiseSystem.cs) — it feeds SharedAwareness, which folds hearing
        // into the same vagueness a shouted warning carries.
        private readonly NoiseSystem _noise = new();

        private readonly ComboSystem _combo = new(ComboSystem.DefaultWindow);

        // The securing combo: consecutive secures within a short window
        // multiply the coin payout.
        private Difficulty _difficulty = Difficulty.Normal;
        private FriendRoster _roster = null!;

        // Security cameras watching the aisles.
        private CameraField _cameras = null!;

        private bool _roundHadTheft;
        private bool _roundAlarmCritical;
        private float _alarmRateScaleBase;

        // The objective: loot, the alarm that fills while it's missing, and the
        // ball you can throw to move a cat off whatever it's doing.
        private ItemField _items = null!;
        // Per-round shopping order: kinds the player is asked to secure for a
        // bonus. The rules (and the HUD line) live in ShoppingOrder, so the
        // payout can be tested without playing a round.
        private readonly ShoppingOrder _order = new();
        private AlarmSystem _alarm = null!;
        private Ball _ball = null!;

        // Power-ups: three one-use items per round that go in a one-slot
        // pocket, and the timers for the two that are effects rather than
        // instant actions.
        private PowerUpField _powerups = null!;
        private readonly PowerUpInventory _pocket = new();
        private float _skateTimer;
        private float _treatTimer;
        private GridPos _treatCell;

        // The shift itself: the pure scheduler (when/which) plus the two
        // timers its events set (how long the lights stay out, how long the
        // rat runs). The effects live in ApplyShiftEvent.
        private ShiftEventSystem _shift = null!;
        private float _darkTimer;
        private float _ratTimer;
        private GridPos _ratCell;
        private float _dayPhase;
        private float _rainTimer;
        private float _rainActive;
        private float _fogTimer;
        private float _fogActive;

        // Floor spills and the one-shot fire alarm, plus where the player was
        // last frame (a spill triggers on *entering* its cell, not every
        // frame you stand on it).
        private HazardField _hazards = null!;
        private GridPos _wasPlayerCell;

        // The juice layer: particles, floating texts, and the hit-stop timer
        // that briefly crawls the simulation on a hard impact.
        private readonly FxSystem _fx = new();
        private float _hitStop;

        // The run: which store is next, the purse, and every upgrade bought
        // between rounds. Survives rounds — only StartRound reads it, so the
        // campaign itself stays headless (see Campaign.cs).
        private CampaignState _campaign = new();

        private BasicEffect _effect = null!;
        private Camera _camera = null!;
        private InputSystem _input = null!;
        private RainbowTitle _title = null!;
        private AchievementTracker _achievements = null!;

        // The screen-space overlay. It owns no game state: Game1 snapshots
        // what it needs each frame (see BuildHudState) and hands it over.
        // Assigned in LoadContent, once its graphics dependencies exist.
        private Hud _hud = null!;

        // Cube/floor meshes, built once and reused (just repositioned via world matrix).
        private VertexPositionNormalColorTexture[] _wallVerts = null!; // border ring
        private VertexPositionNormalColorTexture[] _floorVerts = null!;
        // Shelf units, indexed by [ShelfSlot(style)][variant] — one mesh per
        // style, built once in LoadContent (see ShelfMesh.Build).
        private VertexPositionNormalColorTexture[][][] _shelfVerts = null!;
        private PersonPart[] _playerParts = null!;

        // Shoppers: harmless bodies that wander the aisles. One per round
        // is a shoplifter, who can pocket a loose item and make for the
        // exit. Pure state (Customer.cs + CustomerBrain.cs), drawn with the
        // same PersonMesh parts as the player.
        private readonly List<GridPos> _traps = new();
        private float _laserTimer;
        private GridPos _laserCell;
        private readonly List<Customer> _customers = new();
        private PersonPart[] _customerParts = null!;
        private readonly List<GridPos> _exits = new();
    private GridPos _doorCell;
        private float _customerWalkPhase;
        private float _walkPhase; // drives the player's limb swing

        // Where the floor quad sits in the world. Grid cells are anchored at
        // cell * CellSize (a cell spans ±CellSize/2 around its anchor), so
        // the store really occupies [-CellSize/2, dim*CellSize - CellSize/2]
        // and the quad, authored from (0,0), is shifted back half a cell to
        // sit flush under the border walls. Camera clamps to the same frame.
        private static readonly Matrix FloorWorld =
            Matrix.CreateTranslation(-CellSize * 0.5f, 0f, -CellSize * 0.5f);

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

        private RunPhase _phase = RunPhase.Title;
        private bool _paused;
        private bool _showOptions;
        private bool _timeAttackMode;
        private bool _timeAttack;
        private float _timeLeft;
        private StoreLayout? _customLayout;
        private bool _customRound;
        private int _optionsSelection;
        private int _rebindIndex = -1;
        private string _rebindMessage = "";
        // The two overlay strings are built on change, not per frame: the
        // options panel is twenty lines of string building for a screen that
        // only changes when the player touches it.
        private string _optionsText = "";
        private bool _optionsTextDirty = true;
        private string? _pausedText;
        private float _optionsRepeat;
        private float _musicVolume = 1f;
        private float _sfxVolume = 1f;
        // True while the run in play came out of a save file, so the title
        // screen can offer a way back out to a fresh run.
        private bool _runResumed;
        private List<(string Name, Func<Keys> Get, Action<Keys> Set)> _bindings = new();

    // Attract mode: after a few seconds idle on the title screen the game
    // plays a demo round by itself for a couple of minutes, then returns to
    // the attract screen. Any input exits the demo.
    private bool _attractMode;
    private float _attractIdleTimer;
    private float _demoTimer;
    private const float AttractIdleSeconds = 8f;
    private const float DemoDurationSeconds = 150f;
    private Vector2 _demoMoveDir = Vector2.Zero;
        private float _elapsedTime = 0f;

        // Cached per-frame values shared by the critters and the HUD.
        private GridPos? _ballCell;
        private GridPos? _lootTarget;
        private LootItem? _lootTargetItem;

        public const int LootCount = 5;
        public const int CritterCount = 3;
        public const float CarryHeight = GameConstants.CarryHeight;

        // ---- Power-ups ---------------------------------------------------
        // How long the skates last and how much they give. 1.5x is noticeable
        // (a tile takes 0.44s instead of 0.67s) without trivialising the
        // alarm, which is a timer about travel time.
        public const float SkatesSeconds = 8f;
        public const float SkatesSpeedBoost = 1.5f;

        // The spray's panic: how long a cat stays spooked, and how far the
        // hiss carries — walking distance, so a shelf between you and the cat
        // genuinely stops it hearing the spray.
        public const float SprayPanicSeconds = 4f;
        public const int SprayPanicRadius = 5;

        // A treat lingers where you drop it. The AI's own radius
        // (SmartAnimalAI.TreatRadius) decides how far away cats notice it.
        public const float TreatSeconds = 6f;

        /// <summary>
        /// How much of the alarm meter a thief reaching the counter costs.
        ///
        /// It has to be big enough that the escape is a real loss rather than
        /// a clever move — otherwise every thief would make for the till and
        /// dump the goods there, which is a lot easier than running away from
        /// the player. It also has to be survivable: the round has to stay
        /// winnable after one, because the player can still pick the goods up
        /// off the till and finish the job.
        /// </summary>
        public const float CounterDropAlarmSpike = 0.25f;

        // Pulling the fire alarm scatters every cat and drops their goods,
        // but you are pulling the fire alarm in a store that is already
        // watching you: a fixed fraction of the meter, same reasoning as the
        // counter-drop spike (an event, not a rate, so not scaled).
        public const float FireAlarmSpike = 0.15f;

        // Coins banked for a flawless round: nothing stolen, alarm never critical.
        public const int CleanRoundBonus = 25;

        // Tackles it takes to retire the Boss Cat. Three reads as "a fight"
        // without dragging the round out: each tackle costs the boss the loot
        // it was carrying, so every hit is progress even before the last.
        public const int BossHealth = 3;

        // Coins for completing the shopping order, before the securing combo
        // multiplier. Same scale as WinBonus, because it is the same kind of
        // "you finished what was asked" payout.
        public const int OrderBonus = 25;

        // How long a spill keeps the player sliding. Skates double it —
        // water and roller skates is a comedy combination, and the boost
        // should earn its risk.
        public const float SlipSeconds = 1.1f;

        // Noise multipliers on top of one plain footstep (NoiseSystem.
        // StepLoudness). Skates rattle — speed without sound would make the
        // shop's fastest item strictly better than walking; a skid is a
        // body hitting a shelf end, loud because it is out of control.
        public const float SkatesNoiseLoudness = 1.6f;
        public const float SlipClatterLoudness = 2.4f;

        // What the shift can throw at the store (see ShiftEvents.cs). The
        // power cut is the interesting one: it is a *stealth window* — the
        // cats keep a fraction of their vision and the store goes dark, so
        // the quiet player gets a few seconds of real leverage.
        public const float PowerCutSeconds = 8f;
        public const float DarkVisionFactor = 0.4f;
        public const float DarkLightFactor = 0.45f;
        public const float RatSeconds = 6f;
        public const float AlarmAuditFraction = 0.2f;
        public const int DeliveryCount = 2;

        // What the HUD's noise pip reads as "as loud as it gets": a skid
        // while skating (1.8 slip x 1.6 skates x ... close enough to round).
        public const float NoiseHudScale = 2.9f;

        // Hit-stop: this much real time with the simulation at this fraction
        // of its normal speed, on a hard impact. Small numbers — a hit-stop
        // you can count frames in is a game that feels broken, not punchy.
        public const float HitStopSeconds = 0.07f;
        public const float HitStopScale = 0.12f;

        public Game1()
        {
            _graphics = new GraphicsDeviceManager(this);
            Content.RootDirectory = "Content";
            IsMouseVisible = true;

            var diff = Environment.GetEnvironmentVariable("SC_DIFF");
            if (diff != null)
                _difficulty = diff.ToLowerInvariant() switch
                {
                    "easy" => Difficulty.Easy,
                    "hard" => Difficulty.Hard,
                    _ => Difficulty.Normal,
                };

            if (Environment.GetEnvironmentVariable("SC_DAILY") != null)
            {
                int seed = (int)(DateTime.UtcNow.Date.Ticks & 0x7FFFFFFF);
                _rng = new Random(seed);
                _difficulty = Difficulty.Hard;
            }
        }

        protected override void Initialize()
        {
            _graphics.PreferredBackBufferWidth = 1600;
            _graphics.PreferredBackBufferHeight = 1200;
            _graphics.ApplyChanges();

            _input = new InputSystem(new KeyboardMouseInput(), new GamePadInput());

            _title = new RainbowTitle();

            new RunStats(EventSystem.Instance);
            _achievements = new AchievementTracker(EventSystem.Instance);
            EventSystem.Instance.Subscribe<CatBefriendedEvent>(e =>
            {
                // map species name back to kind — find via profile name match
                foreach (var k in CritterProfile.AllKinds)
                    if (CritterProfile.For(k).Name == e.Species) { _roster.Befriend(k); break; }
            });

            // Restore a saved run, if any, before the first round is built.
            try
            {
                var savePath = SavePath();
                if (System.IO.File.Exists(savePath))
                {
                    var loaded = CampaignState.Load(savePath);
                    if (loaded != null) { _campaign = loaded; _runResumed = true; }
                }
            }
            catch { /* fall back to a fresh run */ }

            // Rebindings, likewise. A fresh install simply has no file.
            InputMapper.LoadDefaults();

            // Synthesised up front so the first cue doesn't stutter. Safe to
            // fail: Sfx turns itself off when there's no audio device.
            Sfx.Initialise();

            // The soundtrack likewise: it keys off Sfx.Available, so a
            // silent machine gets silence rather than a crash.
            Music.Start();

            TryLoadCustomLayout();

            StartRound(false);

            // Like SC_TILT / SC_FP: skip the attract screen and start the
            // round immediately. Synthetic key input is blocked in some
            // environments, so there is otherwise no way to screenshot the
            // store without a human pressing Enter.
            if (Environment.GetEnvironmentVariable("SC_PLAY") != null)
                _phase = RunPhase.Playing;

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

        // One floor tile every 2 world units (= CellSize), which lines the
        // grout up with the shelf grid — and with the floor drawn from
        // FloorWorld, that means the grout falls on cell boundaries. Rebuilt
        // when the campaign moves the game to a bigger store (LoadContent
        // builds the first one).
        private void RebuildFloor()
        {
            float floorWidth = _map.Width * CellSize;
            float floorDepth = _map.Height * CellSize;
            _floorVerts = CubeMesh.BuildFloorQuad(
                Color.White, floorWidth, floorDepth, floorWidth / CellSize, floorDepth / CellSize);
        }

        // Builds every shelf-unit style once: all aisle colours x end kinds
        // x open sides x product variants. A store draws ~150 of these per
        // frame, so they must be shared, and the count is tiny
        // (6 x 3 x 3 x 3 meshes of ~500 verts).
        private void BuildShelfMeshes()
        {
            const int variants = 3;
            int slots = ShelfMesh.AislePalette.Length * 9; // ends(3) x opens(3)
            _shelfVerts = new VertexPositionNormalColorTexture[slots][][];
            for (int aisle = 0; aisle < ShelfMesh.AislePalette.Length; aisle++)
            {
                for (int e = 0; e < 3; e++)
                {
                    for (int o = 0; o < 3; o++)
                    {
                        int slot = (aisle * 9) + (e * 3) + o;
                        var styles = new VertexPositionNormalColorTexture[variants][];
                        for (int v = 0; v < variants; v++)
                            styles[v] = ShelfMesh.Build(
                                new ShelfStyle(aisle, (ShelfEnd)e, (ShelfOpen)o, v));
                        _shelfVerts[slot] = styles;
                    }
                }
            }
        }

        // How BuildShelfMeshes packed a style into an array index.
        private static int ShelfSlot(in ShelfStyle style) =>
            (style.Aisle * 9) + ((int)style.End * 3) + (int)style.Open;

        // The style for one blocked cell, from its neighbours and row:
        //   - Aisle colour by row band: the generator's gondolas are two
        //     shelf rows per three rows, so (y-2)/3 gives one colour per
        //     run of shelves — the zone identity you read from the spine.
        //   - A sign where the run *terminates* (the neighbour that way is
        //     open). Both sides open means an isolated cell; it takes the
        //     +X sign, which is as good as any for a one-cell island.
        //   - Products only on sides that face an aisle; a side whose
        //     neighbour is also a shelf is interior to the gondola.
        //   - Product arrangement varies per cell so a run doesn't repeat
        //     identically every cell.
        private ShelfStyle ShelfStyleFor(int x, int y)
        {
            int aisle = Math.Max(0, (y - 2) / 3);

            ShelfEnd end = _map.IsWalkable(x + 1, y) ? ShelfEnd.PositiveX
                : _map.IsWalkable(x - 1, y) ? ShelfEnd.NegativeX
                : ShelfEnd.None;

            bool south = _map.IsWalkable(x, y + 1);
            bool north = _map.IsWalkable(x, y - 1);
            ShelfOpen open = south && north ? ShelfOpen.BothSides
                : south ? ShelfOpen.PositiveZ   // grid +y is world +Z
                : north ? ShelfOpen.NegativeZ
                : ShelfOpen.BothSides;          // fully enclosed: hidden either way

            int variant = ((x * 73 + y * 191 + x * y * 13) % 3 + 3) % 3;
            return new ShelfStyle(aisle, end, open, variant);
        }

        /// <summary>
        /// Pays out for any item that reaches the counter, however it got
        /// there: the player securing it, or a thief tackled at the till
        /// (whose drop-on-counter is a secure — see ItemField.DropFrom).
        /// Hooked to <see cref="ItemField.ItemSecured"/> so no path to the
        /// counter pays twice or not at all.
        /// </summary>
        private void OnItemSecured(LootItem item)
        {
            int mult = _combo.RegisterSecure(_elapsedTime);
            int coins = CampaignState.CoinsFor(item) * mult;
            _campaign.AddCoins(coins);

            // Shopping order fulfilment: a secured item that the store asked
            // for counts toward the bonus. Fulfilling the whole order pays out
            // and announces it, once per round — Fulfill is the edge detector,
            // so the payout cannot be paid twice.
            if (_order.Fulfill(item.Kind))
            {
                int bonus = OrderBonus * mult;
                _campaign.AddCoins(bonus);
                _fx.Text(item.WorldPos + new Vector3(0f, 2.4f, 0f),
                    $"ORDER COMPLETE  +{bonus}c", new Color(120, 230, 160), 2f);
                _fx.Burst(item.WorldPos + new Vector3(0f, 1.2f, 0f),
                    new Color(120, 230, 160), count: 18, speed: 2.8f, life: 1.0f, _rng);
            }

            // The purse line: gold, rising off the item where it landed.
            _fx.Text(item.WorldPos + new Vector3(0f, 1.8f, 0f),
                mult > 1 ? $"+{coins}c  x{mult}!" : $"+{coins}c",
                new Color(245, 205, 110), 1.5f);
            _camera.AddShake(0.08f);
            if (mult > 1) _fx.Burst(item.WorldPos + new Vector3(0f, 1.2f, 0f),
                new Color(255, 230, 120), count: 12, speed: 2.5f, life: 0.9f, _rng);
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
        private void StartRound(bool startPlaying = true)
        {
            var spec = _campaign.CurrentSpec;

            // A custom store is a one-off: consume the flag here rather than
            // leaving it set, or every later round silently replaces the
            // campaign's store with it (there is no way back to the title
            // screen from the between-rounds shop to clear it by hand).
            bool customRound = _customRound;
            _customRound = false;

            StoreLayout layout = (customRound && _customLayout != null) ? _customLayout : spec.Layout;

            _timeAttack = _timeAttackMode;
            _paused = false;
            _showOptions = false;
            _timeLeft = _timeAttack ? 90f + spec.LootCount * 5f : 0f;

            // A new round can be a new store. The camera is sized to the
            // floor plan and the floor quad/minimap bake it, so they are
            // rebuilt here — guarded, because the very first round runs in
            // Initialize, before LoadContent has built any of them (LoadContent
            // then builds them from whatever layout this branch left behind).
            if (_map == null || _map.Layout != layout)
            {
                _map = new GridMap(layout);

                // ItemField holds the map it was built for, so a new store
                // needs a new field. (The power-up and hazard fields take the
                // map per call, so they survive a move.) Subscribing here —
                // not once in Initialize — keeps the coin purse attached to
                // whichever field the round is actually using.
                _items = new ItemField(_map, CellSize, _rng);
                _items.ItemSecured += OnItemSecured;

                SetupCamera();

                if (_textures != null)
                {
                    RebuildFloor();
                    _hud.SetLayout(_map.Layout);
                }
            }

            _player = new Player(_map.Layout.PlayerStart, CellSize);
            _player.SpeedMultiplier = _campaign.SpeedFactor;

            _critters.Clear();

            // The meshes are keyed on the critter objects, and each is a full
            // copy of the cat's ~36k vertices, so dropping the cast has to drop
            // them too or every restart leaks one model per cat.
            _catAnims.Clear();
            _catMeshes.Clear();
            _roster ??= new FriendRoster(_rng);
            int critterCount = Math.Max(1, spec.CritterCount + (_difficulty == Difficulty.Hard ? 1 : _difficulty == Difficulty.Easy ? -1 : 0));
            int spawnIndex = 0;
            foreach (var cell in SpawnPoints(critterCount))
            {
                var profile = CritterProfile.Random(_rng);
                // Every fourth round (0-based round 3, 7, ...) features a
                // Boss Cat: a bigger, tougher Moggy that takes three tackles.
                // The flag is the boss's identity — everything downstream
                // (name, health, the HUD's bar) reads it rather than matching
                // on the display name, which is a presentation detail.
                bool isBoss = spawnIndex == 0 && _campaign.RoundIndex % 4 == 3;
                if (isBoss)
                {
                    var baseProfile = CritterProfile.For(CritterKind.Moggy);
                    profile = baseProfile with
                    {
                        Name = "Boss Cat",
                        Scale = 2.0f,
                        Tint = new Color(200, 60, 60),
                        MoveSpeed = 2.6f,
                        VisionRadius = 14,
                        AlertRadius = 14,
                    };
                }
                var ai = new SmartAnimalAI(profile, _rng);
                var critter = new Critter(cell, CellSize, ai, profile);
                critter.Name = isBoss ? profile.Name : _roster.NameFor(profile.Kind);
                if (isBoss) critter.MakeBoss(BossHealth);
                if (_roster.IsFriend(profile.Kind))
                    for (int i = 0; i < Critter.FriendThreshold; i++) critter.Feed();
                _critters.Add(critter);
                spawnIndex++;
            }

            _items.Spawn(spec.LootCount, _player.Cell);

            // Build this round's shopping order: a handful of the goods the
            // store actually wants back, for a bonus when fulfilled. It is a
            // bonus objective on top of securing everything — the round is
            // still won by clearing the shelves. ShoppingOrder picks the list
            // from what actually spawned and caches the HUD line.
            _order.Build(_items.Items, _rng);

            // One of each power-up, scattered away from the start. The pocket
            // starts empty every round — except for what the shop sold you:
            // consumables queue at the till and are pocketed here, one per
            // round, so two bought skates means two fast rounds.
            _powerups ??= new PowerUpField(CellSize);
            _powerups.Spawn(_map, _player.Cell, _rng);
            _pocket.Clear();
            if (_campaign.TryTakeQueued(out var bought))
                _pocket.TryPocket(bought);
            _skateTimer = 0f;
            _treatTimer = 0f;
            _fx.Clear();
            _noise.Clear();
            _darkTimer = 0f;
            _ratTimer = 0f;
            _dayPhase = 0f;
            _rainTimer = 40f + (float)_rng.NextDouble() * 30f;
            _rainActive = 0f;
            _fogTimer = 60f + (float)_rng.NextDouble() * 40f;
            _fogActive = 0f;
            _traps.Clear();
            _laserTimer = 0f;
            _shift ??= new ShiftEventSystem(_rng);
            _shift.StartRound();
            _combo.Reset();
            _roundHadTheft = false;
            _roundAlarmCritical = false;
            _hitStop = 0f;

            // Security cameras: a couple of wall-mounted lenses watching the
            // aisles. A carrying cat in a beam is noticed (alarm rises faster);
            // a thrown ball that lands on one disables it for a few seconds.
            _cameras = new CameraField();
            foreach (var cell in _map.WalkableCells())
            {
                if (cell.X == 0 || cell.Y == 0 || cell.X == _map.Width - 1 || cell.Y == _map.Height - 1)
                {
                    // Face inward: border cell on left edge faces right, etc.
                    int dir = cell.X == 0 ? 0 : cell.X == _map.Width - 1 ? 2
                            : cell.Y == 0 ? 1 : 3;
                    // Only mount a few, deterministically.
                    if ((cell.X + cell.Y) % 7 == 0)
                        _cameras.Add(new SecurityCamera(cell, dir));
                }
            }

            // Shoppers: a few bodies in the aisles, one of whom is a shoplifter
            // (the first spawn). Exits are the walkable border cells; if the
            // store has none, every walkable cell is an exit, so nobody gets
            // trapped.
            _customers.Clear();
            _exits.Clear();
            foreach (var cell in _map.WalkableCells())
                if (cell.X == 0 || cell.Y == 0 || cell.X == _map.Width - 1 || cell.Y == _map.Height - 1)
                    _exits.Add(cell);
            if (_exits.Count == 0)
                foreach (var cell in _map.WalkableCells()) _exits.Add(cell);

            // The store's door: a walkable border cell that acts as the
            // entrance and exit. Customers enter through it and can escape
            // the maze through it at random.
            _doorCell = PickDoorCell(_map);
            if (!_exits.Contains(_doorCell)) _exits.Add(_doorCell);

            const int customerCount = 3;
            for (int i = 0; i < customerCount; i++)
            {
                // Spawn on a walkable cell away from the player.
                GridPos spawn = _player.Cell;
                for (int tries = 0; tries < 20; tries++)
                {
                    var c = _map.WalkableCells().ElementAt(_rng.Next(_map.WalkableCells().Count()));
                    if (Math.Abs(c.X - _player.Cell.X) + Math.Abs(c.Y - _player.Cell.Y) > 4) { spawn = c; break; }
                }
                _customers.Add(new Customer(spawn, CellSize, shoplifter: i == 0, _rng));
            }

            // Spills and the fire alarm, off the loot and power-up cells so
            // the floor markers never stack into an unreadable pile.
            _hazards ??= new HazardField(CellSize);
            var occupied = _items.Items.Select(i => i.Cell)
                .Concat(_powerups.Items.Select(p => p.Cell));
            _hazards.Spawn(_map, _player.Cell, _rng, spillCount: 2, alsoAvoid: occupied);
            _wasPlayerCell = _player.Cell;

            _alarm ??= new AlarmSystem();
            _alarm.Reset();
            _alarm.PlayerCarryingRate = 0.02f * _campaign.PlayerAlarmFactor;

            // Scale the alarm to the store. The rates are per-second and were
            // tuned against the small reference floor plan; a bigger store is
            // further to walk, so at the same rates the meter fills before
            // the round can be cleared. Scaling by the store's size keeps the
            // same amount of alarm budget in a round either way.
            _alarm.RateScale = _map.Layout.AlarmScale(spec.LootCount, Player.GridSpeed);
            _alarm.RateScale *= _difficulty == Difficulty.Hard ? 1.2f : _difficulty == Difficulty.Easy ? 0.8f : 1f;
            _alarmRateScaleBase = _alarm.RateScale;

            // And the Calm nerves upgrade, if bought: decay only — the rise
            // rates are the simulation's, not the shop's to edit.
            _alarm.DecayMultiplier = _campaign.DecayFactor;

            _ball ??= new Ball();
            _ball.Configure(CellSize);
            _ball.Clear();

            _elapsedTime = 0f;
            if (startPlaying) _phase = RunPhase.Playing;
            _endMessage = "";
            // A fresh cast needs a fresh set of animated meshes. Do this after
            // the critters list is rebuilt, since the meshes are keyed on the
            // critter objects. LoadCritterModels is a no-op for critters that
            // already have one.
            LoadCritterModels();

            if (startPlaying)
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
        private IEnumerable<GridPos> SpawnPoints(int count)
        {
            var start = _map.Layout.PlayerStart;

            // Walking distance from the player, not Manhattan: the store has
            // shelves in it now, and a cell can be close in a straight line and
            // ten aisles away on foot.
            var dist = _map.DistanceMapFrom(start);

            var candidates = _map.WalkableCells()
                .Where(c => !_map.IsCounter(c.X, c.Y))
                .Where(c => dist[c.X, c.Y] != int.MaxValue && dist[c.X, c.Y] > 5)
                .ToList();

            if (candidates.Count == 0) { yield return start; yield break; }

            // Bias toward the far end of the store so the round opens with a
            // moment of quiet before anything happens. The old code took "the
            // far half of the candidate list", which was only equivalent to
            // "far away" when the list happened to be in row-major order and
            // the store was one screen wide.
            var furthest = candidates.Max(c => dist[c.X, c.Y]);
            int near = (int)(furthest * 0.55f);
            var far = candidates.Where(c => dist[c.X, c.Y] >= near).ToList();
            if (far.Count == 0) far = candidates;

            for (int i = 0; i < count && far.Count > 0; i++)
            {
                int idx = _rng.Next(far.Count);
                yield return far[idx];
                far.RemoveAt(idx);
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

            // The border ring is plain painted wall (one cube per wall cell,
            // full cell size so the ring closes); the interior blocked cells
            // are real shelf units, one mesh per style. See ShelfMesh for
            // what a unit actually contains and how its styles are chosen.
            _wallVerts = CubeMesh.Build(Color.White, 2f, 1f);
            BuildShelfMeshes();
            _playerParts = PersonMesh.Build();
            _customerParts = PersonMesh.Build();

            RebuildFloor();

            // The HUD takes the sprite batch, the 1x1 white texture and the
            // font, and draws the overlay from a per-frame snapshot of game
            // state (see HudState). Both graphics objects belong to the HUD,
            // so they're created here and handed over rather than kept as
            // fields; the font arrives just below.
            var spriteBatch = new SpriteBatch(GraphicsDevice);
            var uiPixel = new Texture2D(GraphicsDevice, 1, 1);
            uiPixel.SetData(new[] { Color.White });
            _hud = new Hud(spriteBatch, GraphicsDevice, uiPixel, _map.Layout);

            LoadCatModel();
            _propVerts = CubeMesh.Build(Color.White, 1f, 1f);

            // Status/timer text is rendered from this font (see Hud). The font
            // belongs to the HUD, so it's loaded here and handed over.
            _hud.Font = Content.Load<SpriteFont>("DefaultFont");

            // The attract-screen wordmark face. Blocky and arcade-shaped, which
            // the UI font is not; used only by Hud.DrawPaletteWordmark.
            _hud.TitleFont = Content.Load<SpriteFont>("Robotron");

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
            bool firstPerson = _camera.Mode == CameraMode.FirstPerson;
            _input.MouseLookEnabled = firstPerson;
            _input.MouseLookCenter = new Point(
                GraphicsDevice.Viewport.Width / 2, GraphicsDevice.Viewport.Height / 2);
            IsMouseVisible = !firstPerson;

            _input.Update(gameTime);
            var input = _input.State;

            if (input.Exit) Exit();

            // Pause toggles during play.
            if (_phase == RunPhase.Playing && input.Pause)
            {
                _paused = !_paused;
                if (!_paused) _showOptions = false;
                _pausedText = null;
                _optionsTextDirty = true;
            }

            // The camera is only live during play. On the attract screen it
            // was still being driven, so a stray wheel notch, tilt trigger or
            // V/Y press invisibly re-framed the store before the round began —
            // the player's first look at the store was a camera nobody set.
            // Paused, it holds still: the frame is frozen, and letting tilt
            // (O) keep running there would move the view the moment the
            // options overlay opened, since O is also the tilt-up key.
            if (_phase != RunPhase.Title && !_paused)
                _camera.Update(gameTime, input, _player.WorldPos, _player.FacingAngle);

            // The juice layer ages in real time regardless of phase, so a
            // burst never freezes mid-air behind the end banner.
            _fx.Update((float)gameTime.ElapsedGameTime.TotalSeconds);

            // And the soundtrack plays to the alarm: the store's mood is the
            // music's only input. Between rounds it settles back to calm.
            Music.SetMood(_phase == RunPhase.Playing ? _alarm.Stage : AlarmStage.Calm);

            if (_phase == RunPhase.Playing && !_paused)
            {
                _elapsedTime += (float)gameTime.ElapsedGameTime.TotalSeconds;

                if (_attractMode)
                {
                    _demoTimer += (float)gameTime.ElapsedGameTime.TotalSeconds;
                    // Any input exits the demo back to the attract screen.
                    if (input.Start || input.Restart || input.Throw || input.Use
                        || input.Move != Vector2.Zero || input.Tilt != 0f)
                    {
                        _attractMode = false;
                        _phase = RunPhase.Title;
                        _attractIdleTimer = 0f;

                        StartRound(false);
                        return;
                    }
                    if (_demoTimer >= DemoDurationSeconds)
                    {
                        _attractMode = false;
                        _phase = RunPhase.Title;
                        _attractIdleTimer = 0f;

                        StartRound(false);
                        return;
                    }
                    // Demo player AI is applied in UpdatePlay via _demoMoveDir.
                }

                UpdatePlay(gameTime, input);
            }
            else if (_paused)
            {
                // Paused: no simulation, but options/rebind input is live.
                _optionsRepeat = Math.Max(0f, _optionsRepeat - (float)gameTime.ElapsedGameTime.TotalSeconds);

                // Toggle the options overlay.
                if (input.LastKeyPressed == Keys.O)
                {
                    _showOptions = !_showOptions;
                    _optionsTextDirty = true;
                }

                if (_showOptions)
                {
                    // Navigation.
                    if (_optionsRepeat <= 0f)
                    {
                        if (input.Move.Y < 0) { _optionsSelection = Math.Max(0, _optionsSelection - 1); _optionsRepeat = 0.18f; _optionsTextDirty = true; }
                        else if (input.Move.Y > 0) { _optionsSelection = Math.Min(BindingCount() + 1, _optionsSelection + 1); _optionsRepeat = 0.18f; _optionsTextDirty = true; }
                    }

                    // Volume adjust on the two volume rows.
                    if (_optionsSelection == 0 && input.Move.X != 0 && _optionsRepeat <= 0f)
                    {
                        _musicVolume = MathHelper.Clamp(_musicVolume + input.Move.X * 0.1f, 0f, 1f);
                        Music.Volume = _musicVolume;
                        _optionsRepeat = 0.12f;
                        _optionsTextDirty = true;
                    }
                    else if (_optionsSelection == 1 && input.Move.X != 0 && _optionsRepeat <= 0f)
                    {
                        _sfxVolume = MathHelper.Clamp(_sfxVolume + input.Move.X * 0.1f, 0f, 1f);
                        Sfx.Volume = _sfxVolume;
                        _optionsRepeat = 0.12f;
                        _optionsTextDirty = true;
                    }

                    // Rebinding: confirm on a binding row starts capture; the
                    // next key rebinds. Escape backs out — binding "quit" to
                    // whatever the player was reaching for is not a kindness.
                    if (_rebindIndex >= 0)
                    {
                        if (input.LastKeyPressed == Keys.Escape)
                        {
                            _rebindIndex = -1;
                            _rebindMessage = "cancelled";
                            _optionsTextDirty = true;
                        }
                        else if (input.LastKeyPressed != Keys.None && input.LastKeyPressed != Keys.Enter)
                        {
                            _rebindMessage = TryRebind(_rebindIndex, input.LastKeyPressed) ?? "saved";
                            _rebindIndex = -1;
                            _optionsTextDirty = true;
                        }
                    }
                    else
                    {
                        int row = _optionsSelection - 2;
                        if (row >= 0 && row < BindingCount() && input.Start)
                        {
                            _rebindIndex = row;
                            _rebindMessage = "";
                            _optionsTextDirty = true;
                        }
                    }
                }

                // Resume with P handled above; R restarts.
                if (input.Restart)
                {
                    _paused = false;
                    _showOptions = false;
                    if (_phase == RunPhase.Won) _campaign.Advance();
                    StartRound();
                    return;
                }
            }
            else if (_phase == RunPhase.Title)
            {
                _attractIdleTimer += (float)gameTime.ElapsedGameTime.TotalSeconds;
                // Any input resets the idle timer (and, once a demo is not
                // running, waits for an explicit start).
                if (input.Start || input.Restart || input.Move != Vector2.Zero)
                    _attractIdleTimer = 0f;

                if (_attractIdleTimer >= AttractIdleSeconds && !_attractMode)
                {
                    _attractMode = true;
                    _demoTimer = 0f;
                    StartRound();
                    return;
                }

                // T toggles Time Attack mode on the title screen.
                if (input.LastKeyPressed == Keys.T) _timeAttackMode = !_timeAttackMode;

                // A resumed run resumes until it is explicitly abandoned, or
                // the only way out of it is deleting a file by hand.
                if (input.LastKeyPressed == Keys.N && _runResumed) StartFreshRun();

                // L launches a custom store (if custom_store.txt was found).
                if (input.LastKeyPressed == Keys.L && _customLayout != null)
                {
                    _customRound = true;
                    StartRound();
                    return;
                }

                // Either confirm button...
                if (input.Start || input.Restart)
                {
                    _customRound = false;
                    StartRound();
                }
            }
            else
            {
                if (_attractMode)
                {
                    // Demo: skip the shop and auto-restart the round.
                    if (_phase == RunPhase.Won) _campaign.Advance();
                    StartRound();
                    return;
                }

                // Between rounds the shop is open: number keys 1-6 buy.
                // Each press is a rising edge (see KeyboardMouseInput), so
                // one press is one purchase.
                if (input.BuySlot != 0) TryBuySlot(input.BuySlot);

                // R restarts — but only a win moves the run along. A loss
                // retries the same store, same loot, same cats count.
                if (input.Restart)
                {
                    if (_phase == RunPhase.Won) _campaign.Advance();
                    StartRound();
                }
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
            // Hit-stop: on a hard impact the whole simulation runs at a crawl
            // for a beat, which is what gives a tackle its punch. The clock
            // counts down in *real* time (so the freeze lasts as long as it
            // says), while everything driven below sees the slowed dt. The
            // camera is not on this path — the shake should keep shaking.
            float rawDt = (float)gameTime.ElapsedGameTime.TotalSeconds;
            if (_hitStop > 0f)
            {
                _hitStop -= rawDt;
                rawDt *= HitStopScale;
            }
            float dt = rawDt;
            var simTime = new GameTime(gameTime.TotalGameTime, TimeSpan.FromSeconds(dt));

            // Isometric walks the grid cell to cell; first-person moves the
            // player freely (camera-relative) with wall sliding.
            bool freeMove = _camera.Mode == CameraMode.FirstPerson;
            Vector2 moveDir;
            if (_attractMode)
            {
                moveDir = ComputeDemoMoveDir();
            }
            else
            {
                moveDir = freeMove ? _camera.MapMoveToWorld(input) : input.Move;
            }
            _player.IsCrouching = false;
            _player.Update(simTime, _map, moveDir, freeMove);

            // Walking over a power-up pockets it — if the one slot is free.
            // A full pocket leaves it on the floor rather than swapping.
            if (_powerups.TryPickUp(_player.Cell, _pocket))
            {
                Sfx.Play(SfxId.PowerUp, volume: 0.5f, pitch: -0.2f);
                EventSystem.Instance.Publish(new PowerUpCollectedEvent(_pocket.Pocketed!.Value));
            }

            // Stepped onto a spill: skid. The skates make it worse, on
            // purpose — the boost is a package deal with the risk.
            if (_player.Cell != _wasPlayerCell)
            {
                _wasPlayerCell = _player.Cell;

                // One footstep per cell crossed, loud enough for the species
                // nearby to hear (NoiseSystem + CritterProfile.HearingRadius).
                // Standing still emits nothing, which is the whole stealth
                // loop: freeze and the store goes quiet.
                _noise.Footstep(_player.Cell,
                    NoiseSystem.StepLoudness
                    * (_player.Slipping ? NoiseSystem.SlipLoudness : 1f)
                    * (_skateTimer > 0f ? SkatesNoiseLoudness : 1f)
                    * _campaign.NoiseFactor);

                if (_hazards.IsSpill(_player.Cell))
                {
                    float slip = _skateTimer > 0f ? SlipSeconds * 2f : SlipSeconds;
                    if (_player.ApplySlip(slip))
                    {
                        Sfx.Play(SfxId.Slip, volume: 0.55f);
                        // The crash is its own noise: louder and longer-lived
                        // than a step, so nearby cats flinch at the source
                        // even though the player keeps sliding away.
                        _noise.Clatter(_player.Cell, SlipClatterLoudness);
                        EventSystem.Instance.Publish(new PlayerSlippedEvent());
                        _fx.Text(_player.WorldPos + new Vector3(0f, 1.5f, 0f),
                            "Slip!", new Color(140, 190, 240), 1.0f);
                        _camera.AddShake(0.15f);
                    }
                }
            }

            // The Use key: the fire alarm first (you're standing on it — that
            // IS the interaction), otherwise the pocketed power-up.
            if (input.Use)
            {
                if (_hazards.TryPullFireAlarm(_player.Cell, _critters, _items, _alarm,
                        FireAlarmSpike, out int scattered))
                {
                    Sfx.Play(SfxId.FireAlarm, volume: 0.7f);
                    EventSystem.Instance.Publish(new FireAlarmPulledEvent(scattered));
                    _camera.AddShake(1f);
                    _hitStop = 0.05f;
                    var shoutPos = _player.WorldPos + new Vector3(0f, 2f, 0f);
                    _fx.Text(shoutPos, "FIRE ALARM!", new Color(255, 150, 90), 1.8f);
                    _fx.Text(shoutPos - new Vector3(0f, 0.6f, 0f),
                        $"alarm +{FireAlarmSpike:P0}", new Color(255, 110, 80), 1.8f);
                    _fx.Burst(_player.WorldPos + new Vector3(0f, 1.2f, 0f),
                        new Color(255, 120, 80), count: 18, speed: 3.2f, life: 0.8f, _rng);
                }
                else if (_pocket.Pocketed is { } pocketed)
                {
                    _pocket.Use();
                    Activate(pocketed);
                }
            }

            // Timed effects tick down; the speed multiplier follows the
            // skates, and a lingering treat keeps feeding the AI's locus.
            if (_skateTimer > 0f) _skateTimer -= dt;
            if (_treatTimer > 0f) _treatTimer -= dt;
            if (_darkTimer > 0f) _darkTimer -= dt;
            if (_ratTimer > 0f) _ratTimer -= dt;

            if (_laserTimer > 0f)
            {
                _laserTimer -= dt;
                if (_rng.NextDouble() < dt * 0.6)
                {
                    var cells = _map.WalkableCells().ToList();
                    if (cells.Count > 0) _laserCell = cells[_rng.Next(cells.Count)];
                }
            }

            // Day/night cycle: a slow loop that nudges the ambient light.
            _dayPhase = (_dayPhase + dt / 120f) % 1f;
            // Weather: an occasional rainstorm that slicks the floor.
            if (_rainActive > 0f)
            {
                _rainActive -= dt;
                if (_rng.NextDouble() < dt * 0.4)
                {
                    var cells = _map.WalkableCells().ToList();
                    if (cells.Count > 0)
                        _hazards.AddSpill(cells[_rng.Next(cells.Count)]);
                }
            }
            else
            {
                _rainTimer -= dt;
                if (_rainTimer <= 0f)
                {
                    _rainActive = 8f;
                    _rainTimer = 40f + (float)_rng.NextDouble() * 30f;
                    _fx.Text(_player.WorldPos + new Vector3(0f, 2.2f, 0f),
                        "Rain!", new Color(140, 170, 210), 1.4f);
                }
            }

            // Fog: a slow, store-wide haze that softens vision.
            if (_fogActive > 0f)
            {
                _fogActive -= dt;
            }
            else
            {
                _fogTimer -= dt;
                if (_fogTimer <= 0f)
                {
                    _fogActive = 12f;
                    _fogTimer = 70f + (float)_rng.NextDouble() * 50f;
                    _fx.Text(_player.WorldPos + new Vector3(0f, 2.2f, 0f),
                        "Fog rolls in...", new Color(180, 190, 205), 1.4f);
                }
            }
            // Skates multiply with the Fast shoes upgrade: the shop's boost
            // is a factor of whatever baseline the campaign has bought.
            _player.SpeedMultiplier = (_skateTimer > 0f ? SkatesSpeedBoost : 1f)
                                     * _campaign.SpeedFactor;

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

            // A ball resting on a camera takes it out for a few seconds.
            if (_ballCell.HasValue && _ball.State != BallState.Thrown)
                _cameras.DisableAt(_ballCell.Value, 8f);

            _cameras.Update(dt);

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

                    // The stun honours the species: a Moggy shrugs the ball
                    // off entirely (it just flinches and keeps going), and a
                    // half-hardy species would sit out half the time.
                    bool stunned = critter.TryStunFromBall(3f);
                    _ball.Explode();
                    Sfx.Play(stunned ? SfxId.Tackle : SfxId.Bounce, volume: 0.5f);
                    _fx.Dust(critter.WorldPos + new Vector3(0f, 0.6f, 0f), _rng);
                    if (stunned) _camera.AddShake(0.2f);

                    if (stunned && critter.Carrying)
                    {
                        _items.DropFrom(critter, critter.Cell);
                        critter.Carrying = false;
                        critter.Mood = Mood.Wary;
                        critter.MoodTimer = 10f;
                        Sfx.Play(SfxId.Drop, volume: 0.5f);
                        _alarm.Ping();
                    }
                    break;
                }
            }

            // Pops fade on their own clock; this runs before the cats read
            // the field, so a footstep heard this frame is at least one
            // frame old (never instant).
            _noise.Update(dt);

            // The shift itself: if an event is due, apply it. The schedule
            // is pure (ShiftEvents.cs); the effects live in ApplyShiftEvent
            // because they touch the goods, the meter, the lights and the
            // rats — all things only the game owns.
            if (_shift.Update(dt) is { } shiftEvent)
                ApplyShiftEvent(shiftEvent);

            // Shoppers wander; the shoplifter can pocket and run. Possession
            // rules live in ItemField; here we just drive movement and react
            // to arrivals and tackles.
            foreach (var customer in _customers)
            {
                if (customer.Escaped) continue;
                CustomerBrain.Decide(customer, _map, _player.Cell, _items, _rng, _exits, _doorCell);
                customer.Update(dt, _map);

                if (!customer.IsMoving && customer.IsShoplifter && customer.Carrying == null)
                {
                    var item = _items.LooseAt(customer.Cell);
                    if (item != null)
                    {
                        _items.TakeByCustomer(item, customer);
                        customer.Carrying = item;
                        _roundHadTheft = true;
                        _fx.Text(customer.WorldPos + new Vector3(0f, 1.5f, 0f),
                            "pocketed...", new Color(230, 170, 90), 1.1f);
                    }
                }

                if (customer.JustArrived && customer.IsShoplifter && customer.Carrying != null
                    && _exits.Contains(customer.Cell))
                {
                    customer.MarkEscaped();
                    customer.Carrying = null;
                    _fx.Text(customer.WorldPos + new Vector3(0f, 1.5f, 0f),
                        "shoplifter escaped!", new Color(230, 90, 90), 1.4f);
                    Sfx.Play(SfxId.Steal, volume: 0.7f);
                }

                if (customer.IsShoplifter && customer.Carrying != null
                    && customer.Cell == _player.Cell)
                {
                    _items.DropFromCustomer(customer, customer.Cell);
                    customer.Carrying = null;
                    customer.Leaving = true;
                    _fx.Text(customer.WorldPos + new Vector3(0f, 1.5f, 0f),
                        "Caught the shoplifter!", new Color(150, 220, 160), 1.4f);
                    _camera.AddShake(0.12f);
                    Sfx.Play(SfxId.Tackle, volume: 0.6f);
                }

                // A shopper that reaches the door leaves the store; a new one
                // enters through the same door so the aisles stay populated.
                if (!customer.IsShoplifter && customer.JustArrived
                    && customer.Cell == _doorCell)
                {
                    customer.MarkEscaped();
                    _fx.Text(customer.WorldPos + new Vector3(0f, 1.5f, 0f),
                        "left the store", new Color(180, 180, 200), 0.9f);
                }
            }
            _customers.RemoveAll(c => c.Escaped);

            // Keep the shopper population topped up through the door.
            while (_customers.Count < 3)
            {
                _customers.Add(new Customer(_doorCell, CellSize,
                    shoplifter: _rng.Next(4) == 0, _rng));
            }

            UpdateCritters(simTime);

            // Floor traps: the first cat to step on one is stunned and drops
            // whatever it was carrying.
            for (int i = _traps.Count - 1; i >= 0; i--)
            {
                var trap = _traps[i];
                foreach (var critter in _critters)
                {
                    if (critter.Cell != trap) continue;
                    bool stunned = critter.TryStunFromBall(3f);
                    if (stunned && critter.Carrying)
                    {
                        _items.DropFrom(critter, critter.Cell);
                        critter.Carrying = false;
                        critter.Mood = Mood.Wary;
                        critter.MoodTimer = 10f;
                        _alarm.Ping();
                    }
                    Sfx.Play(SfxId.Tackle, volume: 0.5f);
                    _fx.Burst(critter.WorldPos + new Vector3(0f, 0.6f, 0f),
                        new Color(180, 90, 90), count: 12, speed: 2f, life: 0.8f, _rng);
                    _traps.RemoveAt(i);
                    break;
                }
            }
            UpdateLoot(dt);
            UpdateHeartbeat(dt);
            _hud.Update(dt);

            bool covered = _critters.Any(c => c.Carrying && _cameras.IsCovered(_map, c.Cell));
            _alarm.RateScale = _alarmRateScaleBase * (covered ? 1.3f : 1f);
            _alarm.Update(dt, _items.StolenWeight, _items.Held != null, _items.AllSecured);
            if (_alarm.Stage == AlarmStage.Critical) _roundAlarmCritical = true;
            UpdateAlarmAudio(dt);

            if (_timeAttack && _timeLeft > 0f)
            {
                _timeLeft -= dt;
                if (_timeLeft <= 0f)
                {
                    _timeLeft = 0f;
                    EndRound(RunPhase.Lost, "Time's up!");
                    return;
                }
            }

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
            Vector3 nearestPos = Vector3.Zero;
            foreach (var critter in _critters)
            {
                float d = Vector3.Distance(_player.WorldPos, critter.WorldPos);
                if (d < nearest)
                {
                    nearest = d;
                    nearestPos = critter.WorldPos;
                }
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
                // panic rather than as a volume change — and from where the
                // cat actually is, so you can turn towards it.
                Sfx.PlayAt(SfxId.Heartbeat, _player.WorldPos, nearestPos, HeartbeatRange,
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
        private static GridPos PickDoorCell(GridMap map)
    {
        // Prefer a walkable cell on the top edge, near the centre, so the
        // door reads as the store's entrance. Fall back to any walkable
        // border cell, then to the first walkable cell.
        GridPos best = default;
        bool found = false;
        int bestDist = int.MaxValue;
        int cx = map.Width / 2;
        foreach (var cell in map.WalkableCells())
        {
            if (cell.Y != 0) continue;
            int d = Math.Abs(cell.X - cx);
            if (d < bestDist) { bestDist = d; best = cell; found = true; }
        }
        if (!found)
        {
            foreach (var cell in map.WalkableCells())
            {
                if (cell.X != 0 && cell.Y != 0 && cell.X != map.Width - 1 && cell.Y != map.Height - 1) continue;
                int d = Math.Abs(cell.X - cx) + Math.Abs(cell.Y - map.Height / 2);
                if (d < bestDist) { bestDist = d; best = cell; found = true; }
            }
        }
        if (!found)
        {
            foreach (var cell in map.WalkableCells()) { best = cell; found = true; break; }
        }
        return best;
    }

    private Vector2 ComputeDemoMoveDir()
    {
        GridPos target;
        if (_items.Held != null)
        {
            // Secure: head for the nearest counter cell.
            GridPos counterCell = new(_map.CounterStart, _map.CounterRow);
            target = counterCell;
        }
        else
        {
            var loose = _items.NearestLoose(_player.Cell);
            if (loose != null)
                target = loose.Cell;
            else
            {
                // Wander: pick a random walkable cell.
                var cells = _map.WalkableCells().ToList();
                target = cells.Count > 0 ? cells[_rng.Next(cells.Count)] : _player.Cell;
            }
        }

        var step = _map.NextStepToward(_player.Cell, target);
        if (step == null) return Vector2.Zero;

        // Avoid walking into a cat.
        foreach (var critter in _critters)
        {
            if (critter.Cell == step.Value) return Vector2.Zero;
        }

        int dx = step.Value.X - _player.Cell.X;
        int dy = step.Value.Y - _player.Cell.Y;
        return new Vector2(dx, dy);
    }

    private void UpdateCritters(GameTime gameTime)
        {
            float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;

            // Rebuilt each frame: it's a struct of two delegates, and it keeps
            // the per-critter data (sight, being watched) out of the shared
            // context.
            var locus = new CritterLocus(
                Map: _map,
                PlayerCell: _player.Cell,
                Ball: _ballCell ?? (_ratTimer > 0f ? _ratCell : (_laserTimer > 0f ? _laserCell : null)),
                LootTarget: _lootTarget,
                LootTargetItem: _lootTargetItem,
                StolenCount: _items.StolenCount,
                LootUnderfoot: c => _items.LooseAt(c.Cell),
                TrySteal: c => TrySteal(c),
                AlarmStage: _alarm.Stage,
                Treat: _treatTimer > 0f ? _treatCell : null,
                TryEatTreat: c => TryEatTreat(c));

            // What each cat can see, worked out for all of them at once. This
            // has to happen before any of them acts, so that a cat which
            // spots the player on this frame can pass the news to the others
            // on the same frame.
            foreach (var critter in _critters)
                UpdateAwareness(critter);

            _awareness.Reset(_critters);
            _awareness.Update(_player.Cell, _map, _noise);

            for (int i = _critters.Count - 1; i >= 0; i--)
            {
                var critter = _critters[i];
                critter.Update(gameTime, locus);

                // Wet floor: a cat that lands on a spill loses its footing
                // and drops what it was carrying (the species' tolerance
                // applies — a Moggy plows straight through).
                if (critter.JustArrived)
                {
                    bool hadLoot = critter.Carrying;
                    if (_hazards.TripCritter(critter, _items))
                    {
                        Sfx.Play(SfxId.Slip, volume: 0.5f, pitch: 0.15f);
                        if (hadLoot)
                            Sfx.Play(SfxId.Drop, volume: 0.45f);
                    }
                }

                // Drive the rig here, in the update loop, not in Draw: it
                // rewrites the mesh's vertices in place (see CatAnimator), so
                // it is a state change and belongs with the other updates.
                // The head tracks the player once they've been noticed, which
                // is what gives the cat a sense of seeing you.
                if (_catAnims.TryGetValue(critter, out var anim))
                    anim.Update(BuildCatAnimInput(critter, dt));

                // A thief that reaches the counter with loot dumps it on the
                // till. This is the thief's escape plan and it is a losing
                // one: the goods are dropped rather than secured, so the
                // player still has to collect them, but the alarm takes a
                // hard hit for it.
                if (critter.JustArrived && critter.Carrying &&
                    _map.IsCounter(critter.Cell.X, critter.Cell.Y))
                {
                    var dumped = _items.AbandonAtCounter(critter, critter.Cell);
                    critter.Carrying = false;
                    if (dumped != null)
                    {
                        // The spike is deliberately NOT scaled with the store.
                        // The rates are per-second, and a bigger round lasts
                        // longer, so they scale. The spike is an absolute
                        // event — a fraction of the meter, gone in one go —
                        // and a fixed fraction means the same thing in every
                        // store. A Lucky charm nibbles at that fraction;
                        // it's the one upgrade against an event, so its
                        // value shows up exactly here.
                        float spike = CounterDropAlarmSpike * _campaign.SpikeFactor;
                        _alarm.Spike(spike);
                        Sfx.Play(SfxId.Drop, volume: 0.7f);
                        EventSystem.Instance.Publish(new ItemStolenEvent(critter.Profile.Name));

                        // Bad news should land like bad news: a shove of the
                        // camera, a beat of freeze, and the cost on screen.
                        _camera.AddShake(0.7f);
                        _hitStop = 0.06f;
                        _fx.Text(dumped.WorldPos + new Vector3(0f, 1.5f, 0f),
                            $"alarm +{spike:P0}", new Color(255, 96, 80), 1.7f);
                    }
                }

                if (critter.Tackled)
                {
                    // A tackle makes it drop whatever it had, right here.
                    bool hadLoot = critter.Carrying;
                    _items.DropFrom(critter, critter.Cell);
                    critter.Carrying = false;
                    Sfx.Play(SfxId.Tackle, volume: 0.7f);

                    // The signature punch of the game: a beat of freeze, a
                    // shove of the camera, and fur drifting off the impact.
                    _hitStop = HitStopSeconds;
                    _camera.AddShake(0.45f);
                    _fx.Burst(critter.WorldPos + new Vector3(0f, 0.7f, 0f),
                        new Color(214, 206, 194), count: 12, speed: 2.4f, life: 0.6f, _rng);
                    _fx.Text(critter.WorldPos + new Vector3(0f, 1.6f, 0f),
                        "Gotcha!", new Color(255, 224, 130), 1.1f);

                    if (hadLoot)
                    {
                        Sfx.Play(SfxId.Drop, volume: 0.6f);
                        // Recovering the goods is a small win; let the meter off
                        // a little so a clean tackle feels like progress.
                        _alarm.Ping();
                    }

                    // Boss critters take several tackles: drop loot and stun,
                    // but stay in the game until their health runs out. The
                    // critter owns the arithmetic (and the "was that the last
                    // one" answer), so this reads the result rather than
                    // poking at health.
                    if (critter.IsBoss && !critter.TakeTackleDamage())
                    {
                        critter.ResetTackle();
                        critter.Stun(2.5f);
                        _fx.Text(critter.WorldPos + new Vector3(0f, 2.4f, 0f),
                            $"Boss: {critter.Health}/{critter.MaxHealth}", new Color(255, 120, 120), 1.6f);
                        continue;
                    }

                    _critters.RemoveAt(i);
                    _catAnims.Remove(critter);
                    _catMeshes.Remove(critter);
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
            // A power cut takes most of their sight: the cats are nocturnal,
            // not blind, so they keep a fraction of their radius. This is
            // the shift's stealth window.
            float vision = critter.Profile.VisionRadius
                           * (_darkTimer > 0f ? DarkVisionFactor : 1f)
                           * (_fogActive > 0f ? 0.8f : 1f)
                           * (_player.IsCrouching ? 0.5f : 1f);
            bool couldSee = dist <= vision &&
                            _map.HasLineOfSight(critter.Cell, _player.Cell);

            // The first frame it notices you, it says so. Gated per critter so
            // it doesn't meow every frame while standing in your eyeline — and
            // positioned at the cat, so a meow from the next aisle is faint and
            // to one side.
            if (couldSee && !critter.CanSeePlayer)
                Sfx.PlayAt(SfxId.Meow, _player.WorldPos, critter.WorldPos, 34f, volume: 0.55f);

            critter.CanSeePlayer = couldSee;

            // Judged from the player's eye. In first person that's a real cone
            // — the cat has to be in front of you. In the isometric view the
            // camera looks straight down, so the camera falls back to "is it
            // on screen", which is now a meaningful question: the viewport is
            // a window onto a store that may be bigger than the screen, so a
            // cat across the store is genuinely out of sight.
            float eyeHeight = _camera.Mode == CameraMode.FirstPerson
                ? Camera.FirstPersonEyeHeight
                : 1f;
            critter.BeingWatched =
                _camera.IsInViewCone(_player.WorldPos + new Vector3(0f, eyeHeight, 0f), critter.WorldPos);
        }

        /// <summary>
        /// Fires a pocketed power-up.
        ///
        /// The pocket is already empty by the time this runs (the caller
        /// spends it), so this is only the *effect*: each one is a different
        /// shape of answer to the same question — how do I want the cats to
        /// be busy for the next few seconds?
        /// </summary>
        private void Activate(PowerUpKind kind)
        {
            EventSystem.Instance.Publish(new PowerUpUsedEvent(kind));

            switch (kind)
            {
                case PowerUpKind.Skates:
                    _skateTimer = SkatesSeconds;
                    Sfx.Play(SfxId.PowerUp, volume: 0.6f);
                    _fx.Dust(_player.WorldPos, _rng);
                    _fx.Text(_player.WorldPos + new Vector3(0f, 1.6f, 0f),
                        "Skates on!", new Color(140, 205, 245), 1.2f);
                    break;

                case PowerUpKind.Spray:
                    ApplySpray();
                    break;

                case PowerUpKind.Treat:
                    _treatTimer = TreatSeconds;
                    _treatCell = _player.Cell;
                    Sfx.Play(SfxId.PowerUp, volume: 0.55f, pitch: -0.3f);
                    _fx.Sparkle(new Vector3(_treatCell.X * CellSize, 0.4f, _treatCell.Y * CellSize),
                        new Color(245, 200, 120), _rng);
                    _fx.Text(_player.WorldPos + new Vector3(0f, 1.6f, 0f),
                        "Snack down!", new Color(245, 205, 130), 1.2f);
                    break;

                case PowerUpKind.SmokeBomb:
                    {
                        int smoked = SprayEffect.PanicAt(_map, _player.Cell, 6, _critters, 6f);
                        Sfx.Play(SfxId.Hiss, volume: 0.6f);
                        _fx.Burst(_player.WorldPos + new Vector3(0f, 1.2f, 0f),
                            new Color(140, 140, 150), count: 24, speed: 2.5f, life: 1.0f, _rng);
                        _fx.Text(_player.WorldPos + new Vector3(0f, 1.6f, 0f),
                            smoked > 0 ? $"smoke! {smoked} scattered" : "smoke (nobody home)",
                            new Color(200, 200, 210), 1.2f);
                    }
                    break;

                case PowerUpKind.Trap:
                    _traps.Add(_player.Cell);
                    _fx.Burst(_player.WorldPos + new Vector3(0f, 0.2f, 0f),
                        new Color(180, 90, 90), count: 8, speed: 1.5f, life: 0.7f, _rng);
                    _fx.Text(_player.WorldPos + new Vector3(0f, 1.6f, 0f),
                        "trap set", new Color(220, 130, 130), 1.2f);
                    break;

                case PowerUpKind.Laser:
                    _laserTimer = 6f;
                    _laserCell = _player.Cell;
                    _fx.Text(_player.WorldPos + new Vector3(0f, 1.6f, 0f),
                        "laser on!", new Color(220, 80, 80), 1.2f);
                    break;
            }
        }

        // The hiss: every cat within earshot — walking distance, so shelves
        // block it — panics and runs. Nothing is dropped; the spray buys
        // space, the ball is still the only thing that separates a cat from
        // its loot. The *where* lives in SprayEffect (headless, tested);
        // this method only adds the sound.
        private void ApplySpray()
        {
            Sfx.Play(SfxId.Hiss, volume: 0.65f);
            int caught = SprayEffect.PanicAt(_map, _player.Cell, SprayPanicRadius, _critters, SprayPanicSeconds);

            var pos = _player.WorldPos + new Vector3(0f, 1.3f, 0f);
            _fx.Burst(pos, new Color(206, 150, 230), count: 16, speed: 3f, life: 0.7f, _rng);
            _fx.Text(pos, caught > 0 ? $"startled {caught}!" : "nobody heard that",
                new Color(216, 165, 240), 1.2f);
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
            _roundHadTheft = true;
            critter.Mood = Mood.Bold;
            critter.MoodTimer = 15f;
            _alarm.Ping();
            Sfx.Play(SfxId.Steal, volume: 0.5f);
            EventSystem.Instance.Publish(new ItemStolenEvent(critter.Profile.Name));
            _fx.Text(item.WorldPos + new Vector3(0f, 1.4f, 0f),
                "Stolen!", new Color(240, 96, 80), 1.2f);
            return true;
        }

        /// <summary>
        /// Performs the feed when a critter decides to eat the dropped treat
        /// (see <see cref="ICritterAI.WantsTreat"/>): the treat is consumed
        /// and the cat is one snack closer to friendship — two snacks make a
        /// friend (see <see cref="Critter.Feed"/>), which stops it stealing
        /// and turns it into a companion for the round.
        /// </summary>
        /// <returns>True when a treat was actually eaten.</returns>
        private bool TryEatTreat(Critter critter)
        {
            if (_treatTimer <= 0f) return false;

            _treatTimer = 0f;
            bool friendNow = critter.Feed();

            Sfx.PlayAt(SfxId.Meow, _player.WorldPos, critter.WorldPos, 34f,
                volume: friendNow ? 0.75f : 0.45f, pitch: friendNow ? 0.15f : -0.1f);
            _fx.Text(critter.WorldPos + new Vector3(0f, 1.4f, 0f),
                friendNow ? "Friend!" : "snack...",
                friendNow ? new Color(150, 220, 160) : new Color(235, 200, 130), 1.2f);

            EventSystem.Instance.Publish(new CatFedEvent(critter.Profile.Name));
            if (friendNow)
            {
                EventSystem.Instance.Publish(new CatBefriendedEvent(critter.Profile.Name));
                _fx.Sparkle(critter.WorldPos + new Vector3(0f, 0.6f, 0f),
                    new Color(160, 230, 170), _rng);
            }
            return true;
        }

        /// <summary>
        /// Applies one shift event (see <see cref="ShiftEventKind"/>).
        ///
        /// Every effect here touches something only the game owns — the
        /// goods, the meter, the lights, the rats — which is why the effects
        /// live here while the *schedule* stays pure and testable in
        /// ShiftEvents.cs.
        /// </summary>
        private void ApplyShiftEvent(ShiftEventKind kind)
        {
            EventSystem.Instance.Publish(new ShiftEventFiredEvent(kind));

            var overhead = _player.WorldPos + new Vector3(0f, 2.2f, 0f);
            switch (kind)
            {
                case ShiftEventKind.Delivery:
                    // Fresh stock, mid-round: more goods on the shelves.
                    // Pressure up — the alarm counts what is out — but also
                    // more to save, so it cuts both ways.
                    _items.Spawn(DeliveryCount, _player.Cell);
                    _fx.Text(overhead, "Delivery arrived!", new Color(180, 210, 240), 1.6f);
                    break;

                case ShiftEventKind.PowerCut:
                    _darkTimer = PowerCutSeconds;
                    _fx.Text(overhead, "Power cut!", new Color(150, 160, 190), 1.6f);
                    break;

                case ShiftEventKind.AlarmAudit:
                    // The manager is counting stock: the meter drops a chunk
                    // (they check the *counter*, apparently, not the shelf).
                    _alarm.Drain(AlarmAuditFraction);
                    _fx.Text(overhead, "Manager's audit - alarm drained",
                        new Color(150, 220, 160), 1.6f);
                    break;

                case ShiftEventKind.Rat:
                    // The rat runs the aisles and every cat chases it: the
                    // locus passes its cell as the ball, so the existing
                    // "go and look at that" pathing does the herding.
                    _ratTimer = RatSeconds;
                    _ratCell = RandomWalkableFarFrom(_player.Cell);
                    _fx.Burst(new Vector3(_ratCell.X * CellSize, 0.4f, _ratCell.Y * CellSize),
                        new Color(100, 90, 80), count: 12, speed: 2.5f, life: 1.2f, _rng);
                    _fx.Text(overhead, "Rat!", new Color(220, 140, 120), 1.4f);
                    break;

                case ShiftEventKind.BottleBreak:
                    {
                        var cells = _map.WalkableCells().ToList();
                        if (cells.Count > 0)
                        {
                            var spill = cells[_rng.Next(cells.Count)];
                            _hazards.AddSpill(spill);
                            _fx.Burst(new Vector3(spill.X * CellSize, 0.3f, spill.Y * CellSize),
                                new Color(120, 180, 220), count: 10, speed: 2f, life: 0.9f, _rng);
                        }
                        _fx.Text(overhead, "Bottle broke - spill!", new Color(140, 190, 230), 1.4f);
                    }
                    break;

                case ShiftEventKind.CustomerDrop:
                    {
                        var cells = _map.ShelfEdgeCells().ToList();
                        if (cells.Count > 0)
                        {
                            var cell = cells[_rng.Next(cells.Count)];
                            _items.AddLoose(LootKind.Boxed, cell);
                            _fx.Burst(new Vector3(cell.X * CellSize, 0.6f, cell.Y * CellSize),
                                new Color(235, 200, 130), count: 8, speed: 2f, life: 0.8f, _rng);
                        }
                        _fx.Text(overhead, "Dropped basket - free goods!", new Color(245, 210, 140), 1.4f);
                    }
                    break;
            }
        }

        // A walkable cell as far from the player as is convenient: the rat
        // should read as something happening *over there*, not underfoot.
        private GridPos RandomWalkableFarFrom(GridPos from)
        {
            GridPos best = from;
            int bestGap = -1;
            foreach (var cell in _map.WalkableCells())
            {
                int gap = Math.Abs(cell.X - from.X) + Math.Abs(cell.Y - from.Y);
                if (gap > bestGap && _rng.NextDouble() < 0.25)
                {
                    bestGap = gap;
                    best = cell;
                }
            }
            return best;
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
                    _fx.Sparkle(_player.WorldPos + new Vector3(0f, 1.1f, 0f),
                        new Color(150, 220, 160), _rng);
                }
            }

            if (_items.TrySecure(_player.Cell))
            {
                _alarm.Ping();
                Sfx.Play(SfxId.Secure, volume: 0.7f);
                EventSystem.Instance.Publish(new ItemSecuredEvent());

                // The good-news beat: a sparkle off the counter. The coin
                // line ("+15c") comes from the ItemSecured hook, which sees
                // every path to the counter, not just this one.
                _fx.Sparkle(_player.WorldPos + new Vector3(0f, 1.7f, 0f),
                    new Color(140, 240, 160), _rng);
            }

            _items.Update(dt, _player.Cell, CarryHeight);

            // The nearest loose item, and where it is. The critters need both
            // halves: the cell to path to, and the item itself so they can
            // decide whether they want what is in it.
            _lootTargetItem = _items.NearestLoose(_player.Cell);
            _lootTarget = _lootTargetItem?.Cell;
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

            // The campaign keeps score: a win pays a bonus and moves the run
            // on to the next store (R does the moving, in Update); a loss is
            // recorded but the purse survives — you lose the round, not the
            // run. That's what makes the shop between rounds a real decision
            // rather than a scoreboard.
            if (phase == RunPhase.Won)
            {
                _campaign.AddCoins(CampaignState.WinBonus);
                if (!_roundHadTheft && !_roundAlarmCritical)
                {
                    _campaign.AddCoins(CleanRoundBonus);
                    _fx.Text(_player.WorldPos + new Vector3(0f, 2.4f, 0f),
                        "Clean store! +25c", new Color(180, 230, 180), 1.6f);
                }
                _campaign.RecordWin();
            }
            else
            {
                _campaign.RecordLoss();
            }

            // Persist the run so a restart of the game can continue it.
            try { _campaign.Save(SavePath()); } catch { /* never block the game */ }

            Sfx.Play(phase == RunPhase.Won ? SfxId.Win : SfxId.Lose, volume: 0.8f);
            if (phase == RunPhase.Won)
                EventSystem.Instance.Publish(new GameWonEvent(_elapsedTime));
            else
                EventSystem.Instance.Publish(new GameLostEvent(_elapsedTime, _items.SecuredCount));
        }

        private string _endMessage = "";

        /// <summary>
        /// One shop purchase, by number key. The catalog is slot-ordered, so
        /// slot N is item N; the HUD draws the same order, which is the only
        /// reason this is safe. The coin cue plays on success, and every
        /// attempt (including a refusal) publishes for the message feed —
        /// "can't afford it" is worth saying out loud, because a refused buy
        /// with no feedback feels like a dead key.
        /// </summary>
        private void TryBuySlot(int slot)
        {
            var items = ShopCatalog.All;
            if (slot < 1 || slot > items.Count) return;

            var item = items[slot - 1];
            int tierBefore = _campaign.Tier(item.Id);
            var result = _campaign.TryBuy(item.Id);

            if (result == BuyResult.Bought)
            {
                Sfx.Play(SfxId.Coin, volume: 0.55f);
                _fx.Text(_player.WorldPos + new Vector3(0f, 2f, 0f),
                    $"bought {item.Name}", new Color(245, 205, 110), 1.4f);
                try { _campaign.Save(SavePath()); } catch { }
            }
            else
            {
                Sfx.Play(SfxId.Drop, volume: 0.3f, pitch: -0.3f);
            }

            // The cost shown is what the *next* one costs, so an upgrade
            // reads "bought Fast shoes II (50c)" rather than naming the
            // price it actually charged.
            int cost = item.CostForTier(tierBefore);
            EventSystem.Instance.Publish(new ShopPurchaseEvent(item.Name, cost, result));
        }

        protected override void Draw(GameTime gameTime)
        {
            // The void behind the store is the palette's black, not a lifted
            // grey. A dark-but-not-black clear reads as haze and washes out
            // every surface in front of it.
            GraphicsDevice.Clear(ArcadePalette.Black);
            GraphicsDevice.DepthStencilState = DepthStencilState.Default; // enable depth testing for 3D

            if (_phase != RunPhase.Title)
            {
                _effect.View = _camera.View;
                _effect.Projection = _camera.Projection;
                _effect.LightingEnabled = true;

                // The store's lighting reacts to the alarm. A store that is about
                // to go off does not look like a calm one, and the ambient tint
                // is the cheapest way to say so — it needs no extra geometry and
                // it affects every surface in the room at once.
                // A power cut darkens the whole store on top of the alarm tint.
                float light = _darkTimer > 0f ? DarkLightFactor : 1f;
                // Day/night: a slow brightness pulse, dimmer at "night".
                float day = 0.85f + 0.15f * MathF.Sin(_dayPhase * MathF.Tau);
                if (_rainActive > 0f) day *= 0.9f;
                if (_fogActive > 0f) day *= 0.75f;
                _effect.AmbientLightColor = AmbientForAlarm(_alarm.Level) * light * day;
                // No specular on the store. BasicEffect's three directional
                // lights all carry a white specular term, and the material
                // SpecularColor multiplies it: at 0.2 the three summed gave a
                // flat ~0.36 white added to *every* surface, which lifted the
                // blacks and turned the saturated product colours into
                // pastels. That is what "washed out" was. A flat arcade
                // palette cannot survive a white highlight layer.
                _effect.SpecularColor = Vector3.Zero;
                DrawMesh(_floorVerts, FloorWorld, _textures.Floor);

                // Blocked cells: the border ring as plain wall, every interior
                // cell as a stocked shelf unit picked by style (aisle colour,
                // run end, open sides, product variant — see ShelfStyleFor).
                // The counter row is drawn in a different material so the goal is
                // visible from across the store.
                for (int y = 0; y < _map.Height; y++)
                {
                    for (int x = 0; x < _map.Width; x++)
                    {
                        if (_map[x, y] != 1)
                            continue;

                        if (x == 0 || y == 0 || x == _map.Width - 1 || y == _map.Height - 1)
                        {
                            var wall = Matrix.CreateScale(CellSize, CellSize * 1.5f, CellSize)
                                       * Matrix.CreateTranslation(x * CellSize, 0f, y * CellSize);
                            DrawMesh(_wallVerts, wall, _textures.Wall);
                        }
                        else
                        {
                            var style = ShelfStyleFor(x, y);
                            var mesh = _shelfVerts[ShelfSlot(style)][style.Variant];
                            // Slightly inset along X so run sections read as
                            // separate gondolas; full size along Z so the two
                            // rows of a block close into one unit.
                            var shelf = Matrix.CreateScale(CellSize * 0.96f, CellSize * 1.5f, CellSize)
                                        * Matrix.CreateTranslation(x * CellSize, 0f, y * CellSize);
                            DrawMesh(mesh, shelf, _textures.Unit);
                        }
                    }
                }

                DrawCounter();
                DrawDoor();
                DrawPlayer(gameTime);
                DrawLoot();
                DrawPowerUps();
                DrawHazards();
                DrawBall();
                DrawTrapsAndLaser();
                DrawCritters(gameTime);
                DrawCustomers(gameTime);
                DrawFx();
            }
            if (_phase == RunPhase.Title)
                _hud.DrawTitleScreen(BuildHudState());
            else
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
            for (int x = _map.CounterStart; x <= _map.CounterEnd; x++)
            {
                if (!_map.IsCounter(x, _map.CounterRow)) continue;
                var world = Matrix.CreateScale(CellSize * 0.9f, GridMap.CounterHeight, CellSize * 0.5f)
                            * Matrix.CreateTranslation(x * CellSize, 0f, _map.CounterRow * CellSize);
                DrawMesh(_propVerts, world, _textures.Counter);
            }
        }

        /// <summary>
        /// Draws the store's door: a vertical slab on the floor at the door
        /// cell, so customers have a visible entrance/exit to walk through.
        /// </summary>
        private void DrawDoor()
        {
            // Floor marker.
            var floorWorld = Matrix.CreateScale(CellSize * 0.9f, 0.05f, CellSize * 0.9f)
                           * Matrix.CreateTranslation(_doorCell.X * CellSize, 0.025f, _doorCell.Y * CellSize);
            DrawMesh(_propVerts, floorWorld, _textures.Flat, new Color(120, 80, 50));

            // Door slab.
            var slabWorld = Matrix.CreateScale(CellSize * 0.8f, CellSize * 1.6f, CellSize * 0.15f)
                          * Matrix.CreateTranslation(_doorCell.X * CellSize, CellSize * 0.8f, _doorCell.Y * CellSize);
            DrawMesh(_propVerts, slabWorld, _textures.Flat, new Color(150, 100, 60));
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
        private void DrawCustomers(GameTime gameTime)
        {
            if (_camera.Mode == CameraMode.FirstPerson) return;

            float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;
            if (_customers.Any(c => c.IsMoving)) _customerWalkPhase += dt * WalkCycleSpeed;
            else _customerWalkPhase = MathHelper.Lerp(_customerWalkPhase, 0f, 1f - MathF.Exp(-8f * dt));
            float swing = MathF.Sin(_customerWalkPhase) * 0.35f;

            foreach (var customer in _customers)
            {
                if (customer.Escaped) continue;
                var tint = new Color(170, 180, 200); 
                if (customer.IsShoplifter)
                {
                    //tint = new Color(220, 70, 60);
                    tint = new Color(255, 0, 0);
                }
                
                var facing = Matrix.CreateRotationY(customer.FacingAngle);
                var placement = Matrix.CreateTranslation(customer.WorldPos);

                foreach (var part in _customerParts)
                {
                    Matrix world;
                    if (part.SwingSign != 0f)
                        world = Matrix.CreateRotationX(swing * part.SwingSign)
                              * Matrix.CreateTranslation(part.Pivot)
                              * facing * placement;
                    else
                        world = facing * placement;
                    DrawMesh(part.Vertices, world, _textures.Flat, tint);
                }
            }
        }
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
        /// Draws the power-ups still on the floor, bobbing gently so they
        /// read as pickups rather than as dropped stock — and the treat while
        /// one is lingering, so the lure is visible to the player too (the
        /// cats are already reacting to it through the AI).
        /// </summary>
        private void DrawPowerUps()
        {
            foreach (var item in _powerups.Items)
            {
                var pos = item.WorldPos + new Vector3(0f, 0.35f + MathF.Sin(_elapsedTime * 3f + item.Cell.X) * 0.08f, 0f);
                var world = Matrix.CreateScale(CellSize * 0.2f, CellSize * 0.2f, CellSize * 0.2f)
                            * Matrix.CreateTranslation(pos);
                DrawMesh(_propVerts, world, _textures.Flat, item.Tint);
            }

            if (_treatTimer <= 0f) return;

            var treatPos = new Vector3(_treatCell.X * CellSize, 0.2f + MathF.Sin(_elapsedTime * 5f) * 0.05f, _treatCell.Y * CellSize);
            var treatWorld = Matrix.CreateScale(CellSize * 0.22f, CellSize * 0.14f, CellSize * 0.22f)
                             * Matrix.CreateTranslation(treatPos);
            DrawMesh(_propVerts, treatWorld, _textures.Flat, new Color(235, 190, 110));
        }

        /// <summary>
        /// Draws the spills (flat puddles you can see coming) and the fire
        /// alarm box — red while live, grey once pulled, so a spent alarm
        /// never baits you into standing on it again.
        /// </summary>
        private void DrawHazards()
        {
            foreach (var cell in _hazards.Spills)
            {
                var world = Matrix.CreateScale(CellSize * 0.72f, 0.04f, CellSize * 0.72f)
                            * Matrix.CreateTranslation(cell.X * CellSize, 0.02f, cell.Y * CellSize);
                DrawMesh(_propVerts, world, _textures.Flat, new Color(116, 168, 226));
            }

            if (_hazards.FireAlarmCell is { } alarmCell)
            {
                float y = 0.55f + MathF.Sin(_elapsedTime * 4f) * 0.05f;
                var world = Matrix.CreateScale(CellSize * 0.28f, CellSize * 0.34f, CellSize * 0.16f)
                            * Matrix.CreateTranslation(alarmCell.X * CellSize, y, alarmCell.Y * CellSize);
                DrawMesh(_propVerts, world, _textures.Flat,
                    _hazards.FireAlarmPulled ? new Color(110, 104, 100) : new Color(226, 66, 56));
            }
        }

        /// <summary>
        /// Draws the juice: every live particle as a small shrinking cube.
        /// Fading is done by lerping the tint toward black rather than by
        /// alpha — the sprite/3D state machine here is deliberately dumb, and
        /// a darkening speck reads as a fade against the store floor anyway.
        /// </summary>
        private void DrawFx()
        {
            foreach (var p in _fx.Particles)
            {
                float size = 0.1f * (0.35f + 0.65f * (1f - p.Age));
                var world = Matrix.CreateScale(size, size, size)
                            * Matrix.CreateTranslation(p.Position);
                DrawMesh(_propVerts, world, _textures.Flat,
                    Color.Lerp(Color.Black, p.Color, 1f - p.Age));
            }
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

        private void DrawTrapsAndLaser()
        {
            foreach (var trap in _traps)
            {
                var w = Matrix.CreateScale(0.6f, 0.15f, 0.6f)
                      * Matrix.CreateTranslation(trap.X * CellSize, 0.05f, trap.Y * CellSize);
                DrawMesh(_propVerts, w, _textures.Flat, new Color(180, 90, 90));
            }
            if (_laserTimer > 0f)
            {
                var w = Matrix.CreateScale(0.25f, 0.25f, 0.25f)
                      * Matrix.CreateTranslation(_laserCell.X * CellSize, 0.1f, _laserCell.Y * CellSize);
                DrawMesh(_propVerts, w, _textures.Flat, new Color(220, 80, 80));
            }
        }
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
            if (_camera.Mode == CameraMode.FirstPerson) return;

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

            // A stunned cat stands still and looks startled, whatever the AI
            // last wanted it to do. A cat that merely flinched (a ball
            // bouncing off a Moggy's head) plays the same pose but keeps its
            // freedom of movement.
            var pose = critter.Stunned || critter.Startled ? CatPose.Startle : critter.Pose;
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

        /// <summary>
        /// The ambient light colour for a given alarm level.
        ///
        /// Cool and neutral when the store is calm, warming towards red as
        /// the meter fills. The shift is eased rather than stepped, so the
        /// room's colour follows the meter continuously.
        /// </summary>
        private static Vector3 AmbientForAlarm(float level)
        {
            // Calm is neutral-cool, so wall and metal keep their own colour.
            var calm = new Vector3(0.52f, 0.53f, 0.58f);
            // Alarmed stays a *red shift*, not a red light. At the old
            // (0.62, 0.26, 0.24) every surface was lit almost entirely in red,
            // which turned the walls a flesh tone and drained the product
            // colours. Keeping the green/blue channels up leaves a room that
            // is clearly alarmed but still shows what is in it.
            var alarmed = new Vector3(0.58f, 0.33f, 0.30f);
            return Vector3.Lerp(calm, alarmed, MathHelper.Clamp(level, 0f, 1f));
        }

        // Snapshots everything the HUD needs for one frame. The HUD is
        // read-only: it draws the state, it never writes it back. Passing a
        // snapshot rather than the subsystems themselves is what keeps the
        // HUD from reaching into the game.
        private void TryLoadCustomLayout()
        {
            // Parsing and validation live in CustomStore, so a rejected file
            // says why instead of silently not appearing in the HUD.
            _customLayout = CustomStore.Load(CustomStore.FileName, out string? error);
            if (_customLayout == null && error != "not found")
                Console.WriteLine($"Custom store: {error}");
        }

        private static string SavePath() =>
            System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "StoreChase", "run.json");

        private void EnsureBindings()
        {
            if (_bindings.Count > 0) return;
            var m = InputMapper.Default;
            _bindings.Add(("Move up", () => m.MoveUp, v => m.MoveUp = v));
            _bindings.Add(("Move down", () => m.MoveDown, v => m.MoveDown = v));
            _bindings.Add(("Move left", () => m.MoveLeft, v => m.MoveLeft = v));
            _bindings.Add(("Move right", () => m.MoveRight, v => m.MoveRight = v));
            _bindings.Add(("Pan up", () => m.PanUp, v => m.PanUp = v));
            _bindings.Add(("Pan down", () => m.PanDown, v => m.PanDown = v));
            _bindings.Add(("Pan left", () => m.PanLeft, v => m.PanLeft = v));
            _bindings.Add(("Pan right", () => m.PanRight, v => m.PanRight = v));
            _bindings.Add(("Tilt up", () => m.TiltUp, v => m.TiltUp = v));
            _bindings.Add(("Tilt down", () => m.TiltDown, v => m.TiltDown = v));
            _bindings.Add(("Toggle camera", () => m.ToggleCamera, v => m.ToggleCamera = v));
            _bindings.Add(("Throw", () => m.Throw, v => m.Throw = v));
            _bindings.Add(("Use", () => m.Use, v => m.Use = v));
            _bindings.Add(("Crouch", () => m.Crouch, v => m.Crouch = v));
            _bindings.Add(("Restart", () => m.Restart, v => m.Restart = v));
            _bindings.Add(("Pause", () => m.Pause, v => m.Pause = v));
        }

        private int BindingCount() { EnsureBindings(); return _bindings.Count; }

        /// <summary>
        /// Throws the resumed run away: a fresh campaign, no save file. The
        /// purse and the round index survive only because the game saved them,
        /// so a player who wants the long way round has to be able to say so
        /// from the title screen.
        /// </summary>
        private void StartFreshRun()
        {
            try { System.IO.File.Delete(SavePath()); }
            catch { /* nothing to delete, or not deletable: either way we carry on */ }

            _campaign = new CampaignState();
            _runResumed = false;
            _fx.Text(_player.WorldPos + new Vector3(0f, 2.2f, 0f),
                "New run", new Color(150, 220, 255), 1.4f);
        }

        /// <summary>
        /// Applies a rebind, or explains why it was refused. A refused rebind
        /// is a message, not a silent no-op: the player has to be able to see
        /// that the key they picked is already doing something else.
        /// </summary>
        private string? TryRebind(int index, Keys key)
        {
            EnsureBindings();
            if (index < 0 || index >= _bindings.Count) return "no such binding";

            // A key that another row already owns is refused rather than
            // stolen: two actions on one key means one of them silently stops
            // working, and which one depends on the order the input layer
            // happens to read them in.
            var clash = _bindings.Select((b, i) => (b, i))
                                 .FirstOrDefault(t => t.i != index && t.b.Get() == key);
            if (clash.b.Get != null && clash.b.Get() == key)
                return $"'{key}' is already {clash.b.Name}";

            // Keys the rebind screen does not list but the game still acts on
            // (the arrow-key alternates, Start, Exit, the shop slots) are just
            // as protected.
            var taken = InputMapper.Default.HiddenKeys.FirstOrDefault(r => r.Key == key);
            if (taken.Name != null && taken.Key == key)
                return $"'{key}' is already {taken.Name}";

            _bindings[index].Set(key);
            InputMapper.SaveDefaults();
            return null;
        }

        private string BuildPausedText()
        {
            // The panel only changes when a win lands, so it is built once per
            // pause rather than re-joined 60 times a second.
            if (_pausedText != null) return _pausedText;

            string fastest = _achievements.FastestWin < float.MaxValue
                ? $"{_achievements.FastestWin:F1}s" : "none";
            _pausedText = $"PAUSED\n\n" +
                          $"Achievements: {_achievements.Unlocked.Count}/{_achievements.Total}\n" +
                          $"Fastest win: {fastest}\n\n" +
                          "P resume   R restart   Esc quit   O options";
            return _pausedText;
        }

        private string BuildOptionsText()
        {
            if (!_optionsTextDirty && _optionsText != null) return _optionsText;

            EnsureBindings();
            var sb = new System.Text.StringBuilder();
            sb.AppendLine(_paused ? "PAUSED" : "");
            sb.AppendLine($"Music volume: {(int)(_musicVolume * 10)}  (left/right)");
            sb.AppendLine($"Sfx volume:   {(int)(_sfxVolume * 10)}  (left/right)");
            sb.AppendLine("");
            sb.AppendLine("Bindings (Enter to rebind):");
            for (int i = 0; i < _bindings.Count; i++)
            {
                string sel = (i + 2 == _optionsSelection) ? "> " : "  ";
                string wait = (_rebindIndex == i) ? "  [press a key]" : "";
                sb.AppendLine($"{sel}{_bindings[i].Name}: {_bindings[i].Get()}{wait}");
            }
            if (!string.IsNullOrEmpty(_rebindMessage))
                sb.AppendLine($"  ({_rebindMessage})");
            sb.AppendLine("");
            sb.AppendLine("P resume   R restart   Esc quit   O close options");
            _optionsText = sb.ToString();
            _optionsTextDirty = false;
            return _optionsText;
        }

        private HudState BuildHudState() => new(
            Phase: _phase,
            ElapsedTime: _elapsedTime,
            EndMessage: _endMessage,
            SecuredCount: _items.SecuredCount,
            TotalCount: _items.TotalCount,
            PlayerCarrying: _items.Held != null,
            StolenCount: _items.StolenCount,
            AlarmCause: _alarm.Cause,
            AlarmStage: _alarm.Stage,
            AlarmLevel: _alarm.Level,
            ViewMode: _camera.Mode,
            ViewProjection: _camera.ViewProjection,
            Critters: _critters,
            Loot: _items.Items,
            PlayerCell: _player.Cell,
            Layout: _map.Layout,
            TitlePhase: _title.Phase,
            TitleTime: _title.Time,
            Pocketed: _pocket.Pocketed,
            // Normalised: 0 = silent, 1 = as loud as the store gets (a skid
            // while skating). Only the HUD reads this scale.
            Noise: Math.Clamp(_noise.CurrentLoudness / NoiseHudScale, 0f, 1f),
            Combo: _combo.Combo,
            TimeAttack: _timeAttack,
            TimeLeft: _timeLeft,
            TimeAttackMode: _timeAttackMode,
            CustomStoreAvailable: _customLayout != null,
            OrderText: _order.Text,
            OrderComplete: _order.Complete,
            Paused: _paused,
            OptionsText: _paused && _showOptions ? BuildOptionsText() : (_paused ? BuildPausedText() : ""),
            RunResumed: _runResumed,
            BoostSecondsLeft: _skateTimer,
            Popups: _fx.Texts,
            Coins: _campaign.Coins,
            RoundNumber: _campaign.CurrentSpec.Number,
            RestartHint: _phase == RunPhase.Won ? "[R] next store" : "[R] retry this store",
            // The shop is an end-phase object; while playing it stays empty
            // rather than being rebuilt (and affordability-checked) every
            // frame for a panel nobody can see.
            Shop: _phase == RunPhase.Playing ? Array.Empty<ShopOption>() : _campaign.ShopOptions());

        protected override void UnloadContent()
        {
            Music.Stop();
            _title?.Dispose();
            _textures?.Dispose();
            _hud?.Dispose();
            base.UnloadContent();
        }
    }
}
