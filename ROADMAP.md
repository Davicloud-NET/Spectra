# Roadmap

Spectra is a C#/.NET 10 game engine with a level editor in the spirit of Hammer and Roblox Studio. It is for people who build worlds: Roblox developers who want to own and ship their game, and Hammer mappers who want a current engine they can ship on. You block out a world with brushes, dress it with models and materials, wire behaviour from entities, and press play in the level you are editing. Luau scripting comes on top of that.

The first target is a small first-person game made in the editor and in Luau, with no engine code. `docs/positioning.md` has the audience and the steps after that.

This file says where the engine is and what comes next. Status was checked against the code on 2026-10-05. The roadmap it replaced is `docs/archive/roadmap-2026-10.md`. That one keeps the reasoning behind each milestone, and the section numbers and rulings (R-1 to R-10) that other docs cite.

Milestone ids are listed with their status at the end. Sizes are S, M and L, and only compare milestones with each other.

## Where it stands

### World building

- Brushes are convex solids on scene nodes. World brushes that overlap fuse into one static world.
- The world compiles in 32-unit chunks on a background thread, and only the chunks an edit touched are rebuilt. The cost of an edit does not grow with the world or with distance from the origin. `CsgBench openworld` checks that.
- A brush is a world brush or a part. A part keeps its own mesh and moves without a recompile.
- A brush adds solid or removes it. Subtractive brushes cut doorways and windows.
- Each face has its own material and texture axes.
- There is no terrain.

### Editor

- An Avalonia shell with the engine running inside it, on D3D11 or D3D12. Windows only.
- It opens on a start page and works on a project folder. A ribbon with Build and View tabs, a command palette, and docked panels: Levels, Scene, Properties, Content, Output, Problems, Console.
- Move, rotate and resize gizmos in two styles, with grid and angle snapping. Box select, duplicate, delete, group, rename, reparent. One gesture is one undo entry.
- Camera: free look, orbit, pan, frame selection, and top, front and side views.
- Insert blocks, parts, cuts, lights, entities and models.
- A material dropped on a face paints it. Scale, offset, rotation and alignment are set in the Properties panel.
- Entity keyvalues and output wiring are edited in panels built from the `.sentdef` schema.
- Lights have icons and handles in the viewport. Selection shows as an outline.
- The Console panel is a command line over the editor's own verbs. It has no variables or binds.
- F8 plays the level in first person, and F8 again stops. Stop does not yet put back what the run moved.
- Levels save and load as `.smap` folders of JSON.
- The viewport is a native child window by default. The composited viewport can dock and takes dropped assets. It is asked for with `--viewport=composition`, and becomes the default on a machine after five clean sessions there.

### Entities

- An entity is a class name, keyvalues and output connections, stored as strings on a node and saved in the map. A class the build does not know keeps its data through a save.
- Entity classes are C# with attributes. A source generator writes the parsing, the input dispatch and the registration, so nothing uses reflection.
- The build exports the schemas as a `.sentdef` file. The editor reads only that.
- The runtime ticks on the fixed step while playing and never writes to the authored data.
- Built in, all logic: `logic_auto`, `logic_relay`, `logic_timer`, `math_counter`, `logic_branch`, `logic_case`, `logic_compare`. Nothing yet touches the world.

### Rendering

- Three backends: OpenGL, D3D11 and D3D12.
- Deferred is the default pipeline. Forward and wireframe sit beside it. Forward draws the same picture as deferred, and tests and the `--pipeline-compare` switch check that.
- Shading is physically based, in linear light, into an HDR target that is tone mapped on the way out.
- Lights: directional, point, spot, rect and disc, plus a sky and ground ambient. Eight lights at most.
- One directional light casts shadows: four cascades in a 4096 atlas, out to 200 units with a fade.
- Repeated parts draw instanced.
- The selection outline is drawn in the tone-map resolve. The grid and other world lines are alpha blended.
- Missing: anti-aliasing, normal maps, transparent materials, bloom, fog, image-based lighting, particles, decals.

