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
git submodule update --init --recursive                       # Box3D and Luau source
native/build-box3d.ps1                                        # builds box3d.dll
native/build-luau.ps1                                         # builds Luau and its shim (build-luau.sh on Linux)
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
| `SpectraEngine.Scripting.Tests` | the Luau shim and its binding, and the crash cases of a published console |
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
| `--script=<file>` | run one `.luau` file with no level and no window; what it prints is logged with `[script]`, and the exit code is 1 when it failed |

The console command `fake_device_loss` makes the next present fail as a lost graphics device. The demo then ends with exit code 1, and the editor restarts its viewport. It is the only way to test either without pulling a laptop's plug.

A `--command` line runs before `--play` starts the level. Put `wait` in front of anything that needs it running: `--play --command="ent_watch on; wait; ent_list"`. This is how an agent checks entity wiring with no person present.

A run nobody should hear takes `--command="sound_volume 0"` as its first command: `--play` starts the level's sounds.

Publishing:

- Don't pass `-p:PublishAot=true`. The projects that want it already set it. On the command line it reaches the netstandard2.0 generator and the publish fails with NETSDK1207.
- The AOT link step needs `vswhere.exe`. Publish from a Developer prompt or put `C:\Program Files (x86)\Microsoft Visual Studio\Installer` on PATH. Otherwise: MSB3073, exit code 123.
- `native/build-box3d.ps1` needs neither. Its output is not committed.

## Layout

- `Assets/`: the content root. Textures, `.spectramat` materials, models. Copied next to the executable.
- `SpectraEngine.Core/`: the engine. `Graphics/` (renderers, pipelines, built-in shaders), `Scene/`, `Bsp/` (brushes, CSG, BSP), `Assets/` (content sources, pack readers, caches), `Maps/`, `Projects/`, `Entities/` (the runtime and the `ent_` commands), `Play/` (`PlaySession`), `ConsoleSystem/`, `Physics/`, `Audio/`, `Input/`, `Hosting/` (`EngineHost`), `Inspection/`, `Scripting/` (what a script reports, and the sink for it).
- `SpectraEngine.Editing/`: editor logic. Commands, undo, gizmos, selection, cameras. `Assets/Icons/` holds the icons of entities and lights.
- `SpectraEngine.Editor/`: the Avalonia shell. `Shell/` (the Logic view is `Shell/Logic/`), `Viewport/`, `Theme/`, `Sounds/`.
- `SpectraEngine.Executable/`: the demo. Also hosts the editing layer.
- `SpectraEngine.Entities/` and `.Generator/`: built-in entities and the Roslyn generator.
- `SpectraEngine.Physics.Box3D/`: the Box3D binding. Source is the `external/box3d` submodule.
- `SpectraEngine.Scripting.Luau/`: the Luau binding. Source is the `external/luau` submodule, and the shim between them is `native/luau`.
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
- A script with no children has no place (`SceneNodeClassifier.HasPlace`): no Transform rows, no handles, and `PropertyEditor` and `GizmoTool` skip it. A new tool that moves the selection asks the same question.
- Insert Script makes its file in the command that adds the node (`ScriptFileMaker`), on the render thread, because the name comes from the live selection. Undo leaves the file.
- A drop in the viewport lands on what a click there selects (`ViewportInteractionController.NodeAt`). `Scene.Raycast` alone never finds a light or a point entity.
- A tap puts a press and its release in one frame. A gesture ends when its button is up, or it waits for a release that never comes.
- The icon of a light or a point entity is drawn and picked where `SceneIcons` puts it, which is beside the node when icons pile up. Code that draws or picks one at the node's position misses.

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
- While a level plays, a node changes only through the play journal (`PlayJournal`), which keeps the first value and puts it back at Stop. An entity moves a node with `EntityWorld.SetLocalTransform`. A script sets a member with `ScriptWrites.TryWrite`, and the binding has no other way to change a node.
- A wire carries one string. `ParameterText` is the one place that turns a typed value into that string and back, and a generated `Fire` method goes through it too.
- The player is not an entity. A trigger or a button the player sets off is its own activator.
- Everything an entity waits for counts ticks: a wire's delay, a think, a mover's trip. Seconds become whole ticks once, when the wait starts, by `WholeTicks`. World time is the tick number times the step and is only for a person to read.

