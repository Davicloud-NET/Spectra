using System;
using System.Buffers.Binary;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using Spectra.Kitchen.Maps;
using SpectraEngine.Core;
using SpectraEngine.Core.Assets;
using SpectraEngine.Core.Assets.Packs;
using SpectraEngine.Core.Bsp;
using SpectraEngine.Core.Maps.Compiled;
using SpectraEngine.Core.Scene;

namespace Spectra.Kitchen.Tests;

/// <summary>
/// The five tables of a compiled map, written and read back. Reader refusals
/// are tested by editing the bytes of a valid file.
/// </summary>
public class ScmapBuilderTests
{
    [Fact]
    public void The_five_tables_read_back_as_they_were_written()
    {
        ScmapProbe probe = ScmapProbe.Read(ScmapFixture.Build());

        probe.Header.FormatVersion.ShouldBe(EngineInfo.CompiledMapFormatVersion);
        probe.Header.SourceMapDigest.ShouldBe(ScmapFixture.Digest);
        probe.Header.MapFormatVersion.ShouldBe((uint)EngineInfo.MapFormatVersion);
        probe.SceneName.ShouldBe(ScmapFixture.SceneName);

        probe.Assets.Select(a => a.Path).ShouldBe(ScmapFixture.AssetPaths);
        probe.Assets[0].Kind.ShouldBe(PackEntryKind.Material);
        probe.Assets[2].Kind.ShouldBe(PackEntryKind.Image);
        probe.Assets[3].ContentHash.ShouldBe(0xDDDD_EEEE_FFFF_0000ul);

        probe.NodeNames.ShouldBe(ScmapFixture.NodeNames);
        probe.Nodes[0].ParentIndex.ShouldBe(-1);
        probe.Nodes[2].ParentIndex.ShouldBe(1);
        probe.Nodes[3].IsSubtractiveBrush.ShouldBeTrue();
        probe.Nodes[6].PayloadKind.ShouldBe(ScmapPayloadKind.MeshInstance);
        probe.Nodes[6].PayloadIndex.ShouldBe(3u);
        probe.Nodes[6].PayloadFlags.ShouldBe(ScmapPayloadFlags.IsEntityOwned);

        for (int i = 0; i < probe.Nodes.Count; i++)
            probe.Nodes[i].NodeId.ShouldBe(ScmapFixture.NodeId(i));

        probe.Spawns.Count.ShouldBe(1);
        probe.Meta.SpawnCount.ShouldBe(1u);
        probe.InvalidDeclaredStates.ShouldBe(0);
    }

    [Fact]
    public void Every_authored_transform_round_trips_bit_for_bit()
    {
        // Bits, not a tolerance: the compile cache keys on exact equality.
        ScmapProbe probe = ScmapProbe.Read(ScmapFixture.Build());

        Transform[] expected = ScmapFixture.Transforms;
        probe.Nodes.Count.ShouldBe(expected.Length);
        for (int i = 0; i < expected.Length; i++)
        {
            ScmapNodeRecord record = probe.Nodes[i];

            Bits(record.LocalPosition.X).ShouldBe(Bits(expected[i].Position.X), $"node {i} position x");
            Bits(record.LocalPosition.Y).ShouldBe(Bits(expected[i].Position.Y), $"node {i} position y");
            Bits(record.LocalPosition.Z).ShouldBe(Bits(expected[i].Position.Z), $"node {i} position z");

            Bits(record.LocalRotation.X).ShouldBe(Bits(expected[i].Rotation.X), $"node {i} rotation x");
            Bits(record.LocalRotation.Y).ShouldBe(Bits(expected[i].Rotation.Y), $"node {i} rotation y");
            Bits(record.LocalRotation.Z).ShouldBe(Bits(expected[i].Rotation.Z), $"node {i} rotation z");
            Bits(record.LocalRotation.W).ShouldBe(Bits(expected[i].Rotation.W), $"node {i} rotation w");

            Bits(record.LocalScale.X).ShouldBe(Bits(expected[i].Scale.X), $"node {i} scale x");
            Bits(record.LocalScale.Y).ShouldBe(Bits(expected[i].Scale.Y), $"node {i} scale y");
            Bits(record.LocalScale.Z).ShouldBe(Bits(expected[i].Scale.Z), $"node {i} scale z");
        }
    }

    [Fact]
    public void Strings_are_emitted_in_first_reference_order_during_the_canonical_walk()
    {
        // Empty string, scene name, asset table order, node names in pre-order.
        ScmapProbe probe = ScmapProbe.Read(ScmapFixture.Build());
        probe.Strings.ShouldBe(ScmapFixture.ExpectedStrings());
    }

