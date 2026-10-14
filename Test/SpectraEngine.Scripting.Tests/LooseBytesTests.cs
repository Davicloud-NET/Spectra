using SpectraEngine.Core.Scripting;
using SpectraEngine.Scripting.Luau.Runtime;

namespace SpectraEngine.Scripting.Tests;

/// <summary>
/// Made nodes in no level that a script let go of, with long attributes on
/// them. They are looked for by the bytes scripts set on them, and not only
/// when enough trees have piled up. Nothing here asks for a collection.
/// </summary>
public sealed class LooseBytesTests
{
    private const long Megabyte = 1024 * 1024;

    [Fact]
    public void A_loop_that_lets_go_of_loose_parts_with_a_long_attribute_keeps_few_of_them_waiting()
    {
        using var level = new ScriptLevel(hasCharacter: false);
        level.AddScript(level.Scene.Root, """
            local blob = string.rep("x", 65536)
            for index = 1, 2000 do
                local part = Instance.new("Part")
                part:SetAttribute("Blob", blob)
                part:AddTag("Loose")
            end
            print("done")
            """, "Main");

        LuauScriptRuntime runtime = level.Play().Tick().Runtime.ShouldNotBeNull();

        level.Errors.ShouldBeEmpty();
        level.Printed.ShouldBe(["done"]);
        runtime.PeakLooseTreeCount.ShouldBeLessThan(32);
        runtime.PeakNodeBytes.ShouldBeLessThan(2 * Megabyte);
    }

    [Fact]
    public void Loose_parts_let_go_of_on_every_tick_do_not_pile_up()
    {
        using var level = new ScriptLevel(hasCharacter: false);
        level.AddScript(level.Scene.Root, """
            local blob = string.rep("x", 65536)
            game:GetService("RunService").Heartbeat:Connect(function()
                for index = 1, 10 do
                    local part = Instance.new("Part")
                    part:SetAttribute("Blob", blob)
                end
            end)
            """, "Main");
        LuauScriptRuntime runtime = level.Play().Tick().Runtime.ShouldNotBeNull();

        int mostWaiting = 0;
        for (int tick = 0; tick < 200; tick++)
        {
            level.Tick();
            mostWaiting = Math.Max(mostWaiting, runtime.LooseTreeCount);
        }

        level.Errors.ShouldBeEmpty();
        mostWaiting.ShouldBeLessThan(32);
        runtime.PeakNodeBytes.ShouldBeLessThan(2 * Megabyte);
    }

    // The collector goes round many times while the script holds them.
    [Fact]
    public void Loose_parts_a_script_keeps_keep_their_attributes()
    {
        using var level = new ScriptLevel(hasCharacter: false);
        level.AddScript(level.Scene.Root, """
            local blob = string.rep("x", 65536)
            local kept = {}
            for index = 1, 40 do
                local part = Instance.new("Part")
                part:SetAttribute("Blob", blob)
                part:SetAttribute("Index", index)
                kept[index] = part
            end
            for index = 1, 400 do
                local part = Instance.new("Part")
                part:SetAttribute("Blob", blob)
            end
            task.wait()
            local sum = 0
            for index = 1, 40 do
                assert(#kept[index]:GetAttribute("Blob") == 65536)
                sum += kept[index]:GetAttribute("Index")
            end
            print(sum)
            """, "Main");

        LuauScriptRuntime runtime = level.Play().Tick(2).Runtime.ShouldNotBeNull();

        level.Errors.ShouldBeEmpty();
        level.Printed.ShouldBe(["820"]);
        runtime.LooseTreeCount.ShouldBeGreaterThanOrEqualTo(40);
    }

    // The script makes nothing more, so only the collector's step every so
    // many ticks finds them.
    [Fact]
    public void Loose_parts_a_script_lets_go_of_are_found_without_another_part_made()
    {
        using var level = new ScriptLevel(hasCharacter: false);
        level.AddScript(level.Scene.Root, """
            local blob = string.rep("x", 65536)
            local kept = {}
            for index = 1, 20 do
                local part = Instance.new("Part")
                part:SetAttribute("Blob", blob)
                kept[index] = part
            end
            task.wait()
            kept = nil
            """, "Main");
        LuauScriptRuntime runtime = level.Play().Tick().Runtime.ShouldNotBeNull();
        runtime.LooseTreeCount.ShouldBe(20);

        int ticks = 0;
        while (runtime.LooseTreeCount > 0 && ticks < 20000)
        {
            level.Tick();
            ticks++;
        }

        level.Errors.ShouldBeEmpty();
        runtime.LooseTreeCount.ShouldBe(0, "nothing looked for what the script let go of");
        runtime.NodeBytes.ShouldBe(0);
    }

