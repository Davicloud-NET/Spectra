using SpectraEngine.Editor.Shell;

namespace SpectraEngine.Editor.Tests;

/// <summary>How the centre of the window is divided between the view panes.</summary>
public sealed class ViewPaneLayoutTests
{
    private const double Gutter = WorkspaceLayout.Gutter;

    private static readonly ViewArrangement[] Arrangements =
    [
        ViewArrangement.Single, ViewArrangement.LogicBelow, ViewArrangement.LogicBeside,
    ];

    // The numbers WorkspaceLayoutTests uses for the window minimum: the chrome
    // round the viewport cell, and the editor grid's height.
    private static readonly WorkspaceChrome Chrome = new(Vertical: 224, Horizontal: 32);
    private const double RowsAtTheWindowMinimum = 399;

    // The bottom row's minimum in the editor grid.
    private const double DrawerMinHeight = 90;

    [Fact]
    public void The_3D_view_alone_takes_the_whole_grid()
    {
        ViewPaneGrid grid = ViewPaneLayout.Arrange(ViewArrangement.Single, 0.6, 0.45, 900, 600);

        grid.ShowsLogic.ShouldBeFalse();
        grid.View.ShouldBe(new ViewPaneCell(0, 0));

        grid.Columns.IsSplit.ShouldBeFalse();
        grid.Columns.First.ShouldBe(900);
        grid.Columns.Gutter.ShouldBe(0);
        grid.Columns.Second.ShouldBe(0);

        grid.Rows.IsSplit.ShouldBeFalse();
        grid.Rows.First.ShouldBe(600);
        grid.Rows.Gutter.ShouldBe(0);
        grid.Rows.Second.ShouldBe(0);
    }

    [Fact]
    public void Logic_below_splits_the_rows_and_leaves_the_columns_whole()
    {
        ViewPaneGrid grid = ViewPaneLayout.Arrange(ViewArrangement.LogicBelow, 0.6, 0.5, 900, 607);

        grid.ShowsLogic.ShouldBeTrue();
        grid.View.ShouldBe(new ViewPaneCell(0, 0));
        grid.Logic.ShouldBe(new ViewPaneCell(0, 2));

        grid.Columns.IsSplit.ShouldBeFalse();
        grid.Columns.First.ShouldBe(900);

        grid.Rows.IsSplit.ShouldBeTrue();
        grid.Rows.First.ShouldBe(300);
        grid.Rows.Gutter.ShouldBe(Gutter);
        grid.Rows.Second.ShouldBe(300);
    }

    [Fact]
    public void Logic_beside_splits_the_columns_and_leaves_the_rows_whole()
    {
        ViewPaneGrid grid = ViewPaneLayout.Arrange(ViewArrangement.LogicBeside, 0.6, 0.45, 1007, 600);

        grid.ShowsLogic.ShouldBeTrue();
        grid.View.ShouldBe(new ViewPaneCell(0, 0));
        grid.Logic.ShouldBe(new ViewPaneCell(2, 0));

        grid.Rows.IsSplit.ShouldBeFalse();
        grid.Rows.First.ShouldBe(600);

        grid.Columns.IsSplit.ShouldBeTrue();
        grid.Columns.First.ShouldBe(600);
        grid.Columns.Gutter.ShouldBe(Gutter);
        grid.Columns.Second.ShouldBe(400);
    }

    [Theory]
    [InlineData(ViewArrangement.LogicBelow)]
    [InlineData(ViewArrangement.LogicBeside)]
    public void The_tracks_of_a_split_axis_add_up_to_the_space(ViewArrangement arrangement)
    {
        ViewPaneGrid grid = ViewPaneLayout.Arrange(arrangement, 0.37, 0.61, 913, 571);

        ViewPaneAxis columns = grid.Columns;
        ViewPaneAxis rows = grid.Rows;

        (columns.First + columns.Gutter + columns.Second).ShouldBe(913, tolerance: 1e-9);
        (rows.First + rows.Gutter + rows.Second).ShouldBe(571, tolerance: 1e-9);
    }

