using SpectraEngine.Core.Bsp;
using SpectraEngine.Core.Physics.Character;
using SpectraEngine.Core.Scene;
using System;
using System.Numerics;

namespace SpectraEngine.Core.Entities;

// How far a capsule is from what a node can be touched by: a part's brush as
// its real shape, or the box around a mesh, which has no shape of its own.
internal static class TouchMeasure
{
    private const int BoxSearchSteps = 32;

    // The golden section, which keeps one of the two samples at each step.
    private const float Keep = 0.618034f;

    // False for a node with nothing to touch, or one squashed flat. The
    // distance is from the capsule's surface, and below zero inside.
    public static bool TryDistance(SceneNode node, in CharacterCapsule capsule, out float distance)
    {
        distance = 0f;
        Matrix4x4 world = node.WorldMatrix;
        if (!Matrix4x4.Invert(world, out Matrix4x4 toLocal))
            return false;

        Vector3 low = Vector3.Transform(capsule.Center1, toLocal);
        Vector3 high = Vector3.Transform(capsule.Center2, toLocal);

        if (node.Brush is { } brush)
        {
            // A brush node's transform is rigid, so a distance measured in
            // the brush's frame is a world distance.
            var local = new CharacterCapsule(low, high, capsule.Radius);
            distance = CapsuleGeometry.Distance(in local, brush.LocalPlaneSpan, brush.LocalFaceSpan, out _, out _);
            return true;
        }

        if (node.MeshRenderer is not { } renderer)
            return false;

        // A mesh may be scaled, and by another amount along each axis. With
        // each axis stretched to its length in the world, the box and the
        // capsule are as far apart as in the world. A sheared node is not.
        var scale = new Vector3(
            new Vector3(world.M11, world.M12, world.M13).Length(),
            new Vector3(world.M21, world.M22, world.M23).Length(),
            new Vector3(world.M31, world.M32, world.M33).Length());
        Aabb box = SceneBvh.MeshLocalBounds(renderer.Mesh);
        var stretched = new Aabb(box.Min * scale, box.Max * scale);
        distance = SegmentToBox(low * scale, high * scale, in stretched) - capsule.Radius;
        return true;
    }

    // The least distance from a segment to a box. The distance from a point
    // to a box is convex along a line, so a search that narrows on the lower
    // of two samples ends at the least one.
    private static float SegmentToBox(Vector3 a, Vector3 b, in Aabb box)
    {
        float from = 0f;
        float to = 1f;
        float near = to - (Keep * (to - from));
        float far = from + (Keep * (to - from));
        float atNear = GapSquared(a, b, in box, near);
        float atFar = GapSquared(a, b, in box, far);

        for (int step = 0; step < BoxSearchSteps; step++)
        {
            if (atNear <= atFar)
            {
                to = far;
                far = near;
                atFar = atNear;
                near = to - (Keep * (to - from));
                atNear = GapSquared(a, b, in box, near);
            }
            else
            {
                from = near;
                near = far;
                atNear = atFar;
                far = from + (Keep * (to - from));
                atFar = GapSquared(a, b, in box, far);
            }
        }

        return MathF.Sqrt(MathF.Min(atNear, atFar));
    }

    private static float GapSquared(Vector3 a, Vector3 b, in Aabb box, float along)
    {
        Vector3 point = Vector3.Lerp(a, b, along);
        return Vector3.DistanceSquared(point, box.ClosestPoint(point));
    }
}
