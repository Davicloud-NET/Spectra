using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using Spectra.Kitchen.Cooking;
using Spectra.Kitchen.Diagnostics;
using Spectra.Kitchen.Maps;
using Spectra.Kitchen.Rules;
using SpectraEngine.Core.Assets;
using SpectraEngine.Core.Assets.Packs;
using SpectraEngine.Core.Bsp;
using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Maps;
using SpectraEngine.Core.Maps.Compiled;
using SpectraEngine.Core.Scene;

namespace Spectra.Kitchen.Tests;

/// <summary>
/// The map bake: a <c>.smap</c> bundle in, a <c>.scmap</c> out. Runs the real
/// rule over a real bundle on disk.
/// </summary>
public class MapRuleTests
{
    [Fact]
    public void A_bundle_bakes_into_a_compiled_map_with_geometry_in_it()
    {
        using var project = new TempProject();
        MapFixture fixture = MapFixture.Fresh();
        fixture.WriteBundle(project, "Room.smap");

        ScmapProbe map = Bake(project, "Maps/Room.smap");

        map.SceneName.ShouldBe("BakeRoom");
        map.Chunks.Count.ShouldBeGreaterThan(0);
        map.TriangleCount.ShouldBeGreaterThan(0);

        map.Geometry.Count(cell => cell.HasBsp).ShouldBe(map.Chunks.Count);
    }

    [Fact]
    public void A_submesh_names_an_asset_TABLE_ROW_and_never_a_material_id()
    {
        using var project = new TempProject();
        MapFixture fixture = MapFixture.Fresh();

        // Unrelated materials interned first, so this map's ids cannot equal
        // its asset row indices by coincidence.
        for (int i = 0; i < 5; i++) MaterialRegistry.Intern($"Materials/unrelated_{Guid.NewGuid():N}.spectramat");

        fixture.WriteBundle(project, "Room.smap");
        ScmapProbe map = Bake(project, "Maps/Room.smap");

        int wallId = MaterialRegistry.Intern(fixture.WallMaterial).Id;
        int floorId = MaterialRegistry.Intern(fixture.FloorMaterial).Id;

        map.Assets.Count.ShouldBe(2);
        wallId.ShouldBeGreaterThan(map.Assets.Count);
        floorId.ShouldBeGreaterThan(map.Assets.Count);

        var paths = map.Assets.Select(row => row.Path).ToList();
        paths.ShouldContain(fixture.WallMaterial);
        paths.ShouldContain(fixture.FloorMaterial);
        map.Assets.ShouldAllBe(row => row.Kind == PackEntryKind.Material);

        foreach (ScmapProbe.CellGeometry cell in map.Geometry)
        {
            foreach (ScmapProbe.SubmeshCopy submesh in cell.Submeshes)
            {
                if (submesh.AssetIndex == ScmapFormat.NoAssetIndex) continue;
                submesh.AssetIndex.ShouldBeLessThan((uint)map.Assets.Count);
            }
        }
    }

    [Fact]
    public void The_wall_submesh_resolves_to_the_wall_material_through_the_table()
    {
        using var project = new TempProject();
        MapFixture fixture = MapFixture.Fresh();
        for (int i = 0; i < 3; i++) MaterialRegistry.Intern($"Materials/unrelated_{Guid.NewGuid():N}.spectramat");

        fixture.WriteBundle(project, "Room.smap");
        ScmapProbe map = Bake(project, "Maps/Room.smap");

        var drawn = new HashSet<string>(StringComparer.Ordinal);
        foreach (ScmapProbe.CellGeometry cell in map.Geometry)
        {
            foreach (ScmapProbe.SubmeshCopy submesh in cell.Submeshes)
                drawn.Add(map.Assets[(int)submesh.AssetIndex].Path);
        }

        drawn.ShouldBe(new[] { fixture.WallMaterial, fixture.FloorMaterial }, ignoreOrder: true);
    }

    [Fact]
    public void Submeshes_are_in_ascending_asset_order_within_a_cell()
    {
        using var project = new TempProject();
        MapFixture fixture = MapFixture.Fresh();
        fixture.WriteBundle(project, "Room.smap");

        ScmapProbe map = Bake(project, "Maps/Room.smap");

        // The compile's order is material id, which varies per process.
        bool multi = false;
        foreach (ScmapProbe.CellGeometry cell in map.Geometry)
        {
            for (int i = 1; i < cell.Submeshes.Count; i++)
            {
                multi = true;
                cell.Submeshes[i].AssetIndex.ShouldBeGreaterThan(cell.Submeshes[i - 1].AssetIndex);
            }
        }

        multi.ShouldBeTrue("no cell in the fixture wears two materials, so the ordering rule was not exercised");
    }

