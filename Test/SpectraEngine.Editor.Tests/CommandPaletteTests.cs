using SpectraEngine.Editor.Shell;
using SpectraEngine.Editor.Shell.Ribbon;

namespace SpectraEngine.Editor.Tests;

/// <summary>The palette's command table and its ranking.</summary>
public sealed class CommandPaletteTests
{
    [Fact]
    public void The_palette_reaches_every_verb_the_ribbon_does()
    {
        var inPalette = CommandTable.Commands.Select(c => c.Verb).ToHashSet();

        var missing = RibbonLayout.Tabs
            .SelectMany(RibbonLayout.ItemsOf)
            .Concat(RibbonLayout.AlwaysVisible)

            // A field is typed into, so the palette has nothing to run.
            .Where(i => i.Kind != RibbonControlKind.Field)
            .Select(i => i.Verb)
            .Where(v => !inPalette.Contains(v))
            .ToList();

        missing.ShouldBeEmpty("every verb on the ribbon must be reachable from the palette");
    }

    [Fact]
    public void Initials_reach_the_command_they_are_the_initials_of()
    {
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

        // Play/stop and collapse/expand are exclusive pairs, so one half of
        // each is withheld. Named, so a row missing for another reason shows up.
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
        Titles(hasSelection: false, isPlaying: false).ShouldNotContain("Duplicate");
        Titles(hasSelection: true, isPlaying: false).ShouldContain("Duplicate");

        Titles(hasSelection: true, isPlaying: true).ShouldNotContain("Duplicate");
        Titles(hasSelection: true, isPlaying: true).ShouldNotContain("Undo");

        // A camera verb is not a scene edit, so play does not gate it.
        Titles(hasSelection: true, isPlaying: true).ShouldContain("Frame everything");
    }

    [Fact]
    public void A_name_typed_out_in_full_finds_itself()
    {
        // A letter spent on a later word's first letter must not strand the
        // rest: "rem" giving its e to "entity" leaves no m to find.
        foreach (ShellCommand command in CommandTable.Commands)
            CommandScore.Of(command.Title, command.Title).ShouldNotBe(CommandScore.NoMatch, command.Title);

        CommandScore.Of("Remove entity", "rem").ShouldNotBe(CommandScore.NoMatch);
        CommandScore.Of("Insert entity", "insert").ShouldNotBe(CommandScore.NoMatch);

        // A word start is still preferred where it leads somewhere.
        CommandScore.Of("Frame everything", "fe")
            .ShouldBeGreaterThan(CommandScore.Of("Frame selection", "fe"));
    }

    [Fact]
    public void Make_and_remove_entity_are_found_by_what_a_mapper_would_type()
    {
        // The row opens the class list, so it ends in an ellipsis like the
        // other rows that ask something before acting.
        First("make entity").ShouldBe("Make entity...");
        First("door").ShouldBe("Make entity...");
        First("trigger").ShouldBe("Make entity...");
        First("remove entity").ShouldBe("Remove entity");

        Titles(hasSelection: false, isPlaying: false).ShouldNotContain("Make entity...");
        Titles(hasSelection: true, isPlaying: true).ShouldNotContain("Make entity...");
        Titles(hasSelection: true, isPlaying: true).ShouldNotContain("Remove entity");
    }

    [Fact]
    public void The_same_query_ranks_the_same_way_every_time()
    {
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

    [Fact]
    public void Every_application_level_verb_is_in_the_table()
    {
        // Hand-written, not reflected over the enums: trimming removes that.
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
    public void Save_is_not_offered_while_playing()
    {
        IReadOnlyList<string> editing = Titles(hasSelection: true, isPlaying: false);
        editing.ShouldContain("Save level");
        editing.ShouldContain("Save level as...");

        IReadOnlyList<string> playing = Titles(hasSelection: true, isPlaying: true);
        playing.ShouldNotContain("Save level");
        playing.ShouldNotContain("Save level as...");
    }

    [Fact]
    public void Play_is_withheld_on_a_scene_that_cannot_run()
    {
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
    public void The_Logic_view_has_a_row_for_each_place_it_can_be()
    {
        // Three set verbs, all offered whatever shows: a toggle row would
        // have to know the window's state.
        (string Title, WorkspaceCommand Command)[] rows =
        [
            ("Logic view: below the viewport", WorkspaceCommand.ShowLogicBelow),
            ("Logic view: beside the viewport", WorkspaceCommand.ShowLogicBeside),
            ("Logic view: hide", WorkspaceCommand.HideLogic),
        ];

        foreach ((string title, WorkspaceCommand command) in rows)
        {
            ShellCommand row = CommandTable.Commands.Single(c => c.Verb == ShellVerb.Of(command));

            row.Title.ShouldBe(title);
            row.Needs.ShouldBe(CommandNeeds.Session, title);
        }

        IReadOnlyList<string> found = CommandTable.Search("logic", Ready()).Rows.Select(c => c.Title).ToList();
        found.ShouldBe(rows.Select(r => r.Title), ignoreOrder: true);

        First("wiring").ShouldStartWith("Logic view:");

        CommandTable.Search("logic", Ready(hasSession: false, hasProject: false, canPlay: false))
            .Rows.ShouldBeEmpty();
    }

    [Fact]
    public void A_document_verb_needs_whatever_it_acts_on()
    {
        IReadOnlyList<string> nothingOpen = CommandTable.Search(
            string.Empty,
            Ready(hasSession: false, hasProject: false, canPlay: false),
            limit: 500).Rows.Select(c => c.Title).ToList();

        nothingOpen.ShouldContain("New project...");
        nothingOpen.ShouldContain("Open project...");
        nothingOpen.ShouldContain("Exit");

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
