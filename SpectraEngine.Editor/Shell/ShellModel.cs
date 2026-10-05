using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using SpectraEngine.Core.Hosting;
using SpectraEngine.Core.Scene;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace SpectraEngine.Editor.Shell;

/// <summary>
/// One map in the open project's panel: the manifest's, the folder's, or both.
/// </summary>
/// <param name="RelativePath">Project-relative bundle path, the manifest's key.</param>
/// <param name="IsUnlisted">On disk but not in the manifest.</param>
public sealed record ProjectMapRow(string RelativePath, string Name, bool IsStartup, bool IsUnlisted);

/// <summary>
/// Everything the window binds to: the engine's reported state, the readouts,
/// and the message line.
/// </summary>
// UI thread only. Holds values copied out of a FrameSnapshot, never anything
// the engine owns. The message line is not snapshot-driven, so a startup
// failure written once survives later frames.
public sealed class ShellModel : ObservableObject
{
    // Values shown on the click and confirmed by the engine later. The
    // pipeline dropdown and the snap increment are left out: a pipeline
    // switch can fail, and the snap field has its own focus guard.
    private readonly OptimisticValue<string> _modeOpt = new("move", StringComparer.Ordinal);
    private readonly OptimisticValue<string> _styleOpt = new("Studio", StringComparer.Ordinal);
    private readonly OptimisticValue<string> _orientationOpt = new("world", StringComparer.Ordinal);
    private readonly OptimisticValue<bool> _snapOpt = new(false);
    private readonly OptimisticValue<DebugVisualization> _debugOpt = new(DebugVisualization.None);

    // Longer hold: entering play mode takes real work and can be refused.
    private readonly OptimisticValue<bool> _playOpt = new(false) { HoldTicks = 12 };

    // One value, because undo and redo depth move together. Without the
    // prediction a second quick click on Undo would undo two edits.
    private readonly OptimisticValue<(int Undo, int Redo)> _historyOpt = new((0, 0));

    private SceneTreeModel? _tree;
    private string _gizmoMode = "move";
    private string _gizmoStyle = "Studio";
    private string _orientation = "world";
    private string _navigation = "-";
    private bool _snapEnabled;
    private float _snapIncrement;
    private int _selectionCount;
    private int _undoDepth;
    private int _redoDepth;
    private int _compileCount;
    private int _nodeCount;
    private int _matchCount;
    private double _fps;
    private double _frameTimeMs;
    private int _viewportWidth;
    private int _viewportHeight;
    private string _message = string.Empty;
    private bool _isError;
    private string _filterText = string.Empty;
    private PropertyPanelModel? _properties;

    private DispatcherTimer? _filterDebounce;

    /// <summary>The scene graph mirror, once a session has started.</summary>
    public SceneTreeModel? Tree
    {
        get => _tree;
        set
        {
            if (Set(ref _tree, value))
                Raise(nameof(HasTree));
        }
    }

    /// <summary>Whether there is a tree to show at all.</summary>
    public bool HasTree => _tree is not null;

    private bool _hasSession;

    /// <summary>
    /// Whether an engine session is running. Separates the editor view from
    /// the start page.
    /// </summary>
    public bool HasSession
    {
        get => _hasSession;
        set => Set(ref _hasSession, value);
    }

    private bool _hasProject;

    /// <summary>Whether a project is open, for the maps panel.</summary>
    public bool HasProject
    {
        get => _hasProject;
        set
        {
            if (Set(ref _hasProject, value)) Raise(nameof(CanValidateCooked));
        }
    }

    private bool _isValidatingCooked;

    /// <summary>Whether a cooked-content validation is running right now.</summary>
    // Gates its own menu item: two cooks must not write one cooked/ folder.
    public bool IsValidatingCooked
    {
        get => _isValidatingCooked;
        set
        {
            if (Set(ref _isValidatingCooked, value)) Raise(nameof(CanValidateCooked));
        }
    }

    /// <summary>Whether the Validate Cooked verb can be asked for.</summary>
    // Needs a project, not a session: the cook only reads the folder on disk.
    public bool CanValidateCooked => _hasProject && !_isValidatingCooked;

    /// <summary>
    /// The open project's maps: the manifest's list in the author's order,
    /// then anything on disk the manifest does not name.
    /// </summary>
    public ObservableCollection<ProjectMapRow> ProjectMaps { get; } = [];

    private string _documentName = "untitled";
    private string _projectName = string.Empty;
    private bool _isDocumentDirty;

    /// <summary>The open level's name, which is its bundle folder's name.</summary>
    public string DocumentName
    {
        get => _documentName;
        private set => Set(ref _documentName, value);
    }

    /// <summary>The open project's name, or empty when a bundle was opened alone.</summary>
    public string ProjectName
    {
        get => _projectName;
        private set => Set(ref _projectName, value);
    }

    /// <summary>Whether the level has unsaved edits, for the mark beside its name.</summary>
    public bool IsDocumentDirty
    {
        get => _isDocumentDirty;
        private set => Set(ref _isDocumentDirty, value);
    }

    /// <summary>Takes the document's identity. UI thread.</summary>
    public void SetDocument(string name, string project, bool dirty)
    {
        DocumentName = name;
        ProjectName = project;
        IsDocumentDirty = dirty;
    }

    /// <summary>The Help menu's version line.</summary>
    public string AboutLabel { get; set; } = "Spectra Editor";

    private bool _isPlaying;
    private bool _canPlay;

    /// <summary>Whether play mode is active, for the Play/Stop button's face.</summary>
    public bool IsPlaying
    {
        get => _isPlaying;
        private set
        {
            if (!Set(ref _isPlaying, value))
                return;

            Raise(nameof(PlayTip));
            Raise(nameof(PlayLabel));
            Raise(nameof(CanInsertEntity));
            Raise(nameof(CanEditSelection));
            Raise(nameof(CanMakeEntity));
            Raise(nameof(CanUndo));
            Raise(nameof(CanRedo));
        }
    }

