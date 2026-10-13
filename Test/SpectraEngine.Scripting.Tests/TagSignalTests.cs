using System.Numerics;
using SpectraEngine.Core.Scene;

namespace SpectraEngine.Scripting.Tests;

/// <summary>
/// The two signals CollectionService hands out for a tag: one fires when a
/// node of the level gets the tag, the other when one loses it. Both are
/// deferred, as every signal is.
/// </summary>
public sealed class TagSignalTests
{
    private const string Listeners = """
        local CollectionService = game:GetService("CollectionService")
        CollectionService:GetInstanceAddedSignal("Lava"):Connect(function(...)
            print("added", select("#", ...), ..., (...):HasTag("Lava"))
        end)
        CollectionService:GetInstanceRemovedSignal("Lava"):Connect(function(...)
            print("removed", select("#", ...), ..., (...):HasTag("Lava"))
        end)

        """;

    [Fact]
    public void A_listener_is_called_with_the_node_after_the_call_that_tagged_it_has_returned()
    {
        string[] printed = House.Ask(Listeners + """
            local door = workspace.House.Door
            door:AddTag("Lava")
            print("tagged")
            door:RemoveTag("Lava")
            print("untagged")
            CollectionService:AddTag(workspace.House.Lamp, "Lava")
            """);

        // By the time the first is called the door has lost the tag again.
        printed.ShouldBe(["tagged", "untagged", "added 1 Door false", "removed 1 Door false", "added 1 Lamp true"]);
    }

    [Fact]
    public void Adding_a_tag_a_node_has_and_removing_one_it_lacks_fire_nothing()
    {
        using ScriptLevel level = House.With(Listeners + """
            local door = workspace.House.Door
            door:RemoveTag("Lava")
            door:AddTag("Lava")
            door:AddTag("Lava")
            CollectionService:AddTag(door, "Lava")
            door:AddTag("Water")
            door:RemoveTag("Water")
            task.wait()
            print("next tick")
            door:RemoveTag("Lava")
            door:RemoveTag("Lava")
            CollectionService:RemoveTag(door, "Lava")
            """);

        level.Play().Tick(2);

        level.Errors.ShouldBeEmpty();
        level.Printed.ShouldBe(["added 1 Door true", "next tick", "removed 1 Door false"]);
    }

    [Fact]
    public void A_signal_is_the_same_object_on_every_ask_and_another_for_another_tag()
    {
        string[] printed = House.Ask("""
            local CollectionService = game:GetService("CollectionService")
            local added = CollectionService:GetInstanceAddedSignal("Lava")
            local removed = CollectionService:GetInstanceRemovedSignal("Lava")
            print(added, removed, typeof(added))
            print(added == CollectionService:GetInstanceAddedSignal("Lava"), removed == CollectionService:GetInstanceRemovedSignal("Lava"))
            print(added == removed, added == CollectionService:GetInstanceAddedSignal("lava"))
            print(#CollectionService:GetTagged("Lava"), #CollectionService:GetTagged("lava"))
            """);

        printed.ShouldBe(["Signal InstanceAdded Signal InstanceRemoved Instance", "true true", "false false", "0 0"]);
    }

    [Fact]
    public void A_listener_that_removes_the_tag_it_hears_of_leaves_no_node_tagged()
    {
        using var level = new ScriptLevel();
        SceneNode pit = level.Scene.Root.CreateChild("Pit");
        level.Part("First", new Vector3(3f, 1f, 3f), pit);
        level.Part("Second", new Vector3(4f, 1f, 3f), pit);
        level.AddScript(level.Scene.Root, """
            local CollectionService = game:GetService("CollectionService")
            CollectionService:GetInstanceAddedSignal("Lava"):Connect(function(part)
                print("added", part.Name, part:HasTag("Lava"), #CollectionService:GetTagged("Lava"))
                part:RemoveTag("Lava")
            end)
            CollectionService:GetInstanceRemovedSignal("Lava"):Connect(function(part)
                print("removed", part.Name, part:HasTag("Lava"), #CollectionService:GetTagged("Lava"))
            end)
            for _, part in workspace.Pit:GetChildren() do
                part:AddTag("Lava")
            end
            """);

        level.Play().Tick();

        level.Errors.ShouldBeEmpty();
        level.Printed.ShouldBe(
        [
            "added First true 2", "added Second true 1", "removed First false 0", "removed Second false 0",
        ]);
        level.Runtime.ShouldNotBeNull().TaggedNodeCount.ShouldBe(0);
    }

