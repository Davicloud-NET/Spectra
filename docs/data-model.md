# The Spectra data model

> **How to read this page.** What exists is cited by file. What is marked **PLANNED** names the document that designs it and is not in the code.
>
> The payload list in section 3, section 6 and the summary table in section 8 were checked against the tree on 2026-10-05. The `file:line` citations in the other sections were read on 2026-08-21 and the line numbers have moved since. Trust the file name, not the number.

---

## 1. Orientation: three ideas

Everything below follows from three decisions. They are worth reading first, because most of the model's oddities are consequences of them rather than choices in their own right.

### The scene graph is the single spine. There is no ECS.

One concrete class — `SceneNode` (`SpectraEngine.Core/Scene/SceneNode.cs:14`) — holds a name, a local transform, a parent and children, and *optional payload fields*. There is no component registry, no archetype table, no node subclassing, no polymorphic node types. World geometry is not a parallel system: a brush is a field on a node (`SceneNode.cs:116`).

**Consequence:** every new capability is a nullable field on `SceneNode` or it does not exist. That is cheap to reason about and cheap to traverse, and it makes the payload count a real, watched cost on the hottest type in the engine. There are four payload fields today (§3), and each addition is a deliberate act.

### Derived data is never authored.

Brushes are the authored truth for static world geometry. The carved surfaces, the per-cell BSP trees, the chunk meshes and the GPU buffers are all *recomputed* from brush placements (`Scene.cs:335`, documented as a build artifact). Nothing writes into them; you change the world by editing brush nodes, and the engine notices.

**Consequence:** derived state may be discarded at any point without losing information, and the whole compile can run on a background thread reading nothing but an immutable snapshot (`Scene.cs:1011`). It also means the save format may contain no derived data at all — a rule `docs/formats-and-pipeline.md` §2.6 carries into `.smap`.

### Identity is a `Guid`, everywhere.

`SceneNode.Id` (`SceneNode.cs:60`) is assigned at construction and never changes — not on rename, not on reparent, not on moving between scenes. A second constructor (`SceneNode.cs:44`) exists solely so a node can be *recreated under its old id*.

**Consequence:** references survive destruction. Undo of a delete rebuilds the node with the same id, so editor commands address nodes by `Guid` rather than by object reference (`SpectraEngine.Editing/Commands/IEditorCommand.cs:11`), and `Scene.TryFindById` (`Scene.cs:186`) is the resolver. The same property is what the planned save format, collaborative editing and entity wiring all lean on.

---

## 2. The spine as it is today

### 2.1 Identity

| Member | Where | Notes |
| --- | --- | --- |
| `SceneNode.Id` — `Guid` | `SceneNode.cs:60` | Immutable. Stable across rename, reparent, scene change. |
| `new SceneNode(name)` | `SceneNode.cs:30` | Mints a fresh `Guid.NewGuid()`. |
| `new SceneNode(name, id)` | `SceneNode.cs:44` | Re-uses an identity — the undo-of-delete and deserialization door. |
| `SceneNode.Name` — `string` | `SceneNode.cs:62` | Mutable, not unique, not an identity. |
| `Scene.TryFindById` | `Scene.cs:186` | O(1), allocation-free. Backed by `_nodesById` (`Scene.cs:176`), maintained from the membership events. |
| `Scene.NodeCount` | `Scene.cs:193` | The size of that index — i.e. the graph's node count, root included. |

**Non-obvious:** the id index is written with the indexer, not `Add` (`Scene.cs:171`), because it runs inside the ownership walk where a throw would leave the graph half-owned. If two live nodes ever shared an id, the most recently added wins rather than crashing an edit. De-indexing is identity-checked (`Scene.cs:143`) so a stale duplicate cannot unmap the live node.

### 2.2 Hierarchy

| Member | Where |
| --- | --- |
| `Parent` (read-only) | `SceneNode.cs:64` |
| `Children` — `IReadOnlyList<SceneNode>` | `SceneNode.cs:75` |
| `AddChild` / `CreateChild` / `RemoveChild` | `SceneNode.cs:247` / `:287` / `:293` |
| `Traverse()` — pre-order, explicit stack | `SceneNode.cs:313` |
| `Scene.Root` | `Scene.cs:60` |
| `Scene.Nodes` — `Root.Traverse()` | `Scene.cs:1392` |
| `SceneNode.Owner` — owning `Scene`, internal | `SceneNode.cs:73` |

**Non-obvious invariants:**

- **Sibling order is load-bearing.** Traversal order drives `BrushPlacement` order, which drives carve order, which the determinism oracles pin.
- **`Owner` propagates per subtree, not per node.** `SetOwner` (`SceneNode.cs:343`) early-outs when the owner is unchanged, which is exactly why a reparent *within* one scene raises no membership events — the nodes never left.
- **`AddChild` invalidates cached world matrices *before* announcing the node** (`SceneNode.cs:265`), because `NodeAdded` handlers read `WorldMatrix`.
- **There is no cycle guard.** `AddChild` does not check ancestry; a parent loop recurses to stack exhaustion in `MarkWorldDirty`.

### 2.3 Transform

| Member | Where |
| --- | --- |
| `Transform` struct — `Position`, `Rotation`, `Scale` | `SpectraEngine.Core/Scene/Transform.cs:5` |
| `Transform.Model` — S·R·T | `Transform.cs:20` |
| `LocalTransform` | `SceneNode.cs:155` |
| `LocalPosition` / `LocalRotation` / `LocalScale` | `SceneNode.cs:171` / `:183` / `:195` |
| `WorldMatrix` (cached) | `SceneNode.cs:225` |
| `WorldPosition` | `SceneNode.cs:239` |
| `CFrame` struct: `Position`, `Rotation`, no scale | `SpectraEngine.Core/Scene/CFrame.cs` |
| `LocalCFrame` / `CFrame` (world) | `SceneNode.cs`, solved in `Scene/WorldFrame.cs` |

**Non-obvious invariants:**

- **Every transform setter early-outs on exact equality** (`SceneNode.cs:162`, `:176`, `:184`, `:196`). A no-op write invalidates nothing, dirties no static world, and raises no `NodeTransformChanged`. This is what makes absolute-value commands free to replay.
- **`WorldMatrix` is cached and lazily recomputed** (`SceneNode.cs:229`): `local * Parent.WorldMatrix`. The dirty flag is propagated *eagerly* down the whole subtree by `MarkWorldDirty` (`SceneNode.cs:390`) — cheap for shallow trees, revisit for deep ones.
- **Brush placements must be rigid.** `Scene.DescribeNonRigidDefect` (`Scene.cs:1409`) rejects a snapshot whose brush node has non-finite elements, projective components, scale, shear, or a reflection, at tolerance `1e-4f` (`Scene.cs:1398`). Rigidity is enforced at the snapshot — the single point where node transforms enter the brush pipeline — not trusted.
- **Rigidity is a *subtree* property.** A scale written anywhere above a world brush makes that brush's placement non-rigid and rejects the whole snapshot. A tool about to write `LocalScale` must consult `SubtreeBrushCount`, not `node.Brush is not null`. That count covers world brushes and parts alike (§2.4), because node scale is as wrong above a part as above a world brush: `LocalScale` accepts negative components, `Brush.WithScaledExtents` refuses them, and a mirrored brush inverts winding. The snapshot only checks world brushes, so for a part the callers keep the rule. `ScaleGizmo` reads the count, and `EntityWorld.SetLocalTransform` refuses a scale change above any brush. `docs/physics.md` §2.3a.