    /// <summary>Whether play mode can be entered at all (the scene has a character).</summary>
    public bool CanPlay
    {
        get => _canPlay;
        private set
        {
            if (Set(ref _canPlay, value))
                Raise(nameof(PlayTip));
        }
    }

    /// <summary>The play button's tooltip, which names the exit key while playing.</summary>
    public string PlayTip => _isPlaying
        ? "Stop the run and put the camera back.  F8 or Esc"
        : _canPlay
            ? "Walk the level in first person.  F8"
            : "This level has no character to walk with.";

    /// <summary>The play button's word.</summary>
    public string PlayLabel => _isPlaying ? "Stop" : "Play";

    private DebugVisualization _debugFlags;

    /// <summary>Whether the wireframe overlay is on.</summary>
    public bool DebugWireframe => (_debugFlags & DebugVisualization.Wireframe) != 0;

    /// <summary>Whether the vertex-marker overlay is on.</summary>
    public bool DebugVertices => (_debugFlags & DebugVisualization.Vertices) != 0;

    /// <summary>Whether the bounds overlay is on.</summary>
    public bool DebugAabbs => (_debugFlags & DebugVisualization.Aabbs) != 0;

    /// <summary>Whether the normals overlay is on.</summary>
    public bool DebugNormals => (_debugFlags & DebugVisualization.Normals) != 0;

    /// <summary>Whether the scene-graph overlay is on.</summary>
    public bool DebugSceneGraph => (_debugFlags & DebugVisualization.SceneGraph) != 0;

    /// <summary>Whether one overlay is on, by flag.</summary>
    public bool IsDebugEnabled(DebugVisualization flag) => (_debugFlags & flag) != 0;

    private IReadOnlyList<string> _pipelineNames = Array.Empty<string>();
    private string? _pipelineName;

    /// <summary>Every pipeline the backend offers, for the View strip's dropdown.</summary>
    public IReadOnlyList<string> PipelineNames
    {
        get => _pipelineNames;
        private set => Set(ref _pipelineNames, value);
    }

    /// <summary>
    /// The live pipeline. Two-way: a user choice raises
    /// <see cref="PipelineRequested"/>; a snapshot writing the engine's answer
    /// back raises nothing.
    /// </summary>
    public string? PipelineName
    {
        get => _pipelineName;
        set
        {
            if (!Set(ref _pipelineName, value))
                return;

            // Null is the dropdown clearing itself while its items change.
            // The snapshot guard stops a published value re-requesting itself.
            if (!_applyingSnapshot && value is { Length: > 0 } requested)
                PipelineRequested?.Invoke(requested);
        }
    }

    /// <summary>Raised when the user picks a pipeline, with its name.</summary>
    public event Action<string>? PipelineRequested;

    private bool _applyingSnapshot;

    // Tool state is mirrored as booleans too: a XAML class binding cannot
    // compare strings.

    /// <summary>The live manipulator: <c>move</c>, <c>rotate</c> or <c>resize</c>.</summary>
    public string GizmoMode
    {
        get => _gizmoMode;
        private set
        {
            if (!Set(ref _gizmoMode, value))
                return;

            Raise(nameof(IsMoveActive));
            Raise(nameof(IsRotateActive));
            Raise(nameof(IsResizeActive));
            Raise(nameof(SnapUnitLabel));
            Raise(nameof(SnapSummary));
            RefreshGestureHint();
            GizmoModeChanged?.Invoke();
        }
    }

    /// <summary>Whether the move tool is live.</summary>
    public bool IsMoveActive => _gizmoMode == "move";

    /// <summary>Whether the rotate tool is live.</summary>
    public bool IsRotateActive => _gizmoMode == "rotate";

    /// <summary>Whether the resize tool is live.</summary>
    public bool IsResizeActive => _gizmoMode == "resize";

    /// <summary>The handle style: <c>Studio</c> or <c>Classic</c>.</summary>
    public string GizmoStyle
    {
        get => _gizmoStyle;
        private set
        {
            if (!Set(ref _gizmoStyle, value))
                return;

            Raise(nameof(IsStudioStyle));
            Raise(nameof(GizmoStyleMenuLabel));
        }
    }

    /// <summary>Whether the Studio handle roster is live.</summary>
    public bool IsStudioStyle => _gizmoStyle == "Studio";

    // The Request* methods show the value at once and start the hold-off.
    // They never talk to the engine; the caller still posts the verb.

    /// <summary>The user picked a tool. Lights it now; the engine confirms.</summary>
    public void RequestGizmoMode(string mode)
    {
        _modeOpt.Request(mode);
        GizmoMode = _modeOpt.Value;
    }

    /// <summary>The user picked a handle style.</summary>
    public void RequestGizmoStyle(string style)
    {
        _styleOpt.Request(style);
        GizmoStyle = _styleOpt.Value;
    }

    /// <summary>The user picked an axis frame.</summary>
    public void RequestOrientation(string orientation)
    {
        _orientationOpt.Request(orientation);
        Orientation = _orientationOpt.Value;
    }

    /// <summary>The user turned snapping on or off.</summary>
    public void RequestSnapEnabled(bool enabled)
    {
        _snapOpt.Request(enabled);
        SnapEnabled = _snapOpt.Value;
    }

    /// <summary>The user asked to start or stop play mode.</summary>
    public void RequestPlaying(bool playing)
    {
        _playOpt.Request(playing);
        IsPlaying = _playOpt.Value;
    }

