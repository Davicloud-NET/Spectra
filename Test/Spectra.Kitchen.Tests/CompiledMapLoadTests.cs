using Spectra.Kitchen.Diagnostics;
using Spectra.Kitchen.Maps;
using Spectra.Kitchen.Cooking;
using Spectra.Kitchen.Rules;
using SpectraEngine.Bsp.Tests;
using SpectraEngine.Core;
using SpectraEngine.Core.Assets;
using SpectraEngine.Core.Assets.Sources;
using SpectraEngine.Core.Bsp;
using SpectraEngine.Core.Graphics;
using SpectraEngine.Core.Maps;
using SpectraEngine.Core.Maps.Compiled;
using SpectraEngine.Core.Scene;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;

namespace Spectra.Kitchen.Tests;

/// <summary>
/// Loading a baked map: it reaches the GPU without a carve, and the scene then
/// refuses to carve it. Refusals are tested by editing the file's bytes.
/// </summary>
public class CompiledMapLoadTests
{
    [Fact]
    public void Loading_a_baked_map_runs_no_carve_at_all()
    {
        using var project = new TempProject();
        MapFixture fixture = MapFixture.Fresh();
        fixture.WriteBundle(project, "Room.smap");
        byte[] file = Bake(project, "Maps/Room.smap");

        var renderer = new FakeRenderer();
        var scene = new SpectraEngine.Core.Scene.Scene("empty");

        long before = Csg.CarveInvocationsOnThisThread;
        CompiledMapLoadReport report = Load(scene, renderer, file);
        long carves = Csg.CarveInvocationsOnThisThread - before;

        carves.ShouldBe(0, "a compiled map must reach the GPU with no CSG at all");

        scene.StaticWorld.ShouldBeNull("a compiled world is not the output of a compile");
        scene.StaticWorldCompileCount.ShouldBe(0);
        scene.StaticWorldDirty.ShouldBeFalse();

        report.ChunksLoaded.ShouldBeGreaterThan(0);
        report.SubmeshesUploaded.ShouldBeGreaterThan(0);
        report.TriangleCount.ShouldBeGreaterThan(0);
        scene.StaticWorldChunkMeshes.Count.ShouldBe(report.ChunksLoaded);
        renderer.LiveMeshes.Count.ShouldBe(report.SubmeshesUploaded);
    }

    [Fact]
    public void An_adopted_world_refuses_a_rebuild_and_names_the_reason()
    {
        using var project = new TempProject();
        MapFixture fixture = MapFixture.Fresh();
        fixture.WriteBundle(project, "Room.smap");

        var renderer = new FakeRenderer();
        var scene = new SpectraEngine.Core.Scene.Scene("empty");
        Load(scene, renderer, Bake(project, "Maps/Room.smap"));

        int meshes = renderer.LiveMeshes.Count;
        long before = Csg.CarveInvocationsOnThisThread;

        scene.RebuildStaticWorld(renderer);
        scene.RebuildStaticWorldIfDirty(renderer);

        (Csg.CarveInvocationsOnThisThread - before).ShouldBe(
            0, "a rebuild on an adopted world is the double-geometry bug");

        scene.RefusedStaticWorldRebuilds.ShouldBe(2);
        scene.StaticWorld.ShouldBeNull();
        renderer.LiveMeshes.Count.ShouldBe(meshes, "nothing was uploaded and nothing was destroyed");

        string said = scene.StaticWorldGuardMessage.ShouldNotBeNull();
        said.ShouldContain("Maps/Room.scmap");
        said.ShouldContain("ReleaseCompiledStaticWorld");
    }

