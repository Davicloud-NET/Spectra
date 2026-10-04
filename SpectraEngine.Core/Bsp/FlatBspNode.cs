using System.Numerics;
using System.Runtime.InteropServices;

namespace SpectraEngine.Core.Bsp;

/// <summary>
/// One internal node of a solid-leaf BSP tree in flat, blittable form, as the
/// compiled map format stores it and <see cref="FlatBspTree"/> queries it.
/// </summary>
// The layout is a file format: raw bytes are cast into this struct (24 bytes,
// pinned by a test). Children at or above zero index the node array, negative
// values are leaf codes. A real Plane, so queries match BspTree bit for bit.
[StructLayout(LayoutKind.Sequential)]
public readonly struct FlatBspNode
{
    /// <summary>Child code: the convex region on that side is empty space.</summary>
    public const int EmptyLeaf = -1;

    /// <summary>Child code: the convex region on that side is solid.</summary>
    public const int SolidLeaf = -2;

    /// <summary>The splitting plane. Points at or in front of it take <see cref="Front"/>.</summary>
    public readonly Plane Plane;

    /// <summary>The child on the plane's normal side: a node index, or a leaf code.</summary>
    public readonly int Front;

    /// <summary>The child behind the plane: a node index, or a leaf code.</summary>
    public readonly int Back;

    public FlatBspNode(Plane plane, int front, int back)
    {
        Plane = plane;
        Front = front;
        Back = back;
    }

    /// <summary>True when a child value is a leaf code rather than a node index.</summary>
    public static bool IsLeaf(int child) => child < 0;

    /// <summary>True when a child value is the solid-leaf code.</summary>
    public static bool IsSolid(int child) => child == SolidLeaf;
}
