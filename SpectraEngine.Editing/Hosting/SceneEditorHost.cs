using Microsoft.Extensions.Logging;
using SpectraEngine.Core.Assets;
using SpectraEngine.Core.Bsp;
using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Graphics;
using SpectraEngine.Core.Input;
using SpectraEngine.Core.Scene;
using SpectraEngine.Editing.Cameras;
using SpectraEngine.Editing.Gizmos;
using SpectraEngine.Editing.Input;
using SpectraEngine.Editing.Undo;
using SpectraEngine.Core.Inspection;
using SpectraEngine.Editing.Commands;
using SpectraEngine.Editing.Viewport;
using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;

namespace SpectraEngine.Editing.Hosting;

/// <summary>
/// The engine's scene editor: the per-frame input snapshot, the viewport's
/// pick/gizmo/marquee arbitration, the editor camera, the undo history and the
/// keyboard that drives them. Render thread only.
/// </summary>
// Camera movement keys are fed to the editor camera only while the look button
// is held, so W/E mean "switch tool" the rest of the time. The engine's fly
// camera reads the keyboard itself, so while it drives, the conflicting
// letter-row tool keys stand down.
public sealed class SceneEditorHost : ISceneEditor
{
    private const InputKey NavigationToggleKey = InputKey.F7;

    // With Control held, so the bare letter stays free for tool switching.
    private const InputKey BrushKindToggleKey = InputKey.T;

    // Constants, not formatted enums: the stats line reading these must not allocate.
    private const string EditorNavigationLabel = "editor freelook";
    private const string FlyCameraNavigationLabel = "fly camera";

    // Offered to the editing layer's default table; unrecognised keys are dropped.
    private static readonly InputKey[] GizmoKeyCandidates =
    [
        InputKey.W, InputKey.E, InputKey.R,
        InputKey.Number2, InputKey.Number3, InputKey.Number4,
        InputKey.X, InputKey.Y, InputKey.G, InputKey.LeftBracket, InputKey.RightBracket,
    ];

    // Keys a camera owns while it is driving.
    private static readonly InputKey[] CameraKeys =
    [
        InputKey.W, InputKey.A, InputKey.S, InputKey.D, InputKey.Q, InputKey.E,
        InputKey.Space, InputKey.ControlLeft, InputKey.ShiftLeft,
    ];

    private readonly ILogger<SceneEditorHost> _logger;
    private readonly Scene _scene;
    private readonly Renderer _renderer;
    private readonly InputManager _input;

    private readonly UndoStack _undo;
    private readonly GizmoController _gizmos;
    private readonly EditorCameraController _camera;
    private readonly ViewportInteractionController _viewport;
    private readonly EngineEditorInputSource _inputSource;
    private readonly GizmoBinding[] _gizmoBindings;
    private readonly IEditorFrameProbe? _probe;

    private readonly PartBrushOverlay _partOutlines = new();

    // Subtractive brushes render nothing, so this is the only way to see one.
    private readonly SubtractiveBrushOverlay _negativeOutlines = new();

    // A part with its render bit off draws nothing either.
    private readonly VolumeOverlay _volumeOutlines = new();

    // An entity with no brush, mesh or light has nothing else to see or click.
    private readonly EntityMarkerOverlay _entityMarkers = new();

    /// <summary>The ground grid and the world axes, drawn depth-tested.</summary>
    public GroundGrid Grid { get; } = new();

    /// <summary>
    /// When the grid shows. <see cref="Viewport.GridMode.Auto"/> by default:
    /// only during move and resize gestures.
    /// </summary>
    public GridMode GridMode { get; set; } = GridMode.Auto;

    // 0..1 fade written into Grid.Opacity each frame, so the grid does not pop.
    private float _gridOpacity;

    private const float GridFadeInSeconds = 0.10f;
    private const float GridFadeOutSeconds = 0.25f;

    /// <summary>The corner axis widget, drawn on top like the manipulators.</summary>
    public AxisCompass Compass { get; } = new();

    /// <summary>The outline of what is selected and what the cursor is over.</summary>
    public SelectionOutline Selection { get; } = new();

    /// <summary>Every light's icon, and the selected lights' shapes.</summary>
    public LightOverlay Lights { get; } = new();

    /// <summary>The handles for a light's shape and aim.</summary>
    public LightGizmo LightGizmo { get; }

    private Vector2 _viewportSize;

    private readonly ICursorShape _cursorShape;
    private CursorShape _lastCursorShape = CursorShape.Arrow;
    private bool _editorNavigation = true;

    /// <summary>Builds an editor over a freshly loaded scene.</summary>
    /// <param name="probe">Optional per-frame instrumentation, see <see cref="IEditorFrameProbe"/>.</param>
    public SceneEditorHost(
        ILoggerFactory loggerFactory,
        Scene scene,
        Renderer renderer,
        InputManager input,
        IEditorFrameProbe? probe = null)
    {
        ArgumentNullException.ThrowIfNull(loggerFactory);
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(renderer);
        ArgumentNullException.ThrowIfNull(input);

        _logger = loggerFactory.CreateLogger<SceneEditorHost>();
        _scene = scene;
        _renderer = renderer;
        _input = input;

        // One orange for the silhouette and for the face loops drawn as lines.
        Selection.Silhouettes = new OutlineMeshes(renderer);
        renderer.Outlines.SelectedColor = SelectionOutline.SelectedColor;
        renderer.Outlines.HoveredColor = SelectionOutline.SelectedColor;

        _undo = new UndoStack(scene);
        _gizmos = new GizmoController(scene, _undo);
        _gizmos.Scale.Logger = loggerFactory.CreateLogger<ScaleGizmo>();
        _camera = new EditorCameraController(scene) { CursorLock = input };
        _cursorShape = input;
        // Shares the move and rotate ladders: a range is a length, a cone an angle.
        LightGizmo = new LightGizmo(scene, _undo)
        {
            Snap = _gizmos.Translate.Snap,
            AngleSnap = _gizmos.Rotate.Snap,
        };

        _viewport = new ViewportInteractionController(scene, _gizmos)
        {
            CameraController = _camera,
            LightTool = LightGizmo,
        };
        _inputSource = new EngineEditorInputSource(input, renderer);
        _gizmoBindings = BuildGizmoBindings();

        // Seeded so a pick on frame one has a real viewport size.
        renderer.GetFramebufferSize(out int width, out int height);
        _viewportSize = new Vector2(width, height);

        _probe = probe;

        _logger.LogInformation(
            "Editor viewport ready: hold right mouse to lock the cursor and look, W/A/S/D + Q/E " +
            "(or Space/Ctrl) fly while you do, Shift boosts, the wheel trims fly speed while " +
            "looking and zooms otherwise; Alt+drag orbits, middle-drag pans, F frames the " +
            "selection; left-drag picks/moves, marquee on empty space; W/E/R or 2/3/4 pick the " +
            "tool, X flips world/local, Y flips the handle style, G and [ ] drive snap, " +
            "Ctrl+Z / Ctrl+Y walk {Capacity} " +
            "entries of history; Ctrl+D duplicates, Delete removes, Ctrl+G groups and Ctrl+Shift+G " +
            "ungroups the selection; Ctrl+T converts the selected brushes between world geometry and " +
            "parts (parts leave the CSG carve, so they stop merging with what they touch and cost " +
            "no recompile when they move — they are outlined in cyan); F7 toggles between the " +
            "editor camera and the engine fly camera (starting: {Mode}, gizmos: {Gizmos})",
            _undo.Capacity, NavigationModeName, $"{GizmoModeName}/{GizmoStyleName}");
    }

    /// <inheritdoc/>
    public int SelectionCount => _scene.Selection.Count;