Icons

- An entity class names its icon in `[SpectraEntity(Icon = "door")]`. The name travels in the `.sentdef` and becomes the file `IconEntity` plus the name in Pascal case. `WorldIcons` in `SpectraEngine.Editing` is the one place that rule is written.
- The 3D view and the shell draw the same file from `SpectraEngine.Editing/Assets/Icons`. Never copy one into the editor's folder and never draw an entity or a light in code.
- `SpectraEngine.Editor/Assets/Icons/tabler.py` is the only way a Tabler glyph gets into either folder. Its tables say which Tabler icon each file is. It fetches from the network when it runs.
- In the shell only `Shell/EditorIcons.cs` names the icon of an entity, a light or a file, and every surface asks it. A large ribbon button asks it for the same file scaled up, so there is no `IconLg` copy of one.
- `EditorIcons` names their tints too. The `kind-entity` and `kind-light` style classes in `Theme/Controls.axaml` repeat two of them, so a glyph set in markup can still grey out when its button is disabled. A test holds those two to the registry.

Sound

- What is playing lives in the simulation (`EntityWorld.Sounds`) and is counted in ticks. A sound's end and its markers never come from the audio device.
- `SoundPresenter` gives the loudest sounds a voice each frame, on the render thread, once the camera is final.
- Loudness over distance is the engine's (`SoundFalloff`). OpenAL's distance model is off and it only places the sound.
- `WallPropagation` muffles a sound by the solids between it and the listener. It keeps an answer for each sound and traces 60 lines a frame, so an answer can be a quarter of a second old.
- The engine reads cooked `.saudio` only. The editor cooks a wav the first time a level uses it, into a cache under `%LOCALAPPDATA%\Spectra\SoundCache`. The demo's build cooks its sounds with `scook sounds`.

Play and console

- `PlaySession` runs the fixed tick: entities, physics, then the character. It needs no camera, input or renderer. It calls the level's scripts before the physics step and at the end of the tick, also when nothing walks.
- A console line carries where it came from as data: `ConsoleLine.Place`, the calls under an error in `Stack`, and `ProblemNode` for a script that did not start. `Text` has no file and line in it. What writes a line where nobody can click writes `PlainText`.
- There is one `SpectraConsole` per engine, never a static. A line goes in as a string through `EngineHost.SubmitConsoleLine`, and what it prints comes back in `FrameSnapshot.ConsoleLines`.
- A command reads `Scene.EntitySchemas`, never `EntityCatalog.Shared`, which freezes on its first read.

Scripting

- One `LuauHost` owns one Luau state and knows nothing of a scene. A script is a thread in it with globals of its own, and a function the host keeps (`LuauFunction`) runs on a new thread that shares them.
- A script that runs past `LuauHost.CallLimit` or holds more than `MemoryBudget` is switched off and never runs again.
- A level's scripts run in a `LuauScriptRuntime`, one for each play session. A host sets `SceneManager.Scripts.RuntimeFactory`, `StartEntityWorld` makes the runtime and `StopEntityWorld` closes it. A thread that waits is an event in the entity queue (`EntityWorld.QueueScriptWake`).

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
- A `ListBox` marks Enter handled. The Content panel hears it with a tunnel handler.
- An `InputGesture` string is parsed. Use real `Key` names (`OemOpenBrackets`, `Delete`), or the window throws at startup.
- `ApplyViewArrangement` sizes and shows the view panes and never moves one. The 3D view's track never goes to zero: a swap chain with no width may not come back.
- The compositor waits on the shared target's key with no deadline. An engine that dies must keep answering (`Renderer.OfferSharedTurn`) until the shell has cleared `viewport.Host` and `IsAwaitingEngine` is false, or the whole window freezes.
- When the engine's render thread dies the editor restarts the viewport and keeps the level (`MainWindow.Recovery.cs`, `SessionRecovery`). Anything a session owns must be rebuilt by that path too, or it works until the first restart.
- A text or number a property refuses goes in `PropertyLimits`. The panel reads it to say why and `PropertyEditor` reads it to refuse. A rule in only the editor is a value dropped on the render thread with no message.

