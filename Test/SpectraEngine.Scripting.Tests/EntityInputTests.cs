using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Scene;

namespace SpectraEngine.Scripting.Tests;

/// <summary>
/// An input of an entity class as a method of its node: what a script hands
/// it becomes the text a wire carries, the input is queued and not run, and
/// what the class does not take is an error at the script's line.
/// </summary>
public sealed class EntityInputTests
{
    [Fact]
    public void A_call_queues_the_input_and_the_entity_has_not_got_it_on_the_next_line()
    {
        using var level = new ScriptLevel();
        Echo(level);
        level.AddScript(level.Scene.Root, """
            workspace.Echo:Ping()
            print('called')
            """);

        level.Play().Tick();

        level.Errors.ShouldBeEmpty();
        level.Printed.ShouldBe(["called", "Echo got Ping ''"]);
    }

    [Theory]
    [InlineData("SayFlag(true)", "SayFlag '1'")]
    [InlineData("SayFlag(false)", "SayFlag '0'")]
    [InlineData("SayWhole(-3)", "SayWhole '-3'")]
    [InlineData("SayNumber(2.5)", "SayNumber '2.5'")]
    [InlineData("SayNumber(0.1)", "SayNumber '0.1'")]
    [InlineData("SayNumber('7')", "SayNumber '7'")]
    [InlineData("SayText('hello there')", "SayText 'hello there'")]
    [InlineData("SayText('')", "SayText ''")]
    [InlineData("SayName('door1')", "SayName 'door1'")]
    [InlineData("SayPair(1, 2.5)", "SayPair '1 2.5'")]
    [InlineData("SayPoint(Vector3.new(1, 2, 3))", "SayPoint '1 2 3'")]
    [InlineData("SayFour(1, 2, 3, 4)", "SayFour '1 2 3 4'")]
    [InlineData("SayColor(Vector3.new(1, 0.5, 0))", "SayColor '1 0.5 0'")]
    [InlineData("SayAngles(Vector3.new(0, 90, 0))", "SayAngles '0 90 0'")]
    [InlineData("SayNode(nil)", "SayNode ''")]
    [InlineData("SayNode()", "SayNode ''")]
    [InlineData("SayMaybe(4)", "SayMaybe '4'")]
    [InlineData("SayMaybe()", "SayMaybe ''")]
    [InlineData("SayMaybe(nil)", "SayMaybe ''")]
    [InlineData("SayAny()", "SayAny ''")]
    [InlineData("SayAny(true)", "SayAny '1'")]
    [InlineData("SayAny(12)", "SayAny '12'")]
    [InlineData("SayAny(1.5)", "SayAny '1.5'")]
    [InlineData("SayAny('as typed')", "SayAny 'as typed'")]
    [InlineData("SayAny(Vector3.new(4, 5, 6))", "SayAny '4 5 6'")]
    public void A_value_of_each_type_reaches_the_entity_as_the_text_a_wire_carries(string call, string got)
    {
        using var level = new ScriptLevel();
        Echo(level);
        level.AddScript(level.Scene.Root, $"workspace.Echo:{call}");

        level.Play().Tick();

        level.Errors.ShouldBeEmpty();
        level.Printed.ShouldBe([$"Echo got {got}"]);
    }

    [Fact]
    public void A_node_reaches_the_entity_as_its_id()
    {
        using var level = new ScriptLevel();
        Echo(level);
        SceneNode crate = level.Part("Crate", default);
        level.AddScript(level.Scene.Root, """
            workspace.Echo:SayNode(workspace.Crate)
            workspace.Echo:SayAny(workspace.Crate)
            """);

        level.Play().Tick();

        string id = KeyvalueWire.Format(crate.Id);
        level.Printed.ShouldBe([$"Echo got SayNode '{id}'", $"Echo got SayAny '{id}'"]);
    }

