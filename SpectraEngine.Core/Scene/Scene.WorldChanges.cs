using System;
using System.Numerics;
using SpectraEngine.Core.Bsp;

namespace SpectraEngine.Core.Scene;

public sealed partial class Scene
{
    private readonly WorldChange[] _worldChanges = new WorldChange[256];
    private readonly record struct WorldChange(int Revision, bool Full, bool HasBounds, Aabb Bounds);

    private void RecordWorldPublication(CsgWorld? world)
    {
        int revision = StaticWorldCompileCount;
        var cells = world?.DirtyCells;
        bool hasBounds = cells is { Count: > 0 };
        Vector3 min = new(float.MaxValue), max = new(float.MinValue);
        if (cells is not null)
            foreach (ChunkCoord cell in cells)
            {
                min = Vector3.Min(min, cell.MinCorner);
                max = Vector3.Max(max, cell.MaxCorner);
            }
        _worldChanges[revision % _worldChanges.Length] =
            new(revision, cells is null || cells.Count == 0, hasBounds, new Aabb(min, max));
    }

    // Conservative: true when the history has overflowed, so the caller reselects.
    internal bool WorldChangedSince(int revision, in Aabb dependencies)
    {
        if (revision < 0 || revision > StaticWorldCompileCount ||
            StaticWorldCompileCount - revision > _worldChanges.Length) return true;
        for (int i = revision + 1; i <= StaticWorldCompileCount; i++)
        {
            WorldChange change = _worldChanges[i % _worldChanges.Length];
            if (change.Revision != i || change.Full ||
                (change.HasBounds && change.Bounds.Intersects(dependencies))) return true;
        }
        return false;
    }
}
