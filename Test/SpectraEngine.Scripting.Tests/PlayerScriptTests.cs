using System.Numerics;
using SpectraEngine.Core.Physics;
using SpectraEngine.Core.Physics.Character;
using static SpectraEngine.Scripting.Tests.PlayerScripts;

namespace SpectraEngine.Scripting.Tests;

/// <summary>
/// The player as a script reaches it: where it is as it walks, a teleport,
/// a respawn, and how fast it walks and how high it jumps.
/// </summary>
public sealed class PlayerScriptTests
{
    private static readonly float Gravity = MathF.Abs(PhysicsDefaults.Gravity.Y);

    [Fact]
    public void A_script_reads_where_the_player_is_as_the_player_walks()
    {
        using var level = new ScriptLevel();
        level.AddScript(level.Scene.Root, $$"""
            {{Character}}
            game:GetService("RunService").Heartbeat:Connect(function()
                local p, v = character.Position, character.Velocity
                print(p.X, p.Y, p.Z, v.X, v.Y, v.Z)
                print(character.IsGrounded)
            end)
            """);
        level.Play();
        CharacterSimulation walker = Walker(level);

        var seen = new List<CharacterState>();
        for (int i = 0; i < 90; i++)
        {
            Tick(level, Forward());
            seen.Add(walker.State);
        }

        level.Errors.ShouldBeEmpty();
        string[] printed = level.Printed;
        printed.Length.ShouldBe(2 * seen.Count);
        for (int i = 0; i < seen.Count; i++)
        {
            float[] numbers = Numbers(printed[2 * i]);
            new Vector3(numbers[0], numbers[1], numbers[2]).ShouldBe(seen[i].Position, $"tick {i + 1}");
            new Vector3(numbers[3], numbers[4], numbers[5]).ShouldBe(seen[i].Velocity, $"tick {i + 1}");
            printed[(2 * i) + 1].ShouldBe(seen[i].Grounded ? "true" : "false", $"tick {i + 1}");
        }

        // It fell to the ground and walked off along +X.
        seen[0].Grounded.ShouldBeFalse();
        seen[^1].Grounded.ShouldBeTrue();
        seen[^1].Position.X.ShouldBeGreaterThan(seen[0].Position.X + 2f);
    }

    [Fact]
    public void The_frame_a_script_reads_stands_at_the_feet_and_looks_the_way_the_player_walks()
    {
        const float Yaw = 0.6f;
        using var level = new ScriptLevel();
        level.AddScript(level.Scene.Root, $$"""
            {{Character}}
            game:GetService("RunService").Heartbeat:Connect(function()
                local frame = character.CFrame
                print(frame.Position == character.Position)
                print(frame.LookVector.X, frame.LookVector.Y, frame.LookVector.Z)
            end)
            """);
        level.Play();
        Vector3 start = Walker(level).State.Position;

        Tick(level, Forward(Yaw), 60);

        level.Errors.ShouldBeEmpty();
        level.Printed[^2].ShouldBe("true");
        Vector3 facing = Vector(level.Printed[^1]);
        facing.X.ShouldBe(MathF.Cos(Yaw), 1e-5f);
        facing.Y.ShouldBe(0f, 1e-5f);
        facing.Z.ShouldBe(MathF.Sin(Yaw), 1e-5f);

        Vector3 walked = Walker(level).State.Position - start;
        Vector3 along = Vector3.Normalize(new Vector3(walked.X, 0f, walked.Z));
        Vector3.Dot(along, facing).ShouldBe(1f, 1e-3f);
    }

    [Fact]
    public void What_a_script_puts_in_front_of_the_frame_is_ahead_of_the_player_and_its_right_is_to_the_right()
    {
        using var level = new ScriptLevel();
        level.AddScript(level.Scene.Root, $$"""
            {{Character}}
            game:GetService("RunService").Heartbeat:Connect(function()
                local frame = character.CFrame
                local ahead = (frame * CFrame.new(0, 0, -5)).Position - frame.Position
                local right = (frame * CFrame.new(2, 0, 0)).Position - frame.Position
                print(ahead.X, ahead.Y, ahead.Z, right.X, right.Y, right.Z)
            end)
            """);
        level.Play();
        CharacterSimulation walker = Walker(level);

        // Yaw zero walks along +X, and a strafe to the right goes along +Z.
        Tick(level, Forward(), 40);
        float[] numbers = Numbers(level.Printed[^1]);
        Vector3 before = walker.State.Position;
        Tick(level, new CharacterCommand { MoveStrafe = CharacterCommand.Axis(1f) }, 40);

        level.Errors.ShouldBeEmpty();
        float[] aheadThenRight = [5f, 0f, 0f, 0f, 0f, 2f];
        for (int i = 0; i < aheadThenRight.Length; i++)
            numbers[i].ShouldBe(aheadThenRight[i], 1e-5f, $"number {i}");

        (walker.State.Position.Z - before.Z).ShouldBeGreaterThan(1f);
    }

