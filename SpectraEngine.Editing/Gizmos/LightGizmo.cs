using System;
using System.Numerics;
using SpectraEngine.Core.Graphics;
using SpectraEngine.Core.Input;
using SpectraEngine.Core.Scene;
using SpectraEngine.Editing.Commands;
using SpectraEngine.Editing.Input;
using SpectraEngine.Editing.Undo;

namespace SpectraEngine.Editing.Gizmos;

/// <summary>Which of a light's own handles a press landed on.</summary>
public enum LightHandle
{
    /// <summary>Nothing.</summary>
    None,

    /// <summary>The reach ring: drag out to grow the light's range.</summary>
    Range,

    /// <summary>The aim knob: drag to point a directional light.</summary>
    Aim,

    /// <summary>A spot's outer half-angle: the edge of the cone.</summary>
    ConeOuter,

    /// <summary>A spot's inner half-angle: where the falloff begins.</summary>
    ConeInner,

    /// <summary>A rect light's width.</summary>
    Width,

    /// <summary>A rect light's height.</summary>
    Height,

    /// <summary>A disc light's radius.</summary>
    Radius,
}

/// <summary>
/// The light's own manipulator: range, aim and shape, dragged in the viewport.
/// Runs beside the transform gizmo, which gets the press first.
/// </summary>
// Not a GizmoMode: range and aim are payload edits, and a lamp is moved all
// the time, so a mode would put a keypress between the two.
public sealed class LightGizmo
{
    private readonly Scene _scene;
    private readonly UndoStack _undo;

    private SceneNode? _node;
    private LightHandle _handle;
    private SetLightCommand? _command;

    // Grab capture. Every drag frame recomputes from this, never from the
    // previous frame.
    private float _grabScalar;
    private Vector2 _grabCursor;
    private Vector2 _grabAxis;
    private float _grabWorldPerPixel;

    public LightGizmo(Scene scene, UndoStack undo)
    {
        _scene = scene ?? throw new ArgumentNullException(nameof(scene));
        _undo = undo ?? throw new ArgumentNullException(nameof(undo));
    }

    /// <summary>Whether the tool draws and answers presses at all.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>The snap policy for length handles. Shared with the move tool.</summary>
    public SnapSettings? Snap { get; set; }

    /// <summary>Whether a drag is in progress.</summary>
    public bool IsDragging => _handle != LightHandle.None;

    /// <summary>
    /// Whether the live drag edits a length (range or an extent), which snaps
    /// on the move grid.
    /// </summary>
    public bool IsDraggingLength => _handle is LightHandle.Range or LightHandle.Width
        or LightHandle.Height or LightHandle.Radius;

    /// <summary>The handle the cursor is over, or <see cref="LightHandle.None"/>.</summary>
    public LightHandle Hovered { get; private set; }

    /// <summary>How close, in screen pixels, a press must be to grab a handle.</summary>
    public float GrabPixels { get; set; } = 8f;

    /// <summary>The handle knob's radius in screen pixels.</summary>
    public const float KnobPixels = 5f;

    /// <summary>How far the aim knob sits from the lamp, in screen pixels.</summary>
    public const float AimReachPixels = 74f;

    /// <summary>Advances the tool by one frame. True while it owns the pointer.</summary>
    /// <param name="cancelRequested">Escape, or a viewport that lost focus.</param>
    public bool Update(in EditorInputFrame frame, bool cancelRequested)
    {
        if (IsDragging)
        {
            if (cancelRequested)
            {
                Cancel();
                return false;
            }

            if (!frame.IsDown(PointerButtons.Left))
            {
                Commit();
                return false;
            }

            Drag(in frame);
            return true;
        }

        Hovered = Enabled ? Pick(in frame, out _) : LightHandle.None;

        if (Hovered == LightHandle.None || !frame.WasPressed(PointerButtons.Left))
            return false;

        return TryBeginDrag(in frame);
    }

