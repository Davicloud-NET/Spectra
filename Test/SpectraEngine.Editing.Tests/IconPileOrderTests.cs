using System.Collections.Generic;
using System.Numerics;
using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Scene;
using SpectraEngine.Editing.Viewport;

namespace SpectraEngine.Editing.Tests;

/// <summary>
/// A pile's row stands in the order of the scene tree, whatever order its
/// nodes were attached in.
/// </summary>
public sealed class IconPileOrderTests
{
    // The nodes of every icon, from the left of the screen to the right.
    private static List<SceneNode> Row(IconPileRig rig, SceneIcons icons)
    {
        icons.Place(rig.Scene, rig.Camera, rig.ViewportSize);

        var row = new List<(float Across, SceneNode Node)>();
        foreach (SceneIcon icon in icons.Lights)
            row.Add((Vector3.Dot(icon.At, rig.Camera.Right), rig.Scene.LightNodes[icon.Node]));

        foreach (SceneIcon icon in icons.Markers)
            row.Add((Vector3.Dot(icon.At, rig.Camera.Right), rig.Scene.EntityNodes[icon.Node]));

        row.Sort((a, b) => a.Across.CompareTo(b.Across));
        return row.ConvertAll(entry => entry.Node);
    }

    [Fact]
    public void An_entity_that_is_deleted_and_put_back_takes_its_old_place_in_the_row()
    {
        var rig = new IconPileRig();
        var icons = new SceneIcons();
        SceneNode[] lift = rig.AddLift(Vector3.Zero);
        Row(rig, icons).ShouldBe(lift);

        // What a delete and its undo do: the node comes back at its old index.
        int index = lift[1].IndexInParent;
        rig.Scene.Root.RemoveChild(lift[1]);
        rig.Scene.Root.InsertChild(index, lift[1]);

        Row(rig, icons).ShouldBe(lift);
    }

    [Fact]
    public void A_light_stands_where_the_tree_has_it_among_the_entities()
    {
        var rig = new IconPileRig();
        SceneNode sound = rig.AddEntity(IconPileRig.Sound, Vector3.Zero);
        SceneNode lamp = rig.AddLamp(Vector3.Zero);
        SceneNode relay = rig.AddEntity(IconPileRig.Relay, Vector3.Zero);

        Row(rig, new SceneIcons()).ShouldBe([sound, lamp, relay]);
    }

    [Fact]
    public void An_entity_moved_to_the_top_of_the_tree_moves_to_the_front_of_its_row()
    {
        var rig = new IconPileRig();
        var icons = new SceneIcons();
        SceneNode[] lift = rig.AddLift(Vector3.Zero);
        Row(rig, icons).ShouldBe(lift);

        rig.Scene.Root.InsertChild(0, lift[3]);

        Row(rig, icons).ShouldBe([lift[3], lift[0], lift[1], lift[2]]);
    }

    [Fact]
    public void A_child_stands_after_its_parent_and_before_what_follows_the_parent()
    {
        var rig = new IconPileRig();
        SceneNode parent = rig.AddEntity(IconPileRig.Sound, Vector3.Zero);
        SceneNode next = rig.AddEntity(IconPileRig.Relay, Vector3.Zero);
        var child = new SceneNode("Child") { Entity = new EntityData(IconPileRig.Sound) };
        parent.AddChild(child);

        Row(rig, new SceneIcons()).ShouldBe([parent, child, next]);
    }

    [Fact]
    public void A_node_that_becomes_an_entity_takes_its_place_in_the_tree()
    {
        var rig = new IconPileRig();
        var icons = new SceneIcons();
        SceneNode first = rig.AddEntity(IconPileRig.Sound, Vector3.Zero);
        SceneNode plain = rig.Scene.Root.CreateChild("Plain");
        SceneNode last = rig.AddEntity(IconPileRig.Relay, Vector3.Zero);
        Row(rig, icons).ShouldBe([first, last]);

        plain.Entity = new EntityData(IconPileRig.Sound);

        Row(rig, icons).ShouldBe([first, plain, last]);
    }

    [Fact]
    public void The_row_of_another_scene_is_in_that_scenes_order()
    {
        var one = new IconPileRig();
        var other = new IconPileRig();
        var icons = new SceneIcons();
        SceneNode[] lift = one.AddLift(Vector3.Zero);
        SceneNode[] second = other.AddLift(Vector3.Zero);
        other.Scene.Root.InsertChild(0, second[2]);

        Row(one, icons).ShouldBe(lift);
        Row(other, icons).ShouldBe([second[2], second[0], second[1], second[3]]);
        Row(one, icons).ShouldBe(lift);
    }
}
