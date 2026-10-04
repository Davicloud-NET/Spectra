using Microsoft.Extensions.Logging.Abstractions;
using SpectraEngine.Editor.Shell;
using SpectraEngine.Editor.Viewport;
using System;
using System.IO;

namespace SpectraEngine.Editor.Tests;

/// <summary>
/// The per-user recents store behind the start page.
/// </summary>
/// <remarks>
/// <b>The claims worth pinning are the forgiving ones.</b> A recents cache
/// must never become a reason the shell cannot start: a missing file is an
/// empty list, a corrupt file is an empty list with a warning, and a bad
/// entry inside an otherwise good file costs that entry and nothing else.
/// The round trip itself matters less — it is a cache — but a list that
/// reordered or duplicated across restarts would read as the shell forgetting
/// things, which is the one impression a recents list must not give.
/// </remarks>
public sealed class EditorSettingsTests
{
    private static string TempPath() =>
        Path.Combine(Path.GetTempPath(), "spectra-tests", Path.GetRandomFileName(), "editor.json");

    [Fact]
    public void Touching_a_project_puts_it_first_and_deduplicates_by_path()
    {
        var settings = new EditorSettings();

        settings.TouchProject(@"C:\Games\Alpha", "Alpha", new DateTime(2026, 8, 1));
        settings.TouchProject(@"C:\Games\Beta", "Beta", new DateTime(2026, 8, 2));

        // Same folder, different spelling: one card, moved to the front, not
        // two cards for one project.
        settings.TouchProject(@"C:\GAMES\ALPHA", "Alpha", new DateTime(2026, 8, 3));

        settings.RecentProjects.Count.ShouldBe(2);
        settings.RecentProjects[0].Name.ShouldBe("Alpha");
        settings.RecentProjects[1].Name.ShouldBe("Beta");
    }

    [Fact]
    public void The_list_is_capped_rather_than_unbounded()
    {
        var settings = new EditorSettings();
        for (int i = 0; i < 25; i++)
            settings.TouchProject($@"C:\Games\P{i}", $"P{i}", new DateTime(2026, 1, 1).AddDays(i));

        settings.RecentProjects.Count.ShouldBe(10);
        settings.RecentProjects[0].Name.ShouldBe("P24", "newest first");
    }

    [Fact]
    public void Settings_round_trip_through_disk_in_order()
    {
        string path = TempPath();
        string alpha = Path.Combine(Path.GetTempPath(), "Games", "Alpha");
        string beta = Path.Combine(Path.GetTempPath(), "Games", "Beta");
        var settings = new EditorSettings();
        settings.TouchProject(alpha, "Alpha", new DateTime(2026, 8, 1, 10, 30, 0, DateTimeKind.Utc));
        settings.TouchProject(beta, "Beta", new DateTime(2026, 8, 2, 11, 0, 0, DateTimeKind.Utc));

        settings.Save(path, NullLogger.Instance);
        EditorSettings loaded = EditorSettings.Load(path, NullLogger.Instance);

        loaded.RecentProjects.Count.ShouldBe(2);
        loaded.RecentProjects[0].Name.ShouldBe("Beta");
        loaded.RecentProjects[0].Path.ShouldBe(beta);
        loaded.RecentProjects[0].OpenedUtc.ShouldBe(new DateTime(2026, 8, 2, 11, 0, 0, DateTimeKind.Utc));
        loaded.RecentProjects[1].Name.ShouldBe("Alpha");
    }

    [Fact]
    public void A_missing_file_is_an_empty_list_not_an_error()
    {
        EditorSettings loaded = EditorSettings.Load(TempPath(), NullLogger.Instance);
        loaded.RecentProjects.ShouldBeEmpty();
    }

    [Fact]
    public void A_corrupt_file_is_an_empty_list_not_an_error()
    {
        string path = TempPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "{ this is not json");

