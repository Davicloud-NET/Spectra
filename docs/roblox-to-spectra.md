# Roblox to Spectra

What each Roblox concept is in the engine's code, and how it stands. This page is for work on the engine.

What a script author reads is on the site: `site/src/content/docs/start/coming-from-roblox.mdx`. It has every difference a script meets, grouped by what comes up in the first hour, and the list of what is not there yet. A fact about scripts belongs there, and this page only points at the types behind it.

This page replaces the concept map of August 2026, which was written when the engine had no editor, no scripting and no save format. Other documents cite its rows by name, so the tables keep them: the `Lighting` row, the `NegateOperation` row and the Instance API table.

Status is one of three words.

| Status | Meaning |
|---|---|
| built | In the tree, and tested. |
| planned | Designed in the document the row names, and not written. |
| not planned | The engine does it another way on purpose, or not at all. |

## Tree and services

| Roblox | Here | Status | Note |
|---|---|---|---|
| `Instance` | `SceneNode`. A script holds one as `Instance` | built | One node type. What a node is comes from what it carries: a brush, a mesh, a light, an entity, a script. `Scene/NodeClass.cs` gives each a class name for `IsA`, from a table |
| Instance identity | `SceneNode.Id`, a `Guid` | built | Stable across a rename and a new parent. Saves, undo and wires go by it |
| `DataModel`, `game` | `Scene` in C#. `game` to a script | built | `game:GetService` knows `RunService`, `Workspace`, `Players` and `CollectionService`, and refuses any other name |
| `Workspace` | `Scene.Root`. `workspace` to a script | built | There is no container: everything in the level is under the root, and its class to a script is `Folder` |
| `Lighting` | A typed `Scene.Environment` for global settings | planned | A light itself is a node with a `Light` payload, which is built, and a script sets its members. Nothing holds fog or a sky as data yet. A bounded fog zone would be a payload on a node (`R19b`) |
| `ServerScriptService`, `ServerStorage`, `ReplicatedStorage`, `ReplicatedFirst` | A node's `Realm` and `State` | planned | [`realms.md`](realms.md). A node declares both and a map saves them, and nothing reads them: there is one side and every node is live. A template a script clones sits in the level |
| `StarterGui`, `StarterPack`, `StarterPlayer` and its script folders | Spawn rules in the project file | planned | [`realms.md`](realms.md) |
| `RunService` | The fixed tick | built | `PreSimulation` and `Stepped` before the physics step, `PostSimulation` and `Heartbeat` at the end of the tick, 60 a second with the same step. Nothing fires once a frame |
| `Players` | `Players.LocalPlayer` and its `Character` | built for one player | The character is no node. `IPlayerPresence` is the player as the entity runtime sees it and `PlayerWrites` the one way a script changes it. More than one player is [`networking.md`](networking.md), planned |
| `CollectionService` | `TagSet` on the node, and a reverse index in the script runtime | built | The index lives for one play session. No `ObserveTag` |
| `SoundService`, `Sound` | The `point_sound` entity | built | A script plays one through its inputs, as a wire does. [`audio.md`](audio.md) |
| `Humanoid` | `CharacterSimulation` and its `ICharacterMover` | not planned as an object | The mover is a function a script can replace (`physics.md` §4.5). There is no health and no character model |
| `Teams`, `Debris`, `CoreGui`, `Chat`, `TestService` | Nothing | not planned | `Debris` is `task.delay` and `Destroy` |

## Parts and geometry

| Roblox | Here | Status | Note |
|---|---|---|---|
| `Part`, `BasePart` | A node with a part brush: `BrushKind.Part` | built | It keeps its own faces, moves at no recompile, and is what a script makes, moves and destroys |
| Level geometry | A node with a world brush, which the editor calls a block | built | Fused into the static world. A script cannot make, move, clone or destroy one, and gets an error that says to convert it |
| `MeshPart` | A node with a `MeshRenderer` | built | A script has the class and cannot make one. It has no collision shape of its own yet (`Y9`) |
| `UnionOperation` | The carve of all world brushes | not planned as an object | Overlapping blocks become one solid, live, with nothing to bake |
| `NegateOperation` | `Brush.Operation = Subtractive`, which the editor calls a cut | built | [`negative-brushes.md`](negative-brushes.md). One bit on the brush and no wrapper. It cuts every block it overlaps and never a part |
| `Part.Shape` | A convex set of planes | built | Wedges and prisms are such sets. A sphere is not |
| `Part.Size` | The brush's plane extents: `Brush.WithScaledExtents` | built in the editor | The size tool. A script has no `Size` yet |
| `Part.CFrame`, `Position` | `SceneNode.CFrame` and `WorldPosition`, with `LocalCFrame` and `LocalPosition` | built | World space to a script, as in Roblox. A node also moves with its parent, which a Roblox part does not |
| Scale | `SceneNode.LocalScale` | built | Never on a brush or above one. A brush is resized by its planes |
| `Anchored` | Nothing | planned with `Y6` | No body falls yet, so every part stays where it is put. [`physics.md`](physics.md) |
| `CanCollide`, `CanQuery`, `CanTouch` | Flags on the node | built | A script sets them on a part. `CanQuery` does not follow `CanCollide` |
| `Touched` | `TouchTracker`, for the player against parts | built for the player | A trigger entity and a script's `Touched` hear one pass. Nothing touches but the player (`Y8`) |
| `Material`, `Color`, `Transparency` | A material for each face of a brush | built in the editor | A script cannot set them. `Visible` draws or hides a part. Blended transparency is `R10` |
| Texture alignment | UV axes for each face | built | Saved in the map |
| 1 stud | 1 unit is 1 metre | built | Decided 2026-08-21. A ported number has to be scaled: a `WalkSpeed` of 16 is 4.5 here |

