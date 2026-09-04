using SpectraEngine.Editing.Gizmos;

namespace SpectraEngine.Editor.Shell.Ribbon;

/// <summary>
/// The one place a two-way choice is resolved: what "on" means, how the shell
/// is told, and which idempotent verb lands there.
/// </summary>
/// <remarks>
/// <para>
/// <b>This replaces a tuple that was expressed twice and read once.</b>
/// <c>RibbonLayout.CommandsFor</c> returned <c>(WhenOff, WhenOn)</c> and said
/// of itself that "the dispatcher and the tests read the same pairing" - but
/// the dispatcher read nothing of the sort: <c>OnOrientationClicked</c>,
/// <c>OnStyleClicked</c> and <c>OnSnapClicked</c> each recomputed the pair
/// inline, so the pairing existed twice and the table's only caller was a test.
/// </para>
/// <para>
/// <b>And the tuple was ambiguous in a way nothing could catch.</b> Its own doc
/// read it as "the verb it posts when the choice is currently OFF", which for
/// <c>Axes</c> meant posting <c>UseWorldOrientation</c> while already in world -
/// a no-op. It only parses as "the verb that LANDS on off", and the test
/// asserted the pair was distinct and defined rather than which way round it
/// went, so an inversion would have shipped as three controls that did nothing
/// on their first click. A pair distinguished only by position is what produced
/// that, so the direction is a named parameter here.
/// </para>
/// </remarks>
public static class RibbonToggles
{
    /// <summary>
    /// Whether the choice is in its NON-DEFAULT half: local axes, Classic
    /// handles, snapping on.
    /// </summary>
    public static bool IsOn(RibbonToggle toggle, ShellModel shell)
    {
        ArgumentNullException.ThrowIfNull(shell);

        return toggle switch
        {
            RibbonToggle.Axes => !shell.IsWorldSpace,
            RibbonToggle.Handles => !shell.IsStudioStyle,
            RibbonToggle.Snap => shell.SnapEnabled,
            _ => throw new ArgumentOutOfRangeException(nameof(toggle)),
        };
    }

    /// <summary>
    /// Shows the requested half at once. The caller still posts the verb: the
    /// model never talks to the engine, exactly as every other request does.
    /// </summary>
    public static void Request(ShellModel shell, RibbonToggle toggle, bool on)
    {
        ArgumentNullException.ThrowIfNull(shell);

        switch (toggle)
        {
            case RibbonToggle.Axes:
                shell.RequestOrientation(on ? "local" : "world");
                break;

            case RibbonToggle.Handles:
                shell.RequestGizmoStyle(on ? "Classic" : "Studio");
                break;

            case RibbonToggle.Snap:
                shell.RequestSnapEnabled(on);
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(toggle));
        }
    }

    /// <summary>
    /// The idempotent set-verb that LANDS on <paramref name="on"/> - never the
    /// verb posted while in that state, which would be a no-op.
    /// </summary>
    public static GizmoCommand CommandFor(RibbonToggle toggle, bool on) => toggle switch
    {
        RibbonToggle.Axes => on ? GizmoCommand.UseLocalOrientation : GizmoCommand.UseWorldOrientation,
        RibbonToggle.Handles => on ? GizmoCommand.UseClassicStyle : GizmoCommand.UseStudioStyle,
        RibbonToggle.Snap => on ? GizmoCommand.EnableSnap : GizmoCommand.DisableSnap,
        _ => throw new ArgumentOutOfRangeException(nameof(toggle)),
    };
}
