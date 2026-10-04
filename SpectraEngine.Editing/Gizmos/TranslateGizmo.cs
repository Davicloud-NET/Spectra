using SpectraEngine.Core.Graphics;
using SpectraEngine.Core.Scene;
using SpectraEngine.Editing.Commands;
using SpectraEngine.Editing.Input;
using SpectraEngine.Editing.Undo;
using System.Collections.Generic;
using System.Numerics;

namespace SpectraEngine.Editing.Gizmos;

/// <summary>
/// The move tool: a translate gizmo at the selection's pivot whose drags move
/// every selected node. Snapping quantises the displacement unless
/// <see cref="TranslateSnapMode.AbsoluteGrid"/> is chosen. Render thread only.
/// </summary>
public sealed class TranslateGizmo : GizmoTool
{
    // Index-aligned with Targets.
    private readonly List<SetTransformCommand> _commands = [];

    private Vector3 _grabPoint;
    private Vector3 _constraintAxis;
    private Vector3 _constraintNormal;
    private Vector3 _freeAxisMask;
    private Vector3 _appliedDelta;

    // Screen handle's plane basis, frozen at the grab. A snapped free drag
    // quantises along these so it stays in the plane the cursor is tracked in.
    private Vector3 _screenRight;
    private Vector3 _screenUp;

    // AbsoluteGrid anchors on the reference node's start, not the pivot
    // average: rounding around an off-grid average lands no node on the grid.
    private Vector3 _absoluteAnchor;

    /// <summary>Creates a move tool over a scene and the history its edits land in.</summary>
    public TranslateGizmo(Scene scene, UndoStack undo)
        : base(scene, undo, "Move")
    {
    }

    /// <inheritdoc/>
    public override GizmoMode Mode => GizmoMode.Translate;

    /// <summary>
    /// The centre disc: a drag that started on an object follows the cursor in
    /// the camera-facing plane.
    /// </summary>
    public override GizmoHandle FreeMoveHandle => GizmoHandle.Screen;

    /// <summary>Grid-snapping configuration for drags.</summary>
    public GridSnapSettings Snap { get; } = new();

    /// <summary>
    /// The world-space movement applied so far in the current drag, snapping
    /// included; <see cref="Vector3.Zero"/> when no drag is in progress.
    /// </summary>
    public Vector3 DragDelta => _appliedDelta;

    /// <inheritdoc/>
    protected override bool HasEdit => _appliedDelta != Vector3.Zero;

    /// <summary>The grab pivot plus the movement applied so far.</summary>
    protected override Vector3 LivePivot => GrabPivot + _appliedDelta;

    /// <inheritdoc/>
    protected override GizmoPick HitTest(in GizmoGeometry geometry, in Ray3 ray, float tolerancePixels) =>
        TranslateGizmoHitTester.Pick(in geometry, in ray, tolerancePixels);

    /// <inheritdoc/>
    protected override bool TryPrepareDrag(in EditorInputFrame frame, in Ray3 ray)
    {
        SetUpConstraint(ActiveHandle);

        if (!TryProjectOntoConstraint(in ray, out Vector3 grabPoint))
            return false;

        _grabPoint = grabPoint;
        _appliedDelta = Vector3.Zero;
        _absoluteAnchor = ResolveAbsoluteAnchor();
        return true;
    }

    // Falls back to the last target when the reference node was skipped at
    // capture (a selected ancestor carries it), then to the grab pivot.
    private Vector3 ResolveAbsoluteAnchor()
    {
        IReadOnlyList<GizmoDragTarget> targets = Targets;
        if (targets.Count == 0)
            return GrabPivot;

        SceneNode? reference = ReferenceNode;
        for (int i = 0; i < targets.Count; i++)
        {
            if (ReferenceEquals(targets[i].Node, reference))
                return targets[i].StartWorldPosition;
        }

        return targets[targets.Count - 1].StartWorldPosition;
    }

    /// <inheritdoc/>
    protected override void RecordCommands()
    {
        _commands.Clear();

        IReadOnlyList<GizmoDragTarget> targets = Targets;
        for (int i = 0; i < targets.Count; i++)
        {
            SceneNode node = targets[i].Node;
            Vector3 startLocal = targets[i].StartLocal.Position;
            var command = new SetTransformCommand(
                node.Id, startLocal, node.LocalRotation, startLocal, node.LocalRotation)
            {
                Name = TransactionName,
            };

            _commands.Add(command);
            Undo.Record(command);
        }
    }

