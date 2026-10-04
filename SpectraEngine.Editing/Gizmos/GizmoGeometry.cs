using SpectraEngine.Core.Scene;
using System;
using System.Numerics;

namespace SpectraEngine.Editing.Gizmos;

/// <summary>
/// One frame's world-space shape of a manipulator: pivot, handle frame, how far
/// out each handle stands, handle size, and the camera basis. Drawing and
/// hit-testing both read this, so you can only grab what you can see. Rebuilt
/// from the camera every frame.
/// </summary>
// Handle size is a screen-space constant. Where a handle stands is not always:
// a Studio-style gizmo stands handles on the selection's box, and the two ends
// of an axis differ once the pivot is off-centre. Hence two reach vectors.
public readonly struct GizmoGeometry
{
    /// <summary>The default on-screen length of one handle, in pixels.</summary>
    public const float DefaultPixelSize = 96f;

    // Nullable so default(GizmoGeometry) answers style questions instead of throwing.
    private readonly GizmoStyle? _style;

    private GizmoGeometry(
        Vector3 pivot,
        Vector3 axisX,
        Vector3 axisY,
        Vector3 axisZ,
        float handleLength,
        Vector3 positiveReach,
        Vector3 negativeReach,
        float worldPerPixel,
        float viewDepth,
        Vector3 viewRight,
        Vector3 viewUp,
        Vector3 viewNormal,
        GizmoStyle style,
        GizmoMode mode)
    {
        Pivot = pivot;
        AxisX = axisX;
        AxisY = axisY;
        AxisZ = axisZ;
        AxisLength = handleLength;
        PositiveReach = positiveReach;
        NegativeReach = negativeReach;
        WorldPerPixel = worldPerPixel;
        ViewDepth = viewDepth;
        ViewRight = viewRight;
        ViewUp = viewUp;
        ViewNormal = viewNormal;
        _style = style;
        Mode = mode;
    }

    /// <summary>The world-space point the gizmo is centred on.</summary>
    public Vector3 Pivot { get; }

    /// <summary>The frame's first axis: world +x, or the reference node's +x in world space.</summary>
    public Vector3 AxisX { get; }

    /// <summary>The frame's second axis.</summary>
    public Vector3 AxisY { get; }

    /// <summary>The frame's third axis.</summary>
    public Vector3 AxisZ { get; }

    /// <summary>
    /// The world length that covers the handle's pixel size at the pivot's
    /// depth. Every proportion is a multiple of it.
    /// </summary>
    public float AxisLength { get; }

    /// <summary>
    /// How far from the pivot the +x, +y and +z handles stand, in world units.
    /// </summary>
    public Vector3 PositiveReach { get; }

    /// <summary>
    /// How far from the pivot the -x, -y and -z handles stand, in world units.
    /// </summary>
    public Vector3 NegativeReach { get; }

    /// <summary>World units per viewport pixel at the pivot's depth.</summary>
    public float WorldPerPixel { get; }

    /// <summary>
    /// The pivot's depth along the camera's view axis. Negative when the
    /// selection is behind the camera.
    /// </summary>
    public float ViewDepth { get; }

    /// <summary>The camera's right vector.</summary>
    public Vector3 ViewRight { get; }

    /// <summary>The camera's up vector.</summary>
    public Vector3 ViewUp { get; }

    /// <summary>
    /// The camera's forward vector: the normal of the screen handle's plane
    /// and the axis of the view-aligned rotate ring.
    /// </summary>
    public Vector3 ViewNormal { get; }

    /// <summary>The style this geometry was laid out in. Never null.</summary>
    public GizmoStyle Style => _style ?? GizmoStyle.Classic;

    /// <summary>Which tool this geometry was built for.</summary>
    public GizmoMode Mode { get; }

    /// <summary>
    /// True when the pivot is at or behind the camera plane. Callers skip
    /// drawing and picking.
    /// </summary>
    public bool IsBehindCamera => ViewDepth <= 0f;

    /// <summary>The last axis handle this geometry's style and tool offer.</summary>
    public GizmoHandle LastAxisHandle =>
        Mode == GizmoMode.Rotate ? GizmoHandle.AxisZ : Style.LastAxisHandle;

    /// <summary>Whether this geometry's style and tool offer the handle.</summary>
    public bool Offers(GizmoHandle handle) => Style.Offers(handle, Mode);

    /// <summary>How far from the pivot a translate plane quad's near corner sits.</summary>
    public float PlaneOffset => AxisLength * Style.PlaneOffsetFactor;

    /// <summary>The edge length of a translate plane quad.</summary>
    public float PlaneSize => AxisLength * Style.PlaneSizeFactor;

    /// <summary>The radius of the centre disc (translate) or uniform cube (scale).</summary>
    public float ScreenRadius => AxisLength * Style.ScreenRadiusFactor;

    /// <summary>
    /// The nominal radius of a rotate axis ring. <see cref="TryGetRing"/> gives
    /// the radius a ring is drawn and picked at.
    /// </summary>
    public float RingRadius => AxisLength * Style.RingRadiusFactor;

    /// <summary>The radius of the rotate gizmo's view-aligned outer ring.</summary>
    public float ScreenRingRadius => AxisLength * Style.ScreenRingRadiusFactor;

    /// <summary>The half-extent of a scale gizmo's cube handle.</summary>
    public float HandleBoxRadius => AxisLength * Style.HandleBoxRadiusFactor;

    /// <summary>The length of a translate arrowhead.</summary>
    public float HeadLength => AxisLength * Style.HeadLengthFactor;

    /// <summary>The radius of a translate arrowhead's base.</summary>
    public float HeadRadius => AxisLength * Style.HeadRadiusFactor;

    /// <summary>
    /// Builds a translate gizmo's geometry for one frame in the classic style.
    /// </summary>
    /// <param name="frame">
    /// The rotation taking the frame's axes to world space: identity for a
    /// world-aligned gizmo, a node's world rotation for a local one.
    /// </param>
    public static GizmoGeometry Build(
        Camera camera, Vector3 pivot, Quaternion frame, Vector2 viewportSize, float pixelSize) =>
        Build(
            camera, pivot, frame, viewportSize, pixelSize,
            GizmoStyle.Classic, GizmoMode.Translate, Vector3.Zero, Vector3.Zero);

    /// <summary>Builds the gizmo's geometry for one frame.</summary>
    /// <param name="frame">The rotation taking the frame's axes to world space.</param>
    /// <param name="positiveExtent">
    /// Distance from the pivot to the selection box's +x/+y/+z faces along the
    /// frame's axes. Only used when the style stands handles off the bounds.
    /// </param>
    /// <param name="negativeExtent">
    /// Distance from the pivot to the -x/-y/-z faces, as a positive quantity.
    /// </param>
    public static GizmoGeometry Build(
        Camera camera,
        Vector3 pivot,
        Quaternion frame,
        Vector2 viewportSize,
        float pixelSize,
        GizmoStyle style,
        GizmoMode mode,
        Vector3 positiveExtent,
        Vector3 negativeExtent)
    {
        ArgumentNullException.ThrowIfNull(camera);
        ArgumentNullException.ThrowIfNull(style);

        float viewDepth = GizmoMath.ViewDepth(camera, pivot);

        // Floor at the near plane so a pivot behind the camera cannot give a
        // zero or negative scale.
        float scaleDepth = MathF.Max(viewDepth, camera.NearPlane);
        float worldPerPixel = GizmoMath.WorldPerPixel(camera, viewportSize.Y, scaleDepth);
        float handleLength = worldPerPixel * pixelSize;

        Vector3 positiveReach;
        Vector3 negativeReach;
        if (style.HandlesStandOffBounds)
        {
            // The gap is face to the handle's near end, so add the handle's
            // own body or its near half sinks into the surface.
            float gap = worldPerPixel * style.BoundsGapPixels + mode switch
            {
                GizmoMode.Scale => handleLength * style.HandleBoxRadiusFactor,
                GizmoMode.Translate => handleLength * style.ShaftLengthFactor,
                _ => 0f,
            };

            float floor = handleLength * style.MinimumReachFactor;
            positiveReach = Reach(positiveExtent, gap, floor);
            negativeReach = Reach(negativeExtent, gap, floor);
        }
        else
        {
            positiveReach = new Vector3(handleLength);
            negativeReach = positiveReach;
        }

        // Skip the rotations for identity: world-mode axes stay bit-exact.
        bool identity = frame == Quaternion.Identity;
        return new GizmoGeometry(
            pivot,
            identity ? Vector3.UnitX : Vector3.Transform(Vector3.UnitX, frame),
            identity ? Vector3.UnitY : Vector3.Transform(Vector3.UnitY, frame),
            identity ? Vector3.UnitZ : Vector3.Transform(Vector3.UnitZ, frame),
            handleLength,
            positiveReach,
            negativeReach,
            worldPerPixel,
            viewDepth,
            camera.Right,
            camera.Up,
            camera.Forward,
            style,
            mode);
    }

    /// <summary>
    /// The frame direction an axis handle points in, negatives included. Zero
    /// for any other handle.
    /// </summary>
    public Vector3 Axis(GizmoHandle handle) => handle switch
    {
        GizmoHandle.AxisX => AxisX,
        GizmoHandle.AxisY => AxisY,
        GizmoHandle.AxisZ => AxisZ,
        GizmoHandle.AxisNegX => -AxisX,
        GizmoHandle.AxisNegY => -AxisY,
        GizmoHandle.AxisNegZ => -AxisZ,
        _ => Vector3.Zero,
    };

    /// <summary>
    /// How far from the pivot an axis handle stands, in world units. Zero for a
    /// non-axis handle.
    /// </summary>
    public float AxisReach(GizmoHandle handle)
    {
        GizmoHandle positive = GizmoHandles.PositiveAxis(handle);
        if (positive == GizmoHandle.None)
            return 0f;

        return Component(GizmoHandles.IsNegativeAxis(handle) ? NegativeReach : PositiveReach, positive);
    }

    /// <summary>
    /// The unit normal of a plane handle's constraint plane.
    /// <see cref="ViewNormal"/> for <see cref="GizmoHandle.Screen"/>, zero for
    /// any other handle.
    /// </summary>
    public Vector3 PlaneNormal(GizmoHandle handle) => handle switch
    {
        GizmoHandle.PlaneYZ => AxisX,
        GizmoHandle.PlaneZX => AxisY,
        GizmoHandle.PlaneXY => AxisZ,
        GizmoHandle.Screen => ViewNormal,
        _ => Vector3.Zero,
    };

    /// <summary>
    /// The two frame axes a plane handle spans, in the order its name gives
    /// them. Both zero for a non-plane handle.
    /// </summary>
    public void PlaneAxes(GizmoHandle handle, out Vector3 first, out Vector3 second)
    {
        switch (handle)
        {
            case GizmoHandle.PlaneYZ: first = AxisY; second = AxisZ; break;
            case GizmoHandle.PlaneZX: first = AxisZ; second = AxisX; break;
            case GizmoHandle.PlaneXY: first = AxisX; second = AxisY; break;
            default: first = Vector3.Zero; second = Vector3.Zero; break;
        }
    }

    /// <summary>
    /// Two frame axes perpendicular to an axis handle's axis. The same pair
    /// for both ends of an axis. Both zero for a non-axis handle.
    /// </summary>
    public void AxisPerpendiculars(GizmoHandle handle, out Vector3 first, out Vector3 second)
    {
        switch (GizmoHandles.PositiveAxis(handle))
        {
            case GizmoHandle.AxisX: first = AxisY; second = AxisZ; break;
            case GizmoHandle.AxisY: first = AxisZ; second = AxisX; break;
            case GizmoHandle.AxisZ: first = AxisX; second = AxisY; break;
            default: first = Vector3.Zero; second = Vector3.Zero; break;
        }
    }

    /// <summary>
    /// The world-space segment of an axis handle's shaft, ending where the
    /// handle stands. False for a handle that is not offered or has no shaft.
    /// </summary>
    public bool TryGetAxisSegment(GizmoHandle handle, out Vector3 start, out Vector3 end)
    {
        start = Pivot;
        end = Pivot;

        if (!GizmoHandles.IsAxis(handle) || !Offers(handle))
            return false;

        float reach = AxisReach(handle);
        float shaft = MathF.Min(reach, AxisLength * Style.ShaftLengthFactor);
        if (shaft <= 0f)
            return false;

        Vector3 direction = Axis(handle);
        start = Pivot + direction * (reach - shaft);
        end = Pivot + direction * reach;
        return true;
    }

    /// <summary>
    /// The world-space square of a plane handle, offset into the quadrant
    /// between its two arrows. False for a handle that is not an offered plane.
    /// </summary>
    public bool TryGetPlaneQuad(
        GizmoHandle handle, out Vector3 corner, out Vector3 firstAxis, out Vector3 secondAxis, out float size)
    {
        if (!GizmoHandles.IsPlane(handle) || !Offers(handle))
        {
            corner = Pivot;
            firstAxis = Vector3.Zero;
            secondAxis = Vector3.Zero;
            size = 0f;
            return false;
        }

        PlaneAxes(handle, out firstAxis, out secondAxis);
        float offset = PlaneOffset;
        corner = Pivot + firstAxis * offset + secondAxis * offset;
        size = PlaneSize;
        return true;
    }

    /// <summary>
    /// The axis and radius of a rotate handle's ring, centred on the pivot.
    /// <see cref="GizmoHandle.Screen"/> gives the view axis. False for a handle
    /// with no ring or one the style does not offer.
    /// </summary>
    public bool TryGetRing(GizmoHandle handle, out Vector3 axis, out float radius)
    {
        axis = Vector3.Zero;
        radius = 0f;

        if (!Offers(handle))
            return false;

        if (GizmoHandles.IsPositiveAxis(handle))
        {
            axis = Axis(handle);
            radius = AxisRingRadius(handle);
            return true;
        }

        if (handle == GizmoHandle.Screen)
        {
            axis = ViewNormal;
            radius = ScreenRingRadius;
            return true;
        }

        return false;
    }

    /// <summary>
    /// The centre and half-extent of a scale handle's cube. The pivot for the
    /// uniform <see cref="GizmoHandle.Screen"/> handle. False for a handle with
    /// no cube or one the style does not offer.
    /// </summary>
    public bool TryGetHandleBox(GizmoHandle handle, out Vector3 centre, out float radius)
    {
        centre = Pivot;
        radius = 0f;

        if (!Offers(handle))
            return false;

        if (GizmoHandles.IsAxis(handle))
        {
            centre = Pivot + Axis(handle) * AxisReach(handle);
            radius = HandleBoxRadius;
            return true;
        }

        if (handle == GizmoHandle.Screen)
        {
            radius = ScreenRadius;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Converts a world-space length at the pivot's depth into viewport pixels.
    /// Infinity for a degenerate viewport, so nothing tests as within tolerance.
    /// </summary>
    public float WorldToPixels(float worldLength) =>
        WorldPerPixel > 0f ? worldLength / WorldPerPixel : float.PositiveInfinity;

    // Off-bounds styles: largest reach in the ring's plane, so the ring
    // goes around the selection instead of through it.
    private float AxisRingRadius(GizmoHandle handle)
    {
        if (!Style.HandlesStandOffBounds)
            return RingRadius;

        PerpendicularHandles(handle, out GizmoHandle first, out GizmoHandle second);
        float radius = MathF.Max(
            MathF.Max(AxisReach(first), AxisReach(GizmoHandles.Opposite(first))),
            MathF.Max(AxisReach(second), AxisReach(GizmoHandles.Opposite(second))));

        return MathF.Max(radius, AxisLength * Style.MinimumReachFactor);
    }

    private static void PerpendicularHandles(GizmoHandle handle, out GizmoHandle first, out GizmoHandle second)
    {
        switch (GizmoHandles.PositiveAxis(handle))
        {
            case GizmoHandle.AxisX: first = GizmoHandle.AxisY; second = GizmoHandle.AxisZ; break;
            case GizmoHandle.AxisY: first = GizmoHandle.AxisZ; second = GizmoHandle.AxisX; break;
            default: first = GizmoHandle.AxisX; second = GizmoHandle.AxisY; break;
        }
    }

    private static Vector3 Reach(Vector3 extent, float gap, float floor) => new(
        MathF.Max(MathF.Max(extent.X, 0f) + gap, floor),
        MathF.Max(MathF.Max(extent.Y, 0f) + gap, floor),
        MathF.Max(MathF.Max(extent.Z, 0f) + gap, floor));

    private static float Component(Vector3 value, GizmoHandle positiveAxis) => positiveAxis switch
    {
        GizmoHandle.AxisX => value.X,
        GizmoHandle.AxisY => value.Y,
        _ => value.Z,
    };
}
