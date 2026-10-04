using Microsoft.Extensions.Logging;
using SpectraEngine.Core.Bsp;
using SpectraEngine.Core.Graphics;
using SpectraEngine.Core.Input;
using SpectraEngine.Core.Scene;
using SpectraEngine.Editing.Commands;
using SpectraEngine.Editing.Input;
using SpectraEngine.Editing.Undo;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace SpectraEngine.Editing.Gizmos;

/// <summary>
/// The resize tool: a cube-capped handle per axis direction the style offers,
/// plus a uniform centre cube. A drag changes the object's size in world units.
/// Local frame only.
/// </summary>
// Never write node scale on a brush node or above one. A non-rigid brush
// placement makes the static-world compile reject the whole snapshot, scene-wide.
// - Brush node: rebuild via Brush.WithScaledExtents. The transform gets
//   position only.
// - No brush in the subtree: write LocalScale.
// - Brush descendants but no brush of its own: declined.
//
// Travel along the constraint is the size change, 1:1. Each node's factor is
// derived from its own measured size, so one notch is one increment on every
// object. A node with no measurable size drags proportionally instead and is
// counted in ProportionalFallbackCount.
//
// Local only: world-axis factors on a rotated object are a shear.
public sealed class ScaleGizmo : GizmoTool
{
    /// <summary>
    /// The smallest world size a resize will produce along an axis.
    /// <see cref="Brush.WithScaledExtents"/> rejects zero, and negative would mirror.
    /// </summary>
    public const float MinimumSize = 0.01f;

    /// <summary>The smallest factor a drag will apply to a node.</summary>
    public const float MinimumFactor = 0.01f;

    /// <summary>The largest factor a single drag will apply.</summary>
    public const float MaximumFactor = 1000f;

    /// <summary>
    /// The factor snap step for the proportional fallback: targets with no
    /// measurable world size.
    /// </summary>
    public const float ProportionalFactorIncrement = 0.25f;

    // Per-node state for one gesture. Transform is set for every accepted node
    // (position only on a brush node). Brush is set for brush nodes. Both null
    // for a declined node, whose slot keeps the list index-aligned with Targets.
    // LocalAnchor is what a face-anchored resize holds, LocalCentre what a
    // symmetric one holds.
    private readonly record struct ResizeTarget(
        SetLocalTransformCommand? Transform,
        SetBrushCommand? Brush,
        Brush? StartBrush,
        Vector3 StartSize,
        Vector3 LocalAnchor,
        Vector3 LocalCentre);

    private readonly List<ResizeTarget> _resizeTargets = [];

    private Vector3 _constraintAxis;
    private Vector3 _constraintNormal;
    private Vector3 _uniformDirection;
    private Vector3 _grabPoint;
    private Vector3 _axisMask;
    private float _grabOffset;
    private float _grabAxisLength;

    // What the last Apply wrote, so a frame asking for the same thing skips
    // the pass. A brush rebuild is expensive.
    private float _appliedSizeChange;
    private float _appliedProportional = 1f;
    private bool _appliedSymmetric;
    private bool _appliedEdit;

    /// <summary>Creates a resize tool over a scene and the history its edits land in.</summary>
    public ScaleGizmo(Scene scene, UndoStack undo)
        : base(scene, undo, "Resize")
    {
    }

    /// <inheritdoc/>
    public override GizmoMode Mode => GizmoMode.Scale;

    /// <summary>Always false: a resize only makes sense in the object's own axes.</summary>
    public override bool SupportsOrientation => false;

    /// <summary>Size snapping for drags.</summary>
    public ResizeSnapSettings Snap { get; } = new();

    /// <summary>
    /// The modifier that flips the style's anchoring for one gesture: symmetric
    /// where the style is face-anchored, face-anchored where it is symmetric.
    /// <see cref="KeyModifiers.None"/> removes the option.
    /// </summary>
    public KeyModifiers SymmetricModifier { get; set; } = KeyModifiers.Shift;

    /// <summary>
    /// Optional sink for the warning that a target had no measurable size and is
    /// being resized proportionally.
    /// </summary>
    public ILogger? Logger { get; set; }

    /// <summary>
    /// How many of the last gesture's targets fell back to a proportional factor
    /// because they have no measurable world size along the dragged axes.
    /// </summary>
    public int ProportionalFallbackCount { get; private set; }

    /// <summary>
    /// The world-unit size change the current drag asks for: of the dragged axis,
    /// or of the largest dimension for the uniform handle. Zero when idle.
    /// </summary>
    public float DragSizeChange => _appliedSizeChange;

    /// <inheritdoc/>
    protected override bool HasEdit => _appliedEdit;

