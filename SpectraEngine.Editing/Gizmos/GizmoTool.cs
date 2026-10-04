using SpectraEngine.Core.Graphics;
using SpectraEngine.Core.Input;
using SpectraEngine.Core.Scene;
using SpectraEngine.Editing.Input;
using SpectraEngine.Editing.Undo;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace SpectraEngine.Editing.Gizmos;

/// <summary>
/// What the move, rotate and resize tools share: pivot and frame, constant
/// screen size, hit testing, the grab, drag, commit or cancel state machine,
/// and one undo transaction per gesture. Render thread only.
/// </summary>
// Each drag frame recomputes from the grab capture, never from the previous
// frame, so a cancel restores the selection exactly.
public abstract class GizmoTool
{
    // Kept across gestures to avoid reallocating per drag.
    private readonly List<GizmoDragTarget> _targets = [];

    private GizmoGeometry _geometry;
    private GizmoStyle _style = GizmoStyle.Studio;
    private bool _hasGeometry;

    private GizmoInteractionState _state = GizmoInteractionState.Idle;
    private GizmoHandle _hovered = GizmoHandle.None;
    private GizmoHandle _active = GizmoHandle.None;

    private Vector3 _grabPivot;
    private Vector3 _livePivot;
    private Quaternion _grabFrame = Quaternion.Identity;

    /// <summary>
    /// Creates a tool over a scene and the history its edits land in.
    /// <paramref name="undo"/> must be the history for <paramref name="scene"/>.
    /// </summary>
    protected GizmoTool(Scene scene, UndoStack undo, string transactionName)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(undo);
        ArgumentNullException.ThrowIfNull(transactionName);
        if (!ReferenceEquals(undo.Scene, scene))
        {
            throw new ArgumentException(
                $"The undo history edits scene '{undo.Scene.Name}', not '{scene.Name}'.", nameof(undo));
        }

