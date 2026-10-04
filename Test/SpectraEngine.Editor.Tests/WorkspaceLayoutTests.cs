using SpectraEngine.Editor.Shell;

namespace SpectraEngine.Editor.Tests;

public sealed class WorkspaceLayoutTests
{
    // Measured from the running window. Vertical: menu row, ribbon, viewport
    // header, status bar, bezel. Horizontal: two 1px splitter columns plus bezel.
    private static readonly WorkspaceChrome Chrome = new(Vertical: 241, Horizontal: 6);

    [Fact]
    public void Compact_at_the_window_minimum_still_leaves_a_usable_viewport()
    {
        WorkspaceMetrics metrics = WorkspaceLayout.For(WorkspacePreset.Compact);

        (double width, double height) = WorkspaceLayout.ViewportCell(metrics, 1180, 640, Chrome);

        width.ShouldBeGreaterThanOrEqualTo(640);
        height.ShouldBeGreaterThanOrEqualTo(300);
    }

    [Fact]
    public void Compact_at_the_default_window_gives_the_viewport_most_of_the_room()
    {
        WorkspaceMetrics metrics = WorkspaceLayout.For(WorkspacePreset.Compact);

        (double width, double height) = WorkspaceLayout.ViewportCell(metrics, 1480, 920, Chrome);

        double share = width * height / (1480 * 920);
        share.ShouldBeGreaterThan(0.40, $"the viewport measured {width}x{height}");
    }

    [Fact]
    public void Expanded_is_what_the_editor_shipped_as()
    {
        WorkspaceMetrics metrics = WorkspaceLayout.For(WorkspacePreset.Expanded);

        metrics.LeftWidth.ShouldBe(288);
        metrics.RightWidth.ShouldBe(308);
        metrics.DrawerOpen.ShouldBeTrue();
        metrics.LevelsDocked.ShouldBeTrue();

        (double width, double height) = WorkspaceLayout.ViewportCell(metrics, 1480, 920, Chrome);
        double share = width * height / (1480 * 920);

        // Measured at 28.5%.
        share.ShouldBeInRange(0.25, 0.32);
    }

    [Fact]
    public void The_drawer_never_pushes_the_viewport_row_under_its_minimum()
    {
        double clamped = WorkspaceLayout.ClampDrawerHeight(160, 300);

        clamped.ShouldBe(300 - WorkspaceLayout.ViewportMinHeight - 1);
        clamped.ShouldBeLessThan(160);
    }

    [Fact]
    public void A_drawer_that_fits_is_left_alone()
    {
        // Row heights at the default window and at the window minimum.
        WorkspaceLayout.ClampDrawerHeight(160, 679).ShouldBe(160);

        WorkspaceLayout.ClampDrawerHeight(160, 399).ShouldBe(160);
    }

    [Fact]
    public void A_drawer_taller_than_the_window_clamps_to_nothing_rather_than_going_negative()
    {
        WorkspaceLayout.ClampDrawerHeight(400, 120).ShouldBe(0);
    }

    [Fact]
    public void A_closed_drawer_gives_its_height_back()
    {
        WorkspaceMetrics open = WorkspaceLayout.For(WorkspacePreset.Expanded);
        WorkspaceMetrics closed = WorkspaceLayout.For(WorkspacePreset.Compact);

        (_, double openHeight) = WorkspaceLayout.ViewportCell(open, 1480, 920, Chrome);
        (_, double closedHeight) = WorkspaceLayout.ViewportCell(closed, 1480, 920, Chrome);

        closedHeight.ShouldBeGreaterThan(openHeight);
    }

    [Theory]
    [InlineData("compact", WorkspacePreset.Compact)]
    [InlineData("expanded", WorkspacePreset.Expanded)]
    [InlineData("EXPANDED", WorkspacePreset.Expanded)]
    public void A_known_preset_word_reads_back(string name, WorkspacePreset expected)
    {
        WorkspaceLayout.TryParse(name, out WorkspacePreset preset).ShouldBeTrue();
        preset.ShouldBe(expected);
    }

    [Fact]
    public void An_unknown_preset_word_reads_as_compact_rather_than_failing()
    {
        WorkspaceLayout.TryParse("theatre", out WorkspacePreset preset).ShouldBeFalse();
        preset.ShouldBe(WorkspacePreset.Compact);

        WorkspaceLayout.TryParse(null, out preset).ShouldBeFalse();
        preset.ShouldBe(WorkspacePreset.Compact);
    }

    [Fact]
    public void Every_preset_has_a_name_that_reads_back()
    {
        foreach (WorkspacePreset preset in new[] { WorkspacePreset.Compact, WorkspacePreset.Expanded })
        {
            WorkspaceLayout.TryParse(WorkspaceLayout.NameOf(preset), out WorkspacePreset back).ShouldBeTrue();
            back.ShouldBe(preset);
        }
    }
}
