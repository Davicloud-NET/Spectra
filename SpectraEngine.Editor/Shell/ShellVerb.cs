using SpectraEngine.Core.Scene;
using SpectraEngine.Editing.Cameras;
using SpectraEngine.Editing.Gizmos;
using SpectraEngine.Editing.Hosting;

namespace SpectraEngine.Editor.Shell;

/// <summary>Which family of verb a shell control resolves to.</summary>
public enum ShellVerbKind
{
    /// <summary>Nothing. Never valid on a roster item.</summary>
    None,

    /// <summary><see cref="EditorHostCommand"/>.</summary>
    Host,

    /// <summary><see cref="GizmoCommand"/>.</summary>
    Gizmo,

    /// <summary><see cref="EditorCameraCommand"/>.</summary>
    Camera,

    /// <summary><see cref="InsertKind"/>, through <c>SceneEditorHost.Insert</c>.</summary>
    Insert,

    /// <summary>
    /// One <see cref="DebugVisualization"/> flag, through
    /// <c>EngineHost.RequestDebugVisualization</c>.
    /// </summary>
    Debug,

    /// <summary>
    /// A two-way choice whose target verb depends on the state it is in. See
    /// <see cref="ShellToggle"/>.
    /// </summary>
    Toggle,

    /// <summary>
    /// The snap increment field, through <c>SceneEditorHost.SetSnapIncrement</c>.
    /// </summary>
    SnapIncrement,

    /// <summary>
    /// Place an entity, through <c>EditorSession.InsertEntity</c>. Carries no
    /// class: entity classes come from the project's <c>.sentdef</c>, so the
    /// class is resolved from the session when the button is pressed.
    /// </summary>
    InsertEntity,

    /// <summary>
    /// Make the selection an entity, through <c>EditorSession.MakeEntity</c>.
    /// Carries no class: it opens the list of classes made from geometry, and
    /// the row picked there names the class.
    /// </summary>
    MakeEntity,

    /// <summary>
    /// Take the entity off the selection, through
    /// <c>EditorSession.RemoveEntity</c>.
    /// </summary>
    RemoveEntity,

    /// <summary>
    /// A document verb: a project, a level, a save. See <see cref="DocumentVerb"/>.
    /// </summary>
    Document,

    /// <summary>Enter or leave play mode. See <see cref="PlayVerb"/>.</summary>
    Play,

    /// <summary>Show one panel. See <see cref="PanelId"/>.</summary>
    Panel,

    /// <summary>Collapse or expand the ribbon. See <see cref="RibbonVerb"/>.</summary>
    Ribbon,

    /// <summary>
    /// A workspace verb: the preset, the drawer, maximise. See
    /// <see cref="WorkspaceCommand"/>.
    /// </summary>
    Workspace,
}

/// <summary>A verb about the project or the level, rather than the scene.</summary>
public enum DocumentVerb
{
    /// <summary>Create a project.</summary>
    NewProject,

    /// <summary>Open a project.</summary>
    OpenProject,

    /// <summary>Close the open project.</summary>
    CloseProject,

    /// <summary>Start a fresh level.</summary>
    NewLevel,

    /// <summary>Open a level bundle.</summary>
    OpenLevel,

    /// <summary>Save the open level.</summary>
    Save,

    /// <summary>Save the open level somewhere else.</summary>
    SaveAs,

    /// <summary>Cook the project and check the pack.</summary>
    ValidateCooked,

    /// <summary>Close the editor.</summary>
    Exit,
}

/// <summary>Play mode, as a pair of set verbs.</summary>
// Not a toggle: one sent against a stale snapshot flips the wrong way.
public enum PlayVerb
{
    /// <summary>Enter play mode.</summary>
    Play,

    /// <summary>Leave play mode.</summary>
    Stop,
}

/// <summary>A panel the window can bring to the front.</summary>
public enum PanelId
{
    /// <summary>The scene tree.</summary>
    Scene,

    /// <summary>The project's levels.</summary>
    Levels,

    /// <summary>The inspector.</summary>
    Properties,

    /// <summary>The content browser.</summary>
    Content,

    /// <summary>The output history.</summary>
    Output,

    /// <summary>The standing problems.</summary>
    Problems,

    /// <summary>The console.</summary>
    Console,

    /// <summary>The keyboard reference window.</summary>
    KeyboardReference,
}

/// <summary>The ribbon's two states, as set verbs.</summary>
public enum RibbonVerb
{
    /// <summary>Show the tab strip alone.</summary>
    Collapse,

    /// <summary>Show the active page.</summary>
    Expand,
}

/// <summary>
/// A verb about how the window is arranged, rather than about the level.
/// </summary>
public enum WorkspaceCommand
{
    /// <summary>Give the viewport the whole window.</summary>
    MaximiseViewport,