    /// <inheritdoc/>
    public string GizmoModeName => _gizmos.Mode switch
    {
        GizmoMode.Rotate => "rotate",
        GizmoMode.Scale => "resize",
        _ => "move",
    };

    /// <inheritdoc/>
    public string GizmoStyleName =>
        _gizmos.Style.Kind == GizmoStyleKind.Classic ? "Classic" : "Studio";

    /// <inheritdoc/>
    public string GizmoOrientationName =>
        _gizmos.Orientation == GizmoOrientation.Local ? "local" : "world";

    /// <inheritdoc/>
    public bool SnapEnabled => _gizmos.SnapEnabled;

    /// <inheritdoc/>
    // The property gesture counts: an inspector scrub moves the object without
    // the viewport knowing.
    public bool IsInteracting =>
        _viewport.DragMode != ViewportDragMode.None || _propertyGestureOpen;

    /// <inheritdoc/>
    public float SnapIncrement => _gizmos.Mode switch
    {
        GizmoMode.Rotate => _gizmos.Rotate.Snap.Increment,
        GizmoMode.Scale => _gizmos.Scale.Snap.Increment,
        _ => _gizmos.Translate.Snap.Increment,
    };

    /// <inheritdoc/>
    public float MoveSnapIncrement => _gizmos.Translate.Snap.Increment;

    /// <inheritdoc/>
    public float RotateSnapIncrement => _gizmos.Rotate.Snap.Increment;

    /// <inheritdoc/>
    public float ResizeSnapIncrement => _gizmos.Scale.Snap.Increment;

    /// <inheritdoc/>
    public string NavigationModeName => _editorNavigation ? EditorNavigationLabel : FlyCameraNavigationLabel;

    /// <inheritdoc/>
    public string GridModeName => GridMode switch
    {
        GridMode.On => "on",
        GridMode.Off => "off",
        _ => "auto",
    };

    // Constants: read once per snapshot, so no formatting.
    private const string SuspendedState = "suspended";
    private const string FlyState = "fly";
    private const string LookState = "look";
    private const string OrbitState = "orbit";
    private const string PanState = "pan";
    private const string DragManipulateState = "drag-manipulate";
    private const string DragMoveState = "drag-move";
    private const string DragBoxState = "drag-box";
    private const string HoverHandleState = "hover-handle";
    private const string HoverObjectState = "hover-object";
    private const string HoverEmptyState = "hover-empty";
    private const string IdleState = "idle";

    /// <inheritdoc/>
    // Same order as the press arbitration: suspension, camera, live drag, hover.
    public string InteractionStateName
    {
        get
        {
            if (IsSuspended) return SuspendedState;
            if (!_editorNavigation) return FlyState;

            if (_camera.IsFreeLooking) return LookState;
            if (_camera.IsOrbiting) return OrbitState;
            if (_camera.IsPanning) return PanState;

            switch (_viewport.DragMode)
            {
                case ViewportDragMode.Manipulate: return DragManipulateState;
                case ViewportDragMode.SelectAndMove: return DragMoveState;
                case ViewportDragMode.BoxSelect: return DragBoxState;
            }

            return _viewport.HoverMode switch
            {
                ViewportDragMode.Manipulate => HoverHandleState,
                ViewportDragMode.SelectAndMove => HoverObjectState,
                ViewportDragMode.BoxSelect => HoverEmptyState,
                _ => IdleState,
            };
        }
    }

    /// <inheritdoc/>
    public int UndoDepth => _undo.UndoCount;

    /// <inheritdoc/>
    public int RedoDepth => _undo.RedoCount;

    /// <inheritdoc/>
    public bool Update(double deltaTime)
    {
        _renderer.GetFramebufferSize(out int width, out int height);
        if (width <= 0 || height <= 0)
        {
            // Minimized: no viewport to hit-test, so abandon any live gesture.
            _viewport.Reset();
            return _editorNavigation;
        }

        _viewportSize = new Vector2(width, height);

        HandleShortcuts();

        EditorInputFrame frame = _inputSource.CaptureFrame((float)deltaTime, CaptureNavigation());

        // The probe runs before the viewport and is told "not idle" on the frame
        // a press arrives. The self-test leaves its node displaced while it
        // waits for a compile; a gizmo grab must not capture that as its start.
        bool viewportIdle =
            _viewport.DragMode == ViewportDragMode.None && !frame.WasPressed(_viewport.DragButton);
        _probe?.Update(deltaTime, _viewportSize, viewportIdle);

        // Escape is a cancel flag, not a GizmoCommand: it must reach the marquee too.
        bool escape = _input.WasKeyPressed(InputKey.Escape);
        _viewport.Update(in frame, escape);

        // With no drag to cancel, Escape clears the selection.
        if (escape && viewportIdle && _scene.Selection.Count > 0)
            ClearSelection();
        UpdateCursorShape();
        TrackSelectMoveTravel(in frame);
        UpdateGridFade((float)deltaTime);

        return _editorNavigation;
    }

    // Gestures that snap on the move grid: move and resize drags, a
    // select-and-move that has travelled, a light's length handle. Not rotate.
    private bool MoveGestureLive => _viewport.DragMode switch
    {
        ViewportDragMode.SelectAndMove => _selectMoveTravelled,
        ViewportDragMode.Manipulate => LightGizmo.IsDragging
            ? LightGizmo.IsDraggingLength
            : _gizmos.Mode is GizmoMode.Translate or GizmoMode.Scale,
        _ => false,
    };

    // SelectAndMove starts on the press, so a plain click would flash the grid.
    // Wait for real travel. A handle grab shows it at once.
    private const float SelectMoveTravelPixels = 4f;
    private Vector2 _selectMoveAnchor;
    private bool _selectMoveAnchorValid;
    private bool _selectMoveTravelled;

    private void TrackSelectMoveTravel(in EditorInputFrame frame)
    {
        if (_viewport.DragMode != ViewportDragMode.SelectAndMove)
        {
            _selectMoveAnchorValid = false;
            _selectMoveTravelled = false;
            return;
        }

        if (!_selectMoveAnchorValid)
        {
            _selectMoveAnchor = frame.CursorPosition;
            _selectMoveAnchorValid = true;
            return;
        }

        if (!_selectMoveTravelled &&
            Vector2.Distance(frame.CursorPosition, _selectMoveAnchor) > SelectMoveTravelPixels)
        {
            _selectMoveTravelled = true;
        }
    }

    private void UpdateGridFade(float dt)
    {
        float target = GridMode switch
        {
            GridMode.On => 1f,
            GridMode.Off => 0f,
            _ => MoveGestureLive ? 1f : 0f,
        };

        float step = target > _gridOpacity
            ? dt / GridFadeInSeconds
            : dt / GridFadeOutSeconds;

        _gridOpacity = _gridOpacity < target
            ? MathF.Min(_gridOpacity + step, target)
            : MathF.Max(_gridOpacity - step, target);

        Grid.Opacity = _gridOpacity;
    }

    // The verbs below are the same calls HandleShortcuts makes. A UI thread
    // reaches them through EngineHost.EnqueueCommand.

    /// <summary>
    /// Whether the editor has handed the frame away (play mode). While true,
    /// every verb that would change the scene refuses and logs it.
    /// </summary>
    // A shell's own play-mode state is a snapshot old, so the check has to be here.
    public bool IsSuspended { get; private set; }