    /// <summary>
    /// Always the reference node's world rotation, whatever
    /// <see cref="GizmoTool.Orientation"/> says.
    /// </summary>
    protected override Quaternion FrameRotation() =>
        ReferenceNode is { } node ? WorldRotationOf(node) : Quaternion.Identity;

    /// <inheritdoc/>
    protected override GizmoPick HitTest(in GizmoGeometry geometry, in Ray3 ray, float tolerancePixels) =>
        ScaleGizmoHitTester.Pick(in geometry, in ray, tolerancePixels);

    /// <inheritdoc/>
    protected override bool TryPrepareDrag(in EditorInputFrame frame, in Ray3 ray)
    {
        // Refuse here, before a transaction opens, when nothing can be resized.
        if (!AnyResizableTarget())
            return false;

        GizmoGeometry geometry = Geometry;

        // The proportional fallback divides by this.
        if (geometry.AxisLength <= 0f)
            return false;

        _constraintAxis = geometry.Axis(ActiveHandle);
        _constraintNormal = geometry.ViewNormal;
        _grabAxisLength = geometry.AxisLength;
        _axisMask = AxisMaskOf(ActiveHandle);

        // Up and to the right grows. Frozen at the grab.
        _uniformDirection = Vector3.Normalize(geometry.ViewRight + geometry.ViewUp);

        if (GizmoHandles.IsAxis(ActiveHandle))
        {
            if (!TryProjectOntoAxis(in ray, out float offset))
                return false;

            _grabOffset = offset;
        }
        else
        {
            if (!TryProjectOntoViewPlane(in ray, out Vector3 grabPoint))
                return false;

            _grabPoint = grabPoint;
        }

        _appliedSizeChange = 0f;
        _appliedProportional = 1f;
        _appliedSymmetric = false;
        _appliedEdit = false;
        return true;
    }

    /// <inheritdoc/>
    protected override void RecordCommands()
    {
        _resizeTargets.Clear();
        ProportionalFallbackCount = 0;

        IReadOnlyList<GizmoDragTarget> targets = Targets;
        for (int i = 0; i < targets.Count; i++)
        {
            SceneNode node = targets[i].Node;

            if (node.Brush is null && node.SubtreeBrushCount > 0)
            {
                // Declined: LocalScale here would make every brush below
                // non-rigid. Hold the slot to stay index-aligned.
                _resizeTargets.Add(default);
                continue;
            }

            if (!ResizeMath.TryMeasure(node, out Vector3 size, out Aabb bounds) || !IsMeasurable(size))
            {
                size = Vector3.Zero;
                bounds = default;
                ProportionalFallbackCount++;
                Logger?.LogWarning(
                    "Resize: node '{Node}' has no measurable size along the dragged axes; " +
                    "falling back to a proportional ×{Increment} factor step instead of the " +
                    "{SizeIncrement}-unit resize increment",
                    node.Name, ProportionalFactorIncrement, Snap.Increment);
            }

            // Mesh: scale and anchoring shift. Brush: the shift only.
            var transform = new SetLocalTransformCommand(node.Id, targets[i].StartLocal, targets[i].StartLocal)
            {
                Name = TransactionName,
            };
            Undo.Record(transform);

            SetBrushCommand? brushCommand = null;
            if (node.Brush is { } brush)
            {
                brushCommand = new SetBrushCommand(node.Id, brush, brush) { Name = TransactionName };
                Undo.Record(brushCommand);
            }

            _resizeTargets.Add(new ResizeTarget(
                transform, brushCommand, node.Brush, size,
                ResolveAnchor(node, in bounds), (bounds.Min + bounds.Max) * 0.5f));
        }
    }

    /// <inheritdoc/>
    protected override void ApplyDrag(in EditorInputFrame frame, in Ray3 ray)
    {
        // No projection this frame: hold the last size.
        if (!TryComputeTravel(in ray, out float travel))
            return;

        // The uniform handle is excluded: its travel is already the whole
        // size change, and doubling would make one notch two.
        bool inverted = SymmetricModifier != KeyModifiers.None &&
            (frame.Modifiers & SymmetricModifier) == SymmetricModifier;
        bool symmetric = ActiveHandle != GizmoHandle.Screen &&
            (Style.FaceAnchoredResize ? inverted : !inverted);

        // Symmetric moves both faces, so travel is half the size change.
        // Double before the snap so the snapped quantity is the size.
        float requested = symmetric ? travel * 2f : travel;

        bool snapping = Snap.IsActiveWith(frame.Modifiers);
        float sizeChange = snapping ? Snap.SnapScalar(requested) : requested;

        // Fallback for targets with no measurable size: one gizmo length of
        // travel doubles them. Only computed when a target reads it. It moves
        // with raw travel, so left unconditional it defeats Apply's skip and
        // rebuilds brushes several times per notch for identical geometry.
        float proportional = 1f;
        if (ProportionalFallbackCount > 0)
        {
            proportional = 1f + travel / _grabAxisLength;
            if (snapping)
            {
                proportional = MathF.Round(
                    proportional / ProportionalFactorIncrement, MidpointRounding.AwayFromZero) *
                    ProportionalFactorIncrement;
            }
            proportional = Math.Clamp(proportional, MinimumFactor, MaximumFactor);
        }

        Apply(sizeChange, proportional, symmetric);
    }

