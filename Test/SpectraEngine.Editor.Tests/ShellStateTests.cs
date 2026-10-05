using SpectraEngine.Core.Hosting;
using SpectraEngine.Editor.Shell;
using System.Collections.Generic;
using System.Globalization;

namespace SpectraEngine.Editor.Tests;

/// <summary>
/// The optimistic value a command-bar control shows between a click and the
/// engine's echo.
/// </summary>
public sealed class OptimisticValueTests
{
    [Fact]
    public void A_request_is_displayed_at_once()
    {
        var value = new OptimisticValue<string>("move");

        Assert.True(value.Request("rotate"));
        Assert.Equal("rotate", value.Value);
        Assert.True(value.HasPending);
    }

    [Fact]
    public void An_agreeing_echo_hands_authority_back_to_the_engine()
    {
        var value = new OptimisticValue<string>("move");
        value.Request("rotate");

        value.Apply("rotate");
        Assert.False(value.HasPending);

        // Nothing pending, so a change from elsewhere applies at once.
        value.Apply("resize");
        Assert.Equal("resize", value.Value);
    }

    [Fact]
    public void A_stale_echo_is_ignored_while_the_request_is_in_flight()
    {
        var value = new OptimisticValue<string>("move") { HoldTicks = 6 };
        value.Request("rotate");

        // Snapshots from before the click.
        for (int i = 0; i < 5; i++)
        {
            Assert.False(value.Apply("move"));
            Assert.Equal("rotate", value.Value);
        }
    }

    [Fact]
    public void The_engine_wins_once_the_hold_expires()
    {
        var value = new OptimisticValue<string>("move") { HoldTicks = 3 };
        value.Request("rotate");

        value.Apply("move");
        value.Apply("move");
        Assert.Equal("rotate", value.Value);

        // Past the hold this is a refusal, not lag, and has to show.
        Assert.True(value.Apply("move"));
        Assert.Equal("move", value.Value);
        Assert.False(value.HasPending);
    }

    [Fact]
    public void A_second_request_replaces_the_first_rather_than_queueing()
    {
        var value = new OptimisticValue<string>("move") { HoldTicks = 3 };
        value.Request("rotate");
        value.Apply("move");

        value.Request("resize");
        Assert.Equal("resize", value.Value);

        // The hold restarts with the new request.
        value.Apply("move");
        value.Apply("move");
        Assert.Equal("resize", value.Value);
    }

    [Fact]
    public void Undo_and_redo_depth_move_together()
    {
        var value = new OptimisticValue<(int Undo, int Redo)>((4, 1));

        value.Request((3, 2));
        Assert.Equal((3, 2), value.Value);

        value.Apply((3, 2));
        Assert.False(value.HasPending);
    }

    [Fact]
    public void A_reset_drops_the_pending_request()
    {
        var value = new OptimisticValue<string>("move");
        value.Request("rotate");

        // Session close. A held request would make the next session ignore its first snapshots.
        value.Reset("move");

        Assert.False(value.HasPending);
        Assert.Equal("move", value.Value);
        Assert.True(value.Apply("resize"));
    }
}

public sealed class OutputLogTests
{
    [Fact]
    public void Errors_and_warnings_are_counted_separately()
    {
        var log = new OutputLog();

        log.Append(OutputSeverity.Info, "opened");
        log.Append(OutputSeverity.Warning, "a texture is missing");
        log.Append(OutputSeverity.Error, "the save failed");
        log.Append(OutputSeverity.Error, "and again");

        Assert.Equal(2, log.ErrorCount);
        Assert.Equal(1, log.WarningCount);
    }