    [Fact]
    public void An_adopted_world_refuses_the_automatic_dirty_marks_and_logs_once()
    {
        using var project = new TempProject();
        MapFixture fixture = MapFixture.Fresh();
        fixture.WriteBundle(project, "Room.smap");

        var renderer = new FakeRenderer();
        var scene = new SpectraEngine.Core.Scene.Scene("empty");
        Load(scene, renderer, Bake(project, "Maps/Room.smap"));

        scene.MarkStaticWorldDirty();

        SceneNode added = scene.Root.CreateChild("LateWall");
        added.Brush = Brush.CreateBox(-Vector3.One, Vector3.One);   // world brush: attaching marks dirty

        SceneNode part = FindPartBrushNode(scene).ShouldNotBeNull();
        part.BrushKind = BrushKind.World;                            // admission change

        scene.RefusedStaticWorldDirtyMarks.ShouldBeGreaterThanOrEqualTo(3);
        scene.StaticWorldDirty.ShouldBeFalse("a refused mark must not arm the pump");

        long before = Csg.CarveInvocationsOnThisThread;
        var logger = new CapturingLogger();
        scene.ProcessStaticWorldCompilation(renderer, logger);
        scene.ProcessStaticWorldCompilation(renderer, logger);

        (Csg.CarveInvocationsOnThisThread - before).ShouldBe(0);
        scene.StaticWorld.ShouldBeNull();

        // Logged once: the marks fire from property setters, per frame.
        List<string> warnings = logger
            .MessagesAt(Microsoft.Extensions.Logging.LogLevel.Warning)
            .Where(m => m.Contains("Static world guard"))
            .ToList();

        warnings.Count.ShouldBe(1);
        warnings[0].ShouldContain("dirty mark(s) refused");
    }

    [Fact]
    public void A_submesh_resolves_through_the_asset_TABLE_and_never_through_a_material_id()
    {
        // Unrelated materials interned first, so material ids and ASTB row
        // indices cannot agree by coincidence.
        for (int i = 0; i < 5; i++)
            MaterialRegistry.Intern($"Materials/unrelated_{Guid.NewGuid():N}.spectramat");

        using var project = new TempProject();
        MapFixture fixture = MapFixture.Fresh();
        fixture.WriteBundle(project, "Room.smap");

        var renderer = new FakeRenderer();
        var scene = new SpectraEngine.Core.Scene.Scene("empty");
        Load(scene, renderer, Bake(project, "Maps/Room.smap"));

        var worn = new HashSet<string>(StringComparer.Ordinal);
        foreach (StaticWorldChunkMesh chunk in scene.StaticWorldChunkMeshes)
        {
            foreach (StaticWorldSubmesh submesh in chunk.Submeshes)
            {
                if (submesh.SourceMaterial.IsDefault) continue;

                MaterialRegistry.TryGetPath(submesh.SourceMaterial, out string path).ShouldBeTrue();
                worn.Add(path);
            }
        }

        worn.ShouldBe(
            new HashSet<string>([fixture.WallMaterial, fixture.FloorMaterial], StringComparer.Ordinal));
    }

    [Fact]
    public void A_part_brushs_faces_resolve_through_the_same_table()
    {
        for (int i = 0; i < 3; i++)
            MaterialRegistry.Intern($"Materials/unrelated_{Guid.NewGuid():N}.spectramat");

        using var project = new TempProject();
        MapFixture fixture = MapFixture.Fresh();
        fixture.WriteBundle(project, "Room.smap");

        var renderer = new FakeRenderer();
        var scene = new SpectraEngine.Core.Scene.Scene("empty");
        Load(scene, renderer, Bake(project, "Maps/Room.smap"));

        SceneNode part = FindPartBrushNode(scene).ShouldNotBeNull();
        Brush brush = part.Brush.ShouldNotBeNull();
        brush.LocalPlanes.Count.ShouldBe(6);
        brush.FaceSurfaces.Count.ShouldBe(6);

        MaterialRegistry.TryGetPath(brush.FaceSurfaces[0].Material, out string path).ShouldBeTrue();
        path.ShouldBe(fixture.FloorMaterial);
    }

