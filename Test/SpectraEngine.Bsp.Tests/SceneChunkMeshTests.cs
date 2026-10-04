using System.Diagnostics;
using System.Numerics;
using SpectraEngine.Core.Bsp;
using SpectraEngine.Core.Scene;

namespace SpectraEngine.Bsp.Tests;

/// <summary>
/// The chunk mesh swap: a landed compile creates and destroys GPU meshes only for
/// changed cells, and a failed swap leaves the previous world renderable.
/// </summary>
// With a 2-unit cube brush, (16,16,16) is mid-cell (0,0,0), (80,16,16) mid-cell
// (2,0,0) and (144,16,16) mid-cell (4,0,0).
public sealed class SceneChunkMeshTests
{
    [Fact]
    public void Editing_one_brush_swaps_only_its_cells_gpu_mesh()
    {
        var (scene, renderer, logger) = CreateScene();
        SceneNode edited = AddUnitBoxNode(scene, "edited", new Vector3(16f, 16f, 16f)); // cell (0,0,0)
        AddUnitBoxNode(scene, "mid", new Vector3(80f, 16f, 16f));                       // cell (2,0,0)
        AddUnitBoxNode(scene, "far", new Vector3(144f, 16f, 16f));                      // cell (4,0,0)

        PumpUntil(scene, renderer, logger, () => scene.StaticWorldCompileCount == 1);
        renderer.CreatedMeshes.Count.ShouldBe(3);
        scene.StaticWorldChunkMeshes.Count.ShouldBe(3);
        scene.TryGetStaticWorldChunkMesh(new ChunkCoord(0, 0, 0), out StaticWorldChunkMesh editedBefore).ShouldBeTrue();
        scene.TryGetStaticWorldChunkMesh(new ChunkCoord(2, 0, 0), out StaticWorldChunkMesh midBefore).ShouldBeTrue();
        scene.TryGetStaticWorldChunkMesh(new ChunkCoord(4, 0, 0), out StaticWorldChunkMesh farBefore).ShouldBeTrue();

        edited.LocalPosition = new Vector3(18f, 16f, 16f); // still mid-cell (0,0,0)
        PumpUntil(scene, renderer, logger, () => scene.StaticWorldCompileCount == 2);

        scene.StaticWorld.ShouldNotBeNull().MeshStats.ShouldBe(new CsgMeshStats(Reused: 2, Built: 1));
        renderer.CreatedMeshes.Count.ShouldBe(4);
        editedBefore.SingleFakeMesh().Disposed.ShouldBeTrue();

        scene.TryGetStaticWorldChunkMesh(new ChunkCoord(0, 0, 0), out StaticWorldChunkMesh editedAfter).ShouldBeTrue();
        editedAfter.SingleMesh().ShouldBeSameAs(renderer.CreatedMeshes[3]);
        editedAfter.SingleMesh().ShouldNotBeSameAs(editedBefore.SingleMesh());

        scene.TryGetStaticWorldChunkMesh(new ChunkCoord(2, 0, 0), out StaticWorldChunkMesh midAfter).ShouldBeTrue();
        midAfter.SingleMesh().ShouldBeSameAs(midBefore.SingleMesh());
        midAfter.Artifact.ShouldBeSameAs(midBefore.Artifact);
        midBefore.SingleFakeMesh().Disposed.ShouldBeFalse();

        scene.TryGetStaticWorldChunkMesh(new ChunkCoord(4, 0, 0), out StaticWorldChunkMesh farAfter).ShouldBeTrue();
        farAfter.SingleMesh().ShouldBeSameAs(farBefore.SingleMesh());
        farBefore.SingleFakeMesh().Disposed.ShouldBeFalse();
    }

