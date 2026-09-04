using SpectraEngine.Core.Scene;
using SpectraEngine.Editing.Cameras;
using SpectraEngine.Editing.Gizmos;
using SpectraEngine.Editing.Hosting;

namespace SpectraEngine.Editor.Shell;

/// <summary>What a command needs before it is worth offering.</summary>
[Flags]
public enum CommandNeeds
{
    /// <summary>Nothing beyond an open session.</summary>
    None = 0,

    /// <summary>Something has to be selected.</summary>
    Selection = 1,

    /// <summary>Refused while play mode owns the scene.</summary>
    NotPlaying = 2,
}

/// <summary>One row of the palette.</summary>
/// <param name="Title">What the palette shows and what the query is matched against.</param>
/// <param name="Verb">The existing verb it resolves to.</param>
/// <param name="Needs">What has to be true for it to be offered.</param>
/// <param name="Gesture">The chord that also reaches it, printed beside it, or empty.</param>
public sealed record ShellCommand(string Title, ShellVerb Verb, CommandNeeds Needs = CommandNeeds.None, string Gesture = "");

/// <summary>
/// Every verb the palette can reach, by name.
/// </summary>
/// <remarks>
/// <para>
/// <b>The palette is a second reader of one dispatcher, not a fourth command
/// path.</b> Every row here carries a <see cref="ShellVerb"/> - the same closed
/// union the ribbon's roster carries - and <c>MainWindow.OnShellVerb</c> already
/// routes each kind through the window's own optimistic handlers, so a command
/// invoked from here cannot light a frame later than the same command invoked
/// from a button. That type was called <c>RibbonVerb</c> and lived under
/// <c>Shell/Ribbon/</c>; it was renamed and lifted precisely so this could be
/// true rather than nearly true.
/// </para>
/// <para>
/// <b>Hand-written, and no reflection anywhere.</b> Enumerating verbs by
/// reflecting over an enum is what trimming removes, and it would fail in a
/// published build having worked in every debug run - the same discipline
/// <c>GizmoShortcuts.TryResolve</c> and <c>ConsoleCommands</c> already follow.
/// </para>
/// <para>
/// <b>It is a SUPERSET of the ribbon, and a test says so.</b> Every verb on
/// either page appears here; the rows beyond them are the document verbs and
/// the ones the ribbon deliberately does not carry.
/// </para>
/// </remarks>
public static class CommandTable
{
    /// <summary>Every row, in no particular order: the score decides what is shown.</summary>
    public static IReadOnlyList<ShellCommand> Commands { get; } =
    [
        // ─── Insert ──────────────────────────────────────
        new("Insert block", ShellVerb.Of(InsertKind.WorldBrush), CommandNeeds.NotPlaying, "Ctrl+1"),
        new("Insert part", ShellVerb.Of(InsertKind.PartBrush), CommandNeeds.NotPlaying, "Ctrl+2"),
        new("Insert cut", ShellVerb.Of(InsertKind.SubtractiveBrush), CommandNeeds.NotPlaying, "Ctrl+3"),
        new("Insert light", ShellVerb.Of(InsertKind.PointLight), CommandNeeds.NotPlaying, "Ctrl+4"),
        new("Insert surface light panel", ShellVerb.Of(InsertKind.SurfaceLight), CommandNeeds.NotPlaying),
        new("Insert empty group", ShellVerb.Of(InsertKind.Group), CommandNeeds.NotPlaying),
        new("Insert entity", ShellVerb.InsertEntity(), CommandNeeds.NotPlaying),

        // ─── Tools ───────────────────────────────────────
        new("Move tool", ShellVerb.Of(GizmoCommand.UseTranslate), CommandNeeds.None, "W"),
        new("Rotate tool", ShellVerb.Of(GizmoCommand.UseRotate), CommandNeeds.None, "E"),
        new("Size tool", ShellVerb.Of(GizmoCommand.UseScale), CommandNeeds.None, "R"),
        new("Drag axes: world or local", ShellVerb.Of(ShellToggle.Axes), CommandNeeds.None, "X"),
        new("Handles: Studio or Classic", ShellVerb.Of(ShellToggle.Handles), CommandNeeds.None, "Y"),

        // ─── Snap ────────────────────────────────────────
        new("Snap to grid", ShellVerb.Of(ShellToggle.Snap), CommandNeeds.None, "G"),
        new("Finer grid", ShellVerb.Of(GizmoCommand.FinerSnap), CommandNeeds.None, "["),
        new("Coarser grid", ShellVerb.Of(GizmoCommand.CoarserSnap), CommandNeeds.None, "]"),

        // ─── Arrange ─────────────────────────────────────
        new("Duplicate", ShellVerb.Of(EditorHostCommand.Duplicate), CommandNeeds.Selection | CommandNeeds.NotPlaying, "Ctrl+D"),
        new("Delete", ShellVerb.Of(EditorHostCommand.Delete), CommandNeeds.Selection | CommandNeeds.NotPlaying, "Del"),
        new("Convert block or part", ShellVerb.Of(EditorHostCommand.ToggleBrushKind), CommandNeeds.Selection | CommandNeeds.NotPlaying, "Ctrl+T"),
        new("Group", ShellVerb.Of(EditorHostCommand.Group), CommandNeeds.Selection | CommandNeeds.NotPlaying, "Ctrl+G"),
        new("Ungroup", ShellVerb.Of(EditorHostCommand.Ungroup), CommandNeeds.Selection | CommandNeeds.NotPlaying, "Ctrl+Shift+G"),

        // ─── History and selection ───────────────────────
        new("Undo", ShellVerb.Of(EditorHostCommand.Undo), CommandNeeds.NotPlaying, "Ctrl+Z"),
        new("Redo", ShellVerb.Of(EditorHostCommand.Redo), CommandNeeds.NotPlaying, "Ctrl+Y"),
        new("Select all", ShellVerb.Of(EditorHostCommand.SelectAll), CommandNeeds.None, "Ctrl+A"),
        new("Deselect all", ShellVerb.Of(EditorHostCommand.ClearSelection), CommandNeeds.Selection, "Esc"),

        // ─── View ────────────────────────────────────────
        new("Frame the selection", ShellVerb.Of(EditorCameraCommand.FrameSelection), CommandNeeds.Selection, "F"),
        new("Frame everything", ShellVerb.Of(EditorCameraCommand.FrameAll), CommandNeeds.None, "Shift+F"),
        new("Camera: editor or fly", ShellVerb.Of(EditorHostCommand.ToggleNavigation), CommandNeeds.None, "F7"),
        new("Ground grid: during move and resize", ShellVerb.Of(EditorHostCommand.GridAuto)),
        new("Ground grid: always", ShellVerb.Of(EditorHostCommand.GridOn)),
        new("Ground grid: off", ShellVerb.Of(EditorHostCommand.GridOff)),

        // ─── Overlays ────────────────────────────────────
        new("Overlay: wireframe", ShellVerb.Of(DebugVisualization.Wireframe), CommandNeeds.None, "F1"),
        new("Overlay: CSG vertices", ShellVerb.Of(DebugVisualization.Vertices), CommandNeeds.None, "F2"),
        new("Overlay: bounds", ShellVerb.Of(DebugVisualization.Aabbs), CommandNeeds.None, "F3"),
        new("Overlay: face normals", ShellVerb.Of(DebugVisualization.Normals), CommandNeeds.None, "F4"),
        new("Overlay: node axes", ShellVerb.Of(DebugVisualization.SceneGraph), CommandNeeds.None, "F5"),
    ];

