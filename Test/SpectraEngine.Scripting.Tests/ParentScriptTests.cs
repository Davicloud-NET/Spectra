using System.Numerics;
using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Scene;

namespace SpectraEngine.Scripting.Tests;

/// <summary>
/// A Parent that can be set: to a node, which moves the node with all below
/// it and keeps it where it is in the world, and to nil, which takes it out
/// of the level. And what goes with the node: its scripts, its entities and
/// the waits for a child.
/// </summary>
public sealed class ParentScriptTests
{
    [Fact]
    public void A_node_goes_under_another_as_its_last_child_and_stays_where_it_is_in_the_world()
    {
        using var level = new ScriptLevel();
        SceneNode group = level.Scene.Root.CreateChild("Group");
        group.LocalPosition = new Vector3(4f, 0f, -2f);
        group.LocalRotation = Quaternion.CreateFromYawPitchRoll(0.7f, 0f, 0f);
        level.Part("Shelf", new Vector3(0f, 1f, 0f), group);
        SceneNode key = level.Part("Key", new Vector3(1f, 2f, 3f));
        level.Part("Ring", new Vector3(0f, 0.2f, 0f), key);
        level.AddScript(level.Scene.Root, """
            local key, group = workspace.Key, workspace.Group
            local at, ringAt = key.CFrame, key.Ring.Position
            key.Parent = group
            print(key.Parent, group:GetChildren()[2] == key, workspace:FindFirstChild("Key"), key:GetFullName())
            print(key.CFrame:FuzzyEq(at, 1e-4), (key.Ring.Position - ringAt).Magnitude < 1e-4, key.LocalPosition ~= at.Position)
            key.Parent = group
            print(#group:GetChildren())
            """, "Main");

        level.Play().Tick();

        level.Errors.ShouldBeEmpty();
        level.Printed.ShouldBe(["Group true nil Root.Group.Key", "true true true", "2"]);
        Vector3.Distance(key.WorldPosition, new Vector3(1f, 2f, 3f)).ShouldBeLessThan(1e-4f);
    }

    [Fact]
    public void Nil_takes_a_node_out_of_the_level_and_a_parent_brings_it_back()
    {
        string[] printed = House.Ask("""
            local door = workspace.House.Door
            local at = door.Position
            door.Parent = nil
            print(door.Parent, door:IsDescendantOf(game), door.Hinge.Parent == door, workspace.House:FindFirstChild("Door"))
            print(door.Position == at, door.Name)
            door.Parent = workspace
            print(door.Parent, door:IsDescendantOf(game), door.Position == at, workspace.Door == door)
            """);

        printed.ShouldBe(["nil false true nil", "true Door", "Root true true true"]);
    }

    [Fact]
    public void A_cycle_the_root_and_what_is_no_node_are_refused()
    {
        using ScriptLevel level = House.With("""
            local house = workspace.House
            print(pcall(function() house.Parent = house end))
            print(pcall(function() house.Parent = house.Door.Hinge end))
            print(pcall(function() workspace.Parent = house end))
            print(pcall(function() workspace.Parent = nil end))
            print(pcall(function() house.Parent = game end))
            print(pcall(function() house.Parent = "workspace" end))
            print(pcall(function() workspace.Baseplate.Parent = house end))
            print(house.Parent, workspace.Baseplate.Parent)
            """);

        level.Play().Tick();

        string script = level.Scene.ScriptNodes.Single(node => node.Name == House.ScriptName).Script.ShouldNotBeNull().Path;
        level.Printed.ShouldBe(
        [
            $"false {script}:2: Attempt to set parent of House to House would result in circular reference",
            $"false {script}:3: Attempt to set parent of House to Hinge would result in circular reference",
            $"false {script}:4: The Parent property of Root is locked: it is the root of the level",
            $"false {script}:5: The Parent property of Root is locked: it is the root of the level",
            $"false {script}:6: Unable to assign property Parent. Instance expected, got DataModel",
            $"false {script}:7: Unable to assign property Parent. Instance expected, got string",
            $"false {script}:8: Baseplate cannot get another parent: a world brush sits at or below it, and only " +
            "parts can move while a level plays. Convert the brush to a part.",
            "Root Root",
        ]);
    }