    [Fact]
    public void The_3D_view_keeps_its_minimum_when_the_split_asks_for_less()
    {
        ViewPaneGrid below = ViewPaneLayout.Arrange(ViewArrangement.LogicBelow, 0.6, 0.1, 900, 607);
        below.Rows.First.ShouldBe(WorkspaceLayout.ViewportMinHeight);
        below.Rows.Second.ShouldBe(600 - WorkspaceLayout.ViewportMinHeight);

        ViewPaneGrid beside = ViewPaneLayout.Arrange(ViewArrangement.LogicBeside, 0.1, 0.45, 1007, 600);
        beside.Columns.First.ShouldBe(WorkspaceLayout.ViewportMinWidth);
        beside.Columns.Second.ShouldBe(1000 - WorkspaceLayout.ViewportMinWidth);
    }

    [Fact]
    public void The_Logic_view_keeps_its_minimum_when_the_split_squeezes_it()
    {
        ViewPaneGrid below = ViewPaneLayout.Arrange(ViewArrangement.LogicBelow, 0.6, 0.98, 900, 607);
        below.Rows.Second.ShouldBe(ViewPaneLayout.LogicMinHeight);

        ViewPaneGrid beside = ViewPaneLayout.Arrange(ViewArrangement.LogicBeside, 0.98, 0.45, 1007, 600);
        beside.Columns.Second.ShouldBe(ViewPaneLayout.LogicMinWidth);
    }

    [Fact]
    public void When_both_minimums_cannot_hold_the_3D_view_keeps_its_own()
    {
        // 250 high: 200 for the 3D view leaves 43 for a pane that wants 90.
        ViewPaneGrid grid = ViewPaneLayout.Arrange(ViewArrangement.LogicBelow, 0.6, 0.45, 900, 250);

        grid.Rows.First.ShouldBe(WorkspaceLayout.ViewportMinHeight);
        grid.Rows.Second.ShouldBe(250 - Gutter - WorkspaceLayout.ViewportMinHeight);
    }

    [Fact]
    public void A_space_smaller_than_the_3D_view_never_gives_a_pane_a_negative_length()
    {
        ViewPaneGrid grid = ViewPaneLayout.Arrange(ViewArrangement.LogicBeside, 0.6, 0.45, 300, 600);

        grid.Columns.Second.ShouldBe(0);
        grid.Columns.First.ShouldBe(WorkspaceLayout.ViewportMinWidth);
    }

    [Fact]
    public void With_no_size_the_pane_lengths_are_the_shares()
    {
        // A grid takes them as star weights and divides its own space.
        ViewPaneGrid grid = ViewPaneLayout.Arrange(ViewArrangement.LogicBeside, 0.6, 0.45);

        grid.Columns.First.ShouldBe(0.6);
        grid.Columns.Second.ShouldBe(0.4, tolerance: 1e-12);
        grid.Columns.Gutter.ShouldBe(Gutter);

        grid.Rows.First.ShouldBe(1);
        grid.Rows.Second.ShouldBe(0);
        grid.Rows.Gutter.ShouldBe(0);
    }

    [Fact]
    public void The_shares_are_as_asked_for_even_where_a_minimum_would_bite()
    {
        // So the proportion comes back when the window grows.
        ViewPaneGrid grid = ViewPaneLayout.Arrange(ViewArrangement.LogicBelow, 0.6, 0.1);

        grid.Rows.First.ShouldBe(0.1);
        grid.Rows.Second.ShouldBe(0.9, tolerance: 1e-12);
        grid.Rows.FirstMin.ShouldBe(WorkspaceLayout.ViewportMinHeight);
        grid.Rows.SecondMin.ShouldBe(ViewPaneLayout.LogicMinHeight);
    }

