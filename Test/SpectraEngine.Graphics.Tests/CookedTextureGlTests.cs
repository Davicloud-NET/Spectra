using BCnEncoder.Encoder;
using Spectra.Kitchen.Cooking;
using Spectra.Kitchen.Images;
using Spectra.Kitchen.Rules;
using SpectraEngine.Core;
using SpectraEngine.Core.Assets;
using SpectraEngine.Core.Assets.Images;
using SpectraEngine.Core.Graphics;
using SpectraEngine.Core.Graphics.OpenGL;
using System;
using System.Linq;
using Texture = SpectraEngine.Core.Graphics.Texture;

namespace SpectraEngine.Graphics.Tests;

/// <summary>
/// A PNG cooked to a <c>.simage</c>, read back, uploaded and sampled against a
/// real driver.
/// </summary>
// The fixture is cooked here, not checked in, so an encoder or container
// change can't leave a stale artifact passing.
[Collection(GlRendererCollection.Name)]
public sealed class CookedTextureGlTests
{
    private readonly GlRendererFixture _fixture;

    private const int TargetSize = 16;

    public CookedTextureGlTests(GlRendererFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public void A_cooked_image_samples_the_same_way_up_as_the_loose_file_it_came_from()
    {
        // Block-compressed rows can't be flipped at load, so the flip happens
        // at cook time through the same ImageDecoder the loose path uses.
        TextureOrientationProbe.Reading loose = Render(UploadLoose());
        TextureOrientationProbe.Reading cooked = Render(UploadCooked());

        cooked.MatchesAuthoredImage.ShouldBeTrue(
            $"the cooked image rendered as: {cooked}. Verdict: {cooked.Verdict}");

        // If the v = 0 convention ever moves, these two must move together.
        cooked.ShouldBe(loose);
    }

    [Fact]
    public void A_cooked_image_whose_rows_were_flipped_before_encoding_reads_as_flipped()
    {
        // Control for the test above: proves the probe can report a flip.
        DecodedImage image = DecodeFixture();
        var mirrored = new DecodedImage(
            MirrorRows(image), image.Width, image.Height, image.Channels, image.Format);

        TextureOrientationProbe.Reading reading = Render(Upload(Encode(mirrored)));

        reading.IsVerticallyFlipped.ShouldBeTrue($"a mirrored cook rendered as: {reading}");
    }

    [Fact]
    public void A_cooked_image_reaches_the_GPU_with_no_decode_and_a_supplied_mip_chain()
    {
        byte[] cooked = Cook();
        SimageInfo info = SimageReader.Read(cooked, "cooked.simage");

        DecodedImage loose = DecodeFixture();
        info.Width.ShouldBe(loose.Width);
        info.Height.ShouldBe(loose.Height);

        info.Format.ShouldBe(TextureFormat.Bc7);
        info.PayloadBytes.ShouldBeLessThan(loose.Width * loose.Height * loose.Channels);

        // The backend can't build mips for a compressed format, so the cook
        // has to supply the chain.
        info.MipCount.ShouldBe(4);

        Texture texture = Upload(cooked);
        try
        {
            texture.Width.ShouldBe(loose.Width);
            texture.Height.ShouldBe(loose.Height);
            texture.Format.ShouldBe(TextureFormat.Bc7);
        }
        finally
        {
            _fixture.Renderer.DestroyTexture(texture);
        }
    }

    [Fact]
    public void The_upload_takes_the_CALLERS_colour_space_rather_than_the_files()
    {
        // The cooker writes the UNORM vkFormat: one cooked image can be an
        // albedo in one material and a mask in another.
        byte[] cooked = Cook();
        SimageReader.Read(cooked, "cooked.simage").DeclaredColorSpace.ShouldBe(TextureColorSpace.Linear);

        Texture asColour = Upload(cooked, TextureColorSpace.Srgb);
        try
        {
            asColour.ColorSpace.ShouldBe(TextureColorSpace.Srgb);
        }
        finally
        {
            _fixture.Renderer.DestroyTexture(asColour);
        }
    }

    private static byte[] Cook()
    {
        var context = new RuleContext(
            ContentRoot.Path, TextureOrientationProbe.TexturePath, CookProfile.Ship);

        new ImageRule().Cook(context);

        context.Diagnostics.ShouldBeEmpty();
        return context.Emissions.Single().Payload;
    }

    // Same container the rule writes, over pixels the test chose.
    private static byte[] Encode(DecodedImage image)
    {
        byte[][] levels = ImageBlockEncoder.Encode(
            image, TextureFormat.Bc7, CompressionQuality.Balanced);

        return Ktx2Writer.Write(
            TextureFormat.Bc7,
            image.Width,
            image.Height,
            levels,
            SimageRowOrder.BottomUp,
            EngineInfo.TextureFormatVersion);
    }

    private Texture UploadCooked() => Upload(Cook());

    private Texture Upload(byte[] cooked, TextureColorSpace colorSpace = TextureColorSpace.Linear)
    {
        SimageInfo info = SimageReader.Read(cooked, "cooked.simage");

        // Nearest: no mip selection, so only the base level is measured.
        return _fixture.Renderer.CreateTexture(new TextureUploadDesc(
            info.Format, colorSpace, cooked, info.Mips, TextureFilter.Nearest, TextureWrap.Clamp));
    }

    private Texture UploadLoose()
    {
        DecodedImage image = DecodeFixture();

        // Linear on both sides: only the tone curve sits between the bytes.
        return _fixture.Renderer.CreateTexture(
            image.Pixels, image.Width, image.Height, image.Format, TextureColorSpace.Linear,
            TextureFilter.Nearest, TextureWrap.Clamp);
    }

    private TextureOrientationProbe.Reading Render(Texture source)
    {
        OpenGLRenderer renderer = _fixture.Renderer;
        RenderTarget output = renderer.CreateRenderTarget(
            new RenderTargetDesc(TargetSize, TargetSize, TextureFormat.Rgba8, TextureColorSpace.Linear));

        try
        {
            renderer.DrawOrientationQuad(source, output, OrientationQuad.Coverage.Full);

            return new TextureOrientationProbe.Reading(
                Read(renderer, output, 0, TargetSize - 1),
                Read(renderer, output, TargetSize - 1, TargetSize - 1),
                Read(renderer, output, 0, 0),
                Read(renderer, output, TargetSize - 1, 0));
        }
        finally
        {
            renderer.DestroyRenderTarget(output);
            renderer.DestroyTexture(source);
        }
    }

    private static TextureOrientationProbe.Quadrant Read(
        Renderer renderer, RenderTarget target, int x, int y)
    {
        (byte r, byte g, byte b, _) = renderer.ReadTargetPixel(target, x, y);
        return TextureOrientationProbe.Classify(r, g, b);
    }

    private static byte[] MirrorRows(DecodedImage image)
    {
        var mirrored = new byte[image.Height * image.Stride];
        for (int y = 0; y < image.Height; y++)
        {
            ReadOnlySpan<byte> source = image.Pixels.Slice((image.Height - 1 - y) * image.Stride, image.Stride);
            source.CopyTo(mirrored.AsSpan(y * image.Stride, image.Stride));
        }

        return mirrored;
    }

    private static DecodedImage DecodeFixture() => ImageDecoder.DecodeFile(
        System.IO.Path.Combine(ContentRoot.Path, "Textures", "orientation_probe.png"));
}
