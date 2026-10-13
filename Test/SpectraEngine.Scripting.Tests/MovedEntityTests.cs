using System.Numerics;
using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Scene;
using SpectraEngine.Entities;

namespace SpectraEngine.Scripting.Tests;

/// <summary>
/// An entity whose node a script puts under another node of the level runs
/// on. What it read when it spawned follows the change: a door goes on
/// from where it stands, and a trigger senses with the brushes it has now.
/// </summary>
public sealed class MovedEntityTests
{
    private const float Trip = 2f;

    [Fact]
    public void A_door_a_script_put_under_another_node_opens_from_where_it_stands()
    {
        using var level = new ScriptLevel();
        SceneNode door = DoorUnderTheRoot(level);
        Vector3 stood = door.WorldPosition;
        level.AddScript(level.Scene.Root, """
            local door = workspace.Door
            door.Parent = workspace.Shelf
            task.wait()
            door:Open()
            """, "Main");

        level.Play().Tick(40);

        level.Errors.ShouldBeEmpty();
        ReferenceEquals(door.Parent, Shelf(level)).ShouldBeTrue();
        Running(level).IsFullyOpen.ShouldBeTrue();
        Vector3.Distance(door.WorldPosition, stood + new Vector3(Trip, 0f, 0f)).ShouldBeLessThan(1e-3f);
    }

    [Fact]
    public void A_door_a_script_put_under_another_node_on_its_way_arrives_where_it_was_going()
    {
        using var level = new ScriptLevel();
        SceneNode door = DoorUnderTheRoot(level);
        Vector3 stood = door.WorldPosition;
        level.AddScript(level.Scene.Root, """
            local door = workspace.Door
            door:Open()
            task.wait(0.1)
            local at = door.Position
            door.Parent = workspace.Shelf
            print((door.Position - at).Magnitude < 1e-4)
            """, "Main");

        level.Play().Tick(60);

        level.Errors.ShouldBeEmpty();
        level.Printed.ShouldBe(["true"]);
        Running(level).IsFullyOpen.ShouldBeTrue();
        Vector3.Distance(door.WorldPosition, stood + new Vector3(Trip, 0f, 0f)).ShouldBeLessThan(1e-3f);
    }

    [Fact]
    public void Stop_puts_a_door_back_that_opened_under_another_node()
    {
        using var level = new ScriptLevel();
        DoorUnderTheRoot(level);
        level.AddScript(level.Scene.Root, """
            workspace.Door.Parent = workspace.Shelf
            task.wait()
            workspace.Shelf.Door:Open()
            """, "Main");
        var before = new LevelState(level.Scene);

        level.Play().Tick(40);
        level.Stop();

        level.Errors.ShouldBeEmpty();
        new LevelState(level.Scene).ShouldBe(before, "after the door moved");
    }

    [Fact]
    public void A_part_a_script_puts_under_a_trigger_sets_it_off()
    {
        using var level = new ScriptLevel();
        Zone(level);
        level.AddScript(level.Scene.Root, """
            task.wait(0.1)
            local pad = Instance.new("Part")
            pad.CanCollide = false
            pad.Position = Vector3.new(0, 1, 0)
            pad.Parent = workspace.Zone
            """, "Main");

        level.Play().Tick(5);
        level.Printed.ShouldBeEmpty();
        level.Tick(10);

        level.Errors.ShouldBeEmpty();
        level.Printed.ShouldBe(["Relay got Ping ''"]);
    }

    [Fact]
    public void A_part_a_script_takes_from_under_a_trigger_no_longer_sets_it_off()
    {
        using var level = new ScriptLevel();
        // Under the trigger and around the player, who starts at the origin.
        SceneNode zone = Zone(level);
        TouchParts.Box(level, "Pad", new Vector3(0f, 1f, 0f) - zone.LocalPosition, new Vector3(1f), isSolid: false, zone);
        level.AddScript(level.Scene.Root, """workspace.Zone.Pad.Parent = workspace""", "Main");

        level.Play().Tick(10);

        level.Errors.ShouldBeEmpty();
        level.Printed.ShouldBeEmpty();
    }

    // A door that slides along its own X, and a turned node away from it.
    private static SceneNode DoorUnderTheRoot(ScriptLevel level)
    {
        SceneNode shelf = level.Scene.Root.CreateChild("Shelf");
        shelf.LocalPosition = new Vector3(-30f, 5f, 8f);
        shelf.LocalRotation = Quaternion.CreateFromYawPitchRoll(1.2f, 0.4f, 0f);

        var data = new EntityData("func_door");
        data.SetValue("movedir", "1 0 0");
        data.SetValue("distance", "2");
        data.SetValue("speed", "6");
        data.SetValue("wait", "-1");
        SceneNode door = level.Part("Door", new Vector3(20f, 1f, 20f));
        door.Entity = data;
        return door;
    }

    private static SceneNode Shelf(ScriptLevel level) =>
        level.Scene.Root.Children.Single(node => node.Name == "Shelf");

    private static FuncDoor Running(ScriptLevel level) => level.Entities.Entities.OfType<FuncDoor>().Single();

    // A trigger far from the player with a brush of its own, wired to an
    // entity that says so when the trigger is set off.
    private static SceneNode Zone(ScriptLevel level)
    {
        level.Scene.Root.CreateChild("Relay").Entity = new EntityData(EchoEntity.Class);

        var data = new EntityData("trigger_multiple");
        data.Connections.Add(new EntityConnection("OnStartTouch", "Relay", "Ping", "", 0f, EntityConnection.Infinite));
        SceneNode zone = TouchParts.Box(level, "Zone", new Vector3(50f, 1f, 50f), new Vector3(1f), isSolid: false);
        zone.Entity = data;
        return zone;
    }
}