    [Fact]
    public void A_teleport_lands_the_player_there_on_the_next_tick_and_the_mover_carries_on_from_it()
    {
        using var level = new ScriptLevel();
        level.AddScript(level.Scene.Root, $$"""
            {{Character}}
            local beats = 0
            game:GetService("RunService").Heartbeat:Connect(function()
                beats += 1
                if beats == 40 then
                    character:Teleport(Vector3.new(20, 0.05, -12))
                    print(character.Position == Vector3.new(20, 0.05, -12), character.Velocity == Vector3.zero)
                end
            end)
            """);
        level.Play();
        CharacterSimulation walker = Walker(level);

        Tick(level, Forward(), 40);

        // Put there at once and at rest. What the script reads is what it wrote.
        level.Errors.ShouldBeEmpty();
        level.Printed.ShouldBe(["true true"]);
        walker.State.Position.ShouldBe(new Vector3(20f, 0.05f, -12f));
        walker.State.Velocity.ShouldBe(Vector3.Zero);

        // The next tick starts from there.
        Tick(level, Forward());
        walker.State.Position.X.ShouldBeInRange(20f, 20.1f);
        walker.State.Position.Y.ShouldBeInRange(0f, 0.05f);
        walker.State.Position.Z.ShouldBe(-12f, 1e-4f);

        // And the player lands and walks on.
        Tick(level, Forward(), 59);
        walker.State.Grounded.ShouldBeTrue();
        walker.State.Position.X.ShouldBeGreaterThan(22.5f);
        walker.State.Position.Z.ShouldBe(-12f, 1e-3f);
    }

    [Fact]
    public void A_teleport_to_a_frame_turns_the_player_to_what_the_frame_looks_at()
    {
        using var level = new ScriptLevel();
        level.AddScript(level.Scene.Root, $$"""
            {{Character}}
            character:Teleport(CFrame.lookAt(Vector3.new(5, 0.05, 5), Vector3.new(5, 0.05, -4)))
            local p, look = character.Position, character.CFrame.LookVector
            print(p.X, p.Y, p.Z, look.X, look.Y, look.Z)
            """);

        level.Play().Tick();

        level.Errors.ShouldBeEmpty();
        float[] numbers = Numbers(level.Printed.Single());
        numbers[..3].ShouldBe([5f, 0.05f, 5f]);
        numbers[3].ShouldBe(0f, 1e-5f);
        numbers[4].ShouldBe(0f, 1e-5f);
        numbers[5].ShouldBe(-1f, 1e-5f);

        // A view turns to it as it does for a trigger that teleports: this yaw walks along -Z.
        Walker(level).TryTakeTeleport(out float? yaw).ShouldBeTrue();
        yaw.ShouldNotBeNull().ShouldBe(-MathF.PI / 2f, 1e-5f);
    }

    [Fact]
    public void A_teleport_to_the_frame_the_player_has_leaves_it_where_it_is_and_facing_as_it_did()
    {
        using var level = new ScriptLevel();
        level.AddScript(level.Scene.Root, $$"""
            {{Character}}
            local beats = 0
            game:GetService("RunService").Heartbeat:Connect(function()
                beats += 1
                if beats == 30 then
                    local before = character.CFrame
                    character:Teleport(before)
                    print(character.Position == before.Position)
                    print((character.CFrame.LookVector - before.LookVector).Magnitude < 1e-5)
                end
            end)
            """);
        level.Play();

        Tick(level, Forward(0.6f), 30);

        level.Errors.ShouldBeEmpty();
        level.Printed.ShouldBe(["true", "true"]);
        Walker(level).TryTakeTeleport(out float? yaw).ShouldBeTrue();
        yaw.ShouldNotBeNull().ShouldBe(0.6f, 1e-5f);
    }

