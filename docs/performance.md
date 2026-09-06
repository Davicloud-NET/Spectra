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

## Deliberately separate future work

Instancing in geometry/shadows, cooked BC textures, mapped packs, baked maps and the fixes above are implemented. LOD/HLOD, occlusion culling, render scaling, staggered shadow updates and world paging remain separate features requiring their own measurements. The current patch adds none of them.
