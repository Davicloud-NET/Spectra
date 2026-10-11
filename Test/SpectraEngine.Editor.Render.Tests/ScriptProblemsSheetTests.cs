using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;

using Microsoft.Extensions.Logging;

using SpectraEngine.Core.ConsoleSystem;
using SpectraEngine.Core.Hosting;
using SpectraEngine.Editor.Shell;

namespace SpectraEngine.Editor.Render.Tests;

/// <summary>
/// The Problems panel with scripts that did not start in it, rasterised so a
/// person can look at it, and the double click that goes to a script's node.
/// </summary>
[Collection(RibbonSessionCollection.Name)]
public sealed class ScriptProblemsSheetTests(RibbonSession session)
{
    private const string Mistake =
        "Expected identifier when parsing variable name, got '='. The script on 'Door' did not start.";

    private const string Missing =
        "'Lamp' names a script file that is not in the level's content, so it did not start.";

    [Fact]
    public void A_script_that_does_not_compile_and_one_with_no_file_are_rows_that_go_to_their_node()
    {
        session.On(() =>
        {
            Guid door = Guid.NewGuid();
            Guid lamp = Guid.NewGuid();
            var model = new ShellModel();
            new ScriptStartProblems(model.Problems).Apply(new FrameSnapshot
            {
                IsPlaying = true,
                ConsoleLines =
                [
                    new ConsoleLine(0, LogLevel.Error, Mistake)
                    {
                        Place = new ConsolePlace("Scripts/door.luau", 2),
                        ProblemNode = door,
                    },
                    new ConsoleLine(1, LogLevel.Error, Missing)
                    {
                        Place = new ConsolePlace("Scripts/nowhere.luau", 0),
                        ProblemNode = lamp,
                    },
                ],
            });

            var panel = new ProblemsPanel { DataContext = model };
            var activated = new List<Guid>();
            panel.EntryActivated += entry => activated.Add(entry.NodeId);
            var window = new Window { Content = panel, Width = 760, Height = 160 };
            window.SetRenderScaling(2.0);
            window.Show();
            Dispatcher.UIThread.RunJobs();

            model.Problems.Summary.ShouldBe("2 errors");
            TextBlock mistake = Row(panel, "Scripts/door.luau:2: " + Mistake);
            TextBlock missing = Row(panel, "Scripts/nowhere.luau: " + Missing);

            ContentPanelHarness.Click(window, missing, times: 2);
            ContentPanelHarness.Click(window, mistake, times: 2);

            activated.ShouldBe([lamp, door]);
            ScriptSheet.Save(window, "output-script-problems@2x.png");
            window.Close();
        });
    }

    [Fact]
    public void One_broken_file_on_two_nodes_is_one_row_that_goes_to_the_node_it_names()
    {
        session.On(() =>
        {
            Guid door = Guid.NewGuid();
            var model = new ShellModel();
            new ScriptStartProblems(model.Problems).Apply(new FrameSnapshot
            {
                IsPlaying = true,
                ConsoleLines =
                [
                    new ConsoleLine(0, LogLevel.Error, Mistake)
                    {
                        Place = new ConsolePlace("Scripts/door.luau", 2),
                        ProblemNode = door,
                    },
                    new ConsoleLine(1, LogLevel.Error, Mistake.Replace("'Door'", "'Gate'"))
                    {
                        Place = new ConsolePlace("Scripts/door.luau", 2),
                        ProblemNode = Guid.NewGuid(),
                    },
                ],
            });

            var panel = new ProblemsPanel { DataContext = model };
            var activated = new List<Guid>();
            panel.EntryActivated += entry => activated.Add(entry.NodeId);
            var window = new Window { Content = panel, Width = 760, Height = 160 };
            window.SetRenderScaling(2.0);
            window.Show();
            Dispatcher.UIThread.RunJobs();

            model.Problems.Summary.ShouldBe("1 error");
            TextBlock row = Row(panel, "Scripts/door.luau:2: " + Mistake + " The same goes for 1 more node.");

            ContentPanelHarness.Click(window, row, times: 2);

            activated.ShouldBe([door]);
            ScriptSheet.Save(window, "output-script-problems-shared@2x.png");
            window.Close();
        });
    }

    private static TextBlock Row(ProblemsPanel panel, string message) =>
        ScriptSheet.Shown<TextBlock>(panel).Single(block => block.Text == message);
}