    [Fact]
    public void A_respawn_sends_the_player_back_to_the_start_at_rest()
    {
        using var level = new ScriptLevel();
        level.AddScript(level.Scene.Root, $$"""
            {{Character}}
            local beats = 0
            game:GetService("RunService").Heartbeat:Connect(function()
                beats += 1
                if beats == 60 then character:Respawn() end
            end)
            """);
        level.Play();
        CharacterSimulation walker = Walker(level);
        Vector3 start = walker.State.Position;

        Tick(level, Forward(0.4f), 59);
        Vector3.Distance(walker.State.Position, start).ShouldBeGreaterThan(1.5f);

        Tick(level, Forward(0.4f));

        level.Errors.ShouldBeEmpty();
        walker.State.Position.ShouldBe(start);
        walker.State.Velocity.ShouldBe(Vector3.Zero);
        walker.Yaw.ShouldBe(walker.SpawnYaw);

        // A view takes it as a teleport. The fall-out guard did not fire.
        walker.TryTakeTeleport(out float? yaw).ShouldBeTrue();
        yaw.ShouldBe(walker.SpawnYaw);
        walker.Respawns.ShouldBe(0);

        // The mover carries on from the start.
        Tick(level, default, 40);
        walker.State.Grounded.ShouldBeTrue();
        walker.State.Position.X.ShouldBe(start.X, 0.01f);
        walker.State.Position.Z.ShouldBe(start.Z, 0.01f);
    }

    [Fact]
    public void A_walk_speed_a_script_sets_changes_how_far_the_player_gets_in_a_second()
    {
        float asAuthored = WalkedInASecond("");
        float twiceAsFast = WalkedInASecond("character.WalkSpeed = character.WalkSpeed * 2");

        float speed = new CharacterTuning().WalkSpeed;
        asAuthored.ShouldBeInRange(0.9f * speed, speed);
        twiceAsFast.ShouldBeInRange(1.8f * speed, 2f * speed);
    }

    [Fact]
    public void A_jump_height_a_script_sets_is_how_high_the_player_jumps()
    {
        using var level = new ScriptLevel();
        level.AddScript(level.Scene.Root, $"{Character}\ncharacter.JumpHeight = 3\nprint(character.JumpHeight)");
        level.Play();
        float ground = Land(level);

        float highest = HighestIn(level, new CharacterCommand { Buttons = CharacterButtons.Jump }, ticks: 120);

        level.Errors.ShouldBeEmpty();
        level.Printed.ShouldBe(["3"]);
        (highest - ground).ShouldBe(3f, 0.1f);
        Walker(level).State.Grounded.ShouldBeTrue();
    }

    [Fact]
    public void A_velocity_a_script_sets_throws_the_player_and_reads_back()
    {
        using var level = new ScriptLevel();
        level.AddScript(level.Scene.Root, $$"""
            {{Character}}
            local beats = 0
            game:GetService("RunService").Heartbeat:Connect(function()
                beats += 1
                if beats == 41 then
                    print(character.IsGrounded)
                    character.Velocity = Vector3.new(0, 8, 0)
                    print(character.Velocity == Vector3.new(0, 8, 0), character.IsGrounded)
                end
            end)
            """);
        level.Play();
        float ground = Land(level);

        float highest = HighestIn(level, default, ticks: 120);

        level.Errors.ShouldBeEmpty();
        level.Printed.ShouldBe(["true", "true false"]);
        (highest - ground).ShouldBe(8f * 8f / (2f * Gravity), 0.15f);
        Walker(level).State.Grounded.ShouldBeTrue();
    }

    [Fact]
    public void The_two_handles_a_script_gets_to_the_player_are_equal()
    {
        using var level = new ScriptLevel();
        level.AddScript(level.Scene.Root, $$"""
            {{Character}}
            print(Players == game:GetService("Players"), Players == game.Players)
            print(Players.LocalPlayer == Players.LocalPlayer, character == Players.LocalPlayer.Character)
            local seen = { [character] = 'the character', [Players.LocalPlayer] = 'the player' }
            task.wait(0.1)
            print(character == game.Players.LocalPlayer.Character, seen[Players.LocalPlayer.Character], seen[Players.LocalPlayer])
            print(character == Players.LocalPlayer, Players.LocalPlayer == Players)
            """);

        level.Play().Tick(10);

        level.Errors.ShouldBeEmpty();
        level.Printed.ShouldBe(["true true", "true true", "true the character the player", "false false"]);
    }

