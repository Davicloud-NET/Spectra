using System.Reflection;
using SpectraEngine.Core.Scene;
using SpectraEngine.Core.Scripting;

namespace SpectraEngine.Scripting.Tests;

/// <summary>
/// WaitForChild: the child at once when it is there, and otherwise a wait
/// until a child gets the name or the time is over. No script can add a node
/// yet, so the test adds it through the scene.
/// </summary>
public sealed class WaitForChildTests
{
    private const string WaitsForKey = """
        local key = workspace.House:WaitForChild("Key")
        print("got", key, key.Parent)
        """;

    [Fact]
    public void A_child_that_is_there_comes_back_at_once_and_nothing_waits()
    {
        using ScriptLevel level = House.With("""
            print(workspace:WaitForChild("House"), workspace.House:WaitForChild("Door", 5))
            print(workspace:WaitForChild("House") == workspace.House)
            """);

        level.Play().Tick();

        level.Errors.ShouldBeEmpty();
        level.Printed.ShouldBe(["House Door", "true"]);
        level.Runtime.ShouldNotBeNull().WaitingThreadCount.ShouldBe(0);
    }

    [Fact]
    public void A_thread_waits_until_a_child_of_that_name_arrives_and_goes_on_with_it_on_the_next_tick()
    {
        using ScriptLevel level = House.With(WaitsForKey);
        SceneNode house = level.Scene.Root.Children.Single(node => node.Name == "House");

        level.Play().Tick(30);
        level.Printed.ShouldBeEmpty();
        level.Runtime.ShouldNotBeNull().WaitingThreadCount.ShouldBe(1);

        // Adding the node runs no script: the scene only tells that it came.
        house.CreateChild("Other");
        house.CreateChild("Key");
        level.Printed.ShouldBeEmpty();

        level.Tick();

        level.Errors.ShouldBeEmpty();
        level.Printed.ShouldBe(["got Key House"]);
        level.Runtime.WaitingThreadCount.ShouldBe(0);
    }

    // What a script that makes a node will do: the node arrives while a
    // script runs, in the middle of a tick.
    [Fact]
    public void A_child_that_arrives_in_the_middle_of_a_tick_ends_the_wait_in_that_tick_and_after_what_added_it()
    {
        using ScriptLevel level = House.With(WaitsForKey);
        SceneNode house = level.Scene.Root.Children.Single(node => node.Name == "House");
        level.AddScript(level.Scene.Root, """
            task.wait(0.1)
            print("adding")
            print("added")
            """, "Adder");

        level.Sink.Heard = message =>
        {
            if (message.Text == "adding")
                house.CreateChild("Key");
        };

        level.Play().Tick(6);
        level.Printed.ShouldBeEmpty();

        level.Tick();

        level.Errors.ShouldBeEmpty();
        level.Printed.ShouldBe(["adding", "added", "got Key House"]);
    }

