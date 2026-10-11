using SpectraEngine.Core.Bsp;
using SpectraEngine.Core.Inspection;
using SpectraEngine.Core.Scene;
using SpectraEngine.Editing.Commands;
using SpectraEngine.Editor.Shell;
using System.Numerics;

namespace SpectraEngine.Editor.Tests;

/// <summary>
/// The shell's half of a script node: the word the Properties header calls it
/// by, and the three rows it edits.
/// </summary>
public sealed class ScriptShellTests
{
    private sealed class Rig
    {
        public List<PropertyEdit> Edits { get; } = [];
        public PropertyPanelModel Panel { get; }

        public Rig() => Panel = new PropertyPanelModel(Edits.Add, _ => { }, _ => { });

        // The rows the engine publishes for the selection, from the inspector itself.
        public void Publish(params SceneNode[] nodes)
        {
            var rows = new List<PropertyRow>();
            NodeInspector.Describe(nodes, rows);
            Panel.Apply(rows, nodes.Length);
        }

        public PropertyRowModel Row(PropertyId id) =>
            Panel.Groups.SelectMany(group => group.Rows).Single(row => row.Id == id);
    }

    private static SceneNode Opener() => new("Opener") { Script = new Script("Scripts/door.luau") };

    [Fact]
    public void The_header_calls_a_script_node_a_script()
    {
        var rig = new Rig();

        rig.Publish(Opener());

        rig.Panel.HeaderKind.ShouldBe("Script");
    }

    [Fact]
    public void A_part_that_carries_a_script_is_still_called_a_part()
    {
        // The same order as the tree row's icon.
        var rig = new Rig();
        var part = new SceneNode("Door")
        {
            BrushKind = BrushKind.Part,
            Brush = Brush.CreateBox(-Vector3.One, Vector3.One),
            Script = new Script("Scripts/door.luau"),
        };

        rig.Publish(part);

        rig.Panel.HeaderKind.ShouldBe("Part");
    }

    [Fact]
    public void A_node_with_nothing_on_it_has_no_kind_word()
    {
        var rig = new Rig();

        rig.Publish(new SceneNode("Marker"));

        rig.Panel.HeaderKind.ShouldBeEmpty();
    }

    [Fact]
    public void A_script_with_no_children_shows_its_script_section_and_no_transform()
    {
        var rig = new Rig();

        rig.Publish(Opener());

        rig.Panel.Groups.Select(group => group.Name).ShouldBe([NodeInspector.ScriptGroup]);
    }

    [Fact]
    public void The_script_section_holds_a_file_to_pick_and_two_switches()
    {
        var rig = new Rig();

        rig.Publish(Opener());

        PropertyGroupModel section = rig.Panel.Groups.Single(group => group.Name == NodeInspector.ScriptGroup);
        section.Rows.Select(row => row.Name).ShouldBe(["File", "Enabled", "Module"]);
        section.Rows.Select(row => row.Kind).ShouldBe(
            [PropertyKind.Asset, PropertyKind.Boolean, PropertyKind.Boolean]);
        rig.Row(PropertyId.ScriptEnabled).Flag.ShouldBeTrue();
        rig.Row(PropertyId.ScriptModule).Flag.ShouldBeFalse();
        rig.Row(PropertyId.ScriptModule).HasHelp.ShouldBeTrue();
    }

    [Fact]
    public void The_file_row_shows_the_files_name_and_keeps_its_whole_path()
    {
        var rig = new Rig();
        rig.Publish(Opener());

        PropertyRowModel file = rig.Row(PropertyId.ScriptFile);

        file.IsAsset.ShouldBeTrue();
        file.AssetKind.ShouldBe(AssetKind.Script);
        file.AssetLabel.ShouldBe("door");
        file.AssetPath.ShouldBe("Scripts/door.luau");
        file.HasNote.ShouldBeFalse();
    }

    [Fact]
    public void The_file_row_has_an_open_button_and_no_play_button()
    {
        var rig = new Rig();
        rig.Publish(Opener());

        PropertyRowModel file = rig.Row(PropertyId.ScriptFile);

        file.CanOpen.ShouldBeTrue();
        file.CanPreview.ShouldBeFalse();
    }

    [Fact]
    public void Two_scripts_that_name_different_files_have_nothing_to_open()
    {
        var rig = new Rig();
        var other = new SceneNode("Closer") { Script = new Script("Scripts/gate.luau") };

        rig.Publish(Opener(), other);

        PropertyRowModel file = rig.Row(PropertyId.ScriptFile);
        file.AssetLabel.ShouldBe("(mixed)");
        file.CanOpen.ShouldBeFalse();
    }

