using System.Text.RegularExpressions;
using SpectraEngine.Core.Inspection;
using SpectraEngine.Core.Scene;
using SpectraEngine.Editing.Commands;
using SpectraEngine.Editing.Hosting;
using SpectraEngine.Editor.Shell;
using SpectraEngine.Editor.Shell.Ribbon;
using SpectraEngine.Entities;

namespace SpectraEngine.Editor.Tests;

/// <summary>
/// The site's guides for scripts, held to the editor: a page stops the level
/// before it plays it again, and what it tells a person to press is there.
/// </summary>
// The pages' scripts are held in SpectraEngine.Scripting.Tests, which can run them.
public sealed partial class ScriptGuideStepsTests
{
    private const string Guides = "guides/";

    // What a page tells a person to do with a level that plays or does not.
    // A stop names both keys, or is "Stop." or "Stop," as the page telling
    // the reader to stop. "Stop puts back" is the page telling what stopping
    // does, and Esc alone clears the selection of a level that does not play.
    [GeneratedRegex(@"(?<play>[Pp]ress Play)|(?<stop><kbd>F8</kbd> or <kbd>Esc</kbd>|\bStop[.,])|(?<click>Click the line)|(?<deselect>[Pp]ress <kbd>Esc</kbd>)")]
    private static partial Regex Step();

    public static TheoryData<string> Pages() =>
    [
        "move-a-part-with-a-script.mdx", "talk-to-the-level.mdx", "hear-what-the-level-does.mdx",
        "kill-brick-and-checkpoints.mdx", "make-things-while-the-level-plays.mdx", "change-how-the-player-moves.mdx",
    ];

    private static string Page(string name) =>
        File.ReadAllText(Path.Combine(IconFiles.SourceRoot(), "site", "src", "content", "docs", Guides + name))
            .Replace("\r\n", "\n");

    // Play is one button and one key. Pressed while a level plays it stops
    // the level, and a level that plays has the mouse for looking around.
    // Esc stops a level that plays, so it clears a selection only in one that does not.
    [Theory]
    [MemberData(nameof(Pages))]
    public void The_page_stops_the_level_before_it_plays_again_and_before_a_click_in_output(string name)
    {
        string page = Page(name);

        bool isPlaying = false;
        int plays = 0;
        foreach (Match step in Step().Matches(page))
        {
            string where = $"line {page[..step.Index].Count(c => c == '\n') + 1} of {name}, \"{step.Value}\"";

            if (step.Groups["play"].Success)
            {
                isPlaying.ShouldBeFalse($"the level plays already, so Play would stop it: {where}");
                isPlaying = true;
                plays++;
            }
            else if (step.Groups["stop"].Success)
            {
                isPlaying.ShouldBeTrue($"no level plays, so there is nothing to stop: {where}");
                isPlaying = false;
            }
            else if (step.Groups["deselect"].Success)
            {
                isPlaying.ShouldBeFalse($"the level plays, so Esc would stop it and clear nothing: {where}");
            }
            else
            {
                isPlaying.ShouldBeFalse($"the level plays and has the mouse, so nothing can be clicked: {where}");
            }
        }

        plays.ShouldBeGreaterThan(0, $"{name} never tells the reader to play, or says it in other words than this test reads");
    }

    [Fact]
    public void The_buttons_the_pages_name_are_on_the_ribbon_where_they_say()
    {
        RibbonTab build = RibbonLayout.Tabs.Single(tab => tab.Title == "Build");
        string[] insert = [.. build.Groups.Single(group => group.Caption == "Insert").Items.Select(item => item.Label)];
        string[] arrange = [.. build.Groups.Single(group => group.Caption == "Arrange").Items.Select(item => item.Label)];

        insert.ShouldContain("Script");
        insert.ShouldContain("Light");
        insert.ShouldContain("Group");
        insert.ShouldContain("Entity");
        arrange.ShouldContain("Convert");

        Page("talk-to-the-level.mdx").ShouldContain("press Script on the Build tab");
        Page("talk-to-the-level.mdx").ShouldContain("Click the arrow beside Entity");
        Page("hear-what-the-level-does.mdx").ShouldContain("Press Light on the Build tab");
        Page("hear-what-the-level-does.mdx").ShouldContain("Click the arrow beside Entity");
        Page("make-things-while-the-level-plays.mdx").ShouldContain("Insert a group with Group on the Build tab");
        Page("move-a-part-with-a-script.mdx").ShouldContain("press Convert in the Arrange group of the Build tab");
    }