## Values

| Roblox | Here | Status | Note |
|---|---|---|---|
| `Vector3` | `System.Numerics.Vector3`. Luau's own vector type to a script | built | |
| `CFrame` | `Scene/CFrame.cs` | built | A position and a rotation, no scale. Roblox's meaning for every operation, held to answers from Studio (`data-model.md` §2.3) |
| `Color3` | `Scene/Color3.cs` | built | A colour as a person picks one. The engine lights in linear values, and each store converts at its own door |
| `Orientation` in degrees | `CFrame.fromOrientation` in radians | not planned | The Properties panel shows degrees |
| `BrickColor`, `Enum`, `UDim2`, `Vector2`, `TweenInfo` | Nothing | not planned | Nothing a script can reach takes one |

## Instance API

| Roblox | Here | Status | Note |
|---|---|---|---|
| `Instance.new` | `TreeWrites.TryMake`, by `MadeNodes` | built | A part, a folder and the five lights. Anything else is cloned |
| `Parent = x` | `TreeWrites.TrySetParent` | built for scripts | Guards a cycle, the root and a destroyed node. In C# `SceneNode.Parent` is read only, with `AddChild` and `RemoveChild` |
| `GetChildren`, `GetDescendants`, `FindFirstChild` and its relatives, `IsA` | The binding's `NodeApi`, over `SceneNode.Children` and `Traverse` | built | |
| `WaitForChild` | A thread that waits in the entity runtime's queue | built | It yields until the child arrives |
| `workspace.Door` | `ObjectMembers` looks a name that is no member up among the children | built | |
| `Clone`, `Destroy` | `TreeWrites.TryClone`, `TryDestroy` | built for scripts | `SceneNode.Clone` copies the brush and shares the mesh. Stop takes clones away and brings destroyed nodes back |
| Attributes | `AttributeSet` on the node, `AttributeTable` in the script runtime | built | Six kinds of value. A script also reads its entity's keyvalues through `GetAttribute`. No kind refers to a node |
| Tags | `TagSet` on the node, `TagTable` in the script runtime | built | |
| `RBXScriptSignal` | `ScriptSignal` in the binding | built for scripts | An entity's outputs, a part's touch, the tick, a change of a tag or an attribute. There is no signal type in C#, and `ChildAdded`, `Changed` and `Destroying` are not built |
| Deferred signals | The entity runtime's queue | built | A connected function never runs inside the call that fired |
| `Model`, `PrimaryPart` | Any node with children | not planned as a class | Transforms compose down the tree, so a group moves as one |

## Scripts and Studio

| Roblox | Here | Status | Note |
|---|---|---|---|
| `Script`, `ModuleScript` | `SceneNode.Script`: a path to a `.luau` file, `IsEnabled`, `IsModule` | built | [`data-model.md`](data-model.md) §3. `require` takes a path or a module node |
| `LocalScript`, `RunContext` | The node's realm | planned | [`realms.md`](realms.md) §6.2. There is one side today |
| Gameplay in the engine's own language | An entity class in C#, with its parsing and registration from a source generator | built | Entity classes in Luau are `D15`, planned |
| Explorer, Properties, Output | The scene tree, the Properties panel and the Output panel | built | |
| Command Bar | Luau in the console, `C7` | planned | [`console.md`](console.md) |
| Play and Stop | `PlaySession` and the play journal | built | Stop puts back what the run changed. No snapshot and no reload of the level |
| `.rbxl` | `.smap`, a folder of JSON | built | [`formats-and-pipeline.md`](formats-and-pipeline.md) §2.6. A baked `.scmap` is what a game ships |
| Studio's script editor | Visual Studio Code with `luau-lsp` | built | The editor writes `spectra.d.luau` and the settings that point at it |

## The service containers

Roblox says where a thing is held, when it arrives and whether it is live by where it sits in the tree. The design here makes those two properties of the node, `Realm` and `State`, inherited down the tree, and ships the folders a Roblox developer expects as ordinary nodes of a project template. None of it is built beyond the two declarations, which a map saves. [`realms.md`](realms.md) is the design and has the mapping of every container.

Until then a level has one side. Every node is live, every script runs once when the level starts, and a template sits in the level.

## What Spectra does that Roblox does not

- Blocks carve each other live and without baking, and a cut removes solid from them.
- The world has no edges, and an edit costs the same in a large world as in a small one.
- A face has its own material and its own texture axes.
- Entities are wired with no code, and a script uses the same inputs and outputs.
- A project is plain files, and a level is JSON that diffs.
- A game is a NativeAOT executable that reads packs.
- Shaders are written in the engine's own shader language and reload on save.
