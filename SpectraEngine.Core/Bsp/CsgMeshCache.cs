using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace SpectraEngine.Core.Bsp;

/// <summary>
/// How many geometry-owning cells kept their mesh and how many were rebuilt in one compile.
/// </summary>
public readonly record struct CsgMeshStats(int Reused, int Built)
{
    /// <summary>Reused plus built.</summary>
    public int Total => Reused + Built;
}

/// <summary>
/// Per-cell render meshes from one static-world compile, reused by the next when
/// a cell's owned welded arrays are the same references. Immutable.
/// </summary>
// Same reasoning as CsgBspCache: reference equality implies identical input.
// Any doubt is a miss: a false hit means stale render geometry.
public sealed class CsgMeshCache
{
    private readonly Dictionary<ChunkCoord, Entry> _entries;

    private CsgMeshCache(Dictionary<ChunkCoord, Entry> entries) => _entries = entries;

    /// <summary>Number of cells with a cached mesh.</summary>
    public int Count => _entries.Count;

    // Caller must still check OwnersMatch before reusing the entry.
    internal bool TryGet(ChunkCoord coord, [NotNullWhen(true)] out Entry? entry) =>
        _entries.TryGetValue(coord, out entry);

    // Compares relative order, so placement-index shifts between compiles are fine.
    internal static bool OwnersMatch(Entry entry, WorldChunk chunk, Polygon[][] weldedPerBrush)
    {
        Polygon[][] recorded = entry.OwnedWelded;
        IReadOnlyList<int> owned = chunk.OwnedBrushIndices;
        if (recorded.Length != owned.Count)
            return false;

        for (int k = 0; k < recorded.Length; k++)
        {
            if (!ReferenceEquals(recorded[k], weldedPerBrush[owned[k]]))
                return false;
        }
        return true;
    }

    internal static CsgMeshCache FromEntries(IReadOnlyList<WorldChunk> geometryCells, Entry[] entries)
    {
        var map = new Dictionary<ChunkCoord, Entry>(entries.Length);
        for (int c = 0; c < entries.Length; c++)
            map.Add(geometryCells[c].Coord, entries[c]);
        return new CsgMeshCache(map);
    }

    // ownedWelded is in ascending placement-index order.
    internal sealed class Entry(Polygon[][] ownedWelded, ChunkMesh mesh)
    {
        public readonly Polygon[][] OwnedWelded = ownedWelded;
        public readonly ChunkMesh Mesh = mesh;
    }
}
