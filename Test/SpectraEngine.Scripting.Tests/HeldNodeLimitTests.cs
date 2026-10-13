using SpectraEngine.Core.Scripting;
using SpectraEngine.Scripting.Luau.Runtime;

namespace SpectraEngine.Scripting.Tests;

/// <summary>
/// What a script holds of a destroyed node keeps the whole node. So a node
/// a script made counts against the bound for as long as a script holds it,
/// destroyed or not, and stops counting when the last one lets go.
/// </summary>
public sealed class HeldNodeLimitTests
{
    [Fact]
    public void A_loop_that_destroys_every_part_it_makes_and_keeps_each_is_stopped_by_the_bound()
    {
        using ScriptLevel level = House.With("""
            local kept = {}
            while true do
                local part = Instance.new("Part")
                part:Destroy()
                kept[#kept + 1] = part
            end
            """);

        level.Play().Tick();

        ScriptMessage error = level.Sink.Messages.Single(message => message.Severity == ScriptSeverity.Error);
        error.Line.ShouldBe(3);
        error.Text.ShouldBe(
            $"Instance.new made nothing: scripts would have made more than {TreeLimits.MaxMadeNodes} nodes that are " +
            "still there or still held, and a level holds no more. Destroy the ones that are done with, and keep " +
            "none of them in a variable");

        // The script was ended, and with it went the table that held them.
        LuauScriptRuntime runtime = level.Runtime.ShouldNotBeNull();
        runtime.Collect();
        new ScriptWrites(level.Entities).Tree.MadeCount.ShouldBe(0);
        runtime.HeldDestroyedCount.ShouldBe(0);
    }

    [Fact]
    public void A_destroyed_part_no_script_holds_any_more_makes_room_for_another()
    {
        using ScriptLevel level = House.With("""
            for i = 1, 40000 do
                local part = Instance.new("Part")
                part:Destroy()
            end
            print("done")
            """);

        level.Play().Tick();

        level.Errors.ShouldBeEmpty();
        level.Printed.ShouldBe(["done"]);
        LuauScriptRuntime runtime = level.Runtime.ShouldNotBeNull();
        runtime.Collect();
        new ScriptWrites(level.Entities).Tree.MadeCount.ShouldBe(0);
    }

    [Fact]
    public void A_destroyed_part_a_script_holds_counts_until_the_script_lets_go_of_it()
    {
        using ScriptLevel level = House.With("""
            local kept = {}
            for i = 1, 300 do
                kept[i] = Instance.new("Part", workspace)
                kept[i]:Destroy()
            end
            task.wait(0.1)
            print(#kept, kept[300].Parent)
            kept = nil
            task.wait(0.1)
            print("let go")
            """);
        LuauScriptRuntime runtime = level.Play().Tick(2).Runtime.ShouldNotBeNull();
        TreeWrites tree = new ScriptWrites(level.Entities).Tree;

        runtime.Collect();
        tree.MadeCount.ShouldBe(300);
        runtime.HeldDestroyedCount.ShouldBe(300);

        level.Tick(20);
        runtime.Collect();

        level.Errors.ShouldBeEmpty();
        level.Printed.ShouldBe(["300 nil", "let go"]);
        tree.MadeCount.ShouldBe(0);
        runtime.HeldDestroyedCount.ShouldBe(0);
    }

