using System;
using System.Buffers.Binary;
using System.Numerics;
using Spectra.Kitchen.Maps;
using SpectraEngine.Core;
using SpectraEngine.Core.Maps.Compiled;

namespace Spectra.Kitchen.Tests;

/// <summary>
/// The collision hull table of a compiled map, written and read back. Offsets
/// are literals from the format, and reader refusals are tested by editing the
/// bytes of a valid file.
/// </summary>
public class ScmapCollisionTableTests
{
    // The fixture's counts: a six-plane box and a five-plane wedge.
    private const int HullCount = 2;
    private const int PlaneCount = 11;

    // Where the fixture's plane array starts: 16 + 2 * 16.
    private const int PlaneArray = 48;

    [Fact]
    public void Hulls_read_back_with_their_planes_bit_for_bit()
    {
        ScmapProbe probe = ScmapProbe.Read(ScmapFixture.Build());

        probe.Hulls.Count.ShouldBe(ScmapFixture.Hulls.Length);
        for (int i = 0; i < probe.Hulls.Count; i++)
        {
            probe.Hulls[i].NodeIndex.ShouldBe((uint)ScmapFixture.Hulls[i].NodeIndex, $"hull {i}");
            probe.Hulls[i].Planes.ShouldBe(ScmapFixture.Hulls[i].Planes);
        }

        // Each hull sits on a baked brush, and no baked brush is without one.
        foreach (ScmapProbe.HullCopy hull in probe.Hulls)
            probe.Nodes[(int)hull.NodeIndex].BakedIntoChunks.ShouldBeTrue();
    }

    [Fact]
    public void The_collision_section_is_the_documented_size_with_its_records_at_the_documented_offsets()
    {
        byte[] file = ScmapFixture.Build();
        (int coll, int size) = ScmapSurgery.Section(file, ScmapFormat.CollisionSection);

        // 16 + 2 * 16 + 11 * 16.
        size.ShouldBe(224);

        ScmapSurgery.U32(file, coll + 0x00).ShouldBe((uint)HullCount);
        ScmapSurgery.U32(file, coll + 0x04).ShouldBe((uint)PlaneCount);
        BinaryPrimitives.ReadUInt64LittleEndian(file.AsSpan(coll + 0x08)).ShouldBe(0ul);

        // The second record: the wedge on node 3, after the box's six planes.
        int wedge = coll + 16 + 16;
        ScmapSurgery.U32(file, wedge + 0x00).ShouldBe(3u, "node");
        ScmapSurgery.U32(file, wedge + 0x04).ShouldBe(5u, "plane count");
        ScmapSurgery.U32(file, wedge + 0x08).ShouldBe(6u, "first plane");
        ScmapSurgery.U32(file, wedge + 0x0C).ShouldBe(0u, "reserved");

        // Its first plane: the wedge's floor, normal then distance.
        int plane = coll + PlaneArray + (6 * 16);
        BitConverter.ToSingle(file, plane + 0x00).ShouldBe(0f);
        BitConverter.ToSingle(file, plane + 0x04).ShouldBe(-1f);
        BitConverter.ToSingle(file, plane + 0x08).ShouldBe(0f);
        BitConverter.ToSingle(file, plane + 0x0C).ShouldBe(-0.5f);
    }

    [Fact]
    public void A_map_with_no_world_brushes_carries_the_preamble_and_nothing_else()
    {
        var builder = new ScmapBuilder("Bare");
        builder.AddNode(new ScmapNodeSource(Guid.NewGuid(), "Root", -1, default, ScmapPayloadKind.None));
        byte[] file = builder.Build(UInt128.Zero, EngineInfo.MapFormatVersion);

        ScmapSurgery.Section(file, ScmapFormat.CollisionSection).Size.ShouldBe(16);
        ScmapProbe.Read(file).Hulls.ShouldBeEmpty();
    }

    [Fact]
    public void A_file_without_a_collision_section_is_refused()
    {
        byte[] file = ScmapFixture.Build();

        // A code nothing knows: the reader steps over the record.
        ScmapSurgery.SetU32(
            file,
            ScmapSurgery.TableRecord(file, ScmapFormat.CollisionSection),
            'Z' | ('Z' << 8) | ('Z' << 16) | ((uint)'Z' << 24));

        Should.Throw<ScmapFormatException>(() => ScmapProbe.Read(file)).Message.ShouldContain("COLL");
    }

    [Fact]
    public void A_collision_section_with_no_preamble_is_refused()
    {
        byte[] file = ScmapFixture.Build();
        ScmapSurgery.Resize(file, ScmapFormat.CollisionSection, 0);

        Should.Throw<ScmapFormatException>(() => ScmapProbe.Read(file))
            .Message.ShouldContain("0-byte COLL section");
    }

    [Fact]
    public void A_count_reaching_past_the_section_is_refused()
    {
        byte[] file = ScmapFixture.Build();
        (int coll, _) = ScmapSurgery.Section(file, ScmapFormat.CollisionSection);
        ScmapSurgery.SetU32(file, coll + 0x04, 1000);

        Should.Throw<ScmapFormatException>(() => ScmapProbe.Read(file))
            .Message.ShouldContain("over 1000 planes");
    }

