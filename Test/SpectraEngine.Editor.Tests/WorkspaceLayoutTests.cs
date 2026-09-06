using SpectraEngine.Editor.Shell;

namespace SpectraEngine.Editor.Tests;

/// <summary>
/// How much of the window the viewport gets.
/// </summary>
/// <remarks>
/// <b>Measured before any of this: 876x442 in a 1480x920 client, so 28%.</b>
/// Two wide sidebars, an expanded ribbon and a 236px bottom dock around a 3D
/// view. The numbers here are the arithmetic half of the fix; a headless render
/// measures the real grid against them.
/// </remarks>
public sealed class WorkspaceLayoutTests
{
    // Measured from the running window: menu row, ribbon strip and body, the
    // viewport header, the status bar and the bezel; and horizontally the two
    // 1px splitter COLUMNS plus the bezel, without the panel columns. The
    // splitters are inside the grid, so leaving them out made the model two
    // pixels optimistic - which the headless measurement caught.
    private static readonly WorkspaceChrome Chrome = new(Vertical: 241, Horizontal: 6);

    [Fact]
    public void Compact_at_the_window_minimum_still_leaves_a_usable_viewport()
    {
        WorkspaceMetrics metrics = WorkspaceLayout.For(WorkspacePreset.Compact);

        (double width, double height) = WorkspaceLayout.ViewportCell(metrics, 1180, 640, Chrome);

        // The window refuses to go below 1180x640, so this is the worst case
        // anybody can actually produce.
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
        // Kept whole rather than tuned, so somebody who wants every panel open
        // gets exactly what they had.
        WorkspaceMetrics metrics = WorkspaceLayout.For(WorkspacePreset.Expanded);

        metrics.LeftWidth.ShouldBe(288);
        metrics.RightWidth.ShouldBe(308);
        metrics.DrawerOpen.ShouldBeTrue();
        metrics.LevelsDocked.ShouldBeTrue();

        (double width, double height) = WorkspaceLayout.ViewportCell(metrics, 1480, 920, Chrome);
        double share = width * height / (1480 * 920);

        // The measured 28.5%, pinned so a change to the expanded preset has to
        // be deliberate.
        share.ShouldBeInRange(0.25, 0.32);
    }

    [Fact]
    public void The_drawer_never_pushes_the_viewport_row_under_its_minimum()
    {
        // 300px of rows cannot hold a 160px drawer AND the viewport's own 200px
        // floor, so the drawer opens smaller rather than the grid resolving it
        // by shrinking something else.
        double clamped = WorkspaceLayout.ClampDrawerHeight(160, 300);

        clamped.ShouldBe(300 - WorkspaceLayout.ViewportMinHeight - 1);
        clamped.ShouldBeLessThan(160);
    }

    [Fact]
    public void A_drawer_that_fits_is_left_alone()
    {
        // At the default window there is room for it, so nothing is clamped.
        WorkspaceLayout.ClampDrawerHeight(160, 679).ShouldBe(160);

        // And at the window minimum too: 399 rows hold 160 plus the floor.
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
        // From a newer shell, or a hand edit. Every other setting here degrades
        // the same way rather than losing the whole file.
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
