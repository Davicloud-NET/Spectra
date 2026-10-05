# Performance

Current implementation and evidence: [5 September 2026 performance review](reviews/2026-09-05-performance-review.md#implementation-record--5-september-2026). It records all fourteen findings, fixes, source locations, tests, before/after measurements and limitations. Earlier measurements and speculative proposals are retained in [historical notes](reviews/performance-history-2026-08.md); they are not the current feature status.

## Measure a defined workload

Run performance captures sequentially, in Release, on a named adapter with a fixed resolution. Separate startup, idle scenes, CSG edits, asset loading and presentation pacing. Disable driver validation for throughput measurements; enable it for correctness tests.

```powershell
dotnet run -c Release --project SpectraEngine.Executable -- d3d12 --profile --uncapped --debug-layer=false --adapter=4070 --size=1920x1080 --demo-animation=off --selftest=false
```

| Option | Behavior |
|---|---|
| `--profile` | CPU phase averages, 2,048-frame rolling p50/p95/p99/max and asynchronous GPU pass timings |
| `--demo-animation=off\|csg` | Default off; CSG selects the preserved animated regression fixture |
| `--selftest` | Independent editing correctness fixture; use for an approximately 15-second smoke run |
| `--uncapped` | Explicit benchmarking mode; D3D12 checks tearing capability, GL requests swap interval zero, D3D11 reports unavailable |
| `--frame-contexts=1\|2\|3` | D3D12 context count, default 2; configure before renderer initialization |
| `--gbuffer=standard\|extended` | Four color attachments plus depth by default; extended retains five color attachments |
| `--adapter=<name>`, `--size=WxH` | Fix adapter and resolution; record both with the result |
| `--props=<count>` | Repeated part brushes sharing one brush identity |
| `--parts=<grid>` | Scale authored demo world geometry |
| `--shadows=false`, `--pipeline=forward` | Explicit cost-isolation controls |
| `--fullscreen-cycle=2` | Correctness stress: windowed/fullscreen transitions every two seconds |

Normal presentation behavior is preserved unless uncapped mode is requested. Shared-viewport producer/consumer pacing has its own `--pacing-probe` and pixel gate `--viewport-compare`.

CPU scopes are exclusive: editor interaction, collision synchronization, part mesh maintenance, audio, snapshot publication, presentation and fence waits are separately accounted, with remaining frame time reported as Unaccounted. CPU EMA and the host's frame average use different smoothing; compare like with like. Percentiles are sorted only on the reporting cadence using reusable storage. A 2,048-frame window lasts over a minute at 30 fps, so a short capture can retain startup stalls.

Tracing the walls between the sounds and the listener has its own scope, `SoundWalls`, opened inside `Audio` and not counted in it. The console command `sound_walls` prints its time and how many lines the frame traced.

GPU timestamps are read asynchronously on OpenGL, D3D11 and D3D12. Unavailable/disjoint samples are unavailable, never a fabricated zero duration. Busy query rings drop instrumentation without waiting for the GPU. Driver scheduling/preemption can appear inside a measured pass; timestamps do not by themselves identify that cause. Existing allocation rates, GC counts, draw/triangle/instancing statistics and mesh buffer memory counters remain available.

## Current defaults and ownership

- Scene drawable/light membership uses identity indexes and attachment-order views. A dedicated drawable BVH selects camera/shadow survivors; general scene queries retain their own spatial index. All-visible views use the ordered collection directly.
- Part meshes reconcile only changed brush reference counts. Replacement-only chunk deltas refit affected 64-chunk clusters; inserting/removing chunks rebuilds cluster layout.
- Structural CSG uses immutable stable placement slots and a change journal, while public views and baking preserve authored order. Local patches retain immutable carve/snap/weld results; incomplete journal information falls back to a validated full compile.
- D3D12 has two frame contexts and waits before reusing one. Shutdown, resize and synchronous readback explicitly drain. Resources and replaced rings retire by fence. The completed mesh buffer pool is bounded to 64 MiB globally and 16 MiB per bucket, with 300-submission idle expiry. Active, retired and pooled bytes are distinct.
- Textures and models share one fair upload scheduler: **2 ms CPU / 8 MiB per frame**, **256 KiB per aligned step**, **256 MiB admitted payloads**, and `min(4, max(1, processorCount - 1))` decode/import workers. One oversized payload can be admitted exclusively. Indivisible driver/import work makes these soft limits; queue snapshots expose worker-held bytes and overruns.
- Model geometry can share GPU buffers and indexed draw ranges, including base-vertex offsets. Full CPU retention remains the default and preserves editor picking. Picking retention keeps shared picking storage; GPU-only drops CPU geometry after publication. Hierarchy/material metadata remains available independently.
- Standard deferred rendering uses four color attachments and depth, preserving emissive. The compact shader has a separate asset identity and matching MRT/PSO formats. An explicit extended variant remains available.
- Cook outputs spool immediately to immutable cache/session files. Pack assembly streams in canonical order with incremental hashing, unchanged compression/padding, temporary-file publication and atomic replacement. Existing span writer and synchronous loading entry points remain supported.

Configure `AssetManager.UploadBudget` before attaching its renderer, then inspect `QueueStatistics` for pressure and overruns. Select retention with `ModelImportOptions.CpuRetention` (`Full`, `Picking`, `GpuOnly`). Synchronous loading intentionally drives the same machinery to completion and can block. A Full model's compatibility array accessor may materialize a submesh slice; upload and picking paths consume shared geometry directly.

**Recook required:** `GeometryFormatVersion = 2` and map cook rule version 2 intentionally change compiled CSG ordering. Old compiled maps/models carrying the geometry version are rejected with a recook request. Authored maps and undo order are unchanged.

## Measured results and limits

On the review machine, 50k fixed-visible drawable selection fell from 1,002 to 14.3 µs, one-chunk replacement from 829 to 15.8 µs, and remote collision publication from 1,617 to 2.0 µs. Live 50k structural add/remove publication is about 1.9/2.2 ms; inserting/removing chunk membership still rebuilds cluster layout. World-only attachment has approximately 11% additional one-time journal bookkeeping.

RTX 4070 Ti 1080p D3D12 host frame averages fell from 0.54–0.55 ms with one context to 0.34–0.35 ms with two; ordinary tails improve, while isolated maxima remain noisy. The compact G-buffer reduces 4K geometry time on both tested adapters and removes 63.28 MiB of target storage. At 4K on UHD 770, two contexts with compact targets increase p95 to about 35 ms despite lower median/geometry cost; the measured single-context control restores p95 to 30.22 ms. Use `--frame-contexts=1` to reproduce that control. This is a workload-specific tuning result, not a universal adapter policy.

The overlapping-submesh fixture retains 10 MiB of CPU geometry in Full mode instead of 116 MiB; its largest upload pump is 0.95 ms instead of about 22 ms. A 2 GiB raw cook peaks at approximately 14 MiB private memory instead of 4.6–5.2 GiB, with identical output bytes. The cold cached cook takes additional disk I/O (11.31 versus 9.40 seconds in the recorded run); warm replay is faster. Do not treat single disk-throughput runs as stable percentage guarantees.

Full measurements, repetitions, allocation traces, suite logs and caveats are in the dated review. [Reproduction probes](reviews/probes/README.md) include 1k/10k/50k visibility controls, structural editing, maintenance, scratch reuse, model/cook memory, GPU captures and runtime stress scripts.

## Sound through walls

`WallPropagation` traces five lines for a sound, one to the listener and four to a ring of a quarter unit round the listener's head, and 60 lines a frame at most over all sounds. That is twelve sounds a frame. The numbers are in `WallPropagationSettings`.

How a sound's lines become one answer (`SoundLines`, `WallLoss`):

- Along one line the losses add up in decibels. Each solid costs what its material takes at its thickness. A list of solids that ran out of room, 16 a line, counts for what it holds.
- The lines' gains are averaged, not their decibels. An opening that half the lines pass then lets half the sound through. Averaged in decibels, a view half open beside a thick wall would sound nearly shut, and a real gap leaks far more than that. The high end is what the lines leave at 5 kHz over what they leave in all, so the open lines carry it.
- A solid that the listener or the sound is inside counts by how deep that end is in it, and its surface loss comes in over a quarter unit (`WallLoss.EndDepth`). A camera that dips into a wall for a frame is a few centimetres deep, and the room behind it must not drop by 10 dB for that frame.
- A point of the ring that lies in a solid the listener is not in is no place to listen from, and its line counts as the listener's own. The ring fits inside the player's capsule to the sides. Its top is 5 cm above the capsule's, which is where this rule matters: under a ceiling the head touches.
- A sound on a part is heard from the part. Its node and the parts above it in the tree are not in its way, and each line starts where it leaves that part's box grown by a quarter unit (`SceneSoundObstacles.BodyReach`). That keeps a door's sound clear inside the wall the door slides into, and past the jamb. At the demo's start room door that holds for a listener up to about 60 degrees off the doorway's axis. Further round, the wall counts.

An answer is traced again when a compile landed near its lines, when the listener or the sound has moved a tenth of a unit, or after a quarter of a second, because a door that moves raises no signal. Near is by the cells the compile touched (`Scene.WorldChangedSince`), so a world brush that animates in another cell does not make every answer due on every frame. A full rebuild, another scene and a baked map that comes or goes do. When the level ends the answers are dropped: the next play session knows its sounds by the same node ids.

A sound with no answer yet goes first, then one heard along a single line so far, then the answers that have grown old, oldest first, then the rest, loudest first. With more due than fits, every sound still gets its turn within the quarter second plus one round: sounds times five over 60, in frames.

Measured in Release on the review machine, on a level the size of the demo's, with sounds up to 60 units from the listener. Other builds ran at the time, so each time is the span of three runs:

| Case | Lines a frame | Time a frame |
|---|---|---|
| 8 sounds, listener standing | 2.7 | 0.005 to 0.008 ms mean, 0.001 median |
| 8 sounds, listener walking at 4.5 units a second | 20 | 0.04 to 0.055 ms |
| 32 sounds, listener standing | 10.7 | 0.017 to 0.026 ms mean, 0.001 median |
| 32 sounds, listener walking | 60 | 0.105 to 0.16 ms, and 0.08 to 0.09 on the cooked level |
| 200 sounds, listener standing | 60 | 0.09 ms, one run |
| 200 sounds, listener walking | 60 | 0.12 to 0.17 ms |

So a frame at the full budget costs 0.1 to 0.17 ms, which is 2 to 3 microseconds a line with the sum. The 99th frame in a hundred took about twice the mean. Nothing allocates.

The first look at a material's file is paid once for each material, on the render thread, in the frame a line first meets it. For a loose file it took 59 microseconds at the median of 199 files and 0.18 ms at the slowest. The very first look of the run took 8.7 ms, with none of that code having run before, under the JIT. A file in a pack was not measured, and neither was the first look inside the engine. A level's materials are not read ahead.

The times are taken round the whole of `WallPropagation.Resolve` in `WallPropagationCostTests`, which is opt-in. They are not from `--profile` on a level being played:

```powershell
$env:SPECTRA_WALL_COST = "1"
dotnet run -c Release --project Test/Spectra.Kitchen.Tests -- -trait "Suite=WallCost" -showLiveOutput
```

## Deliberately separate future work

Instancing in geometry/shadows, cooked BC textures, mapped packs, baked maps and the fixes above are implemented. LOD/HLOD, occlusion culling, render scaling, staggered shadow updates and world paging remain separate features requiring their own measurements. The current patch adds none of them.
