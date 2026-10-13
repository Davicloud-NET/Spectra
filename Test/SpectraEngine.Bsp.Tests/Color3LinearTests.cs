using System.Globalization;
using System.Numerics;
using SpectraEngine.Core.Graphics;
using SpectraEngine.Core.Scene;

namespace SpectraEngine.Bsp.Tests;

/// <summary>
/// A <see cref="Color3"/> is a colour as a screen shows it and a light holds
/// a linear one. Crossing there and back has to give the colour that went in.
/// </summary>
public sealed class Color3LinearTests
{
    [Fact]
    public void A_colour_becomes_the_linear_value_a_picked_colour_becomes()
    {
        var picked = new Vector3(1f, 128f / 255f, 0.2f);

        new Color3(picked.X, picked.Y, picked.Z).ToLinear().ShouldBe(ColorSpace.SrgbToLinear(picked));
    }

    [Fact]
    public void White_and_black_cross_as_themselves()
    {
        Color3.FromLinear(Vector3.One).ShouldBe(new Color3(1f, 1f, 1f));
        Color3.FromLinear(Vector3.Zero).ShouldBe(default);
        new Color3(1f, 1f, 1f).ToLinear().ShouldBe(Vector3.One);
    }

    // The curve's own way back gives 0.99999994 for white, which is why
    // FromLinear looks round it.
    [Fact]
    public void The_curve_alone_does_not_bring_white_back()
    {
        ColorSpace.LinearToSrgb(1f).ShouldNotBe(1f);
    }

    [Fact]
    public void Every_colour_of_255_steps_comes_back_from_linear_as_it_went_in()
    {
        for (int step = 0; step <= 255; step++)
        {
            float part = step / 255f;

            Color3.FromLinear(new Color3(part, part, part).ToLinear()).R.ShouldBe(part, $"step {step}");
        }
    }

    // Two neighbouring colours can become one linear value, and nothing can
    // tell which of the two it was. What comes back is one of them.
    [Fact]
    public void Any_colour_comes_back_as_one_that_becomes_the_same_linear_value()
    {
        var random = new Random(2026);
        for (int i = 0; i < 50_000; i++)
        {
            float part = random.NextSingle() * 1.5f;
            Vector3 linear = new Color3(part, part, part).ToLinear();
            Color3 back = Color3.FromLinear(linear);

            back.ToLinear().ShouldBe(linear, part.ToString("R", CultureInfo.InvariantCulture));
            back.R.ShouldBe(part, 1e-6f);
        }
    }

    // A linear value typed by hand can lie between two that a colour
    // becomes. It reads as the nearest colour.
    [Fact]
    public void A_linear_value_no_colour_becomes_reads_as_the_nearest_colour()
    {
        Color3 read = Color3.FromLinear(new Vector3(0.8f, 0.3f, 4f));

        read.R.ShouldBe(ColorSpace.LinearToSrgb(0.8f), 1e-6f);
        read.G.ShouldBe(ColorSpace.LinearToSrgb(0.3f), 1e-6f);
        read.B.ShouldBe(ColorSpace.LinearToSrgb(4f), 1e-5f);
    }

    [Fact]
    public void A_value_that_is_no_number_stays_none()
    {
        float.IsNaN(Color3.FromLinear(new Vector3(float.NaN, 0f, 0f)).R).ShouldBeTrue();
    }
}
