using System.Numerics;

namespace SpectraEngine.Editing.Gizmos;

/// <summary>
/// One manipulator handle. A plane handle is named for the two axes it spans,
/// so <see cref="PlaneXY"/> moves in x and y.
/// </summary>
// The six axis values must stay contiguous, positives first: IsAxis and the
// positive/negative mapping are range arithmetic.
public enum GizmoHandle
{
    /// <summary>No handle.</summary>
    None = 0,

    /// <summary>The +x arrow or face handle.</summary>
    AxisX,

    /// <summary>The +y arrow or face handle.</summary>
    AxisY,

    /// <summary>The +z arrow or face handle.</summary>
    AxisZ,

    /// <summary>
    /// The -x arrow or face handle. A resize grabbed here moves the -x face
    /// and holds the +x one.
    /// </summary>
    AxisNegX,

    /// <summary>The -y arrow or face handle.</summary>
    AxisNegY,

    /// <summary>The -z arrow or face handle.</summary>
    AxisNegZ,

    /// <summary>The quad spanning y and z.</summary>
    PlaneYZ,

    /// <summary>The quad spanning z and x.</summary>
    PlaneZX,

    /// <summary>The quad spanning x and y.</summary>
    PlaneXY,

    /// <summary>
    /// The centre disc. Drags follow the cursor in the camera-facing plane
    /// through the pivot.
    /// </summary>
    Screen,
}

/// <summary>
/// Queries over <see cref="GizmoHandle"/> values, answered in world axes. A
/// gizmo laid out in a node's own frame asks <see cref="GizmoGeometry"/>.
/// </summary>
public static class GizmoHandles
{
    /// <summary>True for all six axis handles.</summary>
    public static bool IsAxis(GizmoHandle handle) =>
        handle >= GizmoHandle.AxisX && handle <= GizmoHandle.AxisNegZ;

    /// <summary>True for the three handles pointing along a positive frame axis.</summary>
    public static bool IsPositiveAxis(GizmoHandle handle) =>
        handle >= GizmoHandle.AxisX && handle <= GizmoHandle.AxisZ;

    /// <summary>True for the three handles pointing along a negative frame axis.</summary>
    public static bool IsNegativeAxis(GizmoHandle handle) =>
        handle >= GizmoHandle.AxisNegX && handle <= GizmoHandle.AxisNegZ;

    /// <summary>True for the three plane quads.</summary>
    public static bool IsPlane(GizmoHandle handle) =>
        handle is GizmoHandle.PlaneYZ or GizmoHandle.PlaneZX or GizmoHandle.PlaneXY;

    /// <summary>
    /// +1 for a positive axis handle, -1 for a negative one, 0 for anything else.
    /// </summary>
    public static float AxisSign(GizmoHandle handle)
    {
        if (IsPositiveAxis(handle))
            return 1f;

        return IsNegativeAxis(handle) ? -1f : 0f;
    }

    /// <summary>
    /// The positive handle on the same axis. <see cref="GizmoHandle.None"/> for
    /// a non-axis handle.
    /// </summary>
    public static GizmoHandle PositiveAxis(GizmoHandle handle)
    {
        if (IsPositiveAxis(handle))
            return handle;

        if (IsNegativeAxis(handle))
            return handle - (GizmoHandle.AxisNegX - GizmoHandle.AxisX);

        return GizmoHandle.None;
    }

    /// <summary>
    /// The handle on the other end of the same axis, or
    /// <see cref="GizmoHandle.None"/> for a non-axis handle.
    /// </summary>
    public static GizmoHandle Opposite(GizmoHandle handle)
    {
        if (IsPositiveAxis(handle))
            return handle + (GizmoHandle.AxisNegX - GizmoHandle.AxisX);

        if (IsNegativeAxis(handle))
            return handle - (GizmoHandle.AxisNegX - GizmoHandle.AxisX);

        return GizmoHandle.None;
    }

    /// <summary>
    /// The unit world direction an axis handle points in, negatives included.
    /// Zero for any other handle.
    /// </summary>
    public static Vector3 AxisDirection(GizmoHandle handle) => handle switch
    {
        GizmoHandle.AxisX => Vector3.UnitX,
        GizmoHandle.AxisY => Vector3.UnitY,
        GizmoHandle.AxisZ => Vector3.UnitZ,
        GizmoHandle.AxisNegX => -Vector3.UnitX,
        GizmoHandle.AxisNegY => -Vector3.UnitY,
        GizmoHandle.AxisNegZ => -Vector3.UnitZ,
        _ => Vector3.Zero,
    };

    /// <summary>
    /// The unit normal of a plane handle's constraint plane. Zero for any other
    /// handle, the screen handle included: its normal is the camera's.
    /// </summary>
    public static Vector3 PlaneNormal(GizmoHandle handle) => handle switch
    {
        GizmoHandle.PlaneYZ => Vector3.UnitX,
        GizmoHandle.PlaneZX => Vector3.UnitY,
        GizmoHandle.PlaneXY => Vector3.UnitZ,
        _ => Vector3.Zero,
    };

    /// <summary>
    /// A 0/1 mask of the world axes a handle's drag can move along: one for an
    /// arrow (either end), two for a plane quad, all three for the screen handle.
    /// </summary>
    // Snapping rounds only the masked components, so an x drag does not pull
    // y = 0.3 onto the grid. A snapped screen drag rounds all three and can
    // leave the camera plane by up to half a step.
    public static Vector3 FreeAxisMask(GizmoHandle handle) => PositiveAxis(handle) switch
    {
        GizmoHandle.AxisX => Vector3.UnitX,
        GizmoHandle.AxisY => Vector3.UnitY,
        GizmoHandle.AxisZ => Vector3.UnitZ,
        _ => handle switch
        {
            GizmoHandle.PlaneYZ => new Vector3(0f, 1f, 1f),
            GizmoHandle.PlaneZX => new Vector3(1f, 0f, 1f),
            GizmoHandle.PlaneXY => new Vector3(1f, 1f, 0f),
            GizmoHandle.Screen => Vector3.One,
            _ => Vector3.Zero,
        },
    };
}
