using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace SpectraEngine.Core.Bsp;

/// <summary>How many cells kept their BSP tree and how many were rebuilt in one compile.</summary>
public readonly record struct CsgBspStats(int Reused, int Built)
{
    /// <summary>Reused plus built.</summary>
    public int Total => Reused + Built;
}

/// <summary>
/// Per-cell BSP trees from one static-world compile, reused by the next when a
/// cell's resident welded arrays are the same references. Immutable.
/// </summary>
// A welded array only keeps its reference through weld-cache and carve-cache
// hits, so reference equality implies identical tree input. Any doubt is a
// miss: a false hit means stale queries.
public sealed class CsgBspCache
{
    private readonly Dictionary<ChunkCoord, Entry> _entries;

    private CsgBspCache(Dictionary<ChunkCoord, Entry> entries) => _entries = entries;

    /// <summary>Number of cells with a cached tree.</summary>
    public int Count => _entries.Count;

    // Caller must still check ResidentsMatch before reusing the entry.
    internal bool TryGet(ChunkCoord coord, [NotNullWhen(true)] out Entry? entry) =>
        _entries.TryGetValue(coord, out entry);

    // Compares relative order, so placement-index shifts between compiles are fine.
    internal static bool ResidentsMatch(Entry entry, WorldChunk chunk, Polygon[][] weldedPerBrush)
    {
        Polygon[][] recorded = entry.ResidentWelded;
        IReadOnlyList<int> residents = chunk.ResidentBrushIndices;
        if (recorded.Length != residents.Count)
            return false;

        for (int k = 0; k < recorded.Length; k++)
        {
            if (!ReferenceEquals(recorded[k], weldedPerBrush[residents[k]]))
                return false;
        }
        return true;
    }

    internal static CsgBspCache FromEntries(IReadOnlyList<WorldChunk> orderedChunks, Entry[] entries)
    {
        var map = new Dictionary<ChunkCoord, Entry>(entries.Length);
        for (int c = 0; c < entries.Length; c++)
            map.Add(orderedChunks[c].Coord, entries[c]);
        return new CsgBspCache(map);
    }

    // residentWelded is in ascending placement-index order.
    internal sealed class Entry(Polygon[][] residentWelded, BspTree tree)
    {
        public readonly Polygon[][] ResidentWelded = residentWelded;
        public readonly BspTree Tree = tree;
    }
}
