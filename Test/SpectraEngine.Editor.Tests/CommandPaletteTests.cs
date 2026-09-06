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
        CommandTable.Search("zzqx", Ready()).Rows.ShouldBeEmpty();
    }

    [Fact]
    public void An_empty_query_offers_everything_it_is_allowed_to()
    {
        CommandSearchResult all = CommandTable.Search(string.Empty, Ready(), limit: 500);

        // Not the whole table, and it must not be: the two SET-verb pairs are
        // mutually exclusive by construction, so exactly one half of each is
        // offered in any one state. Naming them is the point - a row that
        // vanished for some OTHER reason would show up here as a third.
        IReadOnlyList<string> withheld = CommandTable.Commands
            .Select(c => c.Title)
            .Except(all.Rows.Select(c => c.Title))
            .OrderBy(title => title, StringComparer.Ordinal)
            .ToList();

        withheld.ShouldBe(["Expand the ribbon", "Stop"]);
        all.IsTruncated.ShouldBeFalse();
    }

    [Fact]
    public void Every_row_is_reachable_in_some_state()
    {
        // The other half of the pair above: nothing in the table is dead.
        var reachable = new HashSet<string>(StringComparer.Ordinal);

        foreach (bool playing in new[] { false, true })
        foreach (bool expanded in new[] { false, true })
        foreach (bool selection in new[] { false, true })
        {
            CommandSearchResult result = CommandTable.Search(
                string.Empty,
                Ready(hasSelection: selection, isPlaying: playing, ribbonExpanded: expanded),
                limit: 500);

            foreach (ShellCommand row in result.Rows) reachable.Add(row.Title);
        }

        reachable.Count.ShouldBe(CommandTable.Commands.Count);
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
        IReadOnlyList<ShellCommand> once = CommandTable.Search("in", Ready()).Rows;
        IReadOnlyList<ShellCommand> twice = CommandTable.Search("in", Ready()).Rows;

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

    // --- the verbs the palette could not reach -------------------------------
    //
    // The class doc always said the rows beyond the ribbon's were "the document
    // verbs"; there were none. Saving, opening a project, playing and showing a
    // panel were reachable by menu and by chord, and by nothing you could type.

    [Fact]
    public void Every_application_level_verb_is_in_the_table()
    {
        // Hand-written rather than reflected over the enums: enumerating by
        // reflection is what trimming removes, and it would pass here and fail
        // in a published build.
        DocumentVerb[] documents =
        [
            DocumentVerb.NewProject, DocumentVerb.OpenProject, DocumentVerb.CloseProject,
            DocumentVerb.NewLevel, DocumentVerb.OpenLevel, DocumentVerb.Save,
            DocumentVerb.SaveAs, DocumentVerb.ValidateCooked, DocumentVerb.Exit,
        ];
        PanelId[] panels =
        [
            PanelId.Scene, PanelId.Levels, PanelId.Properties, PanelId.Content,
            PanelId.Output, PanelId.Problems, PanelId.Console, PanelId.KeyboardReference,
        ];

        foreach (DocumentVerb verb in documents)
            CommandTable.Commands.ShouldContain(c => c.Verb == ShellVerb.Of(verb), verb.ToString());

        foreach (PanelId panel in panels)
            CommandTable.Commands.ShouldContain(c => c.Verb == ShellVerb.Of(panel), panel.ToString());

        CommandTable.Commands.ShouldContain(c => c.Verb == ShellVerb.Of(PlayVerb.Play));
        CommandTable.Commands.ShouldContain(c => c.Verb == ShellVerb.Of(PlayVerb.Stop));
        CommandTable.Commands.ShouldContain(c => c.Verb == ShellVerb.Of(RibbonVerb.Collapse));
        CommandTable.Commands.ShouldContain(c => c.Verb == ShellVerb.Of(RibbonVerb.Expand));
    }

    [Fact]
    public void Play_and_stop_are_offered_by_state_and_never_both()
    {
        Titles(hasSelection: true, isPlaying: false).ShouldContain("Play");
        Titles(hasSelection: true, isPlaying: false).ShouldNotContain("Stop");

        Titles(hasSelection: true, isPlaying: true).ShouldContain("Stop");
        Titles(hasSelection: true, isPlaying: true).ShouldNotContain("Play");
    }

    [Fact]
    public void Play_is_withheld_on_a_scene_that_cannot_run()
    {
        // Offering it would be offering a verb the engine answers by refusing.
        CommandTable.Search(string.Empty, Ready(canPlay: false), limit: 500).Rows
            .Select(c => c.Title).ShouldNotContain("Play");
    }

    [Fact]
    public void Collapse_and_expand_follow_the_ribbon()
    {
        CommandTable.Search(string.Empty, Ready(ribbonExpanded: true), limit: 500).Rows
            .Select(c => c.Title).ShouldContain("Collapse the ribbon");

        CommandTable.Search(string.Empty, Ready(ribbonExpanded: false), limit: 500).Rows
            .Select(c => c.Title).ShouldContain("Expand the ribbon");
    }

    [Fact]
    public void A_document_verb_needs_whatever_it_acts_on()
    {
        IReadOnlyList<string> nothingOpen = CommandTable.Search(
            string.Empty,
            Ready(hasSession: false, hasProject: false, canPlay: false),
            limit: 500).Rows.Select(c => c.Title).ToList();

        // These make sense with nothing open, and are the way OUT of that state.
        nothingOpen.ShouldContain("New project...");
        nothingOpen.ShouldContain("Open project...");
        nothingOpen.ShouldContain("Exit");

        // These act on something that is not there.
        nothingOpen.ShouldNotContain("Save level");
        nothingOpen.ShouldNotContain("Close project");
        nothingOpen.ShouldNotContain("Validate cooked content");
    }

    [Fact]
    public void An_alias_reaches_a_command_whose_title_nobody_would_type()
    {
        First("wireframe").ShouldBe("Overlay: wireframe");
        First("quit").ShouldBe("Exit");
        First("inspector").ShouldBe("Show Properties panel");
        First("shortcuts").ShouldBe("Keyboard reference");
    }

    [Fact]
    public void A_title_still_outranks_an_alias_that_matches_as_well()
    {
        // The alias scores a point below its own match, so a row whose TITLE
        // says what you typed wins against one that merely answers to it.
        First("play").ShouldBe("Play");
    }

    [Fact]
    public void The_result_says_how_many_it_hid()
    {
        CommandSearchResult result = CommandTable.Search(string.Empty, Ready());

        result.Rows.Count.ShouldBe(CommandTable.DefaultLimit);
        result.TotalMatches.ShouldBeGreaterThan(result.Rows.Count);
        result.IsTruncated.ShouldBeTrue();
        result.FooterLabel.ShouldContain($"{result.Rows.Count} of {result.TotalMatches}");
    }

    [Fact]
    public void A_query_that_matches_nothing_says_so_rather_than_showing_an_empty_list()
    {
        CommandSearchResult result = CommandTable.Search("zzqx", Ready());

        result.Rows.ShouldBeEmpty();
        result.FooterLabel.ShouldBe("No command matches.");
    }

    [Fact]
    public void A_result_that_fits_says_nothing()
    {
        CommandSearchResult result = CommandTable.Search("wireframe", Ready());

        result.IsTruncated.ShouldBeFalse();
        result.FooterLabel.ShouldBeEmpty();
    }

    private static string First(string query) =>
        CommandTable.Search(query, Ready()).Rows[0].Title;

    /// <summary>A session that can do everything, as the baseline.</summary>
    private static CommandContext Ready(
        bool hasSelection = true,
        bool isPlaying = false,
        bool hasSession = true,
        bool hasProject = true,
        bool ribbonExpanded = true,
        bool canPlay = true) =>
        new(hasSelection, isPlaying, hasSession, hasProject, ribbonExpanded, canPlay);

    private static IReadOnlyList<string> Titles(bool hasSelection, bool isPlaying) =>
        CommandTable.Search(string.Empty, Ready(hasSelection, isPlaying), limit: 500).Rows
            .Select(c => c.Title)
            .ToList();
}
