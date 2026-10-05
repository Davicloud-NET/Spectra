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

/// <summary>What the shell can currently do, for deciding what to offer.</summary>
public readonly record struct CommandContext(
    bool HasSelection,
    bool IsPlaying,
    bool HasSession,
    bool HasProject,
    bool RibbonExpanded,
    bool CanPlay);

/// <summary>One row of the palette.</summary>
/// <param name="Title">What the palette shows and what the query is matched against.</param>
/// <param name="Needs">What has to be true for it to be offered.</param>
/// <param name="Gesture">The chord that also reaches it, printed beside it, or empty.</param>
/// <param name="Aliases">Other words that should find it.</param>
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

/// <summary>What a search found, and how much of it is being shown.</summary>
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
/// Every verb the palette can reach, by name. Rows dispatch through the same
/// <see cref="ShellVerb"/> handler the ribbon uses.
/// </summary>
// Hand-written, no enum reflection: trimming removes it in a published build.
// A test holds that every ribbon verb is in here.
public static class CommandTable
{
    /// <summary>Every row, in no particular order: the score decides what is shown.</summary>
    public static IReadOnlyList<ShellCommand> Commands { get; } =
    [
        new("Insert block", ShellVerb.Of(InsertKind.WorldBrush), CommandNeeds.NotPlaying, "Ctrl+1"),
        new("Insert part", ShellVerb.Of(InsertKind.PartBrush), CommandNeeds.NotPlaying, "Ctrl+2"),
        new("Insert cut", ShellVerb.Of(InsertKind.SubtractiveBrush), CommandNeeds.NotPlaying, "Ctrl+3"),
        new("Insert light", ShellVerb.Of(InsertKind.PointLight), CommandNeeds.NotPlaying, "Ctrl+4"),
        new("Insert surface light panel", ShellVerb.Of(InsertKind.SurfaceLight), CommandNeeds.NotPlaying),
        new("Insert empty group", ShellVerb.Of(InsertKind.Group), CommandNeeds.NotPlaying),
        new("Insert entity", ShellVerb.InsertEntity(), CommandNeeds.NotPlaying),

        new("Move tool", ShellVerb.Of(GizmoCommand.UseTranslate), CommandNeeds.None, "W"),
        new("Rotate tool", ShellVerb.Of(GizmoCommand.UseRotate), CommandNeeds.None, "E"),
        new("Size tool", ShellVerb.Of(GizmoCommand.UseScale), CommandNeeds.None, "R", ["scale", "resize"]),
        new("Drag axes: world or local", ShellVerb.Of(ShellToggle.Axes), CommandNeeds.None, "X"),
        new("Handles: Studio or Classic", ShellVerb.Of(ShellToggle.Handles), CommandNeeds.None, "Y"),

        new("Snap to grid", ShellVerb.Of(ShellToggle.Snap), CommandNeeds.None, "G"),
        new("Finer grid", ShellVerb.Of(GizmoCommand.FinerSnap), CommandNeeds.None, "["),
        new("Coarser grid", ShellVerb.Of(GizmoCommand.CoarserSnap), CommandNeeds.None, "]"),

        new("Duplicate", ShellVerb.Of(EditorHostCommand.Duplicate), CommandNeeds.Selection | CommandNeeds.NotPlaying, "Ctrl+D"),
        new("Delete", ShellVerb.Of(EditorHostCommand.Delete), CommandNeeds.Selection | CommandNeeds.NotPlaying, "Del"),
        new("Convert block or part", ShellVerb.Of(EditorHostCommand.ToggleBrushKind), CommandNeeds.Selection | CommandNeeds.NotPlaying, "Ctrl+T"),
        new("Group", ShellVerb.Of(EditorHostCommand.Group), CommandNeeds.Selection | CommandNeeds.NotPlaying, "Ctrl+G"),
        new("Ungroup", ShellVerb.Of(EditorHostCommand.Ungroup), CommandNeeds.Selection | CommandNeeds.NotPlaying, "Ctrl+Shift+G"),

        new("Undo", ShellVerb.Of(EditorHostCommand.Undo), CommandNeeds.NotPlaying, "Ctrl+Z"),
        new("Redo", ShellVerb.Of(EditorHostCommand.Redo), CommandNeeds.NotPlaying, "Ctrl+Y"),
        new("Select all", ShellVerb.Of(EditorHostCommand.SelectAll), CommandNeeds.None, "Ctrl+A"),
        new("Deselect all", ShellVerb.Of(EditorHostCommand.ClearSelection), CommandNeeds.Selection, "Esc", ["clear selection"]),

        new("Frame the selection", ShellVerb.Of(EditorCameraCommand.FrameSelection), CommandNeeds.Selection, "F", ["focus"]),
        new("Frame everything", ShellVerb.Of(EditorCameraCommand.FrameAll), CommandNeeds.None, "Shift+F"),

        // The views are here too because a laptop has no keypad.
        new("View: perspective", ShellVerb.Of(EditorCameraCommand.ViewPerspective), CommandNeeds.Session, "Numpad 5", ["camera"]),
        new("View: top", ShellVerb.Of(EditorCameraCommand.ViewTop), CommandNeeds.Session, "Numpad 7", ["plan", "orthographic"]),
        new("View: bottom", ShellVerb.Of(EditorCameraCommand.ViewBottom), CommandNeeds.Session, "Ctrl+Numpad 7", ["orthographic"]),
        new("View: front", ShellVerb.Of(EditorCameraCommand.ViewFront), CommandNeeds.Session, "Numpad 1", ["elevation", "orthographic"]),
        new("View: back", ShellVerb.Of(EditorCameraCommand.ViewBack), CommandNeeds.Session, "Ctrl+Numpad 1", ["orthographic"]),
        new("View: right", ShellVerb.Of(EditorCameraCommand.ViewRight), CommandNeeds.Session, "Numpad 3", ["side", "orthographic"]),
        new("View: left", ShellVerb.Of(EditorCameraCommand.ViewLeft), CommandNeeds.Session, "Ctrl+Numpad 3", ["side", "orthographic"]),
        new("Camera: editor or fly", ShellVerb.Of(EditorHostCommand.ToggleNavigation), CommandNeeds.None, "F7"),
        new("Ground grid: during move and resize", ShellVerb.Of(EditorHostCommand.GridAuto)),
        new("Ground grid: always", ShellVerb.Of(EditorHostCommand.GridOn)),
        new("Ground grid: off", ShellVerb.Of(EditorHostCommand.GridOff), CommandNeeds.None, "", ["hide grid"]),

        new("Overlay: wireframe", ShellVerb.Of(DebugVisualization.Wireframe), CommandNeeds.None, "F1", ["wireframe"]),
        new("Overlay: CSG vertices", ShellVerb.Of(DebugVisualization.Vertices), CommandNeeds.None, "F2"),
        new("Overlay: bounds", ShellVerb.Of(DebugVisualization.Aabbs), CommandNeeds.None, "F3"),
        new("Overlay: face normals", ShellVerb.Of(DebugVisualization.Normals), CommandNeeds.None, "F4"),
        new("Overlay: node axes", ShellVerb.Of(DebugVisualization.SceneGraph), CommandNeeds.None, "F5"),

        new("New project...", ShellVerb.Of(DocumentVerb.NewProject)),
        new("Open project...", ShellVerb.Of(DocumentVerb.OpenProject)),
        new("Close project", ShellVerb.Of(DocumentVerb.CloseProject), CommandNeeds.Session),
        new("New level", ShellVerb.Of(DocumentVerb.NewLevel), CommandNeeds.Session, "Ctrl+N"),
        new("Open level...", ShellVerb.Of(DocumentVerb.OpenLevel), CommandNeeds.Session, "Ctrl+O"),
        // Not while playing: the save would hold what the run has moved.
        new("Save level", ShellVerb.Of(DocumentVerb.Save), CommandNeeds.Session | CommandNeeds.NotPlaying, "Ctrl+S"),
        new("Save level as...", ShellVerb.Of(DocumentVerb.SaveAs), CommandNeeds.Session | CommandNeeds.NotPlaying, "Ctrl+Shift+S"),
        new("Validate cooked content", ShellVerb.Of(DocumentVerb.ValidateCooked), CommandNeeds.Project),
        new("Exit", ShellVerb.Of(DocumentVerb.Exit), CommandNeeds.None, "", ["quit", "close editor"]),

        new("Play", ShellVerb.Of(PlayVerb.Play),
            CommandNeeds.Session | CommandNeeds.NotPlaying | CommandNeeds.CanPlay, "F8", ["run", "test"]),
        new("Stop", ShellVerb.Of(PlayVerb.Stop), CommandNeeds.Session | CommandNeeds.Playing, "F8"),

        new("Show Scene panel", ShellVerb.Of(PanelId.Scene), CommandNeeds.Session, "", ["tree", "outliner"]),
        new("Show Levels panel", ShellVerb.Of(PanelId.Levels), CommandNeeds.Session, "", ["maps"]),
        new("Show Properties panel", ShellVerb.Of(PanelId.Properties), CommandNeeds.Session, "", ["inspector"]),
        new("Show Content panel", ShellVerb.Of(PanelId.Content), CommandNeeds.Session, "", ["assets", "browser"]),
        new("Show Output panel", ShellVerb.Of(PanelId.Output), CommandNeeds.Session, "", ["log"]),
        new("Show Problems panel", ShellVerb.Of(PanelId.Problems), CommandNeeds.Session, "", ["errors", "warnings"]),
        new("Show Console panel", ShellVerb.Of(PanelId.Console), CommandNeeds.Session, "`"),
        new("Keyboard reference", ShellVerb.Of(PanelId.KeyboardReference), CommandNeeds.None, "", ["shortcuts", "keys"]),

        new("Collapse the ribbon", ShellVerb.Of(RibbonVerb.Collapse),
            CommandNeeds.Session | CommandNeeds.RibbonExpanded, "Ctrl+F1"),
        new("Expand the ribbon", ShellVerb.Of(RibbonVerb.Expand),
            CommandNeeds.Session | CommandNeeds.RibbonCollapsed, "Ctrl+F1"),

        new("Maximise viewport", ShellVerb.Of(WorkspaceCommand.MaximiseViewport),
            CommandNeeds.Session, "F11", ["fullscreen", "full screen"]),
        new("Restore workspace", ShellVerb.Of(WorkspaceCommand.RestoreWorkspace),
            CommandNeeds.Session, "F11", ["unmaximise", "show panels"]),
        new("Workspace: compact", ShellVerb.Of(WorkspaceCommand.UseCompactWorkspace),
            CommandNeeds.Session),
        new("Workspace: expanded", ShellVerb.Of(WorkspaceCommand.UseExpandedWorkspace),
            CommandNeeds.Session),
        new("Bottom panel: open", ShellVerb.Of(WorkspaceCommand.OpenBottomDrawer),
            CommandNeeds.Session, "Ctrl+`"),
        new("Bottom panel: close", ShellVerb.Of(WorkspaceCommand.CloseBottomDrawer),
            CommandNeeds.Session),
        new("Diagnostics readouts: show", ShellVerb.Of(WorkspaceCommand.ShowDiagnostics),
            CommandNeeds.Session, "", ["counters", "fps"]),
        new("Diagnostics readouts: hide", ShellVerb.Of(WorkspaceCommand.HideDiagnostics),
            CommandNeeds.Session),
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

            // -1 so an alias never outranks the same match on a title.
            foreach (string alias in command.Aliases)
            {
                int aliasScore = CommandScore.Of(alias, query);
                if (aliasScore != CommandScore.NoMatch && aliasScore - 1 > score)
                    score = aliasScore - 1;
            }

            if (score == CommandScore.NoMatch) continue;
            matches.Add((command, score));
        }

        // Title breaks ties so the same query always gives the same order.
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

        if (needs.HasFlag(CommandNeeds.CanPlay) && !context.CanPlay) return false;

        return true;
    }
}
