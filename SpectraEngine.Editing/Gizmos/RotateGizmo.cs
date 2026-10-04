using SpectraEngine.Core.Graphics;
using SpectraEngine.Core.Scene;
using SpectraEngine.Editing.Commands;
using SpectraEngine.Editing.Input;
using SpectraEngine.Editing.Undo;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace SpectraEngine.Editing.Gizmos;

/// <summary>
/// The rotate tool: three axis rings and a view-aligned ring at the selection's
/// pivot. A drag around one rotates the whole selection about that pivot as one
/// rigid body.
/// </summary>
// The winding number is the only state carried between drag frames. The rest
// is recomputed from the grab capture, so cancel restores the start exactly.
public sealed class RotateGizmo : GizmoTool
{
    private const float MinimumGrabRadiusFactor = RotateGizmoHitTester.MinimumGrabRadiusFactor;

    // Index-aligned with Targets; reused across gestures.
    private readonly List<SetTransformCommand> _commands = [];

    private Vector3 _ringAxis;
    private Vector3 _reference;      // unit, pivot to grab point, in the ring's plane
    private Vector3 _perpendicular;  // cross(axis, reference)
    private float _ringRadius;

    private float _rawAngle;         // last frame's angle in (-pi, pi]
    private int _turns;
    private float _appliedAngle;

    /// <summary>Creates a rotate tool over a scene and the history its edits land in.</summary>
    public RotateGizmo(Scene scene, UndoStack undo)
        : base(scene, undo, "Rotate")
    {
    }

    /// <inheritdoc/>
    public override GizmoMode Mode => GizmoMode.Rotate;

    /// <summary>Angle snapping for drags.</summary>
    public AngleSnapSettings Snap { get; } = new();

    /// <summary>
    /// The angle applied so far in the current drag, in radians, signed about
    /// <see cref="DragAxis"/>. Zero when no drag is in progress.
    /// </summary>
    public float DragAngle => _appliedAngle;

    /// <summary>The same angle in degrees.</summary>
    public float DragAngleDegrees => _appliedAngle * (180f / MathF.PI);

    /// <summary>
    /// The world-space unit axis the current drag turns about.
    /// <see cref="Vector3.Zero"/> when no drag is in progress.
    /// </summary>
    public Vector3 DragAxis => _ringAxis;

    /// <inheritdoc/>
    protected override bool HasEdit => _appliedAngle != 0f;

    /// <inheritdoc/>
    protected override GizmoPick HitTest(in GizmoGeometry geometry, in Ray3 ray, float tolerancePixels) =>
        RotateGizmoHitTester.Pick(in geometry, in ray, tolerancePixels);

    /// <inheritdoc/>
    protected override bool TryPrepareDrag(in EditorInputFrame frame, in Ray3 ray)
    {
        GizmoGeometry geometry = Geometry;
        if (!geometry.TryGetRing(ActiveHandle, out Vector3 axis, out float radius))
            return false;

        // Frozen at the grab. A plane that followed the turning selection or
        // the camera would chase the cursor.
        _ringAxis = axis;
        _ringRadius = radius;

        if (!TryProjectOntoRingPlane(in ray, out Vector3 grabPoint))
            return false;

        Vector3 spoke = grabPoint - GrabPivot;
        float length = spoke.Length();
        if (length < radius * MinimumGrabRadiusFactor)
            return false; // grabbed at the pivot, no direction to sweep from

        _reference = spoke / length;
        _perpendicular = Vector3.Cross(_ringAxis, _reference);
        _rawAngle = 0f;
        _turns = 0;
        _appliedAngle = 0f;
        return true;
    }

    /// <inheritdoc/>
    protected override void RecordCommands()
    {
        _commands.Clear();

        IReadOnlyList<GizmoDragTarget> targets = Targets;
        for (int i = 0; i < targets.Count; i++)
        {
            SceneNode node = targets[i].Node;
            var command = new SetTransformCommand(
                node.Id, node.LocalPosition, node.LocalRotation, node.LocalPosition, node.LocalRotation)
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
        // Edge-on to the ring's plane: hold the last angle.
        if (!TryProjectOntoRingPlane(in ray, out Vector3 point))
            return;

        Vector3 spoke = point - GrabPivot;
        float length = spoke.Length();
        if (length < _ringRadius * MinimumGrabRadiusFactor)
            return; // cursor at the pivot, direction is noise

        float raw = MathF.Atan2(Vector3.Dot(spoke, _perpendicular), Vector3.Dot(spoke, _reference));

        // A jump of more than half a turn between frames is the branch cut at pi.
        float jump = raw - _rawAngle;
        if (jump > MathF.PI)
            _turns--;
        else if (jump < -MathF.PI)
            _turns++;
        _rawAngle = raw;

        float angle = raw + _turns * MathF.Tau;
        if (Snap.IsActiveWith(frame.Modifiers))
            angle = Snap.SnapRadians(angle);

        ApplyAngle(angle);
    }

    /// <inheritdoc/>
    protected override void ClearDragState()
    {
        _commands.Clear();
        _ringAxis = Vector3.Zero;
        _reference = Vector3.Zero;
        _perpendicular = Vector3.Zero;
        _ringRadius = 0f;
        _rawAngle = 0f;
        _turns = 0;
        _appliedAngle = 0f;
    }

    /// <inheritdoc/>
    protected override void DrawHandles(DebugDraw output, in GizmoGeometry geometry, GizmoHandle highlighted)
    {
        RotateGizmoRenderer.Draw(output, in geometry, highlighted);

        // A zero sweep would draw two coincident spokes.
        if (State == GizmoInteractionState.Dragging && _appliedAngle != 0f)
            RotateGizmoRenderer.DrawSweep(output, in geometry, _ringAxis, _reference, _ringRadius, _appliedAngle);
    }

    private bool TryProjectOntoRingPlane(in Ray3 ray, out Vector3 point)
    {
        if (GizmoMath.TryRayPlane(in ray, GrabPivot, _ringAxis, out float distance))
        {
            point = ray.PointAt(distance);
            return true;
        }

        point = Vector3.Zero;
        return false;
    }

    private void ApplyAngle(float angle)
    {
        _appliedAngle = angle;

        // CreateFromAxisAngle(axis, 0) is not bit-exact identity. It would
        // dirty a brush node's chunk on every frame of a snapped drag at zero.
        Quaternion delta = angle == 0f
            ? Quaternion.Identity
            : Quaternion.CreateFromAxisAngle(_ringAxis, angle);

        IReadOnlyList<GizmoDragTarget> targets = Targets;
        for (int i = 0; i < targets.Count; i++)
        {
            GizmoDragTarget target = targets[i];
            SetTransformCommand command = _commands[i];

            // From the captured start, never the node's current transform.
            // Delta is in world space, so it pre-multiplies.
            Vector3 worldPosition = GrabPivot + Vector3.Transform(target.StartWorldPosition - GrabPivot, delta);
            Quaternion worldRotation = delta * target.StartWorldRotation;

            command.SetAfter(
                Vector3.Transform(worldPosition, target.ParentWorldInverse),
                target.ParentWorldRotationInverse * worldRotation);
            command.Do(Scene);
        }
    }
}