    /// <summary>
    /// Which handle a press at this frame's cursor would grab, and on which
    /// node. Changes no state.
    /// </summary>
    public LightHandle Pick(in EditorInputFrame frame, out SceneNode? node)
    {
        node = null;

        if (!Enabled || !frame.IsPointerUsable || !TrySoleLight(out SceneNode? lamp, out Light? light))
            return LightHandle.None;

        Camera camera = _scene.Camera;
        Vector2 viewport = frame.ViewportSize;
        Vector3 at = lamp!.WorldPosition;

        if (!TryProject(camera, at, viewport, out Vector2 origin))
            return LightHandle.None;

        node = lamp;
        float grab = GrabPixels + KnobPixels;

        if (light!.Kind == LightKind.Directional)
        {
            if (TryAimKnob(camera, lamp, viewport, out Vector2 knob) &&
                Vector2.Distance(frame.CursorPosition, knob) <= grab)
            {
                return LightHandle.Aim;
            }

            // A directional light has no range.
            return LightHandle.None;
        }

        // Shape handles before range: a cone rim and the range knob can
        // coincide, and the shape is the more specific answer.
        foreach (LightHandle candidate in ShapeHandles(light.Kind))
        {
            if (TryKnobWorld(camera, lamp, light, candidate, viewport, out Vector3 world) &&
                TryProject(camera, world, viewport, out Vector2 knob) &&
                Vector2.Distance(frame.CursorPosition, knob) <= grab)
            {
                return candidate;
            }
        }

        if (TryKnobWorld(camera, lamp, light, LightHandle.Range, viewport, out Vector3 rangeWorld) &&
            TryProject(camera, rangeWorld, viewport, out Vector2 rangeKnob) &&
            Vector2.Distance(frame.CursorPosition, rangeKnob) <= grab)
        {
            return LightHandle.Range;
        }

        node = null;
        return LightHandle.None;
    }

    // In pick order.
    private static LightHandle[] ShapeHandles(LightKind kind) => kind switch
    {
        LightKind.Spot => [LightHandle.ConeInner, LightHandle.ConeOuter],
        LightKind.Rect => [LightHandle.Width, LightHandle.Height],
        LightKind.Disc => [LightHandle.Radius],
        _ => [],
    };

    /// <summary>Draws the handles for the selected light, if there is exactly one.</summary>
    public void Draw(DebugDraw output, Vector2 viewportSize)
    {
        ArgumentNullException.ThrowIfNull(output);

        if (!Enabled || !TrySoleLight(out SceneNode? lamp, out Light? light))
            return;

        Camera camera = _scene.Camera;
        Vector3 at = lamp!.WorldPosition;

        if (light!.Kind == LightKind.Directional)
        {
            if (TryAimKnobWorld(camera, lamp, viewportSize, out Vector3 knob))
            {
                DrawKnob(output, knob, camera, viewportSize,
                    Colour(LightHandle.Aim), out _);
            }

            return;
        }

        if (TryKnobWorld(camera, lamp, light, LightHandle.Range, viewportSize, out Vector3 rangeKnob))
        {
            // The line ties the knob to its lamp. A loose dot reads as a second light.
            output.Line(at, rangeKnob, Colour(LightHandle.Range) * 0.5f);
            DrawKnob(output, rangeKnob, camera, viewportSize, Colour(LightHandle.Range), out _);
        }

        foreach (LightHandle handle in ShapeHandles(light.Kind))
        {
            if (!TryKnobWorld(camera, lamp, light, handle, viewportSize, out Vector3 knob))
                continue;

            output.Line(at, knob, Colour(handle) * 0.4f);
            DrawKnob(output, knob, camera, viewportSize, Colour(handle), out _);
        }
    }

    /// <summary>Abandons a drag in progress. Idempotent.</summary>
    public void Reset()
    {
        if (IsDragging)
            Cancel();

        Hovered = LightHandle.None;
    }

