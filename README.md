# Store Chase 3D — a heist in a supermarket

A grid-based chase game in a 3D store. Cats wander the aisles and steal
merchandise off the shelves; you have to get it all back behind the counter
before the store alarm fills.

Press **V** (or gamepad **Y**) to switch between the isometric overhead camera
and a first-person mouse-look view. The two aren't just different ways of
looking at the store — the first-person camera is a tactic, because a cat can
see whether you're looking straight at it.

Everything visual is either procedural geometry, a texture generated in code,
or a runtime-loaded OBJ. There are no image or audio assets.

## Run it

```
dotnet restore
dotnet run
```

The content pipeline runs automatically on build. To edit content with the
MGCB CLI instead, `dotnet tool restore` (pins `dotnet-mgcb`) then use `mgcb`.

## The round

Five items sit on the shelves. Three cats wander the store, and any of them
will pick something up and make off with it. Two things raise the alarm:

- **a cat is carrying something** — and the faster it rises the more cats are
  carrying, so a pile-up is a genuine crisis
- **you are carrying something** — at a much lower rate, so the walk back adds
  pressure without being a second timer

When nothing is out of place, the alarm drains. Get all five behind the counter
before it fills and you've won.

A cat that reaches the counter while carrying something **drops it there** and
the alarm jumps — bolting for the till is a delaying tactic that moves the
goods somewhere inconvenient, not a way out.

## Controls

Keyboard/mouse and gamepad are both supported and **auto-detected** — whichever
device you use last becomes active (see Input below).

Keyboard / mouse:
- Arrow keys / WASD: move the player (in first-person, camera-relative: W
  forward, S back, A/D strafe)
- I / J / K / L: pan the camera (up/left/down/right)
- U / O: tilt the camera down/up
- Mouse wheel: zoom in/out
- V: toggle first-person / isometric view
- Mouse: look around in first-person (the pointer is captured)
- Left click / F: throw a ball (first-person only)
- Escape: quit
- R: restart, once the round is over

Gamepad:
- Left stick / D-pad: move
- Right stick: pan the camera; look around in first-person
- Triggers (RT/LT): tilt
- Shoulders (RB/LB): zoom
- Y: toggle first-person / isometric view
- X: throw a ball
- Start: restart
- Back: quit

## Architecture at a glance

- `Program.cs` → `Game1.cs` (all rendering + orchestration). Gameplay files
  are top-level `.cs` files, namespace `StoreChase`.
- `GridMap.cs` — pure grid logic: walkability, BFS (`NextStepToward`,
  `DistanceMapFrom`), line of sight, the counter row, and the cell enumerations
  the spawners use. `GridPos` lives in the same file. Map layout is a hardcoded
  string array, **18×12**.
- `Player.cs` / `Critter.cs` — movement. `Critter` is always grid-locked;
  `Player` is grid-locked in isometric and free world-space in first-person.
  Both expose `Vector3 WorldPos` (X/Z plane, Y=0) and have **no `Draw` method**;
  `Game1` owns all rendering. World space is X/Z with Y = up;
  `Game1.CellSize = 2f` world units per grid cell.
- `Camera.cs` — camera state and controls, plus the view-cone test the AI uses
  to know whether it's being watched.
- `ItemField.cs` / `LootItem.cs` — the merchandise and every rule about who is
  holding what.
- `AlarmSystem.cs` — the 0..1 meter and the conditions that move it.
- `Ball.cs` — the throwable, and the distraction it exists to provide.
- `CritterProfile.cs` — every number that makes one species differ from
  another.
- `ICritterAI.cs` / `SmartAnimalAI.cs` — the decision seam and the brain behind
  it.
- `CatAnimator.cs` — the procedural rig (see below).
- `StoreTextures.cs` — the store's surfaces, generated in code.
- `Sfx.cs` — every sound, synthesised at startup, including the proximity
  heartbeat.
- Input, events, and the title each have their own small subsystem; see the
  sections below.

## The heist loop

`ItemField` owns the items and the possession rules; the critter AI decides
*whether* it wants something and the game performs the transfer. That split is
deliberate — the AI is handed a `LootItem?` it may want and answers with
`WantsToSteal`, and the game does the bookkeeping.

The priority order in `SmartAnimalAI.DecideNextMove` is the whole design:

1. standing on loot and inclined to take it → stand still and take it
2. being watched, if this profile freezes → do nothing at all
3. a ball is bouncing nearby → go and look at the ball
4. carrying something, player close → run
5. carrying something, player far → make for the counter
6. the player is close → flee
7. the player is nearby → drift away
8. otherwise → wander, biased toward shelves

Step 8's bias matters more than it looks: the loot is *on the shelves*, so a
wandering cat tends to wander into some. That keeps the heist going without
every cat pathing across the store the moment something appears.

### Balance

The alarm rates were tuned against a headless simulation of full rounds (a
scripted player moving at the real `Player` grid speed, beelining for the
nearest item and ignoring the cats). At the original `StolenRate` of 0.055 that
bot won about a third of its rounds and lost almost every round in which
anything was stolen — there wasn't time left to chase a thief down and still
clear the store. `0.042` gives one unattended thief roughly 24 seconds, which
is enough to corner one, while two thieves still fill the meter in about 12.
Rounds run 10–35 seconds.

The bot losing ~70% of the time is the intended read: it has no way to respond
to a theft at all, and a player who actually corners a thief has the margin.

## Critters

Four species, differing only in a `CritterProfile` — speed, senses, planning
patience, how much they care about merchandise, and what they do when idle. A
new species is a new record, not a new class.

