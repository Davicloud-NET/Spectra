namespace SpectraEngine.Scripting.Tests;

/// <summary>
/// WaitForChild under a node that is in no level, where no scene event
/// tells of a child: the thread is woken when a child gets the name.
/// </summary>
public sealed class LooseChildWaitTests
{
    [Fact]
    public void A_child_renamed_under_a_node_in_no_level_wakes_the_thread_that_waits_for_it()
    {
        using ScriptLevel level = House.With("""
            local folder = Instance.new("Folder")
            local part = Instance.new("Part", folder)
            task.spawn(function() print("got", folder:WaitForChild("Key", 60).Name) end)
            task.wait()
            part.Name = "Key"
            task.wait()
            print("after")
            """);

        level.Play().Tick(5);

        level.Errors.ShouldBeEmpty();
        level.Printed.ShouldBe(["got Key", "after"]);
        level.Runtime.ShouldNotBeNull().WaitingThreadCount.ShouldBe(0);
    }
}