    [Fact]
    public void The_loaded_arrays_are_element_identical_to_a_fresh_cache_free_compile()
    {
        using var project = new TempProject();
        MapFixture fixture = MapFixture.Fresh();
        fixture.WriteBundle(project, "Room.smap");

        var renderer = new FakeRenderer();
        var scene = new SpectraEngine.Core.Scene.Scene("empty");
        Load(scene, renderer, Bake(project, "Maps/Room.smap"));

        MapDocument document = MapBundle.Load(Path.Combine(project.Layout.MapsPath, "Room.smap"));
        var authored = new SpectraEngine.Core.Scene.Scene(document.Scene.Name);
        MapSceneBinder.ApplyTo(document, authored);
        IReadOnlyList<BrushPlacement> placements =
            authored.CaptureStaticWorldPlacements(out _).ShouldNotBeNull();

        CsgWorld fresh = CsgWorld.Build(placements);
        fresh.ChunkMeshes.Count.ShouldBeGreaterThan(0);
        scene.StaticWorldChunkMeshes.Count.ShouldBe(fresh.ChunkMeshes.Count);

        foreach (ChunkMesh expected in fresh.ChunkMeshes)
        {
            scene.TryGetStaticWorldChunkMesh(expected.Coord, out StaticWorldChunkMesh loaded)
                .ShouldBeTrue($"cell {expected.Coord} is missing from the loaded world");

            loaded.RenderBounds.Min.ShouldBe(expected.RenderBounds.Min);
            loaded.RenderBounds.Max.ShouldBe(expected.RenderBounds.Max);
            loaded.Submeshes.Length.ShouldBe(expected.Submeshes.Count);

            // Matched by material, not position: the file sorts by asset row,
            // the compile by material id.
            foreach (ChunkSubmesh submesh in expected.Submeshes)
            {
                StaticWorldSubmesh actual = loaded.Submeshes.Single(s => s.SourceMaterial == submesh.Material);
                var mesh = (FakeMesh)actual.Mesh;
                mesh.VertexData.ShouldBe(submesh.Vertices);
                mesh.IndexData.ShouldBe(submesh.Indices);
            }
        }
    }

    [Fact]
    public void The_adopted_trees_answer_point_queries_exactly_as_a_fresh_compile_does()
    {
        using var project = new TempProject();
        MapFixture fixture = MapFixture.Fresh();
        fixture.WriteBundle(project, "Room.smap");

        var renderer = new FakeRenderer();
        var scene = new SpectraEngine.Core.Scene.Scene("empty");
        Load(scene, renderer, Bake(project, "Maps/Room.smap"));

        MapDocument document = MapBundle.Load(Path.Combine(project.Layout.MapsPath, "Room.smap"));
        var authored = new SpectraEngine.Core.Scene.Scene(document.Scene.Name);
        MapSceneBinder.ApplyTo(document, authored);
        CsgWorld fresh = CsgWorld.Build(authored.CaptureStaticWorldPlacements(out _).ShouldNotBeNull());

        CompiledStaticWorld adopted = scene.CompiledStaticWorld.ShouldNotBeNull();

        // Floor slab, air above it, wall, doorway.
        Vector3[] probes =
        [
            new(0f, -0.25f, 0f),
            new(0f, 3f, 0f),
            new(2.5f, 1.5f, -4.25f),
            new(0f, 1.0f, -4.25f),
        ];

        foreach (Vector3 probe in probes)
        {
            adopted.ContainsPoint(probe).ShouldBe(
                fresh.ContainsPoint(probe), $"the baked tree disagrees at {probe}");
        }

        // A tree answering false everywhere must not pass.
        probes.Select(fresh.ContainsPoint).Distinct().Count().ShouldBe(2);
        adopted.BspChunkCount.ShouldBeGreaterThan(0);
    }

    [Fact]
    public void A_baked_brush_is_not_rebuilt_even_when_the_file_carries_its_planes()
    {
        using var project = new TempProject();
        MapFixture fixture = MapFixture.Fresh();
        fixture.WriteBundle(project, "Room.smap");

        var plainRenderer = new FakeRenderer();
        var plain = new SpectraEngine.Core.Scene.Scene("plain");
        CompiledMapLoadReport plainReport = Load(plain, plainRenderer, Bake(project, "Maps/Room.smap"));

        var keptRenderer = new FakeRenderer();
        var kept = new SpectraEngine.Core.Scene.Scene("kept");
        CompiledMapLoadReport keptReport =
            Load(kept, keptRenderer, Bake(project, "Maps/Room.smap", keepBrushSource: true));

        keptReport.BakedBrushSourcesSkipped.ShouldBeGreaterThan(0);
        plainReport.BakedBrushSourcesSkipped.ShouldBe(0);

        // Rebuilt brushes would draw every wall twice.
        keptReport.TriangleCount.ShouldBe(plainReport.TriangleCount);
        keptRenderer.LiveMeshes.Count.ShouldBe(plainRenderer.LiveMeshes.Count);

        CountStaticWorldBrushes(kept.Root).ShouldBe(0);
        kept.StaticWorld.ShouldBeNull();
        kept.RefusedStaticWorldDirtyMarks.ShouldBe(0, "attaching nothing dirties nothing");
    }

