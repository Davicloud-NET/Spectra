using SpectraEngine.Core.Bsp;
using SpectraEngine.Core.Scene;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace SpectraEngine.Editing.Gizmos;

/// <summary>
/// The box a selection occupies in the gizmo's frame. Gives a Studio-style
/// gizmo its pivot and the reach of each handle. Render thread only.
/// </summary>
public static class GizmoSelectionBounds
{
    /// <summary>
    /// Measures the box enclosing <paramref name="nodes"/> along three unit axes,
    /// as distances along each axis from the world origin. False for an empty list.
    /// A node with no geometry counts as a point at its origin.
    /// </summary>
    public static bool TryMeasure(
        IReadOnlyList<SceneNode> nodes,
        Vector3 axisX,
        Vector3 axisY,
        Vector3 axisZ,
        out Vector3 min,
        out Vector3 max)
    {
        ArgumentNullException.ThrowIfNull(nodes);

        min = Vector3.Zero;
        max = Vector3.Zero;
        if (nodes.Count == 0)
            return false;

        bool any = false;
        for (int i = 0; i < nodes.Count; i++)
        {
            SceneNode node = nodes[i];
            Vector3 centre;
            Vector3 half;

            if (TryGetLocalBounds(node, out Aabb local))
            {
                Matrix4x4 world = node.WorldMatrix;
                Vector3 localHalf = local.Size * 0.5f;
                Vector3 row0 = new(world.M11, world.M12, world.M13);
                Vector3 row1 = new(world.M21, world.M22, world.M23);
                Vector3 row2 = new(world.M31, world.M32, world.M33);

                centre = Vector3.Transform((local.Min + local.Max) * 0.5f, world);
                half = new Vector3(
                    ExtentAlong(axisX, row0, row1, row2, localHalf),
                    ExtentAlong(axisY, row0, row1, row2, localHalf),
                    ExtentAlong(axisZ, row0, row1, row2, localHalf));
            }
            else
            {
                centre = node.WorldPosition;
                half = Vector3.Zero;
            }

            var frameCentre = new Vector3(
                Vector3.Dot(centre, axisX),
                Vector3.Dot(centre, axisY),
                Vector3.Dot(centre, axisZ));

            Vector3 nodeMin = frameCentre - half;
            Vector3 nodeMax = frameCentre + half;

            if (any)
            {
                min = Vector3.Min(min, nodeMin);
                max = Vector3.Max(max, nodeMax);
            }
            else
            {
                min = nodeMin;
                max = nodeMax;
                any = true;
            }
        }

        return any;
    }

    /// <summary>The world point for a set of frame coordinates. Axes must be orthonormal.</summary>
    public static Vector3 ToWorld(Vector3 frameCoordinates, Vector3 axisX, Vector3 axisY, Vector3 axisZ) =>
        axisX * frameCoordinates.X + axisY * frameCoordinates.Y + axisZ * frameCoordinates.Z;

    /// <summary>
    /// The local box of a node's geometry: brush plane bounds, else mesh bounds.
    /// False for a node with neither.
    /// </summary>
    // ResizeMath.TryMeasure uses this too, so handles and resize agree on
    // which nodes have a shape.
    public static bool TryGetLocalBounds(SceneNode node, out Aabb bounds)
    {
        ArgumentNullException.ThrowIfNull(node);

        if (node.Brush is { } brush)
        {
            bounds = brush.LocalBounds;
            return true;
        }

        if (node.MeshRenderer is { } renderer && renderer.Mesh.HasLocalBounds)
        {
            bounds = renderer.Mesh.LocalBounds;
            return true;
        }

        bounds = default;
        return false;
    }

    // Oriented-box projection: exact half-extent of the transformed box along
    // a unit direction.
    private static float ExtentAlong(
        Vector3 direction, Vector3 row0, Vector3 row1, Vector3 row2, Vector3 localHalf) =>
        MathF.Abs(Vector3.Dot(direction, row0)) * localHalf.X +
        MathF.Abs(Vector3.Dot(direction, row1)) * localHalf.Y +
        MathF.Abs(Vector3.Dot(direction, row2)) * localHalf.Z;
}