    // Gate for every mutating verb: play mode, or an open transaction
    // (transactions do not nest).
    private bool RefuseEdit(string label, bool allowPropertyGesture = false)
    {
        if (IsSuspended)
        {
            _logger.LogDebug("{Label}: refused, play mode owns the scene", label);
            return true;
        }

        if (_gizmos.Active.State == GizmoInteractionState.Dragging)
        {
            _logger.LogDebug("{Label}: refused, a manipulation is in progress", label);
            return true;
        }

        // A pointer capture does not block the keyboard. An insert during a
        // scrub would join the scrub's transaction and be rolled back with it.
        if (_propertyGestureOpen && !allowPropertyGesture)
        {
            _logger.LogDebug("{Label}: refused, a property gesture is in progress", label);
            return true;
        }

        return false;
    }

    /// <summary>Runs one host verb: history, a structural edit, or a mode toggle.</summary>
    public void Apply(EditorHostCommand command)
    {
        // Navigation, clear-selection and grid mode are not scene edits, so
        // they stay live in play mode and mid-drag.
        if (command is not (EditorHostCommand.ToggleNavigation or EditorHostCommand.ClearSelection
                or EditorHostCommand.GridAuto or EditorHostCommand.GridOn or EditorHostCommand.GridOff) &&
            RefuseEdit(command.ToString()))
        {
            return;
        }

        switch (command)
        {
            case EditorHostCommand.Undo: Undo(); break;
            case EditorHostCommand.Redo: Redo(); break;
            case EditorHostCommand.Duplicate: RunStructuralEdit("Duplicate", StructuralEditor.TryDuplicate); break;
            case EditorHostCommand.Delete: RunStructuralEdit("Delete", StructuralEditor.TryDelete); break;
            case EditorHostCommand.Group: RunStructuralEdit("Group", (s, u, n) => StructuralEditor.TryGroup(s, u, n)); break;
            case EditorHostCommand.Ungroup: RunStructuralEdit("Ungroup", StructuralEditor.TryUngroup); break;
            case EditorHostCommand.ToggleBrushKind: ToggleSelectionBrushKind(); break;
            case EditorHostCommand.ToggleNavigation: ToggleNavigation(); break;
            case EditorHostCommand.SelectAll: SelectAll(); break;
            case EditorHostCommand.ClearSelection: ClearSelection(); break;
            case EditorHostCommand.GridAuto: GridMode = GridMode.Auto; break;
            case EditorHostCommand.GridOn: GridMode = GridMode.On; break;
            case EditorHostCommand.GridOff: GridMode = GridMode.Off; break;
        }
    }

    /// <summary>
    /// Runs one manipulator verb: pick a tool, flip the frame or the style,
    /// drive snap, cancel a drag.
    /// </summary>
    public bool Apply(GizmoCommand command) => _gizmos.Apply(command);

    /// <summary>
    /// Sets one tool's snap increment. A non-positive or non-finite value is
    /// refused and logged.
    /// </summary>
    public void SetSnapIncrement(GizmoMode tool, float increment)
    {
        if (!float.IsFinite(increment) || increment <= 0f)
        {
            _logger.LogInformation(
                "Snap increment {Value} for {Tool} refused: it must be a positive number",
                increment, tool);
            return;
        }

        _gizmos.SetSnapIncrement(tool, increment);
    }

    /// <summary>Runs one camera verb, such as framing the selection.</summary>
    public void Apply(EditorCameraCommand command) => _camera.Apply(command);

    /// <summary>
    /// Selects the node with this id, replacing, extending or toggling the
    /// selection. An id the scene does not have clears the selection under
    /// <see cref="SelectionUpdate.Replace"/> and is otherwise ignored.
    /// </summary>
    public void SelectById(Guid nodeId, SelectionUpdate mode = SelectionUpdate.Replace)
    {
        // A live gesture holds a capture of the old selection.
        _viewport.Reset();

        if (!_scene.TryFindById(nodeId, out SceneNode? node))
        {
            if (mode == SelectionUpdate.Replace)
                _scene.Selection.Clear();
            return;
        }

        switch (mode)
        {
            case SelectionUpdate.Add: _scene.Selection.Add(node); break;
            case SelectionUpdate.Toggle: _scene.Selection.Toggle(node); break;
            default: _scene.Selection.Select(node); break;
        }
    }

    /// <summary>
    /// Selects a set of ids in one operation, raising one change event. Ids
    /// the scene no longer has are skipped; under
    /// <see cref="SelectionUpdate.Replace"/> the selection becomes the
    /// resolvable set, empty included.
    /// </summary>
    public void SelectByIds(IReadOnlyList<Guid> nodeIds, SelectionUpdate mode = SelectionUpdate.Replace)
    {
        ArgumentNullException.ThrowIfNull(nodeIds);

        _viewport.Reset();

        var nodes = new List<SceneNode>(nodeIds.Count);
        for (int i = 0; i < nodeIds.Count; i++)
        {
            if (_scene.TryFindById(nodeIds[i], out SceneNode? node))
                nodes.Add(node);
        }

        _scene.Selection.Apply(nodes, mode);
    }

    /// <summary>
    /// Renames one node, addressed by id, as one history entry. Returns false
    /// (writing nothing) for an unknown id, an empty name after trimming, or a
    /// name the node already has.
    /// </summary>
    public bool RenameById(Guid nodeId, string name)
    {
        // Grabbing a gizmo handle blurs the rename box, which commits the
        // rename inside the drag's transaction. Refuse instead of throwing.
        if (RefuseEdit("Rename"))
            return false;

        if (!_scene.TryFindById(nodeId, out SceneNode? node))
            return false;

        string trimmed = name?.Trim() ?? string.Empty;
        if (trimmed.Length == 0 || string.Equals(node.Name, trimmed, StringComparison.Ordinal))
            return false;

        _undo.BeginTransaction("Rename");
        _undo.Execute(SetNodeNameCommand.Capture(node, trimmed));
        _undo.CommitTransaction();

        _logger.LogInformation("Rename: '{Id}' is now '{Name}'", nodeId, trimmed);
        return true;
    }

    /// <summary>
    /// Moves nodes, addressed by id, under a new parent at the given index
    /// (<c>-1</c> appends), keeping world transforms, as one history entry.
    /// </summary>
    public void ReparentByIds(IReadOnlyList<Guid> nodeIds, Guid newParentId, int insertIndex)
    {
        ArgumentNullException.ThrowIfNull(nodeIds);

        if (RefuseEdit("Reparent"))
            return;

        _viewport.Reset();

        if (!_scene.TryFindById(newParentId, out SceneNode? newParent))
        {
            _logger.LogInformation("Reparent: target parent {Id} is not in the scene", newParentId);
            return;
        }

        var nodes = new List<SceneNode>(nodeIds.Count);
        for (int i = 0; i < nodeIds.Count; i++)
        {
            if (_scene.TryFindById(nodeIds[i], out SceneNode? node))
                nodes.Add(node);
        }

        if (!StructuralEditor.TryReparent(_scene, _undo, nodes, newParent, insertIndex))
        {
            _logger.LogInformation(
                "Reparent: nothing to move ({Count} node(s) in, target '{Parent}')",
                nodes.Count, newParent.Name);
            return;
        }

        _logger.LogInformation(
            "Reparent: {Count} node(s) under '{Parent}' at {Index} (undo {UndoDepth})",
            nodes.Count, newParent.Name, insertIndex, _undo.UndoCount);
    }

    /// <summary>
    /// Retargets the selection for a right-click before a context menu: an
    /// unselected object under the point becomes the selection, while a
    /// selected object or empty space leaves the selection as it is.
    /// </summary>
    public void SelectAtPoint(Vector2 viewportPoint)
    {
        if (_viewportSize.X <= 0f || _viewportSize.Y <= 0f)
            return;

        if (IsSuspended)
            return;

        _viewport.Reset();

        Ray3 ray = _scene.Camera.ScreenPointToRay(viewportPoint, _viewportSize);

        // The pick's reach, not the insert clamp: a far object is still
        // clickable, and a miss here would leave Delete on the wrong object.
        if (!_scene.Raycast(
                in ray, out SceneRaycastHit hit, SceneQueryFilter.EditorPicking, _viewport.PickDistance))
        {
            _logger.LogDebug("Right-click at ({X:0.#}, {Y:0.#}) hit nothing; the selection stands",
                viewportPoint.X, viewportPoint.Y);
            return;
        }

        if (!_scene.Selection.Contains(hit.Node))
            _scene.Selection.Select(hit.Node);
    }

