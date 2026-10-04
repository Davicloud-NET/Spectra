using SpectraEngine.Editor.Shell;
using System;
using System.Collections.Generic;
using System.IO;

namespace SpectraEngine.Editor.Tests;

/// <summary>
/// Recent project paths are shortened from the left, so the last folders survive.
/// </summary>
public sealed class RecentLocationTests
{
    private static string P(params string[] segments) =>
        string.Join(Path.DirectorySeparatorChar, segments);

    private static RecentProject Recent(string name, string path) =>
        new(path, name, DateTime.UtcNow);

    [Fact]
    public void A_short_path_is_shown_whole()
    {
        RecentLocation.Shorten(P("D:", "Demo"), 3).ShouldBe(P("D:", "Demo"));
    }

    [Fact]
    public void A_long_path_keeps_its_last_folders_and_marks_the_cut()
    {
        string shortened = RecentLocation.Shorten(P("D:", "Users", "David", "Projects", "Spectra", "Demo"), 3);

        shortened.ShouldBe(RecentLocation.Cut + Path.DirectorySeparatorChar + P("Projects", "Spectra", "Demo"));
        shortened.ShouldEndWith("Demo");
    }

    [Fact]
    public void Mixed_and_trailing_separators_normalise()
    {
        RecentLocation.Shorten("D:/Games/Demo/", 3).ShouldBe(P("D:", "Games", "Demo"));
    }

    [Fact]
    public void An_empty_path_yields_nothing_rather_than_a_cut_marker()
    {
        RecentLocation.Shorten(null, 3).ShouldBeEmpty();
        RecentLocation.Shorten("   ", 3).ShouldBeEmpty();
    }

    [Fact]
    public void Two_projects_with_one_name_are_lengthened_until_they_differ()
    {
        List<RecentProject> recents =
        [
            Recent("Demo", P("D:", "Work", "ClientA", "Spectra", "Demo")),
            Recent("Demo", P("D:", "Work", "ClientB", "Spectra", "Demo")),
        ];

        IReadOnlyList<string> locations = RecentLocation.Locations(recents);

        locations[0].ShouldNotBe(locations[1]);
        locations[0].ShouldContain("ClientA");
        locations[1].ShouldContain("ClientB");
    }

    [Fact]
    public void A_row_with_its_own_name_is_not_lengthened_for_somebody_elses_clash()
    {
        List<RecentProject> recents =
        [
            Recent("Demo", P("D:", "Work", "ClientA", "Spectra", "Demo")),
            Recent("Demo", P("D:", "Work", "ClientB", "Spectra", "Demo")),
            Recent("Sandbox", P("D:", "Work", "ClientC", "Spectra", "Sandbox")),
        ];

        IReadOnlyList<string> locations = RecentLocation.Locations(recents);

        locations[2].ShouldBe(
            RecentLocation.Cut + Path.DirectorySeparatorChar + P("ClientC", "Spectra", "Sandbox"));
    }

    [Fact]
    public void Two_identical_paths_stay_identical_rather_than_growing_forever()
    {
        // Must terminate. Duplicate entries are the settings layer's problem.
        List<RecentProject> recents =
        [
            Recent("Demo", P("D:", "Games", "Demo")),
            Recent("Demo", P("D:", "Games", "Demo")),
        ];

        IReadOnlyList<string> locations = RecentLocation.Locations(recents);

        locations[0].ShouldBe(locations[1]);
    }

    [Fact]
    public void One_project_needs_no_disambiguation()
    {
        List<RecentProject> recents = [Recent("Demo", P("D:", "A", "B", "C", "Demo"))];

        RecentLocation.Locations(recents)[0]
            .ShouldBe(RecentLocation.Cut + Path.DirectorySeparatorChar + P("B", "C", "Demo"));
    }
}