    [Fact]
    public void The_graph_is_rebuilt_in_one_forward_pass_with_ids_parents_and_order_intact()
    {
        byte[] file = ScmapFixture.Build();

        var renderer = new FakeRenderer();
        var scene = new SpectraEngine.Core.Scene.Scene("empty");
        CompiledMapLoadReport report = Load(scene, renderer, file);

        report.NodesLoaded.ShouldBe(ScmapFixture.NodeNames.Length);
        scene.Name.ShouldBe(ScmapFixture.SceneName);

        SceneNode world = scene.Root.Children.Single();
        world.Name.ShouldBe("World");
        world.Id.ShouldBe(ScmapFixture.NodeId(0));

        // Sibling order is authored: it decides how the carve breaks ties.
        world.Children.Select(c => c.Name).ShouldBe(new[] { "zeta_room", "alpha_room" });
        world.Children[0].Children.Select(c => c.Name).ShouldBe(new[] { "Wall", "Cut" });
        world.Children[1].Children.Select(c => c.Name).ShouldBe(new[] { "Lamp", "Crate" });

        SceneNode wall = world.Children[0].Children[0];
        wall.Id.ShouldBe(ScmapFixture.NodeId(2));
        wall.LocalPosition.ShouldBe(ScmapFixture.Transforms[2].Position);
        wall.LocalRotation.ShouldBe(ScmapFixture.Transforms[2].Rotation);
        wall.LocalScale.ShouldBe(ScmapFixture.Transforms[2].Scale);

        wall.Brush.ShouldBeNull();
        scene.TryFindById(ScmapFixture.NodeId(3), out SceneNode? cut).ShouldBeTrue();
        cut.ShouldNotBeNull().Brush.ShouldBeNull();
    }

    [Fact]
    public void A_mesh_instance_is_NAMED_rather_than_silently_dropped()
    {
        byte[] file = ScmapFixture.Build();

        var renderer = new FakeRenderer();
        var scene = new SpectraEngine.Core.Scene.Scene("empty");
        CompiledMapLoadReport report = Load(scene, renderer, file);

        report.UnboundMeshInstances.ShouldBe(new[] { "Crate" });
        report.IsComplete.ShouldBeFalse();
        report.Describe().ShouldNotBeNull().ShouldContain("Crate");

        scene.TryFindById(ScmapFixture.NodeId(6), out SceneNode? crate).ShouldBeTrue();
        crate.ShouldNotBeNull().MeshRenderer.ShouldBeNull();
    }

    [Fact]
    public void The_format_gaps_are_stated_rather_than_discovered()
    {
        string said = CompiledMapLoadReport.DescribeFormatGaps();

        said.ShouldContain("spawns");
        said.ShouldContain("scripts");
        said.ShouldContain("submesh indices");
        said.ShouldContain("materials for ray hits");
        said.ShouldNotContain("entities");
        said.ShouldNotContain("lights");
        said.ShouldNotContain("collision");
        CompiledMapLoadReport.FormatGaps.Count.ShouldBe(5);
    }