    [Fact]
    public void A_hull_with_fewer_than_four_planes_is_refused()
    {
        byte[] file = ScmapFixture.Build();
        (int coll, _) = ScmapSurgery.Section(file, ScmapFormat.CollisionSection);
        ScmapSurgery.SetU32(file, coll + 16 + 0x04, 3);

        Should.Throw<ScmapFormatException>(() => ScmapProbe.Read(file))
            .Message.ShouldContain("declares 3 planes");
    }

    [Fact]
    public void A_hull_claiming_planes_past_the_table_is_refused()
    {
        byte[] file = ScmapFixture.Build();
        (int coll, _) = ScmapSurgery.Section(file, ScmapFormat.CollisionSection);
        ScmapSurgery.SetU32(file, coll + 16 + 16 + 0x08, 9);

        Should.Throw<ScmapFormatException>(() => ScmapProbe.Read(file))
            .Message.ShouldContain($"of a {PlaneCount}-plane table");
    }

    [Fact]
    public void A_baked_brush_with_no_hull_is_refused_naming_the_node()
    {
        // A wall that is drawn and can be walked through.
        byte[] file = ScmapFixture.Build();
        (int coll, _) = ScmapSurgery.Section(file, ScmapFormat.CollisionSection);

        // The first hull moves off "Wall" onto the node after it.
        ScmapSurgery.SetU32(file, coll + 16, 3);

        ScmapFormatException refused = Should.Throw<ScmapFormatException>(() => ScmapProbe.Read(file));
        refused.Message.ShouldContain("node 2 ('Wall')");
        refused.Message.ShouldContain("no collision hull");
        refused.Message.ShouldContain("Recook");
    }

    [Fact]
    public void Two_hulls_on_one_brush_are_refused()
    {
        byte[] file = ScmapFixture.Build();
        (int coll, _) = ScmapSurgery.Section(file, ScmapFormat.CollisionSection);

        // The second hull takes the first one's node.
        ScmapSurgery.SetU32(file, coll + 16 + 16, 2);

        Should.Throw<ScmapFormatException>(() => ScmapProbe.Read(file))
            .Message.ShouldContain("collision hull 1 names node 2");
    }

    [Fact]
    public void A_hull_on_a_node_that_is_not_a_baked_brush_is_refused()
    {
        byte[] file = ScmapFixture.Build();
        (int nodes, _) = ScmapSurgery.Section(file, ScmapFormat.NodeSection);

        // "Cut" stops being a baked brush. Its hull is still in the table.
        BinaryPrimitives.WriteUInt16LittleEndian(
            file.AsSpan(nodes + 16 + (3 * 80) + 0x40), (ushort)ScmapPayloadKind.None);

        Should.Throw<ScmapFormatException>(() => ScmapProbe.Read(file))
            .Message.ShouldContain("collision hull 1 names node 3, which is not the next baked world brush");
    }

    [Fact]
    public void A_hull_with_too_few_planes_or_out_of_node_order_is_refused_by_the_cook()
    {
        var builder = new ScmapBuilder("Order");
        builder.AddNode(new ScmapNodeSource(Guid.NewGuid(), "A", -1, default, ScmapPayloadKind.StaticWorldBrush));
        builder.AddNode(new ScmapNodeSource(Guid.NewGuid(), "B", -1, default, ScmapPayloadKind.StaticWorldBrush));

        Plane[] box = ScmapFixture.Hulls[0].Planes;

        Should.Throw<InvalidOperationException>(
                () => builder.AddCollisionHull(new ScmapCollisionHullSource(0, box[..3])))
            .Message.ShouldContain("3 planes");

        builder.AddCollisionHull(new ScmapCollisionHullSource(1, box));

        Should.Throw<InvalidOperationException>(
                () => builder.AddCollisionHull(new ScmapCollisionHullSource(0, box)))
            .Message.ShouldContain("node order");

        builder.CollisionHullCount.ShouldBe(1);
    }

    [Fact]
    public void A_baked_brush_with_no_hull_is_refused_when_the_file_is_built()
    {
        var builder = new ScmapBuilder("Hole");
        builder.AddNode(new ScmapNodeSource(Guid.NewGuid(), "Floor", -1, default, ScmapPayloadKind.StaticWorldBrush));

        InvalidOperationException refused = Should.Throw<InvalidOperationException>(
            () => builder.Build(UInt128.Zero, EngineInfo.MapFormatVersion));

        refused.Message.ShouldContain("'Floor'");
        refused.Message.ShouldContain("no collision hull");
    }

    [Fact]
    public void A_hull_on_a_part_or_on_a_node_that_was_never_added_is_refused_when_the_file_is_built()
    {
        Plane[] box = ScmapFixture.Hulls[0].Planes;

        var onPart = new ScmapBuilder("Part");
        onPart.AddNode(new ScmapNodeSource(Guid.NewGuid(), "Door", -1, default, ScmapPayloadKind.PartBrush));
        onPart.AddCollisionHull(new ScmapCollisionHullSource(0, box));

        Should.Throw<InvalidOperationException>(() => onPart.Build(UInt128.Zero, EngineInfo.MapFormatVersion))
            .Message.ShouldContain("names node 0, which is not a baked world brush");

        var missing = new ScmapBuilder("Missing");
        missing.AddNode(new ScmapNodeSource(Guid.NewGuid(), "A", -1, default, ScmapPayloadKind.None));
        missing.AddCollisionHull(new ScmapCollisionHullSource(4, box));

        Should.Throw<InvalidOperationException>(() => missing.Build(UInt128.Zero, EngineInfo.MapFormatVersion))
            .Message.ShouldContain("names node 4");
    }
}
