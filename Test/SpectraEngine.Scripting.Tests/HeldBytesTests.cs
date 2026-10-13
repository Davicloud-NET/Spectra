using SpectraEngine.Core.Scripting;
using SpectraEngine.Scripting.Luau;
using SpectraEngine.Scripting.Luau.Runtime;

namespace SpectraEngine.Scripting.Tests;

/// <summary>
/// What a destroyed node keeps while a script holds it, in bytes. Luau's
/// collector counts the handle and not the node, so it is told what the node
/// weighs. Nothing here asks for a collection: the bounds hold by themselves.
/// </summary>
public sealed class HeldBytesTests
{
    private const long Megabyte = 1024 * 1024;

    // A 64 KiB string is 128 KiB of text in memory.
    private const long BlobBytes = 2 * 65536;

    [Fact]
    public void A_loop_that_destroys_parts_with_a_long_attribute_keeps_few_of_them_waiting()
    {
        using var level = new ScriptLevel(hasCharacter: false);
        level.AddScript(level.Scene.Root, """
            local blob = string.rep("x", 16384)
            for index = 1, 2000 do
                local part = Instance.new("Part")
                part:SetAttribute("Blob", blob)
                part.Parent = workspace
                part:Destroy()
            end
            print("done")
            """, "Main");

        LuauScriptRuntime runtime = level.Play().Tick().Runtime.ShouldNotBeNull();

        level.Errors.ShouldBeEmpty();
        level.Printed.ShouldBe(["done"]);
        runtime.PeakHeldDestroyedBytes.ShouldBeLessThan(Megabyte);
        runtime.PeakHeldDestroyedCount.ShouldBeLessThan(32);
    }

    [Fact]
    public void Parts_destroyed_on_every_tick_do_not_pile_up_while_Luau_has_little_garbage_of_its_own()
    {
        using var level = new ScriptLevel(hasCharacter: false);
        level.AddScript(level.Scene.Root, """
            local blob = string.rep("x", 16384)
            game:GetService("RunService").Heartbeat:Connect(function()
                for index = 1, 10 do
                    local part = Instance.new("Part")
                    part:SetAttribute("Blob", blob)
                    part:AddTag("Spawned")
                    part.Parent = workspace
                    part:Destroy()
                end
            end)
            """, "Main");
        LuauScriptRuntime runtime = level.Play().Tick().Runtime.ShouldNotBeNull();

        int mostWaiting = 0;
        for (int tick = 0; tick < 200; tick++)
        {
            level.Tick();
            mostWaiting = Math.Max(mostWaiting, runtime.HeldDestroyedCount);
        }

        level.Errors.ShouldBeEmpty();
        mostWaiting.ShouldBeLessThan(32);
        runtime.PeakHeldDestroyedBytes.ShouldBeLessThan(Megabyte);
    }

    [Fact]
    public void Small_parts_wait_up_to_the_first_goal_and_no_further()
    {
        using var level = new ScriptLevel(hasCharacter: false);
        level.AddScript(level.Scene.Root, """
            for index = 1, 20000 do
                local part = Instance.new("Part")
                part:SetAttribute("Index", index)
                part:Destroy()
            end
            print("done")
            """, "Main");

        LuauScriptRuntime runtime = level.Play().Tick().Runtime.ShouldNotBeNull();

        level.Printed.ShouldBe(["done"]);
        runtime.PeakHeldDestroyedBytes.ShouldBeLessThan(3 * HeldNodes.FirstGoal);
        runtime.PeakHeldDestroyedCount.ShouldBeLessThan(1000);
    }

    [Fact]
    public void A_script_that_keeps_destroyed_parts_reads_them_after_the_collector_ran_and_is_not_collected_for_each_part()
    {
        using var level = new ScriptLevel(hasCharacter: false);
        level.AddScript(level.Scene.Root, """
            local blob = string.rep("x", 65536)
            local kept = {}
            for index = 1, 40 do
                local part = Instance.new("Part")
                part:SetAttribute("Blob", blob)
                part:SetAttribute("Index", index)
                part:AddTag("Kept")
                part:Destroy()
                kept[index] = part
            end
            print("kept")
            task.wait()
            for index = 1, 40 do
                local part = Instance.new("Part")
                part:Destroy()
                kept[40 + index] = part
            end
            local sum = 0
            for index = 1, 40 do
                assert(#kept[index]:GetAttribute("Blob") == 65536 and kept[index]:HasTag("Kept"))
                sum += kept[index]:GetAttribute("Index")
            end
            print(sum)
            """, "Main");

        LuauScriptRuntime runtime = level.Play().Tick().Runtime.ShouldNotBeNull();
        level.Printed.ShouldBe(["kept"]);
        runtime.HeldDestroyedCount.ShouldBe(40);
        runtime.HeldDestroyedBytes.ShouldBeGreaterThan(40 * BlobBytes);
        int cycles = runtime.HeldCollectionCount;
        cycles.ShouldBeGreaterThan(0);

        level.Tick(2);

        level.Errors.ShouldBeEmpty();
        level.Printed.ShouldBe(["kept", "820"]);
        runtime.HeldDestroyedCount.ShouldBe(80);
        (runtime.HeldCollectionCount - cycles).ShouldBeLessThanOrEqualTo(1, "the goal did not rise with what the script holds");
    }

