using System;
using SpectraEngine.Core.Graphics;
using SpectraEngine.Core.Graphics.D3D12;
using Silk.NET.Direct3D12;
using Silk.NET.DXGI;

namespace SpectraEngine.Graphics.Tests;

/// <summary>
/// Pipeline-state cache identity, tested without a device.
/// </summary>
// Everything D3D12 bakes into a pipeline object has to be in the key, or a
// cached pipeline is handed to a draw that wanted different state.
public sealed class D3D12PsoKeyTests
{
    [Fact]
    public void Two_keys_describing_the_same_draw_are_equal()
    {
        D3D12PsoKey a = Key();
        D3D12PsoKey b = Key();

        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void The_depth_mode_separates_two_keys()
    {
        Assert.NotEqual(Key(depth: DepthMode.TestWrite), Key(depth: DepthMode.None));
        Assert.NotEqual(Key(depth: DepthMode.TestWrite), Key(depth: DepthMode.TestNoWrite));
    }

    [Fact]
    public void The_blend_mode_separates_two_keys()
    {
        Assert.NotEqual(Key(blend: BlendMode.Opaque), Key(blend: BlendMode.AlphaBlend));
    }

    [Fact]
    public void The_depth_bias_separates_two_keys()
    {
        // The shadow pass differs from other depth passes only by its bias.
        Assert.NotEqual(Key(bias: DepthBias.None), Key(bias: new DepthBias(2000, 2.5f)));
        Assert.NotEqual(Key(bias: new DepthBias(2000, 2.5f)), Key(bias: new DepthBias(2000, 3f)));
        Assert.NotEqual(Key(bias: new DepthBias(2000, 2.5f)), Key(bias: new DepthBias(3000, 2.5f)));
    }

    [Theory]
    // The back buffer baseline is the _SRGB view, so plain _UNORM is a different colour format.
    [InlineData(Format.FormatR8G8B8A8Unorm, 1u, Format.FormatD24UnormS8Uint, 1u)]      // LDR offscreen
    [InlineData(Format.FormatR16G16B16A16Float, 1u, Format.FormatD24UnormS8Uint, 1u)]  // HDR offscreen
    [InlineData(Format.FormatR8G8B8A8Unorm, 0u, Format.FormatD32Float, 1u)]            // depth only
    [InlineData(Format.FormatR8G8B8A8Unorm, 1u, Format.FormatD24UnormS8Uint, 4u)]      // MSAA
    public void Every_part_of_the_target_configuration_separates_two_keys(
        Format color, uint count, Format depth, uint samples)
    {
        var target = new D3D12TargetState(color, count, depth, samples);
        Assert.NotEqual(Key(), Key(target: target));
    }

    [Fact]
    public void The_back_buffer_target_names_the_srgb_view_format()
    {
        // A PSO is validated against the view format, not the resource's.
        // The back buffer is an _SRGB view over a _UNORM resource.
        Assert.Equal(Format.FormatR8G8B8A8UnormSrgb, D3D12TargetState.BackBuffer.ColorFormat);
        Assert.NotEqual(Format.FormatR8G8B8A8Unorm, D3D12TargetState.BackBuffer.ColorFormat);

        Assert.NotEqual(
            Key(target: D3D12TargetState.BackBuffer),
            Key(target: D3D12TargetState.BackBuffer with { ColorFormat = Format.FormatR8G8B8A8Unorm }));
    }

    [Fact]
    public void The_fill_mode_and_topology_separate_two_keys()
    {
        Assert.NotEqual(Key(fill: FillMode.Solid), Key(fill: FillMode.Wireframe));
        Assert.NotEqual(
            Key(topology: PrimitiveTopologyType.Triangle),
            Key(topology: PrimitiveTopologyType.Line));
    }

    [Fact]
    public void The_vertex_layout_is_compared_element_by_element_not_by_its_hash()
    {
        D3D12VertexLayout standard = Layout(Format.FormatR32G32B32Float);
        D3D12VertexLayout altered = Layout(Format.FormatR32G32Float);

        Assert.NotEqual(Key(layout: standard), Key(layout: altered));

        // Equal layouts in different objects must share one entry, or every
        // mesh compiles its own pipeline.
        Assert.Equal(
            Key(layout: Layout(Format.FormatR32G32B32Float)),
            Key(layout: Layout(Format.FormatR32G32B32Float)));
    }

    [Fact]
    public void A_key_survives_a_round_trip_through_a_dictionary()
    {
        var cache = new System.Collections.Generic.Dictionary<D3D12PsoKey, int>
        {
            [Key()] = 1,
            [Key(depth: DepthMode.None)] = 2,
        };

        Assert.Equal(2, cache.Count);
        Assert.Equal(1, cache[Key()]);
        Assert.Equal(2, cache[Key(depth: DepthMode.None)]);
    }

    private static D3D12PsoKey Key(
        D3D12VertexLayout? layout = null,
        FillMode fill = FillMode.Solid,
        PrimitiveTopologyType topology = PrimitiveTopologyType.Triangle,
        DepthMode depth = DepthMode.TestWrite,
        BlendMode blend = BlendMode.Opaque,
        DepthBias bias = default,
        D3D12TargetState? target = null)
    {
        D3D12TargetState state = target ?? D3D12TargetState.BackBuffer;
        return new D3D12PsoKey(
            layout ?? Layout(Format.FormatR32G32B32Float), fill, topology, depth, blend, bias, in state);
    }

    private static D3D12VertexLayout Layout(Format firstElementFormat) => new(
    [
        new D3D12VertexLayout.Element(0, firstElementFormat, 0),
        new D3D12VertexLayout.Element(1, Format.FormatR32G32Float, 12),
    ], strideBytes: 32);
}
