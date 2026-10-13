using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Spectra.Kitchen.Maps;
using SpectraEngine.Core;
using SpectraEngine.Core.Maps.Compiled;
using SpectraEngine.Core.Scene;
using SpectraEngine.Core.Scripting;

namespace Spectra.Kitchen.Tests;

/// <summary>
/// The tags and attributes a level was built with, in a baked map: two
/// sections a map has only when a node has any, read back onto the nodes as
/// they were, and refused when a record names what no node can hold.
/// </summary>
public class ScmapNodeSetsTableTests
{
    private const int Preamble = 16;

    private static readonly uint Unknown = 'Z' | ('Z' << 8) | ('Z' << 16) | ((uint)'Z' << 24);

    [Fact]
    public void Tags_and_attributes_arrive_on_their_nodes_as_they_were_built()
    {
        using CookedLevel level = CookedLevel.Bake(TaggedRoom.Build(MapFixture.Fresh()));

        int marked = 0;
        foreach (SceneNode authored in level.Authored.Root.Traverse().Skip(1))
        {
            SceneNode cooked = level.Scene.TryFindById(authored.Id, out SceneNode? found)
                ? found
                : throw new InvalidOperationException(authored.Name);

            cooked.Tags.ShouldBe(authored.Tags, authored.Name);
            cooked.Attributes.ShouldBe(authored.Attributes, authored.Name);
            if (authored.Tags.Count > 0 || authored.Attributes.Count > 0) marked++;
        }

        marked.ShouldBe(5);

        // A world brush has no brush once it is baked. It still has what it was built with.
        SceneNode floor = TaggedRoom.Named(level.Scene, "Floor");
        floor.Brush.ShouldBeNull();
        floor.Tags.ShouldBe(["Ground", "Lava"]);

        SceneNode crate = TaggedRoom.Named(level.Scene, "Crate");
        crate.Attributes.Select(pair => pair.Key).ShouldBe(["Damage", "Label", "Deadly", "Push", "Glow", "Rest"]);
        crate.Attributes.TryGet("Rest", out MemberValue rest).ShouldBeTrue();
        rest.Frame.Rotation.ShouldBe(TaggedRoom.Rest.Rotation, "bit for bit");
        crate.Attributes.TryGet("Label", out MemberValue label).ShouldBeTrue();
        label.Text.ShouldBe(TaggedRoom.Heiss);

        // A name with a period, a hyphen and a slash is a string of the table like any other.
        SceneNode door = TaggedRoom.Named(level.Scene, "Door");
        door.Attributes.Select(pair => pair.Key).ShouldBe(["Owner", TaggedRoom.Pathlike]);
        door.Attributes.TryGet(TaggedRoom.Pathlike, out MemberValue pathlike).ShouldBeTrue();
        pathlike.ShouldBe(MemberValue.Of(3d));
    }

    [Fact]
    public void A_map_with_no_tag_and_no_attribute_has_neither_section()
    {
        byte[] plain = ScmapFixture.Build();

        HasSection(plain, ScmapFormat.NodeTagSection).ShouldBeFalse();
        HasSection(plain, ScmapFormat.NodeAttributeSection).ShouldBeFalse();

        // A node that was handed two empty sets adds nothing either.
        ScmapBuilder builder = ScmapFixture.CreateBuilder();
        builder.AddNodeSets(new ScmapNodeSetsSource(2, TagSet.Empty, AttributeSet.Empty));
        builder.Build(ScmapFixture.Digest, EngineInfo.MapFormatVersion).ShouldBe(plain);
    }

    [Fact]
    public void A_map_with_tags_only_has_the_tag_section_and_not_the_other()
    {
        ScmapBuilder builder = ScmapFixture.CreateBuilder();
        builder.AddNodeSets(new ScmapNodeSetsSource(2, TagSet.Of("Lava"), AttributeSet.Empty));
        byte[] file = builder.Build(ScmapFixture.Digest, EngineInfo.MapFormatVersion);

        HasSection(file, ScmapFormat.NodeTagSection).ShouldBeTrue();
        HasSection(file, ScmapFormat.NodeAttributeSection).ShouldBeFalse();
    }

