using System;

namespace SpectraEngine.Editor.Shell.Ribbon;

/// <summary>Where the active tab's body is showing, if anywhere.</summary>
public enum RibbonBodyHost
{
    /// <summary>Nowhere: the ribbon is collapsed to its tab strip.</summary>
    None,

    /// <summary>In the window, under the tab strip, taking layout space.</summary>
    Inline,

    /// <summary>
    /// In the flyout, over whatever is below. A popup is its own OS window on
    /// Windows, so it can cross a native viewport.
    /// </summary>
    Flyout,
}

/// <summary>The ribbon's collapse state.</summary>
/// <param name="Expanded">Pinned open. Persisted; see <see cref="EditorSettings"/>.</param>
/// <param name="ActiveTabId">Which page the strip is pointing at. Not persisted.</param>
/// <param name="FlyoutOpen">
/// A collapsed ribbon showing one page temporarily. Never true while
/// <paramref name="Expanded"/> is.
/// </param>
public readonly record struct RibbonSurfaceState(bool Expanded, string ActiveTabId, bool FlyoutOpen);

/// <summary>
/// The collapse state machine. Pure: no control, no window, no settings.
/// </summary>
public static class RibbonSurface
{
    /// <summary>The state a session starts in, on the default tab.</summary>
    public static RibbonSurfaceState Create(bool expanded) =>
        new(expanded, RibbonLayout.DefaultTabId, FlyoutOpen: false);

    /// <summary>Where the active tab's body belongs right now.</summary>
    public static RibbonBodyHost HostFor(in RibbonSurfaceState state)
    {
        if (state.Expanded)
            return RibbonBodyHost.Inline;

        return state.FlyoutOpen ? RibbonBodyHost.Flyout : RibbonBodyHost.None;
    }

    /// <summary>
    /// A tab was clicked. Expanded, it switches page. Collapsed, it flies the
    /// page out, or puts it away if that page is already out. An unknown id
    /// changes nothing.
    /// </summary>
    public static RibbonSurfaceState SelectTab(in RibbonSurfaceState state, string? tabId)
    {
        if (RibbonLayout.FindTab(tabId) is not { } tab)
            return state;

        if (state.Expanded)
            return state with { ActiveTabId = tab.Id, FlyoutOpen = false };

        bool sameTabAlreadyOut = state.FlyoutOpen
            && string.Equals(state.ActiveTabId, tab.Id, StringComparison.Ordinal);

        return state with { ActiveTabId = tab.Id, FlyoutOpen = !sameTabAlreadyOut };
    }

    /// <summary>
    /// Pins the ribbon open, or collapses it to the strip. Closes the flyout
    /// either way.
    /// </summary>
    public static RibbonSurfaceState SetExpanded(in RibbonSurfaceState state, bool expanded) =>
        state with { Expanded = expanded, FlyoutOpen = false };

    /// <summary>
    /// A ribbon control was invoked. Closes the flyout, which would otherwise
    /// stay over the viewport the edit landed in.
    /// </summary>
    public static RibbonSurfaceState Invoke(in RibbonSurfaceState state) =>
        state.FlyoutOpen ? state with { FlyoutOpen = false } : state;

    /// <summary>The flyout was dismissed from outside: a click away, a closing session.</summary>
    public static RibbonSurfaceState Dismiss(in RibbonSurfaceState state) =>
        state.FlyoutOpen ? state with { FlyoutOpen = false } : state;
}
