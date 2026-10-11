using SpectraEngine.Editor.Shell.Scripts;

namespace SpectraEngine.Editor.Tests;

/// <summary>
/// How a script is opened, decided from what a computer has. No program is
/// started and no disk is read: each test describes the computer.
/// </summary>
public sealed class ScriptOpenPlanTests
{
    private const string Code = @"C:\Users\Ada\AppData\Local\Programs\Microsoft VS Code\Code.exe";
    private const string Project = @"D:\Games\Tower";
    private const string File = @"D:\Games\Tower\Assets\Scripts\Spin.luau";

    private static readonly ScriptOpenRequest Request = new(File, Project);

    private static ScriptEditorFacts Has(
        string? code = null, bool defaultProgram = false, FileManagerKind manager = FileManagerKind.Explorer) =>
        new(code, defaultProgram, manager);

    [Fact]
    public void With_visual_studio_code_the_file_opens_there_on_the_projects_folder()
    {
        ScriptOpenStep first = ScriptOpenPlan.For(Request, Has(Code, defaultProgram: true))[0];

        first.Way.ShouldBe(ScriptOpenWay.VisualStudioCode);
        first.Start.FileName.ShouldBe(Code);

        // The folder first, so the language server sees every script in it.
        first.Start.Arguments.ShouldBe([Project, File]);
        first.Start.UseShell.ShouldBeFalse();
        first.Start.VerbatimArguments.ShouldBeNull();
    }

