# Store Chase 3D — MonoGame Top-Down (Orthographic)

A small grid-based chase game in a 3D scene viewed from above: you (a blue
procedural person) hunt the cat through store shelves. Everything visual is
either procedural geometry or a runtime-loaded OBJ — no external scene assets.

## Run it

```
dotnet restore
dotnet run
```
The content pipeline runs automatically on build. To edit content with the
MGCB CLI instead, `dotnet tool restore` (pins `dotnet-mgcb`) then use `mgcb`.

## Controls

Keyboard/mouse and gamepad are both supported and **auto-detected** — whichever
device you use last becomes active (see Input below).

Keyboard / mouse:
- Arrow keys / WASD: move the player
- I / J / K / L: pan the camera (up/left/down/right)
- U / O: tilt the camera down/up
- Mouse wheel: zoom in/out
- Escape: quit
- R: restart, once you've caught the critter

Gamepad:
- Left stick / D-pad: move
- Right stick: pan the camera
- Triggers (RT/LT): tilt
- Shoulders (RB/LB): zoom
- Start: restart
- Back: quit

## Architecture at a glance

- `Program.cs` → `Game1.cs` (all rendering + orchestration). Gameplay files
  are top-level `.cs` files, namespace `StoreChase`.
- `GridMap.cs` — pure grid logic (walkable/BFS: `NextStepToward`,
  `DistanceMapFrom`). `GridPos` lives in the same file. Map layout is a
  hardcoded string array, **18×12**.
- `Player.cs` / `Critter.cs` — grid-locked movement. They expose `Vector3
  WorldPos` (X/Z plane, Y=0) and have **no `Draw` method**; `Game1` owns all
  rendering. World space is X/Z with Y = up; `Game1.CellSize = 2f` world units
  per grid cell.
- `Camera.cs` — camera state and controls (extracted out of `Game1`, see below).
- Input, events, and the title each have their own small subsystem; see the
  sections below.

## Content & assets

- The MGCB content pipeline (`Content/Content.mgcb`) builds only
  `Content/DefaultFont.spritefont` (UI text, currently `DejaVu Sans`). If the
  build fails with "font not found" on another machine, change `<FontName>` to
  an installed font and rebuild.
- The cat model — `Content/Models/Cat.obj`, `Cat.mtl`, `Cat_diffuse.jpg`,
  `Cat_bump.jpg` — is loaded **at runtime** by `ObjLoader.cs`, NOT through MGCB.
  `StoreChase.csproj` copies `Content\Models\**` to the output dir, so new
  model files just need to land under `Content/Models`.
- `ObjLoader` is deliberately narrow: single material, quad faces, `vt`/`vn`
  assumed present; uses 32-bit `int` indices, so vertex count isn't capped at
  65,535.
- `CubeMesh.cs` builds cube/floor geometry by hand as `VertexPositionNormalColor`
  (a custom vertex type — MonoGame ships no Position+Normal+Color struct, and
  Normal is required for `BasicEffect` lighting). `PersonMesh.cs` reuses the
  same codepath to build a small humanoid (head/torso/arms/legs) for the player,
  returned as parts each tagged with a joint pivot. `Game1.DrawPlayer` rotates
  the arms/legs around those pivots each frame with an opposite-phase sine swing
  (eased back to rest when idle) — so the walk cycle is purely procedural, no
  animation assets. `Game1` draws these with `DrawUserPrimitives`.

## Camera

`Camera.cs` is the single source of truth for pan, zoom, and tilt. `Game1`
constructs it in `SetupCamera()` and calls `Camera.Update(gameTime, input)`.

- Pan (`_panOffset`, world units) is clamped to `mapSize * 0.75` so you can't
  drift far from the store. Pan speed scales with zoom.
- Zoom is expressed as camera *distance* in both modes, so it feels continuous
  across the tilt transition. Zoom 1.0 is standard; smaller = closer,
  larger = farther. Clamped `MinZoom` 0.25 to `MaxZoom` 2.5.
- Tilt goes 0 → `MaxTiltDegrees` (80). At tilt ≈ 0 the camera sits directly
  above the look-at point and uses `Matrix.CreateOrthographic` (no perspective
  distortion). Once tilted at all it swings back along +Z, pivoting around the
  look-at point, and switches to `Matrix.CreatePerspectiveFieldOfView` (50°).
- **Known seam**: the view matrix's "up" vector has to flip from `Forward`
  (only valid at tilt=0, where the look direction is parallel to world Up) to
  world `Up` (any nonzero tilt). This causes a one-frame roll snap right at the
  0°/1° boundary. Brief and easy to miss, but visible if you linger at exactly
  that boundary; smoothing would mean blending up vectors across a small range.

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

**Not wired up**: no normal mapping/bump — `BasicEffect` doesn't support it;
that needs a custom shader. `Content/Models/Cat_bump.jpg` ships next to the
model and `Cat.mtl` references it (`map_Bump Cat_bump.jpg`), but `ObjLoader`
ignores `map_Bump`, so at runtime the bump has no effect. The cat renders with
diffuse texture + default directional lighting only.

