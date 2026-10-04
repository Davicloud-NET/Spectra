using System;
using System.Numerics;

namespace SpectraEngine.Core.Bsp;

/// <summary>
/// Integer coordinates of one cell in the sparse static-world chunk grid. A
/// cell is the cube <c>[coord * CellSize, (coord + 1) * CellSize)</c> per axis,
/// so a position on a boundary belongs to the positive side. Coordinates are
/// unbounded and may be negative.
/// </summary>
public readonly record struct ChunkCoord(int X, int Y, int Z) : IComparable<ChunkCoord>
{
    /// <summary>
    /// Edge length of a chunk cell in world units. A power of two, so the
    /// division is exact. The compile stages and their tests are calibrated to 32.
    /// </summary>
    public const float CellSize = 32.0f;

    /// <summary>The cell containing <paramref name="worldPosition"/> (floor per axis).</summary>
    public static ChunkCoord FromPosition(Vector3 worldPosition) => new(
        FloorToCell(worldPosition.X), FloorToCell(worldPosition.Y), FloorToCell(worldPosition.Z));

    /// <summary>Minimum (inclusive) world-space corner of the cell.</summary>
    public Vector3 MinCorner => new(X * CellSize, Y * CellSize, Z * CellSize);

    /// <summary>Maximum (exclusive) world-space corner of the cell.</summary>
    // (coord + 1) * CellSize, not MinCorner + CellSize: bit-identical to the
    // neighbour's MinCorner.
    public Vector3 MaxCorner => new((X + 1) * CellSize, (Y + 1) * CellSize, (Z + 1) * CellSize);

    /// <summary>The cell's world-space box, <see cref="MinCorner"/>..<see cref="MaxCorner"/>.</summary>
    public Aabb Bounds => new(MinCorner, MaxCorner);

    /// <summary>
    /// Lexicographic X, Y, Z order. The order per-cell work is combined in.
    /// </summary>
    public int CompareTo(ChunkCoord other)
    {
        int c = X.CompareTo(other.X);
        if (c != 0) return c;
        c = Y.CompareTo(other.Y);
        return c != 0 ? c : Z.CompareTo(other.Z);
    }

    /// <summary>
    /// A Z-order (Morton) key: the bits of X, Y and Z interleaved, so a run
    /// of consecutive keys is a compact block of cells. 21 bits per axis.
    /// </summary>
    // Don't fold this into CompareTo. Dirty-cell sets rely on lexicographic order.
    // Biased to unsigned first so negative cells sort below positive ones.
    public ulong MortonKey => Interleave(Bias(X)) | (Interleave(Bias(Y)) << 1) | (Interleave(Bias(Z)) << 2);

    private const int MortonBits = 21;
    private const int MortonBias = 1 << (MortonBits - 1);

    private static uint Bias(int value) =>
        (uint)Math.Clamp(value + MortonBias, 0, (1 << MortonBits) - 1);

    // Spreads the low 21 bits to every third position.
    private static ulong Interleave(uint value)
    {
        ulong x = value & 0x1FFFFFUL;
        x = (x | (x << 32)) & 0x1F00000000FFFFUL;
        x = (x | (x << 16)) & 0x1F0000FF0000FFUL;
        x = (x | (x << 8)) & 0x100F00F00F00F00FUL;
        x = (x | (x << 4)) & 0x10C30C30C30C30C3UL;
        x = (x | (x << 2)) & 0x1249249249249249UL;
        return x;
    }

    private static int FloorToCell(float value) => (int)MathF.Floor(value / CellSize);
}