        Scene = scene;
        Undo = undo;
        TransactionName = transactionName;
    }

    /// <summary>The scene this gizmo manipulates.</summary>
    public Scene Scene { get; }

    /// <summary>The history each drag lands in.</summary>
    public UndoStack Undo { get; }

    /// <summary>Which manipulator this is.</summary>
    public abstract GizmoMode Mode { get; }

    /// <summary>
    /// Whether <see cref="Orientation"/> applies to this tool. False for
    /// <see cref="ScaleGizmo"/>, which is local-only.
    /// </summary>
    public virtual bool SupportsOrientation => true;

    /// <summary>
    /// The manipulator style. Defaults to <see cref="GizmoStyle.Studio"/>.
    /// Set it between gestures, not during a drag.
    /// </summary>
    public GizmoStyle Style
    {
        get => _style;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            _style = value;
        }
    }

    /// <summary>
    /// The frame the handles are laid out in. Ignored when
    /// <see cref="SupportsOrientation"/> is false.
    /// </summary>
    public GizmoOrientation Orientation { get; set; } = GizmoOrientation.World;

    /// <summary>
    /// The length of one axis handle in pixels, constant at any camera distance.
    /// </summary>
    public float HandlePixelSize { get; set; } = GizmoGeometry.DefaultPixelSize;

    /// <summary>How close in pixels the cursor must come to a line-shaped handle to pick it.</summary>
    public float PickTolerancePixels { get; set; } = GizmoHitTesting.DefaultTolerancePixels;

    /// <summary>The button that grabs a handle and, on release, commits the drag.</summary>
    public PointerButtons DragButton { get; set; } = PointerButtons.Left;

    /// <summary>The button that cancels an in-progress drag.</summary>
    public PointerButtons CancelButton { get; set; } = PointerButtons.Right;

    /// <summary>The label a committed drag carries into the undo menu.</summary>
    public string TransactionName { get; set; }

    /// <summary>Where the gizmo is in its interaction cycle.</summary>
    public GizmoInteractionState State => _state;

    /// <summary>The handle under the cursor, or <see cref="GizmoHandle.None"/>.</summary>
    public GizmoHandle HoveredHandle => _hovered;

    /// <summary>The handle being dragged, or <see cref="GizmoHandle.None"/>.</summary>
    public GizmoHandle ActiveHandle => _active;

    /// <summary>True when the last <see cref="Update"/> produced drawable geometry.</summary>
    public bool IsVisible => _hasGeometry;

    /// <summary>
    /// The pivot the last <see cref="Update"/> placed the gizmo at.
    /// <see cref="Vector3.Zero"/> before the first <see cref="Update"/>.
    /// </summary>
    public Vector3 Pivot => _livePivot;

    /// <summary>
    /// The last frame's geometry, the same struct picking used. Meaningless
    /// when <see cref="IsVisible"/> is false.
    /// </summary>
    public GizmoGeometry Geometry => _geometry;

    /// <summary>How many nodes the current drag is manipulating. Zero when idle.</summary>
    public int DragTargetCount => _targets.Count;

    /// <summary>The nodes the current drag is manipulating, in capture order.</summary>
    protected IReadOnlyList<GizmoDragTarget> Targets => _targets;

    /// <summary>The pivot the current drag was grabbed at.</summary>
    protected Vector3 GrabPivot => _grabPivot;

    /// <summary>The frame rotation the current drag was grabbed in.</summary>
    // Frozen at the grab: a live frame would follow a rotating selection.
    protected Quaternion GrabFrame => _grabFrame;

    /// <summary>
    /// Where the gizmo is drawn during the drag. Defaults to the grab pivot.
    /// </summary>
    protected virtual Vector3 LivePivot => _grabPivot;

    /// <summary>
    /// Whether the current drag has changed anything. A gesture that ends with
    /// this false cancels instead of committing.
    /// </summary>
    protected abstract bool HasEdit { get; }

    /// <summary>
    /// Advances the gizmo by one frame: hover, grab, drag, and commit or cancel.
    /// </summary>
    /// <param name="cancelRequested">
    /// True on the frame the user asked to abort, such as Escape or a lost focus.
    /// The host owns the keymap, so this is not part of the input frame.
    /// </param>
    /// <param name="pointerAvailable">
    /// False when something else claimed this frame's press, such as camera
    /// navigation. Hover still updates and a live drag is unaffected.
    /// </param>
    public GizmoUpdateResult Update(in EditorInputFrame frame, bool cancelRequested = false, bool pointerAvailable = true)
    {
        if (_state == GizmoInteractionState.Dragging)
            return UpdateDrag(in frame, cancelRequested);

        return UpdateHover(in frame, pointerAvailable);
    }

    /// <summary>
    /// Hit-tests the gizmo at this frame's cursor without changing the tool's
    /// state. Works on a frame where <see cref="Update"/> has not run.
    /// </summary>
    public GizmoPick PickAt(in EditorInputFrame frame)
    {
        if (Scene.Selection.Count == 0 ||
            frame.ViewportSize.X <= 0f || frame.ViewportSize.Y <= 0f ||
            !frame.IsPointerUsable)
        {
            return GizmoPick.Miss;
        }

        Quaternion frameRotation = FrameRotation();
        GizmoGeometry geometry = BuildGeometry(
            ResolveLayout(frameRotation), frameRotation, frame.ViewportSize);
        Ray3 ray = Scene.Camera.ScreenPointToRay(frame.CursorPosition, frame.ViewportSize);
        return HitTest(in geometry, in ray, PickTolerancePixels);
    }

    /// <summary>
    /// The geometry this tool would lay out for the current selection at the
    /// given viewport size, without changing its state. Default geometry for an
    /// empty selection or a viewport with no area.
    /// </summary>
    public GizmoGeometry GeometryFor(Vector2 viewportSize)
    {
        if (Scene.Selection.Count == 0 || viewportSize.X <= 0f || viewportSize.Y <= 0f)
            return default;

        Quaternion frameRotation = FrameRotation();
        return BuildGeometry(ResolveLayout(frameRotation), frameRotation, viewportSize);
    }

    /// <summary>
    /// The handle a press on an object, rather than on the gizmo, drags with.
    /// <see cref="GizmoHandle.None"/> for a tool with no free move, which
    /// leaves the press a plain click-select.
    /// </summary>
    public virtual GizmoHandle FreeMoveHandle => GizmoHandle.None;

    /// <summary>
    /// Starts a drag on <paramref name="handle"/> as if the user had grabbed it
    /// this frame, wherever the cursor is. False, with no transaction opened,
    /// when a drag is already live, nothing is selected, or the tool refuses.
    /// </summary>
    public bool TryBeginDrag(in EditorInputFrame frame, GizmoHandle handle)
    {
        if (_state == GizmoInteractionState.Dragging || handle == GizmoHandle.None)
            return false;

        // The caller may have just changed the selection, so rebuild here
        // rather than trust the hover pass.
        if (!TryBuildGeometry(in frame))
            return false;

        _hovered = handle;
        return BeginDrag(in frame) == GizmoUpdateResult.DragBegan;
    }

    /// <summary>
    /// Draws the gizmo, highlighting the active handle while dragging and the
    /// hovered one otherwise.
    /// </summary>
    public void Draw(DebugDraw output)
    {
        ArgumentNullException.ThrowIfNull(output);
        if (!_hasGeometry || _geometry.IsBehindCamera || _geometry.AxisLength <= 0f)
            return;

        GizmoHandle highlighted = _state == GizmoInteractionState.Dragging ? _active : _hovered;
        DrawHandles(output, in _geometry, highlighted);
    }

    /// <summary>
    /// Aborts an in-progress drag, restoring every node to its state at the
    /// grab. False when no drag was in progress.
    /// </summary>
    public bool CancelDrag()
    {
        if (_state != GizmoInteractionState.Dragging)
            return false;

        Undo.CancelTransaction();
        EndDrag();
        return true;
    }

    /// <summary>
    /// Cancels any drag, forgets the hover and drops the cached geometry.
    /// Call it on a mode switch.
    /// </summary>
    // A tool left mid-drag keeps its transaction open, and the next tool's
    // BeginTransaction throws because transactions do not nest.
    public void Reset()
    {
        CancelDrag();
        _hovered = GizmoHandle.None;
        _active = GizmoHandle.None;
        _hasGeometry = false;
        _state = GizmoInteractionState.Idle;
    }

    /// <summary>Picks the handle this tool's shape puts under <paramref name="ray"/>.</summary>
    protected abstract GizmoPick HitTest(in GizmoGeometry geometry, in Ray3 ray, float tolerancePixels);

    /// <summary>
    /// Sets up the constraint for a grab on <see cref="ActiveHandle"/>.
    /// Returning false refuses the gesture and no transaction is opened.
    /// <see cref="Targets"/> is already populated and non-empty.
    /// </summary>
    protected abstract bool TryPrepareDrag(in EditorInputFrame frame, in Ray3 ray);

    /// <summary>
    /// Records this tool's per-node commands into the transaction the base just
    /// opened. Record, do not execute: nothing has moved yet, and the drag
    /// applies later values through the same command objects.
    /// </summary>
    protected abstract void RecordCommands();

    /// <summary>
    /// Applies one frame of the drag, recomputed from the grab capture. If the
    /// cursor ray cannot be projected onto the constraint, keep the last value.
    /// </summary>
    protected abstract void ApplyDrag(in EditorInputFrame frame, in Ray3 ray);

    /// <summary>Clears the tool's per-gesture state beyond <see cref="Targets"/>.</summary>
    protected abstract void ClearDragState();

    /// <summary>Draws this tool's handles.</summary>
    protected abstract void DrawHandles(DebugDraw output, in GizmoGeometry geometry, GizmoHandle highlighted);

    /// <summary>
    /// The node a local-frame gizmo aligns to: the most recently selected one.
    /// Null for an empty selection.
    /// </summary>
    public SceneNode? ReferenceNode
    {
        get
        {
            IReadOnlyList<SceneNode> items = Scene.Selection.Items;
            return items.Count == 0 ? null : items[items.Count - 1];
        }
    }

    /// <summary>
    /// The rotation the handles are laid out in this frame: identity in world
    /// orientation, the reference node's world rotation in local.
    /// </summary>
    protected virtual Quaternion FrameRotation()
    {
        if (Orientation == GizmoOrientation.World || ReferenceNode is not { } node)
            return Quaternion.Identity;

        return WorldRotationOf(node);
    }

    /// <summary>
    /// A node's world rotation, decomposed from its world matrix. Identity when
    /// the matrix cannot be decomposed (a zero scale in the chain).
    /// </summary>
    protected static Quaternion WorldRotationOf(SceneNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        return Matrix4x4.Decompose(node.WorldMatrix, out _, out Quaternion rotation, out _)
            ? rotation
            : Quaternion.Identity;
    }

    private GizmoUpdateResult UpdateHover(in EditorInputFrame frame, bool pointerAvailable)
    {
        _hovered = GizmoHandle.None;
        _active = GizmoHandle.None;

        if (!TryBuildGeometry(in frame))
        {
            _state = GizmoInteractionState.Idle;
            return GizmoUpdateResult.None;
        }

        // A locked cursor has no position, and one outside the viewport is
        // over another panel.
        if (frame.IsPointerUsable)
        {
            Ray3 ray = Scene.Camera.ScreenPointToRay(frame.CursorPosition, frame.ViewportSize);
            _hovered = HitTest(in _geometry, in ray, PickTolerancePixels).Handle;
        }

        if (pointerAvailable && _hovered != GizmoHandle.None && frame.WasPressed(DragButton))
            return BeginDrag(in frame);

        _state = _hovered != GizmoHandle.None ? GizmoInteractionState.Hovering : GizmoInteractionState.Idle;
        return _state == GizmoInteractionState.Hovering ? GizmoUpdateResult.Hovering : GizmoUpdateResult.None;
    }

    private bool TryBuildGeometry(in EditorInputFrame frame)
    {
        if (Scene.Selection.Count == 0 || frame.ViewportSize.X <= 0f || frame.ViewportSize.Y <= 0f)
        {
            _hasGeometry = false;
            return false;
        }

        Quaternion frameRotation = FrameRotation();
        GizmoLayout layout = ResolveLayout(frameRotation);
        _geometry = BuildGeometry(layout, frameRotation, frame.ViewportSize);
        _livePivot = layout.Pivot;
        _hasGeometry = true;
        return true;
    }

    private GizmoGeometry BuildGeometry(in GizmoLayout layout, Quaternion frameRotation, Vector2 viewportSize) =>
        GizmoGeometry.Build(
            Scene.Camera,
            layout.Pivot,
            frameRotation,
            viewportSize,
            HandlePixelSize,
            _style,
            Mode,
            layout.PositiveExtent,
            layout.NegativeExtent);

    private readonly record struct GizmoLayout(
        Vector3 Pivot, Vector3 PositiveExtent, Vector3 NegativeExtent);

    // Pivot and handle reach from one measurement of the selection.
    // forcedPivot is the drag's live pivot. The box is measured relative to
    // it, so a handle stays on its face while a face-anchored resize moves
    // geometry away from a stationary pivot.
    private GizmoLayout ResolveLayout(Quaternion frameRotation, Vector3? forcedPivot = null)
    {
        bool needsBox = _style.HandlesStandOffBounds || _style.PivotMode == GizmoPivotMode.BoundsCentre;
        if (!needsBox)
            return new GizmoLayout(forcedPivot ?? SelectionOriginAverage(), Vector3.Zero, Vector3.Zero);

        FrameAxes(frameRotation, out Vector3 axisX, out Vector3 axisY, out Vector3 axisZ);
        if (!GizmoSelectionBounds.TryMeasure(
                Scene.Selection.Items, axisX, axisY, axisZ, out Vector3 min, out Vector3 max))
        {
            return new GizmoLayout(forcedPivot ?? SelectionOriginAverage(), Vector3.Zero, Vector3.Zero);
        }

        Vector3 pivot = forcedPivot ?? (_style.PivotMode == GizmoPivotMode.BoundsCentre
            ? GizmoSelectionBounds.ToWorld((min + max) * 0.5f, axisX, axisY, axisZ)
            : SelectionOriginAverage());

        if (!_style.HandlesStandOffBounds)
            return new GizmoLayout(pivot, Vector3.Zero, Vector3.Zero);

        var pivotInFrame = new Vector3(
            Vector3.Dot(pivot, axisX), Vector3.Dot(pivot, axisY), Vector3.Dot(pivot, axisZ));

        return new GizmoLayout(pivot, max - pivotInFrame, pivotInFrame - min);
    }

    private static void FrameAxes(Quaternion frameRotation, out Vector3 axisX, out Vector3 axisY, out Vector3 axisZ)
    {
        if (frameRotation == Quaternion.Identity)
        {
            axisX = Vector3.UnitX;
            axisY = Vector3.UnitY;
            axisZ = Vector3.UnitZ;
            return;
        }

        axisX = Vector3.Transform(Vector3.UnitX, frameRotation);
        axisY = Vector3.Transform(Vector3.UnitY, frameRotation);
        axisZ = Vector3.Transform(Vector3.UnitZ, frameRotation);
    }

    // Averages the whole selection, including nodes whose selected ancestor
    // carries them: the pivot is where the user sees the selection.
    private Vector3 SelectionOriginAverage()
    {
        IReadOnlyList<SceneNode> items = Scene.Selection.Items;
        if (items.Count == 0)
            return Vector3.Zero;

        Vector3 sum = Vector3.Zero;
        for (int i = 0; i < items.Count; i++)
            sum += items[i].WorldPosition;

        return sum / items.Count;
    }

    private GizmoUpdateResult BeginDrag(in EditorInputFrame frame)
    {
        _active = _hovered;
        _grabPivot = _geometry.Pivot;
        _grabFrame = FrameRotation();

        CaptureTargets();

        Ray3 ray = Scene.Camera.ScreenPointToRay(frame.CursorPosition, frame.ViewportSize);
        if (_targets.Count == 0 || !TryPrepareDrag(in frame, in ray))
        {
            // Nothing to manipulate, or no anchor for the drag. Refuse before
            // a transaction opens.
            _targets.Clear();
            _active = GizmoHandle.None;
            _state = GizmoInteractionState.Hovering;
            return GizmoUpdateResult.Hovering;
        }

        Undo.BeginTransaction(TransactionName);
        RecordCommands();

        _livePivot = _grabPivot;
        _state = GizmoInteractionState.Dragging;
        return GizmoUpdateResult.DragBegan;
    }

    private void CaptureTargets()
    {
        _targets.Clear();

        IReadOnlyList<SceneNode> items = Scene.Selection.Items;
        for (int i = 0; i < items.Count; i++)
        {
            SceneNode node = items[i];

            // A selected ancestor already carries this node. Editing both
            // would apply the edit twice.
            if (HasSelectedAncestor(node))
                continue;

            _targets.Add(GizmoDragTarget.Capture(node));
        }
    }

    private bool HasSelectedAncestor(SceneNode node)
    {
        for (SceneNode? parent = node.Parent; parent is not null; parent = parent.Parent)
        {
            if (Scene.Selection.Contains(parent))
                return true;
        }
        return false;
    }

    private GizmoUpdateResult UpdateDrag(in EditorInputFrame frame, bool cancelRequested)
    {
        if (cancelRequested || frame.WasPressed(CancelButton))
        {
            Undo.CancelTransaction();
            EndDrag();
            return GizmoUpdateResult.DragCancelled;
        }

        if (frame.WasReleased(DragButton))
            return CommitDrag();

        // Zero-size viewport mid-drag (minimised window): ScreenPointToRay
        // would return NaN. Hold the drag. Hosts are not required to guard this.
        if (frame.ViewportSize.X <= 0f || frame.ViewportSize.Y <= 0f)
            return GizmoUpdateResult.DragUpdated;

        Ray3 ray = Scene.Camera.ScreenPointToRay(frame.CursorPosition, frame.ViewportSize);
        ApplyDrag(in frame, in ray);

        // Rebuild at the live pivot in the frozen grab frame. The box is
        // re-measured so handles follow their faces. The drag math never reads
        // this geometry, so there is no feedback loop.
        _livePivot = LivePivot;
        _geometry = BuildGeometry(
            ResolveLayout(_grabFrame, _livePivot), _grabFrame, frame.ViewportSize);
        _hasGeometry = true;

        return GizmoUpdateResult.DragUpdated;
    }

    private GizmoUpdateResult CommitDrag()
    {
        // A grab that changed nothing is a click. Cancel so the history
        // gets no no-op entry.
        bool edited = HasEdit;
        if (edited)
            Undo.CommitTransaction();
        else
            Undo.CancelTransaction();

        EndDrag();
        return edited ? GizmoUpdateResult.DragCommitted : GizmoUpdateResult.DragCancelled;
    }

    private void EndDrag()
    {
        ClearDragState();
        _targets.Clear();
        _active = GizmoHandle.None;
        // Idle, not Hovering: the next frame's hit test decides.
        _state = GizmoInteractionState.Idle;
    }
}
