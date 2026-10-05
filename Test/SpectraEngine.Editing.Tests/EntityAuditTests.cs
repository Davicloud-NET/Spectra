using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Scene;
using SpectraEngine.Editing.Hosting;
using System.Collections.Generic;

namespace SpectraEngine.Editing.Tests;

/// <summary>
/// The audit behind the problem list: an entity made from geometry that still
/// has world geometry in it.
/// </summary>
public sealed class EntityAuditTests
{
    [Fact]
    public void A_made_entity_has_nothing_to_report()
    {
        var rig = new EntityEditRig();
        SceneNode group = rig.Scene.Root.CreateChild("Gate");
        rig.AddBlock("Left", group);
        rig.AddBlock("Right", group);
        rig.Make(EntityEditRig.Door, group);

        rig.Host.FindEntityProblems().ShouldBeEmpty();
    }

    [Fact]
    public void Converting_a_door_back_to_a_block_is_reported_and_names_the_way_out()
    {
        var rig = new EntityEditRig();
        SceneNode door = rig.AddBlock("Door");
        rig.Make(EntityEditRig.Door, door);

        rig.Host.Apply(EditorHostCommand.ToggleBrushKind);

        EntityProblem problem = rig.Host.FindEntityProblems().ShouldHaveSingleItem();
        problem.EntityId.ShouldBe(door.Id);
        problem.BrushId.ShouldBe(door.Id);
        problem.ClassName.ShouldBe(EntityEditRig.Door);
        problem.IsCut.ShouldBeFalse();
        problem.Describe().ShouldBe(
            $"'Door' is a {EntityEditRig.Door}, but it is still a block. " +
            "It will not work when the level plays. Select the block and convert it to a part (Ctrl+T).");

        rig.Host.Apply(EditorHostCommand.ToggleBrushKind);
        rig.Host.FindEntityProblems().ShouldBeEmpty();
    }

    [Fact]
    public void A_block_dropped_into_an_entity_group_is_reported_by_name()
    {
        var rig = new EntityEditRig();
        SceneNode group = rig.Scene.Root.CreateChild("Lift");
        rig.AddPart("Floor", group);
        rig.Make(EntityEditRig.Door, group);
        SceneNode stray = rig.AddBlock("Rail", group);

        EntityProblem problem = rig.Host.FindEntityProblems().ShouldHaveSingleItem();

        problem.EntityId.ShouldBe(group.Id);
        problem.BrushId.ShouldBe(stray.Id, "that is the node Ctrl+T has to land on");
        problem.Describe().ShouldContain("'Rail' inside it is still a block");
    }

    [Fact]
    public void A_cut_inside_an_entity_is_reported_as_a_cut()
    {
        var rig = new EntityEditRig();
        SceneNode group = rig.Scene.Root.CreateChild("Lift");
        rig.AddPart("Floor", group);
        rig.Make(EntityEditRig.Trigger, group);
        rig.AddCut("Shaft", group);

        EntityProblem problem = rig.Host.FindEntityProblems().ShouldHaveSingleItem();

        problem.IsCut.ShouldBeTrue();
        problem.Describe().ShouldContain("'Shaft' inside it is still a cut");
        problem.Describe().ShouldContain("Move the cut out of it.");
    }

    [Fact]
    public void A_point_class_and_an_unknown_class_are_left_alone()
    {
        // Neither is made from geometry, so neither is moved by its class.
        var rig = new EntityEditRig();
        SceneNode marker = rig.AddBlock("Marker");
        marker.Entity = new EntityData(EntityEditRig.Marker);
        SceneNode foreign = rig.AddBlock("Foreign");
        foreign.Entity = new EntityData("nothing_declares_this");

        rig.Host.FindEntityProblems().ShouldBeEmpty();
    }

    [Fact]
    public void Every_stuck_entity_is_listed_in_scene_order()
    {
        var rig = new EntityEditRig();
        SceneNode first = rig.AddBlock("First");
        first.Entity = new EntityData(EntityEditRig.Door);
        SceneNode second = rig.AddBlock("Second");
        second.Entity = new EntityData(EntityEditRig.Trigger);

        List<EntityProblem> problems = rig.Host.FindEntityProblems();

        problems.Count.ShouldBe(2);
        problems[0].EntityId.ShouldBe(first.Id);
        problems[1].EntityId.ShouldBe(second.Id);
    }

    [Fact]
    public void A_scene_with_no_catalogue_reports_nothing()
    {
        var rig = new EntityEditRig();
        SceneNode door = rig.AddBlock("Door");
        door.Entity = new EntityData(EntityEditRig.Door);
        rig.Scene.EntitySchemas = null;

        rig.Host.FindEntityProblems().ShouldBeEmpty();
    }
}