Host

- `ConsoleOutput` drops what is written past 1024 lines between two drains and keeps up to `ErrorRoom` errors past that. Whoever builds a snapshot with console lines sets `ConsoleLinesWritten` too, or lines dropped at the end of a burst are never reported.
- The Output and the Console panel show one log through one control, `OutputRows`. A row drawn in one panel's own markup is missing in the other.
- `ScriptStartProblems` makes the Problems rows of scripts from console lines with `ProblemNode`, and has to see every snapshot in order. A snapshot the window has no room for still sends those lines through `ScriptStartProblems.KeptOf`.
- Apply every published `FrameSnapshot`. The scene changes and console lines in one are sent once, so sampling the newest loses the rest.
- A scene event handler must not change the graph. Neither must an `IEntityTrace`: it runs inside the dispatch.
- The entity world has one trace slot. `ent_watch` and the Logic view share it through `EntityTracePair.Join`. Set `SceneManager.EntityTrace` to one of them and the other goes quiet.
- The scene tree learns what a node is, its entity class, its kind of light and whether it carries a script from a `SceneChange` and nowhere else. A part that carries a script is still a part in its kind, and only `SceneChange.HasScript` tells a UI. The node's setters raise `Scene.NodeKindChanged`. A light edited in place raises nothing, so whoever changes its kind calls `Scene.NotifyNodeKindChanged`, or the row keeps its old icon.
- The engine publishes a level's wiring (`FrameSnapshot.LogicGraph`) only while a view asks for it through `EngineHost.RequestLogicView`. A new session has been asked for nothing.
- A member or namespace named `Console` hides `System.Console` for the whole file.

Span query

- `Scene.TraceSolidSpans` from a point inside a part starts in that part. A sound under a door reads as muffled by its own door unless the door is in `filter.Ignore`.
- Its default filter leaves out a part with `CanQuery` off, also one that collides and stops the player. `IgnoreQueryFlags` counts it.
- A map cooked before the `COLM` section still traces, and every world span names the default material. `CompiledMapLoadReport.CollisionFaceMaterialsMissing` is the only sign.

Sound through walls

- A `SoundQuery` without its `Body` is muffled by the part its sound sits in. `SoundPresenter` sets it to the emitter's node.
- A sound on a part is heard from that part: its lines start where they leave the part's box grown by `SceneSoundObstacles.BodyReach`. So a door's sound stays clear inside the wall the door slid into, and solid that close to any part does not muffle that part's sounds.
- A `SoundQuery` names its node, and a node holds its whole level. Nothing keeps one past the frame: the presenter clears its own after `Resolve`.
- A wall answer is kept by the node's id, which the next play session has too. `SoundPresenter` calls `ISoundPropagation.Forget` when the level ends, and a propagation that keeps anything between calls drops it there.
- A part that moves raises no signal. `WallPropagation` hears it once the answer is `RefreshSeconds` old, by its own clock: a test passes a `ManualClock` and steps it, or the door never moves.
- A compile makes an answer due only when it touched cells near the answer's lines. `Scene.RebuildStaticWorld` reports the whole world as changed, so a test of a compile somewhere else needs `SpanLevel.CompileChanges`.
- With no audio device the engine traces nothing (`Engine.HeardScene`), and captions then go by distance alone.
- A voice that starts allocates. An allocation test with walls has to keep the same sounds loudest, or sources change hands and it fails for that.

Entities and play

