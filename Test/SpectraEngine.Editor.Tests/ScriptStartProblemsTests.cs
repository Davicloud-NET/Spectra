using Microsoft.Extensions.Logging;
using SpectraEngine.Core.ConsoleSystem;
using SpectraEngine.Core.Hosting;
using SpectraEngine.Editor.Shell;

namespace SpectraEngine.Editor.Tests;

/// <summary>
/// The Problems panel's rows about scripts that did not start: one that does
/// not compile and one whose file is missing, each with its node, and gone
/// once the level is played again and the script starts.
/// </summary>
public sealed class ScriptStartProblemsTests
{
    private const string Door = "Scripts/door.luau";

    private readonly ProblemList _problems = new();
    private readonly ScriptStartProblems _feed;

    public ScriptStartProblemsTests() => _feed = new ScriptStartProblems(_problems);

    [Fact]
    public void A_script_that_does_not_compile_is_a_problem_of_its_node_with_file_and_line()
    {
        Guid node = Guid.NewGuid();

        _feed.Apply(Playing(DidNotStart(
            node, "Expected identifier, got '='. The script on 'Door' did not start.", new ConsolePlace(Door, 2))));

        ProblemEntry entry = _problems.Entries.ShouldHaveSingleItem();
        entry.Severity.ShouldBe(OutputSeverity.Error);
        entry.Message.ShouldBe("Scripts/door.luau:2: Expected identifier, got '='. The script on 'Door' did not start.");
        entry.Subject.ShouldBe(Door);
        entry.NodeId.ShouldBe(node);
        entry.HasNode.ShouldBeTrue("a double click selects the node");
        entry.Scope.ShouldBe(ProblemScope.Map);
    }

    [Fact]
    public void A_missing_script_file_is_a_problem_of_its_node()
    {
        Guid node = Guid.NewGuid();

        _feed.Apply(Playing(DidNotStart(
            node,
            "'Lamp' names a script file that is not in the level's content, so it did not start.",
            new ConsolePlace("Scripts/nowhere.luau", 0))));

        ProblemEntry entry = _problems.Entries.ShouldHaveSingleItem();
        entry.Message.ShouldBe(
            "Scripts/nowhere.luau: 'Lamp' names a script file that is not in the level's content, so it did not start.");
        entry.NodeId.ShouldBe(node);
    }

    [Fact]
    public void A_print_and_an_error_of_a_running_script_are_no_problems()
    {
        _feed.Apply(Playing(
            new ConsoleLine(0, LogLevel.Information, "hello") { Place = new ConsolePlace(Door, 3) },
            new ConsoleLine(1, LogLevel.Error, "it broke") { Place = new ConsolePlace(Door, 12) },
            new ConsoleLine(2, LogLevel.Error, "no command named 'noclip'. Type help.")));

        _problems.HasProblems.ShouldBeFalse();
    }

    [Fact]
    public void A_script_that_was_fixed_loses_its_row_when_the_level_is_played_again()
    {
        Guid door = Guid.NewGuid();
        Guid lamp = Guid.NewGuid();
        _feed.Apply(Playing(
            DidNotStart(door, "The script on 'Door' did not start.", new ConsolePlace(Door, 2)),
            DidNotStart(lamp, "The script on 'Lamp' did not start.", new ConsolePlace("Scripts/lamp.luau", 7))));
        _problems.Entries.Count.ShouldBe(2);

        // Stop leaves the rows: the scripts are as wrong as they were.
        _feed.Apply(Stopped());
        _problems.Entries.Count.ShouldBe(2);

        // The door's script was fixed. The lamp's says the same again.
        _feed.Apply(Playing(
            DidNotStart(lamp, "The script on 'Lamp' did not start.", new ConsolePlace("Scripts/lamp.luau", 7))));

        ProblemEntry left = _problems.Entries.ShouldHaveSingleItem();
        left.NodeId.ShouldBe(lamp);
        left.Count.ShouldBe(1);
    }

    [Fact]
    public void A_level_that_starts_with_every_script_running_clears_the_rows()
    {
        _feed.Apply(Playing(DidNotStart(Guid.NewGuid(), "It did not start.", new ConsolePlace(Door, 2))));
        _feed.Apply(Stopped());

        _feed.Apply(Playing());

        _problems.HasProblems.ShouldBeFalse();
    }

    [Fact]
    public void A_level_that_keeps_playing_keeps_its_rows()
    {
        _feed.Apply(Playing(DidNotStart(Guid.NewGuid(), "It did not start.", new ConsolePlace(Door, 2))));

        _feed.Apply(Playing());
        _feed.Apply(Playing());

        _problems.Entries.ShouldHaveSingleItem();
    }

    [Fact]
    public void Other_problems_of_the_level_stay_when_it_is_played_again()
    {
        _problems.Report(
            OutputSeverity.Warning, "Map: a mesh node loaded without its model",
            "Signpost loaded without its model and draws nothing.", "Signpost", ProblemScope.Map);
        _feed.Apply(Playing(DidNotStart(Guid.NewGuid(), "It did not start.", new ConsolePlace(Door, 2))));
        _feed.Apply(Stopped());

        _feed.Apply(Playing());

        _problems.Entries.ShouldHaveSingleItem().Subject.ShouldBe("Signpost");
    }

    [Fact]
    public void One_broken_file_on_two_nodes_is_one_row_that_counts_both()
    {
        Guid first = Guid.NewGuid();

        _feed.Apply(Playing(
            DidNotStart(first, "The script on 'Spin' did not start.", new ConsolePlace(Door, 2)),
            DidNotStart(Guid.NewGuid(), "The script on 'Spin' did not start.", new ConsolePlace(Door, 2))));

        ProblemEntry entry = _problems.Entries.ShouldHaveSingleItem();
        entry.CountLabel.ShouldBe("x2");
        entry.NodeId.ShouldBe(first);
    }

    [Fact]
    public void After_a_new_engine_the_first_play_still_clears_what_the_last_one_left()
    {
        _feed.Apply(Playing(DidNotStart(Guid.NewGuid(), "It did not start.", new ConsolePlace(Door, 2))));

        // The engine died while the level played. The next one starts stopped.
        _feed.Reset();
        _problems.Entries.ShouldHaveSingleItem();

        _feed.Apply(Playing());

        _problems.HasProblems.ShouldBeFalse();
    }

    private static ConsoleLine DidNotStart(Guid node, string text, ConsolePlace place) =>
        new(0, LogLevel.Error, text) { Place = place, ProblemNode = node };

    private static FrameSnapshot Playing(params ConsoleLine[] lines) =>
        new() { IsPlaying = true, ConsoleLines = lines };

    private static FrameSnapshot Stopped() => new() { IsPlaying = false };
}
