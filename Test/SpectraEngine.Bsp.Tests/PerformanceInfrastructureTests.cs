using System;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Collections.Generic;
using SpectraEngine.Core.Bsp;
using SpectraEngine.Core.Diagnostics;
using SpectraEngine.Core.Scene;

namespace SpectraEngine.Bsp.Tests;

public sealed class PerformanceInfrastructureTests
{
    [Fact]
    public void Carve_scratch_is_exclusively_leased_and_discards_large_retained_storage()
    {
        var first = Csg.CarveScratch.Rent();
        using var nested = Csg.CarveScratch.Rent();
        nested.ShouldNotBeSameAs(first);
        first.Current.Capacity = 5000;
        first.Current.Add(Brush.CreateBox(new Vector3(-1), new Vector3(1)).LocalFaces[0]);
        first.EnsureCarverCapacity(5000, 5000);
        first.Dispose();
        using var reused = Csg.CarveScratch.Rent();
        reused.ShouldBeSameAs(first);
        reused.Current.Count.ShouldBe(0);
        reused.Current.Capacity.ShouldBe(0);
        reused.Carvers.ShouldBeEmpty();
        reused.PlaneBuffer.ShouldBeEmpty();
    }

    [Fact]
    public void Drawable_index_tracks_kind_parent_motion_and_cross_scene_ownership()
    {
        var scene = new Scene();
        var other = new Scene();
        var parent = scene.Root.CreateChild("parent");
        var node = parent.CreateChild("brush");
        node.Brush = Brush.CreateBox(new Vector3(-.5f), new Vector3(.5f));
        node.LocalPosition = new Vector3(0, 0, -5);
        scene.DrawableBvh.LeafCount.ShouldBe(0);
        node.BrushKind = BrushKind.Part;
        scene.DrawableBvh.LeafCount.ShouldBe(1);
        var found = new List<SceneNode>();
        var frustum = scene.Camera.GetFrustum();
        scene.DrawableBvh.QueryFrustum(frustum, found);
        found.ShouldBe(new[] { node });
        parent.LocalPosition = new Vector3(0, 0, 20);
        found.Clear();
        scene.DrawableBvh.QueryFrustum(frustum, found);
        found.ShouldBeEmpty();
        other.Root.AddChild(parent);
        scene.DrawableBvh.LeafCount.ShouldBe(0);
        other.DrawableBvh.LeafCount.ShouldBe(1);
        node.BrushKind = BrushKind.World;
        other.DrawableBvh.LeafCount.ShouldBe(0);
        scene.Bvh.Validate();
        scene.DrawableBvh.Validate();
        other.DrawableBvh.Validate();
    }

    [Fact]
    public void Part_mesh_lifetime_follows_final_reference_counts_and_survives_device_recreation()
    {
        var cache = new PartBrushMeshCache();
        var renderer = new FakeRenderer();
        var brush = Brush.CreateBox(new Vector3(-1), new Vector3(1));
        var first = new SceneNode();
        var second = new SceneNode();
        cache.SetReference(first, brush);
        cache.SetReference(second, brush);
        cache.Pump(renderer, _ => null);
        renderer.CreatedMeshes.Count.ShouldBe(1);
        cache.PendingCount.ShouldBe(0);
        cache.SetReference(first, null);
        cache.SetReference(first, brush);
        cache.Pump(renderer, _ => null);
        renderer.CreatedMeshes.Count.ShouldBe(1);
        cache.ReleaseGraphicsResources(renderer);
        cache.Count.ShouldBe(0);
        cache.Pump(renderer, _ => null);
        renderer.CreatedMeshes.Count.ShouldBe(2);
        cache.SetReference(first, null);
        cache.SetReference(second, null);
        cache.Pump(renderer, _ => null);
        cache.Count.ShouldBe(0);
    }

    [Fact]
    public void Gpu_ring_drops_instrumentation_until_queries_complete()
    {
        using var timer = new FakeGpuTimer();
        for (int i = 0; i < 12; i++) { timer.BeginFrame(); timer.EndFrame(); }
        timer.DroppedFrames.ShouldBe(4);
        timer.Snapshot.Available.ShouldBeFalse();
        timer.Ready = true;
        timer.BeginFrame();
        timer.Snapshot.Available.ShouldBeTrue();
        timer.Snapshot.Frame.ShouldBe(8);
        timer.Snapshot.Total.ShouldBe(2);
        timer.EndFrame();
        timer.Invalid = true;
        timer.BeginFrame();
        timer.Snapshot.Available.ShouldBeFalse();
        timer.EndFrame();
    }

    private sealed class FakeGpuTimer : GpuTimestampTimer
    {
        internal bool Ready, Invalid;
        protected override void WriteTimestamp(int slot, int mark) { }
        protected override void EndQueries(int slot, int count) { }
        protected override bool TryRead(int slot, int count, ulong[] values, out ulong frequency)
        { frequency = Invalid ? 0UL : 1000UL; values[0] = 1; values[1] = 3; return Ready; }
        public override void Dispose() { }
    }

    [Fact]
    public void Ordered_membership_preserves_survivors_and_reappends_removed_items()
    {
        var list = new OrderedIdentityList<object>();
        object[] items = Enumerable.Range(0, 5000).Select(_ => new object()).ToArray();
        foreach (object item in items) list.Add(item).ShouldBeTrue();
        foreach (object item in items) list.Add(item).ShouldBeFalse();
        for (int i = 0; i < items.Length; i += 2) list.Remove(items[i]).ShouldBeTrue();
        list.Count.ShouldBe(2500);
        list.ToArray().ShouldBe(items.Where((_, i) => i % 2 == 1).ToArray());
        list[100].ShouldBeSameAs(items[201]);
        list.Add(items[0]).ShouldBeTrue();
        list[list.Count - 1].ShouldBeSameAs(items[0]);
        list.Compare(items[1], items[0]).ShouldBeLessThan(0);
    }

    [Fact]
    public void Nested_waits_are_exclusive_and_unaccounted_time_completes_the_frame()
    {
        var profiler = new FrameProfiler(1) { Enabled = true };
        long unit = Stopwatch.Frequency / 1000;
        profiler.BeginFrame(0);
        profiler.Open(FramePhase.Present, unit);
        profiler.Open(FramePhase.GpuWait, 3 * unit);
        profiler.Close(FramePhase.GpuWait, 7 * unit);
        profiler.Close(FramePhase.Present, 8 * unit);
        profiler.EndFrame(10 * unit);
        profiler[FramePhase.Present].ShouldBe(3, .01);
        profiler[FramePhase.GpuWait].ShouldBe(4, .01);
        profiler[FramePhase.Unaccounted].ShouldBe(3, .01);
        profiler.TotalMs.ShouldBe(10, .01);
        profiler.Snapshot().Max.ShouldBe(10, .01);
    }

    [Fact]
    public void Rolling_statistics_keep_stalls_then_expire_them_without_allocating()
    {
        var profiler = new FrameProfiler { Enabled = true };
        long unit = Stopwatch.Frequency / 1000;
        for (int i = 0; i < 2048; i++)
        {
            profiler.BeginFrame(0);
            profiler.EndFrame((i == 0 ? 100 : 1) * unit);
        }
        profiler.Snapshot().Max.ShouldBe(100, .01);
        profiler.Snapshot().P99.ShouldBe(1, .01);
        profiler.BeginFrame(0);
        profiler.EndFrame(unit);
        profiler.Snapshot().Max.ShouldBe(1, .01);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 10; i++) profiler.Snapshot();
        (GC.GetAllocatedBytesForCurrentThread() - before).ShouldBe(0);
    }
}