    [Fact]
    public void The_two_sections_are_the_documented_size_with_their_fields_at_the_documented_offsets()
    {
        byte[] file = Marked();
        List<string> strings = ScmapProbe.Read(file).Strings;

        (int ntag, int tagSize) = ScmapSurgery.Section(file, ScmapFormat.NodeTagSection);
        tagSize.ShouldBe(Preamble + (3 * 8));
        ScmapSurgery.U32(file, ntag).ShouldBe(3u);
        file.AsSpan(ntag + 4, 12).ToArray().ShouldBe(new byte[12]);

        // The last tag: "Lava" again, on node 5, as one string of the table.
        int last = ntag + Preamble + (2 * 8);
        ScmapSurgery.U32(file, last).ShouldBe(5u, "node");
        strings[(int)ScmapSurgery.U32(file, last + 4)].ShouldBe("Lava");
        ScmapSurgery.U32(file, last + 4).ShouldBe(ScmapSurgery.U32(file, ntag + Preamble + 4), "one string");

        (int natr, int attributeSize) = ScmapSurgery.Section(file, ScmapFormat.NodeAttributeSection);
        attributeSize.ShouldBe(Preamble + (6 * 56));
        ScmapFormat.NodeAttributeRecordSize.ShouldBe(56);
        ScmapSurgery.U32(file, natr).ShouldBe(6u);

        // The number: kind 3, the value as a double at 16.
        int number = natr + Preamble;
        ScmapSurgery.U32(file, number).ShouldBe(2u, "node");
        strings[(int)ScmapSurgery.U32(file, number + 4)].ShouldBe("Damage");
        file[number + 8].ShouldBe(ScmapAttributeRecord.NumberKind);
        BitConverter.ToDouble(file, number + 16).ShouldBe(0.1d);

        // The text: kind 1, its string's index at 12.
        int text = natr + Preamble + 56;
        file[text + 8].ShouldBe(ScmapAttributeRecord.TextKind);
        strings[(int)ScmapSurgery.U32(file, text + 12)].ShouldBe("hot");

        // The frame: kind 6, three floats at 24 and four at 36.
        int frame = natr + Preamble + (5 * 56);
        file[frame + 8].ShouldBe(ScmapAttributeRecord.CFrameKind);
        BitConverter.ToSingle(file, frame + 24).ShouldBe(1f);
        BitConverter.ToSingle(file, frame + 32).ShouldBe(3f);
        BitConverter.ToSingle(file, frame + 48).ShouldBe(1f, "the w of no rotation");
        file.AsSpan(frame + 52, 4).ToArray().ShouldBe(new byte[4]);
    }

    [Fact]
    public void A_reader_that_does_not_know_the_sections_loads_the_map_without_them()
    {
        byte[] file = Marked();
        ScmapSurgery.SetU32(file, ScmapSurgery.TableRecord(file, ScmapFormat.NodeTagSection), Unknown);
        ScmapSurgery.SetU32(file, ScmapSurgery.TableRecord(file, ScmapFormat.NodeAttributeSection), Unknown + 1);

        ScmapDocument document = ScmapReader.Read(file, "old.scmap");

        document.NodeTags.Length.ShouldBe(0);
        document.NodeAttributes.Length.ShouldBe(0);
        document.SkippedSectionCount.ShouldBe(2);
    }

    [Fact]
    public void The_builder_refuses_sets_out_of_node_order_and_a_node_the_map_does_not_have()
    {
        ScmapBuilder builder = ScmapFixture.CreateBuilder();
        builder.AddNodeSets(new ScmapNodeSetsSource(3, TagSet.Of("Lava"), AttributeSet.Empty));

        Should.Throw<InvalidOperationException>(
                () => builder.AddNodeSets(new ScmapNodeSetsSource(3, TagSet.Of("Hot"), AttributeSet.Empty)))
            .Message.ShouldContain("node order");

        ScmapBuilder lost = ScmapFixture.CreateBuilder();
        lost.AddNodeSets(new ScmapNodeSetsSource(99, TagSet.Of("Lava"), AttributeSet.Empty));
        Should.Throw<InvalidOperationException>(() => lost.Build(UInt128.Zero, EngineInfo.MapFormatVersion))
            .Message.ShouldContain("node 99");
    }

    [Theory]
    [InlineData(0x00, 99u, "tag 1 names node 99")]
    [InlineData(0x00, 0u, "not in node order")]
    [InlineData(0x04, 9999u, "names string 9999")]
    public void A_tag_record_that_names_what_is_not_there_is_refused(int field, uint value, string words)
    {
        byte[] file = Marked();
        (int ntag, _) = ScmapSurgery.Section(file, ScmapFormat.NodeTagSection);

        // The second record: the first node's second tag.
        ScmapSurgery.SetU32(file, ntag + Preamble + 8 + field, value);

        Should.Throw<ScmapFormatException>(() => Read(file)).Message.ShouldContain(words);
    }

