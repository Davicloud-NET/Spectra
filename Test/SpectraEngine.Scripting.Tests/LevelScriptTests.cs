using System.Numerics;
using SpectraEngine.Core.Scene;
using SpectraEngine.Core.Scripting;
using SpectraEngine.Scripting.Luau.Runtime;

namespace SpectraEngine.Scripting.Tests;

/// <summary>
/// Scripts on the nodes of a level that is played: which of them start, in
/// what order, and what Stop leaves behind.
/// </summary>
public sealed class LevelScriptTests
{
    private const string Spin = """
        local part = script.Parent
        game:GetService("RunService").Heartbeat:Connect(function(step)
            part.CFrame = part.CFrame * CFrame.Angles(0, step, 0)
        end)
        """;

    [Fact]
    public void A_part_turns_under_a_Heartbeat_script_and_is_back_after_Stop()
    {
        using var level = new ScriptLevel();
        SceneNode part = level.Part("Spinner", new Vector3(3f, 2f, 1f));
        part.LocalRotation = Quaternion.CreateFromYawPitchRoll(0.3f, 0.2f, 0.1f);
        Transform authored = part.LocalTransform;
        level.AddScript(part, Spin);

        level.Play().Tick(30);

        CFrame turned = new CFrame(authored.Position, authored.Rotation) * CFrame.Angles(0f, 30 * ScriptLevel.Dt, 0f);
        part.CFrame.FuzzyEq(turned, 1e-4f).ShouldBeTrue($"the part is at {part.CFrame}");
        part.LocalPosition.ShouldBe(authored.Position);
        level.Errors.ShouldBeEmpty();

        level.Stop();

        part.LocalTransform.Position.ShouldBe(authored.Position);
        part.LocalTransform.Rotation.ShouldBe(authored.Rotation);
        part.LocalTransform.Scale.ShouldBe(authored.Scale);
    }

    [Fact]
    public void Scripts_start_at_the_head_of_the_first_tick_and_not_when_the_level_starts()
    {
        using var level = new ScriptLevel();
        level.AddScript(level.Scene.Root, "print('started')");

        level.Play();
        level.Printed.ShouldBeEmpty();

        level.Tick();
        level.Printed.ShouldBe(["started"]);

        level.Tick(3);
        level.Printed.ShouldBe(["started"]);
    }

    [Fact]
    public void Scripts_start_in_the_order_of_the_scene_tree()
    {
        using var level = new ScriptLevel();

        // Attached last and placed first, so the order is the tree's and not
        // the order the nodes were made in.
        SceneNode second = level.Part("Second", Vector3.Zero);
        level.AddScript(second, "print('second')");
        level.AddScript(second.CreateChild("Inner"), "print('second inner')");
        level.AddScript(level.Scene.Root, "print('last')");

        var first = new SceneNode("First");
        level.Scene.Root.InsertChild(0, first);
        level.AddScript(first, "print('first')");

        level.Play().Tick();

        level.Printed.ShouldBe(["first", "second", "second inner", "last"]);
    }

    [Fact]
    public void A_disabled_script_and_a_module_do_not_start()
    {
        using var level = new ScriptLevel();
        SceneNode off = level.AddScript(level.Scene.Root, "print('disabled')");
        off.Script = off.Script.ShouldNotBeNull() with { IsEnabled = false };
        SceneNode module = level.AddScript(level.Scene.Root, "print('module')");
        module.Script = module.Script.ShouldNotBeNull() with { IsModule = true };
        level.AddScript(level.Scene.Root, "print('script')");

        level.Play().Tick(2);

        level.Printed.ShouldBe(["script"]);
    }

    [Fact]
    public void A_script_file_that_is_missing_names_its_node_and_the_other_scripts_run()
    {
        using var level = new ScriptLevel();
        SceneNode lamp = level.Scene.Root.CreateChild("Lamp");
        lamp.Script = new Script("Scripts/nowhere.luau");
        level.AddScript(level.Scene.Root, "print('here')");

        level.Play().Tick();

        ScriptMessage missing = level.Sink.Messages.Single(message => message.Severity == ScriptSeverity.Error);
        missing.Script.ShouldBe("Scripts/nowhere.luau");
        missing.Text.ShouldContain("'Lamp'");
        missing.ProblemNode.ShouldBe(lamp.Id);
        level.Printed.ShouldBe(["here"]);
    }

