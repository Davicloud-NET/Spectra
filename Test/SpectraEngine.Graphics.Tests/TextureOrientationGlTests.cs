using SpectraEngine.Core.Assets;
using SpectraEngine.Core.Graphics;
using SpectraEngine.Core.Graphics.OpenGL;
using System;
using System.IO;
using Texture = SpectraEngine.Core.Graphics.Texture;

namespace SpectraEngine.Graphics.Tests;

/// <summary>
/// Which way up an uploaded texture arrives on OpenGL, measured against a real
/// driver with an asymmetric fixture.
/// </summary>
// An upside-down texture raises no error, so the fixture is drawn through a
// quad with no per-backend UV adjustment and its four corners are read back.
[Collection(GlRendererCollection.Name)]
public sealed class TextureOrientationGlTests
{
    private readonly GlRendererFixture _fixture;

    private const int TargetSize = 16;

    public TextureOrientationGlTests(GlRendererFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public void The_fixture_texture_has_four_distinct_corners()
    {
        // A symmetric fixture would make every other test here pass vacuously.
        DecodedImage image = DecodeFixture();

        // ImageDecoder flips rows: y = 0 is the picture's bottom edge.
        Corner(image, 0, image.Height - 1).ShouldBe(TextureOrientationProbe.Quadrant.Red);
        Corner(image, image.Width - 1, image.Height - 1).ShouldBe(TextureOrientationProbe.Quadrant.Green);
        Corner(image, 0, 0).ShouldBe(TextureOrientationProbe.Quadrant.Blue);
        Corner(image, image.Width - 1, 0).ShouldBe(TextureOrientationProbe.Quadrant.Yellow);
    }

    [Fact]
    public void The_readback_counts_y_from_the_bottom_of_the_picture()
    {
        // White 1x1 source: this checks the readback itself, with no texture
        // orientation in the path.
        OpenGLRenderer renderer = _fixture.Renderer;
        Texture white = renderer.CreateTexture(
            [255, 255, 255, 255], 1, 1, TextureFormat.Rgba8, TextureColorSpace.Linear,
            TextureFilter.Nearest, TextureWrap.Clamp);
        RenderTarget output = renderer.CreateRenderTarget(
            new RenderTargetDesc(TargetSize, TargetSize, TextureFormat.Rgba8, TextureColorSpace.Linear));

        try
        {
            renderer.DrawOrientationQuad(white, output, OrientationQuad.Coverage.TopHalf);

            (byte topR, _, _, _) = renderer.ReadTargetPixel(output, TargetSize / 2, TargetSize - 1);
            (byte bottomR, _, _, _) = renderer.ReadTargetPixel(output, TargetSize / 2, 0);

            topR.ShouldBeGreaterThan((byte)120,
                "the quad covers clip y 0 to 1, which rasterises to the top of the viewport, " +
                "so the readback's highest y must be inside it");
            bottomR.ShouldBeLessThan((byte)80,
                "the bottom half was cleared and never drawn, so the readback's y = 0 must be black");
        }
        finally
        {
            renderer.DestroyRenderTarget(output);
            renderer.DestroyTexture(white);
        }
    }

    [Fact]
    public void An_uploaded_texture_renders_the_way_the_image_was_authored()
    {
        OpenGLRenderer renderer = _fixture.Renderer;
        DecodedImage image = DecodeFixture();

        // Linear on both sides, so no sRGB conversion sits between the file's
        // bytes and the readback.
        Texture source = renderer.CreateTexture(
            image.Pixels, image.Width, image.Height, image.Format, TextureColorSpace.Linear,
            TextureFilter.Nearest, TextureWrap.Clamp);
        RenderTarget output = renderer.CreateRenderTarget(
            new RenderTargetDesc(TargetSize, TargetSize, TextureFormat.Rgba8, TextureColorSpace.Linear));

        try
        {
            renderer.DrawOrientationQuad(source, output, OrientationQuad.Coverage.Full);

            var reading = new TextureOrientationProbe.Reading(
                Read(renderer, output, 0, TargetSize - 1),
                Read(renderer, output, TargetSize - 1, TargetSize - 1),
                Read(renderer, output, 0, 0),
                Read(renderer, output, TargetSize - 1, 0));

            reading.MatchesAuthoredImage.ShouldBeTrue(
                $"OpenGL rendered the fixture as: {reading}. Verdict: {reading.Verdict}");
        }
        finally
        {
            renderer.DestroyRenderTarget(output);
            renderer.DestroyTexture(source);
        }
    }

    [Fact]
    public void A_vertically_mirrored_upload_reads_as_flipped()
    {
        // Shows the probe can report a flip, so the test above is not vacuous.
        OpenGLRenderer renderer = _fixture.Renderer;
        DecodedImage image = DecodeFixture();

        Texture source = renderer.CreateTexture(
            MirrorRows(image), image.Width, image.Height, image.Format, TextureColorSpace.Linear,
            TextureFilter.Nearest, TextureWrap.Clamp);
        RenderTarget output = renderer.CreateRenderTarget(
            new RenderTargetDesc(TargetSize, TargetSize, TextureFormat.Rgba8, TextureColorSpace.Linear));

        try
        {
            renderer.DrawOrientationQuad(source, output, OrientationQuad.Coverage.Full);

            var reading = new TextureOrientationProbe.Reading(
                Read(renderer, output, 0, TargetSize - 1),
                Read(renderer, output, TargetSize - 1, TargetSize - 1),
                Read(renderer, output, 0, 0),
                Read(renderer, output, TargetSize - 1, 0));

            reading.IsVerticallyFlipped.ShouldBeTrue($"mirrored rows rendered as: {reading}");
        }
        finally
        {
            renderer.DestroyRenderTarget(output);
            renderer.DestroyTexture(source);
        }
    }

    private static byte[] MirrorRows(DecodedImage image)
    {
        var mirrored = new byte[image.Height * image.Stride];
        for (int y = 0; y < image.Height; y++)
        {
            System.ReadOnlySpan<byte> source =
                image.Pixels.Slice((image.Height - 1 - y) * image.Stride, image.Stride);
            source.CopyTo(mirrored.AsSpan(y * image.Stride, image.Stride));
        }
        return mirrored;
    }

    private static TextureOrientationProbe.Quadrant Read(
        Renderer renderer, RenderTarget target, int x, int y)
    {
        (byte r, byte g, byte b, _) = renderer.ReadTargetPixel(target, x, y);
        return TextureOrientationProbe.Classify(r, g, b);
    }

    private static TextureOrientationProbe.Quadrant Corner(DecodedImage image, int x, int y)
    {
        System.ReadOnlySpan<byte> texel = image.GetPixel(x, y);
        return TextureOrientationProbe.Classify(texel[0], texel[1], texel[2]);
    }

    private static DecodedImage DecodeFixture() =>
        ImageDecoder.DecodeFile(Path.Combine(ContentRoot.Path, "Textures", "orientation_probe.png"));
}