#### CFrame

A `CFrame` is a position and a unit quaternion. It is the transform a script works with, and every operation on it means what it means in Roblox:

- `a * b` places `b` in the space of `a`. Applied to a point it applies `b` first, then `a`. The engine's matrices go the other way round, `world = local * parent.World`, because they act on row vectors. So `child.CFrame` is `parent.CFrame * child.LocalCFrame`.
- A frame looks along its own -Z (`LookVector`), with +X right and +Y up. The engine's lights and player starts face +Z. That difference is decided and stays.
- `Angles(rx, ry, rz)` takes radians and applies the Z turn first: `Rx * Ry * Rz`. `FromOrientation` is `Ry * Rx * Rz`, the same order as `Quaternion.CreateFromYawPitchRoll(ry, rx, rz)` and as `EulerAngles`.
- `GetComponents` writes the position, then the rotation matrix row by row for column vectors. `ToMatrix` gives the engine's matrix, which holds the same nine numbers transposed.
- Two frames are equal when all twelve components are, so a quaternion and its negative are one frame. `default(CFrame)` is the identity.
- A product keeps a rotation that is unit length to within rounding as it is. One that has drifted past that is renormalized, so a long chain of products stays a rotation.
- A vector is turned by the rotation matrix, not by `Vector3.Transform` with the quaternion. The matrix sends the zero vector to zero with nothing left over, so `a * CFrame.Identity` equals `a` and a rotation times a rotation stays at the origin.

On a node:

- `LocalCFrame` reads and writes `LocalPosition` and `LocalRotation` and keeps `LocalScale`.
- `CFrame` is the world frame. Its position is `WorldPosition`. Its rotation is the rotations of the node and its ancestors composed, with scale left out. Under a stretched and turned ancestor the world matrix is sheared and holds no rotation of its own, so this is the one definition that always has an answer.
- Setting `CFrame` solves the local transform under the parent. The position goes through the inverse of the parent's world matrix, so it lands where asked under a scaled ancestor too. The parent's own position is taken off before the matrix is inverted, which keeps the answer to a float step far from the origin.
- A world position the node already has is not solved again. So `node.CFrame = node.CFrame * CFrame.Angles(0, 0.01, 0)` turns the node where it stands, as in Roblox, however often it is written.
- Writing a frame equal to the one the node has changes nothing, in either setter. Equal means equal as frames: the same rotation with its quaternion negated is no move. A world frame the node already has is not solved at all, since the answer would come back a rounding error away and count as a move.

Where Roblox's documentation gives no answer, the engine's is its own and the tests say so: a look along the up vector keeps the right vector on +X, a look at the frame's own position gives no rotation, `FromAxisAngle` takes an axis of any length, `FromMatrix` squares up columns that are not a rotation, at an Euler pole the Z angle is zero, and `Lerp` with an alpha below 0 or above 1 carries on along the same line and the same arc.

The meanings are pinned twice. The tests hold `CFrame` against the rules written out with plain 3 by 3 matrices (`RobloxFrame` in the Bsp tests). And `Test/SpectraEngine.Bsp.Tests/Data/cframe-probe.luau` asks Roblox Studio the same cases: paste it into Studio's command bar, save what it prints beside it as `cframe-studio.json`, and the suite compares every case. Until that file exists those two tests skip.

### 2.4 Payload bookkeeping

| Member | Where |
| --- | --- |
| `SubtreeBrushCount` | `SceneNode.cs` |
| `SubtreeStaticWorldBrushCount` | `SceneNode.cs` |
| `AdjustSubtreeBrushCounts` (private) | `SceneNode.cs` |

Two counts with one writer. `SubtreeBrushCount` counts every brush in the subtree, the node's own included. `SubtreeStaticWorldBrushCount` counts only `World` brushes. Both are `int` fields, and `AdjustSubtreeBrushCounts` is the only code that writes either. It walks the ancestor chain from the `Brush` setter, the `BrushKind` setter and every reparent, so reading a count is O(1).

Each answers its own question. The total is what a tool reads before it writes `LocalScale` (§2.3). The static-world count decides whether a transform edit dirties the compiled world: moving a camera, a prop or a subtree of parts launches no recompile, and moving a group with a world brush below it does.

### 2.5 Events

| Event | Where |
| --- | --- |
| `Scene.NodeAdded` | `Scene.cs:98` |
| `Scene.NodeRemoved` | `Scene.cs:108` |
| `Scene.NodeTransformChanged` | `Scene.cs:118` |

**Non-obvious contract (`Scene.cs:85`): handlers must not mutate the graph.** Membership events fire *in the middle of* the ownership walk, and a structural edit there corrupts the traversal. Observe and record; defer structural edits until after the event returns. This is a stated contract, not an enforced guard.

Further rules: a subtree attach raises `NodeAdded` once per node, pre-order; a cross-scene move raises `NodeRemoved` on the source and `NodeAdded` on the destination, per node; a reparent within one scene raises **nothing**; `Owner` is already repointed when a handler runs; and `NodeTransformChanged` fires for every owned node, brush-bearing or not.

Two internal hooks cover what the public events do not: `OnNodeSpatialComponentChanged` (`Scene.cs:155`) for payload edits on an already-owned node, and `OnNodeSubtreeMoved` (`Scene.cs:157`) for in-scene reparents.

### 2.6 Spatial queries

| Member | Where |
| --- | --- |
| `Scene.Raycast` → `SceneRaycastHit` | `Scene.cs:208`; hit type at `SceneBvh.cs:30` |
| `Scene.TraceSolidSpans` → `SolidSpan` | `Scene.SolidSpans.cs`; span type in `SolidSpan.cs` |
| `Scene.QueryFrustum` | `Scene.cs:219` |
| `Scene.TryGetWorldBounds` | `Scene.cs:235` |
| `Scene.BuildRenderView` | `Scene.cs:269` |
| `SceneBvh` (internal dynamic AABB tree) | `SceneBvh.cs:60` |
| `SceneBvh.IsSpatial` | `SceneBvh.cs:146` |
| `Ray3` (unit-direction contract) | `Ray3.cs:16` |
| `Frustum` (six planes) | `Frustum.cs:16` |
| `Aabb` | `Bsp/Aabb.cs:8` |
| `Camera` | `Camera.cs:14`; `ScreenPointToRay` `:213`, `ScreenRectToFrustum` `:263`, `GetFrustum` `:203` |