### Content and cook

- All content is read through one stack of sources: loose files, `.spack` packs, or packs with loose files on top.
- `scook` cooks a project into a pack: compressed textures, models from glTF, audio, compiled shaders and baked maps.
- A cook gives the same bytes in a second process, from the cache and at any worker count. CI checks that on Windows and Linux.
- A baked map loads without running CSG.
- A project is a folder with a `.spectraproj` file. The demo boots one from its pack.
- Missing: cooked materials, skinned models, collision hulls on models, patch and mod packs.

### Physics

- Box3D is vendored, bound through P/Invoke and published under NativeAOT.
- The static world collides: one convex hull per brush, kept in step with each compile.
- A first-person character walks, climbs steps and jumps on a fixed tick.
- Ray and overlap queries on the scene, collide and query flags per node, collision groups.
- Missing: dynamic bodies, moving parts that push, touch events.

### Shader language

- SpectraShade compiles `.spectrashade` files to GLSL and HLSL: vertex, fragment, geometry and compute stages.
- Loose shader files reload on save. A cooked pack carries them compiled.
- `ssc` is the command line compiler. There is a language server and a Visual Studio extension.
- Missing: `import` parses and does nothing, there is no type checker, and a shader cannot describe its material parameters.

### Audio and animation

- Audio plays through OpenAL, with a source pool and streaming voices. Nothing in a level can play a sound yet.
- Skeletons, clips and pose blending exist on the CPU. Nothing imports or draws them.

### Docs and CI

- The user docs are a Starlight site in `site/`: first pages on the concepts, level and material files, logic entities, the cook and the keyboard. The C# reference is generated from `///` comments.
- CI runs on Windows and Linux: the test suites, a NativeAOT publish on each, the cook determinism tests and a boot from a cooked pack.

## In progress

- Brush entities (P7). A brush owned by an entity leaves the static world and moves with the entity.
- Trigger volumes (P8).
- The first world entities: door, button, platform, teleport, player start.
- Console commands to inspect entities and fire their inputs (C9).
- A spike on embedding Luau under NativeAOT. Its result decides how O4 starts.

## Next

In order. The order follows `docs/positioning.md`: finish build, wire and play, then add what a small first-person game needs.

1. Stop puts the level back (P11a, M). Play mode restores every transform a run changed. Needed as soon as a door moves. Depends on brush entities. Still to decide: restore in place, or play a copy of the scene and throw it away.
2. Sound in a level (S). An entity that plays a cooked sound. Audio playback and the entity runtime are both there.
3. Luau scripting (O1 to O5, O7 to O9; L). Scripts on nodes, generated bindings to the scene, attributes, tags and signals. Depends on the spike. The character mover should end up replaceable from Luau.
4. Prefabs (P10, L). A subtree saved once and placed many times. Depends on entities in maps, which are done. Risky because the rule for names inside a prefab is saved into every map, and it is not decided.
5. Bodies that move (Y6 to Y8, L). Dynamic bodies, parts that push and carry the player, touch events. Depends on brush entities, and on scripting for the events. Risky because a brush that leaves the static world also leaves its collision, so both have to change together.
6. The console (C0 to C6, L together). Typed variables, commands, key binds and config files, usable from a terminal before any overlay exists.
7. The look (R9, R14, R19a, R15, R16, R10). Normal maps, bloom, height fog, image-based ambient, temporal anti-aliasing and transparent materials, in that order. The bar is the look of a good late-UE4 game. Normal maps are the risky one: they change the vertex layout that every mesh and every CSG test baseline uses.
8. Material parameters (F4, S2, S3; L together). A shader declares its parameters and the Properties panel edits them. Cooked materials (D19) and preview thumbnails (S7) wait on this.
9. Game UI and skinned characters. The first template game needs a label on screen and a model that animates. Neither has a design doc.
10. The editor on Linux (H2). Needs an OpenGL context embedded in the shell. It is the largest piece left in the shell.

