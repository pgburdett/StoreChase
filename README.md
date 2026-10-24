# Store Chase 3D — a heist in a supermarket

A grid-based chase game in a 3D store. Cats wander the aisles and steal
merchandise off the shelves; you have to get it all back behind the counter
before the store alarm fills.

**Player instructions: see [`MANUAL.md`](MANUAL.md).** This README is the
design/decision log for developers.

Press **V** (or gamepad **Y**) to switch between the isometric overhead camera
and a first-person mouse-look view. The two aren't just different ways of
looking at the store — the first-person camera is a tactic, because a cat can
see whether you're looking straight at it.

The store is bigger than the screen. The camera follows the player, so the
aisles scroll past as you move; the shelf walls block your view, and a cat
two aisles away that has heard you is a different problem from one you can
see.

Everything visual is either procedural geometry, a texture generated in code,
or a runtime-loaded OBJ. There are no image or audio assets.

## Run it

```
dotnet restore
dotnet run
```

The content pipeline runs automatically on build. To edit content with the
MGCB CLI instead, `dotnet tool restore` (pins `dotnet-mgcb`) then use `mgcb`.

## Verifying changes

The game has a headless harness that exercises everything except the
rendering:

```
dotnet run --project harness/StoreChase.Harness
```

It plays whole rounds and asserts the heist rules, measures the animation rig
for tearing, smoke-tests the geometry builders, and checks the camera's
framing. It exits non-zero if anything fails, so it works as a smoke test
after any change. There are more modes:

```
--geometry    only the geometry, camera, loot and awareness checks
--rig         only the rig measurements
--rounds      only the round simulation
--trace       trace one round, frame by frame
--sweep       play many rounds at a range of alarm settings
```

## The round

Five items sit on the shelves (more, in later stores). Three cats wander the
store, and any of them will pick something up and make off with it. Two things
raise the alarm:

- **a cat is carrying something** — and the faster it rises the more cats are
  carrying, so a pile-up is a genuine crisis
- **you are carrying something** — at a much lower rate, so the walk back adds
  pressure without being a second timer

When nothing is out of place, the alarm drains. Get everything behind the
counter before it fills and you've won.

A cat that reaches the counter while carrying something **drops it there** and
the alarm jumps — bolting for the till is a delaying tactic that moves the
goods somewhere inconvenient, not a way out.

### The goods have kinds

The three kinds of goods are not just different colours:

- **cans** are cheap (the store barely notices one going missing) — and worth
  7c at the counter
- **bags** are worth the going rate — 10c
- **cartons** are expensive, and the alarm cares twice as much about them —
  15c

Each species also has a **taste**. The Shop Cat goes for the cartons and
ignores everything else; a Kitten will take anything but prefers the bags; the
Lounge Lizard only nibbles at tins and only if it has to. The HUD lists what
each cat is after, so knowing which goods to watch is a matter of reading the
panel.

### The store has moods

The alarm meter is not just a number. It passes through three stages, and the
cats react to them:

- **calm** — the cats go about their business
- **suspicious** — the lighting warms towards red and the cats get braver
- **critical** — the cats stop playing about and go for the goods whatever
  they feel like, which is the moment the store is most likely to be emptied
  out from under you

### The cats talk to each other

One cat seeing you is enough for the others to know. A cat that spots you
raises the alarm, and it carries to any cat within eight cells — but the
further the news travels, the vaguer it gets, so a cat across the store has
only a rough idea of where you were. A cat with a rough idea heads that way
rather than wandering off.

### Power-ups

Five one-use items sit on the shelves each round (one of every kind), and you
may carry **one at a time** (E on the keyboard, B on the gamepad):

- **roller skates** — 8 seconds at ×1.5 speed, the escape tool
- **spray bottle** — panics every cat within range while you walk, for as long
  as you keep moving. It does not make them drop what they're carrying; it's a
  repositioning tool, not a weapon
- **cat treat** — drop it and every cat within earshot makes for it, loot or
  no loot. The cheapest way to empty an aisle; a cat that eats one becomes a
  friend and shadows you instead of stealing
- **floor trap** — set it where you stand; the first cat to step on it is
  stunned 3s and drops whatever it was carrying
- **laser pointer** — a moving dot that wanders the aisles for 6s; cats
  chase it instead of you

You also start each round empty-pocketed; what you carried is what you spent.
(The shop between rounds sells more — see the campaign below.)

### The floor has opinions

Two spills and a fire alarm are laid out each round:

- **spills** (wet floor) — walk into one and you *slip*: in the isometric
  grid you can't brake for a beat, in first person you lag into momentum you
  didn't ask for. Slipping is faster for a moment (×1.35), so a slip can be
  an accident or a launch, depending on which way you were pointed. Skates
  double how long it takes to recover. Cats slip too, and a slipping cat
  drops what it was carrying — sometimes in the right direction
- **fire alarm** — stand on it and press Use to pull it. Every cat in the
  store panics (and drops any loot) and the alarm spikes by a fixed 15%.
  It's the one moment where the meter is your fault, so it's priced as a
  decision: trade 15% of the meter for total chaos

### Shift events

Mid-round, the store throws things at you (first event ~10–18s in, then every
12–22s; a short round may see none):

- **delivery** — fresh stock hits the shelves, pressure up
- **power cut** — the lights go; cats can barely see for a few seconds
- **alarm audit** — the manager audits the stockroom; the alarm drains a chunk
- **rat** — a rat runs the aisles; every cat chases it
- **bottle break** — a spill appears mid-aisle
- **customer drop** — a customer drops their basket; free goods on the floor

