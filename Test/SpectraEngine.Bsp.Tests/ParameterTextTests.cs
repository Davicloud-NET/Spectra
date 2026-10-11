using SpectraEngine.Core.Entities;
using System.Numerics;

namespace SpectraEngine.Bsp.Tests;

/// <summary>
/// A typed value to the text a wire carries and back: what a script hands an
/// input, and what it gets from an output.
/// </summary>
public sealed class ParameterTextTests
{
    private static readonly Guid Door = Guid.Parse("3f2a9c1e-5b7d-4e0a-9c41-0d2f6a8b1c33");

    private static ParameterType Of(KeyvalueType type) => ParameterType.Of(type);

    private static string Format(ParameterType parameter, ParameterValue value)
    {
        ParameterText.TryFormat(parameter, in value, out string text).ShouldBeTrue();
        return text;
    }

    private static ParameterValue Parse(ParameterType parameter, string text)
    {
        ParameterText.TryParse(parameter, text, out ParameterValue value).ShouldBeTrue();
        return value;
    }

    [Fact]
    public void A_number_becomes_the_text_a_keyvalue_of_a_float_is_written_as()
    {
        Format(Of(KeyvalueType.Float), ParameterValue.Of(3)).ShouldBe("3");
        Format(Of(KeyvalueType.Float), ParameterValue.Of(-0.5)).ShouldBe("-0.5");
        Format(Of(KeyvalueType.Float), ParameterValue.Of(0.1)).ShouldBe(KeyvalueWire.Format(0.1f));
    }

    [Theory]
    [InlineData(1f / 3f, "0.33333334")]
    [InlineData(0.30000004f, "0.30000004")]
    [InlineData(16777217f, "16777216")]
    [InlineData(1e-8f, "1E-08")]
    [InlineData(3.4028235e38f, "3.4028235E+38")]
    [InlineData(-123456.79f, "-123456.79")]
    public void A_float_that_needs_many_digits_comes_back_as_the_same_float(float single, string text)
    {
        ParameterType type = Of(KeyvalueType.Float);

        Format(type, ParameterValue.Of(single)).ShouldBe(text);

        ParameterValue read = Parse(type, text);
        read.Kind.ShouldBe(ParameterValueKind.Number);
        ((float)read.Number).ShouldBe(single);
        Format(type, read).ShouldBe(text);
    }

    [Fact]
    public void A_number_read_from_text_is_the_number_the_text_spells()
    {
        // Not the float widened, which would hand a script 0.10000000149011612.
        Parse(Of(KeyvalueType.Float), "0.1").Number.ShouldBe(0.1);
        Parse(Of(KeyvalueType.Float), " 2.5 ").Number.ShouldBe(2.5);
    }

    [Fact]
    public void A_number_too_big_for_a_float_or_not_a_number_at_all_is_refused()
    {
        ParameterType type = Of(KeyvalueType.Float);

        ParameterText.TryFormat(type, ParameterValue.Of(1e39), out _).ShouldBeFalse();
        ParameterText.TryFormat(type, ParameterValue.Of(double.NaN), out _).ShouldBeFalse();
        ParameterText.TryFormat(type, ParameterValue.Of(double.PositiveInfinity), out _).ShouldBeFalse();
        ParameterText.TryParse(type, "1e39", out _).ShouldBeFalse();
        ParameterText.TryParse(type, "NaN", out _).ShouldBeFalse();
        ParameterText.TryParse(type, "three", out _).ShouldBeFalse();
        ParameterText.TryParse(type, "1,5", out _).ShouldBeFalse();
    }

    [Fact]
    public void A_whole_number_keeps_every_digit_and_a_fraction_is_refused()
    {
        ParameterType type = Of(KeyvalueType.Int);

        Format(type, ParameterValue.Of(16777217)).ShouldBe("16777217");
        Format(type, ParameterValue.Of(int.MinValue)).ShouldBe("-2147483648");
        Parse(type, "2147483647").Number.ShouldBe(int.MaxValue);

        ParameterText.TryFormat(type, ParameterValue.Of(2.5), out _).ShouldBeFalse();
        ParameterText.TryFormat(type, ParameterValue.Of(2147483648d), out _).ShouldBeFalse();
        ParameterText.TryFormat(type, ParameterValue.Of(double.NaN), out _).ShouldBeFalse();
        ParameterText.TryParse(type, "2.5", out _).ShouldBeFalse();
    }

