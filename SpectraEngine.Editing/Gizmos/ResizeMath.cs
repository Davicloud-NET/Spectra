using SpectraEngine.Core.Bsp;
using SpectraEngine.Core.Scene;
using System;
using System.Numerics;

namespace SpectraEngine.Editing.Gizmos;

/// <summary>
/// The arithmetic behind a fixed-increment resize: a node's world size, the
/// scale factor for a requested size, and the shift that keeps the opposite
/// face planted.
/// </summary>
public static class ResizeMath
{
    /// <summary>
    /// The smallest world size that counts as measurable. Below it the factor
    /// for a given size change explodes.
    /// </summary>
    public const float MinimumMeasurableSize = 1e-4f;

    /// <summary>
    /// Measures <paramref name="node"/>'s world size along its local axes and
    /// reports the local bounds a resize anchors against. False for a node with
    /// no geometry.
    /// </summary>
    /// <param name="worldSize">
    /// A component can be zero for a flat object. Callers check per axis.
    /// </param>
    /// <param name="localBounds">
    /// The min corner is the planted face for a grow along +axis, the max
    /// corner for a grow along -axis.
    /// </param>
    public static bool TryMeasure(SceneNode node, out Vector3 worldSize, out Aabb localBounds)
    {
        ArgumentNullException.ThrowIfNull(node);

        if (!GizmoSelectionBounds.TryGetLocalBounds(node, out localBounds))
        {
            worldSize = Vector3.Zero;
            return false;
        }

        worldSize = localBounds.Size * WorldScaleOf(node);
        return true;
    }

    /// <summary>The node's world scale: world units per local unit along each axis.</summary>
    // Row lengths, not Matrix4x4.Decompose, which fails on a zero scale.
    public static Vector3 WorldScaleOf(SceneNode node)
    {
        ArgumentNullException.ThrowIfNull(node);

        Matrix4x4 m = node.WorldMatrix;
        return new Vector3(
            new Vector3(m.M11, m.M12, m.M13).Length(),
            new Vector3(m.M21, m.M22, m.M23).Length(),
            new Vector3(m.M31, m.M32, m.M33).Length());
    }

    /// <summary>
    /// The multiplier that turns <paramref name="startWorldSize"/> into
    /// <c>startWorldSize + sizeChange</c>, clamped so the result can neither
    /// collapse nor invert. Sizes are in world units.
    /// </summary>
    // A factor rather than an absolute-extents call on Brush: a brush is a set
    // of half-spaces scaled about its local origin, not a centred box.
    public static float FactorForSizeChange(
        float startWorldSize,
        float sizeChange,
        float minimumSize,
        float minimumFactor,
        float maximumFactor)
    {
        if (startWorldSize <= MinimumMeasurableSize)
            return 1f;

        float wanted = MathF.Max(startWorldSize + sizeChange, minimumSize);
        return Math.Clamp(wanted / startWorldSize, minimumFactor, maximumFactor);
    }

    /// <summary>
    /// How far the node must move along one local axis, in its parent's units,
    /// so the face at <paramref name="localAnchor"/> stays put while the object
    /// is scaled by <paramref name="factor"/> about the node's origin.
    /// </summary>
    /// <param name="localAnchor">
    /// The face opposite the handle, in the node's local frame: the bounds'
    /// minimum for a positive handle, its maximum for a negative one.
    /// </param>
    /// <param name="localScale">Parent units per local unit on that axis.</param>
    public static float AnchorShift(float localAnchor, float localScale, float factor) =>
        localScale * localAnchor * (1f - factor);
}