    [Fact]
    public void The_minimums_are_the_viewport_cells_own_and_the_Logic_panes()
    {
        ViewPaneLayout.MinimumSize(ViewArrangement.Single)
            .ShouldBe((WorkspaceLayout.ViewportMinWidth, WorkspaceLayout.ViewportMinHeight));

        ViewPaneLayout.MinimumSize(ViewArrangement.LogicBelow).ShouldBe((
            WorkspaceLayout.ViewportMinWidth,
            WorkspaceLayout.ViewportMinHeight + Gutter + ViewPaneLayout.LogicMinHeight));

        ViewPaneLayout.MinimumSize(ViewArrangement.LogicBeside).ShouldBe((
            WorkspaceLayout.ViewportMinWidth + Gutter + ViewPaneLayout.LogicMinWidth,
            WorkspaceLayout.ViewportMinHeight));
    }

    [Fact]
    public void An_arrangement_fits_from_its_minimum_up()
    {
        (double width, double height) = ViewPaneLayout.MinimumSize(ViewArrangement.LogicBeside);

        ViewPaneLayout.Fits(ViewArrangement.LogicBeside, width, height).ShouldBeTrue();
        ViewPaneLayout.Fits(ViewArrangement.LogicBeside, width - 1, height).ShouldBeFalse();
        ViewPaneLayout.Fits(ViewArrangement.LogicBeside, width, height - 1).ShouldBeFalse();

        // The same space still holds the 3D view alone.
        ViewPaneLayout.Fits(ViewArrangement.Single, width - 1, height).ShouldBeTrue();
    }

    [Theory]
    [InlineData(WorkspacePreset.Compact)]
    [InlineData(WorkspacePreset.Expanded)]
    public void Every_arrangement_fits_both_workspaces_at_the_window_minimum(WorkspacePreset preset)
    {
        WorkspaceMetrics metrics = WorkspaceLayout.For(preset);
        (double width, _) = WorkspaceLayout.ViewportCell(metrics, 1180, 640, Chrome);

        foreach (ViewArrangement arrangement in Arrangements)
        {
            (_, double minHeight) = ViewPaneLayout.MinimumSize(arrangement);

            // An open drawer gives way until the centre row has its minimum.
            double drawer = metrics.DrawerOpen
                ? WorkspaceLayout.ClampDrawerHeight(metrics.DrawerHeight, RowsAtTheWindowMinimum, minHeight)
                : 0;
            double height = RowsAtTheWindowMinimum - (metrics.DrawerOpen ? drawer + Gutter : 0);

            ViewPaneLayout.Fits(arrangement, width, height).ShouldBeTrue(
                $"{arrangement} in the {preset} workspace gets {width}x{height}");

            if (metrics.DrawerOpen)
                drawer.ShouldBeGreaterThanOrEqualTo(DrawerMinHeight, $"{arrangement} leaves the drawer {drawer}");
        }
    }

    [Fact]
    public void The_drawer_clamp_leaves_the_centre_whatever_minimum_it_is_given()
    {
        (_, double minHeight) = ViewPaneLayout.MinimumSize(ViewArrangement.LogicBelow);

        WorkspaceLayout.ClampDrawerHeight(236, 399, minHeight).ShouldBe(399 - minHeight - Gutter);

        // The two-argument clamp is this one at the viewport's own minimum.
        WorkspaceLayout.ClampDrawerHeight(236, 399, WorkspaceLayout.ViewportMinHeight)
            .ShouldBe(WorkspaceLayout.ClampDrawerHeight(236, 399));
    }

    [Fact]
    public void A_split_that_leaves_both_minimums_is_not_moved()
    {
        ViewPaneLayout.ClampSplit(0.5, 607, 200, 90).ShouldBe(0.5);
    }

    [Fact]
    public void A_split_is_clamped_to_the_nearest_one_both_panes_can_live_with()
    {
        ViewPaneLayout.ClampSplit(0.05, 607, 200, 90).ShouldBe(200.0 / 600, tolerance: 1e-12);
        ViewPaneLayout.ClampSplit(0.99, 607, 200, 90).ShouldBe(510.0 / 600, tolerance: 1e-12);
    }

    [Fact]
    public void A_clamp_with_no_size_yet_changes_nothing()
    {
        ViewPaneLayout.ClampSplit(0.05, 0, 200, 90).ShouldBe(0.05);
    }

