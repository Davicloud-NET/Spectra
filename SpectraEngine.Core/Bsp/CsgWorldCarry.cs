namespace SpectraEngine.Core.Bsp;

/// <summary>
/// The per-placement working state one static-world compile hands to the next
/// — what makes the incremental (previous-world) compile's fixed cost
/// O(edit neighbourhood) instead of O(world). Index-aligned with the world's
/// placements: each brush's carved (pre-snap) surfaces, its snapped+welded
/// surfaces, its broadphase neighbour list in carve order, and its weld
/// candidate set. Stored paged (<see cref="PagedArray{T}"/>) so a derived
/// compile clones only the pages an edit touches.
/// </summary>
/// <remarks>
/// Immutable after construction, like every other compiled CSG artifact; the
/// classic validation caches (<see cref="CsgCompileCache"/> and friends) are
/// derived views over this state, materialized lazily only when a compile
/// falls back to the fully validated path (see
/// <see cref="CsgIncrementalCompiler"/> for the fallback triggers).
/// </remarks>
internal sealed class CsgWorldCarry
{
    public CsgWorldCarry(
        PagedArray<Polygon[]> carvedPerBrush,
        PagedArray<Polygon[]> weldedPerBrush,
        PagedArray<int[]> carveNeighbors,
        PagedArray<int[]> weldCandidates,
        PagedArray<Polygon[]?> snappedPerBrush)
    {
        CarvedPerBrush = carvedPerBrush;
        WeldedPerBrush = weldedPerBrush;
        CarveNeighbors = carveNeighbors;
        WeldCandidates = weldCandidates;
        SnappedPerBrush = snappedPerBrush;
    }

    /// <summary>Each placement's carved (pre-snap, world-space) surfaces.</summary>
    public PagedArray<Polygon[]> CarvedPerBrush { get; }
    /// <summary>Immutable snap results; null means not requested by this world.</summary>
    public PagedArray<Polygon[]?> SnappedPerBrush { get; }

    /// <summary>Each placement's snapped+welded surfaces.</summary>
    public PagedArray<Polygon[]> WeldedPerBrush { get; }

    /// <summary>
    /// Each placement's broadphase neighbour indices in carve (clip) order —
    /// the order its carved surfaces were produced under.
    /// </summary>
    public PagedArray<int[]> CarveNeighbors { get; }

    /// <summary>
    /// Each placement's weld candidate index set, ascending (see
    /// <see cref="ChunkWelder"/>). Placements sharing a footprint box share
    /// one array.
    /// </summary>
    public PagedArray<int[]> WeldCandidates { get; }

    internal CsgWorldCarry Expand(int count)
    {
        int oldCount = CarvedPerBrush.Count;
        if (count == oldCount) return this;
        var surfaces = new (int, Polygon[])[count - oldCount];
        var indices = new (int, int[])[surfaces.Length];
        for (int i = 0; i < surfaces.Length; i++)
        { surfaces[i] = (oldCount + i, []); indices[i] = (oldCount + i, []); }
        return new(CarvedPerBrush.WithReplacements(count, surfaces), WeldedPerBrush.WithReplacements(count, surfaces),
            CarveNeighbors.WithReplacements(count, indices), WeldCandidates.WithReplacements(count, indices),
            SnappedPerBrush.WithReplacements(count, []));
    }
}
