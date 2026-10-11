using SpectraEngine.Core.Entities;

namespace SpectraEngine.Bsp.Tests;

/// <summary>
/// What becomes of the parameter a wire was authored with, as far as two
/// schemas can tell before the level plays.
/// </summary>
public sealed class ParameterVerdictTests
{
    private static readonly ParameterType Unsaid = ParameterType.NotSaid;
    private static readonly ParameterType Nothing = ParameterType.None;
    private static readonly ParameterType Number = ParameterType.Of(KeyvalueType.Float);
    private static readonly ParameterType Flag = ParameterType.Of(KeyvalueType.Bool);
    private static readonly ParameterType Words = ParameterType.Of(KeyvalueType.String);

    [Theory]
    [InlineData("3")]
    [InlineData("-0.25")]
    [InlineData(" 1e3 ")]
    public void A_number_for_an_input_that_takes_one_is_fine(string parameter)
    {
        ParameterText.Judge(Nothing, Number, parameter).ShouldBe(ParameterVerdict.Fine);
    }

    [Theory]
    [InlineData("three")]
    [InlineData("1 2")]
    [InlineData("1,5")]
    [InlineData("NaN")]
    public void Text_for_an_input_that_takes_a_number_cannot_be_read(string parameter)
    {
        ParameterText.Judge(Nothing, Number, parameter).ShouldBe(ParameterVerdict.Unreadable);
    }

    [Fact]
    public void A_flag_is_one_or_zero_and_true_cannot_be_read()
    {
        ParameterText.Judge(Nothing, Flag, "1").ShouldBe(ParameterVerdict.Fine);
        ParameterText.Judge(Nothing, Flag, "0").ShouldBe(ParameterVerdict.Fine);
        ParameterText.Judge(Nothing, Flag, "true").ShouldBe(ParameterVerdict.Unreadable);
        ParameterText.Judge(Nothing, Flag, "2").ShouldBe(ParameterVerdict.Unreadable);
    }

    [Fact]
    public void A_parameter_for_an_input_that_takes_none_is_not_taken()
    {
        ParameterText.Judge(Nothing, Nothing, "5").ShouldBe(ParameterVerdict.NotTaken);
        ParameterText.Judge(Unsaid, Nothing, "5").ShouldBe(ParameterVerdict.NotTaken);
        ParameterText.Judge(Nothing, Nothing, "").ShouldBe(ParameterVerdict.Fine);
        ParameterText.Judge(Nothing, Nothing, null).ShouldBe(ParameterVerdict.Fine);
    }

    [Fact]
    public void An_input_that_needs_a_number_and_gets_none_is_missing_one()
    {
        ParameterText.Judge(Nothing, Number, "").ShouldBe(ParameterVerdict.Missing);
        ParameterText.Judge(Nothing, Flag, null).ShouldBe(ParameterVerdict.Missing);
    }

    [Fact]
    public void An_input_that_also_works_without_its_number_is_fine_with_none()
    {
        ParameterType spare = ParameterType.Optional(KeyvalueType.Float);

        ParameterText.Judge(Nothing, spare, "").ShouldBe(ParameterVerdict.Fine);
        ParameterText.Judge(Nothing, spare, "5").ShouldBe(ParameterVerdict.Fine);
        ParameterText.Judge(Nothing, spare, "five").ShouldBe(ParameterVerdict.Unreadable);
    }

    [Fact]
    public void Empty_text_is_text_so_an_input_that_takes_text_is_never_missing_it()
    {
        ParameterText.Judge(Nothing, Words, "").ShouldBe(ParameterVerdict.Fine);
        ParameterText.Judge(Nothing, Words, "anything at all").ShouldBe(ParameterVerdict.Fine);
        ParameterText.Judge(Nothing, ParameterType.Of(KeyvalueType.TargetName), "").ShouldBe(ParameterVerdict.Fine);
        ParameterText.Judge(Nothing, ParameterType.Of(KeyvalueType.NodeRef), "").ShouldBe(ParameterVerdict.Fine);
    }

    [Fact]
    public void An_output_that_sends_its_own_value_replaces_whatever_the_wire_says()
    {
        ParameterText.Judge(Number, Number, "5").ShouldBe(ParameterVerdict.Replaced);
        ParameterText.Judge(Words, Nothing, "5").ShouldBe(ParameterVerdict.Replaced);
        ParameterText.Judge(Number, Unsaid, "five").ShouldBe(ParameterVerdict.Replaced);
    }

    [Fact]
    public void An_output_that_sends_its_own_value_needs_no_parameter_on_the_wire()
    {
        // The counter's number arrives, so the input is not missing one.
        ParameterText.Judge(Number, Number, "").ShouldBe(ParameterVerdict.Fine);
        ParameterText.Judge(Number, Flag, "").ShouldBe(ParameterVerdict.Fine);
        ParameterText.Judge(Words, Nothing, "").ShouldBe(ParameterVerdict.Fine);
    }

    [Fact]
    public void An_input_whose_class_does_not_say_is_never_judged()
    {
        ParameterText.Judge(Nothing, Unsaid, "anything").ShouldBe(ParameterVerdict.Fine);
        ParameterText.Judge(Unsaid, Unsaid, "").ShouldBe(ParameterVerdict.Fine);
    }

    [Fact]
    public void An_output_whose_class_does_not_say_may_send_the_value_so_nothing_is_missing()
    {
        ParameterText.Judge(Unsaid, Number, "").ShouldBe(ParameterVerdict.Fine);

        // What is typed is still read, and still cannot be.
        ParameterText.Judge(Unsaid, Number, "five").ShouldBe(ParameterVerdict.Unreadable);
        ParameterText.Judge(Unsaid, Number, "5").ShouldBe(ParameterVerdict.Fine);
    }

    [Fact]
    public void Three_numbers_are_needed_where_a_vector_is_taken()
    {
        ParameterType position = ParameterType.Of(KeyvalueType.Vec3);

        ParameterText.Judge(Nothing, position, "1 2 3").ShouldBe(ParameterVerdict.Fine);
        ParameterText.Judge(Nothing, position, "1 2").ShouldBe(ParameterVerdict.Unreadable);
        ParameterText.Judge(Nothing, position, "").ShouldBe(ParameterVerdict.Missing);
    }
}
