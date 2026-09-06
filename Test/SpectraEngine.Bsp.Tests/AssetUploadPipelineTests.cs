using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using SpectraEngine.Core.Assets;

namespace SpectraEngine.Bsp.Tests;

public sealed class AssetUploadPipelineTests
{
    [Fact]
    public void Models_and_textures_share_bytes_and_receive_round_robin_steps()
    {
        var queue = NewQueue(128, 64, 16, 1);
        var order = new ConcurrentQueue<string>();
        var model = new Upload("model", 64, order);
        var texture = new Upload("texture", 64, order);
        queue.Queue(new object(), () => false, () => model, () => { });
        queue.Queue(new object(), () => false, () => texture, () => { });
        Await(() => queue.Snapshot.PendingUploads == 2);
        queue.Pump().ShouldBe(0);
        order.ToArray().ShouldBe(new[] { "model", "texture", "model", "texture" });
        queue.Snapshot.LastPumpBytes.ShouldBe(64);
        queue.Pump().ShouldBe(2);
        queue.Snapshot.QueuedPayloadBytes.ShouldBe(0);
        model.Disposals.ShouldBe(1); texture.Disposals.ShouldBe(1);
        queue.StopWorkers(); queue.ReleaseUploads();
    }

    [Fact]
    public void Oversized_payload_makes_progress_exclusively_and_cancellation_wakes_admission()
    {
        var queue = NewQueue(128, 64, 64, 2);
        var order = new ConcurrentQueue<string>();
        var large = new Upload("large", 256, order);
        queue.Queue(new object(), () => false, () => large, () => { });
        Await(() => queue.Snapshot.QueuedPayloadBytes == 256);
        var small = new Upload("small", 32, order);
        int discarded = 0;
        queue.Queue(new object(), () => false, () => small, () => Interlocked.Increment(ref discarded));
        for (int frame = 0; frame < 3; frame++) queue.Pump().ShouldBe(0);
        order.ShouldAllBe(name => name == "large");
        queue.Pump().ShouldBe(1);
        Await(() => queue.Snapshot.PendingUploads == 1);
        queue.Pump().ShouldBe(1);
        queue.Snapshot.PeakQueuedPayloadBytes.ShouldBe(256);
        queue.Snapshot.ActiveWorkers.ShouldBeInRange(0, 2);

        var blocked = new Upload("blocked", 256, order);
        queue.Queue(new object(), () => false, () => blocked, () => Interlocked.Increment(ref discarded));
        Await(() => queue.Snapshot.QueuedPayloadBytes == 256);
        queue.Queue(new object(), () => false, () => new Upload("waiting", 32, order), () => Interlocked.Increment(ref discarded));
        queue.StopWorkers(); queue.ReleaseUploads();
        queue.Snapshot.ActiveWorkers.ShouldBe(0);
        queue.Snapshot.QueuedPayloadBytes.ShouldBe(0);
        blocked.Disposals.ShouldBe(1);
        discarded.ShouldBe(1);
    }

    [Fact]
    public void Save_storm_coalesces_before_expensive_decode_and_drops_stale_ready_results()
    {
        var queue = NewQueue(1024, 128, 32, 1);
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var order = new ConcurrentQueue<string>();
        queue.Queue(new object(), () => false, () =>
        {
            entered.Set(); release.Wait(TimeSpan.FromSeconds(10)).ShouldBeTrue();
            return new Upload("blocker", 32, order);
        }, () => { });
        entered.Wait(TimeSpan.FromSeconds(10)).ShouldBeTrue();
        object key = new();
        int discarded = 0, decodes = 0;
        for (int i = 0; i < 100; i++)
        {
            queue.Queue(key, () => false, () =>
            {
                Interlocked.Increment(ref decodes);
                return new Upload("newest", 32, order);
            }, () => Interlocked.Increment(ref discarded));
        }
        release.Set();
        Await(() => queue.Snapshot.PendingUploads == 2);
        decodes.ShouldBe(1); discarded.ShouldBe(99);
        queue.Pump().ShouldBe(2);
        var obsolete = new Upload("obsolete", 64, order);
        queue.Queue(new object(), () => false, () => obsolete, () => { });
        Await(() => queue.Snapshot.PendingUploads == 1);
        obsolete.IsStale = true;
        queue.Pump().ShouldBe(0);
        obsolete.Disposals.ShouldBe(1);
        order.ShouldNotContain("obsolete");
        queue.StopWorkers(); queue.ReleaseUploads();
    }

    private static AssetUploadPipeline NewQueue(long memory, long frame, int step, int workers) =>
        new(new AssetUploadBudget { QueuedPayloadBytes = memory, BytesPerFrame = frame, BytesPerStep = step, DecodeWorkers = workers, CpuMilliseconds = 2000 });

    private static void Await(Func<bool> condition)
    {
        var timeout = Stopwatch.StartNew();
        while (!condition())
        {
            if (timeout.Elapsed.TotalSeconds > 10) throw new TimeoutException("Upload queue did not reach the expected state.");
            Thread.Sleep(1);
        }
    }

    private sealed class Upload(string name, long bytes, ConcurrentQueue<string> order) : IAssetUpload
    {
        private long _remaining = bytes;
        public long PayloadBytes => bytes;
        public bool IsStale { get; set; }
        internal int Disposals;
        public AssetUploadStep Step(int maxBytes)
        {
            order.Enqueue(name);
            int written = (int)Math.Min(_remaining, maxBytes);
            _remaining -= written;
            return new(written, _remaining == 0, _remaining == 0);
        }
        public void Dispose() => Disposals++;
    }
}