**Non-obvious:** a node is *spatial*, meaning the BVH tracks it, when it carries a `MeshRenderer` or a `Brush` (`SceneBvh.cs`). The test ignores ancestry and ignores `BrushKind`: world brushes and parts are both indexed, or parts would fall out of editor picking (`docs/physics.md` §2.3a). The scene keeps a second BVH for what draws on its own, which is a mesh or a part brush with `IsRendered` on. `CanQuery` is in neither test. A query applies it through its `SceneQueryFilter`. `realms.md` R12 plans a *liveness* condition here and no realm one. `QueryFrustum` does not clear the results list; `Frustum.Intersects` is conservative (false positives possible, false negatives not).

`TraceSolidSpans` answers what a sound has to get through: the stretches of solid on a segment, nearest first, each with the material of the face the segment entered by. It reads the static world as last compiled, from the live placements or from a baked map's collision hulls, and the part brushes that collide, where they stand now. Cuts are open, and what lies behind a cut names the cut's face. Meshes do not block. It clips brush planes and does not ask the BSP.

A segment that lies in the plane of a brush face is in solid only where there is solid on every side of it. In the seam of two flush brushes it is, and under a wall that stands on the floor. Along the face of a wall, the sill of a cut window or the jamb of an open doorway it is not. A segment counts as lying in a face when it stays within `Polygon.Epsilon` of the plane from end to end.

### 2.7 Selection

`SelectionSet` (`SelectionSet.cs:20`) is scene-owned (`Scene.cs:68`), created before the root is claimed so it can subscribe to `NodeRemoved` for auto-deselection (`SelectionSet.cs:49`, `:263`). Surface: `Items` (`:58`), `Count` (`:61`), `SelectionChanged` (`:70`), `Contains` (`:73`), `Select`/`Add`/`Toggle`/`Deselect` (`:80`/`:98`/`:114`/`:132`), the batched `Apply` and its `SetRange`/`AddRange`/`ToggleRange` wrappers (`:185`, `:158`, `:166`, `:173`), and `Clear` (`:253`).

### 2.8 Threading

**Every member of `Scene`, `SceneNode`, `SelectionSet`, `SceneBvh` and `RenderView` is render-thread-only** (`Scene.cs:35`). The compile-state fields are deliberately unsynchronized (`Scene.cs:428`) because scene edits are single-threaded. The only work that leaves the render thread is the background CSG compile, and it reads nothing but its immutable snapshot; `Task.Run`/task completion provide the happens-before edges.

---

## 3. The payload model

A **payload** is an optional, nullable field on `SceneNode` that gives the node a job beyond being a named transform. It is composition, not subclassing: there is one node class and no `IPayload` interface. Assigning a payload notifies the owning scene through the internal hooks, which is how the BVH and the static world stay in step.

**Four payloads exist today**, and this page is the counting authority the other documents cite. They are `MeshRenderer`, `Brush`, `Light` and `Entity`. Two more are designed and unbuilt: `Script` and `PrefabInstance` (§6). A document that names an ordinal ("the fifth payload") must agree with this paragraph or say that it is counting designed ones.

Some node fields are not payloads and do not move the count:

- `BrushKind` is a one-byte enum that qualifies the `Brush` the node already carries.
- `PhysicsFlags` is one byte of bits and `CollisionGroup` is another byte. Physics bodies are kept out of the payload list on purpose (`docs/physics.md` §2.6). They are meant to live in a side table, which is not built.
- `IsRendered` is a bool.
- `MeshSource` names the model file a `MeshRenderer` came from. Clearing the renderer clears it.

`Brush.Operation` is not a node field at all. It is a one-byte enum on the immutable `Brush` value (`docs/negative-brushes.md`).

One proposal is still open. `docs/networking.md` §6.2 describes `RemoteEvent`, `UnreliableRemoteEvent` and `RemoteFunction` as node payloads, which would be three more fields if taken literally. The choice there is one field with a kind enum, a side table, or three accepted fields, and it must be settled before `N15`. Until it is, this page counts none of them.

### `MeshRenderer` — `SceneNode.cs:82`

`MeshRenderer` (`Scene/MeshRenderer.cs:10`) is a `Mesh` + `Material` pair, both read-only after construction (`:18`, `:20`). GPU lifetime belongs to the renderer that created the mesh, never to the component. `Mesh` (`Graphics/Mesh.cs:14`) also keeps a CPU-side copy — `Positions` (`:19`), `Normals` (`:22`), `Indices` (`:25`), `LocalBounds` (`:28`) — so debug drawing and raycasts need no GPU round trip. `Material` (`Graphics/Material.cs:47`) holds an optional `ShaderProgram` plus typed parameter and texture-slot dictionaries.

The setter is reference-compared and, on change, calls `Owner?.OnNodeSpatialComponentChanged` (`SceneNode.cs:92`): the node just became spatial, stopped being spatial, or changed its bounds.

### `Brush` — `SceneNode.cs:116`

**A brush is not a mesh. It is a convex solid defined as an intersection of half-spaces** — one outward-facing plane per face (`Bsp/Brush.cs:20`). Planes and clipped face polygons live in the brush's *local* frame; the node's world transform places it.

Why half-spaces rather than triangles:

1. **CSG is exact and cheap on planes.** Carving one solid out of another is plane classification and polygon splitting (`Polygon.Classify` `Polygon.cs:94`, `Polygon.Split` `:153`), not mesh boolean surgery.
2. **Precision is position-independent.** Splits run in each brush's own local frame, so a brush 10 km from the origin has the same floating-point accuracy as one at the origin (`Brush.cs:13`). This is the numerical half of the open-world pillar.
3. **Resize is a plane edit, not a node scale.** `Brush.WithScaledExtents` (`Brush.cs:308`) maps each half-space exactly under a diagonal map — `n' = normalize(S⁻¹n)`, `d' = d / ‖S⁻¹n‖` — so wedges and cut corners come through correctly, and node transforms stay rigid.
4. **The solid is validated at construction.** Duplicate same-facing planes are rejected (`Brush.cs:350`), and a two-seed probe rejects unbounded plane sets (`Brush.cs:372`).

Brush surface: `Transform` (`:128`, for standalone use only — node-attached brushes ignore it), `LocalPlanes` (`:131`), `LocalFaces` (`:140`), `FaceSurfaces` (`:148`), `LocalBounds` (`:151`), `WorldBounds` (`:154`), `CreateBox` (`:170`, `:184`), `WithFaceMaterial` (`:231`), `WithFaceSurface` (`:246`).

