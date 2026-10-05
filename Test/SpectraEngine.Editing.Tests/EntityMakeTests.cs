using SpectraEngine.Core.Bsp;
using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Scene;
using SpectraEngine.Editing.Commands;
using SpectraEngine.Editing.Hosting;

namespace SpectraEngine.Editing.Tests;

/// <summary>
/// Make entity: what it stamps on the selection, that it is one undo entry,
/// and what it refuses.
/// </summary>
public sealed class EntityMakeTests
{
    private const PhysicsFlags Sensing = PhysicsFlags.CanTouch | PhysicsFlags.Anchored;

    [Fact]
    public void A_block_made_a_door_becomes_a_solid_drawn_part_with_the_entity()
    {
        var rig = new EntityEditRig();
        SceneNode block = rig.AddBlock("Door");

        EntityEditReport report = rig.Make(EntityEditRig.Door, block);

        report.Applied.ShouldBeTrue(report.Message);
        block.BrushKind.ShouldBe(BrushKind.Part);
        block.Entity.ShouldNotBeNull();
        block.Entity.ClassName.ShouldBe(EntityEditRig.Door);
        block.Entity.Keyvalues.ShouldBeEmpty("a default belongs in the map only once somebody changes it");
        block.PhysicsFlags.ShouldBe(PhysicsFlags.Default);
        block.IsRendered.ShouldBeTrue();
        block.Name.ShouldBe("Door", "making an entity keeps the name a wire may already target");
        rig.Scene.Selection.Items.ShouldBe([block]);
    }

    [Fact]
    public void A_block_made_a_volume_senses_and_is_neither_drawn_nor_solid()
    {
        var rig = new EntityEditRig();
        SceneNode block = rig.AddBlock("Doorway");

        rig.Make(EntityEditRig.Trigger, block).Applied.ShouldBeTrue();

        block.BrushKind.ShouldBe(BrushKind.Part);
        block.PhysicsFlags.ShouldBe(Sensing);
        block.IsRendered.ShouldBeFalse();
        rig.Scene.HiddenBrushNodes.ShouldBe([block], "the outline is the only way to see it now");
    }

    [Fact]
    public void Making_an_entity_is_one_undo_entry_that_puts_everything_back()
    {
        var rig = new EntityEditRig();
        SceneNode block = rig.AddBlock("Doorway");
        block.CanQuery = false;
        block.Anchored = false;
        Brush authored = block.Brush!;
        PhysicsFlags flags = block.PhysicsFlags;

        rig.Make(EntityEditRig.Trigger, block);
        rig.Host.UndoDepth.ShouldBe(1);

        rig.Host.Apply(EditorHostCommand.Undo);

        block.BrushKind.ShouldBe(BrushKind.World);
        block.Brush.ShouldBeSameAs(authored, "undo gives back the world-aligned faces");
        block.PhysicsFlags.ShouldBe(flags);
        block.IsRendered.ShouldBeTrue();
        block.Entity.ShouldBeNull();
        rig.Host.UndoDepth.ShouldBe(0);

        rig.Host.Apply(EditorHostCommand.Redo);

        block.BrushKind.ShouldBe(BrushKind.Part);
        block.Entity!.ClassName.ShouldBe(EntityEditRig.Trigger);
        block.IsRendered.ShouldBeFalse();
        block.Anchored.ShouldBeFalse("the stamp writes collide, query and touch and leaves the rest");
    }

    [Fact]
    public void A_group_takes_the_entity_and_every_brush_below_it_is_stamped()
    {
        var rig = new EntityEditRig();
        SceneNode group = rig.Scene.Root.CreateChild("Gate");
        SceneNode left = rig.AddBlock("Left", group);
        SceneNode right = rig.AddPart("Right", group);
        SceneNode inner = group.CreateChild("Inner");
        SceneNode deep = rig.AddBlock("Deep", inner);

        rig.Make(EntityEditRig.Trigger, group).Applied.ShouldBeTrue();

        group.Entity!.ClassName.ShouldBe(EntityEditRig.Trigger);
        foreach (SceneNode brush in new[] { left, right, deep })
        {
            brush.BrushKind.ShouldBe(BrushKind.Part, brush.Name);
            brush.IsRendered.ShouldBeFalse(brush.Name);
            brush.CanCollide.ShouldBeFalse(brush.Name);
            brush.Entity.ShouldBeNull(brush.Name);
        }

        group.SubtreeStaticWorldBrushCount.ShouldBe(0, "a world brush below it would refuse the move at play time");
        rig.Host.UndoDepth.ShouldBe(1);

        rig.Host.Apply(EditorHostCommand.Undo);

        group.Entity.ShouldBeNull();
        left.BrushKind.ShouldBe(BrushKind.World);
        deep.BrushKind.ShouldBe(BrushKind.World);
        right.BrushKind.ShouldBe(BrushKind.Part);
        right.IsRendered.ShouldBeTrue();
    }

    [Fact]
    public void An_entity_inside_the_group_keeps_its_own_flags()
    {
        // The door does not own the trigger's brush, so it must not make it solid.
        var rig = new EntityEditRig();
        SceneNode group = rig.Scene.Root.CreateChild("Door");
        rig.AddBlock("Leaf", group);
        SceneNode sensor = rig.AddBlock("Sensor", group);
        rig.Make(EntityEditRig.Trigger, sensor).Applied.ShouldBeTrue();

        rig.Make(EntityEditRig.Door, group).Applied.ShouldBeTrue();

        sensor.IsRendered.ShouldBeFalse();
        sensor.PhysicsFlags.ShouldBe(Sensing);
        sensor.Entity!.ClassName.ShouldBe(EntityEditRig.Trigger);
    }

