using Microsoft.Extensions.Logging.Abstractions;
using SpectraEngine.Core.Assets;
using SpectraEngine.Editor.Shell;
using System.IO;
using System.Threading.Tasks;
using System.Linq;

namespace SpectraEngine.Editor.Tests;

/// <summary>
/// What an asset drag carries, and what a drop does with it.
/// </summary>
/// <remarks>
/// <b>The gesture cannot be tested and both of its decisions can.</b> A drag is
/// a pointer, a compositor and an OLE session; what is actually capable of being
/// wrong is the conversion from a browsed filesystem path to the engine's own
/// content-relative identity, and the rule about which drops can be honoured.
/// Both are pure functions, so both are here rather than left as reasoning.
/// </remarks>
public sealed class ContentDragTests
{
    private static string Root => Path.Combine(Path.GetTempPath(), "spectra-assets");

    private static string Under(params string[] parts) =>
        Path.Combine([Root, .. parts]);

    // --- The payload ---------------------------------------------------------

    [Fact]
    public void A_dragged_file_carries_the_path_the_engine_names_it_by()
    {
        // The identity every other layer uses: forward slashes, relative to the
        // content root, no separator in front. Not the filesystem path - a
        // payload spelling identity a fifth way resolves nothing at the drop
        // while every log line reads healthy, because the path it names really
        // does exist.
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
        // Stated as an equality against the engine's own normalizer rather than
        // against a literal, because the claim is not "this string" but "the
        // same string the asset caches, the map codec and the pack's id hash
        // all key on". A second normalizer here would be free to drift.
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
        // The browser's root is null until a project opens, and a relative path
        // against nothing is not a thing that can be computed. Refused at the
        // source, so no drag ever starts carrying one.
        ContentDragPayload.TryCreate(null, Under("Models", "crate.obj"), ContentKind.Model, out _)
            .ShouldBeFalse();
    }

    [Fact]
    public void A_file_outside_the_content_root_is_refused_rather_than_carried()
    {
        // GetRelativePath answers with '..' segments, which
        // NormalizeRelativePath refuses because a content reference must stay
        // inside the root. Caught here so a mis-rooted browser declines the drag
        // instead of handing the render thread a path it will reject three
        // threads later, with the user watching an empty node appear.
        string outside = Path.Combine(Path.GetTempPath(), "somewhere-else", "prop.obj");

        ContentDragPayload.TryCreate(Root, outside, ContentKind.Model, out _)
            .ShouldBeFalse();
    }

    // --- The drop decision ---------------------------------------------------

    [Fact]
    public void A_model_dropped_into_a_composited_viewport_with_a_session_is_placed()
    {
        AssetDropPolicy.Refuse(Model(), hasSession: true, viewportAcceptsDrops: true)
            .ShouldBeNull();
    }

    [Fact]
    public void A_native_viewport_refuses_the_drop_IN_WORDS_and_names_the_way_out()
    {
        // The whole reason the capability is asked for rather than assumed. The
        // two viewports render an identical picture, so a drop that silently
        // does nothing over one of them is indistinguishable from a broken
        // drag - and the fix is a command-line switch nobody would guess at.
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
        // Order follows what the user can act on: with no project open, nothing
        // else is true yet, and telling somebody their viewport cannot take a
        // texture is a fact about a session that does not exist.
        var texture = new ContentDragPayload(ContentKind.Texture, "Textures/x.png", "x.png");

        string? refusal = AssetDropPolicy.Refuse(
            texture, hasSession: false, viewportAcceptsDrops: false);

        refusal.ShouldNotBeNullOrWhiteSpace();
        refusal.ShouldContain("project");
    }

    [Fact]
    public void A_model_and_a_material_can_both_be_placed()
    {
        // Two kinds behind one predicate, and two different gestures behind
        // that: a model becomes a node at the point under the pointer, a
        // material becomes the surface of the face under it. They share this
        // answer because the only question here is whether letting go would do
        // anything, which the overlay and the drop both have to agree about.
        AssetDropPolicy.CanPlace(ContentKind.Model).ShouldBeTrue();
        AssetDropPolicy.CanPlace(ContentKind.Material).ShouldBeTrue();
    }

    [Fact]
    public void Everything_else_is_refused_with_a_sentence_naming_the_file()
    {
        // Answered with a sentence rather than with a cursor, because the "no
        // entry" pointer says the shell did not understand the gesture when in
        // fact it understood it perfectly.
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
        // The near miss: somebody dragging a .png onto a wall is asking for
        // exactly what a material drop does, and a refusal reading "only models
        // and materials can be dropped" tells them nothing about which of the
        // two files in front of them is which.
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
/// Selecting a file in the content browser, and what the strip then says about
/// it.
/// </summary>
/// <remarks>
/// <b>Double-clicking a model used to say placement was not built and open a
/// folder in Explorer.</b> That had stopped being true the moment a drag could
/// drop one into the viewport; the placement verb takes an optional pixel and a
/// null one means the centre of the view, so a double-click needs nothing the
/// drag needs. Everything that is not a model selects instead, which is what
/// this covers.
/// </remarks>
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

    // The listing comes off an index walked in the background, so a test that
    // read Entries straight after SetRoot would be reading the empty list that
    // fills the pane while the walk runs. Awaiting is what the panel does not
    // have to do, because the index publishes to it when the walk lands.
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

            // The CONTENT-relative path, because that is the name this file has
            // as far as a material, a map or a pack id is concerned. An
            // absolute path is a fact about this machine.
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
        // The entry it named has left the list, and a strip describing a file
        // nobody can see is worse than an empty one.
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