**Procedurally animated legs**: OBJ has no skeleton/bones, so rather than an
animation asset, `CatWalkAnimator.cs` derives the four legs from the mesh's own
geometry — vertices in the bottom ~20% band, split left/right by X and
nose/tail by Y (the four paw clusters straddle those with clear gaps). Each
frame it rotates leg-weighted vertices around their hip pivot (at 45% of model
height) with a diagonal trot gait (front-left + back-right in phase). Weight
falls off to zero toward the hip and radially out from each leg centroid, so the
body doesn't tear or deform. `Game1.DrawCritter` updates it before drawing;
`Critter.IsMoving` drives the cycle. All thresholds are fractions of model
height, so a re-exported/rescaled cat still works. `StaticMesh` still stores only
geometry, so a future animated mesh (glTF/FBX with bones) could replace it
alongside the procedural path.

**Known rough edges**:
- No mipmapping/sampler tuning — the 346KB diffuse at a small on-screen size can
  alias; `SamplerState.AnisotropicClamp` before drawing the cat would help.
- Collision/catch is purely grid-cell based (`Critter.Cell`), unaffected by the
  visual model footprint.

## Critter AI

The AI is split behind an interface: `ICritterAI.cs` defines `CritterContext`
(a `record struct`) and `DecideNextMove`; `SmartAnimalAI.cs` implements the
behavior. `Critter.cs` handles movement/catch and calls the injected AI —
`Game1` constructs it as `new Critter(pos, CellSize, new SmartAnimalAI())`.

`SmartAnimalAI` has three distance-based tiers (tunables are the public
properties at the top of `SmartAnimalAI.cs`, **not** in `Critter.cs`):

- **Alert** (within `AlertRadius` = 8): drifts away using a quick
  Manhattan-distance check on immediate neighbors — casual, not committed.
- **Flee** (within `FleeRadius` = 4): builds a real escape plan. Runs
  `GridMap.DistanceMapFrom` (wall-respecting BFS) from both itself and the
  player, scores reachable tiles by "how far from the player, penalized for
  detour distance," and paths to the best via `GridMap.NextStepToward`.
- **Wander** (otherwise): random walkable steps.

`QuirkChance` (20%) is meant to make the critter occasionally double back or
sidestep. Note: `ChooseQuirkMove` is currently a **stub that returns null**, so
quirks don't actually fire yet — the roll just costs it a frame. `_lastCell`
(and its build warning) is the leftover intended for this.

Tunables: `ReplanInterval`, `AlertRadius`, `FleeRadius`, `QuirkChance`.
`ReplanInterval` gates flee replans via `_replanTimer`, but only partially: the
timer is decremented only while the path is empty (so pacing is effectively
"per finished path, then wait out the remainder of the interval"), and the
alert/wander tiers never consult it.

**Cost note**: each re-plan does two full-grid BFS scans. Trivial at this map
size, but if you scale the maze up, increase `ReplanInterval` or cache one map.

## Input

All device input goes through a small abstraction so gameplay never touches the
device APIs:

- `InputState.cs` — a per-frame, device-agnostic snapshot: `Move`, `CameraPan`,
  `Tilt`, `Zoom`, `Restart`, `Exit`, plus `HasActivity`.
- `IInputSource.cs` — one physical device. `InputDeviceKind` (`KeyboardMouse`,
  `GamePad`) tells the rest of the game what the player is using.
- `KeyboardMouseInput.cs` / `GamePadInput.cs` — the **only** files allowed to
  call `Keyboard`/`Mouse`/`GamePad`.
- `InputSystem.cs` — polls every source each frame (so per-device deltas like
  the mouse wheel stay current) and exposes the active device's state as
  `State`. The active device is auto-detected: the current source keeps control
  while it has input, otherwise the first other source with input takes over.
  `ActiveDevice`/`ActiveSource` expose the current one.

`Player.Update` and `Camera.Update` take `in InputState`; add a new
`IInputSource` (and register it in `Game1.Initialize`) rather than reading
devices from gameplay code.

## Events

`EventSystem.cs` is a thread-safe pub/sub singleton (`EventSystem.Instance`).
Concrete events subclass `BaseEvent`:

- `GameStartedEvent` — published on initial startup and each restart.
- `GameWonEvent` — published when the critter is caught; carries `ElapsedTime`.

`Game1` publishes these. `RunStats.cs` subscribes and logs a win summary to the
console (win count, run count, best time). It isn't stored on `Game1` — the
singleton's handler list keeps it rooted. Add new events as `sealed` subclasses
of `BaseEvent` and publish via `EventSystem.Instance.Publish`.

## Rainbow title

`RainbowTitle.cs` drives the centered "Store Chase" title. Its hue phase is
advanced on a **background thread** (requested), while `Game1.DrawRainbowTitle`
only reads `Phase` and draws each glyph with a per-character hue offset for the
rainbow gradient. Keep all graphics work on the game thread — `SpriteBatch` and
`GraphicsDevice` are not thread-safe. `Game1.UnloadContent()` disposes the
title's thread.

## HUD

The top of the screen shows the animated title, a small status indicator
(gray = chasing, green = caught), and the elapsed-time / win text rendered with
the `DefaultFont` SpriteFont.

## Things you'll likely want to tweak

1. **Camera height/framing** — orthographic width/height is map size plus
   `CellSize * 2` padding, scaled by zoom; adjust in `Camera.UpdateCameraMatrices`.
2. **Wall height** — currently `CellSize * 1.5f`. In a true top-down orthographic
   view taller walls don't occlude more floor, so height mostly matters if you
   tilt.
3. **No textures on the cubes** — flat vertex color. Texture-mapping them is a
   bigger jump (UVs on the vertex type, `Texture2D`, `_effect.TextureEnabled`).
