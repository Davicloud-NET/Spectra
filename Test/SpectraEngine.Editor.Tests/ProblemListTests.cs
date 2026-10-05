using SpectraEngine.Editor.Shell;

namespace SpectraEngine.Editor.Tests;

/// <summary>
/// The list of standing problems: a row stays until something resolves it.
/// </summary>
public sealed class ProblemListTests
{
    private const string Template = "Material {Path} is unreadable";

    [Fact]
    public void An_empty_list_says_so_and_says_nothing_else()
    {
        var problems = new ProblemList();

        Assert.False(problems.HasProblems);
        Assert.Equal("no problems", problems.Summary);
    }

    [Fact]
    public void A_repeat_is_one_row_with_a_count_and_a_moving_last_seen()
    {
        var problems = new ProblemList();

        ProblemEntry? first = problems.Report(
            OutputSeverity.Warning, Template, "Materials/a is unreadable", "Materials/a.spectramat");
        problems.Report(
            OutputSeverity.Warning, Template, "Materials/a is unreadable", "Materials/a.spectramat");

        ProblemEntry only = Assert.Single(problems.Entries);
        Assert.Same(first, only);
        Assert.Equal(2, only.Count);
        Assert.Equal("x2", only.CountLabel);

        Assert.Equal(1, problems.WarningCount);
        Assert.Equal("1 warning", problems.Summary);
    }

    [Fact]
    public void The_same_template_about_two_files_is_two_rows()
    {
        var problems = new ProblemList();

        problems.Report(OutputSeverity.Warning, Template, "a", "Materials/a.spectramat");
        problems.Report(OutputSeverity.Warning, Template, "b", "Materials/b.spectramat");

        Assert.Equal(2, problems.Entries.Count);
        Assert.Equal("2 warnings", problems.Summary);
    }

    [Fact]
    public void The_same_subject_at_two_severities_is_two_rows()
    {
        var problems = new ProblemList();

        problems.Report(OutputSeverity.Warning, Template, "a", "Materials/a.spectramat");
        problems.Report(OutputSeverity.Error, Template, "a", "Materials/a.spectramat");

        Assert.Equal(2, problems.Entries.Count);
        Assert.Equal("1 error, 1 warning", problems.Summary);
    }

    [Fact]
    public void Info_records_nothing()
    {
        var problems = new ProblemList();

        Assert.Null(problems.Report(OutputSeverity.Info, "Loaded {Path}", "loaded", "Textures/a.png"));
        Assert.Null(problems.Report(OutputSeverity.Command, "> grid 1", "> grid 1"));

        Assert.False(problems.HasProblems);
    }

    [Fact]
    public void Resolve_removes_every_row_for_the_subject()
    {
        var problems = new ProblemList();
        problems.Report(OutputSeverity.Warning, Template, "a", "Materials/a.spectramat");
        problems.Report(OutputSeverity.Error, "Texture {Path} failed", "t", "Materials/a.spectramat");
        problems.Report(OutputSeverity.Warning, Template, "b", "Materials/b.spectramat");

        Assert.Equal(2, problems.Resolve("Materials/a.spectramat"));

        ProblemEntry left = Assert.Single(problems.Entries);
        Assert.Equal("Materials/b.spectramat", left.Subject);
        Assert.Equal("1 warning", problems.Summary);
    }

    [Fact]
    public void Resolve_with_a_template_removes_only_that_condition_for_the_subject()
    {
        var problems = new ProblemList();
        problems.Report(OutputSeverity.Warning, Template, "a", "Sounds/a.wav");
        problems.Report(OutputSeverity.Warning, "Sound {Path} was not played", "p", "Sounds/a.wav");
        problems.Report(OutputSeverity.Warning, "Sound {Path} was not played", "p", "Sounds/b.wav");

        Assert.Equal(1, problems.Resolve("Sounds/a.wav", "Sound {Path} was not played"));

        Assert.Equal(2, problems.Entries.Count);
        Assert.Contains(problems.Entries, entry => entry.Subject == "Sounds/a.wav" && entry.Template == Template);
        Assert.Contains(problems.Entries, entry => entry.Subject == "Sounds/b.wav");
        Assert.Equal("2 warnings", problems.Summary);
    }

