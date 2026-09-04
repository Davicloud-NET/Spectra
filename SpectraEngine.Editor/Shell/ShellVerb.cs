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
    ShellToggle Toggle)
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
}
