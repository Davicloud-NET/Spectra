using System.Numerics;
using SpectraEngine.Core.Scene;

namespace SpectraEngine.Scripting.Tests;

/// <summary>
/// What a script costs the managed heap on every tick. A tick is in the frame
/// loop, where nothing may allocate.
/// </summary>
public sealed class ScriptAllocationTests
{
    private const int Ticks = 600;

    private const string TurnsOnHeartbeat = """
        local part = script.Parent
        game:GetService("RunService").Heartbeat:Connect(function(step)
            part.CFrame = part.CFrame * CFrame.Angles(0, step, 0)
        end)
        """;

    private const string MovesInAWaitLoop = """
        local part = script.Parent
        while true do
            task.wait()
            part.Position += Vector3.new(0, 0.001, 0)
        end
        """;

    [Theory]
    [InlineData(TurnsOnHeartbeat)]
    [InlineData(MovesInAWaitLoop)]
    public void A_script_that_moves_a_part_every_tick_allocates_nothing_once_it_runs(string script)
    {
        // Nobody walks: a character's own tick is not what is measured here.
        using var level = new ScriptLevel(hasCharacter: false);
        SceneNode part = level.Part("Mover", new Vector3(0f, 2f, 0f));
        level.AddScript(part, script);

        // Long enough for every table to reach its size and for Luau's
        // collector to have let go of userdata.
        level.Play().Tick(Ticks);
        Transform warm = part.LocalTransform;

        long before = GC.GetAllocatedBytesForCurrentThread();
        level.Tick(Ticks);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        level.Errors.ShouldBeEmpty();
        (part.LocalTransform.Position == warm.Position && part.LocalTransform.Rotation == warm.Rotation)
            .ShouldBeFalse("the script did not move the part");
        allocated.ShouldBe(0, $"{Ticks} ticks of the script allocated on the managed heap");
    }
}