    [Fact]
    public void A_node_with_one_tag_twice_is_refused_before_anything_is_loaded()
    {
        byte[] file = Marked();
        (int ntag, _) = ScmapSurgery.Section(file, ScmapFormat.NodeTagSection);

        // The second tag of node 2 becomes its first one again.
        ScmapSurgery.SetU32(file, ntag + Preamble + 8 + 4, ScmapSurgery.U32(file, ntag + Preamble + 4));

        Should.Throw<ScmapFormatException>(() => Read(file))
            .Message.ShouldContain("'Lava' is there twice");
    }

    [Theory]
    [InlineData(0x00, 99u, "names node 99")]
    [InlineData(0x04, 9999u, "names string 9999")]
    [InlineData(0x08, 7u, "declares kind 7")]
    [InlineData(0x08, 0u, "declares kind 0")]
    public void An_attribute_record_that_names_what_is_not_there_is_refused(int field, uint value, string words)
    {
        byte[] file = Marked();
        (int natr, _) = ScmapSurgery.Section(file, ScmapFormat.NodeAttributeSection);

        ScmapSurgery.SetU32(file, natr + Preamble + 56 + field, value);

        Should.Throw<ScmapFormatException>(() => Read(file)).Message.ShouldContain(words);
    }

    [Fact]
    public void A_number_that_is_not_finite_and_a_name_no_attribute_has_are_refused()
    {
        byte[] notFinite = Marked();
        (int natr, _) = ScmapSurgery.Section(notFinite, ScmapFormat.NodeAttributeSection);
        BitConverter.GetBytes(double.NaN).CopyTo(notFinite, natr + Preamble + 16);

        Should.Throw<ScmapFormatException>(() => Read(notFinite))
            .Message.ShouldContain("has to be finite");

        // The second attribute of the node gets the name of its first.
        byte[] twice = Marked();
        ScmapSurgery.SetU32(twice, natr + Preamble + 56 + 4, ScmapSurgery.U32(twice, natr + Preamble + 4));

        Should.Throw<ScmapFormatException>(() => Read(twice))
            .Message.ShouldContain("'Damage' is there twice");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_section_too_short_for_its_preamble_or_its_count_is_refused(bool isTags)
    {
        uint kind = isTags ? ScmapFormat.NodeTagSection : ScmapFormat.NodeAttributeSection;
        string name = isTags ? "NTAG" : "NATR";

        byte[] empty = Marked();
        ScmapSurgery.Resize(empty, kind, 0);
        Should.Throw<ScmapFormatException>(() => Read(empty))
            .Message.ShouldContain($"0-byte {name} section");

        byte[] reaching = Marked();
        ScmapSurgery.SetU32(reaching, ScmapSurgery.Section(reaching, kind).Offset, 9999u);
        Should.Throw<ScmapFormatException>(() => Read(reaching))
            .Message.ShouldContain($"-byte {name} section");
    }

    // Tags on nodes 2 and 5, and an attribute of every kind on node 2.
    private static byte[] Marked()
    {
        ScmapBuilder builder = ScmapFixture.CreateBuilder();

        builder.AddNodeSets(new ScmapNodeSetsSource(
            2,
            TagSet.Of("Lava", "Hot stuff"),
            AttributeSet.Empty
                .With("Damage", MemberValue.Of(0.1d))
                .With("Label", MemberValue.Of("hot"))
                .With("Deadly", MemberValue.Of(true))
                .With("Push", MemberValue.Of(new Vector3(0f, 12.5f, -1f)))
                .With("Glow", MemberValue.Of(new Color3(1f, 0.5f, 0f)))
                .With("Rest", MemberValue.Of(new CFrame(1f, 2f, 3f)))));

        builder.AddNodeSets(new ScmapNodeSetsSource(5, TagSet.Of("Lava"), AttributeSet.Empty));
        return builder.Build(ScmapFixture.Digest, EngineInfo.MapFormatVersion);
    }

    private static void Read(byte[] file) => ScmapReader.Read(file, "bad.scmap");

    private static bool HasSection(byte[] file, uint kind)
    {
        try
        {
            ScmapSurgery.TableRecord(file, kind);
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }
}
