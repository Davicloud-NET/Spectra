using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace SpectraEngine.Core.Bsp;

/// <summary>How many brushes kept their welded surfaces and how many were welded fresh in one compile.</summary>
public readonly record struct CsgWeldStats(int Reused, int Welded)
{
    /// <summary>Reused plus welded.</summary>
    public int Total => Reused + Welded;
}

/// <summary>
/// Per-brush snap + weld results from one static-world compile, reused by the next
/// when a brush's carved array and its candidates' carved arrays are the same
/// references. Immutable.
/// </summary>
// A fresh carve always allocates, so a carved array is only a cached key after
// a carve-cache hit, which already checked brush, placement and carver sequence.
// Any doubt is a miss: a false hit means stale geometry.
public sealed class CsgWeldCache
{
    private readonly Dictionary<Polygon[], Entry> _entries;

    private CsgWeldCache(Dictionary<Polygon[], Entry> entries) => _entries = entries;

    /// <summary>Number of brushes with a cached weld result.</summary>
    public int Count => _entries.Count;

    // Caller must still check CandidatesMatch. Kept separate so ChunkWelder can
    // memoize that check per candidate set.
    internal bool TryGet(Polygon[] carvedSurfaces, [NotNullWhen(true)] out Entry? entry) =>
        _entries.TryGetValue(carvedSurfaces, out entry);

    // Compares relative order, so index shifts between compiles are fine.
    internal static bool CandidatesMatch(Entry entry, int[] candidateIndices, Polygon[][] perBrushSurfaces)
    {
        Polygon[][] records = entry.CandidateCarves;
        if (records.Length != candidateIndices.Length)
            return false;

        for (int k = 0; k < records.Length; k++)
        {
            if (!ReferenceEquals(records[k], perBrushSurfaces[candidateIndices[k]]))
                return false;
        }
        return true;
    }

    internal static CsgWeldCache FromEntries(Polygon[][] perBrushSurfaces, Entry[] entries)
    {
        // The cache depends on reference keys, so the comparer is explicit.
        // Two placements should never share a carved array; TryAdd keeps the first.
        var map = new Dictionary<Polygon[], Entry>(entries.Length, ReferenceEqualityComparer.Instance);
        for (int i = 0; i < entries.Length; i++)
            map.TryAdd(perBrushSurfaces[i], entries[i]);
        return new CsgWeldCache(map);
    }

    // candidateCarves is in ascending candidate order and shared between
    // entries with the same candidate set.
    internal sealed class Entry(Polygon[][] candidateCarves, Polygon[] welded)
    {
        public readonly Polygon[][] CandidateCarves = candidateCarves;
        public readonly Polygon[] Welded = welded;
    }
}
