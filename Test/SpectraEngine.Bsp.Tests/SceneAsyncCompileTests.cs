using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Numerics;
using SpectraEngine.Core.Bsp;
using SpectraEngine.Core.Scene;

namespace SpectraEngine.Bsp.Tests;

/// <summary>
/// The async static-world pipeline behind <see cref="Scene.ProcessStaticWorldCompilation"/>.
/// </summary>
// The test thread plays the render thread and pumps once per "frame"; only the
// CSG compile runs off-thread. Worlds are swapped in by pump calls on this
// thread, so no assertion depends on how fast the pool is.
public sealed class SceneAsyncCompileTests
{
    [Fact]
    public void Dirty_scene_launches_a_background_compile_and_swaps_the_world_in()
    {
        var (scene, _, renderer, logger) = CreateSceneWithBrushNode();
        scene.StaticWorldDirty.ShouldBeTrue();
        scene.StaticWorldCompileCount.ShouldBe(0);

        scene.ProcessStaticWorldCompilation(renderer, logger);

        // Launched, not run: the result is only harvested on a later pump call.
        scene.StaticWorldDirty.ShouldBeFalse();
        scene.StaticWorld.ShouldBeNull();
        scene.StaticWorldCompileCount.ShouldBe(0);

        PumpUntil(scene, renderer, logger,
            () => scene.StaticWorldCompileCount == 1, "the first background compile to land");

        scene.StaticWorld.ShouldNotBeNull();
        scene.StaticWorld.Surfaces.ShouldNotBeEmpty();
        StaticWorldChunkMesh chunk = scene.StaticWorldChunkMeshes.ShouldHaveSingleItem();
        renderer.CreatedMeshes.Count.ShouldBe(1);
        chunk.SingleMesh().ShouldBeSameAs(renderer.CreatedMeshes[0]);
        renderer.CreatedMeshes[0].VertexData.ShouldNotBeEmpty();
        scene.StaticWorldDirty.ShouldBeFalse();
    }

    [Fact]
    public void Edits_during_an_in_flight_compile_coalesce_into_one_follow_up_compile()
    {
        var (scene, node, renderer, logger) = CreateSceneWithBrushNode();
        scene.ProcessStaticWorldCompilation(renderer, logger); // snapshots at the origin

        var moved = new Vector3(5f, 0f, 0f);
        node.LocalPosition = moved;
        scene.StaticWorldDirty.ShouldBeTrue();

        // The first compile read its snapshot, not the live node.
        PumpUntil(scene, renderer, logger,
            () => scene.StaticWorldCompileCount == 1, "the origin-snapshot compile to land");
        scene.StaticWorld.ShouldNotBeNull();
        scene.StaticWorld.Placements[0].Transform.Translation.ShouldBe(Vector3.Zero);

        PumpUntil(scene, renderer, logger,
            () => scene.StaticWorldCompileCount == 2, "the follow-up compile to land");
        scene.StaticWorld.ShouldNotBeNull();
        scene.StaticWorld.Placements[0].Transform.Translation.ShouldBe(moved);
        scene.StaticWorldDirty.ShouldBeFalse();

        scene.ProcessStaticWorldCompilation(renderer, logger);
        scene.StaticWorldCompileCount.ShouldBe(2);
    }

    [Fact]
    public void Defective_snapshot_logs_once_keeps_the_previous_world_and_recovers()
    {
        var (scene, node, renderer, logger) = CreateSceneWithBrushNode();
        PumpUntil(scene, renderer, logger,
            () => scene.StaticWorldCompileCount == 1, "the initial compile to land");
        CsgWorld previousWorld = scene.StaticWorld.ShouldNotBeNull();
        FakeMesh previousMesh = scene.StaticWorldChunkMeshes.ShouldHaveSingleItem().SingleFakeMesh();

        node.LocalScale = new Vector3(2f, 1f, 1f); // non-rigid
        scene.StaticWorldDirty.ShouldBeTrue();

        Should.NotThrow(() => scene.ProcessStaticWorldCompilation(renderer, logger));
        logger.MessagesAt(LogLevel.Error).ShouldHaveSingleItem().ShouldContain("non-rigid");
        scene.StaticWorld.ShouldBeSameAs(previousWorld);
        scene.StaticWorldChunkMeshes.ShouldHaveSingleItem().SingleMesh().ShouldBeSameAs(previousMesh);
        previousMesh.Disposed.ShouldBeFalse();
        scene.StaticWorldCompileCount.ShouldBe(1);
        scene.StaticWorldDirty.ShouldBeFalse(); // handled, so no retry

        for (int i = 0; i < 5; i++)
            scene.ProcessStaticWorldCompilation(renderer, logger);
        logger.MessagesAt(LogLevel.Error).Count.ShouldBe(1);
        scene.StaticWorldCompileCount.ShouldBe(1);

        // The recovered geometry matches the pre-defect snapshot, so the caches
        // validate and the chunk's GPU mesh is carried, not rebuilt.
        node.LocalScale = Vector3.One;
        scene.StaticWorldDirty.ShouldBeTrue();
        PumpUntil(scene, renderer, logger,
            () => scene.StaticWorldCompileCount == 2, "the recovery compile to land");
        scene.StaticWorld.ShouldNotBeNull();
        scene.StaticWorld.ShouldNotBeSameAs(previousWorld);
        scene.StaticWorldChunkMeshes.ShouldHaveSingleItem().SingleMesh().ShouldBeSameAs(previousMesh);
        previousMesh.Disposed.ShouldBeFalse();
        renderer.CreatedMeshes.Count.ShouldBe(1);
    }

