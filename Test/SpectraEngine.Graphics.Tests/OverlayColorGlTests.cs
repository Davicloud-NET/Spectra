using System.Numerics;
using SpectraEngine.Core.Graphics;
using SpectraEngine.Core.Graphics.OpenGL;
using SpectraEngine.Core.Scene;
using Texture = SpectraEngine.Core.Graphics.Texture;

namespace SpectraEngine.Graphics.Tests;

/// <summary>
/// The overlay's lines are given colours in linear light, and the target
/// encodes them. A line meant to show a theme colour has to be given that
/// colour's linear value.
/// </summary>
// The 3D view draws an entity's icon in its group's tint this way, and the
// tint has to come out as the shell shows it.
[Collection(GlRendererCollection.Name)]
public sealed class OverlayColorGlTests
{
    private const int Size = 64;

    // The theme's blue for movers, as the shell writes it.
    private static readonly (byte R, byte G, byte B) Theme = (0x7F, 0xB4, 0xFF);

    private readonly GlRendererFixture _fixture;

    public OverlayColorGlTests(GlRendererFixture fixture) => _fixture = fixture;

    [Fact]
    public void A_line_given_a_theme_colours_linear_value_shows_as_that_colour()
    {
        var linear = ColorSpace.SrgbToLinear(new Vector3(Theme.R / 255f, Theme.G / 255f, Theme.B / 255f));

        (byte r, byte g, byte b) = DrawAcrossTheMiddle(linear);

        ((int)r).ShouldBeInRange(Theme.R - 2, Theme.R + 2);
        ((int)g).ShouldBeInRange(Theme.G - 2, Theme.G + 2);
        ((int)b).ShouldBeInRange(Theme.B - 2, Theme.B + 2);
    }

    [Fact]
    public void A_line_given_the_colour_as_written_shows_too_bright()
    {
        // The control: were the overlay not encoded, this is the one that would match.
        (byte r, byte g, _) = DrawAcrossTheMiddle(new Vector3(Theme.R / 255f, Theme.G / 255f, Theme.B / 255f));

        ((int)r).ShouldBeGreaterThan(Theme.R + 30);
        ((int)g).ShouldBeGreaterThan(Theme.G + 20);
    }

    // Lines through the middle of the view, drawn over a resolved frame into a
    // target that encodes as the window and the editor's viewport do. The
    // middle pixel, read back.
    private (byte R, byte G, byte B) DrawAcrossTheMiddle(Vector3 color)
    {
        OpenGLRenderer renderer = _fixture.Renderer;
        RenderTarget output = renderer.CreateRenderTarget(
            new RenderTargetDesc(Size, Size, ColorSpace: TextureColorSpace.Srgb));
        var view = new RenderView();

        var scene = new Scene("overlay");
        scene.Camera.Position = new Vector3(0f, 0f, 4f);
        scene.Camera.LookAt(Vector3.Zero);

        try
        {
            renderer.DebugDraw.Clear();
            scene.BuildRenderView(scene.Camera, view);
            renderer.Render(scene, view, 1.0 / 60.0);

            // Several hairlines a part of a pixel apart, so the middle pixel
            // is covered whichever way the driver rounds.
            for (int i = -6; i <= 6; i++)
            {
                float y = i * 0.02f;
                renderer.DebugDraw.Line(new Vector3(-1f, y, 0f), new Vector3(1f, y, 0f), color);
            }

            Texture frame = renderer.SceneTarget.ShouldNotBeNull().ColorTexture.ShouldNotBeNull();
            renderer.ResolveForTest(frame, output, scene);

            (byte r, byte g, byte b, _) = renderer.ReadTargetPixel(output, Size / 2, Size / 2);
            return (r, g, b);
        }
        finally
        {
            renderer.DebugDraw.Clear();
            renderer.DestroyRenderTarget(output);
        }
    }
}