    [Fact]
    public void The_chunk_directory_is_sorted_however_the_cells_arrived()
    {
        ScmapProbe probe = ScmapProbe.Read(ScmapFixture.Build());

        probe.Chunks.Count.ShouldBe(ScmapFixture.SortedCells.Length);
        for (int i = 0; i < probe.Chunks.Count; i++)
        {
            ChunkCoord expected = ScmapFixture.SortedCells[i];
            probe.Chunks[i].X.ShouldBe(expected.X);
            probe.Chunks[i].Y.ShouldBe(expected.Y);
            probe.Chunks[i].Z.ShouldBe(expected.Z);

            // A cell with no geometry of its own is legal.
            probe.Chunks[i].MeshSize.ShouldBe(0u);
            probe.Chunks[i].BspSize.ShouldBe(0u);
        }

        // Render bounds, not the cell cube.
        probe.Chunks[2].BoundsMin.ShouldBe(new Vector3(-0.5f));
    }

    [Fact]
    public void An_unsorted_chunk_directory_in_a_FILE_is_refused()
    {
        // A binary search over an unsorted directory misses cells that are there.
        byte[] file = ScmapFixture.Build();
        (int offset, _) = FindSection(file, ScmapFormat.ChunkDirectorySection);

        int first = offset + ScmapFormat.ChunkPreambleSize;
        int second = first + ScmapFormat.ChunkRecordSize;

        byte[] swap = file.AsSpan(first, ScmapFormat.ChunkRecordSize).ToArray();
        file.AsSpan(second, ScmapFormat.ChunkRecordSize).CopyTo(file.AsSpan(first));
        swap.CopyTo(file.AsSpan(second));

        Should.Throw<ScmapFormatException>(() => ScmapProbe.Read(file))
            .Message.ShouldContain("ascending cell order");
    }

    [Fact]
    public void The_reserved_section_codes_are_claimed_and_empty()
    {
        byte[] file = ScmapFixture.Build();

        foreach (uint kind in new[]
        {
            ScmapFormat.EntitySection,
            ScmapFormat.EntityConnectionSection,
            ScmapFormat.ScriptSection,
            ScmapFormat.ScriptBytecodeSection,
            ScmapFormat.ScriptSourceSection,
            ScmapFormat.ChunkMeshSection,
            ScmapFormat.ChunkBspSection,
        })
        {
            (int offset, long size) = FindSection(file, kind);
            offset.ShouldBeGreaterThan(0, ScmapFormat.DescribeFourCc(kind));
            size.ShouldBe(0, ScmapFormat.DescribeFourCc(kind));
        }

        // These two have no producer and are never written.
        Should.Throw<InvalidOperationException>(() => FindSection(file, ScmapFormat.RegionIndexSection));
        Should.Throw<InvalidOperationException>(() => FindSection(file, ScmapFormat.BrushModelSection));

        ScmapProbe probe = ScmapProbe.Read(file);
        probe.SkippedSections.ShouldBe(ScmapFixture.ReservedEmptySections);
    }

    [Fact]
    public void A_section_code_this_build_has_no_meaning_for_is_skipped_rather_than_refused()
    {
        // A section a later cooker adds must not make the map unreadable.
        byte[] file = ScmapFixture.Build();
        (int offset, _) = FindSection(file, ScmapFormat.EntitySection);
        int record = TableRecordOffset(file, ScmapFormat.EntitySection);

        BinaryPrimitives.WriteUInt32LittleEndian(
            file.AsSpan(record), 'Z' | ('Z' << 8) | ('Z' << 16) | ((uint)'Z' << 24));

        offset.ShouldBeGreaterThan(0);

        ScmapProbe probe = ScmapProbe.Read(file);
        probe.SkippedSections.ShouldBe(ScmapFixture.ReservedEmptySections);
        probe.NodeNames.ShouldBe(ScmapFixture.NodeNames);
    }

    [Fact]
    public void A_retired_payload_kind_is_refused_by_the_cook_naming_the_node()
    {
        ScmapBuilder builder = ScmapFixture.CreateBuilder();

        Should.Throw<InvalidOperationException>(() => builder.AddNode(new ScmapNodeSource(
            Guid.NewGuid(), "DoorLeaf", 0, default, ScmapPayloadKind.RetiredBrushModel)))
            .Message.ShouldContain("DoorLeaf");
    }

