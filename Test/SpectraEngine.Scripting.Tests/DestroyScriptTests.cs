using System.Numerics;
using SpectraEngine.Core.Scene;
using SpectraEngine.Core.Scripting;
using SpectraEngine.Scripting.Luau.Runtime;

namespace SpectraEngine.Scripting.Tests;

/// <summary>
/// Destroy: the node and all below it leave the level for good. What a
/// script still holds of a destroyed node reads as Roblox's does, and
/// nothing is kept for it: its scripts, its connections, the waits on it,
/// its tags and its attributes all end.
/// </summary>
public sealed class DestroyScriptTests
{
    [Fact]
    public void A_destroyed_node_has_no_parent_and_no_children_and_still_reads_what_it_held()
    {
        string[] printed = House.Ask("""
            local door, hinge = workspace.House.Door, workspace.House.Door.Hinge
            local at = door.Position
            door:Destroy()
            print(door.Parent, hinge.Parent, #door:GetChildren(), door:IsDescendantOf(game), hinge:IsDescendantOf(game))
            print(door.Name, door.ClassName, door.Position == at, door:IsA("BasePart"), door:GetFullName())
            print(workspace.House:FindFirstChild("Door"), #workspace.House:GetChildren())
            door:Destroy()
            hinge:Destroy()
            print("twice is nothing")
            """);

        printed.ShouldBe(
        [
            "nil nil 0 false false",
            "Door sayer true true Door",
            "nil 3",
            "twice is nothing",
        ]);
    }

    [Fact]
    public void The_parent_of_a_destroyed_node_is_locked_and_nothing_goes_under_one()
    {
        using ScriptLevel level = House.With("""
            local door = workspace.House.Door
            door:Destroy()
            print(pcall(function() door.Parent = workspace end))
            print(pcall(function() door.Parent = nil end))
            print(pcall(function() workspace.House.Lamp.Parent = door end))
            door.Name = "Gone"
            print(door.Name)
            """);

        level.Play().Tick();

        string script = level.Scene.ScriptNodes.Single(node => node.Name == House.ScriptName).Script.ShouldNotBeNull().Path;
        level.Printed.ShouldBe(
        [
            $"false {script}:3: The Parent property of Door is locked: Door was destroyed",
            $"false {script}:4: The Parent property of Door is locked: Door was destroyed",
            $"false {script}:5: Lamp cannot get Door as its parent: Door was destroyed",
            "Gone",
        ]);
    }

    [Fact]
    public void A_node_another_script_destroyed_reads_as_destroyed_to_the_script_that_holds_it()
    {
        using ScriptLevel level = House.With("""
            local lamp = workspace.House.Lamp
            task.wait(0.1)
            print(lamp.Name, lamp.Parent, lamp:IsDescendantOf(game), lamp.Brightness, lamp:IsDescendantOf(nil))
            print(pcall(function() lamp.Parent = workspace end))
            """);
        level.AddScript(level.Scene.Root, """workspace.House.Lamp:Destroy()""", "Destroyer");

        level.Play().Tick(8);

        level.Errors.ShouldBeEmpty();
        level.Printed[0].ShouldBe("Lamp nil false 1 true");
        level.Printed[1].ShouldEndWith("The Parent property of Lamp is locked: Lamp was destroyed");
    }

    [Fact]
    public void The_threads_and_connections_of_a_script_under_a_destroyed_node_end()
    {
        using var level = new ScriptLevel();
        SceneNode crate = level.Part("Crate", new Vector3(0f, 2f, 5f));
        level.AddScript(crate, """
            game:GetService("RunService").Heartbeat:Connect(function() print("beat") end)
            task.spawn(function()
                while true do
                    print("loop")
                    task.wait()
                end
            end)
            task.wait(0.05)
            script.Parent:Destroy()
            print("goes on to its next wait")
            task.wait()
            print("never")
            """, "Doomed");

        level.Play().Tick(8);

        level.Errors.ShouldBeEmpty();
        // The thread that destroys was made first, so it is woken first.
        level.Printed.ShouldBe(["loop", "beat", "loop", "beat", "loop", "beat", "goes on to its next wait"]);
        level.Runtime.ShouldNotBeNull().RunningScriptCount.ShouldBe(0);
        level.Runtime.WaitingThreadCount.ShouldBe(0);
    }