    /// <summary>The user turned one debug visualisation on or off.</summary>
    public void RequestDebugVisualization(DebugVisualization flag, bool enabled)
    {
        DebugVisualization wanted = enabled ? _debugFlags | flag : _debugFlags & ~flag;
        _debugOpt.Request(wanted);
        SetDebugFlags(_debugOpt.Value);
    }

    /// <summary>
    /// The user asked to undo. Predicts the depths so the buttons settle on
    /// the click rather than a snapshot later.
    /// </summary>
    public void RequestUndo() =>
        ApplyHistory(_historyOpt.Request((Math.Max(0, _undoDepth - 1), _redoDepth + 1)));

    /// <summary>The user asked to redo.</summary>
    public void RequestRedo() =>
        ApplyHistory(_historyOpt.Request((_undoDepth + 1, Math.Max(0, _redoDepth - 1))));

    private void ApplyHistory(bool _)
    {
        (int undo, int redo) = _historyOpt.Value;
        UndoDepth = undo;
        RedoDepth = redo;
    }

    /// <summary>How many overlays are shown one chip at a time.</summary>
    // Two is what fits the header strip at its narrowest (644px).
    public const int MaxOverlayChips = 2;

    /// <summary>How many debug overlays are latched.</summary>
    public int OverlayCount { get; private set; }

    /// <summary>
    /// Whether the overlays are shown as one chip rather than one each.
    /// </summary>
    public bool OverlaysCollapsed => OverlayCount > MaxOverlayChips;

    /// <summary>Whether each latched overlay gets its own chip.</summary>
    public bool OverlaysExpanded => OverlayCount is > 0 and <= MaxOverlayChips;

    /// <summary>"3 overlays", for the collapsed chip.</summary>
    public string OverlayCountLabel =>
        OverlayCount == 1 ? "1 overlay" : $"{OverlayCount} overlays";

    private void RefreshOverlayCount()
    {
        int count = 0;
        if (DebugWireframe) count++;
        if (DebugVertices) count++;
        if (DebugAabbs) count++;
        if (DebugNormals) count++;
        if (DebugSceneGraph) count++;

        if (OverlayCount == count) return;

        OverlayCount = count;
        Raise(nameof(OverlayCount));
        Raise(nameof(OverlaysCollapsed));
        Raise(nameof(OverlaysExpanded));
        Raise(nameof(OverlayCountLabel));
    }

    private void SetDebugFlags(DebugVisualization flags)
    {
        if (_debugFlags == flags)
            return;

        _debugFlags = flags;
        Raise(nameof(DebugWireframe));
        Raise(nameof(DebugVertices));
        Raise(nameof(DebugAabbs));
        Raise(nameof(DebugNormals));
        Raise(nameof(DebugSceneGraph));

        RefreshOverlayCount();
    }

    /// <summary>
    /// Drops every unconfirmed request. Call when a session closes, or the
    /// next session briefly shows the old one's pending values.
    /// </summary>
    public void ResetOptimisticState()
    {
        _modeOpt.Reset(_gizmoMode);
        _styleOpt.Reset(_gizmoStyle);
        _orientationOpt.Reset(_orientation);
        _snapOpt.Reset(_snapEnabled);
        _debugOpt.Reset(_debugFlags);
        _playOpt.Reset(_isPlaying);
        _historyOpt.Reset((_undoDepth, _redoDepth));
    }

    /// <summary>
    /// Raised on the UI thread when the live tool changes, so the snap field
    /// can re-read its increment.
    /// </summary>
    public event Action? GizmoModeChanged;

    /// <summary>The axis frame: <c>world</c> or <c>local</c>.</summary>
    public string Orientation
    {
        get => _orientation;
        private set
        {
            if (!Set(ref _orientation, value))
                return;

            Raise(nameof(IsWorldSpace));
            Raise(nameof(OrientationMenuLabel));
        }
    }

    /// <summary>Whether drags resolve against world axes.</summary>
    public bool IsWorldSpace => _orientation == "world";

    /// <summary>The Edit menu's wording for the axis toggle.</summary>
    public string OrientationMenuLabel => $"Drag axes: {_orientation}";

    /// <summary>Whether the live manipulator quantises its drags.</summary>
    public bool SnapEnabled
    {
        get => _snapEnabled;
        private set
        {
            // Raise only on a change: this setter runs on every pump.
            if (Set(ref _snapEnabled, value))
            {
                Raise(nameof(SnapUnitLabel));
                Raise(nameof(SnapSummary));
                RefreshGestureHint();
            }
        }
    }

    /// <summary>The live manipulator's increment.</summary>
    public float SnapIncrement
    {
        get => _snapIncrement;
        private set
        {
            if (Set(ref _snapIncrement, value))
            {
                Raise(nameof(SnapUnitLabel));
                Raise(nameof(SnapSummary));
            }
        }
    }

    /// <summary>
    /// The unit the live tool's snap increment is measured in: degrees under
    /// rotate, world units otherwise.
    /// </summary>
    public string SnapUnitLabel => _gizmoMode == "rotate" ? "deg" : "su";

    /// <summary>
    /// Snap state as one phrase, for the inspector's empty state.
    /// </summary>
    public string SnapSummary => _snapEnabled
        ? $"{_snapIncrement:0.##} {SnapUnitLabel}"
        : "off";

    /// <summary>The Edit menu's wording for the handle-style toggle.</summary>
    public string GizmoStyleMenuLabel => $"Handles: {_gizmoStyle}";

    /// <summary>Which camera is driving.</summary>
    public string Navigation
    {
        get => _navigation;
        private set
        {
            if (!Set(ref _navigation, value)) return;

            Raise(nameof(NavigationMenuLabel));
            Raise(nameof(NavigationChipLabel));
        }
    }

    /// <summary>The View menu's wording for the camera toggle.</summary>
    public string NavigationMenuLabel => $"Camera: {_navigation}";