    [Fact]
    public void Moving_a_brush_across_a_border_creates_the_new_cell_and_removes_the_emptied_one()
    {
        var (scene, renderer, logger) = CreateScene();
        SceneNode mover = AddUnitBoxNode(scene, "mover", new Vector3(16f, 16f, 16f)); // cell (0,0,0)
        AddUnitBoxNode(scene, "anchor", new Vector3(80f, 16f, 16f));                  // cell (2,0,0)

        PumpUntil(scene, renderer, logger, () => scene.StaticWorldCompileCount == 1);
        scene.TryGetStaticWorldChunkMesh(new ChunkCoord(0, 0, 0), out StaticWorldChunkMesh oldCell).ShouldBeTrue();
        scene.TryGetStaticWorldChunkMesh(new ChunkCoord(2, 0, 0), out StaticWorldChunkMesh anchorBefore).ShouldBeTrue();

        mover.LocalPosition = new Vector3(48f, 16f, 16f); // into cell (1,0,0)
        PumpUntil(scene, renderer, logger, () => scene.StaticWorldCompileCount == 2);

        scene.StaticWorldChunkMeshes.Count.ShouldBe(2);
        scene.TryGetStaticWorldChunkMesh(new ChunkCoord(0, 0, 0), out _).ShouldBeFalse();
        oldCell.SingleFakeMesh().Disposed.ShouldBeTrue();
        scene.TryGetStaticWorldChunkMesh(new ChunkCoord(1, 0, 0), out _).ShouldBeTrue();
        scene.TryGetStaticWorldChunkMesh(new ChunkCoord(2, 0, 0), out StaticWorldChunkMesh anchorAfter).ShouldBeTrue();
        anchorAfter.SingleMesh().ShouldBeSameAs(anchorBefore.SingleMesh());
        anchorBefore.SingleFakeMesh().Disposed.ShouldBeFalse();
    }

    [Fact]
    public void Failed_chunk_mesh_creation_keeps_every_old_chunk_renderable_and_recovers()
    {
        var (scene, renderer, logger) = CreateScene();
        SceneNode a = AddUnitBoxNode(scene, "a", new Vector3(16f, 16f, 16f));   // cell (0,0,0)
        AddUnitBoxNode(scene, "b", new Vector3(80f, 16f, 16f));                 // cell (2,0,0)
        SceneNode c = AddUnitBoxNode(scene, "c", new Vector3(144f, 16f, 16f));  // cell (4,0,0)

        PumpUntil(scene, renderer, logger, () => scene.StaticWorldCompileCount == 1);
        CsgWorld previousWorld = scene.StaticWorld.ShouldNotBeNull();
        StaticWorldChunkMesh[] before = [.. scene.StaticWorldChunkMeshes];
        before.Length.ShouldBe(3);

        // Two dirty cells, budget for one: the second CreateMesh throws after
        // the first replacement already exists.
        a.LocalPosition = new Vector3(18f, 16f, 16f);
        c.LocalPosition = new Vector3(146f, 16f, 16f);
        renderer.CreateMeshBudget = 1;
        PumpUntilCreateMeshFails(scene, renderer, logger);

        scene.StaticWorld.ShouldBeSameAs(previousWorld);
        scene.StaticWorldChunkMeshes.Count.ShouldBe(3);
        for (int i = 0; i < before.Length; i++)
        {
            scene.StaticWorldChunkMeshes[i].SingleMesh().ShouldBeSameAs(before[i].SingleMesh());
            before[i].SingleFakeMesh().Disposed.ShouldBeFalse();
        }

        // The replacement that was created is rolled back, not leaked.
        renderer.CreatedMeshes.Count.ShouldBe(4);
        renderer.CreatedMeshes[3].Disposed.ShouldBeTrue();

        renderer.CreateMeshBudget = int.MaxValue;
        scene.MarkStaticWorldDirty();
        PumpUntil(scene, renderer, logger, () => scene.StaticWorldCompileCount == 2);

        scene.StaticWorldChunkMeshes.Count.ShouldBe(3);
        before[0].SingleFakeMesh().Disposed.ShouldBeTrue(); // cell (0,0,0)
        before[2].SingleFakeMesh().Disposed.ShouldBeTrue(); // cell (4,0,0)
        scene.TryGetStaticWorldChunkMesh(new ChunkCoord(2, 0, 0), out StaticWorldChunkMesh untouched).ShouldBeTrue();
        untouched.SingleMesh().ShouldBeSameAs(before[1].SingleMesh());
        untouched.SingleFakeMesh().Disposed.ShouldBeFalse();
    }