    [Fact]
    public void A_connection_to_what_a_destroyed_node_fires_is_ended()
    {
        using var level = new ScriptLevel();
        level.Scene.Root.CreateChild("Relay").Entity = new Core.Entities.EntityData(EchoEntity.Class);
        level.Part("Pad", new Vector3(0f, 2f, 5f));
        level.AddScript(level.Scene.Root, """
            local relay, pad = workspace.Relay, workspace.Pad
            local pinged = relay.OnPing:Connect(function() print("pinged") end)
            local touched = pad.Touched:Connect(function() print("touched") end)
            relay:Ping()
            task.wait()
            print(pinged.Connected, touched.Connected)
            relay:Destroy()
            pad:Destroy()
            print(pinged.Connected, touched.Connected)
            local late = pad.Touched:Connect(function() end)
            print(late.Connected, pcall(function() relay:Ping() end))
            """, "Main");

        level.Play().Tick(3);

        level.Errors.ShouldBeEmpty();
        level.Printed[..4].ShouldBe(["Relay got Ping ''", "pinged", "true true", "false false"]);
        level.Printed[4].ShouldStartWith("true false ");
        level.Printed[4].ShouldEndWith("Ping was not sent: the entity of \"Relay\" is not running");
        level.Entities.Listeners.Count.ShouldBe(0);
        level.Runtime.ShouldNotBeNull().NodeSignalCount.ShouldBe(0);
    }

    [Fact]
    public void A_wait_for_a_child_of_a_destroyed_node_ends_with_nil_at_once_or_with_a_warning()
    {
        using ScriptLevel level = House.With("""
            local lamp = workspace.House.Lamp
            task.spawn(function() print("timed", lamp:WaitForChild("Bulb", 60)) end)
            task.spawn(function() print("untimed", lamp:WaitForChild("Bulb")) end)
            task.wait()
            lamp:Destroy()
            task.wait()
            print("after")
            """);

        level.Play().Tick(4);

        level.Errors.ShouldBeEmpty();
        level.Printed.ShouldBe(["timed nil", "after"]);
        level.Sink.Texts(ScriptSeverity.Warning).ShouldBe(
        [
            "WaitForChild(\"Bulb\") will not return: 'Lamp' was destroyed, so the thread that waited was ended",
        ]);
        level.Runtime.ShouldNotBeNull().WaitingThreadCount.ShouldBe(0);
    }

    [Fact]
    public void The_tags_and_attributes_of_a_destroyed_node_are_gone_and_it_takes_no_new_ones()
    {
        using ScriptLevel level = House.With("""
            local CollectionService = game:GetService("CollectionService")
            local lamp = workspace.House.Lamp
            lamp:AddTag("Bright")
            lamp.Shade:AddTag("Bright")
            lamp:SetAttribute("Watts", 60)
            local changed = lamp.AttributeChanged:Connect(function() end)
            CollectionService:GetInstanceRemovedSignal("Bright"):Connect(function(node) print("removed", node.Name) end)
            lamp:Destroy()
            print(lamp:HasTag("Bright"), lamp:GetAttribute("Watts"), #lamp:GetTags(), #CollectionService:GetTagged("Bright"), changed.Connected)
            print(pcall(function() lamp:AddTag("Late") end))
            print(pcall(function() lamp:SetAttribute("Watts", 100) end))
            print(lamp.AttributeChanged == lamp:GetAttributeChangedSignal("Watts"))
            """);

        level.Play().Tick();

        level.Errors.ShouldBeEmpty();
        level.Printed[0].ShouldBe("false nil 0 0 false");
        level.Printed[1].ShouldEndWith("\"Lamp\" was destroyed, and a destroyed node holds no tag");
        level.Printed[2].ShouldEndWith("\"Lamp\" was destroyed, and a destroyed node holds no attribute");
        level.Printed[3..].ShouldBe(["true", "removed Lamp", "removed Shade"]);
        LuauScriptRuntime runtime = level.Runtime.ShouldNotBeNull();
        runtime.TaggedNodeCount.ShouldBe(0);
        runtime.AttributeNodeCount.ShouldBe(0);
    }

    [Fact]
    public void A_world_brush_and_the_root_are_not_destroyed()
    {
        using ScriptLevel level = House.With("""
            print(pcall(function() workspace.Baseplate:Destroy() end))
            print(pcall(function() workspace:Destroy() end))
            print(workspace.Baseplate.Parent, workspace.Baseplate:IsDescendantOf(game))
            """);

        level.Play().Tick();

        string script = level.Scene.ScriptNodes.Single(node => node.Name == House.ScriptName).Script.ShouldNotBeNull().Path;
        level.Printed.ShouldBe(
        [
            $"false {script}:1: Baseplate cannot be destroyed: a world brush sits at or below it, and only parts " +
            "can be destroyed while a level plays. Convert the brush to a part.",
            $"false {script}:2: Root is the root of the level and cannot be destroyed",
            "Root true",
        ]);
    }

    [Fact]
    public void Stop_brings_back_what_a_script_destroyed_with_all_it_held()
    {
        using ScriptLevel level = House.With("""
            workspace.House.Walls:Destroy()
            workspace.House.Door.Hinge:Destroy()
            workspace.Relay:Destroy()
            workspace.Panel:Destroy()
            """);
        var before = new LevelState(level.Scene);
        int built = level.Scene.NodeCount;

        level.Play().Tick(2);
        level.Scene.NodeCount.ShouldBe(built - 6);
        level.Stop();

        level.Errors.ShouldBeEmpty();
        level.Scene.NodeCount.ShouldBe(built);
        new LevelState(level.Scene).ShouldBe(before, "after Destroy");
    }
}
