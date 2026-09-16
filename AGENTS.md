# AGENTS.md

Single-project C# **MonoGame DesktopGL** game targeting `net10.0` (see `StoreChase.csproj`). No tests, no CI, no solution file. Layout: namespace `StoreChase`, entrypoint `Program.cs` → `Game1.cs`. All gameplay files are top-level `.cs` files in the repo root.

## Build / run

- Build: `dotnet build`. Run: `dotnet run`.
- To edit content via the MGCB CLI: `dotnet tool restore` (pins `dotnet-mgcb` 3.8.5.1 in `dotnet-tools.json`), then use `mgcb`. Not needed for plain builds — the `MonoGame.Content.Builder.Task` package runs the content build automatically.

## Known warnings

`dotnet build` is green but emits a warning for the unused `_lastCell` field in `SmartAnimalAI`.

## Content & assets

- The MGCB content pipeline (`Content/Content.mgcb`) builds only `Content/DefaultFont.spritefont` (UI text). Everything visual is either procedural geometry (`CubeMesh.cs`, `VertexPositionNormalColor`) or a runtime-loaded OBJ.
- `DefaultFont.spritefont` names the system font `DejaVu Sans`; if the content build fails with "font not found" on another machine/OS, change `<FontName>` to an installed font and rebuild (`dotnet tool restore` then `dotnet build`).
- The cat model — `Content/Models/Cat.obj`, `Cat.mtl`, `Cat_diffuse.jpg` — is loaded **at runtime** by `ObjLoader.cs` from the relative path `Content/Models/Cat.obj`, NOT through MGCB. `StoreChase.csproj` already copies `Content\Models\**` to the output dir (`CopyToOutputDirectory`), so new model files only work if they land under `Content/Models` — do not register them in `Content.mgcb`.
- `ObjLoader` is deliberately narrow: single material, quad faces, `vt`/`vn` assumed present; uses `short` indices (35K verts fits, cap is 65,535). If a new OBJ fails, start there.

## Architecture

- `GridMap.cs` is pure grid logic (walkable/BFS: `NextStepToward`, `DistanceMapFrom`); `GridPos` is defined in that same file (`GridMap.cs:134`). Map layout is a hardcoded string array, 18×12.
- World space is X/Z with Y = up. `Game1.CellSize = 2f` world units per grid cell. `Player`/`Critter` expose `WorldPos` (X/Z, Y=0) and have **no `Draw` method** — `Game1` owns all rendering and the camera.
- `Game1` builds a `BasicEffect` (default lighting, no textures except the cat) and draws meshes with `DrawUserPrimitives`. Camera logic lives in `Camera.cs` (constructed in `Game1.SetupCamera()`, driven by `Camera.Update()`): orthographic top-down at tilt 0, perspective FOV once tilted (U/O, up to 80°); the view "up" vector snaps at the 0/1° boundary — known, intentional.
- `EventSystem.cs` is a pub/sub singleton (`EventSystem.Instance`, namespace `StoreChase`). Game1 publishes `GameStartedEvent` (init + restart) and `GameWonEvent`; `RunStats.cs` subscribes and logs a win summary to the console. Add new events as `sealed` subclasses of `BaseEvent`. `RunStats` isn't stored on `Game1` — the singleton's handler list keeps it rooted.
- `RainbowTitle.cs` animates the centered "Store Chase" title. The hue phase is advanced on a **background thread**; `Game1.DrawRainbowTitle` only reads `Phase` and draws. Keep all graphics work on the game thread (SpriteBatch/GraphicsDevice aren't thread-safe) — `UnloadContent()` disposes the title's thread.

## Input

- All device input goes through `IInputSource` (`InputState.cs` for the per-frame snapshot). `KeyboardMouseInput` and `GamePadInput` are the only files allowed to call `Keyboard`/`Mouse`/`GamePad`; `InputSystem` polls the registered sources and exposes the active device's state as `InputSystem.State` once per `Game1.Update()`.
- `InputSystem` auto-detects the active device: it keeps the current source while it has input (`InputState.HasActivity`), otherwise hands off to the first other source with input. `ActiveDevice`/`ActiveSource` expose which is current; every source is still polled each frame so internal state (mouse wheel delta) stays current.
- `Player.Update` and `Camera.Update` take `in InputState` — do not read device APIs from gameplay code; add a source instead. `InputState` axes: `Move.Y` = +down, `Tilt` = +up, `Zoom` = wheel notches/frame; add `GamePadInput` mappings there.

## Critter AI

- `ICritterAI.cs` defines `CritterContext` (a `record struct`) and one method `DecideNextMove`. `SmartAnimalAI.cs` implements alert(≤8)/flee(≤4)/wander tiers and tunables `ReplanInterval`, `AlertRadius`, `FleeRadius`, `QuirkChance`.
- `ChooseQuirkMove` is a stub returning null, so `QuirkChance` never actually fires; `ReplanInterval` isn't enforced (path is followed to completion, then replanned). Movement/catch logic lives in `Critter.cs`.

## Repo gotchas

- `.gitignore` ignores `bin/`, `obj/`, `Content/bin`, `Content/obj`, `.vs/`, `.vscode/`. Previously-committed build artifacts were untracked via `git rm --cached` (124 files, staged deletions) — don't re-add them. `artifacts/` holds the original cat model upload (`.zip` + extracted files) and is intentional to keep.
- No test project. Verification = a clean `dotnet build`.
- README.md is the detailed design/decision log (camera math, OBJ orientation analysis, AI rationale). Trust it for intent, but verify code-level claims against the source — some have gone stale.