## Later

- Networking: a headless server, replication and prediction, then collaborative editing. `docs/networking.md`.
- Realms: which side of a session a node exists on. `docs/realms.md`. Lands with networking.
- Entity classes written in Luau (D15) and Luau in the console (C7), after scripting.
- More rendering: screen-space ambient occlusion and reflections, light shafts, decals, particles, compute dispatch, MSAA for the editor viewport.
- More shader language: imports, a type checker, shader variants.
- More editor: vertex and edge snapping (E8), dragging along surfaces, user themes, more than one viewport.
- Terrain, navmesh and save games. `docs/positioning.md` names them. None is designed.
- Patch and mod packs (D20) and editor plugins (D21).
- Vulkan (S9). There is no Vulkan renderer, and whether there will be one is undecided.
- Licence files. The decision is MPL-2.0 for the engine and MIT for templates. The files are not written.

## Not planned

- UE5-class rendering: GPU-driven submission, real-time global illumination, virtual shadow maps, virtual texturing, virtualized geometry, ray tracing.
- 2D as its own mode, mobile, consoles.
- Sealed maps, PVS and map extents.
- An ECS. Entities stay payloads on scene nodes.
- Video playback. `.svideo` (D22) is a reserved name and nothing more.

## Milestones

Design docs are in `docs/`. In the Design column, "archive" means `docs/archive/roadmap-2026-10.md`. Status is done, partly, in progress, not started, not planned, dropped or unclear.

### Foundations

| Id | Name | Status | Design |
|---|---|---|---|
| F1 | Material files, per-face materials, per-material chunk meshes | done | archive |
| F2 | Stable node ids, rename event, find by id | done | archive |
| F3 | One shared draw body for every pipeline | done, inside `Renderer`; there is no `ViewDrawer` class | archive |
| F4 | Shader diagnostics with codes, compile without throwing | not started | archive |

### Editor interaction

| Id | Name | Status | Design |
|---|---|---|---|
| E1 | Editing layer, move gizmo, undo | done | archive |
| E2 | Editor camera | done | archive |
| E3 | Multi-select and box select | done | archive |
| E4 | Brush resize | done | archive |
| E5 | Rotate gizmo, angle snap, local and world space | done | archive |
| E6 | Duplicate, delete, group | done; Alt+drag duplicate is not built | archive |
| E7 | Face texturing | done through material drops and the Properties panel; no fit or justify | archive |
| E8 | Vertex, edge and surface snapping | not started | archive |
| E9 | Studio and Classic gizmo styles | done; dragging along surfaces, filled handles and resizing a selection as one box are not built | archive |

### Maps and entities

| Id | Name | Status | Design |
|---|---|---|---|
| P2 | `.smap` save and load | done | formats-and-pipeline.md |
| P3 | Face materials and texture axes in the map | done | formats-and-pipeline.md |
| P4 | Entity runtime | done | archive |
| P5 | Entity source generator and schema export | done | archive |
| P6 | Logic entities | done; `logic_case` has no random pick, the runtime has no deterministic random source | archive |
| P7 | Brush entities | in progress | archive, physics.md |
| P7a | World and part brushes (`BrushKind`) | done | physics.md |
| P7b | Subtractive brushes | done | negative-brushes.md |
| P8 | Trigger volumes | in progress | archive |
| P9 | Entities and connections in the map | done | archive |
| P10 | Prefabs | not started | archive |
| P11a | Play and stop | partly; stop does not restore what a run moved | archive |
| P11b | `.spectramapb` | dropped; `.scmap` (D12) replaced it | formats-and-pipeline.md |

### Shader authoring

| Id | Name | Status | Design |
|---|---|---|---|
| S2 | Parameter scopes, UI metadata, defaults | not started | archive |
| S3 | Parameter manifest and binder | not started | archive |
| S4 | Imports | not started | archive |
| S5 | Type checker, first phase | not started | archive |
| S6 | Type checker, second phase | not started | archive |
| S7 | Material preview thumbnails | not started | archive |
| S8 | Shader features and variants | not started | archive |
| S9 | SPIR-V output for Vulkan | not started | archive |