    /// <inheritdoc/>
    protected override void ApplyDrag(in EditorInputFrame frame, in Ray3 ray)
    {
        // View went edge-on to the constraint: hold the last position.
        if (!TryProjectOntoConstraint(in ray, out Vector3 point))
            return;

        Vector3 delta = point - _grabPoint;

        // Skip the snap at a zero delta. AbsoluteGrid would still return
        // round(anchor) - anchor, so a plain click on an off-grid selection
        // would move it and commit an edit. An unmoved cursor projects back to
        // the grab point bit for bit, so exact comparison is right.
        if (delta != Vector3.Zero && Snap.IsActiveWith(frame.Modifiers))
            delta = SnapDelta(delta);

        ApplyDelta(delta);
    }

    /// <inheritdoc/>
    protected override void ClearDragState()
    {
        _commands.Clear();
        _appliedDelta = Vector3.Zero;
    }

    /// <inheritdoc/>
    protected override void DrawHandles(DebugDraw output, in GizmoGeometry geometry, GizmoHandle highlighted) =>
        TranslateGizmoRenderer.Draw(output, in geometry, highlighted);

    private void SetUpConstraint(GizmoHandle handle)
    {
        GizmoGeometry geometry = Geometry;
        _freeAxisMask = GizmoHandles.FreeAxisMask(handle);
        _constraintAxis = geometry.Axis(handle);
        // Frozen at the grab: a camera moving mid-drag would otherwise swing
        // the constraint plane and drag the selection with it.
        _constraintNormal = geometry.PlaneNormal(handle);
        _screenRight = geometry.ViewRight;
        _screenUp = geometry.ViewUp;
    }

    // False when the view is too close to edge-on. `point` is then undefined;
    // callers must hold their last value.
    private bool TryProjectOntoConstraint(in Ray3 ray, out Vector3 point)
    {
        if (GizmoHandles.IsAxis(ActiveHandle))
            return GizmoMath.TryClosestPointOnLine(in ray, GrabPivot, _constraintAxis, out point);

        if (GizmoMath.TryRayPlane(in ray, GrabPivot, _constraintNormal, out float distance))
        {
            point = ray.PointAt(distance);
            return true;
        }

        point = Vector3.Zero;
        return false;
    }

    // Local orientation always snaps the displacement: a local frame has no
    // absolute grid to land on.
    private Vector3 SnapDelta(Vector3 delta)
    {
        // The screen plane is axis-aligned in no frame, so snap along its own
        // basis. Rounding world components would push the result off the plane.
        if (ActiveHandle == GizmoHandle.Screen &&
            (Orientation == GizmoOrientation.Local || Snap.Mode == TranslateSnapMode.Delta))
        {
            return _screenRight * Snap.SnapScalar(Vector3.Dot(delta, _screenRight))
                 + _screenUp * Snap.SnapScalar(Vector3.Dot(delta, _screenUp));
        }

        if (Orientation == GizmoOrientation.World)
        {
            if (Snap.Mode == TranslateSnapMode.AbsoluteGrid)
                return Snap.SnapMasked(_absoluteAnchor + delta, _freeAxisMask) - _absoluteAnchor;

            return Snap.SnapMasked(delta, _freeAxisMask);
        }

        GizmoGeometry geometry = Geometry;
        Vector3 snapped = Vector3.Zero;
        if (_freeAxisMask.X != 0f)
            snapped += geometry.AxisX * Snap.SnapScalar(Vector3.Dot(delta, geometry.AxisX));
        if (_freeAxisMask.Y != 0f)
            snapped += geometry.AxisY * Snap.SnapScalar(Vector3.Dot(delta, geometry.AxisY));
        if (_freeAxisMask.Z != 0f)
            snapped += geometry.AxisZ * Snap.SnapScalar(Vector3.Dot(delta, geometry.AxisZ));

        return snapped;
    }

    private void ApplyDelta(Vector3 worldDelta)
    {
        _appliedDelta = worldDelta;

        IReadOnlyList<GizmoDragTarget> targets = Targets;
        for (int i = 0; i < targets.Count; i++)
        {
            GizmoDragTarget target = targets[i];
            SetTransformCommand command = _commands[i];

            // From the captured start, not from where the node is now.
            Vector3 local = target.StartLocal.Position
                + Vector3.TransformNormal(worldDelta, target.ParentWorldInverse);

            command.SetAfter(local, command.AfterRotation);
            // Through the command, so scene and history entry agree.
            command.Do(Scene);
        }
    }
}
