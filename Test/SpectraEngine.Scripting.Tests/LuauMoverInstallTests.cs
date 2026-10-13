using SpectraEngine.Core.Physics.Character;
using SpectraEngine.Core.Scene;
using SpectraEngine.Core.Scripting;
using SpectraEngine.Scripting.Luau.Runtime;

namespace SpectraEngine.Scripting.Tests;

/// <summary>
/// How a script's mover comes and goes: SetMover puts it in, nil and Stop
/// put the built-in mover back, and a second one takes the place of the
/// first.
/// </summary>
public sealed class LuauMoverInstallTests
{
    private const string Character = """
        local character = game:GetService("Players").LocalPlayer.Character
        local walk = require("./movers/walk")
        """;

    private static readonly CharacterCommand Ahead = MoverCourse.Walk(0f);

    [Fact]
    public void Stop_puts_the_built_in_mover_back_and_the_next_Play_starts_with_it_until_a_script_installs_again()
    {
        using MoverLevel level = new MoverLevel().WithTheShippedMover();
        level.Character.Mover.ShouldBeSameAs(DefaultCharacterMover.Instance);

        level.Play();
        level.Character.Mover.ShouldBeSameAs(DefaultCharacterMover.Instance, "scripts start on the first tick");
        level.Tick(in Ahead, 30);
        ICharacterMover first = level.Character.Mover;
        level.Stop();

        first.ShouldBeOfType<LuauCharacterMover>();
        level.Character.Mover.ShouldBeSameAs(DefaultCharacterMover.Instance);

        level.Play();
        level.Character.Mover.ShouldBeSameAs(DefaultCharacterMover.Instance);
        level.Tick(in Ahead, 30);

        level.Character.Mover.ShouldBeOfType<LuauCharacterMover>().ShouldNotBeSameAs(first);
        level.Errors.ShouldBeEmpty();
        level.Printed.ShouldBe(["The player moves by a mover in Luau", "The player moves by a mover in Luau"]);
        level.State.Position.X.ShouldBeGreaterThan(126f, "the second run walked too");
    }

    [Fact]
    public void A_level_whose_script_no_longer_installs_a_mover_plays_with_the_built_in_one()
    {
        using MoverLevel level = new MoverLevel().WithTheShippedMover();
        level.Play().Tick(in Ahead, 5).Stop();
        SceneNode installer = level.Scene.Root.Children.Single(node => node.Name == DemoMover.ScriptName);
        installer.Script = new Script(MoverLevel.Installer) { IsEnabled = false };

        level.Play().Tick(in Ahead, 30);

        level.Character.Mover.ShouldBeSameAs(DefaultCharacterMover.Instance);
        level.State.Position.X.ShouldBeGreaterThan(126f);
    }

    [Fact]
    public void Stop_puts_back_the_mover_a_host_had_installed_before_the_level_played()
    {
        using MoverLevel level = new MoverLevel().WithTheShippedMover();
        var hosts = new BeginOnlyMover();
        level.Character.Mover = hosts;

        level.Play().Tick(in Ahead, 5);
        level.Character.Mover.ShouldBeOfType<LuauCharacterMover>();
        level.Stop();

        level.Character.Mover.ShouldBeSameAs(hosts);
    }

    [Fact]
    public void A_mover_moves_the_player_from_the_tick_it_is_installed_in_and_nil_puts_the_built_in_one_back()
    {
        using MoverLevel level = new MoverLevel().WithScript($$"""
            {{Character}}
            local run = game:GetService("RunService")
            character:SetMover(function(state) state.Position += Vector3.new(0, 0, 0.25) end)
            for _ = 1, 4 do run.Heartbeat:Wait() end
            character:SetMover(nil)
            """);
        level.Play();
        float start = level.State.Position.Z;

        // The fourth heartbeat comes at the end of the fourth tick.
        level.Tick(default, 3);
        bool wasScripted = level.Character.Mover is LuauCharacterMover;
        level.Tick(default);
        float carried = level.State.Position.Z;
        level.Tick(default, 20);

        level.Errors.ShouldBeEmpty();
        wasScripted.ShouldBeTrue();
        carried.ShouldBe(start + 1f, 1e-5f);
        level.Character.Mover.ShouldBeSameAs(DefaultCharacterMover.Instance);
        level.State.Position.Z.ShouldBe(carried, 1e-5f);
        level.State.Grounded.ShouldBeTrue("the built-in mover stood the player on the floor");
    }

