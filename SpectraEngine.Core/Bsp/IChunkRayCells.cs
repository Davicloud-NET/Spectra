using System.Numerics;

namespace SpectraEngine.Core.Bsp;

// The per-cell trees a ray walk steps through.
internal interface IChunkRayCells
{
    // Inclusive cell bounds over every occupied cell; false when there is none.
    bool TryGetCellBounds(out ChunkCoord min, out ChunkCoord max);

    bool ContainsPoint(Vector3 point);

    // False for an empty cell and for a segment that hits nothing in it.
    bool RaycastCell(ChunkCoord cell, Vector3 origin, Vector3 direction, float maxDistance, out BspRaycastHit hit);
}
