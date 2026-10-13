using System.Text;
using SpectraEngine.Core.Scripting.Tooling;

namespace SpectraEngine.Scripting.Tests;

/// <summary>
/// The files a project gets for a code editor: what is written the first
/// time, what is left alone after that, and what happens in a folder that
/// does not take them.
/// </summary>
public sealed class ScriptProjectSetupTests : IDisposable
{
    private const string Api = "-- the script API\ndeclare game: DataModel\n";

    private static readonly string[] All =
    [
        ScriptProjectFiles.Definitions, ScriptProjectFiles.LuauRc,
        ScriptProjectFiles.CodeSettings, ScriptProjectFiles.CodeExtensions,
    ];

    private readonly ProjectFolder _project = new();

    public void Dispose() => _project.Dispose();

    public static TheoryData<string> PersonsFiles() =>
        [ScriptProjectFiles.LuauRc, ScriptProjectFiles.CodeSettings, ScriptProjectFiles.CodeExtensions];

    public static TheoryData<string> EveryFile() => [.. All];

    // The text as an editor saves it that puts a byte order mark first.
    private static byte[] Marked(string text) => [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(text)];

    [Fact]
    public void A_project_gets_the_four_files_each_with_its_text()
    {
        ScriptProjectReport report = ScriptProjectSetup.Prepare(_project.Root, Api);

        report.Written.ShouldBe(All);
        report.IsRefused.ShouldBeFalse();
        _project.Read("spectra.d.luau").ShouldBe(Api);
        _project.Read(".luaurc").ShouldBe(ScriptProjectFiles.LuauRcText());
        _project.Read(".vscode/settings.json").ShouldBe(ScriptProjectFiles.CodeSettingsText());
        _project.Read(".vscode/extensions.json").ShouldBe(ScriptProjectFiles.CodeExtensionsText());
    }

    [Fact]
    public void No_file_starts_with_a_byte_order_mark()
    {
        ScriptProjectSetup.Prepare(_project.Root, Api);

        foreach (string file in All)
            File.ReadAllBytes(_project.PathOf(file))[0].ShouldBeOneOf((byte)'-', (byte)'{');
    }

    [Fact]
    public void A_second_run_writes_nothing_and_touches_no_files_time()
    {
        ScriptProjectSetup.Prepare(_project.Root, Api);
        var longAgo = new DateTime(2001, 2, 3, 4, 5, 6, DateTimeKind.Utc);
        foreach (string file in All)
            File.SetLastWriteTimeUtc(_project.PathOf(file), longAgo);

        ScriptProjectReport second = ScriptProjectSetup.Prepare(_project.Root, Api);

        second.ShouldBeSameAs(ScriptProjectReport.Nothing);
        second.Describe().ShouldBeNull();
        foreach (string file in All)
            File.GetLastWriteTimeUtc(_project.PathOf(file)).ShouldBe(longAgo, file);
    }

    [Theory]
    [MemberData(nameof(PersonsFiles))]
    public void A_file_the_person_has_is_kept_also_when_it_is_not_valid_json(string file)
    {
        const string theirs = "{ // mine\n  \"luau-lsp.platform.type\": \"roblox\",, }";
        _project.Write(file, theirs);

        ScriptProjectReport report = ScriptProjectSetup.Prepare(_project.Root, Api);

        _project.Read(file).ShouldBe(theirs);
        report.Written.ShouldBe(All.Where(name => name != file));
        report.IsRefused.ShouldBeFalse();
    }

    [Theory]
    [MemberData(nameof(PersonsFiles))]
    public void A_file_the_person_has_is_kept_also_when_it_is_empty(string file)
    {
        _project.Write(file, string.Empty);

        ScriptProjectReport report = ScriptProjectSetup.Prepare(_project.Root, Api);

        _project.Read(file).ShouldBeEmpty();
        report.Written.ShouldBe(All.Where(name => name != file));
        report.IsRefused.ShouldBeFalse();
    }