    [Fact]
    public void Every_section_and_every_blob_starts_on_the_payload_alignment()
    {
        using var project = new TempProject();
        MapFixture fixture = MapFixture.Fresh();
        fixture.WriteBundle(project, "Room.smap");

        byte[] file = BakeBytes(project, "Maps/Room.smap");
        ScmapProbe map = ScmapProbe.Read(file);

        // Read off the bytes, not from the layout that wrote them.
        for (int i = 0; i < map.Header.SectionCount; i++)
        {
            int at = ScmapFormat.SectionTableOffset + (i * ScmapFormat.SectionSize);
            ulong offset = BitConverter.ToUInt64(file, at + 8);
            (offset % ScmapFormat.PayloadAlignment).ShouldBe(0ul);
        }

        foreach (ScmapChunkRecord cell in map.Chunks)
        {
            (cell.MeshOffset % ScmapFormat.PayloadAlignment).ShouldBe(0u);
            (cell.BspOffset % ScmapFormat.PayloadAlignment).ShouldBe(0u);
        }
    }

    [Fact]
    public void A_directory_entry_reaching_past_the_mesh_section_is_refused()
    {
        // Bytes edited by hand: the builder cannot produce this state.
        using var project = new TempProject();
        MapFixture fixture = MapFixture.Fresh();
        fixture.WriteBundle(project, "Room.smap");

        byte[] file = BakeBytes(project, "Maps/Room.smap");
        (int offset, _) = FindSection(file, ScmapFormat.ChunkDirectorySection);

        // First record's MeshSize: after the preamble, three cell coordinates,
        // two bounds vectors and the mesh offset.
        int meshSize = offset + ScmapFormat.ChunkPreambleSize
            + (3 * sizeof(int)) + (6 * sizeof(float)) + sizeof(uint);
        BitConverter.GetBytes(uint.MaxValue - 15).CopyTo(file, meshSize);

        Should.Throw<ScmapFormatException>(() => ScmapProbe.Read(file))
            .Message.ShouldContain("CMSH");
    }

    [Fact]
    public void Keeping_the_brush_source_draws_the_same_triangles_as_not_keeping_it()
    {
        // A loader that re-carved the kept brushes would draw every wall twice.
        using var project = new TempProject();
        MapFixture fixture = MapFixture.Fresh();
        fixture.WriteBundle(project, "Room.smap");

        ScmapProbe without = Bake(project, "Maps/Room.smap", keepBrushSource: false);
        ScmapProbe with = Bake(project, "Maps/Room.smap", keepBrushSource: true);

        with.TriangleCount.ShouldBe(without.TriangleCount);
        with.TriangleCount.ShouldBeGreaterThan(0);

        // The asset table must not depend on which brushes the cook kept.
        with.Assets.ShouldBe(without.Assets);

        with.Brushes.Count.ShouldBeGreaterThan(without.Brushes.Count);
    }

    [Fact]
    public void Every_world_brush_bakes_a_collision_hull_whether_or_not_its_source_is_kept()
    {
        // Collision is not the kept source: a level cooked without it still has a floor.
        using var project = new TempProject();
        MapFixture fixture = MapFixture.Fresh();
        fixture.WriteBundle(project, "Room.smap");

        ScmapProbe without = Bake(project, "Maps/Room.smap", keepBrushSource: false);
        ScmapProbe with = Bake(project, "Maps/Room.smap", keepBrushSource: true);

        // The crate is a part, so it is not among them.
        without.Hulls.Select(hull => without.NodeNames[(int)hull.NodeIndex])
            .ShouldBe(["Floor", "Wall", "BackWall", "Doorway"]);

        with.Hulls.Count.ShouldBe(without.Hulls.Count);
        for (int i = 0; i < with.Hulls.Count; i++)
        {
            with.Hulls[i].NodeIndex.ShouldBe(without.Hulls[i].NodeIndex);
            with.Hulls[i].Planes.ShouldBe(without.Hulls[i].Planes);

            // The planes the kept source holds for the same brush.
            with.Brushes.Single(brush => brush.NodeIndex == with.Hulls[i].NodeIndex)
                .Planes.ShouldBe(with.Hulls[i].Planes);
        }
    }

