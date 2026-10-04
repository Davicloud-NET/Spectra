using System;
using System.Numerics;

namespace SpectraEngine.Core.Bsp;

/// <summary>
/// A solid-leaf BSP tree as a block of <see cref="FlatBspNode"/> plus the
/// root's child code. Answers queries the same as the <see cref="BspTree"/>
/// it was flattened from.
/// </summary>
// The block may be a memory-mapped view: do not assume ownership or mutate it.
public sealed class FlatBspTree
{
    // Stack frames for one trace. A deeper tree spills to the heap.
    private const int InlineTraceDepth = 64;

    private readonly ReadOnlyMemory<FlatBspNode> _nodes;

    /// <param name="nodes">The internal nodes, in <see cref="BspFlattener"/> order.</param>
    /// <param name="rootIndex">An index into <paramref name="nodes"/>, or a leaf code.</param>
    public FlatBspTree(ReadOnlyMemory<FlatBspNode> nodes, int rootIndex)
    {
        // Only the root is checked: scanning every child would page in the whole block.
        if (rootIndex < FlatBspNode.SolidLeaf || rootIndex >= nodes.Length)
        {
            throw new ArgumentOutOfRangeException(
                nameof(rootIndex), rootIndex,
                $"Root is neither a node index in [0, {nodes.Length}) nor a leaf code.");
        }

        _nodes = nodes;
        RootIndex = rootIndex;
    }

    /// <summary>The root's child code: a node index, or a leaf code for a bare-leaf tree.</summary>
    public int RootIndex { get; }

    /// <summary>The node block, for a writer that has to emit it.</summary>
    public ReadOnlyMemory<FlatBspNode> Nodes => _nodes;

    /// <summary>Internal node count. Leaves take no slots.</summary>
    public int NodeCount => _nodes.Length;

    /// <summary>True when the point lies inside solid space.</summary>
    public bool ContainsPoint(Vector3 point)
    {
        ReadOnlySpan<FlatBspNode> nodes = _nodes.Span;

        int index = RootIndex;
        while (index >= 0)
        {
            ref readonly FlatBspNode node = ref nodes[index];
            index = Plane.DotCoordinate(node.Plane, point) >= 0f ? node.Front : node.Back;
        }
        return index == FlatBspNode.SolidLeaf;
    }

    /// <summary>
    /// Casts a ray against solid space and reports the first surface entered.
    /// A ray that starts inside solid hits at distance 0.
    /// </summary>
    public bool Raycast(Vector3 origin, Vector3 direction, float maxDistance, out BspRaycastHit hit)
    {
        hit = default;
        if (direction == Vector3.Zero || maxDistance <= 0f)
            return false;

        direction = Vector3.Normalize(direction);

        if (ContainsPoint(origin))
        {
            hit = new BspRaycastHit(origin, -direction, 0f);
            return true;
        }

        Vector3 end = origin + direction * maxDistance;
        if (TraceSegment(origin, end, direction, out hit))
        {
            hit = hit with { Distance = Vector3.Distance(origin, hit.Point) };
            return true;
        }
        return false;
    }

    // BspTree.TraceSegment with the recursion unrolled: one frame per crossed
    // splitter holds the deferred far side. Keep every comparison and normal
    // sign in step with the live tree; a flipped one just names the wrong surface.
    private bool TraceSegment(Vector3 origin, Vector3 end, Vector3 direction, out BspRaycastHit hit)
    {
        ReadOnlySpan<FlatBspNode> nodes = _nodes.Span;

        Span<TraceFrame> frames = stackalloc TraceFrame[InlineTraceDepth];
        int depth = 0;

        int index = RootIndex;
        Vector3 a = origin;
        Vector3 b = end;
        bool hasEntry = false;
        Vector3 entryNormal = default;

        while (true)
        {
            while (index >= 0)
            {
                ref readonly FlatBspNode node = ref nodes[index];
                float da = Plane.DotCoordinate(node.Plane, a);
                float db = Plane.DotCoordinate(node.Plane, b);

                if (da >= 0f && db >= 0f)
                {
                    index = node.Front;
                    continue;
                }
                if (da < 0f && db < 0f)
                {
                    index = node.Back;
                    continue;
                }

                float t = da / (da - db);
                Vector3 mid = Vector3.Lerp(a, b, t);

                int near = da >= 0f ? node.Front : node.Back;
                int far = da >= 0f ? node.Back : node.Front;

                // Oriented toward the side the ray came from.
                Vector3 crossingNormal = da >= 0f ? node.Plane.Normal : -node.Plane.Normal;

                if (depth == frames.Length)
                {
                    var grown = new TraceFrame[frames.Length * 2];
                    frames.CopyTo(grown);
                    frames = grown;
                }
                frames[depth++] = new TraceFrame(far, mid, b, crossingNormal);

                // Near side first; hasEntry and entryNormal carry over unchanged.
                index = near;
                b = mid;
            }

            if (index == FlatBspNode.SolidLeaf)
            {
                // a is the crossing point. No entry plane means the ray started in solid.
                hit = new BspRaycastHit(a, hasEntry ? entryNormal : -direction, 0f);
                return true;
            }

            if (depth == 0)
            {
                hit = default;
                return false;
            }

            // Near side was clear; continue across the plane.
            TraceFrame frame = frames[--depth];
            index = frame.Child;
            a = frame.Start;
            b = frame.End;
            entryNormal = frame.EntryNormal;
            hasEntry = true;
        }
    }

    private readonly record struct TraceFrame(int Child, Vector3 Start, Vector3 End, Vector3 EntryNormal);
}
