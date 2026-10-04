using System;

namespace SpectraEngine.Core.Assets.Models;

/// <summary>
/// Whole-model properties, stored in the <c>.smodel</c> header's <c>u16</c> flag
/// word. Append-only: these values are on disk.
/// </summary>
// HasSkeleton and HasCollision repeat what the section table says; the reader
// cross-checks them. Index32 has no other source.
[Flags]
public enum SmodelFlags : ushort
{
    /// <summary>No flag set.</summary>
    None = 0,

    /// <summary>A <c>SKEL</c> section is present.</summary>
    HasSkeleton = 1 << 0,

    /// <summary>A <c>COLL</c> section is present.</summary>
    HasCollision = 1 << 1,

    /// <summary>
    /// <c>IBUF</c> holds <c>u32</c> elements instead of <c>u16</c>. Set only
    /// when the vertex count does not fit in 16 bits.
    /// </summary>
    Index32 = 1 << 2,
}