        EditorSettings loaded = EditorSettings.Load(path, NullLogger.Instance);
        loaded.RecentProjects.ShouldBeEmpty();
    }

    [Fact]
    public void An_entry_missing_its_essentials_is_dropped_alone()
    {
        string path = TempPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, """
            {
              "recentProjects": [
                { "name": "NoPath" },
                { "path": "C:\\Games\\Good", "name": "Good", "openedUtc": "2026-08-01T10:00:00.0000000Z" }
              ]
            }
            """);

        EditorSettings loaded = EditorSettings.Load(path, NullLogger.Instance);

        loaded.RecentProjects.Count.ShouldBe(1);
        loaded.RecentProjects[0].Name.ShouldBe("Good");
    }

    [Fact]
    public void Forgetting_a_project_removes_exactly_that_card()
    {
        var settings = new EditorSettings();
        settings.TouchProject(@"C:\Games\Alpha", "Alpha", DateTime.UtcNow);
        settings.TouchProject(@"C:\Games\Beta", "Beta", DateTime.UtcNow);

        settings.ForgetProject(@"C:\games\alpha");

        settings.RecentProjects.Count.ShouldBe(1);
        settings.RecentProjects[0].Name.ShouldBe("Beta");
    }

    [Fact]
    public void A_wrong_typed_member_is_an_empty_list_not_a_crash()
    {
        // Valid JSON of the wrong SHAPE is a different exception class from
        // invalid JSON, and a settings cache must survive both: it is a
        // convenience, not a dependency the shell can refuse to start over.
        string path = TempPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, """{ "recentProjects": [ { "path": 5, "name": true } ] }""");

        EditorSettings loaded = EditorSettings.Load(path, NullLogger.Instance);
        loaded.RecentProjects.ShouldBeEmpty();
    }

    [Fact]
    public void Two_shells_merge_on_save_rather_than_clobbering_each_other()
    {
        // Both shells load the same (empty) file, each opens its own project,
        // each saves. Last-writer-wins would erase the first shell's entry;
        // the save-time merge keeps both, newest touch first.
        string path = TempPath();

        var first = EditorSettings.Load(path, NullLogger.Instance);
        var second = EditorSettings.Load(path, NullLogger.Instance);

        first.TouchProject(@"C:\Games\Alpha", "Alpha", new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc));
        first.Save(path, NullLogger.Instance);

        second.TouchProject(@"C:\Games\Beta", "Beta", new DateTime(2026, 8, 2, 0, 0, 0, DateTimeKind.Utc));
        second.Save(path, NullLogger.Instance);

        EditorSettings merged = EditorSettings.Load(path, NullLogger.Instance);
        merged.RecentProjects.Count.ShouldBe(2);
        merged.RecentProjects[0].Name.ShouldBe("Beta");
        merged.RecentProjects[1].Name.ShouldBe("Alpha");
    }

    [Fact]
    public void A_forgotten_project_is_not_resurrected_by_the_merge()
    {
        // Forgetting happens because the folder is gone; the copy of the file
        // on disk still lists it, and the merge folding it back in would make
        // the dead card immortal.
        string path = TempPath();

        var settings = new EditorSettings();
        settings.TouchProject(@"C:\Games\Gone", "Gone", DateTime.UtcNow);
        settings.Save(path, NullLogger.Instance);

        settings.ForgetProject(@"C:\Games\Gone");
        settings.Save(path, NullLogger.Instance);

        EditorSettings loaded = EditorSettings.Load(path, NullLogger.Instance);
        loaded.RecentProjects.ShouldBeEmpty();
    }

    // --- The viewport block --------------------------------------------------

    [Fact]
    public void A_file_with_no_viewport_block_reads_as_the_default()
    {
        // Every settings file written before this stage. Auto is the
        // conservative answer: it resolves to the native child until a history
        // says otherwise, and there is no history to read.
        var settings = new EditorSettings();

        settings.ViewportPreference.ShouldBe(ViewportPreference.Default);
        settings.ViewportPreference.Mode.ShouldBe(ViewportMode.Auto);
    }

    [Fact]
    public void The_viewport_history_survives_a_round_trip()
    {
        string path = TempPath();

        var settings = new EditorSettings();
        settings.SetViewportMode(ViewportMode.Composition);
        settings.RebaseViewport("9a91010000000000", "31.0.101.5085");
        settings.RecordCompositedSession(sessionGreen: true);
        settings.RecordCompositedSession(sessionGreen: true);
        settings.Save(path, NullLogger.Instance);

        ViewportPreference loaded = EditorSettings.Load(path, NullLogger.Instance).ViewportPreference;

        loaded.Mode.ShouldBe(ViewportMode.Composition);
        loaded.GreenSessions.ShouldBe(2);
        loaded.AdapterLuid.ShouldBe("9a91010000000000");
        loaded.DriverVersion.ShouldBe("31.0.101.5085");
    }

    [Fact]
    public void The_viewport_block_merges_by_recency_rather_than_element_by_element()
    {
        // It is ONE state, not a set of entries. Two shells that both ran a
        // composited session would otherwise interleave their counts into a
        // number neither of them measured.
        string path = TempPath();

        var first = EditorSettings.Load(path, NullLogger.Instance);
        var second = EditorSettings.Load(path, NullLogger.Instance);

        first.RebaseViewport("aaaa", "1.0");
        first.RecordCompositedSession(sessionGreen: true);
        first.Save(path, NullLogger.Instance);

        second.RebaseViewport("bbbb", "2.0");
        second.RecordCompositedSession(sessionGreen: true);
        second.RecordCompositedSession(sessionGreen: true);
        second.Save(path, NullLogger.Instance);

        ViewportPreference merged =
            EditorSettings.Load(path, NullLogger.Instance).ViewportPreference;

        merged.AdapterLuid.ShouldBe("bbbb");
        merged.GreenSessions.ShouldBe(2);
    }

    [Fact]
    public void A_corrupt_file_loses_the_viewport_history_with_the_rest()
    {
        // A half-read block would be a count with no adapter behind it, which
        // reads afterwards as a machine that has proved something it never did.
        string path = TempPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "{ \"viewport\": { \"greenSessions\": 5, \"adapterLuid\": ");

        EditorSettings loaded = EditorSettings.Load(path, NullLogger.Instance);

        loaded.ViewportPreference.ShouldBe(ViewportPreference.Default);
    }

    [Fact]
    public void A_mode_word_this_build_does_not_know_reads_as_auto()
    {
        // The one thing that must not happen is a file written by a newer shell
        // stopping this one from starting.
        string path = TempPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "{ \"viewport\": { \"mode\": \"holographic\", \"greenSessions\": 3 } }");

        ViewportPreference loaded = EditorSettings.Load(path, NullLogger.Instance).ViewportPreference;

        loaded.Mode.ShouldBe(ViewportMode.Auto);
        loaded.GreenSessions.ShouldBe(3);
    }

    // --- the workspace block -------------------------------------------------

    [Fact]
    public void The_workspace_defaults_to_compact()
    {
        // The whole point of the preset: a fresh install gives the viewport the
        // room rather than the panels.
        var settings = new EditorSettings();

        settings.WorkspacePreset.ShouldBe(WorkspacePreset.Compact);
        settings.DrawerHeight.ShouldBe(WorkspaceLayout.DefaultDrawerHeight);
        settings.DiagnosticsReadouts.ShouldBeFalse();
    }

    [Fact]
    public void The_workspace_block_round_trips()
    {
        string path = TempPath();
        var settings = new EditorSettings();
        settings.SetWorkspacePreset(WorkspacePreset.Expanded);
        settings.SetDrawerHeight(212);
        settings.SetDiagnosticsReadouts(true);

        settings.Save(path, NullLogger.Instance);
        EditorSettings loaded = EditorSettings.Load(path, NullLogger.Instance);

        loaded.WorkspacePreset.ShouldBe(WorkspacePreset.Expanded);
        loaded.DrawerHeight.ShouldBe(212);
        loaded.DiagnosticsReadouts.ShouldBeTrue();
    }

    [Fact]
    public void A_file_with_no_workspace_block_reads_as_compact()
    {
        // Every settings file written before this existed.
        string path = TempPath();
        var settings = new EditorSettings();
        settings.TouchProject(@"C:\Games\Alpha", "Alpha", DateTime.UtcNow);
        settings.Save(path, NullLogger.Instance);

        string json = File.ReadAllText(path);
        json.ShouldContain("workspace");

        File.WriteAllText(path, json.Replace("workspace", "somethingelse"));
        EditorSettings loaded = EditorSettings.Load(path, NullLogger.Instance);

        loaded.WorkspacePreset.ShouldBe(WorkspacePreset.Compact);
        loaded.RecentProjects.Count.ShouldBe(1);
    }

    [Fact]
    public void The_workspace_block_merges_by_recency_like_every_other_one()
    {
        // Two shells open at once: the one that chose last wins, whole block at
        // a time, so a preset and its drawer height cannot come from different
        // sessions.
        string path = TempPath();

        var first = new EditorSettings();
        first.SetWorkspacePreset(WorkspacePreset.Expanded);
        first.SetDrawerHeight(300);
        first.Save(path, NullLogger.Instance);

        var second = EditorSettings.Load(path, NullLogger.Instance);
        second.WorkspacePreset.ShouldBe(WorkspacePreset.Expanded);

        // An older opinion must not overwrite it.
        var stale = new EditorSettings();
        stale.Save(path, NullLogger.Instance);

        EditorSettings loaded = EditorSettings.Load(path, NullLogger.Instance);
        loaded.WorkspacePreset.ShouldBe(WorkspacePreset.Expanded);
        loaded.DrawerHeight.ShouldBe(300);
    }

    [Fact]
    public void A_drawer_height_that_is_not_a_number_is_refused()
    {
        var settings = new EditorSettings();
        settings.SetDrawerHeight(double.NaN);
        settings.SetDrawerHeight(-40);

        settings.DrawerHeight.ShouldBe(WorkspaceLayout.DefaultDrawerHeight);
    }
    // --- The content block ---------------------------------------------------

    [Fact]
    public void The_content_view_defaults_to_the_grid_and_round_trips()
    {
        string path = TempPath();

        var settings = new EditorSettings();
        settings.ContentView.ShouldBe(ContentViewMode.Grid);

        settings.SetContentView(ContentViewMode.List);
        settings.Save(path, NullLogger.Instance);

        EditorSettings.Load(path, NullLogger.Instance).ContentView.ShouldBe(ContentViewMode.List);
    }

    [Fact]
    public void An_unknown_view_word_reads_as_the_grid()
    {
        string path = TempPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        // A settings file written by a newer shell must lose the setting it
        // cannot read rather than every setting beside it.
        File.WriteAllText(
            path,
            "{ \"content\": { \"viewMode\": \"gallery\", " +
            "\"recordedUtc\": \"2026-09-06T00:00:00.0000000Z\" } }");

        EditorSettings.Load(path, NullLogger.Instance).ContentView.ShouldBe(ContentViewMode.Grid);
    }

    [Fact]
    public void A_file_with_no_content_block_reads_as_the_grid()
    {
        string path = TempPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "{ }");

        EditorSettings.Load(path, NullLogger.Instance).ContentView.ShouldBe(ContentViewMode.Grid);
    }
}