    private bool TryBeginDrag(in EditorInputFrame frame)
    {
        if (Pick(in frame, out SceneNode? lamp) is var handle &&
            (handle == LightHandle.None || lamp?.Light is null))
        {
            return false;
        }

        _node = lamp;
        _handle = handle;
        _grabCursor = frame.CursorPosition;
        _grabScalar = ScalarOf(lamp!.Light!, handle);
        _grabWorldPerPixel = GizmoMath.WorldPerPixel(
            _scene.Camera, frame.ViewportSize.Y,
            MathF.Max(GizmoMath.ViewDepth(_scene.Camera, lamp.WorldPosition), 0.01f));

        _grabAxis = ScreenAxisFor(lamp, lamp.Light!, handle, frame.ViewportSize);

        // One transaction per gesture, so one Ctrl+Z undoes the drag.
        _undo.BeginTransaction(TransactionName(handle));
        _command = null;
        return true;
    }

    private void Drag(in EditorInputFrame frame)
    {
        if (_node?.Light is not { } light)
            return;

        if (_handle == LightHandle.Aim)
        {
            DragAim(in frame);
            return;
        }

        DragScalar(in frame, light);
    }

    private void DragScalar(in EditorInputFrame frame, Light light)
    {
        float travelPixels = Vector2.Dot(frame.CursorPosition - _grabCursor, _grabAxis);

        bool angular = _handle is LightHandle.ConeInner or LightHandle.ConeOuter;

        float value = angular
            ? _grabScalar + (travelPixels * DegreesPerPixel)
            : _grabScalar + (travelPixels * _grabWorldPerPixel);

        // IsActiveWith, not Enabled: Alt inverts the snap for one gesture.
        SnapSettings? snap = angular ? AngleSnap : Snap;
        if (snap is { } live && live.IsActiveWith(frame.Modifiers))
            value = live.SnapScalar(value);

        // Apply clamps. Light.Range throws at or below zero, and a throw from
        // Do would leave the transaction open.
        Record(Apply(SetLightCommand.Settings.From(light), _handle, value));
    }

    private const float DegreesPerPixel = 0.25f;

    /// <summary>The snap policy for angular handles. Shared with the rotate tool.</summary>
    public SnapSettings? AngleSnap { get; set; }

    private static float ScalarOf(Light light, LightHandle handle) => handle switch
    {
        LightHandle.ConeInner => light.InnerAngle,
        LightHandle.ConeOuter => light.OuterAngle,
        LightHandle.Width => light.Width,
        LightHandle.Height => light.Height,
        LightHandle.Radius => light.Radius,
        _ => light.Range,
    };

    private static SetLightCommand.Settings Apply(
        SetLightCommand.Settings settings, LightHandle handle, float value) => handle switch
    {
        // Light's setters clamp the angles themselves.
        LightHandle.ConeInner => settings with { InnerAngle = value },
        LightHandle.ConeOuter => settings with { OuterAngle = value },
        LightHandle.Width => settings with { Width = MathF.Max(value, Light.MinimumExtent) },
        LightHandle.Height => settings with { Height = MathF.Max(value, Light.MinimumExtent) },
        LightHandle.Radius => settings with { Radius = MathF.Max(value, Light.MinimumExtent) },
        _ => settings with { Range = MathF.Max(value, MinimumRange) },
    };

    private static string TransactionName(LightHandle handle) => handle switch
    {
        LightHandle.Aim => "Aim light",
        LightHandle.ConeInner or LightHandle.ConeOuter => "Light cone",
        LightHandle.Width or LightHandle.Height or LightHandle.Radius => "Light size",
        _ => "Light range",
    };

    private void DragAim(in EditorInputFrame frame)
    {
        if (_node is not { } lamp)
            return;

        Camera camera = _scene.Camera;
        Vector3 at = lamp.WorldPosition;

        // Intersect the cursor ray with the camera-facing plane through the lamp.
        Ray3 ray = camera.ScreenPointToRay(frame.CursorPosition, frame.ViewportSize);
        float depth = GizmoMath.ViewDepth(camera, at);
        if (depth <= 0.01f)
            return;

        float denominator = Vector3.Dot(ray.Direction, camera.Forward);
        if (MathF.Abs(denominator) < 1e-4f)
            return;

        float t = (depth - Vector3.Dot(ray.Origin - camera.Position, camera.Forward)) / denominator;
        Vector3 target = ray.Origin + (ray.Direction * t);

        Vector3 travel = target - at;
        if (travel.LengthSquared() < 1e-8f)
            return;

        // RotationForDirection takes the direction light travels: lamp toward knob.
        Quaternion rotation = Light.RotationForDirection(Vector3.Normalize(travel));

        var command = new SetLocalTransformCommand(
            lamp.Id,
            lamp.LocalTransform,
            lamp.LocalTransform with { Rotation = rotation })
        {
            Name = "Aim light",
        };

        _undo.Execute(command);
    }