- A new built-in class goes into `BuiltinEntities.Schemas` and `ClassCount`. Left out of both, it works until a trimmed build drops it, and then every map naming it loads a placeholder.
- The entity generator reads only the members a class declares itself. A keyvalue on a base class binds nothing.
- A class a builder turns to aim, like a player start, sets `HasFacing` on `[SpectraEntity]`. Left out, its marker in the 3D view has no arrow.
- An `Icon` name with no file draws the plain entity icon and warns nobody. `IconDriftConventionTests` catches it for the built-in classes only.
- `MapSceneBinder.FromScene` builds fresh nodes. A node member it does not copy is deleted by the next save.
- A write to a node that records nothing stays in the level after Stop, and nothing says so. A new member a script can set needs a `JournalKind`, a row in `ScriptWrites`, and a case in its `Read` and `TryWrite`. `StopPromiseTests` and `SettableListTests` catch one left out.
- The journal puts a transform back bit for bit (`SceneNode.RestoreLocalTransform`). The ordinary setter takes -0 for 0 and keeps the one it has, and the level would save other bytes.
- `NodeName` is the rule for a name a running level gives a node. The editor's rename is looser, so a map can hold a name a script cannot give.
- A script is a `.luau` file under `Assets/`, named on its node by content path (`SceneNode.Script`). A `Script` never changes once made: an edit sets a new one.
- A baked map carries no scripts yet. The cook warns with `SC7011`, the loaded node is a plain node, and a map that warns is baked again on every cook.
- `SceneNode.DeclaredRealm` and `DeclaredState` are saved, also in a baked map, and nothing reads them. A dormant node still carves, draws and collides.
- A default `SceneQueryFilter` wants `CanQuery`, which a trigger has off.
- A wire's input name is case sensitive, and a wrong one is only a Debug log line. `ent_watch` shows it as `deny`.
- A tick sends at most `MaxDispatchesPerTick` of the events that were waiting, and as many again of those it makes. Waiting ones past that go out on later ticks, in order, and are never dropped. What a tick makes past it is a loop with no delay, and is dropped. A loop that waits and multiplies is stopped by `MaxPendingEvents`: an event made while the queue is full is dropped. Entities and scripts each have both limits to themselves (`EntityEventLane`).
- An input that reads `context.Parameter` says its type in `[EntityInput(Parameter = ...)]`, and an output that sends a value says it in `[EntityOutput(Parameter = ...)]`. Left out, the schema says it carries nothing, and the editor warns under every wire that gives it a parameter. `BuiltinParameterTypeTests` holds the list.
- A typed output's `Fire` method handed a value no wire can carry, like a float that is not finite, fires nothing and warns once. It does not throw.
- A `.sentdef` from before the types reads every input and output as `ParameterType.NotSaid`, which is not `None`. Judge nothing against it.
- Whatever puts the play camera somewhere else in one frame calls `SoundPresenter.ListenerJumped`. A hop under 8 units is otherwise heard as motion, and every sound chirps.

Luau

- A Luau call that can raise, made from managed code, works under the JIT and kills a NativeAOT build at a later garbage collection. Managed code reaches Luau through `Shim`, whose calls return a status, and a host function fails by returning `stack.Fail(...)`. `BindingSourceTests` holds the short list of direct calls.
- So the crash cases only count from a published console: `dotnet publish Test/SpectraEngine.Scripting.CrashCases -c Release -r win-x64`, then `SpectraEngine.Scripting.Tests`. Until then they skip, and a stale publish is run as it is.
- A change to the shim raises `SL_SHIM_REVISION` in `spectra_luau.h` and `Shim.Revision`. Left alone, a library built before the change passes the startup check and runs the old code.
- A status form that runs protected answers with an error on a thread that has yielded or failed, and does nothing. So a string or a table for a waiting thread is pushed on `LuauHost.Stack`, and `LuauThread.Resume` moves it.
- `LuauHost.CallLimit` counts the host's own time too. A host function that takes three seconds gets the script that called it switched off, and the message blames the script.
- A value a script no longer names can still sit in a stack slot of a waiting thread, and Luau keeps it alive until the slot is written again. A test that waits for Luau to let an object go ends the thread first.
- Nothing the tick calls on `IScriptRuntime` may throw: `EntityWorld.Tick` has no catch. `LuauScriptRuntime` catches at each entry point, and a new one needs the same. `IScriptSink.Report` runs in the middle of a tick, and what a sink throws is dropped with its message.
- `LuauScriptRuntime.Close` may run on another thread while a call into a script has not returned. It may not dispose a kept function or touch a stack. When a call into a script returns, the state may be gone: ask `LuauHost.IsClosed` before the host's stack is touched again.
- A thread waits for one thing at a time and `WaitingThreads` holds it, for a tick or for a signal. A host function that yields asks `stack.CanYield` before it keeps anything for the resume, and `ThreadRunner.Resume` asks whether the thread has ended.
- A userdata gets its tag's metatable when it is made. One pushed before the tag had a metatable has none for good, and a script gets `attempt to index userdata`.
- `LuauHost.PushFunction` keeps its delegate until the host closes. Make a method once and hand it out from a `MethodTable`, never on a read.
- Scripts print at most `PrintCap.MaxPrintsPerTick` lines a tick, all scripts of a level together. Warnings and errors always pass. A test that prints more in one tick gets the rest as one warning.
- A level with scripts and no `RuntimeFactory` plays without them and warns once. `--pack` plays the demo without its spinner, since a baked map has no scripts.

