using System.Numerics;
using SpectraEngine.Core.Scene;
using SpectraEngine.Core.Scripting;

namespace SpectraEngine.Scripting.Tests;

/// <summary>
/// A script that fails or hangs in a level: what the sink is told, and that
/// the level and its other scripts go on.
/// </summary>
public sealed class ScriptFailureTests
{
    private const string Beats = """
        game:GetService("RunService").Heartbeat:Connect(function() print('beat') end)
        """;

    [Fact]
    public void An_error_in_one_script_arrives_with_file_and_line_and_the_others_keep_running()
    {
        using var level = new ScriptLevel();
        SceneNode broken = level.AddScript(level.Scene.Root, """
            local function open(door)
                return door.angle
            end

            print('before')
            open(nil)
            print('after')
            """);
        level.AddScript(level.Scene.Root, Beats);

        level.Play().Tick(3);

        string path = broken.Script.ShouldNotBeNull().Path;
        ScriptMessage failure = level.Sink.Messages.Single(message => message.Severity == ScriptSeverity.Error);
        failure.Script.ShouldBe(path);
        failure.Line.ShouldBe(2);
        failure.Text.ShouldBe("attempt to index nil with 'angle'");
        failure.Stack.ShouldBe([new ScriptFrame(path, 2, "open"), new ScriptFrame(path, 6, null)]);
        level.Printed.ShouldBe(["before", "beat", "beat", "beat"]);
    }

    [Fact]
    public void An_error_in_a_handler_ends_that_call_and_the_handler_is_called_again()
    {
        using var level = new ScriptLevel();
        level.AddScript(level.Scene.Root, """
            local beats = 0
            game:GetService("RunService").Heartbeat:Connect(function()
                beats += 1
                error('beat ' .. beats)
            end)
            """);

        level.Play().Tick(3);

        level.Errors.ShouldBe(["beat 1", "beat 2", "beat 3"]);
    }

    [Fact]
    public void An_endless_loop_is_stopped_and_its_Heartbeat_handler_never_runs_again()
    {
        using var level = new ScriptLevel();
        SceneNode endless = level.AddScript(level.Scene.Root, """
            game:GetService("RunService").Heartbeat:Connect(function() print('endless beat') end)
            task.wait()
            while true do end
            """);
        level.AddScript(level.Scene.Root, Beats);

        level.Play();
        level.Runtime.ShouldNotBeNull().CallLimit = TimeSpan.FromMilliseconds(100);
        level.Tick(5);

        ScriptMessage stopped = level.Sink.Messages.Single(message => message.Severity == ScriptSeverity.Error);
        stopped.Position.ShouldBe($"{endless.Script.ShouldNotBeNull().Path}:3");
        stopped.Text.ShouldContain("switched off");

        // Its handler ran on the first tick, before the loop began on the second.
        level.Printed.Count(line => line == "endless beat").ShouldBe(1);
        level.Printed.Count(line => line == "beat").ShouldBe(5);
    }

    [Fact]
    public void A_thread_of_a_script_that_was_switched_off_is_resumed_by_nothing()
    {
        using var level = new ScriptLevel();
        level.AddScript(level.Scene.Root, """
            task.spawn(function()
                task.wait(0.1)
                print('woke')
            end)
            task.delay(0.1, function() print('delayed') end)
            game:GetService("RunService").Stepped:Once(function() print('stepped') end)
            task.wait()
            while true do end
            """);

        level.Play();
        level.Runtime.ShouldNotBeNull().CallLimit = TimeSpan.FromMilliseconds(100);
        level.Tick(12);

        level.Printed.ShouldBe(["stepped"]);
        level.Errors.Length.ShouldBe(1);
    }

    [Fact]
    public void A_write_to_a_node_with_a_world_brush_is_an_error_that_says_why()
    {
        using var level = new ScriptLevel();
        Vector3 authored = level.Baseplate.LocalPosition;
        level.AddScript(level.Baseplate, """
            script.Parent.Position = Vector3.new(0, 5, 0)
            print('not reached')
            """);

        level.Play().Tick();

        level.Errors.ShouldBe(
        [
            "Baseplate cannot be moved: a world brush sits at or below it, and only parts can move " +
            "while a level plays. Convert the brush to a part.",
        ]);
        level.Sink.Messages.Single().Line.ShouldBe(1);
        level.Baseplate.LocalPosition.ShouldBe(authored);
        level.Printed.ShouldBeEmpty();
    }

    [Fact]
    public void A_position_that_is_no_number_is_refused_and_the_part_stays()
    {
        using var level = new ScriptLevel();
        SceneNode part = level.Part("Door", new Vector3(1f, 2f, 3f));
        level.AddScript(part, """
            local door = script.Parent
            print(pcall(function() door.Position = Vector3.new(0 / 0, 0, 0) end))
            print(pcall(function() door.CFrame = CFrame.new(0, math.huge, 0) end))
            print(pcall(function() door.LocalPosition = Vector3.new(0, 0, -math.huge) end))
            """);

        level.Play().Tick();

        level.Printed.Length.ShouldBe(3);
        level.Printed.ShouldAllBe(line => line.StartsWith("false ") && line.Contains("Door cannot be moved"));
        part.LocalPosition.ShouldBe(new Vector3(1f, 2f, 3f));
    }

    [Fact]
    public void A_member_that_is_not_there_is_an_error_on_a_read_and_on_a_write()
    {
        using var level = new ScriptLevel();
        level.AddScript(level.Part("Door", Vector3.Zero), """
            local door = script.Parent
            print(pcall(function() return door.Anchored end))
            print(pcall(function() door.Anchored = true end))
            print(pcall(function() return door[1] end))
            print(door.Anchored)
            """);

        level.Play().Tick();

        string here = level.Scene.ScriptNodes[0].Script.ShouldNotBeNull().Path;
        level.Printed.ShouldBe(
        [
            $"false {here}:2: Anchored is not a valid member of Part \"Door\"",
            $"false {here}:3: Anchored is not a valid member of Part \"Door\"",
            $"false {here}:4: a member is named by a string, not by a number",
        ]);
        level.Errors.ShouldBe(["Anchored is not a valid member of Part \"Door\""]);
    }

    [Fact]
    public void A_property_handed_the_wrong_kind_of_value_says_what_it_wanted()
    {
        using var level = new ScriptLevel();
        SceneNode part = level.Part("Door", new Vector3(1f, 2f, 3f));
        level.AddScript(part, """
            local door = script.Parent
            print(pcall(function() door.Position = 5 end))
            print(pcall(function() door.CFrame = Vector3.new(1, 2, 3) end))
            print(pcall(function() door.Name = 'Gate' end))
            print(pcall(function() door.ClassName = 'Gate' end))
            print(pcall(function() door.Parent = workspace end))
            """);

        level.Play().Tick();

        string here = level.Scene.ScriptNodes[0].Script.ShouldNotBeNull().Path;
        level.Printed.ShouldBe(
        [
            $"false {here}:2: Unable to assign property Position. Vector3 expected, got number",
            $"false {here}:3: Unable to assign property CFrame. CFrame expected, got Vector3",
            $"false {here}:4: Name cannot be set from a script yet",
            $"false {here}:5: Unable to assign property ClassName. Property is read only",
            $"false {here}:6: Parent cannot be set from a script yet",
        ]);
        part.LocalPosition.ShouldBe(new Vector3(1f, 2f, 3f));
        part.Name.ShouldBe("Door");
    }
}
