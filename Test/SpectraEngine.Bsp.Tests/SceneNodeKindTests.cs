using SpectraEngine.Core.Bsp;
using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Scene;
using System.Numerics;

namespace SpectraEngine.Bsp.Tests;

/// <summary>A node's kind, derived from what it carries, and the priority between payloads.</summary>
public sealed class SceneNodeKindTests
{
    private static Brush Box(BrushOperation operation = BrushOperation.Additive) =>
        Brush.CreateBox(Vector3.Zero, new Vector3(1f, 1f, 1f)).WithOperation(operation);

    [Fact]
    public void A_bare_node_is_empty()
    {
        var scene = new Scene("Kinds");
        SceneNode node = scene.Root.CreateChild("Marker");

        SceneNodeClassifier.Classify(node).ShouldBe(SceneNodeKind.Empty);
    }

    [Fact]
    public void A_node_with_children_and_no_payload_is_a_group()
    {
        // Nodes carry no group marker; grouping just creates a plain parent.
        var scene = new Scene("Kinds");
        SceneNode node = scene.Root.CreateChild("Group");
        node.CreateChild("Child");

        SceneNodeClassifier.Classify(node).ShouldBe(SceneNodeKind.Group);
    }

    [Fact]
    public void A_group_stops_being_one_when_it_is_emptied()
    {
        var scene = new Scene("Kinds");
        SceneNode node = scene.Root.CreateChild("Group");
        SceneNode child = node.CreateChild("Child");

        node.RemoveChild(child);

        SceneNodeClassifier.Classify(node).ShouldBe(SceneNodeKind.Empty);
    }

    [Fact]
    public void An_additive_world_brush_is_world_geometry()
    {
        var scene = new Scene("Kinds");
        SceneNode node = scene.Root.CreateChild("Wall");
        node.Brush = Box();

        SceneNodeClassifier.Classify(node).ShouldBe(SceneNodeKind.BrushWorld);
    }

    [Fact]
    public void An_additive_part_brush_is_a_part()
    {
        var scene = new Scene("Kinds");
        SceneNode node = scene.Root.CreateChild("Crate");
        node.Brush = Box();
        node.BrushKind = BrushKind.Part;

        SceneNodeClassifier.Classify(node).ShouldBe(SceneNodeKind.BrushPart);
    }

    [Fact]
    public void A_subtractive_brush_outranks_its_kind()
    {
        // A subtractive brush renders nothing, so the tree is the only place
        // it can be seen.
        var scene = new Scene("Kinds");
        SceneNode node = scene.Root.CreateChild("DoorwayCut");
        node.Brush = Box(BrushOperation.Subtractive);

        SceneNodeClassifier.Classify(node).ShouldBe(SceneNodeKind.BrushSubtractive);

        node.BrushKind = BrushKind.Part;
        SceneNodeClassifier.Classify(node).ShouldBe(SceneNodeKind.BrushSubtractive);
    }

    [Fact]
    public void A_light_is_a_light()
    {
        var scene = new Scene("Kinds");
        SceneNode node = scene.Root.CreateChild("Sun");
        node.Light = new Light();

        SceneNodeClassifier.Classify(node).ShouldBe(SceneNodeKind.Light);
    }

    [Fact]
    public void An_entity_is_an_entity_even_with_children_under_it()
    {
        var scene = new Scene("Kinds");
        SceneNode node = scene.Root.CreateChild("Door");
        node.Entity = new EntityData("func_door");
        node.CreateChild("Handle");

        SceneNodeClassifier.Classify(node).ShouldBe(SceneNodeKind.Entity);
    }

    [Fact]
    public void An_entity_outranks_a_light()
    {
        var scene = new Scene("Kinds");
        SceneNode node = scene.Root.CreateChild("Lamp");
        node.Light = new Light();
        node.Entity = new EntityData("light_dynamic");

        SceneNodeClassifier.Classify(node).ShouldBe(SceneNodeKind.Entity);

        node.Entity = null;
        SceneNodeClassifier.Classify(node).ShouldBe(SceneNodeKind.Light);
    }

    [Fact]
    public void A_brush_carrying_entity_data_still_reads_as_its_brush_kind()
    {
        // There are no brush entities yet, so the geometry wins.
        var scene = new Scene("Kinds");
        SceneNode node = scene.Root.CreateChild("Trigger");
        node.Brush = Box();
        node.Entity = new EntityData("trigger_multiple");

        SceneNodeClassifier.Classify(node).ShouldBe(SceneNodeKind.BrushWorld);

        node.Brush = Box(BrushOperation.Subtractive);
        SceneNodeClassifier.Classify(node).ShouldBe(SceneNodeKind.BrushSubtractive);
    }

    [Fact]
    public void A_node_stops_being_an_entity_when_the_data_is_taken_off_it()
    {
        var scene = new Scene("Kinds");
        SceneNode node = scene.Root.CreateChild("Door");
        node.Entity = new EntityData("func_door");

        node.Entity = null;

        SceneNodeClassifier.Classify(node).ShouldBe(SceneNodeKind.Empty);
    }

    [Fact]
    public void A_brush_that_also_carries_children_is_still_a_brush()
    {
        var scene = new Scene("Kinds");
        SceneNode node = scene.Root.CreateChild("Wall");
        node.Brush = Box();
        node.CreateChild("Decal");

        SceneNodeClassifier.Classify(node).ShouldBe(SceneNodeKind.BrushWorld);
    }
}