    [Fact]
    public void Pump_on_a_clean_scene_does_nothing()
    {
        var scene = new Scene("Test");
        scene.Root.CreateChild("plain");
        var renderer = new FakeRenderer();
        var logger = new CapturingLogger();

        scene.ProcessStaticWorldCompilation(renderer, logger);

        scene.StaticWorldCompileCount.ShouldBe(0);
        scene.StaticWorld.ShouldBeNull();
        renderer.CreatedMeshes.ShouldBeEmpty();
    }

    [Fact]
    public void Dirty_scene_without_brush_nodes_clears_the_world_without_compiling()
    {
        // No brushes, no carve: the pump resolves this synchronously.
        var scene = new Scene("Test");
        var renderer = new FakeRenderer();
        var logger = new CapturingLogger();
        scene.MarkStaticWorldDirty();

        scene.ProcessStaticWorldCompilation(renderer, logger);

        scene.StaticWorldCompileCount.ShouldBe(1);
        scene.StaticWorld.ShouldBeNull();
        scene.StaticWorldDirty.ShouldBeFalse();
        renderer.CreatedMeshes.ShouldBeEmpty();
    }

    [Fact]
    public void Detaching_the_last_brush_clears_the_world_and_destroys_its_mesh()
    {
        var (scene, node, renderer, logger) = CreateSceneWithBrushNode();
        PumpUntil(scene, renderer, logger,
            () => scene.StaticWorldCompileCount == 1, "the initial compile to land");
        FakeMesh mesh = scene.StaticWorldChunkMeshes.ShouldHaveSingleItem().SingleFakeMesh();

        node.Brush = null;
        scene.StaticWorldDirty.ShouldBeTrue();

        // The no-brushes path is synchronous, so one pump call is enough.
        scene.ProcessStaticWorldCompilation(renderer, logger);

        scene.StaticWorldCompileCount.ShouldBe(2);
        scene.StaticWorld.ShouldBeNull();
        scene.StaticWorldChunkMeshes.ShouldBeEmpty();
        mesh.Disposed.ShouldBeTrue();
    }

    [Fact]
    public void Recompiling_an_unchanged_scene_reuses_every_chunk_gpu_mesh()
    {
        // Overlapping, so the compile does a real carve.
        var scene = new Scene("Test");
        SceneNode a = scene.Root.CreateChild("a");
        a.Brush = Brush.CreateBox(new Vector3(-1f, -1f, -1f), new Vector3(1f, 1f, 1f));
        SceneNode b = scene.Root.CreateChild("b");
        b.LocalPosition = new Vector3(1.25f, 0.5f, 0f);
        b.Brush = Brush.CreateBox(new Vector3(-1f, -1f, -1f), new Vector3(1f, 1f, 1f));
        var renderer = new FakeRenderer();
        var logger = new CapturingLogger();

        PumpUntil(scene, renderer, logger,
            () => scene.StaticWorldCompileCount == 1, "the first compile to land");
        int createdAfterFirst = renderer.CreatedMeshes.Count;
        FakeMesh[] firstMeshes = [.. scene.StaticWorldChunkMeshes.Select(c => c.SingleFakeMesh())];
        firstMeshes.ShouldNotBeEmpty();

        scene.MarkStaticWorldDirty(); // identical snapshot
        PumpUntil(scene, renderer, logger,
            () => scene.StaticWorldCompileCount == 2, "the second compile to land");

        renderer.CreatedMeshes.Count.ShouldBe(createdAfterFirst);
        scene.StaticWorldChunkMeshes.Count.ShouldBe(firstMeshes.Length);
        for (int i = 0; i < firstMeshes.Length; i++)
        {
            scene.StaticWorldChunkMeshes[i].SingleMesh().ShouldBeSameAs(firstMeshes[i]);
            firstMeshes[i].Disposed.ShouldBeFalse();
        }
    }

