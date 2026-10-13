using SpectraEngine.Core.Scene;

namespace SpectraEngine.Bsp.Tests;

/// <summary>
/// <see cref="Color3"/> against Roblox's Color3. Where Roblox's documentation
/// says nothing, the test says what was chosen here.
/// </summary>
public sealed class Color3Tests
{
    [Fact]
    public void Parts_of_0_to_255_are_the_same_colour_as_parts_of_0_to_1()
    {
        Color3.FromRgb(255f, 0f, 51f).ShouldBe(new Color3(1f, 0f, 0.2f));
        Color3.FromRgb(0f, 0f, 0f).ShouldBe(default);
    }

    // Roblox's documentation asks for parts in range and does not say what
    // becomes of one outside it. Chosen: it is kept, as Color3.new keeps it.
    [Fact]
    public void A_part_outside_its_range_is_kept_as_it_is()
    {
        new Color3(2f, -1f, 0.5f).R.ShouldBe(2f);
        new Color3(2f, -1f, 0.5f).G.ShouldBe(-1f);
        Color3.FromRgb(510f, -255f, 0f).ShouldBe(new Color3(2f, -1f, 0f));
    }

    [Theory]
    [InlineData(0f, 1f, 1f, 1f, 0f, 0f)]
    [InlineData(1f / 3f, 1f, 1f, 0f, 1f, 0f)]
    [InlineData(2f / 3f, 1f, 1f, 0f, 0f, 1f)]
    [InlineData(1f / 6f, 1f, 1f, 1f, 1f, 0f)]
    [InlineData(0.5f, 1f, 0.5f, 0f, 0.5f, 0.5f)]
    [InlineData(0.25f, 0f, 0.75f, 0.75f, 0.75f, 0.75f)]
    [InlineData(0.9f, 0.5f, 0f, 0f, 0f, 0f)]
    public void A_hue_a_saturation_and_a_value_make_the_colour_they_name(
        float h, float s, float v, float r, float g, float b)
    {
        Color3 made = Color3.FromHsv(h, s, v);

        made.R.ShouldBe(r, 1e-6f);
        made.G.ShouldBe(g, 1e-6f);
        made.B.ShouldBe(b, 1e-6f);
    }

    // Roblox's documentation asks for a hue from 0 to 1 and says nothing of
    // one outside. Chosen: the hue goes round, so 1 is red again.
    [Fact]
    public void A_hue_goes_round()
    {
        Color3.FromHsv(1f, 1f, 1f).ShouldBe(new Color3(1f, 0f, 0f));
        Color3.FromHsv(1.5f, 1f, 1f).ShouldBe(Color3.FromHsv(0.5f, 1f, 1f));
        Color3.FromHsv(-0.5f, 1f, 1f).ShouldBe(Color3.FromHsv(0.5f, 1f, 1f));
    }

    // Roblox's reference prints 1 1 1 for the hue, saturation and value of
    // Color3.fromRGB(255, 0, 0), and 0.3333333 1 1 for green.
    [Theory]
    [InlineData(1f, 0f, 0f, 1f, 1f, 1f)]
    [InlineData(0f, 1f, 0f, 1f / 3f, 1f, 1f)]
    [InlineData(0f, 0f, 1f, 2f / 3f, 1f, 1f)]
    [InlineData(1f, 0f, 1f, 5f / 6f, 1f, 1f)]
    [InlineData(1f, 1f, 0f, 1f / 6f, 1f, 1f)]
    [InlineData(0f, 1f, 1f, 0.5f, 1f, 1f)]
    [InlineData(1f, 0.5f, 0f, 1f / 12f, 1f, 1f)]
    [InlineData(1f, 0f, 0.5f, 11f / 12f, 1f, 1f)]
    public void A_colour_gives_its_hue_its_saturation_and_its_value(
        float r, float g, float b, float h, float s, float v)
    {
        (float hue, float saturation, float value) = new Color3(r, g, b).ToHsv();

        hue.ShouldBe(h, 1e-6f);
        saturation.ShouldBe(s, 1e-6f);
        value.ShouldBe(v, 1e-6f);
    }

    // Roblox's sample only shows it for full red. Taken here for every red
    // with as much green as blue, which is what the sum that gives 1 for
    // full red gives for those too.
    [Theory]
    [InlineData(1f, 0f, 0f, 1f)]
    [InlineData(0.5f, 0.25f, 0.25f, 0.5f)]
    [InlineData(0.2f, 0.1f, 0.1f, 0.5f)]
    public void A_red_with_as_much_green_as_blue_has_a_hue_of_1_and_not_0(float r, float g, float b, float s)
    {
        new Color3(r, g, b).ToHsv().ShouldBe((1f, s, r));
    }

    [Fact]
    public void A_grey_has_no_hue_and_no_saturation()
    {
        new Color3(0.4f, 0.4f, 0.4f).ToHsv().ShouldBe((0f, 0f, 0.4f));
        default(Color3).ToHsv().ShouldBe((0f, 0f, 0f));
    }

    // Roblox's documentation says nothing of a hue that is no number.
    // Chosen: it is not made into one, and the colour has such a part too.
    [Theory]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    public void A_hue_that_is_no_number_makes_a_colour_with_a_part_that_is_no_number(float hue)
    {
        Color3 made = Color3.FromHsv(hue, 1f, 1f);

        (float.IsNaN(made.R) || float.IsNaN(made.G) || float.IsNaN(made.B)).ShouldBeTrue();
    }