**A `Brush` is immutable after construction.** Every mutator returns a new instance, and that is load-bearing: `Brush` reference identity is the carve cache's validity key (`Brush.cs:219`), so swapping the successor onto the node is precisely what invalidates that brush's cached carve — and only that brush's.

**`Operation` says whether a brush adds solid or removes it** (`Bsp/BrushOperation.cs`, `docs/negative-brushes.md`). It is `Additive` or `Subtractive`, and `WithOperation` returns the successor. The compiled world is the union of the additive brushes minus the union of the subtractive ones, with no order between brushes. A subtractive brush removes its solid from every additive brush it overlaps, and the cavity walls wear its face materials. A `.smap` saves it as `operation` inside the brush, and writes it only when it is subtractive.

`Operation` lives on the `Brush` value and not on the node. The compile caches and the incremental compiler tell a changed brush by `Brush` reference identity and the placement matrix. A flag kept anywhere else could flip without a new `Brush` reference, and the old geometry would keep drawing. `WithAllFacesMaterial`, `WithFaceSurface` and `WithScaledExtents` all pass `Operation` on, or retexturing or resizing a hole would turn it into a solid block.

`Operation` and `BrushKind` are independent. A subtractive part carves nothing and draws nothing, and `Scene.InertPartBrushCount` counts those so the mistake can be reported.

**The brush setter does three things** (`SceneNode.cs`). It keeps both subtree counts right on the ancestor chain when a brush is attached or detached. It dirties the static world: `MarkStructuralWorldDirty` for an attach or detach, since placement slots shift, and `MarkBrushSubtreeDirty` for a brush-for-brush swap. And it notifies the spatial index.

The first two depend on `BrushKind`. Attaching, swapping or detaching a `Part` brush leaves the static-world count alone and dirties nothing. The third does not: `OnNodeSpatialComponentChanged` runs for both kinds, because parts are culled and picked through the BVH too. The compiled world does not see part brushes, and the spatial index sees every brush.

### `BrushKind`

`SceneNode.BrushKind` (`Scene/BrushKind.cs`) says what the node's brush is. A `World` brush is carved against its neighbours and fused into the compiled static world. A `Part` brush stands alone with its own mesh and is never carved. The default is `World`. The kind is not inherited, so a reparent cannot change what is carved. `IsStaticWorldBrush` is true for a node that has a brush of kind `World`.

Moving a part costs no recompile, so anything that moves while a level plays must be a part. `EntityWorld.SetLocalTransform` refuses to move a node with a world brush at or below it.

Changing the kind of a node that has a brush adjusts the static-world count and calls `Scene.MarkAdmissionChanged`, which dirties the static world and moves the node into or out of the part list. A part's mesh comes from `PartBrushMeshCache`.

In the editor the change is a `SetBrushKindCommand`. Turning a block into a part also runs `BrushKindConversion.BakeFaceAxes`, which gives each world-aligned face stored axes so its texture does not jump. Converting back does not make those faces world-aligned again. Undo does.

A `.smap` saves the kind as `kind`, and writes it only for a part.

### `FaceSurface` — the per-face payload

Each brush plane carries a `FaceSurface` (`Bsp/FaceSurface.cs:86`), index-aligned with `LocalPlanes`: a `MaterialRef` (`:136`) plus Hammer-style texture axes — `UAxis`/`VAxis` (`:142`, `:148`), `UOffset`/`VOffset` (`:151`, `:154`), `UScale`/`VScale` (`:157`, `:160`).

The UV convention (`FaceSurface.cs:22`) is `u = dot(p, UAxis)/UScale + UOffset`. Scales are **world units per repeat**; offsets are in **repeats**, added after the division; rotation is not stored separately — it is expressed by rotating the axes about the face normal, so there is no "rotate then scale" ambiguity.

**Zero axes mean world-aligned** (`IsWorldAligned` `:168`): the axes are derived at UV time from the face's world normal by the dominant-axis rule (`ResolveAxes` `:239`). That is Hammer's default and is bit-identical to the projection the engine used before per-face surfaces existed, so no existing geometry's UVs moved. Explicit axes get full **texture lock** through `Transformed` (`:206`): the axes rotate with the brush and the translation folds into the offsets, so a dragged brush keeps its texture glued to the surface.

What this buys: per-face materials with no per-face asset object anywhere. **The payload is a pure value** — floats and an interned id, no GPU handle, no asset-manager state — which is exactly what lets it ride through the entire background compile and become a real material only at upload time.

### `MaterialRef` — the interning seam

`MaterialRef` (`Assets/MaterialRef.cs:26`) is a 4-byte id (`:38`); id 0 is the engine default (`:29`, `:41`). `MaterialRegistry` (`:82`) is a process-wide, **append-only**, lock-guarded intern table: `Intern(path)` (`:110`) and `TryGetPath` (`:134`), keyed case-insensitively with backslashes folded. Ids are never reused or revoked, which is what makes one safe to hold inside an immutable CSG artifact for the life of the process. Resolution to a real `Material` happens once, on the render thread, through `AssetManager.ResolveMaterial` (`Assets/AssetManager.cs:574`), which degrades to `DefaultMaterial` (`:220`) rather than throwing.

### `Light`

`SceneNode.Light` (`Scene/Light.cs`) makes the node a light: directional, point, spot, rect or disc. The node supplies the position and the direction. The scene lists lit nodes in `Scene.LightNodes`. A light does not put its node in the BVH. A `.smap` saves it as `light`.

### `Entity`

`SceneNode.Entity` is an `EntityData` (`Entities/EntityData.cs`): a class name, keyvalues as strings, and the connections leaving the entity's outputs. The class is a name and not a schema reference, so a map that names a class this build lacks still loads and saves. Keyvalues are a list and not a dictionary, so their order and any duplicate keys survive a save. The scene lists entity nodes in `Scene.EntityNodes`. A `.smap` saves the payload as `entity`, with `class`, `keys` and `outputs`.

The name other entities fire at is the node's name. There is no separate target name field.

What sits on the node is authored data. While a level plays, `EntityWorld` (`Entities/EntityWorld.cs`) builds one live `Entity` per entity node from the `EntityCatalog`, or a `PlaceholderEntity` for a class it does not know. The live entity gets its own copy of the connections and never writes back to the node. `EntityWorld.SetLocalTransform` is the only way a running entity moves a node, and `Deactivate` puts every moved node back.

`PlaySession` runs the entity tick first, then physics, then the character.

### Node flags

Four switches on the node say how it behaves. They are not payloads, and each applies to its own node only.