    [Fact]
    public void A_mover_installed_twice_by_one_script_runs_once_a_tick_and_only_the_later_one()
    {
        using MoverLevel level = new MoverLevel().WithScript($$"""
            {{Character}}
            local first, second = 0, 0
            character:SetMover(function(...) first += 1 walk(...) end)
            character:SetMover(function(...) second += 1 walk(...) end)
            game:GetService("RunService").Heartbeat:Connect(function() print(first, second) end)
            """);

        level.Play().Tick(in Ahead, 3);

        level.Sink.Messages.Where(message => message.Severity != ScriptSeverity.Print).ShouldBeEmpty();
        level.Printed.ShouldBe(["0 1", "0 2", "0 3"]);
    }

    [Fact]
    public void A_mover_of_another_script_takes_the_place_of_the_first_and_a_warning_names_whose_it_was()
    {
        using MoverLevel level = new MoverLevel()
            .WithScript($$"""
                {{Character}}
                character:SetMover(function(...) print("first") walk(...) end)
                """)
            .WithScript($$"""
                {{Character}}
                character:SetMover(function(...) print("second") walk(...) end)
                """);

        level.Play().Tick(in Ahead, 2);

        level.Errors.ShouldBeEmpty();
        level.Printed.ShouldBe(["second", "second"]);
        ScriptMessage warning = level.Sink.Messages.Single(message => message.Severity == ScriptSeverity.Warning);
        warning.Text.ShouldBe("This mover takes the place of the one Scripts/test1.luau installed.");
        warning.Position.ShouldBe("Scripts/test2.luau:3");
    }

    // The old one finishes the tick it is in. Its thread is let go of only
    // then, which a crash would tell of if it were not.
    [Fact]
    public void A_mover_that_installs_another_from_inside_its_own_call_finishes_its_tick_first()
    {
        using MoverLevel level = new MoverLevel().WithScript($$"""
            {{Character}}
            character:SetMover(function(state, ...)
                character:SetMover(function(state, ...) print("second") walk(state, ...) end)
                local filler = {}
                for i = 1, 2000 do filler[i] = { i } end
                print("first")
                walk(state, ...)
            end)
            """);

        level.Play().Tick(in Ahead, 3);

        level.Errors.ShouldBeEmpty();
        level.Printed.ShouldBe(["first", "second", "second"]);
        level.State.Position.X.ShouldBeGreaterThan(125f);
    }

    [Theory]
    [InlineData("character:SetMover('walk')", "invalid argument #1 to 'SetMover' (function expected, got string)")]
    [InlineData("character:SetMover(walk, 2) character.SetMover(walk)", "Expected ':' not '.' calling member function SetMover")]
    public void What_is_no_mover_is_refused_at_the_line_that_hands_it_over(string statement, string said)
    {
        using MoverLevel level = new MoverLevel().WithScript($$"""
            {{Character}}
            {{statement}}
            """);

        level.Play().Tick(in Ahead, 2);

        ScriptMessage error = level.Sink.Messages.Single(message => message.Severity == ScriptSeverity.Error);
        error.Text.ShouldBe(said);
        error.Position.ShouldBe("Scripts/test1.luau:3");
    }

    [Fact]
    public void A_map_loaded_while_the_level_plays_puts_the_built_in_mover_back_and_the_player_walks_on()
    {
        using MoverLevel level = new MoverLevel().WithTheShippedMover();
        level.Play().Tick(in Ahead, 10);
        float before = level.State.Position.X;

        level.Manager.OnSceneReplaced();
        level.Tick(in Ahead, 30);

        level.Character.Mover.ShouldBeSameAs(DefaultCharacterMover.Instance);
        level.State.Position.X.ShouldBeGreaterThan(before + 1f);
        level.Errors.ShouldBeEmpty();
    }

