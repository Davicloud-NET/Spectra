using SpectraEngine.Core.Physics.Character;
using SpectraEngine.Core.Scripting;
using SpectraEngine.Scripting.Luau.Runtime;

namespace SpectraEngine.Scripting.Tests;

/// <summary>
/// A mover in Luau that fails: an error, a value that is no number, a wait,
/// a loop with no end. Each time the built-in mover moves the player on
/// that same tick and from then on, and one line in Output says why.
/// </summary>
public sealed class LuauMoverFailureTests
{
    private const string TookOver = " The built-in mover took over.";

    // The script of every case: the mover is its third line.
    private const string Installs = """
        local character = game:GetService("Players").LocalPlayer.Character
        character:SetMover(function(state, command, collision, tuning, dt)
            {0}
        end)
        """;

    [Theory]
    [InlineData("error('no legs')", "The mover failed: no legs.")]
    [InlineData("state.Speed = 4", "The mover failed: Speed is not a valid member of CharacterState \"CharacterState\".")]
    [InlineData("local feet = nil; state.Position = feet.X", "The mover failed: attempt to index nil with 'X'.")]
    [InlineData(
        "state.Position = Vector3.new(0 / 0, 0, 0)",
        "The mover failed: Position cannot be (NaN, 0, 0): it takes three numbers, each within a billion units of the origin.")]
    [InlineData(
        "state.Position = Vector3.new(0, 1e30, 0)",
        "The mover failed: Position cannot be (0, 1E+30, 0): it takes three numbers, each within a billion units of the origin.")]
    [InlineData(
        "state.Velocity = Vector3.new(0, 0, math.huge)",
        "The mover failed: Velocity cannot be (0, 0, Infinity): it takes three numbers and no more than 10000 units a second.")]
    [InlineData(
        "state.Velocity = Vector3.new(2e19, 0, 0)",
        "The mover failed: Velocity cannot be (2E+19, 0, 0): it takes three numbers and no more than 10000 units a second.")]
    [InlineData("state.AirTicks = 300", "The mover failed: AirTicks cannot be 300: it takes a whole number from 0 to 255.")]
    [InlineData("state.Position = 'here'", "The mover failed: Unable to assign property Position. Vector3 expected, got string.")]
    [InlineData(
        "collision:Sweep(state.Position, Vector3.new(0 / 0, 0, 0))",
        "The mover failed: A sweep cannot be (NaN, 0, 0): it takes three numbers and no more than 10000 units.")]
    [InlineData(
        "collision:Gather(Vector3.new(0, 3e38, 0), 0)",
        "The mover failed: A place cannot be (0, 3E+38, 0): it takes three numbers, each within a billion units of the origin.")]
    [InlineData("state:StandOn(Vector3.zero)", "The mover failed: A normal cannot be (0, 0, 0): it takes three numbers that are not all zero.")]
    [InlineData("state:SetMemory(9, 1)", "The mover failed: A mover has the slots 1 to 8 to keep numbers in.")]
    [InlineData("task.wait()", "The mover waited, and a mover has to finish in the tick that calls it.")]
    [InlineData("coroutine.yield()", "The mover waited, and a mover has to finish in the tick that calls it.")]
    [InlineData("game:GetService('RunService').Heartbeat:Wait()", "The mover waited, and a mover has to finish in the tick that calls it.")]
    public void A_mover_that_fails_leaves_its_first_tick_and_every_later_one_to_the_built_in_mover(string body, string said)
    {
        using var builtIn = new MoverLevel();
        using MoverLevel failing = new MoverLevel().WithScript(string.Format(Installs, body));

        string[] expected = Walk(builtIn.Play(), 90);
        string[] walked = Walk(failing.Play(), 90);

        ScriptMessage line = failing.Sink.Single;
        line.Severity.ShouldBe(ScriptSeverity.Error);
        line.Text.ShouldBe(said + TookOver);
        line.Position.ShouldBe("Scripts/test1.luau:3");
        walked.ShouldBe(expected);
        failing.Character.Mover.ShouldBeSameAs(DefaultCharacterMover.Instance);
    }

