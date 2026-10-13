using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using SpectraEngine.Core.Scene;
using SpectraEngine.Scripting.Luau.Runtime;

namespace SpectraEngine.Scripting.Tests;

/// <summary>
/// How long tags and attributes last. They are the play session's: a node
/// that leaves the level drops out of its tags' lists, and Stop takes
/// everything away.
/// </summary>
public sealed class TagLifetimeTests
{
    // Tags three crates and says each tick what the tag lists and who it heard of.
    private const string Watcher = """
        local CollectionService = game:GetService("CollectionService")
        local heard = {}
        CollectionService:GetInstanceAddedSignal("Heavy"):Connect(function(node) table.insert(heard, "+" .. node.Name) end)
        CollectionService:GetInstanceRemovedSignal("Heavy"):Connect(function(node) table.insert(heard, "-" .. node.Name) end)
        local stack = workspace.Stack
        local crates = { stack.Low, stack.Low.Mid, stack.Low.Mid.Top }
        for _, crate in crates do
            crate:AddTag("Heavy")
        end
        while true do
            task.wait()
            local listed = {}
            for _, node in CollectionService:GetTagged("Heavy") do
                table.insert(listed, node.Name)
            end
            local has = {}
            for _, crate in crates do
                table.insert(has, tostring(crate:HasTag("Heavy")))
            end
            print(table.concat(listed, " ") .. " | " .. table.concat(heard, " ") .. " | " .. table.concat(has, " "))
            table.clear(heard)
        end
        """;

    [Fact]
    public void A_node_that_leaves_the_level_drops_out_of_the_list_and_fires_the_removed_signal()
    {
        using ScriptLevel level = Stacked(out SceneNode stack, out SceneNode low, out SceneNode mid);
        level.AddScript(level.Scene.Root, Watcher);
        level.Play().Tick(2);

        // The middle crate goes, and the one on it with it. Each keeps its tag.
        low.RemoveChild(mid);
        level.Tick();
        low.AddChild(mid);
        level.Tick();
        stack.RemoveChild(low);
        level.Tick();

        level.Errors.ShouldBeEmpty();
        level.Printed.ShouldBe(
        [
            "Low Mid Top | +Low +Mid +Top | true true true",
            "Low | -Mid -Top | true true true",
            "Low Mid Top | +Mid +Top | true true true",
            " | -Low -Mid -Top | true true true",
        ]);
    }

    [Fact]
    public void A_tag_put_on_a_node_outside_the_level_counts_once_the_node_comes_in()
    {
        using ScriptLevel level = Stacked(out SceneNode stack, out SceneNode low, out _);
        level.AddScript(level.Scene.Root, """
            local CollectionService = game:GetService("CollectionService")
            CollectionService:GetInstanceAddedSignal("Heavy"):Connect(function(node) print("added", node.Name) end)
            CollectionService:GetInstanceRemovedSignal("Heavy"):Connect(function(node) print("removed", node.Name) end)
            local low = workspace.Stack.Low
            task.wait()
            low:AddTag("Heavy")
            low:AddTag("Odd")
            low:RemoveTag("Odd")
            print(low:HasTag("Heavy"), #CollectionService:GetTagged("Heavy"), low:IsDescendantOf(game))
            task.wait()
            print(low:HasTag("Heavy"), #CollectionService:GetTagged("Heavy"), low:IsDescendantOf(game))
            """);
        level.Play().Tick();

        stack.RemoveChild(low);
        level.Tick();
        stack.AddChild(low);
        level.Tick();

        level.Errors.ShouldBeEmpty();
        level.Printed.ShouldBe(["true 0 false", "added Low", "true 1 true"]);
    }

    [Fact]
    public void Stop_takes_every_tag_and_attribute_away_and_the_next_play_starts_with_none()
    {
        using ScriptLevel level = Stacked(out _, out SceneNode low, out _);
        level.AddScript(level.Scene.Root, """
            local CollectionService = game:GetService("CollectionService")
            local low = workspace.Stack.Low
            print(low:HasTag("Heavy"), #CollectionService:GetTagged("Heavy"), low:GetAttribute("Weight"), next(low:GetAttributes()))
            CollectionService:GetInstanceAddedSignal("Heavy"):Connect(function(node) print("added", node.Name) end)
            low.AttributeChanged:Connect(function(name) print("changed", name) end)
            low:AddTag("Heavy")
            low:SetAttribute("Weight", 40)
            workspace:SetAttribute("Round", 1)
            """);
        var before = new LevelState(level.Scene);

        level.Play().Tick();
        LuauScriptRuntime first = level.Runtime.ShouldNotBeNull();
        first.TaggedNodeCount.ShouldBe(1);
        first.AttributeNodeCount.ShouldBe(2);
        level.Stop();

        first.TaggedNodeCount.ShouldBe(0);
        first.AttributeNodeCount.ShouldBe(0);
        new LevelState(level.Scene).ShouldBe(before, "after the first play");

        level.Play().Tick();
        level.Runtime.ShouldNotBe(first);
        level.Stop();

        level.Errors.ShouldBeEmpty();
        string[] onePlay = ["false 0 nil nil", "added Low", "changed Weight"];
        level.Printed.ShouldBe([.. onePlay, .. onePlay]);
        new LevelState(level.Scene).ShouldBe(before, "after the second play");
    }

