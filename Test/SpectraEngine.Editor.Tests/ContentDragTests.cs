using Microsoft.Extensions.Logging.Abstractions;
using SpectraEngine.Core.Assets;
using SpectraEngine.Editor.Shell;
using System.IO;
using System.Threading.Tasks;
using System.Linq;

namespace SpectraEngine.Editor.Tests;

/// <summary>What an asset drag carries, and which drops are honoured.</summary>
public sealed class ContentDragTests
{
    private static string Root => Path.Combine(Path.GetTempPath(), "spectra-assets");

    private static string Under(params string[] parts) =>
        Path.Combine([Root, .. parts]);

    [Fact]
    public void A_dragged_file_carries_the_path_the_engine_names_it_by()
    {
        ContentDragPayload.TryCreate(
            Root, Under("Models", "crate.obj"), ContentKind.Model, out ContentDragPayload? payload)
            .ShouldBeTrue();

        payload.ShouldNotBeNull();
        payload.ContentPath.ShouldBe("Models/crate.obj");
        payload.Kind.ShouldBe(ContentKind.Model);
        payload.Name.ShouldBe("crate.obj");
    }

    [Fact]
    public void The_carried_path_is_the_one_ContentRoot_would_normalize_to()
    {
        ContentDragPayload.TryCreate(
            Root, Under("Textures", "Props", "wall_brick.png"), ContentKind.Texture,
            out ContentDragPayload? payload).ShouldBeTrue();

        payload!.ContentPath.ShouldBe(
            ContentRoot.NormalizeRelativePath(Path.Combine("Textures", "Props", "wall_brick.png")));
    }

    [Fact]
    public void A_folder_is_not_an_asset()
    {
        ContentDragPayload.TryCreate(Root, Under("Models"), ContentKind.Folder, out _)
            .ShouldBeFalse();
    }

    [Fact]
    public void A_browser_with_no_project_open_produces_no_payload()
    {
        ContentDragPayload.TryCreate(null, Under("Models", "crate.obj"), ContentKind.Model, out _)
            .ShouldBeFalse();
    }

    [Fact]
    public void A_file_outside_the_content_root_is_refused_rather_than_carried()
    {
        string outside = Path.Combine(Path.GetTempPath(), "somewhere-else", "prop.obj");

        ContentDragPayload.TryCreate(Root, outside, ContentKind.Model, out _)
            .ShouldBeFalse();
    }

    [Fact]
    public void A_model_dropped_into_a_composited_viewport_with_a_session_is_placed()
    {
        AssetDropPolicy.Refuse(Model(), hasSession: true, viewportAcceptsDrops: true)
            .ShouldBeNull();
    }

    [Fact]
    public void A_native_viewport_refuses_the_drop_IN_WORDS_and_names_the_way_out()
    {
        string? refusal = AssetDropPolicy.Refuse(
            Model(), hasSession: true, viewportAcceptsDrops: false);

        refusal.ShouldNotBeNullOrWhiteSpace();
        refusal.ShouldContain("--viewport=composition");
    }

    [Fact]
    public void A_drop_with_no_session_says_to_open_a_project_first()
    {
        string? refusal = AssetDropPolicy.Refuse(
            Model(), hasSession: false, viewportAcceptsDrops: true);

        refusal.ShouldNotBeNullOrWhiteSpace();
        refusal.ShouldContain("project");
    }

    [Fact]
    public void The_missing_session_outranks_the_viewport_and_the_kind()
    {
        var texture = new ContentDragPayload(ContentKind.Texture, "Textures/x.png", "x.png");

        string? refusal = AssetDropPolicy.Refuse(
            texture, hasSession: false, viewportAcceptsDrops: false);

        refusal.ShouldNotBeNullOrWhiteSpace();
        refusal.ShouldContain("project");
    }

    [Fact]
    public void A_model_and_a_material_can_both_be_placed()
    {
        AssetDropPolicy.CanPlace(ContentKind.Model).ShouldBeTrue();
        AssetDropPolicy.CanPlace(ContentKind.Material).ShouldBeTrue();
    }

