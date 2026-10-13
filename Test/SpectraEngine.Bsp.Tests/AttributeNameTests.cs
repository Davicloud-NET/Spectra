using SpectraEngine.Core.Scripting;

namespace SpectraEngine.Bsp.Tests;

/// <summary>
/// The names an attribute may have, which is Roblox's rule as Roblox Studio
/// 0.742 answered it on 2026-10-13, and what is said of a name that breaks it.
/// </summary>
public sealed class AttributeNameTests
{
    private const string Rule =
        "An attribute's name is 1 to 100 letters, digits and any of _ . - /, and does not start with RBX";

    [Theory]
    [InlineData("Score")]
    [InlineData("score_2")]
    [InlineData("9lives")]
    [InlineData("rbx_in_small_letters")]
    [InlineData("NotRBXInFront")]
    public void A_name_of_letters_digits_and_underscores_is_taken(string name)
    {
        AttributeName.IsValid(name).ShouldBeTrue();
    }

    // Studio took each of these in SetAttribute.
    [Theory]
    [InlineData("a.b")]
    [InlineData("a-b")]
    [InlineData("a/b")]
    [InlineData("rbxa")]
    [InlineData("9a")]
    [InlineData("_")]
    public void A_period_a_hyphen_a_slash_and_a_digit_in_front_are_taken_as_Studio_takes_them(string name)
    {
        AttributeName.IsValid(name).ShouldBeTrue();
    }

    [Theory]
    [InlineData(".")]
    [InlineData("-")]
    [InlineData("/")]
    [InlineData("../up")]
    [InlineData("ui/hud.title-2")]
    [InlineData("-.-/_")]
    public void Each_of_the_three_can_stand_anywhere_in_a_name(string name)
    {
        AttributeName.IsValid(name).ShouldBeTrue();
    }

    // Studio refused "a b", the empty name and RBXa.
    [Theory]
    [InlineData("")]
    [InlineData("a b")]
    [InlineData("RBXa")]
    public void A_space_the_empty_name_and_RBX_in_front_are_refused_as_Studio_refuses_them(string name)
    {
        AttributeName.IsValid(name).ShouldBeFalse();
        AttributeName.Refusal(name).ShouldBe($"'{name}' is not a valid attribute name. {Rule}");
    }

    [Theory]
    [InlineData("RBX")]
    [InlineData("RBX.a")]
    [InlineData("a\\b")]
    [InlineData("a:b")]
    [InlineData("a,b")]
    [InlineData("a+b")]
    [InlineData("a.b ")]
    [InlineData("line\nbreak")]
    public void A_name_with_anything_else_is_refused_with_the_rule(string name)
    {
        AttributeName.IsValid(name).ShouldBeFalse();
        AttributeName.Refusal(name).ShouldBe($"'{name}' is not a valid attribute name. {Rule}");
    }

    [Fact]
    public void The_rule_is_said_in_one_sentence()
    {
        AttributeName.Rule.ShouldBe(Rule);
    }

    // The letter by its code point: an escape in source does not survive every editing tool.
    [Fact]
    public void A_letter_outside_ASCII_is_no_letter_of_a_name()
    {
        AttributeName.IsValid($"T{(char)0xFC}r").ShouldBeFalse();
    }

    // Studio took 100 characters and refused 101.
    [Fact]
    public void A_hundred_characters_are_taken_and_one_more_is_refused_and_not_said_back()
    {
        AttributeName.IsValid(new string('n', AttributeName.MaxLength)).ShouldBeTrue();
        AttributeName.IsValid(new string('/', AttributeName.MaxLength)).ShouldBeTrue();
        AttributeName.IsValid(new string('n', AttributeName.MaxLength + 1)).ShouldBeFalse();
        AttributeName.Refusal(new string('n', 101)).ShouldStartWith("A name of 101 characters is not a valid attribute name. ");
    }
}