    // The script makes no garbage and destroys nothing more, so nothing but
    // the step every so many ticks has Luau look.
    [Fact]
    public void Destroyed_parts_a_script_lets_go_of_are_found_without_another_Destroy()
    {
        using var level = new ScriptLevel(hasCharacter: false);
        level.AddScript(level.Scene.Root, """
            local blob = string.rep("x", 65536)
            local kept = {}
            for index = 1, 20 do
                local part = Instance.new("Part")
                part:SetAttribute("Blob", blob)
                part:Destroy()
                kept[index] = part
            end
            task.wait()
            kept = nil
            """, "Main");
        LuauScriptRuntime runtime = level.Play().Tick().Runtime.ShouldNotBeNull();
        runtime.HeldDestroyedCount.ShouldBe(20);

        int ticks = 0;
        while (runtime.HeldDestroyedCount > 0 && ticks < 20000)
        {
            level.Tick();
            ticks++;
        }

        level.Errors.ShouldBeEmpty();
        runtime.HeldDestroyedCount.ShouldBe(0, "nothing looked for what the script let go of");
        runtime.HeldDestroyedBytes.ShouldBe(0);
    }

    [Fact]
    public void A_script_that_keeps_more_in_destroyed_parts_than_the_memory_budget_is_switched_off_and_told_why()
    {
        using var level = new ScriptLevel(hasCharacter: false);
        level.AddScript(level.Scene.Root, """
            local blob = string.rep("x", 65536)
            local kept = {}
            while true do
                local part = Instance.new("Part")
                part:SetAttribute("Blob", blob)
                part:Destroy()
                kept[#kept + 1] = part
            end
            """, "Main");
        level.Play();
        LuauScriptRuntime runtime = level.Runtimes.ShouldHaveSingleItem();
        runtime.MemoryBudget = 8 * Megabyte;

        level.Tick();

        ScriptMessage error = level.Sink.Messages.Single(message => message.Severity == ScriptSeverity.Error);
        error.Text.ShouldBe(
            "the scripts hold more than 8 MB, with what the destroyed nodes they keep hold, and this one was switched off");
        error.Line.ShouldBeInRange(3, 8);
        runtime.PeakHeldDestroyedBytes.ShouldBeInRange(7 * Megabyte, 9 * Megabyte);
        runtime.LuauBytes.ShouldBeLessThan(2 * Megabyte, "Luau's own count is far under the budget");

        // The script ended, and its table went with it.
        runtime.Collect();
        runtime.HeldDestroyedCount.ShouldBe(0);
        runtime.HeldDestroyedBytes.ShouldBe(0);
    }

    [Fact]
    public void What_a_script_sets_on_a_destroyed_part_it_holds_is_weighed_too()
    {
        using var level = new ScriptLevel(hasCharacter: false);
        level.AddScript(level.Scene.Root, """
            local part = Instance.new("Part")
            part:Destroy()
            task.wait()
            part:SetAttribute("Blob", string.rep("x", 65536))
            part:AddTag("Late")
            task.wait()
            part:SetAttribute("Blob", nil)
            task.wait()
            print(part:HasTag("Late"), part:GetAttribute("Blob"))
            """, "Main");

        LuauScriptRuntime runtime = level.Play().Tick().Runtime.ShouldNotBeNull();
        long bare = runtime.HeldDestroyedBytes;
        level.Tick();
        long full = runtime.HeldDestroyedBytes;
        level.Tick();
        long emptied = runtime.HeldDestroyedBytes;
        level.Tick();

        level.Errors.ShouldBeEmpty();
        level.Printed.ShouldBe(["true nil"]);
        bare.ShouldBe(NodeWeight.OfNode);
        long name = NodeWeight.OfText + (2 * "Blob".Length);
        long tag = NodeWeight.OfTag + (2 * "Late".Length);
        (full - bare).ShouldBe(
            NodeWeight.OfAttributes + NodeWeight.OfAttribute + name + NodeWeight.OfText + BlobBytes + tag);
        emptied.ShouldBe(bare + tag);
    }

