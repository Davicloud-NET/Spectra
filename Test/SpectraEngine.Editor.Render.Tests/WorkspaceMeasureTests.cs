using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using SpectraEngine.Editor.Shell;

namespace SpectraEngine.Editor.Render.Tests;

/// <summary>
/// <see cref="WorkspaceLayout"/>'s arithmetic against a real layout pass. The
/// model may promise less than the grid gives, never more.
/// </summary>
// Uses a plain grid with the shell's definitions, not MainWindow.
[Collection(RibbonSessionCollection.Name)]
public sealed class WorkspaceMeasureTests(RibbonSession session)
{
    private static readonly WorkspaceChrome Chrome = new(Vertical: 224, Horizontal: 32);

    // The model's horizontal chrome (32) includes the two gutter columns. The
    // grid has those itself, so only what is outside it is subtracted here:
    // the window margins, the tile edges and the bezel.
    private const double OutsideGrid = 32 - (2 * WorkspaceLayout.Gutter);

    [Theory]
    [InlineData(WorkspacePreset.Compact, 1180, 640)]
    [InlineData(WorkspacePreset.Compact, 1480, 920)]
    [InlineData(WorkspacePreset.Expanded, 1480, 920)]
    public void The_measured_viewport_cell_is_never_smaller_than_the_model_says(
        WorkspacePreset preset, double width, double height)
    {
        session.On(() =>
        {
            WorkspaceMetrics metrics = WorkspaceLayout.For(preset);
            (double modelWidth, double modelHeight) =
                WorkspaceLayout.ViewportCell(metrics, width, height, Chrome);

            (double measuredWidth, double measuredHeight) = Measure(metrics, width, height);

            measuredWidth.ShouldBeGreaterThanOrEqualTo(
                modelWidth, $"model said {modelWidth}, grid measured {measuredWidth}");
            measuredHeight.ShouldBeGreaterThanOrEqualTo(
                modelHeight, $"model said {modelHeight}, grid measured {measuredHeight}");
        });
    }

    [Fact]
    public void The_compact_preset_really_does_give_the_viewport_more_than_the_expanded_one()
    {
        session.On(() =>
        {
            (double compactWidth, double compactHeight) =
                Measure(WorkspaceLayout.For(WorkspacePreset.Compact), 1480, 920);
            (double expandedWidth, double expandedHeight) =
                Measure(WorkspaceLayout.For(WorkspacePreset.Expanded), 1480, 920);

            double compact = compactWidth * compactHeight;
            double expanded = expandedWidth * expandedHeight;

            compact.ShouldBeGreaterThan(expanded * 1.4);
        });
    }

    // Mirrors the editor grid's column and row definitions.
    private static (double Width, double Height) Measure(
        in WorkspaceMetrics metrics, double width, double height)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition(metrics.LeftWidth, GridUnitType.Pixel) { MinWidth = 180 });
        grid.ColumnDefinitions.Add(new ColumnDefinition(WorkspaceLayout.Gutter, GridUnitType.Pixel));
        grid.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star) { MinWidth = WorkspaceLayout.ViewportMinWidth });
        grid.ColumnDefinitions.Add(new ColumnDefinition(WorkspaceLayout.Gutter, GridUnitType.Pixel));
        grid.ColumnDefinitions.Add(new ColumnDefinition(metrics.RightWidth, GridUnitType.Pixel) { MinWidth = 220 });

        double drawer = metrics.DrawerOpen ? metrics.DrawerHeight : 0;
        grid.RowDefinitions.Add(new RowDefinition(1, GridUnitType.Star) { MinHeight = WorkspaceLayout.ViewportMinHeight });
        grid.RowDefinitions.Add(new RowDefinition(metrics.DrawerOpen ? WorkspaceLayout.Gutter : 0, GridUnitType.Pixel));
        grid.RowDefinitions.Add(new RowDefinition(drawer, GridUnitType.Pixel));

        var cell = new Border();
        Grid.SetColumn(cell, 2);
        Grid.SetRow(cell, 0);
        grid.Children.Add(cell);

        // Client area minus the chrome outside the grid: top row, ribbon,
        // header strip, status bar.
        var window = new Window
        {
            Content = grid,
            Width = width,
            Height = height - Chrome.Vertical + WorkspaceLayout.ViewportMinHeight,
        };

        window.Show();
        grid.Measure(new Size(width - OutsideGrid, height - Chrome.Vertical));
        grid.Arrange(new Rect(0, 0, width - OutsideGrid, height - Chrome.Vertical));
        Dispatcher.UIThread.RunJobs();

        (double Width, double Height) measured = (cell.Bounds.Width, cell.Bounds.Height);
        window.Close();
        return measured;
    }
}