### The juice layer

`FxSystem` is the whole game-feel budget, and it's deliberately headless:
particles, floating texts, hit-stop, and camera shake are all pure state that
`Hud` and `Camera` render from. The rules that matter:

- particles fade by lerping their tint to black, so a burst dies to the
  background instead of popping out
- texts rise and fade — a popup that hangs in the air reads as a bug
- hit-stop (`HitStopSeconds`) crawls the *simulation* for a beat on a hard
  impact; the clock keeps running in real time, so a freeze never extends a
  round
- camera shake is a frustum translation (the focus point is untouched), and
  is deterministic when the store is calm — an idle round never wobbles

### The campaign: shops and coins

A run is a sequence of stores (see `Campaign.cs`, all headless):

- **rounds escalate** — loot grows by one every two rounds (capped at 8),
  cats every three (capped at 5), and the floor plan cycles through four
  stores, ending on the biggest
- **you bank coins** — every secured item is worth its alarm weight × 10 (a
  tin 7c, a bag 10c, a carton 15c), plus a 25c bonus for clearing a store.
  The purse survives losses: a loss costs you the round, not the run
- **the shop** is open on the end screen, nine slots on keys 1–9:
  four consumables (skates 15c, spray 20c, treat 18c, smoke bomb 24c —
  queued, and pocketed one per round) and five permanents in three tiers
  each, priced at list × (tier + 1): **fast shoes** (+7% speed/tier),
  **calm nerves** (alarm decay ×1.15/tier), **lucky charm** (counter-drop
  spike ×0.8/tier), **quiet shoes** (footsteps 25% quieter/tier),
  **steady hands** (alarm rises 10% slower while carrying/tier)
- **R advances** on a win and **retries** on a loss

The coin hook is `ItemField.ItemSecured`, so *every* path to the counter pays
exactly once: the player securing a bag, or a thief tackled at the till. The
counter-drop spike is scaled by the lucky charm's factor, since that's the
one upgrade that acts on an event rather than a rate.

## Controls

Keyboard / mouse and gamepad are both supported and **auto-detected** — whichever
device you use last becomes active (see Input below).

Keyboard / mouse:
- Arrow keys / WASD: move the player (in first-person, camera-relative: W
  forward, S back, A/D strafe)