    [Fact]
    public void A_hull_on_a_scaled_node_is_named_rather_than_given_collision_in_the_wrong_place()
    {
        // The fixture's nodes are scaled, which the cook refuses for a brush.
        // Planes under scale are not unit length, so the hull would be wrong.
        byte[] file = ScmapFixture.Build();

        var renderer = new FakeRenderer();
        var scene = new SpectraEngine.Core.Scene.Scene("empty");
        CompiledMapLoadReport report = Load(scene, renderer, file);

        report.CollisionHullsRefused.ShouldBe(new[] { "Wall", "Cut" });
        report.CollisionHullsLoaded.ShouldBe(0);
        report.Describe().ShouldNotBeNull().ShouldContain("2 world brush(es) with no collision (Wall, Cut)");
        scene.CompiledStaticWorld.ShouldNotBeNull().CollisionPlacements.ShouldBeEmpty();
    }

    [Fact]
    public void A_light_arrives_on_its_node_from_the_table()
    {
        byte[] file = ScmapFixture.Build();

        var renderer = new FakeRenderer();
        var scene = new SpectraEngine.Core.Scene.Scene("empty");
        CompiledMapLoadReport report = Load(scene, renderer, file);

        report.LightsLoaded.ShouldBe(ScmapFixture.Lights.Length);

        scene.TryFindById(ScmapFixture.NodeId(5), out SceneNode? lamp).ShouldBeTrue();
        Light spot = lamp.ShouldNotBeNull().Light.ShouldNotBeNull();
        spot.Kind.ShouldBe(LightKind.Spot);
        spot.Enabled.ShouldBeFalse();
        spot.OuterAngle.ShouldBe(40f);

        // A node the table does not name gets no light.
        scene.TryFindById(ScmapFixture.NodeId(2), out SceneNode? wall).ShouldBeTrue();
        wall.ShouldNotBeNull().Light.ShouldBeNull();
    }

    [Fact]
    public void A_map_at_another_format_version_is_refused_naming_both_numbers()
    {
        byte[] file = ScmapFixture.Build();
        BitConverter.GetBytes((ushort)(EngineInfo.CompiledMapFormatVersion + 1)).CopyTo(file, 0x04);

        ScmapFormatException refused = Should.Throw<ScmapFormatException>(
            () => Load(new SpectraEngine.Core.Scene.Scene("empty"), new FakeRenderer(), file));

        refused.Message.ShouldContain((EngineInfo.CompiledMapFormatVersion + 1).ToString());
        refused.Message.ShouldContain(EngineInfo.CompiledMapFormatVersion.ToString());
        refused.Message.ShouldContain("Recook");
    }

    [Fact]
    public void A_map_compiled_on_another_cell_size_is_refused_naming_both_numbers()
    {
        byte[] file = ScmapFixture.Build();

        // META: SceneNameString, SpawnCount, then the three compile constants.
        (int offset, _) = FindSection(file, ScmapFormat.MetaSection);
        BitConverter.GetBytes(ChunkCoord.CellSize * 2f).CopyTo(file, offset + 8);

        ScmapFormatException refused = Should.Throw<ScmapFormatException>(
            () => Load(new SpectraEngine.Core.Scene.Scene("empty"), new FakeRenderer(), file));

        refused.Message.ShouldContain("cell size");
        refused.Message.ShouldContain((ChunkCoord.CellSize * 2f).ToString());
        refused.Message.ShouldContain(ChunkCoord.CellSize.ToString());
        refused.Message.ShouldContain("Recook");
    }

    [Fact]
    public void A_map_welded_on_another_grid_is_refused_naming_both_numbers()
    {
        byte[] file = ScmapFixture.Build();
        (int offset, _) = FindSection(file, ScmapFormat.MetaSection);
        BitConverter.GetBytes(ScmapFormat.EngineSnapGrid * 4f).CopyTo(file, offset + 16);

        ScmapFormatException refused = Should.Throw<ScmapFormatException>(
            () => Load(new SpectraEngine.Core.Scene.Scene("empty"), new FakeRenderer(), file));

        refused.Message.ShouldContain("snap grid");
        refused.Message.ShouldContain("Recook");
    }