    [Fact]
    public void A_wait_for_a_child_is_over_when_a_script_makes_the_child()
    {
        using ScriptLevel level = House.With("""
            local key = workspace.House:WaitForChild("Key")
            print("got", key, key.Parent)
            """);
        level.AddScript(level.Scene.Root, """
            task.wait(0.1)
            local key = Instance.new("Part")
            key.Name = "Key"
            print("making")
            key.Parent = workspace.House
            print("made")
            """, "Maker");

        level.Play().Tick(10);

        // The thread that waited goes on after the script that made the child.
        level.Errors.ShouldBeEmpty();
        level.Printed.ShouldBe(["making", "made", "got Key House"]);
        level.Runtime.ShouldNotBeNull().WaitingThreadCount.ShouldBe(0);
    }

    [Fact]
    public void A_wait_for_a_child_of_a_node_in_no_level_is_over_when_the_child_gets_that_parent()
    {
        string[] printed = House.Ask("""
            local box = Instance.new("Folder")
            task.spawn(function() print("got", box:WaitForChild("Key").Name) end)
            local key = Instance.new("Part")
            key.Name = "Key"
            key.Parent = box
            print("put in")
            """);

        printed.ShouldBe(["put in", "got Key"]);
    }

    [Fact]
    public void A_script_ends_when_its_node_leaves_the_level_and_starts_afresh_when_it_comes_back()
    {
        using var level = new ScriptLevel();
        SceneNode crate = level.Part("Crate", new Vector3(0f, 2f, 5f));
        level.AddScript(crate, """
            local runs = (script.Parent:GetAttribute("Runs") or 0) + 1
            script.Parent:SetAttribute("Runs", runs)
            while true do
                print("run " .. runs)
                task.wait()
            end
            """, "Counter");
        level.AddScript(level.Scene.Root, """
            local crate = workspace.Crate
            task.wait(0.05)
            crate.Parent = nil
            task.wait(0.05)
            crate.Parent = workspace
            """, "Main");

        level.Play().Tick(9);

        // Out on tick 4 and back on tick 7, where it starts at the end of the tick.
        level.Errors.ShouldBeEmpty();
        level.Printed.ShouldBe(["run 1", "run 1", "run 1", "run 2", "run 2", "run 2"]);
        level.Runtime.ShouldNotBeNull().RunningScriptCount.ShouldBe(2);
    }

    [Fact]
    public void An_entity_stops_when_its_node_leaves_the_level_and_a_new_one_starts_when_it_comes_back()
    {
        using var level = new ScriptLevel();
        level.Scene.Root.CreateChild("Relay").Entity = new EntityData(EchoEntity.Class);
        level.AddScript(level.Scene.Root, """
            local relay = workspace.Relay
            relay.Parent = nil
            print(pcall(function() relay:Ping() end))
            task.wait()
            relay.Parent = workspace
            task.wait()
            relay:Ping()
            """, "Main");

        level.Play();
        Entity first = level.Entities.Entities.Single();
        level.Tick(4);

        level.Errors.ShouldBeEmpty();
        level.Printed.Length.ShouldBe(2);
        level.Printed[0].ShouldEndWith("Ping was not sent: the entity of \"Relay\" is not running");
        level.Printed[1].ShouldBe("Relay got Ping ''");
        level.Entities.Entities.Single().ShouldNotBeSameAs(first);
    }

    [Fact]
    public void Stop_puts_every_node_back_under_the_parent_it_was_built_with()
    {
        using ScriptLevel level = House.With("""
            local house = workspace.House
            house.Lamp.Parent = house.Door
            house.Door.Parent = workspace.Panel
            house.Walls.North.Parent = nil
            house.Sign.Parent = workspace
            workspace.Relay.Parent = house
            house.Parent = Instance.new("Folder", workspace)
            """);
        var before = new LevelState(level.Scene);

        level.Play().Tick(2);
        new LevelState(level.Scene).IsSavedAs(before).ShouldBeFalse();
        level.Stop();

        level.Errors.ShouldBeEmpty();
        new LevelState(level.Scene).ShouldBe(before, "after the parents were set");
    }
}
