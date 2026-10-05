using SpectraEngine.Core.Graphics;
using SpectraEngine.Core.Input;
using SpectraEngine.Core.Scene;
using SpectraEngine.Editing.Cameras;
using SpectraEngine.Editing.Gizmos;
using SpectraEngine.Editing.Input;
using SpectraEngine.Editing.Selection;
using System;
using System.Numerics;

namespace SpectraEngine.Editing.Viewport;

/// <summary>
/// Decides what a press in the viewport means and routes the pointer to it:
/// a gizmo handle manipulates, an object is selected and moved, empty space
/// starts a box select. The camera runs only when none of them owns the pointer.
/// Render thread only.
/// </summary>
// Handles are tested before objects: they draw on top, so the object behind
// would otherwise take every grab.
// A gesture keeps the pointer until it ends, and that goes for the camera's
// gestures too. A locked cursor has no position, so nothing starts while it is.
public sealed class ViewportInteractionController
{
    // Clicking a selected node collapses a multi-selection on release, not on
    // press, so a group can be dragged by one of its members.
    private SceneNode? _deferredSelect;

    /// <summary>Creates a viewport over a scene and its manipulator.</summary>
    public ViewportInteractionController(Scene scene, GizmoController gizmos)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(gizmos);
        if (!ReferenceEquals(gizmos.Scene, scene))
        {
            throw new ArgumentException(
                $"The manipulator edits scene '{gizmos.Scene.Name}', not '{scene.Name}'.", nameof(gizmos));
        }

