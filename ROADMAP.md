# Roadmap

Where Spectra is going and in what order. What already works is in the [README](README.md#where-its-at).

The first real target is a small first-person game made completely in the editor and in Luau, with no engine code. Everything under Next is ordered by what that game needs. After it come a multiplayer shooter and then a co-op open world game, and each of those forces the next set of missing systems. [docs/positioning.md](docs/positioning.md) has the reasoning.

Ids like `O4` or `R9` are milestones. [docs/milestones.md](docs/milestones.md) lists every one with its status. Sizes are small, medium and large, and they only compare milestones with each other.

The old roadmap is [docs/archive/roadmap-2026-10.md](docs/archive/roadmap-2026-10.md). It's much longer and keeps the reasoning behind each milestone, plus the section numbers and rulings (R-1 to R-10) that other docs cite.

## Now

The no-code loop just landed, and Luau scripting right after it: build a level, wire it, script it, play it, hear it. Before the next big thing starts I want it to hold up when a person actually uses it.

- Entity icons, in the 3D view and in the panels. You should be able to tell what an entity is by looking at it.
- Trying it all by hand. The tests cover what the door, the lift, the triggers and the console do. They can't tell me how riding a lift or standing in a closing door feels, where focus goes when the console opens mid-play, or whether the Make entity menus make sense.
- Listening to it. No test knows whether a sound falls off well with distance, clicks when it starts or stops, or whether the placeholder sounds are bearable. Sound through walls is built and nobody has heard it yet. That covers the material presets, the demo's door, and whether a sound jumps when you walk past a door frame.
- Maybe: sound settings on doors, lifts and buttons (small). Wiring sound by hand is clumsy right now. The demo's start room needed seven sound entities and a dozen wires for a door, a lift and a button, and the lift's hum can still start over a lift that isn't moving. If that doesn't get better, the movers get sound settings of their own.

## Next

In this order.

### 1. Asset import

There's a cook, but nothing in the editor imports anything. A file gets into a project by copying it into the folder by hand. This needs a UI and a pipeline behind it. It should be easy as cake to use and still offer every feature.

No design yet, and no milestone ids.

### 2. Prefabs

A subtree you save once and place many times. Everything it depends on is done. The risky part is how names inside a prefab resolve: that rule gets saved into every map, and it isn't decided.

`P10`, large.

### 3. Bodies that move

Dynamic bodies, parts that push each other, and touch events between bodies. Right now the player is the only thing a moving part carries and the only thing that sets off a trigger.

`Y6` and what's left of `Y7` and `Y8`, large.

### 4. The rest of the console

The command line is there, with ways into it from the editor and the terminal, and the entity commands. What's missing is the part that makes Source's console so good: typed variables, key binds and config files.

`C0` to `C6`, large together.

### 5. The look

Normal maps, bloom, height fog, image-based ambient light, temporal anti-aliasing and transparent materials, in that order. The bar is how a good late-UE4 game looks, something like Stray. Normal maps are the risky one, because they change the vertex layout that every mesh and every CSG test baseline uses.

`R9`, `R14`, `R19a`, `R15`, `R16`, `R10`.

### 6. Material parameters

A shader declares its parameters and the Properties panel edits them. Cooked materials (`D19`) and preview thumbnails (`S7`) wait on this.

`F4`, `S2` and `S3`, large together.

### 7. Game UI and animated characters

The first template game needs a label on screen and a model that animates. Neither has a design doc yet.

### 8. The editor on Linux

The shell needs an OpenGL context embedded in it. It's the largest piece left in the shell.

`H2`.

## Later

Not in any order.

- Multiplayer. A headless server, replication and prediction, and after that editing a level together. [docs/networking.md](docs/networking.md).
- Realms: which side of a session a node exists on. Lands with multiplayer. [docs/realms.md](docs/realms.md).
- Sound around corners. A spike says a grid of air cells flooded from the listener holds up, with limits ([the report](docs/spikes/2026-10-sound-round-corners.md)). The design is in [docs/audio.md](docs/audio.md). No code yet.
- Entity classes written in Luau (`D15`) and Luau in the console (`C7`).
- More rendering: screen-space ambient occlusion and reflections, light shafts, decals, particles, compute dispatch, MSAA for the editor viewport.
- More shader language: imports, a type checker, shader variants.
- More editor: vertex and edge snapping (`E8`), dragging along surfaces, user themes, more than one viewport.
- Terrain, navmesh and save games. [docs/positioning.md](docs/positioning.md) names them and none of them is designed.
- Patch and mod packs (`D20`) and editor plugins (`D21`).
- Vulkan (`S9`). There's no Vulkan renderer and I haven't decided whether there will be one.
- License files. The decision is MPL-2.0 for the engine and MIT for templates. The files aren't written.

## Not planned

- UE5-class rendering: GPU-driven submission, real-time global illumination, virtual shadow maps, virtual texturing, virtualized geometry, ray tracing. Maybe one day, but nothing here plans for it.
- 2D as its own mode, mobile, consoles.
- Sealed maps, PVS and map extents.
- An ECS. Entities stay payloads on scene nodes.
- Video playback. `.svideo` (`D22`) is a reserved name and nothing more.
