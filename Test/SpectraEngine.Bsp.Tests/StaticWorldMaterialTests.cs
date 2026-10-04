using System.Diagnostics;
using System.Numerics;
using Microsoft.Extensions.Logging.Abstractions;
using SpectraEngine.Core.Assets;
using SpectraEngine.Core.Bsp;
using SpectraEngine.Core.Graphics;
using SpectraEngine.Core.Scene;
using static SpectraEngine.Bsp.Tests.SpatialTestHelpers;

namespace SpectraEngine.Bsp.Tests;

/// <summary>
/// The per-material render path: compile split, one GPU mesh per (cell, material),
/// one draw item per (visible chunk, material).
/// </summary>
// With a 2-unit cube brush, (10,10,10) and (20,20,20) are both in cell (0,0,0)
// and (80,16,16) is mid-cell (2,0,0).
public sealed class StaticWorldMaterialTests
{
    [Fact]
    public void A_cell_with_two_materials_produces_two_submeshes_and_one_material_produces_one()
    {
        (MaterialRef low, MaterialRef high) = InternPair();
        var scene = new Scene("Test");
        var renderer = new FakeRenderer();

        // "far" is the single-material control cell.
        AddBrushNode(scene, "a", new Vector3(10f, 10f, 10f), low);
        AddBrushNode(scene, "b", new Vector3(20f, 20f, 20f), high);
        AddBrushNode(scene, "far", new Vector3(80f, 16f, 16f), MaterialRef.Default);

        scene.RebuildStaticWorld(renderer);
        CsgWorld world = scene.StaticWorld.ShouldNotBeNull();

        ChunkMesh mixed = world.ChunkMeshes.Single(m => m.Coord == new ChunkCoord(0, 0, 0));
        mixed.Submeshes.Count.ShouldBe(2);
        mixed.Submeshes.Select(s => s.Material).ShouldBe(new[] { low, high });

        ChunkMesh uniform = world.ChunkMeshes.Single(m => m.Coord == new ChunkCoord(2, 0, 0));
        ChunkSubmesh only = uniform.Submeshes.ShouldHaveSingleItem();
        only.Material.IsDefault.ShouldBeTrue();
        only.Indices.Length.ShouldBeGreaterThan(0);

        int mixedIndices = mixed.Submeshes.Sum(s => s.Indices.Length);
        mixedIndices.ShouldBe(uniform.Submeshes.Sum(s => s.Indices.Length) * 2); // two identical boxes
        foreach (ChunkSubmesh submesh in mixed.Submeshes)
        {
            submesh.Indices.Length.ShouldBeGreaterThan(0);
            submesh.Indices.Max().ShouldBeLessThan((uint)(submesh.Vertices.Length / 8));
        }
    }

    [Fact]
    public void Submeshes_are_ordered_by_ascending_material_id_not_emission_order()
    {
        (MaterialRef low, MaterialRef high) = InternPair();
        var scene = new Scene("Test");

        // High-id brush first, so id order and emission order disagree.
        AddBrushNode(scene, "high", new Vector3(10f, 10f, 10f), high);
        AddBrushNode(scene, "low", new Vector3(20f, 20f, 20f), low);
        scene.RebuildStaticWorld(new FakeRenderer());

        ChunkMesh cell = scene.StaticWorld.ShouldNotBeNull().ChunkMeshes
            .Single(m => m.Coord == new ChunkCoord(0, 0, 0));
        cell.Submeshes.Select(s => s.Material.Id).ShouldBe(new[] { low.Id, high.Id });
    }

    [Fact]
    public void Two_compiles_of_a_mixed_world_produce_bit_identical_submeshes()
    {
        (MaterialRef low, MaterialRef high) = InternPair();
        BrushPlacement[] placements =
        [
            new(Brush.CreateBox(new Vector3(-1f), new Vector3(1f), high),
                Matrix4x4.CreateTranslation(10f, 10f, 10f)),
            new(Brush.CreateBox(new Vector3(-1f), new Vector3(1f), low).WithFaceMaterial(2, high),
                Matrix4x4.CreateTranslation(20f, 20f, 20f)),
        ];

        CsgWorld first = CsgWorld.Build(placements);
        CsgWorld second = CsgWorld.Build(placements);

        second.ChunkMeshes.Count.ShouldBe(first.ChunkMeshes.Count);
        for (int c = 0; c < first.ChunkMeshes.Count; c++)
        {
            ChunkMesh a = first.ChunkMeshes[c];
            ChunkMesh b = second.ChunkMeshes[c];
            b.Coord.ShouldBe(a.Coord);
            b.Submeshes.Count.ShouldBe(a.Submeshes.Count);
            // Guard: the cell must be mixed or the test proves nothing.
            a.Submeshes.Count.ShouldBe(2);
            for (int s = 0; s < a.Submeshes.Count; s++)
            {
                b.Submeshes[s].Material.ShouldBe(a.Submeshes[s].Material);
                b.Submeshes[s].Vertices.SequenceEqual(a.Submeshes[s].Vertices).ShouldBeTrue(
                    $"submesh #{s} vertices diverged for cell {a.Coord}");
                b.Submeshes[s].Indices.SequenceEqual(a.Submeshes[s].Indices).ShouldBeTrue(
                    $"submesh #{s} indices diverged for cell {a.Coord}");
            }
        }
    }

