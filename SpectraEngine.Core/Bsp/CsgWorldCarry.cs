namespace SpectraEngine.Core.Bsp;

// Per-placement state one compile hands to the next, index-aligned with the
// world's placements. Paged so an incremental compile clones only touched pages.
// Immutable after construction.
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

    // Pre-snap, world space.
    public PagedArray<Polygon[]> CarvedPerBrush { get; }
    // null: this world did not request snap results.
    public PagedArray<Polygon[]?> SnappedPerBrush { get; }

    public PagedArray<Polygon[]> WeldedPerBrush { get; }

    // Broadphase neighbours in carve (clip) order.
    public PagedArray<int[]> CarveNeighbors { get; }

    // Ascending. Placements sharing a footprint box share one array.
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
