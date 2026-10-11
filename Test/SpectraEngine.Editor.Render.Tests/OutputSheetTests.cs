using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;

using SpectraEngine.Core.ConsoleSystem;
using SpectraEngine.Editor.Shell;

namespace SpectraEngine.Editor.Render.Tests;

/// <summary>
/// The Output panel with what scripts print in it, rasterised so a person can
/// look at it: a line's place beside its text, an error's calls under it, a
/// folded line, and the notices about lines that were dropped.
/// </summary>
[Collection(RibbonSessionCollection.Name)]
public sealed class OutputSheetTests(RibbonSession session)
{
    private const string Door = "Scripts/level one/door.luau";
    private const string Spinner = "Scripts/spinner.luau";

    private static readonly ConsolePlace[] NoCalls = [];

    [Fact]
    public void A_scripts_lines_show_their_place_beside_the_text_and_an_errors_calls_under_it()
    {
        session.On(() =>
        {
            (Window window, OutputPanel panel, ShellModel model) = Open(Fill);

            Shown(panel, "spinner.luau:5").ShouldHaveSingleItem();
            Shown(panel, "door.luau:4").ShouldHaveSingleItem();
            Shown(panel, "x12").ShouldHaveSingleItem();
            Shown(panel, "door.luau:30 in press").ShouldHaveSingleItem();
            Shown(panel, "Show 3 more calls").ShouldHaveSingleItem();
            Shown(panel, "door.luau:77 in step4").ShouldBeEmpty("the fourth call waits to be asked for");
            model.Output.HistoryLabel.ShouldBe("Shell output, 8 of 500 lines");

            // The place stands to the right of the text, and a call starts
            // under the text of its error, a step in.
            TextBlock text = Shown(panel, "attempt to index nil with 'Name'").ShouldHaveSingleItem();
            TextBlock place = Shown(panel, "door.luau:12").ShouldHaveSingleItem();
            TextBlock call = Shown(panel, "door.luau:30 in press").ShouldHaveSingleItem();
            Left(place, window).ShouldBeGreaterThan(Left(text, window) + text.Bounds.Width - 1);
            Left(call, window).ShouldBe(Left(text, window) + 12, 0.5);

            // With a count beside it or without one, a place ends in the same column.
            TextBlock counted = Shown(panel, "door.luau:4").ShouldHaveSingleItem();
            Right(counted, window).ShouldBe(Right(place, window), 0.01);

            ScriptSheet.Save(window, "output-script-lines@2x.png");
            window.Close();
        });
    }

    [Fact]
    public void A_line_with_no_place_is_drawn_and_placed_as_a_line_that_has_one_and_is_no_button()
    {
        session.On(() =>
        {
            (Window window, OutputPanel panel, _) = Open(Fill);

            TextBlock plain = Shown(panel, "Opened level one").ShouldHaveSingleItem();
            TextBlock placed = Shown(panel, "Spinner turns on a script").ShouldHaveSingleItem();

            Left(plain, window).ShouldBe(Left(placed, window), 0.01, "both texts start in the same column");
            plain.Bounds.Height.ShouldBe(placed.Bounds.Height);
            plain.FontSize.ShouldBe(placed.FontSize);
            plain.GetVisualAncestors().OfType<Button>().ShouldBeEmpty("a line with no place opens nothing");
            placed.GetVisualAncestors().OfType<Button>().ShouldHaveSingleItem().Focusable.ShouldBeTrue();

            // Rows are as far apart with a place as without one.
            TextBlock gap = Shown(panel, "6 console line(s) are missing here: more were printed than the editor kept up with.")
                .ShouldHaveSingleItem();
            TextBlock notice = Shown(panel, DroppedNotice).ShouldHaveSingleItem();
            double plainStep = Top(gap, window) - Top(notice, window);
            double placedStep = Top(placed, window) - Top(plain, window);
            plainStep.ShouldBe(placedStep, 0.01);

            window.Close();
        });
    }

    [Fact]
    public void A_click_on_a_line_or_on_a_call_asks_for_its_place_and_a_plain_line_asks_for_nothing()
    {
        session.On(() =>
        {
            (Window window, OutputPanel panel, ShellModel model) = Open(Fill);
            var asked = new List<ConsolePlace>();
            model.Output.PlaceRequested += asked.Add;

            ContentPanelHarness.Click(window, Shown(panel, "Spinner turns on a script").Single());
            ContentPanelHarness.Click(window, Shown(panel, "door.luau:30 in press").Single());
            ContentPanelHarness.Click(window, Shown(panel, "Opened level one").Single());

            asked.ShouldBe([new ConsolePlace(Spinner, 5), new ConsolePlace(Door, 30, "press")]);
            window.Close();
        });
    }

