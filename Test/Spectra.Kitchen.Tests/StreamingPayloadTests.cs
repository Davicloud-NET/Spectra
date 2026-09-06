using Spectra.Kitchen.Cache;
using Spectra.Kitchen.Cooking;
using Spectra.Kitchen.Packs;
using SpectraEngine.Core.Assets.Packs;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Spectra.Kitchen.Tests;

public class StreamingPayloadTests
{
    [Fact]
    public void Concurrent_identical_emissions_and_streaming_readers_share_one_valid_cache_payload()
    {
        using var project = new TempProject();
        var store = new ContentStore(project.Root);
        byte[] bytes = TempProject.Bytes(256 * 1024, 37);
        Parallel.For(0, 128, new ParallelOptions { MaxDegreeOfParallelism = 8 }, _ =>
        {
            var payload = store.PutPayload(bytes);
            payload.CopyTo(Stream.Null);
        });
        store.TryGetPayload(System.IO.Hashing.XxHash128.HashToUInt128(bytes), out var result).ShouldBeTrue();
        result.ReadAllBytes().ShouldBe(bytes);
        Directory.GetFiles(store.Root, "*.t*", SearchOption.AllDirectories).ShouldBeEmpty();
    }
    [Theory]
    [InlineData(PackCodec.None)]
    [InlineData(PackCodec.Deflate)]
    public void File_and_span_payloads_produce_identical_repeatable_packs(PackCodec codec)
    {
        using var project = new TempProject();
        byte[] bytes = TempProject.Bytes(1024 * 1024 + 17, 23);
        string file = Path.Combine(project.Root, "payload");
        File.WriteAllBytes(file, bytes);
        using var span = new PackWriter();
        using var streamed = new PackWriter();
        span.Add("b.raw", PackEntryKind.Raw, bytes, codec);
        span.Add("a.raw", PackEntryKind.Raw, bytes.AsSpan(0, 53));
        streamed.Add("a.raw", PackEntryKind.Raw, bytes.AsSpan(0, 53));
        streamed.AddFile("b.raw", PackEntryKind.Raw, file, codec);
        byte[] expected = Write(span);
        Write(streamed).ShouldBe(expected);
        Write(streamed).ShouldBe(expected);
    }

    [Fact]
    public void Changed_source_and_cancellation_preserve_the_previous_output_and_remove_temporaries()
    {
        using var project = new TempProject();
        string source = Path.Combine(project.Root, "payload");
        string output = Path.Combine(project.Root, "out.spack");
        byte[] original = [1, 2, 3];
        File.WriteAllBytes(source, [4, 5, 6]);
        File.WriteAllBytes(output, original);
        using var writer = new PackWriter();
        writer.AddFile("source.raw", PackEntryKind.Raw, source);
        File.WriteAllBytes(source, [4, 9, 6]); // Same length, wrong identity.
        Should.Throw<IOException>(() => writer.WriteToFile(output));
        File.ReadAllBytes(output).ShouldBe(original);
        Directory.GetFiles(project.Root, "*.tmp-*").ShouldBeEmpty();
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        Should.Throw<OperationCanceledException>(() => writer.WriteToFile(output, cancelled.Token));
        File.ReadAllBytes(output).ShouldBe(original);
        Directory.GetFiles(project.Root, "*.tmp-*").ShouldBeEmpty();
    }

    [Fact]
    public void Owned_memory_mutation_is_detected_and_cancelled_spooling_leaves_no_partial_cache_file()
    {
        using var project = new TempProject();
        byte[] bytes = [1, 2, 3];
        PackPayload payload = PackPayload.FromBytes(bytes);
        bytes[1] = 9;
        Should.Throw<IOException>(() => payload.CopyTo(Stream.Null));
        var store = new ContentStore(project.Root);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        Should.Throw<OperationCanceledException>(() => store.PutPayload(payload, cancelled.Token));
        Directory.GetFiles(store.Root, "*", SearchOption.AllDirectories).ShouldBeEmpty();
    }

    [Fact]
    [Trait("Suite", "Determinism")]
    public void Serial_parallel_cold_warm_and_session_spools_match_and_loose_output_matches_source()
    {
        using var project = new TempProject();
        for (int i = 0; i < 9; i++) project.WriteAsset($"raw/{i}.blob", TempProject.Bytes(200_003 + i, (byte)i));
        byte[]? baseline = null;
        foreach (var (jobs, cache) in new[] { (1, true), (4, true), (4, false), (1, false) })
        {
            string output = Path.Combine(project.Root, "result.spack");
            var result = new CookSession(project.Layout, new CookSettings { Jobs = jobs, UseCache = cache, OutputPath = output }).Run();
            result.Succeeded.ShouldBeTrue();
            byte[] actual = File.ReadAllBytes(result.OutputPath!);
            if (baseline is null) baseline = actual;
            else actual.ShouldBe(baseline);
        }
        string loose = Path.Combine(project.Root, "loose");
        new CookSession(project.Layout, new CookSettings { Loose = true, OutputPath = loose }).Run().Succeeded.ShouldBeTrue();
        for (int i = 0; i < 9; i++)
            File.ReadAllBytes(Path.Combine(loose, "raw", $"{i}.blob")).ShouldBe(TempProject.Bytes(200_003 + i, (byte)i));
    }

    private static byte[] Write(PackWriter writer)
    {
        using var output = new MemoryStream();
        writer.Write(output);
        return output.ToArray();
    }
}

