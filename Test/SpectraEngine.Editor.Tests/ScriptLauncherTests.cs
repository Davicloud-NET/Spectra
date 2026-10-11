using SpectraEngine.Editor.Shell;
using SpectraEngine.Editor.Shell.Scripts;

namespace SpectraEngine.Editor.Tests;

/// <summary>
/// Opening a script: what is started, what the status bar is told, and what
/// happens when the program that should open it does not start.
/// </summary>
// Nothing is started. The starter is a fake that keeps what it was asked for.
public sealed class ScriptLauncherTests
{
    private const string Code = @"C:\Programs\Microsoft VS Code\Code.exe";
    private const string Project = @"D:\Games\Tower";
    private const string File = @"D:\Games\Tower\Assets\Scripts\Spin.luau";

    private static readonly ScriptOpenRequest Request = new(File, Project);

    private sealed class Rig
    {
        public FakeProgramStarter Starter { get; } = new();
        public ScriptEditorFacts Facts { get; set; }
        public bool FileIsThere { get; set; } = true;

        public ScriptLauncher Launcher => new(Starter, () => Facts, _ => FileIsThere);
    }

    private static Rig With(string? code = null, bool defaultProgram = false) => new()
    {
        Facts = new ScriptEditorFacts(code, defaultProgram, FileManagerKind.Explorer),
    };

    [Fact]
    public void With_visual_studio_code_the_script_opens_there_and_the_status_says_so()
    {
        Rig rig = With(Code, defaultProgram: true);

        ScriptOpenOutcome outcome = rig.Launcher.Open(Request);

        rig.Starter.Asked.ShouldHaveSingleItem().FileName.ShouldBe(Code);
        outcome.Way.ShouldBe(ScriptOpenWay.VisualStudioCode);
        outcome.IsOpenInEditor.ShouldBeTrue();
        outcome.Severity.ShouldBe(OutputSeverity.Info);
        outcome.Message.ShouldBe("Opened Spin.luau in Visual Studio Code.");
        outcome.Failures.ShouldBeEmpty();
    }

    [Fact]
    public void Without_it_the_systems_program_opens_the_script()
    {
        Rig rig = With(defaultProgram: true);

        ScriptOpenOutcome outcome = rig.Launcher.Open(Request);

        rig.Starter.Asked.ShouldHaveSingleItem().FileName.ShouldBe(File);
        outcome.Way.ShouldBe(ScriptOpenWay.DefaultProgram);
        outcome.Severity.ShouldBe(OutputSeverity.Info);
        outcome.Message.ShouldBe("Opened Spin.luau in the program your system has for .luau files.");
    }

    [Fact]
    public void With_no_program_for_the_file_it_is_shown_in_its_folder_and_the_status_says_why()
    {
        Rig rig = With();

        ScriptOpenOutcome outcome = rig.Launcher.Open(Request);

        rig.Starter.Asked.ShouldHaveSingleItem().FileName.ShouldBe("explorer.exe");
        outcome.Way.ShouldBe(ScriptOpenWay.FileManager);
        outcome.IsOpenInEditor.ShouldBeFalse();
        outcome.Severity.ShouldBe(OutputSeverity.Warning);
        outcome.Message.ShouldBe("No program is set for .luau files. Spin.luau is shown in its folder.");
    }

    [Fact]
    public void When_visual_studio_code_is_there_and_does_not_start_the_next_way_is_taken()
    {
        Rig rig = With(Code, defaultProgram: true);
        rig.Starter.Break(Code, "Access is denied.");

        ScriptOpenOutcome outcome = rig.Launcher.Open(Request);

        rig.Starter.Asked.Select(start => start.FileName).ShouldBe([Code, File]);
        outcome.Way.ShouldBe(ScriptOpenWay.DefaultProgram);
        outcome.Severity.ShouldBe(OutputSeverity.Warning);
        outcome.Message.ShouldBe(
            "Visual Studio Code did not start. Opened Spin.luau in the program your system has for .luau files.");
        outcome.Failures.ShouldBe([$"Visual Studio Code did not start ({Code}): Access is denied."]);
    }

    [Fact]
    public void When_it_does_not_start_and_nothing_else_opens_luau_the_file_is_shown_in_its_folder()
    {
        Rig rig = With(Code);
        rig.Starter.Break(Code, "The file is not a program.");

        ScriptOpenOutcome outcome = rig.Launcher.Open(Request);

        rig.Starter.Asked.Select(start => start.FileName).ShouldBe([Code, "explorer.exe"]);
        outcome.Way.ShouldBe(ScriptOpenWay.FileManager);
        outcome.Severity.ShouldBe(OutputSeverity.Warning);
        outcome.Message.ShouldBe(
            "Visual Studio Code did not start. No program is set for .luau files. " +
            "Spin.luau is shown in its folder.");
        outcome.Failures.ShouldHaveSingleItem();
    }

