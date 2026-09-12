# Store Chase 3D — MonoGame Top-Down (Orthographic)

Same maze, movement, and AI as the 2D version — swapped to a real 3D scene
viewed straight down, with cube "models" (blue = you, orange = critter,
brown = shelves).

## Run it

Same as before:
```
dotnet restore
dotnet run
```
(If you haven't already: `dotnet tool install dotnet-mgcb` from a manifest,
per the earlier setup.)

## Controls
- Arrow keys / WASD: move the player
- I / J / K / L: pan the camera (up/left/down/right)
- U / O: tilt the camera down/up
- Mouse wheel: zoom in/out
- Escape: quit
- R: restart, once you've caught the critter

## What changed from the 2D version

- **GridMap.cs** — untouched. It's pure grid logic (walkable/BFS), no
  rendering dependency, so it ported over as-is.
- **Player.cs / Critter.cs** — same movement/AI logic, just exposing
  `Vector3 WorldPos` (X/Z plane, Y=0) instead of `Vector2 PixelPos`, and no
  `Draw` method — Game1 now owns all rendering since it's managing the 3D
  pipeline.
- **CubeMesh.cs** (new) — builds cube and floor-quad geometry by hand as
  `VertexPositionNormalColor` arrays. This *is* the "just squares for
  models" approach: no external 3D model files, no content pipeline model
  loading — just procedural geometry. A custom vertex type was needed
  because MonoGame doesn't ship a Position+Normal+Color struct out of the
  box (Normal is required for lighting).
- **Game1.cs** — the real work:
  - `SetupCamera()`: `Matrix.CreateOrthographic` for a true top-down view
    with no perspective distortion (per your choice), positioned above the
    map's center looking straight down.
  - `BasicEffect` with `LightingEnabled = true` and
    `EnableDefaultLighting()` so the cubes actually shade like 3D objects
    instead of flat colored squares — you'll see the top faces lit
    differently than the sides.
  - Walls are drawn as scaled-up cubes (taller than wide) so they read as
    "shelves" from directly above; floor is one big flat quad under
    everything.
  - `DrawUserPrimitives` is used instead of index/vertex buffers — simplest
    approach for a small number of objects; fine for this scale, but if you
    add hundreds of wall tiles later, batching or instancing would help
    perf.

## Things you'll likely want to tweak

1. **Camera height/framing** — `SetupCamera()` positions the camera 30
   units up and the orthographic view size is map size + padding. If the
   maze looks too small/large on screen, adjust the `+ CellSize * 2`
   padding or the camera height.

2. **Wall height** — currently `CellSize * 1.5f` tall. Since it's a true
   top-down orthographic view, taller walls won't visually occlude more of
   the floor behind them the way they would in a perspective camera — so
   height mostly matters if you ever add a slight camera tilt later.

3. **No textures** — literally flat-colored cube faces via vertex color.
   If you want texture-mapped cubes later, that's a bigger jump: UV
   coordinates on the vertex type, a `Texture2D` loaded via content
   pipeline, and `_effect.TextureEnabled = true`.

4. **No font**, same as before — UI is a colored dot. See the note in
   `Game1.LoadContent()`/`DrawUiOverlay()` for how to wire up a
   SpriteFont for real text (timer, win message).

5. **Straight-down only** — you picked orthographic/no-tilt. If you
   later want a slight angle for more depth perception, that's a small
   change: adjust the camera's Y offset to include some Z/X offset too,
   and switch `CreateOrthographic` to `CreatePerspectiveFieldOfView` if you
   want actual perspective distortion instead of an angled orthographic.

## Camera controls (pan/zoom)

- `_cameraPanOffset` (Vector2, world units) shifts where the camera looks,
  applied on top of the map's center. I/J/K/L adjust it each frame in
  `UpdateCameraControls()`.
- `_zoom` scales the orthographic view size passed to
  `Matrix.CreateOrthographic` — smaller view size = more zoomed in. Clamped
  between `MinZoom` (0.25, close up) and `MaxZoom` (2.5, zoomed way out) so
  you can't zoom through the floor or out to nothing.