    [Fact]
    public void The_header_says_what_it_is_and_never_claims_no_problems()
    {
        // The history is bounded, so it cannot say whether problems remain.
        // ProblemList answers that.
        var log = new OutputLog();
        Assert.DoesNotContain("problem", log.HistoryLabel, StringComparison.OrdinalIgnoreCase);

        log.Append(OutputSeverity.Error, "the save failed");
        Assert.DoesNotContain("problem", log.HistoryLabel, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("1 of", log.HistoryLabel);
    }

    [Fact]
    public void A_repeated_line_grows_a_count_instead_of_a_row()
    {
        var log = new OutputLog();

        log.Append(OutputSeverity.Warning, "a texture is missing");
        log.Append(OutputSeverity.Warning, "a texture is missing");
        log.Append(OutputSeverity.Warning, "a texture is missing");

        OutputEntry only = Assert.Single(log.Entries);
        Assert.Equal(3, only.Count);
        Assert.Equal("x3", only.CountLabel);

        Assert.Equal(1, log.WarningCount);
    }

    [Fact]
    public void Two_lines_alternating_stay_two_rows()
    {
        // Only the last entry is compared.
        var log = new OutputLog();

        log.Append(OutputSeverity.Warning, "first");
        log.Append(OutputSeverity.Warning, "second");
        log.Append(OutputSeverity.Warning, "first");

        Assert.Equal(3, log.Entries.Count);
    }

    [Fact]
    public void An_empty_line_is_not_recorded()
    {
        var log = new OutputLog();
        log.Append(OutputSeverity.Info, "   ");
        Assert.Empty(log.Entries);
    }

    [Fact]
    public void The_oldest_lines_go_first_and_their_counts_go_with_them()
    {
        var log = new OutputLog();

        // The error count has to drop when the error scrolls out.
        log.Append(OutputSeverity.Error, "the first failure");
        for (int i = 0; i < OutputLog.Capacity; i++)
            log.Append(OutputSeverity.Info, $"line {i}");

        Assert.Equal(OutputLog.Capacity, log.Entries.Count);
        Assert.Equal(0, log.ErrorCount);
        Assert.DoesNotContain(log.Entries, e => e.Text == "the first failure");
    }
}

public sealed class ConsoleCommandsTests
{
    private static ConsoleCommands Build(
        List<string> log,
        bool sessionOpen = true,
        List<string>? forwarded = null,
        bool viewportStopped = false)
    {
        return new ConsoleCommands(
            postHost: c => Record(log, $"host:{c}", sessionOpen),
            postGizmo: c => Record(log, $"gizmo:{c}", sessionOpen),
            postCamera: c => Record(log, $"camera:{c}", sessionOpen),
            insert: k => Record(log, $"insert:{k}", sessionOpen),
            // Invariant, or the test fails on a machine whose culture writes 0,25.
            setSnap: (tool, value) => Record(
                log, $"snap:{tool}={value.ToString(CultureInfo.InvariantCulture)}", sessionOpen),
            setPipeline: n => Record(log, $"pipeline:{n}", sessionOpen),
            setPlaying: p => log.Add($"play:{p}"),
            forward: line =>
            {
                if (sessionOpen)
                    forwarded?.Add(line);

                return sessionOpen;
            },
            restartViewport: () => Record(log, "restart", viewportStopped));

        static bool Record(List<string> log, string entry, bool ok)
        {
            if (ok)
                log.Add(entry);

            return ok;
        }
    }

    [Fact]
    public void A_verb_resolves_to_the_command_a_button_would_send()
    {
        List<string> log = [];
        ConsoleResult result = Build(log).Execute("duplicate");

        Assert.Equal(OutputSeverity.Info, result.Severity);
        Assert.Equal(["host:Duplicate"], log);
    }

    [Fact]
    public void A_verb_the_editor_does_not_own_is_forwarded_as_typed()
    {
        List<string> log = [];
        List<string> forwarded = [];
        ConsoleResult result = Build(log, forwarded: forwarded).Execute("ent_list door*");

        // The engine's reply comes later, so there is nothing to print now.
        Assert.Equal(OutputSeverity.Info, result.Severity);
        Assert.Equal(string.Empty, result.Reply);
        Assert.Equal(["ent_list door*"], forwarded);
        Assert.Empty(log);
    }

    [Fact]
    public void A_forwarded_line_keeps_its_case_and_its_quotes()
    {
        List<string> forwarded = [];
        const string Typed = "  ent_fire \"Main Door\"  Open  \"\" 2; ENT_show Relay1";

        Build([], forwarded: forwarded).Execute(Typed);

        Assert.Equal([Typed], forwarded);
    }