    [Fact]
    public void Failed_swap_restores_the_consumed_dirty_cells_for_the_next_compile()
    {
        // If a failed swap dropped the dirty cells it consumed, the published
        // world would keep stale geometry for those edits.
        var (scene, renderer, logger) = CreateScene();
        SceneNode a = AddUnitBoxNode(scene, "a", new Vector3(16f, 16f, 16f));   // cell (0,0,0)
        AddUnitBoxNode(scene, "b", new Vector3(80f, 16f, 16f));                 // cell (2,0,0)
        SceneNode c = AddUnitBoxNode(scene, "c", new Vector3(144f, 16f, 16f));  // cell (4,0,0)
        PumpUntil(scene, renderer, logger, () => scene.StaticWorldCompileCount == 1);

        a.LocalPosition = new Vector3(18f, 16f, 16f);
        c.LocalPosition = new Vector3(146f, 16f, 16f);
        renderer.CreateMeshBudget = 1;
        PumpUntilCreateMeshFails(scene, renderer, logger);

        // No further edits: an unchanged scene would report an empty dirty
        // set, so these cells can only come from the failed swap.
        renderer.CreateMeshBudget = int.MaxValue;
        scene.MarkStaticWorldDirty();
        PumpUntil(scene, renderer, logger, () => scene.StaticWorldCompileCount == 2);

        scene.LastCompileDirtyCells.ShouldBe(
            new[] { new ChunkCoord(0, 0, 0), new ChunkCoord(4, 0, 0) });

        CsgWorld world = scene.StaticWorld.ShouldNotBeNull();
        world.ContainsPoint(new Vector3(18f, 16f, 16f)).ShouldBeTrue();
        world.ContainsPoint(new Vector3(146f, 16f, 16f)).ShouldBeTrue();
        world.ContainsPoint(new Vector3(15.5f, 16f, 16f)).ShouldBeFalse(); // vacated by a's move
    }

    private static (Scene Scene, FakeRenderer Renderer, CapturingLogger Logger) CreateScene() =>
        (new Scene("Test"), new FakeRenderer(), new CapturingLogger());

    // A 2-unit cube. Stays inside one cell unless it is within a half extent
    // plus the weld band of a border.
    private static SceneNode AddUnitBoxNode(Scene scene, string name, Vector3 position)
    {
        SceneNode node = scene.Root.CreateChild(name);
        node.LocalPosition = position;
        node.Brush = Brush.CreateBox(new Vector3(-1f), new Vector3(1f));
        return node;
    }

    private static readonly TimeSpan CompileTimeout = TimeSpan.FromSeconds(30);

    private static void PumpUntil(Scene scene, FakeRenderer renderer, CapturingLogger logger, Func<bool> condition)
    {
        var stopwatch = Stopwatch.StartNew();
        while (true)
        {
            scene.ProcessStaticWorldCompilation(renderer, logger);
            if (condition())
                return;
            if (stopwatch.Elapsed > CompileTimeout)
                throw new TimeoutException(
                    $"Timed out waiting for a compile to land. Captured log:{Environment.NewLine}{logger.Describe()}");
            Thread.Sleep(1);
        }
    }

    private static void PumpUntilCreateMeshFails(Scene scene, FakeRenderer renderer, CapturingLogger logger)
    {
        var stopwatch = Stopwatch.StartNew();
        while (true)
        {
            try
            {
                scene.ProcessStaticWorldCompilation(renderer, logger);
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("Simulated CreateMesh failure"))
            {
                return;
            }
            if (stopwatch.Elapsed > CompileTimeout)
                throw new TimeoutException(
                    $"Timed out waiting for the simulated CreateMesh failure. Captured log:{Environment.NewLine}{logger.Describe()}");
            Thread.Sleep(1);
        }
    }
}