    [Fact]
    public void The_swap_creates_one_gpu_mesh_per_cell_and_material()
    {
        (MaterialRef low, MaterialRef high) = InternPair();
        var scene = new Scene("Test");
        var renderer = new FakeRenderer();
        AddBrushNode(scene, "a", new Vector3(10f, 10f, 10f), low);
        AddBrushNode(scene, "b", new Vector3(20f, 20f, 20f), high);
        AddBrushNode(scene, "far", new Vector3(80f, 16f, 16f), MaterialRef.Default);

        scene.RebuildStaticWorld(renderer);

        scene.StaticWorldChunkMeshes.Count.ShouldBe(2);
        renderer.CreatedMeshes.Count.ShouldBe(3);
        scene.TryGetStaticWorldChunkMesh(new ChunkCoord(0, 0, 0), out StaticWorldChunkMesh mixed).ShouldBeTrue();
        mixed.Submeshes.Length.ShouldBe(2);
        scene.TryGetStaticWorldChunkMesh(new ChunkCoord(2, 0, 0), out StaticWorldChunkMesh far).ShouldBeTrue();
        far.Submeshes.Length.ShouldBe(1);

        for (int s = 0; s < mixed.Submeshes.Length; s++)
        {
            StaticWorldSubmesh entry = mixed.Submeshes[s];
            ChunkSubmesh source = mixed.Artifact.ShouldNotBeNull().Submeshes[s];
            entry.SourceMaterial.ShouldBe(source.Material);
            var uploaded = (FakeMesh)entry.Mesh;
            uploaded.VertexData.SequenceEqual(source.Vertices).ShouldBeTrue();
            uploaded.IndexData.SequenceEqual(source.Indices).ShouldBeTrue();
        }
    }

    [Fact]
    public void Editing_one_cell_recreates_only_that_cells_material_meshes()
    {
        (MaterialRef low, MaterialRef high) = InternPair();
        var (scene, renderer, logger) = CreateScene();
        SceneNode edited = AddBrushNode(scene, "a", new Vector3(10f, 10f, 10f), low);
        AddBrushNode(scene, "b", new Vector3(20f, 20f, 20f), high);
        AddBrushNode(scene, "far", new Vector3(80f, 16f, 16f), MaterialRef.Default);

        PumpUntil(scene, renderer, logger, () => scene.StaticWorldCompileCount == 1);
        renderer.CreatedMeshes.Count.ShouldBe(3);
        scene.TryGetStaticWorldChunkMesh(new ChunkCoord(0, 0, 0), out StaticWorldChunkMesh mixedBefore).ShouldBeTrue();
        scene.TryGetStaticWorldChunkMesh(new ChunkCoord(2, 0, 0), out StaticWorldChunkMesh farBefore).ShouldBeTrue();

        edited.LocalPosition = new Vector3(11f, 10f, 10f); // still mid-cell (0,0,0)
        PumpUntil(scene, renderer, logger, () => scene.StaticWorldCompileCount == 2);

        // Both materials of the edited cell are recreated: the whole cell is rebuilt.
        renderer.CreatedMeshes.Count.ShouldBe(5);
        foreach (StaticWorldSubmesh stale in mixedBefore.Submeshes)
            ((FakeMesh)stale.Mesh).Disposed.ShouldBeTrue();
        farBefore.SingleFakeMesh().Disposed.ShouldBeFalse();

        scene.TryGetStaticWorldChunkMesh(new ChunkCoord(0, 0, 0), out StaticWorldChunkMesh mixedAfter).ShouldBeTrue();
        mixedAfter.Submeshes.Length.ShouldBe(2);
        mixedAfter.Submeshes.Select(s => s.SourceMaterial).ShouldBe(new[] { low, high });
        for (int s = 0; s < mixedAfter.Submeshes.Length; s++)
            mixedAfter.Submeshes[s].Mesh.ShouldNotBeSameAs(mixedBefore.Submeshes[s].Mesh);

        scene.TryGetStaticWorldChunkMesh(new ChunkCoord(2, 0, 0), out StaticWorldChunkMesh farAfter).ShouldBeTrue();
        farAfter.SingleMesh().ShouldBeSameAs(farBefore.SingleMesh());
        farAfter.Artifact.ShouldBeSameAs(farBefore.Artifact);
    }

