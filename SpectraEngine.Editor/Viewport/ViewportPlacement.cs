using System;

namespace SpectraEngine.Editor.Viewport;

/// <summary>Where the viewport pane lives in the shell's layout.</summary>
public enum ViewportPlacement
{
    /// <summary>A plain grid cell nothing may re-parent. The native child's placement.</summary>
    PinnedCell,

    /// <summary>A dock tool like every other panel. Only ever a composited pane.</summary>
    DockedTool,
}

/// <summary>What a placement permits.</summary>
/// <param name="CanPin">
/// Dock draws a pinned flyout in the main window's own layer, which a native
/// child composites over.
/// </param>
/// <param name="ToolsMayShareTheWindow">
/// Whether anything Avalonia draws in this window may cross the viewport's rectangle.
/// </param>
public readonly record struct ViewportPlacementRules(
    bool Docked,
    bool CanFloat,
    bool CanPin,
    bool ToolsMayShareTheWindow);

/// <summary>Maps the viewport decision to a placement and its rules.</summary>
public static class ViewportLayout
{
    /// <summary>Where a session's pane goes, given the viewport it chose.</summary>
    // Takes the decision, not a bool: docking a native child destroys its HWND
    // and the engine session with it.
    public static ViewportPlacement For(in ViewportDecision decision) =>
        decision.UseComposition ? ViewportPlacement.DockedTool : ViewportPlacement.PinnedCell;

    /// <summary>What a placement permits. Throws for a placement with no rules.</summary>
    public static ViewportPlacementRules RulesFor(ViewportPlacement placement) => placement switch
    {
        ViewportPlacement.PinnedCell => new ViewportPlacementRules(
            Docked: false, CanFloat: false, CanPin: false, ToolsMayShareTheWindow: false),

        ViewportPlacement.DockedTool => new ViewportPlacementRules(
            Docked: true, CanFloat: true, CanPin: true, ToolsMayShareTheWindow: true),

        _ => throw new ArgumentOutOfRangeException(nameof(placement), placement, "No rules for this placement."),
    };

    /// <summary>One sentence per placement, for the session's log line.</summary>
    public static string Describe(ViewportPlacement placement) => placement switch
    {
        ViewportPlacement.PinnedCell =>
            "the viewport is pinned in its own cell: a native child window cannot be re-parented without " +
            "destroying the HWND and the engine session behind it, and nothing Avalonia draws in this " +
            "window may cross it.",

        ViewportPlacement.DockedTool =>
            "the viewport is a dock tool: a composited pane has no window to destroy, so it docks, tabs " +
            "and floats like every other panel and the airspace rule no longer binds this window.",

        _ => throw new ArgumentOutOfRangeException(nameof(placement), placement, "No sentence for this placement."),
    };

    /// <summary>The viewport tool never closes, in either placement.</summary>
    // Nothing reopens it, so a closed viewport would be a running engine nobody can see.
    public const bool ViewportCanClose = false;
}
