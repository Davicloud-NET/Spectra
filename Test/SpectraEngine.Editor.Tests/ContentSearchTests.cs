using Microsoft.Extensions.Logging.Abstractions;
using SpectraEngine.Editor.Shell;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace SpectraEngine.Editor.Tests;

/// <summary>
/// Searching a project, filtering it by kind, and navigating back up.
/// </summary>
public sealed class ContentSearchTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "spectra-search-" + Guid.NewGuid().ToString("N"));

    public ContentSearchTests()
    {
        Write("Materials/wall_brick.spectramat");
        Write("Materials/floor.spectramat");
        Write("Textures/brick.png");
        Write("Textures/dev/grid.png");
        Write("Models/crate.obj");
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private void Write(string relative)
    {
        string full = Path.Combine(_root, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, "x");
    }

    private async Task<ContentBrowserModel> BrowserAsync()
    {
        var browser = new ContentBrowserModel(NullLogger.Instance);
        browser.SetRoot(_root);
        await browser.Index.Walking;

        // Re-list after the walk. In the app the index's Changed event does
        // this, but no dispatcher delivers it here.
        browser.NavigateTo(_root);
        return browser;
    }

    private static List<string> NamesOf(ContentBrowserModel browser)
    {
        List<string> names = [];
        foreach (ContentEntry entry in browser.Entries) names.Add(entry.Name);
        return names;
    }

    [Fact]
    public async Task A_folder_view_lists_that_folder_only()
    {
        ContentBrowserModel browser = await BrowserAsync();

        List<string> names = NamesOf(browser);

        names.ShouldContain("Materials");
        names.ShouldContain("Textures");
        names.ShouldNotContain("brick.png");
    }

    [Fact]
    public async Task A_query_searches_the_whole_project_rather_than_the_folder()
    {
        ContentBrowserModel browser = await BrowserAsync();
        browser.Query = "brick";

        List<string> names = NamesOf(browser);

        names.ShouldContain("brick.png");
        names.ShouldContain("wall_brick.spectramat");
        browser.IsSearching.ShouldBeTrue();
    }

    [Fact]
    public async Task A_search_result_says_which_folder_it_is_in()
    {
        ContentBrowserModel browser = await BrowserAsync();
        browser.Query = "grid";

        browser.Entries.Count.ShouldBe(1);

        browser.Entries[0].FolderLabel.ShouldBe("Textures/dev");
    }

    [Fact]
    public async Task A_folder_view_carries_no_folder_labels()
    {
        ContentBrowserModel browser = await BrowserAsync();
        browser.NavigateTo(Path.Combine(_root, "Textures"));

        foreach (ContentEntry entry in browser.Entries)
            entry.FolderLabel.ShouldBe("");
    }

    [Fact]
    public async Task Clearing_the_query_puts_the_folder_back()
    {
        ContentBrowserModel browser = await BrowserAsync();
        browser.Query = "brick";
        browser.Query = string.Empty;

        browser.IsSearching.ShouldBeFalse();
        NamesOf(browser).ShouldContain("Materials");
    }

    [Fact]
    public async Task The_filter_narrows_a_search_by_kind()
    {
        ContentBrowserModel browser = await BrowserAsync();
        browser.Query = "brick";
        browser.Filter = ContentFilter.Materials;

        List<string> names = NamesOf(browser);

        names.ShouldContain("wall_brick.spectramat");
        names.ShouldNotContain("brick.png");
        browser.IsFilterMaterials.ShouldBeTrue();
    }

    [Fact]
    public async Task A_filtered_folder_view_keeps_its_folders()
    {
        ContentBrowserModel browser = await BrowserAsync();
        browser.Filter = ContentFilter.Textures;

        // Without folders a filtered view is a dead end.
        NamesOf(browser).ShouldContain("Textures");
    }

    [Fact]
    public async Task Breadcrumbs_run_from_the_root_to_here_and_navigate()
    {
        ContentBrowserModel browser = await BrowserAsync();
        browser.NavigateTo(Path.Combine(_root, "Textures", "dev"));

        browser.Breadcrumbs.Count.ShouldBe(3);
        browser.Breadcrumbs[0].Label.ShouldBe("Assets");
        browser.Breadcrumbs[1].Label.ShouldBe("Textures");
        browser.Breadcrumbs[2].Label.ShouldBe("dev");

        browser.NavigateTo(browser.Breadcrumbs[1].FullPath);
        NamesOf(browser).ShouldContain("brick.png");
    }

    [Fact]
    public async Task Reveal_navigates_selects_and_clears_the_query()
    {
        ContentBrowserModel browser = await BrowserAsync();
        browser.Query = "crate";

        ContentEntry? scrolled = null;
        browser.RevealScrolled += entry => scrolled = entry;

        browser.Reveal("Textures/dev/grid.png");

        browser.IsSearching.ShouldBeFalse();
        browser.Selected.ShouldNotBeNull();
        browser.Selected!.Name.ShouldBe("grid.png");
        scrolled.ShouldBeSameAs(browser.Selected);
    }

    [Fact]
    public async Task Navigating_clears_the_query_so_the_path_is_not_a_lie()
    {
        ContentBrowserModel browser = await BrowserAsync();
        browser.Query = "brick";

        browser.NavigateTo(Path.Combine(_root, "Models"));

        browser.Query.ShouldBe("");
        browser.IsSearching.ShouldBeFalse();
        NamesOf(browser).ShouldContain("crate.obj");
    }
}