    [Fact]
    public void A_boolean_is_one_or_zero()
    {
        ParameterType type = Of(KeyvalueType.Bool);

        Format(type, ParameterValue.Of(true)).ShouldBe("1");
        Format(type, ParameterValue.Of(false)).ShouldBe("0");
        Parse(type, "1").ShouldBe(ParameterValue.Of(true));
        Parse(type, "0").ShouldBe(ParameterValue.Of(false));

        ParameterText.TryParse(type, "true", out _).ShouldBeFalse();
        ParameterText.TryParse(type, "", out _).ShouldBeFalse();
    }

    [Fact]
    public void A_vector_is_its_numbers_with_spaces_between()
    {
        var position = new Vector3(1.5f, -2f, 1f / 3f);

        string text = Format(Of(KeyvalueType.Vec3), ParameterValue.Of(position));

        text.ShouldBe("1.5 -2 0.33333334");
        ParameterValue read = Parse(Of(KeyvalueType.Vec3), text);
        read.Kind.ShouldBe(ParameterValueKind.Vector);
        read.VectorSize.ShouldBe(3);
        read.Vector.ShouldBe(new Vector4(position, 0f));
        read.ShouldBe(ParameterValue.Of(position));
    }

    [Theory]
    [InlineData(KeyvalueType.Vec3)]
    [InlineData(KeyvalueType.Color)]
    [InlineData(KeyvalueType.Angles)]
    public void A_position_a_colour_and_angles_are_all_three_numbers(KeyvalueType type)
    {
        var value = new Vector3(0.25f, 90f, -180f);

        string text = Format(Of(type), ParameterValue.Of(value));

        text.ShouldBe("0.25 90 -180");
        Parse(Of(type), text).ShouldBe(ParameterValue.Of(value));
    }

    [Fact]
    public void Two_and_four_numbers_round_trip_too()
    {
        var two = new Vector2(0.5f, -8f);
        var four = new Vector4(1f, 2f, 3f, 0.125f);

        Format(Of(KeyvalueType.Vec2), ParameterValue.Of(two)).ShouldBe("0.5 -8");
        Parse(Of(KeyvalueType.Vec2), "0.5 -8").ShouldBe(ParameterValue.Of(two));
        Format(Of(KeyvalueType.Vec4), ParameterValue.Of(four)).ShouldBe("1 2 3 0.125");
        Parse(Of(KeyvalueType.Vec4), "1 2 3 0.125").ShouldBe(ParameterValue.Of(four));
    }

    [Fact]
    public void A_vector_of_the_wrong_size_or_with_a_number_that_is_not_finite_is_refused()
    {
        ParameterText.TryFormat(Of(KeyvalueType.Vec3), ParameterValue.Of(new Vector2(1f, 2f)), out _).ShouldBeFalse();
        ParameterText.TryFormat(Of(KeyvalueType.Vec2), ParameterValue.Of(Vector3.One), out _).ShouldBeFalse();
        ParameterText.TryFormat(
            Of(KeyvalueType.Vec3), ParameterValue.Of(new Vector3(1f, float.NaN, 0f)), out _).ShouldBeFalse();
        ParameterText.TryParse(Of(KeyvalueType.Vec3), "1 2", out _).ShouldBeFalse();
        ParameterText.TryParse(Of(KeyvalueType.Vec3), "1 2 3 4", out _).ShouldBeFalse();
    }

