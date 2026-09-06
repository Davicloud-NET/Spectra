using SpectraEngine.Core.Scene;
using SpectraEngine.Editing.Cameras;
using SpectraEngine.Editing.Gizmos;
using SpectraEngine.Editing.Hosting;

namespace SpectraEngine.Editor.Shell;

/// <summary>
/// Which family of existing verb a ribbon control resolves to.
/// </summary>
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
    /// The one ribbon control whose verb carries a NUMBER rather than naming a
    /// state, which is exactly why it is a field and not a button.
    /// </summary>
    SnapIncrement,

    /// <summary>
    /// Place an entity, through <c>EditorSession.InsertEntity</c>.
    /// </summary>
    /// <remarks>
    /// <b>The one verb here that carries no payload and cannot.</b> Every other
    /// kind names something this build knows at compile time; an entity class
    /// comes from the project's own <c>.sentdef</c>, so the roster - which is
    /// compile-time data - can name the CONTROL and not the class. The class is
    /// session state, resolved when the button is pressed: the split's main
    /// half places the last one used and its caret opens the list.
    /// </remarks>
    InsertEntity,

    /// <summary>
    /// A document verb: a project, a level, a save. See <see cref="DocumentVerb"/>.
    /// </summary>
    /// <remarks>
    /// <b>These are the window's own, not the editor's</b>, which is why they
    /// were the last to reach the palette: everything else here resolves to an
    /// enum some other assembly already declared, and a document verb resolves
    /// to a menu handler in this file's own window. Naming them anyway is what
    /// makes "save" reachable by typing it.
    /// </remarks>
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

/// <summary>
/// Play mode, as a pair of SET verbs.
/// </summary>
/// <remarks>
/// Two rows rather than one toggle, for the reason every other two-way choice
/// in this shell is a pair: a toggle sent against a stale snapshot flips the
/// wrong way exactly when somebody clicks fastest.
/// </remarks>
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

/// <summary>The ribbon's two states, as SET verbs.</summary>
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
}

/// <summary>
/// A two-way choice the ribbon offers. Each resolves to one of a PAIR of
/// existing idempotent verbs, chosen from the state the shell is displaying.
/// </summary>
/// <remarks>
/// <b>A pair of set verbs, never a toggle verb.</b> A toggle sent against a
/// snapshot one publish stale flips the wrong way exactly when the user clicks
/// fastest; a verb that names its target state cannot. The keyboard keeps the
/// toggles, which is right, because a key press carries no displayed state to
/// disagree with.
/// </remarks>
public enum ShellToggle
{
    /// <summary>World or local drag axes.</summary>
    Axes,

    /// <summary>Studio or Classic manipulator handles.</summary>
    Handles,

    /// <summary>Snapping on or off.</summary>
    Snap,
}

/// <summary>
/// Exactly one existing editor verb, named by a ribbon control.
/// </summary>
/// <remarks>
/// <para>
/// <b>A closed union rather than a delegate or a string, because the roster is
/// the thing the tests read.</b> The defect that killed the previous tab strip -
/// two tabs carrying the same verbs - is only mechanically checkable if a verb
/// is a VALUE that compares equal to itself across tabs, so this is a record
/// struct and <c>RibbonLayoutTests</c> compares it. A click handler carrying a
/// lambda would make the same defect invisible again.
/// </para>
/// <para>
/// <b>Nothing here is a new verb.</b> Every case names a member of an enum that
/// already existed and already had a keyboard route and a menu route; the
/// ribbon is a third route onto the same <c>SceneEditorHost.Apply</c> surface,
/// never a second command path. That is the property <c>ROADMAP.md</c>'s H4
/// bullet asks to protect.
/// </para>
/// </remarks>
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
