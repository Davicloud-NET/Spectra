using System;
using System.Collections.Generic;
using System.Numerics;
using SpectraEngine.Core.Bsp;

namespace SoundCornersSpike.Grid;

// The vertical limit: no shortest path has to rise above the highest brush
// near it or drop below the lowest, unless one of its own two ends is there.
internal static class WorldHeights
{
    // The lowest bottom and the highest top of the brushes whose boxes reach
    // the square of the given half size round the centre. Reads only the
    // chunk grid and the placements, so a worker can call it.
    public static void Near(CsgWorld world, Vector3 center, float halfSize, out float bottom, out float top)
    {
        bottom = float.PositiveInfinity;
        top = float.NegativeInfinity;
        if (!world.Chunks.TryGetCellBounds(out ChunkCoord low, out ChunkCoord high)) return;

        ChunkCoord from = ChunkCoord.FromPosition(center - new Vector3(halfSize, 0f, halfSize));
        ChunkCoord to = ChunkCoord.FromPosition(center + new Vector3(halfSize, 0f, halfSize));
        IReadOnlyList<BrushPlacement> placements = world.Placements;

        for (int x = Math.Max(from.X, low.X); x <= Math.Min(to.X, high.X); x++)
        {
            for (int z = Math.Max(from.Z, low.Z); z <= Math.Min(to.Z, high.Z); z++)
            {
                for (int y = low.Y; y <= high.Y; y++)
                {
                    if (!world.Chunks.TryGet(new ChunkCoord(x, y, z), out WorldChunk chunk)) continue;

                    foreach (int resident in chunk.ResidentBrushIndices)
                    {
                        BrushPlacement placement = placements[resident];
                        if (placement.Brush.Operation == BrushOperation.Subtractive) continue;

                        Aabb bounds = placement.WorldBounds;
                        if (bounds.Min.X > center.X + halfSize || bounds.Max.X < center.X - halfSize) continue;
                        if (bounds.Min.Z > center.Z + halfSize || bounds.Max.Z < center.Z - halfSize) continue;

                        bottom = MathF.Min(bottom, bounds.Min.Y);
                        top = MathF.Max(top, bounds.Max.Y);
                    }
                }
            }
        }
    }

    // The highest a cell centre may be for a flood from the listener to the
    // given sounds.
    public static float Ceiling(CsgWorld world, Vector3 listener, ReadOnlySpan<Vector3> sounds, float radius, float cell)
    {
        Limits(world, listener, sounds, radius, cell, out _, out float ceiling);
        return ceiling;
    }

    public static void Limits(
        CsgWorld world, Vector3 listener, ReadOnlySpan<Vector3> sounds, float radius, float cell,
        out float floor, out float ceiling)
    {
        Near(world, listener, radius, out float bottom, out float top);

        bottom = MathF.Min(bottom, listener.Y);
        top = MathF.Max(top, listener.Y);
        foreach (Vector3 sound in sounds)
        {
            bottom = MathF.Min(bottom, sound.Y);
            top = MathF.Max(top, sound.Y);
        }

        floor = bottom - cell;
        ceiling = top + cell;
    }

    /// <summary>Sets the vertical limit on a flood's options.</summary>
    public static void Bound(
        FloodOptions options, CsgWorld world, Vector3 listener, ReadOnlySpan<Vector3> sounds, float radius, float cell)
    {
        Limits(world, listener, sounds, radius, cell, out float floor, out float ceiling);
        options.MinY = floor;
        options.MaxY = ceiling;
    }
}
