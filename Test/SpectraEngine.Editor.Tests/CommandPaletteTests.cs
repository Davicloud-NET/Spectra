using SpectraEngine.Editor.Shell;
using SpectraEngine.Editor.Shell.Ribbon;

namespace SpectraEngine.Editor.Tests;

/// <summary>
/// The palette's data and its ranking, both of which are pure and neither of
/// which a person can check by looking at a list.
/// </summary>
public sealed class CommandPaletteTests
{
    [Fact]
    public void The_palette_reaches_every_verb_the_ribbon_does()
    {
        // THE PROPERTY THAT MAKES IT A THIRD ROUTE RATHER THAN A RIVAL. The
        // design doctrine this shell follows asks for every command to be
        // reachable by direct manipulation, by a menu with its shortcut printed,
        // and by a palette; a palette that reaches a subset of the buttons is
        // none of those things. It fails the day somebody adds a ribbon control
        // and forgets this table, which is the only way the two can drift.
        var inPalette = CommandTable.Commands.Select(c => c.Verb).ToHashSet();

        var missing = RibbonLayout.Tabs
            .SelectMany(RibbonLayout.ItemsOf)
            .Concat(RibbonLayout.AlwaysVisible)

            // The snap increment is a FIELD: its verb names a control that is
            // typed into rather than invoked, and there is nothing for a palette
            // to run. Its two steppers are here.
            .Where(i => i.Kind != RibbonControlKind.Field)
            .Select(i => i.Verb)
            .Where(v => !inPalette.Contains(v))
            .ToList();

        missing.ShouldBeEmpty("every verb on the ribbon must be reachable from the palette");
    }

    [Fact]
    public void Initials_reach_the_command_they_are_the_initials_of()
    {
        // The whole value of a palette is typing the letters you remember rather
        // than a prefix you do not, and whether the ranking honours that is
        // exactly what no amount of looking at the list will tell you.
        First("ib").ShouldBe("Insert block");
        First("dup").ShouldBe("Duplicate");
        First("ung").ShouldBe("Ungroup");
        First("fe").ShouldBe("Frame everything");
        First("snap").ShouldBe("Snap to grid");
    }

    [Fact]
    public void A_query_that_matches_nothing_returns_nothing()
    {
        CommandScore.Of("Insert block", "zzz").ShouldBe(CommandScore.NoMatch);
        CommandTable.Search("zzqx", hasSelection: true, isPlaying: false).ShouldBeEmpty();
    }

    [Fact]
    public void An_empty_query_offers_everything_it_is_allowed_to()
    {
        IReadOnlyList<ShellCommand> all =
            CommandTable.Search(string.Empty, hasSelection: true, isPlaying: false, limit: 500);

        all.Count.ShouldBe(CommandTable.Commands.Count);
    }

    [Fact]
    public void A_command_that_cannot_run_is_not_offered()
    {
        // The same gate the buttons wear, and for the same reason: a palette row
        // that runs into RefuseEdit and logs at Debug is a control that lies
        // about what the engine is doing.
        Titles(hasSelection: false, isPlaying: false).ShouldNotContain("Duplicate");
        Titles(hasSelection: true, isPlaying: false).ShouldContain("Duplicate");

        Titles(hasSelection: true, isPlaying: true).ShouldNotContain("Duplicate");
        Titles(hasSelection: true, isPlaying: true).ShouldNotContain("Undo");

        // A camera verb is not a scene edit, so play does not gate it.
        Titles(hasSelection: true, isPlaying: true).ShouldContain("Frame everything");
    }

    [Fact]
    public void The_same_query_ranks_the_same_way_every_time()
    {
        // Score ties are broken ordinally rather than left to whatever order the
        // table happens to be written in, so the list a person builds muscle
        // memory against does not reshuffle under them.
        IReadOnlyList<ShellCommand> once = CommandTable.Search("in", true, false);
        IReadOnlyList<ShellCommand> twice = CommandTable.Search("in", true, false);

        once.Select(c => c.Title).ShouldBe(twice.Select(c => c.Title));
    }

    [Fact]
    public void Every_row_names_a_verb_and_says_something()
    {
        foreach (ShellCommand command in CommandTable.Commands)
        {
            command.Title.ShouldNotBeNullOrWhiteSpace();
            command.Verb.Kind.ShouldNotBe(ShellVerbKind.None, command.Title);
        }

        CommandTable.Commands.Select(c => c.Title).Distinct().Count()
            .ShouldBe(CommandTable.Commands.Count, "two rows with one name is one the user cannot pick");
    }

    private static string First(string query) =>
        CommandTable.Search(query, hasSelection: true, isPlaying: false)[0].Title;

    private static IReadOnlyList<string> Titles(bool hasSelection, bool isPlaying) =>
        CommandTable.Search(string.Empty, hasSelection, isPlaying, limit: 500)
            .Select(c => c.Title)
            .ToList();
}
