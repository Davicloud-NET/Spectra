using System.Text.RegularExpressions;

namespace SpectraEngine.Editor.Tests;

/// <summary>
/// Arranging the view panes must never move a control to another parent.
/// </summary>
// A native viewport is a child window, and re-parenting it destroys the engine
// session. Nothing fails when that happens in a composited session, so the
// rule is checked against the sources. This project has no Avalonia.
public sealed class ViewPaneConventionTests
{
    private static readonly Regex Reparenting = new(
        @"Children\s*\.\s*(Add|Insert|Remove|Clear|Move)|\.\s*Child\s*=(?!=)",
        RegexOptions.CultureInvariant);

    [Fact]
    public void Arranging_the_view_panes_adds_removes_and_moves_no_child()
    {
        var offenders = new List<string>();

        // Split on "\n" and trim: a checkout can be LF or CRLF.
        string[] lines = Source().Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i].Trim();
            if (line.StartsWith("//", StringComparison.Ordinal)) continue;

            if (Reparenting.IsMatch(line))
                offenders.Add($"MainWindow.ViewPanes.cs({i + 1}): {line}");
        }

        offenders.ShouldBeEmpty(
            "an arrangement is track lengths, cells and visibility; a control that changes parent " +
            "takes a native viewport's window, and the engine session, with it");
    }

    [Fact]
    public void The_file_that_is_checked_is_the_one_that_arranges_the_panes()
    {
        // Without this the guard above passes over a file that moved.
        string source = Source();

        source.ShouldContain("void ApplyViewArrangement(");
        source.ShouldContain("Grid.SetColumn(");
        source.ShouldContain("IsVisible =");
    }

    [Theory]
    [InlineData("ViewPanes.Children.Add(pane);")]
    [InlineData("EditorView.Children.Insert(0, ViewPanes);")]
    [InlineData("ViewPanes.Children.Remove(LogicPane);")]
    [InlineData("_viewportDockHost.Child = ViewPanes;")]
    [InlineData("host.Child=null;")]
    public void The_guard_knows_a_re_parenting_line_when_it_sees_one(string line)
    {
        Reparenting.IsMatch(line).ShouldBeTrue();
    }

    [Theory]
    [InlineData("LogicPane.IsVisible = grid.ShowsLogic;")]
    [InlineData("if (host.Child == ViewPanes) return;")]
    [InlineData("Grid.SetColumn(LogicPane, grid.Logic.Column);")]
    public void The_guard_leaves_layout_lines_alone(string line)
    {
        Reparenting.IsMatch(line).ShouldBeFalse();
    }

    private static string Source() => File.ReadAllText(
        Path.Combine(SourceRoot(), "SpectraEngine.Editor", "MainWindow.ViewPanes.cs"));

    private static string SourceRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (dir.EnumerateFiles("*.slnx").Any() || dir.EnumerateFiles("*.sln").Any())
                return dir.FullName;

            dir = dir.Parent;
        }

        throw new InvalidOperationException("could not find the solution root above the test binary");
    }
}