    [Fact]
    public void A_destroyed_part_the_level_hands_to_a_listener_after_the_last_script_let_go_counts_again()
    {
        using ScriptLevel level = House.With("""
            local CollectionService = game:GetService("CollectionService")
            local kept = {}
            CollectionService:GetInstanceRemovedSignal("Spawned"):Connect(function(node) kept[#kept + 1] = node end)
            for i = 1, 5 do
                local model = Instance.new("Folder", workspace)
                model.Name = "Model" .. i
                Instance.new("Part", model):AddTag("Spawned")
            end
            task.wait(0.1)
            for i = 1, 5 do
                workspace["Model" .. i]:Destroy()
            end
            task.wait(0.1)
            print(#kept, kept[5].Parent)
            """);
        LuauScriptRuntime runtime = level.Play().Tick(2).Runtime.ShouldNotBeNull();
        TreeWrites tree = new ScriptWrites(level.Entities).Tree;

        // No script holds a part now. Each is in the level, so it counts.
        runtime.Collect();
        tree.MadeCount.ShouldBe(10);

        level.Tick(20);
        runtime.Collect();

        level.Errors.ShouldBeEmpty();
        level.Printed.ShouldBe(["5 nil"]);
        tree.MadeCount.ShouldBe(5, "the five parts the listener keeps");
        runtime.HeldDestroyedCount.ShouldBe(5);
    }

    [Fact]
    public void A_part_that_was_let_go_of_and_then_destroyed_by_a_listener_is_taken_off_the_count_once()
    {
        using ScriptLevel level = House.With("""
            local CollectionService = game:GetService("CollectionService")
            CollectionService:GetInstanceRemovedSignal("Spawned"):Connect(function(node) node:Destroy() end)
            local part = Instance.new("Part", workspace)
            part:AddTag("Spawned")
            part.Parent = nil
            part = nil

            -- Enough loose nodes for the runtime to look for those no script holds.
            for i = 1, 200 do
                Instance.new("Folder")
            end
            task.wait(0.1)
            print("done")
            """);

        level.Play().Tick(10);
        LuauScriptRuntime runtime = level.Runtime.ShouldNotBeNull();
        runtime.Collect();

        level.Errors.ShouldBeEmpty();
        level.Printed.ShouldBe(["done"]);
        new ScriptWrites(level.Entities).Tree.MadeCount.ShouldBe(0);
        runtime.HeldDestroyedCount.ShouldBe(0);
        runtime.LooseTreeCount.ShouldBe(0);
    }

    // A destroyed node keeps its tags and attributes while a script holds
    // it. The bound on made nodes is what bounds them too.
    [Fact]
    public void A_loop_that_keeps_every_destroyed_part_with_a_tag_and_an_attribute_on_it_is_stopped_by_the_bound()
    {
        using ScriptLevel level = House.With("""
            local kept = {}
            while true do
                local part = Instance.new("Part")
                part:Destroy()
                part:AddTag("Kept")
                part:SetAttribute("Note", "kept after Destroy")
                kept[#kept + 1] = part
            end
            """);

        level.Play().Tick();

        ScriptMessage error = level.Sink.Messages.Single(message => message.Severity == ScriptSeverity.Error);
        error.Line.ShouldBe(3);
        error.Text.ShouldStartWith(
            $"Instance.new made nothing: scripts would have made more than {TreeLimits.MaxMadeNodes} nodes");

        LuauScriptRuntime runtime = level.Runtime.ShouldNotBeNull();
        runtime.Collect();
        new ScriptWrites(level.Entities).Tree.MadeCount.ShouldBe(0);
        runtime.HeldDestroyedCount.ShouldBe(0);
    }

    [Fact]
    public void A_destroyed_part_no_script_holds_any_more_makes_room_whatever_it_was_left_holding()
    {
        using ScriptLevel level = House.With("""
            for i = 1, 40000 do
                local part = Instance.new("Part")
                part:AddTag("Early")
                part:Destroy()
                part:AddTag("Late")
                part:SetAttribute("Index", i)
            end
            print("done")
            """);

        level.Play().Tick();

        level.Errors.ShouldBeEmpty();
        level.Printed.ShouldBe(["done"]);
        LuauScriptRuntime runtime = level.Runtime.ShouldNotBeNull();
        runtime.Collect();
        new ScriptWrites(level.Entities).Tree.MadeCount.ShouldBe(0);
        runtime.HeldDestroyedCount.ShouldBe(0);
        runtime.TaggedNodeCount.ShouldBe(0);
    }
}
