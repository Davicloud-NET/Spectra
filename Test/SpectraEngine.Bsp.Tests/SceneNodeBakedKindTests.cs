using Microsoft.Extensions.Logging.Abstractions;
using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Scene;
using System.Numerics;

namespace SpectraEngine.Bsp.Tests;

/// <summary>
/// A node of a baked level that lost its world brush or its mesh to the bake:
/// it still has the class name it had, and a world brush still does not move.
/// </summary>
public sealed class SceneNodeBakedKindTests
{
    [Fact]
    public void A_node_holds_no_world_brush_until_one_is_at_or_below_it()
    {
        var scene = new Scene("Baked");
        SceneNode room = scene.Root.CreateChild("Room");
        SceneNode shelf = room.CreateChild("Shelf");

        room.HoldsWorldBrush.ShouldBeFalse();

        SceneNode wall = shelf.AddChild(new SceneNode("Wall") { BakedKind = SceneNodeKind.BrushWorld });

        wall.HoldsWorldBrush.ShouldBeTrue();
        shelf.HoldsWorldBrush.ShouldBeTrue();
        room.HoldsWorldBrush.ShouldBeTrue();
        scene.Root.HoldsWorldBrush.ShouldBeTrue();

        // Neither count of live brushes moved: nothing is there to compile.
        room.SubtreeBrushCount.ShouldBe(0);
        room.SubtreeStaticWorldBrushCount.ShouldBe(0);
    }

    [Fact]
    public void A_cut_that_was_baked_counts_as_a_world_brush_and_a_mesh_does_not()
    {
        new SceneNode("Cut") { BakedKind = SceneNodeKind.BrushSubtractive }.HoldsWorldBrush.ShouldBeTrue();
        new SceneNode("Prop") { BakedKind = SceneNodeKind.Mesh }.HoldsWorldBrush.ShouldBeFalse();
        new SceneNode("Plain").HoldsWorldBrush.ShouldBeFalse();
    }

    [Fact]
    public void The_count_follows_a_node_that_is_moved_to_another_parent_or_taken_out()
    {
        var scene = new Scene("Baked");
        SceneNode left = scene.Root.CreateChild("Left");
        SceneNode right = scene.Root.CreateChild("Right");
        SceneNode group = left.CreateChild("Group");
        group.AddChild(new SceneNode("Wall") { BakedKind = SceneNodeKind.BrushWorld });
        group.AddChild(new SceneNode("Floor") { BakedKind = SceneNodeKind.BrushWorld });

        right.AddChild(group);

        left.HoldsWorldBrush.ShouldBeFalse();
        right.HoldsWorldBrush.ShouldBeTrue();

        right.RemoveChild(group);

        right.HoldsWorldBrush.ShouldBeFalse();
        scene.Root.HoldsWorldBrush.ShouldBeFalse();
        group.HoldsWorldBrush.ShouldBeTrue();
    }

    [Fact]
    public void A_live_world_brush_is_held_too()
    {
        var scene = new Scene("Authored");
        SceneNode room = scene.Root.CreateChild("Room");
        room.CreateChild("Wall").Brush = Core.Bsp.Brush.CreateBox(new Vector3(-1f), new Vector3(1f));

        room.HoldsWorldBrush.ShouldBeTrue();

        SceneNode part = scene.Root.CreateChild("Parts").CreateChild("Crate");
        part.BrushKind = BrushKind.Part;
        part.Brush = Core.Bsp.Brush.CreateBox(new Vector3(-1f), new Vector3(1f));

        part.HoldsWorldBrush.ShouldBeFalse();
    }

    [Fact]
    public void A_copy_is_what_the_node_it_was_copied_from_is()
    {
        var wall = new SceneNode("Wall") { BakedKind = SceneNodeKind.BrushWorld };
        wall.AddChild(new SceneNode("Cut") { BakedKind = SceneNodeKind.BrushSubtractive });

        SceneNode copy = wall.Clone();

        copy.BakedKind.ShouldBe(SceneNodeKind.BrushWorld);
        copy.Children[0].BakedKind.ShouldBe(SceneNodeKind.BrushSubtractive);
        NodeClassName.Of(copy).ShouldBe(NodeClassName.Part);

        var above = new SceneNode("Above");
        above.AddChild(copy);
        above.HoldsWorldBrush.ShouldBeTrue();
    }

    [Theory]
    [InlineData(SceneNodeKind.BrushWorld, NodeClassName.Part)]
    [InlineData(SceneNodeKind.BrushSubtractive, NodeClassName.Part)]
    [InlineData(SceneNodeKind.Mesh, NodeClassName.MeshPart)]
    public void A_baked_node_has_the_class_name_of_what_the_bake_took(SceneNodeKind baked, string className)
    {
        NodeClassName.Of(new SceneNode("Node") { BakedKind = baked }).ShouldBe(className);

        // With a script node under it, which makes a bare node a group.
        var parent = new SceneNode("Node") { BakedKind = baked };
        parent.CreateChild("Script").Script = new Script("Scripts/a.luau");
        NodeClassName.Of(parent).ShouldBe(className);
    }

    [Fact]
    public void An_entity_and_a_light_come_before_a_mesh_as_they_do_on_an_authored_node()
    {
        var lamp = new SceneNode("Lamp") { BakedKind = SceneNodeKind.Mesh, Light = new Light { Kind = LightKind.Spot } };
        var door = new SceneNode("Door") { BakedKind = SceneNodeKind.Mesh, Entity = new EntityData("func_door") };

        NodeClassName.Of(lamp).ShouldBe("SpotLight");
        NodeClassName.Of(door).ShouldBe("func_door");
    }

    [Fact]
    public void A_brush_comes_before_a_light_as_it_does_on_an_authored_node()
    {
        var authored = new SceneNode("Wall")
        {
            Brush = Core.Bsp.Brush.CreateBox(new Vector3(-1f), new Vector3(1f)),
            Light = new Light(),
        };
        var baked = new SceneNode("Wall") { BakedKind = SceneNodeKind.BrushWorld, Light = new Light() };

        NodeClassName.Of(baked).ShouldBe(NodeClassName.Of(authored));
    }

    [Fact]
    public void A_running_level_refuses_to_move_a_baked_world_brush_as_it_refuses_a_live_one()
    {
        var scene = new Scene("Both");
        SceneNode live = scene.Root.CreateChild("Live");
        live.Brush = Core.Bsp.Brush.CreateBox(new Vector3(-1f), new Vector3(1f));
        SceneNode baked = scene.Root.AddChild(new SceneNode("Baked") { BakedKind = SceneNodeKind.BrushWorld });
        SceneNode above = scene.Root.CreateChild("Above");
        above.AddChild(new SceneNode("Cut") { BakedKind = SceneNodeKind.BrushSubtractive });

        var world = new EntityWorld(scene, NullLogger.Instance, new EntityCatalog());
        world.Activate();

        var up = new Transform { Position = Vector3.UnitY, Rotation = Quaternion.Identity, Scale = Vector3.One };
        world.TrySetLocalTransform(live, in up, out string? ofLive).ShouldBeFalse();
        world.TrySetLocalTransform(baked, in up, out string? ofBaked).ShouldBeFalse();
        world.TrySetLocalTransform(above, in up, out string? ofAbove).ShouldBeFalse();

        ofBaked.ShouldBe(ofLive);
        ofAbove.ShouldBe(ofLive);
        baked.LocalPosition.ShouldBe(Vector3.Zero);
        world.Deactivate();
    }
}
