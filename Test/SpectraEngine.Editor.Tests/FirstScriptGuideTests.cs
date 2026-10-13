using SpectraEngine.Editing.Hosting;
using SpectraEngine.Editor.Shell.Ribbon;

namespace SpectraEngine.Editor.Tests;

/// <summary>
/// The site's page on a first script, held to the editor: what it tells a
/// person to press is there, and what it says the editor answers is what
/// the editor answers.
/// </summary>
// The page's scripts are held in SpectraEngine.Scripting.Tests, which can run them.
public sealed class FirstScriptGuideTests
{
    private static readonly string Page = File.ReadAllText(Path.Combine(
            IconFiles.SourceRoot(), "site", "src", "content", "docs", "guides", "write-your-first-script.mdx"))
        .Replace("\r\n", "\n");

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
}
