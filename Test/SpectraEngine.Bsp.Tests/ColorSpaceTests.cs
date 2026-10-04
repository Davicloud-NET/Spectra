using System;
using System.Numerics;
using SpectraEngine.Core.Graphics;

namespace SpectraEngine.Bsp.Tests;

/// <summary>The sRGB transfer function, held against the published constants.</summary>
// A round trip alone proves nothing: pow(2.2) and its inverse round-trip too.
public sealed class ColorSpaceTests
{
    [Fact]
    public void The_endpoints_are_fixed_points()
    {
        ColorSpace.SrgbToLinear(0f).ShouldBe(0f);
        ColorSpace.SrgbToLinear(1f).ShouldBe(1f, 1e-6f);
        ColorSpace.LinearToSrgb(0f).ShouldBe(0f);
        ColorSpace.LinearToSrgb(1f).ShouldBe(1f, 1e-6f);
    }

    [Fact]
    public void Mid_grey_decodes_to_the_published_value()
    {
        ColorSpace.SrgbToLinear(0.5f).ShouldBe(0.2140f, 5e-4f);

        // The byte a paint program calls mid grey.
        ColorSpace.SrgbToLinear(128 / 255f).ShouldBe(0.2158f, 5e-4f);
    }

    [Fact]
    public void The_linear_toe_is_a_straight_line_not_a_power_curve()
    {
        // Below 0.04045 the curve is value/12.92.
        ColorSpace.SrgbToLinear(0.04f).ShouldBe(0.04f / 12.92f, 1e-7f);
        ColorSpace.SrgbToLinear(0.02f).ShouldBe(0.02f / 12.92f, 1e-7f);

        // pow(2.2) is off by more than an order of magnitude down here.
        float approximation = MathF.Pow(0.02f, 2.2f);
        ColorSpace.SrgbToLinear(0.02f).ShouldBeGreaterThan(approximation * 5f);
    }

    [Fact]
    public void Encode_inverts_decode_across_the_range()
    {
        for (int i = 0; i <= 255; i++)
        {
            float srgb = i / 255f;
            ColorSpace.LinearToSrgb(ColorSpace.SrgbToLinear(srgb))
                .ShouldBe(srgb, 1e-5f, $"code {i}");
        }
    }

    [Fact]
    public void Alpha_is_never_converted()
    {
        Vector4 converted = ColorSpace.SrgbToLinear(new Vector4(0.5f, 0.5f, 0.5f, 0.5f));

        converted.W.ShouldBe(0.5f);
        converted.X.ShouldBe(0.2140f, 5e-4f);
    }

    [Fact]
    public void The_sky_clear_colour_is_linear_cornflower_blue()
    {
        ClearColors.Sky.X.ShouldBeLessThan(0.392f);
        ColorSpace.LinearToSrgb(ClearColors.Sky.X).ShouldBe(0.392f, 1e-4f);
        ColorSpace.LinearToSrgb(ClearColors.Sky.Y).ShouldBe(0.584f, 1e-4f);
        ColorSpace.LinearToSrgb(ClearColors.Sky.Z).ShouldBe(0.929f, 1e-4f);
        ClearColors.Sky.W.ShouldBe(1f);

        ClearColors.Wireframe.ShouldBe(new Vector4(0f, 0f, 0f, 1f));
    }

    [Fact]
    public void Only_multi_channel_formats_can_be_srgb()
    {
        // Neither DXGI nor GL has a one-channel sRGB format.
        TextureFormatInfo.SupportsSrgb(TextureFormat.Rgba8).ShouldBeTrue();
        TextureFormatInfo.SupportsSrgb(TextureFormat.Rgb8).ShouldBeTrue();
        TextureFormatInfo.SupportsSrgb(TextureFormat.R8).ShouldBeFalse();

        TextureFormatInfo.Resolve(TextureFormat.Rgba8, TextureColorSpace.Srgb)
            .ShouldBe(TextureColorSpace.Srgb);
        TextureFormatInfo.Resolve(TextureFormat.R8, TextureColorSpace.Srgb)
            .ShouldBe(TextureColorSpace.Linear);

        TextureFormatInfo.Resolve(TextureFormat.Rgba8, TextureColorSpace.Linear)
            .ShouldBe(TextureColorSpace.Linear);
    }
}