    [Fact]
    public void The_rest_of_a_long_stack_shows_on_a_click_and_hides_on_the_next()
    {
        session.On(() =>
        {
            (Window window, OutputPanel panel, _) = Open(Fill);

            ContentPanelHarness.Click(window, Shown(panel, "Show 3 more calls").Single());

            Shown(panel, "door.luau:77 in step4").ShouldHaveSingleItem();
            Shown(panel, "door.luau:79").ShouldHaveSingleItem("the script's top level has no function name");
            ScriptSheet.Save(window, "output-script-stack-open@2x.png");

            ContentPanelHarness.Click(window, Shown(panel, "Show fewer calls").Single());

            Shown(panel, "door.luau:77 in step4").ShouldBeEmpty();
            window.Close();
        });
    }

    [Fact]
    public void Tab_lands_on_the_newest_row_that_opens_something_and_the_arrows_walk_the_rows()
    {
        session.On(() =>
        {
            (Window window, OutputPanel panel, ShellModel model) = Open(Fill);
            var asked = new List<ConsolePlace>();
            model.Output.PlaceRequested += asked.Add;
            Button clear = panel.GetVisualDescendants().OfType<Button>().First(button => button.Classes.Contains("ghost"));

            clear.Focus(NavigationMethod.Tab);
            window.KeyPressQwerty(PhysicalKey.Tab, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();

            Focused(window).ShouldBe(DroppedNotice, "the dropped lines notice is the newest line with a place");

            window.KeyPressQwerty(PhysicalKey.ArrowUp, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();
            Focused(window).ShouldBe("Show 3 more calls");

            window.KeyPressQwerty(PhysicalKey.ArrowUp, RawInputModifiers.None);
            window.KeyPressQwerty(PhysicalKey.ArrowUp, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();
            Focused(window).ShouldBe("door.luau:44 in open");
            ScriptSheet.Save(window, "output-script-row-focus@2x.png");

            window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();
            asked.ShouldBe([new ConsolePlace(Door, 44, "open")]);

            // One stop for the whole list: the next Tab leaves it.
            window.KeyPressQwerty(PhysicalKey.Tab, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();
            (window.FocusManager?.GetFocusedElement() as Visual)?.GetVisualAncestors().OfType<ItemsControl>()
                .ShouldBeEmpty("Tab must not walk five hundred rows");

            window.Close();
        });
    }

    [Fact]
    public void The_pointer_over_a_row_that_opens_something_lights_it_as_the_editors_other_rows()
    {
        session.On(() =>
        {
            (Window window, OutputPanel panel, _) = Open(Fill);
            TextBlock text = Shown(panel, "the door has no hinge").Single();
            Button row = text.GetVisualAncestors().OfType<Button>().Single();

            Point over = text.TranslatePoint(new Point(4, 4), window).ShouldNotBeNull();
            window.MouseMove(over, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();

            row.IsPointerOver.ShouldBeTrue();
            ToolTip.GetTip(row).ShouldBe("Open Scripts/level one/door.luau at line 9");

            // What a person sees of it: the fill, and the place gets brighter.
            Face(row).Background.ShouldBe(Token("SpectraRowHover"));
            Shown(panel, "door.luau:9").Single().Foreground.ShouldBe(Token("SpectraTextEmphasis"));

            TextBlock away = Shown(panel, "spinner.luau:5").Single();
            Face(away.GetVisualAncestors().OfType<Button>().Single()).Background.ShouldBe(Brushes.Transparent);
            away.Foreground.ShouldBe(Token("SpectraTextMuted"));

            ScriptSheet.Save(window, "output-script-row-hover@2x.png");
            window.Close();
        });
    }

    [Fact]
    public void In_a_narrow_panel_the_text_wraps_and_the_place_keeps_its_room()
    {
        session.On(() =>
        {
            (Window window, OutputPanel panel, _) = Open(Fill, width: 380);

            TextBlock place = Shown(panel, "flood.luau:3").ShouldHaveSingleItem();
            TextBlock notice = Shown(panel, DroppedNotice).ShouldHaveSingleItem();

            place.Bounds.Width.ShouldBeGreaterThan(40);
            notice.Bounds.Height.ShouldBeGreaterThan(place.Bounds.Height * 1.5, "the notice wraps");
            (Left(place, window) + place.Bounds.Width).ShouldBeLessThanOrEqualTo(380);

            ScriptSheet.Save(window, "output-script-narrow@2x.png");
            window.Close();
        });
    }

    [Fact]
    public void The_console_panel_shows_the_same_log_with_the_same_rows()
    {
        session.On(() =>
        {
            var model = new ShellModel();
            Fill(model.Output);
            var asked = new List<ConsolePlace>();
            model.Output.PlaceRequested += asked.Add;

            var panel = new ConsolePanel { DataContext = model };
            var window = new Window { Content = panel, Width = 760, Height = 320 };
            window.SetRenderScaling(2.0);
            window.Show();
            Dispatcher.UIThread.RunJobs();

            Shown(panel, "spinner.luau:5").ShouldHaveSingleItem();
            Shown(panel, "door.luau:30 in press").ShouldHaveSingleItem();
            Shown(panel, "Opened level one").ShouldHaveSingleItem();

            ContentPanelHarness.Click(window, Shown(panel, "the door has no hinge").Single());

            asked.ShouldBe([new ConsolePlace(Door, 9)]);
            ScriptSheet.Save(window, "output-script-console@2x.png");
            window.Close();
        });
    }

    private const string DroppedNotice =
        "150 more lines were printed in this tick and dropped. Scripts may print 100 lines a tick.";

    // What a level with three scripts leaves in Output: a line from the shell,
    // a print, a print said twelve times, a warning, an error five calls deep,
    // and the two notices about lines that are not there.
    private static void Fill(OutputLog output)
    {
        output.Append(OutputSeverity.Info, "Opened level one");
        output.Append(OutputSeverity.Info, "Spinner turns on a script", new ConsolePlace(Spinner, 5), NoCalls);

        for (int i = 0; i < 12; i++)
            output.Append(OutputSeverity.Info, "the door is closed", new ConsolePlace(Door, 4), NoCalls);

        output.Append(OutputSeverity.Warning, "the door has no hinge", new ConsolePlace(Door, 9), NoCalls);
        output.Append(
            OutputSeverity.Error, "attempt to index nil with 'Name'", new ConsolePlace(Door, 12),
            [
                new ConsolePlace(Door, 30, "press"),
                new ConsolePlace(Door, 44, "open"),
                new ConsolePlace("Scripts/shared/Timer.luau", 8, "tick"),
                new ConsolePlace(Door, 77, "step4"),
                new ConsolePlace(Door, 78, "step5"),
                new ConsolePlace(Door, 79),
            ]);
        output.Append(OutputSeverity.Warning, DroppedNotice, new ConsolePlace("Scripts/flood.luau", 3), NoCalls);
        output.Append(
            OutputSeverity.Warning,
            "6 console line(s) are missing here: more were printed than the editor kept up with.");
        output.Append(OutputSeverity.Command, "> ent_list");
    }

    private static (Window Window, OutputPanel Panel, ShellModel Model) Open(Action<OutputLog> fill, double width = 760)
    {
        var model = new ShellModel();
        fill(model.Output);

        var panel = new OutputPanel { DataContext = model };
        var window = new Window { Content = panel, Width = width, Height = 320 };
        window.SetRenderScaling(2.0);
        window.Show();
        Dispatcher.UIThread.RunJobs();

        return (window, panel, model);
    }

    // The part of a row that is filled when the pointer is over it.
    private static ContentPresenter Face(Button row) =>
        row.GetVisualDescendants().OfType<ContentPresenter>().First(part => part.Name == "PART_ContentPresenter");

    private static IBrush Token(string key) =>
        Application.Current.ShouldNotBeNull().TryFindResource(key, out object? value) && value is IBrush brush
            ? brush
            : throw new InvalidOperationException($"The theme has no brush named {key}.");

    private static TextBlock[] Shown(Visual within, string text) =>
        [.. ScriptSheet.Shown<TextBlock>(within).Where(block => block.Text == text)];

    private static double Left(Visual visual, Visual window) =>
        visual.TranslatePoint(default, window).ShouldNotBeNull().X;

    private static double Right(Visual visual, Visual window) => Left(visual, window) + visual.Bounds.Width;

    private static double Top(Visual visual, Visual window) =>
        visual.TranslatePoint(default, window).ShouldNotBeNull().Y;

    // The words on the row that has the keyboard.
    private static string? Focused(Window window) =>
        (window.FocusManager?.GetFocusedElement() as Visual)?.GetVisualDescendants().OfType<TextBlock>()
            .Where(block => block.Classes.Contains("line") || block.Classes.Contains("call"))
            .Select(block => block.Text)
            .FirstOrDefault();
}
