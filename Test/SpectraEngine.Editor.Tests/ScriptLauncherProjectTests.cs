using Spectra.Kitchen.Tests;
using SpectraEngine.Core.Projects;
using SpectraEngine.Core.Scripting.Tooling;
using SpectraEngine.Editor.Shell;
using SpectraEngine.Editor.Shell.Scripts;

namespace SpectraEngine.Editor.Tests;

/// <summary>
/// Opening a script of a project on disk: what Visual Studio Code is handed,
/// and that the project's files for it are there before it starts.
/// </summary>
// Nothing is started. The starter is a fake that keeps what it was asked for.
public sealed class ScriptLauncherProjectTests : IDisposable
{
    private const string Code = @"C:\Programs\Microsoft VS Code\Code.exe";
    private const string Api = "-- the script API\n";

    private static readonly string[] All =
    [
        ScriptProjectFiles.Definitions, ScriptProjectFiles.LuauRc,
        ScriptProjectFiles.CodeSettings, ScriptProjectFiles.CodeExtensions,
    ];

    private readonly TempProject _temp = new();
    private readonly FakeProgramStarter _starter = new();
    private readonly ProjectLayout _project;
    private readonly string _script;

    public ScriptLauncherProjectTests()
    {
        // A space in the folder and in the file, as a person names them.
        _project = ProjectLayout.Create(Path.Combine(_temp.Root, "My Game"), "My Game");
        _script = Path.Combine(_project.AssetsPath, "Scripts", "Front Door.luau");
        Directory.CreateDirectory(Path.Combine(_project.AssetsPath, "Scripts"));
        File.WriteAllText(_script, "print('hello')\n");
    }

    public void Dispose() => _temp.Dispose();

    private ScriptLauncher Launcher(string? code = Code) => new(
        _starter,
        () => new ScriptEditorFacts(code, true, FileManagerKind.Explorer),
        setup: new ScriptEditorSetup(() => Api));

    private string[] Missing() => [.. All.Where(file => !File.Exists(_project.Resolve(file)))];

    [Fact]
    public void A_script_opens_on_its_projects_folder_with_the_file_after_it()
    {
        ScriptOpenOutcome outcome = Launcher().Open(new ScriptOpenRequest(_script, _project.Root));

        // One argument each, spaces and all: the folder is the window, the file a tab in it.
        ProgramStart start = _starter.Asked.ShouldHaveSingleItem();
        start.FileName.ShouldBe(Code);
        start.Arguments.ShouldBe([_project.Root, _script]);
        start.UseShell.ShouldBeFalse();
        outcome.Message.ShouldBe("Opened Front Door.luau in Visual Studio Code.");
    }

    [Fact]
    public void A_line_goes_with_the_file_and_the_folder_stays_first()
    {
        Launcher().Open(new ScriptOpenRequest(_script, _project.Root, 1));

        _starter.Asked.ShouldHaveSingleItem().Arguments.ShouldBe([_project.Root, "--goto", _script + ":1"]);
    }

    [Fact]
    public void The_projects_files_are_there_before_the_program_is_asked_to_start()
    {
        // luau-lsp reads its settings once, when the window opens.
        string[]? missingAtStart = null;
        _starter.OnAsked = _ => missingAtStart = Missing();

        ScriptOpenOutcome outcome = Launcher().Open(new ScriptOpenRequest(_script, _project.Root));

        missingAtStart.ShouldNotBeNull().ShouldBeEmpty();
        outcome.Project.Written.ShouldBe(All);
        File.ReadAllText(_project.Resolve(ScriptProjectFiles.Definitions)).ShouldBe(Api);
    }

    [Fact]
    public void A_second_open_writes_nothing()
    {
        ScriptLauncher launcher = Launcher();
        launcher.Open(new ScriptOpenRequest(_script, _project.Root));

        ScriptOpenOutcome second = launcher.Open(new ScriptOpenRequest(_script, _project.Root, 1));

        second.Project.ShouldBeSameAs(ScriptProjectReport.Nothing);
        second.Severity.ShouldBe(OutputSeverity.Info);
    }

    [Fact]
    public void A_script_of_no_project_opens_alone_and_nothing_is_written()
    {
        ScriptOpenOutcome outcome = Launcher().Open(new ScriptOpenRequest(_script, null, 1));

        _starter.Asked.ShouldHaveSingleItem().Arguments.ShouldBe(["--goto", _script + ":1"]);
        outcome.Project.ShouldBeSameAs(ScriptProjectReport.Nothing);
        Missing().ShouldBe(All);
    }

    [Fact]
    public void Without_visual_studio_code_the_files_are_written_all_the_same()
    {
        // It may be installed tomorrow, and the files are what it needs then.
        ScriptOpenOutcome outcome = Launcher(code: null).Open(new ScriptOpenRequest(_script, _project.Root));

        outcome.Way.ShouldBe(ScriptOpenWay.DefaultProgram);
        Missing().ShouldBeEmpty();
    }

    [Fact]
    public void A_file_that_is_no_script_sets_no_project_up()
    {
        string batch = Path.Combine(_project.AssetsPath, "Scripts", "setup.bat");
        File.WriteAllText(batch, "echo");

        ScriptOpenOutcome outcome = Launcher().Open(new ScriptOpenRequest(batch, _project.Root));

        outcome.Way.ShouldBe(ScriptOpenWay.FileManager);
        Missing().ShouldBe(All);
    }

    [Fact]
    public void A_script_that_is_not_on_disk_sets_no_project_up()
    {
        string gone = Path.Combine(_project.AssetsPath, "Scripts", "Gone.luau");

        Launcher().Open(new ScriptOpenRequest(gone, _project.Root)).Way.ShouldBeNull();

        _starter.Asked.ShouldBeEmpty();
        Missing().ShouldBe(All);
    }

    [Fact]
    public void A_project_that_does_not_take_the_files_still_opens_its_script()
    {
        Directory.CreateDirectory(_project.Resolve(ScriptProjectFiles.Definitions));
        File.WriteAllText(_project.Resolve(".vscode"), "not a folder");

        ScriptOpenOutcome outcome = Launcher().Open(new ScriptOpenRequest(_script, _project.Root));

        outcome.Way.ShouldBe(ScriptOpenWay.VisualStudioCode);
        outcome.Severity.ShouldBe(OutputSeverity.Info);
        outcome.Failures.ShouldBeEmpty();
        outcome.Project.Refused.ShouldBe(
            [ScriptProjectFiles.Definitions, ScriptProjectFiles.CodeSettings, ScriptProjectFiles.CodeExtensions]);
    }

    [Fact]
    public void A_setup_that_throws_is_a_report_and_the_script_still_opens()
    {
        var launcher = new ScriptLauncher(
            _starter, () => new ScriptEditorFacts(Code, true, FileManagerKind.Explorer),
            setup: new ScriptEditorSetup(() => throw new InvalidOperationException("The description is broken")));

        ScriptOpenOutcome outcome = launcher.Open(new ScriptOpenRequest(_script, _project.Root));

        outcome.Way.ShouldBe(ScriptOpenWay.VisualStudioCode);
        outcome.Project.Describe().ShouldBe(
            "Could not write spectra.d.luau into the project folder. The description is broken.");
    }

    [Fact]
    public void A_launcher_that_was_given_no_setup_writes_nothing()
    {
        var launcher = new ScriptLauncher(_starter, () => new ScriptEditorFacts(Code, true, FileManagerKind.Explorer));

        launcher.Open(new ScriptOpenRequest(_script, _project.Root)).Project.ShouldBeSameAs(ScriptProjectReport.Nothing);

        Missing().ShouldBe(All);
    }
}
