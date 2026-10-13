using SpectraEngine.Core.Physics.Character;
using SpectraEngine.Core.Scripting;
using SpectraEngine.Scripting.Luau.Runtime;

namespace SpectraEngine.Scripting.Tests;

/// <summary>
/// The limits of a mover in Luau from the inside: the mover the engine
/// ships stays in at the fastest a script can make the player, and a sweep
/// may leave the volume the tick picked.
/// </summary>
public sealed class LuauMoverLimitTests
{
    private static readonly CharacterCommand Stand = default;
    private static readonly CharacterCommand Jump = new() { Buttons = CharacterButtons.Jump };

    // The built-in mover has no cap, so the one in Luau must not fall out
    // where a script's own caps let the player go.
    [Fact]
    public void The_shipped_mover_stays_in_at_the_fastest_walk_a_script_can_set_with_the_sprint_held()
    {
        using MoverLevel level = new MoverLevel(hasCourse: false).WithTheShippedMover().WithScript($"""
            game:GetService("Players").LocalPlayer.Character.WalkSpeed = {PlayerWrites.MaxSpeed}
            """);
        CharacterCommand sprint = MoverCourse.Walk(0f, CharacterButtons.Sprint);

        float fastest = FastestOver(level.Play(), in sprint, 30);

        level.Errors.ShouldBeEmpty();
        level.Character.Mover.ShouldBeOfType<LuauCharacterMover>();
        fastest.ShouldBeGreaterThan(PlayerWrites.MaxSpeed);
    }

    // Thrown level at the cap, the fall adds to the speed.
    [Fact]
    public void The_shipped_mover_stays_in_when_the_player_is_thrown_as_fast_as_a_script_can_throw_it_and_falls()
    {
        using MoverLevel level = new MoverLevel(hasCourse: false).WithTheShippedMover().WithScript($"""
            local run = game:GetService("RunService")
            for _ = 1, 40 do run.Heartbeat:Wait() end
            game:GetService("Players").LocalPlayer.Character.Velocity = Vector3.new({PlayerWrites.MaxSpeed}, 0, 0)
            """);

        // In the air from a jump when it is thrown, on the fortieth tick.
        level.Play().Tick(in Stand, 30).Tick(in Jump);
        float fastest = FastestOver(level, in Stand, 110);

        level.Errors.ShouldBeEmpty();
        level.Character.Mover.ShouldBeOfType<LuauCharacterMover>();
        fastest.ShouldBeGreaterThan(PlayerWrites.MaxSpeed);
    }

    [Fact]
    public void A_sweep_that_leaves_the_volume_the_tick_picked_finds_what_is_in_its_way()
    {
        using MoverLevel level = new MoverLevel(hasCourse: false).WithScript("""
            local character = game:GetService("Players").LocalPlayer.Character
            local walk = require("./movers/walk")
            local ticks = 0
            character:SetMover(function(state, command, collision, tuning, dt)
                walk(state, command, collision, tuning, dt)
                ticks += 1
                if ticks == 30 then
                    local above = state.Position + Vector3.new(0, 300, 0)
                    local went, normal = collision:Sweep(above, Vector3.new(0, -400, 0))
                    print(math.round(went * 400), normal)
                end
            end)
            """);

        level.Play().Tick(in Stand, 31);

        level.Errors.ShouldBeEmpty();
        level.Printed.ShouldBe(["300 0, 1, 0"]);
    }

    private static float FastestOver(MoverLevel level, in CharacterCommand command, int ticks)
    {
        float fastest = 0f;
        for (int i = 0; i < ticks; i++)
        {
            level.Tick(in command);
            fastest = MathF.Max(fastest, level.State.Velocity.Length());
        }

        return fastest;
    }
}