        Scene = scene;
        Gizmos = gizmos;
        BoxSelect = new BoxSelectController(scene);
    }

    /// <summary>The scene this viewport looks at.</summary>
    public Scene Scene { get; }

    /// <summary>The manipulator.</summary>
    public GizmoController Gizmos { get; }

    /// <summary>The marquee.</summary>
    public BoxSelectController BoxSelect { get; }

    /// <summary>
    /// The camera driver, run on frames where nothing has claimed the pointer.
    /// Null leaves navigation entirely to the host.
    /// </summary>
    public EditorCameraController? CameraController { get; set; }

    /// <summary>The button that selects, box-selects and grabs handles.</summary>
    public PointerButtons DragButton { get; set; } = PointerButtons.Left;

    /// <summary>
    /// Whether a press on an object drags it or only selects it.
    /// </summary>
    public bool ObjectDragEnabled { get; set; } = true;

    /// <summary>How far a picking ray reaches. Infinite by default.</summary>
    public float PickDistance { get; set; } = float.PositiveInfinity;

    /// <summary>
    /// The light manipulator, or null in a viewport that does not edit lights.
    /// A light drag reports <see cref="ViewportDragMode.Manipulate"/>.
    /// </summary>
    public Gizmos.LightGizmo? LightTool { get; set; }

    /// <summary>What currently owns the pointer.</summary>
    public ViewportDragMode DragMode { get; private set; }

    /// <summary>
    /// The node the last press landed on, or null when it landed on a handle or
    /// on empty space. Cleared when the gesture ends.
    /// </summary>
    public SceneNode? PressedNode { get; private set; }

    /// <summary>Raised when <see cref="DragMode"/> changes, carrying the new mode.</summary>
    public event Action<ViewportDragMode>? DragModeChanged;

    /// <summary>
    /// How far a hover ray reaches. Finite, unlike <see cref="PickDistance"/>:
    /// a hover runs every frame the cursor moves.
    /// </summary>
    public float HoverPickDistance { get; set; } = 500f;

    /// <summary>
    /// The node under the cursor, or null. Also null during a gesture and
    /// over a gizmo handle, where a press would not select.
    /// </summary>
    public SceneNode? HoveredNode { get; private set; }

    /// <summary>What a press would mean at the cursor's current position.</summary>
    public ViewportDragMode HoverMode { get; private set; }

    /// <summary>Which brush face is under the cursor, or -1.</summary>
    public int HoveredPlaneIndex { get; private set; } = -1;

    private int _pressedPlane = -1;

    // Compared exactly: a still cursor skips the pick.
    private Vector2 _hoverCursor = new(float.NaN, float.NaN);

    // Same precedence as ClassifyPress, so the outline matches what a click does.
    private void UpdateHover(in EditorInputFrame frame, bool cameraOwnsPointer)
    {
        if (!frame.IsPointerUsable || cameraOwnsPointer || DragMode != ViewportDragMode.None)
        {
            ClearHover();
            return;
        }

        if (frame.CursorPosition == _hoverCursor)
            return;

        _hoverCursor = frame.CursorPosition;

        if (Gizmos.Active.PickAt(in frame).IsHit)
        {
            HoveredNode = null;
            HoverMode = ViewportDragMode.Manipulate;
            return;
        }

        float previous = PickDistance;
        PickDistance = HoverPickDistance;
        try
        {
            HoveredNode = TryPickNode(in frame, out SceneNode? node, out int hoveredPlane) ? node : null;
            HoveredPlaneIndex = HoveredNode is null ? -1 : hoveredPlane;
        }
        finally
        {
            PickDistance = previous;
        }

        HoverMode = HoveredNode is null ? ViewportDragMode.BoxSelect : ViewportDragMode.SelectAndMove;
    }

    private void ClearHover()
    {
        HoveredNode = null;
        HoveredPlaneIndex = -1;
        HoverMode = ViewportDragMode.None;
        _hoverCursor = new Vector2(float.NaN, float.NaN);
    }

    /// <summary>
    /// Decides what a press at this frame's cursor would mean, without changing
    /// anything. <see cref="ViewportDragMode.None"/> when the cursor is outside
    /// the viewport or locked, or the camera has the pointer.
    /// </summary>
    public ViewportDragMode ClassifyPress(in EditorInputFrame frame)
    {
        if (!frame.IsPointerUsable)
            return ViewportDragMode.None;

        if (CameraOwnsPointer(in frame))
            return ViewportDragMode.None;

        if (Gizmos.Active.PickAt(in frame).IsHit)
            return ViewportDragMode.Manipulate;

        return TryPickNode(in frame, out _)
            ? ViewportDragMode.SelectAndMove
            : ViewportDragMode.BoxSelect;
    }

    /// <summary>
    /// Advances the viewport by one frame: routes the pointer to whatever owns
    /// it, arbitrates a fresh press, and runs the camera when nothing does.
    /// </summary>
    /// <param name="frame">This frame's input snapshot.</param>
    /// <param name="cancelRequested">
    /// True on the frame the user aborts (Escape, lost focus). Cancels the
    /// live gesture.
    /// </param>
    /// <returns>The mode that owns the pointer after this frame.</returns>
    public ViewportDragMode Update(in EditorInputFrame frame, bool cancelRequested = false)
    {
        if (DragMode == ViewportDragMode.BoxSelect)
        {
            ClearHover();
            UpdateBoxSelect(in frame, cancelRequested);
            return WithheldFromCamera();
        }

        // Before the transform gizmo: both report Manipulate, and its finish
        // logic would otherwise run against a light drag it never started.
        if (LightTool is { IsDragging: true } dragging)
        {
            ClearHover();

            if (!dragging.Update(in frame, cancelRequested))
                SetMode(ViewportDragMode.None);

            return WithheldFromCamera();
        }

        bool cameraOwnsPointer = CameraOwnsPointer(in frame);

        // Runs every frame for the handle hover; the grab is off while the
        // camera has the pointer.
        GizmoUpdateResult gizmoResult = Gizmos.Update(in frame, cancelRequested, !cameraOwnsPointer);

        if (DragMode != ViewportDragMode.None)
        {
            ClearHover();
            FinishGizmoGesture(gizmoResult, cancelRequested);
            return WithheldFromCamera();
        }

        if (gizmoResult == GizmoUpdateResult.DragBegan)
        {
            ClearHover();
            SetMode(ViewportDragMode.Manipulate);
            return WithheldFromCamera();
        }

        // The light tool only gets a press the transform gizmo did not take.
        if (LightTool is { } lights)
        {
            if (cameraOwnsPointer)
            {
                // Reset, or a handle stays lit under a cursor driving the camera.
                lights.Reset();
            }
            else if (lights.Update(in frame, cancelRequested))
            {
                ClearHover();
                SetMode(ViewportDragMode.Manipulate);
                return WithheldFromCamera();
            }
        }

        if (frame.WasPressed(DragButton) && frame.IsPointerUsable && !cameraOwnsPointer)
        {
            ClearHover();
            BeginGesture(in frame);
            return WithheldFromCamera();
        }

        CameraController?.Update(in frame);

        UpdateHover(in frame, cameraOwnsPointer);
        return DragMode;
    }

    /// <summary>Draws the manipulator and, during a box select, the marquee.</summary>
    public void Draw(DebugDraw output, Vector2 viewportSize)
    {
        ArgumentNullException.ThrowIfNull(output);
        Gizmos.Draw(output);
        BoxSelect.Draw(output, viewportSize);
    }

    /// <summary>
    /// Abandons the live gesture and returns to
    /// <see cref="ViewportDragMode.None"/>. For a host that lost focus, replaced
    /// the scene or is tearing the viewport down.
    /// </summary>
    public void Reset()
    {
        BoxSelect.Cancel();
        Gizmos.Reset();
        _deferredSelect = null;
        _pressedPlane = -1;
        PressedNode = null;
        ClearHover();
        LightTool?.Reset();
        CameraController?.SuspendNavigation();
        SetMode(ViewportDragMode.None);
    }

    // The camera measures its drag from the last cursor it saw. Tell it about
    // every frame it missed, or the whole withheld travel lands as one step.
    private ViewportDragMode WithheldFromCamera()
    {
        CameraController?.SuspendNavigation();
        return DragMode;
    }

    // OwnsPointer, not ClaimsPress: claiming only sees this frame's press edge,
    // so a pan or look already under way would lose the pointer to a left click.
    private bool CameraOwnsPointer(in EditorInputFrame frame) =>
        CameraController is { } camera && camera.OwnsPointer(in frame);

    private void BeginGesture(in EditorInputFrame frame)
    {
        if (TryPickNode(in frame, out SceneNode? node, out int planeIndex))
        {
            _pressedPlane = planeIndex;
            BeginObjectGesture(in frame, node!);
            return;
        }

        _pressedPlane = -1;

        BoxSelect.DragButton = DragButton;
        BoxSelect.Begin(frame.CursorPosition);
        PressedNode = null;
        SetMode(ViewportDragMode.BoxSelect);
    }

    private void BeginObjectGesture(in EditorInputFrame frame, SceneNode node)
    {
        PressedNode = node;
        SelectionUpdate update = SelectionModifiers.Resolve(frame.Modifiers);

        if (update == SelectionUpdate.Replace && Scene.Selection.Contains(node))
        {
            _deferredSelect = node;
        }
        else
        {
            _deferredSelect = null;
            ApplySingle(node, update);
        }

        GizmoTool tool = Gizmos.Active;
        if (ObjectDragEnabled && tool.TryBeginDrag(in frame, tool.FreeMoveHandle))
        {
            SetMode(ViewportDragMode.SelectAndMove);
            return;
        }

        // No drag started (rotate and resize have no free-move handle), so no
        // release will come through the gizmo: collapse now.
        ResolveDeferredSelect();
        PressedNode = null;
        SetMode(ViewportDragMode.None);
    }

    private void ApplySingle(SceneNode node, SelectionUpdate update)
    {
        switch (update)
        {
            case SelectionUpdate.Add:
                Scene.Selection.Add(node);
                break;
            case SelectionUpdate.Toggle:
                Scene.Selection.Toggle(node);
                break;
            default:
                Scene.Selection.Select(node);
                break;
        }
    }

    private void UpdateBoxSelect(in EditorInputFrame frame, bool cancelRequested)
    {
        // A locked cursor has no position to track, so cancel. Should not
        // happen: the camera never sees a press while the marquee is live.
        BoxSelectResult result = BoxSelect.Update(in frame, cancelRequested || frame.IsCursorLocked);
        if (result == BoxSelectResult.Dragging)
            return;

        SetMode(ViewportDragMode.None);
    }

    private void FinishGizmoGesture(GizmoUpdateResult result, bool cancelRequested)
    {
        if (result is GizmoUpdateResult.DragBegan or GizmoUpdateResult.DragUpdated)
            return;

        // DragCancelled without an abort means it never moved: a click, so
        // collapse. A committed drag and an Escape both leave the selection alone.
        if (result == GizmoUpdateResult.DragCancelled && !cancelRequested)
            ResolveDeferredSelect();

        _deferredSelect = null;
        PressedNode = null;
        SetMode(ViewportDragMode.None);
    }

    private void ResolveDeferredSelect()
    {
        // The node may have been deleted between press and release.
        if (_deferredSelect is { } node && Scene.TryFindById(node.Id, out SceneNode? live) && ReferenceEquals(live, node))
            Scene.Selection.Select(node);

        _deferredSelect = null;

        // After the selection settles: SelectFace only takes the sole selected node.
        PickPressedFace();
    }

    // A click on anything but a brush clears the picked face.
    private void PickPressedFace()
    {
        int plane = _pressedPlane;
        _pressedPlane = -1;

        if (plane < 0 || PressedNode is not { Brush: not null } node)
        {
            Scene.Selection.ClearFace();
            return;
        }

        if (!Scene.Selection.SelectFace(node, plane))
            Scene.Selection.ClearFace();
    }

    private bool TryPickNode(in EditorInputFrame frame, out SceneNode? node) =>
        TryPickNode(in frame, out node, out _);

    private bool TryPickNode(in EditorInputFrame frame, out SceneNode? node, out int planeIndex)
    {
        node = null;
        planeIndex = -1;
        if (frame.ViewportSize.X <= 0f || frame.ViewportSize.Y <= 0f)
            return false;

        Ray3 ray = Scene.Camera.ScreenPointToRay(frame.CursorPosition, frame.ViewportSize);

        // Editor picking ignores CanQuery: anything visible must be selectable.
        bool geometry = Scene.Raycast(
            in ray, out SceneRaycastHit hit, SceneQueryFilter.EditorPicking, PickDistance);

        // Lights and point entities are not in the spatial index (they would
        // become collidable), so their icons are picked separately.
        if (!TryPickIcon(in ray, frame.ViewportSize, out SceneNode? icon, out float iconDistance))
        {
            node = geometry ? hit.Node : null;
            planeIndex = geometry ? hit.PlaneIndex : -1;
            return geometry;
        }

        // The icon wins unless geometry is nearer by more than the icon's
        // radius. It draws on top, and a lamp flush against a ceiling would
        // otherwise be unclickable.
        if (geometry)
        {
            float slack = LightPicking.WorldRadius(Scene.Camera, frame.ViewportSize, icon!.WorldPosition);
            if (iconDistance > hit.Distance + slack)
            {
                node = hit.Node;
                planeIndex = hit.PlaneIndex;
                return true;
            }
        }

        node = icon;
        return true;
    }

    // The nearer of a light's icon and an entity's marker.
    private bool TryPickIcon(in Ray3 ray, Vector2 viewportSize, out SceneNode? node, out float distance)
    {
        bool lamp = LightPicking.TryPick(Scene, Scene.Camera, in ray, viewportSize, out node, out distance);

        bool marker = EntityMarkerPicking.TryPick(
            Scene, Scene.Camera, in ray, viewportSize, out SceneNode? marked, out float markerDistance);

        if (!marker || (lamp && distance <= markerDistance))
            return lamp;

        node = marked;
        distance = markerDistance;
        return true;
    }

    private void SetMode(ViewportDragMode mode)
    {
        if (DragMode == mode)
            return;

        DragMode = mode;
        DragModeChanged?.Invoke(mode);
    }
}
