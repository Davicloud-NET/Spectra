using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;

using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Scene;
using SpectraEngine.Editing.Commands;
using SpectraEngine.Editor.Shell;

namespace SpectraEngine.Editor.Render.Tests;

/// <summary>
/// The label column of the Properties panel when a setting's name is long:
/// how wide it gets, what a label with no room looks like, and what its
/// tooltip then says.
/// </summary>
[Collection(RibbonSessionCollection.Name)]
public sealed class PropertyLabelTests(RibbonSession session)
{
    private const double Usual = PropertyLabelFixture.UsualWidth;
    private const double Expanded = PropertyLabelFixture.ExpandedWidth;
    private const double Narrowest = PropertyLabelFixture.NarrowestWidth;

    private const string DragHint = "Drag sideways to change all three. Shift for ten times, Ctrl for a tenth.";

    [Theory]
    [InlineData("sound", Usual)]
    [InlineData("sound", Expanded)]
    [InlineData("sound", Narrowest)]
    [InlineData("spawner", Usual)]
    [InlineData("spawner", Expanded)]
    [InlineData("spawner", Narrowest)]
    [InlineData("both", Usual)]
    [InlineData("both", Narrowest)]
    [InlineData("lamp", Usual)]
    [InlineData("part", Usual)]
    [InlineData("part", Narrowest)]
    public void The_label_column_rasterises_into_a_sheet(string selection, double width)
    {
        On(width, Selection(selection), (window, _, _) =>
            Save(window, $"properties-labels-{selection}-{width:0}@2x.png"));
    }

    [Fact]
    public void A_tooltip_with_a_name_and_its_help_rasterises_into_a_sheet()
    {
        session.On(() =>
        {
            // The tip as the panel composes it, in the theme's own tooltip.
            // Opacity is set because the theme keeps a tooltip clear until it opens.
            var tip = new ToolTip
            {
                Content = "Maximum distance\nThe sound is silent beyond this distance.",
                Opacity = 1,
            };
            var window = new Window
            {
                Content = new Border { Padding = new Thickness(8), Child = tip },
                SizeToContent = SizeToContent.WidthAndHeight,
            };
            window.SetRenderScaling(2.0);
            window.Show();
            Dispatcher.UIThread.RunJobs();

            Save(window, "properties-label-tip@2x.png");
            window.Close();
        });
    }

    [Fact]
    public void A_long_label_is_shown_whole_when_the_panel_has_the_room()
    {
        On(Usual, [PropertyLabelFixture.Sound()], (_, panel, _) =>
        {
            Label(panel, "Minimum distance").IsTrimmed.ShouldBeFalse();
            Label(panel, "Maximum distance").IsTrimmed.ShouldBeFalse();
            Label(panel, "Fades with distance").IsTrimmed.ShouldBeFalse();
        });
    }

    [Fact]
    public void A_label_with_no_room_is_trimmed_and_its_tooltip_says_the_whole_name_and_then_its_help()
    {
        On(Narrowest, [PropertyLabelFixture.Sound()], (_, panel, _) =>
        {
            RowLabel label = Label(panel, "Maximum distance");

            label.IsTrimmed.ShouldBeTrue();
            ToolTip.GetTip(label).ShouldBe("Maximum distance\nThe sound is silent beyond this distance.");
        });
    }

    [Fact]
    public void A_trimmed_label_whose_setting_has_no_help_says_only_its_name()
    {
        On(Usual, [PropertyLabelFixture.Spawner()], (_, panel, _) =>
        {
            RowLabel label = Label(panel, "Farthest a spawned thing may land");

            label.IsTrimmed.ShouldBeTrue();
            ToolTip.GetTip(label).ShouldBe("Farthest a spawned thing may land");
        });
    }

    [Fact]
    public void A_label_that_fits_says_only_its_settings_help()
    {
        On(Usual, [PropertyLabelFixture.Sound()], (_, panel, _) =>
            ToolTip.GetTip(Label(panel, "Maximum distance")).ShouldBe("The sound is silent beyond this distance."));
    }