    [Fact]
    public void A_baked_brush_is_never_offered_for_re_carving_and_a_part_always_is()
    {
        using var project = new TempProject();
        MapFixture fixture = MapFixture.Fresh();
        fixture.WriteBundle(project, "Room.smap");

        ScmapProbe map = Bake(project, "Maps/Room.smap", keepBrushSource: true);

        map.HasBrushSource.ShouldBeTrue();

        int reCarvable = 0;
        int baked = 0;
        foreach (ScmapProbe.BrushCopy brush in map.Brushes)
        {
            ScmapNodeRecord node = map.Nodes[(int)brush.NodeIndex];

            if (ScmapBrushSource.IsReCarvable(in node)) reCarvable++;
            else baked++;

            node.BakedIntoChunks.ShouldBe(node.PayloadKind == ScmapPayloadKind.StaticWorldBrush);
        }

        // Four world brushes, one part.
        baked.ShouldBe(4);
        reCarvable.ShouldBe(1);
    }

    [Fact]
    public void A_part_brush_keeps_its_planes_even_when_the_cook_was_not_asked_to()
    {
        // A part is never baked into a chunk, so its planes live only in BRSH.
        using var project = new TempProject();
        MapFixture fixture = MapFixture.Fresh();
        fixture.WriteBundle(project, "Room.smap");

        ScmapProbe map = Bake(project, "Maps/Room.smap", keepBrushSource: false);

        map.HasBrushSource.ShouldBeTrue();
        map.Brushes.Count.ShouldBe(1);
        map.Nodes[(int)map.Brushes[0].NodeIndex].PayloadKind.ShouldBe(ScmapPayloadKind.PartBrush);
    }

    [Fact]
    public void A_map_with_no_part_brushes_and_no_kept_source_carries_no_BRSH_at_all()
    {
        using var project = new TempProject();
        MapFixture fixture = MapFixture.Fresh();
        fixture.WriteBundle(project, "Room.smap", withPart: false);

        ScmapProbe map = Bake(project, "Maps/Room.smap", keepBrushSource: false);

        // Absent, not empty: the header flag says whether the section exists.
        map.HasBrushSource.ShouldBeFalse();
        map.Brushes.ShouldBeEmpty();
        (map.Header.FileFlags & ScmapFlags.HasBrushSource).ShouldBe(ScmapFlags.None);
    }

    [Fact]
    public void A_subtractive_brush_bakes_its_cavity_rather_than_its_own_skin()
    {
        using var project = new TempProject();
        MapFixture fixture = MapFixture.Fresh();
        fixture.WriteBundle(project, "Room.smap");
        fixture.WriteBundle(project, "Solid.smap", withDoorway: false);

        ScmapProbe cut = Bake(project, "Maps/Room.smap");
        ScmapProbe solid = Bake(project, "Maps/Solid.smap");

        // A doorway adds triangles: the cavity walls.
        cut.TriangleCount.ShouldBeGreaterThan(solid.TriangleCount);

        ScmapNodeRecord doorway = cut.Nodes[cut.NodeNames.IndexOf("Doorway")];
        doorway.PayloadKind.ShouldBe(ScmapPayloadKind.StaticWorldBrush);
        doorway.IsSubtractiveBrush.ShouldBeTrue();
    }

    [Fact]
    public void The_flush_coplanar_doorway_is_open_in_the_baked_trees()
    {
        using var project = new TempProject();
        MapFixture fixture = MapFixture.Fresh();
        fixture.WriteBundle(project, "Room.smap");

        byte[] file = BakeBytes(project, "Maps/Room.smap");
        var opening = new Vector3(0f, 1.2f, -4.25f);

        ScmapDocument document = ScmapReader.Read(file, "Room.scmap");
        ChunkCoord cell = ChunkCoord.FromPosition(opening);

        bool asked = false;
        for (int i = 0; i < document.Chunks.Length; i++)
        {
            if (document.Chunks[i].X != cell.X ||
                document.Chunks[i].Y != cell.Y ||
                document.Chunks[i].Z != cell.Z)
            {
                continue;
            }

            asked = true;
            document.ChunkBsp(i).ContainsPoint(opening).ShouldBeFalse(
                "the doorway compiled solid in the baked tree");
        }

        asked.ShouldBeTrue("the cell holding the doorway has no baked tree to ask");
    }