    /// <inheritdoc/>
    public void Suspend()
    {
        // Roll back any open gesture, and release the camera's cursor lock so
        // it does not fight the character controller's.
        _viewport.Reset();
        _camera.SuspendNavigation();
        ResetGridFade();
        IsSuspended = true;
    }

    // Update stops while suspended, so the fade would freeze mid-gesture.
    private void ResetGridFade()
    {
        _gridOpacity = 0f;
        Grid.Opacity = 0f;
    }

    /// <inheritdoc/>
    public void Resume() => IsSuspended = false;

    /// <summary>
    /// Applies a property-panel edit to the selection as it is when the edit
    /// runs, which may be newer than the one the UI saw.
    /// </summary>
    /// <returns>How many nodes changed.</returns>
    public int ApplyProperty(PropertyEdit edit)
    {
        // Exempt from the gesture gate: these are the gesture's own edits.
        if (RefuseEdit("Property edit", allowPropertyGesture: true))
            return 0;

        return PropertyEditor.Apply(_undo, _scene.Selection.Items, edit, _propertyGestureOpen);
    }

    /// <summary>
    /// Replaces the wiring on one entity node, as one undoable entry. A list
    /// equal to the stored one records nothing.
    /// </summary>
    /// <returns>True when something was written.</returns>
    /// <param name="connections">The wires it should carry, in authored order.</param>
    // By id, unlike ApplyProperty: this replaces a whole list, so landing on a
    // newer selection would overwrite another node's wiring.
    public bool ApplyEntityConnections(Guid nodeId, IReadOnlyList<EntityConnection> connections)
    {
        ArgumentNullException.ThrowIfNull(connections);

        if (RefuseEdit("Entity wiring", allowPropertyGesture: true))
            return false;

        if (!_scene.TryFindById(nodeId, out SceneNode? node) || node.Entity is not { } entity)
        {
            _logger.LogDebug("Entity wiring: no entity for node {NodeId}", nodeId);
            return false;
        }

        if (SetEntityConnectionsCommand.SameWiring(entity.Connections, connections))
            return false;

        var command = SetEntityConnectionsCommand.Capture(node, connections);

        // Inside a gesture the caller owns the transaction.
        bool ownTransaction = !_propertyGestureOpen;
        if (ownTransaction)
            _undo.BeginTransaction("Entity Wiring");

        _undo.Execute(command);

        if (ownTransaction)
            _undo.CommitTransaction();

        return true;
    }

    private bool _propertyGestureOpen;

    /// <summary>
    /// Opens one history entry for a continuous property gesture, such as a
    /// drag across a numeric field. Edits that arrive while it is open join it.
    /// </summary>
    /// <returns>
    /// False when the editor refused; the caller must then not emit edits or
    /// call <see cref="EndPropertyGesture"/>.
    /// </returns>
    public bool BeginPropertyGesture(string name)
    {
        if (_propertyGestureOpen || RefuseEdit("Property gesture"))
            return false;

        _undo.BeginTransaction(string.IsNullOrEmpty(name) ? "Edit" : name);
        _propertyGestureOpen = true;
        return true;
    }

    /// <summary>
    /// Closes a gesture opened by <see cref="BeginPropertyGesture"/>, keeping
    /// what it did or rolling it back.
    /// </summary>
    public void EndPropertyGesture(bool commit)
    {
        if (!_propertyGestureOpen)
            return;

        _propertyGestureOpen = false;

        if (commit)
            _undo.CommitTransaction();
        else
            _undo.CancelTransaction();
    }

    /// <summary>
    /// Resets the editor after the scene's graph has been replaced wholesale,
    /// as a map load does: rolls back any open gesture and clears the selection
    /// and the history, which all refer to the old graph.
    /// </summary>
    public void OnSceneReplaced()
    {
        // Not Suspend(): that sets IsSuspended, which only leaving play mode
        // clears, and the editor would refuse every edit after a map load.
        _viewport.Reset();
        _camera.SuspendNavigation();
        _scene.Selection.Clear();
        _undo.Clear();
        Selection.Silhouettes?.Release();

        ResetGridFade();
    }

    /// <inheritdoc/>
    public void Draw(DebugDraw output)
    {
        // Draw order: context outlines, selection, manipulator handles, compass.
        _partOutlines.Draw(output, _scene);
        _negativeOutlines.Draw(output, _scene);
        _volumeOutlines.Draw(output, _scene);
        Lights.Draw(output, _scene, _scene.Camera, _viewportSize);
        _entityMarkers.Draw(output, _scene, _scene.Camera, _viewportSize);
        LightGizmo.Draw(output, _viewportSize);

        Selection.Draw(output, _scene, _scene.Camera, _viewportSize, new OutlineFocus(
            _viewport.HoveredNode,
            _viewport.HoveredPlaneIndex,
            _materialDrag,
            _scene.Selection.FaceNode,
            _scene.Selection.FacePlane));

        _viewport.Draw(output, _viewportSize);

        Compass.Draw(output, _scene.Camera, _viewportSize);
    }

    // Derived each frame from the drag mode and the hover, so the cursor
    // always matches what a press would do. A selectable object gets an arrow;
    // the outline is the affordance.
    private void UpdateCursorShape()
    {
        // Camera first: it owns the pointer while the drag mode is still None.
        // Not freelook, which locks the cursor.
        if (_camera.IsNavigating && !_camera.IsFreeLooking)
        {
            Request(CursorShape.SizeAll);
            return;
        }

        CursorShape shape = _viewport.DragMode switch
        {
            ViewportDragMode.Manipulate => CursorShape.Grabbing,
            ViewportDragMode.SelectAndMove => CursorShape.Grabbing,
            ViewportDragMode.BoxSelect => CursorShape.Crosshair,

            _ => _viewport.HoverMode == ViewportDragMode.Manipulate
                ? CursorShape.Grab
                : IsSuspended ? CursorShape.No : CursorShape.Arrow,
        };

        Request(shape);

        // Only on a change: the request takes a lock, and this runs every frame.
        void Request(CursorShape wanted)
        {
            if (wanted == _lastCursorShape)
                return;

            _lastCursorShape = wanted;
            _cursorShape.RequestCursorShape(wanted);
        }
    }

    /// <inheritdoc/>
    public void DrawWorld(DebugDraw output)
    {
        // The engine keeps calling this during play, with the fade frozen.
        if (IsSuspended)
            return;

        // Spacing follows the live gesture: the resize step during a resize
        // drag, the move step otherwise. Never the rotate step, which is degrees.
        bool resizing = _viewport.DragMode == ViewportDragMode.Manipulate
            && !LightGizmo.IsDragging
            && _gizmos.Mode == GizmoMode.Scale;
        float increment = resizing
            ? _gizmos.Scale.Snap.Increment
            : _gizmos.Translate.Snap.Increment;

        Grid.Draw(
            output, _scene.Camera, increment, _viewportSize.Y,
            EditorViewPresets.GridPlaneOf(_camera.View));
    }

