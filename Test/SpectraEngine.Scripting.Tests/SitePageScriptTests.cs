namespace SpectraEngine.Scripting.Tests;

/// <summary>
/// Every page of the docs site a person wrote, held to the sample scripts:
/// a block of Luau on a page is the whole text of a sample, so luau-lsp has
/// checked it under both solvers and the engine's own Luau compiles it.
/// </summary>
public sealed class SitePageScriptTests
{
    // The pages that show Luau, each with the test class that plays its scripts.
    private static readonly Dictionary<string, Type> PlayedBy = new(StringComparer.Ordinal)
    {
        ["guides/write-your-first-script.mdx"] = typeof(FirstScriptGuideTests),
        ["guides/move-a-part-with-a-script.mdx"] = typeof(MovePartGuideTests),
        ["guides/talk-to-the-level.mdx"] = typeof(TalkToLevelGuideTests),
        ["guides/kill-brick-and-checkpoints.mdx"] = typeof(PlayerGuideTests),
        ["guides/make-things-while-the-level-plays.mdx"] = typeof(MakeThingsGuideTests),
        ["guides/change-how-the-player-moves.mdx"] = typeof(MoverGuideTests),
    };

    [Fact]
    public void Every_block_of_Luau_on_every_page_is_the_text_of_a_sample()
    {
        HashSet<string> samples = [.. ScriptSamples.All.Select(sample => sample.Code)];

        List<string> loose = [];
        foreach (SitePage page in SitePage.All)
        {
            string[] shown = page.Blocks("luau");
            for (int i = 0; i < shown.Length; i++)
            {
                if (!samples.Contains(shown[i]))
                    loose.Add($"{page.Path}, block {i + 1}, which starts \"{shown[i].Split('\n')[0]}\"");
            }
        }

        loose.ShouldBeEmpty(
            "A script on a page is no sample under Test/SpectraEngine.Scripting.Tests/Samples, so nothing checks " +
            "it. Make the block the whole text of a sample file, without the arrows of a mistake.");
    }

    // A fence that says lua is drawn the same and would pass the test above unread.
    [Fact]
    public void No_page_shows_a_script_in_a_fence_that_is_not_marked_luau()
    {
        string[] dodging = [.. SitePage.All.Where(page => page.Blocks("lua").Length > 0).Select(page => page.Path)];

        dodging.ShouldBeEmpty("Mark a block of script as luau, so it is held to a sample.");
    }

    [Fact]
    public void Every_page_that_shows_a_script_has_a_test_class_that_plays_it()
    {
        string[] showing = [.. SitePage.All.Where(page => page.Blocks("luau").Length > 0).Select(page => page.Path)];

        string[] played = [.. PlayedBy.Keys.Order(StringComparer.Ordinal)];
        showing.ShouldBe(
            played, "A page that shows a script gets a test class that plays the script in a level, and a line here.");
    }

    // A guide that drops a step leaves its sample behind, checked for nobody.
    [Fact]
    public void A_sample_no_page_shows_is_not_kept_in_a_guides_folder()
    {
        HashSet<string> shown = [.. SitePage.All.SelectMany(page => page.Blocks("luau"))];

        string[] unused =
        [
            .. ScriptSamples.All
                .Where(sample => sample.ContentPath.StartsWith("Guides/", StringComparison.Ordinal))
                .Where(sample => !shown.Contains(sample.Code))
                .Select(sample => sample.ContentPath),
        ];

        unused.ShouldBeEmpty("A sample under Samples/Assets/Guides is there because a page shows it.");
    }
}
