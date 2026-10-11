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

        // The rows the engine publishes for the node, from the inspector itself.
        public void Publish(SceneNode node)
        {
            var rows = new List<PropertyRow>();
            NodeInspector.Describe(node, rows);
            Panel.Apply(rows, 1);
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
    public void The_script_section_holds_a_file_box_and_two_switches()
    {
        var rig = new Rig();

        rig.Publish(Opener());

        PropertyGroupModel section = rig.Panel.Groups.Single(group => group.Name == NodeInspector.ScriptGroup);
        section.Rows.Select(row => row.Name).ShouldBe(["File", "Enabled", "Module"]);
        section.Rows.Select(row => row.Kind).ShouldBe(
            [PropertyKind.Text, PropertyKind.Boolean, PropertyKind.Boolean]);
        rig.Row(PropertyId.ScriptFile).Fields[0].Text.ShouldBe("Scripts/door.luau");
        rig.Row(PropertyId.ScriptEnabled).Flag.ShouldBeTrue();
        rig.Row(PropertyId.ScriptModule).Flag.ShouldBeFalse();
        rig.Row(PropertyId.ScriptModule).HasHelp.ShouldBeTrue();
    }

    [Fact]
    public void A_typed_file_is_sent_as_text_for_the_file_row()
    {
        var rig = new Rig();
        rig.Publish(Opener());

        PropertyFieldModel file = rig.Row(PropertyId.ScriptFile).Fields[0];
        file.BeginEdit();
        file.Text = "Scripts/gate.luau";
        file.Commit();

        PropertyEdit edit = rig.Edits.Single();
        edit.Id.ShouldBe(PropertyId.ScriptFile);
        edit.Text.ShouldBe("Scripts/gate.luau");
    }

    [Theory]
    [InlineData(@"C:\Game\Assets\Scripts\gate.luau")]
    [InlineData("../Scripts/gate.luau")]
    [InlineData("./")]
    public void A_file_outside_the_assets_folder_is_refused_and_the_row_says_why(string typed)
    {
        var rig = new Rig();
        rig.Publish(Opener());
        PropertyRowModel row = rig.Row(PropertyId.ScriptFile);

        PropertyFieldModel file = row.Fields[0];
        file.BeginEdit();
        file.Text = typed;
        file.Commit();

        rig.Edits.ShouldBeEmpty();
        file.Text.ShouldBe("Scripts/door.luau");
        row.Rejection.ShouldBe("Not applied: File must be a path inside this project's Assets folder.");
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
        // The node is a script before and after, so the boxes stay the same boxes.
        var rig = new Rig();
        SceneNode node = Opener();
        rig.Publish(node);
        PropertyRowModel file = rig.Row(PropertyId.ScriptFile);

        node.Script = new Script("Scripts/gate.luau") { IsEnabled = false };
        rig.Publish(node);

        rig.Row(PropertyId.ScriptFile).ShouldBeSameAs(file);
        file.Fields[0].Text.ShouldBe("Scripts/gate.luau");
        rig.Row(PropertyId.ScriptEnabled).Flag.ShouldBeFalse();
        rig.Edits.ShouldBeEmpty();
    }
}