    [Fact]
    public void A_forwarded_line_with_nothing_open_says_so()
    {
        List<string> forwarded = [];
        ConsoleResult result = Build([], sessionOpen: false, forwarded: forwarded).Execute("ent_list");

        Assert.Equal(OutputSeverity.Error, result.Severity);
        Assert.Equal("nothing is open", result.Reply);
        Assert.Empty(forwarded);
    }

    [Fact]
    public void Help_lists_the_editor_verbs_and_asks_the_engine_for_its_own()
    {
        List<string> forwarded = [];
        ConsoleResult result = Build([], forwarded: forwarded).Execute("Help");

        Assert.Equal(OutputSeverity.Info, result.Severity);
        foreach (string name in ConsoleCommands.Names)
            Assert.Contains(name, result.Reply);
        Assert.Equal(["help"], forwarded);
    }

    [Fact]
    public void Help_still_lists_the_editor_verbs_with_nothing_open()
    {
        ConsoleResult result = Build([], sessionOpen: false).Execute("help");

        Assert.Equal(OutputSeverity.Info, result.Severity);
        Assert.Contains("duplicate", result.Reply);
    }

    [Fact]
    public void Help_for_an_editor_verb_is_answered_here_and_any_other_is_forwarded()
    {
        List<string> forwarded = [];
        ConsoleCommands console = Build([], forwarded: forwarded);

        ConsoleResult local = console.Execute("help grid");
        Assert.StartsWith("grid <n>", local.Reply);
        Assert.Empty(forwarded);

        console.Execute("help ent_fire");
        Assert.Equal(["help ent_fire"], forwarded);
    }

    [Fact]
    public void Snap_takes_an_invariant_number_and_refuses_anything_else()
    {
        List<string> log = [];
        ConsoleCommands console = Build(log);

        Assert.Equal(OutputSeverity.Info, console.Execute("grid 0.25").Severity);
        Assert.Equal(["snap:Translate=0.25"], log);

        // Refused, not clamped: a grid of zero divides by zero downstream.
        Assert.Equal(OutputSeverity.Error, console.Execute("grid 0").Severity);
        Assert.Equal(OutputSeverity.Error, console.Execute("grid -2").Severity);
        Assert.Equal(OutputSeverity.Error, console.Execute("grid wide").Severity);
        Assert.Single(log);
    }

    [Fact]
    public void On_and_off_are_set_verbs_and_a_bare_verb_means_on()
    {
        List<string> log = [];
        ConsoleCommands console = Build(log);

        console.Execute("snap");
        console.Execute("snap off");
        console.Execute("snap on");

        // Set verbs, not toggles: a stale echo cannot flip a set the wrong way.
        Assert.Equal(["gizmo:EnableSnap", "gizmo:DisableSnap", "gizmo:EnableSnap"], log);
    }

    [Fact]
    public void Nothing_open_is_reported_rather_than_appearing_to_work()
    {
        List<string> log = [];
        ConsoleResult result = Build(log, sessionOpen: false).Execute("block");

        Assert.Equal(OutputSeverity.Error, result.Severity);
        Assert.Empty(log);
    }

    [Fact]
    public void Help_names_every_command_the_table_offers()
    {
        List<string> log = [];
        string help = Build(log).Execute("help").Reply;

        foreach (string name in ConsoleCommands.Names)
            Assert.Contains(name, help);
    }

    [Fact]
    public void Restart_starts_a_viewport_that_was_left_stopped()
    {
        List<string> log = [];
        List<string> forwarded = [];
        ConsoleResult result = Build(log, sessionOpen: false, forwarded, viewportStopped: true).Execute("restart");

        Assert.Equal(OutputSeverity.Info, result.Severity);
        Assert.Equal(["restart"], log);

        // The editor's own verb: the engine it would go to is the one that died.
        Assert.Empty(forwarded);
    }

    [Fact]
    public void Restart_with_a_viewport_that_is_running_says_so_and_does_nothing()
    {
        List<string> log = [];
        ConsoleResult result = Build(log).Execute("restart");

        Assert.Equal(OutputSeverity.Error, result.Severity);
        Assert.Equal("the viewport is not stopped", result.Reply);
        Assert.Empty(log);
    }

