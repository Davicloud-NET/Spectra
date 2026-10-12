using static SpectraEngine.Scripting.Tests.PlayerScripts;

namespace SpectraEngine.Scripting.Tests;

/// <summary>
/// What a script does with the player on every tick may not allocate: a
/// read, a write through the door and a teleport.
/// </summary>
public sealed class PlayerCostTests
{
    private const int Rounds = 500;
    private const int WarmTicks = 30;
    private const int Ticks = 60;

    [Theory]
    [InlineData("local _ = Players.LocalPlayer")]
    [InlineData("local _ = Players.LocalPlayer.Character")]
    [InlineData("local _ = character.Position")]
    [InlineData("local _ = character.CFrame")]
    [InlineData("local _ = character.Velocity")]
    [InlineData("local _ = character.WalkSpeed")]
    [InlineData("local _ = character.IsGrounded")]
    [InlineData("character.WalkSpeed = 5")]
    [InlineData("character.JumpHeight = 2")]
    [InlineData("character.Velocity = still")]
    [InlineData("character:Teleport(place)")]
    [InlineData("character:Teleport(frame)")]
    [InlineData("character:Respawn()")]
    public void A_use_of_the_player_allocates_nothing_on_the_managed_heap(string statement)
    {
        Allocated(statement).ShouldBe(0, $"{Ticks * Rounds} rounds of '{statement}' allocated");
    }

    private static long Allocated(string statement)
    {
        using var level = new ScriptLevel();
        level.AddScript(level.Scene.Root, $$"""
            {{Character}}
            local still = Vector3.zero
            local place = Vector3.new(3, 0.05, 3)
            local frame = CFrame.new(-3, 0.05, 3) * CFrame.Angles(0, 1, 0)
            game:GetService("RunService").Heartbeat:Connect(function()
                for i = 1, {{Rounds}} do
                    {{statement}}
                end
            end)
            """);
        level.Play().Tick(WarmTicks);

        // The least of a few runs: the runtime compiles a method again once
        // it has run often, and that one time is no cost of the script's.
        long allocated = long.MaxValue;
        for (int run = 0; run < 3; run++)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            level.Tick(Ticks);
            allocated = Math.Min(allocated, GC.GetAllocatedBytesForCurrentThread() - before);
        }

        level.Errors.ShouldBeEmpty();
        return allocated;
    }
}
