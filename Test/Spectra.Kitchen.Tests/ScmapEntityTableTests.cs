using System;
using System.Buffers.Binary;
using System.Linq;
using Spectra.Kitchen.Maps;
using SpectraEngine.Core;
using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Maps.Compiled;

namespace Spectra.Kitchen.Tests;

/// <summary>
/// The entity, keyvalue and connection tables of a compiled map and the node
/// flag bits, written and read back. Offsets are literals from the format, and
/// reader refusals are tested by editing the bytes of a valid file.
/// </summary>
public class ScmapEntityTableTests
{
    // The fixture's counts.
    private const int EntityCount = 3;
    private const int KeyvalueCount = 4;
    private const int ConnectionCount = 3;

    // Where the fixture's keyvalue array starts: 16 + 3 * 24 = 88, padded to 96.
    private const int KeyvalueArray = 96;

    [Fact]
    public void Entities_read_back_with_keyvalues_and_wires_in_authored_order()
    {
        ScmapProbe probe = ScmapProbe.Read(ScmapFixture.Build());

        probe.Entities.Count.ShouldBe(ScmapFixture.Entities.Length);
        for (int i = 0; i < probe.Entities.Count; i++)
        {
            ScmapEntitySource expected = ScmapFixture.Entities[i];
            ScmapProbe.EntityCopy actual = probe.Entities[i];

            actual.NodeIndex.ShouldBe((uint)expected.NodeIndex, $"entity {i}");
            actual.ClassName.ShouldBe(expected.ClassName, $"entity {i}");
            actual.Keyvalues.ShouldBe(expected.Keyvalues);
            actual.Connections.ShouldBe(expected.Connections);
        }

        // A repeated key is two records, not one.
        probe.Entities[1].Keyvalues.Select(pair => pair.Key).ShouldBe(["delay", "tag", "tag"]);
    }

    [Fact]
    public void The_entity_section_is_the_documented_size_with_its_records_at_the_documented_offsets()
    {
        byte[] file = ScmapFixture.Build();
        ScmapProbe probe = ScmapProbe.Read(file);
        (int entt, int size) = ScmapSurgery.Section(file, ScmapFormat.EntitySection);

        // Preamble and records padded to 96, then 4 keyvalues of 8 bytes.
        size.ShouldBe(128);

        ScmapSurgery.U32(file, entt + 0x00).ShouldBe((uint)EntityCount);
        ScmapSurgery.U32(file, entt + 0x04).ShouldBe((uint)KeyvalueCount);
        BinaryPrimitives.ReadUInt64LittleEndian(file.AsSpan(entt + 0x08)).ShouldBe(0ul);

        // The second record: the relay on node 5.
        int relay = entt + 16 + 24;
        ScmapSurgery.U32(file, relay + 0x00).ShouldBe(5u);
        probe.Strings[(int)ScmapSurgery.U32(file, relay + 0x04)].ShouldBe("logic_relay");
        ScmapSurgery.U32(file, relay + 0x08).ShouldBe(0u, "first keyvalue");
        ScmapSurgery.U32(file, relay + 0x0C).ShouldBe(3u, "keyvalue count");
        ScmapSurgery.U32(file, relay + 0x10).ShouldBe(1u, "first wire");
        ScmapSurgery.U32(file, relay + 0x14).ShouldBe(2u, "wire count");

        // Padding is written as zeros.
        file.AsSpan(entt + 88, 8).ToArray().ShouldBe(new byte[8]);

        // The second keyvalue: tag = first.
        int tag = entt + KeyvalueArray + 8;
        probe.Strings[(int)ScmapSurgery.U32(file, tag + 0x00)].ShouldBe("tag");
        probe.Strings[(int)ScmapSurgery.U32(file, tag + 0x04)].ShouldBe("first");
    }

    [Fact]
    public void The_connection_section_is_the_documented_size_with_its_records_at_the_documented_offsets()
    {
        byte[] file = ScmapFixture.Build();
        ScmapProbe probe = ScmapProbe.Read(file);
        (int econ, int size) = ScmapSurgery.Section(file, ScmapFormat.EntityConnectionSection);

        // 16 + 3 * 24.
        size.ShouldBe(88);

        ScmapSurgery.U32(file, econ).ShouldBe((uint)ConnectionCount);
        file.AsSpan(econ + 4, 12).ToArray().ShouldBe(new byte[12]);

        // The third record: the relay's delayed, counted wire.
        int wire = econ + 16 + (2 * 24);
        probe.Strings[(int)ScmapSurgery.U32(file, wire + 0x00)].ShouldBe("OnTrigger");
        probe.Strings[(int)ScmapSurgery.U32(file, wire + 0x04)].ShouldBe("zeta_*");
        probe.Strings[(int)ScmapSurgery.U32(file, wire + 0x08)].ShouldBe("Kill");
        probe.Strings[(int)ScmapSurgery.U32(file, wire + 0x0C)].ShouldBe("now");
        BitConverter.ToSingle(file, wire + 0x10).ShouldBe(1.5f);
        BitConverter.ToInt32(file, wire + 0x14).ShouldBe(3);

        // An empty parameter is string 0, and no limit is -1.
        int first = econ + 16 + 24;
        ScmapSurgery.U32(file, first + 0x0C).ShouldBe(0u);
        BitConverter.ToInt32(file, first + 0x14).ShouldBe(EntityConnection.Infinite);
    }

