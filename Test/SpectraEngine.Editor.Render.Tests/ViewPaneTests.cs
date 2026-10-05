using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using SpectraEngine.Editor.Shell;

namespace SpectraEngine.Editor.Render.Tests;

/// <summary>
/// The view panes against a real layout pass: the Logic header's size, the
/// viewport header's fit beside it, and <see cref="ViewPaneLayout"/>'s
/// arithmetic.
/// </summary>
// Needs real Skia: the headless stub typeface gives every glyph the same advance.
[Collection(RibbonSessionCollection.Name)]
public sealed class ViewPaneTests(RibbonSession session)
{
    private static readonly WorkspaceChrome Chrome = new(Vertical: 224, Horizontal: 32);

    [Fact]
    public void The_Logic_header_is_as_tall_as_the_viewport_header()
    {
        session.On(() =>
        {
            (LogicHeaderStrip strip, Window window) = OpenLogicHeader();

            try
            {
                // The two sit side by side in one arrangement.
                strip.DesiredSize.Height.ShouldBe(28);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void The_Logic_header_fits_the_narrowest_Logic_pane()
    {
        session.On(() =>
        {
            (LogicHeaderStrip strip, Window window) = OpenLogicHeader();

            try
            {
                strip.DesiredSize.Width.ShouldBeLessThanOrEqualTo(
                    ViewPaneLayout.LogicMinWidth,
                    $"the header wants {strip.DesiredSize.Width:0.#}px");
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Theory]
    [InlineData(WorkspacePreset.Compact)]
    [InlineData(WorkspacePreset.Expanded)]
    public void The_viewport_header_fits_beside_the_Logic_view_at_the_default_window(WorkspacePreset preset)
    {
        session.On(() =>
        {
            (double width, double height) = WorkspaceLayout.ViewportCell(
                WorkspaceLayout.For(preset), 1480, 920, Chrome);

            ViewPaneGrid grid = ViewPaneLayout.Arrange(
                ViewArrangement.LogicBeside,
                ViewPaneLayout.DefaultColumnSplit,
                ViewPaneLayout.DefaultRowSplit,
                width,
                height);

            (ViewportHeaderStrip strip, Window window) = HeaderStripWidthTests.Open(HeaderStripWidthTests.AtRest());

            try
            {
                strip.DesiredSize.Width.ShouldBeLessThanOrEqualTo(
                    grid.Columns.First,
                    $"the strip wants {strip.DesiredSize.Width:0.#}px and the 3D pane is " +
                    $"{grid.Columns.First:0.#}px of {width:0.#}px in the {preset} workspace");
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Theory]
    [InlineData(ViewArrangement.Single, 0.6, 0.45, 900, 600)]
    [InlineData(ViewArrangement.LogicBelow, 0.6, 0.45, 900, 600)]
    [InlineData(ViewArrangement.LogicBeside, 0.6, 0.45, 900, 600)]

    // A minimum bites: the 3D view's, then the Logic view's.
    [InlineData(ViewArrangement.LogicBelow, 0.6, 0.1, 900, 600)]
    [InlineData(ViewArrangement.LogicBeside, 0.95, 0.45, 900, 600)]
    [InlineData(ViewArrangement.LogicBelow, 0.6, 0.45, 900, 297)]
    public void The_grid_comes_to_what_the_layout_says(
        ViewArrangement arrangement, double columnSplit, double rowSplit, double width, double height)
    {
        session.On(() =>
        {
            ViewPaneGrid said = ViewPaneLayout.Arrange(arrangement, columnSplit, rowSplit, width, height);
            Grid grid = Build(ViewPaneLayout.Arrange(arrangement, columnSplit, rowSplit));

            var window = new Window { Content = grid, Width = width, Height = height };
            window.Show();
            grid.Measure(new Size(width, height));
            grid.Arrange(new Rect(0, 0, width, height));
            Dispatcher.UIThread.RunJobs();

            try
            {
                // Within a pixel: the grid rounds its tracks to whole ones.
                grid.ColumnDefinitions[0].ActualWidth.ShouldBe(said.Columns.First, tolerance: 1);
                grid.ColumnDefinitions[1].ActualWidth.ShouldBe(said.Columns.Gutter, tolerance: 1);
                grid.ColumnDefinitions[2].ActualWidth.ShouldBe(said.Columns.Second, tolerance: 1);

                grid.RowDefinitions[0].ActualHeight.ShouldBe(said.Rows.First, tolerance: 1);
                grid.RowDefinitions[1].ActualHeight.ShouldBe(said.Rows.Gutter, tolerance: 1);
                grid.RowDefinitions[2].ActualHeight.ShouldBe(said.Rows.Second, tolerance: 1);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void The_Logic_header_rasterises_into_a_sheet()
    {
        session.On(() =>
        {
            var strip = new LogicHeaderStrip();
            var window = new Window { Content = strip, Width = 360, Height = 28 };
            window.SetRenderScaling(2.0);
            window.Show();
            Dispatcher.UIThread.RunJobs();

            AvaloniaHeadlessPlatform.ForceRenderTimerTick(1);
            WriteableBitmap? frame = window.GetLastRenderedFrame();
            frame.ShouldNotBeNull();

            Directory.CreateDirectory(RibbonSheetTests.OutputDirectory);
            frame.Save(Path.Combine(RibbonSheetTests.OutputDirectory, "logic-header@2x.png"), quality: null);

            frame.PixelSize.Height.ShouldBe(56);
            window.Close();
        });
    }

    private static (LogicHeaderStrip Strip, Window Window) OpenLogicHeader()
    {
        var strip = new LogicHeaderStrip();

        // Must sit in a Window: the font comes from Controls.axaml's Window selector.
        var window = new Window { Width = 2600, Height = 200, Content = strip };
        window.SetRenderScaling(1.0);
        window.Show();
        Dispatcher.UIThread.RunJobs();

        strip.Measure(new Size(double.PositiveInfinity, 28));
        return (strip, window);
    }

    // The pane grid as the window sets it up: shares as star weights, and a
    // minimum only on a split axis.
    private static Grid Build(in ViewPaneGrid shares)
    {
        ViewPaneAxis columns = shares.Columns;
        ViewPaneAxis rows = shares.Rows;

        var grid = new Grid();

        grid.ColumnDefinitions.Add(new ColumnDefinition(Weight(columns.First))
        {
            MinWidth = columns.IsSplit ? columns.FirstMin : 0,
        });
        grid.ColumnDefinitions.Add(new ColumnDefinition(columns.Gutter, GridUnitType.Pixel));
        grid.ColumnDefinitions.Add(new ColumnDefinition(Weight(columns.Second)) { MinWidth = columns.SecondMin });

        grid.RowDefinitions.Add(new RowDefinition(Weight(rows.First))
        {
            MinHeight = rows.IsSplit ? rows.FirstMin : 0,
        });
        grid.RowDefinitions.Add(new RowDefinition(rows.Gutter, GridUnitType.Pixel));
        grid.RowDefinitions.Add(new RowDefinition(Weight(rows.Second)) { MinHeight = rows.SecondMin });

        return grid;
    }

    private static GridLength Weight(double share) =>
        share > 0 ? new GridLength(share, GridUnitType.Star) : new GridLength(0);
}
