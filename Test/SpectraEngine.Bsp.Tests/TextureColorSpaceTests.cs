using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SpectraEngine.Core.Assets;
using SpectraEngine.Core.Graphics;

namespace SpectraEngine.Bsp.Tests;

/// <summary>
/// Colour space as it travels from a <c>.spectramat</c> line to a GPU texture.
/// </summary>
public sealed class TextureColorSpaceTests
{
    private const string Grid = "Textures/dev_grid.png";
    private const string Mask = "Textures/gradient_mask.png";

    [Fact]
    public void A_texture_is_srgb_unless_the_line_says_data()
    {
        MaterialDefinition definition = MaterialParser.Parse("""
            texture uDiffuse  = Textures/wall_brick.png
            texture uNormal   = Textures/wall_normal.png, data
            texture uEmissive = Textures/glow.png, srgb
            """, "spaces.spectramat");

        definition.Warnings.ShouldBeEmpty();

        definition.TryGetTextureSlot("uDiffuse", out MaterialTextureSlot diffuse).ShouldBeTrue();
        diffuse.ColorSpace.ShouldBe(TextureColorSpace.Srgb);

        definition.TryGetTextureSlot("uNormal", out MaterialTextureSlot normal).ShouldBeTrue();
        normal.ColorSpace.ShouldBe(TextureColorSpace.Linear);

        definition.TryGetTextureSlot("uEmissive", out MaterialTextureSlot emissive).ShouldBeTrue();
        emissive.ColorSpace.ShouldBe(TextureColorSpace.Srgb);
    }

    [Fact]
    public void The_keyword_is_data_because_linear_already_means_a_filter()
    {
        MaterialDefinition definition = MaterialParser.Parse("""
            texture uNormal = Textures/n.png, linear, clamp, data
            """, "order.spectramat");

        definition.Warnings.ShouldBeEmpty();
        definition.TryGetTextureSlot("uNormal", out MaterialTextureSlot slot).ShouldBeTrue();

        slot.Filter.ShouldBe(TextureFilter.Linear);
        slot.Wrap.ShouldBe(TextureWrap.Clamp);
        slot.ColorSpace.ShouldBe(TextureColorSpace.Linear);
    }

    [Fact]
    public void Options_are_order_independent()
    {
        MaterialDefinition definition = MaterialParser.Parse("""
            texture uA = Textures/a.png, data, nearest, clamp
            texture uB = Textures/b.png, clamp, data, nearest
            """, "shuffled.spectramat");

        definition.Warnings.ShouldBeEmpty();
        definition.TryGetTextureSlot("uA", out MaterialTextureSlot a).ShouldBeTrue();
        definition.TryGetTextureSlot("uB", out MaterialTextureSlot b).ShouldBeTrue();

        a.Filter.ShouldBe(b.Filter);
        a.Wrap.ShouldBe(b.Wrap);
        a.ColorSpace.ShouldBe(b.ColorSpace);
        a.ColorSpace.ShouldBe(TextureColorSpace.Linear);
    }

    [Fact]
    public void An_unknown_option_still_warns_and_names_the_new_choices()
    {
        MaterialDefinition definition = MaterialParser.Parse("""
            texture uDiffuse = Textures/a.png, gamma
            """, "typo.spectramat");

        definition.Warnings.ShouldContain(w => w.Contains("unknown option 'gamma'"));
        definition.Warnings.ShouldContain(w => w.Contains("srgb/data"));

        // The slot itself survives the bad option.
        definition.TryGetTextureSlot("uDiffuse", out MaterialTextureSlot slot).ShouldBeTrue();
        slot.ColorSpace.ShouldBe(TextureColorSpace.Srgb);
    }

