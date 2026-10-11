using SpectraEngine.Core.Assets;
using SpectraEngine.Editor.Shell;
using SpectraEngine.Editor.Shell.Scripts;

namespace SpectraEngine.Editor.Tests;

/// <summary>
/// What the window does with a script, as far as it goes with no level open:
/// what it refuses, what it hands the launcher, and what the status bar and
/// Output are told.
/// </summary>
// Nothing is started. With no session, nothing reaches an engine either.
public sealed class ScriptCommandsTests
{
    private const string Code = @"C:\Programs\Microsoft VS Code\Code.exe";

    private sealed class Rig
    {
        public ShellModel Shell { get; } = new();
        public FakeProgramStarter Starter { get; } = new();
        public ScriptEditorFacts Facts { get; set; } = new(Code, true, FileManagerKind.Explorer);
        public bool FileIsThere { get; set; } = true;
        public ScriptCommands Commands { get; }

        public Rig() => Commands = new ScriptCommands(
            Shell, new EditorDocument(), () => null,
            new ScriptLauncher(Starter, () => Facts, _ => FileIsThere),
            answer => answer());

        public string[] Output => [.. Shell.Output.Entries.Select(entry => entry.Text)];
    }

    // Where a level opened by itself keeps its scripts.
    private static string InContent(string contentPath) => ContentRoot.ResolveAbsolute(ContentRoot.Path, contentPath);

    [Fact]
    public void Insert_with_no_project_open_says_what_to_open_first()
    {
        var rig = new Rig();

        rig.Commands.InsertNew();

        rig.Shell.Message.ShouldBe(
            "Open a project before adding a script. Its file is made in the project's Assets folder.");
        rig.Shell.IsError.ShouldBeFalse();
    }

    [Fact]
    public async Task A_stored_path_opens_the_file_it_names_under_the_content_folder()
    {
        var rig = new Rig();

        await rig.Commands.OpenAsync("Scripts/Spin.luau", line: 12);

        ProgramStart start = rig.Starter.Asked.ShouldHaveSingleItem();
        start.FileName.ShouldBe(Code);
        start.Arguments.ShouldBe(["--goto", InContent("Scripts/Spin.luau") + ":12"]);
        rig.Shell.Message.ShouldBe("Opened Spin.luau in Visual Studio Code.");
    }

    [Theory]
    [InlineData("../outside.luau")]
    [InlineData(@"C:\Windows\win.luau")]
    public async Task A_path_that_leaves_the_assets_folder_is_an_error_and_starts_nothing(string path)
    {
        var rig = new Rig();

        await rig.Commands.OpenAsync(path);

        rig.Starter.Asked.ShouldBeEmpty();
        rig.Shell.Message.ShouldBe($"'{path}' is not a path inside the Assets folder.");
        rig.Shell.IsError.ShouldBeTrue();
    }

    [Fact]
    public async Task A_level_that_names_a_batch_file_as_its_script_gets_it_shown_and_never_run()
    {
        // What the Open button and a double click in the tree come down to.
        var rig = new Rig();

        await rig.Commands.OpenAsync("Scripts/setup.bat");

        rig.Starter.Asked.ShouldHaveSingleItem().FileName.ShouldBe("explorer.exe");
        rig.Shell.Message.ShouldBe(
            "setup.bat is not a .luau file, so no program was started for it. setup.bat is shown in its folder.");
    }

    [Fact]
    public async Task A_file_that_is_not_there_is_an_error_in_the_status_bar()
    {
        var rig = new Rig { FileIsThere = false };

        await rig.Commands.OpenAsync("Scripts/Spin.luau");

        rig.Shell.Message.ShouldBe("Spin.luau is not on disk.");
        rig.Shell.IsError.ShouldBeTrue();
    }

    [Fact]
    public async Task After_an_insert_a_plain_open_goes_to_output_and_leaves_the_status_bar()
    {
        // The status bar says where the new file was made.
        var rig = new Rig();
        rig.Shell.SetMessage("Added a script to 'Coin' and made its file, Assets/Scripts/Coin.luau.");

        await rig.Commands.OpenAsync("Scripts/Coin.luau", keepStatus: true);

        rig.Shell.Message.ShouldBe("Added a script to 'Coin' and made its file, Assets/Scripts/Coin.luau.");
        rig.Output[^1].ShouldBe("Opened Coin.luau in Visual Studio Code.");
    }

    [Fact]
    public async Task After_an_insert_an_open_that_fell_back_still_reaches_the_status_bar()
    {
        var rig = new Rig { Facts = new ScriptEditorFacts(null, false, FileManagerKind.Explorer) };
        rig.Shell.SetMessage("Added a script to 'Coin' and made its file, Assets/Scripts/Coin.luau.");

        await rig.Commands.OpenAsync("Scripts/Coin.luau", keepStatus: true);

        rig.Shell.Message.ShouldBe("No program is set for .luau files. Coin.luau is shown in its folder.");
    }

    [Fact]
    public async Task A_program_that_did_not_start_is_a_line_in_output_before_the_status()
    {
        var rig = new Rig();
        rig.Starter.Break(Code, "Access is denied.");

        await rig.Commands.OpenFileAsync(@"D:\Games\Tower\Assets\Scripts\Spin.luau");

        rig.Output.ShouldBe(
        [
            $"Visual Studio Code did not start ({Code}): Access is denied.",
            "Visual Studio Code did not start. Opened Spin.luau in the program your system has for .luau files.",
        ]);
    }

    [Fact]
    public void A_file_dropped_on_a_node_with_no_level_open_is_refused_with_a_sentence()
    {
        var rig = new Rig();
        var script = new ContentDragPayload(ContentKind.Script, "Scripts/Spin.luau", "Spin.luau");

        rig.Commands.AttachDropped(script, Guid.NewGuid());

        rig.Shell.Message.ShouldBe("Open a project before dropping anything into the scene.");
    }

    [Fact]
    public void A_script_picked_in_content_with_no_level_open_is_refused_with_a_sentence()
    {
        var rig = new Rig();
        var entry = new ContentEntry
        {
            Name = "Spin.luau", FullPath = InContent("Scripts/Spin.luau"), Kind = ContentKind.Script, SizeLabel = "1 KB",
        };

        rig.Commands.AttachListed(entry);

        rig.Shell.Message.ShouldBe("Open a project before inserting or assigning anything.");
    }
}
