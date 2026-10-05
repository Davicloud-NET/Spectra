# AGENTS.md

The guide for coding agents in this repo. `CLAUDE.md` points here.

Keep it short. A line belongs here when breaking it fails without an error, or when you could not work it out from the code.

## What this is

Spectra Engine is a C#/.NET 10 game engine with an editor in the spirit of Hammer and Roblox Studio. One scene graph, CSG brushes for world geometry, renderers for OpenGL, D3D11 and D3D12 through Silk.NET, and its own shader language, SpectraShade, which compiles to GLSL and HLSL.

Read `docs/code-style.md` before writing anything. The plan is in `ROADMAP.md`.

## Commands

```bash
dotnet build                                                  # the solution, Spectra.slnx
dotnet run --project SpectraEngine.Executable -- d3d11        # demo: opengl | d3d11 | d3d12
dotnet run --project SpectraEngine.Editor -- d3d11            # editor: d3d11 | d3d12, Windows only
dotnet run --project Test/SpectraEngine.Bsp.Tests             # one test suite
dotnet run --project Spectra.Kitchen.CLI -- cook <projectDir> # scook: cook | verify <pack> | inspect <pack> | sounds -o <dir> <contentDir>
dotnet run --project SpectraShade.Compiler.CLI -- <file>      # ssc, the shader compiler
dotnet run -c Release --project Benchmarks/CsgBench           # CSG benchmarks
dotnet publish SpectraEngine.Executable -c Release -r win-x64 # NativeAOT build
git submodule update --init --recursive                       # Box3D source
native/build-box3d.ps1                                        # builds box3d.dll
npm --prefix site run api && npm --prefix site run dev         # the docs site, on localhost:4321
```

Tests are xUnit v3 on Microsoft.Testing.Platform. Run a suite with `dotnet run`, never `dotnet test`. Filter with `-- -class "<full type name>"` or `-- -trait "Suite=Determinism"`.

| Suite | Covers |
|---|---|
| `SpectraShade.Compiler.Tests` | the shader compiler |
| `SpectraEngine.Bsp.Tests` | CSG, scene, maps, assets, entity runtime, play session, console |
| `SpectraEngine.Editing.Tests` | commands, undo, gizmos, cameras |
| `SpectraEngine.Editor.Tests` | the shell's models, plus tests that read the shell's sources |
| `SpectraEngine.Editor.Render.Tests` | shell controls rendered headless with real Skia |
| `SpectraEngine.Entities.Tests` | the entity generator, `.sentdef`, the built-in classes, a no-code level played end to end |
| `SpectraEngine.Physics.Tests` | Box3D binding and the character mover |
| `Spectra.Kitchen.Tests` | the cook, pack formats, determinism |
| `SpectraEngine.Graphics.Tests` | pixel tests on a real GL driver |

Smoke test: run the demo with `--selftest` for about 15 seconds. It must log `Editing self-test: PASS` every five seconds or so and nothing at `ERR`.

Demo switches worth knowing (`docs/performance.md` has the profiling ones):

| Switch | Does |
|---|---|
| `--selftest` | synthetic edit and undo run, for the smoke test |
| `--play` | start in first-person play mode (F8 toggles it) |
| `--profile` | frame phase timings in the stats line |
| `--pipeline=<name>` | pick the render pipeline, e.g. `deferred` |
| `--map=<bundle>`, `--save-map=<bundle>` | run or write a `.smap` |
| `--project=<dir>` with `--pack`, `--dev` | run a project, from cooked packs, with loose files on top |
| `--save-project=<dir>` with `--exit-after-save` | export the scene as a project |
| `--offscreen-probe`, `--viewport-compare`, `--pacing-probe`, `--pipeline-compare` | render checks that need no person |
| `--command="<line>"`, repeatable | run a console line once the scene is loaded; replies are logged with `[console]` |
| `--console` | read console lines from the terminal while the demo runs |

The console command `fake_device_loss` makes the next present fail as a lost graphics device. The demo then ends with exit code 1, and the editor restarts its viewport. It is the only way to test either without pulling a laptop's plug.

A `--command` line runs before `--play` starts the level. Put `wait` in front of anything that needs it running: `--play --command="ent_watch on; wait; ent_list"`. This is how an agent checks entity wiring with no person present.

A run nobody should hear takes `--command="sound_volume 0"` as its first command: `--play` starts the level's sounds.

Publishing:

- Don't pass `-p:PublishAot=true`. The projects that want it already set it. On the command line it reaches the netstandard2.0 generator and the publish fails with NETSDK1207.
- The AOT link step needs `vswhere.exe`. Publish from a Developer prompt or put `C:\Program Files (x86)\Microsoft Visual Studio\Installer` on PATH. Otherwise: MSB3073, exit code 123.
- `native/build-box3d.ps1` needs neither. Its output is not committed.

