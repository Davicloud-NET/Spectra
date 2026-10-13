using System.Numerics;

namespace SpectraEngine.Scripting.Tests;

/// <summary>
/// AttributeChanged and GetAttributeChangedSignal: a script hears of an
/// attribute another one set. Both are deferred, as every signal is.
/// </summary>
public sealed class AttributeSignalTests
{
    [Fact]
    public void A_listener_is_called_with_the_name_after_the_call_that_set_the_attribute_has_returned()
    {
        string[] printed = House.Ask("""
            local door = workspace.House.Door
            door.AttributeChanged:Connect(function(...)
                print("heard", select("#", ...), ..., door:GetAttribute(...))
            end)
            door:SetAttribute("Score", 1)
            print("set")
            door:SetAttribute("State", "open")
            print("set again")
            """);

        printed.ShouldBe(["set", "set again", "heard 1 Score 1", "heard 1 State open"]);
    }

    [Fact]
    public void A_set_of_the_value_an_attribute_has_fires_nothing()
    {
        using ScriptLevel level = House.With("""
            local door = workspace.House.Door
            local heard = {}
            door.AttributeChanged:Connect(function(name) table.insert(heard, name) end)
            door:GetAttributeChangedSignal("Score"):Connect(function() table.insert(heard, "the score") end)
            local frame = CFrame.new(1, 2, 3)
            door:SetAttribute("Score", 1)
            door:SetAttribute("Score", 1)
            door:SetAttribute("Word", "same")
            door:SetAttribute("Word", "sa" .. "me")
            door:SetAttribute("Where", frame)
            door:SetAttribute("Where", CFrame.new(1, 2, 3))
            door:SetAttribute("Tint", Color3.new(1, 0.5, 0))
            door:SetAttribute("Tint", Color3.new(1, 0.5, 0))
            door:SetAttribute("Point", Vector3.zero)
            door:SetAttribute("Point", Vector3.new(0, 0, 0))
            door:SetAttribute("Nothing", 0 / 0)
            door:SetAttribute("Nothing", 0 / 0)
            door:SetAttribute("Never", nil)
            door:SetAttribute("Score", nil)
            door:SetAttribute("Score", nil)
            task.wait()
            print(table.concat(heard, " "))
            """);

        level.Play().Tick(2);

        level.Errors.ShouldBeEmpty();
        level.Printed.ShouldBe(["Score the score Word Where Tint Point Nothing Score the score"]);
    }

    [Fact]
    public void A_value_of_another_kind_is_another_value()
    {
        using ScriptLevel level = House.With("""
            local door = workspace.House.Door
            local heard = 0
            door.AttributeChanged:Connect(function() heard += 1 end)
            door:SetAttribute("Mixed", 1)
            door:SetAttribute("Mixed", "1")
            door:SetAttribute("Mixed", true)
            door:SetAttribute("Mixed", Vector3.new(1, 1, 1))
            door:SetAttribute("Mixed", Color3.new(1, 1, 1))
            task.wait()
            print(heard)
            """);

        level.Play().Tick(2);

        level.Errors.ShouldBeEmpty();
        level.Printed.ShouldBe(["5"]);
    }

    [Fact]
    public void The_signal_of_one_name_fires_for_that_name_only_and_hands_over_nothing()
    {
        using ScriptLevel level = House.With("""
            local door = workspace.House.Door
            door:GetAttributeChangedSignal("Score"):Connect(function(...)
                print("score", select("#", ...), door:GetAttribute("Score"))
            end)
            door:SetAttribute("Other", 1)
            door:SetAttribute("score", 1)
            door:SetAttribute("Score", 2)
            task.wait()
            door:SetAttribute("Score", nil)
            workspace.House:SetAttribute("Score", 3)
            """);

        level.Play().Tick(2);

        level.Errors.ShouldBeEmpty();
        level.Printed.ShouldBe(["score 0 2", "score 0 nil"]);
    }

    [Fact]
    public void A_signal_is_the_same_object_on_every_read_and_another_for_another_name_or_node()
    {
        string[] printed = House.Ask("""
            local door = workspace.House.Door
            local changed = door.AttributeChanged
            local score = door:GetAttributeChangedSignal("Score")
            print(changed, typeof(changed), score)
            print(changed == door.AttributeChanged, rawequal(changed, workspace.House.Door.AttributeChanged))
            print(score == door:GetAttributeChangedSignal("Score"), score == door:GetAttributeChangedSignal("Other"))
            print(changed == workspace.House.AttributeChanged, score == workspace:GetAttributeChangedSignal("Score"))
            print(score == changed, door:GetAttributeChangedSignal("not a name") ~= nil)
            print(pcall(function() door:AttributeChanged() end))
            """);

        printed[..5].ShouldBe(
        [
            "Signal AttributeChanged Instance Signal AttributeChanged",
            "true true",
            "true false",
            "false false",
            "false true",
        ]);
        printed[5].ShouldEndWith("attempt to call a RBXScriptSignal value");
    }