    [Fact]
    public void Repeated_recompiles_of_a_mixed_world_leak_no_gpu_meshes()
    {
        (MaterialRef low, MaterialRef high) = InternPair();
        var (scene, renderer, logger) = CreateScene();
        SceneNode edited = AddBrushNode(scene, "a", new Vector3(10f, 10f, 10f), low);
        AddBrushNode(scene, "b", new Vector3(20f, 20f, 20f), high);
        AddBrushNode(scene, "far", new Vector3(80f, 16f, 16f), MaterialRef.Default);

        PumpUntil(scene, renderer, logger, () => scene.StaticWorldCompileCount == 1);

        // Mixed cell's two meshes plus the far cell's one.
        const int SteadyStateMeshes = 3;
        renderer.LiveMeshes.Count.ShouldBe(SteadyStateMeshes);

        const int Recompiles = 60;
        for (int i = 0; i < Recompiles; i++)
        {
            int target = scene.StaticWorldCompileCount + 1;
            // New position every pass or the scene stays clean and no compile
            // comes. The walk stays inside cell (0,0,0), which spans 0..32.
            edited.LocalPosition = new Vector3(10f + (i + 1) * 0.05f, 10f, 10f);
            PumpUntil(scene, renderer, logger, () => scene.StaticWorldCompileCount >= target);

            renderer.LiveMeshes.Count.ShouldBe(
                SteadyStateMeshes,
                $"live GPU meshes drifted after {i + 1} recompile(s)");
        }

        // Guard: meshes really were recreated.
        renderer.CreatedMeshes.Count.ShouldBeGreaterThan(SteadyStateMeshes + Recompiles);
        foreach (FakeMesh mesh in renderer.CreatedMeshes.Except(renderer.LiveMeshes))
            mesh.Disposed.ShouldBeTrue("a deregistered mesh must also have been disposed");
        foreach (FakeMesh live in renderer.LiveMeshes)
            live.Disposed.ShouldBeFalse("a live mesh must still be renderable");

        scene.StaticWorldChunkMeshes.Count.ShouldBe(2);
        scene.StaticWorldChunkMeshes.Sum(c => c.Submeshes.Length).ShouldBe(SteadyStateMeshes);
    }

    [Fact]
    public void A_failed_material_mesh_creation_rolls_back_the_whole_cell()
    {
        (MaterialRef low, MaterialRef high) = InternPair();
        var (scene, renderer, logger) = CreateScene();
        SceneNode edited = AddBrushNode(scene, "a", new Vector3(10f, 10f, 10f), low);
        AddBrushNode(scene, "b", new Vector3(20f, 20f, 20f), high);
        PumpUntil(scene, renderer, logger, () => scene.StaticWorldCompileCount == 1);

        CsgWorld previousWorld = scene.StaticWorld.ShouldNotBeNull();
        scene.TryGetStaticWorldChunkMesh(new ChunkCoord(0, 0, 0), out StaticWorldChunkMesh before).ShouldBeTrue();
        before.Submeshes.Length.ShouldBe(2);

        edited.LocalPosition = new Vector3(11f, 10f, 10f);
        renderer.CreateMeshBudget = 1; // the cell's second material throws
        PumpUntilCreateMeshFails(scene, renderer, logger);

        scene.StaticWorld.ShouldBeSameAs(previousWorld);
        scene.TryGetStaticWorldChunkMesh(new ChunkCoord(0, 0, 0), out StaticWorldChunkMesh after).ShouldBeTrue();
        after.Submeshes.ShouldBe(before.Submeshes);
        foreach (StaticWorldSubmesh entry in before.Submeshes)
            ((FakeMesh)entry.Mesh).Disposed.ShouldBeFalse();

        // The one replacement that was created is rolled back.
        renderer.CreatedMeshes.Count.ShouldBe(3);
        renderer.CreatedMeshes[2].Disposed.ShouldBeTrue();
    }

