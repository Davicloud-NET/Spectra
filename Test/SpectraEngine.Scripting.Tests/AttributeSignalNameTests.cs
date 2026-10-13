namespace SpectraEngine.Scripting.Tests;

/// <summary>
/// GetAttributeChangedSignal for a name no attribute can have. Such a name
/// is never set, so its signal never fires and nothing is kept for it.
/// </summary>
public sealed class AttributeSignalNameTests
{
    [Fact]
    public void A_signal_asked_for_a_name_no_attribute_can_have_is_kept_nowhere()
    {
        using ScriptLevel level = House.With("""
            local door = workspace.House.Door
            for round = 1, 200 do
                door:GetAttributeChangedSignal("not a name " .. round)
                door:GetAttributeChangedSignal(string.rep("n", 100 + round))
            end
            door:GetAttributeChangedSignal("")
            door:GetAttributeChangedSignal("RBXScore")
            door:GetAttributeChangedSignal("\255")
            print("asked")
            """);

        level.Play().Tick();

        level.Errors.ShouldBeEmpty();
        level.Printed.ShouldBe(["asked"]);
        level.Runtime.ShouldNotBeNull().AttributeNodeCount.ShouldBe(0);
    }

    [Fact]
    public void Such_a_signal_is_a_signal_all_the_same_and_takes_listeners_that_are_never_called()
    {
        using ScriptLevel level = House.With("""
            local door = workspace.House.Door
            local never = door:GetAttributeChangedSignal("not a name")
            print(never, typeof(never), never == door:GetAttributeChangedSignal("not a name"))
            print(door:GetAttributeChangedSignal("\255"), door:GetAttributeChangedSignal(string.rep("n", 101)))
            local connection = never:Connect(function() print("heard") end)
            print(connection.Connected, never ~= door:GetAttributeChangedSignal("Score"), never ~= door.AttributeChanged)
            door:SetAttribute("Score", 1)
            task.wait()
            connection:Disconnect()
            print(connection.Connected)
            """);

        level.Play().Tick(2);

        level.Errors.ShouldBeEmpty();
        level.Printed.ShouldBe(
        [
            "Signal AttributeChanged Instance true",
            "Signal AttributeChanged Signal AttributeChanged",
            "true true true",
            "false",
        ]);
    }

    // The rule is the one SetAttribute refuses a name by, so the two agree.
    [Theory]
    [InlineData("Score", true)]
    [InlineData("_private_9", true)]
    [InlineData("rbx_lower", true)]
    [InlineData("my score", false)]
    [InlineData("a.b", false)]
    [InlineData("RBXScore", false)]
    [InlineData("", false)]
    public void A_signal_is_kept_for_the_names_SetAttribute_takes_and_for_no_other(string name, bool isKept)
    {
        using ScriptLevel level = House.With($"""
            local door = workspace.House.Door
            door:GetAttributeChangedSignal("{name}")
            print((pcall(door.SetAttribute, door, "{name}", nil)))
            """);

        level.Play().Tick();

        level.Errors.ShouldBeEmpty();
        level.Printed.ShouldBe([isKept ? "true" : "false"]);
        level.Runtime.ShouldNotBeNull().AttributeNodeCount.ShouldBe(isKept ? 1 : 0);
    }
}