    [Fact]
    public void A_refused_map_releases_the_bytes_it_was_handed()
    {
        // On a mounted pack the blob holds a PackHandle reference. Leaking it
        // keeps the pack mounted for the life of the process.
        byte[] file = ScmapFixture.Build();
        BitConverter.GetBytes((ushort)(EngineInfo.CompiledMapFormatVersion + 1)).CopyTo(file, 0x04);

        ContentBlob blob = ContentBlob.CopyOf(file);
        Should.Throw<ScmapFormatException>(
            () => CompiledMapLoader.Load(
                new SpectraEngine.Core.Scene.Scene("empty"), new FakeRenderer(), blob, "Maps/Room.scmap"));

        Should.Throw<ObjectDisposedException>(() => blob.Span.Length);
    }

    [Fact]
    public void Releasing_an_adopted_world_destroys_its_meshes_and_frees_its_bytes()
    {
        using var project = new TempProject();
        MapFixture fixture = MapFixture.Fresh();
        fixture.WriteBundle(project, "Room.smap");

        var renderer = new FakeRenderer();
        var scene = new SpectraEngine.Core.Scene.Scene("empty");
        Load(scene, renderer, Bake(project, "Maps/Room.smap"));

        CompiledStaticWorld adopted = scene.CompiledStaticWorld.ShouldNotBeNull();
        FlatBspTree tree = adopted.Chunks.First(c => c.Bsp is not null).Bsp!;
        renderer.LiveMeshes.Count.ShouldBeGreaterThan(0);

        scene.ReleaseCompiledStaticWorld(renderer);

        scene.CompiledStaticWorld.ShouldBeNull();
        scene.HasCompiledStaticWorld.ShouldBeFalse();
        renderer.LiveMeshes.ShouldBeEmpty("DestroyMesh, so the tracking list loses them too");
        scene.StaticWorldChunkMeshes.ShouldBeEmpty();

        // The tree's nodes were a view into the released bytes.
        Should.Throw<ObjectDisposedException>(() => tree.ContainsPoint(Vector3.Zero));

        long before = Csg.CarveInvocationsOnThisThread;
        scene.Root.CreateChild("Block").Brush = Brush.CreateBox(-Vector3.One, Vector3.One);
        scene.RebuildStaticWorld(renderer);

        (Csg.CarveInvocationsOnThisThread - before).ShouldBeGreaterThan(0);
        scene.StaticWorld.ShouldNotBeNull();
        scene.RefusedStaticWorldRebuilds.ShouldBe(0);
    }

    private static CompiledMapLoadReport Load(
        SpectraEngine.Core.Scene.Scene scene, Renderer renderer, byte[] file) =>
        CompiledMapLoader.Load(scene, renderer, ContentBlob.CopyOf(file), "Maps/Room.scmap");

    private static byte[] Bake(TempProject project, string bundlePath, bool keepBrushSource = false)
    {
        var context = new RuleContext(
            project.Root, bundlePath, CookProfile.Ship, keepBrushSource: keepBrushSource);

        new MapRule().Cook(context);

        context.Diagnostics.Count.ShouldBe(
            0, string.Join(Environment.NewLine, context.Diagnostics.Select(d => d.ToString())));

        return context.Emissions[0].Payload;
    }

    private static SceneNode? FindPartBrushNode(SpectraEngine.Core.Scene.Scene scene)
    {
        foreach (SceneNode node in scene.Root.Traverse())
        {
            if (node.Brush is not null && node.BrushKind == BrushKind.Part) return node;
        }

        return null;
    }

    private static int CountStaticWorldBrushes(SceneNode root)
    {
        int found = 0;
        foreach (SceneNode node in root.Traverse())
        {
            if (node.IsStaticWorldBrush) found++;
        }

        return found;
    }

    private static (int Offset, int Size) FindSection(byte[] file, uint kind)
    {
        uint count = BitConverter.ToUInt32(file, 0x0C);
        for (int i = 0; i < count; i++)
        {
            int at = ScmapFormat.SectionTableOffset + (i * ScmapFormat.SectionSize);
            if (BitConverter.ToUInt32(file, at) != kind) continue;

            return ((int)BitConverter.ToUInt64(file, at + 8), (int)BitConverter.ToUInt64(file, at + 16));
        }

        throw new InvalidOperationException($"No section '{ScmapFormat.DescribeFourCc(kind)}' in this file.");
    }
}
