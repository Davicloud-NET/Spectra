# Performance review probes

Run from the repository root, in Release, one benchmark at a time. These probes are outside the solution. They create generated inputs under `docs/reviews/scratch/`, which is ignored. The cook probe needs approximately 7 GiB of free disk space per checkout.

```powershell
$env:DOTNET_TieredCompilation = '0'
dotnet build -c Release docs/reviews/probes/SceneProbe.csproj
dotnet docs/reviews/probes/bin/Release/net10.0/SceneProbe.dll
dotnet docs/reviews/probes/bin/Release/net10.0/SceneProbe.dll maintenance
dotnet docs/reviews/probes/bin/Release/net10.0/SceneProbe.dll structural
dotnet docs/reviews/probes/bin/Release/net10.0/SceneProbe.dll workspace
dotnet docs/reviews/probes/bin/Release/net10.0/SceneProbe.dll models
dotnet docs/reviews/probes/bin/Release/net10.0/SceneProbe.dll cook
```

The visibility probe uses 1k/10k/50k populations with 0, 100 and all leaves visible. Each sample measures 200 calls, with two warmups and three measured repetitions. Structural and maintenance probes use two warmups and nine samples. The structural timer includes journal capture, worker dispatch, compilation, publication and fake renderer resource replacement. Maintenance drives a real compiled world and real collision selection through a headless renderer. The workspace control holds an outer lease to force exclusive fresh scratch; it never mutates the published input world.

The model probe uses the RTX 4070 Ti through D3D12, a mapped cooked model with eight submeshes sharing overlapping spans of 262,144 vertices, and 1 ms between upload pumps. It reports CPU memory, queue statistics and GPU buffer accounting separately. The cook probe creates four distinct 512 MiB raw inputs, then runs serial cached, parallel cached and parallel uncached cooking. **The first run is cold only if its `.spectra-cook` cache is absent.** Re-running the probe without removing that generated cache produces a warm first run. Memory sampling is every 5 ms; short peaks can be missed. The raw corpus has no geometry version field and is expected to retain its original pack hash across the geometry version change.

`RunGpu.ps1` runs nine rotated 1/2/3-frame-context captures, two reversed standard/extended G-buffer comparisons per adapter, driver smoke checks and shared-viewport gates. `RunIntegrated.ps1` runs longer 4K UHD 770 captures with 75 seconds of warmup; `-FrameContexts 1` selects the isolation control. `RunFinalSmoke.ps1` stresses 8,000 props with shader reload/fullscreen/CSG editing, then runs the NativeAOT executable. These scripts open real demo windows and close them with WM_CLOSE. Build and publish the executable first. `SummarizeGpu.ps1` creates the CSV from completed captures, discarding warmup reports. Its percentile columns summarize the reported 2,048-frame rolling windows, not all frames in the capture concatenated together.

## Original revision controls

`original/` preserves the small probe sources adapted to revision `e3a2280d75a3d0e0012c1252ea7f5ef5005a1614`. Archive that revision into an isolated folder, copy these files into a `Probe/` directory at the archived root, and build `Probe/Baseline.csproj`. Run the resulting `SpectraEngine.Bsp.Tests.dll` from the active repository root to match the recorded content paths. Modes are the default visibility probe, `maintenance`, `models` and `cook`. The original model loader is unbudgeted; the printed budget text describes the final comparison settings, not a feature of the original loader. The baseline needs the same SDK and native dependencies as the main repository. Generated archives and large input/output corpora are deliberately excluded from version control.

## Allocation traces

The recorded before/after traces ran the Release CsgBench `openworld` workload with tiering disabled, using `dotnet-trace` 10.0.731102 and provider `Microsoft-Windows-DotNETRuntime:0x1:5`. Install that tool version into `docs/reviews/tools/`; `trace/TraceSummary.csproj` references its TraceEvent assemblies. Pass a `.nettrace` path to the summary program. It groups sampled allocation ticks having `CsgIncrementalCompiler` on the stack; sampled byte amounts are not exact object totals. Text summaries are checked in, while binary traces and tool packages are ignored. The exact allocation-byte comparison is the separate workspace probe.