    private void Record(SetLightCommand.Settings after)
    {
        if (_node is not { } lamp)
            return;

        // Retarget one command per frame so its before state stays the grab's.
        if (_command is { } existing)
        {
            existing.SetAfter(after);
            existing.Do(_scene);
            return;
        }

        _command = SetLightCommand.Capture(lamp, after);
        _undo.Execute(_command);
    }

    private void Commit()
    {
        _handle = LightHandle.None;
        _node = null;
        _command = null;

        // An empty transaction lands no history entry.
        _undo.CommitTransaction();
    }

    private void Cancel()
    {
        _handle = LightHandle.None;
        _node = null;
        _command = null;
        _undo.CancelTransaction();
    }

    /// <summary>
    /// The smallest range a drag may produce. Not zero:
    /// <see cref="Light.Range"/> throws at or below it.
    /// </summary>
    public const float MinimumRange = 0.05f;

    // One selected light only. Bulk edits go through the inspector.
    private bool TrySoleLight(out SceneNode? node, out Light? light)
    {
        node = null;
        light = null;

        var selection = _scene.Selection.Items;
        if (selection.Count != 1 || selection[0].Light is not { } only)
            return false;

        node = selection[0];
        light = only;
        return true;
    }

    private static Vector3 Colour(LightHandle handle) => handle switch
    {
        LightHandle.Aim => new Vector3(1f, 0.85f, 0.35f),

        // Cone knobs share a hue: inner and outer are one quantity.
        LightHandle.ConeOuter => new Vector3(0.55f, 1f, 0.75f),
        LightHandle.ConeInner => new Vector3(0.28f, 0.55f, 0.4f),

        LightHandle.Width or LightHandle.Height or LightHandle.Radius => new Vector3(1f, 0.6f, 0.85f),

        _ => new Vector3(0.6f, 0.9f, 1f),
    };

    private static bool TryProject(Camera camera, Vector3 world, Vector2 viewport, out Vector2 screen)
    {
        screen = default;

        Vector4 clip = Vector4.Transform(new Vector4(world, 1f), camera.GetViewProjection());
        if (clip.W <= 1e-4f)
            return false;

        screen = new Vector2(
            ((clip.X / clip.W) + 1f) * 0.5f * viewport.X,
            (1f - (clip.Y / clip.W)) * 0.5f * viewport.Y);

        return true;
    }

    // Where a handle's knob sits in the world. Pick and draw both use this,
    // so what is grabbed is what is on screen.
    private static bool TryKnobWorld(
        Camera camera, SceneNode lamp, Light light, LightHandle handle, Vector2 viewport, out Vector3 knob)
    {
        knob = default;
        if (viewport.Y <= 0f)
            return false;

        Vector3 at = lamp.WorldPosition;

        switch (handle)
        {
            case LightHandle.Range:
                // Screen right, not a world axis: the range sphere has no facing.
                knob = at + (camera.Right * light.Range);
                return true;

            case LightHandle.Aim:
                return TryAimKnobWorld(camera, lamp, viewport, out knob);
        }

        // Shape handles sit on the light's own basis, where the shape is drawn.
        Basis(lamp, out Vector3 forward, out Vector3 right, out Vector3 up);

        switch (handle)
        {
            case LightHandle.Width:
                knob = at + (right * light.Width * 0.5f);
                return true;

            case LightHandle.Height:
                knob = at + (up * light.Height * 0.5f);
                return true;

            case LightHandle.Radius:
                knob = at + (right * light.Radius);
                return true;

            case LightHandle.ConeOuter:
            case LightHandle.ConeInner:
            {
                float degrees = handle == LightHandle.ConeOuter ? light.OuterAngle : light.InnerAngle;
                float radians = degrees * (MathF.PI / 180f);

                // On the cone's rim at the light's range, where the overlay draws the ring.
                knob = at
                    + (forward * light.Range * MathF.Cos(radians))
                    + (right * light.Range * MathF.Sin(radians));

                return true;
            }
        }

        return false;
    }