    /// <summary>Put the panels back.</summary>
    RestoreWorkspace,

    /// <summary>The compact arrangement.</summary>
    UseCompactWorkspace,

    /// <summary>The roomy arrangement.</summary>
    UseExpandedWorkspace,

    /// <summary>Show the bottom panel.</summary>
    OpenBottomDrawer,

    /// <summary>Hide the bottom panel.</summary>
    CloseBottomDrawer,

    /// <summary>Show the engine counters in the status bar.</summary>
    ShowDiagnostics,

    /// <summary>Hide them.</summary>
    HideDiagnostics,

    /// <summary>Show the Logic view under the 3D view.</summary>
    ShowLogicBelow,

    /// <summary>Show the Logic view beside the 3D view.</summary>
    ShowLogicBeside,

    /// <summary>Give the centre back to the 3D view.</summary>
    HideLogic,
}

/// <summary>
/// A two-way choice the ribbon offers. Each resolves to one of a pair of
/// set verbs, chosen from the state the shell is displaying.
/// </summary>
public enum ShellToggle
{
    /// <summary>World or local drag axes.</summary>
    Axes,

    /// <summary>Studio or Classic manipulator handles.</summary>
    Handles,

    /// <summary>Snapping on or off.</summary>
    Snap,
}

/// <summary>One editor verb, named by a ribbon control or a palette row.</summary>
// A value, not a delegate: RibbonLayoutTests compares verbs across tabs to
// catch one verb sitting on two of them.
public readonly record struct ShellVerb(
    ShellVerbKind Kind,
    EditorHostCommand Host,
    GizmoCommand Gizmo,
    EditorCameraCommand Camera,
    InsertKind Insert,
    DebugVisualization Debug,
    ShellToggle Toggle,
    DocumentVerb Document = default,
    PlayVerb Play = default,
    PanelId Panel = default,
    RibbonVerb Ribbon = default,
    WorkspaceCommand Workspace = default)
{
    /// <summary>A host verb: history, a structural edit, a grid mode.</summary>
    public static ShellVerb Of(EditorHostCommand command) =>
        new(ShellVerbKind.Host, command, default, default, default, default, default);

    /// <summary>A manipulator verb.</summary>
    public static ShellVerb Of(GizmoCommand command) =>
        new(ShellVerbKind.Gizmo, default, command, default, default, default, default);

    /// <summary>A camera verb.</summary>
    public static ShellVerb Of(EditorCameraCommand command) =>
        new(ShellVerbKind.Camera, default, default, command, default, default, default);

    /// <summary>An insert.</summary>
    public static ShellVerb Of(InsertKind kind) =>
        new(ShellVerbKind.Insert, default, default, default, kind, default, default);

    /// <summary>One debug overlay flag.</summary>
    public static ShellVerb Of(DebugVisualization flag) =>
        new(ShellVerbKind.Debug, default, default, default, default, flag, default);

    /// <summary>A two-way choice.</summary>
    public static ShellVerb Of(ShellToggle toggle) =>
        new(ShellVerbKind.Toggle, default, default, default, default, default, toggle);

    /// <summary>The snap increment field.</summary>
    public static ShellVerb SnapIncrement() =>
        new(ShellVerbKind.SnapIncrement, default, default, default, default, default, default);

    /// <summary>Place an entity of whichever class the session last used.</summary>
    public static ShellVerb InsertEntity() =>
        new(ShellVerbKind.InsertEntity, default, default, default, default, default, default);

    /// <summary>Open the list of classes the selection can be made into.</summary>
    public static ShellVerb MakeEntity() =>
        new(ShellVerbKind.MakeEntity, default, default, default, default, default, default);

    /// <summary>Take the entity off the selection.</summary>
    public static ShellVerb RemoveEntity() =>
        new(ShellVerbKind.RemoveEntity, default, default, default, default, default, default);

    /// <summary>A document verb.</summary>
    public static ShellVerb Of(DocumentVerb verb) =>
        new(ShellVerbKind.Document, default, default, default, default, default, default, Document: verb);

    /// <summary>Play or stop.</summary>
    public static ShellVerb Of(PlayVerb verb) =>
        new(ShellVerbKind.Play, default, default, default, default, default, default, Play: verb);

    /// <summary>Show a panel.</summary>
    public static ShellVerb Of(PanelId panel) =>
        new(ShellVerbKind.Panel, default, default, default, default, default, default, Panel: panel);

    /// <summary>Collapse or expand the ribbon.</summary>
    public static ShellVerb Of(RibbonVerb verb) =>
        new(ShellVerbKind.Ribbon, default, default, default, default, default, default, Ribbon: verb);

    /// <summary>A workspace verb.</summary>
    public static ShellVerb Of(WorkspaceCommand command) =>
        new(ShellVerbKind.Workspace, default, default, default, default, default, default, Workspace: command);
}
