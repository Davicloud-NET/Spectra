namespace SpectraEngine.Editing.Gizmos;

/// <summary>Which built-in manipulator style a <see cref="GizmoStyle"/> is.</summary>
public enum GizmoStyleKind
{
    /// <summary>Roblox Studio's manipulators. See <see cref="GizmoStyle.Studio"/>.</summary>
    Studio,

    /// <summary>The Blender/Unity/Maya layout. See <see cref="GizmoStyle.Classic"/>.</summary>
    Classic,

    /// <summary>Anything a host built itself.</summary>
    Custom,
}

/// <summary>Where a gizmo sits for a multi-selection or off-centre geometry.</summary>
public enum GizmoPivotMode
{
    /// <summary>
    /// The average of the selected nodes' world positions. Blender's median
    /// point, and the only mode that works for a selection with no geometry.
    /// </summary>
    OriginAverage,

    /// <summary>
    /// The centre of the box enclosing the selection, in the gizmo's frame.
    /// Roblox Studio's.
    /// </summary>
    BoundsCentre,
}

/// <summary>
/// What differs between manipulator styles: which handles exist, where they
/// stand, what a resize holds still, and the gizmo's proportions. Immutable.
/// </summary>
// Data, not a strategy object: each tool keeps one Pick and one Draw reading
// the same GizmoGeometry, so drawn and pickable cannot differ per style.
public sealed class GizmoStyle
{
    /// <summary>
    /// Roblox Studio's manipulators, the default: six handles standing on the
    /// selection's box, resizes that hold the opposite face, pivot at the box centre.
    /// </summary>
    // Face anchoring needs all six handles. A face-anchored resize only moves
    // the grabbed face, so three handles would leave three faces unreachable.
    public static GizmoStyle Studio { get; } = new()
    {
        Kind = GizmoStyleKind.Studio,
        Name = "Studio",
        NegativeAxisHandles = true,
        PlaneHandles = false,
        CentreDiscHandle = false,
        ViewRing = false,
        HandlesStandOffBounds = true,
        FaceAnchoredResize = true,
        PivotMode = GizmoPivotMode.BoundsCentre,
        ShaftLengthFactor = 0.34f,
        HandleBoxRadiusFactor = 0.1f,
        ScreenRadiusFactor = 0.12f,
        BoundsGapPixels = 10f,
        MinimumReachFactor = 0.42f,
    };

    /// <summary>
    /// The Blender/Unity/Maya layout: three arrows on the positive ends, plane
    /// quads, a centre disc, four rotate rings, and resizes about the pivot.
    /// </summary>
    // Three handles are enough because the resize is symmetric.
    public static GizmoStyle Classic { get; } = new()
    {
        Kind = GizmoStyleKind.Classic,
        Name = "Classic",
        NegativeAxisHandles = false,
        PlaneHandles = true,
        CentreDiscHandle = true,
        ViewRing = true,
        HandlesStandOffBounds = false,
        FaceAnchoredResize = false,
        PivotMode = GizmoPivotMode.OriginAverage,
        ShaftLengthFactor = 1f,
        HandleBoxRadiusFactor = 0.075f,
        ScreenRadiusFactor = 0.14f,
        BoundsGapPixels = 0f,
        MinimumReachFactor = 1f,
    };

    /// <summary>Which of the built-in styles this is, or <see cref="GizmoStyleKind.Custom"/>.</summary>
    public GizmoStyleKind Kind { get; init; } = GizmoStyleKind.Custom;

    /// <summary>A short display name.</summary>
    public string Name { get; init; } = "Custom";

    /// <summary>
    /// Whether the three negative-direction axis handles exist. Should be set
    /// together with <see cref="FaceAnchoredResize"/>.
    /// </summary>
    public bool NegativeAxisHandles { get; init; }

    /// <summary>Whether the move tool offers the three two-axis plane quads.</summary>
    public bool PlaneHandles { get; init; }

    /// <summary>
    /// Whether the move tool draws a grabbable centre disc. Free move
    /// (<see cref="GizmoTool.FreeMoveHandle"/>) works either way.
    /// </summary>
    public bool CentreDiscHandle { get; init; }

    /// <summary>Whether the rotate tool offers the view-aligned ring outside the three axis rings.</summary>
    public bool ViewRing { get; init; }