- I / J / K / L: pan the camera (up/left/down/right)
- U / O: tilt the camera down/up
- Mouse wheel: zoom in/out
- V: toggle first-person / isometric view
- Mouse: look around in first-person (the pointer is captured)
- Left click / F: throw a ball (aimed along the camera's ground heading)
- E: use — the pocketed power-up, or pull the fire alarm if you're standing
  on one (the alarm wins that priority; it's the bigger button)
- 1–9: buy from the shop, between rounds
- Escape: quit
- R: between rounds — next store on a win, retry on a loss

Gamepad:
- Left stick / D-pad: move
- Right stick: pan the camera; look around in first-person
- Triggers (RT/LT): tilt
- Shoulders (RB/LB): zoom
- Y: toggle first-person / isometric view
- X: throw the ball
- B: use the pocketed power-up (or pull the fire alarm)
- Start: restart / next round
- Back: quit

The shop is currently keyboard-only (six items, six number keys); a gamepad
player can play the whole run but not spend the purse.

## Architecture at a glance

- `Program.cs` → `Game1.cs` (all rendering + orchestration). Gameplay files
  are top-level `.cs` files, namespace `StoreChase`.
- `StoreLayout.cs` — the floor plans: the grid, the counter, the player
  start, and the generator that makes a store of any size. `StoreLayouts.All`
  is the list.
- `GridMap.cs` — pure grid logic over a `StoreLayout`: walkability, BFS
  (`NextStepToward`, `DistanceMapFrom`), line of sight, the counter, and the
  cell enumerations the spawners use. `GridPos` lives in the same file.
- `Player.cs` / `Critter.cs` — movement. `Critter` is always grid-locked;
  `Player` is grid-locked in isometric and free world-space in first-person.
  Both expose `Vector3 WorldPos` (X/Z plane, Y=0) and have **no `Draw` method**;
  `Game1` owns all rendering. World space is X/Z with Y = up;
  `Game1.CellSize = 2f` world units per grid cell.
- `Camera.cs` — camera state and controls, plus the view test the AI uses to
  know whether it's being watched. It follows the player, so a store bigger
  than the screen scrolls.
- `ItemField.cs` / `LootItem.cs` — the merchandise and every rule about who is
  holding what.
- `AlarmSystem.cs` — the 0..1 meter, the conditions that move it, and the
  stages it passes through.
- `SharedAwareness.cs` — what the cats know about the player, and how the
  news travels between them.
- `Ball.cs` — the throwable, and the distraction it exists to provide.
- `PowerUps.cs` — the one-slot pocket (`PowerUpInventory`) and the field of
  pickups (`PowerUpField`).
- `Hazards.cs` — spills and fire alarms, and the per-frame hazard state the
  player and cats react to.
- `Campaign.cs` — the run between rounds: coin values, the shop catalog and
  buy rules (`ShopCatalog` / `CampaignState`), and the escalating round specs
  (`CampaignRounds.SpecFor`). Entirely headless.
- `FxSystem.cs` — the juice layer: particles, floating texts, and hit-stop
  state, all pure data that `Hud` renders.
- `Music.cs` — the adaptive soundtrack (see Sound below).
- `CritterProfile.cs` — every number that makes one species differ from
  another, including what it wants to steal.
- `ICritterAI.cs` / `SmartAnimalAI.cs` — the decision seam and the brain behind
  it.
- `CatAnimator.cs` — the procedural rig (see below).
- `StoreTextures.cs` — the store's surfaces, generated in code.
- `Sfx.cs` — every sound, synthesised at startup, including the proximity
  heartbeat.
- `Hud.cs` / `MessageFeed.cs` — the screen-space overlay and the event feed.
- Input, events, and the title each have their own small subsystem; see the
  sections below.
- `harness/StoreChase.Harness/` — the headless verification (see above).

## The heist loop

`ItemField` owns the items and the possession rules; the critter AI decides
*whether* it wants something and the game performs the transfer. That split is
deliberate — the AI is handed a `LootItem?` it may want and answers with
`WantsToSteal`, and the game does the bookkeeping.

The priority order in `SmartAnimalAI.DecideNextMove` is the whole design:

1. panicking (sprayed, or a fire alarm went off) → run, ignore everything
2. standing on loot and inclined to take it → stand still and take it
3. being watched, if this profile freezes → do nothing at all
4. a ball is bouncing nearby → go and look at the ball
5. a treat has landed nearby → go and eat it (feeding is befriending)
6. befriended → shadow the player
7. carrying something and the player is close → run, ideally for the till
8. carrying something and the player is far → make for the counter
9. the player is close → flee
10. the player is nearby → drift away
10b. heard about the player but can't see them → head for where they were
11. otherwise → wander, biased toward shelves

Step 2 is not a random walk: a cat paths *straight* at the nearest goods it
wants, so a store where the Shop Cat is loose is a store where the cartons
disappear first. That is the whole point of the tastes — the player has to
work out which goods are at risk and get to them, rather than treating every
item the same. "Inclined to take it" is also where **sneaky** bites: a
Burglar that is being watched declines (checked in `WantsToSteal`, every
frame, not just at the initial grab), so watching it genuinely stops it.

**Feeding is befriending.** A dropped treat is the lure it always was, but a
cat that reaches it now *eats* it (`WantsTreat`, the same AI-decides/game-
performs split as stealing), and eating is friendship: two treats and the cat
is yours for the round. A friend never steals — `ShouldTakeLoot` returns
false for friends before any drive roll, so not even a critical alarm tempts
it — it doesn't flee the player, and it shadows you a few cells behind. It
is also exempt from the tackle: a fed cat is not a thief. That makes the
whole pacifist route through a store a real one: treats for the species
whose thieving you fear most, a quiet word (or a punt) for the rest.

### Balance

The alarm rates were tuned against the headless simulation of full rounds (a
scripted player moving at the real `Player` grid speed, beelining for the
nearest item and ignoring the cats). At the original `StolenRate` of 0.055 that
bot won about a third of its rounds and lost almost every round in which
anything was stolen — there wasn't time left to chase a thief down and still
clear the store. `0.042` gives one unattended thief roughly 24 seconds, which
is enough to corner one, while two thieves still fill the meter in about 12.

The bot losing ~70% of the time is the intended read: it has no way to respond
to a theft at all, and a player who actually corners a thief has the margin.

The rates are *per second*, and a bigger store takes longer to clear, so they
are scaled by how much work the store is (`StoreLayout.AlarmScale`). Without
that, a bigger store is not harder but unwinnable: the player has further to
walk between the till and the shelves, and the meter fills before the round
can be finished. The scaling keeps the same amount of *alarm budget* in a
round whatever its size. The counter drop is a fixed fraction of the meter
rather than a scaled one, because it is a single event rather than a rate.

The sweep modes exist so these numbers can be chosen against data rather than
by playing a handful of rounds by hand:

```
dotnet run --project harness/StoreChase.Harness -- --sweep
```

## Critters

Seven species, differing only in a `CritterProfile` — speed, senses, planning
patience, how much they care about merchandise, and what they do when idle. A
new species is a new record, not a new class.

| | speed | alert | flee | thief | notes |
|---|---|---|---|---|---|
| Shop Cat | 4.2 | 8 | 5 | high | the one that actually plans a heist |
| Kitten | 5.6 | 10 | 7 | medium | re-decides constantly, jukes a lot |
| Lounge Lizard | 2.4 | 4 | 2 | low | sleeps through it; only runs when holding something |
| Nervous | 4.8 | 7 | 6 | medium | freezes while you look at it |
| Burglar Cat | 4.4 | 9 | 7 | high | won't shoplift while you watch; sprints (×1.4) once it has something; takes the cheap tins |
| Moggy | 2.0 | 3 | 2 | medium | enormous and unbothered: the ball doesn't stun it at all, and it plows through spills |
| Mouser | 6.0 | 12 | 8 | medium | the fastest thing in the store; grab-and-run on the light bags |

Three of the new fields in a profile are worth naming, because they changed
what the camera and the ball *are*:

- **`Sneaky`** — won't take loot while the player is watching. Pinning a
  Burglar mid-shoplift is a real use for the first-person camera, alongside
  freezing a Nervous one.
- **`StunDurationFactor`** — how much of a ball's stun the species keeps (0 =
  immune). The Moggy's immunity is physics, not a flag: it applies to the
  wet-floor trip too, so a spill is a hazard for some cats and scenery for
  others.
- **`CarrySpeedBoost`** — a multiplier applied only while carrying. The
  Burglar's getaway is a different problem from its shoplifting.

They're told apart in-world by size and a shader tint (`Tint` in
`CatNormalMap.fx`), since there's one model. The HUD lists who's in the store
and flags the one carrying something.

