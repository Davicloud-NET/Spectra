using System.Collections.Generic;

namespace SpectraEngine.Core.Assets.Packs;

/// <summary>
/// One logical path a source decides, and whether the decision is a deletion.
/// </summary>
public readonly record struct MountPath(string Path, bool IsTombstone);

/// <summary>
/// A content source that can list every logical path it decides, tombstones
/// included. Optional: a source without it is flattened from its enumeration.
/// </summary>
// TryEnumerate skips tombstones, so a mount stack built from it would never see a deletion.
public interface IMountPathSource
{
    /// <summary>Appends every path this source decides to <paramref name="results"/>.</summary>
    void EnumerateMountPaths(List<MountPath> results);
}
