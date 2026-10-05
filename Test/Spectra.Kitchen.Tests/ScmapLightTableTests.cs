using System;
using System.Buffers.Binary;
using System.Numerics;
using Spectra.Kitchen.Maps;
using SpectraEngine.Core;
using SpectraEngine.Core.Maps.Compiled;
using SpectraEngine.Core.Scene;

namespace Spectra.Kitchen.Tests;

/// <summary>
/// The light table of a compiled map, written and read back. Offsets are
/// literals from the format, and reader refusals are tested by editing the
/// bytes of a valid file.
/// </summary>
public class ScmapLightTableTests
{
    // The second record of the fixture: the spot on node 5.
    private const int Spot = 16 + 48;

    [Fact]
    public void Lights_read_back_with_every_field()
    {
        ScmapProbe probe = ScmapProbe.Read(ScmapFixture.Build());

        probe.Lights.Count.ShouldBe(ScmapFixture.Lights.Length);
        for (int i = 0; i < probe.Lights.Count; i++)
        {
            Light expected = ScmapFixture.Lights[i].Light;
            ScmapLightRecord record = probe.Lights[i];
            Light actual = record.ToLight();

            record.NodeIndex.ShouldBe((uint)ScmapFixture.Lights[i].NodeIndex, $"light {i}");
            actual.Kind.ShouldBe(expected.Kind, $"light {i}");
            actual.Color.ShouldBe(expected.Color, $"light {i}");
            actual.Intensity.ShouldBe(expected.Intensity, $"light {i}");
            actual.Range.ShouldBe(expected.Range, $"light {i}");
            actual.Enabled.ShouldBe(expected.Enabled, $"light {i}");
            actual.InnerAngle.ShouldBe(expected.InnerAngle, $"light {i}");
            actual.OuterAngle.ShouldBe(expected.OuterAngle, $"light {i}");
            actual.Width.ShouldBe(expected.Width, $"light {i}");
            actual.Height.ShouldBe(expected.Height, $"light {i}");
            actual.Radius.ShouldBe(expected.Radius, $"light {i}");
        }
    }

    [Fact]
    public void The_light_section_is_the_documented_size_with_its_fields_at_the_documented_offsets()
    {
        byte[] file = ScmapFixture.Build();
        (int lght, int size) = ScmapSurgery.Section(file, ScmapFormat.LightSection);

        // 16 + 2 * 48.
        size.ShouldBe(112);

        ScmapSurgery.U32(file, lght).ShouldBe(2u);
        file.AsSpan(lght + 4, 12).ToArray().ShouldBe(new byte[12]);

        int spot = lght + Spot;
        ScmapSurgery.U32(file, spot + 0x00).ShouldBe(5u, "node");
        U16(file, spot + 0x04).ShouldBe((ushort)2, "kind: spot");
        U16(file, spot + 0x06).ShouldBe((ushort)1, "flags: disabled");
        F32(file, spot + 0x08).ShouldBe(1f);
        F32(file, spot + 0x0C).ShouldBe(0.5f);
        F32(file, spot + 0x10).ShouldBe(0.25f);
        F32(file, spot + 0x14).ShouldBe(3.5f, "intensity");
        F32(file, spot + 0x18).ShouldBe(12.25f, "range");
        F32(file, spot + 0x1C).ShouldBe(15f, "inner angle");
        F32(file, spot + 0x20).ShouldBe(40f, "outer angle");
        F32(file, spot + 0x24).ShouldBe(2f, "width");
        F32(file, spot + 0x28).ShouldBe(0.75f, "height");
        F32(file, spot + 0x2C).ShouldBe(0.3f, "radius");
    }

    [Fact]
    public void A_light_left_at_its_defaults_writes_no_flag_and_kind_zero()
    {
        byte[] file = ScmapFixture.Build();
        (int lght, _) = ScmapSurgery.Section(file, ScmapFormat.LightSection);

        int first = lght + 16;
        ScmapSurgery.U32(file, first + 0x00).ShouldBe(0u, "node");
        U16(file, first + 0x04).ShouldBe((ushort)0, "kind: directional");
        U16(file, first + 0x06).ShouldBe((ushort)0, "flags");
        F32(file, first + 0x14).ShouldBe(1f, "intensity");
        F32(file, first + 0x18).ShouldBe(10f, "range");
    }

    [Fact]
    public void A_map_with_no_lights_carries_the_preamble_and_nothing_else()
    {
        var builder = new ScmapBuilder("Dark");
        builder.AddNode(new ScmapNodeSource(Guid.NewGuid(), "Root", -1, default, ScmapPayloadKind.None));
        byte[] file = builder.Build(UInt128.Zero, EngineInfo.MapFormatVersion);

        ScmapSurgery.Section(file, ScmapFormat.LightSection).Size.ShouldBe(16);
        ScmapProbe.Read(file).Lights.ShouldBeEmpty();
    }

    [Fact]
    public void A_file_without_a_light_section_is_refused()
    {
        byte[] file = ScmapFixture.Build();

        // A code nothing knows: the reader steps over the record.
        ScmapSurgery.SetU32(
            file,
            ScmapSurgery.TableRecord(file, ScmapFormat.LightSection),
            'Z' | ('Z' << 8) | ('Z' << 16) | ((uint)'Z' << 24));

        Should.Throw<ScmapFormatException>(() => ScmapProbe.Read(file)).Message.ShouldContain("LGHT");
    }

    [Fact]
    public void A_light_section_with_no_preamble_is_refused()
    {
        byte[] file = ScmapFixture.Build();
        ScmapSurgery.Resize(file, ScmapFormat.LightSection, 0);

        Should.Throw<ScmapFormatException>(() => ScmapProbe.Read(file))
            .Message.ShouldContain("0-byte LGHT section");
    }