    [Fact]
    public void A_mover_that_hands_a_state_back_is_told_that_it_returns_nothing()
    {
        using var builtIn = new MoverLevel();
        using MoverLevel failing = new MoverLevel().WithScript(string.Format(Installs, "return state"));

        string[] expected = Walk(builtIn.Play(), 30);
        string[] walked = Walk(failing.Play(), 30);

        ScriptMessage line = failing.Sink.Single;
        line.Text.ShouldBe("The mover returned a value. A mover changes the state it is handed and returns nothing." + TookOver);
        line.Script.ShouldBe("Scripts/test1.luau");
        walked.ShouldBe(expected);
    }

    [Fact]
    public void A_mover_that_never_ends_is_stopped_and_its_tick_goes_to_the_built_in_mover()
    {
        using var builtIn = new MoverLevel();
        using MoverLevel failing = new MoverLevel().WithScript(string.Format(Installs, "while true do end"));
        failing.Play();
        failing.Runtime.ShouldNotBeNull().CallLimit = TimeSpan.FromMilliseconds(50);

        string[] expected = Walk(builtIn.Play(), 30);
        string[] walked = Walk(failing, 30);

        ScriptMessage line = failing.Sink.Single;
        line.Text.ShouldBe(
            "The mover was stopped: the script ran for more than 0.05 s without a pause and was switched off." + TookOver);
        line.Position.ShouldBe("Scripts/test1.luau:3");
        walked.ShouldBe(expected);
    }

    [Fact]
    public void A_mover_that_holds_too_much_is_stopped_and_its_tick_goes_to_the_built_in_mover()
    {
        using var builtIn = new MoverLevel();
        using MoverLevel failing = new MoverLevel().WithScript(string.Format(
            Installs, "local held = {} while true do held[#held + 1] = string.rep('x', 4096) .. #held end"));
        failing.Play();
        failing.Runtime.ShouldNotBeNull().MemoryBudget = 4 * 1024 * 1024;

        string[] expected = Walk(builtIn.Play(), 30);
        string[] walked = Walk(failing, 30);

        ScriptMessage line = failing.Sink.Single;
        line.Text.ShouldBe("The mover was stopped: the scripts hold more than 4 MB and this one was switched off." + TookOver);
        line.Position.ShouldBe("Scripts/test1.luau:3");
        walked.ShouldBe(expected);
    }

    // The tick it fails in starts from the state the tick began with, not
    // from what the mover had made of its copy by then.
    [Fact]
    public void A_mover_that_fails_on_a_later_tick_leaves_that_tick_to_the_built_in_mover_from_where_the_tick_began()
    {
        using var builtIn = new MoverLevel();
        using MoverLevel failing = new MoverLevel().WithScript("""
            local walk = require("./movers/walk")
            local ticks = 0
            game:GetService("Players").LocalPlayer.Character:SetMover(function(state, command, collision, tuning, dt)
                ticks += 1
                walk(state, command, collision, tuning, dt)
                if ticks == 40 then
                    state.Position += Vector3.new(0, 50, 0)
                    error("lost its footing")
                end
            end)
            """);
        CharacterCommand ahead = MoverCourse.Walk(0f);
        failing.Play().Tick(in ahead, 39);
        failing.Errors.ShouldBeEmpty();
        CharacterState before = failing.State;

        failing.Tick(in ahead);

        builtIn.Play().Tick(in ahead, 39);
        builtIn.Character.Restore(in before);
        builtIn.Tick(in ahead);
        failing.Sink.Single.Text.ShouldBe("The mover failed: lost its footing." + TookOver);
        failing.Sink.Single.Position.ShouldBe("Scripts/test1.luau:8");
        StateBits.Of(failing.State, failing.Scene).ShouldBe(StateBits.Of(builtIn.State, builtIn.Scene));
        failing.State.Position.X.ShouldBeGreaterThan(before.Position.X + 0.05f, "the player walked on in the tick that failed");
    }

    [Fact]
    public void After_a_failure_no_mover_is_taken_until_the_level_is_played_again()
    {
        using MoverLevel level = new MoverLevel().WithScript("""
            local character = game:GetService("Players").LocalPlayer.Character
            character:SetMover(function() error("no legs") end)
            game:GetService("RunService").Heartbeat:Wait()
            print(pcall(function() character:SetMover(function() end) end))
            character:SetMover(nil)
            print("the built-in mover can still be asked for")
            """);

        level.Play().Tick(MoverCourse.Walk(0f), 5);

        level.Printed.ShouldBe(
        [
            $"false Scripts/test1.luau:4: {MoverSlot.FailedBefore}",
            "the built-in mover can still be asked for",
        ]);
        level.Errors.ShouldBe(["The mover failed: no legs." + TookOver]);
        level.Character.Mover.ShouldBeSameAs(DefaultCharacterMover.Instance);

        level.Stop();
        level.Play().Tick(MoverCourse.Walk(0f), 2);

        // The level's script installs its mover again, and it fails again.
        level.Errors.Length.ShouldBe(2);
    }

