using SpectraEngine.Editor.Shell;
using System;

namespace SpectraEngine.Editor.Tests;

/// <summary>
/// Rows for a condition that is found by looking at the scene: the list is
/// made to match each fresh answer.
/// </summary>
public sealed class ProblemListReplaceTests
{
    private const string Template = "An entity made from geometry still has world geometry in it";
    private const string Other = "Material {Path} is unreadable";

    private static int Replace(ProblemList problems, params ProblemRow[] rows) =>
        problems.Replace(OutputSeverity.Warning, Template, ProblemScope.Map, rows);

    [Fact]
    public void A_fresh_answer_becomes_rows_that_select_their_node()
    {
        var problems = new ProblemList();
        Guid brush = Guid.NewGuid();

        Assert.Equal(1, Replace(problems, new ProblemRow("door1", "door1 is still a block.", brush)));

        ProblemEntry entry = Assert.Single(problems.Entries);
        Assert.Equal("door1 is still a block.", entry.Message);
        Assert.Equal(brush, entry.NodeId);
        Assert.True(entry.HasNode);
        Assert.Equal(ProblemScope.Map, entry.Scope);
        Assert.Equal("1 warning", problems.Summary);
    }

    [Fact]
    public void A_condition_that_still_stands_keeps_its_row_and_is_not_counted_again()
    {
        // The scene is asked after every edit. A count would only say how
        // many edits there were.
        var problems = new ProblemList();
        Guid brush = Guid.NewGuid();
        Replace(problems, new ProblemRow("door1", "door1 is still a block.", brush));
        ProblemEntry first = problems.Entries[0];

        Assert.Equal(0, Replace(problems, new ProblemRow("door1", "door1 is still a block.", brush)));

        Assert.Same(first, Assert.Single(problems.Entries));
        Assert.Equal(1, first.Count);
        Assert.Equal(string.Empty, first.CountLabel);
    }

    [Fact]
    public void A_condition_that_is_gone_takes_its_row_with_it()
    {
        var problems = new ProblemList();
        Replace(problems,
            new ProblemRow("door1", "a", Guid.NewGuid()),
            new ProblemRow("door2", "b", Guid.NewGuid()));

        Assert.Equal(1, Replace(problems, new ProblemRow("door2", "b", problems.Entries[1].NodeId)));

        Assert.Equal("door2", Assert.Single(problems.Entries).Subject);

        Assert.Equal(1, Replace(problems));
        Assert.False(problems.HasProblems);
        Assert.Equal("no problems", problems.Summary);
    }

    [Fact]
    public void Rows_of_other_templates_are_not_touched()
    {
        var problems = new ProblemList();
        problems.Report(OutputSeverity.Warning, Other, "unreadable", "Materials/a.spectramat");
        Replace(problems, new ProblemRow("door1", "a", Guid.NewGuid()));

        Replace(problems);

        Assert.Equal(Other, Assert.Single(problems.Entries).Template);
    }

    [Fact]
    public void The_same_name_on_another_node_is_a_new_row_for_that_node()
    {
        // Two doors may share a name. Fixing the first must not leave the row
        // pointing at it.
        var problems = new ProblemList();
        Guid first = Guid.NewGuid();
        Guid second = Guid.NewGuid();
        Replace(problems, new ProblemRow("door", "a", first), new ProblemRow("door", "a", second));
        Assert.Equal(first, Assert.Single(problems.Entries).NodeId);

        Replace(problems, new ProblemRow("door", "a", second));

        Assert.Equal(second, Assert.Single(problems.Entries).NodeId);
    }

    [Fact]
    public void A_standing_row_takes_the_newer_wording()
    {
        var problems = new ProblemList();
        Guid brush = Guid.NewGuid();
        Replace(problems, new ProblemRow("lift", "'Rail' inside it is still a block.", brush));

        Replace(problems, new ProblemRow("lift", "'Handrail' inside it is still a block.", brush));

        Assert.Equal("'Handrail' inside it is still a block.", Assert.Single(problems.Entries).Message);
    }

    [Fact]
    public void A_dismissed_row_comes_back_while_the_condition_stands()
    {
        var problems = new ProblemList();
        Guid brush = Guid.NewGuid();
        Replace(problems, new ProblemRow("door1", "a", brush));
        problems.Remove(problems.Entries[0]);

        Assert.Equal(1, Replace(problems, new ProblemRow("door1", "a", brush)));

        Assert.Single(problems.Entries);
    }
}