| Member | Default | What it does |
| --- | --- | --- |
| `CanCollide` | on | The node's geometry takes part in collision. |
| `CanQuery` | on | Ray and overlap queries see the node. A default `SceneQueryFilter` requires it. |
| `CanTouch` | on | The node raises touch events. |
| `IsRendered` | on | The node's own mesh or part brush is drawn and casts a shadow. It does nothing on a world brush, which the static world draws. |

The first three are bits in `PhysicsFlags` (`Scene/PhysicsFlags.cs`), one byte on the node. The byte also holds `Anchored`, `Massless` and `HasBody`. `HasBody` belongs to the physics layer, and nothing sets it yet. `IsRendered` is a separate bool.

A `.smap` saves the four as `collide`, `query`, `touch` and `render`, each written only when it is off. A `.scmap` node record stores them inverted, as `NoCollide`, `NoQuery`, `NoTouch` and `NoRender` bits, so a record with none set has the defaults. `Anchored`, `Massless` and the collision group are not saved.

The editor writes the flags with `SetNodeFlagsCommand`.

---

## 4. Derived data: brushes to pixels

Authored truth is exactly this: the set of brush nodes, their brushes, and their nodes' world transforms. **Everything in this section is recomputed and disposable.**

### 4.1 The snapshot

`Scene.SnapshotBrushPlacements` (`Scene.Placements.cs`) captures one `BrushPlacement` (`Bsp/BrushPlacement.cs`) per static-world brush node: the brush reference plus the node's world matrix at that instant. Both paths test `IsStaticWorldBrush`, so a part brush is never captured. The **full walk** visits every node. It runs for the first snapshot and after `MarkStaticWorldDirty`. The **fast path** revisits only the nodes that reported an edit and the world brushes under a dirty subtree. Rigidity is validated on both paths, and a defective snapshot is rejected whole.

### 4.2 The compile chain

Run entirely on a thread-pool thread by `Scene.CompileStaticWorld` (`Scene.cs:1011`), reading only the snapshot and the immutable previous world:

| Stage | What it produces | Where |
| --- | --- | --- |
| **Carve** | Per-brush visible exterior surfaces — each brush's faces minus every overlapping brush's volume | `Csg.CarvePerBrush`, called at `CsgWorld.cs:467` |
| **Bucket** | The sparse chunk partition: each placement into the cells its weld-band-inflated AABB touches | `ChunkGrid.Build` (`ChunkGrid.cs:167`), called at `CsgWorld.cs:527` |
| **Snap + weld** | Per-cell vertex snapping to a grid and T-junction welding | `ChunkWelder.Weld`, `CsgWorld.cs:534` |
| **Per-cell BSP** | One solid-leaf tree per cell over its residents' welded surfaces, cut to the cell | `ChunkBspBuilder.Build`, `CsgWorld.cs:544` |
| **Per-cell mesh** | One `ChunkMesh` per geometry-owning cell, split into one `ChunkSubmesh` per face material | `ChunkMeshBuilder.Build`, `CsgWorld.cs:553` |
| **GPU upload** | One `Mesh` per (chunk, material), materials resolved here | `Scene.CreateChunkSubmeshes` (`Scene.cs:1192`) — **render thread only** |

### 4.3 The compiled artifacts

| Type | Where | Holds |
| --- | --- | --- |
| `CsgWorld` | `Bsp/CsgWorld.cs:16` | `Placements` (`:138`), `Surfaces` (`:148`, lazily materialized), `SurfaceCount` (`:162`), `Chunks` (`:180`), `ChunkMeshes` (`:191`), `DirtyCells` (`:212`) |
| `ChunkCoord` | `Bsp/ChunkCoord.cs:14` | Unbounded integer cell, `CellSize` pinned at 32 (`:25`), lexicographic X→Y→Z canonical order (`:51`) |
| `ChunkGrid` | `Bsp/ChunkGrid.cs:23` | Dictionary-keyed sparse cells; `WeldBand` (`:37`), `OwnerCell` (`:131`), `ComputeFootprint` (`:141`) |
| `WorldChunk` | `Bsp/WorldChunk.cs:30` | `OwnedBrushIndices` (`:47`), `ResidentBrushIndices` (`:54`), `Surfaces` (`:61`), `WeldedSurfaces` (`:72`), `Bsp` (`:85`) |
| `BspTree` / `BspNode` | `Bsp/BspTree.cs:14` / `BspNode.cs:10` | Solid-leaf tree; `IsLeaf`/`IsSolid`/`Plane`/`Front`/`Back` (`BspNode.cs:26`–`:38`) |
| `ChunkMesh` | `Bsp/ChunkMesh.cs:75` | `Coord` (`:85`), `Submeshes` (`:95`), `RenderBounds` (`:107`) |
| `ChunkSubmesh` | `Bsp/ChunkMesh.cs:39` | `MaterialRef` + self-contained zero-based vertex/index arrays |
| `Polygon` | `Bsp/Polygon.cs:13` | Vertices, `Surface` plane (`:77`), `Face` payload (`:85`), `Bounds` (`:91`), `Epsilon = 1e-4f` (`:16`) |

**The BSP is a query structure only** (`CsgWorld.ContainsPoint` `:603`, `Raycast` `:617`) — never the render path. Point queries route to the containing cell; rays walk occupied cells front-to-back by 3D-DDA.

### 4.4 What is a cache and can be thrown away

All of it. Specifically:

- `Scene.StaticWorld` (`Scene.cs:335`) is null until the first compile lands, and null when the scene has no brush nodes.
- The GPU side — `_staticWorldChunkMeshes` / `_staticWorldChunkList` (`Scene.cs:343`), exposed as `StaticWorldChunkMeshes` (`:355`) — is rebuilt or spliced per swap in `ReplaceStaticWorld` (`:1041`) and `ApplyChunkMeshDelta` (`:1127`), create-before-destroy so a `CreateMesh` throw leaves the last good world intact and renderable.
- The four validation caches — `CompileCache` (`CsgWorld.cs:224`), `WeldCache` (`:254`), `BspCache` (`:301`), `MeshCache` (`:338`) — are pure accelerators. Dropping them costs a full recompile, never correctness. The synchronous `RebuildStaticWorld` (`Scene.cs:759`) is deliberately cache-free and always compiles fresh.
- `CsgWorldCarry` (`CsgWorld.cs:108`) is the incremental compiler's paged copy-on-write input; a faulted compile restores it from the published world (`Scene.cs:866`) and re-covers the dropped dirty cells.
- `_lastSnapshotFootprints` (`Scene.cs:495`) and `_pendingDirtyCells` (`:500`) are the dirty-cell tracker. The dirtying rule: an unchanged brush reference *and* placement matrix dirties nothing; any change dirties the **union of old and new footprints**, because a brush leaving a cell strands stale geometry there.

**Authored vs derived, stated once:** brush nodes and their transforms are authored. `CsgWorld`, `ChunkGrid`, `WorldChunk`, `BspTree`, `ChunkMesh`, every GPU buffer, the BVH and the `RenderView` are derived.