    /// <summary>
    /// Whether handles stand at the faces of the selection's bounding box rather
    /// than at a fixed distance from the pivot. Handle size on screen is the same either way.
    /// </summary>
    public bool HandlesStandOffBounds { get; init; }

    /// <summary>
    /// Whether a resize holds the face opposite the grabbed handle still, or
    /// scales about the pivot. <see cref="ScaleGizmo.SymmetricModifier"/> flips
    /// it for one gesture.
    /// </summary>
    public bool FaceAnchoredResize { get; init; }

    /// <summary>Where the gizmo sits for a multi-selection and for off-centre geometry.</summary>
    public GizmoPivotMode PivotMode { get; init; } = GizmoPivotMode.OriginAverage;

    /// <summary>
    /// How much of an axis handle is shaft, in units of
    /// <see cref="GizmoGeometry.AxisLength"/>, measured back from where the
    /// handle stands.
    /// </summary>
    // Below 1 when handles stand on the bounds: a full shaft would start inside
    // the object and draws depth-off.
    public float ShaftLengthFactor { get; init; } = 1f;

    /// <summary>The half-extent of a resize handle's cube, in units of <see cref="GizmoGeometry.AxisLength"/>.</summary>
    public float HandleBoxRadiusFactor { get; init; } = 0.075f;

    /// <summary>The radius of the screen-facing centre disc and the uniform resize cube.</summary>
    public float ScreenRadiusFactor { get; init; } = 0.14f;

    /// <summary>How far from the pivot a plane quad's near corner sits.</summary>
    public float PlaneOffsetFactor { get; init; } = 0.32f;

    /// <summary>The edge length of a plane quad.</summary>
    public float PlaneSizeFactor { get; init; } = 0.26f;

    /// <summary>The radius of one rotate axis ring, when rings are not sized to the bounds.</summary>
    public float RingRadiusFactor { get; init; } = 0.86f;

    /// <summary>The radius of the view-aligned rotate ring.</summary>
    public float ScreenRingRadiusFactor { get; init; } = 1.1f;

    /// <summary>The length of an arrowhead, in units of <see cref="GizmoGeometry.AxisLength"/>.</summary>
    public float HeadLengthFactor { get; init; } = 0.22f;

    /// <summary>The radius of an arrowhead's base.</summary>
    public float HeadRadiusFactor { get; init; } = 0.07f;

    /// <summary>
    /// The visible gap in pixels between the selection's bounding box and the
    /// near end of a handle. Ignored unless <see cref="HandlesStandOffBounds"/>.
    /// </summary>
    public float BoundsGapPixels { get; init; }

    /// <summary>
    /// The least distance a handle may stand out, in units of
    /// <see cref="GizmoGeometry.AxisLength"/>, so a tiny or flat object still
    /// gets handles far enough apart to aim at.
    /// </summary>
    public float MinimumReachFactor { get; init; } = 1f;

    /// <summary>
    /// The last axis handle in this style's roster: <see cref="GizmoHandle.AxisZ"/>
    /// for three handles, <see cref="GizmoHandle.AxisNegZ"/> for six. Axis values
    /// are contiguous, so a roster walk runs from AxisX to this.
    /// </summary>
    public GizmoHandle LastAxisHandle =>
        NegativeAxisHandles ? GizmoHandle.AxisNegZ : GizmoHandle.AxisZ;

    /// <summary>
    /// Whether this style offers <paramref name="handle"/> for the given tool.
    /// Hit testing and drawing both ask here.
    /// </summary>
    public bool Offers(GizmoHandle handle, GizmoMode mode)
    {
        if (GizmoHandles.IsNegativeAxis(handle))
        {
            // A ring about -x is the same ring as +x, so rotate stops at three.
            return NegativeAxisHandles && mode != GizmoMode.Rotate;
        }

        if (GizmoHandles.IsPositiveAxis(handle))
            return true;

        if (GizmoHandles.IsPlane(handle))
            return PlaneHandles && mode == GizmoMode.Translate;

        return handle == GizmoHandle.Screen && mode switch
        {
            // Both styles keep the uniform resize cube. Studio has none, but
            // it is the only uniform resize in the editor.
            GizmoMode.Scale => true,
            GizmoMode.Rotate => ViewRing,
            _ => CentreDiscHandle,
        };
    }
}
