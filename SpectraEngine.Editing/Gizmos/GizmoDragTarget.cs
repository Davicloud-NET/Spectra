using SpectraEngine.Core.Scene;
using System.Numerics;

namespace SpectraEngine.Editing.Gizmos;

/// <summary>
/// One node a drag is manipulating, captured at the grab. Each drag frame
/// computes its result from this, not from the node's current state, so
/// rounding and snapping leave no drift.
/// </summary>
/// <param name="Node">The node being manipulated. Held only for the gesture.</param>
/// <param name="StartLocal">Its local transform at the grab.</param>
/// <param name="StartWorldPosition">Its world position at the grab.</param>
/// <param name="StartWorldRotation">Its world rotation at the grab.</param>
/// <param name="ParentWorldInverse">
/// Turns a world position into a local one. Identity for a root node or a
/// parent whose world matrix cannot be inverted.
/// </param>
/// <param name="ParentWorldRotationInverse">Turns a world rotation into a local one.</param>
public readonly record struct GizmoDragTarget(
    SceneNode Node,
    Transform StartLocal,
    Vector3 StartWorldPosition,
    Quaternion StartWorldRotation,
    Matrix4x4 ParentWorldInverse,
    Quaternion ParentWorldRotationInverse)
{
    /// <summary>Captures a node's starting state.</summary>
    public static GizmoDragTarget Capture(SceneNode node)
    {
        Matrix4x4 parentWorldInverse = Matrix4x4.Identity;
        Quaternion parentRotationInverse = Quaternion.Identity;

        if (node.Parent is { } parent)
        {
            Matrix4x4 parentWorld = parent.WorldMatrix;
            if (!Matrix4x4.Invert(parentWorld, out parentWorldInverse))
                parentWorldInverse = Matrix4x4.Identity; // degenerate parent scale; treat as world space

            // Decompose the world matrix; chaining LocalRotations is wrong
            // under a non-uniformly scaled ancestor.
            if (Matrix4x4.Decompose(parentWorld, out _, out Quaternion parentRotation, out _))
                parentRotationInverse = Quaternion.Conjugate(parentRotation);
        }

        Matrix4x4 world = node.WorldMatrix;
        Quaternion worldRotation = Matrix4x4.Decompose(world, out _, out Quaternion rotation, out _)
            ? rotation
            : Quaternion.Identity;

        return new GizmoDragTarget(
            node,
            node.LocalTransform,
            world.Translation,
            worldRotation,
            parentWorldInverse,
            parentRotationInverse);
    }
}
