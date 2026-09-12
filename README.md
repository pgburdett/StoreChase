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
