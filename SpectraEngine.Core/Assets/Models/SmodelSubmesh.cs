using System.Numerics;
using System.Runtime.InteropServices;

namespace SpectraEngine.Core.Assets.Models;

/// <summary>
/// One drawable index range of a cooked model, as its forty bytes sit in a
/// <c>SUBM</c> section.
/// </summary>
// A range into the shared buffers, so an LOD switch is a draw-range change.
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct SmodelSubmesh
{
    /// <summary>First index of this range within <c>IBUF</c>.</summary>
    public readonly uint IndexStart;

    /// <summary>How many indices this range covers.</summary>
    public readonly uint IndexCount;

    /// <summary>
    /// Offset into the <c>NAME</c> blob of this submesh's material asset path, or
    /// <see cref="SmodelFormat.NameOffsetAbsent"/> when it names none.
    /// </summary>
    public readonly uint MaterialNameOffset;

    /// <summary>Per-submesh flags. None defined in v1; written zero.</summary>
    public readonly uint Flags;

    /// <summary>Model-local minimum corner of this submesh's bounds.</summary>
    public readonly Vector3 BoundsMin;

    /// <summary>Model-local maximum corner of this submesh's bounds.</summary>
    public readonly Vector3 BoundsMax;

    /// <summary>Builds one submesh record.</summary>
    public SmodelSubmesh(
        uint indexStart,
        uint indexCount,
        uint materialNameOffset,
        Vector3 boundsMin,
        Vector3 boundsMax,
        uint flags = 0)
    {
        IndexStart = indexStart;
        IndexCount = indexCount;
        MaterialNameOffset = materialNameOffset;
        Flags = flags;
        BoundsMin = boundsMin;
        BoundsMax = boundsMax;
    }

    /// <summary>Whether this submesh names a material.</summary>
    public bool HasMaterial => MaterialNameOffset != SmodelFormat.NameOffsetAbsent;
}