    [Theory]
    [InlineData("Ping(1)", "too many arguments to 'Ping' (no value expected, got 1 value)")]
    [InlineData("SayNumber(1, 2)", "too many arguments to 'SayNumber' (number expected, got 2 values)")]
    [InlineData("SayPair(1, 2, 3)", "too many arguments to 'SayPair' (number, number expected, got 3 values)")]
    [InlineData("SayAny(1, 2)", "too many arguments to 'SayAny' (one value expected, got 2 values)")]
    [InlineData("SayNumber()", "invalid argument #1 to 'SayNumber' (number expected, got nil)")]
    [InlineData("SayNumber('fast')", "invalid argument #1 to 'SayNumber' (number expected, got string)")]
    [InlineData("SayNumber(1 / 0)", "invalid argument #1 to 'SayNumber' (a finite number expected, got Infinity)")]
    [InlineData("SayWhole(1.5)", "invalid argument #1 to 'SayWhole' (a whole number expected, got 1.5)")]
    [InlineData("SayWhole(2 ^ 40)", "invalid argument #1 to 'SayWhole' (a whole number expected, got 1099511627776)")]
    [InlineData("SayFlag(1)", "invalid argument #1 to 'SayFlag' (boolean expected, got number)")]
    [InlineData("SayText(5)", "invalid argument #1 to 'SayText' (string expected, got number)")]
    [InlineData("SayPoint(3)", "invalid argument #1 to 'SayPoint' (Vector3 expected, got number)")]
    [InlineData("SayPair(1)", "invalid argument #2 to 'SayPair' (number expected, got nil)")]
    [InlineData("SayFour(1, 2, 'x', 4)", "invalid argument #3 to 'SayFour' (number expected, got string)")]
    [InlineData("SayNode('Crate')", "invalid argument #1 to 'SayNode' (Instance expected, got string)")]
    [InlineData("SayNode(game)", "invalid argument #1 to 'SayNode' (Instance expected, got DataModel)")]
    [InlineData("SayAny({})", "invalid argument #1 to 'SayAny' (boolean, number, string, Vector3 or Instance expected, got table)")]
    public void What_an_input_does_not_take_is_an_error_at_the_scripts_line_and_nothing_is_sent(string call, string error)
    {
        using var level = new ScriptLevel();
        Echo(level);
        level.AddScript(level.Scene.Root, $"""
            print(select(2, pcall(function() workspace.Echo:{call} end)))
            """);

        level.Play().Tick();

        string here = level.Scene.ScriptNodes[0].Script.ShouldNotBeNull().Path;
        level.Printed.ShouldBe([$"{here}:1: {error}"]);
    }

    [Fact]
    public void A_name_the_class_does_not_have_is_no_member_and_names_are_case_sensitive()
    {
        using var level = new ScriptLevel();
        Echo(level);
        level.AddScript(level.Scene.Root, """
            local echo = workspace.Echo
            print(select(2, pcall(function() echo:ping() end)))
            print(select(2, pcall(function() echo:Explode() end)))
            print(select(2, pcall(function() return echo.onPing end)))
            print(select(2, pcall(function() echo.Ping = nil end)))
            print(select(2, pcall(function() echo.OnPing = nil end)))
            """);

        level.Play().Tick();

        string here = level.Scene.ScriptNodes[0].Script.ShouldNotBeNull().Path;
        level.Printed.ShouldBe(
        [
            $"{here}:2: ping is not a valid member of echo \"Echo\"",
            $"{here}:3: Explode is not a valid member of echo \"Echo\"",
            $"{here}:4: onPing is not a valid member of echo \"Echo\"",
            $"{here}:5: Unable to assign property Ping. Property is read only",
            $"{here}:6: Unable to assign property OnPing. Property is read only",
        ]);
    }

