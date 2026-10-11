using SpectraEngine.Core.Bsp;
using SpectraEngine.Core.Scene;
using System.Numerics;

namespace SpectraEngine.Bsp.Tests;

/// <summary>
/// A script on a node: the scene's list of such nodes, what a copy carries,
/// and that the payload costs the world nothing.
/// </summary>
public sealed class SceneScriptNodeTests
{
    private static Script Door() => new("Scripts/door.luau");

    private static Brush Box() => Brush.CreateBox(new Vector3(-1f), new Vector3(1f));

    [Fact]
    public void A_node_is_listed_while_it_carries_a_script()
    {
        var scene = new Scene("Scripts");
        SceneNode node = scene.Root.CreateChild("Opener");
        scene.ScriptNodes.ShouldBeEmpty();

        node.Script = Door();
        scene.ScriptNodes.ShouldBe([node]);

        node.Script = null;
        scene.ScriptNodes.ShouldBeEmpty();
    }

    [Fact]
    public void A_script_given_before_the_node_is_attached_is_listed_when_it_is()
    {
        var scene = new Scene("Scripts");
        var part = new SceneNode("Door");
        SceneNode inner = part.CreateChild("Opener");
        inner.Script = Door();
        scene.ScriptNodes.ShouldBeEmpty();

        scene.Root.AddChild(part);

        scene.ScriptNodes.ShouldBe([inner]);
    }

    [Fact]
    public void A_node_that_leaves_the_scene_and_comes_back_is_listed_again()
    {
        // What undo of a delete does.
        var scene = new Scene("Scripts");
        SceneNode part = scene.Root.CreateChild("Door");
        SceneNode opener = part.CreateChild("Opener");
        opener.Script = Door();

        scene.Root.RemoveChild(part);
        scene.ScriptNodes.ShouldBeEmpty();

        scene.Root.AddChild(part);
        scene.ScriptNodes.ShouldBe([opener]);
    }

    [Fact]
    public void A_node_moved_under_another_parent_stays_listed_once()
    {
        var scene = new Scene("Scripts");
        SceneNode door = scene.Root.CreateChild("Door");
        SceneNode gate = scene.Root.CreateChild("Gate");
        SceneNode opener = door.CreateChild("Opener");
        opener.Script = Door();

        gate.AddChild(opener);

        scene.ScriptNodes.ShouldBe([opener]);
        opener.Parent.ShouldBe(gate);
    }

    [Fact]
    public void A_node_moved_to_another_scene_is_listed_there_and_not_here()
    {
        var here = new Scene("Here");
        var there = new Scene("There");
        SceneNode opener = here.Root.CreateChild("Opener");
        opener.Script = Door();

        there.Root.AddChild(opener);

        here.ScriptNodes.ShouldBeEmpty();
        there.ScriptNodes.ShouldBe([opener]);
    }

    [Fact]
    public void The_list_is_in_the_order_the_nodes_were_attached()
    {
        var scene = new Scene("Scripts");
        SceneNode second = scene.Root.CreateChild("Second");
        second.Script = Door();
        var first = new SceneNode("First") { Script = Door() };
        scene.Root.InsertChild(0, first);

        scene.ScriptNodes.ShouldBe([second, first]);
    }

    [Fact]
    public void A_copy_carries_the_script_and_is_listed_beside_the_original()
    {
        var scene = new Scene("Scripts");
        SceneNode node = scene.Root.CreateChild("Opener");
        node.Script = new Script("Scripts/door.luau") { IsEnabled = false, IsModule = true };

        SceneNode copy = scene.Root.AddChild(node.Clone());

        copy.Script.ShouldBe(node.Script);
        scene.ScriptNodes.ShouldBe([node, copy]);
    }

    [Fact]
    public void An_edit_of_the_original_leaves_its_copy_alone()
    {
        var node = new SceneNode("Opener") { Script = Door() };
        SceneNode copy = node.Clone();

        node.Script = Door() with { IsEnabled = false };

        copy.Script.ShouldNotBeNull().IsEnabled.ShouldBeTrue();
    }

    [Fact]
    public void A_deep_copy_carries_the_scripts_under_it()
    {
        var part = new SceneNode("Door") { BrushKind = BrushKind.Part, Brush = Box() };
        part.CreateChild("Opener").Script = Door();

        SceneNode copy = part.Clone();

        copy.Children[0].Script.ShouldBe(Door());
    }

    [Fact]
    public void A_script_dirties_nothing_and_stays_out_of_the_spatial_index()
    {
        // A world brush elsewhere keeps a compiled world to dirty.
        var scene = new Scene("Scripts");
        scene.Root.CreateChild("Wall").Brush = Box();
        SceneNode opener = scene.Root.CreateChild("Opener");
        scene.RebuildStaticWorld(new FakeRenderer());
        scene.StaticWorldDirty.ShouldBeFalse();
        int compiles = scene.StaticWorldCompileCount;

        opener.Script = Door();
        opener.Script = Door() with { IsEnabled = false };
        opener.Script = null;
        opener.Script = Door();

        scene.StaticWorldDirty.ShouldBeFalse();
        scene.StaticWorldCompileCount.ShouldBe(compiles);
        scene.TryGetWorldBounds(opener, out _).ShouldBeFalse();
        scene.DrawableNodes.ShouldBeEmpty();
        opener.SubtreeBrushCount.ShouldBe(0);
        scene.Root.SubtreeBrushCount.ShouldBe(1);
        scene.Root.SubtreeStaticWorldBrushCount.ShouldBe(1);
    }

    [Fact]
    public void A_script_on_a_world_brush_leaves_the_compiled_world_alone()
    {
        var scene = new Scene("Scripts");
        SceneNode wall = scene.Root.CreateChild("Wall");
        wall.Brush = Box();
        scene.RebuildStaticWorld(new FakeRenderer());

        wall.Script = Door();

        scene.StaticWorldDirty.ShouldBeFalse();
        wall.SubtreeBrushCount.ShouldBe(1);
    }

    [Fact]
    public void The_same_script_set_again_changes_nothing()
    {
        var scene = new Scene("Scripts");
        SceneNode node = scene.Root.CreateChild("Opener");
        Script script = Door();
        node.Script = script;
        int raised = 0;
        scene.NodeKindChanged += _ => raised++;

        node.Script = script;

        raised.ShouldBe(0);
        scene.ScriptNodes.ShouldBe([node]);
    }
}