    [Fact]
    public void Resolving_something_that_was_never_wrong_does_nothing()
    {
        var problems = new ProblemList();
        problems.Report(OutputSeverity.Warning, Template, "a", "Materials/a.spectramat");

        Assert.Equal(0, problems.Resolve("Textures/unrelated.png"));
        Assert.Single(problems.Entries);
    }

    [Fact]
    public void A_resolved_condition_that_comes_back_is_a_fresh_row()
    {
        // The count restarts at one.
        var problems = new ProblemList();
        problems.Report(OutputSeverity.Warning, Template, "a", "Materials/a.spectramat");
        problems.Resolve("Materials/a.spectramat");

        ProblemEntry? again = problems.Report(
            OutputSeverity.Warning, Template, "a", "Materials/a.spectramat");

        Assert.NotNull(again);
        Assert.Equal(1, again.Count);
        Assert.Single(problems.Entries);
    }

    [Fact]
    public void Clearing_one_scope_leaves_the_others_alone()
    {
        var problems = new ProblemList();
        problems.Report(OutputSeverity.Warning, Template, "a", "a", ProblemScope.Session);
        problems.Report(OutputSeverity.Warning, Template, "b", "b", ProblemScope.Map);
        problems.Report(OutputSeverity.Error, Template, "c", "c", ProblemScope.Cook);

        Assert.Equal(1, problems.ClearScope(ProblemScope.Map));

        Assert.Equal(2, problems.Entries.Count);
        Assert.DoesNotContain(problems.Entries, e => e.Scope == ProblemScope.Map);
        Assert.Equal("1 error, 1 warning", problems.Summary);
    }

    [Fact]
    public void A_dismissed_row_goes_and_its_count_goes_with_it()
    {
        var problems = new ProblemList();
        ProblemEntry entry = problems.Report(OutputSeverity.Error, Template, "a", "a")!;
        problems.Report(OutputSeverity.Warning, Template, "b", "b");

        problems.Remove(entry);

        Assert.Single(problems.Entries);
        Assert.Equal(0, problems.ErrorCount);
        Assert.Equal("1 warning", problems.Summary);
    }

    [Fact]
    public void A_dismissed_row_comes_back_if_it_is_reported_again()
    {
        var problems = new ProblemList();
        ProblemEntry entry = problems.Report(OutputSeverity.Warning, Template, "a", "a")!;
        problems.Remove(entry);

        problems.Report(OutputSeverity.Warning, Template, "a", "a");

        Assert.Single(problems.Entries);
    }

    [Fact]
    public void Clearing_empties_the_list_and_its_counts()
    {
        var problems = new ProblemList();
        problems.Report(OutputSeverity.Error, Template, "a", "a");
        problems.Report(OutputSeverity.Warning, Template, "b", "b");

        problems.Clear();

        Assert.Empty(problems.Entries);
        Assert.Equal(0, problems.ErrorCount);
        Assert.Equal(0, problems.WarningCount);
        Assert.Equal("no problems", problems.Summary);
        Assert.False(problems.HasProblems);
    }

    [Fact]
    public void The_status_count_is_rows_rather_than_reports()
    {
        var problems = new ProblemList();
        problems.Report(OutputSeverity.Warning, Template, "a", "a");
        problems.Report(OutputSeverity.Warning, Template, "a", "a");
        Assert.Equal("1 problem", problems.CountLabel);

        problems.Report(OutputSeverity.Error, Template, "b", "b");
        Assert.Equal("2 problems", problems.CountLabel);
    }

    [Theory]
    [InlineData(0, 0, "no problems")]
    [InlineData(1, 0, "1 error")]
    [InlineData(0, 1, "1 warning")]
    [InlineData(2, 0, "2 errors")]
    [InlineData(0, 2, "2 warnings")]
    [InlineData(1, 1, "1 error, 1 warning")]
    [InlineData(2, 1, "2 errors, 1 warning")]
    [InlineData(1, 2, "1 error, 2 warnings")]
    [InlineData(3, 2, "3 errors, 2 warnings")]
    public void The_summary_reads_as_a_sentence_at_every_count(int errors, int warnings, string expected)
    {
        var problems = new ProblemList();
        for (int i = 0; i < errors; i++)
            problems.Report(OutputSeverity.Error, Template, "e", $"error{i}");
        for (int i = 0; i < warnings; i++)
            problems.Report(OutputSeverity.Warning, Template, "w", $"warning{i}");

        Assert.Equal(expected, problems.Summary);
    }
}