    // What a host kept of a level that has stopped must not reach into a
    // Luau state that is gone.
    [Fact]
    public void A_mover_kept_past_its_level_moves_the_player_as_the_built_in_mover_does()
    {
        using MoverLevel level = new MoverLevel().WithTheShippedMover();
        using var builtIn = new MoverLevel();
        level.Play().Tick(in Ahead, 5);
        ICharacterMover kept = level.Character.Mover;
        level.Stop();
        builtIn.Play().Tick(in Ahead, 5).Stop();

        level.Character.Mover = kept;
        level.Character.Spawn();
        builtIn.Character.Spawn();
        for (int i = 0; i < 30; i++)
        {
            level.Character.Tick(in Ahead, MoverLevel.Dt);
            builtIn.Character.Tick(in Ahead, MoverLevel.Dt);
        }

        kept.ShouldBeOfType<LuauCharacterMover>();
        level.State.Position.ShouldBe(builtIn.State.Position);
        level.Errors.ShouldBeEmpty();
    }

    [Fact]
    public void What_a_mover_is_handed_reads_the_player_between_ticks_and_changes_nothing_there()
    {
        using MoverLevel level = new MoverLevel().WithScript($$"""
            {{Character}}
            local run = game:GetService("RunService")
            local kept = {}
            character:SetMover(function(state, command, collision, tuning, dt)
                kept = { state, command, collision, tuning }
                walk(state, command, collision, tuning, dt)
            end)
            run.Heartbeat:Wait()
            local state, command, collision, tuning = table.unpack(kept)
            print(state.Position == character.Position, state.IsGrounded == character.IsGrounded)
            print(command.MoveDirection, command.Jump, tuning.WalkSpeed == character.WalkSpeed)
            print(pcall(function() state.Position = Vector3.zero end))
            print(pcall(function() state:LeaveGround() end))
            print(pcall(function() return collision:Sweep(state.Position, Vector3.yAxis) end))
            print(pcall(function() collision:ClearPlanes() end))
            """);

        level.Play().Tick(in Ahead, 2);

        level.Errors.ShouldBeEmpty();
        level.Printed.ShouldBe(
        [
            "true true",
            "1, 0, 0 false true",
            $"false Scripts/test1.luau:13: {MoverFrame.OnlyWhileMoving}",
            $"false Scripts/test1.luau:14: {MoverFrame.OnlyWhileMoving}",
            $"false Scripts/test1.luau:15: {MoverFrame.OnlyWhileMoving}",
            $"false Scripts/test1.luau:16: {MoverFrame.OnlyWhileMoving}",
        ]);
    }

    // The mover works on a copy of the state. A teleport moves the player
    // itself, and writing the copy over it would take the teleport back.
    [Fact]
    public void A_teleport_from_inside_the_mover_stands_and_the_mover_goes_on_from_there()
    {
        using MoverLevel level = new MoverLevel().WithScript($$"""
            {{Character}}
            local ticks = 0
            character:SetMover(function(state, ...)
                ticks += 1
                if ticks == 10 then
                    character:Teleport(Vector3.new(141.5, 2.05, 0))
                end
                walk(state, ...)
            end)
            """);

        level.Play().Tick(in Ahead, 10);
        System.Numerics.Vector3 landed = level.State.Position;
        level.Tick(in Ahead, 30);

        level.Errors.ShouldBeEmpty();
        landed.ShouldBe(new System.Numerics.Vector3(141.5f, 2.05f, 0f));
        level.State.Position.X.ShouldBeGreaterThan(142.5f, "it walked on along the terrace");
        level.State.Grounded.ShouldBeTrue();
        level.Character.Mover.ShouldBeOfType<LuauCharacterMover>();
    }

    [Fact]
    public void A_mover_gets_the_same_four_objects_on_every_tick_and_the_step_in_seconds()
    {
        using MoverLevel level = new MoverLevel().WithScript($$"""
            {{Character}}
            local seen
            character:SetMover(function(state, command, collision, tuning, dt, more)
                local now = { state, command, collision, tuning }
                if seen then
                    print(now[1] == seen[1], now[2] == seen[2], now[3] == seen[3], now[4] == seen[4], dt, more)
                    print(tostring(state), tostring(command), tostring(collision), tostring(tuning), typeof(state))
                end
                seen = now
                walk(state, command, collision, tuning, dt)
            end)
            """);

        level.Play().Tick(in Ahead, 2);

        level.Errors.ShouldBeEmpty();
        level.Printed.ShouldBe(
        [
            "true true true true 0.01666666753590107 nil",
            "CharacterState CharacterCommand CharacterCollision CharacterTuning Instance",
        ]);
    }
}
