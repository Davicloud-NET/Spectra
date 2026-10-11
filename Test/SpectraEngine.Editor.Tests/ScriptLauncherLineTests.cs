using SpectraEngine.Editor.Shell;
using SpectraEngine.Editor.Shell.Scripts;

namespace SpectraEngine.Editor.Tests;

/// <summary>
/// Opening a script at a line: what becomes of a line the file no longer has,
/// and of one the program that opens the file cannot be handed.
/// </summary>
// Nothing is started. The starter is a fake that keeps what it was asked for.
public sealed class ScriptLauncherLineTests
{
    private const string Code = @"C:\Programs\Microsoft VS Code\Code.exe";
    private const string Project = @"D:\Games\Tower";
    private const string File = @"D:\Games\Tower\Assets\Scripts\Spin.luau";

    private readonly FakeProgramStarter _starter = new();

    private ScriptLauncher Launcher(int? linesInFile, string? code = Code, bool defaultProgram = true) => new(
        _starter,
        () => new ScriptEditorFacts(code, defaultProgram, FileManagerKind.Explorer),
        _ => true,
        _ => linesInFile);

    private static ScriptOpenRequest At(int line) => new(File, Project, line);

    [Fact]
    public void A_line_in_the_file_is_handed_over_and_nothing_is_said_about_it()
    {
        ScriptOpenOutcome outcome = Launcher(linesInFile: 12).Open(At(12));

        _starter.Asked.ShouldHaveSingleItem().Arguments.ShouldBe([Project, "--goto", File + ":12"]);
        outcome.Severity.ShouldBe(OutputSeverity.Info);
        outcome.Message.ShouldBe("Opened Spin.luau in Visual Studio Code.");
    }

    [Fact]
    public void The_line_after_the_last_line_break_is_still_in_the_file()
    {
        // Where a script that ends too soon fails: "expected 'end' near the end of the file".
        ScriptOpenOutcome outcome = Launcher(linesInFile: 12).Open(At(13));

        _starter.Asked.ShouldHaveSingleItem().Arguments.ShouldBe([Project, "--goto", File + ":13"]);
        outcome.Severity.ShouldBe(OutputSeverity.Info);
    }

    [Fact]
    public void A_line_past_the_end_opens_the_last_line_and_the_status_says_how_long_the_file_is()
    {
        ScriptOpenOutcome outcome = Launcher(linesInFile: 12).Open(At(40));

        _starter.Asked.ShouldHaveSingleItem().Arguments.ShouldBe([Project, "--goto", File + ":12"]);
        outcome.Way.ShouldBe(ScriptOpenWay.VisualStudioCode);
        outcome.Severity.ShouldBe(OutputSeverity.Warning);
        outcome.Message.ShouldBe("Opened Spin.luau in Visual Studio Code. Line 40 is past its end: it has 12 lines now.");
    }

    [Theory]
    [InlineData(0, "it has 0 lines now", 1)]
    [InlineData(1, "it has 1 line now", 1)]
    public void A_file_that_was_emptied_opens_at_its_first_line(int linesInFile, string says, int opensAt)
    {
        ScriptOpenOutcome outcome = Launcher(linesInFile).Open(At(9));

        _starter.Asked.ShouldHaveSingleItem().Arguments.ShouldBe([Project, "--goto", $"{File}:{opensAt}"]);
        outcome.Message.ShouldEndWith($"Line 9 is past its end: {says}.");
    }

    [Fact]
    public void A_file_whose_length_is_not_known_is_opened_at_the_line_as_asked()
    {
        ScriptOpenOutcome outcome = Launcher(linesInFile: null).Open(At(40));

        _starter.Asked.ShouldHaveSingleItem().Arguments.ShouldBe([Project, "--goto", File + ":40"]);
        outcome.Severity.ShouldBe(OutputSeverity.Info);
    }

    [Fact]
    public void The_systems_program_cannot_be_handed_a_line_so_the_status_names_it()
    {
        ScriptOpenOutcome outcome = Launcher(linesInFile: 12, code: null).Open(At(7));

        _starter.Asked.ShouldHaveSingleItem().FileName.ShouldBe(File);
        outcome.Severity.ShouldBe(OutputSeverity.Info);
        outcome.Message.ShouldBe(
            "Opened Spin.luau in the program your system has for .luau files. Look at line 7.");
    }

    [Fact]
    public void A_file_shown_in_its_folder_names_the_line_too()
    {
        ScriptOpenOutcome outcome = Launcher(linesInFile: 12, code: null, defaultProgram: false).Open(At(7));

        outcome.Way.ShouldBe(ScriptOpenWay.FileManager);
        outcome.Message.ShouldBe(
            "No program is set for .luau files. Spin.luau is shown in its folder. Look at line 7.");
    }

    [Fact]
    public void An_open_that_names_no_line_says_nothing_of_one_and_reads_no_file()
    {
        int reads = 0;
        var launcher = new ScriptLauncher(
            _starter, () => new ScriptEditorFacts(null, true, FileManagerKind.Explorer), _ => true,
            _ =>
            {
                reads++;
                return 12;
            });

        ScriptOpenOutcome outcome = launcher.Open(new ScriptOpenRequest(File, Project));

        outcome.Message.ShouldBe("Opened Spin.luau in the program your system has for .luau files.");
        reads.ShouldBe(0);
    }
}
