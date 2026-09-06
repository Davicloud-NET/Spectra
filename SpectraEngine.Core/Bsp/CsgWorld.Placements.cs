using System;
using System.Collections.Generic;
using System.Threading;

namespace SpectraEngine.Core.Bsp;

public sealed partial class CsgWorld
{
    private CsgWorld? _canonicalWorld;

    // Public placement/chunk views and cooking use dense authored indices.
    // Live compiler and query paths stay on stable slots; materialization is
    // deliberately outside their edit-neighborhood work.
    private CsgWorld CanonicalWorld
    {
        get
        {
            if (_canonicalWorld is null)
            {
                var placements = SourceSnapshot!.ToDense(out int[] orderedSlots);
                var mapping = new int[SourceSnapshot.Slots.Count];
                Array.Fill(mapping, -1);
                for (int i = 0; i < orderedSlots.Length; i++) mapping[orderedSlots[i]] = i;
                var canonical = new CsgWorld(placements, _surfaces, SurfaceCount,
                    StorageChunks.RemapIndices(i => mapping[i]), ChunkMeshes, DirtyCells,
                    RemapCarry(Carry, mapping, placements.Length), _lazyCaches,
                    _compileCache, CacheStats, _weldCache, WeldStats, _bspCache, BspStats, _meshCache, MeshStats);
                Interlocked.CompareExchange(ref _canonicalWorld, canonical, null);
            }
            return _canonicalWorld;
        }
    }

    private Polygon[] SurfacesArray()
    {
        _ = Surfaces;
        return _surfaces!;
    }

    private static CsgWorld FromDense(PlacementSnapshot snapshot, CsgWorld dense)
    {
        var mapping = new int[snapshot.Count];
        for (int i = 0; i < mapping.Length; i++) mapping[i] = snapshot.EntryAt(i).Slot;
        return new(snapshot.Storage, dense._surfaces, dense.SurfaceCount,
            dense.StorageChunks.RemapIndices(i => mapping[i]), dense.ChunkMeshes, dense.DirtyCells,
            RemapCarry(dense.Carry, mapping, snapshot.Slots.Count), dense._lazyCaches,
            dense._compileCache, dense.CacheStats, dense._weldCache, dense.WeldStats,
            dense._bspCache, dense.BspStats, dense._meshCache, dense.MeshStats);
    }

    private static CsgWorldCarry RemapCarry(CsgWorldCarry source, int[] mapping, int count)
    {
        var carved = new Polygon[count][];
        var welded = new Polygon[count][];
        var snaps = new Polygon[]?[count];
        var neighbors = new int[count][];
        var candidates = new int[count][];
        Array.Fill(carved, []); Array.Fill(welded, []);
        Array.Fill(neighbors, []); Array.Fill(candidates, []);
        // Shared candidate sets remain shared after projection.
        var mappedSets = new Dictionary<int[], int[]>();
        for (int i = 0; i < mapping.Length; i++)
        {
            int target = mapping[i];
            if (target < 0) continue;
            carved[target] = source.CarvedPerBrush[i];
            welded[target] = source.WeldedPerBrush[i];
            snaps[target] = source.SnappedPerBrush[i];
            neighbors[target] = Map(source.CarveNeighbors[i]);
            candidates[target] = Map(source.WeldCandidates[i]);
        }
        return new(PagedArray<Polygon[]>.From(carved), PagedArray<Polygon[]>.From(welded),
            PagedArray<int[]>.From(neighbors), PagedArray<int[]>.From(candidates), PagedArray<Polygon[]?>.From(snaps));
        int[] Map(int[] values)
        {
            if (values.Length == 0) return [];
            if (mappedSets.TryGetValue(values, out var cached)) return cached;
            var result = new int[values.Length];
            for (int j = 0; j < result.Length; j++)
            {
                result[j] = mapping[values[j]];
                if (result[j] < 0) throw new InvalidOperationException("A deleted placement survived in a CSG dependency set.");
            }
            mappedSets.Add(values, result);
            return result;
        }
    }
}