    [Fact]
    public void A_block_under_an_inner_entity_still_becomes_a_part()
    {
        // The play-time move counts every world brush below the node, owned or not.
        var rig = new EntityEditRig();
        SceneNode group = rig.Scene.Root.CreateChild("Lift");
        rig.AddPart("Floor", group);
        SceneNode marker = group.CreateChild("Marker");
        marker.Entity = new EntityData(EntityEditRig.Marker);
        SceneNode stray = rig.AddBlock("Stray", marker);

        rig.Make(EntityEditRig.Door, group).Applied.ShouldBeTrue();

        stray.BrushKind.ShouldBe(BrushKind.Part);
        group.SubtreeStaticWorldBrushCount.ShouldBe(0);
    }

    [Fact]
    public void A_cut_is_refused_and_told_how_to_become_solid()
    {
        var rig = new EntityEditRig();
        SceneNode cut = rig.AddCut("Hole");

        EntityEditReport report = rig.Make(EntityEditRig.Door, cut);

        report.Applied.ShouldBeFalse();
        report.Message.ShouldContain("'Hole' cuts solid");
        report.Message.ShouldContain("Adds solid");
        cut.Entity.ShouldBeNull();
        cut.BrushKind.ShouldBe(BrushKind.World);
        rig.Host.UndoDepth.ShouldBe(0);
    }

    [Fact]
    public void A_group_with_a_cut_in_it_is_refused_and_names_the_cut()
    {
        var rig = new EntityEditRig();
        SceneNode group = rig.Scene.Root.CreateChild("Wall");
        SceneNode block = rig.AddBlock("Slab", group);
        rig.AddCut("Window", group);

        EntityEditReport report = rig.Make(EntityEditRig.Door, group);

        report.Applied.ShouldBeFalse();
        report.Message.ShouldContain("'Window' cuts solid");
        report.Message.ShouldContain("Move it out of 'Wall'");
        block.BrushKind.ShouldBe(BrushKind.World, "a refusal changes nothing");
        rig.Host.UndoDepth.ShouldBe(0);
    }

    [Fact]
    public void A_node_that_is_already_an_entity_is_refused_and_pointed_at_remove()
    {
        var rig = new EntityEditRig();
        SceneNode block = rig.AddBlock("Door");
        rig.Make(EntityEditRig.Door, block).Applied.ShouldBeTrue();

        EntityEditReport report = rig.Make(EntityEditRig.Trigger, block);

        report.Applied.ShouldBeFalse();
        report.Message.ShouldContain($"already a {EntityEditRig.Door}");
        report.Message.ShouldContain("Remove entity");
        block.Entity!.ClassName.ShouldBe(EntityEditRig.Door);
        block.IsRendered.ShouldBeTrue();
        rig.Host.UndoDepth.ShouldBe(1);
    }

    [Fact]
    public void Something_with_no_geometry_is_refused()
    {
        var rig = new EntityEditRig();
        SceneNode empty = rig.Scene.Root.CreateChild("Empty");

        EntityEditReport report = rig.Make(EntityEditRig.Door, empty);

        report.Applied.ShouldBeFalse();
        report.Message.ShouldContain("no block or part");
        empty.Entity.ShouldBeNull();
    }

    [Fact]
    public void Several_selected_nodes_are_refused_and_told_to_group()
    {
        var rig = new EntityEditRig();
        SceneNode a = rig.AddBlock("A");
        SceneNode b = rig.AddBlock("B");

        EntityEditReport report = rig.Make(EntityEditRig.Door, a, b);

        report.Applied.ShouldBeFalse();
        report.Message.ShouldContain("Group the 2 selected nodes first (Ctrl+G)");
        a.Entity.ShouldBeNull();
        b.Entity.ShouldBeNull();
    }

    [Fact]
    public void Nothing_selected_is_refused()
    {
        var rig = new EntityEditRig();

        EntityEditReport report = rig.Make(EntityEditRig.Door);

        report.Applied.ShouldBeFalse();
        report.Message.ShouldContain("Select one first");
    }

    [Theory]
    [InlineData(EntityEditRig.Relay)]
    [InlineData(EntityEditRig.Marker)]
    public void A_class_that_is_not_made_from_geometry_is_refused(string className)
    {
        var rig = new EntityEditRig();
        SceneNode block = rig.AddBlock("Block");

        EntityEditReport report = rig.Make(className, block);

        report.Applied.ShouldBeFalse();
        report.Message.ShouldContain("Insert, Entity");
        block.Entity.ShouldBeNull();
        block.BrushKind.ShouldBe(BrushKind.World);
    }

    [Fact]
    public void A_class_the_project_does_not_have_is_refused()
    {
        var rig = new EntityEditRig();
        SceneNode block = rig.AddBlock("Block");

        EntityEditReport report = rig.Make("nothing_declares_this", block);

        report.Applied.ShouldBeFalse();
        report.Message.ShouldContain("nothing_declares_this");
        block.Entity.ShouldBeNull();
    }

    [Fact]
    public void Nothing_is_made_while_the_level_plays()
    {
        var rig = new EntityEditRig();
        SceneNode block = rig.AddBlock("Block");
        rig.Host.Suspend();

        EntityEditReport report = rig.Make(EntityEditRig.Door, block);

        report.Applied.ShouldBeFalse();
        report.Message.ShouldContain("Stop it first");
        block.Entity.ShouldBeNull();
    }
}
