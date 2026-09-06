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

    /// <summary>Needs an engine session running.</summary>
    Session = 4,

    /// <summary>Needs an open project, which is not the same as a session.</summary>
    Project = 8,

    /// <summary>Only while play mode owns the scene.</summary>
    Playing = 16,

    /// <summary>Only while the ribbon is showing a page.</summary>
    RibbonExpanded = 32,

    /// <summary>Only while it is collapsed to the strip.</summary>
    RibbonCollapsed = 64,

    /// <summary>Needs a scene play mode can actually run.</summary>
    CanPlay = 128,
}

/// <summary>
/// What the shell can currently do, for deciding what to offer.
/// </summary>
/// <remarks>
/// <b>A struct of flags rather than a growing parameter list.</b> The gate took
/// two booleans and now needs six; every call site would otherwise have to be
/// edited every time a verb family arrives with a new condition.
/// </remarks>
public readonly record struct CommandContext(
    bool HasSelection,
    bool IsPlaying,
    bool HasSession,
    bool HasProject,
    bool RibbonExpanded,
    bool CanPlay);

/// <summary>One row of the palette.</summary>
/// <param name="Title">What the palette shows and what the query is matched against.</param>
/// <param name="Verb">The existing verb it resolves to.</param>
/// <param name="Needs">What has to be true for it to be offered.</param>
/// <param name="Gesture">The chord that also reaches it, printed beside it, or empty.</param>
/// <param name="Aliases">
/// Other words that should find it. The title is what people READ and not
/// always what they would type: nobody types "Overlay: wireframe" looking for
/// wireframe.
/// </param>
public sealed record ShellCommand(
    string Title,
    ShellVerb Verb,
    CommandNeeds Needs = CommandNeeds.None,
    string Gesture = "",
    IReadOnlyList<string>? Aliases = null)
{
    /// <summary>Other words that find this row.</summary>
    public IReadOnlyList<string> Aliases { get; init; } = Aliases ?? [];
}