    [Fact]
    public void A_label_that_fits_and_has_no_help_has_no_tooltip()
    {
        On(Usual, [PropertyLabelFixture.Spawner()], (_, panel, _) =>
            ToolTip.GetTip(Label(panel, "Limit")).ShouldBeNull());
    }

    [Fact]
    public void A_label_gets_its_tooltip_back_when_the_panel_is_made_narrow()
    {
        On(Usual, [PropertyLabelFixture.Sound()], (window, panel, _) =>
        {
            RowLabel label = Label(panel, "Minimum distance");
            label.IsTrimmed.ShouldBeFalse();

            window.Width = Narrowest;
            Dispatcher.UIThread.RunJobs();

            label.IsTrimmed.ShouldBeTrue();
            ToolTip.GetTip(label).ShouldBe("Minimum distance\nThe sound is at full volume inside this distance.");
        });
    }

    [Fact]
    public void A_vector_label_still_says_how_to_drag_it()
    {
        On(Usual, [PropertyLabelFixture.Sound()], (_, panel, _) =>
            ToolTip.GetTip(Label(panel, "Position")).ShouldBe(DragHint));
    }

    [Theory]
    [InlineData(Usual)]
    [InlineData(Expanded)]
    [InlineData(Narrowest)]
    public void Every_editor_starts_where_the_others_do(double width)
    {
        On(width, [PropertyLabelFixture.Sound(), PropertyLabelFixture.Spawner()], (_, panel, _) =>
        {
            double[] columns = LabelColumns(panel);

            columns.Length.ShouldBeGreaterThan(10);
            columns.Distinct().ShouldHaveSingleItem();
        });
    }

    [Theory]
    [InlineData(Usual)]
    [InlineData(Expanded)]
    [InlineData(Narrowest)]
    public void Long_labels_take_the_column_to_its_limit_and_no_further(double width)
    {
        On(width, [PropertyLabelFixture.Spawner()], (_, panel, _) =>
        {
            double row = RowGrids(panel).First().Bounds.Width;

            LabelColumns(panel).Distinct().ShouldHaveSingleItem().ShouldBe(RowLabelCell.Limit(row), 0.5);
        });
    }

    [Fact]
    public void Short_labels_leave_the_column_at_its_floor()
    {
        On(Expanded, [PropertyLabelFixture.Lamp()], (_, panel, _) =>
            LabelColumns(panel).Distinct().ShouldHaveSingleItem().ShouldBe(RowLabelCell.Floor));
    }

    [Fact]
    public void A_label_as_wide_as_the_floor_is_shown_whole_in_the_narrowest_panel()
    {
        // The longest label of a part. It gives up the gap after it to fit.
        On(Narrowest, [PropertyLabelFixture.Part()], (_, panel, _) =>
        {
            Label(panel, "Seen by queries").IsTrimmed.ShouldBeFalse();
            LabelColumns(panel).Distinct().ShouldHaveSingleItem().ShouldBe(RowLabelCell.Floor);
        });
    }

    [Fact]
    public void The_column_goes_back_to_its_floor_when_the_next_selection_has_short_labels()
    {
        On(Usual, [PropertyLabelFixture.Spawner()], (_, panel, _) =>
        {
            LabelColumns(panel).Max().ShouldBeGreaterThan(RowLabelCell.Floor);

            PropertyLabelFixture.Select(panel, PropertyLabelFixture.Lamp());

            LabelColumns(panel).Distinct().ShouldHaveSingleItem().ShouldBe(RowLabelCell.Floor);
        });
    }

    // Row widths at the narrowest, usual and expanded panel, between the
    // first two, and in a wide one.
    [Theory]
    [InlineData(196, 86)]
    [InlineData(220, 110)]
    [InlineData(256, 128)]
    [InlineData(284, 142)]
    [InlineData(600, 300)]
    public void The_column_takes_no_more_than_half_the_row_and_leaves_the_editor_its_floor(double row, double limit)
    {
        RowLabelCell.Limit(row).ShouldBe(limit);
    }