## Layout

- `Assets/`: the content root. Textures, `.spectramat` materials, models. Copied next to the executable.
- `SpectraEngine.Core/`: the engine. `Graphics/` (renderers, pipelines, built-in shaders), `Scene/`, `Bsp/` (brushes, CSG, BSP), `Assets/` (content sources, pack readers, caches), `Maps/`, `Projects/`, `Entities/` (the runtime and the `ent_` commands), `Play/` (`PlaySession`), `ConsoleSystem/`, `Physics/`, `Audio/`, `Input/`, `Hosting/` (`EngineHost`), `Inspection/`.
- `SpectraEngine.Editing/`: editor logic. Commands, undo, gizmos, selection, cameras.
- `SpectraEngine.Editor/`: the Avalonia shell. `Shell/` (the Logic view is `Shell/Logic/`), `Viewport/`, `Theme/`, `Sounds/`.
- `SpectraEngine.Executable/`: the demo. Also hosts the editing layer.
- `SpectraEngine.Entities/` and `.Generator/`: built-in entities and the Roslyn generator.
- `SpectraEngine.Physics.Box3D/`: the Box3D binding. Source is the `external/box3d` submodule.
- `Spectra.Kitchen/` and `.CLI/`: the cook (`scook`).
- `SpectraShade.Compiler/`, `.CLI/` (`ssc`), `.LSP/`, `.VSIX/`.
- `site/`: the user docs, built with Starlight. `npm run api` generates the C# reference from the `///` comments.
- `Test/`, `Benchmarks/CsgBench/`, `docs/` (design notes), `native/`.

## Rules

- AOT-safe code only: no reflection, no `dynamic`, no runtime codegen.
- Nullable is on.
- Package versions live in `Directory.Packages.props`.
- The render thread owns the graphics context, every scene change and all GPU resource creation. The main thread pumps OS events.
- A UI thread talks to the engine only through `EngineHost`: `EnqueueCommand`, the request latches, and `FrameSnapshot`s. Only ids and values cross that line, never a `SceneNode`.
- `SpectraEngine.Editing` references Core and nothing else. No Silk.NET, no window types. Only the demo and the editor reference it, so tool code never ships in a game.
- Pack writers stay in `Spectra.Kitchen`. A shipped game only reads packs.
- Content is files under `Assets/`. Shaders are `.spectrashade` files. Nothing lives in a string literal.
- A content error never reaches the draw loop. A missing material becomes the default one, a missing texture the magenta placeholder, each with a warning.
- Open world: no sealed-world requirement, no PVS, no map extents.
- Measure before optimizing. `--profile` and `docs/performance.md`.
- Keep the oracles green: chunked against monolithic CSG, the bit-identical determinism tests, and the `CsgBench openworld` verdict "world-size independent". Changing a baseline is a decision, not a fix.

## How it fits together

World geometry

- The scene graph is the spine. A brush is a payload on a node (`SceneNode.Brush`) and its placement is the node's world transform.
- That transform must stay rigid: no scale on a brush node or above one. Resizing edits plane extents (`Brush.WithScaledExtents`).
- `SceneNode.BrushKind` (World or Part) says whether a brush is fused into the static world. `Brush.Operation` (Additive or Subtractive) says whether it adds or removes solid. Neither is inherited. A subtractive Part does nothing.
- `Brush` is immutable. The `With...` methods return a new one, and that new reference is what invalidates caches.
- `Scene.StaticWorld` and the chunk meshes are derived. Edits mark the world dirty, a background task compiles, the render thread swaps in the chunks that changed. `Scene.RebuildStaticWorld` is the synchronous path for loading and tests.
- BSP answers queries. It is never the render path.
- A face is named by its plane index. `Brush.FaceSurfaces` has one entry per plane, `Brush.LocalFaces` skips planes that clip away.
- A baked map (`.scmap`) is adopted as is. Never carve it again: ask `ScmapBrushSource.IsReCarvable`.
- Under a baked map `Scene.StaticWorld` is null and no node carries a world brush. Collision reads `CompiledStaticWorld.CollisionPlacements` and world rays read its trees. Loading one does not move `StaticWorldCompileCount`, so a cache of world data must also compare `Scene.CompiledStaticWorld`.

Editing

- Every scene change is an `IEditorCommand`. It names nodes by `SceneNode.Id` and stores absolute before and after values.
- One gesture is one undo entry.
- Tools read an `EditorInputFrame`.
- A UI sends set verbs ("snap on"), not toggles. A toggle against a stale snapshot flips the wrong way.
- Undoing a structural edit puts the node back at its old sibling index. Child order is placement order for the carve.

Rendering