    [Fact]
    public void Uploaded_submeshes_carry_the_material_the_asset_manager_resolved()
    {
        MaterialRef accent = MaterialRegistry.Intern(AccentMaterialPath);
        var renderer = new FakeRenderer();
        var assets = new AssetManager(
            NullLogger<AssetManager>.Instance, ContentRoot.Path, hotReloadEnabled: false);
        assets.AttachRenderer(renderer);

        var scene = new Scene("Test")
        {
            Assets = assets,
            StaticWorldMaterial = NoopMaterial,
        };
        AddBrushNode(scene, "plain", new Vector3(10f, 10f, 10f), MaterialRef.Default);
        AddBrushNode(scene, "accent", new Vector3(20f, 20f, 20f), accent);

        scene.RebuildStaticWorld(renderer);

        scene.TryGetStaticWorldChunkMesh(new ChunkCoord(0, 0, 0), out StaticWorldChunkMesh cell).ShouldBeTrue();
        cell.Submeshes.Length.ShouldBe(2);

        StaticWorldSubmesh plain = cell.Submeshes.Single(s => s.SourceMaterial.IsDefault);
        plain.Material.ShouldBeSameAs(NoopMaterial);
        StaticWorldSubmesh accented = cell.Submeshes.Single(s => s.SourceMaterial == accent);
        accented.Material.ShouldBeSameAs(assets.LoadMaterial(AccentMaterialPath));

        assets.ReleaseGraphicsResources();
    }

    [Fact]
    public void A_face_whose_material_path_escapes_the_content_root_still_compiles_and_uploads()
    {
        // Interning accepts this path; normalisation rejects it at resolve time,
        // inside the GPU swap, where a throw would crash the render thread.
        MaterialRef escaping = MaterialRegistry.Intern($"../evil_{Guid.NewGuid():N}.spectramat");
        var renderer = new FakeRenderer();
        var logger = new CapturingLogger();
        var assets = new AssetManager(logger, ContentRoot.Path, hotReloadEnabled: false);
        assets.AttachRenderer(renderer);

        var scene = new Scene("Test") { Assets = assets };
        AddBrushNode(scene, "escaping", new Vector3(10f, 10f, 10f), escaping);

        Should.NotThrow(() => scene.RebuildStaticWorld(renderer));

        scene.TryGetStaticWorldChunkMesh(new ChunkCoord(0, 0, 0), out StaticWorldChunkMesh cell).ShouldBeTrue();
        StaticWorldSubmesh only = cell.Submeshes.ShouldHaveSingleItem();
        only.Material.ShouldBeSameAs(assets.DefaultMaterial);
        logger.MessagesAt(Microsoft.Extensions.Logging.LogLevel.Warning)
            .ShouldContain(m => m.Contains("not usable"), customMessage: logger.Describe());

        scene.MarkStaticWorldDirty();
        Should.NotThrow(() => scene.RebuildStaticWorld(renderer));

        assets.ReleaseGraphicsResources();
    }

    [Fact]
    public void Without_an_asset_manager_every_face_falls_back_to_the_world_material()
    {
        (MaterialRef low, MaterialRef high) = InternPair();
        var scene = new Scene("Test") { StaticWorldMaterial = NoopMaterial };
        AddBrushNode(scene, "a", new Vector3(10f, 10f, 10f), low);
        AddBrushNode(scene, "b", new Vector3(20f, 20f, 20f), high);

        scene.RebuildStaticWorld(new FakeRenderer());

        scene.TryGetStaticWorldChunkMesh(new ChunkCoord(0, 0, 0), out StaticWorldChunkMesh cell).ShouldBeTrue();
        cell.Submeshes.Length.ShouldBe(2); // still split
        foreach (StaticWorldSubmesh entry in cell.Submeshes)
            entry.Material.ShouldBeSameAs(NoopMaterial);
    }

    [Fact]
    public void RefreshStaticWorldMaterials_re_resolves_without_touching_gpu_meshes()
    {
        var scene = new Scene("Test");
        var renderer = new FakeRenderer();
        AddBrushNode(scene, "a", new Vector3(10f, 10f, 10f), MaterialRef.Default);
        scene.RebuildStaticWorld(renderer); // no world material assigned yet

        scene.StaticWorldChunkMeshes.ShouldHaveSingleItem().SingleMaterial().ShouldBeNull();
        Mesh uploaded = scene.StaticWorldChunkMeshes[0].SingleMesh();

        scene.StaticWorldMaterial = NoopMaterial;
        scene.RefreshStaticWorldMaterials();

        scene.StaticWorldChunkMeshes.ShouldHaveSingleItem().SingleMaterial().ShouldBeSameAs(NoopMaterial);
        scene.StaticWorldChunkMeshes[0].SingleMesh().ShouldBeSameAs(uploaded);
        renderer.CreatedMeshes.Count.ShouldBe(1);
        ((FakeMesh)uploaded).Disposed.ShouldBeFalse();
    }

