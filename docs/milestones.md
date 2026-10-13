# Milestones

Every milestone id and where it stands. The order of work is in [ROADMAP.md](../ROADMAP.md).

Status was last checked against the code on 2026-10-05, and the scripting rows on 2026-10-13. It is one of: done, partly, in progress, not started, not planned, dropped or unclear.

The Design column names a file in this folder. "archive" means [archive/roadmap-2026-10.md](archive/roadmap-2026-10.md), the old long roadmap. It has the reasoning behind each milestone.

Sizes in the roadmap are small, medium and large. They only compare milestones with each other.

## Foundations

| Id | Name | Status | Design |
|---|---|---|---|
| F1 | Material files, per-face materials, per-material chunk meshes | done | archive |
| F2 | Stable node ids, rename event, find by id | done | archive |
| F3 | One shared draw body for every pipeline | done, inside `Renderer`; there is no `ViewDrawer` class | archive |
| F4 | Shader diagnostics with codes, compile without throwing | not started | archive |

## Editor interaction

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

## Maps and entities

| Id | Name | Status | Design |
|---|---|---|---|
| P2 | `.smap` save and load | done | formats-and-pipeline.md |
| P3 | Face materials and texture axes in the map | done | formats-and-pipeline.md |
| P4 | Entity runtime | done | archive |
| P5 | Entity source generator and schema export | done | archive |
| P6 | Logic entities | done; `logic_case` has no random pick, the runtime has no deterministic random source | archive |
| P7 | Brush entities | done; a brush entity is a part that carries an entity | archive, physics.md |
| P7a | World and part brushes (`BrushKind`) | done | physics.md |
| P7b | Subtractive brushes | done | negative-brushes.md |
| P8 | Trigger volumes | done for the player; nothing else sets a trigger off | archive |
| P9 | Entities and connections in the map | done | archive |
| P10 | Prefabs | not started | archive |
| P11a | Play and stop | done; stop puts back everything the run changed, from the play journal | archive |
| P11b | `.spectramapb` | dropped; `.scmap` (D12) replaced it | formats-and-pipeline.md |

## Shader authoring

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

## Rendering

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

## Editor shell

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

## Formats and cook

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
| D12 | `.scmap` baked maps | done; entities, node flags, lights and world collision are in, placed models and spawns are not | formats-and-pipeline.md |
| D13 | Cook determinism tests and the bake oracle | done | formats-and-pipeline.md |
| D14 | `.sentdef` entity schemas | done | formats-and-pipeline.md |
| D15 | Entity classes written in Luau | not started | formats-and-pipeline.md |
| D16 | Entity properties and wiring from `.sentdef` | done | formats-and-pipeline.md |
| D17 | `.smodel` and the glTF reader | done; no skins or collision hulls | formats-and-pipeline.md |
| D18 | `.saudio` and the audio manager | done, with markers since version 2; no buses or effects | formats-and-pipeline.md |
| D19 | `.smaterial` cooked materials | not started; waits on S3 | formats-and-pipeline.md |
| D20 | Patch and mod packs | partly; packs mount in order and tombstones hide entries, nothing builds a patch | formats-and-pipeline.md |
| D21 | Engine SDK mode, Luau editor plugins | not started | formats-and-pipeline.md |
| D22 | `.svideo` | not planned | formats-and-pipeline.md |

## Physics

| Id | Name | Status | Design |
|---|---|---|---|
| Y0 | Query flags and overlap queries | done | physics.md |
| Y1 | Box3D vendored, bound, published under AOT | done | physics.md |
| Y2 | The physics seam | done | physics.md |
| Y3 | The static world as convex hulls | done | physics.md |
| Y4 | One gameplay query surface | partly; the gameplay raycast is in, the rest is unclear | physics.md |
| Y5 | Character mover | done; a script can put a mover written in Luau in its place | physics.md |
| Y6 | Dynamic bodies | not started | physics.md |
| Y7 | Kinematic parts, moving platforms | partly; a moving part carries the player and stops when it would squeeze them, and nothing else rides or is pushed | physics.md |
| Y8 | Touch events | partly; the player against parts, for triggers and for a script's `Touched`, and nothing between bodies | physics.md |
| Y9 | Collision hulls on models | not started | physics.md |
| Y10 | Play and stop for physics | not started | physics.md |
| Y11 | Luau bindings | partly; `CanCollide`, `CanQuery`, `CanTouch`, `Touched` and the player are bound, rays and bodies are not | physics.md |
| Y12 | Networked bodies | not started | physics.md |
| Y13 | Rollback decision | not started | physics.md |
| Y14 | Physics self-test | not started | physics.md |
| Y15 | Convex decomposition at cook time | not started | physics.md |
| Y16 | Lag compensation | not started | physics.md |