    [Fact]
    public void The_four_node_flags_sit_in_bits_8_to_11_of_the_payload_flags()
    {
        byte[] file = ScmapFixture.Build();
        (int nodes, _) = ScmapSurgery.Section(file, ScmapFormat.NodeSection);

        // The flags are stored inverted: a node with every flag on writes no bit.
        PayloadFlags(file, nodes, 0).ShouldBe((ushort)0x0000);

        // "Wall": touch off is bit 10.
        PayloadFlags(file, nodes, 2).ShouldBe((ushort)0x0400);

        // "Lamp": collide, query and render off are bits 8, 9 and 11.
        PayloadFlags(file, nodes, 5).ShouldBe((ushort)0x0B00);

        ScmapNodeRecord lamp = ScmapProbe.Read(file).Nodes[5];
        lamp.PayloadFlags.ShouldBe(ScmapFixture.LampFlags);
        lamp.DeclaredRealm.ShouldBe(ScmapNodeRealm.Inherit);
        lamp.DeclaredState.ShouldBe(ScmapNodeState.Inherit);
    }

    [Fact]
    public void A_map_with_no_entities_carries_both_preambles_and_nothing_else()
    {
        var builder = new ScmapBuilder("Bare");
        builder.AddNode(new ScmapNodeSource(Guid.NewGuid(), "Root", -1, default, ScmapPayloadKind.None));
        byte[] file = builder.Build(UInt128.Zero, EngineInfo.MapFormatVersion);

        ScmapSurgery.Section(file, ScmapFormat.EntitySection).Size.ShouldBe(16);
        ScmapSurgery.Section(file, ScmapFormat.EntityConnectionSection).Size.ShouldBe(16);
        ScmapProbe.Read(file).Entities.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("ENTT")]
    [InlineData("ECON")]
    public void A_file_without_an_entity_section_is_refused(string section)
    {
        byte[] file = ScmapFixture.Build();

        // A code nothing knows: the reader steps over the record.
        ScmapSurgery.SetU32(
            file,
            ScmapSurgery.TableRecord(file, Code(section)),
            'Z' | ('Z' << 8) | ('Z' << 16) | ((uint)'Z' << 24));

        Should.Throw<ScmapFormatException>(() => ScmapProbe.Read(file)).Message.ShouldContain(section);
    }

    [Theory]
    [InlineData("ENTT")]
    [InlineData("ECON")]
    public void An_entity_section_with_no_preamble_is_refused(string section)
    {
        // What format version 1 wrote: the code claimed, the section empty.
        byte[] file = ScmapFixture.Build();
        ScmapSurgery.Resize(file, Code(section), 0);

        ScmapFormatException refused = Should.Throw<ScmapFormatException>(() => ScmapProbe.Read(file));
        refused.Message.ShouldContain($"0-byte {section} section");
    }

    [Fact]
    public void A_count_reaching_past_its_section_is_refused()
    {
        byte[] entities = ScmapFixture.Build();
        (int entt, _) = ScmapSurgery.Section(entities, ScmapFormat.EntitySection);
        ScmapSurgery.SetU32(entities, entt + 0x04, 1000);

        Should.Throw<ScmapFormatException>(() => ScmapProbe.Read(entities))
            .Message.ShouldContain("1000 keyvalues");

        byte[] wires = ScmapFixture.Build();
        (int econ, _) = ScmapSurgery.Section(wires, ScmapFormat.EntityConnectionSection);
        ScmapSurgery.SetU32(wires, econ, 1000);

        Should.Throw<ScmapFormatException>(() => ScmapProbe.Read(wires))
            .Message.ShouldContain("1000 connections");
    }

    [Fact]
    public void An_entity_naming_a_node_the_map_does_not_have_is_refused()
    {
        byte[] file = ScmapFixture.Build();
        (int entt, _) = ScmapSurgery.Section(file, ScmapFormat.EntitySection);

        // The last record, so the order check does not fire first.
        ScmapSurgery.SetU32(file, entt + 16 + (2 * 24), 99);

        Should.Throw<ScmapFormatException>(() => ScmapProbe.Read(file))
            .Message.ShouldContain("node 99");
    }

