using SpectraEngine.Core.Graphics;
using System;

namespace SpectraEngine.Graphics.Tests;

/// <summary>
/// The refusals <see cref="RenderTargetDesc.Validate"/> makes once a target asks
/// to be shared.
/// </summary>
public sealed class SharedRenderTargetTests
{
    [Fact]
    public void An_ordinary_target_is_unshared_unless_it_asks()
    {
        var desc = new RenderTargetDesc(256, 256);

        desc.Sharing.ShouldBe(RenderTargetSharing.None);
        Should.NotThrow(desc.Validate);
    }

    [Fact]
    public void A_shared_eight_bit_colour_target_is_accepted()
    {
        var desc = new RenderTargetDesc(
            1280, 720, TextureFormat.Rgba8, TextureColorSpace.Srgb,
            Depth: true, TextureFilter.Linear, TextureWrap.Clamp, Color: true,
            RenderTargetSharing.KeyedMutex);

        Should.NotThrow(desc.Validate);
    }

    [Fact]
    public void A_shared_half_float_target_is_refused_naming_the_format()
    {
        // The compositor's external-image import has no half-float path.
        var desc = new RenderTargetDesc(
            1280, 720, TextureFormat.Rgba16Float, TextureColorSpace.Linear,
            Depth: true, TextureFilter.Linear, TextureWrap.Clamp, Color: true,
            RenderTargetSharing.KeyedMutex);

        Should.Throw<ArgumentOutOfRangeException>(desc.Validate)
            .Message.ShouldContain(nameof(TextureFormat.Rgba8));
    }

    [Fact]
    public void A_half_float_target_that_is_not_shared_is_still_fine()
    {
        // The HDR scene target is this description.
        var desc = new RenderTargetDesc(
            1280, 720, TextureFormat.Rgba16Float, TextureColorSpace.Linear);

        Should.NotThrow(desc.Validate);
    }

    [Fact]
    public void A_shared_depth_only_target_is_refused()
    {
        RenderTargetDesc desc = RenderTargetDesc.DepthOnly(2048) with
        {
            Sharing = RenderTargetSharing.KeyedMutex,
        };

        Should.Throw<ArgumentException>(desc.Validate);
    }

    [Fact]
    public void A_backend_that_cannot_share_says_so_rather_than_throwing()
    {
        // Never initialized: this is the base class's default answer.
        var renderer = new Core.Graphics.OpenGL.OpenGLRenderer(
            Microsoft.Extensions.Logging.Abstractions.NullLogger<Renderer>.Instance,
            new SpectraShade.Compiler.SpectraShadeCompiler());

        renderer.TryGetSharedHandle(out Renderer.SharedTargetHandle handle).ShouldBeFalse();
        handle.ShouldBe(default);
        renderer.BeginSharedWrite().ShouldBeFalse();
        Should.NotThrow(renderer.EndSharedWrite);
    }

    [Fact]
    public void A_shared_handle_carries_its_size_and_its_generation()
    {
        var first = new Renderer.SharedTargetHandle(0x1234, 1280, 720, 1);
        var resized = first with { NtHandle = 0x5678, Width = 1600, Height = 900, Generation = 2 };

        resized.Generation.ShouldNotBe(first.Generation);
        resized.ShouldNotBe(first);
    }
}