    [Fact]
    public void Wait_goes_on_with_the_name_of_the_attribute_that_changed_next()
    {
        using var level = new ScriptLevel();
        level.Part("Board", new Vector3(3f, 1f, 3f));
        level.AddScript(level.Scene.Root, """
            local board = workspace.Board
            print("went on with", board.AttributeChanged:Wait())
            print("and again with", select("#", board:GetAttributeChangedSignal("Score"):Wait()))
            """, "Waits");
        level.AddScript(level.Scene.Root, """
            workspace.Board:SetAttribute("Score", 1)
            task.wait()
            workspace.Board:SetAttribute("Score", 2)
            """, "Sets");

        level.Play().Tick(2);

        level.Errors.ShouldBeEmpty();
        level.Printed.ShouldBe(["went on with Score", "and again with 0"]);
        level.Runtime.ShouldNotBeNull().WaitingThreadCount.ShouldBe(0);
    }

    [Fact]
    public void A_listener_that_disconnects_is_not_called_for_a_change_that_was_already_on_its_way()
    {
        string[] printed = House.Ask("""
            local door = workspace.House.Door
            local connection
            connection = door.AttributeChanged:Connect(function(name)
                print("heard", name, connection.Connected)
                connection:Disconnect()
            end)
            door.AttributeChanged:Once(function(name) print("once", name) end)
            door:SetAttribute("First", 1)
            door:SetAttribute("Second", 1)
            """);

        printed.ShouldBe(["heard First true", "once First"]);
    }

    [Fact]
    public void A_listener_connected_after_a_change_does_not_hear_it()
    {
        using ScriptLevel level = House.With("""
            local door = workspace.House.Door
            door:SetAttribute("Early", 1)
            door.AttributeChanged:Connect(function(name)
                print("heard", name)
                door.AttributeChanged:Connect(function(late) print("made in a call heard", late) end)
            end)
            door:SetAttribute("Late", 1)
            task.wait()
            print("next tick")
            door:SetAttribute("Later", 1)
            """);

        level.Play().Tick(2);

        level.Errors.ShouldBeEmpty();
        level.Printed.ShouldBe(["heard Late", "next tick", "heard Later", "made in a call heard Later"]);
    }

    [Fact]
    public void A_listener_that_sets_what_it_hears_of_is_called_again_until_the_value_holds_still()
    {
        using ScriptLevel level = House.With("""
            local door = workspace.House.Door
            door:GetAttributeChangedSignal("Count"):Connect(function()
                local count = door:GetAttribute("Count")
                print("count", count)
                door:SetAttribute("Count", math.min(count + 1, 4))
            end)
            door:SetAttribute("Count", 1)
            """);

        level.Play().Tick();

        level.Errors.ShouldBeEmpty();
        level.Printed.ShouldBe(["count 1", "count 2", "count 3", "count 4"]);
    }

    // Each call is a wake of its own, and a tick sends only so many of those
    // it made itself. The rest are dropped, which ends the loop.
    [Fact]
    public void A_listener_that_sets_off_its_own_signal_for_ever_is_cut_off_and_hears_the_next_change()
    {
        using ScriptLevel level = House.With("""
            local door = workspace.House.Door
            local calls = 0
            door:GetAttributeChangedSignal("Flip"):Connect(function()
                calls += 1
                door:SetAttribute("Flip", not door:GetAttribute("Flip"))
            end)
            door:SetAttribute("Flip", true)
            task.wait()
            print("calls in the first tick", calls)
            door:SetAttribute("Other", 1)
            door.AttributeChanged:Once(function(name) print("still hears", name) end)
            door:SetAttribute("Other", 2)
            """);
        level.Play();
        level.Entities.MaxDispatchesPerTick = 50;

        level.Tick();
        int dropped = level.Entities.DiscardedEventCount;
        level.Tick();

        // The one that was waiting when the tick began, and the fifty it made.
        level.Errors.ShouldBeEmpty();
        dropped.ShouldBe(1);
        level.Printed.ShouldBe(["calls in the first tick 51", "still hears Other"]);
    }

    [Fact]
    public void A_listener_whose_script_was_switched_off_is_not_called_and_the_others_are()
    {
        using var level = new ScriptLevel();
        level.Part("Board", new Vector3(3f, 1f, 3f));
        level.AddScript(level.Scene.Root, """
            workspace.Board.AttributeChanged:Connect(function() print("the hung script heard") end)
            task.wait()
            while true do end
            """, "Hangs");
        level.AddScript(level.Scene.Root, """
            workspace.Board.AttributeChanged:Connect(function(name) print("the other heard", name) end)
            task.wait(0.1)
            workspace.Board:SetAttribute("Score", 1)
            """, "Listens");
        level.Play().Tick();

        level.StopTheNextHangSoon();
        level.Tick(8);

        level.Errors.ShouldHaveSingleItem().ShouldContain("switched off");
        level.Printed.ShouldBe(["the other heard Score"]);
    }
}