    // Idle unless the look button is held, and while the fly camera drives:
    // it reads the same keys itself.
    private EditorNavigationInput CaptureNavigation()
    {
        if (!_editorNavigation || !IsLookButtonHeld())
            return default;

        return EditorNavigationInput.FromKeys(
            forward: _input.IsKeyDown(InputKey.W),
            back: _input.IsKeyDown(InputKey.S),
            left: _input.IsKeyDown(InputKey.A),
            right: _input.IsKeyDown(InputKey.D),
            up: _input.IsKeyDown(InputKey.E) || _input.IsKeyDown(InputKey.Space),
            down: _input.IsKeyDown(InputKey.Q) || _input.IsKeyDown(InputKey.ControlLeft),
            boost: _input.IsKeyDown(InputKey.ShiftLeft) || _input.IsKeyDown(InputKey.ShiftRight));
    }

    private bool IsLookButtonHeld() =>
        (_input.PointerButtonsDown & _camera.FreeLookButton) == _camera.FreeLookButton;

    private void HandleShortcuts()
    {
        if (_input.WasKeyPressed(NavigationToggleKey))
            ToggleNavigation();

        KeyModifiers modifiers = _input.Modifiers;

        // Control chords never fall through to a bare-key verb.
        if ((modifiers & KeyModifiers.Control) != 0)
        {
            bool shift = (modifiers & KeyModifiers.Shift) != 0;

            // Ctrl is also a camera's descend key, so Ctrl+A and Ctrl+D are
            // ordinary flying while a camera reads the movement keys.
            bool movementClaimed = !_editorNavigation || IsLookButtonHeld();

            if (_input.WasKeyPressed(InputKey.Z))
            {
                if (shift) Redo();
                else Undo();
            }
            else if (_input.WasKeyPressed(InputKey.Y))
            {
                Redo();
            }
            else if (_input.WasKeyPressed(BrushKindToggleKey))
            {
                ToggleSelectionBrushKind();
            }
            else if (!movementClaimed && _input.WasKeyPressed(InputKey.D))
            {
                RunStructuralEdit("Duplicate", StructuralEditor.TryDuplicate);
            }
            else if (!movementClaimed && _input.WasKeyPressed(InputKey.A))
            {
                SelectAll();
            }
            else if (_input.WasKeyPressed(InputKey.G))
            {
                if (shift) RunStructuralEdit("Ungroup", StructuralEditor.TryUngroup);
                else RunStructuralEdit("Group", (s, u, n) => StructuralEditor.TryGroup(s, u, n));
            }
            return;
        }

        if (_input.WasKeyPressed(InputKey.Delete))
            RunStructuralEdit("Delete", StructuralEditor.TryDelete);

        for (int i = 0; i < _gizmoBindings.Length; i++)
        {
            GizmoBinding binding = _gizmoBindings[i];

            if (binding.ConflictsWithCamera && (!_editorNavigation || IsLookButtonHeld()))
                continue;

            if (_input.WasKeyPressed(binding.Key))
                _gizmos.Apply(binding.Command);
        }

        // Resolved by name through a hand-written table: reflecting over the
        // key enum would not survive trimming.
        foreach ((InputKey key, string name) in CameraKeyCandidates)
        {
            if (!_input.WasKeyPressed(key)) continue;
            if (!EditorCameraShortcuts.TryResolve(name, modifiers, out EditorCameraCommand cameraCommand))
                continue;

            // Switching projection mid-look would fight the look gesture.
            if (IsViewPreset(cameraCommand) && IsLookButtonHeld())
            {
                _logger.LogDebug("View preset {Command} ignored while the look button is held", cameraCommand);
                continue;
            }

            _camera.Apply(cameraCommand);
            break;
        }
    }

    private static readonly (InputKey Key, string Name)[] CameraKeyCandidates =
    [
        (InputKey.F, "F"),
        (InputKey.Keypad7, "Keypad7"),
        (InputKey.Keypad1, "Keypad1"),
        (InputKey.Keypad3, "Keypad3"),
        (InputKey.Keypad5, "Keypad5"),
    ];

    private static bool IsViewPreset(EditorCameraCommand command) => command
        is EditorCameraCommand.ViewPerspective
        or EditorCameraCommand.ViewTop or EditorCameraCommand.ViewBottom
        or EditorCameraCommand.ViewFront or EditorCameraCommand.ViewBack
        or EditorCameraCommand.ViewRight or EditorCameraCommand.ViewLeft;

    /// <summary>Which view the editor camera is showing, for the status bar.</summary>
    public string ViewName => EditorViewPresets.NameOf(_camera.View);

    private void ToggleNavigation()
    {
        // The fly camera knows nothing about projection, so leave a plan view
        // first, and log it since the user did not ask for that.
        if (_camera.View != EditorViewPreset.Perspective)
        {
            _camera.SetView(EditorViewPreset.Perspective);
            _logger.LogInformation("Left the orthographic view: the fly camera is perspective only");
        }

        _editorNavigation = !_editorNavigation;

        if (_editorNavigation)
        {
            // Take over the fly camera's pose so the view does not jump.
            _camera.AdoptCamera();
            _viewport.CameraController = _camera;
        }
        else
        {
            // Ends any live camera gesture and releases its cursor lock.
            _camera.SuspendNavigation();
            _viewport.CameraController = null;
        }

        _logger.LogInformation("Navigation: {Mode} ({Key} toggles)", NavigationModeName, NavigationToggleKey);
    }

    // A mixed selection normalises instead of flipping per node: if any brush
    // is world geometry, all become parts; only an all-part selection converts back.
    private void ToggleSelectionBrushKind()
    {
        _viewport.Reset();

        IReadOnlyList<SceneNode> selected = _scene.Selection.Items;
        var commands = new List<IEditorCommand>();
        bool anyWorld = false;
        int skipped = 0;

        for (int i = 0; i < selected.Count; i++)
        {
            if (selected[i].Brush is null)
            {
                skipped++;
                continue;
            }
            if (selected[i].BrushKind == BrushKind.World)
                anyWorld = true;
        }

        BrushKind target = anyWorld ? BrushKind.Part : BrushKind.World;
        int converted = 0;
        for (int i = 0; i < selected.Count; i++)
        {
            SceneNode node = selected[i];
            if (node.Brush is null || node.BrushKind == target)
                continue;

            // To a part, the face axes are baked so the texture stays put.
            if (target == BrushKind.Part)
                BrushKindConversion.AppendToPart(node, commands);
            else
                commands.Add(SetBrushKindCommand.Capture(node, target));
            converted++;
        }

        if (commands.Count == 0)
        {
            _logger.LogInformation(
                "Convert brush: nothing to convert ({Selected} selected, {Skipped} without a brush)",
                selected.Count, skipped);
            return;
        }

        string name = target == BrushKind.Part ? "Convert to Part" : "Convert to World";
        _undo.Execute(commands.Count == 1 ? commands[0] : new CompositeCommand(name, commands));

        _logger.LogInformation(
            "{Name}: {Converted} brush(es), {Skipped} selected node(s) had no brush. " +
            "Part brushes leave the CSG carve: they no longer merge with the geometry around them, " +
            "and they cost no static-world recompile when they move.",
            name, converted, skipped);
    }

    private void RunStructuralEdit(
        string label, Func<Scene, UndoStack, IReadOnlyList<SceneNode>, bool> operation)
    {
        if (RefuseEdit(label))
            return;

        // Copied: these verbs rewrite the selection, and Items is the live list.
        var selection = new List<SceneNode>(_scene.Selection.Items);
        if (!operation(_scene, _undo, selection))
        {
            _logger.LogInformation("{Label}: nothing to act on ({Selected} selected)", label, selection.Count);
            return;
        }

        _logger.LogInformation(
            "{Label}: {Selected} node(s) in, {Now} selected now (undo {UndoDepth} / redo {RedoDepth})",
            label, selection.Count, _scene.Selection.Count, _undo.UndoCount, _undo.RedoCount);
    }