    [Fact]
    public void A_count_reaching_past_the_section_is_refused()
    {
        byte[] file = ScmapFixture.Build();
        (int lght, _) = ScmapSurgery.Section(file, ScmapFormat.LightSection);
        ScmapSurgery.SetU32(file, lght, 1000);

        Should.Throw<ScmapFormatException>(() => ScmapProbe.Read(file))
            .Message.ShouldContain("1000 lights");
    }

    [Fact]
    public void A_light_naming_a_node_the_map_does_not_have_is_refused()
    {
        byte[] file = ScmapFixture.Build();
        (int lght, _) = ScmapSurgery.Section(file, ScmapFormat.LightSection);
        ScmapSurgery.SetU32(file, lght + Spot, 99);

        Should.Throw<ScmapFormatException>(() => ScmapProbe.Read(file))
            .Message.ShouldContain("node 99");
    }

    [Fact]
    public void Two_lights_on_one_node_are_refused()
    {
        // The loader walks lights in step with the nodes.
        byte[] file = ScmapFixture.Build();
        (int lght, _) = ScmapSurgery.Section(file, ScmapFormat.LightSection);
        ScmapSurgery.SetU32(file, lght + Spot, 0);

        Should.Throw<ScmapFormatException>(() => ScmapProbe.Read(file))
            .Message.ShouldContain("ascending node order");
    }

    [Fact]
    public void A_light_kind_this_engine_does_not_know_is_refused()
    {
        byte[] file = ScmapFixture.Build();
        (int lght, _) = ScmapSurgery.Section(file, ScmapFormat.LightSection);
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(lght + Spot + 0x04), 9);

        ScmapFormatException refused = Should.Throw<ScmapFormatException>(() => ScmapProbe.Read(file));
        refused.Message.ShouldContain("light 1 declares kind 9");
        refused.Message.ShouldContain("Recook");
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(-4f)]
    [InlineData(float.NaN)]
    public void A_light_whose_range_is_not_positive_is_refused(float range)
    {
        // Light throws on such a range, which would end the load half way
        // through the graph.
        byte[] file = ScmapFixture.Build();
        (int lght, _) = ScmapSurgery.Section(file, ScmapFormat.LightSection);
        BinaryPrimitives.WriteSingleLittleEndian(file.AsSpan(lght + Spot + 0x18), range);

        Should.Throw<ScmapFormatException>(() => ScmapProbe.Read(file))
            .Message.ShouldContain("range must be positive");
    }

    [Theory]
    [InlineData(-0.5f)]
    [InlineData(float.NaN)]
    public void A_light_with_a_negative_intensity_is_refused(float intensity)
    {
        byte[] file = ScmapFixture.Build();
        (int lght, _) = ScmapSurgery.Section(file, ScmapFormat.LightSection);
        BinaryPrimitives.WriteSingleLittleEndian(file.AsSpan(lght + Spot + 0x14), intensity);

        Should.Throw<ScmapFormatException>(() => ScmapProbe.Read(file))
            .Message.ShouldContain("intensity cannot be negative");
    }

    [Fact]
    public void A_light_added_out_of_node_order_is_refused_by_the_cook()
    {
        var builder = new ScmapBuilder("Order");
        builder.AddNode(new ScmapNodeSource(Guid.NewGuid(), "A", -1, default, ScmapPayloadKind.None));
        builder.AddNode(new ScmapNodeSource(Guid.NewGuid(), "B", -1, default, ScmapPayloadKind.None));

        builder.AddLight(new ScmapLightSource(1, new Light()));

        Should.Throw<InvalidOperationException>(() => builder.AddLight(new ScmapLightSource(0, new Light())))
            .Message.ShouldContain("node order");

        // One light per node.
        Should.Throw<InvalidOperationException>(() => builder.AddLight(new ScmapLightSource(1, new Light())));

        builder.LightCount.ShouldBe(1);
    }

    [Fact]
    public void A_light_on_a_node_that_was_never_added_is_refused_when_the_file_is_built()
    {
        var builder = new ScmapBuilder("Missing");
        builder.AddNode(new ScmapNodeSource(Guid.NewGuid(), "A", -1, default, ScmapPayloadKind.None));
        builder.AddLight(new ScmapLightSource(4, new Light()));

        Should.Throw<InvalidOperationException>(() => builder.Build(UInt128.Zero, EngineInfo.MapFormatVersion))
            .Message.ShouldContain("node 4");
    }

    [Fact]
    public void A_light_is_written_as_it_was_when_it_was_added()
    {
        // Light is mutable and the file is built later.
        var builder = new ScmapBuilder("Copy");
        builder.AddNode(new ScmapNodeSource(Guid.NewGuid(), "Lamp", -1, default, ScmapPayloadKind.None));

        var light = new Light { Kind = LightKind.Point, Color = new Vector3(0.25f, 0.5f, 1f), Range = 7f };
        builder.AddLight(new ScmapLightSource(0, light));
        light.Range = 99f;

        ScmapProbe.Read(builder.Build(UInt128.Zero, EngineInfo.MapFormatVersion))
            .Lights[0].Range.ShouldBe(7f);
    }

    private static ushort U16(byte[] file, int offset) =>
        BinaryPrimitives.ReadUInt16LittleEndian(file.AsSpan(offset));

    private static float F32(byte[] file, int offset) =>
        BinaryPrimitives.ReadSingleLittleEndian(file.AsSpan(offset));
}