### Vision

`CritterContext` carries two separate booleans, and the difference is the
mechanic:

- **`CanSeePlayer`** — `GridMap.HasLineOfSight` (a Bresenham walk that shelves
  actually block) within the species' `VisionRadius`. This is *sight*, not
  reachability: a cat can see you straight down an aisle it can't walk to you
  along, which `DistanceMapFrom` would get wrong.
- **`BeingWatched`** — `Camera.IsInViewCone`. In first person it's a real cone
  (a 36° half-angle, so 72° across); overhead the camera looks straight down,
  so a cone would cover the whole store and mean nothing, and the test is
  instead "is it on screen". That distinction only became meaningful once
  the store grew bigger than the viewport: a cat across the store is genuinely
  out of sight. A Nervous cat roots itself while this is true.

So the overhead view is wide but bounded — you can pin a Nervous cat that is
in front of you, but not one you have to walk to — and first person is the
narrow, deliberate version where your gaze is the pin.

### The ball

Thrown along the camera's ground heading, bounces off shelves, and explodes
where it lands. It works in both camera modes, but only first person aims it
meaningfully: overhead, the camera looks straight down, so a throw always
travels the same way.

A ball that lands on a cat **stuns it** and makes it drop whatever it was
carrying — the cat sits out the stun and stays in the game. That is the only
offensive tool, and it doubles as a leash.

Cats that aren't `IgnoresBall` go and look at a ball that is bouncing — but
not a thief in the middle of an escape, which keeps a bolt for the counter
honest.

## Content & assets

- The MGCB content pipeline (`Content/Content.mgcb`) builds
  `Content/DefaultFont.spritefont` (UI text) and `Content/Effects/CatNormalMap.fx`
  (the cat's shader; effect compilation needs Wine on Linux/macOS — see
  AGENTS.md).
- `StoreTextures.cs` generates the floor tiles, shelf boards, cardboard and
  counter from code. They have to be seamless and mip-mapped — see below.
- The cat model — `Content/Models/Cat.obj`, `Cat.mtl`, `Cat_diffuse.jpg`,
  `Cat_bump.jpg` — is loaded **at runtime** by `ObjLoader.cs`, NOT through
  MGCB. `StoreChase.csproj` copies `Content\Models\**` to the output dir, so
  new model files just need to land under `Content/Models`.
- `ObjLoader` is deliberately narrow: single material, quad faces, `vt`/`vn`
  assumed present; uses 32-bit `int` indices, so vertex count isn't capped at
  65,535. `ObjLoader.ParseGeometry` is the same parser with no
  `GraphicsDevice` and no textures, which is what lets the animation rig be
  exercised headlessly (see "Working without a display").
- `CubeMesh.cs` builds the unit cube and floor quad as
  `VertexPositionNormalColorTexture` — MonoGame ships no Position+Normal+Color
  vertex, and every mesh drawn with the shared `BasicEffect` has to agree on one
  vertex declaration. The player samples a flat white texture and is shaded
  entirely by vertex color as before. (The old untextured
  `VertexPositionNormalColor` was deleted rather than kept: mixing the two under
  one effect means sampling a texture with an uninitialised TEXCOORD.)

### The two texture traps

Both of these bit during development and both fail *silently*:

1. **An incomplete mip chain samples black.** MonoGame has no
   `GenerateMipmaps` on `Texture2D`, so every texture here is written level by
   level down to 1×1. A texture with only level 0 is mipmap-incomplete, and a
   mipmapping sampler then returns black for every pixel — which is how the cat
   once rendered as a black silhouette.
2. **HLSL uniform initializers are not reliably applied** by the effect
   framework. Every uniform in `CatNormalMap.fx`, including the light colours,
   is pushed from C#. Zeroed lights also render black.

`BasicEffect.DiffuseColor` is sticky state that multiplies the texture, so
`DrawMesh` resets it to white on every draw — otherwise a tinted prop would
leave the shelves behind it in the same colour.

## Camera

`Camera.cs` is the single source of truth for camera state and controls. `Game1`
constructs it in `SetupCamera()` and calls `Camera.Update(gameTime, input,
playerWorldPos, playerFacing)`.

### Isometric (default)

- **The camera follows the player.** The viewport is a fixed window of
  `Camera.ViewCellsWide` (16) cells across at zoom 1, scaled by zoom, and the
  look-at point is the player plus any manual panning. That is what makes the
  store scroll: a store bigger than the viewport is a window onto a floor
  plan, not a diorama that zooms itself out to fit.
- The look-at point is clamped so the viewport never leaves the floor plan
  (`Camera.ClampToMap`). The player can walk right up to a wall, and a view
  centred there would show half the screen full of nothing outside the store.
  When the store is smaller than the viewport — a small map at maximum
  zoom-out — the view is pinned to the middle of the store instead.
- Pan (`_panOffset`, world units) is an *offset from the player*, not an
  absolute camera position, so it can't drift the view away from them
  entirely. It is clamped to `mapSize * 0.5`. Pan speed scales with zoom.