    // A fresh brush is 2x2x2.
    private const float InsertHalfExtent = 1f;

    // How far ahead an insert lands when the ray hits nothing.
    private const float InsertFallbackDistance = 12f;

    private const float InsertRayReach = 200f;

    /// <summary>
    /// Creates one thing where the user is looking, as one history entry, and
    /// selects it. It rests on the surface under the aim point, snapped to the
    /// move grid when snapping is on, or a fixed distance ahead on a miss.
    /// </summary>
    /// <param name="viewportPoint">Where to aim, in viewport pixels; null means the centre of the view.</param>
    public void Insert(InsertKind kind, Vector2? viewportPoint = null)
    {
        if (RefuseEdit("Insert"))
            return;

        _viewport.Reset();

        float clearance = kind switch
        {
            InsertKind.PointLight => 1.5f,

            // One-sided and facing out, so it can sit on the surface; the gap
            // only avoids z-fighting with its overlay.
            InsertKind.SurfaceLight => 0.01f,

            // Half-buried: a hole resting flush on a solid carves nothing.
            InsertKind.SubtractiveBrush => 0f,

            InsertKind.Group => 0f,

            _ => InsertHalfExtent,
        };
        Vector3 position = FindInsertPosition(clearance, viewportPoint);

        SceneNode node = BuildInsert(kind);

        SceneNode parent = _scene.Root;

        if (kind == InsertKind.SurfaceLight && TryFindSurface(viewportPoint, out SceneRaycastHit surface))
        {
            parent = surface.Node;

            // RotationForDirection takes the direction light travels, which is
            // out of the solid: +normal. Backwards shines into the wall.
            Quaternion facing = Light.RotationForDirection(surface.Normal);

            // In the parent's space, since it becomes the parent's child.
            Matrix4x4 toLocal = InverseOf(parent.WorldMatrix);
            Vector3 world = position;

            node.LocalTransform = new Transform
            {
                Position = Vector3.Transform(world, toLocal),
                Rotation = Quaternion.Concatenate(
                    facing, Quaternion.Inverse(RotationOf(parent.WorldMatrix))),
                Scale = Vector3.One,
            };
        }
        else
        {
            node.LocalPosition = position;
        }

        _undo.Execute(new AddNodesCommand(
            [new NodePlacement(node, parent.Id, parent.Children.Count)])
        {
            Name = $"Insert {node.Name}",
        });

        _scene.Selection.Select(node);

        _logger.LogInformation(
            "Insert {Kind}: '{Name}' at ({X:0.##}, {Y:0.##}, {Z:0.##}) (undo {UndoDepth})",
            kind, node.Name, position.X, position.Y, position.Z, _undo.UndoCount);
    }

    /// <summary>
    /// Creates one entity of <paramref name="className"/> where the user is
    /// looking, as one history entry, and selects it. It starts with no
    /// keyvalues. A class made from geometry comes as a 2x2x2 part resting on
    /// the surface, flagged for its placement. Any other class is a bare node
    /// named after the class.
    /// </summary>
    /// <param name="className">The class to place. Blank is refused.</param>
    /// <param name="viewportPoint">Where to aim, in viewport pixels; null is the view centre.</param>
    public void InsertEntity(string className, Vector2? viewportPoint = null)
    {
        if (RefuseEdit("Insert entity"))
            return;

        if (string.IsNullOrWhiteSpace(className))
        {
            _logger.LogWarning("Insert entity: refused, no class name");
            return;
        }

        _viewport.Reset();

        EntitySchema? geometry = null;
        if (_scene.EntitySchemas is { } schemas
            && schemas.TryGetSchema(className, out EntitySchema? schema)
            && EntityEditor.UsesGeometry(schema.Placement))
        {
            geometry = schema;
        }

        Vector3 position = FindInsertPosition(geometry is null ? 0f : InsertHalfExtent, viewportPoint);

        // Keyvalues stay empty: a key belongs in the map only once somebody
        // changes it, or a later change to a schema default reaches no saved level.
        SceneNode node = geometry is null
            ? new SceneNode(className) { Entity = new EntityData(className) }
            : EntityEditor.BuildGeometryNode(_scene, geometry, InsertHalfExtent);
        node.LocalPosition = position;

        _undo.Execute(new AddNodesCommand(
            [new NodePlacement(node, _scene.Root.Id, _scene.Root.Children.Count)])
        {
            Name = $"Insert {className}",
        });

        _scene.Selection.Select(node);

        _logger.LogInformation(
            "Insert entity '{Class}' at ({X:0.##}, {Y:0.##}, {Z:0.##}) (undo {UndoDepth})",
            className, position.X, position.Y, position.Z, _undo.UndoCount);
    }

    /// <summary>
    /// Makes the selected block, part or group an entity of
    /// <paramref name="className"/>, as one history entry. See
    /// <see cref="EntityEditor.Make"/>.
    /// </summary>
    public EntityEditReport MakeEntity(string className)
    {
        if (RefuseEdit("Make entity"))
            return EntityEditReport.RefusedBecause($"Make entity did nothing: {EditRefusal}");

        _viewport.Reset();

        EntityEditReport report = EntityEditor.Make(_scene, _undo, _scene.Selection.Items, className);
        _logger.LogInformation("Make entity '{Class}': {Result} (undo {UndoDepth})", className, report.Message, _undo.UndoCount);
        return report;
    }

    /// <summary>
    /// Takes the entity off the selected nodes, as one history entry. See
    /// <see cref="EntityEditor.Remove"/>.
    /// </summary>
    public EntityEditReport RemoveEntity()
    {
        if (RefuseEdit("Remove entity"))
            return EntityEditReport.RefusedBecause($"Remove entity did nothing: {EditRefusal}");

        _viewport.Reset();

        EntityEditReport report = EntityEditor.Remove(_scene, _undo, _scene.Selection.Items);
        _logger.LogInformation("Remove entity: {Result} (undo {UndoDepth})", report.Message, _undo.UndoCount);
        return report;
    }

    /// <summary>
    /// The entities that have world geometry in them and so will not work
    /// when the level plays, for a problem list.
    /// </summary>
    public List<EntityProblem> FindEntityProblems() => EntityAudit.FindWorldBrushOwners(_scene);

    // Why RefuseEdit just said no, as a sentence that names the way out.
    private string EditRefusal => IsSuspended
        ? "the level is playing. Stop it first."
        : "a drag is in progress. Finish it first.";

    /// <summary>
    /// Places one model file where the pointer was, as one history entry, and
    /// selects it. A model that cannot be resolved still places an empty node,
    /// with the reason in the report. The file's own root translation is
    /// discarded; its rotation and scale survive.
    /// </summary>
    /// <param name="contentPath">The model, as a path relative to the content root.</param>
    /// <param name="viewportPoint">Where to aim, in viewport pixels; null means the centre of the view.</param>
    // The import is synchronous: attaching meshes a few frames later would be
    // a second history entry or a write behind the user's undo.
    public ModelInsertReport InsertModel(string contentPath, Vector2? viewportPoint = null)
    {
        if (string.IsNullOrWhiteSpace(contentPath))
        {
            _logger.LogWarning("Insert model: refused, no asset path");
            return ModelInsertReport.RefusedBecause(contentPath ?? string.Empty, "no asset path was given");
        }

        if (RefuseEdit("Insert model"))
        {
            return ModelInsertReport.RefusedBecause(
                contentPath,
                IsSuspended ? "play mode owns the scene" : "a manipulation is in progress");
        }

        _viewport.Reset();

        // The file's name: an importer's root is often "RootNode" or unnamed.
        string name = Path.GetFileNameWithoutExtension(contentPath);
        if (string.IsNullOrEmpty(name))
            name = contentPath;

        string? unresolved = TryBuildModelNode(contentPath, name, out SceneNode node);

        // Zeroed before RestClearance measures the subtree from where it sits.
        node.LocalPosition = Vector3.Zero;

        Vector3 normal = TryFindSurface(viewportPoint, out SceneRaycastHit surface)
            ? surface.Normal
            : Vector3.UnitY;

        Vector3 position = FindInsertPosition(RestClearance(node, normal), viewportPoint);
        node.LocalPosition = position;

        _undo.Execute(new AddNodesCommand(
            [new NodePlacement(node, _scene.Root.Id, _scene.Root.Children.Count)])
        {
            Name = $"Insert {name}",
        });

        _scene.Selection.Select(node);

        if (unresolved is null)
        {
            _logger.LogInformation(
                "Insert model '{Path}' at ({X:0.##}, {Y:0.##}, {Z:0.##}) (undo {UndoDepth})",
                contentPath, position.X, position.Y, position.Z, _undo.UndoCount);
        }
        else
        {
            _logger.LogWarning(
                "Insert model '{Path}': placed without geometry ({Reason})", contentPath, unresolved);
        }

        return new ModelInsertReport(contentPath, node.Id, name, unresolved, null);
    }

