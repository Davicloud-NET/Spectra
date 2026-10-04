using Avalonia.Controls;
using Microsoft.Extensions.Logging;
using SpectraEngine.Editor.Shell;
using SpectraEngine.Editor.Shell.Ribbon;
using System;

namespace SpectraEngine.Editor;

// Workspace layout: presets, the bottom drawer, viewport maximise.
public partial class MainWindow
{
    private WorkspacePreset _preset = WorkspacePreset.Compact;
    private double _drawerHeight = WorkspaceLayout.DefaultDrawerHeight;
    private bool _drawerOpen;

    private readonly record struct GridSlot(GridLength Length, double Min);

    private sealed record WorkspaceBeforeMaximise(
        GridSlot Left, GridSlot Right, GridSlot Bottom, bool DrawerOpen, bool RibbonExpanded);

    private WorkspaceBeforeMaximise? _beforeMaximise;

    private void ApplyWorkspace(WorkspacePreset preset)
    {
        // Restore first, or the later restore writes the old widths over the preset.
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

    // Zero height and hidden, not removed, so the tabs and their order survive.
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

    // Removed, not collapsed: an empty ToolDock still takes its proportion
    // as a blank strip above the scene tree.
    private void SetLevelsDocked(bool docked)
    {
        if (docked == _levelsDocked) return;

        if (!docked)
        {
            // Leave a floated Levels tool where the user put it.
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

    private void ShowToolInDrawer(Dock.Model.Avalonia.Controls.Tool tool)
    {
        if (!_drawerOpen) SetBottomDrawer(true);
        ShowTool(tool);
    }

    // Collapses the grid around the viewport; nothing is re-parented.
    // Re-parenting a native child viewport destroys its HWND and the session.
    // Refused when a composited viewport has been docked outside the centre.
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

        // Not saved: the pin state on disk stays as it was.
        _ribbon = RibbonSurface.SetExpanded(_ribbon, false);
        ApplyRibbonState();

        _shell.IsViewportMaximised = true;
    }

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
        // Lower the min first when shrinking, or the grid refuses the width.
        EditorView.ColumnDefinitions[index].MinWidth = Math.Min(min, width.IsAbsolute ? width.Value : min);
        EditorView.ColumnDefinitions[index].Width = width;
        EditorView.ColumnDefinitions[index].MinWidth = min;
    }

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
