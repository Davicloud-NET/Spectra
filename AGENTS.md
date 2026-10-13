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
| `SpectraEngine.Scripting.Tests` | the Luau shim and its binding, the crash cases of a published console, the files a project gets for luau-lsp, and the sample scripts luau-lsp checks |
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
| `--export-script-types=<file>`, `--export-script-api=<file>` | write the script API as `spectra.d.luau` for luau-lsp, or as JSON for the docs site, and exit with no window |
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
- A script makes, clones, destroys and reparents nodes only through `TreeWrites`. `PlayJournal.Tree` keeps each authored parent's first list of children and Stop puts the lists back. A node the level made records nothing anywhere.
- A clone keeps its source's names. The nodes of one clone share a number (`SceneNode.CopyGroup`), and `TargetNameIndex.Resolve` sends what an entity of a clone names to the entities of that clone when one has the name. Whoever resolves a name for a view does the same, or it draws a clone's wire to every door of the name.
- A script changes the player only through `PlayerWrites`. `EntityWorld.Player` is set after the script runtime is made, so it is asked on every use and never kept.
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
- Everything a script can reach is declared by attributes in `SpectraEngine.Scripting.Luau/Runtime`. `ScriptApiGenerator` (in `SpectraEngine.Entities.Generator/ScriptApi`) writes the binding and `LuauScriptApi.Description` from them, and the description writes the definitions file and the reference. `docs/script-api.md` says how a member is added.
- A node is asked through the class of what it carries. `NodeClass.KindOf` in Core picks it and `NodeMembers` calls that class's switches.
- `Players.LocalPlayer.Character` is no node. One `ScriptPlayer` and one `ScriptCharacter` live for a play session, so two reads of either are equal in Luau.
- A module runs inside the `require` that first asks for it, on the asker's native stack (`ModuleRunner`). It cannot wait: one that yields before it returns is ended there and fails for everyone.
- An entity class reaches a script through its schema: an input is a method and an output is a signal. `EntityClassApi` in Core reads a schema that way, and the binding answers from it (`EntityMembers`). A member of the node's own class wins, then an input, then an output, then a child.
- `door:Open()` and a listener's call are events in the scripts' share of the queue (`EntityWorld.QueueScriptInput`, `EntityWorld.Listeners`). Neither runs inside the call that made it. A listener is numbered on from the output's own wires.
- `Touched` and `TouchEnded` of a part are found by the pass that tells trigger entities (`TouchTracker`, with `PartTouches`). A script's listener sits in `EntityWorld.Listeners` under the node and a name of `TouchSignal`, and only a node with one is measured.
- A builder's tags and attributes are on the node (`SceneNode.Tags`, `SceneNode.Attributes`), two sets that never change once made, saved in the map and the baked map. `LevelSeed` copies them into the runtime's `TagTable` and `AttributeTable` before the first script starts. Scripts change that copy and never the node, so nothing of it is journalled, and an entity never sees it.
- A script replaces how the player moves with `character:SetMover(fn)`. `LuauCharacterMover` calls the function on one thread kept for it (`MoverCall`) with objects that live for the play session, and the function works on a copy of the state. `docs/physics.md` has the seam. The demo's level installs `Scripts/player_mover.luau`.
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
- Focus begins an edit and losing it commits, so a click into a cell and out runs `PropertyFieldModel.Commit` with nothing typed. It leaves an empty mixed cell alone and takes the box as it was shown for no change. A new kind of cell relies on both.
- `TagPanelInfo` and `AttributePanelInfo` come back as the same instance while the selected nodes hold the same sets, and the panel's models skip an instance they have seen. After a cancelled colour drag the view calls `AttributesModel.ShowAgain` for that reason.
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
- The window sets `EditorDocument.Project` again after every save, with a new `ProjectLayout` for the same folder. What should happen once for a project compares `Root`, as `ScriptCommands` does, or it happens on every Ctrl+S.
- `LogicPlayInfo.Recent` is two rings merged by number, wires and scripts. Add a wire's event with `LogicRecentEvents.Add` and a script's with `AddOfScript`, or script traffic pushes the wires out.
- A card in the Logic view that is no stub is not always an entity: a script's card is neither, and a node can have one of each. Ask `LogicCard.IsEntity`. A script's edge has no `WireIndices`, and code that reads `WireIndices[0]` of any edge throws on one.
- A Logic layout that only scripts caused carries `LogicArrangementChange.ByScripts` and must not ask for a fit, or the view jumps each time a script connects.
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
- A write to a node that records nothing stays in the level after Stop, and nothing says so. A new member a script can set needs a `JournalKind`, a row in `ScriptWrites`, a case in its `Read` and `TryWrite`, and a `[ScriptProperty(Sets = ...)]` declared partial with no body. `StopPromiseTests` and `SettableListTests` catch one left out.
- The journal puts a transform back bit for bit (`SceneNode.RestoreLocalTransform`). The ordinary setter takes -0 for 0 and keeps the one it has, and the level would save other bytes.
- `NodeName` is the rule for a name a running level gives a node. The editor's rename is looser, so a map can hold a name a script cannot give.
- A script is a `.luau` file under `Assets/`, named on its node by content path (`SceneNode.Script`). A `Script` never changes once made: an edit sets a new one.
- A baked map names its scripts' files and holds none of their text. The text is a pack entry behind a header (`PackedScript`). `ContentScriptSource` reads it for a level, and whatever else opens a `.luau` path gets the header when it comes from a pack.
- A script node's file must end in `.luau`, or the bake stops with `SC7013`. The script rule goes by the extension, and any other file would be packed unchecked.
- A cook asks `CookSettings.ScriptCheck` whether a script compiles. Left unset, a project with scripts fails with `SC8002`. scook and the editor install `LuauSyntaxCheck`, and a new host of the cook has to as well. Its `CompilerVersion` is in a script's cache key: whatever can change a verdict goes into that string.
- Under a baked map a node that was a world brush has no brush. `SceneNode.BakedKind` says what it was. Ask `HoldsWorldBrush` before moving a node while a level plays, and `NodeClassName` for its class.
- `SceneNode.DeclaredRealm` and `DeclaredState` are saved, also in a baked map, and nothing reads them. A dormant node still carves, draws and collides.
- A default `SceneQueryFilter` wants `CanQuery`, which a trigger has off.
- A wire's input name is case sensitive, and a wrong one is only a Debug log line. `ent_watch` shows it as `deny`.
- A tick sends at most `MaxDispatchesPerTick` of the events that were waiting, and as many again of those it makes. Waiting ones past that go out on later ticks, in order, and are never dropped. What a tick makes past it is a loop with no delay, and is dropped. A loop that waits and multiplies is stopped by `MaxPendingEvents`: an event made while the queue is full is dropped. Entities and scripts each have both limits to themselves (`EntityEventLane`).
- An input that reads `context.Parameter` says its type in `[EntityInput(Parameter = ...)]`, and an output that sends a value says it in `[EntityOutput(Parameter = ...)]`. Left out, the schema says it carries nothing, and the editor warns under every wire that gives it a parameter. `BuiltinParameterTypeTests` holds the list.
- A typed output's `Fire` method handed a value no wire can carry, like a float that is not finite, fires nothing and warns once. It does not throw.
- A `.sentdef` from before the types reads every input and output as `ParameterType.NotSaid`, which is not `None`. Judge nothing against it.
- A part a script hides keeps its GPU mesh until Stop: `PartMembers` calls `Scene.KeepMeshWhileHidden` before it writes `IsRendered`. Anything else that writes `IsRendered` while a level plays builds the mesh again on every show, and only `PartVisibleMeshTests` sees that.
- `AuthoredAttributes` reads a declared keyvalue as `EntityWorld.ParseKeyvalues` leaves the entity: the last pair its type can read, else the class's default. The editor's Properties shows the first pair, so a name written twice shows one value and runs with another.
- Scripts start on the first tick, so none hears what fired while the level spawned, as `logic_auto` fires `OnMapSpawn`. A wire carries it. The first script that listens for such an output is warned once.
- A listener is kept by node and name, not by entity. It outlives a despawn and a node that leaves the level. So do a node's tags and the attributes scripts set. Whatever destroys a node for good has to end its connections in `NodeSignals` and take it out of `TagTable` and `AttributeTable`.
- A solid part is touched within two of the skins the player says (`IPlayerPresence.SkinWidth`). What the mover leaves in the state is touched whatever the gap: `GroundNodeId` while grounded, and `SlideContacts`, the parts its slide stopped at. A mover that leaves both empty misses a corner the player glances off at speed.
- `CharacterSimulation.Tick` clears `SlideContacts` before the mover runs, so a mover writes them on every tick.
- A world brush fires no touch, loose or baked, and listening to one is an error (`TouchSignal.WhyNone`). A subtractive part fires none either. A mesh is touched by the box around it, and under a baked map it is not loaded and nothing touches it: the log warns once.
- A touch tells a trace nothing, so `ent_watch` and the Logic view do not show it.
- The mover builds the planes of every solid part near the player on each tick, which allocates. An allocation test of touch uses parts with `CanCollide` off, or a fake player.
- An entity is not restarted when a script gives its node another parent in the level. A class that keeps what `CollectOwnedBrushes` gave it overrides `OnOwnedBrushesChanged`, and one that keeps a local transform has to see the new parent, as `LinearMover` does.
- On a node with an entity a keyvalue wins over an attribute of its name: `LevelSeed` leaves the attribute out and warns, and the editor refuses to make one. `AttributePanelInfo.RoomFor` is the one answer to whether a node can get an attribute it lacks. A new reason goes into `AttributeRoom` and `PropertyLimits.LeftOut`, or the editor skips a node while the row says the value goes on all.
- The level's root is not saved, so it takes no tag and no attribute, and the panel says why.
- `NTAG` and `NATR` are optional sections of a baked map, so `CompiledMapFormatVersion` did not move. `MapRule.Version` still has to go up with such a section, or a cached bake from before it is served.
- Nothing holds a mover in Luau to being a function of what it is handed. What has to come back with a restored state goes into `CharacterState.Memory`. The bit-for-bit tests hold for `walk.luau` and `double_jump.luau` alone.
- A mover in Luau that fails, or returns a value, is out for the play session and the built-in one moves the player from that tick. One that fails after its own call put another in its place takes nothing out (`MoverSlot.Failed`). A level closed under the mover's call says nothing.
- `PlayerWrites.TrySetMover` records the mover the level started with. A host that sets `CharacterSimulation.Mover` itself while a level plays has it replaced at Stop when a script set one first.
- The character and its `CharacterTuning` outlive a play session. What a level changes on the tuning records in the play journal with a `TuningJournalKind`, or it is still there on the next Play. `PlayerSettingTests` catches a settable member of the character that records nothing.
- The player's frame looks along `LookVector` (`CharacterFacing`). A player start and a teleport destination face along their node's +Z, so `character:Teleport(start.CFrame)` turns the player round.
- The facing is `CharacterState.Yaw`, written by `CharacterSimulation` and not by the mover. A facing a teleport named holds until `TryTakeTeleport` is called. A host with no view calls it itself, or the facing never follows its commands again.
- A velocity that rises off the ground has to leave it as a jump does (`CharacterSimulation.SetVelocity`), or the ground snap takes the rise back. It asks the ground normal, not Y alone: a walk up a ramp has a Y above zero.
- A teleport into solid is taken, from a script and from `trigger_teleport`. The mover pushes the player out through the nearest face, which under a floor can be down and out of the level.
- A speed past about 1.8e19 squares to infinity and the capsule maths then throws inside the tick. `PlayerWrites` caps what a script sets, and `MoverLimits` what a mover in Luau hands over: four times that speed, and a reach of 1000 units from where the player began the tick. Nothing caps a mover replaced in C#.
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
- A level with scripts and no `RuntimeFactory` plays without them and warns once.
- A member a metatable answers by hand (`ObjectMembers`, `CFrameMembers`, `Vector3Members`, `Color3Members`) with no attribute works, and is missing from the definitions file and the reference.
- Luau keeps a string's atom once it has asked. `LuauHost.SetAtoms` runs once, before anything reads an atom. A name that is not in the list reads as -1.
- A class with `[ScriptLibrary]` or `[ScriptGlobals]` does nothing until `LuauScriptRuntime` calls its generated `ScriptInstall`. The description has it either way.
- A script API summary is read from the `///` lines. A `/** */` comment is not read, and the member fails with `SPS002`.
- The definitions file marks a property with no `Sets` as `read`. The generator refuses a name Luau keeps for itself (`SPS004`), and a description built from data is not checked.
- A property of an object that is no node is set through `SetBy` on `[ScriptProperty]`. The description then has `CanSet` true and `Sets` null, so ask `CanSet` for read only, never `Sets is null`.
- A new class of nodes needs a `NodeClassKind`, its name and base in `NodeClass`, a `[ScriptClass]` with `Extends`, a line in each switch of `NodeMembers`, and a node in `ClassHolders`. Left out of `NodeMembers`, its members are described and no script finds them.
- A child answers to its name as a member does, and a member wins. A member added to a class hides every child of that name from `node.Name` in every level. `FindFirstChild` still finds it.
- `ChildWaits` answers `Wake` and `Drop` before `TaskLibrary`. A new kind of wait with a table of its own does the same, or `TaskLibrary.Drop` cancels its thread.
- A module node and its file asked for by path are two modules. The one asked for by path has no `script`: the global is nil there, and the definitions file still types it `Instance`.
- `require` is described and left out of `spectra.d.luau` (`LuauDefinitions.IsTheCheckersOwn`). The type checker's own `require` gives a module its type, and a declared one would return `any`.
- `@assets` is the content root. The editor writes a project's `.luaurc` with `"assets": "./Assets"` (`ScriptProjectFiles`), and a test holds the alias to `ModulePath`. A `.luaurc` of the person's own that lacks it has `luau-lsp` and the engine read one path two ways.
- The editor writes `spectra.d.luau`, `.luaurc` and two files under `.vscode` into a project when a script of it opens (`ScriptProjectSetup`, called by `ScriptLauncher`). Only the first is ever written again. A `ScriptLauncher` made with no `ScriptEditorSetup` writes nothing, which is right for a test and wrong for a new host.
- `luau-lsp` does not read a definitions file that starts with a byte order mark, and then knows no global. `ScriptProjectSetup` writes such a file again.
- `LuauDefinitions.Write(LuauScriptApi.Description)` writes a file with no entity class. A host hands it `Description.WithEntityClasses(schemas)`. The editor's `ScriptDefinitions` makes the text by the calls `--export-script-types` makes, and a test compares the two as text. Change both.
- An input or output named as a member of any class of nodes cannot be reached on such a node: the member wins. `EntityClassGapsTests` catches it for the built-in classes, and for any other class the level's scripts get one warning.
- An output fired with text its type cannot read calls a listener with nothing, and warns once for that output.
- The sender of a script's input is looked up only while the world has a trace. One queued before a watch was turned on has no sender.
- `WireActivityTrace` leaves out what a script sent, so the wires' recent events stay the wires'. `ScriptWireActivity` keeps what scripts send and hear for the Logic view, only while a view asks.
- A signal and a connection have userdata tags of their own, so `typeof` names them as Roblox does. What reads an object off the stack by `ScriptTags.Object` alone finds neither: use `ScriptTags.TryGet` or `ScriptContext.TryGetObject`.
- A listener carries who it is: `ScriptListeners.Add(node, name, listener, by)`. Added without `by`, it is called as before and the Logic view draws no wire for it.
- A script's node is `LuauScript.Owner`. `ScriptStarter` sets it, and it is what tells the level a script stopped. A script started any other way, such as one under a clone, sets it and is added there too, or it has no card and its sent wires stay after it is switched off. A module's `LuauScript` has the owner of whoever asked first.
- What scripts hear is read from the listeners at each capture. What they sent is counted from when a view asks.
- While a level is watched, `LuauScriptRuntime.AfterSimulation` hides every listener nothing can call (`ScriptListeners.Hide`). It removes none: a watched level must queue what an unwatched one does.
- A signal a script sets off (`AttributeChanged`, a tag's two) is fired through `DeferredCalls`. Each listener's call is queued with `EntityWorld.QueueScriptCall` and comes back through `IScriptRuntime.Wake` and `Drop`, in the order they were made.
- Tags and the attributes scripts set are outside `MemoryBudget`. `AttributeLimits` and `TagLimits` bound them, and `AttributeMethods` and `TagMethods` do the refusing. What is put into the tables directly goes past the checks.
- A tag's list keeps a node that lost the tag until `TagTable.InOrder` is asked, and is put in order only after a node joined or `NodeReparented` fired. Read a tag's nodes through `InOrder`.
- A property or signal of a static script class may take the `ScriptContext` in front of the object. One with `Sets` may not.
- `NodeLifetime.Forget` is what ends everything kept of a destroyed node: its connections, waits, tags, attributes, modules and its script's owner. A new table keyed by node is emptied there, and `SpawnKeepsNothingTests` holds a clone to keeping nothing.
- A destroyed node a script made counts against `TreeLimits.MaxMadeNodes` while a script holds it (`HeldNodes`), so `TreeWrites.MadeCount` is above what is in the level until Luau has collected.
- `LuauObjects.Reached` runs inside the push of an object no script held. It may count and list. It pushes nothing and runs no script.
- `LuauFunction.Start` makes a Luau thread per call. What the host calls every tick keeps one thread, as `MoverCall` does.
- What a mover is handed reads between ticks and writes only inside the call. A mover's `Sweep` or `Gather` that leaves the tick's volume builds the part lane again (`MoverCollision.Cover`).
- A listener whose script was switched off or whose thread was cancelled stays in `EntityWorld.Listeners` until its output fires next or `NodeSignals` looks them over. `ent_watch` counts it until then.
- The door gives a member to the class `NodeClass.KindOf` names and to no other. A part that carries a light is a Part, and no script reaches its light.
- A light's colour or brightness that is a number is still refused when the linear colour times the brightness is none.
- `Color3.ToHsv` gives a hue of 1 for a red with as much green as blue, as Roblox prints. The editor's `ColorMath.SrgbToHsv` gives 0 for the same colour.
- A class or global added to the script API needs a line in a clean sample under `Test/SpectraEngine.Scripting.Tests/Samples`, outside a comment and a string, or `ScriptSampleTests` fails. A sample with no `--!strict` is checked as a new project is, and then almost nothing is reported.
- `require` finds `./`, `../` and `@self` from the file of the script function that calls it. A thread that begins with `require`, as in `task.spawn(require, path)`, has none and takes only `@assets` and a node.
- The path rules are stricter than Luau's, since a script source has no folders: no `..` after a name, no name that ends in a dot, `@self` only from an `init.luau` in small letters.
- While a module's file runs, `NodeNamingSink` holds the error that came last, and `ModuleRunner` takes it as the module's own failure. A module that fails for one it asked for is known by the error's text (`Refusal`) and not reported again.
- A failed module stays failed until the next Play. One the host stopped runs again on the next ask, unless half the call limit was its own time or half the memory budget was taken while it ran (`ScriptModule.IsKept`).
- `luau-lsp` 1.70.1 takes the definitions file as it is. Only its new solver (`--flag:LuauSolverV2=true`) refuses a write to a `read` property.
- The tests that run luau-lsp (`-- -trait "Suite=LuauLsp"`) skip unless `SPECTRA_LUAU_LSP` holds the program's path. A green scripting suite without it says nothing about the definitions file. `luau-lsp analyze` exits 0 when it printed only lints, and 1 with no line named when it could not read a script.
- Every class of nodes has `[string]: Instance` in the definitions file, so `workspace.Door` checks. The price: a name the checker does not know is a child, and `door:Open()` reads as `Cannot call a value of type Instance`.

Tests and tools

- Some tests read the source tree as text (COM ownership, tool content, brush opacity, the ribbon roster). They should not depend on line endings.
- The cook's determinism tests run `scook` in separate processes on purpose: the string hash seed is per process.
- The task in `Icons.targets` is a code fragment, and one of those succeeds whatever it logged. It sets `Success` itself. Without that a refused icon file is an error line in a build that goes on. `IconBuildTests` runs the task through `dotnet msbuild`, and skips where there is a runtime and no SDK.
- The tests that render through the real OpenAL (`-- -trait "Suite=AudioLoopback"`) skip unless `SPECTRA_AUDIO_LOOPBACK=1` is set. A green scene suite says nothing about `OpenAlBackend` or the low-pass filter.
- An agent cannot drive the running editor. `SpectraEngine.Editor.Render.Tests` renders controls and writes PNGs under `artifacts/`. Colour, feel and gestures still need a person, and say so when you hand over.
- After any agent run with write access, check `git status` before staging. Probes get left behind.
- A Spectra.Kitchen test that needs the Luau library calls `RealScriptCheck.Require()` and carries `Suite=RealLuau`. It skips without the library and fails without the trait. A test file outside the binding with `spectra_luau` in a string fails `BindingSourceTests`.
- `Data/cframe-studio.json` in the Bsp tests holds what Roblox Studio answered to `cframe-probe.luau`. A new case in `CFrameProbeCases` fails both comparisons until Studio has been asked again. An answer the engine keeps against Studio's goes in `KeptOurs` with its reason.
- Studio's log file cuts a printed message at about 1000 characters. What a probe prints is copied from the Output window, or printed one case a line.
- The scripts and quoted lines of the site's `write-your-first-script.mdx` are held by the two `FirstScriptGuideTests`. A code block added there has to be a sample file.
- A mover test that shortens `CallLimit` calls `MoverLevel.StopTheNextHangSoon` right before the call that hangs. The watchdog goes by the wall clock, and a short limit left on stops a call that only stalled.
- `TagsAndAttributesKeysTests` drives the real boxes. `KeyTextInput` sends text and no key press, so a test of what a key does after Enter presses a key first.
- A render test that edits and then saves a sheet runs the jobs and a render tick several times before it captures, or the PNG is the frame before the last change.
- `CsgBench openworld` times edits of a tenth of a millisecond, and its MOVE verdict moves with the machine: read it over several runs on a quiet one.
- `ScriptCostTests` fails on an allocation. Its table of times is explicit and runs only with `-- -explicit only`, so a normal run reports one test as not run.
- Everything that opens a script goes through `ScriptLauncher`, and it starts a program only for a `.luau` file (`ScriptOpenPlan.IsScriptFile`), since a level can name any file as its script. No test starts a program, except `LuauLspTests`, which starts luau-lsp where the variable names it: `FakeProgramStarter` keeps what was asked.
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
| `docs/script-api.md` | how a member is added to the script API: declare it, what is generated, what the tests demand |
| `docs/console.md`, `docs/networking.md`, `docs/realms.md` | designs that are not built yet |
| `docs/positioning.md`, `docs/roblox-*.md` | who the engine is for |
| `docs/archive/architecture-notes-2026-10.md` | the old long guide: past decisions with their reasoning. Search it when you touch a subsystem. |
| `docs/archive/roadmap-2026-10.md` | the old long roadmap: the reasoning behind each milestone, and the sections and rulings other docs cite |