    [Fact]
    public void A_script_that_keeps_loose_parts_past_the_budget_is_switched_off()
    {
        using var level = new ScriptLevel(hasCharacter: false);
        level.AddScript(level.Scene.Root, """
            task.wait()
            local blob = string.rep("x", 65536)
            local kept = {}
            while true do
                local part = Instance.new("Part")
                part:SetAttribute("Blob", blob)
                kept[#kept + 1] = part
            end
            """, "Main");
        LuauScriptRuntime runtime = level.Play().Runtime.ShouldNotBeNull();
        runtime.MemoryBudget = 8 * Megabyte;

        level.Tick(2);

        level.Errors.ShouldHaveSingleItem().ShouldBe(
            "the scripts hold more than 8 MB, with the tags and attributes they set on nodes, and this one was switched off");

        // The script ended, and its parts went with it.
        runtime.Collect();
        runtime.LooseTreeCount.ShouldBe(0);
        runtime.NodeBytes.ShouldBe(0);
    }

    // What one script let go of is no garbage to Luau's collector, so the
    // interrupt cannot take it off. Another script that builds up Luau data
    // before the parts are found is not stopped for them.
    [Fact]
    public void Loose_parts_a_script_let_go_of_do_not_stop_another_script()
    {
        using var level = new ScriptLevel(hasCharacter: false);
        level.AddScript(level.Scene.Root, """
            local blob = string.rep("x", 65536)
            local kept = {}
            for index = 1, 50 do
                local part = Instance.new("Part")
                part:SetAttribute("Blob", blob)
                kept[index] = part
            end
            task.wait()
            kept = nil
            """, "Keeper");
        level.AddScript(level.Scene.Root, """
            task.wait()
            task.wait()
            local data = string.rep("y", 5 * 1024 * 1024)
            print("built")
            task.wait()
            print(#data)
            """, "Builder");
        LuauScriptRuntime runtime = level.Play().Runtime.ShouldNotBeNull();
        runtime.MemoryBudget = 8 * Megabyte;

        level.Tick(4);

        level.Errors.ShouldBeEmpty();
        level.Printed.ShouldBe(["built", "5242880"]);
        runtime.NodeBytes.ShouldBe(0);
    }

    // The interrupt lets a script on for loose parts only until they have
    // been looked over. Those still held then count in full.
    [Fact]
    public void Loose_parts_a_script_holds_still_stop_a_script_past_the_budget()
    {
        using var level = new ScriptLevel(hasCharacter: false);
        level.AddScript(level.Scene.Root, """
            local blob = string.rep("x", 65536)
            local kept = {}
            for index = 1, 50 do
                local part = Instance.new("Part")
                part:SetAttribute("Blob", blob)
                kept[index] = part
            end
            while true do
                task.wait()
            end
            """, "Keeper");
        level.AddScript(level.Scene.Root, """
            task.wait()
            task.wait()
            local data = string.rep("y", 5 * 1024 * 1024)
            print("built")
            task.wait()
            local more = {}
            for index = 1, 200000 do
                more[index] = index
            end
            print("more")
            """, "Builder");
        LuauScriptRuntime runtime = level.Play().Runtime.ShouldNotBeNull();
        runtime.MemoryBudget = 8 * Megabyte;

        level.Tick(5);

        level.Errors.ShouldHaveSingleItem().ShouldBe(
            "the scripts hold more than 8 MB, with the tags and attributes they set on nodes, and this one was switched off");
        level.Printed.ShouldNotContain("more");
        runtime.LooseTreeCount.ShouldBe(50);
    }

    // Only a tree that leaves the level for the first time adds what it
    // keeps to what is looked for.
    [Fact]
    public void A_made_model_hidden_and_shown_on_every_tick_asks_for_no_collection()
    {
        using var level = new ScriptLevel(hasCharacter: false);
        level.AddScript(level.Scene.Root, """
            local model = Instance.new("Folder")
            for index = 1, 5 do
                local part = Instance.new("Part")
                part.Parent = model
                part:SetAttribute("Blob", string.rep("x", 65536))
            end
            model.Parent = workspace
            task.wait()
            game:GetService("RunService").Heartbeat:Connect(function()
                model.Parent = nil
                model.Parent = workspace
            end)
            """, "Main");
        LuauScriptRuntime runtime = level.Play().Tick(2).Runtime.ShouldNotBeNull();
        int before = runtime.LooseLookOverCount;

        level.Tick(100);

        level.Errors.ShouldBeEmpty();
        (runtime.LooseLookOverCount - before).ShouldBeLessThanOrEqualTo(1);
    }
}