    /// <summary>
    /// The first word of <see cref="Navigation"/>, for the header chip, which
    /// has no room for the full phrase.
    /// </summary>
    public string NavigationChipLabel
    {
        get
        {
            int space = _navigation.IndexOf(' ');
            return space > 0 ? _navigation[..space] : _navigation;
        }
    }

    // No optimistic hold: the menu is closed by the time the echo lands.
    private string _gridMode = "auto";

    /// <summary>Whether the grid shows during move/resize gestures only.</summary>
    public bool GridAuto => _gridMode == "auto";

    /// <summary>Whether the grid is always drawn.</summary>
    public bool GridOn => _gridMode == "on";

    /// <summary>Whether the grid is off.</summary>
    public bool GridOff => _gridMode == "off";

    /// <summary>The grid mode as the header chip's value word.</summary>
    public string GridModeLabel => _gridMode;

    private string _viewName = "Perspective";

    /// <summary>Which view the editor camera is showing.</summary>
    public string ViewName => _viewName;

    public bool IsViewPerspective => _viewName == "Perspective";
    public bool IsViewTop => _viewName == "Top";
    public bool IsViewBottom => _viewName == "Bottom";
    public bool IsViewFront => _viewName == "Front";
    public bool IsViewBack => _viewName == "Back";
    public bool IsViewRight => _viewName == "Right";
    public bool IsViewLeft => _viewName == "Left";

    // Follows the engine, no optimistic hold. Looking around leaves a plan
    // view; that return to perspective is reported unless the shell asked.
    private void ApplyViewName(string name)
    {
        if (_viewName == name) return;

        bool wasOrthographic = _viewName != "Perspective";
        _viewName = name;

        Raise(nameof(ViewName));
        Raise(nameof(IsViewPerspective));
        Raise(nameof(IsViewTop));
        Raise(nameof(IsViewBottom));
        Raise(nameof(IsViewFront));
        Raise(nameof(IsViewBack));
        Raise(nameof(IsViewRight));
        Raise(nameof(IsViewLeft));

        if (wasOrthographic && name == "Perspective" && _expectedView != "Perspective")
        {
            SetMessage(
                "Left the orthographic view: looking around returns to perspective. " +
                "Numpad 7, 1 and 3 go back.");
        }

        if (_expectedView == name) _expectedView = null;
    }

    private string? _expectedView;

    /// <summary>
    /// Says the shell asked for this view, so its arrival is not reported as a
    /// surprise.
    /// </summary>
    public void ExpectViewName(string name) => _expectedView = name;

    private void ApplyGridMode(string mode)
    {
        if (_gridMode == mode)
            return;

        _gridMode = mode;
        Raise(nameof(GridAuto));
        Raise(nameof(GridOn));
        Raise(nameof(GridOff));
        Raise(nameof(GridModeLabel));
    }

    /// <summary>How many nodes the engine reports as selected.</summary>
    public int SelectionCount
    {
        get => _selectionCount;
        private set
        {
            if (!Set(ref _selectionCount, value))
                return;

            Raise(nameof(HasSelection));
            Raise(nameof(CanEditSelection));
            Raise(nameof(CanMakeEntity));
            Raise(nameof(SelectionLabel));
        }
    }

    /// <summary>Whether anything is selected, for the structural buttons.</summary>
    public bool HasSelection => _selectionCount > 0;

    /// <summary>
    /// The selection as a phrase, naming the first selected node where the
    /// tree mirror knows it.
    /// </summary>
    public string SelectionLabel => _selectionCount switch
    {
        0 => "nothing selected",
        1 => _selectionName ?? "1 selected",
        _ => _selectionName is { } name
            ? $"{name} +{_selectionCount - 1}"
            : $"{_selectionCount} selected",
    };

    private string? _selectionName;

    private void UpdateSelectionName(FrameSnapshot snapshot)
    {
        string? name = null;
        if (snapshot.SelectedIds.Count > 0 && _tree is { } tree
            && tree.TryGetNode(snapshot.SelectedIds[0], out var node))
        {
            name = node.Name;
        }

        if (!string.Equals(_selectionName, name, StringComparison.Ordinal))
        {
            _selectionName = name;
            Raise(nameof(SelectionLabel));
        }
    }

    /// <summary>How many edits can be undone.</summary>
    public int UndoDepth
    {
        get => _undoDepth;
        private set
        {
            if (!Set(ref _undoDepth, value))
                return;

            Raise(nameof(CanUndo));
            Raise(nameof(UndoTip));
        }
    }

    /// <summary>How many undone edits can be redone.</summary>
    public int RedoDepth
    {
        get => _redoDepth;
        private set
        {
            if (!Set(ref _redoDepth, value))
                return;

            Raise(nameof(CanRedo));
            Raise(nameof(RedoTip));
        }
    }

    /// <summary>Whether the undo button should be live.</summary>
    // Play mode refuses edits but keeps the history, so depth alone is not enough.
    public bool CanUndo => _undoDepth > 0 && !_isPlaying;

    /// <summary>Whether the redo button should be live. See <see cref="CanUndo"/>.</summary>
    public bool CanRedo => _redoDepth > 0 && !_isPlaying;

    /// <summary>
    /// Whether the verbs that change the selection should be live: duplicate,
    /// delete, convert, group, ungroup. False during play.
    /// </summary>
    public bool CanEditSelection => _selectionCount > 0 && !_isPlaying;

    /// <summary>
    /// The undo button's tooltip, which says how deep the history is.
    /// </summary>
    public string UndoTip => _undoDepth == 0
        ? "Nothing to undo"
        : $"Undo, {_undoDepth} step(s) back.  Ctrl+Z";

    /// <summary>The redo button's tooltip. See <see cref="UndoTip"/>.</summary>
    public string RedoTip => _redoDepth == 0
        ? "Nothing to redo"
        : $"Redo, {_redoDepth} step(s) forward.  Ctrl+Y";

