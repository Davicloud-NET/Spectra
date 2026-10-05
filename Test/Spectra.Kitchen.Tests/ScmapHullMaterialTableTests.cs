using System;
using System.Buffers.Binary;
using System.Linq;
using System.Numerics;
using Spectra.Kitchen.Maps;
using SpectraEngine.Core;
using SpectraEngine.Core.Assets.Packs;
using SpectraEngine.Core.Maps.Compiled;

namespace Spectra.Kitchen.Tests;

/// <summary>
/// The table that says what each collision hull face is made of, written and
/// read back. It is optional: a map without it reads, and so does a map read
/// by something that does not know it. Reader refusals are tested by editing
/// the bytes of a valid file.
/// </summary>
public class ScmapHullMaterialTableTests
{
    // The fixture's two hulls: six planes and five.
    private const int FaceCount = 11;

    private const uint UnknownCode = 'Z' | ('Z' << 8) | ('Z' << 16) | ((uint)'Z' << 24);

    [Fact]
    public void Face_materials_read_back_one_per_hull_plane()
    {
        ScmapProbe probe = ScmapProbe.Read(ScmapFixture.Build(withHullMaterials: true));

        probe.HasHullMaterials.ShouldBeTrue();
        probe.Hulls.Count.ShouldBe(ScmapFixture.Hulls.Length);

        for (int i = 0; i < probe.Hulls.Count; i++)
        {
            probe.Hulls[i].FaceAssets.ShouldBe(ScmapFixture.HullFaceAssets[i], $"hull {i}");
            probe.Hulls[i].FaceAssets.Length.ShouldBe(probe.Hulls[i].Planes.Length);
        }

        // A row is a material, or the sentinel for a face that names none.
        foreach (uint row in probe.Hulls.SelectMany(hull => hull.FaceAssets))
        {
            if (row != ScmapFormat.NoAssetIndex)
                probe.Assets[(int)row].Kind.ShouldBe(PackEntryKind.Material);
        }
    }

    [Fact]
    public void The_section_is_the_documented_size_with_its_records_at_the_documented_offsets()
    {
        byte[] file = ScmapFixture.Build(withHullMaterials: true);
        (int colm, int size) = ScmapSurgery.Section(file, ScmapFormat.HullMaterialSection);

        // 16 + 11 * 4.
        size.ShouldBe(60);

        ScmapSurgery.U32(file, colm + 0x00).ShouldBe((uint)FaceCount);
        ScmapSurgery.U32(file, colm + 0x04).ShouldBe(0u);
        BinaryPrimitives.ReadUInt64LittleEndian(file.AsSpan(colm + 0x08)).ShouldBe(0ul);

        // The box's six faces, then the wedge's five.
        ScmapSurgery.U32(file, colm + 16).ShouldBe(0u);
        ScmapSurgery.U32(file, colm + 16 + (3 * 4)).ShouldBe(0xFFFFFFFFu, "the box's fourth face names none");
        ScmapSurgery.U32(file, colm + 16 + (6 * 4)).ShouldBe(1u, "the wedge's first face");
        ScmapSurgery.U32(file, colm + 16 + (10 * 4)).ShouldBe(1u, "the wedge's last face");
    }

    [Fact]
    public void The_section_sits_right_after_the_hulls_it_describes()
    {
        byte[] file = ScmapFixture.Build(withHullMaterials: true);

        int collision = ScmapSurgery.TableRecord(file, ScmapFormat.CollisionSection);
        int materials = ScmapSurgery.TableRecord(file, ScmapFormat.HullMaterialSection);

        materials.ShouldBe(collision + ScmapFormat.SectionSize);
    }

    [Fact]
    public void A_map_whose_hulls_name_no_materials_carries_no_section_and_still_reads()
    {
        byte[] file = ScmapFixture.Build();

        HasSection(file, ScmapFormat.HullMaterialSection).ShouldBeFalse();

        ScmapProbe probe = ScmapProbe.Read(file);
        probe.HasHullMaterials.ShouldBeFalse();
        probe.Hulls.ShouldAllBe(hull => hull.FaceAssets.Length == 0);
        probe.SkippedSections.ShouldBe(ScmapFixture.ReservedEmptySections);
    }

