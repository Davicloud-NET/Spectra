using System;

namespace SpectraEngine.Core.Assets.Models;

/// <summary>
/// The vertex layout the cook writes and the loader accepts: position, normal,
/// UV0, eight interleaved floats. A file with any other layout is refused.
/// </summary>
// Offsets come from ModelVertexLayout so the cooked and loose paths share one shape.
public static class SmodelStandardLayout
{
    private static readonly SmodelVertexAttribute[] _attributes =
    [
        new(SmodelSemantic.Position, SmodelComponentType.Float32, 3,
            (ushort)(ModelVertexLayout.PositionOffset * sizeof(float))),
        new(SmodelSemantic.Normal, SmodelComponentType.Float32, 3,
            (ushort)(ModelVertexLayout.NormalOffset * sizeof(float))),
        new(SmodelSemantic.Uv0, SmodelComponentType.Float32, 2,
            (ushort)(ModelVertexLayout.TexCoordOffset * sizeof(float))),
    ];

    /// <summary>The three attributes, in declaration order.</summary>
    public static ReadOnlySpan<SmodelVertexAttribute> Attributes => _attributes;

    /// <summary>Floats per vertex, which is what <c>VTXL</c> stores as its stride.</summary>
    public const uint StrideFloats = (uint)ModelVertexLayout.FloatsPerVertex;

    /// <summary>The id a header stamps for this layout.</summary>
    public static uint LayoutId { get; } = SmodelFormat.ComputeVertexLayoutId(_attributes);
}