### Rendering

| Id | Name | Status | Design |
|---|---|---|---|
| R1 | D3D12 pipeline state key | done | archive |
| R2 | sRGB end to end | done | archive |
| R3 | Offscreen render targets | done | archive |
| R4 | HDR target and tone mapping | done | archive |
| R5 | Array uniforms | done | archive |
| R6 | Shadow map | done | archive |
| R7 | Cascaded shadows | done | archive |
| R8 | Deferred pipeline, PBR, many lights | done; capped at eight lights | archive |
| R9 | Normal mapping and tangents | not started; the shading model it needs is in | archive |
| R10 | Blend state and transparency | partly; world lines blend, materials have no blend mode | archive |
| R11 | FXAA and MSAA | not started | archive |
| R12 | Instancing | done, in the shadow and geometry passes | archive, performance.md |
| R13 | Compute dispatch | not started | archive |
| R14 | Bloom, colour grading, lens effects | not started | archive |
| R15 | Ambient with direction | partly; sky and ground ambient is in, image-based lighting is not | archive |
| R16 | Temporal anti-aliasing | not started | archive |
| R17 | Screen-space ambient occlusion | not started | archive |
| R18 | Screen-space reflections | not started | archive |
| R19a | Height and distance fog | not started | archive |
| R19b | Fog volumes | not started | archive |
| R19c | Light shafts | not started | archive |
| R19d | Froxel fog | not started | archive |
| R20 | Decals | not started | archive |
| R21 | Particles | not started | archive |

### Editor shell

| Id | Name | Status | Design |
|---|---|---|---|
| H1 | H1a and H1b together | done | archive |
| H1a | Render surface seam | done | archive |
| H1b | `EngineHost` | done | archive |
| H2 | Viewport as a native child window | done on Windows; nothing on Linux | archive |
| H2b | Shell styling and toolbar | done, and since rebuilt | archive |
| H3 | Composited viewport | done for D3D11 and D3D12 | archive |
| H4 | Ribbon, docking, user themes | partly; ribbon and docking are done, user themes are not | archive |
| H10 | The viewport docks | done, composited sessions only | archive |
| H11 | Drag assets into the scene | done, composited sessions only | archive |
| H12 | Drop overlay on the viewport | done | archive |
| H13 | Ribbon | done | archive |
| H15 | Render tests, icons as files, command palette | done | archive |

### Formats and cook

| Id | Name | Status | Design |
|---|---|---|---|
| D0 | AOT publish gate, CI on two hosts | done | formats-and-pipeline.md |
| D1 | Uno AOT spike | dropped; the shell is Avalonia | formats-and-pipeline.md |
| D2 | Content source seam | done | formats-and-pipeline.md |
| D3 | `.spack` packs | done | formats-and-pipeline.md |
| D4 | The cook: `scook`, cache, dependencies | done | formats-and-pipeline.md |
| D5 | Cooked-only validation in CI | done | formats-and-pipeline.md |
| D6 | `.simage` textures | done | formats-and-pipeline.md |
| D7 | Shader cook | done; the demo still links the compiler, for loose files | formats-and-pipeline.md |
| D8 | Material cook and validation | done | formats-and-pipeline.md |
| D9 | Project file and boot from a pack | done | formats-and-pipeline.md |
| D10 | Flat BSP | done | formats-and-pipeline.md |
| D11 | Canonical `.smap` writer | done | formats-and-pipeline.md |
| D12 | `.scmap` baked maps | done | formats-and-pipeline.md |
| D13 | Cook determinism tests and the bake oracle | done | formats-and-pipeline.md |
| D14 | `.sentdef` entity schemas | done | formats-and-pipeline.md |
| D15 | Entity classes written in Luau | not started | formats-and-pipeline.md |
| D16 | Entity properties and wiring from `.sentdef` | done | formats-and-pipeline.md |
| D17 | `.smodel` and the glTF reader | done; no skins or collision hulls | formats-and-pipeline.md |
| D18 | `.saudio` and the audio manager | done; no buses or effects | formats-and-pipeline.md |
| D19 | `.smaterial` cooked materials | not started; waits on S3 | formats-and-pipeline.md |
| D20 | Patch and mod packs | partly; packs mount in order and tombstones hide entries, nothing builds a patch | formats-and-pipeline.md |
| D21 | Engine SDK mode, Luau editor plugins | not started | formats-and-pipeline.md |
| D22 | `.svideo` | not planned | formats-and-pipeline.md |

