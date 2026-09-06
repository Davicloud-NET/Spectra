using SpectraEngine.Core.Inspection;
using SpectraEngine.Editing.Commands;

namespace SpectraEngine.Editing.Tests;

/// <summary>
/// What a property will accept, and the words for saying it did not.
/// </summary>
/// <remarks>
/// <b>One rule, two readers.</b> The editor refuses a light range of zero
/// because the setter throws rather than clamps, and a command carrying one
/// would throw halfway through an open transaction. The panel refuses the same
/// value before posting so it can say why. Two hand-written copies of that rule
/// would agree exactly until one of them was corrected.
/// </remarks>
public sealed class PropertyLimitsTests
{
    [Theory]
    [InlineData(0f)]
    [InlineData(-1f)]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    public void Range_refuses_anything_that_is_not_a_positive_number(float value)
    {
        string refusal = PropertyLimits.Refusal(PropertyId.LightRange, value).ShouldNotBeNull();

        // The bound is NAMED, because "invalid" tells somebody nothing about
        // what to type instead.
        refusal.ShouldContain("greater than 0");
    }

    [Fact]
    public void Range_allows_an_ordinary_positive_number()
    {
        PropertyLimits.Refusal(PropertyId.LightRange, 8f).ShouldBeNull();
    }

    [Theory]
    [InlineData(-0.001f)]
    [InlineData(float.NaN)]
    public void Intensity_refuses_negatives_and_non_numbers(float value)
    {
        PropertyLimits.Refusal(PropertyId.LightIntensity, value).ShouldNotBeNull();
    }

    [Fact]
    public void Intensity_allows_zero_because_an_unlit_light_is_a_thing_people_make()
    {
        PropertyLimits.Refusal(PropertyId.LightIntensity, 0f).ShouldBeNull();
    }

    [Fact]
    public void Everything_else_is_allowed_here()
    {
        // The angles and extents clamp in Light's own setters rather than
        // throwing, so a value past their ends means "as far as it goes" and is
        // not this class's business.
        PropertyLimits.Refusal(PropertyId.LightOuterAngle, -20f).ShouldBeNull();
        PropertyLimits.Refusal(PropertyId.LightWidth, 0f).ShouldBeNull();
        PropertyLimits.Refusal(PropertyId.Position, float.NaN).ShouldBeNull();
    }

    [Fact]
    public void The_expected_text_names_the_format_and_the_bound()
    {
        PropertyLimits.Expected(PropertyId.LightColor, PropertyKind.Color).ShouldBe("#RRGGBB");
        PropertyLimits.Expected(PropertyId.Position, PropertyKind.Vector3).ShouldBe("a number");
        PropertyLimits.Expected(PropertyId.LightRange, PropertyKind.Number)
            .ShouldContain("greater than 0");
        PropertyLimits.Expected(PropertyId.LightIntensity, PropertyKind.Number)
            .ShouldContain("0 or more");
    }
}
