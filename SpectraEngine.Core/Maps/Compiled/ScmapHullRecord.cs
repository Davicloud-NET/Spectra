using System.Runtime.InteropServices;

namespace SpectraEngine.Core.Maps.Compiled;

/// <summary>
/// One 16-byte <c>COLL</c> record: the collision hull of one baked world brush,
/// as a run of the section's planes. There is one per baked brush node, in node
/// order.
/// </summary>
// Planes, not a native hull blob: a plane set is what Brush is built from, and
// it does not tie the file to a physics library's layout.
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct ScmapHullRecord
{
    /// <summary>
    /// Index into <c>NODE</c> of the brush's node. The node places the hull and
    /// says whether it subtracts.
    /// </summary>
    public readonly uint NodeIndex;

    /// <summary>How many planes bound this hull. At least <see cref="ScmapFormat.MinimumHullPlanes"/>.</summary>
    public readonly uint PlaneCount;

    /// <summary>Index of this hull's first plane in the section's plane array.</summary>
    public readonly uint PlaneStart;

    /// <summary>Reserved; written zero.</summary>
    public readonly uint Reserved;

    /// <summary>Builds one hull record.</summary>
    public ScmapHullRecord(uint nodeIndex, uint planeCount, uint planeStart)
    {
        NodeIndex = nodeIndex;
        PlaneCount = planeCount;
        PlaneStart = planeStart;
        Reserved = 0;
    }
}