    /// <summary>
    /// The rows matching <paramref name="query"/>, best first, filtered by what
    /// the session can currently do.
    /// </summary>
    public static IReadOnlyList<ShellCommand> Search(string query, bool hasSelection, bool isPlaying, int limit = 12)
    {
        ArgumentNullException.ThrowIfNull(query);

        return Commands
            .Where(c => Available(c, hasSelection, isPlaying))
            .Select(c => (Command: c, Score: CommandScore.Of(c.Title, query)))
            .Where(row => row.Score != CommandScore.NoMatch)

            // Ordinal by title after the score, so two rows that score the same
            // come out in the same order every time somebody types the same
            // letters. A palette whose list reshuffles between identical queries
            // is one nobody can build muscle memory against.
            .OrderByDescending(row => row.Score)
            .ThenBy(row => row.Command.Title, StringComparer.Ordinal)
            .Take(limit)
            .Select(row => row.Command)
            .ToList();
    }

    /// <summary>Whether a command is worth offering right now.</summary>
    public static bool Available(ShellCommand command, bool hasSelection, bool isPlaying)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (command.Needs.HasFlag(CommandNeeds.Selection) && !hasSelection)
        {
            return false;
        }

        return !command.Needs.HasFlag(CommandNeeds.NotPlaying) || !isPlaying;
    }
}