    /// <summary>The engine's smoothed frame rate.</summary>
    public double Fps
    {
        get => _fps;
        private set
        {
            if (Set(ref _fps, value))
                Raise(nameof(FpsLabel));
        }
    }

    /// <summary>The engine's smoothed frame time.</summary>
    public double FrameTimeMs
    {
        get => _frameTimeMs;
        private set
        {
            if (!Set(ref _frameTimeMs, value))
                return;

            Raise(nameof(FrameTimeLabel));
            Raise(nameof(FrameTimeOverBudget));
        }
    }

    /// <summary>Whether the frame is over a 30 Hz budget, for the readout's colour.</summary>
    public bool FrameTimeOverBudget => _frameTimeMs > 33.0;

    /// <summary>Frame rate, formatted.</summary>
    public string FpsLabel => $"{_fps,5:0} fps";

    private float _sharedAcquirePeakMs;

    /// <summary>
    /// The longest wait the render thread spent on the shared target's key in
    /// the last publish window, in milliseconds. When it rises with a falling
    /// frame rate, the UI thread is holding the engine up.
    /// </summary>
    public float SharedAcquirePeakMs
    {
        get => _sharedAcquirePeakMs;
        private set
        {
            if (Set(ref _sharedAcquirePeakMs, value))
            {
                Raise(nameof(SharedAcquireLabel));
                Raise(nameof(SharedAcquireVisible));
            }
        }
    }

    /// <summary>
    /// Whether the producer waited long enough to be worth showing.
    /// </summary>
    // The mutex paces the producer, so a healthy 60 Hz consumer already costs
    // about 15.7 ms peak (measured with --pacing-probe). 20 ms sits above that
    // and below a 40 Hz turn (24 ms), so the chip shows only for missed turns.
    public bool SharedAcquireVisible => _sharedAcquirePeakMs >= 20f;

    /// <summary>The wait, as the status bar shows it.</summary>
    public string SharedAcquireLabel => $"turn late {_sharedAcquirePeakMs,4:0.0} ms";

    /// <summary>Frame time, formatted.</summary>
    public string FrameTimeLabel => $"{_frameTimeMs,6:0.00} ms";

    /// <summary>How many static-world compiles have landed.</summary>
    public int CompileCount
    {
        get => _compileCount;
        private set => Set(ref _compileCount, value);
    }

    /// <summary>
    /// The viewport camera's position, formatted for the header strip.
    /// </summary>
    public string CameraPositionLabel { get; private set; } = string.Empty;

    // Compared rounded, so the label re-formats only when a shown digit moves.
    // NaN never equals itself, which forces the first publish.
    private System.Numerics.Vector3 _cameraShown = new(float.NaN);