    [Fact]
    public void Clear_asks_for_the_log_to_be_emptied_rather_than_printing()
    {
        List<string> log = [];
        Assert.Equal(ConsoleCommands.ClearMarker, Build(log).Execute("clear").Reply);
    }
}

/// <summary>
/// The status bar's standing slot for graphics debug-layer errors.
/// </summary>
public sealed class DebugLayerStatusTests
{
    private static ShellModel Apply(int errors, bool active)
    {
        var model = new ShellModel();
        model.ApplySnapshot(new FrameSnapshot
        {
            DebugLayerErrorCount = errors,
            DebugLayerActive = active,
        });
        return model;
    }

    [Fact]
    public void A_clean_session_shows_nothing()
    {
        ShellModel model = Apply(errors: 0, active: true);

        Assert.False(model.HasDebugLayerErrors);
        Assert.True(model.DebugLayerClean);
    }

    [Fact]
    public void A_reported_error_takes_the_standing_slot_and_names_its_count()
    {
        ShellModel model = Apply(errors: 3, active: true);

        Assert.True(model.HasDebugLayerErrors);
        Assert.Equal("3 graphics errors", model.DebugLayerLabel);
        Assert.Contains("3", model.DebugLayerTip);

        // A standing slot, kept off the message line.
        Assert.False(model.HasMessage);
    }

    [Fact]
    public void One_error_is_not_pluralised()
    {
        Assert.Equal("1 graphics error", Apply(errors: 1, active: true).DebugLayerLabel);
    }

    [Fact]
    public void A_layer_that_is_not_running_does_not_read_as_clean()
    {
        // On D3D the count only exists while validation runs, so zero with the
        // layer off must not read as clean.
        ShellModel model = Apply(errors: 0, active: false);

        Assert.False(model.DebugLayerClean);
        Assert.False(model.HasDebugLayerErrors);
        Assert.Contains("not running", model.DebugLayerTip);
    }

    [Fact]
    public void The_count_going_back_to_zero_clears_the_slot()
    {
        var model = new ShellModel();
        model.ApplySnapshot(new FrameSnapshot { DebugLayerErrorCount = 2, DebugLayerActive = true });
        Assert.True(model.HasDebugLayerErrors);

        model.ApplySnapshot(new FrameSnapshot { DebugLayerErrorCount = 0, DebugLayerActive = true });
        Assert.False(model.HasDebugLayerErrors);
        Assert.True(model.DebugLayerClean);
    }
}

/// <summary>
/// The status bar's standing slot for missing assets: how many references are
/// bound to the magenta placeholder.
/// </summary>
public sealed class PlaceholderBoundStatusTests
{
    private static ShellModel Apply(int count)
    {
        var model = new ShellModel();
        model.ApplySnapshot(new FrameSnapshot { PlaceholderBoundCount = count });
        return model;
    }

    [Fact]
    public void A_healthy_session_shows_nothing()
    {
        ShellModel model = Apply(0);

        Assert.False(model.HasPlaceholderBound);
    }

    [Fact]
    public void A_missing_asset_takes_the_standing_slot_and_names_its_count()
    {
        ShellModel model = Apply(3);

        Assert.True(model.HasPlaceholderBound);
        Assert.Equal("3 missing assets", model.PlaceholderBoundLabel);
        Assert.Contains("3", model.PlaceholderBoundTip);

        // A standing slot, kept off the message line.
        Assert.False(model.HasMessage);
    }

    [Fact]
    public void One_missing_asset_is_not_pluralised()
    {
        Assert.Equal("1 missing asset", Apply(1).PlaceholderBoundLabel);
    }

    [Fact]
    public void The_tip_says_that_unnamed_geometry_is_not_a_missing_asset()
    {
        string tip = Apply(1).PlaceholderBoundTip;

        Assert.Contains("names no material", tip);
        Assert.Contains("grey", tip);
    }

    [Fact]
    public void The_count_going_back_to_zero_clears_the_slot()
    {
        var model = new ShellModel();
        model.ApplySnapshot(new FrameSnapshot { PlaceholderBoundCount = 2 });
        Assert.True(model.HasPlaceholderBound);

        model.ApplySnapshot(new FrameSnapshot { PlaceholderBoundCount = 0 });
        Assert.False(model.HasPlaceholderBound);
    }
}