    [Fact]
    public void Naming_the_faces_changes_nothing_else_in_the_file()
    {
        ScmapProbe without = ScmapProbe.Read(ScmapFixture.Build());
        ScmapProbe with = ScmapProbe.Read(ScmapFixture.Build(withHullMaterials: true));

        with.Strings.ShouldBe(without.Strings);
        with.Assets.ShouldBe(without.Assets);
        with.Nodes.ShouldBe(without.Nodes);
        with.Header.FormatVersion.ShouldBe(without.Header.FormatVersion);
        with.SkippedSections.ShouldBe(without.SkippedSections);

        for (int i = 0; i < with.Hulls.Count; i++)
            with.Hulls[i].Planes.ShouldBe(without.Hulls[i].Planes);
    }

    [Fact]
    public void A_reader_that_does_not_know_the_section_steps_over_it()
    {
        byte[] file = ScmapFixture.Build(withHullMaterials: true);

        // What an older engine sees: a code it has no meaning for.
        ScmapSurgery.SetU32(file, ScmapSurgery.TableRecord(file, ScmapFormat.HullMaterialSection), UnknownCode);

        ScmapProbe probe = ScmapProbe.Read(file);

        probe.HasHullMaterials.ShouldBeFalse();
        probe.SkippedSections.ShouldBe(ScmapFixture.ReservedEmptySections + 1);
        probe.Hulls.Count.ShouldBe(ScmapFixture.Hulls.Length);
        probe.Hulls[0].Planes.ShouldBe(ScmapFixture.Hulls[0].Planes);
    }

    [Fact]
    public void A_section_with_no_preamble_is_refused()
    {
        byte[] file = ScmapFixture.Build(withHullMaterials: true);
        ScmapSurgery.Resize(file, ScmapFormat.HullMaterialSection, 0);

        Should.Throw<ScmapFormatException>(() => ScmapProbe.Read(file))
            .Message.ShouldContain("0-byte COLM section");
    }

    [Fact]
    public void A_face_count_that_is_not_the_plane_count_is_refused()
    {
        byte[] file = ScmapFixture.Build(withHullMaterials: true);
        (int colm, _) = ScmapSurgery.Section(file, ScmapFormat.HullMaterialSection);
        ScmapSurgery.SetU32(file, colm, 10);

        ScmapFormatException refused = Should.Throw<ScmapFormatException>(() => ScmapProbe.Read(file));
        refused.Message.ShouldContain("10 collision hull faces");
        refused.Message.ShouldContain($"{FaceCount} planes");
    }

    [Fact]
    public void Records_reaching_past_the_section_are_refused()
    {
        byte[] file = ScmapFixture.Build(withHullMaterials: true);
        ScmapSurgery.Resize(file, ScmapFormat.HullMaterialSection, 16 + 4);

        Should.Throw<ScmapFormatException>(() => ScmapProbe.Read(file))
            .Message.ShouldContain("of a 20-byte COLM section");
    }

    [Fact]
    public void A_face_naming_a_row_past_the_asset_table_is_refused()
    {
        byte[] file = ScmapFixture.Build(withHullMaterials: true);
        (int colm, _) = ScmapSurgery.Section(file, ScmapFormat.HullMaterialSection);
        ScmapSurgery.SetU32(file, colm + 16 + (2 * 4), 99);

        Should.Throw<ScmapFormatException>(() => ScmapProbe.Read(file))
            .Message.ShouldContain("face 2 names asset 99 of a 4-asset table");
    }

    [Fact]
    public void A_face_naming_a_row_that_is_not_a_material_is_refused()
    {
        byte[] file = ScmapFixture.Build(withHullMaterials: true);
        (int colm, _) = ScmapSurgery.Section(file, ScmapFormat.HullMaterialSection);

        // Row 3 is the crate's model.
        ScmapSurgery.SetU32(file, colm + 16, 3);

        ScmapFormatException refused = Should.Throw<ScmapFormatException>(() => ScmapProbe.Read(file));
        refused.Message.ShouldContain("face 0 names asset 3");
        refused.Message.ShouldContain("not a material");
    }