    [Theory]
    [MemberData(nameof(PersonsFiles))]
    public void A_file_the_person_has_and_nobody_may_write_is_kept_and_is_no_refusal(string file)
    {
        _project.Write(file, "{}");
        File.SetAttributes(_project.PathOf(file), FileAttributes.ReadOnly);

        ScriptProjectReport report = ScriptProjectSetup.Prepare(_project.Root, Api);

        _project.Read(file).ShouldBe("{}");
        report.Written.ShouldBe(All.Where(name => name != file));
        report.IsRefused.ShouldBeFalse();
    }

    [Fact]
    public void A_definitions_file_with_another_text_is_written_again()
    {
        ScriptProjectSetup.Prepare(_project.Root, "-- an older script API\n");

        ScriptProjectReport report = ScriptProjectSetup.Prepare(_project.Root, Api);

        _project.Read("spectra.d.luau").ShouldBe(Api);
        report.Written.ShouldBe(["spectra.d.luau"]);
        report.IsRenewal.ShouldBeTrue();
        report.Describe().ShouldBe("Wrote spectra.d.luau again. It did not hold this engine's script API.");
    }

    [Fact]
    public void A_definitions_file_that_differs_only_in_its_line_ends_is_left_alone()
    {
        // What a checkout on Windows can make of it.
        _project.Write("spectra.d.luau", Api.Replace("\n", "\r\n"));

        ScriptProjectReport report = ScriptProjectSetup.Prepare(_project.Root, Api);

        report.Written.ShouldNotContain("spectra.d.luau");
        _project.Read("spectra.d.luau").ShouldContain("\r\n");
    }

    [Fact]
    public void A_definitions_file_behind_a_byte_order_mark_is_written_again_without_it()
    {
        ScriptProjectSetup.Prepare(_project.Root, Api);
        File.WriteAllBytes(_project.PathOf("spectra.d.luau"), Marked(Api));

        ScriptProjectReport report = ScriptProjectSetup.Prepare(_project.Root, Api);

        File.ReadAllBytes(_project.PathOf("spectra.d.luau")).ShouldBe(Encoding.UTF8.GetBytes(Api));
        report.Describe().ShouldBe(
            "Wrote spectra.d.luau again, without the byte order mark it had. luau-lsp does not read the file " +
            "with one.");
    }

    [Fact]
    public void An_old_definitions_file_behind_a_byte_order_mark_is_said_to_be_old()
    {
        ScriptProjectSetup.Prepare(_project.Root, Api);
        File.WriteAllBytes(_project.PathOf("spectra.d.luau"), Marked("-- an older script API\n"));

        ScriptProjectReport report = ScriptProjectSetup.Prepare(_project.Root, Api);

        File.ReadAllBytes(_project.PathOf("spectra.d.luau")).ShouldBe(Encoding.UTF8.GetBytes(Api));
        report.Describe().ShouldBe("Wrote spectra.d.luau again. It did not hold this engine's script API.");
    }

    [Fact]
    public void Refresh_leaves_a_project_that_was_never_set_up_alone()
    {
        ScriptProjectReport report = ScriptProjectSetup.Refresh(_project.Root, Api);

        report.ShouldBeSameAs(ScriptProjectReport.Nothing);
        Directory.EnumerateFileSystemEntries(_project.Root).Select(Path.GetFileName).ShouldBe(["Assets"]);
    }

    [Fact]
    public void Refresh_brings_an_old_definitions_file_up_and_writes_nothing_else()
    {
        _project.Write("spectra.d.luau", "-- an older script API\n");

        ScriptProjectReport report = ScriptProjectSetup.Refresh(_project.Root, Api);

        report.Written.ShouldBe(["spectra.d.luau"]);
        _project.Read("spectra.d.luau").ShouldBe(Api);
        _project.Has(".luaurc").ShouldBeFalse();
        Directory.Exists(_project.PathOf(".vscode")).ShouldBeFalse();
        ScriptProjectSetup.Refresh(_project.Root, Api).ShouldBeSameAs(ScriptProjectReport.Nothing);
    }

    [Fact]
    public void Refresh_takes_a_folder_of_that_name_for_no_definitions_file()
    {
        Directory.CreateDirectory(_project.PathOf("spectra.d.luau"));

        ScriptProjectSetup.Refresh(_project.Root, Api).ShouldBeSameAs(ScriptProjectReport.Nothing);
    }

