using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging.Abstractions;
using Silk.NET.Core.Contexts;
using Silk.NET.Maths;
using Spectra.Kitchen.Models;
using Spectra.Kitchen.Packs;
using SpectraEngine.Core.Assets;
using SpectraEngine.Core.Assets.Packs;
using SpectraEngine.Core.Assets.Sources;
using SpectraEngine.Core.Graphics;
using SpectraEngine.Core.Graphics.D3D12;
using SpectraShade.Compiler;

internal static class ModelMemoryProbe
{
    internal static void Run()
    {
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        string root = Path.GetFullPath("docs/reviews/scratch/model-memory");
        Directory.CreateDirectory(root);
        WriteModel(Path.Combine(root, "overlap.smodel"));
        string packPath = Path.Combine(root, "models.spack");
        using (var writer = new PackWriter())
        {
            writer.AddFile("overlap.smodel", PackEntryKind.Model, Path.Combine(root, "overlap.smodel"));
            writer.WriteToFile(packPath);
        }
        var surface = new Surface();
        var renderer = new D3D12Renderer(NullLogger<Renderer>.Instance, new SpectraShadeCompiler()) { PreferredAdapter = "4070", EnableDebugLayer = false };
        renderer.SetFramebufferSize(surface.PixelSize); renderer.Initialize(surface);
        Console.WriteLine($"Adapter={renderer.AdapterName}; D3D12 contexts=2; 262144 vertices, 8 submeshes with overlapping full vertex spans; 2 ms / 8 MiB / 256 KiB upload defaults; 1 ms delay between pumps.");
        try
        {
            // Warm initialization and lazy runtime paths before sampling policies.
            Measure(ModelCpuRetention.GpuOnly, report: false);
            foreach (var policy in new[] { ModelCpuRetention.Full, ModelCpuRetention.Picking, ModelCpuRetention.GpuOnly }) Measure(policy, true);
        }
        finally { renderer.Shutdown(); }

        void Measure(ModelCpuRetention policy, bool report)
        {
            using var pack = new PackSource(NullLogger.Instance, packPath);
            var content = new ContentSourceStack(); content.Mount(pack);
            var assets = new AssetManager(NullLogger<AssetManager>.Instance, root, content, hotReloadEnabled: false);
            assets.AttachRenderer(renderer);
            long before = GC.GetTotalMemory(true), peak = before;
            var stopwatch = Stopwatch.StartNew();
            ModelAsset model = assets.RequestModel("overlap.smodel", new ModelImportOptions { CpuRetention = policy });
            var pumps = new List<double>();
            while (!model.IsReady)
            {
                if (stopwatch.Elapsed.TotalSeconds > 30 || model.Error is not null) throw new Exception(model.Error ?? "Model load timed out.");
                assets.PumpPendingUploads(); renderer.FlushUploads();
                if (assets.QueueStatistics.LastPumpBytes > 0) pumps.Add(assets.QueueStatistics.LastPumpMilliseconds);
                peak = Math.Max(peak, GC.GetTotalMemory(false));
                Thread.Sleep(1);
            }
            stopwatch.Stop(); renderer.FlushUploads(true);
            while (assets.QueueStatistics.ActiveWorkers != 0) Thread.Yield();
            long retained = GC.GetTotalMemory(true) - before;
            if (model.Meshes.Count != 8 || (policy != ModelCpuRetention.GpuOnly && model.Meshes[0].Positions.Count != 262144)) throw new Exception("Retention or range mismatch.");
            pumps.Sort();
            if (report) Console.WriteLine($"{policy}: retained={retained/1048576d:F2} MiB peak increase={(peak-before)/1048576d:F2} MiB load={stopwatch.Elapsed.TotalMilliseconds:F2}ms; upload pumps={pumps.Count}, p50={pumps[pumps.Count/2]:F3}ms max={pumps[^1]:F3}ms; queue={assets.QueueStatistics}; GPU buffers={renderer.MeshBufferMemory}");
            assets.ReleaseGraphicsResources(); assets.Shutdown();
        }
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void WriteModel(string file)
    {
        float[] vertices = new float[262144 * 8];
        for (int i = 0; i < 262144; i++)
        {
            vertices[i * 8] = i % 512; vertices[i * 8 + 1] = i / 512; vertices[i * 8 + 5] = 1;
        }
        const int perRange = 65538;
        uint[] indices = new uint[perRange * 8];
        var ranges = new SmodelSubmeshSpec[8];
        for (int r = 0; r < ranges.Length; r++)
        {
            for (int j = 0; j < perRange; j++) indices[r * perRange + j] = (uint)(j * 17 % 262144);
            indices[r * perRange] = 0; indices[(r + 1) * perRange - 1] = 262143;
            ranges[r] = new((uint)(r * perRange), perRange, null);
        }
        File.WriteAllBytes(file, SmodelWriter.Write(vertices, indices, ranges));
    }
    private sealed class Surface : IRenderSurface
    {
        public RenderSurfaceKind Kind => RenderSurfaceKind.Composited;
        public nint NativeHandle => 0;
        public IGLContext? GLContext => null;
        public Vector2D<int> PixelSize => new(64,64);
        public event Action<Vector2D<int>>? Resized { add { } remove { } }
    }
}
