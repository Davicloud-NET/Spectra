using BCnEncoder.Encoder;
using Microsoft.Extensions.Logging.Abstractions;
using Spectra.Kitchen.Cooking;
using Spectra.Kitchen.Images;
using Spectra.Kitchen.Rules;
using SpectraEngine.Core;
using SpectraEngine.Core.Assets;
using SpectraEngine.Core.Assets.Images;
using SpectraEngine.Core.Assets.Packs;
using SpectraEngine.Core.Assets.Sources;
using SpectraEngine.Core.Graphics;
using System.IO;
using System.Linq;

namespace Spectra.Kitchen.Tests;

/// <summary>
/// The image cook: a PNG in, a <c>.simage</c> of BC blocks out. Checks the pack
/// entry and its bytes, not the rendered picture.
/// </summary>
public class ImageRuleTests
{
    private const string SourcePath = "Textures/wall_brick.png";
    private const string CookedPath = "Textures/wall_brick.simage";

    [Fact]
    public void A_png_is_cooked_to_a_simage_rather_than_copied()
    {
        using var project = new TempProject();
        project.WriteAsset(SourcePath, TempProject.Png(16, 16, seed: 3));

        CookResult result = new CookSession(project.Layout, new CookSettings { UseCache = false }).Run();

        result.Succeeded.ShouldBeTrue();

        CookedAsset asset = result.Assets.Single();
        asset.Rule.ShouldBe(RuleKind.Image);

        CookedOutput output = asset.Outputs.Single();
        output.Path.ShouldBe(CookedPath);

        var pack = project.Track(new PackSource(NullLogger.Instance, result.OutputPath!));
        pack.Exists(CookedPath).ShouldBeTrue();
        pack.Exists(SourcePath).ShouldBeFalse();

        pack.TryOpen(CookedPath, out ContentBlob? blob).ShouldBeTrue();
        using (blob)
        {
            SimageInfo info = SimageReader.Read(blob.Span, CookedPath);
            info.Format.ShouldBe(TextureFormat.Bc7);
            info.Width.ShouldBe(16);
            info.Height.ShouldBe(16);

            // Full mip chain: the GPU cannot generate mips for BC formats.
            info.MipCount.ShouldBe(5);

            info.PayloadBytes.ShouldBeLessThan(16 * 16 * 4);
        }
    }

    [Fact]
    public void A_single_channel_image_cooks_to_BC4_rather_than_to_a_colour_format()
    {
        using var project = new TempProject();
        project.WriteAsset("Textures/mask.png", TempProject.Png(8, 8, channels: 1));

        CookResult result = new CookSession(project.Layout, new CookSettings { UseCache = false }).Run();
        result.Succeeded.ShouldBeTrue(string.Join('\n', result.Diagnostics));

        var pack = project.Track(new PackSource(NullLogger.Instance, result.OutputPath!));
        pack.TryOpen("Textures/mask.simage", out ContentBlob? blob).ShouldBeTrue();
        using (blob)
            SimageReader.Read(blob.Span, "mask").Format.ShouldBe(TextureFormat.Bc4);
    }

    [Fact]
    public void A_file_named_png_that_is_not_one_is_an_error_rather_than_a_raw_copy()
    {
        // A raw copy would ship a broken PNG that renders as the placeholder.
        using var project = new TempProject();
        project.WriteAsset(SourcePath, TempProject.Bytes(64));

        CookResult result = new CookSession(project.Layout, new CookSettings { UseCache = false }).Run();

        result.Succeeded.ShouldBeFalse();
        result.Diagnostics.ShouldContain(d => d.IsError && d.Id.ToString() == "SC2001");
        result.Assets.Single(a => a.SourcePath == SourcePath).Outputs.ShouldBeEmpty();
    }

    [Fact]
    public void Two_cooks_of_one_image_produce_the_same_bytes()
    {
        using var project = new TempProject();
        project.WriteAsset(SourcePath, TempProject.Png(32, 32, seed: 9));

        // Byte identity: the cook cache is content-addressed.
        byte[] first = CookOne(project, "a");
        byte[] second = CookOne(project, "b");

        second.ShouldBe(first);
    }

    [Fact]
    public void A_parallel_encode_and_a_serial_one_agree()
    {
        // The cook encodes single-threaded only for scheduling. This guards a
        // BCnEncoder bump that makes the two differ.
        DecodedImage image = ImageDecoder.Decode(TempProject.Png(32, 32, seed: 11), "parallel.png");

        byte[][] serial = ImageBlockEncoder.Encode(
            image, TextureFormat.Bc7, CompressionQuality.Balanced, parallel: false);
        byte[][] parallel = ImageBlockEncoder.Encode(
            image, TextureFormat.Bc7, CompressionQuality.Balanced, parallel: true);

        parallel.Length.ShouldBe(serial.Length);
        for (int level = 0; level < serial.Length; level++)
            parallel[level].ShouldBe(serial[level], $"level {level}");
    }

    [Fact]
    public void The_profile_changes_the_bytes_which_is_why_the_rule_declares_it()
    {
        using var project = new TempProject();
        project.WriteAsset(SourcePath, TempProject.Png(32, 32, seed: 13));

        byte[] ship = CookOne(project, "ship");
        byte[] fast = CookOne(project, "fast", CookProfile.Fast);

        fast.ShouldNotBe(ship);
    }

    [Fact]
    public void A_cooked_texture_still_resolves_by_the_path_its_material_names()
    {
        // A material keeps naming the .png. ImageContentPath redirects.
        using var project = new TempProject();
        project.WriteAsset(SourcePath, TempProject.Png(8, 8, seed: 17));

        CookResult result = new CookSession(project.Layout, new CookSettings { UseCache = false }).Run();
        var pack = project.Track(new PackSource(NullLogger.Instance, result.OutputPath!));

        var stack = new ContentSourceStack();
        stack.Mount(pack);

        ImageContentPath.Resolve(stack, SourcePath).ShouldBe(CookedPath);

        ImageContentPath.Resolve(stack, "Textures/absent.png").ShouldBe("Textures/absent.png");
    }

    [Fact]
    public void Handles_names_the_formats_the_decoder_can_actually_open()
    {
        // Must match what ImageDecoder can open, or the file becomes an SC2001.
        ImageRule.Handles("Textures/a.png").ShouldBeTrue();
        ImageRule.Handles("Textures/a.JPG").ShouldBeTrue();
        ImageRule.Handles("Textures/a.tga").ShouldBeTrue();
        ImageRule.Handles("Textures/a.bmp").ShouldBeTrue();

        ImageRule.Handles("Textures/a.simage").ShouldBeFalse("a cooked image is not an input");
        ImageRule.Handles("Logo/LogoSpectra.ico").ShouldBeFalse();
        ImageRule.Handles("Materials/wall.spectramat").ShouldBeFalse();
    }

    private static byte[] CookOne(TempProject project, string label, CookProfile profile = CookProfile.Ship)
    {
        string output = Path.Combine(project.Root, label);
        CookResult result = new CookSession(
            project.Layout,
            new CookSettings { UseCache = false, Profile = profile, OutputPath = output }).Run();

        result.Succeeded.ShouldBeTrue(string.Join('\n', result.Diagnostics));
        return File.ReadAllBytes(result.OutputPath!);
    }
}
