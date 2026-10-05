using Avalonia;
using SpectraEngine.Editor.Shell.Logic;
using System;

namespace SpectraEngine.Editor.Tests.Logic;

/// <summary>Where the graph sits in its view: panning, zooming, fitting and centring.</summary>
public sealed class LogicPanZoomTests
{
    private static readonly Size View = new(800, 600);

    [Fact]
    public void A_graph_that_fits_readably_is_placed_fitted()
    {
        var scene = new Size(900, 500);

        LogicPanZoom.Placed(scene, View, null).ShouldBe(LogicPanZoom.Fit(scene, View));
    }

    [Fact]
    public void A_graph_that_would_be_too_small_to_read_is_placed_readable_at_its_corner()
    {
        LogicPanZoom.Placed(new Size(4000, 3000), View, null)
            .ShouldBe(new LogicPanZoom(default, LogicPanZoom.ReadableZoom));
    }

    [Fact]
    public void With_something_to_focus_on_it_is_placed_readable_about_that()
    {
        var focus = new Rect(2000, 1500, 184, 90);

        LogicPanZoom placed = LogicPanZoom.Placed(new Size(4000, 3000), View, focus);

        placed.Zoom.ShouldBe(LogicPanZoom.ReadableZoom);
        Point middle = placed.ToView(focus.Center);
        Math.Abs(middle.X - View.Width / 2).ShouldBeLessThanOrEqualTo(1);
        Math.Abs(middle.Y - View.Height / 2).ShouldBeLessThanOrEqualTo(1);
    }

    [Fact]
    public void A_scene_point_goes_to_the_view_and_back()
    {
        var at = new LogicPanZoom(new Vector(30, -12), 0.5);

        at.ToView(new Point(100, 40)).ShouldBe(new Point(80, 8));
        at.ToScene(new Point(80, 8)).ShouldBe(new Point(100, 40));
        at.ToScene(new Rect(30, -12, 400, 300)).ShouldBe(new Rect(0, 0, 800, 600));
    }

    [Theory]
    [InlineData(0.5)]
    [InlineData(1.0)]
    [InlineData(1.15)]
    [InlineData(1.7)]
    public void Zooming_about_a_point_keeps_what_is_under_it_within_a_pixel(double zoom)
    {
        var start = new LogicPanZoom(new Vector(17, 43), 0.8);
        var anchor = new Point(311, 207);
        Point held = start.ToScene(anchor);

        LogicPanZoom zoomed = start.ZoomedAbout(anchor, zoom);
        Point now = zoomed.ToView(held);

        zoomed.Zoom.ShouldBe(zoom);
        Math.Abs(now.X - anchor.X).ShouldBeLessThanOrEqualTo(0.5);
        Math.Abs(now.Y - anchor.Y).ShouldBeLessThanOrEqualTo(0.5);
    }

    [Theory]
    [InlineData(0.01, LogicPanZoom.MinimumZoom)]
    [InlineData(9.0, LogicPanZoom.MaximumZoom)]
    [InlineData(double.NaN, 1.0)]
    public void The_wheel_stops_at_ten_percent_and_at_two_hundred(double asked, double expected)
    {
        LogicPanZoom.Identity.ZoomedAbout(new Point(10, 10), asked).Zoom.ShouldBe(expected);
    }

    [Fact]
    public void Every_move_and_zoom_leaves_the_offset_on_whole_pixels()
    {
        LogicPanZoom moved = LogicPanZoom.Identity.MovedBy(new Vector(10.4, -3.6));
        LogicPanZoom zoomed = moved.ZoomedAbout(new Point(101.3, 77.7), 1.33);
        LogicPanZoom centered = zoomed.CenteredOn(new Rect(13, 9, 185, 71), new Size(801, 599));

        moved.Offset.ShouldBe(new Vector(10, -4));
        IsWhole(zoomed.Offset).ShouldBeTrue();
        IsWhole(centered.Offset).ShouldBeTrue();
    }

    [Fact]
    public void A_scene_smaller_than_the_view_is_fitted_at_its_own_size_in_the_middle()
    {
        LogicPanZoom fit = LogicPanZoom.Fit(new Size(400, 200), View);

        fit.Zoom.ShouldBe(1);
        fit.Offset.ShouldBe(new Vector(200, 200));
    }

    [Fact]
    public void A_scene_larger_than_the_view_is_shrunk_until_all_of_it_shows()
    {
        var scene = new Size(1600, 600);

        LogicPanZoom fit = LogicPanZoom.Fit(scene, View);
        Rect shown = new(fit.ToView(default), fit.ToView(new Point(scene.Width, scene.Height)));

        fit.Zoom.ShouldBe(0.5);
        shown.ShouldBe(new Rect(0, 150, 800, 300));
    }

    [Fact]
    public void A_fit_goes_further_out_than_the_wheel_so_a_large_level_shows_whole()
    {
        var scene = new Size(20000, 9000);

        LogicPanZoom fit = LogicPanZoom.Fit(scene, View);

        fit.Zoom.ShouldBe(0.04);
        fit.ToView(new Point(scene.Width, scene.Height)).X.ShouldBeLessThanOrEqualTo(View.Width);
    }

    [Fact]
    public void The_wheel_does_not_jump_back_to_its_limit_from_a_fit_that_went_past_it()
    {
        LogicPanZoom fit = LogicPanZoom.Fit(new Size(20000, 9000), View);
        var anchor = new Point(400, 300);

        fit.ZoomedAbout(anchor, fit.Zoom / 1.15).Zoom.ShouldBe(fit.Zoom);
        fit.ZoomedAbout(anchor, fit.Zoom * 1.15).Zoom.ShouldBe(fit.Zoom * 1.15, 1e-9);
    }

    [Fact]
    public void A_fit_stops_at_its_own_floor()
    {
        LogicPanZoom.Fit(new Size(1e7, 1e7), View).Zoom.ShouldBe(LogicPanZoom.MinimumFitZoom);
    }

    [Theory]
    [InlineData(0, 0, 800, 600)]
    [InlineData(400, 200, 0, 600)]
    [InlineData(400, 200, 800, 0)]
    public void Nothing_to_fit_or_nowhere_to_fit_it_leaves_the_scene_as_it_is(
        double sceneWidth, double sceneHeight, double viewWidth, double viewHeight)
    {
        LogicPanZoom.Fit(new Size(sceneWidth, sceneHeight), new Size(viewWidth, viewHeight))
            .ShouldBe(LogicPanZoom.Identity);
    }

    [Fact]
    public void Centring_puts_the_middle_of_a_rectangle_in_the_middle_of_the_view()
    {
        var at = new LogicPanZoom(new Vector(5, 5), 2);
        var card = new Rect(100, 50, 184, 70);

        LogicPanZoom centered = at.CenteredOn(card, View);

        centered.Zoom.ShouldBe(2);
        centered.ToView(card.Center).ShouldBe(new Point(400, 300));
    }

    private static bool IsWhole(Vector offset) =>
        offset.X == Math.Round(offset.X) && offset.Y == Math.Round(offset.Y);
}
