using SpectraEngine.Editor.Shell;
using SpectraEngine.Editor.Shell.Ribbon;

namespace SpectraEngine.Editor.Tests;

/// <summary>
/// The ways Insert Script is reached, and that each of them ends in the one
/// handler: the ribbon, the palette, the Object menu and the two right-click
/// menus.
/// </summary>
// Read from the roster and from the sources. No test can click the window.
public sealed class ScriptInsertRouteTests
{
    private static string Source(params string[] parts) =>
        File.ReadAllText(Path.Combine([IconFiles.SourceRoot(), "SpectraEngine.Editor", .. parts]));

    // The opening tag of the first element that carries the text, as text.
    private static string ElementWith(string markup, string text)
    {
        int at = markup.IndexOf(text, StringComparison.Ordinal);
        at.ShouldBeGreaterThanOrEqualTo(0, $"nothing carries {text}");

        return markup[markup.LastIndexOf('<', at)..markup.IndexOf('>', at)];
    }

    [Fact]
    public void The_ribbons_insert_group_has_a_script_button()
    {
        RibbonGroup insert = RibbonLayout.FindTab(RibbonLayout.DefaultTabId).ShouldNotBeNull().Groups[0];

        RibbonItem script = insert.Items.Single(item => item.Id == "insert.script");

        insert.Caption.ShouldBe("Insert");
        script.Label.ShouldBe("Script");
        script.Verb.ShouldBe(ShellVerb.InsertScript());
        script.Kind.ShouldBe(RibbonControlKind.Button);
    }

    [Fact]
    public void The_script_button_shares_a_column_with_the_other_small_inserts()
    {
        // Three small rows fill one column, so the page is no wider for it.
        RibbonGroup insert = RibbonLayout.FindTab(RibbonLayout.DefaultTabId).ShouldNotBeNull().Groups[0];

        insert.Items.Where(item => item.Size == RibbonItemSize.Small).Select(item => item.Id)
            .ShouldBe(["insert.panel", "insert.group", "insert.script"]);
    }

    [Fact]
    public void The_button_wears_the_script_icon_and_posts_through_the_roster()
    {
        string markup = Source("Shell", "Ribbon", "RibbonBuildTab.axaml");

        ElementWith(markup, "Tag=\"insert.script\"").ShouldContain("Click=\"OnRibbonItemClick\"");
        ElementWith(markup, "Tag=\"insert.script\"").ShouldContain("IsEnabled=\"{Binding !IsPlaying}\"");

        string button = markup[markup.IndexOf("Tag=\"insert.script\"", StringComparison.Ordinal)..];
        button = button[..button.IndexOf("</Button>", StringComparison.Ordinal)];
        button.ShouldContain("Classes=\"icon kind-script\"");
        button.ShouldContain("{x:Static shell:EditorIcons.ScriptGlyph}");
    }

    [Fact]
    public void The_palette_offers_it_once_a_project_is_open_and_not_while_the_level_plays()
    {
        ShellCommand row = CommandTable.Commands.Single(command => command.Verb == ShellVerb.InsertScript());
        row.Title.ShouldBe("Insert script");

        var editing = new CommandContext(
            HasSelection: false, IsPlaying: false, HasSession: true, HasProject: true,
            RibbonExpanded: true, CanPlay: true);

        CommandTable.Available(row, editing).ShouldBeTrue("nothing selected makes a level script");
        CommandTable.Available(row, editing with { IsPlaying = true }).ShouldBeFalse();
        CommandTable.Available(row, editing with { HasProject = false }).ShouldBeFalse(
            "the file is made in a project's Assets folder");
    }

    [Fact]
    public void The_object_menu_has_it_among_the_inserts()
    {
        string item = ElementWith(Source("MainWindow.axaml"), "Header=\"Insert script\"");

        item.ShouldContain("Click=\"OnInsertScriptClicked\"");
        item.ShouldContain("IsEnabled=\"{Binding !IsPlaying}\"");
    }

    [Fact]
    public void The_scene_trees_menu_has_it_on_every_node()
    {
        string menu = Source("Shell", "ScenePanel.axaml");

        ElementWith(menu, "Header=\"Insert script\"").ShouldContain("Click=\"OnMenuInsertScript\"");
        ElementWith(menu, "Header=\"Insert script\"").ShouldNotContain("IsVisible");
    }

    [Fact]
    public void The_scene_trees_menu_opens_the_script_of_a_node_that_carries_one()
    {
        string item = ElementWith(Source("Shell", "ScenePanel.axaml"), "Header=\"Open script\"");

        item.ShouldContain("Click=\"OnMenuOpenScript\"");
        item.ShouldContain("IsVisible=\"{Binding HasScript}\"");
    }

    [Fact]
    public void Every_way_in_ends_in_the_one_command()
    {
        string window = Source("MainWindow.axaml.cs");

        // The ribbon and the palette post the verb, the tree raises its event,
        // and the two menus name the handler.
        window.ShouldContain("case ShellVerbKind.InsertScript:");
        window.Split("_scripts.InsertNew()").Length.ShouldBe(3, "the verb and the Object menu's handler");
        window.ShouldContain("_sceneView.InsertScriptRequested += _scripts.InsertNew;");
        window.ShouldContain("Item(\"Insert script\", null, _scripts.InsertNew)");
        window.ShouldContain("void OnInsertScriptClicked(object? sender, RoutedEventArgs e) => _scripts.InsertNew();");
    }

    [Fact]
    public void Every_way_a_script_is_opened_goes_through_the_one_launcher()
    {
        // The Open button, a double click in the Content panel, the tree's
        // menu, and Insert Script. A second Process.Start would be a second
        // answer to which program opens a script.
        string commands = Source("ScriptCommands.cs");
        string window = Source("MainWindow.axaml.cs");

        commands.Split("_launcher.OpenAsync(").Length.ShouldBe(2, "one call to the launcher");
        commands.ShouldContain("_ = OpenAsync(report.ContentPath, keepStatus: true);");
        commands.ShouldNotContain("Process.Start");

        window.Split("ScriptLauncher.").Length.ShouldBe(2, "the window makes one launcher and hands it over");
        window.ShouldContain("_propertiesView.ScriptOpenRequested += path => _ = _scripts.OpenAsync(path);");
        window.ShouldContain("_sceneView.OpenScriptRequested += _scripts.OpenOf;");
        window.ShouldContain("_ = _scripts.OpenFileAsync(entry.FullPath);");
    }
}
