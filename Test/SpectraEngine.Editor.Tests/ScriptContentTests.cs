using Microsoft.Extensions.Logging.Abstractions;
using SpectraEngine.Core.Inspection;
using SpectraEngine.Core.Scene;
using SpectraEngine.Editor.Shell;
using System.Text.RegularExpressions;

namespace SpectraEngine.Editor.Tests;

/// <summary>
/// A script file as a kind of content: what the browser calls it, where the
/// pickers list it, and what dropping it into the level would do.
/// </summary>
public sealed class ScriptContentTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "spectra-script-content-" + Guid.NewGuid().ToString("N"));

    public ScriptContentTests()
    {
        Write("Scripts/Door.luau", "print(\"open\")\nprint(\"close\")\n");
        Write("Scripts/Spin.luau", "print(\"spin\")\n");
        Write("Scripts/shared/Timer.luau", string.Empty);
        Write("Scripts/notes.txt", "not a script\n");
        Write("Scripts/old.lua", "print(\"lua, not luau\")\n");
        Write("Materials/door.spectramat", "shader lit\n");
        Write("Models/door.obj", "o door\n");
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // A locked temp folder is not a test failure.
        }
    }

    private void Write(string relative, string text)
    {
        string full = Path.Combine(_root, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(full).ShouldNotBeNull());
        File.WriteAllText(full, text);
    }

    private async Task<ContentBrowserModel> BrowserAsync()
    {
        var browser = new ContentBrowserModel(NullLogger.Instance, watchFiles: false);
        browser.SetRoot(_root);
        await browser.Index.Walking;

        // Re-list after the walk. In the app the index's Changed event does
        // this, but no dispatcher delivers it here.
        browser.NavigateTo(_root);
        return browser;
    }

    private AssetCatalog Catalog()
    {
        var catalog = new AssetCatalog(NullLogger.Instance);
        catalog.Rebuild(_root);
        return catalog;
    }

    private static ContentDragPayload Script() => new(ContentKind.Script, "Scripts/Door.luau", "Door.luau");

    [Theory]
    [InlineData("Scripts/Door.luau")]
    [InlineData("Scripts/DOOR.LUAU")]
    [InlineData(@"C:\project\Assets\Scripts\spin.luau")]
    public void A_luau_file_is_a_script(string path)
    {
        ContentClassifier.Classify(path).ShouldBe(ContentKind.Script);
    }

    [Theory]
    [InlineData("Scripts/old.lua")]
    [InlineData("Scripts/notes.txt")]
    [InlineData("Scripts/Door.luau.bak")]
    [InlineData("Scripts/luau")]
    public void What_only_looks_like_a_script_is_not_one(string path)
    {
        ContentClassifier.Classify(path).ShouldBe(ContentKind.Other);
    }

    [Fact]
    public void The_word_for_a_script_is_script()
    {
        ContentClassifier.Label(ContentKind.Script).ShouldBe("Script");
    }

    [Fact]
    public void A_script_file_wears_the_icon_its_node_wears_in_the_scene_tree()
    {
        EditorIcons.ForFile(ContentKind.Script)
            .ShouldBe(EditorIcons.ForNode(SceneNodeKind.Script, null, null, null));
    }

    [Fact]
    public void A_script_glyph_set_in_markup_gets_the_tint_the_registry_gives()
    {
        // The ribbon and the Object menu take the drawing from the registry
        // and the tint from this class, so a disabled button can grey it.
        string styles = File.ReadAllText(
            Path.Combine(IconFiles.SourceRoot(), "SpectraEngine.Editor", "Theme", "Controls.axaml"));

        Match rule = Regex.Match(
            styles,
            @"<Style Selector=""Path\.icon\.kind-script"">\s*" +
            @"<Setter Property=""Stroke"" Value=""\{StaticResource (?<tint>\w+)\}""");

        rule.Success.ShouldBeTrue("the theme should have a kind-script class");
        rule.Groups["tint"].Value.ShouldBe(EditorIcons.ForFile(ContentKind.Script).Tint);
        styles.ShouldContain("Button:disabled Path.icon.kind-script");
    }

    [Fact]
    public async Task A_script_row_has_a_word_of_its_own_and_the_path_a_level_stores()
    {
        ContentBrowserModel browser = await BrowserAsync();
        browser.Query = "Door.luau";

        ContentEntry entry = browser.Entries.ShouldHaveSingleItem();

        entry.Kind.ShouldBe(ContentKind.Script);
        entry.KindLabel.ShouldBe("script");
        entry.IsScript.ShouldBeTrue();
        entry.IsSound.ShouldBeFalse();
        entry.ContentPath.ShouldBe("Scripts/Door.luau");
    }

    [Fact]
    public async Task The_scripts_chip_lists_scripts_and_nothing_else()
    {
        ContentBrowserModel browser = await BrowserAsync();
        browser.Query = "o";

        browser.Filter = ContentFilter.Scripts;

        browser.IsFilterScripts.ShouldBeTrue();
        browser.IsFilterAll.ShouldBeFalse();
        browser.Entries.Select(entry => entry.Name).ShouldBe(["Door.luau"]);
    }

    [Fact]
    public async Task The_scripts_chip_keeps_the_folders_of_a_folder_view()
    {
        ContentBrowserModel browser = await BrowserAsync();
        browser.NavigateTo(Path.Combine(_root, "Scripts"));

        browser.Filter = ContentFilter.Scripts;

        // The text file and the .lua are gone, the subfolder is not.
        browser.Entries.Select(entry => entry.Name)
            .ShouldBe(["shared", "Door.luau", "Spin.luau"]);
    }

    [Fact]
    public void Every_filter_has_a_word_and_the_word_names_the_filter()
    {
        ContentFilterNames.All.Select(row => row.Filter).ShouldBe(Enum.GetValues<ContentFilter>());
        ContentFilterNames.All.Select(row => row.Word).ShouldBe(
            ["All", "Textures", "Materials", "Models", "Sounds", "Scripts"]);

        foreach ((ContentFilter filter, string word) in ContentFilterNames.All)
        {
            ContentFilterNames.WordFor(filter).ShouldBe(word);
            ContentFilterNames.Parse(word).ShouldBe(filter);
        }

        ContentFilterNames.Parse("Shaders").ShouldBe(ContentFilter.All);
        ContentFilterNames.Parse(null).ShouldBe(ContentFilter.All);
    }

    [Fact]
    public void Every_chip_in_the_content_panel_carries_a_filters_word()
    {
        // A chip's Tag is how its click finds the filter. A word that names
        // none would list everything and light no chip.
        string markup = File.ReadAllText(Path.Combine(
            IconFiles.SourceRoot(), "SpectraEngine.Editor", "Shell", "ContentPanel.axaml"));

        Regex.Matches(markup, @"Tag=""(\w+)"" Click=""OnFilterClicked""")
            .Select(match => match.Groups[1].Value)
            .ShouldBe(ContentFilterNames.All.Select(row => row.Word));
    }

    [Fact]
    public async Task The_narrow_panels_one_chip_names_the_kinds_being_listed()
    {
        ContentBrowserModel browser = await BrowserAsync();
        var raised = new List<string?>();
        browser.PropertyChanged += (_, change) => raised.Add(change.PropertyName);
        browser.FilterLabel.ShouldBe("All");

        browser.Filter = ContentFilter.Scripts;

        browser.FilterLabel.ShouldBe("Scripts");
        raised.ShouldContain(nameof(ContentBrowserModel.FilterLabel));
    }

    [Fact]
    public async Task Another_chip_hides_the_scripts()
    {
        ContentBrowserModel browser = await BrowserAsync();
        browser.Query = "door";

        browser.Filter = ContentFilter.Models;

        browser.IsFilterScripts.ShouldBeFalse();
        browser.Entries.Select(entry => entry.Name).ShouldBe(["door.obj"]);
    }

    [Fact]
    public async Task Selecting_a_script_fills_the_details_strip_with_its_length()
    {
        ContentBrowserModel browser = await BrowserAsync();
        browser.Query = "Door.luau";
        var landed = new TaskCompletionSource();
        browser.PropertyChanged += (_, change) =>
        {
            if (change.PropertyName == nameof(ContentBrowserModel.SelectedExtra) && browser.SelectedExtra.Length > 0)
                landed.TrySetResult();
        };

        browser.Select(browser.Entries.ShouldHaveSingleItem());
        await landed.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        browser.SelectedDetails.ShouldStartWith("script  Scripts/Door.luau");
        browser.SelectedExtra.ShouldBe("2 lines");
    }

    [Theory]
    [InlineData(0, "empty")]
    [InlineData(1, "1 line")]
    [InlineData(240, "240 lines")]
    public void A_scripts_length_is_said_in_lines(int lines, string expected)
    {
        ScriptDetails.Describe(lines).ShouldBe(expected);
    }

    [Fact]
    public void A_script_that_is_gone_when_it_is_read_says_nothing_and_does_not_throw()
    {
        ScriptDetails.Describe(Path.Combine(_root, "Scripts", "nobody_wrote_this.luau")).ShouldBeEmpty();
    }

    [Fact]
    public void A_script_row_asks_the_catalogue_for_scripts()
    {
        AssetCatalog.KindFor(AssetKind.Script).ShouldBe(ContentKind.Script);
    }

    [Fact]
    public void The_catalogue_lists_the_projects_scripts_for_a_script_row_and_nothing_else()
    {
        List<AssetCatalogEntry> found = Catalog().Search("", AssetCatalog.KindFor(AssetKind.Script), 50);

        found.Select(entry => entry.ContentPath).ShouldBe(
            ["Scripts/Door.luau", "Scripts/Spin.luau", "Scripts/shared/Timer.luau"]);
        found.ShouldAllBe(entry => entry.Kind == ContentKind.Script);
    }

    [Fact]
    public void A_script_is_found_by_its_name_and_carries_the_path_a_level_stores()
    {
        AssetCatalogEntry timer = Catalog().Search("timer", ContentKind.Script, 5).ShouldHaveSingleItem();

        timer.ContentPath.ShouldBe("Scripts/shared/Timer.luau");
        timer.Stem.ShouldBe("Timer");
        timer.Folder.ShouldBe("Scripts/shared");
    }

    [Fact]
    public void A_search_for_another_kind_finds_no_script()
    {
        AssetCatalog catalog = Catalog();

        foreach (ContentKind kind in new[] { ContentKind.Material, ContentKind.Model, ContentKind.Sound })
            catalog.Search("door", kind, 50).ShouldAllBe(entry => entry.Kind == kind);
    }

    [Fact]
    public void A_script_can_be_dropped_into_the_viewport()
    {
        AssetDropPolicy.CanPlace(ContentKind.Script).ShouldBeTrue();
        AssetDropPolicy.Refuse(Script(), hasSession: true, viewportAcceptsDrops: true).ShouldBeNull();
    }

    [Fact]
    public void A_script_is_refused_in_the_words_a_model_is_when_nothing_can_take_it()
    {
        var model = new ContentDragPayload(ContentKind.Model, "Models/door.obj", "door.obj");

        AssetDropPolicy.Refuse(Script(), hasSession: false, viewportAcceptsDrops: true)
            .ShouldBe(AssetDropPolicy.Refuse(model, hasSession: false, viewportAcceptsDrops: true));
        AssetDropPolicy.Refuse(Script(), hasSession: true, viewportAcceptsDrops: false)
            .ShouldBe(AssetDropPolicy.Refuse(model, hasSession: true, viewportAcceptsDrops: false));
    }

    [Fact]
    public void A_file_that_cannot_be_dropped_is_told_that_scripts_can()
    {
        var shader = new ContentDragPayload(ContentKind.Shader, "Shaders/lit.spectrashade", "lit.spectrashade");

        AssetDropPolicy.Refuse(shader, hasSession: true, viewportAcceptsDrops: true).ShouldBe(
            "lit.spectrashade cannot be dropped into the scene; only models, materials, sounds and scripts can.");
    }

    [Fact]
    public void A_script_can_be_dropped_on_a_node_in_the_scene_tree()
    {
        AssetDropPolicy.CanDropOnNode(ContentKind.Script).ShouldBeTrue();
        AssetDropPolicy.RefuseOnNode(Script(), hasSession: true, isPlaying: false).ShouldBeNull();
    }

    [Theory]
    [InlineData(ContentKind.Model, "door.obj cannot be dropped on a node; only a script can. Drop it into the viewport instead.")]
    [InlineData(ContentKind.Sound, "door.obj cannot be dropped on a node; only a script can. Drop it into the viewport instead.")]
    [InlineData(ContentKind.Texture, "door.obj cannot be dropped on a node; only a script can.")]
    [InlineData(ContentKind.Other, "door.obj cannot be dropped on a node; only a script can.")]
    public void Any_other_file_is_sent_from_the_tree_to_where_it_can_go(ContentKind kind, string expected)
    {
        var payload = new ContentDragPayload(kind, "Models/door.obj", "door.obj");

        AssetDropPolicy.CanDropOnNode(kind).ShouldBeFalse();
        AssetDropPolicy.RefuseOnNode(payload, hasSession: true, isPlaying: false).ShouldBe(expected);
    }

    [Fact]
    public void A_script_dropped_on_a_node_while_the_level_plays_is_refused_with_the_way_out()
    {
        AssetDropPolicy.RefuseOnNode(Script(), hasSession: true, isPlaying: true)
            .ShouldBe("Door.luau was not added: the level is playing. Stop it first.");
    }

    [Fact]
    public void A_script_dropped_on_a_node_with_no_project_open_is_refused()
    {
        AssetDropPolicy.RefuseOnNode(Script(), hasSession: false, isPlaying: false)
            .ShouldBe("Open a project before dropping anything into the scene.");
    }

    [Fact]
    public void Over_the_viewport_the_chip_says_where_the_script_will_go()
    {
        // A script has no place. Over a node it goes under it, over nothing
        // it is the level's.
        ViewportDropPrompt prompt = ViewportDropPrompt.For(Script(), hasSession: true, viewportAcceptsDrops: true);

        prompt.IsVisible.ShouldBeTrue();
        prompt.Accepts.ShouldBeTrue();
        prompt.Headline.ShouldBe("Drop to add");
        prompt.Subject.ShouldBe("Scripts/Door.luau");
        prompt.Reason.ShouldBeEmpty();
        prompt.Hint.ShouldBe("as a script under what it lands on, or a level script over nothing");
        prompt.IconKey.ShouldBe(EditorIcons.ForFile(ContentKind.Script).Icon);
    }

    [Fact]
    public void Where_no_drop_gets_a_chip_a_script_gets_none_either()
    {
        ViewportDropPrompt.For(Script(), hasSession: false, viewportAcceptsDrops: true)
            .ShouldBe(ViewportDropPrompt.None);
        ViewportDropPrompt.For(Script(), hasSession: true, viewportAcceptsDrops: false)
            .ShouldBe(ViewportDropPrompt.None);
    }
}