### 4.5 The frame view

`RenderView` (`Graphics/RenderView.cs:39`) is the per-frame draw list: `Items` (`:48`) for mesh nodes and `WorldItems` (`:60`) for static-world chunks, each entry a `RenderItem` (`:15`) of mesh + optional material + world matrix. It is engine-owned and reused; `Clear` (`:99`) keeps capacity so steady-state builds allocate nothing. World chunk vertices are already in world space, hence the identity matrix. Culling is **per chunk** against the chunk's true render AABB (`Scene.cs:309`) — owned surfaces overhang their cell — and a surviving chunk emits one item per material it wears.

---

## 5. Editing: how changes are expressed

| Concept | Where | Notes |
| --- | --- | --- |
| `IEditorCommand` | `Editing/Commands/IEditorCommand.cs:31` | `Name`, `Do(Scene)`, `Undo(Scene)`, `RollBack(Scene)` |
| `ICoalescingCommand` | `Commands/ICoalescingCommand.cs:21` | `TryAbsorb` — a whole drag collapses to one entry |
| `SetTransformCommand` | `Commands/SetTransformCommand.cs:25` | Absolute before/after position + rotation |
| `SetLocalTransformCommand` | `Commands/SetLocalTransformCommand.cs:18` | Absolute before/after full `Transform` |
| `SetBrushCommand` | `Commands/SetBrushCommand.cs:32` | Brush swap — retexture and resize ride this |
| `CompositeCommand` | `Commands/CompositeCommand.cs:18` | Undo runs children in reverse |
| `UndoStack` | `Undo/UndoStack.cs:41` | Bounded ring (`:51`), default 256 (`:44`); transactions at `:184`/`:205`/`:242` |

Two rules make this work, both stated at `IEditorCommand.cs:11` and `:19`:

- **Address by `Guid`, never by reference**, and treat a lookup miss as a no-op rather than an error — a node may legitimately be absent while a stretch of history sits undone.
- **Capture absolute before/after values, never deltas or scene snapshots.** Absolute values are idempotent, which is concretely free here: the transform setters early-out on exact equality and the CSG caches key on exact values, so replaying a value the scene already has costs a comparison and dirties nothing. Deltas would drift under coalescing; snapshots would make every edit O(scene).

Every command's `NodeId` is a `Guid` (`SetTransformCommand.cs:87`, `SetLocalTransformCommand.cs:44`, `SetBrushCommand.cs:57`).

---

## 6. What the designs add: all PLANNED, none in the tree

Nothing in this section exists in the code. Each row names the document that owns it. What is built is in §3: `BrushKind`, `Brush.Operation`, the light and entity payloads and the node flags.

| Concept | What it is | Why | Owner |
| --- | --- | --- | --- |
| `NodeRealm` — `Inherit \| Shared \| Server \| Client` | A declared, inherited byte on `SceneNode` saying **who holds this node**. | Deletes Roblox's container zoo: audience becomes data on the node, so the tree can be organised around the game instead of around replication. **Name is LOCKED to `Realm`.** | `docs/realms.md` §2.2 |
| `NodeState` — `Inherit \| Active \| Dormant` | Whether the node is **live**: carved, in the BVH, drawn, queried, ticking. | There is no way to make a brush inert today — `SnapshotFullWalk` admits every brush unconditionally (`Scene.cs:1349`) and `IsSpatial` is ancestry-blind (`SceneBvh.cs:146`) — so a parked template would carve the world. Ship both axes or neither. | `docs/realms.md` §2.1–2.2 |
| `RealmSet` — `[Flags] Inert \| Server \| Client \| Shared` | The **resolved** value, and it is a *set*, not an enum value. | Resolution is an intersection: `ToSet(declared) & parentEffective`. `Server ∩ Client` is empty, and the empty set has a name — `Inert` — rather than being promoted to the ancestor's realm, which would turn a dragged client HUD subtree into server code holding authority. | `docs/realms.md` §2.3, §3 R1–R3 |
| `SceneNode.EffectiveRealm` / `IsLive` / `IsVisibleTo(mask)` | Cached resolved fields, maintained on declaration change and reparent only — never on the transform path. | `IsVisibleTo` is one AND and one compare: the entire enforcement primitive. | `docs/realms.md` §2.4 |
| `Script` payload | A **node** with a `Script` payload — so `script.Parent` is literally the parent node — carrying **one axis, `bool IsModule`**, plus `Disabled`. | Matches Roblox character-for-character, and falls out free: a script-only node carries no `MeshRenderer` or `Brush`, so the BVH and static world ignore it automatically. **There is no `ScriptKind.Server`/`.Client`:** run location is the *node's* `Realm`, so a client script is a node declared `Realm = "Client"` (`realms.md` §6.2 spends `O8`'s reservation; `networking.md` `N15` records the same correction). | `docs/roblox-onboarding.md` O8, corrected by `docs/realms.md` §6.2 |
| Attributes | A closed `AttributeValue` readonly-struct union (Bool, Int, Double, String, Vector2, Vector3, Color3, CFrame, NumberRange…) **plus a `NodeRef` wrapping the node's `Guid`**, in a lazily allocated per-node dictionary. | **One** keyvalue mechanism serves both Roblox attributes and Source entity keyvalues — they are the same feature. `NodeRef` is the addition Roblox lacks and the entity wiring is unusable without it. | `docs/roblox-onboarding.md` O3 |
| Tags | Per-node lazy tag list, a `Scene` reverse index, `GetTagged`, `TagAdded`/`TagRemoved`, and `ObserveTag` which **replays already-tagged nodes before connecting**. | The `CollectionService` equivalent, minus the `GetTagged`-loop-beside-every-connect boilerplate. The reverse index must take an audience mask or it enumerates content the caller cannot see. | `docs/roblox-onboarding.md` O3; `docs/roblox-to-spectra.md` (Instance API) |
| `NetId` — packed `(index, generation)` `uint` | A **second, session-scoped** identity for gameplay replication. 20 index bits, generation above. | 16-byte Guids in every gameplay packet cost 64 KB/s per client in identifiers alone at 200 objects / 20 Hz. Authored nodes derive their index from the `.scmap` pre-order position, so both sides agree with zero spawn traffic. | `docs/networking.md` §3.3 |
| Physics bodies, kept out of the payload list | A side table in the physics world keyed by `SceneNode.Id`, holding the body handle and probed only when the node's `HasBody` bit is set. The `PhysicsFlags` byte that bit lives in is built (§3). The side table and dynamic bodies are not. | A body then costs the node one bit and no reference, which is the payload-count cost this page tracks. Rollback (`Y13`) and lag compensation (`Y16`) stay deferred. The per-instance state history ring is `networking.md`'s (§4.4). | `docs/physics.md` §2.6, `Y6` |