## Scripting

| Id | Name | Status | Design |
|---|---|---|---|
| O0 | One AOT publish of the engine | done; CI publishes on both hosts | roblox-onboarding.md |
| O1 | Vector and `CFrame` value types | done; `Vector3` is Luau's own vector type | roblox-onboarding.md, data-model.md |
| O2 | Familiar node API | done for scripts; no `Size`, and `SceneNode` kept its C# surface | roblox-onboarding.md, script-api.md |
| O3 | Attributes, tags, signals | done another way; tags and attributes are built in the editor and set by scripts for one play session, signals are a script's own, and there is no `ObserveTag` and no attribute that refers to a node | roblox-onboarding.md, script-api.md |
| O4 | Luau vendored and bound | done; the crash cases run from a published binary on both hosts | roblox-onboarding.md, spikes/2026-10-luau-aot.md |
| O5 | Node handles and generated bindings | done; one generator writes the binding, `spectra.d.luau` and the reference's data | roblox-onboarding.md, script-api.md |
| O6 | Command bar | dropped; C7 replaced it | roblox-onboarding.md |
| O7 | `Instance.new` and parts at runtime | done; a made part is a part brush | roblox-onboarding.md, script-api.md |
| O8 | Script payload and lifecycle | done; script events run on the fixed tick, and nothing fires once a frame | roblox-onboarding.md, data-model.md |
| O9 | Play and stop for scripts | done; a play journal puts back what a script changed | roblox-onboarding.md, data-model.md |

The October plan built these in 22 steps under its own numbers: Luau in the tree, the binding, a queue that counts ticks, `CFrame`, the script payload, the runtime, the play journal, the generator, output with a place, the editor, the node API, typed inputs and outputs, inputs as methods and outputs as signals, properties and attributes, the cook and the baked map, touch, the player, tags and attributes on any node, create and destroy, `require`, the mover in Luau, and the docs. `roblox-onboarding.md` has what changed against the August design.

Not built: keys and mouse for scripts, script events once a frame and the camera, entity classes in Luau (D15), Luau in the console (C7), a debugger, native code generation, bytecode in packs, scripts that run while editing (D21), parallel Luau, and a part's colour and transparency from a script.

## Documentation

The user docs as a place to learn the engine, after the model of Roblox's Creator Hub and learn.microsoft.com. No design doc yet. The pages that exist were written beside the features and are the starting stock.

| Id | Name | Status | Design |
|---|---|---|---|
| L1 | Structure: a front page by what you came to do, and the sections tutorials, how-to guides, concepts, reference and samples | not started; the site has Start here, Concepts, Guides and Reference | none yet |
| L2 | Page kinds: every page is one kind, with a template, what you need before it and where to go next | not started; `writing-docs.mdx` has rules for a guide only | none yet |
| L3 | A tutorial course that builds one small game from an empty project to a cooked build | not started | none yet |
| L4 | The script guides and the Roblox page moved into the structure, one task a page | not started; six guides and Coming from Roblox exist | none yet |
| L5 | Reference with an example for every member: script API, entities, console, file formats | partly; the script and C# references are generated, with no examples | script-api.md |
| L6 | Screenshots and short videos, captured by a script so they can be taken again | not started; no page has an image of scripting | none yet |
| L7 | Sample projects a tutorial ends in, a link check and the site build in CI, and the deploy | not started | none yet |

## Console

| Id | Name | Status | Design |
|---|---|---|---|
| C0 | Variables and commands | partly; a command table with `help`, `echo` and `wait`, no variables and no generator | console.md |
| C1 | Terminal front ends | partly; the demo takes `--command` and reads its terminal with `--console` | console.md |
| C2 | Key binds | not started | console.md |
| C3 | Retire the F1 to F6 debug keys | not started | console.md |
| C4 | Config files | not started | console.md |
| C5 | Completion and history | not started | console.md |
| C6 | Shipping flags | not started | console.md |
| C7 | Luau in the console | not started | console.md |
| C8 | Variables as project settings | not started | console.md |
| C9 | Entity commands | done; `ent_fire`, `ent_list`, `ent_show`, `ent_watch` | console.md |
| C10 | In-game overlay | not started | console.md |
| C11 | 2D overlay and font atlas | not started | console.md |
| C12 | Editor console and settings from variable metadata | not started; the editor's Console panel runs editor verbs and forwards the rest | console.md |

## Networking

| Id | Name | Status | Design |
|---|---|---|---|
| N0 to N22 | Game networking | not started; the fixed tick (N2) exists because physics needed it | networking.md |
| T0 to T11 | Team Edit | not started | networking.md |

`docs/realms.md` also uses R1 to R17, for its own rules. Other docs cite those as `realms.md R9`.
