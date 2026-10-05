using SpectraEngine.Core.Bsp;
using SpectraEngine.Core.Scene;
using SpectraEngine.Editing.Hosting;
using System;
using System.Linq;
using System.Numerics;

namespace SpectraEngine.Editing.Tests;

/// <summary>
/// Inserting an entity whose class is made from geometry: it arrives as a
/// part, flagged for its placement, ready for the move and size tools.
/// </summary>
public sealed class EntityInsertTests
{
    [Fact]
    public void A_volume_class_arrives_as_a_hidden_non_solid_part_with_the_entity()
    {
        var rig = new EntityEditRig();

        rig.Host.InsertEntity(EntityEditRig.Trigger);

        SceneNode node = rig.Scene.Root.Children.ShouldHaveSingleItem();
        node.Entity!.ClassName.ShouldBe(EntityEditRig.Trigger);
        node.Entity.Keyvalues.ShouldBeEmpty();
        node.BrushKind.ShouldBe(BrushKind.Part);
        node.Brush.ShouldNotBeNull();
        node.Brush.LocalBounds.Size.ShouldBe(new Vector3(2f), "a fresh brush is 2 by 2 by 2");
        node.IsRendered.ShouldBeFalse();
        node.CanCollide.ShouldBeFalse();
        node.CanQuery.ShouldBeFalse();
        node.CanTouch.ShouldBeTrue();
        rig.Scene.HiddenBrushNodes.ShouldBe([node]);
    }

    [Fact]
    public void A_brush_class_arrives_as_a_solid_drawn_part_with_the_entity()
    {
        var rig = new EntityEditRig();

        rig.Host.InsertEntity(EntityEditRig.Door);

        SceneNode node = rig.Scene.Root.Children.ShouldHaveSingleItem();
        node.Entity!.ClassName.ShouldBe(EntityEditRig.Door);
        node.BrushKind.ShouldBe(BrushKind.Part);
        node.Brush.ShouldNotBeNull();
        node.PhysicsFlags.ShouldBe(PhysicsFlags.Default);
        node.IsRendered.ShouldBeTrue();
        node.SubtreeStaticWorldBrushCount.ShouldBe(0, "it never counted as world geometry");
    }

    [Fact]
    public void It_is_one_undo_entry_and_comes_back_whole_under_the_same_id()
    {
        var rig = new EntityEditRig();

        rig.Host.InsertEntity(EntityEditRig.Trigger);

        SceneNode node = rig.Scene.Root.Children[0];
        rig.Scene.Selection.Items.ShouldBe([node]);
        rig.Host.UndoDepth.ShouldBe(1);
        rig.Scene.StaticWorldDirty.ShouldBeFalse("a part costs no compile");

        Guid id = node.Id;
        rig.Host.Apply(EditorHostCommand.Undo);
        rig.Scene.Root.Children.ShouldBeEmpty();
        rig.Scene.HiddenBrushNodes.ShouldBeEmpty();

        rig.Host.Apply(EditorHostCommand.Redo);
        SceneNode back = rig.Scene.Root.Children.ShouldHaveSingleItem();
        back.Id.ShouldBe(id);
        back.Entity.ShouldNotBeNull();
        back.IsRendered.ShouldBeFalse();
    }

    [Fact]
    public void It_rests_on_the_aimed_surface_like_a_part()
    {
        var rig = new EntityEditRig();
        SceneNode plate = rig.Scene.Root.CreateChild("Plate");
        plate.Brush = Brush.CreateBox(new Vector3(-8f, -1f, -8f), new Vector3(8f, 1f, 8f));
        rig.Scene.Camera.Position = new Vector3(0.5f, 8f, 4f);
        rig.Scene.Camera.LookAt(new Vector3(0.5f, 1f, 0.5f));

        rig.Host.InsertEntity(EntityEditRig.Door);

        rig.Scene.Root.Children[^1].LocalPosition.Y.ShouldBe(2f, 0.001f);
    }

    [Fact]
    public void Each_one_takes_the_first_free_number_so_a_wire_can_tell_them_apart()
    {
        var rig = new EntityEditRig();

        rig.Host.InsertEntity(EntityEditRig.Door);
        rig.Host.InsertEntity(EntityEditRig.Door);
        rig.Host.InsertEntity(EntityEditRig.Trigger);

        rig.Scene.Root.Children.Select(n => n.Name).ShouldBe(
            [$"{EntityEditRig.Door}1", $"{EntityEditRig.Door}2", $"{EntityEditRig.Trigger}1"]);

        rig.Scene.Selection.Select(rig.Scene.Root.Children[0]);
        rig.Host.Apply(EditorHostCommand.Delete);
        rig.Host.InsertEntity(EntityEditRig.Door);

        rig.Scene.Root.Children[^1].Name.ShouldBe($"{EntityEditRig.Door}1");
    }

    [Theory]
    [InlineData(EntityEditRig.Relay)]
    [InlineData(EntityEditRig.Marker)]
    public void A_point_or_logic_class_still_arrives_as_a_bare_node_named_after_the_class(string className)
    {
        var rig = new EntityEditRig();

        rig.Host.InsertEntity(className);

        SceneNode node = rig.Scene.Root.Children.ShouldHaveSingleItem();
        node.Name.ShouldBe(className);
        node.Brush.ShouldBeNull();
        node.Entity!.ClassName.ShouldBe(className);
        node.IsRendered.ShouldBeTrue();
    }

    [Fact]
    public void A_class_the_catalogue_does_not_know_arrives_as_a_bare_node()
    {
        // A map from another game names classes this build has no schema for.
        var rig = new EntityEditRig();

        rig.Host.InsertEntity("nothing_declares_this");

        SceneNode node = rig.Scene.Root.Children.ShouldHaveSingleItem();
        node.Brush.ShouldBeNull();
        node.Entity!.ClassName.ShouldBe("nothing_declares_this");
    }
}