    [Theory]
    [InlineData("Main Door")]
    [InlineData("door*")]
    [InlineData("!activator")]
    [InlineData("  spaced  ")]
    [InlineData("")]
    public void A_node_by_name_is_its_name_untouched(string name)
    {
        ParameterType type = Of(KeyvalueType.TargetName);

        Format(type, ParameterValue.Of(name)).ShouldBe(name);

        ParameterValue read = Parse(type, name);
        read.Kind.ShouldBe(ParameterValueKind.Text);
        read.Text.ShouldBe(name);
    }

    [Fact]
    public void A_node_by_id_is_its_id_with_hyphens()
    {
        ParameterType type = Of(KeyvalueType.NodeRef);

        Format(type, ParameterValue.Of(Door)).ShouldBe("3f2a9c1e-5b7d-4e0a-9c41-0d2f6a8b1c33");

        ParameterValue read = Parse(type, "3f2a9c1e-5b7d-4e0a-9c41-0d2f6a8b1c33");
        read.Kind.ShouldBe(ParameterValueKind.NodeId);
        read.NodeId.ShouldBe(Door);
    }

    [Fact]
    public void No_node_is_the_empty_text_as_it_is_for_a_keyvalue()
    {
        ParameterType type = Of(KeyvalueType.NodeRef);

        Format(type, ParameterValue.None).ShouldBe("");
        Parse(type, "").ShouldBe(ParameterValue.None);

        ParameterText.TryParse(type, "door1", out _).ShouldBeFalse();
    }

    [Fact]
    public void The_id_of_no_node_is_no_value_as_a_class_holds_it_in_a_keyvalue()
    {
        ParameterType type = Of(KeyvalueType.NodeRef);

        ParameterValue.Of(Guid.Empty).ShouldBe(ParameterValue.None);
        Format(type, ParameterValue.Of(Guid.Empty)).ShouldBe("");
        Parse(type, "00000000-0000-0000-0000-000000000000").ShouldBe(ParameterValue.None);

        // So it is refused where a value is needed.
        ParameterText.TryFormat(Of(KeyvalueType.Float), ParameterValue.Of(Guid.Empty), out _).ShouldBeFalse();
    }

    [Fact]
    public void Text_is_carried_as_it_is()
    {
        ParameterType type = Of(KeyvalueType.String);

        Format(type, ParameterValue.Of("two words, 1.5")).ShouldBe("two words, 1.5");
        Parse(type, "two words, 1.5").ShouldBe(ParameterValue.Of("two words, 1.5"));
        Parse(type, "").ShouldBe(ParameterValue.Of(""));
    }

    [Fact]
    public void A_value_of_another_kind_than_the_input_takes_is_refused()
    {
        ParameterText.TryFormat(Of(KeyvalueType.Float), ParameterValue.Of("3"), out _).ShouldBeFalse();
        ParameterText.TryFormat(Of(KeyvalueType.Float), ParameterValue.Of(true), out _).ShouldBeFalse();
        ParameterText.TryFormat(Of(KeyvalueType.Bool), ParameterValue.Of(1), out _).ShouldBeFalse();
        ParameterText.TryFormat(Of(KeyvalueType.String), ParameterValue.Of(3), out _).ShouldBeFalse();
        ParameterText.TryFormat(Of(KeyvalueType.TargetName), ParameterValue.Of(Door), out _).ShouldBeFalse();
        ParameterText.TryFormat(Of(KeyvalueType.NodeRef), ParameterValue.Of("door1"), out _).ShouldBeFalse();
        ParameterText.TryFormat(Of(KeyvalueType.Vec3), ParameterValue.Of(1), out _).ShouldBeFalse();
    }

    [Fact]
    public void An_input_that_takes_nothing_is_given_nothing()
    {
        Format(ParameterType.None, ParameterValue.None).ShouldBe("");

        ParameterText.TryFormat(ParameterType.None, ParameterValue.Of(5), out _).ShouldBeFalse();
        ParameterText.TryFormat(ParameterType.None, ParameterValue.Of(""), out _).ShouldBeFalse();
    }

    [Fact]
    public void An_output_that_sends_nothing_gives_no_value_whatever_the_wire_says()
    {
        Parse(ParameterType.None, "").ShouldBe(ParameterValue.None);
        Parse(ParameterType.None, "5").ShouldBe(ParameterValue.None);
    }

