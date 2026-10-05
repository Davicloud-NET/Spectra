using SpectraEngine.Editor.Shell.Logic;
using static SpectraEngine.Editor.Tests.Logic.LogicFixture;

namespace SpectraEngine.Editor.Tests.Logic;

/// <summary>What the status row says of a wire the Logic view made or removed.</summary>
public sealed class LogicWireTextTests
{
    [Fact]
    public void A_new_wire_is_said_as_who_fires_what_at_whom()
    {
        LogicWireText.Wired("StartZone", Wire("OnStartTouch", "Lift", "Open"))
            .ShouldBe("Wired StartZone.OnStartTouch to Lift.Open.");
    }

    [Fact]
    public void A_new_wire_with_no_input_says_where_to_set_it()
    {
        LogicWireText.Wired("StartZone", Wire("OnStartTouch", "Thing", ""))
            .ShouldBe("Wired StartZone.OnStartTouch to Thing. Set its input under Sends in Properties.");
    }

    [Fact]
    public void A_removed_wire_is_said_the_same_way()
    {
        LogicWireText.Removed("StartZone", Wire("OnTrigger", "StartDoor", "Open"), alike: 1)
            .ShouldBe("Removed StartZone.OnTrigger to StartDoor.Open.");
    }

    [Fact]
    public void A_wire_removed_from_an_edge_of_several_says_how_many_there_were()
    {
        LogicWireText.Removed("StartZone", Wire("OnTrigger", "StartDoor", "Open"), alike: 2)
            .ShouldBe("Removed 1 of 2 wires from StartZone.OnTrigger to StartDoor.Open.");
    }

    [Theory]
    [InlineData("!self", "Wired Relay.OnTrigger to Toggle on itself.")]
    [InlineData("!activator", "Wired Relay.OnTrigger to Toggle on the activator.")]
    [InlineData("!caller", "Wired Relay.OnTrigger to Toggle on the caller.")]
    [InlineData("", "Wired Relay.OnTrigger to Toggle on nothing.")]
    public void A_target_that_is_not_a_name_is_said_in_words(string target, string sentence)
    {
        LogicWireText.Wired("Relay", Wire("OnTrigger", target, "Toggle")).ShouldBe(sentence);
    }

    [Theory]
    [InlineData("!self", "Removed Zone.OnStartTouch to itself.")]
    [InlineData("!activator", "Removed Zone.OnStartTouch to the activator.")]
    [InlineData("", "Removed Zone.OnStartTouch to nothing.")]
    public void A_target_that_is_not_a_name_is_said_alone_when_the_wire_sends_no_input(string target, string sentence)
    {
        LogicWireText.Removed("Zone", Wire("OnStartTouch", target, ""), alike: 1).ShouldBe(sentence);
    }

    [Fact]
    public void An_entity_with_no_name_is_called_by_its_class()
    {
        LogicWireText.Called("", "logic_relay").ShouldBe("logic_relay");
    }
}