- Zoom scales the viewport size in both modes, so it feels continuous across
  the tilt transition. Zoom 1.0 is standard; smaller = closer, larger =
  farther. Clamped `MinZoom` 0.25 to `MaxZoom` 2.5.
- Tilt goes 0 → `MaxTiltDegrees` (80). At tilt ≈ 0 the camera sits directly
  above the look-at point and uses `Matrix.CreateOrthographic` (no perspective
  distortion). Once tilted at all it swings back along +Z, pivoting around
  the look-at point, and switches to `Matrix.CreatePerspectiveFieldOfView` (50°).
  The camera distance is derived from the viewport height, so tilting keeps
  roughly the same amount of store in frame and the view does not shrink and
  grow as the player zooms.
- The camera always uses world `Up`. A pure top-down view would make the look
  direction parallel to `Up` (and the up vector ambiguous), so `Camera` nudges
  its position by a tiny fraction of the camera distance along +Z. That keeps
  the look direction ever-so-slightly off-vertical at every angle, so the same
  `Up` is valid from 0° to 80° with no up-vector flip (an earlier revision
  swapped `Forward`→`Up` at the 0°/1° boundary and got a one-frame roll).

### First-person (V / gamepad Y)

`CameraMode.FirstPerson` seats a 60° perspective camera at the player's
head (`FirstPersonEyeHeight` = 2.2, the PersonMesh head center) and drives it
with mouse-look:

- Yaw/pitch come from `InputState.Look` (right/up positive) scaled by
  `LookSensitivity` rad/unit; pitch clamps to ±`MaxPitchDegrees` (85). Yaw 0
  means +Z, matching `Player.FacingAngle`, and entering FPS seeds yaw from
  the player's facing so you start off looking where the player faces. Forward is
  `(sin yaw, cos yaw)` and the camera's right is `cross(forward, up)`, so
  mouse-right turns right, mouse-up looks up.
- Movement is **free (not grid-locked)** in first-person. `Camera.MapMoveToWorld`
  turns WASD into a normalized camera-relative XZ heading (W heads where you
  look, S backs up, A/D strafe); `Player` moves continuously at `FreeMoveSpeed`
  with per-axis collision so you slide along shelves. The catch check and
  critter AI are still grid-based, so `Player.Cell` tracks the nearest walkable
  tile while free-moving; toggling back to isometric snaps the player to that
  cell center so the grid state stays coherent.
- `Camera.FacingOnPlane` is the view direction flattened to the XZ plane, and
  is what a thrown ball is aimed along: pitching up or down shouldn't change
  where it lands.
- The view is rebuilt from the player's current `WorldPos` each frame, so it
  tracks the player while they move. Iso pan/zoom/tilt are left untouched while
  in FPS and restored exactly when you toggle back.
- `Game1` hides the cursor, captures the pointer (see Input), and skips drawing
  the player's own body since the camera sits in its head.

## The cat rig

The OBJ is a single rigid pose with no skeleton, so there are no bones to
drive. `CatAnimator` derives a rig from the mesh's own geometry once, at
construction, and rewrites vertices every frame:

- **legs** — four hip-pivot clusters found in the low band, diagonal trot
- **knees** — a second joint partway down each leg, so a leg can bend
- **spine** — the whole torso rotating about the rear hip
- **head** — a neck pivot at the front of the torso, which tracks the player
  once they've been noticed
- **tail** — a base pivot with an arc that grows toward the tip, plus an idle
  swish layered on top of whatever curl the pose asked for

The model's own axes are odd (up is +Z, the nose is at −Y), so the rig rotates
in model space; see the orientation notes in `Game1.BuildCritterModelTransform`.

