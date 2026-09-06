# Performance review — 5 September 2026

Reviewed revision: `e3a2280d75a3d0e0012c1252ea7f5ef5005a1614`.

Spectra has a strong incremental geometry path and working draw instancing. The largest remaining problems are content scaling and frame consistency: part-node attachment becomes quadratic, invisible drawables still cost a full scan per view, structural brush edits lose the fast compile path, and resource uploads can occupy an unrestricted portion of a frame. D3D12 also serializes CPU and GPU frame execution.

The original findings and measurements below describe the reviewed revision. The [implementation record](#implementation-record--5-september-2026) documents all fourteen fixes, completed verification and measured tradeoffs. **Measured** means a run performed for this review. **Code-confirmed** means the mechanism was present in the reviewed source, but its cost had not been isolated experimentally. Proposed savings in the historical findings are not benchmark results.

## Evidence and limits

- Machine: Intel Core i9-13900K, 24 cores / 32 logical processors, approximately 31.8 GiB usable physical RAM, Windows 10.0.26200. NVIDIA RTX 4070 Ti driver `32.0.16.1074`; Intel UHD 770 also installed, driver `32.0.101.6129`.
- .NET SDK `10.0.201`, runtime `10.0.5`, Release **JIT** builds. These are not NativeAOT results. The two headless benchmarks disable tiered compilation; CsgBench reports workstation GC.
- GPU demos ran separately, for approximately 22 seconds each, using the deferred pipeline and shadows. Performance runs disabled validation and the editing self-test. A separate run enabled both validation and the self-test.
- The default demo still animates a world brush with `--selftest=false`. Therefore these GPU runs include ongoing CSG, mesh replacement and collision synchronization. They are not idle-scene baselines.
- Frame logs report smoothed wall-clock frame time and CPU phase durations, not GPU timestamps or frame-time percentiles. No shader speedup, integrated-GPU budget, or backend throughput ranking can be inferred from them.
- Source inspection concentrated on scene membership/culling, CSG, renderer submission/resource lifetime, assets/cooking, character collision, and engine-to-editor publication. OpenGL, an active editor UI, cooked-game startup, and large gameplay/physics workloads were not timed.

### Measurements made here

| Workload | Result | Interpretation |
|---|---|---|
| CSG: move one isolated brush, 1k / 10k / 50k brushes | **0.12 / 0.09 / 0.11 ms** | Good scaling for the trusted incremental path. |
| CSG: add one isolated brush, same worlds | **0.86 / 20.61 / 175.34 ms** | Structural edits remain expensive; these are CPU compile times, not main-thread frame stalls. |
| Initial CSG, 50k brushes | **2,999 ms**, **700.8 MiB allocated** | Loading/cooking cost, not resident memory or steady-frame cost. |
| Dense overlapping grid: one brush moved, 1k brushes | Full **10.33 ms**, cached **10.69 ms**; allocations **10.8 → 4.8 MiB** | Caching reduces allocations but did not reduce elapsed time in this dense fixture. |
| Scene attachment, 1k / 10k / 50k parts | **1.45 / 77.35 / 2,438.43 ms** | Serious bulk insertion cost; excludes constructing the nodes/brush. |
| Equivalent world-node attachment | **0.87 / 16.89 / 89.29 ms** | Control includes graph attachment and BVH insertion but does not populate the drawable list. |
| BuildRenderView, 1k / 10k / 50k invisible parts | **17.29 / 200.15 / 1,122.44 µs per view**, **0 B/call** | Linear CPU cost remains even when nothing is visible. |
| Default D3D11 demo, 1920×1080 | Post-startup log samples **0.27–0.30 ms/frame**, **712–771 MiB/s allocated**, **31.3 MiB/s on render thread** | Default adapter was used and its name was not logged; interpret as an illustrative local sample. |
| D3D11, RTX 4070 Ti, 8,000 repeated props, 1920×1080 | Post-startup **1.20–1.27 ms/frame**; ViewBuild **0.20–0.21 ms**, Shadows **0.63–0.66 ms** | **2,132 geometry draws and 3,209 shadow draws saved** by instancing. Shadow phase includes culling and batching. |
| D3D12, RTX 4070 Ti, default demo, 1920×1080 | **16.67–16.68 ms/frame**, Present **16.23–16.31 ms** | Presentation-limited on this setup despite no `--vsync`; this does not measure maximum D3D12 throughput. |
| Map bake, 50k open-world brushes | Compile **1,600.94 ms**, serialize **197.52 ms**, output **66.74 MiB** | Serialization was 12.3% of compile time in this separate fixture/run. Do not compare its compile directly to the initial-build row. |

The scene probe puts every shared-brush box behind the default camera. No meshes are uploaded, so it measures scene attachment and rejection, not rendering. A direct `Scene.QueryFrustum` rejects the same fixture at the BVH root; its approximately 0.01 µs result is below useful precision for predicting real workloads. That is evidence that hierarchical rejection is available, not a promised speedup for arbitrary scenes.

Raw evidence: [CSG and bake](data/2026-09-05-csg-benchmark.txt), [scene probe](data/2026-09-05-scene-probe.txt), [D3D11](data/2026-09-05-d3d11.txt), [D3D11 props](data/2026-09-05-d3d11-props8000.txt), [D3D12](data/2026-09-05-d3d12.txt), [editing smoke](data/2026-09-05-d3d11-selftest.txt). The engine labels allocation rates “MB/s” but divides by 1024²; the table uses MiB/s.

## Issues, fixes, and locations

P1 means address in the next performance pass for the affected workload. P2 means a confirmed scaling limitation whose priority depends on content. They are performance priorities, not correctness or security severities.

### 1. P1 — Part-node membership makes bulk attachment quadratic

**Measured.** [Scene.cs:298](../../SpectraEngine.Core/Scene/Scene.cs#L298), `UpdateDrawableMembership`, calls `_drawableNodes.IndexOf(node)` on every membership check. `OnNodeAdded` reaches this through `UpdatePartBrushMembership`. Adding N drawable nodes performs progressively longer searches, even though each newly attached node is absent. The 50k-part attachment took **2.44 seconds**, versus **89 ms** for the world-node control. `OnNodeRemoved` also uses linear removal, and [light membership at line 226](../../SpectraEngine.Core/Scene/Scene.cs#L226) has the same growth pattern for lights.

**Fix:** maintain an identity membership set beside the ordered drawable/light lists so membership checks and successful appends are O(1). For bulk removals, use stable compaction or stable slots with deferred compaction; a set alone does not fix repeated `List.Remove`. Preserve deterministic emission order—unconditional swap-removal changes it.

**Verify:** rerun the supplied attachment probe and add/remove/undo large subtrees. Check stable draw order and membership after component changes and reparenting. Measure teardown separately.

### 2. P1 — Every view scans every drawable, including completely hidden ones

**Measured.** [Scene.cs:725](../../SpectraEngine.Core/Scene/Scene.cs#L725), `CollectVisible`, loops over `_drawableNodes` and does a bounds lookup/frustum test per node. Both the camera and each shadow cascade call it. Fifty thousand invisible parts cost **1.12 ms for one camera view**. Four cascades can repeat the same population scan, although their actual timings and surviving sets will differ.

**Fix:** maintain a drawable-only BVH, or add drawable-subtree metadata to the existing BVH so traversal skips branches containing only world brushes. Do not simply query the current unfiltered tree: that reintroduces world-brush traversal the drawable list was created to avoid. Preserve a stable emission key when materializing the surviving draws.

**Verify:** fixed visible count against growing total count, all-visible scenes, mixed world/part scenes, moving parents, and off-camera shadow casters. Measure the camera and each cascade separately; instancing does not eliminate selection work.

### 3. P1 — Adding/removing world brushes loses the fast CSG path

**Measured.** [CsgIncrementalCompiler.cs:98](../../SpectraEngine.Core/Bsp/CsgIncrementalCompiler.cs#L98) refuses a placement-count change. [Scene.cs:2043](../../SpectraEngine.Core/Scene/Scene.cs#L2043), `SnapshotBrushPlacements`, also requires unchanged graph structure for its retained snapshot. The fallback validates/rebuilds world-scale structures: one added brush went from **0.86 ms at 1k** to **175.34 ms at 50k**, while moving one remained near 0.1 ms.

**Fix:** extend the incremental representation with stable placement identities/slots, insertion/removal deltas, and old/new dirty footprints. An explicit append-only fast path is a smaller first step. Preserve canonical authored ordering separately from storage slots. Count checks and full validation are correctness guards; removing them without replacement is unsafe.

**Verify:** add, remove, duplicate, undo/redo and reparent at 1k/10k/50k, comparing output to a full compile. Include overlapping/subtractive brushes and dirty-cell boundaries. Measure snapshot time, background compile time, and publication latency separately. Existing results prove the move path, not structural editing responsiveness.

### 4. P1 for D3D12 — Every frame waits until the GPU is idle

**Code-confirmed.** [D3D12Renderer.cs:1076](../../SpectraEngine.Core/Graphics/D3D12/D3D12Renderer.cs#L1076), `Present`, unconditionally calls `WaitForGpu` at line 1102, then recycles resources. This prevents frame N+1 command construction from overlapping outstanding frame N execution. Current upload/descriptor rings and mesh retirement deliberately depend on that wait.

**Fix:** introduce two or three frame contexts, each with an allocator, transient upload/descriptor storage, and completion fence. Wait when reusing a context; retire resources against their last-use fence. Update mesh pooling, ring growth, shader replacement, resize, and shared-target ownership together. Deleting the existing wait by itself corrupts resource lifetime.

**Verify:** run the debug layer, resize/fullscreen cycles, shader reload, descriptor-ring growth, and shared-viewport gates. Measure CPU submission and GPU execution separately. The current **60 Hz D3D12 sample is presentation-limited**, so it cannot establish the size of this optimization. [Swap-chain creation at line 535](../../SpectraEngine.Core/Graphics/D3D12/D3D12Renderer.cs#L535) uses FlipDiscard with flags zero; add a capability-checked uncapped measurement path while retaining intentional editor pacing.

### 5. P1 — “Async” asset loading has an unlimited synchronous tail

**Code-confirmed.** [AssetManager.cs:749](../../SpectraEngine.Core/Assets/AssetManager.cs#L749), `PumpPendingUploads`, drains the complete texture queue, then [AssetManager.Models.cs:195](../../SpectraEngine.Core/Assets/AssetManager.Models.cs#L195) drains the complete model queue. Applying a model resolves materials and can call synchronous `LoadTexture` at line 408. [D3D12Renderer.cs:2273](../../SpectraEngine.Core/Graphics/D3D12/D3D12Renderer.cs#L2273) creates a staging resource and waits for GPU completion for each texture upload. A burst of worker completions can become a long render-thread frame.

**Fix:** add a shared per-frame time/byte budget with fair texture/model scheduling and worker/queued-byte backpressure. Request model texture dependencies asynchronously through stable placeholder handles. Batch D3D12 copies and retire staging storage using fences. A queue-item limit alone cannot bound a single huge texture/model; support incremental uploads and publish complete assets atomically.

**Verify:** cold multi-model loads and texture hot-reload storms, measuring p95/p99 frame time, queued bytes, oldest request age, and time until assets become ready. Preserve sequence cancellation, `ContentBlob` ownership, teardown disposal and retry behavior.

### 6. P2 — The part-mesh cache sweeps unchanged parts every frame

**Code-confirmed.** [Scene.cs:1040](../../SpectraEngine.Core/Scene/Scene.cs#L1040), `ProcessPartBrushMeshes`, enumerates all part nodes even if none changed. [PartBrushMeshCache.cs:87](../../SpectraEngine.Core/Scene/PartBrushMeshCache.cs#L87) first clears every entry's touched flag, and `EndPump` at line 112 scans every entry again. Eight thousand copies sharing one mesh still cause eight thousand acquisitions per frame.

**Fix:** process brush-reference membership changes through a dirty queue and maintain reference counts per immutable brush. Node movement should cause no mesh-cache work. Handle brush replacement, node detach/reattach, kind conversion and scene shutdown explicitly.

**Verify:** count acquisitions/builds/releases at rest and during transforms; they should follow membership changes. Keep tests for shared brushes, undo, material refresh and failed builds. Add a timing scope before assessing its share of a frame—this work currently sits outside the Assets scope.

### 7. P2 — A tiny mesh delta still rebuilds all chunk clusters

**Code-confirmed.** [Scene.cs:1953](../../SpectraEngine.Core/Scene/Scene.cs#L1953), `ApplyChunkMeshDelta`, always calls `RebuildChunkClusters`. The [rebuild at line 864](../../SpectraEngine.Core/Scene/Scene.cs#L864) visits the entire chunk list and recounts all material batches, including for ordinary same-cell replacements. The CPU compiler's constant-size edit benchmark does not include this render-side work.

**Fix:** for replacement-only deltas, refit just the affected 64-chunk clusters and adjust batch totals by old/new counts. Retain the full rebuild when insertions/removals change cluster membership. Later, a hierarchy over clusters can remove the per-view linear scan of all cluster bounds.

**Verify:** measure WorldSwap at fixed dirty-cell count with increasing total chunk count. Preserve true render bounds, including overhangs, Morton emission order, and create-before-destroy atomicity.

### 8. P2 — A distant world edit still triggers a whole-world character scan

**Code-confirmed.** [BrushPlaneCollisionSource.cs:375](../../SpectraEngine.Core/Physics/Character/BrushPlaneCollisionSource.cs#L375), `RebuildWorldLaneIfStale`, correctly avoids rebuilding the local convex cover when its inputs match. However, whenever the compile count changes it calls [SelectPlacements at line 442](../../SpectraEngine.Core/Physics/Character/BrushPlaneCollisionSource.cs#L442), scanning every world placement and collecting every subtractive brush. With an active character and continuous remote edits, selection remains O(world) per affected tick.

**Fix:** select additive candidates through resident chunk lists or a placement index; track regional revisions across every compile since the last tick. Resolve cutters against the selected additives' bounds, including cutters outside the character region that affect an overlapping additive. A single most-recent dirty list can miss multiple intervening compiles.

**Verify:** idle character plus a remotely animated world brush in 1k/10k/50k worlds, then nearby edits, region crossings and boundary-spanning subtractive doorways. Measure selection and convex-cover construction separately.

### 9. P2, higher on integrated GPUs — Deferred rendering writes an unused attachment

**Code-confirmed.** [GBuffer.cs:74](../../SpectraEngine.Core/Graphics/GBuffer.cs#L74) allocates a full-size RGBA16F custom target. [GBufferFill.spectrashade:100](../../SpectraEngine.Core/Graphics/BaseShaders/GBufferFill.spectrashade#L100) writes zero into it, while the current deferred light shader has no custom sampler. This reserves **8 bytes/pixel**, or **15.82 MiB at 1920×1080**, before hardware compression/caching. The full layout including depth is 36 bytes/pixel; removing this target reduces that to 28. These are format-size calculations, not measured GPU traffic or milliseconds.

**Fix:** omit the target in the current material/pipeline layout, or make it an optional layout variant for shading models that use it. Change the shader output contract, attachments and D3D12 PSO formats together. **Keep emissive:** [DeferredLight.spectrashade:292](../../SpectraEngine.Core/Graphics/BaseShaders/DeferredLight.spectrashade#L292) consumes it.

**Verify:** pixel comparisons for existing material types, all backend validation gates, and GPU timings at multiple resolutions on UHD 770 and RTX 4070 Ti. Future custom shading models need an explicit compatible layout; do not assume the reserved channel can be removed without a shader-contract change.

### 10. P2 — Cooked models retain duplicate CPU geometry

**Code-confirmed.** [CookedModelData.cs:162](../../SpectraEngine.Core/Assets/CookedModelData.cs#L162), `BuildMesh`, copies/remaps each submesh's index range and its min-to-max vertex slice. [AssetManager.Models.cs:290](../../SpectraEngine.Core/Assets/AssetManager.Models.cs#L290) creates meshes with the default retained CPU access, and line 310 retains `asset.Data`. [Mesh.cs:105](../../SpectraEngine.Core/Graphics/Mesh.cs#L105) stores another positions array and index copy. The mapped reader avoids import work, but the upload bridge still copies and retains data. Widely interleaved submesh indices can duplicate large vertex ranges.

**Fix:** support shared vertex/index storage and draw ranges with base-vertex offsets. Make CPU retention a usage policy: keep data needed for picking/collision, but let runtime-only assets discard the import representation after upload. Carry mapped-blob ownership until the last consumer completes if upload begins referencing it directly.

**Verify:** resident and peak memory, load latency, exact picking, unload/hot-reload, and submeshes with shared or interleaved indices. Do not remove the picking data globally merely to lower an allocation counter.

### 11. P2 — Cooking holds all outputs in memory, then copies them into the pack writer

**Code-confirmed.** [CookSession.cs:167](../../Spectra.Kitchen/Cooking/CookSession.cs#L167) finishes the entire worker level into `RuleOutcome[]` before consuming any result. Each outcome retains emission payloads. At [line 204](../../Spectra.Kitchen/Cooking/CookSession.cs#L204), these are handed to `PackWriter.Add`; [PackWriter.cs:368](../../Spectra.Kitchen/Packs/PackWriter.cs#L368) copies even uncompressed payloads with `ToArray`. The writer retains the copies until final pack assembly. Worker count bounds concurrent encoders, but does not bound accumulated output memory.

**Fix:** spool immutable rule outputs to temporary/cache files and keep metadata in outcomes. Lay out entries in canonical sorted order, then stream or map payloads into the final pack while computing the digest. A bounded producer/consumer queue should preserve authored diagnostic order and deterministic pack bytes. Ownership transfer can remove one copy, but still leaves total-output-sized residency.

**Verify:** peak committed/private memory on a multi-gigabyte project, warm and cold cooks, bounded jobs, failure cleanup, and byte-identical serial/parallel outputs. The successful small bake benchmark does not establish bounded memory for large asset packs.

### 12. P2 — D3D12 mesh pooling retains its high-water allocation until shutdown

**Code-confirmed.** [D3D12Renderer.cs:2148](../../SpectraEngine.Core/Graphics/D3D12/D3D12Renderer.cs#L2148) rents power-of-two mesh buffers. `RecycleRetiredMeshBuffers` at line 2172 retains all returned buffers without a byte cap or aging; [ReleaseMeshBufferPool at line 2189](../../SpectraEngine.Core/Graphics/D3D12/D3D12Renderer.cs#L2189) is a shutdown path. Switching from large content to a small map can therefore keep a large reusable allocation alive. This is retained cache capacity, not evidence of a COM leak.

**Fix:** track free bytes per bucket and globally, enforce a configurable retention budget, and trim at map unload or after inactivity, only after the appropriate fence. Keep enough hot buckets to avoid recreating the resource-allocation problem the pool solved.

**Verify:** large-map → small-map cycles, resize/edit stress, committed bytes and pool hit rate. Separately profile the [upload-heap mesh placement](../../SpectraEngine.Core/Graphics/D3D12/D3D12Mesh.cs#L8) before migrating static geometry to default-heap storage; that is a content-dependent GPU optimization, not a demonstrated bottleneck in these runs.

### 13. P1 for trustworthy optimization — The profiler and baseline documents leave important costs ambiguous

**Measured and code-confirmed.** [Engine.cs:1009](../../SpectraEngine.Core/Engine.cs#L1009) runs editor interaction outside the Update scope. Static collision synchronization at line 1089, part-mesh maintenance at line 1095, audio at line 1117, and host snapshot publication at line 1282 also sit outside the named phases. [FrameProfiler.cs:112](../../SpectraEngine.Core/Diagnostics/FrameProfiler.cs#L112) retains exponential averages rather than raw frame distributions; there are no GPU phase timestamps. A sum of the displayed phases is not a complete frame account.

Two benchmark pitfalls are observable: the default demo generates CSG churn through [SceneManager.cs:1674](../../SpectraEngine.Core/Scene/SceneManager.cs#L1674) even with the self-test off, and D3D12 remained presentation-limited here. Old figures in [performance.md](../performance.md) are also mixed with outdated status: its table still says instancing is absent, texture compression is listed as future work, and the allocation section says measurement is missing despite the live counters at [SceneManager.cs:1564](../../SpectraEngine.Core/Scene/SceneManager.cs#L1564).

**Fix:** add non-overlapping scopes for the omitted work and an explicit unaccounted-frame bucket; separate DXGI Present time from fence wait time. Record rolling p50/p95/p99/max frame durations, real GPU timestamps read asynchronously, upload bytes and total draw/triangle counts. Add an explicit idle/CSG-animation benchmark switch, with the regression fixture retained as a distinct mode. Refresh the status table and maintain dated, reproducible baselines. Existing allocation counters should be retained and expanded, not reimplemented.

**Verify:** a static scene reports zero recompiles, the animation fixture still recompiles, phase accounting covers the frame, and an injected stall is visible in max/tail statistics. Add repeatable performance checks on known hardware rather than absolute GPU limits on arbitrary hosted CI machines.

There is also a small avoidable render-loop allocation: [Engine.cs:178](../../SpectraEngine.Core/Engine.cs#L178) constructs an instance-capturing snapshot delegate on every `PublishHostFrame` call, before `EngineHost.PublishFrame` checks its publication interval. Cache the delegate once or use a cached instance method. Measure that change separately; it cannot account for the demo's hundreds of MiB/s of compile churn.

### 14. P2 — Fast incremental compiles still recreate reusable snap data and scratch

**Measured allocation volume; code-confirmed reuse opportunity.** The isolated-edit benchmark allocates **0.08–0.10 MiB per edit**, and the continuous-edit demo produces hundreds of MiB/s overall. [CsgIncrementalCompiler.cs:441](../../SpectraEngine.Core/Bsp/CsgIncrementalCompiler.cs#L441) creates a fresh `snappedMemo` per compile; its `SnappedOf` helper at line 549 re-snaps requested carved arrays even when their identity was carried unchanged from the previous world. [CsgWorldCarry.cs:20](../../SpectraEngine.Core/Bsp/CsgWorldCarry.cs#L20) retains carved and welded arrays, but has no separate retained snapped representation. The compiler also constructs temporary sets, dictionaries and lists for each edit. These sites are not individually allocation-profiled here, so they are candidates rather than an attribution of the entire logged rate.

**Fix:** capture allocation stacks, then retain immutable snapped results keyed by unchanged carved input and any snap-affecting settings. Reuse compile-private scratch between jobs, with caps to avoid retaining an exceptional large edit forever. Keep published arrays immutable while earlier worlds are still rendering; pooling output arrays without proving their last reader is finished would break the compile/render ownership contract.

**Verify:** repeat the isolated, dense-overlap and animated-demo workloads with allocation bytes per compile, GC counts, frame-tail latency and mesh/BSP byte-identity oracles. Also measure retained heap size: lower allocation rate obtained by retaining everything is not a sufficient result. Preserve the intentional world-brush stress fixture; ordinary simulated moving objects should use part semantics when they do not need CSG merging.

## What is already working, and what to defer

- Incremental isolated-brush moves remain independent of world size in the tested corpus. CSG query rates were **42.42 M point queries/s and 7.52 M rays/s** on the floorplan; the sparse open-world fixture was **10.23 M and 1.60 M**, respectively. These are synthetic BSP queries, not gameplay physics throughput.
- The map serializer passed both existing share-of-compile and per-cell scaling gates. Its worst share in this run was 21.7%, below the benchmark's 33% ceiling.
- Instancing is active in both geometry and shadows. The repeated-prop log demonstrates actual saved draws; a task to “add instancing” would duplicate existing work.
- Static chunks already use Morton-ordered 64-chunk cull clusters. The scene has a dynamic BVH for queries, retained render-view buffers, asynchronous CSG, and incremental GPU mesh replacement.
- Cooked BC textures, mapped content, baked maps, cache-aware cooking and allocation/GC logging already exist. Their remaining limitations are more specific than “add compression,” “add a cook,” or “start counting allocations.”

LOD/distance policy, occlusion culling, render scale, and shadow update policy are valid next-stage options, but this review did not measure their benefits. The runtime model bridge does not use the format's LOD table. Shadow rendering clears the full atlas and redraws all cascades in [Renderer.cs:1481](../../SpectraEngine.Core/Graphics/Renderer.cs#L1481); staggered updates require retaining each cached cascade's matching projection and depth and clearing only updated tiles. Treat these as separately profiled features after the measured CPU/latency defects, not guaranteed percentage wins.

## Suggested implementation order

1. Repair measurement coverage and split idle, continuous-edit, and presentation tests (#13). This can happen alongside the well-localized membership fix (#1).
2. Fix drawable visibility scaling and unchanged-part work (#2, #6), then refit chunk clusters incrementally (#7).
3. Add upload budgets/backpressure (#5) and measure structural-edit latency while developing the insertion/removal CSG path (#3). Use allocation stacks to prioritize compile reuse (#14).
4. Optimize D3D12 frame contexts (#4) and deferred bandwidth (#9) using explicit GPU evidence. Keep lifetime and image gates active.
5. Address character selection, model residency, cook residency and pool budgets (#8, #10–#12) against representative large projects.

## Reproduction and checks completed

Run each benchmark separately to avoid contention:

```powershell
dotnet build Benchmarks/CsgBench/CsgBench.csproj -c Release
dotnet run -c Release --no-build --project Benchmarks/CsgBench -- query incremental openworld bake
dotnet run -c Release --project docs/reviews/probes/SceneProbe.csproj

dotnet build SpectraEngine.Executable/SpectraEngine.Executable.csproj -c Release
dotnet run -c Release --no-build --project SpectraEngine.Executable -- d3d11 --profile --debug-layer=false --size=1920x1080 --selftest=false
dotnet run -c Release --no-build --project SpectraEngine.Executable -- d3d11 --profile --debug-layer=false --size=1920x1080 --adapter=4070 --props=8000 --selftest=false
dotnet run -c Release --no-build --project SpectraEngine.Executable -- d3d12 --profile --debug-layer=false --size=1920x1080 --adapter=4070 --selftest=false
dotnet run -c Release --no-build --project SpectraEngine.Executable -- d3d11 --debug-layer=true --size=1280x720 --adapter=4070 --selftest
```

GPU commands open a demo; close it after about 22 seconds. The captured runs launched the built executable with stdout/stderr redirected and requested window close at the time limit. No performance run overlapped another benchmark.

- CsgBench Release build succeeded, with eight existing obsolete Silk.NET API warnings; all requested scenarios completed with exit code 0 and the built-in verdicts passed.
- Release demo build succeeded. The headless scene probe built and completed successfully; its visible-result check and zero-allocation measurements passed.
- D3D11/D3D12 demos shut down cleanly. The separate D3D11 run enabled the debug layer and logged **four `Editing self-test: PASS` results**, with no ERR/FTL entries. It is a correctness smoke check, not a performance baseline.
- The original review changed no production source. Its probe is outside the solution, in [probes/Program.cs](probes/Program.cs).

## Implementation record — 5 September 2026

The fourteen fixes are implemented in the working tree against the reviewed revision. The table below is the current disposition; the findings above remain a historical record. Correctness gates pass. Performance results include two material tradeoffs: cold raw cooking takes additional disk I/O, and the compact G-buffer's faster geometry pass has less consistent 4K UHD 770 presentation tails. Those limitations are measured below, rather than hidden by an average.

**Compatibility:** `GeometryFormatVersion` and the map cook rule are now **2**. Recook compiled maps and models carrying the geometry version; their readers reject old versions with a recook diagnostic. Authored map formats, authored scene traversal and undo ordering are unchanged. The cache graph also advances to version 2 for 64-bit payload lengths; old cache graphs miss and regenerate.

Paths in the implementation column are relative to `SpectraEngine.Core/` unless prefixed otherwise.

| Finding | Implementation and location | Verified disposition |
|---|---|---|
| **#1 Membership** | [OrderedIdentityList](../../SpectraEngine.Core/Scene/OrderedIdentityList.cs), [Scene](../../SpectraEngine.Core/Scene/Scene.cs): identity-indexed membership/removal, attachment ordinals, tombstones and lazy public-view compaction. Renderer mesh/texture tracking also uses reference-identity sets. | Ordering, component/subtree/cross-scene changes and detach/reattach tests pass. At 50k parts, attachment falls from 2,245 to 207 ms, including the additional drawable BVH. |
| **#2 Drawable selection** | [SceneBvh](../../SpectraEngine.Core/Scene/SceneBvh.cs), [Frustum](../../SpectraEngine.Core/Scene/Frustum.cs), `Scene.BuildRenderView/BuildShadowView`: separate drawable index, surviving-leaf ordinal sort using retained storage; fully contained root uses the ordered membership view directly. General queries keep their own index. | Parent motion, reparent/component changes, cross-scene ownership and stable draw order pass. Hidden, fixed-visible and all-visible controls improve; steady view construction allocates 0 B/call. |
| **#3 Structural CSG** | [PlacementSnapshot](../../SpectraEngine.Core/Bsp/PlacementSnapshot.cs), [Scene.Placements](../../SpectraEngine.Core/Scene/Scene.Placements.cs), [CsgWorld.Placements](../../SpectraEngine.Core/Bsp/CsgWorld.Placements.cs), [CsgIncrementalCompiler](../../SpectraEngine.Core/Bsp/CsgIncrementalCompiler.cs): immutable persistent authored order plus stable internal IDs/slots, structural journal and local residency/relationship/carve/weld/chunk patches. | Full-versus-incremental byte oracles pass for insert/remove/duplicate/reparent/reorder/undo-shaped edits, tied bounds, cutters, newly formed overlaps and cell boundaries. Older worlds remain immutable. Public placement/chunk views and baking use canonical dense authored indices. Live 50k add/remove publication is about 1.9/2.2 ms; plain-array calls without a complete structural journal retain the validated full fallback. |
| **#4 D3D12 overlap** | [D3D12Renderer.Frames](../../SpectraEngine.Core/Graphics/D3D12/D3D12Renderer.Frames.cs), renderer and resource classes: 1–3 contexts, default 2; independent command allocators/lists, upload/descriptor storage and completion fences; versioned mutable instance/debug storage. Replaced resources/rings retire after their submission fence. | CPU waits only at context reuse or explicit drain operations. Real-driver tests, 8,000-prop resize/reload stress, clean shutdown and shared-viewport gates pass. RTX 1080p host frame average falls from 0.54–0.55 ms with one context to 0.34–0.35 ms with two. Capability-checked uncapped presentation is opt-in; unsupported modes report that fact. |
| **#5 Asset scheduling** | [AssetUploadPipeline](../../SpectraEngine.Core/Assets/AssetUploadPipeline.cs), [AssetManager.Uploads](../../SpectraEngine.Core/Assets/AssetManager.Uploads.cs), [AssetManager.ModelPreparation](../../SpectraEngine.Core/Assets/AssetManager.ModelPreparation.cs), [MeshUpload](../../SpectraEngine.Core/Graphics/MeshUpload.cs), [TextureUpload](../../SpectraEngine.Core/Graphics/TextureUpload.cs), backend upload implementations. | One fair texture/model queue; 2 ms / 8 MiB per frame, 256 KiB aligned steps; up to four workers, 256 MiB admitted payload limit and exclusive oversized admission. Tests cover stale/coalesced requests, fair byte sharing, blocked cancellation, retries and teardown ownership. Large RGBA, RGB mip and padded BC uploads match pixels on all three backends. D3D12 direct-queue copies share submission and fence retirement. Model publication is atomic; texture placeholders may resolve afterward. |
| **#6 Part meshes** | [PartBrushMeshCache](../../SpectraEngine.Core/Scene/PartBrushMeshCache.cs), `Scene.ProcessPartBrushMeshes`: coalesced final reference counts keyed by immutable brush identity. | Unchanged/transform-only frames do no cache work. Shared brushes, material refresh, failed builds, detach/reattach and device recreation pass. The previous 409 µs unchanged 50k-part scan becomes a no-op below the probe's useful timing precision. |
| **#7 Chunk publication** | `Scene.ApplyChunkMeshDelta/RefitChunkClusters` in [Scene](../../SpectraEngine.Core/Scene/Scene.cs): refit only affected 64-chunk groups and adjust material totals for replacement-only deltas. | 50k one-chunk replacement falls from 829 µs to 15.8 µs including headless renderer resource bookkeeping. Actual membership insertion/removal intentionally rebuilds cluster layout. |
| **#8 Collision selection** | [BrushPlaneCollisionSource](../../SpectraEngine.Core/Physics/Character/BrushPlaneCollisionSource.cs), [Scene.WorldChanges](../../SpectraEngine.Core/Scene/Scene.WorldChanges.cs), chunk residency: local additive selection, cutters against complete additive bounds, bounded 256-publication history. | Remote and nearby edits, multiple publications between ticks, 257-publication overflow, region crossings and cutters outside the character region pass. Remote publication at 50k brushes falls from 1,617 µs to 2.0 µs. History misses reselect through the local index. |
| **#9 Deferred targets** | [GBuffer](../../SpectraEngine.Core/Graphics/GBuffer.cs), [Renderer](../../SpectraEngine.Core/Graphics/Renderer.cs), [GBufferFillCompact](../../SpectraEngine.Core/Graphics/BaseShaders/GBufferFillCompact.spectrashade): four standard color attachments plus depth, emissive preserved, distinct compact shader identity and matching MRT/PSO formats. Explicit extended layout retains five attachments. | Standard/extended pixel comparisons pass on all three backends, including nonzero emissive. One removed RGBA16F target saves 63.28 MiB at 4K. Geometry GPU time falls 0.27→0.22 ms on RTX and 3.77→2.91 ms on UHD 770. UHD frame-tail tradeoff is recorded separately below. |
| **#10 Model storage** | [ModelGeometry](../../SpectraEngine.Core/Assets/ModelGeometry.cs), [ModelMetadata](../../SpectraEngine.Core/Assets/ModelMetadata.cs), [CookedModelData](../../SpectraEngine.Core/Assets/CookedModelData.cs), [SharedMeshStorage](../../SpectraEngine.Core/Graphics/SharedMeshStorage.cs): shared CPU/GPU backing, indexed ranges with base-vertex offsets, separate metadata and explicit Full/Picking/GpuOnly CPU retention. | Default Full preserves import data and editor picking. Shared index/position views avoid duplicate picking arrays. Base-vertex/first-index/first-instance and owner-disposal pixel tests pass on all backends. Overlapping-submesh fixture retains 10 MiB Full / 8 MiB Picking / approximately zero GPU-only CPU geometry, versus 116 MiB originally. |
| **#11 Cook residency** | [PackPayload](../../Spectra.Kitchen/Packs/PackPayload.cs), [PayloadSpool](../../Spectra.Kitchen/Cache/PayloadSpool.cs), [ContentStore](../../Spectra.Kitchen/Cache/ContentStore.cs), [PackWriter](../../Spectra.Kitchen/Packs/PackWriter.cs), [AtomicOutput](../../Spectra.Kitchen/Packs/AtomicOutput.cs), cook/cache/rule paths. | Emissions spool immediately; outcomes/cache replay retain file handles, lengths and hashes. Canonical pack streaming uses bounded buffers, unchanged compression/padding and incremental digesting. Span writer API remains available. Corruption/mutation, concurrent CAS publication, repeated writes, cancellation/failure cleanup, loose output and serial/parallel/cold/warm identity pass. The 2 GiB corpus peaks at roughly 14 MiB private memory; cold disk-I/O cost remains. |
| **#12 Mesh pool** | [D3D12Renderer.MeshPool](../../SpectraEngine.Core/Graphics/D3D12/D3D12Renderer.MeshPool.cs), [MeshBufferMemory](../../SpectraEngine.Core/Graphics/MeshBufferMemory.cs): completed buffers only, 64 MiB global/16 MiB bucket caps, least-recently-used eviction and 300-submission expiration. | Real-driver capacity/idle-expiration and shared-target tests pass. Active, retired and pooled bytes report separately. Outstanding GPU references cannot enter the reusable pool. Static mesh upload-heap placement remains unchanged; default-heap migration was not part of the measured fix. |
| **#13 Measurement** | [FrameProfiler](../../SpectraEngine.Core/Diagnostics/FrameProfiler.cs), [GpuTimestampTimer](../../SpectraEngine.Core/Diagnostics/GpuTimestampTimer.cs), three backend timers, [Engine](../../SpectraEngine.Core/Engine.cs), [DemoStartupOptions](../../SpectraEngine.Executable/DemoStartupOptions.cs). | Exclusive editor/collision/part/audio/snapshot/presentation/fence-wait scopes and explicit unaccounted time; 2,048-frame rolling p50/p95/p99/max sorted on report cadence in reusable storage. Nonblocking GPU query rings drop busy samples and report invalid/unavailable values explicitly. Existing allocation/GC counters remain. Snapshot callback is cached. Default demo animation is off; explicit CSG fixture and independent self-test pass. |
| **#14 Compile allocation** | [CompileWorkspace](../../SpectraEngine.Core/Bsp/CompileWorkspace.cs), [CsgWorldCarry](../../SpectraEngine.Core/Bsp/CsgWorldCarry.cs), `Csg.CarveScratch`, compiler/welder: retain immutable snapped results and exclusively lease private scratch. | Nested jobs cannot share a workspace. References clear on release; collections over 4,096 capacity are discarded. Published arrays are never pooled. Allocation stack traces identify immutable pages and output geometry as remaining allocations. Controlled fresh-versus-reused scratch reduces 78,228→70,083 B/compile (10.4%); timing ranges overlap, so no separate speedup is claimed. |

### Measurement protocol and results

Same review machine: Core i9-13900K (32 logical processors), 31.8 GiB RAM, RTX 4070 Ti (driver 32.0.16.1074), Intel UHD 770, Windows build 10.0.26200, .NET SDK 10.0.201/runtime 10.0.5. Release JIT probes disable tiered compilation. Performance runs execute sequentially, with validation, editing self-test and CSG animation disabled unless explicitly testing those features. Original controls use an isolated archive of the reviewed revision; small adapted baseline probe sources are retained under [probes/original](probes/original/). No absolute GPU performance threshold is added to hosted CI.

**Scene scaling.** Two warmups, three measured samples of 200 view calls, median reported. Shared mesh populations have 0, 100 or all leaves visible; attachment uses preconstructed shared-brush nodes. All listed final views allocate 0 B/call. Root rejection is a favorable hidden-scene case, not a claim about arbitrary visibility distributions.

| Population / visible | Original view µs | Final view µs |
|---|---:|---:|
| 1k / 0 | 14.472 | 0.041 |
| 1k / 100 | 19.097 | 14.387 |
| 1k / all | 58.184 | 33.211 |
| 10k / 0 | 164.089 | 0.049 |
| 10k / 100 | 167.739 | 14.483 |
| 10k / all | 597.496 | 331.188 |
| 50k / 0 | 983.788 | 0.050 |
| 50k / 100 | 1,002.013 | 14.346 |
| 50k / all | 3,458.845 | 1,728.052 |

50k part attachment is 2,244.66→206.70 ms. World-only attachment is 98.25→108.83 ms: approximately 11% additional one-time bookkeeping for the structural journal. This control regression is explicit; it does not recur every frame. Sources: [original visibility/attachment](data/2026-09-05-baseline-visibility-control.txt), [final](data/2026-09-05-final-scene.txt).

**Maintenance and structural editing.** Two warmups, nine samples, median. At 50k chunks, replacement-only publication is 828.6 µs (range 796.7–1,027.7)→15.8 µs (15.0–17.9); remote collision publication is 1,617.4 µs (1,612.2–1,708.5)→2.0 µs (1.8–2.6). These improvements exceed observed run variation. [Original](data/2026-09-05-maintenance-original.txt), [final](data/2026-09-05-maintenance-final.txt).

Live structural add/remove medians are 0.169/0.065 ms at 1k, 0.514/0.388 ms at 10k and 1.895/2.185 ms at 50k. The timer includes snapshot journal capture, worker dispatch, compile, publication and fake GPU resource replacement. New chunk membership still requires a full cluster-layout rebuild, so end-to-end structural publication is not constant-time and allocates approximately 0.24/0.55/2.11 MB per edit. Isolated compiler moves remain approximately 0.08–0.10 ms from 1k to 50k. Plain-array structural additions intentionally take the full recovery path; do not confuse that benchmark with the live Scene journal. [Structural probe](data/2026-09-05-final-structural.txt), [CSG/query/bake gates](data/2026-09-05-final-csg-bench.txt).

Scratch reuse uses two warmups followed by nine groups of 100 compiles, alternating fresh exclusive storage and reuse on the same immutable 1k world. Allocations fall 10.4%; medians are 0.0507 versus 0.0486 ms with overlapping ranges. [Exact control](data/2026-09-05-workspace-control.txt). Sampled allocation stacks [before](data/2026-09-05-allocation-stacks-before.txt) and [after](data/2026-09-05-allocation-stacks-after.txt) still show copy-on-write published pages, BSP polygons and emitted mesh arrays. Their lifetime belongs to the published world, so reducing scratch allocation does not justify pooling them.

**D3D12 overlap.** RTX 4070 Ti, 1920×1080, deferred standard layout, shadows on, explicit uncapped presentation. Three 32-second runs per context count, rotated order, first 10 seconds discarded. Each cell below is the range across run summaries. Percentiles are medians of reported 2,048-frame rolling windows, not a percentile recomputed over the entire capture.

| Contexts | Host frame average ms | Frame p50 ms | Frame p95 ms | Frame p99 ms | Highest reported window max ms |
|---|---:|---:|---:|---:|---:|
| 1 | 0.54–0.55 | 0.43–0.44 | 1.02–1.10 | 2.19–2.31 | 4.12 |
| 2 (default) | 0.34–0.35 | 0.26 | 0.75–0.76 | 1.94–1.97 | 4.95 |
| 3 | 0.33 | 0.26 | 0.70–0.72 | 1.79–1.87 | 4.43 |

Two contexts improve throughput and ordinary tails beyond run variation; worst individual stalls remain noisy. GPU total is 0.20 ms with one context versus 0.17–0.18 ms with two. Steady allocation rises from about 0.8 to 1.3 MB/s as more frames execute; per-frame allocation stays approximately 400 B. Idle captures report no post-initial-build CSG recompilation. This is a controlled comparison using the new instruments, not a comparison against the original presentation-limited 60 Hz capture. [CSV](data/2026-09-05-gpu-summary.csv), [run manifest](data/2026-09-05-gpu-runs.txt).

**Deferred layout.** Two 32-second runs per layout/adapter, reversed order, 4K, same defaults as the overlap capture. On RTX, geometry GPU time is consistently 0.27→0.22 ms and host average 0.90–0.91→0.85–0.86 ms. On UHD 770, geometry is consistently 3.77→2.89–2.91 ms. Removing one 8-byte-per-pixel attachment saves 15.82 MiB at 1080p or 63.28 MiB at 4K; those are format-size calculations, not a bandwidth-counter measurement.

The longer UHD captures run for 102 seconds and discard 75 seconds, enough to expire startup from the 2,048-frame window. Extended→standard host average is 30.84→30.11 ms, p50 30.81→29.94 ms, but p95 **31.04→34.91 ms** and p99 **31.24→35.06 ms**. The fence-wait scope dominates those frames; individual timestamp samples contain approximately 5.2 ms gaps in different GPU phases. The cause of those gaps is not established by these instruments. This is a measured 4K integrated-GPU frame-tail regression despite lower geometry cost; no claim of uniformly better frame pacing is made. The explicit extended variant remains available for this workload. Short UHD runs retain startup in their rolling maxima and must not be used as steady-state tail evidence. [Extended long](data/2026-09-05-gbuffer-770-extended-long.txt), [standard long](data/2026-09-05-gbuffer-770-standard-long.txt).

An additional single-context control uses the same 102-second duration and 75-second warmup. Compact layout reports host average 29.99 ms, p50/p95/p99 29.97/30.22/30.40 ms and maximum 31.11 ms, retaining the 2.90 ms geometry time. Thus `--frame-contexts=1` is a measured mitigation for this particular UHD 770 4K workload; the default remains two as planned. The extended single-context control itself has large outliers (p95 50.73 ms, p99 118.07 ms), so it does not support a universal preference for one context or identify the driver scheduling cause. [Compact control](data/2026-09-05-gbuffer-770-standard-contexts-1-long.txt), [extended control](data/2026-09-05-gbuffer-770-extended-contexts-1-long.txt).

**Models and upload latency.** A mapped cooked model contains 262,144 vertices and eight submeshes with overlapping full vertex spans. Original Full retention is 116.01 MiB, loading in 46.9–53.4 ms with a single 22.0–22.4 ms upload pump. Final Full retains 10.00 MiB, Picking 8.00 MiB and GPU-only approximately zero CPU geometry after publication. Final loads take 38.2–44.7 ms; two upload pumps per model, largest measured pump 0.95 ms under the stock budget, no CPU/byte/alignment overruns. Shared GPU buffer buckets total 12 MiB. Peak admitted/worker payloads are 10 MiB Full/GPU-only and 16 MiB Picking. The probe separates retained CPU storage from in-flight payloads and GPU buffers. [Original three repeats](data/2026-09-05-model-memory-original.txt), [final policies](data/2026-09-05-model-memory-mapped.txt).

These are measured fixture results, not hard latency guarantees. Importers, driver allocation, mip generation and minimum aligned rows/blocks are indivisible; time and memory limits are soft around those operations and overruns are counted. Workers can temporarily hold payloads awaiting admission, reported separately from the 256 MiB queue bound. Synchronous APIs deliberately drive the same machinery to completion and can block. Full retention's compatibility array accessors materialize an individual submesh slice only when requested; upload/picking use shared backing directly.

**Cook memory and identity.** Four distinct 512 MiB raw inputs, 2 GiB total, 128 KiB streaming buffers, 5 ms private/managed-memory sampling. Input creation, final independent hash verification and GC-retained measurements occur outside the timed cook. Cold means no cook cache; it does not promise a cold OS file cache.

| Run | Original time / private peak | Final time / private peak | Final managed peak / total allocation |
|---|---|---|---|
| Cold serial, cached output | 9.400 s / 4,643 MiB | 11.310 s / 13.77 MiB | 2.63 / 2.31 MiB |
| Warm parallel, 4 workers | 7.565 s / 5,204 MiB | 6.366 s / 13.84 MiB | 2.18 / 1.71 MiB |
| Uncached parallel, 4 workers | Not captured | 5.909 s / 14.04 MiB | 2.19 / 1.36 MiB |

All produce 2,147,487,760-byte packs with hash `6ED5885A716B3B00EFEB100DFE599669`. The raw corpus has no geometry version field; geometry-bearing determinism is separately covered by the Kitchen oracles within version 2. Streaming/spooling removes multi-gigabyte residency but adds persistent cache disk writes; this single cold run is about 20% slower, while warm replay is faster. Filesystem timing was not repeated enough to promise those throughput percentages. The memory reduction is several orders of magnitude larger than sampling variation. Valid immutable cache entries survive later failures; temporary/session output is cleaned, final files are atomically replaced. [Original](data/2026-09-05-cook-memory-original.txt), [final](data/2026-09-05-cook-memory-final.txt).

### Correctness and release gates

All eight relevant suites ran through `dotnet run`: **3,105 discovered, 3,104 passed, one pre-existing skip, zero failures**. Latest suite totals are BSP/scene 1,057, Editing 795, Editor 443, Physics 53, Entities 77, Kitchen 346, Graphics 260 and compiler 74. The skip is the existing inexact-plane flush staircase closure defect; this change neither hides it nor claims to fix it.

Focused new tests include `PlacementSnapshotTests`, `ScenePlacementJournalTests`, `PerformanceInfrastructureTests`, `CompileWorkspaceTests`, `CollisionPublicationTests`, `AssetUploadPipelineTests`, `StreamingPayloadTests`, `MeshUploadParity`, `GBufferLayoutParity` and expanded mesh-pool/shared-target/asset/model tests. Existing map/pack/bake determinism oracles run in the complete Kitchen suite. A concurrent CAS race found during verification was fixed by accepting only an independently verified same-hash/same-length destination; the final parallel suite is green.

- [Release solution build](data/2026-09-05-final-release-build.txt): 0 errors; 40 warnings in this build.
- [NativeAOT publish](data/2026-09-05-nativeaot-publish.txt): passed without a global `PublishAot` override. Existing Silk.NET trim/single-file warnings remain. [Published executable smoke](data/2026-09-05-final-nativeaot-smoke.txt): clean exit, four editing self-test passes, no ERR/FTL.
- [BSP/scene](data/2026-09-05-final-bsp-tests.txt), [Editing](data/2026-09-05-final-options-tests.txt), [Kitchen](data/2026-09-05-final-kitchen-tests.txt), [Graphics](data/2026-09-05-final-graphics-tests.txt); the other four suite logs are in [data](data/).
- D3D12 [8,000-prop stress](data/2026-09-05-final-d3d12-props-stress.txt): 22 seconds, animation and editing self-test, fullscreen every two seconds and repeated shader reload; four PASS results, clean shutdown, no ERR/FTL. Separate [D3D11](data/2026-09-05-final-d3d11-smoke.txt) and [OpenGL](data/2026-09-05-final-opengl-smoke.txt) smoke tests also pass. Validation was enabled for Direct3D correctness runs.
- D3D11 and D3D12 [viewport comparison](data/2026-09-05-final-d3d12-viewport-compare.txt) and [producer/consumer pacing](data/2026-09-05-final-d3d12-pacing.txt) gates exit successfully; counterpart D3D11 logs are alongside them. Graphics tests compare sliced upload and draw-range pixels on all three actual backends.
- CsgBench `query incremental openworld bake` passes. Bake's worst serialization share is 32.5%, below its 33% ceiling; per-cell staging remains flat. New geometry ordering intentionally changes the output version, not the deterministic-within-version contract.

Reproduction commands, workload details, original-control setup and scripts are in [probes/README.md](probes/README.md). Large generated cook corpora, isolated baseline checkout, binary traces and tool packages are excluded from the patch. No LOD, occlusion culling, render scaling or staggered-shadow feature was added.
