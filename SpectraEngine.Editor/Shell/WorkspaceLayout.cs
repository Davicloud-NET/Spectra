using System;

namespace SpectraEngine.Editor.Shell;

/// <summary>How the window is arranged.</summary>
public enum WorkspacePreset
{
    /// <summary>The viewport gets the room. The default.</summary>
    Compact,

    /// <summary>Every panel open at the width it wants.</summary>
    Expanded,
}

/// <summary>The numbers a preset writes into the editor grid.</summary>
/// <param name="LeftWidth">The scene column.</param>
/// <param name="RightWidth">The inspector column.</param>
/// <param name="DrawerHeight">The bottom row's height when it is open.</param>
/// <param name="DrawerOpen">Whether the bottom row starts open.</param>
/// <param name="LevelsDocked">Whether the Levels tool has a dock of its own.</param>
public readonly record struct WorkspaceMetrics(
    double LeftWidth,
    double RightWidth,
    double DrawerHeight,
    bool DrawerOpen,
    bool LevelsDocked);

/// <summary>What the viewport cell does not get, measured from a real window.</summary>
/// <param name="Vertical">Menu row, ribbon, header strip, status bar, bezel.</param>
/// <param name="Horizontal">Both splitters and the bezel, without the columns.</param>
public readonly record struct WorkspaceChrome(double Vertical, double Horizontal);

/// <summary>
/// How much of the window the viewport gets, and what the presets are.
/// </summary>
public static class WorkspaceLayout
{
    /// <summary>The viewport column's own minimum, which the grid enforces.</summary>
    public const double ViewportMinWidth = 360;

    /// <summary>The viewport row's minimum.</summary>
    public const double ViewportMinHeight = 200;

    /// <summary>How tall the bottom drawer opens by default.</summary>
    public const double DefaultDrawerHeight = 220;

    /// <summary>The numbers for a preset.</summary>
    public static WorkspaceMetrics For(WorkspacePreset preset) => preset switch
    {
        WorkspacePreset.Compact => new(250, 280, DefaultDrawerHeight, false, false),

        _ => new(288, 308, 236, true, true),
    };

    /// <summary>What the viewport cell measures under a preset.</summary>
    public static (double Width, double Height) ViewportCell(
        in WorkspaceMetrics metrics, double windowWidth, double windowHeight, in WorkspaceChrome chrome)
    {
        double width = windowWidth - metrics.LeftWidth - metrics.RightWidth - chrome.Horizontal;
        double drawer = metrics.DrawerOpen ? metrics.DrawerHeight + 1 : 0;
        double height = windowHeight - chrome.Vertical - drawer;

        return (Math.Max(width, 0), Math.Max(height, 0));
    }

    /// <summary>
    /// The drawer height that leaves the viewport its minimum.
    /// </summary>
    public static double ClampDrawerHeight(double wanted, double rowsAvailable) =>
        Math.Max(0, Math.Min(wanted, rowsAvailable - ViewportMinHeight - 1));

    /// <summary>The preset's name, for settings and for the menu.</summary>
    public static string NameOf(WorkspacePreset preset) =>
        preset == WorkspacePreset.Expanded ? "expanded" : "compact";

    /// <summary>
    /// Reads a preset name. An unknown word leaves <paramref name="preset"/>
    /// at compact and returns false.
    /// </summary>
    public static bool TryParse(string? name, out WorkspacePreset preset)
    {
        preset = WorkspacePreset.Compact;

        if (string.Equals(name, "expanded", StringComparison.OrdinalIgnoreCase))
        {
            preset = WorkspacePreset.Expanded;
            return true;
        }

        return string.Equals(name, "compact", StringComparison.OrdinalIgnoreCase);
    }
}
