using SpectraEngine.Core.Scene;

namespace SpectraEngine.Bsp.Tests;

/// <summary>
/// The names a running level may give a node, and the reason it gives for
/// each one it refuses.
/// </summary>
public sealed class NodeNameTests
{
    private const string Door = "\U0001F6AA";

    [Theory]
    [InlineData("Door")]
    [InlineData("a")]
    [InlineData("Tür 2 (links)")]
    [InlineData("wild*")]
    [InlineData("not!first")]
    public void A_plain_name_is_taken(string name)
    {
        NodeName.WhyRefused(name).ShouldBeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData(" \t ")]
    public void A_name_of_nothing_but_spaces_is_refused(string name)
    {
        NodeName.WhyRefused(name).ShouldBe("a name cannot be empty");
    }

    [Theory]
    [InlineData("two\nlines")]
    [InlineData("tab\there")]
    [InlineData("nul\0inside")]
    [InlineData("next\u0085line")]
    [InlineData("line\u2028separator")]
    [InlineData("paragraph\u2029separator")]
    public void A_name_that_does_not_stay_on_one_line_is_refused(string name)
    {
        NodeName.WhyRefused(name).ShouldBe("a name cannot hold a line break or any other control character");
    }

    [Theory]
    [InlineData(" Door")]
    [InlineData("Door ")]
    [InlineData("\u00a0Door")]
    public void A_name_with_a_space_at_an_end_is_refused(string name)
    {
        NodeName.WhyRefused(name).ShouldBe("a name cannot start or end with a space");
    }

    // TargetNamePattern.Matches never matches a target that starts with one.
    [Theory]
    [InlineData("!")]
    [InlineData("!self")]
    [InlineData("!gate")]
    public void A_name_no_wire_could_reach_is_refused(string name)
    {
        NodeName.WhyRefused(name).ShouldBe("a name cannot start with !, since no wire can reach one that does");
    }

    [Fact]
    public void A_name_is_at_most_a_hundred_characters_long()
    {
        NodeName.WhyRefused(new string('x', NodeName.MaxLength)).ShouldBeNull();
        NodeName.WhyRefused(new string('x', NodeName.MaxLength + 1))
            .ShouldBe("a name is at most 100 characters long, and this one has 101");
    }

    [Fact]
    public void A_character_a_string_holds_as_two_counts_once()
    {
        string hundred = string.Concat(Enumerable.Repeat(Door, NodeName.MaxLength));
        hundred.Length.ShouldBe(2 * NodeName.MaxLength);

        NodeName.WhyRefused(hundred).ShouldBeNull();
        NodeName.WhyRefused(hundred + Door)
            .ShouldBe("a name is at most 100 characters long, and this one has 101");
    }
}