    [Fact]
    public void The_baked_arrays_are_element_identical_to_a_fresh_compile_of_the_same_source()
    {
        using var project = new TempProject();
        MapFixture fixture = MapFixture.Fresh();
        fixture.WriteBundle(project, "Room.smap");

        ScmapProbe map = Bake(project, "Maps/Room.smap");

        MapDocument document = MapBundle.Load(Path.Combine(project.Layout.MapsPath, "Room.smap"));
        var scene = new SpectraEngine.Core.Scene.Scene(document.Scene.Name);
        MapSceneBinder.ApplyTo(document, scene);

        IReadOnlyList<BrushPlacement>? placements = scene.CaptureStaticWorldPlacements(out _);
        placements.ShouldNotBeNull();

        CsgWorld world = CsgWorld.Build(placements);
        world.ChunkMeshes.Count.ShouldBeGreaterThan(0);

        foreach (ChunkMesh mesh in world.ChunkMeshes)
        {
            ScmapProbe.CellGeometry cell = map.Geometry.Single(
                c => c.X == mesh.Coord.X && c.Y == mesh.Coord.Y && c.Z == mesh.Coord.Z);

            cell.Submeshes.Count.ShouldBe(mesh.Submeshes.Count);

            // Matched by material path, not position: the file sorts by asset
            // index, the compile by material id.
            foreach (ChunkSubmesh submesh in mesh.Submeshes)
            {
                uint index = submesh.Material.IsDefault
                    ? ScmapFormat.NoAssetIndex
                    : (uint)map.Assets.FindIndex(row =>
                        string.Equals(
                            row.Path,
                            MaterialPath(submesh.Material),
                            StringComparison.OrdinalIgnoreCase));

                ScmapProbe.SubmeshCopy baked = cell.Submeshes.Single(s => s.AssetIndex == index);
                baked.Vertices.ShouldBe(submesh.Vertices);
                baked.Indices.ShouldBe(submesh.Indices);
            }
        }
    }

    [Fact]
    public void A_brush_node_under_a_scale_is_refused_rather_than_baked()
    {
        using var project = new TempProject();
        MapFixture fixture = MapFixture.Fresh();

        SpectraEngine.Core.Scene.Scene scene = fixture.BuildScene();
        scene.Root.Children[0].LocalScale = new Vector3(2f, 1f, 1f);
        MapFixture.WriteBundle(project, "Bad.smap", scene);

        (byte[]? file, List<CookDiagnostic> said) = TryBake(project, "Maps/Bad.smap");

        // The editor only warns here. The cook must refuse.
        file.ShouldBeNull();
        said.Single().Id.ToString().ShouldBe("SC7001");
    }

    [Fact]
    public void An_entity_on_a_world_brush_bakes_and_warns_naming_the_node()
    {
        using var project = new TempProject();
        MapFixture fixture = MapFixture.Fresh();

        SpectraEngine.Core.Scene.Scene scene = fixture.BuildScene();
        scene.Root.Children.Single(node => node.Name == "Wall").Entity = new EntityData("func_door");
        MapFixture.WriteBundle(project, "Stuck.smap", scene);

        (byte[]? file, List<CookDiagnostic> said) = TryBake(project, "Maps/Stuck.smap");

        // A warning: the level still bakes, with the entity on its node.
        CookDiagnostic warned = said.Single();
        warned.Id.ToString().ShouldBe("SC7010");
        warned.IsError.ShouldBeFalse();
        warned.Message.ShouldContain("'Wall'");
        warned.Message.ShouldContain("func_door");

        CookGate.Verdict(CookDiagnosticCodes.MapEntityOnWorldBrush)
            .ShouldBe(CookGateVerdict.WarningUnlessStrict);

        ScmapProbe map = ScmapProbe.Read(file.ShouldNotBeNull());
        map.NodeNames[(int)map.Entities.Single().NodeIndex].ShouldBe("Wall");
    }