    [Fact]
    public void A_picked_file_is_sent_as_text_for_the_file_row()
    {
        var rig = new Rig();
        rig.Publish(Opener());

        rig.Row(PropertyId.ScriptFile).PickAsset("Scripts/gate.luau");

        PropertyEdit edit = rig.Edits.Single();
        edit.Id.ShouldBe(PropertyId.ScriptFile);
        edit.Text.ShouldBe("Scripts/gate.luau");
    }

    [Theory]
    [InlineData(@"C:\Game\Assets\Scripts\gate.luau")]
    [InlineData("../Scripts/gate.luau")]
    [InlineData("./")]
    [InlineData("")]
    public void A_file_outside_the_assets_folder_is_refused_and_the_row_says_why(string picked)
    {
        var rig = new Rig();
        rig.Publish(Opener());
        PropertyRowModel row = rig.Row(PropertyId.ScriptFile);

        row.PickAsset(picked);

        rig.Edits.ShouldBeEmpty();
        row.AssetPath.ShouldBe("Scripts/door.luau");
        row.HasRejection.ShouldBeTrue();
        row.Rejection.ShouldBe("Not applied: File must be a path inside this project's Assets folder.");
    }

    [Fact]
    public void A_refusal_goes_away_when_a_good_file_is_picked()
    {
        var rig = new Rig();
        rig.Publish(Opener());
        PropertyRowModel row = rig.Row(PropertyId.ScriptFile);
        row.PickAsset("../elsewhere.luau");
        var raised = new List<string?>();
        row.PropertyChanged += (_, change) => raised.Add(change.PropertyName);

        row.PickAsset("Scripts/gate.luau");

        row.HasRejection.ShouldBeFalse();
        raised.ShouldContain(nameof(PropertyRowModel.HasRejection));
        rig.Edits.Single().Text.ShouldBe("Scripts/gate.luau");
    }

    [Fact]
    public void A_material_row_may_still_be_set_to_none()
    {
        // The rule above is the script's alone: an empty material is the default one.
        var rig = new Rig();
        var block = new SceneNode("Wall") { Brush = Brush.CreateBox(-Vector3.One, Vector3.One) };
        rig.Publish(block);

        rig.Row(PropertyId.BrushMaterial).PickAsset(string.Empty);

        rig.Row(PropertyId.BrushMaterial).HasRejection.ShouldBeFalse();
    }

    [Theory]
    [InlineData(PropertyId.ScriptEnabled, false)]
    [InlineData(PropertyId.ScriptModule, true)]
    public void A_switch_is_sent_on_the_click(PropertyId id, bool clicked)
    {
        var rig = new Rig();
        rig.Publish(Opener());

        rig.Row(id).Flag = clicked;

        PropertyEdit edit = rig.Edits.Single();
        edit.Id.ShouldBe(id);
        edit.Flag.ShouldBe(clicked);
    }

    [Fact]
    public void An_edit_of_a_script_refreshes_the_rows_without_rebuilding_them()
    {
        // The node is a script before and after, so the rows stay the same rows.
        var rig = new Rig();
        SceneNode node = Opener();
        rig.Publish(node);
        PropertyRowModel file = rig.Row(PropertyId.ScriptFile);

        node.Script = new Script("Scripts/gate.luau") { IsEnabled = false };
        rig.Publish(node);

        rig.Row(PropertyId.ScriptFile).ShouldBeSameAs(file);
        file.AssetPath.ShouldBe("Scripts/gate.luau");
        file.AssetLabel.ShouldBe("gate");
        rig.Row(PropertyId.ScriptEnabled).Flag.ShouldBeFalse();
        rig.Edits.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("Scripts/gate.luau", "Scripts/gate.luau")]
    [InlineData(@"\Scripts\Doors\vault.luau ", "Scripts/Doors/vault.luau")]
    [InlineData("  Scripts//gate.LUAU", "Scripts/gate.LUAU")]
    [InlineData("gate.luau", "gate.luau")]
    public void A_path_typed_into_the_picker_is_read_as_the_path_a_level_stores(string typed, string expected)
    {
        TypedScriptPath.TryRead(typed, out string? path).ShouldBeTrue();

        path.ShouldBe(expected);
    }

    [Theory]
    [InlineData("")]
    [InlineData("gate")]
    [InlineData("Scripts/gate.lua")]
    [InlineData(".luau")]
    [InlineData("Scripts/.luau")]
    [InlineData("../gate.luau")]
    [InlineData(@"C:\Game\gate.luau")]
    public void What_is_typed_is_no_path_unless_it_names_a_luau_file_inside_the_assets_folder(string typed)
    {
        TypedScriptPath.TryRead(typed, out string? path).ShouldBeFalse();

        path.ShouldBeNull();
    }
}