    [Fact]
    public void A_line_number_puts_the_caret_on_that_line()
    {
        ScriptOpenStep first = ScriptOpenPlan.For(Request with { Line = 42 }, Has(Code))[0];

        first.Start.Arguments.ShouldBe([Project, "--goto", File + ":42"]);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public void A_line_that_is_no_line_opens_the_file_at_its_top(int line)
    {
        ScriptOpenStep first = ScriptOpenPlan.For(Request with { Line = line }, Has(Code))[0];

        first.Start.Arguments.ShouldBe([Project, File]);
    }

    [Fact]
    public void A_script_of_no_project_opens_by_itself()
    {
        ScriptOpenStep first = ScriptOpenPlan.For(new ScriptOpenRequest(File, null), Has(Code))[0];

        first.Start.Arguments.ShouldBe([File]);
    }

    [Fact]
    public void Without_visual_studio_code_the_systems_program_for_the_file_opens_it()
    {
        IReadOnlyList<ScriptOpenStep> steps = ScriptOpenPlan.For(Request, Has(defaultProgram: true));

        steps[0].Way.ShouldBe(ScriptOpenWay.DefaultProgram);
        steps[0].Start.FileName.ShouldBe(File);
        steps[0].Start.Arguments.ShouldBeEmpty();
        steps[0].Start.UseShell.ShouldBeTrue("the system picks the program from the file's type");
    }

    [Fact]
    public void With_neither_the_file_is_shown_in_the_file_manager()
    {
        ScriptOpenStep only = ScriptOpenPlan.For(Request, Has()).ShouldHaveSingleItem();

        only.Way.ShouldBe(ScriptOpenWay.FileManager);
        only.Start.FileName.ShouldBe("explorer.exe");
        only.Start.VerbatimArguments.ShouldBe("/select,\"" + File + "\"");
    }

    [Fact]
    public void Each_way_is_followed_by_the_next_so_one_that_fails_to_start_has_somewhere_to_go()
    {
        IReadOnlyList<ScriptOpenStep> steps = ScriptOpenPlan.For(Request, Has(Code, defaultProgram: true));

        steps.Select(step => step.Way).ShouldBe(
            [ScriptOpenWay.VisualStudioCode, ScriptOpenWay.DefaultProgram, ScriptOpenWay.FileManager]);
    }

    [Fact]
    public void Finder_is_asked_to_select_the_file()
    {
        const string onMac = "/Users/ada/Tower/Assets/Scripts/Spin.luau";

        ScriptOpenStep only = ScriptOpenPlan
            .For(new ScriptOpenRequest(onMac, null), Has(manager: FileManagerKind.Finder))
            .ShouldHaveSingleItem();

        only.Start.FileName.ShouldBe("open");
        only.Start.Arguments.ShouldBe(["-R", onMac]);
    }

    [Fact]
    public void A_desktop_that_can_only_open_folders_is_shown_the_files_folder()
    {
        const string onLinux = "/home/ada/Tower/Assets/Scripts/Spin.luau";

        ScriptOpenStep only = ScriptOpenPlan
            .For(new ScriptOpenRequest(onLinux, null), Has(manager: FileManagerKind.FolderOpener))
            .ShouldHaveSingleItem();

        only.Start.FileName.ShouldBe("/home/ada/Tower/Assets/Scripts");
        only.Start.UseShell.ShouldBeTrue();
    }

    [Fact]
    public void A_computer_with_nothing_to_ask_gets_no_steps_and_no_exception()
    {
        ScriptOpenPlan.For(Request, Has(manager: FileManagerKind.None)).ShouldBeEmpty();
    }

    // The computers below are described by two questions: what an environment
    // variable holds, and whether a path is a file.

    private static Func<string, string?> Environment(params (string Name, string Value)[] variables) =>
        name => variables.FirstOrDefault(v => v.Name == name).Value;

    private static Func<string, bool> Files(params string[] paths) =>
        path => paths.Contains(path, StringComparer.OrdinalIgnoreCase);

    [Fact]
    public void Code_on_the_path_is_found_as_the_program_beside_its_batch_file()
    {
        // The path holds the bin folder. Its code.cmd is a batch file, and the
        // program one folder up takes the same arguments with no shell between.
        string? found = VisualStudioCodeLocator.Find(
            Environment(("PATH", @"C:\Windows;C:\Tools\VS Code\bin;C:\Other")),
            Files(@"C:\Tools\VS Code\bin\code.cmd", @"C:\Tools\VS Code\Code.exe"),
            isWindows: true);

        found.ShouldBe(@"C:\Tools\VS Code\Code.exe");
    }

    [Theory]
    [InlineData("LOCALAPPDATA", @"C:\Users\Ada\AppData\Local", @"C:\Users\Ada\AppData\Local\Programs\Microsoft VS Code\Code.exe")]
    [InlineData("ProgramFiles", @"C:\Program Files", @"C:\Program Files\Microsoft VS Code\Code.exe")]
    [InlineData("ProgramFiles(x86)", @"C:\Program Files (x86)", @"C:\Program Files (x86)\Microsoft VS Code\Code.exe")]
    public void Off_the_path_it_is_found_where_its_installer_puts_it(string variable, string value, string program)
    {
        string? found = VisualStudioCodeLocator.Find(
            Environment(("PATH", @"C:\Windows"), (variable, value)), Files(program), isWindows: true);

        found.ShouldBe(program);
    }

    [Fact]
    public void The_path_wins_over_an_install_folder()
    {
        string? found = VisualStudioCodeLocator.Find(
            Environment(("PATH", @"D:\Portable\bin"), ("ProgramFiles", @"C:\Program Files")),
            Files(
                @"D:\Portable\bin\code.cmd", @"D:\Portable\Code.exe",
                @"C:\Program Files\Microsoft VS Code\Code.exe"),
            isWindows: true);

        found.ShouldBe(@"D:\Portable\Code.exe");
    }

    [Fact]
    public void A_path_entry_in_quotes_or_with_a_trailing_slash_still_finds_it()
    {
        string? found = VisualStudioCodeLocator.Find(
            Environment(("PATH", "\"C:\\Tools\\VS Code\\bin\\\";C:\\Windows")),
            Files(@"C:\Tools\VS Code\bin\code.cmd", @"C:\Tools\VS Code\Code.exe"),
            isWindows: true);

        found.ShouldBe(@"C:\Tools\VS Code\Code.exe");
    }

    [Fact]
    public void A_computer_without_it_answers_null()
    {
        VisualStudioCodeLocator.Find(
            Environment(("PATH", @"C:\Windows;C:\Windows\System32"), ("ProgramFiles", @"C:\Program Files")),
            Files(@"C:\Windows\notepad.exe"),
            isWindows: true).ShouldBeNull();

        VisualStudioCodeLocator.Find(Environment(), Files(), isWindows: true).ShouldBeNull();
        VisualStudioCodeLocator.Find(Environment(), Files(), isWindows: false).ShouldBeNull();
    }

    [Fact]
    public void On_linux_code_on_the_path_is_the_program()
    {
        string? found = VisualStudioCodeLocator.Find(
            Environment(("PATH", "/usr/local/sbin:/home/ada/.local/bin:/usr/bin")),
            Files("/home/ada/.local/bin/code"),
            isWindows: false);

        found.ShouldBe("/home/ada/.local/bin/code");
    }

    [Theory]
    [InlineData("/snap/bin/code")]
    [InlineData("/Applications/Visual Studio Code.app/Contents/Resources/app/bin/code")]
    public void Off_the_path_on_linux_and_mac_it_is_found_in_its_usual_places(string place)
    {
        VisualStudioCodeLocator.Find(Environment(("PATH", "/bin")), Files(place), isWindows: false)
            .ShouldBe(place);
    }

    [Fact]
    public void Looking_at_this_computer_starts_nothing_and_does_not_throw()
    {
        ScriptEditorFacts facts = ScriptEditorSurvey.Take();

        facts.FileManager.ShouldNotBe(FileManagerKind.None);
        if (facts.VisualStudioCode is { } code)
            System.IO.File.Exists(code).ShouldBeTrue();
    }

    [Fact]
    public void Windows_has_no_program_for_an_extension_nobody_registered()
    {
        if (!OperatingSystem.IsWindows())
            return;

        // Without the right flag Windows answers with its own chooser window.
        FileAssociations.HasProgramFor(".spectra-nothing-opens-this").ShouldBeFalse();
        FileAssociations.HasProgramFor(".txt").ShouldBeTrue();
    }
}
