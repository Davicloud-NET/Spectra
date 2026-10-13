using System.Numerics;
using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Scene;
using SpectraEngine.Core.Scripting;

namespace SpectraEngine.Scripting.Tests;

/// <summary>
/// Clone: a copy of a node and all below it, in no level until it gets a
/// Parent. What it copies of a part, a light, an entity, a script and what
/// scripts left on the node, and when a script and an entity below it start.
/// </summary>
public sealed class CloneScriptTests
{
    [Fact]
    public void A_clone_is_another_node_with_the_same_name_place_and_children_in_no_level()
    {
        string[] printed = House.Ask("""
            local door = workspace.House.Door
            local copy = door:Clone()
            print(copy ~= door, copy.Name, copy.ClassName, copy.Parent, copy:IsDescendantOf(game), typeof(copy))
            print(copy.Position == door.Position, copy.CFrame == door.CFrame, #copy:GetChildren())
            print(copy.Hinge ~= door.Hinge, copy.Hinge.Parent == copy, copy.Hinge.Position == door.Hinge.Position)
            print(#workspace.House:GetChildren(), workspace.House:FindFirstChild("Door") == door)
            """);

        printed.ShouldBe(["true Door sayer nil false Instance", "true true 1", "true true true", "4 true"]);
    }

    [Fact]
    public void A_clone_of_a_node_under_a_turned_parent_stands_where_its_source_stands_once_it_is_in_the_level()
    {
        using var level = new ScriptLevel();
        SceneNode group = level.Scene.Root.CreateChild("Group");
        group.LocalPosition = new Vector3(4f, 0f, -2f);
        group.LocalRotation = Quaternion.CreateFromYawPitchRoll(0.7f, 0.2f, 0f);
        SceneNode crate = level.Part("Crate", new Vector3(1f, 0.5f, 2f), group);
        level.AddScript(level.Scene.Root, """
            local crate = workspace.Group.Crate
            local copy = crate:Clone()
            print((copy.Position - crate.Position).Magnitude < 1e-4, copy.CFrame:FuzzyEq(crate.CFrame, 1e-4))
            copy.Name = "Copy"
            copy.Parent = workspace
            print((copy.Position - crate.Position).Magnitude < 1e-4, copy.CFrame:FuzzyEq(crate.CFrame, 1e-4))
            copy.Parent = workspace.Group
            print((copy.LocalPosition - crate.LocalPosition).Magnitude < 1e-4)
            """);

        level.Play().Tick();

        level.Errors.ShouldBeEmpty();
        level.Printed.ShouldBe(["true true", "true true", "true"]);
        group.Children.Select(node => node.Name).ShouldBe(["Crate", "Copy"]);
        group.Children[1].Brush.ShouldNotBeSameAs(crate.Brush);
    }

    [Fact]
    public void A_clone_has_the_tags_and_attributes_scripts_left_on_its_source_and_they_are_its_own()
    {
        string[] printed = House.Ask("""
            local CollectionService = game:GetService("CollectionService")
            local lamp = workspace.House.Lamp
            lamp:AddTag("Bright")
            lamp.Shade:AddTag("Bright")
            lamp:SetAttribute("Watts", 60)
            CollectionService:GetInstanceAddedSignal("Bright"):Connect(function(node) print("added", node:GetFullName()) end)

            local copy = lamp:Clone()
            print(copy:HasTag("Bright"), copy.Shade:HasTag("Bright"), copy:GetAttribute("Watts"), #CollectionService:GetTagged("Bright"))
            copy:SetAttribute("Watts", 100)
            copy.Shade:RemoveTag("Bright")
            print(lamp:GetAttribute("Watts"), copy:GetAttribute("Watts"), lamp.Shade:HasTag("Bright"))
            copy.Name = "Spare"
            copy.Parent = workspace
            print(#CollectionService:GetTagged("Bright"))
            """);

        // The signal tells of the copy when it comes into the level.
        printed.ShouldBe(["true true 60 2", "60 100 true", "3", "added Root.Spare"]);
    }

    [Fact]
    public void A_clone_of_a_light_shines_as_its_source_does_and_is_changed_alone()
    {
        string[] printed = House.Ask("""
            local lamp = workspace.House.Lamp
            lamp.Brightness = 3
            lamp.Color = Color3.new(1, 0.5, 0.25)
            local copy = lamp:Clone()
            print(copy.ClassName, copy.Brightness, copy.Color, copy.Range == lamp.Range, copy.Shade.ClassName)
            copy.Brightness = 7
            copy.Enabled = false
            print(lamp.Brightness, lamp.Enabled, copy.Brightness, copy.Enabled)
            """);

        printed.ShouldBe(["PointLight 3 1, 0.5, 0.25 true SpotLight", "3 true 7 false"]);
    }

    [Fact]
    public void A_script_under_a_clone_starts_at_the_end_of_the_tick_the_clone_comes_into_the_level()
    {
        using var level = new ScriptLevel();
        SceneNode crate = level.Part("Crate", new Vector3(0f, 2f, 5f));
        level.AddScript(crate, """print("hello from", script.Parent.Name, script.Parent.Parent)""", "Hello");
        level.AddScript(level.Scene.Root, """
            local copy = workspace.Crate:Clone()
            copy.Name = "Copy"
            task.wait(0.1)
            print("parenting")
            copy.Parent = workspace
            print("parented")
            task.wait()
            print("next tick")
            """, "Main");

        level.Play().Tick(5);
        level.Printed.ShouldBe(["hello from Crate Root"]);
        level.Runtime.ShouldNotBeNull().RunningScriptCount.ShouldBe(2);

        level.Tick(3);

        level.Errors.ShouldBeEmpty();
        level.Printed.ShouldBe(["hello from Crate Root", "parenting", "parented", "hello from Copy Root", "next tick"]);
        level.Runtime.RunningScriptCount.ShouldBe(3);
    }

