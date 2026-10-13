using Spectra.Kitchen.Tests;
using SpectraEngine.Core.Projects;
using SpectraEngine.Core.Scripting.Tooling;
using SpectraEngine.Editor.Shell;
using SpectraEngine.Editor.Shell.Scripts;

namespace SpectraEngine.Editor.Tests;

/// <summary>
/// What Output is told about the files a project gets for a code editor:
/// one line when they are written, one when they cannot be, and none after.
/// </summary>
// Nothing is started. The project is a real folder, since files are the subject.
public sealed class ScriptCommandsProjectTests : IDisposable
{
    private const string Code = @"C:\Programs\Microsoft VS Code\Code.exe";
    private const string Api = "-- the script API\n";
    private const string Opened = "Opened Spin.luau in Visual Studio Code.";
    private const string Wrote =
        "Wrote spectra.d.luau, .luaurc, .vscode/settings.json and .vscode/extensions.json into the project " +
        "folder. They set a code editor up for the project's scripts.";

    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    private readonly TempProject _project = new();
    private readonly ShellModel _shell = new();
    private readonly EditorDocument _document = new();
    private readonly FakeProgramStarter _starter = new();
    private readonly ScriptCommands _commands;

    // How often the project's folder was looked at: every look asks for the text.
    private int _definitionsAsked;

    public ScriptCommandsProjectTests()
    {
        _project.WriteAsset("Scripts/Spin.luau", "print('hello')\n");
        _commands = new ScriptCommands(
            _shell, _document, () => null,
            new ScriptLauncher(
                _starter, () => new ScriptEditorFacts(Code, true, FileManagerKind.Explorer),
                setup: new ScriptEditorSetup(() =>
                {
                    Interlocked.Increment(ref _definitionsAsked);
                    return Api;
                })),
            answer => answer());
    }

    public void Dispose() => _project.Dispose();

    // Every line of Output but the status of an open, which goes there too.
    private (OutputSeverity Severity, string Text)[] Said =>
        [.. _shell.Output.Entries.Where(entry => entry.Text != Opened).Select(entry => (entry.Severity, entry.Text))];

    private string DefinitionsPath => _project.Layout.Resolve(ScriptProjectFiles.Definitions);