    [Fact]
    public void A_retired_payload_kind_in_a_FILE_is_refused_naming_the_node()
    {
        // Payload kind 3 is retired and must never be reused.
        byte[] file = ScmapFixture.Build();
        (int offset, _) = FindSection(file, ScmapFormat.NodeSection);

        const int NodeIndex = 2;
        int kindOffset = offset + ScmapFormat.NodePreambleSize + (NodeIndex * ScmapFormat.NodeRecordSize) + 0x40;
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(kindOffset), 3);

        ScmapFormatException failure = Should.Throw<ScmapFormatException>(() => ScmapProbe.Read(file));
        failure.Message.ShouldContain(ScmapFixture.NodeNames[NodeIndex]);
        failure.Message.ShouldContain("Recook");
    }

    [Fact]
    public void A_material_reaches_the_asset_table_as_a_PATH_and_never_as_its_interned_id()
    {
        // An id is per-process interning order and means nothing in a file.
        // The unrelated intern stops ids and table indices agreeing by coincidence.
        MaterialRegistry.Intern("Materials/scmap_unrelated_first.spectramat");

        MaterialRef wall = MaterialRegistry.Intern("Materials/scmap_wall.spectramat");
        MaterialRef trim = MaterialRegistry.Intern("Materials/scmap_trim.spectramat");

        var builder = new ScmapBuilder("MaterialOrder");
        uint wallIndex = builder.AddMaterial(wall, 0x11ul);
        uint trimIndex = builder.AddMaterial(trim, 0x22ul);

        wallIndex.ShouldBe(0u);
        trimIndex.ShouldBe(1u);
        ((int)wallIndex).ShouldNotBe(wall.Id);
        ((int)trimIndex).ShouldNotBe(trim.Id);

        ScmapProbe probe = ScmapProbe.Read(builder.Build(UInt128.Zero, EngineInfo.MapFormatVersion));

        probe.Assets.Count.ShouldBe(2);
        probe.Assets[0].Path.ShouldBe("Materials/scmap_wall.spectramat");
        probe.Assets[1].Path.ShouldBe("Materials/scmap_trim.spectramat");
        probe.Assets[0].Kind.ShouldBe(PackEntryKind.Material);

        probe.Strings[0].ShouldBe(string.Empty);
        probe.Strings[1].ShouldBe("MaterialOrder");
        probe.Strings[2].ShouldBe("Materials/scmap_wall.spectramat");
        probe.Strings[3].ShouldBe("Materials/scmap_trim.spectramat");
    }

    [Fact]
    public void The_default_material_has_no_asset_row_and_says_so()
    {
        var builder = new ScmapBuilder("Defaults");

        Should.Throw<InvalidOperationException>(() => builder.AddMaterial(MaterialRef.Default))
            .Message.ShouldContain("names no path");
    }

    [Fact]
    public void One_path_added_twice_is_one_row()
    {
        var builder = new ScmapBuilder("Dedupe");
        uint first = builder.AddAsset(new ScmapAssetSource(PackEntryKind.Image, "Textures/a.png"));
        uint again = builder.AddAsset(new ScmapAssetSource(PackEntryKind.Image, "Textures\\a.png"));

        again.ShouldBe(first);
        builder.AssetCount.ShouldBe(1);

        Should.Throw<InvalidOperationException>(
            () => builder.AddAsset(new ScmapAssetSource(PackEntryKind.Model, "Textures/a.png")));
    }

    [Fact]
    public void A_parent_index_that_does_not_precede_its_child_is_refused()
    {
        var builder = new ScmapBuilder("Order");

        Should.Throw<InvalidOperationException>(() => builder.AddNode(
            new ScmapNodeSource(Guid.NewGuid(), "Orphan", 0, default, ScmapPayloadKind.None)));

        builder.AddNode(new ScmapNodeSource(Guid.NewGuid(), "Root", -1, default, ScmapPayloadKind.None));

        Should.Throw<InvalidOperationException>(() => builder.AddNode(
            new ScmapNodeSource(Guid.NewGuid(), "Forward", 1, default, ScmapPayloadKind.None)));
    }

    [Fact]
    public void One_cell_added_twice_is_refused()
    {
        var builder = new ScmapBuilder("Twice");
        var cell = new ChunkCoord(3, -1, 7);
        var bounds = new Aabb(Vector3.Zero, Vector3.One);

        builder.AddChunk(new ScmapChunkSource(cell, bounds));

        Should.Throw<InvalidOperationException>(
                () => builder.AddChunk(new ScmapChunkSource(cell, bounds)))
            .Message.ShouldContain("already in the chunk directory");

        builder.ChunkCount.ShouldBe(1);
    }

    [Fact]
    public void A_cell_the_builder_REFUSED_may_still_be_added()
    {
        // The duplicate set must only record a cell once every refusal has passed.
        var builder = new ScmapBuilder("Rejected");
        var cell = new ChunkCoord(2, 2, 2);
        var bounds = new Aabb(Vector3.Zero, Vector3.One);

        Should.Throw<InvalidOperationException>(() => builder.AddChunk(new ScmapChunkSource(
            cell,
            bounds,
            [
                new ScmapSubmeshSource(3, new float[8], [0, 0, 0]),
                new ScmapSubmeshSource(1, new float[8], [0, 0, 0]),
            ])));

        builder.AddChunk(new ScmapChunkSource(cell, bounds));
        builder.ChunkCount.ShouldBe(1);
    }

    [Fact]
    public void A_cell_whose_submeshes_are_out_of_ascending_asset_order_is_refused()
    {
        // Asset index order, not material id order: ids vary per process.
        var builder = new ScmapBuilder("Blobs");

        Should.Throw<InvalidOperationException>(() => builder.AddChunk(new ScmapChunkSource(
            new ChunkCoord(0, 0, 0),
            new Aabb(Vector3.Zero, Vector3.One),
            [
                new ScmapSubmeshSource(3, new float[8], [0, 0, 0]),
                new ScmapSubmeshSource(1, new float[8], [0, 0, 0]),
            ])))
            .Message.ShouldContain("ascending asset order");
    }

    [Fact]
    public void A_compile_constant_that_does_not_match_this_engine_is_refused_naming_both_numbers()
    {
        byte[] file = ScmapFixture.Build();
        (int offset, _) = FindSection(file, ScmapFormat.MetaSection);

        BinaryPrimitives.WriteSingleLittleEndian(file.AsSpan(offset + 0x08), 64f);

        ScmapFormatException failure = Should.Throw<ScmapFormatException>(() => ScmapProbe.Read(file));
        failure.Message.ShouldContain("cell size");
        failure.Message.ShouldContain("64");
        failure.Message.ShouldContain(ChunkCoord.CellSize.ToString());
        failure.Message.ShouldContain("Recook");
    }

    [Fact]
    public void A_section_that_does_not_start_on_a_sixteen_byte_boundary_is_refused()
    {
        // Payloads are cast in place, so sections must start 16-aligned.
        byte[] file = ScmapFixture.Build();
        int record = TableRecordOffset(file, ScmapFormat.NodeSection);

        ulong offset = BinaryPrimitives.ReadUInt64LittleEndian(file.AsSpan(record + 0x08));
        BinaryPrimitives.WriteUInt64LittleEndian(file.AsSpan(record + 0x08), offset + 1);

        Should.Throw<ScmapFormatException>(() => ScmapProbe.Read(file))
            .Message.ShouldContain("NODE");
    }

    [Fact]
    public void A_format_version_this_engine_does_not_read_is_refused_rather_than_carried()
    {
        // Exact match, not a minimum: a compiled map can always be recooked.
        byte[] file = ScmapFixture.Build();
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(0x04), 99);

        Should.Throw<ScmapFormatException>(() => ScmapProbe.Read(file))
            .Message.ShouldContain("Recook");
    }

    [Fact]
    public void A_file_that_is_not_a_compiled_map_is_refused_by_its_own_first_four_bytes()
    {
        byte[] file = ScmapFixture.Build();
        file[0] = (byte)'X';

        Should.Throw<ScmapFormatException>(() => ScmapProbe.Read(file))
            .Message.ShouldContain("SCMP");
    }

    [Fact]
    public void Two_builds_of_one_fixture_are_byte_identical_in_this_process()
    {
        // In-process only. ScmapDeterminismTests covers two processes, where the
        // string hash seed differs.
        ScmapFixture.Build().ShouldBe(ScmapFixture.Build());
    }

    private static uint Bits(float value) => BitConverter.SingleToUInt32Bits(value);

    private static int TableRecordOffset(byte[] file, uint kind)
    {
        ScmapHeader header = MemoryMarshal.Read<ScmapHeader>(file);
        for (int i = 0; i < header.SectionCount; i++)
        {
            int record = ScmapFormat.SectionTableOffset + (i * ScmapFormat.SectionSize);
            if (BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(record)) == kind) return record;
        }

        throw new InvalidOperationException($"No '{ScmapFormat.DescribeFourCc(kind)}' section in the file.");
    }

    private static (int Offset, long Size) FindSection(byte[] file, uint kind)
    {
        int record = TableRecordOffset(file, kind);
        ScmapSection section = MemoryMarshal.Read<ScmapSection>(file.AsSpan(record));
        return ((int)section.Offset, (long)section.Size);
    }
}
