using System.Runtime.InteropServices;

namespace SpectraEngine.Core.Assets.Models;

/// <summary>
/// One attribute of a cooked vertex layout, as its eight bytes sit in a
/// <c>VTXL</c> section.
/// </summary>
// Mapped bytes are cast straight to this struct. Field order and Pack = 1 are the file layout.
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct SmodelVertexAttribute
{
    /// <summary>What this attribute means.</summary>
    public readonly SmodelSemantic Semantic;

    /// <summary>The element type of each component.</summary>
    public readonly SmodelComponentType ComponentType;

    /// <summary>How many components: 3 for a position, 2 for a UV, 4 for a tangent.</summary>
    public readonly byte ComponentCount;

    /// <summary>Per-attribute flags. None defined in v1; written zero.</summary>
    public readonly byte Flags;

    /// <summary>Where this attribute starts within one vertex.</summary>
    public readonly ushort ByteOffset;

    /// <summary>Reserved, written zero. Pads the record to eight bytes.</summary>
    public readonly ushort Reserved;

    /// <summary>Builds one attribute record.</summary>
    public SmodelVertexAttribute(
        SmodelSemantic semantic,
        SmodelComponentType componentType,
        byte componentCount,
        ushort byteOffset,
        byte flags = 0,
        ushort reserved = 0)
    {
        Semantic = semantic;
        ComponentType = componentType;
        ComponentCount = componentCount;
        Flags = flags;
        ByteOffset = byteOffset;
        Reserved = reserved;
    }
}