### Physics

| Id | Name | Status | Design |
|---|---|---|---|
| Y0 | Query flags and overlap queries | done | physics.md |
| Y1 | Box3D vendored, bound, published under AOT | done | physics.md |
| Y2 | The physics seam | done | physics.md |
| Y3 | The static world as convex hulls | done | physics.md |
| Y4 | One gameplay query surface | partly; the gameplay raycast is in, the rest is unclear | physics.md |
| Y5 | Character mover | done | physics.md |
| Y6 | Dynamic bodies | not started | physics.md |
| Y7 | Kinematic parts, moving platforms | not started | physics.md |
| Y8 | Touch events | not started | physics.md |
| Y9 | Collision hulls on models | not started | physics.md |
| Y10 | Play and stop for physics | not started | physics.md |
| Y11 | Luau bindings | not started | physics.md |
| Y12 | Networked bodies | not started | physics.md |
| Y13 | Rollback decision | not started | physics.md |
| Y14 | Physics self-test | not started | physics.md |
| Y15 | Convex decomposition at cook time | not started | physics.md |
| Y16 | Lag compensation | not started | physics.md |

### Scripting

| Id | Name | Status | Design |
|---|---|---|---|
| O0 | One AOT publish of the engine | done; CI publishes on both hosts | roblox-onboarding.md |
| O1 | Vector and `CFrame` value types | not started | roblox-onboarding.md |
| O2 | Familiar node API | not started | roblox-onboarding.md |
| O3 | Attributes, tags, signals | not started | roblox-onboarding.md |
| O4 | Luau vendored and bound | not started; a spike is in progress | roblox-onboarding.md |
| O5 | Node handles and generated bindings | not started | roblox-onboarding.md |
| O6 | Command bar | dropped; C7 replaced it | roblox-onboarding.md |
| O7 | `Instance.new` and parts at runtime | not started | roblox-onboarding.md |
| O8 | Script payload and lifecycle | not started | roblox-onboarding.md |
| O9 | Play and stop for scripts | not started | roblox-onboarding.md |

### Console

| Id | Name | Status | Design |
|---|---|---|---|
| C0 | Variables and commands | not started | console.md |
| C1 | Terminal front ends | not started | console.md |
| C2 | Key binds | not started | console.md |
| C3 | Retire the F1 to F6 debug keys | not started | console.md |
| C4 | Config files | not started | console.md |
| C5 | Completion and history | not started | console.md |
| C6 | Shipping flags | not started | console.md |
| C7 | Luau in the console | not started | console.md |
| C8 | Variables as project settings | not started | console.md |
| C9 | Entity commands | in progress | console.md |
| C10 | In-game overlay | not started | console.md |
| C11 | 2D overlay and font atlas | not started | console.md |
| C12 | Editor console and settings from variable metadata | not started; the editor's Console panel runs editor verbs only | console.md |

### Networking

| Id | Name | Status | Design |
|---|---|---|---|
| N0 to N22 | Game networking | not started; the fixed tick (N2) exists because physics needed it | networking.md |
| T0 to T11 | Team Edit | not started | networking.md |

`docs/realms.md` also uses R1 to R17, for its own rules. Other docs cite those as `realms.md R9`.
