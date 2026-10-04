using SpectraEngine.Core.Graphics;
using SpectraEngine.Editor.Shell;
using System.Numerics;

namespace SpectraEngine.Editor.Tests;

/// <summary>The colour picker's conversions.</summary>
public sealed class ColorMathTests
{
    [Theory]
    [InlineData(0f, 1f, 0f, 0f)]      // red
    [InlineData(120f, 0f, 1f, 0f)]    // green
    [InlineData(240f, 0f, 0f, 1f)]    // blue
    public void The_primaries_round_trip_through_hsv(float hue, float r, float g, float b)
    {
        Vector3 srgb = ColorMath.HsvToSrgb(hue, 1f, 1f);

        Assert.Equal(r, srgb.X, 3);
        Assert.Equal(g, srgb.Y, 3);
        Assert.Equal(b, srgb.Z, 3);

        (float back, float saturation, float value) = ColorMath.SrgbToHsv(srgb);
        Assert.Equal(hue, back, 2);
        Assert.Equal(1f, saturation, 3);
        Assert.Equal(1f, value, 3);
    }

    [Fact]
    public void A_grey_has_no_saturation_and_reports_hue_zero()
    {
        (float hue, float saturation, float value) = ColorMath.SrgbToHsv(new Vector3(0.5f, 0.5f, 0.5f));

        Assert.Equal(0f, hue);
        Assert.Equal(0f, saturation, 4);
        Assert.Equal(0.5f, value, 4);
    }

    [Fact]
    public void Black_is_representable_and_does_not_divide_by_zero()
    {
        (float hue, float saturation, float value) = ColorMath.SrgbToHsv(Vector3.Zero);

        Assert.Equal(0f, hue);
        Assert.Equal(0f, saturation);
        Assert.Equal(0f, value);
    }

    [Fact]
    public void Hex_round_trips()
    {
        Assert.True(ColorMath.TryParseHex("#8C8C99", out Vector3 srgb));
        Assert.Equal("#8C8C99", ColorMath.ToHex(srgb));
    }

    [Theory]
    [InlineData("#80")]
    [InlineData("#8C8C9")]
    [InlineData("#8C8C99FF")]
    [InlineData("nonsense")]
    [InlineData(null)]
    public void A_partial_or_alpha_hex_is_refused(string? text)
    {
        // Six digits only, same as the panel's hex cell.
        Assert.False(ColorMath.TryParseHex(text, out _));
    }

    [Fact]
    public void The_linear_conversions_are_the_engines_own()
    {
        var srgb = new Vector3(0.2f, 0.5f, 0.9f);

        Assert.Equal(ColorSpace.SrgbToLinear(srgb), ColorMath.SrgbToLinear(srgb));
        Assert.Equal(srgb.X, ColorMath.LinearToSrgb(ColorMath.SrgbToLinear(srgb)).X, 4);
    }

    [Fact]
    public void An_over_bright_linear_colour_clamps_rather_than_wrapping()
    {
        // A light's colour is not bounded at 1.
        string hex = ColorMath.ToHex(ColorMath.LinearToSrgb(new Vector3(4f, 4f, 4f)));

        Assert.Equal("#FFFFFF", hex);
    }
}

/// <summary>The picker's state.</summary>
public sealed class ColorPickerModelTests
{
    [Fact]
    public void Loading_a_colour_sets_the_markers_from_it()
    {
        var model = new ColorPickerModel();
        model.Load(ColorMath.SrgbToLinear(new Vector3(1f, 0f, 0f)));

        Assert.Equal(0f, model.Hue, 2);
        Assert.Equal(1f, model.Saturation, 3);
        Assert.Equal(1f, model.Value, 3);
        Assert.False(model.IsMixed);
        Assert.Equal("#FF0000", model.Hex);
    }

    [Fact]
    public void Moving_raises_a_linear_colour()
    {
        var model = new ColorPickerModel();
        model.Load(ColorMath.SrgbToLinear(new Vector3(1f, 0f, 0f)));

        var raised = new List<Vector3>();
        model.Changed += raised.Add;

        model.SetSaturationValue(0.5f, 0.5f);

        Vector3 only = Assert.Single(raised);
        Assert.Equal(model.Linear, only);

        // The scene stores linear. sRGB 0.5 is about 0.21 linear.
        Assert.True(only.X < ColorMath.SrgbToLinear(new Vector3(0.5f)).X + 0.01f);
    }

    [Fact]
    public void Dragging_to_black_keeps_the_hue_the_user_chose()
    {
        var model = new ColorPickerModel();
        model.SetHue(200f);
        model.SetSaturationValue(1f, 0f);

        Assert.Equal(200f, model.Hue, 2);
        Assert.Equal("#000000", model.Hex);

        model.SetSaturationValue(1f, 1f);
        Assert.Equal("#00AAFF", model.Hex);
    }

    [Fact]
    public void A_mixed_selection_opens_at_mid_grey_and_stops_being_mixed_on_the_first_move()
    {
        var model = new ColorPickerModel();
        model.Load(new Vector3(float.NaN, float.NaN, float.NaN));

        Assert.True(model.IsMixed);
        Assert.Equal("#808080", model.Hex);

        model.SetHue(90f);
        Assert.False(model.IsMixed);
    }

    [Fact]
    public void A_typed_hex_moves_the_colour_and_a_bad_one_changes_nothing()
    {
        var model = new ColorPickerModel();
        model.Load(ColorMath.SrgbToLinear(new Vector3(1f, 0f, 0f)));

        Assert.True(model.TrySetHex("#00FF00"));
        Assert.Equal(120f, model.Hue, 1);

        Assert.False(model.TrySetHex("#0F"));
        Assert.Equal(120f, model.Hue, 1);
    }

    [Fact]
    public void A_typed_grey_leaves_the_hue_strip_where_it_was()
    {
        var model = new ColorPickerModel();
        model.SetHue(200f);

        Assert.True(model.TrySetHex("#808080"));
        Assert.Equal(200f, model.Hue, 2);
    }

    [Fact]
    public void The_hue_wraps_rather_than_clamping()
    {
        var model = new ColorPickerModel();

        model.SetHue(370f);
        Assert.Equal(10f, model.Hue, 3);

        model.SetHue(-10f);
        Assert.Equal(350f, model.Hue, 3);
    }
}