    [Fact]
    public void A_hull_naming_more_or_fewer_faces_than_it_has_planes_is_refused_by_the_cook()
    {
        var builder = new ScmapBuilder("Faces");
        builder.AddNode(new ScmapNodeSource(Guid.NewGuid(), "A", -1, default, ScmapPayloadKind.StaticWorldBrush));

        Plane[] box = ScmapFixture.Hulls[0].Planes;

        Should.Throw<InvalidOperationException>(
                () => builder.AddCollisionHull(new ScmapCollisionHullSource(0, box, [0, 0, 0])))
            .Message.ShouldContain("6 planes and names materials for 3 faces");

        builder.CollisionHullCount.ShouldBe(0);
    }

    [Fact]
    public void A_map_where_only_some_hulls_name_their_faces_is_refused_when_the_file_is_built()
    {
        ScmapBuilder builder = TwoBrushes(out Plane[] box);
        builder.AddCollisionHull(new ScmapCollisionHullSource(0, box, Sentinels(box.Length)));
        builder.AddCollisionHull(new ScmapCollisionHullSource(1, box));

        Should.Throw<InvalidOperationException>(() => builder.Build(UInt128.Zero, EngineInfo.MapFormatVersion))
            .Message.ShouldContain("1 of 2 collision hulls");
    }

    [Fact]
    public void A_face_naming_something_that_is_not_a_material_is_refused_when_the_file_is_built()
    {
        ScmapBuilder builder = TwoBrushes(out Plane[] box);
        uint model = builder.AddAsset(new ScmapAssetSource(PackEntryKind.Model, "Models/crate.smodel"));

        uint[] faces = Sentinels(box.Length);
        faces[2] = model;

        builder.AddCollisionHull(new ScmapCollisionHullSource(0, box, Sentinels(box.Length)));
        builder.AddCollisionHull(new ScmapCollisionHullSource(1, box, faces));

        InvalidOperationException refused = Should.Throw<InvalidOperationException>(
            () => builder.Build(UInt128.Zero, EngineInfo.MapFormatVersion));

        refused.Message.ShouldContain("node 1");
        refused.Message.ShouldContain("not a material row");
    }

    [Fact]
    public void Faces_that_all_name_no_material_still_write_the_section()
    {
        ScmapBuilder builder = TwoBrushes(out Plane[] box);
        builder.AddCollisionHull(new ScmapCollisionHullSource(0, box, Sentinels(box.Length)));
        builder.AddCollisionHull(new ScmapCollisionHullSource(1, box, Sentinels(box.Length)));

        ScmapProbe probe = ScmapProbe.Read(builder.Build(UInt128.Zero, EngineInfo.MapFormatVersion));

        probe.HasHullMaterials.ShouldBeTrue();
        probe.Hulls[1].FaceAssets.ShouldAllBe(row => row == ScmapFormat.NoAssetIndex);
    }

    private static ScmapBuilder TwoBrushes(out Plane[] box)
    {
        var builder = new ScmapBuilder("Faces");
        builder.AddNode(new ScmapNodeSource(Guid.NewGuid(), "A", -1, default, ScmapPayloadKind.StaticWorldBrush));
        builder.AddNode(new ScmapNodeSource(Guid.NewGuid(), "B", -1, default, ScmapPayloadKind.StaticWorldBrush));
        box = ScmapFixture.Hulls[0].Planes;
        return builder;
    }

    private static uint[] Sentinels(int count)
    {
        var rows = new uint[count];
        Array.Fill(rows, ScmapFormat.NoAssetIndex);
        return rows;
    }

    private static bool HasSection(byte[] file, uint kind)
    {
        uint count = BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(0x0C));
        for (int i = 0; i < count; i++)
        {
            if (ScmapSurgery.U32(file, ScmapFormat.SectionTableOffset + (i * ScmapFormat.SectionSize)) == kind)
                return true;
        }

        return false;
    }
}
