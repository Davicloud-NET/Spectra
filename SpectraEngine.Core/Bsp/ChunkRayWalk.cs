using System;
using System.Numerics;

namespace SpectraEngine.Core.Bsp;

// One ray walk for the live world and a baked one, so the two cannot drift.
internal static class ChunkRayWalk
{
    // How far a cell's ray segment may overshoot its interval when accepting a
    // hit, so a crossing that rounds just past a cell boundary is taken by the
    // earlier cell. Must stay at most ChunkGrid.WeldBand: brushes with geometry
    // in the overshoot window are then resident in the current cell too.
    private const float IntervalEpsilon = Polygon.Epsilon;

    public static bool Cast<TCells>(
        TCells cells, Vector3 origin, Vector3 direction, float maxDistance, out BspRaycastHit hit)
        where TCells : struct, IChunkRayCells
    {
        hit = default;
        if (direction == Vector3.Zero || maxDistance <= 0f)
            return false;

        direction = Vector3.Normalize(direction);

        // Starting inside solid hits at once, as the monolithic tree does.
        if (cells.ContainsPoint(origin))
        {
            hit = new BspRaycastHit(origin, -direction, 0f);
            return true;
        }

        // Clip the walk to the occupied-cell box. Without it a miss walks
        // every cell up to maxDistance, and never ends for an infinite one:
        // tMax saturates in float while the cell integers keep stepping.
        if (!cells.TryGetCellBounds(out ChunkCoord cellMin, out ChunkCoord cellMax))
            return false;
        float boxEnter = 0f;
        float walkEnd = maxDistance;
        if (!ClipToBox(origin, direction, cellMin.MinCorner, cellMax.MaxCorner, ref boxEnter, ref walkEnd))
            return false;

        // A ray starting far outside skips ahead to one cell before the box.
        // The one-cell back-off absorbs slab-vs-DDA rounding. A ray starting
        // inside keeps baseT at 0, so its arithmetic is unchanged.
        float baseT = 0f;
        Vector3 walkOrigin = origin;
        if (boxEnter > ChunkCoord.CellSize)
        {
            baseT = boxEnter - ChunkCoord.CellSize;
            walkOrigin = origin + direction * baseT;
        }
        float maxLocal = maxDistance - baseT;

        // Amanatides-Woo 3D-DDA. An origin on a cell boundary belongs to the
        // positive-side cell; heading negative from there gives tMax = 0, a
        // zero-length first segment that the loop skips.
        ChunkCoord startCell = ChunkCoord.FromPosition(walkOrigin);
        int x = startCell.X, y = startCell.Y, z = startCell.Z;
        SetupAxis(walkOrigin.X, direction.X, x, out int stepX, out float tMaxX, out float tDeltaX);
        SetupAxis(walkOrigin.Y, direction.Y, y, out int stepY, out float tMaxY, out float tDeltaY);
        SetupAxis(walkOrigin.Z, direction.Z, z, out int stepZ, out float tMaxZ, out float tDeltaZ);

        // One cell of slack past the box exit, for slab-vs-DDA rounding.
        float walkLimit = walkEnd - baseT + ChunkCoord.CellSize;

        float entryT = 0f;
        while (true)
        {
            // t is relative to the walk origin: global t = baseT + local t.
            float exitT = MathF.Min(MathF.Min(tMaxX, tMaxY), tMaxZ);
            float segmentEnd = MathF.Min(exitT, maxLocal);

            if (segmentEnd > entryT)
            {
                // Overshoot only at cell boundaries, never past the ray's end.
                float window = segmentEnd - entryT +
                    (segmentEnd < maxLocal ? IntervalEpsilon : 0f);
                Vector3 segmentStart = walkOrigin + direction * entryT;
                if (cells.RaycastCell(new ChunkCoord(x, y, z), segmentStart, direction, window, out BspRaycastHit local))
                {
                    float distance = baseT + entryT + local.Distance;
                    if (distance > maxDistance)
                        return false;
                    hit = new BspRaycastHit(local.Point, local.Normal, distance);
                    return true;
                }
            }

            if (exitT >= maxLocal || entryT > walkLimit)
                return false;

            // Ties (edge and corner crossings) step one axis at a time.
            entryT = exitT;
            if (tMaxX <= tMaxY && tMaxX <= tMaxZ)
            {
                x += stepX;
                tMaxX += tDeltaX;
            }
            else if (tMaxY <= tMaxZ)
            {
                y += stepY;
                tMaxY += tDeltaY;
            }
            else
            {
                z += stepZ;
                tMaxZ += tDeltaZ;
            }
        }
    }

    // Boundary arithmetic must match ChunkCoord.MinCorner/MaxCorner bit for bit.
    private static void SetupAxis(float origin, float direction, int cell,
        out int step, out float tMax, out float tDelta)
    {
        if (direction > 0f)
        {
            step = 1;
            tMax = ((cell + 1) * ChunkCoord.CellSize - origin) / direction;
            tDelta = ChunkCoord.CellSize / direction;
        }
        else if (direction < 0f)
        {
            step = -1;
            tMax = (cell * ChunkCoord.CellSize - origin) / direction;
            tDelta = ChunkCoord.CellSize / -direction;
        }
        else
        {
            step = 0;
            tMax = float.PositiveInfinity;
            tDelta = float.PositiveInfinity;
        }
    }

    // Slab test. Grows enter and shrinks walkEnd to the box interval.
    private static bool ClipToBox(Vector3 origin, Vector3 direction, Vector3 boxMin, Vector3 boxMax,
        ref float enter, ref float walkEnd)
    {
        return ClipAxis(origin.X, direction.X, boxMin.X, boxMax.X, ref enter, ref walkEnd) &&
               ClipAxis(origin.Y, direction.Y, boxMin.Y, boxMax.Y, ref enter, ref walkEnd) &&
               ClipAxis(origin.Z, direction.Z, boxMin.Z, boxMax.Z, ref enter, ref walkEnd);
    }

    private static bool ClipAxis(float origin, float direction, float min, float max, ref float enter, ref float exit)
    {
        if (direction == 0f)
            return origin >= min && origin <= max;

        float inverse = 1f / direction;
        float t0 = (min - origin) * inverse;
        float t1 = (max - origin) * inverse;
        if (t0 > t1)
            (t0, t1) = (t1, t0);
        if (t0 > enter)
            enter = t0;
        if (t1 < exit)
            exit = t1;
        return enter <= exit;
    }
}