    /// <inheritdoc/>
    protected override void ClearDragState()
    {
        _resizeTargets.Clear();
        _constraintAxis = Vector3.Zero;
        _constraintNormal = Vector3.Zero;
        _uniformDirection = Vector3.Zero;
        _grabPoint = Vector3.Zero;
        _axisMask = Vector3.Zero;
        _grabOffset = 0f;
        _grabAxisLength = 0f;
        _appliedSizeChange = 0f;
        _appliedProportional = 1f;
        _appliedSymmetric = false;
        _appliedEdit = false;
    }

    /// <inheritdoc/>
    protected override void DrawHandles(DebugDraw output, in GizmoGeometry geometry, GizmoHandle highlighted) =>
        ScaleGizmoRenderer.Draw(output, in geometry, highlighted);

    // Cursor travel along the constraint since the grab, in world units. An
    // axis handle measures along its own direction, so pulling a -x handle
    // outward is positive travel. The uniform handle uses the frozen diagonal.
    private bool TryComputeTravel(in Ray3 ray, out float travel)
    {
        if (GizmoHandles.IsAxis(ActiveHandle))
        {
            if (!TryProjectOntoAxis(in ray, out float offset))
            {
                travel = 0f;
                return false;
            }

            travel = offset - _grabOffset;
            return true;
        }

        if (!TryProjectOntoViewPlane(in ray, out Vector3 point))
        {
            travel = 0f;
            return false;
        }

        travel = Vector3.Dot(point - _grabPoint, _uniformDirection);
        return true;
    }

    private bool TryProjectOntoAxis(in Ray3 ray, out float offset)
    {
        if (!GizmoMath.TryClosestPointOnLine(in ray, GrabPivot, _constraintAxis, out Vector3 onAxis))
        {
            offset = 0f;
            return false;
        }

        offset = Vector3.Dot(onAxis - GrabPivot, _constraintAxis);
        return true;
    }

    private bool TryProjectOntoViewPlane(in Ray3 ray, out Vector3 point)
    {
        if (!GizmoMath.TryRayPlane(in ray, GrabPivot, _constraintNormal, out float distance))
        {
            point = Vector3.Zero;
            return false;
        }

        point = ray.PointAt(distance);
        return true;
    }

    private void Apply(float sizeChange, float proportionalFactor, bool symmetric)
    {
        // A snapped drag mostly asks for the same size. Skip the brush rebuild
        // and the dirtying that comes with it.
        if (sizeChange == _appliedSizeChange &&
            proportionalFactor == _appliedProportional &&
            symmetric == _appliedSymmetric)
        {
            return;
        }

        _appliedSizeChange = sizeChange;
        _appliedProportional = proportionalFactor;
        _appliedSymmetric = symmetric;

        bool edited = false;
        IReadOnlyList<GizmoDragTarget> targets = Targets;
        for (int i = 0; i < targets.Count; i++)
        {
            ResizeTarget target = _resizeTargets[i];
            if (target.Transform is null)
                continue; // declined in RecordCommands

            Transform start = targets[i].StartLocal;
            Vector3 factor = SolveFactor(in target, sizeChange, proportionalFactor);

            // The uniform handle is always symmetric.
            Vector3 shift = SolveAnchorShift(
                in target, start.Scale, factor, symmetric || ActiveHandle == GizmoHandle.Screen);

            if (target.Brush is { } brushCommand)
            {
                if (!TryScaleExtents(target.StartBrush!, factor, out Brush? resized))
                    continue; // degenerate result, hold the last good extents

                brushCommand.SetAfter(resized);
                brushCommand.Do(Scene);
            }
            else
            {
                // Componentwise in the node's own axes, so no shear.
                start.Scale *= factor;
            }

            // The shift is along the node's local axes. Rotate it into the parent's frame.
            if (shift != Vector3.Zero)
                start.Position += Vector3.Transform(shift, start.Rotation);

            target.Transform.SetAfter(start);
            target.Transform.Do(Scene);

            edited |= factor != Vector3.One || shift != Vector3.Zero;
        }

        _appliedEdit = edited;
    }

