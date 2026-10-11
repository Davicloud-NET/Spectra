using System.Numerics;

namespace SpectraEngine.Core.Scene;

/// <summary>A node's <see cref="CFrame"/> in the world, read from the graph and solved back into it.</summary>
internal static class WorldFrame
{
    /// <summary>
    /// The node's world position with the rotations of the node and its
    /// ancestors composed. Scale moves the position and is left out of the
    /// rotation.
    /// </summary>
    public static CFrame Of(SceneNode node) => new(node.WorldPosition, RotationOf(node));

    /// <summary>The local transform that puts the node at a world frame. The scale is kept.</summary>
    public static Transform LocalFor(SceneNode node, CFrame world)
    {
        Transform local = node.LocalTransform;
        if (node.Parent is not { } parent)
        {
            local.Position = world.Position;
            local.Rotation = world.Rotation;
            return local;
        }

        CFrame solved = Of(parent).ToObjectSpace(world);
        local.Rotation = solved.Rotation;

        // A turn in place asks for the position the node has. Solved again it
        // would come back a rounding error away, and a spinning node would creep.
        if (world.Position != node.WorldPosition)
            local.Position = PositionUnder(parent, world.Position) ?? solved.Position;
        return local;
    }

    // By the parent's whole matrix, so the position lands under a scaled
    // ancestor too. The parent's own position comes off first: far from the
    // origin the inverse of the whole matrix loses the low digits of the
    // answer. Null under an ancestor scaled to nothing, which has no inverse.
    private static Vector3? PositionUnder(SceneNode parent, Vector3 worldPosition)
    {
        Matrix4x4 axes = parent.WorldMatrix;
        Vector3 offset = worldPosition - axes.Translation;
        axes.Translation = Vector3.Zero;
        return Matrix4x4.Invert(axes, out Matrix4x4 back) ? Vector3.TransformNormal(offset, back) : null;
    }

    // The parent's rotation goes on the left: the engine turns a point by the
    // child first, world = local * parent with row vectors. The CFrame
    // constructor brings the chain back to unit length.
    private static Quaternion RotationOf(SceneNode node)
    {
        Quaternion rotation = node.LocalRotation;
        for (SceneNode? ancestor = node.Parent; ancestor is not null; ancestor = ancestor.Parent)
            rotation = ancestor.LocalRotation * rotation;
        return rotation;
    }
}