    // Opens the project and waits for what that starts off this thread.
    private Task OpenProject()
    {
        _document.SetProject(_project.Layout);
        return _commands.ProjectRefresh.WaitAsync(Patience, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task The_first_script_opened_sets_the_project_up_and_output_says_so_once()
    {
        await OpenProject();

        await _commands.OpenAsync("Scripts/Spin.luau");
        await _commands.OpenAsync("Scripts/Spin.luau", line: 1);

        Said.ShouldBe([(OutputSeverity.Info, Wrote)]);
        _shell.Output.Entries[0].Text.ShouldBe(Wrote, "the files are written before the program starts");
        _shell.Message.ShouldBe(Opened);
        _starter.Asked.Count.ShouldBe(2);
        _starter.Asked[0].Arguments[0].ShouldBe(_project.Root);
    }

    [Fact]
    public async Task A_project_that_does_not_take_the_files_is_one_warning_and_the_script_opens()
    {
        Directory.CreateDirectory(DefinitionsPath);
        File.WriteAllText(_project.Layout.Resolve(".vscode"), "not a folder");
        await OpenProject();

        await _commands.OpenAsync("Scripts/Spin.luau");
        await _commands.OpenAsync("Scripts/Spin.luau", line: 1);

        // The second open is refused the same files, and that is not said again.
        (OutputSeverity severity, string text) = Said.ShouldHaveSingleItem();
        severity.ShouldBe(OutputSeverity.Warning);
        text.ShouldBe(
            "Wrote .luaurc into the project folder. It sets a code editor up for the project's scripts. " +
            "Could not write spectra.d.luau, .vscode/settings.json and .vscode/extensions.json into the " +
            "project folder. spectra.d.luau is a folder.");
        _shell.Message.ShouldBe(Opened);
        _shell.IsError.ShouldBeFalse();
        _starter.Asked.Count.ShouldBe(2);
    }

    [Fact]
    public async Task A_refusal_that_was_put_right_and_comes_back_is_said_again()
    {
        Directory.CreateDirectory(DefinitionsPath);
        await OpenProject();
        await _commands.OpenAsync("Scripts/Spin.luau");

        Directory.Delete(DefinitionsPath);
        await _commands.OpenAsync("Scripts/Spin.luau");
        File.Delete(DefinitionsPath);
        Directory.CreateDirectory(DefinitionsPath);
        await _commands.OpenAsync("Scripts/Spin.luau");

        Said.Select(line => line.Severity).ShouldBe(
            [OutputSeverity.Warning, OutputSeverity.Info, OutputSeverity.Warning]);
        Said[1].Text.ShouldBe(
            "Wrote spectra.d.luau into the project folder. It sets a code editor up for the project's scripts.");
        Said[2].Text.ShouldBe("Could not write spectra.d.luau into the project folder. spectra.d.luau is a folder.");
    }

    [Fact]
    public async Task A_level_with_no_project_gets_no_files_and_no_line()
    {
        await _commands.OpenFileAsync(Path.Combine(_project.Layout.AssetsPath, "Scripts", "Spin.luau"));

        Said.ShouldBeEmpty();
        _starter.Asked.ShouldHaveSingleItem().Arguments.Count.ShouldBe(1);
        File.Exists(DefinitionsPath).ShouldBeFalse();
    }

    [Fact]
    public async Task Opening_a_project_brings_its_old_definitions_file_up_and_says_so()
    {
        File.WriteAllText(DefinitionsPath, "-- an older script API\n");

        // Nobody asks for it: opening the project is what brings the file up.
        await OpenProject();

        Said.ShouldBe(
            [(OutputSeverity.Info, "Wrote spectra.d.luau again. It did not hold this engine's script API.")]);
        File.ReadAllText(DefinitionsPath).ShouldBe(Api);
        File.Exists(_project.Layout.Resolve(ScriptProjectFiles.LuauRc)).ShouldBeFalse();
    }

    [Fact]
    public async Task Opening_a_project_that_was_never_set_up_writes_nothing()
    {
        await OpenProject();

        Said.ShouldBeEmpty();
        File.Exists(DefinitionsPath).ShouldBeFalse();
        Directory.Exists(_project.Layout.Resolve(".vscode")).ShouldBeFalse();
    }

    [Fact]
    public async Task With_no_project_open_there_is_nothing_to_bring_up()
    {
        await _commands.RefreshProjectAsync();

        Said.ShouldBeEmpty();
    }

    // What the window does after a save: it reads the manifest again and
    // sets the project it read, which is another object for the same folder.
    private Task SaveLevel()
    {
        _document.SetProject(ProjectLayout.Open(_project.Layout.ManifestPath));
        return _commands.ProjectRefresh.WaitAsync(Patience, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task A_save_of_a_level_does_not_bring_the_project_up_again()
    {
        await OpenProject();
        int asked = _definitionsAsked;

        await SaveLevel();

        _definitionsAsked.ShouldBe(asked);
    }

    [Fact]
    public async Task A_refusal_that_stands_is_not_said_again_after_a_save()
    {
        Directory.CreateDirectory(DefinitionsPath);
        await OpenProject();
        await _commands.OpenAsync("Scripts/Spin.luau");

        await SaveLevel();
        await _commands.OpenAsync("Scripts/Spin.luau");

        Said.ShouldHaveSingleItem().Severity.ShouldBe(OutputSeverity.Warning);
    }

    [Fact]
    public async Task A_project_that_was_closed_and_opened_again_is_brought_up_again()
    {
        await OpenProject();
        int asked = _definitionsAsked;

        _document.SetProject(null);
        await OpenProject();

        _definitionsAsked.ShouldBe(asked + 1);
    }
}
