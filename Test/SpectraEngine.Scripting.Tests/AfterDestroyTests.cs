using System.Numerics;
using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Scene;
using SpectraEngine.Core.Scripting;
using SpectraEngine.Scripting.Luau.Runtime;

namespace SpectraEngine.Scripting.Tests;

/// <summary>
/// What a script still holds once a node is destroyed: a node that was below
/// it, a signal of it, and the node itself to wait on. Each behaves the
/// same whether the script took it before the node was destroyed or after.
/// </summary>
public sealed class AfterDestroyTests
{
    [Fact]
    public void A_node_below_a_destroyed_node_reads_the_place_it_stood_at()
    {
        using var level = new ScriptLevel();
        SceneNode group = level.Scene.Root.CreateChild("Group");
        group.LocalPosition = new Vector3(4f, 0f, -2f);
        group.LocalRotation = Quaternion.CreateFromYawPitchRoll(0.7f, 0.2f, 0f);
        SceneNode crate = level.Part("Crate", new Vector3(1f, 0.5f, 2f), group);
        level.Part("Lid", new Vector3(0f, 0.5f, 0.25f), crate);
        level.AddScript(level.Scene.Root, """
            local crate, lid = workspace.Group.Crate, workspace.Group.Crate.Lid
            local at, frame = lid.Position, lid.CFrame
            workspace.Group:Destroy()
            print(lid.Parent, (lid.Position - at).Magnitude < 1e-4, lid.CFrame:FuzzyEq(frame, 1e-4))
            print((crate.Position - lid.Position).Magnitude > 0.1)
            """, "Main");
        var before = new LevelState(level.Scene);

        level.Play().Tick();

        level.Errors.ShouldBeEmpty();
        level.Printed.ShouldBe(["nil true true", "true"]);

        level.Stop();

        new LevelState(level.Scene).ShouldBe(before, "after Destroy");
    }

    [Fact]
    public void A_wait_for_a_child_that_begins_after_the_node_was_destroyed_ends_as_one_that_began_before()
    {
        using ScriptLevel level = House.With("""
            local lamp = workspace.House.Lamp
            lamp:Destroy()
            task.spawn(function() print("timed", lamp:WaitForChild("Bulb", 60)) end)
            task.spawn(function() print("untimed", lamp:WaitForChild("Bulb")) end)
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
    public void A_signal_a_script_kept_of_a_destroyed_node_takes_a_function_and_never_calls_it()
    {
        using ScriptLevel level = Kept("""
            local late = touched:Connect(function() print("never") end)
            local other = pinged:Connect(function() print("never") end)
            print(late.Connected, other.Connected)
            """);

        level.Play().Tick(3);

        level.Errors.ShouldBeEmpty();
        level.Printed.ShouldBe(["true true"]);
        level.Entities.Listeners.Count.ShouldBe(0);
        LuauScriptRuntime runtime = level.Runtime.ShouldNotBeNull();
        runtime.NodeSignalCount.ShouldBe(0);
    }

    [Fact]
    public void A_wait_on_a_signal_of_a_destroyed_node_is_an_error_since_nothing_would_resume_it()
    {
        using ScriptLevel level = Kept("""
            print(pcall(function() touched:Wait() end))
            print(pcall(function() pinged:Wait() end))
            print(pcall(function() pad.Touched:Wait() end))
            """);

        level.Play().Tick(3);

        level.Errors.ShouldBeEmpty();
        string script = level.Scene.ScriptNodes.Single().Script.ShouldNotBeNull().Path;
        level.Printed.ShouldBe(
        [
            $"false {script}:5: Touched:Wait() will not return: 'Pad' was destroyed",
            $"false {script}:6: OnPing:Wait() will not return: 'Relay' was destroyed",
            $"false {script}:7: Touched:Wait() will not return: 'Pad' was destroyed",
        ]);
        level.Runtime.ShouldNotBeNull().WaitingThreadCount.ShouldBe(0);
    }

    // A level whose script keeps a signal of an entity and one of a part,
    // destroys both nodes and then runs the lines it is handed.
    private static ScriptLevel Kept(string lines)
    {
        var level = new ScriptLevel();
        level.Scene.Root.CreateChild("Relay").Entity = new EntityData(EchoEntity.Class);
        level.Part("Pad", new Vector3(0f, 2f, 5f));
        level.AddScript(level.Scene.Root, """
            local relay, pad = workspace.Relay, workspace.Pad
            local pinged, touched = relay.OnPing, pad.Touched
            relay:Destroy()
            pad:Destroy()

            """ + lines, "Main");
        return level;
    }
}
