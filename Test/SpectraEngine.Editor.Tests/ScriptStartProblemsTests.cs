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
    public void One_broken_file_on_two_nodes_is_one_row_that_names_the_node_it_selects()
    {
        Guid first = Guid.NewGuid();

        _feed.Apply(Playing(
            DidNotStart(first, "The script on 'Door' did not start.", new ConsolePlace(Door, 2)),
            DidNotStart(Guid.NewGuid(), "The script on 'Gate' did not start.", new ConsolePlace(Door, 2))));

        ProblemEntry entry = _problems.Entries.ShouldHaveSingleItem();
        entry.NodeId.ShouldBe(first);
        entry.Message.ShouldBe(
            "Scripts/door.luau:2: The script on 'Door' did not start. The same goes for 1 more node.");
        entry.CountLabel.ShouldBe("x2");
    }

    [Fact]
    public void Each_further_node_with_the_same_broken_file_is_counted_in_the_rows_words()
    {
        Guid first = Guid.NewGuid();
        _feed.Apply(Playing(
            DidNotStart(first, "The script on 'Door' did not start.", new ConsolePlace(Door, 2)),
            DidNotStart(Guid.NewGuid(), "The script on 'Gate' did not start.", new ConsolePlace(Door, 2))));

        // A script that starts later, as one under a clone will.
        _feed.Apply(Playing(
            DidNotStart(Guid.NewGuid(), "The script on 'Hatch' did not start.", new ConsolePlace(Door, 2))));

        ProblemEntry entry = _problems.Entries.ShouldHaveSingleItem();
        entry.NodeId.ShouldBe(first);
        entry.Message.ShouldBe(
            "Scripts/door.luau:2: The script on 'Door' did not start. The same goes for 2 more nodes.");
    }

    [Fact]
    public void The_next_play_counts_the_nodes_of_a_broken_file_again()
    {
        Guid gate = Guid.NewGuid();
        _feed.Apply(Playing(
            DidNotStart(Guid.NewGuid(), "The script on 'Door' did not start.", new ConsolePlace(Door, 2)),
            DidNotStart(gate, "The script on 'Gate' did not start.", new ConsolePlace(Door, 2))));
        _feed.Apply(Stopped());

        // The door's script was taken off its node. The gate's is as broken as it was.
        _feed.Apply(Playing(DidNotStart(gate, "The script on 'Gate' did not start.", new ConsolePlace(Door, 2))));

        ProblemEntry entry = _problems.Entries.ShouldHaveSingleItem();
        entry.NodeId.ShouldBe(gate);
        entry.Message.ShouldBe("Scripts/door.luau:2: The script on 'Gate' did not start.");
        entry.Count.ShouldBe(1);
    }

    [Fact]
    public void A_row_somebody_dismissed_comes_back_with_the_words_of_the_node_that_says_it_next()
    {
        Guid hatch = Guid.NewGuid();
        _feed.Apply(Playing(
            DidNotStart(Guid.NewGuid(), "The script on 'Door' did not start.", new ConsolePlace(Door, 2))));
        _problems.Remove(_problems.Entries[0]);

        _feed.Apply(Playing(DidNotStart(hatch, "The script on 'Hatch' did not start.", new ConsolePlace(Door, 2))));

        ProblemEntry entry = _problems.Entries.ShouldHaveSingleItem();
        entry.NodeId.ShouldBe(hatch);
        entry.Message.ShouldBe("Scripts/door.luau:2: The script on 'Hatch' did not start.");
    }

    [Fact]
    public void A_snapshot_the_shell_has_no_room_for_still_gives_the_row_of_a_script_that_did_not_start()
    {
        Guid door = Guid.NewGuid();
        var dropped = new FrameSnapshot
        {
            FrameNumber = 40,
            IsPlaying = true,
            ConsoleLines =
            [
                new ConsoleLine(7, LogLevel.Information, "hello") { Place = new ConsolePlace(Door, 3) },
                DidNotStart(door, "The script on 'Door' did not start.", new ConsolePlace(Door, 2)) with { Sequence = 8 },
                new ConsoleLine(9, LogLevel.Error, "it broke") { Place = new ConsolePlace(Door, 12) },
            ],
            ConsoleLinesWritten = 10,
        };

        FrameSnapshot kept = ScriptStartProblems.KeptOf(dropped).ShouldNotBeNull();

        kept.ConsoleLines.ShouldHaveSingleItem().Sequence.ShouldBe(8);
        kept.IsPlaying.ShouldBeTrue("the play that starts with it still clears the rows of the last one");
        kept.FrameNumber.ShouldBe(40);
        kept.ConsoleLinesWritten.ShouldBe(10);

        _feed.Apply(kept);

        _problems.Entries.ShouldHaveSingleItem().NodeId.ShouldBe(door);
    }

    [Fact]
    public void A_dropped_snapshot_that_names_no_script_that_did_not_start_keeps_nothing()
    {
        ScriptStartProblems.KeptOf(Stopped()).ShouldBeNull();
        ScriptStartProblems.KeptOf(Playing(
            new ConsoleLine(0, LogLevel.Information, "hello") { Place = new ConsolePlace(Door, 3) },
            new ConsoleLine(1, LogLevel.Error, "it broke") { Place = new ConsolePlace(Door, 12) })).ShouldBeNull();
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