- Panning is clamped to `mapSize * 0.75` in each direction so you can't
  drift the camera arbitrarily far from the store.
- Pan speed scales with current zoom (`PanSpeed * _zoom`) so panning feels
  similarly fast whether you're zoomed in or out — otherwise panning while
  zoomed in would feel sluggish relative to the visible area.
- Mouse wheel delta is computed by diffing `Mouse.GetState().ScrollWheelValue`
  against the previous frame's value, since MonoGame reports it as a
  cumulative total rather than a per-frame delta.

## Cat model (replaces the procedural critter cube)

The critter is now the uploaded cat OBJ (`Content/Models/Cat.obj`,
35,290 vertices, single material, quad faces) instead of an orange cube,
loaded and rendered at runtime.

**Why runtime loading instead of the MGCB content pipeline**: this OBJ
uses quad faces and a specific 3ds-Max-export layout. Rather than depend on
MGCB's built-in model importer handling that correctly, `ObjLoader.cs` is a
small hand-written parser — it reads `v`/`vt`/`vn`/`f` lines, triangulates
each quad into two triangles, dedupes shared vertex corners, and pulls the
diffuse texture filename out of the referenced `.mtl`. It's deliberately
narrow: single material, no n-gons beyond quads, assumes vt/vn are present
for every vertex. If you ever load a different OBJ and it doesn't work,
that's the first place to check.

**Getting the orientation right wasn't a guess** — the raw OBJ's axes don't
match MonoGame's, and rather than assume a convention, the actual vertex
data was inspected to confirm it:
- X (±6.3 units) — narrow, left/right width.
- Y (-22.3 to 38.6, the largest extent) — nose-to-tail axis. Confirmed by
  checking vertex density at each end: the Y-minimum end has ~9,200 nearby
  vertices in a wide (6.4×7.5 unit) cluster (the body/head), while the
  Y-maximum end has only ~250 vertices in a narrow (2.0×2.5 unit) cluster
  (the tail tip). Head is at Y-minimum.
- Z (0.01 to 37.6) — vertical/height axis. Confirmed by checking that
  paw-height vertices (near Z=0) span nearly the model's full Y range
  (front and back paws both near the ground), and that near the head end
  (low Y), Z ranges 18–35 — consistent with a standing/crouching pose,
  head held up off the ground.

`BuildCritterModelTransform()` in `Game1.cs` uses this to build one
transform: center on X/Z, rotate -90° around X (maps old Z-up to new
Y-up), scale to fit one grid cell's height, then lift so the paws land on
Y=0. Each step was traced numerically with real coordinates (not just
reasoned about) to confirm: a paw vertex ends up exactly at Y=0, and a
nose vertex ends up at positive Z — which is why `Critter.FacingAngle = 0`
(computed from direction of travel) correctly means "facing +Z" and the
model rotates via `Matrix.CreateRotationY(FacingAngle)` to face wherever
it's currently moving.

**What's NOT wired up**: `Cat_bump.jpg` (the bump/normal map) is copied
into `Content/Models/` but never loaded or used. `BasicEffect` doesn't
support normal mapping — that needs a custom shader (`Effect`/HLSL) with a
normal-mapping technique, which is a meaningfully bigger addition than
anything else here. The cat currently renders with diffuse texture +
`BasicEffect`'s default directional lighting only, no surface bump detail.

