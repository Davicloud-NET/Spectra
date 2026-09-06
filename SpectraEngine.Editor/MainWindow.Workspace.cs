using Avalonia.Controls;
using Microsoft.Extensions.Logging;
using SpectraEngine.Editor.Shell;
using SpectraEngine.Editor.Shell.Ribbon;
using System;

namespace SpectraEngine.Editor;

/// <summary>
/// How much of the window the viewport gets, and the two verbs that change it.
/// </summary>
/// <remarks>
/// <para>
/// <b>Measured: the viewport was 28% of the window.</b> 876x442 in a 1480x920
/// client. A 3D editor spending nearly three quarters of its screen on chrome
/// is the single most visible thing wrong with this shell, and it is entirely a
/// question of what the grid is told at startup.
/// </para>
/// <para>
/// <b>A partial rather than a fourteenth region of MainWindow.axaml.cs</b>,
/// which is already 3,500 lines. A partial class is not a second command path:
/// every verb here still arrives through <c>OnShellVerb</c>.
/// </para>
/// </remarks>
public partial class MainWindow
{
    private WorkspacePreset _preset = WorkspacePreset.Compact;
    private double _drawerHeight = WorkspaceLayout.DefaultDrawerHeight;
    private bool _drawerOpen;

    /// <summary>What a maximise has to put back.</summary>
    private readonly record struct GridSlot(GridLength Length, double Min);

    private sealed record WorkspaceBeforeMaximise(
        GridSlot Left, GridSlot Right, GridSlot Bottom, bool DrawerOpen, bool RibbonExpanded);

    private WorkspaceBeforeMaximise? _beforeMaximise;

    /// <summary>Writes a preset into the grid.</summary>
    private void ApplyWorkspace(WorkspacePreset preset)
    {
        // A preset applied under a maximise would be written into a grid whose
        // columns are all zero and then be invisible until the restore, which
        // would put the OLD widths back over it.
        if (_beforeMaximise is not null) RestoreWorkspace();

        _preset = preset;
        WorkspaceMetrics metrics = WorkspaceLayout.For(preset);

        EditorView.ColumnDefinitions[0].Width = new GridLength(metrics.LeftWidth);
        EditorView.ColumnDefinitions[4].Width = new GridLength(metrics.RightWidth);

        _drawerHeight = _settings.DrawerHeight;
        SetBottomDrawer(metrics.DrawerOpen);
        SetLevelsDocked(metrics.LevelsDocked);

        _shell.WorkspacePreset = preset;
    }

    /// <summary>Opens or closes the bottom region.</summary>
    /// <remarks>
    /// <b>Zero height and hidden, rather than removed.</b> The docks keep their
    /// contents and their layout, so opening the drawer again shows the same
    /// tabs in the same order; a removal would rebuild them and lose which one
    /// was in front.
    /// </remarks>
    private void SetBottomDrawer(bool open)
    {
        _drawerOpen = open;

        double available = EditorView.Bounds.Height;
        double height = available > 0
            ? WorkspaceLayout.ClampDrawerHeight(_drawerHeight, available)
            : _drawerHeight;

        if (open && height < _drawerHeight)
        {
            _logger.LogDebug(
                "Bottom drawer clamped from {Wanted} to {Actual}: the viewport row keeps its minimum",
                _drawerHeight, height);
        }

        EditorView.RowDefinitions[2].MinHeight = open ? 90 : 0;
        EditorView.RowDefinitions[2].Height = new GridLength(open ? height : 0);
        EditorView.RowDefinitions[1].Height = new GridLength(open ? 1 : 0);

        BottomDock.IsVisible = open;
        BottomSplitter.IsVisible = open;
        BottomSplitInk.IsVisible = open;

        _shell.IsDrawerOpen = open;
    }

    /// <summary>Gives the Levels tool its own dock, or takes it away.</summary>
    /// <remarks>
    /// <b>Removed rather than collapsed</b>, because an empty <c>ToolDock</c> at
    /// its declared proportion is an 8% strip of nothing above the scene tree.
    /// A project has one or two levels; the compact workspace names them on a
    /// chip beside the document instead.
    /// </remarks>
    private void SetLevelsDocked(bool docked)
    {
        if (docked == _levelsDocked) return;

        if (!docked)
        {
            // A floated tool is somewhere the user put it, and yanking it back
            // to remove it would be the layout deciding for them.
            if (!ReferenceEquals(MapsTool.Owner, MapsDock))
            {
                _logger.LogInformation(
                    "Levels is floated, so the compact workspace leaves it where it is");
                return;
            }

            _dockFactory.RemoveDockable(MapsDock, collapse: false);
            _dockFactory.RemoveDockable(MapsSplitter, collapse: false);
            SceneDock.Proportion = 1;
        }
        else
        {
            _dockFactory.InsertDockable(LeftLayout, MapsDock, 0);
            _dockFactory.InsertDockable(LeftLayout, MapsSplitter, 1);
            SceneDock.Proportion = 0.89;
        }

        _levelsDocked = docked;
    }

    private bool _levelsDocked = true;

    /// <summary>Shows a tool, opening the drawer first when it lives there.</summary>
    private void ShowToolInDrawer(Dock.Model.Avalonia.Controls.Tool tool)
    {
        if (!_drawerOpen) SetBottomDrawer(true);
        ShowTool(tool);
    }

    // --- Maximise ------------------------------------------------------------

