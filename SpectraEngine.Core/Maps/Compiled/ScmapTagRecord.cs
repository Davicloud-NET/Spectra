using System.Runtime.InteropServices;

namespace SpectraEngine.Core.Maps.Compiled;

/// <summary>
/// One 8-byte <c>NTAG</c> record: one tag of one node. Records are in node
/// order, and a node's own are in the order it got its tags.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct ScmapTagRecord
{
    /// <summary>Index into <c>NODE</c> of the node that has the tag.</summary>
    public readonly uint NodeIndex;

    /// <summary>Index into <c>STRT</c> of the tag.</summary>
    public readonly uint TagString;

    /// <summary>Builds one tag record.</summary>
    public ScmapTagRecord(uint nodeIndex, uint tagString)
    {
        NodeIndex = nodeIndex;
        TagString = tagString;
    }
}
