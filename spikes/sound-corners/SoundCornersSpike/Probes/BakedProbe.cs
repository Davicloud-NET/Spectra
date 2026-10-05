using System;
using System.Numerics;
using SpectraEngine.Core.Bsp;
using SpectraEngine.Core.Maps.Compiled;

namespace SoundCornersSpike.Probes;

// A baked world: CompiledStaticWorld's public point and ray queries, and its
// flat per-cell trees for the box test.
internal sealed class BakedProbe(CompiledStaticWorld world, string name) : SolidProbe
{
    public override string Name => name;

    public override bool IsSolid(Vector3 point) => world.ContainsPoint(point);

    public override bool SegmentBlocked(Vector3 a, Vector3 b)
    {
        Vector3 delta = b - a;
        float length = delta.Length();
        if (length <= 0f) return world.ContainsPoint(a);

        return world.Raycast(a, delta, length, out _);
    }

    public override bool HasGeometry(ChunkCoord chunk) =>
        world.TryGetChunk(chunk, out CompiledStaticWorldChunk found) && found.Bsp is not null;

    public override BoxContent ClassifyBox(Vector3 center, Vector3 half)
    {
        if (!world.TryGetChunk(ChunkCoord.FromPosition(center), out CompiledStaticWorldChunk chunk)
            || chunk.Bsp is not { } tree)
        {
            return BoxContent.Air;
        }

        int seen = 0;
        Descend(tree.Nodes.Span, tree.RootIndex, center, half, ref seen);
        return ToContent(seen);
    }

    private static void Descend(ReadOnlySpan<FlatBspNode> nodes, int index, Vector3 center, Vector3 half, ref int seen)
    {
        while (index >= 0)
        {
            ref readonly FlatBspNode node = ref nodes[index];
            float distance = Plane.DotCoordinate(node.Plane, center);
            float reach = Reach(in node.Plane, half);

            if (distance >= reach)
            {
                index = node.Front;
            }
            else if (distance <= -reach)
            {
                index = node.Back;
            }
            else
            {
                Descend(nodes, node.Front, center, half, ref seen);
                if (seen == (SeenAir | SeenSolid)) return;
                index = node.Back;
            }
        }

        seen |= index == FlatBspNode.SolidLeaf ? SeenSolid : SeenAir;
    }
}
