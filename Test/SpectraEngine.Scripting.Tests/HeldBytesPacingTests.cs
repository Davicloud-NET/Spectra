using SpectraEngine.Scripting.Luau.Runtime;

namespace SpectraEngine.Scripting.Tests;

/// <summary>
/// When the destroyed nodes a script holds make Luau's collector step: past
/// the goal, by whole kilobytes, and not for the step every so many ticks.
/// </summary>
public sealed class HeldBytesPacingTests
{
    // More than the ticks between two steps the collector takes by itself.
    private const int IdleTicks = 70;

    [Fact]
    public void A_gain_too_small_to_step_for_is_owed_and_ends_no_cycle()
    {
        // Two bytes a character, so the part ends up this far under the goal.
        const int Short = 16;
        long room = HeldNodes.FirstGoal - 200 - WeightWithBlob(Short);
        int characters = Short + (int)(room / 2);

        using var level = new ScriptLevel(hasCharacter: false);
        level.AddScript(level.Scene.Root, WithBlob(characters) + """
            task.wait()
            for index = 1, 4 do
                part:SetAttribute("N" .. index, index)
            end
            task.wait()
            for index = 5, 16 do
                part:SetAttribute("N" .. index, index)
            end
            """, "Main");

        LuauScriptRuntime runtime = level.Play().Tick().Runtime.ShouldNotBeNull();
        runtime.HeldDestroyedBytes.ShouldBe(HeldNodes.FirstGoal - 200);

        level.Tick();

        level.Errors.ShouldBeEmpty();
        runtime.HeldDestroyedBytes.ShouldBeInRange(HeldNodes.FirstGoal, HeldNodes.FirstGoal + 1023);
        runtime.HeldStepCount.ShouldBe(0);
        runtime.HeldCollectionCount.ShouldBe(0, "a cycle that never began was counted as ended");
        runtime.HeldGoalBytes.ShouldBe(HeldNodes.FirstGoal);

        level.Tick();

        level.Errors.ShouldBeEmpty();
        runtime.HeldStepCount.ShouldBeGreaterThan(0, "what was owed never made the collector step");
    }

    [Fact]
    public void A_script_that_holds_much_for_long_is_not_stepped_for_each_small_part_it_destroys()
    {
        using var level = new ScriptLevel(hasCharacter: false);
        level.AddScript(level.Scene.Root, $$"""
            -- Enough of Luau's own that one step does not end a cycle.
            local ballast = table.create(50000)
            for index = 1, 50000 do
                ballast[index] = { index }
            end

            local blob = string.rep("x", 65536)
            local kept = {}
            for index = 1, 12 do
                local part = Instance.new("Part")
                part:SetAttribute("Blob", blob)
                part:Destroy()
                kept[index] = part
            end
            for tick = 1, {{IdleTicks}} do
                task.wait()
            end
            for index = 1, 20 do
                Instance.new("Part"):Destroy()
            end
            print("done")
            """, "Main");

        LuauScriptRuntime runtime = level.Play().Tick(IdleTicks).Runtime.ShouldNotBeNull();
        level.Printed.ShouldBeEmpty();
        int steps = runtime.HeldStepCount;
        (runtime.HeldGoalBytes - runtime.HeldDestroyedBytes).ShouldBeGreaterThan(
            20 * 2 * NodeWeight.OfNode, "the test wants room under the goal for its small parts");

        level.Tick(2);

        level.Errors.ShouldBeEmpty();
        level.Printed.ShouldBe(["done"]);
        runtime.HeldDestroyedCount.ShouldBeGreaterThanOrEqualTo(12);
        (runtime.HeldStepCount - steps).ShouldBe(0, "the step every so many ticks switched the goal off");
    }

    // An attribute takes 64 KiB of text and no more, so there are two.
    private static string WithBlob(int characters) => $"""
        local part = Instance.new("Part")
        part:SetAttribute("Full", string.rep("x", 65536))
        part:SetAttribute("Blob", string.rep("x", {characters}))
        part:Destroy()

        """;

    private static long WeightWithBlob(int characters)
    {
        using var level = new ScriptLevel(hasCharacter: false);
        level.AddScript(level.Scene.Root, WithBlob(characters) + "task.wait(100)\nprint(part)\n", "Main");
        return level.Play().Tick().Runtime.ShouldNotBeNull().HeldDestroyedBytes;
    }
}