    /// <summary>
    /// Paints the brush face under the pointer, or the whole brush. Does not
    /// change the selection.
    /// </summary>
    /// <param name="contentPath">The material, or empty for the engine default.</param>
    /// <param name="viewportPoint">Where the drop landed, or null for the view centre.</param>
    public MaterialAssignReport AssignMaterial(
        string contentPath, Vector2? viewportPoint, MaterialDropScope scope)
    {
        contentPath ??= string.Empty;

        if (RefuseEdit("Assign material"))
        {
            return MaterialAssignReport.RefusedBecause(
                contentPath,
                IsSuspended ? "play mode owns the scene" : "a manipulation is in progress");
        }

        _viewport.Reset();

        if (!TryFindSurface(viewportPoint, out SceneRaycastHit hit))
            return MaterialAssignReport.RefusedBecause(contentPath, "nothing is under the pointer");

        if (hit.Node.Brush is not { } brush || hit.PlaneIndex < 0)
        {
            return MaterialAssignReport.RefusedBecause(
                contentPath, $"'{hit.Node.Name}' is a mesh, and only brush faces take a material");
        }

        if (!TryInternMaterial(contentPath, out MaterialRef material))
            return MaterialAssignReport.RefusedBecause(contentPath, "that is not a content-relative path");

        Brush next = scope == MaterialDropScope.Brush
            ? brush.WithAllFacesMaterial(material)
            : brush.WithFaceMaterial(hit.PlaneIndex, material);

        int faces = CountChangedFaces(brush, next);
        string? unresolved = DescribeMissingMaterial(contentPath);

        if (faces == 0)
        {
            // No command: a new Brush instance would recompile the world for nothing.
            _logger.LogDebug("Assign material '{Path}': '{Node}' already wears it", contentPath, hit.Node.Name);
            return new MaterialAssignReport(
                contentPath, hit.Node.Id, hit.Node.Name, 0, brush.FaceSurfaces.Count, null, unresolved);
        }

        _undo.BeginTransaction(scope == MaterialDropScope.Brush ? "Material" : "Face Material");
        try
        {
            _undo.Execute(SetBrushCommand.Capture(hit.Node, next));
        }
        finally
        {
            _undo.CommitTransaction();
        }

        _logger.LogInformation(
            "Assign material '{Path}' to {Faces} face(s) of '{Node}' (undo {UndoDepth})",
            contentPath, faces, hit.Node.Name, _undo.UndoCount);

        return new MaterialAssignReport(
            contentPath, hit.Node.Id, hit.Node.Name, faces, brush.FaceSurfaces.Count, null, unresolved);
    }

    /// <summary>Paints every selected brush, whole, in one history entry.</summary>
    public MaterialAssignReport AssignMaterialToSelection(string contentPath)
    {
        contentPath ??= string.Empty;

        if (RefuseEdit("Assign material"))
        {
            return MaterialAssignReport.RefusedBecause(
                contentPath,
                IsSuspended ? "play mode owns the scene" : "a manipulation is in progress");
        }

        if (!TryInternMaterial(contentPath, out MaterialRef material))
            return MaterialAssignReport.RefusedBecause(contentPath, "that is not a content-relative path");

        List<IEditorCommand> commands = [];
        int faces = 0;
        int faceCount = 0;
        int brushes = 0;
        string lastName = string.Empty;

        foreach (SceneNode node in _scene.Selection.Items)
        {
            if (node.Brush is not { } brush) continue;

            brushes++;
            lastName = node.Name;
            faceCount += brush.FaceSurfaces.Count;

            Brush next = brush.WithAllFacesMaterial(material);
            int changed = CountChangedFaces(brush, next);
            if (changed == 0) continue;

            faces += changed;
            commands.Add(SetBrushCommand.Capture(node, next));
        }

        if (brushes == 0)
        {
            return MaterialAssignReport.RefusedBecause(
                contentPath, "no block is selected. Select one, then assign a material to it");
        }

        string name = brushes == 1 ? lastName : $"{brushes} blocks";
        string? unresolved = DescribeMissingMaterial(contentPath);

        if (commands.Count == 0)
        {
            return new MaterialAssignReport(
                contentPath, Guid.Empty, name, 0, faceCount, null, unresolved);
        }

        _undo.BeginTransaction("Material");
        try
        {
            foreach (IEditorCommand command in commands)
                _undo.Execute(command);
        }
        finally
        {
            _undo.CommitTransaction();
        }

        _logger.LogInformation(
            "Assign material '{Path}' to {Faces} face(s) across {Brushes} block(s) (undo {UndoDepth})",
            contentPath, faces, brushes, _undo.UndoCount);

        return new MaterialAssignReport(contentPath, Guid.Empty, name, faces, faceCount, null, unresolved);
    }

    /// <summary>
    /// Says that a material is being dragged over the viewport, so the outline
    /// can show what letting go would paint. Null ends the drag. The pointer
    /// position arrives through the ordinary input path.
    /// </summary>
    public void SetMaterialDrag(MaterialDropScope? scope) => _materialDrag = scope;

    /// <summary>What a material drag over the viewport would paint, or null.</summary>
    public MaterialDropScope? MaterialDrag => _materialDrag;

    private MaterialDropScope? _materialDrag;