    private Vector3 SolveFactor(in ResizeTarget target, float sizeChange, float proportionalFactor)
    {
        if (ActiveHandle == GizmoHandle.Screen)
        {
            // Uniform: the largest dimension grows by the increment, the rest follow.
            float reference = MathF.Max(target.StartSize.X, MathF.Max(target.StartSize.Y, target.StartSize.Z));
            float uniform = reference > ResizeMath.MinimumMeasurableSize
                ? ResizeMath.FactorForSizeChange(reference, sizeChange, MinimumSize, MinimumFactor, MaximumFactor)
                : proportionalFactor;
            return new Vector3(uniform);
        }

        return new Vector3(
            AxisFactor(_axisMask.X, target.StartSize.X, sizeChange, proportionalFactor),
            AxisFactor(_axisMask.Y, target.StartSize.Y, sizeChange, proportionalFactor),
            AxisFactor(_axisMask.Z, target.StartSize.Z, sizeChange, proportionalFactor));
    }

    private static float AxisFactor(float mask, float startSize, float sizeChange, float proportionalFactor)
    {
        if (mask == 0f)
            return 1f;

        return startSize > ResizeMath.MinimumMeasurableSize
            ? ResizeMath.FactorForSizeChange(startSize, sizeChange, MinimumSize, MinimumFactor, MaximumFactor)
            : proportionalFactor;
    }

    private static Vector3 SolveAnchorShift(
        in ResizeTarget target, Vector3 localScale, Vector3 factor, bool symmetric)
    {
        // Symmetric holds the bounds centre, not the origin. For off-centre
        // geometry the origin would move the two faces by different amounts
        // and the handle would drift from the cursor.
        Vector3 anchor = symmetric ? target.LocalCentre : target.LocalAnchor;

        return new Vector3(
            ResizeMath.AnchorShift(anchor.X, localScale.X, factor.X),
            ResizeMath.AnchorShift(anchor.Y, localScale.Y, factor.Y),
            ResizeMath.AnchorShift(anchor.Z, localScale.Z, factor.Z));
    }

    // The local corner a face-anchored drag holds for one node. The handle's
    // sign is in the gizmo's frame and the anchor in the node's. A node turned
    // past a right angle has its local axis pointing against the handle, so
    // the corner is picked per node.
    private Vector3 ResolveAnchor(SceneNode node, in Aabb bounds)
    {
        Matrix4x4 world = node.WorldMatrix;
        return new Vector3(
            AnchorOn(new Vector3(world.M11, world.M12, world.M13), bounds.Min.X, bounds.Max.X),
            AnchorOn(new Vector3(world.M21, world.M22, world.M23), bounds.Min.Y, bounds.Max.Y),
            AnchorOn(new Vector3(world.M31, world.M32, world.M33), bounds.Min.Z, bounds.Max.Z));

        float AnchorOn(Vector3 nodeAxis, float min, float max) =>
            Vector3.Dot(nodeAxis, _constraintAxis) >= 0f ? min : max;
    }

    private bool IsMeasurable(Vector3 size)
    {
        if (ActiveHandle == GizmoHandle.Screen)
        {
            return MathF.Max(size.X, MathF.Max(size.Y, size.Z)) > ResizeMath.MinimumMeasurableSize;
        }

        // The mask is a unit axis, so the dot picks the dragged component.
        return Vector3.Dot(size, _axisMask) > ResizeMath.MinimumMeasurableSize;
    }

    // Mask by the handle's axis, not the raw handle: a negative handle would
    // fall into the uniform default and resize all three axes.
    private static Vector3 AxisMaskOf(GizmoHandle handle) => GizmoHandles.PositiveAxis(handle) switch
    {
        GizmoHandle.AxisX => Vector3.UnitX,
        GizmoHandle.AxisY => Vector3.UnitY,
        GizmoHandle.AxisZ => Vector3.UnitZ,
        _ => Vector3.One,
    };

    private static bool IsResizable(SceneNode node) =>
        node.Brush is not null || node.SubtreeBrushCount == 0;

    private bool AnyResizableTarget()
    {
        IReadOnlyList<GizmoDragTarget> targets = Targets;
        for (int i = 0; i < targets.Count; i++)
        {
            if (IsResizable(targets[i].Node))
                return true;
        }

        return false;
    }

    // WithScaledExtents throws when an extreme factor makes the plane set
    // degenerate. Mid-drag, hold the last good size instead.
    private static bool TryScaleExtents(Brush start, Vector3 factor, out Brush? resized)
    {
        try
        {
            resized = start.WithScaledExtents(factor);
            return true;
        }
        catch (ArgumentException)
        {
            resized = null;
            return false;
        }
    }
}