    [Fact]
    public void A_wait_with_a_timeout_gives_nil_when_the_time_is_over()
    {
        using ScriptLevel level = House.With("""
            print("timed out with", workspace.House:WaitForChild("Key", 0.1))
            """);

        // The script starts on the first tick, and a tenth of a second is six more.
        level.Play().Tick(6);
        level.Printed.ShouldBeEmpty();

        level.Tick();

        level.Errors.ShouldBeEmpty();
        level.Sink.Texts(ScriptSeverity.Warning).ShouldBeEmpty();
        level.Printed.ShouldBe(["timed out with nil"]);
        level.Runtime.ShouldNotBeNull().WaitingThreadCount.ShouldBe(0);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-3")]
    [InlineData("0/0")]
    public void A_timeout_of_no_time_still_waits_for_the_next_tick(string timeout)
    {
        using ScriptLevel level = House.With($$"""
            print("timed out with", workspace.House:WaitForChild("Key", {{timeout}}))
            """);

        level.Play().Tick();
        level.Printed.ShouldBeEmpty();

        level.Tick();

        level.Errors.ShouldBeEmpty();
        level.Printed.ShouldBe(["timed out with nil"]);
    }

    [Fact]
    public void A_child_that_arrives_before_the_timeout_ends_the_wait_and_the_timeout_then_wakes_nothing()
    {
        using ScriptLevel level = House.With("""
            print("got", workspace.House:WaitForChild("Key", 0.1))
            print("slept", task.wait(0.5) > 0.4)
            """);
        SceneNode house = level.Scene.Root.Children.Single(node => node.Name == "House");

        level.Play().Tick(2);
        house.CreateChild("Key");
        level.Tick();
        level.Printed.ShouldBe(["got Key"]);

        // The timeout comes due while the thread sleeps. It must sleep on.
        level.Tick(20);
        level.Printed.ShouldBe(["got Key"]);

        level.Tick(11);

        level.Errors.ShouldBeEmpty();
        level.Printed.ShouldBe(["got Key", "slept true"]);
    }

    [Fact]
    public void A_wait_with_no_timeout_warns_once_after_five_seconds_in_Robloxs_words_and_keeps_waiting()
    {
        using ScriptLevel level = House.With("""
            local house = workspace.House
            local key = house:WaitForChild("Key")
            print("got", key)
            """);
        SceneNode house = level.Scene.Root.Children.Single(node => node.Name == "House");

        // Five seconds are 300 ticks after the tick the script started on.
        level.Play().Tick(300);
        level.Sink.Messages.ShouldBeEmpty();

        level.Tick();

        ScriptMessage warning = level.Sink.Messages.ShouldHaveSingleItem();
        warning.Severity.ShouldBe(ScriptSeverity.Warning);
        warning.Text.ShouldBe("Infinite yield possible on 'Root.House:WaitForChild(\"Key\")'");
        warning.Script.ShouldBe(level.Scene.ScriptNodes.Single(node => node.Name == House.ScriptName).Script.ShouldNotBeNull().Path);
        warning.Line.ShouldBe(2);

        level.Tick(900);
        level.Sink.Messages.Count.ShouldBe(1);
        level.Runtime.ShouldNotBeNull().WaitingThreadCount.ShouldBe(1);

        house.CreateChild("Key");
        level.Tick();

        level.Errors.ShouldBeEmpty();
        level.Printed.ShouldBe(["got Key"]);
    }

    [Fact]
    public void A_timeout_too_long_to_count_never_warns_and_still_ends_when_the_child_arrives()
    {
        using ScriptLevel level = House.With("""
            print("got", workspace.House:WaitForChild("Key", math.huge))
            """);
        SceneNode house = level.Scene.Root.Children.Single(node => node.Name == "House");

        level.Play().Tick(400);
        level.Sink.Messages.ShouldBeEmpty();

        house.CreateChild("Key");
        level.Tick();

        level.Errors.ShouldBeEmpty();
        level.Printed.ShouldBe(["got Key"]);
    }

    [Fact]
    public void A_node_that_is_renamed_to_the_name_or_moved_under_the_parent_ends_the_wait_too()
    {
        using ScriptLevel level = House.With("""
            task.spawn(function() print("renamed", workspace.House:WaitForChild("Gate")) end)
            task.spawn(function() print("moved", workspace.House.Walls:WaitForChild("Hinge").Parent) end)
            """);
        SceneNode house = level.Scene.Root.Children.Single(node => node.Name == "House");
        SceneNode door = house.Children.Single(node => node.Name == "Door");

        level.Play().Tick(3);
        level.Printed.ShouldBeEmpty();

        door.Name = "Gate";
        level.Tick();
        level.Printed.ShouldBe(["renamed Gate"]);

        house.Children.Single(node => node.Name == "Walls").AddChild(door.Children[0]);
        level.Tick();

        level.Errors.ShouldBeEmpty();
        level.Printed.ShouldBe(["renamed Gate", "moved Walls"]);
    }

    [Fact]
    public void Only_a_child_of_the_node_itself_ends_the_wait_and_only_one_with_the_whole_name()
    {
        using ScriptLevel level = House.With(WaitsForKey);
        SceneNode house = level.Scene.Root.Children.Single(node => node.Name == "House");

        level.Play().Tick(2);
        house.Children[0].CreateChild("Key");
        level.Scene.Root.CreateChild("Key");
        house.CreateChild("Keys");
        house.CreateChild("key");
        level.Tick(3);

        level.Printed.ShouldBeEmpty();
        level.Runtime.ShouldNotBeNull().WaitingThreadCount.ShouldBe(1);
        level.Errors.ShouldBeEmpty();
    }

    [Fact]
    public void A_child_that_is_gone_again_before_the_wake_ends_no_wait()
    {
        using ScriptLevel level = House.With(WaitsForKey);
        SceneNode house = level.Scene.Root.Children.Single(node => node.Name == "House");

        level.Play().Tick(2);
        SceneNode key = house.CreateChild("Key");
        house.RemoveChild(key);
        level.Tick(3);

        level.Printed.ShouldBeEmpty();
        level.Errors.ShouldBeEmpty();

        house.AddChild(key);
        level.Tick();

        level.Printed.ShouldBe(["got Key House"]);
    }

    [Fact]
    public void Threads_that_wait_for_one_child_go_on_in_the_order_they_began_to_wait()
    {
        using ScriptLevel level = House.With("""
            for index = 1, 3 do
                task.spawn(function()
                    local key = workspace.House:WaitForChild("Key", index == 2 and 60 or nil)
                    print(index, key)
                end)
            end
            """);
        SceneNode house = level.Scene.Root.Children.Single(node => node.Name == "House");

        level.Play().Tick(2);
        level.Runtime.ShouldNotBeNull().WaitingThreadCount.ShouldBe(3);

        house.CreateChild("Key");
        level.Tick();

        level.Errors.ShouldBeEmpty();
        level.Printed.ShouldBe(["1 Key", "2 Key", "3 Key"]);
        level.Runtime.WaitingThreadCount.ShouldBe(0);
    }

    // A tick sends only so many of the wakes it makes itself and drops the
    // rest. A thread that waits for a child is not lost with its wake.
    [Fact]
    public void More_threads_than_a_tick_wakes_all_go_on_when_their_child_arrives_in_the_middle_of_one()
    {
        using ScriptLevel level = House.With("""
            for index = 1, 6 do
                task.spawn(function()
                    print(index, workspace.House:WaitForChild("Key"))
                end)
            end
            task.wait(0.1)
            print("adding")
            """);
        SceneNode house = level.Scene.Root.Children.Single(node => node.Name == "House");
        level.Sink.Heard = message =>
        {
            if (message.Text == "adding")
                house.CreateChild("Key");
        };

        level.Play();
        level.Entities.MaxDispatchesPerTick = 2;
        level.Tick(7);
        level.Printed.ShouldBe(["adding", "1 Key", "2 Key"]);

        level.Tick(2);

        level.Errors.ShouldBeEmpty();
        level.Printed.ShouldBe(["adding", "1 Key", "2 Key", "3 Key", "4 Key", "5 Key", "6 Key"]);
        level.Runtime.ShouldNotBeNull().WaitingThreadCount.ShouldBe(0);
    }

    // A wait with a timeout too long to count has no wake of its own. The
    // wake of the arrival is its only one, so it may not be lost for room.
    [Fact]
    public void An_arrival_the_queue_has_no_room_for_ends_the_wait_once_there_is_room()
    {
        using ScriptLevel level = House.With("""
            task.spawn(function()
                print("got", workspace.House:WaitForChild("Key", math.huge))
            end)
            for _ = 1, 4 do
                task.delay(0.1, function() end)
            end
            """);
        SceneNode house = level.Scene.Root.Children.Single(node => node.Name == "House");

        level.Play();
        level.Entities.MaxPendingEvents = 4;
        level.Tick();

        // The four delays fill the queue until they come due.
        house.CreateChild("Key");
        level.Tick(4);
        level.Printed.ShouldBeEmpty();

        level.Tick(6);

        level.Errors.ShouldBeEmpty();
        level.Printed.ShouldBe(["got Key"]);
        level.Runtime.ShouldNotBeNull().WaitingThreadCount.ShouldBe(0);
    }

    [Fact]
    public void A_thread_that_was_cancelled_is_not_woken_by_the_child_or_by_the_warning()
    {
        using ScriptLevel level = House.With("""
            local thread = task.spawn(function()
                print("got", workspace.House:WaitForChild("Key"))
            end)
            task.wait()
            task.cancel(thread)
            print("cancelled")
            """);
        SceneNode house = level.Scene.Root.Children.Single(node => node.Name == "House");

        level.Play().Tick(3);
        level.Runtime.ShouldNotBeNull().WaitingThreadCount.ShouldBe(0);

        house.CreateChild("Key");
        level.Tick(400);

        level.Errors.ShouldBeEmpty();
        level.Sink.Texts(ScriptSeverity.Warning).ShouldBeEmpty();
        level.Printed.ShouldBe(["cancelled"]);
    }

    [Fact]
    public void A_thread_resumed_by_hand_out_of_the_wait_is_not_woken_by_the_child_while_it_waits_for_a_tick()
    {
        using ScriptLevel level = House.With("""
            local thread = task.spawn(function()
                print("resumed with", workspace.House:WaitForChild("Key"))
                print("slept", task.wait(0.5) > 0.4)
            end)
            coroutine.resume(thread, "a hand")
            """);
        SceneNode house = level.Scene.Root.Children.Single(node => node.Name == "House");

        level.Play().Tick(2);
        level.Printed.ShouldBe(["resumed with a hand"]);

        house.CreateChild("Key");
        level.Tick(10);
        level.Printed.ShouldBe(["resumed with a hand"]);

        level.Tick(400);

        level.Errors.ShouldBeEmpty();
        level.Sink.Texts(ScriptSeverity.Warning).ShouldBeEmpty();
        level.Printed.ShouldBe(["resumed with a hand", "slept true"]);
    }

    [Fact]
    public void A_wait_where_a_thread_cannot_yield_is_an_error_and_leaves_no_wait_behind()
    {
        using ScriptLevel level = House.With("""
            local lazy = setmetatable({}, { __index = function() return workspace.House:WaitForChild("Key") end })
            print(pcall(function() return lazy.field end))
            """);
        SceneNode house = level.Scene.Root.Children.Single(node => node.Name == "House");

        level.Play().Tick();

        level.Printed.ShouldHaveSingleItem().ShouldEndWith("attempt to yield across metamethod/C-call boundary");
        level.Runtime.ShouldNotBeNull().WaitingThreadCount.ShouldBe(0);

        house.CreateChild("Key");
        level.Tick(3);

        level.Errors.ShouldBeEmpty();
        level.Printed.Length.ShouldBe(1);
    }

    [Theory]
    [InlineData("workspace:WaitForChild()", "invalid argument #1 to 'WaitForChild' (string expected, got nil)")]
    [InlineData("workspace:WaitForChild(workspace)", "invalid argument #1 to 'WaitForChild' (string expected, got Instance)")]
    [InlineData("workspace:WaitForChild('Key', 'soon')", "invalid argument #2 to 'WaitForChild' (number expected, got string)")]
    [InlineData("workspace:WaitForChild('\\xff')", "WaitForChild was handed a name that is not valid UTF-8, which no node has")]
    [InlineData("workspace.WaitForChild('Key')", "Expected ':' not '.' calling member function WaitForChild")]
    public void WaitForChild_handed_the_wrong_thing_is_an_error_and_no_wait(string call, string error)
    {
        using ScriptLevel level = House.With($"local _ = {call}");

        level.Play().Tick();

        level.Errors.ShouldBe([error]);
        level.Runtime.ShouldNotBeNull().WaitingThreadCount.ShouldBe(0);
    }

    [Fact]
    public void Stop_ends_the_wait_and_a_node_that_arrives_after_it_reaches_no_script()
    {
        using ScriptLevel level = House.With(WaitsForKey);
        SceneNode house = level.Scene.Root.Children.Single(node => node.Name == "House");

        level.Play().Tick(3);
        level.Stop();
        SceneNode key = house.CreateChild("Key");

        level.Sink.Messages.ShouldBeEmpty();

        // The next play has the child from its first tick.
        level.Play().Tick();

        level.Errors.ShouldBeEmpty();
        level.Printed.ShouldBe(["got Key House"]);
        house.RemoveChild(key);
    }

    // The editor plays one level many times. A runtime left on the scene's
    // events would be kept there with all it holds, one for every Play.
    [Theory]
    [InlineData(nameof(Scene.NodeAdded))]
    [InlineData(nameof(Scene.NodeReparented))]
    [InlineData(nameof(Scene.NodeRenamed))]
    public void A_runtime_listens_to_the_scene_while_its_level_plays_and_no_longer(string sceneEvent)
    {
        using ScriptLevel level = House.With(WaitsForKey);
        int before = ListenerCount(level.Scene, sceneEvent);

        // The entity world listens too, and also lets go at Stop.
        level.Play().Tick(2);
        ListenerCount(level.Scene, sceneEvent).ShouldBeGreaterThan(before);

        level.Stop();
        ListenerCount(level.Scene, sceneEvent).ShouldBe(before);
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
