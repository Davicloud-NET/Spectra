using System.Numerics;
using SpectraEngine.Core.Graphics;
using SpectraEngine.Core.Scene;

namespace SpectraEngine.Bsp.Tests;

/// <summary>
/// <see cref="Light.DisplayColor"/>: a light's linear colour as the colour a
/// person picks, which reads back as it was set.
/// </summary>
public sealed class LightDisplayColorTests
{
    [Fact]
    public void A_light_is_white_until_it_is_given_a_colour()
    {
        new Light().DisplayColor.ShouldBe(new Color3(1f, 1f, 1f));
    }

    [Fact]
    public void Setting_the_display_colour_sets_the_linear_colour_a_picked_colour_becomes()
    {
        var light = new Light { DisplayColor = new Color3(1f, 0.5f, 0.2f) };

        light.Color.ShouldBe(ColorSpace.SrgbToLinear(new Vector3(1f, 0.5f, 0.2f)));
    }

    [Fact]
    public void A_linear_colour_a_picker_made_reads_as_the_colour_that_was_picked()
    {
        var light = new Light { Color = ColorSpace.SrgbToLinear(new Vector3(1f, 128f / 255f, 10f / 255f)) };

        light.DisplayColor.ShouldBe(Color3.FromRgb(255f, 128f, 10f));
    }

    // A linear colour holds fewer steps than a display colour does in
    // places, so the light keeps the one it was given.
    [Fact]
    public void Every_display_colour_reads_back_as_it_was_set()
    {
        var light = new Light();
        var random = new Random(31337);
        for (int i = 0; i < 20_000; i++)
        {
            var set = new Color3(random.NextSingle(), random.NextSingle() * 0.05f, random.NextSingle() * 3f);

            light.DisplayColor = set;

            light.DisplayColor.ShouldBe(set);
        }
    }

    [Fact]
    public void A_linear_colour_set_afterwards_is_what_the_light_shows()
    {
        var light = new Light { DisplayColor = new Color3(0.2f, 0.4f, 0.6f) };

        light.Color = Vector3.One;

        light.DisplayColor.ShouldBe(new Color3(1f, 1f, 1f));
    }

    [Fact]
    public void A_copy_of_a_light_shows_the_same_colour()
    {
        var light = new Light { DisplayColor = Color3.FromRgb(12f, 200f, 77f) };

        Light copy = light.Clone();

        copy.Color.ShouldBe(light.Color);
        copy.DisplayColor.ShouldBe(light.DisplayColor);
    }
}
