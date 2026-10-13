using System.Numerics;
using System.Runtime.CompilerServices;
using SpectraEngine.Core.Scene;
using SpectraEngine.Core.Scripting;
using SpectraEngine.Scripting.Luau.Runtime;

namespace SpectraEngine.Scripting.Tests;

/// <summary>
/// The tags and attributes of a destroyed node. As in Roblox it keeps them
/// and takes more. They last while anything can hand the node to a script,
/// are let go with the node, and Stop takes them away like all the rest.
/// </summary>
public sealed class DestroyedNodeKeepsTests
{
    [Fact]
    public void A_function_that_runs_after_its_part_was_destroyed_reads_what_the_part_was_built_with()
    {
        using var level = new ScriptLevel();
        SceneNode coin = level.Part("Coin", new Vector3(2f, 1f, 3f));
        coin.Tags = TagSet.Of("Coin", "Gold");
        coin.Attributes = AttributeSet.Empty.With("Points", MemberValue.Of(5d));
        level.AddScript(level.Scene.Root, """
            local coin = workspace.Coin
            task.defer(function()
                print(coin.Parent, coin:GetAttribute("Points"), coin:HasTag("Gold"), table.concat(coin:GetTags(), " "))
            end)
            coin:Destroy()
            """, "Main");

        level.Play().Tick(2);

        level.Errors.ShouldBeEmpty();
        level.Printed.ShouldBe(["nil 5 true Coin Gold"]);
    }

    [Fact]
    public void A_part_no_script_held_when_it_was_destroyed_keeps_them_for_the_listener_the_level_hands_it_to()
    {
        using var level = new ScriptLevel(hasCharacter: false);
        level.AddScript(level.Scene.Root, """
            local CollectionService = game:GetService("CollectionService")
            local score = 0
            CollectionService:GetInstanceRemovedSignal("Enemy"):Connect(function(enemy)
                score += enemy:GetAttribute("Points")
                print(enemy.Name, enemy.Parent, enemy:HasTag("Enemy"), score)
            end)

            local function muster()
                local squad = Instance.new("Folder")
                squad.Name = "Squad"
                for index = 1, 3 do
                    local enemy = Instance.new("Part", squad)
                    enemy.Name = "Enemy" .. index
                    enemy:AddTag("Enemy")
                    enemy:SetAttribute("Points", index * 10)
                end
                squad.Parent = workspace
            end

            muster()
            task.wait()
            game:GetService("RunService").Heartbeat:Once(function()
                workspace.Squad:Destroy()
            end)
            """, "Main");
        LuauScriptRuntime runtime = level.Play().Tick(2).Runtime.ShouldNotBeNull();

        // The squad was destroyed at the end of the tick. Each call of the
        // listener waits in the level's queue, and nothing else has an enemy.
        level.Printed.ShouldBeEmpty();
        level.Scene.Root.Children.ShouldNotContain(node => node.Name == "Squad");
        runtime.Collect();
        CollectAll();

        level.Tick();

        level.Errors.ShouldBeEmpty();
        level.Printed.ShouldBe(["Enemy1 nil true 10", "Enemy2 nil true 30", "Enemy3 nil true 60"]);
    }

    [Fact]
    public void What_a_destroyed_node_holds_lasts_while_a_script_holds_the_node_and_is_let_go_with_it()
    {
        using var level = new ScriptLevel(hasCharacter: false);
        level.AddScript(level.Scene.Root, """
            local kept = {}
            for index = 1, 50 do
                local part = Instance.new("Part")
                part:AddTag("Spare")
                part:SetAttribute("Index", index)
                part.Parent = workspace
                kept[index] = part
            end
            task.wait(0.1)
            for _, part in kept do
                part:Destroy()
                part:AddTag("Gone")
                part:SetAttribute("Note", "kept after Destroy")
            end
            task.wait(0.1)
            local sum = 0
            for _, part in kept do
                assert(part:HasTag("Spare") and part:HasTag("Gone") and part:GetAttribute("Note") == "kept after Destroy")
                sum += part:GetAttribute("Index")
            end
            print(sum, #game:GetService("CollectionService"):GetTagged("Spare"))
            kept = nil
            task.wait(0.1)
            print("let go")
            """, "Main");
        LuauScriptRuntime runtime = level.Play().Tick(2).Runtime.ShouldNotBeNull();
        WeakReference<SceneNode>[] spares = MadeNow(level);
        spares.Length.ShouldBe(50);

        // Destroyed, and in the script's table.
        level.Tick(8);
        runtime.Collect();
        CollectAll();

        spares.Count(IsThere).ShouldBe(50);
        runtime.HeldDestroyedCount.ShouldBe(50);
        runtime.DestroyedTaggedCount.ShouldBe(50);
        runtime.DestroyedAttributeCount.ShouldBe(50);
        runtime.TaggedNodeCount.ShouldBe(0, "a destroyed node is not one of the level's");
        runtime.AttributeNodeCount.ShouldBe(0);

        // The script read them all, let go of its table and ended.
        level.Tick(20);
        runtime.Collect();
        CollectAll();

        level.Errors.ShouldBeEmpty();
        level.Printed.ShouldBe(["1275 0", "let go"]);
        runtime.HeldDestroyedCount.ShouldBe(0);
        spares.Count(IsThere).ShouldBe(0, "a destroyed part is still held, with what it kept");
        runtime.DestroyedTaggedCount.ShouldBe(0);
        runtime.DestroyedAttributeCount.ShouldBe(0);
    }