    /// <summary>Gives the viewport the whole window.</summary>
    /// <remarks>
    /// <para>
    /// <b>The grid collapses; nothing is re-parented.</b> A native child viewport
    /// cannot be re-parented at all without destroying the HWND and the engine
    /// session with it, and a composited one would pay a compositor rebuild.
    /// Collapsing leaves all four docks exactly where they are, so the restore
    /// is arithmetic rather than reconstruction.
    /// </para>
    /// <para>
    /// <b>The one thing it cannot do is maximise a viewport somebody moved.</b>
    /// In a composited session the viewport is a dock tool and can be dragged
    /// into a side dock or floated; collapsing the columns around an empty
    /// centre cell would show nothing at all. That is refused in words.
    /// </para>
    /// </remarks>
    private void MaximiseViewport()
    {
        if (_beforeMaximise is not null) return;

        if (!ReferenceEquals(ViewportTool.Owner, ViewportDock) && CenterDock.IsVisible)
        {
            _shell.SetWarning(
                "The viewport is docked outside the centre; put it back there before maximising.");
            return;
        }

        _beforeMaximise = new WorkspaceBeforeMaximise(
            new GridSlot(EditorView.ColumnDefinitions[0].Width, EditorView.ColumnDefinitions[0].MinWidth),
            new GridSlot(EditorView.ColumnDefinitions[4].Width, EditorView.ColumnDefinitions[4].MinWidth),
            new GridSlot(EditorView.RowDefinitions[2].Height, EditorView.RowDefinitions[2].MinHeight),
            _drawerOpen,
            _ribbon.Expanded);

        _logger.LogInformation(
            "Viewport maximised; restoring to {Left} / {Right} columns",
            _beforeMaximise.Left.Length, _beforeMaximise.Right.Length);

        SetColumn(0, new GridLength(0), 0);
        SetColumn(4, new GridLength(0), 0);
        SetBottomDrawer(false);

        LeftDock.IsVisible = false;
        RightDock.IsVisible = false;
        LeftSplitter.IsVisible = false;
        RightSplitter.IsVisible = false;
        LeftSplitInk.IsVisible = false;
        RightSplitInk.IsVisible = false;

        // Transient, so the pin state on disk is untouched: F11 twice must leave
        // the ribbon exactly as it was found.
        _ribbon = RibbonSurface.SetExpanded(_ribbon, false);
        ApplyRibbonState();

        _shell.IsViewportMaximised = true;
    }

    /// <summary>Puts the panels back exactly as they were.</summary>
    private void RestoreWorkspace()
    {
        if (_beforeMaximise is not { } before) return;

        _beforeMaximise = null;

        SetColumn(0, before.Left.Length, before.Left.Min);
        SetColumn(4, before.Right.Length, before.Right.Min);

        LeftDock.IsVisible = true;
        RightDock.IsVisible = true;
        LeftSplitter.IsVisible = true;
        RightSplitter.IsVisible = true;
        LeftSplitInk.IsVisible = true;
        RightSplitInk.IsVisible = true;

        SetBottomDrawer(before.DrawerOpen);

        _ribbon = RibbonSurface.SetExpanded(_ribbon, before.RibbonExpanded);
        ApplyRibbonState();

        _shell.IsViewportMaximised = false;
    }

    private void SetColumn(int index, GridLength width, double min)
    {
        // Min first when shrinking, or the grid refuses the width and the
        // column stays where it was with nothing reporting why.
        EditorView.ColumnDefinitions[index].MinWidth = Math.Min(min, width.IsAbsolute ? width.Value : min);
        EditorView.ColumnDefinitions[index].Width = width;
        EditorView.ColumnDefinitions[index].MinWidth = min;
    }

    // The menu's own handlers. Each posts a SET verb through the one runner, so
    // the menu, the palette and the chords cannot diverge.

    private void OnUseCompactWorkspace(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        RunWorkspaceVerb(WorkspaceCommand.UseCompactWorkspace);

    private void OnUseExpandedWorkspace(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        RunWorkspaceVerb(WorkspaceCommand.UseExpandedWorkspace);

    private void OnToggleMaximiseViewport(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        RunWorkspaceVerb(_shell.IsViewportMaximised
            ? WorkspaceCommand.RestoreWorkspace
            : WorkspaceCommand.MaximiseViewport);

    private void OnToggleBottomDrawer(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        RunWorkspaceVerb(_shell.IsDrawerOpen
            ? WorkspaceCommand.CloseBottomDrawer
            : WorkspaceCommand.OpenBottomDrawer);

    private void OnToggleDiagnostics(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        RunWorkspaceVerb(_shell.ShowDiagnostics
            ? WorkspaceCommand.HideDiagnostics
            : WorkspaceCommand.ShowDiagnostics);

    /// <summary>Runs a workspace verb.</summary>
    private void RunWorkspaceVerb(WorkspaceCommand command)
    {
        switch (command)
        {
            case WorkspaceCommand.MaximiseViewport:
                MaximiseViewport();
                break;

            case WorkspaceCommand.RestoreWorkspace:
                RestoreWorkspace();
                break;

            case WorkspaceCommand.UseCompactWorkspace:
            case WorkspaceCommand.UseExpandedWorkspace:
                WorkspacePreset preset = command == WorkspaceCommand.UseExpandedWorkspace
                    ? WorkspacePreset.Expanded
                    : WorkspacePreset.Compact;

                ApplyWorkspace(preset);
                _settings.SetWorkspacePreset(preset);
                _settings.Save(_logger);
                break;

            case WorkspaceCommand.OpenBottomDrawer:
                SetBottomDrawer(true);
                break;

            case WorkspaceCommand.CloseBottomDrawer:
                SetBottomDrawer(false);
                break;

            case WorkspaceCommand.ShowDiagnostics:
            case WorkspaceCommand.HideDiagnostics:
                bool shown = command == WorkspaceCommand.ShowDiagnostics;
                _shell.ShowDiagnostics = shown;
                _settings.SetDiagnosticsReadouts(shown);
                _settings.Save(_logger);
                break;
        }
    }
}