    [Fact]
    public void The_players_objects_print_as_their_names_and_are_no_nodes()
    {
        using var level = new ScriptLevel();
        level.AddScript(level.Scene.Root, $$"""
            {{Character}}
            print(Players, Players.LocalPlayer, Players.LocalPlayer.Name, character)
            print(typeof(Players), typeof(Players.LocalPlayer), typeof(character))
            print(pcall(function() return character.Parent end))
            """);

        level.Play().Tick();

        level.Errors.ShouldBeEmpty();
        level.Printed[0].ShouldBe("Players Player Player Character");
        level.Printed[1].ShouldBe("Instance Instance Instance");
        level.Printed[2].ShouldEndWith("Parent is not a valid member of Character \"Character\"");
    }

    [Fact]
    public void There_is_no_local_player_when_nothing_walks_and_a_script_that_guards_for_it_runs()
    {
        using var level = new ScriptLevel(hasCharacter: false);
        level.AddScript(level.Scene.Root, """
            local player = game:GetService("Players").LocalPlayer
            print(player, game.Players.LocalPlayer)
            if player and player.Character then
                print('somebody walks')
            else
                print('nobody walks')
            end
            """);

        level.Play().Tick();

        level.Errors.ShouldBeEmpty();
        level.Printed.ShouldBe(["nil nil", "nobody walks"]);
    }

    [Fact]
    public void A_script_that_does_not_guard_fails_at_its_line_when_nothing_walks()
    {
        using var level = new ScriptLevel(hasCharacter: false);
        level.AddScript(level.Scene.Root, Character + "\nprint('not reached')");

        level.Play().Tick();

        level.Errors.Single().ShouldContain("attempt to index nil with 'Character'");
        level.Sink.Messages.Single().Line.ShouldBe(2);
        level.Printed.ShouldBeEmpty();
    }

    [Fact]
    public void A_character_a_script_kept_is_an_error_to_use_once_the_player_has_left()
    {
        using var level = new ScriptLevel();
        level.AddScript(level.Scene.Root, $$"""
            {{Character}}
            task.wait()
            print(Players.LocalPlayer)
            print(pcall(function() return character.Position end))
            print(pcall(function() character.WalkSpeed = 9 end))
            print(pcall(function() character:Respawn() end))
            """);
        level.Play().Tick();

        level.Entities.Player = null;
        level.Tick();

        level.Errors.ShouldBeEmpty();
        level.Printed[0].ShouldBe("nil");
        level.Printed[1].ShouldEndWith("this object has left the level");
        level.Printed[2].ShouldEndWith("The player cannot be changed: nothing walks the level.");
        level.Printed[3].ShouldEndWith("The player cannot be changed: nothing walks the level.");
        level.Printed.ShouldAllBe(line => line == "nil" || line.StartsWith("false "));
    }

    // Lets the player land, then walks it for a second along +X.
    private static float WalkedInASecond(string statement)
    {
        using var level = new ScriptLevel();
        level.AddScript(level.Scene.Root, $"{Character}\n{statement}");
        level.Play();
        Land(level);
        float from = Walker(level).State.Position.X;

        Tick(level, Forward(), 60);

        level.Errors.ShouldBeEmpty();
        return Walker(level).State.Position.X - from;
    }

    // Lets the player fall from its start to the ground. Hands back how high the ground is.
    private static float Land(ScriptLevel level)
    {
        Tick(level, default, 40);
        Walker(level).State.Grounded.ShouldBeTrue();
        return Walker(level).State.Position.Y;
    }

    // The highest the feet get while the command is held for so many ticks.
    private static float HighestIn(ScriptLevel level, in CharacterCommand command, int ticks)
    {
        float highest = float.NegativeInfinity;
        for (int i = 0; i < ticks; i++)
        {
            Tick(level, in command);
            highest = MathF.Max(highest, Walker(level).State.Position.Y);
        }

        return highest;
    }
}
