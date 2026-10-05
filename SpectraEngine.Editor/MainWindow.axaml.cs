using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Microsoft.Extensions.Logging;
using Serilog.Extensions.Logging;
using Silk.NET.Maths;
using Spectra.Kitchen.Diagnostics;
using SpectraEngine.Core.ConsoleSystem;
using SpectraEngine.Core.Graphics;
using SpectraEngine.Core.Hosting;
using SpectraEngine.Core.Maps;
using SpectraEngine.Core.Projects;
using SpectraEngine.Core.Scene;
using SpectraEngine.Editing.Cameras;
using SpectraEngine.Editing.Gizmos;
using SpectraEngine.Editing.Commands;
using SpectraEngine.Editing.Hosting;
using SpectraEngine.Editor.Shell;
using SpectraEngine.Editor.Shell.Ribbon;
using SpectraEngine.Editor.Viewport;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SpectraEngine.Editor;

/// <summary>
/// The shell window: the start page, the tabbed command bar, the docked
/// panels around a pinned viewport, and the status bar.
/// </summary>
// The UI thread reads published snapshots and posts commands; it never touches
// a Scene, a SceneNode or the renderer.
public partial class MainWindow : Window
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<MainWindow> _logger;
    private readonly DispatcherTimer _pump;
    private readonly ShellModel _shell = new();
    private readonly CaptionOutput _captions;
    private readonly EditorDocument _document = new();

    // Every snapshot is queued, not just the newest: a structural change rides
    // one snapshot and is then gone, so sampling the latest drops graph edits.
    private readonly ConcurrentQueue<FrameSnapshot> _published = new();

    // Bounded so a stalled UI thread can't grow the queue forever. About five
    // seconds at the 120Hz interactive publish rate, half a minute at rest.
    private const int MaxQueuedSnapshots = 600;
    private int _queuedSnapshots;
    private volatile bool _droppedSnapshots;

    // Set on the render thread per publish, cleared when the pump starts:
    // one post per UI frame.
    private int _pumpPosted;

    private ContentPanel? _contentView;
    private Dock.Model.Avalonia.Factory _dockFactory = new();
    private OutputPanel? _outputView;
    private ProblemsPanel? _problemsView;
    private ConsolePanel? _consoleView;
    private ConsoleCommands? _console;
    private readonly ConsoleLineFeed _consoleFeed = new();

    private EditorSession? _session;
    private SceneTreeModel? _tree;
    private IRenderSurface? _surface;
    private FrameSnapshot _latest = FrameSnapshot.Empty;

    // Lets the watchdog timer skip a snapshot it already applied. Compared by
    // reference: the engine publishes a fresh instance each time.
    private FrameSnapshot _lastApplied = FrameSnapshot.Empty;
    private bool _stopping;
    private int _lastUndoDepth;
    private int _lastRedoDepth;

    private readonly EditorSettings _settings;

    // Exists only while a session does: a native child window and a composited
    // viewport's imported texture both live as long as the control is in the tree.
    private IEngineViewport? _viewport;

    private ViewportPlacement _placement = ViewportPlacement.PinnedCell;

    // The viewport tool's content for the window's life; the view panes move in
    // and out of it, so Dock never sees a content change after its layout is built.
    private readonly Border _viewportDockHost = new();

    private readonly int _viewPanesIndex;

    // Set by LaunchSession, consumed by OnSurfaceCreated.
    private SessionLaunch? _pendingLaunch;

    // Fields rather than XAML names: a dock tool would template XAML children
    // instead of keeping one instance.
    private readonly ScenePanel _sceneView;
    private readonly PropertiesPanel _propertiesView;
    private readonly MapsPanel _mapsView;

    // One live instance per ribbon page, moved between the inline host and the
    // flyout rather than rebuilt.
    private readonly RibbonBuildTab _buildTab = new();
    private readonly RibbonViewTab _viewTab = new();
    private readonly Dictionary<string, RibbonTabView> _ribbonPages = new(StringComparer.Ordinal);

    private RibbonSurfaceState _ribbon;

    // Applying the state closes the popup, whose Closed would apply it again.
    private bool _applyingRibbonState;

    /// <summary>Creates the window and wires the viewport's lifetime to the engine's.</summary>
    public MainWindow()
    {
        InitializeComponent();

        DataContext = _shell;
        _captions = new CaptionOutput(_shell.Output);

        HeaderStrip.Activated += OnHeaderAction;
        LogicHeader.HideRequested += () => OnShellVerb(ShellVerb.Of(WorkspaceCommand.HideLogic));

        // Somewhere for focus to land when a field must blur (CommitFocusedEdit).
        Focusable = true;

        _loggerFactory = new SerilogLoggerFactory(Serilog.Log.Logger, dispose: false);
        _logger = _loggerFactory.CreateLogger<MainWindow>();

        _settings = EditorSettings.Load(_logger);

        // --viewport= is persisted, not a one-run override; there is no UI for it.
        if (ViewportModePolicy.RequestedMode(Program.StartupArgs) is { } requestedViewport)
        {
            _settings.SetViewportMode(requestedViewport);
            _settings.Save(_logger);
            _logger.LogInformation(
                "Viewport mode set to {Mode} by the command line ({Usage}).",
                ViewportModePolicy.NameOf(requestedViewport), ViewportModePolicy.Usage);
        }

        VersionLabel.Text = SpectraEngine.Core.EngineInfo.VersionString;

        StartView.NewProjectRequested += () => _ = CreateProjectFlowAsync();
        StartView.OpenProjectRequested += () => _ = OpenProjectFlowAsync();
        StartView.OpenMapRequested += () => _ = OpenLooseMapFlowAsync();
        StartView.RecentProjectPicked += recent => _ = OpenRecentProjectAsync(recent);
        StartView.RecentProjectForgotten += ForgetRecent;
        StartView.RecentProjectRevealRequested += recent => RevealInExplorer(recent.Path);
        RefreshRecents();

        MoveToolTabsAbove();

        // One factory for every dock control, assigned before the window attaches.
        // With none, DockControl.Initialize bails and the docks render empty; with
        // one per control, a panel can't be dragged from one dock to another.
        _dockFactory = new Dock.Model.Avalonia.Factory();
        Dock.Model.Avalonia.Factory dockFactory = _dockFactory;
        LeftDock.Factory = dockFactory;
        RightDock.Factory = dockFactory;
        BottomDock.Factory = dockFactory;
        CenterDock.Factory = dockFactory;

        SetToolContent(ViewportTool, _viewportDockHost);
        _viewPanesIndex = EditorView.Children.IndexOf(ViewPanes);

        ViewColumnSplitter.Tag = ViewColumnSplitInk;
        ViewRowSplitter.Tag = ViewRowSplitInk;

        // The viewport tool has no XAML attribute for CanPin, so set it here.
        ApplyPlacement(ViewportPlacement.PinnedCell);

        // Panels are handed to the dock tools as live controls, so one wired
        // instance survives every re-dock and float.
        _sceneView = new ScenePanel
        {
            Logger = _loggerFactory.CreateLogger<ScenePanel>(),
        };
        _sceneView.SelectionRequested += ids => _session?.SelectMany(ids);
        _sceneView.RenameRequested += (id, name) => _session?.Rename(id, name);
        _sceneView.CommandRequested += command => _session?.Post(command);
        _sceneView.FrameRequested += () => _session?.Post(EditorCameraCommand.FrameSelection);
        _sceneView.ReparentRequested += (ids, parentId, index) => _session?.Reparent(ids, parentId, index);
        _sceneView.MakeEntityRequested += MakeEntity;
        _sceneView.RemoveEntityRequested += RemoveEntity;
        SetToolContent(SceneTool, _sceneView);

        _propertiesView = new PropertiesPanel();
        _propertiesView.EscapePressed += () => _viewport?.FocusEngine();
        _propertiesView.SelectRequested += id => _session?.Select(id);
        SetToolContent(PropertiesTool, _propertiesView);

        _mapsView = new MapsPanel();
        _mapsView.MapClicked += row => _ = OpenProjectMapAsync(row);
        _mapsView.SetStartupRequested += SetStartupMap;
        _mapsView.RevealRequested += row =>
        {
            if (_document.Project is { } project)
                RevealInExplorer(project.Resolve(row.RelativePath));
        };
        SetToolContent(MapsTool, _mapsView);

        _shell.Content = new ContentBrowserModel(_loggerFactory.CreateLogger<ContentBrowserModel>());
        _shell.Assets = new AssetCatalog(_loggerFactory.CreateLogger<AssetCatalog>());
        _shell.SoundPreview.Send = SendSoundPreview;
        _shell.SoundPreview.NotSent += () => _shell.SetWarning(SessionFaultText.ListenWhileStopped);

        // Saved on change, not at shutdown, so a crash doesn't lose it.
        _shell.Content.ViewMode = _settings.ContentView;
        _shell.Content.ViewChanged += mode =>
        {
            _settings.SetContentView(mode);
            _settings.Save(_logger);
        };

        _contentView = new ContentPanel();
        _contentView.EntryActivated += OnContentActivated;
        _contentView.RevealRequested += entry => RevealInExplorer(entry.FullPath);
        SetToolContent(ContentTool, _contentView);

        _outputView = new OutputPanel();
        SetToolContent(OutputTool, _outputView);

        _problemsView = new ProblemsPanel();
        _problemsView.EntryActivated += OnProblemActivated;
        SetToolContent(ProblemsTool, _problemsView);

        // Attach delivers everything the relay queued since startup.
        Program.LogRelay.LineArrived += OnEngineLogLine;
        Program.LogRelay.LinesDropped += OnEngineLinesDropped;
        Program.LogRelay.Attach(work => Dispatcher.UIThread.Post(work, DispatcherPriority.Background));

        _consoleView = new ConsolePanel();
        _consoleView.CommandSubmitted += OnConsoleCommand;
        SetToolContent(ConsoleTool, _consoleView);

        CreateLogicView();

        // Each lambda returns false with no session, so the console can say so.
        _console = new ConsoleCommands(
            postHost: command => _session is { } s && Post(() => s.Post(command)),
            postGizmo: command => _session is { } s && Post(() => s.Post(command)),
            postCamera: command => _session is { } s && Post(() => s.Post(command)),
            insert: kind => _session is { } s && Post(() => s.Insert(kind)),
            setSnap: (tool, value) => _session is { } s && Post(() => s.SetSnapIncrement(tool, value)),
            setPipeline: name => _session is { } s && Post(() => s.Host.RequestPipeline(name)),
            setPlaying: playing =>
            {
                _shell.RequestPlaying(playing);
                _session?.Host.RequestPlayMode(playing);
            },
            forward: line => _session is { } s && s.SubmitConsoleLine(line),
            restartViewport: RestartStoppedViewport);

        static bool Post(Action action)
        {
            action();
            return true;
        }

        _document.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is nameof(EditorDocument.Title))
                RefreshDocumentIdentity();

            if (args.PropertyName is nameof(EditorDocument.Project))
            {
                _shell.Content?.SetRoot(_document.Project?.AssetsPath);
                _shell.Assets?.Rebuild(_document.Project?.AssetsPath);
            }
        };
        RefreshDocumentIdentity();
        _shell.AboutLabel = $"Version {SpectraEngine.Core.EngineInfo.VersionString}";

        _shell.Properties = new PropertyPanelModel(
            OnPropertyEdit,
            name => _session?.BeginPropertyGesture(name),
            commit => _session?.EndPropertyGesture(commit),
            OnEntityConnectionsEdit);

        _shell.PipelineRequested += name => _session?.Host.RequestPipeline(name);

        // The one snap field shows the live tool's increment, so re-read it on
        // a tool switch.
        _shell.GizmoModeChanged += () => RefreshSnapField(_latest);

        BuildRibbon();

        // Document chords. The viewport intercepts the same ones (ShellChord):
        // while a native child has focus Avalonia sees no keyboard at all.
        KeyBindings.Add(new KeyBinding
        {
            Gesture = new KeyGesture(Key.N, KeyModifiers.Control),
            Command = new RelayCommand(() => { CommitFocusedEdit(); OnNewMapClicked(this, new RoutedEventArgs()); }),
        });
        KeyBindings.Add(new KeyBinding
        {
            Gesture = new KeyGesture(Key.O, KeyModifiers.Control),
            Command = new RelayCommand(() => { CommitFocusedEdit(); OnOpenMapClicked(this, new RoutedEventArgs()); }),
        });
        KeyBindings.Add(new KeyBinding
        {
            Gesture = new KeyGesture(Key.S, KeyModifiers.Control),
            Command = new RelayCommand(() => { CommitFocusedEdit(); OnSaveClicked(this, new RoutedEventArgs()); }),
        });
        KeyBindings.Add(new KeyBinding
        {
            Gesture = new KeyGesture(Key.S, KeyModifiers.Control | KeyModifiers.Shift),
            Command = new RelayCommand(() => { CommitFocusedEdit(); OnSaveAsClicked(this, new RoutedEventArgs()); }),
        });

        // Window-wide undo/redo. A focused field commits first so undo takes back
        // the typed value. A TextBox still handles Ctrl+Z for its own text.
        KeyBindings.Add(new KeyBinding
        {
            Gesture = new KeyGesture(Key.Z, KeyModifiers.Control),
            Command = new RelayCommand(() => { CommitFocusedEdit(); _session?.Post(EditorHostCommand.Undo); }),
        });
        KeyBindings.Add(new KeyBinding
        {
            Gesture = new KeyGesture(Key.Y, KeyModifiers.Control),
            Command = new RelayCommand(() => { CommitFocusedEdit(); _session?.Post(EditorHostCommand.Redo); }),
        });
        KeyBindings.Add(new KeyBinding
        {
            Gesture = new KeyGesture(Key.Z, KeyModifiers.Control | KeyModifiers.Shift),
            Command = new RelayCommand(() => { CommitFocusedEdit(); _session?.Post(EditorHostCommand.Redo); }),
        });

        // Insert chords, also intercepted in the viewport (ShellChord).
        AddChord(Key.D1, KeyModifiers.Control, () => _session?.Insert(InsertKind.WorldBrush));
        AddChord(Key.D2, KeyModifiers.Control, () => _session?.Insert(InsertKind.PartBrush));
        AddChord(Key.D3, KeyModifiers.Control, () => _session?.Insert(InsertKind.SubtractiveBrush));
        AddChord(Key.D4, KeyModifiers.Control, () => _session?.Insert(InsertKind.PointLight));

        // Window-wide too: the engine keymap only sees keys while the viewport has focus.
        AddChord(Key.F8, KeyModifiers.None, () => _session?.Host.RequestPlayMode(!_latest.IsPlaying));
        AddChord(Key.F, KeyModifiers.None, () => _session?.Post(EditorCameraCommand.FrameSelection));
        AddChord(Key.F, KeyModifiers.Shift, () => _session?.Post(EditorCameraCommand.FrameAll));
        AddChord(Key.A, KeyModifiers.Control, () => _session?.Post(EditorHostCommand.SelectAll));

        AddChord(Key.OemTilde, KeyModifiers.None, () => OnShowConsolePanel(this, new RoutedEventArgs()));

        // Office's chord for collapsing the ribbon; its only keyboard route.
        AddChord(Key.F1, KeyModifiers.Control, () => OnRibbonPinClicked(this, new RoutedEventArgs()));

        AddChord(Key.P, KeyModifiers.Control, TogglePalette);

        AddChord(Key.F11, KeyModifiers.None, () => OnShellChord(ShellChord.MaximiseViewport));
        AddChord(Key.OemTilde, KeyModifiers.Control, () => OnShellChord(ShellChord.ToggleBottomDrawer));
        AddChord(Key.L, KeyModifiers.Control, () => OnShellChord(ShellChord.ToggleLogicView));

        // The window is the drop target: a native viewport never sees Avalonia
        // drag events.
        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);

        // Watchdog. The pump normally runs off OnFrameCompleted; this timer
        // covers the cursor-mode latch, which is not snapshot-driven.
        // 8ms is real only while TimerResolution is held; otherwise Windows
        // rounds it to 15.6. Normal outranks Render in Avalonia (unlike WPF),
        // so what the pump writes shows in the same frame.
        _pump = new DispatcherTimer(
            TimeSpan.FromMilliseconds(8), DispatcherPriority.Normal, OnPump);

        Opened += (_, _) =>
        {
            DarkCaption.Apply(this, _logger);

            // The interop probe runs instead of opening a session, so it doesn't
            // compete with an engine for the GPU. It closes the window itself.
            if (InteropProbe.Requested(Program.StartupArgs))
            {
                _ = RunInteropProbeAsync();
                return;
            }

            OpenFromStartupArgs();
        };

        if (!EngineViewports.IsSupported)
        {
            _shell.SetError(
                "This platform cannot host the viewport yet: the embedded surface is Windows-only in v1.");
        }
        else
        {
            _shell.SetMessage("Open a project to start building, or drop one on this window.");
        }
    }

    // Adds a window-level chord. It commits any focused field first, so Ctrl+S
    // doesn't save without the value just typed.
    private void AddChord(Key key, KeyModifiers modifiers, Action run)
    {
        // TextBox only handles caret and editing keys, so a printable key bubbles
        // to the window binding. Stand down while a text box has focus. Shift
        // counts as no modifier: a capital letter is still typing.
        bool printable = modifiers is KeyModifiers.None or KeyModifiers.Shift
            && (key is >= Key.A and <= Key.Z || key is >= Key.D0 and <= Key.D9
                || key is Key.OemTilde);

        KeyBindings.Add(new KeyBinding
        {
            Gesture = new KeyGesture(key, modifiers),
            Command = new RelayCommand(() =>
            {
                if (printable && FocusManager?.GetFocusedElement() is TextBox)
                    return;

                CommitFocusedEdit();
                run();
            }),
        });
    }

    private void RefreshDocumentIdentity()
    {
        string project = _document.Project?.Project.Name ?? string.Empty;
        _shell.SetDocument(_document.MapLabel, project, _document.IsDirty);

        if (!_shell.HasSession)
        {
            Title = "Spectra Editor";
            return;
        }

        string mark = _document.IsDirty ? "*" : string.Empty;
        Title = project.Length == 0 || string.Equals(project, _document.MapLabel, StringComparison.Ordinal)
            ? $"{_document.MapLabel}{mark} - Spectra Editor"
            : $"{_document.MapLabel}{mark} - {project} - Spectra Editor";
    }

    // Fills the Renderer submenu from the pipelines the running backend offers.
    private void RefreshRendererMenu()
    {
        RendererMenu.Items.Clear();

        foreach (string name in _shell.PipelineNames)
        {
            string pipeline = name;
            var item = new MenuItem
            {
                Header = pipeline,
                ToggleType = MenuItemToggleType.Radio,
                GroupName = "renderer",
                IsChecked = string.Equals(pipeline, _shell.PipelineName, StringComparison.Ordinal),
            };
            item.Click += (_, _) => _session?.Host.RequestPipeline(pipeline);
            RendererMenu.Items.Add(item);
        }

        RendererMenu.IsEnabled = RendererMenu.Items.Count > 0;
    }

    // Opens the first startup argument that names a manifest, a project folder
    // or a map bundle.
    private void OpenFromStartupArgs()
    {
        foreach (string arg in Program.StartupArgs)
        {
            if (arg.Length == 0 || arg[0] == '-')
                continue;

            switch (arg.ToLowerInvariant())
            {
                case "d3d11" or "d3d12" or "opengl":
                    continue;
            }

            if (TryOpenPath(arg))
                return;
        }
    }

    // Shared by the command line and drag-and-drop. True when a launch started.
    private bool TryOpenPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;

        if (File.Exists(path) &&
            path.EndsWith(ProjectFormat.Extension, StringComparison.OrdinalIgnoreCase))
        {
            OpenProjectAt(path);
            return true;
        }

        if (!Directory.Exists(path))
            return false;

        if (MapBundle.IsBundle(path))
        {
            _document.SetProject(null);
            LaunchSession(new SessionLaunch(null, null, Path.GetFullPath(path)));
            return true;
        }

        if (Directory.GetFiles(path, "*" + ProjectFormat.Extension).Length >= 1)
        {
            OpenProjectAt(path);
            return true;
        }

        return false;
    }

    // Opens a project or level dropped onto the window. Refused during play.
    private async Task DropAsync(IEnumerable<Avalonia.Platform.Storage.IStorageItem> items)
    {
        if (_latest.IsPlaying)
        {
            _shell.SetMessage("Stop the run before opening something else.");
            return;
        }

        foreach (Avalonia.Platform.Storage.IStorageItem item in items)
        {
            string? path = item.TryGetLocalPath();
            if (string.IsNullOrEmpty(path))
                continue;

            // Classify before the unsaved-work prompt.
            bool openable = (File.Exists(path)
                    && path.EndsWith(ProjectFormat.Extension, StringComparison.OrdinalIgnoreCase))
                || (Directory.Exists(path)
                    && (MapBundle.IsBundle(path)
                        || Directory.GetFiles(path, "*" + ProjectFormat.Extension).Length >= 1));

            if (!openable)
                continue;

            if (!await ConfirmDiscardAsync("opening what you dropped"))
                return;

            if (TryOpenPath(path))
                return;
        }

        _shell.SetError("That is not a Spectra project or level folder.");
    }

    // Creates the viewport and switches to the editor view. The engine session
    // is built when the surface arrives.
    private void LaunchSession(SessionLaunch launch)
    {
        if (_viewport is not null || _launchInFlight)
        {
            _logger.LogWarning("A session is already running; ignoring the launch request");
            return;
        }

        if (!EngineViewports.IsSupported)
        {
            _shell.SetError(
                "This platform cannot host the viewport yet: the embedded surface is Windows-only in v1.");
            return;
        }

        _launchInFlight = true;
        _ = LaunchSessionAsync(launch);
    }

    // Covers the gap while the compositor is being measured and no viewport
    // exists yet.
    private bool _launchInFlight;

    // Read again when the session closes, to decide whether it counted as green.
    private ViewportCapabilities _sessionCapabilities = ViewportCapabilities.NotMeasured;
    private bool _sessionIsComposited;
    private bool _sessionFaulted;
    private bool _sessionEngineDied;
    private int _sessionDebugLayerErrors;

    // Picks the viewport kind, then launches. The rehearsal import runs before
    // the session exists, so a compositor that refuses the texture is found
    // with no engine running. Must not throw: any failure falls back to native.
    private async Task LaunchSessionAsync(SessionLaunch launch)
    {
        ViewportDecision decision = new(
            UseComposition: false,
            ViewportChoiceReason.ExplicitNative,
            ViewportModePolicy.Describe(ViewportChoiceReason.ExplicitNative));

        try
        {
            GraphicsBackend backend = ResolveRequestedBackend();
            ViewportPreference preference = _settings.ViewportPreference;
            var capabilities = ViewportCapabilities.NotMeasured;

            if (ViewportModePolicy.RequiresMeasurement(preference, backend))
            {
                capabilities = await ViewportProbe.MeasureAsync(
                    this, backend, _loggerFactory.CreateLogger(nameof(ViewportProbe)));
            }

            decision = ViewportModePolicy.Decide(preference, capabilities, backend);
            _sessionCapabilities = capabilities;

            // Rebase after the decision: the old count is what produces the
            // AdapterChanged/DriverChanged reasons. Skipped when nothing was
            // measured, or empty strings would overwrite a real history.
            if (capabilities.AdapterLuid.Length > 0)
            {
                _settings.RebaseViewport(capabilities.AdapterLuid, capabilities.DriverVersion);
                _settings.Save(_logger);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Choosing the viewport failed; falling back to the native child");
        }
        finally
        {
            _launchInFlight = false;
        }

        // Always log the choice: both viewports render the same picture, so a
        // fallback is otherwise invisible.
        ViewportPlacement placement = ViewportLayout.For(decision);

        _logger.LogInformation(
            "Viewport: {Choice} ({Reason}) - {Explanation} Layout: {Placement} - {Rule}",
            decision.UseComposition ? "composition" : "native child",
            decision.Reason,
            decision.Explanation,
            placement,
            ViewportLayout.Describe(placement));

        StartViewport(launch, decision.UseComposition, placement);
    }

    private void StartViewport(SessionLaunch launch, bool composited, ViewportPlacement placement)
    {
        _pendingLaunch = launch;
        _recovery.Launched(launch);
        _sessionIsComposited = composited;
        _sessionFaulted = false;
        _sessionEngineDied = false;
        _sessionDebugLayerErrors = 0;

        IEngineViewport viewport = EngineViewports.Create(
            composited, _loggerFactory, _shell.SetError, OnViewportFailed);
        viewport.SurfaceCreated += OnSurfaceCreated;
        viewport.SurfaceDestroying += OnSurfaceDestroying;

        viewport.ShellChord += OnShellChord;
        viewport.ContextMenuRequested += OnViewportContextMenu;
        viewport.AssetDropped += OnViewportAssetDropped;
        viewport.AssetDragChanged += OnViewportAssetDragChanged;
        _viewport = viewport;

        // Held for the session only; a raised timer rate costs battery.
        TimerResolution.Acquire(_logger);

        // A restart after a fault keeps the workspace the user arranged.
        bool restarting = _shell.HasSession;
        if (!restarting)
        {
            StartView.IsVisible = false;
            EditorView.IsVisible = true;
            _shell.HasSession = true;
            RefreshDocumentIdentity();

            // Workspace, view panes and placement before the control attaches,
            // so the first surface is created at its real size.
            ApplyWorkspace(_settings.WorkspacePreset);
            ApplyViewArrangement(_settings.ViewArrangement);
            _shell.ShowDiagnostics = _settings.DiagnosticsReadouts;
        }

        ApplyPlacement(placement);

        // Somebody typing when the viewport comes back keeps the keyboard. A
        // composited viewport can take it while it attaches, so it is handed
        // back afterwards.
        TextBox? typing = restarting ? FocusManager?.GetFocusedElement() as TextBox : null;

        // Attach last: it leads to SurfaceCreated, which needs everything above.
        // Index 0 keeps the drop overlay from the markup on top.
        ViewportHost.Children.Insert(0, viewport.Control);

        if (typing is null)
            viewport.FocusEngine();
        else
            typing.Focus();
    }

    // The only place a tool's content may be assigned. Dock gives the content
    // presenter its own DataContext (the Tool) and a float leaves this window's
    // tree, so the DataContext is set here too. A failed binding reports nothing.
    private static bool _toolTabsMoved;

    // Dock docks a tool dock's tab strip under its content, inline in its
    // template, so no style can move it. Above the content matches the ribbon.
    private static void MoveToolTabsAbove()
    {
        if (_toolTabsMoved)
            return;
        _toolTabsMoved = true;
        LoadedEvent.AddClassHandler<Dock.Avalonia.Controls.ToolTabStrip>(
            (strip, _) => DockPanel.SetDock(strip, Avalonia.Controls.Dock.Top));
    }

    private void SetToolContent(Dock.Model.Avalonia.Controls.Tool tool, Control content)
    {
        content.DataContext = _shell;
        tool.Content = content;
    }

    // Moves the view panes between the grid cell and the dock tool. CanPin
    // follows the placement: a pinned flyout draws in this window's own layer,
    // which a native child composites over.
    private void ApplyPlacement(ViewportPlacement placement)
    {
        ViewportPlacementRules rules = ViewportLayout.RulesFor(placement);

        if (_placement != placement)
        {
            if (rules.Docked)
            {
                EditorView.Children.Remove(ViewPanes);
                _viewportDockHost.Child = ViewPanes;
            }
            else
            {
                _viewportDockHost.Child = null;

                // Back at their declared index: appended, they would paint over
                // the splitters that overhang into their cell.
                if (!EditorView.Children.Contains(ViewPanes))
                    EditorView.Children.Insert(_viewPanesIndex, ViewPanes);
            }

            CenterDock.IsVisible = rules.Docked;
            _placement = placement;
        }

        ViewportTool.CanPin = rules.CanPin;
        ViewportTool.CanFloat = rules.CanFloat;
        ViewportTool.CanClose = ViewportLayout.ViewportCanClose;

        foreach (Dock.Model.Avalonia.Controls.Tool tool in PanelTools)
            tool.CanPin = rules.CanPin;
    }

    private Dock.Model.Avalonia.Controls.Tool[] PanelTools =>
        [MapsTool, SceneTool, PropertiesTool, ContentTool, OutputTool, ProblemsTool, ConsoleTool];

    // A running composited viewport failed. Report it; never hot-swap to a
    // native child, which would tear down the live engine.
    private void OnViewportFailed(ViewportChoiceReason reason)
    {
        _sessionFaulted = true;
        _shell.SetError($"The composited viewport failed: {ViewportModePolicy.Describe(reason)}.");
    }

    // Folds the ending session into the composited history. Native sessions
    // say nothing about the composited path and are not recorded.
    private void RecordSessionOutcome()
    {
        if (!_sessionIsComposited)
            return;

        // A session whose engine died vouches for nothing, a lost device least
        // of all: the engine and the compositor share a texture across devices.
        bool green = ViewportModePolicy.IsSessionGreen(
            _sessionDebugLayerErrors, _sessionFaulted || _sessionEngineDied, _sessionCapabilities.CompareGreen);

        _settings.RecordCompositedSession(green);
        _settings.Save(_logger);

        _logger.LogInformation(
            "Composited session recorded as {Verdict}: {Errors} counted debug-layer error(s), " +
            "{Faults}, {Engine}, colour comparison {Compare}. {Count} of {Required} consecutive green " +
            "session(s) on this adapter and driver.",
            green ? "green" : "not green",
            _sessionDebugLayerErrors,
            _sessionFaulted ? "the hand-over faulted" : "no hand-over fault",
            _sessionEngineDied ? "the engine died" : "the engine ran to the end",
            _sessionCapabilities.CompareGreen ? "green" : "missing or red for this adapter and backend",
            _settings.ViewportPreference.GreenSessions,
            ViewportModePolicy.RequiredGreenSessions);

        _sessionIsComposited = false;
    }

    // Tears the session down and returns to the start page. Callers have
    // already confirmed any unsaved work.
    private void CloseSessionView()
    {
        // A viewport left stopped after a fault has no control, and its
        // session view is still up.
        if (_viewport is null && !_recovery.IsStopped)
            return;

        if (_viewport is { } viewport)
        {
            // While the counters still describe this session.
            RecordSessionOutcome();
            DetachViewport(viewport);
        }

        _recovery.Reset();

        // Pane home before the floats close, so it is not inside a window that
        // is about to be destroyed.
        ApplyPlacement(ViewportPlacement.PinnedCell);

        // Floated panels are OS windows outside EditorView; close them too.
        LeftRoot.ExitWindows?.Execute(null);
        RightRoot.ExitWindows?.Execute(null);
        BottomRoot.ExitWindows?.Execute(null);
        CenterRoot.ExitWindows?.Execute(null);

        // Entity classes belong to the closed project's catalogue.
        _lastEntityClass = null;
        _shell.SetEntityClasses(null);
        RefreshEntityInsertTip();
        if (_shell.Properties is { } panel)
            panel.Schemas = null;

        _shell.HasSession = false;

        // The ribbon flyout is a popup and would not hide with the session.
        // Dismiss keeps the user's pin state.
        _ribbon = RibbonSurface.Dismiss(_ribbon);
        ApplyRibbonState();

        RefreshDocumentIdentity();
        _shell.HasProject = false;
        _shell.ProjectMaps.Clear();

        RestoreWorkspace();

        // An empty snapshot clears the readouts so no verb stays enabled.
        _shell.ClearFilter();
        _shell.ApplySnapshot(FrameSnapshot.Empty);

        _shell.Problems.Clear();

        EditorView.IsVisible = false;
        StartView.IsVisible = true;
        RefreshRecents();
    }

    // Takes the viewport out of the window and stops its session. The session
    // view stays up: closing it, or starting another viewport, is the caller's.
    private void DetachViewport(IEngineViewport viewport)
    {
        // Shutdown first: it tells a composited viewport the coming detach is
        // the end and not a re-dock.
        viewport.Shutdown();

        // Removing a native child raises SurfaceDestroying, which stops the
        // engine. The explicit stop covers a viewport that never got a surface.
        ViewportHost.Children.Remove(viewport.Control);
        StopSession();

        _shell.DropPrompt = ViewportDropPrompt.None;

        // Pending optimistic values would make the next session ignore its
        // first snapshots.
        _shell.ResetOptimisticState();
        TimerResolution.Release();

        viewport.SurfaceCreated -= OnSurfaceCreated;
        viewport.SurfaceDestroying -= OnSurfaceDestroying;
        viewport.ShellChord -= OnShellChord;
        viewport.ContextMenuRequested -= OnViewportContextMenu;
        viewport.AssetDropped -= OnViewportAssetDropped;
        viewport.AssetDragChanged -= OnViewportAssetDragChanged;
        _viewport = null;
        _pendingLaunch = null;

        _tree = null;
        _shell.Tree = null;
    }

    private void OnSurfaceCreated(IRenderSurface surface)
    {
        try
        {
            SessionLaunch? launch = _pendingLaunch;
            _pendingLaunch = null;

            var session = new EditorSession(_loggerFactory, ResolveBackend(), launch?.ContentRoot);

            // Input armed before the engine starts, so early clicks are not dropped.
            _viewport!.Host = session.Host;
            session.Host.FrameCompleted += OnFrameCompleted;

            _tree = new SceneTreeModel(session.Host, _loggerFactory.CreateLogger<SceneTreeModel>());
            _shell.Tree = _tree;

            // The same catalogue instance the render thread stamps scenes with.
            _shell.SetEntityClasses(EntityInsertMenu.Build(session.EntitySchemas));
            RefreshEntityInsertTip();

            // Filled now as well as per open: a menu item with no rows has no
            // submenu to open.
            FillEntityMenu(InsertEntityMenu, static () => null);
            if (_shell.Properties is { } panel)
                panel.Schemas = session.EntitySchemas;

            _surface = surface;
            _shell.SetViewportSize(surface.PixelSize.X, surface.PixelSize.Y);
            surface.Resized += OnViewportResized;

            session.Start(surface);
            _session = session;
            _pump.Start();

            _logic.Schemas = session.EntitySchemas;
            SendLogicRequest();

            // The engine boots on a baseplate; the real map opens through the
            // ordinary path so a broken bundle is reported.
            if (launch?.Restore is { } level)
            {
                RestoreLevel(session, level);
            }
            else if (launch?.OpenMapPath is { } mapPath)
            {
                // Refused, so the baseplate is all this session will show.
                if (!OpenMapAt(mapPath))
                    _recovery.LevelShown();
            }
            else
            {
                _document.MarkNew();

                // Don't overwrite a failure that was just reported.
                if (!ReportRestart() && !_shell.IsError)
                {
                    _shell.SetMessage(_document.HasProject
                        ? $"New baseplate scene. Save it to add a first map to {_document.ProjectLabel}."
                        : "Ready.");
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "The editor session could not start");

            // A restart that cannot start keeps its level and waits to be
            // asked again. Posted for the same reason as the close below.
            if (_recovery.RestartFailed() is { } notice)
            {
                _shell.SetError(SessionFaultText.StartFailed(notice, ex.Message));
                Dispatcher.UIThread.Post(() =>
                {
                    if (_viewport is { } failed)
                        DetachViewport(failed);
                });
                return;
            }

            _shell.SetError($"The engine could not start: {ex.Message}");

            // Back to the start page. Posted: this runs during the native
            // child's attach, and removing it from inside that is not safe.
            Dispatcher.UIThread.Post(CloseSessionView);
        }
    }

    private void OnSurfaceDestroying()
    {
        // Stop before the window goes: the render thread presents into it.
        StopSession();
    }

    // Stops the Close that follows a confirmation from asking again.
    private bool _closeConfirmed;

    /// <inheritdoc/>
    // The dialog is async and closing is not, so a dirty close is cancelled,
    // asked, and re-issued.
    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (_document.IsDirty && !_closeConfirmed)
        {
            e.Cancel = true;
            _ = ConfirmCloseAsync();
        }
        else
        {
            // Most sessions end here, not in CloseSessionView.
            RecordSessionOutcome();

            // Tells a composited viewport this detach is final.
            _viewport?.Shutdown();
            StopSession();
        }

        base.OnClosing(e);
    }

    private async Task ConfirmCloseAsync()
    {
        if (!await ConfirmDiscardAsync("closing the editor")) return;

        _closeConfirmed = true;
        Close();
    }

    private void StopSession()
    {
        if (_stopping || _session is null)
            return;

        _stopping = true;
        _pump.Stop();
        if (_viewport is { } viewport)
            viewport.Host = null;

        if (_surface is { } surface)
        {
            surface.Resized -= OnViewportResized;
            _surface = null;
        }

        _session.Host.FrameCompleted -= OnFrameCompleted;
        _session.Stop();
        _session.Dispose();
        _session = null;

        // Reset per-session state so nothing leaks into the next one.
        _latest = FrameSnapshot.Empty;
        _lastApplied = FrameSnapshot.Empty;
        while (_published.TryDequeue(out _))
            Interlocked.Decrement(ref _queuedSnapshots);
        _droppedSnapshots = false;
        _lastUndoDepth = 0;
        _lastRedoDepth = 0;
        _entityAuditPending = false;
        _entityAuditStale = false;
        _sceneView.ResetSelectionMemory();
        _consoleFeed.Reset();
        _logic.Reset();
        _captions.Reset();
        _shell.SoundPreview.EndSession();
        _deathNoticed = false;
        _dyingSince = null;

        _stopping = false;
    }

    // Runs on the render thread inside the engine's frame. Must be cheap and
    // must not touch a control.
    private void OnFrameCompleted(FrameSnapshot snapshot)
    {
        _latest = snapshot;

        if (Interlocked.Increment(ref _queuedSnapshots) > MaxQueuedSnapshots)
        {
            // Flag the drop so the tree rebuilds from the live graph.
            Interlocked.Decrement(ref _queuedSnapshots);
            _droppedSnapshots = true;
            return;
        }

        _published.Enqueue(snapshot);

        // Post a pump per publish so the UI doesn't wait on its own timer.
        // Coalesced: the pump drains every queued snapshot in one pass.
        if (Interlocked.Exchange(ref _pumpPosted, 1) != 0)
            return;

        try
        {
            Dispatcher.UIThread.Post(() => OnPump(null, EventArgs.Empty), DispatcherPriority.Normal);
        }
        catch (InvalidOperationException)
        {
            // Dispatcher already shut down: the window is closing.
            Interlocked.Exchange(ref _pumpPosted, 0);
        }
    }

    private void OnPump(object? sender, EventArgs e)
    {
        Interlocked.Exchange(ref _pumpPosted, 0);

        _viewport?.PumpCursorMode();

        // Drain every snapshot: each change list exists once.
        while (_published.TryDequeue(out FrameSnapshot? queued))
        {
            Interlocked.Decrement(ref _queuedSnapshots);
            _tree?.ApplyChanges(queued);
            ShowConsoleLines(queued);
        }

        if (_droppedSnapshots)
        {
            _droppedSnapshots = false;
            _logger.LogWarning("The shell fell behind the engine's snapshots; rebuilding the scene tree");
            _tree?.MarkStale();
        }

        if (_session is { Fault: not null } dying && !_deathNoticed)
            FollowDeath(dying);

        FrameSnapshot snapshot = _latest;
        if (ReferenceEquals(snapshot, FrameSnapshot.Empty))
            return;

        // The watchdog ticks ~125 times a second; only a new snapshot gets the
        // full apply.
        if (ReferenceEquals(snapshot, _lastApplied))
            return;
        _lastApplied = snapshot;

        if (snapshot.DebugLayerErrorCount > _sessionDebugLayerErrors)
            _sessionDebugLayerErrors = snapshot.DebugLayerErrorCount;

        // Selection is state, not history: applied once, from the newest.
        _sceneView.SyncSelection(snapshot);
        TrackDirty(snapshot);

        // Close a context menu that was open when play started.
        if (snapshot.IsPlaying && _viewportMenu is { IsOpen: true } menu)
            menu.Close();

        string? pipelineBefore = _shell.PipelineName;
        int pipelineCountBefore = _shell.PipelineNames.Count;

        _shell.ApplySnapshot(snapshot);
        _logic.Apply(snapshot);
        _captions.Apply(snapshot);
        RefreshSnapField(snapshot);

        if (pipelineCountBefore != _shell.PipelineNames.Count
            || !string.Equals(pipelineBefore, _shell.PipelineName, StringComparison.Ordinal))
        {
            RefreshRendererMenu();
        }
    }

    // The snap field follows the property panel's commit contract: Enter and
    // blur commit, Escape reverts, bad text reverts.

    private void RefreshSnapField(FrameSnapshot snapshot)
    {
        // Don't overwrite a field somebody is typing into.
        TextBox box = _buildTab.SnapField;
        if (box.IsFocused)
            return;

        string text = PropertyFieldModel.Format(IncrementFor(snapshot, LiveSnapTool));
        if (box.Text != text)
            box.Text = text;
    }

    private static float IncrementFor(FrameSnapshot snapshot, GizmoMode tool) => tool switch
    {
        GizmoMode.Rotate => snapshot.RotateSnapIncrement,
        GizmoMode.Scale => snapshot.ResizeSnapIncrement,
        _ => snapshot.MoveSnapIncrement,
    };

    private void OnSnapFieldFocused(object? sender, FocusChangedEventArgs e)
    {
        if (sender is TextBox box)
            box.SelectAll();
    }

    private void OnSnapFieldBlurred(object? sender, RoutedEventArgs e)
    {
        if (sender is TextBox box)
            CommitSnapField(box);
    }

    private void OnSnapFieldKeyDown(object? sender, KeyEventArgs e)
    {
        if (sender is not TextBox box)
            return;

        switch (e.Key)
        {
            case Key.Enter:
                CommitSnapField(box);
                e.Handled = true;
                break;

            case Key.Escape:
                RevertSnapField(box);
                _viewport?.FocusEngine();
                e.Handled = true;
                break;
        }
    }

    private void CommitSnapField(TextBox box)
    {
        // Zero or negative would throw on the render thread; refuse it here.
        if (PropertyFieldModel.TryParseNumber(box.Text ?? string.Empty, out float value) && value > 0f)
            _session?.SetSnapIncrement(LiveSnapTool, value);
        else
            RevertSnapField(box);
    }

    // Blurs a focused text box so its LostFocus commit runs before a chord acts.
    private void CommitFocusedEdit()
    {
        // Avalonia 12 has no ClearFocus; the window takes focus instead.
        if (FocusManager?.GetFocusedElement() is TextBox)
            Focus();
    }

    // Tool keys go through the engine keymap, which only fires while the
    // viewport has the keyboard. Commit first: ribbon controls don't take
    // focus, so nothing else blurs the snap field.
    private void ReturnKeyboardToEngine()
    {
        CommitFocusedEdit();
        _viewport?.FocusEngine();
    }

    private void RevertSnapField(TextBox box)
    {
        // Before the first snapshot, fall back to the editor's default increments.
        FrameSnapshot latest = _latest;
        GizmoMode tool = LiveSnapTool;
        float value = ReferenceEquals(latest, FrameSnapshot.Empty)
            ? (tool == GizmoMode.Rotate ? 15f : 1f)
            : IncrementFor(latest, tool);
        box.Text = PropertyFieldModel.Format(value);
    }

    // From the shell model, not the snapshot: the unit label binds to the model
    // and the two must agree.
    private GizmoMode LiveSnapTool => _shell.GizmoMode switch
    {
        "rotate" => GizmoMode.Rotate,
        "resize" => GizmoMode.Scale,
        _ => GizmoMode.Translate,
    };

    // Any movement of the undo history marks the document dirty. Comparing
    // against the depth at save time is not enough: undo one, make a different
    // edit, and the depth is the same over different content.
    private void TrackDirty(FrameSnapshot snapshot)
    {
        if (snapshot.UndoDepth == _lastUndoDepth && snapshot.RedoDepth == _lastRedoDepth)
            return;

        _lastUndoDepth = snapshot.UndoDepth;
        _lastRedoDepth = snapshot.RedoDepth;
        _document.MarkDirty();
        RequestEntityAudit();
    }

    private const string StuckEntityTemplate = "An entity made from geometry still has world geometry in it";

    private bool _entityAuditPending;
    private bool _entityAuditStale;

    // A door turned back into a block refuses to move when the level plays,
    // and nothing logs that until then. So the scene is asked after every
    // edit and every load, and the rows follow the answer.
    private void RequestEntityAudit()
    {
        if (_session is not { } session)
            return;

        // One in flight at a time. An edit that lands meanwhile asks again.
        if (_entityAuditPending)
        {
            _entityAuditStale = true;
            return;
        }

        _entityAuditPending = true;
        session.FindEntityProblems(problems => Dispatcher.UIThread.Post(() =>
        {
            if (!ReferenceEquals(session, _session))
                return;

            _entityAuditPending = false;

            var rows = new List<ProblemRow>(problems.Count);
            foreach (EntityProblem problem in problems)
                rows.Add(new ProblemRow(problem.EntityName, problem.Describe(), problem.BrushId));

            _shell.Problems.Replace(OutputSeverity.Warning, StuckEntityTemplate, ProblemScope.Map, rows);

            if (_entityAuditStale)
            {
                _entityAuditStale = false;
                RequestEntityAudit();
            }
        }));
    }

    private void OnViewportResized(Vector2D<int> size)
    {
        _shell.SetViewportSize(size.X, size.Y);
        _logger.LogDebug("Viewport resized to {Width}x{Height}", size.X, size.Y);
    }

    // Panel menu entries show, never toggle. A closed tool is still in the
    // layout, so making it active brings it back.

    private void OnShowScenePanel(object? sender, RoutedEventArgs e) => ShowTool(SceneTool);

    private void OnShowMapsPanel(object? sender, RoutedEventArgs e)
    {
        // The compact workspace has no Levels dock.
        SetLevelsDocked(true);
        ShowTool(MapsTool);
    }

    private void OnShowPropertiesPanel(object? sender, RoutedEventArgs e) => ShowTool(PropertiesTool);

    private void OnShowContentPanel(object? sender, RoutedEventArgs e) => ShowToolInDrawer(ContentTool);

    private void OnShowOutputPanel(object? sender, RoutedEventArgs e) => ShowToolInDrawer(OutputTool);

    private void OnShowProblemsPanel(object? sender, RoutedEventArgs e) => ShowToolInDrawer(ProblemsTool);

    // A resolution line clears the problems it ends about its subject and is
    // only logged when it cleared one.
    private void OnEngineLogLine(EngineLogLine line)
    {
        if (line.Ends is { } ends)
        {
            if (_shell.Problems.Resolve(line.Subject, ends) > 0)
                _shell.Output.Append(OutputSeverity.Info, line.Message);
            return;
        }

        _shell.Output.Append(line.Severity, line.Message);
        _shell.Problems.Report(line.Severity, line.Template, line.Message, line.Subject);
    }

    private void OnEngineLinesDropped(int count)
    {
        string text = $"{count} engine log line(s) were dropped because the shell fell behind. " +
            "The run log has them all.";
        _shell.Output.Append(OutputSeverity.Warning, text);
        _shell.Problems.Report(OutputSeverity.Warning, "Engine log lines dropped", text);
    }

    private void OnProblemActivated(ProblemEntry entry)
    {
        if (entry.HasNode)
        {
            _session?.Select(entry.NodeId);
            return;
        }

        if (entry.HasSubject && LooksLikeContentPath(entry.Subject))
        {
            RevealInContent(entry.Subject);
            return;
        }

        if (entry.HasSubject)
            _shell.SetMessage(entry.Subject);
    }

    private void RevealInContent(string contentPath)
    {
        ShowToolInDrawer(ContentTool);
        _shell.Content?.Reveal(contentPath);
    }

    private static bool LooksLikeContentPath(string subject) =>
        subject.Contains('/', StringComparison.Ordinal) &&
        Path.HasExtension(subject) &&
        !subject.Contains(' ', StringComparison.Ordinal);

    private void OnViewPerspectiveClicked(object? sender, RoutedEventArgs e) =>
        RunViewPreset(EditorCameraCommand.ViewPerspective, "Perspective");

    private void OnViewTopClicked(object? sender, RoutedEventArgs e) =>
        RunViewPreset(EditorCameraCommand.ViewTop, "Top");

    private void OnViewBottomClicked(object? sender, RoutedEventArgs e) =>
        RunViewPreset(EditorCameraCommand.ViewBottom, "Bottom");

    private void OnViewFrontClicked(object? sender, RoutedEventArgs e) =>
        RunViewPreset(EditorCameraCommand.ViewFront, "Front");

    private void OnViewBackClicked(object? sender, RoutedEventArgs e) =>
        RunViewPreset(EditorCameraCommand.ViewBack, "Back");

    private void OnViewRightClicked(object? sender, RoutedEventArgs e) =>
        RunViewPreset(EditorCameraCommand.ViewRight, "Right");

    private void OnViewLeftClicked(object? sender, RoutedEventArgs e) =>
        RunViewPreset(EditorCameraCommand.ViewLeft, "Left");

    private void RunViewPreset(EditorCameraCommand command, string name)
    {
        // So the shell doesn't report the view change as unrequested.
        _shell.ExpectViewName(name);
        OnShellVerb(ShellVerb.Of(command));
    }

    private void OnHeaderAction(HeaderAction action)
    {
        switch (action)
        {
            case HeaderAction.ToggleNavigation: _session?.Post(EditorHostCommand.ToggleNavigation); break;

            case HeaderAction.GridAuto: _session?.Post(EditorHostCommand.GridAuto); break;
            case HeaderAction.GridOn: _session?.Post(EditorHostCommand.GridOn); break;
            case HeaderAction.GridOff: _session?.Post(EditorHostCommand.GridOff); break;

            case HeaderAction.ViewPerspective: RunViewPreset(EditorCameraCommand.ViewPerspective, "Perspective"); break;
            case HeaderAction.ViewTop: RunViewPreset(EditorCameraCommand.ViewTop, "Top"); break;
            case HeaderAction.ViewBottom: RunViewPreset(EditorCameraCommand.ViewBottom, "Bottom"); break;
            case HeaderAction.ViewFront: RunViewPreset(EditorCameraCommand.ViewFront, "Front"); break;
            case HeaderAction.ViewBack: RunViewPreset(EditorCameraCommand.ViewBack, "Back"); break;
            case HeaderAction.ViewRight: RunViewPreset(EditorCameraCommand.ViewRight, "Right"); break;
            case HeaderAction.ViewLeft: RunViewPreset(EditorCameraCommand.ViewLeft, "Left"); break;

            case HeaderAction.DebugWireframe: RequestDebug(DebugVisualization.Wireframe, !_shell.DebugWireframe); break;
            case HeaderAction.DebugVertices: RequestDebug(DebugVisualization.Vertices, !_shell.DebugVertices); break;
            case HeaderAction.DebugAabbs: RequestDebug(DebugVisualization.Aabbs, !_shell.DebugAabbs); break;
            case HeaderAction.DebugNormals: RequestDebug(DebugVisualization.Normals, !_shell.DebugNormals); break;
            case HeaderAction.DebugSceneGraph: RequestDebug(DebugVisualization.SceneGraph, !_shell.DebugSceneGraph); break;
        }
    }

    private void OnShowConsolePanel(object? sender, RoutedEventArgs e)
    {
        ShowToolInDrawer(ConsoleTool);
        _consoleView?.FocusInput();
    }

    private static void ShowTool(Dock.Model.Avalonia.Controls.Tool tool)
    {
        if (tool.Owner is Dock.Model.Core.IDock owner)
            owner.ActiveDockable = tool;
    }

    private async Task RunInteropProbeAsync()
    {
        await InteropProbe.RunAsync(this, _logger);

        // Time for the log to flush.
        await Task.Delay(TimeSpan.FromMilliseconds(500));
        Close();
    }

    // Every snapshot's lines, in the pump's drain loop: they are sent once.
    // The panel keeps 500 rows and cannot be searched, so the run log gets
    // them too. The log relay shows nothing below Warning, so no row doubles.
    private void ShowConsoleLines(FrameSnapshot snapshot)
    {
        if (snapshot.ConsoleLines.Count == 0)
            return;

        _consoleFeed.Feed(snapshot.ConsoleLines, _shell.Output);

        foreach (ConsoleLine line in snapshot.ConsoleLines)
            _logger.LogInformation("[console] {Line}", line.Text);
    }

    private void OnConsoleCommand(string line)
    {
        _shell.Output.Append(OutputSeverity.Command, "> " + line);

        if (_console is not { } console)
            return;

        ConsoleResult result = console.Execute(line);

        if (result.Reply == ConsoleCommands.ClearMarker)
        {
            _shell.Output.Clear();
            return;
        }

        if (!string.IsNullOrEmpty(result.Reply))
            _shell.Output.Append(result.Severity, result.Reply);
    }

    // Double-click in the content browser: a model or a sound is placed at the
    // view centre, a material paints the selection, anything else is selected.
    private void OnContentActivated(ContentEntry entry)
    {
        if (!AssetDropPolicy.CanPlace(entry.Kind))
        {
            _shell.Content?.Select(entry);
            return;
        }

        if (_session is not { } session)
        {
            _shell.SetWarning("Open a project before inserting or assigning anything.");
            return;
        }

        if (_shell.Content is not { } browser || !browser.TryDescribe(entry, out ContentDragPayload? payload))
        {
            _shell.SetWarning($"{entry.Name} is not inside this project's Assets folder.");
            return;
        }

        if (entry.Kind == ContentKind.Material)
        {
            _shell.Content?.Select(entry);
            session.AssignMaterialToSelection(
                payload.ContentPath,
                report => Dispatcher.UIThread.Post(() => ReportMaterialAssign(report)));
            return;
        }

        // Null point means the view centre. The report arrives on the render thread.
        PlaceAsset(session, payload, null);
    }

    // A model becomes a mesh node, a sound a sound entity that plays it.
    private void PlaceAsset(EditorSession session, ContentDragPayload payload, System.Numerics.Vector2? point)
    {
        if (payload.Kind == ContentKind.Sound)
        {
            session.InsertSound(
                payload.ContentPath,
                point,
                report => Dispatcher.UIThread.Post(() => ReportSoundInsert(report)));
            return;
        }

        session.InsertModel(
            payload.ContentPath,
            point,
            report => Dispatcher.UIThread.Post(() => ReportModelInsert(report)));
    }

    private void ReportSoundInsert(SoundInsertReport report)
    {
        if (report.Placed) _shell.SetMessage(report.Describe());
        else _shell.SetWarning(report.Describe());
    }

    // False while the viewport is stopped: the files are still listed, and
    // there is no engine to play one.
    private bool SendSoundPreview(string path)
    {
        if (_session is not { } session)
            return false;

        session.PreviewSound(path);
        return true;
    }

    // Splitter hover is set from code on the 1px ink child (in Tag): a child
    // can't read its parent's pseudo-classes from a selector.

    private static void OnSplitterEntered(object? sender, PointerEventArgs e) =>
        SetSplitterHot(sender, hot: true);

    private void OnSplitterExited(object? sender, PointerEventArgs e)
    {
        // A fast drag leaves the hit band; stay hot until the drag ends.
        if (!ReferenceEquals(sender, _draggingSplitter))
            SetSplitterHot(sender, hot: false);
    }

    private object? _draggingSplitter;

    private void OnSplitterDragStarted(object? sender, VectorEventArgs e)
    {
        _draggingSplitter = sender;
        SetSplitterHot(sender, hot: true);
    }

    private void OnSplitterDragCompleted(object? sender, VectorEventArgs e)
    {
        _draggingSplitter = null;
        SetSplitterHot(sender, hot: false);
    }

    private static void SetSplitterHot(object? sender, bool hot)
    {
        if (sender is Control { Tag: Control ink })
            ink.Classes.Set("hot", hot);
    }

    private void OnSnapFinerClicked(object? sender, RoutedEventArgs e) =>
        _session?.Post(GizmoCommand.FinerSnap);

    private void OnSnapCoarserClicked(object? sender, RoutedEventArgs e) =>
        _session?.Post(GizmoCommand.CoarserSnap);

    private void OnKeyboardReferenceClicked(object? sender, RoutedEventArgs e) =>
        _ = new KeyboardReferenceWindow().ShowDialog(this);

    // Cooks the open project and verifies the pack with nothing else mounted.
    // Runs off the UI thread and touches no scene, so no EnqueueCommand.
    private async void OnValidateCookedClicked(object? sender, RoutedEventArgs e)
    {
        if (_document.Project is not { } project || _shell.IsValidatingCooked)
            return;

        _shell.IsValidatingCooked = true;
        _shell.SetMessage($"Cooking {project.Project.Name} and validating the pack...");

        try
        {
            CookedValidationReport report = await Task.Run(() => CookedValidation.Run(project));

            _shell.Problems.ClearScope(ProblemScope.Cook);

            foreach (CookDiagnostic diagnostic in report.Diagnostics)
            {
                OutputSeverity severity = diagnostic.Severity switch
                {
                    CookDiagnosticSeverity.Error => OutputSeverity.Error,
                    CookDiagnosticSeverity.Warning => OutputSeverity.Warning,
                    _ => OutputSeverity.Info,
                };

                string line = diagnostic.ToBuildLine("scook");
                _shell.Output.Append(severity, line);

                _shell.Problems.Report(
                    severity, $"Cook {diagnostic.Id}", line, diagnostic.File ?? string.Empty, ProblemScope.Cook);
            }

            if (report.Succeeded) _shell.SetMessage(report.Summary);
            else _shell.SetError(report.Summary);
        }
        catch (Exception ex)
        {
            // An async void handler that throws takes the application down.
            _shell.SetError($"The cooked-content validation did not finish: {ex.Message}");
        }
        finally
        {
            _shell.IsValidatingCooked = false;
        }
    }

    // A file drag opens a project or level. An asset drag reaching the window
    // means no viewport claimed it.
    private void OnDragOver(object? sender, DragEventArgs e)
    {
        if (e.DataTransfer.Contains(ContentDrag.Format))
        {
            // Accept over the viewport rectangle so the drop can land and be
            // refused with a message.
            e.DragEffects = IsOverViewport(e) ? DragDropEffects.Copy : DragDropEffects.None;
            return;
        }

        e.DragEffects = e.DataTransfer.Contains(DataFormat.File)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        if (e.DataTransfer.TryGetValue(ContentDrag.Format) is { } payload)
        {
            // A composited viewport handles its own drops, so this is a refusal.
            _shell.SetWarning(
                AssetDropPolicy.Refuse(payload, _session is not null, _viewport?.AcceptsAssetDrops ?? false)
                ?? $"{payload.Name} was not dropped into the scene.");
            return;
        }

        if (e.DataTransfer.TryGetFiles() is { } files)
            _ = DropAsync(files);
    }

    // Bounds, not hit testing: Avalonia knows nothing about the HWND inside a
    // NativeControlHost.
    private bool IsOverViewport(DragEventArgs e)
    {
        if (_viewport?.Control is not { } control || control.Bounds.Width <= 0 || control.Bounds.Height <= 0)
            return false;

        Point point = e.GetPosition(control);
        return point.X >= 0 && point.Y >= 0 &&
            point.X < control.Bounds.Width && point.Y < control.Bounds.Height;
    }

    // Passes the file and the pixel on. Placement is decided by SceneEditorHost
    // on the render thread; this thread's view of the scene is frames behind.
    private void OnViewportAssetDropped(
        ContentDragPayload payload, int x, int y, MaterialDropScope scope)
    {
        if (AssetDropPolicy.Refuse(payload, _session is not null, viewportAcceptsDrops: true) is { } refusal)
        {
            _shell.SetWarning(refusal);
            return;
        }

        if (_session is not { } session)
            return;

        var point = new System.Numerics.Vector2(x, y);

        if (payload.Kind == ContentKind.Material)
        {
            session.AssignMaterial(
                payload.ContentPath, point, scope,
                report => Dispatcher.UIThread.Post(() => ReportMaterialAssign(report)));
            return;
        }

        PlaceAsset(session, payload, point);
    }

    // Refused: nothing happened (warning). Missing file: the faces were painted
    // but the material doesn't exist (error). Otherwise a plain message.
    private void ReportMaterialAssign(MaterialAssignReport report)
    {
        string line = report.Describe();

        if (!report.Applied)
        {
            _shell.SetWarning(line);
            return;
        }

        if (report.Unresolved is not null)
        {
            _shell.SetError(line);
            _shell.Problems.Report(
                OutputSeverity.Error, "Material {Path} is missing", line, report.ContentPath,
                ProblemScope.Map, report.NodeId);
            return;
        }

        _shell.SetMessage(line);
    }

    // Shows or clears the drop overlay. The prompt asks the same policy the
    // drop does, so the two can't disagree.
    private void OnViewportAssetDragChanged(AssetDragState? state)
    {
        _shell.DropPrompt = ViewportDropPrompt.For(
            state?.Payload, _session is not null, _viewport?.AcceptsAssetDrops ?? false, state?.Scope ?? MaterialDropScope.Face);

        // Only a material drag highlights the face under the pointer.
        _session?.SetMaterialDrag(
            state is { Payload.Kind: ContentKind.Material } ? state.Scope : null);
    }

    // One standing problem per missing mesh or dead connection.
    private void RecordMapProblems(MapLoadReport? report)
    {
        if (report is null) return;

        foreach (string node in report.UnresolvedMeshes)
        {
            _shell.Problems.Report(
                OutputSeverity.Warning,
                "Map: a mesh node loaded without its model",
                $"{node} loaded without its model and draws nothing.",
                node,
                ProblemScope.Map);
        }

        foreach (string wire in report.UnresolvedTargets)
        {
            _shell.Problems.Report(
                OutputSeverity.Warning,
                "Map: a connection names a target this level does not have",
                $"{wire} names a target this level does not have.",
                wire,
                ProblemScope.Map);
        }
    }

    private void ReportModelInsert(ModelInsertReport report)
    {
        if (report.Refused is not null)
            _shell.SetWarning(report.Describe());
        else if (report.Unresolved is not null)
        {
            // An empty node is in the scene and the history, so this is an error.
            _shell.SetError(report.Describe());
            _shell.Problems.Report(
                OutputSeverity.Error,
                "A model was placed as an empty node",
                report.Describe(),
                report.ContentPath,
                ProblemScope.Map,
                report.NodeId);
        }
        else
            _shell.SetMessage(report.Describe());
    }

    // Builds the tab strip from RibbonLayout.Tabs so it can't drift from the roster.
    private void BuildRibbon()
    {
        _ribbonPages[RibbonLayout.DefaultTabId] = _buildTab;
        _ribbonPages[RibbonLayout.ViewTabId] = _viewTab;

        foreach (RibbonTab tab in RibbonLayout.Tabs)
        {
            if (!_ribbonPages.TryGetValue(tab.Id, out RibbonTabView? page))
            {
                throw new InvalidOperationException(
                    $"The ribbon roster names a '{tab.Id}' page this window does not build.");
            }

            // Set explicitly: in the flyout the page sits under a separate
            // visual root and inherits nothing.
            page.DataContext = _shell;
            page.Invoked += OnShellVerb;

            var button = new Button
            {
                Classes = { "ribbontab" },
                Tag = tab.Id,
                Content = new TextBlock { Text = tab.Title },
            };

            ToolTip.SetTip(button, tab.Summary);
            button.Click += OnRibbonTabClicked;
            RibbonTabs.Children.Add(button);
        }

        _buildTab.SnapField.GotFocus += OnSnapFieldFocused;
        _buildTab.SnapField.LostFocus += OnSnapFieldBlurred;
        _buildTab.SnapField.KeyDown += OnSnapFieldKeyDown;

        WireEntitySplit();

        RibbonFlyout.Closed += OnRibbonFlyoutClosed;

        _ribbon = RibbonSurface.Create(_settings.RibbonExpanded);
        ApplyRibbonState();
    }

    private void TogglePalette()
    {
        if (CommandPalettePopup.IsOpen)
        {
            ClosePalette();
            return;
        }

        Palette.QueryBox.Text = string.Empty;
        RefreshPalette();
        CommandPalettePopup.IsOpen = true;

        // Posted: the box can't take focus until the popup is open.
        Dispatcher.UIThread.Post(() => Palette.QueryBox.Focus(), DispatcherPriority.Input);
    }

    private void ClosePalette()
    {
        CommandPalettePopup.IsOpen = false;
        ReturnKeyboardToEngine();
    }

    private void RefreshPalette()
    {
        CommandSearchResult result =
            CommandTable.Search(Palette.QueryBox.Text ?? string.Empty, PaletteContext());

        Palette.RowList.ItemsSource = result.Rows;
        Palette.RowList.SelectedIndex = result.Rows.Count > 0 ? 0 : -1;

        Palette.FooterLabel.Text = result.FooterLabel;
        Palette.FooterLabel.IsVisible = result.FooterLabel.Length > 0;
    }

    private CommandContext PaletteContext() => new(
        _shell.HasSelection,
        _shell.IsPlaying,
        _shell.HasSession,
        _document.HasProject,
        _ribbon.Expanded,
        _shell.CanPlay);

    private void OnPaletteQueryChanged(object? sender, TextChangedEventArgs e) => RefreshPalette();

    // Handled on the query box: the list never takes focus.
    private void OnPaletteKeyDown(object? sender, KeyEventArgs e)
    {
        int count = Palette.RowList.ItemCount;

        switch (e.Key)
        {
            case Key.Escape:
                ClosePalette();
                e.Handled = true;
                break;

            case Key.Down when count > 0:
                Palette.RowList.SelectedIndex = (Palette.RowList.SelectedIndex + 1) % count;
                e.Handled = true;
                break;

            case Key.Up when count > 0:
                Palette.RowList.SelectedIndex = (Palette.RowList.SelectedIndex - 1 + count) % count;
                e.Handled = true;
                break;

            case Key.Enter:
                RunSelectedCommand();
                e.Handled = true;
                break;

            default:
                break;
        }
    }

    private void OnPaletteRowClicked(object? sender, TappedEventArgs e) => RunSelectedCommand();

    private void RunSelectedCommand()
    {
        if (Palette.RowList.SelectedItem is not ShellCommand command)
            return;

        // Close first so the keyboard is back on the viewport when the verb lands.
        ClosePalette();
        OnShellVerb(command.Verb);
    }

    // Undo and redo sit on the tab strip, outside both pages, and are routed
    // through the roster like page controls.
    private void OnRibbonStripClick(object? sender, RoutedEventArgs e)
    {
        if (RibbonTabView.ItemOf(sender) is { } item)
            OnShellVerb(item.Verb);
    }

    private void OnRibbonTabClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { Tag: string tabId })
            return;

        _ribbon = RibbonSurface.SelectTab(_ribbon, tabId);
        ApplyRibbonState();

        // An open flyout is a popup and needs the keyboard for light dismiss.
        if (RibbonSurface.HostFor(_ribbon) != RibbonBodyHost.Flyout)
            ReturnKeyboardToEngine();
    }

    private void OnRibbonPinClicked(object? sender, RoutedEventArgs e) =>
        SetRibbonExpanded(!_ribbon.Expanded);

    private void SetRibbonExpanded(bool expanded)
    {
        _ribbon = RibbonSurface.SetExpanded(_ribbon, expanded);
        ApplyRibbonState();

        // Only the pin state is persisted, not the active tab.
        _settings.SetRibbonExpanded(_ribbon.Expanded);
        _settings.Save(_logger);

        ReturnKeyboardToEngine();
    }

    // Calls the menu's own handlers so every confirmation still runs.
    private void RunDocumentVerb(DocumentVerb verb, RoutedEventArgs args)
    {
        CommitFocusedEdit();

        switch (verb)
        {
            case DocumentVerb.NewProject: OnNewProjectClicked(this, args); break;
            case DocumentVerb.OpenProject: OnOpenProjectClicked(this, args); break;
            case DocumentVerb.CloseProject: OnCloseProjectClicked(this, args); break;
            case DocumentVerb.NewLevel: OnNewMapClicked(this, args); break;
            case DocumentVerb.OpenLevel: OnOpenMapClicked(this, args); break;
            case DocumentVerb.Save: OnSaveClicked(this, args); break;
            case DocumentVerb.SaveAs: OnSaveAsClicked(this, args); break;
            case DocumentVerb.ValidateCooked: OnValidateCookedClicked(this, args); break;
            case DocumentVerb.Exit: Close(); break;
        }
    }

    private void ShowPanel(PanelId panel, RoutedEventArgs args)
    {
        switch (panel)
        {
            case PanelId.Scene: ShowTool(SceneTool); break;
            case PanelId.Levels: OnShowMapsPanel(this, args); break;
            case PanelId.Properties: ShowTool(PropertiesTool); break;
            case PanelId.Content: ShowToolInDrawer(ContentTool); break;
            case PanelId.Output: ShowToolInDrawer(OutputTool); break;
            case PanelId.Problems: ShowToolInDrawer(ProblemsTool); break;
            case PanelId.Console: OnShowConsolePanel(this, args); break;
            case PanelId.KeyboardReference: OnKeyboardReferenceClicked(this, args); break;
        }
    }

    private void OnRibbonFlyoutClosed(object? sender, EventArgs e)
    {
        if (_applyingRibbonState)
            return;

        _ribbon = RibbonSurface.Dismiss(_ribbon);
        ApplyRibbonState();
    }

    private void ApplyRibbonState()
    {
        _applyingRibbonState = true;
        try
        {
            RibbonBodyHost host = RibbonSurface.HostFor(_ribbon);

            // A tab is drawn joined to its page, so with no page showing no tab is lit.
            foreach (Control child in RibbonTabs.Children)
            {
                child.Classes.Set(
                    "active",
                    host != RibbonBodyHost.None
                    && child.Tag is string id
                    && string.Equals(id, _ribbon.ActiveTabId, StringComparison.Ordinal));
            }

            _ribbonPages.TryGetValue(_ribbon.ActiveTabId, out RibbonTabView? page);

            // A control has one parent: clear the old host before assigning the new.
            if (host != RibbonBodyHost.Inline)
                RibbonInlineHost.Content = null;
            if (host != RibbonBodyHost.Flyout)
                RibbonFlyoutHost.Content = null;

            switch (host)
            {
                case RibbonBodyHost.Inline:
                    RibbonInlineHost.Content = page;
                    break;
                case RibbonBodyHost.Flyout:
                    RibbonFlyoutHost.Content = page;
                    break;
            }

            RibbonBody.IsVisible = host == RibbonBodyHost.Inline;
            RibbonFlyout.IsOpen = host == RibbonBodyHost.Flyout;

            RibbonPin.Classes.Set("collapsed", !_ribbon.Expanded);
            ToolTip.SetTip(
                RibbonPin,
                _ribbon.Expanded ? "Collapse the ribbon to its tabs" : "Pin the ribbon open");
        }
        finally
        {
            _applyingRibbonState = false;
        }
    }

    // Dispatcher for the ribbon and the palette. Undo, redo, the tool verbs and
    // the two-way choices go through the optimistic handlers the menus use.
    private void OnShellVerb(ShellVerb verb)
    {
        // A verb from a flown-out page closes the page.
        if (RibbonSurface.HostFor(_ribbon) == RibbonBodyHost.Flyout)
        {
            _ribbon = RibbonSurface.Invoke(_ribbon);
            ApplyRibbonState();
        }

        var args = new RoutedEventArgs();
        switch (verb.Kind)
        {
            case ShellVerbKind.Insert:
                _session?.Insert(verb.Insert);
                break;

            case ShellVerbKind.Camera:
                _session?.Post(verb.Camera);
                break;

            case ShellVerbKind.Debug:
                RequestDebug(verb.Debug, !_shell.IsDebugEnabled(verb.Debug));
                break;

            case ShellVerbKind.Host when verb.Host == EditorHostCommand.Undo:
                OnUndoClicked(this, args);
                break;

            case ShellVerbKind.Host when verb.Host == EditorHostCommand.Redo:
                OnRedoClicked(this, args);
                break;

            case ShellVerbKind.Host:
                _session?.Post(verb.Host);
                break;

            case ShellVerbKind.Gizmo when verb.Gizmo == GizmoCommand.UseTranslate:
                UseTool("move", GizmoCommand.UseTranslate);
                break;

            case ShellVerbKind.Gizmo when verb.Gizmo == GizmoCommand.UseRotate:
                UseTool("rotate", GizmoCommand.UseRotate);
                break;

            case ShellVerbKind.Gizmo when verb.Gizmo == GizmoCommand.UseScale:
                UseTool("resize", GizmoCommand.UseScale);
                break;

            case ShellVerbKind.Gizmo:
                _session?.Post(verb.Gizmo);
                break;

            case ShellVerbKind.Toggle:
                ApplyTwoWayChoice(verb.Toggle);
                break;

            case ShellVerbKind.SnapIncrement:
                // The field commits through its own focus and Enter handlers.
                break;

            case ShellVerbKind.InsertEntity:
                // The class comes from the project, so it is resolved at click time.
                if (ResolveEntityClass() is { } className)
                    _session?.InsertEntity(className);
                break;

            case ShellVerbKind.MakeEntity:
                ShowMakeEntityList();
                break;

            case ShellVerbKind.RemoveEntity:
                RemoveEntity();
                break;

            case ShellVerbKind.Document:
                RunDocumentVerb(verb.Document, args);
                break;

            case ShellVerbKind.Play:
                RequestPlay(verb.Play == PlayVerb.Play);
                break;

            case ShellVerbKind.Panel:
                ShowPanel(verb.Panel, args);
                break;

            case ShellVerbKind.Ribbon:
                SetRibbonExpanded(verb.Ribbon == RibbonVerb.Expand);
                break;

            case ShellVerbKind.Workspace:
                RunWorkspaceVerb(verb.Workspace);
                break;
        }

        // The snap field and the panels own their focus; don't take it back.
        // Nor from the Make entity list, which hands it back when it closes.
        if (verb.Kind is not (ShellVerbKind.SnapIncrement or ShellVerbKind.Panel or ShellVerbKind.MakeEntity))
            ReturnKeyboardToEngine();
    }

    // The class the split button's main half places. Null means the first
    // class in the catalogue.
    private string? _lastEntityClass;

    private MenuFlyout? _entityFlyout;
    private MenuFlyout? _makeEntityFlyout;

    private void WireEntitySplit()
    {
        _entityFlyout = new MenuFlyout { Placement = PlacementMode.BottomEdgeAlignedLeft };

        // Shown from the click, not via Button.Flyout: the list is filled first.
        _buildTab.EntityCaretButton.Click += OnEntityCaretClicked;
        _entityFlyout.Closed += (_, _) => ReturnKeyboardToEngine();

        _makeEntityFlyout = new MenuFlyout();
        _buildTab.MakeEntityButton.Click += (_, _) =>
            ShowMakeEntityList(_buildTab.MakeEntityButton, PlacementMode.BottomEdgeAlignedLeft);
        _makeEntityFlyout.Closed += (_, _) => ReturnKeyboardToEngine();

        Palette.QueryBox.TextChanged += OnPaletteQueryChanged;
        Palette.QueryBox.KeyDown += OnPaletteKeyDown;
        Palette.RowList.Tapped += OnPaletteRowClicked;

        CommandPalettePopup.Closed += (_, _) => ReturnKeyboardToEngine();

        RefreshEntityInsertTip();
    }

    // Refilled at every open: the window outlives its sessions and their classes.
    private void OnEntityCaretClicked(object? sender, RoutedEventArgs e)
    {
        if (_entityFlyout is not { } flyout)
            return;

        FillEntityItems(flyout.Items, static () => null);
        if (flyout.Items.Count > 0)
            flyout.ShowAt(_buildTab.EntityCaretButton);
    }

    // The label stays "Entity"; the class goes in the tooltip.
    private void RefreshEntityInsertTip()
    {
        string? className = ResolveEntityClass();

        ToolTip.SetTip(
            _buildTab.EntityInsertButton,
            className is null
                ? "This project declares no entity classes."
                : $"Place a {className}. The arrow chooses a different class.");
    }

    // The last class used, if the live catalogue still has it, else the first.
    private string? ResolveEntityClass()
    {
        if (_lastEntityClass is { } remembered)
        {
            foreach (EntityInsertItem entry in _shell.EntityClasses)
            {
                if (string.Equals(entry.ClassName, remembered, StringComparison.Ordinal))
                    return remembered;
            }
        }

        return _shell.EntityClasses.Count > 0 ? _shell.EntityClasses[0].ClassName : null;
    }


    private void OnInsertWorldBrushClicked(object? sender, RoutedEventArgs e) => _session?.Insert(InsertKind.WorldBrush);
    private void OnInsertPartBrushClicked(object? sender, RoutedEventArgs e) => _session?.Insert(InsertKind.PartBrush);
    private void OnInsertSubtractiveBrushClicked(object? sender, RoutedEventArgs e) => _session?.Insert(InsertKind.SubtractiveBrush);
    private void OnInsertLightClicked(object? sender, RoutedEventArgs e) => _session?.Insert(InsertKind.PointLight);
    private void OnInsertSurfaceLightClicked(object? sender, RoutedEventArgs e) => _session?.Insert(InsertKind.SurfaceLight);
    private void OnInsertGroupClicked(object? sender, RoutedEventArgs e) => _session?.Insert(InsertKind.Group);

    // The shell posts set verbs, never toggles: a toggle against a stale
    // snapshot flips the wrong way. The model shows the request at once and
    // ShellModel bounds how long it waits for the engine's echo.

    private void OnPlayClicked(object? sender, RoutedEventArgs e) => RequestPlay(!_shell.IsPlaying);

    private void RequestPlay(bool wanted)
    {
        if (_session is not { } session)
            return;

        _shell.RequestPlaying(wanted);
        session.Host.RequestPlayMode(wanted);
    }

    private void OnDebugWireClicked(object? sender, RoutedEventArgs e) =>
        RequestDebug(DebugVisualization.Wireframe, !_shell.DebugWireframe);
    private void OnDebugVerticesClicked(object? sender, RoutedEventArgs e) =>
        RequestDebug(DebugVisualization.Vertices, !_shell.DebugVertices);
    private void OnDebugAabbsClicked(object? sender, RoutedEventArgs e) =>
        RequestDebug(DebugVisualization.Aabbs, !_shell.DebugAabbs);
    private void OnDebugNormalsClicked(object? sender, RoutedEventArgs e) =>
        RequestDebug(DebugVisualization.Normals, !_shell.DebugNormals);
    private void OnDebugSceneGraphClicked(object? sender, RoutedEventArgs e) =>
        RequestDebug(DebugVisualization.SceneGraph, !_shell.DebugSceneGraph);

    private void RequestDebug(DebugVisualization flag, bool enabled)
    {
        if (_session is not { } session)
            return;

        _shell.RequestDebugVisualization(flag, enabled);
        session.Host.RequestDebugVisualization(flag, enabled);
    }

    private void OnMoveClicked(object? sender, RoutedEventArgs e) => UseTool("move", GizmoCommand.UseTranslate);
    private void OnRotateClicked(object? sender, RoutedEventArgs e) => UseTool("rotate", GizmoCommand.UseRotate);
    private void OnResizeClicked(object? sender, RoutedEventArgs e) => UseTool("resize", GizmoCommand.UseScale);

    private void UseTool(string mode, GizmoCommand command)
    {
        if (_session is not { } session)
            return;

        _shell.RequestGizmoMode(mode);
        session.Post(command);
    }

    private void OnOrientationClicked(object? sender, RoutedEventArgs e) =>
        ApplyTwoWayChoice(ShellToggle.Axes);

    private void OnStyleClicked(object? sender, RoutedEventArgs e) =>
        ApplyTwoWayChoice(ShellToggle.Handles);

    private void OnSnapClicked(object? sender, RoutedEventArgs e) =>
        ApplyTwoWayChoice(ShellToggle.Snap);

    // Shows the other half at once and posts the set verb that lands on it.
    private void ApplyTwoWayChoice(ShellToggle toggle)
    {
        if (_session is not { } session)
            return;

        bool on = !ShellToggles.IsOn(toggle, _shell);
        ShellToggles.Request(_shell, toggle, on);
        session.Post(ShellToggles.CommandFor(toggle, on));
    }

    private void OnUndoClicked(object? sender, RoutedEventArgs e)
    {
        if (_session is not { } session)
            return;

        _shell.RequestUndo();
        session.Post(EditorHostCommand.Undo);
    }

    private void OnRedoClicked(object? sender, RoutedEventArgs e)
    {
        if (_session is not { } session)
            return;

        _shell.RequestRedo();
        session.Post(EditorHostCommand.Redo);
    }
    private void OnDuplicateClicked(object? sender, RoutedEventArgs e) => _session?.Post(EditorHostCommand.Duplicate);
    private void OnDeleteClicked(object? sender, RoutedEventArgs e) => _session?.Post(EditorHostCommand.Delete);
    private void OnGroupClicked(object? sender, RoutedEventArgs e) => _session?.Post(EditorHostCommand.Group);
    private void OnUngroupClicked(object? sender, RoutedEventArgs e) => _session?.Post(EditorHostCommand.Ungroup);
    private void OnToggleBrushKindClicked(object? sender, RoutedEventArgs e) => _session?.Post(EditorHostCommand.ToggleBrushKind);
    private void OnToggleNavigationClicked(object? sender, RoutedEventArgs e) => _session?.Post(EditorHostCommand.ToggleNavigation);
    private void OnGridAutoClicked(object? sender, RoutedEventArgs e) => _session?.Post(EditorHostCommand.GridAuto);
    private void OnGridOnClicked(object? sender, RoutedEventArgs e) => _session?.Post(EditorHostCommand.GridOn);
    private void OnGridOffClicked(object? sender, RoutedEventArgs e) => _session?.Post(EditorHostCommand.GridOff);

    private void OnFrameClicked(object? sender, RoutedEventArgs e) =>
        _session?.Post(EditorCameraCommand.FrameSelection);

    private void OnFrameAllClicked(object? sender, RoutedEventArgs e) =>
        _session?.Post(EditorCameraCommand.FrameAll);

    private void OnSelectAllClicked(object? sender, RoutedEventArgs e) =>
        _session?.Post(EditorHostCommand.SelectAll);

    private void OnClearSelectionClicked(object? sender, RoutedEventArgs e) =>
        _session?.Post(EditorHostCommand.ClearSelection);

    private void OnExitClicked(object? sender, RoutedEventArgs e) => Close();

    private void OnShellChord(ShellChord chord)
    {
        switch (chord)
        {
            case ShellChord.NewMap: OnNewMapClicked(this, new RoutedEventArgs()); break;
            case ShellChord.OpenMap: OnOpenMapClicked(this, new RoutedEventArgs()); break;
            case ShellChord.SaveMap: OnSaveClicked(this, new RoutedEventArgs()); break;
            case ShellChord.SaveMapAs: OnSaveAsClicked(this, new RoutedEventArgs()); break;
            case ShellChord.InsertBlock: _session?.Insert(InsertKind.WorldBrush); break;
            case ShellChord.InsertPart: _session?.Insert(InsertKind.PartBrush); break;
            case ShellChord.InsertCut: _session?.Insert(InsertKind.SubtractiveBrush); break;
            case ShellChord.InsertLight: _session?.Insert(InsertKind.PointLight); break;
            case ShellChord.OpenPalette: TogglePalette(); break;

            // Focusing the input takes the keyboard from the viewport, which
            // lets go of the cursor. A level that is playing keeps playing.
            case ShellChord.ShowConsole: OnShowConsolePanel(this, new RoutedEventArgs()); break;

            // Toggling is safe here: this state is the window's own and never stale.
            case ShellChord.MaximiseViewport:
                RunWorkspaceVerb(_shell.IsViewportMaximised
                    ? WorkspaceCommand.RestoreWorkspace
                    : WorkspaceCommand.MaximiseViewport);
                break;

            case ShellChord.ToggleBottomDrawer:
                RunWorkspaceVerb(_shell.IsDrawerOpen
                    ? WorkspaceCommand.CloseBottomDrawer
                    : WorkspaceCommand.OpenBottomDrawer);
                break;

            case ShellChord.ToggleLogicView:
                RunWorkspaceVerb(LogicViewFlipVerb());
                break;
        }
    }

    private ContextMenu? _viewportMenu;
    private MenuItem? _viewportEntityMenu;
    private MenuItem? _viewportMakeEntityMenu;

    // Where the menu was opened, in framebuffer pixels, for "insert here".
    private System.Numerics.Vector2 _viewportMenuPoint;

    // A right-click that never became a freelook drag. The menu is an OS
    // popup, which may cross a native viewport.
    private void OnViewportContextMenu(int x, int y)
    {
        if (_session is null || _viewport is not { } viewport || _shell.IsPlaying)
            return;

        // Retarget the selection to what is under the cursor first.
        _viewportMenuPoint = new System.Numerics.Vector2(x, y);
        _session.SelectAtPoint(_viewportMenuPoint);

        _viewportMenu ??= BuildViewportMenu();
        RefreshViewportEntityItems();

        // Client pixels are physical; Avalonia placement wants logical units.
        double scaling = (VisualRoot as TopLevel)?.RenderScaling ?? 1.0;
        _viewportMenu.Placement = PlacementMode.AnchorAndGravity;
        _viewportMenu.PlacementAnchor = Avalonia.Controls.Primitives.PopupPositioning.PopupAnchor.TopLeft;
        _viewportMenu.PlacementGravity = Avalonia.Controls.Primitives.PopupPositioning.PopupGravity.BottomRight;
        _viewportMenu.PlacementRect = new Rect(x / scaling, y / scaling, 1, 1);
        _viewportMenu.Open(viewport.Control);
    }

    private ContextMenu BuildViewportMenu()
    {
        MenuItem Item(string header, string? gesture, Action action)
        {
            var item = new MenuItem { Header = header };
            if (gesture is not null)
                item.InputGesture = KeyGesture.Parse(gesture);
            item.Click += (_, _) => action();
            return item;
        }

        var insert = new MenuItem { Header = "Insert here" };
        insert.Items.Add(Item("Block", "Ctrl+D1", () => _session?.Insert(InsertKind.WorldBrush, _viewportMenuPoint)));
        insert.Items.Add(Item("Part", "Ctrl+D2", () => _session?.Insert(InsertKind.PartBrush, _viewportMenuPoint)));
        insert.Items.Add(Item("Cut", "Ctrl+D3", () => _session?.Insert(InsertKind.SubtractiveBrush, _viewportMenuPoint)));
        insert.Items.Add(Item("Light", "Ctrl+D4", () => _session?.Insert(InsertKind.PointLight, _viewportMenuPoint)));

        insert.Items.Add(Item("Surface light", null, () => _session?.Insert(InsertKind.SurfaceLight, _viewportMenuPoint)));
        insert.Items.Add(Item("Empty group", null, () => _session?.Insert(InsertKind.Group, _viewportMenuPoint)));

        _viewportEntityMenu = new MenuItem { Header = "Entity" };
        insert.Items.Add(_viewportEntityMenu);

        var menu = new ContextMenu();
        menu.Items.Add(insert);
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Duplicate", "Ctrl+D", () => _session?.Post(EditorHostCommand.Duplicate)));
        menu.Items.Add(Item("Delete", "Delete", () => _session?.Post(EditorHostCommand.Delete)));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Group", "Ctrl+G", () => _session?.Post(EditorHostCommand.Group)));
        menu.Items.Add(Item("Ungroup", "Ctrl+Shift+G", () => _session?.Post(EditorHostCommand.Ungroup)));
        menu.Items.Add(Item("Convert block / part", "Ctrl+T", () => _session?.Post(EditorHostCommand.ToggleBrushKind)));

        _viewportMakeEntityMenu = new MenuItem { Header = "Make entity" };
        menu.Items.Add(_viewportMakeEntityMenu);
        menu.Items.Add(Item("Remove entity", null, RemoveEntity));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Frame selection", "F", () => _session?.Post(EditorCameraCommand.FrameSelection)));

        // The popup took focus; hand the keyboard back or the tool keys go dead.
        menu.Closed += (_, _) => _viewport?.FocusEngine();
        return menu;
    }

    private void RefreshViewportEntityItems()
    {
        FillEntityMenu(_viewportEntityMenu, () => _viewportMenuPoint);

        if (_viewportMakeEntityMenu is { } make)
        {
            make.IsVisible = _shell.HasMakeEntityClasses;
            EntityInsertMenu.Fill(make.Items, _shell.MakeEntityClasses, forMake: true, MakeEntity);
        }
    }

    // Refilled per open: the window outlives its sessions and their classes.
    private void OnInsertEntityMenuOpened(object? sender, RoutedEventArgs e) =>
        FillEntityMenu(InsertEntityMenu, static () => null);

    // Rebuilds an entity submenu from the live classes; hidden when there are
    // none. point is evaluated at click time, not captured while building.
    private void FillEntityMenu(MenuItem? submenu, Func<System.Numerics.Vector2?> point)
    {
        if (submenu is null)
            return;

        submenu.IsVisible = _shell.EntityClasses.Count > 0;
        FillEntityItems(submenu.Items, point);
    }

    // Shared by both entity submenus and the ribbon's split button.
    private void FillEntityItems(ItemCollection items, Func<System.Numerics.Vector2?> point)
    {
        EntityInsertMenu.Fill(items, _shell.EntityClasses, forMake: false, className =>
        {
            _lastEntityClass = className;
            RefreshEntityInsertTip();
            _session?.InsertEntity(className, point());
        });
    }

    // The palette has no control to hang the list on, so it opens over the
    // viewport.
    private void ShowMakeEntityList()
    {
        if (_viewport is { } viewport)
            ShowMakeEntityList(viewport.Control, PlacementMode.Center);
    }

    // Refilled at every open, like the insert list.
    private void ShowMakeEntityList(Control anchor, PlacementMode placement)
    {
        if (_makeEntityFlyout is not { } flyout)
            return;

        if (!_shell.HasMakeEntityClasses)
        {
            _shell.SetWarning("This project declares no entity class that is made from geometry.");
            return;
        }

        EntityInsertMenu.Fill(flyout.Items, _shell.MakeEntityClasses, forMake: true, MakeEntity);
        flyout.Placement = placement;
        flyout.ShowAt(anchor);
    }

    private void MakeEntity(string className) =>
        _session?.MakeEntity(className, report => Dispatcher.UIThread.Post(() => ReportEntityEdit(report)));

    private void RemoveEntity() =>
        _session?.RemoveEntity(report => Dispatcher.UIThread.Post(() => ReportEntityEdit(report)));

    // A refusal changed nothing and says how to get there, so it is a warning.
    private void ReportEntityEdit(EntityEditReport report)
    {
        if (report.Applied)
            _shell.SetMessage(report.Message);
        else
            _shell.SetWarning(report.Message);
    }


    // The edit names a property and a value; the editor decides which nodes
    // when it runs, because this thread's selection is a frame or two behind.
    private void OnPropertyEdit(PropertyEdit edit) => _session?.ApplyProperty(edit);

    // By node id, not the selection: a wiring edit replaces a whole list.
    private void OnEntityConnectionsEdit(
        Guid nodeId, IReadOnlyList<SpectraEngine.Core.Entities.EntityConnection> connections) =>
        _session?.ApplyEntityConnections(nodeId, connections);

    // File handlers: filesystem work on the UI thread, scene work on the
    // render thread through EditorSession.

    private async void OnNewMapClicked(object? sender, RoutedEventArgs e)
    {
        if (_session is not { } session) return;
        if (!await ConfirmDiscardAsync("starting a new map")) return;

        string? name = await NameDialog.AskAsync(
            this, "New map", "Name for the new scene:", "Untitled");
        if (name is null) return;

        session.NewMap(name, error => Dispatcher.UIThread.Post(() =>
        {
            // The session may have been replaced by the time this post runs. A
            // stale callback must not rebind the new session's document.
            if (!ReferenceEquals(session, _session))
                return;

            if (error is not null)
            {
                _shell.SetError($"Could not start a new map: {error.Message}");
                return;
            }

            _document.MarkNew();
            ResetDirtyBaseline();
            RequestEntityAudit();
            _shell.SetMessage($"New map: {name}");
        }));
    }

    private async void OnOpenMapClicked(object? sender, RoutedEventArgs e)
    {
        if (_session is null) return;
        if (!await ConfirmDiscardAsync("opening another map")) return;

        // Folder picker: a map bundle is a directory.
        IReadOnlyList<IStorageFolder> picked = await StorageProvider.OpenFolderPickerAsync(
            new FolderPickerOpenOptions
            {
                Title = "Open map bundle",
                AllowMultiple = false,
                SuggestedStartLocation = await SuggestedStartAsync(_document.SuggestedMapFolder),
            });

        if (picked.Count == 0) return;
        OpenMapAt(picked[0].Path.LocalPath);
    }

    // False when the open was refused here and nothing was sent to the engine.
    private bool OpenMapAt(string bundlePath)
    {
        if (_session is not { } session) return false;

        if (!MapBundle.IsBundle(bundlePath))
        {
            _shell.SetError(
                $"That folder is not a map bundle: it has no {MapFormat.DocumentFileName}.");
            return false;
        }

        session.OpenMap(bundlePath, (report, error) => Dispatcher.UIThread.Post(() =>
        {
            // Stale-session guard, as in OnNewMapClicked.
            if (!ReferenceEquals(session, _session))
                return;

            if (error is not null)
            {
                // The session shows its baseplate, which is all it will show.
                _recovery.LevelShown();
                _shell.SetError($"Could not open the map: {error.Message}");
                return;
            }

            _document.MarkOpened(bundlePath);
            ResetDirtyBaseline();

            _shell.Problems.ClearScope(ProblemScope.Map);
            RecordMapProblems(report);
            RequestEntityAudit();

            _shell.SetMessage(report?.Describe() is { } missing
                ? $"Opened {_document.MapLabel}. {missing}"
                : $"Opened {_document.MapLabel}");

            ReportRestart();
        }));

        return true;
    }

    // One session per project: the content root is fixed when a session is
    // built, so opening another project closes the session and launches a new one.

    private void OnNewProjectClicked(object? sender, RoutedEventArgs e) => _ = CreateProjectFlowAsync();
    private void OnOpenProjectClicked(object? sender, RoutedEventArgs e) => _ = OpenProjectFlowAsync();

    private async void OnCloseProjectClicked(object? sender, RoutedEventArgs e)
    {
        // Checks the viewport, not the session: a session that failed to start
        // leaves a viewport with nothing behind it, and this must still work.
        // So must closing a viewport that was left stopped.
        if (_viewport is null && !_recovery.IsStopped) return;
        if (!await ConfirmDiscardAsync("closing the project")) return;

        CloseSessionView();
        _document.SetProject(null);
        _document.MarkNew();
        _shell.SetMessage(string.Empty);
    }

    private async Task CreateProjectFlowAsync()
    {
        if (!await ConfirmDiscardAsync("creating a new project")) return;

        IReadOnlyList<IStorageFolder> picked = await StorageProvider.OpenFolderPickerAsync(
            new FolderPickerOpenOptions { Title = "Folder to create the project in", AllowMultiple = false });
        if (picked.Count == 0) return;

        string? name = await NameDialog.AskAsync(
            this, "New project", "Name for the project:", "MyGame");
        if (name is null) return;

        name = name.Trim();
        if (name.Length == 0 || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            _shell.SetError("A project name has to work as a folder name.");
            return;
        }

        string root = Path.Combine(picked[0].Path.LocalPath, name);
        if (Directory.Exists(root) && Directory.EnumerateFileSystemEntries(root).Any())
        {
            // Any non-empty folder is refused: scaffolding would adopt its files,
            // and a second manifest makes the folder unopenable.
            _shell.SetError($"'{root}' already exists and is not empty; open it as a project, or pick another name.");
            return;
        }

        ProjectLayout layout;
        try
        {
            layout = ProjectLayout.Create(root, name);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ProjectFormatException)
        {
            _shell.SetError($"Could not create the project: {ex.Message}");
            return;
        }

        OpenProjectLayout(layout);
    }

    private async Task OpenProjectFlowAsync()
    {
        if (!await ConfirmDiscardAsync("opening another project")) return;

        IReadOnlyList<IStorageFolder> picked = await StorageProvider.OpenFolderPickerAsync(
            new FolderPickerOpenOptions { Title = "Open project folder", AllowMultiple = false });
        if (picked.Count == 0) return;

        OpenProjectAt(picked[0].Path.LocalPath);
    }

    // Updates the start page and the File menu's recents together.
    private void RefreshRecents()
    {
        StartView.ShowRecents(_settings.RecentProjects);

        // Remove only the dynamic items; the XAML ones stay.
        for (int i = RecentProjectsMenu.Items.Count - 1; i >= 0; i--)
        {
            if (RecentProjectsMenu.Items[i] is MenuItem { DataContext: RecentProject })
                RecentProjectsMenu.Items.RemoveAt(i);
        }

        IReadOnlyList<RecentProject> recents = _settings.RecentProjects;
        RecentProjectsEmptyItem.IsVisible = recents.Count == 0;
        RecentProjectsSeparator.IsVisible = recents.Count > 0;
        RecentProjectsClearItem.IsVisible = recents.Count > 0;

        for (int i = 0; i < recents.Count; i++)
        {
            RecentProject recent = recents[i];
            var item = new MenuItem
            {
                Header = recent.Name,
                DataContext = recent,
            };
            ToolTip.SetTip(item, recent.Path);
            item.Click += (_, _) => _ = OpenRecentProjectAsync(recent);
            RecentProjectsMenu.Items.Insert(i, item);
        }
    }

    private void ForgetRecent(RecentProject recent)
    {
        _settings.ForgetProject(recent.Path);
        _settings.Save(_logger);
        RefreshRecents();
    }

    private void OnClearRecentsClicked(object? sender, RoutedEventArgs e)
    {
        foreach (RecentProject recent in _settings.RecentProjects.ToArray())
            _settings.ForgetProject(recent.Path);

        _settings.Save(_logger);
        RefreshRecents();
    }

    private void RevealInExplorer(string? path)
    {
        if (string.IsNullOrEmpty(path))
            return;

        string full;
        try
        {
            full = Path.GetFullPath(path);
        }
        catch (Exception ex) when (ex is ArgumentException or PathTooLongException)
        {
            _shell.SetError($"Not a usable path: {path}");
            return;
        }

        if (!File.Exists(full) && !Directory.Exists(full))
        {
            _shell.SetError($"'{full}' is not on disk any more.");
            return;
        }

        try
        {
            // /select shows the item in its parent instead of opening it.
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{full}\"")
            {
                UseShellExecute = false,
            });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException)
        {
            _logger.LogWarning(ex, "Could not open Explorer at {Path}", full);
            _shell.SetError("Could not open the file browser.");
        }
    }

    // Re-reads the manifest from disk before editing it: the copy in memory
    // may be behind the author's hand edits.
    private void SetStartupMap(ProjectMapRow row)
    {
        if (_document.Project is not { } stale)
            return;

        ProjectLayout project;
        try
        {
            project = ProjectLayout.Open(stale.ManifestPath);
        }
        catch (Exception ex) when (ex is FileNotFoundException or ProjectFormatException)
        {
            _logger.LogWarning(ex, "Could not re-read the project manifest");
            _shell.SetError("The project manifest could not be read.");
            return;
        }

        // The startup map must also be in the manifest's list.
        if (!project.Project.Maps.Any(m => ManifestPathsEqual(m, row.RelativePath)))
            project.Project.Maps.Add(row.RelativePath);

        project.Project.StartupMap = row.RelativePath;

        try
        {
            project.Save();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Could not write the project manifest");
            _shell.SetError("The project manifest could not be written.");
            return;
        }

        _document.SetProject(project);
        RefreshProjectMaps();
        _shell.SetMessage($"{row.Name} is now the startup map.");
    }

    private async Task OpenRecentProjectAsync(RecentProject recent)
    {
        if (!Directory.Exists(recent.Path))
        {
            _settings.ForgetProject(recent.Path);
            _settings.Save(_logger);
            RefreshRecents();
            _shell.SetError($"'{recent.Path}' is gone; removed it from the recent list.");
            return;
        }

        if (!await ConfirmDiscardAsync("opening another project")) return;
        OpenProjectAt(recent.Path);
    }

    private async Task OpenLooseMapFlowAsync()
    {
        // One bundle, no project.
        IReadOnlyList<IStorageFolder> picked = await StorageProvider.OpenFolderPickerAsync(
            new FolderPickerOpenOptions { Title = "Open map bundle", AllowMultiple = false });
        if (picked.Count == 0) return;

        string path = picked[0].Path.LocalPath;
        if (!MapBundle.IsBundle(path))
        {
            _shell.SetError($"That folder is not a map bundle: it has no {MapFormat.DocumentFileName}.");
            return;
        }

        _document.SetProject(null);
        LaunchSession(new SessionLaunch(null, null, Path.GetFullPath(path)));
    }

    private void OpenProjectAt(string path)
    {
        ProjectLayout layout;
        try
        {
            layout = ProjectLayout.Open(path);
        }
        catch (Exception ex) when (ex is FileNotFoundException or ProjectFormatException)
        {
            _shell.SetError($"Could not open the project: {ex.Message}");
            return;
        }

        OpenProjectLayout(layout);
    }

    private void OpenProjectLayout(ProjectLayout layout)
    {
        CloseSessionView();

        _document.SetProject(layout);
        _document.MarkNew();

        _settings.TouchProject(layout.Root, layout.Project.Name, DateTime.UtcNow);
        _settings.Save(_logger);
        RefreshRecents();

        // A missing startup map is reported and the session still starts.
        string? mapToOpen = null;
        if (layout.Project.StartupMap is { Length: > 0 } startup)
        {
            string resolved = layout.Resolve(startup);
            if (MapBundle.IsBundle(resolved))
                mapToOpen = resolved;
            else
                _shell.SetError($"The project's startup map '{startup}' is not on disk; starting on a baseplate.");
        }

        LaunchSession(new SessionLaunch(layout, layout.AssetsPath, mapToOpen));
        RefreshProjectMaps();
    }

    // The manifest's maps in the author's order, then unlisted ones found on disk.
    private void RefreshProjectMaps()
    {
        _shell.ProjectMaps.Clear();

        if (_document.Project is not { } project)
        {
            _shell.HasProject = false;
            return;
        }

        _shell.HasProject = true;
        string? startup = project.Project.StartupMap;

        // Normalised slashes, so a hand-written backslash path is the same row.
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (string relative in project.Project.Maps)
        {
            if (!seen.Add(relative.Replace('\\', '/'))) continue;
            _shell.ProjectMaps.Add(new ProjectMapRow(
                relative,
                MapDisplayName(relative),
                IsStartup: startup is not null && ManifestPathsEqual(relative, startup),
                IsUnlisted: false));
        }

        foreach (string relative in project.DiscoverMaps())
        {
            if (!seen.Add(relative.Replace('\\', '/'))) continue;
            _shell.ProjectMaps.Add(new ProjectMapRow(
                relative, MapDisplayName(relative), IsStartup: false, IsUnlisted: true));
        }
    }

    private static string MapDisplayName(string projectRelative) =>
        Path.GetFileNameWithoutExtension(projectRelative.Replace('/', Path.DirectorySeparatorChar));

    private async Task OpenProjectMapAsync(ProjectMapRow row)
    {
        if (_document.Project is not { } project || _session is null) return;

        string resolved = project.Resolve(row.RelativePath);
        if (!MapBundle.IsBundle(resolved))
        {
            _shell.SetError($"'{row.RelativePath}' is in the manifest but not on disk.");
            return;
        }

        if (_document.MapPath is { } current &&
            string.Equals(Path.GetFullPath(resolved), current, StringComparison.OrdinalIgnoreCase))
        {
            _shell.SetMessage($"{row.Name} is already open");
            return;
        }

        if (!await ConfirmDiscardAsync($"opening {row.Name}")) return;
        OpenMapAt(resolved);
    }

    private void OnSaveClicked(object? sender, RoutedEventArgs e)
    {
        if (RefuseSaveWhilePlaying())
            return;

        if (_document.MapPath is { } path)
            SaveMapTo(path);
        else
            OnSaveAsClicked(sender, e);
    }

    private async void OnSaveAsClicked(object? sender, RoutedEventArgs e)
    {
        if (RefuseSaveWhilePlaying())
            return;

        if (await PickSaveTargetAsync() is { } target)
            SaveMapTo(target);
    }

    // The session refuses as well. Asking here spares the folder picker.
    private bool RefuseSaveWhilePlaying()
    {
        if (!_latest.IsPlaying && !_shell.IsPlaying)
            return false;

        _shell.SetMessage("Stop the run before saving.");
        return true;
    }

    // A folder picker plus a name dialog: a level is a folder, and a save-file
    // dialog names files. Null when the user backed out.
    private async Task<string?> PickSaveTargetAsync()
    {
        if (_session is null)
            return null;

        IReadOnlyList<IStorageFolder> picked = await StorageProvider.OpenFolderPickerAsync(
            new FolderPickerOpenOptions
            {
                Title = "Folder to save the level into",
                AllowMultiple = false,
                SuggestedStartLocation = await SuggestedStartAsync(_document.SuggestedMapFolder),
            });

        if (picked.Count == 0)
            return null;

        string? name = await NameDialog.AskAsync(
            this, "Save level as", "Name for the level folder:", _document.MapLabel);

        return name is null
            ? null
            : Path.Combine(picked[0].Path.LocalPath, name + MapFormat.BundleExtension);
    }

    private void SaveMapTo(string bundlePath) => _ = SaveMapToAsync(bundlePath);

    // Awaitable because the unsaved-work prompt must know the save really landed.
    private Task<bool> SaveMapToAsync(string bundlePath)
    {
        if (_session is not { } session)
        {
            // The level is held for the restart. Doing nothing here would
            // look like a save.
            if (_recovery.IsStopped)
                _shell.SetError(SessionFaultText.SaveWhileStopped);

            return Task.FromResult(false);
        }

        var done = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        session.SaveMap(bundlePath, (report, error) => Dispatcher.UIThread.Post(() =>
        {
            // Stale-session guard, as in OnNewMapClicked.
            if (!ReferenceEquals(session, _session))
                return;

            if (error is not null)
            {
                _shell.SetError($"Could not save the level: {error.Message}");
                done.TrySetResult(false);
                return;
            }

            _document.MarkSaved(bundlePath);
            ResetDirtyBaseline();

            string manifestNote = UpdateManifestAfterSave();

            // The report lists what the format could not save, e.g. a mesh built in code.
            _shell.SetMessage(report?.Describe() is { } lost
                ? $"Saved {_document.MapLabel}.{manifestNote} {lost}"
                : $"Saved {_document.MapLabel}.{manifestNote}");

            done.TrySetResult(true);
        }));

        // If the session is torn down before the callback runs, this never completes.
        return done.Task;
    }

    // Lists a just-saved map in the project manifest and makes it the startup
    // map if there is none. Returns a note for the status line. A map saved
    // outside the project is not listed, and entries are never removed.
    private string UpdateManifestAfterSave()
    {
        if (_document.Project is not { } stale)
            return string.Empty;

        if (_document.MapPathWithinProject() is not { } relative)
            return string.Empty;

        // Re-read from disk and edit that. Writing the copy loaded at open
        // time would revert every hand edit made since.
        ProjectLayout project;
        try
        {
            project = ProjectLayout.Open(stale.ManifestPath);
        }
        catch (Exception ex) when (ex is FileNotFoundException or ProjectFormatException)
        {
            _logger.LogWarning(ex, "Saved the map but could not re-read the project manifest");
            _shell.SetError("The map saved, but the project manifest could not be read to list it.");
            return string.Empty;
        }

        bool listed = project.Project.Maps.Any(m => ManifestPathsEqual(m, relative));
        bool becameStartup = false;

        if (!listed)
            project.Project.Maps.Add(relative);

        if (string.IsNullOrEmpty(project.Project.StartupMap))
        {
            project.Project.StartupMap = relative;
            becameStartup = true;
        }

        try
        {
            // Save skips the write when the bytes are unchanged.
            project.Save();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Saved the map but could not update the project manifest");
            _shell.SetError("The map saved, but the project manifest could not be written.");
            return string.Empty;
        }

        _document.SetProject(project);
        RefreshProjectMaps();
        return !listed
            ? becameStartup ? " Added to the project as its startup map." : " Added to the project."
            : string.Empty;
    }

    // A hand-edited manifest may use backslashes or different case.
    private static bool ManifestPathsEqual(string a, string b) =>
        string.Equals(a.Replace('\\', '/'), b.Replace('\\', '/'), StringComparison.OrdinalIgnoreCase);

    // Asks before unsaved work is thrown away. True means go ahead.
    private async Task<bool> ConfirmDiscardAsync(string what)
    {
        if (!_document.IsDirty) return true;

        UnsavedChoice choice = await ConfirmDialog.AskAsync(this, _document.MapLabel, what);

        return choice switch
        {
            UnsavedChoice.Discard => true,
            UnsavedChoice.Save => await SaveFromPromptAsync(),
            _ => false,
        };
    }

    // True only when the level is on disk. Cancelling the target picker means
    // "go back", not "discard".
    private async Task<bool> SaveFromPromptAsync()
    {
        string? target = _document.MapPath ?? await PickSaveTargetAsync();
        return target is not null && await SaveMapToAsync(target);
    }

    private async Task<IStorageFolder?> SuggestedStartAsync(string? path)
    {
        if (path is null || !Directory.Exists(path)) return null;
        try { return await StorageProvider.TryGetFolderFromPathAsync(path); }
        catch (IOException) { return null; }
    }

    // After a save or load, so the history movement they cause doesn't mark
    // the document dirty again.
    private void ResetDirtyBaseline()
    {
        _lastUndoDepth = _latest.UndoDepth;
        _lastRedoDepth = _latest.RedoDepth;
    }

    private GraphicsBackend ResolveBackend()
    {
        GraphicsBackend backend = ResolveRequestedBackend();

        // Refused here by name; otherwise it surfaces as a driver failure.
        if (backend is GraphicsBackend.OpenGL)
        {
            throw new NotSupportedException(
                "The editor viewport cannot host OpenGL yet; use d3d11 or d3d12.");
        }

        return backend;
    }

    // Includes OpenGL, which ResolveBackend refuses: the viewport policy needs
    // to name it.
    private static GraphicsBackend ResolveRequestedBackend()
    {
        foreach (string arg in Program.StartupArgs)
        {
            switch (arg.ToLowerInvariant())
            {
                case "d3d11": return GraphicsBackend.D3D11;
                case "d3d12": return GraphicsBackend.D3D12;
                case "opengl": return GraphicsBackend.OpenGL;
            }
        }

        return GraphicsBackend.D3D11;
    }
}