- Shading is in linear light. sRGB is decoded and encoded by texture and target formats, never by `pow` in a shader.
- Everything draws inside `BeginPass` and `EndPass`. Size things from `Renderer.PassSize`, not from the window.
- Deferred is the default pipeline. Forward stays for MSAA and blended transparency.
- Forward and deferred draw the same picture. SpectraShade has no include, so `Lit.spectrashade` and `DeferredLight.spectrashade` each hold a copy of one lighting block, and `LightingSourceTests` fails when the copies differ. Change both, then run `PipelineParityGlTests` and the demo with `--pipeline-compare` on d3d11 and d3d12.
- Light data goes to shaders as parallel `vec4` arrays. A struct array compiles and then does nothing on OpenGL. Light type numbers are append-only.

Content

- Everything is read through `IContentSource` and `ContentSourceStack`. An existence check and the open that follows must use the same stack.
- An asset's identity is its normalized content-relative path, from `ContentRoot.NormalizeRelativePath`.
- `MaterialRef.Id` is per process. Write the path to disk, never the id.
- Maps (`.smap`) and projects are folders of canonical JSON. Use `Serialization/CanonicalJson`: it keeps the bytes identical across a round trip and across operating systems.
- A cook must produce the same bytes in a second process, from the cache, and at any `-j`. Never write a file in dictionary order.

Entities

- An entity is strings on the node (`SceneNode.Entity`). The runtime copies them and never writes back.
- The editor learns schemas only from `.sentdef` bytes, even in process.
- A brush entity is a part that carries an entity. A trigger is such a part with collide, query and render off.
- While a level plays, only `EntityWorld.SetLocalTransform` moves a node. It keeps the authored pose and Stop puts it back.
- The player is not an entity. A trigger or a button the player sets off is its own activator.
- Entity motion counts ticks. World time is a float sum and drifts.

Sound

- What is playing lives in the simulation (`EntityWorld.Sounds`) and is counted in ticks. A sound's end and its markers never come from the audio device.
- `SoundPresenter` gives the loudest sounds a voice each frame, on the render thread, once the camera is final.
- Loudness over distance is the engine's (`SoundFalloff`). OpenAL's distance model is off and it only places the sound.
- `WallPropagation` muffles a sound by the solids between it and the listener. It keeps an answer for each sound and traces 60 lines a frame, so an answer can be a quarter of a second old.
- The engine reads cooked `.saudio` only. The editor cooks a wav the first time a level uses it, into a cache under `%LOCALAPPDATA%\Spectra\SoundCache`. The demo's build cooks its sounds with `scook sounds`.

Play and console

- `PlaySession` runs the fixed tick: entities, physics, then the character. It needs no camera, input or renderer.
- There is one `SpectraConsole` per engine, never a static. A line goes in as a string through `EngineHost.SubmitConsoleLine`, and what it prints comes back in `FrameSnapshot.ConsoleLines`.
- A command reads `Scene.EntitySchemas`, never `EntityCatalog.Shared`, which freezes on its first read.

## Things that fail quietly

Graphics

- `new ComPtr<T>(p)` adds a reference. Wrap a fresh COM pointer with `ComOwnership.Own` and let go with `ComOwnership.Release(ref field)`. A test fails on `new ComPtr<` anywhere under `Graphics/`.
- OpenGL wants `Use()` before uniforms are set. Both D3D backends want it after.
- `AcquireSync` on a keyed mutex times out with `WAIT_TIMEOUT` (0x102), which is a positive HRESULT. `hr < 0` reads it as success.
- A shared render target is rebuilt on resize under a new generation. Compare generations: Windows reuses handle values.
- The D3D debug layer is on in Debug and off in Release. A check that counts debug-layer errors proves nothing while it is off.
- Call `SilkPlatform.EnsureRegistered()` before creating a window, or an AOT build finds no window platform. On Linux, `SilkPlatform.UsePortableRuntimeId()` must run before any Silk.NET `GetApi()`.

Avalonia

- Never write `private void InitializeComponent()` by hand. It shadows the generated one and every `x:Name` field stays null.
- A local value beats any style. A selector with a class beats a bare type selector. Fluent paints many states on template parts, so set those through its resource keys.
- A failed binding raises nothing. The property keeps its default.
- Put a fill's alpha in the colour, not in the brush's `Opacity`, when the fill is transitioned.
- Dock tool content goes through `SetToolContent`, which also sets the `DataContext`. All dock controls share one `Factory`.
- A native viewport is a child window. Nothing drawn in the same window can cross it, only popups can, and re-parenting it destroys the session. A composited viewport has neither limit.
- An `InputGesture` string is parsed. Use real `Key` names (`OemOpenBrackets`, `Delete`), or the window throws at startup.
- `ApplyViewArrangement` sizes and shows the view panes and never moves one. The 3D view's track never goes to zero: a swap chain with no width may not come back.
- The compositor waits on the shared target's key with no deadline. An engine that dies must keep answering (`Renderer.OfferSharedTurn`) until the shell has cleared `viewport.Host` and `IsAwaitingEngine` is false, or the whole window freezes.
- When the engine's render thread dies the editor restarts the viewport and keeps the level (`MainWindow.Recovery.cs`, `SessionRecovery`). Anything a session owns must be rebuilt by that path too, or it works until the first restart.

