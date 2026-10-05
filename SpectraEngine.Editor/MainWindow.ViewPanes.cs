using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Microsoft.Extensions.Logging;
using SpectraEngine.Editor.Shell;
using SpectraEngine.Editor.Viewport;
using System;

namespace SpectraEngine.Editor;

// The view panes in the centre of the window: the 3D view and the Logic view.
// An arrangement is track lengths, cells and visibility. Nothing here adds,
// removes or moves a child: re-parenting a native viewport destroys its session.
public partial class MainWindow
{
    private ViewArrangement _viewArrangement = ViewArrangement.Single;

    private void ApplyViewArrangement(ViewArrangement arrangement)
    {
        ViewPaneGrid grid = ViewPaneLayout.Arrange(
            arrangement, _settings.ViewColumnSplit, _settings.ViewRowSplit);

        ViewPaneAxis columns = grid.Columns;
        ViewPaneAxis rows = grid.Rows;

        // The shares go in as star weights: the panes keep their proportion as
        // the window changes size, and the minimums do the clamping. An axis
        // with one pane takes no minimum here. The cell's own holds it, and a
        // docked pane has less room than the cell.
        SetPaneColumn(0, Weight(columns.First), columns.IsSplit ? columns.FirstMin : 0);
        SetPaneColumn(1, new GridLength(columns.Gutter), 0);
        SetPaneColumn(2, Weight(columns.Second), columns.SecondMin);

        SetPaneRow(0, Weight(rows.First), rows.IsSplit ? rows.FirstMin : 0);
        SetPaneRow(1, new GridLength(rows.Gutter), 0);
        SetPaneRow(2, Weight(rows.Second), rows.SecondMin);

        Grid.SetColumn(ViewportPane, grid.View.Column);
        Grid.SetRow(ViewportPane, grid.View.Row);
        Grid.SetColumn(LogicPane, grid.Logic.Column);
        Grid.SetRow(LogicPane, grid.Logic.Row);

        LogicPane.IsVisible = grid.ShowsLogic;
        ViewColumnSplitter.IsVisible = columns.IsSplit;
        ViewColumnSplitInk.IsVisible = columns.IsSplit;
        ViewRowSplitter.IsVisible = rows.IsSplit;
        ViewRowSplitInk.IsVisible = rows.IsSplit;

        // A floated pane grid has no cell minimum behind it.
        ViewPanes.MinWidth = columns.IsSplit ? columns.Min : 0;
        ViewPanes.MinHeight = rows.IsSplit ? rows.Min : 0;

        Size chrome = CenterChrome();
        EditorView.ColumnDefinitions[2].MinWidth = columns.Min + (columns.IsSplit ? chrome.Width : 0);
        EditorView.RowDefinitions[0].MinHeight = rows.Min + (rows.IsSplit ? chrome.Height : 0);
        ReclampDrawer();

        _viewArrangement = arrangement;
        _shell.ViewArrangement = arrangement;
        SendLogicRequest();
    }

    private static GridLength Weight(double share) =>
        share > 0 ? new GridLength(share, GridUnitType.Star) : new GridLength(0);

    // The minimum goes down before the length and up after it, as in SetColumn.
    private void SetPaneColumn(int index, GridLength width, double min)
    {
        ColumnDefinition column = ViewPanes.ColumnDefinitions[index];
        column.MinWidth = Math.Min(min, column.MinWidth);
        column.Width = width;
        column.MinWidth = min;
    }

    private void SetPaneRow(int index, GridLength height, double min)
    {
        RowDefinition row = ViewPanes.RowDefinitions[index];
        row.MinHeight = Math.Min(min, row.MinHeight);
        row.Height = height;
        row.MinHeight = min;
    }

    private bool PanesAreInTheCenter =>
        _placement == ViewportPlacement.PinnedCell || ReferenceEquals(ViewportTool.Owner, ViewportDock);

    // What the centre cell holds besides the panes: the dock's header and edge
    // when they are a tool in the centre dock. Nothing when they are pinned in
    // the cell, and nothing that can be measured before the first layout.
    private Size CenterChrome()
    {
        Rect panes = ViewPanes.Bounds;
        if (_placement != ViewportPlacement.DockedTool || !PanesAreInTheCenter || panes.Height <= 0)
            return default;

        return new Size(
            Math.Max(0, EditorView.ColumnDefinitions[2].ActualWidth - panes.Width),
            Math.Max(0, EditorView.RowDefinitions[0].ActualHeight - panes.Height));
    }