    // A tag no node of the level has is kept by the destroyed part alone.
    [Fact]
    public void Tags_and_attribute_names_on_a_destroyed_part_are_weighed_with_their_text()
    {
        using var level = new ScriptLevel(hasCharacter: false);
        level.AddScript(level.Scene.Root, """
            local part = Instance.new("Part")
            part:Destroy()
            task.wait()
            for index = 1, 100 do
                part:AddTag(string.rep("t", 200) .. index)
                part:SetAttribute(string.rep("n", 60) .. index, index)
            end
            """, "Main");

        LuauScriptRuntime runtime = level.Play().Tick().Runtime.ShouldNotBeNull();
        long bare = runtime.HeldDestroyedBytes;
        level.Tick();

        level.Errors.ShouldBeEmpty();
        long tags = 100 * (NodeWeight.OfTag + (2 * 200));
        long names = 100 * (NodeWeight.OfAttribute + NodeWeight.OfText + (2 * 60));
        (runtime.HeldDestroyedBytes - bare).ShouldBeGreaterThan(tags + names);
    }

    // The part's handle goes while the part still has its place among the
    // objects scripts held, and the listener is handed a new one.
    [Fact]
    public void A_destroyed_part_handed_out_again_after_Luau_collected_its_handle_is_weighed_again()
    {
        using var level = new ScriptLevel(hasCharacter: false);
        level.AddScript(level.Scene.Root, """
            local kept
            game:GetService("CollectionService"):GetInstanceRemovedSignal("Enemy"):Connect(function(enemy)
                kept = enemy
                print(#enemy:GetAttribute("Blob"))
            end)

            local function muster()
                local enemy = Instance.new("Part")
                enemy.Name = "Enemy"
                enemy:AddTag("Enemy")
                enemy:SetAttribute("Blob", string.rep("x", 65536))
                enemy.Parent = workspace
            end

            local function destroy()
                workspace.Enemy:Destroy()
            end

            -- Garbage of Luau's own, so its collector goes round by itself.
            local function litter()
                for index = 1, 200000 do
                    local scrap = { index }
                end
            end

            muster()
            task.wait()
            destroy()
            litter()
            """, "Main");

        LuauScriptRuntime runtime = level.Play().Tick(3).Runtime.ShouldNotBeNull();

        level.Errors.ShouldBeEmpty();
        level.Printed.ShouldBe(["65536"]);
        runtime.HeldDestroyedCount.ShouldBe(1);
        runtime.HeldDestroyedBytes.ShouldBeGreaterThan(BlobBytes, "the part's new handle weighs nothing");
    }

    [Fact]
    public void A_destroyed_part_weighs_nothing_once_Luau_has_collected_it()
    {
        using var level = new ScriptLevel(hasCharacter: false);
        level.AddScript(level.Scene.Root, """
            local part = Instance.new("Part")
            part:Destroy()
            task.wait()
            part = nil
            """, "Main");
        LuauScriptRuntime runtime = level.Play().Tick().Runtime.ShouldNotBeNull();
        runtime.HeldDestroyedBytes.ShouldBe(NodeWeight.OfNode);

        level.Tick();
        runtime.Collect();

        runtime.HeldDestroyedBytes.ShouldBe(0);
        runtime.HeldDestroyedCount.ShouldBe(0);
    }

    [Fact]
    public void The_next_play_starts_with_nothing_counted_and_counts_the_same()
    {
        using var level = new ScriptLevel(hasCharacter: false);
        level.AddScript(level.Scene.Root, """
            local kept = {}
            for index = 1, 10 do
                kept[index] = Instance.new("Part")
                kept[index]:SetAttribute("Blob", string.rep("x", 65536))
                kept[index]:Destroy()
            end
            task.wait(100)
            print(#kept)
            """, "Main");
        LuauScriptRuntime runtime = level.Play().Tick().Runtime.ShouldNotBeNull();
        long counted = runtime.HeldDestroyedBytes;
        counted.ShouldBeGreaterThan(10 * BlobBytes);

        level.Stop();
        runtime.DestroyedAttributeCount.ShouldBe(0);
        LuauScriptRuntime next = level.Play().Runtime.ShouldNotBeNull();

        next.ShouldNotBeSameAs(runtime);
        next.HeldDestroyedBytes.ShouldBe(0);
        level.Tick();
        next.HeldDestroyedBytes.ShouldBe(counted);
    }
}