    // The editor plays one level many times. A runtime left on the scene's
    // events would be kept there with every node it holds a tag for.
    [Theory]
    [InlineData(nameof(Scene.NodeAdded))]
    [InlineData(nameof(Scene.NodeRemoved))]
    [InlineData(nameof(Scene.NodeReparented))]
    public void A_runtime_hears_of_nodes_that_come_and_go_while_its_level_plays_and_no_longer(string sceneEvent)
    {
        using ScriptLevel level = Stacked(out _, out _, out _);
        level.AddScript(level.Scene.Root, "workspace.Stack.Low:AddTag('Heavy')");
        int before = ListenerCount(level.Scene, sceneEvent);

        level.Play().Tick();
        ListenerCount(level.Scene, sceneEvent).ShouldBeGreaterThan(before);

        level.Stop();
        ListenerCount(level.Scene, sceneEvent).ShouldBe(before);
    }

    [Fact]
    public void A_node_that_goes_after_Stop_is_no_business_of_the_runtime_that_tagged_it()
    {
        using ScriptLevel level = Stacked(out SceneNode stack, out SceneNode low, out _);
        level.AddScript(level.Scene.Root, """
            workspace.Stack.Low:AddTag("Heavy")
            game:GetService("CollectionService"):GetInstanceRemovedSignal("Heavy"):Connect(function() print("heard") end)
            """);
        level.Play().Tick();
        level.Stop();

        stack.RemoveChild(low);
        stack.AddChild(low);

        level.Printed.ShouldBeEmpty();
        level.Sink.Messages.ShouldBeEmpty();
    }

    // The node left the level while it played, so by Stop only the runtime
    // knew of it: its tag, its attribute and a listener of its signal.
    [Fact]
    public void A_node_that_left_the_level_is_held_by_nothing_once_the_level_has_stopped()
    {
        using var level = new ScriptLevel();
        WeakReference marker = PlayAndLoseATaggedNode(level);

        // The next play has a runtime of its own, and the test lets go of the first.
        level.Play().Tick();
        level.Stop();
        level.Runtimes.Clear();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        marker.IsAlive.ShouldBeFalse();
    }

    // Apart, so no local of the test keeps the node.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference PlayAndLoseATaggedNode(ScriptLevel level)
    {
        SceneNode marker = level.Scene.Root.CreateChild("Marker");
        level.AddScript(level.Scene.Root, """
            local marker = workspace:FindFirstChild("Marker")
            if marker then
                marker:AddTag("Heavy")
                marker:SetAttribute("Weight", 4)
                marker.AttributeChanged:Connect(function() end)
                game:GetService("CollectionService"):GetInstanceRemovedSignal("Heavy"):Connect(function() end)
                print("marked")
            end
            """);
        level.Play().Tick();
        level.Scene.Root.RemoveChild(marker);
        level.Tick();
        level.Stop();

        level.Errors.ShouldBeEmpty();
        level.Printed.ShouldBe(["marked"]);
        return new WeakReference(marker);
    }

    // Stack holds Low, Low holds Mid and Mid holds Top.
    private static ScriptLevel Stacked(out SceneNode stack, out SceneNode low, out SceneNode mid)
    {
        var level = new ScriptLevel();
        stack = level.Scene.Root.CreateChild("Stack");
        low = level.Part("Low", new Vector3(3f, 0.5f, 3f), stack);
        mid = level.Part("Mid", new Vector3(0f, 0.5f, 0f), low);
        level.Part("Top", new Vector3(0f, 0.5f, 0f), mid);
        return level;
    }

    // A field-like event keeps its listeners in a field of its own name.
    private static int ListenerCount(Scene scene, string sceneEvent)
    {
        FieldInfo field = typeof(Scene)
            .GetField(sceneEvent, BindingFlags.Instance | BindingFlags.NonPublic)
            .ShouldNotBeNull($"Scene keeps {sceneEvent} some other way now");

        return (field.GetValue(scene) as Delegate)?.GetInvocationList().Length ?? 0;
    }
}
