using SpectraEngine.Core.Bsp;
using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Inspection;
using SpectraEngine.Core.Scene;
using System.Numerics;

namespace SpectraEngine.Bsp.Tests;

/// <summary>The rows a property panel shows for a node's script.</summary>
public sealed class ScriptInspectorTests
{
    private static List<PropertyRow> Describe(params SceneNode[] nodes)
    {
        var rows = new List<PropertyRow>();
        NodeInspector.Describe(nodes, rows);
        return rows;
    }

    private static List<PropertyRow> ScriptRows(List<PropertyRow> rows) =>
        rows.Where(row => row.Group == NodeInspector.ScriptGroup).ToList();

    [Fact]
    public void A_script_node_shows_its_file_and_its_two_switches()
    {
        var node = new SceneNode("Opener")
        {
            Script = new Script("Scripts/door.luau") { IsEnabled = false, IsModule = true },
        };

        List<PropertyRow> rows = ScriptRows(Describe(node));

        rows.Select(row => row.Name).ShouldBe(["File", "Enabled", "Module"]);
        rows.Select(row => row.Id).ShouldBe(
            [PropertyId.ScriptFile, PropertyId.ScriptEnabled, PropertyId.ScriptModule]);

        rows[0].Kind.ShouldBe(PropertyKind.Text);
        rows[0].Text.ShouldBe("Scripts/door.luau");
        rows[1].Kind.ShouldBe(PropertyKind.Boolean);
        rows[1].Flag.ShouldBeFalse();
        rows[2].Kind.ShouldBe(PropertyKind.Boolean);
        rows[2].Flag.ShouldBeTrue();
        rows.ShouldAllBe(row => row.IsEditable);
    }

    [Fact]
    public void A_new_script_shows_switched_on_and_no_module()
    {
        List<PropertyRow> rows = ScriptRows(Describe(
            new SceneNode("Opener") { Script = new Script("Scripts/door.luau") }));

        rows[1].Flag.ShouldBeTrue();
        rows[2].Flag.ShouldBeFalse();
    }

    [Fact]
    public void The_module_row_says_what_a_module_is()
    {
        PropertyRow module = Describe(new SceneNode("Shared") { Script = new Script("Scripts/shared.luau") })
            .Single(row => row.Id == PropertyId.ScriptModule);

        module.Help.ShouldContain("another script asks for it");
    }

    [Fact]
    public void A_node_with_no_script_grows_no_script_group()
    {
        Describe(new SceneNode("Plain")).ShouldNotContain(row => row.Group == NodeInspector.ScriptGroup);
        Describe(new SceneNode("Lamp") { Light = new Light() })
            .ShouldNotContain(row => row.Group == NodeInspector.ScriptGroup);
    }

    [Fact]
    public void A_script_node_has_no_group_for_a_payload_it_does_not_carry()
    {
        var node = new SceneNode("Opener") { Script = new Script("Scripts/door.luau") };

        Describe(node).Select(row => row.Group).Distinct().ShouldBe(
            [NodeInspector.NodeGroup, NodeInspector.TransformGroup, NodeInspector.ScriptGroup]);
    }

    [Fact]
    public void The_script_group_comes_after_the_entity_and_before_the_material()
    {
        // The panel lays out in PropertyId order, and a merged selection sorts by it.
        var node = new SceneNode("Door")
        {
            BrushKind = BrushKind.Part,
            Brush = Brush.CreateBox(new Vector3(-1f), new Vector3(1f)),
            Entity = new EntityData("func_door"),
            Script = new Script("Scripts/door.luau"),
        };

        List<PropertyRow> rows = Describe(node);

        rows.Select(row => row.Group).Distinct().ShouldBe(
        [
            NodeInspector.NodeGroup,
            NodeInspector.TransformGroup,
            NodeInspector.BrushGroup,
            NodeInspector.BehaviorGroup,
            NodeInspector.EntityGroup,
            NodeInspector.ScriptGroup,
            NodeInspector.MaterialGroup,
        ]);
        rows.Select(row => (int)row.Id).ShouldBeInOrder();
    }

    [Fact]
    public void Two_scripts_that_agree_merge_and_two_that_differ_say_which_rows_do()
    {
        var a = new SceneNode("A") { Script = new Script("Scripts/door.luau") };
        var b = new SceneNode("B") { Script = new Script("Scripts/door.luau") { IsEnabled = false } };

        List<PropertyRow> rows = ScriptRows(Describe(a, b));

        rows.Count.ShouldBe(3);
        rows.Single(row => row.Id == PropertyId.ScriptFile).IsMixed.ShouldBeFalse();
        rows.Single(row => row.Id == PropertyId.ScriptEnabled).IsMixed.ShouldBeTrue();
        rows.Single(row => row.Id == PropertyId.ScriptModule).IsMixed.ShouldBeFalse();
        rows.ShouldAllBe(row => row.PresentCount == 2 && row.SelectionCount == 2);
    }

    [Fact]
    public void A_selection_where_only_some_nodes_are_scripts_says_how_many_carry_the_rows()
    {
        var script = new SceneNode("Opener") { Script = new Script("Scripts/door.luau") };
        var plain = new SceneNode("Plain");

        List<PropertyRow> rows = ScriptRows(Describe(script, plain));

        rows.Count.ShouldBe(3);
        rows.ShouldAllBe(row => row.PresentCount == 1 && row.SelectionCount == 2 && row.IsPartial);
    }
}