| | speed | alert | flee | thief | notes |
|---|---|---|---|---|---|
| Shop Cat | 4.2 | 8 | 5 | high | the one that actually plans a heist |
| Kitten | 5.6 | 10 | 7 | medium | re-decides constantly, jukes a lot |
| Lounge Lizard | 2.4 | 4 | 2 | low | sleeps through it; only runs when holding something |
| Nervous | 4.8 | 7 | 6 | medium | freezes while you look at it |

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
- **`BeingWatched`** — `Camera.IsInViewCone`, a 36° cone in first person and a
  near-180° one overhead. A Nervous cat roots itself while this is true.

That's why the overhead camera is a real cost rather than a neutral view: from
up there everything is in frame, so a Nervous cat is never pinned.

### The ball

Thrown in first person along the camera's ground heading, bounces off shelves,
settles, and lies there being interesting for seven seconds. Cats that aren't
`IgnoresBall` abandon everything to go and look at it — including a thief
mid-escape, which is how you pull one off a good hiding place. It's the only
offensive tool, and it doubles as a leash.

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

- Pan (`_panOffset`, world units) is clamped to `mapSize * 0.75` so you can't
  drift far from the store. Pan speed scales with zoom.
- Zoom is expressed as camera *distance* in both modes, so it feels continuous
  across the tilt transition. Zoom 1.0 is standard; smaller = closer,
  larger = farther. Clamped `MinZoom` 0.25 to `MaxZoom` 2.5.
- Tilt goes 0 → `MaxTiltDegrees` (80). At tilt ≈ 0 the camera sits directly
  above the look-at point and uses `Matrix.CreateOrthographic` (no perspective
  distortion). Once tilted at all it swings back along +Z, pivoting around
  the look-at point, and switches to `Matrix.CreatePerspectiveFieldOfView` (50°).
- The camera always uses world `Up`. A pure top-down view would make the look
  direction parallel to `Up` (and the up vector ambiguous), so `Camera` nudges
  its position by a tiny fraction of the camera distance along +Z. That keeps
  the look direction ever-so-slightly off-vertical at every angle, so the same
  `Up` is valid from 0° to 80° with no up-vector flip (an earlier revision
  swapped `Forward`→`Up` at the 0°/1° boundary and got a one-frame roll).

### First-person (V / gamepad Y)

`CameraViewMode.FirstPerson` seats a 60° perspective camera at the player's
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
top of the model at Z=38.6), the neck is around Y=−11, the tail is a thin
isolated spike past Y≈17, the front paws sit at Y≈−12 and the rear at Y≈+8.

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
  `Tilt`, `Zoom`, `Look`, `ToggleCamera`, `Throw`, `Restart`, `Exit`, plus
  `HasActivity`.
- `IInputSource.cs` — one physical device. `InputDeviceKind` (`KeyboardMouse`,
  `GamePad`) tells the rest of the game what the player is using. The optional
  `IMouseLookSource` capability (`MouseLookEnabled`/`MouseLookCenter`) lets a
  pointer-owning source switch from absolute position to a centered, relative
  look delta; `InputSystem` forwards the camera's capture request to any source
  that implements it.
- `KeyboardMouseInput.cs` / `GamePadInput.cs` — the **only** files allowed to
  call `Keyboard`/`Mouse`/`GamePad`. Both turn the toggle button (V / Y) and the
  throw button (click / X) into rising edges so holding doesn't repeat.
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
so the quiet early part of a round stays quiet.

## Working without a display

None of `GridMap`, `Critter`, `SmartAnimalAI`, `ItemField`, `AlarmSystem` or
`Ball` touch a `GraphicsDevice`, and `ObjLoader.ParseGeometry` returns the
geometry with no textures and no device. That makes the whole simulation
testable from a console project referencing the game:

- **Play full rounds headlessly** and assert on the rules: items never
  duplicate or vanish, an item is never in two hands, both outcomes are
  reachable, and no round deadlocks. This is how the alarm balance and two real
  bugs were found — an item orphaned in the `Held` state when a thief was
  tackled at the counter, and a `NearestLoose` sort key that made a chasing
  player oscillate between two items forever.
- **Render the rig** with a small software rasterizer, and measure edge stretch
  per body region against the rest pose to catch tearing numerically instead of
  by eye.
- **Smoke-test the pure geometry builders** and assert on invariants rather
  than golden values: UVs reach the repeat counts, normals are unit length, the
  cube still sits on the floor, a shelf actually blocks line of sight, a thrown
  ball travels a useful distance and never ends up inside geometry. The ball
  checks caught two physics bugs that were invisible in the source.

All three were necessary. The rig in particular cannot be judged by reading it,
and its first pass looked plausible in code and was unusable on screen.

## Things you'll likely want to tweak

1. **Camera height/framing** — orthographic width/height is map size plus
   `CellSize * 2` padding, scaled by zoom; adjust in `Camera.UpdateCameraMatrices`.
2. **Wall height** — currently `CellSize * 1.5f`. In a true top-down orthographic
   view taller walls don't occlude more floor, so height mostly matters if you
   tilt.
3. **Alarm balance** — `StolenRate`, `PlayerCarryingRate`, `DecayRate` and
   `GraceSeconds` on `AlarmSystem`, and `LootCount` on `Game1`. Re-run the
   headless round simulation after changing any of them.
4. **Rig amplitudes** — the constants at the top of `CatAnimator`, and the pose
   table in `PoseTargets`. Raise them, then re-measure edge stretch; the caps
   are there because of what the measurements said.
5. **First-person feel** — `Player.FreeMoveSpeed` (world units/sec) and
   `Player.FreeRadius` (collision radius) tune free movement; `Camera.LookSensitivity`
   (radians per look unit) and `Camera.MaxPitchDegrees` tune the look.