    [Fact]
    public void A_script_that_does_not_compile_is_one_message_with_its_line_and_its_node()
    {
        using var level = new ScriptLevel();
        SceneNode broken = level.AddScript(level.Part("Door", Vector3.Zero), "local a = 1\nlocal = 2", "Broken");
        level.AddScript(level.Scene.Root, "print('here')");

        level.Play().Tick();

        ScriptMessage mistake = level.Sink.Messages.Single(message => message.Severity == ScriptSeverity.Error);
        mistake.Script.ShouldBe(broken.Script.ShouldNotBeNull().Path);
        mistake.Line.ShouldBe(2);
        mistake.Text.ShouldContain("'Broken'");
        mistake.ProblemNode.ShouldBe(broken.Id);
        level.Printed.ShouldBe(["here"]);
    }

    [Fact]
    public void An_error_in_a_script_that_started_is_no_standing_problem_of_its_node()
    {
        using var level = new ScriptLevel();
        level.AddScript(level.Scene.Root, "print('one')\nerror('two')");

        level.Play().Tick();

        ScriptMessage failure = level.Sink.Messages.Single(message => message.Severity == ScriptSeverity.Error);
        failure.ProblemNode.ShouldBe(Guid.Empty);
        level.Sink.Messages.ShouldAllBe(message => message.ProblemNode == Guid.Empty);
    }

    [Fact]
    public void A_script_saved_with_a_byte_order_mark_runs_and_keeps_its_line_numbers()
    {
        using var level = new ScriptLevel();
        level.Files.AddWithMark("Scripts/marked.luau", "print('one')\nerror('two')");
        level.Scene.Root.CreateChild("Marked").Script = new Script("Scripts/marked.luau");

        level.Play().Tick();

        level.Printed.ShouldBe(["one"]);
        ScriptMessage failure = level.Sink.Messages.Single(message => message.Severity == ScriptSeverity.Error);
        failure.Position.ShouldBe("Scripts/marked.luau:2");
    }

    [Fact]
    public void A_second_Play_starts_fresh()
    {
        using var level = new ScriptLevel();
        level.AddScript(level.Scene.Root, """
            count = (count or 0) + 1
            print(count)
            task.wait(10)
            print('late')
            """);

        level.Play().Tick(5);
        level.Stop();
        level.Play().Tick(5);

        level.Printed.ShouldBe(["1", "1"]);
        level.Runtimes.Count.ShouldBe(2);
        level.Runtimes[0].IsClosed.ShouldBeTrue();
        level.Runtimes[1].IsClosed.ShouldBeFalse();
    }

    [Fact]
    public void Stop_ends_every_thread_and_nothing_runs_after_it()
    {
        using var level = new ScriptLevel();
        level.AddScript(level.Scene.Root, """
            game:GetService("RunService").Heartbeat:Connect(function() print('beat') end)
            task.wait(0.05)
            print('woke')
            """);

        level.Play().Tick(2);
        LuauScriptRuntime runtime = level.Runtime.ShouldNotBeNull();
        level.Stop();

        // A stopped level has no entity world to tick, so the runtime is
        // called here the way a tick would call it.
        runtime.IsClosed.ShouldBeTrue();
        runtime.BeforeSimulation();
        runtime.AfterSimulation();

        level.Printed.ShouldBe(["beat", "beat"]);
        level.Errors.ShouldBeEmpty();
    }

    [Fact]
    public void A_level_with_no_script_gets_no_runtime()
    {
        using var level = new ScriptLevel();

        level.Play().Tick();

        level.Runtime.ShouldBeNull();
        level.Entities.Scripts.ShouldBeNull();
    }
}