    [Fact]
    public void Weld_cache_travels_through_the_pump_so_an_edit_reuses_far_welds()
    {
        // Many cells apart, so editing one leaves the other's carve and weld cached.
        var scene = new Scene("Test");
        SceneNode near = scene.Root.CreateChild("near");
        near.LocalPosition = new Vector3(16f, 16f, 16f);
        near.Brush = Brush.CreateBox(new Vector3(-1f, -1f, -1f), new Vector3(1f, 1f, 1f));
        SceneNode far = scene.Root.CreateChild("far");
        far.LocalPosition = new Vector3(200f, 16f, 16f);
        far.Brush = Brush.CreateBox(new Vector3(-1f, -1f, -1f), new Vector3(1f, 1f, 1f));
        var renderer = new FakeRenderer();
        var logger = new CapturingLogger();

        PumpUntil(scene, renderer, logger,
            () => scene.StaticWorldCompileCount == 1, "the first compile to land");
        CsgWorld first = scene.StaticWorld.ShouldNotBeNull();
        first.CacheStats.ShouldBe(new CsgCacheStats(Hits: 0, Misses: 2));
        first.WeldStats.ShouldBe(new CsgWeldStats(Reused: 0, Welded: 2));

        near.LocalPosition = new Vector3(18f, 16f, 16f);
        PumpUntil(scene, renderer, logger,
            () => scene.StaticWorldCompileCount == 2, "the post-edit compile to land");

        CsgWorld second = scene.StaticWorld.ShouldNotBeNull();
        second.CacheStats.ShouldBe(new CsgCacheStats(Hits: 1, Misses: 1));
        second.WeldStats.ShouldBe(new CsgWeldStats(Reused: 1, Welded: 1));
    }

    [Fact]
    public void A_continuously_dragged_brush_never_renders_an_older_placement_than_one_already_shown()
    {
        // Compiled geometry may trail the node but must never go backwards, or a
        // dragged brush visibly jitters.
        // Random interleaving, fixed seed: a 1:1 edit/pump rhythm would only
        // probe one phase of the race.
        var (scene, node, renderer, logger) = CreateSceneWithBrushNode();
        var random = new Random(20260821);

        const float Step = 0.05f;
        const int Frames = 400;
        const int WantedSwaps = 8;
        float authored = 0f;
        float lastRendered = float.NegativeInfinity;
        int backwards = 0;
        int swaps = 0;

        // A slow machine can run 400 frames before a second compile lands, so
        // keep dragging until enough swaps were seen.
        var stopwatch = Stopwatch.StartNew();
        for (int frame = 0; frame < Frames || swaps < WantedSwaps; frame++)
        {
            if (stopwatch.Elapsed > CompileTimeout)
                break;

            // 0 to 2 edits per frame, so the drag can outrun the compiles.
            int edits = random.Next(0, 3);
            for (int i = 0; i < edits; i++)
            {
                authored += Step;
                node.LocalPosition = new Vector3(authored, 0f, 0f);
            }

            scene.ProcessStaticWorldCompilation(renderer, logger);

            // Sometimes let the pool finish, to cover a result waiting to be harvested.
            if (random.Next(0, 5) == 0)
                Thread.Sleep(1);

            if (scene.StaticWorld is not { } world)
                continue;

            float rendered = world.Placements[0].Transform.Translation.X;
            if (rendered != lastRendered)
                swaps++;
            if (rendered < lastRendered)
                backwards++;
            lastRendered = rendered;

            // It may never lead the node either.
            rendered.ShouldBeLessThanOrEqualTo(authored);
        }

        backwards.ShouldBe(0, $"a swap published an older placement than one already rendered ({swaps} swaps seen)");
        swaps.ShouldBeGreaterThan(1,
            $"the drag never actually recompiled ({scene.StaticWorldCompileCount} compiles landed), " +
            $"so nothing was proved. Captured log:{Environment.NewLine}{logger.Describe()}");

        // Once the edits stop, the world catches up on its own.
        PumpUntil(scene, renderer, logger,
            () => scene.StaticWorld is { } settled &&
                  settled.Placements[0].Transform.Translation.X == authored,
            "the compiled world to catch up with the final drag position");
    }

    // Only a hung or lost compile reaches this, so it never slows a passing run.
    private static readonly TimeSpan CompileTimeout = TimeSpan.FromSeconds(30);

    private static void PumpUntil(
        Scene scene, FakeRenderer renderer, CapturingLogger logger,
        Func<bool> condition, string description)
    {
        var stopwatch = Stopwatch.StartNew();
        while (true)
        {
            scene.ProcessStaticWorldCompilation(renderer, logger);
            if (condition())
                return;
            if (stopwatch.Elapsed > CompileTimeout)
                throw new TimeoutException(
                    $"Timed out waiting for {description}. Captured log:{Environment.NewLine}{logger.Describe()}");
            Thread.Sleep(1);
        }
    }

    private static (Scene Scene, SceneNode Node, FakeRenderer Renderer, CapturingLogger Logger) CreateSceneWithBrushNode()
    {
        var scene = new Scene("Test");
        SceneNode node = scene.Root.CreateChild("brush");
        node.Brush = Brush.CreateBox(new Vector3(-1f, -1f, -1f), new Vector3(1f, 1f, 1f));
        return (scene, node, new FakeRenderer(), new CapturingLogger());
    }
}