    [Theory]
    [InlineData(0.1f, 0.8f, 0.6f)]
    [InlineData(0.55f, 0.3f, 1f)]
    [InlineData(0.99f, 1f, 0.25f)]
    public void A_colour_made_from_a_hue_gives_the_hue_back(float h, float s, float v)
    {
        (float hue, float saturation, float value) = Color3.FromHsv(h, s, v).ToHsv();

        hue.ShouldBe(h, 1e-5f);
        saturation.ShouldBe(s, 1e-5f);
        value.ShouldBe(v, 1e-5f);
    }

    [Theory]
    [InlineData("#FF8000", 255f, 128f, 0f)]
    [InlineData("ff8000", 255f, 128f, 0f)]
    [InlineData("#0a0B0c", 10f, 11f, 12f)]
    [InlineData("#f80", 255f, 136f, 0f)]
    [InlineData("FfF", 255f, 255f, 255f)]
    public void Six_hex_digits_or_three_are_a_colour_with_or_without_the_sign(string hex, float r, float g, float b)
    {
        Color3.TryFromHex(hex, out Color3 color).ShouldBeTrue();

        color.ShouldBe(Color3.FromRgb(r, g, b));
    }

    [Theory]
    [InlineData("")]
    [InlineData("#")]
    [InlineData("ff80")]
    [InlineData("#ff80000")]
    [InlineData("gg8000")]
    [InlineData("##f80")]
    [InlineData("ff 800")]
    [InlineData("0xff8000")]
    public void Text_that_is_no_hex_colour_is_refused(string hex)
    {
        Color3.TryFromHex(hex, out Color3 color).ShouldBeFalse();

        color.ShouldBe(default);
    }

    // Roblox's documentation says six digits and no sign, and not which
    // case the letters are. Chosen: lower case.
    [Fact]
    public void A_colour_is_written_as_six_hex_digits_in_lower_case_with_no_sign()
    {
        Color3.FromRgb(255f, 128f, 0f).ToHex().ShouldBe("ff8000");
        default(Color3).ToHex().ShouldBe("000000");
        new Color3(1f, 1f, 1f).ToHex().ShouldBe("ffffff");
    }

    // Roblox's documentation does not say how a part between two steps is
    // written, or one out of range. Chosen: the nearest step, and a part
    // brought into range first.
    [Fact]
    public void A_part_between_two_steps_is_written_as_the_nearest_and_one_out_of_range_as_the_end()
    {
        new Color3(0.5f, 0.498f, 0.502f).ToHex().ShouldBe("807f80");
        new Color3(2f, -1f, float.NaN).ToHex().ShouldBe("ff0000");
    }

    [Fact]
    public void What_is_written_as_hex_reads_back_as_the_same_colour()
    {
        for (int step = 0; step <= 255; step += 5)
        {
            Color3 color = Color3.FromRgb(step, 255 - step, (step * 7) % 256);

            Color3.TryFromHex(color.ToHex(), out Color3 read).ShouldBeTrue();
            read.ShouldBe(color, color.ToHex());
        }
    }

    [Fact]
    public void A_colour_part_of_the_way_to_another_is_each_part_that_far()
    {
        var from = new Color3(0f, 1f, 0.5f);
        var to = new Color3(1f, 0f, 0.5f);

        from.Lerp(to, 0f).ShouldBe(from);
        from.Lerp(to, 1f).ShouldBe(to);
        from.Lerp(to, 0.25f).ShouldBe(new Color3(0.25f, 0.75f, 0.5f));
    }

    // Roblox's documentation does not say whether the alpha is kept between
    // 0 and 1. Chosen: it is not, as with a Vector3 and a CFrame.
    [Fact]
    public void An_alpha_past_the_goal_goes_past_the_goal()
    {
        new Color3(0f, 0f, 0f).Lerp(new Color3(1f, 0.5f, 0f), 2f).ShouldBe(new Color3(2f, 1f, 0f));
        new Color3(0f, 0f, 0f).Lerp(new Color3(1f, 0.5f, 0f), -1f).ShouldBe(new Color3(-1f, -0.5f, 0f));
    }

    // Roblox prints each part with six digits, as C's %g does.
    [Theory]
    [InlineData(1f, 0.5f, 0f, "1, 0.5, 0")]
    [InlineData(1f, 128f / 255f, 0f, "1, 0.501961, 0")]
    [InlineData(163f / 255f, 162f / 255f, 165f / 255f, "0.639216, 0.635294, 0.647059")]
    [InlineData(0.1f, 0.2f, 0.3f, "0.1, 0.2, 0.3")]
    [InlineData(22f / 255f, 1e-7f, 1234567f, "0.0862745, 1e-07, 1.23457e+06")]
    [InlineData(float.NaN, float.PositiveInfinity, float.NegativeInfinity, "nan, inf, -inf")]
    public void A_colour_prints_as_Roblox_prints_one(float r, float g, float b, string printed)
    {
        new Color3(r, g, b).ToString().ShouldBe(printed);
    }

    [Fact]
    public void Two_colours_are_equal_when_their_parts_are()
    {
        (new Color3(0.1f, 0.2f, 0.3f) == new Color3(0.1f, 0.2f, 0.3f)).ShouldBeTrue();
        (new Color3(0.1f, 0.2f, 0.3f) == new Color3(0.1f, 0.2f, 0.30000004f)).ShouldBeFalse();
        (Color3.FromRgb(255f, 0f, 0f) == new Color3(1f, 0f, 0f)).ShouldBeTrue();
    }
}
