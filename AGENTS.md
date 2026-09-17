# AGENTS.md

Single-project C# **MonoGame DesktopGL** game targeting `net10.0` (see `StoreChase.csproj`). No tests, no CI, no solution file. Layout: namespace `StoreChase`, entrypoint `Program.cs` → `Game1.cs`. All gameplay files are top-level `.cs` files in the repo root.

## Build / run

- Build: `dotnet build`. Run: `dotnet run`.
- To edit content via the MGCB CLI: `dotnet tool restore` (pins `dotnet-mgcb` 3.8.5.1 in `dotnet-tools.json`), then use `mgcb`. Not needed for plain builds — the `MonoGame.Content.Builder.Task` package runs the content build automatically.
- **Effect compilation needs Wine on Linux/macOS.** `Content/Effects/CatNormalMap.fx` is built by MGCB, which shells out to a Windows HLSL compiler under Wine. This machine is already set up (`~/~/.winemonogame` via MonoGame's `net8_mgfxc_wine_setup.sh`), and `MGFXC_WINE_PATH` is exported from `~/.profile`/`~/.zprofile`. If a build fails with `MGFXC0001`, open a new login shell (or `export MGFXC_WINE_PATH=...`) so MGCB can find that prefix.

## Known warnings

`dotnet build` is clean (0 warnings). The old unused-`_lastCell` warning is gone — the field is now used by the critter quirk logic.

## Content & assets

- The MGCB content pipeline (`Content/Content.mgcb`) builds `Content/DefaultFont.spritefont` (UI text) and `Content/Effects/CatNormalMap.fx` (the cat's normal-mapped shader). Everything else visual is either procedural geometry (`CubeMesh.cs`, `PersonMesh.cs`, `VertexPositionNormalColor`) or a runtime-loaded OBJ. The player is a procedural humanoid whose arms/legs swing around joint pivots each frame (`PersonMesh` + `Game1.DrawPlayer`) — no animation assets.
- `DefaultFont.spritefont` names the system font `DejaVu Sans`; if the content build fails with "font not found" on another machine/OS, change `<FontName>` to an installed font and rebuild (`dotnet tool restore` then `dotnet build`).
- The cat model — `Content/Models/Cat.obj`, `Cat.mtl`, `Cat_diffuse.jpg`, `Cat_bump.jpg` — is loaded **at runtime** by `ObjLoader.cs` from the relative path `Content/Models/Cat.obj`, NOT through MGCB. `StoreChase.csproj` already copies `Content\Models\**` to the output dir (`CopyToOutputDirectory`), so new model files only work if they land under `Content/Models` — do not register them in `Content.mgcb`.
- `ObjLoader` is deliberately narrow: single material, quad faces, `vt`/`vn` assumed present; uses 32-bit `int` indices (no 65,535 vertex cap). It produces `VertexPositionNormalTextureTangent` (position/normal/UV/tangent+handedness), computes tangents from UVs, and bakes the grayscale `map_Bump` height map into a tangent-space normal map. Diffuse/normal textures get a generated mip chain — every level is filled, because an incomplete chain samples black under a mipmapping sampler. If a new OBJ fails, start there.

## Architecture

- `GridMap.cs` is pure grid logic (walkable/BFS: `NextStepToward`, `DistanceMapFrom`); `GridPos` is defined in that same file (`GridMap.cs:134`). Map layout is a hardcoded string array, 18×12.
- World space is X/Z with Y = up. `Game1.CellSize = 2f` world units per grid cell. `Player`/`Critter` expose `WorldPos` (X/Z, Y=0) and have **no `Draw` method** — `Game1` owns all rendering and the camera. `Player.Update(gameTime, map, moveDir, freeMove)` has two modes: grid-locked cell-to-cell (isometric; `moveDir` is a grid step) and free world-space (first-person; `moveDir` is a normalized camera-relative XZ heading, with per-axis wall sliding). Free mode keeps `Cell` synced to the nearest walkable tile so the grid-based critter AI/catch still work, and reverts to a cell center when toggled back to grid mode.
- `Game1` draws the procedural meshes with a `BasicEffect` and the cat with the custom `CatNormalMap.fx` effect (normal mapping + its own key/fill/back lighting), using `DrawUserPrimitives`/`DrawUserIndexedPrimitives`. Every `CatNormalMap.fx` uniform (textures, transforms, light directions/colors) is pushed from C# in `LoadCatModel`/`DrawCritter` — the effect framework does not reliably apply HLSL uniform initializers, and zeroed light colors make the shader return black (the cat renders as an invisible/black silhouette). Camera logic lives in `Camera.cs` (constructed in `Game1.SetupCamera()`, driven by `Camera.Update(gameTime, input, playerWorldPos, playerFacing)`): isometric is the default — orthographic top-down at tilt 0, perspective FOV once tilted (U/O, up to 80°). The camera always uses world `Up` and nudges its position slightly along +Z at tilt 0 so the look direction is never exactly vertical (no up-vector snap at the boundary). `CameraViewMode` also offers a `FirstPerson` mode (V / gamepad Y toggles): a 60° perspective camera at the player's head (`FirstPersonEyeHeight = 2.2`, PersonMesh's head center) with mouse-look yaw/pitch (`LookSensitivity` rad/unit, pitch clamped to ±85°). `Mode == FirstPerson` seeds yaw from `playerFacing` and pitch 0; iso pan/zoom/tilt are left intact so toggling back restores the exact view. `Game1` skips `DrawPlayer` in first-person and hides the cursor. `Camera.MapMoveToWorld` turns WASD into the camera-relative heading used by free movement (W heads where you look, S backs up, A/D strafe).
- `EventSystem.cs` is a pub/sub singleton (`EventSystem.Instance`, namespace `StoreChase`). Game1 publishes `GameStartedEvent` (init + restart) and `GameWonEvent`; `RunStats.cs` subscribes and logs a win summary to the console. Add new events as `sealed` subclasses of `BaseEvent`. `RunStats` isn't stored on `Game1` — the singleton's handler list keeps it rooted.
- `RainbowTitle.cs` animates the centered "Store Chase" title. The hue phase is advanced on a **background thread**; `Game1.DrawRainbowTitle` only reads `Phase` and draws. Keep all graphics work on the game thread (SpriteBatch/GraphicsDevice aren't thread-safe) — `UnloadContent()` disposes the title's thread.

## Input

- All device input goes through `IInputSource` (`InputState.cs` for the per-frame snapshot). `KeyboardMouseInput` and `GamePadInput` are the only files allowed to call `Keyboard`/`Mouse`/`GamePad`; `InputSystem` polls the registered sources and exposes the active device's state as `InputSystem.State` once per `Game1.Update()`.
- `InputSystem` auto-detects the active device: it keeps the current source while it has input (`InputState.HasActivity`), otherwise hands off to the first other source with input. `ActiveDevice`/`ActiveSource` expose which is current; every source is still polled each frame so internal state (mouse wheel delta) stays current.
- First-person mouse capture is a source capability, not a gameplay concern: `IMouseLookSource` (`MouseLookEnabled`/`MouseLookCenter`) is implemented by `KeyboardMouseInput`, and `InputSystem.MouseLookEnabled`/`MouseLookCenter` are set from the camera mode in `Game1.Update` and forwarded to any source that supports it. While captured, the source reports a centered, relative look delta and re-centers the pointer (skipping the capture-start frame so entering FPS doesn't snap the view).
- `Camera.Update` takes `in InputState` (and `Player.Update` takes a resolved `moveDir` + `freeMove`, not the raw state) — do not read device APIs from gameplay code; add a source instead. `InputState` axes: `Move.Y` = +down, `Tilt` = +up, `Zoom` = wheel notches/frame, `Look` = first-person look delta (right/up positive; mouse pixels or a gamepad stick scaled to the same unit), `ToggleCamera` = rising edge that flips the view mode; add `GamePadInput` mappings there.

## Critter AI

- `ICritterAI.cs` defines `CritterContext` (a `record struct`) and one method `DecideNextMove`. `SmartAnimalAI.cs` implements alert(≤8)/flee(≤4)/wander tiers and tunables `ReplanInterval`, `AlertRadius`, `FleeRadius`, `QuirkChance`.
- `ChooseQuirkMove` implements the `QuirkChance` roll: it side-steps perpendicular to the player bearing, or doubles back to the cell it just left (tracked in `_lastCell`). `ReplanInterval` gates flee replans via `_replanTimer`, but the timer only ticks while the path is empty and alert/wander tiers ignore it. Movement/catch logic lives in `Critter.cs`.
- `CatWalkAnimator.cs` procedurally swings the cat's four legs: it finds leg vertices from the mesh geometry (low-Z band split by X/Y) and rotates them around hip pivots with a trot gait in `Update`, rewriting `StaticMesh.Vertices` positions plus their normals and tangents in place (driven by `Critter.IsMoving` from `Game1.DrawCritter`). No skeleton/assets.

## Repo gotchas

- `.gitignore` ignores `bin/`, `obj/`, `Content/bin`, `Content/obj`, `.vs/`, `.vscode/`. Previously-committed build artifacts were untracked via `git rm --cached` (124 files, staged deletions) — don't re-add them. `artifacts/` holds the original cat model upload (`.zip` + extracted files) and is intentional to keep.
- No test project. Verification = a clean `dotnet build`.
- README.md is the detailed design/decision log (camera math, OBJ orientation analysis, AI rationale). Trust it for intent, but verify code-level claims against the source — some have gone stale.