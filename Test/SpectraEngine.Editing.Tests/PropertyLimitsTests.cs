using SpectraEngine.Core.Inspection;
using SpectraEngine.Editing.Commands;

namespace SpectraEngine.Editing.Tests;

/// <summary>What a property will accept, and the refusal text when it does not.</summary>
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
        // Angles and extents clamp in Light's setters; they do not throw.
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