    [Fact]
    public void Entity_records_out_of_node_order_are_refused()
    {
        // The loader walks entities in step with the nodes.
        byte[] file = ScmapFixture.Build();
        (int entt, _) = ScmapSurgery.Section(file, ScmapFormat.EntitySection);

        int first = entt + 16;
        int second = first + 24;
        byte[] swap = file.AsSpan(first, 24).ToArray();
        file.AsSpan(second, 24).CopyTo(file.AsSpan(first));
        swap.CopyTo(file.AsSpan(second));

        Should.Throw<ScmapFormatException>(() => ScmapProbe.Read(file))
            .Message.ShouldContain("ascending node order");
    }

    [Fact]
    public void Two_entities_on_one_node_are_refused()
    {
        byte[] file = ScmapFixture.Build();
        (int entt, _) = ScmapSurgery.Section(file, ScmapFormat.EntitySection);

        // The third record takes the second's node.
        ScmapSurgery.SetU32(file, entt + 16 + (2 * 24), 5);

        Should.Throw<ScmapFormatException>(() => ScmapProbe.Read(file))
            .Message.ShouldContain("ascending node order");
    }

    [Fact]
    public void An_entity_claiming_records_past_either_table_is_refused()
    {
        byte[] keyvalues = ScmapFixture.Build();
        (int entt, _) = ScmapSurgery.Section(keyvalues, ScmapFormat.EntitySection);
        ScmapSurgery.SetU32(keyvalues, entt + 16 + 24 + 0x0C, 9);

        Should.Throw<ScmapFormatException>(() => ScmapProbe.Read(keyvalues))
            .Message.ShouldContain($"of a {KeyvalueCount}-keyvalue table");

        byte[] wires = ScmapFixture.Build();
        ScmapSurgery.SetU32(wires, entt + 16 + 24 + 0x14, 9);

        Should.Throw<ScmapFormatException>(() => ScmapProbe.Read(wires))
            .Message.ShouldContain($"of a {ConnectionCount}-connection table");
    }

    [Fact]
    public void A_string_index_past_the_table_is_refused_in_every_record()
    {
        const uint NoSuchString = 9999;
        byte[] valid = ScmapFixture.Build();
        (int entt, _) = ScmapSurgery.Section(valid, ScmapFormat.EntitySection);
        (int econ, _) = ScmapSurgery.Section(valid, ScmapFormat.EntityConnectionSection);

        byte[] className = (byte[])valid.Clone();
        ScmapSurgery.SetU32(className, entt + 16 + 0x04, NoSuchString);
        Should.Throw<ScmapFormatException>(() => ScmapProbe.Read(className))
            .Message.ShouldContain("as its class");

        byte[] value = (byte[])valid.Clone();
        ScmapSurgery.SetU32(value, entt + KeyvalueArray + 0x04, NoSuchString);
        Should.Throw<ScmapFormatException>(() => ScmapProbe.Read(value))
            .Message.ShouldContain("keyvalue 0");

        // Each of a wire's four strings.
        for (int field = 0; field < 4; field++)
        {
            byte[] wire = (byte[])valid.Clone();
            ScmapSurgery.SetU32(wire, econ + 16 + 24 + (field * 4), NoSuchString);
            Should.Throw<ScmapFormatException>(() => ScmapProbe.Read(wire))
                .Message.ShouldContain("connection 1");
        }
    }

    [Fact]
    public void An_entity_added_out_of_node_order_is_refused_by_the_cook()
    {
        var builder = new ScmapBuilder("Order");
        builder.AddNode(new ScmapNodeSource(Guid.NewGuid(), "A", -1, default, ScmapPayloadKind.None));
        builder.AddNode(new ScmapNodeSource(Guid.NewGuid(), "B", -1, default, ScmapPayloadKind.None));

        builder.AddEntity(new ScmapEntitySource(1, "logic_relay", [], []));

        Should.Throw<InvalidOperationException>(
                () => builder.AddEntity(new ScmapEntitySource(0, "logic_relay", [], [])))
            .Message.ShouldContain("node order");

        // One entity per node.
        Should.Throw<InvalidOperationException>(
            () => builder.AddEntity(new ScmapEntitySource(1, "logic_auto", [], [])));

        builder.EntityCount.ShouldBe(1);
    }

    [Fact]
    public void An_entity_on_a_node_that_was_never_added_is_refused_when_the_file_is_built()
    {
        var builder = new ScmapBuilder("Missing");
        builder.AddNode(new ScmapNodeSource(Guid.NewGuid(), "A", -1, default, ScmapPayloadKind.None));
        builder.AddEntity(new ScmapEntitySource(4, "logic_relay", [], []));

        Should.Throw<InvalidOperationException>(() => builder.Build(UInt128.Zero, EngineInfo.MapFormatVersion))
            .Message.ShouldContain("node 4");
    }

    private static ushort PayloadFlags(byte[] file, int nodeSection, int node) =>
        BinaryPrimitives.ReadUInt16LittleEndian(file.AsSpan(nodeSection + 16 + (node * 80) + 0x42));

    private static uint Code(string section) =>
        section[0] | ((uint)section[1] << 8) | ((uint)section[2] << 16) | ((uint)section[3] << 24);
}
