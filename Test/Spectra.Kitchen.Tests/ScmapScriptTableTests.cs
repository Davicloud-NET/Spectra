using System;
using System.Linq;
using Spectra.Kitchen.Maps;
using SpectraEngine.Core;
using SpectraEngine.Core.Assets.Packs;
using SpectraEngine.Core.Maps.Compiled;

namespace Spectra.Kitchen.Tests;

/// <summary>
/// The script table of a compiled map, written and read back. Offsets are
/// literals from the format, and reader refusals are tested by editing the
/// bytes of a valid file.
/// </summary>
public class ScmapScriptTableTests
{
    private const int Preamble = 16;
    private const int Record = 16;

    // Rows of the fixture's asset table: the first script file and a material.
    private const uint WorldScriptRow = 4;
    private const uint MaterialRow = 0;

    [Fact]
    public void Scripts_read_back_on_their_nodes_switched_on_or_off_and_as_script_or_module()
    {
        ScmapProbe probe = ScmapProbe.Read(ScmapFixture.Build());

        probe.Scripts.Count.ShouldBe(ScmapFixture.Scripts.Length);
        for (int i = 0; i < probe.Scripts.Count; i++)
        {
            ScmapScriptSource expected = ScmapFixture.Scripts[i];
            ScmapProbe.ScriptCopy actual = probe.Scripts[i];

            actual.NodeIndex.ShouldBe((uint)expected.NodeIndex, $"script {i}");
            actual.Path.ShouldBe(expected.Path, $"script {i}");
            actual.IsEnabled.ShouldBe(expected.IsEnabled, $"script {i}");
            actual.IsModule.ShouldBe(expected.IsModule, $"script {i}");
        }

        // All four pairings, or a flag could be dropped and nothing here would see it.
        probe.Scripts.Select(script => (script.IsEnabled, script.IsModule)).Distinct().Count().ShouldBe(4);
    }

    [Fact]
    public void A_scripts_file_is_a_row_of_the_asset_table_and_two_scripts_share_one()
    {
        ScmapProbe probe = ScmapProbe.Read(ScmapFixture.Build());

        ScmapProbe.AssetRow[] files = [.. probe.Assets.Where(row => row.Kind == PackEntryKind.Script)];

        files.Select(row => row.Path).ShouldBe(["Scripts/world.luau", "Scripts/Lib/rules.luau", "Scripts/Lib/off.luau"]);
        probe.Assets.Select(row => row.Path).ShouldBe(ScmapFixture.AssetPaths);
    }

    [Fact]
    public void The_script_section_is_the_documented_size_with_its_fields_at_the_documented_offsets()
    {
        byte[] file = ScmapFixture.Build();
        (int scpt, int size) = ScmapSurgery.Section(file, ScmapFormat.ScriptSection);

        // 16 + 4 * 16.
        size.ShouldBe(80);
        ScmapFormat.ScriptPreambleSize.ShouldBe(Preamble);
        ScmapFormat.ScriptRecordSize.ShouldBe(Record);

        ScmapSurgery.U32(file, scpt).ShouldBe(4u);
        file.AsSpan(scpt + 4, 12).ToArray().ShouldBe(new byte[12]);

        // The last one: a module, switched off, on node 6.
        int off = scpt + Preamble + (3 * Record);
        ScmapSurgery.U32(file, off + 0x00).ShouldBe(6u, "node");
        ScmapSurgery.U32(file, off + 0x04).ShouldBe(6u, "asset row");
        file[off + 0x08].ShouldBe((byte)1, "kind: module");
        file[off + 0x09].ShouldBe((byte)1, "flags: switched off");
        file.AsSpan(off + 0x0A, 6).ToArray().ShouldBe(new byte[6]);

        // The first one: a script that runs, which writes no bit at all.
        int world = scpt + Preamble;
        ScmapSurgery.U32(file, world + 0x00).ShouldBe(0u, "node");
        ScmapSurgery.U32(file, world + 0x04).ShouldBe(WorldScriptRow, "asset row");
        file.AsSpan(world + 0x08, 8).ToArray().ShouldBe(new byte[8]);
    }