    [Fact]
    public void A_script_under_a_clone_that_never_comes_into_the_level_never_runs()
    {
        using var level = new ScriptLevel();
        level.AddScript(level.Part("Crate", new Vector3(0f, 2f, 5f)), """print("hello")""", "Hello");
        level.AddScript(level.Scene.Root, """
            local kept = workspace.Crate:Clone()
            local gone = workspace.Crate:Clone()
            gone.Parent = workspace
            gone:Destroy()
            task.wait(0.2)
            print(kept.Parent, gone.Parent)
            """, "Main");

        level.Play().Tick(20);

        level.Errors.ShouldBeEmpty();
        level.Printed.ShouldBe(["hello", "nil nil"]);
        level.Runtime.ShouldNotBeNull().RunningScriptCount.ShouldBe(2);
    }

    [Fact]
    public void A_script_that_is_switched_off_or_a_module_does_not_start_under_a_clone()
    {
        using var level = new ScriptLevel();
        SceneNode crate = level.Part("Crate", new Vector3(0f, 2f, 5f));
        SceneNode idle = level.AddScript(crate, """print("idle ran")""", "Idle");
        idle.Script = idle.Script.ShouldNotBeNull() with { IsEnabled = false };
        SceneNode shared = level.AddScript(crate, """print("module ran") return {}""", "Shared");
        shared.Script = shared.Script.ShouldNotBeNull() with { IsModule = true };
        level.AddScript(level.Scene.Root, """workspace.Crate:Clone().Parent = workspace""", "Main");

        level.Play().Tick(3);

        level.Errors.ShouldBeEmpty();
        level.Printed.ShouldBeEmpty();
    }

    [Fact]
    public void An_entity_under_a_clone_runs_from_the_end_of_the_tick_and_takes_no_input_before()
    {
        using var level = new ScriptLevel();
        level.Scene.Root.CreateChild("Relay").Entity = new EntityData(EchoEntity.Class);
        level.AddScript(level.Scene.Root, """
            local copy = workspace.Relay:Clone()
            copy.Name = "Spare"
            copy.OnPing:Connect(function() print("spare pinged") end)
            copy.Parent = workspace
            print(pcall(function() copy:Ping() end))
            task.wait()
            copy:Ping()
            workspace.Relay:Ping()
            """, "Main");

        level.Play().Tick(3);

        level.Errors.ShouldBeEmpty();
        string script = level.Scene.ScriptNodes[0].Script.ShouldNotBeNull().Path;
        level.Printed.ShouldBe(
        [
            $"false {script}:5: Ping was not sent: the entity of \"Spare\" is not running",
            "Spare got Ping ''", "Relay got Ping ''", "spare pinged",
        ]);
        level.Entities.Entities.Count.ShouldBe(2);
    }

    [Fact]
    public void A_reference_to_a_node_inside_the_clone_points_at_its_copy()
    {
        using var level = new ScriptLevel();
        SceneNode stand = level.Scene.Root.CreateChild("Stand");
        SceneNode partner = level.Part("Partner", new Vector3(3f, 1f, 3f), stand);
        SceneNode outside = level.Part("Outside", new Vector3(6f, 1f, 3f));
        Sign(stand, "Inner", partner);
        Sign(stand, "Outer", outside);
        level.AddScript(level.Scene.Root, """
            local stand = workspace.Stand
            local copy = stand:Clone()
            copy.Name = "Spare"
            copy.Parent = workspace
            print(copy.Inner:GetAttribute("partner") == copy.Partner, stand.Inner:GetAttribute("partner") == stand.Partner)
            print(copy.Outer:GetAttribute("partner") == workspace.Outside)
            """, "Main");

        level.Play().Tick();

        level.Errors.ShouldBeEmpty();
        level.Printed.ShouldBe(["true true", "true"]);
    }

    [Fact]
    public void A_world_brush_and_the_root_are_not_cloned()
    {
        using ScriptLevel level = House.With("""
            print(pcall(function() return workspace.Baseplate:Clone() end))
            print(pcall(function() return workspace:Clone() end))
            """);

        level.Play().Tick();

        string script = level.Scene.ScriptNodes.Single(node => node.Name == House.ScriptName).Script.ShouldNotBeNull().Path;
        level.Printed.ShouldBe(
        [
            $"false {script}:1: Baseplate cannot be cloned: a world brush sits at or below it, and only parts can " +
            "be made while a level plays. Convert the brush to a part.",
            $"false {script}:2: Root is the root of the level and cannot be cloned",
        ]);
        new ScriptWrites(level.Entities).Tree.MadeCount.ShouldBe(0);
    }

    [Fact]
    public void Stop_takes_every_clone_away_and_ends_the_scripts_that_came_with_them()
    {
        using var level = new ScriptLevel();
        SceneNode crate = level.Part("Crate", new Vector3(0f, 2f, 5f));
        level.AddScript(crate, """while true do task.wait() end""", "Idler");
        level.AddScript(level.Scene.Root, """
            for i = 1, 4 do
                workspace.Crate:Clone().Parent = workspace
            end
            """, "Main");
        var before = new LevelState(level.Scene);

        level.Play().Tick(3);
        level.Runtime.ShouldNotBeNull().RunningScriptCount.ShouldBe(6);
        level.Stop();

        level.Errors.ShouldBeEmpty();
        new LevelState(level.Scene).ShouldBe(before, "after Clone");
    }

    private static void Sign(SceneNode parent, string name, SceneNode partner)
    {
        var data = new EntityData(SignEntity.Class);
        data.SetValue("partner", KeyvalueWire.Format(partner.Id));
        parent.CreateChild(name).Entity = data;
    }
}
