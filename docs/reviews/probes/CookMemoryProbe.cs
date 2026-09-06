using System.Diagnostics;
using System.Globalization;
using Spectra.Kitchen.Cooking;
using Spectra.Kitchen.Packs;
using SpectraEngine.Core.Projects;

internal static class CookMemoryProbe
{
    internal static void Run()
    {
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        string root = Path.GetFullPath("docs/reviews/scratch/cook-memory");
        var project = ProjectLayout.Create(root, "MemoryProbe");
        byte[] block = new byte[1024 * 1024];
        new Random(731).NextBytes(block);
        for (int i = 0; i < 4; i++)
        {
            string file = Path.Combine(project.AssetsPath, $"payload{i}.blob");
            if (File.Exists(file) && new FileInfo(file).Length == 512L * 1024 * 1024) continue;
            block[0] = (byte)i;
            using var output = File.Create(file);
            for (int j = 0; j < 512; j++) output.Write(block);
        }
        Console.WriteLine("2 GiB raw corpus, four distinct 512 MiB payloads, 128 KiB streaming buffers; runtime " + Environment.Version);
        UInt128? expected = null;
        foreach (var (name, jobs, cache) in new[] { ("cold-serial",1,true), ("warm-parallel",4,true), ("session-parallel",4,false) })
        {
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            using var process = Process.GetCurrentProcess();
            process.Refresh(); long initial = process.PrivateMemorySize64, peak = initial;
            long heapStart = GC.GetTotalMemory(false), heapPeak = heapStart;
            using var stop = new CancellationTokenSource();
            var sampler = Task.Run(() =>
            {
                while (!stop.IsCancellationRequested)
                {
                    process.Refresh(); peak = Math.Max(peak, process.PrivateMemorySize64);
                    heapPeak = Math.Max(heapPeak, GC.GetTotalMemory(false));
                    Thread.Sleep(5);
                }
            });
            long allocated = GC.GetTotalAllocatedBytes(true);
            var clock = Stopwatch.StartNew();
            CookResult result;
            try { result = new CookSession(project, new CookSettings { Jobs = jobs, UseCache = cache, OutputPath = Path.Combine(root, "output") }).Run(); }
            finally { stop.Cancel(); sampler.GetAwaiter().GetResult(); }
            clock.Stop();
            if (!result.Succeeded) throw new Exception(string.Join("\n", result.Diagnostics));
            allocated = GC.GetTotalAllocatedBytes(true) - allocated;
            long retained = GC.GetTotalMemory(true) - heapStart;
            var payload = PackPayload.FromFile(result.OutputPath!);
            if (expected is { } hash && hash != payload.Hash) throw new Exception("Pack byte identity changed.");
            expected = payload.Hash;
            Console.WriteLine($"{name} jobs={jobs} elapsed={clock.Elapsed.TotalSeconds:F3}s pack={payload.Length} hash={payload.Hash:X32} private peak={peak/1048576d:F2} MiB increase={(peak-initial)/1048576d:F2}; heap peak={heapPeak/1048576d:F2} MiB retained delta={retained/1048576d:F2} MiB allocated={allocated/1048576d:F2} MiB");
        }
    }
}