    [Fact]
    public void A_map_with_no_scripts_carries_the_preamble_and_nothing_else()
    {
        var builder = new ScmapBuilder("Quiet");
        builder.AddNode(new ScmapNodeSource(Guid.NewGuid(), "Root", -1, default, ScmapPayloadKind.None));
        byte[] file = builder.Build(UInt128.Zero, EngineInfo.MapFormatVersion);

        ScmapSurgery.Section(file, ScmapFormat.ScriptSection).Size.ShouldBe(Preamble);
        ScmapProbe.Read(file).Scripts.ShouldBeEmpty();
    }

    [Fact]
    public void A_file_without_a_script_section_is_refused()
    {
        byte[] file = ScmapFixture.Build();

        // A code nothing knows: the reader steps over the record.
        ScmapSurgery.SetU32(
            file,
            ScmapSurgery.TableRecord(file, ScmapFormat.ScriptSection),
            'Z' | ('Z' << 8) | ('Z' << 16) | ((uint)'Z' << 24));

        Should.Throw<ScmapFormatException>(() => ScmapProbe.Read(file)).Message.ShouldContain("SCPT");
    }

    [Fact]
    public void A_map_of_the_version_before_scripts_is_refused_and_says_to_recook()
    {
        byte[] file = ScmapFixture.Build();
        BitConverter.GetBytes((ushort)3).CopyTo(file, 0x04);

        ScmapFormatException refused = Should.Throw<ScmapFormatException>(() => ScmapProbe.Read(file));

        refused.Message.ShouldContain("version 3");
        refused.Message.ShouldContain("Recook");
        EngineInfo.CompiledMapFormatVersion.ShouldBe((ushort)4);
    }

    [Fact]
    public void A_script_section_with_no_preamble_is_refused()
    {
        // What every map before this version wrote.
        byte[] file = ScmapFixture.Build();
        ScmapSurgery.Resize(file, ScmapFormat.ScriptSection, 0);

        Should.Throw<ScmapFormatException>(() => ScmapProbe.Read(file))
            .Message.ShouldContain("0-byte SCPT section");
    }

    [Fact]
    public void A_count_reaching_past_the_section_is_refused()
    {
        byte[] file = ScmapFixture.Build();
        (int scpt, _) = ScmapSurgery.Section(file, ScmapFormat.ScriptSection);
        ScmapSurgery.SetU32(file, scpt, 1000);

        Should.Throw<ScmapFormatException>(() => ScmapProbe.Read(file))
            .Message.ShouldContain("1000 scripts");
    }

    [Fact]
    public void A_script_naming_a_node_the_map_does_not_have_is_refused()
    {
        byte[] file = ScmapFixture.Build();
        (int scpt, _) = ScmapSurgery.Section(file, ScmapFormat.ScriptSection);
        ScmapSurgery.SetU32(file, scpt + Preamble + (3 * Record), 99);

        Should.Throw<ScmapFormatException>(() => ScmapProbe.Read(file))
            .Message.ShouldContain("node 99");
    }

    [Fact]
    public void Two_scripts_on_one_node_are_refused()
    {
        // The loader walks scripts in step with the nodes.
        byte[] file = ScmapFixture.Build();
        (int scpt, _) = ScmapSurgery.Section(file, ScmapFormat.ScriptSection);
        ScmapSurgery.SetU32(file, scpt + Preamble + Record, 0);

        Should.Throw<ScmapFormatException>(() => ScmapProbe.Read(file))
            .Message.ShouldContain("ascending node order");
    }