    [Fact]
    public void A_dragged_pair_of_tracks_reads_back_as_the_split_that_made_them()
    {
        ViewPaneGrid grid = ViewPaneLayout.Arrange(ViewArrangement.LogicBeside, 0.7, 0.45, 1207, 600);

        ViewPaneLayout.TryReadSplit(grid.Columns.First, grid.Columns.Second, out double split).ShouldBeTrue();
        split.ShouldBe(0.7, tolerance: 1e-12);
    }

    [Theory]
    [InlineData(0, 300)]
    [InlineData(300, 0)]
    [InlineData(0, 0)]
    [InlineData(double.NaN, 300)]
    public void Tracks_with_no_length_read_back_as_no_split(double first, double second)
    {
        ViewPaneLayout.TryReadSplit(first, second, out _).ShouldBeFalse();
    }

    [Theory]
    [InlineData(0.01, true)]
    [InlineData(0.5, true)]
    [InlineData(0.99, true)]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(-0.2, false)]
    [InlineData(1.5, false)]
    [InlineData(double.NaN, false)]
    [InlineData(double.PositiveInfinity, false)]
    public void A_split_is_a_finite_share_between_nothing_and_everything(double split, bool expected)
    {
        ViewPaneLayout.IsSplit(split).ShouldBe(expected);
    }

    [Fact]
    public void The_key_hides_a_Logic_view_that_shows()
    {
        ViewPaneLayout.Flip(ViewArrangement.LogicBelow, ViewArrangement.LogicBelow).ShouldBe(ViewArrangement.Single);
        ViewPaneLayout.Flip(ViewArrangement.LogicBeside, ViewArrangement.LogicBelow).ShouldBe(ViewArrangement.Single);
    }

    [Fact]
    public void The_key_brings_a_hidden_Logic_view_back_where_it_last_was()
    {
        ViewPaneLayout.Flip(ViewArrangement.Single, ViewArrangement.LogicBeside).ShouldBe(ViewArrangement.LogicBeside);
        ViewPaneLayout.Flip(ViewArrangement.Single, ViewArrangement.LogicBelow).ShouldBe(ViewArrangement.LogicBelow);
    }

    [Fact]
    public void The_key_opens_below_when_no_split_was_ever_used()
    {
        ViewPaneLayout.Flip(ViewArrangement.Single, ViewArrangement.Single).ShouldBe(ViewArrangement.LogicBelow);
    }

    [Fact]
    public void The_defaults_give_the_Logic_view_the_larger_half_below_and_the_smaller_beside()
    {
        ViewPaneLayout.DefaultSplit.ShouldBe(ViewArrangement.LogicBelow);
        ViewPaneLayout.DefaultRowSplit.ShouldBeInRange(0.4, 0.5);
        ViewPaneLayout.DefaultColumnSplit.ShouldBe(0.6);
    }

    [Fact]
    public void Every_arrangement_has_a_name_that_reads_back()
    {
        foreach (ViewArrangement arrangement in Arrangements)
        {
            ViewPaneLayout.TryParse(ViewPaneLayout.NameOf(arrangement), out ViewArrangement back).ShouldBeTrue();
            back.ShouldBe(arrangement);
        }
    }

    [Theory]
    [InlineData("single", ViewArrangement.Single)]
    [InlineData("logicBelow", ViewArrangement.LogicBelow)]
    [InlineData("LOGICBESIDE", ViewArrangement.LogicBeside)]
    public void A_known_arrangement_word_reads_back(string name, ViewArrangement expected)
    {
        ViewPaneLayout.TryParse(name, out ViewArrangement arrangement).ShouldBeTrue();
        arrangement.ShouldBe(expected);
    }

    [Fact]
    public void An_unknown_arrangement_word_reads_as_single_rather_than_failing()
    {
        ViewPaneLayout.TryParse("quad", out ViewArrangement arrangement).ShouldBeFalse();
        arrangement.ShouldBe(ViewArrangement.Single);

        ViewPaneLayout.TryParse(null, out arrangement).ShouldBeFalse();
        arrangement.ShouldBe(ViewArrangement.Single);
    }
}