    private static void Basis(SceneNode node, out Vector3 forward, out Vector3 right, out Vector3 up)
    {
        Matrix4x4 world = node.WorldMatrix;
        forward = Vector3.Normalize(new Vector3(world.M31, world.M32, world.M33));
        right = Vector3.Normalize(new Vector3(world.M11, world.M12, world.M13));
        up = Vector3.Normalize(new Vector3(world.M21, world.M22, world.M23));
    }

    // Screen direction the knob moves when its scalar grows. Measured, not
    // assumed: handles do not share an axis (screen right, the light's up,
    // the cone rim).
    private Vector2 ScreenAxisFor(SceneNode lamp, Light light, LightHandle handle, Vector2 viewport)
    {
        Camera camera = _scene.Camera;

        if (!TryKnobWorld(camera, lamp, light, handle, viewport, out Vector3 here) ||
            !TryProject(camera, here, viewport, out Vector2 a))
        {
            return new Vector2(1f, 0f);
        }

        // Finite difference: project the knob again with a slightly larger scalar.
        float scalar = ScalarOf(light, handle);
        float probe = MathF.Max(scalar * 1.01f, scalar + 0.01f);

        Light widened = light.Clone();
        Apply(SetLightCommand.Settings.From(widened), handle, probe).ApplyTo(widened);

        if (!TryKnobWorld(camera, lamp, widened, handle, viewport, out Vector3 there) ||
            !TryProject(camera, there, viewport, out Vector2 b))
        {
            return new Vector2(1f, 0f);
        }

        Vector2 delta = b - a;
        return delta.LengthSquared() < 1e-6f ? new Vector2(1f, 0f) : Vector2.Normalize(delta);
    }

    private static bool TryAimKnobWorld(Camera camera, SceneNode lamp, Vector2 viewport, out Vector3 knob)
    {
        knob = default;
        if (viewport.Y <= 0f)
            return false;

        Vector3 at = lamp.WorldPosition;
        float depth = GizmoMath.ViewDepth(camera, at);
        if (depth <= 0.01f)
            return false;

        Matrix4x4 world = lamp.WorldMatrix;
        var travel = new Vector3(world.M31, world.M32, world.M33);
        if (travel.LengthSquared() < 1e-8f)
            return false;

        // Constant screen distance, so the knob stays grabbable at any zoom.
        // Not the range, which would move the aim knob when the range changes.
        float reach = AimReachPixels * GizmoMath.WorldPerPixel(camera, viewport.Y, depth);
        knob = at + (Vector3.Normalize(travel) * reach);
        return true;
    }

    private static bool TryAimKnob(Camera camera, SceneNode lamp, Vector2 viewport, out Vector2 knob)
    {
        knob = default;
        return TryAimKnobWorld(camera, lamp, viewport, out Vector3 world)
            && TryProject(camera, world, viewport, out knob);
    }

    private void DrawKnob(
        DebugDraw output, Vector3 at, Camera camera, Vector2 viewport, Vector3 colour, out float radius)
    {
        float depth = MathF.Max(GizmoMath.ViewDepth(camera, at), 0.01f);
        radius = KnobPixels * GizmoMath.WorldPerPixel(camera, viewport.Y, depth);

        Vector3 right = camera.Right * radius;
        Vector3 up = camera.Up * radius;

        // Diamond plus diagonals: reads as filled, and DebugDraw has no triangles.
        output.Line(at + right, at + up, colour);
        output.Line(at + up, at - right, colour);
        output.Line(at - right, at - up, colour);
        output.Line(at - up, at + right, colour);
        output.Line(at - right, at + right, colour);
        output.Line(at - up, at + up, colour);
    }
}