    [Fact]
    public void Entities_on_parts_and_on_bare_nodes_bake_without_a_warning()
    {
        using var project = new TempProject();
        MapFixture fixture = MapFixture.Fresh();
        fixture.WriteBundle(project, "Room.smap", withEntities: true);

        // BakeBytes refuses any diagnostic at all.
        ScmapProbe map = Bake(project, "Maps/Room.smap");

        map.Entities.Select(entity => map.NodeNames[(int)entity.NodeIndex])
            .ShouldBe(["Door", "DoorTrigger", "Relay", "Start"]);

        // Entity-owned is a brush node that carries an entity.
        map.NodeNames
            .Where((_, i) => (map.Nodes[i].PayloadFlags & ScmapPayloadFlags.IsEntityOwned) != 0)
            .ShouldBe(["Door", "DoorTrigger"]);
    }

    [Fact]
    public void A_bundle_whose_document_will_not_parse_is_refused_naming_the_document()
    {
        using var project = new TempProject();
        string bundle = Path.Combine(project.Layout.MapsPath, "Broken.smap");
        Directory.CreateDirectory(bundle);
        File.WriteAllText(Path.Combine(bundle, MapFormat.DocumentFileName), "{ \"spectramap\": ");

        (byte[]? file, List<CookDiagnostic> said) = TryBake(project, "Maps/Broken.smap");

        file.ShouldBeNull();
        said.Single().Id.ToString().ShouldBe("SC7007");
    }

    [Fact]
    public void Per_user_editor_state_is_not_read_and_not_hashed()
    {
        // Gitignored, per-user, and changes whenever the viewport camera moves.
        using var project = new TempProject();
        MapFixture fixture = MapFixture.Fresh();
        string bundle = fixture.WriteBundle(project, "Room.smap");

        byte[] before = BakeBytes(project, "Maps/Room.smap");

        File.WriteAllText(Path.Combine(bundle, MapFormat.UserStateFileName), "{ \"camera\": 1 }");
        byte[] after = BakeBytes(project, "Maps/Room.smap");

        after.ShouldBe(before);
    }

    [Fact]
    public void The_source_digest_moves_when_the_document_does()
    {
        using var project = new TempProject();
        MapFixture fixture = MapFixture.Fresh();
        fixture.WriteBundle(project, "Room.smap");
        ScmapProbe before = Bake(project, "Maps/Room.smap");

        fixture.WriteBundle(project, "Room.smap", withPart: false);
        ScmapProbe after = Bake(project, "Maps/Room.smap");

        after.Header.SourceMapDigest.ShouldNotBe(before.Header.SourceMapDigest);
    }

    private static ScmapProbe Bake(TempProject project, string bundlePath, bool keepBrushSource = false) =>
        ScmapProbe.Read(BakeBytes(project, bundlePath, keepBrushSource), bundlePath);

    private static byte[] BakeBytes(TempProject project, string bundlePath, bool keepBrushSource = false)
    {
        (byte[]? file, List<CookDiagnostic> said) = TryBake(project, bundlePath, keepBrushSource);
        said.ShouldBeEmpty();
        file.ShouldNotBeNull();
        return file;
    }

    private static (byte[]? File, List<CookDiagnostic> Diagnostics) TryBake(
        TempProject project, string bundlePath, bool keepBrushSource = false)
    {
        // Content root is the project root: a bundle lives beside Assets/.
        var context = new RuleContext(
            project.Root, bundlePath, CookProfile.Ship, keepBrushSource: keepBrushSource);

        new MapRule().Cook(context);

        return (
            context.Emissions.Count == 0 ? null : context.Emissions[0].Payload,
            [.. context.Diagnostics]);
    }

    private static string MaterialPath(MaterialRef material) =>
        MaterialRegistry.TryGetPath(material, out string path) ? path : string.Empty;

    private static (int Offset, int Size) FindSection(byte[] file, uint kind)
    {
        uint count = BitConverter.ToUInt32(file, 0x0C);
        for (int i = 0; i < count; i++)
        {
            int at = ScmapFormat.SectionTableOffset + (i * ScmapFormat.SectionSize);
            if (BitConverter.ToUInt32(file, at) != kind) continue;

            return ((int)BitConverter.ToUInt64(file, at + 8), (int)BitConverter.ToUInt64(file, at + 16));
        }

        throw new InvalidOperationException($"No '{ScmapFormat.DescribeFourCc(kind)}' section in the file.");
    }
}