    [Fact]
    public void A_node_with_no_entity_has_no_input_and_no_output()
    {
        using var level = new ScriptLevel();
        level.Part("Crate", default);
        level.AddScript(level.Scene.Root, """
            print(select(2, pcall(function() workspace.Crate:Ping() end)))
            print(select(2, pcall(function() return workspace.Crate.OnPing end)))
            """);

        level.Play().Tick();

        string here = level.Scene.ScriptNodes[0].Script.ShouldNotBeNull().Path;
        level.Printed.ShouldBe(
        [
            $"{here}:1: Ping is not a valid member of Part \"Crate\"",
            $"{here}:2: OnPing is not a valid member of Part \"Crate\"",
        ]);
    }

    [Fact]
    public void A_node_whose_class_is_not_in_the_build_says_so_for_any_name()
    {
        using var level = new ScriptLevel();
        level.Scene.Root.CreateChild("Lift").Entity = new EntityData("func_lift");
        level.AddScript(level.Scene.Root, """
            local lift = workspace.Lift
            print(lift.ClassName, lift:IsA("func_lift"))
            print(select(2, pcall(function() lift:Start() end)))
            print(select(2, pcall(function() return lift.OnArrived end)))
            """);

        level.Play().Tick();

        string here = level.Scene.ScriptNodes[0].Script.ShouldNotBeNull().Path;
        const string Why = "The class func_lift is not in this build, so the node has none of its inputs and outputs";
        level.Printed.ShouldBe(
        [
            "func_lift true",
            $"{here}:3: Start is not a valid member of func_lift \"Lift\". {Why}",
            $"{here}:4: OnArrived is not a valid member of func_lift \"Lift\". {Why}",
        ]);
    }

    [Fact]
    public void An_input_read_and_not_called_is_one_function_that_sends_it_to_the_node_it_is_handed()
    {
        using var level = new ScriptLevel();
        Echo(level);
        Echo(level, "Other");
        level.Part("Crate", default);
        level.AddScript(level.Scene.Root, """
            local say = workspace.Echo.SayNumber
            print(type(say), say == workspace.Echo.SayNumber, say == workspace.Other.SayNumber)
            say(workspace.Other, 3)
            print(select(2, pcall(say)))
            print(select(2, pcall(say, workspace.Crate, 3)))
            print(select(2, pcall(say, workspace.Echo, 'x')))
            """);

        level.Play().Tick();

        string here = level.Scene.ScriptNodes[0].Script.ShouldNotBeNull().Path;
        level.Printed.ShouldBe(
        [
            "function true true",
            $"{here}:4: Expected ':' not '.' calling member function SayNumber",
            $"{here}:5: SayNumber is not a valid member of Part \"Crate\"",
            $"{here}:6: invalid argument #1 to 'SayNumber' (number expected, got string)",
            "Other got SayNumber '3'",
        ]);
    }

    [Fact]
    public void A_call_on_a_node_whose_entity_no_longer_runs_is_an_error_that_says_so()
    {
        using var level = new ScriptLevel();
        SceneNode echo = Echo(level);
        level.AddScript(level.Scene.Root, """
            workspace.Echo:Ping()
            task.wait()
            print(select(2, pcall(function() workspace.Echo:Ping() end)))
            """);
        level.Play();
        Entity live = level.Entities.Entities.Single(entity => ReferenceEquals(entity.Node, echo));

        // The scripts start at the head of the tick, and the despawn runs at its end.
        level.Entities.QueueDespawn(live);
        level.Tick(2);

        string here = level.Scene.ScriptNodes[0].Script.ShouldNotBeNull().Path;
        level.Errors.ShouldBeEmpty();
        level.Printed.ShouldBe(
        [
            "Echo got Ping ''",
            $"{here}:3: Ping was not sent: the entity of \"Echo\" is not running",
        ]);
    }

    private static SceneNode Echo(ScriptLevel level, string name = "Echo")
    {
        SceneNode node = level.Scene.Root.CreateChild(name);
        node.Entity = new EntityData(EchoEntity.Class);
        return node;
    }
}