Also planned and adjacent to the model, named so they are not mistaken for existing: the `PrefabInstance` payload (`ROADMAP.md` P10), a typed `Scene.Environment` (`roblox-to-spectra.md`, the `Lighting` row), and cvars as the settings surface (`docs/console.md` §5).

---

## 7. Identity and references

**One identity today, two in the design.**

### `Guid` is the universal reference

`SceneNode.Id` is the only identity the engine has, and everything that must survive time uses it.

**Why commands address by `Guid` rather than by object reference.** Undo of a delete cannot resurrect the original object — it constructs a new `SceneNode` under the old id (`SceneNode.cs:44`). Any object reference a command captured would point at a corpse. So commands store the id and resolve through `Scene.TryFindById` at execution time, and a miss is a no-op because a node may legitimately be absent while history sits undone.

**How references survive undo.** The id outlives the instance; `_nodesById` is rebuilt from `NodeAdded`/`NodeRemoved`, so a resurrected node re-registers under the same key and every recorded command behind the delete still resolves.

**How references will survive save/load.** The `Guid` is written verbatim: `.smap` as lowercase `"D"`-format hex (`formats-and-pipeline.md` §2.6), `.scmap` as 16 big-endian RFC 4122 bytes at offset 0 of the `NODE` record (`formats-and-pipeline.md` §2.7), so the binary bytes match the text spelling character-for-character. A node record is kept for **every** authored node, including brushes whose geometry dissolved into chunks, because the id is what entity I/O, undo, prefab overrides and Luau lookups all resolve through (`formats-and-pipeline.md` §2.7, "Keep a node record for every authored node").

One consequence already written down: instantiating a project template must **re-GUID every node in every `.smap`** (`realms.md`, "How a template ships, and why the engine never learns one existed"), or every project made from that template shares node ids.

### Where a second identity appears, and why (PLANNED)

`NetId` (`networking.md` §3.3) exists only because gameplay packets cannot afford 16 bytes of identity per object per tick. Two identity spaces, on purpose (`networking.md` §3.3): **collaborative editing addresses nodes by `Guid`** — edits are human-rate, and an edit legitimately names a node the receiver has not created yet — while **gameplay addresses objects by `NetId`**, with the `Guid` riding along exactly once in the interest-enter message.

**NetIds are allocated only to `Shared`-realm nodes** (`networking.md` §3.3), by ordinal in pre-order *restricted to the `Shared` subset*. Three reasons, each a bug if written otherwise: a `Server`- or `Client`-realm node never replicates, so it has no baseline, no delta and no id to burn; restricting the ordinal makes a client-target and a server-target cook of one map number identically, where an unrestricted ordinal would renumber everything after the first stripped record; and per-player content is therefore **not** a fourth audience — it is a per-client interest filter over `Shared` nodes, which is what `NodeRealm.Owner` was deleted for (`realms.md` §2.2).