**Static pose, animation-ready structure**: this OBJ has no skeleton/bones
(OBJ format doesn't support that) — it's one fixed pose that translates,
rotates, and scales as a rigid whole. `StaticMesh` intentionally stores
just geometry with no baked-in transform, so a future animated model
(glTF or FBX with bones, needing a different loader entirely) could
replace it without restructuring how `Critter`/`Game1` consume a mesh —
both already work in terms of "a mesh, a per-frame world transform," which
is the same shape an animated skeletal mesh needs, just computed via bone
matrices instead of one static transform.

**Known rough edges**:
- No mipmapping/texture filtering tuning — large diffuse texture (346KB
  JPG) at a small on-screen size (cat fills less than one grid cell) may
  show some aliasing/shimmer, especially if the camera tilts to a shallow
  angle. Enabling `SamplerState.AnisotropicClamp` on the graphics device
  before drawing the cat would likely help if you notice this.
- `ObjLoader` uses `short` indices, capped at 65,535 unique vertices. This
  model's ~35K vertices fit comfortably, but a denser model would need
  switching to `int` indices and `DrawUserIndexedPrimitives`'s 32-bit
  overload.
- The critter's collision/catch logic still operates purely on grid cells
  (`Critter.Cell`), unaffected by the visual model swap — catching the cat
  still means landing on the same tile, regardless of the model's visual
  footprint being smaller than a full cell.

## Critter AI

Two awareness tiers instead of one flat flee radius:

- **Alert** (within 8 tiles): the critter starts drifting away using a
  quick Manhattan-distance check on its immediate neighbors — casual, not
  committed to a route. This is what makes it seem to notice you coming
  before you're right on top of it.
- **Flee** (within 4 tiles): the critter does a real escape plan. It runs
  `GridMap.DistanceMapFrom` (a full BFS distance map, so it respects walls
  rather than judging distance as the crow flies) from both itself and the
  player, scores every reachable tile within 6 steps by "how far from the
  player, penalized for how far out of the way it is to reach," and paths
  to the best one via `GridMap.NextStepToward`. It re-plans every 0.6s
  rather than every tile, so it commits to a route instead of twitching
  tile-to-tile.
- **Quirks**: 20% chance per flee decision to ignore the plan and either
  double back through the tile it just came from, or sidestep
  perpendicular to the escape direction — both deliberately suboptimal, so
  it doesn't read as a perfect pathfinding bot. The abandoned plan gets
  rebuilt next tick.

Tunable constants are all at the top of `Critter.cs`: `AlertRadius`,
`FleeRadius`, `ReplanInterval`, `QuirkChance`. Turning `QuirkChance` down to
0 gives you a "serious" critter that always takes the optimal escape route;
turning it up makes it feel more erratic/animal-like (and easier to corner,
since it'll sometimes dodge into a dead end).

**Cost note**: each re-plan does two full-grid BFS scans. Trivial at this
map's size (19×12), but if you significantly scale up the maze, consider
increasing `ReplanInterval` or caching one of the two distance maps.

- `_tiltDegrees` (0 to `MaxTiltDegrees` = 80) drives both the camera's
  position and which projection is used. U tilts down toward 0 (flat
  top-down), O tilts up toward 80 (near-horizontal, angled view).
- At tilt ≈ 0, the camera sits directly above the look-at point and uses
  `Matrix.CreateOrthographic` — identical to the original straight-down
  view, no perspective distortion.
- Once tilted at all, the camera swings back along +Z (pivoting around the
  look-at point, distance controlled by `baseDistance` = zoom) and the
  projection switches to `Matrix.CreatePerspectiveFieldOfView` with a fixed
  50° FOV, so distant objects appear smaller — real depth perception,
  at the cost of the flat, distortion-free look.
- Zoom (`_zoom`) is expressed as camera *distance* in both modes now,
  rather than orthographic view size — this keeps zoom behavior roughly
  consistent whether you're flat or tilted, since a perspective camera
  doesn't have a "view size" to shrink the way an orthographic one does.
- **Known seam**: the view matrix's "up" vector has to flip from `Forward`
  (used only at tilt=0, since the look direction is otherwise parallel to
  world Up) to world `Up` (used for any nonzero tilt) — this causes a
  one-frame snap in camera roll right at the 0°/1° boundary. It's brief and
  easy to miss during normal use, but if you build something that lingers
  exactly at that boundary (e.g. an animated tilt reset), you'll see it.
  Smoothing this out would mean blending the up vector across a small tilt
  range instead of switching it outright.