    // What an open drawer can give up before it reaches its own minimum. Only
    // the centre cell shares its height with the drawer.
    private double DrawerSlack()
    {
        if (!_drawerOpen || !PanesAreInTheCenter)
            return 0;

        RowDefinition drawer = EditorView.RowDefinitions[2];
        return Math.Max(0, drawer.Height.Value - drawer.MinHeight);
    }

    // Lowers an open drawer until the centre row has its minimum. The row's
    // own height is read, not the saved one: a drag may have changed it.
    private void ReclampDrawer()
    {
        double available = EditorView.Bounds.Height;
        if (!_drawerOpen || available <= 0)
            return;

        RowDefinition drawer = EditorView.RowDefinitions[2];
        double wanted = drawer.Height.Value;
        double height = WorkspaceLayout.ClampDrawerHeight(
            wanted, available, EditorView.RowDefinitions[0].MinHeight);

        if (height < wanted)
            drawer.Height = new GridLength(Math.Max(height, drawer.MinHeight));
    }

    // Measured against the panes as they lie now. Before the first layout
    // there is nothing to measure, and the saved arrangement is taken as it is.
    private bool ViewArrangementFits(ViewArrangement arrangement)
    {
        Rect panes = ViewPanes.Bounds;
        if (panes.Width <= 0 || panes.Height <= 0)
            return true;

        return ViewPaneLayout.Fits(arrangement, panes.Width, panes.Height + DrawerSlack());
    }

    private void RequestViewArrangement(ViewArrangement arrangement)
    {
        if (arrangement == _viewArrangement)
            return;

        if (!ViewArrangementFits(arrangement))
        {
            _shell.SetWarning(arrangement == ViewArrangement.LogicBeside
                ? "There is no room for the Logic view beside the viewport. Make the window wider or a side panel narrower."
                : "There is no room for the Logic view below the viewport. Make the window taller.");

            // The menu item that was clicked checked itself, and its binding
            // only hears a change. Say the refused arrangement, then take it back.
            _shell.ViewArrangement = arrangement;
            _shell.ViewArrangement = _viewArrangement;
            return;
        }

        ApplyViewArrangement(arrangement);

        _settings.SetViewArrangement(arrangement);
        _settings.Save(_logger);

        _logger.LogInformation("View panes: {Arrangement}", ViewPaneLayout.NameOf(arrangement));
    }

    // The set verb Ctrl+L stands for right now: hide a Logic view that shows,
    // else show it in the split last used.
    private WorkspaceCommand LogicViewFlipVerb() =>
        ViewPaneLayout.Flip(_viewArrangement, _settings.LastViewSplit) switch
        {
            ViewArrangement.LogicBelow => WorkspaceCommand.ShowLogicBelow,
            ViewArrangement.LogicBeside => WorkspaceCommand.ShowLogicBeside,
            _ => WorkspaceCommand.HideLogic,
        };

    private void OnShowLogicBelow(object? sender, RoutedEventArgs e) =>
        RunWorkspaceVerb(WorkspaceCommand.ShowLogicBelow);

    private void OnShowLogicBeside(object? sender, RoutedEventArgs e) =>
        RunWorkspaceVerb(WorkspaceCommand.ShowLogicBeside);

    private void OnHideLogic(object? sender, RoutedEventArgs e) =>
        RunWorkspaceVerb(WorkspaceCommand.HideLogic);

    // A drag leaves both pane tracks as star weights of their pixel sizes.
    private void OnViewSplitterDragCompleted(object? sender, VectorEventArgs e)
    {
        OnSplitterDragCompleted(sender, e);

        if (ReferenceEquals(sender, ViewColumnSplitter))
        {
            ColumnDefinitions columns = ViewPanes.ColumnDefinitions;
            if (!ViewPaneLayout.TryReadSplit(columns[0].Width.Value, columns[2].Width.Value, out double split))
                return;

            _settings.SetViewColumnSplit(split);
        }
        else
        {
            RowDefinitions rows = ViewPanes.RowDefinitions;
            if (!ViewPaneLayout.TryReadSplit(rows[0].Height.Value, rows[2].Height.Value, out double split))
                return;

            _settings.SetViewRowSplit(split);
        }

        _settings.Save(_logger);
    }
}
