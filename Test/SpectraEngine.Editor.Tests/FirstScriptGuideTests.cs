using System.Text.RegularExpressions;
using SpectraEngine.Editing.Hosting;
using SpectraEngine.Editor.Shell;
using SpectraEngine.Editor.Shell.Ribbon;

namespace SpectraEngine.Editor.Tests;

/// <summary>
/// The site's page on a first script, held to the editor: what it tells a
/// person to press is there, and what it says the editor answers is what
/// the editor answers.
/// </summary>
// The page's scripts are held in SpectraEngine.Scripting.Tests, which can run them.
public sealed partial class FirstScriptGuideTests
{
    private static readonly string Page = File.ReadAllText(Path.Combine(
            IconFiles.SourceRoot(), "site", "src", "content", "docs", "guides", "write-your-first-script.mdx"))
        .Replace("\r\n", "\n");

    // What the page tells a person to do with a level that plays or does
    // not. Only a stop names Esc.
    [GeneratedRegex(@"(?<play>[Pp]ress Play)|(?<stop><kbd>Esc</kbd>)|(?<click>Click the line)")]
    private static partial Regex Step();

    [Fact]
    public void The_status_line_on_the_page_is_what_insert_script_says_for_a_part()
    {
        // A new part is called Part, and its script's file is named after it.
        var report = new ScriptInsertReport("Scripts/Part.luau", Guid.NewGuid(), "Part", null) { MadeFile = true };

        Page.ShouldContain(report.Describe());
    }

    [Fact]
    public void The_buttons_the_page_names_are_on_the_ribbon_where_it_says()
    {
        RibbonTab build = RibbonLayout.Tabs.Single(tab => tab.Title == "Build");
        RibbonGroup insert = build.Groups.Single(group => group.Caption == "Insert");

        insert.Items.Select(item => item.Label).ShouldContain("Part");
        insert.Items.Select(item => item.Label).ShouldContain("Script");
        Page.ShouldContain("Press Part on the Build tab of the ribbon");
        Page.ShouldContain("press Script in the Insert group of the Build tab");
    }

    // Play is one button and one key. Pressed while a level plays it stops
    // the level, and a level that plays has the mouse for looking around.
    [Fact]
    public void The_page_stops_the_level_before_it_plays_again_and_before_a_click_in_output()
    {
        var shell = new ShellModel();
        shell.RequestPlaying(true);
        shell.PlayLabel.ShouldBe("Stop");
        GestureOf("Play").ShouldBe(GestureOf("Stop"));

        bool isPlaying = false;
        foreach (Match step in Step().Matches(Page))
        {
            string where = $"line {Page[..step.Index].Count(c => c == '\n') + 1} of the page, \"{step.Value}\"";

            if (step.Groups["play"].Success)
            {
                isPlaying.ShouldBeFalse($"the level plays already, so Play would stop it: {where}");
                isPlaying = true;
            }
            else if (step.Groups["stop"].Success)
            {
                isPlaying.ShouldBeTrue($"no level plays, so there is nothing to stop: {where}");
                isPlaying = false;
            }
            else
            {
                isPlaying.ShouldBeFalse($"the level plays and has the mouse, so nothing can be clicked: {where}");
            }
        }

        Page.ShouldContain("The Play button reads Stop while the level plays");
    }

    private static string GestureOf(string command) =>
        CommandTable.Commands.Single(row => row.Title == command).Gesture;
}