Host

- Apply every published `FrameSnapshot`. The scene changes and console lines in one are sent once, so sampling the newest loses the rest.
- A scene event handler must not change the graph. Neither must an `IEntityTrace`: it runs inside the dispatch.
- The entity world has one trace slot. `ent_watch` and the Logic view share it through `EntityTracePair.Join`. Set `SceneManager.EntityTrace` to one of them and the other goes quiet.
- The engine publishes a level's wiring (`FrameSnapshot.LogicGraph`) only while a view asks for it through `EngineHost.RequestLogicView`. A new session has been asked for nothing.
- A member or namespace named `Console` hides `System.Console` for the whole file.

Span query

- `Scene.TraceSolidSpans` from a point inside a part starts in that part. A sound under a door reads as muffled by its own door unless the door is in `filter.Ignore`.
- Its default filter leaves out a part with `CanQuery` off, also one that collides and stops the player. `IgnoreQueryFlags` counts it.
- A map cooked before the `COLM` section still traces, and every world span names the default material. `CompiledMapLoadReport.CollisionFaceMaterialsMissing` is the only sign.

Sound through walls

- A `SoundQuery` without its `Body` is muffled by the part its sound sits in. `SoundPresenter` sets it to the emitter's node.
- A sound under a part is not behind a solid it stands in, so a door's sound stays clear inside the wall the door slid into. It is then heard clear from both sides of that wall.
- A part that moves raises no signal. `WallPropagation` hears it once the answer is `RefreshSeconds` old, by its own clock: a test passes a `ManualClock` and steps it, or the door never moves.
- With no audio device the engine traces nothing (`Engine.HeardScene`), and captions then go by distance alone.
- A voice that starts allocates. An allocation test with walls has to keep the same sounds loudest, or sources change hands and it fails for that.

Entities and play

- A new built-in class goes into `BuiltinEntities.Schemas` and `ClassCount`. Left out of both, it works until a trimmed build drops it, and then every map naming it loads a placeholder.
- The entity generator reads only the members a class declares itself. A keyvalue on a base class binds nothing.
- `MapSceneBinder.FromScene` builds fresh nodes. A node member it does not copy is deleted by the next save.
- A default `SceneQueryFilter` wants `CanQuery`, which a trigger has off.
- A wire's input name is case sensitive, and a wrong one is only a Debug log line. `ent_watch` shows it as `deny`.
- Whatever puts the play camera somewhere else in one frame calls `SoundPresenter.ListenerJumped`. A hop under 8 units is otherwise heard as motion, and every sound chirps.

Tests and tools

- Some tests read the source tree as text (COM ownership, tool content, brush opacity, the ribbon roster). They should not depend on line endings.
- The cook's determinism tests run `scook` in separate processes on purpose: the string hash seed is per process.
- The tests that render through the real OpenAL (`-- -trait "Suite=AudioLoopback"`) skip unless `SPECTRA_AUDIO_LOOPBACK=1` is set. A green scene suite says nothing about `OpenAlBackend` or the low-pass filter.
- An agent cannot drive the running editor. `SpectraEngine.Editor.Render.Tests` renders controls and writes PNGs under `artifacts/`. Colour, feel and gestures still need a person, and say so when you hand over.
- After any agent run with write access, check `git status` before staging. Probes get left behind.
- CI is `.github/workflows/ci.yml`, on Windows and Linux. The VSIX builds only on Windows. The graphics suite is skipped on hosted runners, which have no GPU.

## Where things are written down

| File | Covers |
|---|---|
| `docs/code-style.md` | how code, comments and commits are written |
| `ROADMAP.md` | milestones and their order |
| `site/src/content/docs/contributing/writing-docs.mdx` | how to write user docs: pages, images, video |
| `docs/data-model.md` | `SceneNode`, `Scene` and the payloads |
| `docs/formats-and-pipeline.md` | every file format and the cook |
| `docs/performance.md` | how to measure, and current numbers |
| `docs/physics.md`, `docs/negative-brushes.md` | physics, brush kinds, subtractive brushes |
| `docs/console.md`, `docs/networking.md`, `docs/realms.md` | designs that are not built yet |
| `docs/positioning.md`, `docs/roblox-*.md` | who the engine is for |
| `docs/archive/architecture-notes-2026-10.md` | the old long guide: past decisions with their reasoning. Search it when you touch a subsystem. |
| `docs/archive/roadmap-2026-10.md` | the old long roadmap: the reasoning behind each milestone, and the sections and rulings other docs cite |
