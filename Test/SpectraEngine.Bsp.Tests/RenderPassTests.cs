using System;
using System.Numerics;
using Silk.NET.Maths;
using SpectraEngine.Core.Graphics;

namespace SpectraEngine.Bsp.Tests;

/// <summary>Render passes: which target a frame goes to, and at what size.</summary>
public sealed class RenderPassTests
{
    [Fact]
    public void A_pass_takes_its_size_from_the_target_when_it_opens()
    {
        var renderer = new FakeRenderer();
        renderer.SetFramebufferSize(new Vector2D<int>(1280, 720));

        renderer.BeginPass(PassClear.To(ClearColors.Sky));
        renderer.PassSize.ShouldBe(new Vector2D<int>(1280, 720));
        renderer.PassAspectRatio!.Value.ShouldBe(1280f / 720f, 1e-6f);
        renderer.EndPass();

        renderer.Passes.Count.ShouldBe(1);
        renderer.Passes[0].Size.ShouldBe(new Vector2D<int>(1280, 720));
    }

    [Fact]
    public void A_resize_between_frames_is_picked_up_by_the_next_pass()
    {
        var renderer = new FakeRenderer();

        renderer.SetFramebufferSize(new Vector2D<int>(800, 600));
        renderer.BeginPass(PassClear.Keep);
        renderer.EndPass();

        renderer.SetFramebufferSize(new Vector2D<int>(1920, 1080));
        renderer.BeginPass(PassClear.Keep);
        renderer.EndPass();

        renderer.Passes[0].Size.ShouldBe(new Vector2D<int>(800, 600));
        renderer.Passes[1].Size.ShouldBe(new Vector2D<int>(1920, 1080));
    }

    [Fact]
    public void A_zero_height_target_has_no_aspect_ratio_rather_than_infinity()
    {
        // Happens when minimised and mid-resize. Dividing would put NaN or
        // infinity into the projection.
        var renderer = new FakeRenderer();
        renderer.SetFramebufferSize(new Vector2D<int>(1280, 0));

        renderer.BeginPass(PassClear.Keep);
        renderer.PassAspectRatio.ShouldBeNull();
        renderer.EndPass();
    }

    [Fact]
    public void Passes_do_not_nest()
    {
        var renderer = new FakeRenderer();
        renderer.SetFramebufferSize(new Vector2D<int>(64, 64));

        renderer.BeginPass(PassClear.Keep);

        Should.Throw<InvalidOperationException>(() => renderer.BeginPass(PassClear.Keep));

        renderer.EndPass();
    }

    [Fact]
    public void Ending_a_pass_that_was_never_begun_throws()
    {
        var renderer = new FakeRenderer();
        Should.Throw<InvalidOperationException>(renderer.EndPass);
    }

    [Fact]
    public void A_clear_can_name_colour_depth_both_or_neither()
    {
        // Null means leave the attachment alone, which is not the same as
        // clearing it to black.
        PassClear.To(new Vector4(1f, 0f, 0f, 1f)).Color.ShouldBe(new Vector4(1f, 0f, 0f, 1f));
        PassClear.To(Vector4.One).Depth.ShouldBe(1f);

        PassClear.DepthOnly.Color.ShouldBeNull();
        PassClear.DepthOnly.Depth.ShouldBe(1f);

        PassClear.Keep.Color.ShouldBeNull();
        PassClear.Keep.Depth.ShouldBeNull();
    }

    [Fact]
    public void The_sky_a_pass_clears_to_is_the_one_shared_linear_constant()
    {
        PassClear sky = PassClear.To(ClearColors.Sky);

        sky.Color!.Value.ShouldBe(ClearColors.Sky);
        ColorSpace.LinearToSrgb(sky.Color.Value.X).ShouldBe(0.392f, 1e-4f);
    }
}