    [Fact]
    public void The_keys_the_pages_name_are_the_commands_keys()
    {
        GestureOf("Convert block or part").ShouldBe("Ctrl+T");
        Page("move-a-part-with-a-script.mdx").ShouldContain("<kbd>Ctrl</kbd> + <kbd>T</kbd>");

        CommandTable.Commands.ShouldContain(row => row.Gesture == "Ctrl+L");
        Page("talk-to-the-level.mdx").ShouldContain("<kbd>Ctrl</kbd> + <kbd>L</kbd>");

        GestureOf("Play").ShouldBe("F8");
        foreach (string name in Pages())
            Page(name).ShouldNotContain("<kbd>F5</kbd>");
    }

    // A page that says "with nothing selected" comes from steps that left
    // something selected, so it says how to clear the selection.
    [Fact]
    public void The_pages_clear_the_selection_with_the_key_that_does_it_before_a_level_script()
    {
        GestureOf("Deselect all").ShouldBe("Esc");

        foreach (string name in new[]
        {
            "hear-what-the-level-does.mdx", "kill-brick-and-checkpoints.mdx", "make-things-while-the-level-plays.mdx",
        })
        {
            Page(name).ShouldContain("Press <kbd>Esc</kbd>, which clears the selection, and then press Script.");
        }
    }

    // ScriptInsertTests holds that the file has the level's name. Script.luau
    // is what a level gets whose name has no letter or digit in it.
    [Fact]
    public void A_level_scripts_file_on_the_pages_is_named_after_the_level()
    {
        var report = new ScriptInsertReport("Scripts/Lobby.luau", Guid.NewGuid(), "Root", null)
        {
            IsLevelScript = true,
            MadeFile = true,
        };
        string page = Page("kill-brick-and-checkpoints.mdx");

        page.ShouldContain("For a level called `Lobby` the status bar says:");
        page.ShouldContain(report.Describe());
        page.ShouldContain($"`{ScriptFileName.FirstFree("Lobby", name => name == "Lobby.luau")}`");

        ScriptFileName.StemFor("Lobby").ShouldBe("Lobby");
        ScriptFileName.StemFor("...").ShouldBe("Script");
        foreach (string name in Pages())
            Page(name).ShouldNotContain("Scripts/Script.luau");
    }

    // The door page has the reader rename the button, because its script
    // looks for a node called Button.
    [Fact]
    public void A_new_button_has_the_name_the_door_page_says_the_editor_gave_it()
    {
        SceneNode button = EntityEditor.BuildGeometryNode(new Scene("Lobby"), FuncButton.SpectraSchema, halfExtent: 1f);

        button.Name.ShouldBe("func_button1");
        Page("talk-to-the-level.mdx").ShouldContain("The editor called it `func_button1`");
        Page("talk-to-the-level.mdx").ShouldContain("type `Button` in the name box at the top of the panel");
    }

    [Fact]
    public void The_kinds_and_switches_the_pages_name_are_the_ones_the_Properties_panel_lists()
    {
        string player = Page("kill-brick-and-checkpoints.mdx");
        string make = Page("make-things-while-the-level-plays.mdx");

        AttributeKindWords.Labels.ShouldContain("Number");
        player.ShouldContain("pick Number in the list below it");

        NodeInspector.BehaviorGroup.ShouldBe("Behavior");
        player.ShouldContain("Under Behavior, switch Collides off");
        player.ShouldContain("Touch events off under Behavior");
        make.ShouldContain("Under Behavior in the Properties panel, switch Collides off");

        // Where the door page sends a reader for the word to write after "::".
        NodeInspector.EntityGroup.ShouldBe("Entity");
        Page("talk-to-the-level.mdx").ShouldContain("the Class row under Entity in the Properties panel");
    }

    private static string GestureOf(string command) =>
        CommandTable.Commands.Single(row => row.Title == command).Gesture;
}
