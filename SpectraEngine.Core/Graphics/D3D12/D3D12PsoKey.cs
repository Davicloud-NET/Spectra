using System;
using Silk.NET.Direct3D12;
using Silk.NET.DXGI;

namespace SpectraEngine.Core.Graphics.D3D12;

// The target configuration a PSO is compiled against. D3D12 bakes it into the
// PSO, so one built for another configuration is invalid.
// Zero colour targets is legal: a depth-only shadow pass.
internal readonly record struct D3D12TargetState(
    Format ColorFormat,
    uint RenderTargetCount,
    Format DepthFormat,
    uint SampleCount,
    Format ColorFormat1 = Format.FormatUnknown,
    Format ColorFormat2 = Format.FormatUnknown,
    Format ColorFormat3 = Format.FormatUnknown,
    Format ColorFormat4 = Format.FormatUnknown,
    Format ColorFormat5 = Format.FormatUnknown,
    Format ColorFormat6 = Format.FormatUnknown,
    Format ColorFormat7 = Format.FormatUnknown)
{
    // Explicit fields, not an array: this is a dictionary key, and an array
    // compares by reference, so every draw would miss the PSO cache.
    public static D3D12TargetState ForTargets(ReadOnlySpan<Format> colors, Format depth)
    {
        static Format At(ReadOnlySpan<Format> c, int i) => i < c.Length ? c[i] : Format.FormatUnknown;

        return new D3D12TargetState(
            At(colors, 0), (uint)colors.Length, depth, 1,
            At(colors, 1), At(colors, 2), At(colors, 3),
            At(colors, 4), At(colors, 5), At(colors, 6), At(colors, 7));
    }

    public Format ColorAt(int index) => index switch
    {
        0 => ColorFormat,
        1 => ColorFormat1,
        2 => ColorFormat2,
        3 => ColorFormat3,
        4 => ColorFormat4,
        5 => ColorFormat5,
        6 => ColorFormat6,
        7 => ColorFormat7,
        _ => Format.FormatUnknown,
    };

    // RTV format, not the resource's: a PSO is validated against the bound
    // view, and the back buffer is a _UNORM resource behind an _SRGB view.
    public static D3D12TargetState BackBuffer => new(
        D3D12Renderer.BackBufferRtvFormat, 1, D3D12Renderer.DepthFormat, 1);
}

// Everything D3D12 bakes into a PSO. Equality is structural, layout included:
// a hash collision must not return a PSO built for another layout or target.
internal readonly struct D3D12PsoKey : IEquatable<D3D12PsoKey>
{
    public readonly D3D12VertexLayout Layout;
    public readonly FillMode Fill;
    public readonly PrimitiveTopologyType Topology;
    public readonly DepthMode Depth;
    public readonly BlendMode Blend;

    // Baked into the PSO. Without it the shadow pass could get an unbiased one.
    public readonly DepthBias Bias;

    public readonly D3D12TargetState Target;

    public D3D12PsoKey(
        D3D12VertexLayout layout,
        FillMode fill,
        PrimitiveTopologyType topology,
        DepthMode depth,
        BlendMode blend,
        DepthBias bias,
        in D3D12TargetState target)
    {
        Layout = layout;
        Fill = fill;
        Topology = topology;
        Depth = depth;
        Blend = blend;
        Bias = bias;
        Target = target;
    }

    public bool Equals(D3D12PsoKey other) =>
        Fill == other.Fill
        && Topology == other.Topology
        && Depth == other.Depth
        && Blend == other.Blend
        && Bias == other.Bias
        && Target == other.Target
        && Layout.StrideBytes == other.Layout.StrideBytes
        && Layout.Elements.AsSpan().SequenceEqual(other.Layout.Elements);

    public override bool Equals(object? obj) => obj is D3D12PsoKey other && Equals(other);

    // Bucketing only. Equals is the identity.
    public override int GetHashCode() =>
        HashCode.Combine(Layout.Key, Fill, Topology, Depth, Blend, Target.GetHashCode());
}