    [Fact]
    public void Everything_else_is_refused_with_a_sentence_naming_the_file()
    {
        foreach (ContentKind kind in new[]
        {
            ContentKind.Texture, ContentKind.Shader, ContentKind.Other,
        })
        {
            AssetDropPolicy.CanPlace(kind).ShouldBeFalse();

            var payload = new ContentDragPayload(kind, "Assets/thing.dat", "thing.dat");
            string? refusal = AssetDropPolicy.Refuse(
                payload, hasSession: true, viewportAcceptsDrops: true);

            refusal.ShouldNotBeNullOrWhiteSpace();
            refusal.ShouldContain("thing.dat");
        }

        AssetDropPolicy.CanPlace(ContentKind.Folder).ShouldBeFalse();
    }

    [Fact]
    public void A_texture_is_refused_by_naming_the_material_to_drop_instead()
    {
        var texture = new ContentDragPayload(ContentKind.Texture, "Textures/brick.png", "brick.png");

        string? refusal = AssetDropPolicy.Refuse(
            texture, hasSession: true, viewportAcceptsDrops: true);

        refusal.ShouldNotBeNull();
        refusal.ShouldContain("brick.png");
        refusal.ShouldContain("material");
    }

    private static ContentDragPayload Model() =>
        new(ContentKind.Model, "Models/crate.obj", "crate.obj");
}

/// <summary>
/// Selecting a file in the content browser, and what the strip says about it.
/// </summary>
public sealed class ContentSelectionTests
{
    private static (ContentBrowserModel Browser, string Root) Rig()
    {
        string root = Path.Combine(Path.GetTempPath(), "SpectraContentTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "Materials"));
        File.WriteAllText(Path.Combine(root, "Materials", "wall.spectramat"), "shader = lit");

        var browser = new ContentBrowserModel(NullLogger.Instance);
        browser.SetRoot(root);
        return (browser, root);
    }

    // The index walks in the background, so Entries is empty until it lands.
    private static async Task<(ContentBrowserModel Browser, string Root)> WalkedRig()
    {
        (ContentBrowserModel browser, string root) = Rig();
        await browser.Index.Walking;
        browser.NavigateTo(root);
        return (browser, root);
    }

    [Fact]
    public void Nothing_is_selected_until_something_is()
    {
        (ContentBrowserModel browser, string root) = Rig();
        try
        {
            Assert.False(browser.HasSelected);
            Assert.Equal(string.Empty, browser.SelectedDetails);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task The_strip_names_the_file_the_way_the_engine_does()
    {
        (ContentBrowserModel browser, string root) = await WalkedRig();
        try
        {
            browser.Open(browser.Entries.Single(e => e.IsFolder));
            ContentEntry material = browser.Entries.Single(e => e.Kind == ContentKind.Material);

            browser.Select(material);

            Assert.True(browser.HasSelected);
            Assert.True(material.IsSelected);

            Assert.Contains("Materials/wall.spectramat", browser.SelectedDetails);
            Assert.Contains("material", browser.SelectedDetails);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task Selecting_another_entry_deselects_the_first()
    {
        (ContentBrowserModel browser, string root) = await WalkedRig();
        try
        {
            File.WriteAllText(Path.Combine(root, "Materials", "floor.spectramat"), "shader = lit");
            browser.Refresh();
            await browser.Index.Walking;
            browser.Open(browser.Entries.Single(e => e.IsFolder));

            ContentEntry first = browser.Entries[0];
            ContentEntry second = browser.Entries[1];

            browser.Select(first);
            browser.Select(second);

            Assert.False(first.IsSelected);
            Assert.True(second.IsSelected);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task Navigating_away_clears_the_selection()
    {
        (ContentBrowserModel browser, string root) = await WalkedRig();
        try
        {
            ContentEntry folder = browser.Entries.Single(e => e.IsFolder);
            browser.Open(folder);
            browser.Select(browser.Entries[0]);
            Assert.True(browser.HasSelected);

            browser.GoUp();

            Assert.False(browser.HasSelected);
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