    [Fact]
    public void When_the_systems_program_does_not_start_the_file_is_shown_in_its_folder()
    {
        Rig rig = With(defaultProgram: true);
        rig.Starter.Break(File, "The program it was set to is gone.");

        ScriptOpenOutcome outcome = rig.Launcher.Open(Request);

        outcome.Way.ShouldBe(ScriptOpenWay.FileManager);
        outcome.Message.ShouldBe(
            "The system's program for .luau files did not start. Spin.luau is shown in its folder.");
        outcome.Failures.ShouldBe(
            [$"The system's program for .luau files did not start ({File}): The program it was set to is gone."]);
    }

    [Fact]
    public void When_nothing_starts_the_status_says_so_and_every_failure_is_a_line()
    {
        Rig rig = With(Code, defaultProgram: true);
        rig.Starter.Break(Code, "one");
        rig.Starter.Break(File, "two");
        rig.Starter.Break("explorer.exe", "three");

        ScriptOpenOutcome outcome = rig.Launcher.Open(Request);

        outcome.Way.ShouldBeNull();
        outcome.Severity.ShouldBe(OutputSeverity.Error);
        outcome.Message.ShouldBe("Spin.luau could not be opened or shown in its folder.");
        outcome.Failures.Count.ShouldBe(3);
        outcome.Failures[2].ShouldBe("The file manager did not start (explorer.exe): three");
    }

    [Fact]
    public void A_script_that_is_not_on_disk_starts_nothing()
    {
        Rig rig = With(Code, defaultProgram: true);
        rig.FileIsThere = false;

        ScriptOpenOutcome outcome = rig.Launcher.Open(Request);

        rig.Starter.Asked.ShouldBeEmpty();
        outcome.Way.ShouldBeNull();
        outcome.Severity.ShouldBe(OutputSeverity.Error);

        // Also said of a path picked before its file was written.
        outcome.Message.ShouldBe("Spin.luau is not on disk.");
    }

    [Fact]
    public void A_file_that_is_no_script_is_never_started_and_the_status_says_why()
    {
        // A level from somebody else can name a batch file as its script.
        const string batch = @"D:\Games\Tower\Assets\Scripts\setup.bat";
        Rig rig = With(Code, defaultProgram: true);

        ScriptOpenOutcome outcome = rig.Launcher.Open(new ScriptOpenRequest(batch, Project));

        ProgramStart only = rig.Starter.Asked.ShouldHaveSingleItem();
        only.FileName.ShouldBe("explorer.exe");
        only.UseShell.ShouldBeFalse();
        outcome.Way.ShouldBe(ScriptOpenWay.FileManager);
        outcome.IsOpenInEditor.ShouldBeFalse();
        outcome.Severity.ShouldBe(OutputSeverity.Warning);
        outcome.Message.ShouldBe(
            "setup.bat is not a .luau file, so no program was started for it. setup.bat is shown in its folder.");
    }

    [Fact]
    public void A_file_that_is_no_script_with_no_file_manager_to_show_it_starts_nothing()
    {
        var rig = new Rig { Facts = new ScriptEditorFacts(Code, true, FileManagerKind.None) };

        ScriptOpenOutcome outcome = rig.Launcher.Open(
            new ScriptOpenRequest(@"D:\Games\Tower\Assets\Scripts\setup.bat", Project));

        rig.Starter.Asked.ShouldBeEmpty();
        outcome.Way.ShouldBeNull();
        outcome.Severity.ShouldBe(OutputSeverity.Error);
        outcome.Message.ShouldBe("setup.bat is not a .luau file, so no program was started for it.");
    }

    [Fact]
    public void The_computer_is_looked_at_again_on_every_open()
    {
        // A code editor installed while this one runs is found without a restart.
        Rig rig = With();
        ScriptLauncher launcher = rig.Launcher;
        launcher.Open(Request).Way.ShouldBe(ScriptOpenWay.FileManager);

        rig.Facts = new ScriptEditorFacts(Code, false, FileManagerKind.Explorer);

        launcher.Open(Request).Way.ShouldBe(ScriptOpenWay.VisualStudioCode);
    }

    [Fact]
    public async Task Opened_off_the_calling_thread_the_answer_is_the_same()
    {
        Rig rig = With(Code);

        ScriptOpenOutcome outcome = await rig.Launcher.OpenAsync(Request with { Line = 7 });

        outcome.Way.ShouldBe(ScriptOpenWay.VisualStudioCode);
        rig.Starter.Asked.ShouldHaveSingleItem().Arguments.ShouldBe([Project, "--goto", File + ":7"]);
    }

    [Fact]
    public async Task A_survey_that_throws_becomes_a_line_and_not_an_exception()
    {
        var launcher = new ScriptLauncher(
            new FakeProgramStarter(), () => throw new InvalidOperationException("the registry is locked"), _ => true);

        ScriptOpenOutcome outcome = await launcher.OpenAsync(Request);

        outcome.Way.ShouldBeNull();
        outcome.Severity.ShouldBe(OutputSeverity.Error);
        outcome.Failures.ShouldBe(["the registry is locked"]);
    }
}