    [Theory]
    [MemberData(nameof(EveryFile))]
    public void A_file_that_is_there_as_a_folder_is_one_line_and_the_others_are_written(string file)
    {
        Directory.CreateDirectory(_project.PathOf(file));

        ScriptProjectReport report = ScriptProjectSetup.Prepare(_project.Root, Api);

        report.Refused.ShouldBe([file]);
        report.Reason.ShouldBe($"{file} is a folder.");
        report.Written.ShouldBe(All.Where(name => name != file));
        report.Describe().ShouldNotBeNull().ShouldEndWith(
            $"Could not write {file} into the project folder. {file} is a folder.");
    }

    [Fact]
    public void A_vscode_that_is_a_file_refuses_both_of_its_files_in_one_line()
    {
        _project.Write(".vscode", "not a folder");

        ScriptProjectReport report = ScriptProjectSetup.Prepare(_project.Root, Api);

        report.Written.ShouldBe(["spectra.d.luau", ".luaurc"]);
        report.Refused.ShouldBe([".vscode/settings.json", ".vscode/extensions.json"]);
        report.Reason.ShouldNotBeEmpty();
        string line = report.Describe().ShouldNotBeNull();
        line.ShouldStartWith(
            "Wrote spectra.d.luau and .luaurc into the project folder. They set a code editor up for the " +
            "project's scripts. Could not write .vscode/settings.json and .vscode/extensions.json into the " +
            "project folder. ");
        line.ShouldNotContain("\n");
        _project.Read(".vscode").ShouldBe("not a folder");
    }

    [Fact]
    public void A_definitions_file_that_cannot_be_written_is_reported_and_the_rest_is_written()
    {
        Assert.SkipWhen(
            !OperatingSystem.IsWindows() && Environment.IsPrivilegedProcess,
            "This process is root, which writes through a read only file. Nothing can be refused here.");

        _project.Write("spectra.d.luau", "-- an older script API\n");
        File.SetAttributes(_project.PathOf("spectra.d.luau"), FileAttributes.ReadOnly);

        ScriptProjectReport report = ScriptProjectSetup.Prepare(_project.Root, Api);

        report.Refused.ShouldBe(["spectra.d.luau"]);
        report.Reason.ShouldEndWith(".");
        report.Written.ShouldBe([".luaurc", ".vscode/settings.json", ".vscode/extensions.json"]);
        _project.Read("spectra.d.luau").ShouldBe("-- an older script API\n");
    }

    [Fact]
    public void A_project_folder_that_is_not_there_is_not_made()
    {
        string gone = Path.Combine(_project.Root, "Deleted");

        ScriptProjectSetup.Prepare(gone, Api).ShouldBeSameAs(ScriptProjectReport.Nothing);
        ScriptProjectSetup.Refresh(gone, Api).ShouldBeSameAs(ScriptProjectReport.Nothing);

        Directory.Exists(gone).ShouldBeFalse();
    }

    [Fact]
    public void The_first_setup_is_one_line_that_names_every_file()
    {
        ScriptProjectSetup.Prepare(_project.Root, Api).Describe().ShouldBe(
            "Wrote spectra.d.luau, .luaurc, .vscode/settings.json and .vscode/extensions.json into the project " +
            "folder. They set a code editor up for the project's scripts.");
    }

    [Fact]
    public void One_missing_file_is_named_alone()
    {
        ScriptProjectSetup.Prepare(_project.Root, Api);
        File.Delete(_project.PathOf(".luaurc"));

        ScriptProjectSetup.Prepare(_project.Root, Api).Describe().ShouldBe(
            "Wrote .luaurc into the project folder. It sets a code editor up for the project's scripts.");
    }

    [Fact]
    public async Task Runs_at_the_same_time_write_each_file_once_and_refuse_none()
    {
        // Two scripts opened at once each set the project up.
        ScriptProjectReport[] reports = await Task.WhenAll(
            Enumerable.Range(0, 8).Select(_ => Task.Run(() => ScriptProjectSetup.Prepare(_project.Root, Api))));

        reports.ShouldAllBe(report => !report.IsRefused);
        reports.SelectMany(report => report.Written).Order().ShouldBe(All.Order());
    }
}