    [Fact]
    public void A_value_that_is_needed_cannot_be_left_out()
    {
        ParameterText.TryFormat(Of(KeyvalueType.Float), ParameterValue.None, out _).ShouldBeFalse();
        ParameterText.TryFormat(Of(KeyvalueType.String), ParameterValue.None, out _).ShouldBeFalse();
        ParameterText.TryParse(Of(KeyvalueType.Float), "", out _).ShouldBeFalse();
    }

    [Fact]
    public void A_value_that_may_be_left_out_is_the_empty_text_when_it_is()
    {
        ParameterType type = ParameterType.Optional(KeyvalueType.Float);

        Format(type, ParameterValue.None).ShouldBe("");
        Parse(type, "").ShouldBe(ParameterValue.None);
        Format(type, ParameterValue.Of(5)).ShouldBe("5");
        Parse(type, "5").ShouldBe(ParameterValue.Of(5));

        // Leaving it out is the only other choice.
        ParameterText.TryParse(type, "five", out _).ShouldBeFalse();
        ParameterText.TryFormat(type, ParameterValue.Of("five"), out _).ShouldBeFalse();
    }

    [Fact]
    public void With_no_type_said_a_value_is_written_the_way_a_person_would_type_it()
    {
        ParameterType unsaid = ParameterType.NotSaid;

        Format(unsaid, ParameterValue.None).ShouldBe("");
        Format(unsaid, ParameterValue.Of(true)).ShouldBe("1");
        Format(unsaid, ParameterValue.Of(5)).ShouldBe("5");
        Format(unsaid, ParameterValue.Of(16777217)).ShouldBe("16777217");
        Format(unsaid, ParameterValue.Of(0.25)).ShouldBe("0.25");
        Format(unsaid, ParameterValue.Of("door1")).ShouldBe("door1");
        Format(unsaid, ParameterValue.Of(new Vector3(1f, 2f, 3f))).ShouldBe("1 2 3");
        Format(unsaid, ParameterValue.Of(Door)).ShouldBe("3f2a9c1e-5b7d-4e0a-9c41-0d2f6a8b1c33");

        ParameterText.TryFormat(unsaid, ParameterValue.Of(double.NaN), out _).ShouldBeFalse();
    }

    [Fact]
    public void With_no_type_said_text_is_read_as_the_text_it_is()
    {
        Parse(ParameterType.NotSaid, "3").ShouldBe(ParameterValue.Of("3"));
        Parse(ParameterType.NotSaid, "").ShouldBe(ParameterValue.Of(""));

        ParameterText.TryParse(ParameterType.NotSaid, null, out ParameterValue value).ShouldBeTrue();
        value.ShouldBe(ParameterValue.Of(""));
    }

    [Fact]
    public void A_value_holds_one_kind_and_reads_as_nothing_of_the_others()
    {
        ParameterValue number = ParameterValue.Of(2.5);

        number.Kind.ShouldBe(ParameterValueKind.Number);
        number.Number.ShouldBe(2.5);
        number.Boolean.ShouldBeFalse();
        number.Text.ShouldBe("");
        number.VectorSize.ShouldBe(0);
        number.NodeId.ShouldBe(Guid.Empty);

        ParameterValue.None.Kind.ShouldBe(ParameterValueKind.None);
        default(ParameterValue).ShouldBe(ParameterValue.None);
        ParameterValue.Of(true).Number.ShouldBe(0d);
        ParameterValue.Of(0).ShouldNotBe(ParameterValue.Of(false));
    }

    [Fact]
    public void A_counters_value_reaches_a_script_as_the_number_and_goes_back_as_the_same_text()
    {
        // What a counter at 3 sends on OutValue, and what SetValue then reads.
        string sent = KeyvalueWire.Format(3f);

        ParameterValue heard = Parse(Of(KeyvalueType.Float), sent);

        heard.Number.ShouldBe(3d);
        Format(Of(KeyvalueType.Float), heard).ShouldBe(sent);
    }
}
