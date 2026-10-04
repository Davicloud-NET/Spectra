using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;

namespace SpectraEngine.Core.Bsp;

/// <summary>How many brushes reused their carve and how many were carved fresh in one compile.</summary>
public readonly record struct CsgCacheStats(int Hits, int Misses)
{
    /// <summary>Hits plus misses.</summary>
    public int Total => Hits + Misses;
}

/// <summary>
/// Per-brush carve results from one static-world compile, keyed by
/// <see cref="Brush"/> reference and reused by the next compile when the
/// brush's placement and carver sequence are unchanged. Immutable.
/// </summary>
// A hit needs the same brush reference, an equal placement matrix, and the same
// carvers (reference, matrix, precedence flag) in carve order.
//
// Ordered, not a set: clip order changes how a face is split, so the same
// carvers in another order give different polygons.
// Brush by reference: a retexture makes a new Brush and so misses.
// Any doubt is a miss: a false hit means wrong geometry.
//
// One Brush can back several placements. Only the first gets an entry; the
// others fail validation and miss.
public sealed class CsgCompileCache
{
    private readonly Dictionary<Brush, Entry> _entries;

    private CsgCompileCache(Dictionary<Brush, Entry> entries) => _entries = entries;

    /// <summary>Number of brushes with a cached carve result.</summary>
    public int Count => _entries.Count;

    /// <summary>True when the cache holds a carve result for this brush instance.</summary>
    public bool Contains(Brush brush) => _entries.ContainsKey(brush);

    // carverIndices must be in carve order.
    internal bool TryGetValid(
        IReadOnlyList<BrushPlacement> placements, int index, int[] carverIndices,
        [NotNullWhen(true)] out Entry? entry)
    {
        entry = null;
        BrushPlacement placement = placements[index];
        if (!_entries.TryGetValue(placement.Brush, out Entry? cached))
            return false;

        // Float == is enough: NaN never reaches a carve, and -0 == 0 can only
        // flip the sign of a zero.
        if (cached.Placement != placement.Transform)
            return false;

        CarverRecord[] records = cached.Carvers;
        if (records.Length != carverIndices.Length)
            return false;

        for (int k = 0; k < records.Length; k++)
        {
            int o = carverIndices[k];
            BrushPlacement carver = placements[o];
            if (!ReferenceEquals(records[k].Brush, carver.Brush) ||
                records[k].Transform != carver.Transform ||
                records[k].CarverWins != (o < index))
            {
                return false;
            }
        }

        entry = cached;
        return true;
    }

    internal static CsgCompileCache FromEntries(IReadOnlyList<BrushPlacement> placements, Entry?[] entries)
    {
        // The cache depends on reference keys, so the comparer is explicit.
        var map = new Dictionary<Brush, Entry>(placements.Count, ReferenceEqualityComparer.Instance);
        for (int i = 0; i < entries.Length; i++)
        {
            // First placement of a shared brush wins.
            map.TryAdd(placements[i].Brush, entries[i]!);
        }
        return new CsgCompileCache(map);
    }

    internal readonly struct CarverRecord(Brush brush, Matrix4x4 transform, bool carverWins)
    {
        public readonly Brush Brush = brush;
        public readonly Matrix4x4 Transform = transform;
        public readonly bool CarverWins = carverWins;
    }

    // Surfaces is shared with every compile result that reuses it.
    internal sealed class Entry
    {
        public readonly Matrix4x4 Placement;
        public readonly CarverRecord[] Carvers;
        public readonly Polygon[] Surfaces;

        private Entry(Matrix4x4 placement, CarverRecord[] carvers, Polygon[] surfaces)
        {
            Placement = placement;
            Carvers = carvers;
            Surfaces = surfaces;
        }

        // carverIndices must be in carve order.
        public static Entry Create(
            IReadOnlyList<BrushPlacement> placements, int index, int[] carverIndices, Polygon[] surfaces)
        {
            var carvers = new CarverRecord[carverIndices.Length];
            for (int k = 0; k < carvers.Length; k++)
            {
                int o = carverIndices[k];
                carvers[k] = new CarverRecord(placements[o].Brush, placements[o].Transform, o < index);
            }
            return new Entry(placements[index].Transform, carvers, surfaces);
        }
    }
}
