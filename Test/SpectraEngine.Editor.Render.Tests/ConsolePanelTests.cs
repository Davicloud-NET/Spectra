using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;

using SpectraEngine.Core.ConsoleSystem;
using SpectraEngine.Editor.Shell;

namespace SpectraEngine.Editor.Render.Tests;

/// <summary>
/// The Console panel over a log with lines that open something: when its
/// view follows the newest line, and where the keyboard is after a click.
/// </summary>
[Collection(RibbonSessionCollection.Name)]
public sealed class ConsolePanelTests(RibbonSession session)
{
    private const string Door = "Scripts/door.luau";

    [Fact]
    public void A_click_on_an_older_line_leaves_the_view_where_it_is()
    {
        session.On(() =>
        {
            (Window window, ConsolePanel panel, ShellModel model) = Open();
            ScrollViewer scroller = Scroller(panel);

            // What a click leads to: the open says what it did, in this log.
            model.Output.PlaceRequested += place => model.SetMessage($"Opened {place.File}.");
            ScrollTo(scroller, 40);

            ContentPanelHarness.Click(window, Shown(panel, "line 6"));
            Dispatcher.UIThread.RunJobs();

            model.Output.Entries[^1].Text.ShouldBe("Opened Scripts/door.luau.");
            scroller.Offset.Y.ShouldBe(40, "the line that was clicked is still under the pointer");
            window.Close();
        });
    }

    [Fact]
    public void The_view_follows_the_newest_line_while_it_shows_the_newest_line()
    {
        session.On(() =>
        {
            (Window window, ConsolePanel panel, ShellModel model) = Open();
            ScrollViewer scroller = Scroller(panel);
            AtEnd(scroller).ShouldBeTrue("a console that comes up shows its newest line");

            model.Output.Append(OutputSeverity.Info, "one more", new ConsolePlace(Door, 3), []);
            Dispatcher.UIThread.RunJobs();

            AtEnd(scroller).ShouldBeTrue();
            window.Close();
        });
    }

    [Fact]
    public void A_view_somebody_scrolled_up_stays_until_a_line_is_typed()
    {
        session.On(() =>
        {
            (Window window, ConsolePanel panel, ShellModel model) = Open();
            ScrollViewer scroller = Scroller(panel);
            ScrollTo(scroller, 0);

            model.Output.Append(OutputSeverity.Info, "the level prints on", new ConsolePlace(Door, 3), []);
            Dispatcher.UIThread.RunJobs();
            scroller.Offset.Y.ShouldBe(0);

            // The window appends what was typed before it runs it.
            model.Output.Append(OutputSeverity.Command, "> ent_list");
            model.Output.Append(OutputSeverity.Info, "12 entities");
            Dispatcher.UIThread.RunJobs();

            AtEnd(scroller).ShouldBeTrue("the answer to a command is at the end");
            window.Close();
        });
    }

    [Fact]
    public void A_click_on_a_line_leaves_the_keyboard_in_the_input_line()
    {
        session.On(() =>
        {
            (Window window, ConsolePanel panel, ShellModel model) = Open();
            var asked = new List<ConsolePlace>();
            model.Output.PlaceRequested += asked.Add;
            panel.FocusInput();
            Dispatcher.UIThread.RunJobs();
            ScrollTo(Scroller(panel), 0);

            ContentPanelHarness.Click(window, Shown(panel, "line 3"));

            asked.ShouldBe([new ConsolePlace(Door, 4)]);
            window.FocusManager.ShouldNotBeNull().GetFocusedElement()
                .ShouldBeOfType<TextBox>("the next key is typed, not sent to a row");
            window.Close();
        });
    }

    // A console over eighty lines that each open something, more than it shows.
    private static (Window Window, ConsolePanel Panel, ShellModel Model) Open()
    {
        var model = new ShellModel();
        for (int i = 0; i < 80; i++)
            model.Output.Append(OutputSeverity.Info, $"line {i}", new ConsolePlace(Door, i + 1), []);

        var panel = new ConsolePanel { DataContext = model };
        var window = new Window { Content = panel, Width = 760, Height = 320 };
        window.SetRenderScaling(2.0);
        window.Show();
        Dispatcher.UIThread.RunJobs();

        return (window, panel, model);
    }

    private static ScrollViewer Scroller(ConsolePanel panel) =>
        panel.GetVisualDescendants().OfType<ScrollViewer>().First(scroller => scroller.Name == "Scroller");

    private static void ScrollTo(ScrollViewer scroller, double y)
    {
        scroller.Offset = new Vector(0, y);
        Dispatcher.UIThread.RunJobs();
    }

    private static bool AtEnd(ScrollViewer scroller) =>
        scroller.Extent.Height > scroller.Viewport.Height
        && scroller.Offset.Y >= scroller.Extent.Height - scroller.Viewport.Height - 1;

    private static TextBlock Shown(ConsolePanel panel, string text) =>
        ScriptSheet.Shown<TextBlock>(panel).Single(block => block.Text == text);
}