    [Fact]
    public void Stop_takes_away_what_a_destroyed_node_held_and_the_next_play_finds_the_level_as_it_was_built()
    {
        using var level = new ScriptLevel();
        SceneNode lamp = level.Part("Lamp", new Vector3(2f, 1f, 3f));
        TagSet tags = lamp.Tags = TagSet.Of("Bright");
        AttributeSet attributes = lamp.Attributes = AttributeSet.Empty.With("Watts", MemberValue.Of(60d));
        level.AddScript(level.Scene.Root, """
            local lamp = workspace.Lamp
            print(table.concat(lamp:GetTags(), " "), lamp:GetAttribute("Watts"), lamp:GetAttribute("Late"))
            lamp:Destroy()
            lamp:AddTag("Late")
            lamp:SetAttribute("Watts", 100)
            lamp:SetAttribute("Late", true)
            print(table.concat(lamp:GetTags(), " "), lamp:GetAttribute("Watts"), lamp:GetAttribute("Late"))
            """, "Main");
        var before = new LevelState(level.Scene);

        string[] expected = ["Bright 60 nil", "Bright Late 100 true"];
        for (int play = 1; play <= 2; play++)
        {
            LuauScriptRuntime runtime = level.Play().Tick().Runtime.ShouldNotBeNull();
            lamp.Parent.ShouldBeNull();
            (runtime.DestroyedTaggedCount, runtime.DestroyedAttributeCount).ShouldBe((1, 1));
            level.Stop();

            // The lamp is back in the level, and the runtime that stopped has nothing of it.
            (runtime.DestroyedTaggedCount, runtime.DestroyedAttributeCount).ShouldBe((0, 0));
            level.Errors.ShouldBeEmpty();
            level.Printed.ShouldBe([.. Enumerable.Repeat(expected, play).SelectMany(lines => lines)]);
            lamp.Parent.ShouldBe(level.Scene.Root);
            lamp.Tags.ShouldBeSameAs(tags);
            lamp.Attributes.ShouldBeSameAs(attributes);
            new LevelState(level.Scene).ShouldBe(before, $"after play {play}");
        }
    }

    [Fact]
    public void A_copy_of_a_destroyed_node_has_the_tags_and_attributes_the_node_kept()
    {
        string[] printed = House.Ask("""
            local lamp = workspace.House.Lamp
            lamp:AddTag("Bright")
            lamp:SetAttribute("Watts", 60)
            lamp:Destroy()
            lamp:AddTag("Late")
            local copy = lamp:Clone()
            copy.Parent = workspace
            print(table.concat(copy:GetTags(), " "), copy:GetAttribute("Watts"), #game:GetService("CollectionService"):GetTagged("Late"))
            """);

        printed.ShouldBe(["Bright Late 60 1"]);
    }

    [Fact]
    public void A_destroyed_node_holds_no_more_than_a_node_of_the_level()
    {
        using ScriptLevel level = House.With("""
            local lamp = workspace.House.Lamp
            lamp:Destroy()
            for index = 1, 256 do
                lamp:AddTag("t" .. index)
                lamp:SetAttribute("n" .. index, index)
            end
            print(#lamp:GetTags(), lamp:GetAttribute("n256"))
            print(select(2, pcall(function() lamp:AddTag("one more") end)))
            print(select(2, pcall(function() lamp:SetAttribute("OneMore", 1) end)))
            """);

        level.Play().Tick();

        level.Errors.ShouldBeEmpty();
        level.Printed[0].ShouldBe("256 256");
        level.Printed[1].ShouldEndWith("\"Lamp\" has 256 tags, and a node has no more. RemoveTag takes one off");
        level.Printed[2].ShouldEndWith(
            "\"Lamp\" has 256 attributes, and a node holds no more. SetAttribute with nil takes one away");
    }

    private static bool IsThere(WeakReference<SceneNode> node) => node.TryGetTarget(out _);

    private static void CollectAll()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    // In a method of its own, so no local keeps a node alive in the test.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference<SceneNode>[] MadeNow(ScriptLevel level) =>
        [.. level.Scene.Root.Children.Where(TreeWrites.IsMade).Select(node => new WeakReference<SceneNode>(node))];
}
