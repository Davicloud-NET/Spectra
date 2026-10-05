using System.Numerics;

namespace SpectraEngine.Core.Scene;

// The faces of one brush that a segment lies in, as outward world normals.
// Zero where there is none. There are two when the segment runs along an edge.
// The brush is then on one side of the segment and not on the other.
internal readonly record struct LineFaces(Vector3 First, Vector3 Second)
{
    // True when the brush covers the points just off the segment toward side.
    // Always, for a segment that lies in none of its faces.
    public bool Covers(Vector3 side) =>
        Vector3.Dot(side, First) <= 0f && Vector3.Dot(side, Second) <= 0f;
}
