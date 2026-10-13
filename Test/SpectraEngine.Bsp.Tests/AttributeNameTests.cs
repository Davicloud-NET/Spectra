using SpectraEngine.Core.Scripting;

namespace SpectraEngine.Bsp.Tests;

/// <summary>
/// The names an attribute may have, which is Roblox's rule, and what is
/// said of a name that breaks it.
/// </summary>
public sealed class AttributeNameTests
{
    [Theory]
    [InlineData("Score")]
    [InlineData("score_2")]
    [InlineData("_")]
    [InlineData("9lives")]
    [InlineData("rbx_in_small_letters")]
    [InlineData("NotRBXInFront")]
    public void A_name_of_letters_digits_and_underscores_is_taken(string name)
    {
        AttributeName.IsValid(name).ShouldBeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("my score")]
    [InlineData("score-2")]
    [InlineData("a.b")]
    [InlineData("a/b")]
    [InlineData("RBX")]
    [InlineData("RBXScore")]
    [InlineData("line\nbreak")]
    public void A_name_with_anything_else_and_one_that_starts_with_RBX_are_refused(string name)
    {
        AttributeName.IsValid(name).ShouldBeFalse();
        AttributeName.Refusal(name).ShouldBe(
            $"'{name}' is not a valid attribute name. An attribute's name is 1 to 100 letters, digits and " +
            "underscores, and does not start with RBX");
    }

    // The letter by its code point: an escape in source does not survive every editing tool.
    [Fact]
    public void A_letter_outside_ASCII_is_no_letter_of_a_name()
    {
        AttributeName.IsValid($"T{(char)0xFC}r").ShouldBeFalse();
    }

    [Fact]
    public void A_hundred_characters_are_taken_and_one_more_is_refused_and_not_said_back()
    {
        AttributeName.IsValid(new string('n', AttributeName.MaxLength)).ShouldBeTrue();
        AttributeName.IsValid(new string('n', AttributeName.MaxLength + 1)).ShouldBeFalse();
        AttributeName.Refusal(new string('n', 101)).ShouldStartWith("A name of 101 characters is not a valid attribute name. ");
    }
}
