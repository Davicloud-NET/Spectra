using SpectraEngine.Core.Entities;

namespace SpectraEngine.Bsp.Tests;

/// <summary>What an input takes or an output sends, as a schema holds it.</summary>
public sealed class ParameterTypeTests
{
    public static TheoryData<KeyvalueType> EveryType => new(Enum.GetValues<KeyvalueType>());

    [Fact]
    public void A_schema_that_says_nothing_is_not_one_that_says_no_value()
    {
        ParameterType unsaid = default;

        unsaid.ShouldBe(ParameterType.NotSaid);
        unsaid.IsSaid.ShouldBeFalse();
        unsaid.IsNone.ShouldBeFalse();
        unsaid.HasValue.ShouldBeFalse();

        ParameterType.None.IsSaid.ShouldBeTrue();
        ParameterType.None.IsNone.ShouldBeTrue();
        ParameterType.None.HasValue.ShouldBeFalse();
        ParameterType.None.ShouldNotBe(ParameterType.NotSaid);
    }

    [Fact]
    public void A_value_has_a_type_and_may_be_one_that_can_be_left_out()
    {
        ParameterType needed = ParameterType.Of(KeyvalueType.Float);
        ParameterType spare = ParameterType.Optional(KeyvalueType.Float);

        needed.HasValue.ShouldBeTrue();
        needed.IsSaid.ShouldBeTrue();
        needed.IsNone.ShouldBeFalse();
        needed.IsOptional.ShouldBeFalse();
        needed.ValueType.ShouldBe(KeyvalueType.Float);

        spare.HasValue.ShouldBeTrue();
        spare.IsOptional.ShouldBeTrue();
        spare.ValueType.ShouldBe(KeyvalueType.Float);
        spare.ShouldNotBe(needed);
    }

    [Fact]
    public void Two_types_are_equal_when_they_say_the_same()
    {
        ParameterType.Of(KeyvalueType.Vec3).ShouldBe(ParameterType.Of(KeyvalueType.Vec3));
        ParameterType.Of(KeyvalueType.Vec3).ShouldNotBe(ParameterType.Of(KeyvalueType.Color));

        // A bool's own byte is zero, which must not read as nothing.
        ParameterType.Of(KeyvalueType.Bool).ShouldNotBe(ParameterType.None);
        ParameterType.Of(KeyvalueType.Bool).ShouldNotBe(ParameterType.NotSaid);
    }

    [Theory]
    [MemberData(nameof(EveryType))]
    public void A_wire_carries_a_number_a_flag_text_a_vector_or_a_node_and_nothing_a_keyvalue_alone_means(
        KeyvalueType type)
    {
        bool carried = type is KeyvalueType.Bool or KeyvalueType.Int or KeyvalueType.Float or KeyvalueType.String
            or KeyvalueType.Vec2 or KeyvalueType.Vec3 or KeyvalueType.Vec4 or KeyvalueType.Color
            or KeyvalueType.Angles or KeyvalueType.TargetName or KeyvalueType.NodeRef;

        ParameterType.CanCarry(type).ShouldBe(carried);

        if (carried)
        {
            ParameterType.Of(type).ValueType.ShouldBe(type);
            ParameterType.Optional(type).ValueType.ShouldBe(type);
        }
        else
        {
            Should.Throw<ArgumentException>(() => ParameterType.Of(type)).Message.ShouldContain("No wire carries");
            Should.Throw<ArgumentException>(() => ParameterType.Optional(type));
        }
    }

    [Fact]
    public void A_type_no_build_names_is_not_carried()
    {
        ParameterType.CanCarry((KeyvalueType)200).ShouldBeFalse();
    }

    [Theory]
    [InlineData(KeyvalueType.Bool, "bool")]
    [InlineData(KeyvalueType.Int, "int")]
    [InlineData(KeyvalueType.Float, "float")]
    [InlineData(KeyvalueType.String, "string")]
    [InlineData(KeyvalueType.Vec2, "vec2")]
    [InlineData(KeyvalueType.Vec3, "vec3")]
    [InlineData(KeyvalueType.Vec4, "vec4")]
    [InlineData(KeyvalueType.Color, "color")]
    [InlineData(KeyvalueType.Angles, "angles")]
    [InlineData(KeyvalueType.TargetName, "targetname")]
    [InlineData(KeyvalueType.NodeRef, "noderef")]
    public void A_type_is_printed_as_one_lower_case_word(KeyvalueType type, string word)
    {
        ParameterType.Of(type).ToString().ShouldBe(word);
        ParameterType.Optional(type).ToString().ShouldBe(word + "?");
    }

    [Fact]
    public void No_value_and_nothing_said_are_printed_in_words()
    {
        ParameterType.None.ToString().ShouldBe("none");
        ParameterType.NotSaid.ToString().ShouldBe("not said");
    }

    [Fact]
    public void Ports_made_from_names_say_nothing_about_what_they_carry()
    {
        EntityPort[] ports = EntityPort.Named("Open", "Close");

        ports.ShouldBe([new EntityPort("Open", ParameterType.NotSaid), new EntityPort("Close", ParameterType.NotSaid)]);
        EntityPort.Named().ShouldBeEmpty();
    }
}