    [Fact]
    public void A_listener_that_tags_the_next_node_is_called_for_each_in_turn()
    {
        using var level = new ScriptLevel();
        SceneNode row = level.Scene.Root.CreateChild("Row");
        for (int i = 1; i <= 4; i++)
            level.Part($"Tile{i}", new Vector3(i, 1f, 3f), row);
        level.AddScript(level.Scene.Root, """
            local CollectionService = game:GetService("CollectionService")
            local tiles = workspace.Row:GetChildren()
            CollectionService:GetInstanceAddedSignal("Lit"):Connect(function(tile)
                local lit = #CollectionService:GetTagged("Lit")
                print("lit", tile.Name, lit)
                if tiles[lit + 1] then
                    tiles[lit + 1]:AddTag("Lit")
                end
            end)
            tiles[1]:AddTag("Lit")
            """);

        level.Play().Tick();

        level.Errors.ShouldBeEmpty();
        level.Printed.ShouldBe(["lit Tile1 1", "lit Tile2 2", "lit Tile3 3", "lit Tile4 4"]);
    }

    [Fact]
    public void Wait_goes_on_with_the_node_that_got_the_tag_next()
    {
        using var level = new ScriptLevel();
        level.Part("Crate", new Vector3(3f, 1f, 3f));
        level.AddScript(level.Scene.Root, """
            local CollectionService = game:GetService("CollectionService")
            local crate = CollectionService:GetInstanceAddedSignal("Heavy"):Wait()
            print("went on with", crate, crate:HasTag("Heavy"))
            print("and lost by", CollectionService:GetInstanceRemovedSignal("Heavy"):Wait())
            """, "Waits");
        level.AddScript(level.Scene.Root, """
            workspace.Crate:AddTag("Heavy")
            task.wait()
            workspace.Crate:RemoveTag("Heavy")
            """, "Tags");

        level.Play().Tick(2);

        level.Errors.ShouldBeEmpty();
        level.Printed.ShouldBe(["went on with Crate true", "and lost by Crate"]);
        level.Runtime.ShouldNotBeNull().WaitingThreadCount.ShouldBe(0);
    }

    [Fact]
    public void A_listener_that_disconnects_and_one_connected_late_hear_what_Roblox_lets_them_hear()
    {
        using var level = new ScriptLevel();
        SceneNode row = level.Scene.Root.CreateChild("Row");
        level.Part("One", new Vector3(1f, 1f, 3f), row);
        level.Part("Two", new Vector3(2f, 1f, 3f), row);
        level.Part("Three", new Vector3(3f, 1f, 3f), row);
        level.AddScript(level.Scene.Root, """
            local added = game:GetService("CollectionService"):GetInstanceAddedSignal("Lit")
            local row = workspace.Row
            row.One:AddTag("Lit")
            local first
            first = added:Connect(function(tile)
                print("first heard", tile.Name)
                first:Disconnect()
                added:Connect(function(late) print("late heard", late.Name) end)
            end)
            added:Once(function(tile) print("once heard", tile.Name) end)
            row.Two:AddTag("Lit")
            row.Three:AddTag("Lit")
            task.wait()
            row.One:RemoveTag("Lit")
            row.One:AddTag("Lit")
            """);

        level.Play().Tick(2);

        level.Errors.ShouldBeEmpty();
        level.Printed.ShouldBe(["first heard Two", "once heard Two", "late heard One"]);
    }

    [Fact]
    public void Threads_cancelled_while_they_wait_for_a_tag_nobody_adds_do_not_pile_up()
    {
        using ScriptLevel level = House.With("""
            local added = game:GetService("CollectionService"):GetInstanceAddedSignal("Never")
            for _ = 1, 500 do
                task.cancel(task.spawn(function() added:Wait() end))
            end
            local changed = workspace.House.Door.AttributeChanged
            for _ = 1, 500 do
                task.cancel(task.spawn(function() changed:Wait() end))
            end
            added:Connect(function(node) print("still heard", node.Name) end)
            workspace.House.Door:AddTag("Never")
            """);

        level.Play().Tick();

        level.Errors.ShouldBeEmpty();
        level.Printed.ShouldBe(["still heard Door"]);
        level.Entities.PendingEventCount.ShouldBe(0);
        level.Entities.LastTickDispatchCount.ShouldBeLessThan(100);
    }
}
