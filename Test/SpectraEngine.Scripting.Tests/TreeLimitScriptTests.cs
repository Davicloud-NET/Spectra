using SpectraEngine.Core.Scripting;
using SpectraEngine.Scripting.Luau.Runtime;

namespace SpectraEngine.Scripting.Tests;

/// <summary>
/// A script in a loop cannot fill a level with nodes or build a tree too
/// deep to walk. Each bound stops it with an error at its line that names
/// the bound, and a node a script made and let go of does not count for good.
/// </summary>
public sealed class TreeLimitScriptTests
{
    [Fact]
    public void A_loop_that_makes_parts_without_end_is_stopped_at_its_line_by_the_bound_on_made_nodes()
    {
        using ScriptLevel level = House.With("""
            local made = 0
            while true do
                Instance.new("Folder").Parent = workspace
                made += 1
            end
            """);

        level.Play().Tick();

        ScriptMessage error = level.Sink.Messages.Single(message => message.Severity == ScriptSeverity.Error);
        error.Line.ShouldBe(3);
        error.Text.ShouldBe(
            $"Instance.new made nothing: scripts would have made more than {TreeLimits.MaxMadeNodes} nodes that are " +
            "still there, and a level holds no more. Destroy the ones that are done with");
        new ScriptWrites(level.Entities).Tree.MadeCount.ShouldBe(TreeLimits.MaxMadeNodes);
    }

    [Fact]
    public void A_clone_counts_every_node_it_copies()
    {
        using ScriptLevel level = House.With("""
            local house = workspace.House
            local copies = 0
            local made, why = pcall(function()
                while true do
                    house:Clone().Parent = workspace
                    copies += 1
                end
            end)
            print(copies, why)
            """);

        level.Play().Tick();

        // The house is nine nodes.
        level.Errors.ShouldBeEmpty();
        string line = level.Printed.ShouldHaveSingleItem();
        line.ShouldStartWith($"{TreeLimits.MaxMadeNodes / 9} ");
        line.ShouldEndWith(
            $"House was not cloned: with its copy, scripts would have made more than {TreeLimits.MaxMadeNodes} " +
            "nodes that are still there, and a level holds no more. Destroy the ones that are done with");
    }

    [Fact]
    public void A_node_that_is_destroyed_makes_room_for_another()
    {
        using ScriptLevel level = House.With("""
            for i = 1, 40000 do
                local part = Instance.new("Part")
                part.Parent = workspace
                part:Destroy()
            end
            print("done")
            """);

        level.Play().Tick();

        level.Errors.ShouldBeEmpty();
        level.Printed.ShouldBe(["done"]);
        new ScriptWrites(level.Entities).Tree.MadeCount.ShouldBe(0);
    }

    [Fact]
    public void A_node_a_script_made_and_let_go_of_stops_counting_once_no_script_holds_it()
    {
        using ScriptLevel level = House.With("""
            for i = 1, 40000 do
                local folder = Instance.new("Folder")
                Instance.new("Part", folder)
                folder:AddTag("Loose")
                folder:SetAttribute("Index", i)
            end
            print("done")
            """);

        level.Play().Tick();

        level.Errors.ShouldBeEmpty();
        level.Printed.ShouldBe(["done"]);
        LuauScriptRuntime runtime = level.Runtime.ShouldNotBeNull();
        runtime.LooseTreeCount.ShouldBeLessThan(200);
        runtime.TaggedNodeCount.ShouldBeLessThan(200);
        runtime.AttributeNodeCount.ShouldBeLessThan(200);
        new ScriptWrites(level.Entities).Tree.MadeCount.ShouldBeLessThan(400);
    }

    [Fact]
    public void A_made_node_a_script_still_holds_counts_though_it_is_in_no_level()
    {
        using ScriptLevel level = House.With("""
            local kept = {}
            for i = 1, 300 do
                kept[i] = Instance.new("Folder")
            end
            task.wait()
            print(#kept, kept[300].Name)
            """);

        level.Play().Tick(2);

        level.Errors.ShouldBeEmpty();
        level.Printed.ShouldBe(["300 Folder"]);
        new ScriptWrites(level.Entities).Tree.MadeCount.ShouldBe(300);
        level.Runtime.ShouldNotBeNull().LooseTreeCount.ShouldBe(300);
    }

    [Fact]
    public void A_chain_of_nodes_is_stopped_at_its_line_by_the_bound_on_depth()
    {
        using ScriptLevel level = House.With("""
            local deepest = workspace
            local depth = 1
            while true do
                local next = Instance.new("Folder")
                next.Parent = deepest
                deepest = next
                depth += 1
            end
            """);

        level.Play().Tick();

        ScriptMessage error = level.Sink.Messages.Single(message => message.Severity == ScriptSeverity.Error);
        error.Line.ShouldBe(5);
        error.Text.ShouldBe(
            $"Folder cannot get Folder as its parent: that would make the tree more than {TreeLimits.MaxDepth} " +
            "nodes deep, which is as deep as a tree may be");
    }

    [Fact]
    public void The_walks_of_a_tree_as_deep_as_it_goes_come_back()
    {
        using ScriptLevel level = House.With($$"""
            local deepest = workspace
            for level = 2, {{TreeLimits.MaxDepth}} do
                deepest = Instance.new("Folder", deepest)
            end
            deepest.Name = "Bottom"
            local top = workspace:GetChildren()[#workspace:GetChildren()]
            print(#top:GetDescendants(), workspace:FindFirstChild("Bottom", true) == deepest, deepest:IsDescendantOf(top))
            print(#deepest:GetFullName() > 1000, top:IsAncestorOf(deepest), deepest:FindFirstAncestor("Root") == workspace)
            local copy = top:Clone()
            copy.Parent = workspace
            top:Destroy()
            print(deepest.Parent, #copy:GetDescendants())
            """);
        var before = new LevelState(level.Scene);

        level.Play().Tick(2);
        level.Stop();

        level.Errors.ShouldBeEmpty();
        level.Printed.ShouldBe(["254 true true", "true true true", "nil 254"]);
        new LevelState(level.Scene).ShouldBe(before, "after a deep tree");
    }
}
