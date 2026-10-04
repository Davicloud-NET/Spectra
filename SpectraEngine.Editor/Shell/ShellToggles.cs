using SpectraEngine.Editing.Gizmos;

namespace SpectraEngine.Editor.Shell;

/// <summary>
/// Resolves a two-way choice: what "on" means, how the shell is told, and
/// which set verb lands there.
/// </summary>
public static class ShellToggles
{
    /// <summary>
    /// Whether the choice is in its non-default half: local axes, Classic
    /// handles, snapping on.
    /// </summary>
    public static bool IsOn(ShellToggle toggle, ShellModel shell)
    {
        ArgumentNullException.ThrowIfNull(shell);

        return toggle switch
        {
            ShellToggle.Axes => !shell.IsWorldSpace,
            ShellToggle.Handles => !shell.IsStudioStyle,
            ShellToggle.Snap => shell.SnapEnabled,
            _ => throw new ArgumentOutOfRangeException(nameof(toggle)),
        };
    }

    /// <summary>
    /// Shows the requested half at once. The caller still posts the verb; the
    /// model never talks to the engine.
    /// </summary>
    public static void Request(ShellModel shell, ShellToggle toggle, bool on)
    {
        ArgumentNullException.ThrowIfNull(shell);

        switch (toggle)
        {
            case ShellToggle.Axes:
                shell.RequestOrientation(on ? "local" : "world");
                break;

            case ShellToggle.Handles:
                shell.RequestGizmoStyle(on ? "Classic" : "Studio");
                break;

            case ShellToggle.Snap:
                shell.RequestSnapEnabled(on);
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(toggle));
        }
    }

    /// <summary>
    /// The set verb that lands on <paramref name="on"/>, not the one posted
    /// while already in that state.
    /// </summary>
    public static GizmoCommand CommandFor(ShellToggle toggle, bool on) => toggle switch
    {
        ShellToggle.Axes => on ? GizmoCommand.UseLocalOrientation : GizmoCommand.UseWorldOrientation,
        ShellToggle.Handles => on ? GizmoCommand.UseClassicStyle : GizmoCommand.UseStudioStyle,
        ShellToggle.Snap => on ? GizmoCommand.EnableSnap : GizmoCommand.DisableSnap,
        _ => throw new ArgumentOutOfRangeException(nameof(toggle)),
    };
}
