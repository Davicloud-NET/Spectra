using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Scene;

namespace SpectraEngine.Bsp.Tests;

/// <summary>The scene's list of nodes that carry entity data.</summary>
public sealed class SceneEntityNodeTests
{
    [Fact]
    public void A_node_is_listed_while_it_carries_an_entity()
    {
        var scene = new Scene("Entities");
        SceneNode node = scene.Root.CreateChild("relay");
        scene.EntityNodes.ShouldBeEmpty();

        node.Entity = new EntityData("logic_relay");
        scene.EntityNodes.ShouldBe([node]);

        node.Entity = null;
        scene.EntityNodes.ShouldBeEmpty();
    }

    [Fact]
    public void An_entity_given_before_the_node_is_attached_is_listed_when_it_is()
    {
        var scene = new Scene("Entities");
        var group = new SceneNode("group");
        SceneNode inner = group.CreateChild("inner");
        inner.Entity = new EntityData("logic_relay");
        scene.EntityNodes.ShouldBeEmpty();

        scene.Root.AddChild(group);

        scene.EntityNodes.ShouldBe([inner]);
    }

    [Fact]
    public void A_node_that_leaves_the_scene_and_comes_back_is_listed_again()
    {
        var scene = new Scene("Entities");
        SceneNode node = EntityRuntime.Place(scene.Root, "relay", "logic_relay");

        scene.Root.RemoveChild(node);
        scene.EntityNodes.ShouldBeEmpty();

        scene.Root.AddChild(node);
        scene.EntityNodes.ShouldBe([node]);
    }

    [Fact]
    public void The_list_is_in_the_order_the_nodes_were_attached()
    {
        var scene = new Scene("Entities");
        SceneNode second = EntityRuntime.Place(scene.Root, "second", "logic_relay");
        var first = new SceneNode("first") { Entity = new EntityData("logic_relay") };
        scene.Root.InsertChild(0, first);

        scene.EntityNodes.ShouldBe([second, first]);
    }

    [Fact]
    public void A_copy_of_an_entity_node_is_listed_beside_the_original()
    {
        var scene = new Scene("Entities");
        SceneNode node = EntityRuntime.Place(scene.Root, "relay", "logic_relay");

        SceneNode copy = scene.Root.AddChild(node.Clone());

        scene.EntityNodes.ShouldBe([node, copy]);
    }
}