**`MaterialRef.Id` is a third id and it must never leave the process** — not to disk (`formats-and-pipeline.md` §2.7's `ASTB`), not onto the wire (`networking.md` §3.3). It is an index into a per-process append-only table; peer A interning `wall` first and peer B interning `floor` first silently mis-textures the world. Materials travel as a path or a session-table index.

---

## 8. Summary table

| Concept | Exists today | Where it lives | Purpose |
| --- | --- | --- | --- |
| `SceneNode` | **Yes** | `Scene/SceneNode.cs` | The single spine: name, transform, hierarchy, optional payloads |
| `SceneNode.Id` (`Guid`) | **Yes** | `Scene/SceneNode.cs` | Universal, lifetime-stable reference |
| `SceneNode(name, id)` | **Yes** | `Scene/SceneNode.cs` | Resurrect a node under its old identity |
| `Transform` (pos/rot/scale) | **Yes** | `Scene/Transform.cs` | Local pose; `Model` composes S·R·T |
| `WorldMatrix` (cached) | **Yes** | `Scene/SceneNode.cs` | Derived world pose; eagerly invalidated down the subtree |
| `CFrame` (pos/rot, no scale) | **Yes** | `Scene/CFrame.cs` | The transform scripts use, with Roblox's meaning for every operation |
| `LocalCFrame` / `CFrame` on a node | **Yes** | `Scene/SceneNode.cs`, `Scene/WorldFrame.cs` | Local and world frame, both settable; scale is kept |
| Transform equality early-out | **Yes** | `Scene/SceneNode.cs` | A no-op write dirties nothing and raises nothing |
| `SubtreeBrushCount` / `SubtreeStaticWorldBrushCount` | **Yes** | `Scene/SceneNode.cs` | O(1) "does this subtree hold a brush" and "does this edit touch the static world". Two counts, one writer |
| `Scene` | **Yes** | `Scene/Scene.cs` and the `Scene.*.cs` partials | Owns root, camera, selection, BVH, id index, derived world |
| `Scene.TryFindById` | **Yes** | `Scene/Scene.cs` | Guid → live node, O(1) |
| `NodeAdded`/`NodeRemoved`/`NodeTransformChanged` | **Yes** | `Scene/Scene.cs` | Change notification; handlers must not mutate the graph |
| `SelectionSet` | **Yes** | `Scene/SelectionSet.cs` | Editor selection, auto-deselects on removal |
| `SceneBvh` + `SceneRaycastHit` | **Yes** | `Scene/SceneBvh.cs` | Dynamic AABB tree over spatial nodes |
| `Camera`, `Ray3`, `Frustum`, `Aabb` | **Yes** | `Scene/Camera.cs`, `Scene/Ray3.cs`, `Scene/Frustum.cs`, `Bsp/Aabb.cs` | View, picking and culling primitives |
| `MeshRenderer` payload | **Yes** | `Scene/SceneNode.cs`; type in `Scene/MeshRenderer.cs` | Draw this node with this mesh + material |
| `Mesh` (GPU + CPU copy) | **Yes** | `Graphics/Mesh.cs` | Renderer-owned geometry with CPU positions/indices/bounds |
| `Material` | **Yes** | `Graphics/Material.cs` | Shader + typed parameters + texture slots |
| `Brush` payload | **Yes** | `Scene/SceneNode.cs`; type in `Bsp/Brush.cs` | Convex solid as half-spaces; the authoring primitive |
| `Brush.WithScaledExtents` | **Yes** | `Bsp/Brush.cs` | Resize by editing plane offsets, never node scale |
| `Brush.Operation` (`Additive`/`Subtractive`) + `Brush.WithOperation` | **Yes** | `Bsp/Brush.cs`; enum in `Bsp/BrushOperation.cs` | On the brush value, not the node, and not a payload: does this brush add solid or remove it. Independent of `BrushKind` |
| `BrushKind` (`World`/`Part`) + `IsStaticWorldBrush` | **Yes** | `Scene/SceneNode.cs`; enum in `Scene/BrushKind.cs` | Declared per node, not inherited, not a payload: is this brush fused into the compiled static world, or does it stand alone as a part |
| `Scene.MarkAdmissionChanged` | **Yes** | `Scene/Scene.cs` | Runs when a brush changes kind: dirties the static world and updates the part list |
| `SetBrushKindCommand` + `BrushKindConversion` | **Yes** | `Editing/Commands/SetBrushKindCommand.cs`, `Editing/Commands/BrushKindConversion.cs` | World to part and back as an undoable command. Making a part bakes the axes of world-aligned faces, and converting back does not undo that |
| `FaceSurface` | **Yes** | `Bsp/FaceSurface.cs` | Per-face material + Hammer texture axes, as a pure value |
| `MaterialRef` / `MaterialRegistry` | **Yes** | `Assets/MaterialRef.cs` | Interned material id safe to carry through a background compile |
| `AssetManager` | **Yes** | `Assets/AssetManager.cs` and the `AssetManager.*.cs` partials | Typed caches; sync `Load*` vs async `Request*`; owns every GPU resource |
| `Light` payload | **Yes** | `Scene/SceneNode.cs`; type in `Scene/Light.cs` | Makes the node a light: directional, point, spot, rect or disc |
| `Entity` payload | **Yes** | `Scene/SceneNode.cs`; type in `Entities/EntityData.cs` | Class name, keyvalues and output connections, authored on the node. The target name is `SceneNode.Name` |
| `EntityWorld` / `Entity` / `EntityCatalog` | **Yes** | `Entities/EntityWorld.cs`, `Entities/Entity.cs`, `Entities/EntityCatalog.cs` | The entity runtime: builds live entities from the authored data while a level plays, and never writes back |
| Node flags `CanCollide`, `CanQuery`, `CanTouch` | **Yes** | `Scene/SceneNode.cs`; bits in `Scene/PhysicsFlags.cs` | Whether the node collides, is seen by queries and raises touch events. Not payloads. Saved in `.smap` as `collide`, `query`, `touch` |
| Node flag `IsRendered` | **Yes** | `Scene/SceneNode.cs` | Whether the node's own mesh or part brush is drawn. Not a payload. Saved in `.smap` as `render` |
| `SetNodeFlagsCommand`, `SetEntityCommand`, `SetEntityKeyvalueCommand`, `SetEntityConnectionsCommand`, `SetLightCommand` | **Yes** | `Editing/Commands/` | Undoable edits of the flags, the entity payload and the light |
| `BrushPlacement` | **Yes** | `Bsp/BrushPlacement.cs` | Brush + captured world matrix: the snapshot unit |
| Rigidity validation | **Yes** | `Scene/Scene.cs`, `Scene/Scene.Placements.cs` | Rejects scale/shear/mirror/NaN brush placements at the snapshot |
| `CsgWorld` | **Yes** | `Bsp/CsgWorld.cs` | The compiled static world (derived, never authored) |
| `ChunkCoord` / `ChunkGrid` / `WorldChunk` | **Yes** | `Bsp/ChunkCoord.cs`, `Bsp/ChunkGrid.cs`, `Bsp/WorldChunk.cs` | Sparse unbounded 32-unit cell partition |
| `BspTree` / `BspNode` | **Yes** | `Bsp/BspTree.cs`, `Bsp/BspNode.cs` | Per-cell solid-leaf tree. **Queries only**, never rendering |
| `ChunkMesh` / `ChunkSubmesh` | **Yes** | `Bsp/ChunkMesh.cs` | Per-cell render arrays, split per material |
| Incremental compile caches | **Yes** | `Bsp/CsgCompileCache.cs`, `Bsp/CsgWeldCache.cs`, `Bsp/CsgBspCache.cs`, `Bsp/CsgMeshCache.cs` | Pure accelerators; disposable without loss |
| `RenderView` / `RenderItem` | **Yes** | `Graphics/RenderView.cs` | Reusable per-frame draw list |
| `.smap` / `.scmap` node records | **Yes** | `Maps/MapDocument.cs`, `Maps/Compiled/ScmapRecords.cs` | Authored JSON with no derived data; baked binary with 80-byte `NODE` records |
| `IEditorCommand` | **Yes** | `Editing/Commands/IEditorCommand.cs` | The only sanctioned way to change a scene |
| `ICoalescingCommand` | **Yes** | `Editing/Commands/ICoalescingCommand.cs` | One drag → one undo entry |
| `SetTransformCommand` / `SetLocalTransformCommand` / `SetBrushCommand` / `CompositeCommand` | **Yes** | `Editing/Commands/` | Absolute-value, Guid-addressed edits |
| `UndoStack` | **Yes** | `Editing/Undo/UndoStack.cs` | Bounded ring history with gesture transactions |
| `NodeRealm` (audience) | **No** | `docs/realms.md` §2.2 | Declared, inherited "who holds this node" |
| `NodeState` (liveness) | **No** | `docs/realms.md` §2.2 | Declared, inherited "is this node live" |
| `RealmSet` / `EffectiveRealm` / `IsLive` | **No** | `docs/realms.md` §2.3–2.4 | Set-valued resolution; the empty set is `Inert` |
| `Script` payload | **No** | `docs/roblox-onboarding.md` O8 | A script is a node, so `script.Parent` is the parent node |
| Attributes (`AttributeValue`, `NodeRef`) | **No** | `docs/roblox-onboarding.md` O3 | One typed keyvalue bag for both Roblox attributes and entity keyvalues |
| Tags + reverse index + `ObserveTag` | **No** | `docs/roblox-onboarding.md` O3 | `CollectionService` equivalent with the boilerplate removed |
| `NetId` | **No** | `docs/networking.md` §3.3 | Session-scoped 4-byte gameplay identity, `Shared` nodes only |
| Physics-world side table for bodies | **No** | `docs/physics.md` §2.6 | Designed, and not a payload: the `HasBody` bit on the node, the body handle in a side table |
| `PrefabInstance` payload | **No** | `ROADMAP.md` P10 | `{prefab, seed, overrides}` with derived child GUIDs |
| `Scene.Environment` | **No** | `docs/roblox-to-spectra.md`, the `Lighting` row | Global settings as a typed struct, not a node |
| Signals (`Signal<T>`, `Destroying`) | **No** | `docs/roblox-to-spectra.md`, the Instance API table | Per-node events; scene-level events exist, per-node ones do not |
| Settable `Parent`, `Destroy`, `FindFirstChild`, `IsA` | **No** | `docs/roblox-to-spectra.md`, the Instance API table | The familiarity API. `AddChild`, `InsertChild`, `RemoveChild`, `Clone` and `Traverse` exist |