/// <summary>
/// What a search found, and how much of it is being shown.
/// </summary>
/// <remarks>
/// <b>The count is the point.</b> The palette shows twelve rows and used to
/// return twelve with no way to know whether that was all of them, so a query
/// matching thirty looked exactly like a query matching twelve and the other
/// eighteen were unreachable by any means the user could see.
/// </remarks>
public readonly record struct CommandSearchResult(IReadOnlyList<ShellCommand> Rows, int TotalMatches)
{
    /// <summary>Whether anything matched that is not shown.</summary>
    public bool IsTruncated => TotalMatches > Rows.Count;

    /// <summary>What the footer says, or empty when there is nothing to add.</summary>
    public string FooterLabel => TotalMatches == 0
        ? "No command matches."
        : IsTruncated ? $"{Rows.Count} of {TotalMatches} shown. Keep typing." : string.Empty;
}

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
        new("Size tool", ShellVerb.Of(GizmoCommand.UseScale), CommandNeeds.None, "R", ["scale", "resize"]),
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
        new("Deselect all", ShellVerb.Of(EditorHostCommand.ClearSelection), CommandNeeds.Selection, "Esc", ["clear selection"]),

        // ─── View ────────────────────────────────────────
        new("Frame the selection", ShellVerb.Of(EditorCameraCommand.FrameSelection), CommandNeeds.Selection, "F", ["focus"]),
        new("Frame everything", ShellVerb.Of(EditorCameraCommand.FrameAll), CommandNeeds.None, "Shift+F"),
        new("Camera: editor or fly", ShellVerb.Of(EditorHostCommand.ToggleNavigation), CommandNeeds.None, "F7"),
        new("Ground grid: during move and resize", ShellVerb.Of(EditorHostCommand.GridAuto)),
        new("Ground grid: always", ShellVerb.Of(EditorHostCommand.GridOn)),
        new("Ground grid: off", ShellVerb.Of(EditorHostCommand.GridOff), CommandNeeds.None, "", ["hide grid"]),

        // ─── Overlays ────────────────────────────────────
        new("Overlay: wireframe", ShellVerb.Of(DebugVisualization.Wireframe), CommandNeeds.None, "F1", ["wireframe"]),
        new("Overlay: CSG vertices", ShellVerb.Of(DebugVisualization.Vertices), CommandNeeds.None, "F2"),
        new("Overlay: bounds", ShellVerb.Of(DebugVisualization.Aabbs), CommandNeeds.None, "F3"),
        new("Overlay: face normals", ShellVerb.Of(DebugVisualization.Normals), CommandNeeds.None, "F4"),
        new("Overlay: node axes", ShellVerb.Of(DebugVisualization.SceneGraph), CommandNeeds.None, "F5"),

        // ─── Documents ───────────────────────────────────
        //
        // The rows the class doc always claimed were here. Every one calls the
        // same handler the File menu does, so a confirmation the menu asks for
        // is a confirmation the palette asks for.
        new("New project...", ShellVerb.Of(DocumentVerb.NewProject)),
        new("Open project...", ShellVerb.Of(DocumentVerb.OpenProject)),
        new("Close project", ShellVerb.Of(DocumentVerb.CloseProject), CommandNeeds.Session),
        new("New level", ShellVerb.Of(DocumentVerb.NewLevel), CommandNeeds.Session, "Ctrl+N"),
        new("Open level...", ShellVerb.Of(DocumentVerb.OpenLevel), CommandNeeds.Session, "Ctrl+O"),
        new("Save level", ShellVerb.Of(DocumentVerb.Save), CommandNeeds.Session, "Ctrl+S"),
        new("Save level as...", ShellVerb.Of(DocumentVerb.SaveAs), CommandNeeds.Session, "Ctrl+Shift+S"),
        new("Validate cooked content", ShellVerb.Of(DocumentVerb.ValidateCooked), CommandNeeds.Project),
        new("Exit", ShellVerb.Of(DocumentVerb.Exit), CommandNeeds.None, "", ["quit", "close editor"]),

        // ─── Play ────────────────────────────────────────
        new("Play", ShellVerb.Of(PlayVerb.Play),
            CommandNeeds.Session | CommandNeeds.NotPlaying | CommandNeeds.CanPlay, "F8", ["run", "test"]),
        new("Stop", ShellVerb.Of(PlayVerb.Stop), CommandNeeds.Session | CommandNeeds.Playing, "F8"),

        // ─── Panels ──────────────────────────────────────
        new("Show Scene panel", ShellVerb.Of(PanelId.Scene), CommandNeeds.Session, "", ["tree", "outliner"]),
        new("Show Levels panel", ShellVerb.Of(PanelId.Levels), CommandNeeds.Session, "", ["maps"]),
        new("Show Properties panel", ShellVerb.Of(PanelId.Properties), CommandNeeds.Session, "", ["inspector"]),
        new("Show Content panel", ShellVerb.Of(PanelId.Content), CommandNeeds.Session, "", ["assets", "browser"]),
        new("Show Output panel", ShellVerb.Of(PanelId.Output), CommandNeeds.Session, "", ["log"]),
        new("Show Problems panel", ShellVerb.Of(PanelId.Problems), CommandNeeds.Session, "", ["errors", "warnings"]),
        new("Show Console panel", ShellVerb.Of(PanelId.Console), CommandNeeds.Session, "`"),
        new("Keyboard reference", ShellVerb.Of(PanelId.KeyboardReference), CommandNeeds.None, "", ["shortcuts", "keys"]),

        // ─── The ribbon ──────────────────────────────────
        new("Collapse the ribbon", ShellVerb.Of(RibbonVerb.Collapse),
            CommandNeeds.Session | CommandNeeds.RibbonExpanded, "Ctrl+F1"),
        new("Expand the ribbon", ShellVerb.Of(RibbonVerb.Expand),
            CommandNeeds.Session | CommandNeeds.RibbonCollapsed, "Ctrl+F1"),
    ];

    /// <summary>
    /// The rows matching <paramref name="query"/>, best first, filtered by what
    /// the session can currently do.
    /// </summary>
    public static CommandSearchResult Search(string query, in CommandContext context, int limit = DefaultLimit)
    {
        ArgumentNullException.ThrowIfNull(query);

        List<(ShellCommand Command, int Score)> matches = [];
        foreach (ShellCommand command in Commands)
        {
            if (!Available(command, in context)) continue;

            int score = CommandScore.Of(command.Title, query);

            // An alias scores as itself and the best wins. Without the -1 an
            // alias could outrank an exact title match, which would put "Size
            // tool" above a command actually called "Scale" if one ever existed.
            foreach (string alias in command.Aliases)
            {
                int aliasScore = CommandScore.Of(alias, query);
                if (aliasScore != CommandScore.NoMatch && aliasScore - 1 > score)
                    score = aliasScore - 1;
            }

            if (score == CommandScore.NoMatch) continue;
            matches.Add((command, score));
        }

        // Ordinal by title after the score, so two rows that score the same
        // come out in the same order every time somebody types the same
        // letters. A palette whose list reshuffles between identical queries
        // is one nobody can build muscle memory against.
        List<ShellCommand> rows = matches
            .OrderByDescending(row => row.Score)
            .ThenBy(row => row.Command.Title, StringComparer.Ordinal)
            .Take(limit)
            .Select(row => row.Command)
            .ToList();

        return new CommandSearchResult(rows, matches.Count);
    }

    /// <summary>How many rows the palette shows.</summary>
    public const int DefaultLimit = 12;

    /// <summary>Whether a command is worth offering right now.</summary>
    public static bool Available(ShellCommand command, in CommandContext context)
    {
        ArgumentNullException.ThrowIfNull(command);

        CommandNeeds needs = command.Needs;

        if (needs.HasFlag(CommandNeeds.Selection) && !context.HasSelection) return false;
        if (needs.HasFlag(CommandNeeds.NotPlaying) && context.IsPlaying) return false;
        if (needs.HasFlag(CommandNeeds.Playing) && !context.IsPlaying) return false;
        if (needs.HasFlag(CommandNeeds.Session) && !context.HasSession) return false;
        if (needs.HasFlag(CommandNeeds.Project) && !context.HasProject) return false;
        if (needs.HasFlag(CommandNeeds.RibbonExpanded) && !context.RibbonExpanded) return false;
        if (needs.HasFlag(CommandNeeds.RibbonCollapsed) && context.RibbonExpanded) return false;

        // Offering Play on a scene with no character would be offering a verb
        // the engine answers by refusing.
        if (needs.HasFlag(CommandNeeds.CanPlay) && !context.CanPlay) return false;

        return true;
    }
}
