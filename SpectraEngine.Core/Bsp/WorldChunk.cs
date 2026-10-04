using System;
using System.Collections.Generic;

namespace SpectraEngine.Core.Bsp;

/// <summary>
/// One occupied cell of a <see cref="ChunkGrid"/>. A brush is owned by the
/// one cell holding the centre of its inflated AABB, which holds its render
/// surfaces, and is resident in every cell that AABB touches, for queries and
/// welding. Immutable once the <see cref="CsgWorld"/> build returns.
/// </summary>
public sealed class WorldChunk
{
    private readonly List<int> _ownedBrushIndices = [];
    private readonly List<int> _residentBrushIndices = [];
    private readonly List<Polygon> _surfaces = [];
    private readonly List<Polygon> _weldedSurfaces = [];

    internal WorldChunk(ChunkCoord coord) => Coord = coord;

    internal WorldChunk RemapIndices(Func<int, int> map)
    {
        var result = new WorldChunk(Coord) { Bsp = Bsp };
        foreach (int i in _ownedBrushIndices) result._ownedBrushIndices.Add(map(i));
        foreach (int i in _residentBrushIndices) result._residentBrushIndices.Add(map(i));
        result._surfaces.AddRange(_surfaces);
        result._weldedSurfaces.AddRange(_weldedSurfaces);
        return result;
    }

    /// <summary>This cell's coordinates in the grid.</summary>
    public ChunkCoord Coord { get; }

    /// <summary>Placement indices owned by this cell, ascending.</summary>
    public IReadOnlyList<int> OwnedBrushIndices => _ownedBrushIndices;

    /// <summary>
    /// Placement indices resident in this cell, ascending. A superset of
    /// <see cref="OwnedBrushIndices"/>.
    /// </summary>
    public IReadOnlyList<int> ResidentBrushIndices => _residentBrushIndices;

    /// <summary>The carved, pre-snap surfaces of the owned brushes, in placement order.</summary>
    public IReadOnlyList<Polygon> Surfaces => _surfaces;

    /// <summary>
    /// The snapped and welded surfaces of the owned brushes, in placement
    /// order. The same instances as in <see cref="CsgWorld.Surfaces"/>.
    /// </summary>
    public IReadOnlyList<Polygon> WeldedSurfaces => _weldedSurfaces;

    /// <summary>
    /// This cell's BSP tree, built from all its resident brushes. Only valid
    /// for points and ray segments inside this cell.
    /// </summary>
    public BspTree Bsp { get; private set; } = null!;

    internal void AttachBsp(BspTree tree) => Bsp = tree;

    internal void AddResident(int placementIndex) => _residentBrushIndices.Add(placementIndex);

    internal void AddOwned(int placementIndex, Polygon[] surfaces)
    {
        _ownedBrushIndices.Add(placementIndex);
        _surfaces.AddRange(surfaces);
    }

    internal void AddWeldedSurfaces(Polygon[] welded) => _weldedSurfaces.AddRange(welded);
}