    // The other one is the level's mover by then, and nothing it did failed.
    [Fact]
    public void A_mover_that_fails_after_it_put_another_in_its_place_leaves_the_other_there()
    {
        using MoverLevel level = new MoverLevel().WithScript("""
            local character = game:GetService("Players").LocalPlayer.Character
            local run = game:GetService("RunService")
            local walk = require("./movers/walk")
            character:SetMover(function()
                character:SetMover(function(...) print("second") walk(...) end)
                error("no legs")
            end)
            run.Heartbeat:Wait()
            run.Heartbeat:Wait()
            print(pcall(function() character:SetMover(walk) end))
            """);
        CharacterCommand ahead = MoverCourse.Walk(0f);

        level.Play().Tick(in ahead);
        ICharacterMover second = level.Character.Mover;
        level.Tick(in ahead, 29);

        ScriptMessage line = level.Sink.Messages.Single(message => message.Severity == ScriptSeverity.Error);
        line.Text.ShouldBe("The mover failed: no legs. The built-in mover moved the player on this tick.");
        line.Position.ShouldBe("Scripts/test1.luau:6");
        second.ShouldBeOfType<LuauCharacterMover>();
        level.Printed.ShouldBe(["second", "true"]);
        level.Character.Mover.ShouldBeOfType<LuauCharacterMover>().ShouldNotBeSameAs(second);
        level.State.Position.X.ShouldBeGreaterThan(126f, "the player walked on");
    }

    [Fact]
    public void A_mover_that_fails_after_it_asked_for_the_built_in_one_does_not_bar_the_next()
    {
        using MoverLevel level = new MoverLevel().WithScript("""
            local character = game:GetService("Players").LocalPlayer.Character
            local walk = require("./movers/walk")
            character:SetMover(function()
                character:SetMover(nil)
                error("no legs")
            end)
            game:GetService("RunService").Heartbeat:Wait()
            print(pcall(function() character:SetMover(walk) end))
            """);

        level.Play().Tick(MoverCourse.Walk(0f), 30);

        level.Errors.ShouldBe(["The mover failed: no legs. The built-in mover moved the player on this tick."]);
        level.Printed.ShouldBe(["true"]);
        level.Character.Mover.ShouldBeOfType<LuauCharacterMover>();
        level.State.Position.X.ShouldBeGreaterThan(126f, "the player walked on");
    }

    // Its listener looped, which is no fault of the mover. The mover goes
    // with the script all the same.
    [Fact]
    public void A_mover_whose_script_was_switched_off_is_taken_out_on_its_next_tick()
    {
        using MoverLevel level = new MoverLevel().WithScript("""
            local walk = require("./movers/walk")
            local ticks = 0
            game:GetService("Players").LocalPlayer.Character:SetMover(walk)
            game:GetService("RunService").Heartbeat:Connect(function()
                ticks += 1
                while ticks == 3 do end
            end)
            """);
        level.Play();
        level.Runtime.ShouldNotBeNull().CallLimit = TimeSpan.FromMilliseconds(50);

        level.Tick(MoverCourse.Walk(0f), 10);

        level.Errors.ShouldBe(
        [
            "the script ran for more than 0.05 s without a pause and was switched off",
            "The mover's script was switched off." + TookOver,
        ]);
        level.Character.Mover.ShouldBeSameAs(DefaultCharacterMover.Instance);
        level.State.Position.X.ShouldBeGreaterThan(125.3f, "the player walked on");
    }

    // East from the start, through the door. Every state bit for bit.
    private static string[] Walk(MoverLevel level, int ticks)
    {
        var states = new string[ticks];
        for (int i = 0; i < ticks; i++)
        {
            level.Tick(MoverCourse.Walk(0f, i == 20 ? CharacterButtons.Jump : CharacterButtons.None));
            states[i] = StateBits.Of(level.State, level.Scene);
        }

        return states;
    }
}
