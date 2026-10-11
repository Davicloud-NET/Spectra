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

        // The parent's whole matrix, so the position lands under a scaled
        // ancestor too. An ancestor scaled to nothing has no inverse, and the
        // position is then solved as if nothing were scaled.
        local.Position = Matrix4x4.Invert(parent.WorldMatrix, out Matrix4x4 toParent)
            ? Vector3.Transform(world.Position, toParent)
            : solved.Position;
        return local;
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