    private void UpdateCameraReadout(System.Numerics.Vector3 position)
    {
        var rounded = new System.Numerics.Vector3(
            MathF.Round(position.X, 1), MathF.Round(position.Y, 1), MathF.Round(position.Z, 1));
        if (rounded == _cameraShown)
            return;

        _cameraShown = rounded;
        CameraPositionLabel = string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"{rounded.X:0.0}  {rounded.Y:0.0}  {rounded.Z:0.0}");
        Raise(nameof(CameraPositionLabel));
    }

    /// <summary>How many nodes the tree holds.</summary>
    public int NodeCount
    {
        get => _nodeCount;
        private set
        {
            if (Set(ref _nodeCount, value))
            {
                Raise(nameof(TreeCountLabel));
                Raise(nameof(NodeCountLabel));
            }
        }
    }

    /// <summary>The scene's population, for the status bar.</summary>
    public string NodeCountLabel => _nodeCount == 1 ? "1 node" : $"{_nodeCount} nodes";

    /// <summary>How many nodes pass the filter.</summary>
    public int MatchCount
    {
        get => _matchCount;
        private set
        {
            if (!Set(ref _matchCount, value))
                return;

            Raise(nameof(TreeCountLabel));
            Raise(nameof(HasNoMatches));
            Raise(nameof(NoMatchLabel));
        }
    }

    /// <summary>
    /// The tree's population, shown as a fraction only while a filter narrows
    /// it.
    /// </summary>
    public string TreeCountLabel =>
        _matchCount == _nodeCount ? $"{_nodeCount}" : $"{_matchCount} / {_nodeCount}";

    /// <summary>
    /// Whether a filter is on and nothing passes it.
    /// </summary>
    // The tree dims non-matches instead of hiding them, so zero matches would
    // otherwise look like a broken panel.
    public bool HasNoMatches => _filterText.Length > 0 && _matchCount == 0;

    /// <summary>What to say when the filter matched nothing.</summary>
    public string NoMatchLabel =>
        _tree is { FilterIsUnknown: true }
            ? $"“{_filterText}” is not a kind. Try t:block, t:part, t:cut, t:light, t:mesh or t:group."
            : $"Nothing here is called “{_filterText}”.";

    /// <summary>The viewport's pixel size, which is not the window's.</summary>
    public string ViewportLabel => $"{_viewportWidth}x{_viewportHeight}";

    /// <summary>Records the viewport's current pixel size.</summary>
    public void SetViewportSize(int width, int height)
    {
        _viewportWidth = width;
        _viewportHeight = height;
        Raise(nameof(ViewportLabel));
    }

    private ViewportDropPrompt _dropPrompt = ViewportDropPrompt.None;

    /// <summary>
    /// What the viewport draws over the picture while an asset drag is over it.
    /// </summary>
    // Set at pointer rate; an unchanged prompt raises nothing.
    public ViewportDropPrompt DropPrompt
    {
        get => _dropPrompt;
        set
        {
            if (_dropPrompt == value)
                return;

            _dropPrompt = value;
            Raise(nameof(DropVisible));
            Raise(nameof(DropAccepts));
            Raise(nameof(DropHeadline));
            Raise(nameof(DropSubject));
            Raise(nameof(DropReason));
            Raise(nameof(DropHint));
            Raise(nameof(DropHasHint));
            Raise(nameof(DropIcon));
        }
    }

    /// <summary>Whether the drop overlay is drawn.</summary>
    public bool DropVisible => _dropPrompt.IsVisible;

    /// <summary>Whether letting go would place something.</summary>
    public bool DropAccepts => _dropPrompt.Accepts;

    /// <summary>The overlay's verdict.</summary>
    public string DropHeadline => _dropPrompt.Headline;

    /// <summary>What would be placed, as the path the engine names it by.</summary>
    public string DropSubject => _dropPrompt.Subject;

    /// <summary>Why not, when the answer is no.</summary>
    public string DropReason => _dropPrompt.Reason;

    /// <summary>What would be covered, and which key changes it.</summary>
    public string DropHint => _dropPrompt.Hint;

    /// <summary>Whether there is a hint line to draw.</summary>
    public bool DropHasHint => _dropPrompt.Hint.Length > 0;

    /// <summary>
    /// The glyph beside the verdict, resolved from the theme by name. Null
    /// when the resource is missing.
    /// </summary>
    public Geometry? DropIcon =>
        Application.Current?.TryFindResource(_dropPrompt.IconKey, out object? value) == true
            ? value as Geometry
            : null;

    /// <summary>The transient message zone, at the left of the status bar.</summary>
    public string Message
    {
        get => _message;
        private set
        {
            if (Set(ref _message, value))
                Raise(nameof(HasMessage));
        }
    }

    /// <summary>Whether the current message is a failure.</summary>
    public bool IsError
    {
        get => _isError;
        private set => Set(ref _isError, value);
    }

    /// <summary>Whether there is anything to show in the message zone.</summary>
    public bool HasMessage => _message.Length > 0;

    private string? _worldDefect;

    /// <summary>
    /// Why the level has stopped rebuilding, or null when it is current.
    /// </summary>
    // Not on the message line: that is last-writer-wins and this stands until
    // it is fixed.
    public string? WorldDefect
    {
        get => _worldDefect;
        private set
        {
            if (!Set(ref _worldDefect, value))
                return;

            Raise(nameof(HasWorldDefect));
            Raise(nameof(WorldDefectTip));
        }
    }

    /// <summary>Whether to show the standing world warning.</summary>
    public bool HasWorldDefect => !string.IsNullOrEmpty(_worldDefect);

    /// <summary>The warning's full text, for its tooltip.</summary>
    public string WorldDefectTip =>
        $"The level has stopped rebuilding, so the viewport is showing the last version that compiled. {_worldDefect}";

    private int _debugLayerErrors;
    private bool _debugLayerActive;

    /// <summary>
    /// How many errors the graphics validation layer has reported this session.
    /// </summary>
    public int DebugLayerErrors
    {
        get => _debugLayerErrors;
        private set
        {
            if (!Set(ref _debugLayerErrors, value))
                return;

            Raise(nameof(HasDebugLayerErrors));
            Raise(nameof(DebugLayerLabel));
            Raise(nameof(DebugLayerClean));
            Raise(nameof(DebugLayerTip));
        }
    }

    /// <summary>Whether the layer producing that count is running at all.</summary>
    public bool DebugLayerActive
    {
        get => _debugLayerActive;
        private set
        {
            if (!Set(ref _debugLayerActive, value))
                return;

            Raise(nameof(DebugLayerClean));
            Raise(nameof(DebugLayerTip));
        }
    }

    /// <summary>Whether to show the standing graphics warning.</summary>
    public bool HasDebugLayerErrors => _debugLayerErrors > 0;

    /// <summary>
    /// Whether the detector is running and has reported nothing.
    /// </summary>
    // Not just "count is zero": an inactive layer also reports zero.
    public bool DebugLayerClean => _debugLayerActive && _debugLayerErrors == 0;

    /// <summary>What the graphics detector has to say, for the slot's tooltip.</summary>
    public string DebugLayerTip
    {
        get
        {
            if (!_debugLayerActive)
            {
                return "The graphics validation layer is not running, so nothing is watching for " +
                    "driver-level faults. Re-run with --debug-layer=true for the full check.";
            }

            return _debugLayerErrors == 0
                ? "The graphics validation layer is running and has reported nothing."
                : $"The graphics validation layer has reported {_debugLayerErrors} error(s). These draw a " +
                    "picture rather than stopping the frame, so the viewport cannot show them; the run log has the detail.";
        }
    }

    /// <summary>The standing warning's text.</summary>
    public string DebugLayerLabel =>
        _debugLayerErrors == 1 ? "1 graphics error" : $"{_debugLayerErrors} graphics errors";

    private int _placeholderBound;

    /// <summary>
    /// How many asset references are standing on a failure and drawing the
    /// magenta checker.
    /// </summary>
    public int PlaceholderBoundCount
    {
        get => _placeholderBound;
        private set
        {
            if (!Set(ref _placeholderBound, value))
                return;

            Raise(nameof(HasPlaceholderBound));
            Raise(nameof(PlaceholderBoundLabel));
            Raise(nameof(PlaceholderBoundTip));
        }
    }

    /// <summary>Whether to show the standing missing-asset warning.</summary>
    public bool HasPlaceholderBound => _placeholderBound > 0;

    /// <summary>The standing warning's text.</summary>
    public string PlaceholderBoundLabel =>
        _placeholderBound == 1 ? "1 missing asset" : $"{_placeholderBound} missing assets";

    /// <summary>What the slot has to say, for its tooltip.</summary>
    public string PlaceholderBoundTip =>
        $"{_placeholderBound} texture or material reference(s) could not be resolved, so those " +
        "surfaces draw the magenta checker. Geometry that names no material at all is not counted " +
        "here and is drawn flat grey.";

    /// <summary>Reports something that went normally.</summary>
    public void SetMessage(string text)
    {
        Message = text;
        IsError = false;
        Output.Append(OutputSeverity.Info, text);
    }

    /// <summary>Reports a failure, which stays until something replaces it.</summary>
    public void SetError(string text)
    {
        Message = text;
        IsError = true;
        Output.Append(OutputSeverity.Error, text);
    }

    /// <summary>Reports something that is not right but did not stop anything.</summary>
    public void SetWarning(string text)
    {
        Message = text;
        IsError = false;
        Output.Append(OutputSeverity.Warning, text);
    }

    /// <summary>
    /// Everything the shell has reported, oldest first. The status line shows
    /// its newest entry.
    /// </summary>
    public OutputLog Output { get; } = new();

    /// <summary>
    /// What is wrong right now: one row per standing condition, kept until
    /// something ends it.
    /// </summary>
    public ProblemList Problems { get; } = new();

    private WorkspacePreset _workspacePreset = WorkspacePreset.Compact;
    private bool _drawerOpen;
    private bool _viewportMaximised;
    private bool _showDiagnostics;

    /// <summary>How the window is arranged.</summary>
    public WorkspacePreset WorkspacePreset
    {
        get => _workspacePreset;
        set
        {
            if (!Set(ref _workspacePreset, value)) return;

            Raise(nameof(IsCompactWorkspace));
            Raise(nameof(IsExpandedWorkspace));
        }
    }

    /// <summary>Whether the compact arrangement is live.</summary>
    public bool IsCompactWorkspace => _workspacePreset == WorkspacePreset.Compact;

    /// <summary>Whether the roomy one is.</summary>
    public bool IsExpandedWorkspace => _workspacePreset == WorkspacePreset.Expanded;

    /// <summary>Whether the bottom region is showing.</summary>
    public bool IsDrawerOpen
    {
        get => _drawerOpen;
        set => Set(ref _drawerOpen, value);
    }

    /// <summary>Whether the viewport has the whole window.</summary>
    public bool IsViewportMaximised
    {
        get => _viewportMaximised;
        set => Set(ref _viewportMaximised, value);
    }

    private ViewArrangement _viewArrangement = ViewArrangement.Single;

    /// <summary>Which view panes the centre of the window shows.</summary>
    public ViewArrangement ViewArrangement
    {
        get => _viewArrangement;
        set
        {
            if (!Set(ref _viewArrangement, value)) return;

            Raise(nameof(IsLogicHidden));
            Raise(nameof(IsLogicBelow));
            Raise(nameof(IsLogicBeside));
        }
    }

    /// <summary>Whether the 3D view has the centre to itself.</summary>
    public bool IsLogicHidden => _viewArrangement == ViewArrangement.Single;

    /// <summary>Whether the Logic view shows under the 3D view.</summary>
    public bool IsLogicBelow => _viewArrangement == ViewArrangement.LogicBelow;

    /// <summary>Whether it shows beside it.</summary>
    public bool IsLogicBeside => _viewArrangement == ViewArrangement.LogicBeside;

    private string _interactionState = string.Empty;
    private string _gestureHint = string.Empty;

    /// <summary>
    /// What the mouse and the modifiers do right now.
    /// </summary>
    public string GestureHint
    {
        get => _gestureHint;
        private set => Set(ref _gestureHint, value);
    }

    private void RefreshGestureHint() =>
        GestureHint = GestureHints.For(_interactionState, _gizmoMode, _snapEnabled);

    /// <summary>Whether the status bar shows the engine counters. Off by default.</summary>
    public bool ShowDiagnostics
    {
        get => _showDiagnostics;
        set => Set(ref _showDiagnostics, value);
    }

    /// <summary>The project's assets, browsed. Assigned by the window.</summary>
    public ContentBrowserModel? Content { get; set; }

    /// <summary>
    /// The project's files, for the pickers that assign them.
    /// </summary>
    public AssetCatalog? Assets { get; set; }

    /// <summary>Which sound the engine is playing by itself, for the play buttons beside sound files.</summary>
    public SoundPreviewModel SoundPreview { get; } = new();

    /// <summary>
    /// The scene filter's text. Applied to the tree after a short pause rather
    /// than per keystroke, because a filter pass touches every node.
    /// </summary>
    public string FilterText
    {
        get => _filterText;
        set
        {
            if (!Set(ref _filterText, value))
                return;

            Raise(nameof(HasFilter));
            Raise(nameof(HasNoMatches));
            Raise(nameof(NoMatchLabel));
            _filterDebounce?.Stop();
            _filterDebounce ??= new DispatcherTimer(
                TimeSpan.FromMilliseconds(120), DispatcherPriority.Background, (_, _) => ApplyFilterNow());
            _filterDebounce.Start();
        }
    }

    /// <summary>
    /// The property panel, or null until the session exists to apply edits
    /// through.
    /// </summary>
    public PropertyPanelModel? Properties
    {
        get => _properties;
        set
        {
            if (Set(ref _properties, value))
                Raise(nameof(HasProperties));
        }
    }

    /// <summary>Whether the panel is wired up at all.</summary>
    public bool HasProperties => _properties is not null;

    /// <summary>
    /// The entity classes the Insert menu offers, in catalogue order. They
    /// come from the open project's <c>.sentdef</c>; see
    /// <see cref="EntityInsertMenu"/>.
    /// </summary>
    public ObservableCollection<EntityInsertItem> EntityClasses { get; } = [];

    /// <summary>Whether there is anything to put in the entity submenu.</summary>
    public bool HasEntityClasses => EntityClasses.Count > 0;

    /// <summary>
    /// Replaces the entity submenu's entries. Called when a session opens with
    /// its catalogue, and with null when one closes.
    /// </summary>
    public void SetEntityClasses(IReadOnlyList<EntityInsertItem>? items)
    {
        EntityClasses.Clear();
        MakeEntityClasses.Clear();
        if (items is not null)
        {
            foreach (EntityInsertItem item in items)
                EntityClasses.Add(item);

            foreach (EntityInsertItem item in EntityInsertMenu.MadeFromGeometry(items))
                MakeEntityClasses.Add(item);
        }

        Raise(nameof(HasEntityClasses));
        Raise(nameof(CanInsertEntity));
        Raise(nameof(HasMakeEntityClasses));
        Raise(nameof(CanMakeEntity));
    }

    /// <summary>
    /// Whether the ribbon's Entity split button can do anything.
    /// </summary>
    // Gates both halves of the split. Raised from SetEntityClasses and from
    // the IsPlaying setter.
    public bool CanInsertEntity => EntityClasses.Count > 0 && !_isPlaying;

    /// <summary>
    /// The classes Make entity offers: the ones made from a block, a part or
    /// a group of them. A subset of <see cref="EntityClasses"/>, in the same order.
    /// </summary>
    public ObservableCollection<EntityInsertItem> MakeEntityClasses { get; } = [];

    /// <summary>Whether there is anything to put in a Make entity list.</summary>
    public bool HasMakeEntityClasses => MakeEntityClasses.Count > 0;

    /// <summary>
    /// Whether Make entity can do anything: a class to make, and a selection
    /// to make it from.
    /// </summary>
    // Raised wherever CanEditSelection is, and from SetEntityClasses.
    public bool CanMakeEntity => MakeEntityClasses.Count > 0 && CanEditSelection;

    /// <summary>Whether a filter is narrowing the tree, for the clear button.</summary>
    public bool HasFilter => _filterText.Length > 0;

    /// <summary>Clears the filter immediately.</summary>
    public void ClearFilter()
    {
        FilterText = string.Empty;
        ApplyFilterNow();
    }

    private void ApplyFilterNow()
    {
        _filterDebounce?.Stop();

        if (_tree is not { } tree)
            return;

        tree.ApplyFilter(_filterText);
        MatchCount = tree.MatchCount;

        // Always raised: FilterIsUnknown can flip while the count stays 0
        // ("t:zz" to "zzz").
        Raise(nameof(NoMatchLabel));
    }

    /// <summary>
    /// Copies one finished frame's values in. UI thread, from the pump.
    /// </summary>
    public void ApplySnapshot(FrameSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        // So writing the engine's value into a two-way binding is not taken
        // for a user choice.
        _applyingSnapshot = true;
        try
        {
            Fps = snapshot.Fps;
            SharedAcquirePeakMs = snapshot.SharedAcquirePeakMs;
            FrameTimeMs = snapshot.FrameTimeMs;
            SelectionCount = snapshot.SelectedIds.Count;
            UpdateSelectionName(snapshot);
            UpdateCameraReadout(snapshot.CameraPosition);
            CompileCount = snapshot.StaticWorldCompileCount;
            WorldDefect = snapshot.StaticWorldDefect;

            // Active first: the tooltip reads the count against it.
            DebugLayerActive = snapshot.DebugLayerActive;
            DebugLayerErrors = snapshot.DebugLayerErrorCount;
            PlaceholderBoundCount = snapshot.PlaceholderBoundCount;

            string interaction = snapshot.InteractionStateName ?? string.Empty;
            if (!string.Equals(interaction, _interactionState, StringComparison.Ordinal))
            {
                _interactionState = interaction;
                RefreshGestureHint();
            }

            _historyOpt.Apply((snapshot.UndoDepth, snapshot.RedoDepth));
            ApplyHistory(true);

            _modeOpt.Apply(snapshot.GizmoModeName ?? "move");
            GizmoMode = _modeOpt.Value;

            _styleOpt.Apply(snapshot.GizmoStyleName ?? "Studio");
            GizmoStyle = _styleOpt.Value;

            _orientationOpt.Apply(snapshot.GizmoOrientationName ?? "world");
            Orientation = _orientationOpt.Value;

            _snapOpt.Apply(snapshot.SnapEnabled);
            SnapEnabled = _snapOpt.Value;

            // While a tool switch is unconfirmed the reported increment still
            // belongs to the previous tool.
            if (!_modeOpt.HasPending)
                SnapIncrement = snapshot.SnapIncrement;

            Navigation = snapshot.NavigationModeName ?? "-";
            ApplyGridMode(snapshot.GridModeName ?? "auto");
            ApplyViewName(snapshot.ViewName ?? "Perspective");

            _playOpt.Apply(snapshot.IsPlaying);
            IsPlaying = _playOpt.Value;

            CanPlay = snapshot.CanPlay;
            PipelineNames = snapshot.PipelineNames;
            PipelineName = snapshot.PipelineName;

            _debugOpt.Apply(snapshot.DebugFlags);
            SetDebugFlags(_debugOpt.Value);

            _properties?.Apply(
                snapshot.SelectionProperties, snapshot.SelectedIds.Count, snapshot.SelectionEntity);

            SoundPreview.Apply(snapshot.PreviewingSound);

            if (_tree is { } tree)
            {
                NodeCount = tree.Count;
                MatchCount = _filterText.Length == 0 ? tree.Count : tree.MatchCount;
            }
            else
            {
                // A viewport left stopped keeps its panels up with no tree.
                NodeCount = 0;
                MatchCount = 0;
            }
        }
        finally
        {
            _applyingSnapshot = false;
        }
    }
}