Tests and tools

- Some tests read the source tree as text (COM ownership, tool content, brush opacity, the ribbon roster). They should not depend on line endings.
- The cook's determinism tests run `scook` in separate processes on purpose: the string hash seed is per process.
- The task in `Icons.targets` is a code fragment, and one of those succeeds whatever it logged. It sets `Success` itself. Without that a refused icon file is an error line in a build that goes on. `IconBuildTests` runs the task through `dotnet msbuild`, and skips where there is a runtime and no SDK.
- The tests that render through the real OpenAL (`-- -trait "Suite=AudioLoopback"`) skip unless `SPECTRA_AUDIO_LOOPBACK=1` is set. A green scene suite says nothing about `OpenAlBackend` or the low-pass filter.
- An agent cannot drive the running editor. `SpectraEngine.Editor.Render.Tests` renders controls and writes PNGs under `artifacts/`. Colour, feel and gestures still need a person, and say so when you hand over.
- After any agent run with write access, check `git status` before staging. Probes get left behind.
- Everything that opens a script goes through `ScriptLauncher`, and it starts a program only for a `.luau` file (`ScriptOpenPlan.IsScriptFile`), since a level can name any file as its script. No test starts a program: `FakeProgramStarter` keeps what was asked.
- An agent's worktree keeps about 4.6 GB of build output. Delete `bin` and `obj` in a worktree once its branch is merged, or the drive fills.
- A build that runs out of disk leaves truncated files in `obj` that the next build takes for up to date. Delete `bin` and `obj`.
- A save stamps `spectramap` with `EngineInfo.MapFormatVersion`. Raise it and every fixture that compares saved bytes has to name the new number.
- CI is `.github/workflows/ci.yml`, on Windows and Linux. The VSIX builds only on Windows. The graphics suite is skipped on hosted runners, which have no GPU.

## Where things are written down

| File | Covers |
|---|---|
| `docs/code-style.md` | how code, comments and commits are written |
| `README.md` | what works today and what does not |
| `ROADMAP.md` | what is being worked on, what comes next and in what order |
| `docs/milestones.md` | every milestone id and its status |
| `site/src/content/docs/contributing/writing-docs.mdx` | how to write user docs: pages, images, video |
| `docs/data-model.md` | `SceneNode`, `Scene` and the payloads |
| `docs/formats-and-pipeline.md` | every file format and the cook |
| `docs/performance.md` | how to measure, and current numbers |
| `docs/physics.md`, `docs/negative-brushes.md` | physics, brush kinds, subtractive brushes |
| `docs/audio.md` | how sound is built, what in it is tuning data, and the design for sound round corners |
| `docs/console.md`, `docs/networking.md`, `docs/realms.md` | designs that are not built yet |
| `docs/positioning.md`, `docs/roblox-*.md` | who the engine is for |
| `docs/archive/architecture-notes-2026-10.md` | the old long guide: past decisions with their reasoning. Search it when you touch a subsystem. |
| `docs/archive/roadmap-2026-10.md` | the old long roadmap: the reasoning behind each milestone, and the sections and rulings other docs cite |
