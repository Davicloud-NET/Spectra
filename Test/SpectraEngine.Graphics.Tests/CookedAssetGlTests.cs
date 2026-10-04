using Microsoft.Extensions.Logging.Abstractions;
using Spectra.Kitchen.Cooking;
using Spectra.Kitchen.Images;
using Spectra.Kitchen.Packs;
using Spectra.Kitchen.Rules;
using SpectraEngine.Core;
using SpectraEngine.Core.Assets;
using SpectraEngine.Core.Assets.Images;
using SpectraEngine.Core.Assets.Packs;
using SpectraEngine.Core.Assets.Sources;
using SpectraEngine.Core.Graphics;
using System;
using System.IO;
using System.Linq;
using Texture = SpectraEngine.Core.Graphics.Texture;

namespace SpectraEngine.Graphics.Tests;

/// <summary>
/// <see cref="AssetManager"/> over cooked content: a material naming a
/// <c>.png</c> that resolves to the <c>.simage</c> beside it.
/// </summary>
[Collection(GlRendererCollection.Name)]
public sealed class CookedAssetGlTests : IDisposable
{
    private readonly GlRendererFixture _fixture;
    private readonly string _root;

    private const string SourcePath = "Textures/orientation_probe.png";
    private const string CookedPath = "Textures/orientation_probe.simage";
    private const string MaterialPath = "Materials/probe.spectramat";

    public CookedAssetGlTests(GlRendererFixture fixture)
    {
        _fixture = fixture;
        _root = Path.Combine(Path.GetTempPath(), $"spectra_cooked_{Guid.NewGuid():N}");
    }

    [Fact]
    public void A_texture_named_as_a_png_loads_from_the_simage_beside_it()
    {
        // No .png in the tree, as in a shipped build.
        WriteCooked(Cook());
        using AssetManager assets = Open();

        TextureAsset texture = assets.LoadTexture(SourcePath, TextureFilter.Nearest, TextureWrap.Clamp);

        texture.IsPlaceholder.ShouldBeFalse();
        texture.Texture.Format.ShouldBe(TextureFormat.Bc7);

        texture.RelativePath.ShouldBe(SourcePath);
    }

    [Fact]
    public void A_materials_texture_slot_probes_the_same_path_the_open_takes()
    {
        WriteCooked(Cook());
        File.WriteAllText(
            Path.Combine(_root, "Materials", "probe.spectramat"),
            $"shader = lit\ntexture uDiffuse = {SourcePath}, nearest, clamp\n");

        using AssetManager assets = Open();
        Material material = assets.LoadMaterial(MaterialPath);

        material.ShouldNotBe(assets.DefaultMaterial);
        material.TextureCount.ShouldBe(1);

        material.TryGetTexture("uDiffuse", out _, out Texture? bound).ShouldBeTrue();

        // The placeholder is Rgb8, so a slot that fell back shows here.
        bound.Format.ShouldBe(TextureFormat.Bc7);
    }

    [Fact]
    public void An_async_request_lands_the_cooked_texture_through_the_pump()
    {
        WriteCooked(Cook());
        using AssetManager assets = Open();

        TextureAsset handle = assets.RequestTexture(SourcePath, TextureFilter.Nearest, TextureWrap.Clamp);
        handle.IsPlaceholder.ShouldBeTrue("a request returns immediately on the placeholder");

        Pump(assets, handle);

        handle.IsPlaceholder.ShouldBeFalse();
        handle.Texture.Format.ShouldBe(TextureFormat.Bc7);
        handle.LoadFailed.ShouldBeFalse();
    }

    [Fact]
    public void A_pack_mounted_alone_serves_its_cooked_textures_with_no_loose_file_anywhere()
    {
        // A mounted pack hands out spans into its mapped view, so this takes
        // the no-copy upload branch.
        Directory.CreateDirectory(_root);
        string packPath = Path.Combine(_root, "content.spack");

        var writer = new PackWriter();
        writer.Add(CookedPath, PackEntryKind.Image, Cook());
        writer.WriteToFile(packPath);

        using var pack = new PackSource(NullLogger.Instance, packPath);
        var stack = new ContentSourceStack();
        stack.Mount(pack);

        using var assets = new AssetManager(NullLogger<AssetManager>.Instance, _root, stack, hotReloadEnabled: false);
        assets.AttachRenderer(_fixture.Renderer);

        TextureAsset texture = assets.LoadTexture(SourcePath, TextureFilter.Nearest, TextureWrap.Clamp);

        texture.IsPlaceholder.ShouldBeFalse();
        texture.Texture.Format.ShouldBe(TextureFormat.Bc7);

        // Release before the pack unmounts.
        assets.ReleaseGraphicsResources();
    }

    [Fact]
    public void A_simage_from_another_profile_version_degrades_rather_than_taking_the_frame_down()
    {
        WriteCooked(CookForVersion(EngineInfo.TextureFormatVersion + 1));
        using AssetManager assets = Open();

        TextureAsset handle = assets.RequestTexture(SourcePath, TextureFilter.Nearest, TextureWrap.Clamp);
        Pump(assets, handle);

        handle.IsPlaceholder.ShouldBeTrue();
        handle.LoadFailed.ShouldBeTrue("a refused .simage must stay retryable, exactly like a failed decode");
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // Don't fail a test on its own cleanup.
        }
    }

    private static byte[] Cook()
    {
        var context = new RuleContext(ContentRoot.Path, SourcePath, CookProfile.Ship);
        new ImageRule().Cook(context);
        return context.Emissions.Single().Payload;
    }

    private static byte[] CookForVersion(int profileVersion)
    {
        DecodedImage image = ImageDecoder.DecodeFile(
            Path.Combine(ContentRoot.Path, "Textures", "orientation_probe.png"));

        return Ktx2Writer.Write(
            TextureFormat.Bc7,
            image.Width,
            image.Height,
            ImageBlockEncoder.Encode(image, TextureFormat.Bc7, BCnEncoder.Encoder.CompressionQuality.Fast),
            SimageRowOrder.BottomUp,
            profileVersion);
    }

    private void WriteCooked(byte[] cooked)
    {
        Directory.CreateDirectory(Path.Combine(_root, "Textures"));
        Directory.CreateDirectory(Path.Combine(_root, "Materials"));
        File.WriteAllBytes(Path.Combine(_root, "Textures", "orientation_probe.simage"), cooked);
    }

    private AssetManager Open()
    {
        var assets = new AssetManager(NullLogger<AssetManager>.Instance, _root, hotReloadEnabled: false);
        assets.AttachRenderer(_fixture.Renderer);
        return assets;
    }

    // The decode runs on the thread pool, so pump until it lands. Bounded.
    private static void Pump(AssetManager assets, TextureAsset handle)
    {
        for (int i = 0; i < 2000 && handle.IsPlaceholder && !handle.LoadFailed; i++)
        {
            assets.PumpPendingUploads();
            System.Threading.Thread.Sleep(1);
        }
    }
}