    [Fact]
    public void World_items_are_one_per_visible_chunk_and_material_with_per_chunk_stats()
    {
        (MaterialRef low, MaterialRef high) = InternPair();
        var scene = new Scene("Test") { StaticWorldMaterial = NoopMaterial };

        // One two-material brush in view, one brush behind the camera.
        SceneNode front = scene.Root.CreateChild("front");
        front.LocalPosition = new Vector3(0f, 0f, -5f);
        front.Brush = Brush.CreateBox(new Vector3(-1f), new Vector3(1f), low).WithFaceMaterial(2, high);
        AddBrushNode(scene, "behind", new Vector3(0f, 0f, 100f), MaterialRef.Default);

        scene.RebuildStaticWorld(new FakeRenderer());
        scene.StaticWorldChunkMeshes.Count.ShouldBe(2);

        var view = new RenderView();
        scene.BuildRenderView(MakeCamera(), view);

        // Chunk stats count chunks; the item list counts per-material draws.
        view.WorldChunksTotal.ShouldBe(2);
        view.WorldChunksVisible.ShouldBe(1);
        view.WorldItems.Count.ShouldBe(2);
        view.WorldMaterialBatchesVisible.ShouldBe(2);
        view.WorldMaterialBatchesTotal.ShouldBe(3);

        StaticWorldChunkMesh visible = scene.StaticWorldChunkMeshes
            .Single(c => c.RenderBounds.Max.Z < 0f);
        for (int i = 0; i < view.WorldItems.Count; i++)
        {
            RenderItem item = view.WorldItems[i];
            item.Mesh.ShouldBeSameAs(visible.Submeshes[i].Mesh); // ascending material id, as uploaded
            item.Material.ShouldBeSameAs(visible.Submeshes[i].Material);
            item.World.ShouldBe(Matrix4x4.Identity);
        }
    }

    [Fact]
    public void BuildRenderView_is_allocation_free_with_multiple_materials()
    {
        (MaterialRef low, MaterialRef high) = InternPair();
        var scene = new Scene("Test") { StaticWorldMaterial = NoopMaterial };
        for (int x = 0; x < 6; x++)
        {
            for (int z = 0; z < 6; z++)
            {
                SceneNode node = scene.Root.CreateChild($"b{x}_{z}");
                node.LocalPosition = new Vector3(x * 4f - 10f, 0f, -z * 4f - 5f);
                node.Brush = Brush.CreateBox(new Vector3(-0.5f), new Vector3(0.5f), (x + z) % 2 == 0 ? low : high);
            }
        }
        scene.RebuildStaticWorld(new FakeRenderer());

        var camera = MakeCamera();
        var view = new RenderView();
        for (int i = 0; i < 50; i++)
            scene.BuildRenderView(camera, view);

        // Guard: the multi-material path is what gets measured.
        view.WorldMaterialBatchesVisible.ShouldBeGreaterThan(view.WorldChunksVisible);

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 100; i++)
            scene.BuildRenderView(camera, view);
        long delta = GC.GetAllocatedBytesForCurrentThread() - before;

        delta.ShouldBe(0L);
    }

    private const string AccentMaterialPath = "Materials/checker_orange.spectramat";

    // At z = 3 looking down -Z. These are the Camera defaults, set here so the
    // tests don't depend on them.
    private static Camera MakeCamera() => new()
    {
        Position = new Vector3(0f, 0f, 3f),
        Yaw = -MathF.PI / 2f,
        Pitch = 0f,
        AspectRatio = 16f / 9f,
    };

    // Ids follow interning order and are never reused, so low.Id < high.Id
    // whatever other tests intern.
    private static (MaterialRef Low, MaterialRef High) InternPair()
    {
        MaterialRef low = MaterialRegistry.Intern($"Materials/w5_low_{Guid.NewGuid():N}.spectramat");
        MaterialRef high = MaterialRegistry.Intern($"Materials/w5_high_{Guid.NewGuid():N}.spectramat");
        low.Id.ShouldBeLessThan(high.Id);
        return (low, high);
    }

    private static (Scene Scene, FakeRenderer Renderer, CapturingLogger Logger) CreateScene() =>
        (new Scene("Test"), new FakeRenderer(), new CapturingLogger());

    private static SceneNode AddBrushNode(Scene scene, string name, Vector3 position, MaterialRef material)
    {
        SceneNode node = scene.Root.CreateChild(name);
        node.LocalPosition = position;
        node.Brush = Brush.CreateBox(new Vector3(-1f), new Vector3(1f), material);
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