    // The registry interns whatever it is given, so a rooted or escaping path
    // must be refused here or it ends up in a map as an unresolvable reference.
    private static bool TryInternMaterial(string contentPath, out MaterialRef material)
    {
        material = MaterialRef.Default;

        if (contentPath.Length == 0) return true;

        try
        {
            material = MaterialRegistry.Intern(ContentRoot.NormalizeRelativePath(contentPath));
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static int CountChangedFaces(Brush before, Brush after)
    {
        if (ReferenceEquals(before, after)) return 0;

        int changed = 0;
        int count = Math.Min(before.FaceSurfaces.Count, after.FaceSurfaces.Count);
        for (int i = 0; i < count; i++)
        {
            if (!before.FaceSurfaces[i].Material.Equals(after.FaceSurfaces[i].Material))
                changed++;
        }

        return changed;
    }

    private string? DescribeMissingMaterial(string contentPath)
    {
        if (contentPath.Length == 0) return null;

        if (_scene.Assets is not { } assets)
            return "the scene has no asset manager attached";

        // A failed load is cached as the default material for the session;
        // the file may have been written since.
        assets.ForgetFailedMaterial(contentPath);

        return assets.Content.Exists(contentPath)
            ? null
            : $"{contentPath} is not in the content root";
    }

    // Returns the reason the node is empty, or null. ArgumentException is in
    // the catch list for a rooted or ".." path from a badly built drag payload.
    private string? TryBuildModelNode(string contentPath, string name, out SceneNode node)
    {
        if (_scene.Assets is not { } assets)
        {
            node = new SceneNode(name);
            return "the scene has no asset manager attached";
        }

        try
        {
            ModelAsset model = assets.LoadModel(contentPath);
            if (model.Data is null)
            {
                node = new SceneNode(name);
                return model.Error ?? "the model is not loaded";
            }

            node = ModelInstantiator.Instantiate(model, name);
            return null;
        }
        catch (Exception ex) when (
            ex is FileNotFoundException or InvalidDataException
                or InvalidOperationException or ArgumentException)
        {
            node = new SceneNode(name);
            return ex.Message;
        }
    }

    // How far along the normal a detached subtree must move for its lowest
    // point to rest on the surface. Signed: a pivot above its own geometry
    // sinks, so do not clamp at zero.
    private static float RestClearance(SceneNode root, Vector3 normal)
    {
        var nodes = new List<SceneNode>();
        Collect(root, nodes);

        // Any tangent works as long as the frame is orthonormal.
        Vector3 seed = MathF.Abs(normal.Y) > 0.9f ? Vector3.UnitX : Vector3.UnitY;
        Vector3 tangent = Vector3.Normalize(Vector3.Cross(seed, normal));
        Vector3 bitangent = Vector3.Cross(normal, tangent);

        return GizmoSelectionBounds.TryMeasure(nodes, tangent, normal, bitangent, out Vector3 min, out _)
            ? -min.Y
            : 0f;

        static void Collect(SceneNode node, List<SceneNode> into)
        {
            into.Add(node);
            IReadOnlyList<SceneNode> children = node.Children;
            for (int i = 0; i < children.Count; i++)
                Collect(children[i], into);
        }
    }

    private Vector3 FindInsertPosition(float clearance, Vector2? viewportPoint)
    {
        if (_viewportSize.X <= 0f || _viewportSize.Y <= 0f)
            return SnapAllAxes(_scene.Camera.Position + _scene.Camera.Forward * InsertFallbackDistance);

        Ray3 ray = _scene.Camera.ScreenPointToRay(viewportPoint ?? _viewportSize * 0.5f, _viewportSize);

        // The picking query, parts and meshes included. The static world alone
        // would let the ray pass through a platform built of parts.
        if (!_scene.Raycast(in ray, out SceneRaycastHit hit, SceneQueryFilter.EditorPicking, InsertRayReach))
            return SnapAllAxes(ray.PointAt(InsertFallbackDistance));

        Vector3 point = hit.Point;
        SnapSettings snap = _gizmos.Translate.Snap;
        if (snap.Enabled)
        {
            // Snap, then project back onto the hit plane, so a coarse grid
            // can neither bury the insert nor float it.
            var snapped = new Vector3(
                snap.SnapScalar(point.X), snap.SnapScalar(point.Y), snap.SnapScalar(point.Z));
            snapped += hit.Normal * Vector3.Dot(point - snapped, hit.Normal);
            point = snapped;
        }

        return point + hit.Normal * clearance;
    }

    private bool TryFindSurface(Vector2? viewportPoint, out SceneRaycastHit hit)
    {
        hit = default;

        if (_viewportSize.X <= 0f || _viewportSize.Y <= 0f)
            return false;

        Ray3 ray = _scene.Camera.ScreenPointToRay(viewportPoint ?? _viewportSize * 0.5f, _viewportSize);
        return _scene.Raycast(in ray, out hit, SceneQueryFilter.EditorPicking, InsertRayReach);
    }

    // A mesh parent may carry a degenerate scale; fall back to identity
    // instead of throwing out of an insert.
    private static Matrix4x4 InverseOf(Matrix4x4 world) =>
        Matrix4x4.Invert(world, out Matrix4x4 inverse) ? inverse : Matrix4x4.Identity;

    private static Quaternion RotationOf(Matrix4x4 world) =>
        Matrix4x4.Decompose(world, out _, out Quaternion rotation, out _)
            ? rotation
            : Quaternion.Identity;

    private Vector3 SnapAllAxes(Vector3 point)
    {
        SnapSettings snap = _gizmos.Translate.Snap;
        if (!snap.Enabled)
            return point;

        return new Vector3(
            snap.SnapScalar(point.X), snap.SnapScalar(point.Y), snap.SnapScalar(point.Z));
    }

    private static SceneNode BuildInsert(InsertKind kind)
    {
        var half = new Vector3(InsertHalfExtent);
        switch (kind)
        {
            case InsertKind.PartBrush:
            {
                var node = new SceneNode("Part") { Brush = Brush.CreateBox(-half, half) };
                node.BrushKind = BrushKind.Part;
                return node;
            }

            case InsertKind.SubtractiveBrush:
                // World kind: a subtractive part carves nothing and draws nothing.
                return new SceneNode("Hole")
                {
                    Brush = Brush.CreateBox(-half, half).WithOperation(BrushOperation.Subtractive),
                };

            case InsertKind.SurfaceLight:
                // Fixed extents, not fitted to the face: the face polygon is
                // derived and changes with every carve.
                return new SceneNode("Panel")
                {
                    Light = new Light
                    {
                        Kind = LightKind.Rect,
                        Color = ColorSpace.SrgbToLinear(new Vector3(1f, 0.97f, 0.92f)),
                        Intensity = 25f,
                        Range = 8f,
                        Width = 1f,
                        Height = 1f,
                    },
                };

            case InsertKind.PointLight:
                return new SceneNode("Light")
                {
                    Light = new Light
                    {
                        Kind = LightKind.Point,
                        Color = ColorSpace.SrgbToLinear(new Vector3(1f, 0.95f, 0.85f)),
                        Intensity = 40f,
                        Range = 8f,
                    },
                };

            case InsertKind.Group:
                return new SceneNode("Group");

            default:
                return new SceneNode("Brush") { Brush = Brush.CreateBox(-half, half) };
        }
    }

    private void SelectAll()
    {
        _viewport.Reset();

        _scene.Selection.Clear();
        _scene.Selection.AddRange(_scene.Root.Children);

        _logger.LogInformation("Select all: {Count} top-level node(s)", _scene.Selection.Count);
    }

    private void ClearSelection()
    {
        _viewport.Reset();
        _scene.Selection.Clear();
    }

    private void Undo()
    {
        // The stack refuses a step while a transaction is open, so abandon
        // any live gesture first.
        _viewport.Reset();
        if (_undo.Undo())
            _logger.LogInformation("Undo '{Name}' (undo {UndoDepth} / redo {RedoDepth})", _undo.RedoName, _undo.UndoCount, _undo.RedoCount);
    }

    private void Redo()
    {
        _viewport.Reset();
        if (_undo.Redo())
            _logger.LogInformation("Redo '{Name}' (undo {UndoDepth} / redo {RedoDepth})", _undo.UndoName, _undo.UndoCount, _undo.RedoCount);
    }

    // Resolved once at construction: the table matches on key names, and
    // ToString per key per frame would allocate.
    private static GizmoBinding[] BuildGizmoBindings()
    {
        var bindings = new List<GizmoBinding>(GizmoKeyCandidates.Length);
        foreach (InputKey key in GizmoKeyCandidates)
        {
            if (!GizmoShortcuts.TryResolve(key.ToString(), out GizmoCommand command))
                continue;

            bindings.Add(new GizmoBinding(key, command, Array.IndexOf(CameraKeys, key) >= 0));
        }

        return [.. bindings];
    }

    private readonly record struct GizmoBinding(InputKey Key, GizmoCommand Command, bool ConflictsWithCamera);
}