    [Fact]
    public void A_script_kind_this_engine_does_not_know_is_refused()
    {
        byte[] file = ScmapFixture.Build();
        (int scpt, _) = ScmapSurgery.Section(file, ScmapFormat.ScriptSection);
        file[scpt + Preamble + Record + 0x08] = 2;

        ScmapFormatException refused = Should.Throw<ScmapFormatException>(() => ScmapProbe.Read(file));
        refused.Message.ShouldContain("script 1 declares kind 2");
        refused.Message.ShouldContain("Recook");
    }

    [Fact]
    public void A_script_naming_an_asset_row_the_map_does_not_have_is_refused()
    {
        byte[] file = ScmapFixture.Build();
        (int scpt, _) = ScmapSurgery.Section(file, ScmapFormat.ScriptSection);
        ScmapSurgery.SetU32(file, scpt + Preamble + 0x04, 70);

        Should.Throw<ScmapFormatException>(() => ScmapProbe.Read(file))
            .Message.ShouldContain("asset 70");
    }

    [Fact]
    public void A_script_whose_file_is_a_material_is_refused()
    {
        byte[] file = ScmapFixture.Build();
        (int scpt, _) = ScmapSurgery.Section(file, ScmapFormat.ScriptSection);
        ScmapSurgery.SetU32(file, scpt + Preamble + 0x04, MaterialRow);

        Should.Throw<ScmapFormatException>(() => ScmapProbe.Read(file))
            .Message.ShouldContain("not a script");
    }

    [Fact]
    public void A_script_added_out_of_node_order_is_refused_by_the_cook()
    {
        var builder = new ScmapBuilder("Order");
        builder.AddNode(new ScmapNodeSource(Guid.NewGuid(), "A", -1, default, ScmapPayloadKind.None));
        builder.AddNode(new ScmapNodeSource(Guid.NewGuid(), "B", -1, default, ScmapPayloadKind.None));

        builder.AddScript(new ScmapScriptSource(1, "Scripts/b.luau"));

        Should.Throw<InvalidOperationException>(() => builder.AddScript(new ScmapScriptSource(0, "Scripts/a.luau")))
            .Message.ShouldContain("node order");

        // One script per node.
        Should.Throw<InvalidOperationException>(() => builder.AddScript(new ScmapScriptSource(1, "Scripts/c.luau")));

        builder.ScriptCount.ShouldBe(1);
    }

    [Fact]
    public void A_script_on_a_node_that_was_never_added_is_refused_when_the_file_is_built()
    {
        var builder = new ScmapBuilder("Missing");
        builder.AddNode(new ScmapNodeSource(Guid.NewGuid(), "A", -1, default, ScmapPayloadKind.None));
        builder.AddScript(new ScmapScriptSource(4, "Scripts/a.luau"));

        Should.Throw<InvalidOperationException>(() => builder.Build(UInt128.Zero, EngineInfo.MapFormatVersion))
            .Message.ShouldContain("node 4");
    }

    [Theory]
    [InlineData("")]
    [InlineData("../outside.luau")]
    [InlineData("C:/Game/door.luau")]
    public void A_path_that_is_no_content_path_is_refused_by_the_cook(string path)
    {
        var builder = new ScmapBuilder("Paths");
        builder.AddNode(new ScmapNodeSource(Guid.NewGuid(), "A", -1, default, ScmapPayloadKind.None));

        Should.Throw<ArgumentException>(() => builder.AddScript(new ScmapScriptSource(0, path)));
    }

    [Fact]
    public void A_file_whose_scripts_path_leaves_the_content_root_is_refused()
    {
        // As long as the path it replaces, so no offset moves.
        byte[] file = ScmapFixture.Build();
        byte[] honest = "Scripts/world.luau"u8.ToArray();
        byte[] leaving = "../../../a/bb.luau"u8.ToArray();
        leaving.Length.ShouldBe(honest.Length);

        int at = file.AsSpan().IndexOf(honest);
        at.ShouldBeGreaterThan(0);
        leaving.CopyTo(file.AsSpan(at));

        Should.Throw<ScmapFormatException>(() => ScmapProbe.Read(file))
            .Message.ShouldContain("no path inside the content root");
    }
}
