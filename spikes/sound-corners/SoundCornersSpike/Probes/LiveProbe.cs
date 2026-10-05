using System.Numerics;
using SpectraEngine.Core.Bsp;

namespace SoundCornersSpike.Probes;

// The live world: CsgWorld's public point and ray queries, and its per-cell
// trees for the box test.
internal sealed class LiveProbe(CsgWorld world) : SolidProbe
{
    public override string Name => "live";

    public CsgWorld World => world;

    public override bool IsSolid(Vector3 point) => world.ContainsPoint(point);

    public override bool SegmentBlocked(Vector3 a, Vector3 b)
    {
        Vector3 delta = b - a;
        float length = delta.Length();
        if (length <= 0f) return world.ContainsPoint(a);

        return world.Raycast(a, delta, length, out _);
    }

    public override bool HasGeometry(ChunkCoord chunk) => world.Chunks.TryGet(chunk, out _);

    // The box must lie inside one chunk: a cell's tree is only valid there.
    public override BoxContent ClassifyBox(Vector3 center, Vector3 half)
    {
        if (!world.Chunks.TryGet(ChunkCoord.FromPosition(center), out WorldChunk chunk))
            return BoxContent.Air;

        int seen = 0;
        Descend(chunk.Bsp.Root, center, half, ref seen);
        return ToContent(seen);
    }

    private static void Descend(BspNode node, Vector3 center, Vector3 half, ref int seen)
    {
        while (!node.IsLeaf)
        {
            Plane plane = node.Plane;
            float distance = Plane.DotCoordinate(plane, center);
            float reach = Reach(in plane, half);

            if (distance >= reach)
            {
                node = node.Front!;
            }
            else if (distance <= -reach)
            {
                node = node.Back!;
            }
            else
            {
                Descend(node.Front!, center, half, ref seen);
                if (seen == (SeenAir | SeenSolid)) return;
                node = node.Back!;
            }
        }

        seen |= node.IsSolid ? SeenSolid : SeenAir;
    }
}
