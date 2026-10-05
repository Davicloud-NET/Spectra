using SpectraEngine.Core.Bsp;
using SpectraEngine.Core.Physics.Character;
using SpectraEngine.Core.Scene;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace SpectraEngine.Entities;

/// <summary>
/// How deep a capsule sits inside the solid brushes of a set of nodes.
/// </summary>
// Measured in each brush's own frame, against its authored planes and faces:
// the brush's real shape counts, and a node that moves costs no rebuild.
internal sealed class BrushPenetration
{
    private readonly IReadOnlyList<SceneNode> _nodes;
    private readonly Shape?[] _shapes;

    public BrushPenetration(IReadOnlyList<SceneNode> nodes)
    {
        _nodes = nodes;
        _shapes = new Shape?[nodes.Count];
    }

    /// <summary>
    /// The deepest the capsule reaches into any of the brushes, in world
    /// units. Not above zero when it is inside none of them.
    /// </summary>
    public float Deepest(in CharacterCapsule capsule)
    {
        float deepest = float.NegativeInfinity;
        for (int i = 0; i < _nodes.Count; i++)
        {
            SceneNode node = _nodes[i];

            // What the character walks through cannot squeeze it.
            if (node.Brush is not { Operation: BrushOperation.Additive } brush || !node.CanCollide)
                continue;
            if (!Matrix4x4.Invert(node.WorldMatrix, out Matrix4x4 toLocal))
                continue;

            // A brush node is rigid, so the radius holds.
            var local = new CharacterCapsule(
                Vector3.Transform(capsule.Center1, toLocal),
                Vector3.Transform(capsule.Center2, toLocal),
                capsule.Radius);

            if (!BoundsOf(in local).Intersects(brush.LocalBounds))
                continue;

            Shape shape = ShapeOf(i, brush);
            float distance = CapsuleGeometry.Distance(in local, shape.Planes, shape.Faces, out _, out _);
            deepest = MathF.Max(deepest, -distance);
        }

        return deepest;
    }

    private static Aabb BoundsOf(in CharacterCapsule capsule)
    {
        var reach = new Vector3(capsule.Radius);
        return new Aabb(
            Vector3.Min(capsule.Center1, capsule.Center2) - reach,
            Vector3.Max(capsule.Center1, capsule.Center2) + reach);
    }

    // Copied once a brush: the distance test wants spans. A changed brush is
    // a new instance.
    private Shape ShapeOf(int index, Brush brush)
    {
        if (_shapes[index] is { } known && ReferenceEquals(known.Source, brush))
            return known;

        var shape = new Shape(brush);
        _shapes[index] = shape;
        return shape;
    }

    private sealed class Shape(Brush source)
    {
        public Brush Source { get; } = source;

        public Plane[] Planes { get; } = [.. source.LocalPlanes];

        public Polygon[] Faces { get; } = [.. source.LocalFaces];
    }
}