    [Fact]
    public void The_partial_tag_stays_whole_beside_a_trimmed_label()
    {
        On(Narrowest, [PropertyLabelFixture.Sound(), PropertyLabelFixture.Spawner()], (_, panel, _) =>
        {
            RowLabel label = Label(panel, "Maximum distance");
            Grid row = label.FindAncestorOfType<Grid>().ShouldNotBeNull();
            TextBlock tag = row.GetVisualDescendants().OfType<TextBlock>().Single(text => text.Text == "1 of 2");

            label.IsTrimmed.ShouldBeTrue();
            tag.IsEffectivelyVisible.ShouldBeTrue();
            tag.Bounds.Width.ShouldBe(tag.DesiredSize.Width - tag.Margin.Left, 0.5);
            RibbonProbe.BoundsIn(tag, row).Right.ShouldBeLessThanOrEqualTo(row.ColumnDefinitions[0].ActualWidth);
        });
    }

    [Fact]
    public void Dragging_a_trimmed_label_still_changes_its_number()
    {
        On(Narrowest, [PropertyLabelFixture.Sound()], (window, panel, edits) =>
        {
            RowLabel label = Label(panel, "Maximum distance");
            label.IsTrimmed.ShouldBeTrue();
            Point start = label.TranslatePoint(
                new Point(label.Bounds.Width / 2, label.Bounds.Height / 2), window).ShouldNotBeNull();

            window.MouseDown(start, MouseButton.Left);
            window.MouseMove(start + new Vector(50, 0), RawInputModifiers.LeftMouseButton);
            window.MouseUp(start + new Vector(50, 0), MouseButton.Left);
            Dispatcher.UIThread.RunJobs();

            edits.ShouldNotBeEmpty();
            PropertyEdit edit = edits[^1];
            edit.Key.ShouldBe("maxdistance");
            KeyvalueWire.TryParseFloat(edit.Text, out float distance).ShouldBeTrue();
            distance.ShouldBe(31f, 0.01f);
        });
    }

    private void On(double width, SceneNode[] selection, Action<Window, PropertiesPanel, List<PropertyEdit>> body)
    {
        session.On(() =>
        {
            (Window window, PropertiesPanel panel, List<PropertyEdit> edits) =
                PropertyLabelFixture.Open(width, selection);

            try
            {
                body(window, panel, edits);
            }
            finally
            {
                window.Close();
            }
        });
    }

    private static SceneNode[] Selection(string name) => name switch
    {
        "sound" => [PropertyLabelFixture.Sound()],
        "spawner" => [PropertyLabelFixture.Spawner()],
        "lamp" => [PropertyLabelFixture.Lamp()],
        "part" => [PropertyLabelFixture.Part()],
        _ => [PropertyLabelFixture.Sound(), PropertyLabelFixture.Spawner()],
    };

    private static RowLabel Label(PropertiesPanel panel, string name) =>
        panel.GetVisualDescendants().OfType<RowLabel>()
            .Single(label => label.Text == name && label.IsEffectivelyVisible);

    // The grid of every row that has its label on the left and its editor on the right.
    private static IEnumerable<Grid> RowGrids(PropertiesPanel panel) =>
        panel.GetVisualDescendants().OfType<Grid>()
            .Where(grid => grid.IsEffectivelyVisible
                && grid.ColumnDefinitions.Count == 2
                && grid.ColumnDefinitions[0].SharedSizeGroup is not null);

    private static double[] LabelColumns(PropertiesPanel panel) =>
        [.. RowGrids(panel).Select(grid => grid.ColumnDefinitions[0].ActualWidth)];

    // Two ticks: a change made while the last one waits to be drawn is only
    // handed to the renderer once that one has been.
    private static void Save(TopLevel surface, string fileName)
    {
        AvaloniaHeadlessPlatform.ForceRenderTimerTick(1);
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick(1);
        WriteableBitmap frame = surface.GetLastRenderedFrame().ShouldNotBeNull("nothing was rasterised");

        Directory.CreateDirectory(RibbonSheetTests.OutputDirectory);
        frame.Save(Path.Combine(RibbonSheetTests.OutputDirectory, fileName), new PngBitmapEncoderOptions());
    }
}