    [Fact]
    public void One_image_asked_for_both_ways_loads_two_textures()
    {
        var (assets, renderer) = Attach(NullLogger<AssetManager>.Instance);

        TextureAsset albedo = assets.LoadTexture(
            Grid, TextureFilter.Nearest, TextureWrap.Repeat, TextureColorSpace.Srgb);
        TextureAsset data = assets.LoadTexture(
            Grid, TextureFilter.Nearest, TextureWrap.Repeat, TextureColorSpace.Linear);

        // Colour space is part of the GPU format, so the two can't share a texture.
        data.ShouldNotBeSameAs(albedo);
        assets.TextureCount.ShouldBe(2);
        ((FakeTexture)albedo.Texture).ColorSpace.ShouldBe(TextureColorSpace.Srgb);
        ((FakeTexture)data.Texture).ColorSpace.ShouldBe(TextureColorSpace.Linear);

        assets.LoadTexture(Grid, TextureFilter.Nearest, TextureWrap.Repeat, TextureColorSpace.Srgb)
            .ShouldBeSameAs(albedo);
        assets.LoadTexture(Grid, TextureFilter.Nearest, TextureWrap.Repeat, TextureColorSpace.Linear)
            .ShouldBeSameAs(data);
        renderer.CreatedTextures.Count.ShouldBe(AssetTestFacts.BuiltInTextures + 2); // + the two variants

        assets.ReleaseGraphicsResources();
    }

    [Fact]
    public void A_material_carries_its_slots_colour_space_into_the_cache()
    {
        var (assets, _) = Attach(NullLogger<AssetManager>.Instance);

        // dev_grid names no colour space, so it takes the sRGB default.
        assets.LoadMaterial("Materials/dev_grid.spectramat");

        assets.TryGetTexture(Grid, TextureFilter.LinearMipmap, TextureWrap.Repeat,
            out TextureAsset? srgbVariant, TextureColorSpace.Srgb).ShouldBeTrue();
        srgbVariant.ColorSpace.ShouldBe(TextureColorSpace.Srgb);

        assets.TryGetTexture(Grid, TextureFilter.LinearMipmap, TextureWrap.Repeat,
            out _, TextureColorSpace.Linear).ShouldBeFalse();

        assets.ReleaseGraphicsResources();
    }

    [Fact]
    public void A_single_channel_image_asked_for_srgb_falls_back_and_says_so()
    {
        var logger = new CapturingLogger();
        var (assets, _) = Attach(logger);

        // gradient_mask.png is one channel. No backend has a one-channel sRGB format.
        TextureAsset asset = assets.LoadTexture(
            Mask, TextureFilter.Linear, TextureWrap.Repeat, TextureColorSpace.Srgb);

        asset.Texture.Format.ShouldBe(TextureFormat.R8);
        asset.Texture.ColorSpace.ShouldBe(TextureColorSpace.Linear);

        logger.MessagesAt(LogLevel.Warning).ShouldContain(
            m => m.Contains("gradient_mask") && m.Contains("no sRGB form"),
            customMessage: logger.Describe());
        logger.MessagesAt(LogLevel.Error).ShouldBeEmpty(logger.Describe());

        assets.ReleaseGraphicsResources();
    }

    private static (AssetManager Assets, FakeRenderer Renderer) Attach(ILogger logger)
    {
        var assets = new AssetManager(logger, ContentRoot.Path, hotReloadEnabled: false);
        var renderer = new FakeRenderer();
        assets.AttachRenderer(renderer);
        return (assets, renderer);
    }

    [Fact]
    public void The_requested_space_stays_the_cache_key_even_when_it_cannot_be_honoured()
    {
        var (assets, _) = Attach(NullLogger<AssetManager>.Instance);

        // The handle keeps what was asked for, the texture what it got.
        TextureAsset asked = assets.LoadTexture(
            Mask, TextureFilter.Linear, TextureWrap.Repeat, TextureColorSpace.Srgb);
        TextureAsset plain = assets.LoadTexture(
            Mask, TextureFilter.Linear, TextureWrap.Repeat, TextureColorSpace.Linear);

        asked.ColorSpace.ShouldBe(TextureColorSpace.Srgb);
        plain.ColorSpace.ShouldBe(TextureColorSpace.Linear);
        asked.ShouldNotBeSameAs(plain);

        asked.Texture.ColorSpace.ShouldBe(TextureColorSpace.Linear);
        plain.Texture.ColorSpace.ShouldBe(TextureColorSpace.Linear);

        assets.ReleaseGraphicsResources();
    }
}