All landmarks are fractions of the model's bounding box, so a re-exported or
rescaled cat still works. Where they came from was measured, not guessed: the
head is the dense cluster from Y=−22.3 back to about Y=−13 (the ears reach the
top of the model at Z=37.6), the neck is around Y=−11, the tail is a thin
isolated spike past Y≈17, the front paws sit at Y≈−12 and the rear at Y≈+8.
(The model's up axis is +Z and its nose-to-tail axis is −Y, so "the top of the
model" is a Z maximum and "the nose" is a Y minimum.)

### Why the amplitudes are small

Every pose in `PoseTargets` is deliberately restrained, and the numbers came
from measurement rather than taste. A rigid mesh bends by rotating regions
about pivots, and a vertex caught part-way through a blend is rotated by a
*fraction* of the angle — so wide blends shear the surface apart. A first pass
with confident angles produced a shapeless blob for `sit` (5.4× edge stretch
and a rear up on end), and a head turn that dragged the front paws along with
it (7× on leg edges), because the paws sit at the same Y as the neck.

Three findings shaped the final rig:

- **Gates, not just bands.** The head band is additionally gated by *height*
  and the tail band by *width*. Head and paws share a Y range; tail and haunches
  share a Y range. A Y threshold alone cannot separate them.
- **A crouch folds the legs; it doesn't sink them.** `CrouchFold` solves the
  hip angle that shortens a two-link leg by the same amount the body dropped, so
  the paws stay planted and the knees travel outward. `BodyDropMax` is capped
  at 0.16 of model height; past that the knee shear gets visible.
- **The knee takes twice the hip's angle**, which is what makes it a Z-shaped
  fold rather than a lean, and its blend is wide (26% of body height) so the
  difference is spread along the leg instead of shearing at one band.

The counter-intuitive one: **the spine rotation is a far worse shear source
than the crouch.** `Sit` was originally a deep crouch plus a -0.45 rad spine
tilt, and produced a shapeless blob; a -0.26 rad tilt still sheared head edges
3.5x, while the *deeper* crouch in `Loaf` measures clean. The reason is
geometry — the torso pivots about the rear hip, so the shoulders travel about
14 units per radian, and the shoulders sit inside the head blend. A crouch
folds the legs instead, which is a local deformation near the ground where
there's geometry to hide it. `Sit` is therefore built almost entirely from the
crouch, with the spine tilt reduced to -0.10 rad.

### Poses

`CatPose` is a behaviour the AI chooses, not a frame the game scripts:
`Stand`, `Walk`, `Sit`, `Groom`, `Stretch`, `Loaf`, `Startle`. `CatPose.Walk`
is deliberately the same rest configuration as `Stand`, because the gait comes
from `IsMoving` — so a cat can change pose mid-stride without popping out of
its walk cycle. Everything is eased toward its target, so a state change blends
rather than snaps.

## Cat model

The critter is the uploaded cat OBJ (`Content/Models/Cat.obj`, single material,
quad faces), loaded and rendered at runtime rather than through MGCB.

**Why runtime loading** instead of the content pipeline: the OBJ uses quad
faces and a specific 3ds-Max-export layout. Rather than depend on MGCB's model
importer handling that correctly, `ObjLoader.cs` is a small hand-written parser
that reads `v`/`vt`/`vn`/`f`, fan-triangulates faces, dedupes shared corners,
and pulls the diffuse filename out of the referenced `.mtl`.

**Getting the orientation right wasn't a guess** — the raw OBJ's axes don't
match MonoGame's, so the actual vertex data was inspected to confirm it:
- X (±6.3 units) — narrow, left/right width.
- Y (-22.3 to 38.6, the largest extent) — nose-to-tail axis. Confirmed by
  checking vertex density at each end: the Y-minimum end has ~9,200 nearby
  vertices in a wide (6.4×7.5 unit) cluster (the body/head), while the
  Y-maximum end has only ~250 vertices in a narrow (2.0×2.5 unit) cluster (the
  tail tip). Head is at Y-minimum.
- Z (0.01 to 37.6) — vertical/height axis. Confirmed by checking that
  paw-height vertices (near Z=0) span nearly the model's full Y range, and that
  near the head end (low Y), Z ranges 18–35 — consistent with a standing pose,
  head held up off the ground.

`BuildCritterModelTransform()` in `Game1.cs` uses this to build one transform:
center on X/Z, rotate -90° around X (maps old Z-up to new Y-up), scale to fit
one grid cell's height, then lift so the paws land on Y=0. Each step was traced
numerically with real coordinates, so `Critter.FacingAngle = 0` correctly means
"facing +Z" and the model rotates via `Matrix.CreateRotationY(FacingAngle)`.
(Independently re-checked: sampling `Cat_diffuse.jpg` at the OBJ's UVs puts the
dark eye/face texels at the Y-minimum end and plain fur at the tail end, and
MonoGame's `CreateRotationX(-90°)` maps that -Y nose to +Z.)

**Normal mapping**: the cat is drawn with a custom effect
(`Content/Effects/CatNormalMap.fx`) rather than `BasicEffect`, so the bump map
actually contributes. `Cat.mtl` references it (`map_Bump Cat_bump.jpg`); that
image is a flat grayscale height map, not a tangent-space normal map, so
`ObjLoader` bakes it into one at load (central differences) and derives
per-vertex tangents from the OBJ's UVs (`ComputeTangents`, handedness stored in
the tangent's `w`). The shader rebuilds the tangent basis, perturbs the normal
per pixel, and lights it with the same key/fill/back setup as BasicEffect's
`EnableDefaultLighting` so the cat sits consistently beside the floor/walls.
A per-critter `Tint` uniform carries the species colour.

**Known rough edges**:
- `ObjLoader` builds a mip chain for both the diffuse and the baked normal map,
  and `DrawCritters` uses `SamplerState.AnisotropicClamp` on both slots, so the
  diffuse no longer aliases when the cat is small on screen.
- Collision/catch is purely grid-cell based (`Critter.Cell`), unaffected by
  the visual model footprint.
- Each critter owns its own parsed copy of the geometry (~36k vertices), because
  the rig rewrites vertices in place. Sharing one mesh would mean every cat in
  frame shows whichever was drawn last.

## Input

All device input goes through a small abstraction so gameplay never touches the
device APIs:

- `InputState.cs` — a per-frame, device-agnostic snapshot: `Move`, `CameraPan`,
  `Tilt`, `Zoom`, `Look`, `ToggleCamera`, `Throw`, `Use`, `BuySlot`, `Restart`,
  `Exit`, plus `HasActivity`. `Use` and `BuySlot` are rising edges (so are
  `ToggleCamera` and `Throw`); `BuySlot` is 1-based, 0 for none — safe as a
  default because a struct default is zero.
- `IInputSource.cs` — one physical device. `InputDeviceKind` (`KeyboardMouse`,
  `GamePad`) tells the rest of the game what the player is using. The optional
  `IMouseLookSource` capability (`MouseLookEnabled`/`MouseLookCenter`) lets a
  pointer-owning source switch from absolute position to a centered, relative
  look delta; `InputSystem` forwards the camera's capture request to any source
  that implements it.
- `KeyboardMouseInput.cs` / `GamePadInput.cs` — the **only** files allowed to
  call `Keyboard`/`Mouse`/`GamePad`. Both turn the toggle button (V / Y), the
  throw button (click / X) and the use button (E / B) into rising edges so
  holding doesn't repeat. The keyboard source additionally edge-detects D1–D6
  into `BuySlot` (the gamepad leaves it 0 — the shop is keyboard-only).
- `InputSystem.cs` — polls every source each frame (so per-device deltas like
  the mouse wheel stay current) and exposes the active device's state as
  `State`. The active device is auto-detected: the current source keeps control
  while it has input, otherwise the first other source with input takes over.
  `ActiveDevice`/`ActiveSource` expose the current one.

`Camera.Update` takes `in InputState`; `Player.Update` takes an already-resolved
`moveDir` plus a `freeMove` flag. Add a new `IInputSource` (and register it in
`Game1.Initialize`) rather than reading devices from gameplay code.

## Events

`EventSystem.cs` is a thread-safe pub/sub singleton (`EventSystem.Instance`).
Concrete events subclass `BaseEvent`:

- `GameStartedEvent` — published on initial startup and each restart.
- `GameWonEvent` — published when every item is back behind the counter.
- `GameLostEvent` — published when the alarm fills; carries how much was
  recovered.
- `ItemStolenEvent` / `ItemSecuredEvent` — the heist's individual beats.
- `PowerUpCollectedEvent` / `PowerUpUsedEvent` — the pocket filling and emptying.
- `PlayerSlippedEvent` / `FireAlarmPulledEvent` — the hazards going off (the
  latter carries how many cats scattered).
- `ShopPurchaseEvent` — a between-rounds buy, with the result (bought, can't
  afford, maxed) so the feed can say something sensible for each.

`RunStats.cs` subscribes and logs a summary to the console (wins, losses, best
time, and thefts per species — which is the cheapest read on whether one
profile is too effective). It isn't stored on `Game1` — the singleton's handler
list keeps it rooted. Add new events as `sealed` subclasses of `BaseEvent` and
publish via `EventSystem.Instance.Publish`.

## Sound

`Sfx.cs` synthesises every cue into a PCM buffer at startup — a sine glide for
a pickup, a two-tone blip, a pitch-dropping thud for a drop, filtered noise for
a tackle, and a formant-ish meow whose pitch contour does most of the work. No
audio files, which keeps the project asset-free.

If there's no audio device the constructor throws, which is caught once at
`Initialise` and turns the whole system into no-ops. The game is fully playable
in silence.

### The proximity cue

The headline use of audio is the **heartbeat**. It plays faster and louder (and
higher pitched) as the nearest cat closes, over a 9-unit range: about 1.15s
between beats at the edge, 0.42s when it's on top of you, with the response
eased so the alarm creeps rather than flipping from calm to panic.

It exists because the isometric camera can't cheaply answer "is something
behind me" — a cat two aisles away is off the edge of a zoomed-in view, and a
shelf maze has a lot of blind corners. One timer, and it turns a spatial
question into an audible one.

Two smaller ones: `PlayAt` attenuates a bounce by distance (the ball announcing
itself *is* the mechanic, so it has to be locatable by ear), and the alarm
meter gets its own blip that only starts past a third full and accelerates —
so the quiet early part of a round stays quiet. Meows and the heartbeat are
positional too (`PlayAt` takes a world position and a pitch), so a theft
you can't see is a theft you can hear coming from the right aisle.

### The soundtrack

`Music.cs` is a second synthesiser with one job: be the store's mood. It runs
on a `DynamicSoundEffectInstance` fed from a background thread (never touch
graphics from there — same rule as `RainbowTitle`), gated on `Sfx.Available`,
and stopped in `UnloadContent`.

Three layers ease in and out on the alarm stage — calm, suspicious, critical —
and the tempo steps 84 → 96 → 124 BPM with it. The only input it gets is the
alarm stage (`Music.SetMood`), so it stays a pure function of the game state
and is testable as one (`MusicChecks`). Oscillator phases run on real time, so
a hiccup in the fill thread changes volume, not pitch.

## Enhancements

The five directions below are all live. Each is a small system that stays
`GraphicsDevice`-free so the harness can exercise it, and each publishes
through `EventSystem` so the feed and `RunStats` pick it up for free.

### Noise and stealth (`NoiseSystem.cs`)

One pop per cell crossed, plus a louder, longer-lived clatter when a spill
takes you down. Each pop fades over ~1.3s (clatter ~2.2s); the loudest one
in a cat's ear (scaled by `CritterProfile.HearingRadius`) sets what it knows,
folded into `SharedAwareness` as a vagueness of `max(1, distance)`. Standing
still is silence — that is the whole stealth loop. Skates make you louder
(`Game1.SkatesNoiseLoudness`), a skid is louder still, and the HUD's noise
pip reads the normalised loudness. `Campaign.NoiseFactor` (quiet shoes) scales
every footstep.

### Befriending cats

A dropped treat is still a lure, but a cat that reaches it now *eats* it
(`WantsTreat`, same AI-decides/game-performs split as stealing), and eating
is friendship: two treats and the cat is yours for the round. A friend never
steals (`ShouldTakeLoot` returns false before any drive roll, so even a
critical alarm won't tempt it), doesn't flee, and shadows you a few cells
behind. It is also exempt from the tackle. The priority list in
`SmartAnimalAI` is updated to match (see the heist loop above).

### Shift events (`ShiftEvents.cs`)

A pure scheduler fires a random event every 12–22s (first one 10–18s in):
a delivery (+2 loose items), a power cut (8s of darkness: cats keep 40% of
their vision, the store dims to 45%), a manager's audit (alarm −20%), or a
rat (every cat chases it for 6s). The schedule is the testable part; the
effects live in `Game1.ApplyShiftEvent`. `AlarmSystem.Drain` mirrors
`Spike`.

### Customers and the shoplifter

`Customer` (pure grid mover) and `CustomerBrain` drive a handful of shoppers
who wander the aisles; one per round is a shoplifter who pockets a loose
item when unwatched and makes for the exit. Catching one (walk onto them
while they carry) makes them drop it; reaching the exit with it means the
goods are gone. `ItemField` gained `TakeByCustomer`/`DropFromCustomer`; the
shoplifter's goods count as stolen for the alarm. Drawn with the same
`PersonMesh` parts, tinted to tell them apart.

### Gadgets (shop keys 1–9)

Three more shop rows: quiet shoes (permanent, 3 tiers — feeds
`Campaign.NoiseFactor`), steady hands (permanent, 3 tiers — slows the alarm
while carrying), and a smoke bomb (consumable — panics every cat in earshot
for six seconds). `KeyboardMouseInput` now reads keys 1–9.

## Working without a display

None of `GridMap`, `Critter`, `SmartAnimalAI`, `ItemField`, `AlarmSystem`,
`SharedAwareness`, `Ball`, `PowerUps`, `Hazards`, `FxSystem` or `Campaign`
touch a `GraphicsDevice`, `ObjLoader.ParseGeometry` returns the geometry with
no textures and no device, and `Camera` has a constructor that takes an
aspect ratio instead of a device. That makes the whole simulation testable
from a console project referencing the game — which is what
`harness/StoreChase.Harness` is.

- **Play full rounds headlessly** and assert on the rules: items never
  duplicate or vanish, an item is never in two hands, both outcomes are
  reachable, a passive player never wins, and no round deadlocks. This is how
  the alarm balance and three real bugs were found — an item orphaned in the
  `Held` state when a thief was tackled at the counter, a `NearestLoose` sort
  key that made a chasing player oscillate between two items forever, and a
  thief that reached the counter and stood there holding the loot forever,
  which made any round with a theft unwinnable.
- **Measure the rig** for edge stretch per body region against the rest pose,
  to catch tearing numerically instead of by eye.
- **Smoke-test the pure geometry builders** and assert on invariants rather
  than golden values: UVs reach the repeat counts, normals are unit length,
  the cube still sits on the floor, a shelf actually blocks line of sight, a
  thrown ball travels a useful distance and never ends up inside geometry. The
  ball checks caught two physics bugs that were invisible in the source.
- **Check the camera's framing**: that the viewport is smaller than the store,
  that the view follows the player, that it never leaves the floor plan, and
  that the far side of the store is genuinely off screen.
- **Sweep the alarm** across a range of settings and report the win rate for
  each, so the numbers are chosen against data rather than by hand.

All of them were necessary. The rig in particular cannot be judged by reading
it, and its first pass looked plausible in code and was unusable on screen.
The player-cannot-stop bug is the other one worth remembering: it was not in
the code, it was in a comment explaining why it was deliberate.

## Things you'll likely want to tweak

1. **Camera framing** — the viewport is `Camera.ViewCellsWide` cells across at
   zoom 1, scaled by zoom and clamped to the store's edges; adjust in
   `Camera.UpdateCameraMatrices`.
2. **Wall height** — currently `CellSize * 1.5f`. In a true top-down orthographic
   view taller walls don't occlude more floor, so height mostly matters if you
   tilt.
3. **Alarm balance** — `StolenRate`, `PlayerCarryingRate`, `DecayRate` and
   `GraceSeconds` on `AlarmSystem`. The per-round workload now comes from
   `CampaignRounds.SpecFor` (loot/critters per round), with `Game1.LootCount`
   and `CritterCount` as the reference values the round simulation runs
   against. Re-run the headless round simulation after changing any of them.
4. **Campaign pacing** — the escalation slopes and caps in
   `CampaignRounds.SpecFor` (loot `5 + i/2` capped 8, critters `3 + i/3`
   capped 5), the store path, and the win bonus / coin values in
   `CampaignState`. The shop prices live in `ShopCatalog`.
5. **Rig amplitudes** — the constants at the top of `CatAnimator`, and the pose
   table in `PoseTargets`. Raise them, then re-measure edge stretch; the caps
   are there because of what the measurements said.
6. **First-person feel** — `Player.FreeMoveSpeed` (world units/sec) and
   `Player.FreeRadius` (collision radius) tune free movement; `Camera.LookSensitivity`
   (radians per look unit) and `Camera.MaxPitchDegrees` tune the look.
7. **Power-ups and hazards** — durations and strengths are constants on
   `Game1` (`SkatesSeconds`, `SkatesSpeedBoost`, `FireAlarmSpike`,
   `SlipSeconds`) and `Player.SlipSpeedBoost`; the cat's treat radius is
   `SmartAnimalAI.TreatRadius`.
8. **Music** — the layer mix and per-stage tempo live in `MusicSynth`; the
   mood input is just the alarm stage, so it stays safe to re-tune by ear.
